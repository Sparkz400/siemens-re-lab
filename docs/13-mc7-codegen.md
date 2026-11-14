# MC7 codegen: classic S7-300 vs plus S7-1500

**Status: 2026-11-14.** Measured on TIA Portal V21 (`2100.0.121.1`), Openness
V21, STEP 7 Professional. Two generated lab projects were used:

| Project | CPU | Code format |
|---|---|---|
| `PlantE` | CPU 315-2 PN/DP `6ES7 315-2EH14-0AB0/V3.2` | classic MC7, unencrypted in `BinaryData` |
| `PlantB2` | CPU 1515-2 PN `6ES7 515-2AN03-0AB0/V4.1` | plus MC7, encrypted in `MC7PlusData` |

Both projects contain the same eight SCL functions (`FC_LogicB`, `FC_And`,
`FC_Or`, `FC_Not`, `FC_Xor`, `FC_Mux`, `FC_Cmp`, `FC_Add`). The classic project
is the readable reference; the plus project is the target but its code is
encrypted at rest.

No Siemens binaries, project archives, licenses or customer data are committed.
The probes use a local TIA installation and generated lab projects only.

## Why this matters

- Goal 1 (snapshot/diff) sees block XML, not the compiled MC7 that runs on the
  CPU.
- Goal 2 (S7comm-plus) needs to know what the CPU actually executes.
- Classic S7-300 MC7 is a small, decodable reference implementation of the
  instruction encoding. It is useful for validating a reader or a compiler
  model before attacking the plus format.

## Classic S7-300 path

A classic block is stored as
`Siemens.Simatic.Lang.Model.Blocks.LoadableCodeBlockData` (objectframe TypeId
`2232330`). Relevant attributes:

| Attribute | Meaning |
|---|---|
| `BinaryData` | `coreblob`: 78-byte loadable header, code, trailer |
| `BlockDebugInformation` | `coreblob`: XML with `<Net ... EndSAC="N" />` |
| `BlockNumber` | FC/OB number |

The code slice is:

```text
code = BinaryData[78 : 78 + EndSAC + 1]
```

TIA's own `Siemens.Simatic.Lang.CodeGenClassic.DOL.AwlRegenerator` decodes the
slice into `IExpandedStatement` objects. The host in
[`probe/mc7_classic_host`](../probe/mc7_classic_host) exposes that API as JSON;
[`probe/mc7_classic_extract.py`](../probe/mc7_classic_extract.py) extracts the
slice from an objectframe XML export.

### Measured classic corpus

All eight functions compiled with `Success`, 0 errors, 0 warnings. The gate
numbers come from a local `tia-project compiler classic-test` run over the
objectframe export; the raw probe/model artifacts stay local and are not
committed. The extractor and decoder in this repo reproduce the code slices and
statement counts.

The code gate is `compiler classic-test`:

```text
compiler classic-test pass=True
observed=8  withMc7=8  distinct=8  realCodegen=True  observedLoo=7/8
r2=0.9969  rmse=0.72  differential.mae=0.875  differential.jaccard=0.768
```

The eight observed SCL-to-MC7 pairs:

| Block | Code bytes | Statements | Code SHA-256 |
|---|---:|---:|---|
| `FC_LogicB` | 42 | 12 | `3f996fd34e0f887f85e2d2d857af760362165de4a041db5ac56be2748f6897d0` |
| `FC_And` | 26 | 8 | `488efca4aed9d56f5e0df9bac78f4ce4045717f750aafb6ced4386a21e2a7446` |
| `FC_Or` | 26 | 8 | `47e95974cc07401529253962cff96ebc791984f501183476ac50317dd6994b74` |
| `FC_Not` | 22 | 7 | `9a3b2e3a25ca55c1164e6c3c0723a9cf020914b852800609788b660c26833cc6` |
| `FC_Xor` | 26 | 8 | `740f1a32440bcda79d2ef5a48e9b4eca4067c16f3cf3ef54f965142faa869d9e` |
| `FC_Mux` | 46 | 14 | `c428a2deb5bcbd0c666d8855bd3a1863ccfda507c3cda3a3a34f07f4f823f0e8` |
| `FC_Cmp` | 28 | 9 | `8adaa88f86772b1a6b8410dc5d7e97df1e48b99b5af8eb426d36b85bb8aefef1` |
| `FC_Add` | 28 | 9 | `c03e81a25980bf791135d60919dac4cc00675d5dd79f7d496974154dd17ce232` |

Observed-block leave-one-out (the model sees the other seven blocks):

| Block | Actual | Predicted | Jaccard | Length OK | Opcode OK |
|---|---:|---:|---:|---|---|
| `FC_LogicB` | 42 | 43 | 0.57 | yes | yes |
| `FC_And` | 26 | 26 | 0.80 | yes | yes |
| `FC_Or` | 26 | 26 | 0.67 | yes | yes |
| `FC_Not` | 22 | 21 | 0.80 | yes | yes |
| `FC_Xor` | 26 | 26 | 0.67 | yes | yes |
| `FC_Mux` | 46 | 49 | 0.44 | no | no |
| `FC_Cmp` | 28 | 26 | 0.67 | yes | yes |
| `FC_Add` | 28 | 26 | 0.57 | yes | yes |

The model is a length/opcode predictor, not a full operand or register
allocator. `FC_Mux` is the one observed miss because the model has no branch
target or jump-offset feature.

### Negative extraction test

Truncating `FC_And`'s `BinaryData` from 126 to 80 bytes leaves no room for the
26-byte code slice. The extractor reports `withMc7=7`, and `FC_And` has
`codeLength=0`; it does not fabricate instructions.

## S7-1500 plus path

An S7-1500 block is stored as
`Siemens.Simatic.Lang.Model.Blocks.LoadablePlusCodeBlockData` (TypeId
`2232336`) with a dependent `LoadableDependentData` object. Relevant fields:

| Field | Observed |
|---|---|
| `LoadType` | `PLUSBLOCK` |
| `OpenImmediateData` | 12 bytes, identical for all eight blocks: `1500030030490000000802D5` |
| `UnencryptedCodeBinaryLength` | 14, 28, 23, 23, 20, 23, 35, 25, 23 for Main + eight FCs |
| `MC7PlusData` | `EF BE AD DE`-framed sections |

`MC7PlusData` section layout (section index:length) for the same blocks:

| Block | `UnencryptedCodeBinaryLength` | Section 1 length | Sections |
|---|---:|---:|---|
| `Main` | 14 | 18 | `124:1, 0:32, 1:18, 2:0` |
| `FC_LogicB` | 28 | 16 | `124:1, 0:48, 1:16, 2:0` |
| `FC_And` | 23 | 27 | `124:1, 0:32, 1:27, 2:0` |
| `FC_Or` | 23 | 27 | `124:1, 0:32, 1:27, 2:0` |
| `FC_Not` | 20 | 24 | `124:1, 0:32, 1:24, 2:0` |
| `FC_Xor` | 23 | 27 | `124:1, 0:32, 1:27, 2:0` |
| `FC_Mux` | 35 | 23 | `124:1, 0:48, 1:23, 2:0` |
| `FC_Cmp` | 25 | 29 | `124:1, 0:32, 1:29, 2:0` |
| `FC_Add` | 23 | 27 | `124:1, 0:32, 1:27, 2:0` |

The lengths do not line up with `UnencryptedCodeBinaryLength`, and the section
payloads do not decode as instructions. `OpenImmediateData` is a metadata stub,
not code: it is byte-identical for every block, so using it as "the MC7" yields
`observed.distinctMc7=1` and `realCodegen=false`.

### Decoder attempts

The public TIA assemblies expose:

- `Siemens.Simatic.Lang.CodeGenPlus.DOL.MC7plus.Disassembler`
  (`DisassembleCallStatements`, `GetHighestLanguageLevel`,
  `GetCompilerVersion`) and the `IMC7plusExchange2` callback.
- `Mc7PlusCommon.MC7PlusBinaryReader` for the binary section format.
- `Siemens.Simatic.Lang.CodeGenPlus.DOL.MC7plus.KeyKeeper`
  (`AddKey`, `GenKey`).
- `Siemens.Simatic.PlcLanguages.BlockLogic.BlindMC7plusExchange2`, which
  ignores every operand.

Measured behavior:

| Attempt | Result |
|---|---|
| `Disassembler.DisassembleCallStatements` on `MC7PlusData` section 1 | `InvalidOperationException: Error in operand decoding` |
| `MC7PlusBinaryReader.ReadAll` on section 1 | decodes a few unrelated opcodes, then `Read not allowed type code` or `IndexOutOfRangeException` |
| `KeyKeeper.AddKey(FamilyKey)` then `Disassembler` | `SEHException`; `DisassembleCallStatements` still fails |
| `KeyKeeper.GenKey(FamilyKey, null)` | `NullReferenceException` |
| `OpenImmediateData` as code | no instructions; identical across blocks |

`FamilyKey` is a 42-byte project-wide blob, not a per-block decryption key.
`OwnSignatureData` is 48 bytes; `BindingMac` and `ClosedImmediateData` are
empty. The plus code is encrypted at rest in a way the public Openness surface
does not expose. This is a **blocked** result, not a claim of decryption.

## PLCSIM V21 read-back attempt

This was measured with **PLCSIM V21 standard**, not PLCSIM Advanced V8. The
goal was to download `PlantB2` to a virtual S7-1500 and upload the compiled
blocks, bypassing the encrypted project store.

What worked:

- Openness `DownloadProvider` is attached to the CPU `DeviceItem` (not
  `PlcSoftware`).
- The CPU's `PROFINET interface_1` has one `NetworkInterface` node (`X1`).
  `Node.CreateAndConnectToSubnet("PROFINET_1")` creates a subnet and connects
  the node; the node then reports `Address=192.168.0.1`,
  `SubnetMask=255.255.255.0`.
- The PLCSIM V21 API can power on a real vPLC:
  `SimulationController.PowerOn(Family.S71500, Vplc1500.dll,
  Vplc1500.Motion.dll, storage, CycleTimeoutMode.IgnoreConfiguration, 0)`
  returns `true`; state reports `OperatingMode=Stop`, `IsBooting=False`,
  `IsPoweredOn=True`.
- The vPLC IP can be set through the private `SetAddress_DEBUG_ONLY(1,
  192.168.0.1, true)` path via reflection.

What failed:

| Step | Observed |
|---|---|
| Hardware compile | 2 errors: `Password for confidential PLC configuration data is not configured in the PLC.` and the certificate error that follows it |
| Download configuration target `1 X1` | `addresses=0` |
| `ApplyConfiguration` with a created `192.168.0.1` address | `The configuration provided is invalid` |
| Openness `Download(target, address, ..., DownloadOptions.Software)` | `An error has occured during download: 'Connect to module PLC_B failed.'` |

The API-created vPLC is not reachable through TIA's `PLCSIM` PC
interface in this setup, so TIA cannot connect to it. The public PLCSIM API exposes runtime control (scan,
process values, memory reset, power) but no block download or upload. The
remaining path is the TIA UI **Start simulation** command, which creates the
PLCSIM instance TIA can talk to, followed by an Openness station upload. That
needs a manual TIA UI action or the confidential PLC configuration password.

## Cross-release drift

Only TIA Portal V21 is installed on this host (`<TIA_PORTAL>`).
No V15.1-V20 installation or installer is available. The format-drift
comparison therefore remains V21-vs-V21; it is not a genuine cross-release
comparison. A second TIA release is required for that evidence.

## Reproduction

```powershell
# 1. Export the project to objectframe XML with Openness (or TIA MCP).
#    The exact command depends on the Openness wrapper in use.

# 2. Extract classic code slices.
python .\probe\mc7_classic_extract.py `
    --xml .\PlantE.objectframe.xml `
    --out .\out\classic-blocks

# 3. Build the decoder against a local TIA installation.
.\probe\mc7_classic_host\build.ps1 `
    -TiaBin 'C:\Program Files\Siemens\Automation\Portal V21\Bin'

# 4. Decode one block.
.\probe\mc7_classic_host\bin\Mc7ClassicHost.exe `
    --tia-bin 'C:\Program Files\Siemens\Automation\Portal V21\Bin' `
    --data .\out\classic-blocks\FC_And.bin `
    --start 0 --length 26
```

Expected: one JSON object with `ok=true`, `codeLength=26`, and the statement
list shown in the host README.

## Limits

- The classic path is real codegen evidence: eight distinct SCL-to-MC7 pairs,
  `observed.distinctMc7=8`, `realCodegen=true`.
- The plus path is blocked: `observed.distinctMc7=1`, `realCodegen=false`.
  `OpenImmediateData` must not be counted as code.
- The PLCSIM V21 finding is runtime-only; it does not prove that PLCSIM
  Advanced V8 behaves the same way.
- The code hashes and byte signatures are from generated lab projects. They
  are not vendor binaries and do not contain customer data.
