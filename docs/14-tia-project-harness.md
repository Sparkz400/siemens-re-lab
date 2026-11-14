# TIA project harness (30 ideas)

`tools/tia-project` is a .NET 10 CLI that implements the 30 platform ideas as
real command surfaces. It is the integration harness for the classic MC7,
Openness, HMI, history, corpus, compiler-recovery and deployment experiments.

It is not a second copy of the four lab goals. It is the measurement and
automation layer that produces the evidence in [`evidence/`](../evidence) and
the MC7 findings in [13-mc7-codegen.md](13-mc7-codegen.md).

## Build

```powershell
dotnet build .\tools\tia-project\TiaProject.csproj -c Release
.\tools\tia-project\bin\Release\net10.0\tia-project.exe --help
```

The project needs the .NET 10 SDK. Openness commands need a running TIA Portal
and the V21 PublicAPI; the pure XML/SQLite commands do not. If TIA is installed
outside `C:\Program Files\Siemens\Automation\Portal V21`, pass
`--bin '<TIA_BIN>'` to the commands that read Openness schemas.

## Command map

The full command map, artifact names and measured numbers are in
[`tools/tia-project/PLATFORM.md`](../tools/tia-project/PLATFORM.md). The main
groups are:

| Track | Ideas | Commands |
|---|---|---|
| Semantic change control | behavioral bisect/safety gate, Git merge driver, forensic timeline, flight recorder, programmed forgetting | `gate`, `merge`, `forensics`, `flightrec`, `forgetting` |
| Queryable project | ontology, agent API/refactoring benchmark, vendor-neutral IR, LLVM-style IR, headless CI | `ontology`, `api`, `ir`, `ir roundtrip`, `ci` |
| Proof and provenance | equivalence certifier, bisimulation oracle, signed registry, verified marketplace | `proof`, `bisim`, `registry`, `market` |
| Corpus and drift | format-drift tripwire, golden corpus, mutant corpus, fleet corpus | `drift`, `corpus`, `fleet`, `fleet pattern-test` |
| Compiler recovery | compiler probe/model/differential, affinity maturation, thymic selection, replay, quorum, midden | `compiler`, `evolve`, `thymus`, `replay`, `contracts`, `midden` |
| Deployment supply chain | returns desk, proof-of-delivery, bonded warehouse, customs, LTL, demurrage | `deploy`, `pod`, `warehouse`, `customs`, `ltl`, `queue` |

## Status

| Run | Result | Evidence |
|---|---|---|
| `platform run` (Scratch) | 29/29 pass | [`evidence/platform-all`](../evidence/platform-all) |
| `platform run --compiler-export PlantE` | 29/29 pass | [`evidence/platform-classic`](../evidence/platform-classic) |
| `platform realpair` (4-plant fleet) | 8/8 pass | [`evidence/realpair`](../evidence/realpair) |
| `compiler classic-test` | `observed=8`, `withMc7=8`, `distinct=8`, `realCodegen=true`, observed LOO 7/8 | [`evidence/plantE-classic-gate`](../evidence/plantE-classic-gate) |
| Negative extraction | truncated `FC_And` -> `withMc7=7`, `FC_And` 0 MC7 | [`evidence/plantE-negative2`](../evidence/plantE-negative2) |

## What is real, what is still a fixture

- **Real:** classic S7-300 SCL-to-MC7 pairs, code hashes, statement counts,
  classic compiler gate, Openness project creation/compilation, PLF/IDX/XRef
  parsing, objectframe XML parsing, HMI SQL/screen/tag/SVG export, savepoint
  history, real-pair drift/corpus/fleet.
- **Fixture:** the flight-recorder sample values are metadata-derived; no
  runtime historian tag-log archive is present. The 20 synthetic SCL->MC7
  probes are auxiliary rows, not observed code.
- **Blocked:** S7-1500 plus-code decryption, PLCSIM V21 block download/upload
  read-back, and genuine cross-release format drift (only Portal V21 is
  installed).

See [15-decompilation-status.md](15-decompilation-status.md) for the surface-by-surface
status.

## Relationship to the four goals

- Goal 1 (snapshot/diff): `tia-project` supplies the block/tag/XRef/HMI
  extraction and the semantic diff/gate commands. The repo's Python
  `probe/tia_snapshot.py` remains the portable snapshot path.
- Goal 2 (S7comm-plus): the classic MC7 decoder and the plus-code findings are
  the compiled-code reference. The S7comm-plus client itself is still the
  planned track.
- Goal 3 (WinCC Unified): the harness has HMI SQL/screen/tag/SVG exports; the
  live GraphQL logger remains the repo's probe path.
- Goal 4 (panel backups): not covered by this harness.

## Limits

- The harness is not a vendor binary redistribution. It reads a local TIA
  installation and generated lab projects.
- Project archives and objectframe exports are not committed; see
  [`evidence/README.md`](../evidence/README.md) for the sanitized summaries.
- The plus-code path is encrypted and blocked; the harness does not claim
  otherwise.
- `dotnet build` reports NuGet audit warnings for a transitive
  `System.Security.Cryptography.Pkcs` 9.0.4 dependency; the build succeeds.
  Treat the warning as a dependency-maintenance item, not a harness failure.
