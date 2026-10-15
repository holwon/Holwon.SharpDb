using System.Text.Json;
using SharpDb.Abstractions.Schema;
using SharpDb.Provider.Contract;

namespace SharpDb.Provider.MariaDb.Tests;

/// <summary>
/// The contract assertions the shared suite cannot hold, because Oracle cannot satisfy them.
/// </summary>
/// <remarks>
/// The shared suites keep only what every engine answers identically (see
/// <see cref="ProviderContractSuite"/>), so a behaviour that is not universal is pinned in the
/// engine's own project or it is pinned nowhere. These assertions read the shared schema, and each
/// one is a question Oracle answers differently — or cannot answer at all:
/// <list type="bullet">
/// <item><description>
/// a foreign key's <c>ON UPDATE</c> rule, which Oracle has no syntax for (<c>ORA-00905</c>);
/// </description></item>
/// <item><description>
/// the empty string and the empty blob, which Oracle stores as NULL (<c>''</c> is NULL there, and
/// so is <c>HEXTORAW('')</c>);
/// </description></item>
/// <item><description>
/// a NULL parameter, whose count changes once the empty string beside it becomes NULL;
/// </description></item>
/// <item><description>
/// a SELECT with no <c>FROM</c>, which Oracle 21c refuses (<c>ORA-00923</c>);
/// </description></item>
/// <item><description>
/// an error message naming the object it could not find, which Oracle's <c>ORA-00942</c> does not;
/// </description></item>
/// <item><description>
/// an <c>EXPLAIN</c> that returns a plan, which Oracle spells <c>EXPLAIN PLAN FOR</c> and which
/// writes a table rather than answering.
/// </description></item>
/// </list>
/// </remarks>
/// <param name="database">The seeded container the collection shares.</param>
[Collection(MariaDbContractCollection.Name)]
public sealed class MariaDbSharedSchemaTests(MariaDbContractDatabase database)
{
    /// <summary>
    /// A description reports a foreign key's update rule as the engine holds it.
    /// </summary>
    [SkippableFact]
    public async Task DescribeTable_reports_a_foreign_key_with_its_update_rule()
    {
        database.SkipWhenUnavailable();

        await using var connection = await database.Provider.CreateReadOnlyConnectionAsync(
            database.ConnectionString,
            CancellationToken.None);

        var schema = await database.Provider.DescribeTableAsync(connection, "users", CancellationToken.None);

        Assert.NotNull(schema);
        var foreignKey = Assert.Single(schema.ForeignKeys);
        Assert.Equal(ForeignKeyAction.SetNull, foreignKey.OnUpdate);
    }

    /// <summary>
    /// A result set carries the empty string and the empty blob as themselves.
    /// </summary>
    [SkippableFact]
    public async Task A_query_answers_columns_once_and_rows_as_lists_including_the_empty_values()
    {
        database.SkipWhenUnavailable();

        var result = await QueryTool.For(database).ExecuteQueryAsync(
            "SELECT id, label, ratio, payload, note FROM readings ORDER BY id");

        Assert.NotEqual(true, result.IsError);
        Assert.Equal(
            """{"columns":["id","label","ratio","payload","note"],"rows":[[1,"alpha",1.5,"AQI=",null],[2,"beta",-0.25,"","hello"],[3,"gamma",0,null,""],[4,"delta",2.5,null,"four"],[5,"epsilon",3.5,null,"five"],[6,"zeta",4.5,null,"six"]]}""",
            ToolPayload.Text(result));
    }

    /// <summary>
    /// A parameter may be null, and reaches the database as SQL null.
    /// </summary>
    /// <remarks>
    /// The sentinel the comparison falls back to is what tells the two apart: a null that arrived
    /// as the text <c>"null"</c> would match nothing here. Only the row whose note is really NULL
    /// matches, which is why the count is one — the empty string beside it is not NULL here.
    /// </remarks>
    [SkippableFact]
    public async Task A_parameter_may_be_null()
    {
        database.SkipWhenUnavailable();

        var result = await QueryTool.For(database).ExecuteQueryAsync(
            "SELECT count(*) AS matching FROM readings WHERE coalesce(note, '<null>') = coalesce(@note, '<null>')",
            ToolPayload.Parameters(("note", null)));

        Assert.NotEqual(true, result.IsError);
        Assert.Equal("""{"columns":["matching"],"rows":[[1]]}""", ToolPayload.Text(result));
    }

    /// <summary>
    /// Text outside ASCII is written as itself, not as an escape.
    /// </summary>
    /// <remarks>
    /// A name spelled <c>\u4E2D\u6587</c> has to be decoded in the reader's head before it means
    /// anything, while a name spelled <c>中文</c> means what it says (ADR 0012). The statement
    /// builds the text rather than seeding it, so the shared schema is left as the other
    /// assertions read it.
    /// </remarks>
    [SkippableFact]
    public async Task Text_outside_ascii_is_written_as_itself()
    {
        database.SkipWhenUnavailable();

        var result = await QueryTool.For(database).ExecuteQueryAsync(
            """SELECT '中文' AS 列, '<tag> & "q" + it''s' AS 标""");

        Assert.NotEqual(true, result.IsError);
        Assert.Equal(
            """{"columns":["列","标"],"rows":[["中文","<tag> & \"q\" + it's"]]}""",
            ToolPayload.Text(result));
    }

    /// <summary>
    /// A failure names what it could not find as itself.
    /// </summary>
    /// <remarks>
    /// The raw payload is asserted rather than the message read back out of it, because reading it
    /// back would undo the very thing under test: a JSON reader turns <c>\u4E0D</c> into
    /// <c>不</c>, so a message that had come back escaped would still read correctly once parsed.
    /// </remarks>
    [SkippableFact]
    public async Task A_failure_names_what_it_could_not_find_as_itself()
    {
        database.SkipWhenUnavailable();

        var result = await QueryTool.For(database).ExecuteQueryAsync("SELECT id FROM 不存在");

        Assert.True(result.IsError);
        Assert.Contains("不存在", ToolPayload.Text(result));
    }

    /// <summary>
    /// An EXPLAIN runs and answers with a plan.
    /// </summary>
    /// <remarks>
    /// What the plan holds is the engine's business and its column names differ, so the assertion
    /// is only that a plan came back.
    /// </remarks>
    [SkippableFact]
    public async Task An_explain_of_a_query_answers_with_a_plan()
    {
        database.SkipWhenUnavailable();

        var result = await QueryTool.For(database).ExecuteQueryAsync("EXPLAIN SELECT id FROM readings");

        Assert.NotEqual(true, result.IsError);
        var columns = JsonDocument.Parse(ToolPayload.Text(result)).RootElement.GetProperty("columns");
        Assert.True(columns.GetArrayLength() > 0);
    }

    /// <summary>
    /// A failure names the object the caller got wrong.
    /// </summary>
    /// <remarks>
    /// MariaDB's <c>1146</c> message carries the table's name and <c>1054</c> the column's, so a
    /// caller is told what to correct. Oracle's messages do not, which is why this is asserted here
    /// rather than in the shared suite.
    /// </remarks>
    [SkippableFact]
    public async Task A_failure_names_the_object_it_could_not_find()
    {
        database.SkipWhenUnavailable();

        var missingTable = await QueryTool.For(database).ExecuteQueryAsync("SELECT id FROM no_such_table");
        var missingColumn = await QueryTool.For(database).ExecuteQueryAsync("SELECT no_such_column FROM readings");

        Assert.True(missingTable.IsError);
        Assert.Contains("no_such_table", ToolPayload.Error(missingTable));
        Assert.True(missingColumn.IsError);
        Assert.Contains("no_such_column", ToolPayload.Error(missingColumn));
    }
}
