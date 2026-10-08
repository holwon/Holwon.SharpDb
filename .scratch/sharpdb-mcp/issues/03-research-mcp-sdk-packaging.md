# MCP SDK: packaging, logging, testing and config binding

Type: research
Status: resolved
Blocked by:

## Question

Pin down the concrete mechanics of shipping and testing a **stdio MCP server** with the official .NET SDK (`ModelContextProtocol`, currently 2.2.0). Answer each precisely, with a source:

1. **Global tool packaging** — what exactly goes in the `.csproj` to make the server installable as `dotnet tool install -g <id>`? Cover `<PackAsTool>`, `<ToolCommandName>`, `<PackageId>`, target framework requirements, and what the user's `mcp.json` `command`/`args` then look like. Note any interaction with `Microsoft.Extensions.Hosting`.
2. **stdout/stderr discipline** — the precise `ILoggingBuilder` configuration required so that *nothing* but JSON-RPC ever reaches stdout. What is the failure mode if a stray `Console.WriteLine` or default logger slips through?
3. **Tool annotations** — the exact semantics and defaults of `McpServerToolAttribute` properties (`ReadOnly`, `Destructive`, `Idempotent`, `OpenWorld`, `UseStructuredContent`, `OutputSchemaType`), and what values a read-only DB tool should declare.
4. **Structured output** — how a tool returns structured JSON today (`UseStructuredContent`, `OutputSchemaType`), and whether returning a POCO/`IEnumerable` is serialised as JSON text or as MCP structured content.
5. **In-process testing** — how to drive an `McpServer` in a test without spawning a process: `StreamServerTransport` / `StreamClientTransport`, `McpClient`, and what a minimal test looks like. Is there an official test helper?
6. **Configuration binding** — confirm how `Host.CreateApplicationBuilder` surfaces MCP-client-supplied `env` variables to the app, and whether `IOptions<T>` / `builder.Configuration.GetSection(...)` binding works for a prefix like `SHARPDB_`.

## Answer

Findings: [`docs/research/mcp-sdk-mechanics.md`](../../../docs/research/mcp-sdk-mechanics.md)

- **Packaging**: `<PackAsTool>true</PackAsTool>` + `<ToolCommandName>` on a `net8.0` exe; consumer's `mcp.json` names the shim directly (no `--`). `PublishSingleFile`/`PublishAot` are incompatible with `PackAsTool`.
- **stdout purity**: `builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace)` is the documented pattern. Pollution is fatal and unrecoverable (transport dies, no reconnect). No analyzer guards it — runtime discipline.
- **Annotations**: defaults are hostile (`ReadOnly=false`, `Destructive=true`, `OpenWorld=true`), so read-only tools must declare `ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false` explicitly.
- **Structured output**: `UseStructuredContent = true` + `OutputSchemaType` populates both `Content` (JSON text) and `StructuredContent` (raw JSON). Without it, POCOs are text-only. This directly feeds ticket 04.
- **Testing**: no published test-helper package. Two tiers — `McpServerTool.Create(...).InvokeAsync(...)` for isolated tool assertions, and a real `McpClient` over `Pipe`-backed `StreamServerTransport`/`StreamClientTransport` for end-to-end.
- **Config**: `Host.CreateApplicationBuilder` auto-reads env vars, and `SHARPDB__X` binds to `SharpDbOptions.X` via the `__` hierarchy separator. **A single-underscore `SHARPDB_X` would not bind** — this is the key input for ticket 06.
