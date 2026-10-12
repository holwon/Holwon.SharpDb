using System.Data.Common;
using Oracle.ManagedDataAccess.Client;
using SharpDb.Abstractions;
using SharpDb.Abstractions.Schema;

namespace SharpDb.Provider.Oracle;

/// <summary>
/// Serves Oracle databases, reaching them exclusively through the Driver this project owns.
/// </summary>
/// <remarks>
/// This engine's properties shape the whole Provider, and each was measured rather than assumed.
/// <para>
/// The first is that the read-only guarantee has to be taken at <em>transaction</em> level. Oracle
/// has no session-wide read-only mode; what it has is <c>SET TRANSACTION READ ONLY</c>, which
/// applies to the transaction that runs it and to no other. Measured: sent on its own it enforces
/// nothing — an <c>INSERT</c>, an <c>UPDATE</c> and even a <c>CREATE TABLE</c> all succeed and
/// commit — so the Provider begins a transaction first and asks for the mode inside it. The
/// transaction is never committed, so the connection is handed out inside a read-only transaction
/// that lasts exactly as long as the connection does (ADR 0014).
/// </para>
/// <para>
/// The second is identifier case. Oracle folds an unquoted identifier to upper case, so an object
/// created as <c>readings</c> is stored as <c>READINGS</c> and is reached by a caller writing
/// <c>readings</c>. A name the catalogue holds in all upper case is therefore reported here in
/// lower case — which is how a caller writes it, and how every other engine in this project spells
/// it — while a name that is not all upper case came from a quoted identifier and is reported
/// exactly as the catalogue holds it.
/// </para>
/// <para>
/// The third is that introspection reads the <c>ALL_*</c> dictionary views rather than the
/// <c>DBA_*</c> ones, so an account needs no privilege beyond the <c>SELECT</c> it already has
/// (ADR 0014). The connection's own schema is the scope, exactly as a search path is for
/// PostgreSQL.
/// </para>
/// </remarks>
public sealed class OracleDatabaseProvider : IDatabaseProvider
{
    /// <summary>
    /// Puts the current transaction in read-only mode.
    /// </summary>
    /// <remarks>
    /// The statement is worthless without a transaction to apply it to, which is why the Provider
    /// begins one first. Measured on Oracle XE 21.3: <c>SET TRANSACTION READ ONLY</c> executed on
    /// its own is granted, enforces nothing, and the write that follows it succeeds.
    /// </remarks>
    private const string EnterReadOnlyTransaction = "SET TRANSACTION READ ONLY";

    /// <summary>
    /// Lists the tables and views the connection's own schema holds.
    /// </summary>
    /// <remarks>
    /// <c>ALL_TAB_COMMENTS</c> is what is read, and both halves of that are measured rather than
    /// assumed. It carries a table's and a view's comment — <c>ALL_TABLES</c> and <c>ALL_VIEWS</c>
    /// have no comment column at all (<c>ORA-00904</c>) — and its <c>TABLE_TYPE</c> says which kind
    /// each object is, so the two facts arrive in one read instead of a union of two views that
    /// each know only one of them. A view is not a table in the dictionary: measured,
    /// <c>ALL_TABLES</c> answers nothing for the seeded view, which is why reading one view is
    /// enough here where reading the two table-catalogue views would not be.
    /// </remarks>
    private const string ListTablesSql = """
        SELECT table_name, table_type, comments
        FROM all_tab_comments
        WHERE owner = USER
        ORDER BY table_name
        """;

    /// <summary>
    /// Lists the schemas whose objects this connection can see, plus its own.
    /// </summary>
    /// <remarks>
    /// Oracle's nearest thing to a database listing is the set of owners the dictionary exposes.
    /// The connection's own schema is added explicitly, because a schema holding no table and no
    /// view would otherwise not appear — and the connection is bound to it, which is the one fact
    /// the listing exists to convey.
    /// </remarks>
    private const string ListDatabasesSql = """
        SELECT DISTINCT owner FROM all_tab_comments
        UNION
        SELECT USER FROM dual
        ORDER BY 1
        """;

    /// <summary>
    /// Finds the object a name refers to, and which kind it is, or nothing when there is none.
    /// </summary>
    /// <remarks>
    /// The name is matched the way the engine matches an identifier, which is two comparisons
    /// rather than one: a caller writing <c>users</c> reaches the object Oracle stored as
    /// <c>USERS</c> because an unquoted identifier is folded to upper case, and a caller writing a
    /// quoted name reaches only the object spelled exactly that way. An exact match outranks a
    /// folded one, because a caller who spelled the name the catalogue's way meant that object.
    /// </remarks>
    private const string DescribeExistsSql = """
        SELECT * FROM (
            SELECT table_name, table_type, comments
            FROM all_tab_comments
            WHERE owner = USER AND table_name IN (:name, UPPER(:name))
            ORDER BY CASE WHEN table_name = :name THEN 0 ELSE 1 END, table_name
        ) WHERE ROWNUM = 1
        """;

    /// <summary>
    /// Reads one row per column, in declaration order.
    /// </summary>
    /// <remarks>
    /// <c>ALL_TAB_COLUMNS</c> already excludes hidden columns, so what it lists is what
    /// <c>SELECT *</c> returns. <c>DATA_DEFAULT</c> is read directly and never wrapped in a
    /// function: measured, it is a <c>LONG</c> column and any call around it fails with
    /// <c>ORA-00932</c>. The declared type is reassembled here because the dictionary splits it
    /// across several columns.
    /// </remarks>
    private const string DescribeColumnsSql = """
        SELECT column_name, column_id, data_type, data_length, data_precision, data_scale,
               char_length, nullable, data_default, identity_column
        FROM all_tab_columns
        WHERE owner = USER AND table_name = :table_name
        ORDER BY column_id
        """;

    /// <summary>
    /// Reads the primary key's columns in key order.
    /// </summary>
    /// <remarks>
    /// <c>POSITION</c> is the key's order rather than the table's, so a composite key declared
    /// <c>(b, a)</c> arrives as <c>b, a</c> even though the columns are declared the other way
    /// round.
    /// </remarks>
    private const string DescribePrimaryKeySql = """
        SELECT cc.column_name
        FROM all_constraints c
        JOIN all_cons_columns cc
          ON cc.owner = c.owner AND cc.constraint_name = c.constraint_name
        WHERE c.owner = USER AND c.table_name = :table_name AND c.constraint_type = 'P'
        ORDER BY cc.position
        """;

    /// <summary>
    /// Reads one row per column of every foreign key, with its target and its delete rule.
    /// </summary>
    /// <remarks>
    /// Oracle names the parent constraint rather than the parent table, so the target is reached by
    /// joining the referenced constraint back to <c>ALL_CONSTRAINTS</c>. The two column lists are
    /// paired by <c>POSITION</c>, which is what keeps a composite key's second local column
    /// pointing at the second referenced one.
    /// <para>
    /// There is no update rule to read, and none is invented: Oracle has no <c>ON UPDATE</c>
    /// referential action at all — declaring one is <c>ORA-00905</c> — and the dictionary has no
    /// column for one, so every key reports <c>NoAction</c>.
    /// </para>
    /// </remarks>
    private const string DescribeForeignKeysSql = """
        SELECT c.constraint_name,
               rc.table_name AS referenced_table,
               cc.column_name AS local_column,
               rcc.column_name AS referenced_column,
               c.delete_rule,
               cc.position
        FROM all_constraints c
        JOIN all_cons_columns cc
          ON cc.owner = c.owner AND cc.constraint_name = c.constraint_name
        JOIN all_constraints rc
          ON rc.owner = c.r_owner AND rc.constraint_name = c.r_constraint_name
        JOIN all_cons_columns rcc
          ON rcc.owner = rc.owner AND rcc.constraint_name = rc.constraint_name
         AND rcc.position = cc.position
        WHERE c.owner = USER AND c.table_name = :table_name AND c.constraint_type = 'R'
        ORDER BY c.constraint_name, cc.position
        """;

    /// <summary>
    /// Reads one row per indexed column of every index except the one backing the primary key.
    /// </summary>
    /// <remarks>
    /// The exclusion is the catalogue's own answer rather than a guess from the index's name: a
    /// primary key constraint records the index it is backed by in <c>INDEX_NAME</c>, and that is
    /// what is filtered out. Oracle names such an index after the constraint when one was named and
    /// after nothing in particular when it was not, so a name-based rule would miss half the cases.
    /// <para>
    /// A column of an index may be an expression, which has no column name; it is dropped, so an
    /// index of nothing but expressions arrives with no columns — the same answer the other engines
    /// give.
    /// </para>
    /// </remarks>
    private const string DescribeIndexesSql = """
        SELECT i.index_name, i.uniqueness, ic.column_name, ic.column_position
        FROM all_indexes i
        JOIN all_ind_columns ic
          ON ic.index_owner = i.owner AND ic.index_name = i.index_name
        WHERE i.table_owner = USER AND i.table_name = :table_name
          AND i.index_name NOT IN (
              SELECT c.index_name
              FROM all_constraints c
              WHERE c.owner = USER AND c.table_name = :table_name
                AND c.constraint_type = 'P' AND c.index_name IS NOT NULL
          )
        ORDER BY i.index_name, ic.column_position
        """;

    /// <inheritdoc/>
    public string Type => "oracle";

    /// <inheritdoc/>
    /// <remarks>
    /// Oracle's own dialect, and no denied function names. The dialect value is what makes Core
    /// respell a placeholder <c>@name</c> as <c>:name</c>, which is the only spelling this Driver
    /// binds.
    /// </remarks>
    public ReadOnlyPolicy Policy => OracleReadOnlyPolicy.Definition;

    /// <inheritdoc/>
    /// <remarks>
    /// The order of the steps is the whole of it. The transaction is begun before the mode is asked
    /// for, because the mode applies to a transaction and to nothing else. <c>BindByName</c> is set
    /// before the connection is handed out, because this Driver binds by position by default and a
    /// positional binding of a named parameter is a wrong answer that looks like a right one.
    /// The transaction is deliberately never committed: it is what the read-only mode lives in, and
    /// disposing the connection rolls it back (ADR 0014).
    /// </remarks>
    public async Task<DbConnection> CreateReadOnlyConnectionAsync(
        string connectionString,
        CancellationToken cancellationToken)
    {
        var connection = new OracleConnection(connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);

            // The handle is not kept: the connection owns the transaction, and letting it go out of
            // scope here is what leaves it open for the connection's whole life rather than
            // disposing it — which would end the read-only mode with it. The Driver's own typed
            // overload is used rather than the base one, because a command takes an
            // OracleTransaction and the base-typed one would not assign.
            var transaction = connection.BeginTransaction();

            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = EnterReadOnlyTransaction;
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            // Measured: this Driver binds by position unless told otherwise, and a positional bind
            // of a named placeholder silently fills the wrong column — or, when a statement names a
            // placeholder twice, raises rather than answering. Set on the connection so that the
            // statement the Tool runs — whose command Core builds — binds by name too.
            connection.BindByName = true;

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

            tables.Add(new TableInfo(NormalizedName(reader.GetString(0)), type, CommentAt(reader, 2)));
        }

        // Ordered here rather than by the statement, because the names are lower-cased on the way
        // out and the catalogue's order is the upper-case one.
        return tables.OrderBy(table => table.Name, StringComparer.Ordinal).ToArray();
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

        var columns = await ReadColumnsAsync(connection, found.Stored, cancellationToken);
        var primaryKey = await ReadPrimaryKeyAsync(connection, found.Stored, cancellationToken);
        var foreignKeys = await ReadForeignKeysAsync(connection, found.Stored, cancellationToken);
        var indexes = await ReadIndexesAsync(connection, found.Stored, cancellationToken);

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
            databases.Add(NormalizedName(reader.GetString(0)));
        }

        return databases
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// The object a described name refers to, as the catalogue spells it.
    /// </summary>
    /// <param name="Stored">The name the catalogue holds, which every read below keys on.</param>
    /// <param name="Name">The name to report, normalised for the caller.</param>
    /// <param name="Type">Whether the object is a table or a view.</param>
    /// <param name="Comment">The object's comment, or <see langword="null"/> when it has none.</param>
    private sealed record DescribeObject(string Stored, string Name, TableType Type, string? Comment);

    /// <summary>
    /// Finds the table or view a caller named.
    /// </summary>
    /// <param name="connection">An open connection to the database.</param>
    /// <param name="tableName">The name the caller asked for, in whatever case.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <returns>The object, or <see langword="null"/> when the schema holds no such table or view.</returns>
    private static async Task<DescribeObject?> FindObjectAsync(
        DbConnection connection,
        string tableName,
        CancellationToken cancellationToken)
    {
        await using var command = CommandFor(connection);
        command.CommandText = DescribeExistsSql;
        AddNameParameter(command, tableName);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var stored = reader.GetString(0);
        var type = reader.GetString(1).Equals("VIEW", StringComparison.OrdinalIgnoreCase)
            ? TableType.View
            : TableType.Table;

        return new DescribeObject(stored, NormalizedName(stored), type, CommentAt(reader, 2));
    }

    /// <summary>
    /// Reads a table's columns, in declaration order.
    /// </summary>
    /// <param name="connection">An open connection to the database.</param>
    /// <param name="tableName">The name the catalogue holds.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The columns.</returns>
    private static async Task<IReadOnlyList<ColumnInfo>> ReadColumnsAsync(
        DbConnection connection,
        string tableName,
        CancellationToken cancellationToken)
    {
        await using var command = CommandFor(connection);
        command.CommandText = DescribeColumnsSql;
        AddTableParameter(command, tableName);

        var columns = new List<ColumnInfo>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            columns.Add(new ColumnInfo(
                Name: NormalizedName(reader.GetString(0)),
                Ordinal: reader.GetInt32(1),
                DataType: DeclaredType(reader),
                IsNullable: reader.GetString(7).Equals("Y", StringComparison.Ordinal),
                DefaultValue: DefaultAt(reader, 8),
                IsAutoIncrement: reader.GetString(9).Equals("YES", StringComparison.Ordinal),
                Comment: null));
        }

        return columns;
    }

    /// <summary>
    /// Reads a table's primary key, in key order.
    /// </summary>
    /// <param name="connection">An open connection to the database.</param>
    /// <param name="tableName">The name the catalogue holds.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The key's column names, ordered by their position in the key.</returns>
    private static async Task<IReadOnlyList<string>> ReadPrimaryKeyAsync(
        DbConnection connection,
        string tableName,
        CancellationToken cancellationToken)
    {
        await using var command = CommandFor(connection);
        command.CommandText = DescribePrimaryKeySql;
        AddTableParameter(command, tableName);

        var columns = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            columns.Add(NormalizedName(reader.GetString(0)));
        }

        return columns;
    }

    /// <summary>
    /// Reads a table's foreign keys.
    /// </summary>
    /// <param name="connection">An open connection to the database.</param>
    /// <param name="tableName">The name the catalogue holds.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>One entry per foreign key.</returns>
    private static async Task<IReadOnlyList<ForeignKeyInfo>> ReadForeignKeysAsync(
        DbConnection connection,
        string tableName,
        CancellationToken cancellationToken)
    {
        await using var command = CommandFor(connection);
        command.CommandText = DescribeForeignKeysSql;
        AddTableParameter(command, tableName);

        var rows = new List<(string Name, string Table, string Local, string Referenced, string DeleteRule)>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add((
                Name: NormalizedName(reader.GetString(0)),
                Table: NormalizedName(reader.GetString(1)),
                Local: NormalizedName(reader.GetString(2)),
                Referenced: NormalizedName(reader.GetString(3)),
                DeleteRule: reader.GetString(4)));
        }

        return rows
            .GroupBy(row => row.Name)
            .Select(group => new ForeignKeyInfo(
                Name: group.Key,
                Columns: group.Select(row => row.Local).ToArray(),
                ReferencedTable: group.First().Table,
                ReferencedColumns: group.Select(row => row.Referenced).ToArray(),
                OnDelete: Action(group.First().DeleteRule),

                // Oracle has no ON UPDATE referential action, so there is no rule to report and
                // none is invented (ADR 0014).
                OnUpdate: ForeignKeyAction.NoAction))
            .ToArray();
    }

    /// <summary>
    /// Reads a table's indexes, excluding the one backing the primary key.
    /// </summary>
    /// <param name="connection">An open connection to the database.</param>
    /// <param name="tableName">The name the catalogue holds.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>One entry per index.</returns>
    private static async Task<IReadOnlyList<IndexInfo>> ReadIndexesAsync(
        DbConnection connection,
        string tableName,
        CancellationToken cancellationToken)
    {
        await using var command = CommandFor(connection);
        command.CommandText = DescribeIndexesSql;
        AddTableParameter(command, tableName);

        var rows = new List<(string Name, bool IsUnique, string? Column)>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add((
                Name: NormalizedName(reader.GetString(0)),
                IsUnique: reader.GetString(1).Equals("UNIQUE", StringComparison.Ordinal),
                Column: reader.IsDBNull(2) ? null : NormalizedName(reader.GetString(2))));
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
    /// Reports a catalogue name the way a caller writes it.
    /// </summary>
    /// <param name="stored">The name the catalogue holds.</param>
    /// <returns>The name to report.</returns>
    /// <remarks>
    /// Oracle folds an unquoted identifier to upper case, so an all-upper-case name is one a caller
    /// writes in lower case and is reported that way. A name with any lower-case letter in it came
    /// from a quoted identifier, which is not folded, and is reported exactly as the catalogue holds
    /// it — reporting it folded would name an object that does not exist.
    /// </remarks>
    private static string NormalizedName(string stored) =>
        stored.All(character => !char.IsLetter(character) || char.IsUpper(character))
            ? stored.ToLowerInvariant()
            : stored;

    /// <summary>
    /// Reassembles a column's declared type from the columns the dictionary splits it across.
    /// </summary>
    /// <param name="reader">The reader positioned on a column's row.</param>
    /// <returns>The declaration, as a caller would write it.</returns>
    /// <remarks>
    /// Oracle keeps the base type in one column and its length, precision and scale in others, so
    /// the declaration is put back together here. Only the types that carry a size are given one:
    /// a <c>DATE</c> has no length to report, and reporting the storage width of one would be a
    /// fact about Oracle's internals rather than about the column.
    /// </remarks>
    private static string DeclaredType(DbDataReader reader)
    {
        var type = reader.GetString(2);
        var precision = IntegerAt(reader, 4);
        var scale = IntegerAt(reader, 5);
        var characterLength = IntegerAt(reader, 6);

        return type switch
        {
            "NUMBER" when precision is > 0 && scale is > 0 => $"{type}({precision},{scale})",
            "NUMBER" when precision is > 0 => $"{type}({precision})",
            "CHAR" or "VARCHAR2" or "NCHAR" or "NVARCHAR2" => $"{type}({characterLength})",
            "RAW" => $"{type}({IntegerAt(reader, 3)})",
            _ => type,
        };
    }

    /// <summary>
    /// Reads a nullable numeric column.
    /// </summary>
    /// <param name="reader">The reader positioned on a row.</param>
    /// <param name="ordinal">The column's position in the row.</param>
    /// <returns>The value, or <see langword="null"/> when the column is null.</returns>
    private static int? IntegerAt(DbDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : Convert.ToInt32(reader.GetValue(ordinal));

    /// <summary>
    /// Reads a column's default, which the dictionary keeps in a <c>LONG</c> column.
    /// </summary>
    /// <param name="reader">The reader positioned on a column's row.</param>
    /// <param name="ordinal">The default's position in the row.</param>
    /// <returns>The default, or <see langword="null"/> when the column has none.</returns>
    /// <remarks>
    /// The value is read directly, because <c>DATA_DEFAULT</c> is a <c>LONG</c> and any function
    /// around it fails — measured, <c>ORA-00932</c>. The trailing whitespace Oracle pads it with is
    /// removed, since it is storage rather than part of the expression.
    /// </remarks>
    private static string? DefaultAt(DbDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        var value = reader.GetString(ordinal).TrimEnd();

        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    /// <summary>
    /// Translates the catalogue's delete rule into the action it names.
    /// </summary>
    /// <param name="rule">The text from <c>ALL_CONSTRAINTS.DELETE_RULE</c>.</param>
    /// <returns>The action.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The rule is not one the server documents.</exception>
    /// <remarks>
    /// The three rules are Oracle's own and are documented rather than guessed. An unrecognised one
    /// fails loudly: a foreign key whose action silently became <c>NoAction</c> would be a lie about
    /// what deleting a row does.
    /// </remarks>
    private static ForeignKeyAction Action(string rule) => rule switch
    {
        "NO ACTION" => ForeignKeyAction.NoAction,
        "CASCADE" => ForeignKeyAction.Cascade,
        "SET NULL" => ForeignKeyAction.SetNull,
        _ => throw new ArgumentOutOfRangeException(
            nameof(rule),
            rule,
            "Oracle reported a foreign key delete rule this Provider does not know."),
    };

    /// <summary>
    /// Reads a comment that may be absent.
    /// </summary>
    /// <param name="reader">The reader positioned on a row.</param>
    /// <param name="ordinal">The comment's position in the row.</param>
    /// <returns>The comment, or <see langword="null"/> when the object carries none.</returns>
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
    private static void AddNameParameter(DbCommand command, string tableName)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = "name";
        parameter.Value = tableName;

        command.Parameters.Add(parameter);
    }

    /// <summary>
    /// Creates a command that binds by name and fetches <c>LONG</c> values whole.
    /// </summary>
    /// <param name="connection">The connection the command belongs to.</param>
    /// <returns>The command, ready for a statement with named placeholders.</returns>
    /// <remarks>
    /// Measured, and each for a different reason.
    /// <para>
    /// <c>BindByName</c>: this Driver binds by position unless told otherwise, and a statement that
    /// names a placeholder more than once — which the name lookup does, so that a caller's spelling
    /// and the catalogue's can be tried together — cannot bind positionally at all. The connection
    /// is set the same way when it is opened; setting it here too keeps a command correct however it
    /// was reached, and is cheap.
    /// </para>
    /// <para>
    /// <c>InitialLONGFetchSize</c>: it defaults to zero, which means a <c>LONG</c> column is
    /// fetched empty. Measured: <c>ALL_TAB_COLUMNS.DATA_DEFAULT</c> is a <c>LONG</c>, so a column's
    /// default read back as null until this was set to fetch the whole value.
    /// </para>
    /// </remarks>
    private static OracleCommand CommandFor(DbConnection connection)
    {
        var command = (OracleCommand)connection.CreateCommand();
        command.BindByName = true;
        command.InitialLONGFetchSize = -1;

        return command;
    }

    /// <summary>
    /// Binds the name the catalogue holds.
    /// </summary>
    /// <param name="command">The command to bind to.</param>
    /// <param name="tableName">The name the catalogue holds.</param>
    /// <remarks>
    /// The name is <c>table_name</c> rather than <c>table</c>, because <c>TABLE</c> is an Oracle
    /// reserved word and a bind variable may not be named after one — measured, <c>:table</c> is
    /// <c>ORA-01745</c>.
    /// </remarks>
    private static void AddTableParameter(DbCommand command, string tableName)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = "table_name";
        parameter.Value = tableName;

        command.Parameters.Add(parameter);
    }
}
