Labels: ready-for-agent

# sharpdb-mssql: SQL Server Provider

## Problem Statement

SharpDb 目前支持四种数据库（MySQL、PostgreSQL、SQLite、Oracle）。用户需要把 SharpDb 接入一个以 SQL Server 为持久层的系统，让 AI 代理能以同样的只读方式理解其结构并执行查询。

SQL Server 与现有四种引擎有一处**结构性**差异，且是本次新增引擎的核心：它**没有引擎层只读**。`SET TRANSACTION ISOLATION LEVEL` 只有五个隔离级别、没有 READ ONLY；`ApplicationIntent=ReadOnly` 是 Always On 的只读**路由**开关，只有目标真是可读副本时才是硬边界，standalone 实例上被接受但什么都不强制。前四种引擎各自都有一句真正生效的第 2 层（MySQL/PG 会话级、SQLite 文件级、Oracle 事务级），SQL Server 一句都没有。

这意味着"加一个 Provider"不只是加一个项目：它第一次让只读三层里**第 2 层缺席**，因此必须把这个事实写进领域模型（新术语），并诚实地记下它的后果：在本引擎上，"即使 Guard 被绕过、数据库仍会拒绝写"这句话**不成立**——唯一挡在写前面的是连接所用账号的权限。**这是引擎的事实，不是 SharpDb 对用户的要求**：用哪个账号由运维方决定，SharpDb 不检查、不校验、也不改写用户的连接串。同时把共享契约套件里唯一直接验证第 2 层的那条断言诚实地让给真有第 2 层的引擎。其余差异（`[bracket]` 标识符、`sys.*` 目录、schema 限定名、rowset function）都是可处理的常规分歧。

## Solution

1. 新增一个 **SQL Server Provider**，实现 `IDatabaseProvider` 全部成员，驱动用 `Microsoft.Data.SqlClient`（MIT、net8.0；自 7.0 起 Entra/Azure 认证被拆到独立包，基础包因此比 5.x 更瘦）。
2. **第 2 层如实降级为"意图"**：Provider 在 `CreateReadOnlyConnectionAsync` 里用 `SqlConnectionStringBuilder` 把 `ApplicationIntent=ReadOnly` 写进连接串再打开连接。**不假装它挡住写**。新增 ADR 记录：这一层只在目标是 Always On 可读副本时成立，standalone 上是 no-op，因此它**不等于** enforcement。ADR 只陈述这个事实及其后果（本引擎上唯一挡写的是账号权限），**不向用户提出任何要求**——SharpDb 把用户写的连接串原样拿去用，不检查、不校验、不改写；与 Oracle 的 ADR 0014 同构（都是"第 2 层有缺口，因此账号才是真正的边界"）。
3. **Core 加一个方言值**：`SqlDialect` 新增一个 SQL Server 值，在 `ReadOnlyGuard` 的方言映射里指向解析器自带的 MS SQL 方言（`SqlParserCS 0.6.5` 确有 `MsSqlDialect`）。收益：`TOP n` 与 `[bracket]` 标识符能被解析——后者在通用方言下是被拒的，既有测试把它记为"可接受的缺口"，本引擎让该缺口闭合。
4. **禁用函数表加四个 rowset function**：`OPENROWSET` / `OPENQUERY` / `OPENDATASOURCE` / `OPENXML`。它们是"读本机文件/读另一台服务器"的入口，与 PostgreSQL 的 `pg_read_file` 同类；**正因为第 2 层不挡写，第 1 层在本引擎上更重要**。`xp_cmdshell` / `sp_executesql` 走 `EXEC`，而 `EXEC` 不在许可清单里，已被拒，不入表。
5. **内省读 `sys.*` 目录视图**，与 PostgreSQL 选目录而非 `information_schema` 同理：`sys.identity_columns`（自增）、`sys.extended_properties` 的 `MS_Description`（注释）、`sys.foreign_key_columns.constraint_column_id`（复合外键列配对顺序）、`sys.indexes` / `sys.index_columns`（唯一性、列序、主键索引）都只有目录视图拿得到。
6. **报名字集合 = 引擎自己解析得动的名字集合**：`list_tables` 对默认 schema 的对象报**裸名**，对非默认 schema 的对象报 `schema.name` 限定名；`list_databases` 排除 `master` / `tempdb` / `model` / `msdb`。这不是自创规则，正是 SQL Server 自己的名字解析语义（默认 schema 优先），也是 PostgreSQL Provider 已写下的理由。新增 ADR 记录这条规则。
7. **诚实收缩共享契约套件**：`The_connection_a_provider_opens_refuses_a_write` 从共享套件下沉到 MySQL / PostgreSQL / SQLite / Oracle 四个引擎各自的测试项目（覆盖零丢失，只是换位置——Oracle 先例）。共享套件只保留每个引擎都能自己满足的断言。
8. **文档与 ADR**：新增两张 ADR（第 2 层缺席 / 报名字集合规则）；`GLOSSARY.md` 新增术语 `Read-only intent`；`ADR 0002` 的第 2 层清单补一行；`ADR 0005` 复核 `[bracket]` 缺口；`README` 补 env 示例、能力矩阵加一整列、示例报错文案加新类型名。

## User Stories

### MCP 客户端（AI 代理）

1. As an MCP client, I want to start the server pointed at a SQL Server database, so that I can query it through the same four Tools I use for the other engines.
2. As an MCP client, I want `SHARPDB__TYPE=mssql` to select the engine, so that switching engines does not require a different install.
3. As an MCP client, I want `list_tables` on SQL Server to report every table and view I can see, so that I know what I can ask about.
4. As an MCP client, I want each listed object to say whether it is a table or a view, so that I do not guess.
5. As an MCP client, I want `describe_table` on SQL Server to report columns, primary key, foreign keys and indexes, so that I can write correct queries without guessing column names.
6. As an MCP client, I want the names `list_tables` gives me to be names I can use unqualified, so that I never get a resolution error for a name the server itself offered.
7. As an MCP client, I want an object outside the default schema to be reported with its schema, so that I know to qualify it rather than guess.
8. As an MCP client, I want `execute_query` on SQL Server to accept `@name` placeholders exactly as on the other engines, so that I do not rewrite my queries when the engine changes.
9. As an MCP client, I want `TOP n` to be accepted by the Guard, so that ordinary SQL Server read-only SQL is not refused.
10. As an MCP client, I want `[bracketed]` identifiers to be accepted by the Guard, so that ordinary SQL Server SQL is not refused.
11. As an MCP client, I want a rowset function that reaches outside the database (`OPENROWSET`, `OPENQUERY`, `OPENDATASOURCE`, `OPENXML`) to be refused with a clear reason, so that a read-only query cannot read the server's files or another server.
12. As an MCP client, I want a write attempt to be refused by the engine when the connection's account has no write permission, so that the server's read-only promise holds even if the Guard is bypassed.
13. As an MCP client, I want the connection to carry read-only intent, so that on an Always On readable secondary the engine itself refuses a write.
14. As an MCP client, I want a query that exceeds the configured timeout to be interrupted, so that a slow statement does not hang me.
15. As an MCP client, I want a query returning no rows to still name its columns, so that I know the statement was understood.
16. As an MCP client, I want a statement the database refuses to reach me as a database failure in the database's own words, so that I can act on it.
17. As an MCP client, I want a failed call not to repeat the connection string's secret, so that an error message never leaks a credential.
18. As an MCP client, I want `list_databases` on SQL Server to report the databases I can see minus the server's own four, so that the answer holds only names I could mean.
19. As an MCP client, I want `describe_table` for a name that does not exist to be reported as not found rather than as an error, so that a wrong guess is an answer.
20. As an MCP client, I want a table's and a column's comment (`MS_Description`) reported, so that I understand what the schema means rather than only its shape.
21. As an MCP client, I want an `IDENTITY` column reported as auto-increment, so that I know which columns the engine fills in.
22. As an MCP client, I want a composite foreign key's local and referenced columns paired in key order, so that I can write the join correctly.
23. As an MCP client, I want an index's uniqueness and its column order reported, so that I can predict query behaviour.
24. As an MCP client, I want a view's columns described like a table's, so that I can query a view without reading its definition.

### 人类运维

25. As a human operator, I want to configure the server for SQL Server with `SHARPDB__TYPE=mssql` and an ordinary SqlClient connection string, so that switching engines does not require a different install.
26. As a human operator, I want the server to fail fast at startup if the SQL Server database is unreachable, so that a misconfiguration is caught before the first Tool call.
27. As a human operator, I want the README to state which SQL Server versions are supported, so that I do not deploy against an unsupported version.
28. As a human operator, I want the README to state that SQL Server has no engine-level read-only mode, so that I understand what actually stands between a bypassed Guard and a write.
29. As a human operator, I want the README to list SQL Server's semantic differences from the other engines in the same capability matrix, so that I can brief my team on what to expect.
30. As a human operator, I want the README to explain that a self-signed certificate needs `TrustServerCertificate=True`, so that I can connect to a development server without disabling encryption.
31. As a human operator, I want the README to explain that the account needs `SELECT` plus `VIEW DEFINITION`, so that introspection does not silently report fewer objects than exist.
32. As a human operator, I want the server's NuGet package to keep serving every engine from one install, so that I do not choose a package per database.
33. As a human operator, I want the server to work on a machine with only the .NET runtime, so that deployment stays lightweight.

### SQL Server 数据库管理员

34. As a SQL Server DBA, I want to decide for myself which account the server connects with — including a least-privilege one — so that how much power the server has is mine to bound, not something the tool imposes.
35. As a SQL Server DBA, I want to be told plainly that the engine has no read-only mode to set, so that I enforce the boundary through the account's privileges rather than assuming the server did.
36. As a SQL Server DBA, I want the server's introspection to use only catalogue views reachable by an ordinary account, so that it needs no elevated role.
37. As a SQL Server DBA, I want to know that metadata visibility is permission-filtered, so that I grant `VIEW DEFINITION` deliberately rather than wonder why objects are missing.
38. As a SQL Server DBA, I want the server's connection to hold no lock beyond the duration of a single Tool call, so that my write workload is not blocked.
39. As a SQL Server DBA, I want the server not to require `master` access for ordinary introspection, so that the account stays scoped to its database.

### 开发者（仓库维护者）

40. As a developer, I want the SQL Server Provider to live in its own project that references only Abstractions, so that the layering rules (ADR 0004) hold.
41. As a developer, I want the shared contract suite to contain only assertions every engine can satisfy, so that adding a new engine never forces a lie into the suite.
42. As a developer, I want the write-refusal assertion to still run against the four engines that can satisfy it, so that coverage does not regress.
43. As a developer, I want the Core changes limited to a dialect value, its mapping, and denied function names, so that "a new engine = a new Provider project" stays true.
44. As a developer, I want the naming rule stated once as a decision (ADR), so that a later reader does not mistake it for an accident.
45. As a developer, I want the engine's missing layer 2 recorded as a decision, so that the three-layer model stays honest rather than being quietly weakened.
46. As a developer, I want the new engine to leave the four existing engines' behaviour untouched, so that adding it is additive.
47. As a developer, I want the engine-count wording in the repository to stop naming a number, so that the next engine does not have to re-sweep the same drift.

## Implementation Decisions

### 模块

- **新增 Provider 项目**：实现 `IDatabaseProvider` 全部成员（`Type` / `Policy` / `CreateReadOnlyConnectionAsync` / `ListTablesAsync` / `DescribeTableAsync` / `ListDatabasesAsync`）。只引用 Abstractions 与驱动。
- **新增 Provider 的只读策略**：一个 `ReadOnlyPolicy` 值——声明方言值与四个禁用函数名。策略只能往禁用表里**加**名字，不能放宽许可清单（ADR 0013）。
- **新增测试项目**：承载引擎专属套件与共享套件的具体运行。
- **`SharpDb.Abstractions` 修改**：`SqlDialect` 枚举新增一个值。
- **`SharpDb.Core` 修改**：`ReadOnlyGuard` 的方言映射新增一条分支。Core 是唯一引用解析器的项目（ADR 0005 as amended），未知方言值继续响亮失败。
- **`SharpDb`（Host）修改**：装配目录多注册一个 Provider；分发包的描述与标签补上引擎名。
- **文档修改**：`README`、`GLOSSARY.md`、`ADR 0002`、`ADR 0005`、新增两张 ADR。

### 接口契约

- `SHARPDB__TYPE` 的值为 **`mssql`**。这是**已发布的公开契约**：发布后改名会破坏用户既有的 `mcp.json`。装配目录把"接受哪些类型"从注册的 Provider **动态拼出**，因此新增引擎不需要改 Core 的文案逻辑——但 README 里逐字写出类型名的地方（含示例报错文案）要手工补。
- 连接串是**普通 SqlClient 连接串**，原样粘贴即可。只读 intent 由服务器加，不由用户写——与另外四个引擎一致（"连接串里不写只读参数，把会话设成什么是服务器的事"）。
- `describe_table` 找不到名字时返回"不存在"而非异常；措辞归 Tool。
- 内省只读目录视图，不依赖任何高于普通账号的角色。

### 只读三层

- **第 1 层（Guard）**：算法仍是 Core 唯一的一份。本引擎新增的是**数据**——一个方言值 + 四个禁用函数名。
- **第 2 层（引擎层）**：**本引擎缺席**。Provider 写 `ApplicationIntent=ReadOnly`，但这只表达意图。新增 ADR 必须写明：它在 Always On 可读副本上是硬边界，在 standalone 上是 no-op，因此不能算作 enforcement。
- **第 3 层（账号权限）**：**本引擎上它是唯一的边界**，因为第 2 层不存在。这是引擎的事实，不是对用户的要求：账号由运维方选定，SharpDb 不检查也不改动连接串。`SELECT` 挡写与 DDL，`VIEW DEFINITION` 决定元数据可见范围。
- 三层模型本身不变；变的是"第 2 层可能缺席"这件事第一次成为模型的一部分——由新术语 `Read-only intent` 承载。

### 领域术语

`GLOSSARY.md` 在"只读"分组、紧跟 `Read-only enforcement` 之后新增 **`Read-only intent`**：引擎层的一种只读约束，只在特定部署形态下才真的拒绝写；它表达意图，但不构成保证。与 `Read-only enforcement` 相对。`_Avoid_`：用 `Read-only intent` 指代 `Read-only enforcement`。**词条内不写具体连接串关键字**（GLOSSARY 不放实现细节）。

### 未决（由实测决定，不影响本 spec 的形状）

- Guard 的函数遍历器能否看见写在 `FROM` 里的 rowset function。若看不见，需要在 Core 的函数遍历里补一条分支（Oracle 先例允许 Core 为方言差异改动）。
- `ApplicationIntent=ReadOnly` 在 standalone 实例上是否真无副作用。

## Testing Decisions

**好测试的标准**：只断言外部可观察的行为，不断言实现细节。具体到本仓库——断言客户端拿到的 JSON、引擎真正拒绝的行为、以及"连接真的处在某个状态"，而不是断言某句 SQL 长什么样或某个私有方法被调用。断言"只读"的方式是**试着写一次**（引擎拒绝），而不是检查连接串里有没有某个关键字——关键字是"怎么要"，被拒绝的写才是"真的在生效"。

**测哪些模块**（复用三个既有接缝，零新增）：

1. **`IDatabaseProvider` 接缝**：新引擎实现 `IContractDatabase`，继承两个共享契约套件（内省 + 查询）。这是既有四个引擎的同一套断言，第五个引擎必须同样通过。
2. **引擎专属套件**：承载共享套件不能包含的、本引擎独有的真事——连接串带只读 intent、账号无写权限时引擎拒写（引擎的事实，不是对用户的要求）、限定名与裸名的分界、`MS_Description` 注释、`IDENTITY` 自增、复合外键列配对顺序、索引唯一性与列序、`EXPLAIN`/`SHOW` 由引擎报错、`[bracket]` 标识符被 Guard 接受、rowset function 被 Guard 拒绝、超时被中断。
3. **MCP Tool 面**：查询类断言经由按宿主方式组装的真 Tool 进行（既有的 `QueryTool` 组装点），这样行数上限、超时、拒绝理由与 JSON 形状都被覆盖。
4. **真宿主进程**：一条进程级 smoke 覆盖 `Program.cs` 的注册与 `SHARPDB__TYPE=mssql` 的选择——进程内测试证明不了宿主的接线，这是本仓库已记录的教训。

**先例（照抄既有形态，不发明）**：

- 容器化测试的写法：`PostgreSqlContractDatabase`（pin 镜像版本、Docker 缺席即 skip、连接串不含只读模式、额外数据库用于需要自有 schema 的测试）。
- **多账号**的写法：`OracleContractDatabase`（用管理员账号播种、用另一个账号供被测 Provider 连接）。
- 共享 schema 的每引擎一份 DDL：`ContractSchema`（新增本引擎的 T-SQL 版本）。
- 断言下沉的写法：Oracle 那次把 6 条断言移出共享套件并落到各引擎项目（`.scratch/sharpdb-oracle/issues/02-…`）。
- 集合夹具：一个容器服务整个集合，而不是每个测试类一个容器。

**本次测试上的一个特殊决定**：共享套件里唯一直接验证第 2 层的断言（打开连接后试着 INSERT，断言引擎拒绝）**下沉**到 MySQL / PostgreSQL / SQLite / Oracle 四个引擎项目。SQL Server 自己改为两条断言——"连接串带只读 intent"（本引擎能保证的部分）与"账号无写权限时引擎拒写"（本引擎真正的边界，即第 3 层；断言的是**引擎行为**，不是对用户的要求）。这不是把断言改弱，而是把一个引擎做不到的断言交给能做到的引擎，并为本引擎补上它真正成立的那条。

## Out of Scope

- **发版**：version bump、打 tag、`publish.yml` 触发、nuget.org 发布——不在本 feature 内。
- **`tools/` 下的验证脚本与种子脚本**：Oracle 那次没有为它加 seed 脚本、也没动 `verify-installed-tool.ps1`（`tools/` 里 Oracle 零痕迹），本次同样不加。
- **Azure SQL 的显式支持声明**：未实测就不声明。驱动与连接串在 Azure 上大概率可用，但"支持"这句话要有实测背书。
- **修改另外四个引擎的任何行为**：本次是纯增量；四个引擎的测试必须保持全绿。
- **为 SQL Server 引入任何新的只读机制**（例如要求用户开只读路由、或强制走 AG 监听器）：第 2 层缺席这件事只**记录**，不在本仓库修复。
- **`NOTICE` 的第三方许可段落**：本引擎的驱动是 MIT，既有 `NOTICE` 只列 Oracle（其许可要求随附全文），因此不动。

## Further Notes

- **本仓库反复踩到的坑：把引擎数量写进文案会随新引擎过期。** 上次（Oracle）在 README、契约套件注释、seed 脚本里全漂了一遍，且没有任何检查会发现。本次收尾时要**用宽模式复扫**（`both|two|three|four|all four|MySQL, PostgreSQL` 等），而不是窄 grep 一两处。已知漂移面：`README`（引擎清单、env 示例、类型表、能力矩阵、示例报错文案、"三层只读"叙述）、分发包的描述与标签、`ADR 0002` 的第 2 层清单、`ADR 0005` 的 `[bracket]` 缺口。user story 47 就是为这条教训写的。
- **测试镜像必须钉版本，不能用浮动 tag**（本仓库已写明的规矩：`postgres:16` 而非 `latest`，否则一次静默升级就改掉共享断言在断言什么）。SQL Server 镜像的 `2022-latest` 是浮动 tag，实测票里要钉成 CU tag 或摘要。
- **`[bracket]` 缺口闭合的连带复核**：既有测试把 `SELECT * FROM [my table]` 记为"可接受缺口"，注释写"brackets are the SQL Server style"。本引擎让该缺口闭合后，这条注释与 `ADR 0005` 需要复核措辞——不是改断言，而是改它解释的对象（缺口对通用方言仍在，对本引擎已闭合）。
- **`TrustServerCertificate`**：驱动自 7.0 起默认 `Encrypt=Mandatory`，自签证书的服务器必须显式 `TrustServerCertificate=True`。**不建议**退到旧版驱动来规避——那是把默认安全降级。
- **本机 Docker 可用**（`mysql:8.0`、`postgres:16` 已缓存）。SQL Server 镜像约 1.5GB、容器需约 2GB 内存，是本仓库目前最重的测试容器。
- **实测票是独立的第一张票**，Provider 票 `Blocked by` 它：`ApplicationIntent` 副作用、rowset function 可见性、`sys.*` 内省 SQL 的真实形状、一条合格的慢只读 SELECT、容器 recipe、只读账号的最小授权集——这些结论会直接改 Provider 与 Core 的写法。
