using System.Text.Json;

namespace SharpDb.Core.Execution;

/// <summary>
/// Turns the parameter object a caller sent into the values a statement is bound with.
/// </summary>
/// <remarks>
/// A key is used as the caller wrote it, without a leading <c>@</c>, because that is the spelling
/// the Tool documents: the placeholder carries the marker and the key does not, so the two are
/// never confusable.
/// <para>
/// A value keeps its JSON kind, so a whole number binds as a whole number rather than as text. A
/// number too large for a whole number is bound as a decimal when it fits one, so a price or a
/// quantity is not rounded on the way to the database.
/// </para>
/// </remarks>
public static class QueryParameters
{
    /// <summary>
    /// Binds a caller's parameter object to values.
    /// </summary>
    /// <param name="parameters">The parameter object, which may be absent or empty.</param>
    /// <returns>The values to bind, or <see langword="null"/> when there is nothing to bind.</returns>
    /// <remarks>
    /// An absent object and an empty one are the same thing here — a statement with no parameters —
    /// so both become <see langword="null"/> rather than an empty set of bindings.
    /// </remarks>
    public static IReadOnlyDictionary<string, object?>? From(
        IReadOnlyDictionary<string, JsonElement>? parameters)
    {
        if (parameters is null || parameters.Count == 0)
        {
            return null;
        }

        var values = new Dictionary<string, object?>(parameters.Count, StringComparer.Ordinal);
        foreach (var (name, element) in parameters)
        {
            values[name] = ValueOf(element);
        }

        return values;
    }

    /// <summary>
    /// Reads one JSON value as the value to bind.
    /// </summary>
    /// <param name="element">The value the caller sent.</param>
    /// <returns>The value to bind.</returns>
    /// <remarks>
    /// A structured value — an object or an array — is bound as the JSON text it was sent as. No
    /// database parameter holds a structure, and the text is what a caller who means to store one
    /// in a text or JSON column is sending, so it is passed along rather than refused.
    /// </remarks>
    private static object? ValueOf(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.TryGetInt64(out var whole) ? whole
            : element.TryGetDecimal(out var exact) ? exact
            : element.GetDouble(),
        _ => element.GetRawText(),
    };
}
