using SharpDb.Provider.Contract;

namespace SharpDb.Provider.PostgreSql.Tests;

/// <summary>
/// The Provider contract, asserted against a real PostgreSQL server.
/// </summary>
/// <remarks>
/// The body is empty on purpose: every assertion lives in <see cref="ProviderContractSuite"/>,
/// every engine's test project runs the same suite against the same schema. One suite, run once per
/// engine, is what proves ADR 0003's promise — that a new database type is a new Provider and
/// nothing else.
/// </remarks>
/// <param name="database">The seeded container the collection shares.</param>
[Collection(PostgreSqlContractCollection.Name)]
public sealed class PostgreSqlProviderContractTests(PostgreSqlContractDatabase database)
    : ProviderContractSuite(database);
