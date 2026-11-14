---
tags: [tia, inventory, c-layer, managed, native]
created: 2026-11-14
---

# TIA IDE Inventory

## Metadata inventory

`probe/tia_inventory.ps1` classified every `*.dll`/`*.exe` in the TIA V21
`Bin` directory:

```text
files=2573
managed=2326
native=247
total=1640.8 MB
managed=931.1 MB
native=709.6 MB
```

Full CSV: `analysis/tia-assembly-inventory.csv`
Summary: `analysis/tia-assembly-summary.json`

## Top managed assemblies

| Assembly | MB | Version |
|---|---:|---|
| `Siemens.Automation.Cax.MddPublicationTool.OpennessDB.dll` | 80.55 | 2100.0.121.1 |
| `Siemens.Simatic.Hmi.BL.ExpressionValidator.dll` | 21.63 | 1.0.9566.23729 |
| `Siemens.MP.tkWebUICli.dll` | 19.80 | 2100.244.8.0 |
| `Siemens.TechTrace.Editor.UserInterface.dll` | 13.87 | 2100.0.121.1 |
| `Siemens.MP.tkDotNetDirectProxies.dll` | 12.05 | 2100.244.8.0 |
| `OMSp_core_managed.dll` | 11.78 | 16.0.0.1 |
| `Siemens.Simatic.Hwcn.BusinessLogic.dll` | 8.81 | 2100.300.4.1 |
| `s7irsrvmc7x.dll` | 7.52 | 2100.0.121.1 |

`s7irsrvmc7x.dll` is a C++/CLI mixed assembly; its managed surface is mostly
compiler attributes. Treat the MC7 logic as native inside the mixed module.

## Top native assemblies

| Assembly | MB |
|---|---:|
| `Qt6WebEngineCoreRTIL.dll` | 150.59 |
| `Siemens.MP.MotionCore.exe` | 75.62 |
| `Siemens.MP.tkImplementation.dll` | 67.61 |
| `Siemens.MP.LMC.tkImplementation.dll` | 51.18 |
| `JSEngineAdapterV8.dll` | 28.66 |
| `GfxVCS.dll` | 14.19 |
| `libWCCILRdfFwSerializer.dll` | 7.29 |

## Key managed targets

| Assembly | Why |
|---|---|
| `Siemens.Simatic.Lang.MC7Codegenerator.dll` | `Disassembler`, `ImmediateLongTable`, `KeyKeeper`, `CsiStreamImpl` |
| `Siemens.Simatic.Lang.MC7plusCommon.dll` | `MC7PlusBinaryReader`, instruction table |
| `Siemens.Simatic.Lang.MC7plus.Internal.dll` | `IMC7plusExchange2`, `IDisassembler`, CSI interfaces |
| `Siemens.Simatic.Lang.MC7plus.External.dll` | `IKeyKeeper`, operators/scopes |
| `Siemens.Simatic.Lang.MC7plus.Adapter.dll` | `PlusCodeGenerator`, `KeyKeeper.Dlc`, `Factory.Dlc` |
| `Siemens.Simatic.Lang.Block.Compiler.dll` | `CallPatcher`, `BlindMC7plusExchange2` |
| `Siemens.Simatic.Lang.PLInternal.dll` | `InternalBlockUtilities`, `IExpandedStatement` |
| `Siemens.Simatic.Lang.BlockHandling.TIA.Impl.dll` | `PlusLoadableBinary`, `GetMC7Plus` |
| `Siemens.Simatic.Lang.KnowHowProtection.dll` | `KnowHowProtectionService`, `KeyFactory` |

## Key native target

`Siemens.DF.CsiWrapper.dll` exports:

```text
BlockLength
CalculateMACHybrid
CipherBufferLength
EncryptDataFinal
EncryptDataInit
EncryptDataUpdate
GetContextLength
InitCommonSecurity
```

There is no `Decrypt*` export. If the mode is symmetric, the same context can
decrypt; if it is hybrid/signature-only, the plaintext must be captured at the
managed boundary.

## Decompiled locally (not committed)

| Assembly | Local output |
|---|---|
| `Siemens.Simatic.Lang.Block.Compiler.dll` | `C:\Windows\Temp\opencode\decomp-blockcompiler` |
| `Siemens.Simatic.Lang.PLInternal.dll` | `C:\Windows\Temp\opencode\decomp-plinternal` |
| `Siemens.Simatic.Lang.BlockHandling.TIA.Impl.dll` | `C:\Windows\Temp\opencode\decomp-blockhandling` |
| `Siemens.Simatic.Lang.BlockLogic.dll` | `C:\Windows\Temp\opencode\decomp-blocklogic` |
| `Siemens.Simatic.Lang.NetworkEditorFrame.dll` | `C:\Windows\Temp\opencode\decomp-neteditor` |

Vendor decompiled source stays local; the repo gets this index, scripts and
notes.

## Next

- Decompile `Siemens.Simatic.Lang.MC7Codegenerator.dll` and
  `MC7plusCommon` to local folders and map the `CsiStreamImpl` /
  `CsiVerifierImpl` call paths.
- Use `dumpbin /exports` on the native CSI/runtime DLLs.
- Search the managed assemblies for the caller of
  `Disassembler.DisassembleCallStatements` to find the plaintext boundary.
