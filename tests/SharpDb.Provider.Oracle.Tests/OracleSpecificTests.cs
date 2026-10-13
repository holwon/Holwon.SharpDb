using Oracle.ManagedDataAccess.Client;
using SharpDb.Abstractions;
using SharpDb.Abstractions.Schema;
using SharpDb.Core.Mcp;
using SharpDb.Provider.Contract;

namespace SharpDb.Provider.Oracle.Tests;

/// <summary>
/// What the Oracle Provider answers, and what Oracle does, that the shared contract suite cannot
/// assert — because no other engine answers it the same way.
/// </summary>
/// <remarks>
/// These are not extras. The shared suites hold only what every engine answers identically, so a
/// behaviour that is Oracle's own has to be pinned somewhere or it is pinned nowhere. Each section
/// is a measured difference, and most of them were measured by running the shared suites against
/// this very container and reading where they broke:
/// <list type="bullet">
/// <item><description>
/// the empty string is NULL, and so is a zero-length <c>RAW</c>;
/// </description></item>
/// <item><description>
/// there is no <c>ON UPDATE</c> referential action (<c>ORA-00905</c>);
/// </description></item>
/// <item><description>
/// <c>EXPLAIN SELECT</c> is a syntax error (<c>ORA-00905</c>) — only <c>EXPLAIN PLAN FOR</c>
/// exists, and it writes a table;
/// </description></item>
/// <item><description>
/// a <c>SELECT</c> with no <c>FROM</c> is a syntax error on 21c (<c>ORA-00923</c>);
/// </description></item>
/// <item><description>
/// an error message does not name the object the caller got wrong (<c>ORA-00942</c>), and the
/// column one names it only in upper case and truncated (<c>ORA-00904</c>);
/// </description></item>
/// <item><description>
/// an unquoted identifier is folded to upper case, so a result column's label is upper case and a
/// catalogue name is upper case;
/// </description></item>
/// <item><description>
/// the read-only guarantee is taken at transaction level, and a DDL statement implicitly commits
/// and thereby escapes it.
/// </description></item>
/// </list>
/// <para>
/// These tests seed tables of their own, so they run against the container's spare schema rather
/// than the shared one: the shared suites assert the exact set of objects in the shared schema, and
/// a table seeded beside it would break the listing for whichever suite ran afterwards.
/// </para>
/// </remarks>
/// <param name="database">The container the collection shares.</param>
[Collection(OracleContractCollection.Name)]
public sealed class OracleSpecificTests(OracleContractDatabase database)
{
    /// <summary>
    /// The empty string is NULL, and a zero-length <c>RAW</c> is too.
    /// </summary>
    /// <remarks>
    /// This is the single fact behind three of the assertions the shared suite had to give up: a
    /// comparison against <c>''</c> matches nothing, <c>IS NULL</c> matches the row, and a column
    /// written with an empty value reads back as a null. Measured both ways so the equivalence is
    /// asserted rather than inferred from one side.
    /// </remarks>
    [SkippableFact]
    public async Task An_empty_string_is_null()
    {
        database.SkipWhenUnavailable();

        var empty = await Query().ExecuteQueryAsync("""SELECT count(*) AS "n" FROM readings WHERE note = ''""");
        var isNull = await Query().ExecuteQueryAsync("""SELECT count(*) AS "n" FROM readings WHERE note IS NULL""");

        Assert.NotEqual(true, empty.IsError);
        Assert.NotEqual(true, isNull.IsError);
        Assert.Equal("""{"columns":["n"],"rows":[[0]]}""", ToolPayload.Text(empty));
        Assert.Equal("""{"columns":["n"],"rows":[[2]]}""", ToolPayload.Text(isNull));
    }

    /// <summary>
    /// A zero-length <c>RAW</c> is stored as NULL, so the empty blob is not a value Oracle holds.
    /// </summary>
    [SkippableFact]
    public async Task A_blob_loads_as_base64_and_an_empty_one_is_null()
    {
        database.SkipWhenUnavailable();

        var result = await Query().ExecuteQueryAsync("""SELECT id AS "id", payload AS "payload" FROM readings ORDER BY id""");

        Assert.NotEqual(true, result.IsError);
        // Row 1 holds HEXTORAW('0102'); every other row's payload is absent rather than empty.
        Assert.Equal(
            """{"columns":["id","payload"],"rows":[[1,"AQI="],[2,null],[3,null],[4,null],[5,null],[6,null]]}""",
            ToolPayload.Text(result));
    }

    /// <summary>
    /// An <c>ON UPDATE</c> referential action cannot be declared at all.
    /// </summary>
    [SkippableFact]
    public async Task An_on_update_referential_action_is_a_syntax_error()
    {
        database.SkipWhenUnavailable();
        await SeedDdlAsync(
            "CREATE TABLE onupd_parent (id NUMBER PRIMARY KEY)");

        var failure = await Assert.ThrowsAsync<OracleException>(() => SeedDdlAsync(
            """
            CREATE TABLE onupd_child (
                id NUMBER PRIMARY KEY,
                pid NUMBER,
                CONSTRAINT fk_onupd FOREIGN KEY (pid) REFERENCES onupd_parent(id) ON UPDATE CASCADE
            )
            """));

        Assert.Contains("ORA-00905", failure.Message);
    }

    /// <summary>
    /// <c>EXPLAIN SELECT</c> is a syntax error; Oracle has no <c>EXPLAIN</c> that returns a plan.
    /// </summary>
    /// <remarks>
    /// The Guard permits <c>EXPLAIN</c>, so this reaches the engine and the engine refuses it. The
    /// only plan form Oracle has is <c>EXPLAIN PLAN FOR</c>, which writes a table and is therefore
    /// a write rather than an answer.
    /// </remarks>
    [SkippableFact]
    public async Task An_explain_of_a_select_is_a_syntax_error()
    {
        database.SkipWhenUnavailable();

        var result = await Query().ExecuteQueryAsync("EXPLAIN SELECT id FROM readings");

        Assert.True(result.IsError);
        Assert.Contains("ORA-00905", ToolPayload.Error(result));
    }

    /// <summary>
    /// A <c>SELECT</c> with no <c>FROM</c> is a syntax error on this version.
    /// </summary>
    /// <remarks>
    /// Oracle 21c requires a <c>FROM</c> clause; only 23c allows it to be omitted. The shared suite
    /// builds a constant expression with no <c>FROM</c>, so the equivalent assertion lives here and
    /// uses <c>dual</c>.
    /// </remarks>
    [SkippableFact]
    public async Task A_select_with_no_from_is_a_syntax_error()
    {
        database.SkipWhenUnavailable();

        var result = await Query().ExecuteQueryAsync("SELECT 1");

        Assert.True(result.IsError);
        Assert.Contains("ORA-00923", ToolPayload.Error(result));
    }

    /// <summary>
    /// An error message does not name the object the caller got wrong.
    /// </summary>
    /// <remarks>
    /// Measured: <c>ORA-00942</c> is the bare words "table or view does not exist" with no name in
    /// it, and <c>ORA-00904</c> names a column only in upper case and truncated to the identifier
    /// limit. This is why the shared suite asserts only that a database failure is reported and its
    /// object-naming half is asserted in the engines that do name the object.
    /// </remarks>
    [SkippableFact]
    public async Task A_failure_does_not_name_the_object_as_the_caller_spelled_it()
    {
        database.SkipWhenUnavailable();

        var missingTable = await Query().ExecuteQueryAsync("SELECT id FROM no_such_table");

        Assert.True(missingTable.IsError);
        Assert.DoesNotContain("no_such_table", ToolPayload.Error(missingTable));
        Assert.Contains("ORA-00942", ToolPayload.Error(missingTable));
    }

    /// <summary>
    /// Text outside ASCII is written as itself, not as an escape.
    /// </summary>
    /// <remarks>
    /// The mirror of the assertion the shared suite had to move: the engine needs a <c>FROM</c>, so
    /// the text is selected from <c>dual</c>. The alias is written in Chinese too, because a payload
    /// is not only its rows — a column is named by the statement as well.
    /// </remarks>
    [SkippableFact]
    public async Task Text_outside_ascii_is_written_as_itself()
    {
        database.SkipWhenUnavailable();

        var result = await Query().ExecuteQueryAsync(
            """SELECT '中文' AS "列" FROM dual""");

        Assert.NotEqual(true, result.IsError);
        Assert.Equal("""{"columns":["列"],"rows":[["中文"]]}""", ToolPayload.Text(result));
    }

    /// <summary>
    /// The read-only transaction refuses a write, and the refusal is the engine's own.
    /// </summary>
    /// <remarks>
    /// <c>ORA-01456</c> is "cannot perform a DML operation inside a read-only transaction", which is
    /// the engine layer of the read-only guarantee (ADR 0002). Asserted for all three DML forms,
    /// because the mode being in force is what the guarantee rests on.
    /// </remarks>
    [SkippableFact]
    public async Task The_read_only_transaction_refuses_a_write()
    {
        database.SkipWhenUnavailable();
        await SeedDdlAsync(
            "CREATE TABLE dml_target (id NUMBER PRIMARY KEY, label VARCHAR2(20))",
            "INSERT INTO dml_target (id, label) VALUES (1, 'a')");

        await using var connection = await OpenSpareReadOnlyAsync();

        foreach (var statement in new[]
        {
            "INSERT INTO dml_target (id, label) VALUES (2, 'b')",
            "UPDATE dml_target SET label = 'c' WHERE id = 1",
            "DELETE FROM dml_target WHERE id = 1",
        })
        {
            await using var command = connection.CreateCommand();
            command.CommandText = statement;

            var failure = await Assert.ThrowsAsync<OracleException>(() => command.ExecuteNonQueryAsync());
            Assert.Contains("ORA-01456", failure.Message);
        }
    }

    /// <summary>
    /// A DDL statement implicitly commits and thereby escapes the read-only transaction.
    /// </summary>
    /// <remarks>
    /// This asserts the hole <em>exists</em> rather than trying to close it, because Oracle does not
    /// offer a way to: <c>SET TRANSACTION READ ONLY</c> is a transaction property, and a DDL
    /// statement commits the transaction it is in. The consequence is that Oracle's layer-2
    /// read-only does not stand against a DDL, so on Oracle the account's own privileges — layer 3
    /// — are not advice but the boundary (ADR 0014).
    /// </remarks>
    [SkippableFact]
    public async Task A_ddl_statement_escapes_the_read_only_transaction()
    {
        database.SkipWhenUnavailable();

        await using var connection = await OpenSpareReadOnlyAsync();

        // The DDL succeeds inside a transaction the Provider opened read-only, and committing it
        // ends the read-only mode, so the writes that follow succeed too.
        await using (var create = connection.CreateCommand())
        {
            create.CommandText = "CREATE TABLE ddl_escape (id NUMBER PRIMARY KEY, label VARCHAR2(20))";
            await create.ExecuteNonQueryAsync();
        }

        await using (var insert = connection.CreateCommand())
        {
            insert.CommandText = "INSERT INTO ddl_escape (id, label) VALUES (1, 'escaped')";
            await insert.ExecuteNonQueryAsync();
        }

        await using var verify = connection.CreateCommand();
        verify.CommandText = "SELECT count(*) FROM ddl_escape";
        verify.BindByName = true;

        Assert.Equal(1m, Convert.ToDecimal(await verify.ExecuteScalarAsync()));

        await DropAsync(connection, "ddl_escape");
    }

    /// <summary>
    /// The Provider opens its connections binding by name, so a placeholder repeated in one
    /// statement reaches the right value.
    /// </summary>
    /// <remarks>
    /// Measured: this Driver binds by position unless told otherwise, and a positional bind of a
    /// named placeholder either fills the wrong value silently or — when a name appears twice —
    /// fails outright with <c>ORA-00936</c>. The statement here names <c>@id</c> twice, which is
    /// exactly the shape the Provider's own name lookup uses.
    /// </remarks>
    [SkippableFact]
    public async Task The_connection_binds_by_name()
    {
        database.SkipWhenUnavailable();

        await using var connection = await OpenSpareReadOnlyAsync();
        Assert.True(connection.BindByName);

        var result = await Query().ExecuteQueryAsync(
            """SELECT id AS "id" FROM readings WHERE id = @id OR id = @id""",
            ToolPayload.Parameters(("id", 1)));

        Assert.NotEqual(true, result.IsError);
        Assert.Equal("""{"columns":["id"],"rows":[[1]]}""", ToolPayload.Text(result));
    }

    /// <summary>
    /// An identity column is reported as auto-increment.
    /// </summary>
    /// <remarks>
    /// Oracle records this in <c>ALL_TAB_COLUMNS.IDENTITY_COLUMN</c>, which is <c>YES</c> for a
    /// column declared <c>GENERATED … AS IDENTITY</c> on 12c and later.
    /// </remarks>
    [SkippableFact]
    public async Task An_identity_column_is_reported_as_autoincrement()
    {
        database.SkipWhenUnavailable();
        await SeedDdlAsync(
            "CREATE TABLE identity_target (id NUMBER GENERATED BY DEFAULT AS IDENTITY, label VARCHAR2(20))");

        var schema = await database.ReadThroughProviderAsync(
            connection => database.Provider.DescribeTableAsync(
                connection,
                "identity_target",
                CancellationToken.None),
            database.SpareConnectionString);

        Assert.NotNull(schema);
        Assert.True(schema.Columns.Single(column => column.Name == "id").IsAutoIncrement);
        Assert.False(schema.Columns.Single(column => column.Name == "label").IsAutoIncrement);
    }

    /// <summary>
    /// A column default is reported, and the <c>LONG</c> column that carries it is read directly.
    /// </summary>
    /// <remarks>
    /// <c>DATA_DEFAULT</c> is a <c>LONG</c>, and a function around it fails — measured,
    /// <c>ORA-00932</c> — so the Provider reads it as it comes. A column without a default reports
    /// none, which is the control.
    /// </remarks>
    [SkippableFact]
    public async Task A_column_default_is_reported()
    {
        database.SkipWhenUnavailable();
        await SeedDdlAsync(
            "CREATE TABLE defaulted (id NUMBER, label VARCHAR2(20) DEFAULT 'plain', rating NUMBER DEFAULT 7)");

        var schema = await database.ReadThroughProviderAsync(
            connection => database.Provider.DescribeTableAsync(
                connection,
                "defaulted",
                CancellationToken.None),
            database.SpareConnectionString);

        Assert.NotNull(schema);
        Assert.Equal("'plain'", schema.Columns.Single(column => column.Name == "label").DefaultValue);
        Assert.Equal("7", schema.Columns.Single(column => column.Name == "rating").DefaultValue);
        Assert.Null(schema.Columns.Single(column => column.Name == "id").DefaultValue);
    }

    /// <summary>
    /// A view is listed as a view, and a description of it reports no key, foreign keys or indexes.
    /// </summary>
    /// <remarks>
    /// Measured: the object catalogue that lists both tables and views is <c>ALL_TAB_COMMENTS</c>,
    /// and it is the one that says which each is. A view's columns come from the same
    /// <c>ALL_TAB_COLUMNS</c> a table's do.
    /// </remarks>
    [SkippableFact]
    public async Task A_view_is_listed_as_a_view()
    {
        database.SkipWhenUnavailable();

        await using var connection = await OpenAsync();

        var listed = await database.Provider.ListTablesAsync(connection, CancellationToken.None);
        Assert.Equal(TableType.View, listed.Single(table => table.Name == "active_users").Type);

        var schema = await database.Provider.DescribeTableAsync(connection, "active_users", CancellationToken.None);
        Assert.NotNull(schema);
        Assert.Equal("active_users", schema.Name);
        Assert.Equal(["id", "email"], schema.Columns.Select(column => column.Name));
        Assert.Empty(schema.PrimaryKey);
        Assert.Empty(schema.ForeignKeys);
        Assert.Empty(schema.Indexes);
    }

    /// <summary>
    /// An unquoted name is reported in lower case, the way a caller writes it.
    /// </summary>
    /// <remarks>
    /// Oracle folds an unquoted identifier to upper case, so a caller writing <c>unquoted_target</c>
    /// reaches an object the catalogue spells <c>UNQUOTED_TARGET</c>. The Provider reports the name
    /// in lower case, because that is the name a caller would write and the name every other
    /// engine's Provider reports.
    /// </remarks>
    [SkippableFact]
    public async Task An_unquoted_name_is_reported_in_lower_case()
    {
        database.SkipWhenUnavailable();
        await SeedDdlAsync("CREATE TABLE unquoted_target (id NUMBER)");

        var schema = await database.ReadThroughProviderAsync(
            connection => database.Provider.DescribeTableAsync(
                connection,
                "unquoted_target",
                CancellationToken.None),
            database.SpareConnectionString);

        Assert.NotNull(schema);
        Assert.Equal("unquoted_target", schema.Name);
        Assert.Equal("id", schema.Columns.Single().Name);
    }

    /// <summary>
    /// A quoted, lower-case name is reported as the catalogue spells it.
    /// </summary>
    /// <remarks>
    /// The mirror of the assertion above: a quoted identifier is <em>not</em> folded, so a lower-case
    /// name exists only as written and is reported exactly so. Reporting it folded the other way
    /// would name an object that does not exist.
    /// </remarks>
    [SkippableFact]
    public async Task A_quoted_lower_case_name_is_reported_as_the_catalogue_spells_it()
    {
        database.SkipWhenUnavailable();
        await SeedDdlAsync("""CREATE TABLE "lower_target" (id NUMBER)""");

        var schema = await database.ReadThroughProviderAsync(
            connection => database.Provider.DescribeTableAsync(
                connection,
                "lower_target",
                CancellationToken.None),
            database.SpareConnectionString);

        Assert.NotNull(schema);
        Assert.Equal("lower_target", schema.Name);
    }

    /// <summary>
    /// A result column's label is upper case unless the caller aliases it.
    /// </summary>
    /// <remarks>
    /// This is a genuine difference the shared suite had to accommodate: Oracle folds an unquoted
    /// identifier, including a result column's label, so <c>SELECT id FROM readings</c> answers a
    /// column named <c>ID</c>. A caller who wants a particular label writes one, which every engine
    /// honours identically.
    /// </remarks>
    [SkippableFact]
    public async Task A_result_columns_label_is_upper_case_unless_aliased()
    {
        database.SkipWhenUnavailable();

        var unaliased = await Query().ExecuteQueryAsync("SELECT id FROM readings WHERE ROWNUM = 1");
        var aliased = await Query().ExecuteQueryAsync("""SELECT id AS "id" FROM readings WHERE ROWNUM = 1""");

        Assert.NotEqual(true, unaliased.IsError);
        Assert.Equal("""{"columns":["ID"],"rows":[[1]]}""", ToolPayload.Text(unaliased));
        Assert.NotEqual(true, aliased.IsError);
        Assert.Equal("""{"columns":["id"],"rows":[[1]]}""", ToolPayload.Text(aliased));
    }

    /// <summary>
    /// The query Tool bound to the shared schema.
    /// </summary>
    /// <returns>The Tool, wired as the running server wires it.</returns>
    private QueryTools Query() => QueryTool.For(database);

    /// <summary>
    /// Opens the connection a test will read through, over the shared schema.
    /// </summary>
    /// <returns>The open, read-only connection, which the test disposes.</returns>
    private Task<System.Data.Common.DbConnection> OpenAsync() =>
        database.Provider.CreateReadOnlyConnectionAsync(database.ConnectionString, CancellationToken.None);

    /// <summary>
    /// Opens the Provider's read-only connection over the spare schema.
    /// </summary>
    /// <returns>The open connection, cast to the Driver's type so its settings can be read.</returns>
    private async Task<OracleConnection> OpenSpareReadOnlyAsync() =>
        (OracleConnection)await database.Provider.CreateReadOnlyConnectionAsync(
            database.SpareConnectionString,
            CancellationToken.None);

    /// <summary>
    /// Runs one or more statements against the spare schema through a writable connection.
    /// </summary>
    /// <param name="statements">The statements to run, each on its own.</param>
    /// <returns>The work that runs them.</returns>
    /// <remarks>
    /// The spare user owns its schema and may create and drop objects in it, which is what lets
    /// these tests build the tables they need without touching the shared schema.
    /// </remarks>
    private async Task SeedDdlAsync(params string[] statements)
    {
        await using var connection = new OracleConnection(database.SpareConnectionString);
        await connection.OpenAsync();

        foreach (var statement in statements)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = statement;
            await command.ExecuteNonQueryAsync();
        }
    }

    /// <summary>
    /// Drops a table the tests created, ignoring the error when it is not there.
    /// </summary>
    /// <param name="connection">An open connection to the spare schema.</param>
    /// <param name="tableName">The table to drop.</param>
    /// <returns>The work that drops it.</returns>
    /// <remarks>
    /// Oracle has no <c>DROP TABLE IF EXISTS</c>, so the drop is wrapped in a block that swallows
    /// the "does not exist" error — the one place a swallowed error is the intent rather than a
    /// defect, because the table's presence is not what the test is asserting.
    /// </remarks>
    private static async Task DropAsync(OracleConnection connection, string tableName)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            BEGIN
                EXECUTE IMMEDIATE 'DROP TABLE {tableName}';
            EXCEPTION WHEN OTHERS THEN NULL;
            END;
            """;
        await command.ExecuteNonQueryAsync();
    }
}
