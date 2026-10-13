# 03: Core 认得 mssql 方言，Guard 拒 rowset function

**What to build:** Guard 认得 SQL Server 的只读 SQL：`TOP n` 与 `[bracket]` 标识符被接受；四个 rowset function（`OPENROWSET` / `OPENQUERY` / `OPENDATASOURCE` / `OPENXML`）被拒并给出清晰理由。另外四个引擎的行为逐字节不变。此票完成后，mssql 的 Policy 才能声明一个真实存在的方言值。

**Blocked by:** 01 (rowset function 能否被遍历器看见、以及解析器的 MS SQL 方言的实际行为，都由实测决定)

**Status:** resolved

- [x] `SqlDialect` 新增 mssql 值；`ReadOnlyGuard` 的方言映射指向解析器的 MS SQL 方言（锁 `SqlParserCS 0.6.5`，不升预发布版）
- [x] 未知方言值仍**响亮失败**，不回落到没人选的方言
- [x] Guard 在 mssql 策略下接受 `TOP n` 与 `[bracket]` 标识符（有断言）
- [x] Guard 拒绝四个 rowset function，拒绝理由**点名是哪个函数**（有断言）
- [x] 若实测证明遍历器看不见 `FROM` 里的 rowset function，本票含 Core 的那处补丁，且补丁有断言
- [x] 另外四个引擎的 Guard 行为不变（既有测试全绿）
- [x] 复核既有"方括号标识符是可接受缺口"的断言与 ADR 0005 的措辞：缺口对通用方言**仍在**，对 mssql **已闭合** —— 不是改断言，是改它解释的对象

## Comments

### 2026-10-13 — 完成（`7124517`，复审修复 `cceaef6`）

`SqlDialect.MsSql` 新增；`DialectFor` 映射到 `new MsSqlDialect()`（解析器自带，实测存在）；未知值仍抛 `ArgumentOutOfRangeException`。

**Core 补丁（实测逼出来的）**：遍历器只看 `Expression.Function`，而 `FROM` 里的 rowset function 被解析成 `TableFactor.Table`（名字是函数、`Args = TableFunctionArgs`）——**表达式钩子根本不触发**，`SELECT * FROM OPENQUERY(x, 'SELECT 1')` 会被当作普通 SELECT 放行。补丁加 `PreVisitTableFactor`。

**复审修复（`cceaef6`）**：初版补丁**只按名字**判定，把任何名为禁用项的表都拒了。复审时实测（MySQL 8.0 与 SQLite）：**字面名为 `sleep` 的表可以被 `SELECT … FROM sleep` 正常读取**——该位置两个引擎都按表解析。所以名字本身不构成函数调用的证据，改为**按实参列表**区分：带 `TableFunctionArgs` 的才算调用。两侧都有配对断言（读得到 / 仍拒绝），互相锁定。

断言在 `ReadOnlyGuardTests`：`TOP`、`[bracket]`、限定名、`sys.tables` 均可读；四种 rowset 写法被拒且**理由点名函数**；三种解析不了的写法按"无法解析"拒；`EXEC` 按语句种类拒；名为 `sleep`/`benchmark`/`get_lock` 的表可读。

ADR 0005 的 `[bracket]` 缺口已复核并加注：缺口**属于方言**、不属于 Guard——对通用方言仍在（断言保留），对 mssql 已闭合。
