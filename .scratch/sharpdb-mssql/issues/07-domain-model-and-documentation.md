# 07: 领域模型与文档

**What to build:** 把本引擎"第 2 层缺席"这件事写进领域模型与决策记录，并把 README 更新到"每个引擎一套差异"的完整形态。

**Blocked by:** 05 (能力矩阵要写实测事实，不能写纸面推理)

**Status:** resolved

- [x] ADR 0016：SQL Server 没有引擎层只读；只读 intent 只在目标是 Always On 可读副本时成立、standalone 上是 no-op，因此**不等于** enforcement；本引擎上唯一挡写的是连接账号的权限 —— 写成**引擎的事实**而非对用户的要求，并明写 SharpDb 不检查、不校验、不改写连接串
- [x] ADR 0017：`list_tables` 报的名字集合 = 引擎自己解析得动的名字集合（默认 schema 裸名、其余限定名）
- [x] `GLOSSARY.md` 在"只读"分组新增 `Read-only intent` 词条，紧跟 `Read-only enforcement`，并写 `_Avoid_` 禁止用它指代 enforcement；**词条内不写连接串关键字**（GLOSSARY 不放实现细节）
- [x] ADR 0002 的第 2 层清单补 mssql 一行 + 说明（这一层在本引擎缺席）
- [x] ADR 0005 的 `[bracket]` 缺口措辞复核（对通用方言仍在、对本引擎已闭合）
- [x] README：引擎列表、env 示例、类型表（含版本下限）、**能力矩阵加一整列**、示例报错文案、三层只读叙述；补运维要点：自签证书需 `TrustServerCertificate=True`、账号需 `SELECT` + `VIEW DEFINITION`
- [x] 能力矩阵里"账号权限"那一行沿用**告知后果**的口气（不写 required/必需），与已改过的 Oracle 行一致；"账号是真正的边界"一节同理，写成事实而不是对用户的指示
- [x] 分发包的 `Description` 与 `PackageTags` 补上引擎名
- [x] **宽模式复扫"引擎计数"字样**（README、契约套件注释、分发包元数据），不留数字短语 —— 本仓库反复踩到的坑，且没有检查会发现
- [x] `bash tools/check-retired-identifiers.sh` 干净

## Comments

### 2026-10-13 — 完成（`fb80d52`）

两张新 ADR（0016 第 2 层缺席 / 0017 命名规则），`GLOSSARY.md` 新增 `Read-only intent`（词条内不写关键字，符合 GLOSSARY 不放实现细节的约定），ADR 0002 的第 2 层表补一行并说明"第 2 层可能不存在"第一次成为模型的一部分。

README 改动面：引擎列表、新 env 示例、类型表两行（`mssql` 行、版本下限理由）、能力矩阵**加一整列**、三层只读叙述补一段"第 2 层不是普适的"、"账号是真正的边界"补 SQL Server 一段（告知后果语气）、示例报错文案补新类型名、`TrustServerCertificate` 与 `VIEW DEFINITION` 两条运维要点。分发包 Description 与 PackageTags 补上引擎名。

**宽模式复扫**（spec 点名的已知漂移面）实测结果，漂了三处、均已修：

| 位置 | 原文 | 改为 |
|---|---|---|
| `tools/verify-installed-tool.ps1` | "one install serves MySQL and SQLite" | "every database type" |
| `tests/SharpDb.Provider.Sqlite.Tests/` 两处 | "asserted there once, for both" | "once for every engine" |
| `tests/SharpDb.Provider.PostgreSql.Tests/` 两处 | "the MySQL and SQLite projects run the same suite … Three runs of one suite" | "every engine's test project … One suite, run once per engine" |

另外把 `ProviderContractSuite` 类注释里"Each of MySQL, PostgreSQL, SQLite and Oracle has an engine layer"改成不枚举具名集合的写法（枚举会随新引擎过期）。`check-retired-identifiers.sh` 干净。

一处**新的实测事实**替掉了我原先的纸面推理：视图列的传播行为按引擎不同——MySQL 传播 `NOT NULL` 与默认值、SQL Server 只传播 `NOT NULL`、PostgreSQL 与 SQLite 都不传播。README 的矩阵行按实测重写。
