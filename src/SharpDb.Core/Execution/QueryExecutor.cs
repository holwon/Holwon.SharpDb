using System.Data.Common;
using System.Diagnostics;
using Dapper;

namespace SharpDb.Core.Execution;

/// <summary>
/// Runs one read-only statement on a connection the Provider opened, and reads its result set.
/// </summary>
/// <remarks>
/// The row limit is applied while reading rather than by rewriting the statement: the caller's SQL
/// is run exactly as it was written, so what the database is asked for is what the caller asked
/// for. Reading stops one row past the limit, which is what tells a full answer from a cut one.
/// <para>
/// The timeout is applied twice on purpose. The command carries it, which is what lets a Driver
/// interrupt a statement it can interrupt; and the read runs under a deadline of the same length,
/// which is what lets Core recognise a timeout without knowing which Driver produced it (ADR 0004)
/// and what bounds a Driver that never observes the command's own timeout.
/// </para>
/// </remarks>
internal static class QueryExecutor
{
    /// <summary>
    /// Runs a statement and reads its result set.
    /// </summary>
    /// <param name="connection">The open, read-only connection to run on.</param>
    /// <param name="sql">The statement to run, exactly as the caller wrote it.</param>
    /// <param name="parameters">The values to bind, or <see langword="null"/> when there are none.</param>
    /// <param name="maxRows">The greatest number of rows to return.</param>
    /// <param name="timeoutSeconds">The timeout in seconds; zero or less means no timeout.</param>
    /// <param name="cancellationToken">Cancels the call on the caller's behalf.</param>
    /// <returns>The result set, marked when the limit cut it short.</returns>
    /// <exception cref="QueryTimedOutException">The statement outlived its timeout.</exception>
    /// <exception cref="DbException">The database refused the statement.</exception>
    /// <exception cref="OperationCanceledException">The caller cancelled the call.</exception>
    internal static async Task<QueryResult> ExecuteAsync(
        DbConnection connection,
        string sql,
        IReadOnlyDictionary<string, object?>? parameters,
        int maxRows,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        using var deadline = DeadlineFor(timeoutSeconds, cancellationToken);
        var started = Stopwatch.GetTimestamp();

        var command = new CommandDefinition(
            sql,
            BindingsFor(parameters),
            commandTimeout: Math.Max(timeoutSeconds, 0),
            cancellationToken: deadline.Token);

        try
        {
            await using var reader = await connection.ExecuteReaderAsync(command);

            return await ReadAsync(reader, maxRows, deadline.Token);
        }
        catch (Exception) when (IsOurTimeout(deadline, cancellationToken, started, timeoutSeconds))
        {
            // The statement outlived this server's timeout, so the caller is told about the
            // timeout rather than about whichever exception a Driver chose to raise for it.
            throw new QueryTimedOutException(timeoutSeconds);
        }
    }

    /// <summary>
    /// Whether a failure is this server's own timeout rather than the database's own answer.
    /// </summary>
    /// <param name="deadline">The deadline every read runs under.</param>
    /// <param name="cancellationToken">The caller's own token.</param>
    /// <param name="started">The timestamp the statement started at.</param>
    /// <param name="timeoutSeconds">The configured timeout in seconds.</param>
    /// <returns><see langword="true"/> when the failure is the timeout.</returns>
    /// <remarks>
    /// Two ways of recognising it, and the second is not redundant. The deadline being cancelled
    /// is the ordinary case — Core's own timer fired. But a Driver is free to enforce the
    /// command's timeout itself, and measured, MySqlConnector does exactly that and raises its own
    /// exception from its own timer, which can win the race against Core's cancellation callback
    /// by a hair. Requiring the flag alone would then let that Driver's wording escape to the
    /// caller as a database error — measured: <c>The Command Timeout expired…</c> instead of this
    /// server's fixed sentence — and the same query would report differently depending on which
    /// timer happened to fire first.
    /// <para>
    /// So elapsed time is the second test, and it is sound because this method is what set the
    /// timeout: a statement that failed no earlier than the timeout it was given cannot have
    /// failed for any other reason. The comparison is against the same number the command was
    /// given, so a Driver that enforces it a few milliseconds early — clocks are not exact — is
    /// still recognised.
    /// </para>
    /// <para>
    /// A caller's own cancellation is excluded throughout: that is the caller's decision, not this
    /// server's timeout, and it must stay distinguishable.
    /// </para>
    /// </remarks>
    private static bool IsOurTimeout(
        CancellationTokenSource deadline,
        CancellationToken cancellationToken,
        long started,
        int timeoutSeconds)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return false;
        }

        if (deadline.IsCancellationRequested)
        {
            return true;
        }

        return timeoutSeconds > 0
            && Stopwatch.GetElapsedTime(started) >= TimeSpan.FromSeconds(timeoutSeconds);
    }

    /// <summary>
    /// Starts a deadline that fires after the configured timeout.
    /// </summary>
    /// <param name="timeoutSeconds">The timeout in seconds; zero or less means no timeout.</param>
    /// <param name="cancellationToken">The caller's own token, which the deadline follows.</param>
    /// <returns>The source the deadline is read from, which the caller disposes.</returns>
    private static CancellationTokenSource DeadlineFor(int timeoutSeconds, CancellationToken cancellationToken)
    {
        var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        // Zero means "no timeout", the same reading the command's own timeout gives it.
        if (timeoutSeconds > 0)
        {
            deadline.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        }

        return deadline;
    }

    /// <summary>
    /// Turns the values to bind into the parameter set the statement is run with.
    /// </summary>
    /// <param name="parameters">The values to bind, or <see langword="null"/> when there are none.</param>
    /// <returns>The parameter set, or <see langword="null"/> when there is nothing to bind.</returns>
    /// <remarks>
    /// A key is added as the caller wrote it. Dapper matches it to the <c>@name</c> placeholder in
    /// the statement, which is the pair the Tool's description promises.
    /// </remarks>
    private static object? BindingsFor(IReadOnlyDictionary<string, object?>? parameters)
    {
        if (parameters is null)
        {
            return null;
        }

        var bindings = new DynamicParameters();
        foreach (var (name, value) in parameters)
        {
            bindings.Add(name, value);
        }

        return bindings;
    }

    /// <summary>
    /// Reads a result set up to the row limit.
    /// </summary>
    /// <param name="reader">The reader positioned before the first row.</param>
    /// <param name="maxRows">The greatest number of rows to return.</param>
    /// <param name="cancellationToken">The deadline the read runs under.</param>
    /// <returns>The result set.</returns>
    private static async Task<QueryResult> ReadAsync(
        DbDataReader reader,
        int maxRows,
        CancellationToken cancellationToken)
    {
        var columns = new string[reader.FieldCount];
        for (var index = 0; index < columns.Length; index++)
        {
            columns[index] = reader.GetName(index);
        }

        var rows = new List<IReadOnlyList<object?>>();
        var truncated = false;

        while (await reader.ReadAsync(cancellationToken))
        {
            // A Driver that observes the token interrupts its own read; one that does not — measured:
            // Microsoft.Data.Sqlite returns only once the whole statement has finished, and Dapper
            // does not forward the token to the reader either — is caught here, on the first row it
            // manages to produce. Either way the deadline is what decides.
            cancellationToken.ThrowIfCancellationRequested();

            // The row just read is the one past the limit, so it is not returned: it exists only to
            // tell a complete answer from one that was cut short.
            if (rows.Count >= maxRows)
            {
                truncated = true;
                break;
            }

            rows.Add(ReadRow(reader));
        }

        return new QueryResult(columns, rows, truncated);
    }

    /// <summary>
    /// Reads the current row's values in column order.
    /// </summary>
    /// <param name="reader">The reader positioned on a row.</param>
    /// <returns>The row's values, mapped for the payload.</returns>
    private static object?[] ReadRow(DbDataReader reader)
    {
        var values = new object?[reader.FieldCount];
        for (var index = 0; index < values.Length; index++)
        {
            values[index] = QueryValues.ForJson(reader.GetValue(index));
        }

        return values;
    }
}
