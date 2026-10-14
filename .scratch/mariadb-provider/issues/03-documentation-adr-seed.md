# 03: 文档 + ADR + Seed 脚本

**What to build:** README 显示 MariaDB 支持、ADR 0018 记录决策、seed 脚本可以跑。可验证：文件存在且内容正确。

**Blocked by:** 01: MariaDB Provider 实现 + Host 装配

**Status:** ready-for-agent

- [ ] 更新 `README.md`，添加 MariaDB 支持声明和配置示例（`SHARPDB__TYPE=mariadb`）
- [ ] 写 ADR 0018：MariaDB is an independent Provider, not a MySQL variant
- [ ] 新建 `tools/seed-verification-mariadb.sql`，和 MySQL 版本相似
- [ ] 运行 `bash tools/check-retired-identifiers.sh` 通过
