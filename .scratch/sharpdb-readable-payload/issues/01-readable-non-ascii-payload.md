# 01: Tool payload 的非 ASCII 文本原样到达客户端

**What to build:** 调用任意一个 Tool 时，客户端收到的 payload 里非 ASCII 文本以字符本身出现，而不是 `\uXXXX` 转义字面量。这条路径覆盖 payload 的**全部文本**——行值、列名、表名，以及失败结果里的错误消息——因为它们都出自 Core 里同一处序列化。完成后，查一个中文库看到的应当是 `{"columns":["id","name"],"rows":[[1,"中文"]]}`。

payload 的 JSON 形状不变（仍是 `{columns, rows}` 信封），只有文本的书写形式改变。

**Blocked by:** None (can start immediately)

**Status:** resolved

- [x] Tool 写出的 payload 中，非 ASCII 文本以字符本身出现（行值、列名、表名、错误消息四处都覆盖）
- [x] HTML 敏感字符 `<` `>` `&` `'` 与 `+` 不再被转义
- [x] 共享契约套件新增断言，MySQL 与 SQLite 两处运行都通过（证明两个引擎逐字节一致）
- [x] 把 encoder 设置去掉时这些断言**失败**，证明它们是真守卫而非永绿装饰
- [x] 全量测试通过，且**没有任何既有断言被改动**
- [x] payload 的 JSON 形状与类型映射不变（数值/布尔/null 保持类型，日期 ISO 8601，blob base64）

## Comments

**2026-10-11** — 完成。`666aec4`（实现）与 `baee5ff`（文档）。

### 做了什么

Core 里 Tool 结果共用的那一个序列化器（`ToolResults`）加了一个 `Encoder` 设置：
`JavaScriptEncoder.UnsafeRelaxedJsonEscaping`。这是唯一的实现改动——四个 Tool 的成功路径与失败路径都从这个序列化器写出，所以一处生效、四个一起好。

### 验收证据

- 全量 **261 个测试通过**（原 259 + 新增 2 条 × 两个引擎），**既有断言零改动**（diff 全是新增行）。
- 新增两条断言落在共享契约套件，因此 **MySQL 与 SQLite 各跑一遍**：
  - `Text_outside_ascii_is_written_as_itself` —— 断言 payload 的文本**与列名**（列别名用中文）。
  - `A_failure_names_what_it_could_not_find_as_itself` —— 断言失败路径。
- 两条断言都实测过**去掉 encoder 设置即失败**（默认 encoder 会写出 `\u4E2D\u6587`、`\u003C…`）。

### 过程中发现并修正的三处

1. **失败路径的断言差点是装饰**：最初用 `ToolPayload.Error(result)` 读回错误消息，而它内部 `JsonDocument.Parse` 会把 `\u4E0D` 还原成 `不` —— 于是无论 payload 是否被转义，读回来都是中文，断言恒真。改为断言**未解析的原始 payload**（`ToolPayload.Text`）。这是 tdd 点名的 *tautological* 陷阱的变体：期望值经由被测代码的路径得到，就永远无法与它分歧。
2. **列名漏测**：原用例的列别名是 ASCII，只证明了行值这一半；补了中文列别名。
3. **文档数字漂移**：ADR 写「约减半」、源码注释写「twice the bytes」、测试注释写「a third」——同一份实测 201→81 字符被写成三个互不相同的数。统一为实测值与一句定性说明。

### 已知边界（不可消除）

U+3000 全角空格与 BMP 之外的字符（emoji、CJK 扩展 B）在任何 encoder 下仍被转义，属 `System.Text.Json` 的 global block list。值本身不丢，只在字面形态上仍是转义。见 ADR 0012。

