using DotNet.Testcontainers.Builders;
using Oracle.ManagedDataAccess.Client;
using SharpDb.Abstractions;
using SharpDb.Provider.Contract;
using Testcontainers.Oracle;

namespace SharpDb.Provider.Oracle.Tests;

/// <summary>
/// A real Oracle both shared suites run against: a container pinned to Oracle XE 21, seeded with
/// the shared schema, then handed to the Provider to serve read-only.
/// </summary>
/// <remarks>
/// The image is pinned to <c>gvenzl/oracle-xe:21-slim-faststart</c> rather than a floating tag,
/// because the introspection SQL was written and measured against that version and a silent
/// upgrade would change what the shared assertions are asserting. The <c>faststart</c> variant
/// ships a pre-expanded database, so the container becomes usable in a minute or two rather than
/// several.
/// <para>
/// The connection is made as a dedicated application user rather than as the container's
/// <c>system</c> account. That is the point of the fixture: every introspection and every query
/// runs as an ordinary account that owns one schema and holds no administrative privilege, which
/// is the least-privilege shape a real deployment should have (ADR 0014).
/// </para>
/// <para>
/// One container serves the whole collection: Oracle takes minutes to start, so a container per
/// test would be unusable. The container is the collection fixture, which is also what keeps both
/// shared suites asserting against the same server.
/// </para>
/// <para>
/// When Docker is absent the fixture reports the engine as unavailable rather than throwing; the
/// shared suites turn that into skips. Only Docker's own absence is an acceptable reason not to
/// test — a failure of the engine once it is reachable must surface loudly.
/// </para>
/// </remarks>
public sealed class OracleContractDatabase : IContractDatabase, IAsyncLifetime
{
    /// <summary>
    /// The password the container's server and the application user are both given.
    /// </summary>
    private const string Password = "sharpdb-contract-pw";

    /// <summary>
    /// The application user's name, which is also the schema the connection is bound to.
    /// </summary>
    private const string Username = "sharpdb";

    /// <summary>
    /// A second schema the Oracle-specific tests own, so their tables never appear in the shared
    /// listing.
    /// </summary>
    /// <remarks>
    /// An Oracle connection is scoped to one schema — the user it connects as — and the Provider's
    /// listing reports that schema's objects. The shared suites assert the exact set of objects in
    /// it, so a test that needs tables of its own must create them somewhere else; a second schema
    /// is that somewhere else, exactly as a second database is for MySQL.
    /// </remarks>
    private const string SpareUsername = "sharpdb_spare";

    /// <summary>
    /// The password the spare schema's user is created with.
    /// </summary>
    private const string SparePassword = "sharpdb-spare-pw";

    /// <summary>
    /// Oracle's "unable to read data - table definition has changed" (<c>ORA-01466</c>).
    /// </summary>
    /// <remarks>
    /// The engine answers this when a query is parsed against a table definition that has just
    /// changed and the change is not yet visible to the session. Its message calls the fault
    /// transient, which is what lets the fixture absorb it (see <see cref="WarmUpAsync"/>).
    /// </remarks>
    private const int TableDefinitionChanged = 1466;

    private OracleContainer? _container;

    /// <inheritdoc/>
    public string Type => "oracle";

    /// <inheritdoc/>
    public IDatabaseProvider Provider { get; } = new OracleDatabaseProvider();

    /// <inheritdoc/>
    public string ConnectionString { get; private set; } = string.Empty;

    /// <inheritdoc/>
    /// <remarks>
    /// Oracle has no separate database name at this scope; what a connection is bound to is a
    /// schema, which is the user it connects as. A database listing reports the schemas the
    /// connection can see, so this is the name that listing must contain.
    /// </remarks>
    public string DatabaseName => Username;

    /// <inheritdoc/>
    /// <remarks>
    /// A cross join of the object dictionary against itself. On Oracle XE it runs for far longer
    /// than the one second the shared suite allows, so it outlives the timeout, and it touches no
    /// table of its own.
    /// </remarks>
    public string SlowQuery => """
        SELECT count(*) FROM all_objects a CROSS JOIN all_objects b
        """;

    /// <inheritdoc/>
    public bool Available { get; private set; }

    /// <inheritdoc/>
    public string SkipReason { get; private set; } = string.Empty;

    /// <inheritdoc/>
    /// <remarks>
    /// The password both the server and the application user were given: a credential rather than
    /// a path, and the kind of value ADR 0010 keeps out of the messages a caller reads.
    /// </remarks>
    public string SensitiveText => Password;

    /// <summary>
    /// A connection string for a second schema of this container that no shared-suite assertion
    /// looks at, where tests that need their own tables can create them.
    /// </summary>
    /// <value>
    /// The same server as <see cref="ConnectionString"/>, connecting as the spare user.
    /// </value>
    /// <remarks>
    /// The shared schema must stay as <see cref="ContractSchema.OracleDdl"/> left it, because the
    /// shared suites assert the exact set of objects in it. A test that seeded a table of its own
    /// there would break the listing for whichever suite ran after it, so tests with their own
    /// schema material get their own schema — still inside this one container, which is what keeps
    /// the container count at one.
    /// </remarks>
    public string SpareConnectionString { get; private set; } = string.Empty;

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
            _container = new OracleBuilder("gvenzl/oracle-xe:21-slim-faststart")
                .WithUsername(Username)
                .WithPassword(Password)
                .Build();

            await _container.StartAsync();
        }
        catch (DockerUnavailableException unavailable)
        {
            SkipReason = $"Docker is not available, so Oracle cannot be started: {unavailable.Message}";
            return;
        }

        ConnectionString = _container.GetConnectionString();

        await SeedAsync();
        await WarmUpAsync();

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
    /// Seeds the shared schema into the application user's own schema.
    /// </summary>
    /// <returns>The work that seeds it.</returns>
    /// <remarks>
    /// The statements are run one at a time, because Oracle's Driver executes exactly one statement
    /// per round trip — there is no multi-statement form to send (see
    /// <see cref="ContractSchema.OracleDdl"/>).
    /// </remarks>
    private async Task SeedAsync()
    {
        await using var connection = new OracleConnection(ConnectionString);
        await connection.OpenAsync();

        foreach (var statement in ContractSchema.OracleDdl)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = statement;
            await command.ExecuteNonQueryAsync();
        }

        await CreateSpareSchemaAsync();
    }

    /// <summary>
    /// Creates the second schema the Oracle-specific tests own.
    /// </summary>
    /// <returns>The work that creates it and grants the application user read access.</returns>
    /// <remarks>
    /// The schema is created through the container's administrative account, because the
    /// application user holds no <c>CREATE USER</c> privilege — which is exactly the least-privilege
    /// shape the fixture exists to present. The spare user is then granted rights on its own schema
    /// so the application user can read and seed it, the same division the MySQL fixture makes
    /// between <c>root</c> and the application account.
    /// </remarks>
    private async Task CreateSpareSchemaAsync()
    {
        await using var administrator = new OracleConnection(BuildSystemConnectionString());
        await administrator.OpenAsync();

        // One PL/SQL block rather than several statements, because Oracle's Driver runs exactly one
        // statement per round trip and EXECUTE IMMEDIATE is what turns each DDL into one.
        await using var create = administrator.CreateCommand();
        create.CommandText = $"""
            BEGIN
                EXECUTE IMMEDIATE 'CREATE USER {SpareUsername} IDENTIFIED BY "{SparePassword}"';
                EXECUTE IMMEDIATE 'GRANT CREATE SESSION, CREATE TABLE, CREATE VIEW, CREATE SEQUENCE TO {SpareUsername}';
                EXECUTE IMMEDIATE 'GRANT UNLIMITED TABLESPACE TO {SpareUsername}';
            END;
            """;
        await create.ExecuteNonQueryAsync();

        SpareConnectionString = BuildSpareConnectionString();
    }

    /// <summary>
    /// Proves the seeded schema is readable through the Provider's read-only path before any test reads it.
    /// </summary>
    /// <remarks>
    /// Measured, and the engine's own error text is the reason. The first read-only query issued after
    /// the seed's DDL can fail with <c>ORA-01466</c> ("unable to read data - table definition has
    /// changed"): the query is parsed against a table definition the seed's own DDL has just changed,
    /// and the new definition is not yet visible to the session. The engine reports it as a transient
    /// fault — its message carries a link saying so — and it is not what any assertion here is about,
    /// so the fixture absorbs it rather than letting it surface as a spurious failure in a test.
    /// <para>
    /// The read is the shape the assertions use — the shared schema, through a connection the Provider
    /// opened read-only — so bounding the fault here keeps it out of every suite. A fault that persists
    /// past the retries is not this transient one and is left to throw, so a database that is genuinely
    /// unreadable still fails loudly instead of being retried away.
    /// </para>
    /// </remarks>
    private async Task WarmUpAsync()
    {
        const int attempts = 5;

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var connection = await Provider.CreateReadOnlyConnectionAsync(
                    ConnectionString,
                    CancellationToken.None);
                await using var command = connection.CreateCommand();
                command.CommandText = "SELECT count(*) FROM readings";
                await command.ExecuteScalarAsync();
                return;
            }
            catch (OracleException failure) when (failure.Number == TableDefinitionChanged && attempt < attempts)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100 * attempt));
            }
        }
    }

    /// <summary>
    /// Builds the administrator's connection string from the container's own.
    /// </summary>
    /// <returns>The connection string.</returns>
    /// <remarks>
    /// The container's own connection string reaches the application user, so only the credentials
    /// are swapped: the data source — host, port and service name — is the one the container
    /// published.
    /// </remarks>
    private string BuildSystemConnectionString() =>
        new OracleConnectionStringBuilder(ConnectionString)
        {
            UserID = "system",
            Password = Password,
        }.ConnectionString;

    /// <summary>
    /// Builds the spare user's connection string from the container's own.
    /// </summary>
    /// <returns>The connection string.</returns>
    private string BuildSpareConnectionString() =>
        new OracleConnectionStringBuilder(ConnectionString)
        {
            UserID = SpareUsername,
            Password = SparePassword,
        }.ConnectionString;
}
