# Prototype: tool output shapes

Type: prototype
Status: resolved
Blocked by:

## Question

What should each tool's **output actually look like on the wire**, so we can react to it before committing the contract to the spec?

Build a throwaway prototype (a small C# file-based app or a scratch console snippet is fine — it does not need to be the real server) that prints the real payloads for:

1. `execute_query` — a result set of ~10 rows with mixed column types (int, string, nullable, datetime, decimal, blob/bytes), rendered as JSON objects. Show how NULL, `byte[]` and `DateTime` are represented, and how a `columns` + `rows` envelope compares to a plain array of objects.
2. `list_tables` — table names (plus type/comment if cheap).
3. `describe_table` — one table with columns, PK, FK, indexes.
4. The **error** shape — a rejected write statement, a SQL syntax error, and a timeout.

Then judge, with the payloads in hand:

- **Token cost** of plain JSON objects vs a `{columns, rows}` envelope vs a compact `[["col"],[val]]` array — measure actual character/token counts on the same data.
- **AI readability** — which shape is least ambiguous about types and nullability.
- Whether `UseStructuredContent` (from ticket 03) changes the answer.

Link the prototype source as an asset from this ticket; do not paste it into the body.

## Answer

**Prototype**: [`prototypes/04-tool-output-shapes/`](../prototypes/04-tool-output-shapes/) — run with `dotnet run`.

### Verdict

| Tool | Chosen format | Rationale |
|---|---|---|
| `execute_query` | **Format B: `{columns, rows}` envelope** | ~40–46% fewer tokens than array-of-objects at realistic row counts; native JSON null; type-preserving |
| `list_tables` | Array of `{name, type, comment?}` objects | Metadata, not tabular data — structured objects are the natural shape |
| `describe_table` | Single `{name, type, comment?, columns[], primaryKey, foreignKeys[], indexes[]}` object | Hierarchical metadata — one object per call |
| Errors | `{error: string}` | Single field, clear and parseable |

### Token cost comparison (execute_query, compact JSON)

| Rows | Format A (objects) | Format B ({columns, rows}) | Format C (compact array) |
|---|---|---|---|
| 10 | 1405 chars / ~352 tok | **850 chars / ~213 tok** | 885 chars / ~222 tok |
| 100 | 13777 chars / ~3445 tok | **7462 chars / ~1866 tok** | 8121 chars / ~2031 tok |

Format B wins decisively because column names are written once, not repeated per row. The savings grow with row count.

### Why not Format C (compact array)?

Format C uses a `"__NULL__"` string sentinel instead of native JSON null. This is ambiguous — the AI cannot distinguish a real string `"__NULL__"` from a missing value. It also loses type information (all values become string-or-native-mixed). Format B is equally compact but uses proper JSON null.

### Type representation decisions

| Type | JSON representation | Notes |
|---|---|---|
| `int` | JSON number | `1`, `42` |
| `string` | JSON string | `"hello"` |
| `NULL` | JSON `null` | Native null, not a sentinel |
| `datetime` | ISO 8601 string | `"2024-01-15T08:30:00Z"` |
| `decimal` | JSON number | `1250.50` — preserved by System.Text.Json |
| `byte[]` (blob) | Base64 string | `"iVBORw0KGgo="` |
| `bool` | JSON boolean | `true` / `false` |

### UseStructuredContent

**Does not change the shape decision.** `UseStructuredContent = true` sends the same JSON in both `Content` (text) and `StructuredContent` (raw object). It's a transport optimization, not a format choice. The `{columns, rows}` envelope works identically in both modes.

### Error shape

All errors use a single `{ "error": "message" }` envelope. Three categories observed:

1. **Guard rejection**: `"Statement rejected: INSERT is not allowed. Only SELECT, EXPLAIN and SHOW statements are permitted."`
2. **SQL syntax error**: `"SQL syntax error: near \"SELEC\": syntax error at line 1, column 1"`
3. **Timeout**: `"Query execution timed out after 30 seconds. Consider adding LIMIT or optimizing the query."`

The error messages should be actionable (tell the AI what to do differently) and never leak connection details.

### Fog graduated

- **Error surface shape** — decided: `{error: string}`, single envelope. Moves from "Not yet specified" to decided.
- **Large-result handling** — still open. Format B's token savings help, but pagination/truncation is a separate concern.
