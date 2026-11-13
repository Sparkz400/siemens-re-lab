# Artifacts and reproduction

## Published in this repo

| Path | Content |
|---|---|
| `docs/` | goals, environment, lab project, GraphQL, TIA Openness, plans, roadmap, legal |
| `probe/unified_probe.py` | login / browse / read / write / CSV watcher |
| `probe/live_logger.py` | graphql-ws tag subscription → SQLite + CSV, reconnect/backoff |
| `probe/demo_writer.py` | deterministic 1 Hz value generator for soak tests |
| `probe/live_logger.example.toml` | logger configuration template (no credentials) |
| `probe/soak_report.py` | capture summary: rate, intervals, sessions, gaps, integrity |
| `probe/rebuild_runtime_package.py` | rebuild a runtime `.zip` from the project's `IM\HMI\I\0\Saved` |
| `probe/unified_alarms.py` | active alarms, ack/reset/enable/disable/shelve/unshelve |
| `probe/alarm_history_watch.py` | live alarm feed by polling `loggedAlarms` into SQLite/CSV |
| `probe/unified_history.py` | `loggedTagValues` / `loggedAlarms` time-range queries |
| `probe/tia_snapshot.py` | TIA project snapshot capture + offline diff CLI |
| `probe/tia_mcp.py` | stdio MCP client for the TIA MCP Host |
| `probe/tests/` | alarm-store, TIA MCP and snapshot-report tests |
| `docs/11-alarms-history.md` | M2 API shapes, live failure evidence, storage, fixture path |
| `probe/dump_schema.py` | introspection → local SDL + JSON |
| `probe/summarize_schema.py` | introspection JSON → Markdown operation summary |
| `analysis/schema-summary.md` | curated queries/mutations/subscriptions/tag model |
| `examples/tags.csv` | truncated demo capture (10 tags, 0.5 s) |
| `examples/alarm_history.csv` | 4-row alarm transition sample from the M2 fixture |

The **raw GraphQL SDL** (`schema.runtime.graphql`) is generated locally from the licensed
installation and is intentionally **not committed**. Run `probe/dump_schema.py` to recreate it.

## Local lab artifacts (not published)

| Artifact | Path |
|---|---|
| TIA project | `C:\Programs\04_Programme\lab\tia-mcp-artifacts\scratch\TiaMcpScratch_20261112_123345_9eb052ce\...ap21` |
| Runtime package | `%USERPROFILE%\Documents\Simatic.HMI\RT_Projects\HMI_RT_1[...] - Full <date>.zip` |
| Runtime project data | `C:\ProgramData\SCADAProjects\HMI_RT_1\` |
| Alarm persistency DB | `C:\ProgramData\SCADAProjects\HMI_RT_1\db\APERDB\HMI_RT_1_AlarmPersistencyDatabase.db3` |
| Tag persistency DB | `C:\ProgramData\SCADAProjects\HMI_RT_1\db\TPERDB\HMI_RT_1_TagPersistencyDatabase.db3` |
| Compiled HMI output | `...\TiaMcpScratch_...\IM\HMI\I\0\Saved\` (package source) |
| TIA MCP bundle | `C:\TiaMcp\releases\tia-mcp-v21-0.9.0_20261112_122441_bb02d6cf` |
| TIA MCP source | `C:\Programs\04_Programme\src\TIA_MCP` |
| TIA MCP changes/journals | `C:\Programs\04_Programme\lab\tia-mcp-artifacts\changes\` |
| DPAPI credentials | `...\scratch-credentials\*.dpapi` (current Windows user only) |
| GraphQL capture | `C:\Programs\04_Programme\analysis\wincc-graphql\` |
| Logger soak data | `C:\Programs\04_Programme\analysis\wincc-graphql\soak\` |
| TIA snapshots/diffs | `C:\Programs\siemens-re-lab\snapshots\`, `diff-*\` (ignored) |
| WSL worker | `\\wsl$\Ubuntu-24.04\opt\...` (Ghidra, Kuna, rizin, jars) |
| Windows toolkit | `C:\Programs\04_Programme\` |

Do not commit any of these: they contain project data, credentials/tokens, vendor archives,
certificates or machine-specific paths.

## Reproduction

### GraphQL probe

```powershell
$py = "$env:LOCALAPPDATA\Programs\Python\Python312\python.exe"
$env:PYTHONPATH = ''
& $py probe/unified_probe.py browse --user Lab --password '...'
& $py probe/unified_probe.py watch  --user Lab --password '...' --tags Tag01,Tag02 --duration 10 --out tags.csv
```

### Live logger

```powershell
$env:WINCC_USER = 'Lab'
$env:WINCC_PASSWORD = '...'
& $py probe/live_logger.py --config probe/live_logger.example.toml --duration 3600
& $py probe/demo_writer.py --tags Tag01,Tag02,Tag03,Tag04,Tag05,Tag06,Tag07,Tag08,Tag09,Tag10 `
    --duration 3590 --interval 1
```

Outputs are the SQLite tables `samples`, `sessions`, `events`, `meta` and the CSV listed in
[04-wincc-graphql.md](04-wincc-graphql.md#live-logger-m1). Soak data stays under the local
`analysis\wincc-graphql\soak\` path and is not committed.

### Alarms and history (M2)

```powershell
$env:WINCC_USER = 'Lab'
$env:WINCC_PASSWORD = '...'
& $py probe/unified_alarms.py list
& $py probe/alarm_history_watch.py --duration 60 `
    --sqlite data/alarm_history.sqlite --csv data/alarm_history.csv
& $py probe/unified_alarms.py ack --name HMI_RT_1::Tag00:Alarm_Tag01 --instance 1
& $py probe/unified_history.py tags  --names 'Tag01:Archivvariable_1' --max 20
& $py probe/unified_history.py alarms --system HMI_RT_1 --max 20
& $py -m unittest discover -s probe/tests -v
```

The direct `activeAlarms` query/subscription still returns `INTERNAL_SERVER_ERROR`; use
`alarm_history_watch.py` for the live alarm feed. See
[11-alarms-history.md](11-alarms-history.md) for the evidence.

### TIA snapshot / diff (Goal 1)

```powershell
& $py probe/tia_snapshot.py capture --project <project.ap21> --out snapshots\baseline `
    --max-blocks 200
& $py probe/tia_snapshot.py compare --before snapshots\baseline `
    --after snapshots\changed --out diff
& $py -m unittest discover -s probe/tests -v
```

Capture starts a private TIA MCP Host with the native worker. Compare is pure Python over the
saved `review.json`; it does not start TIA or the MCP Host. See
[12-tia-snapshot-diff.md](12-tia-snapshot-diff.md) for coverage and acceptance tests.

### Schema dump

```powershell
& $py probe/dump_schema.py --url http://localhost:4000/graphql --out .
& $py probe/summarize_schema.py --introspection introspection.json --out schema-summary.md
```

### Runtime package

```powershell
$rm = 'C:\Program Files\Siemens\Automation\WinCCUnified\bin\SIMATICRuntimeManager.exe'
& $rm -s -o download.log -overwriteUmc -c fulldownload '<runtime>.zip'
& $rm -s -o start.log -c start <project-id>
```

### TIA MCP

```powershell
$env:FrameworkPathOverride = 'C:\Programs\04_Programme\src\TIA_MCP\.net48refs\build\.NETFramework\v4.8'
.\scripts\New-IsolatedBuild.ps1 -Configuration Release
.\scripts\Package.ps1 -Configuration Release -SkipBuild -IsolatedBuildDirectory <build-root>
```

## Environment snapshot

- TIA V21, PLCSIM Advanced V8, WinCC Unified PC RT 21.0.
- Node 24.22.3, REA 6.9.0 (Windows 6.8), Python 3.12.
- Ghidra 12.1.3 (Windows + WSL), IDA Free 9.5, jadx 1.5.6, apktool 3.0.3.
- WSL Ubuntu 24.04.5, Kuna 1.990, Rizin 0.9.1, TShark 4.2.2, Frida 17.26.2.
- TIA MCP v0.9.0 (314 tools), local Test Suite stub + HMI station-name patch.
