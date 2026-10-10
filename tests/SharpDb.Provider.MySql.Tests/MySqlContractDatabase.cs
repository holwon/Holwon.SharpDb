using DotNet.Testcontainers.Builders;
using MySqlConnector;
using SharpDb.Abstractions;
using SharpDb.Provider.Contract;
using Testcontainers.MySql;

namespace SharpDb.Provider.MySql.Tests;

/// <summary>
/// A real MySQL server both shared suites run against: a container pinned to MySQL 8.0, seeded
/// with the shared schema, then handed to the Provider to serve read-only.
/// </summary>
/// <remarks>
/// The image is pinned to <c>mysql:8.0</c> rather than a floating tag, because the introspection
/// SQL was written and measured against that version and a silent upgrade would change what the
/// shared assertions are asserting.
/// <para>
/// One container serves the whole collection: measured on this machine, starting it takes about
/// twelve seconds, so a container per test would cost minutes. The container is the collection
/// fixture, which is also what keeps the two shared suites — introspection and query — asserting
/// against the same server.
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
public sealed class MySqlContractDatabase : IContractDatabase, IAsyncLifetime
{
    /// <summary>
    /// The one account whose databases can be seen and written. The Provider connects as it; the
    /// fixture seeds as it.
    /// </summary>
    private const string Password = "sharpdb-contract-pw";

    private MySqlContainer? _container;

    /// <inheritdoc/>
    public string Type => "mysql";

    /// <inheritdoc/>
    public IDatabaseProvider Provider { get; } = new MySqlDatabaseProvider();

    /// <inheritdoc/>
    public string ConnectionString { get; private set; } = string.Empty;

    /// <inheritdoc/>
    public string DatabaseName { get; } = "sharpdb_contract";

    /// <inheritdoc/>
    /// <remarks>
    /// A three-way cross join over the data dictionary. Measured on MySQL 8.0: it runs for well
    /// over a second, so it outlives the timeout the shared suite allows, and it touches no table
    /// of its own.
    /// </remarks>
    public string SlowQuery => """
        SELECT COUNT(*) FROM information_schema.columns AS a
        CROSS JOIN information_schema.columns AS b
        CROSS JOIN information_schema.columns AS c
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
    /// Used to create the extra databases the MySQL-specific tests need; the container sets root's
    /// password to the same one the application account uses.
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
    /// The empty state cannot be produced by deleting: MySQL has no way to empty a database of the
    /// shared schema without dropping the schema itself. A second database, created and never used,
    /// is the state itself.
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
            _container = new MySqlBuilder("mysql:8.0")
                .WithDatabase(DatabaseName)
                .WithUsername("sharpdb")
                .WithPassword(Password)
                .Build();

            await _container.StartAsync();
        }
        catch (DockerUnavailableException unavailable)
        {
            SkipReason = $"Docker is not available, so MySQL cannot be started: {unavailable.Message}";
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
    /// <returns>The root connection string.</returns>
    private string BuildRootConnectionString()
    {
        var endpoint = new MySqlConnectionStringBuilder(ConnectionString)
        {
            UserID = "root",
            Password = Password,
            Database = string.Empty,
        };

        return endpoint.ConnectionString;
    }

    /// <summary>
    /// Builds a connection string bound to another database on the same server.
    /// </summary>
    /// <param name="databaseName">The database to bind to.</param>
    /// <returns>The connection string.</returns>
    private string BuildBoundConnectionString(string databaseName) =>
        new MySqlConnectionStringBuilder(ConnectionString) { Database = databaseName }.ConnectionString;

    /// <summary>
    /// Creates the shared database, the spare one, and the empty one, and seeds the shared schema.
    /// </summary>
    /// <returns>The work that creates and seeds them.</returns>
    /// <remarks>
    /// The spare and empty databases are created through the administrator because the container's
    /// application account holds its privileges on the one database the builder named. The same
    /// account is then granted rights on the spare one, since the specific tests seed it through an
    /// ordinary connection.
    /// </remarks>
    private async Task SeedAsync()
    {
        await using (var administrator = new MySqlConnection(RootConnectionString))
        {
            await administrator.OpenAsync();

            await using var command = administrator.CreateCommand();
            command.CommandText = $"""
                CREATE DATABASE IF NOT EXISTS {SpareDatabaseName};
                CREATE DATABASE IF NOT EXISTS {EmptyDatabaseName};
                GRANT ALL PRIVILEGES ON {SpareDatabaseName}.* TO 'sharpdb'@'%';
                GRANT SELECT ON {EmptyDatabaseName}.* TO 'sharpdb'@'%';
                """;
            await command.ExecuteNonQueryAsync();
        }

        await using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using var seeding = connection.CreateCommand();
        seeding.CommandText = ContractSchema.MySqlDdl;
        await seeding.ExecuteNonQueryAsync();
    }
}
