"""Tests for the TIA MCP stdio client and snapshot report formatting."""
import json
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from tia_mcp import McpHost, McpToolError
from tia_snapshot import render_markdown, resolve_archive


class McpClientTest(unittest.TestCase):
    def test_roundtrip_and_tool_error(self):
        fake = Path(__file__).with_name("fake_mcp_host.py")
        host = McpHost(command=[sys.executable, str(fake)])
        try:
            host.start()
            self.assertEqual(host.call_tool("echo", {"x": 1}), {"x": 1})
            with self.assertRaises(McpToolError):
                host.call_tool("fail", {})
        finally:
            host.close()


class SnapshotTest(unittest.TestCase):
    def test_resolve_archive(self):
        with tempfile.TemporaryDirectory() as tmp:
            project = Path(tmp) / "demo.ap21"
            project.write_bytes(b"demo")
            path, digest, size = resolve_archive(str(Path(tmp)))
            self.assertEqual(path, project.resolve())
            self.assertEqual(size, 4)
            self.assertEqual(len(digest), 64)

    def test_markdown_report(self):
        result = {
            "totalChanges": 2,
            "contentComparedBlocks": 1,
            "contentUnknownBlocks": 0,
            "contentComparedTypes": 0,
            "contentUnknownTypes": 0,
            "comparisonPolicy": "exact paths",
            "changes": [
                {"change": "addedToCapturedScope", "kind": "tag",
                 "path": "PLC/Tags/Tag01", "changedFields": []},
                {"change": "modified", "kind": "block",
                 "path": "PLC/Main [OB1]", "changedFields": ["logicChanged"]},
            ],
        }
        before = {"capturedUtc": "a", "project": {"name": "p", "archiveSha256": "a" * 64},
                  "tiaMcp": {"reviewId": "b" * 64}}
        after = {"capturedUtc": "b", "project": {"name": "p", "archiveSha256": "c" * 64},
                 "tiaMcp": {"reviewId": "d" * 64}}
        report = render_markdown(result, before, after)
        self.assertIn("2 change(s)", report)
        self.assertIn("Tag01", report)
        self.assertIn("logicChanged", report)


if __name__ == "__main__":
    unittest.main()
