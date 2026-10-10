# 06: 执行查询（SQLite）

**What to build:** AI 能执行一条只读 SQL 并拿到结果集。**此刻四个工具全部就位**，服务器功能完整。Guard 在这一票接入——用户查询是唯一需要过只读检查的入口。

**Blocked by:** 03, 05

**Status:** ready-for-agent

- [x] `execute_query` 工具：必填 SQL + 可选参数对象 + 可选行数上限
- [x] 参数用 `@name` 占位符；参数对象的键**不带** `@` 前缀
- [x] 返回 `{columns, rows}` 信封（列名只出现一次）；行是数组的数组
- [x] 类型表示：整数 / 小数 / 布尔 / 空值用**原生 JSON**，日期时间用 ISO 8601，二进制用 base64
- [x] 到达行数上限时**截断并标记**（仅在截断时出现该标记），不报错
- [x] 请求的行数被服务端**硬上限**静默裁剪，不报错
- [x] 未显式传行数时用配置的默认值
- [x] 超时用配置的秒数；超时时返回错误结果，消息含秒数与可操作建议
- [x] **Guard 前置**：非只读语句在执行前被拒，拒绝文案清晰
- [x] 数据库自身的错误**原样透传**（带前缀），AI 能看到表名 / 列名 / SQL 位置
- [x] 错误消息里**隐去文件路径**
- [x] 所有业务失败以 `IsError` 结果返回，**不抛异常**（协议层异常除外）
- [x] MCP 端到端：**工具列表恰为 4 个**，四个工具的 annotations 全部正确，且一条拒绝路径确实返回 `IsError`

---

**验收说明**：只测外部行为——断言"这条查询返回什么"、`IsError` 与消息内容，不断言内省 SQL、也不断言抛了哪种异常（本来就不抛）。

**为何依赖 05**：本票含"四工具全注册 + annotations 正确"的全栈断言，四个工具齐了才断言得了。

---

## Comments

**2026-10-10 — 完成。** 十三条验收项逐条落地。`dotnet build SharpDb.slnx -warnaserror` 与 `dotnet test SharpDb.slnx` 双绿（0 警告 / 0 错误；**200** 例，失败 0——本票新增 **51** 例：`SharpDb.Core.Tests` +27、`SharpDb.Provider.Sqlite.Tests` +19、`SharpDb.Tests` +5）。**此刻四个工具全部就位**，Host 也第一次真的跑起 stdio 循环。

交付物：

- `src/SharpDb.Core/Execution/` —— `QueryValues`（值 → JSON 映射）、`QueryErrors`（超时文案 + DB 消息成形与脱敏）、`QueryParameters`（参数对象 → 绑定值）、`QueryExecutor`（执行 + 行限 + 截断 + 超时）、`QueryResult`、`QueryTimedOutException`
- `src/SharpDb.Core/Mcp/QueryTools.cs` —— `execute_query` 工具；`ToolConnectionString.cs` —— 两个工具类共用的连接串守卫（原内联在 `SchemaTools` 里，本票出现第二个工具类后提取）
- `src/SharpDb/Program.cs` —— `AddMcpServer().WithTools<SchemaTools>().WithTools<QueryTools>().WithStdioServerTransport()` + `host.RunAsync()`
- 测试：`SharpDb.Core.Tests/Execution/`（25 例）、`SharpDb.Provider.Sqlite.Tests/QueryExecutionTests.cs`（19 例）、`SharpDb.Tests/Mcp/`（5 例）、`SharpDb.Core.Tests/Mcp/McpSurfaceTests.cs`（原 `SchemaToolTests.cs` 改名，+2 例）

### 一、超时：**SQLite Driver 完全忽略超时**，截止时间只能由 Core 自己看（实测）

票面"超时用配置的秒数"在本仓库没有现成机制可用。四条实测（Microsoft.Data.Sqlite 10.0.12 / net8.0）：

| 手段 | 结果 |
|---|---|
| `DbCommand.CommandTimeout = 1` | **无效**——3.5 秒的查询照跑完 |
| `ExecuteReaderAsync(token)` 传 1 秒 token | **读取阶段无效**——3.5 秒后正常返回 |
| `SqliteCommand.Cancel()`（后台定时器） | **无效**——6.2 秒跑完，`Cancel()` 被忽略 |
| token 在**开始执行前**已取消 | **有效**——`OperationCanceledException` |

结论：token 只在**执行前后**被检查，**执行中途**不被观察。因此超时实现为"**Core 侧截止时间** + 读完 `ThrowIfCancellationRequested()`"：SQLite 语句一旦开始就**无法中断**，只能在它返回时**丢弃结果**并把该次调用判为超时。同时仍把秒数交给 `CommandTimeout`（对**能**观察它的 Driver 有效，且不额外写代码）。代价如实记录：一条慢 SQLite 查询仍会占满 CPU 直到自身结束，本票只保证"调用方按时拿到超时错误"。

### 二、类型映射：SQLite 只会交出 5 种 CLR 类型（实测）

声明类型与 Driver 交出的 CLR 类型**不是一回事**。实测一张含 `BOOLEAN` / `DECIMAL(10,2)` / `DATETIME` / `DATE` 列的表：

| 声明类型 | `GetFieldType()` / 值类型 |
|---|---|
| `INTEGER` | `Int64` |
| `REAL` | `Double` |
| `TEXT` | `String` |
| `BLOB` | `Byte[]` |
| `BOOLEAN` | **`Int64`**（不是 bool） |
| `DECIMAL` | **`Double`**（不是 decimal） |
| `DATETIME` / `DATE` | **`String`**（存进去什么样就什么样） |

所以 `QueryValues` 的映射（int/decimal/bool/null 原生、`byte[]` → base64、`DateTime`/`DateTimeOffset` → ISO 8601 `"O"`）在 SQLite 上**只有 `long`/`double`/`string`/`byte[]`/`null` 五条分支会被走到**——bool、decimal、datetime 三条**只能靠单元测试固定**，票面要的 ISO 8601 在 SQLite 上观察不到（Driver 不给 `DateTime`）。这与票 05 的"视图列 nullability 跨 Provider 分歧"是同一类现象：**跨 Provider 的语义一致性必须逐项实测**。MySQL 的 `execute_query` 是票 07 的范围。

### 三、一个漏掉就前功尽弃的失败形态：**缺参数抛 `InvalidOperationException`**（实测）

`SELECT ... WHERE id = @id` 却不传 `id`——**AI 最可能犯的错**——Microsoft.Data.Sqlite 抛的是 **`InvalidOperationException`**（`Must add values for the following parameters:`），**不是 `DbException`**。只 catch `DbException` 会让它冒泡成 SDK 的泛化文案（ADR 0010 明列的反面教材）。已补 `catch (InvalidOperationException) when (... is not ObjectDisposedException)`——排除 `ObjectDisposedException`，因为那意味着**本服务器**过早释放，是缺陷而非"调用方的输入有问题"。另实测：多传参数（占位符没有对应项）、键带 `@`、键含空格**都不报错**，无需处理。

### 四、脱敏：SQLite 的失败消息**本来就不含路径**（实测）

ADR 0010 要求把文件路径换成 `<path>`。实测 SQLite 打开失败的消息是 `SQLite Error 14: 'unable to open database file'.`——**Driver 不写文件名**。所以脱敏**无法经 SQLite 端到端触发**，改为：`QueryErrors` 的路径识别做**单元测试**（Windows / Unix / 相对前缀三种写法 + 反例"`1/2` 不是路径"），集成测试只断言"测试目录的绝对路径没出现在消息里"。已知缺口（写在 XML docs 里）：含空格的路径只隐藏到空格；无显式前缀的裸相对路径（`data/x.sqlite`）不隐藏，因为与普通文本无法区分。

### 五、Host 第一次接上 stdio 循环，并补了一条**进程级** MCP smoke

`Program.cs` 从"探测完就退出"变成"探测 → `host.RunAsync()`"。两处连带改动：

1. **进程级测试的 stdin 必须显式关闭**。原来的 `HostProcess` 不重定向 stdin，接上循环后子进程会一直等输入。改为 `RedirectStandardInput = true` 且启动后立即 `Close()`——这正是 MCP client 断开时的形态（EOF），也让成功路径测试保持确定性（12 例仍在 3 秒内跑完）。
2. **新增 `SharpDb.Tests/Mcp/`（5 例）**。理由：进程内的 `McpSurfaceTests` **自己搭** `AddMcpServer().WithTools<...>()`，因此它**证明不了 `Program.cs` 注册了同样的工具**——少注册一个，进程内测试照样全绿。新的 `ServedHostTests` 起**真进程**、用 `McpClient` 走**进程的 stdio**，断言：工具恰为 4 个、跑一条查询拿到结果集、一条写语句返回 `IsError`，以及**参数对象与 `maxRows` 真的能穿过协议**（这两条属于"文档承诺了但可能不可达"：schema 若不对，`parameters` 根本传不进来）。

`ServedHost.DisposeAsync` 里要**显式关掉进程 stdin**：`McpClient.DisposeAsync()` 不会关它，否则每个用例都要等满 30 秒超时再被 kill（实测 3 例共 1 分 31 秒 → 修正后 1 秒）。

### 六、双轴 code review 后的修正

对 diff 跑了 Standards 与 Spec 两轴并行审阅（Spec 轴：无缺失、无越界、无实现错误）。采纳 1 处、否决 2 处：

1. **采纳**：`QueryPayload` 私有 record 与 `QueryResult` 结构重复（仅 `Truncated` 的 `bool` vs `bool?` 不同）。改为**匿名对象**内联成形——与 `SchemaTools` 既有写法一致，重复消失，线形形状逐字节不变（测试原样通过）。
2. **否决（误报）**：审阅称 `QueryTimedOutException` 的主构造参数缺 `<param>`。**实际存在**（第 12 行），已复核文件。
3. **否决（保留判断）**：`QueryTools` 是复数却只有一个工具。保留——`SchemaTools`（3 个）与本类都是"**某关注点下的工具组**"，命名取关注点而非数量；且两者的**分野是有意义的**（`SchemaTools` 的类注释写明"没有 Tool 查 Guard"，而 `execute_query` 是本仓库**唯一**查 Guard 的入口）。

### 已接受的边界

- **MySQL 的 `execute_query` 零测试**：与票 04 / 05 同一条边界，票 07 必须补上（届时 `ExecuteQueryAsync` 一行不改，只由 MySQL Provider 提供连接）。
- **超时无法中断 SQLite 语句**：见上，只保证按时返回超时错误。
- **ISO 8601 在 SQLite 上不可观察**：见上，靠单元测试固定。
- **裸相对路径不脱敏**：见上，已写在 XML docs 里。

`bash tools/check-retired-identifiers.sh`（经 Git 的 bash）通过。本 Comment 未新增退役标识符。
