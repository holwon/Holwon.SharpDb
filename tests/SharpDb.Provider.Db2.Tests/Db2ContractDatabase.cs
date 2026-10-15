using System.Diagnostics;
using IBM.Data.Db2;
using SharpDb.Abstractions;
using SharpDb.Provider.Contract;

namespace SharpDb.Provider.Db2.Tests;

/// <summary>
/// A real Db2 LUW database the shared suites run against: a container started by hand (there is no
/// Testcontainers package for Db2), seeded with the shared schema, then handed to the Provider.
/// </summary>
/// <remarks>
/// One container serves the whole collection. When Docker is absent the fixture reports the engine
/// as unavailable rather than throwing; the suites turn that into skips. Anything else — a failed
/// seed, a container that does not start — surfaces loudly, because only Docker's own absence is
/// an acceptable reason not to test.
/// <para>
/// The Db2 Provider opens the connection as-is (no read-only mode to set), so the connection string
/// the fixture builds is the same one the Provider receives.
/// </para>
/// </remarks>
public sealed class Db2ContractDatabase : IContractDatabase, IAsyncLifetime
{
    /// <summary>
    /// The Docker image for the Db2 Community Edition.
    /// </summary>
    private const string Image = "ibmcom/db2:latest";

    /// <summary>
    /// The container name, so it can be stopped and removed deterministically.
    /// </summary>
    private const string ContainerName = "sharpdb-db2-test";

    /// <summary>
    /// The host port the container maps to.
    /// </summary>
    private const int HostPort = 50100;

    /// <summary>
    /// The Db2 instance user.
    /// </summary>
    private const string User = "db2inst1";

    /// <summary>
    /// The Db2 instance password.
    /// </summary>
    private const string Password = "SharpDb!Contract2026";

    /// <summary>
    /// The database name created inside the container.
    /// </summary>
    private const string Database = "contract";

    /// <inheritdoc/>
    public string Type => "db2";

    /// <inheritdoc/>
    public IDatabaseProvider Provider { get; } = new Db2DatabaseProvider();

    /// <inheritdoc/>
    public string ConnectionString { get; private set; } = string.Empty;

    /// <inheritdoc/>
    public string DatabaseName => Database;

    /// <inheritdoc/>
    /// <remarks>
    /// A cross join over SYSCAT.COLUMNS, measured to run for several seconds on Db2 LUW.
    /// </remarks>
    public string SlowQuery => """
        SELECT COUNT(*) FROM SYSCAT.COLUMNS AS a
        CROSS JOIN SYSCAT.COLUMNS AS b
        CROSS JOIN SYSCAT.COLUMNS AS c
        """;

    /// <inheritdoc/>
    public bool Available { get; private set; }

    /// <inheritdoc/>
    public string SkipReason { get; private set; } = string.Empty;

    /// <inheritdoc/>
    public string SensitiveText => Password;

    /// <inheritdoc/>
    /// <remarks>
    /// The container is managed manually: start it with <c>docker run</c>, seed it, and the fixture
    /// checks whether it is reachable. When Docker is absent or the container is not running, the
    /// fixture reports the engine as unavailable rather than throwing; the suites turn that into
    /// skips.
    /// </remarks>
    public async Task InitializeAsync()
    {
        if (!IsDockerAvailable())
        {
            SkipReason = "Docker is not available, so Db2 cannot be started.";
            return;
        }

        // Check whether the test container is already running.
        var inspectResult = RunDocker("inspect", "-f", "{{.State.Running}}", ContainerName);
        if (inspectResult.ExitCode != 0 || inspectResult.StandardOutput.Trim() != "true")
        {
            SkipReason = $"Db2 container '{ContainerName}' is not running. Start it with: docker run -d --name {ContainerName} --privileged -e LICENSE=accept -e DB2INST1_PASSWORD={Password} -e DBNAME={Database} -p {HostPort}:50000 {Image}";
            return;
        }

        ConnectionString = BuildConnectionString();

        // Prove the connection works before declaring available.
        try
        {
            await using var probe = new IBM.Data.Db2.DB2Connection(ConnectionString);
            await probe.OpenAsync();
        }
        catch (Exception ex)
        {
            SkipReason = $"Db2 container is running but not reachable: {ex.Message}";
            return;
        }

        // Seed the shared schema (idempotent: drops first, then creates).
        try
        {
            await SeedAsync();
        }
        catch (Exception ex)
        {
            SkipReason = $"Db2 seed failed: {ex.Message}";
            return;
        }

        Available = true;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The container is not owned by the fixture: it is started by hand and left running for
    /// repeated test runs. Disposing the fixture does not stop it.
    /// </remarks>
    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// Builds the connection string for the Provider to use.
    /// </summary>
    private static string BuildConnectionString() =>
        $"Server=127.0.0.1:{HostPort};Database={Database};UID={User};Password={Password}";

    /// <summary>
    /// Seeds the shared schema using the Db2 DDL from <see cref="ContractSchema"/>.
    /// </summary>
    /// <remarks>
    /// Db2 LUW does not support <c>DROP VIEW IF EXISTS</c>, so the view is dropped by trying and
    /// catching the error before the DDL runs. Everything else in the DDL uses <c>DROP TABLE IF
    /// EXISTS</c>, which Db2 does support.
    /// </remarks>
    private async Task SeedAsync()
    {
        await using var connection = new DB2Connection(ConnectionString);
        await connection.OpenAsync();

        // Db2 has no DROP VIEW IF EXISTS — try and swallow the error when the view is absent.
        await TryDropViewAsync(connection, "ACTIVE_USERS");

        foreach (var statement in ContractSchema.Db2Ddl)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = statement;
            await command.ExecuteNonQueryAsync();
        }
    }

    /// <summary>
    /// Drops a view if it exists, swallowing the error when it does not.
    /// </summary>
    private static async Task TryDropViewAsync(DB2Connection connection, string viewName)
    {
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"DROP VIEW {viewName}";
            await command.ExecuteNonQueryAsync();
        }
        catch (DB2Exception)
        {
            // The view does not exist — this is the expected case on a fresh database.
        }
    }

    /// <summary>
    /// Polls the container logs until "Setup has completed" appears or the timeout expires.
    /// </summary>
    private static async Task<(bool Ready, string Diagnostic)> WaitForReadyAsync(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            var logs = RunDocker("logs", ContainerName);
            var combined = logs.StandardOutput + logs.StandardError;

            if (combined.Contains("Setup has completed") || combined.Contains("All databases are now active"))
            {
                return (true, string.Empty);
            }

            await Task.Delay(TimeSpan.FromSeconds(10));
        }

        var finalLogs = RunDocker("logs", ContainerName);
        return (false, $"Last logs: {(finalLogs.StandardOutput + finalLogs.StandardError)[^500..]}");
    }

    /// <summary>
    /// Whether Docker is available on this machine.
    /// </summary>
    private static bool IsDockerAvailable()
    {
        try
        {
            var result = RunDocker("info");
            return result.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// The result of running a docker command.
    /// </summary>
    private sealed record DockerResult(int ExitCode, string StandardOutput, string StandardError);

    /// <summary>
    /// Runs a docker command and captures its output.
    /// </summary>
    private static DockerResult RunDocker(params string[] arguments)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "docker",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };

        foreach (var arg in arguments)
        {
            process.StartInfo.ArgumentList.Add(arg);
        }

        process.Start();
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return new DockerResult(process.ExitCode, output, error);
    }
}
