namespace SharpDb.Provider.MySql.Tests;

/// <summary>
/// The collection that shares one MySQL container between every MySQL test.
/// </summary>
/// <remarks>
/// All of this project's tests — the two shared suites and the MySQL-specific ones alike — name
/// this collection, so exactly one container is started per run and no test can observe another
/// test's container lifecycle.
/// </remarks>
[CollectionDefinition(Name)]
public sealed class MySqlContractCollection : ICollectionFixture<MySqlContractDatabase>
{
    /// <summary>
    /// The collection's name, which every test class in this project names in its own attribute.
    /// </summary>
    public const string Name = "mysql";
}
