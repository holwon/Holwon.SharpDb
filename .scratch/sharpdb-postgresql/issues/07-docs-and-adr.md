# 07: 文档与 ADR 修订收尾

**What to build:**

把"支持 PostgreSQL"这件事对外说清楚、把这次的设计决定记进 ADR：README 增补 PostgreSQL 的配置示例与类型说明；包元数据从"两种"改为"三种"；安装验证脚本覆盖 `postgresql`；并修订三篇受影响的 ADR、新增一篇记录 Guard 与策略数据这一决定。

**Blocked by:** 04, 06

**Status:** ready-for-agent

- [ ] README 增补：PostgreSQL 的 `mcp.json` 示例、`SHARPDB__TYPE` 取值含 `postgresql`、连接串为普通 Npgsql 串、声明支持 PostgreSQL 10+ 且"只在 16 上测过"。
- [ ] 包元数据（描述与标签）写明三种数据库类型；包仍是**同一个**、版本号仍是一套。
- [ ] 安装验证脚本覆盖 `postgresql`：类型被接受；`postgres` 仍是"未知类型"示例；期望类型串为三者。
- [ ] **ADR 0002** 修订：第 2 层补上 PostgreSQL 的会话只读机制，并记录"MySQL 的 `SET SESSION TRANSACTION READ ONLY` 写法在 PostgreSQL 跨事务无效"这一实测坑。
- [ ] **ADR 0003** 修订：契约新增"策略数据"成员；"Provider 是一个深接口"的结论保持不变。
- [ ] **ADR 0005** 修订：结论修正后仍成立（方言不改变放行/拒绝的**分类**），但更正"方言不参与解析成败"，记录已知**误拒**缺口（`TABLESAMPLE` / `IS JSON` / `OVERLAPS` / `ORDER BY … USING` / `FOR NO KEY UPDATE` 等），并删除被策略数据成员取代的两句。
- [ ] 新增 ADR：Guard 是 Core 的 DI 服务、Provider 贡献只读策略数据（方言 + 禁用函数）；记录"为何不做每 Provider 一个 Guard 子类"（许可清单的集中保证会降级为逐实现保证）。
- [ ] 词汇守卫脚本通过：无被退休的标识符出现在解释处之外；`GLOSSARY.md` 用词一致。
