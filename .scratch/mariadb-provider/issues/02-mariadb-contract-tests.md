# 02: MariaDB 契约测试

**What to build:** `dotnet test tests/SharpDb.Provider.MariaDb.Tests` 运行 35+ 契约测试并全部通过（Docker 可用时）。验证 MariaDB 和其他 Provider 行为一致。

**Blocked by:** 01: MariaDB Provider 实现 + Host 装配

**Status:** resolved

- [x] 新建 `tests/SharpDb.Provider.MariaDb.Tests/` 项目
- [x] `MariaDbContractDatabase.cs` 实现 `IContractDatabase, IAsyncLifetime`，用 Testcontainers 启动 MariaDB Docker 容器
- [x] `MariaDbContractCollection.cs` 作为 xUnit collection fixture
- [x] `MariaDbProviderContractTests.cs` 继承 `ProviderContractSuite`（空类）
- [x] `MariaDbQueryContractTests.cs` 继承 `QueryContractSuite`（空类）
- [x] `MariaDbSharedSchemaTests.cs` 包含 MariaDB+MySQL+SQLite 共有断言
- [x] `MariaDbSpecificTests.cs` 包含 MariaDB 独有行为测试
- [x] Docker 不可用时 `Available=false`，所有测试 skip 而非 fail（实测：57 skipped / 0 failed）
- [x] `SharpDb.slnx` 加 `SharpDb.Provider.MariaDb.Tests` 项目
- [x] 更新 `tests/SharpDb.Tests/Startup/StartupContractTests.cs` 的 type 列表，添加 `"mariadb"`
- [x] 契约测试全部通过

## Comments

**2026-10-15 · resolved · commit `1bd88d5` + `d1c5d3b` + `0c5bf85`**

新建测试项目，用 Testcontainers 启动 `mariadb:11.4`（镜像钉版本），3 个数据库（`sharpdb_contract` / `sharpdb_spare` / `sharpdb_empty`），共享 schema 复用 `ContractSchema.MySqlDdl`。两个共享契约套件通过空子类接入，与 MySQL 完全对称。

**测试结果**：`dotnet test tests/SharpDb.Provider.MariaDb.Tests` → **58 passed / 0 failed**（Docker 启动后）。全套 `dotnet test SharpDb.slnx` → **649 passed / 0 failed / 0 skipped**。

**运行时发现并修复的两个缺陷**（均由实测而非信任注释确定）：

1. **只读机制**：`The_session_the_provider_opens_is_read_only_at_the_server` 首次运行**失败**（期望 1，实得 0），暴露出 spec 指定的 `SET TRANSACTION READ ONLY` 在 MariaDB 上不设会话变量、且不挡 DDL。改为 `SET SESSION TRANSACTION READ ONLY`，并新增 `The_connection_the_provider_opens_refuses_ddl` 断言 `CREATE TABLE` 被拒。
2. **外键动作映射**：审查发现 `RESTRICT => NoAction` 的注释声称"MariaDB 把两者都报成 RESTRICT"，实测证伪（显式 RESTRICT 报 `RESTRICT`、显式 NO ACTION 报 `NO ACTION`，不合并）。映射有损。真实分歧是**省略从句的默认值**（MariaDB `RESTRICT` vs 其余 `NO ACTION`），故让共享 DDL 显式声明 `ON UPDATE NO ACTION`，映射改逐名对应。新增 `Referential_actions_keep_restrict_and_no_action_apart` 两形状配对断言守护。

