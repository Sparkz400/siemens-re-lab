# Goal 1 — TIA project snapshot / diff

Status: **capture via TIA MCP; comparison is pure Python. Real project captures pending.**

The CLI captures a TIA MCP `project-review.v1` snapshot and compares two snapshots with a native
Python engine. Capture needs TIA/Openness; comparison needs neither TIA nor the TIA MCP.

## Tools

| Tool | Purpose |
|---|---|
| `probe/tia_snapshot.py` | `capture` and `compare` commands |
| `probe/tia_compare.py` | native comparison of saved `project-review.v1` files |
| `probe/tia_mcp.py` | stdio MCP client used only by `capture` |
| `probe/tests/test_tia_compare.py` | native added/modified/removed/content/impact tests |
| `probe/tests/test_tia_snapshot.py` | report/archive tests |
| `probe/tests/fake_mcp_host.py` | fake JSON-RPC host for the MCP client test |

## Architecture

```text
tia_snapshot.py capture
  └─ starts a private TiaMcp.Host.exe (native worker)
       ├─ project_open(readOnly=true)
       ├─ project_snapshot_capture(projectName, maxBlocks, …)
       └─ returns reviewId + project-review.v1 JSON
  └─ writes snapshots/<name>/snapshot.json + review.json

tia_snapshot.py compare
  └─ loads both review.json files
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
**copy** of the project. The private Host opens the project read-only but TIA still refuses a
second open of the same project.

## Snapshot layout

```text
snapshots/baseline/
  snapshot.json     metadata: project path/name, archive SHA-256, capture request,
                    reviewId, host identity, coverage
  review.json       TIA MCP project-review.v1 (model hierarchy, edges, block/type XML)
  host.log          Host stderr (diagnostics; ignored by git)
```

`review.json` is content-addressed by `reviewId` (SHA-256 of the file). The capture verifies the
hash against the Host's `reviewId` before writing the snapshot. `snapshot.json` also records the
`.ap21` archive SHA-256, so the project archive and the review can be tied together.

Snapshots contain project data. Keep `snapshots/` and generated `diff-*/` directories out of
git; the `.gitignore` covers them.

## Comparison output

`compare.json` is the native comparison result. `report.md` contains:

- before/after capture times, project names, archive hashes and review IDs;
- total changes and added/removed/modified counts;
- a change table (`path`, `kind`, `change`, `changedFields`);
- observed static impact paths (bounded);
- coverage/limitations (`wholeProjectEquivalent=false`, unknown content counts, partial scope).

The command exits **1** when changes are found, **0** when the captured scope is unchanged, and
**2** on a TIA MCP error. Use `--no-fail` for a non-CI run.

## Coverage

- **Blocks/types:** exact native XML exports are compared; interface/logic/document hashes are
  reported. Protected, safety and unsupported blocks are explicit unknowns.
- **Hierarchy/metadata:** all captured native nodes and direct relationships, with exact paths.
- **Tags:** captured as project-model metadata; the block/type XML is the content payload.
- **HMI screens/items:** the TIA MCP snapshot captures HMI objects as model metadata, but not the
  full screen-item XML. A `hmi_inspect section=model` supplement is planned if screen item
  changes need content-level coverage.
- Capture is bounded by `--max-blocks` (default 25, max 200) and `--max-visited`. Omitted content
  is explicit; absence from a partial capture is not proof of deletion.

## Acceptance plan

1. Capture an unchanged project twice → `compare` reports 0 changes and exits 0.
2. Add one tag → one `addedToCapturedScope` change for the exact tag path.
3. Modify one block → one `modified` change with `logicChanged`/`interfaceChanged`.
4. Modify one HMI screen property → confirm whether the model metadata changes; if not, add the
   HMI model supplement.
5. Capture a project copy and compare with `--allow-different-project-copy`.
6. Run `compare` with no TIA process and no TIA MCP bundle; verify it still works.

## Status

- [x] stdio MCP client with timeout/error handling
- [x] portable snapshot + archive hash + review hash verification
- [x] native Python compare; no TIA, no MCP Host
- [x] Markdown/JSON report and exit code
- [x] unit tests (fake MCP host, native compare, report, archive)
- [x] offline compare validated with synthetic `project-review.v1` fixtures:
  an added tag + a modified block produced 2 changes (exit 1); an identical clone produced
  0 changes (exit 0)
- [ ] unchanged double-capture test on the lab project
- [ ] tag/block/screen change acceptance tests
- [ ] HMI model supplement if screen content is not covered
