# Schema introspection SQL: MySQL vs SQLite

Research for ticket `01-research-schema-introspection-sql` in `.scratch/sharpdb-mcp/`.

**Sources**: MySQL Reference Manual ch. 28 (INFORMATION_SCHEMA); SQLite docs — [Schema Table](https://www.sqlite.org/schematab.html), [PRAGMA](https://www.sqlite.org/pragma.html), [Foreign Keys](https://www.sqlite.org/foreignkeys.html).

## MySQL — `INFORMATION_SCHEMA`

All queries filter on `TABLE_SCHEMA = DATABASE()` (or bind a target schema explicitly). `DATABASE()` returns the connection's default schema.

### Tables

```sql
SELECT TABLE_NAME, TABLE_TYPE, TABLE_COMMENT
FROM INFORMATION_SCHEMA.TABLES
WHERE TABLE_SCHEMA = DATABASE()
ORDER BY TABLE_NAME;
```

`TABLE_TYPE` is `'BASE TABLE'` or `'VIEW'`. `TABLE_COMMENT` is the `COMMENT` clause (or an engine description).

### Columns

```sql
SELECT COLUMN_NAME, ORDINAL_POSITION, COLUMN_TYPE, DATA_TYPE,
       IS_NULLABLE, COLUMN_DEFAULT, EXTRA, COLUMN_COMMENT
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = ?
ORDER BY ORDINAL_POSITION;
```

- `COLUMN_TYPE` = full declaration (`'bigint(20) unsigned'`); `DATA_TYPE` = base type (`'bigint'`).
- `IS_NULLABLE` is the **string** `'YES'`/`'NO'`, not a boolean.
- `EXTRA` carries `'auto_increment'`, `'on update CURRENT_TIMESTAMP'`, `'STORED GENERATED'`, `'VIRTUAL GENERATED'`.

### Primary keys

```sql
SELECT kcu.COLUMN_NAME, kcu.ORDINAL_POSITION, kcu.CONSTRAINT_NAME
FROM INFORMATION_SCHEMA.KEY_COLUMN_USAGE kcu
WHERE kcu.TABLE_SCHEMA = DATABASE() AND kcu.TABLE_NAME = ?
  AND kcu.CONSTRAINT_NAME = 'PRIMARY'
ORDER BY kcu.ORDINAL_POSITION;
```

The PK constraint is always named `'PRIMARY'`. `INFORMATION_SCHEMA.STATISTICS` (`SEQ_IN_INDEX`, `NON_UNIQUE`) is an equivalent source.

### Foreign keys

```sql
SELECT kcu.CONSTRAINT_NAME, kcu.COLUMN_NAME AS local_column,
       kcu.REFERENCED_TABLE_NAME, kcu.REFERENCED_COLUMN_NAME,
       kcu.ORDINAL_POSITION, rc.UPDATE_RULE, rc.DELETE_RULE
FROM INFORMATION_SCHEMA.KEY_COLUMN_USAGE kcu
JOIN INFORMATION_SCHEMA.REFERENTIAL_CONSTRAINTS rc
  ON kcu.CONSTRAINT_NAME = rc.CONSTRAINT_NAME
 AND kcu.TABLE_SCHEMA  = rc.CONSTRAINT_SCHEMA
WHERE kcu.TABLE_SCHEMA = DATABASE() AND kcu.TABLE_NAME = ?
  AND kcu.REFERENCED_TABLE_NAME IS NOT NULL
ORDER BY kcu.CONSTRAINT_NAME, kcu.ORDINAL_POSITION;
```

`UPDATE_RULE`/`DELETE_RULE` ∈ `CASCADE | SET NULL | SET DEFAULT | RESTRICT | NO ACTION`.

### Indexes

```sql
SELECT INDEX_NAME, NON_UNIQUE, SEQ_IN_INDEX, COLUMN_NAME, COLLATION, SUB_PART
FROM INFORMATION_SCHEMA.STATISTICS
WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = ?
ORDER BY INDEX_NAME, SEQ_IN_INDEX;
```

`NON_UNIQUE`: `0` = unique, `1` = non-unique. `COLLATION`: `'A'` asc, `'D'` desc, `NULL` unsorted. The PK index is named `'PRIMARY'`.

### Version notes

5.7 reads `TABLES` from `.frm` files; 8.0 reads the data dictionary (faster, same columns). 8.0 adds `STATISTICS.EXPRESSION` for functional indexes.

## SQLite — `sqlite_schema` + PRAGMAs

### Tables and views

```sql
SELECT type, name, sql
FROM sqlite_schema
WHERE type IN ('table', 'view') AND name NOT LIKE 'sqlite_%'
ORDER BY name;
```

`sqlite_master` is a historical alias that works everywhere. `sql` holds the normalised `CREATE` statement (NULL for implicit indexes from PK/UNIQUE constraints).

### Columns — `PRAGMA table_info('t')`

Returns `cid, name, type, notnull, dflt_value, pk`.

- **`pk` is NOT a boolean** — it is a 1-based ordinal within the primary key.
- `type` is the raw declared text (`'INTEGER'`, `'VARCHAR(255)'`), or empty string.
- `dflt_value` is the SQL expression text, or NULL.
- Omits generated and hidden columns.

`PRAGMA table_xinfo('t')` adds a `hidden` column: `0` normal, `1` hidden virtual-table, `2` stored generated, `3` dynamic generated.

### Foreign keys — `PRAGMA foreign_key_list('t')`

Returns `id, seq, table, from, to, on_update, on_delete, match`.

- `id` groups the columns of one FK constraint; `seq` orders them within it (0-based).
- `to` is NULL when the FK targets the parent's primary key implicitly.
- `match` exists but only `SIMPLE` is enforced.

### Indexes — `PRAGMA index_list('t')` + `PRAGMA index_info('i')`

`index_list` returns `seq, name, unique, origin, partial`. `origin` ∈ `'c'` (CREATE INDEX), `'u'` (UNIQUE constraint), `'pk'` (PRIMARY KEY).

`index_info` returns `seqno, cid, name` — `cid = -1` for rowid, `-2` for an expression; `name` NULL for those.

`index_xinfo` adds `desc` (1 = DESC), `coll`, and `key` (1 = key column, 0 = auxiliary).

### PRAGMA parameterisation

PRAGMAs **cannot** be parameterised — the object name must be a literal. Two safe forms:

- Quote with single quotes, doubling embedded quotes: `PRAGMA table_info('my''table')`.
- Or use the table-valued function form, which *does* accept a bind parameter:
  ```sql
  SELECT * FROM pragma_table_info(?);
  ```
  This is the preferred form for a provider — no string interpolation, no quoting bug class.

**Table-scoped PRAGMAs** (one call per table): `table_info`, `table_xinfo`, `foreign_key_list`, `index_list`. There is no database-scoped equivalent for schema introspection.

## Divergence — what the neutral model must mark optional

| Feature | MySQL | SQLite |
|---|---|---|
| Table / column comments | ✅ `TABLE_COMMENT`, `COLUMN_COMMENT` | ❌ none |
| Character set / collation | ✅ per-column `CHARACTER_SET_NAME`, `COLLATION_NAME` | ❌ single database-wide encoding |
| Base vs full type | ✅ `DATA_TYPE` + `COLUMN_TYPE` | ⚠️ one raw declared `type` string |
| Default value | ✅ string literal | ✅ SQL expression text |
| Auto-increment | ✅ `EXTRA = 'auto_increment'` | ⚠️ inferred: `pk = 1` + `type = 'INTEGER'` |
| Generated columns | ✅ `EXTRA` flags | ✅ `table_xinfo.hidden` 2/3 |
| Index sort order | ✅ `COLLATION` `'A'`/`'D'` | ✅ `index_xinfo.desc` 0/1 |
| Partial indexes | ❌ unsupported | ✅ `index_list.partial` |
| Expression indexes | ✅ 8.0 `STATISTICS.EXPRESSION` | ✅ `index_info.cid = -2` |
| FK actions | ✅ `UPDATE_RULE`/`DELETE_RULE` | ✅ `on_update`/`on_delete` |
| FK match type | ❌ | ⚠️ `match`, only `SIMPLE` enforced |
| Schema qualifier | ✅ `TABLE_SCHEMA` (per database) | ⚠️ `main`/`temp`/attached DB |
| Internal objects | ✅ filter by schema | ✅ filter `name NOT LIKE 'sqlite_%'` |

**Implication**: `Comment`, `Charset`, `Collation` and `FullDataType` are optional (nullable) on the neutral model. Auto-increment must be *computed* by the SQLite provider rather than read.
