#!/usr/bin/env python3
"""Extract classic S7-300 MC7 code blobs from a TIA objectframe XML export.

The objectframe export is produced by the Openness `fs2xml` step (or by a TIA
MCP equivalent). Classic S7-300 blocks store their loadable binary in
`Siemens.Simatic.Lang.Model.Blocks.LoadableCodeBlockData`:

    BinaryData            coreblob, 78-byte loadable header + code + trailer
    BlockDebugInformation coreblob, XML containing <Net ... EndSAC="N" />
    BlockNumber           int32

The executable code is `BinaryData[78 : 78 + EndSAC + 1]`. This script writes
one `.bin` per block and a `manifest.json` with the slice length and SHA-256.
It does not decode the instructions; use `probe/mc7_classic_host` for that.

No Siemens binaries or project archives are required or committed.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

CODE_OFFSET = 78


def local_name(tag: str) -> str:
    return tag.rsplit("}", 1)[-1]


def attribute_value(storage_object: ET.Element, name: str) -> ET.Element | None:
    """Return the first child value of an <attrib name="..."> below an object."""
    for element in storage_object.iter():
        if local_name(element.tag) != "attrib" or element.get("name") != name:
            continue
        for child in element:
            return child
    return None


def text_of(element: ET.Element | None) -> str:
    return (element.text or "").strip() if element is not None else ""


def hex_blob(element: ET.Element | None) -> bytes:
    value = text_of(element)
    return bytes.fromhex(value) if value else b""


def end_sac(debug_info: bytes) -> int | None:
    text = debug_info.decode("utf-8", "replace")
    match = re.search(r'EndSAC="(\d+)"', text)
    return int(match.group(1)) if match else None


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--xml", required=True, help="objectframe XML export")
    parser.add_argument("--out", required=True, help="output directory for .bin files")
    parser.add_argument(
        "--manifest",
        default=None,
        help="manifest path (default: <out>/manifest.json)",
    )
    args = parser.parse_args()

    out_dir = Path(args.out)
    out_dir.mkdir(parents=True, exist_ok=True)

    root = ET.parse(args.xml).getroot()

    # Map loadable instId -> block name through the parent CodeBlockData
    # relation (TypeId 2232330 classic, 2232336 plus).
    block_names: dict[str, str] = {}
    for storage_object in root.iter():
        if local_name(storage_object.tag) != "StorageObject":
            continue
        if not (storage_object.get("name") or "").endswith("CodeBlockData"):
            continue
        block_name = text_of(attribute_value(storage_object, "Name"))
        if not block_name:
            continue
        for relation in storage_object.iter():
            if local_name(relation.tag) != "relation":
                continue
            for link in relation:
                if local_name(link.tag) != "link":
                    continue
                text = (link.text or "").strip()
                if "-" not in text:
                    continue
                type_id, inst_id = text.split("-", 1)
                if type_id in ("2232330", "2232336"):
                    block_names[inst_id] = block_name

    blocks: list[dict] = []

    for storage_object in root.iter():
        if local_name(storage_object.tag) != "StorageObject":
            continue
        class_name = storage_object.get("name") or ""
        if not class_name.endswith("LoadableCodeBlockData"):
            continue

        number = text_of(attribute_value(storage_object, "BlockNumber"))
        binary = hex_blob(attribute_value(storage_object, "BinaryData"))
        debug_info = hex_blob(attribute_value(storage_object, "BlockDebugInformation"))
        if not binary:
            continue

        sac = end_sac(debug_info)
        code_length = (sac + 1) if sac is not None else 0
        end = CODE_OFFSET + code_length
        code = binary[CODE_OFFSET:end] if code_length > 0 and end <= len(binary) else b""
        block_name = block_names.get(storage_object.get("instId", ""))
        safe_name = re.sub(r"[^A-Za-z0-9_.-]+", "_", block_name) if block_name else f"block_{number or 'unknown'}"
        file_name = f"{safe_name}.bin"
        file_path = out_dir / file_name
        if code:
            file_path.write_bytes(code)

        blocks.append(
            {
                "blockName": block_name,
                "blockNumber": number,
                "binaryLength": len(binary),
                "endSac": sac,
                "codeLength": len(code),
                "codeSha256": hashlib.sha256(code).hexdigest() if code else None,
                "file": str(file_path) if code else None,
            }
        )

    manifest = {
        "xml": str(Path(args.xml).resolve()),
        "codeOffset": CODE_OFFSET,
        "blocks": blocks,
    }
    manifest_path = Path(args.manifest) if args.manifest else out_dir / "manifest.json"
    manifest_path.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(manifest, indent=2))
    return 0


if __name__ == "__main__":
    sys.exit(main())
