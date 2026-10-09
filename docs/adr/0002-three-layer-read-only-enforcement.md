# 只读由三层共同保证，而不是单点检查

只读不是一次检查，而是三层叠加：应用层的 Guard（用 SqlParserCS 解析，要求恰好一条语句且为 SELECT / SHOW / EXPLAIN）、数据库引擎层的只读模式（MySQL 的 `SET SESSION TRANSACTION READ ONLY`、SQLite 连接串的 `Mode=ReadOnly`）、以及最小权限的数据库账号。

不只用一层的原因：关键字前缀检查存在已证实的绕过路径（注释隐藏关键字、`WITH … INSERT`、`EXPLAIN ANALYZE` 后接 DML、`SELECT … INTO`、SQLite `PRAGMA` 赋值等），单靠它不可靠；而只靠引擎层虽然最安全，却无法向 AI 给出清晰的拒绝理由。因此保留解析器作为快速反馈层，用引擎层兜底，账号权限作为最终边界。

**绕过的确切范围需要一处更正。** 上面的清单最初是从 Rust crate `sqlparser-rs` 的覆盖面推断的，但本次交付时逐条实测了 C# 移植版（SqlParserCS 0.6.5），发现 `REPLACE INTO`、`LOAD DATA`、`HANDLER`、`LOCK TABLES`、`DO` 在这份移植版里是**语法错**——解析器在策略运行之前就拒了它们。清单里真正需要**策略**处理的只有注释隐藏关键字、`WITH … INSERT`、`EXPLAIN ANALYZE` 后接 DML、`SELECT … INTO` 与 `PRAGMA` 赋值。这不削弱本决策：清单越短，恰恰越说明「关键字前缀检查」可以在其中任何一条上失手。
