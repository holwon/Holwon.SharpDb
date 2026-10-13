using SharpDb.Abstractions;
using SharpDb.Abstractions.Schema;

namespace SharpDb.Provider.Sqlite.Tests;

/// <summary>
/// What SQLite's own introspection answers that the shared contract suite cannot assert, because
/// no other engine answers it the same way.
/// </summary>
/// <remarks>
/// Everything every engine agrees on — column order and ordinals, the primary key, a foreign key's
/// columns and actions, an index's columns and uniqueness, a view, a name that matches nothing —
/// lives in <c>SharpDb.Provider.Contract</c> and is asserted there once for every engine. What remains
/// here is what only SQLite does: a declared type that may be empty, autoincrement computed from
/// the key's shape, an implicit foreign key target, an index a <c>UNIQUE</c> constraint creates,
/// an expression's nameless column, a generated column, and a name matched case-insensitively.
/// <para>
/// Every test describes one table of its own through <see cref="DescribeAsync"/>, which is where
/// the create-open-describe walk lives so no test repeats it.
/// </para>
/// </remarks>
public class SqliteDescribeTableTests
{
    private readonly IDatabaseProvider _provider = new SqliteDatabaseProvider();

    [Fact]
    public async Task DescribeTable_reports_the_type_string_exactly_as_declared_and_empty_when_none()
    {
        // SQLite allows a column with no declared type at all. The declaration is what a caller has
        // to work with, so it is reported as written rather than normalised.
        var schema = await DescribeAsync("""CREATE TABLE raw (code, label VARCHAR(50));""", "raw");

        Assert.NotNull(schema);
        Assert.Equal("", schema.Columns.Single(column => column.Name == "code").DataType);
        Assert.Equal("VARCHAR(50)", schema.Columns.Single(column => column.Name == "label").DataType);
    }

    [Fact]
    public async Task DescribeTable_computes_autoincrement_from_an_integer_primary_key()
    {
        // SQLite records no such flag: whether a value is generated follows from the key being one
        // column and that column being declared INTEGER. Nothing else on the table is one.
        var schema = await DescribeAsync(
            """
            CREATE TABLE users (id INTEGER PRIMARY KEY AUTOINCREMENT, email TEXT, role TEXT NOT NULL DEFAULT 'member');
            """,
            "users");

        Assert.NotNull(schema);
        Assert.True(schema.Columns.Single(column => column.Name == "id").IsAutoIncrement);
        Assert.All(
            schema.Columns.Where(column => column.Name != "id"),
            column => Assert.False(column.IsAutoIncrement));
    }

    [Fact]
    public async Task DescribeTable_does_not_compute_autoincrement_for_a_key_that_is_not_an_exact_integer()
    {
        // Only the exact type name INTEGER makes a column an alias for the row id: a BIGINT key is
        // not one, so a value is not generated for it.
        var schema = await DescribeAsync("""CREATE TABLE big_key (id BIGINT PRIMARY KEY, label TEXT);""", "big_key");

        Assert.NotNull(schema);
        Assert.False(schema.Columns.Single(column => column.Name == "id").IsAutoIncrement);
    }

    [Fact]
    public async Task DescribeTable_reports_the_default_expression_as_the_text_the_schema_wrote()
    {
        var schema = await DescribeAsync(
            """CREATE TABLE users (id INTEGER PRIMARY KEY, role TEXT NOT NULL DEFAULT 'member', email TEXT);""",
            "users");

        Assert.NotNull(schema);
        Assert.Equal("'member'", schema.Columns.Single(column => column.Name == "role").DefaultValue);
        Assert.Null(schema.Columns.Single(column => column.Name == "email").DefaultValue);
    }

    [Fact]
    public async Task DescribeTable_leaves_referenced_columns_empty_when_a_key_points_at_the_parent_key_implicitly()
    {
        // The schema named no referenced column, so there is none to report. This is an answer, not
        // a failure, so it must arrive as an empty list rather than as an error.
        var schema = await DescribeAsync(
            """
            CREATE TABLE users (id INTEGER PRIMARY KEY);
            CREATE TABLE memberships (id INTEGER PRIMARY KEY, user_id INTEGER REFERENCES users);
            """,
            "memberships");

        Assert.NotNull(schema);
        var foreignKey = Assert.Single(schema.ForeignKeys);
        Assert.Equal("users", foreignKey.ReferencedTable);
        Assert.Equal(["user_id"], foreignKey.Columns);
        Assert.Empty(foreignKey.ReferencedColumns);
    }

    [Fact]
    public async Task DescribeTable_lists_the_index_a_unique_constraint_creates()
    {
        // Excluding the primary key's index must not exclude every index the engine named itself:
        // a UNIQUE constraint makes one too, and it is an index of the table like any other.
        var schema = await DescribeAsync(
            """CREATE TABLE codes (id INTEGER PRIMARY KEY, code TEXT UNIQUE);""",
            "codes");

        Assert.NotNull(schema);
        var index = Assert.Single(schema.Indexes);
        Assert.Equal("sqlite_autoindex_codes_1", index.Name);
        Assert.True(index.IsUnique);
        Assert.Equal(["code"], index.Columns);
    }

    [Fact]
    public async Task DescribeTable_reports_an_expression_index_with_no_columns()
    {
        // SQLite reports an expression's column with no name. There is no column name to hand back,
        // so the index arrives with none rather than the Provider failing on the absent value.
        var schema = await DescribeAsync(
            """
            CREATE TABLE people (id INTEGER PRIMARY KEY, email TEXT);
            CREATE UNIQUE INDEX idx_people_email_lower ON people(lower(email));
            """,
            "people");

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
        var schema = await DescribeAsync(
            """
            CREATE TABLE metrics (id INTEGER PRIMARY KEY, base INTEGER, doubled INTEGER GENERATED ALWAYS AS (base * 2));
            """,
            "metrics");

        Assert.NotNull(schema);
        Assert.Equal(["id", "base"], schema.Columns.Select(column => column.Name));
    }

    [Fact]
    public async Task DescribeTable_finds_a_table_whose_name_the_caller_cased_differently()
    {
        // SQLite matches identifiers case-insensitively, so a caller that asks for USERS is asking
        // about users; answering "no such table" would be wrong.
        var schema = await DescribeAsync(
            """CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT);""",
            "USERS");

        Assert.NotNull(schema);
        Assert.Equal("users", schema.Name);
        Assert.Equal(["id", "name"], schema.Columns.Select(column => column.Name));
    }

    [Fact]
    public async Task DescribeTable_returns_null_rather_than_the_engines_own_bookkeeping_table()
    {
        // An AUTOINCREMENT table makes SQLite create sqlite_sequence beside the database's own
        // tables. It is not one of this database's tables and may not be described as if it were.
        var schema = await DescribeAsync(
            """CREATE TABLE tickets (id INTEGER PRIMARY KEY AUTOINCREMENT);""",
            "sqlite_sequence");

        Assert.Null(schema);
    }

    /// <summary>
    /// Creates a database holding the given schema, describes one object in it, and answers with
    /// the description.
    /// </summary>
    /// <param name="ddl">The DDL that gives the database its shape.</param>
    /// <param name="tableName">The object to describe, as a caller would spell it.</param>
    /// <returns>The object's structure, or <see langword="null"/> when the schema holds no such object.</returns>
    /// <remarks>
    /// The three steps every test here takes — create, open, describe — live in this one place, so
    /// a test body holds only what makes it different: the schema it needs, the name it asks
    /// about, and what the answer must be.
    /// </remarks>
    private async Task<TableSchema?> DescribeAsync(string ddl, string tableName)
    {
        using var database = await SqliteTestDatabase.CreateAsync(ddl);
        await using var connection = await _provider.CreateReadOnlyConnectionAsync(
            database.ConnectionString,
            CancellationToken.None);

        return await _provider.DescribeTableAsync(connection, tableName, CancellationToken.None);
    }
}
