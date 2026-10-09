namespace SharpDb;

/// <summary>
/// The entry point of the SharpDb host executable.
/// </summary>
/// <remarks>
/// The host is a buildable shell: it currently starts and exits without doing
/// anything. Configuration binding, provider selection, the read-only connection
/// probe and the stdio loop are wired in by the tickets that need them.
/// </remarks>
internal static class Program
{
    /// <summary>
    /// Starts the host.
    /// </summary>
    /// <returns>
    /// The process exit code.
    /// </returns>
    private static int Main()
    {
        return 0;
    }
}
