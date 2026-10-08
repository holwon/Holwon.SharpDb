# Configuration, startup validation and distribution

Type: grilling
Status: open
Blocked by: 03

## Question

Settle how the server is **configured, validated at startup, and shipped**.

1. **Config surface** — confirm the final env-var names (`SHARPDB_TYPE`, `SHARPDB_CONNECTION_STRING`) and decide whether any per-run knobs are also configurable this way: default row limit, query timeout, max row limit. Should these be env vars, CLI args, or hard-coded defaults? Give each a name and a default.
2. **Startup validation** — the process starts, reads config, and something is wrong (missing `SHARPDB_TYPE`, unknown type, missing/blank connection string, unparsable connection string, connection refused). Decide, for each: does the server **fail fast** (exit non-zero, message on stderr) or **start anyway** and surface the error when a tool is called? What is the argument for each, given an MCP host will show the user a broken/quiet server?
3. **Connection check at startup** — do we eagerly open/verify a connection on boot (fail fast on bad credentials) or lazily on first tool call? Trade-off: boot-time latency & determinism vs a server that can start even if the DB is momentarily down.
4. **Distribution** — confirm the global-tool shape from ticket 03: package id, `ToolCommandName`, initial version, and the exact `mcp.json` snippet a user copies (for a MySQL config and for a SQLite config). Confirm README install instructions.
5. **Secrets** — the connection string arrives as an env var. Decide the stance on logging it (never), echoing it in errors (redact), and whether to support any alternative source (user-secrets / config file) in v1 or rule it out.

## Answer

_(unresolved)_
