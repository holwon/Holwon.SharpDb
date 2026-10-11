# 01: Tool payload 的非 ASCII 文本原样到达客户端

**What to build:** 调用任意一个 Tool 时，客户端收到的 payload 里非 ASCII 文本以字符本身出现，而不是 `\uXXXX` 转义字面量。这条路径覆盖 payload 的**全部文本**——行值、列名、表名，以及失败结果里的错误消息——因为它们都出自 Core 里同一处序列化。完成后，查一个中文库看到的应当是 `{"columns":["id","name"],"rows":[[1,"中文"]]}`。

payload 的 JSON 形状不变（仍是 `{columns, rows}` 信封），只有文本的书写形式改变。

**Blocked by:** None (can start immediately)

**Status:** ready-for-agent

- [ ] Tool 写出的 payload 中，非 ASCII 文本以字符本身出现（行值、列名、表名、错误消息四处都覆盖）
- [ ] HTML 敏感字符 `<` `>` `&` `'` 与 `+` 不再被转义
- [ ] 共享契约套件新增一条断言，MySQL 与 SQLite 两处运行都通过（证明两个引擎逐字节一致）
- [ ] 把 encoder 设置去掉时该断言**失败**，证明它是真守卫而非永绿装饰
- [ ] 全量测试通过，且**没有任何既有断言被改动**
- [ ] payload 的 JSON 形状与类型映射不变（数值/布尔/null 保持类型，日期 ISO 8601，blob base64）
