using SharpDb.Abstractions;
using SharpDb.Provider.Contract;

namespace SharpDb.Provider.Sqlite.Tests;

/// <summary>
/// A real SQLite database both shared suites run against: the shared schema seeded through a
/// writable connection, then handed to the Provider to serve read-only.
/// </summary>
/// <remarks>
/// The database is a temporary file, which is a deliberate, measured choice — see
/// <see cref="SqliteTestDatabase"/> for the comparison with an in-memory database and the reason
/// the Provider's read-only mode leaves no other substrate.
/// <para>
/// The connection string carries no read-only mode of its own: adding the mode is the Provider's
/// job, and the shared suite asserts that it does.
/// </para>
/// </remarks>
public sealed class SqliteContractDatabase : IContractDatabase, IAsyncLifetime
{
    private SqliteTestDatabase? _file;

    /// <inheritdoc/>
    public string Type => "sqlite";

    /// <inheritdoc/>
    public IDatabaseProvider Provider { get; } = new SqliteDatabaseProvider();

    /// <inheritdoc/>
    public string ConnectionString =>
        _file?.ConnectionString ?? throw new InvalidOperationException("The database has not been created yet.");

    /// <inheritdoc/>
    public string DatabaseName => "main";

    /// <inheritdoc/>
    /// <remarks>
    /// A recursive query without a useful bound runs far longer than the one second the shared
    /// suite allows. It is pure computation over nothing, so it costs the database nothing.
    /// </remarks>
    public string SlowQuery => """
        WITH RECURSIVE cnt(x) AS (SELECT 1 UNION ALL SELECT x + 1 FROM cnt WHERE x < 20000000)
        SELECT count(*) FROM cnt
        """;

    /// <inheritdoc/>
    /// <remarks>
    /// A temporary file is always available, so this engine never skips.
    /// </remarks>
    public bool Available => true;

    /// <inheritdoc/>
    public string SkipReason => string.Empty;

    /// <inheritdoc/>
    public async Task InitializeAsync() =>
        _file = await SqliteTestDatabase.CreateAsync(ContractSchema.SqliteDdl);

    /// <inheritdoc/>
    public Task DisposeAsync()
    {
        _file?.Dispose();

        return Task.CompletedTask;
    }
}
