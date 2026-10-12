# 03: PostgreSQL `describe_table`：列与主键

**What to build:**

描述一张 PostgreSQL 表时，代理能看到它全部的列与主键：列按声明顺序、序号 **1 起**、给出**原始类型声明**（如 `character varying(255)`、`numeric(10,2)`、`timestamp with time zone`）、nullability、默认值表达式、以及自增标记（`GENERATED … AS IDENTITY` 与遗留 `serial` 都要认出来）；主键按**键序**给出（复合键时不是声明序）。表名匹配按 PostgreSQL 自身的标识符规则（未加引号折叠为小写），猜错大小写也能命中；名字不存在时明确回"没有"而不是报错。

**Blocked by:** 02

**Status:** resolved

- [x] 列以声明顺序返回，序号从 1 起，与另两种引擎的答案同形。
- [x] 类型是**原始声明**（保留长度/精度/时区），不是被拆开的基类型名。
- [x] nullability 与默认值如实报告。
- [x] 自增：`GENERATED … AS IDENTITY` 与 `serial`（默认值为 `nextval(...)`）都被识别为自增。
- [x] 复合主键按键序返回（例如声明为 `(b, a)` 的键给出 `b, a`）。
- [x] 大小写不敏感的名字匹配；不存在的名字回 `null`。
- [x] 契约套件中列与主键的断言在 PostgreSQL 上通过（含复合键那条）。

## Comments

### 2026-10-12

**这一票的实现在票 02 里就位了**，因为 ADR 0003 的"一个深接口"意味着 `DescribeTableAsync` 是一次实现的整体：写它的时候列与主键就已经在里面了。本票的实际工作是**把验收逐条钉死**，并补上票 02 没覆盖到的那条。

**逐条落在哪里**

| 验收项 | 断言 | 位置 |
|---|---|---|
| 声明顺序、序号 1 起 | `DescribeTable_reports_columns_in_declaration_order` | 共享套件（三引擎各跑一遍） |
| 原始类型声明 | `DescribeTable_reports_the_declared_type_whole` | PG 专属（`character varying(255)` / `numeric(10,2)` / `timestamp with time zone`） |
| nullability 与默认值 | 共享套件同上 + `DescribeTable_reports_a_base_tables_own_nullability_and_defaults` | 共享 + PG 专属 |
| `IDENTITY` 自增 | 共享套件 `..._reports_columns_in_declaration_order`（`users.id` 的 `isAutoIncrement`） | 共享 |
| `serial` 自增 | `DescribeTable_reports_a_serial_column_as_autoincrement` / `..._bigserial_...` | PG 专属 |
| 复合主键按键序 | `DescribeTable_reports_a_composite_key_in_key_order`（`{b,a}`） | 共享 |
| 大小写匹配与 `null` | `DescribeTable_reaches_an_object_through_any_capitalisation` / `..._name_the_catalogue_spells` / 共享的 `..._answers_null_for_a_table_that_does_not_exist` | PG 专属 + 共享 |

**为什么两条自增要分开测**

`GENERATED … AS IDENTITY` 与 `serial` 是同一件事的两种拼写，但留下的证据完全不同：前者设 `attidentity`，后者**什么都不设**——它是个宏，创建序列并把默认值写成 `nextval(…)`。只读标志的 Provider 会把 `serial` 主键报成"调用方必须自己提供"，而共享 schema 用的是 `IDENTITY` 拼写，抓不到这个错。所以 `serial`/`bigserial` 必须在引擎专属测试里钉住，这两条也因此是本票唯一真正新增的断言。

**顺带修掉一个真缺陷**

写 `DescribeTable_reports_the_name_the_catalogue_spells` 时抓到：第一版的名称匹配只写 `c.relname = lower(@table)`，于是用引号建的 `"MixedCase"` 这类表**永远查不到**。PG 的规则是双重的——未引号写法折叠成小写、引号写法原样——所以现在两种都试，精确匹配优先。这条已写进票 02 的 Comments，此处记一笔是因为它是本票验收项"大小写不敏感的名字匹配"在写断言时才被发现的。

**验证**

PostgreSQL 项目中票 03/04 相关断言共 **14 条，全绿**；全项目 **37 条全绿**。
