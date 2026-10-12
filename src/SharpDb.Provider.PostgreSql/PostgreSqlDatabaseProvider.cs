using System.Data.Common;
using Npgsql;
using SharpDb.Abstractions;
using SharpDb.Abstractions.Schema;

namespace SharpDb.Provider.PostgreSql;

/// <summary>
/// Serves PostgreSQL databases, reaching them exclusively through the Driver this project owns.
/// </summary>
/// <remarks>
/// Introspection reads the system catalogues rather than <c>information_schema</c>: the catalogue
/// is where a declaration's full text, a constraint's column order and an index's expression all
/// survive, while the standard views break them into columns that have to be reassembled and, for
/// a composite foreign key, lose the pairing altogether.
/// <para>
/// The objects reported are the ones the connection's <c>search_path</c> reaches, which is exactly
/// the set a name may be used for without qualifying it: an agent that reads <c>users</c> here and
/// writes <c>FROM users</c> gets an answer rather than a resolution error.
/// </para>
/// </remarks>
public sealed class PostgreSqlDatabaseProvider : IDatabaseProvider
{
    /// <summary>
    /// Puts the session's transactions in read-only mode; every write is refused by the server.
    /// </summary>
    /// <remarks>
    /// This is the spelling that works, and the one that looks right but does not is worth naming:
    /// <c>SET SESSION TRANSACTION READ ONLY</c> — which is what MySQL's Provider sends — is
    /// accepted by PostgreSQL and applies <em>only to the current transaction</em>. Under the
    /// Driver's own autocommit that transaction has already ended, so the statement is granted,
    /// the next <c>INSERT</c> succeeds, and a Provider that had sent it would look read-only while
    /// being nothing of the kind. Measured on PostgreSQL 16.15.
    /// <para>
    /// <c>CHARACTERISTICS</c> is what makes the setting a property of the session, so it outlives
    /// the transaction that set it.
    /// </para>
    /// </remarks>
    private const string EnterReadOnlySession = "SET SESSION CHARACTERISTICS AS TRANSACTION READ ONLY";

    /// <summary>
    /// The schemas whose objects belong to the server rather than to the database.
    /// </summary>
    /// <remarks>
    /// They have to be excluded by name, because <c>pg_table_is_visible</c> answers about the
    /// search path and the search path reaches them: measured on PostgreSQL 16.15, the visibility
    /// test alone puts 140 system objects into a listing that should hold the database's own.
    /// </remarks>
    private const string SystemSchemas = "('pg_catalog', 'information_schema')";

    /// <summary>
    /// Lists the tables and views the connection's search path reaches.
    /// </summary>
    /// <remarks>
    /// <c>relkind</c> distinguishes the kinds: <c>r</c> is an ordinary table, <c>p</c> a
    /// partitioned one, <c>f</c> a foreign one, and <c>v</c> and <c>m</c> are the two kinds of
    /// view. All of them are things a caller reaches by name, so all of them are listed.
    /// </remarks>
    private const string ListTablesSql = """
        SELECT c.relname,
               CASE WHEN c.relkind IN ('v', 'm') THEN 'view' ELSE 'table' END AS kind,
               obj_description(c.oid, 'pg_class') AS comment
        FROM pg_class c
        JOIN pg_namespace n ON n.oid = c.relnamespace
        WHERE c.relkind IN ('r', 'p', 'f', 'v', 'm')
          AND pg_table_is_visible(c.oid)
          AND n.nspname NOT IN ('pg_catalog', 'information_schema')
        ORDER BY c.relname
        """;

    /// <summary>
    /// Lists the databases this connection can see.
    /// </summary>
    /// <remarks>
    /// The template databases are left out: <c>template0</c> and <c>template1</c> are the server's
    /// own moulds for new databases rather than places a caller could mean, and they are visible
    /// to every account, so listing them would add two names to every answer that no caller can
    /// act on. <c>datallowconn</c> filters what an account may not connect to anyway.
    /// </remarks>
    private const string ListDatabasesSql = """
        SELECT datname
        FROM pg_database
        WHERE NOT datistemplate AND datallowconn
        ORDER BY datname
        """;

    /// <summary>
    /// Finds the object a name refers to, and which kind it is, or nothing when there is none.
    /// </summary>
    /// <remarks>
    /// The name is matched the way the engine matches an identifier, which means two comparisons
    /// rather than one. A caller writing <c>Users</c> in unquoted SQL reaches <c>users</c>, because
    /// PostgreSQL folds an unquoted identifier to lower case — so the requested name folded to
    /// lower case has to match too. A caller writing <c>"MixedCase"</c> reaches only the object
    /// spelled exactly that way, because a quoted identifier is not folded — so the name as asked
    /// has to match as well.
    /// <para>
    /// An exact match wins over a folded one, because a caller who spelled the name the catalogue's
    /// way meant that object. Both are tried rather than one being derived from the other: the two
    /// are separate spellings of a query, not two ways of writing one name.
    /// </para>
    /// <para>
    /// The object's own <c>oid</c> comes back with it, because every read below keys on it: names
    /// are not unique across schemas, and the catalogue's identifier is what identifies a relation.
    /// </para>
    /// </remarks>
    private const string DescribeExistsSql = """
        SELECT c.oid, c.relname,
               CASE WHEN c.relkind IN ('v', 'm') THEN 'view' ELSE 'table' END AS kind,
               obj_description(c.oid, 'pg_class') AS comment
        FROM pg_class c
        JOIN pg_namespace n ON n.oid = c.relnamespace
        WHERE c.relkind IN ('r', 'p', 'f', 'v', 'm')
          AND pg_table_is_visible(c.oid)
          AND n.nspname NOT IN ('pg_catalog', 'information_schema')
          AND (c.relname = @table OR c.relname = lower(@table))
        ORDER BY CASE WHEN c.relname = @table THEN 0 ELSE 1 END, c.relname
        LIMIT 1
        """;

    /// <summary>
    /// Reads one row per column, in declaration order.
    /// </summary>
    /// <remarks>
    /// <c>format_type</c> is asked for the declaration rather than the base type: it returns
    /// <c>character varying(255)</c>, <c>numeric(10,2)</c> and <c>timestamp with time zone</c> as
    /// written, which is what a caller needs in order to predict how a value will be treated.
    /// <c>pg_get_expr</c> gives the default as the expression text the server stores.
    /// <para>
    /// Autoincrement is answered by two facts rather than one. <c>attidentity</c> is set for a
    /// column declared <c>GENERATED … AS IDENTITY</c>, and a column declared <c>serial</c> — the
    /// older spelling, still everywhere — keeps no flag at all and is recognisable only by a
    /// default of <c>nextval(…)</c>. Both are read here and judged from the row.
    /// </para>
    /// <para>
    /// <c>attidentity</c> is cast to text because PostgreSQL declares it as its own one-byte
    /// <c>"char"</c> type, which the Driver will not read back as a string — measured: it raises
    /// rather than converting. The cast asks the server for the same value in a type the Driver
    /// and the provider agree on.
    /// </para>
    /// </remarks>
    private const string DescribeColumnsSql = """
        SELECT a.attname,
               a.attnum,
               format_type(a.atttypid, a.atttypmod) AS declared,
               a.attnotnull,
               pg_get_expr(d.adbin, d.adrelid) AS default_expression,
               a.attidentity::text AS attidentity,
               col_description(a.attrelid, a.attnum) AS comment
        FROM pg_attribute a
        LEFT JOIN pg_attrdef d ON d.adrelid = a.attrelid AND d.adnum = a.attnum
        WHERE a.attrelid = @oid
          AND a.attnum > 0
          AND NOT a.attisdropped
        ORDER BY a.attnum
        """;

    /// <summary>
    /// Reads the primary key's columns in key order.
    /// </summary>
    /// <remarks>
    /// <c>conkey</c> is an array, and its order <em>is</em> the key's order: the key declared
    /// <c>(b, a)</c> arrives as <c>{b, a}</c> even though the columns are declared the other way
    /// round. Unnesting with an ordinal carries that order out of the array and keeps the answer
    /// about the key rather than about the table.
    /// </remarks>
    private const string DescribePrimaryKeySql = """
        SELECT att.attname
        FROM pg_constraint con
        CROSS JOIN LATERAL unnest(con.conkey) WITH ORDINALITY AS k(attnum, ord)
        JOIN pg_attribute att ON att.attrelid = con.conrelid AND att.attnum = k.attnum
        WHERE con.conrelid = @oid AND con.contype = 'p'
        ORDER BY k.ord
        """;

    /// <summary>
    /// Reads one row per column of every foreign key, with its target and its actions.
    /// </summary>
    /// <remarks>
    /// <c>unnest(conkey, confkey)</c> walks the two arrays in step, so row <em>n</em> pairs the
    /// key's <em>n</em>th local column with the <em>n</em>th referenced one. That is the whole
    /// reason for reading the catalogue here: the standard views report the columns without a
    /// position, and a composite key's pairing cannot be recovered from them.
    /// <para>
    /// The actions arrive as single-character codes rather than as words, and are translated in
    /// code — the mapping is the server's, documented rather than discoverable. They are cast to
    /// text for the same reason <c>attidentity</c> is: the server declares them as its one-byte
    /// <c>"char"</c>, which the Driver will not hand back as a string.
    /// </para>
    /// </remarks>
    private const string DescribeForeignKeysSql = """
        SELECT con.conname,
               ref.relname AS referenced_table,
               local_att.attname AS local_column,
               ref_att.attname AS referenced_column,
               con.confdeltype::text AS on_delete,
               con.confupdtype::text AS on_update,
               k.ord
        FROM pg_constraint con
        JOIN pg_class ref ON ref.oid = con.confrelid
        CROSS JOIN LATERAL unnest(con.conkey, con.confkey) WITH ORDINALITY AS k(local_attnum, ref_attnum, ord)
        JOIN pg_attribute local_att ON local_att.attrelid = con.conrelid AND local_att.attnum = k.local_attnum
        JOIN pg_attribute ref_att ON ref_att.attrelid = con.confrelid AND ref_att.attnum = k.ref_attnum
        WHERE con.conrelid = @oid AND con.contype = 'f'
        ORDER BY con.conname, k.ord
        """;

    /// <summary>
    /// Reads one row per indexed column of every index except the one backing the primary key.
    /// </summary>
    /// <remarks>
    /// <c>indisprimary</c> is what excludes that index: PostgreSQL names it after the table rather
    /// than after a convention, so its name says nothing about what it backs, and the ledger is
    /// what does. The primary key is reported as its own member, so repeating its index here would
    /// present one fact as two.
    /// <para>
    /// A column of an index may be an expression rather than a column, and an expression has no
    /// name: <c>attnum</c> is zero for it, no catalogue row matches, and the name comes back null.
    /// It is dropped, so an index of nothing but expressions arrives with no columns — the same
    /// answer the other engines give.
    /// </para>
    /// </remarks>
    private const string DescribeIndexesSql = """
        SELECT i.relname AS index_name,
               ix.indisunique,
               att.attname AS column_name,
               k.ord
        FROM pg_index ix
        JOIN pg_class i ON i.oid = ix.indexrelid
        CROSS JOIN LATERAL unnest(ix.indkey) WITH ORDINALITY AS k(attnum, ord)
        LEFT JOIN pg_attribute att
               ON att.attrelid = ix.indrelid AND att.attnum = k.attnum AND k.attnum > 0
        WHERE ix.indrelid = @oid AND NOT ix.indisprimary
        ORDER BY i.relname, k.ord
        """;

    /// <inheritdoc/>
    public string Type => "postgresql";

    /// <inheritdoc/>
    /// <remarks>
    /// PostgreSQL's own dialect, which is what the Guard needs in order to read the engine's
    /// ordinary SQL: measured, the generic dialect cannot parse <c>FILTER (WHERE …)</c>, a
    /// collective form that appears in perfectly read-only aggregates. The denied functions are
    /// the ones that read the server's own files — and they matter more here than anywhere else,
    /// because measured on 16.15 those functions still work inside a read-only session.
    /// </remarks>
    public ReadOnlyPolicy Policy => PostgreSqlReadOnlyPolicy.Definition;

    /// <inheritdoc/>
    /// <remarks>
    /// Read-only is switched on immediately after connecting and before the connection is handed
    /// out, so the mode is in force before a single statement runs (ADR 0002, layer 2).
    /// </remarks>
    public async Task<DbConnection> CreateReadOnlyConnectionAsync(
        string connectionString,
        CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(connectionString);
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
            var type = reader.GetString(1).Equals("view", StringComparison.Ordinal)
                ? TableType.View
                : TableType.Table;

            tables.Add(new TableInfo(reader.GetString(0), type, CommentAt(reader, 2)));
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

        var target = await FindObjectAsync(connection, tableName, cancellationToken);
        if (target is not { } found)
        {
            return null;
        }

        var columns = await ReadColumnsAsync(connection, found.Oid, cancellationToken);
        var primaryKey = await ReadPrimaryKeyAsync(connection, found.Oid, cancellationToken);
        var foreignKeys = await ReadForeignKeysAsync(connection, found.Oid, cancellationToken);
        var indexes = await ReadIndexesAsync(connection, found.Oid, cancellationToken);

        return new TableSchema(found.Name, found.Type, found.Comment, columns, primaryKey, foreignKeys, indexes);
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
    /// The object a described name refers to, as the catalogue spells it.
    /// </summary>
    /// <param name="Oid">The relation's catalogue identifier, which every read below keys on.</param>
    /// <param name="Name">The name the catalogue stores.</param>
    /// <param name="Type">Whether the object is a table or a view.</param>
    /// <param name="Comment">The object's comment, or <see langword="null"/> when it has none.</param>
    private sealed record DescribeObject(uint Oid, string Name, TableType Type, string? Comment);

    /// <summary>
    /// Finds the table or view a caller named.
    /// </summary>
    /// <param name="connection">An open connection to the database.</param>
    /// <param name="tableName">The name the caller asked for, in whatever case.</param>
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

        var type = reader.GetString(2).Equals("view", StringComparison.Ordinal)
            ? TableType.View
            : TableType.Table;

        return new DescribeObject(reader.GetFieldValue<uint>(0), reader.GetString(1), type, CommentAt(reader, 3));
    }

    /// <summary>
    /// Reads a table's columns, in declaration order.
    /// </summary>
    /// <param name="connection">An open connection to the database.</param>
    /// <param name="oid">The relation's catalogue identifier.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The columns.</returns>
    private static async Task<IReadOnlyList<ColumnInfo>> ReadColumnsAsync(
        DbConnection connection,
        uint oid,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = DescribeColumnsSql;
        AddOidParameter(command, oid);

        var columns = new List<ColumnInfo>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var defaultValue = reader.IsDBNull(4) ? null : reader.GetString(4);
            var identity = reader.IsDBNull(5) ? string.Empty : reader.GetString(5);

            columns.Add(new ColumnInfo(
                Name: reader.GetString(0),
                Ordinal: reader.GetInt32(1),
                DataType: reader.GetString(2),
                IsNullable: !reader.GetBoolean(3),
                DefaultValue: defaultValue,
                IsAutoIncrement: IsAutoIncrement(identity, defaultValue),
                Comment: CommentAt(reader, 6)));
        }

        return columns;
    }

    /// <summary>
    /// Reads a table's primary key, in key order.
    /// </summary>
    /// <param name="connection">An open connection to the database.</param>
    /// <param name="oid">The relation's catalogue identifier.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The key's column names, ordered by their position in the key.</returns>
    private static async Task<IReadOnlyList<string>> ReadPrimaryKeyAsync(
        DbConnection connection,
        uint oid,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = DescribePrimaryKeySql;
        AddOidParameter(command, oid);

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
    /// <param name="oid">The relation's catalogue identifier.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>One entry per foreign key.</returns>
    private static async Task<IReadOnlyList<ForeignKeyInfo>> ReadForeignKeysAsync(
        DbConnection connection,
        uint oid,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = DescribeForeignKeysSql;
        AddOidParameter(command, oid);

        var rows = new List<(string Name, string Table, string Local, string Referenced, string OnDelete, string OnUpdate)>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add((
                Name: reader.GetString(0),
                Table: reader.GetString(1),
                Local: reader.GetString(2),
                Referenced: reader.GetString(3),
                OnDelete: reader.GetString(4),
                OnUpdate: reader.GetString(5)));
        }

        return rows
            .GroupBy(row => row.Name)
            .Select(group => new ForeignKeyInfo(
                Name: group.Key,
                Columns: group.Select(row => row.Local).ToArray(),
                ReferencedTable: group.First().Table,
                ReferencedColumns: group.Select(row => row.Referenced).ToArray(),
                OnDelete: Action(group.First().OnDelete),
                OnUpdate: Action(group.First().OnUpdate)))
            .ToArray();
    }

    /// <summary>
    /// Reads a table's indexes, excluding the one backing the primary key.
    /// </summary>
    /// <param name="connection">An open connection to the database.</param>
    /// <param name="oid">The relation's catalogue identifier.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>One entry per index.</returns>
    private static async Task<IReadOnlyList<IndexInfo>> ReadIndexesAsync(
        DbConnection connection,
        uint oid,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = DescribeIndexesSql;
        AddOidParameter(command, oid);

        var rows = new List<(string Name, bool IsUnique, string? Column)>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add((
                Name: reader.GetString(0),
                IsUnique: reader.GetBoolean(1),
                Column: reader.IsDBNull(2) ? null : reader.GetString(2)));
        }

        return rows
            .GroupBy(row => row.Name)
            .Select(group => new IndexInfo(
                Name: group.Key,
                IsUnique: group.First().IsUnique,
                Columns: group.Where(row => row.Column is not null).Select(row => row.Column!).ToArray()))
            .ToArray();
    }

    /// <summary>
    /// Whether a column's value is generated by the server.
    /// </summary>
    /// <param name="identity">The <c>attidentity</c> flag, empty when the column has none.</param>
    /// <param name="defaultValue">The column's default expression, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the server generates the value.</returns>
    /// <remarks>
    /// Two spellings mean the same thing to a caller. <c>GENERATED … AS IDENTITY</c> sets
    /// <c>attidentity</c>. The older <c>serial</c> is not a type at all — it is a macro that
    /// creates a sequence and a <c>nextval(…)</c> default — so it leaves no flag and is recognised
    /// by the default it wrote. A <c>nextval</c> default written by hand means the same thing and
    /// is answered the same way, which is the honest reading: the server will generate the value
    /// whether or not a macro wrote the clause.
    /// </remarks>
    private static bool IsAutoIncrement(string identity, string? defaultValue) =>
        !string.IsNullOrEmpty(identity)
        || (defaultValue is not null
            && defaultValue.TrimStart().StartsWith("nextval(", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Translates the catalogue's action code into the action it names.
    /// </summary>
    /// <param name="code">The single-character code from <c>pg_constraint</c>.</param>
    /// <returns>The action.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The code is not one the server documents.</exception>
    /// <remarks>
    /// The codes are PostgreSQL's own and are documented rather than guessed. An unrecognised one
    /// fails loudly: a foreign key whose action silently became <c>NoAction</c> would be a lie
    /// about what deleting a row does.
    /// </remarks>
    private static ForeignKeyAction Action(string code) => code switch
    {
        "a" => ForeignKeyAction.NoAction,
        "r" => ForeignKeyAction.Restrict,
        "c" => ForeignKeyAction.Cascade,
        "n" => ForeignKeyAction.SetNull,
        "d" => ForeignKeyAction.SetDefault,
        _ => throw new ArgumentOutOfRangeException(
            nameof(code),
            code,
            "PostgreSQL reported a foreign key action this Provider does not know."),
    };

    /// <summary>
    /// Reads a comment that may be absent.
    /// </summary>
    /// <param name="reader">The reader positioned on a row.</param>
    /// <param name="ordinal">The comment's position in the row.</param>
    /// <returns>The comment, or <see langword="null"/> when the object carries none.</returns>
    /// <remarks>
    /// PostgreSQL already stores "no comment" as a null, so the only judgement left is that a
    /// comment consisting of nothing but whitespace is no more informative than none at all —
    /// which is the same reading the MySQL Provider gives an empty string.
    /// </remarks>
    private static string? CommentAt(DbDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        var comment = reader.GetString(ordinal);

        return string.IsNullOrWhiteSpace(comment) ? null : comment;
    }

    /// <summary>
    /// Binds the name a caller asked for.
    /// </summary>
    /// <param name="command">The command to bind to.</param>
    /// <param name="tableName">The name the caller asked for.</param>
    private static void AddTableParameter(DbCommand command, string tableName)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = "table";
        parameter.Value = tableName;

        command.Parameters.Add(parameter);
    }

    /// <summary>
    /// Binds a relation's catalogue identifier.
    /// </summary>
    /// <param name="command">The command to bind to.</param>
    /// <param name="oid">The relation's <c>oid</c>.</param>
    /// <remarks>
    /// The type is named explicitly, which the Driver requires here and only here: an <c>oid</c>
    /// is an unsigned 32-bit integer, and measured, the Driver refuses to guess a wire type for
    /// <see cref="uint"/> and refuses to send one without being told. The Driver's own type is set
    /// rather than the ADO.NET one, because measured, its ADO mapping rejects
    /// <see cref="DbType.UInt32"/> outright while accepting the driver-specific spelling.
    /// Every other parameter this Provider binds is a string or a signed integer, neither of which
    /// raises the question.
    /// </remarks>
    private static void AddOidParameter(DbCommand command, uint oid)
    {
        command.Parameters.Add(new NpgsqlParameter("oid", NpgsqlTypes.NpgsqlDbType.Oid) { Value = oid });
    }
}
