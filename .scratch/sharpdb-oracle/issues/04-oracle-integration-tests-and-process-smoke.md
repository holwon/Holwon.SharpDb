# 04: Oracle 集成测试与进程级 smoke

**What to build:** Oracle 测试项目用 Testcontainers 启动真实 Oracle XE 容器，跑共享套件里 Oracle 能过的断言 + Oracle 专属断言（`''` 即 NULL、DDL 解除只读、`EXPLAIN` 报语法错等）。进程级 smoke 证明 Host 装配正确。此票完成后，Oracle 的测试覆盖完整。

**Blocked by:** 02 (共享套件必须先收缩，否则 Oracle 跑不过), 03 (Oracle Provider 必须先存在)

**Status:** ready-for-agent

- [ ] 新增 `SharpDb.Provider.Oracle.Tests` 项目，引用 `Testcontainers.Oracle` + `SharpDb.Provider.Contract` + `SharpDb.Provider.Oracle`
- [ ] `OracleContractDatabase : IContractDatabase, IAsyncLifetime` fixture 用 `Testcontainers.Oracle` + `gvenzl/oracle-xe:21-slim-faststart` 镜像
- [ ] Fixture 创建一个**专用测试用户**（不是 `probe`/`system`），所有内省与查询都通过这个用户，模拟最小权限场景
- [ ] `OracleProviderContractTests : ProviderContractSuite(database)` 空子类，跑共享套件里 Oracle 能过的断言
- [ ] `OracleQueryContractTests : QueryContractSuite(database)` 空子类，跑共享套件里 Oracle 能过的断言
- [ ] `OracleSpecificTests` 包含 Oracle 专属断言：
  - `''` 即 NULL 的语义（`v=''` 返回 0 行、`v IS NULL` 返回 1 行）
  - `ON UPDATE` 不可声明（`ORA-00905`）
  - `EXPLAIN SELECT` 报语法错（`ORA-00905`）
  - 报错消息不含对象名（`ORA-00942`）
  - 无 FROM 的 SELECT 报语法错（`ORA-00923`）
  - Layer 2 只读拒绝 INSERT/UPDATE/DELETE（`ORA-01456`）
  - **DDL 解除只读**：只读事务内 `CREATE TABLE` 成功，之后 `INSERT`/`UPDATE` 成功 —— 断言这个缺口**存在**（不试图修它）
  - `BindByName` 默认为 false，Provider 设为 true 后参数按名绑定（按位置绑会静默错值）
  - `IDENTITY_COLUMN = 'YES'` 识别自增（12c+）
  - `ALL_TAB_COLUMNS.DATA_DEFAULT` 是 LONG 列，直接读不进函数
  - 视图在 `ALL_VIEWS` 不在 `ALL_TABLES`
  - 带引号小写标识符的读写行为（字典回的就是小写）
- [ ] Docker 不可用时 `[SkippableFact]` + `SkipWhenUnavailable()` 跳过，不失败
- [ ] 进程级 smoke 测试：起真实进程，stdin EOF 触发断开，证明 Host 装配正确
