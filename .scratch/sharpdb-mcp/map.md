# Map: SharpDb MCP Server v1

Label: `wayfinder:map`

## Destination

A hand-off-ready spec for **SharpDb v1**: a C# stdio MCP server built on the official `ModelContextProtocol` SDK + Dapper, exposing a small set of read-only tools, supporting MySQL and SQLite first, configured purely by environment variables, and structured so additional database types are added by writing a new provider — not by editing the core.

> **Reached.** 地图已全部走完，交接产物：[`spec.md`](../sharpdb-v1/spec.md)（label `ready-for-agent`），实现票开在 `.scratch/sharpdb-v1/`。

## Notes

- **Domain**: .NET 8 console app, MCP server (stdio transport), Dapper over `DbConnection`.
- **Read first**: [`GLOSSARY.md`](../../GLOSSARY.md) for vocabulary (Provider vs Driver, Guard vs Read-only enforcement) and [`docs/adr/`](../../docs/adr/) for settled decisions.
- **Skills every session should consult**: `grilling`, `domain-modeling`, `research`, `prototype`, `codebase-design`.
- **Standing preferences for this effort**:
  - v1 is **read-only**. No INSERT/UPDATE/DELETE/DDL.
  - **Explicit** provider selection (`SHARPDB__TYPE`); no connection-string sniffing.
  - **Single** connection per server instance.
  - **Small, token-efficient** tool surface (4 tools).
  - Multi-DB extensibility is a first-class constraint, not an afterthought.
- **Tracker**: local markdown (`.scratch/sharpdb-mcp/`), per `docs/agents/issue-tracker.md`.

## Decisions so far

<!-- the index: one line per closed ticket, enough to judge relevance, then zoom the link for the detail the ticket holds -->

- [Schema introspection SQL for MySQL and SQLite](issues/01-research-schema-introspection-sql.md): MySQL via `INFORMATION_SCHEMA` (`TABLES`/`COLUMNS`/`KEY_COLUMN_USAGE`/`STATISTICS`); SQLite via `sqlite_schema` + table-scoped PRAGMAs (`table_info`, `foreign_key_list`, `index_list`/`index_info`), using the parameterisable `pragma_*` table-valued form. Comments/charsets/collations are MySQL-only; SQLite auto-increment must be inferred.
- [Read-only SQL enforcement options in .NET](issues/02-research-sql-readonly-validation.md): three layers — `SqlParserCS` AST guard (single statement, `Select`/`Explain`/`Show`/read-only `Pragma`), then engine-level read-only (MySQL `SET SESSION TRANSACTION READ ONLY`, SQLite `Mode=ReadOnly`), then least-privilege credentials. Plain keyword heuristics rejected.
- [MCP SDK: packaging, logging, testing and config binding](issues/03-research-mcp-sdk-packaging.md): `PackAsTool` + `ToolCommandName` on `net8.0`; all logging to stderr via `LogToStandardErrorThreshold`; read-only tools must explicitly declare `ReadOnly/Destructive/Idempotent/OpenWorld`; `UseStructuredContent` + `OutputSchemaType` yields both text and structured content; tests via `McpServerTool.Create` or an in-process `McpClient` over pipes; `SHARPDB__X` binds to options via the `__` separator.
- [Prototype: tool output shapes](issues/04-prototype-tool-output-shapes.md): `execute_query` uses `{columns, rows}` envelope (~40–46% fewer tokens than array-of-objects); `list_tables` and `describe_table` use structured JSON objects; errors use `{error: string}`. `UseStructuredContent` is orthogonal to shape choice. Type representation: native JSON for int/bool/decimal/null, ISO 8601 for datetime, base64 for blob.
- [Provider abstraction: how Core stays database-agnostic](issues/05-provider-abstraction-design.md): 单个深接口 `IDatabaseProvider`（`Type` / `CreateReadOnlyConnectionAsync` / `ListTablesAsync` / `DescribeTableAsync` / `ListDatabasesAsync`）置于 `SharpDb.Abstractions`；Schema model 用**原始声明类型字符串** + 闭合集合 enum，v1 无 Charset/Collation/FullDataType，索引仅 Name/IsUnique/Columns 且排除 PK。Core 不引用任何 Driver，Provider 独占 Driver，Host 按 `SHARPDB__TYPE` 用 `switch` 显式装配（零反射，未知类型启动期 fail-fast）。只读的**机制**在 Provider、**策略**在 Core。连接**每次调用现开现关**（走连接池）——"单连接" = 单数据库。Guard 固定用 `GenericDialect` + 超集允许清单，故 Provider **不暴露方言**。多数据库靠**多 MCP server（多进程）**，v1 不支持进程内多连接。分发维持**单包 + `SHARPDB__TYPE`**。
- [Configuration, startup validation and distribution](issues/06-config-startup-and-distribution.md): 配置**只**来自环境变量，前缀 `SHARPDB__`（**双**下划线，单下划线静默不绑定，见 ADR 0006）；键 `TYPE` / `CONNECTIONSTRING`（必填）+ `DEFAULTMAXROWS`=100 / `MAXROWS`=1000 / `TIMEOUTSECONDS`=30；单个 `SharpDbOptions` 放 Core，`GetSection("SHARPDB").Get<SharpDbOptions>()` 一次性读取，不用 `IOptions`。**配置错误与连接失败一律启动期 fail-fast**（exit 1 + 单行 `sharpdb: ` stderr、不打堆栈、不回显连接串，见 ADR 0007）；启动**主动探测一次连接**（复用 `CreateReadOnlyConnectionAsync`），故无需新增校验成员。分发沿用 03：`Holwon.SharpDb.Mcp` / `sharpdb-mcp` / `0.1.0` / net8.0 / `PackAsTool`。日志默认 Info、**SQL 记语句但不记参数值**、**连接串任何级别都不记**；v1 只认环境变量（排除 user-secrets / config file）。**工具集固定为 4 个**，不提供"只启用部分工具"的开关。行限/超时的**语义**归 ticket 07。
- [Tool contract: names, signatures and behaviour](issues/07-tool-contract-design.md): 4 个工具（`execute_query` / `list_tables` / `describe_table` / `list_databases`）的 description、参数、返回形状、annotations 全部锁定。`execute_query` = `sql` + `parameters`（`@name` 占位符，key 不带 `@`）+ `maxRows`（clamp 到 `MAXROWS`）；行限=截断 + 标记（`truncated?`）；超时走 `CommandTimeout`。`describe_table` 形状以 ticket 05 的 model 为准（无 `fullDataType`、`indexes` 排除 PK）。`list_databases` = 连接可见库（信息性），v1 不支持跨 schema 探查。**业务失败自己 return `CallToolResult{IsError=true}`**，不 throw（SDK 会吞非 `McpException` 的消息，见 ADR 0010）；DB 错误原样透传、仅脱敏文件路径。Annotations 统一 `ReadOnly=true, Destructive=false, Idempotent=true, OpenWorld=false`。
- [Testing strategy and CI](issues/08-testing-strategy-and-ci.md): **xUnit**；测试项目**镜像生产层级**（`SharpDb.Core.Tests` / `SharpDb.Provider.Sqlite.Tests` / `SharpDb.Provider.MySql.Tests` / `SharpDb.Tests`，`Abstractions` 无测试）。Guard 纯单元测试（无 DB）；Provider 集成测试；MCP 端到端只做**薄 smoke**（4 个工具 + annotations + 一条拒绝路径）。SQLite 用**命名共享缓存内存库**（`Data Source={guid};Mode=Memory;Cache=Shared;Pooling=False` + keeper 连接保活，传连接串不传连接实例；fixture 先用可写连接 seed DDL）。**已知残余风险**：SQLite 引擎忽略内存库的 `SQLITE_OPEN_READONLY`（[EFCore#15118](https://github.com/dotnet/efcore/issues/15118)，closed as not planned），故 `Mode=ReadOnly` 无法集成测试——补救是断言 Provider 产出的连接串 `Mode == ReadOnly`。MySQL 用 **`Testcontainers.MySql`（`mysql:8.0`）**，仅测试项目依赖、不进发布包；无 Docker 时 **skip**。解决方案用 **`.slnx`**（需 SDK ≥ 9.0.200；放仓库根，勿与 `.csproj` 同目录）。CI = GitHub Actions **单 job**：build + `dotnet test SharpDb.slnx`；v1 **不设** `dotnet format` / analyzers 门禁（`dotnet format` 对 `.slnx` 支持未经证实，实施时实测）。

## Not yet specified

Fog — in scope, but not yet sharp enough to ticket. Graduates as the frontier advances.

- **MCP resources vs prompts**: whether schema is *also* exposed as MCP resources (e.g. `sharpdb://tables`) alongside tools, or tools only.
- **Large-result handling**: pagination / streaming / truncation strategy when a query returns many rows. Format B's token savings help but are not a substitute for a size cap.

## Out of scope

Work consciously ruled out of *this* effort. Never graduates.

- **Write operations** (INSERT / UPDATE / DELETE / DDL) — v1 is read-only by decision.
- **Automatic DB-type detection** from the connection string — explicitly declined; type is configured.
- **Multiple connections per server instance** — one server, one database.
- **Database types beyond MySQL and SQLite** — the *mechanism* to add them is in scope; shipping them is not.
- **NativeAOT / self-contained single-file distribution** — v1 ships as a .NET global tool.
- **Remote / HTTP MCP transport** — v1 is stdio only.
