# 04: mssql Provider 项目

**What to build:** 一个完整的 SQL Server Provider，实现 `IDatabaseProvider` 全部成员。它打开一个带只读 intent 的连接，并用 `sys.*` 目录视图回答三个内省问题。名字规则：默认 schema 的对象报**裸名**，其余非系统 schema 的对象报**限定名**。此票完成后，Provider 可被实例化并贡献 Policy，但尚未接入 Host。

**Blocked by:** 01 (内省 SQL 的形状与只读账号的授权集由实测决定), 03 (Policy 要声明一个已存在的方言值)

**Status:** ready-for-agent

- [ ] 新增 Provider 项目，只引用 Abstractions 与驱动 `Microsoft.Data.SqlClient`（版本锁在中央包管理文件里）
- [ ] 实现 `IDatabaseProvider` 全部成员
- [ ] `Type` 返回 `mssql`（这是**已发布的公开契约**）
- [ ] Policy 声明 mssql 方言值 + 四个 rowset function 禁用名；策略只**加**禁用名，不放宽许可清单
- [ ] `CreateReadOnlyConnectionAsync` 用连接串构建器写入只读 intent 后打开连接，交出可用连接 —— 但不假装它挡住写
- [ ] `ListTablesAsync` 报默认 schema 的裸名 + 其余非系统 schema 的限定名，每条说明是表还是视图
- [ ] `DescribeTableAsync` 报列（类型、nullability、默认值、`IDENTITY` 自增）、主键列序、复合外键的列配对顺序、索引唯一性与列序；名字不存在时返回"不存在"而非异常
- [ ] `ListDatabasesAsync` 报可见数据库并排除 `master` / `tempdb` / `model` / `msdb`
- [ ] 内省只用普通账号可达的目录视图，不需要任何高于 `SELECT` + `VIEW DEFINITION` 的角色
- [ ] 单元测试：Provider 可实例化、Policy 形状正确、`Type` 匹配 `mssql`（只覆盖不需要容器的部分）
