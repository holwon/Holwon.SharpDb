# 05: Db2 集成测试

**What to build:** 手动 Docker 管理的测试 fixture（Testcontainers 无 Db2 包），跑通共享契约套件 + Db2 专属断言。Docker 不可用时所有测试 skip（不 fail）。此票完成后，Db2 Provider 的行为被真库验证。

**Blocked by:** 03 (套件收缩后才跑得绿), 04 (被测 Provider 要先存在)

**Status:** resolved

- [ ] 新增测试项目，引用 Provider + 共享契约库 + 测试框架（含 `Xunit.SkippableFact`）
- [ ] `IContractDatabase` 实现：手动 `docker run icr.io/db2_community/db2`（`--privileged`、`LICENSE=accept`、密码、`DBNAME`），等待就绪日志，Docker 不可用时设 `Available = false` + `SkipReason`
- [ ] 容器镜像**钉成 `@sha256:` 摘要**；一个容器服务整个测试项目（collection fixture）
- [ ] Seed DDL（Db2 语法）逐条执行（或按票 01 实测决定单条/多条），共享逻辑 schema 与其它引擎一致
- [ ] 空类继承共享 `ProviderContractSuite` 与 `QueryContractSuite`
- [ ] Db2 专属断言：大写折叠透明（报小写）、`FETCH FIRST n ROWS ONLY` 被 Guard 接受、`GENERATED AS IDENTITY` 报自增、`SYSCAT` 目录形状、只读账号挡写（若有的话按实测）
- [ ] Policy 单测：方言值 + 禁用名集形状
- [ ] Docker 可用时全绿；不可用时全部 skip

## Comments
