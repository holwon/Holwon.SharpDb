namespace SharpDb.Core.Configuration;

/// <summary>
/// The complete configuration of a running host, read once at startup and never mutated (ADR 0006).
/// </summary>
/// <remarks>
/// Every value comes from an environment variable under the <c>SHARPDB__</c> prefix — a double
/// underscore, which is the configuration system's hierarchy separator (ADR 0006). No other
/// source exists: no appsettings file, no user secrets, no command line (ADR 0008).
/// </remarks>
public sealed record SharpDbOptions
{
    /// <summary>
    /// The configuration section every key is read from.
    /// </summary>
    public const string SectionName = "SHARPDB";

    /// <summary>
    /// The database type to serve, matched against a provider's identifier.
    /// </summary>
    /// <value>
    /// The <c>SHARPDB__TYPE</c> value, such as <c>mysql</c> or <c>sqlite</c>.
    /// </value>
    public string? Type { get; init; }

    /// <summary>
    /// The connection string handed to the selected provider's Driver.
    /// </summary>
    /// <value>
    /// The <c>SHARPDB__CONNECTIONSTRING</c> value.
    /// </value>
    public string? ConnectionString { get; init; }

    /// <summary>
    /// The row limit a query uses when its caller does not ask for one.
    /// </summary>
    /// <value>
    /// The <c>SHARPDB__DEFAULTMAXROWS</c> value, 100 unless configured otherwise.
    /// </value>
    public int DefaultMaxRows { get; init; } = 100;

    /// <summary>
    /// The row limit no caller may exceed.
    /// </summary>
    /// <value>
    /// The <c>SHARPDB__MAXROWS</c> value, 1000 unless configured otherwise.
    /// </value>
    public int MaxRows { get; init; } = 1000;

    /// <summary>
    /// The command timeout applied to every query, in seconds.
    /// </summary>
    /// <value>
    /// The <c>SHARPDB__TIMEOUTSECONDS</c> value, 30 unless configured otherwise.
    /// </value>
    public int TimeoutSeconds { get; init; } = 30;
}
