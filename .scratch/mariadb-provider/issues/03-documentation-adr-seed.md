# 03: 文档 + ADR + Seed 脚本

**What to build:** README 显示 MariaDB 支持、ADR 0018 记录决策、seed 脚本可以跑。可验证：文件存在且内容正确。

**Blocked by:** 01: MariaDB Provider 实现 + Host 装配

**Status:** resolved

- [x] 更新 `README.md`，添加 MariaDB 支持声明和配置示例（`SHARPDB__TYPE=mariadb`）
- [x] 写 ADR 0018：MariaDB is an independent Provider, not a MySQL variant
- [x] 新建 `tools/seed-verification-mariadb.sql`，和 MySQL 版本相似
- [x] 运行 `bash tools/check-retired-identifiers.sh` 通过

## Comments

**2026-10-15 · resolved · commit `1bd88d5` + `d1c5d3b` + `0c5bf85`**

- README：支持声明加上 MariaDB；新增 `SHARPDB__TYPE=mariadb` 配置示例；Driver 表加 `mariadb` 行；引擎差异矩阵加 MariaDB 列（外键 `ON UPDATE` 支持、引擎级只读 = session、`EXPLAIN` 返回计划、`SHOW` 可用、错误消息含对象名、无 `FROM` 的 `SELECT` 可用）；启动错误消息示例更新为含 `"mariadb"` 的 type 列表。
- ADR 0018：**按实测重写**。原稿把"只读语句不同"当作独立的首要理由，被实测推翻（两个引擎都接受两种写法，差异在作用域）；改以"内省目录会分化"（实测 `INFORMATION_SCHEMA.COLUMNS` 已双向分化：MariaDB 多 3 个时态表列、MySQL 多 `SRS_ID`）与"独立演进"为首要理由，并把两次被推翻的预期记录在案。首版 ADR 中"MariaDB 10.1+ 才支持 `SET TRANSACTION READ ONLY`"的前提随之一并作废。
- `tools/seed-verification-mariadb.sql`：与 MySQL 版对称，仅 `CREATE DATABASE IF NOT EXISTS` / `CREATE USER IF NOT EXISTS` 两处幂等化差异。
- `bash tools/check-retired-identifiers.sh` → 退出码 0，无违规。

