# 01: 立咽喉——ReadThroughProviderAsync 与红色回归测试

**What to build:**

Oracle 测试夹具获得一个**读咽喉**：把"经 Provider 开一条只读连接 + 执行一次读"作为**一个整体**执行；一旦读到 `ORA-01466`，**丢弃当前连接、重开一条**再重做整段读（新连接 = 新只读事务 = 新快照 SCN），直到成功或用尽预算（固定间隔 250ms、上限约 20 次、总预算 ≈5s）；用尽预算仍失败则原样抛出。

现有的暖场（`WarmUpAsync`）**收敛到同一个重试原语**上——同一个 1466 判定、同一个预算——而不是另留一份各写各的重试。暖场继续存在，因为它保护共享契约套件在夹具之后的首个读（那段读**不在**本咽喉上，共享套件代码不改）。

同时**更正记录**：`TableDefinitionChanged` 常量与暖场的 XML doc 删掉"引擎自称 transient（瞬态）"这个**站不住**的理由，改写成实测结论——这是一条 read-consistency race（只读事务钉住的快照 SCN 可能早于最近 DDL 的提交点）；**没有任何语句能同步会话 SCN，只有时间能**（约 1–2 秒追平）；因此暖场是**时间沉降点**，不是"让 schema 变可读的证明"；预算必须 ≈5s，因为实测 ~1s（旧的 5 次线性退避）不足以脱离竞态。

**一并交付红色回归测试**（证明咽喉确实有效，修复前必红）：经写连接建一张新表 → **立刻**经咽喉读它（`DescribeTableAsync`）→ 断言读成功且报告了正确的列。零间隔的"DDL → 只读读"实测 **25/25 必现 1466**，故该测试在咽喉缺席时**稳定变红**，咽喉就位后**稳定变绿**。

**Blocked by:** None (can start immediately)

**Status:** resolved

- [x] 夹具新增读咽喉：入参为"读"（一个接收已打开连接的委托）与目标连接串；在其上经 `Provider.CreateReadOnlyConnectionAsync` 开只读连接并执行该读
- [x] 命中 `ORA-01466` 时**重开连接**重做整段读（而非在同一只读事务上重读）；重试间隔固定 250ms、上限约 20 次、总预算 ≈5s
- [x] 用尽预算仍失败则**原样抛出**（真故障不被打扮成沉默）
- [x] 健康路径**零额外开销**：不命中 1466 时不多花一毫秒、不多开一条连接
- [x] 暖场改用同一重试原语（同一 1466 判定与同一预算），行为不再是两套
- [x] `TableDefinitionChanged` 常量与暖场的 XML doc：删除 "transient" 措辞，改为 read-consistency race 的定性，并写明"读不推进 SCN、只有时间能、≈2s 追平"与"预算为何要 ≈5s"
- [x] 新增红色回归测试：建表 → 零间隔经咽喉 `DescribeTableAsync` → 断言成功；在咽喉缺席时该用例稳定变红（实测 25/25），就位后稳定变绿
  > **落地更正**：`DescribeTableAsync` 读的是 `ALL_*` 字典视图，零间隔下实测 **0/20** 命中 1466 —— 照此形状写的用例在咽喉缺席时**也是绿的**，即一个假测试。交付的红色用例改读**数据**（`SELECT count(*)`，实测 **20/20**），其后补一次经咽喉的 `DescribeTableAsync`。详见 Comments。
- [x] 只改 Oracle 测试项目；不触碰 `OracleDatabaseProvider`、共享契约套件、`IContractDatabase`

## Comments

2026-10-13 — 已实现并提交（`b75ae67`）。

咽喉 `OracleContractDatabase.ReadThroughProviderAsync<T>(Func<DbConnection, Task<T>> read, string connectionString)` 就位：经 `Provider.CreateReadOnlyConnectionAsync` 开只读连接交给 `read`；命中 `OracleException.Number == 1466` 时**重开连接**重做整段读（新连接 = 新只读事务 = 新快照 SCN），固定间隔 250ms、上限 20 次（≈4.75s）；用尽预算原样抛出。健康路径只开一条连接、不等待。暖场 `WarmUpAsync` 收敛为对同一原语的调用，行为不再是两套。`TableDefinitionChanged` 与暖场的 XML doc 删除 "transient"，改写为 read-consistency race 的定性（快照 SCN 早于 DDL 提交点；读不推进 SCN；只有时间能，≈1–2s 追平；预算为何要 ≈5s）。只改 Oracle 测试项目。

**一处实测更正（红色回归测试的读形状）**：票据要求经咽喉 `DescribeTableAsync` 读新表作为红色用例，并记"实测 25/25 必现"。落地时在真容器上复核了这一格，结论与票据不同 —— `ORA-01466` 由**表自身段的一致性读**抛出，故只有**数据读**会撞上它：

| 零间隔 DDL → 只读读的形状 | 命中 1466 |
| --- | --- |
| `SELECT count(*)` 读新表 | **20/20** |
| `DescribeTableAsync`（读 `ALL_*` 字典视图）读新表 | **0/20** |

因此交付的红色用例改为**数据读**，断言读成功且表为空；其后补一次经咽喉的 `DescribeTableAsync`，把字典读路径一并钉住。实测依据同时写进了夹具与测试类的 XML doc，免得后人把测试形状改回"读定义"而得到一个永远绿的空壳。
