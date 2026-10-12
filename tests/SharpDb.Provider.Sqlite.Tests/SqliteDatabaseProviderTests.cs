using SharpDb.Abstractions;

namespace SharpDb.Provider.Sqlite.Tests;

/// <summary>
/// What the SQLite Provider answers that the shared contract suite cannot assert, because no other
/// engine answers it the same way.
/// </summary>
/// <remarks>
/// Everything every engine agrees on lives in <c>SharpDb.Provider.Contract</c> and is asserted there
/// once, for both. What remains here is SQLite's own behaviour: the bookkeeping objects the engine
/// creates for itself, the <c>LIKE</c> wildcard inside a filter, a name a caller cased differently,
/// and an empty database.
/// </remarks>
public class SqliteDatabaseProviderTests
{
    /// <summary>
    /// The database's shape: a table with <c>AUTOINCREMENT</c> so the engine creates bookkeeping
    /// objects of its own.
    /// </summary>
    private const string Schema = """
        CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT NOT NULL);
        CREATE TABLE tickets (id INTEGER PRIMARY KEY AUTOINCREMENT, subject TEXT);
        """;

    private readonly IDatabaseProvider _provider = new SqliteDatabaseProvider();

    [Fact]
    public async Task ListTables_leaves_out_the_engines_own_objects()
    {
        // The AUTOINCREMENT above makes SQLite create sqlite_sequence, an engine bookkeeping table
        // that lives in sqlite_schema alongside the database's own. It is not a table of this
        // database, and it may not appear as one.
        using var database = await SqliteTestDatabase.CreateAsync(Schema);
        await using var connection = await _provider.CreateReadOnlyConnectionAsync(
            database.ConnectionString,
            CancellationToken.None);

        var names = (await _provider.ListTablesAsync(connection, CancellationToken.None))
            .Select(table => table.Name);

        Assert.DoesNotContain(names, name => name.StartsWith("sqlite_", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ListTables_keeps_a_table_whose_name_merely_looks_like_the_engines()
    {
        // sqlite_ is not the whole story: the underscore is a single-character wildcard in LIKE,
        // so a naive filter would also hide this genuine table the user created.
        using var database = await SqliteTestDatabase.CreateAsync(
            """CREATE TABLE "sqliteXmine" (id INTEGER PRIMARY KEY);""");
        await using var connection = await _provider.CreateReadOnlyConnectionAsync(
            database.ConnectionString,
            CancellationToken.None);

        var names = (await _provider.ListTablesAsync(connection, CancellationToken.None))
            .Select(table => table.Name);

        Assert.Contains("sqliteXmine", names);
    }

    [Fact]
    public async Task ListTables_reports_no_comment_because_SQLite_keeps_none()
    {
        using var database = await SqliteTestDatabase.CreateAsync(Schema);
        await using var connection = await _provider.CreateReadOnlyConnectionAsync(
            database.ConnectionString,
            CancellationToken.None);

        var tables = await _provider.ListTablesAsync(connection, CancellationToken.None);

        Assert.NotEmpty(tables);
        Assert.All(tables, table => Assert.Null(table.Comment));
    }

    [Fact]
    public async Task ListTables_of_an_empty_database_is_empty()
    {
        using var database = await SqliteTestDatabase.CreateAsync(string.Empty);
        await using var connection = await _provider.CreateReadOnlyConnectionAsync(
            database.ConnectionString,
            CancellationToken.None);

        var tables = await _provider.ListTablesAsync(connection, CancellationToken.None);

        Assert.Empty(tables);
    }
}
