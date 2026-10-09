# Testing strategy and CI

Type: grilling
Status: resolved
Blocked by: 07

## Question

Decide how SharpDb v1 is **tested and verified**, given two very different engines.

1. **Test layers** — what is unit-tested vs integration-tested? Candidates: SQL read-only enforcement (ticket 02) as pure unit tests with no DB; schema mapping as unit tests over captured provider output; provider behaviour as integration tests.
2. **SQLite integration** — in-memory (`:memory:` / shared cache) vs a temp file vs `Data Source=file:...?mode=memory&cache=shared`. Which gives reliable, isolated, parallel-safe tests? How is the schema seeded (raw DDL in a fixture)?
3. **MySQL integration** — Testcontainers (`Testcontainers.MySql`) with a real `mysql` image. Confirm: image tag to pin, container startup cost, whether tests must be serialised, and what happens on a machine/CI without Docker (skip via a trait? fail?).
4. **MCP-level tests** — do we test through the in-process transport from ticket 03 (a real `McpClient` calling real tools), and if so, how many tools/behaviours are covered that way vs at the service layer below?
5. **CI** — which workflows run which tests. Does CI run MySQL container tests on every push, or gated? Is there a `dotnet format` / analyzers gate? Confirm the test command(s) the repo will standardise on.
6. **Test project layout** — project name(s), whether one test project or one per provider, and how the MySQL-only tests are filtered when Docker is absent.

## Answer

### 1. 测试分层

| 层 | 测什么 | 在哪 | 需要 DB？ |
|---|---|---|---|
| **单元** | Guard（AST 只读检查）、`SharpDbOptions` 校验、行限截断逻辑、错误成形与脱敏 | `SharpDb.Core.Tests` | ❌ |
| **Provider 集成** | 内省 SQL → Schema model 映射、查询执行、只读强制 | `SharpDb.Provider.{Sqlite,MySql}.Tests` | ✅ |
| **MCP 端到端** | 真实 `McpClient` 打真实 server | `SharpDb.Tests` | ✅（SQLite 足够） |

**Guard 全部是纯单元测试**——`SqlParserCS` 不需要数据库，把 ticket 02 的绕过清单（注释隐藏关键字、`WITH … INSERT`、`EXPLAIN ANALYZE` + DML、`SELECT … INTO OUTFILE`、SQLite PRAGMA setter、`REPLACE INTO`、堆叠语句）逐个变成测试用例。

### 2. SQLite 集成：命名共享缓存内存库（ABP 模式）

连接串形如 `Data Source=SharpDbTest_{Guid};Mode=Memory;Cache=Shared;Pooling=False`，fixture 持有**一条保活连接**（keeper）保证库存活。

**为什么需要 keeper**：内存库"在最后一条连接关闭时被删除"。我们的 Provider 是"每次调用现开现关"，没有 keeper 的话库在第一次调用后就消失了。

**关键：传连接字符串，不传连接实例**。每个测试开自己的连接，keeper 只负责保活。这与 ticket 05 的"现开现关"天然吻合。

**fixture 两步走**：先以**可写**连接 seed DDL（Provider 的连接是 `Mode=ReadOnly`，不能建表），再让 Provider 以只读打开。每个测试一个独立库名（含 GUID），天然并行安全。

### 3. SQLite 的只读强制：残余风险 + 免文件补救

**已确认的事实**：SQLite **引擎层面忽略** in-memory 数据库的 read/write flags。`SQLITE_OPEN_READONLY` 对内存库无效——Microsoft 官方实验确认，EF Core issue [#15118](https://github.com/dotnet/efcore/issues/15118) 以 *closed as not planned* 结案。且 Microsoft.Data.Sqlite 源码里 `Mode=ReadOnly` 与 `Mode=Memory` 是**互斥分支**，写不出"既内存又只读"的连接串。

**后果**：ADR 0002 三层防御的第 2 层在 SQLite 侧（`Mode=ReadOnly`）**不会被集成测试覆盖**。已接受此风险——第 1 层 Guard 完全可测，第 3 层凭据才是真边界。

**补救（不碰文件、不碰数据库）**：加一个断言，验证 Provider 产出的连接串里 `Mode == ReadOnly`（用 `SqliteConnectionStringBuilder` 解析）。纯字符串测试，把"Provider 有没有做只读增补"这个契约钉住。

### 4. MySQL 集成：Testcontainers

用 `Testcontainers.MySql`，镜像钉 **`mysql:8.0`**（ticket 01 的内省研究覆盖 8.0；5.7 已 EOL）。不用 `latest`。

**为何值得这个依赖**：Testcontainers 是**仅测试项目**的依赖，**永不进发布包**。发布物 `Holwon.SharpDb.Mcp` 的重量完全不受影响。代价只在测试项目侧（需要 Docker + 依赖树变重），换来 ticket 01 那些 `INFORMATION_SCHEMA` join 的真实覆盖。

**测试串行化**：容器启动成本高，MySQL 测试用 xUnit 的 collection fixture 共用一个容器，避免每测试起一个。

### 5. MySQL / Docker 缺失时：skip

没有 Docker 时 **skip** MySQL 测试，不 fail。本地开发者不必为了跑测试装 Docker。

**代价（已知并接受）**：CI 里若 Docker 不可用，MySQL 测试会**静默不跑**。缓解：CI 用 GitHub Actions（runner 自带 Docker），所以正常路径下 MySQL 测试**会真跑**。

**隔离靠项目布局**（见第 6 点）——MySQL 测试全部锁在 `SharpDb.Provider.MySql.Tests`，skip 范围天然收窄。

### 6. 测试项目布局：镜像生产层级

测试项目的层级**与生产项目一一对应**（ABP 风格 / DDD）：

| 测试项目 | 镜像的生产项目 |
|---|---|
| `SharpDb.Core.Tests` | `SharpDb.Core` |
| `SharpDb.Provider.Sqlite.Tests` | `SharpDb.Provider.Sqlite` |
| `SharpDb.Provider.MySql.Tests` | `SharpDb.Provider.MySql` |
| `SharpDb.Tests` | `SharpDb`（exe） |

`SharpDb.Abstractions` **无测试项目**——纯接口 + record，没有行为可测。

目录内按关注点分组：`Guard/`、`Introspection/`、`Execution/`、`Mcp/`。

**理由**：与 ticket 05 的项目拓扑同构，看生产结构就知道测试在哪；且 MySQL 的 skip 天然限定在一个项目内。

### 7. MCP 端到端测试：薄 smoke

用 ticket 03 定的 in-process `McpClient` over pipes，打真实工具。覆盖**恰好这些**：

- 工具列表 = 4 个（证明注册正确）
- 四个工具的 annotations 正确（`ReadOnly=true, Destructive=false, Idempotent=true, OpenWorld=false`）
- 每个工具至少一次真实调用成功
- 一条拒绝路径返回 `IsError = true`（证明 ticket 07 的错误通路）

**深度行为测试留在服务层**——端到端慢且脆，它的价值在"整条链路能通"，不在穷举边界。

### 8. 测试框架与命令

- **xUnit**（.NET 8 生态默认，与 ticket 03 的 in-process 测试风格契合）
- 统一命令：**`dotnet test SharpDb.slnx`**（显式路径）

### 9. 解决方案文件：`.slnx`

用 **`.slnx`**（XML 格式），放仓库根，收 5 个生产项目 + 4 个测试项目。

**事实依据**：`.slnx` 自 **.NET SDK 9.0.200** 起被 CLI 支持（`dotnet build` / `dotnet test` 直接接受路径）；`.slnx` 与目标框架**正交**——`net8.0` 项目可放 `.slnx`，只需 SDK ≥ 9.0.200。

**两个已确认的坑（我们规避）**：
1. `.sln` 与 `.slnx` **共存会歧义**（必须显式指定路径）→ 我们只用 `.slnx`，无此问题。
2. `.slnx` **未被 SDK 默认忽略**（[dotnet/sdk#48728](https://github.com/dotnet/sdk/issues/48728)，open）→ 别把 `.slnx` 与 `.csproj` 放同一目录；`.slnx` 放仓库根。

`.vscode/settings.json` 设 `"dotnet.defaultSolution": "SharpDb.slnx"`，让 VS Code 正确加载。

### 10. CI：GitHub Actions 单 job

一个 job，顺序执行：

1. `actions/checkout`
2. `actions/setup-dotnet`（SDK ≥ 9.0.200，`.slnx` 所需）
3. `dotnet build SharpDb.slnx`
4. `dotnet test SharpDb.slnx`

GitHub Actions runner 自带 Docker → Testcontainers 的 MySQL 测试在 CI 里真跑。

**v1 不加格式化门禁**：有报告称 `dotnet format` 对 `.slnx` 不工作（来源为博客评论，**未权威确认**）。不把未经证实的工具放进门禁——否则 CI 红了却不知原因。实施时实测 `dotnet format SharpDb.slnx`；能吃 `.slnx` 再加回来。

**v1 不设 analyzers 硬门禁**：保持 .NET 默认严重级别。

### 11. 术语说明

"CI 结构 / 形态"是本会话的口语，指 **workflow 的 job 组织方式**，非项目领域概念。**不进 `GLOSSARY.md`**——glossary 只收项目独有的领域概念（Provider、Core、Host、Guard……），不收通用编程概念（CI、job、workflow、keeper connection）。

