# Prototype: tool output shapes

Type: prototype
Status: claimed
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

_(unresolved)_
