using SharpDb.Provider.Contract;

namespace SharpDb.Provider.Oracle.Tests;

/// <summary>
/// The Provider contract, asserted against a real Oracle server.
/// </summary>
/// <remarks>
/// The body is empty on purpose: every assertion lives in <see cref="ProviderContractSuite"/>,
/// which the other engines' projects run too. Running the same suite once per engine is what makes
/// the Provider port a contract rather than several implementations that merely resemble one
/// another (ADR 0003).
/// </remarks>
/// <param name="database">The seeded container the collection shares.</param>
[Collection(OracleContractCollection.Name)]
public sealed class OracleProviderContractTests(OracleContractDatabase database)
    : ProviderContractSuite(database);
