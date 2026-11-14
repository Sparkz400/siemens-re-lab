---
tags: [tia, roadmap, a-b-c]
created: 2026-11-14
---

# Roadmap A B C

## A — engineering data

- [x] PLF/IDX/XRef parsing
- [x] Openness objectframe XML graph
- [x] SimaticML export
- [x] HMI SQL/screens/tags/SVG
- [x] Hardware, safety/UMAC/drives, VCI
- [ ] Full add-on package coverage (Startdrive, Safety, Test Suite)
- [ ] Cross-release project corpus

## B — compiled artifacts

- [x] Classic S7-300 MC7 (8 SCL FCs, distinct hashes, statement lists)
- [x] Negative extraction test
- [ ] S7-1500 plus MC7 at rest
- [ ] S7-1500 plus MC7 runtime capture
- [ ] S7-1200 plus MC7
- [ ] HMI JavaScript / runtime package
- [ ] Drive/safety compiled artifacts

### Plus-code experiment order

1. [x] Non-optimized S7-1500 block — **negative**: still `PLUSBLOCK`.
2. [x] `MC7PlusBinaryReader` with `OpenImmediateData` — **negative**: not sufficient.
3. [x] Static CSI route (`CsiStreamImpl` / `CsiVerifierImpl` / `CsiWrapper`) - **negative**: write-only encoder + verifier, no decrypt.
4. [ ] **UI call path** - TIA loads `Siemens.Simatic.Lang.Mc7PlusDisassembler.dll` and calls `Mc7PlusDisassember.Disassembler.Disassembler.GetDisassembledCode()` with `GetMC7Plus()` + `ImmediateLongTable`. The DLL is **missing** on this install. Obtain it from a TIA update/optional package/another release.
5. [ ] `GetMC7Plus(PlusBinaryType.Simulation)` / `VPlc` - different dependent blob.
6. [ ] Runtime hook of `Disassembler.DisassembleCallStatements`.
7. [ ] PLCSIM/CPU memory dump after download.

## C — TIA Portal and add-ons

- [x] Assembly inventory (managed/native, size, version, type count)
- [ ] Curated decompilation of Openness, block logic, MC7, HMI, PLCSIM, CSI
- [x] Call-path map for plus-code decryption/disassembly
- [ ] Add-on package inventory (Startdrive, Safety, WinCC Unified, Test Suite)
- [ ] Native exports/entry-point map (CSI, compiler, runtime)

## Milestones

| Milestone | Definition |
|---|---|
| M1 | Plus code captured at runtime for at least one block |
| M2 | Plus corpus of 8+ blocks with decoded statements |
| M3 | PLCSIM/CPU memory read-back path documented |
| M4 | Cross-release corpus (V15.1–V21) |
| M5 | Add-on coverage matrix |

## Rule

Vendor decompiled source stays local. The repo gets metadata, scripts, hashes,
sanitized evidence and notes — not Siemens binaries or decompiled vendor code.
