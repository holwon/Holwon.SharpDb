# 02: PostgreSQL Provider 骨架：装配、只读连接、列对象

**What to build:**

`SHARPDB__TYPE=postgresql` 能被识别、装配并启动。启动时用普通 Npgsql 连接串连上库；连接由引擎自己置于只读模式，因此写操作被引擎拒绝（不必用户手动加参数）。两个"列对象"的工具——列有哪些表和视图、列有哪些库——对真实 PostgreSQL 库答出与 MySQL/SQLite **同形**的结果。

这一票走完整条路径（配置 → 装配 → 连接 → 内省 → Tool → 契约测试），PostgreSQL 的引擎专属测试项目与种子 schema 也在这一票就位，后续票在它上面加内容。

**Blocked by:** 01

**Status:** ready-for-agent

- [ ] 新增 PostgreSQL Provider 项目，独占 Npgsql 依赖；Core 不引用任何 Driver（沿用既有分层）。
- [ ] Host 装配三个 Provider；`SHARPDB__TYPE=postgresql` 启动成功，`postgres` 仍报"未知类型"，期望类型串变为三者。
- [ ] 只读由 Provider 自己加上；连接交出前引擎已处于只读模式。用一个写语句验证：**引擎**拒绝它（不是 Guard 拒绝）。
- [ ] 启动探测：连不上 / 密码错 / 库不存在 → fail-fast，一行 stderr，不含连接串凭据。
- [ ] `list_tables` 报出 `search_path` **可见**的表与视图、按名排序、区分表与视图；**系统 schema（`pg_catalog` / `information_schema`）的对象不出现**（实测：可见性判定会把约 64 张系统表算进来）。
- [ ] `list_databases` 报出连接可见的库名；**模板库不出现**。
- [ ] 新增 PostgreSQL 契约夹具（Testcontainers，pin `postgres:16`）与 PG 种子脚本；Docker 缺席时跳过而非失败。
- [ ] `ProviderContractSuite` 中与"列表"有关的断言在 PostgreSQL 上通过；若某条断言暴露真实引擎分歧而非 Provider 缺陷，按既有规则移入引擎专属测试并记录。
- [ ] MySQL 与 SQLite 的既有测试保持绿。
