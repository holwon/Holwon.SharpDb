# Provider abstraction: how Core stays database-agnostic

Type: grilling
Status: open
Blocked by: 01, 03

## Question

Design the **seam between the MCP core and a database provider**, so that adding a database type means writing a new provider project and nothing else. This is the load-bearing decision of the whole effort.

Work through, at minimum:

1. **The provider contract** — what interface(s) does a provider implement? Candidates: a single `IDatabaseProvider` (owns connection creation, SQL dialect quirks, introspection) vs several small seams (`IDbConnectionFactory` + `ISchemaIntrospector` + `ISqlDialect`). Which is deeper / simpler? What does each method actually take and return?
2. **The neutral schema model** — the POCOs a provider returns for tables/columns/PK/FK/indexes (informed by ticket 01's "no equivalent" list). Where do they live, and how much do they normalise (e.g. one `string DataType` vs a normalised enum + raw string)?
3. **Discovery & registration** — how does the running server pick a provider from `SHARPDB_TYPE`? Options: (a) explicit compile-time switch in Core referencing provider packages, (b) each provider ships a `[assembly:]`-level registration entry point that Core reflects over, (c) a tiny registry Core exposes that the host program populates. Which keeps Core free of driver references while staying debuggable and trim-safe?
4. **Driver dependencies** — confirm Core references no `MySqlConnector`/`Microsoft.Data.Sqlite`; each provider owns its driver. Does the *host* project (the global tool) reference all providers, and is that acceptable?
5. **What the core owns vs the provider** — read-only enforcement (ticket 02), row limits, timeouts, parameter handling, error shaping: which side of the seam do these sit on, and why?

Record the resulting interface sketch in the Answer (names and signatures are fine — this ticket resolves the *design*, not the code).

## Answer

_(unresolved)_
