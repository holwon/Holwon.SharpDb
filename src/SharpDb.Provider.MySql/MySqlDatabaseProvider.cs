using System.Data.Common;
using MySqlConnector;
using SharpDb.Abstractions;
using SharpDb.Abstractions.Schema;

namespace SharpDb.Provider.MySql;

/// <summary>
/// Serves MySQL databases, reaching them exclusively through the Driver this project owns.
/// </summary>
public sealed class MySqlDatabaseProvider : IDatabaseProvider
{
    /// <summary>
    /// Puts the session in read-only mode; every write it would carry is refused by the server.
    /// </summary>
    private const string EnterReadOnlySession = "SET SESSION TRANSACTION READ ONLY";

    /// <summary>
    /// Lists the tables and views in the connection's own database.
    /// </summary>
    /// <remarks>
    /// <c>DATABASE()</c> is the schema the connection selected, so the listing covers exactly the
    /// database this process is bound to and no other (ADR 0009).
    /// </remarks>
    private const string ListTablesSql = """
        SELECT TABLE_NAME, TABLE_TYPE, TABLE_COMMENT
        FROM INFORMATION_SCHEMA.TABLES
        WHERE TABLE_SCHEMA = DATABASE()
        ORDER BY TABLE_NAME
        """;

    /// <summary>
    /// Lists the schemas the connection's account can see.
    /// </summary>
    private const string ListDatabasesSql = """
        SELECT SCHEMA_NAME
        FROM INFORMATION_SCHEMA.SCHEMATA
        ORDER BY SCHEMA_NAME
        """;

    /// <inheritdoc/>
    public string Type => "mysql";

    /// <inheritdoc/>
    /// <remarks>
    /// Read-only cannot ride along in the connection string here, so the session is switched to
    /// read-only immediately after connecting and before the connection is handed out — the mode
    /// is in force before a single statement runs (ADR 0002, layer 2).
    /// </remarks>
    public async Task<DbConnection> CreateReadOnlyConnectionAsync(
        string connectionString,
        CancellationToken cancellationToken)
    {
        var builder = new MySqlConnectionStringBuilder(connectionString);

        var connection = new MySqlConnection(builder.ConnectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText = EnterReadOnlySession;
            await command.ExecuteNonQueryAsync(cancellationToken);

            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<TableInfo>> ListTablesAsync(
        DbConnection connection,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);

        await using var command = connection.CreateCommand();
        command.CommandText = ListTablesSql;

        var tables = new List<TableInfo>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var type = reader.GetString(1).Equals("VIEW", StringComparison.OrdinalIgnoreCase)
                ? TableType.View
                : TableType.Table;

            tables.Add(new TableInfo(reader.GetString(0), type, Comment(reader, 2, type)));
        }

        return tables;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<string>> ListDatabasesAsync(
        DbConnection connection,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);

        await using var command = connection.CreateCommand();
        command.CommandText = ListDatabasesSql;

        var databases = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            databases.Add(reader.GetString(0));
        }

        return databases;
    }

    /// <summary>
    /// Reads a comment column, treating the database's two ways of saying "no comment" as absent.
    /// </summary>
    /// <param name="reader">The reader positioned on a row.</param>
    /// <param name="ordinal">The comment column's position.</param>
    /// <param name="type">The kind of object the row describes.</param>
    /// <returns>The comment, or <see langword="null"/> when the database kept none.</returns>
    /// <remarks>
    /// Two quirks of <c>INFORMATION_SCHEMA.TABLES</c> are handled here. A table nobody commented on
    /// reports an empty string rather than null, which is absent rather than a comment saying
    /// nothing. And a view reports the literal text <c>VIEW</c> in this column — the server's way
    /// of filling in a column a view cannot use, since <c>CREATE VIEW</c> has no <c>COMMENT</c>
    /// clause — which is a statement about the object's kind, not about its purpose, and would be
    /// read as a comment if it were passed through.
    /// </remarks>
    private static string? Comment(DbDataReader reader, int ordinal, TableType type)
    {
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        var comment = reader.GetString(ordinal);
        if (string.IsNullOrWhiteSpace(comment))
        {
            return null;
        }

        return type == TableType.View && comment.Trim().Equals("VIEW", StringComparison.OrdinalIgnoreCase)
            ? null
            : comment;
    }
}
