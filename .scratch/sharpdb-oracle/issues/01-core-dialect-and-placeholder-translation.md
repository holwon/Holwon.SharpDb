# 01: Core 方言与占位符翻译

**What to build:** Core 能识别 Oracle 方言，并在执行前把用户 SQL 里的 `@name` 翻译成 `:name`（仅对 Oracle 方言生效）。MySQL/PostgreSQL/SQLite 完全不受影响。此票完成后，Oracle Provider 的带参数查询才能跑通。

**Blocked by:** None (can start immediately)

**Status:** resolved

- [x] `SqlDialect` 枚举新增 `Oracle` 值
- [x] `ReadOnlyGuard.DialectFor()` 加映射分支：`SqlDialect.Oracle => new GenericDialect()`（锁 `SqlParserCS 0.6.5`，不升预发布版）
- [x] 新增翻译层：由 `provider.Policy.Dialect` 驱动，把用户 SQL 里的 `@name` 翻译成 `:name`（仅对 Oracle 方言生效）
- [x] 翻译复用 `ParameterPlaceholders` 的词法扫描器（已会跳过引号与注释），所以 `t@dblink` 不会被翻成 `t:dblink`（`@dblink` 仍被识别为占位符 → 走"未绑定占位符"的拒绝路径）
- [x] 翻译发生在 Core 执行路径上（Provider 不在执行路径上，看不到用户 SQL）
- [x] 单元测试：翻译正确性（`@name` → `:name`）、MySQL/PG/SQLite 不受影响、`t@dblink` 不被翻译、引号内与注释内的 `@` 不被翻译、`@dblink` 仍走"未绑定占位符"拒绝路径

## Comments

**2026-10-12 — `e565e3a`（后续 `cd163c6` 补测试）**

- 新增 `SqlDialect.Oracle`，`ReadOnlyGuard.DialectFor()` 映射到 `GenericDialect`（`SqlParserCS` 锁 `0.6.5`，不升预发布版）。
- 新增 `PlaceholderTranslation.ForDialect(sql, dialect)`：仅 `SqlDialect.Oracle` 时把 `@name` 重写成 `:name`，其余方言逐字节原样返回。
- 复用词法扫描器：把 `ParameterPlaceholders` 内部的 `Names` 改为暴露位置的 `Spans`，新增公有 `WithMarker(sql, marker)`；引号内、注释内、`'@id'` 字面量里的 `@` 都不动。
- 落点在 `QueryTools.ExecuteQueryAsync`：Guard 判定与缺参检查都读**用户原句**，只有交给 Driver 的语句被改写，所以日志里记的仍是调用方写的那句。
- 测试：`PlaceholderTranslationTests`（Oracle 变、其余三种方言不变、无占位符不变、`'@id'` 数据不变）、`ParameterPlaceholdersTests.WithMarker` 一组、`ReadOnlyGuardTests` 增 `OracleGuard` 并断言 Oracle 方言下普通只读 SQL 照常放行。
- **`t@dblink` 的真相**（票面措辞与机制略有出入）：扫描器把 `@dblink` 当成占位符，所以 `WithMarker` 确实会把它改写成 `:dblink`——但这无害，因为改写发生在语句即将执行前，而带 `@dblink` 的语句根本到不了那一步：`FindUnbound` 先把它识别为未绑定占位符并拒绝。拒绝是对的（dblink 会摸另一个数据库）。`cd163c6` 补上了这两条测试，把机制如实固化。
- 顺带把 Core/契约套件里三处"the three engines"计数字样改成不写数字（新引擎加入即过期，本仓库此坑反复出现）。
