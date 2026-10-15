namespace SharpDb.Provider.MariaDb.Tests;

/// <summary>
/// The collection that shares one MariaDB container between every MariaDB test.
/// </summary>
/// <remarks>
/// All of this project's tests — the two shared suites and the MariaDB-specific ones alike — name
/// this collection, so exactly one container is started per run and no test can observe another
/// test's container lifecycle.
/// </remarks>
[CollectionDefinition(Name)]
public sealed class MariaDbContractCollection : ICollectionFixture<MariaDbContractDatabase>
{
    /// <summary>
    /// The collection's name, which every test class in this project names in its own attribute.
    /// </summary>
    public const string Name = "mariadb";
}
