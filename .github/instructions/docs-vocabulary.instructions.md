---
name: Docs vocabulary and retired identifiers
description: "Guards GLOSSARY vocabulary and retired identifiers. Loads for markdown — where every drift found so far has occurred."
applyTo: "**/*.md"
---

# Docs vocabulary and retired identifiers

Two checks with two different mechanisms. One is mechanical and has a script; one is a judgement call and never will.

## 1. Retired identifiers — run the check

```bash
bash tools/check-retired-identifiers.sh
```

Also wired into CI. It fails when an identifier this project has **retired** (a renamed config key, a rejected interface shape) reappears outside the documents that explain the retirement. Run it before finishing any change that touches `.md`.

When a decision retires an identifier, add it to that script's table — the ADR that retires it becomes the allowlisted explanation site.

## 2. GLOSSARY vocabulary — judgement, not grep

`GLOSSARY.md` pairs each canonical term with an `_Avoid_` list. **Those lists are semantic guidance, not a banned-word list.** Several avoided words are correct in other senses:

| Word | Why it still appears legitimately |
|---|---|
| `Driver` | It is itself a **defined term** — the third-party ADO.NET package. `_Avoid_: Driver` means *don't use it to mean Provider*. |
| `Connector` | Inside package names: `MySqlConnector`. Never a violation. |
| `Engine` | How this domain says *database engine* / *engine-level read-only* (the name of enforcement layer 2). |
| `Server` | In "MCP server" it is the protocol's own role name, not a synonym for Core. |
| `Filter` | A common verb ("filter on `TABLE_SCHEMA`"). |

The rule is one sentence: **when a term has a canonical entry, use the canonical word for that concept.** A grep cannot tell "Provider" from "adapter"; you can.

Before finishing a change that touches docs, re-read the `_Avoid_` lists and search your change for each avoided word — then decide, per hit, whether the canonical term was meant.

## 3. Frozen Question sections are history

A tracker ticket's `## Question` records the **pre-decision** wording. A stale name inside a frozen Question is history, not drift: leave it, and let the `## Answer` carry the correction plus a pointer.

Rewriting the Question erases how the decision was reached — the very thing a later reader needs most.
