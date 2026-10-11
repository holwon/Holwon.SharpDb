# Tool 的 payload 用宽松 encoder 写出，非 ASCII 文本原样到达客户端

四个 Tool 的 payload 都由 Core 里唯一一处序列化写出（`ToolResults` 的 `Serializer`），它必须设 `Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping`。System.Text.Json 默认的 encoder 只放行 BasicLatin，会把中文写成 `\u4E2D\u6587`，客户端拿到的是转义字面量而不是文字。行值、列名、表名和错误消息全都经这一处，所以漏设一次，四个 Tool 一起坏。

选用 `UnsafeRelaxedJsonEscaping` 而非 `JavaScriptEncoder.Create(UnicodeRanges.All)`：两者都放行非 ASCII，但后者仍转义 `<` `>` `&` `'` `+`，而这些字符恰恰是"人能直接看懂"要覆盖的。名字里的 *Unsafe* 指的是不再转义 HTML 敏感字符，也就是放弃对 XSS 的纵深防御——在这里不构成风险：payload 的消费者是 MCP 客户端的 JSON parser，不是 HTML 页面。转义与否只改变字面形态，`\u003C` 和 `<` 解析后是同一个字符串。

**残余**：U+3000 全角空格与 BMP 之外的字符（emoji、CJK 扩展 B）在任何 encoder 下都仍被转义，且压不掉——这是 encoder 的 global block list，与所选实例无关，`Create(UnicodeRanges.All)` 同样压不住。损失只在字面形态，值本身不丢。

**收益**：一个转义要六个字符，而它代表的那个字符只是一个。实测一份中文结果集 201 字符 → 81 字符（约 −60%）。payload 直接进模型的上下文，这与工具输出选 `{columns, rows}` 信封以省 token 的动机一致。
