using System.Data.Common;
using System.Globalization;
using Microsoft.Data.SqlClient;
using SharpDb.Abstractions;
using SharpDb.Abstractions.Schema;

namespace SharpDb.Provider.MsSql;

/// <summary>
/// Serves SQL Server databases, reaching them exclusively through the Driver this project owns.
/// </summary>
/// <remarks>
/// Introspection reads the <c>sys</c> catalogue views rather than <c>information_schema</c>, for
/// the same reason the PostgreSQL Provider reads its catalogues: a column's declared type, a
/// <c>IDENTITY</c> flag, a comment, the order of a composite key's columns and the pairing of a
/// composite foreign key all survive only there. Measured, the standard views lose the pairing
/// outright and report a type without its length.
/// <para>
/// The names reported are the names this engine resolves, which is what makes them usable without
/// qualification: an object in the connection's default schema is reported bare, because
/// <c>FROM users</c> finds it, and an object anywhere else is reported schema-qualified, because
/// only <c>FROM reporting.totals</c> finds that. Measured, <c>OBJECT_ID('users')</c> resolves while
/// <c>OBJECT_ID('totals')</c> does not — which is the engine stating the rule this Provider
/// follows.
/// </para>
/// <para>
/// This Provider has no engine-level read-only mode to open, and says so rather than implying
/// otherwise: <see cref="CreateReadOnlyConnectionAsync"/> records the connection's read-only
/// <em>intent</em>, which is a real boundary only when the target is an Always On readable
/// secondary and is a no-op on a standalone instance (ADR 0016). What actually stands between a
/// bypassed Guard and a write on this engine is the privileges of the account in the connection
/// string, which the caller supplies and this Provider does not inspect.
/// </para>
/// </remarks>
public sealed class MsSqlDatabaseProvider : IDatabaseProvider
{
    /// <summary>
    /// The engine's own name for the four databases every server carries and no caller means.
    /// </summary>
    /// <remarks>
    /// Excluded by name rather than by a flag, because there is no flag: measured, these four
    /// appear in <c>sys.databases</c> beside the databases a caller could actually mean, and no
    /// column distinguishes them from one.
    /// </remarks>
    private const string SystemDatabases = "('master', 'tempdb', 'model', 'msdb')";

    /// <summary>
    /// Lists the tables and views a caller may use, reported under the names it may use.
    /// </summary>
    /// <remarks>
    /// An object in the connection's default schema is reported bare and anything else is
    /// qualified, because that is exactly the set of names this engine resolves: measured,
    /// <c>OBJECT_ID('users')</c> finds the <c>dbo</c> table while <c>OBJECT_ID('totals')</c> does
    /// not find the one in <c>reporting</c>, whose qualified spelling does resolve (ADR 0017).
    /// <para>
    /// <c>is_ms_shipped</c> and the <c>U</c>/<c>V</c> filter are what keep the server's own objects
    /// out. Measured, an unfiltered read of the index catalogue returns hundreds of internal tables
    /// — queues, ledgers and allocation maps — and they are not tables of this database.
    /// </para>
    /// </remarks>
    private const string ListTablesSql = """
        SELECT CASE WHEN s.name = SCHEMA_NAME() THEN o.name ELSE s.name + '.' + o.name END AS reported_name,
               CASE WHEN o.type = 'V' THEN 'view' ELSE 'table' END AS kind,
               CONVERT(NVARCHAR(4000), ep.value) AS comment
        FROM sys.objects AS o
        JOIN sys.schemas AS s ON s.schema_id = o.schema_id
        LEFT JOIN sys.extended_properties AS ep
               ON ep.major_id = o.object_id AND ep.minor_id = 0 AND ep.name = 'MS_Description'
        WHERE o.type IN ('U', 'V') AND o.is_ms_shipped = 0
        ORDER BY reported_name
        """;

    /// <summary>
    /// Lists the databases this connection can see, minus the server's own four.
    /// </summary>
    /// <remarks>
    /// The answer is filtered by the server rather than by the account: measured, a login with no
    /// server-level rights still sees all four system databases, so leaving them in would put four
    /// names into every answer that no caller can mean.
    /// </remarks>
    private const string ListDatabasesSql = $"""
        SELECT name
        FROM sys.databases
        WHERE name NOT IN {SystemDatabases}
        ORDER BY name
        """;

    /// <summary>
    /// Finds the object a caller named, or nothing when this engine cannot resolve the name.
    /// </summary>
    /// <remarks>
    /// <c>OBJECT_ID</c> is the engine's own name resolution, so asking it is asking the question a
    /// caller would ask: the bare name of an object outside the default schema does not resolve,
    /// the qualified spelling does, and a missing name yields nothing rather than an error — which
    /// is the answer the contract wants for a wrong guess.
    /// <para>
    /// It is also permission-aware, which is the honest behaviour: measured, a login with no rights
    /// on a table gets <c>NULL</c> for its <c>OBJECT_ID</c>, so a name the account cannot see is a
    /// name it cannot resolve. That is the same answer as the catalogue reads below would give, and
    /// it is why metadata visibility is a deployment concern on this engine (ADR 0016).
    /// </para>
    /// </remarks>
    private const string DescribeObjectSql = """
        SELECT o.object_id,
               CASE WHEN s.name = SCHEMA_NAME() THEN o.name ELSE s.name + '.' + o.name END AS reported_name,
               CASE WHEN o.type = 'V' THEN 'view' ELSE 'table' END AS kind,
               CONVERT(NVARCHAR(4000), ep.value) AS comment
        FROM sys.objects AS o
        JOIN sys.schemas AS s ON s.schema_id = o.schema_id
        LEFT JOIN sys.extended_properties AS ep
               ON ep.major_id = o.object_id AND ep.minor_id = 0 AND ep.name = 'MS_Description'
        WHERE o.object_id = OBJECT_ID(@table)
          AND o.type IN ('U', 'V')
          AND o.is_ms_shipped = 0
        """;

    /// <summary>
    /// Reads one row per column, in declaration order.
    /// </summary>
    /// <remarks>
    /// The declared length, precision and scale are read alongside the type name so the answer can
    /// carry the declaration a caller needs to predict behaviour — <c>varchar(50)</c> and
    /// <c>varchar(max)</c> are the same type and not the same column.
    /// <para>
    /// A default is reported as the engine's own text for it, parentheses and all: measured,
    /// <c>DEFAULT ('none')</c> reads back as <c>('none')</c>, and rewriting it would be this
    /// Provider editing the server's words.
    /// </para>
    /// <para>
    /// <c>ep.value</c> is a <c>sql_variant</c> and is converted to text in the query, because the
    /// Driver will not read the variant back as a string.
    /// </para>
    /// </remarks>
    private const string DescribeColumnsSql = """
        SELECT c.name AS column_name,
               c.column_id AS ordinal,
               ty.name AS type_name,
               c.max_length,
               c.precision,
               c.scale,
               c.is_nullable,
               c.is_identity,
               dc.definition AS default_definition,
               CONVERT(NVARCHAR(4000), ep.value) AS comment
        FROM sys.columns AS c
        JOIN sys.types AS ty ON ty.user_type_id = c.user_type_id
        LEFT JOIN sys.default_constraints AS dc ON dc.object_id = c.default_object_id
        LEFT JOIN sys.extended_properties AS ep
               ON ep.major_id = c.object_id AND ep.minor_id = c.column_id AND ep.name = 'MS_Description'
        WHERE c.object_id = @oid
        ORDER BY c.column_id
        """;

    /// <summary>
    /// Reads the primary key's columns in key order.
    /// </summary>
    /// <remarks>
    /// <c>key_ordinal</c> is what carries the order: measured, the key declared <c>(b, a)</c> on a
    /// table whose columns are declared the other way round comes back <c>b</c> then <c>a</c>. The
    /// index that backs the key is identified by <c>is_primary_key</c> rather than by its name,
    /// which is the caller's to choose.
    /// </remarks>
    private const string DescribePrimaryKeySql = """
        SELECT c.name AS column_name
        FROM sys.index_columns AS ic
        JOIN sys.indexes AS i ON i.object_id = ic.object_id AND i.index_id = ic.index_id
        JOIN sys.columns AS c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
        WHERE ic.object_id = @oid AND i.is_primary_key = 1
        ORDER BY ic.key_ordinal
        """;

    /// <summary>
    /// Reads one row per column of every foreign key, with its target and its actions.
    /// </summary>
    /// <remarks>
    /// <c>constraint_column_id</c> is the pairing that matters: measured, a key declared
    /// <c>(composite_b, composite_a) REFERENCES composite(b, a)</c> comes back as row 1 =
    /// <c>composite_b</c>/<c>b</c> and row 2 = <c>composite_a</c>/<c>a</c>. That is the whole
    /// reason for reading the catalogue here, and it is what the standard views do not preserve.
    /// <para>
    /// The referenced table is reported under the same naming rule as a listing: bare when it sits
    /// in the default schema, qualified otherwise, so the name is one this engine resolves.
    /// </para>
    /// </remarks>
    private const string DescribeForeignKeysSql = """
        SELECT fk.name AS foreign_key_name,
               CASE WHEN OBJECT_SCHEMA_NAME(fk.referenced_object_id) = SCHEMA_NAME()
                    THEN OBJECT_NAME(fk.referenced_object_id)
                    ELSE OBJECT_SCHEMA_NAME(fk.referenced_object_id) + '.' + OBJECT_NAME(fk.referenced_object_id)
               END AS referenced_table,
               pc.name AS local_column,
               rc.name AS referenced_column,
               fk.delete_referential_action_desc AS on_delete,
               fk.update_referential_action_desc AS on_update,
               fkc.constraint_column_id AS ordinal
        FROM sys.foreign_keys AS fk
        JOIN sys.foreign_key_columns AS fkc ON fkc.constraint_object_id = fk.object_id
        JOIN sys.columns AS pc ON pc.object_id = fkc.parent_object_id AND pc.column_id = fkc.parent_column_id
        JOIN sys.columns AS rc ON rc.object_id = fkc.referenced_object_id AND rc.column_id = fkc.referenced_column_id
        WHERE fk.parent_object_id = @oid
        ORDER BY fk.name, fkc.constraint_column_id
        """;

    /// <summary>
    /// Reads one row per key column of every index except the one backing the primary key.
    /// </summary>
    /// <remarks>
    /// <c>is_primary_key</c> excludes that index for the same reason the primary key read uses it:
    /// the key is reported as its own member, so repeating its index would present one fact as two.
    /// <para>
    /// <c>is_included_column = 0</c> keeps the answer about the index's key. An <c>INCLUDE</c>
    /// column is carried by the index without being part of what it is ordered by, and the shape
    /// this answer has — a name, uniqueness and a list of columns — has no way to say which is
    /// which, so reporting both would make the list mean two different things in two rows.
    /// </para>
    /// </remarks>
    private const string DescribeIndexesSql = """
        SELECT i.name AS index_name,
               i.is_unique,
               c.name AS column_name,
               ic.key_ordinal
        FROM sys.indexes AS i
        JOIN sys.index_columns AS ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
        JOIN sys.columns AS c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
        WHERE i.object_id = @oid
          AND i.is_primary_key = 0
          AND i.type > 0
          AND ic.is_included_column = 0
        ORDER BY i.name, ic.key_ordinal
        """;

    /// <inheritdoc/>
    public string Type => "mssql";

    /// <inheritdoc/>
    /// <remarks>
    /// SQL Server's own dialect, which is what the Guard needs in order to read the engine's
    /// ordinary SQL: measured, the generic dialect refuses <c>[bracketed]</c> identifiers outright.
    /// The denied functions are the rowset functions that read a file on the server or a table on
    /// another server — and they matter more here than anywhere else, because this engine has no
    /// read-only mode underneath the Guard to refuse what the Guard lets through (ADR 0016).
    /// </remarks>
    public ReadOnlyPolicy Policy => MsSqlReadOnlyPolicy.Definition;

    /// <inheritdoc/>
    /// <remarks>
    /// The read-only <em>intent</em> is written into the connection string and the connection is
    /// left open; it is not a mode this Provider switches on, because the engine has none to
    /// switch. Measured on SQL Server 2022, a standalone instance accepts the keyword and then
    /// permits an <c>INSERT</c> through the same connection, so calling this a read-only mode would
    /// be a claim the engine does not back.
    /// <para>
    /// What it is for is the deployment where the keyword is a real boundary — an Always On
    /// readable secondary — and there the engine refuses a write itself. On a standalone instance
    /// the account's privileges are the boundary, and those belong to whoever wrote the connection
    /// string: this method passes that string through with the intent added and checks nothing
    /// about what it may do (ADR 0016).
    /// </para>
    /// </remarks>
    public async Task<DbConnection> CreateReadOnlyConnectionAsync(
        string connectionString,
        CancellationToken cancellationToken)
    {
        var intent = new SqlConnectionStringBuilder(connectionString)
        {
            ApplicationIntent = ApplicationIntent.ReadOnly,
        }.ConnectionString;

        var connection = new SqlConnection(intent);
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

        var columns = await ReadColumnsAsync(connection, found.Oid, found.Type, cancellationToken);
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
    /// The object a described name refers to.
    /// </summary>
    /// <param name="Oid">The object's catalogue identifier, which every read below keys on.</param>
    /// <param name="Name">The name the object is reported under, which is the name a caller may use.</param>
    /// <param name="Type">Whether the object is a table or a view.</param>
    /// <param name="Comment">The object's comment, or <see langword="null"/> when it has none.</param>
    /// <remarks>
    /// The reported name is the same form the listing uses — bare in the default schema, qualified
    /// outside it — so a name a client read from one Tool is the name it can hand back to the
    /// other. It is computed by the query rather than taken from the caller's spelling, so it
    /// arrives spelled the way the catalogue resolves it.
    /// </remarks>
    private sealed record DescribeObject(int Oid, string Name, TableType Type, string? Comment);

    /// <summary>
    /// Finds the table or view a caller named.
    /// </summary>
    /// <param name="connection">An open connection to the database.</param>
    /// <param name="tableName">The name the caller asked for, bare or qualified.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <returns>The object, or <see langword="null"/> when the engine cannot resolve the name.</returns>
    private static async Task<DescribeObject?> FindObjectAsync(
        DbConnection connection,
        string tableName,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = DescribeObjectSql;
        AddParameter(command, "@table", tableName);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var type = reader.GetString(2).Equals("view", StringComparison.Ordinal)
            ? TableType.View
            : TableType.Table;

        return new DescribeObject(reader.GetInt32(0), reader.GetString(1), type, CommentAt(reader, 3));
    }

    /// <summary>
    /// Reads a table's columns, in declaration order.
    /// </summary>
    /// <param name="connection">An open connection to the database.</param>
    /// <param name="oid">The object's catalogue identifier.</param>
    /// <param name="type">Whether the object is a table or a view.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The columns.</returns>
    /// <remarks>
    /// Autoincrement is reported only for a table, and that is a measured distinction rather than
    /// a cautious one: on this engine a view's column inherits its base column's
    /// <c>is_identity</c> flag, so <c>sys.columns</c> reports the <c>id</c> column of a view over
    /// an identity column as an identity column. Nothing generates a value in a view, so the flag
    /// is not an answer there.
    /// </remarks>
    private static async Task<IReadOnlyList<ColumnInfo>> ReadColumnsAsync(
        DbConnection connection,
        int oid,
        TableType type,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = DescribeColumnsSql;
        AddParameter(command, "@oid", oid);

        var columns = new List<ColumnInfo>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var typeName = reader.GetString(2);
            var isIdentity = reader.GetBoolean(7);

            columns.Add(new ColumnInfo(
                Name: reader.GetString(0),
                Ordinal: reader.GetInt32(1),
                DataType: DeclaredType(typeName, reader.GetInt16(3), reader.GetByte(4), reader.GetByte(5)),
                IsNullable: reader.GetBoolean(6),
                DefaultValue: reader.IsDBNull(8) ? null : reader.GetString(8),
                IsAutoIncrement: type == TableType.Table && isIdentity,
                Comment: CommentAt(reader, 9)));
        }

        return columns;
    }

    /// <summary>
    /// Reads a table's primary key, in key order.
    /// </summary>
    /// <param name="connection">An open connection to the database.</param>
    /// <param name="oid">The object's catalogue identifier.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The key's column names, ordered by their position in the key.</returns>
    private static async Task<IReadOnlyList<string>> ReadPrimaryKeyAsync(
        DbConnection connection,
        int oid,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = DescribePrimaryKeySql;
        AddParameter(command, "@oid", oid);

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
    /// <param name="oid">The object's catalogue identifier.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>One entry per foreign key.</returns>
    private static async Task<IReadOnlyList<ForeignKeyInfo>> ReadForeignKeysAsync(
        DbConnection connection,
        int oid,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = DescribeForeignKeysSql;
        AddParameter(command, "@oid", oid);

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
    /// <param name="oid">The object's catalogue identifier.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>One entry per index.</returns>
    private static async Task<IReadOnlyList<IndexInfo>> ReadIndexesAsync(
        DbConnection connection,
        int oid,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = DescribeIndexesSql;
        AddParameter(command, "@oid", oid);

        var rows = new List<(string Name, bool IsUnique, string Column)>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add((
                Name: reader.GetString(0),
                IsUnique: reader.GetBoolean(1),
                Column: reader.GetString(2)));
        }

        return rows
            .GroupBy(row => row.Name)
            .Select(group => new IndexInfo(
                Name: group.Key,
                IsUnique: group.First().IsUnique,
                Columns: group.Select(row => row.Column).ToArray()))
            .ToArray();
    }

    /// <summary>
    /// The type declaration a caller needs in order to predict how a value is treated.
    /// </summary>
    /// <param name="typeName">The type's name, as the catalogue spells it.</param>
    /// <param name="maxLength">The declared length in bytes, or -1 for the maximum.</param>
    /// <param name="precision">The declared precision.</param>
    /// <param name="scale">The declared scale.</param>
    /// <returns>The declaration, such as <c>varchar(50)</c> or <c>decimal(10,2)</c>.</returns>
    /// <remarks>
    /// The parts that carry meaning are put back, because losing them makes two different columns
    /// look alike: <c>varchar(50)</c> and <c>varchar(max)</c> share a type name and not a
    /// behaviour. <c>max_length</c> counts bytes, so a Unicode type's length is half of it —
    /// measured, <c>nvarchar(50)</c> reports 100. Types whose length is not part of what a caller
    /// decides with are left as their name alone.
    /// </remarks>
    private static string DeclaredType(string typeName, short maxLength, byte precision, byte scale) =>
        typeName.ToLowerInvariant() switch
        {
            "char" or "varchar" or "binary" or "varbinary" => $"{typeName}({Length(maxLength)})",
            "nchar" or "nvarchar" => $"{typeName}({UnicodeLength(maxLength)})",
            "decimal" or "numeric" => $"{typeName}({Number(precision)},{Number(scale)})",
            "datetime2" or "datetimeoffset" or "time" => $"{typeName}({Number(scale)})",
            "float" => $"{typeName}({Number(precision)})",
            _ => typeName,
        };

    /// <summary>
    /// Renders a declared length, including the engine's spelling of the unbounded one.
    /// </summary>
    /// <param name="maxLength">The declared length in bytes, or -1 for the maximum.</param>
    /// <returns>The length as the engine spells it.</returns>
    private static string Length(short maxLength) =>
        maxLength < 0 ? "max" : Convert.ToString(maxLength, CultureInfo.InvariantCulture);

    /// <summary>
    /// Renders a declared length for a type whose characters occupy two bytes.
    /// </summary>
    /// <param name="maxLength">The declared length in bytes, or -1 for the maximum.</param>
    /// <returns>The number of characters, or <c>max</c> for the unbounded declaration.</returns>
    /// <remarks>
    /// The catalogue counts bytes, so the halves are taken before the length is rendered and never
    /// after: halving the unbounded marker would turn <c>-1</c> into <c>0</c> and report
    /// <c>nvarchar(0)</c> for a column that holds any amount of text.
    /// </remarks>
    private static string UnicodeLength(short maxLength) =>
        Length(maxLength < 0 ? (short)-1 : (short)(maxLength / 2));

    /// <summary>
    /// Renders one of a declaration's numbers.
    /// </summary>
    /// <param name="value">The number.</param>
    /// <returns>The number as text, in the invariant culture.</returns>
    private static string Number(byte value) => Convert.ToString(value, CultureInfo.InvariantCulture);

    /// <summary>
    /// Translates the catalogue's action description into the action it names.
    /// </summary>
    /// <param name="description">The description from <c>sys.foreign_keys</c>.</param>
    /// <returns>The action.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The description is not one the server documents.</exception>
    /// <remarks>
    /// The descriptions are the server's own and are documented rather than guessed. An
    /// unrecognised one fails loudly: a foreign key whose action silently became
    /// <see cref="ForeignKeyAction.NoAction"/> would be a lie about what deleting a row does.
    /// </remarks>
    private static ForeignKeyAction Action(string description) => description switch
    {
        "NO_ACTION" => ForeignKeyAction.NoAction,
        "CASCADE" => ForeignKeyAction.Cascade,
        "SET_NULL" => ForeignKeyAction.SetNull,
        "SET_DEFAULT" => ForeignKeyAction.SetDefault,
        _ => throw new ArgumentOutOfRangeException(
            nameof(description),
            description,
            "SQL Server reported a foreign key action this Provider does not know."),
    };

    /// <summary>
    /// Reads the comment at a column, which the engine stores as a variant.
    /// </summary>
    /// <param name="reader">The reader positioned on a row.</param>
    /// <param name="ordinal">The comment's ordinal, or one past the end when there is none.</param>
    /// <returns>The comment, or <see langword="null"/> when the column is absent or null.</returns>
    private static string? CommentAt(DbDataReader reader, int ordinal) =>
        ordinal >= reader.FieldCount || reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    /// <summary>
    /// Binds a parameter, naming its type so the server reads it as the value it is.
    /// </summary>
    /// <param name="command">The command to bind onto.</param>
    /// <param name="name">The parameter's name, with its <c>@</c>.</param>
    /// <param name="value">The value.</param>
    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
