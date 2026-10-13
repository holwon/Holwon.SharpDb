# 02: 把 4 个建表后即读的站点接上咽喉

**What to build:**

Oracle 专属测试里 4 个形状逐字相同的站点——「经写连接建一张新表 → 立刻经 Provider 读回该表」——改走 01 号票立起的读咽喉。它们的共同形状是：`SeedDdlAsync(CREATE TABLE ...)` → 打开一条只读连接 → `DescribeTableAsync(...)`。

改造后：**建表仍走写连接**（DDL 需要可写会话，不变），而**读改为经咽喉**执行——这条读因此自动获得"遇 1466 重开连接重做"的覆盖，不再可能被引擎的 SCN 时序随机判红。

4 个站点各自**保持原有断言不变**（identity 列被报为自增；列默认值被报出；未加引号的标识符被报成小写；加了引号的小写名按目录原样报出），覆盖**不丢失**。

**Blocked by:** 01（立咽喉）

**Status:** resolved

- [x] `An_identity_column_is_reported_as_autoincrement`：建表后立刻的读改经咽喉；原有断言（identity 列报为自增）保持不变
- [x] `A_column_default_is_reported`：同上；原有断言（列默认值被报出）保持不变
- [x] `An_unquoted_name_is_reported_in_lower_case`：同上；原有断言（未加引号名报为小写）保持不变
- [x] `A_quoted_lower_case_name_is_reported_as_the_catalogue_spells_it`：同上；原有断言（引号名按目录原样报出）保持不变
- [x] 读经咽喉时按目标 schema 传入对应连接串（这 4 处均针对 spare schema）
- [x] 不改变各站点的建表/清理路径与所用 schema（spare schema 的隔离语义不变）
- [x] 只改 Oracle 测试项目；不触碰 `OracleDatabaseProvider`、共享契约套件、`IContractDatabase`
- [x] 改完后**整套 Oracle 套件连续多次运行**零 `ORA-01466`；另三引擎测试项目零 diff

## Comments

2026-10-13 — 已实现并提交（`b55cc60`）。

4 个站点的读改经 `ReadThroughProviderAsync`，按目标 schema 传 `database.SpareConnectionString`；建表仍走 `SeedDdlAsync`（写连接）、spare schema 的隔离语义不变；各自原有断言逐字保留（identity 列报为自增 / 列默认值被报出 / 未加引号名报为小写 / 引号小写名按目录原样报出）。

**关于这 4 处是否真在风险面**：01 号票的实测表明 `DescribeTableAsync` 读的是 `ALL_*` 字典视图，零间隔下 **0/20** 命中 1466，故这 4 处按测量并不在竞态面上。接上咽喉是票据的明确要求，属防御性收敛（将来 Oracle 或驱动若改变字典读的行为，覆盖已在），且不改变任何断言。

**验证**：整套 Oracle 套件连续 3 次运行，每次 `73/73` 通过、`0` 失败、`0` 跳过，输出中无 `ORA-01466`。`git diff --name-status main...HEAD` 逐字确认 `src/`、`tests/SharpDb.Provider.Contract/` 与另三引擎测试项目**零 diff**；本次改动只落在 `tests/SharpDb.Provider.Oracle.Tests/` 与 `.scratch/`。
