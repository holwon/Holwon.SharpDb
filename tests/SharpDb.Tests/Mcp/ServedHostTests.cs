using System.Text.Json;
using ModelContextProtocol.Protocol;
using SharpDb.Tests.Startup;

namespace SharpDb.Tests.Mcp;

/// <summary>
/// The host as an MCP client meets it: the tools the built executable actually serves, and what
/// calling one of them answers.
/// </summary>
/// <remarks>
/// The subject here is the host's own wiring — that the executable registers the four Tools and
/// serves them over stdio. The behaviour of each Tool is covered where it lives: the schema Tools
/// in-process, the query Tool against a real database in the Provider's tests. This test is what
/// would fail if a Tool were left out of the host's registration, which nothing else would catch.
/// </remarks>
public class ServedHostTests
{
    [Fact]
    public async Task The_host_serves_exactly_the_four_tools()
    {
        using var database = new EmptySqliteDatabase();
        await using var host = await ServedHost.StartAsync(Sqlite(database));

        var names = (await host.Client.ListToolsAsync()).Select(tool => tool.Name).Order().ToArray();

        Assert.Equal(["describe_table", "execute_query", "list_databases", "list_tables"], names);
    }

    [Fact]
    public async Task The_host_runs_a_read_only_query_and_answers_with_its_result_set()
    {
        using var database = new EmptySqliteDatabase();
        await using var host = await ServedHost.StartAsync(Sqlite(database));

        var result = await host.Client.CallToolAsync(
            "execute_query",
            new Dictionary<string, object?> { ["sql"] = "SELECT 1 AS one" });

        Assert.NotEqual(true, result.IsError);
        Assert.Equal("""{"columns":["one"],"rows":[[1]]}""", TextOf(result));
    }

    [Fact]
    public async Task The_host_refuses_a_write_as_a_failed_result()
    {
        // The whole refusal path, over the wire: the client is told why rather than being handed a
        // protocol error, which is what lets it correct itself (ADR 0010).
        using var database = new EmptySqliteDatabase();
        await using var host = await ServedHost.StartAsync(Sqlite(database));

        var result = await host.Client.CallToolAsync(
            "execute_query",
            new Dictionary<string, object?> { ["sql"] = "INSERT INTO anything VALUES (1)" });

        Assert.True(result.IsError);

        using var payload = JsonDocument.Parse(TextOf(result));
        Assert.Contains(
            "Statement rejected",
            payload.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task The_host_binds_a_parameter_sent_over_the_wire()
    {
        // The parameter object is the one part of the contract that has to survive the protocol:
        // the schema the server publishes is what lets a client send it at all, so a mistake there
        // would leave the documented feature unreachable even though the Tool itself works.
        using var database = new EmptySqliteDatabase();
        await using var host = await ServedHost.StartAsync(Sqlite(database));

        var result = await host.Client.CallToolAsync(
            "execute_query",
            new Dictionary<string, object?>
            {
                ["sql"] = "SELECT @value AS echoed",
                ["parameters"] = new Dictionary<string, object?> { ["value"] = 42 },
            });

        Assert.NotEqual(true, result.IsError);
        Assert.Equal("""{"columns":["echoed"],"rows":[[42]]}""", TextOf(result));
    }

    [Fact]
    public async Task The_host_truncates_a_result_at_the_row_limit_sent_over_the_wire()
    {
        using var database = new EmptySqliteDatabase();
        await using var host = await ServedHost.StartAsync(Sqlite(database));

        var result = await host.Client.CallToolAsync(
            "execute_query",
            new Dictionary<string, object?>
            {
                ["sql"] = "SELECT 1 AS n UNION ALL SELECT 2 UNION ALL SELECT 3",
                ["maxRows"] = 2,
            });

        Assert.NotEqual(true, result.IsError);
        Assert.Equal("""{"columns":["n"],"rows":[[1],[2]],"truncated":true}""", TextOf(result));
    }

    /// <summary>
    /// The environment that serves the given database over SQLite.
    /// </summary>
    /// <param name="database">The database the host is pointed at.</param>
    /// <returns>The environment the host starts with.</returns>
    private static Dictionary<string, string?> Sqlite(EmptySqliteDatabase database) => new()
    {
        ["SHARPDB__TYPE"] = "sqlite",
        ["SHARPDB__CONNECTIONSTRING"] = $"Data Source={database.DatabasePath}",
    };

    /// <summary>
    /// The text a call answered with, taken from its first text block.
    /// </summary>
    /// <param name="result">The call's result.</param>
    /// <returns>The payload text.</returns>
    private static string TextOf(CallToolResult result) =>
        Assert.Single(result.Content.OfType<TextContentBlock>()).Text;
}
