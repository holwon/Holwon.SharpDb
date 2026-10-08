# 只读由三层共同保证，而不是单点检查

只读不是一次检查，而是三层叠加：应用层的 Guard（用 SqlParserCS 解析，要求恰好一条语句且为 SELECT / SHOW / EXPLAIN / 只读 PRAGMA）、数据库引擎层的只读模式（MySQL 的 `SET SESSION TRANSACTION READ ONLY`、SQLite 连接串的 `Mode=ReadOnly`）、以及最小权限的数据库账号。

不只用一层的原因：关键字前缀检查存在大量已证实的绕过路径（注释隐藏关键字、`WITH … INSERT`、`EXPLAIN ANALYZE` 后接 DML、`SELECT … INTO OUTFILE`、SQLite PRAGMA 赋值、`REPLACE INTO` 等），单靠它不可靠；而只靠引擎层虽然最安全，却无法向 AI 给出清晰的拒绝理由。因此保留解析器作为快速反馈层，用引擎层兜底，账号权限作为最终边界。
