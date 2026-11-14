# Decompilation roadmap A/B/C

**Status: 2026-11-14.** This is the working roadmap for artifact-level
decompilation of TIA Portal and its add-ons.

The detailed lab notes live in the Obsidian vault at
[`obsidian/siemens-re-lab/`](../obsidian/siemens-re-lab). The repo keeps the
scripts, sanitized evidence and status; vendor decompiled source stays local.

## Three layers

| Layer | Target | Current coverage |
|---|---|---|
| **A. Engineering data** | blocks, tags, screens, hardware, safety, drives, history | 85–95% via Openness + PLF/IDX/XML |
| **B. Compiled artifacts** | classic MC7, S7-1500 plus MC7, HMI JS, drive/safety code | classic ~100%; plus blocked at rest, runtime routes open |
| **C. TIA/add-ons** | managed/native application internals | inventory started; selected assemblies decompiled locally |

## A — engineering data

Already covered by `tools/tia-project` and the existing probes:

- PLF/IDX/XRef parsing
- Openness objectframe XML graph
- SimaticML LAD/SCL export
- HMI SQL/screens/tags/SVG
- hardware, safety/UMAC/drives, VCI
- savepoint history and programmed forgetting

Remaining: full add-on package coverage and a cross-release corpus.

## B — compiled artifacts

### Classic MC7

Fully decoded for the eight SCL FCs:

```text
observed=8  withMc7=8  distinct=8  realCodegen=True  observedLoo=7/8
r2=0.9969  rmse=0.72  mae=0.875  jaccard=0.768
```

### S7-1500 plus MC7

At rest:

- `LoadType=PLUSBLOCK`
- `OpenImmediateData` = `1500030030490000000802D5` (12 bytes, identical)
- `MC7PlusData` = 124-byte CSI envelope, `EF BE AD DE` sections `124:1, 0:32..48, 1:16..29, 2:0`
- `FamilyKey` 42 bytes, `OwnSignatureData` 48 bytes
- `MC7plus.Disassembler` / `MC7PlusBinaryReader` fail on the raw payload
- `KeyKeeper.AddKey(FamilyKey)` alone does not decrypt

Key derivation (decoded):

- `TargetSeed = BitConverter.GetBytes(project.CreationTime.ToBinary()) + ControllerTarget.Id.ToByteArray()`
- `ObjectId.ToByteArray()` = `4F 47` + type id (`u32 LE`) + instance id (`u64 LE`)
- `PublicId = KeyKeeper.AddKey(familyKey)`, `TargetId = KeyKeeper.GenKey(TargetSeed, PublicId)`
- Both ids match the container header at `0x1C` / `0x2C` exactly for
  `PlantB2 / FC_And`.
- Tools: `probe/mc7plus_keys.py`, `probe/mc7plus_keys/`.

Negative experiments:

| Experiment | Result |
|---|---|
| Non-optimized S7-1500 (`MemoryLayout.Standard`) | still `LoadablePlusCodeBlockData` / `PLUSBLOCK` |
| `MC7PlusBinaryReader` + `OpenImmediateData` / section 0 | still `Read not allowed type code` / operand errors |
| `CsiStreamImpl` / `CsiVerifierImpl` / `CsiWrapper` | write-only encoder + verifier; no `Decrypt*` export |

The official TIA plus view path is:

```text
MC7ViewLogic
  -> Siemens.Simatic.Lang.Mc7PlusDisassembler.dll
  -> Mc7PlusDisassember.Disassembler.Disassembler(GetMC7Plus(), ImmediateLongTable)
  -> GetDisassembledCode()
```

That DLL is **not installed** on this TIA V21 host. This is an artifact gap,
not a crypto dead end: the UI passes exactly the blobs we already extract.

Open leads, ranked:

1. **Obtain `Siemens.Simatic.Lang.Mc7PlusDisassembler.dll`** from a TIA
   update, optional package or another release; load it directly.
2. Runtime hook of `CsiEncoderUm.Encrypt` (native, `MC7Codegenerator.dll`) or
   `Disassembler.DisassembleCallStatements` — capture the plaintext before it
   is encrypted.
3. `GetMC7Plus(PlusBinaryType.Simulation)` / `VPlc` — different dependent blob,
   possibly recoded or unencrypted.
4. PLCSIM/CPU memory dump after download — the CPU executes plaintext MC7.
5. Static CSI decryption — **blocked**: `CsiEncoderUm` generates a fresh
   random session key per call and `CsiWrapper` exposes only
   `EncryptDataInit/Update/Final` plus a verifier.

The practical insight: TIA must decrypt plus code to compile, download,
simulate or disassemble it. The plaintext exists at runtime; capturing the
boundary is more promising than breaking the at-rest crypto.

### Other compiled artifacts

- S7-1200 plus MC7: same plus family, test after S7-1500.
- HMI Unified: GraphQL + runtime SQLite + screen model; scripts/JS still open.
- Drives/safety: Openness exports first, native formats per add-on.

## C — TIA Portal and add-ons

Inventory (`probe/tia_inventory.ps1`):

```text
files=2573  managed=2326  native=247
total=1640.8 MB  managed=931.1 MB  native=709.6 MB
```

Key managed targets:

- `Siemens.Simatic.Lang.MC7Codegenerator.dll`
- `Siemens.Simatic.Lang.MC7plusCommon.dll`
- `Siemens.Simatic.Lang.MC7plus.Internal/External/Adapter.dll`
- `Siemens.Simatic.Lang.Block.Compiler.dll`
- `Siemens.Simatic.Lang.PLInternal.dll`
- `Siemens.Simatic.Lang.BlockHandling.TIA.Impl.dll`
- `Siemens.Simatic.Lang.KnowHowProtection.dll`
- `Siemens.Simatic.Lang.Mc7PlusDisassembler.dll` — **missing on this install**;
  the official plus MC7 view assembly.

Key native target: `Siemens.DF.CsiWrapper.dll` (`InitCommonSecurity`,
`EncryptDataInit/Update/Final`, `CalculateMACHybrid`; no `Decrypt*` export).

The mixed C++/CLI `s7irsrvmc7x.dll` is the MC7 runtime; its managed surface is
mostly compiler attributes, so the MC7 logic is native inside the module.

Add-on families (`probe/tia_addon_inventory.py`): WinCC/HMI 952 files/470.5 MB,
PLC language 207/64.0 MB, hardware config 146/230.3 MB, Safety 26/11.2 MB,
drives 116/39.7 MB, CSI/security 96/13.3 MB, OPC UA 10/11.4 MB; Test Suite is
not installed. See [17-tia-addon-inventory.md](17-tia-addon-inventory.md).

Native exports (`probe/native_exports.ps1`): `s7irsrvmc7x.dll` exports 254
`IR_*` compiler/runtime functions (`IR_CodeGenerator_GenerateBinaryCode`,
`IR_CreateCompilerEnvironment`, `IR_GenerateLIRInstructions`); the motion
modules export thousands of C++ symbols. See
[19-native-exports.md](19-native-exports.md).

## Feasibility

| Target | Possibility | Route |
|---|---:|---|
| A — engineering graph | 90%+ | Openness + parsers |
| B — classic MC7 | ~100% | `AwlRegenerator` |
| B — S7-1500 plus at rest | 60–80% | container + key ids decoded; payload decrypt blocked on missing DLL |
| B — S7-1500 plus runtime | 70–90% | hook, UI view, memory |
| B — HMI Unified | 80–90% | GraphQL + runtime DB; V8 scripting stack inventoried |
| B — safety/drives | 50–80% per add-on | Openness + native RE |
| C — full IDE source | <10% | large hybrid, low value |

## Milestones

| Milestone | Definition |
|---|---|
| M1 | Plus code captured at runtime for one block |
| M2 | Plus corpus of 8+ blocks decoded to statements |
| M3 | PLCSIM/CPU memory read-back documented |
| M4 | Cross-release corpus (V15.1–V21) |
| M5 | Add-on coverage matrix |

## Rule

No Siemens binaries, project archives, licenses or decompiled vendor source in
the repo. The repo keeps metadata, scripts, hashes, sanitized evidence and
notes.
