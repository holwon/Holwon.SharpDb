using SharpDb.Provider.Contract;

namespace SharpDb.Provider.Sqlite.Tests;

/// <summary>
/// What the query Tool answers on SQLite that the shared contract suite cannot assert, because no
/// other engine answers it the same way.
/// </summary>
/// <remarks>
/// Everything every engine agrees on — the payload's shape, the limits, parameters, a refusal, a
/// database error, a timeout, and that a message discloses nothing sensitive — lives in
/// <c>SharpDb.Provider.Contract</c> and is asserted there once for every engine. What remains here is
/// SQLite's own: a database file that is not there at all.
/// </remarks>
public class SqliteQueryExecutionTests
{
    /// <summary>
    /// A database file that is not there is answered with a failure the caller can read.
    /// </summary>
    [Fact]
    public async Task A_connection_the_server_cannot_open_is_a_failure_the_caller_can_read()
    {
        // A database that is not there is a business failure like any other: the caller gets a
        // failed result it can read, and nothing escapes as an exception (ADR 0010).
        //
        // Measured on Microsoft.Data.Sqlite 10.0.12: the message for a failed read-only open is
        // "SQLite Error 14: 'unable to open database file'." — the Driver names no file. A failed
        // open is still the one path where a path could appear if the Driver started naming it,
        // so the absence is asserted here as well as in the shared suite's disclosure guard, whose
        // failing statement takes a different route to the message.
        using var database = new MissingSqliteDatabase();
        var tool = QueryTool.For(
            new SqliteDatabaseProvider(),
            database.ConnectionString,
            "sqlite",
            timeoutSeconds: 30);

        var result = await tool.ExecuteQueryAsync("SELECT 1");

        Assert.True(result.IsError);
        Assert.StartsWith("Database error:", ToolPayload.Error(result));
        Assert.DoesNotContain(database.DirectoryPath, ToolPayload.Error(result), StringComparison.OrdinalIgnoreCase);
    }
}
