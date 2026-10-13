using DotNet.Testcontainers.Builders;
using Microsoft.Data.SqlClient;
using SharpDb.Abstractions;
using SharpDb.Provider.Contract;
using SharpDb.Provider.MsSql;
using Testcontainers.MsSql;

namespace SharpDb.Provider.MsSql.Tests;

/// <summary>
/// A real SQL Server the shared suites run against: a container pinned to a SQL Server 2022 image
/// by digest, seeded with the shared schema by an administrator, then handed to the Provider as an
/// account that may only read.
/// </summary>
/// <remarks>
/// The image is pinned by <c>@sha256:</c> digest rather than by tag, and that is stricter than the
/// other engines' pins on purpose. <c>2022-latest</c> is a floating tag — measured, it moved to
/// CU27 (16.0.4295.3) while this feature was being written — and a silent upgrade would change
/// what the shared assertions are asserting. A digest cannot move.
/// <para>
/// One container serves the whole collection, which is what keeps every suite asserting against
/// the same server. It is also the heaviest container in this repository: measured, it needs about
/// 2 GB of memory and takes noticeably longer to accept connections than MySQL or PostgreSQL, so
/// the read loop that waits for it is generous rather than hopeful.
/// </para>
/// <para>
/// Two accounts, and the split is the point. The administrator seeds; the Provider is handed
/// <c>sharpdb_ro</c>, which holds <c>SELECT</c> and nothing else. That is this engine's read-only
/// boundary, because it has no read-only mode of its own to open (ADR 0016) — the write-refusal
/// assertion in this project's own tests is a statement about what the account may do, not a
/// demand on any user.
/// </para>
/// <para>
/// When Docker is absent the fixture reports the engine as unavailable rather than throwing; the
/// suites turn that into skips. Anything else — a broken image, a failed seed — surfaces loudly,
/// because only Docker's own absence is an acceptable reason not to test.
/// </para>
/// </remarks>
public sealed class MsSqlContractDatabase : IContractDatabase, IAsyncLifetime
{
    /// <summary>
    /// The image, pinned by digest so a re-tag cannot change what is being tested.
    /// </summary>
    /// <value>
    /// SQL Server 2022 CU27 (16.0.4295.3), Developer edition, the measurement target for every
    /// catalogue query in <see cref="MsSqlDatabaseProvider"/>.
    /// </value>
    private const string Image =
        "mcr.microsoft.com/mssql/server@sha256:4402d880dd4c34bfa7d8705e56a86cd6c88da80a1f6bbbe741f999e76264a090";

    /// <summary>
    /// The administrator password. SQL Server refuses a weak one at startup, so this is not a
    /// placeholder: it has to satisfy the server's own complexity policy.
    /// </summary>
    private const string AdminPassword = "SharpDb!Contract2026";

    /// <summary>
    /// The password of the account the Provider connects as.
    /// </summary>
    private const string ReadOnlyPassword = "SharpDb!Reader2026";

    /// <summary>
    /// The account the Provider connects as.
    /// </summary>
    private const string ReadOnlyLogin = "sharpdb_ro";

    private MsSqlContainer? _container;

    /// <summary>
    /// The connection string the container reported, kept so the bound strings are derived from it
    /// rather than from one another.
    /// </summary>
    private string _reportedConnectionString = string.Empty;

    /// <inheritdoc/>
    public string Type => "mssql";

    /// <inheritdoc/>
    public IDatabaseProvider Provider { get; } = new MsSqlDatabaseProvider();

    /// <inheritdoc/>
    /// <remarks>
    /// The string the Provider is handed, which carries no read-only intent of its own: adding it
    /// is the Provider's job, and this project's tests assert that it does.
    /// </remarks>
    public string ConnectionString { get; private set; } = string.Empty;

    /// <inheritdoc/>
    public string DatabaseName { get; } = "sharpdb_contract";

    /// <inheritdoc/>
    /// <remarks>
    /// A three-way cross join over a catalogue view, measured on SQL Server 2022 CU27 to run for
    /// several seconds — well past the one second the shared suite allows. <c>COUNT_BIG</c> rather
    /// than <c>COUNT</c>: measured, the plain aggregating form overflows <c>int</c> on this shape
    /// and fails for a reason that has nothing to do with time.
    /// </remarks>
    public string SlowQuery => """
        SELECT COUNT_BIG(*) FROM sys.all_columns AS a
        CROSS JOIN sys.all_columns AS b
        CROSS JOIN sys.all_columns AS c
        """;

    /// <inheritdoc/>
    public bool Available { get; private set; }

    /// <inheritdoc/>
    public string SkipReason { get; private set; } = string.Empty;

    /// <inheritdoc/>
    /// <remarks>
    /// The read-only account's password: a credential rather than a path, and the kind of value
    /// ADR 0010 keeps out of the messages a caller reads.
    /// </remarks>
    public string SensitiveText => ReadOnlyPassword;

    /// <summary>
    /// The connection string the server's own administrator opens with.
    /// </summary>
    /// <value>
    /// Used to seed the shared schema and to create the accounts and the extra databases.
    /// </value>
    public string RootConnectionString { get; private set; } = string.Empty;

    /// <summary>
    /// A connection string for a database of this container that no shared-suite assertion looks
    /// at, where tests that need their own tables can create them.
    /// </summary>
    /// <value>
    /// The same server as <see cref="ConnectionString"/>, bound to a second database, as the
    /// read-only account.
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
    /// The spare database, opened as the server's administrator.
    /// </summary>
    /// <value>
    /// What a test seeds through. The Provider is never handed this string: it exists so the
    /// tests that need tables of their own can create them in the same database they read them
    /// back from, as a user with the rights to do so would.
    /// </value>
    public string SpareAdministratorConnectionString { get; private set; } = string.Empty;

    /// <summary>
    /// The name of the database <see cref="SpareConnectionString"/> is bound to.
    /// </summary>
    public string SpareDatabaseName { get; } = "sharpdb_spare";

    /// <summary>
    /// The account the Provider connects as.
    /// </summary>
    /// <value>
    /// The login every connection string this fixture hands to the Provider names, so a test that
    /// creates an object of its own can grant the same account the right to read it.
    /// </value>
    public string ReadOnlyAccountName => ReadOnlyLogin;

    /// <summary>
    /// A connection string for a database of this container that holds no objects at all.
    /// </summary>
    /// <value>
    /// The same server as <see cref="ConnectionString"/>, bound to an empty database.
    /// </value>
    public string EmptyConnectionString { get; private set; } = string.Empty;

    /// <summary>
    /// The name of the database <see cref="EmptyConnectionString"/> is bound to.
    /// </summary>
    public string EmptyDatabaseName { get; } = "sharpdb_empty";

    /// <inheritdoc/>
    /// <remarks>
    /// Starting the container is the only step Docker's absence can legitimately stop, so it is the
    /// only one whose failure is caught. Whatever comes after — seeding included — is this code's
    /// own business and must fail rather than skip.
    /// </remarks>
    public async Task InitializeAsync()
    {
        try
        {
            _container = new MsSqlBuilder(Image)
                .WithPassword(AdminPassword)
                .WithDatabase(DatabaseName)
                .Build();

            await _container.StartAsync();
        }
        catch (DockerUnavailableException unavailable)
        {
            SkipReason = $"Docker is not available, so SQL Server cannot be started: {unavailable.Message}";
            return;
        }

        _reportedConnectionString = _container.GetConnectionString();
        RootConnectionString = AdministratorConnection("master");
        ConnectionString = ReaderConnection(DatabaseName);
        SpareConnectionString = ReaderConnection(SpareDatabaseName);
        SpareAdministratorConnectionString = AdministratorConnection(SpareDatabaseName);
        EmptyConnectionString = ReaderConnection(EmptyDatabaseName);

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
    /// Builds a connection string binding to one database as the server's administrator.
    /// </summary>
    /// <param name="database">The database to bind to.</param>
    /// <returns>The connection string the fixture seeds with.</returns>
    private string AdministratorConnection(string database) =>
        Connection(database, "sa", AdminPassword);

    /// <summary>
    /// Builds a connection string binding to one database as the read-only account.
    /// </summary>
    /// <param name="database">The database to bind to.</param>
    /// <returns>The connection string the Provider is handed.</returns>
    private string ReaderConnection(string database) =>
        Connection(database, ReadOnlyLogin, ReadOnlyPassword);

    /// <summary>
    /// Builds a connection string for one account and one database.
    /// </summary>
    /// <param name="database">The database to bind to.</param>
    /// <param name="user">The account's name.</param>
    /// <param name="password">The account's password.</param>
    /// <returns>The connection string.</returns>
    /// <remarks>
    /// <c>TrustServerCertificate=True</c> is required and not a convenience: the Driver encrypts by
    /// default from 7.0, the container's certificate is self-signed, and measured, the connection
    /// fails without it. It is what the README tells a user with a development server to write, so
    /// the tests are written the same way a user would.
    /// </remarks>
    private string Connection(string database, string user, string password) =>
        new SqlConnectionStringBuilder(_reportedConnectionString)
        {
            InitialCatalog = database,
            UserID = user,
            Password = password,
            TrustServerCertificate = true,
        }.ConnectionString;

    /// <summary>
    /// Creates the extra databases, the read-only account, and seeds the shared schema.
    /// </summary>
    /// <returns>The work that prepares the server.</returns>
    private async Task SeedAsync()
    {
        await using var administrator = new SqlConnection(RootConnectionString);
        await administrator.OpenAsync();

        foreach (var database in new[] { SpareDatabaseName, EmptyDatabaseName })
        {
            await using var create = administrator.CreateCommand();
            create.CommandText = $"IF DB_ID('{database}') IS NULL CREATE DATABASE [{database}]";
            await create.ExecuteNonQueryAsync();
        }

        // The account is created server-wide, then given a user in each database it must reach —
        // a login alone grants nothing inside a database, which is what makes the boundary the
        // grant below and not the login.
        await using (var login = administrator.CreateCommand())
        {
            login.CommandText = $"""
                IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = '{ReadOnlyLogin}')
                    CREATE LOGIN [{ReadOnlyLogin}] WITH PASSWORD = '{ReadOnlyPassword}', CHECK_POLICY = OFF;
                """;
            await login.ExecuteNonQueryAsync();
        }

        foreach (var database in new[] { DatabaseName, SpareDatabaseName, EmptyDatabaseName })
        {
            await using var user = administrator.CreateCommand();
            user.CommandText = $"""
                USE [{database}];
                IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = '{ReadOnlyLogin}')
                    CREATE USER [{ReadOnlyLogin}] FOR LOGIN [{ReadOnlyLogin}];

                -- SELECT on the schema, not only on the database. Measured, a database-level grant
                -- alone left the account unable to see objects it had not created — this engine
                -- filters metadata by permission — so the grant is made where the objects are.
                -- SELECT is what refuses every write, which is this engine's read-only boundary
                -- (ADR 0016).
                IF SCHEMA_ID('dbo') IS NOT NULL GRANT SELECT ON SCHEMA::dbo TO [{ReadOnlyLogin}];

                -- VIEW DEFINITION beside it, and this one was measured the same way. Holding only
                -- SELECT, the account reads a column's default as NULL rather than as the text the
                -- server stores: sys.default_constraints.definition is hidden from a caller
                -- without it. Nothing here is a demand on a user — an account is whoever wrote the
                -- connection string — but a deployment that leaves it off will see a schema with
                -- fewer details than the server holds, which is worth knowing rather than
                -- discovering.
                GRANT VIEW DEFINITION ON SCHEMA::dbo TO [{ReadOnlyLogin}];
                """;
            await user.ExecuteNonQueryAsync();
        }

        // The schema is seeded as the administrator, in the read-only account's place: the Provider
        // is what must be limited, and the fixture is what prepares the ground for it. The grants
        // above were made before these objects existed, so they are repeated for the schema now
        // that there is something in it to cover.
        await using var seeding = new SqlConnection(AdministratorConnection(DatabaseName));
        await seeding.OpenAsync();

        foreach (var batch in ContractSchema.MsSqlDdl)
        {
            await using var statement = seeding.CreateCommand();
            statement.CommandText = batch;
            await statement.ExecuteNonQueryAsync();
        }
    }
}
