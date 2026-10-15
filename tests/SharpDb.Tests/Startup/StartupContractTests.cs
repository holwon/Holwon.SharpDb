namespace SharpDb.Tests.Startup;

/// <summary>
/// The host's startup contract, observed over a real child process: configuration comes only
/// from the environment, and anything it cannot serve makes the process exit immediately with
/// one readable line on stderr (ADR 0007).
/// </summary>
public class StartupContractTests
{
    [Fact]
    public async Task Startup_without_a_database_type_fails_fast()
    {
        var result = await HostProcess.RunAsync(new Dictionary<string, string?>
        {
            ["SHARPDB__CONNECTIONSTRING"] = "Data Source=sharpdb.sqlite",
        });

        var reason = StartupAssertions.AssertFailsFast(result);
        Assert.Contains("SHARPDB__TYPE is required", reason);
    }

    [Fact]
    public async Task Startup_with_an_unknown_database_type_fails_fast_and_lists_the_expected_ones()
    {
        var result = await HostProcess.RunAsync(new Dictionary<string, string?>
        {
            ["SHARPDB__TYPE"] = "postgres",
            ["SHARPDB__CONNECTIONSTRING"] = "Data Source=sharpdb.sqlite",
        });

        var reason = StartupAssertions.AssertFailsFast(result);
        Assert.Contains("'postgres' is unknown", reason);
        Assert.Contains("\"db2\"", reason);
        Assert.Contains("\"mariadb\"", reason);
        Assert.Contains("\"mssql\"", reason);
        Assert.Contains("\"mysql\"", reason);
        Assert.Contains("\"oracle\"", reason);
        Assert.Contains("\"postgresql\"", reason);
        Assert.Contains("\"sqlite\"", reason);
    }

    [Fact]
    public async Task Startup_accepts_the_mssql_type_and_reaches_the_database()
    {
        // The type is recognized and the Provider is assembled: what fails is the connection, and
        // it fails as the SQL Server Driver would. Nothing is listening on this port, so the run
        // proves the whole path from the configured type to a Driver that speaks TDS, without
        // needing a server for a startup contract test.
        var result = await HostProcess.RunAsync(new Dictionary<string, string?>
        {
            ["SHARPDB__TYPE"] = "mssql",
            ["SHARPDB__CONNECTIONSTRING"] = "Server=127.0.0.1,1;Database=none;User Id=none;Password=none;TrustServerCertificate=True;Connect Timeout=2",
        });

        var reason = StartupAssertions.AssertFailsFast(result);
        Assert.Contains("Could not connect to the mssql database", reason);
    }

    [Fact]
    public async Task Startup_against_an_mssql_database_that_does_not_exist_reports_a_reason_without_credentials()
    {
        // The connection string carries a password, and the failure must not: ADR 0010 keeps a
        // credential out of every message a reader sees, and this is the run that would break it.
        var result = await HostProcess.RunAsync(new Dictionary<string, string?>
        {
            ["SHARPDB__TYPE"] = "mssql",
            ["SHARPDB__CONNECTIONSTRING"] =
                "Server=127.0.0.1,1;Database=none;User Id=secret_user;Password=secret_password;TrustServerCertificate=True;Connect Timeout=2",
        });

        var reason = StartupAssertions.AssertFailsFast(result);
        Assert.DoesNotContain("secret_password", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Startup_accepts_the_postgresql_type_and_reaches_the_database()
    {
        // The type is recognized and the Provider is assembled: what fails is the connection, and
        // it fails as the PostgreSQL Provider would. Nothing is listening on this port, so the
        // run proves the whole path from the configured type to a Driver that speaks PostgreSQL,
        // without needing a server for a startup contract test.
        var result = await HostProcess.RunAsync(new Dictionary<string, string?>
        {
            ["SHARPDB__TYPE"] = "PostgreSQL",
            ["SHARPDB__CONNECTIONSTRING"] = "Host=127.0.0.1;Port=1;Database=none;Username=none;Password=none",
        });

        var reason = StartupAssertions.AssertFailsFast(result);
        Assert.Contains("Could not connect to the postgresql database", reason);
    }

    [Fact]
    public async Task Startup_against_a_postgresql_database_that_does_not_exist_reports_a_reason_without_credentials()
    {
        const string password = "sharpdb-should-not-appear";

        var result = await HostProcess.RunAsync(new Dictionary<string, string?>
        {
            ["SHARPDB__TYPE"] = "postgresql",
            ["SHARPDB__CONNECTIONSTRING"] =
                $"Host=127.0.0.1;Port=1;Database=none;Username=postgres;Password={password}",
        });

        var reason = StartupAssertions.AssertFailsFast(result);
        Assert.DoesNotContain(password, reason, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Password", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Startup_accepts_the_oracle_type_and_reaches_the_database()
    {
        // The type is recognized and the Provider is assembled: what fails is the connection, and
        // it fails as the Oracle Provider would. Nothing is listening on this port, so the run
        // proves the whole path from the configured type to a Driver that speaks Oracle, without
        // needing a server for a startup contract test. The Data Source is the short form an
        // Oracle connection string may use, which is the one this Driver parses without a client
        // installed.
        var result = await HostProcess.RunAsync(new Dictionary<string, string?>
        {
            ["SHARPDB__TYPE"] = "Oracle",
            ["SHARPDB__CONNECTIONSTRING"] = "User Id=none;Password=none;Data Source=127.0.0.1:1/none",
        });

        var reason = StartupAssertions.AssertFailsFast(result);
        Assert.Contains("Could not connect to the oracle database", reason);
    }

    [Fact]
    public async Task Startup_against_an_oracle_database_that_does_not_exist_reports_a_reason_without_credentials()
    {
        const string password = "sharpdb-should-not-appear";

        var result = await HostProcess.RunAsync(new Dictionary<string, string?>
        {
            ["SHARPDB__TYPE"] = "oracle",
            ["SHARPDB__CONNECTIONSTRING"] = $"User Id=sharpdb;Password={password};Data Source=127.0.0.1:1/none",
        });

        var reason = StartupAssertions.AssertFailsFast(result);
        Assert.DoesNotContain(password, reason, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Password", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Startup_without_a_connection_string_fails_fast()
    {
        var result = await HostProcess.RunAsync(new Dictionary<string, string?>
        {
            ["SHARPDB__TYPE"] = "sqlite",
        });

        var reason = StartupAssertions.AssertFailsFast(result);
        Assert.Contains("SHARPDB__CONNECTIONSTRING is required", reason);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Startup_with_a_blank_connection_string_fails_fast(string connectionString)
    {
        var result = await HostProcess.RunAsync(new Dictionary<string, string?>
        {
            ["SHARPDB__TYPE"] = "sqlite",
            ["SHARPDB__CONNECTIONSTRING"] = connectionString,
        });

        var reason = StartupAssertions.AssertFailsFast(result);
        Assert.Contains("SHARPDB__CONNECTIONSTRING is required", reason);
    }

    [Fact]
    public async Task Startup_ignores_the_single_underscore_spelling_of_the_type()
    {
        // SHARPDB_TYPE is a different environment variable that the configuration system does
        // not bind; only the double underscore reaches SharpDbOptions.Type (ADR 0006).
        var result = await HostProcess.RunAsync(new Dictionary<string, string?>
        {
            ["SHARPDB_TYPE"] = "sqlite",
            ["SHARPDB_CONNECTIONSTRING"] = "Data Source=sharpdb.sqlite",
        });

        var reason = StartupAssertions.AssertFailsFast(result);
        Assert.Contains("SHARPDB__TYPE is required", reason);
    }

    [Fact]
    public async Task Startup_against_a_database_that_is_not_there_fails_fast()
    {
        using var database = new EmptySqliteDatabase();

        var result = await HostProcess.RunAsync(new Dictionary<string, string?>
        {
            ["SHARPDB__TYPE"] = "sqlite",
            ["SHARPDB__CONNECTIONSTRING"] = $"Data Source={database.MissingDatabasePath}",
        });

        var reason = StartupAssertions.AssertFailsFast(result);
        Assert.Contains("Could not connect to the sqlite database", reason);
    }

    [Fact]
    public async Task Startup_failure_never_repeats_the_connection_string()
    {
        using var database = new EmptySqliteDatabase();
        const string password = "hunter2";
        var connectionString = $"Data Source={database.MissingDatabasePath};Password={password}";

        var result = await HostProcess.RunAsync(new Dictionary<string, string?>
        {
            ["SHARPDB__TYPE"] = "sqlite",
            ["SHARPDB__CONNECTIONSTRING"] = connectionString,
        });

        var reason = StartupAssertions.AssertFailsFast(result);
        Assert.DoesNotContain(password, reason, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Data Source", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Startup_against_a_database_that_is_available_succeeds_and_leaves_stdout_clean()
    {
        using var database = new EmptySqliteDatabase();

        var result = await HostProcess.RunAsync(database.Environment());

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.StandardOutput);
    }

    [Fact]
    public async Task Startup_with_an_unreadable_row_limit_fails_fast()
    {
        var result = await HostProcess.RunAsync(new Dictionary<string, string?>
        {
            ["SHARPDB__TYPE"] = "sqlite",
            ["SHARPDB__CONNECTIONSTRING"] = "Data Source=sharpdb.sqlite",
            ["SHARPDB__MAXROWS"] = "plenty",
        });

        var reason = StartupAssertions.AssertFailsFast(result);
        Assert.Contains("Invalid configuration", reason);
        Assert.Contains("MaxRows", reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("plenty", reason);
    }

    [Fact]
    public async Task Startup_with_an_unparsable_connection_string_fails_fast()
    {
        var result = await HostProcess.RunAsync(new Dictionary<string, string?>
        {
            ["SHARPDB__TYPE"] = "sqlite",
            ["SHARPDB__CONNECTIONSTRING"] = "not-a-connection-string",
        });

        var reason = StartupAssertions.AssertFailsFast(result);
        Assert.Contains("Could not connect to the sqlite database", reason);
    }

    [Fact]
    public async Task Startup_ignores_a_configuration_file_even_when_it_sits_beside_the_host()
    {
        // The host runs with its own directory as content root, so this is exactly where the
        // generic host's defaults would have looked for appsettings.json. Environment variables
        // are the only source, so the type must still be reported missing (ADR 0008).
        var configurationFile = Path.Combine(HostProcess.HostDirectory, "appsettings.json");
        Assert.False(
            File.Exists(configurationFile),
            $"{configurationFile} already exists; the test cannot tell whether the host read it.");

        await File.WriteAllTextAsync(
            configurationFile,
            """{ "SHARPDB": { "TYPE": "sqlite", "CONNECTIONSTRING": "Data Source=sharpdb.sqlite" } }""");

        try
        {
            var result = await HostProcess.RunAsync(new Dictionary<string, string?>());

            var reason = StartupAssertions.AssertFailsFast(result);
            Assert.Contains("SHARPDB__TYPE is required", reason);
        }
        finally
        {
            File.Delete(configurationFile);
        }
    }
}
