using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharpDb.Abstractions;
using SharpDb.Core.Configuration;
using SharpDb.Core.Startup;
using SharpDb.Provider.MySql;
using SharpDb.Provider.Sqlite;

namespace SharpDb;

/// <summary>
/// The entry point of the SharpDb host executable.
/// </summary>
/// <remarks>
/// Startup is a short, strictly ordered sequence: read the configuration, pick the Provider the
/// requested database type names, and prove the database answers. Anything that goes wrong ends
/// the process right there with one line on stderr, because an MCP client shows that line to the
/// user while it would never show the runtime output of a server that started anyway (ADR 0007).
/// After a successful probe the process still exits; the stdio loop arrives with the Tools.
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
            var catalog = new ProviderCatalog(new MySqlDatabaseProvider(), new SqliteDatabaseProvider());
            var provider = catalog.Select(options.Type);

            // The Provider is registered for the Tool services to resolve, and probed straight
            // from the local: the probe must not depend on the container, or a wiring mistake
            // would escape startup as a stack trace instead of a reason (ADR 0003).
            builder.Services.AddSingleton(provider);

            using var host = builder.Build();

            await StartupProbe.ProbeAsync(provider, options);

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

        return builder;
    }
}
