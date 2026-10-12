# 01: Guard 改造为策略驱动的 DI 服务

**What to build:**

Guard 不再是一个用 `GenericDialect` 解析、写死四个 MySQL 函数名的静态类。它变成一个由**被选中 Provider 提供的只读策略数据**构造的 Core 服务，策略数据只有两项：**方言**与**禁用函数表**。MySQL 与 SQLite 的策略就是它们今天的行为，因此这次改造**不改变任何既有行为**——只是把"哪个引擎用什么策略"从写死变成由 Provider 声明。

这是"让改动变简单"的那一步，也是本特性的唯一结构性改动：先落地它并保持全绿，PostgreSQL 的票才有可能只写 Provider。

**Blocked by:** None (can start immediately)

**Status:** resolved

- [x] Guard 是 Core 里一个可注入的服务，由策略数据构造；工具通过注入取得它，而不是静态调用。
- [x] 策略数据只含两项：一个方言枚举值、一组禁用函数名；**不引入对 SQL parser 的依赖**（方言以枚举表达，parser 仍只留在 Core）。
- [x] MySQL 与 SQLite 各自声明自己的策略：方言取"通用"，禁用函数表保持今天那四个名字。
- [x] 既有 Guard 测试改为按策略断言，且全部覆盖 **MySQL 与 SQLite 两套策略**。
- [x] 共享契约套件里的工具构造入口改为接受策略；三个既有测试程序集全部保持绿（MySQL 与 SQLite 的契约测试一条不改、一条不红）。
- [x] 反引号标识符仍被放行（锁住"不回归 MySQL"）；一个只读语句在被误拒的方言下仍被拒（锁住"策略数据真的在起作用"）。
- [x] 不改 Provider 契约的既有成员语义；不放宽许可清单——策略只能**加**禁用函数，不能删改放行规则。

## Comments

### 2026-10-12

`feat/postgresql-provider` 上的第一个提交。

**落下什么**

- `SharpDb.Abstractions` 新增三个类型：`SqlDialect`（枚举：`Generic` / `PostgreSql`）、`ReadOnlyPolicy`（记录：`Dialect` + `DeniedFunctions`）、`SharedReadOnlyPolicy.Generic`（MySQL 与 SQLite 共用的那一份，四个函数名原样保留）。
  - 落点选择照 spec：放进既有的契约项目，**不新建项目**，也**不把 `SqlParserCS` 拖进它**。
- `ReadOnlyGuard` 从 `static` 类变为 `sealed class`，构造参数是策略数据。方言到 parser 方言的映射（`GenericDialect` / `PostgreSqlDialect`）留在 Core；未知枚举值抛 `ArgumentOutOfRangeException` 而不是静默回退到某个方言——没有外部调用者会构造策略，所以这是本仓库自身的缺陷，应当响亮。
- `IDatabaseProvider` 新增 `Policy { get; }`；MySQL 与 SQLite 都返回 `SharedReadOnlyPolicy.Generic`。
- `QueryTools` 由构造参数接收 `ReadOnlyGuard`；`Program.cs` 用 `new ReadOnlyGuard(provider.Policy)` 注册，`QueryTool.For` 与 `McpSurfaceTests` 的会话同样传策略。

**为什么两套策略是同一份数据**

MySQL 与 SQLite 今天声明的策略逐字相同，因此 "覆盖两套策略" 与 "覆盖通用策略" 是同一件事。这不是待重构的重复：两者都用 generic 方言，是因为两者的 SQL 都写反引号/双引号标识符；一旦其中一方有了不同的理由，它声明自己的策略、`SharedReadOnlyPolicy` 继续服务另一方。把这一点写进类型的 remarks，免得后人把它当成可合并的重复。

**新增的断言（锁住两件真实的事）**

- `Back_quoted_identifiers_stay_allowed_under_the_policy_mysql_declares` —— 实测 `PostgreSqlDialect` 不认反引号，所以这条锁住的正是"不能为 PG 全局换方言"这个结论。
- `The_dialect_a_policy_names_is_the_one_the_statement_is_read_with` —— `count(*) FILTER (WHERE …)` 在 PG 策略下放行、在通用策略下以 `could not be parsed` 拒绝。这是两个方言**唯一**测得的行为差异，也就是方差不被白白携带的唯一可观察证据。
- `A_function_the_policy_denies_is_refused_wherever_it_appears` —— 用一份含五个 PG 文件读取函数的临时策略，验证禁用表真的在起作用（含嵌套调用与 `WHERE` 内位置）。
- `A_function_outside_the_denied_list_is_still_judged_on_its_statement` —— `lower` / `count` / `max` 仍放行，证明禁用表是许可清单里的一张网、不是策略本身。
- `Every_write_form_is_refused_under_every_policy` —— 以 `Assert.All(Policies, …)` 对**所有在用策略**断言十种写形式全拒；后加的策略自动被覆盖。

**没做但应当记下的**

- 不许任何 Provider 放宽许可清单：策略数据里没有"允许清单"这种东西，`DeniedFunctions` 只能往里加名字。Guard 的算法留在 Core，只有一份。
- `QueryTools` 的构造签名变了（多了一个 `ReadOnlyGuard`），但那是 Core 内部类型，不在 Provider 契约上——Provider 只多了一个 `Policy` 成员。

**验证**

- `dotnet build SharpDb.slnx`：0 error。
- `dotnet test SharpDb.slnx`：**282 通过 / 0 失败 / 0 跳过**（其中 `SharpDb.Core.Tests` 153 条，比改造前多出上节列出的策略断言）。
- MySQL 与 SQLite 的契约测试与专属测试**一条未改**，全绿。
