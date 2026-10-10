# Triage Labels

The skills speak in terms of five canonical triage roles. This file maps those roles to the actual label strings used in this repo's issue tracker.

| Label in mattpocock/skills | Label in our tracker | Meaning                                  |
| -------------------------- | -------------------- | ---------------------------------------- |
| `needs-triage`             | `needs-triage`       | Maintainer needs to evaluate this issue  |
| `needs-info`               | `needs-info`         | Waiting on reporter for more information |
| `ready-for-agent`          | `ready-for-agent`    | Fully specified, ready for an AFK agent  |
| `ready-for-human`          | `ready-for-human`    | Requires human implementation            |
| `wontfix`                  | `wontfix`            | Will not be actioned                     |

When a skill mentions a role (e.g. "apply the AFK-ready triage label"), use the corresponding label string from this table.

Edit the "Label in our tracker" column to match whatever vocabulary you actually use.

## Lifecycle values

A ticket's `Status:` line also carries values that are not triage roles — where the ticket has got to since it was triaged. These are the values both lifecycle sections of `issue-tracker.md` refer to:

| Value | Appears on | Meaning |
| ----- | ---------- | ------- |
| `ready-for-agent` | implementation ticket | Triaged and fully specified; the value an implementation ticket starts at |
| `claimed` | wayfinding child ticket | A worker has taken it |
| `resolved` | either kind | Wayfinding: the question is answered. Implementation: built, reviewed and committed |
