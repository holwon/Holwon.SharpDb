# 01: Core 方言与占位符翻译

**What to build:** Core 能识别 Oracle 方言，并在执行前把用户 SQL 里的 `@name` 翻译成 `:name`（仅对 Oracle 方言生效）。MySQL/PostgreSQL/SQLite 完全不受影响。此票完成后，Oracle Provider 的带参数查询才能跑通。

**Blocked by:** None (can start immediately)

**Status:** ready-for-agent

- [ ] `SqlDialect` 枚举新增 `Oracle` 值
- [ ] `ReadOnlyGuard.DialectFor()` 加映射分支：`SqlDialect.Oracle => new GenericDialect()`（锁 `SqlParserCS 0.6.5`，不升预发布版）
- [ ] 新增翻译层：由 `provider.Policy.Dialect` 驱动，把用户 SQL 里的 `@name` 翻译成 `:name`（仅对 Oracle 方言生效）
- [ ] 翻译复用 `ParameterPlaceholders` 的词法扫描器（已会跳过引号与注释），所以 `t@dblink` 不会被翻成 `t:dblink`（`@dblink` 仍被识别为占位符 → 走"未绑定占位符"的拒绝路径）
- [ ] 翻译发生在 Core 执行路径上（Provider 不在执行路径上，看不到用户 SQL）
- [ ] 单元测试：翻译正确性（`@name` → `:name`）、MySQL/PG/SQLite 不受影响、`t@dblink` 不被翻译、引号内与注释内的 `@` 不被翻译、`@dblink` 仍走"未绑定占位符"拒绝路径
