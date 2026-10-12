# 05: PostgreSQL 只读策略（方言 + 文件读取函数禁用）

**What to build:**

PostgreSQL 的策略被真正用起来：使用 PostgreSQL 方言，使 `FILTER (WHERE …)` 这类常见只读聚合写法不再被误拒；同时把该引擎上"读服务器本机文件"的函数列入禁用表——它们在只读会话下**实测仍然可用**，而 Guard 今天会把它们当成普通 `SELECT` 放行。堵住之后，即使使用者用的是超级用户账号，这些函数也会在 Guard 处被拒。

MySQL 一侧必须原样不动：反引号标识符仍被放行。

**Blocked by:** 02

**Status:** resolved

- [x] PostgreSQL 策略使用 PostgreSQL 方言；`SELECT count(*) FILTER (WHERE …) FROM t` 被**放行**。
- [x] 同一语句在 MySQL/通用策略下仍被拒——证明换方言确实只改变可解析面，而两方言对写操作的分类一致。
- [x] PostgreSQL 策略禁用该引擎的文件读取函数（`pg_read_file` / `pg_read_binary_file` / `pg_ls_dir` / `lo_import` / `pg_stat_file`）；它们经 Guard 被拒，理由是清楚的名字。
- [x] 反引号标识符在 MySQL 策略下仍被放行（不回归）。
- [x] 写语句（含 `WITH … INSERT`、`SELECT … INTO`、`EXPLAIN ANALYZE <DML>`）在两套策略下都被拒。
- [x] 这些断言以服务 + 策略的形式写，不依赖任何具体 Provider 类型。

## Comments

### 2026-10-12

策略数据在票 02 随 Provider 一起声明了；本票把它**从两端各钉一遍**——Guard 那一端在 Core 测试里（策略是构造参数），Provider 那一端在 PG 项目里（Provider 交出去的那份策略就是 Guard 执行的那份）。

**为什么两端都要测**

这两件事的失败方式完全不同，任何一端单独测都盖不住另一端：

- 只测 "Guard 收到策略 X 就按 X 行为" → 如果 `PostgreSqlDatabaseProvider.Policy` 返回了通用策略（复制粘贴、或者漏改），所有 Guard 测试照样绿，而线上 PG 用户被误拒 `FILTER (WHERE …)`、且 `pg_read_file` 形同虚设。
- 只测 "Provider 声明了什么" → 如果 Guard 根本不读那个字段，同样全绿。

所以新增 `PostgreSqlReadOnlyPolicyTests`：用 `new ReadOnlyGuard(new PostgreSqlDatabaseProvider().Policy)` 构造——**Host 的装配方式**——再断言行为。这条缝一断，两边都会红。

**逐条落在哪里**

| 验收项 | 断言 | 位置 |
|---|---|---|
| PG 方言放行 `FILTER (WHERE …)` | `The_dialect_a_policy_names_is_the_one_the_statement_is_read_with` | Core（策略参数化）+ PG（Provider 策略） |
| 同一语句在通用策略下被拒 | 同上，同一方法内的反断言 | Core |
| 五个文件读取函数被拒 | `A_function_the_policy_denies_is_refused_wherever_it_appears` + `The_providers_policy_refuses_a_file_reading_function` | Core + PG |
| 反引号在 MySQL 策略下放行 | `Back_quoted_identifiers_stay_allowed_under_the_policy_mysql_declares` | Core |
| 写语句两套策略都拒 | `Every_write_form_is_refused_under_every_policy`（10 条 × 全策略）+ `A_write_is_refused_under_the_providers_own_policy` | Core + PG |
| 不依赖具体 Provider 类型 | Core 侧全部用匿名构造的 `ReadOnlyPolicy` | Core |

**顺带钉住的两条决定**

- **`nextval` / `setval` 不在禁用表里**（spec 的 Out of Scope）：它们写序列，只读事务下由引擎自己拒绝。`A_sequence_function_is_left_to_the_engine` 把这条决定固化下来——将来若有人往表里加这两个名字，测试会说这是**有意的改主意**而不是顺手添加。
- **反引号在 PG 策略下被拒**：`A_back_quoted_identifier_is_refused_under_this_policy`。这是"每引擎读自己的方言"的对称代价，写成断言以免被当成缺陷去修。

**只读机制本身也测了（票 02 的验收项在这里补全）**

共享套件用"试着写一条"来断言只读保证，那是对的，但它证明不了**跨事务**。PG 的坑恰好在这里，所以补了 `The_session_the_provider_opens_is_read_only_beyond_its_transaction`：先 `SELECT 1`（驱动 autocommit 下自成事务）再 `SHOW transaction_read_only`，得到 `on` 才算过。若 Provider 写的是 `SET SESSION TRANSACTION READ ONLY`，这条会得到 `off`——而"试着写一条"只会在第一条语句上通过。

另加两条引擎侧断言：`..._refuses_a_write_at_the_engine`（异常是 Npgsql 自己的 `PostgresException`，消息含 `read-only transaction`——证明是服务端拒绝而非 Guard）、`..._refuses_a_temporary_table`（临失表也是表，实测同样被拒）。

**验证**

- PostgreSQL 项目：**65 通过 / 0 失败**。
- Core 项目：策略参数化断言全绿。
- 全量：见下一条提交后的报告。
