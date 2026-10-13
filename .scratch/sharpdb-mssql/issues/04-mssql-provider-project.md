# 04: mssql Provider 项目

**What to build:** 一个完整的 SQL Server Provider，实现 `IDatabaseProvider` 全部成员。它打开一个带只读 intent 的连接，并用 `sys.*` 目录视图回答三个内省问题。名字规则：默认 schema 的对象报**裸名**，其余非系统 schema 的对象报**限定名**。此票完成后，Provider 可被实例化并贡献 Policy，但尚未接入 Host。

**Blocked by:** 01 (内省 SQL 的形状与只读账号的授权集由实测决定), 03 (Policy 要声明一个已存在的方言值)

**Status:** resolved

- [x] 新增 Provider 项目，只引用 Abstractions 与驱动 `Microsoft.Data.SqlClient`（版本锁在中央包管理文件里）
- [x] 实现 `IDatabaseProvider` 全部成员
- [x] `Type` 返回 `mssql`（这是**已发布的公开契约**）
- [x] Policy 声明 mssql 方言值 + 四个 rowset function 禁用名；策略只**加**禁用名，不放宽许可清单
- [x] `CreateReadOnlyConnectionAsync` 用连接串构建器写入只读 intent 后打开连接，交出可用连接 —— 但不假装它挡住写
- [x] `ListTablesAsync` 报默认 schema 的裸名 + 其余非系统 schema 的限定名，每条说明是表还是视图
- [x] `DescribeTableAsync` 报列（类型、nullability、默认值、`IDENTITY` 自增）、主键列序、复合外键的列配对顺序、索引唯一性与列序；名字不存在时返回"不存在"而非异常
- [x] `ListDatabasesAsync` 报可见数据库并排除 `master` / `tempdb` / `model` / `msdb`
- [x] 内省只用普通账号可达的目录视图，不需要任何高于 `SELECT` + `VIEW DEFINITION` 的角色
- [x] 单元测试：Provider 可实例化、Policy 形状正确、`Type` 匹配 `mssql`（只覆盖不需要容器的部分）

## Comments

### 2026-10-13 — 完成（`b7e125a`）

`src/SharpDb.Provider.MsSql/` 三个文件：Provider（约 640 行）、Policy、csproj（只引用 Abstractions 与 `Microsoft.Data.SqlClient` 7.1.1）。

内省读 `sys.*`，形状全部来自 ticket 01 的实测（见 `01` 的 Comment）。命名规则照 ADR 0017：`CASE WHEN s.name = SCHEMA_NAME() THEN o.name ELSE s.name + '.' + o.name END`，`describe_table` 用 `OBJECT_ID(@table)` 交给引擎自己解析，找到后回显**同一形式**的名字（客户端从 `list_tables` 拿到的名字可以原样交给 `describe_table`）。

两个实现细节值得记下来，都不是计划里的：

- **默认值文本在无 `VIEW DEFINITION` 时读回 NULL**——所以文档里把该授权记成"告知后果"而非要求。
- **视图列继承基列的 `IDENTITY` 标志**：实测视图的 `id` 列也报 `is_identity = 1`。视图里没有任何东西会生成值，所以只在对象是表时才报自增。

类型声明按实测还原：`varchar(50)` / `varchar(max)` / `nvarchar(50)`（`max_length` 是**字节**，Unicode 类型要折半，且**先折半再渲染**否则 `-1` 会变成 `0`）/ `decimal(10,2)` / `datetime2(3)`。

单测 9 条，全绿（`SharpDb.Provider.MsSql.Tests`）。
