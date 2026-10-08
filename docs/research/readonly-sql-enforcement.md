# Read-only SQL enforcement in .NET (MySQL + SQLite)

Research for ticket `02-research-sql-readonly-validation` in `.scratch/sharpdb-mcp/`.

**Sources**: [SqlParserCS on NuGet](https://www.nuget.org/packages/SqlParserCS), [TylerBrinks/SqlParser-cs](https://github.com/TylerBrinks/SqlParser-cs), [bytebase/dbhub `allowed-keywords.ts`](https://github.com/bytebase/dbhub/blob/main/src/utils/allowed-keywords.ts), [benborla/mcp-server-mysql](https://github.com/benborla/mcp-server-mysql), [designcomputer/mysql_mcp_server](https://github.com/designcomputer/mysql_mcp_server), [Microsoft.Data.Sqlite connection strings](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/connection-strings), [SQLite `PRAGMA query_only`](https://www.sqlite.org/pragma.html#pragma_query_only).

## Approach 1 — full SQL parser: `SqlParserCS`

| Property | Value |
|---|---|
| NuGet id | `SqlParserCS` |
| Latest stable | `0.6.5` (2025-04-02); prerelease `0.6.6-release0000` targets net8.0 |
| Stable TFM | `net7.0` (usable from net8.0) |
| Licence | MIT |
| Dialects | Generic, ANSI, BigQuery, ClickHouse, DuckDB, Hive, **MsSql, MySQL, SQLite**, PostgreSQL, Redshift, Snowflake |
| Repo | `TylerBrinks/SqlParser-cs` (port of `sqlparser-rs`) |

It is the only mature .NET-native parser covering **both** MySQL and SQLite dialects. Alternatives are worse fits: `SqlParser.Druid` is prerelease/net10-only; ANTLR grammars require hand-integration; `Microsoft.SqlServer.TransactSql.ScriptDom` is T-SQL only.

Shape of the guard:

```csharp
var statements = new SqlQueryParser().Parse(sql, new MySqlDialect());
if (statements.Count != 1) return false;          // reject stacked statements
return statements[0] switch
{
    Statement.Select  => true,                     // includes WITH ... SELECT
    Statement.Explain => true,
    Statement.Show    => true,                     // MySQL
    // SQLite PRAGMA needs a setter check (reject `=` forms)
    _ => false,
};
```

`Statement` is an abstract record; concrete statements are nested records (`Statement.Select`, `Statement.Insert`, …) so C# pattern matching does the classification. A visitor is available for deeper walks.

Caveats even with a parser: `SELECT … INTO OUTFILE`, `EXPLAIN ANALYZE <DML>`, SQLite `PRAGMA` setters (`PRAGMA query_only = 0`), and escape-hatch functions (`LOAD_FILE(`, `GET_LOCK(`) need explicit extra checks.

## Approach 2 — keyword / prefix heuristics

A leading-keyword + semicolon check is **provably insufficient**. Demonstrated bypasses:

| Bypass | Example | Caught? |
|---|---|---|
| Comment-hidden keyword | `/**/ INSERT INTO t VALUES(1)` | ❌ |
| `WITH … INSERT/UPDATE/DELETE` | `WITH cte AS (…) INSERT INTO t SELECT * FROM cte` | ❌ |
| `EXPLAIN ANALYZE` + DML | `EXPLAIN ANALYZE INSERT INTO t …` | ❌ |
| `SELECT … INTO OUTFILE` | writes a file | ❌ |
| SQLite PRAGMA setter | `PRAGMA journal_mode=WAL` | ❌ |
| `REPLACE INTO` | | ❌ |
| MySQL `HANDLER` / `LOAD DATA` / `CALL` / `DO` / `SET` | | ❌ |
| Stacked statements | `SELECT 1; DROP TABLE users` | ✅ (semicolon check) |

`bytebase/dbhub` shows what a *careful* keyword classifier costs: strip comments and string literals, then check the leading keyword, then recursively check CTE bodies for DML, then check `SELECT INTO`, then `EXPLAIN ANALYZE`, then reject SQLite PRAGMA setters, then reject escape-hatch functions, then special-case `REPLACE INTO`. That is a lot of edge-case surface for a regex-grade guarantee.

## Approach 3 — database-level enforcement

### MySQL

| Mechanism | Guarantee | Portable from Dapper? |
|---|---|---|
| `START TRANSACTION READ ONLY` | transaction-scoped; writes rejected | ✅ via `DbCommand` before the query |
| `SET SESSION TRANSACTION READ ONLY` | session-scoped default | ✅ |
| Read-only user (`GRANT SELECT`) | **strongest** — server refuses writes regardless of client | ✅ just connect with that user |
| `read_only` / `super_read_only` server mode | server-wide, affects all clients | ⚠️ not appropriate for a shared instance |

### SQLite

| Mechanism | Guarantee | Available in Microsoft.Data.Sqlite? |
|---|---|---|
| `Mode=ReadOnly` in the connection string | **strongest** — opens via `SQLITE_OPEN_READONLY`; writes return `SQLITE_READONLY` | ✅ documented |
| `PRAGMA query_only = 1` | connection-scoped; blocks DML/DDL but CHECKPOINT/COMMIT still possible; not reflected in `sqlite3_db_readonly()` | ✅ via `DbCommand` |

## What real MCP DB servers do

| Project | Parsing | DB-level fallback | Strength |
|---|---|---|---|
| `bytebase/dbhub` | keyword classifier with comment/string stripping, CTE/`INTO`/`ANALYZE`/PRAGMA/escape-hatch checks | connector-level read-only | medium–strong |
| `benborla/mcp-server-mysql` | `node-sql-parser` AST + per-schema permission flags | `SET SESSION TRANSACTION READ ONLY` → `BEGIN` → query → `ROLLBACK` | strong |
| `designcomputer/mysql_mcp_server` | none (semicolon check only) | none — relies on DB permissions | weak |

## Recommendation for SharpDb v1

Defence in depth, three layers:

1. **Layer 1 — `SqlParserCS` AST guard (application)**: reject if statement count ≠ 1; allow only `Select` / `Explain` / `Show` (and SQLite `Pragma`, minus setters); reject `SELECT … INTO`; reject escape-hatch functions. This is the *fast-feedback* layer that gives the AI a clear "refused" message.
2. **Layer 2 — engine-level read-only (safety net)**: MySQL `SET SESSION TRANSACTION READ ONLY` around each query; SQLite `Mode=ReadOnly` (or `PRAGMA query_only`) on the connection. Catches anything Layer 1 misses.
3. **Layer 3 — least-privilege credentials (infrastructure)**: document that a MySQL `SELECT`-only user is the real boundary. Not enforced by code.

**Reject plain keyword heuristics** — the bypass list above is long and the maintenance cost never ends. **Do not rely on a parser alone** either; the engine-level net is cheap and closes the parser's gaps.
