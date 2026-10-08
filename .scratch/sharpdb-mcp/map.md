# Map: SharpDb MCP Server v1

Label: `wayfinder:map`

## Destination

A hand-off-ready spec for **SharpDb v1**: a C# stdio MCP server built on the official `ModelContextProtocol` SDK + Dapper, exposing a small set of read-only tools, supporting MySQL and SQLite first, configured purely by environment variables, and structured so additional database types are added by writing a new provider — not by editing the core.

## Notes

- **Domain**: .NET 8 console app, MCP server (stdio transport), Dapper over `DbConnection`/`DbProviderFactory`.
- **Read first**: [`GLOSSARY.md`](../../GLOSSARY.md) for vocabulary (Provider vs Driver, Guard vs Read-only enforcement) and [`docs/adr/`](../../docs/adr/) for settled decisions.
- **Skills every session should consult**: `grilling`, `domain-modeling`, `research`, `prototype`, `codebase-design`.
- **Standing preferences for this effort**:
  - v1 is **read-only**. No INSERT/UPDATE/DELETE/DDL.
  - **Explicit** provider selection (`SHARPDB_TYPE`); no connection-string sniffing.
  - **Single** connection per server instance.
  - **Small, token-efficient** tool surface (4 tools).
  - Multi-DB extensibility is a first-class constraint, not an afterthought.
- **Tracker**: local markdown (`.scratch/sharpdb-mcp/`), per `docs/agents/issue-tracker.md`.

## Decisions so far

<!-- the index: one line per closed ticket, enough to judge relevance, then zoom the link for the detail the ticket holds -->

- [Schema introspection SQL for MySQL and SQLite](issues/01-research-schema-introspection-sql.md): MySQL via `INFORMATION_SCHEMA` (`TABLES`/`COLUMNS`/`KEY_COLUMN_USAGE`/`STATISTICS`); SQLite via `sqlite_schema` + table-scoped PRAGMAs (`table_info`, `foreign_key_list`, `index_list`/`index_info`), using the parameterisable `pragma_*` table-valued form. Comments/charsets/collations are MySQL-only; SQLite auto-increment must be inferred.
- [Read-only SQL enforcement options in .NET](issues/02-research-sql-readonly-validation.md): three layers — `SqlParserCS` AST guard (single statement, `Select`/`Explain`/`Show`/read-only `Pragma`), then engine-level read-only (MySQL `SET SESSION TRANSACTION READ ONLY`, SQLite `Mode=ReadOnly`), then least-privilege credentials. Plain keyword heuristics rejected.
- [MCP SDK: packaging, logging, testing and config binding](issues/03-research-mcp-sdk-packaging.md): `PackAsTool` + `ToolCommandName` on `net8.0`; all logging to stderr via `LogToStandardErrorThreshold`; read-only tools must explicitly declare `ReadOnly/Destructive/Idempotent/OpenWorld`; `UseStructuredContent` + `OutputSchemaType` yields both text and structured content; tests via `McpServerTool.Create` or an in-process `McpClient` over pipes; `SHARPDB__X` binds to options via the `__` separator.

## Not yet specified

Fog — in scope, but not yet sharp enough to ticket. Graduates as the frontier advances.

- **MCP resources vs prompts**: whether schema is *also* exposed as MCP resources (e.g. `sharpdb://tables`) alongside tools, or tools only.
- **Large-result handling**: pagination / streaming / truncation strategy when a query returns many rows.
- **Connection lifetime & pooling**: per-call connection open/close vs a pooled holder; interaction with the long-running stdio process.
- **Error surface**: how DB errors, validation rejections and timeouts are shaped into tool results without leaking connection secrets.
- **Logging**: what is logged to stderr, at which levels, and whether SQL text is logged.

## Out of scope

Work consciously ruled out of *this* effort. Never graduates.

- **Write operations** (INSERT / UPDATE / DELETE / DDL) — v1 is read-only by decision.
- **Automatic DB-type detection** from the connection string — explicitly declined; type is configured.
- **Multiple connections per server instance** — one server, one database.
- **Database types beyond MySQL and SQLite** — the *mechanism* to add them is in scope; shipping them is not.
- **NativeAOT / self-contained single-file distribution** — v1 ships as a .NET global tool.
- **Remote / HTTP MCP transport** — v1 is stdio only.
