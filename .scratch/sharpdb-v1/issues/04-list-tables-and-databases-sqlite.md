# 04: 列出表与库（SQLite）——首个端到端垂直片

**What to build:** AI 第一次能通过 MCP 问数据库"你有哪些表"。这是 **walking skeleton**：它同时确立 Provider 契约的形状、Schema 聚合的边界，并首次打通 MCP → Tool → Provider → 数据库的整条链路。

**Blocked by:** 02

**Status:** ready-for-agent

- [x] `IDatabaseProvider` 契约定型（**四个**成员；第五个 `DescribeTableAsync` 随票 05 落地——偏离票面"五个"的理由见下方 Comment），并在 Core 侧提供"由类型标识装配对应 Provider"的**显式**分支——零反射
- [x] **Schema 聚合一次定型**：聚合根及其子对象（列 / 外键 / 索引）的结构、字段与不变量全部就位，即使本票只用到其中一部分
- [x] SQLite Provider 实现契约中的四个成员：类型标识、建只读连接、列表、列数据库
- [x] `CreateReadOnlyConnectionAsync` 返回**已打开且已进入引擎级只读**的连接——只读体现在它增补出的连接串上
- [x] `list_tables` 工具：列出表**与视图**，每项含名称、类型（表 / 视图）、可选注释；按名称排序
- [x] `list_databases` 工具：返回连接可见的库
- [x] 两个工具的 description 与参数 description 均就位；annotations 声明为只读、非破坏、幂等、封闭世界
- [x] 两个工具都从环境变量读到的连接串工作，且**不经过 Guard**（它们不是用户 SQL）
- [x] MCP 端到端：进程内客户端能列出工具并成功调用这两个

---

**验收说明**：Provider 测试断言"这个库里有哪些表"，**不断言**生成的内省 SQL 字符串。测试用**临时文件库**（票面原写"命名共享缓存内存库 + 一条保活连接"，因与 Provider 强制的 `Mode=ReadOnly` 互斥而经用户拍板改为文件库——见下方 Comment），且**传连接字符串而非连接实例**（与"每次调用现开现关"吻合）；fixture 先用可写连接建表，再让 Provider 以只读打开。

---

## Comments

**2026-10-09 — 完成。** 九条验收项中八条按字面落地，两处经用户拍板偏离票面（见下）。`dotnet build SharpDb.slnx -warnaserror` 与 `dotnet test SharpDb.slnx` 双绿（0 警告 / 0 错误；125 例，失败 0）。

交付物：

- `src/SharpDb.Abstractions/Schema/` —— **Schema 聚合一次定型**：`TableSchema`（聚合根）+ `ColumnInfo` / `ForeignKeyInfo` / `IndexInfo`（子记录）+ `TableInfo`（read model）+ `TableType` / `ForeignKeyAction`（闭合 enum）
- `src/SharpDb.Abstractions/IDatabaseProvider.cs` —— 契约 +2 个成员（`ListTablesAsync` / `ListDatabasesAsync`），共 4 个
- `src/SharpDb.Provider.Sqlite/SqliteDatabaseProvider.cs` —— 两个新成员的真实内省
- `src/SharpDb.Provider.MySql/MySqlDatabaseProvider.cs` —— 同样两个成员的真实内省（`INFORMATION_SCHEMA`）
- `src/SharpDb.Core/Mcp/SchemaTools.cs`、`ToolResults.cs` —— 两个工具
- `tests/SharpDb.Provider.Sqlite.Tests/`（11 例）、`tests/SharpDb.Core.Tests/Mcp/`（5 例）

### 偏离票面之一：测试基座由内存库改为**临时文件库**（用户拍板）

票面指定"命名共享缓存内存库 + 保活连接"。**实测该基座与票 02 已落地的 Provider 行为结构性互斥**：Provider 无条件把 `Mode` 覆写成 `SqliteOpenMode.ReadOnly`，而 `Mode` 是**单值**（`Memory` XOR `ReadOnly`）。三种拼法全部实测（Microsoft.Data.Sqlite 10.0.12）：

| 调用方连接串 | 结果 |
|---|---|
| `Data Source={n};Mode=Memory;Cache=Shared` | **Error 14 `unable to open database file`** |
| `Data Source=file:{n}?mode=memory&cache=shared` | 能开，但**写入成功**——URI 的 `mode=memory` 静默架空强制 ReadOnly |
| `Data Source=file:{n}?cache=shared` | 同上，写入成功 |

（对照：ABP 的 `AbpUnitTestSqliteDatabase` 用的正是这个内存库配方，但它**从不设** `ReadOnly`——源码已核对。差异只有一处，而这一处是致命的。）

**顺带订正一处 spec 的既有论断。** spec 的"已知残余风险"引用 EFCore#15118，称"SQLite 引擎层面**忽略**内存库的 `SQLITE_OPEN_READONLY`"。**实测不成立**：`Data Source=:memory:;Mode=ReadOnly` 拒绝写入（`attempt to write a readonly database`）。真正的障碍比该论断窄得多，且只针对**命名共享缓存**这一种形式。spec 中该段与"靠断言连接串 `Mode == ReadOnly` 兜底"的补救措施**本次均未采用**，因为 `CreateReadOnlyConnectionAsync` 返回连接实例而非字符串，那个断言在 seam 上够不着。

采用**临时文件库**后额外收益：**引擎级只读首次被真实验证**（写入被拒），票面基座反而做不到这点。保留了票面要的全部性质——传连接字符串、fixture 先可写 seed 再只读打开、每测独立。文件用 GUID 命名 + `Pooling=False`（否则 Windows 拒绝删除仍被占用的文件）。

### 偏离票面之二：契约加到 **4** 个成员，不是 5 个（用户拍板）

C# 接口加成员会**同时**强制所有实现者实现。Host 同时引用两个 Provider，故按票面"加到五个"会立刻需要：SQLite 补 `DescribeTableAsync` 桩、MySQL 补 3 个桩——**违反本仓库"无 dummy code"硬规则**，且每张票都建立于"构建恒绿"。

改为：本票只加调用方真正需要的两个成员（接口 4 个），**两个 Provider 都给出真实实现**（MySQL 的 SQL 已由 `docs/research/schema-introspection-sql.md` 给出；其容器化测试是票 07 的范围）。票 05 再加 `DescribeTableAsync`。

### 一个前置事实的更正：MCP 工具描述**不吃 XML `<summary>`**

实测（ModelContextProtocol 2.2.0）：`[McpServerTool]` 的方法若要向客户端暴露 description，必须用 `[Description("...")]`（`System.ComponentModel`）；XML `<summary>` **完全不参与**，`McpServerToolCreateOptions.Description` 也**实测无效**。工具名由 PascalCase 派生为 snake_case（`ListTables` → `list_tables`）。

ADR 0010 的依据也一并复核：工具**抛异常**时 SDK 只回泛化文案 `An error occurred invoking 'x'.`（自定义消息被丢弃），与 ADR 描述一致。本票两个工具无业务失败路径（票 06 才引入），故未实现 `IsError` 分支。

### 审阅后的三处修正（均已落地）

1. **MySQL 视图的注释** —— 实测文档核对：`INFORMATION_SCHEMA.TABLES.TABLE_COMMENT` 对视图返回**字面量 `'VIEW'`**（`CREATE VIEW` 根本没有 `COMMENT` 子句），且未注释的表返回空串而非 null。原实现会把 `'VIEW'` 当注释回给 AI。已改为两者都归 null。
2. **`LIKE 'sqlite_%'` 的 `_` 是通配符** —— 会**误滤**用户真实创建的 `sqliteX…` 表。实测：未转义时 `sqliteXgone` 被吞掉，`ESCAPE '\'` 后正常。已加转义并加回归测试。
3. **`SchemaTools` 的连接串** —— 原用 `?? string.Empty` 静默兜底，会把装配缺陷伪装成数据库错误。改为构造期显式守卫。

（另按 `abp-naming-conventions` 把助手方法 `Data` 改名为 `Success`；`ToolResults` 的类注释原先称"两个成员"，实际只有一个，已订正。）

### 已接受的边界

- **MySQL 两个新成员零测试**：spec 的 seam 2 要求共享契约套件两库各跑一遍，但容器化 MySQL 是票 07 的范围。本票 MySQL 代码**编译通过且逻辑真实**，尚未被任何测试驱动——这是本票最实在的残余风险，票 07 必须补上。
- **Schema 聚合的"不变量"是约定而非强制**：`TableSchema` 是位置 record，`Indexes` 不含主键、外键表/列成对出现，靠**构造者**满足（Provider 在另一程序集，`internal` 构造不可行）。已在 XML docs 写明。
- **`DescribeTableAsync` 未加**：见上。`IndexInfo` / `ForeignKeyInfo` / `ColumnInfo` 本票无消费者，是票面明确要求的"一次定型"。
- **Host 未接 stdio 循环**：票面只要求进程内 MCP 端到端。实测（`McpServer` + `StdioServerTransport`，stdin 立即 EOF）服务**干净退出、exit code 0**，故接上不会打破票 02 的成功路径测试——留给后续票。

`bash tools/check-retired-identifiers.sh`（经 Git 的 bash）通过。本 Comment 未新增退役标识符。
