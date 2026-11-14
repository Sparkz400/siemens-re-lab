# Platform layer — all 30 ideas

The 30 ideas from the “think bigger” map are implemented as CLI commands and
orchestrated by:

```powershell
tia-project platform run --project <dir> --fs2xml <project.objectframe.xml> --out out\platform
```

The run writes `platform-summary.json` / `platform-summary.md` with one row per
step. On `Scratch` all 29 steps pass:

`<LAB>\tia-project\out\platform-all\platform-summary.md`

The real-pair run uses the Openness-created `PlantB2` release for drift,
corpus, and fleet, and the classic S7-300 `PlantE` export for compiler
recovery; it also passes 29/29:

`<LAB>\tia-project\out\platform-real-b2\platform-summary.md`

`<LAB>\tia-project\out\platform-classic\platform-summary.md`
(the same run with `--compiler-export PlantE.objectframe.xml`)

`<LAB>\tia-project\out\real-pair\bundle-4plant\realpair-summary.json` (8 steps)

Pass `--release-project` and `--release-export` to bind a real second
project; without them the run creates a synthetic release fixture. Pass
`--compiler-export` to point the compiler step at a different real project
(the canonical run uses `PlantE.objectframe.xml`).

## Semantic change control

| Idea | Command | Artifact |
|---|---|---|
| Behavioral bisect / safety merge gate | `gate compare`, `gate bisect` | `gate/gate.json`, `gate/bisect.json` |
| Git merge driver for PLC projects | `merge --base --ours --theirs` | `merge/merged.xml`, `merge/merge.json` |
| Forensic timeline | `forensics --project --fs2xml` | `forensics/forensics.json`, `forensics.md` |
| Plant flight recorder | `flightrec record/replay/test/ingest/recorded-test` | `flightrec/recorded-trace.json`, `flightrec/io.csv`, `flightrec/alarms.csv`, `flightrec/replay.json`, `flightrec/recorded-test.json` |
| Programmed forgetting | `forget --keep-last N`, `prune` | `forgetting/result.json`, `forgetting/project/`, `retention/retention.json` |

The gate compares block bodies, LAD contact/coil counts, interface sections,
tag addresses/limits, and XRef edge removals. Verdict is `allow`, `review`, or
`block`. The merge driver is a semantic three-way merge over `StorageObject`
GUIDs, with per-object conflict reporting.

Recorded I/O is ingested from timestamped CSV rather than generated in memory.
`flightrec ingest` accepts a long sample file (`timestamp,tag,value`), a wide
sample file (`timestamp,<tag>,...`), an alarm file
(`timestamp,code,severity,message,tag`) and a savepoint-time map
(`{ "0": "2026-11-13T14:46:00Z", ... }`). It rejects unknown tags,
non-monotonic timestamps, samples before the first savepoint, invalid
severities, and explicit savepoint/time disagreements, then binds every row to
the latest savepoint at or before its timestamp. Replay also compares each
sample's recorded savepoint state hash with the history-derived state hash and
reports `savepoint-state-mismatch`. `evolve recorded` maps recorded tags to
block symbols (`Tag=Symbol`) and scores the block against the recorded
inputs/expected outputs.
## The project as a queryable platform

| Idea | Command | Artifact |
|---|---|---|
| Queryable ontology / SQL over the plant | `ontology build`, `ontology query` | `ontology/ontology.db` |
| Agent-native semantic API + benchmark | `api call`, `api benchmark` | `ontology/benchmark.json` |
| Vendor-neutral control IR | `ir export`, `ir verify`, `ir roundtrip` | `ir/project.ir.json`, `ir/verify.json`, `ir/roundtrip/roundtrip.json` |
| LLVM-style automation IR | `ir passes`, `ir lower --backend` | `ir/project-ir.dot`, `ir/roundtrip/lowered/*.simaticml.xml` |
| Openness-without-TIA | `ci run`, `ci linux`, `ci init` | `ci/ci-report.json`, `ci/linux/ci-linux-report.json`, CI scaffold |

`ci linux` cross-publishes the toolkit as a self-contained `linux-x64`
executable and runs `ci run` under WSL. It is a read-only, GUI-free backend:
PLF/IDX, XRef, VCI, semantic gate, ontology, and IR verification all work from
the exported project artifacts. Project creation and SCL compilation still
require TIA Portal; this covers the headless verification half of Openness.
The ontology is a SQLite property graph (`nodes`, `edges`, `savepoints`) with
block/tag/screen/device nodes and `references`/`uses`/`contains` edges. The API
verbs are `resolve`, `callers`, `writers`, `screens_using_tag`, `impact`,
`diff`, `stats`, `query`.

`ir roundtrip` lifts SCL, classic FlgNet, and MC7 from an export into the typed
IR, lowers each block to SimaticML (`StructuredText` for SCL, V5 `FlgNet` for
LAD), validates the body and interface against the installed Openness XSDs
(`SW.PlcBlocks.SCL_v4.xsd`, `SW.PlcBlocks.LADFBD_v5.xsd`,
`SW.InterfaceSections_v5.xsd`), then re-lifts the lowered documents and compares
token streams, LAD parts/wires/operands, interface sections, MC7
hex/hash/length/instructions, tags, and a canonical IR projection hash.
Operand references in classic FlgNet are resolved through the block's
`IdentPartData` table (`RefId` -> tag name) before lowering. The LAD lowering
maps `Part Gate` to `Part Name`, `ORef` to an `Access` operand, `PCon` to
`NameCon`, and `OCon` to `IdentCon`.

## Proof, provenance, and custody

| Idea | Command | Artifact |
|---|---|---|
| Equivalence certifier | `certify semantic`, `certify semantic-test` | `proof/mc7-semantic-test.json`, `proof/mc7-semantic-main-main.json` |
| Bisimulation oracle | `certify symbolic`, `certify bisim`, `certify bisim-test`, `certify mc7-test` | `proof/mc7-cycle-test.json`, `proof/mc7-cycle-main-main.json`, `proof/mc7-cycle-main-block1.json`, `proof/mc7-symbolic-main-block1.json`, `proof/mc7-main-block1.json` |
| Signed logic registry | `registry init/sign/verify/attest` | `registry/manifest.sig.json` |
| Verified block marketplace | `market publish/verify/test` | `market/package.json`, `market/market-test.json` |

`certify bisim` is cycle-for-cycle observational equivalence over MC7
transitions, not graph isomorphism:

- Each block is lifted to an explicit transition system: decoded MC7
  instructions, resolved `JMP` targets, and successor edges.
- A scan cycle runs from entry to block end. At every cycle boundary the
  observation is `(RLO, ACCU, bit-memory cells, byte-memory cells)` over an
  explicit observation set (`--observe-bits`, `--observe-bytes`; default is
  the union of addresses referenced by either block).
- Both machines share symbolic inputs, so the same input valuation feeds both
  sides. `--init-bits` / `--init-bytes` pin the initial memory state.
- Paths are enumerated exactly (branch guards are the symbolic `RLO` at each
  `JMP`), chained cycle by cycle, and the frontier is deduplicated by symbolic
  state. For each cycle and path pair, Z3 QF_AUFBV checks whether the
  observations can differ; a `sat` result yields the cycle index, both paths,
  the differing cells, and the model. `--cycles` bounds the unrolling.
- `complete=true` means no truncation or loop occurred and every requested
  cycle was checked; a non-terminating cycle reports `complete=false` and
  `definitive=false` instead of claiming equivalence. `certify bisim-test`
  runs 12 positive/negative cases plus concrete replay of the same programs.

Signatures use ECDSA P-256 / SHA-256. The contract schema is
`tia-project/block-contract/v1`: block name, interface sections and members,
preconditions, postconditions, and declared effects. `market publish` verifies
the interface and effects against the block IR, executes the block's MC7
symbolically, and asks Z3 whether `preconditions AND NOT postconditions` is
satisfiable. It also binds the package to a signed VCI `FileRecord` for
`System/PEData.plf`, comparing the recorded hash/description against the
actual project file. `market verify` re-checks the signature, the recorded
contract/provenance status, the block body hash, and (with `--fs2xml`) the
contract against the buyer's copy; `--project --vci` re-binds provenance.

## Corpus, benchmark, and drift economics

| Idea | Command | Artifact |
|---|---|---|
| Format-drift tripwire | `drift snapshot`, `drift compare` | `drift/a.json`, `drift/b.json`, `drift/compare.json` |
| Golden conformance corpus | `corpus build`, `corpus verify`, `corpus multi` | `corpus/index.json`, `corpus/<project>/manifest.json` |
| Sell the corpus / mutants | `corpus mutate` | `corpus/mutants/` |
| Fleet corpus | `fleet record/report/aggregate/pattern-test` | `fleet/*.fleet.json`, `fleet/report.json`, `fleet/crossplant/report.json`, `fleet-pattern-test.json` |

The drift snapshot captures PLF format version, IDX entry size/count, PLF type
histogram, XRef schema hash, XRef table counts, FS2XML version, and artifact
hashes. `platform realpair` runs drift, multi-project corpus, fleet, and
compiler recovery against two real projects. The current pair is `Scratch`
(WinCC + STEP 7) and `PlantB2` (Openness-created CPU 1515-2 PN with four
tags and eight compiled SCL blocks). The cross-plant fleet adds `PlantC`
(CPU 1511-1 PN) and `PlantD` (CPU 1517-3 PN/DP), both Openness-created.
plant, records each project, and aggregates patterns across plants. The
pattern CSV columns are `timestamp,code,severity,message,tag,pattern`; unknown
tags, invalid severities, empty patterns, and malformed timestamps are
rejected. `fleet pattern-test` validates the cross-plant aggregation with four
synthetic records and the negative CSV cases.

`corpus multi` builds and verifies a manifest
per project and writes an aggregate index; `fleet report` aggregates all
`*.fleet.json` records and includes per-project rows.

### Real Openness pair

`PlantB2` is a real TIA V21 project created through Openness (a copy of
`PlantB` with seven additional SCL blocks):

- CPU 1515-2 PN `OrderNumber:6ES7 515-2AN03-0AB0/V4.1`
- tags `Start_B` `%I0.0`, `Stop_B` `%I0.1`, `Motor_B` `%Q0.0`,
  `Fault_B` `%Q0.1`
- external SCL source `FC_LogicB` containing
  `Motor := Start AND NOT Stop; Fault := Stop;`
- generated block and compiled with `ICompilable.Compile()`:
  `Success`, 0 errors, 0 warnings
- exported with `opns fs2xml`

```powershell
tia-project platform realpair `
  --project <Scratch> --fs2xml <Scratch.objectframe.xml> `
  --release-project <PlantB2> --release-export <PlantB2.objectframe.xml> `
  --compiler-export <PlantE.objectframe.xml> `
  --out out\real-pair
```

## Compiler recovery and evolutionary engineering

| Idea | Command | Artifact |
|---|---|---|
| Recover the compiler | `compiler probe/model/predict/differential/test/classic-test` | `compiler/probe.json`, `compiler/model.json`, `compiler/compiler-test.json` |
| Affinity maturation build loop | `evolve trace/run/test/recorded/recorded-test` | `evolve/trace.json`, `evolve/evolve.json`, `evolve/evolve-test.json`, `evolve/recorded/recorded-evolve.json` |
| Thymic negative selection | `thymus train/scan` | `thymus/repertoire.json`, `thymus/scan.json` |
| Hippocampal replay | `replay consolidate` | `replay/consolidated.json` |
| Quorum sensing on DB writes | `contracts discover` | `contracts/contracts.json` |
| Ant-colony midden | `midden analyze` | `midden/midden.json` |

`compiler probe` builds a differential corpus: observed source/MC7 pairs from
`CompileUnitData.Data` plus `LoadablePlusCodeBlockData.OpenImmediateData`, and
20 synthetic SCL->MC7 probes encoded from the MC7+ opcode table. Each row
carries source features (statements, assignments, branches, calls, symbols,
constants, operator counts, depth) and MC7 features (byte length, instruction
count, opcode histogram, operand kinds). `compiler model` fits a ridge
regression from source features to total length and per-opcode counts;
`compiler predict` returns length, opcode mix, and confidence; `compiler
differential` compares predictions against observed MC7. Leave-one-out
validation passes 20/20 probes.

`evolve` scores AST mutants against an I/O trace (`inputs` + `expected`
outputs) with the SCL interpreter, not body length. Mutations are semantic
(operator flip, constant substitution, statement delete/duplicate, symbol
rename); `evolve test` proves a 0.5-fitness OR implementation evolves to a
1.0-fitness AND implementation under an AND truth table. `evolve trace`
generates a golden trace from a block for the platform run. `thymus` trains
n-gram detectors on mutants and filters them against legitimate bodies.
`contracts` proposes a contract for symbols used by at least N blocks.
`midden` clusters unreferenced objects.

## Deployment supply chain

| Idea | Command | Artifact |
|---|---|---|
| Returns desk | `deploy rollback` | `rollback/rollback-manifest.json`, `payload/` |
| Last-mile proof-of-delivery | `deploy pod`, `deploy verify-pod` | `pod/pod.json` |
| Bonded warehouse | `warehouse add/release/list/review/umac/test` | `warehouse/list.json`, `warehouse/umac-review.json`, `warehouse/warehouse-test.json` |
| Customs brokerage | `customs classify` | `customs/tariff.json` |
| LTL consolidation hub | `deploy consolidate` | `deploy/consolidated.json` |
| Detention/demurrage | `queue age` | `queue/invoice.json` |

The rollback package copies PLF/IDX/XRef/export into `payload/` and seals the
manifest. POD binds an artifact hash to a CPU serial and rack slot and signs it.
The warehouse quarantines a vendor artifact until named tests pass and an
approved, hash-bound UMAC review artifact matches the artifact hash, UMAC
digest, and object count. `warehouse review` creates the review from the
export's UMAC object set and can sign it with the registry key;
`warehouse release` rejects missing, rejected, wrong-digest, tampered, or
wrong-artifact reviews.

## Evidence from the Scratch run

- 29/29 steps pass.
- Gate self-check: `allow`; bisect found `Main` versions across savepoints.
- Merge self-check: 1,317 objects, 0 conflicts.
- Recorded flight recorder: 8 timestamped samples + 8 alarms ingested from
  `io.csv`/`alarms.csv` and bound to savepoints 0-7. Replay resolves the exact
  savepoint state ID, attributes 5/5 rule violations (`Tag_1 >= 5`), and
  `state-0 != state-5`. Negative ingestion cases reject unknown tags,
  non-monotonic timestamps, pre-savepoint samples, alarm tags that do not
  exist, tampered savepoint state hashes, and unknown alarm savepoints.
- Recorded evolution (Scratch): baseline 1.00, statement-delete mutant 0.00,
  best 1.00 from the ingested trace.
- Recorded evolution (PlantB `FC_LogicB`): 8 recorded samples, inputs
  `Start_B->Start`, `Stop_B->Stop`, outputs `Motor_B->Motor`,
  `Fault_B->Fault`; baseline 16/16 (1.00), statement-delete mutant 8/16
  (0.50), best 1.00.
- Programmed forgetting: executed against a project copy. Kept savepoints
  `0,6,7`, tombstoned `1–5`; rebuilt PLF = 1,309,898 bytes / 1,383 records;
  rebuilt IDX = 105,896 bytes / 2,760 entries; new history = 3 savepoints,
  1,376 live objects. All 10 verification checks pass (hashes, trailing
  bytes, GUIDs, index completeness, savepoint count, latest state keys).
- Ontology: 25 nodes, 3 edges; benchmark 5/5.
- Control IR: 2 units, 2/2 body hashes verified.
- Control IR round-trip (Scratch): 2 units, 2 lowered, 2/2 schema-valid,
  2/2 body, 2/2 interface, MC7 1/1, tags 2/2, projection hash
  `d380ddfa...` identical before/after.
- Control IR round-trip (PlantB): 2 units, 2/2 schema-valid, 2/2 body,
  2/2 interface, MC7 2/2, tags 4/4, projection hash `85f212b0...`
  identical before/after.
- CI: pass (PLF, XRef, VCI, gate, ontology, IR).
- Headless Linux CI: `ci linux` cross-published a self-contained `linux-x64`
  binary, ran it under WSL, and `ci run` passed all 6 checks on `PlantB2`
  (898 records/835 live, 14 XRef parts/27 relations, 29 ontology nodes/27
  edges, 9 IR units/8 bodies verified) with no TIA Portal process or GUI.
- Register/memory symbolic execution: 11 MC7 steps (8 known, 3 unknown) over
  RLO, 32-bit accumulator, bit memory, byte memory, and guarded writes.
  `Main` = `Main` gives Z3 QF_BV `unsat`; `Main` != `Block_1` gives `sat`
  (`no-mc7-path`); `Main` != a one-byte-mutated `Main` gives `sat` with
  `register-memory-state-difference` at offset 11 and a 677-char Z3 model.
  The opcode/byte path-equality certificate and graph bisimulation remain as
  secondary checks; `certify bisim` now defaults to the cycle-for-cycle
  observational checker described below.
- Cycle-for-cycle equivalence: 12/12 self-test cases pass. Positive:
  commuted `AND` is equivalent for 3/3 cycles with `complete=true`; a hidden
  temp write is equivalent when only `0x10` is observed and non-equivalent
  when `0x20` is also observed; a `JMP` branch program is equivalent to its
  branchless form with `pairs=6` and `complete=true`. Negative:
  `AND` vs `OR` gives `sat` at cycle 0 with `rlo` and `bit 0x10` differing;
  a toggle vs a straight store differs only at cycle 1 (initial `0x10=0`),
  matching concrete replay `1,1` vs `1,0`; real `FC_LogicB` self-compare is
  `unsat` for 3/3 cycles, while its one-byte mutant is `sat` at cycle 0.
  A non-terminating `JMP` loop is reported `equivalent=true` but
  `complete=false`, `definitive=false`, so no exactness is claimed.
- Registry: manifest signed and verified.
- Market: positive `Main` package has schema-valid contract, interface/effects
  verified, Z3 pre/post `unsat`, VCI provenance `match` for
  `System/PEData.plf`, and a valid ECDSA signature. Negative cases: contract
  contradiction `sat`, bogus interface section, wrong VCI hash, and
  signed-but-invalid contract all fail both publish verification and
  buyer-side re-verification. Artifacts: `market/market-test.json`,
  `market/package.json`, `market/contract.schema.json`, `market/vci-good.db`,
  `market/vci-bad.db`.
- Real pair: `Scratch` and `PlantB2`. `PlantB2` is a copy of the Openness
  project `PlantB` with seven additional SCL blocks (`FC_And`, `FC_Or`,
  `FC_Not`, `FC_Xor`, `FC_Mux`, `FC_Cmp`, `FC_Add`), so it contains
  `FC_LogicB` plus seven new FCs and `Main`. All eight SCL blocks compiled
  with `Success`, 0 errors, 0 warnings.
- Classic compiler project: `PlantE` is an Openness-created CPU 315-2 PN/DP
  project with tags `Start_C`, `Stop_C`, `Motor_C`, `Fault_C` and the same
  eight SCL functions. It compiled with `Success`, 0 errors, 0 warnings. Its
  classic MC7 is unencrypted and is the real codegen corpus used by the
  canonical `--compiler-export` run.
- Drift: `breaking` with 46 removed object types and 1 added type; XRef
  counts moved `parts 21 -> 14`, `objs 15 -> 37`, `rels 12 -> 27`,
  `addrs 2 -> 4`.
- Corpus: 2 projects, 2,392 records, 2,211 live, 11 blocks, 6 tags,
  1 screen, 35 XRef parts, 39 relations; both per-project manifests verify.
- Fleet (2-project run): 2 records, avgBlocks 5.5, avgTags 3.0, both projects
  in `byFormat` `0.1400.1.0`; per-project rows are retained in the report.
- Cross-plant fleet: 4 real Openness projects (`Scratch`, `PlantB2`, `PlantC`,
  `PlantD`) with recorded failure-pattern CSVs. Aggregation found 4 patterns,
  3 shared across plants: `overload` (PlantB2/PlantC/PlantD, 4 events),
  `interlock-open` (PlantB2/PlantC/PlantD, 3 events), `high-level`
  (Scratch/PlantC, 3 events); `overheat` is PlantD-only. `fleet pattern-test`
  passes with the four positive records and four rejected negative CSVs.
- `PlantC` is a CPU 1511-1 PN project with tags `Pump_Run`, `Pump_Fault`,
  `Level_High`, `Valve_Open` and SCL `FC_Pump`/`FC_Level`; `PlantD` is a CPU
  1517-3 PN/DP project with `Fan_Run`, `Fan_Trip`, `Temp_High`, `Door_Open`
  and SCL `FC_Fan`/`FC_Temp`. Both compiled with `Success`, 0 errors,
  0 warnings.
- Compiler on `PlantE` (classic S7-300): observed=8, withMc7=8,
  `observed.distinctMc7=8`, `observed.distinctCodeHashes=8`,
  `realCodegen=true`, modelRows=27, R2=0.9969,
  RMSE=0.72, synthetic-probe leave-one-out 18/20 length and 19/20 opcodes,
  observed-block leave-one-out 7/8 length and 7/8 opcodes, differential
  8 rows with MAE 0.875 / Jaccard 0.768. The eight observed blocks are real
  SCL-to-MC7 pairs (`FC_LogicB`, `FC_And`, `FC_Or`, `FC_Not`, `FC_Xor`,
  `FC_Mux`, `FC_Cmp`, `FC_Add`); their code lengths are 42, 26, 26, 22, 26,
  46, 28, and 28 bytes.
- `compiler classic-test` is the explicit classic codegen gate: it passes
  with `observed=8`, `withMc7=8`, `observed.distinctMc7=8`,
  `realCodegen=true`, and observed LOO 7/8. A negative extraction test
  truncates `FC_And`'s `BinaryData` to 80 bytes; `compiler probe` then reports
  `withMc7=7` and `FC_And` has 0 MC7, so the extractor rejects invalid
  ranges instead of inventing code.
- `PlantE` is a real TIA V21 classic project: CPU 315-2 PN/DP
  `OrderNumber:6ES7 315-2EH14-0AB0/V3.2`, tags `Start_C` `%I0.0`,
  `Stop_C` `%I0.1`, `Motor_C` `%Q0.0`, `Fault_C` `%Q0.1`, and eight SCL
  blocks compiled with `Success`, 0 errors, 0 warnings. Classic S7-300 code
  is stored unencrypted in `LoadableCodeBlockData.BinaryData`; the analyzer
  reads `BlockDebugInformation.EndSAC`, extracts
  `BinaryData[78..78+EndSAC+1]`, and decodes it with TIA's
  `AwlRegenerator.RegenerateExpanded`.
- `PlantB2` (S7-1500 plus code) remains limited: observed=8, withMc7=8,
  `observed.distinctMc7=1`, `realCodegen=false`. All eight plus blocks carry
  the same 12-byte `OpenImmediateData` `1500030030490000000802D5`; `MC7Host`
  reports no instructions for it, and the `MC7PlusData` sections
  (16/27/48 bytes) fail operand decoding. `PlcBlock.Export(WithReadOnly)`
  exposes `LoadMemoryLength` and compile dates but not code. The plus-code
  path is protected; the classic S7-300 path is the real codegen corpus.
- PLCSIM read-back attempt (2026-11-14): a copy `PlantB2-plcsim` was opened
  through Openness; `PROFINET interface_1` node `X1` was connected to a new
  `PROFINET_1` subnet and set to `192.168.0.1/24`. The software compiles
  (`Success`, 0 errors), but hardware compile still reports 2 errors asking
  for the password for confidential PLC configuration data, and the download
  configuration target `1 X1` still has 0 addresses; `ApplyConfiguration`
  rejects a created address with "The configuration provided is invalid".
  The PLCSIM V21 API can power on an S7-1500 vPLC
  (`Siemens.Simatic.PlcSim.Vplc1500.dll`, state `Stop`, `IsPoweredOn=true`)
  and set IP `192.168.0.1`, but TIA Openness download then fails with
  `Connect to module PLC_B failed`. The API-created vPLC is not registered
  with TIA's PLCSIM PC interface; a TIA UI "Start simulation" download (or a
  PLCSIM instance created by the TIA UI) is required for read-back.
- Scratch compiler: observed=2, probes=20, withMc7=1, model R2=0.9953,
  RMSE=0.22, leave-one-out 20/20; `Block_1` predicted at 12 MC7 bytes;
  observed `Main` differential is exact (MAE 0, opcode Jaccard 1.0).
- Evolve: trace-fitness self-test starts at 0.50 (OR) and selects
  `operator-flip:OR->AND` at 1.00; `Block_1` golden trace baseline 1.00,
  best 1.00, statement deletion drops to 0.00.
- Thymus: 16 detectors, 0 flagged legitimate blocks.
- Replay: 8 savepoints, 10 stable objects, 1,379 one-off.
- Contracts: 1 contract (`Test` used by `Block_1`, minBlocks=1).
- Midden: 2 orphans (`CorePersistenceInfo`, `MddCache`).
- Deployment: rollback sealed, POD verified, warehouse UMAC review gate
  passed with 33 UMAC objects (digest `6bc5a50dd383...`); positive release
  succeeds, while missing, rejected, wrong-digest, tampered, and
  wrong-artifact reviews are all rejected. Tariff
  `TC-LAD-03-00-8df95848`, 3 changes -> 2 batches, queue invoice computed.

## Synthetic negative-path evidence

The self-check run is intentionally clean. Separate synthetic fixtures exercise
the blocking paths:

- `out/platform-test/gate/gate.json` — verdict `block`, high=2: `Block_1`
  removed and `Tag_1.MaxLimit` widened from 100 to 200.
- `out/platform-test/merge2.json` — 1 conflict: both sides changed `Tag_1`.
- `out/platform-test/drift-report.json` — severity `breaking`: PLF format
  version changed from `0.1400.1.0` to `1400.2.0.0`.
- `out/platform-test/ci-scaffold/` - Dockerfile, GitHub Actions, Azure
  Pipelines, and `tia-project.ci.json`.
- `out/market-test/market-test.json` - positive contract/provenance/signature
  pass; contract contradiction `sat`, bogus interface section, wrong VCI
  hash, and buyer-side re-verification all fail.
- `out/market-e2e/` - CLI publish/verify with a good VCI fixture; verify fails
  with the bad VCI and with a tampered package.
- `out/warehouse-test/warehouse-test.json` - UMAC review gate: positive
  release; missing, rejected, wrong-digest, tampered, and wrong-artifact
  reviews all rejected.

## Limits

- The Scratch project has no safety program, no drives, and an empty VCI
  `FileRecord` table, so those exports correctly report zero. The marketplace
  test therefore creates a copied VCI fixture with an inserted `FileRecord`
  for `System/PEData.plf`; the positive package binds to that fixture, and the
  negative case uses a wrong hash. On a project with a populated VCI table the
  same path binds to the real record.
- The contract expression language is `RLO`-based (`true`, `false`, `RLO`,
  `!`, `&&`, `||`, `==`, `!=`, parentheses). Interface and effect checks cover
  section/member names and block symbols; typed member ranges, tag memory, and
  branch conditions are not yet part of the contract grammar.
- MC7 disassembly is call-statement focused; `Main` has no calls. The
  register/memory checker decodes known MC7+ opcode patterns and executes
  them over symbolic RLO, accumulator, bit-memory, and byte-memory state.
  Unknown opcodes are explicitly counted and havoc the state; this is a
  sound over-approximation for the modeled subset. Branch guards are
  represented on conditional writes (`ST`, `STN`, `ST_LD`, `S`, `R`).
  The Z3 QF_BV check is real, but it is not a full MC7 ISA model: operand
  encodings outside the known table and indirect addressing remain
  over-approximated.
- Cycle-for-cycle equivalence shares nondeterministic choices for unknown
  opcodes by `(offset, opcode)`, so identical unknown instructions compare
  equal while different ones are conservatively non-equivalent. `JMP` targets
  are read as absolute block offsets; unresolved targets end the cycle.
  Branching is enumerated exactly, but a loop makes the scan cycle
  non-terminating, so the result is reported bounded (`complete=false`) rather
  than exact. Bit and byte memory are observed at explicit addresses, not as
  full arrays.
- The recorded trace fixture uses real project tag names and real WinCC
  alarm texts extracted from the `CC_HMI_W5ND_26_11_01_13_59_26` CS backup,
  but the runtime tag-log archive segments were not present in the project
  folder, so the sample values are a metadata-derived sequence. The
  `flightrec ingest` path accepts the same CSV shape from a real historian;
  point `--samples`/`--alarms` at that export to replay actual plant data.
  Replay resolves the exact state ID for each savepoint;
  `tag-not-present-at-savepoint` and `savepoint-state-mismatch` are
  first-class findings.
- The cross-plant fleet has four real Openness projects, but its failure
  patterns are recorded fixture CSVs assembled from project tag names and
  WinCC alarm texts, not a live historian export. The aggregation itself is
  real across four projects; point each `patterns` entry in the fleet spec at
  a real alarm export to use production failure data.
- The ontology and IR are derived from the decrypted FS2XML export; indirect
  addressing remains an explicit coverage gap.
- The IR round-trip lowers SCL to `StructuredText` tokens (`Access`, `Token`,
  `Text`), not to regenerated SCL source text. Classic `LAD_CLASSIC` FlgNet
  (version 14.0.0.2) is converted to the V5 `FlgNet` dialect; operand names
  come from `IdentPartData`, with `Ref_<id>` fallback when no table matches.
  XSD validation strips namespace URIs because the vendor schemas are
  namespace-less, so it validates element/attribute structure rather than
  namespace bindings.
- The forgotten project copy is rebuilt as complete state snapshots for each
  kept savepoint; this preserves verified history but duplicates shared
  objects across snapshots, so the rebuilt IDX can be larger than the
  original append-only index. The rebuilt PLF/IDX are verified by this
  toolkit; TIA round-trip validation is still a separate gate.
- Compiler recovery now has eight real classic S7-300 SCL-to-MC7 pairs in
  `PlantE` (`observed.distinctMc7=8`, `realCodegen=true`, observed LOO 7/8)
  and one observed LAD block in Scratch. The S7-1500 `PlantB2` plus-code
  path remains encrypted: its eight blocks share a 12-byte
  `OpenImmediateData` stub, and `MC7PlusData` fails operand decoding. The 20
  synthetic SCL->MC7 probes are still encodings from the MC7+ opcode table
  and remain auxiliary rows; the eight classic SCL->MC7 pairs are the real
  codegen corpus. The model predicts length and operation mix rather than
  full operand encoding, branch targets, or register allocation. Plus-code
  recovery still needs a TIA decryption/export path or a CPU/PLCSIM
  read-back.
- Headless Linux CI is read-only: it verifies exported PLF/IDX, XRef, VCI,
  ontology, semantic gate, and IR. Creating projects, generating blocks, and
  compiling SCL still require the licensed TIA Portal process; `ci linux`
  does not replace the Openness create/compile half.
- The evolution interpreter covers assignments, boolean/arithmetic/comparison
  expressions, and simple `IF` branches. Calls and complex control flow are
  counted as unmodeled and penalized in fitness; real plant I/O traces use the
  same `inputs`/`expected` JSON shape as the self-test.
- The second project is a real Openness-created TIA V21 project with its own
  CPU, tags, and compiled SCL block. Both projects are TIA V21, so the drift
  report reflects project-content/product-set drift, not a different TIA
  release. Creating it requires a TIA Portal UI session and granting the
  Openness access prompt (`Yes to all`).
