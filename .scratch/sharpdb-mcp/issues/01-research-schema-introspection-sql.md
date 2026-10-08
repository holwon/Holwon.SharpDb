# Schema introspection SQL for MySQL and SQLite

Type: research
Status: resolved
Blocked by:

## Question

What are the **exact, portable-as-possible SQL queries** needed to introspect a database's schema on MySQL and on SQLite, such that both can be mapped onto a single provider-neutral schema model?

Cover, for each engine:

- **Tables** — names and (where available) type (table vs view), and a human-readable comment if the engine has one.
- **Columns** — name, ordinal position, data type (raw + a normalised form), nullability, default value, auto-increment/identity, and comment.
- **Primary keys** — column(s) and order.
- **Foreign keys** — name, local column(s), referenced table, referenced column(s), and on-delete/on-update actions.
- **Indexes** — name, uniqueness, and the columns in order (optional if it complicates the model).

For MySQL, the source is `INFORMATION_SCHEMA`; be explicit about which `TABLE_SCHEMA` value to filter on and how to handle a connection whose default schema is not the target. For SQLite, the sources are `sqlite_master` / `sqlite_schema` plus the `PRAGMA` family (`table_info`, `foreign_key_list`, `index_list`, `index_info`); be explicit about which PRAGMAs are table-scoped and therefore need one call per table.

Also report: which fields have **no equivalent** across the two engines (e.g. MySQL comments and character sets vs SQLite's lack of them), so the neutral model can mark them optional.

## Answer

Findings: [`docs/research/schema-introspection-sql.md`](../../../docs/research/schema-introspection-sql.md)

- **MySQL** is fully served by `INFORMATION_SCHEMA` filtered on `TABLE_SCHEMA = DATABASE()`: `TABLES`, `COLUMNS`, `KEY_COLUMN_USAGE` (+ `REFERENTIAL_CONSTRAINTS`) and `STATISTICS`. 5.7 and 8.0 differ only in storage, not columns (8.0 adds `STATISTICS.EXPRESSION`).
- **SQLite** is served by `sqlite_schema` for tables/views plus **table-scoped PRAGMAs** — `table_info`/`table_xinfo`, `foreign_key_list`, `index_list` + `index_info`/`index_xinfo`. Each needs one call per table. PRAGMAs cannot be parameterised; use the table-valued form `SELECT * FROM pragma_table_info(?)` to stay injection-safe.
- **Trap**: SQLite's `table_info.pk` is a 1-based **ordinal**, not a boolean.
- **Divergence**: comments, charsets/collations and a base/full type split exist only on MySQL; SQLite auto-increment must be *inferred* (`pk = 1` + `type = 'INTEGER'`), not read. The neutral schema model must make `Comment`, `Charset`, `Collation` and `FullDataType` optional.
