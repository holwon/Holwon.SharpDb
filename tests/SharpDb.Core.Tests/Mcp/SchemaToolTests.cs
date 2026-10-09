using System.IO.Pipelines;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using SharpDb.Abstractions;
using SharpDb.Core.Configuration;
using SharpDb.Core.Mcp;

namespace SharpDb.Core.Tests.Mcp;

/// <summary>
/// The MCP surface as a client sees it: which tools exist, what they declare, and whether calling
/// them answers.
/// </summary>
/// <remarks>
/// The client here is a real <see cref="McpClient"/> talking over an in-memory pipe to a real
/// server, so the test covers the whole path a client takes — tool registration, the schema the
/// server publishes, and invocation. It asserts the shape of the surface, not the depth of the
/// behaviour: the introspection itself is covered against a real database in the Provider's tests.
/// </remarks>
public class SchemaToolTests
{
    [Fact]
    public async Task The_server_lists_the_two_schema_tools()
    {
        await using var session = await McpSession.StartAsync();

        var names = (await session.Client.ListToolsAsync()).Select(tool => tool.Name);

        Assert.Equal(["list_databases", "list_tables"], names.Order().ToArray());
    }

    [Fact]
    public async Task Both_tools_declare_themselves_read_only()
    {
        await using var session = await McpSession.StartAsync();

        var tools = await session.Client.ListToolsAsync();

        Assert.All(tools, tool =>
        {
            var annotations = tool.ProtocolTool.Annotations;
            Assert.NotNull(annotations);
            Assert.True(annotations.ReadOnlyHint);
            Assert.False(annotations.DestructiveHint);
            Assert.True(annotations.IdempotentHint);
            Assert.False(annotations.OpenWorldHint);
        });
    }

    [Fact]
    public async Task Both_tools_describe_themselves_to_the_client()
    {
        await using var session = await McpSession.StartAsync();

        var descriptions = (await session.Client.ListToolsAsync())
            .ToDictionary(tool => tool.Name, tool => tool.Description);

        Assert.Contains("tables and views", descriptions["list_tables"]);
        Assert.Contains("informational only", descriptions["list_databases"]);
    }

    [Fact]
    public async Task List_tables_answers_with_names_types_and_comments()
    {
        await using var session = await McpSession.StartAsync();

        var result = await session.Client.CallToolAsync("list_tables", new Dictionary<string, object?>());

        Assert.NotEqual(true, result.IsError);
        using var payload = JsonDocument.Parse(TextOf(result));
        Assert.Equal(
            """[{"name":"users","type":"table","comment":"User accounts"},{"name":"active_users","type":"view"}]""",
            payload.RootElement.GetRawText());
    }

    [Fact]
    public async Task List_databases_answers_with_the_visible_names()
    {
        await using var session = await McpSession.StartAsync();

        var result = await session.Client.CallToolAsync("list_databases", new Dictionary<string, object?>());

        Assert.NotEqual(true, result.IsError);
        Assert.Equal("""["main"]""", TextOf(result));
    }

    /// <summary>
    /// The text a call answered with, taken from its first text block.
    /// </summary>
    /// <param name="result">The call's result.</param>
    /// <returns>The payload text.</returns>
    private static string TextOf(CallToolResult result) =>
        Assert.Single(result.Content.OfType<TextContentBlock>()).Text;

    /// <summary>
    /// A running server with a client attached to it over an in-memory pipe.
    /// </summary>
    private sealed class McpSession : IAsyncDisposable
    {
        private readonly IHost _host;
        private readonly McpClient _client;

        private McpSession(IHost host, McpClient client)
        {
            _host = host;
            _client = client;
        }

        /// <summary>
        /// The client the test drives.
        /// </summary>
        internal McpClient Client => _client;

        /// <summary>
        /// Starts a server with the schema tools and connects a client to it.
        /// </summary>
        /// <returns>The connected session, which the caller disposes.</returns>
        internal static async Task<McpSession> StartAsync()
        {
            var builder = Host.CreateEmptyApplicationBuilder(
                new HostApplicationBuilderSettings { ContentRootPath = AppContext.BaseDirectory });

            builder.Services.AddSingleton<IDatabaseProvider>(new StubDatabaseProvider());
            builder.Services.AddSingleton(new SharpDbOptions
            {
                Type = "stub",
                ConnectionString = "stub",
            });

            // The pipe carries the client's words one way and the server's the other; each stream
            // is read at one end and written at the other, so the two are crossed deliberately.
            var clientToServer = new Pipe();
            var serverToClient = new Pipe();

            builder.Services
                .AddMcpServer()
                .WithTools<SchemaTools>()
                .WithStreamServerTransport(
                    clientToServer.Reader.AsStream(),
                    serverToClient.Writer.AsStream());

            var host = builder.Build();
            await host.StartAsync();

            var client = await McpClient.CreateAsync(new StreamClientTransport(
                clientToServer.Writer.AsStream(),
                serverToClient.Reader.AsStream()));

            return new McpSession(host, client);
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            await _client.DisposeAsync();
            await _host.StopAsync();
            _host.Dispose();
        }
    }
}
