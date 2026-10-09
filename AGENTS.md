## Agent skills

### Issue tracker

Issues and specs live as local markdown files under `.scratch/<feature>/`. See `docs/agents/issue-tracker.md`.

One feature per directory, so a **design** effort and its **implementation** effort are separate `.scratch/` folders and both number from `01`. Only the folder holding a `spec.md` is implementation work; a folder that also holds `map.md` is a wayfinder design record — read it, do not implement from it.

### Triage labels

The five canonical triage roles use their default label strings (`needs-triage`, `needs-info`, `ready-for-agent`, `ready-for-human`, `wontfix`). See `docs/agents/triage-labels.md`.

### Domain docs

Single-context layout: one `GLOSSARY.md` and `docs/adr/` at the repo root. See `docs/agents/domain.md`.
