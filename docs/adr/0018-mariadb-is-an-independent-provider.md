# MariaDB is an independent Provider, not a MySQL variant

MariaDB 是一个独立的 Provider，而不是 MySQL 的变体。两者共享同一个 Driver（`MySqlConnector`），但各自维护独立的项目、独立的 Introspection SQL、独立的只读机制。

## 实测依据

MariaDB 和 MySQL 的 `INFORMATION_SCHEMA` 在 10.0+ 版本高度兼容，但只读 SQL 语法不同：

| 引擎 | 只读语句 |
|---|---|
| MySQL | `SET SESSION TRANSACTION READ ONLY` |
| MariaDB 10.1+ | `SET TRANSACTION READ ONLY`（去掉 `SESSION` 关键字） |

这个差异看似微小，但它是两个引擎的分水岭：MySQL 要求 `SESSION`，MariaDB 接受但不要求。一个 Provider 如果同时服务两者，就必须在连接后探测引擎版本并选择对应的语句，而那个探测本身就是一次额外的往返。

## 决策

新建独立的 `SharpDb.Provider.MariaDb` 项目，和 `SharpDb.Provider.MySql` 平级。两个 Provider 各自维护自己的 Introspection SQL 和只读机制，不提取共享基类。

## 原因

1. **只读 SQL 不同**：`SET TRANSACTION READ ONLY` vs `SET SESSION TRANSACTION READ ONLY`。独立项目让各自的只读机制清晰可见，不需要条件分支。

2. **`INFORMATION_SCHEMA` 可能分化**：MariaDB 已经在加 MySQL 没有的列（如 `INFORMATION_SCHEMA.COLUMNS.EXTRA` 在 MariaDB 10.0+ 有但 MySQL 5.7 没有）。独立项目让各自的 Introspection SQL 可以独立演进，不会因为一个引擎的改动影响另一个。

3. **成本不高**：一个 `.csproj` + 一个 `.cs` 文件，结构完全对称。认知负担低，维护成本可控。

4. **Driver 共享不违反 ADR 0004**：两个 Provider 都引用 `MySqlConnector`，但各自引用，不互相依赖。Driver 是实现细节，Provider 是对外接口——两个 Provider 共享一个 Driver 不等于两个 Provider 共享一个实现。

## 代价

- 多一个项目要维护，但结构完全对称，认知负担低。
- 两个 Provider 的 Introspection SQL 高度重复（7 条查询几乎相同），但重复是显式的，不是隐式的——每个 Provider 的 SQL 都在自己手里，改一个不影响另一个。
- 如果未来发现 MariaDB 需要禁用额外的函数（如 `IS_FREELOCK`），可以独立拆出 `MariaDbReadOnlyPolicy`，不需要改 MySQL Provider。

## 替代方案

### 复用 MySQL Provider，加一个 `variant` 配置

让 `mysql` Provider 接受 `SHARPDB__VARIANT=mariadb`，在连接后探测引擎版本并选择对应的只读语句。

**拒绝原因**：
- 探测引擎版本需要一次额外的往返（`SELECT VERSION()` 或 `SHOW VARIABLES LIKE 'version'`），启动时间增加。
- `variant` 配置增加了配置复杂度，用户需要知道"我的 MariaDB 其实是 mysql variant"。
- 两个引擎的 Introspection SQL 虽然高度兼容，但未来可能分化——一个 Provider 服务两个引擎会让分化变得困难。

### 提取共享基类 `MySqlFamilyDatabaseProvider`

让 `MySqlDatabaseProvider` 和 `MariaDbDatabaseProvider` 继承同一个基类，共享 7 条 Introspection SQL。

**拒绝原因**：
- 当前的重复是显式的，不是隐式的——每个 Provider 的 SQL 都在自己手里，改一个不影响另一个。提取基类会让重复变成隐式的，增加理解成本。
- 如果未来 MariaDB 的 Introspection SQL 需要改动（比如 MariaDB 11.0 改了某个列的语义），基类会让改动变得困难——要么改基类影响两个 Provider，要么在子类里覆盖基类的方法（继承层次变深）。
- 当前的重复成本不高（7 条 SQL，约 100 行代码），而提取基类的收益不大。

## 后果

- MariaDB 通过和 MySQL 完全相同的 35 个契约测试，证明行为一致。
- MariaDB 的 Introspection SQL 可以独立演进，不受 MySQL 影响。
- 两个 Provider 共享同一个 Driver（`MySqlConnector`），但各自引用，不违反 ADR 0004。
- 如果未来发现 MariaDB 需要禁用额外的函数，可以独立拆出 `MariaDbReadOnlyPolicy`，不需要改 MySQL Provider。
