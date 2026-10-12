namespace SharpDb.Provider.PostgreSql.Tests;

/// <summary>
/// The collection that shares one seeded PostgreSQL server between the contract tests.
/// </summary>
/// <remarks>
/// A collection fixture rather than one container per test class, so the shared suites' classes —
/// the introspection one and the query one — assert against the same server, exactly as the MySQL
/// and SQLite runs do. Measured on this machine, starting the container takes a few seconds, so a
/// container per test would cost minutes.
/// </remarks>
[CollectionDefinition(Name)]
public sealed class PostgreSqlContractCollection : ICollectionFixture<PostgreSqlContractDatabase>
{
    /// <summary>
    /// The collection's name, which the concrete runs name in their own attributes.
    /// </summary>
    public const string Name = "postgresql-contract";
}
