#!/usr/bin/env python3
"""Extract the S7-1500 plus-code key derivation inputs from an object frame.

The TIA plus-code container is encrypted with a key pair that TIA derives from

    TargetSeed = BitConverter.GetBytes(project.CreationTime.ToBinary())
               + ControllerTarget.Id.ToByteArray()

where ``ObjectId.ToByteArray()`` is

    4F 47 <typeId:u32 LE> <instanceId:u64 LE>

(see ``ObjectIdConvert.ObjectIdStructurePartsToByteArray`` in
``Siemens.Automation.ObjectFrame.Storage.Base`` and
``PlusPlcSecurityInfo.GetTargetSeed`` in ``Siemens.Simatic.Lang.SwTarget``).

This script prints the seed, the block family key and the public/target key
ids stored in the plus container header.  The actual key ids are derived by
TIA's ``KeyKeeper`` (see ``probe/mc7plus_keys``).
"""

from __future__ import annotations

import argparse
import hashlib
import json
import struct
import sys
import xml.etree.ElementTree as ET
from pathlib import Path


def local(tag: str) -> str:
    return tag.rsplit("}", 1)[-1]


def attr_value(obj: ET.Element, name: str) -> ET.Element | None:
    for e in obj.iter():
        if local(e.tag) == "attrib" and e.get("name") == name:
            for c in e:
                return c
    return None


def text(e: ET.Element | None) -> str:
    return (e.text or "").strip() if e is not None else ""


def blob(obj: ET.Element, name: str) -> bytes:
    value = text(attr_value(obj, name))
    return bytes.fromhex(value) if value else b""


def parse_container(data: bytes) -> dict:
    if len(data) < 0x40 or data[:4] != b"\xef\xbe\xad\xde":
        raise ValueError("not a plus container")
    header_len = struct.unpack_from("<I", data, 4)[0]
    mode = struct.unpack_from("<I", data, 0x0C)[0]
    chunk = struct.unpack_from("<I", data, 0x14)[0]
    public_id = data[0x1C:0x2C]
    target_id = data[0x2C:0x3C]
    mac_len = struct.unpack_from("<I", data, 0x3C)[0]
    mac = data[0x40:0x40 + mac_len]
    sections = []
    i = header_len
    while i + 12 <= len(data):
        if data[i:i + 4] != b"\xef\xbe\xad\xde":
            break
        idx, length = struct.unpack_from("<II", data, i + 4)
        sections.append({"index": idx, "length": length})
        i += 12 + length
    return {
        "headerLength": header_len,
        "mode": mode,
        "chunk": chunk,
        "publicId": public_id.hex(),
        "targetId": target_id.hex(),
        "macLength": mac_len,
        "mac": mac.hex(),
        "sections": sections,
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--xml", required=True, help="objectframe export")
    parser.add_argument("--block", required=True, help="block name, e.g. FC_And")
    parser.add_argument("--out", help="optional JSON output")
    args = parser.parse_args()

    root = ET.parse(args.xml).getroot()
    types = {}
    for e in root.iter():
        if local(e.tag) == "typeInfo":
            types[e.get("name")] = int(e.get("id"))

    project_creation = None
    for obj in root.iter():
        if local(obj.tag) != "StorageObject":
            continue
        if obj.get("name") != "Siemens.Automation.DomainModel.ProjectData":
            continue
        value = text(attr_value(obj, "CreationTime"))
        if value:
            project_creation = int(value)
            break
    if project_creation is None:
        print("project CreationTime not found", file=sys.stderr)
        return 1

    cpu = None
    for obj in root.iter():
        if local(obj.tag) != "StorageObject":
            continue
        if obj.get("name") != "Siemens.Simatic.HwConfiguration.Model.S7ControllerTargetData":
            continue
        cpu = obj
        break
    if cpu is None:
        print("S7ControllerTargetData not found", file=sys.stderr)
        return 1

    type_name = "Siemens.Simatic.HwConfiguration.Model.S7ControllerTargetData"
    type_id = types.get(type_name)
    instance_id = int(cpu.get("instId"))
    if type_id is None:
        print("typeId for " + type_name + " not found", file=sys.stderr)
        return 1

    object_id = b"OG" + struct.pack("<I", type_id) + struct.pack("<Q", instance_id)
    seed = struct.pack("<q", project_creation) + object_id

    names = {}
    for obj in root.iter():
        if local(obj.tag) != "StorageObject":
            continue
        if not (obj.get("name") or "").endswith("CodeBlockData"):
            continue
        name = text(attr_value(obj, "Name"))
        for rel in obj.iter():
            if local(rel.tag) != "relation":
                continue
            for link in rel:
                t = (link.text or "").strip()
                if "-" in t:
                    tid, iid = t.split("-", 1)
                    if tid in ("2232330", "2232336"):
                        names[iid] = name

    family_key = None
    container = None
    mac = None
    for obj in root.iter():
        if local(obj.tag) != "StorageObject":
            continue
        if not (obj.get("name") or "").endswith("LoadablePlusCodeBlockData"):
            continue
        if names.get(obj.get("instId")) != args.block:
            continue
        for rel in obj.iter():
            if local(rel.tag) != "relation":
                continue
            for link in rel:
                t = (link.text or "").strip()
                if not t.startswith("2232327-"):
                    continue
                dep_id = t.split("-", 1)[1]
                dep = next((o for o in root.iter()
                            if local(o.tag) == "StorageObject" and o.get("instId") == dep_id), None)
                if dep is None:
                    continue
                family_key = blob(dep, "FamilyKey")
                container = blob(dep, "MC7PlusData")
                for e in obj.iter():
                    if local(e.tag) == "entry" and e.get("name") == "MAC":
                        for c in e:
                            mac = bytes.fromhex((c.text or "").strip())
        break

    if container is None:
        print("block not found: " + args.block, file=sys.stderr)
        return 1

    header = parse_container(container)
    report = {
        "block": args.block,
        "projectCreationTime": project_creation,
        "controllerTarget": {
            "typeId": type_id,
            "instanceId": instance_id,
            "objectIdHex": object_id.hex(),
        },
        "seedHex": seed.hex(),
        "seedSha256": hashlib.sha256(seed).hexdigest(),
        "familyKeyLength": len(family_key or b""),
        "familyKeySha256": hashlib.sha256(family_key or b"").hexdigest(),
        "mac": (mac or b"").hex(),
        "container": header,
    }
    print(json.dumps(report, indent=2))
    if args.out:
        Path(args.out).write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
