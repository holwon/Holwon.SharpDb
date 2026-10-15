# 01: MariaDB Provider 实现 + Host 装配

**What to build:** 用 `SHARPDB__TYPE=mariadb` 启动 SharpDb，连接 MariaDB 数据库，执行 4 个 Tool（list_tables、describe_table、execute_query、list_databases）。这是完整的端到端功能，可以 demo。

**Blocked by:** None (can start immediately)

**Status:** resolved

- [x] 新建 `src/SharpDb.Provider.MariaDb/` 项目，包含 `.csproj` 和 `MariaDbDatabaseProvider.cs`
- [x] `MariaDbDatabaseProvider` 实现 `IDatabaseProvider` 接口的所有方法
- [x] Type 标识符为 `"mariadb"`
- [x] 只读机制使用 `SET SESSION TRANSACTION READ ONLY`（**偏离 spec**：实测不带 `SESSION` 的写法留 DDL 漏洞，见下方 Comment）
- [x] SqlDialect 复用 `SqlDialect.Generic`
- [x] Policy 复用 `SharedReadOnlyPolicy.Generic`
- [x] Driver 复用 `MySqlConnector`（不引入新包）
- [x] 7 条 INFORMATION_SCHEMA 查询从 MySQL Provider 复制（MariaDB 10.0+ 兼容）
- [x] `SharpDb/Program.cs` 注册 `new MariaDbDatabaseProvider()`
- [x] `SharpDb.csproj` 加 `ProjectReference` 到 `SharpDb.Provider.MariaDb`
- [x] `SharpDb.slnx` 加 `SharpDb.Provider.MariaDb` 项目
- [x] 解决方案能编译通过

## Comments

**2026-10-15 · resolved · commit `1defe61`（实现）+ `0c5bf85`（审查修复）**

Provider 已实现并接入 Host：`MariaDbDatabaseProvider`（`Type => "mariadb"`）、`SharedReadOnlyPolicy.Generic`、7 条内省 SQL 从 MySQL Provider 复制、Host 装配与解决方案条目齐备。解决方案编译通过。

**偏离 spec 之处（实测强制）**：spec 指定只读语句为 `SET TRANSACTION READ ONLY`（去掉 `SESSION`），实现改为 `SET SESSION TRANSACTION READ ONLY`。实测 MariaDB 11.4：不带 `SESSION` 的写法拒绝 `INSERT`（`ERROR 1792`）但**放行 `CREATE TABLE`**——其一次性只读标志只守护当前事务，DDL 隐式提交后逃逸（与 ADR 0014 记录在 Oracle 上的漏洞同形）。`SESSION` 形式对会话内每个事务有效，DML 与 DDL 都拒。两个引擎都接受两种写法，差异在作用域而非语法。已记入 ADR 0018。

另修：外键动作映射 `RESTRICT => NoAction` 的注释理由被实测证伪（MariaDB 明确区分两者），且映射有损；已改为逐名对应并让共享 DDL 显式声明 `ON UPDATE NO ACTION`。详见 02 的 Comment 与 ADR 0018。

