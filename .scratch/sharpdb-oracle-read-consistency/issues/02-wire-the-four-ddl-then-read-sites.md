# 02: 把 4 个建表后即读的站点接上咽喉

**What to build:**

Oracle 专属测试里 4 个形状逐字相同的站点——「经写连接建一张新表 → 立刻经 Provider 读回该表」——改走 01 号票立起的读咽喉。它们的共同形状是：`SeedDdlAsync(CREATE TABLE ...)` → 打开一条只读连接 → `DescribeTableAsync(...)`。

改造后：**建表仍走写连接**（DDL 需要可写会话，不变），而**读改为经咽喉**执行——这条读因此自动获得"遇 1466 重开连接重做"的覆盖，不再可能被引擎的 SCN 时序随机判红。

4 个站点各自**保持原有断言不变**（identity 列被报为自增；列默认值被报出；未加引号的标识符被报成小写；加了引号的小写名按目录原样报出），覆盖**不丢失**。

**Blocked by:** 01（立咽喉）

**Status:** ready-for-agent

- [ ] `An_identity_column_is_reported_as_autoincrement`：建表后立刻的读改经咽喉；原有断言（identity 列报为自增）保持不变
- [ ] `A_column_default_is_reported`：同上；原有断言（列默认值被报出）保持不变
- [ ] `An_unquoted_name_is_reported_in_lower_case`：同上；原有断言（未加引号名报为小写）保持不变
- [ ] `A_quoted_lower_case_name_is_reported_as_the_catalogue_spells_it`：同上；原有断言（引号名按目录原样报出）保持不变
- [ ] 读经咽喉时按目标 schema 传入对应连接串（这 4 处均针对 spare schema）
- [ ] 不改变各站点的建表/清理路径与所用 schema（spare schema 的隔离语义不变）
- [ ] 只改 Oracle 测试项目；不触碰 `OracleDatabaseProvider`、共享契约套件、`IContractDatabase`
- [ ] 改完后**整套 Oracle 套件连续多次运行**零 `ORA-01466`；另三引擎测试项目零 diff
