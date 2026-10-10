using System.Text.Json;
using ModelContextProtocol.Protocol;

namespace SharpDb.Provider.Contract;

/// <summary>
/// Reads what a Tool call answered, so an assertion can be about the payload a client receives
/// rather than about the objects behind it.
/// </summary>
/// <remarks>
/// Every shared assertion that goes through a Tool reads its result the same way: the payload is
/// the single text block the call returned, and a failure is the <c>error</c> member of that
/// payload's envelope (ADR 0010 — failures are results, not exceptions).
/// </remarks>
public static class ToolPayload
{
    /// <summary>
    /// Reads the text a call answered with.
    /// </summary>
    /// <param name="result">The call's result.</param>
    /// <returns>The payload text.</returns>
    /// <exception cref="InvalidOperationException">
    /// The result does not carry exactly one text block, which would mean the call answered in a
    /// shape these assertions do not know.
    /// </exception>
    public static string Text(CallToolResult result)
    {
        var blocks = result.Content.OfType<TextContentBlock>().ToArray();

        return blocks.Length == 1
            ? blocks[0].Text
            : throw new InvalidOperationException(
                $"Expected exactly one text block, found {blocks.Length}.");
    }

    /// <summary>
    /// Reads the reason a failed call carries.
    /// </summary>
    /// <param name="result">The call's failed result.</param>
    /// <returns>The message the caller reads.</returns>
    public static string Error(CallToolResult result) =>
        JsonDocument.Parse(Text(result)).RootElement.GetProperty("error").GetString()!;

    /// <summary>
    /// Builds the parameter object a call receives from name-and-value pairs.
    /// </summary>
    /// <param name="entries">The names and values, the names carrying no <c>@</c>.</param>
    /// <returns>The object the Tool receives.</returns>
    public static Dictionary<string, JsonElement> Parameters(params (string Name, object? Value)[] entries)
    {
        var parameters = new Dictionary<string, JsonElement>();
        foreach (var (name, value) in entries)
        {
            parameters[name] = JsonSerializer.SerializeToElement(value);
        }

        return parameters;
    }
}
