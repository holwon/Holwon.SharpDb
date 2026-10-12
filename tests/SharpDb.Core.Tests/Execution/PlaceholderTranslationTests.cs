using SharpDb.Abstractions;
using SharpDb.Core.Execution;

namespace SharpDb.Core.Tests.Execution;

/// <summary>
/// The dialect-driven placeholder rewrite, observed through its answer: given a statement and the
/// dialect an engine declares, what the Driver is handed.
/// </summary>
/// <remarks>
/// The rewrite exists for one engine and must be invisible to every other, so the assertions come
/// in pairs — Oracle's spelling changes, the other engines' does not — and the one engine-specific
/// fact is pinned rather than left to the Provider's tests: a statement written with <c>@name</c>
/// reaches an Oracle Driver as <c>:name</c>.
/// </remarks>
public class PlaceholderTranslationTests
{
    [Fact]
    public void A_placeholder_reaches_oracle_with_a_colon_marker()
    {
        Assert.Equal(
            "SELECT id FROM readings WHERE id = :id",
            PlaceholderTranslation.ForDialect("SELECT id FROM readings WHERE id = @id", SqlDialect.Oracle));
    }

    [Theory]
    [InlineData(SqlDialect.Generic)]
    [InlineData(SqlDialect.PostgreSql)]
    public void A_placeholder_is_untouched_for_an_engine_that_spells_it_with_an_at_sign(SqlDialect dialect)
    {
        // The rewrite is the one engine-specific step on the execution path, so an engine that does
        // not declare Oracle's dialect must see its statement byte for byte.
        const string sql = "SELECT id FROM readings WHERE id = @id";

        Assert.Equal(sql, PlaceholderTranslation.ForDialect(sql, dialect));
    }

    [Fact]
    public void A_statement_with_no_placeholder_is_untouched_even_for_oracle()
    {
        const string sql = "SELECT id FROM readings";

        Assert.Equal(sql, PlaceholderTranslation.ForDialect(sql, SqlDialect.Oracle));
    }

    [Fact]
    public void An_at_sign_that_is_data_is_untouched_even_for_oracle()
    {
        // The rewrite reuses the placeholder scan, so the characters in a literal are not a
        // placeholder and are left as written — the caller's data must survive the trip.
        Assert.Equal(
            "SELECT '@id' AS note FROM dual WHERE id = :id",
            PlaceholderTranslation.ForDialect("SELECT '@id' AS note FROM dual WHERE id = @id", SqlDialect.Oracle));
    }

    [Fact]
    public void Translation_rejects_a_missing_statement()
    {
        Assert.Throws<ArgumentNullException>(() => PlaceholderTranslation.ForDialect(null!, SqlDialect.Oracle));
    }
}
