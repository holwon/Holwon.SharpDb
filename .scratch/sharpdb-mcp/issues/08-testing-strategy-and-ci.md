# Testing strategy and CI

Type: grilling
Status: open
Blocked by: 07

## Question

Decide how SharpDb v1 is **tested and verified**, given two very different engines.

1. **Test layers** — what is unit-tested vs integration-tested? Candidates: SQL read-only enforcement (ticket 02) as pure unit tests with no DB; schema mapping as unit tests over captured provider output; provider behaviour as integration tests.
2. **SQLite integration** — in-memory (`:memory:` / shared cache) vs a temp file vs `Data Source=file:...?mode=memory&cache=shared`. Which gives reliable, isolated, parallel-safe tests? How is the schema seeded (raw DDL in a fixture)?
3. **MySQL integration** — Testcontainers (`Testcontainers.MySql`) with a real `mysql` image. Confirm: image tag to pin, container startup cost, whether tests must be serialised, and what happens on a machine/CI without Docker (skip via a trait? fail?).
4. **MCP-level tests** — do we test through the in-process transport from ticket 03 (a real `McpClient` calling real tools), and if so, how many tools/behaviours are covered that way vs at the service layer below?
5. **CI** — which workflows run which tests. Does CI run MySQL container tests on every push, or gated? Is there a `dotnet format` / analyzers gate? Confirm the test command(s) the repo will standardise on.
6. **Test project layout** — project name(s), whether one test project or one per provider, and how the MySQL-only tests are filtered when Docker is absent.

## Answer

_(unresolved)_
