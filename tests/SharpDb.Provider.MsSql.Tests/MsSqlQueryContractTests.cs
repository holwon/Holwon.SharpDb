using SharpDb.Provider.Contract;

namespace SharpDb.Provider.MsSql.Tests;

/// <summary>
/// The query Tool's contract, run against a real SQL Server: the payload a call answers with, the
/// limits it applies, the parameters it binds, and the failures it reports.
/// </summary>
/// <param name="database">The seeded database the collection shares.</param>
[Collection(MsSqlContractCollection.Name)]
public sealed class MsSqlQueryContractTests(MsSqlContractDatabase database)
    : QueryContractSuite(database);
