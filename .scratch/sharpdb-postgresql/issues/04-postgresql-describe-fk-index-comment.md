# 04: PostgreSQL `describe_table`：外键、索引与注释

**What to build:**

同一张表的描述补齐其余部分：外键（含复合键的**本地列与引用列逐步配对**，以及 `ON DELETE` / `ON UPDATE` 动作）、索引（含列清单与唯一性，且**主键背后的索引不重复出现**）、以及表与列的注释。描述一个视图时，只给列，不给键、外键或索引。

**Blocked by:** 03

**Status:** resolved

- [x] 外键报告名称、本地列、引用表、引用列与两个动作。
- [x] 复合外键的本地列与引用列**按同一位置配对**（第二个本地列对应第二个引用列），不是按名或按集合。
- [x] 索引报告列清单与唯一性；表达式列按"无名"处理（与另两种引擎一致）。
- [x] **主键背后的索引不出现在索引清单里**。
- [x] 表/视图注释与列注释如实报告（无注释则为空）。
- [x] 描述视图只给列，键/外键/索引为空。
- [x] 契约套件中这些断言在 PostgreSQL 上通过；**视图列 nullability 在 PG 不传播**这一真实分歧留在引擎专属测试并记录。

## Comments

### 2026-10-12

外键、索引与注释的读法在票 02 一起就位了（同为 `DescribeTableAsync` 的一部分），本票补的是**契约套件覆盖不到的那部分**：注释一处都没有测过，表达式索引也没测过。

**逐条落在哪里**

| 验收项 | 断言 | 位置 |
|---|---|---|
| 外键名称/列/目标/动作 | `DescribeTable_reports_a_foreign_key_with_its_actions` | 共享（三引擎各跑一遍） |
| 复合外键逐位配对 | `DescribeTable_reports_a_composite_foreign_key_in_key_order` | 共享 |
| 索引列清单与唯一性 | `DescribeTable_lists_indexes_with_their_columns_and_uniqueness` | 共享 |
| 表达式列"无名" | `DescribeTable_reports_an_expression_index_with_no_columns` / `..._keeps_the_named_columns_of_a_mixed_index` | PG 专属 |
| 主键索引不重复出现 | 共享的 `..._does_not_list_the_primary_keys_index` + PG 专属的 `..._lists_indexes_without_the_one_backing_the_primary_key` | 共享 + PG 专属 |
| 表/列/视图注释 | `DescribeTable_reports_table_and_column_comments` / `..._a_views_comment` / `ListTables_reports_a_tables_comment` | PG 专属 |
| 视图只给列 | `DescribeTable_describes_a_view` | 共享 |
| 视图列 nullability 不传播 | `DescribeTable_reports_a_views_columns_without_the_base_tables_constraints` | PG 专属 |

**为什么注释只在专属测试里**

共享 schema 刻意不带注释：MySQL 会把**视图**的注释报成字面量 `'VIEW'`（`CREATE VIEW` 根本没有 `COMMENT` 子句），SQLite 则一条注释都不存。所以"注释如实报告"这件事在三个引擎上不可能同形，只能各自钉住。PG 这边实测：表、列、视图的注释都能通过 `obj_description` / `col_description` 读回，三者存法一致（都在 `pg_description`），这是它比 MySQL 干净的地方。

**表达式索引的两种形态都测了**

`indkey` 里表达式的位置是 `attnum = 0`，匹配不到任何列的目录行，于是读回来是 NULL 并被丢弃。实测两种形态：

- 纯表达式索引 `lower(role)` → `indkey = {0}` → 列清单为**空**；
- 混合索引 `(lower(role), org_id)` → `indkey = {0, 2}` → 列清单为 `{org_id}`。

第二种是容易漏的：只测纯表达式的话，"遇到表达式就整条跳过"和"只跳过那一列"两种实现都能通过。

**主键索引为什么必须有专属断言**

PG 把主键索引命名为 `<表名>_pkey`，这是**命名习惯而不是可依赖的标识**——判据是 `pg_index.indisprimary`。共享套件已经能抓错（它断言 `bookings` 的索引清单恰好是 `idx_bookings_parent`），但专属测试用一张名字不遵守任何习惯的表再钉一遍，避免有人把判据换回名字匹配。

**验证**

PostgreSQL 项目中票 03/04 相关断言共 **14 条，全绿**；全项目 **37 条全绿**。
