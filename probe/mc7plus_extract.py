import json
import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path


def local(tag):
    return tag.rsplit("}", 1)[-1]


def attr_value(obj, name):
    for e in obj.iter():
        if local(e.tag) == "attrib" and e.get("name") == name:
            for c in e:
                return c
    return None


def text(e):
    return (e.text or "").strip() if e is not None else ""


def blob(obj, name):
    e = attr_value(obj, name)
    v = text(e)
    return bytes.fromhex(v) if v else b""


def sections(data):
    out = []
    i = 0
    while i + 12 <= len(data):
        if data[i:i+4] == b"\xef\xbe\xad\xde":
            idx = int.from_bytes(data[i+4:i+8], "little")
            length = int.from_bytes(data[i+8:i+12], "little")
            start = i + 12
            out.append((idx, data[start:start+length]))
            i = start + length
        else:
            i += 1
    return out


def main():
    xml_path = Path(sys.argv[1])
    out_dir = Path(sys.argv[2])
    out_dir.mkdir(parents=True, exist_ok=True)
    root = ET.parse(xml_path).getroot()

    names = {}
    for obj in root.iter():
        if local(obj.tag) != "StorageObject":
            continue
        if not (obj.get("name") or "").endswith("CodeBlockData"):
            continue
        name = text(attr_value(obj, "Name"))
        if not name:
            continue
        for rel in obj.iter():
            if local(rel.tag) != "relation":
                continue
            for link in rel:
                if local(link.tag) != "link":
                    continue
                t = (link.text or "").strip()
                if "-" not in t:
                    continue
                tid, iid = t.split("-", 1)
                if tid in ("2232330", "2232336"):
                    names[iid] = name

    manifest = []
    for obj in root.iter():
        if local(obj.tag) != "StorageObject":
            continue
        if not (obj.get("name") or "").endswith("LoadablePlusCodeBlockData"):
            continue
        inst = obj.get("instId", "")
        name = names.get(inst, "block_" + inst)
        open_imm = blob(obj, "OpenImmediateData")
        dep = None
        for rel in obj.iter():
            if local(rel.tag) != "relation":
                continue
            for link in rel:
                if local(link.tag) != "link":
                    continue
                t = (link.text or "").strip()
                if t.startswith("2232327-"):
                    did = t.split("-", 1)[1]
                    dep = next((o for o in root.iter()
                                if local(o.tag) == "StorageObject" and o.get("instId") == did), None)
        if dep is None:
            print("no dependent for", name)
            continue
        mc7 = blob(dep, "MC7PlusData")
        fam = blob(dep, "FamilyKey")
        sig = blob(dep, "OwnSignatureData")
        opti = blob(dep, "OptimizeInfoData")
        closed = blob(dep, "ClosedImmediateData")
        secs = sections(mc7)
        safe = re.sub(r"[^A-Za-z0-9_.-]+", "_", name)
        for idx, data in secs:
            (out_dir / f"{safe}.sec{idx}.bin").write_bytes(data)
        (out_dir / f"{safe}.open.bin").write_bytes(open_imm)
        (out_dir / f"{safe}.mc7plus.bin").write_bytes(mc7)
        manifest.append({
            "block": name,
            "openImmediateData": open_imm.hex(),
            "mc7PlusLength": len(mc7),
            "sections": [{"index": i, "length": len(d)} for i, d in secs],
            "familyKeyLength": len(fam),
            "ownSignatureLength": len(sig),
            "optimizeInfoLength": len(opti),
            "closedImmediateLength": len(closed),
        })
    (out_dir / "manifest.json").write_text(json.dumps(manifest, indent=2), encoding="utf-8")
    print(json.dumps(manifest, indent=2))


if __name__ == "__main__":
    main()
