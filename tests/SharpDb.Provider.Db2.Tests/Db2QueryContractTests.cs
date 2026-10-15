using SharpDb.Provider.Contract;

namespace SharpDb.Provider.Db2.Tests;

/// <summary>
/// The query Tool's contract, run against a real Db2 LUW database: the payload a call answers with,
/// the limits it applies, the parameters it binds, and the failures it reports.
/// </summary>
/// <param name="database">The seeded database the collection shares.</param>
[Collection(Db2ContractCollection.Name)]
public sealed class Db2QueryContractTests(Db2ContractDatabase database)
    : QueryContractSuite(database);
