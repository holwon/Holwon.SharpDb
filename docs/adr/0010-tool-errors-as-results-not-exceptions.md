# 业务失败以 `CallToolResult{IsError=true}` 返回，不抛异常

SharpDb 的四个工具在遇到业务性失败（Guard 拒绝非只读语句、SQL 语法错误、查询超时、表不存在等）时，**自己构造并返回** `CallToolResult{IsError = true}`，`Content` 是 `{error: string}` 的 JSON 文本。**不抛异常**。

反直觉之处在于：.NET 的习惯是 throw，且 SDK 的 catch 块看起来"已经处理了异常"。但 SDK 的 catch 块会**丢弃非 `McpException` 的 `Message`**，只返回泛化文案 `"An error occurred invoking 'x'."`——AI 看不到"为什么拒绝"，也就无法调整行为。要让 AI 看到清晰的拒绝理由（"INSERT is not allowed"、"Query timed out after 30 seconds"），必须自己构造错误结果。

**被否决的选项**：throw `McpException` 让 SDK 透出消息。否决理由是这会让业务代码与 SDK 的异常类型耦合，且 `McpProtocolException` / `OperationCanceledException` 仍会被 SDK rethrow 为 JSON-RPC error，不是 tool error result——行为不一致。

Consequences：所有工具的失败路径都走 `return new CallToolResult { IsError = true, Content = [...] }`，不 throw。测试时断言 `result.IsError == true` 而非 `Assert.Throws`。

**错误消息内容**：DB 错误（语法错、表不存在等）**原样透传** DB 消息，前缀 `"Database error: "`，仅脱敏文件路径（SQLite 的绝对/相对路径替换为 `<path>`）。Guard 拒绝和超时由 Core 生成固定文案。本地单用户工具，AI 就是用户的代理，schema 信息（表名、列名、SQL 位置）没有保密需求，但文件路径会泄漏到 stderr / 日志 / 截图，且对 AI 自我纠正毫无帮助。
