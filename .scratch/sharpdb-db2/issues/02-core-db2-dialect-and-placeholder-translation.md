# 02: Core 认得 db2 方言，占位符翻译扩展到 db2

**What to build:** Guard 认得 Db2 的只读 SQL（`FETCH FIRST n ROWS ONLY` 等标准 SQL 形状）；`@name` 占位符被翻译成 Db2 驱动可用的形态。另外六个引擎的行为逐字节不变。此票完成后，db2 的 Policy 才能声明一个真实存在的方言值。

**Blocked by:** 01 (方言覆盖率与占位符形态由实测决定)

**Status:** resolved

- [ ] `SqlDialect` 新增 db2 值；`ReadOnlyGuard` 的方言映射指向 `GenericDialect`（本仓库锁的 `SqlParserCS 0.6.5` 无 Db2 方言，与 Oracle 同形），XML doc 注明降级原因
- [ ] 未知方言值仍**响亮失败**，不回落到没人选的方言
- [ ] Guard 在 db2 策略下接受 Db2 常用只读 SQL（`FETCH FIRST n ROWS ONLY`、双引号标识符等，有断言）
- [ ] 占位符翻译条件从"仅 Oracle"扩展为"Oracle 或 Db2"（复用已有的 `@name` → `:name` 翻译路径；若票 01 实测驱动原生接受 `:name` 之外的形态，按实测调整）
- [ ] 翻译复用 `ParameterPlaceholders` 词法扫描器：字符串/引号标识符/注释里的 `@name` 不被翻（有断言）
- [ ] 另外六个引擎的 Guard 与占位符行为不变（既有测试全绿）

## Comments
