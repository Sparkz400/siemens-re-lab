# Native exports and entry points

Curated `dumpbin /exports` inventory for the native TIA V21 modules
that matter to the A/B/C tracks. The JSON keeps the first 200 names per
module; the full export table is in the DLL itself.

| Module | Size MB | Exports |
|---|---:|---:|
| `Siemens.DF.CsiWrapper.dll` | 5.22 | 8 |
| `s7irsrvmc7x.dll` | 7.518 | 254 |
| `JSEngineAdapterV8.dll` | 28.655 | 4 |
| `rtpu.dll` | 6.99 | 3 |
| `S7GSDxDLL.dll` | 7.041 | 16 |
| `Siemens.MP.tkImplementation.dll` | 67.609 | 4211 |
| `Siemens.MP.LMC.tkImplementation.dll` | 51.184 | 3361 |
| `Siemens.Simatic.Hwcn.BusinessLogic.Ncm.scw_mips_V6_1.dll` | 11.577 | 0 |
| `Siemens.Simatic.Hwcn.BusinessLogic.Ncm.scw_ppc460_V6_1.dll` | 10.658 | 0 |
| `Siemens.Simatic.Srm.Rdp.Data.dll` | 6.286 | 233 |
| `Siemens.Simatic.Hmi.BL.Scripting.RTScriptComponentWrapper.dll` | 0.062 | 4 |
| `CCHMIScriptingSite.dll` | 0.427 | 4 |
| `CCScriptComp.dll` | 1.039 | 4 |
| `Siemens.DF.DG.SecurityPlus.CsiWrapper.Pure.BL.dll` | 0.021 | 0 |

## Key findings

- `Siemens.DF.CsiWrapper.dll` exposes only `InitCommonSecurity`,
  `EncryptDataInit/Update/Final`, `CalculateMACHybrid`, `BlockLength`,
  `CipherBufferLength` and `GetContextLength`. There is no `Decrypt*`.
- `s7irsrvmc7x.dll` exposes the native MC7 compiler/runtime surface:
  `IR_CodeGenerator_GenerateBinaryCode`, `IR_CallInstruction_*`,
  `IR_CompilerEnvironment_*`, `IR_*` message and stack-size helpers.
- `JSEngineAdapterV8.dll` and `rtpu.dll` expose the module entry points
  `CfCreateInstanceModule`, `CfInitModule`, `CfExitModule`.
- `Siemens.MP.tkImplementation.dll` exports a large C++ API
  (`tk::`, `CTKServicePropertyBase`, `ITK*`); it is the motion/technology
  implementation, not a small helper DLL.
- `CCHMIScriptingSite.dll` / `CCScriptComp.dll` are the classic WinCC
  VB scripting path; `RTScriptComponentWrapper.dll` is the Unified V8 path.

Reproduce:

```powershell
.\probe\native_exports.ps1 -Bin '<TIA_BIN>'
```
