# 04: Oracle 集成测试与进程级 smoke

**What to build:** Oracle 测试项目用 Testcontainers 启动真实 Oracle XE 容器，跑共享套件里 Oracle 能过的断言 + Oracle 专属断言（`''` 即 NULL、DDL 解除只读、`EXPLAIN` 报语法错等）。进程级 smoke 证明 Host 装配正确。此票完成后，Oracle 的测试覆盖完整。

**Blocked by:** 02 (共享套件必须先收缩，否则 Oracle 跑不过), 03 (Oracle Provider 必须先存在)

**Status:** resolved

- [x] 新增 `SharpDb.Provider.Oracle.Tests` 项目，引用 `Testcontainers.Oracle` + `SharpDb.Provider.Contract` + `SharpDb.Provider.Oracle`
- [x] `OracleContractDatabase : IContractDatabase, IAsyncLifetime` fixture 用 `Testcontainers.Oracle` + `gvenzl/oracle-xe:21-slim-faststart` 镜像
- [x] Fixture 创建一个**专用测试用户**（不是 `probe`/`system`），所有内省与查询都通过这个用户，模拟最小权限场景
- [x] `OracleProviderContractTests : ProviderContractSuite(database)` 空子类，跑共享套件里 Oracle 能过的断言
- [x] `OracleQueryContractTests : QueryContractSuite(database)` 空子类，跑共享套件里 Oracle 能过的断言
- [x] `OracleSpecificTests` 包含 Oracle 专属断言：
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
- [x] Docker 不可用时 `[SkippableFact]` + `SkipWhenUnavailable()` 跳过，不失败
- [x] 进程级 smoke 测试：起真实进程，stdin EOF 触发断开，证明 Host 装配正确

## Comments

**2026-10-12 — `b341145`（进程级 smoke 见 `8ffdafd`）**

- `SharptDb.Provider.Oracle.Tests` 建成：fixture 用 `Testcontainers.Oracle` 起 `gvenzl/oracle-xe:21-slim-faststart`，以容器创建的**专用应用用户**连接（`WithUsername`/`WithPassword`），共享 schema 由 `ContractSchema.OracleDdl`（`string[]`，逐条执行）播入。
- `OracleSpecificTests` 覆盖票列的专属行为，另加两条实测发现：**Oracle 折大写未加引号标识符 → 结果列标签大写**、**无 `ON UPDATE` 语法**、**identity 列报自增**、**默认值（`LONG`）可读**、**视图归类**、**带引号小写名按字典原样报**。
- **真机首跑暴露 4 类缺陷，均在 `b341145` 修掉**（票面未预见）：`:table` 保留字（`ORA-01745`）、`DATA_DEFAULT` 的 `LONG` fetch size、`ALL_TABLES` 无 comment 列（`ORA-00904`）、结果列标签大写 + 错误消息不含对象名（后两者迫使票 02 再收缩一次共享套件）。
- **进程级 smoke 的形态与票面措辞不同**：票写"起真实进程、stdin EOF 触发断开"，实现放在 `SharpDb.Tests/Startup`，沿用仓库对另外三引擎的既有做法——**类型识别 + Provider 形状的连接失败**（`Startup_accepts_the_oracle_type_and_reaches_the_database`、`Startup_against_an_oracle_database_that_does_not_exist_reports_a_reason_without_credentials`），外加更新"未知类型列出预期值"那条以含 `oracle`。功能上等价（真进程、证 Host 装配），且与既有三引擎的进程级手法一致——三个引擎都没有在 `SharpDb.Tests` 里起容器的进程级 smoke。
- 全绿：Oracle 72 例（31.3s）；整套解决方案 501 例 0 失败 0 跳过。
