using SharpDb.Provider.Contract;

namespace SharpDb.Provider.Sqlite.Tests;

/// <summary>
/// The query Tool's contract, asserted against a real SQLite database.
/// </summary>
/// <param name="database">The seeded database the collection shares.</param>
[Collection(SqliteContractCollection.Name)]
public sealed class SqliteQueryContractTests(SqliteContractDatabase database)
    : QueryContractSuite(database);
