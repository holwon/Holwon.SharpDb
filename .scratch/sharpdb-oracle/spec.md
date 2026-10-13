Labels: ready-for-agent

# sharpdb-oracle: Oracle Database Provider

## Problem Statement

SharpDb 目前支持三种数据库（MySQL、PostgreSQL、SQLite）。用户需要把 SharpDb 接入一个以 Oracle Database 为持久层的系统，让 AI 代理能以同样的只读方式理解其结构并执行查询。

Oracle 与现有三种引擎在物理语义上有多处不可调和的差异：无会话级只读、空串等价 NULL、不支持外键 `ON UPDATE` 参照动作、`EXPLAIN`/`SHOW` 语法不存在、报错消息不回显对象名。这些差异意味着"加一个 Provider"不是只加一个项目——它同时要求诚实收缩共享契约套件、在 Core 加占位符翻译、并在文档里引入引擎能力矩阵。

## Solution

1. 新增一个 **Oracle Provider**，实现 `IDatabaseProvider` 全部成员，驱动用 `Oracle.ManagedDataAccess.Core`（纯托管、net8.0、Oracle Free Distribution 许可）。
2. **Layer 2 只读**采用事务级机制：Provider 在 `CreateReadOnlyConnectionAsync` 内依次执行 `BeginTransaction()` → `SET TRANSACTION READ ONLY` → `BindByName = true`，交出连接；事务永不提交，随连接 dispose 回滚。**Core 零改动**（不引入 `DbTransaction` 参数）。
3. **Core 加占位符翻译**：把用户 SQL 里的 `@name` 翻译成 `:name`（仅对 Oracle 方言生效），复用 `ParameterPlaceholders` 的词法扫描器。新增 `SqlDialect.Oracle` 枚举值，在 `ReadOnlyGuard.DialectFor()` 加映射分支（用 `GenericDialect`，锁 `SqlParserCS 0.6.5`）。
4. **诚实收缩共享契约套件**：把 6 条 Oracle 物理语义不可能满足的断言从 `ProviderContractSuite` 移出，下沉到 MySQL/PostgreSQL/SQLite 各自的测试项目（覆盖零丢失）。共享套件只保留四个引擎都能满足的最小公倍数。
5. **引入引擎能力矩阵**：在 spec/README 里明列四引擎差异，把"同等完整度"改为"四个 Tool 全可用，语义差异逐条记录"。
6. **文档与 ADR**：`GLOSSARY.md` 的 `Read-only enforcement` 改为引擎中立定义；新增 ADR 0014（Oracle layer 2 事务级 + DDL 洞 + layer 3 必需）与 ADR 0015（Core 占位符翻译）。

## User Stories

### MCP 客户端（AI 代理）

1. As an MCP client, I want to start the server pointed at an Oracle database, so that I can query it through the same four Tools I use for MySQL/PostgreSQL/SQLite.
2. As an MCP client, I want `list_tables` on Oracle to report every table and view I can see, so that I know what I can ask about.
3. As an MCP client, I want `describe_table` on Oracle to report columns, primary keys, foreign keys and indexes, so that I can write correct queries without guessing column names.
4. As an MCP client, I want `execute_query` on Oracle to accept `@name` placeholders (the same spelling I use on the other three engines), so that I do not have to rewrite my queries when the engine changes.
5. As an MCP client, I want a write attempt on Oracle to be refused by the engine, so that the server's read-only promise holds even if the Guard is bypassed.
6. As an MCP client, I want Oracle's empty-string-is-NULL semantics to be reflected faithfully in query results, so that I never mistake a NULL for an empty string.
7. As an MCP client, I want Oracle's `EXPLAIN` / `SHOW` unavailability to surface as an engine error (not a silent no-op), so that I know the statement did not run.
8. As an MCP client, I want Oracle's error messages to reach me as-is (including non-ASCII text), so that I can act on the names and values the database reports.
9. As an MCP client, I want a query that exceeds the configured timeout on Oracle to be interrupted, so that a slow statement does not hang me.
10. As an MCP client, I want `list_databases` on Oracle to report the schemas the connection can see, so that I understand the scope of the connection.
11. As an MCP client, I want a query that returns no rows on Oracle to still name its columns, so that I know the statement was understood.
12. As an MCP client, I want Oracle's `EXPLAIN` unavailability to be documented in the capability matrix, so that I do not try to use it.

### 人类运维

13. As a human operator, I want to configure the server for Oracle with `SHARPDB__TYPE=oracle` and a standard connection string, so that switching engines does not require a different install.
14. As a human operator, I want the server to fail fast at startup if the Oracle database is unreachable, so that a misconfiguration is caught before the first Tool call.
15. As a human operator, I want the README to state clearly which Oracle versions are supported, so that I do not deploy against an unsupported version.
16. As a human operator, I want the README to list Oracle's semantic differences from the other engines, so that I can brief my team on what to expect.
17. As a human operator, I want the server's NuGet package to include the Oracle license text, so that redistribution is compliant.
18. As a human operator, I want the server to work on a machine with only the .NET runtime (no Oracle client installed), so that deployment stays lightweight.

### Oracle 数据库管理员

19. As an Oracle DBA, I want the server to connect with a read-only account that has only SELECT privileges, so that the server cannot modify my data.
20. As an Oracle DBA, I want the server's connection to not hold any locks beyond the duration of a single Tool call, so that my write workload is not blocked.
21. As an Oracle DBA, I want the server's introspection to use only `ALL_*` dictionary views (not `DBA_*`), so that the account needs no special privileges.
22. As an Oracle DBA, I want to be told that Oracle's layer-2 read-only does not block DDL, so that I can enforce the DDL restriction through layer-3 privileges.

### 开发者（仓库维护者）

23. As a developer, I want the Oracle Provider to live in its own project that references only Abstractions, so that the layering rules (ADR 0004) hold.
24. As a developer, I want the shared contract suite to contain only assertions every engine can satisfy, so that adding a new engine never forces a lie into the suite.
25. As a developer, I want the six assertions Oracle cannot satisfy to still run against the engines that can, so that coverage does not regress.
26. As a developer, I want the placeholder translation to be dialect-driven, so that MySQL/PostgreSQL/SQLite are unaffected by the new code path.
27. As a developer, I want the Guard's permit list to stay closed (no per-engine narrowing), so that ADR 0013's shape is preserved.
28. As a developer, I want the Oracle Provider to pass the same startup probe every other Provider passes, so that the fail-fast promise holds for Oracle too.

## Implementation Decisions

### 1. Oracle Provider 项目

新增一个 Provider 项目，引用 `Oracle.ManagedDataAccess.Core`（版本锁定在 `Directory.Packages.props`，当前最新稳定 23.26.301）与 `SharpDb.Abstractions`。Provider 实现 `IDatabaseProvider` 全部成员：

- **`Type`** 返回 `"oracle"`（大小写不敏感匹配，与现有三个同形；无别名）。
- **`Policy`** 返回一个 `OracleReadOnlyPolicy`，`Dialect = SqlDialect.Oracle`（新增枚举值，见下），`DeniedFunctions` 初版为空集（Oracle 上没有"只在只读会话下可用、且 Guard 挡不住"的函数被实测出来；若后续发现再补）。
- **`CreateReadOnlyConnectionAsync`** 的固定时序：
  1. `new OracleConnection(connectionString)`
  2. `await connection.OpenAsync(ct)`
  3. `connection.BeginTransaction()` —— 关键，缺这一步 `SET TRANSACTION READ ONLY` 什么也不强制（实测）
  4. 执行 `SET TRANSACTION READ ONLY`
  5. `connection.BindByName = true` —— 关键，缺这一步 ODP.NET 默认按位置绑，会静默错值（实测）
  6. 返回连接（事务永不提交，随 `DisposeAsync` 回滚）
- **`ListTablesAsync`**：`SELECT table_name, 'TABLE', comments FROM all_tables WHERE owner = USER UNION ALL SELECT view_name, 'VIEW', comments FROM all_views WHERE owner = USER ORDER BY 1`。视图不在 `ALL_TABLES`（实测 0 行），必须 union `ALL_VIEWS`。
- **`DescribeTableAsync`**：缺失返回 `null`（不抛）。用 `ALL_TAB_COLUMNS`（已排除隐藏列，与 `SELECT *` 实际返回一致）。`DATA_DEFAULT` 是 `LONG` 列，**必须直接读**，不能放进 `NVL()`/任何函数（`ORA-00932`）。主键用 `ALL_CONSTRAINTS`（`CONSTRAINT_TYPE = 'P'`）+ `ALL_CONS_COLUMNS`（按 `POSITION` 排序）。外键用 `ALL_CONSTRAINTS`（`CONSTRAINT_TYPE = 'R'`）的 `R_OWNER`/`R_CONSTRAINT_NAME` 反查父键列；`DELETE_RULE` 列值映射到 `ForeignKeyAction`（`CASCADE`→Cascade、`SET NULL`→SetNull、`NO ACTION`→NoAction）；Oracle 无 `ON UPDATE`，`OnUpdate` 一律报 `NoAction`。索引用 `ALL_INDEXES` + `ALL_IND_COLUMNS`（按 `COLUMN_POSITION` 排序），排除主键索引（`ALL_INDEXES.UNIQUENESS = 'UNIQUE'` 且名字以 `_PK` 结尾或匹配主键约束名）。自增识别用 `ALL_TAB_COLUMNS.IDENTITY_COLUMN = 'YES'`（12c+）。
- **`ListDatabasesAsync`**：返回当前用户可见的 distinct owner 列表（`SELECT DISTINCT owner FROM all_tables UNION SELECT DISTINCT owner FROM all_views ORDER BY 1`），并包含连接自身的 schema。
- **标识符大小写**：Oracle 未加引号的标识符折成大写。共享 Oracle DDL 给所有标识符加双引号写小写（`"users"`），确保字典回的就是小写，与共享套件的精确小写名字断言兼容。

### 2. Core 占位符翻译

`SqlDialect` 枚举新增 `Oracle` 值。`ReadOnlyGuard.DialectFor()` 加映射分支：`SqlDialect.Oracle => new GenericDialect()`（实测 `GenericDialect` 对 Oracle 常用只读 SQL 43 条里 38 条通过；`OracleDialect` 在 `SqlParserCS 0.6.6-release0000` 里存在但不修 `MINUS`/`CONNECT BY`/`(+)` 且带 2 处回归，因此不升预发布版）。

新增翻译层：在执行前，由 `provider.Policy.Dialect` 驱动，把用户 SQL 里的 `@name` 翻译成 `:name`（仅对 Oracle 方言生效）。复用 `ParameterPlaceholders` 的词法扫描器（它已经会跳过引号与注释），所以 `t@dblink` 不会被翻成 `t:dblink`（`@dblink` 仍被识别为占位符 → 走"未绑定占位符"的拒绝路径，拒绝是对的，因为 dblink 会去摸另一个数据库，违背 `use-cases.md` 的意图）。

翻译必须在 Core 做（Provider 不在执行路径上，看不到用户 SQL，也碰不到 Core 建的 `OracleCommand`）。这是 Core 唯一一处因新引擎而改动的地方（除 `SqlDialect` 枚举与 `DialectFor` 映射外）。

### 3. 共享契约套件收缩

从 `ProviderContractSuite` 移出 6 条断言（Oracle 物理语义不可能满足）：

| 断言 | 原因 |
|---|---|
| `DescribeTable_reports_a_foreign_key_with_its_actions`（`OnUpdate == SetNull` 部分） | Oracle 无 `ON UPDATE` 参照动作（`ORA-00905`）；`ALL_CONSTRAINTS` 无 update rule 列 |
| `A_query_answers_columns_once_and_rows_as_lists`（空串 `""` 部分） | Oracle `''` 即 NULL（实测 `v=''` 返回 0 行、`v IS NULL` 返回 1 行） |
| `A_parameter_may_be_null`（连带） | 因 gamma 行的 `''`→NULL，计数变 2 |
| `Text_outside_ascii_is_written_as_itself`（无 FROM） | Oracle 21c 要求 FROM（`ORA-00923`）；23c 才允许省略 |
| `A_failure_names_what_it_could_not_find_as_itself` | `ORA-00942` 消息不含对象名 |
| `An_explain_of_a_query_answers_with_a_plan` | Oracle 无 `EXPLAIN SELECT`（`ORA-00905`）；只有 `EXPLAIN PLAN FOR`（写 `PLAN_TABLE`，不返回结果集） |

这些断言**下沉到 MySQL/PostgreSQL/SQLite 各自的测试项目**（覆盖零丢失，只是换位置）。共享套件只保留四个引擎都能满足的最小公倍数。这正是 `ContractSchema` 注释里已经写明的仓库既有约定（"where an engine cannot express something … the engine-specific tests cover it in the engine's own project"），只是第一次真的执行它。

Oracle 的共享 seed（`ContractSchema.OracleDdl`）给所有标识符加双引号写小写；FK 只声明 `ON DELETE`（无 `ON UPDATE`）；空串用 `NULL` 替代（因为 Oracle 存 `''` 也是 NULL）；`readings.payload` 用 `RAW(10)` + `HEXTORAW('0102')`（Oracle 无零长 blob 字面量，空 blob 写 `HEXTORAW('')` 也存成 NULL，所以共享 seed 里 `beta` 行的空 payload 在 Oracle 上存 NULL —— 这条断言在 Oracle 测试项目里单独处理）。

### 4. 引擎能力矩阵

在 spec 与 README 里引入一张表，明列四引擎差异：

| 能力 | MySQL | PostgreSQL | SQLite | Oracle |
|---|---|---|---|---|
| `''` 即 NULL | 否 | 否 | 否 | **是** |
| `ON UPDATE` 参照动作 | 是 | 是 | 是 | **否** |
| Layer 2 只读机制 | 会话级 | 会话级 | 文件级 | **事务级** |
| Layer 2 防 DDL | 是 | 是 | 是 | **否**（DDL 隐式提交解除只读） |
| Layer 3 必需性 | 建议 | 建议 | 否（文件级） | **必需** |
| `EXPLAIN` | 是 | 是 | 是 | **否**（引擎报语法错） |
| `SHOW` | 是 | 否（Guard 放行，引擎报语法错） | 否 | **否** |
| 报错消息含对象名 | 是 | 是 | 是 | **否** |
| 无 FROM 的 SELECT | 是 | 是 | 是 | **否**（21c 要求 FROM） |

### 5. Guard 与 Policy

Guard 的许可清单（`SELECT` / `EXPLAIN` / `SHOW*`）**不因引擎而变**。Oracle 上 `EXPLAIN`/`SHOW` 由引擎自然报错（`ORA-00905` / `ORA-00900`），Tool 把引擎错误原样上抛给客户端。`ReadOnlyPolicy` 的 `DeniedFunctions` 初版为空集（P2 可裁剪：未来若发现 Oracle 上有"只在只读会话下可用、且 Guard 挡不住"的函数，再补）。

### 6. 打包与许可

`Oracle.ManagedDataAccess.Core` ~4.95MB 进入同一个 global tool 包（ADR 0009 的"一次安装、多库通用"承诺）。分发包根目录附 Oracle 许可协议副本；`README.md` 与 `NOTICE` 文件加标准声明："This project uses Oracle Managed Data Access, licensed under Oracle Free Distribution Terms"。

### 7. Host 装配

`Program.Main` 的 `ProviderCatalog` 构造加 `new OracleDatabaseProvider()`。`SharpDb.csproj` 加 `ProjectReference` 到 Oracle 项目，更新 `Description`/`PackageTags` 加 `oracle`。

### 8. 文档

- `GLOSSARY.md`：`Read-only enforcement` 改为引擎中立定义（"数据库引擎层的只读保证——即使 Guard 被绕过，写操作仍会被数据库自身拒绝。具体实现机制由各 Provider 决定，不在本词条内。"）
- `docs/adr/0014-oracle-read-only-transactional-with-ddl-hole.md`：Oracle layer 2 是事务级、DDL 会解除、layer 3 必需、连接在事务中交付
- `docs/adr/0015-core-placeholder-translation.md`：Core 按方言翻译 `@` → `:`
- `README.md`：加 Oracle 到支持引擎列表（枚举式，不引入"four engines"数字短语），引入能力矩阵
- `docs/use-cases.md`：若需要，更新引擎计数表述（用宽泛词 `every engine`，不引入数字）

## Testing Decisions

### 测试哲学

只测外部行为（Tool 的输入/输出），不测实现细节（内省 SQL 怎么写、连接怎么开）。共享契约套件是"所有引擎都必须满足的最小公倍数"，引擎特异性测试在各自的测试项目里。

### 测试基座

- **Testcontainers.Oracle** + `gvenzl/oracle-xe:21-slim-faststart`（~1GB，启动 1-5 分钟）。
- 每个测试项目用 `ICollectionFixture<T>` 共享一个容器；`[SkippableFact]` + `SkipWhenUnavailable()` 扩展方法（Docker 不可用时跳过，不失败）。
- 容器启动后，fixture 创建一个**专用测试用户**（不是 `probe`/`system`），所有内省与查询都通过这个用户，模拟最小权限场景。

### 测试项目

- **`tests/SharpDb.Provider.Oracle.Tests/`**：Oracle 专属测试项目。包含：
  - `OracleContractDatabase : IContractDatabase, IAsyncLifetime`（fixture，启动容器、seed 共享 schema）
  - `OracleProviderContractTests : ProviderContractSuite(database)`（空子类，跑共享套件里 Oracle 能过的断言）
  - `OracleQueryContractTests : QueryContractSuite(database)`（空子类，跑共享套件里 Oracle 能过的断言）
  - `OracleSpecificTests`（Oracle 专属断言）：
    - `''` 即 NULL 的语义
    - `ON UPDATE` 不可声明
    - `EXPLAIN` 报语法错
    - 报错消息不含对象名
    - 无 FROM 的 SELECT 报语法错
    - Layer 2 只读拒绝 INSERT/UPDATE/DELETE
    - **DDL 解除只读**（实测：只读事务内 `CREATE TABLE` 成功，之后 `INSERT` 成功）—— 这是 Oracle 的已知缺口，断言它**存在**（不试图修它），并在 README 里写明
    - `BindByName` 默认为 false，Provider 设为 true 后参数按名绑定
    - `IDENTITY_COLUMN = 'YES'` 识别自增
    - `ALL_TAB_COLUMNS.DATA_DEFAULT` 是 LONG 列，直接读不进函数
    - 视图在 `ALL_VIEWS` 不在 `ALL_TABLES`
    - 带引号小写标识符的读写行为

### 共享套件收缩的回归测试

收缩共享套件后，必须验证 MySQL/PostgreSQL/SQLite 的测试项目**仍然全绿**（断言被搬走，但行为没变，只是换地方断言）。这是收缩操作的验收条件。

### 进程级 smoke

与现有三个引擎一样，Oracle 也需要一条进程级 smoke 测试（起真实进程，stdin EOF 触发断开），证明 Host 装配正确。

## Out of Scope

- **对象类型、嵌套表、VARRAY**：不在本 spec 范围内。`describe_table` 只处理关系表与视图。
- **升级 `SqlParserCS` 到预发布版**：实测 `0.6.6-release0000` 的 `OracleDialect` 不修 `MINUS`/`CONNECT BY`/`(+)` 且带 2 处回归，且 0.6.6 的 `GenericDialect` 与 0.6.5 有行为漂移（`SAMPLE` FAIL→OK）。锁 `0.6.5`。
- **`ReadOnlyPolicy` 加"能减"的维度**：ADR 0013 刻意用类型形状把"只能加"固化了，不为单一引擎撬开。
- **让 Oracle 支持 `EXPLAIN PLAN FOR`**：它写 `PLAN_TABLE`，是写语句，放行会击穿 ADR 0001/0002 的只读保证。
- **`OracleConnection.BindByName` 的全局静态设置**：用实例属性（Provider 在连接上设），不动 `OracleConfiguration.BindByName`（全局会影响进程内其他连接）。
- **Oracle 11g 及更早版本**：锁 12c+（`IDENTITY` 列、`OFFSET/FETCH` 分页）。
- **Oracle Cloud Autonomous Database 特化**：本 spec 针对通用 Oracle Database（EE/SE/XE），ADB 的连接串差异由用户自行处理。

## Further Notes

### 实测证据

本 spec 的所有技术决定都经过 Oracle XE 21.3 真库实测验证（探针在 `%TEMP%\sharpdb-oracle-*\`，未进仓库）。关键实测结论：

- `SET TRANSACTION READ ONLY` 单独发什么也不强制（INSERT/UPDATE/CREATE TABLE 全部成功且已提交）；必须先 `BeginTransaction()`。
- `OracleConnection.BindByName` 是 public 可写实例属性；ODP.NET 默认 `BindByName = false`，按位置绑会静默错值。
- `SELECT @id FROM DUAL` → `ORA-00936`（缺失表达式），`BindByName` true/false 都一样；必须翻译成 `:name`。
- `ALL_TAB_COLUMNS.DATA_DEFAULT` 是 LONG 列，不能放进 `NVL()`/任何函数。
- 视图不在 `ALL_TABLES`（实测 0 行），在 `ALL_VIEWS`。
- `ON UPDATE SET NULL` / `ON UPDATE CASCADE` → `ORA-00905`；`ALL_CONSTRAINTS` 无 update rule 列。
- `EXPLAIN SELECT` → `ORA-00905`；`SHOW TABLES` → `ORA-00900`。
- `SELECT id FROM 不存在` → `ORA-00942`，消息不含对象名。
- `SELECT '中文' AS 列`（无 FROM）→ `ORA-00923`。
- `''` 即 NULL（`v=''` 返回 0 行、`v IS NULL` 返回 1 行）。
- 只读事务内 `CREATE TABLE` 成功，之后 `INSERT`/`UPDATE` 成功（DDL 隐式提交解除只读）。
- `Oracle.ManagedDataAccess.Core` 23.26.301 nupkg = 4.95MB，包内无 XML 文档。

### 仓库既有约定的首次执行

`ContractSchema` 的注释里早就写明："where an engine cannot express something … the engine-specific tests cover it in the engine's own project"。Oracle 是第一个逼着仓库真的执行这条约定的引擎。这不是 Oracle 的缺陷，而是仓库既有规则的落地。

### 环境副作用

Oracle XE 容器 `sharpdb-oracle-probe`（端口 1521）仍在运行，可用于开发期调试。探针在 `%TEMP%` 下，未进仓库。
