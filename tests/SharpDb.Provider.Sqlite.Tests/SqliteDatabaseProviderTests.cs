using Microsoft.Data.Sqlite;
using SharpDb.Abstractions;
using SharpDb.Abstractions.Schema;

namespace SharpDb.Provider.Sqlite.Tests;

/// <summary>
/// The SQLite Provider's contract, asserted against a real database: what the listing reports,
/// and that the connection it opens is one the engine refuses to write through (ADR 0002, layer 2).
/// </summary>
/// <remarks>
/// Each assertion is about what the Provider answers — which objects are in this database, which
/// databases are visible — never about the introspection SQL it happens to generate, so the SQL
/// stays free to change.
/// </remarks>
public class SqliteDatabaseProviderTests
{
    /// <summary>
    /// The database's shape: several tables, a view, a foreign key, and a table with
    /// <c>AUTOINCREMENT</c> so the engine creates bookkeeping objects of its own.
    /// </summary>
    private const string Schema = """
        CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT NOT NULL, email TEXT);
        CREATE TABLE orders (id INTEGER PRIMARY KEY, user_id INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE);
        CREATE TABLE zeta (id INTEGER PRIMARY KEY);
        CREATE TABLE alpha (id INTEGER PRIMARY KEY);
        CREATE TABLE tickets (id INTEGER PRIMARY KEY AUTOINCREMENT, subject TEXT);
        CREATE VIEW active_users AS SELECT id, name FROM users;
        """;

    private readonly IDatabaseProvider _provider = new SqliteDatabaseProvider();

    [Fact]
    public void Type_is_sqlite()
    {
        Assert.Equal("sqlite", _provider.Type);
    }

    [Fact]
    public async Task ListTables_reports_tables_and_views_ordered_by_name()
    {
        using var database = await SqliteTestDatabase.CreateAsync(Schema);
        await using var connection = await _provider.CreateReadOnlyConnectionAsync(
            database.ConnectionString,
            CancellationToken.None);

        var listed = (await _provider.ListTablesAsync(connection, CancellationToken.None))
            .Select(table => (table.Name, table.Type))
            .ToArray();

        Assert.Equal(
            [
                ("active_users", TableType.View),
                ("alpha", TableType.Table),
                ("orders", TableType.Table),
                ("tickets", TableType.Table),
                ("users", TableType.Table),
                ("zeta", TableType.Table),
            ],
            listed);
    }

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

    [Fact]
    public async Task ListDatabases_reports_the_connected_database()
    {
        using var database = await SqliteTestDatabase.CreateAsync(Schema);
        await using var connection = await _provider.CreateReadOnlyConnectionAsync(
            database.ConnectionString,
            CancellationToken.None);

        var databases = await _provider.ListDatabasesAsync(connection, CancellationToken.None);

        Assert.Equal(["main"], databases);
    }

    [Fact]
    public async Task The_connection_the_provider_opens_refuses_writes()
    {
        // The read-only guarantee is engine-level (ADR 0002, layer 2), so it is asserted by trying
        // to write rather than by inspecting the connection string the Provider built: the string
        // is how the mode is asked for, the refusal is the mode actually being in force.
        using var database = await SqliteTestDatabase.CreateAsync(Schema);
        await using var connection = await _provider.CreateReadOnlyConnectionAsync(
            database.ConnectionString,
            CancellationToken.None);

        await using var write = connection.CreateCommand();
        write.CommandText = "INSERT INTO users (name) VALUES ('should not land')";

        await Assert.ThrowsAsync<SqliteException>(() => write.ExecuteNonQueryAsync());
    }

    [Fact]
    public async Task The_connection_the_provider_opens_can_be_used_at_once()
    {
        // "Already open" is part of the contract: the connection arrives usable, with no implied
        // Open() left to the caller.
        using var database = await SqliteTestDatabase.CreateAsync(Schema);

        await using var connection = await _provider.CreateReadOnlyConnectionAsync(
            database.ConnectionString,
            CancellationToken.None);

        Assert.Equal(System.Data.ConnectionState.Open, connection.State);
    }

    [Fact]
    public async Task ListTables_rejects_a_missing_connection() =>
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _provider.ListTablesAsync(null!, CancellationToken.None));

    [Fact]
    public async Task ListDatabases_rejects_a_missing_connection() =>
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _provider.ListDatabasesAsync(null!, CancellationToken.None));
}
