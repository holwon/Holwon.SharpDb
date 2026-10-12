# Oracle 的只读是事务级的，且挡不住 DDL

Oracle Provider 的 layer 2 只读用**事务级**机制：连接打开后 `BeginTransaction()` → `SET TRANSACTION READ ONLY` → `BindByName = true`，然后把连接交出。事务**永不提交**，随连接 dispose 回滚——只读模式因此与连接同寿。

反直觉之处在于：`SET TRANSACTION READ ONLY` 看起来是一条"设置会话"的语句，单独发出去也**不报错**——但它什么也不强制。实测：单发该语句后 `INSERT`、`UPDATE`、`CREATE TABLE` 全部成功并已提交。它只作用于**当前事务**，而 Driver 默认 autocommit 下那条事务在语句结束时就已经结束了。所以顺序是硬性的：先开事务，再要只读。

## DDL 会隐式提交，从而解除只读

这是本引擎的**已知缺口**，不在本仓库修复范围内，只记录：

- 只读事务内发 `CREATE TABLE` **成功**（DDL 是隐式提交语句）。
- 该 DDL 提交并结束了只读事务，于是**紧随其后的 `INSERT`/`UPDATE` 也成功**。
- 因此 Oracle 的 layer 2 **不能**保证"引擎层挡住写"。它挡住 DML，挡不住 DDL，且 DDL 会连带解掉 DML 的保护。

结论：在 Oracle 上，**layer 3（最小权限账号）从"建议"变成"必需"**。`README` 的"三层只读"里，第 3 层本就是"真正兜底的那层"；Oracle 把这句话变成了字面事实——只给 `SELECT` 权限的账号是唯一能挡住 DDL 的边界。

## 代价与边界

- **连接在事务中交付**：调用方拿到的每个连接都处在一条只读事务里。事务不提交是刻意的——提交就解除只读。Provider 不保留事务句柄，连接 dispose 时回滚。
- **不引入 `DbTransaction` 参数**：Core 的执行路径（`QueryExecutor`）不需要知道事务存在；Provider 在交出连接前把模式设好即可。这是 ADR 0003"单个深接口"的又一次适用——连接创建连同它的模式设置一起藏在 `CreateReadOnlyConnectionAsync` 后面。
- **不使用 `OracleConfiguration.BindByName` 全局静态**：那是进程级开关，会影响同进程内其他连接。用连接/命令上的实例属性。
- 这条 ADR 不改变 Guard：许可清单（`SELECT`/`EXPLAIN`/`SHOW`）仍不因引擎而变，Oracle 上 `EXPLAIN`/`SHOW` 由引擎自然报错（`ORA-00905` / `ORA-00900`）。
