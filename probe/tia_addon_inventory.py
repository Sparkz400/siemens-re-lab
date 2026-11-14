#!/usr/bin/env python3
"""Classify the TIA V21 assembly inventory into product/add-on families.

Reads ``analysis/tia-assembly-inventory.csv`` and writes a JSON + Markdown
summary.  The classification is prefix-based and intentionally conservative:
an assembly can match more than one family, and the JSON records every match.
"""

from __future__ import annotations

import argparse
import csv
import json
from collections import defaultdict
from pathlib import Path

FAMILIES = [
    ("wincc-unified", ("Siemens.Simatic.Hmi.", "Siemens.WinCC.", "Siemens.MP.")),
    ("plc-language", ("Siemens.Simatic.Lang.",)),
    ("hardware-config", ("Siemens.Simatic.Hwcn.",)),
    ("safety", ("Siemens.Simatic.Safety.", "Siemens.Simatic.F.", "Siemens.Simatic.S7F.")),
    ("drives-startdrive", ("Siemens.MC.Drives.", "Siemens.Simatic.Drive.", "Siemens.Simatic.Startdrive.")),
    ("test-suite", ("Siemens.Simatic.TestSuite.", "Siemens.Simatic.Test.")),
    ("opc-ua", ("Opc.Ua.", "Siemens.Simatic.OpcUa.")),
    ("security-csi", ("Siemens.DF.", "Siemens.Automation.Security.", "Siemens.Automation.Cryptography.")),
    ("automation-framework", ("Siemens.Automation.",)),
    ("motion-technology", ("Siemens.MP.", "Siemens.TechTrace.", "Siemens.Simatic.Tech.")),
]


def size_mb(value: str) -> float:
    value = (value or "0").replace(".", "").replace(",", ".")
    try:
        return float(value)
    except ValueError:
        return 0.0


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--csv", default="analysis/tia-assembly-inventory.csv")
    parser.add_argument("--json", default="analysis/tia-addon-inventory.json")
    parser.add_argument("--md", default="docs/17-tia-addon-inventory.md")
    args = parser.parse_args()

    rows = list(csv.DictReader(Path(args.csv).open(encoding="utf-8-sig")))
    families: dict[str, dict] = {}
    for name, prefixes in FAMILIES:
        families[name] = {
            "prefixes": list(prefixes),
            "files": 0,
            "managed": 0,
            "native": 0,
            "sizeMB": 0.0,
            "examples": [],
        }

    unmatched = []
    for row in rows:
        assembly = row.get("Name") or ""
        size = size_mb(row.get("SizeMB", "0"))
        matched = False
        for name, prefixes in FAMILIES:
            if any(assembly.startswith(p) for p in prefixes):
                matched = True
                fam = families[name]
                fam["files"] += 1
                fam["sizeMB"] += size
                if (row.get("Managed") or "").lower() == "true":
                    fam["managed"] += 1
                else:
                    fam["native"] += 1
                if len(fam["examples"]) < 8:
                    fam["examples"].append(assembly)
        if not matched:
            unmatched.append(assembly)

    report = {
        "source": args.csv if not Path(args.csv).is_absolute() else Path(args.csv).name,
        "totalFiles": len(rows),
        "totalSizeMB": round(sum(size_mb(r.get("SizeMB", "0")) for r in rows), 1),
        "families": families,
        "unmatchedFiles": len(unmatched),
        "unmatchedExamples": unmatched[:20],
    }
    Path(args.json).write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")

    lines = [
        "# TIA V21 add-on inventory",
        "",
        f"Source: `{report['source']}` ({report['totalFiles']} assemblies, "
        f"{report['totalSizeMB']} MB).",
        "",
        "Prefix-based classification. An assembly may match more than one family.",
        "",
        "| Family | Files | Managed | Native | Size MB | Examples |",
        "|---|---:|---:|---:|---:|---|",
    ]
    for name, fam in families.items():
        examples = ", ".join(f"`{e}`" for e in fam["examples"][:4])
        lines.append(
            f"| {name} | {fam['files']} | {fam['managed']} | {fam['native']} | "
            f"{fam['sizeMB']:.1f} | {examples} |"
        )
    lines += [
        "",
        f"Unmatched assemblies: {report['unmatchedFiles']}.",
        "",
        "Notable families:",
        "",
        "- `wincc-unified` includes `Siemens.Simatic.Hmi.*` and `Siemens.MP.*`",
        "  (WinCC Unified engineering/runtime and motion proxies).",
        "- `plc-language` includes the block compiler, MC7 codegen and the",
        "  classic/plus language assemblies.",
        "- `security-csi` includes `Siemens.DF.CsiWrapper.dll` and the",
        "  `Siemens.DF.DG.SecurityPlus.*` CSI facades.",
        "",
        "Reproduce:",
        "",
        "```powershell",
        "python .\\probe\\tia_addon_inventory.py",
        "```",
    ]
    Path(args.md).write_text("\n".join(lines) + "\n", encoding="utf-8")
    print(json.dumps({k: v for k, v in report.items() if k != "families"}, indent=2))
    for name, fam in families.items():
        print(f"{name:24s} files={fam['files']:4d} size={fam['sizeMB']:8.1f} MB")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
