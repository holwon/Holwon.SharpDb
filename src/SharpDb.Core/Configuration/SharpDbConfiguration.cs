using Microsoft.Extensions.Configuration;

namespace SharpDb.Core.Configuration;

/// <summary>
/// Reads the host's whole configuration, once, at startup.
/// </summary>
/// <remarks>
/// The section is named after the environment-variable prefix, so <c>SHARPDB__TYPE</c> binds to
/// <see cref="SharpDbOptions.Type"/> (ADR 0006). Nothing here consults a second source: the
/// configuration handed in is the host's only one (ADR 0008).
/// </remarks>
public static class SharpDbConfiguration
{
    /// <summary>
    /// Binds the configuration to the options the process runs on.
    /// </summary>
    /// <param name="configuration">The host's configuration, already carrying the environment variables.</param>
    /// <returns>The immutable options, read once.</returns>
    /// <exception cref="ArgumentNullException">The configuration is null.</exception>
    /// <exception cref="Startup.StartupException">
    /// A value could not be read as the kind of value its key names.
    /// </exception>
    /// <remarks>
    /// A malformed number is a configuration error like a missing key, so it becomes the same
    /// fail-fast failure rather than an exception escaping the host (ADR 0007). The binder's own
    /// message is kept because it names the offending key and value.
    /// </remarks>
    public static SharpDbOptions Read(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        try
        {
            return configuration.GetSection(SharpDbOptions.SectionName).Get<SharpDbOptions>()
                ?? new SharpDbOptions();
        }
        catch (InvalidOperationException malformedValue)
        {
            throw Startup.StartupException.InvalidOptionValue(malformedValue);
        }
    }
}
