# 配置只来自环境变量，不读 appsettings.json 与 user-secrets

SharpDb 的配置**只**来自环境变量（即 MCP 客户端 `mcp.json` 的 `env` 块）。`Host.CreateApplicationBuilder` 默认会挂上 `appsettings.json` / `appsettings.{Environment}.json` / user-secrets，这三样被**刻意**排除。

反直觉之处在于：用 .NET 泛型主机，这些来源是白送的，绝大多数 .NET 程序都会用。这里主动放弃，是因为对 SharpDb 而言它们是**错误形状的配置**。MCP server 由客户端以子进程拉起，工作目录不确定——`appsettings.json` 会随"从哪个目录启动"而变，用户无从预期；user-secrets 绑定到开发机的用户 profile，与"在 `mcp.json` 里声明配置"这唯一的心智模型相冲突。多一个来源，就多一处可能覆盖配置的地方，也就多一类"我明明改了却没生效"的问题。环境变量是 MCP 客户端**唯一**真正能传参的通道；把它定为唯一来源，配置面就只有一个答案。

Consequences：将来若要支持配置文件，那是一次有意的契约变更，需要重新设计优先级。本 ADR 存在的意义，就是阻止后来者"顺手把 appsettings 加回来"。
