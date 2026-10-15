using SharpDb.Provider.Contract;

namespace SharpDb.Provider.MariaDb.Tests;

/// <summary>
/// The Provider contract, asserted against a real MariaDB server.
/// </summary>
/// <remarks>
/// The body is empty on purpose: every assertion lives in <see cref="ProviderContractSuite"/>,
/// and the MySQL project runs the same suite against the same schema. Runs of one suite are
/// what prove ADR 0003's promise — that a new database type is a new Provider and nothing else —
/// rather than projects that happen to test similar things.
/// </remarks>
/// <param name="database">The seeded container the collection shares.</param>
[Collection(MariaDbContractCollection.Name)]
public sealed class MariaDbProviderContractTests(MariaDbContractDatabase database)
    : ProviderContractSuite(database);
