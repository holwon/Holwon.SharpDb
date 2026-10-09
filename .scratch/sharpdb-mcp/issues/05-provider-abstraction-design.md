# Provider abstraction: how Core stays database-agnostic

Type: grilling
Status: resolved
Blocked by: 01, 03

## Question

Design the **seam between the MCP core and a database provider**, so that adding a database type means writing a new provider project and nothing else. This is the load-bearing decision of the whole effort.

Work through, at minimum:

1. **The provider contract** — what interface(s) does a provider implement? Candidates: a single `IDatabaseProvider` (owns connection creation, SQL dialect quirks, introspection) vs several small seams (`IDbConnectionFactory` + `ISchemaIntrospector` + `ISqlDialect`). Which is deeper / simpler? What does each method actually take and return?
2. **The neutral schema model** — the POCOs a provider returns for tables/columns/PK/FK/indexes (informed by ticket 01's "no equivalent" list). Where do they live, and how much do they normalise (e.g. one `string DataType` vs a normalised enum + raw string)?
3. **Discovery & registration** — how does the running server pick a provider from `SHARPDB__TYPE`? Options: (a) explicit compile-time switch in Core referencing provider packages, (b) each provider ships a `[assembly:]`-level registration entry point that Core reflects over, (c) a tiny registry Core exposes that the host program populates. Which keeps Core free of driver references while staying debuggable and trim-safe?
4. **Driver dependencies** — confirm Core references no `MySqlConnector`/`Microsoft.Data.Sqlite`; each provider owns its driver. Does the *host* project (the global tool) reference all providers, and is that acceptable?
5. **What the core owns vs the provider** — read-only enforcement (ticket 02), row limits, timeouts, parameter handling, error shaping: which side of the seam do these sit on, and why?

Record the resulting interface sketch in the Answer (names and signatures are fine — this ticket resolves the *design*, not the code).

## Answer

新增一种数据库类型 = **新增一个 Provider 项目 + 在 Host 的 `switch` 里加一条分支**。Core 零改动——包括无需任何方言映射。

### 1. 契约粒度：单个深接口

`IDatabaseProvider` 一个接口收下连接创建、内省与标识；**不**拆成 `IDbConnectionFactory` + `ISchemaIntrospector` + `ISqlDialect`。这些部分从不独立变化，拆开只换来多个注册点与没有消费者的组合问题。两个真 Provider（MySQL / SQLite）落在**同一个** seam 上。理由见 [ADR 0003](../../../docs/adr/0003-single-deep-provider-interface.md)。

**Provider 不暴露方言**（见第 5 点与 [ADR 0005](../../../docs/adr/0005-guard-uses-generic-dialect.md)）。

### 2. 中立 Schema model

放在 `SharpDb.Abstractions`，纯 POCO / record，不引用任何 Driver。

- **数据类型用原始声明字符串**（MySQL `COLUMN_TYPE` 如 `bigint(20) unsigned`；SQLite 声明 `type`，可能为空）。不加归一化 enum——那是有损映射，且 AI 需要的就是声明类型。将来若要粗粒度提示，再加 `ColumnCategory`。
- **闭合集合用 enum**（`TableType`、`ForeignKeyAction`），**开放集合用 string**（数据类型）。
- **v1 不含 `Charset` / `Collation` / `FullDataType`**：MySQL-only、对 AI 价值低，直接省略比标为 optional 的接口更小。
- `IsAutoIncrement` 由 SQLite Provider **计算**（`pk = 1` + `type = 'INTEGER'`），MySQL 读 `EXTRA`。
- **索引只保留** `Name / IsUnique / Columns`；丢弃排序方向、partial、expression。**`Indexes` 排除主键**，避免与 `PrimaryKey` 重复表达。
- `ReferencedColumns` 在 SQLite 隐式指向父表主键时可为空列表，不报错。

### 3. 发现与注册：Host 侧显式 `switch`，零反射

Host 读 `SHARPDB__TYPE`，`switch` 决定注册哪一个 Provider（每个 Provider 提供自己的 DI 扩展方法）。运行时只有一个 Provider、一条连接。不用 `[assembly:]` 反射扫描——它带来 trim 不安全、装配错误静默失败、调试困难，而省下的注册行不值得。未知类型成为**启动期** fail-fast 错误（与 ticket 06 交界）。

**多数据库 = 多进程**：MCP 客户端每配一条 server 就是独立子进程。一个 SharpDb 进程只有一条连接、一个库，agent 面对单一数据库无歧义。**v1 不支持进程内多连接**（维持 map 的 out-of-scope）。

### 4. Driver 依赖

Core 是库，**不引用** `MySqlConnector` / `Microsoft.Data.Sqlite`；每个 Provider 独占自己的 Driver；Host（global tool）引用全部 Provider。见 [ADR 0004](../../../docs/adr/0004-core-driver-free-provider-owned-drivers.md)。

### 5. Core 与 Provider 的职责分界

| 关注点 | 归属 |
|---|---|
| Guard（AST 只读检查） | **Core**（固定 `GenericDialect` + 超集允许清单） |
| 引擎级只读（会话只读 / `Mode=ReadOnly`） | **Provider**（烤进建连接过程） |
| 行数上限 / 截断 | **Core**（读到第 N 行停止，不改写 SQL） |
| 超时 | **Core**（`DbCommand.CommandTimeout`） |
| 用户查询参数 | **Core**（Dapper `DynamicParameters`） |
| 内省参数 | **Provider**（自写 SQL，SQLite 用 `pragma_table_info(?)`） |
| 错误成形 / 脱敏 | **Core**（Provider 让原生 `DbException` 冒泡） |
| `list_databases` | **Provider 作答**（MySQL `SCHEMATA` / SQLite `PRAGMA database_list` → `["main"]`） |
| 连接串校验 | **Provider 校验语法**，是否 fail-fast 由 Core/Host 定（ticket 06） |
| capability 标志位 | **v1 不要** |

### 6. 连接所有权

**Core 持有、Provider 创建；每次 tool 调用现开现关**（走 ADO.NET 连接池）。"单连接" = 单数据库，不是单连接对象。`DbConnection` 非线程安全，现开现关使并发安全与断线自愈免费获得，Provider 保持无状态。工厂返回**已打开、已进入只读模式**的连接（MySQL 需在建连后执行 `SET SESSION TRANSACTION READ ONLY`）。

### Interface sketch

```csharp
namespace SharpDb.Abstractions;

public interface IDatabaseProvider
{
    /// 与 SHARPDB__TYPE 匹配的标识，如 "mysql" / "sqlite"。用于诊断与工具文案。
    string Type { get; }

    /// 打开一个已处于引擎级只读模式的连接。
    Task<DbConnection> CreateReadOnlyConnectionAsync(string connectionString, CancellationToken ct);

    Task<IReadOnlyList<TableInfo>> ListTablesAsync(DbConnection conn, CancellationToken ct);

    /// 表不存在时返回 null（由 Core 翻译为用户文案）。
    Task<TableSchema?> DescribeTableAsync(DbConnection conn, string tableName, CancellationToken ct);

    Task<IReadOnlyList<string>> ListDatabasesAsync(DbConnection conn, CancellationToken ct);
}
```

```csharp
public enum TableType { Table, View }
public enum ForeignKeyAction { NoAction, Restrict, Cascade, SetNull, SetDefault }

public sealed record TableInfo(string Name, TableType Type, string? Comment);

public sealed record ColumnInfo(
    string Name, int Ordinal, string DataType, bool IsNullable,
    string? DefaultValue, bool IsAutoIncrement, string? Comment);

public sealed record ForeignKeyInfo(
    string Name, IReadOnlyList<string> Columns, string ReferencedTable,
    IReadOnlyList<string> ReferencedColumns,
    ForeignKeyAction OnDelete, ForeignKeyAction OnUpdate);

public sealed record IndexInfo(string Name, bool IsUnique, IReadOnlyList<string> Columns);

public sealed record TableSchema(
    string Name, TableType Type, string? Comment,
    IReadOnlyList<ColumnInfo> Columns,
    IReadOnlyList<string> PrimaryKey,
    IReadOnlyList<ForeignKeyInfo> ForeignKeys,
    IReadOnlyList<IndexInfo> Indexes);
```

### 项目拓扑

| 项目 | 内容 | 引用 |
|---|---|---|
| `SharpDb.Abstractions` | Schema model + `IDatabaseProvider` | 无（不引 Driver、不引 parser） |
| `SharpDb.Core` | Guard、查询执行、options、4 个 Tool | Abstractions + Dapper + SqlParserCS + ModelContextProtocol |
| `SharpDb.Provider.MySql` | MySQL Provider | Abstractions + MySqlConnector |
| `SharpDb.Provider.Sqlite` | SQLite Provider | Abstractions + Microsoft.Data.Sqlite |
| `SharpDb`（exe / global tool） | 装配、配置绑定、`PackAsTool` | Core + 两个 Provider |

### 分发：单包 + `SHARPDB__TYPE`

打包单位与工具面**无关**——agent 只看到 MCP server 及其 Tool，看不到 NuGet 包。工具数量只由"配了几个 server"决定，且每个 server 暴露同一组 4 个工具名。故维持**单包**（装一次、一个版本号、与 map 一致），放弃"每库一包"。见 [ADR 0009](../../../docs/adr/0009-single-package-runtime-type-selection.md)。

### 交接给 ticket 07

- Guard 实现细节：AST 无统一 `Statement.Show`，只有 `ShowTables` / `ShowColumns` / `ShowCreate` / `ShowCollation` 等一串嵌套 record，允许清单需逐个枚举；`Explain` 需递归检查内层语句。
