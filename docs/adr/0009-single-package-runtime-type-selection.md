# 分发为单个 global tool 包，数据库类型由配置在运行时选择

SharpDb 只发布**一个** NuGet 包（`Holwon.SharpDb.Mcp`，`PackAsTool`），其中同时包含全部 Provider；用户装一次，用哪个数据库由 `SHARPDB__TYPE` 在启动时决定。被否决的方案是"每个数据库一个包"（`Holwon.SharpDb.Mcp.MySql` / `…Sqlite`，按需装一个）。

反直觉之处在于：既然每种数据库是一个**独立的 Provider 项目**，直觉上就该每种配一个包。但**打包单位与工具面无关**——agent 只看到 MCP server 和它暴露的 4 个工具，看不到 NuGet 包。工具数量只由"用户在 `mcp.json` 里配了几个 server"决定，且每个 server 暴露同一组工具名。每库一包因此只增加成本：换库要重装、版本号要各管一套、README 要教两遍，而 `SHARPDB__TYPE` 在这种形态下甚至变得多余。注意**包 ≠ 项目**：编译期是 5 个项目，分发期是 1 个包，两者不一一对应。

**有意接受的代价**：可执行程序引用了全部 Provider，所以哪怕只用 SQLite，安装也会连带拉下 `MySqlConnector`。这是"装一次省心"换"多带一个用不到的 Driver"。

Consequences：新增一种数据库 = 加一个 Provider 项目 + 在 Host 的 `switch` 加一条分支；包仍然是同一个、版本号仍然只有一套。`PackageId` 与 `ToolCommandName` 一旦发布即为公开契约，改动等于破坏用户既有的 `mcp.json`。
