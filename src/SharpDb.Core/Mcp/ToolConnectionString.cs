using SharpDb.Core.Configuration;

namespace SharpDb.Core.Mcp;

/// <summary>
/// The connection string every Tool connects with, proven present before a Tool is built.
/// </summary>
/// <remarks>
/// A blank string is refused here rather than at the first call: it can only mean the wiring put
/// the Tools ahead of the startup check that guarantees one, and substituting an empty string would
/// surface later as an obscure database error instead of the wiring defect it is.
/// </remarks>
internal static class ToolConnectionString
{
    /// <summary>
    /// Reads the connection string the Tools must use.
    /// </summary>
    /// <param name="options">The configuration read at startup.</param>
    /// <returns>The connection string, which is never blank.</returns>
    /// <exception cref="InvalidOperationException">The configuration carries no connection string.</exception>
    internal static string Required(SharpDbOptions? options)
    {
        var connectionString = options?.ConnectionString;

        return string.IsNullOrWhiteSpace(connectionString)
            ? throw new InvalidOperationException("The Tools were built without a connection string.")
            : connectionString;
    }
}
