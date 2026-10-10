# 07: MySQL 适配器

**What to build:** 同一组四个工具在 MySQL 上全部工作。这一票**验证 ADR 0003 / 0004 的承诺**：新增一种数据库 = 新增一个 Provider 项目 + 装配处加一条分支，**Core 零改动**。若实现中发现必须改 Core，说明端口设计失败——那是本票要暴露的信号。

**Blocked by:** 06

**Status:** resolved

- [x] MySQL Provider 实现契约的全部五个成员，落在**同一个**深接口上
- [x] 只读连接通过**会话级只读**达成（连接建立后立即设置）
- [x] 内省从 `INFORMATION_SCHEMA` 取数，只看**当前库**（不跨 schema）
- [x] 列出表：含表与视图、含注释、按名称排序
- [x] 描述表：列（含注释与真正读到的自增标志）、主键、外键、索引；索引**排除主键**
- [x] 列出数据库：返回该连接可见的库
- [x] 执行查询：与 SQLite 同一套 `{columns, rows}` 形态、同一套行限 / 超时 / 错误透传 / 脱敏规则
- [x] Core 侧**没有**任何 MySQL 专属分支——Core 只认识契约
- [x] Provider 契约的**共享契约测试套件**跑在 MySQL 上，与 SQLite 跑的是同一份断言
- [x] 集成测试用容器化 MySQL，镜像钉在 8.0；容器共用以避免每个测试起一个
- [x] 无 Docker 时这些测试**跳过**而非失败

---

**验收说明**：断言与 SQLite 票相同的外部行为（"这个库里有哪些表 / 这张表长什么样 / 这条查询返回什么"），确保两个适配器对同一契约给出**语义一致**的应答。

---

## Comments

**2026-10-10 — 完成。** 十一条验收项逐条落地。`dotnet build SharpDb.slnx` 与 `dotnet test SharpDb.slnx` 全绿（**251** 例，失败 0；本票新增/改写集中在三个测试项目）。

**本票只改了测试，生产代码一行未动。** Provider 的实现早在票 04 / 05 落地（当时没有任何测试驱动它），本票补上的正是那份**共享契约套件**——外加容器化 MySQL 的真实覆盖。这本身就是 ADR 0003 / 0004 的验证：为 MySQL 写测试**没有触发任何 Core 改动**，连一个 Core 分支都没加。

### 交付物

- `tests/SharpDb.Provider.Contract/` —— **共享契约套件**：`IContractDatabase`（每引擎交出的东西）+ `ProviderContractSuite`（内省 22 例）+ `QueryContractSuite`（查询 16 例）+ `ContractSchema`（两份 DDL）+ `ToolPayload` / `QueryTool` / `ContractDatabaseExtensions`（三个助手）。**一个库、两份运行**
- `tests/SharpDb.Provider.Sqlite.Tests/` —— 契约的 SQLite 运行 + 只保留 SQLite 独有行为的分解文件（**53** 例）
- `tests/SharpDb.Provider.MySql.Tests/` —— 契约的 MySQL 运行 + MySQL 专属 11 例（**49** 例 = 22 + 16 + 11）
- `SharpDb.slnx` —— 收入契约项目

### 一、共享契约：**同一份断言跑两遍**，且逐字段实测过可比性

新建 `SharpDb.Provider.Contract`：一个普通库（非测试项目），里面是**抽象**的 `ProviderContractSuite` 与 `QueryContractSuite`，两个引擎的测试项目各继承一次、各交一个 `IContractDatabase`。断言全是外部行为（有哪些表、表长什么样、查询返回什么），不碰内省 SQL。

难点不在套件本身，而在**共享 DDL**：两份脚本要描述同一张表，而两个引擎的"同一张表"并不总是同一张表。**先写探针把两边跑通，再写测试**——50+ 条断言先在两库上实测全绿，才落进套件。设计里的一处关键发现只有实测才看得见：

| 共享 DDL 的写法 | 为什么必须这样写 |
|---|---|
| `users.org_id` **可空** | MySQL 拒绝在 `NOT NULL` 列上写 `ON UPDATE SET NULL`（实测：`Column 'org_id' cannot be NOT NULL: needed in a foreign key constraint 'fk_users_org' SET NULL`）。SQLite 照收。想两边表达同一个 `SetNull`，列就得可空 |
| 每个外键都有**显式索引覆盖** | InnoDB 会给没有索引的外键**自建**一个索引，SQLite 一个都不建 → 索引清单会因"引擎实现细节"而不同。显式覆盖后两边清单逐字相同 |
| **无默认值 / 无注释 / 无 bool / 无 decimal / 无 date** | 这些正是两库真实分歧所在（MySQL 把基表约束传播到视图列、默认值按引擎写法回读、decimal 带标度而 SQLite 交 double、date 交 DateTime 而 SQLite 交字符串）→ 全部留给**引擎专属**项目，共享套件不碰 |
| `ratio` MySQL 用 `DOUBLE`、SQLite 用 `REAL` | SQLite 把数值声明折叠成自己的存储类，两边都交 double，JSON 逐字节相同 |

### 二、无 Docker ⇒ **跳过而非失败**：不是纸面承诺，是在无 socket 容器里跑过的

xunit 2.9.3 **没有**动态跳过（`Assert.Skip` / `SkipException.ForSkip` 是 v3 才有的运行时基建——探针实测：会被报成 **FAILED**）。所以用了 `Xunit.SkippableFact` 1.5.85：fixture **只**捕获 `DockerUnavailableException` 置 `Available=false`，其余异常一律冒泡——**只有 Docker 缺席可以跳过，容器起不来/镜像坏了必须响亮地失败**，否则"静默不跑"就成了缺陷的藏身处。

验证方式：把测试项目挂进 `mcr.microsoft.com/dotnet/sdk:8.0` 容器、**不给 docker socket**：

```
Skipped! - Failed: 0, Passed: 0, Skipped: 49, Total: 49
```

49 例全部跳过、0 失败。**这条路在 Windows 本机测不出来**——探针实测：即使把 `DOCKER_HOST` 指向死掉的 `tcp://127.0.0.1:1`，Testcontainers 的端点解析链会**回退到本机 npipe 并成功**（`EnvironmentEndpointAuthenticationProvider` 不可用则继续尝试 `NpipeEndpointAuthenticationProvider`），所以"假端点"证明不了任何事。

### 三、容器共用与测试隔离：外加一条**架设陷阱**

`MySqlBuilder("mysql:8.0")` 建**一个**容器，`[CollectionDefinition("mysql")]` 让整个项目共用（实测启动 ~12 秒，每测一个起一个不可行）。

写第一版 MySQL 专属测试时踩到一条真实陷阱：**专属测试往共享库里建表，把共享套件的 `list_tables` 断言弄红了**（`Assert.Equal` 期望 7 张表，实际 9 张）。原因不是代码错，是**共享库的状态被别的测试改了**。解法：容器里另建 `sharpdb_spare` 库给专属测试用，另建 `sharpdb_empty` 空库给"空库"用例——**一个容器、三个库**，共享库只有共享 schema。这条正是"共享套件断言的是精确的表集合"这一设计选择的连带要求。

### 四、MySQL 专属的 11 例（共享套件**故意不覆盖**的部分）

| 用例 | 钉住的行为 |
|---|---|
| 表/列注释往返 | MySQL 存注释，SQLite 不存 |
| 无注释→缺省 | 空串归 null（不是 `""`） |
| 视图注释 | `TABLE_COMMENT` 对视图返回**字面量 `VIEW`**，不是注释——必须吃掉 |
| 声明类型原样 | `varchar(50)` / `decimal(10,2)` / `tinyint` |
| 默认值 | `CURRENT_TIMESTAMP` 原样回读 |
| `ON UPDATE CURRENT_TIMESTAMP` **不是**自增 | `EXTRA` 非空≠自增；按**整 token** 匹配而非子串 |
| 跨 schema 隔离 | 另建可见 schema + 授权 + 放表 → `list_tables` 不含它、`describe_table` 取不到它 |
| 会话只读 | `@@session.transaction_read_only` = 1 |
| `SHOW` 语句 | 过 Guard、经 Tool 执行成功 |
| MySQL 值类型→JSON | `TINYINT(1)`→`true`、`DECIMAL(10,2)`→`12.34`、`DATETIME(6)`→ISO 8601 带微秒、`BIGINT UNSIGNED` 最大值不丢精度、blob→base64 |
| 空库列空表 | `[]` |

### 五、实测订正与已知边界

**补全一处只测过半边的行为**：票 06 发现\"缺参数\"在 SQLite 上抛 `InvalidOperationException`（非 `DbException`），并为此加了专门的 catch 分支。MySQL 实测抛的是 **`MySqlConnector.MySqlException`**（`Parameter '@id' must be defined. To use this as a variable, set 'Allow User Variables=true'…`）——**是** `DbException`，由既有的另一个 catch 分支接住。两个引擎各走一条分支、都返回可读失败结果，这正是共享套件 `A_placeholder_with_no_parameter_is_a_readable_failure` 在两库上都断言的东西。

**已接受的边界**：

- **超时机制两库不同**：MySQL 在 `CommandTimeout` 到期时**打断语句**（1 秒 ≈ 1.03 秒返回），SQLite 忽略一切打断、只能等语句跑完再丢弃（1 秒 ≈ 2.3 秒返回）。共享套件只断言**调用方拿到同一条超时消息**，不断言用时——这正是 Core"两边都设"策略的实证。
- **视图列的 nullability / 默认值**：已知分歧（MySQL 传播基表约束、SQLite 不报），中立模型不统一——沿用票 04/05 的记载，共享套件不碰。
- **未测**：容器网络异常、`Allow User Variables=true` 连接串变体、MySQL 5.7（已 EOL）。

`bash tools/check-retired-identifiers.sh`（经 Git 的 bash）通过。本 Comment 未新增退役标识符。
