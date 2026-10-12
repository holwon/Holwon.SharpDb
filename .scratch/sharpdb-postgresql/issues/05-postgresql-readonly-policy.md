# 05: PostgreSQL 只读策略（方言 + 文件读取函数禁用）

**What to build:**

PostgreSQL 的策略被真正用起来：使用 PostgreSQL 方言，使 `FILTER (WHERE …)` 这类常见只读聚合写法不再被误拒；同时把该引擎上"读服务器本机文件"的函数列入禁用表——它们在只读会话下**实测仍然可用**，而 Guard 今天会把它们当成普通 `SELECT` 放行。堵住之后，即使使用者用的是超级用户账号，这些函数也会在 Guard 处被拒。

MySQL 一侧必须原样不动：反引号标识符仍被放行。

**Blocked by:** 02

**Status:** ready-for-agent

- [ ] PostgreSQL 策略使用 PostgreSQL 方言；`SELECT count(*) FILTER (WHERE …) FROM t` 被**放行**。
- [ ] 同一语句在 MySQL/通用策略下仍被拒——证明换方言确实只改变可解析面，而两方言对写操作的分类一致。
- [ ] PostgreSQL 策略禁用该引擎的文件读取函数（`pg_read_file` / `pg_read_binary_file` / `pg_ls_dir` / `lo_import` / `pg_stat_file`）；它们经 Guard 被拒，理由是清楚的名字。
- [ ] 反引号标识符在 MySQL 策略下仍被放行（不回归）。
- [ ] 写语句（含 `WITH … INSERT`、`SELECT … INTO`、`EXPLAIN ANALYZE <DML>`）在两套策略下都被拒。
- [ ] 这些断言以服务 + 策略的形式写，不依赖任何具体 Provider 类型。
