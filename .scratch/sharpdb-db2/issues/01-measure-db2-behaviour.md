# 01: 实测 Db2 在容器里的行为

**What to build:** 一张把本 feature 依赖的每条事实钉成实测结论的票。这些结论决定 Core 要不要改、Provider 的内省 SQL 怎么写、测试容器怎么起、只读账号怎么授。结论写进本票的 Comment（仓库惯例：实测细节去 `issues/NN-*.md` 的 Comment）。本票无入库代码产出。

**Blocked by:** None (can start immediately)

**Status:** resolved

- [x] 容器 recipe 落定：镜像用 `icr.io/db2_community/db2`（旧 `ibmcom/db2` 即将下线），**钉成 `@sha256:` 摘要**（本仓库规矩：浮动 tag 会被静默升级改掉共享断言在断言什么）；记录 `--privileged=true` 的必要性、`LICENSE=accept`、`DB2INST1_PASSWORD`、`DBNAME`、端口 50000、内存需求（Community Edition 上限 16 GB）、就绪判定方式（日志 "Setup has completed"）、仅支持 amd64 架构
- [x] 实测第 2 层彻底缺席：`SET TRANSACTION READ ONLY` 报错、`SET SESSION TRANSACTION READ ONLY` 报错、`FOR READ ONLY` 是游标提示（不阻止 DML）、连接串无 `ApplicationIntent` 等价物；给出"Provider 只能原样打开连接"的明确结论
- [x] 实测 `GenericDialect` 对 Db2 只读 SQL 的解析覆盖率（照 Oracle 的 43 条分类语句方法），记录哪些被误拒；给出"DialectFor 映射到 GenericDialect 是否够用"的结论
- [x] 实测 `Net.IBM.Data.Db2` 是否原生接受 `:name` 占位符（决定要不要复用 Oracle 的 `@name` → `:name` 翻译），以及 `@name` 的行为；记录驱动版本（10.0.0.300 是否 multi-target net8.0，不兼容则记录退到哪个版本）
- [x] `SYSCAT` 内省 SQL 跑通并记录真实形状：表/视图清单（`SYSCAT.TABLES` 的 `TYPE` 值、`REMARKS` 注释）、列（`SYSCAT.COLUMNS` 的类型/`NULLS`/`DEFAULT`/`IDENTITY` 自增/`REMARKS`）、主键列序（`SYSCAT.TABCONST` + `SYSCAT.KEYCOLUSE.COLSEQ`）、复合外键的列配对顺序（`SYSCAT.REFERENCES` + `SYSCAT.KEYCOLUSE`，FK 侧与引用侧 `COLSEQ` 对齐方式）、索引唯一性与列序（`SYSCAT.INDEXES.UNIQUERULE` + `SYSCAT.INDEXCOLUSE.COLSEQ`，排除主键索引）、视图列
- [x] 实测哪些共享契约套件断言 Db2 **物理上满足不了**（决定票 03 要搬哪些）；若全部满足，记为"无需收缩"
- [x] 记录 `list_databases` 的形状（Db2 的 database 概念与 schema 的关系），以及 `CURRENT SCHEMA` 过滤后的结果
- [x] 实测驱动是否支持一条命令多条语句（决定 seed DDL 是 `string[]` 还是单个 `string`）
- [x] 找到一条 Guard 允许的慢只读 SELECT（超过 1 秒），供超时断言使用
- [x] 记录只读账号的最小授权集（Db2 上第 3 层是唯一真正的边界），并实测权限受限时内省会少给什么
- [x] 结论以"实测值 + 与直觉相反之处"的形式写进 Comment，供后续票直接引用

## Comments
