Labels: ready-for-agent

# sharpdb-db2: IBM Db2 LUW Provider

## Problem Statement

SharpDb 目前支持六种数据库（MySQL、PostgreSQL、SQLite、Oracle、SQL Server、MariaDB）。用户需要把 SharpDb 接入一个以 IBM Db2 LUW（Linux/Unix/Windows）为持久层的系统，让 AI 代理能以同样的只读方式理解其结构并执行查询。

Db2 LUW 与现有六种引擎有一处**结构性**差异，且是本次新增引擎的核心：它**没有引擎层只读**——比 SQL Server 更极端。SQL Server 至少有 `ApplicationIntent=ReadOnly`（虽然在 standalone 上不强制，但表达了意图）；Db2 连这个都没有。`SET TRANSACTION READ ONLY` 不存在；`FOR READ ONLY` 子句只是游标提示，不阻止 DML；连接字符串也没有只读路由关键字。

这意味着 Db2 的 `CreateReadOnlyConnectionAsync` 只能**原样打开连接**——不加任何只读修饰。三层只读防御在 Db2 上退化为两层：Guard（应用层 SQL 解析）+ 最小权限账号。这是所有已支持引擎中 Layer 2 最"裸"的形态。

其余差异（`SYSCAT` 目录视图、`:name` 占位符、大写标识符折叠、`FETCH FIRST n ROWS ONLY`）都是可处理的常规分歧。SqlParserCS 0.6.5 没有 Db2 方言，需要照搬 Oracle 的降级模式（`GenericDialect`）。

## Solution

1. 新增一个 **Db2 Provider**，实现 `IDatabaseProvider` 全部成员，驱动用 `Net.IBM.Data.Db2`（IBM 官方当前活跃的 .NET 包，目标 .NET 10 向下兼容 .NET 8+）。
2. **Layer 2 完全缺席**：Provider 在 `CreateReadOnlyConnectionAsync` 里只做 `new Db2Connection(connectionString)` + `OpenAsync`，不加任何只读修饰。新增 ADR 记录：Db2 LUW 没有 session/事务/连接级只读机制，唯一挡在写前面的是连接所用账号的权限。与 SQL Server 的 ADR 0016 同形但更极端——连 intent 都没有。
3. **Core 加一个方言值**：`SqlDialect` 新增 `Db2` 值，在 `ReadOnlyGuard.DialectFor()` 里映射到 `GenericDialect`（SqlParserCS 0.6.5 无 Db2 方言，与 Oracle 同形，ADR 0015）。
4. **占位符翻译复用 Oracle 路径**：Db2 驱动接受 `:name` 语法，因此 `PlaceholderTranslation.ForDialect` 的翻译条件从 `== Oracle` 扩展为 `is Oracle or Db2`，复用已有的 `@name` → `:name` 翻译。
5. **内省读 `SYSCAT` 目录视图**：`SYSCAT.TABLES`（表/视图）、`SYSCAT.COLUMNS`（列）、`SYSCAT.TABCONST` + `SYSCAT.KEYCOLUSE`（主键）、`SYSCAT.REFERENCES` + `SYSCAT.KEYCOLUSE`（外键）、`SYSCAT.INDEXES` + `SYSCAT.INDEXCOLUSE`（索引）。与 PostgreSQL 选目录而非 `information_schema` 同理：自增识别、注释、复合外键列配对顺序都只有目录视图拿得到。
6. **Schema 过滤用 `CURRENT SCHEMA`**：`ListTablesAsync` 只报当前默认 schema 下的表/视图，与 Oracle 的 `owner = USER` 同形。
7. **标识符大写折叠**：Db2 未加引号的标识符折为大写（与 Oracle 同形）。Provider 用 `NormalizedName` 把全大写名折成小写报给 AI 消费方；含小写的名字（来自引号标识符）原样返回。
8. **Denied functions 先空集**：Guard 本身已拒绝所有非 SELECT 语句，denied functions 只防"SELECT 里的逃逸口"。Db2 上没有"只在只读会话下可用、且 Guard 挡不住"的函数被实测出来；后续发现再补。
9. **测试用手动 Docker 管理**：Testcontainers 没有 Db2 包，测试 fixture 用 `docker run icr.io/db2_community/db2` 手动管理容器（`--privileged`、`LICENSE=accept`、等待 "Setup has completed"）。Docker 不可用时测试 skip（不 fail）。
10. **文档与 ADR**：新增 ADR 0019（Db2 Layer 2 缺席）；`README` 能力矩阵加 Db2 列；示例报错文案加新类型名。

## User Stories

### MCP 客户端（AI 代理）

1. As an MCP client, I want to start the server pointed at a Db2 database, so that I can query it through the same four Tools I use for the other engines.
2. As an MCP client, I want `SHARPDB__TYPE=db2` to select the engine, so that switching engines does not require a different install.
3. As an MCP client, I want `list_tables` on Db2 to report every table and view in my current schema, so that I know what I can ask about.
4. As an MCP client, I want each listed object to say whether it is a table or a view, so that I do not guess.
5. As an MCP client, I want `describe_table` on Db2 to report columns, primary key, foreign keys and indexes, so that I can write correct queries without guessing column names.
6. As an MCP client, I want the names `list_tables` gives me to be names I can use unqualified in my current schema, so that I never get a resolution error for a name the server itself offered.
7. As an MCP client, I want `execute_query` on Db2 to accept `@name` placeholders exactly as on the other engines, so that I do not rewrite my queries when the engine changes.
8. As an MCP client, I want `FETCH FIRST n ROWS ONLY` to be accepted by the Guard, so that ordinary Db2 read-only SQL is not refused.
9. As an MCP client, I want a write attempt to be refused by the engine when the connection's account has no write permission, so that the server's read-only promise holds even if the Guard is bypassed.
10. As an MCP client, I want a query that exceeds the configured timeout to be interrupted, so that a slow statement does not hang me.
11. As an MCP client, I want a query returning no rows to still name its columns, so that I know the statement was understood.
12. As an MCP client, I want a statement the database refuses to reach me as a database failure in the database's own words, so that I can act on it.
13. As an MCP client, I want a failed call not to repeat the connection string's secret, so that an error message never leaks a credential.
14. As an MCP client, I want `list_databases` on Db2 to report the current database name, so that I know which database I am connected to.
15. As an MCP client, I want `describe_table` for a name that does not exist to be reported as not found rather than as an error, so that a wrong guess is an answer.
16. As an MCP client, I want a table's and a column's comment (`REMARKS`) reported, so that I understand what the schema means rather than only its shape.
17. As an MCP client, I want an `IDENTITY` or `GENERATED AS IDENTITY` column reported as auto-increment, so that I know which columns the engine fills in.
18. As an MCP client, I want a composite foreign key's local and referenced columns paired in key order, so that I can write the join correctly.
19. As an MCP client, I want an index's uniqueness and its column order reported, so that I can predict query behaviour.
20. As an MCP client, I want a view's columns described like a table's, so that I can query a view without reading its definition.
21. As an MCP client, I want Db2's uppercase identifier folding to be transparent — names reported in lowercase so that I can use them as-is in unquoted SQL.

### 人类运维

22. As a human operator, I want to configure the server for Db2 with `SHARPDB__TYPE=db2` and an ordinary Db2 connection string, so that switching engines does not require a different install.
23. As a human operator, I want the server to fail fast at startup if the Db2 database is unreachable, so that a misconfiguration is caught before the first Tool call.
24. As a human operator, I want the README to state which Db2 versions are supported, so that I do not deploy against an unsupported version.
25. As a human operator, I want the README to state that Db2 has no engine-level read-only mode, so that I understand what actually stands between a bypassed Guard and a write.
26. As a human operator, I want the README to list Db2's semantic differences from the other engines in the same capability matrix, so that I can brief my team on what to expect.
27. As a human operator, I want the README to explain that the account needs only `SELECT` privilege, so that introspection does not silently report fewer objects than exist.
28. As a human operator, I want the server's NuGet package to keep serving every engine from one install, so that I do not choose a package per database.
29. As a human operator, I want the server to work on a machine with only the .NET runtime, so that deployment stays lightweight.

### Db2 数据库管理员

30. As a Db2 DBA, I want to decide for myself which account the server connects with — including a least-privilege one — so that how much power the server has is mine to bound, not something the tool imposes.
31. As a Db2 DBA, I want to be told plainly that the engine has no read-only mode to set, so that I enforce the boundary through the account's privileges rather than assuming the server did.
32. As a Db2 DBA, I want the server's introspection to use only `SYSCAT` views reachable by an ordinary account, so that it needs no elevated role.
33. As a Db2 DBA, I want the server's connection to not hold any locks beyond the duration of a single Tool call, so that my write workload is not blocked.
34. As a Db2 DBA, I want the server not to require access to system schemas for ordinary introspection, so that the account stays scoped to its schema.

### 开发者（仓库维护者）

35. As a developer, I want the Db2 Provider to live in its own project that references only Abstractions, so that the layering rules (ADR 0004) hold.
36. As a developer, I want the shared contract suite to contain only assertions every engine can satisfy, so that adding a new engine never forces a lie into the suite.
37. As a developer, I want the Core changes limited to a dialect value, its mapping, and placeholder translation extension, so that "a new engine = a new Provider project" stays true.
38. As a developer, I want the engine's missing layer 2 recorded as a decision, so that the three-layer model stays honest rather than being quietly weakened.
39. As a developer, I want the new engine to leave the six existing engines' behaviour untouched, so that adding it is additive.
40. As a developer, I want the Db2 tests to skip gracefully when Docker is unavailable, so that CI without Docker does not fail.

## Implementation Decisions

### 模块

- **新增 Provider 项目** `SharpDb.Provider.Db2`：实现 `IDatabaseProvider` 全部成员（`Type` / `Policy` / `CreateReadOnlyConnectionAsync` / `ListTablesAsync` / `DescribeTableAsync` / `ListDatabasesAsync`）。只引用 Abstractions 与驱动 `Net.IBM.Data.Db2`。
- **新增 Provider 的只读策略**：一个 `ReadOnlyPolicy` 值——声明方言值 `SqlDialect.Db2` 与空的禁用函数集。策略只能往禁用表里**加**名字，不能放宽许可清单（ADR 0013）。
- **新增测试项目** `SharpDb.Provider.Db2.Tests`：承载引擎专属套件与共享套件的具体运行。
- **`SharpDb.Abstractions` 修改**：`SqlDialect` 枚举新增 `Db2` 值。
- **`SharpDb.Core` 修改**：`ReadOnlyGuard.DialectFor()` 新增 `SqlDialect.Db2 => new GenericDialect()` 分支；`PlaceholderTranslation.ForDialect` 翻译条件扩展为 `dialect is SqlDialect.Oracle or SqlDialect.Db2`。Core 是唯一引用解析器的项目（ADR 0005）。
- **`SharpDb`（Host）修改**：`ProviderCatalog` 构造多注册一个 `Db2DatabaseProvider`；`SharpDb.csproj` 加 `ProjectReference`。
- **文档修改**：`README` 能力矩阵加 Db2 列；新增 ADR 0019。

### 接口契约

- `SHARPDB__TYPE` 的值为 **`db2`**（大小写不敏感匹配，与现有六个同形）。这是**已发布的公开契约**：发布后改名会破坏用户既有的 `mcp.json`。
- 连接串是**普通 Db2 连接串**，原样粘贴即可。Db2 没有只读 intent 关键字可加——Provider 原样打开连接。
- 占位符：用户端统一用 `@name`，Core 翻译成 `:name` 后交给 Db2 驱动（复用 Oracle 的翻译路径）。

### Layer 2 只读

- Db2 LUW **没有** session/事务/连接级只读机制。`SET TRANSACTION READ ONLY` 不存在；`FOR READ ONLY` 是游标提示；连接字符串无 `ApplicationIntent` 等价物。
- `CreateReadOnlyConnectionAsync` 只做 `new Db2Connection(connectionString)` + `OpenAsync`，不加任何只读修饰。
- 三层防御在 Db2 上退化为两层：Guard + 账号权限。唯一挡在写前面的是连接所用账号的权限。
- 这是**引擎的事实，不是 SharpDb 对用户的要求**：用哪个账号由运维方决定，SharpDb 不检查、不校验、也不改写用户的连接串。

### 方言与占位符

- `SqlDialect.Db2` 映射到 `GenericDialect`（SqlParserCS 0.6.5 无 Db2 方言，与 Oracle 同形，ADR 0015）。
- 占位符翻译：`@name` → `:name`（Db2 驱动接受 `:name` 语法），复用 Oracle 已有的翻译路径。
- `GenericDialect` 对 Db2 SQL 的解析覆盖率需实测（Oracle 是 38/43）；预期不会更低，因为 Db2 的 SQL 语法更接近标准 SQL。

### 内省 SQL

- **表/视图**：`SYSCAT.TABLES WHERE TABSCHEMA = CURRENT SCHEMA AND TYPE IN ('T', 'V')`，按 `TABNAME` 排序。`TYPE` 值：`'T'` = 表，`'V'` = 视图。
- **列**：`SYSCAT.COLUMNS WHERE TABSCHEMA = ? AND TABNAME = ? ORDER BY COLNO`。`NULLS` 列是字符串 `'Y'`/`'N'`；`DEFAULT` 列含默认值文本；`IDENTITY` 列识别自增。
- **主键**：`SYSCAT.TABCONST`（`TYPE = 'P'`）+ `SYSCAT.KEYCOLUSE`（按 `COLSEQ` 排序）。
- **外键**：`SYSCAT.REFERENCES` + `SYSCAT.KEYCOLUSE`（FK 侧与引用侧各按 `COLSEQ` 排序，然后按位置对齐）。`DELETERULE` / `UPDATERULE` 映射到 `ForeignKeyAction`。
- **索引**：`SYSCAT.INDEXES` + `SYSCAT.INDEXCOLUSE`（按 `COLSEQ` 排序），排除主键索引（`UNIQUERULE = 'U'` 且名字匹配主键约束名）。`UNIQUERULE` 值：`'U'` = 唯一，`'D'` = 允许重复。
- **注释**：各目录视图的 `REMARKS` 列。
- **名字大小写**：`NormalizedName` 与 Oracle 同形——全大写折小写，含小写则原样返回。

### 测试

- 手动 Docker 管理：`docker run icr.io/db2_community/db2`（`--privileged`、`LICENSE=accept`、`DB2INST1_PASSWORD`、`DBNAME`），等待 "Setup has completed" 日志（超时 ~5 分钟）。
- Docker 不可用时 `Available = false`，测试 skip（不 fail）。
- Seed DDL 逐条执行（与 Oracle/MsSql 同形，不假设多语句支持）。
- 共享契约套件只保留每个引擎都能满足的最小公倍数；Db2 不能直接满足的断言下沉到引擎专属测试。

## Testing Decisions

### 测试什么

- **外部行为，不是实现细节**：测试 Provider 的 `IDatabaseProvider` 接口行为（连接、内省、查询结果），不测试内部 SQL 怎么写。
- **共享契约套件**：`ProviderContractSuite` 与 `QueryContractSuite` 的具体运行，断言四个 Tool 在 Db2 上的行为与其他引擎一致（类型、连接状态、内省结构、查询结果、超时、错误消息）。
- **引擎专属测试**：Db2 特有行为（大写折叠、`SYSCAT` 目录、`FETCH FIRST` 语法、`GENERATED AS IDENTITY` 自增）。
- **Policy 单元测试**：`Db2ReadOnlyPolicy` 的方言值与禁用函数集。

### 测试模块

- `SharpDb.Provider.Db2.Tests`：承载所有 Db2 测试。
- `SharpDb.Provider.Contract`：共享契约套件（不修改，只继承）。

### 先例

- Oracle 测试项目（`SharpDb.Provider.Oracle.Tests`）：Testcontainers 管理 + warm-up 重试。
- MsSql 测试项目（`SharpDb.Provider.MsSql.Tests`）：手动 Docker + 权限设置 + digest pin 镜像。
- Db2 走 MsSql 的手动 Docker 路线（无 Testcontainers 包可用）。

## Out of Scope

- **Db2 for i / Db2 for z/OS**：本 spec 只覆盖 Db2 LUW（Linux/Unix/Windows），Docker Community Edition。for i（AS/400）和 for z/OS（大型机）的目录视图与 SQL 方言有差异，不在本次范围内。
- **Db2 特有函数禁用**：Denied functions 初版为空集。后续若实测发现 Db2 上有"只在只读会话下可用、且 Guard 挡不住"的函数（如 `SYSPROC.ADMIN_CMD`），再补进 Policy。
- **GenericDialect 覆盖率修复**：若实测发现 `GenericDialect` 对 Db2 SQL 的解析覆盖率低于预期，不升级 SqlParserCS 或自写 Db2 方言——照搬 Oracle 的"接受误拒"策略（功能损失，安全无损，ADR 0005）。
- **CI 上的 Db2 测试**：GitHub Actions Ubuntu runner 上 `Net.IBM.Data.Db2` 是否能加载未确认。最坏情况：CI 上 Db2 测试全部 skip（不影响其他引擎）。
- **Db2 Connect license**：`Net.IBM.Data.Db2` 驱动连接 LUW 服务器时需要 Db2 Connect v12.1 license。Docker Community Edition 自带客户端许可，因此测试环境不需要额外 license；生产环境由用户自行处理。

## Further Notes

### Driver 版本选择

`Net.IBM.Data.Db2` 最新 10.0.0.300 目标 .NET 10，需确认是否 multi-target .NET 8。如果不兼容，退到 8.0.0.x 版本（该包有 .NET 8 专用版本）。

### Docker 镜像

当前 Db2 Community Edition 的 Docker 镜像在 `icr.io/db2_community/db2`（旧位置 `ibmcom/db2` 即将下线）。镜像仅支持 **amd64** 架构。

### 资源需求

Db2 容器启动需要 ~2 GB 内存，上限 16 GB（Community Edition 硬限制）。启动较慢（几分钟，看到 "Setup has completed" 才就绪）。端口 50000。

### 外键列映射复杂度

Db2 的外键列映射比大多数引擎复杂：`SYSCAT.REFERENCES` 给出表级关系，列级配对需要配合 `SYSCAT.KEYCOLUSE`，FK 侧与引用侧的 `COLSEQ` 是独立的，需要按位置对齐。实现时实测确认。

### 与 SQL Server ADR 0016 的关系

Db2 的 Layer 2 缺席与 SQL Server 同形，但更极端——连 `ApplicationIntent` 都没有。ADR 0019 应引用 ADR 0016 的模型（`Read-only intent` vs `Read-only enforcement`），并记录 Db2 上连 intent 都不存在的事实。

### 占位符翻译的对称性

Oracle 与 Db2 共享同一条翻译路径（`@name` → `:name`），这是 `PlaceholderTranslation.ForDialect` 的第一次"多引擎共享翻译"。未来若有第三个引擎也用 `:name`，翻译条件继续扩展即可；若有引擎用不同语法（如 `?` 位置参数），则需新增独立分支。
