---
name: Matt Flow Gate
description: "MANDATORY workflow gate — no code changes allowed without completing the full Matt flow (grill → to-spec → to-tickets → implement). Prevents skipping phases or jumping straight to code."
applyTo: "**"
---

# Matt Flow Gate

## 1. The Rule: No Flow, No Code

NEVER modify, create, or delete code files unless the Matt flow has been completed end-to-end. This is a **hard gate**, not a suggestion.

The Matt flow is a **sequential pipeline** — each phase must be explicitly completed before the next begins:

```
/grilling → /to-spec → /to-tickets → /implement
```

## 2. Phase Definitions

| Phase | Skill | Who triggers | What happens |
|---|---|---|---|
| **Grill** | `/grilling` or `/grill-with-docs` | User starts the conversation | Agent interviews the user relentlessly, exploring the design tree. NO code changes. |
| **To Spec** | `/to-spec` | User explicitly requests | Agent synthesizes the grilling into a spec and publishes to issue tracker. |
| **To Tickets** | `/to-tickets` | User explicitly requests | Agent breaks the spec into tracer-bullet tickets with blocking edges. |
| **Implement** | `/implement` | User explicitly requests | Agent builds each ticket, driving `/tdd` internally, then `/code-review`. |

## 3. Hard Constraints

1. **During grill**: NEVER propose code changes, NEVER edit files, NEVER create branches. Only ask questions and explore the design tree.
2. **No self-generated issues**: Agent MUST NOT create issues, tickets, or specs on its own initiative. Only the user can trigger `/to-spec` and `/to-tickets`.
3. **No phase skipping**: If the user says "fix this bug" or "add this feature" without having completed grill → spec → tickets, the agent MUST refuse and redirect to the start of the flow.
4. **User controls phase transitions**: The agent waits for explicit user confirmation before moving to the next phase. Never assume the user is done grilling.
5. **Context hygiene**: Keep grill → to-spec → to-tickets in one unbroken context window. Each `/implement` starts fresh from the ticket.

## 4. What to Do When Asked to Change Code Prematurely

When the user requests code changes without completing the flow:

1. **Acknowledge** the request.
2. **State** that the Matt flow requires completing the earlier phases first.
3. **Offer** to start the grill phase if not yet done, or pick up from the current phase if partially complete.

Example response pattern:

> 这个修改需要先走Matt流程。我们现在处于 [当前阶段]，需要先完成 [下一阶段] 才能进入代码实现。要开始吗？

## 5. Exceptions

The ONLY exception is when the user **explicitly and directly** requests a code change with clear intent to bypass the flow (e.g., "just fix this typo", "revert my last commit"). In these cases, the change must be:

- Trivial (no design decisions needed)
- Reversible
- Confirmed by the user as intentional flow bypass
