using SharpDb.Abstractions;
using SharpDb.Core.Guard;
using SharpDb.Provider.Oracle;

namespace SharpDb.Provider.Oracle.Tests;

/// <summary>
/// The read-only policy this Provider declares, exercised through the real Guard.
/// </summary>
/// <remarks>
/// The Guard's own tests build a policy directly, which is what pins the Guard's use of one. This
/// asserts the other half of the wiring: that the policy the <em>Provider</em> hands over is the
/// policy the Guard enforces. A Provider whose policy were ignored would leave the Guard refusing
/// what it should permit and permitting what it should refuse, with the Guard's own tests still
/// green.
/// </remarks>
public class OracleReadOnlyPolicyTests
{
    /// <summary>
    /// The Guard built from this Provider's own declaration, as the host builds it.
    /// </summary>
    private static readonly ReadOnlyGuard Guard = new(new OracleDatabaseProvider().Policy);

    /// <summary>
    /// The Provider declares the type the configuration selects it by.
    /// </summary>
    [Fact]
    public void The_provider_declares_the_oracle_type()
    {
        Assert.Equal("oracle", new OracleDatabaseProvider().Type);
    }

    /// <summary>
    /// The Provider's own policy is Oracle's dialect.
    /// </summary>
    /// <remarks>
    /// Asserted by the value rather than by an effect, because Oracle's dialect maps to the same
    /// parser dialect the generic engines use — so the value is not observable through what the
    /// Guard permits. What it <em>is</em> observable through is the placeholder rewrite, which
    /// lives in Core; here the assertion pins that the Provider names the value that selects it.
    /// </remarks>
    [Fact]
    public void The_providers_policy_names_the_oracle_dialect()
    {
        Assert.Equal(SqlDialect.Oracle, new OracleDatabaseProvider().Policy.Dialect);
    }

    /// <summary>
    /// The Provider's policy denies no functions, and that is deliberate.
    /// </summary>
    /// <remarks>
    /// The list exists for functions that work inside the engine's own read-only mode but reach
    /// outside the query — measured on PostgreSQL, <c>pg_read_file</c> does exactly that. No such
    /// function was measured on Oracle, so the list is empty, and asserting it stays empty pins the
    /// decision: a later addition would be a deliberate one rather than an accident. The list may
    /// only grow (ADR 0013), so this is also the record of where it started.
    /// </remarks>
    [Fact]
    public void The_providers_policy_denies_no_functions()
    {
        Assert.Empty(new OracleDatabaseProvider().Policy.DeniedFunctions);
    }

    /// <summary>
    /// Ordinary Oracle read-only SQL is understood under the Provider's own policy.
    /// </summary>
    /// <remarks>
    /// Oracle declares its own dialect value, and that value must not narrow what the permit list
    /// allows. These are the ordinary statements an Oracle user writes.
    /// </remarks>
    [Theory]
    [InlineData("SELECT 1 FROM dual")]
    [InlineData("SELECT id, email FROM users")]
    [InlineData("SELECT * FROM readings WHERE id = @id")]
    [InlineData("SELECT * FROM readings WHERE ROWNUM <= 10")]
    [InlineData("SELECT count(*) AS n FROM readings")]
    public void The_providers_policy_permits_ordinary_read_only_sql(string sql)
    {
        Assert.True(Guard.Inspect(sql).IsAllowed);
    }

    /// <summary>
    /// A write is refused under this policy, as it is under every other.
    /// </summary>
    [Theory]
    [InlineData("INSERT INTO orgs (name) VALUES ('refused')")]
    [InlineData("UPDATE orgs SET name = 'refused'")]
    [InlineData("DELETE FROM orgs")]
    [InlineData("TRUNCATE TABLE orgs")]
    [InlineData("DROP TABLE orgs")]
    [InlineData("CREATE TABLE t (a NUMBER)")]
    [InlineData("WITH x AS (INSERT INTO orgs (name) VALUES ('x') RETURNING id) SELECT * FROM x")]
    [InlineData("SELECT * INTO newtable FROM orgs")]
    [InlineData("EXPLAIN ANALYZE INSERT INTO orgs (name) VALUES ('x')")]
    public void A_write_is_refused_under_the_providers_own_policy(string sql)
    {
        Assert.False(Guard.Inspect(sql).IsAllowed);
    }
}
