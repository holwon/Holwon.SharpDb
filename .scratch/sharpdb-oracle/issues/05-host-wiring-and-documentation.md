# 05: Host 装配与文档

**What to build:** Oracle Provider 接入 Host，`SHARPDB__TYPE=oracle` 可启动。文档更新：`GLOSSARY.md` 中立定义、ADR 0014（Oracle layer 2）、ADR 0015（占位符翻译）、README 能力矩阵、NOTICE 许可声明。此票完成后，整个 feature 可交付。

**Blocked by:** 04 (测试必须先通过)

**Status:** resolved

- [x] `Program.Main` 的 `ProviderCatalog` 构造加 `new OracleDatabaseProvider()`
- [x] `SharpDb.csproj` 加 `ProjectReference` 到 `SharpDb.Provider.Oracle` 项目
- [x] `SharpDb.csproj` 的 `Description` / `PackageTags` 加 `oracle`
- [x] `GLOSSARY.md`：`Read-only enforcement` 改为引擎中立定义（"数据库引擎层的只读保证——即使 Guard 被绕过，写操作仍会被数据库自身拒绝。具体实现机制由各 Provider 决定，不在本词条内。"）
- [x] `docs/adr/0014-oracle-read-only-transactional-with-ddl-hole.md`：Oracle layer 2 是事务级（必须先 `BeginTransaction()`）、DDL 会隐式提交解除只读、layer 3（最小权限账号）在 Oracle 上从"建议"变"必需"、连接在事务中交付（随 dispose 回滚）
- [x] `docs/adr/0015-core-placeholder-translation.md`：Core 按方言翻译 `@` → `:`（仅 Oracle），复用 `ParameterPlaceholders` 词法扫描器，`t@dblink` 不被翻译
- [x] `README.md`：加 Oracle 到支持引擎列表（枚举式，不引入"four engines"数字短语），引入能力矩阵（`''` 即 NULL / `ON UPDATE` / Layer 2 机制 / Layer 2 防 DDL / Layer 3 必需性 / `EXPLAIN` / `SHOW` / 报错消息含对象名 / 无 FROM 的 SELECT）
- [x] 分发包根目录附 Oracle 许可协议副本（`Oracle.ManagedDataAccess.Core` 的许可文本）
- [x] `README.md` / `NOTICE` 加标准声明："This project uses Oracle Managed Data Access, licensed under Oracle Free Distribution Terms"
- [x] 进程级 smoke 测试通过（`SHARPDB__TYPE=oracle` 启动成功，fail-fast 在不可达时报错）

## Comments

**2026-10-12 — `8ffdafd`**

- `Program.cs` 的 `ProviderCatalog` 增 `new OracleDatabaseProvider()`；`SharpDb.csproj` 加 ProjectReference、`Description`/`PackageTags` 加 `oracle`。
- `GLOSSARY.md` 的 `Read-only enforcement` 改为引擎中立定义。
- 新增 ADR 0014（事务级只读 + DDL 洞 + layer 3 必需 + 连接在事务中交付）与 ADR 0015（Core 占位符翻译、复用扫描器、dblink 走拒绝路径）。
- `README.md`：引擎列表加 Oracle；新增 Oracle 配置块（含字符串拼写与只读模式说明）；"支持类型"表加 `oracle`（12c+）；**新增能力矩阵**明列四引擎差异；"三层只读"与"账号是真正边界"两节补 Oracle 的 DDL 洞与 layer 3 必需；故障表两条的预期类型列表加 `oracle`；许可节加 Oracle 声明。
- **许可**：`Oracle.ManagedDataAccess.Core` 的 `LICENSE.txt`（Oracle Free Distribution Terms）复制到仓库 `third-party/oracle-manageddataaccess-core-LICENSE.txt` 并随包打包（该许可要求分发时附副本）；新增 `NOTICE` 并打包。
- 进程级 smoke：`SharpDb.Tests/Startup` 增 `Startup_accepts_the_oracle_type_and_reaches_the_database` 与 `Startup_against_an_oracle_database_that_does_not_exist_reports_a_reason_without_credentials`，并更新未知类型列表断言含 `oracle`——与既有三引擎同形（真进程、证装配与 fail-fast）。
- **漂移修复**（`cd163c6`，来自两轴复审）：`SharpDb.csproj` 里仍写"MySQL、PostgreSQL and SQLite"的注释改掉；Core 与共享套件里"the three engines"计数字样改成不写数字。
