# Guard 固定使用 GenericDialect，Provider 不暴露方言

只读 Guard 一律用 `SqlParser.Dialects.GenericDialect` 解析，允许清单取**超集**（`Select` / 各 `Show*` / `Explain`（递归检查内层））。Provider 接口因此**没有方言成员**，`SharpDb.Abstractions` 也不必依赖 `SqlParserCS`。

反直觉之处在于：通常"每 Provider 自带方言"看起来更正确。但 SqlParserCS 是 syntax-only——`PRAGMA` / `SHOW` / `EXPLAIN` 在解析器里没有任何方言守卫，用任一方言都解析成功，方言只影响标识符引号风格与能力开关，**从不参与只读分类**。而允许清单本就是 Core 自己的策略。既然方言不改变结论，把它塞进接口就是纯负担：它会让新增数据库类型不得不改 Core，破坏"新增一种数据库 = 只写 Provider"。

已知缺口：`GenericDialect` 接受双引号与反引号，但不接受 SQLite 的 `[方括号]` 标识符，用方括号写 SQLite SQL 会被误拒。方括号是 SQL Server 风格、在 SQLite 中罕见，接受此缺口。

**第二个缺口：`Pragma` 被排除在允许清单之外。** 这是超集允许清单**唯一**有意不为的例外，理由不是保守而是 `IsEqual` 标不出来——实测 `PRAGMA journal_mode('WAL')` 与 `PRAGMA user_version(42)` 都置 `IsEqual = false`，却真的写入（前者换掉日志模式，后者写进文件头）。要安全放行就得手工维护一份「哪些 pragma 名是只读的」清单，并随 SQLite 版本演进。收益为零：探查表结构本就该走 `describe_table` 工具，Provider 自己的内省 SQL 也用表值形式（`pragma_table_info(?)`），后者顺带能绑定参数、免掉字符串拼接。
