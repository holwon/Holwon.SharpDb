# Configuration, startup validation and distribution

Type: grilling
Status: resolved
Blocked by: 03

## Question

Settle how the server is **configured, validated at startup, and shipped**.

1. **Config surface** — confirm the final env-var names (`SHARPDB_TYPE`, `SHARPDB_CONNECTION_STRING`) and decide whether any per-run knobs are also configurable this way: default row limit, query timeout, max row limit. Should these be env vars, CLI args, or hard-coded defaults? Give each a name and a default.
2. **Startup validation** — the process starts, reads config, and something is wrong (missing `SHARPDB_TYPE`, unknown type, missing/blank connection string, unparsable connection string, connection refused). Decide, for each: does the server **fail fast** (exit non-zero, message on stderr) or **start anyway** and surface the error when a tool is called? What is the argument for each, given an MCP host will show the user a broken/quiet server?
3. **Connection check at startup** — do we eagerly open/verify a connection on boot (fail fast on bad credentials) or lazily on first tool call? Trade-off: boot-time latency & determinism vs a server that can start even if the DB is momentarily down.
4. **Distribution** — confirm the global-tool shape from ticket 03: package id, `ToolCommandName`, initial version, and the exact `mcp.json` snippet a user copies (for a MySQL config and for a SQLite config). Confirm README install instructions.
5. **Secrets** — the connection string arrives as an env var. Decide the stance on logging it (never), echoing it in errors (redact), and whether to support any alternative source (user-secrets / config file) in v1 or rule it out.

> **注**：本节是决策前的提问记录。其中 `SHARPDB_TYPE` / `SHARPDB_CONNECTION_STRING` 为当时的候选写法，最终定为 `SHARPDB__TYPE` / `SHARPDB__CONNECTIONSTRING`，见 Answer 与 [ADR 0006](../../../docs/adr/0006-double-underscore-env-prefix.md)。

## Answer

### 1. 配置面：全部走环境变量，前缀 `SHARPDB__`

配置**只**来自环境变量——不用 CLI args，也不用配置文件。MCP host 就是经 `env` 块传参的，且 map 已定 "configured purely by environment variables"；多一条 CLI 通道只会多一套解析与文档。

| 键 | 绑定到 | 默认 | 说明 |
|---|---|---|---|
| `SHARPDB__TYPE` | `Type` | **必填** | `mysql` / `sqlite` |
| `SHARPDB__CONNECTIONSTRING` | `ConnectionString` | **必填** | 由对应 Provider 的 Driver 解析 |
| `SHARPDB__DEFAULTMAXROWS` | `DefaultMaxRows` | `100` | 工具未显式传 `maxRows` 时使用 |
| `SHARPDB__MAXROWS` | `MaxRows` | `1000` | 硬上限，不可被工具参数突破 |
| `SHARPDB__TIMEOUTSECONDS` | `TimeoutSeconds` | `30` | 映射到 `DbCommand.CommandTimeout` |

**为什么是双下划线**：`__` 是 .NET 配置系统的层级分隔符，`SHARPDB__TYPE` 能绑定到 `SharpDbOptions.Type`；看似自然的 `SHARPDB_TYPE` 是一个**不同的**环境变量，**静默不绑定**（只能 `Configuration["SHARPDB_TYPE"]` 原始读取）。见 [ADR 0006](../../../docs/adr/0006-double-underscore-env-prefix.md)。

**绑定方式**：单个 `SharpDbOptions` 放 **`SharpDb.Core`**（`Type` / `ConnectionString` / `DefaultMaxRows` / `MaxRows` / `TimeoutSeconds`），用 `builder.Configuration.GetSection("SHARPDB").Get<SharpDbOptions>()` 一次性读取。不引入 `IOptions<T>`——只在启动读一次，且这正好是官方示例里唯一出现过的写法。连接串留在 Core 而非 Host：Core 执行查询时**本来就需要它**（每次调用现开现关），拆成两处配置来源没有收益。

**工具集固定为 4 个**：`execute_query` / `list_tables` / `describe_table` / `list_databases`。v1 不提供"只启用部分工具"的开关——4 个工具本来就不多，"工具太多"的顾虑在 `map.md` 的 standing preference 里已经通过"只暴露 4 个工具"这个决定本身解决了。若将来真有"schema-only server"等需求，v2 应作为显式模式（如 `SHARPDB__MODE=schema-only`）引入，而非靠字符串白名单拼出来。

**分工**：**本票定名与默认值，ticket 07 定语义**（截断 vs 报错、如何标记、超时如何呈现）。

### 2. 启动校验：配置错误一律 fail-fast

五类错误全部**立即退出**，不"照常启动、等工具调用时才报"：缺 `SHARPDB__TYPE`、未知 type、缺/空连接串、连接串无法解析、**连接失败**（凭据错 / 主机不可达 / 库不存在）。

理由：MCP server 由客户端以子进程拉起，用户几乎看不到它的运行时输出——一个"起来了但每次调用都失败"的 server 只会表现为莫名其妙的工具错误。进程立即退出会让客户端明确报告启动失败并带回 stderr 原因。见 [ADR 0007](../../../docs/adr/0007-startup-config-errors-fail-fast.md)。

**输出契约**：`exit code 1`；stderr **单行**、前缀 `sharpdb: `（如 `sharpdb: SHARPDB__TYPE is required (expected "mysql" or "sqlite").`）；**不打堆栈**（对用户无意义且可能泄漏路径）；**绝不回显连接串**。

**为何不单独做"语法校验"**：一度考虑过在 Provider 上加一个无 I/O 的 `ValidateConnectionString` 成员。否决——既然启动无论如何都要连一次，探测本身就是最完整的校验（同时覆盖语法与可达性），再加一个只做一半的成员是冗余。这也让 `IDatabaseProvider` 维持 ADR 0003 的 **5 个成员**不变。

### 3. 连接检查：启动期 eager 探测

启动时调一次 `CreateReadOnlyConnectionAsync`，开一条连接随即关闭，即完成"连得上 / 凭据对 / 库存在 / 只读模式可进入"的验证。

**被否决的选项**：lazy（启动不连、首次工具调用才连）。好处是数据库暂时不可用时 server 也能起；否决理由是这是本地单用户工具，清晰的启动失败比"起来了但每次都失败"有用得多。

（注：Dapper 本身不解析连接串、不建连接——真正解析的是 ADO.NET Driver，且只在 `OpenAsync()` 时。所以"用了 Dapper 就自动校验了"并不成立；校验来自这次主动探测。）

### 4. 分发：单包 global tool

沿用 ticket 03 的取值，全部不改：

- `PackageId` = `Holwon.SharpDb.Mcp`
- `ToolCommandName` = `sharpdb-mcp`
- `Version` = `0.1.0`
- `net8.0` + `<PackAsTool>true</PackAsTool>`

安装：`dotnet tool install --global Holwon.SharpDb.Mcp`。用户 `mcp.json` 直接写 shim 名，无需 `--` 分隔符：

```jsonc
// MySQL
{
  "mcpServers": {
    "sharpdb": {
      "command": "sharpdb-mcp",
      "args": [],
      "env": {
        "SHARPDB__TYPE": "mysql",
        "SHARPDB__CONNECTIONSTRING": "Server=localhost;Port=3306;Database=mydb;User Id=readonly;Password=***;"
      }
    }
  }
}
```

```jsonc
// SQLite
{
  "mcpServers": {
    "sharpdb": {
      "command": "sharpdb-mcp",
      "args": [],
      "env": {
        "SHARPDB__TYPE": "sqlite",
        "SHARPDB__CONNECTIONSTRING": "Data Source=/path/to/db.sqlite"
      }
    }
  }
}
```

**README** 含：安装命令、上述两份可复制片段、以及"多数据库 = 多 server 条目"的说明。

### 5. 密钥与日志

- **连接串：任何级别都不记录**（含 Debug）。它含密码，而 stderr 极易被截图、贴进 issue、进 CI 日志。**错误消息中也脱敏**——只出现 host / 库名，或整体 `***`。
- **SQL：记录，默认 Info**。这是本地单用户工具，SQL 日志对排查"AI 到底发了什么"有直接价值。通过标准 `Logging__LogLevel__Default` 调整级别。
- **SQL 只记语句本身，不记参数值**（Dapper `DynamicParameters` 的值不落日志）。语句是"AI 发来的查询"（要的可见性），参数值往往就是行数据本身，会把数据带出 stderr。
- 启动打一行 Info（type + 脱敏后的目标）；工具调用失败打一行 Warning（脱敏消息）。
- **替代来源：v1 明确排除** user-secrets / config file。MCP client 经 `env` 传参是唯一有意义的来源，其他来源徒增攻击面而无消费者。见 [ADR 0008](../../../docs/adr/0008-env-vars-are-the-only-config-source.md)。

（事实核查：官方 SDK 文档对"该记什么"**完全没有指引**，只有 `LogToStandardErrorThreshold` 用于把日志导向 stderr。以上是本项目自定的策略。）

### 6. 交接给 ticket 07

- `DefaultMaxRows` / `MaxRows` / `TimeoutSeconds` 的**运行时语义**（达到上限是截断并标记还是报错；超时如何成形为 `{error}`）。

