# 08: 分发 —— 单包 global tool 与 README

**What to build:** 别人能按 README 装一次、配一段 JSON，就让自己的 AI 客户端用上 SharpDb。零源码、零编译。这是"能交付"的最后一步。

**Blocked by:** 07

**Status:** resolved

- [x] 打包为单个 .NET global tool 包，包标识与命令名固定（`Holwon.SharpDb.Mcp` / `sharpdb-mcp`），初始版本 `0.1.0`
- [x] 一次安装即同时获得 MySQL 与 SQLite 支持——**不拆包**
- [x] README 含：安装命令、一份可直接复制的 MySQL 配置片段、一份可直接复制的 SQLite 配置片段
- [x] 配置片段里命令名直接写 shim、参数为空，数据库类型与连接串放在 env 块
- [x] README 说明"多数据库 = 多 server 条目"（一个服务器实例绑一个库）
- [x] README 明确写出**最小权限账号才是只读的真正边界**（三层防御的第 3 层）
- [x] README 说明如何调整日志级别（用标准配置键，不引入私有开关）
- [x] 端到端验证：真的安装一次，按 README 配好，四个工具可用

---

**验收说明**：本票的验收是"照着 README 做能跑通"，不是"文件存在"。装包、贴配置、起客户端、调一次工具——全程不碰源码。

**已知接受的代价**：可执行程序引用全部 Provider，所以只用 SQLite 也会连带装 MySQL 驱动。这是单包换来的"装一次省心"。

---

## Comments

**2026-10-10 — 完成。** 八条验收项全部勾选，`dotnet build SharpDb.slnx -warnaserror` 与 `dotnet test SharpDb.slnx` 双绿（0 警告 / 0 错误；**257** 例，失败 0），`tools/check-retired-identifiers.sh` 通过。

### 一、验收是真跑的：装包 → 按 README 配 → 四工具全部可用

端到端验证落在 `tools/verify-installed-tool.ps1`，它**不读 SharpDb 的构建输出**，两个库都是现造的：

1. SQLite 由 `tools/seed-verification-database.cs`（文件式 C# app，`dotnet run`）建；
2. MySQL 由 harness 自己起 `mysql:8.0` 容器、跑 `tools/seed-verification-mysql.sql`（建表 + 建**只读账号**），验完删容器；Docker 缺席则跳过（不算失败）。

实测结果：**SQLite 20 项 + MySQL 20 项全绿**，两台引擎对同一组断言给出**语义一致**的应答——`list_tables` 都列出 `users`/`orders`/`active_users` 且视图标为 `"type":"view"`，`describe_table` 都报出主键/外键/索引，`execute_query` 都返回 `{"columns":["n"],"rows":[[2]]}`，写操作都被拒。这同时兑现了 ADR 0003/0004：**没有为 MySQL 改一行 Core**。

### 二、实测发现并修掉一个真 bug：标准日志键此前是空转的

票 02 为满足 ADR 0008（配置只来自环境变量）选了 `Host.CreateEmptyApplicationBuilder`——但它**也不会读 `Logging` 配置节**。后果：`Logging__LogLevel__Default` 能绑进配置、**不报错、也毫无作用**。逐级别实测（未设 / `Debug` / `Warning` / `Error` / `Critical` / `None`）全部输出同样的 29 行 stderr，只有 `Debug` 之外一个 `dbug:` 都没有。

这直接打脸本票第 7 条验收项（"用标准配置键，不引入私有开关"）——README 会教一个不生效的开关。修法是在 `Program.cs` 显式 `builder.Logging.AddConfiguration(builder.Configuration.GetSection("Logging"))`；修后实测：`Debug` → 53 行含 `dbug:`，`Warning` → 1 行无 `info:`。补了进程级回归测试 `LoggingContractTests`（在空 builder 上自建 host 的测试**测不出**这个 bug，所以必须走真进程）。

### 三、分发属性

`src/SharpDb/SharpDb.csproj` 设 `PackAsTool` + `PackageId=Holwon.SharpDb.Mcp` + `ToolCommandName=sharpdb-mcp` + `Version=0.1.0`，并**在 `Directory.Build.props` 把 `IsPackable` 默认设为 false**、只有 Host 覆盖回 true。这样"单包"是**由构建保证**的：`dotnet pack SharpDb.slnx` 实测只产出 `Holwon.SharpDb.Mcp.0.1.0.nupkg` 一个包（0 警告），不会顺带吐出四个库包。包内 `DotnetToolSettings.xml` 为 `<Command Name="sharpdb-mcp" EntryPoint="SharpDb.dll">`，两个 Driver 都在包内。

### 四、README 与实际行为逐字对齐

不写"应当是"的文案，而是从**装好的工具**上抓真实字符串：失败表里的 `sharpdb: SHARPDB__TYPE is required (expected "mysql" or "sqlite").`、`... is unknown ...`、`Invalid configuration: Failed to convert configuration value 'plenty' at 'SHARPDB:MaxRows' to type 'System.Int32'.` 都是逐字实测；账号 SQL 也是把 README 那段原样跑进容器验证过的。

`RollForward=Major` 是本票唯一超出票面字面的生产改动，且已 A/B 实测其必要性：请求一个**未安装**的主版本时，默认策略报 `You must install or update .NET to run this application.`，`Major` 则能解析到较新运行时（`Major`/`Minor`/未设三组对照，见提交说明）。README 的"装要 SDK、跑只需运行时（8 或更高主版本）"因此才是真的。

### 五、code-review 两轴复审后的修正

首轮两轴审阅各提出若干项，已逐条处理：README 失败字符串与 SDK/运行时前提（Spec 轴）、`RemoveAmbientConfiguration` 未剥离 `Logging__` 会让日志测试假红、`Program.cs` 误引 ADR 0008、`.props` 注释指代不明、`ServedHostTests` 与 `LoggingContractTests` 重复 helper（改为 `EmptySqliteDatabase.Environment(...)`）（Standards 轴）。MySQL 夹具不可复现这一条最实：现在 harness 自己 provision，不再依赖手工环境。

**许可证**经用户确认为 GPL-3.0（可商用、衍生作品——含封装——必须同样开源），已落 `LICENSE` 全文 + `PackageLicenseExpression=GPL-3.0-only`，并补 ADR 0011 记录该决定（审阅指出这是未记录的公开契约）。

实现提交：`7e91b3b`。

### 六、第二轮两轴复审后的修正

对 `2a8d8e0`（`7e91b3b` 的父提交，即本票实际改动的起点）再跑一轮双轴复审，逐条修掉：

1. **ADR 0011 有一句假断言（最重）。** 原文写"`LICENSE` 与 `PackageLicenseExpression` 必须一致，否则 `dotnet pack` 拒绝出包"。**实测否证**：故意让两者不符（不相关的 `LICENSE` + `PackageLicenseExpression=MIT`），`dotnet pack` 照样 exit 0、无警告出包，nuspec 写 `<license type="expression">MIT</license>`。已改为实话：两份声明互不校验，**只能靠人守**。
2. **harness 会静默降级覆盖。** spec 明确要求"容器起不来必须**响亮失败**，否则'静默不跑'会成为缺陷的藏身处"，但旧版把 `docker run` 失败当成"跳过 MySQL"并最终 exit 0。现已改为 `throw`，并实测：占用容器名后运行 → **exit 1** 且打印真实原因。唯一保留的跳过是"这台机器没装 Docker"，且会明确打印 `MySQL is NOT verified`。
3. **harness 跑不到 README 声明的前提上。** README 说装要 .NET 8 SDK，而旧 seeder 是文件式 C# app（需 SDK 10）——实测 pin 到 `8.0.425` 直接 exit 1。已改为普通 `net8.0` 工程 `tools/SeedVerificationDatabase/`。顺带发现 `dotnet run --project` 会把该工程**写进 `SharpDb.slnx`**，污染构建/测试入口；改为按路径 `dotnet build` + 直接跑 DLL，并实测运行后 `SharpDb.slnx` 无 diff。
4. **README 的失败表此前无人校验。** 现由 harness 断言：5 个用例各自把文档化的那一行与**工具真实输出**逐字比对，并同时断言 exit 1。此前只有 SQLite 的 3 行是手工核过的，MySQL 那行从未验过。
5. **README 词汇漂移。** `shim` 是行话（且未定义）、`Tool` 在 GLOSSARY 中专指 4 个 MCP 操作却被用来指 .NET global tool、"the database type's driver" 混淆了 `Driver` 与 `Provider`。已逐处改为平实说法或规范词（`Driver` 保留为已定义词）。
6. **测试重复 helper** 收口：`StartupContractTests` 的成功用例改用 `EmptySqliteDatabase.Environment()`（其余用例故意用不存在的库/无法解析的连接串，不能共用）。

修后复跑：验收 harness **50 项全绿**（SQLite 20 + MySQL 20 + README 失败表 10）、`dotnet test SharpDb.slnx` **257 例 0 失败**、`check-retired-identifiers.sh` 通过。

（此前一处已记：harness 不再改动父进程环境变量，改为只给子进程注入 —— 上一版会设置并删除 `SHARPDB__*`，泄漏到后续进程。）

修正提交：见提交历史。

