using Microsoft.Extensions.Logging.Abstractions;
using SharpDb.Core.Configuration;
using SharpDb.Core.Mcp;
using SharpDb.Provider.Contract;

namespace SharpDb.Provider.Sqlite.Tests;

/// <summary>
/// What the query Tool answers on SQLite that the shared contract suite cannot assert, because no
/// other engine answers it the same way.
/// </summary>
/// <remarks>
/// Everything both engines agree on — the payload's shape, the limits, parameters, a refusal, a
/// database error, a timeout — lives in <c>SharpDb.Provider.Contract</c> and is asserted there
/// once, for both. What remains here is SQLite's own: a database that cannot be opened at all, and
/// the reason the path a failed open names is kept out of the message.
/// </remarks>
public class SqliteQueryExecutionTests
{
    [Fact]
    public async Task A_connection_the_server_cannot_open_is_a_failure_the_caller_can_read()
    {
        // A database that is not there is a business failure like any other: the caller gets a
        // failed result it can read, and nothing escapes as an exception (ADR 0010).
        //
        // Measured on Microsoft.Data.Sqlite 10.0.12: the message for a failed read-only open is
        // "SQLite Error 14: 'unable to open database file'." — the Driver names no file, so the
        // redaction of a path cannot be exercised through it and is asserted at the unit level
        // instead. What is asserted here is the path's absence, whatever the Driver chose to say.
        using var database = new MissingSqliteDatabase();
        var tool = ToolFor(database.ConnectionString);

        var result = await tool.ExecuteQueryAsync("SELECT 1");

        Assert.True(result.IsError);
        Assert.StartsWith("Database error:", ToolPayload.Error(result));
        Assert.DoesNotContain(database.DirectoryPath, ToolPayload.Error(result), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A query Tool serving the given database.
    /// </summary>
    /// <param name="connectionString">The connection string the Tool connects with.</param>
    /// <returns>The Tool, wired as the running server wires it.</returns>
    private static QueryTools ToolFor(string connectionString) => new(
        new SqliteDatabaseProvider(),
        new SharpDbOptions
        {
            Type = "sqlite",
            ConnectionString = connectionString,
            DefaultMaxRows = 100,
            MaxRows = 1000,
            TimeoutSeconds = 30,
        },
        NullLogger<QueryTools>.Instance);
}
