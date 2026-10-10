using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Protocol;
using SharpDb.Core.Configuration;
using SharpDb.Core.Mcp;

namespace SharpDb.Provider.Sqlite.Tests;

/// <summary>
/// The query Tool against a real SQLite database, observed the way a client observes it: what the
/// payload holds, and whether a call came back as an error.
/// </summary>
/// <remarks>
/// The database is a file the Provider opens read-only, exactly as the running server opens one,
/// so the assertions cover the whole path a query takes — the read-only connection included.
/// <para>
/// Only the outside of the call is asserted: the columns and rows it answers with, and the error
/// it reports. Which SQL the Provider ran and which exception a Driver threw are not part of the
/// contract, and neither is asserted.
/// </para>
/// </remarks>
public class QueryExecutionTests
{
    /// <summary>
    /// The rows every test reads, covering the value kinds a SQLite database can hold.
    /// </summary>
    private const string SeedSql = """
        CREATE TABLE readings (
            id INTEGER PRIMARY KEY,
            label TEXT,
            ratio REAL,
            payload BLOB,
            note TEXT
        );
        INSERT INTO readings VALUES
            (1, 'alpha', 1.5, x'0102', NULL),
            (2, 'beta', -0.25, zeroblob(0), 'hello'),
            (3, 'gamma', 0.0, NULL, ''),
            (4, 'delta', 2.5, NULL, 'four'),
            (5, 'epsilon', 3.5, NULL, 'five'),
            (6, 'zeta', 4.5, NULL, 'six');
        """;

    [Fact]
    public async Task A_query_answers_with_its_columns_and_its_rows()
    {
        // The envelope names each column once and gives every row as a list of values in column
        // order, so a reader never repeats a column name per row.
        using var database = await SqliteTestDatabase.CreateAsync(SeedSql);
        var tool = ToolFor(database);

        var result = await tool.ExecuteQueryAsync(
            "SELECT id, label, ratio, payload, note FROM readings ORDER BY id");

        Assert.NotEqual(true, result.IsError);
        Assert.Equal(
            """{"columns":["id","label","ratio","payload","note"],"rows":[[1,"alpha",1.5,"AQI=",null],[2,"beta",-0.25,"","hello"],[3,"gamma",0,null,""],[4,"delta",2.5,null,"four"],[5,"epsilon",3.5,null,"five"],[6,"zeta",4.5,null,"six"]]}""",
            Payload(result));
    }

    [Fact]
    public async Task A_query_that_matches_nothing_still_names_its_columns()
    {
        // An empty answer is an answer: the caller learns the columns and that there were no rows,
        // rather than having to guess whether the query was understood.
        using var database = await SqliteTestDatabase.CreateAsync(SeedSql);
        var tool = ToolFor(database);

        var result = await tool.ExecuteQueryAsync("SELECT id, label FROM readings WHERE id < 0");

        Assert.NotEqual(true, result.IsError);
        Assert.Equal("""{"columns":["id","label"],"rows":[]}""", Payload(result));
    }

    [Fact]
    public async Task The_configured_default_limit_applies_when_the_caller_asks_for_none()
    {
        using var database = await SqliteTestDatabase.CreateAsync(SeedSql);
        var tool = ToolFor(database, defaultMaxRows: 2);

        var result = await tool.ExecuteQueryAsync("SELECT id FROM readings ORDER BY id");

        Assert.NotEqual(true, result.IsError);
        Assert.Equal("""{"columns":["id"],"rows":[[1],[2]],"truncated":true}""", Payload(result));
    }

    [Fact]
    public async Task A_limit_the_caller_asks_for_is_used()
    {
        using var database = await SqliteTestDatabase.CreateAsync(SeedSql);
        var tool = ToolFor(database);

        var result = await tool.ExecuteQueryAsync("SELECT id FROM readings ORDER BY id", maxRows: 1);

        Assert.NotEqual(true, result.IsError);
        Assert.Equal("""{"columns":["id"],"rows":[[1]],"truncated":true}""", Payload(result));
    }

    [Fact]
    public async Task A_result_that_fits_the_limit_carries_no_truncation_marker()
    {
        // The marker appears only when the limit actually cut the answer short, so its presence
        // means something rather than being noise on every reply.
        using var database = await SqliteTestDatabase.CreateAsync(SeedSql);
        var tool = ToolFor(database);

        var result = await tool.ExecuteQueryAsync("SELECT id FROM readings ORDER BY id", maxRows: 6);

        Assert.NotEqual(true, result.IsError);
        Assert.Equal("""{"columns":["id"],"rows":[[1],[2],[3],[4],[5],[6]]}""", Payload(result));
    }

    [Fact]
    public async Task A_limit_above_the_servers_maximum_is_cut_to_it_without_an_error()
    {
        // The server's maximum is not negotiable, but asking for more is not a mistake worth
        // failing a call over: the caller gets what the server allows and is told it was cut.
        using var database = await SqliteTestDatabase.CreateAsync(SeedSql);
        var tool = ToolFor(database, maxRows: 3);

        var result = await tool.ExecuteQueryAsync("SELECT id FROM readings ORDER BY id", maxRows: 9999);

        Assert.NotEqual(true, result.IsError);
        Assert.Equal("""{"columns":["id"],"rows":[[1],[2],[3]],"truncated":true}""", Payload(result));
    }

    [Fact]
    public async Task A_placeholder_is_filled_from_the_parameter_object()
    {
        // The placeholder carries the @ and the key does not, which is the pair the Tool documents.
        // A fractional value binds as a fraction rather than being rounded on the way in.
        using var database = await SqliteTestDatabase.CreateAsync(SeedSql);
        var tool = ToolFor(database);

        var result = await tool.ExecuteQueryAsync(
            "SELECT label FROM readings WHERE ratio > @ratio ORDER BY id",
            Parameters(("ratio", 1.5)));

        Assert.NotEqual(true, result.IsError);
        Assert.Equal("""{"columns":["label"],"rows":[["delta"],["epsilon"],["zeta"]]}""", Payload(result));
    }

    [Fact]
    public async Task A_parameter_may_be_null()
    {
        // A null is a value like any other, and it must reach the database as SQL NULL rather than
        // as the text "null". The sentinel the comparison falls back to is what tells the two
        // apart: a null that arrived as text would match nothing here.
        using var database = await SqliteTestDatabase.CreateAsync(SeedSql);
        var tool = ToolFor(database);

        var result = await tool.ExecuteQueryAsync(
            "SELECT count(*) FROM readings WHERE coalesce(note, '<null>') = coalesce(@note, '<null>')",
            Parameters(("note", null)));

        Assert.NotEqual(true, result.IsError);
        Assert.Equal("""{"columns":["count(*)"],"rows":[[1]]}""", Payload(result));
    }

    [Fact]
    public async Task A_whole_number_parameter_is_bound_as_a_number()
    {
        using var database = await SqliteTestDatabase.CreateAsync(SeedSql);
        var tool = ToolFor(database);

        var result = await tool.ExecuteQueryAsync(
            "SELECT label FROM readings WHERE id = @id",
            Parameters(("id", 2)));

        Assert.NotEqual(true, result.IsError);
        Assert.Equal("""{"columns":["label"],"rows":[["beta"]]}""", Payload(result));
    }

    [Theory]
    [InlineData("INSERT INTO readings (id) VALUES (99)")]
    [InlineData("UPDATE readings SET label = 'x'")]
    [InlineData("DELETE FROM readings")]
    [InlineData("DROP TABLE readings")]
    [InlineData("CREATE TABLE extra (id INTEGER)")]
    public async Task A_statement_that_is_not_read_only_is_refused_before_it_reaches_the_database(string sql)
    {
        // The refusal happens ahead of the connection, so the database never sees the statement.
        using var database = await SqliteTestDatabase.CreateAsync(SeedSql);
        var tool = ToolFor(database);

        var result = await tool.ExecuteQueryAsync(sql);

        Assert.True(result.IsError);
        Assert.Contains("Statement rejected", Error(result));
        Assert.Contains("Only SELECT, EXPLAIN and SHOW", Error(result));
    }

    [Fact]
    public async Task A_statement_the_database_refuses_is_reported_with_the_databases_own_words()
    {
        // The names the caller needs in order to fix the query must survive: the table it named.
        using var database = await SqliteTestDatabase.CreateAsync(SeedSql);
        var tool = ToolFor(database);

        var result = await tool.ExecuteQueryAsync("SELECT id FROM no_such_table");

        Assert.True(result.IsError);
        Assert.Equal(
            "Database error: SQLite Error 1: 'no such table: no_such_table'.",
            Error(result));
    }

    [Fact]
    public async Task A_column_the_database_does_not_know_is_reported_with_the_databases_own_words()
    {
        // A statement the Guard permits can still be wrong about a column; that answer has to reach
        // the caller so it can correct the query.
        using var database = await SqliteTestDatabase.CreateAsync(SeedSql);
        var tool = ToolFor(database);

        var result = await tool.ExecuteQueryAsync("SELECT no_such_column FROM readings");

        Assert.True(result.IsError);
        Assert.StartsWith("Database error:", Error(result));
        Assert.Contains("no_such_column", Error(result));
    }

    [Fact]
    public async Task A_connection_the_server_cannot_open_is_reported_as_a_failure_not_an_exception()
    {
        // A database that is not there is a business failure like any other: the caller gets a
        // failed result it can read, and nothing escapes as an exception (ADR 0010).
        //
        // Measured on Microsoft.Data.Sqlite 10.0.12: the message for a failed read-only open is
        // "SQLite Error 14: 'unable to open database file'." — the Driver names no file, so the
        // redaction of a path cannot be exercised through it and is asserted at the unit level
        // instead. What is asserted here is the path's absence, whatever the Driver chose to say.
        using var database = new MissingSqliteDatabase();
        var tool = ToolFor(database.ConnectionString);

        var result = await tool.ExecuteQueryAsync("SELECT 1");

        Assert.True(result.IsError);
        Assert.StartsWith("Database error:", Error(result));
        Assert.DoesNotContain(database.DirectoryPath, Error(result), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_placeholder_with_no_parameter_is_reported_as_a_failure_not_an_exception()
    {
        // Writing a placeholder and forgetting its value is the likeliest mistake a caller makes,
        // and the Driver reports it as an InvalidOperationException rather than as a DbException.
        // It still has to arrive as a failed result the caller can read (ADR 0010).
        using var database = await SqliteTestDatabase.CreateAsync(SeedSql);
        var tool = ToolFor(database);

        var result = await tool.ExecuteQueryAsync("SELECT id FROM readings WHERE id = @id");

        Assert.True(result.IsError);
        Assert.StartsWith("Database error:", Error(result));
    }

    [Fact]
    public async Task A_query_that_outlives_its_timeout_reports_the_seconds_and_what_to_do()
    {
        // A recursive query with no useful bound runs far longer than the one second allowed here.
        using var database = await SqliteTestDatabase.CreateAsync(SeedSql);
        var tool = ToolFor(database, timeoutSeconds: 1);

        var result = await tool.ExecuteQueryAsync(
            "WITH RECURSIVE cnt(x) AS (SELECT 1 UNION ALL SELECT x + 1 FROM cnt WHERE x < 20000000) "
            + "SELECT count(*) FROM cnt");

        Assert.True(result.IsError);
        Assert.Contains("timed out after 1 second", Error(result));
        Assert.Contains("SHARPDB__TIMEOUTSECONDS", Error(result));
    }

    /// <summary>
    /// Builds a parameter object from name-and-value pairs.
    /// </summary>
    /// <param name="entries">The names and values, the names carrying no <c>@</c>.</param>
    /// <returns>The object the Tool receives.</returns>
    private static Dictionary<string, JsonElement> Parameters(params (string Name, object? Value)[] entries)
    {
        var parameters = new Dictionary<string, JsonElement>();
        foreach (var (name, value) in entries)
        {
            parameters[name] = JsonSerializer.SerializeToElement(value);
        }

        return parameters;
    }

    /// <summary>
    /// The text a call answered with.
    /// </summary>
    /// <param name="result">The call's result.</param>
    /// <returns>The payload text.</returns>
    private static string Payload(CallToolResult result) =>
        Assert.Single(result.Content.OfType<TextContentBlock>()).Text;

    /// <summary>
    /// The reason a failed call carries, read out of the payload's envelope.
    /// </summary>
    /// <param name="result">The call's result.</param>
    /// <returns>The message the caller reads.</returns>
    private static string Error(CallToolResult result) =>
        JsonDocument.Parse(Payload(result)).RootElement.GetProperty("error").GetString()!;

    /// <summary>
    /// A query Tool serving the given database, with the limits the test wants it to apply.
    /// </summary>
    /// <param name="database">The seeded database the Tool connects to.</param>
    /// <param name="defaultMaxRows">The limit used when the caller asks for none.</param>
    /// <param name="maxRows">The limit no caller may exceed.</param>
    /// <param name="timeoutSeconds">The timeout applied to every query.</param>
    /// <returns>The Tool, wired as the running server wires it.</returns>
    private static QueryTools ToolFor(
        SqliteTestDatabase database,
        int defaultMaxRows = 100,
        int maxRows = 1000,
        int timeoutSeconds = 30) => ToolFor(database.ConnectionString, defaultMaxRows, maxRows, timeoutSeconds);

    /// <summary>
    /// A query Tool serving a database named by a connection string.
    /// </summary>
    /// <param name="connectionString">The connection string the Tool connects with.</param>
    /// <param name="defaultMaxRows">The limit used when the caller asks for none.</param>
    /// <param name="maxRows">The limit no caller may exceed.</param>
    /// <param name="timeoutSeconds">The timeout applied to every query.</param>
    /// <returns>The Tool, wired as the running server wires it.</returns>
    private static QueryTools ToolFor(
        string connectionString,
        int defaultMaxRows = 100,
        int maxRows = 1000,
        int timeoutSeconds = 30) => new(
            new SqliteDatabaseProvider(),
            new SharpDbOptions
            {
                Type = "sqlite",
                ConnectionString = connectionString,
                DefaultMaxRows = defaultMaxRows,
                MaxRows = maxRows,
                TimeoutSeconds = timeoutSeconds,
            },
            NullLogger<QueryTools>.Instance);
}
