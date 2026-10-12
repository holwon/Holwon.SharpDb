using SharpDb.Core.Execution;

namespace SharpDb.Core.Tests.Execution;

/// <summary>
/// The placeholder scan, observed through its answer: given a statement's text and the values the
/// caller sent, is any placeholder left without one, and which.
/// </summary>
/// <remarks>
/// The scanner exists because the engines disagree about an unbound placeholder — measured,
/// PostgreSQL reads the leftover <c>@</c> as its absolute-value operator and returns rows the
/// caller never asked for. The assertions here are about telling a placeholder from the same
/// characters used as data, which is the whole difficulty of the check.
/// </remarks>
public class ParameterPlaceholdersTests
{
    [Theory]
    [InlineData("SELECT id FROM readings WHERE id = @id")]
    [InlineData("SELECT * FROM t WHERE a = @a AND b = @b")]
    [InlineData("SELECT count(*) FROM t WHERE label = @name")]
    public void An_unbound_placeholder_is_found(string sql)
    {
        Assert.NotNull(ParameterPlaceholders.FindUnbound(sql, null));
    }

    [Fact]
    public void The_unbound_placeholder_is_named_without_its_marker()
    {
        Assert.Equal("id", ParameterPlaceholders.FindUnbound("SELECT id FROM readings WHERE id = @id", null));
    }

    [Fact]
    public void A_statement_with_no_placeholder_has_nothing_unbound()
    {
        Assert.Null(ParameterPlaceholders.FindUnbound("SELECT id FROM readings", null));
    }

    [Fact]
    public void A_supplied_value_satisfies_its_placeholder()
    {
        var supplied = new Dictionary<string, object?> { ["id"] = 1 };

        Assert.Null(ParameterPlaceholders.FindUnbound("SELECT id FROM readings WHERE id = @id", supplied));
    }

    [Fact]
    public void The_second_placeholder_is_found_when_the_first_is_supplied()
    {
        var supplied = new Dictionary<string, object?> { ["a"] = 1 };

        Assert.Equal("b", ParameterPlaceholders.FindUnbound("SELECT * FROM t WHERE a = @a AND b = @b", supplied));
    }

    [Fact]
    public void A_value_whose_name_differs_in_case_still_satisfies_it()
    {
        // Measured: MySQL matches a placeholder to a value of any case, so a check that did not
        // would refuse a statement the database would have run.
        var supplied = new Dictionary<string, object?> { ["ID"] = 1 };

        Assert.Null(ParameterPlaceholders.FindUnbound("SELECT id FROM readings WHERE id = @id", supplied));
    }

    [Fact]
    public void A_placeholder_inside_a_string_literal_is_data()
    {
        Assert.Null(ParameterPlaceholders.FindUnbound("SELECT id FROM readings WHERE note = '@id'", null));
    }

    [Fact]
    public void A_placeholder_inside_a_comment_is_prose()
    {
        Assert.Null(ParameterPlaceholders.FindUnbound("SELECT id FROM readings -- @commented", null));
        Assert.Null(ParameterPlaceholders.FindUnbound("SELECT id FROM readings /* @commented */", null));
    }

    [Fact]
    public void A_placeholder_after_a_string_that_contains_a_quote_is_still_found()
    {
        // The doubled quote is an escaped quote inside the literal, not the end of it, so the
        // scan must not mistake the remainder of the literal for statement text.
        Assert.Equal(
            "id",
            ParameterPlaceholders.FindUnbound("SELECT 'it''s' AS note FROM readings WHERE id = @id", null));
    }

    [Theory]
    [InlineData("SELECT `id` FROM `t` WHERE x = @x")]
    [InlineData("SELECT \"id\" FROM \"t\" WHERE x = @x")]
    public void A_placeholder_after_a_quoted_identifier_is_still_found(string sql)
    {
        Assert.Equal("x", ParameterPlaceholders.FindUnbound(sql, null));
    }

    [Theory]
    [InlineData("SELECT 1 @@ 2")]
    [InlineData("SELECT a @> b FROM t")]
    [InlineData("SELECT a @- b FROM t")]
    public void An_at_sign_that_is_an_operator_is_not_a_placeholder(string sql)
    {
        // PostgreSQL's operators are made of these characters, and reading one as a placeholder
        // would refuse a statement that has none.
        Assert.Null(ParameterPlaceholders.FindUnbound(sql, null));
    }

    [Fact]
    public void A_numeric_literal_after_an_at_sign_is_not_a_placeholder()
    {
        Assert.Null(ParameterPlaceholders.FindUnbound("SELECT id FROM t WHERE id = @ -1", null));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Text_with_no_statement_has_nothing_unbound(string? sql)
    {
        Assert.Null(ParameterPlaceholders.FindUnbound(sql, null));
    }

    [Fact]
    public void A_placeholder_written_with_a_dollar_or_an_underscore_is_found()
    {
        Assert.Equal("_private", ParameterPlaceholders.FindUnbound("SELECT * FROM t WHERE a = @_private", null));
        Assert.Equal("$value", ParameterPlaceholders.FindUnbound("SELECT * FROM t WHERE a = @$value", null));
    }

    [Fact]
    public void An_empty_parameter_object_leaves_every_placeholder_unbound()
    {
        Assert.Equal(
            "id",
            ParameterPlaceholders.FindUnbound("SELECT id FROM readings WHERE id = @id", new Dictionary<string, object?>()));
    }

    [Fact]
    public void Respelling_the_marker_rewrites_only_the_placeholder()
    {
        Assert.Equal(
            "SELECT id FROM readings WHERE id = :id",
            ParameterPlaceholders.WithMarker("SELECT id FROM readings WHERE id = @id", ':'));
    }

    [Fact]
    public void Respelling_the_marker_rewrites_every_placeholder()
    {
        Assert.Equal(
            "SELECT * FROM t WHERE a = :a AND b = :b",
            ParameterPlaceholders.WithMarker("SELECT * FROM t WHERE a = @a AND b = @b", ':'));
    }

    [Fact]
    public void Respelling_leaves_an_at_sign_inside_a_string_literal_alone()
    {
        Assert.Equal(
            "SELECT id FROM readings WHERE note = '@id' AND id = :id",
            ParameterPlaceholders.WithMarker("SELECT id FROM readings WHERE note = '@id' AND id = @id", ':'));
    }

    [Theory]
    [InlineData("SELECT `@id` FROM t WHERE x = @x", "SELECT `@id` FROM t WHERE x = :x")]
    [InlineData("SELECT \"@id\" FROM t WHERE x = @x", "SELECT \"@id\" FROM t WHERE x = :x")]
    public void Respelling_leaves_an_at_sign_inside_a_quoted_identifier_alone(string sql, string expected)
    {
        Assert.Equal(expected, ParameterPlaceholders.WithMarker(sql, ':'));
    }

    [Fact]
    public void Respelling_leaves_an_at_sign_inside_a_comment_alone()
    {
        Assert.Equal(
            "SELECT id FROM readings -- @commented\nWHERE id = :id",
            ParameterPlaceholders.WithMarker("SELECT id FROM readings -- @commented\nWHERE id = @id", ':'));

        Assert.Equal(
            "SELECT id FROM readings /* @commented */ WHERE id = :id",
            ParameterPlaceholders.WithMarker("SELECT id FROM readings /* @commented */ WHERE id = @id", ':'));
    }

    [Theory]
    [InlineData("SELECT 1 @@ 2")]
    [InlineData("SELECT a @> b FROM t")]
    [InlineData("SELECT a @- b FROM t")]
    [InlineData("SELECT id FROM t WHERE id = @ -1")]
    public void Respelling_leaves_an_at_sign_that_is_an_operator_alone(string sql)
    {
        Assert.Equal(sql, ParameterPlaceholders.WithMarker(sql, ':'));
    }

    [Fact]
    public void Respelling_rewrites_an_at_sign_glued_to_a_name_because_the_scan_reads_it_as_a_placeholder()
    {
        // `t@dblink` is Oracle's database-link syntax, but `@dblink` names a value to the scan, so
        // the marker is rewritten here. That is harmless rather than wrong: this rewrite only runs
        // once the statement is about to execute, and a statement holding `@dblink` never gets that
        // far — the Tool's unbound-placeholder check runs first and refuses it (see the test below).
        // Refusing is the right answer, because a database link reaches another database and this
        // server is bound to one (use-cases.md).
        Assert.Equal("SELECT * FROM t:dblink", ParameterPlaceholders.WithMarker("SELECT * FROM t@dblink", ':'));
    }

    [Fact]
    public void A_dblink_is_read_as_a_placeholder_so_the_tool_refuses_it_before_any_rewrite()
    {
        // The load-bearing half: the same `@dblink` is what FindUnbound reports, so a query using a
        // database link is refused by name — before translation ever runs, and on every engine,
        // because this check is Core's and is dialect-independent.
        Assert.Equal(
            "dblink",
            ParameterPlaceholders.FindUnbound("SELECT * FROM t@dblink", new Dictionary<string, object?>()));
    }

    [Fact]
    public void Respelling_a_statement_with_no_placeholder_returns_it_unchanged()
    {
        Assert.Equal("SELECT id FROM readings", ParameterPlaceholders.WithMarker("SELECT id FROM readings", ':'));
    }

    [Fact]
    public void Respelling_rejects_a_missing_statement()
    {
        Assert.Throws<ArgumentNullException>(() => ParameterPlaceholders.WithMarker(null!, ':'));
    }
}
