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
| `tools/tia-project/` | 30-idea TIA project harness, command map, classic/plus MC7 hosts |
| `probe/openness_classic/` | create and compile the classic S7-300 PlantE project |
| `probe/openness_probe/` | Openness subnet, download-config, download and close probe |
| `probe/plcsim_v21_host/` | PLCSIM V21 standard vPLC power-on diagnostic |
| `evidence/` | sanitized platform/compiler/real-pair JSON summaries |
| `probe/mc7_classic_extract.py` | classic S7-300 MC7 code-slice extractor from objectframe XML |
| `probe/mc7_classic_host/` | TIA `AwlRegenerator` JSON host, build script and usage notes |
| `probe/mc7plus_extract.py` | S7-1500 plus-code section extractor from objectframe XML |
| `probe/mc7plus_reader/` | `MC7PlusBinaryReader` / `MC7plus.Disassembler` probe |
| `probe/mc7plus_analyze.py` | plus-section entropy/length analysis |
| `probe/tia_inventory.ps1` | TIA Portal assembly metadata inventory |
| `probe/tests/` | alarm-store, TIA MCP, snapshot-report and MC7 extractor tests |
| `analysis/tia-assembly-inventory.csv` | TIA V21 managed/native assembly metadata |
| `analysis/tia-assembly-summary.json` | TIA V21 inventory summary |
| `analysis/plus-section-analysis.json` | plus-section lengths, entropy and prefixes |
| `obsidian/siemens-re-lab/` | Obsidian vault: A/B/C notes, experiment log, inventory |
| `docs/11-alarms-history.md` | M2 API shapes, live failure evidence, storage, fixture path |
| `docs/13-mc7-codegen.md` | classic/plus MC7 evidence, PLCSIM V21 read-back limits |
| `docs/14-tia-project-harness.md` | 30-idea harness command map and status |
| `docs/15-decompilation-status.md` | surface-by-surface decompilation status |
| `docs/16-decompilation-roadmap.md` | A/B/C roadmap, plus-code leads, TIA inventory |
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

Capture starts a private TIA MCP Host with the native worker and also captures a paginated
`hmi_inspect section=model` supplement. Compare is pure Python over the saved `review.json` and
HMI model files; it does not start TIA or the MCP Host. See
[12-tia-snapshot-diff.md](12-tia-snapshot-diff.md) for coverage and acceptance tests.

### MC7 codegen (classic S7-300)

```powershell
& $py probe/mc7_classic_extract.py --xml <PlantE.objectframe.xml> --out out\classic-blocks
.\probe\mc7_classic_host\build.ps1 -TiaBin 'C:\Program Files\Siemens\Automation\Portal V21\Bin'
.\probe\mc7_classic_host\bin\Mc7ClassicHost.exe `
    --tia-bin 'C:\Program Files\Siemens\Automation\Portal V21\Bin' `
    --data out\classic-blocks\FC_And.bin --start 0 --length 26
& $py -m unittest discover -s probe/tests -p 'test_mc7_classic_extract.py' -v
```

The objectframe XML comes from the Openness `fs2xml` step. The extractor reads
`LoadableCodeBlockData.BinaryData` and `BlockDebugInformation.EndSAC`; the host
loads TIA's `AwlRegenerator` from the local installation and prints JSON
statements. See [13-mc7-codegen.md](13-mc7-codegen.md) for the measured classic
and plus-code results and the PLCSIM V21 limits.

### MC7 plus reader

```powershell
python .\probe\mc7plus_extract.py `
    --xml <PlantB2.objectframe.xml> --out out\plus-sections
.\probe\mc7plus_reader\build.ps1 -TiaBin '<TIA_BIN>'
.\probe\mc7plus_reader\bin\Mc7PlusReader.exe `
    --tia-bin '<TIA_BIN>' `
    --code out\plus-sections\FC_And.sec1.bin `
    --immediate out\plus-sections\FC_And.open.bin `
    --label 'FC_And sec1 + open'
```

The measured result is negative: the immediate table does not unlock the
observed sections (`Read not allowed type code` / `Instruction code has no
match`). See [`probe/mc7plus_reader/README.md`](../probe/mc7plus_reader/README.md).

The section analysis records lengths, Shannon entropy and prefixes:

```powershell
python .\probe\mc7plus_analyze.py `
    --dir out\plus-sections --out analysis\plus-section-analysis.json
```

The plus sections are short and near the small-sample entropy maximum, so the
result is consistent with encrypted/encoded data; it is not proof of plaintext.

### TIA assembly inventory

```powershell
.\probe\tia_inventory.ps1 -TiaBin '<TIA_BIN>' -OutDir .\analysis
```

The script classifies every `*.dll`/`*.exe` as managed or native and writes
`analysis/tia-assembly-inventory.csv` + `analysis/tia-assembly-summary.json`.
The TIA V21 run found 2,573 files: 2,326 managed, 247 native, 1,640.8 MB total.
Vendor decompiled source is not committed.

### TIA project harness (30 ideas)

```powershell
dotnet build .\tools\tia-project\TiaProject.csproj -c Release
.\tools\tia-project\bin\Release\net10.0\tia-project.exe --help
.\tools\tia-project\bin\Release\net10.0\tia-project.exe compiler classic-test `
    --fs2xml <PlantE.objectframe.xml> --bin '<TIA_BIN>' --out out\plantE-classic-gate
```

The harness source, command map and artifact names are in
[`tools/tia-project/PLATFORM.md`](../tools/tia-project/PLATFORM.md). The
sanitized platform/compiler/real-pair summaries are in [`evidence/`](../evidence).
See [14-tia-project-harness.md](14-tia-project-harness.md) and
[15-decompilation-status.md](15-decompilation-status.md).

### Openness classic project

```powershell
.\probe\openness_classic\build.ps1
.\probe\openness_classic\bin\OpennessClassic.exe `
    <PROJECTS> PlantE .\probe\openness_classic\PlantE.scl
```

TIA Portal must be running and the Openness access prompt must be accepted.
The program creates CPU `6ES7 315-2EH14-0AB0/V3.2`, four tags and the eight
SCL functions, then compiles. See
[`probe/openness_classic/README.md`](../probe/openness_classic/README.md).

### Openness / PLCSIM V21 probes

```powershell
.\probe\openness_probe\build.ps1
.\probe\openness_probe\bin\OpennessProbe.exe config  .\PlantB2-plcsim\PlantB.ap21
.\probe\openness_probe\bin\OpennessProbe.exe subnet  .\PlantB2-plcsim\PlantB.ap21
.\probe\openness_probe\bin\OpennessProbe.exe download .\PlantB2-plcsim\PlantB.ap21 192.168.0.1

dotnet build .\probe\plcsim_v21_host\plcsimhost.csproj -c Release
.\probe\plcsim_v21_host\bin\Release\net10.0\plcsimhost.exe
```

The download probe documents the measured blocker: the PLCSIM V21 vPLC powers
on, but TIA cannot connect to it (`Connect to module PLC_B failed`). See
[`probe/openness_probe/README.md`](../probe/openness_probe/README.md) and
[`probe/plcsim_v21_host/README.md`](../probe/plcsim_v21_host/README.md).

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
