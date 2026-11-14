# HMI Unified JavaScript scripting stack

TIA V21 ships a JavaScript scripting stack for WinCC Unified. The
validator is a **V8** wrapper, not the legacy VB engine:

```csharp
// Siemens.Simatic.Hmi.BL.Scripting.Validator.NativeMethods
[DllImport("Siemens.Simatic.Hmi.BL.Scripting.RTScriptComponentWrapper.dll")]
static extern IntPtr CreateV8EngineWrapper();
[DllImport(...)]
static extern IntPtr CompileScript(IntPtr engine, string code, CallbackDelegate callback);
```

`JavaScriptValidator.DoValidate` calls `CompileScript` and rewraps the
native `CompileResult` into line/column/module `CompileProblem`s.
`JavaScriptEngineProvider.NativeEnginePointer` owns the V8 engine.

## Layers

| Layer | Files | Size MB | Examples |
|---|---:|---:|---|
| contracts | 3 | 0.30 | `Siemens.Simatic.Hmi.BL.ScriptModuleLib.Interfaces.dll`, `Siemens.Simatic.Hmi.BL.Scripting.Interfaces.dll`, `Siemens.Simatic.Hmi.Scripting.Interfaces.dll` |
| data | 2 | 0.08 | `Siemens.Simatic.Hmi.DL.ScriptModuleLib.dll`, `Siemens.Simatic.Hmi.DL.Scripting.dll` |
| editor | 6 | 4.32 | `Siemens.Simatic.Hmi.PL.ScriptModuleLib.dll`, `Siemens.Simatic.Hmi.PL.Scripting.EditorLogic.dll`, `Siemens.Simatic.Hmi.PL.Scripting.Scan.dll`, `Siemens.Simatic.Hmi.Scripting.EditControl.dll` |
| engine | 2 | 28.72 | `JSEngineAdapterV8.dll`, `Siemens.Simatic.Hmi.BL.Scripting.RTScriptComponentWrapper.dll` |
| legacy-vb | 6 | 2.61 | `CCHMIScriptingSite.dll`, `CCScriptComp.dll`, `FwVBScripting.dll`, `Interop.CCHMISCRIPTINGSITELib.dll` |
| model | 6 | 1.18 | `HmiScriptModel.dll`, `Siemens.Simatic.Hmi.BL.ScriptModuleLib.dll`, `Siemens.Simatic.Hmi.BL.Scripting.ChromDataExchange.dll`, `Siemens.Simatic.Hmi.BL.Scripting.Model.dll` |
| parser | 1 | 0.06 | `Siemens.Simatic.Hmi.BL.Scripting.Parser.dll` |
| runtime | 6 | 2.82 | `ScriptAlarmObjectModel.dll`, `ScriptCommonLib.dll`, `ScriptHMIMath.dll`, `ScriptHMIRuntime.dll` |
| serialization | 1 | 0.37 | `Siemens.Simatic.Hmi.ILCompiler.RtContribution.dll` |
| validator | 1 | 0.05 | `Siemens.Simatic.Hmi.BL.Scripting.Validator.dll` |

## Key assemblies

| Assembly | Layer | Role | Size MB |
|---|---|---|---:|
| `Siemens.Simatic.Hmi.BL.ScriptModuleLib.Interfaces.dll` | contracts | module library contracts | 0.01 |
| `Siemens.Simatic.Hmi.BL.Scripting.Interfaces.dll` | contracts | script, module and validation contracts | 0.11 |
| `Siemens.Simatic.Hmi.Scripting.Interfaces.dll` | contracts | scripting UI contracts | 0.18 |
| `Siemens.Simatic.Hmi.DL.ScriptModuleLib.dll` | data | module library data layer | 0.03 |
| `Siemens.Simatic.Hmi.DL.Scripting.dll` | data | script data layer | 0.06 |
| `Siemens.Simatic.Hmi.PL.ScriptModuleLib.dll` | editor | module library editor | 0.02 |
| `Siemens.Simatic.Hmi.PL.Scripting.EditorLogic.dll` | editor | script editor logic | 0.14 |
| `Siemens.Simatic.Hmi.PL.Scripting.Scan.dll` | editor | script editor scan | 0.24 |
| `Siemens.Simatic.Hmi.Scripting.EditControl.dll` | editor | script edit control | 0.55 |
| `Siemens.Simatic.Hmi.Scripting.Managers.Scan.dll` | editor | script managers | 0.45 |
| `Siemens.Simatic.Hmi.Scripting.Snapin.Scan.dll` | editor | script snap-in | 2.92 |
| `JSEngineAdapterV8.dll` | engine | V8 engine adapter (native) | 28.66 |
| `Siemens.Simatic.Hmi.BL.Scripting.RTScriptComponentWrapper.dll` | engine | native V8 wrapper: CreateV8EngineWrapper, CompileScript | 0.06 |
| `CCHMIScriptingSite.dll` | legacy-vb | classic WinCC scripting site | 0.43 |
| `CCScriptComp.dll` | legacy-vb | classic WinCC script compiler | 1.04 |
| `FwVBScripting.dll` | legacy-vb | legacy VB scripting | 0.27 |
| `Interop.CCHMISCRIPTINGSITELib.dll` | legacy-vb | classic scripting interop | 0.72 |
| `Interop.Siemens.Simatic.Hmi.Scripting.FwVBScripting.dll` | legacy-vb | VB interop | 0.14 |
| `Interop.Siemens.Simatic.Hmi.Scripting.vbobjects.dll` | legacy-vb | VB object model interop | 0.02 |
| `HmiScriptModel.dll` | model | legacy script model and tokenizer | 0.36 |
| `Siemens.Simatic.Hmi.BL.ScriptModuleLib.dll` | model | script module library | 0.13 |
| `Siemens.Simatic.Hmi.BL.Scripting.ChromDataExchange.dll` | model | Chrom data exchange | 0.07 |
| `Siemens.Simatic.Hmi.BL.Scripting.Model.dll` | model | tokenizer, snippets, RT model | 0.09 |
| `Siemens.Simatic.Hmi.BL.Scripting.Scan.dll` | model | script/module data access; HmiScriptModule, virtual event/trigger modules | 0.44 |
| `Siemens.Simatic.Hmi.BL.Vci.GlobalScriptModule.dll` | model | global script module / VCI | 0.08 |
| `Siemens.Simatic.Hmi.BL.Scripting.Parser.dll` | parser | JavaScript lexer/parser (ParserLevel1-3, JsDoc, template strings) | 0.06 |
| `ScriptAlarmObjectModel.dll` | runtime | alarm object model | 0.76 |
| `ScriptCommonLib.dll` | runtime | common script library | 0.46 |
| `ScriptHMIMath.dll` | runtime | math library | 0.15 |
| `ScriptHMIRuntime.dll` | runtime | HMI script runtime | 0.23 |
| `ScriptTagObjectModel.dll` | runtime | tag object model | 1.16 |
| `ScriptTimerObjectModel.dll` | runtime | timer object model | 0.07 |
| `Siemens.Simatic.Hmi.ILCompiler.RtContribution.dll` | serialization | VCS20ScriptAction RDF serialization | 0.37 |
| `Siemens.Simatic.Hmi.BL.Scripting.Validator.dll` | validator | V8 compile/validate; JavaScriptValidator, NativeMethods | 0.05 |

Device script assemblies (`Siemens.Simatic.Hmi.Device.ScriptDevice_PE*.dll`): 12.

## Script model

- `Siemens.Simatic.Hmi.BL.Scripting.Model` has `Tokenizer`, `Snippets`
  and `RTModel` types.
- `Siemens.Simatic.Hmi.BL.Scripting.Parser` has `ScriptParser`,
  `ParserLevel1/2/3`, `JsDocAnnotation`, `TemplateString` and
  `BlockComment`/`LineComment` rules.
- `Siemens.Simatic.Hmi.BL.Scripting.Scan` has `HmiScriptModule`,
  `VirtualEventScriptModule`, `VirtualTriggerScriptModule` and
  `HmiScriptModuleContainer`.
- `Siemens.Simatic.Hmi.ILCompiler.RtContribution.VCS20ScriptAction`
  serializes script actions into the runtime RDF.

## Runtime object model

`ScriptTagObjectModel.dll`, `ScriptAlarmObjectModel.dll`,
`ScriptTimerObjectModel.dll`, `ScriptHMIRuntime.dll` and
`ScriptCommonLib.dll` provide the JavaScript-visible tag, alarm and
timer objects.

## Legacy path

`FwVBScripting.dll`, `CCHMIScriptingSite.dll` and `CCScriptComp.dll`
are the classic WinCC VB scripting path. They are separate from the
Unified V8 path.

## Extraction notes

The scripting model is stored in the HMI Unified project (Chrom/RDF),
not in the PLC objectframe. Extracting script text needs an HMI
Unified project and the `HmiScriptModule` model; the current lab
`PlantB2` project is PLC-only.

Reproduce:

```powershell
python .\probe\hmi_scripting_inventory.py --bin '<TIA_BIN>'
```
