"""Tests for the TIA MCP stdio client and snapshot report formatting."""
import json
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from tia_mcp import McpHost, McpToolError
from tia_snapshot import folder_manifest, render_markdown, resolve_archive


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

    def test_folder_manifest_ignores_logs(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            (root / "a.txt").write_text("a", encoding="utf-8")
            (root / "Logs").mkdir()
            (root / "Logs" / "runtime.log").write_text("one", encoding="utf-8")
            first, files, _ = folder_manifest(root)
            (root / "Logs" / "runtime.log").write_text("two", encoding="utf-8")
            second, _, _ = folder_manifest(root)
            self.assertEqual(first, second)
            (root / "b.txt").write_text("b", encoding="utf-8")
            third, files3, _ = folder_manifest(root)
            self.assertNotEqual(first, third)
            self.assertEqual(files + 1, files3)

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
        self.assertIn("2 project change(s)", report)
        self.assertIn("Tag01", report)
        self.assertIn("logicChanged", report)

    def test_markdown_report_hmi_coverage(self):
        result = {
            "totalChanges": 0,
            "changes": [],
            "hmiModelCompared": True,
            "hmiChanges": [],
            "hmiCoverage": {
                "before": [{"status": "complete", "itemCount": 134}],
                "after": [{"status": "complete", "itemCount": 135}],
            },
        }
        before = {"capturedUtc": "a", "project": {"name": "p"}}
        after = {"capturedUtc": "b", "project": {"name": "p"}}
        report = render_markdown(result, before, after)
        self.assertIn("HMI model before: complete (134 items)", report)
        self.assertIn("after: complete (135 items)", report)
        self.assertIn("No HMI model changes", report)


if __name__ == "__main__":
    unittest.main()
