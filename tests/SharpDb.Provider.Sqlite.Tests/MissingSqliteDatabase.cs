namespace SharpDb.Provider.Sqlite.Tests;

/// <summary>
/// A directory a test owns that holds no database, so a connection to it must fail.
/// </summary>
/// <remarks>
/// This exists so the failure of opening a connection can be observed as the Tool reports it. The
/// path is deliberately a real directory that the test can name, so an assertion can check that
/// the directory does not appear in what the client is told.
/// </remarks>
internal sealed class MissingSqliteDatabase : IDisposable
{
    private readonly DirectoryInfo _directory =
        Directory.CreateTempSubdirectory("sharpdb-sqlite-missing-");

    /// <summary>
    /// The absolute path of the directory the database would live in.
    /// </summary>
    /// <value>A path that must never appear in a message sent to the client.</value>
    internal string DirectoryPath => _directory.FullName;

    /// <summary>
    /// A connection string naming a file inside that directory, which was never created.
    /// </summary>
    /// <value>The string the Tool is handed.</value>
    internal string ConnectionString =>
        $"Data Source={Path.Combine(_directory.FullName, "missing.sqlite")};Pooling=False";

    /// <inheritdoc/>
    public void Dispose()
    {
        try
        {
            _directory.Delete(recursive: true);
        }
        catch (IOException)
        {
            // A leftover temporary directory must never turn into a test failure.
        }
        catch (UnauthorizedAccessException)
        {
            // Same reasoning: a stale handle must not fail the test that just passed.
        }
    }
}
