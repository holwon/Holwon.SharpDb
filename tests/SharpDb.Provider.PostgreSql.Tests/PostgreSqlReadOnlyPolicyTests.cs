using SharpDb.Abstractions;
using SharpDb.Core.Guard;

namespace SharpDb.Provider.PostgreSql.Tests;

/// <summary>
/// The read-only policy this Provider declares, exercised through the real Guard.
/// </summary>
/// <remarks>
/// The Guard's own tests build a policy directly, which is what pins the Guard's use of one. This
/// asserts the other half of the wiring: that the policy the <em>Provider</em> hands over is the
/// policy the Guard enforces. It is a different question from either half, and the one that
/// matters most, because a Provider whose policy were ignored would leave the Guard refusing what
/// it should permit and permitting what it should refuse, with both of those tests still green.
/// </remarks>
public class PostgreSqlReadOnlyPolicyTests
{
    /// <summary>
    /// The Guard built from this Provider's own declaration, as the host builds it.
    /// </summary>
    private static readonly ReadOnlyGuard Guard = new(new PostgreSqlDatabaseProvider().Policy);

    /// <summary>
    /// The Provider declares the type the configuration selects it by.
    /// </summary>
    [Fact]
    public void The_provider_declares_the_postgresql_type()
    {
        Assert.Equal("postgresql", new PostgreSqlDatabaseProvider().Type);
    }

    /// <summary>
    /// The Provider's own policy is PostgreSQL's dialect.
    /// </summary>
    /// <remarks>
    /// Asserted by its effect rather than by reading the value: what the dialect is <em>for</em> is
    /// understanding the engine's SQL, so the assertion is that a statement only this dialect can
    /// read is read.
    /// </remarks>
    [Fact]
    public void The_providers_policy_reads_a_filter_clause()
    {
        // Measured: the generic dialect cannot parse this, and the PostgreSQL dialect can. It is
        // ordinary read-only SQL, so refusing it would turn a legitimate aggregate away.
        Assert.True(Guard.Inspect("SELECT count(*) FILTER (WHERE ratio > 1) FROM readings").IsAllowed);
    }

    /// <summary>
    /// The functions that read the server's files are refused, in whatever position they appear.
    /// </summary>
    /// <remarks>
    /// This is the reason the denied list exists at all. Measured on PostgreSQL 16.15: a session
    /// set to read-only still lets <c>pg_read_file</c> read the server's own configuration file
    /// and <c>pg_ls_dir</c> list a directory, so the engine's own layer does not stop these and
    /// the Guard is the layer that has to. Each is asserted through the Provider's own policy, so
    /// a name dropped from its list would fail here rather than silently reopening the hole.
    /// </remarks>
    [Theory]
    [InlineData("SELECT pg_read_file('postgresql.conf')")]
    [InlineData("SELECT pg_read_binary_file('postgresql.conf')")]
    [InlineData("SELECT pg_ls_dir('.')")]
    [InlineData("SELECT pg_stat_file('postgresql.conf')")]
    [InlineData("SELECT lo_import('/etc/passwd')")]
    [InlineData("SELECT length(pg_read_file('postgresql.conf'))")]
    [InlineData("SELECT 1 FROM readings WHERE pg_read_file('postgresql.conf') IS NOT NULL")]
    [InlineData("SELECT * FROM users WHERE id IN (SELECT lo_import('/etc/passwd'))")]
    public void The_providers_policy_refuses_a_file_reading_function(string sql)
    {
        var decision = Guard.Inspect(sql);

        Assert.False(decision.IsAllowed);
        Assert.Contains("is not allowed", decision.Reason!);
    }

    /// <summary>
    /// A sequence function is not on the list, because the engine already refuses it.
    /// </summary>
    /// <remarks>
    /// The ticket keeps <c>nextval</c> and <c>setval</c> out of the denied list deliberately:
    /// they write to a sequence, so the engine's read-only mode refuses them before the Guard's
    /// list would matter. Asserting that they stay out pins the decision, so a later change that
    /// added them would be a deliberate one rather than an accident — and if the engine ever
    /// stopped refusing them, this is where the assumption would show.
    /// </remarks>
    [Fact]
    public void A_sequence_function_is_left_to_the_engine()
    {
        Assert.True(Guard.Inspect("SELECT nextval('some_sequence')").IsAllowed);
        Assert.True(Guard.Inspect("SELECT setval('some_sequence', 1)").IsAllowed);
    }

    /// <summary>
    /// A write is refused under this policy, as it is under every other.
    /// </summary>
    /// <remarks>
    /// The policy changes which read-only statements are understood and nothing else; this is the
    /// assertion that says so for the declarations this Provider actually makes.
    /// </remarks>
    [Theory]
    [InlineData("INSERT INTO orgs (name) VALUES ('refused')")]
    [InlineData("UPDATE orgs SET name = 'refused'")]
    [InlineData("DELETE FROM orgs")]
    [InlineData("TRUNCATE TABLE orgs")]
    [InlineData("DROP TABLE orgs")]
    [InlineData("CREATE TABLE t (a INT)")]
    [InlineData("WITH x AS (INSERT INTO orgs (name) VALUES ('x') RETURNING id) SELECT * FROM x")]
    [InlineData("SELECT * INTO newtable FROM orgs")]
    [InlineData("EXPLAIN ANALYZE INSERT INTO orgs (name) VALUES ('x')")]
    public void A_write_is_refused_under_the_providers_own_policy(string sql)
    {
        Assert.False(Guard.Inspect(sql).IsAllowed);
    }

    /// <summary>
    /// A back-quoted identifier is refused, because this engine does not use them.
    /// </summary>
    /// <remarks>
    /// The mirror image of the assertion that keeps MySQL's SQL working: PostgreSQL's dialect does
    /// not accept back quotes, so a statement written in MySQL's style is turned away here. That
    /// is the accepted cost of each engine reading its own dialect, and it is deliberately a
    /// refusal rather than a wrong answer.
    /// </remarks>
    [Fact]
    public void A_back_quoted_identifier_is_refused_under_this_policy()
    {
        var decision = Guard.Inspect("SELECT `id` FROM `users`");

        Assert.False(decision.IsAllowed);
    }

    /// <summary>
    /// A policy may only add denied names, never remove one.
    /// </summary>
    /// <remarks>
    /// The one guarantee the design rests on: whatever a Provider declares, a statement form the
    /// permit list does not name is still refused. Asserted over the Provider's real policy so the
    /// statement is about shipping behaviour rather than about a constructed one.
    /// </remarks>
    [Theory]
    [InlineData("GRANT SELECT ON t TO u")]
    [InlineData("SET @x = 1")]
    [InlineData("SELECT 1; DROP TABLE orgs")]
    [InlineData("/**/ INSERT INTO orgs (name) VALUES ('x')")]
    public void An_unrecognised_statement_is_still_refused(string sql)
    {
        Assert.False(Guard.Inspect(sql).IsAllowed);
    }
}
