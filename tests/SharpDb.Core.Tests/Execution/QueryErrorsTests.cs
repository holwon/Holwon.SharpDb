using SharpDb.Core.Execution;

namespace SharpDb.Core.Tests.Execution;

/// <summary>
/// The wording of a query failure, which is the only part of a failure the client can act on.
/// </summary>
/// <remarks>
/// A database's own message is passed through because it names the table, the column or the
/// position the caller has to fix; the one thing removed from it is a file path, which would leak
/// the machine's directory layout into a log or a screenshot without helping anyone correct a
/// query (ADR 0010).
/// </remarks>
public class QueryErrorsTests
{
    [Fact]
    public void A_timeout_names_the_seconds_it_waited()
    {
        var message = QueryErrors.TimedOut(30);

        Assert.Contains("30", message);
        Assert.Contains("timed out", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_timeout_says_what_to_do_next()
    {
        var message = QueryErrors.TimedOut(30);

        // Advice, not just a diagnosis: the caller can narrow the query or raise the setting.
        Assert.Contains("SHARPDB__TIMEOUTSECONDS", message);
    }

    [Fact]
    public void A_one_second_timeout_reads_as_one_second()
    {
        Assert.Contains("1 second.", QueryErrors.TimedOut(1));
    }

    [Fact]
    public void A_database_message_is_passed_through_behind_a_prefix()
    {
        var message = QueryErrors.DatabaseFailure("SQLite Error 1: 'no such table: no_such_table'.");

        Assert.Equal("Database error: SQLite Error 1: 'no such table: no_such_table'.", message);
    }

    [Theory]
    [InlineData(@"SQLite Error 14: 'unable to open database file: C:\data\sharpdb.sqlite'.")]
    [InlineData("SQLite Error 14: 'unable to open database file: /var/lib/sharpdb.sqlite'.")]
    [InlineData("SQLite Error 14: 'unable to open database file: ./data/sharpdb.sqlite'.")]
    public void A_file_path_in_a_database_message_is_hidden(string message)
    {
        var shaped = QueryErrors.DatabaseFailure(message);

        Assert.Contains("<path>", shaped);
        Assert.DoesNotContain("sharpdb.sqlite", shaped);
        Assert.DoesNotContain(@"C:\data", shaped);
        Assert.DoesNotContain("/var/lib", shaped);
    }

    [Fact]
    public void A_message_without_a_path_is_left_alone()
    {
        // The names a caller needs in order to fix a query must survive: table, column and position.
        const string Message =
            "SQLite Error 1: 'no such column: user_id' in \"SELECT user_id FROM users\" at line 1 column 8.";

        Assert.Equal("Database error: " + Message, QueryErrors.DatabaseFailure(Message));
    }

    [Fact]
    public void Division_is_not_mistaken_for_a_path()
    {
        // A lone slash is ordinary SQL, not a directory separator, and must survive the redaction.
        Assert.Equal("Database error: division by 1/2", QueryErrors.DatabaseFailure("division by 1/2"));
    }
}
