# 05: Host 装配与文档

**What to build:** Oracle Provider 接入 Host，`SHARPDB__TYPE=oracle` 可启动。文档更新：`GLOSSARY.md` 中立定义、ADR 0014（Oracle layer 2）、ADR 0015（占位符翻译）、README 能力矩阵、NOTICE 许可声明。此票完成后，整个 feature 可交付。

**Blocked by:** 04 (测试必须先通过)

**Status:** ready-for-agent

- [ ] `Program.Main` 的 `ProviderCatalog` 构造加 `new OracleDatabaseProvider()`
- [ ] `SharpDb.csproj` 加 `ProjectReference` 到 `SharpDb.Provider.Oracle` 项目
- [ ] `SharpDb.csproj` 的 `Description` / `PackageTags` 加 `oracle`
- [ ] `GLOSSARY.md`：`Read-only enforcement` 改为引擎中立定义（"数据库引擎层的只读保证——即使 Guard 被绕过，写操作仍会被数据库自身拒绝。具体实现机制由各 Provider 决定，不在本词条内。"）
- [ ] `docs/adr/0014-oracle-read-only-transactional-with-ddl-hole.md`：Oracle layer 2 是事务级（必须先 `BeginTransaction()`）、DDL 会隐式提交解除只读、layer 3（最小权限账号）在 Oracle 上从"建议"变"必需"、连接在事务中交付（随 dispose 回滚）
- [ ] `docs/adr/0015-core-placeholder-translation.md`：Core 按方言翻译 `@` → `:`（仅 Oracle），复用 `ParameterPlaceholders` 词法扫描器，`t@dblink` 不被翻译
- [ ] `README.md`：加 Oracle 到支持引擎列表（枚举式，不引入"four engines"数字短语），引入能力矩阵（`''` 即 NULL / `ON UPDATE` / Layer 2 机制 / Layer 2 防 DDL / Layer 3 必需性 / `EXPLAIN` / `SHOW` / 报错消息含对象名 / 无 FROM 的 SELECT）
- [ ] 分发包根目录附 Oracle 许可协议副本（`Oracle.ManagedDataAccess.Core` 的许可文本）
- [ ] `README.md` / `NOTICE` 加标准声明："This project uses Oracle Managed Data Access, licensed under Oracle Free Distribution Terms"
- [ ] 进程级 smoke 测试通过（`SHARPDB__TYPE=oracle` 启动成功，fail-fast 在不可达时报错）
