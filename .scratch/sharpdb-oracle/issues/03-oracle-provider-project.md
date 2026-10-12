# 03: Oracle Provider 项目

**What to build:** 一个完整的 Oracle Provider 实现 `IDatabaseProvider` 全部成员，驱动用 `Oracle.ManagedDataAccess.Core`。Layer 2 只读采用事务级机制（`BeginTransaction` → `SET TRANSACTION READ ONLY` → `BindByName = true`）。此票完成后，Provider 可被实例化并贡献 Policy，但尚未接入 Host。

**Blocked by:** 01 (Core 方言 + 占位符翻译必须先就位，否则 Oracle 上带参数查询跑不了)

**Status:** ready-for-agent

- [ ] 新增 `SharpDb.Provider.Oracle` 项目，引用 `Oracle.ManagedDataAccess.Core`（版本锁定在 `Directory.Packages.props`，当前最新稳定 23.26.301）与 `SharpDb.Abstractions`
- [ ] `OracleDatabaseProvider` 实现 `IDatabaseProvider` 全部 6 个成员
- [ ] `Type` 返回 `"oracle"`（大小写不敏感匹配，无别名）
- [ ] `OracleReadOnlyPolicy` 贡献 `SqlDialect.Oracle` + 空 `DeniedFunctions` 初版
- [ ] `CreateReadOnlyConnectionAsync` 固定时序：`new OracleConnection` → `OpenAsync` → `BeginTransaction` → `SET TRANSACTION READ ONLY` → `BindByName = true` → 返回连接（事务永不提交，随 `DisposeAsync` 回滚）
- [ ] `ListTablesAsync` 用 `ALL_TABLES UNION ALL ALL_VIEWS`（视图不在 `ALL_TABLES`，实测 0 行）
- [ ] `DescribeTableAsync` 用 `ALL_TAB_COLUMNS`（已排除隐藏列）；`DATA_DEFAULT` 是 `LONG` 列，**必须直接读**，不能放进 `NVL()`/任何函数（`ORA-00932`）；主键用 `ALL_CONSTRAINTS`（`CONSTRAINT_TYPE = 'P'`）+ `ALL_CONS_COLUMNS`（按 `POSITION` 排序）；外键用 `ALL_CONSTRAINTS`（`CONSTRAINT_TYPE = 'R'`）的 `R_OWNER`/`R_CONSTRAINT_NAME` 反查父键列；`DELETE_RULE` 映射到 `ForeignKeyAction`；Oracle 无 `ON UPDATE`，`OnUpdate` 一律报 `NoAction`；索引用 `ALL_INDEXES` + `ALL_IND_COLUMNS`（按 `COLUMN_POSITION` 排序），排除主键索引；自增识别用 `ALL_TAB_COLUMNS.IDENTITY_COLUMN = 'YES'`（12c+）
- [ ] `ListDatabasesAsync` 返回当前用户可见的 distinct owner 列表（`SELECT DISTINCT owner FROM all_tables UNION SELECT DISTINCT owner FROM all_views ORDER BY 1`），并包含连接自身的 schema
- [ ] 共享 Oracle DDL（`ContractSchema.OracleDdl`）给所有标识符加双引号写小写（`"users"`），确保字典回的就是小写
- [ ] FK 只声明 `ON DELETE`（无 `ON UPDATE`）；空串用 `NULL` 替代（Oracle 存 `''` 也是 NULL）；`readings.payload` 用 `RAW(10)` + `HEXTORAW('0102')`
- [ ] 单元测试：Provider 可实例化、Policy 形状正确、Type 匹配 `"oracle"`
