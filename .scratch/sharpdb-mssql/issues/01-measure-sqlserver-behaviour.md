# 01: 实测 SQL Server 在容器里的行为

**What to build:** 一张把本 feature 依赖的每条事实钉成实测结论的票。这些结论决定 Core 要不要改、Provider 的内省 SQL 怎么写、测试容器怎么起、只读账号怎么授。结论写进本票的 Comment（仓库惯例：实测细节去 `issues/NN-*.md` 的 Comment）。本票无入库代码产出。

**Blocked by:** None (can start immediately)

**Status:** resolved

- [x] 容器 recipe 落定：镜像**钉成 CU tag 或 `@sha256:` 摘要**（不用浮动的 `2022-latest`，本仓库规矩：浮动 tag 会被静默升级改掉共享断言在断言什么）；记录就绪判定方式、内存需求、SA 密码复杂度要求、`TrustServerCertificate=True` 的必要性
- [x] 实测只读 intent 在 standalone 实例上是否真无副作用（连接成功、只读查询照常、写是否被拒）
- [x] 实测 `OPENROWSET` / `OPENQUERY` / `OPENDATASOURCE` / `OPENXML` 在 mssql 方言下能否解析、解析成 AST 里的什么、Guard 的函数遍历器**能否看见**它们 —— 给出"Core 要不要补一条分支"的明确结论
- [x] 实测 `TOP n` 与 `[bracket]` 标识符在解析器的 MS SQL 方言下被接受
- [x] `sys.*` 内省 SQL 跑通并记录真实形状：表/视图清单（含 `MS_Description` 注释）、列（类型、nullability、默认值、`IDENTITY` 自增）、主键列序、复合外键的列配对顺序、索引唯一性与列序（排除主键索引）、视图列
- [x] 记录默认 schema 的名字与 `list_databases` 排除四个系统库（`master` / `tempdb` / `model` / `msdb`）后的结果
- [x] 找到一条 Guard 允许的慢只读 SELECT（超过 1 秒），供超时断言使用
- [x] 记录只读账号的最小授权集，并实测**不授 `VIEW DEFINITION` 时** `list_tables` / `describe_table` 会少给什么（元数据按权限过滤是本引擎的运维要点）
- [x] 结论以"实测值 + 与直觉相反之处"的形式写进 Comment，供后续票直接引用

## Comments

### 2026-10-13 — 实测完成，无入库代码产出

全部结论测自 `mcr.microsoft.com/mssql/server@sha256:4402d880dd4cbfa7d8705e56a86cd6c88da80a1f6bbbe741f999e76264a090`（SQL Server 2022 CU27，16.0.4295.3，Developer，Ubuntu 22.04）。探针脚本在仓库外的临时目录，用文件式 C# app（**必须在仓库外运行**，否则被 CPM 拦为 NU1008）。

**容器**：镜像 tag `2022-latest` 是浮动的（实测期间就移到了 CU27），所以**钉摘要**。容器需约 2 GB 内存；`MSSQL_SA_PASSWORD` 必须过引擎自己的复杂度策略。工具只在 `/opt/mssql-tools18/bin`（**没有** `/opt/mssql-tools`），且该 `sqlcmd` 默认强制加密，**必须加 `-C`** 才连得上自签证书的服务器。

**只读 intent（第 2 层）——与直觉相反之处**：`ApplicationIntent=ReadOnly` 在 standalone 实例上被接受且读照常，但**同一条连接上 `CREATE TABLE` 成功**。所以它不是 enforcement，只是意图。

**方言 / 解析器（`SqlParserCS 0.6.5`）**：`MsSqlDialect` 存在。`TOP n` 与 `[my table]`、`[dbo].[readings]`、`TOP 5 [id]` 都被接受；**通用方言对方括号报 `ParserException: Expected identifier, found [`**（这就是既有"可接受缺口"的另一半——缺口属于方言，不属于 Guard）。`EXEC sp_executesql` → `Execute` 语句，被许可清单拒（无需入禁用表）。

**rowset function —— 遍历器看不见写在 `FROM` 里的它们（Core 必须改）**：

| 写法 | 解析 | Guard 现象 |
|---|---|---|
| `OPENQUERY(x, 'SELECT 1')` | ✅ | `TableFactor.Table`（名字是函数、`Args = TableFunctionArgs`）→ **表达式钩子不触发** |
| `OPENROWSET('SQLNCLI','Server=x','SELECT 1')` | ✅ | 同上 |
| `OPENXML(@h, '/root/row')` | ✅ | 同上 |
| `OPENROWSET(BULK '…', SINGLE_CLOB)` | ❌ 语法错 | 归入"不可解析即拒绝" |
| `OPENDATASOURCE(…).db.dbo.t` | ❌ 语法错 | 同上 |
| `OPENXML(…) WITH (a int)` | ❌ 语法错 | 同上 |

**结论：Core 要补一条分支**——`Visitor.PreVisitTableFactor` 确实触发并能看见该形状（实测）。补丁已落 ticket 03。

**内省**：默认 schema 是 `dbo`（`SCHEMA_NAME()`）。对象靠 `sys.objects o JOIN sys.schemas s` + `type IN ('U','V') AND is_ms_shipped = 0`；**不过滤时的索引目录会返回数百张内部表**（队列/账本/分配映射）。注释在 `sys.extended_properties` 的 `MS_Description`（列用 `minor_id = column_id`），必须 `CONVERT(NVARCHAR(4000), value)`。列默认值在 `sys.default_constraints`，**回读是引擎自己的文本、带括号**（`DEFAULT ('none')` → `('none')`）。主键序用 `key_ordinal`（实测 `(b,a)` ✓）；复合外键配对用 `constraint_column_id`（实测 1=b、2=a ✓）；动作是 `CASCADE` / `SET_NULL` / `NO_ACTION` 的枚举文本。

**名字解析**：`OBJECT_ID('users')` 解析到 `dbo.users`，`OBJECT_ID('totals')` **为 NULL**，`OBJECT_ID('reporting.totals')` 解析——即引擎自己就是"默认 schema 优先、非默认必须限定"。这是 ticket 04 命名规则的依据（ADR 0017）。

**`list_databases`**：四个系统库（`master` / `tempdb` / `model` / `msdb`）在 `sys.databases` 里与用户库并列，**没有标志位可区分**，只能按名字排除。

**慢查询**：三路交叉连接 `sys.all_columns` 约 3.5 s（`COUNT(*)` 在本形状上**溢出 int**，必须 `COUNT_BIG`）；四路 12 s+。`CommandTimeout` **确实会打断语句**（实测报引擎超时）。

**授权集 —— 不授 `VIEW DEFINITION` 会少给什么（本引擎的运维要点）**：用一个只在 `dbo.users` 上有 `SELECT` 的登录实测，单变量对照：

| | 不授 `VIEW DEFINITION` | 授了之后 |
|---|---|---|
| `sys.objects` 报出的对象 | **只有 `users` 一个**（共 9 个） | 全部 9 个（含非默认 schema 的） |
| `sys.columns` 的列 | 只有它有权读的那些 | 相应的都可见 |
| `sys.default_constraints.definition` | **NULL** | `('none')` |
| `sys.all_columns` 等系统视图 | 照常可读 | 照常可读 |

即：**元数据按权限过滤**，漏授 `VIEW DEFINITION` 不会报错，只会**安静地少报**。已落 ticket 05 的实测断言与 README 的运维说明。
