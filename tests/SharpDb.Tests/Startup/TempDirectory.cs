namespace SharpDb.Tests.Startup;

/// <summary>
/// A temporary directory a test owns, deleted when the test is done.
/// </summary>
/// <remarks>
/// Cleanup is best effort throughout: a directory left behind by a failed delete must never turn
/// a passing assertion into a failing one, so deletion failures are swallowed here rather than at
/// every call site.
/// </remarks>
internal sealed class TempDirectory : IDisposable
{
    /// <summary>
    /// Creates a fresh directory with the given prefix.
    /// </summary>
    /// <param name="prefix">A short label that makes the directory recognisable while debugging.</param>
    internal TempDirectory(string prefix)
    {
        Info = Directory.CreateTempSubdirectory(prefix);
    }

    /// <summary>
    /// The directory itself.
    /// </summary>
    /// <value>
    /// The temporary directory this instance owns.
    /// </value>
    internal DirectoryInfo Info { get; }

    /// <summary>
    /// Builds a path inside the directory without creating anything there.
    /// </summary>
    /// <param name="fileName">The file name, relative to the directory.</param>
    /// <returns>The absolute path the name resolves to.</returns>
    internal string PathOf(string fileName) => Path.Combine(Info.FullName, fileName);

    /// <inheritdoc/>
    public void Dispose()
    {
        try
        {
            Info.Delete(recursive: true);
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
