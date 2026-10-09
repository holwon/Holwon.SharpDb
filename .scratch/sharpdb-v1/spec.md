# Spec: SharpDb v1 — 只读数据库 MCP 服务器

Label: `ready-for-agent`

> **实现票在 [`./issues/`](./issues/)（`01`–`08`）。**
> `.scratch/sharpdb-mcp/` 是**设计史**（wayfinder 档案，只读，**勿实现**）——它也有 `01`–`08` 编号，别走错门。
>
> 本文由 `.scratch/sharpdb-mcp/` 的 8 张地图票与 `docs/adr/0001`–`0010` 综合而成。
> 词汇一律以 [`GLOSSARY.md`](../../GLOSSARY.md) 为准（Provider / Driver / Core / Host / Schema model / Introspection / Guard / Read-only enforcement / Tool）。

## Problem Statement

AI 代理在回答关于某个数据库的问题时，只能靠用户手工粘贴的表结构或数据片段。它无法自己看清一个库有哪些表、每张表的列与外键长什么样，也无法执行一条只读查询去验证自己的猜测。用户于是被迫在"解释 schema"和"贴查询结果"之间来回搬运，既费时又容易失真。

数据库 MCP 服务器能填上这个缺口，但现成的方案要么允许写操作（把生产库置于风险中），要么把支持范围锁死在单一数据库引擎（换一个库就得换一个工具、换一套配置）。用户需要的是：**一个只读、可靠、能被 AI 直接调用、且将来能扩展新数据库类型的服务器**。

## Solution

一个以 stdio 运行的 MCP 服务器，向 AI 客户端暴露**恰好四个只读工具**：

- `execute_query` —— 执行一条只读 SQL 并返回结果集
- `list_tables` —— 列出当前库的表与视图
- `describe_table` —— 描述一张表的结构（列 / 主键 / 外键 / 索引）
- `list_databases` —— 列出连接可见的数据库（仅信息性）

只读由**三层**共同保证（ADR 0002）：应用层的 **Guard**（解析 SQL 并拒绝非只读语句，给 AI 清晰的拒绝理由）、数据库引擎层的**只读模式**（MySQL 会话只读、SQLite `Mode=ReadOnly`），以及最小权限账号（文档化的最终边界）。

服务器**只支持配置一个数据库**，用哪个库由环境变量 `SHARPDB__TYPE` 在启动时决定，**不做连接串嗅探**。支持 MySQL 与 SQLite 两种数据库类型；新增一种类型 = **新增一个 Provider 项目 + 在 Host 的装配处加一条分支，Core 零改动**。

配置**只**来自环境变量（即 MCP client `mcp.json` 的 `env` 块）。配置错误或连不上库时**立即退出**并在 stderr 给出一行原因——因为 MCP client 以子进程方式拉起服务器，用户看不到它的运行时输出，“起来了但每次调用都失败”远比“清晰地起不来”更难排查。

分发为**单个** .NET global tool 包；装一次即可，切换数据库只改环境变量。

## User Stories

### 安装与配置

1. 作为 AI 代理的使用者，我想用一条 `dotnet tool install --global` 命令装好服务器，以便不必克隆源码或手工编译。
2. 作为使用者，我想只装**一个**包就同时获得 MySQL 与 SQLite 支持，以便不必猜测该装哪个包。
3. 作为使用者，我想在 `mcp.json` 里通过环境变量选择数据库类型，以便切换数据库时不重装任何东西。
4. 作为使用者，我想在 `mcp.json` 里以连接字符串传入凭据，以便凭据留在客户端配置里而不是代码或命令行历史中。
5. 作为使用者，我想在 README 里看到一份可直接复制的 MySQL 配置片段，以便不必猜键名。
6. 作为使用者，我想在 README 里看到一份可直接复制的 SQLite 配置片段，以便不必猜路径写法。
7. 作为使用者，我想看到"多个数据库 = 多个 server 条目"的说明，以便为两个库各配一个服务器实例。
8. 作为使用者，我想让服务器只认环境变量、不读 `appsettings.json` 或 user-secrets，以便配置只有一个可预期的来源。
9. 作为使用者，我想在漏配数据库类型时看到明确的报错，以便立刻知道少了什么。
10. 作为使用者，我想在数据库类型拼错时看到"期望 mysql 或 sqlite"的提示，以便自己纠正而不必查文档。
11. 作为使用者，我想在漏配或留空连接字符串时看到明确报错，以便区分"没配"和"配错了"。

### 启动与失败

12. 作为使用者，我想让服务器在配置错误时**立即退出**而非半死不活地启动，以便 MCP client 直接报告启动失败。
13. 作为使用者，我想让启动失败的消息出现在 **stderr**，以便它不与协议流量混在一起。
14. 作为使用者，我想让启动失败返回**非零退出码**，以便脚本和 CI 能判断成败。
15. 作为使用者，我想让启动失败**不打堆栈**，以便消息简短可读。
16. 作为使用者，我想让服务器在启动时**真的连一次库**，以便凭据错、库不存在这类问题在启动时就暴露，而不是等到第一次提问。
17. 作为使用者，我想在服务器连不上库时立刻收到失败，以便不必通过反复提问来排查。
18. 作为使用者，我想让**任何**输出都不污染 stdout，以便 MCP 协议不被破坏。

### 探索结构

19. 作为 AI 代理，我想列出库里所有的表和视图，以便知道自己能问什么。
20. 作为 AI 代理，我想在表列表里看到每项是 `table` 还是 `view`，以便区分实体表与派生视图。
21. 作为 AI 代理，我想在表列表里按名称排序，以便结果稳定可预期。
22. 作为 AI 代理，我想在数据库提供表注释时看到注释，以便理解表的用途。
23. 作为 AI 代理，我想描述一张表的列，以便写出正确的查询。
24. 作为 AI 代理，我想看到每列的名称、序号、**声明的数据类型**、是否可空、默认值，以便构造合法的 SQL。
25. 作为 AI 代理，我想看到列是否为自增，以便判断插入语义（即使在只读场景下，也有助于理解主键生成方式）。
26. 作为 AI 代理，我想看到列注释（若数据库提供），以便理解列的语义。
27. 作为 AI 代理，我想看到表的主键列，以便构造连接与去重。
28. 作为 AI 代理，我想看到外键（本地列、被引用的表与列、删除/更新动作），以便自己 join 而不必猜关系。
29. 作为 AI 代理，我想看到索引（名称、是否唯一、列），以便判断哪些查询高效。
30. 作为 AI 代理，我想让**主键不在索引列表里重复出现**，以便不产生冗余信息。
31. 作为 AI 代理，我想在描述一张不存在的表时收到明确的错误，以便知道自己猜错了表名。
32. 作为 AI 代理，我想在描述视图时也能拿到它的列结构，以便查询视图。

### 执行查询

33. 作为 AI 代理，我想执行一条只读 SQL 并拿到结果集，以便用真实数据回答问题。
34. 作为 AI 代理，我想让结果集用 `{columns, rows}` 的形式返回，以便列名只出现一次、节省 token。
35. 作为 AI 代理，我想让整数、小数、布尔、null 以原生 JSON 类型返回，以便直接用于计算与判断。
36. 作为 AI 代理，我想让日期时间以 ISO 8601 字符串返回，以便无歧义地解析。
37. 作为 AI 代理，我想让二进制以 base64 返回，以便能识别它是二进制而不是文本。
38. 作为 AI 代理，我想让 null 用原生 JSON `null` 表示，以便与字符串 `"null"` 区分开。
39. 作为 AI 代理，我想用 `@name` 占位符传参而不是拼接字面量，以便写出安全且可读的查询。
40. 作为 AI 代理，我想用不带 `@` 的键名传参，以便参数对象与占位符的对应关系一目了然。
41. 作为 AI 代理，我想限制单次返回的行数，以便不被超大结果集淹没。
42. 作为 AI 代理，我想在结果被行数上限截断时看到一个明确的标记，以便知道自己看到的不是全部数据。
43. 作为 AI 代理，我想让行数请求被服务端硬上限裁剪而不是报错，以便查询总能返回有用的东西。
44. 作为 AI 代理，我想在查询超时时收到明确错误与建议，以便调整查询而不是重试同样的写法。
45. 作为 AI 代理，我想执行 `EXPLAIN`，以便理解一条查询的计划。
46. 作为 AI 代理，我想执行只读的 `SHOW` 语句（MySQL），以便探查服务器元数据。
47. 作为 AI 代理，我想执行只读的 `PRAGMA`（SQLite），以便探查引擎配置。
48. 作为 AI 代理，我想执行 `WITH ... SELECT` 形式的 CTE 查询，以便表达复杂逻辑。

### 只读保证

49. 作为运维者，我想让任何写操作（INSERT / UPDATE / DELETE / DDL）被拒绝，以便生产库不会被 AI 改动。
50. 作为运维者，我想让拒绝发生在**执行之前**，以便写语句根本不会到达数据库。
51. 作为运维者，我想让拒绝的措辞告诉 AI 只有 SELECT / EXPLAIN / SHOW 可用，以便它自己改用合法写法。
52. 作为运维者，我想让 `WITH ... INSERT` 这类伪装成查询的写操作被拒绝，以便不能用 CTE 绕过检查。
53. 作为运维者，我想让 `EXPLAIN ANALYZE <DML>` 被拒绝，以便不能借 EXPLAIN 执行写操作。
54. 作为运维者，我想让 `SELECT ... INTO OUTFILE` 被拒绝，以便不能借查询写文件。
55. 作为运维者，我想让 SQLite 的 `PRAGMA` 赋值形式被拒绝，以便不能借 PRAGMA 改引擎状态。
56. 作为运维者，我想让一次调用只能执行**一条**语句，以便不能堆叠语句夹带写操作。
57. 作为运维者，我想让注释隐藏的关键字也无处藏身，以便不能靠注释绕过解析。
58. 作为运维者，我想让引擎层的只读模式作为第二道防线，以便即使 Guard 漏过也被数据库拒绝。
59. 作为运维者，我想在文档里被明确告知"最小权限账号才是真正的边界"，以便不会把安全全押在应用层。

### 错误与诊断

60. 作为 AI 代理，我想让数据库自身的错误消息被原样透传，以便知道到底是表名错、列名错还是语法错。
61. 作为 AI 代理，我想在错误里看到 SQL 位置信息，以便定位语法问题。
62. 作为使用者，我想让文件路径在错误消息里被隐去，以便日志与截图不泄漏本机目录结构。
63. 作为使用者，我想让连接串在任何日志级别下都不被记录，以便凭据不进入日志、issue 或 CI 输出。
64. 作为使用者，我想让 SQL 语句被记录（默认 Info 级别），以便事后知道 AI 到底发了什么。
65. 作为使用者，我想让 SQL 的**参数值**不被记录，以便行数据不落到 stderr。
66. 作为使用者，我想用标准的 `Logging__LogLevel__Default` 调整日志级别，以便不必学一套私有开关。
67. 作为 AI 代理，我想让所有业务失败都以"错误结果"而非"协议异常"的形式返回，以便我能读到失败原因并自我纠正。

### 工具契约

68. 作为 AI 代理，我想让每个工具都有清晰的描述，以便在只看工具清单时就能判断该用哪个。
69. 作为 AI 代理，我想让每个工具的**参数**也有描述，以便不必试错就能正确调用。
70. 作为 AI 代理，我想让四个工具的名字稳定不变，以便缓存与复用调用方式。
71. 作为 AI 代理，我想让四个工具**总是全部**可用，以便不必处理"这个服务器少了个工具"的情况。
72. 作为 AI 代理，我想让工具声明自己是只读、非破坏性、幂等、封闭世界的，以便客户端据实判断风险。
73. 作为 AI 代理，我想在 `list_databases` 的描述里被告知服务器只绑定一个库，以便不会误以为能跨库查询。

### 扩展与演进

74. 作为将来的开发者，我想让"新增一种数据库"只需要写一个 Provider 项目并在装配处加一条分支，以便不必改动 Core。
75. 作为将来的开发者，我想让 Provider 契约是一个深接口而不是若干小接口，以便没有"没有消费者的组合问题"。
76. 作为将来的开发者，我想让 Core 不引用任何 Driver，以便它保持数据库无关。
77. 作为将来的开发者，我想让 Guard 固定用一个通用方言解析，以便 Provider 不必暴露方言。
78. 作为将来的开发者，我想让新增数据库类型时打包形态不变，以便版本号与安装方式对新类型同样适用。

### 运行特性

79. 作为使用者，我想让连接每次调用时现开现关，以便断线自愈与并发安全不需要额外代码。
80. 作为使用者，我想让服务器无状态，以便任意时刻重启都不丢东西。
81. 作为使用者，我想让服务器只支持 stdio，以便它作为本地子进程运行、不起网络监听。

## Implementation Decisions

### 领域模型（聚合边界）

单一 bounded context（单一 `GLOSSARY.md`）。**唯一聚合根是 `TableSchema`**：

| 概念 | 分类 | 说明 |
|---|---|---|
| `TableSchema` | **聚合根** | 携带真正的不变量：`Indexes` 不含主键、`PrimaryKey` 是有序列名、外键的表/列成对出现 |
| `ColumnInfo` / `ForeignKeyInfo` / `IndexInfo` | 子实体（聚合内） | 只在 `TableSchema` 内被引用，不单独存在 |
| `TableInfo` | read model | 无子实体、无不变量，是 `TableSchema` 的列表投影 |
| 查询结果（`columns` / `rows` / `truncated`） | 值对象 | 不可变、无标识 |
| Guard | 领域服务 | 无状态、纯函数（只读策略） |
| `IDatabaseProvider` | 端口 | ADR 0003 的单深接口；SQLite / MySQL 是其适配器 |
| `SharpDbOptions` | 值对象 | 启动期一次读取，不可变 |

**约束**：`TableSchema` 及其子对象**不可拆到多张票**——聚合必须一次定型。

### 项目拓扑（5 个生产项目）

| 项目 | 内容 | 引用 |
|---|---|---|
| `SharpDb.Abstractions` | Schema model（聚合）+ `IDatabaseProvider` | 无（不引 Driver、不引 parser） |
| `SharpDb.Core` | Guard、查询执行、`SharpDbOptions`、4 个 Tool | Abstractions + Dapper + SqlParserCS + ModelContextProtocol |
| `SharpDb.Provider.MySql` | MySQL Provider | Abstractions + MySqlConnector |
| `SharpDb.Provider.Sqlite` | SQLite Provider | Abstractions + Microsoft.Data.Sqlite |
| `SharpDb`（exe / global tool） | 装配、配置绑定、`PackAsTool` | Core + 两个 Provider |

Core 不引用任何 Driver（ADR 0004）；新增数据库类型不需要改 Core，只改 Host 的装配处（一行注册）。

### Provider 契约：单个深接口（ADR 0003）

`IDatabaseProvider` 五个成员；不拆成 `IDbConnectionFactory` + `ISchemaIntrospector` + `ISqlDialect`——这些部分从不独立变化。**Provider 不暴露方言**（ADR 0005）。

```csharp
public interface IDatabaseProvider
{
    string Type { get; }                                   // 匹配 SHARPDB__TYPE，如 "mysql" / "sqlite"
    Task<DbConnection> CreateReadOnlyConnectionAsync(string connectionString, CancellationToken ct);
    Task<IReadOnlyList<TableInfo>> ListTablesAsync(DbConnection conn, CancellationToken ct);
    Task<TableSchema?> DescribeTableAsync(DbConnection conn, string tableName, CancellationToken ct);
    Task<IReadOnlyList<string>> ListDatabasesAsync(DbConnection conn, CancellationToken ct);
}
```

*（此接口草图来自早期设计票，是决策的精确编码，故内联。）*

`CreateReadOnlyConnectionAsync` 返回**已打开、已进入引擎级只读**的连接（MySQL 在连上后执行会话只读；SQLite 由连接串 `Mode=ReadOnly` 保证）。

### Schema model（聚合根与子实体）

纯 POCO / record 置于 `SharpDb.Abstractions`，不引 Driver。要点：

- **数据类型用原始声明字符串**（MySQL `COLUMN_TYPE`；SQLite 声明 `type`，可能为空）——不加归一化 enum，AI 要的正是声明类型。
- **闭合集合用 enum**（`TableType`、`ForeignKeyAction`），**开放集合用 string**（数据类型）。
- v1 **不含** `Charset` / `Collation` / `FullDataType`。
- `IsAutoIncrement` 由 SQLite Provider **计算**（`pk = 1` + `type = 'INTEGER'`），MySQL 读 `EXTRA`。
- 索引只保留 `Name` / `IsUnique` / `Columns`；**排除主键**，避免与 `PrimaryKey` 重复表达。
- `ReferencedColumns` 在 SQLite 隐式指向父表主键时可为空列表，不报错。

### 只读：三层（ADR 0002）

| 层 | 归属 | 内容 |
|---|---|---|
| 1. Guard | Core（领域服务） | `SqlParserCS`（MIT, 0.6.5）解析；恰好一条语句；允许 `Select` / `Explain`（递归检查内层）/ `Show` 各变体；拒绝 `SELECT ... INTO`；拒绝逃逸函数（如 `LOAD_FILE(`、`GET_LOCK(`） |
| 2. 引擎级只读 | Provider（适配器） | MySQL `SET SESSION TRANSACTION READ ONLY`；SQLite 连接串 `Mode=ReadOnly` |
| 3. 最小权限账号 | 文档 | 不在代码中强制；README 明确说明这才是真正的边界 |

Guard 固定用 `SqlParser.Dialects.GenericDialect` + **超集**允许清单（ADR 0005）。已知缺口：`GenericDialect` 不接受 SQLite 的 `[方括号]` 标识符——接受此缺口（方括号是 SQL Server 风格，在 SQLite 中罕见）。

**关键字前缀启发式被否决**——已证实的绕过路径太多（注释隐藏关键字、`WITH ... INSERT`、`EXPLAIN ANALYZE` + DML、`SELECT ... INTO`、PRAGMA 赋值、堆叠语句）。

**PRAGMA 不在允许清单里**（见 ADR 0005）。票 03 逐条实测后确认：“只读 `Pragma`”无法按名区分，因为 `PRAGMA journal_mode('WAL')` 与 `PRAGMA user_version(42)` 都置 `IsEqual = false` 却真的写入。

**注意那条拒绝只针对 `execute_query`，管不到 Provider 的内省 SQL**——内省 SQL 本就不经过 Guard，且实测 PRAGMA 在只读连接上工作正常。Provider 用表值形式的真实理由是**能绑定参数**：`PRAGMA table_info(@table)` 报错，`SELECT * FROM pragma_table_info(@table)` 可用。表名因此不必拼字符串，也没有引号转义那一类 bug。

### Core 与 Provider 的职责分界

| 关注点 | 归属 |
|---|---|
| Guard（AST 只读检查） | Core |
| 引擎级只读 | Provider（烤进建连接过程） |
| 行数上限 / 截断 | Core（读到第 N 行停止，**不改写 SQL**） |
| 超时 | Core（`DbCommand.CommandTimeout`） |
| 用户查询参数 | Core（Dapper `DynamicParameters`） |
| 内省参数 | Provider（自写 SQL，SQLite 用 `pragma_table_info(?)` 等表值形式） |
| 错误成形 / 脱敏 | Core（Provider 让原生 `DbException` 冒泡） |
| `list_databases` | Provider 作答 |
| 连接串语法 | 无需单独校验——启动探测即是最完整的校验 |

### 连接所有权

**Core 持有、Provider 创建；每次 Tool 调用现开现关**（走 ADO.NET 连接池）。"单连接" = 单数据库，不是单连接对象。`DbConnection` 非线程安全，现开现关使并发安全与断线自愈免费获得，Provider 保持无状态。

### 配置面（ADR 0006 / 0007 / 0008）

配置**只**来自环境变量。绑定到单个 `SharpDbOptions`（放 Core），用 `GetSection("SHARPDB").Get<SharpDbOptions>()` 一次性读取——**不引入 `IOptions<T>`**。

| 键 | 默认 | 说明 |
|---|---|---|
| `SHARPDB__TYPE` | 必填 | `mysql` / `sqlite` |
| `SHARPDB__CONNECTIONSTRING` | 必填 | 由对应 Provider 的 Driver 解析 |
| `SHARPDB__DEFAULTMAXROWS` | `100` | 工具未显式传 `maxRows` 时使用 |
| `SHARPDB__MAXROWS` | `1000` | 硬上限，不可被工具参数突破 |
| `SHARPDB__TIMEOUTSECONDS` | `30` | 映射到 `DbCommand.CommandTimeout` |

**双下划线**是 .NET 配置系统的层级分隔符；看似自然的单下划线 `SHARPDB_TYPE` 是一个**不同的**环境变量、**静默不绑定**（ADR 0006）。

工具集**固定为 4 个**，无"只启用部分工具"的开关。

### 启动校验（ADR 0007）

五类错误**全部 fail-fast**：缺 `SHARPDB__TYPE`、未知 type、缺/空连接串、连接串无法解析、**连接失败**（凭据错 / 主机不可达 / 库不存在）。

输出契约：**exit code 1**；stderr **单行**、前缀 `sharpdb: `；**不打堆栈**；**绝不回显连接串**。

启动序列：读配置 → 校验 → 显式 `switch` 注册 Provider（**零反射**）→ **主动探测一次连接**（调 `CreateReadOnlyConnectionAsync` 开一条随即关闭）→ 进入 stdio 循环。

**lazy 连接被否决**：本地单用户工具，清晰的启动失败比"起来了但每次都失败"有用得多。

### Tool 契约（4 个）

四个工具的 annotations 统一：`ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false`。

**`execute_query`** —— `sql`（必填）+ `parameters`（可选 object）+ `maxRows`（可选 int，clamp 到 `SHARPDB__MAXROWS`）。

返回形状（来自 ticket 04 的 prototype，比散文更精确地编码了决策）：

```json
{ "columns": ["id", "name"], "rows": [[1, "Alice"], [2, null]], "truncated": true }
```

`truncated` 仅在为 true 时出现。类型表示：int/bool/decimal/null 用**原生 JSON**，datetime 用 **ISO 8601**，blob 用 **base64**。

**`list_tables`** —— 无参数。返回 `[{name, type: "table"|"view", comment?}]`，按名称排序；**包含视图**。MySQL 用 `TABLE_SCHEMA = DATABASE()`（只看当前库）；SQLite 用 `sqlite_schema`。

**`describe_table`** —— `table`（必填）。返回聚合根的单对象形态：`{name, type, comment?, columns[], primaryKey[], foreignKeys[], indexes[]}`。空值省略。`primaryKey` 是纯字符串数组；`indexes` **排除主键**；`ForeignKeyAction` 序列化为枚举名（`"Cascade"` 等）。表不存在时返回错误。

**`list_databases`** —— 无参数。MySQL 用 `SCHEMATA`；SQLite 用 `SELECT * FROM pragma_database_list`（→ `["main"]`）。**v1 不支持跨 schema 探查**（`list_tables` 不加 `database` 参数）——`list_databases` 只是信息性的。

### 错误返回机制（ADR 0010）

所有业务性失败（Guard 拒绝、语法错、超时、表不存在）**自己构造并返回** `CallToolResult{IsError = true}`，`Content` 是 `{error: string}` 的 JSON 文本。**不抛异常**。

理由：官方 SDK 的 catch 块会**丢弃非 `McpException` 的 `Message`**，只返回泛化文案；没有任何开关能改这个行为。要透出拒绝理由，必须自己构造错误结果。

错误消息内容：

- **Guard 拒绝** —— Core 生成：`"Statement rejected: INSERT is not allowed. Only SELECT, EXPLAIN and SHOW statements are permitted."`
- **DB 错误** —— **原样透传** + 前缀 `"Database error: "`。AI 需要表名/列名/SQL 位置来自我纠正。
- **超时** —— Core 生成：含秒数与建议。
- **脱敏** —— 保留表名/列名/SQL 位置/错误码；把文件路径替换为 `<path>`；连接串永不出现。

### 日志

默认 **Info** 级别，经标准 `Logging__LogLevel__Default` 调整。**SQL 语句记录**（含占位符，**不含参数值**）；**连接串在任何级别都不记录**；启动打一行 Info；工具调用失败打一行 Warning。所有日志走 **stderr**（`LogToStandardErrorThreshold`），stdout 保持协议纯净。

### 分发（ADR 0009）

**单个** global tool 包：`PackageId` = `Holwon.SharpDb.Mcp`，`ToolCommandName` = `sharpdb-mcp`，初始版本 `0.1.0`，`net8.0` + `PackAsTool`。

用户 `mcp.json` 里 `command` 直接写 shim 名、`args` 为空，数据库类型在 `env` 块里选择。README 含安装命令与两份可复制片段（MySQL / SQLite）+ "多数据库 = 多 server 条目"的说明。

**被否决**：每库一包。打包单位与工具面无关——agent 只看到 MCP server 和它的 4 个工具，看不到 NuGet 包。已知代价：可执行程序引用全部 Provider，所以只用 SQLite 也会连带装 MySqlConnector。

### 解决方案与 CI

用 **`.slnx`**（XML 格式；CLI 自 SDK 9.0.200 起支持；与目标框架正交）。放仓库根；**不与 `.csproj` 同目录**（`.slnx` 未被 SDK 默认忽略）。

CI = GitHub Actions **单 job**：`setup-dotnet` → `dotnet build SharpDb.slnx` → `dotnet test SharpDb.slnx`。v1 **不设** `dotnet format` 门禁（其对 `.slnx` 的支持未经证实，实施时实测）与 analyzers 硬门禁。

## Testing Decisions

### 什么算好测试

只测**外部行为**，不测实现细节。对本项目，具体含义是：

- Guard 的测试断言"这条 SQL 被接受 / 被拒绝 + 拒绝理由"，**不**断言 AST 的具体形状。
- Provider 的测试断言"这个库里有哪些表 / 这张表长什么样 / 这条查询返回什么"，**不**断言生成的内省 SQL 字符串。
- 错误测试断言 `IsError == true` 与消息内容，**不**断言抛了哪种异常（本来就不抛）。
- Host 的测试断言进程的 `ExitCode` 与 stderr 文本，**不**断言内部校验函数的返回值。

### 四个测试 seam

**1. Guard（Core，纯单元测试，无 DB）**

测 ticket 03 的完整绕过清单（每条一个用例）：注释隐藏关键字、`WITH ... INSERT`、`EXPLAIN ANALYZE` + DML、`SELECT ... INTO`、PRAGMA、堆叠语句、逃逸函数。另测允许清单的正例（`SELECT` / `EXPLAIN` / `SHOW` / CTE），以及拒绝文案的内容。

**2. `IDatabaseProvider`（Provider 集成测试，真库）**

- **SQLite**：**命名共享缓存内存库**（`Data Source={guid};Mode=Memory;Cache=Shared;Pooling=False`）+ 一条**保活连接**（keeper）保证库存活。关键：**传连接字符串，不传连接实例**——每次调用开自己的连接（与"现开现关"吻合）。fixture 先以**可写**连接 seed DDL，再让 Provider 以只读打开。每个测试一个独立库名（含 GUID），天然并行安全。
- **MySQL**：**Testcontainers.MySql**，镜像钉 **`mysql:8.0`**。容器启动成本高，用 collection fixture 共用一个容器，避免每测试起一个。
- **无 Docker / 无 MySQL 时 skip**，不 fail。
- 覆盖：内省 SQL → Schema model 映射（表/视图/列/主键/外键/索引）、查询执行、参数化（`@name`）、行限截断、`list_databases`。
- **契约测试共享套件**：把对 `IDatabaseProvider` 的行为断言抽成可复用套件，MySQL 与 SQLite 各跑一遍——这才真正验证 ADR 0003 的端口契约。

**已知残余风险（已接受）**：SQLite 引擎层面**忽略**内存库的 `SQLITE_OPEN_READONLY`（EF Core issue #15118，closed as not planned）；Microsoft.Data.Sqlite 里 `Mode=ReadOnly` 与 `Mode=Memory` 是**互斥分支**。因此 ADR 0002 第 2 层在 SQLite 侧**无法用集成测试覆盖**。补救（不碰文件、不碰数据库）：加一个断言，验证 Provider 产出的连接串里 `Mode == ReadOnly`。

**3. MCP server（in-process `McpClient` over pipes，薄 smoke）**

只覆盖：工具列表 = 4 个；四个工具的 annotations 正确；每个工具至少一次真实调用成功；一条拒绝路径返回 `IsError = true`。深度行为测试留在服务层——端到端慢且脆，它的价值在"整条链路能通"。

**4. Host 启动契约（进程级薄 smoke，约 5 例）**

真启动服务器进程配不同 env，断言 **`ExitCode`**、**stderr 内容**、**stdout 纯净**。覆盖：缺 type / 未知 type / 缺连接串 / 连接失败（各一例）+ 一条 stderr 不泄漏连接串的断言。

### 测试项目布局

**镜像生产项目层级**（ABP 风格 / DDD）：

| 测试项目 | 镜像 | 覆盖 seam |
|---|---|---|
| `SharpDb.Core.Tests` | `SharpDb.Core` | 1（Guard）+ 3（MCP） |
| `SharpDb.Provider.Sqlite.Tests` | `SharpDb.Provider.Sqlite` | 2 |
| `SharpDb.Provider.MySql.Tests` | `SharpDb.Provider.MySql` | 2 |
| `SharpDb.Tests` | `SharpDb`（exe） | 4（Host） |

`SharpDb.Abstractions` **无测试项目**——纯接口 + record，没有行为可测。目录内按关注点分组：`Guard/`、`Introspection/`、`Execution/`、`Mcp/`。

好处：MySQL 测试天然隔离在 `SharpDb.Provider.MySql.Tests`，无 Docker 时的 skip 范围自然收窄。

### 框架与命令

**xUnit**（.NET 8 生态默认，与 in-process 测试风格契合）。统一命令：`dotnet test SharpDb.slnx`。

### 先例

仓库是绿地，**没有既有测试可作先例**——本 spec 定义的是第一套。TDD 从 Guard 开始最划算：它是纯函数、无 I/O、用例清单已在 ticket 02 列全，是天然的 red-green-refactor 起点。

## Out of Scope

- **写操作**（INSERT / UPDATE / DELETE / DDL）——v1 只读是刻意决定（ADR 0001）。将来若需要，应作为独立版本重新设计权限与确认流程。
- **从连接串自动探测数据库类型**——明确拒绝；类型是配置的。
- **单实例多连接**——一个服务器、一个数据库；多库靠多进程。
- **MySQL 与 SQLite 之外的数据库类型**——*机制*在范围内，*实现*不在。
- **NativeAOT / 自包含单文件分发**——v1 是 .NET global tool。
- **远程 / HTTP MCP 传输**——v1 只有 stdio。
- **`.slnx` 之外的第二套构建入口**——只用 CLI，不额外维护 VS 专属配置。
- **`dotnet format` / analyzers 门禁**——v1 不加（见上）。
- **跨 schema 探查**（`list_tables` 带 `database` 参数）——v1 不做。

**Fog（在范围内，但尚未锐化到可开票）**：

- **MCP resources vs prompts** —— 结构是否**同时**以 MCP resources 形式暴露（如 `sharpdb://tables`），还是只有 tools。
- **大结果处理** —— 查询返回很多行时的分页 / 流式 / 截断策略。`{columns, rows}` 的 token 优势帮不上忙，它不能替代尺寸上限。

## Further Notes

### 已定的 10 条 ADR

| # | 决定 |
|---|---|
| 0001 | v1 只读，不提供写操作 |
| 0002 | 只读由三层共同保证 |
| 0003 | Provider 是单个深接口 |
| 0004 | Core 不引用 Driver，Driver 由 Provider 拥有 |
| 0005 | Guard 固定用 GenericDialect，Provider 不暴露方言 |
| 0006 | 配置项用 `SHARPDB__` 双下划线前缀 |
| 0007 | 启动期配置错误与连接失败一律 fail-fast |
| 0008 | 配置只来自环境变量 |
| 0009 | 分发为单个 global tool 包 |
| 0010 | 工具错误以 `CallToolResult{IsError}` 返回，不抛异常 |

### 依赖全集

**生产**：Dapper、MySqlConnector、Microsoft.Data.Sqlite、SqlParserCS（`0.6.5`，MIT）、ModelContextProtocol（`2.2.0`）。

**测试**：xUnit、Testcontainers.MySql（**仅测试项目**，不进发布包）。

### 已接受的代价与缺口（知情决定，不要"顺手修好"）

1. **只用 SQLite 也会装 MySqlConnector** —— 单包的代价（ADR 0009）。
2. **SQLite 的 `Mode=ReadOnly` 测不到** —— 引擎忽略内存库的只读 flag；靠连接串断言兜底。
3. **SQLite 方括号标识符会被 Guard 误拒** —— `GenericDialect` 不接受 `[name]`；已接受（罕见）。
4. **CI 里 MySQL 测试可能静默跳过** —— 无 Docker 时 skip 而非 fail；缓解是 GitHub runner 自带 Docker。
5. **`.slnx` 生态尚不成熟** —— `dotnet format` 支持存疑、`.slnx` 未被 SDK 默认忽略（须放仓库根）。
