# 配置项使用 `SHARPDB__` 双下划线前缀

SharpDb 的全部配置来自环境变量，统一用 `SHARPDB__` 前缀（**双**下划线），例如 `SHARPDB__TYPE`、`SHARPDB__CONNECTIONSTRING`，由 `Host.CreateApplicationBuilder` 绑定到 `SharpDbOptions`。

`__` 是 .NET 配置系统的层级分隔符，所以 `SHARPDB__TYPE` 能绑定到 `SharpDbOptions.Type`。看起来更自然的单下划线 `SHARPDB_TYPE` 是一个**不同的**环境变量——它**不会**绑定到任何选项属性，只能以 `Configuration["SHARPDB_TYPE"]` 原始读取。选单下划线就等于放弃 `IOptions<T>` 绑定、手写每个键的读取与校验，并给使用者留下"明明配了却没生效"的静默陷阱。这类错误在本项目里尤其难查：server 由 MCP 客户端以子进程方式拉起，用户看不到任何运行时反馈。

配置契约一旦写进 README 和用户的 `mcp.json` 就难以更改，所以 v1 即定死双下划线形式。早期文档（ADR 0004、`GLOSSARY.md`、ticket 05）曾以单下划线的 `SHARPDB_TYPE` 作占位示例，一律以本 ADR 为准。
