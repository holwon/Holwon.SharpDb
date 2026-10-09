using SharpDb.Abstractions;

namespace SharpDb.Core.Startup;

/// <summary>
/// The Providers the host was assembled with, keyed by the database type that selects them.
/// </summary>
/// <remarks>
/// The catalog knows nothing about any particular database — the host hands it the Providers it
/// built (ADR 0004). Keeping it that way is what lets the "unknown type" message name the types
/// that are actually installed rather than a copy that can drift from the assembly point (ADR 0009).
/// </remarks>
public sealed class ProviderCatalog
{
    private readonly Dictionary<string, IDatabaseProvider> _providers;

    /// <summary>
    /// Creates a catalog over the given Providers.
    /// </summary>
    /// <param name="providers">Every Provider the host supports.</param>
    /// <exception cref="ArgumentException">Two Providers claim the same database type.</exception>
    public ProviderCatalog(params IDatabaseProvider[] providers)
    {
        ArgumentNullException.ThrowIfNull(providers);

        // Group rather than ToDictionary: a duplicated type is a defect in the host's own
        // composition, so the message should name the type instead of blaming the sequence.
        _providers = providers
            .GroupBy(provider => provider.Type, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Count() == 1
                    ? group.First()
                    : throw new ArgumentException(
                        $"More than one Provider claims the database type '{group.Key}'.",
                        nameof(providers)),
                StringComparer.OrdinalIgnoreCase);

        ExpectedTypes = string.Join(
            " or ",
            _providers.Keys.Order(StringComparer.Ordinal).Select(type => $"\"{type}\""));
    }

    /// <summary>
    /// The accepted database types, quoted and joined for a failure message.
    /// </summary>
    /// <value>
    /// A phrase such as <c>"mysql" or "sqlite"</c>.
    /// </value>
    public string ExpectedTypes { get; }

    /// <summary>
    /// Picks the Provider that serves the configured database type.
    /// </summary>
    /// <param name="type">The <c>SHARPDB__TYPE</c> value, which may be absent.</param>
    /// <returns>The Provider to serve the process with.</returns>
    /// <exception cref="StartupException">The type is missing, or no Provider claims it.</exception>
    /// <remarks>
    /// The comparison ignores case and surrounding whitespace: capitalization is a likely
    /// typo, and accepting it can never change which database is served.
    /// </remarks>
    public IDatabaseProvider Select(string? type)
    {
        if (string.IsNullOrWhiteSpace(type))
        {
            throw StartupException.MissingDatabaseType(ExpectedTypes);
        }

        var requested = type.Trim();
        if (!_providers.TryGetValue(requested, out var provider))
        {
            throw StartupException.UnknownDatabaseType(requested, ExpectedTypes);
        }

        return provider;
    }
}
