Labels: ready-for-agent

# sharpdb-oracle-read-consistency: 消除 Oracle 测试套件的 ORA-01466 竞态

## Problem Statement

Oracle 契约测试套件会**偶发**整片翻红：一次运行里 55 个用例以同一条 `ORA-01466: unable to read data - table definition has changed` 失败，而这些用例耗时全是 `[1 ms]`——它们继承的是**集合 fixture 初始化抛出**的那一个异常，不是 55 个独立故障。CI 上撞到过，本地几乎撞不到，于是它看起来像"偶发环境噪声"，被当作可以忽略的 flake。

它被**误判**了两次，两次都记录在案：

1. **"约 15% 的偶发"**——错。实测：致病动作是 `SET TRANSACTION READ ONLY`，它把读一致性快照钉在**会话当下的 SCN** 上；若该 SCN 落后于最近一次 DDL 的提交点，读该表**必然**报 1466。**零间隔即 100% 必现**（多次测到 25/25、60/60）。所谓"15%"只是套件里 seed 最后一条 DDL 与首次读之间**自然隔了十几条语句**造成的稀释；间隔 2 秒即归零。
2. **"夹具暖场读一次就能让 schema 变可读"**——错。实测：**没有任何语句能同步会话 SCN**（读 `dual`、读真视图 `all_objects`、连读 3~4 次、换连接读——全部 25/25 挂）。会话 SCN **只随时间自然追平**（≈1–2 秒）。所以现有暖场的立论（"warm up the schema"）不成立：它唯一在起作用的是**消耗了时间**，而它的重试窗口（5 次线性退避 ≈1 秒）**不足**，故仍残留。

对用户可见的后果：一个**正确实现**会被测试基础设施自造的噪声随机判红；红灯既可能来自真回归、也可能来自这个竞态，而报告**看不出来**是哪种；每次红灯都要人去深挖 SCN 机制才能排除。

## Solution

把这条 Oracle 引擎的读一致性竞态**从测试里消除**，而不是"降低它的发生概率"，也不把它藏起来：

1. **定性地承认它**：这是一条 read-consistency race——`SET TRANSACTION READ ONLY` 钉住的快照 SCN 可能早于最近 DDL 的提交点。删掉"引擎自称 transient（瞬态）"这个**站不住**的理由（它并非瞬态，而是在同一事务内**持续存在**）。
2. **在唯一咽喉上收敛**：给 Oracle 测试 fixture 加一个方法，把"**经 Provider 开只读连接 + 执行一次读**"作为**一个整体**执行，遇 1466 **重开连接**重做（新连接 = 新只读事务 = 新快照 SCN），直到成功或用尽预算。
3. **把 4 个真实站点接上咽喉**：4 个"`SeedDdlAsync` 建表 → 立刻经 Provider 读回新表"的 Oracle 专属测试，改走这个咽喉。
4. **暖场改造而非删除**：暖场继续存在，但重新定性为**共享 schema 的时间沉降点**（它保护共享套件在 fixture 之后的首个读），并换成同一个宽预算；共享套件代码**一行不改**。
5. **不动产品代码**：`OracleDatabaseProvider` 不改。生产上并发 DDL 导致的 1466 是引擎**正当的回答**，必须作为数据库失败**冒泡**给调用方；在 Provider 层吞掉它会给用户一个**假的一致视图**，与"只读"的承诺相悖。

## User Stories

### 仓库维护者（开发者）

1. As a developer, I want the Oracle suite to be **判定确定**——健康引擎上要么稳定全绿、要么响亮地红，so that 一次红灯一定意味着真回归，我可以直接信它。
2. As a developer, I want the suite to stop producing a random red that is engine noise and not my change, so that I stop paying the tax of re-running and re-diagnosing CI.
3. As a developer, I want the ORA-01466 fault to be eliminated **at its mechanism**（快照 SCN 早于 DDL 提交点），而不是被"多试几次"稀释，so that the flake rate is 0 and not "small".
4. As a developer, I want the retry to **reopen the connection** rather than re-read on the same read-only transaction, so that the retried read gets a **fresh** snapshot SCN instead of the stale one the transaction pinned.
5. As a developer, I want the retry budget to be **≈ 5 秒**（实测归零的窗口），so that a read that hits the race actually succeeds instead of exhausting ~1 秒 and throwing.
6. As a developer, I want the retry to be **只在踩中 1466 时才付出时间**，so that the healthy path costs zero extra milliseconds.
7. As a developer, I want a read that keeps failing 1466 **past the budget to throw as-is**, so that a genuinely unreadable database still fails loudly instead of being retried away.
8. As a developer, I want **一个** seam 覆盖全部 4 个竞态站点, so that no site is left unprotected and the fix is not copy-pasted four times.
9. As a developer, I want the seam to live in the **existing** Oracle fixture, so that no new architecture layer is invented and the codebase's seam count stays minimal.

### Oracle 测试的作者

10. As an author of an Oracle-specific test, I want a helper that runs "open a read-only connection and read" with the race already handled, so that my test asserts Oracle behaviour, not the engine's SCN timing.
11. As an author, I want the helper to take the read as a delegate, so that I can pass `DescribeTableAsync` / a raw command / any read through the same chokepoint.
12. As an author, I want the helper to accept the target connection string explicitly, so that the shared schema and the spare schema both reach it through one method.
13. As an author of the four affected tests, I want them to keep asserting what they assert today (identity column reported, defaults reported, unquoted name lower-cased, quoted name as-stored), so that fixing the race does not weaken coverage.

### 维护既有决策的人

14. As a maintainer, I want the shared contract suites (`ProviderContractSuite` / `QueryContractSuite`) **untouched**, so that the other three engines are provably unaffected.
15. As a maintainer, I want `IContractDatabase` **unchanged**, so that SQLite/MySQL/PostgreSQL fixtures need no edit.
16. As a maintainer, I want `OracleDatabaseProvider` **unchanged**, so that the production behaviour (1466 surfaces as a database failure) is preserved and ADR 0010 / 0002 hold.
17. As a maintainer, I want the fix to touch **only** the Oracle test project.

### 未来的读者

18. As a future reader, I want the fixture's XML docs to state the **measured** mechanism (snapshot SCN pinned by read-only txn; statements do not sync a session SCN; only time does; ≈2 s to converge), so that the next person does not re-derive it or re-believe a wrong story.
19. As a future reader, I want the word "transient" **removed** and the "warm-up proves readability" claim **corrected**, so that the code no longer asserts a reason that a probe has falsified.
20. As a future reader, I want the retry budget's **why** written down (measured ≈5 s window, ~1 s is not enough), so that nobody "optimises" it back down to 5 attempts × linear backoff.
21. As a future reader, I want to be told **why this is not an ADR**, so that the decision to keep test-infrastructure rationale in code comments rather than `docs/adr/` is deliberate and legible.

## Implementation Decisions

### 决策面（来自本次 grill，逐条）

- **目标**：判定确定性——健康引擎上要么稳绿要么响亮红；偶发红即不合格。**不是**"降低发生率"。
- **性质**：这是**测试缺陷**，不是产品缺陷。生产上并发 DDL 引起的 1466 是引擎正当回答，必须冒泡（ADR 0010）。
- **改哪一层**：**只改 Oracle 测试项目**。不动共享契约套件、不动 `IContractDatabase`、不动 `OracleDatabaseProvider`。
- **消除 vs 容忍**：消除机制；「加长重试窗口」单独不算解决，但**带足额窗口的重开重试**是消除机制（它把新快照 SCN 交到读上，直到脱离竞态）。
- **记录**：机制写进**夹具的 XML doc**；**不开 ADR**（测试基础设施、可逆，本仓 ADR 记录产品行为决策）。

### 要改的模块与接口

- **`OracleContractDatabase`（fixture，既有类）**——本 spec 唯一新增 seam：
  - 新增 `ReadThroughProviderAsync<T>(Func<DbConnection, Task<T>> read, string connectionString)`：在 `connectionString` 上经 `Provider.CreateReadOnlyConnectionAsync` 开只读连接，把连接交给 `read`；**捕获 `OracleException.Number == 1466`**，重开连接重做整段读；成功即返回；用尽预算则原样抛出。
  - 把现有的暖场重试**收敛到同一重试原语**上（同一个 1466 判定与预算），使暖场与咽喉共享一套行为，而不是两份各写各的。
  - **暖场重新定性**：其 XML doc 从"proves the seeded schema is readable / engine says transient" 改为实测结论——它是**时间沉降点**（读不推进 SCN，只有时间能），并说明为何预算要 ≈5 s。保留暖场（它保护共享套件在 fixture 后的首个读）。
  - `TableDefinitionChanged = 1466` 常量的 XML doc 删掉 "transient" 措辞，改为 read-consistency race 的定性。

- **`OracleSpecificTests`（Oracle 专属测试）**——4 个站点接上咽喉：
  - `An_identity_column_is_reported_as_autoincrement`
  - `A_column_default_is_reported`
  - `An_unquoted_name_is_reported_in_lower_case`
  - `A_quoted_lower_case_name_is_reported_as_the_catalogue_spells_it`

  这 4 处形状相同：`SeedDdlAsync(CREATE TABLE ...)` → `OpenSpareReadOnlyAsync()` → `Provider.DescribeTableAsync(...)`。改造后：建表仍走 `SeedDdlAsync`（写连接），读改为经 `ReadThroughProviderAsync(conn => Provider.DescribeTableAsync(conn, name, ct), database.SpareConnectionString)`。

### 技术澄清（实测数字，构成参数选择的依据）

- **致病动作唯一**：`SET TRANSACTION READ ONLY`。测得间隔与结果：

  | 场景 | 1466 率 |
  | --- | --- |
  | 零间隔 + 只读事务读新表 | **100%**（25/25、60/60 多次） |
  | 间隔 2 秒后同读 | 0% |
  | **普通 autocommit 读**（不显式开只读事务） | **0%** |
  | 只读事务内先读 `dual` 再读新表 | 100% |
  | 只读事务建在刚做 DDL 的连接上 | 100% |
  | 事务前同连接先发 1/3/4 条语句（读 dual、读真视图） | 100% |
  | 换一条连接先发语句再关掉 | 100% |
  | **关闭连接池**（`Pooling=false`） | 100% |
  | 重试 ×20 @250ms 或 ×100 @50ms（上限 ≈5 s） | **0%** |
  | 重试 ×5 线性退避（上限 ≈1 s） | 残留 ~28%–72% |

  推论：**没有任何语句能同步会话 SCN**；只有时间能（≈1–2 秒追平）。重试**必须重开连接**（新事务、新快照），因为同一只读事务里的重读永远是同一个陈旧快照。

- **DML 在只读事务内**（`The_read_only_transaction_refuses_a_write`）：实测 INSERT/UPDATE/DELETE **100% 抛 `ORA-01456`**，从不抛 1466——写拒绝早于读一致性检查。**该站点不在风险面，不需包裹。**

- **`A_ddl_statement_escapes_the_read_only_transaction` 不在风险面**：其 DDL 隐式提交**结束了**只读事务，随后的 `INSERT`/`SELECT` 已在 autocommit 下；实测该形状 25/25 正确。**不需包裹。**

- **共享契约套件不在风险面**：`ProviderContractSuite` / `QueryContractSuite` 只读**共享 schema**，而共享 schema 在 fixture seed 之后**再无 DDL**。

### 参数

- **重试间隔**：固定 **250 ms**（不必指数退避——卡点只有约 2 s 一个台阶，线性足够且更可预测）。
- **重试上限**：**约 20 次（总预算 ≈5 s）**，实测归零；超预算**原样抛出**。

## Testing Decisions

- **何为好测试**：只断言**外部行为**，不断言实现细节。本次修复的"外部行为"是：**套件在健康引擎上判定确定**——不因引擎 SCN 时序而偶发红。
- **回归测试的 seam（red-capable，已实测可稳定变红）**：刚证实"零间隔 DDL → 只读读 = 100% 必现"，因此可写一个**确定性必红**的测试证明修复有效：
  1. 经 `SeedDdlAsync` 建一张新表（spare schema）；
  2. **立刻**经 `ReadThroughProviderAsync` 执行 `DescribeTableAsync` 读它；
  3. 断言读成功、且报告了正确的列。
  ——修复前该形状**必红**（实测 25/25），修复后**必绿**。这是真正的 red-capable seam，不是浅层假测试。
- **要测的模块**：`OracleContractDatabase.ReadThroughProviderAsync`（含其 1466 重试与预算）——用一个确定性必红的用例覆盖。
- **连带验证**：4 个改造站点各自仍断言其原有行为（identity/default/quoting/case），覆盖不丢失。
- **Prior art**：
  - 既有 fixture 的 `WarmUpAsync` 就是"对 1466 重试"的**前身**，本 spec 把它抽成共享原语。
  - 既有 `OracleSpecificTests` 的 `SeedDdlAsync` / `OpenSpareReadOnlyAsync` / `DropAsync` 是 spare-schema 测试的既有辅助形状，改造沿用同一形状。
  - 既有共享套件用 `SkippableFact` + `SkipWhenUnavailable()` 表达"Docker 缺席则跳过"，本次不改这套跳过机制。
- **验证方式**：改完后**整套 Oracle 套件连续多次运行**（本机 + 观察 CI），确认**零 1466**；并确认另三引擎测试项目**未被触碰**（应零 diff）。

## Out of Scope

- **不修改 `OracleDatabaseProvider`**，不引入面向生产的 1466 重试。生产上并发 DDL 引起的 1466 继续作为数据库失败冒泡。
- **不修改共享契约套件**（`ProviderContractSuite` / `QueryContractSuite`）与 `IContractDatabase`。
- **不改** SQLite / MySQL / PostgreSQL 的 fixture 与测试。
- **不设 ADR**；不改 `GLOSSARY.md`（read-consistency race 是 Oracle 引擎细节，不是本项目领域概念，加入会污染词汇表）。
- **不改进 CI 报告可读性**（"是 1 个 fixture 故障、不是 55 个坏用例"的可诊断性）——本次不做，若需要另开一笔。
- **不缩短** Oracle 套件本身的运行时间；不引入新的容器策略。

## Further Notes

- **这是一次"实测推翻旧说法"的修复**：两条错误论断（"约 15% 偶发"、"暖场读让 schema 可读"）曾在仓库记忆与代码注释里被当作事实。修复的过程同时是**更正记录**的过程——探针（本机、仓库外、跑完即删）给出的数字是本次决策的唯一依据。
- **诚实边界**：`The_read_only_transaction_refuses_a_write` 是否可能撞 1466，此前只在**读**上测过；本次用 DML 专测补上了这一格，结论是 01456 恒先抛出。这条"不实测就不下结论"正是这次翻车的根源，特此留痕。
- **实测环境**：Oracle XE 21（`gvenzl/oracle-xe:21-slim-faststart`），本机 Docker，`Oracle.ManagedDataAccess.Core 23.26.301`。`DBMS_FLASHBACK` 在 XE 上不可用（企业版功能），故 SCN 无法直接读出，机制由行为对照推断。
- **与既有 ADR 的关系**：ADR 0014（Oracle layer 2 事务级只读 + DDL 洞）不受影响——本 spec 只动测试如何**穿过**那条只读路径，不动路径本身。ADR 0010（tool 错误即结果、不吞）被本 spec **强化**：正因为生产要冒泡，修复才只落在测试。
