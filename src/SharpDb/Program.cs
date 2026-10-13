using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharpDb.Abstractions;
using SharpDb.Core.Configuration;
using SharpDb.Core.Guard;
using SharpDb.Core.Mcp;
using SharpDb.Core.Startup;
using SharpDb.Provider.MsSql;
using SharpDb.Provider.MySql;
using SharpDb.Provider.Oracle;
using SharpDb.Provider.PostgreSql;
using SharpDb.Provider.Sqlite;

namespace SharpDb;

/// <summary>
/// The entry point of the SharpDb host executable.
/// </summary>
/// <remarks>
/// Startup is a short, strictly ordered sequence: read the configuration, pick the Provider the
/// requested database type names, prove the database answers, and only then serve the stdio loop.
/// Anything that goes wrong before the loop ends the process right there with one line on stderr,
/// because an MCP client shows that line to the user while it would never show the runtime output
/// of a server that started anyway (ADR 0007).
/// </remarks>
internal static class Program
{
    /// <summary>
    /// The exit code a startup failure ends with.
    /// </summary>
    private const int FailureExitCode = 1;

    /// <summary>
    /// The exit code a run that started cleanly ends with.
    /// </summary>
    private const int SuccessExitCode = 0;

    /// <summary>
    /// The prefix every startup failure line carries, so it is unmistakably SharpDb's.
    /// </summary>
    private const string FailurePrefix = "sharpdb: ";

    /// <summary>
    /// Starts the host.
    /// </summary>
    /// <returns>
    /// The process exit code.
    /// </returns>
    private static async Task<int> Main()
    {
        try
        {
            var builder = CreateHostBuilder();

            var options = SharpDbConfiguration.Read(builder.Configuration);
            var catalog = new ProviderCatalog(
                new MsSqlDatabaseProvider(),
                new MySqlDatabaseProvider(),
                new OracleDatabaseProvider(),
                new PostgreSqlDatabaseProvider(),
                new SqliteDatabaseProvider());
            var provider = catalog.Select(options.Type);

            // The Provider is registered for the Tool services to resolve, and probed straight
            // from the local: the probe must not depend on the container, or a wiring mistake
            // would escape startup as a stack trace instead of a reason (ADR 0003).
            builder.Services.AddSingleton(provider);

            // The Guard is built from the selected Provider's policy, so the dialect and the
            // denied functions in force are the ones the engine's own declaration names — and the
            // Guard itself stays one algorithm in Core rather than one per database type.
            builder.Services.AddSingleton(new ReadOnlyGuard(provider.Policy));

            // The options carry the limits and the timeout every query is run with, so the Tools
            // resolve them rather than reading the environment a second time.
            builder.Services.AddSingleton(options);

            // The four Tools, registered by type rather than discovered by reflection: the set a
            // client sees is decided here, and a change to it is a change to this line (ADR 0003).
            builder.Services
                .AddMcpServer()
                .WithTools<SchemaTools>()
                .WithTools<QueryTools>()
                .WithStdioServerTransport();

            using var host = builder.Build();

            // The database is proven reachable before the loop starts, so a bad connection string
            // is a startup failure rather than a failure of whichever call happens to be first.
            await StartupProbe.ProbeAsync(provider, options);

            // Runs until the client closes the pipe, which is how an MCP stdio server is stopped.
            await host.RunAsync();

            return SuccessExitCode;
        }
        catch (StartupException failure)
        {
            // The message alone: no stack trace, and nothing that could carry the connection
            // string. stdout is left untouched so the protocol channel stays clean (ADR 0007).
            //
            // Catching only StartupException is what makes the one-line contract hold: the
            // boundaries that touch foreign input — the configuration binder and the Driver —
            // translate their failures into it, so nothing else can reach here from a bad
            // environment or an unreachable database.
            await Console.Error.WriteLineAsync(FailurePrefix + failure.Message);
            return FailureExitCode;
        }
    }

    /// <summary>
    /// Builds the host the process runs on.
    /// </summary>
    /// <returns>The builder whose configuration and services startup reads.</returns>
    /// <remarks>
    /// The host is built without its default configuration sources, which is what keeps
    /// <c>appsettings.json</c>, its environment-specific variant and user secrets out: with the
    /// generic host's defaults they would arrive whether SharpDb wanted them or not, and
    /// environment variables are the only channel an MCP client can use (ADR 0008).
    /// </remarks>
    private static HostApplicationBuilder CreateHostBuilder()
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings
        {
            ContentRootPath = AppContext.BaseDirectory,
        });

        // The only configuration source there is: the MCP client's env block (ADR 0008).
        builder.Configuration.AddEnvironmentVariables();

        // Console logging belongs on stderr: stdout is the stdio transport's, and a stray line
        // there desynchronises the client with no way to recover (ADR 0007).
        builder.Logging.AddConsole(console => console.LogToStandardErrorThreshold = LogLevel.Trace);

        // The `Logging` section is bound explicitly, because the empty builder is empty in this
        // respect too: it leaves the section unread, so `Logging__LogLevel__Default` would land in
        // the configuration and change nothing. Reading it is what makes the ordinary key the way
        // to raise or quiet the log, rather than a private switch this server invented.
        builder.Logging.AddConfiguration(builder.Configuration.GetSection("Logging"));

        return builder;
    }
}
