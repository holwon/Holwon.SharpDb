using DotNet.Testcontainers.Builders;
using Npgsql;
using SharpDb.Abstractions;
using SharpDb.Provider.Contract;
using Testcontainers.PostgreSql;

namespace SharpDb.Provider.PostgreSql.Tests;

/// <summary>
/// A real PostgreSQL server the shared suites run against: a container pinned to PostgreSQL 16,
/// seeded with the shared schema, then handed to the Provider to serve read-only.
/// </summary>
/// <remarks>
/// The image is pinned to <c>postgres:16</c> rather than a floating tag, because the introspection
/// SQL was written and measured against that version and a silent upgrade would change what the
/// shared assertions are asserting. The spec declares support for PostgreSQL 10 and up, which is
/// what <c>attidentity</c> — the catalogue column the auto-increment test reads — requires.
/// <para>
/// One container serves the whole collection, which is what keeps the shared suites' classes
/// asserting against the same server.
/// </para>
/// <para>
/// The fixture seeds the database through its own writable connection, because the Provider's
/// connection is read-only at the engine. The connection string it hands out carries no read-only
/// mode: adding the mode is the Provider's job, and the shared suite asserts that it does.
/// </para>
/// <para>
/// When Docker is absent the fixture reports the engine as unavailable rather than throwing; the
/// shared suites turn that into skips. A Docker-less machine can still run every SQLite test, and
/// the failure of anything else — a broken image, a port problem — surfaces loudly, because only
/// Docker's own absence is an acceptable reason not to test (see <see cref="Available"/>).
/// </para>
/// </remarks>
public sealed class PostgreSqlContractDatabase : IContractDatabase, IAsyncLifetime
{
    /// <summary>
    /// The one account whose databases can be seen and written. The Provider connects as it; the
    /// fixture seeds as it.
    /// </summary>
    private const string Password = "sharpdb-contract-pw";

    private PostgreSqlContainer? _container;

    /// <inheritdoc/>
    public string Type => "postgresql";

    /// <inheritdoc/>
    public IDatabaseProvider Provider { get; } = new PostgreSqlDatabaseProvider();

    /// <inheritdoc/>
    public string ConnectionString { get; private set; } = string.Empty;

    /// <inheritdoc/>
    public string DatabaseName { get; } = "sharpdb_contract";

    /// <inheritdoc/>
    /// <remarks>
    /// A four-way cross join over the catalogue. Measured on PostgreSQL 16.15: it runs for well
    /// over a second, so it outlives the timeout the shared suite allows, and it touches no table
    /// of its own.
    /// </remarks>
    public string SlowQuery => """
        SELECT count(*) FROM pg_attribute AS a
        CROSS JOIN pg_attribute AS b
        CROSS JOIN pg_attribute AS c
        CROSS JOIN pg_attribute AS d
        """;

    /// <inheritdoc/>
    public bool Available { get; private set; }

    /// <inheritdoc/>
    public string SkipReason { get; private set; } = string.Empty;

    /// <inheritdoc/>
    /// <remarks>
    /// The password the container's server was given: a credential rather than a path, and the
    /// other kind of value ADR 0010 keeps out of the messages a caller reads.
    /// </remarks>
    public string SensitiveText => Password;

    /// <summary>
    /// The connection string the server's own administrator account opens with.
    /// </summary>
    /// <value>
    /// Used to create the extra databases the PostgreSQL-specific tests need. The container's
    /// <c>postgres</c> account is a superuser, so it needs no grants to reach them.
    /// </value>
    public string RootConnectionString { get; private set; } = string.Empty;

    /// <summary>
    /// A connection string for a database of this container that no shared-suite assertion looks
    /// at, where tests that need their own tables can create them.
    /// </summary>
    /// <value>
    /// The same server as <see cref="ConnectionString"/>, bound to a second database.
    /// </value>
    /// <remarks>
    /// The shared schema must stay as <see cref="ContractSchema"/> left it, because the shared
    /// suites assert the exact set of tables in it. A test that seeds a table of its own would
    /// break that listing for whichever suite ran after it, so tests with their own schema
    /// material get their own database — still inside this one container, which is what keeps the
    /// container count at one.
    /// </remarks>
    public string SpareConnectionString { get; private set; } = string.Empty;

    /// <summary>
    /// The name of the database <see cref="SpareConnectionString"/> is bound to.
    /// </summary>
    public string SpareDatabaseName { get; } = "sharpdb_spare";

    /// <summary>
    /// A connection string for a database of this container that holds no objects at all.
    /// </summary>
    /// <value>
    /// The same server as <see cref="ConnectionString"/>, bound to an empty database.
    /// </value>
    /// <remarks>
    /// The empty state cannot be produced by deleting: PostgreSQL has no way to empty a database of
    /// the shared schema without dropping the schema itself. A second database, created and never
    /// used, is the state itself.
    /// </remarks>
    public string EmptyConnectionString { get; private set; } = string.Empty;

    /// <summary>
    /// The name of the database <see cref="EmptyConnectionString"/> is bound to.
    /// </summary>
    public string EmptyDatabaseName { get; } = "sharpdb_empty";

    /// <inheritdoc/>
    /// <remarks>
    /// Starting the container is the only step Docker's absence can legitimately stop, so it is
    /// the only one whose failure is caught. Whatever comes after — seeding included — is this
    /// code's own business and must fail rather than skip.
    /// </remarks>
    public async Task InitializeAsync()
    {
        try
        {
            _container = new PostgreSqlBuilder("postgres:16")
                .WithDatabase(DatabaseName)
                .WithUsername("postgres")
                .WithPassword(Password)
                .Build();

            await _container.StartAsync();
        }
        catch (DockerUnavailableException unavailable)
        {
            SkipReason = $"Docker is not available, so PostgreSQL cannot be started: {unavailable.Message}";
            return;
        }

        ConnectionString = _container.GetConnectionString();
        RootConnectionString = BuildRootConnectionString();
        SpareConnectionString = BuildBoundConnectionString(SpareDatabaseName);
        EmptyConnectionString = BuildBoundConnectionString(EmptyDatabaseName);

        await SeedAsync();

        Available = true;
    }

    /// <inheritdoc/>
    public async Task DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    /// <summary>
    /// Builds the administrator's connection string from the container's endpoint.
    /// </summary>
    /// <returns>The administrator connection string.</returns>
    /// <remarks>
    /// PostgreSQL names its administrator <c>postgres</c>, and that is the account the container
    /// was created with, so this differs from the application connection string in nothing but the
    /// database it is bound to.
    /// </remarks>
    private string BuildRootConnectionString() =>
        BuildBoundConnectionString("postgres");

    /// <summary>
    /// Builds a connection string bound to another database on the same server.
    /// </summary>
    /// <param name="databaseName">The database to bind to.</param>
    /// <returns>The connection string.</returns>
    private string BuildBoundConnectionString(string databaseName) =>
        new NpgsqlConnectionStringBuilder(ConnectionString) { Database = databaseName }.ConnectionString;

    /// <summary>
    /// Creates the spare and empty databases and seeds the shared schema.
    /// </summary>
    /// <returns>The work that creates and seeds them.</returns>
    /// <remarks>
    /// The two extra databases are created through the administrator because
    /// <c>CREATE DATABASE</c> cannot run inside a transaction block, and the application account is
    /// the container's superuser anyway — one statement per command keeps that fact explicit.
    /// </remarks>
    private async Task SeedAsync()
    {
        await using (var administrator = new NpgsqlConnection(RootConnectionString))
        {
            await administrator.OpenAsync();

            await using (var spare = administrator.CreateCommand())
            {
                spare.CommandText = $"CREATE DATABASE {SpareDatabaseName}";
                await spare.ExecuteNonQueryAsync();
            }

            await using var empty = administrator.CreateCommand();
            empty.CommandText = $"CREATE DATABASE {EmptyDatabaseName}";
            await empty.ExecuteNonQueryAsync();
        }

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using var seeding = connection.CreateCommand();
        seeding.CommandText = ContractSchema.PostgreSqlDdl;
        await seeding.ExecuteNonQueryAsync();
    }
}
