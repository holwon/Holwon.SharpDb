namespace SharpDb.Tests.Startup;

/// <summary>
/// A real, empty SQLite database file in a directory the test owns.
/// </summary>
/// <remarks>
/// A zero-length file is a valid, empty SQLite database, so this needs no Driver and writes no
/// schema: it exists purely so the host has something it can actually open.
/// </remarks>
internal sealed class EmptySqliteDatabase : IDisposable
{
    private readonly TempDirectory _directory = new("sharpdb-sqlite-");

    /// <summary>
    /// Creates an empty database file in a fresh temporary directory.
    /// </summary>
    internal EmptySqliteDatabase()
    {
        DatabasePath = _directory.PathOf("sharpdb.sqlite");
        File.WriteAllBytes(DatabasePath, []);
    }

    /// <summary>
    /// The absolute path of the empty database file.
    /// </summary>
    /// <value>
    /// A path inside the temporary directory this instance owns.
    /// </value>
    internal string DatabasePath { get; }

    /// <summary>
    /// The absolute path of a file in the same directory that was never created.
    /// </summary>
    /// <value>
    /// A path a connection attempt must fail on.
    /// </value>
    internal string MissingDatabasePath => _directory.PathOf("missing.sqlite");

    /// <inheritdoc/>
    public void Dispose() => _directory.Dispose();
}
