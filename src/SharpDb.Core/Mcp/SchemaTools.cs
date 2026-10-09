using System.ComponentModel;
using System.Data.Common;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using SharpDb.Abstractions;
using SharpDb.Abstractions.Schema;
using SharpDb.Core.Configuration;

namespace SharpDb.Core.Mcp;

/// <summary>
/// The Tools that report what the connected database holds: its tables and views, the structure of
/// one of them, and the databases the connection can see.
/// </summary>
/// <remarks>
/// No Tool consults the Guard. The Guard polices SQL that arrives from the client; what these
/// Tools run is introspection SQL the Provider wrote, which has no injection surface, and the
/// connection it runs on is already read-only at the engine (ADR 0002).
/// <para>
/// Each call opens its own connection and closes it, so calls neither share state nor depend on
/// one another (ADR 0003).
/// </para>
/// </remarks>
/// <param name="provider">The Provider serving the database this process is bound to.</param>
/// <param name="options">The configuration read at startup, which carries the connection string.</param>
/// <exception cref="InvalidOperationException">
/// The configuration carries no connection string, which means the Tools were wired ahead of the
/// startup check that guarantees one (ADR 0007).
/// </exception>
public sealed class SchemaTools(IDatabaseProvider provider, SharpDbOptions options)
{
    /// <summary>
    /// The connection string every call connects with, proven present when the Tools were built.
    /// </summary>
    /// <remarks>
    /// A blank string is refused here rather than at the first call: it can only mean the wiring
    /// put these Tools ahead of the startup check, and substituting an empty string would surface
    /// later as an obscure database error instead of the wiring defect it is.
    /// </remarks>
    private readonly string _connectionString = ConnectionStringOf(options);

    /// <summary>
    /// Reads the connection string the Tools must use.
    /// </summary>
    /// <param name="options">The configuration read at startup.</param>
    /// <returns>The connection string, which is never blank.</returns>
    /// <exception cref="InvalidOperationException">The configuration carries no connection string.</exception>
    private static string ConnectionStringOf(SharpDbOptions options)
    {
        var connectionString = options?.ConnectionString;
        return string.IsNullOrWhiteSpace(connectionString)
            ? throw new InvalidOperationException(
                $"The {nameof(SchemaTools)} were built without a connection string.")
            : connectionString;
    }

    /// <summary>
    /// Lists every table and view in the connected database.
    /// </summary>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The listing, or a failure the client can read.</returns>
    [McpServerTool(ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("List the tables and views in the connected database. "
        + "Each entry has a name, a type (\"table\" or \"view\") and a comment when the database provides one. "
        + "Ordered by name.")]
    public async Task<CallToolResult> ListTablesAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);

        var tables = await provider.ListTablesAsync(connection, cancellationToken);

        return ToolResults.Success(tables.Select(table => new
        {
            table.Name,
            Type = table.Type == TableType.View ? "view" : "table",
            table.Comment,
        }));
    }

    /// <summary>
    /// Describes one table or view: its columns, primary key, foreign keys and indexes.
    /// </summary>
    /// <param name="table">The name of the table or view to describe.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The description, or a failure the client can read.</returns>
    /// <remarks>
    /// A name that matches nothing is a business failure rather than an empty answer, because the
    /// caller asked about one specific table and learning it does not exist is worth more than a
    /// blank structure it might read as "a table with no columns".
    /// </remarks>
    [McpServerTool(ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Describe one table or view: its columns (name, position, declared type, nullable, "
        + "default, auto-increment, comment), its primary key, its foreign keys and its indexes. "
        + "The primary key is a list of column names, and it is not repeated among the indexes. "
        + "Fields with no value are left out.")]
    public async Task<CallToolResult> DescribeTableAsync(
        [Description("The name of the table or view to describe.")] string table,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);

        var schema = await provider.DescribeTableAsync(connection, table, cancellationToken);
        if (schema is null)
        {
            return ToolResults.Failure($"Table '{table}' does not exist.");
        }

        return ToolResults.Success(new
        {
            schema.Name,
            Type = schema.Type == TableType.View ? "view" : "table",
            schema.Comment,
            schema.Columns,
            schema.PrimaryKey,
            schema.ForeignKeys,
            schema.Indexes,
        });
    }

    /// <summary>
    /// Lists the databases the connection can see.
    /// </summary>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The listing, or a failure the client can read.</returns>
    [McpServerTool(ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("List the databases visible to this connection. "
        + "This is informational only: the server is bound to one database, and every other tool operates on it. "
        + "The other databases cannot be queried.")]
    public async Task<CallToolResult> ListDatabasesAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);

        var databases = await provider.ListDatabasesAsync(connection, cancellationToken);

        return ToolResults.Success(databases);
    }

    /// <summary>
    /// Opens the connection a single call runs on.
    /// </summary>
    /// <param name="cancellationToken">Cancels the attempt to connect.</param>
    /// <returns>The open, read-only connection, which the caller disposes.</returns>
    private Task<DbConnection> OpenAsync(CancellationToken cancellationToken) =>
        provider.CreateReadOnlyConnectionAsync(_connectionString, cancellationToken);
}
