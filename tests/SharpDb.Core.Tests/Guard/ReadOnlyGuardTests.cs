using SharpDb.Core.Guard;

namespace SharpDb.Core.Tests.Guard;

/// <summary>
/// The read-only policy, observed through its public answer: given one statement's text, may it
/// run, and if not, what is the reason the client reads.
/// </summary>
/// <remarks>
/// The assertions name the decision and the reason text, never the shape of a syntax tree — the
/// policy may be reimplemented without touching a single test.
/// </remarks>
public class ReadOnlyGuardTests
{
    [Theory]
    [InlineData("SELECT 1")]
    [InlineData("SELECT * FROM users")]
    [InlineData("SELECT id, name FROM users WHERE id = @id")]
    [InlineData("SELECT count(*) FROM users GROUP BY role HAVING count(*) > 1")]
    [InlineData("SELECT u.name, o.total FROM users u JOIN orders o ON o.user_id = u.id")]
    [InlineData("SELECT 1 UNION SELECT 2")]
    [InlineData("(SELECT 1) UNION (SELECT 2)")]
    [InlineData("(SELECT 1)")]
    [InlineData("SELECT * FROM (SELECT id FROM users) AS nested")]
    public void A_read_only_query_is_allowed(string sql)
    {
        var decision = ReadOnlyGuard.Inspect(sql);

        Assert.True(decision.IsAllowed);
        Assert.Null(decision.Reason);
    }

    [Theory]
    [InlineData("INSERT INTO t VALUES (1)", "Insert")]
    [InlineData("UPDATE t SET a = 1", "Update")]
    [InlineData("DELETE FROM t", "Delete")]
    [InlineData("DROP TABLE t", "Drop")]
    [InlineData("TRUNCATE TABLE t", "Truncate")]
    [InlineData("CREATE TABLE t (a INT)", "CreateTable")]
    public void A_write_statement_is_rejected_and_named(string sql, string kind)
    {
        var decision = ReadOnlyGuard.Inspect(sql);

        Assert.False(decision.IsAllowed);
        Assert.Contains(kind, decision.Reason!);
    }

    [Fact]
    public void A_rejection_tells_the_client_which_statements_are_permitted()
    {
        var decision = ReadOnlyGuard.Inspect("DROP TABLE t");

        Assert.Contains("Only SELECT, EXPLAIN and SHOW statements are permitted", decision.Reason!);
    }

    [Theory]
    [InlineData("SELECT 1; DROP TABLE users")]
    [InlineData("SELECT 1;SELECT 2")]
    [InlineData("SELECT 1; INSERT INTO t VALUES (1)")]
    public void Stacked_statements_are_rejected(string sql)
    {
        var decision = ReadOnlyGuard.Inspect(sql);

        Assert.False(decision.IsAllowed);
        Assert.Contains("only one statement", decision.Reason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_write_hidden_behind_a_comment_is_rejected()
    {
        // The leading comment would defeat any check that looks at the first word, and the
        // statement kind it hides is the one that matters.
        var decision = ReadOnlyGuard.Inspect("/**/ INSERT INTO t VALUES (1)");

        Assert.False(decision.IsAllowed);
        Assert.Contains("Insert", decision.Reason!);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void Empty_input_is_rejected(string sql)
    {
        var decision = ReadOnlyGuard.Inspect(sql);

        Assert.False(decision.IsAllowed);
        Assert.Contains("empty", decision.Reason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_null_statement_is_rejected()
    {
        Assert.False(ReadOnlyGuard.Inspect(null).IsAllowed);
    }

    [Theory]
    [InlineData("SELECT * FROM")]
    [InlineData("SELECT 'unterminated")]
    [InlineData("(((")]
    public void Text_that_is_not_a_statement_is_rejected(string sql)
    {
        var decision = ReadOnlyGuard.Inspect(sql);

        Assert.False(decision.IsAllowed);
        Assert.Contains("could not be parsed", decision.Reason!);
    }

    [Fact]
    public void A_grant_is_rejected_as_an_unrecognised_write()
    {
        // A statement kind outside the write list the parser knows: it is refused because the
        // permit list does not name it, not because anything recognises it as a write.
        var decision = ReadOnlyGuard.Inspect("GRANT SELECT ON t TO u");

        Assert.False(decision.IsAllowed);
        Assert.Contains("Grant", decision.Reason!);
    }

    [Theory]
    [InlineData("EXPLAIN SELECT 1")]
    [InlineData("EXPLAIN SELECT * FROM users WHERE id = @id")]
    [InlineData("EXPLAIN VERBOSE SELECT 1")]
    [InlineData("EXPLAIN QUERY PLAN SELECT 1")]
    public void Explaining_a_read_only_query_is_allowed(string sql)
    {
        Assert.True(ReadOnlyGuard.Inspect(sql).IsAllowed);
    }

    [Theory]
    [InlineData("EXPLAIN INSERT INTO t VALUES (1)", "Insert")]
    [InlineData("EXPLAIN UPDATE t SET a = 1", "Update")]
    [InlineData("EXPLAIN DELETE FROM t", "Delete")]
    [InlineData("EXPLAIN CREATE TABLE t (a INT)", "CreateTable")]
    public void Explaining_a_write_is_rejected(string sql, string kind)
    {
        // The statement behind EXPLAIN is classified as if it stood alone, so borrowing EXPLAIN
        // to run a write does not get the write past the Guard.
        var decision = ReadOnlyGuard.Inspect(sql);

        Assert.False(decision.IsAllowed);
        Assert.Contains(kind, decision.Reason!);
    }

    [Theory]
    [InlineData("EXPLAIN ANALYZE INSERT INTO t VALUES (1)")]
    [InlineData("EXPLAIN ANALYZE UPDATE t SET a = 1")]
    [InlineData("EXPLAIN (ANALYZE) INSERT INTO t VALUES (1)")]
    public void Analyze_cannot_be_borrowed_to_run_a_write(string sql)
    {
        // ANALYZE runs the statement it explains, so EXPLAIN ANALYZE is only permitted when what
        // it explains is itself permitted.
        var decision = ReadOnlyGuard.Inspect(sql);

        Assert.False(decision.IsAllowed);
        Assert.Contains("Insert", ReadOnlyGuard.Inspect("EXPLAIN ANALYZE INSERT INTO t VALUES (1)").Reason!);
    }

    [Theory]
    [InlineData("EXPLAIN ANALYZE SELECT 1")]
    [InlineData("EXPLAIN (ANALYZE) SELECT 1")]
    public void Analyze_of_a_read_only_query_is_allowed(string sql)
    {
        // ANALYZE here runs a SELECT, which is permitted, so the deadline-free spelling question
        // does not arise: the inner statement decides, and both spellings agree.
        Assert.True(ReadOnlyGuard.Inspect(sql).IsAllowed);
    }

    [Theory]
    [InlineData("EXPLAIN t")]
    [InlineData("DESCRIBE t")]
    [InlineData("DESC t")]
    public void Describing_a_table_is_rejected_because_it_is_not_a_query_plan(string sql)
    {
        // The parser models a bare `EXPLAIN t` as a different statement from `EXPLAIN SELECT`,
        // and it carries no inner statement to inspect, so it is not in the permit list.
        var decision = ReadOnlyGuard.Inspect(sql);

        Assert.False(decision.IsAllowed);
        Assert.Contains("ExplainTable", decision.Reason!);
    }

    [Theory]
    [InlineData("SHOW TABLES")]
    [InlineData("SHOW FULL TABLES")]
    [InlineData("SHOW DATABASES")]
    [InlineData("SHOW COLUMNS FROM users")]
    [InlineData("SHOW CREATE TABLE users")]
    [InlineData("SHOW VARIABLES")]
    [InlineData("SHOW VARIABLES LIKE 'version%'")]
    [InlineData("SHOW STATUS")]
    [InlineData("SHOW ENGINES")]
    public void Showing_server_state_is_allowed(string sql)
    {
        Assert.True(ReadOnlyGuard.Inspect(sql).IsAllowed);
    }

    [Fact]
    public void A_statement_kind_the_parser_does_not_know_as_read_only_is_rejected()
    {
        // The permit list is what allows, so a kind nobody thought about falls to the refusal
        // side. This is the direction a mistake must point: a refusal is recoverable, a write is not.
        var decision = ReadOnlyGuard.Inspect("SET @x = 1");

        Assert.False(decision.IsAllowed);
        Assert.Contains("SetVariable", decision.Reason!);
    }

    [Fact]
    public void A_write_carried_inside_a_common_table_expression_is_rejected()
    {
        // The parser calls this a SELECT, and the write sits in the query body. Trusting the
        // statement's type would let it through, so the body is what is inspected.
        var decision = ReadOnlyGuard.Inspect("WITH x AS (INSERT INTO t VALUES (1) RETURNING a) SELECT * FROM x");

        Assert.False(decision.IsAllowed);
        Assert.Contains("Insert", decision.Reason!);
    }

    [Fact]
    public void A_write_nested_two_common_table_expressions_deep_is_rejected()
    {
        var decision = ReadOnlyGuard.Inspect(
            "WITH x AS (WITH y AS (INSERT INTO t VALUES (1) RETURNING a) SELECT * FROM y) SELECT * FROM x");

        Assert.False(decision.IsAllowed);
        Assert.Contains("Insert", decision.Reason!);
    }

    [Theory]
    [InlineData("WITH x AS (SELECT 1 AS a) SELECT * FROM x")]
    [InlineData("WITH x AS (SELECT 1 AS a), y AS (SELECT 2 AS b) SELECT * FROM x, y")]
    [InlineData("WITH RECURSIVE t(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM t WHERE n < 5) SELECT * FROM t")]
    public void A_common_table_expression_that_only_reads_is_allowed(string sql)
    {
        // A recursive definition refers to itself, so the walk has to cope with meeting a node
        // twice rather than following it forever.
        Assert.True(ReadOnlyGuard.Inspect(sql).IsAllowed);
    }

    [Fact]
    public void Select_into_is_rejected_because_it_creates_a_table()
    {
        var decision = ReadOnlyGuard.Inspect("SELECT * INTO newtable FROM t");

        Assert.False(decision.IsAllowed);
        Assert.Contains("SELECT INTO", decision.Reason!);
    }

    [Fact]
    public void Select_into_is_found_when_it_is_wrapped_in_a_union()
    {
        // The INTO clause sits on an operand of the union rather than on the outer body, so the
        // walk has to reach into both sides.
        var decision = ReadOnlyGuard.Inspect("SELECT * INTO newtable FROM t UNION SELECT 1");

        Assert.False(decision.IsAllowed);
        Assert.Contains("SELECT INTO", decision.Reason!);
    }

    [Theory]
    [InlineData("SELECT * FROM t FOR UPDATE")]
    [InlineData("SELECT * FROM t FOR SHARE")]
    public void A_locking_query_is_rejected(string sql)
    {
        // The statement still reads, but it asks the engine to hold locks while it does, which a
        // read-only server has no business doing.
        var decision = ReadOnlyGuard.Inspect(sql);

        Assert.False(decision.IsAllowed);
        Assert.Contains("locking", decision.Reason!, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("SELECT LOAD_FILE('/etc/passwd')", "LOAD_FILE")]
    [InlineData("SELECT GET_LOCK('x', 1)", "GET_LOCK")]
    [InlineData("SELECT SLEEP(10)", "SLEEP")]
    [InlineData("SELECT BENCHMARK(1000000, MD5('a'))", "BENCHMARK")]
    [InlineData("SELECT main.load_file('/etc/passwd')", "load_file")]
    [InlineData("SELECT `load_file`('/etc/passwd')", "load_file")]
    public void A_call_that_reaches_outside_the_data_is_rejected(string sql, string function)
    {
        // These are written as ordinary calls inside a query the parser is right to call a
        // SELECT, so they are caught by what they call rather than by their statement kind.
        var decision = ReadOnlyGuard.Inspect(sql);

        Assert.False(decision.IsAllowed);
        Assert.Contains(function, decision.Reason!, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("SELECT CONCAT('x', LOAD_FILE('/etc/passwd'))")]
    [InlineData("SELECT IF(1, LOAD_FILE('/x'), '')")]
    [InlineData("SELECT upper(lower(md5(LOAD_FILE('/x'))))")]
    [InlineData("SELECT (SELECT LOAD_FILE('/x')) AS v")]
    public void A_denied_call_is_found_however_deeply_it_is_nested(string sql)
    {
        // A denied call is not always the outermost one, and the parser's own walk stops at a
        // call's arguments, so the arguments are walked here.
        var decision = ReadOnlyGuard.Inspect(sql);

        Assert.False(decision.IsAllowed);
        Assert.Contains("LOAD_FILE", decision.Reason!);
    }

    [Theory]
    [InlineData("SELECT LOAD_FILE('/etc/passwd'), 1 FROM t")]
    [InlineData("SELECT a FROM t WHERE LOAD_FILE('/x') IS NOT NULL")]
    [InlineData("SELECT CASE WHEN LOAD_FILE('/x') IS NULL THEN 1 ELSE 2 END")]
    [InlineData("SELECT * FROM t WHERE id IN (SELECT id FROM u WHERE GET_LOCK('l', 1))")]
    public void A_denied_call_is_found_wherever_it_sits_in_the_statement(string sql)
    {
        var decision = ReadOnlyGuard.Inspect(sql);

        Assert.False(decision.IsAllowed);
        Assert.Contains("not allowed", decision.Reason!);
    }

    [Theory]
    [InlineData("SELECT created_at FROM users")]
    [InlineData("SELECT * FROM users WHERE deleted_at IS NULL")]
    [InlineData("SELECT * FROM audit WHERE action = 'update'")]
    [InlineData("SELECT 'INSERT INTO t VALUES (1)' AS example")]
    [InlineData("SELECT 1 /* INSERT INTO t */")]
    [InlineData("SELECT upper(name), count(*) FROM users GROUP BY upper(name)")]
    public void Wording_that_merely_resembles_a_write_is_allowed(string sql)
    {
        // Reading the text for write keywords would turn these away, and they are ordinary
        // queries against ordinary schemas: created_at, updated_at and deleted_at are column
        // names, and a write keyword inside a string or a comment is not a write.
        Assert.True(ReadOnlyGuard.Inspect(sql).IsAllowed);
    }

    [Theory]
    [InlineData("PRAGMA query_only")]
    [InlineData("PRAGMA query_only = 0")]
    [InlineData("PRAGMA journal_mode = 'WAL'")]
    [InlineData("PRAGMA table_info('users')")]
    [InlineData("PRAGMA database_list")]
    public void Pragma_is_rejected_even_where_it_only_reads(string sql)
    {
        // The permit list names SELECT, EXPLAIN and SHOW only, so PRAGMA falls on the refusal
        // side — including the forms that only read. This costs nothing: the SQLite introspection
        // SQL uses the table-valued spelling (`SELECT * FROM pragma_table_info('users')`), which
        // is an ordinary SELECT and is permitted.
        //
        // Refusing it also guards the assignment spelling, which is how a client would reach the
        // engine's write-ish settings, such as turning `query_only` back off.
        Assert.False(ReadOnlyGuard.Inspect(sql).IsAllowed);
    }

    [Fact]
    public void A_pragma_whose_argument_is_not_a_value_cannot_be_read_at_all()
    {
        // The parser reads the bracketed argument as a value, so the form SQLite documentation
        // usually shows — a bare identifier, `PRAGMA table_info(users)` — is a syntax error. The
        // quoted spelling parses and is refused by policy instead. Either way the answer is no.
        var decision = ReadOnlyGuard.Inspect("PRAGMA table_info(users)");

        Assert.False(decision.IsAllowed);
        Assert.Contains("could not be parsed", decision.Reason!);
    }

    [Fact]
    public void A_value_query_is_rejected()
    {
        // `VALUES (1)` is a query body the permit list does not name, so it is refused rather than
        // falling through a walk that only knows how to permit a SELECT.
        var decision = ReadOnlyGuard.Inspect("VALUES (1)");

        Assert.False(decision.IsAllowed);
        Assert.Contains("ValuesExpression", decision.Reason!);
    }

    [Fact]
    public void A_square_bracketed_identifier_is_refused_as_an_accepted_gap()
    {
        // The generic dialect does not accept SQLite's [bracketed] identifiers, so SQL written
        // that way is turned away. Brackets are the SQL Server style and are rare in SQLite, which
        // is why the gap is accepted rather than worked around (ADR 0005).
        var decision = ReadOnlyGuard.Inspect("SELECT * FROM [my table]");

        Assert.False(decision.IsAllowed);
        Assert.Contains("could not be parsed", decision.Reason!);
    }
}
