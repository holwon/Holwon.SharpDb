# Db2 LUW 没有引擎层只读，连意图都没有

Db2 LUW Provider 在 `CreateReadOnlyConnectionAsync` 里只做 `new DB2Connection(connectionString)` + `OpenAsync`，不加任何只读修饰。**Db2 LUW 根本没有只读模式可以设。**

`SET TRANSACTION READ ONLY` 是语法错（`SQL0104N`）。`SET SESSION TRANSACTION READ ONLY` 同样是语法错。`FOR READ ONLY` 是游标提示，不阻止 DML。连接字符串没有 `ApplicationIntent` 等价物。

与 SQL Server 的 ADR 0016 同形，但更极端——SQL Server 至少有 `ApplicationIntent=ReadOnly`（虽然在 standalone 上不强制，但表达了意图）；Db2 连这个都没有。

## 实测（Db2 LUW 11.5.8，ibmcom/db2 Docker 镜像）

- `SET TRANSACTION READ ONLY`：**`SQL0104N`**——"An unexpected token 'READ ONLY' was found following 'SET TRANSACTION '."
- `SET SESSION TRANSACTION READ ONLY`：**`SQL0104N`**——"An unexpected token 'TRANSACTION READ ONLY' was found following 'SET SESSION '."
- `FOR READ ONLY`：游标提示，同连接上 `INSERT` 成功。
- 连接字符串无只读关键字可加。

因此"即使 Guard 被绕过、数据库仍会拒绝写"这句话在**本引擎上不成立**。

## 第 2 层在本引擎上完全缺席

ADR 0002 的三层模型本身不变，变的是"第 2 层**可能完全不存在**"这件事。SQL Server 的第 2 层是 `Read-only intent`（ADR 0016）——至少有一个关键字可以加；Db2 的第 2 层**连 intent 都没有**，是三层模型中最"裸"的形态。

所以本引擎上真正挡在写前面的是**连接所用账号的权限**（第 3 层），而 SharpDb 把用户写的连接串原样拿去用，**不检查、不校验、不改写**用户的连接串，也不要求用户部署成某种形态。

## 第 1 层因此更重要

Guard 在本引擎上是**唯一**能拦住写语句的层。在别的引擎上引擎层还会兜一次底，在本引擎上不会。Guard 的许可清单不变（`SELECT` / `EXPLAIN` / `SHOW`），但它的角色在本引擎上比在任何其他引擎上都重要。

## 占位符翻译

Db2 驱动（`Net.IBM.Data.Db2`）只支持 `?` 位置参数绑定——`:name` 和 `@name` 命名参数在绑定阶段都会失败（`SQL0313N`）。Core 的占位符翻译把 `@name` 替换为 `?`，Provider 的内省 SQL 也使用 `?`。这是唯一一个不使用命名参数的引擎。

## 标识符大小写

Db2 把未加引号的标识符折为大写（与 Oracle 同形）。Provider 把全大写名折成小写报给消费方；含小写的名字（来自引号标识符）原样返回。

## 代价与边界

- 不为本引擎引入任何只读机制：第 2 层缺席在本仓库只**记录**，不修复。
- 本 ADR 不改变 Guard 的许可清单。
- Db2 LUW 只支持 `ON UPDATE NO ACTION`——`ON UPDATE SET NULL` 和 `ON UPDATE CASCADE` 都是语法错。共享契约套件不 assert `ON UPDATE` 动作（与 Oracle 同因），引擎专属测试覆盖 `NO ACTION`。
