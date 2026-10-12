using System.ComponentModel;
using System.Data.Common;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using SharpDb.Abstractions;
using SharpDb.Core.Configuration;
using SharpDb.Core.Execution;
using SharpDb.Core.Guard;

namespace SharpDb.Core.Mcp;

/// <summary>
/// The Tool that runs a caller's read-only statement against the connected database.
/// </summary>
/// <remarks>
/// This is the only Tool whose SQL comes from the client, and therefore the only one that consults
/// the Guard. The refusal happens before a connection is opened, so a statement the server will not
/// run never reaches the database at all (ADR 0002).
/// <para>
/// Every call opens its own connection and closes it, so calls neither share state nor depend on
/// one another (ADR 0003).
/// </para>
/// </remarks>
/// <param name="provider">The Provider serving the database this process is bound to.</param>
/// <param name="options">The configuration read at startup, which carries the limits and the timeout.</param>
/// <param name="guard">The read-only Guard, built from the selected Provider's policy.</param>
/// <param name="logger">Where the statement and any failure are recorded.</param>
/// <exception cref="InvalidOperationException">
/// The configuration carries no connection string, which means the Tools were wired ahead of the
/// startup check that guarantees one (ADR 0007).
/// </exception>
public sealed class QueryTools(
    IDatabaseProvider provider,
    SharpDbOptions options,
    ReadOnlyGuard guard,
    ILogger<QueryTools> logger)
{
    /// <summary>
    /// The connection string every call connects with, proven present when the Tools were built.
    /// </summary>
    private readonly string _connectionString = ToolConnectionString.Required(options);

    /// <summary>
    /// Runs one read-only statement and returns its result set.
    /// </summary>
    /// <param name="sql">The statement to run, exactly as the caller wrote it.</param>
    /// <param name="parameters">The values for its placeholders, which may be absent.</param>
    /// <param name="maxRows">The greatest number of rows to return, which may be absent.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The result set, or a failure the client can read.</returns>
    /// <remarks>
    /// A statement that is not read-only, a statement the database refuses, and a statement that
    /// outlives its timeout are all reported as failed results rather than as exceptions, because
    /// the SDK would replace any exception's message with a generic sentence and the caller would
    /// never learn what to change (ADR 0010).
    /// </remarks>
    [McpServerTool(ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Run one read-only SQL statement and return its result set. "
        + "Only SELECT, EXPLAIN and SHOW statements are run; anything else is refused before it reaches the database. "
        + "The reply has a columns array and a rows array of arrays, so each column is named once. "
        + "A truncated marker is present only when the row limit cut the answer short.")]
    public async Task<CallToolResult> ExecuteQueryAsync(
        [Description("The single read-only statement to run, with @name placeholders for any values.")] string sql,
        [Description("Values for the statement's @name placeholders, keyed by the name without its @ prefix.")]
        Dictionary<string, JsonElement>? parameters = null,
        [Description("The greatest number of rows to return. Cut down to the server's maximum when larger; "
            + "the configured default when absent.")]
        int? maxRows = null,
        CancellationToken cancellationToken = default)
    {
        var decision = guard.Inspect(sql);
        if (!decision.IsAllowed)
        {
            return Failed(decision.Reason!);
        }

        // A placeholder the caller gave no value for is refused here rather than left to the
        // Driver. Measured, the three engines do three different things with one: SQLite and MySQL
        // refuse the statement, while PostgreSQL parses the leftover @ as its absolute-value
        // operator and answers with rows the caller never asked for. A wrong answer that looks
        // like a right one is the one outcome this server must never produce (ADR 0010).
        var bindings = QueryParameters.From(parameters);
        if (ParameterPlaceholders.FindUnbound(sql, bindings) is { } unbound)
        {
            return Failed(
                $"No value was supplied for the placeholder @{unbound}. "
                + "Add it to the parameters object under its name without the @ prefix.");
        }

        var rowLimit = RowLimitFor(maxRows);

        // The statement is recorded and the values are not: the statement is what explains a
        // failure later, while the values are row data that has no business in a log.
        logger.LogInformation("Running read-only statement: {Statement}", sql);

        try
        {
            // The connection is opened inside the guard of the failure handling, because a database
            // that cannot be reached is a business failure like any other: reporting it as a result
            // is what keeps it from escaping as an exception the SDK would flatten (ADR 0010).
            await using var connection = await OpenAsync(cancellationToken);

            var result = await QueryExecutor.ExecuteAsync(
                connection,
                sql,
                bindings,
                rowLimit,
                options.TimeoutSeconds,
                cancellationToken);

            // The result set is written in the same shape the schema Tools build: the columns are
            // named once, each row is a list of values in that order, and the truncation marker is
            // present only when the answer was actually cut short.
            return ToolResults.Success(new
            {
                result.Columns,
                result.Rows,
                Truncated = result.Truncated ? (bool?)true : null,
            });
        }
        catch (QueryTimedOutException timedOut)
        {
            return Failed(QueryErrors.TimedOut(timedOut.Seconds));
        }
        catch (DbException databaseFailure)
        {
            return Failed(QueryErrors.DatabaseFailure(databaseFailure.Message));
        }
        catch (InvalidOperationException driverFailure) when (driverFailure is not ObjectDisposedException)
        {
            // Measured on Microsoft.Data.Sqlite 10.0.12: a statement whose placeholder has no
            // parameter is reported as an InvalidOperationException rather than as a DbException,
            // so catching only DbException would let the caller's own mistake — the likeliest one
            // there is — escape as the generic sentence the SDK substitutes for any exception
            // (ADR 0010). An ObjectDisposedException is excluded: it would mean this server disposed
            // something too early, which is a defect here rather than an answer about the caller.
            return Failed(QueryErrors.DatabaseFailure(driverFailure.Message));
        }
    }

    /// <summary>
    /// Reads the row limit a call runs with.
    /// </summary>
    /// <param name="requested">The limit the caller asked for, which may be absent.</param>
    /// <returns>The limit to apply, which is at least one and never above the server's maximum.</returns>
    /// <remarks>
    /// Asking for more rows than the server allows is not an error, and neither is asking for a
    /// nonsensical number: the request is cut to the range the server supports and the call goes
    /// ahead. The lower bound exists because a limit of zero would return nothing while still
    /// claiming to answer.
    /// </remarks>
    private int RowLimitFor(int? requested)
    {
        var maximum = Math.Max(options.MaxRows, 1);
        var limit = requested ?? options.DefaultMaxRows;

        return Math.Clamp(limit, 1, maximum);
    }

    /// <summary>
    /// Reports a failure the client can read.
    /// </summary>
    /// <param name="message">What went wrong, in words meant for the caller.</param>
    /// <returns>The failed result.</returns>
    private CallToolResult Failed(string message)
    {
        logger.LogWarning("execute_query failed: {Reason}", message);

        return ToolResults.Failure(message);
    }

    /// <summary>
    /// Opens the connection a single call runs on.
    /// </summary>
    /// <param name="cancellationToken">Cancels the attempt to connect.</param>
    /// <returns>The open, read-only connection, which the caller disposes.</returns>
    private Task<DbConnection> OpenAsync(CancellationToken cancellationToken) =>
        provider.CreateReadOnlyConnectionAsync(_connectionString, cancellationToken);
}
