# SQL Server 没有引擎层只读，连接只带只读意图

SQL Server Provider 在 `CreateReadOnlyConnectionAsync` 里用 `SqlConnectionStringBuilder` 把 `ApplicationIntent=ReadOnly` 写进连接串再打开连接。**这不是把会话设成只读模式**——本引擎根本没有这种东西。

`SET TRANSACTION ISOLATION LEVEL` 只有五个隔离级别，没有 READ ONLY。`ApplicationIntent=ReadOnly` 是 Always On 的只读**路由**开关：目标是可读副本时，引擎自己拒绝写，那是硬边界；目标是 standalone 实例时，关键字被**接受**、不报错、也什么都不强制。

## 实测（SQL Server 2022 CU27，16.0.4295.3）

- 带 `ApplicationIntent=ReadOnly` 的连接：读（`SELECT`）正常工作。
- **同一条连接上 `CREATE TABLE` 成功。** 这在 standalone 实例上不是意外，而是设计：intent 影响的是路由，不是权限。
- 因此"即使 Guard 被绕过、数据库仍会拒绝写"这句话在**本引擎上不成立**。

## 第 2 层在本引擎上缺席，这改变的是模型

ADR 0002 的三层模型本身不变，变的是"第 2 层**可能不存在**"这件事第一次成为模型的一部分——由 `GLOSSARY.md` 的新术语 **`Read-only intent`** 承载，与 `Read-only enforcement` 相对：前者表达意图、只在特定部署形态下才真的拒绝写；后者才是保证。MySQL 的会话级、SQLite 的文件级、Oracle 的事务级都是 enforcement；SQL Server 的这条是 intent。

所以本引擎上真正挡在写前面的是**连接所用账号的权限**（第 3 层），而 `ApplicationIntent=ReadOnly` 是**运维可选**的加固：把连接指向 Always On 可读副本时它能多挡一层，指向 standalone 时它一个写也挡不住。SharpDb 把用户写的连接串原样拿去用，只加上这一个关键字，**不检查、不校验、不改写**用户的连接串，也不要求用户部署成某种形态。

## 第 1 层因此更重要，不是可有可无

Guard 在本引擎上是**唯一**能拦住"读服务器文件/读另一台服务器"这一类的层——四条 rowset function（`OPENROWSET` / `OPENQUERY` / `OPENDATASOURCE` / `OPENXML`）进了 Provider 的禁用函数表（ADR 0013）。在别的引擎上引擎层还会兜一次底，在本引擎上不会。

## 元数据可见性也是账号的事

实测（同版本）：只持 `GRANT SELECT ON SCHEMA::dbo` 的登录，读 `sys.default_constraints.definition` 得到的是 **NULL**——约束名可见、定义文本不可见。补 `GRANT VIEW DEFINITION ON SCHEMA::dbo` 后才读得到。

这是**引擎的元数据按权限过滤**这一事实的后果，不是 SharpDb 对用户的要求：不给 `VIEW DEFINITION`，`list_tables` / `describe_table` 会少报东西，而客户端看到的是一份"看起来完整"的 schema。信息留在这里供 DBA 判断，语气是**告知后果**，不是下命令。

## 代价与边界

- `ApplicationIntent` 只加在 Provider 自己构造的连接串上；调用方传入的其他关键字（`Encrypt`、超时、`TrustServerCertificate` 等）原样保留。
- 驱动自 7.0 起默认 `Encrypt=Mandatory`，自签证书的服务器需要显式 `TrustServerCertificate=True`。**不建议**退到旧版驱动规避——那是把默认安全降级。
- 本 ADR 不改变 Guard 的许可清单：仍是 `SELECT` / `EXPLAIN` / `SHOW`，本引擎上 `EXPLAIN` 与 `SHOW` 由引擎自然报错。
- 不为本引擎引入任何新的只读机制（如强制走 AG 监听器）：第 2 层缺席在本仓库只**记录**，不修复。
