# Siemens RE Lab

Reverse-engineering lab for Siemens TIA Portal V21, PLCSIM Advanced V8 and WinCC Unified PC RT 21.0.
The repo documents four projects, the environment they run in, and the evidence collected so far.

> **Status: 2026-11-13.** Goal 3's live tag logger passed a clean 1 h soak (35,910 samples,
> 0 errors, max interval 1.387 s); tag/alarm history and alarm controls work, while the direct
> `activeAlarms` subscription remains broken and a history-polling feed is used instead. Goal 1
> has a capture/compare CLI with a native Python diff, validated live for tag, block and HMI
> screen-item changes. Goals 2 and 4 are planned; goal 4 is blocked on hardware/artifacts.

> **MC7 note (2026-11-14).** Classic S7-300 MC7 is readable and was verified on
> eight SCL functions; S7-1500 plus code remains encrypted in `MC7PlusData`, and
> the PLCSIM V21 public API does not expose block download/upload. See
> [docs/13-mc7-codegen.md](docs/13-mc7-codegen.md).

> **Harness note (2026-11-14).** The 30-idea `tools/tia-project` harness is now
> included: `platform run` 29/29, `platform realpair` 8/8, and the classic
> compiler gate `distinct=8`, `realCodegen=true`. See
> [docs/14-tia-project-harness.md](docs/14-tia-project-harness.md) and
> [docs/15-decompilation-status.md](docs/15-decompilation-status.md).

> **Disclaimer:** this is independent research on systems we own or are authorized to test.
> It is not affiliated with or endorsed by Siemens. Siemens, SIMATIC, TIA Portal, PLCSIM,
> WinCC and related marks belong to Siemens AG. No Siemens binaries, licenses, project
> archives, credentials or customer data are published here.

## The four goals

| # | Goal | End state | Status |
|---|---|---|---|
| 1 | **TIA project snapshot/diff** | CLI that snapshots a TIA project (archive + exported object/block/tag/screen metadata) and diffs two snapshots into a readable change report | **CLI validated live** — TIA MCP capture + native Python diff; unchanged = 0, one tag = 1, one block edit = 1, one HMI item = 1 HMI change |
| 2 | **S7comm-plus client** | Read-only tag client that talks to an S7-1500/PLCSIM Advanced without TIA/WinCC; later a Pi/MQTT bridge | Planned — PLCSIM Advanced V8 installed; capture plan ready; no real PLC |
| 3 | **WinCC Unified extractor** | Login, browse tags, read/subscribe values and alarms, write CSV/SQLite/InfluxDB; Grafana on top; history later | **Live logger + history working**; tag/alarm history and alarm controls verified; `activeAlarms` subscription blocked |
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
- `ws://localhost:4000/graphql` — graphql-ws (`graphql-transport-ws`), token in
  `connection_init.payload.Authorization`.
- Captured contract: **138 types, 8 queries, 11 mutations, 3 subscriptions**.
- Local user `Lab` (HMI Administrator), 20 internal tags `Tag01`…`Tag20` (Real).
- Verified: login → browse → read → write → read-back → 10-tag CSV watcher
  (170 rows over 8 s, values changing live).
- Live logger: [`probe/live_logger.py`](probe/live_logger.py) — `tagValues` subscription,
  exponential backoff, re-login, SQLite + CSV, session/gap records.
- Demo writer: [`probe/demo_writer.py`](probe/demo_writer.py) — deterministic 1 Hz sine values
  for soak tests.
- **1 h soak passed:** 35,910 samples (10 tags × 3,590), 0 errors, 1 session, 0 reconnects,
  CSV = SQLite, max interval 1.387 s; final values matched the API 10/10.
- Alarm client: [`probe/unified_alarms.py`](probe/unified_alarms.py) — ack/reset/enable/disable/
  shelve/unshelve; `activeAlarms` query (blocked on this runtime).
- Alarm history feed: [`probe/alarm_history_watch.py`](probe/alarm_history_watch.py) — polls
  `loggedAlarms` and stores transitions in SQLite/CSV.
- History client: [`probe/unified_history.py`](probe/unified_history.py) — `loggedTagValues` and
  `loggedAlarms`; both verified live.
- Probe: [`probe/unified_probe.py`](probe/unified_probe.py)
  (`browse`, `read`, `write`, `watch`).
- **Current M2 limitation:** the direct `activeAlarms` query/subscription still returns
  `INTERNAL_SERVER_ERROR`. Tag history, alarm history, acknowledge/reset and shelve/unshelve work.
  Fixture details and evidence are in [docs/11-alarms-history.md](docs/11-alarms-history.md).

## Working now (goal 1)

- `probe/tia_snapshot.py` captures a TIA MCP `project-review.v1` snapshot into
  `snapshot.json` + `review.json` + `hmi-model.json`, with the `.ap21` archive SHA-256 and a
  project-folder manifest SHA-256.
- `compare` is pure Python over `review.json` and the HMI model (no TIA, no MCP Host) and writes
  `compare.json` + `report.md`; it exits non-zero on project or HMI changes.
- `probe/tia_mcp.py` is the stdio MCP client (newline-delimited JSON-RPC), also usable standalone.
- Unit tests cover the MCP client, native compare, HMI model compare, report rendering and
  archive/folder hashing.
- Live acceptance: unchanged double capture = 0 changes; one PLC tag = 1 change; one block edit
  = 1 change (`interfaceChanged`, `documentChanged`); one HMI screen item (`HmiRectangle`) = 0
  project changes + 1 HMI model change. The HMI model supplement captured 134 typed rows over
  2 pages (`coverage.complete=true`) and 135 after the item. Volatile metadata timestamps are
  ignored; the project-folder hash changed while `.ap21` stayed the same.

## Repository layout

```text
docs/        goals, environment, lab project, WinCC GraphQL, TIA Openness, plans, roadmap
probe/       schema dump, polling probe, live logger, alarm/history clients, MC7/Openness/PLCSIM probes, tests
tools/       tia-project 30-idea harness, classic/plus MC7 hosts
evidence/    sanitized JSON summaries from the platform, compiler and real-pair runs
obsidian/    Obsidian vault: A/B/C notes, experiment log, TIA inventory
analysis/    curated GraphQL schema summary (no vendor SDL committed)
examples/    sample CSV output
```

Docs index:

| Document | Contents |
|---|---|
| [docs/01-goals.md](docs/01-goals.md) | detailed end goals, scope, acceptance criteria |
| [docs/02-environment.md](docs/02-environment.md) | VM, installed products, licenses, RE toolkit, WSL worker |
| [docs/03-lab-project.md](docs/03-lab-project.md) | TIA project, compile, runtime package, Runtime Manager CLI |
| [docs/04-wincc-graphql.md](docs/04-wincc-graphql.md) | GraphQL contract, auth, operations, graphql-ws, live logger |
| [docs/05-tia-openness.md](docs/05-tia-openness.md) | TIA MCP v0.9.0, patches, Openness allow-list, build steps |
| [docs/06-s7comm-plus.md](docs/06-s7comm-plus.md) | S7comm-plus plan, test rig, prior art, risks |
| [docs/07-panel-backups.md](docs/07-panel-backups.md) | `.psb` unpacker plan and current blocker |
| [docs/08-roadmap.md](docs/08-roadmap.md) | sequencing, milestones, next actions |
| [docs/09-artifacts.md](docs/09-artifacts.md) | local artifact paths and reproduction commands |
| [docs/10-legal-security.md](docs/10-legal-security.md) | authorization, trademarks, secret handling |
| [docs/11-alarms-history.md](docs/11-alarms-history.md) | M2 alarm/history client, live evidence, runtime storage, activeAlarms limitation |
| [docs/12-tia-snapshot-diff.md](docs/12-tia-snapshot-diff.md) | Goal 1 snapshot/diff CLI, snapshot layout, coverage and acceptance plan |
| [docs/13-mc7-codegen.md](docs/13-mc7-codegen.md) | Classic S7-300 MC7 extraction, S7-1500 plus-code encryption, PLCSIM V21 read-back limits |
| [docs/14-tia-project-harness.md](docs/14-tia-project-harness.md) | 30-idea TIA project harness, command map, status and evidence |
| [docs/15-decompilation-status.md](docs/15-decompilation-status.md) | Surface-by-surface decompilation status: parsed, fully decoded, fixture and blocked |
| [docs/16-decompilation-roadmap.md](docs/16-decompilation-roadmap.md) | A/B/C decompilation roadmap, plus-code leads, TIA assembly inventory |
| [docs/17-tia-addon-inventory.md](docs/17-tia-addon-inventory.md) | TIA V21 add-on/assembly family inventory (WinCC, PLC language, Safety, drives, CSI) |
| [docs/18-hmi-scripting.md](docs/18-hmi-scripting.md) | WinCC Unified JavaScript/V8 scripting stack inventory |
| [docs/19-native-exports.md](docs/19-native-exports.md) | native module exports (CSI, MC7 runtime, motion, scripting) |

## Quick start (goal 3)

```powershell
$py = "$env:LOCALAPPDATA\Programs\Python\Python312\python.exe"
& $py probe/unified_probe.py browse --user Lab --password '...'
& $py probe/unified_probe.py read   --user Lab --password '...' --tag Tag01
& $py probe/unified_probe.py write  --user Lab --password '...' --tag Tag01 --value 42.5
& $py probe/unified_probe.py watch  --user Lab --password '...' --tags Tag01,Tag02 --duration 10 --out tags.csv

# live subscription logger (credentials from the environment)
$env:WINCC_USER = 'Lab'; $env:WINCC_PASSWORD = '...'
& $py probe/live_logger.py --config probe/live_logger.example.toml --duration 60

# alarms / history (M2; activeAlarms is broken, use the history feed)
& $py probe/alarm_history_watch.py --duration 60 --sqlite data/alarm_history.sqlite
& $py probe/unified_history.py alarms --system HMI_RT_1 --max 20
& $py probe/unified_history.py tags --names 'Tag01:Archivvariable_1' --max 20
& $py probe/unified_alarms.py ack --name HMI_RT_1::Tag00:Alarm_Tag01 --instance 1
```

Credentials are not stored in this repo. Create a local UMC user in TIA and pass it at runtime.

## Quick start (goal 1)

```powershell
$py = "$env:LOCALAPPDATA\Programs\Python\Python312\python.exe"
& $py probe/tia_snapshot.py capture --project <project.ap21> --out snapshots\baseline --max-blocks 200
& $py probe/tia_snapshot.py compare --before snapshots\baseline --after snapshots\changed --out diff
```

Capture starts a private headless TIA; compare does not. See
[docs/12-tia-snapshot-diff.md](docs/12-tia-snapshot-diff.md).

## Quick start (30-idea harness)

```powershell
dotnet build .\tools\tia-project\TiaProject.csproj -c Release
.\tools\tia-project\bin\Release\net10.0\tia-project.exe --help
.\tools\tia-project\bin\Release\net10.0\tia-project.exe compiler classic-test `
    --fs2xml <PlantE.objectframe.xml> --bin '<TIA_BIN>' --out out\plantE-classic-gate
```

The harness needs a local TIA installation for Openness and compiler steps; the
pure XML/SQLite commands run without it. See
[docs/14-tia-project-harness.md](docs/14-tia-project-harness.md).

## Key findings

- `WCCILgraphQLServer.exe` is a **Node.js SEA** with embedded `server.js`, authentication modules
  (`authCAPI.js`, `authSWAC.js`) and graphql-ws; PHMI schemas and WebPHMI query documents ship
  on disk.
- GraphQL subscriptions use `graphql-transport-ws`; the Bearer token must be sent in
  `connection_init.payload.Authorization` — the HTTP upgrade header is ignored.
- A headless-created Unified PC station does not compile until the encrypted-transfer password
  and start screen are configured in the editor (Openness gap **G-004**).
- The Runtime Manager only imports a **runtime `.zip`** from TIA *Download to file*; an ES
  `.zap21` is rejected.
- Importing a runtime package in the default keep mode disables UMC (`UMCData_disabled.json`).
  A full download with **`-overwriteUmc`** activates it.
- GraphQL tag names are fully qualified (`HMI_RT_1::Tag01`), `value` is an object
  (`value { value timestamp }`), and `browse` needs `nameFilters:["*"]`.
- On this project `loggedTagValues` and `loggedAlarms` work; `activeAlarms` and its subscription
  return `INTERNAL_SERVER_ERROR` even with a raised alarm. `browse` works for `ALARM`,
  `ALARMCLASS` and `LOGGINGTAG`. The fixture alarm is `HMI_RT_1::Tag00:Alarm_Tag01` (the mutation
  name includes the trigger-tag prefix) and the logging tags are `Tag0x:Archivvariable_1`.
  Runtime history lives in per-project SQLite databases (`db\APERDB`, `db\TPERDB`) plus the
  configured data/alarm logs.
- The TIA MCP snapshot is a portable `project-review.v1` JSON with a content-hash `reviewId`;
  the diff is a native Python engine over the saved review JSON, so comparison needs neither TIA
  nor the MCP. The snapshot includes hierarchy and block/type XML plus a paginated
  `hmi_inspect section=model` supplement (Unified typed public attributes; script code hashed;
  Classic coverage limited to the tool's supported sections). This is model readback, not a
  native export or runtime proof.
- Classic S7-300 MC7 is stored unencrypted in `LoadableCodeBlockData.BinaryData`;
  `BlockDebugInformation.EndSAC` gives the slice `BinaryData[78 : 78 + EndSAC + 1]`, and
  TIA's `AwlRegenerator` decodes it. Eight SCL FCs produced eight distinct code hashes
  (42/26/26/22/26/46/28/28 bytes), `observed.distinctMc7=8`, `realCodegen=true`.
- S7-1500 plus code is stored as a 124-byte CSI envelope (`MC7PlusData`): the
  header carries public/target CSI key ids, the body is `EF BE AD DE` sections.
  The key ids reproduce exactly from the project `CreationTime`, the CPU
  `ObjectId` (`{1052700-1411}`) and the 42-byte `FamilyKey` via
  `KeyKeeper.AddKey`/`GenKey` (`probe/mc7plus_keys/`). Payload decryption still
  needs the missing `Siemens.Simatic.Lang.Mc7PlusDisassembler.dll`; all eight
  blocks share the 12-byte `OpenImmediateData` stub
  `1500030030490000000802D5` (`observed.distinctMc7=1`, `realCodegen=false`).
  On PLCSIM V21 an API-created vPLC powers on but is not reachable through
  TIA's `PLCSIM` PC interface; Openness download fails with
  `Connect to module PLC_B failed`.
- The `tools/tia-project` harness implements the 30 platform ideas as CLI commands.
  `platform run` passes 29/29, `platform realpair` 8/8, and the classic compiler gate is
  `observed.distinctMc7=8`, `realCodegen=true`. Sanitized evidence is in `evidence/`; the
  plus-code, PLCSIM read-back and cross-release gaps are in
  [docs/15-decompilation-status.md](docs/15-decompilation-status.md).
- The A/B/C decompilation track is in [docs/16-decompilation-roadmap.md](docs/16-decompilation-roadmap.md)
  and the [`obsidian/siemens-re-lab`](obsidian/siemens-re-lab) vault. Non-optimized S7-1500
  blocks still use `PLUSBLOCK`; the container and key derivation are decoded, while
  payload decryption, runtime capture and PLCSIM/CPU memory remain open.
- TIA's own plus MC7 view loads `Siemens.Simatic.Lang.Mc7PlusDisassembler.dll` and calls
  `Mc7PlusDisassember.Disassembler.Disassembler.GetDisassembledCode()` with `GetMC7Plus()` +
  `ImmediateLongTable`. That DLL is **not installed** on this TIA V21 host; obtaining it is
  the top plus-code lead. The static CSI route is closed: `CsiStreamImpl` is write-only and
  `CsiWrapper` has no decrypt export.

## License

Code and documentation: MIT (see [LICENSE](LICENSE)). Vendor names and any regenerated
schemas remain subject to Siemens' terms; no vendor binaries are distributed.
