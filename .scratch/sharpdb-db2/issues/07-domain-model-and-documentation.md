# 07: 领域模型与文档

**What to build:** 新增一张 ADR 记录 Db2 第 2 层缺席这个引擎事实（与 SQL Server 的 ADR 0016 同形但更极端——连 read-only intent 都没有）；README 能力矩阵加 Db2 列、env 示例、示例报错文案加新类型名。

**Blocked by:** 04 (最终语义确定后)

**Status:** ready-for-agent

- [ ] 新增 ADR（Db2 没有引擎层只读）：记录 `SET TRANSACTION READ ONLY` 不存在、`FOR READ ONLY` 是游标提示、连接串无可加的关键字；引 ADR 0016 的模型（`Read-only intent` vs `Read-only enforcement`），并记录 Db2 上**连 intent 都不存在**，第 2 层**完全缺席**；语气是**告知后果**而非下命令（SharpDb 把用户写的连接串原样拿去用，不检查、不校验、不改写）
- [ ] README 能力矩阵加 Db2 一列，逐条写明与其它引擎的语义差异（大写折叠、`FETCH FIRST`、`SYSCAT`、无第 2 层）
- [ ] README 明确 Db2 无引擎层只读，唯一挡在写前面的是连接账号权限
- [ ] README 补 Db2 的 env 示例；示例报错文案加 `db2` 类型名
- [ ] `GLOSSARY.md` 复核：若 Db2 引入新领域术语则加，否则不动（不要把通用编程概念塞进 glossary）
- [ ] 全仓扫一遍 "N 个引擎" 式措辞（README、契约套件注释、seed 脚本），改成宽泛词或一次性全量更正

## Comments
