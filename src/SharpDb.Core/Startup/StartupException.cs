namespace SharpDb.Core.Startup;

/// <summary>
/// Reports that the host cannot start.
/// </summary>
/// <remarks>
/// The <see cref="Exception.Message"/> is the text the user must see, so it is written to be
/// read on a single line on stderr — no punctuation ceremony, no stack trace, and never the
/// connection string (ADR 0007). The host adds the <c>sharpdb: </c> prefix; the message itself
/// does not carry it.
/// </remarks>
public sealed class StartupException : Exception
{
    private StartupException(string reason)
        : base(reason)
    {
    }

    /// <summary>
    /// Reports that <c>SHARPDB__TYPE</c> was not supplied.
    /// </summary>
    /// <param name="expectedTypes">The accepted types, phrased for reading.</param>
    /// <returns>The failure to report.</returns>
    public static StartupException MissingDatabaseType(string expectedTypes) =>
        new($"SHARPDB__TYPE is required (expected {expectedTypes}).");

    /// <summary>
    /// Reports that <c>SHARPDB__TYPE</c> names a database no Provider serves.
    /// </summary>
    /// <param name="type">The type the user configured.</param>
    /// <param name="expectedTypes">The accepted types, phrased for reading.</param>
    /// <returns>The failure to report.</returns>
    public static StartupException UnknownDatabaseType(string type, string expectedTypes) =>
        new($"SHARPDB__TYPE '{type}' is unknown (expected {expectedTypes}).");

    /// <summary>
    /// Reports that <c>SHARPDB__CONNECTIONSTRING</c> was not supplied.
    /// </summary>
    /// <returns>The failure to report.</returns>
    public static StartupException MissingConnectionString() =>
        new("SHARPDB__CONNECTIONSTRING is required and must not be blank.");

    /// <summary>
    /// Reports that a value in the configuration could not be read as the kind of value its key names.
    /// </summary>
    /// <param name="cause">What the configuration binder reported, echoed because it names the offending key.</param>
    /// <returns>The failure to report.</returns>
    public static StartupException InvalidOptionValue(Exception cause) =>
        new($"Invalid configuration: {cause.Message.ReplaceLineEndings(" ")}");

    /// <summary>
    /// Reports that the database could not be reached, carrying the database's own explanation.
    /// </summary>
    /// <param name="databaseType">The type of database that was probed.</param>
    /// <param name="cause">What the Driver reported, echoed so the user can act on it.</param>
    /// <returns>The failure to report.</returns>
    /// <remarks>
    /// The Driver's message never contains the connection string; the host only ever prints
    /// <see cref="Exception.Message"/>, so credentials cannot reach stderr. Line breaks in the
    /// Driver's message become spaces, because the contract is one line of stderr.
    /// </remarks>
    public static StartupException ConnectionFailed(string databaseType, Exception cause) =>
        new($"Could not connect to the {databaseType} database: {cause.Message.ReplaceLineEndings(" ")}");
}
