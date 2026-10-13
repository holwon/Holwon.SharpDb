namespace SharpDb.Provider.MsSql.Tests;

/// <summary>
/// One container serves every class in this project, so the shared suites and the engine-specific
/// ones read the same server.
/// </summary>
[CollectionDefinition(Name)]
public sealed class MsSqlContractCollection : ICollectionFixture<MsSqlContractDatabase>
{
    /// <summary>
    /// The collection's name, which every test class in this project declares.
    /// </summary>
    public const string Name = "mssql-contract";
}
