namespace SharpDb.Tests.Startup;

/// <summary>
/// The host's logging contract, observed over a real child process: how much it logs is decided by
/// the ordinary <c>Logging__LogLevel__Default</c> key, and every line goes to stderr (ADR 0007).
/// </summary>
/// <remarks>
/// This is process-level because the wiring it covers is the host's: an empty application builder
/// does not read the <c>Logging</c> configuration section on its own, so the key can bind into the
/// configuration and still change nothing. A test that built its own host would pass either way,
/// which is exactly how that defect survived.
/// </remarks>
public class LoggingContractTests
{
    [Fact]
    public async Task The_host_logs_at_information_level_when_no_level_is_configured()
    {
        using var database = new EmptySqliteDatabase();

        var result = await HostProcess.RunAsync(database.Environment());

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("info:", result.StandardError);
    }

    [Fact]
    public async Task The_standard_log_level_key_raises_the_log_to_debug()
    {
        using var database = new EmptySqliteDatabase();

        var result = await HostProcess.RunAsync(database.Environment(logLevel: "Debug"));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("dbug:", result.StandardError);
    }

    [Fact]
    public async Task The_standard_log_level_key_quiets_the_log()
    {
        using var database = new EmptySqliteDatabase();

        var result = await HostProcess.RunAsync(database.Environment(logLevel: "Warning"));

        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain("info:", result.StandardError);
    }

    [Fact]
    public async Task The_host_never_logs_the_connection_string_at_any_level()
    {
        using var database = new EmptySqliteDatabase();

        foreach (var level in new[] { "Debug", "Information", "Warning" })
        {
            var result = await HostProcess.RunAsync(database.Environment(logLevel: level));

            Assert.Equal(0, result.ExitCode);
            Assert.DoesNotContain("Data Source", result.StandardError);
            Assert.DoesNotContain(database.DatabasePath, result.StandardError);
        }
    }
}
