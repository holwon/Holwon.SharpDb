# 02: Host 配置绑定与 fail-fast 启动契约

**What to build:** 服务器启动时从环境变量读取配置，任何配置错误或连不上库，都**立即退出**并给出一行原因。用户从 MCP client 那里看到明确的“启动失败”，而不是一个起来了但每次调用都报错的僵尸服务器。

**Blocked by:** 01

**Status:** resolved

- [x] 配置只来自环境变量，绑定到单个不可变的 `SharpDbOptions`（类型 / 连接串 / 默认行限 / 硬行限 / 超时秒数），一次性读取
- [x] 用双下划线分隔的键名（`SHARPDB__TYPE` 等）；同时确认单下划线形式**不会**被绑定
- [x] 缺类型、未知类型、缺/空连接串 → 启动失败
- [x] 启动时**主动探测一次连接**（开一条随即关闭），凭据错 / 主机不可达 / 库不存在 → 启动失败
- [x] 失败输出契约：退出码 1；stderr **单行**、前缀 `sharpdb: `；**不打堆栈**
- [x] 失败消息**绝不回显连接串**（含密码）
- [x] stdout 保持纯净——任何日志都不写 stdout
- [x] 未知类型的提示里列出期望值

---

**验收说明**：这是**进程级**契约，测试要真启动进程、配不同 env，断言退出码 / stderr / stdout 纯净，而不是断言某个内部校验函数的返回值。

**不包含**：真正连接数据库成功后的行为——数据库适配器是 04 及以后的事。本票可以用一个尚未实现的 Provider 探测路径来验证失败分支。

---

## Comments

**2026-10-09 — 完成。** 八条验收项全部勾选。`dotnet build SharpDb.slnx -warnaserror` 与 `dotnet test SharpDb.slnx` 双绿（0 警告 / 0 错误；12 个用例，全部在 `SharpDb.Tests`）。

测试走的是**进程级** seam：`tests/SharpDb.Tests/Startup/` 起真子进程、配不同 env，断言退出码 / stderr / stdout。12 个用例覆盖缺类型、未知类型（含列出期望值）、缺/空连接串、单下划线不绑定、连不上库、连接串无法解析、数值项读不出、成功启动 stdout 纯净、失败不回显连接串、`appsettings.json` 即使放在 content root 也被忽略。

交付物：

- `src/SharpDb.Core/Configuration/SharpDbOptions.cs` —— 五个键的不可变 record（`Type` / `ConnectionString` / `DefaultMaxRows`=100 / `MaxRows`=1000 / `TimeoutSeconds`=30）
- `src/SharpDb.Core/Configuration/SharpDbConfiguration.cs` —— 一次读取；数值项读不出时转成 `StartupException`
- `src/SharpDb.Core/Startup/ProviderCatalog.cs` —— 类型 → Provider 的显式选择；期望值列表由实际注册的 Provider 推出，不是硬编码副本
- `src/SharpDb.Core/Startup/StartupProbe.cs` —— 开一条只读连接随即关闭
- `src/SharpDb.Core/Startup/StartupException.cs` —— 五种失败的单行文案
- `src/SharpDb.Abstractions/IDatabaseProvider.cs` —— 契约的**前两个**成员（`Type` / `CreateReadOnlyConnectionAsync`）
- `src/SharpDb.Provider.Sqlite/`、`src/SharpDb.Provider.MySql/` —— 各一个 Provider，只读机制（`Mode=ReadOnly` / `SET SESSION TRANSACTION READ ONLY`）
- `src/SharpDb/Program.cs` —— 装配与启动序列

本票内的三处选择（均已实测）：

1. **组合根用 `Host.CreateEmptyApplicationBuilder`，而不是手搓 `ConfigurationBuilder`。** spec 的启动序列与 ADR 0004/0006/0008 都写的是 `Host.CreateApplicationBuilder`；但那个默认构造器**无条件**挂上 `appsettings.json` / user-secrets，与本项目"配置只来自环境变量"的 ADR 0008 直接冲突，且 `builder.Configuration` 在默认集合上是**可变**的，无法移除已加载的来源。"无默认来源"的构造器是唯一同时满足两者的形态：实测它不读 `appsettings.json`（放一份在 content root 里也读不到），env 变量正常工作，`builder.Logging.AddConsole(LogToStandardErrorThreshold = Trace)` 也照常可用——后者是后续票要用的日志挂载点。
2. **Provider 通过 `builder.Services.AddSingleton(provider)` 注册**，探测时再从容器解析回来。这既落实 ADR 0003 的"Provider 是 Tool 唯一解析的服务"，也让 `Host` 侧只有一行注册。
3. **`IDatabaseProvider` 只声明两个成员。** ticket 04 才把契约"定型"为五个成员，需要它们的是内省与列表功能；本票只用到类型标识与建连，故只声明用得到的两个，其余随 04 补齐。

已知边界：`ProviderCatalog` 的构造函数会在两个 Provider 声称同一类型时抛 `ArgumentException`——这是 Host 装配处的**开发者**错误（编译期就能发现），不是用户配置错误，故不在 fail-fast 的 catch 范围内。

`bash tools/check-retired-identifiers.sh` 通过。为此在脚本的 allowlist 里加了 `tests/SharpDb.Tests/Startup/StartupContractTests.cs`——该测试证明单下划线拼写**不**绑定，本就必须写出那个已退役的字面量，正属于"解释这次退役"的站点。本 Comment 自身则改用描述性说法，不新增 allowlist 条目。
