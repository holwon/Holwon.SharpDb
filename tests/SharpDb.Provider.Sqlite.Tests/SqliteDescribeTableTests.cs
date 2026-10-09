using SharpDb.Abstractions;
using SharpDb.Abstractions.Schema;

namespace SharpDb.Provider.Sqlite.Tests;

/// <summary>
/// The structure the Provider reports for one table: its columns, primary key, foreign keys and
/// indexes, as a caller asking "what does this table look like" would receive them.
/// </summary>
/// <remarks>
/// Every assertion is about what the Provider answers — column order, the declared type text, the
/// order of a key's columns, the content of a foreign key and an index — never about the
/// introspection SQL it happens to generate, so that SQL stays free to change.
/// </remarks>
public class SqliteDescribeTableTests
{
    /// <summary>
    /// The database's shape: a parent table, a child with a foreign key, a default and an
    /// autoincrement key, a table with no foreign key and a column declared with no type at all, a
    /// composite primary key whose key order differs from the declaration order, a foreign key that
    /// points at a parent's key implicitly, and a view.
    /// </summary>
    private const string Schema = """
        CREATE TABLE orgs (id INTEGER PRIMARY KEY, name TEXT NOT NULL);
        CREATE TABLE users (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            org_id INTEGER NOT NULL REFERENCES orgs(id) ON DELETE CASCADE ON UPDATE SET NULL,
            email TEXT,
            role TEXT NOT NULL DEFAULT 'member');
        CREATE UNIQUE INDEX idx_users_email ON users(email);
        CREATE INDEX idx_users_role ON users(role);
        CREATE TABLE composite (a TEXT, b INTEGER, PRIMARY KEY (b, a));
        CREATE TABLE memberships (id INTEGER PRIMARY KEY, user_id INTEGER REFERENCES users);
        CREATE VIEW active_users AS SELECT id, email FROM users;
        """;

    private readonly IDatabaseProvider _provider = new SqliteDatabaseProvider();

    [Fact]
    public async Task DescribeTable_reports_columns_in_declaration_order_with_their_declared_types()
    {
        var schema = await DescribeAsync("users");

        Assert.NotNull(schema);
        Assert.Equal(
            [
                // The key reports nullable, and that is what the schema says: SQLite records
                // notnull=0 unless NOT NULL is written, even for a rowid alias. Handing NULL to it
                // does not fail — a value is generated instead — so "accepts null" is still true.
                ("id", 1, "INTEGER", true, null, true, null),
                ("org_id", 2, "INTEGER", false, null, false, null),
                ("email", 3, "TEXT", true, null, false, null),
                ("role", 4, "TEXT", false, "'member'", false, null),
            ],
            schema.Columns.Select(column => (
                column.Name,
                column.Ordinal,
                column.DataType,
                column.IsNullable,
                column.DefaultValue,
                column.IsAutoIncrement,
                column.Comment)));
    }

    [Fact]
    public async Task DescribeTable_reports_a_default_value_as_the_expression_text_and_absence_as_null()
    {
        var schema = await DescribeAsync("users");

        Assert.NotNull(schema);
        Assert.Equal("'member'", schema.Columns.Single(column => column.Name == "role").DefaultValue);
        Assert.Null(schema.Columns.Single(column => column.Name == "email").DefaultValue);
    }

    [Fact]
    public async Task DescribeTable_computes_autoincrement_from_an_integer_primary_key()
    {
        // SQLite records no such flag: whether a value is generated follows from the key being one
        // column and that column being declared INTEGER. Nothing else on the table is one.
        var schema = await DescribeAsync("users");

        Assert.NotNull(schema);
        Assert.True(schema.Columns.Single(column => column.Name == "id").IsAutoIncrement);
        Assert.All(
            schema.Columns.Where(column => column.Name != "id"),
            column => Assert.False(column.IsAutoIncrement));
    }

    [Fact]
    public async Task DescribeTable_does_not_compute_autoincrement_for_a_composite_key()
    {
        // The first column carries key position one, but a multi-column key's value is not generated,
        // which is why autoincrement cannot be judged a column at a time.
        var schema = await DescribeAsync("composite");

        Assert.NotNull(schema);
        Assert.All(schema.Columns, column => Assert.False(column.IsAutoIncrement));
    }

    [Fact]
    public async Task DescribeTable_reports_the_primary_key_in_key_order()
    {
        // The key is declared (b, a), so b is its first column even though a is declared first. A
        // composite key's order is part of the key, which is why this is a sequence and not a set.
        var schema = await DescribeAsync("composite");

        Assert.NotNull(schema);
        Assert.Equal(["b", "a"], schema.PrimaryKey);
    }

    [Fact]
    public async Task DescribeTable_reports_the_type_string_exactly_as_declared_and_empty_when_none()
    {
        using var database = await SqliteTestDatabase.CreateAsync(
            """CREATE TABLE raw (code, label VARCHAR(50));""");
        await using var connection = await _provider.CreateReadOnlyConnectionAsync(
            database.ConnectionString,
            CancellationToken.None);

        var schema = await _provider.DescribeTableAsync(connection, "raw", CancellationToken.None);

        Assert.NotNull(schema);
        Assert.Equal("", schema.Columns.Single(column => column.Name == "code").DataType);
        Assert.Equal("VARCHAR(50)", schema.Columns.Single(column => column.Name == "label").DataType);
    }

    [Fact]
    public async Task DescribeTable_reports_a_foreign_key_with_its_columns_table_and_actions()
    {
        var schema = await DescribeAsync("users");

        Assert.NotNull(schema);
        var foreignKey = Assert.Single(schema.ForeignKeys);
        Assert.Equal("orgs", foreignKey.ReferencedTable);
        Assert.Equal(["org_id"], foreignKey.Columns);
        Assert.Equal(["id"], foreignKey.ReferencedColumns);
        Assert.Equal(ForeignKeyAction.Cascade, foreignKey.OnDelete);
        Assert.Equal(ForeignKeyAction.SetNull, foreignKey.OnUpdate);
        Assert.False(string.IsNullOrWhiteSpace(foreignKey.Name));
    }

    [Fact]
    public async Task DescribeTable_reports_a_table_without_a_foreign_key_as_having_none()
    {
        var schema = await DescribeAsync("orgs");

        Assert.NotNull(schema);
        Assert.Empty(schema.ForeignKeys);
    }

    [Fact]
    public async Task DescribeTable_leaves_referenced_columns_empty_when_a_key_points_at_the_parent_key_implicitly()
    {
        // The schema named no referenced column, so there is none to report. This is an answer, not
        // a failure, so it must arrive as an empty list rather than as an error.
        var schema = await DescribeAsync("memberships");

        Assert.NotNull(schema);
        var foreignKey = Assert.Single(schema.ForeignKeys);
        Assert.Equal("users", foreignKey.ReferencedTable);
        Assert.Equal(["user_id"], foreignKey.Columns);
        Assert.Empty(foreignKey.ReferencedColumns);
    }

    [Fact]
    public async Task DescribeTable_lists_indexes_with_their_columns_and_uniqueness()
    {
        var schema = await DescribeAsync("users");

        Assert.NotNull(schema);
        Assert.Equal(
            [("idx_users_email", true, new[] { "email" }), ("idx_users_role", false, new[] { "role" })],
            schema.Indexes.Select(index => (index.Name, index.IsUnique, index.Columns.ToArray())));
    }

    [Fact]
    public async Task DescribeTable_does_not_list_the_primary_key_among_the_indexes()
    {
        // A composite key makes SQLite create an index for it, and that index must not appear: the
        // primary key is reported as its own member, and listing it twice would present one fact as
        // two.
        var schema = await DescribeAsync("composite");

        Assert.NotNull(schema);
        Assert.Equal(["b", "a"], schema.PrimaryKey);
        Assert.Empty(schema.Indexes);
    }

    [Fact]
    public async Task DescribeTable_lists_the_index_a_unique_constraint_creates()
    {
        // Excluding the primary key's index must not exclude every index the engine named itself:
        // a UNIQUE constraint makes one too, and it is an index of the table like any other.
        using var database = await SqliteTestDatabase.CreateAsync(
            """CREATE TABLE codes (id INTEGER PRIMARY KEY, code TEXT UNIQUE);""");
        await using var connection = await _provider.CreateReadOnlyConnectionAsync(
            database.ConnectionString,
            CancellationToken.None);

        var schema = await _provider.DescribeTableAsync(connection, "codes", CancellationToken.None);

        Assert.NotNull(schema);
        var index = Assert.Single(schema.Indexes);
        Assert.Equal("sqlite_autoindex_codes_1", index.Name);
        Assert.True(index.IsUnique);
        Assert.Equal(["code"], index.Columns);
    }

    [Fact]
    public async Task DescribeTable_describes_a_view()
    {
        var schema = await DescribeAsync("active_users");

        Assert.NotNull(schema);
        Assert.Equal(TableType.View, schema.Type);
        Assert.Equal("active_users", schema.Name);
        Assert.Equal(["id", "email"], schema.Columns.Select(column => column.Name));
        Assert.Empty(schema.PrimaryKey);
        Assert.Empty(schema.ForeignKeys);
        Assert.Empty(schema.Indexes);
    }

    [Fact]
    public async Task DescribeTable_reports_an_expression_index_with_no_columns()
    {
        // SQLite reports an expression's column with no name. There is no column name to hand back,
        // so the index arrives with none rather than the Provider failing on the absent value.
        using var database = await SqliteTestDatabase.CreateAsync(
            """
            CREATE TABLE people (id INTEGER PRIMARY KEY, email TEXT);
            CREATE UNIQUE INDEX idx_people_email_lower ON people(lower(email));
            """);
        await using var connection = await _provider.CreateReadOnlyConnectionAsync(
            database.ConnectionString,
            CancellationToken.None);

        var schema = await _provider.DescribeTableAsync(connection, "people", CancellationToken.None);

        Assert.NotNull(schema);
        var index = Assert.Single(schema.Indexes);
        Assert.Equal("idx_people_email_lower", index.Name);
        Assert.True(index.IsUnique);
        Assert.Empty(index.Columns);
    }

    [Fact]
    public async Task DescribeTable_leaves_out_a_generated_column()
    {
        // pragma_table_info reports ordinary columns only; a generated column is not one a caller
        // can write, so it is left as the engine has it rather than asked for separately.
        using var database = await SqliteTestDatabase.CreateAsync(
            """
            CREATE TABLE metrics (id INTEGER PRIMARY KEY, base INTEGER, doubled INTEGER GENERATED ALWAYS AS (base * 2));
            """);
        await using var connection = await _provider.CreateReadOnlyConnectionAsync(
            database.ConnectionString,
            CancellationToken.None);

        var schema = await _provider.DescribeTableAsync(connection, "metrics", CancellationToken.None);

        Assert.NotNull(schema);
        Assert.Equal(["id", "base"], schema.Columns.Select(column => column.Name));
    }

    [Fact]
    public async Task DescribeTable_reports_no_comment_because_SQLite_keeps_none()
    {
        var schema = await DescribeAsync("users");

        Assert.NotNull(schema);
        Assert.Null(schema.Comment);
        Assert.All(schema.Columns, column => Assert.Null(column.Comment));
    }

    [Fact]
    public async Task DescribeTable_returns_null_for_a_table_that_does_not_exist()
    {
        // A name that matches nothing is answered with nothing, and the Tool is what turns that into
        // a message the caller can read.
        var schema = await DescribeAsync("no_such_table");

        Assert.Null(schema);
    }

    [Fact]
    public async Task DescribeTable_returns_null_rather_than_the_engines_own_bookkeeping_table()
    {
        // An AUTOINCREMENT table makes SQLite create sqlite_sequence beside the database's own
        // tables. It is not one of this database's tables and may not be described as if it were.
        Assert.Null(await DescribeAsync("sqlite_sequence"));
    }

    [Fact]
    public async Task DescribeTable_finds_a_table_whose_name_the_caller_cased_differently()
    {
        // SQLite matches identifiers case-insensitively, so a caller that asks for USERS is asking
        // about users; answering "no such table" would be wrong.
        var schema = await DescribeAsync("USERS");

        Assert.NotNull(schema);
        Assert.Equal("users", schema.Name);
        Assert.Equal(["id", "org_id", "email", "role"], schema.Columns.Select(column => column.Name));
    }

    [Fact]
    public async Task DescribeTable_rejects_a_missing_connection() =>
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _provider.DescribeTableAsync(null!, "users", CancellationToken.None));

    [Fact]
    public async Task DescribeTable_rejects_a_missing_name()
    {
        using var database = await SqliteTestDatabase.CreateAsync(Schema);
        await using var connection = await _provider.CreateReadOnlyConnectionAsync(
            database.ConnectionString,
            CancellationToken.None);

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _provider.DescribeTableAsync(connection, null!, CancellationToken.None));
    }

    /// <summary>
    /// Describes one of the tables in the shared schema.
    /// </summary>
    /// <param name="tableName">The table or view to describe.</param>
    /// <returns>The structure, or <see langword="null"/> when the schema holds no such object.</returns>
    private async Task<TableSchema?> DescribeAsync(string tableName)
    {
        using var database = await SqliteTestDatabase.CreateAsync(Schema);
        await using var connection = await _provider.CreateReadOnlyConnectionAsync(
            database.ConnectionString,
            CancellationToken.None);

        return await _provider.DescribeTableAsync(connection, tableName, CancellationToken.None);
    }
}
