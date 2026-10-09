# 03: Guard —— 只读领域策略

**What to build:** 一个纯函数式的只读检查：给它一段 SQL，它回答"能否执行"，并在拒绝时给出 AI 看得懂的明确理由。它是三层只读防御里**面向 AI 的快速反馈层**。

**Blocked by:** 01

**Status:** ready-for-agent

- [x] 用 SQL 解析器把语句转成语法树来判定，**不用**关键字前缀猜测
- [x] 恰好**一条**语句才通过——堆叠语句被拒绝
- [x] 允许：SELECT（含 `WITH ... SELECT` 形式的 CTE）、EXPLAIN、SHOW
      —— ⚠️ **只读 PRAGMA 做不到**，见下方 Comment：「PRAGMA 一律拒绝」
- [x] 拒绝清单逐条覆盖（每条一个测试）：
  - [x] 注释隐藏的写关键字
  - [x] `WITH ... INSERT`（`... UPDATE/DELETE` 在 0.6.5 里是语法错，随解析失败拒绝）
  - [x] `EXPLAIN ANALYZE` 后接 DML
  - [x] `SELECT ... INTO`（`SELECT ... INTO OUTFILE` 在 0.6.5 里是语法错，随解析失败拒绝）
  - [x] PRAGMA 赋值形式（随「一律拒绝 PRAGMA」一并拒绝）
  - [x] 逃逸函数（含嵌套在其它函数实参里的调用）
  - `REPLACE INTO` —— 0.6.5 里是语法错，**无需** AST 规则
- [x] `EXPLAIN` 需**递归检查内层**语句——`EXPLAIN INSERT` 必须被拒
- [x] 拒绝文案告诉使用者只有 SELECT / EXPLAIN / SHOW 可用
- [x] 无 I/O、无数据库依赖——纯领域逻辑
- [x] 解析用**固定**的通用方言 + 超集允许清单，不依赖任何具体数据库的方言

---

**验收说明**：断言"这条 SQL 被接受 / 被拒绝 + 理由文案"，**不断言**语法树的具体形状。

**已知接受的缺口**：通用方言不接受方括号标识符，用方括号写的 SQLite SQL 会被误拒——罕见，接受。

---

## Comments

**2026-10-09 — 完成。** 验收项全部落地，`dotnet build SharpDb.slnx -warnaserror` 0 警告 0 错误；`dotnet test SharpDb.slnx` 双绿（`SharpDb.Core.Tests` **91** 例——全部为本票新增，`SharpDb.Tests` 12 例，失败 0；合计 103）。

交付物：`src/SharpDb.Core/Guard/ReadOnlyGuard.cs`（策略）、`ReadOnlyDecision.cs`（结论）、`tests/SharpDb.Core.Tests/Guard/ReadOnlyGuardTests.cs`（91 例）。

### 本票最要紧的一条：**允许清单是类型白名单，不是关键字黑名单**

放行的只有 `Select` / `Explain` / 白名单内的 SHOW 各形态；**其余一切语句类型落到拒绝侧**。这决定了失效方向是好的那一侧：判断错只会**多拒一条只读查询**（可恢复），**不会漏放一条写操作**。

实测支持这个选择的对照——黑名单做法的两个失败方向都能复现（均已写成测试）：

- **误杀**：`SELECT created_at FROM users`、`WHERE deleted_at IS NULL`、`action = 'update'` 都是普通列名，关键字匹配会拒掉它们。
- **漏放**：`WITH x AS (...) INSERT INTO t ...` 被解析器判为 **`Select`**，写操作藏在 `Query.Body` 里。只看节点类型会放行，必须走进 Body 才抓得住。

### 三处实测修正（相对本票原文）

用 SqlParserCS 0.6.5 逐条跑过（Generic / MySQL / SQLite 三种方言），三处原文与实测不符：

| 原文 | 实测 | 处理 |
|---|---|---|
| 允许只读 PRAGMA | 带括号的只读形式**多为语法错**，见下 | **一律拒绝 PRAGMA** |
| `EXPLAIN ANALYZE` 后接 DML 须拒 | `EXPLAIN ANALYZE SELECT 1` **能解析**，`Analyze=true` | 递归分类内层语句即可，`ANALYZE` 的两种拼写自动一致 |
| `SELECT ... INTO OUTFILE` 须拒 | **解析失败** | 随解析失败拒绝，无需单独规则 |

**PRAGMA 这条是唯一的能力缩减**，但零成本：SQLite 的内省有等价的表值写法 `SELECT * FROM pragma_table_info('users')`，它解析为普通 SELECT、被放行。**ticket 04/05 的内省 SQL 请用这个形式**，不要用 `PRAGMA table_info(...)`。

**订正一处我先前写错的机制说明。** 我最初在 Comment 里写「`PRAGMA table_info(users)` 三种方言一致解析失败 → 说明方言参与只读分类 → ADR 0005 的论断不准确」。**这是错的**，两处都错，已实测订正：

- 失败原因是**解析器要求括号里的实参是一个 value**，而 `users` 是裸标识符 → 语法错。加引号即可通过：`PRAGMA table_info('users')` 在**两种方言下都解析成功**并得到一个 `Pragma` 节点，随后被白名单拒绝。
- 因此 **ADR 0005 的原文是对的**——方言确实不参与分类，GenericDialect 与 SQLiteDialect 逐条一致。本票对它的「更正」作废，**不要**按先前的说法去改 ADR 0005。

### 需要更新的既有文档（**尚未改**，留待决定）

本票把一个 spec 验收项从「允许」改成了「拒绝」，而三处上游文档仍写着旧行为，现在与代码矛盾：

| 文档 | 原文 | 现状 |
|---|---|---|
| `spec.md` 用户故事 47 | 「我想执行只读的 `PRAGMA`（SQLite）」 | 未实现（表值写法是等价替代） |
| `spec.md` 实现决策「只读：三层」 | 允许「只读 `Pragma`（拒绝赋值形式）」 | 现为一律拒绝 |
| `docs/adr/0005` | 允许清单含「只读 `Pragma`」 | 现为一律拒绝 |
| `docs/adr/0002` | 把 `REPLACE INTO`、`LOAD DATA`、`HANDLER`、`LOCK TABLES`、`DO` 列为「需要额外处理的绕过路径」 | 实测在 0.6.5 里是**语法错**，解析器已挡下，无需 AST 规则 |

最后一行的含义：**ADR 0002 与 `docs/research/readonly-sql-enforcement.md` 的绕过清单被高估了。** 那份清单来自 Rust `sqlparser-rs` 的覆盖面推断，但 C# 移植版的语法覆盖更小——几种 MySQL 语句在解析阶段就失败。这不削弱 ADR 0002 的结论（解析器仍不够、需要引擎层兜底），只是把「解析器独自拦不住的那些」和「解析器根本读不懂的那些」混淆了。

**我没有擅自修改 ADR 与 spec**——`docs/agents/domain.md` 要求与既有 ADR 矛盾时显式指出而非静默覆盖。上面这张表就是那一「显式指出」。要不要改这几处，请拍板。

### `ANALYZE` 为什么不看标志位

`Statement.Explain.Analyze` 是**真的**（`EXPLAIN ANALYZE SELECT 1` 会置位），但**不能用它做判断**：括号写法 `EXPLAIN (ANALYZE) SELECT 1` 把标志位留成 `false`，而它对引擎是同一件事。

所以决策不看拼写——**递归分类内层语句**。`EXPLAIN ANALYZE INSERT` 与 `EXPLAIN INSERT` 于是走同一条规则被拒，`EXPLAIN ANALYZE SELECT` 也因此在两种拼写下得到同一个答案。

按原文实现（`Analyze == true` 就拒）会**漏掉括号写法**：`EXPLAIN (ANALYZE) INSERT INTO t ...` 会被放行。

### 实现要点

- **遍历只放行纯 SELECT，其余 query body 一律拒绝。** `FindWrite` 只对 `SelectExpression` 继续下潜；`Insert`、`SetOperation`、`QueryExpression` 各有规则，**其余（如 `VALUES (1)`）落到拒绝侧**。这修掉了初版 `_ => null` 的一个漏洞：语料声明之外的新 body 类型会被**静默放行**，与白名单的安全方向相反。现在新增类型自动被拒。
- **函数逃逸自己做递归**：解析器的 `Visitor` 会走语句的子树，但**不下潜函数的实参**——`CONCAT('x', LOAD_FILE('/x'))` 只报外层 `CONCAT`。所以 `FunctionWalker` 自己递归 `FunctionArguments`，并**在命中时 `Break`**（线性、无栈深度风险，且不会因深嵌套而挂）。
- **拒绝清单只有 4 个函数**（`LOAD_FILE` / `GET_LOCK` / `SLEEP` / `BENCHMARK`），按**最后一段**名字比较（`main.load_file` 与反引号写法都能识别）。刻意短：它是白名单网的补丁，不是自成一套的策略，引擎层仍在后面兜底。
- **`SELECT ... FOR UPDATE` 被拒**（`Query.Locks` 非空）。本票原文没写，但它会取锁，属于写语义。
- **`DESCRIBE t` / `EXPLAIN t` 被拒**：解析器把它们建模为 `ExplainTable`（与 `EXPLAIN SELECT` 不同的类型），且**不带内层语句**可检查，所以落在白名单之外。
- **数值崩溃已挡**：空串传给 0.6.5 会抛 `ArgumentOutOfRangeException`（不是 `ParserException`），故先做 `IsNullOrWhiteSpace`，解析处**捕获所有异常**——未预见的解析器故障绝不能变成放行。

### 代码审阅后的两处修正（已落地）

1. **`FindWrite` 的兜底分支**（上面第一条）：`_ => null` 改成拒绝。原来 `VALUES (1)` 会被放行。
2. **一处测试注释的归因是错的**（已重写）：我先前写「括号 PRAGMA 三种方言都解析失败」，实际是「实参必须是 value，裸标识符 `users` 不行」。加引号能解析，然后被**策略**拒绝——两条路径的结论都是拒绝，但原因不同，且只有后者能说明「方言不参与分类」。

### 已知的实现耦合（有意保留）

拒绝文案里的语句种类取自 AST 类型名（`Insert` / `Drop` / `SetVariable` …），而**测试断言了这个字符串**。严格按本票「不断言语法树形状」的验收说明，这里越了线。

保留的理由：**行为断言要求一个稳定的字串，而唯一的稳定字串就是解析器的类型名。** 若不断言它，就只能断言「含 `Statement rejected`」——那种断言对「拒绝理由说清了是什么」这个验收项（「拒绝文案告诉使用者只有 SELECT / EXPLAIN / SHOW 可用」）是假信心。

代价说清楚：若将来换解析器，类型名会变，这批断言需要跟着改（**是编译期/运行期的显式失败，不是静默错**）。类型名本身对 AI 也算可读（`Insert is not allowed` 比 `the statement is not allowed` 有用），故不引入「类型 → SQL 关键字」的映射表。

### 已接受的缺口

- **方括号标识符**（原文已列）：`SELECT * FROM [my table]` 被拒。已加测试固定这个行为，以免它悄悄改变。
- **`EXPLAIN ANALYZE SELECT 1` 被放行**。这是**偏离** spec 验收项 53 的**字面**要求——它命名的目标是「借 EXPLAIN 执行写操作」，而写操作确实被拒了。额外付出的代价是只读查询的 `EXPLAIN ANALYZE` 也被放行。理由：一、只读，无状态改变；二、引擎层只读仍兜底；三、代价只在**本地只读服务器**上的执行成本，是使用者的资源。已写测试固定此行为（`Analyze_of_a_read_only_query_is_allowed`），以便它是有意为之而非疏漏。

`bash tools/check-retired-identifiers.sh`（经 Git 的 bash）通过——本票不涉及新退役标识符，仍跑一遍确认无回归。
