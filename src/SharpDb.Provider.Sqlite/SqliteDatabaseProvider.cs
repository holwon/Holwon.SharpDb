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

    /// <summary>
    /// Finds the object a name refers to, and which kind it is, or nothing when there is none.
    /// </summary>
    /// <remarks>
    /// The existence question is answered here rather than by the pragmas below, because a pragma
    /// asked about a name that does not exist answers with no rows and no error — the same answer
    /// it gives for an object that exists and simply has nothing to report. The schema table is the
    /// one source that distinguishes the two, and it also carries the kind in the same row.
    /// <para>
    /// The name is matched case-insensitively, as the engine matches identifiers, so a caller may
    /// ask for <c>Users</c> and be given <c>users</c>. The engine's own objects are excluded, so the
    /// bookkeeping tables behind <c>AUTOINCREMENT</c> cannot be described as if they were the
    /// database's own. <c>COLLATE NOCASE</c> folds ASCII case only, which is the engine's own limit.
    /// </para>
    /// </remarks>
    private const string DescribeExistsSql = """
        SELECT type, name
        FROM sqlite_schema
        WHERE name = @table COLLATE NOCASE
          AND type IN ('table', 'view')
          AND name NOT LIKE 'sqlite\_%' ESCAPE '\'
        ORDER BY CASE WHEN name = @table THEN 0 ELSE 1 END, name
        LIMIT 1
        """;

    /// <summary>
    /// Reads one row per column, including the ordinal SQLite assigns the column inside the
    /// primary key (zero when the column is not part of it).
    /// </summary>
    /// <remarks>
    /// <c>pragma_table_info</c> reports ordinary columns only: a generated column is omitted, and
    /// this is left as the engine has it rather than widened to <c>pragma_table_xinfo</c>, whose
    /// added column would be reported for what no caller can see.
    /// </remarks>
    private const string DescribeColumnsSql = """
        SELECT name, cid, "type", "notnull", dflt_value, pk
        FROM pragma_table_info(@table)
        ORDER BY cid
        """;

    /// <summary>
    /// Reads one row per column of every foreign key, grouped by the key's <c>id</c>.
    /// </summary>
    /// <remarks>
    /// SQLite names no foreign key, so the <c>id</c> it assigns groups the key's columns and is
    /// also what the Provider derives a name from. <c>to</c> is null where the key points at the
    /// parent's primary key without saying so.
    /// </remarks>
    private const string DescribeForeignKeysSql = """
        SELECT id, seq, "table", "from", "to", on_update, on_delete
        FROM pragma_foreign_key_list(@table)
        ORDER BY id, seq
        """;

    /// <summary>
    /// Reads one row per index that is not the one backing the primary key.
    /// </summary>
    /// <remarks>
    /// A primary key's index is excluded by origin rather than by name, because SQLite names it
    /// <c>sqlite_autoindex_…</c> only when it has to create one — an <c>INTEGER PRIMARY KEY</c>
    /// has no index row at all, while a composite or non-integer key does. The primary key is
    /// reported as its own member, so listing its index here would present one fact twice.
    /// </remarks>
    private const string DescribeIndexesSql = """
        SELECT name, "unique"
        FROM pragma_index_list(@table)
        WHERE origin <> 'pk'
        ORDER BY name
        """;

    /// <summary>
    /// Reads the column of every index that is not the primary key's, in index order.
    /// </summary>
    /// <remarks>
    /// The index name is passed to <c>pragma_index_info</c> from the <c>pragma_index_list</c> row
    /// beside it, so the two pragmas are joined rather than the Provider looping over names and
    /// quoting each one into a statement. A column that is an expression rather than a column name
    /// is reported with no name and is left out; an index of nothing but expressions therefore
    /// arrives with no columns.
    /// </remarks>
    private const string DescribeIndexColumnsSql = """
        SELECT il.name AS index_name, ii.name AS column_name
        FROM pragma_index_list(@table) AS il
        JOIN pragma_index_info(il.name) AS ii
        WHERE il.origin <> 'pk'
        ORDER BY il.name, ii.seqno
        """;

    /// <inheritdoc/>
    public string Type => "sqlite";

    /// <inheritdoc/>
    /// <remarks>
    /// The generic dialect, which is what this engine's SQL has always been read with: it accepts
    /// double-quoted identifiers, and it refuses SQLite's <c>[bracketed]</c> form, an accepted gap
    /// (ADR 0005).
    /// </remarks>
    public ReadOnlyPolicy Policy => SharedReadOnlyPolicy.Generic;

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

        var (columns, primaryKey) = await ReadColumnsAsync(connection, target.Name, cancellationToken);
        var foreignKeys = await ReadForeignKeysAsync(connection, target.Name, cancellationToken);
        var indexes = await ReadIndexesAsync(connection, target.Name, cancellationToken);

        return new TableSchema(target.Name, target.Type, Comment: null, columns, primaryKey, foreignKeys, indexes);
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
    /// A column as SQLite describes it, before the Provider has judged what it means.
    /// </summary>
    /// <param name="Name">The column's name.</param>
    /// <param name="Ordinal">The column's 1-based position in the declaration order.</param>
    /// <param name="DataType">The declared type, which may be the empty string.</param>
    /// <param name="IsNullable">Whether the column accepts null.</param>
    /// <param name="DefaultValue">The default expression's text, or <see langword="null"/>.</param>
    /// <param name="KeyOrder">
    /// The column's one-based position in the primary key, or zero when it is not part of it.
    /// </param>
    /// <remarks>
    /// Autoincrement is left out deliberately: it cannot be judged a column at a time, because
    /// whether SQLite generates a key's value depends on the key being one column as much as on
    /// that column's declared type. This record carries what the engine said; the meaning is
    /// decided once the whole table is in hand.
    /// </remarks>
    private sealed record ColumnRow(
        string Name,
        int Ordinal,
        string DataType,
        bool IsNullable,
        string? DefaultValue,
        int KeyOrder);

    /// <summary>
    /// Finds the table or view a caller named.
    /// </summary>
    /// <param name="connection">An open connection to the database.</param>
    /// <param name="tableName">The name the caller asked for, in whatever case.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <returns>
    /// The object's kind and its name as the engine spells it, or <see langword="null"/> when the
    /// database holds no such table or view.
    /// </returns>
    private static async Task<(TableType Type, string Name)?> FindObjectAsync(
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

        // The SQL above admits only these two spellings, so a name that is not a view is a table.
        var type = reader.GetString(0).Equals("view", StringComparison.OrdinalIgnoreCase)
            ? TableType.View
            : TableType.Table;

        return (type, reader.GetString(1));
    }

    /// <summary>
    /// Reads a table's columns, in declaration order.
    /// </summary>
    /// <param name="connection">An open connection to the database.</param>
    /// <param name="tableName">The table's name as the engine spells it.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>
    /// The columns, with their autoincrement meaning resolved, and the primary key's column names
    /// in key order.
    /// </returns>
    /// <remarks>
    /// Autoincrement is computed here, because SQLite records no such flag. The rule it follows is
    /// the engine's own: a value is generated for a key that is exactly one column, declared
    /// <c>INTEGER</c>, and that column is the key's only member. A composite key is therefore never
    /// autoincrement even though its first column carries key position one, and a <c>BIGINT</c> or
    /// <c>VARCHAR</c> key is never one either, because only the exact type name <c>INTEGER</c>
    /// makes a column an alias for the row id.
    /// <para>
    /// The primary key is a sequence rather than a set, because a composite key's order is part of
    /// the key. SQLite reports a column's position within the key in the same row as its own
    /// ordinal — key position one belongs to the first key column, which is usually but not
    /// necessarily the first column of the table — so the key is ordered by that position.
    /// </para>
    /// </remarks>
    private static async Task<(IReadOnlyList<ColumnInfo> Columns, IReadOnlyList<string> PrimaryKey)> ReadColumnsAsync(
        DbConnection connection,
        string tableName,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = DescribeColumnsSql;
        AddTableParameter(command, tableName);

        var rows = new List<ColumnRow>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new ColumnRow(
                Name: reader.GetString(0),
                Ordinal: reader.GetInt32(1) + 1,
                DataType: reader.GetString(2),
                IsNullable: reader.GetInt32(3) == 0,
                DefaultValue: reader.IsDBNull(4) ? null : reader.GetString(4),
                KeyOrder: reader.GetInt32(5)));
        }

        var key = rows.Where(row => row.KeyOrder > 0).OrderBy(row => row.KeyOrder).ToArray();
        var onlyKeyColumn = key.Length == 1 ? key[0].Name : null;

        var columns = rows.Select(row => new ColumnInfo(
            row.Name,
            row.Ordinal,
            row.DataType,
            row.IsNullable,
            row.DefaultValue,
            IsAutoIncrement: row.Name == onlyKeyColumn
                && row.DataType.Equals("INTEGER", StringComparison.OrdinalIgnoreCase),
            Comment: null)).ToArray();

        return (columns, key.Select(row => row.Name).ToArray());
    }

    /// <summary>
    /// Reads a table's foreign keys.
    /// </summary>
    /// <param name="connection">An open connection to the database.</param>
    /// <param name="tableName">The table's name as the engine spells it.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>One entry per foreign key.</returns>
    /// <remarks>
    /// A key's columns arrive as one row each and are grouped by the <c>id</c> SQLite assigned,
    /// since the schema names no key for the Provider to group by. The referenced columns are kept
    /// only where the schema named them: a key that points at the parent's primary key implicitly
    /// reports none, and an empty list says exactly that.
    /// </remarks>
    private static async Task<IReadOnlyList<ForeignKeyInfo>> ReadForeignKeysAsync(
        DbConnection connection,
        string tableName,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = DescribeForeignKeysSql;
        AddTableParameter(command, tableName);

        var rows = new List<(int Id, string Table, string LocalColumn, string? ReferencedColumn, string OnUpdate, string OnDelete)>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add((
                Id: reader.GetInt32(0),
                Table: reader.GetString(2),
                LocalColumn: reader.GetString(3),
                ReferencedColumn: reader.IsDBNull(4) ? null : reader.GetString(4),
                OnUpdate: reader.GetString(5),
                OnDelete: reader.GetString(6)));
        }

        return rows
            .GroupBy(row => row.Id)
            .OrderBy(group => group.Key)
            .Select(group => new ForeignKeyInfo(
                Name: $"fk_{tableName}_{group.Key}",
                Columns: group.Select(row => row.LocalColumn).ToArray(),
                ReferencedTable: group.First().Table,
                ReferencedColumns: group.Where(row => row.ReferencedColumn is not null)
                    .Select(row => row.ReferencedColumn!)
                    .ToArray(),
                OnDelete: Action(group.First().OnDelete),
                OnUpdate: Action(group.First().OnUpdate)))
            .ToArray();
    }

    /// <summary>
    /// Reads a table's indexes, excluding the one backing the primary key.
    /// </summary>
    /// <param name="connection">An open connection to the database.</param>
    /// <param name="tableName">The table's name as the engine spells it.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>One entry per index, ordered by name.</returns>
    /// <remarks>
    /// A column that is an expression rather than a column name has no name to report and is left
    /// out, so an index of nothing but expressions arrives with no columns. The expression's text
    /// is deliberately not recovered from the <c>CREATE INDEX</c> statement: it is the engine's, not
    /// a column name a caller could write in a query.
    /// </remarks>
    private static async Task<IReadOnlyList<IndexInfo>> ReadIndexesAsync(
        DbConnection connection,
        string tableName,
        CancellationToken cancellationToken)
    {
        var indexes = new List<(string Name, bool IsUnique)>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = DescribeIndexesSql;
            AddTableParameter(command, tableName);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                indexes.Add((reader.GetString(0), reader.GetInt32(1) != 0));
            }
        }

        var columns = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = DescribeIndexColumnsSql;
            AddTableParameter(command, tableName);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                // An expression's column is reported with no name, and a nameless column is not a
                // column a caller can use.
                if (reader.IsDBNull(1))
                {
                    continue;
                }

                var index = reader.GetString(0);
                if (!columns.TryGetValue(index, out var names))
                {
                    names = [];
                    columns[index] = names;
                }

                names.Add(reader.GetString(1));
            }
        }

        return indexes
            .Select(index => new IndexInfo(
                index.Name,
                index.IsUnique,
                columns.TryGetValue(index.Name, out var names) ? names : []))
            .ToArray();
    }

    /// <summary>
    /// Reads one of SQLite's five referential actions.
    /// </summary>
    /// <param name="action">The action's name as the engine reports it.</param>
    /// <returns>The action the Schema model names the same way.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The engine reported a name outside the five the SQL standard defines, which would mean this
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
}
