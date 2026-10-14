# 02: MariaDB 契约测试

**What to build:** `dotnet test tests/SharpDb.Provider.MariaDb.Tests` 运行 35+ 契约测试并全部通过（Docker 可用时）。验证 MariaDB 和其他 Provider 行为一致。

**Blocked by:** 01: MariaDB Provider 实现 + Host 装配

**Status:** ready-for-agent

- [ ] 新建 `tests/SharpDb.Provider.MariaDb.Tests/` 项目
- [ ] `MariaDbContractDatabase.cs` 实现 `IContractDatabase, IAsyncLifetime`，用 Testcontainers 启动 MariaDB Docker 容器
- [ ] `MariaDbContractCollection.cs` 作为 xUnit collection fixture
- [ ] `MariaDbProviderContractTests.cs` 继承 `ProviderContractSuite`（空类）
- [ ] `MariaDbQueryContractTests.cs` 继承 `QueryContractSuite`（空类）
- [ ] `MariaDbSharedSchemaTests.cs` 包含 MariaDB+MySQL+SQLite 共有断言
- [ ] `MariaDbSpecificTests.cs` 包含 MariaDB 独有行为测试
- [ ] Docker 不可用时 `Available=false`，所有测试 skip 而非 fail
- [ ] `SharpDb.slnx` 加 `SharpDb.Provider.MariaDb.Tests` 项目
- [ ] 更新 `tests/SharpDb.Tests/Startup/StartupContractTests.cs` 的 type 列表，添加 `"mariadb"`
- [ ] 契约测试全部通过
