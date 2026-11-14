# Decompilation status

This is the surface-by-surface status of the lab. "Decompiled" means the data
was parsed and, where applicable, lifted to a typed representation with a
verified round-trip. It does not mean every Siemens format is decrypted.

## Status matrix

| Surface | Status | What was verified | Where |
|---|---|---|---|
| `PEData.plf` | Parsed | Records, live/history split, object hashes, trailing bytes | `tools/tia-project` `plf` |
| `PEData.idx` | Parsed | Entries, commit points, exact EOF | `tools/tia-project` `idx` |
| `XRef.db` | Decoded | Parts, objects, relations, addresses; PLF cross-check | `tools/tia-project` `xref` |
| Openness objectframe XML | Parsed | 1,317 objects, block/tag/HMI/hardware graph | `tools/tia-project` `opns` |
| SimaticML | Exported | LAD `FlgNet` and SCL `SCLSource` bodies | `tools/tia-project` `opns` / `simaticml` |
| Classic S7-300 MC7 | **Fully decoded** | Eight SCL FCs, 42/26/26/22/26/46/28/28 bytes, eight distinct hashes, statement lists | [13-mc7-codegen.md](13-mc7-codegen.md) |
| S7-1500 plus MC7 | **Blocked** | `MC7PlusData` is `EF BE AD DE`-framed but encrypted; `Disassembler`, `MC7PlusBinaryReader`, `KeyKeeper` do not decode it | [13-mc7-codegen.md](13-mc7-codegen.md) |
| HMI SQL / screens / tags / SVG | Exported | SQL schema, screen/tag/SVG exports | `tools/tia-project` `hmi` |
| Hardware topology | Parsed | Devices, modules, interfaces, addresses | `tools/tia-project` `hardware` |
| Safety / UMAC / drives | Exported | Safety, UMAC and drive data exports | `tools/tia-project` `safety` |
| VCI verification | Verified | SHA-256/SHA-1/MD5/SHA-512 file hashes | `tools/tia-project` `vci` |
| Savepoint history | Verified | History/savepoints, programmed forgetting, verification | `tools/tia-project` `history`, `forgetting` |
| Flight recorder | Fixture | 8 timestamped samples + 8 alarms, 5/5 violations attributed | `tools/tia-project` `flightrec` |
| PLCSIM V21 read-back | **Blocked** | API-created vPLC powers on but is not reachable through TIA's `PLCSIM` PC interface; Openness download fails `Connect to module PLC_B failed` | [13-mc7-codegen.md](13-mc7-codegen.md) |
| Cross-release drift | **Blocked** | Only Portal V21 is installed; drift is V21-vs-V21, not cross-release | [13-mc7-codegen.md](13-mc7-codegen.md) |

## What "fully decoded" means for classic S7-300

The classic path is complete for the eight generated SCL functions:

```text
BinaryData[78 : 78 + EndSAC + 1]
  -> AwlRegenerator.RegenerateExpanded
  -> address, byte length, operator, access, range, width, byte/bit, constant
```

Measured evidence:

```text
observed=8  withMc7=8  distinct=8  realCodegen=True  observedLoo=7/8
r2=0.9969  rmse=0.72  differential.mae=0.875  differential.jaccard=0.768
```

The negative test truncates `FC_And`'s `BinaryData` from 126 to 80 bytes and
gets `withMc7=7`, `FC_And` 0 MC7. The extractor rejects invalid ranges instead
of inventing code.

## What is not decoded

- S7-1500 plus MC7 is encrypted at rest. The 12-byte `OpenImmediateData` stub
  `1500030030490000000802D5` is identical for every block and is not code.
- The public `KeyKeeper`/`FamilyKey` path does not unlock the disassembler.
- PLCSIM V21 exposes runtime control, not block download/upload.
- A genuine second TIA release is not installed, so cross-release drift is not
  measured.

Closing the plus-code gap needs one of:

1. A TIA decryption/export path that the public Openness surface does not
   expose.
2. A CPU/PLCSIM read-back where TIA creates and registers the simulation
   instance (the UI **Start simulation** path), followed by a station upload.
3. A genuine second TIA release for cross-release drift.

The classic path remains the real codegen corpus until one of those is
available.
