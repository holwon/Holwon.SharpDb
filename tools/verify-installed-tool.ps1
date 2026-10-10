# Verifies the *installed* SharpDb tool the way the README tells a user to use it: install the
# package, point the client's configuration at the `sharpdb-mcp` shim, then drive the four tools
# over real stdio. Nothing here reads SharpDb's build output, and both databases are made from
# scratch — a throwaway SQLite file and, when Docker is reachable, a throwaway MySQL container. The
# subject is the command a user ends up with.
#
# Why this is not a test project: every test in this repository runs the host *it* built. The
# distribution claim is about the packaged tool — "one install serves MySQL and SQLite" — and only a
# real install can exercise it. This is the ticket's acceptance harness.
#
# Usage:
#   dotnet tool install --global Holwon.SharpDb.Mcp --add-source artifacts --version 0.1.0
#   pwsh -File tools/verify-installed-tool.ps1
#
# Switches:
#   -SkipMySql                  verify SQLite only (e.g. a machine without Docker)
#   -MySqlConnectionString ...  verify an existing MySQL instead of provisioning one; it must
#                               already hold the fixture in tools/seed-verification-mysql.sql
#   -LogLevel X                 also print the stderr the tool produced at that level
#
# Exit code 0 when every check passes (a skip does not fail), 1 otherwise.

[CmdletBinding()]
param(
    [string] $LogLevel = '',
    [switch] $SkipMySql,
    [string] $MySqlConnectionString = ''
)

$ErrorActionPreference = 'Stop'

$shim = (Get-Command sharpdb-mcp -ErrorAction SilentlyContinue)?.Source
if (-not $shim) {
    throw 'sharpdb-mcp is not on PATH. Install the tool first (see this file''s header).'
}

$workspace = Join-Path $PWD 'artifacts'
New-Item -ItemType Directory -Force -Path $workspace | Out-Null

# --- SQLite: seeded by a file-based C# app, so the harness leans on nothing the package ships ----
$sqlitePath = Join-Path $workspace 'verify.db'
$sqlitePath = (dotnet run (Join-Path $PWD 'tools/seed-verification-database.cs') -- $sqlitePath |
        Select-Object -Last 1).Trim()

# --- MySQL: a throwaway container, seeded with the same schema and a SELECT-only account --------
$containerName = 'sharpdb-verify-mysql'
$provisionedContainer = $false

if (-not $SkipMySql -and -not $MySqlConnectionString) {
    $docker = Get-Command docker -ErrorAction SilentlyContinue
    if ($docker) {
        Write-Host 'Starting a throwaway MySQL container...'

        # Seeding needs DDL, so the fixture is built with the root account; the tool is then pointed
        # at a SELECT-only account, which is what the README tells a user to create.
        #
        # `Out-String` is avoided and `$LASTEXITCODE` is read immediately: piping docker's output
        # through a cmdlet replaces the exit code with the cmdlet's, which would make a failed start
        # look like a success.
        $runOutput = docker run -d --name $containerName `
            -e MYSQL_ROOT_PASSWORD=rootpass -e MYSQL_DATABASE=app -p 13306:3306 mysql:8.0 2>&1
        $runExitCode = $LASTEXITCODE

        if ($runExitCode -eq 0) {
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
            # account SQL it contains. The exit code is read before the output is filtered, for the
            # same reason as above.
            $seedPath = Join-Path $PWD 'tools/seed-verification-mysql.sql'
            $seedPath = (Resolve-Path $seedPath).Path
            $seedOutput = Get-Content $seedPath -Raw | docker exec -i $containerName mysql -uroot -prootpass 2>&1
            $seedExitCode = $LASTEXITCODE
            if ($seedExitCode -ne 0) {
                throw "Seeding MySQL failed: $($seedOutput | Where-Object { $_ -notmatch 'Using a password' })"
            }

            $MySqlConnectionString = 'Server=127.0.0.1;Port=13306;Database=app;User ID=sharpdb_ro;' +
                'Password=a-strong-password;SslMode=None'
        }
        else {
            Write-Host "Docker could not start a container, so MySQL is skipped: $runOutput"
        }
    }
    else {
        Write-Host 'Docker is not installed, so MySQL is skipped.'
    }
}

# --- The MCP stdio framing: one JSON-RPC message per line --------------------
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

$failures = 0
function Assert-That {
    param([bool] $Condition, [string] $What)
    if ($Condition) { Write-Host "  ok   $What" }
    else { Write-Host "  FAIL $What"; $script:failures++ }
}

# --- Drive one engine through the installed tool -----------------------------
# Each engine gets its own process, because a process is bound to one database: that is also the
# README's "more than one database means more than one server entry", exercised rather than quoted.
function Test-Engine([string] $Type, [string] $ConnectionString) {
    Write-Host "== $Type =="

    $env:SHARPDB__TYPE = $Type
    $env:SHARPDB__CONNECTIONSTRING = $ConnectionString
    Remove-Item Env:\Logging__LogLevel__Default -ErrorAction SilentlyContinue
    if ($LogLevel) { $env:Logging__LogLevel__Default = $LogLevel }

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $shim
    $startInfo.RedirectStandardInput = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.UseShellExecute = $false

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    $null = $process.Start()

    $toServer = $process.StandardInput
    $fromServer = $process.StandardOutput

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
        try { $process.StandardInput.Close() } catch { }
        if (-not $process.WaitForExit(30000)) { $process.Kill($true) }
        $script:lastStderr = $process.StandardError.ReadToEnd()
        $process.Dispose()
    }
}

$engines = [ordered]@{ sqlite = "Data Source=$sqlitePath" }
if ($MySqlConnectionString) {
    $engines['mysql'] = $MySqlConnectionString
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
}
finally {
    Remove-Item Env:\SHARPDB__TYPE, Env:\SHARPDB__CONNECTIONSTRING -ErrorAction SilentlyContinue
    Remove-Item Env:\Logging__LogLevel__Default -ErrorAction SilentlyContinue

    # The container is the harness's own fixture, so it goes however the run ended.
    if ($provisionedContainer) {
        docker rm -f $containerName 2>&1 | Out-Null
    }
}

if ($LogLevel -and $lastStderr) {
    Write-Host "=== stderr at $LogLevel ==="
    Write-Host $lastStderr
}

if ($failures -eq 0) {
    Write-Host 'All checks passed.'
    exit 0
}
Write-Host "$failures check(s) failed."
Write-Host '=== stderr ==='
Write-Host $lastStderr
exit 1
