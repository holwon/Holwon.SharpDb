namespace SharpDb.Provider.Db2.Tests;

/// <summary>
/// One container serves every class in this project, so the shared suites and the engine-specific
/// ones read the same server.
/// </summary>
[CollectionDefinition(Name)]
public sealed class Db2ContractCollection : ICollectionFixture<Db2ContractDatabase>
{
    /// <summary>
    /// The collection's name, which every test class in this project declares.
    /// </summary>
    public const string Name = "db2-contract";
}
