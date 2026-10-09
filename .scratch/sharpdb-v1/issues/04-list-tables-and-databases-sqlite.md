# 04: 列出表与库（SQLite）——首个端到端垂直片

**What to build:** AI 第一次能通过 MCP 问数据库"你有哪些表"。这是 **walking skeleton**：它同时确立 Provider 契约的形状、Schema 聚合的边界，并首次打通 MCP → Tool → Provider → 数据库的整条链路。

**Blocked by:** 02

**Status:** ready-for-agent

- [ ] `IDatabaseProvider` 契约定型（五个成员），并在 Core 侧提供"由类型标识装配对应 Provider"的**显式**分支——零反射
- [ ] **Schema 聚合一次定型**：聚合根及其子对象（列 / 外键 / 索引）的结构、字段与不变量全部就位，即使本票只用到其中一部分
- [ ] SQLite Provider 实现契约中的四个成员：类型标识、建只读连接、列表、列数据库
- [ ] `CreateReadOnlyConnectionAsync` 返回**已打开且已进入引擎级只读**的连接——只读体现在它增补出的连接串上
- [ ] `list_tables` 工具：列出表**与视图**，每项含名称、类型（表 / 视图）、可选注释；按名称排序
- [ ] `list_databases` 工具：返回连接可见的库
- [ ] 两个工具的 description 与参数 description 均就位；annotations 声明为只读、非破坏、幂等、封闭世界
- [ ] 两个工具都从环境变量读到的连接串工作，且**不经过 Guard**（它们不是用户 SQL）
- [ ] MCP 端到端：进程内客户端能列出工具并成功调用这两个

---

**验收说明**：Provider 测试断言"这个库里有哪些表"，**不断言**生成的内省 SQL 字符串。测试用命名共享缓存内存库 + 一条保活连接，且**传连接字符串而非连接实例**（与"每次调用现开现关"吻合）；fixture 先用可写连接建表，再让 Provider 以只读打开。
