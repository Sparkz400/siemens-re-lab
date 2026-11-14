---
tags: [tia, reverse-engineering, obsidian, index]
created: 2026-11-14
---

# Siemens RE Lab — Decompilation Vault

This vault tracks the A/B/C decompilation work:

- **A** — engineering data: blocks, tags, screens, hardware, safety, drives.
- **B** — compiled artifacts: classic MC7, S7-1500 plus MC7, HMI JS, drive code.
- **C** — TIA Portal and add-on application internals.

## Start here

- [[01 Scope and Definitions]]
- [[02 Current Findings]]
- [[05 Roadmap A B C]]
- [[06 Open Questions]]
- [[04 TIA IDE Inventory]]

## Experiment log

- [[2026-11-14 Plus Reader]]
- [[2026-11-14 Non-Optimized S7-1500]]
- [[2026-11-14 Daily]]

## Current status

| Layer | Status |
|---|---|
| A — engineering data | 85–95% via Openness + PLF/IDX/XML |
| B — classic MC7 | fully decoded on eight SCL FCs |
| B — S7-1500 plus MC7 | blocked at rest; runtime/memory routes open |
| C — TIA IDE | inventory started; selected assemblies decompiled locally |

The repo is `Sparkz400/siemens-re-lab`. The canonical evidence is in
`evidence/` and the surface status is in `docs/15-decompilation-status.md`.
