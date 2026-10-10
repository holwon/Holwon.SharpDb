using System.Globalization;

namespace SharpDb.Core.Execution;

/// <summary>
/// Turns one value read out of a result set into the value the payload carries.
/// </summary>
/// <remarks>
/// A value keeps its kind wherever JSON has a kind of its own — a whole number stays a number, a
/// fraction stays a number, a boolean stays a boolean, a null stays a null — because a caller that
/// has to parse text before it can add, compare or branch has lost the point of asking the
/// database. Only what JSON cannot express is written as text: a blob as base64, so a reader
/// recognises it as bytes rather than as letters, and a date as ISO 8601, so it is unambiguous.
/// <para>
/// A type this mapping does not name is written as its text rather than passed through. A Driver
/// is free to hand back something unexpected, and a payload that cannot be written would surface
/// as an exception escaping the Tool, which is exactly what ADR 0010 rules out.
/// </para>
/// </remarks>
public static class QueryValues
{
    /// <summary>
    /// Maps a value a reader reported to the value a JSON payload carries.
    /// </summary>
    /// <param name="value">The value the reader reported, which may be the database's null.</param>
    /// <returns>The value to write into the payload.</returns>
    public static object? ForJson(object? value)
    {
        if (value is null or DBNull)
        {
            return null;
        }

        return value switch
        {
            byte[] bytes => Convert.ToBase64String(bytes),
            DateTime dateTime => dateTime.ToString("O", CultureInfo.InvariantCulture),
            DateTimeOffset offset => offset.ToString("O", CultureInfo.InvariantCulture),
            bool or string
                or byte or sbyte or short or ushort or int or uint or long or ulong
                or float or double or decimal => value,
            _ => Convert.ToString(value, CultureInfo.InvariantCulture),
        };
    }
}
