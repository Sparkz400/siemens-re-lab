# Siemens RE Lab

Reverse-engineering lab for Siemens TIA Portal V21, PLCSIM Advanced V8 and WinCC Unified PC RT 21.0.
The repo documents four projects, the environment they run in, and the evidence collected so far.

> **Status: 2026-11-12.** Goal 3 has a working end-to-end MVP. Goals 1, 2 and 4 are planned;
> goal 4 is blocked on hardware/artifacts.

> **Disclaimer:** this is independent research on systems we own or are authorized to test.
> It is not affiliated with or endorsed by Siemens. Siemens, SIMATIC, TIA Portal, PLCSIM,
> WinCC and related marks belong to Siemens AG. No Siemens binaries, licenses, project
> archives, credentials or customer data are published here.

## The four goals

| # | Goal | End state | Status |
|---|---|---|---|
| 1 | **TIA project snapshot/diff** | CLI that snapshots a TIA project (archive + exported object/block/tag/screen metadata) and diffs two snapshots into a readable change report | Planned — TIA MCP already exposes `project_index`, `project_snapshot_capture`, `project_model` |
| 2 | **S7comm-plus client** | Read-only tag client that talks to an S7-1500/PLCSIM Advanced without TIA/WinCC; later a Pi/MQTT bridge | Planned — PLCSIM Advanced V8 installed; capture plan ready; no real PLC |
| 3 | **WinCC Unified extractor** | Login, browse tags, read/subscribe values and alarms, write CSV/SQLite/InfluxDB; Grafana on top; history later | **MVP working** — schema captured, auth fixed, 10 tags logged to CSV |
| 4 | **Panel backup unpacker** | Parse a `.psb` panel backup (container/compression/encryption), inspect with ImHex, diff two backups of the same panel | **Blocked** — no panel, no `.psb`; ProSave needs hardware |

The shared lab project is one TIA project containing a CPU 1511-1 PN and a WinCC Unified PC RT
station (`HMI_RT_1`) with tags and a screen, so goals 1–3 can be cross-checked against the same
target.

## Lab architecture

```text
Windows 11 VM
├─ TIA Portal V21 (STEP 7 Professional 2025, WinCC Unified Engineering)
├─ PLCSIM Advanced V8          (virtual S7-1500, no real PLC)
├─ WinCC Unified PC RT 21.0    (HMI_RT_1, GraphQL on http://localhost:4000/graphql)
├─ TIA MCP v0.9.0              (314 Openness tools, MCP server "tia")
└─ REA 6.8 / Ghidra / IDA Free / dnSpyEx / x64dbg / … (RE toolkit)

WSL2 Ubuntu 24.04 worker
└─ REA 6.9 + Ghidra 12.1.3 + Kuna 1.990 + jadx + apktool + rizin + TShark + Frida
```

## Working now (goal 3)

- `POST http://localhost:4000/graphql` — introspection enabled.
- Captured contract: **138 types, 8 queries, 11 mutations, 3 subscriptions**.
- Local user `Lab` (HMI Administrator), 20 internal tags `Tag01`…`Tag20` (Real).
- Verified: login → browse → read → write → read-back → 10-tag CSV watcher
  (170 rows over 8 s, values changing live).
- Probe: [`probe/unified_probe.py`](probe/unified_probe.py)
  (`browse`, `read`, `write`, `watch`).

## Repository layout

```text
docs/        goals, environment, lab project, WinCC GraphQL, TIA Openness, plans, roadmap
probe/       schema dump + tag read/write/watch CLI
analysis/    curated GraphQL schema summary (no vendor SDL committed)
examples/    sample CSV output
```

Docs index:

| Document | Contents |
|---|---|
| [docs/01-goals.md](docs/01-goals.md) | detailed end goals, scope, acceptance criteria |
| [docs/02-environment.md](docs/02-environment.md) | VM, installed products, licenses, RE toolkit, WSL worker |
| [docs/03-lab-project.md](docs/03-lab-project.md) | TIA project, compile, runtime package, Runtime Manager CLI |
| [docs/04-wincc-graphql.md](docs/04-wincc-graphql.md) | GraphQL contract, auth, operations, gotchas, probe |
| [docs/05-tia-openness.md](docs/05-tia-openness.md) | TIA MCP v0.9.0, patches, Openness allow-list, build steps |
| [docs/06-s7comm-plus.md](docs/06-s7comm-plus.md) | S7comm-plus plan, test rig, prior art, risks |
| [docs/07-panel-backups.md](docs/07-panel-backups.md) | `.psb` unpacker plan and current blocker |
| [docs/08-roadmap.md](docs/08-roadmap.md) | sequencing, milestones, next actions |
| [docs/09-artifacts.md](docs/09-artifacts.md) | local artifact paths and reproduction commands |
| [docs/10-legal-security.md](docs/10-legal-security.md) | authorization, trademarks, secret handling |

## Quick start (goal 3)

```powershell
$py = "$env:LOCALAPPDATA\Programs\Python\Python312\python.exe"
& $py probe/unified_probe.py browse --user Lab --password '...'
& $py probe/unified_probe.py read   --user Lab --password '...' --tag Tag01
& $py probe/unified_probe.py write  --user Lab --password '...' --tag Tag01 --value 42.5
& $py probe/unified_probe.py watch  --user Lab --password '...' --tags Tag01,Tag02 --duration 10 --out tags.csv
```

Credentials are not stored in this repo. Create a local UMC user in TIA and pass it at runtime.

## Key findings

- `WCCILgraphQLServer.exe` is a **Node.js SEA** with embedded `server.js`, authentication modules
  (`authCAPI.js`, `authSWAC.js`) and graphql-ws; PHMI schemas and WebPHMI query documents ship
  on disk.
- A headless-created Unified PC station does not compile until the encrypted-transfer password
  and start screen are configured in the editor (Openness gap **G-004**).
- The Runtime Manager only imports a **runtime `.zip`** from TIA *Download to file*; an ES
  `.zap21` is rejected.
- Importing a runtime package in the default keep mode disables UMC (`UMCData_disabled.json`).
  A full download with **`-overwriteUmc`** activates it.
- GraphQL tag names are fully qualified (`HMI_RT_1::Tag01`), `value` is an object
  (`value { value timestamp }`), and `browse` needs `nameFilters:["*"]`.

## License

Code and documentation: MIT (see [LICENSE](LICENSE)). Vendor names and any regenerated
schemas remain subject to Siemens' terms; no vendor binaries are distributed.
