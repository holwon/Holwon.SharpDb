# 05: mssql 测试项目：共享套件 + 引擎专属断言

**What to build:** 第五个引擎跑通两个共享契约套件，并断言它独有的真事。测试用 Testcontainers 起真实 SQL Server 容器，用**管理员账号播种**、用**最小权限登录**供 Provider 连接 —— 共享断言跑在 ADR 强制的真实部署形态上。此票完成后，mssql 的测试覆盖完整。

**Blocked by:** 02 (共享套件必须先收缩，否则 mssql 跑不过), 04 (Provider 必须先存在)

**Status:** ready-for-agent

- [ ] 新增测试项目，引用 Testcontainers 的 SQL Server 包 + 共享契约库 + Provider 项目
- [ ] 测试基座用**钉住的镜像**起容器、播种共享 schema 的 T-SQL 版本（`ContractSchema` 加一份 mssql DDL）、Docker 缺席即 skip
- [ ] 测试基座用管理员账号播种、用最小权限登录（`SELECT` + `VIEW DEFINITION`）供 Provider 连接；交给 Provider 的连接串**不含**只读 intent（加它的是 Provider 的事）
- [ ] 两个共享契约套件各有一个空子类，全部通过
- [ ] 引擎专属断言：连接串带只读 intent；**最小权限账号下引擎拒写**（本引擎真正的边界）；裸名与限定名的分界；`MS_Description` 注释；`IDENTITY` 自增；复合外键列配对顺序；索引唯一性与列序；`EXPLAIN`/`SHOW` 由引擎报错；`[bracket]` 标识符被 Guard 接受；rowset function 被 Guard 拒绝；超时被中断
- [ ] 集合夹具：**一个容器服务整个集合**，不是每个测试类一个容器
- [ ] 需要自有 schema 的测试用另一个数据库，不污染共享 schema（共享套件精确断言表集合，任何自有 schema 都会让别的测试翻红）
