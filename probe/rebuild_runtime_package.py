#!/usr/bin/env python3
"""Rebuild a WinCC Unified runtime package from a TIA project's compiled HMI output.

The project keeps the compiled output under ``IM\\HMI\\I\\0\\Saved``.  This tool reads its
``DownloadTask.xml`` manifest, adds ``Download.config`` and the machine certificate from a
reference package (the last TIA *Download to file* zip), and writes a flat runtime package
accepted by ``SIMATICRuntimeManager.exe -c fulldownload``.

  python rebuild_runtime_package.py \
      --saved "...\\IM\\HMI\\I\\0\\Saved" \
      --reference "%USERPROFILE%\\Documents\\Simatic.HMI\\RT_Projects\\... - Full ....zip" \
      --out artifacts/runtime-m2.zip

This is a lab procedure, not a supported Siemens workflow.  Compile the HMI in TIA first; the
manifest must list every file the runtime needs.
"""
from __future__ import annotations

import argparse
import os
import sys
import xml.etree.ElementTree as ET
import zipfile
from pathlib import Path

CERT = "config/CertStore/own/private/PW.0.cert.pem"
DOWNLOAD_CONFIG = "Download.config"
TASK_XML = "DownloadTask.xml"


def entries_from_manifest(path: Path) -> list[str]:
    root = ET.parse(path).getroot()
    out = []
    for element in root.findall("FullDownload"):
        if element.text:
            out.append(element.text.replace("\\", "/"))
    return out


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--saved", required=True,
                    help="project IM\\HMI\\I\\0\\Saved directory")
    ap.add_argument("--reference", required=True,
                    help="previous runtime package (.zip) used for Download.config/cert")
    ap.add_argument("--out", required=True, help="output runtime package (.zip)")
    ap.add_argument("--cert", help="override certificate path")
    a = ap.parse_args(argv)

    saved = Path(a.saved)
    manifest = saved / TASK_XML
    if not manifest.is_file():
        print(f"missing manifest: {manifest}", file=sys.stderr)
        return 2

    ref = zipfile.ZipFile(a.reference)
    ref_names = set(ref.namelist())

    tree = ET.parse(manifest)
    root = tree.getroot()
    entries = entries_from_manifest(manifest)
    if CERT not in entries:
        ET.SubElement(root, "FullDownload").text = CERT.replace("/", "\\")
        entries.append(CERT)

    cert_path = Path(a.cert) if a.cert else None
    missing = []

    out = Path(a.out)
    out.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED) as z:
        for name in entries:
            source = saved / name
            if source.is_file():
                z.write(source, name)
                continue
            if name == CERT:
                if cert_path and cert_path.is_file():
                    z.write(cert_path, name)
                    continue
                if CERT in ref_names:
                    z.writestr(name, ref.read(CERT))
                    continue
            if name == DOWNLOAD_CONFIG and DOWNLOAD_CONFIG in ref_names:
                z.writestr(name, ref.read(DOWNLOAD_CONFIG))
                continue
            if name in ref_names:
                print(f"warning: {name} missing in Saved; using reference package",
                      file=sys.stderr)
                z.writestr(name, ref.read(name))
                continue
            missing.append(name)

        z.writestr(DOWNLOAD_CONFIG, ref.read(DOWNLOAD_CONFIG))
        z.writestr(TASK_XML, ET.tostring(root, encoding="utf-8",
                                         xml_declaration=True))

    ref.close()
    if missing:
        print("missing files: " + ", ".join(missing), file=sys.stderr)
        return 1

    print(f"wrote {out} ({out.stat().st_size} bytes, "
          f"{len(entries) + 2} entries)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
