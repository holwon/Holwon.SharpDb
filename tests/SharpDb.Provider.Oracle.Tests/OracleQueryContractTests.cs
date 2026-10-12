using SharpDb.Provider.Contract;

namespace SharpDb.Provider.Oracle.Tests;

/// <summary>
/// The query Tool's contract, asserted against a real Oracle server.
/// </summary>
/// <param name="database">The seeded container the collection shares.</param>
[Collection(OracleContractCollection.Name)]
public sealed class OracleQueryContractTests(OracleContractDatabase database)
    : QueryContractSuite(database);
