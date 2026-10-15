using SharpDb.Provider.Contract;

namespace SharpDb.Provider.MariaDb.Tests;

/// <summary>
/// The query Tool's contract, asserted against a real MariaDB server.
/// </summary>
/// <param name="database">The seeded container the collection shares.</param>
[Collection(MariaDbContractCollection.Name)]
public sealed class MariaDbQueryContractTests(MariaDbContractDatabase database)
    : QueryContractSuite(database);
