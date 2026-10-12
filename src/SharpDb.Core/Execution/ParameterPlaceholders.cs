namespace SharpDb.Core.Execution;

/// <summary>
/// Finds the <c>@name</c> placeholders in a statement that the caller supplied no value for.
/// </summary>
/// <remarks>
/// A forgotten value is the likeliest mistake a caller makes, and it must not be answered as
/// silence: measured, the three engines do three different things with an unbound placeholder —
/// SQLite raises <c>InvalidOperationException</c>, MySQL raises its own exception, and PostgreSQL
/// does neither. On PostgreSQL <c>@</c> is also the absolute-value operator, so <c>WHERE id = @id</c>
/// with nothing bound is parsed as <c>WHERE id = abs(id)</c> and the query succeeds, returning rows
/// the caller never asked for. A wrong answer that looks like a right one is the one outcome this
/// server must never produce, so the check happens here, in Core, before any engine sees the
/// statement — which also makes all three engines behave identically.
/// <para>
/// The scan is lexical rather than a search of the raw text, because the text is not the statement:
/// <c>SELECT '@id'</c> holds an <c>@id</c> that is data, and a comment holds one that is prose.
/// Neither is a placeholder, and reporting either would refuse a statement for saying the word.
/// </para>
/// </remarks>
public static class ParameterPlaceholders
{
    /// <summary>
    /// Finds the first placeholder the caller gave no value for.
    /// </summary>
    /// <param name="sql">The statement's text, as the caller sent it.</param>
    /// <param name="supplied">The values the caller sent, keyed by name without its <c>@</c>.</param>
    /// <returns>The name of the first unbound placeholder, or <see langword="null"/> when there is none.</returns>
    /// <remarks>
    /// The first one is reported rather than all of them: a caller told one name can fix it and
    /// resend, and a list would make the message longer without making it more useful. The
    /// comparison ignores case, because the Drivers' own binding does — measured, MySQL matches a
    /// placeholder to a value of any case — so a check that did not would refuse a statement that
    /// would have run.
    /// </remarks>
    public static string? FindUnbound(string? sql, IReadOnlyDictionary<string, object?>? supplied)
    {
        if (string.IsNullOrEmpty(sql))
        {
            return null;
        }

        var names = supplied is null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(supplied.Keys, StringComparer.OrdinalIgnoreCase);

        foreach (var name in Names(sql))
        {
            if (!names.Contains(name))
            {
                return name;
            }
        }

        return null;
    }

    /// <summary>
    /// Reads the placeholder names out of a statement's text.
    /// </summary>
    /// <param name="sql">The statement's text.</param>
    /// <returns>The names, in the order they appear, without their <c>@</c>.</returns>
    private static IEnumerable<string> Names(string sql)
    {
        var index = 0;
        while (index < sql.Length)
        {
            var current = sql[index];

            switch (current)
            {
                // A quoted string holds data. Both the doubled quote and the backslash escape are
                // honoured, because the engines accept both and either can hide a quote.
                case '\'':
                case '"':
                case '`':
                    index = SkipQuoted(sql, index, current);
                    continue;

                // A comment holds prose: "--" to end of line, or "/*" to the closing marker.
                case '-' when index + 1 < sql.Length && sql[index + 1] == '-':
                    index = SkipLine(sql, index + 2);
                    continue;

                case '/' when index + 1 < sql.Length && sql[index + 1] == '*':
                    index = SkipBlock(sql, index + 2);
                    continue;

                case '@':
                    if (StartOfName(sql, index + 1) is { } name)
                    {
                        yield return name;
                        index += name.Length + 1;
                        continue;
                    }

                    index++;
                    continue;

                default:
                    index++;
                    continue;
            }
        }
    }

    /// <summary>
    /// Reads the placeholder name starting at a position, when one does.
    /// </summary>
    /// <param name="sql">The statement's text.</param>
    /// <param name="start">The position just after the <c>@</c>.</param>
    /// <returns>The name, or <see langword="null"/> when the <c>@</c> is not the start of one.</returns>
    /// <remarks>
    /// A name starts with a letter, an underscore or a dollar sign — never a digit, which is what
    /// keeps arithmetic such as <c>@ -3</c> out, and never another <c>@</c>, which is what keeps
    /// PostgreSQL's <c>@@</c> text-search operator and the <c>@&gt;</c> family of operators out.
    /// What remains is the documented <c>@name</c> spelling, and a lone <c>@</c> used as an
    /// operator directly against an identifier is read as a placeholder — accepted deliberately,
    /// because a caller who means the operator can write it with a space, while a caller who
    /// forgot a value has no other way to be told.
    /// </remarks>
    private static string? StartOfName(string sql, int start)
    {
        if (start >= sql.Length || !IsNameStart(sql[start]))
        {
            return null;
        }

        var end = start + 1;
        while (end < sql.Length && IsNameCharacter(sql[end]))
        {
            end++;
        }

        return sql[start..end];
    }

    /// <summary>
    /// Whether a character may begin a placeholder's name.
    /// </summary>
    /// <param name="value">The character.</param>
    /// <returns><see langword="true"/> when it may.</returns>
    private static bool IsNameStart(char value) =>
        char.IsLetter(value) || value is '_' or '$';

    /// <summary>
    /// Whether a character may continue a placeholder's name.
    /// </summary>
    /// <param name="value">The character.</param>
    /// <returns><see langword="true"/> when it may.</returns>
    private static bool IsNameCharacter(char value) =>
        IsNameStart(value) || char.IsDigit(value);

    /// <summary>
    /// Advances past a quoted run.
    /// </summary>
    /// <param name="sql">The statement's text.</param>
    /// <param name="start">The position of the opening quote.</param>
    /// <param name="quote">The quote character that opened it.</param>
    /// <returns>The position just after the closing quote, or the end of the text.</returns>
    /// <remarks>
    /// An unterminated run consumes the rest of the text, which is the safe direction: the Guard
    /// refuses a statement it cannot parse, so by the time this runs the text is known to be a
    /// statement — and treating the tail as quoted can only hide a placeholder, never invent one.
    /// </remarks>
    private static int SkipQuoted(string sql, int start, char quote)
    {
        var index = start + 1;
        while (index < sql.Length)
        {
            if (sql[index] == '\\' && index + 1 < sql.Length)
            {
                index += 2;
                continue;
            }

            if (sql[index] != quote)
            {
                index++;
                continue;
            }

            // A doubled quote is an escaped quote inside the run, not the end of it.
            if (index + 1 < sql.Length && sql[index + 1] == quote)
            {
                index += 2;
                continue;
            }

            return index + 1;
        }

        return index;
    }

    /// <summary>
    /// Advances past a line comment.
    /// </summary>
    /// <param name="sql">The statement's text.</param>
    /// <param name="start">The position just after the opening marker.</param>
    /// <returns>The position of the next line's first character, or the end of the text.</returns>
    private static int SkipLine(string sql, int start)
    {
        var newline = sql.IndexOf('\n', start);

        return newline < 0 ? sql.Length : newline + 1;
    }

    /// <summary>
    /// Advances past a block comment.
    /// </summary>
    /// <param name="sql">The statement's text.</param>
    /// <param name="start">The position just after the opening marker.</param>
    /// <returns>The position just after the closing marker, or the end of the text.</returns>
    /// <remarks>
    /// The engines do not nest block comments, so the first closing marker ends the run.
    /// </remarks>
    private static int SkipBlock(string sql, int start)
    {
        var end = sql.IndexOf("*/", start, StringComparison.Ordinal);

        return end < 0 ? sql.Length : end + 2;
    }
}
