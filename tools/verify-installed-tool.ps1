# Accepts implementation ticket 08 the way the ticket defines it: install the package, point the
# client's configuration at the `sharpdb-mcp` shim, then drive the four tools over real stdio.
#
# Why this is not a test project: every test in this repository runs the host *it* just built. The
# distribution claim is about the *packaged* tool — "one install serves MySQL and SQLite" — and only
# a real install can exercise that. So this harness reads nothing from SharpDb's build output: both
# databases are made from scratch, and it speaks to the shim a user ends up with.
#
# Full run from a clean tree (this is the whole recipe; nothing here is implicit):
#   dotnet build SharpDb.slnx -c Release
#   dotnet pack src/SharpDb/SharpDb.csproj -c Release -o artifacts
#   dotnet tool install --global Holwon.SharpDb.Mcp --add-source artifacts --version 0.2.1
#   pwsh -File tools/verify-installed-tool.ps1
#
# The version above is the one `<Version>` in src/SharpDb/SharpDb.csproj carries: the install has to
# name the same number the pack produced, and publish.yml refuses to release when a tag and the
# project disagree, so a stale number here is a recipe that cannot run.
#
# Switches:
#   -SkipMySql    verify without asking for Docker at all. Use this to say "I am not
#                 verifying MySQL today"; it is the only way MySQL is skipped.
#   -SkipPostgreSql  the same for PostgreSQL.
#   -LogLevel X   also print the stderr the tool produced at that level.
#
# MySQL and PostgreSQL are verified whenever Docker is present: Docker being there but unable to
# produce a container is a FAILURE, not a skip. "Silently not running" is the one outcome a
# verification harness must never produce, because it turns a broken environment into a green run.
#
# Exit code 0 when every check passed, 1 otherwise.

[CmdletBinding()]
param(
    [string] $LogLevel = '',
    [switch] $SkipMySql,
    [switch] $SkipPostgreSql
)

$ErrorActionPreference = 'Stop'

$shim = (Get-Command sharpdb-mcp -ErrorAction SilentlyContinue)?.Source
if (-not $shim) {
    throw 'sharpdb-mcp is not on PATH. Install the tool first (see this file''s header).'
}

$workspace = Join-Path $PWD 'artifacts'
New-Item -ItemType Directory -Force -Path $workspace | Out-Null
$containerName = 'sharpdb-verify-mysql'
$postgresContainerName = 'sharpdb-verify-postgresql'
$provisionedContainer = $false
$provisionedPostgres = $false

$failures = 0
function Assert-That {
    param([bool] $Condition, [string] $What)
    if ($Condition) { Write-Host "  ok   $What" }
    else { Write-Host "  FAIL $What"; $script:failures++ }
}

# --- Child-process plumbing ---------------------------------------------------
# The configuration is handed to the child only. Nothing here touches this process's own
# environment: a harness that mutates it leaks into whatever runs next, and a run that leaves a
# SHARPDB__ variable behind changes the behaviour of the next one.

function Send-Message {
    param([System.IO.StreamWriter] $ToServer, [hashtable] $Message)
    $ToServer.WriteLine(($Message | ConvertTo-Json -Depth 20 -Compress))
    $ToServer.Flush()
}

function Receive-Until {
    param([System.IO.StreamReader] $FromServer, [int] $Id, [int] $TimeoutSeconds = 60)
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        $line = $FromServer.ReadLine()
        if ($null -eq $line) { throw 'The server closed its output before answering.' }
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        $message = $line | ConvertFrom-Json
        if ($message.PSObject.Properties.Name -contains 'id' -and $message.id -eq $Id) {
            return $message
        }
    }
    throw "No reply to request $Id within $TimeoutSeconds seconds."
}

function Invoke-Tool {
    param(
        [System.IO.StreamWriter] $ToServer,
        [System.IO.StreamReader] $FromServer,
        [int] $Id,
        [string] $Name,
        [hashtable] $Arguments
    )
    Send-Message $ToServer @{
        jsonrpc = '2.0'; id = $Id; method = 'tools/call'
        params  = @{ name = $Name; arguments = $Arguments }
    }
    return Receive-Until $FromServer $Id
}

# Starts the installed shim with exactly the given `env` block, which is the only configuration
# channel a client has (ADR 0008). Returns the process plus the readers a test drives it through.
function Start-Shim([hashtable] $Environment) {
    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $shim
    $startInfo.RedirectStandardInput = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.UseShellExecute = $false

    # The child inherits this process's environment, so the logging level has to be removed rather
    # than merely not set: a developer with Logging__LogLevel__Default in their own environment
    # would otherwise change what the logging checks observe.
    $startInfo.Environment.Remove('Logging__LogLevel__Default') | Out-Null
    if ($LogLevel) { $startInfo.Environment['Logging__LogLevel__Default'] = $LogLevel }

    foreach ($key in $Environment.Keys) { $startInfo.Environment[$key] = $Environment[$key] }

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    $null = $process.Start()

    return @{
        Process = $process
        ToServer = $process.StandardInput
        FromServer = $process.StandardOutput
    }
}

function Stop-Shim($ShimProcess) {
    try { $ShimProcess.Process.StandardInput.Close() } catch { }
    if (-not $ShimProcess.Process.WaitForExit(30000)) { $ShimProcess.Process.Kill($true) }
    $stderr = $ShimProcess.Process.StandardError.ReadToEnd()
    $exitCode = $ShimProcess.Process.ExitCode
    $ShimProcess.Process.Dispose()
    return @{ Stderr = $stderr; ExitCode = $exitCode }
}

# Runs the installed shim with the given `env` block and no protocol traffic at all, which is what
# a startup failure looks like from the outside: a process that exits on its own.
function Start-And-Fail([hashtable] $Environment) {
    $started = Start-Shim $Environment
    $started.Process.StandardInput.Close()
    if (-not $started.Process.WaitForExit(30000)) {
        $started.Process.Kill($true)
        throw 'The host did not exit on its own, so it did not fail fast.'
    }
    $stderr = $started.Process.StandardError.ReadToEnd()
    $exitCode = $started.Process.ExitCode
    $started.Process.Dispose()
    return @{ Stderr = $stderr.Trim(); ExitCode = $exitCode }
}

# --- Fixtures -----------------------------------------------------------------
# SQLite is seeded by a small sibling project rather than by SharpDb's build output. It targets
# net8.0 so the harness needs no newer SDK than the README says the tool needs.
#
# It is built and run directly rather than through `dotnet run --project`: `dotnet run` adds the
# project to the solution file as a side effect, which would leave the repository modified by
# merely running a verification.
Write-Host 'Seeding the SQLite fixture...'
$seedProject = Join-Path $PWD 'tools/SeedVerificationDatabase'
$sqlitePath = Join-Path $workspace 'verify.db'

dotnet build $seedProject -c Release --nologo 2>&1 | Out-File -Encoding utf8 (Join-Path $workspace 'seed-build.log')
if ($LASTEXITCODE -ne 0) { throw "Building the SQLite fixture failed; see $workspace/seed-build.log" }

$seedAssembly = Join-Path $seedProject 'bin/Release/net8.0/SeedVerificationDatabase.dll'
$seedPath = (dotnet $seedAssembly $sqlitePath | Select-Object -Last 1).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Seeding SQLite failed.' }

if (-not $SkipMySql) {
    $docker = Get-Command docker -ErrorAction SilentlyContinue
    if (-not $docker) {
        # No Docker at all is a machine that cannot verify MySQL, and saying so is the honest
        # outcome. Docker being present but broken is handled below as a failure.
        Write-Host 'MySQL is NOT verified: Docker is not installed.'
    }
    else {
        Write-Host 'Starting a throwaway MySQL container...'

        # Seeding needs DDL, so the fixture is built with the root account; the tool is then pointed
        # at a SELECT-only account, which is what the README tells a user to create.
        #
        # `$LASTEXITCODE` is read on the line straight after docker runs: piping docker's output
        # through a cmdlet would replace the exit code with the cmdlet's and make a failed start
        # look like a success.
        $runOutput = docker run -d --name $containerName `
            -e MYSQL_ROOT_PASSWORD=rootpass -e MYSQL_DATABASE=app -p 13306:3306 mysql:8.0 2>&1
        $runExitCode = $LASTEXITCODE
        if ($runExitCode -ne 0) {
            throw "Docker could not start the MySQL container. MySQL was NOT verified: $runOutput"
        }

        $provisionedContainer = $true
        $ready = $false
        $deadline = (Get-Date).AddSeconds(180)
        while (-not $ready -and (Get-Date) -lt $deadline) {
            Start-Sleep -Seconds 3
            docker exec $containerName mysqladmin ping -h 127.0.0.1 -uroot -prootpass --silent 2>&1 | Out-Null
            $ready = $LASTEXITCODE -eq 0
        }
        if (-not $ready) { throw 'The MySQL container did not become ready within 180 seconds.' }

        # The fixture is fed to mysql's stdin as one script, so no quoting layer can alter the
        # account SQL it contains. The exit code is read before the output is filtered.
        $seedSql = (Resolve-Path (Join-Path $PWD 'tools/seed-verification-mysql.sql')).Path
        $seedOutput = Get-Content $seedSql -Raw | docker exec -i $containerName mysql -uroot -prootpass 2>&1
        $seedExitCode = $LASTEXITCODE
        if ($seedExitCode -ne 0) {
            throw "Seeding MySQL failed: $($seedOutput | Where-Object { $_ -notmatch 'Using a password' })"
        }

        $MySqlConnectionString = 'Server=127.0.0.1;Port=13306;Database=app;User ID=sharpdb_ro;' +
            'Password=a-strong-password;SslMode=None'
    }
}

if (-not $SkipPostgreSql) {
    $docker = Get-Command docker -ErrorAction SilentlyContinue
    if (-not $docker) {
        Write-Host 'PostgreSQL is NOT verified: Docker is not installed.'
    }
    else {
        Write-Host 'Starting a throwaway PostgreSQL container...'

        $runOutput = docker run -d --name $postgresContainerName `
            -e POSTGRES_PASSWORD=rootpass -e POSTGRES_DB=app -p 15432:5432 postgres:16 2>&1
        $runExitCode = $LASTEXITCODE
        if ($runExitCode -ne 0) {
            throw "Docker could not start the PostgreSQL container. PostgreSQL was NOT verified: $runOutput"
        }

        $provisionedPostgres = $true
        $ready = $false
        $deadline = (Get-Date).AddSeconds(180)
        while (-not $ready -and (Get-Date) -lt $deadline) {
            Start-Sleep -Seconds 3
            docker exec $postgresContainerName pg_isready -U postgres 2>&1 | Out-Null
            $ready = $LASTEXITCODE -eq 0
        }
        if (-not $ready) { throw 'The PostgreSQL container did not become ready within 180 seconds.' }

        # Seeding needs DDL, so the fixture is built with the superuser; the tool is then pointed at
        # a SELECT-only account, which is what the README tells a user to create.
        #
        # `ON_ERROR_STOP=1` is what makes the exit-code check below mean anything. Measured: without
        # it psql reports an error on stderr for every failing statement and still exits 0, so a seed
        # that died on its first CREATE would leave the container half-built and the harness would
        # carry on and test the wrong thing. mysql exits non-zero by itself, which is why only this
        # invocation needs the flag.
        $seedSql = (Resolve-Path (Join-Path $PWD 'tools/seed-verification-postgresql.sql')).Path
        $seedOutput = Get-Content $seedSql -Raw |
            docker exec -i $postgresContainerName psql -v ON_ERROR_STOP=1 -U postgres -d app 2>&1
        $seedExitCode = $LASTEXITCODE
        if ($seedExitCode -ne 0) {
            throw "Seeding PostgreSQL failed: $seedOutput"
        }

        $PostgreSqlConnectionString = 'Host=127.0.0.1;Port=15432;Database=app;Username=sharpdb_ro;' +
            'Password=a-strong-password'
    }
}

# --- Drive one engine through the installed tool ------------------------------
# Each engine gets its own process, because a process is bound to one database: that is also the
# README's "more than one database means more than one server entry", exercised rather than quoted.
function Test-Engine([string] $Type, [string] $ConnectionString) {
    Write-Host "== $Type =="

    $started = Start-Shim @{ SHARPDB__TYPE = $Type; SHARPDB__CONNECTIONSTRING = $ConnectionString }
    $toServer = $started.ToServer
    $fromServer = $started.FromServer

    try {
        # --- handshake ------------------------------------------------------
        Send-Message $toServer @{
            jsonrpc = '2.0'; id = 1; method = 'initialize'
            params  = @{
                protocolVersion = '2025-06-18'
                capabilities    = @{}
                clientInfo      = @{ name = 'verify-installed-tool'; version = '1.0.0' }
            }
        }
        $initialized = Receive-Until $fromServer 1
        Assert-That ($null -ne $initialized.result.serverInfo) '[protocol] initialize returns serverInfo'
        Send-Message $toServer @{ jsonrpc = '2.0'; method = 'notifications/initialized' }

        # --- the four tools, with the annotations a client relies on --------
        Send-Message $toServer @{ jsonrpc = '2.0'; id = 2; method = 'tools/list' }
        $listed = Receive-Until $fromServer 2
        $tools = $listed.result.tools
        $names = ($tools | ForEach-Object { $_.name } | Sort-Object)
        Assert-That (($names -join ',') -eq 'describe_table,execute_query,list_databases,list_tables') `
            "[protocol] tools/list is the four tools (got: $($names -join ','))"

        $query = $tools | Where-Object { $_.name -eq 'execute_query' }
        Assert-That ($query.annotations.readOnlyHint -eq $true) '[annotations] execute_query is read-only'
        Assert-That ($query.annotations.destructiveHint -eq $false) '[annotations] execute_query is not destructive'
        Assert-That ($query.annotations.idempotentHint -eq $true) '[annotations] execute_query is idempotent'
        Assert-That ($query.annotations.openWorldHint -eq $false) '[annotations] execute_query is closed-world'

        # --- list_tables ----------------------------------------------------
        $tables = Invoke-Tool $toServer $fromServer 3 'list_tables' @{}
        Assert-That ($tables.result.isError -ne $true) 'list_tables succeeds'
        $tablesText = $tables.result.content[0].text
        Write-Host "       -> $tablesText"
        Assert-That ($tablesText -match 'users') 'list_tables names the users table'
        Assert-That ($tablesText -match '"type":"view"') 'list_tables reports the view as a view'

        # --- describe_table -------------------------------------------------
        $described = Invoke-Tool $toServer $fromServer 4 'describe_table' @{ table = 'orders' }
        Assert-That ($described.result.isError -ne $true) 'describe_table succeeds'
        $describedText = $described.result.content[0].text
        Assert-That ($describedText -match 'primaryKey') 'describe_table reports the primary key'
        Assert-That ($describedText -match 'foreignKeys') 'describe_table reports the foreign key'
        Assert-That ($describedText -match 'indexes') 'describe_table reports the index'

        # --- list_databases -------------------------------------------------
        $databases = Invoke-Tool $toServer $fromServer 5 'list_databases' @{}
        Assert-That ($databases.result.isError -ne $true) 'list_databases succeeds'
        Write-Host "       -> $($databases.result.content[0].text)"

        # --- execute_query: a real read -------------------------------------
        $queried = Invoke-Tool $toServer $fromServer 6 'execute_query' @{ sql = 'SELECT COUNT(*) AS n FROM users' }
        Assert-That ($queried.result.isError -ne $true) 'execute_query succeeds'
        Assert-That ($queried.result.content[0].text -eq '{"columns":["n"],"rows":[[2]]}') `
            'execute_query returns the result set'
        Write-Host "       -> $($queried.result.content[0].text)"

        # --- execute_query: a write is refused as a result, not an exception -
        $refused = Invoke-Tool $toServer $fromServer 7 'execute_query' @{ sql = 'DELETE FROM users' }
        Assert-That ($refused.result.isError -eq $true) 'a write is refused as a failed result'
        Assert-That ($refused.result.content[0].text -match 'Statement rejected') 'the refusal explains itself'
    }
    finally {
        $script:lastStderr = (Stop-Shim $started).Stderr
    }
}

# --- The README's failure table, checked against the tool ---------------------
# The README quotes these lines verbatim, so they are asserted rather than trusted: a quoted message
# that no longer matches sends a user looking for text that does not appear. Each case pairs the
# documented line with the environment that must produce it.
function Test-StartupFailures {
    Write-Host '== startup failures (the README table) =='

    $cases = @(
        @{
            Documented = 'sharpdb: SHARPDB__TYPE is required (expected "mysql" or "postgresql" or "sqlite").'
            Because    = 'the type is missing'
            Env        = @{ SHARPDB__CONNECTIONSTRING = 'Data Source=unused.db' }
        },
        @{
            Documented = 'sharpdb: SHARPDB__TYPE ''postgres'' is unknown (expected "mysql" or "postgresql" or "sqlite").'
            Because    = 'the type is unknown'
            Env        = @{ SHARPDB__TYPE = 'postgres'; SHARPDB__CONNECTIONSTRING = 'Data Source=unused.db' }
        },
        @{
            Documented = 'sharpdb: SHARPDB__CONNECTIONSTRING is required and must not be blank.'
            Because    = 'the connection string is missing'
            Env        = @{ SHARPDB__TYPE = 'sqlite' }
        },
        @{
            Documented = 'sharpdb: Could not connect to the mysql database:'
            Because    = 'the mysql connection fails'
            Env        = @{
                SHARPDB__TYPE = 'mysql'
                SHARPDB__CONNECTIONSTRING = 'Server=127.0.0.1;Port=1;Database=nope;User ID=nobody;SslMode=None'
                SHARPDB__TIMEOUTSECONDS = '3'
            }
            Prefix     = $true
        },
        @{
            Documented = "sharpdb: Invalid configuration: Failed to convert configuration value 'plenty' at 'SHARPDB:MaxRows' to type 'System.Int32'."
            Because    = 'a row limit is not a number'
            Env        = @{
                SHARPDB__TYPE = 'sqlite'
                SHARPDB__CONNECTIONSTRING = 'Data Source=unused.db'
                SHARPDB__MAXROWS = 'plenty'
            }
        }
    )

    foreach ($case in $cases) {
        $result = Start-And-Fail $case.Env
        $matched = if ($case.Prefix) {
            $result.Stderr.StartsWith($case.Documented)
        }
        else {
            $result.Stderr -eq $case.Documented
        }

        Assert-That $matched "the README's line for '$($case.Because)' is what the tool prints"
        if (-not $matched) { Write-Host "       expected: $($case.Documented)" ; Write-Host "       actual:   $($result.Stderr)" }
        Assert-That ($result.ExitCode -eq 1) "'$($case.Because)' exits 1"
    }
}

# --- Run ----------------------------------------------------------------------
$engines = [ordered]@{ sqlite = "Data Source=$sqlitePath" }
if ($MySqlConnectionString) {
    $engines['mysql'] = $MySqlConnectionString
}
if ($PostgreSqlConnectionString) {
    $engines['postgresql'] = $PostgreSqlConnectionString
}

try {
    Write-Host "Shim: $shim"
    foreach ($engine in $engines.Keys) {
        Write-Host "  $engine -> $($engines[$engine])"
    }
    Write-Host ''

    foreach ($engine in $engines.Keys) {
        Test-Engine $engine $engines[$engine]
        Write-Host ''
    }

    Test-StartupFailures
}
finally {
    # The containers are the harness's own fixtures, so they go however the run ended.
    if ($provisionedContainer) {
        docker rm -f $containerName 2>&1 | Out-Null
    }
    if ($provisionedPostgres) {
        docker rm -f $postgresContainerName 2>&1 | Out-Null
    }
}

Write-Host ''
if ($LogLevel -and $lastStderr) {
    Write-Host "=== stderr at $LogLevel ==="
    Write-Host $lastStderr
}

if ($failures -eq 0) {
    Write-Host 'All checks passed.'
    exit 0
}
Write-Host "$failures check(s) failed."
exit 1
