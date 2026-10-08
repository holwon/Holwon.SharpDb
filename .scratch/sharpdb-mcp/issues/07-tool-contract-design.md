# Tool contract: names, signatures and behaviour

Type: grilling
Status: open
Blocked by: 02, 05

## Question

Fix the **exact contract of the four v1 tools**, in the vocabulary the AI client will see.

For each of `execute_query`, `list_tables`, `describe_table`, `list_databases`:

1. **Exact name and description text** — MCP tool names and the `[Description]` strings. Descriptions are the AI's only manual: what does a good one say?
2. **Parameters** — name, type, required/optional, default. Specifically: does `execute_query` take a `parameters` object (ticket-adjacent to 02's parameterisation stance)? Does it take an optional `maxRows`? Are table/schema names passed as strings, or is `describe_table` table-scoped only?
3. **Return shape** — lock the shape chosen in ticket 04 (JSON envelope vs array vs compact) and state it once, so all tools agree.
4. **Row limit & timeout semantics** — the default limit, the hard cap, what happens when the cap is hit (truncate + flag? error?), and how the timeout surfaces. These interact with ticket 02's read-only enforcement — state where each check runs.
5. **Annotations** — the `[McpServerTool(...)]` annotation values for each tool (per ticket 03), and the rationale for any that are *not* `ReadOnly=true`.
6. **Behaviour on rejection** — when a write statement is refused, what does the tool return: an error result, a text explanation, or a structured payload? Keep it consistent with ticket 04's error shape.
7. **Scope of `list_databases`** — what does it mean for SQLite (which has no server-level database list)? Is the tool conditional per provider, or does it always exist with a provider-defined answer?

## Answer

_(unresolved)_
