namespace SharpDb.Provider.Sqlite.Tests;

/// <summary>
/// The collection that shares one seeded SQLite database between the contract tests.
/// </summary>
/// <remarks>
/// A collection fixture rather than one database per test class, so the shared suites' two classes
/// — the introspection one and the query one — assert against the same database, exactly as the
/// MySQL run shares one container.
/// </remarks>
[CollectionDefinition(Name)]
public sealed class SqliteContractCollection : ICollectionFixture<SqliteContractDatabase>
{
    /// <summary>
    /// The collection's name, which the concrete runs name in their own attributes.
    /// </summary>
    public const string Name = "sqlite-contract";
}
