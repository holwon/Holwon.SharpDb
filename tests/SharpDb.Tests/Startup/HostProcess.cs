using System.Diagnostics;
using System.Reflection;

namespace SharpDb.Tests.Startup;

/// <summary>
/// What one run of the host executable did, as observed from the outside.
/// </summary>
/// <param name="ExitCode">The process exit code.</param>
/// <param name="StandardOutput">Everything the process wrote to stdout.</param>
/// <param name="StandardError">Everything the process wrote to stderr.</param>
internal sealed record HostRunResult(int ExitCode, string StandardOutput, string StandardError)
{
    /// <summary>
    /// The stderr lines, with the trailing newline removed so a one-line message is one element.
    /// </summary>
    /// <value>
    /// The written lines, in order.
    /// </value>
    /// <remarks>
    /// A carriage return left on each line would make every comparison against a message fail on
    /// Windows, which is the only platform this repository is developed on.
    /// </remarks>
    internal string[] StandardErrorLines =>
        StandardError.TrimEnd('\r', '\n').Split('\n').Select(line => line.TrimEnd('\r')).ToArray();
}

/// <summary>
/// Runs the host as a real child process. The startup contract is a process-level one — exit
/// code, stderr and a protocol-clean stdout — so the tests must observe a process, not a method.
/// </summary>
internal static class HostProcess
{
    /// <summary>
    /// The metadata key the host's build output path is injected under.
    /// </summary>
    private const string HostPathMetadataKey = "SharpDbHostPath";

    /// <summary>
    /// How long a single host run may take before it is treated as hung.
    /// </summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Runs the host with exactly the given environment, cheating ambient configuration by
    /// removing every already-set <c>SHARPDB</c> variable first.
    /// </summary>
    /// <param name="environment">The environment the host starts with.</param>
    /// <returns>What the process did.</returns>
    internal static async Task<HostRunResult> RunAsync(IReadOnlyDictionary<string, string?> environment)
    {
        // The host starts in a throwaway empty directory, so no configuration file the
        // repository happens to contain can ever reach it (ADR 0008).
        using var workingDirectory = new TempDirectory("sharpdb-host-");

        return await RunInAsync(environment, workingDirectory.Info.FullName);
    }

    /// <summary>
    /// Runs the host with the given environment from a directory the caller owns and cleans up.
    /// </summary>
    /// <param name="environment">The environment the host starts with.</param>
    /// <param name="workingDirectory">The directory the host starts in.</param>
    /// <returns>What the process did.</returns>
    internal static async Task<HostRunResult> RunInAsync(
        IReadOnlyDictionary<string, string?> environment,
        string workingDirectory)
    {
        if (!File.Exists(HostPath))
        {
            throw new InvalidOperationException($"The host was not built at '{HostPath}'.");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        startInfo.ArgumentList.Add(HostPath);

        RemoveAmbientConfiguration(startInfo);
        foreach (var (key, value) in environment)
        {
            startInfo.Environment[key] = value;
        }

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();

        using var timeout = new CancellationTokenSource(Timeout);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"The host did not exit within {Timeout.TotalSeconds:0} seconds.");
        }

        return new HostRunResult(process.ExitCode, await standardOutput, await standardError);
    }

    /// <summary>
    /// The host's own build output, injected by the test project — it is not copied next to the tests.
    /// </summary>
    /// <value>
    /// The absolute path of <c>SharpDb.dll</c> in the host's build output.
    /// </value>
    private static string HostPath { get; } =
        typeof(HostProcess).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == HostPathMetadataKey)
            ?.Value
        ?? throw new InvalidOperationException(
            $"The '{HostPathMetadataKey}' assembly metadata is missing; check SharpDb.Tests.csproj.");

    /// <summary>
    /// The directory the host's build output sits in, which is also its content root.
    /// </summary>
    /// <value>
    /// The absolute path of the directory a configuration file would have to live in to be found.
    /// </value>
    /// <remarks>
    /// Exposed so a test can put a configuration file exactly where the host would look for one
    /// and prove it is ignored (ADR 0008).
    /// </remarks>
    internal static string HostDirectory => Path.GetDirectoryName(HostPath)!;

    private static void RemoveAmbientConfiguration(ProcessStartInfo startInfo)
    {
        var ambient = startInfo.Environment.Keys
            .Where(key => key.StartsWith("SHARPDB", StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var key in ambient)
        {
            startInfo.Environment.Remove(key);
        }
    }
}
