namespace SharpDb.Provider.Contract;

/// <summary>
/// Skips a test when the engine under contract could not be reached.
/// </summary>
/// <remarks>
/// Both shared suites and every engine-specific suite start their tests the same way, and the pair
/// of values a skip needs lives on the database itself; this is that one call, in one place.
/// </remarks>
public static class ContractDatabaseExtensions
{
    /// <summary>
    /// Skips the calling test when the engine under contract is unavailable.
    /// </summary>
    /// <param name="database">The engine under contract.</param>
    /// <remarks>
    /// Called at the top of every test rather than in a fixture, because a fixture that throws
    /// marks its tests failed; only the test body can turn an absence into a skip.
    /// </remarks>
    public static void SkipWhenUnavailable(this IContractDatabase database) =>
        Skip.IfNot(database.Available, database.SkipReason);
}
