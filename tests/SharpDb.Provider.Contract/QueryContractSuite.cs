using SharpDb.Core.Mcp;

namespace SharpDb.Provider.Contract;

/// <summary>
/// The query Tool's contract, asserted against a real database: the payload a call answers with,
/// the limits it applies, the parameters it binds, and the failures it reports.
/// </summary>
/// <remarks>
/// The Tool is Core's, but what it produces depends on the connection the Provider hands it, so
/// the same assertions run against every engine — the JSON of a result set, the marker on a cut
/// answer, a parameter reaching the database, and a database error surviving to the caller are all
/// statements about the pair, not about either half.
/// <para>
/// What this suite holds is only the <em>common denominator</em>: an assertion stays here when
/// every engine answers it identically, and moves to the engine's own project when even one
/// engine's physical semantics cannot produce it — an empty string, a missing <c>FROM</c>, an
/// object name in an error message, or an <c>EXPLAIN</c> that returns a plan. The shared suite
/// never asks a question a Provider cannot answer truthfully.
/// </para>
/// <para>
/// Only the outside of a call is asserted: the payload a client receives, and whether the call
/// came back as an error. Which SQL the Provider ran and which exception a Driver threw are not
/// part of the contract, and neither is asserted.
/// </para>
/// </remarks>
/// <param name="database">The engine under contract, seeded and ready.</param>
public abstract class QueryContractSuite(IContractDatabase database)
{
    /// <summary>
    /// The engine under contract, handed in by the concrete run.
    /// </summary>
    /// <value>
    /// The fixture whose database every call in this suite runs against.
    /// </value>
    protected IContractDatabase Database { get; } = database;

    /// <summary>
    /// The Tool, wired the way the running server wires it, with a one-second timeout so the
    /// timeout test does not have to wait out a default.
    /// </summary>
    /// <returns>The query Tool under test.</returns>
    protected QueryTools Query() => QueryTool.For(Database);

    /// <summary>
    /// A result set is answered as the columns once and the rows as lists of values.
    /// </summary>
    /// <remarks>
    /// The exact JSON is asserted, not a shape resembling it: the envelope is the whole point of
    /// the design — each column is named once and a row is a list in column order — and every
    /// engine must produce it byte for byte, because a client should not have to care which
    /// database answered.
    /// <para>
    /// The row covers what every engine can hold identically: a whole number, a fraction and text.
    /// The empty string and the empty blob are deliberately absent, because Oracle stores both as
    /// NULL — measured, <c>''</c> is NULL there and <c>HEXTORAW('')</c> is too — so an assertion
    /// naming either could not be shared. The engines that can hold them assert them in their own
    /// projects, against the same seeded rows.
    /// </para>
    /// </remarks>
    [SkippableFact]
    public async Task A_query_answers_columns_once_and_rows_as_lists()
    {
        Database.SkipWhenUnavailable();

        var result = await Query().ExecuteQueryAsync(
            "SELECT id, label, ratio FROM readings ORDER BY id");

        Assert.NotEqual(true, result.IsError);
        Assert.Equal(
            """{"columns":["id","label","ratio"],"rows":[[1,"alpha",1.5],[2,"beta",-0.25],[3,"gamma",0],[4,"delta",2.5],[5,"epsilon",3.5],[6,"zeta",4.5]]}""",
            ToolPayload.Text(result));
    }

    /// <summary>
    /// A query that matches nothing still names its columns.
    /// </summary>
    /// <remarks>
    /// An empty answer is an answer: the caller learns the columns and that there were no rows,
    /// rather than having to guess whether the query was understood.
    /// </remarks>
    [SkippableFact]
    public async Task A_query_that_matches_nothing_still_names_its_columns()
    {
        Database.SkipWhenUnavailable();

        var result = await Query().ExecuteQueryAsync("SELECT id, label FROM readings WHERE id < 0");

        Assert.NotEqual(true, result.IsError);
        Assert.Equal("""{"columns":["id","label"],"rows":[]}""", ToolPayload.Text(result));
    }

    /// <summary>
    /// The configured default limit applies when the caller asks for none.
    /// </summary>
    [SkippableFact]
    public async Task The_configured_default_limit_applies_when_the_caller_asks_for_none()
    {
        Database.SkipWhenUnavailable();

        var tool = QueryTool.For(Database, timeoutSeconds: 30, defaultMaxRows: 2);

        var result = await tool.ExecuteQueryAsync("SELECT id FROM readings ORDER BY id");

        Assert.NotEqual(true, result.IsError);
        Assert.Equal("""{"columns":["id"],"rows":[[1],[2]],"truncated":true}""", ToolPayload.Text(result));
    }

    /// <summary>
    /// A limit the caller asks for is the limit that applies.
    /// </summary>
    [SkippableFact]
    public async Task A_limit_the_caller_asks_for_is_used()
    {
        Database.SkipWhenUnavailable();

        var result = await Query().ExecuteQueryAsync("SELECT id FROM readings ORDER BY id", maxRows: 1);

        Assert.NotEqual(true, result.IsError);
        Assert.Equal("""{"columns":["id"],"rows":[[1]],"truncated":true}""", ToolPayload.Text(result));
    }

    /// <summary>
    /// A result that fits the limit carries no truncation marker.
    /// </summary>
    /// <remarks>
    /// The marker appears only when the limit actually cut the answer short, so its presence means
    /// something rather than being noise on every reply.
    /// </remarks>
    [SkippableFact]
    public async Task A_result_that_fits_the_limit_carries_no_truncation_marker()
    {
        Database.SkipWhenUnavailable();

        var result = await Query().ExecuteQueryAsync("SELECT id FROM readings ORDER BY id", maxRows: 6);

        Assert.NotEqual(true, result.IsError);
        Assert.Equal("""{"columns":["id"],"rows":[[1],[2],[3],[4],[5],[6]]}""", ToolPayload.Text(result));
    }

    /// <summary>
    /// A limit above the server's maximum is cut to it without an error.
    /// </summary>
    /// <remarks>
    /// The server's maximum is not negotiable, but asking for more is not a mistake worth failing
    /// a call over: the caller gets what the server allows and is told it was cut.
    /// </remarks>
    [SkippableFact]
    public async Task A_limit_above_the_servers_maximum_is_cut_to_it_without_an_error()
    {
        Database.SkipWhenUnavailable();

        var tool = QueryTool.For(Database, timeoutSeconds: 30, maxRows: 3);

        var result = await tool.ExecuteQueryAsync("SELECT id FROM readings ORDER BY id", maxRows: 9999);

        Assert.NotEqual(true, result.IsError);
        Assert.Equal("""{"columns":["id"],"rows":[[1],[2],[3]],"truncated":true}""", ToolPayload.Text(result));
    }

    /// <summary>
    /// A placeholder is filled from the parameter object.
    /// </summary>
    /// <remarks>
    /// The placeholder carries the <c>@</c> and the key does not, which is the pair the Tool
    /// documents. A fractional value binds as a fraction rather than being rounded on the way in.
    /// </remarks>
    [SkippableFact]
    public async Task A_placeholder_is_filled_from_the_parameter_object()
    {
        Database.SkipWhenUnavailable();

        var result = await Query().ExecuteQueryAsync(
            "SELECT label FROM readings WHERE ratio > @ratio ORDER BY id",
            ToolPayload.Parameters(("ratio", 1.5)));

        Assert.NotEqual(true, result.IsError);
        Assert.Equal("""{"columns":["label"],"rows":[["delta"],["epsilon"],["zeta"]]}""", ToolPayload.Text(result));
    }

    /// <summary>
    /// A whole-number parameter binds as a number.
    /// </summary>
    [SkippableFact]
    public async Task A_whole_number_parameter_is_bound_as_a_number()
    {
        Database.SkipWhenUnavailable();

        var result = await Query().ExecuteQueryAsync(
            "SELECT label FROM readings WHERE id = @id",
            ToolPayload.Parameters(("id", 2)));

        Assert.NotEqual(true, result.IsError);
        Assert.Equal("""{"columns":["label"],"rows":[["beta"]]}""", ToolPayload.Text(result));
    }

    /// <summary>
    /// A common table expression runs as the read-only query it is.
    /// </summary>
    [SkippableFact]
    public async Task A_query_written_as_a_common_table_expression_runs()
    {
        Database.SkipWhenUnavailable();

        var result = await Query().ExecuteQueryAsync(
            "WITH doubled AS (SELECT id * 2 AS d FROM readings WHERE id = 1) SELECT d FROM doubled");

        Assert.NotEqual(true, result.IsError);
        Assert.Equal("""{"columns":["d"],"rows":[[2]]}""", ToolPayload.Text(result));
    }

    /// <summary>
    /// A statement that is not read-only is refused before it reaches the database.
    /// </summary>
    /// <remarks>
    /// The refusal happens ahead of the connection — the Guard's decision does not depend on which
    /// engine is behind it — and the wording tells the caller what it may send instead.
    /// </remarks>
    [SkippableFact]
    public async Task A_statement_that_is_not_read_only_is_refused()
    {
        Database.SkipWhenUnavailable();

        var result = await Query().ExecuteQueryAsync("INSERT INTO orgs (name) VALUES ('refused')");

        Assert.True(result.IsError);
        Assert.Contains("Statement rejected", ToolPayload.Error(result));
        Assert.Contains("Only SELECT, EXPLAIN and SHOW", ToolPayload.Error(result));
    }

    /// <summary>
    /// A statement the database refuses is reported with the database's own words.
    /// </summary>
    /// <remarks>
    /// The name the caller needs in order to fix the query must survive the trip: the table it
    /// named, in the message the server wrote about it.
    /// </remarks>
    [SkippableFact]
    public async Task A_statement_the_database_refuses_is_reported_with_its_words()
    {
        Database.SkipWhenUnavailable();

        var result = await Query().ExecuteQueryAsync("SELECT id FROM no_such_table");

        Assert.True(result.IsError);
        Assert.StartsWith("Database error:", ToolPayload.Error(result));
        Assert.Contains("no_such_table", ToolPayload.Error(result));
    }

    /// <summary>
    /// A question about a column the database does not know is answered with the database's words.
    /// </summary>
    /// <remarks>
    /// A statement the Guard permits can still be wrong about a column, and the answer has to reach
    /// the caller so it can correct the query rather than guess.
    /// </remarks>
    [SkippableFact]
    public async Task A_column_the_database_does_not_know_is_reported_with_its_words()
    {
        Database.SkipWhenUnavailable();

        var result = await Query().ExecuteQueryAsync("SELECT no_such_column FROM readings");

        Assert.True(result.IsError);
        Assert.StartsWith("Database error:", ToolPayload.Error(result));
        Assert.Contains("no_such_column", ToolPayload.Error(result));
    }

    /// <summary>
    /// A placeholder with no parameter is refused, and the refusal names it.
    /// </summary>
    /// <remarks>
    /// Writing a placeholder and forgetting its value is the likeliest mistake a caller makes, and
    /// the answer has to say which name is missing so it can be corrected (ADR 0010).
    /// <para>
    /// The refusal is Core's, and deliberately so. Measured, the three engines do three different
    /// things with an unbound placeholder — SQLite raises <c>InvalidOperationException</c>, MySQL
    /// raises <c>MySqlException</c>, and PostgreSQL does neither: <c>@</c> is also its
    /// absolute-value operator there, so <c>WHERE id = @id</c> is parsed as <c>WHERE id = abs(id)</c>,
    /// the statement succeeds, and the caller receives rows it never asked for. Refusing it once in
    /// Core is what turns a silent wrong answer into a sentence, and what makes all three engines
    /// answer the same way.
    /// </para>
    /// </remarks>
    [SkippableFact]
    public async Task A_placeholder_with_no_parameter_is_refused_by_name()
    {
        Database.SkipWhenUnavailable();

        var result = await Query().ExecuteQueryAsync("SELECT id FROM readings WHERE id = @id");

        Assert.True(result.IsError);
        Assert.Contains("@id", ToolPayload.Error(result));
    }

    /// <summary>
    /// A placeholdered query runs when its value is supplied.
    /// </summary>
    /// <remarks>
    /// The control for the assertion above: the refusal must be about the missing value and not
    /// about the placeholder's presence, or a caller would be unable to use parameters at all.
    /// </remarks>
    [SkippableFact]
    public async Task A_placeholdered_query_runs_when_its_value_is_supplied()
    {
        Database.SkipWhenUnavailable();

        var result = await Query().ExecuteQueryAsync(
            "SELECT id FROM readings WHERE id = @id AND id IS NOT NULL",
            ToolPayload.Parameters(("id", 1)));

        Assert.NotEqual(true, result.IsError);
        Assert.Equal("""{"columns":["id"],"rows":[[1]]}""", ToolPayload.Text(result));
    }

    /// <summary>
    /// A placeholder written inside a string is data, not a missing value.
    /// </summary>
    /// <remarks>
    /// The refusal reads the statement, so it has to tell a placeholder from the same characters
    /// in a literal. A caller comparing a column to the text <c>'@id'</c> is asking a question
    /// about data, and refusing it would be refusing a statement for saying a word.
    /// </remarks>
    [SkippableFact]
    public async Task A_placeholder_inside_a_string_literal_is_not_a_missing_value()
    {
        Database.SkipWhenUnavailable();

        var result = await Query().ExecuteQueryAsync("SELECT id FROM readings WHERE note = '@id'");

        Assert.NotEqual(true, result.IsError);
        Assert.Equal("""{"columns":["id"],"rows":[]}""", ToolPayload.Text(result));
    }

    /// <summary>
    /// An error message carries neither a secret nor a path of this machine.
    /// </summary>
    /// <remarks>
    /// ADR 0010 keeps two things out of every message a caller reads: the connection string's
    /// credentials, and file paths. Each engine supplies the value it can vouch for — MySQL and
    /// PostgreSQL their passwords, SQLite its database file's directory — and no message carries it
    /// today, measured. So this assertion is a tripwire rather than a reproduction: it fires if a
    /// Driver or a server version starts including what a reader must never receive. That is the
    /// most a test can do where the message cannot be provoked to contain the value in the first
    /// place, and it runs on every engine, which is what the ticket's redaction rule asks for.
    /// </remarks>
    [SkippableFact]
    public async Task An_error_message_discloses_nothing_sensitive()
    {
        Database.SkipWhenUnavailable();

        var result = await Query().ExecuteQueryAsync("SELECT id FROM no_such_table");

        Assert.True(result.IsError);
        Assert.DoesNotContain(Database.SensitiveText, ToolPayload.Error(result), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A query that outlives its timeout reports the timeout and what to do about it.
    /// </summary>
    /// <remarks>
    /// The statement is the engine's own — there is no way to make one statement slow on both —
    /// and it is part of what the fixture supplies.
    /// <para>
    /// MySQL interrupts the statement when the command's timeout expires, while SQLite ignores
    /// every interruption and is stopped only by Core's deadline once it returns; either way the
    /// caller must receive the same timeout message.
    /// </para>
    /// </remarks>
    [SkippableFact]
    public async Task A_query_that_outlives_its_timeout_reports_the_timeout()
    {
        Database.SkipWhenUnavailable();

        var result = await Query().ExecuteQueryAsync(Database.SlowQuery);

        Assert.True(result.IsError);
        Assert.Contains("timed out after 1 second", ToolPayload.Error(result));
        Assert.Contains("SHARPDB__TIMEOUTSECONDS", ToolPayload.Error(result));
    }
}
