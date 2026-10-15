# 04: Db2 Provider 项目

**What to build:** 一个完整的 Db2 Provider，实现 `IDatabaseProvider` 全部成员。它打开一个**原样**连接（Db2 没有引擎层只读可设），并用 `SYSCAT.*` 目录视图回答三个内省问题。名字规则：未加引号的标识符折为大写，Provider 把全大写名折成小写报出（含小写的名字原样返回）。此票完成后，Provider 可被实例化并贡献 Policy，但尚未接入 Host。

**Blocked by:** 01 (内省 SQL 的形状与只读账号的授权集由实测决定), 02 (Policy 要声明一个已存在的方言值)

**Status:** ready-for-agent

- [ ] 新增 Provider 项目，只引用 Abstractions 与驱动 `Net.IBM.Data.Db2`（版本锁在中央包管理文件里）
- [ ] 实现 `IDatabaseProvider` 全部成员
- [ ] `Type` 返回 `db2`（这是**已发布的公开契约**）
- [ ] Policy 声明 db2 方言值 + 禁用名集（初版为空集；若票 01 实测出"只在只读会话下可用、且 Guard 挡不住"的函数则补上）；策略只**加**禁用名，不放宽许可清单
- [ ] `CreateReadOnlyConnectionAsync` 只做 `new Db2Connection(connectionString)` + `OpenAsync`，不加任何只读修饰 —— 不假装它能挡住写
- [ ] `ListTablesAsync` 报当前 schema（`CURRENT SCHEMA`）下的表与视图，每条说明是表还是视图，名字折小写
- [ ] `DescribeTableAsync` 报列（类型、nullability、默认值、自增标志）、主键列序、复合外键的列配对顺序、索引唯一性与列序；名字不存在时返回"不存在"而非异常
- [ ] `ListDatabasesAsync` 报当前数据库名（Db2 的 database 概念与 schema 的关系按票 01 实测）
- [ ] 内省只用普通账号可达的 `SYSCAT` 视图，不需要任何高于 `SELECT` 的角色
- [ ] 单元测试：Provider 可实例化、Policy 形状正确、`Type` 匹配 `db2`（只覆盖不需要容器的部分）

## Comments
