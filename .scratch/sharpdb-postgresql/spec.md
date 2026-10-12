# Spec: 新增 PostgreSQL Provider

Label: `ready-for-agent`

> 实现票在 [`./issues/`](./issues/)。
> 词汇一律以 [`GLOSSARY.md`](../../GLOSSARY.md) 为准（Provider / Driver / Core / Host / Schema model / Introspection / Guard / Read-only enforcement / Tool）。
> 本 spec 附带若干 **ADR 修订**，见「Implementation Decisions → ADR 修订」。
> 全部关键事实均为**本机真容器实测**（`postgres:16.15`），出处见「Further Notes」。

## Problem Statement

SharpDb 现在只能服务 MySQL 与 SQLite 两种数据库。手上是 PostgreSQL 的用户**完全用不了**：把 `SHARPDB__TYPE` 填成 `postgres`，得到的是一句 "is unknown"，然后进程退出——没有部分可用，也没有"降级模式"，就是整个不能用。

这不是配置问题，是**缺一个 Provider**。用户能做的只有：等，或者自己写一个。

对 AI 代理一侧的后果是一样的：它能看见用户的 MySQL 库和 SQLite 库，唯独看不见那个 PostgreSQL 库——而那往往正是生产库。

## Solution

新增 **PostgreSQL Provider**，类型名 **`postgresql`**。装一次（还是同一个包），改一行环境变量，那个库就和另外两种一样能被看见、被描述、被只读查询：

```
SHARPDB__TYPE=postgresql
SHARPDB__CONNECTIONSTRING=Host=localhost;Port=5432;Database=app;Username=sharpdb_ro;Password=CHANGE_ME
```

四个 Tool（`list_tables` / `describe_table` / `execute_query` / `list_databases`）对 PostgreSQL 给出与另外两种**同一形状**的答案，客户端不必知道背后是哪个引擎。

**只读保证与另两种同等强度**：Guard 照旧挡在前面；PostgreSQL 会话被置为只读，写操作由引擎自己拒绝——即使用户用的是超级用户账号，引擎层也拦得住 `INSERT` / `DDL` / `TRUNCATE` / 序列。

顺带修好一个**已实测的真实缺口**：PostgreSQL 的文件读取函数（`pg_read_file` 等）在只读会话下**仍然可用**，而它们会被 Guard 当成普通 `SELECT` 放行。这次一并堵上——这也是"Provider 需要向 Guard 贡献自己的知识"的第一个真实用例。

## User Stories

### 让它能用

1. 作为 PostgreSQL 用户，我想让 SharpDb 认 `postgresql` 这个类型名，以便不必换工具就能接上我的库。
2. 作为 PostgreSQL 用户，我想让连接串就是普通的 Npgsql 连接串，以便我复制粘贴现成的那份就能用。
3. 作为 PostgreSQL 用户，我想让只读模式由 Provider 自己加上，以便我不必在连接串里手写引擎参数、也不必记得写对。
4. 作为 PostgreSQL 用户，我想让不可达/密码错/库不存在在启动时**一次**报错并退出，以便不必等到第一次查询才知道配错了。
5. 作为 PostgreSQL 用户，我想让启动失败信息是一行、不含密码，以便能安全地贴进 issue。
6. 作为用户，我想让 `postgresql` 成为**唯一**的类型名，以便类型与 Provider 一一对应这件事不被别名破坏。
7. 作为用户，我想让类型名大小写与首尾空格不敏感，以便 `PostgreSQL` 这种顺手写法也能用。

### 看结构

8. 作为 AI 代理，我想让 `list_tables` 列出我**能不加限定名查到**的表和视图，以便我看到的每个名字写进 SQL 都能解析。
9. 作为 AI 代理，我想让 `list_tables` 区分表与视图，以便我知道哪些是实体、哪些是派生。
10. 作为 AI 代理，我想让 `describe_table` 给出列的**原始类型声明**（如 `character varying(255)`、`numeric(10,2)`、`timestamp with time zone`），以便我预测值会怎么被处理。
11. 作为 AI 代理，我想让列的序号是 **1 起**、按声明顺序，以便和另外两种引擎的答案一致。
12. 作为 AI 代理，我想让 `describe_table` 报告列的 nullability，以便我知道哪些列可能为空。
13. 作为 AI 代理，我想让 `describe_table` 报告列的默认值表达式，以便我知道省略该列会发生什么。
14. 作为 AI 代理，我想让 `describe_table` 报告自增（含 `GENERATED … AS IDENTITY` 与遗留 `serial` 两种），以便我知道这个键不用我提供。
15. 作为 AI 代理，我想让 `describe_table` 报告**复合主键的键序**（而非声明序），以便我构造正确的连接条件。
16. 作为 AI 代理，我想让外键的**本地列与引用列按同一位置配对**，以便我写出正确的多列连接。
17. 作为 AI 代理，我想让外键带 `ON DELETE` / `ON UPDATE` 动作，以便我知道删除父行会发生什么。
18. 作为 AI 代理，我想让索引带列清单与唯一性，以便我知道哪些查询能走索引。
19. 作为 AI 代理，我想让**主键背后的索引不出现在索引清单里**，以便同一件事不被报两遍。
20. 作为 AI 代理，我想让表/视图与列的注释原样出现，以便理解字段的业务含义。
21. 作为 AI 代理，我想让 `describe_table` 对不存在的名字回答一个明确的"没有"，而不是报错，以便区分"我猜错了名字"与"服务器出问题了"。
22. 作为 AI 代理，我想让表名匹配按 PostgreSQL 自己的规则（未加引号的标识符折叠为小写）进行，以便大小写写错时仍能命中。
23. 作为 AI 代理，我想让 `list_databases` 报出这个连接看得见的库名，以便知道自己触及的范围；并且**不把模板库**列进去。

### 查询

24. 作为 AI 代理，我想让 `execute_query` 跑纯 `SELECT`/`EXPLAIN`/`SHOW`，以便探查数据。
25. 作为 AI 代理，我想让参数用 `@name` 占位、键不带 `@`，以便与另外两种引擎的用法一致。
26. 作为 AI 代理，我想让整型参数按数字绑定、空值按 SQL NULL 绑定，以便比较语义正确。
27. 作为 AI 代理，我想让结果信封仍是 `{columns, rows, truncated?}`，以便既有客户端不必改。
28. 作为 AI 代理，我想让超时以**固定文案**报告（并指出可调的环境变量），以便不必猜是引擎还是服务端的问题。
29. 作为 AI 代理，我想让数据库自己报的错（表不存在、列不存在、语法错）原样到达，以便我照着自己的错误纠正。
30. 作为 AI 代理，我想让错误消息里**不含连接串凭据、也不含本机文件路径**，以便不会把敏感信息带进上下文。
31. 作为 AI 代理，我想让 PostgreSQL 本机的文件读取函数（`pg_read_file` / `pg_read_binary_file` / `pg_ls_dir` / `lo_import` / `pg_stat_file`）被 Guard 拒绝，以便**不依赖用户恰好配了最小权限账号**。
32. 作为 AI 代理，我想让 `FILTER (WHERE …)` 这类 PostgreSQL 常见只读写法被放行，以便聚合查询不被误拒。
33. 作为 AI 代理，我想让"被拒绝"的语句拿到清楚的拒绝理由与可用替代，以便我改写而不是反复试。

### 不变的行为

34. 作为既有用户，我想让 MySQL 与 SQLite 的行为**一字不变**（含反引号标识符仍可用），以便这次新增不造成任何回归。
35. 作为既有用户，我想让包还是同一个、版本号还是一套，以便我升级时只升一个东西。
36. 作为既有用户，我想让 `postgres` 这个拼写**继续是不认识的值**（保持"未知类型"示例不变），以便既有文档与验证脚本不必被改写。
37. 作为维护者，我想让"新增一种数据库"继续等于"新增一个 Provider + Provider 侧声明自己的策略"，以便架构承诺不被这次改动侵蚀。

### 知情边界

38. 作为维护者，我想知道哪些 PostgreSQL 只读写法仍会被**误拒**（`TABLESAMPLE` / `IS JSON` / `OVERLAPS` / `ORDER BY … USING` / `FOR NO KEY UPDATE` 等），以便不把它们当未修完的缺陷。
39. 作为维护者，我想知道这些误拒**只损失功能、不损失安全**（拒绝永远安全），以便评估优先级时方向正确。
40. 作为维护者，我想让受支持的最低 PostgreSQL 版本被写清（10+，因 `attidentity`），以便不虚假承诺。
41. 作为维护者，我想让"只在 PostgreSQL 16 上测过"被写清，以便后人知道测试覆盖的真实边界。
42. 作为维护者，我想让"视图列的 nullability 在 PostgreSQL 上不传播"被记录，以便不误以为中立模型能给出引擎无关的答案。

## Implementation Decisions

### 新增一个 Provider

- 新增 **PostgreSQL Provider**：实现既有的 Provider 契约（`IDatabaseProvider`），独占自己的 **Driver**（Npgsql）。Core 仍然不认识任何 Driver。
- 只读连接的做法：`OpenAsync` 之后、把连接交出去之前，执行
  `SET SESSION CHARACTERISTICS AS TRANSACTION READ ONLY`——与 MySQL Provider 同一位置、同一形态（ADR 0002 第 2 层）。
  - **不用连接串参数**（`Options=-c default_transaction_read_only=on`）：效果相同，但要求 Provider 解析并改写用户给的连接串（还要处理用户自己写的 `Options` 合并），引入新的失败面而收益为零。
  - **实测提醒**：`SET SESSION TRANSACTION READ ONLY`（MySQL 的写法）在 PostgreSQL **跨事务无效**——自动提交下只给一句 warning，下一条 `INSERT` 照常成功。这个坑必须写进代码注释与 ADR。
- **Driver 版本**：Npgsql，取当前 net8.0 兼容的稳定版。这是**发布物**新增的一份依赖，需克制（只有 Provider 项目引用它）。

### 内省：范围与落点

- **对象范围 = `search_path` 可见对象**。理由：这个工具的产品是"让 AI 理解的 schema"，AI 看到 `users` 就会写 `FROM users`；`search_path` 可见性恰好等价于"裸名能解析到什么"。
  - 必须叠加 **schema 排除**（`pg_catalog` / `information_schema`），否则 `pg_table_is_visible` 会把约 64 张系统表算进来（实测）。
  - **否决**"只 `current_schema()`"（会藏掉真能解析到的对象，误导 AI）与"所有非系统 schema"（同名对象会在不同 schema 并存，撞上中立模型"表名唯一标识对象"的假设）。
- **原始类型声明**用 `format_type(atttypid, atttypmod)`——它一次性给出 `character varying(255)` 这种完整声明，与 Schema model 要的"原始声明"天然吻合；`information_schema` 会把长度/精度拆进多个列，需再拼装。
- **自增**判定：`attidentity` 非空（`GENERATED … AS IDENTITY`）或默认值形如 `nextval('…'::regclass)`（遗留 `serial`）。
- **主键**用 `pg_constraint.conkey`（数组即键序）。
- **外键**用 `conkey`/`confkey` **平行数组**——这是"本地列与引用列同序"的结构性保证；`information_schema` 的 `constraint_column_usage` 没有序号，复合外键无法可靠配对。
- **索引**从 `pg_index` 读，**排除 `indisprimary`**；表达式列按"无名"处理（与另外两种引擎的既定行为一致）。
- **注释**用 `obj_description` / `col_description`。
- **对象存在性**由 `pg_class` 判定（表名不存在时给 0 行，`describe_table` 回 `null`）。

### Guard 改造：从静态到 DI 服务

这是本 spec 唯一的**结构性**改动，且它不只为 PostgreSQL——它是把"Provider 贡献只读知识"这件事做成了机制。

- **现状**：Guard 是 Core 里的一个 `static` 类，方言写死 `GenericDialect`，禁用函数表写死四个 MySQL 名字。
- **改成**：Guard 成为 **Core 的 DI 服务**，由**被选中 Provider 提供的策略数据**构造。策略数据只有两项：
  - **方言**（一个枚举值，不是 parser 类型）；
  - **禁用函数表**（该引擎上"读或影响服务器状态、而只读查询不该调用"的函数名）。
- **落点选择**：策略数据放在既有的 Provider 契约项目里（**不新增项目**，也**不把 `SqlParserCS` 拖进它**——方言以枚举表达，parser 仍只留在 Core）。
  - **否决**新建 `Core.Abstractions`/`Core.Share` 项目：那是为将来某种"每 Provider 需要不同 Guard **算法**"预留的形态；今天没有任何 Provider 需要不同算法（实测 43 条语句两方言分类完全一致），提前建它是 ADR 0003 明确反对的"先声明后用不到的东西"。
  - **否决**给每个 Provider 一个 Guard 子类、允许 `override` 放宽策略：Guard 目前唯一的安全性质是**许可清单**——"拒绝永远安全，错误只会表现为误拒、绝不表现为写操作漏过"。允许逐 Provider override 会把这条**集中保证**降级成**逐实现保证**。
- **MySQL 策略**：`GenericDialect`（保住用户的反引号用法）+ 现有四个函数名。
  - 实测：`PostgreSqlDialect` **不认反引号**，所以"全局换方言"会打破 MySQL——这条排除了那个更简单的选项。
- **PostgreSQL 策略**：`PostgreSqlDialect`（换来 `FILTER (WHERE …)` 可用）+ 上述文件读取函数。
- **连带改动**（真实且必须做）：
  - 调用点（Tool）由静态调用改为构造注入；
  - 共享契约套件的 Tool 构造入口要一并传入策略；
  - 既有的 Guard 单元测试由"测一个静态方法"改为"按策略测"。
- **明确不做**：不允许任何 Provider 放宽许可清单；新函数名只能**加**进禁用表。

### 契约差异的处理（必须在实现前定，否则断言会红）

- **`count(*)` 的列标签**：PostgreSQL 给 `count`，MySQL/SQLite 给 `count(*)`。共享套件里有一条断言把它写死成 `count(*)`——要改（把该断言里的聚合列起别名，或把该断言移入引擎专属测试），保持"共享断言必须两个引擎都能逐字满足"的既有规则。
- **视图列 nullability / 默认值**：PostgreSQL **不传播**基表约束（与 SQLite 同、与 MySQL 反）→ 必须继续留在引擎专属测试，不进共享套件。
- **外键不自动建索引**：PostgreSQL 与 SQLite 同、与 MySQL/InnoDB 反 → 共享 schema 里"每个外键配显式覆盖索引"的既有做法**正好也满足** PostgreSQL，无需改。

### 分发与装配

- Provider 加入 Host 的装配点（`ProviderCatalog` 多一个实例）。Catalog **不硬编码**类型列表——它从构造参数生成期望类型串，所以类型名会自动变成 `"mysql" or "postgresql" or "sqlite"`。
- **仍是同一个包**（ADR 0009 不变）：换库不换包。代价照旧：安装会连带拉下 Npgsql。
- 包元数据里写死的 "MySQL and SQLite" 字样更新为三种。
- **README** 增补 PostgreSQL 的 `mcp.json` 示例、类型表、以及"声明支持 PG 10+、只在 16 上测"。

### ADR 修订（本 spec 的一部分）

- **ADR 0002（三层只读）**：第 2 层补上 PostgreSQL 的会话只读机制；记录"MySQL 语句在 PG 上无效"这个实测坑。
- **ADR 0003（单深接口）**：契约新增"策略数据"成员（与既有成员并列）。Provider 仍是**一个** seam 的结论不变——变的只是它多带一组只读知识。
- **ADR 0005（Guard 用 GenericDialect）**：打补丁——**结论修正后仍成立**（方言不改变放行/拒绝的**分类**，43 条语句实测一致），但"方言不参与解析成败"被实测推翻：记 6 个已知**误拒**缺口；并**删除**"Provider 接口没有方言成员 / 契约项目不依赖 parser"这两句——由策略数据成员取代。
- **新增 ADR（候选 0013）**：Guard 是 Core 的 DI 服务，Provider 贡献只读策略数据（方言 + 禁用函数）；记录"为何不做逐 Provider 的 Guard 子类"。

### 已知误拒（接受，记录在 ADR 0005 补丁）

`TABLESAMPLE`、`IS JSON`、`OVERLAPS`、`ORDER BY … USING`、`FOR NO KEY UPDATE`，以及 `DO`/`VACUUM`/`ANALYZE`/`REINDEX`/`LOCK`/`LISTEN`/`NOTIFY`/`RESET` —— 两个方言都解析失败因而被拒。
**性质**：误拒（功能损失），**不是漏放**（安全无损）。与 ADR 0005 已接受的 SQLite `[方括号]` 缺口同类。

## Testing Decisions

### 什么算好测试

只断言**外部行为**：
- Guard——"给定一条语句与一份策略，放行还是拒绝"；
- Provider——"给定一个种子库，Tool 答出什么"。

**不**断言内部 SQL、**不**断言用哪个 parser 类型、**不**断言异常类型。判据：把被测行为改坏，测试必须失败。

### Seam 一：Guard（已有单元 seam，参数化）

沿用 Core 既有的 Guard 测试 seam（现在测的是静态方法）。改造后**同一组语句按不同策略各跑一遍**：

- 一组写语句/逃逸函数 → 在两种策略下都被拒；
- `count(*) FILTER (WHERE …)` → PostgreSQL 策略放行、Generic 策略误拒（这条正是"策略数据真的在起作用"的证明）；
- `SELECT \`id\` FROM \`t\``（反引号）→ MySQL 策略放行（这条锁住"不回归 MySQL"）；
- 文件读取函数 → PostgreSQL 策略拒绝。

### Seam 二：共享契约套件（已有的最高 seam，第三份执行）

沿用 `ProviderContractSuite` + `QueryContractSuite`，新增第三个执行（PostgreSQL），并新增一份 `PostgreSqlDdl` 种子脚本。**不新增 seam。**

- 这正是"新增一种数据库"的验收面：同一组断言跑三遍全绿。
- 若某条断言暴露的是**真实引擎分歧**（不是 Provider 缺陷），按既有规则移入引擎专属测试，并在此处记一笔。

### Seam 三：Host 启动（已有）

沿用既有的启动契约测试与安装验证脚本：类型名 `postgresql` 被接受；`postgres` 仍是未知值；期望类型串变为三者。

### 环境与跳过

- PostgreSQL 契约测试经容器提供（Testcontainers），pin **`postgres:16`**；Docker 缺席时**跳过**而非失败（与 MySQL 侧对称）。
- **不做**多版本矩阵（与 MySQL 现状对称）。

### 先例

- Guard：Core 既有的 Guard 测试。
- Provider：MySQL 与 SQLite 的契约测试与引擎专属测试。
- 启动：既有启动契约测试与安装验证脚本。

## Out of Scope

- **不做**类型名别名（`postgres` 不指向 PostgreSQL Provider）。
- **不把** PostgreSQL 的多 schema 全量暴露——只覆盖 `search_path` 可见对象。
- **不做**多 PostgreSQL 版本矩阵，也**不改** CI 结构（`dotnet test` 自动发现新测试项目即可）。
- **不做**每 Provider 独立的 Guard 子类，**不允许**任何 Provider 放宽许可清单。
- **不修**上文列出的已知误拒缺口（`TABLESAMPLE` 等）。
- **不把** `nextval`/`setval` 加进禁用表——它在只读事务下由引擎自己拒绝，属"引擎兜底已覆盖"。
- **不碰**写操作、事务控制、连接池调优。

## Further Notes

### 全部关键事实为真容器实测（`postgres:16.15`）

- 只读：`SET SESSION CHARACTERISTICS AS TRANSACTION READ ONLY` 跨事务有效；`SET default_transaction_read_only = on` 等效且可入连接串；`SET SESSION TRANSACTION READ ONLY` **无效**（仅当前事务块内生效）。
- 只读会话**拒绝**：`INSERT` / `UPDATE` / `DELETE` / `CREATE TABLE`（**含 `CREATE TEMP TABLE`**）/ `DROP` / `TRUNCATE`；报错为 `cannot execute <VERB> in a read-only transaction`。
- **只读会话挡不住**：`pg_read_file`（实测读到服务器上的配置文件）、`pg_ls_dir`（实测列出数据目录）。——这是 Guard 必须补的洞。
- 内省：`format_type` 给出完整声明；视图列 `attnotnull = false`（不传播）；外键**不**自动建索引（实测仅 `users_pkey` 一个索引）。
- `pg_table_is_visible` 会包含 `pg_catalog` 的系统表 → 必须叠加 schema 过滤。
- 未加引号的标识符**折叠为小写**（方向与 MySQL on Linux 默认相反）。

### Guard 方言对照测得的净结论

43 条语句（含全部写操作、`WITH … INSERT`、`SELECT INTO`、`EXPLAIN ANALYZE`、逃逸函数、锁语句）在两个方言下**分类完全一致**；唯一差别是 `FILTER (WHERE …)` 只有 PG 方言可解析。
→ 结论：换方言**不改变安全性**，只改变可解析面；因此"每 Provider 一个 Guard 算法"没有收益，"Provider 贡献策略数据"有收益。

### 环境前置

- 本机 Docker 可用；`postgres:16` 镜像已拉取（实现阶段的契约测试仍会用到它）。
- 盘问阶段为取得上面的实测数据，临时起过一个探针容器与一组仓库外的探针脚本；**它们与实现无关，已清理**（`postgres:16` 镜像本身保留）。实现阶段的 PostgreSQL 契约测试走 Testcontainers，自起自关，不复用探针容器。
