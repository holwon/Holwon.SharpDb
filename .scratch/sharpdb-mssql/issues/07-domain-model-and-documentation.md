# 07: 领域模型与文档

**What to build:** 把本引擎"第 2 层缺席"这件事写进领域模型与决策记录，并把 README 更新到"每个引擎一套差异"的完整形态。

**Blocked by:** 05 (能力矩阵要写实测事实，不能写纸面推理)

**Status:** ready-for-agent

- [ ] ADR 0016：SQL Server 没有引擎层只读；只读 intent 只在目标是 Always On 可读副本时成立、standalone 上是 no-op，因此**不等于** enforcement；本引擎上唯一挡写的是连接账号的权限 —— 写成**引擎的事实**而非对用户的要求，并明写 SharpDb 不检查、不校验、不改写连接串
- [ ] ADR 0017：`list_tables` 报的名字集合 = 引擎自己解析得动的名字集合（默认 schema 裸名、其余限定名）
- [ ] `GLOSSARY.md` 在"只读"分组新增 `Read-only intent` 词条，紧跟 `Read-only enforcement`，并写 `_Avoid_` 禁止用它指代 enforcement；**词条内不写连接串关键字**（GLOSSARY 不放实现细节）
- [ ] ADR 0002 的第 2 层清单补 mssql 一行 + 说明（这一层在本引擎缺席）
- [ ] ADR 0005 的 `[bracket]` 缺口措辞复核（对通用方言仍在、对本引擎已闭合）
- [ ] README：引擎列表、env 示例、类型表（含版本下限）、**能力矩阵加一整列**、示例报错文案、三层只读叙述；补运维要点：自签证书需 `TrustServerCertificate=True`、账号需 `SELECT` + `VIEW DEFINITION`
- [ ] 能力矩阵里"账号权限"那一行沿用**告知后果**的口气（不写 required/必需），与已改过的 Oracle 行一致；"账号是真正的边界"一节同理，写成事实而不是对用户的指示
- [ ] 分发包的 `Description` 与 `PackageTags` 补上引擎名
- [ ] **宽模式复扫"引擎计数"字样**（README、契约套件注释、分发包元数据），不留数字短语 —— 本仓库反复踩到的坑，且没有检查会发现
- [ ] `bash tools/check-retired-identifiers.sh` 干净
