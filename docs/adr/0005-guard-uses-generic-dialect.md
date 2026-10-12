# Guard 固定使用 GenericDialect，Provider 不暴露方言

只读 Guard 一律用 `SqlParser.Dialects.GenericDialect` 解析，允许清单取**超集**（`Select` / 各 `Show*` / `Explain`（递归检查内层））。Provider 接口因此**没有方言成员**，`SharpDb.Abstractions` 也不必依赖 `SqlParserCS`。

反直觉之处在于：通常"每 Provider 自带方言"看起来更正确。但 SqlParserCS 是 syntax-only——`PRAGMA` / `SHOW` / `EXPLAIN` 在解析器里没有任何方言守卫，用任一方言都解析成功，方言只影响标识符引号风格与能力开关，**从不参与只读分类**。而允许清单本就是 Core 自己的策略。既然方言不改变结论，把它塞进接口就是纯负担：它会让新增数据库类型不得不改 Core，破坏"新增一种数据库 = 只写 Provider"。

已知缺口：`GenericDialect` 接受双引号与反引号，但不接受 SQLite 的 `[方括号]` 标识符，用方括号写 SQLite SQL 会被误拒。方括号是 SQL Server 风格、在 SQLite 中罕见，接受此缺口。

**第二个缺口：`Pragma` 被排除在允许清单之外。** 这是超集允许清单**唯一**有意不为的例外，理由不是保守而是 `IsEqual` 标不出来——实测 `PRAGMA journal_mode('WAL')` 与 `PRAGMA user_version(42)` 都置 `IsEqual = false`，却真的写入（前者换掉日志模式，后者写进文件头）。要安全放行就得手工维护一份「哪些 pragma 名是只读的」清单，并随 SQLite 版本演进。收益为零：探查表结构本就该走 `describe_table` 工具，Provider 自己的内省 SQL 也用表值形式（`pragma_table_info(?)`），后者顺带能绑定参数、免掉字符串拼接。

## 2026-10 修订：结论修正后仍成立，但两句话被推翻

加入 PostgreSQL 时重新逐条实测了这个决策，**结论的修正版仍然成立**，同时有两句原话必须删掉，因为它被实测推翻了。修订后 Guard 是 Core 的 DI 服务，由 Provider 贡献的策略数据构造（见 ADR 0013）；本节只记录与本 ADR 结论相关的部分。

### 仍然成立的部分

**方言不改变放行/拒绝的"分类"。** 用 43 条语句（含全部写操作、`WITH … INSERT`、`SELECT INTO`、`EXPLAIN ANALYZE`、逃逸函数、锁语句）在两个方言下逐条对照，**分类完全一致**。所以"给每个 Provider 一个不同的 Guard 算法"依然没有收益——换方言只改变**可解析面**，不改变安全性。原 ADR 反对"为方言建第二个 seam"的论证，修正后依然正确。

### 被推翻的部分

**原文说"方言不参与解析成败"。这是错的。** 方言恰恰参与解析成败，而且这是它唯一可观察的作用：

- `SELECT count(*) FILTER (WHERE …)`——PostgreSQL 的常见只读聚合写法——**只有 PostgreSQL 方言能解析**，通用方言报语法错，Guard 因而误拒一条纯只读语句。
- 反过来，PostgreSQL 方言**不接受反引号标识符**，而 MySQL SQL 全靠它。

所以方言不是"纯负担"，它是真实的差异维度；只是这个差异的正确落点是**数据**（一个枚举值），不是**算法**（每个 Provider 一个 Guard 子类）。原文的结论幸存，理由是换了一个。

**原文那两句必须删掉**：~~"Provider 接口因此没有方言成员"~~、~~"`SharpDb.Abstractions` 也不必依赖 `SqlParserCS`"~~。第二句的后半段仍然为真——契约项目**不依赖 parser**，因为方言用枚举表达、映射留在 Core——但那是策略数据成员的功劳，不是"没有方言成员"的功劳。

### 已知误拒缺口（接受，不修）

两个方言都解析失败、因而被拒的写法，除本 ADR 已接受的 SQLite `[方括号]` 与外，新增：

`TABLESAMPLE`、`IS JSON`、`OVERLAPS`、`ORDER BY … USING`、`FOR NO KEY UPDATE`，以及 `DO` / `VACUUM` / `ANALYZE` / `REINDEX` / `LOCK` / `LISTEN` / `NOTIFY` / `RESET`。

**性质：误拒（功能损失），不是漏放（安全无损）。** 这条区分是本 ADR 的核心承诺，也是评估优先级时的方向：拒绝永远安全，一个只读语句被误拒只是少了个功能。SQLite 的 `[方括号]` 缺口与本清单同类，因此一并接受而不修。

PostgreSQL Provider 的方言选择（换来 `FILTER (WHERE …)` 可用）是这条取舍的一个具体应用：它接受的代价就是这些误拒。
