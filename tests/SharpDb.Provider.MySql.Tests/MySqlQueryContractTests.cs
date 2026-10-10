using SharpDb.Provider.Contract;

namespace SharpDb.Provider.MySql.Tests;

/// <summary>
/// The query Tool's contract, asserted against a real MySQL server.
/// </summary>
/// <param name="database">The seeded container the collection shares.</param>
[Collection(MySqlContractCollection.Name)]
public sealed class MySqlQueryContractTests(MySqlContractDatabase database)
    : QueryContractSuite(database);
