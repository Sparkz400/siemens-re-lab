#!/usr/bin/env python3
"""Analyze extracted S7-1500 MC7PlusData sections.

Reads the output of `probe/mc7plus_extract.py`:
  <block>.open.bin, <block>.mc7plus.bin, <block>.secN.bin

Writes a JSON report with section lengths, Shannon entropy (bits/byte),
first bytes and cross-block section-1 equality.
"""

from __future__ import annotations

import argparse
import json
import math
from collections import Counter
from pathlib import Path


def entropy(data: bytes) -> float:
    if not data:
        return 0.0
    counts = Counter(data)
    total = len(data)
    return -sum((n / total) * math.log2(n / total) for n in counts.values())


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dir", required=True, help="directory from mc7plus_extract.py")
    parser.add_argument("--out", required=True, help="output JSON")
    args = parser.parse_args()

    root = Path(args.dir)
    blocks = {}
    for open_file in sorted(root.glob("*.open.bin")):
        name = open_file.name[: -len(".open.bin")]
        mc7plus = root / f"{name}.mc7plus.bin"
        if not mc7plus.exists():
            continue
        sections = {}
        for sec_file in sorted(root.glob(f"{name}.sec*.bin")):
            idx = int(sec_file.stem.split(".sec", 1)[1])
            data = sec_file.read_bytes()
            sections[str(idx)] = {
                "length": len(data),
                "entropy": round(entropy(data), 4),
                "firstHex": data[:24].hex(),
                "topBytes": [{"byte": b, "count": n} for b, n in Counter(data).most_common(5)],
            }
        open_data = open_file.read_bytes()
        blocks[name] = {
            "openLength": len(open_data),
            "openEntropy": round(entropy(open_data), 4),
            "openHex": open_data.hex(),
            "mc7PlusLength": mc7plus.stat().st_size,
            "sections": sections,
        }

    sec1 = {}
    for name, row in blocks.items():
        if "1" in row["sections"]:
            sec1[name] = row["sections"]["1"]["firstHex"]
    distinct_sec1 = len(set(sec1.values()))

    report = {
        "blocks": blocks,
        "section1DistinctPrefixes": distinct_sec1,
        "section1Prefixes": sec1,
    }
    Path(args.out).write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")

    for name, row in blocks.items():
        parts = []
        for idx, sec in row["sections"].items():
            parts.append(f"sec{idx}:{sec['length']}B/H{sec['entropy']}")
        print(f"{name:12s} open={row['openLength']}B/H{row['openEntropy']}  " + "  ".join(parts))
    print(f"section1 distinct prefixes: {distinct_sec1}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
