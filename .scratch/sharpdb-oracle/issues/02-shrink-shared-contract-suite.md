# 02: 共享契约套件收缩

**What to build:** 共享契约套件只保留四个引擎（MySQL/PostgreSQL/SQLite/Oracle）都能满足的最小公倍数。6 条 Oracle 物理语义不可能满足的断言从共享套件移出，下沉到 MySQL/PostgreSQL/SQLite 各自的测试项目（覆盖零丢失，只是换位置）。此票完成后，Oracle 测试项目才能加入共享套件。

**Blocked by:** None (can start immediately)

**Status:** ready-for-agent

- [ ] 从 `ProviderContractSuite` 移出 `DescribeTable_reports_a_foreign_key_with_its_actions` 的 `OnUpdate == SetNull` 部分（Oracle 无 `ON UPDATE` 参照动作）
- [ ] 从 `QueryContractSuite` 移出 `A_query_answers_columns_once_and_rows_as_lists` 的空串 `""` 部分（Oracle `''` 即 NULL）
- [ ] 从 `QueryContractSuite` 移出 `A_parameter_may_be_null`（连带，因 gamma 行的 `''`→NULL 计数变 2）
- [ ] 从 `QueryContractSuite` 移出 `Text_outside_ascii_is_written_as_itself`（Oracle 21c 要求 FROM，`ORA-00923`）
- [ ] 从 `QueryContractSuite` 移出 `A_failure_names_what_it_could_not_find_as_itself`（`ORA-00942` 消息不含对象名）
- [ ] 从 `QueryContractSuite` 移出 `An_explain_of_a_query_answers_with_a_plan`（Oracle 无 `EXPLAIN SELECT`，`ORA-00905`）
- [ ] 这 6 条断言分别加入 `SharpDb.Provider.MySql.Tests`、`SharpDb.Provider.PostgreSql.Tests`、`SharpDb.Provider.Sqlite.Tests` 各自的 engine-specific 测试类
- [ ] 三个已有引擎的测试项目**仍然全绿**（回归验证：断言被搬走，但行为没变，只是换地方断言）
- [ ] 共享套件注释更新，说明"这是四个引擎都能满足的最小公倍数"
