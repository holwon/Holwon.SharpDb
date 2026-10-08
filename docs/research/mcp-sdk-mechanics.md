# MCP C# SDK mechanics: packaging, logging, annotations, testing, config

Research for ticket `03-research-mcp-sdk-packaging` in `.scratch/sharpdb-mcp/`.

**Sources**: `modelcontextprotocol/csharp-sdk` @ `3338e88` — [`McpServerToolAttribute.cs`](https://github.com/modelcontextprotocol/csharp-sdk/blob/3338e88/src/ModelContextProtocol.Core/Server/McpServerToolAttribute.cs), [`AIFunctionMcpServerTool.cs`](https://github.com/modelcontextprotocol/csharp-sdk/blob/3338e88/src/ModelContextProtocol.Core/Server/AIFunctionMcpServerTool.cs), [`getting-started.md`](https://github.com/modelcontextprotocol/csharp-sdk/blob/3338e88/docs/concepts/getting-started.md), [`tools.md`](https://github.com/modelcontextprotocol/csharp-sdk/blob/3338e88/docs/concepts/tools/tools.md), [`transports.md`](https://github.com/modelcontextprotocol/csharp-sdk/blob/3338e88/docs/concepts/transports/transports.md), [`samples/InMemoryTransport`](https://github.com/modelcontextprotocol/csharp-sdk/blob/3338e88/samples/InMemoryTransport/Program.cs), [NuGet `ModelContextProtocol` 2.2.0](https://www.nuget.org/packages/ModelContextProtocol/2.2.0).

## 1. Global tool packaging

The SDK multi-targets `net10.0;net9.0;net8.0;netstandard2.0`, so `net8.0` is the floor for a tool.

```xml
<PropertyGroup>
  <OutputType>Exe</OutputType>
  <TargetFramework>net8.0</TargetFramework>
  <PackAsTool>true</PackAsTool>
  <ToolCommandName>sharpdb-mcp</ToolCommandName>
  <PackageId>Holwon.SharpDb.Mcp</PackageId>
  <Version>0.1.0</Version>
</PropertyGroup>
```

Install: `dotnet tool install --global Holwon.SharpDb.Mcp`. The consumer's `mcp.json` then names the shim directly:

```jsonc
{ "command": "sharpdb-mcp", "args": [], "env": { "…": "…" } }
```

Gotchas:
- The `dotnet tool` shim is a native launcher that needs a compatible runtime installed; TFM roll-forward applies.
- In `mcp.json` the `command` *is* the shim, so no `--` separator is needed (`--` only matters for `dotnet tool run`).
- `<PublishSingleFile>` / `<PublishAot>` are **incompatible** with `<PackAsTool>`. The SDK samples use `PublishAot` for standalone publishing, not for tools.
- stdout must stay protocol-pure (see §2).

## 2. stdout / stderr discipline

The canonical pattern, from the SDK's own getting-started and every stdio sample:

```csharp
var builder = Host.CreateApplicationBuilder(args);
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Services.AddMcpServer().WithStdioServerTransport().WithToolsFromAssembly();
await builder.Build().RunAsync();
```

`LogToStandardErrorThreshold = LogLevel.Trace` routes **all** console log levels to stderr, leaving stdout for JSON-RPC only.

Failure mode if stdout is polluted: the transport is newline-delimited JSON-RPC; a stray line makes the peer throw on parse, the transport treats it as fatal, and there is **no reconnect** — the process must restart. There is **no** analyzer or compile-time guard against `Console.WriteLine`; this is a runtime discipline.

## 3. Tool annotations — exact defaults

Constants in `McpServerToolAttribute`: `DestructiveDefault = true`, `IdempotentDefault = false`, `OpenWorldDefault = true`, `ReadOnlyDefault = false`.

| Property | Type | Default |
|---|---|---|
| `Name` | `string?` | `null` → derived from method name (snake_case, `Async` suffix stripped) |
| `Title` | `string?` | `null` |
| `ReadOnly` | `bool` | **`false`** |
| `Destructive` | `bool` | **`true`** |
| `Idempotent` | `bool` | **`false`** |
| `OpenWorld` | `bool` | **`true`** |
| `UseStructuredContent` | `bool` | **`false`** |
| `OutputSchemaType` | `Type?` | **`null`** |
| `IconSource` | `string?` | `null` |

A read-only DB tool should declare:

```csharp
[McpServerTool(ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
```

`Destructive` is semantically moot when `ReadOnly = true`, but setting it explicitly keeps the intent legible.

## 4. Structured output

- `UseStructuredContent = true` makes the SDK emit a JSON Schema into `Tool.OutputSchema` and populate **both** `CallToolResult.Content` (JSON text) and `CallToolResult.StructuredContent` (raw JSON).
- `OutputSchemaType` lets a method that returns `CallToolResult` still declare a schema.
- With `UseStructuredContent = false` (the default), a POCO or `IEnumerable<T>` is serialised **only** as JSON *text* in `Content[0]`; no structured content is produced.

So for a tabular result set: return a POCO with `UseStructuredContent = true, OutputSchemaType = typeof(QueryResult)` to serve both the LLM (text) and programmatic consumers (structured).

## 5. In-process testing

Types: `StreamServerTransport(Stream in, Stream out)`, `StreamClientTransport(serverInput, serverToClient)`, and `McpClient.CreateAsync(ITransport)`. Wire them through `System.IO.Pipelines.Pipe` pairs — see the SDK's `samples/InMemoryTransport` and `tests/.../ClientServerTestBase.cs`.

Two testing tiers:
- **Lightest** — no transport at all: `McpServerTool.Create(delegate)` then `tool.InvokeAsync(requestContext)`. Good for asserting a tool's content in isolation.
- **End-to-end** — real `McpClient` over pipes against a real `McpServer`: exercises listing, schemas and invocation.

There is **no** published test-helper NuGet package; `ClientServerTestBase` lives in the SDK's own test project and must be re-implemented (it is small).

## 6. Configuration binding

`Host.CreateApplicationBuilder(args)` registers, in increasing precedence: `appsettings.json` → `appsettings.{Environment}.json` → user secrets (Development) → **environment variables** → command-line args. MCP-client `env` entries arrive as process environment variables, so they are picked up automatically.

Environment variables use `__` for the configuration hierarchy separator. So a prefixed options class binds cleanly:

```csharp
public sealed class SharpDbOptions { public string? Type { get; set; } public string? ConnectionString { get; set; } }
builder.Services.Configure<SharpDbOptions>(builder.Configuration.GetSection("SHARPDB"));
```

maps `SHARPDB__TYPE` → `SharpDbOptions.Type` and `SHARPDB__CONNECTIONSTRING` → `SharpDbOptions.ConnectionString`.

**Note**: this is the `__` (double-underscore) convention. A flat single-underscore name like `SHARPDB_TYPE` is a *distinct* environment variable and would **not** bind to `SharpDbOptions.Type` — it would only be reachable as `Configuration["SHARPDB_TYPE"]`. This is a real fork in the road for ticket 06.
