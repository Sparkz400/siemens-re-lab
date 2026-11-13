"""Tests for the native project-review comparison (no TIA/MCP required)."""
import copy
import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from tia_compare import compare_reviews

ROOT = "/projects/Demo"
PLC = ROOT + "/devices/PLC"
BLOCK = PLC + "/blocks/Main"
TAG1 = PLC + "/tags/Tag01"
TAG2 = PLC + "/tags/Tag02"


def node(path, kind, name, node_id, metadata=None):
    return {
        "objectPath": path,
        "kind": kind,
        "name": name,
        "nodeId": node_id,
        "origin": "native",
        "access": "ReadWrite",
        "metadata": metadata or {},
    }


def base_nodes():
    return [
        node(ROOT, "project", "Demo", "n0"),
        node(PLC, "device", "PLC", "n1"),
        node(BLOCK, "block", "Main", "n2",
             {"programmingLanguage": "SCL"}),
        node(TAG1, "tag", "Tag01", "n3"),
    ]


def review(nodes, documents=None, project_path="C:/demo/Demo.ap21",
           host="lab", edges=None):
    return {
        "schemaVersion": "project-review.v1",
        "model": {
            "schemaVersion": "project-model.v1",
            "project": {"name": "Demo", "projectPath": project_path,
                        "host": host},
            "nodes": nodes,
            "edges": edges or [],
            "coverage": {"complete": False},
        },
        "documents": documents or [],
        "capture": {},
    }


class CompareReviewsTest(unittest.TestCase):
    def test_unchanged(self):
        before = review(copy.deepcopy(base_nodes()))
        after = review(copy.deepcopy(base_nodes()))
        result = compare_reviews(before, after, "a", "b")
        self.assertEqual(result["totalChanges"], 0)
        self.assertFalse(result["wholeProjectEquivalent"])

    def test_added_and_modified(self):
        before_nodes = copy.deepcopy(base_nodes())
        after_nodes = copy.deepcopy(base_nodes())
        after_nodes[2]["name"] = "Main_v2"
        after_nodes[2]["metadata"] = {"programmingLanguage": "SCL",
                                      "version": "2"}
        after_nodes.append(node(TAG2, "tag", "Tag02", "n4"))
        result = compare_reviews(
            review(before_nodes), review(after_nodes), "a", "b")
        changes = {row["path"]: row for row in result["changes"]}
        self.assertEqual(result["totalChanges"], 2)
        self.assertEqual(changes["/devices/PLC/tags/Tag02"]["change"],
                         "addedToCapturedScope")
        modified = changes["/devices/PLC/blocks/Main"]
        self.assertEqual(modified["change"], "modified")
        self.assertEqual(modified["changedFields"], ["name", "metadata"])

    def test_removed(self):
        before_nodes = copy.deepcopy(base_nodes())
        after_nodes = [n for n in copy.deepcopy(base_nodes())
                       if n["objectPath"] != TAG1]
        result = compare_reviews(
            review(before_nodes), review(after_nodes), "a", "b")
        self.assertEqual(result["totalChanges"], 1)
        self.assertEqual(result["changes"][0]["change"],
                         "removedFromCapturedScope")

    def test_block_content_signature_change(self):
        before_doc = {
            "objectPath": BLOCK, "status": "exported",
            "signatures": {"interface": "i", "logic": "l", "document": "d"},
        }
        after_doc = copy.deepcopy(before_doc)
        after_doc["signatures"]["logic"] = "l2"
        result = compare_reviews(
            review(copy.deepcopy(base_nodes()), [before_doc]),
            review(copy.deepcopy(base_nodes()), [after_doc]),
            "a", "b")
        self.assertEqual(result["totalChanges"], 1)
        self.assertEqual(result["changes"][0]["changedFields"],
                         ["logicChanged"])
        self.assertEqual(result["contentComparedBlocks"], 1)
        self.assertTrue(result["changes"][0]["contentCompared"])

    def test_project_copy_guard(self):
        before = review(copy.deepcopy(base_nodes()),
                        project_path="C:/demo/One.ap21")
        after = review(copy.deepcopy(base_nodes()),
                       project_path="C:/demo/Two.ap21")
        with self.assertRaises(ValueError):
            compare_reviews(before, after, "a", "b")
        result = compare_reviews(
            before, after, "a", "b", allow_different_project_copy=True)
        self.assertEqual(result["totalChanges"], 0)

    def test_impact_path(self):
        after_nodes = copy.deepcopy(base_nodes())
        after_nodes[2]["metadata"] = {"programmingLanguage": "SCL",
                                      "version": "2"}
        edges = [{
            "kind": "reads",
            "source": {"objectPath": TAG1},
            "target": {"objectPath": BLOCK},
            "evidence": {"resolution": "resolved",
                         "origin": "nativeConfirmed"},
        }]
        result = compare_reviews(
            review(copy.deepcopy(base_nodes())),
            review(after_nodes, edges=edges), "a", "b")
        affected = {p["affectedObjectPath"]
                    for p in result["observedImpactPaths"]}
        self.assertIn(TAG1, affected)


if __name__ == "__main__":
    unittest.main()
