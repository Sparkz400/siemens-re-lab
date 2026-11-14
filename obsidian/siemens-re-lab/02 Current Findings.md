---
tags: [tia, findings, mc7, hmi, openness]
created: 2026-11-14
---

# Current Findings

## A — engineering data

| Surface | Status | Evidence |
|---|---|---|
| PLF/IDX | parsed | `tools/tia-project` `plf`/`idx` |
| XRef.db | decoded | `xref` |
| Openness objectframe XML | parsed | `opns` |
| SimaticML | exported | LAD `FlgNet`, SCL `SCLSource` |
| HMI SQL/screens/tags/SVG | exported | `hmi` |
| Hardware | parsed | `hardware` |
| Safety/UMAC/drives | exported | `safety` |
| VCI | verified | `vci` |
| Savepoints/history | verified | `history`, `forgetting` |

## B — classic MC7

Fully decoded for the eight SCL FCs in `PlantE`:

```text
observed=8  withMc7=8  distinct=8  realCodegen=True  observedLoo=7/8
r2=0.9969  rmse=0.72  mae=0.875  jaccard=0.768
```

Code slices:

```text
BinaryData[78 : 78 + EndSAC + 1] -> AwlRegenerator
```

Negative test: truncated `FC_And` -> `withMc7=7`, 0 MC7. The extractor rejects
invalid ranges.

## B — S7-1500 plus MC7

Blocked at rest:

- `LoadType=PLUSBLOCK`
- `OpenImmediateData` is 12 bytes and identical for every block:
  `1500030030490000000802D5`
- `MC7PlusData` is `EF BE AD DE`-framed:
  `124:1, 0:32..48, 1:16..29, 2:0`
- `MC7plus.Disassembler` and `MC7PlusBinaryReader` fail on the section bytes
- `KeyKeeper.AddKey(FamilyKey)` does not unlock the disassembler
- `FamilyKey` is 42 bytes; `OwnSignatureData` is 48 bytes
- `distinctMc7=1`, `realCodegen=false`

## B — PLCSIM V21

- API-created S7-1500 vPLC powers on (`Stop`, `IsPoweredOn=true`), IP set to
  `192.168.0.1`.
- TIA Openness download target `1 X1` still has 0 addresses.
- `ApplyConfiguration` rejects a created address.
- Download fails: `Connect to module PLC_B failed`.
- TIA UI **Start simulation** is the remaining read-back route.

## Cross-release drift

Only Portal V21 is installed. Drift is V21-vs-V21, not cross-release.

## Links

- `docs/13-mc7-codegen.md`
- `docs/15-decompilation-status.md`
- `evidence/plantE-classic-gate/`
- `evidence/plantE-negative2/`
