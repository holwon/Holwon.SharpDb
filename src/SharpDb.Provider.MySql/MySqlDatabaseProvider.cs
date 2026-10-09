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

    /// <summary>
    /// Finds the object a name refers to, and which kind it is, or nothing when there is none.
    /// </summary>
    /// <remarks>
    /// Unlike SQLite, MySQL answers this from the same catalog the columns come from, and a name
    /// that does not exist simply matches no row. The object's comment travels with it, because the
    /// server keeps one where SQLite keeps none.
    /// </remarks>
    private const string DescribeExistsSql = """
        SELECT TABLE_NAME, TABLE_TYPE, TABLE_COMMENT
        FROM INFORMATION_SCHEMA.TABLES
        WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @table
        """;

    /// <summary>
    /// Reads one row per column, in declaration order.
    /// </summary>
    /// <remarks>
    /// <c>COLUMN_TYPE</c> carries the full declaration — <c>bigint(20) unsigned</c> — which is what
    /// the Schema model wants; the base type in <c>DATA_TYPE</c> would throw the width and
    /// signedness away. <c>IS_NULLABLE</c> is the string <c>YES</c>/<c>NO</c> rather than a boolean,
    /// and <c>EXTRA</c> is where the server reports <c>auto_increment</c> among other flags.
    /// </remarks>
    private const string DescribeColumnsSql = """
        SELECT COLUMN_NAME, ORDINAL_POSITION, COLUMN_TYPE, IS_NULLABLE, COLUMN_DEFAULT, EXTRA, COLUMN_COMMENT
        FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @table
        ORDER BY ORDINAL_POSITION
        """;

    /// <summary>
    /// Reads the primary key's columns in key order.
    /// </summary>
    /// <remarks>
    /// MySQL always names the primary key's constraint <c>PRIMARY</c>.
    /// </remarks>
    private const string DescribePrimaryKeySql = """
        SELECT COLUMN_NAME
        FROM INFORMATION_SCHEMA.KEY_COLUMN_USAGE
        WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @table AND CONSTRAINT_NAME = 'PRIMARY'
        ORDER BY ORDINAL_POSITION
        """;

    /// <summary>
    /// Reads one row per column of every foreign key, grouped by the constraint's name.
    /// </summary>
    /// <remarks>
    /// <c>KEY_COLUMN_USAGE</c> carries the columns and <c>REFERENTIAL_CONSTRAINTS</c> carries the
    /// actions, so the two are joined on the constraint's name and schema. Rows with no referenced
    /// table are key and index rows, not foreign keys, and are excluded.
    /// </remarks>
    private const string DescribeForeignKeysSql = """
        SELECT kcu.CONSTRAINT_NAME, kcu.COLUMN_NAME, kcu.REFERENCED_TABLE_NAME,
               kcu.REFERENCED_COLUMN_NAME, rc.UPDATE_RULE, rc.DELETE_RULE
        FROM INFORMATION_SCHEMA.KEY_COLUMN_USAGE kcu
        JOIN INFORMATION_SCHEMA.REFERENTIAL_CONSTRAINTS rc
          ON kcu.CONSTRAINT_NAME = rc.CONSTRAINT_NAME
         AND kcu.TABLE_SCHEMA = rc.CONSTRAINT_SCHEMA
        WHERE kcu.TABLE_SCHEMA = DATABASE() AND kcu.TABLE_NAME = @table
          AND kcu.REFERENCED_TABLE_NAME IS NOT NULL
        ORDER BY kcu.CONSTRAINT_NAME, kcu.ORDINAL_POSITION
        """;

    /// <summary>
    /// Reads one row per column of every index except the primary key's.
    /// </summary>
    /// <remarks>
    /// MySQL names the primary key's index <c>PRIMARY</c>, and it is excluded because the Schema
    /// model reports the primary key as its own member. <c>NON_UNIQUE</c> is <c>0</c> for a unique
    /// index rather than a boolean.
    /// </remarks>
    private const string DescribeIndexesSql = """
        SELECT INDEX_NAME, NON_UNIQUE, COLUMN_NAME
        FROM INFORMATION_SCHEMA.STATISTICS
        WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @table AND INDEX_NAME <> 'PRIMARY'
        ORDER BY INDEX_NAME, SEQ_IN_INDEX
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
    public async Task<TableSchema?> DescribeTableAsync(
        DbConnection connection,
        string tableName,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(tableName);

        var found = await FindObjectAsync(connection, tableName, cancellationToken);
        if (found is not { } target)
        {
            return null;
        }

        // Every lookup below goes through the name the catalog spelled, not the name the caller
        // typed: on a case-insensitive server the two can differ, and the catalog is the one the
        // WHERE clauses must match.
        var columns = await ReadColumnsAsync(connection, target.Name, cancellationToken);
        var primaryKey = await ReadPrimaryKeyAsync(connection, target.Name, cancellationToken);
        var foreignKeys = await ReadForeignKeysAsync(connection, target.Name, cancellationToken);
        var indexes = await ReadIndexesAsync(connection, target.Name, cancellationToken);

        return new TableSchema(target.Name, target.Type, target.Comment, columns, primaryKey, foreignKeys, indexes);
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
    /// The object a described name refers to, as the catalog spells it.
    /// </summary>
    /// <param name="Name">The name the catalog stores.</param>
    /// <param name="Type">Whether the object is a table or a view.</param>
    /// <param name="Comment">The object's comment, or <see langword="null"/> when it has none.</param>
    private sealed record DescribeObject(string Name, TableType Type, string? Comment);

    /// <summary>
    /// Finds the table or view a caller named.
    /// </summary>
    /// <param name="connection">An open connection to the database.</param>
    /// <param name="tableName">The name the caller asked for.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <returns>The object, or <see langword="null"/> when the database holds no such table or view.</returns>
    private static async Task<DescribeObject?> FindObjectAsync(
        DbConnection connection,
        string tableName,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = DescribeExistsSql;
        AddTableParameter(command, tableName);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var type = reader.GetString(1).Equals("VIEW", StringComparison.OrdinalIgnoreCase)
            ? TableType.View
            : TableType.Table;

        return new DescribeObject(reader.GetString(0), type, Comment(reader, 2, type));
    }

    /// <summary>
    /// Reads a table's columns, in declaration order.
    /// </summary>
    /// <param name="connection">An open connection to the database.</param>
    /// <param name="tableName">The table's name as the catalog spells it.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The columns.</returns>
    private static async Task<IReadOnlyList<ColumnInfo>> ReadColumnsAsync(
        DbConnection connection,
        string tableName,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = DescribeColumnsSql;
        AddTableParameter(command, tableName);

        var columns = new List<ColumnInfo>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var comment = reader.IsDBNull(6) ? null : reader.GetString(6);

            columns.Add(new ColumnInfo(
                Name: reader.GetString(0),
                Ordinal: reader.GetInt32(1),
                DataType: reader.GetString(2),
                IsNullable: reader.GetString(3).Equals("YES", StringComparison.OrdinalIgnoreCase),
                DefaultValue: reader.IsDBNull(4) ? null : reader.GetString(4),
                IsAutoIncrement: IsAutoIncrement(reader.GetString(5)),
                Comment: string.IsNullOrWhiteSpace(comment) ? null : comment));
        }

        return columns;
    }

    /// <summary>
    /// Reads a table's primary key, in key order.
    /// </summary>
    /// <param name="connection">An open connection to the database.</param>
    /// <param name="tableName">The table's name as the catalog spells it.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The key's column names, ordered by their position in the key.</returns>
    private static async Task<IReadOnlyList<string>> ReadPrimaryKeyAsync(
        DbConnection connection,
        string tableName,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = DescribePrimaryKeySql;
        AddTableParameter(command, tableName);

        var columns = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            columns.Add(reader.GetString(0));
        }

        return columns;
    }

    /// <summary>
    /// Reads a table's foreign keys.
    /// </summary>
    /// <param name="connection">An open connection to the database.</param>
    /// <param name="tableName">The table's name as the catalog spells it.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>One entry per foreign key.</returns>
    private static async Task<IReadOnlyList<ForeignKeyInfo>> ReadForeignKeysAsync(
        DbConnection connection,
        string tableName,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = DescribeForeignKeysSql;
        AddTableParameter(command, tableName);

        var rows = new List<(string Name, string Table, string LocalColumn, string ReferencedColumn, string OnUpdate, string OnDelete)>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add((
                Name: reader.GetString(0),
                Table: reader.GetString(2),
                LocalColumn: reader.GetString(1),
                ReferencedColumn: reader.GetString(3),
                OnUpdate: reader.GetString(4),
                OnDelete: reader.GetString(5)));
        }

        return rows
            .GroupBy(row => row.Name)
            .Select(group => new ForeignKeyInfo(
                Name: group.Key,
                Columns: group.Select(row => row.LocalColumn).ToArray(),
                ReferencedTable: group.First().Table,
                // MySQL always records the referenced columns, so none of them is absent.
                ReferencedColumns: group.Select(row => row.ReferencedColumn).ToArray(),
                OnDelete: Action(group.First().OnDelete),
                OnUpdate: Action(group.First().OnUpdate)))
            .ToArray();
    }

    /// <summary>
    /// Reads a table's indexes, excluding the one backing the primary key.
    /// </summary>
    /// <param name="connection">An open connection to the database.</param>
    /// <param name="tableName">The table's name as the catalog spells it.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>One entry per index.</returns>
    /// <remarks>
    /// A column that is an expression rather than a column name is reported with no name and is
    /// left out, so an index of nothing but expressions arrives with no columns.
    /// </remarks>
    private static async Task<IReadOnlyList<IndexInfo>> ReadIndexesAsync(
        DbConnection connection,
        string tableName,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = DescribeIndexesSql;
        AddTableParameter(command, tableName);

        var rows = new List<(string Name, bool IsUnique, string? Column)>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add((
                Name: reader.GetString(0),
                IsUnique: reader.GetInt32(1) == 0,
                Column: reader.IsDBNull(2) ? null : reader.GetString(2)));
        }

        return rows
            .GroupBy(row => row.Name)
            .Select(group => new IndexInfo(
                group.Key,
                group.First().IsUnique,
                group.Where(row => row.Column is not null).Select(row => row.Column!).ToArray()))
            .ToArray();
    }

    /// <summary>
    /// Reads whether a column's value is generated on insert.
    /// </summary>
    /// <param name="extra">The column's <c>EXTRA</c> text, a space-separated flag list.</param>
    /// <returns><see langword="true"/> when the column is auto-increment.</returns>
    /// <remarks>
    /// <c>EXTRA</c> holds several flags at once — <c>auto_increment</c>, <c>on update
    /// CURRENT_TIMESTAMP</c>, the generated-column markers — so the flag is looked for as a whole
    /// token rather than as a substring that a longer flag could also contain.
    /// </remarks>
    private static bool IsAutoIncrement(string extra) =>
        extra.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Contains("auto_increment", StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Reads one of the five referential actions.
    /// </summary>
    /// <param name="action">The action's name as the server reports it.</param>
    /// <returns>The action the Schema model names the same way.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The server reported a name outside the five the SQL standard defines, which would mean this
    /// mapping needs extending rather than that a caller did something wrong.
    /// </exception>
    private static ForeignKeyAction Action(string action) => action.ToUpperInvariant() switch
    {
        "CASCADE" => ForeignKeyAction.Cascade,
        "SET NULL" => ForeignKeyAction.SetNull,
        "SET DEFAULT" => ForeignKeyAction.SetDefault,
        "RESTRICT" => ForeignKeyAction.Restrict,
        "NO ACTION" => ForeignKeyAction.NoAction,
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unknown referential action."),
    };

    /// <summary>
    /// Binds a table or view name to the parameter every describe statement names it by.
    /// </summary>
    /// <param name="command">The command to bind to.</param>
    /// <param name="tableName">The name to bind.</param>
    private static void AddTableParameter(DbCommand command, string tableName)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@table";
        parameter.Value = tableName;
        command.Parameters.Add(parameter);
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
