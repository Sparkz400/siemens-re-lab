# Goal 1 — TIA project snapshot / diff

Status: **capture via TIA MCP; comparison is pure Python. Live project, tag, block and HMI
screen-item changes validated.**

The CLI captures a TIA MCP `project-review.v1` snapshot plus a paginated
`hmi_inspect section=model` supplement for every HMI, and compares two snapshots with a native
Python engine. Capture needs TIA/Openness; comparison needs neither TIA nor the TIA MCP.

## Tools

| Tool | Purpose |
|---|---|
| `probe/tia_snapshot.py` | `capture` and `compare` commands; HMI model capture |
| `probe/tia_compare.py` | native comparison of saved reviews and HMI models |
| `probe/tia_mcp.py` | stdio MCP client used only by `capture` |
| `probe/tests/test_tia_compare.py` | added/modified/removed/content/HMI/impact tests |
| `probe/tests/test_tia_snapshot.py` | report/archive/manifest tests |
| `probe/tests/fake_mcp_host.py` | fake JSON-RPC host for the MCP client test |

## Architecture

```text
tia_snapshot.py capture
  └─ starts a private TiaMcp.Host.exe (native worker)
       ├─ tia_start (headless, worker-owned TIA instance)
       ├─ project_open(readOnly=false; needed for block Export)
       ├─ project_snapshot_capture(projectName, maxBlocks, …)
       ├─ hmi_inspect(section=model, hmiPath, offset) for every HMI
       └─ returns reviewId + project-review.v1 JSON + HMI model pages
  └─ writes snapshots/<name>/snapshot.json + review.json + hmi-model*.json

tia_snapshot.py compare
  └─ loads both review.json and HMI model files
  └─ probe/tia_compare.py (pure Python; no TIA, no MCP Host)
  └─ writes diff/compare.json + diff/report.md
```

The TIA MCP Host speaks newline-delimited JSON-RPC over stdin/stdout. `probe/tia_mcp.py` starts a
private Host, initializes the MCP session, calls `project_snapshot_capture`, and shuts it down.
The existing OpenCode-managed TIA MCP server is not touched.

**MCP boundary:** the TIA MCP is only the capture bridge to Openness. The comparison engine is
`probe/tia_compare.py` and consumes only the saved review JSON. If a standalone Openness capture
adapter replaces the MCP later, the snapshot format and diff engine stay unchanged.

## Usage

```powershell
$py = "$env:LOCALAPPDATA\Programs\Python\Python312\python.exe"

# capture (starts a private headless TIA instance)
& $py probe/tia_snapshot.py capture `
    --project "C:\Programs\04_Programme\lab\m2-alarms\TiaMcpScratch_20261112_123345_9eb052ce\TiaMcpScratch_20261112_123345_9eb052ce.ap21" `
    --out snapshots\baseline --max-blocks 200 --max-visited 5000

# compare (pure Python; no TIA, no MCP)
& $py probe/tia_snapshot.py compare `
    --before snapshots\baseline --after snapshots\changed --out diff
```

Bundle/artifacts defaults for **capture** can be overridden with `--bundle`/`--artifacts` or
`TIA_MCP_BUNDLE`/`TIA_MCP_ARTIFACTS`. `compare` accepts neither; it reads only the snapshot
directories.

Close a user-owned TIA Portal instance with the project open before capture, or capture a
**copy** of the project. Capture opens the project read-write because TIA refuses native block
`Export` from a secondary read-only project; it does not save or change the project. The
worker-owned TIA instance is detached after capture.

HMI model capture is on by default. Use `--no-hmi-model` to skip it, and `--hmi-max-pages`
(default 100, 100 items/page) to bound pagination. A model page that does not finish is retained
with explicit `coverage`/`nextOffset` data rather than silently treated as complete.

## Snapshot layout

```text
snapshots/baseline/
  snapshot.json     metadata: project path/name, .ap21 SHA-256, project-folder manifest SHA-256,
                    capture request, reviewId, host identity, coverage, HMI model hashes
  review.json       TIA MCP project-review.v1 (model hierarchy, edges, block/type XML)
  hmi-model.json    hmi-model.v1 merged pages for the single HMI
  hmi-model-2.json  additional HMIs, named by index
  host.log          Host stderr (diagnostics; ignored by git)
```

`review.json` is content-addressed by `reviewId` (SHA-256 of the file). The capture verifies the
hash against the Host's `reviewId` before writing the snapshot. HMI model files are recorded in
`snapshot.json` with their own SHA-256 and are verified on load. `snapshot.json` also records the
`.ap21` archive SHA-256, so the project archive and the review can be tied together.

The HMI model is a paged merge: each page is requested with `offset`, and the last response's
`coverage`, `nextOffset` and `target` are stored. `hmi-model.json` contains the merged `items`
list plus `pages`, `nextOffset`, `hmiPath`, `target`, `coverage` and `capturedUtc`.

Snapshots contain project data. Keep `snapshots/` and generated `diff-*/` directories out of
git; the `.gitignore` covers them.

## Comparison output

`compare.json` is the native comparison result. `report.md` contains:

- before/after capture times, project names, `.ap21` and project-folder hashes, review IDs;
- project change totals and added/removed/modified counts;
- a project change table (`path`, `kind`, `change`, `changedFields`);
- an HMI model change table (`path`, `kind`, `composition`, `change`, `changedFields`);
- per-HMI coverage status and item counts for both snapshots;
- observed static impact paths (bounded);
- coverage/limitations (`wholeProjectEquivalent=false`, unknown content counts, partial scope).

HMI model changes are deduplicated against project changes: if a changed HMI object already
appeared in the project result, the HMI detail is not repeated. `compare.json` carries both
`totalChanges`/`changes` (project) and `totalHmiChanges`/`hmiChanges`, plus
`totalChangesIncludingHmi` and `hmiModelCompared`.

The command exits **1** when changes are found, **0** when the captured scope is unchanged, and
**2** on a TIA MCP error. Use `--no-fail` for a non-CI run.

## Coverage

- **Blocks/types:** exact native XML exports are compared; interface/logic/document hashes are
  reported. Protected, safety and unsupported blocks are explicit unknowns.
- **Hierarchy/metadata:** all captured native nodes and direct relationships, with exact paths.
  Volatile metadata timestamps (`modified`, `created`, `lastModified`, `modificationTime`) are
  normalized away.
- **Tags:** captured as project-model metadata; the block/type XML is the content payload.
- **HMI model:** `hmi_inspect section=model` provides Unified typed readback of the public V21
  object model: one row per composition member (screens, screen items, tags, alarms, logs,
  runtime settings, scripts) with attributes. Items are matched by exact native path; attribute
  changes are normalized for volatile timestamps and `UpdateId`. Script code is present only as
  a SHA-256, sensitive attribute names are excluded, and payloads without a public getter are
  not observed — this is not a native export or runtime proof. Classic HMI coverage depends on
  the tool's per-section support.
- Capture is bounded by `--max-blocks` (default 25, max 200), `--max-visited`, and
  `--hmi-max-pages`. Omitted content is explicit; absence from a partial capture is not proof of
  deletion.

## Acceptance plan

1. Capture an unchanged project twice → `compare` reports 0 changes and exits 0.
2. Add one tag → one `addedToCapturedScope` tag entry (timestamp-only parent metadata is
   ignored).
3. Modify one block → one `modified` change with `logicChanged`/`interfaceChanged`.
4. Add one HMI screen item (e.g. `HmiRectangle` via `item_create`) → one HMI model
   `addedToCapturedScope` entry.
5. Capture a project copy and compare with `--allow-different-project-copy`.
6. Run `compare` with no TIA process and no TIA MCP bundle; verify it still works.

## Live acceptance (2026-11-13)

| Test | Result |
|---|---|
| Unchanged double capture | **0 changes**, exit 0; 4 block contents compared |
| Add one PLC tag (`DiffTag01`, Bool, `%M100.0`) | **1 change**, exit 1: the tag was added; compile/save succeeded; 4 block contents compared |
| Edit one block (`Main` interface comment via `block_import_xml`) | **1 change**, exit 1: `modified` with `interfaceChanged`, `documentChanged`; compile/save succeeded |
| HMI model capture (`hmi_inspect section=model`) | **134 items / 2 pages**, `coverage.complete=true`, 1 screen (`Overview_A`), 20 HMI tags, 18 alarm classes |
| Unchanged double capture with HMI model | **0 project changes, 0 HMI model changes**, exit 0 |
| Add one HMI item (`HmiRectangle` `DiffRect01` via `hmi_screen_create`/`item_create`) | **0 project changes, 1 HMI model change**, exit 1: `addedToCapturedScope`, kind `HmiRectangle`, composition `ScreenItems`; compile/save succeeded; model 134 → 135 items |

Baseline review `aa0e230b…`: 103 nodes, 4 exported blocks, project-folder hash
`c5943384…` (76 files, 10,400,704 bytes). After the tag: 104 nodes, 4 blocks, folder hash
`d48ed4e7…` (76 files, 10,445,323 bytes). After the block edit: 104 nodes, 4 blocks, folder hash
`00bf95e5…` (76 files, 10,516,138 bytes). The `.ap21` file SHA-256 stayed
`aa8fc543…` in all captures — TIA stores much of the project state in the project folder, so
`projectFolderSha256` is the useful state hash; `archiveSha256` alone is not sufficient.

The native compare ignores volatile metadata timestamps (`modified`, `created`, `lastModified`,
`modificationTime`) and `UpdateId`. That removes the tag-table timestamp-only churn: a tag
addition is one semantic entry, and a block edit is one block entry. Content changes are still
detected through the interface/logic/document signature hashes.

The real HMI model snapshot (`snapshots/goal1b-hmi2`) contains 134 model rows: 1 screen, 40
thresholds, 27 system tags, 25 system text lists, 20 HMI tags, 18 alarm classes, 1 tag table,
1 runtime setting and 1 language/font row. The lab screen `Overview_A` started with no items.
A live `HmiRectangle` creation (`DiffRect01`, 80×40 at 10,10) produced 135 rows and compared as
exactly **1 HMI change, 0 project changes**. The item path is
`.../screens/Overview_A/items/DiffRect01`; the project review does not enumerate screen items,
so this is precisely the coverage the HMI supplement adds. The project-folder hash changed to
`f5668789…` (83 files, 10,541,272 bytes), and the `.ap21` hash again did not change.

The HMI engineering gate needs an `expectedContentHash`. `change_prepare` returns the current
preservation baseline hash in its `StaleRevision` evidence; pass that value on the next prepare.
The create applied with compile/save and exact native readback on the project copy.

## Status

- [x] stdio MCP client with timeout/error handling
- [x] portable snapshot + archive/folder hashes + review hash verification
- [x] native Python compare; no TIA, no MCP Host
- [x] Markdown/JSON report and exit code
- [x] unit tests (fake MCP host, native compare, HMI model, report, archive)
- [x] offline compare validated with synthetic `project-review.v1` fixtures
- [x] unchanged double-capture test on the lab project
- [x] one-tag addition test (one semantic entry)
- [x] block edit test (`interfaceChanged`, `documentChanged`)
- [x] HMI model supplement (`hmi_inspect section=model`, paginated, hashed)
- [x] real HMI model capture (134 items, complete) and unchanged HMI compare (0/0)
- [x] live HMI screen-item addition test (0 project, 1 HMI change)
