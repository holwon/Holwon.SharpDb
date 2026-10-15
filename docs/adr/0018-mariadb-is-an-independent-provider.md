# MariaDB is an independent Provider, not a MySQL variant

MariaDB 是一个独立的 Provider，而不是 MySQL 的变体。两者共享同一个 Driver（`MySqlConnector`），但各自维护独立的项目、独立的 Introspection SQL、独立的只读机制。

## 背景

MariaDB 是 MySQL 的分支。两个引擎的 `INFORMATION_SCHEMA` 高度兼容，`MySqlConnector` 也同时支持两者，所以"复用 MySQL Provider、只改 Type 字符串"看起来是最省的写法。

## 决策

新建独立的 `SharpDb.Provider.MariaDb` 项目，和 `SharpDb.Provider.MySql` 平级。两个 Provider 各自维护自己的 Introspection SQL 和只读机制，不提取共享基类。

## 原因

1. **两个引擎的内省目录会各自分化**：MariaDB 已经在往 `INFORMATION_SCHEMA` 里加 MySQL 没有的列，而内省 SQL 的每个字段选择都是针对具体引擎测过的。独立项目让一个引擎的目录变化不会静默改变另一个引擎的断言。

2. **独立演进**：两个引擎的版本节奏与兼容策略不同。共享代码会把"改一处、影响两个引擎"变成常态，而两者的行为本就允许不同。

3. **成本不高**：一个 `.csproj` + 一个 `.cs` 文件，结构完全对称。认知负担低，维护成本可控。

4. **Driver 共享不违反 ADR 0004**：两个 Provider 都引用 `MySqlConnector`，但各自引用，不互相依赖。Driver 是实现细节，Provider 是对外接口——两个 Provider 共享一个 Driver 不等于两个 Provider 共享一个实现。

## 一次被推翻的预期（实测记录）

设计初稿把"只读语句不同"当作独立的首要理由：以为 MySQL 只接受 `SET SESSION TRANSACTION READ ONLY`、MariaDB 只接受去掉 `SESSION` 的 `SET TRANSACTION READ ONLY`。

**实测推翻了它**（MariaDB 11.4，本仓库容器）：

| 语句 | `@@session.transaction_read_only` | 拒绝 `INSERT` | 拒绝 `CREATE TABLE` |
|---|---|---|---|
| `SET TRANSACTION READ ONLY` | `0` | 是（`ERROR 1792`） | **否 —— DDL 成功，只读漏洞** |
| `SET SESSION TRANSACTION READ ONLY` | `1` | 是（`ERROR 1792`） | 是（`ERROR 1792`） |

两个引擎**都接受两种写法**。差异不在"哪个引擎认哪一句"，而在**语句的作用域**：不带 `SESSION` 的形式是一次性的、只守护当前事务，而 DDL 会隐式提交该事务并在其外执行——这正是 ADR 0014 在 Oracle 上记录的同一个漏洞形状。会话级形式对会话内每个事务都有效，因此同时挡住 DML 与 DDL。

结论：MariaDB Provider 采用 **`SET SESSION TRANSACTION READ ONLY`**，与 MySQL Provider 逐字相同。独立项目的价值因此不落在这一句上，而落在上面第 1、2 条。`MariaDbSpecificTests` 里有一条断言 `CREATE TABLE` 被拒，正是为了在两种写法被换回时立刻发现。

## 第二处实测差异：外键动作的默认值

首版 Provider 把 `RESTRICT` 映射成 `ForeignKeyAction.NoAction`，注释写"MariaDB 把 NO ACTION 和 RESTRICT 都报成 RESTRICT"。**实测再次推翻了这个理由**：MariaDB 11.4 明确区分二者——显式 `ON UPDATE RESTRICT` 报 `RESTRICT`，显式 `ON UPDATE NO ACTION` 报 `NO ACTION`。也就是说该映射**有损**：真正的 `RESTRICT` 会被静默报成 `NoAction`，正是本仓库最忌讳的"看起来对的错答案"。

真正的分歧不在这两个字符串之间，而在**省略从句时的默认值**：共享 schema 的 `bookings` 外键省略了 `ON UPDATE`，而

| 引擎 | 省略 `ON UPDATE` 时报什么 |
|---|---|
| MySQL / PostgreSQL / SQLite / SQL Server | `NO ACTION` |
| MariaDB | `RESTRICT` |

当初用有损映射，是为了让共享断言 `OnUpdate == NoAction` 在 MariaDB 上通过。

**修法**：映射改回诚实的逐名对应（`RESTRICT → Restrict`、`NO ACTION → NoAction`），并让共享 DDL 的该外键**显式声明** `ON UPDATE NO ACTION`——消除对引擎默认值的依赖，沿用本仓库用显式索引消除引擎差异的同一手法。`MariaDbSpecificTests.Referential_actions_keep_restrict_and_no_action_apart` 正反两形状配对断言，是这个不再被折回同一值的守护。

## 代价

- 多一个项目要维护，但结构完全对称，认知负担低。
- 两个 Provider 的 Introspection SQL 高度重复（7 条查询几乎相同），但重复是显式的，不是隐式的——每个 Provider 的 SQL 都在自己手里，改一个不影响另一个。
- 两个 Provider 的只读语句当前逐字相同。若将来其中一个引擎的语法或语义分叉，独立项目让各自可以直接改动，不必先做条件分支。

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

- MariaDB 通过和 MySQL 完全相同的契约测试，证明行为一致。
- MariaDB 的 Introspection SQL 可以独立演进，不受 MySQL 影响。
- 两个 Provider 共享同一个 Driver（`MySqlConnector`），但各自引用，不违反 ADR 0004。
- 如果未来发现 MariaDB 需要禁用额外的函数，可以独立拆出 `MariaDbReadOnlyPolicy`，不需要改 MySQL Provider。
