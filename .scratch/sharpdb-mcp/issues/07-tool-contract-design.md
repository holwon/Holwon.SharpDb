# Tool contract: names, signatures and behaviour

Type: grilling
Status: resolved
Blocked by: 02, 05

## Question

Fix the **exact contract of the four v1 tools**, in the vocabulary the AI client will see.

For each of `execute_query`, `list_tables`, `describe_table`, `list_databases`:

1. **Exact name and description text** — MCP tool names and the `[Description]` strings. Descriptions are the AI's only manual: what does a good one say?
2. **Parameters** — name, type, required/optional, default. Specifically: does `execute_query` take a `parameters` object (ticket-adjacent to 02's parameterisation stance)? Does it take an optional `maxRows`? Are table/schema names passed as strings, or is `describe_table` table-scoped only?
3. **Return shape** — lock the shape chosen in ticket 04 (JSON envelope vs array vs compact) and state it once, so all tools agree.
4. **Row limit & timeout semantics** — the default limit, the hard cap, what happens when the cap is hit (truncate + flag? error?), and how the timeout surfaces. These interact with ticket 02's read-only enforcement — state where each check runs.
5. **Annotations** — the `[McpServerTool(...)]` annotation values for each tool (per ticket 03), and the rationale for any that are *not* `ReadOnly=true`.
6. **Behaviour on rejection** — when a write statement is refused, what does the tool return: an error result, a text explanation, or a structured payload? Keep it consistent with ticket 04's error shape.
7. **Scope of `list_databases`** — what does it mean for SQLite (which has no server-level database list)? Is the tool conditional per provider, or does it always exist with a provider-defined answer?

## Answer

### 1. 工具集固定为 4 个

v1 有且仅有 4 个工具：`execute_query` / `list_tables` / `describe_table` / `list_databases`。不提供"只启用部分工具"的开关（`SHARPDB__TOOLS` 已砍）。

### 2. `execute_query`

**Description**:
> Execute a single read-only SQL statement and return its result set. Only SELECT, EXPLAIN and SHOW statements are permitted; anything that writes or changes schema is rejected. Use `@name` placeholders in the SQL and pass values via `parameters` rather than embedding literals. Results are capped at `maxRows` rows; when the cap is reached, `truncated` is true.

**Parameters**:
- `sql` (string, required) — *"The SQL statement to execute. Must be a single read-only statement."*
- `parameters` (object, optional) — *"Values for `@name` placeholders in the SQL, keyed without the `@` prefix (e.g. `{"id": 42}` binds `@id`)."*
- `maxRows` (integer, optional) — *"Maximum rows to return. Defaults to the server's configured default and is silently capped at its configured maximum."*

**Return shape**: `{columns: string[], rows: any[][], truncated?: boolean}`。空值省略 `truncated`。

**语义**：
- Guard 前置（解析 SQL，拒绝非只读语句）。
- 行限：读到 `min(maxRows, SHARPDB__MAXROWS)` 行停止，设 `truncated = true`。
- 超时：`DbCommand.CommandTimeout = SHARPDB__TIMEOUTSECONDS`，超时返回 `{error}` + `IsError = true`。

### 3. `list_tables`

**Description**:
> List the tables and views in the connected database. Each entry has a name, a type (`table` or `view`) and a comment when the database provides one. Ordered by name.

**Parameters**: 无。

**Return shape**: `[{name: string, type: "table" | "view", comment?: string}]`。

**语义**：MySQL 用 `TABLE_SCHEMA = DATABASE()`（只看当前库）；SQLite 用 `sqlite_schema`。

### 4. `describe_table`

**Description**:
> Describe one table's structure: its columns (name, ordinal, declared data type, nullability, default, auto-increment, comment), primary key, foreign keys and indexes. Returns an error if the table does not exist. The primary key is not repeated among the indexes.

**Parameters**:
- `table` (string, required) — *"Name of the table or view to describe."*

**Return shape**:
```json
{
  "name": "string",
  "type": "table" | "view",
  "comment?": "string",
  "columns": [
    {
      "name": "string",
      "ordinal": "integer",
      "dataType": "string",
      "isNullable": "boolean",
      "defaultValue?": "string",
      "isAutoIncrement": "boolean",
      "comment?": "string"
    }
  ],
  "primaryKey": ["string"],
  "foreignKeys": [
    {
      "name": "string",
      "columns": ["string"],
      "referencedTable": "string",
      "referencedColumns": ["string"],
      "onDelete": "NoAction" | "Restrict" | "Cascade" | "SetNull" | "SetDefault",
      "onUpdate": "NoAction" | "Restrict" | "Cascade" | "SetNull" | "SetDefault"
    }
  ],
  "indexes": [
    {
      "name": "string",
      "isUnique": "boolean",
      "columns": ["string"]
    }
  ]
}
```

**语义**：形状以 ticket 05 的 model 为准（无 `fullDataType`、`indexes` 排除 PK、`primaryKey` 是纯字符串数组）。表不存在返回 `{error}` + `IsError = true`。

### 5. `list_databases`

**Description**:
> List the databases visible to this connection. This is informational only: the server is bound to one database and every other tool operates on it. Other databases cannot be queried.

**Parameters**: 无。

**Return shape**: `["string"]`。

**语义**：MySQL 用 `SCHEMATA`（所有可见库）；SQLite 用 `PRAGMA database_list` → `["main"]`。v1 **不支持跨 schema 探查**（`list_tables` 不加 `database` 参数）。

### 6. 错误返回机制

所有业务性失败（Guard 拒绝、语法错、超时、表不存在）**自己 return `CallToolResult{IsError = true}`**，`Content` 是 `{error: string}` 的 JSON 文本。**不要 throw**。

理由：官方 SDK 会 catch 异常并返回泛化文案（非 `McpException` 的 `Message` 被丢弃），没有任何开关能改这个行为。要让 AI 看到"为什么拒绝"，必须自己构造错误结果。

**错误消息内容策略**：
- **Guard 拒绝**：`"Statement rejected: INSERT is not allowed. Only SELECT, EXPLAIN and SHOW statements are permitted."`（Core 生成）
- **DB 错误**（语法错、表不存在、列不存在等）：**原样透传 DB 消息**，前缀 `"Database error: "`。AI 需要表名/列名/SQL 位置信息来自我纠正。
- **超时**：`"Query timed out after 30 seconds. Consider adding LIMIT or optimizing the query."`（Core 生成）

**脱敏规则**（仅针对 DB 透传的消息）：
- ✅ 保留：表名、列名、SQL 位置信息（`at line 1, column 5`）、MySQL 错误码
- ❌ 替换：SQLite 文件路径 → `<path>`（正则匹配 `/...` 和 `C:\...` 等模式）
- ❌ 永不出现：连接串（DB 错误消息本身不含，但作为硬规则保留）

本地单用户工具，AI 就是用户的代理，schema 信息没有保密需求。但文件路径会泄漏到 stderr / 日志 / 截图，且对 AI 自我纠正毫无帮助。

### 7. Annotations

四个 tool 统一：
```csharp
[McpServerTool(ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
```

理由：ticket 03 已定值。`Destructive` 在 `ReadOnly = true` 时语义上无意义，但显式设置保持意图清晰。

### 8. 参数占位符约定

统一用 `@name`（MySQL 与 SQLite 的交集）。`parameters` 的 key **不带 `@`**。即 `sql = "SELECT * FROM users WHERE id = @id"` + `parameters = {"id": 42}`。

### 9. 交接给 ticket 08

- 测试策略：Guard 单元测试（无 DB）、Provider 集成测试（SQLite in-memory / MySQL Testcontainers）、MCP 端到端测试（`McpClient` over pipes）。
- CI：哪些测试在每次 push 跑，哪些 gated。
