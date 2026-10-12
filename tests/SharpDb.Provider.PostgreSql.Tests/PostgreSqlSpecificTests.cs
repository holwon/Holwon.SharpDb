using SharpDb.Abstractions;
using SharpDb.Abstractions.Schema;
using SharpDb.Provider.Contract;

namespace SharpDb.Provider.PostgreSql.Tests;

/// <summary>
/// What PostgreSQL does that the shared contract cannot assert, because no other engine does it
/// the same way.
/// </summary>
/// <remarks>
/// These are not extras. The shared suites can only hold what every engine answers identically, so
/// a behaviour that is this engine's own has to be pinned somewhere or it is pinned nowhere — and
/// the two below are the ones measured to differ. What is asserted here is still the Provider's
/// answer, never the SQL it ran.
/// </remarks>
/// <param name="database">The seeded container the collection shares.</param>
[Collection(PostgreSqlContractCollection.Name)]
public sealed class PostgreSqlSpecificTests(PostgreSqlContractDatabase database)
{
    private readonly PostgreSqlContractDatabase _database = database;

    /// <summary>
    /// The legacy <c>serial</c> spelling of auto-increment is reported as auto-increment.
    /// </summary>
    /// <remarks>
    /// <c>serial</c> is not a type: it is a macro that creates a sequence and sets the column's
    /// default to <c>nextval(…)</c>, leaving no <c>attidentity</c> flag behind. A Provider that
    /// read only the flag would report a generated key as one the caller must supply — so the
    /// shared schema, which uses the newer <c>IDENTITY</c> spelling, would not catch it, and this
    /// is where it is caught.
    /// </remarks>
    [SkippableFact]
    public async Task DescribeTable_reports_a_serial_column_as_autoincrement()
    {
        database.SkipWhenUnavailable();
        await SeedAsync();

        await using var connection = await OpenSpareAsync();
        var schema = await _database.Provider.DescribeTableAsync(connection, "serial_orgs", CancellationToken.None);

        Assert.NotNull(schema);
        var id = schema.Columns.Single(column => column.Name == "id");

        Assert.True(id.IsAutoIncrement);
        Assert.Contains("nextval(", id.DefaultValue);
    }

    /// <summary>
    /// A wider <c>serial</c> spelling is reported the same way.
    /// </summary>
    /// <remarks>
    /// <c>BIGSERIAL</c> is the same macro over a different integer width, so it has to be
    /// recognised by the same evidence rather than by the column's declared type.
    /// </remarks>
    [SkippableFact]
    public async Task DescribeTable_reports_a_bigserial_column_as_autoincrement()
    {
        database.SkipWhenUnavailable();
        await SeedAsync();

        await using var connection = await OpenSpareAsync();
        var schema = await _database.Provider.DescribeTableAsync(connection, "serial_members", CancellationToken.None);

        Assert.NotNull(schema);
        var id = schema.Columns.Single(column => column.Name == "id");

        Assert.True(id.IsAutoIncrement);
        Assert.Equal("bigint", id.DataType);
    }

    /// <summary>
    /// A view's columns report neither the base table's nullability nor its defaults.
    /// </summary>
    /// <remarks>
    /// Measured on PostgreSQL 16.15, and the reason this cannot live in the shared suite: MySQL
    /// <em>does</em> propagate a base table's constraints into a view's columns, so the two
    /// engines genuinely disagree about a question the Schema model asks. A consumer that needs an
    /// engine-independent answer about a view's columns cannot have one, and this is the assertion
    /// that says so rather than letting the difference be discovered.
    /// </remarks>
    [SkippableFact]
    public async Task DescribeTable_reports_a_views_columns_without_the_base_tables_constraints()
    {
        database.SkipWhenUnavailable();
        await SeedAsync();

        await using var connection = await OpenSpareAsync();
        var schema = await _database.Provider.DescribeTableAsync(connection, "serial_view", CancellationToken.None);

        Assert.NotNull(schema);
        Assert.Equal(TableType.View, schema.Type);

        // The base column is NOT NULL with a default; the view's column reports neither.
        var required = schema.Columns.Single(column => column.Name == "required_not_null");
        Assert.True(required.IsNullable);
        Assert.Null(required.DefaultValue);

        var defaulted = schema.Columns.Single(column => column.Name == "nullable_but_defaulted");
        Assert.True(defaulted.IsNullable);
        Assert.Null(defaulted.DefaultValue);
    }

    /// <summary>
    /// A base table's own columns do report their nullability and defaults.
    /// </summary>
    /// <remarks>
    /// The control for the assertion above: the values are absent for the view, so this pins that
    /// they are present for the table rather than absent everywhere.
    /// </remarks>
    [SkippableFact]
    public async Task DescribeTable_reports_a_base_tables_own_nullability_and_defaults()
    {
        database.SkipWhenUnavailable();
        await SeedAsync();

        await using var connection = await OpenSpareAsync();
        var schema = await _database.Provider.DescribeTableAsync(connection, "labelled", CancellationToken.None);

        Assert.NotNull(schema);

        var required = schema.Columns.Single(column => column.Name == "required_not_null");
        Assert.False(required.IsNullable);
        Assert.Equal("'a default'::character varying", required.DefaultValue);

        var defaulted = schema.Columns.Single(column => column.Name == "nullable_but_defaulted");
        Assert.True(defaulted.IsNullable);
        Assert.Equal("'another default'::character varying", defaulted.DefaultValue);
    }

    /// <summary>
    /// The declared type keeps the length, precision and time-zone a caller has to predict values with.
    /// </summary>
    /// <remarks>
    /// The Schema model asks for the raw declaration rather than a base type name, and PostgreSQL
    /// hands it over whole through <c>format_type</c>. Asserted here because the shared schema
    /// keeps its columns simple enough that the difference would not show.
    /// </remarks>
    [SkippableFact]
    public async Task DescribeTable_reports_the_declared_type_whole()
    {
        database.SkipWhenUnavailable();

        await using var seed = new Npgsql.NpgsqlConnection(_database.SpareConnectionString);
        await seed.OpenAsync();

        await using (var create = seed.CreateCommand())
        {
            create.CommandText = """
                DROP TABLE IF EXISTS typed;
                CREATE TABLE typed (
                    id INT GENERATED BY DEFAULT AS IDENTITY,
                    name CHARACTER VARYING(255) NOT NULL,
                    amount NUMERIC(10,2),
                    moment TIMESTAMP WITH TIME ZONE,
                    PRIMARY KEY (id)
                );
                """;
            await create.ExecuteNonQueryAsync();
        }

        await using var connection = await OpenSpareAsync();
        var schema = await _database.Provider.DescribeTableAsync(connection, "typed", CancellationToken.None);

        Assert.NotNull(schema);
        Assert.Equal(
            ["integer", "character varying(255)", "numeric(10,2)", "timestamp with time zone"],
            schema.Columns.Select(column => column.DataType));
    }

    /// <summary>
    /// A name spelled with different capitalisation still reaches the object.
    /// </summary>
    /// <remarks>
    /// PostgreSQL folds an unquoted identifier to lower case, which is the opposite direction from
    /// MySQL on a case-sensitive server, so the Provider has to fold the name it is asked about.
    /// The shared suite cannot assert this: SQLite folds case either way and MySQL's default
    /// collation is case-insensitive, so all three would pass a weaker assertion for different
    /// reasons.
    /// </remarks>
    [SkippableFact]
    public async Task DescribeTable_reaches_an_object_through_any_capitalisation()
    {
        database.SkipWhenUnavailable();
        await SeedAsync();

        await using var connection = await OpenSpareAsync();

        var schema = await _database.Provider.DescribeTableAsync(connection, "SERIAL_ORGS", CancellationToken.None);

        Assert.NotNull(schema);
        Assert.Equal("serial_orgs", schema.Name);
    }

    /// <summary>
    /// A quoted, mixed-case name is reported as the catalogue spells it.
    /// </summary>
    /// <remarks>
    /// A quoted identifier is not folded, so a mixed-case name exists only as written. The
    /// Provider reports the name the catalogue holds rather than the one asked for, which is what
    /// keeps the rest of a description's lookups pointing at the right relation.
    /// </remarks>
    [SkippableFact]
    public async Task DescribeTable_reports_the_name_the_catalogue_spells()
    {
        database.SkipWhenUnavailable();

        await using var seed = new Npgsql.NpgsqlConnection(_database.SpareConnectionString);
        await seed.OpenAsync();

        await using (var create = seed.CreateCommand())
        {
            create.CommandText = """
                DROP TABLE IF EXISTS "MixedCase";
                CREATE TABLE "MixedCase" (id INT PRIMARY KEY, label TEXT);
                """;
            await create.ExecuteNonQueryAsync();
        }

        await using var connection = await OpenSpareAsync();
        var schema = await _database.Provider.DescribeTableAsync(connection, "MixedCase", CancellationToken.None);

        Assert.NotNull(schema);
        Assert.Equal("MixedCase", schema.Name);
    }

    /// <summary>
    /// The system schemas' objects never appear in a listing.
    /// </summary>
    /// <remarks>
    /// This is the one listing assertion that matters most on this engine and cannot be shared:
    /// measured on PostgreSQL 16.15, the search-path visibility test alone admits 140 objects
    /// belonging to <c>pg_catalog</c> and <c>information_schema</c>. The shared suite asserts the
    /// exact set of tables in its database, which would fail if they leaked in — but only on a
    /// machine where the container ran, so the reason is pinned here in words as well as there in
    /// a count.
    /// </remarks>
    [SkippableFact]
    public async Task ListTables_leaves_the_system_schemas_out()
    {
        database.SkipWhenUnavailable();

        await using var connection = await OpenSpareAsync();
        var listed = await _database.Provider.ListTablesAsync(connection, CancellationToken.None);

        Assert.DoesNotContain(listed, table => table.Name.StartsWith("pg_", StringComparison.Ordinal));
        Assert.DoesNotContain(listed, table => table.Name is "tables" or "columns" or "views");
    }

    /// <summary>
    /// A database listing leaves the template databases out.
    /// </summary>
    /// <remarks>
    /// <c>template0</c> and <c>template1</c> are visible to every account and are not places a
    /// caller can mean, so a listing that reported them would add two names to every answer that
    /// no caller can act on.
    /// </remarks>
    [SkippableFact]
    public async Task ListDatabases_leaves_the_template_databases_out()
    {
        database.SkipWhenUnavailable();

        await using var connection = await OpenSpareAsync();
        var databases = await _database.Provider.ListDatabasesAsync(connection, CancellationToken.None);

        Assert.DoesNotContain("template0", databases);
        Assert.DoesNotContain("template1", databases);
        Assert.Contains(_database.DatabaseName, databases);
    }

    /// <summary>
    /// A table's and a column's comments reach the client, and an uncommented one reports none.
    /// </summary>
    /// <remarks>
    /// Comments are the one part of the Schema model that PostgreSQL keeps in a place of its own —
    /// <c>pg_description</c>, read back through <c>obj_description</c> and <c>col_description</c> —
    /// so they are asserted here rather than in the shared suite, which keeps its schema free of
    /// them because MySQL reports a view's comment as the literal word <c>VIEW</c> and SQLite
    /// keeps none at all.
    /// </remarks>
    [SkippableFact]
    public async Task DescribeTable_reports_table_and_column_comments()
    {
        database.SkipWhenUnavailable();
        await SeedAsync();

        await using var connection = await OpenSpareAsync();
        var schema = await _database.Provider.DescribeTableAsync(connection, "commented", CancellationToken.None);

        Assert.NotNull(schema);
        Assert.Equal("A table the catalogue describes", schema.Comment);
        Assert.Equal(
            "A column with a comment",
            schema.Columns.Single(column => column.Name == "documented").Comment);
        Assert.Null(schema.Columns.Single(column => column.Name == "undocumented").Comment);
    }

    /// <summary>
    /// A view's comment reaches the client.
    /// </summary>
    /// <remarks>
    /// A view's comment is stored the same way a table's is on this engine, unlike MySQL where a
    /// view has no comment clause at all and the server answers with the word <c>VIEW</c>.
    /// </remarks>
    [SkippableFact]
    public async Task DescribeTable_reports_a_views_comment()
    {
        database.SkipWhenUnavailable();
        await SeedAsync();

        await using var connection = await OpenSpareAsync();
        var schema = await _database.Provider.DescribeTableAsync(connection, "commented_view", CancellationToken.None);

        Assert.NotNull(schema);
        Assert.Equal(TableType.View, schema.Type);
        Assert.Equal("A view the catalogue describes", schema.Comment);
    }

    /// <summary>
    /// The listing reports a table's comment as well as a description does.
    /// </summary>
    [SkippableFact]
    public async Task ListTables_reports_a_tables_comment()
    {
        database.SkipWhenUnavailable();
        await SeedAsync();

        await using var connection = await OpenSpareAsync();
        var listed = await _database.Provider.ListTablesAsync(connection, CancellationToken.None);
        var commented = listed.Single(table => table.Name == "commented");

        Assert.Equal("A table the catalogue describes", commented.Comment);
        Assert.Null(listed.Single(table => table.Name == "indexed").Comment);
    }

    /// <summary>
    /// An index of nothing but expressions is reported with no columns.
    /// </summary>
    /// <remarks>
    /// An indexed expression has no name, which the catalogue records as an attribute number of
    /// zero that matches no column. The Provider drops it, so the index arrives with an empty list
    /// — the same answer MySQL and SQLite give, and the reason this stays out of the shared suite:
    /// each engine reaches it differently and the shared schema declares no expression index.
    /// </remarks>
    [SkippableFact]
    public async Task DescribeTable_reports_an_expression_index_with_no_columns()
    {
        database.SkipWhenUnavailable();
        await SeedAsync();

        await using var connection = await OpenSpareAsync();
        var schema = await _database.Provider.DescribeTableAsync(connection, "indexed", CancellationToken.None);

        Assert.NotNull(schema);
        var expressionIndex = schema.Indexes.Single(index => index.Name == "idx_indexed_lower_role");

        Assert.Empty(expressionIndex.Columns);
        Assert.False(expressionIndex.IsUnique);
    }

    /// <summary>
    /// An index whose columns mix expressions with real columns keeps the columns it has.
    /// </summary>
    /// <remarks>
    /// The expression is dropped and the column is kept, in position, so the list holds what a
    /// caller can act on rather than a gap where the expression was.
    /// </remarks>
    [SkippableFact]
    public async Task DescribeTable_keeps_the_named_columns_of_a_mixed_index()
    {
        database.SkipWhenUnavailable();
        await SeedAsync();

        await using var connection = await OpenSpareAsync();
        var schema = await _database.Provider.DescribeTableAsync(connection, "indexed", CancellationToken.None);

        Assert.NotNull(schema);
        var mixed = schema.Indexes.Single(index => index.Name == "idx_indexed_mixed");

        Assert.Equal(["org_id"], mixed.Columns);
    }

    /// <summary>
    /// Every index is reported with its columns and its uniqueness, and the primary key's is not.
    /// </summary>
    /// <remarks>
    /// The shared suite asserts the same shape against the shared schema; this asserts it against
    /// a table whose keys and indexes are named the way PostgreSQL spells them, and pins that the
    /// index behind <c>indexed_pkey</c> is absent — PostgreSQL names that index after the table
    /// rather than after a convention, so a Provider that filtered by name would list it.
    /// </remarks>
    [SkippableFact]
    public async Task DescribeTable_lists_indexes_without_the_one_backing_the_primary_key()
    {
        database.SkipWhenUnavailable();
        await SeedAsync();

        await using var connection = await OpenSpareAsync();
        var schema = await _database.Provider.DescribeTableAsync(connection, "indexed", CancellationToken.None);

        Assert.NotNull(schema);
        Assert.Equal(["id"], schema.PrimaryKey);
        Assert.Equal(
            [
                ("idx_indexed_lower_role", false, Array.Empty<string>()),
                ("idx_indexed_mixed", false, new[] { "org_id" }),
                ("idx_indexed_role", false, new[] { "role" }),
                ("idx_indexed_role_unique", true, new[] { "role", "org_id" }),
            ],
            schema.Indexes.Select(index => (index.Name, index.IsUnique, index.Columns.ToArray())));
    }

    /// <summary>
    /// Creates this project's engine-specific tables once.
    /// </summary>
    /// <returns>A task that completes when the tables exist.</returns>
    private async Task SeedAsync() =>
        await new PostgreSqlSchemaFixture(_database).CreateAsync();

    /// <summary>
    /// Opens a read-only connection to the fixture's spare database.
    /// </summary>
    /// <returns>The open connection, which the test disposes.</returns>
    private async Task<System.Data.Common.DbConnection> OpenSpareAsync() =>
        await _database.Provider.CreateReadOnlyConnectionAsync(
            _database.SpareConnectionString,
            CancellationToken.None);
}
