using System.Data.Common;
using Microsoft.Data.Sqlite;
using SharpDb.Abstractions;
using SharpDb.Abstractions.Schema;

namespace SharpDb.Provider.Sqlite;

/// <summary>
/// Serves SQLite databases, reaching one exclusively through the SQLite Driver this project owns.
/// </summary>
public sealed class SqliteDatabaseProvider : IDatabaseProvider
{
    /// <summary>
    /// Lists the tables and views in the connection's database.
    /// </summary>
    /// <remarks>
    /// The schema table holds one row per object, and its <c>type</c> column already says which
    /// kind each one is. Objects whose names begin <c>sqlite_</c> belong to the engine rather than
    /// to the database — the bookkeeping tables behind <c>AUTOINCREMENT</c> and the statistics
    /// tables the query planner keeps — so they are left out. Measured: a database with an
    /// <c>AUTOINCREMENT</c> table lists <c>sqlite_sequence</c> unless this filter is applied.
    /// <para>
    /// The underscore is escaped. It is a single-character wildcard in <c>LIKE</c>, so the obvious
    /// pattern would also swallow a genuine table a user had happened to name <c>sqliteX…</c>.
    /// </para>
    /// </remarks>
    private const string ListTablesSql = """
        SELECT name, type
        FROM sqlite_schema
        WHERE type IN ('table', 'view') AND name NOT LIKE 'sqlite\_%' ESCAPE '\'
        ORDER BY name
        """;

    /// <summary>
    /// Lists the databases the connection can see.
    /// </summary>
    /// <remarks>
    /// The pragma's table-valued form is used throughout this Provider rather than the bare
    /// <c>PRAGMA</c> statement, because a bind parameter can be passed to the function form and
    /// cannot be passed to the statement form (ADR 0005).
    /// </remarks>
    private const string ListDatabasesSql = "SELECT name FROM pragma_database_list ORDER BY name";

    /// <inheritdoc/>
    public string Type => "sqlite";

    /// <inheritdoc/>
    /// <remarks>
    /// Read-only is expressed in the connection string itself, so the engine opens the file
    /// read-only and the mode is in force before a single statement runs (ADR 0002, layer 2).
    /// </remarks>
    public async Task<DbConnection> CreateReadOnlyConnectionAsync(
        string connectionString,
        CancellationToken cancellationToken)
    {
        var builder = new SqliteConnectionStringBuilder(connectionString)
        {
            Mode = SqliteOpenMode.ReadOnly,
        };

        var connection = new SqliteConnection(builder.ConnectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
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
            // The SQL above admits only these two spellings, so a name that is not a view is a table.
            var type = reader.GetString(1).Equals("view", StringComparison.OrdinalIgnoreCase)
                ? TableType.View
                : TableType.Table;

            // SQLite keeps no object comments, so this is never populated for this Provider.
            tables.Add(new TableInfo(reader.GetString(0), type, Comment: null));
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
}
