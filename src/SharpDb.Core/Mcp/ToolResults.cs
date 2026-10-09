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
}
