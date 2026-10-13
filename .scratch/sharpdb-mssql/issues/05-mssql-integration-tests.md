# 05: mssql 测试项目：共享套件 + 引擎专属断言

**What to build:** 第五个引擎跑通两个共享契约套件，并断言它独有的真事。测试用 Testcontainers 起真实 SQL Server 容器，用**管理员账号播种**、用**无写权限的登录**供 Provider 连接 —— 共享断言跑在 ADR 描述的形态上（这是测试自己的取法，不是对用户的要求）。此票完成后，mssql 的测试覆盖完整。

**Blocked by:** 02 (共享套件必须先收缩，否则 mssql 跑不过), 04 (Provider 必须先存在)

**Status:** resolved

- [x] 新增测试项目，引用 Testcontainers 的 SQL Server 包 + 共享契约库 + Provider 项目
- [x] 测试基座用**钉住的镜像**起容器、播种共享 schema 的 T-SQL 版本（`ContractSchema` 加一份 mssql DDL）、Docker 缺席即 skip
- [x] 测试基座用管理员账号播种、用无写权限的登录（`SELECT` + `VIEW DEFINITION`）供 Provider 连接；交给 Provider 的连接串**不含**只读 intent（加它的是 Provider 的事）
- [x] 两个共享契约套件各有一个空子类，全部通过
- [x] 引擎专属断言：连接串带只读 intent；**账号无写权限时引擎拒写**（本引擎真正的边界；断言引擎行为，不是对用户的要求）；裸名与限定名的分界；`MS_Description` 注释；`IDENTITY` 自增；复合外键列配对顺序；索引唯一性与列序；`EXPLAIN`/`SHOW` 由引擎报错；`[bracket]` 标识符被 Guard 接受；rowset function 被 Guard 拒绝；超时被中断
- [x] 集合夹具：**一个容器服务整个集合**，不是每个测试类一个容器
- [x] 需要自有 schema 的测试用另一个数据库，不污染共享 schema（共享套件精确断言表集合，任何自有 schema 都会让别的测试翻红）

## Comments

### 2026-10-13 — 完成（`11f7df1`）

63 条测试全绿，含两个共享契约套件（**未作任何修改**）。镜像钉 `@sha256:4402d880dd4c…`（实测期间 `2022-latest` 从 CU 移了一次，浮动 tag 会让共享断言悄悄改测对象）。

**测试基座用两个账号**：`sa` 播种、`sharpdb_ro`（`SELECT` + `VIEW DEFINITION`）供 Provider 连接。第二个账号与这套授权是**测试自己的取法**，不是对用户的要求——spec 里就是这么定的。

一套容器服务整个集合；需要自有 schema 的测试用第二个库（与既有四引擎同构）。

**过程中实测出来的另外几条引擎事实**（都已落成断言或注释）：

- **`GRANT SELECT` 不够，默认值文本读回 NULL**（见 `01` 的 Comment）——补 `VIEW DEFINITION` 才行，已加断言 `Without_view_definition_a_columns_default_text_is_hidden`。
- **视图列继承基列的 `is_identity` 标志**：视图也报 `True`，所以只在对象是表时报自增（否那么测试会假绿）。
- **`DROP LOGIN IF EXISTS` 是语法错**（而 `DROP USER IF EXISTS` 合法）→ 必须写 `IF EXISTS (…) DROP LOGIN`。
- **不能在登录仍被登入时 `DROP LOGIN`**（`Could not drop login … currently logged in`），且驱动默认**连接池**会让已 dispose 的会话仍算登入 → 该测试的连接串要 `Pooling=false`。
- **一条语句一个命令**：登录/用户/授权拼成一个批会报 `Incorrect syntax near the keyword 'IF'`（这几个 IF 不是合法的批内语句），拆开即可。
- 实测 `0x` 是可用的零长二进制字面量（存进去 `DATALENGTH = 0`，不是 NULL），所以共享 DDL 能用它给 `beta` 行写空 blob。
- 实测视图列：`NOT NULL` 会从基表传播，**默认值不传播**（与 MySQL 传播两者不同）。
