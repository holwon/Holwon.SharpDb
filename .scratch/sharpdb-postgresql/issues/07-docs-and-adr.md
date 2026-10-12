# 07: 文档与 ADR 修订收尾

**What to build:**

把"支持 PostgreSQL"这件事对外说清楚、把这次的设计决定记进 ADR：README 增补 PostgreSQL 的配置示例与类型说明；包元数据从"两种"改为"三种"；安装验证脚本覆盖 `postgresql`；并修订三篇受影响的 ADR、新增一篇记录 Guard 与策略数据这一决定。

**Blocked by:** 04, 06

**Status:** resolved

- [x] README 增补：PostgreSQL 的 `mcp.json` 示例、`SHARPDB__TYPE` 取值含 `postgresql`、连接串为普通 Npgsql 串、声明支持 PostgreSQL 10+ 且"只在 16 上测过"。
- [x] 包元数据（描述与标签）写明三种数据库类型；包仍是**同一个**、版本号仍是一套。
- [x] 安装验证脚本覆盖 `postgresql`：类型被接受；`postgres` 仍是"未知类型"示例；期望类型串为三者。
- [x] **ADR 0002** 修订：第 2 层补上 PostgreSQL 的会话只读机制，并记录"MySQL 的 `SET SESSION TRANSACTION READ ONLY` 写法在 PostgreSQL 跨事务无效"这一实测坑。
- [x] **ADR 0003** 修订：契约新增"策略数据"成员；"Provider 是一个深接口"的结论保持不变。
- [x] **ADR 0005** 修订：结论修正后仍成立（方言不改变放行/拒绝的**分类**），但更正"方言不参与解析成败"，记录已知**误拒**缺口（`TABLESAMPLE` / `IS JSON` / `OVERLAPS` / `ORDER BY … USING` / `FOR NO KEY UPDATE` 等），并删除被策略数据成员取代的两句。
- [x] 新增 ADR：Guard 是 Core 的 DI 服务、Provider 贡献只读策略数据（方言 + 禁用函数）；记录"为何不做每 Provider 一个 Guard 子类"（许可清单的集中保证会降级为逐实现保证）。
- [x] 词汇守卫脚本通过：无被退休的标识符出现在解释处之外；`GLOSSARY.md` 用词一致。

## Comments

### 2026-10-12

**Commit:** `b6e3f72` — `docs: say what PostgreSQL support means, and record the decisions behind it`

（审阅后另有一次措辞修正：`0748abd`）

**改了什么**

- **README**：开头从"两种"改为三种；新增 PostgreSQL 的 `mcp.json` 示例；新增「What each type supports」小表（类型 / Driver / 备注，含 PG 10+ 与"只在 16 上测过"）；新增「What a description reports about a view」一节，把**视图列约束是否传播**这条真实分歧写成使用者能读到的事实（MySQL 传播，PG 与 SQLite 不传播）；配置参考表的 `SHARPDB__TYPE` 改为三值并补"忽略大小写与首尾空格"；错误表新增/更新两行，并点明 **`postgres` 不是别名**。
- **包元数据**：`Description` 与 `PackageTags` 从两种改三种（`postgresql` 入 tags）。**包仍是同一个**（`Holwon.SharpDb.Mcp`）、版本号仍是一套——ADR 0009 不变。
- **验证脚本**：新增 `-SkipPostgreSql` 开关与 PostgreSQL 引擎分支（起 `postgres:16` 容器、`pg_isready` 探活、灌 schema、建 SELECT-only 账号、用完清容器），新增 `tools/seed-verification-postgresql.sql`；`Test-StartupFailures` 的期望类型串改为三者。
  - 顺带修一处脚本注释与实际行为不符的措辞（原文写"-SkipMySql 是跳过 MySQL 的唯一方式"，现在有两个引擎，各有一个开关）。
- **ADR 0002**：第 2 层补第三种机制与那段坑。**特别写明 `psql -c` 会把多条语句放进同一事务块**，因此用它验证会话级只读会得到假结论——这是我自己差点被带偏的地方，值得留在文档里。
- **ADR 0003**：记录契约新增 `ReadOnlyPolicy` 成员，并把原论证里"这些部分从不独立变化"一句更正为"接口的成员不因引擎而分叉"（方言确实因引擎而异了，变的是**数据**不是**成员**）。
- **ADR 0005**：删掉被推翻的两句（"Provider 接口没有方言成员"、"Abstractions 不依赖 parser"——后者后半段仍真，但理由是策略数据而不是"没有方言成员"）；写明"方言不参与解析成败"**是错的**、方言唯一可观察的作用就是决定可解析面；列出六个已知误拒缺口并重申其性质是**误拒（损失功能）而非漏放（损失安全）**。
- **ADR 0013（新增）**：Guard 是 DI 服务、Provider 贡献策略**数据**；分界线是"策略只能加、不能改"；记录为何不做逐 Provider 的 Guard 子类（把许可清单的**集中保证**降级成**逐实现保证**，新增引擎就多一处可能漏放的地方，而漏放是不可恢复的）。
- **GLOSSARY**：`Read-only enforcement` 补三种机制；新增 **Policy** 条目（并 `_Avoid_` 掉"用 Policy 指代 Guard 算法本身"）；`Provider` 补"贡献自己的只读策略"；`Driver` 例子补 Npgsql。

**验证**

- 词汇守卫：`OK — no retired identifiers outside their explanation sites`（exit 0）。
- `GLOSSARY` 的 `_Avoid_` 逐词复核：本次改动里出现的 `Driver` / `Engine` / `Server` / `Filter` 全部是文档允许的那几种用法（`FILTER (WHERE …)` 是 SQL 子句、`engine` 是领域术语、`server` 指 MCP 协议角色）。
- 冻结的历史票据（`.scratch/sharpdb-mcp/`、`.scratch/sharpdb-v1/` 里的旧文案）**未动**——按仓库约定，它们是已交付工作的验收记录，不是漂移。
- **README 里引用的两条启动失败消息逐字实测过**（直接跑宿主抓 stderr）：
  - `sharpdb: SHARPDB__TYPE is required (expected "mysql" or "postgresql" or "sqlite").`
  - `sharpdb: SHARPDB__TYPE 'postgres' is unknown (expected "mysql" or "postgresql" or "sqlite").`
  后者同时证明 spec 的要求仍然成立：`postgres` 继续是"不认识的值"。
- `dotnet build SharpDb.slnx`：0 error。

### 2026-10-12（复审追加）

**Commit:** `见下方「复审发现的两个缺陷」` — 代码复审自查时发现本票的两处错误，均已修。

#### 缺陷一：验证脚本的种子失败检查是空的

`tools/verify-installed-tool.ps1` 的 PostgreSQL 分支照抄了 MySQL 的写法：跑种子脚本、检查 `$LASTEXITCODE`、非零则 `throw`。但**两个客户端的退出码语义不同**——实测：

| 客户端 | 坏 SQL 的退出码 | 正常 SQL 的退出码 |
|---|---|---|
| `mysql` | `1` | `0` |
| `psql` | **`0`**（不加标志时） | `0` |

`psql` 默认对每条失败语句在 stderr 上报错，**却仍退出 0**。所以那句 `throw "Seeding PostgreSQL failed"` 永远不会触发：一个在第一条 `CREATE TABLE` 上就失败的种子会留下半成品容器，而验证脚本会继续往下跑并"验证"错误的东西——正是本脚本文件头明确禁止的"静默不跑"。

**修法**：给该次 `psql` 调用加 `-v ON_ERROR_STOP=1`。实测确认：坏 SQL 现在 exit=3，且正常的种子脚本在该标志下仍然成功。修好后补做了**从未做过的端到端验证**——用种子脚本建出的 `sharpdb_ro` 账号跑真实 Provider，四个内省工具全部可用（`list_tables` 报出 3 个对象、`describe_table` 对表与视图均正确、`list_databases` 得到 `[app, postgres]` 且模板库已排除）。

#### 缺陷二：种子 SQL 里一条注释的理由是错的

`tools/seed-verification-postgresql.sql` 里原本写着：

> A new role does not inherit the right to resolve names in the schema, and without this the tool's own introspection would be refused…

并据此执行了一条 `GRANT USAGE ON SCHEMA public TO sharpdb_ro`。**实测推翻了这个理由**：PostgreSQL 16 的 `public` schema 自带默认 ACL（`\dn+ public` 显示 `=U/pg_database_owner`，即对 `PUBLIC` 授予 `USAGE`），所以新建的角色**本来就能**解析该 schema 下的名字，`has_schema_privilege('probe2','public','USAGE')` 在**没有**任何显式授权时就是 `t`。

那条 `GRANT` 是一次**没起作用的操作**，注释给出的解释也不成立。这比多一行更糟：读者将来把它删掉时会预期出问题，而问题不会来，于是要么白查一轮，要么开始怀疑整个文件的其它理由。

**修法**：删掉那条 `GRANT USAGE`，把注释改成记录实测事实——**不授权是刻意的**，因为默认 ACL 已经给了。

顺带记一个反直觉的旁证：把 `public` 的 `USAGE` 显式收回后再查，报的是 `relation "users" does not exist`，而不是权限错误。**权限不足在 PostgreSQL 上会伪装成"对象不存在"**，这会让排错方向偏掉。

#### 为什么两轴子代理都没抓到

两处都不在 diff 的"逻辑"里，而在**注释所声称的理由**与实际行为之间。两轴复核都按惯例把注释当作可靠线索读了过去。这与仓库既有教训同类：*引用一条理由前先验证那条理由*——只不过这次的引用者是我自己写的注释。
