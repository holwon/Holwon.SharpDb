# 03: Oracle Provider 项目

**What to build:** 一个完整的 Oracle Provider 实现 `IDatabaseProvider` 全部成员，驱动用 `Oracle.ManagedDataAccess.Core`。Layer 2 只读采用事务级机制（`BeginTransaction` → `SET TRANSACTION READ ONLY` → `BindByName = true`）。此票完成后，Provider 可被实例化并贡献 Policy，但尚未接入 Host。

**Blocked by:** 01 (Core 方言 + 占位符翻译必须先就位，否则 Oracle 上带参数查询跑不了)

**Status:** resolved

- [x] 新增 `SharpDb.Provider.Oracle` 项目，引用 `Oracle.ManagedDataAccess.Core`（版本锁定在 `Directory.Packages.props`，当前最新稳定 23.26.301）与 `SharpDb.Abstractions`
- [x] `OracleDatabaseProvider` 实现 `IDatabaseProvider` 全部 6 个成员
- [x] `Type` 返回 `"oracle"`（大小写不敏感匹配，无别名）
- [x] `OracleReadOnlyPolicy` 贡献 `SqlDialect.Oracle` + 空 `DeniedFunctions` 初版
- [x] `CreateReadOnlyConnectionAsync` 固定时序：`new OracleConnection` → `OpenAsync` → `BeginTransaction` → `SET TRANSACTION READ ONLY` → `BindByName = true` → 返回连接（事务永不提交，随 `DisposeAsync` 回滚）
- [x] `ListTablesAsync` 用 `ALL_TABLES UNION ALL ALL_VIEWS`（视图不在 `ALL_TABLES`，实测 0 行）
- [x] `DescribeTableAsync` 用 `ALL_TAB_COLUMNS`（已排除隐藏列）；`DATA_DEFAULT` 是 `LONG` 列，**必须直接读**，不能放进 `NVL()`/任何函数（`ORA-00932`）；主键用 `ALL_CONSTRAINTS`（`CONSTRAINT_TYPE = 'P'`）+ `ALL_CONS_COLUMNS`（按 `POSITION` 排序）；外键用 `ALL_CONSTRAINTS`（`CONSTRAINT_TYPE = 'R'`）的 `R_OWNER`/`R_CONSTRAINT_NAME` 反查父键列；`DELETE_RULE` 映射到 `ForeignKeyAction`；Oracle 无 `ON UPDATE`，`OnUpdate` 一律报 `NoAction`；索引用 `ALL_INDEXES` + `ALL_IND_COLUMNS`（按 `COLUMN_POSITION` 排序），排除主键索引；自增识别用 `ALL_TAB_COLUMNS.IDENTITY_COLUMN = 'YES'`（12c+）
- [x] `ListDatabasesAsync` 返回当前用户可见的 distinct owner 列表（`SELECT DISTINCT owner FROM all_tables UNION SELECT DISTINCT owner FROM all_views ORDER BY 1`），并包含连接自身的 schema
- [x] 共享 Oracle DDL（`ContractSchema.OracleDdl`）给所有标识符加双引号写小写（`"users"`），确保字典回的就是小写
- [x] FK 只声明 `ON DELETE`（无 `ON UPDATE`）；空串用 `NULL` 替代（Oracle 存 `''` 也是 NULL）；`readings.payload` 用 `RAW(10)` + `HEXTORAW('0102')`
- [x] 单元测试：Provider 可实例化、Policy 形状正确、Type 匹配 `"oracle"`

## Comments

**2026-10-12 — `cbe1f59`（后续 `b341145` 修四处实测缺陷）**

- 项目、Policy、Provider 六成员全部就位；在 `Directory.Packages.props` 锁 `Oracle.ManagedDataAccess.Core` 23.26.301 与 `Testcontainers.Oracle` 4.16.0；`.slnx` 记入。
- 只读时序按票实现：`BeginTransaction()` → `SET TRANSACTION READ ONLY` → `BindByName = true`，事务不提交。事务对象用 Driver 的**同步** `connection.BeginTransaction()`（返回 `OracleTransaction`）——异步版返回基类 `DbTransaction`，而 `OracleCommand.Transaction` 要 `OracleTransaction`，编译不过。
- **几处与票面实现的偏离，均由真库实测强制（`b341145`）：**
  - **`ListTablesAsync` 改用单读 `ALL_TAB_COMMENTS`**，不是票写的 `ALL_TABLES UNION ALL ALL_VIEWS`。实测 `ALL_TABLES`/`ALL_VIEWS` **都没有 comment 列**（`ORA-00904`），票面的 union 无法带注释；`ALL_TAB_COMMENTS` 一次给出名、类型（`TABLE_TYPE`）与注释。
  - **`BindByName` 显式设在每个命令上**（`CommandFor`）：同一语句里 `:name` 出现多次时位置绑定直接 `ORA-00936`；单次出现则静默错值。光设连接属性不够稳。
  - **`InitialLONGFetchSize = -1`**：`DATA_DEFAULT` 是 `LONG` 列，ODP.NET 默认 fetch size 为 0 会把它读成空——票只说"不能放进函数"，漏了"必须开 fetch size"。
  - **bind 变量名 `:table_name` 而非 `:table`**：`TABLE` 是保留字，`:table` 直接 `ORA-01745`。
- 标识符大小写：**共享 DDL 改写为不加引号**（票面原写"加双引号写小写"）。原因见票 02 评论——共享查询套件用不加引号的表名，引号化 DDL 会让 `FROM readings` 报 `ORA-00942`；改为 Provider 把全大写的字典名报成小写（`NormalizedName`）。两者不可兼得，选了后者。
- 单元测试在 `OracleReadOnlyPolicyTests`（17 例），只覆盖不需要容器的部分；Provider 行为断言见票 04。
