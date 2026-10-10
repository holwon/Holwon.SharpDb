using Microsoft.Extensions.Logging.Abstractions;
using SharpDb.Abstractions;
using SharpDb.Core.Configuration;
using SharpDb.Core.Mcp;

namespace SharpDb.Provider.Contract;

/// <summary>
/// Builds the query Tool the way the running server builds it, so every assertion in this library
/// exercises the Tool a client would reach rather than a differently-wired one.
/// </summary>
/// <remarks>
/// One place, because the wiring is part of what is under test: the Tool resolves its configuration
/// at construction, and a test that assembled it differently — another limit, another timeout —
/// would still pass while proving less than it claims.
/// </remarks>
public static class QueryTool
{
    /// <summary>
    /// Builds the query Tool for the engine under contract.
    /// </summary>
    /// <param name="database">The engine under contract.</param>
    /// <param name="timeoutSeconds">The timeout every query runs under.</param>
    /// <param name="defaultMaxRows">The limit used when a caller asks for none.</param>
    /// <param name="maxRows">The limit no caller may exceed.</param>
    /// <returns>The Tool, wired as the running server wires it.</returns>
    public static QueryTools For(
        IContractDatabase database,
        int timeoutSeconds = 1,
        int defaultMaxRows = 100,
        int maxRows = 1000) =>
        For(database.Provider, database.ConnectionString, database.Type, timeoutSeconds, defaultMaxRows, maxRows);

    /// <summary>
    /// Builds the query Tool for a database named by a connection string.
    /// </summary>
    /// <param name="provider">The Provider that opens the connection.</param>
    /// <param name="connectionString">The connection string the Tool connects with.</param>
    /// <param name="type">The identifier the configuration carries.</param>
    /// <param name="timeoutSeconds">The timeout every query runs under.</param>
    /// <param name="defaultMaxRows">The limit used when a caller asks for none.</param>
    /// <param name="maxRows">The limit no caller may exceed.</param>
    /// <returns>The Tool, wired as the running server wires it.</returns>
    /// <remarks>
    /// The overload exists for the tests that must reach a database other than the one the fixture
    /// calls its own — a database that is not there at all, or the fixture's spare one.
    /// </remarks>
    public static QueryTools For(
        IDatabaseProvider provider,
        string connectionString,
        string type,
        int timeoutSeconds,
        int defaultMaxRows = 100,
        int maxRows = 1000) => new(
        provider,
        new SharpDbOptions
        {
            Type = type,
            ConnectionString = connectionString,
            DefaultMaxRows = defaultMaxRows,
            MaxRows = maxRows,
            TimeoutSeconds = timeoutSeconds,
        },
        NullLogger<QueryTools>.Instance);
}
