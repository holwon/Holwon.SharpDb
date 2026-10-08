# Read-only SQL enforcement options in .NET

Type: research
Status: resolved
Blocked by:

## Question

What are the viable ways, in a .NET 8 app using Dapper over `DbConnection`, to **enforce that a user-supplied SQL statement is read-only** before it is executed — and how do they compare on correctness, dependency weight and maintenance?

Evaluate at least these approaches against real MySQL and SQLite dialects:

1. **Full SQL parser** — e.g. `SqlParserCS` (the .NET port of sqlparser-rs) or similar. Report: NuGet id, latest version, licence, dialect coverage (does it parse MySQL *and* SQLite?), and how one inspects the resulting AST to assert "single statement, and it is a SELECT / SHOW / PRAGMA / EXPLAIN / WITH…SELECT".
2. **Keyword / prefix heuristics** — trimming and matching a leading keyword, rejecting `;`-separated multi-statements and known write keywords. Report known bypasses (comments, `WITH … INSERT`, `EXPLAIN` variants, stacked statements, `INTO OUTFILE`).
3. **Database-level enforcement** — read-only transactions / read-only sessions (MySQL `SET TRANSACTION READ ONLY`, `START TRANSACTION READ ONLY`) and read-only credentials. Report what each engine actually guarantees and whether Dapper can open a connection in that mode portably.
4. **Any hybrid** used by real MCP DB servers (e.g. bytebase/dbhub, benborla/mcp-server-mysql, designcomputer/mysql_mcp_server) — what do they actually do?

Deliver a recommendation table: approach → catches what → misses what → cost. State clearly which single approach (or combination) gives the best correctness-per-effort for a v1 read-only server.

## Answer

Findings: [`docs/research/readonly-sql-enforcement.md`](../../../docs/research/readonly-sql-enforcement.md)

Recommendation is **three layers**, not one:

1. **`SqlParserCS` (MIT, `0.6.5`, covers both MySQL and SQLite dialects — the only mature .NET parser that does)**. Parse, require exactly one statement, and allow only `Select`/`Explain`/`Show` (plus SQLite `Pragma` minus setters). This is the layer that gives the AI a clear refusal.
2. **Engine-level read-only as a safety net** — MySQL `SET SESSION TRANSACTION READ ONLY`, SQLite `Mode=ReadOnly` on the connection string. Cheap, and closes whatever the parser misses.
3. **Least-privilege credentials** as the real boundary — documented, not code-enforced.

Rejected: **plain keyword heuristics** — the documented bypass list (comment-hidden keywords, `WITH … INSERT`, `EXPLAIN ANALYZE` + DML, `SELECT … INTO OUTFILE`, SQLite PRAGMA setters, `REPLACE INTO`, `HANDLER`/`LOAD DATA`/`CALL`) is long and open-ended.

Also learned: `bytebase/dbhub` (careful keyword classifier) and `benborla/mcp-server-mysql` (AST + read-only transaction) both layer defences; `designcomputer/mysql_mcp_server` does neither and relies solely on DB permissions.
