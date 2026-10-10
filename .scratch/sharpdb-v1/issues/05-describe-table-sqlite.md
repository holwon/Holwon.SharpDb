# 05: 描述表（SQLite）

**What to build:** AI 能问"这张表长什么样"，拿到完整的结构：列（名称 / 序号 / 声明类型 / 可空 / 默认值 / 自增 / 注释）、主键、外键、索引。有了它，AI 才能写出正确的连接与查询，而不是靠猜。

**Blocked by:** 04

**Status:** resolved

- [x] SQLite Provider 实现描述表的能力，把 04 定型的 Schema 聚合**填满**
- [x] `describe_table` 工具返回聚合根的单对象形态：名称、类型、可选注释、列数组、主键、外键数组、索引数组
- [x] 主键是**有序列名数组**；`indexes` **不重复包含主键**
- [x] 外键含：名称、本地列、被引用的表与列、删除动作、更新动作（动作序列化为枚举名）
- [x] 空值字段省略，不输出 `null`
- [x] 自增由 Provider **计算**得出（主键 + 整型声明类型），不是直接读某个字段
- [x] 外键指向父表主键而 SQLite 未显式记录列时，被引用列可为空列表且不报错
- [x] 表不存在时返回错误结果（`IsError`），带明确的"表不存在"消息
- [x] 视图也能被描述

---

**验收说明**：断言"这张表长什么样"（列序、类型字符串、主键序、外键与索引内容），**不断言**内省 SQL 文本。测试要覆盖：有外键与索引的表、无外键的表、空默认值与有默认值、视图、不存在的表。

---

## Comments

**2026-10-10 — 完成。** 九条验收项逐条落地，`dotnet build SharpDb.slnx -warnaserror` 与 `dotnet test SharpDb.slnx` 双绿（0 警告 / 0 错误；**149** 例，失败 0——本票新增 **24** 例：SQLite Provider 描述测试 21 例、MCP 层 3 例）。

交付物：

- `src/SharpDb.Abstractions/IDatabaseProvider.cs` —— 契约 +`DescribeTableAsync`，共 **5** 个成员（票 04 的"四个成员"到此补齐，与 spec 草图一致）
- `src/SharpDb.Provider.Sqlite/SqliteDatabaseProvider.cs` —— 真实内省实现
- `src/SharpDb.Provider.MySql/MySqlDatabaseProvider.cs` —— 同契约的 `INFORMATION_SCHEMA` 实现（票 07 才测，见下）
- `src/SharpDb.Core/Mcp/SchemaTools.cs` —— `describe_table` 工具；`ToolResults.cs` —— 新增失败结果与枚举名序列化
- `tests/SharpDb.Provider.Sqlite.Tests/SqliteDescribeTableTests.cs`（21 例）、`tests/SharpDb.Core.Tests/Mcp/SchemaToolTests.cs`（3 个工具 + 参数必填 + 描述形状 + 不存在表）

### 一、SQLite 的"表不存在"必须从 schema 表回答，不能靠 pragma

实测（Microsoft.Data.Sqlite 10.0.12 / SQLite 3.53.3）：`pragma_table_info('不存在')`、`pragma_index_list`、`pragma_foreign_key_list` 对**不存在的表**一律**返回 0 行且不报错**——与"表存在但没有任何列/索引/外键"**完全同形**。所以"名字是否指向一个对象"由 `sqlite_schema`（`SELECT type, name ... WHERE name = @table`）回答，它同时给出**对象种类**（table / view），一次查询解决两件事。

同一处还顺手挡掉了引擎自建对象：`AND name NOT LIKE 'sqlite\_%' ESCAPE '\'`（沿用票 04 的转义写法）让 `sqlite_sequence` 这类记账表**既不出现在列表里，也无法被描述**。

**名字大小写**：实测 `sqlite_schema WHERE name = @table COLLATE NOCASE` 能命中 `Users`/`USERS`，而 pragma 虽也大小写不敏感，但拿不到"引擎实际拼写"的名字。用 `COLLATE NOCASE` 加 `ORDER BY CASE WHEN name = @table THEN 0 ELSE 1 END` 让精确大小写优先。`NOCASE` 只折叠 **ASCII** 大小写——这是引擎自身的限制，命中不了非 ASCII 的变体，作为已接受的缺口记录在 XML docs 里。

### 二、自增是**计算**出来的，规则比"主键 + 整型"更窄

`pragma_table_info` 的 `pk` 列是**键内 1-based 序号**（不是布尔），所以"主键"与"复合主键的第几列"是同一次读取的两件事。计算规则取引擎自身的定义，比票面"主键 + 整型声明类型"更紧：

- **整型**必须是精确的 `INTEGER`（`BIGINT` / `VARCHAR` 主键**不是** rowid 别名，因此**不是**自增——已实测）
- 且该列必须是主键的**唯一**成员（`pk` 序号的**最大值恰为 1**）——否则 `CREATE TABLE t (a INTEGER PRIMARY KEY, ...)` 的单列主键会被误判

复合主键的**第一列** `pk=1`，但多列键的值由引擎按整串键生成，不是单列自增 → 已加测试固定"复合键不自增"。

### 三、主键顺序来自键序，不是声明序

主键是**有序列名数组**，而"声明序"与"键内序"可以不同：`CREATE TABLE composite (a TEXT, b INTEGER, PRIMARY KEY (b, a))` 的**键是 `(b, a)`**，`a` 反而是第二个。因此 `PrimaryKey` 按 **`pk` 序号**排序得到，不是按列的 `cid`。这条也有独立测试。

### 四、外键：SQLite 无名，`id` 也不是声明序

`pragma_foreign_key_list` 的 `id` 分组一个约束的多列，但实测 **`id` 的先后与声明顺序相反**（两条 `FOREIGN KEY`，先声明的拿到 `id=1`）——所以 Provider **按 `id` 分组、按 `id` 排序**，不依赖"声明序"这个不存在的保证。`to` 列为 NULL 时表示指向父表主键而**未显式写出被引用列** → `ReferencedColumns` 返回 `[]`，不报错（票面明列）。SQLite 不存外键名，`Name` 由 Provider 按 `fk_<表>_<id>` 派生并在 XML docs 写明。

### 五、索引：按 `origin` 排除主键，索引列用**关联**取

- 主键索引**按 `origin = 'pk'` 排除**，不按名字：`INTEGER PRIMARY KEY` 根本不产生索引行，而复合键 / 非整数键会产生 `sqlite_autoindex_*`——按 `'pk'` 排除对两种情形都成立。
- 索引列不逐名拼 SQL，而是用**关联**：`pragma_index_list(@table) AS il JOIN pragma_index_info(il.name) AS ii`——表值 pragma 的实参可以是**同一查询里另一张 pragma 的列**，实测可行。这样避免"取出索引名 → 拼进下一句 SQL"的引号转义面。
- **表达式索引**：`pragma_index_info.name` 对表达式列为 **NULL**。首版 `GetString` 会在这类索引上**抛异常**，审阅时发现并修正为**跳过**（索引仍列出，`Columns` 为空）——已加测试。

### 六、视图的"列不撒谎"

`pragma_table_info` 对视图返回的 `notnull` / `dflt_value` / `pk` 是视图自身声明给出的（实测一个 `SELECT id, name` 的视图三列全 `pk=0`、`notnull=0`），因此视图片段的 `PrimaryKey` / `ForeignKeys` / `Indexes` 都是**空数组**，而列照常给出——这正是票面"视图也能被描述"要的。

### 七、工具层

- 形状即聚合根的**单对象形态**：`{name, type, comment?, columns[], primaryKey[], foreignKeys[], indexes[]}`。`type` 用**小写 `"table"`/`"view"`**，与 `list_tables` 一致（spec 未固定大小写，一致性优先；如要"原样枚举名"需另开一票）。
- **空值省略**靠序列化器的 `WhenWritingNull`；新增 `JsonStringEnumConverter` 让 `ForeignKeyAction` 序列化为**枚举名**（`"Cascade"`），满足票面"动作序列化为枚举名"。该 converter 同时作用于 `ListTables`，但后者不输出枚举，无影响。
- **表不存在** → `ToolResults.Failure(...)` 返回 `CallToolResult{IsError=true}`，消息 `Table 'x' does not exist.`（ADR 0010：业务失败以结果返回，不抛异常）。这是**本仓库第一条失败结果路径**，`ToolResults` 因此新增了 `Failure` 成员。

### 已接受的边界

- **MySQL 的 `DescribeTableAsync` 零测试**：接口加成员会同时强制两个实现者，而"无 dummy code"要求 MySQL 给出真实实现。与票 04 的 MySQL 两成员是**同一条边界**，票 07 必须补上。本票 MySQL 代码编译通过、逻辑真实（`INFORMATION_SCHEMA`：`COLUMNS` / `KEY_COLUMN_USAGE` / `REFERENTIAL_CONSTRAINTS` / `STATISTICS`），但未被任何测试驱动。
- **生成列被略过**：`pragma_table_info` 不含生成列，Provider 未改用 `pragma_table_xinfo`——票面只要求"列（名称 / 序号 / 声明类型 / 可空 / 默认值 / 自增 / 注释）"，生成列不在其中。已用测试**显式固定**这一行为，避免日后被当成 bug 静默"修好"。
- **非 ASCII 大小写**：`COLLATE NOCASE` 只折叠 ASCII。
- **`indexes` 不含排序方向 / 部分索引条件**：Schema model 无对应字段（v1 范围）。

`bash tools/check-retired-identifiers.sh`（经 Git 的 bash）通过。本 Comment 未新增退役标识符。

### 八、双轴 code review 后的修正（均已落地）

对 diff 跑了 Standards 与 Spec 两轴并行审阅，结论都是"过，无 blocker"。采纳的修正：

1. **类注释过时**：`SchemaTools` 类注释仍写 "Neither Tool"，而本票后类里有**三个** Tool → 改 "No Tool"。
2. **一处参数注释与代码不符**：`ColumnRow.Ordinal` 注释写"zero-based"，实际存的是 `reader.GetInt32(1) + 1`（1-based）→ 注释订正。
3. **接口契约说了实现做不到的话**：`DescribeTableAsync` 原写"名字按大小写不敏感匹配，与引擎一致"。SQLite 用 `COLLATE NOCASE` 做到了，但 MySQL 的 `TABLE_NAME = @table` 在 `lower_case_table_names=0` 的 Unix 上是**大小写敏感**——契约承诺了一个 MySQL provider 给不出的保证。已改为把匹配交给引擎定义（"SQLite 双向折叠；不折叠的服务器需按目录里的拼写作答"）。
4. **MySQL 注释里的 "catalogue" 拼写**与仓库既有的 `ProviderCatalog` 不一致 → 统一为 catalog。
5. **测试桩的数据不自洽**：`StubDatabaseProvider` 的注释称"含一个默认值"，但所有列 `DefaultValue` 都是 null；外键的本地列 `org_id` 也不在列清单里（真实库里不可能）。已补齐 `org_id` 与 `role`（默认 `'member'`），并让 MCP 断言同时覆盖"**在的值保留**"（`defaultValue` = `'member'`）与"**空的值省略**"两条边。
6. **弱断言**：`Assert.NotEqual(true, IsError)` 沿用票 04，但**实测 `IsError` 在成功时是 `null` 不是 `false`**（我一度改成 `Assert.False` 结果测试红）——已加注释写明为何必须这样断言，避免下次又被"修"成 `Assert.False`。
7. **补两条反向边界测试**：① `UNIQUE` **约束**产生的 `sqlite_autoindex_*`（`origin='u'`）**必须被列出**——否则把 `origin <> 'pk'` 误写成 `<> 'c'` 不会被发现；② `describe_table` 的 `table` 参数在发布的 JSON Schema 里**确实是 required**。

**未采纳**：一位审阅者建议把两个 Provider 里逐字节相同的 `Action(string)` / `AddTableParameter` 提到 `SharpDb.Abstractions` 共享。**不采纳**——`Abstractions` 是"无行为"的 Schema model + 一个端口（票 01 起即如此，且它没有测试项目），塞入引擎文本→枚举的映射会把它变成"有逻辑的库"；这点重复换来的独立性值得（ADR 0003：一个 Provider 的内部实现不与其他 Provider 耦合）。
