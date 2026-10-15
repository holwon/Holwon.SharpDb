using SharpDb.Provider.Contract;

namespace SharpDb.Provider.Db2.Tests;

/// <summary>
/// The provider contract suite, run against a real Db2 LUW database: what a listing reports, what a
/// description reports, and that the connection arrives open.
/// </summary>
/// <param name="database">The seeded database the collection shares.</param>
[Collection(Db2ContractCollection.Name)]
public sealed class Db2ProviderContractTests(Db2ContractDatabase database)
    : ProviderContractSuite(database);
