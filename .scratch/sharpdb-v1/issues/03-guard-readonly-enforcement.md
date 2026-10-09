# 03: Guard —— 只读领域策略

**What to build:** 一个纯函数式的只读检查：给它一段 SQL，它回答"能否执行"，并在拒绝时给出 AI 看得懂的明确理由。它是三层只读防御里**面向 AI 的快速反馈层**。

**Blocked by:** 01

**Status:** ready-for-agent

- [ ] 用 SQL 解析器把语句转成语法树来判定，**不用**关键字前缀猜测
- [ ] 恰好**一条**语句才通过——堆叠语句被拒绝
- [ ] 允许：SELECT（含 `WITH ... SELECT` 形式的 CTE）、EXPLAIN、SHOW、只读 PRAGMA
- [ ] 拒绝清单逐条覆盖（每条一个测试）：注释隐藏的写关键字、`WITH ... INSERT/UPDATE/DELETE`、`EXPLAIN ANALYZE` 后接 DML、`SELECT ... INTO OUTFILE`、PRAGMA 赋值形式、`REPLACE INTO`、逃逸函数（如读文件、取锁）
- [ ] `EXPLAIN` 需**递归检查内层**语句——`EXPLAIN INSERT` 必须被拒
- [ ] 拒绝文案告诉使用者只有 SELECT / EXPLAIN / SHOW 可用
- [ ] 无 I/O、无数据库依赖——纯领域逻辑
- [ ] 解析用**固定**的通用方言 + 超集允许清单，不依赖任何具体数据库的方言

---

**验收说明**：断言"这条 SQL 被接受 / 被拒绝 + 理由文案"，**不断言**语法树的具体形状。

**已知接受的缺口**：通用方言不接受方括号标识符，用方括号写的 SQLite SQL 会被误拒——罕见，接受。
