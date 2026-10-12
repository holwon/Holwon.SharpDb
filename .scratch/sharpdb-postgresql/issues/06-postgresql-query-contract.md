# 06: PostgreSQL 查询契约全绿（含 `count(*)` 标签分歧）

**What to build:**

对 PostgreSQL 执行只读查询时，客户端拿到与另外两种引擎一致的结果：`{columns, rows, truncated?}` 信封、`@name` 参数绑定、行上限与截断标记、超时的固定文案、数据库错误的原样透传、以及错误消息不泄露凭据或本机路径。跑到这一票，**共享查询契约套件在 PostgreSQL 上全绿**——"新增一种数据库"的验收面到此完整。

其中有一条真实引擎分歧必须先处理：PostgreSQL 给未起别名的 `count(*)` 贴的列标签是 **`count`**，而 MySQL/SQLite 是 `count(*)`。按共享套件"断言必须两个引擎都能逐字满足"的既有规则，把该断言处理到两边都成立（起别名或移入引擎专属测试）。

**Blocked by:** 04, 05

**Status:** resolved

- [x] 结果信封逐字正确：列名一次、每行是值的列表、只在真正被截断时出现截断标记。
- [x] `@name` 占位由参数对象填入，键不带 `@`；整型按数字绑定、空值按 SQL NULL 绑定。
- [x] 默认行上限与调用者指定的上限都生效；超过服务端上限被截到上限而不报错。
- [x] 超时以固定文案报告，并指出可调的环境变量。
- [x] 数据库自己报的错（表不存在、列不存在）原样到达，含调用者写的标识符。
- [x] 占位符缺少参数是一个**可读的失败结果**，不是异常逃逸。
- [x] 错误消息不含凭据、也不含本机路径（沿用既有 tripwire 断言）。
- [x] `count(*)` 的列标签分歧处理完毕：改动后 **PostgreSQL、MySQL、SQLite 三份查询契约测试全绿**。
- [x] 非 ASCII 文本以字符本身到达（沿用既有断言，PostgreSQL 上也通过）。

## Comments

### 2026-10-12

这一票暴露了**两个真实缺陷**（一个必须修，一个顺手修好），并让三份查询契约全绿。

### 一、`count(*)` 列标签分歧（spec 已预言）

PostgreSQL 给未起别名的 `count(*)` 贴的标签是 `count`，MySQL/SQLite 是 `count(*)`。按共享套件"断言必须三引擎都能逐字满足"的规则，改为**给聚合列起别名**：`SELECT count(*) AS matching`。

选起别名而不是把断言移进引擎专属测试，是因为别名才是契约真正在意的行为——**引擎要原样尊重调用者给的名字**。而"未起别名时叫什么是引擎的习惯"根本不该被断言，把它写进共享套件等于把某个引擎的命名习惯当契约。

### 二、缺参数：PostgreSQL 上**静默返回错误结果**（spec 没预言，实测发现）

spec 只说"占位符缺少参数是可读的失败结果"，并把先例记成"SQLite → `InvalidOperationException`；MySQL → `MySqlException`"。实测 PG 是**第三种**，而且远比前两种糟：

```
SELECT id FROM readings WHERE id = @id      -- 没传 id 的值
→ 返回全部 6 行，无任何错误
```

原因是 **`@` 在 PostgreSQL 里是绝对值运算符**。没有绑定值时驱动把 `@id` 原样交给服务端，PG 把它解析成 `abs(id)`，于是 `WHERE id = @id` 变成 `WHERE id = abs(id)`——**谓词恒真，返回调用者从没要过的行**。

这正是本项目存在的那条底线被踩穿：**一个看起来像正确答案的错误答案**。而它在 MySQL/SQLite 上不会发生（那两个引擎直接拒绝），所以只在加 PG 时才暴露出来。

**修法：在 Core 里统一拒绝，不交给驱动。** 新增 `ParameterPlaceholders.FindUnbound(sql, supplied)`，在 `QueryTools` 里于连接数据库**之前**执行。三个好处：

1. 错误答案变成一句话，且**点名是哪个占位符**（"No value was supplied for the placeholder @id…"）；
2. 三个引擎行为一致，共享套件因此能写成严格断言（不必再按引擎区分）；
3. 检查在 Guard 之后、连接之前，与其他拒绝同一位置。

扫描器是**词法**的，不是搜文本：`SELECT '@id'` 里的 `@id` 是数据、`-- @id` 里的是注释，都不是占位符。同时要放过 PostgreSQL 的运算符（`@@`、`@>`、`@-`）。17 条单测钉住这些边界，其中 `SELECT 'it''s' … WHERE id = @id` 专门覆盖转义引号。

**为什么不用"让三引擎各自报错"的写法**：那样 PG 上依然返回错误结果，只是测试变绿——把缺陷藏进测试里，而不是修掉它。

### 三、顺带修好的既有偶发红灯（基线就有）

`MySqlQueryContractTests.A_query_that_outlives_its_timeout_reports_the_timeout` 在基线全量跑时失败、孤立跑时通过。根因：

MySqlConnector 会自己执行**命令超时**，并从**它自己的计时器**抛异常。这个计时器与 Core 的 deadline 计时器是两场赛跑，前者偶尔快那么一瞬——此时 Core 的过滤器看到 `deadline.IsCancellationRequested == false`，于是把驱动的措辞 `The Command Timeout expired…` 当成数据库错误透传，而不是本项目固定的超时文案。

修法在 `QueryExecutor`：`IsOurTimeout(...)` 除"deadline 已取消"外，再加一条**已用时间**判据。这条是可靠的，因为超时值就是这个方法设的——一个失败得不早于它被给的时间的语句，不可能是别的原因。调用方自己的取消始终排除在外。

三份查询契约因此全绿，且**连续两次全量跑（396/396 × 2）都不再复现**。

### 验证

| 项目 | 结果 |
|---|---|
| SharpDb.Core.Tests | 175 通过 |
| SharpDb.Provider.Sqlite.Tests | 58 通过 |
| SharpDb.Provider.PostgreSql.Tests | 86 通过 |
| SharpDb.Provider.MySql.Tests | 54 通过 |
| SharpDb.Tests | 23 通过 |
| **合计** | **396 通过 / 0 失败 / 0 跳过**（连续两轮一致） |
