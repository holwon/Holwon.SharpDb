using SharpDb.Provider.Contract;

namespace SharpDb.Provider.PostgreSql.Tests;

/// <summary>
/// The query contract, asserted against a real PostgreSQL server.
/// </summary>
/// <remarks>
/// The body is empty on purpose: every assertion lives in <see cref="QueryContractSuite"/>, and
/// the MySQL and SQLite projects run the same suite against the same schema. Three runs of one
/// suite are what make the Tool's behaviour a contract rather than three implementations that
/// resemble one another.
/// </remarks>
/// <param name="database">The seeded container the collection shares.</param>
[Collection(PostgreSqlContractCollection.Name)]
public sealed class PostgreSqlQueryContractTests(PostgreSqlContractDatabase database)
    : QueryContractSuite(database);
