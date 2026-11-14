#!/usr/bin/env python3
"""Inventory the TIA V21 HMI Unified JavaScript scripting stack.

Reads sizes from the installed ``Bin`` directory and writes a curated JSON +
Markdown summary.  The roles are derived from the assembly names and from the
decompiled types (``JavaScriptValidator``, ``JavaScriptEngineProvider``,
``NativeMethods``, ``ScriptParser``, ``VCS20ScriptAction``).
"""

from __future__ import annotations

import argparse
import json
from pathlib import Path

FILES = {
    "HmiScriptModel.dll": ("model", "legacy script model and tokenizer"),
    "Siemens.Simatic.Hmi.BL.Scripting.Model.dll": ("model", "tokenizer, snippets, RT model"),
    "Siemens.Simatic.Hmi.BL.Scripting.Parser.dll": ("parser", "JavaScript lexer/parser (ParserLevel1-3, JsDoc, template strings)"),
    "Siemens.Simatic.Hmi.BL.Scripting.Validator.dll": ("validator", "V8 compile/validate; JavaScriptValidator, NativeMethods"),
    "Siemens.Simatic.Hmi.BL.Scripting.RTScriptComponentWrapper.dll": ("engine", "native V8 wrapper: CreateV8EngineWrapper, CompileScript"),
    "Siemens.Simatic.Hmi.BL.Scripting.Scan.dll": ("model", "script/module data access; HmiScriptModule, virtual event/trigger modules"),
    "Siemens.Simatic.Hmi.BL.Scripting.Interfaces.dll": ("contracts", "script, module and validation contracts"),
    "Siemens.Simatic.Hmi.BL.Scripting.ChromDataExchange.dll": ("model", "Chrom data exchange"),
    "Siemens.Simatic.Hmi.BL.ScriptModuleLib.dll": ("model", "script module library"),
    "Siemens.Simatic.Hmi.BL.ScriptModuleLib.Interfaces.dll": ("contracts", "module library contracts"),
    "Siemens.Simatic.Hmi.BL.Vci.GlobalScriptModule.dll": ("model", "global script module / VCI"),
    "Siemens.Simatic.Hmi.DL.Scripting.dll": ("data", "script data layer"),
    "Siemens.Simatic.Hmi.DL.ScriptModuleLib.dll": ("data", "module library data layer"),
    "Siemens.Simatic.Hmi.PL.Scripting.EditorLogic.dll": ("editor", "script editor logic"),
    "Siemens.Simatic.Hmi.PL.Scripting.Scan.dll": ("editor", "script editor scan"),
    "Siemens.Simatic.Hmi.PL.ScriptModuleLib.dll": ("editor", "module library editor"),
    "Siemens.Simatic.Hmi.Scripting.EditControl.dll": ("editor", "script edit control"),
    "Siemens.Simatic.Hmi.Scripting.Interfaces.dll": ("contracts", "scripting UI contracts"),
    "Siemens.Simatic.Hmi.Scripting.Managers.Scan.dll": ("editor", "script managers"),
    "Siemens.Simatic.Hmi.Scripting.Snapin.Scan.dll": ("editor", "script snap-in"),
    "ScriptHMIRuntime.dll": ("runtime", "HMI script runtime"),
    "ScriptCommonLib.dll": ("runtime", "common script library"),
    "ScriptHMIMath.dll": ("runtime", "math library"),
    "ScriptAlarmObjectModel.dll": ("runtime", "alarm object model"),
    "ScriptTagObjectModel.dll": ("runtime", "tag object model"),
    "ScriptTimerObjectModel.dll": ("runtime", "timer object model"),
    "JSEngineAdapterV8.dll": ("engine", "V8 engine adapter (native)"),
    "FwVBScripting.dll": ("legacy-vb", "legacy VB scripting"),
    "Interop.Siemens.Simatic.Hmi.Scripting.FwVBScripting.dll": ("legacy-vb", "VB interop"),
    "CCHMIScriptingSite.dll": ("legacy-vb", "classic WinCC scripting site"),
    "CCScriptComp.dll": ("legacy-vb", "classic WinCC script compiler"),
    "Interop.CCHMISCRIPTINGSITELib.dll": ("legacy-vb", "classic scripting interop"),
    "Interop.Siemens.Simatic.Hmi.Scripting.vbobjects.dll": ("legacy-vb", "VB object model interop"),
    "Siemens.Simatic.Hmi.ILCompiler.RtContribution.dll": ("serialization", "VCS20ScriptAction RDF serialization"),
}


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--bin", default=r"D:\Arbeit\Siemens\Portal V21\Bin")
    parser.add_argument("--json", default="analysis/hmi-scripting-inventory.json")
    parser.add_argument("--md", default="docs/18-hmi-scripting.md")
    args = parser.parse_args()

    bin_dir = Path(args.bin)
    rows = []
    for name, (layer, role) in FILES.items():
        path = bin_dir / name
        if not path.exists():
            continue
        rows.append({
            "name": name,
            "layer": layer,
            "role": role,
            "sizeMB": round(path.stat().st_size / (1024 * 1024), 3),
        })
    rows.sort(key=lambda r: (r["layer"], r["name"]))
    device_scripts = sorted(p.name for p in bin_dir.glob("Siemens.Simatic.Hmi.Device.ScriptDevice_*.dll"))

    report = {
        "bin": "<TIA_BIN>",
        "files": rows,
        "deviceScriptAssemblies": device_scripts,
        "layers": sorted({r["layer"] for r in rows}),
    }
    Path(args.json).write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")

    lines = [
        "# HMI Unified JavaScript scripting stack",
        "",
        "TIA V21 ships a JavaScript scripting stack for WinCC Unified. The",
        "validator is a **V8** wrapper, not the legacy VB engine:",
        "",
        """```csharp
// Siemens.Simatic.Hmi.BL.Scripting.Validator.NativeMethods
[DllImport("Siemens.Simatic.Hmi.BL.Scripting.RTScriptComponentWrapper.dll")]
static extern IntPtr CreateV8EngineWrapper();
[DllImport(...)]
static extern IntPtr CompileScript(IntPtr engine, string code, CallbackDelegate callback);
```""",
        "",
        "`JavaScriptValidator.DoValidate` calls `CompileScript` and rewraps the",
        "native `CompileResult` into line/column/module `CompileProblem`s.",
        "`JavaScriptEngineProvider.NativeEnginePointer` owns the V8 engine.",
        "",
        "## Layers",
        "",
        "| Layer | Files | Size MB | Examples |",
        "|---|---:|---:|---|",
    ]
    layers: dict[str, list[dict]] = {}
    for row in rows:
        layers.setdefault(row["layer"], []).append(row)
    for layer, items in sorted(layers.items()):
        examples = ", ".join(f"`{i['name']}`" for i in items[:4])
        lines.append(
            f"| {layer} | {len(items)} | {sum(i['sizeMB'] for i in items):.2f} | {examples} |"
        )
    lines += [
        "",
        "## Key assemblies",
        "",
        "| Assembly | Layer | Role | Size MB |",
        "|---|---|---|---:|",
    ]
    for row in rows:
        lines.append(
            f"| `{row['name']}` | {row['layer']} | {row['role']} | {row['sizeMB']:.2f} |"
        )
    lines += [
        "",
        f"Device script assemblies (`Siemens.Simatic.Hmi.Device.ScriptDevice_PE*.dll`): "
        f"{len(device_scripts)}.",
        "",
        "## Script model",
        "",
        "- `Siemens.Simatic.Hmi.BL.Scripting.Model` has `Tokenizer`, `Snippets`",
        "  and `RTModel` types.",
        "- `Siemens.Simatic.Hmi.BL.Scripting.Parser` has `ScriptParser`,",
        "  `ParserLevel1/2/3`, `JsDocAnnotation`, `TemplateString` and",
        "  `BlockComment`/`LineComment` rules.",
        "- `Siemens.Simatic.Hmi.BL.Scripting.Scan` has `HmiScriptModule`,",
        "  `VirtualEventScriptModule`, `VirtualTriggerScriptModule` and",
        "  `HmiScriptModuleContainer`.",
        "- `Siemens.Simatic.Hmi.ILCompiler.RtContribution.VCS20ScriptAction`",
        "  serializes script actions into the runtime RDF.",
        "",
        "## Runtime object model",
        "",
        "`ScriptTagObjectModel.dll`, `ScriptAlarmObjectModel.dll`,",
        "`ScriptTimerObjectModel.dll`, `ScriptHMIRuntime.dll` and",
        "`ScriptCommonLib.dll` provide the JavaScript-visible tag, alarm and",
        "timer objects.",
        "",
        "## Legacy path",
        "",
        "`FwVBScripting.dll`, `CCHMIScriptingSite.dll` and `CCScriptComp.dll`",
        "are the classic WinCC VB scripting path. They are separate from the",
        "Unified V8 path.",
        "",
        "## Extraction notes",
        "",
        "The scripting model is stored in the HMI Unified project (Chrom/RDF),",
        "not in the PLC objectframe. Extracting script text needs an HMI",
        "Unified project and the `HmiScriptModule` model; the current lab",
        "`PlantB2` project is PLC-only.",
        "",
        "Reproduce:",
        "",
        "```powershell",
        "python .\\probe\\hmi_scripting_inventory.py --bin '<TIA_BIN>'",
        "```",
    ]
    Path(args.md).write_text("\n".join(lines) + "\n", encoding="utf-8")
    print(f"files={len(rows)} deviceScripts={len(device_scripts)}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
