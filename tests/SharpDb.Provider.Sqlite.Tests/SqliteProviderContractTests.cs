using SharpDb.Provider.Contract;

namespace SharpDb.Provider.Sqlite.Tests;

/// <summary>
/// The Provider contract, asserted against a real SQLite database.
/// </summary>
/// <remarks>
/// The body is empty on purpose: every assertion lives in <see cref="ProviderContractSuite"/>,
/// which MySQL's project runs too. Running the same suite twice is what makes the Provider port a
/// contract rather than two implementations that merely resemble each other (ADR 0003).
/// </remarks>
/// <param name="database">The seeded database the collection shares.</param>
[Collection(SqliteContractCollection.Name)]
public sealed class SqliteProviderContractTests(SqliteContractDatabase database)
    : ProviderContractSuite(database);
