using MySqlConnector;
using SharpDb.Core.Mcp;
using SharpDb.Provider.Contract;

namespace SharpDb.Provider.MySql.Tests;

/// <summary>
/// What the MySQL Provider answers that the shared contract suite cannot assert, because no other
/// engine answers it the same way.
/// </summary>
/// <remarks>
/// These are the differences measured against SQLite, and each is pinned here rather than left
/// implicit:
/// <list type="bullet">
/// <item><description>comments round-trip on MySQL and are absent on SQLite;</description></item>
/// <item><description>a default value arrives as the text the server stored;</description></item>
/// <item><description>declared types are reported as MySQL declares them, <c>varchar(50)</c> and
/// all;</description></item>
/// <item><description>a <c>timestamp … ON UPDATE CURRENT_TIMESTAMP</c> column is not an
/// auto-increment, though its <c>EXTRA</c> text is not empty;</description></item>
/// <item><description>a view's <c>TABLE_COMMENT</c> is the literal <c>VIEW</c>, which is not a
/// comment and must not be reported as one;</description></item>
/// <item><description>only the connection's own schema is introspected, even when another schema
/// is visible;</description></item>
/// <item><description>the value kinds that diverge — boolean, scaled decimal, datetime, unsigned
/// big integer.</description></item>
/// </list>
/// <para>
/// These tests seed tables of their own, so they run against the container's spare database rather
/// than the shared one: the shared suites assert the exact set of tables in the shared schema, and
/// a table seeded beside it would break the listing for whichever suite ran afterwards.
/// </para>
/// </remarks>
/// <param name="database">The container the collection shares.</param>
[Collection(MySqlContractCollection.Name)]
public sealed class MySqlSpecificTests(MySqlContractDatabase database)
{
    /// <summary>
    /// A table whose every facet the shared schema deliberately leaves out, and a view over it.
    /// </summary>
    private const string Ddl = """
        DROP VIEW IF EXISTS annotated_view;
        DROP TABLE IF EXISTS annotated;
        CREATE TABLE annotated (
            id INT NOT NULL AUTO_INCREMENT,
            title VARCHAR(50) NOT NULL COMMENT 'the title column',
            stamp TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
            rating TINYINT,
            price DECIMAL(10,2),
            body TEXT,
            PRIMARY KEY (id)
        ) COMMENT='a table comment';
        CREATE VIEW annotated_view AS SELECT id, title FROM annotated;
        """;

    /// <summary>
    /// MySQL keeps object comments, and both Tools report them.
    /// </summary>
    [SkippableFact]
    public async Task Table_and_column_comments_reach_the_client()
    {
        database.SkipWhenUnavailable();
        await SeedAsync();

        await using var connection = await OpenSpareAsync();
        var schema = await database.Provider.DescribeTableAsync(connection, "annotated", CancellationToken.None);

        Assert.NotNull(schema);
        Assert.Equal("a table comment", schema.Comment);
        Assert.Equal("the title column", schema.Columns.Single(column => column.Name == "title").Comment);
        Assert.Null(schema.Columns.Single(column => column.Name == "body").Comment);
    }

    /// <summary>
    /// A column with no comment reports none.
    /// </summary>
    [SkippableFact]
    public async Task An_uncommented_column_reports_no_comment()
    {
        database.SkipWhenUnavailable();
        await SeedAsync();

        await using var connection = await OpenSpareAsync();
        var schema = await database.Provider.DescribeTableAsync(connection, "annotated", CancellationToken.None);

        Assert.NotNull(schema);
        Assert.Null(schema.Columns.Single(column => column.Name == "rating").Comment);
    }

    /// <summary>
    /// A view's comment is not the server's own word for its kind.
    /// </summary>
    /// <remarks>
    /// Measured on MySQL 8.0: <c>INFORMATION_SCHEMA.TABLES.TABLE_COMMENT</c> holds the literal
    /// text <c>VIEW</c> for a view, because <c>CREATE VIEW</c> has no <c>COMMENT</c> clause for
    /// the server to fill in. It states the object's kind, not its purpose, and a Provider that
    /// passed it through would report a comment that no one wrote.
    /// </remarks>
    [SkippableFact]
    public async Task A_views_comment_is_not_the_servers_word_for_view()
    {
        database.SkipWhenUnavailable();
        await SeedAsync();

        await using var connection = await OpenSpareAsync();

        var listed = await database.Provider.ListTablesAsync(connection, CancellationToken.None);
        var view = listed.Single(table => table.Name == "annotated_view");

        Assert.Null(view.Comment);
        Assert.DoesNotContain(listed, table => table.Comment == "VIEW");
    }

    /// <summary>
    /// Declared types are reported as MySQL declares them.
    /// </summary>
    [SkippableFact]
    public async Task Declared_types_are_reported_as_MySQL_declares_them()
    {
        database.SkipWhenUnavailable();
        await SeedAsync();

        await using var connection = await OpenSpareAsync();
        var schema = await database.Provider.DescribeTableAsync(connection, "annotated", CancellationToken.None);

        Assert.NotNull(schema);
        Assert.Equal("varchar(50)", schema.Columns.Single(column => column.Name == "title").DataType);
        Assert.Equal("decimal(10,2)", schema.Columns.Single(column => column.Name == "price").DataType);
        Assert.Equal("tinyint", schema.Columns.Single(column => column.Name == "rating").DataType);
    }

    /// <summary>
    /// A default value is reported, and a column without one reports none.
    /// </summary>
    [SkippableFact]
    public async Task Default_values_are_reported()
    {
        database.SkipWhenUnavailable();
        await SeedAsync();

        await using var connection = await OpenSpareAsync();
        var schema = await database.Provider.DescribeTableAsync(connection, "annotated", CancellationToken.None);

        Assert.NotNull(schema);
        Assert.Equal(
            "CURRENT_TIMESTAMP",
            schema.Columns.Single(column => column.Name == "stamp").DefaultValue);
        Assert.Null(schema.Columns.Single(column => column.Name == "body").DefaultValue);
    }

    /// <summary>
    /// A column whose <c>EXTRA</c> text is not empty is not thereby an auto-increment.
    /// </summary>
    /// <remarks>
    /// <c>EXTRA</c> carries several flags at once — <c>on update CURRENT_TIMESTAMP</c> is one of
    /// them — so the flag has to be matched as a whole token. A Provider that looked for a
    /// non-empty <c>EXTRA</c>, or for the substring <c>auto</c>, would call this timestamp column
    /// generated on insert.
    /// </remarks>
    [SkippableFact]
    public async Task A_timestamp_with_an_on_update_clause_is_not_an_autoincrement()
    {
        database.SkipWhenUnavailable();
        await SeedAsync();

        await using var connection = await OpenSpareAsync();
        var schema = await database.Provider.DescribeTableAsync(connection, "annotated", CancellationToken.None);

        Assert.NotNull(schema);
        Assert.True(schema.Columns.Single(column => column.Name == "id").IsAutoIncrement);
        Assert.All(
            schema.Columns.Where(column => column.Name != "id"),
            column => Assert.False(column.IsAutoIncrement));
    }

    /// <summary>
    /// A second schema that the connection can see is not introspected.
    /// </summary>
    /// <remarks>
    /// The server is bound to one database (ADR 0009): the other names in the database listing are
    /// reported for information and nothing else. This test creates a schema the connection's
    /// account can read, puts a table in it, and asserts that neither the listing nor a description
    /// can reach it.
    /// </remarks>
    [SkippableFact]
    public async Task Another_visible_schema_is_not_introspected()
    {
        database.SkipWhenUnavailable();

        await using (var administrator = new MySqlConnection(database.RootConnectionString))
        {
            await administrator.OpenAsync();

            await using var command = administrator.CreateCommand();
            command.CommandText = """
                DROP DATABASE IF EXISTS sharpdb_other;
                CREATE DATABASE sharpdb_other;
                CREATE TABLE sharpdb_other.decoy (id INT PRIMARY KEY);
                GRANT SELECT ON sharpdb_other.* TO 'sharpdb'@'%';
                """;
            await command.ExecuteNonQueryAsync();
        }

        await using var connection = await OpenSpareAsync();

        // The other schema really is visible: if it were not, the assertions below would pass for
        // the wrong reason.
        var databases = await database.Provider.ListDatabasesAsync(connection, CancellationToken.None);
        Assert.Contains("sharpdb_other", databases);

        var listed = await database.Provider.ListTablesAsync(connection, CancellationToken.None);
        Assert.DoesNotContain(listed, table => table.Name == "decoy");

        var decoy = await database.Provider.DescribeTableAsync(connection, "decoy", CancellationToken.None);
        Assert.Null(decoy);
    }

    /// <summary>
    /// The session the Provider opens is in the server's read-only mode.
    /// </summary>
    /// <remarks>
    /// The shared suite asserts the guarantee by attempting a write; this asserts the mechanism
    /// behind it, which is the one thing SQLite cannot show — the mode is a session variable the
    /// server reports, so the switch the Provider performs after connecting is visible.
    /// </remarks>
    [SkippableFact]
    public async Task The_session_the_provider_opens_is_read_only_at_the_server()
    {
        database.SkipWhenUnavailable();

        await using var connection = await OpenSpareAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT @@session.transaction_read_only";

        Assert.Equal(1L, Convert.ToInt64(await command.ExecuteScalarAsync()));
    }

    /// <summary>
    /// The query Tool runs MySQL's own SHOW statements.
    /// </summary>
    [SkippableFact]
    public async Task The_query_tool_runs_show_statements()
    {
        database.SkipWhenUnavailable();
        await SeedAsync();

        var result = await QueryOnSpareAsync().ExecuteQueryAsync("SHOW TABLES");

        Assert.NotEqual(true, result.IsError);
        Assert.Contains("annotated", ToolPayload.Text(result));
    }

    /// <summary>
    /// MySQL's value kinds reach the payload the way the Tool documents.
    /// </summary>
    /// <remarks>
    /// This is where MySQL and SQLite genuinely differ, and where the mapping is asserted for
    /// MySQL alone: a <c>TINYINT(1)</c> arrives as a JSON boolean, a <c>DECIMAL</c> keeps its
    /// scale, a <c>DATETIME(6)</c> becomes an ISO 8601 string with the microseconds the column
    /// declared, and a <c>BIGINT UNSIGNED</c> arrives as the whole number rather than a rounded
    /// one.
    /// </remarks>
    [SkippableFact]
    public async Task MySQL_value_kinds_reach_the_payload_as_documented()
    {
        database.SkipWhenUnavailable();

        await using (var writable = new MySqlConnection(database.SpareConnectionString))
        {
            await writable.OpenAsync();

            await using var command = writable.CreateCommand();
            command.CommandText = """
                DROP TABLE IF EXISTS typed;
                CREATE TABLE typed (
                    flag TINYINT(1),
                    price DECIMAL(10,2),
                    stamp DATETIME(6),
                    big BIGINT UNSIGNED,
                    payload VARBINARY(10)
                );
                INSERT INTO typed VALUES (1, 12.34, '2024-01-02 03:04:05.123456', 18446744073709551615, X'0102');
                """;
            await command.ExecuteNonQueryAsync();
        }

        var result = await QueryOnSpareAsync().ExecuteQueryAsync("SELECT * FROM typed");

        Assert.NotEqual(true, result.IsError);
        Assert.Equal(
            """{"columns":["flag","price","stamp","big","payload"],"rows":[[true,12.34,"2024-01-02T03:04:05.1234560",18446744073709551615,"AQI="]]}""",
            ToolPayload.Text(result));
    }

    /// <summary>
    /// A database holding no objects lists nothing.
    /// </summary>
    [SkippableFact]
    public async Task An_empty_database_lists_no_tables()
    {
        database.SkipWhenUnavailable();

        await using var connection = await database.Provider.CreateReadOnlyConnectionAsync(
            database.EmptyConnectionString,
            CancellationToken.None);

        var listed = await database.Provider.ListTablesAsync(connection, CancellationToken.None);

        Assert.Empty(listed);
    }

    /// <summary>
    /// Opens a read-only connection to the spare database.
    /// </summary>
    /// <returns>The open connection, which the test disposes.</returns>
    private async Task<System.Data.Common.DbConnection> OpenSpareAsync() =>
        await database.Provider.CreateReadOnlyConnectionAsync(database.SpareConnectionString, CancellationToken.None);

    /// <summary>
    /// The query Tool bound to the spare database.
    /// </summary>
    /// <returns>The Tool, wired as the running server wires it.</returns>
    private QueryTools QueryOnSpareAsync() =>
        QueryTool.For(database.Provider, database.SpareConnectionString, database.Type, timeoutSeconds: 30);

    /// <summary>
    /// Seeds the annotated table and its view into the spare database.
    /// </summary>
    /// <returns>The work that seeds them.</returns>
    private async Task SeedAsync()
    {
        await using var connection = new MySqlConnection(database.SpareConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = Ddl;
        await command.ExecuteNonQueryAsync();
    }
}
