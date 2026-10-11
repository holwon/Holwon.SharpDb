# Spec: Tool payload 里的非 ASCII 文本原样到达客户端

Label: `ready-for-agent`

> 实现票在 [`./issues/`](./issues/)。
> 词汇一律以 [`GLOSSARY.md`](../../GLOSSARY.md) 为准（Tool / Core / Provider / Host / Guard / Introspection / Schema model）。
> 决定记录在 [`docs/adr/0012-tool-payloads-keep-non-ascii-readable.md`](../../docs/adr/0012-tool-payloads-keep-non-ascii-readable.md)。

## Problem Statement

用户让 AI 代理去查一个存在中文数据的库，代理把结果读回来，**中文变成了 `\u4E2D\u6587` 这种转义字面量**，而不是能直接看懂的文字。

这不只是"不好看"。payload 是直接进模型上下文的东西，转义形态同时带来三个问题：

- **人和模型都得在脑子里解码**，才能确认查到的到底是不是想要的那行数据；
- **token 被浪费**——非 ASCII 密集的一份结果集，转义形态体积接近翻倍；
- **列名和表名也一起被转义**，所以连"这个库有哪些表、这张表有哪些列"这种结构性信息都变得难读。

而这**不是某个 Provider 的问题，也不是数据库字符集的问题**：MySQL 与 SQLite 都在正常返回 Unicode 字符串，问题出在 Core 把结果写成 payload 的那一步。用户无从自行规避——同样的库、同样的查询，换个方式看就正常，唯独经过这个 Tool 就变形了。

## Solution

四个 Tool 写出的 payload 里，**非 ASCII 文本以字符本身出现，不再被转义**。

用户看到的是：

```json
{"columns":["id","name"],"rows":[[1,"中文"]]}
```

而不是：

```json
{"columns":["id","name"],"rows":[[1,"\u4E2D\u6587"]]}
```

适用范围是 payload 的**全部文本**，不是只有行值：行值、列名、表名、以及失败结果里的错误消息，全部一并还原。因为它们都出自同一处序列化——这也是为什么这个改动小得不成比例：**四个 Tool 共用一条出口，改一次四个一起好。**

payload 的 JSON **形状完全不变**（还是 `{columns, rows}` 信封），变的只是文本的书写形式。客户端拿到的仍是同一份 JSON，parse 之后仍是同一个值——只是现在它的字面形态也是人能直接读的。

## User Stories

### 读查询结果

1. 作为 AI 代理的使用者，我想让中文查询结果以中文显示，以便一眼确认拿到的是不是我要的那行数据。
2. 作为 AI 代理的使用者，我想让重音字符（`café`、`naïve`）原样显示，以便读欧洲语言的数据时不必在脑中解码。
3. 作为 AI 代理，我想让非 ASCII 行值以自身出现，以便不必先做一轮转义解码再判断数据的含义。
4. 作为 AI 代理，我想让非 ASCII payload 体积更小，以便在同样的上下文预算里容纳更多行。
5. 作为 AI 代理的使用者，我想让日文、韩文、俄文等所有非 ASCII 文本都同样受益，以便不必按语言区分行为。

### 读结构信息

6. 作为 AI 代理，我想让非 ASCII 的**列名**原样显示，以便不必解码就知道这张表有哪些字段。
7. 作为 AI 代理，我想让非 ASCII 的**表名**原样显示，以便在 `list_tables` 的结果里直接认出目标表。
8. 作为 AI 代理，我想让 `describe_table` 里的非 ASCII 主键/索引名原样显示，以便照抄进下一句 SQL 时不出错。
9. 作为 AI 代理，我想让非 ASCII 的列注释原样显示，以便理解字段的业务含义。

### 读失败信息

10. 作为 AI 代理，我想让**错误消息**里的非 ASCII 标识符原样显示，以便看出到底是哪张表、哪个列不存在。
11. 作为 AI 代理，我想让数据库自己不存在的对象名原样回显，以便直接把名字改对再试。
12. 作为 AI 代理的使用者，我想让被拒的写入语句里提到的非 ASCII 名字原样显示，以便理解拒绝针对的是哪个对象。

### 不变的行为

13. 作为 AI 代理，我想让 payload 的 JSON 形状保持不变，以便既有客户端不必改动。
14. 作为 AI 代理，我想让数值、布尔、null 的**类型**保持不变，以便仍能直接比较与计算。
15. 作为 AI 代理，我想让日期仍以 ISO 8601 显示、blob 仍以 base64 显示，以便不因这次改动而混淆值的种类。
16. 作为 AI 代理，我想让转义与否**不影响 parse 结果**，以便任何按 JSON 解析 payload 的客户端行为完全一致。
17. 作为 AI 代理，我想让两个受支持的数据库类型对同一段文本给出**逐字节相同**的 payload，以便不必关心背后是哪个引擎。

### 边界知情

18. 作为维护者，我想知道全角空格（U+3000）**仍会被转义**且无法消除，以便不把它当成未修完的缺陷。
19. 作为维护者，我想知道 emoji 与 BMP 之外的字符仍会被转义，以便对"完全无转义"不抱错误预期。
20. 作为维护者，我想让上述两条残余被写进 ADR，以便将来有人想"顺手消除"时先看到为什么做不到。
21. 作为维护者，我想让"为什么选这个带 Unsafe 名字的 encoder 是安全的"被记录下来，以便后人不会把它当成一个可以随手改掉的东西。

## Implementation Decisions

### 改动落点

- 改 **Core** 里 Tool 结果序列化的**那一处**：构造 payload 的 `JsonSerializerOptions` 增加一个 `Encoder` 设置。这是**唯一**的改动点——四个 Tool 的成功路径与失败路径共用它。
- **不新增**任何类型、接口或配置项。这不是一个可配置行为，而是服务器唯一正确的输出方式。
- **不改**任何 Provider、Guard、Host 或 Schema model 代码。

### encoder 的选择

- 采用 `JavaScriptEncoder.UnsafeRelaxedJsonEscaping`。
- **否决** `JavaScriptEncoder.Create(UnicodeRanges.All)`：两者放行非 ASCII 的效果相同，但后者**仍会转义 `<` `>` `&` `'` `+`**。这些字符恰是"人能直接看懂"要覆盖的范围（`<tag> & "q" + it's` 这种文本在真实数据里常见），留着它们等于把问题修一半。
- 名字里的 *Unsafe* 指的是**不再转义 HTML 敏感字符**，也就是放弃对 XSS 的纵深防御。此处**不构成风险**：payload 的消费者是 MCP 客户端的 JSON parser，不是 HTML 页面；转义与否只改变字面形态，`\u003C` 与 `<` 解析后是同一个字符串。该论证记入 ADR 0012。
- 不引入自定义 `TextEncoder` 子类。

### 已知残余（无法通过选型消除）

- **U+3000 全角空格**与 **BMP 之外的字符**（emoji、CJK 扩展 B）在任何 encoder 下仍被转义。
- 这是 `System.Text.Json` 的 **global block list** 行为，与所选 encoder 实例无关；`Create(UnicodeRanges.All)` 同样压不住，尝试过的放宽范围（含 `UnicodeRange.Create(0xD800, 0xDFFF)`）都不起作用。
- **损失只在字面形态**：值本身不丢，parse 之后完全正确。因此**接受**，不做进一步处理；这条必须写进 ADR，避免被当作未竟事项。

### 契约影响

- payload 的 **JSON 形状不变**，只有文本的书写形式改变；`{columns, rows}` 信封、类型映射（数值/布尔/null 保持类型，日期 ISO 8601，blob base64）全部不变。
- **存量 byte-exact 断言不受影响**：现有断言涉及的字符（含 `"AQI="` 里的 `=`、`count(*)`）在新旧 encoder 下输出相同。实测全量 259 个测试通过，无需修改任何既有断言。

### 文档

- 新增 **ADR 0012**，记录：决定、为何带 *Unsafe* 名字的 encoder 在此安全（被否决的 `Create(UnicodeRanges.All)`）、U+3000 与 BMP 外字符的已知残余、以及体积收益。
- **不**给 `GLOSSARY.md` 加条目：`payload` 是实现层词汇，术语表刻意不含实现细节。

## Testing Decisions

### 什么算好测试

只测**外部行为**：断言"一个 Tool 被调用后，客户端收到的 payload 文本是什么"。**不**断言序列化配置、**不**断言用了哪个 encoder、**不**断言内部类型。判据很直接——**把 encoder 设置去掉，测试必须失败**。

### 一个 seam，用已有的最高层

用 **`SharpDb.Provider.Contract` 的共享契约套件**（`QueryContractSuite`）。这已经是仓库存量最高、最省的一条缝：一次断言，MySQL 与 SQLite 各跑一遍，恰好证明"客户端不必知道是哪个引擎答的"——也正是本改动要保的性质。**不新增 seam**。

### 用例设计：字面量，不碰种子数据

新增一个测试，用**字面量 SQL** 当场构造要测的文本，**不**修改共享 schema 的种子数据：

```sql
SELECT '中文' AS t, '<tag> & "q" + it''s' AS h
```

（来自实现前的实测原型，此处保留是因为它精确编码了两个决定：覆盖哪些字符、以及为何不必 seed。）

理由：

- 覆盖 **encoder 选型的真正分水岭**（HTML 敏感字符）与非 ASCII 本体；
- **不触碰 `readings` 的种子行**——那会重写一条 6 行的 byte-exact 断言，并牵动 `SharedTableNames`（该数组的用途是"两个引擎的列表逐字节一致"，往里加表会与它纠缠）；
- 一行 SQL 自带全部被测数据，测试读起来就是它的意图。

### 被否决的 seam

- **Core 的 MCP 表面测试**（`McpSurfaceTests`）：本来最接近用户症状（真实 `McpClient` over pipes），但那里的数据库是 `StubDatabaseProvider`，**它不执行任何语句**（`CreateDbCommand` 直接抛），因此无法产生一个带值的行；而 `SharpDb.Core.Tests` 只引用 Core 项目，拿不到真 Provider。要让这条缝可用就得引入 Provider 依赖，**低于**契约套件那条已有的缝，得不偿失。
- **两个引擎各自的专属测试**：会在 MySQL 与 SQLite 里重复同一句话，正是共享 schema 注释反复警告的 drift 来源；同一个行为断言两遍，还没有证明"两引擎一致"。

### 先例

同套件里已有的 byte-exact payload 断言（结果集信封、截断标记、参数绑定、聚合列名）——新测试与它们同形同层：都是"调用 Tool，然后逐字节断言 payload 文本"。

### 已验证的预期行为

实测（两个引擎）：

- 加改动 → **通过**；去掉 encoder 设置 → **失败**，实际值 `"\u4E2D\u6587","\u003C..."` 对比期望值 `"中文","<tag>..."`。说明该测试确实是这道行为的守卫，而不是一条永远绿的装饰。
- 全量套件 **259 个测试通过**（含新增 2 个：两引擎各一），**零既有断言改动**。
- MySQL 契约套件真实执行（Docker 可用，未 skip）。

## Out of Scope

- **payload 的排版**——缩进（`WriteIndented`）、ASCII 表格、或任何脱离 `{columns, rows}` 信封的形式。形状是 ticket 04/07 已锁定的决定，本次只改文本书写形式。缩进会推高 token 数，与信封当初省 token 的动机相悖。
- **消除 U+3000 与 BMP 外字符的转义**——encoder 层做不到（见"已知残余"）；要做得手写 `TextEncoder` 子类，收益仅是字面美观，代价与安全面都不划算。
- **二进制列（blob）的解码**——blob 以 base64 呈现是刻意设计，与本次改动无关；不改。
- **连接层字符集**——MySQL 与 SQLite 均正常返回 Unicode 字符串，问题不在此。不动任何连接串或 charset 设置。
- **其它序列化点**——入站参数（客户端 → 服务器）的序列化不在此列：转义形态仍是合法 JSON、parse 回同值，客户端不受影响。
- **让编码行为可配置**——不加开关。服务器只有一个正确的输出方式。

## Further Notes

### 四个 Tool 的覆盖关系

因为四个 Tool 共用同一条出口，这一处改动同时修好：

| Tool | 受影响的文本 |
|---|---|
| `execute_query` | 行值、列名、失败消息 |
| `list_tables` | 非 ASCII 表名、视图名 |
| `describe_table` | 表名、列名、主键、索引名、注释 |
| `list_databases` | 非 ASCII 数据库名 |

### 实测收益

一份中文结果集：**201 字符 → 81 字符**（约 −60%）。payload 直接进模型上下文，这与工具输出选 `{columns, rows}` 信封以省 token 的动机一致。

### 为什么这个缺陷能一路溜过 259 个测试

现有种子数据**全是 ASCII**，`readings` 的 `label`/`note` 都是 `alpha`/`beta`/`hello` 这类词。转义只在非 ASCII 上显现，而测试数据里一个都没有。新增用例的第一价值不是"多测了一条"，而是**第一次把非 ASCII 纳入了契约**。

### 残余风险

新契约断言只在**引用该套件的两个 Provider 测试项目**里生效。将来若新增第三个 Provider，它会自动继承这条断言——这正是把测试放在共享套件而非引擎专属项目的主要好处。
