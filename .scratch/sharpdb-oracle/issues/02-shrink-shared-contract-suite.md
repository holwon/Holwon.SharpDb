# 02: 共享契约套件收缩

**What to build:** 共享契约套件只保留四个引擎（MySQL/PostgreSQL/SQLite/Oracle）都能满足的最小公倍数。6 条 Oracle 物理语义不可能满足的断言从共享套件移出，下沉到 MySQL/PostgreSQL/SQLite 各自的测试项目（覆盖零丢失，只是换位置）。此票完成后，Oracle 测试项目才能加入共享套件。

**Blocked by:** None (can start immediately)

**Status:** resolved

- [x] 从 `ProviderContractSuite` 移出 `DescribeTable_reports_a_foreign_key_with_its_actions` 的 `OnUpdate == SetNull` 部分（Oracle 无 `ON UPDATE` 参照动作）
- [x] 从 `QueryContractSuite` 移出 `A_query_answers_columns_once_and_rows_as_lists` 的空串 `""` 部分（Oracle `''` 即 NULL）
- [x] 从 `QueryContractSuite` 移出 `A_parameter_may_be_null`（连带，因 gamma 行的 `''`→NULL 计数变 2）
- [x] 从 `QueryContractSuite` 移出 `Text_outside_ascii_is_written_as_itself`（Oracle 21c 要求 FROM，`ORA-00923`）
- [x] 从 `QueryContractSuite` 移出 `A_failure_names_what_it_could_not_find_as_itself`（`ORA-00942` 消息不含对象名）
- [x] 从 `QueryContractSuite` 移出 `An_explain_of_a_query_answers_with_a_plan`（Oracle 无 `EXPLAIN SELECT`，`ORA-00905`）
- [x] 这 6 条断言分别加入 `SharpDb.Provider.MySql.Tests`、`SharpDb.Provider.PostgreSql.Tests`、`SharpDb.Provider.Sqlite.Tests` 各自的 engine-specific 测试类
- [x] 三个已有引擎的测试项目**仍然全绿**（回归验证：断言被搬走，但行为没变，只是换地方断言）
- [x] 共享套件注释更新，说明"这是四个引擎都能满足的最小公倍数"

## Comments

**2026-10-12 — `d8d5d5b`（后续 `b341145` 扩大收缩范围）**

- 6 条断言移出共享套件，落到三个引擎各自新增的 `*SharedSchemaTests` 类（MySQL 56 / PostgreSQL 88 / SQLite 60 全绿）。
- 移动方式分两种：能整条搬的整条搬（EXPLAIN 出计划、`OnUpdate`、`Text_outside_ascii`、`A_failure_names...`），属于"数据行内容"的则**在原处收窄**并另建数据更全的同名断言（结果集那条共享版只留 id/label/ratio，payload/note 的完整行下沉；`A_parameter_may_be_null` 整条下沉）。
- 共享套件类注释写明"只保留 common denominator，任一引擎物理语义表达不了的都下沉"。
- **真实 Oracle 上跑过之后，收缩范围比票面更大**（`b341145`）：
  - **Oracle 折大写未加引号标识符 → 结果列标签变大写**（`SELECT id …` 得到列名 `ID`）。11 条共享断言逐字比 `["id"]` 因而全挂。修法沿用套件既有惯用法（`count(*)` 那条就靠别名统一）：共享查询给每个列显式加**带引号别名** `AS "id"`，四引擎一致，共享断言仍逐字有效——不是把断言改弱。
  - **`ORA-00942`/`ORA-00904` 不像另外三库那样回显调用方拼的对象名**，所以两条错误消息断言收窄为只断言 `StartsWith("Database error:")`（真正普适的那半），"消息点名对象"另在能做3库各自项目里断言。
- 结果：共享套件保住了跨引擎、非重言的断言力（精确 JSON、参数绑定、行数上限、超时、空结果、排序、FK 删除规则、`Database error:` 前缀），没有把断言改成"用实现算期望"的同义反复。
