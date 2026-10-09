namespace SharpDb.Tests.Startup;

/// <summary>
/// The process-level startup contract, asserted wherever a host run is observed.
/// </summary>
internal static class StartupAssertions
{
    /// <summary>
    /// Asserts the fail-fast contract: exit code 1; stderr holding exactly one line prefixed
    /// <c>sharpdb: </c> and carrying no stack trace; and a completely untouched stdout, so the
    /// stdio protocol channel stays clean (ADR 0007).
    /// </summary>
    /// <param name="result">The observed host run.</param>
    /// <returns>The single stderr line, for further assertions about the reason.</returns>
    /// <remarks>
    /// Requiring exactly one stderr line is also the stack-trace assertion: a trace cannot fit
    /// on a single line.
    /// </remarks>
    internal static string AssertFailsFast(HostRunResult result)
    {
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.StandardOutput);

        var reason = Assert.Single(result.StandardErrorLines);
        Assert.StartsWith("sharpdb: ", reason);

        return reason;
    }
}
