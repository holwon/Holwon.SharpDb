using System.Text.RegularExpressions;

namespace SharpDb.Core.Execution;

/// <summary>
/// The wording of a query failure — the only part of a failure the client can act on.
/// </summary>
/// <remarks>
/// A database's own message is passed through, because it names the table, the column or the
/// position the caller has to correct. The one thing taken out of it is a file path: a path tells
/// the caller nothing about how to fix a query, and it would carry this machine's directory layout
/// into a log, an issue or a screenshot (ADR 0010).
/// </remarks>
public static partial class QueryErrors
{
    /// <summary>
    /// What every message that came from the database is introduced by.
    /// </summary>
    private const string DatabasePrefix = "Database error: ";

    /// <summary>
    /// What a hidden path is replaced by.
    /// </summary>
    private const string PathPlaceholder = "<path>";

    /// <summary>
    /// Words a timeout, naming how long the server waited and what to try instead.
    /// </summary>
    /// <param name="seconds">The configured timeout, in seconds.</param>
    /// <returns>The message the client reads.</returns>
    /// <remarks>
    /// The advice is the point of the message: a caller told only that it timed out retries the
    /// same query, while a caller told to narrow it or to raise the setting can change something.
    /// </remarks>
    public static string TimedOut(int seconds) =>
        $"Query timed out after {seconds} second{(seconds == 1 ? string.Empty : "s")}. "
        + "Narrow the query, or raise SHARPDB__TIMEOUTSECONDS.";

    /// <summary>
    /// Shapes a message that came from the database into the one the client reads.
    /// </summary>
    /// <param name="message">The message the Driver reported.</param>
    /// <returns>The message, prefixed and with any file path hidden.</returns>
    public static string DatabaseFailure(string message) => DatabasePrefix + HidePaths(message);

    /// <summary>
    /// Replaces every file path in a message with a placeholder.
    /// </summary>
    /// <param name="message">The message to redact.</param>
    /// <returns>The message with its paths hidden.</returns>
    /// <remarks>
    /// A path is recognised by how it starts — a drive letter, a leading separator, or an explicit
    /// relative prefix — and then by its separator-joined segments. Requiring a segment boundary is
    /// what keeps a division such as <c>1/2</c>, which appears in ordinary SQL text, from being
    /// mistaken for a path.
    /// <para>
    /// Two limits are accepted. A segment holds no whitespace, so a path containing a space is
    /// hidden only up to that space. And a bare relative path with no explicit prefix, such as
    /// <c>data/sharpdb.sqlite</c>, is left alone, because telling it apart from ordinary text is
    /// not possible without redacting text a caller needs.
    /// </para>
    /// </remarks>
    private static string HidePaths(string message) => PathPattern().Replace(message, PathPlaceholder);

    /// <summary>
    /// A file path, written as the parts it is made of.
    /// </summary>
    /// <returns>The compiled pattern.</returns>
    /// <remarks>
    /// Two alternatives: one for a path that announces itself with a drive letter or a relative
    /// prefix, and one for a Unix path, which must hold at least one directory so that a lone
    /// separator inside an expression is not mistaken for a root.
    /// </remarks>
    [GeneratedRegex(
        """(?:[A-Za-z]:[\\/]|\.{1,2}[\\/])(?:[^\s'"<>|:*?\\/]+[\\/])*[^\s'"<>|:*?\\/]+|/(?:[^\s'"<>|:*?\\/]+/)+[^\s'"<>|:*?\\/]+""")]
    private static partial Regex PathPattern();
}
