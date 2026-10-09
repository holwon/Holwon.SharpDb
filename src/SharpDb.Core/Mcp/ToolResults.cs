using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol.Protocol;

namespace SharpDb.Core.Mcp;

/// <summary>
/// Builds the result a Tool hands back to the client, in the shape the Tools agree on.
/// </summary>
/// <remarks>
/// A payload travels as a <see cref="CallToolResult"/> rather than as a plain object, because a
/// Tool that reports a business failure must do so as a result rather than as an exception
/// (ADR 0010) — and a method has one return type, so the success path has to use it too.
/// <para>
/// Payloads serialize with camelCase names and omit absent values, so a reader sees a field only
/// when there is something in it.
/// </para>
/// </remarks>
internal static class ToolResults
{
    /// <summary>
    /// How every Tool's payload is written.
    /// </summary>
    private static readonly JsonSerializerOptions Serializer = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        // A closed set is named, not numbered: a referential action arrives as "Cascade", so the
        // reader of a payload is told what the action is rather than being left to decode a number.
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>
    /// Wraps a payload as the text of a successful result.
    /// </summary>
    /// <typeparam name="TPayload">The payload's type, which decides the JSON shape.</typeparam>
    /// <param name="payload">The value to serialize.</param>
    /// <returns>The result the client receives.</returns>
    internal static CallToolResult Success<TPayload>(TPayload payload) => new()
    {
        Content = [new TextContentBlock { Text = JsonSerializer.Serialize(payload, Serializer) }],
    };

    /// <summary>
    /// Wraps a message as a failed result, which is how a Tool reports a business failure.
    /// </summary>
    /// <param name="message">What went wrong, in words meant for the caller.</param>
    /// <returns>The result the client receives, marked as an error.</returns>
    /// <remarks>
    /// The failure travels as a result rather than as an exception, and it carries a message the
    /// caller can act on, because the SDK would flatten any exception into a generic sentence and
    /// the caller would never learn why it was refused (ADR 0010).
    /// </remarks>
    internal static CallToolResult Failure(string message) => new()
    {
        IsError = true,
        Content = [new TextContentBlock { Text = JsonSerializer.Serialize(new { error = message }, Serializer) }],
    };
}
