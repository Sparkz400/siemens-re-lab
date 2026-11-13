# Goal 1 — TIA project snapshot / diff

Status: **capture/compare CLI implemented and unit-tested; real project captures pending.**

The tool wraps the TIA MCP `project_snapshot_capture` and `project_snapshot_compare` tools in a
small CLI, stores snapshots portably, and turns the comparison into Markdown + JSON with a CI
exit code.

## Tools

| Tool | Purpose |
|---|---|
| `probe/tia_snapshot.py` | `capture` and `compare` commands |
| `probe/tia_mcp.py` | stdio MCP client for the TIA MCP Host (also usable standalone) |
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
  └─ copies both review.json files into a temp project-reviews/ store
  └─ starts TiaMcp.Host.exe --no-native-workers   (no TIA Portal)
       └─ project_snapshot_compare(beforeId, afterId)
  └─ writes diff/compare.json + diff/report.md
```

The TIA MCP Host speaks newline-delimited JSON-RPC over stdin/stdout. `probe/tia_mcp.py` starts a
private Host, initializes the MCP session, calls one tool, and shuts it down. The existing
OpenCode-managed TIA MCP server is not touched.

## Usage

```powershell
$py = "$env:LOCALAPPDATA\Programs\Python\Python312\python.exe"

# capture (starts a private headless TIA instance)
& $py probe/tia_snapshot.py capture `
    --project "C:\Programs\04_Programme\lab\m2-alarms\TiaMcpScratch_20261112_123345_9eb052ce\TiaMcpScratch_20261112_123345_9eb052ce.ap21" `
    --out snapshots\baseline --max-blocks 200 --max-visited 5000

# compare (offline; no TIA)
& $py probe/tia_snapshot.py compare `
    --before snapshots\baseline --after snapshots\changed --out diff
```

Bundle/artifacts defaults can be overridden with `--bundle`/`--artifacts` or
`TIA_MCP_BUNDLE`/`TIA_MCP_ARTIFACTS`. The default bundle is the installed TIA MCP v0.9.0.

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

`compare.json` is the full TIA MCP comparison result. `report.md` contains:

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
6. Run `compare` with no TIA process; verify it still works via `--no-native-workers`.

## Status

- [x] stdio MCP client with timeout/error handling
- [x] portable snapshot + archive hash + review hash verification
- [x] offline compare via `--no-native-workers`
- [x] Markdown/JSON report and exit code
- [x] unit tests (fake MCP host, report, archive)
- [ ] unchanged double-capture test on the lab project
- [ ] tag/block/screen change acceptance tests
- [ ] HMI model supplement if screen content is not covered
