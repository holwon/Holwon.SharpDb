# 只读由三层共同保证，而不是单点检查

只读不是一次检查，而是三层叠加：应用层的 Guard（用 SqlParserCS 解析，要求恰好一条语句且为 SELECT / SHOW / EXPLAIN）、数据库引擎层的只读模式（MySQL 的 `SET SESSION TRANSACTION READ ONLY`、SQLite 连接串的 `Mode=ReadOnly`、PostgreSQL 的 `SET SESSION CHARACTERISTICS AS TRANSACTION READ ONLY`）、以及最小权限的数据库账号。

不只用一层的原因：关键字前缀检查存在已证实的绕过路径（注释隐藏关键字、`WITH … INSERT`、`EXPLAIN ANALYZE` 后接 DML、`SELECT … INTO`、SQLite `PRAGMA` 赋值等），单靠它不可靠；而只靠引擎层虽然最安全，却无法向 AI 给出清晰的拒绝理由。因此保留解析器作为快速反馈层，用引擎层兜底，账号权限作为最终边界。

**绕过的确切范围需要一处更正。** 上面的清单最初是从 Rust crate `sqlparser-rs` 的覆盖面推断的，但本次交付时逐条实测了 C# 移植版（SqlParserCS 0.6.5），发现 `REPLACE INTO`、`LOAD DATA`、`HANDLER`、`LOCK TABLES`、`DO` 在这份移植版里是**语法错**——解析器在策略运行之前就拒了它们。清单里真正需要**策略**处理的只有注释隐藏关键字、`WITH … INSERT`、`EXPLAIN ANALYZE` 后接 DML、`SELECT … INTO` 与 `PRAGMA` 赋值。这不削弱本决策：清单越短，恰恰越说明「关键字前缀检查」可以在其中任何一条上失手。

**第 1 层只守客户的 SQL，不守 Provider 的内省 SQL。** 这一点值得写明，因为它反直觉且容易被「顺手修好」：`PRAGMA` 不在允许清单里（见 ADR 0005），而 SQLite 的内省天然要用 PRAGMA。若把 Provider 的内省 SQL 也送进 Guard，`list_tables` 与 `describe_table` 会**整体瘫掉**。

不走 Guard 不是偷懒——守卫的对象是**不可信的输入**，而内省 SQL 是 Provider 自己写的常量字符串，没有注入面。实测也证明它能被引擎层独立挡住：在 `Mode=ReadOnly` 的连接上，`PRAGMA journal_mode = WAL`、`PRAGMA user_version = 42`、`INSERT` 全部被引擎拒绝。

## 第 2 层 2026-10 修订：SQL Server，以及第一个**缺席**的第 2 层

新增 SQL Server Provider 时发现：本引擎**没有**引擎层只读可设，因此第 2 层在这条链上是**空的**。

| 引擎 | 第 2 层的机制 | 性质 |
|---|---|---|
| MySQL | `SET SESSION TRANSACTION READ ONLY` | enforcement |
| SQLite | 连接串 `Mode=ReadOnly` | enforcement |
| PostgreSQL | `SET SESSION CHARACTERISTICS AS TRANSACTION READ ONLY` | enforcement |
| Oracle | 只读事务（挡 DML，不挡 DDL） | enforcement（有已知缺口） |
| **SQL Server** | **无** —— `ApplicationIntent=ReadOnly` 只是**意图** | **intent，不是 enforcement** |

`SET TRANSACTION ISOLATION LEVEL` 只有五个隔离级别、没有 READ ONLY；`ApplicationIntent=ReadOnly` 是 Always On 的只读**路由**开关，在可读副本上是硬边界，在 standalone 实例上一次写也挡不住——实测：带该关键字的连接上 `CREATE TABLE` 成功。

这不削弱三层模型，而是把它补全：模型原本假设第 2 层总是存在。现在由 `GLOSSARY.md` 的 `Read-only intent` 词条承载"第 2 层可能缺席"，`0016` 记录本引擎的具体事实与后果（唯一挡写的是账号权限），`README` 的能力矩阵加一整列说明各引擎的差异。

## 第 2 层 2026-10 修订：PostgreSQL，以及一个只在它上面存在的坑

新增 PostgreSQL Provider 时补入第三种会话只读机制，同时记一个**实测**出来的陷阱——它足以让一个看起来正确的实现形同虚设。

**可用的写法**是 `SET SESSION CHARACTERISTICS AS TRANSACTION READ ONLY`。`CHARACTERISTICS` 把它设成**会话**的属性，因此跨事务有效。实测（`postgres:16.15`）：设置之后再发一条 `INSERT`，得到 `ERROR: cannot execute INSERT in a read-only transaction`。

**看起来对、实际无效的写法**是 `SET SESSION TRANSACTION READ ONLY`——也就是 MySQL Provider 发的那句。PostgreSQL **接受**它，但它只作用于**当前事务块**。驱动默认 autocommit，发这句话的那个事务在语句返回时就结束了，于是：

- 语句本身成功（有时只给一句 warning）；
- 下一条 `INSERT` 照常成功；
- 一个用了这句的 Provider 会被所有"发一条写语句试试"的测试判为只读，而它根本不是。

这是在 `psql -c` 下特别容易误判的场景：`-c` 把多条语句放进同一个事务块，于是那句 `SET SESSION TRANSACTION READ ONLY` 看起来"生效了"。**验证会话级只读必须跨语句、跨事务**，`psql -c` 做不到这件事。

**第 2 层在 PostgreSQL 上不是万能的。** 实测：只读会话**挡不住**读取服务器本机文件的函数——`pg_read_file` 读回了服务器自己的 30KB 配置文件，`pg_ls_dir` 列出了目录。它们不写数据，所以不触发只读保护，但它们确实越出了数据库的范围。这是第 1 层必须补洞的直接证据（见 ADR 0013：Provider 贡献禁用函数表）。只读会话确实**拒绝**的写入包括 `INSERT` / `UPDATE` / `DELETE` / `CREATE TABLE`（含 `CREATE TEMP TABLE`）/ `DROP` / `TRUNCATE`。`nextval` / `setval` 也在拒绝之列，因此有意不进禁用表——引擎兜底已经覆盖。
