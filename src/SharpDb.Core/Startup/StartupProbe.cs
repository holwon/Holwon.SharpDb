using SharpDb.Abstractions;
using SharpDb.Core.Configuration;

namespace SharpDb.Core.Startup;

/// <summary>
/// Proves at startup that the configured database is reachable, so a broken configuration is
/// reported once, before the server claims to be up (ADR 0007).
/// </summary>
public static class StartupProbe
{
    /// <summary>
    /// Opens one read-only connection and closes it again.
    /// </summary>
    /// <param name="provider">The Provider selected for the configured database type.</param>
    /// <param name="options">The configuration read at startup.</param>
    /// <returns>A task that completes when the database has answered.</returns>
    /// <exception cref="StartupException">
    /// The connection string is missing or blank, or the database refused the connection for any
    /// reason — wrong credentials, unreachable host, missing database, unparsable string.
    /// </exception>
    /// <remarks>
    /// This is a boundary: third-party input (the environment and the Driver) is turned into the
    /// host's single failure message. Because any exception escaping the host would print a stack
    /// trace and break the one-line contract, every failure here is translated rather than
    /// allowed through.
    /// </remarks>
    public static async Task ProbeAsync(IDatabaseProvider provider, SharpDbOptions options)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(options);

        var connectionString = options.ConnectionString;
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw StartupException.MissingConnectionString();
        }

        try
        {
            await using var connection = await provider.CreateReadOnlyConnectionAsync(
                connectionString,
                CancellationToken.None);
        }
        catch (Exception exception)
        {
            throw StartupException.ConnectionFailed(provider.Type, exception);
        }
    }
}
