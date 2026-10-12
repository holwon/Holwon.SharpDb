# Guard 是 Core 的 DI 服务，Provider 贡献只读策略数据

Guard 从 Core 里的 `static` 类变成 Core 的一个**可注入服务**，由被选中 Provider 贡献的 `ReadOnlyPolicy` **数据**构造。策略数据只有两项：**一个方言枚举值**与**一组禁用函数名**。Guard 的**算法**——许可清单（`Select` / 各 `Show*` / `Explain` 递归）与走查（`WITH … INSERT`、`SELECT INTO`、锁定查询、被禁函数）——仍然只有一份，留在 Core。

反直觉之处在于：既然方言与禁用函数因引擎而异，直觉上就该"每 Provider 一个 Guard"。**被否决的正是这一点**，而否决的理由不是"重复"而是**保证的层级**：

> Guard 目前唯一的安全性质是**许可清单**——拒绝永远安全，错误只会表现为误拒、绝不表现为写操作漏过（ADR 0005）。允许逐 Provider `override` 放宽策略，会把这条**集中保证**降级成**逐实现保证**：新增一个数据库类型就多一处可能写错允许清单的地方，而错的方向（漏放）恰恰是不可恢复的那种。

分界线因此是：**策略可以"加"，不能"改"。** `DeniedFunctions` 只能往里加名字；许可清单本身没有对应的可覆盖入口。数据无法表达"放宽"，这是刻意用类型形状把决定固化下来。

## 为什么是数据而不是行为

实测（ADR 0005 修订）：43 条语句在两个方言下**分类完全一致**。方言改变的是**可解析面**——PostgreSQL 方言能读 `FILTER (WHERE …)` 而通用方言不能；通用方言能读反引号而 PostgreSQL 方言不能——从不改变"写操作还是只读操作"的判断。所以"每 Provider 不同算法"没有收益，而"每 Provider 不同数据"有收益。把差异放在数据里，Guard 就只需要一个正确实现。

## 落点

- **策略数据**住在既有的契约项目 `SharpDb.Abstractions`（新增 `SqlDialect` 枚举、`ReadOnlyPolicy` 记录、`SharedReadOnlyPolicy.Generic`）。**不新建项目**：那是为"每 Provider 需要不同 Guard 算法"预留的形态，今天无人需要（ADR 0003 明确反对先声明后用不到的东西）。
- **契约项目不依赖 parser**：方言以枚举表达，枚举到 `GenericDialect` / `PostgreSqlDialect` 的映射写在 Core 的 Guard 里，Core 是唯一引用 `SqlParserCS` 的地方。未知枚举值抛 `ArgumentOutOfRangeException` 而不是静默回退——没有外部调用者会构造策略，所以那是本仓库自身的缺陷，应当响亮。
- **装配点**在 Host：`new ReadOnlyGuard(provider.Policy)` 注册为单例，`QueryTools` 由构造参数接收。
- **MySQL 与 SQLite 共用一份策略**（`SharedReadOnlyPolicy.Generic`：通用方言 + 原有的四个函数名）。这不是待重构的重复：两者都用通用方言，是因为两者的 SQL 都写反引号/双引号标识符；一旦其中一方有了不同的理由，它声明自己的策略、共享的那份继续服务另一方。这次改动因此**不改变任何既有行为**。

## Consequences

- 新增数据库类型 = 新增一个 Provider + 它声明自己的策略数据。Core 零改动，ADR 0004 的承诺不变。
- **Guard 的拒绝文案里出现了引擎才知道的词**（被禁函数的名字）。这是有意的：拒绝理由是给 AI 看的，点名 `pg_read_file` 才让它知道该改用什么，而"某函数不被允许"这种含糊说法对自我纠正没用（ADR 0010）。
- **策略数据是接口的一部分，因此是公开契约**：往 `DeniedFunctions` 加名字是安全的（只会多拒），改 `SqlDialect` 的既有枚举值含义则会改变行为，需按公开契约对待。
- PostgreSQL 的策略换来了两个后果：`FILTER (WHERE …)` 可以用了，代价是一批已知误拒（`TABLESAMPLE` 等，见 ADR 0005 修订）。**误拒只损失功能、不损失安全**，这是接受它的前提。
- 这一决定使"Provider 贡献只读知识"成为**机制**而不是约定：第一个真实用例是 PostgreSQL 的文件读取函数——它们在只读会话下实测仍然可用（ADR 0002 修订），因此必须由 Guard 补洞，而 Guard 只有拿到"这个引擎的哪些函数危险"才能补。
