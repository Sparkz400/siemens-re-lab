from __future__ import annotations

import contextlib
import io
import hashlib
import json
import sys
import tempfile
import unittest
from pathlib import Path
from unittest import mock

PROBE_DIR = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(PROBE_DIR))

import mc7_classic_extract as extract  # noqa: E402


def objectframe_xml(binary: bytes, end_sac: int, number: int = 2) -> str:
    debug_info = f'<?xml version="1.0"?><BlockDebugInfo><Net NetID="1" BeginSAC="0" EndSAC="{end_sac}" /></BlockDebugInfo>'
    return f"""<?xml version="1.0" encoding="utf-8"?>
<x:StorageObject xmlns:x="urn:siemens-re-lab:test" name="Siemens.Simatic.Lang.Model.Blocks.LoadableCodeBlockData">
  <x:attribSet>
    <x:attrib name="BlockNumber"><x:int32>{number}</x:int32></x:attrib>
    <x:attrib name="BinaryData"><x:coreblob>{binary.hex()}</x:coreblob></x:attrib>
    <x:attrib name="BlockDebugInformation"><x:coreblob>{debug_info.encode('utf-8').hex()}</x:coreblob></x:attrib>
  </x:attribSet>
</x:StorageObject>
"""


class ExtractClassicTests(unittest.TestCase):
    def run_extract(self, xml: str) -> tuple[dict, Path]:
        temp = tempfile.TemporaryDirectory()
        self.addCleanup(temp.cleanup)
        root = Path(temp.name)
        xml_path = root / "export.objectframe.xml"
        xml_path.write_text(xml, encoding="utf-8")
        out_dir = root / "blocks"
        argv = [
            "mc7_classic_extract.py",
            "--xml",
            str(xml_path),
            "--out",
            str(out_dir),
        ]
        with contextlib.redirect_stdout(io.StringIO()):
            with mock.patch.object(sys, "argv", argv):
                self.assertEqual(extract.main(), 0)
        manifest = json.loads((out_dir / "manifest.json").read_text(encoding="utf-8"))
        return manifest, out_dir

    def test_extracts_end_sac_slice(self) -> None:
        code = bytes(range(26))
        binary = bytes(78) + code + b"\x00\x00\x00\x00"
        manifest, out_dir = self.run_extract(objectframe_xml(binary, 25))
        self.assertEqual(manifest["codeOffset"], 78)
        self.assertEqual(len(manifest["blocks"]), 1)
        row = manifest["blocks"][0]
        self.assertEqual(row["blockNumber"], "2")
        self.assertEqual(row["binaryLength"], 78 + 26 + 4)
        self.assertEqual(row["endSac"], 25)
        self.assertEqual(row["codeLength"], 26)
        self.assertEqual(row["codeSha256"], hashlib.sha256(code).hexdigest())
        self.assertEqual((out_dir / "block_2.bin").read_bytes(), code)

    def test_resolves_parent_block_name(self) -> None:
        code = bytes(range(26))
        binary = bytes(78) + code + b"\x00\x00\x00\x00"
        loadable = objectframe_xml(binary, 25)
        # Give the loadable a parent CodeBlockData name and relation.
        loadable = loadable.replace(
            'name="Siemens.Simatic.Lang.Model.Blocks.LoadableCodeBlockData"',
            'name="Siemens.Simatic.Lang.Model.Blocks.LoadableCodeBlockData" instId="200"',
        )
        parent = """<x:StorageObject xmlns:x="urn:siemens-re-lab:test" name="Siemens.Simatic.Lang.Model.Blocks.CodeBlockData" instId="100">
  <x:attribSet><x:attrib name="Name"><x:string>FC_And</x:string></x:attrib></x:attribSet>
  <x:relation><x:link>2232330-200</x:link></x:relation>
</x:StorageObject>
"""
        body = loadable.split("\n", 1)[1]
        wrapped = f'<x:root xmlns:x="urn:siemens-re-lab:test">{parent}{body}</x:root>'
        manifest, out_dir = self.run_extract(wrapped)
        row = manifest["blocks"][0]
        self.assertEqual(row["blockName"], "FC_And")
        self.assertEqual((out_dir / "FC_And.bin").read_bytes(), code)

    def test_invalid_end_sac_yields_no_code(self) -> None:
        binary = bytes(78) + bytes(range(26)) + b"\x00\x00\x00\x00"
        manifest, out_dir = self.run_extract(objectframe_xml(binary, 999))
        row = manifest["blocks"][0]
        self.assertEqual(row["endSac"], 999)
        self.assertEqual(row["codeLength"], 0)
        self.assertIsNone(row["codeSha256"])
        self.assertFalse((out_dir / "block_2.bin").exists())


if __name__ == "__main__":
    unittest.main()
