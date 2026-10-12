namespace SharpDb.Provider.Oracle.Tests;

/// <summary>
/// The collection that shares one Oracle container between every Oracle test.
/// </summary>
/// <remarks>
/// All of this project's tests — the two shared suites and the Oracle-specific ones alike — name
/// this collection, so exactly one container is started per run. Oracle takes minutes to become
/// ready, so sharing the one server is not an optimization but the only way the suite is usable.
/// </remarks>
[CollectionDefinition(Name)]
public sealed class OracleContractCollection : ICollectionFixture<OracleContractDatabase>
{
    /// <summary>
    /// The collection's name, which every test class in this project names in its own attribute.
    /// </summary>
    public const string Name = "oracle";
}
