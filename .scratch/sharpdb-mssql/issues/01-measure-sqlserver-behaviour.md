# 01: 实测 SQL Server 在容器里的行为

**What to build:** 一张把本 feature 依赖的每条事实钉成实测结论的票。这些结论决定 Core 要不要改、Provider 的内省 SQL 怎么写、测试容器怎么起、只读账号怎么授。结论写进本票的 Comment（仓库惯例：实测细节去 `issues/NN-*.md` 的 Comment）。本票无入库代码产出。

**Blocked by:** None (can start immediately)

**Status:** ready-for-agent

- [ ] 容器 recipe 落定：镜像**钉成 CU tag 或 `@sha256:` 摘要**（不用浮动的 `2022-latest`，本仓库规矩：浮动 tag 会被静默升级改掉共享断言在断言什么）；记录就绪判定方式、内存需求、SA 密码复杂度要求、`TrustServerCertificate=True` 的必要性
- [ ] 实测只读 intent 在 standalone 实例上是否真无副作用（连接成功、只读查询照常、写是否被拒）
- [ ] 实测 `OPENROWSET` / `OPENQUERY` / `OPENDATASOURCE` / `OPENXML` 在 mssql 方言下能否解析、解析成 AST 里的什么、Guard 的函数遍历器**能否看见**它们 —— 给出"Core 要不要补一条分支"的明确结论
- [ ] 实测 `TOP n` 与 `[bracket]` 标识符在解析器的 MS SQL 方言下被接受
- [ ] `sys.*` 内省 SQL 跑通并记录真实形状：表/视图清单（含 `MS_Description` 注释）、列（类型、nullability、默认值、`IDENTITY` 自增）、主键列序、复合外键的列配对顺序、索引唯一性与列序（排除主键索引）、视图列
- [ ] 记录默认 schema 的名字与 `list_databases` 排除四个系统库（`master` / `tempdb` / `model` / `msdb`）后的结果
- [ ] 找到一条 Guard 允许的慢只读 SELECT（超过 1 秒），供超时断言使用
- [ ] 记录只读账号的最小授权集，并实测**不授 `VIEW DEFINITION` 时** `list_tables` / `describe_table` 会少给什么（元数据按权限过滤是本引擎的运维要点）
- [ ] 结论以"实测值 + 与直觉相反之处"的形式写进 Comment，供后续票直接引用
