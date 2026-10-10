using System.Diagnostics;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using SharpDb.Tests.Startup;

namespace SharpDb.Tests.Mcp;

/// <summary>
/// The real host executable, serving its stdio loop with an MCP client attached to it.
/// </summary>
/// <remarks>
/// This is the one place the server is exercised as a client meets it: the process is the built
/// host, its wiring is the host's own, and the client speaks to it over the process's standard
/// streams. The in-process tests build their own server, so they cannot tell whether the host
/// registers the same Tools — this can.
/// </remarks>
internal sealed class ServedHost : IAsyncDisposable
{
    /// <summary>
    /// How long the host may take to stop once its input has ended.
    /// </summary>
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(30);

    private readonly TempDirectory _workingDirectory;
    private readonly Process _process;
    private readonly McpClient _client;

    private ServedHost(TempDirectory workingDirectory, Process process, McpClient client)
    {
        _workingDirectory = workingDirectory;
        _process = process;
        _client = client;
    }

    /// <summary>
    /// The client the test drives.
    /// </summary>
    internal McpClient Client => _client;

    /// <summary>
    /// Starts the host with the given environment and connects a client to it.
    /// </summary>
    /// <param name="environment">The environment the host starts with.</param>
    /// <returns>The running host, which the caller disposes.</returns>
    internal static async Task<ServedHost> StartAsync(IReadOnlyDictionary<string, string?> environment)
    {
        var workingDirectory = new TempDirectory("sharpdb-served-");

        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = workingDirectory.Info.FullName,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        startInfo.ArgumentList.Add(HostProcess.HostPath);

        HostProcess.RemoveAmbientConfiguration(startInfo);
        foreach (var (key, value) in environment)
        {
            startInfo.Environment[key] = value;
        }

        var process = new Process { StartInfo = startInfo };
        process.Start();

        // The client writes to the host's input and reads its output; the host's own stdout is
        // reserved for the protocol, and its logging goes to stderr (ADR 0007).
        var client = await McpClient.CreateAsync(new StreamClientTransport(
            process.StandardInput.BaseStream,
            process.StandardOutput.BaseStream));

        return new ServedHost(workingDirectory, process, client);
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        // Disposing the client ends the conversation, but it does not close the host's input: the
        // host learns the conversation is over from its input ending, so the pipe is closed here.
        // Without this the host would wait for a client that has already gone, and the test would
        // sit here until the timeout and then kill it.
        await _client.DisposeAsync();
        CloseStandardInput();

        try
        {
            using var timeout = new CancellationTokenSource(ShutdownTimeout);
            await _process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            _process.Kill(entireProcessTree: true);
        }

        _process.Dispose();
        _workingDirectory.Dispose();
    }

    /// <summary>
    /// Ends the host's input, which is how the host is told to stop.
    /// </summary>
    /// <remarks>
    /// Best effort: the client may already have disposed the stream it was handed, and a stream
    /// that is already closed has already delivered the end of input this is here to deliver.
    /// </remarks>
    private void CloseStandardInput()
    {
        try
        {
            _process.StandardInput.Close();
        }
        catch (IOException)
        {
            // The pipe is already gone, which is the outcome this method exists to produce.
        }
        catch (ObjectDisposedException)
        {
            // The client closed the stream when it was disposed; the host has its end of input.
        }
        catch (InvalidOperationException)
        {
            // The process has already exited, so there is nothing left to close.
        }
    }
}
