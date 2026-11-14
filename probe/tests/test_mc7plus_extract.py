from __future__ import annotations

import sys
import unittest
from pathlib import Path

PROBE_DIR = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(PROBE_DIR))

import mc7plus_extract as extract  # noqa: E402


class PlusExtractTests(unittest.TestCase):
    def test_sections_parses_framed_container(self) -> None:
        payload = (
            b"\xef\xbe\xad\xde" + (1).to_bytes(4, "little") + (3).to_bytes(4, "little") + b"abc"
            + b"junk"
            + b"\xef\xbe\xad\xde" + (2).to_bytes(4, "little") + (2).to_bytes(4, "little") + b"xy"
        )
        result = extract.sections(payload)
        self.assertEqual([(i, d) for i, d in result], [(1, b"abc"), (2, b"xy")])

    def test_sections_empty(self) -> None:
        self.assertEqual(extract.sections(b"not a container"), [])

    def test_blob_reads_hex_attribute(self) -> None:
        import xml.etree.ElementTree as ET

        xml = (
            '<x:StorageObject xmlns:x="urn:test" name="Siemens.Simatic.Lang.Model.Blocks.LoadablePlusCodeBlockData">'
            '<x:attribSet><x:attrib name="OpenImmediateData"><x:coreblob>15000300</x:coreblob></x:attrib></x:attribSet>'
            "</x:StorageObject>"
        )
        obj = ET.fromstring(xml)
        self.assertEqual(extract.blob(obj, "OpenImmediateData"), bytes.fromhex("15000300"))
        self.assertEqual(extract.blob(obj, "Missing"), b"")


if __name__ == "__main__":
    unittest.main()
