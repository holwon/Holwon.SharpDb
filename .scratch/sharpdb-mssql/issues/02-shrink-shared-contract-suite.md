# 02: 共享契约套件收缩

**What to build:** 共享契约套件只保留每个引擎都能自己满足的断言。唯一直接验证引擎层只读（第 2 层）的那条断言 —— 打开连接后试着写一次并断言引擎拒绝 —— 下沉到 MySQL / PostgreSQL / SQLite / Oracle 四个引擎各自的测试项目。覆盖零丢失，只是换位置。此票完成后，第五个引擎才能加入共享套件。

**Blocked by:** None (can start immediately)

**Status:** resolved

- [x] `The_connection_a_provider_opens_refuses_a_write` 从共享套件移出
- [x] 该断言在 MySQL / PostgreSQL / SQLite / Oracle 四个引擎的测试项目里各有一份等价断言
- [x] 四个引擎的测试项目**仍然全绿**（回归验证：断言换了位置，行为没变）
- [x] 共享套件注释更新，说明"只保留每个引擎都能自己满足的断言"，并点明第 2 层不是每个引擎都有 —— 这是本票存在的理由，也是下个引擎加入时的依据

## Comments

### 2026-10-13 — 断言下沉完成（`7fc09b1`）

共享套件里的写拒绝断言移出。四引擎各自已有一份等价断言（PostgreSQL、Oracle 本来就有；MySQL、SQLite 本票新增）：

| 引擎 | 新家 | 断言形状 |
|---|---|---|
| MySQL | `MySqlSpecificTests.The_connection_the_provider_opens_refuses_a_write` | `ThrowsAnyAsync<DbException>` |
| PostgreSQL | `PostgreSqlSpecificTests.…_refuses_a_write_at_the_engine`（既有） | `PostgresException` + `"read-only transaction"` |
| SQLite | `SqliteDatabaseProviderTests.The_connection_the_provider_opens_refuses_a_write` | `ThrowsAnyAsync<DbException>` |
| Oracle | `OracleSpecificTests.The_read_only_transaction_refuses_a_write`（既有，三种 DML 形式） | `OracleException` + `ORA-01456` |

回归实测：四个引擎套件全绿（Sqlite 61、MySQL 57、PostgreSQL 88、Oracle 72），0 跳过。

共享套件的类级注释已改写，新增一段说明第 2 层不是端口属性而是引擎属性，并点名 SQL Server 是第一个没有它的引擎（ADR 0016）。
