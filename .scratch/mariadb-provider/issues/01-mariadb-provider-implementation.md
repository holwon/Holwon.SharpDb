# 01: MariaDB Provider 实现 + Host 装配

**What to build:** 用 `SHARPDB__TYPE=mariadb` 启动 SharpDb，连接 MariaDB 数据库，执行 4 个 Tool（list_tables、describe_table、execute_query、list_databases）。这是完整的端到端功能，可以 demo。

**Blocked by:** None (can start immediately)

**Status:** ready-for-agent

- [ ] 新建 `src/SharpDb.Provider.MariaDb/` 项目，包含 `.csproj` 和 `MariaDbDatabaseProvider.cs`
- [ ] `MariaDbDatabaseProvider` 实现 `IDatabaseProvider` 接口的所有方法
- [ ] Type 标识符为 `"mariadb"`
- [ ] 只读机制使用 `SET TRANSACTION READ ONLY`（去掉 MySQL 的 `SESSION` 关键字）
- [ ] SqlDialect 复用 `SqlDialect.Generic`
- [ ] Policy 复用 `SharedReadOnlyPolicy.Generic`
- [ ] Driver 复用 `MySqlConnector`（不引入新包）
- [ ] 7 条 INFORMATION_SCHEMA 查询从 MySQL Provider 复制（MariaDB 10.0+ 兼容）
- [ ] `SharpDb/Program.cs` 注册 `new MariaDbDatabaseProvider()`
- [ ] `SharpDb.csproj` 加 `ProjectReference` 到 `SharpDb.Provider.MariaDb`
- [ ] `SharpDb.slnx` 加 `SharpDb.Provider.MariaDb` 项目
- [ ] 解决方案能编译通过
