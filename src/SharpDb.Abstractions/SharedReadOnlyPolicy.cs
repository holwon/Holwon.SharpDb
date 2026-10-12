namespace SharpDb.Abstractions;

/// <summary>
/// The read-only policy MySQL and SQLite share, written once so the two Providers that use it do
/// not each keep a copy.
/// </summary>
/// <remarks>
/// MySQL and SQLite contribute the same data today, and that is not a coincidence to be factored
/// away later: both are read with the generic dialect because both are written with back-quoted
/// or double-quoted identifiers, and the four denied functions are the ones measured to be
/// dangerous on either engine. When one of them gains a reason to differ, it declares its own
/// policy and this type keeps serving the other.
/// </remarks>
public static class SharedReadOnlyPolicy
{
    /// <summary>
    /// The policy MySQL and SQLite are both read under.
    /// </summary>
    /// <value>
    /// The generic dialect, and the four function names the Guard refused before this policy
    /// existed — so adopting the policy changes nothing about how either engine behaves.
    /// </value>
    public static ReadOnlyPolicy Generic { get; } = new()
    {
        Dialect = SqlDialect.Generic,
        DeniedFunctions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "LOAD_FILE", // MySQL: reads a file from the server host
            "GET_LOCK",  // MySQL: takes a named advisory lock
            "SLEEP",     // MySQL: blocks the server for a while
            "BENCHMARK", // MySQL: burns server CPU for a while
        },
    };
}
