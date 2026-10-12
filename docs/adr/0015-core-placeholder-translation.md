# Core 按方言翻译占位符：`@name` → `:name`

用户 SQL 里的 `@name` 在**执行前**由 Core 翻译成 `:name`，**仅对 Oracle 方言生效**。新增 `SqlDialect.Oracle` 枚举值驱动这次翻译；其他三个引擎的语句**逐字节**原样交给各自 Driver。

反直觉之处在于：把占位符拼进 SQL 看起来是 Provider 的事——毕竟只有 Provider 知道自己的引擎怎么写占位符。但 **Provider 不在执行路径上**：它是 `IDatabaseProvider`，看不到用户语句，也碰不到 Core 建的 `OracleCommand`。要在 Provider 里做这件事，就得往接口加一个"改写语句"的成员，那正是 ADR 0003/0013 反对的形态。所以分工是：**Provider 贡献方言这个值，Core 用这个值决定要不要改写**。这与 ADR 0013 的"策略是数据、算法在 Core"是同一形状。

## 为什么必须翻译

实测：`SELECT @id FROM dual` → `ORA-00936`（缺失表达式）；`BindByName` 无论 true/false 都一样。Oracle 的占位符**是 `:name`**，这是语法而非绑定选项——`@` 在 Oracle SQL 里根本不是占位符标记。

## 复用词法扫描器，而非另写一个

翻译复用 `ParameterPlaceholders` 已有的扫描器（它已经会跳过引号与注释与字符串），所以：

- 字符串字面量里的 `@id` 是**数据**，不动。
- 引号标识符里的 `@id` 是**名字**，不动。
- 注释里的 `@id` 是**散文**，不动。
- `t@dblink` 里的 `@dblink` 仍被识别为占位符 → 走"未绑定占位符"的拒绝路径。**拒绝是对的**：dblink 会去摸另一个数据库，违背 `use-cases.md` 的意图。

## 代价与边界

- 只改 marker，不改名字，也不改其他任何字符。
- 无占位符的语句原样返回（不重新分配字符串）。
- 这是 Core 唯一一处因新引擎而改的执行路径逻辑；除 `SqlDialect` 枚举与 `ReadOnlyGuard.DialectFor()` 的映射分支外，Core 没有别的改动。
- `DialectFor` 里 Oracle 映射到 `GenericDialect`（本仓库锁的 `SqlParserCS 0.6.5` 没有 Oracle 方言）：实测通用方言能读 Oracle 常用只读 SQL 的绝大多数，代价是一批**误拒**（功能损失，安全无损，ADR 0005）。不升级到 `0.6.6-release0000`——实测其 `OracleDialect` 不修 `MINUS`/`CONNECT BY`/`(+)` 且带回归。
