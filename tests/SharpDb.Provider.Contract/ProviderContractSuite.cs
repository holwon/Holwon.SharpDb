using SharpDb.Abstractions;
using SharpDb.Abstractions.Schema;

namespace SharpDb.Provider.Contract;

/// <summary>
/// The Provider contract, asserted against a real database: what a listing reports, what a
/// description reports, and that the connection a Provider opens is one the engine refuses to
/// write through (ADR 0002, layer 2).
/// </summary>
/// <remarks>
/// This suite is abstract and lives in its own assembly; each engine's test project subclasses it
/// once, handing over its own <see cref="IContractDatabase"/>. The runs are the same assertions,
/// which is what makes the port a contract — ADR 0003 promises that a new database type is a new
/// Provider and nothing else, and the satisfaction of that promise is this suite passing once per
/// engine.
/// <para>
/// What this suite holds is only the <em>common denominator</em>: an assertion stays here when every
/// engine answers it identically, and moves to the engine's own project when even one engine's
/// physical semantics cannot produce it. An assertion kept here that only some engines satisfy
/// would force every other engine to be written to a lie, so the rule is that the shared suite never
/// asks a question a Provider cannot answer truthfully.
/// </para>
/// <para>
/// Only what the Provider answers is asserted — which objects are in this database, how a table is
/// shaped — never the introspection SQL it happens to generate, which stays free to change.
/// </para>
/// <para>
/// Every test begins by skipping when the engine is not reachable, so a machine without Docker
/// reports MySQL as skipped rather than failed (see <see cref="IContractDatabase.Available"/>).
/// </para>
/// </remarks>
/// <param name="database">The engine under contract, seeded and ready.</param>
public abstract class ProviderContractSuite(IContractDatabase database)
{
    /// <summary>
    /// Every object the shared schema creates, in the order a listing reports them.
    /// </summary>
    private static readonly string[] SharedTableNames =
    [
        "active_users",
        "bookings",
        "composite",
        "memberships",
        "orgs",
        "readings",
        "users",
    ];

    /// <summary>
    /// The engine under contract, handed in by the concrete run.
    /// </summary>
    /// <value>
    /// The fixture whose database every assertion in this suite reads through the Provider.
    /// </value>
    protected IContractDatabase Database { get; } = database;

    /// <summary>
    /// Opens the connection a test will read through.
    /// </summary>
    /// <returns>The open, read-only connection, which the test disposes.</returns>
    protected async Task<System.Data.Common.DbConnection> OpenAsync() =>
        await Database.Provider.CreateReadOnlyConnectionAsync(Database.ConnectionString, CancellationToken.None);

    /// <summary>
    /// The identifier matches the one configuration selects the Provider by.
    /// </summary>
    [SkippableFact]
    public void Type_is_the_name_the_engine_is_selected_by()
    {
        Database.SkipWhenUnavailable();

        Assert.Equal(Database.Type, Database.Provider.Type);
    }

    /// <summary>
    /// The connection arrives open, with no implied open left to the caller.
    /// </summary>
    [SkippableFact]
    public async Task The_connection_a_provider_opens_is_already_open()
    {
        Database.SkipWhenUnavailable();

        await using var connection = await OpenAsync();

        Assert.Equal(System.Data.ConnectionState.Open, connection.State);
    }

    /// <summary>
    /// The connection is in the engine's read-only mode, so a write through it is refused.
    /// </summary>
    /// <remarks>
    /// Read-only is asserted by trying to write rather than by inspecting the connection string:
    /// the string is how the mode is asked for, while a refused write is the mode actually being in
    /// force. The refusal must be the engine's — every Driver reports it by throwing its own
    /// exception, all of which derive from <see cref="System.Data.Common.DbException"/>, and this
    /// is the tightest type the engines share.
    /// </remarks>
    [SkippableFact]
    public async Task The_connection_a_provider_opens_refuses_a_write()
    {
        Database.SkipWhenUnavailable();

        await using var connection = await OpenAsync();
        await using var write = connection.CreateCommand();
        write.CommandText = "INSERT INTO orgs (name) VALUES ('a write the engine must refuse')";

        await Assert.ThrowsAnyAsync<System.Data.Common.DbException>(() => write.ExecuteNonQueryAsync());
    }

    /// <summary>
    /// The listing reports every table and view, with its kind named.
    /// </summary>
    [SkippableFact]
    public async Task ListTables_reports_every_table_and_view_ordered_by_name()
    {
        Database.SkipWhenUnavailable();

        await using var connection = await OpenAsync();
        var listed = await Database.Provider.ListTablesAsync(connection, CancellationToken.None);

        Assert.Equal(SharedTableNames, listed.Select(table => table.Name));
        Assert.Equal(TableType.View, listed.Single(table => table.Name == "active_users").Type);
        Assert.All(
            listed.Where(table => table.Name != "active_users"),
            table => Assert.Equal(TableType.Table, table.Type));
    }

    /// <summary>
    /// The seeded database keeps no comments, so every listed object reports none.
    /// </summary>
    /// <remarks>
    /// The shared schema keeps comments out deliberately: MySQL reports a table's comment where
    /// SQLite has none to report, and the divergence is asserted in the engine's own project.
    /// </remarks>
    [SkippableFact]
    public async Task ListTables_reports_no_comment_because_the_shared_schema_keeps_none()
    {
        Database.SkipWhenUnavailable();

        await using var connection = await OpenAsync();
        var listed = await Database.Provider.ListTablesAsync(connection, CancellationToken.None);

        Assert.All(listed, table => Assert.Null(table.Comment));
    }

    /// <summary>
    /// The listing reports the database the connection is bound to.
    /// </summary>
    /// <remarks>
    /// "Contains" rather than "equals": MySQL answers this one from the server's own catalogue and
    /// truthfully reports schemas the server holds rather than the one the process is bound to,
    /// which is exactly what its Tool description promises.
    /// </remarks>
    [SkippableFact]
    public async Task ListDatabases_reports_the_bound_database()
    {
        Database.SkipWhenUnavailable();

        await using var connection = await OpenAsync();
        var databases = await Database.Provider.ListDatabasesAsync(connection, CancellationToken.None);

        Assert.Contains(Database.DatabaseName, databases);
    }

    /// <summary>
    /// The database listing arrives ordered and without repeats.
    /// </summary>
    /// <remarks>
    /// The comparison is ordinal, which is the same answer every engine gives only because every
    /// name that can appear here is lower-case — the fixture's own databases and the server's own
    /// schemas alike. SQLite orders names under its case-sensitive <c>BINARY</c> collation while
    /// MySQL's default collation is case-insensitive, so the two genuinely disagree about a
    /// mixed-case name; that divergence is kept out of this assertion the same way the shared
    /// schema keeps its others out, by not producing the state that would expose it.
    /// </remarks>
    [SkippableFact]
    public async Task ListDatabases_is_ordered_and_free_of_duplicates()
    {
        Database.SkipWhenUnavailable();

        await using var connection = await OpenAsync();
        var databases = await Database.Provider.ListDatabasesAsync(connection, CancellationToken.None);

        Assert.Equal(databases.OrderBy(name => name, StringComparer.Ordinal), databases);
        Assert.Equal(databases.Distinct().Count(), databases.Count);
    }

    /// <summary>
    /// A description reports the columns in declaration order, with their ordinal and nullability.
    /// </summary>
    /// <remarks>
    /// The shared schema's <c>users</c> is written so that every engine answers identically: a
    /// single-column auto-increment key, a nullable foreign key column, a nullable column and a
    /// required one — and no defaults, which is where the engines diverge.
    /// </remarks>
    [SkippableFact]
    public async Task DescribeTable_reports_columns_in_declaration_order()
    {
        Database.SkipWhenUnavailable();

        await using var connection = await OpenAsync();
        var schema = await Database.Provider.DescribeTableAsync(connection, "users", CancellationToken.None);

        Assert.NotNull(schema);
        Assert.Equal(
            [
                ("id", 1, false, true),
                ("org_id", 2, true, false),
                ("email", 3, true, false),
                ("role", 4, false, false),
            ],
            schema.Columns.Select(column => (column.Name, column.Ordinal, column.IsNullable, column.IsAutoIncrement)));
    }

    /// <summary>
    /// A description reports the primary key.
    /// </summary>
    [SkippableFact]
    public async Task DescribeTable_reports_the_primary_key()
    {
        Database.SkipWhenUnavailable();

        await using var connection = await OpenAsync();
        var schema = await Database.Provider.DescribeTableAsync(connection, "users", CancellationToken.None);

        Assert.NotNull(schema);
        Assert.Equal(["id"], schema.PrimaryKey);
    }

    /// <summary>
    /// A composite primary key is reported in key order rather than declaration order.
    /// </summary>
    /// <remarks>
    /// The key of the shared <c>composite</c> table is declared <c>(b, a)</c> while the columns are
    /// declared the other way round, so a Provider that reported the key in column order — or in a
    /// set — would answer <c>a, b</c> and fail here rather than pass both ways.
    /// </remarks>
    [SkippableFact]
    public async Task DescribeTable_reports_a_composite_key_in_key_order()
    {
        Database.SkipWhenUnavailable();

        await using var connection = await OpenAsync();
        var schema = await Database.Provider.DescribeTableAsync(connection, "composite", CancellationToken.None);

        Assert.NotNull(schema);
        Assert.Equal(["b", "a"], schema.PrimaryKey);
    }

    /// <summary>
    /// A description of a table whose key is composite reports no auto-increment column.
    /// </summary>
    /// <remarks>
    /// A composite key's values are not generated, whatever the first column's type; this is the
    /// judgement SQLite's Provider computes and MySQL's reads from the server's own flag.
    /// </remarks>
    [SkippableFact]
    public async Task DescribeTable_reports_no_autoincrement_on_a_composite_key()
    {
        Database.SkipWhenUnavailable();

        await using var connection = await OpenAsync();
        var schema = await Database.Provider.DescribeTableAsync(connection, "composite", CancellationToken.None);

        Assert.NotNull(schema);
        Assert.All(schema.Columns, column => Assert.False(column.IsAutoIncrement));
    }

    /// <summary>
    /// A description reports a foreign key's columns, its target and its delete rule.
    /// </summary>
    /// <remarks>
    /// The update rule is deliberately not asserted here. Oracle has no <c>ON UPDATE</c> referential
    /// action at all — measured, declaring one is <c>ORA-00905</c>, and its catalogue has no column
    /// for one — so an assertion naming it would be one no Oracle Provider could satisfy. The
    /// engines that do have it assert it in their own projects, which is where the shared schema's
    /// rule sends what not every engine can express.
    /// </remarks>
    [SkippableFact]
    public async Task DescribeTable_reports_a_foreign_key_with_its_delete_rule()
    {
        Database.SkipWhenUnavailable();

        await using var connection = await OpenAsync();
        var schema = await Database.Provider.DescribeTableAsync(connection, "users", CancellationToken.None);

        Assert.NotNull(schema);
        var foreignKey = Assert.Single(schema.ForeignKeys);
        Assert.Equal(["org_id"], foreignKey.Columns);
        Assert.Equal("orgs", foreignKey.ReferencedTable);
        Assert.Equal(["id"], foreignKey.ReferencedColumns);
        Assert.Equal(ForeignKeyAction.Cascade, foreignKey.OnDelete);
        Assert.False(string.IsNullOrWhiteSpace(foreignKey.Name));
    }

    /// <summary>
    /// A composite foreign key pairs its columns and its referenced columns in key order.
    /// </summary>
    /// <remarks>
    /// The two lists are positional: the second local column references the second referenced
    /// column. A Provider that grouped them by name, or that lost the order, would pair
    /// <c>composite_b</c> with the wrong parent column and still look plausible.
    /// </remarks>
    [SkippableFact]
    public async Task DescribeTable_reports_a_composite_foreign_key_in_key_order()
    {
        Database.SkipWhenUnavailable();

        await using var connection = await OpenAsync();
        var schema = await Database.Provider.DescribeTableAsync(connection, "bookings", CancellationToken.None);

        Assert.NotNull(schema);
        var foreignKey = Assert.Single(schema.ForeignKeys);
        Assert.Equal(["composite_b", "composite_a"], foreignKey.Columns);
        Assert.Equal("composite", foreignKey.ReferencedTable);
        Assert.Equal(["b", "a"], foreignKey.ReferencedColumns);
        Assert.Equal(ForeignKeyAction.Cascade, foreignKey.OnDelete);
        Assert.Equal(ForeignKeyAction.NoAction, foreignKey.OnUpdate);
    }

    /// <summary>
    /// A table with no foreign key reports none.
    /// </summary>
    [SkippableFact]
    public async Task DescribeTable_reports_no_foreign_keys_when_there_are_none()
    {
        Database.SkipWhenUnavailable();

        await using var connection = await OpenAsync();
        var schema = await Database.Provider.DescribeTableAsync(connection, "orgs", CancellationToken.None);

        Assert.NotNull(schema);
        Assert.Empty(schema.ForeignKeys);
    }

    /// <summary>
    /// The index listing names each index, its columns and whether it is unique.
    /// </summary>
    /// <remarks>
    /// The order the engines report indexes in is alphabetical — SQLite sorts by name itself,
    /// MySQL's <c>STATISTICS</c> is ordered by name here too — and the shared schema relies on
    /// that, so the assertion may name the expected sequence rather than a set.
    /// </remarks>
    [SkippableFact]
    public async Task DescribeTable_lists_indexes_with_their_columns_and_uniqueness()
    {
        Database.SkipWhenUnavailable();

        await using var connection = await OpenAsync();
        var schema = await Database.Provider.DescribeTableAsync(connection, "users", CancellationToken.None);

        Assert.NotNull(schema);
        Assert.Equal(
            [
                ("idx_users_email", true, new[] { "email" }),
                ("idx_users_org_role", false, new[] { "org_id", "role" }),
            ],
            schema.Indexes.Select(index => (index.Name, index.IsUnique, index.Columns.ToArray())));
    }

    /// <summary>
    /// The index backing the primary key is not listed among the indexes.
    /// </summary>
    /// <remarks>
    /// The primary key is reported as its own member; listing its index as well would present one
    /// fact as two. MySQL names that index <c>PRIMARY</c>, SQLite gives it an origin of <c>pk</c>,
    /// and both must disappear from this list.
    /// </remarks>
    [SkippableFact]
    public async Task DescribeTable_does_not_list_the_primary_keys_index()
    {
        Database.SkipWhenUnavailable();

        await using var connection = await OpenAsync();
        var schema = await Database.Provider.DescribeTableAsync(connection, "bookings", CancellationToken.None);

        Assert.NotNull(schema);
        Assert.Equal(["id"], schema.PrimaryKey);
        Assert.Equal(["idx_bookings_parent"], schema.Indexes.Select(index => index.Name));
    }

    /// <summary>
    /// A description of a view reports its columns and no key, foreign keys or indexes.
    /// </summary>
    [SkippableFact]
    public async Task DescribeTable_describes_a_view()
    {
        Database.SkipWhenUnavailable();

        await using var connection = await OpenAsync();
        var schema = await Database.Provider.DescribeTableAsync(connection, "active_users", CancellationToken.None);

        Assert.NotNull(schema);
        Assert.Equal(TableType.View, schema.Type);
        Assert.Equal("active_users", schema.Name);
        Assert.Equal(["id", "email"], schema.Columns.Select(column => column.Name));
        Assert.Empty(schema.PrimaryKey);
        Assert.Empty(schema.ForeignKeys);
        Assert.Empty(schema.Indexes);
    }

    /// <summary>
    /// A name that matches nothing is answered with nothing.
    /// </summary>
    /// <remarks>
    /// Not finding a name a caller guessed is an answer rather than a fault, and the Tool is what
    /// turns it into a message; the Provider's part of that contract is the null.
    /// </remarks>
    [SkippableFact]
    public async Task DescribeTable_answers_null_for_a_table_that_does_not_exist()
    {
        Database.SkipWhenUnavailable();

        await using var connection = await OpenAsync();
        var schema = await Database.Provider.DescribeTableAsync(connection, "no_such_table", CancellationToken.None);

        Assert.Null(schema);
    }

    /// <summary>
    /// A description rejects a missing connection rather than failing later on it.
    /// </summary>
    [SkippableFact]
    public async Task DescribeTable_rejects_a_missing_connection()
    {
        Database.SkipWhenUnavailable();

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            Database.Provider.DescribeTableAsync(null!, "users", CancellationToken.None));
    }

    /// <summary>
    /// A description rejects a missing table name rather than failing later on it.
    /// </summary>
    [SkippableFact]
    public async Task DescribeTable_rejects_a_missing_name()
    {
        Database.SkipWhenUnavailable();

        await using var connection = await OpenAsync();

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            Database.Provider.DescribeTableAsync(connection, null!, CancellationToken.None));
    }

    /// <summary>
    /// A listing rejects a missing connection rather than failing later on it.
    /// </summary>
    [SkippableFact]
    public async Task ListTables_rejects_a_missing_connection()
    {
        Database.SkipWhenUnavailable();

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            Database.Provider.ListTablesAsync(null!, CancellationToken.None));
    }

    /// <summary>
    /// A database listing rejects a missing connection rather than failing later on it.
    /// </summary>
    [SkippableFact]
    public async Task ListDatabases_rejects_a_missing_connection()
    {
        Database.SkipWhenUnavailable();

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            Database.Provider.ListDatabasesAsync(null!, CancellationToken.None));
    }
}
