# 03: Core 认得 mssql 方言，Guard 拒 rowset function

**What to build:** Guard 认得 SQL Server 的只读 SQL：`TOP n` 与 `[bracket]` 标识符被接受；四个 rowset function（`OPENROWSET` / `OPENQUERY` / `OPENDATASOURCE` / `OPENXML`）被拒并给出清晰理由。另外四个引擎的行为逐字节不变。此票完成后，mssql 的 Policy 才能声明一个真实存在的方言值。

**Blocked by:** 01 (rowset function 能否被遍历器看见、以及解析器的 MS SQL 方言的实际行为，都由实测决定)

**Status:** ready-for-agent

- [ ] `SqlDialect` 新增 mssql 值；`ReadOnlyGuard` 的方言映射指向解析器的 MS SQL 方言（锁 `SqlParserCS 0.6.5`，不升预发布版）
- [ ] 未知方言值仍**响亮失败**，不回落到没人选的方言
- [ ] Guard 在 mssql 策略下接受 `TOP n` 与 `[bracket]` 标识符（有断言）
- [ ] Guard 拒绝四个 rowset function，拒绝理由**点名是哪个函数**（有断言）
- [ ] 若实测证明遍历器看不见 `FROM` 里的 rowset function，本票含 Core 的那处补丁，且补丁有断言
- [ ] 另外四个引擎的 Guard 行为不变（既有测试全绿）
- [ ] 复核既有"方括号标识符是可接受缺口"的断言与 ADR 0005 的措辞：缺口对通用方言**仍在**，对 mssql **已闭合** —— 不是改断言，是改它解释的对象
