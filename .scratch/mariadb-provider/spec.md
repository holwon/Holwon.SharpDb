# MariaDB Provider

Status: ready-for-agent

## Problem Statement

SharpDb 当前支持 MySQL、PostgreSQL、SQLite、Oracle 和 SQL Server，但不支持 MariaDB。MariaDB 是 MySQL 的广泛使用的分支，许多团队已经在用。用户需要让 SharpDb 连接 MariaDB 数据库，以便 AI agent 能安全地只读查询 MariaDB 中的表结构和数据。

## Solution

新建一个独立的 `SharpDb.Provider.MariaDb` 项目，实现 `IDatabaseProvider` 接口，和现有的 MySQL Provider 平级。MariaDB 复用 `MySqlConnector` Driver（同一个包已经支持 MariaDB 协议），复用 `SqlDialect.Generic` 方言和 `SharedReadOnlyPolicy.Generic` 只读策略。唯一的实现差异是只读 SQL 语句：MariaDB 用 `SET TRANSACTION READ ONLY`（去掉 MySQL 的 `SESSION` 关键字）。

## User Stories

1. As a MariaDB 用户，我想用 `SHARPDB__TYPE=mariadb` 启动 SharpDb，so that 我的 AI agent 能只读查询我的 MariaDB 数据库
2. As a MariaDB 用户，我想让 SharpDb 列出我的表和视图，so that agent 知道数据库里有什么
3. As a MariaDB 用户，我想让 SharpDb 描述一张表的结构（列、主键、外键、索引），so that agent 能写出正确的查询
4. As a MariaDB 用户，我想让 SharpDb 执行只读 SQL 查询，so that agent 能获取数据
5. As a MariaDB 用户，我想让 SharpDb 列出可见的数据库，so that agent 了解数据库全景
6. As a MariaDB 用户，我想让连接自动进入引擎只读模式，so that 任何写入尝试都会被数据库自身拒绝
7. As a SharpDb 维护者，我想让 MariaDB 通过和其他 Provider 相同的契约测试，so that 行为一致性有保障
8. As a SharpDb 维护者，我想让 MariaDB 的测试在 Docker 不可用时自动跳过，so that CI 不会因为环境问题失败
9. As a SharpDb 用户，我想在 README 中看到 MariaDB 的配置示例，so that 我能快速上手

## Implementation Decisions

### 新建独立 Provider 项目

`SharpDb.Provider.MariaDb` 和 `SharpDb.Provider.MySql` 平级，各自维护自己的 Introspection SQL。两个引擎的 `INFORMATION_SCHEMA` 虽然高度兼容，但独立项目让差异不会互相污染，未来各自演进不受约束。

### Type 标识符

`Type => "mariadb"`，小写，和现有命名风格一致（`"mysql"`, `"postgresql"`, `"sqlite"`）。

### 只读机制

MariaDB 10.1+ 支持 `SET TRANSACTION READ ONLY`（去掉 MySQL 的 `SET SESSION TRANSACTION READ ONLY` 中的 `SESSION` 关键字）。连接后立即执行此语句，再交出连接。目标最低版本 MariaDB 10.1。

### SqlDialect

复用 `SqlDialect.Generic`。MariaDB 的 SQL 语法和 MySQL 高度一致——反引号标识符、相同的函数名、相同的占位符 `@param`。

### Policy

先复用 `SharedReadOnlyPolicy.Generic`（禁用 `LOAD_FILE`、`GET_LOCK`、`SLEEP`、`BENCHMARK`）。MariaDB 也有这些函数。后续如果发现需要加 MariaDB 特有的禁用函数（如 `IS_FREE_LOCK`、`RELEASE_ALL_LOCKS`），再拆出独立的 Policy。

### Driver

复用 `MySqlConnector` 2.6.2（已在 `Directory.Packages.props` 中定义）。不引入新包。`MySqlConnector` 同时支持 MySQL 和 MariaDB 协议。

### 类名

`MariaDbDatabaseProvider`（注意大小写：`MariaDb` 不是 `MariaDB`，和 .NET 命名惯例一致）。

### 项目文件结构

2 个文件，和 MySQL Provider 对称：
- `SharpDb.Provider.MariaDb.csproj`
- `MariaDbDatabaseProvider.cs`

### INFORMATION_SCHEMA 查询

MariaDB 10.0+ 的 `INFORMATION_SCHEMA.COLUMNS` 有 `COLUMN_TYPE` 列，行为和 MySQL 一致。7 条 Introspection SQL 可以直接从 MySQL Provider 复制，不需要改写。

### Host 装配

`SharpDb/Program.cs` 加一行 `new MariaDbDatabaseProvider()`，传给 `ProviderCatalog`。`SharpDb.csproj` 加 `ProjectReference`。

### 解决方案文件

`SharpDb.slnx` 加 `SharpDb.Provider.MariaDb` 和 `SharpDb.Provider.MariaDb.Tests`。

### ADR

写 ADR 0018，记录"为什么 MariaDB 是独立 Provider 而不是复用 MySQL Provider"。三个条件都满足：难以完全撤回（加 Provider 有成本）、没上下文会惊讶（未来读者会问为什么分开）、真实权衡（独立 vs 复用）。

### README

加 MariaDB 支持声明和配置示例（`SHARPDB__TYPE=mariadb`）。

## Testing Decisions

### 测试接缝

**一个接缝，最高层：`SharpDb.Provider.Contract` 共享契约测试套件。** 这是跨 Provider 的统一验证层，约 35 个测试覆盖 `IDatabaseProvider` 的所有方法和 `QueryTools` 的行为。MariaDB 通过实现 `IContractDatabase` 接口插入这个套件，和 MySQL、PostgreSQL 等完全对称。

### 测试项目

新建 `tests/SharpDb.Provider.MariaDb.Tests/`，包含：

| 文件 | 说明 |
|---|---|
| `MariaDbContractDatabase.cs` | 实现 `IContractDatabase, IAsyncLifetime`，用 Testcontainers 启动 MariaDB Docker 容器 |
| `MariaDbContractCollection.cs` | xUnit collection fixture，所有测试共享一个容器 |
| `MariaDbProviderContractTests.cs` | **空类**，继承 `ProviderContractSuite`（约 20 个 Introspection 契约测试） |
| `MariaDbQueryContractTests.cs` | **空类**，继承 `QueryContractSuite`（约 15 个查询契约测试） |
| `MariaDbSharedSchemaTests.cs` | MariaDB+MySQL+SQLite 共有但 Oracle 不满足的断言 |
| `MariaDbSpecificTests.cs` | MariaDB 独有行为（只读会话变量、写入拒绝等） |

### 测试基础设施

- 用 Testcontainers 启动 MariaDB Docker 容器（`mariadb:` 镜像）
- 3 个数据库：`sharpdb_contract`（共享 Schema model）、`sharpdb_spare`（特有测试）、`sharpdb_empty`（空数据库）
- Docker 不可用时 `Available=false`，所有测试 skip 而非 fail
- `ContractSchema.MariaDbDdl`：如果和 MySQL DDL 相同，直接复用 `MySqlDdl`

### 需要更新的现有测试

`tests/SharpDb.Tests/Startup/StartupContractTests.cs` 中的 `Startup_with_an_unknown_database_type_fails_fast_and_lists_the_expected_ones` 测试需要更新，添加 `"mariadb"` 到断言的已注册 type 列表。

### Seed 验证脚本

新建 `tools/seed-verification-mariadb.sql`，和 MySQL 版本相似（INFORMATION_SCHEMA 兼容）。

### 测试原则

- 只测外部行为，不测实现细节
- 契约测试优先——MariaDB 必须通过和其他 Provider 完全相同的 35 个契约测试
- 特有测试只测 MariaDB 独有行为（如 `SET TRANSACTION READ ONLY` 的会话变量名）

## Out of Scope

- **MariaDB 特有的只读策略**：当前复用 `SharedReadOnlyPolicy.Generic`。如果未来发现 MariaDB 需要禁用额外的函数（如 `IS_FREE_LOCK`），那是另一个 feature。
- **MariaDB 版本低于 10.1**：不支持。`SET TRANSACTION READ ONLY` 从 10.1 开始。
- **MariaDB 和 MySQL 的代码复用**：当前两个 Provider 各自独立，不提取共享基类。如果未来重复代码增多，可以重构提取，但不在本次范围内。
- **MariaDB 的 `INFORMATION_SCHEMA` 差异处理**：研究表明 MariaDB 10.0+ 和 MySQL 的 `INFORMATION_SCHEMA` 高度兼容，不需要特殊处理。如果发现个别差异，在实现时解决。

## Further Notes

### ADR 0018 要点

- **标题**：MariaDB is an independent Provider, not a MySQL variant
- **决策**：新建独立项目，不复用 MySQL Provider
- **原因**：
  1. 两个引擎的只读 SQL 语法不同（`SET TRANSACTION READ ONLY` vs `SET SESSION TRANSACTION READ ONLY`）
  2. 未来 `INFORMATION_SCHEMA` 可能分化（MariaDB 已经在加 MySQL 没有的列）
  3. 独立项目让各自演进不受约束
  4. 成本不高（一个 `.csproj` + 一个 `.cs` 文件）
- **后果**：
  - 多一个项目要维护，但结构完全对称，认知负担低
  - 两个 Provider 共享同一个 Driver（`MySqlConnector`），但各自引用，不违反 ADR 0004
