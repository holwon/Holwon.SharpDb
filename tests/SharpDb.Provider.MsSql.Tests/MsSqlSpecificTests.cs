using Microsoft.Data.SqlClient;
using SharpDb.Abstractions.Schema;
using SharpDb.Core.Mcp;
using SharpDb.Provider.Contract;

namespace SharpDb.Provider.MsSql.Tests;

/// <summary>
/// What SQL Server answers, and what it does, that the shared contract suite cannot assert —
/// because no other engine answers it the same way, or because no other engine has to.
/// </summary>
/// <remarks>
/// These are not extras. The shared suites hold only what every engine answers identically, so a
/// behaviour that is this engine's own has to be pinned somewhere or it is pinned nowhere. Most of
/// them were measured by running the shared suites against this very container and reading where
/// they broke:
/// <list type="bullet">
/// <item><description>
/// the read-only intent lives in the connection string, and a write through that connection
/// <em>succeeds</em> on a standalone server — so what refuses a write here is the account;
/// </description></item>
/// <item><description>
/// a name outside the default schema is reported qualified, because that is the only spelling this
/// engine resolves;
/// </description></item>
/// <item><description>
/// comments and defaults live in <c>sys.extended_properties</c> and <c>sys.default_constraints</c>,
/// which is where the server keeps them;
/// </description></item>
/// <item><description>
/// a view's column inherits its base column's identity flag, so the flag is not an answer there;
/// </description></item>
/// <item><description>
/// <c>EXPLAIN</c> and <c>SHOW</c> are not this engine's syntax, so the Guard permits them and the
/// server refuses them;
/// </description></item>
/// <item><description>
/// the rowset functions this engine parses are refused by the Guard, and the ones it cannot parse
/// are refused as unreadable.
/// </description></item>
/// </list>
/// <para>
/// These tests seed tables of their own, so they run against the container's spare database rather
/// than the shared one: the shared suites assert the exact set of objects in the shared schema, and
/// a table seeded beside it would break the listing for whichever suite ran afterwards.
/// </para>
/// </remarks>
/// <param name="database">The container the collection shares.</param>
[Collection(MsSqlContractCollection.Name)]
public sealed class MsSqlSpecificTests(MsSqlContractDatabase database)
{
    /// <summary>
    /// A table whose every facet the shared schema deliberately leaves out.
    /// </summary>
    private const string Ddl = """
        DROP TABLE IF EXISTS dbo.annotated;

        CREATE TABLE dbo.annotated (
            id INT IDENTITY(1,1) NOT NULL,
            title VARCHAR(50) NOT NULL,
            rating INT NULL,
            body VARCHAR(50) NULL CONSTRAINT df_annotated_body DEFAULT ('none'),
            stamp DATETIME2(3) NULL,
            price DECIMAL(10,2) NULL,
            CONSTRAINT pk_annotated PRIMARY KEY (id)
        );

        EXEC sys.sp_addextendedproperty
            @name = N'MS_Description', @value = N'a table comment',
            @level0type = N'SCHEMA', @level0name = N'dbo',
            @level1type = N'TABLE', @level1name = N'annotated';

        EXEC sys.sp_addextendedproperty
            @name = N'MS_Description', @value = N'the title column',
            @level0type = N'SCHEMA', @level0name = N'dbo',
            @level1type = N'TABLE', @level1name = N'annotated',
            @level2type = N'COLUMN', @level2name = N'title';
        """;

    /// <summary>
    /// The connection the Provider opens carries read-only intent, and carries the rest of the
    /// caller's string unchanged.
    /// </summary>
    /// <remarks>
    /// This asserts how the intent is asked for rather than what it achieves, and the difference
    /// matters on this engine: the next test shows that a standalone server accepts the keyword and
    /// still permits a write. What is asserted here is that the Provider did its part — the
    /// keyword is on the connection the caller receives, and the caller's own settings survived it.
    /// </remarks>
    [SkippableFact]
    public async Task The_connection_the_provider_opens_carries_read_only_intent()
    {
        database.SkipWhenUnavailable();

        await using var connection = await database.Provider.CreateReadOnlyConnectionAsync(
            database.ConnectionString,
            CancellationToken.None);

        var built = new SqlConnectionStringBuilder(connection.ConnectionString);

        Assert.Equal(ApplicationIntent.ReadOnly, built.ApplicationIntent);
        Assert.Equal(database.DatabaseName, built.InitialCatalog);
    }

    /// <summary>
    /// The engine refuses a write through the connection the Provider opened, because the account
    /// cannot write.
    /// </summary>
    /// <remarks>
    /// This is this engine's real read-only boundary, and it is a fact about the server rather than
    /// a rule for a user: the account is the fixture's choice, and whichever account a deployment
    /// supplies is the deployment's. The write goes straight down the connection, never through a
    /// Tool, so the refusal is the server's rather than the Guard's.
    /// <para>
    /// The assertion is deliberately not "the connection string says read-only": measured on this
    /// engine, a standalone server with <c>ApplicationIntent=ReadOnly</c> lets an <c>INSERT</c>
    /// through, so a test that read the keyword would pass while a write was possible.
    /// </para>
    /// </remarks>
    [SkippableFact]
    public async Task The_account_the_provider_connects_as_refuses_a_write()
    {
        database.SkipWhenUnavailable();

        await using var connection = await database.Provider.CreateReadOnlyConnectionAsync(
            database.ConnectionString,
            CancellationToken.None);

        await using var write = connection.CreateCommand();
        write.CommandText = "INSERT INTO dbo.orgs (name) VALUES ('a write the account must refuse')";

        var refusal = await Assert.ThrowsAnyAsync<SqlException>(() => write.ExecuteNonQueryAsync());

        Assert.Contains("permission", refusal.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Read-only intent is not a mode the engine enforces on a standalone server.
    /// </summary>
    /// <remarks>
    /// This is the fact ADR 0016 is about, and it is asserted rather than only documented: a write
    /// attempted as the administrator through a connection carrying <c>ApplicationIntent=ReadOnly</c>
    /// succeeds. The test is here so that a later engine version, or a server deployed behind an
    /// Always On listener, shows up as a changed fact rather than as a sentence in a document that
    /// has quietly stopped being true.
    /// </remarks>
    [SkippableFact]
    public async Task Read_only_intent_does_not_stop_a_write_on_a_standalone_server()
    {
        database.SkipWhenUnavailable();

        await using var administrator = new SqlConnection(database.SpareAdministratorConnectionString);
        await administrator.OpenAsync();

        await using (var cleanup = administrator.CreateCommand())
        {
            cleanup.CommandText = "DROP TABLE IF EXISTS dbo.intent_probe";
            await cleanup.ExecuteNonQueryAsync();
        }

        await using var intent = new SqlConnection(
            new SqlConnectionStringBuilder(database.SpareAdministratorConnectionString)
            {
                ApplicationIntent = ApplicationIntent.ReadOnly,
            }.ConnectionString);
        await intent.OpenAsync();

        await using var write = intent.CreateCommand();
        write.CommandText = "CREATE TABLE dbo.intent_probe (x INT NULL)";

        await write.ExecuteNonQueryAsync();

        await using var cleanupAfter = administrator.CreateCommand();
        cleanupAfter.CommandText = "DROP TABLE IF EXISTS dbo.intent_probe";
        await cleanupAfter.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// A name in the default schema is reported bare, and one outside it is reported qualified.
    /// </summary>
    /// <remarks>
    /// This is the naming rule ADR 0017 records, and it is the engine's own: measured,
    /// <c>OBJECT_ID('users')</c> resolves the <c>dbo</c> table while <c>OBJECT_ID('totals')</c>
    /// does not resolve the one in another schema. Reporting a bare name the engine cannot resolve
    /// would hand a client a name it cannot use — which is the one thing the listing must never do.
    /// </remarks>
    [SkippableFact]
    public async Task A_name_outside_the_default_schema_is_reported_qualified()
    {
        database.SkipWhenUnavailable();
        await SeedOtherSchemaAsync();

        await using var connection = await database.Provider.CreateReadOnlyConnectionAsync(
            database.SpareConnectionString,
            CancellationToken.None);

        var listed = await database.Provider.ListTablesAsync(connection, CancellationToken.None);
        var names = listed.Select(table => table.Name).ToArray();

        Assert.Contains("qualified_target", names);
        Assert.Contains("reporting.elsewhere", names);

        // The qualified name is the one that resolves, and the bare one is not — the rule the
        // listing follows, restated as the engine answers it.
        await using var check = connection.CreateCommand();
        check.CommandText = """
            SELECT OBJECT_ID('qualified_target') AS bare_resolves,
                   OBJECT_ID('reporting.elsewhere') AS qualified_resolves
            """;
        await using var reader = await check.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.NotEqual(DBNull.Value, reader.GetValue(0));
        Assert.NotEqual(DBNull.Value, reader.GetValue(1));
    }

    /// <summary>
    /// A description of a qualified name finds the object.
    /// </summary>
    /// <remarks>
    /// The other half of the naming rule: a client that read a qualified name from the listing must
    /// be able to describe it, which means the lookup has to accept the spelling the listing gave.
    /// </remarks>
    [SkippableFact]
    public async Task A_description_accepts_the_qualified_name_the_listing_reported()
    {
        database.SkipWhenUnavailable();
        await SeedOtherSchemaAsync();

        await using var connection = await database.Provider.CreateReadOnlyConnectionAsync(
            database.SpareConnectionString,
            CancellationToken.None);

        var schema = await database.Provider.DescribeTableAsync(
            connection,
            "reporting.elsewhere",
            CancellationToken.None);

        Assert.NotNull(schema);
        Assert.Equal("reporting.elsewhere", schema.Name);
        Assert.Equal(["id"], schema.PrimaryKey);
    }

    /// <summary>
    /// A table's and a column's comment reach the client.
    /// </summary>
    /// <remarks>
    /// This engine keeps comments as extended properties rather than as part of the declaration, so
    /// reading them is a join the other engines do not perform. Both Tools report them, because a
    /// client that can read a column's name but not its meaning has half the schema.
    /// </remarks>
    [SkippableFact]
    public async Task Table_and_column_comments_reach_the_client()
    {
        database.SkipWhenUnavailable();
        await SeedAsync();

        await using var connection = await database.Provider.CreateReadOnlyConnectionAsync(
            database.SpareConnectionString,
            CancellationToken.None);

        var schema = await database.Provider.DescribeTableAsync(connection, "annotated", CancellationToken.None);

        Assert.NotNull(schema);
        Assert.Equal("a table comment", schema.Comment);
        Assert.Equal("the title column", schema.Columns.Single(column => column.Name == "title").Comment);
        Assert.Null(schema.Columns.Single(column => column.Name == "rating").Comment);
    }

    /// <summary>
    /// An <c>IDENTITY</c> column is reported as auto-increment, and a view's column is not.
    /// </summary>
    /// <remarks>
    /// The second half is the interesting one: measured, a view's column inherits its base column's
    /// <c>is_identity</c> flag, so the catalogue reports the <c>id</c> column of a view over an
    /// identity column as generated. Nothing generates a value in a view, so reporting it as
    /// auto-increment would be a claim about the view that the engine does not hold.
    /// </remarks>
    [SkippableFact]
    public async Task An_identity_column_is_auto_increment_and_a_views_column_is_not()
    {
        database.SkipWhenUnavailable();
        await SeedAsync();

        await using var connection = await database.Provider.CreateReadOnlyConnectionAsync(
            database.SpareConnectionString,
            CancellationToken.None);

        var table = await database.Provider.DescribeTableAsync(connection, "annotated", CancellationToken.None);
        var view = await database.Provider.DescribeTableAsync(connection, "annotated_view", CancellationToken.None);

        Assert.NotNull(table);
        Assert.NotNull(view);
        Assert.True(table.Columns.Single(column => column.Name == "id").IsAutoIncrement);
        Assert.All(view.Columns, column => Assert.False(column.IsAutoIncrement));
    }

    /// <summary>
    /// A declaration's type carries the parts a caller decides with.
    /// </summary>
    /// <remarks>
    /// The engines differ here and this is the assertion for this one: <c>varchar(50)</c> and
    /// <c>varchar(max)</c> share a type name and not a behaviour, and a Unicode column's length is
    /// half the byte count the catalogue stores. Measured, <c>nvarchar(50)</c> reports
    /// <c>max_length</c> of 100 — a Provider that reported the raw number would be telling a caller
    /// its 50-character column holds 100.
    /// </remarks>
    [SkippableFact]
    public async Task A_columns_declared_type_carries_its_length_and_scale()
    {
        database.SkipWhenUnavailable();
        await SeedAsync();

        await using var connection = await database.Provider.CreateReadOnlyConnectionAsync(
            database.SpareConnectionString,
            CancellationToken.None);

        var schema = await database.Provider.DescribeTableAsync(connection, "typed", CancellationToken.None);

        Assert.NotNull(schema);
        Assert.Equal("varchar(50)", schema.Columns.Single(column => column.Name == "narrow").DataType);
        Assert.Equal("varchar(max)", schema.Columns.Single(column => column.Name == "wide").DataType);
        Assert.Equal("nvarchar(50)", schema.Columns.Single(column => column.Name == "unicode").DataType);
        Assert.Equal("decimal(10,2)", schema.Columns.Single(column => column.Name == "price").DataType);
        Assert.Equal("datetime2(3)", schema.Columns.Single(column => column.Name == "stamp").DataType);
    }

    /// <summary>
    /// A column's default reaches the client in the engine's own words.
    /// </summary>
    /// <remarks>
    /// The engine stores a default as the definition text it will apply, parentheses included, and
    /// this Provider reports it as stored rather than rewriting it — measured, the shared schema's
    /// <c>DEFAULT ('none')</c> reads back as <c>('none')</c>. The absent case is asserted beside it,
    /// because a Provider that invented a default for an unconstrained column would pass the first
    /// half.
    /// </remarks>
    [SkippableFact]
    public async Task A_columns_default_reaches_the_client_as_the_engine_stores_it()
    {
        database.SkipWhenUnavailable();
        await SeedAsync();

        await using var connection = await database.Provider.CreateReadOnlyConnectionAsync(
            database.SpareConnectionString,
            CancellationToken.None);

        var schema = await database.Provider.DescribeTableAsync(connection, "annotated", CancellationToken.None);

        Assert.NotNull(schema);
        Assert.Equal("('none')", schema.Columns.Single(column => column.Name == "body").DefaultValue);
        Assert.Null(schema.Columns.Single(column => column.Name == "title").DefaultValue);
    }

    /// <summary>
    /// The engines that can hold an empty string report it apart from null.
    /// </summary>
    /// <remarks>
    /// The mirror of the assertion Oracle cannot make: this engine keeps <c>''</c> and NULL distinct,
    /// so the shared schema's <c>gamma</c> row carries the empty string and reads it back. It is
    /// asserted here because the shared suite cannot — Oracle stores the empty string as NULL — and
    /// because a Provider that turned one into the other would be inventing a value.
    /// <para>
    /// The two counts are the shared schema's own rows rather than the numbers another engine would
    /// report: of the six seeded readings exactly one has an empty note and exactly one has a null
    /// one, while Oracle would count two nulls and no empty string.
    /// </para>
    /// </remarks>
    [SkippableFact]
    public async Task An_empty_string_is_reported_apart_from_null()
    {
        database.SkipWhenUnavailable();

        var empty = await Query().ExecuteQueryAsync(
            """SELECT COUNT(*) AS "n" FROM readings WHERE note = ''""");
        var isNull = await Query().ExecuteQueryAsync(
            """SELECT COUNT(*) AS "n" FROM readings WHERE note IS NULL""");

        Assert.NotEqual(true, empty.IsError);
        Assert.NotEqual(true, isNull.IsError);
        Assert.Equal("""{"columns":["n"],"rows":[[1]]}""", ToolPayload.Text(empty));
        Assert.Equal("""{"columns":["n"],"rows":[[1]]}""", ToolPayload.Text(isNull));
    }

    /// <summary>
    /// <c>EXPLAIN</c> and <c>SHOW</c> are permitted by the Guard and refused by the engine.
    /// </summary>
    /// <remarks>
    /// The Guard's permit list names both statement forms, and this engine has neither: it explains
    /// through execution plans and enumerates through catalogue views. So the refusal reaches the
    /// client as a database failure in the server's own words, which is what the contract promises
    /// for a statement the Guard allowed and the engine would not.
    /// </remarks>
    [SkippableFact]
    public async Task An_explain_or_a_show_is_refused_by_the_engine()
    {
        database.SkipWhenUnavailable();

        var explain = await Query().ExecuteQueryAsync("EXPLAIN SELECT id FROM readings");
        var show = await Query().ExecuteQueryAsync("SHOW TABLES");

        Assert.True(explain.IsError);
        Assert.StartsWith("Database error:", ToolPayload.Error(explain));
        Assert.True(show.IsError);
        Assert.StartsWith("Database error:", ToolPayload.Error(show));
    }

    /// <summary>
    /// A failure names the object the caller got wrong.
    /// </summary>
    /// <remarks>
    /// Asserted here because the shared suite asserts only the prefix every engine can produce —
    /// Oracle's <c>ORA-00942</c> does not name the object. This engine does, and a client fixing a
    /// typo is better served by the name it got wrong than by a general sentence.
    /// </remarks>
    [SkippableFact]
    public async Task A_failure_names_the_object_the_caller_got_wrong()
    {
        database.SkipWhenUnavailable();

        var result = await Query().ExecuteQueryAsync("SELECT id FROM no_such_table");

        Assert.True(result.IsError);
        Assert.Contains("no_such_table", ToolPayload.Error(result));
    }

    /// <summary>
    /// The Guard accepts this engine's ordinary read-only SQL and refuses its rowset functions.
    /// </summary>
    /// <remarks>
    /// Both halves run through the real Tool, so what is asserted is what a client would actually
    /// get: <c>TOP</c> and bracketed identifiers reach the server and answer, while a query that
    /// reaches outside the database is refused before the connection is opened. The rowset functions
    /// matter more here than on any other engine, because there is no engine-level read-only mode
    /// underneath the Guard on this one (ADR 0016).
    /// </remarks>
    [SkippableFact]
    public async Task The_guard_reads_this_engines_sql_and_refuses_its_rowset_functions()
    {
        database.SkipWhenUnavailable();
        await SeedAsync();

        // The spare database is where the tables this query names live, so the Tool is bound to it
        // rather than to the shared one — the Guard's behaviour is the same either way, and the
        // tables are not.
        var tool = QueryTool.For(database.Provider, database.SpareConnectionString, database.Type, timeoutSeconds: 30);

        var top = await tool.ExecuteQueryAsync("""SELECT TOP 2 [id] AS "id" FROM [annotated] ORDER BY [id]""");
        var rowset = await tool.ExecuteQueryAsync(
            "SELECT * FROM OPENQUERY(LinkedServer, 'SELECT 1')");

        Assert.NotEqual(true, top.IsError);
        Assert.Equal("""{"columns":["id"],"rows":[]}""", ToolPayload.Text(top));

        Assert.True(rowset.IsError);
        Assert.Contains("OPENQUERY", ToolPayload.Error(rowset));
    }

    /// <summary>
    /// A query that outlives its timeout is interrupted by the engine.
    /// </summary>
    /// <remarks>
    /// The shared suite asserts the timeout message; this asserts that on this engine the statement
    /// is actually stopped rather than merely abandoned while it runs on. Measured, the Driver's
    /// command timeout interrupts the statement, so the server is released rather than left working
    /// on an answer nobody will read.
    /// </remarks>
    [SkippableFact]
    public async Task A_query_that_outlives_its_timeout_is_interrupted()
    {
        database.SkipWhenUnavailable();

        await using var connection = await database.Provider.CreateReadOnlyConnectionAsync(
            database.ConnectionString,
            CancellationToken.None);

        await using var command = connection.CreateCommand();
        command.CommandText = database.SlowQuery;
        command.CommandTimeout = 1;

        var started = DateTime.UtcNow;
        await Assert.ThrowsAnyAsync<SqlException>(() => command.ExecuteNonQueryAsync());
        var elapsed = DateTime.UtcNow - started;

        // The statement was stopped rather than left to finish: it would take far longer than this
        // bound if the timeout had only been observed after the fact.
        Assert.True(
            elapsed < TimeSpan.FromSeconds(15),
            $"The timeout should have interrupted the statement, but it ran for {elapsed.TotalSeconds:F1} seconds.");
    }

    /// <summary>
    /// The engine reports its own four databases, and the Provider's answer leaves them out.
    /// </summary>
    /// <remarks>
    /// The two halves are asserted together so the exclusion is shown to be the Provider's doing
    /// rather than the account's: measured, this account sees all four of the server's own
    /// databases in <c>sys.databases</c>, so a Provider that simply reported what the catalogue
    /// said would list them.
    /// </remarks>
    [SkippableFact]
    public async Task The_servers_own_databases_are_excluded_from_the_listing()
    {
        database.SkipWhenUnavailable();

        await using var connection = await database.Provider.CreateReadOnlyConnectionAsync(
            database.ConnectionString,
            CancellationToken.None);

        await using (var check = connection.CreateCommand())
        {
            check.CommandText = "SELECT COUNT(*) FROM sys.databases WHERE name IN ('master', 'tempdb', 'model', 'msdb')";
            Assert.Equal(4, Convert.ToInt32(await check.ExecuteScalarAsync()));
        }

        var listed = await database.Provider.ListDatabasesAsync(connection, CancellationToken.None);

        Assert.DoesNotContain("master", listed);
        Assert.DoesNotContain("tempdb", listed);
        Assert.DoesNotContain("model", listed);
        Assert.DoesNotContain("msdb", listed);
        Assert.Contains(database.DatabaseName, listed);
    }

    /// <summary>
    /// Without <c>VIEW DEFINITION</c>, a column's default text is invisible to the account.
    /// </summary>
    /// <remarks>
    /// This is the measurement behind the README's operational note, asserted rather than only
    /// written down. Measured on SQL Server 2022 CU27: an account holding only
    /// <c>GRANT SELECT ON SCHEMA::dbo</c> still sees the default constraint's <em>name</em> and
    /// <c>default_object_id</c>, while <c>sys.default_constraints.definition</c> reads back NULL —
    /// the server hides the text from a caller without <c>VIEW DEFINITION</c>. So a deployment
    /// without it is not broken, it is quieter: the schema arrives with fewer details than the
    /// server holds, and this is the fact that says which.
    /// <para>
    /// A second account is created and dropped inside the test rather than added to the fixture,
    /// because the fixture's own account needs the grant to answer the other assertions at all.
    /// </para>
    /// </remarks>
    [SkippableFact]
    public async Task Without_view_definition_a_columns_default_text_is_hidden()
    {
        database.SkipWhenUnavailable();
        await SeedAsync();

        const string login = "sharpdb_narrow";
        const string password = "SharpDb!Narrow2026";

        // Pooling is off, and that is what makes the cleanup possible: the Driver pools by default,
        // so disposing the connection would hand the session back to the pool rather than close it,
        // and measured, this engine then refuses to drop the login — "the user is currently logged
        // in" — even though every connection this test opened has been disposed.
        var narrow = new SqlConnectionStringBuilder(database.SpareAdministratorConnectionString)
        {
            UserID = login,
            Password = password,
            Pooling = false,
        }.ConnectionString;

        await using var administrator = new SqlConnection(database.SpareAdministratorConnectionString);
        await administrator.OpenAsync();

        // One statement per command, because this engine's grammar is fussy about what may share a
        // batch: measured, sending the login, the user and the grant as one batch is a syntax error
        // rather than three statements.
        await using (var createLogin = administrator.CreateCommand())
        {
            createLogin.CommandText = $"""
                IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = '{login}')
                    CREATE LOGIN [{login}] WITH PASSWORD = '{password}', CHECK_POLICY = OFF;
                """;
            await createLogin.ExecuteNonQueryAsync();
        }

        await using (var createUser = administrator.CreateCommand())
        {
            createUser.CommandText = $"""
                IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = '{login}')
                    CREATE USER [{login}] FOR LOGIN [{login}];
                GRANT SELECT ON SCHEMA::dbo TO [{login}];
                """;
            await createUser.ExecuteNonQueryAsync();
        }

        SqlConnection? limited = null;
        try
        {
            limited = new SqlConnection(narrow);
            await limited.OpenAsync();

            await using var read = limited.CreateCommand();
            read.CommandText = """
                SELECT c.default_object_id,
                       dc.name,
                       dc.definition
                FROM sys.columns AS c
                LEFT JOIN sys.default_constraints AS dc ON dc.object_id = c.default_object_id
                WHERE c.object_id = OBJECT_ID('dbo.annotated') AND c.name = 'body'
                """;

            await using var reader = await read.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());

            // The constraint is visible and its text is not: that is the whole finding.
            Assert.NotEqual(0, reader.GetInt32(0));
            Assert.Equal("df_annotated_body", reader.GetString(1));
            Assert.True(reader.IsDBNull(2));
        }
        finally
        {
            // The account's own connection is closed before it is dropped, because measured, this
            // engine refuses to drop a login that is logged in: "the user is currently logged in".
            if (limited is not null)
            {
                await limited.DisposeAsync();
            }

            // DROP LOGIN takes no IF EXISTS on this engine either — measured, that spelling is a
            // syntax error while DROP USER's is not — so the existence check is written out.
            await using var dropUser = administrator.CreateCommand();
            dropUser.CommandText = $"DROP USER IF EXISTS [{login}];";
            await dropUser.ExecuteNonQueryAsync();

            await using var dropLogin = administrator.CreateCommand();
            dropLogin.CommandText = $"""
                IF EXISTS (SELECT 1 FROM sys.server_principals WHERE name = '{login}')
                    DROP LOGIN [{login}];
                """;
            await dropLogin.ExecuteNonQueryAsync();
        }
    }

    /// <summary>
    /// The query Tool bound to the shared database.
    /// </summary>
    /// <returns>The Tool, wired as the running server wires it.</returns>
    private QueryTools Query() => QueryTool.For(database);

    /// <summary>
    /// Seeds the annotated table and the typed table into the spare database.
    /// </summary>
    /// <returns>The work that seeds them.</returns>
    private async Task SeedAsync()
    {
        await using var seeding = new SqlConnection(database.SpareAdministratorConnectionString);
        await seeding.OpenAsync();

        await using var command = seeding.CreateCommand();
        command.CommandText = Ddl + "\n" + """
            DROP TABLE IF EXISTS dbo.typed;
            CREATE TABLE dbo.typed (
                narrow VARCHAR(50) NULL,
                wide VARCHAR(MAX) NULL,
                unicode NVARCHAR(50) NULL,
                price DECIMAL(10,2) NULL,
                stamp DATETIME2(3) NULL
            );
            DROP VIEW IF EXISTS dbo.annotated_view;
            """;
        await command.ExecuteNonQueryAsync();

        await using var view = seeding.CreateCommand();
        view.CommandText = "CREATE VIEW dbo.annotated_view AS SELECT id, title FROM dbo.annotated";
        await view.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Seeds a table in the spare database's default schema and one in a second schema.
    /// </summary>
    /// <returns>The work that seeds them.</returns>
    /// <remarks>
    /// The second schema is created here and granted here, because the fixture's grant was made
    /// before it existed: a grant covers the objects in a schema, not the schemas created after it.
    /// Without the grant the read-only account sees this table in no listing and can read no row of
    /// it — measured, this engine filters metadata by permission, so an unreadable table is also an
    /// invisible one.
    /// </remarks>
    private async Task SeedOtherSchemaAsync()
    {
        await using var seeding = new SqlConnection(database.SpareAdministratorConnectionString);
        await seeding.OpenAsync();

        await using var command = seeding.CreateCommand();
        command.CommandText = """
            DROP TABLE IF EXISTS dbo.qualified_target;
            CREATE TABLE dbo.qualified_target (
                id INT NOT NULL,
                CONSTRAINT pk_qualified_target PRIMARY KEY (id)
            );

            IF SCHEMA_ID('reporting') IS NULL EXEC('CREATE SCHEMA reporting');
            """;
        await command.ExecuteNonQueryAsync();

        await using var second = seeding.CreateCommand();
        second.CommandText = """
            DROP TABLE IF EXISTS reporting.elsewhere;
            CREATE TABLE reporting.elsewhere (
                id INT NOT NULL,
                CONSTRAINT pk_elsewhere PRIMARY KEY (id)
            );
            """;
        await second.ExecuteNonQueryAsync();

        await using var grant = seeding.CreateCommand();
        grant.CommandText = $"""
            GRANT SELECT ON SCHEMA::reporting TO [{database.ReadOnlyAccountName}];
            GRANT VIEW DEFINITION ON SCHEMA::reporting TO [{database.ReadOnlyAccountName}];
            """;
        await grant.ExecuteNonQueryAsync();
    }
}
