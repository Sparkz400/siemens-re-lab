#!/usr/bin/env python3
"""Native comparison of two TIA MCP project-review.v1 snapshots.

This is a Python port of the core `project_snapshot_compare` logic.  It needs
only the saved review JSON files: no TIA Portal, no TIA MCP Host, no Openness.

The result shape matches the MCP comparison fields used by ``tia_snapshot.py``
(`changes`, counts, coverage, impact paths).  Impact paths are a simplified
inbound static walk; the report marks the coverage as incomplete.
"""
from __future__ import annotations

from typing import Any

IMPACT_KINDS = {
    "calls", "reads", "writes", "instanceOf", "type", "contains",
    "configuredIo", "configuredSubnet", "configuredIoSystem",
    "technologyObjectHardware", "softwareUnit", "libraryVersion",
}
COMPARISON_POLICY = (
    "Exact relative native paths; no rename inference. Volatile metadata "
    "timestamps (modified, created, lastModified, modificationTime) are "
    "ignored. XML ignores only top-level DocumentInfo/Created. Whitespace, "
    "IDs, namespaces, comments and unknown payload remain significant. Scope "
    "absence is not proof of deletion."
)
IGNORED_METADATA_KEYS = {"modified", "created", "lastModified",
                         "modificationTime", "UpdateId"}


def _normalize_metadata(value: Any) -> Any:
    if isinstance(value, dict):
        return {key: _normalize_metadata(item)
                for key, item in value.items()
                if key not in IGNORED_METADATA_KEYS}
    if isinstance(value, list):
        return [_normalize_metadata(item) for item in value]
    return value


def _nodes(model: dict) -> dict[str, dict]:
    result: dict[str, dict] = {}
    for row in model.get("nodes") or []:
        path = row.get("objectPath") or ""
        if not path:
            continue
        if path in result:
            raise ValueError(f"duplicate native object path: {path}")
        result[path] = row
    return result


def _project_root(model: dict) -> str:
    projects = [n for n in _nodes(model).values()
                if n.get("kind") == "project"]
    if len(projects) != 1:
        raise ValueError("review must contain exactly one project node")
    return projects[0].get("objectPath") or ""


def _relative(path: str, root: str) -> str:
    if path == root:
        return "/"
    prefix = root + "/"
    if not path.startswith(prefix):
        raise ValueError(f"object is outside the captured project root: {path}")
    return path[len(root):]


def _documents(review: dict, root: str) -> dict[str, dict]:
    result: dict[str, dict] = {}
    for row in review.get("documents") or []:
        path = row.get("objectPath") or ""
        if path:
            result[_relative(path, root)] = row
    return result


def _signatures(document: dict | None) -> dict:
    return (document or {}).get("signatures") or {}


def _hmi_kind(item: dict) -> str:
    native = item.get("nativeType") or ""
    if native:
        return native.rsplit(".", 1)[-1]
    return item.get("kind") or "hmiModelNode"


def _hmi_coverage(models: list[dict] | None) -> list[dict]:
    rows = []
    for model in models or []:
        data = model.get("data") or {}
        coverage = data.get("coverage") or {}
        rows.append({
            "hmiPath": model.get("hmiPath"),
            "file": model.get("file"),
            "status": coverage.get("status"),
            "complete": coverage.get("complete"),
            "itemCount": len(data.get("items") or []),
            "error": model.get("error"),
        })
    return rows


def compare_hmi_models(before_models: list[dict] | None,
                       after_models: list[dict] | None,
                       project_changes: list[dict]) -> tuple[list[dict], list[str], list[str]]:
    """Compare hmi_inspect section=model captures, deduped by project path."""
    before_by_path = {
        m.get("hmiPath"): m for m in (before_models or [])
        if m.get("data") and m.get("hmiPath")
    }
    after_by_path = {
        m.get("hmiPath"): m for m in (after_models or [])
        if m.get("data") and m.get("hmiPath")
    }
    common_paths = sorted(set(before_by_path) & set(after_by_path))
    unmatched_paths = sorted(set(before_by_path) ^ set(after_by_path))
    project_paths = {
        change.get("afterObjectPath") or change.get("beforeObjectPath")
        for change in project_changes
    }
    changes: list[dict] = []
    for hmi_path in common_paths:
        before = before_by_path[hmi_path]["data"]
        after = after_by_path[hmi_path]["data"]
        before_items = {item.get("path"): item
                        for item in before.get("items") or []
                        if item.get("path")}
        after_items = {item.get("path"): item
                       for item in after.get("items") or []
                       if item.get("path")}
        for path in sorted(set(before_items) | set(after_items)):
            old = before_items.get(path)
            new = after_items.get(path)
            change = {
                "hmiPath": hmi_path,
                "path": path,
                "kind": _hmi_kind(new or old or {}),
                "composition": (new or old or {}).get("composition"),
            }
            if old is None or new is None:
                change["change"] = ("addedToCapturedScope" if old is None
                                    else "removedFromCapturedScope")
            else:
                fields = []
                for field in ("name", "composition", "nativeType"):
                    if old.get(field) != new.get(field):
                        fields.append(field)
                if (_normalize_metadata(old.get("attributes")) !=
                        _normalize_metadata(new.get("attributes"))):
                    fields.append("attributes")
                if not fields:
                    continue
                change["change"] = "modified"
                change["changedFields"] = fields
            if path in project_paths:
                continue
            changes.append(change)
    return changes, common_paths, unmatched_paths


def compare_reviews(
    before: dict,
    after: dict,
    before_review_id: str,
    after_review_id: str,
    allow_different_project_copy: bool = False,
    impact_depth: int = 3,
    max_impact_paths: int = 100,
    max_impact_edge_visits: int = 5000,
    before_hmi: list[dict] | None = None,
    after_hmi: list[dict] | None = None,
) -> dict:
    a_model = before.get("model") or {}
    b_model = after.get("model") or {}
    a_project = a_model.get("project") or {}
    b_project = b_model.get("project") or {}
    if not allow_different_project_copy and (
            a_project.get("projectPath") != b_project.get("projectPath") or
            a_project.get("host") != b_project.get("host")):
        raise ValueError(
            "different project copies require allow_different_project_copy")

    a_root = _project_root(a_model)
    b_root = _project_root(b_model)
    a_nodes = {_relative(path, a_root): row
               for path, row in _nodes(a_model).items()}
    b_nodes = {_relative(path, b_root): row
               for path, row in _nodes(b_model).items()}
    a_docs = _documents(before, a_root)
    b_docs = _documents(after, b_root)

    changes = []
    content_compared_blocks = content_unknown_blocks = 0
    content_compared_types = content_unknown_types = 0

    for path in sorted(set(a_nodes) | set(b_nodes)):
        old = a_nodes.get(path)
        new = b_nodes.get(path)
        change = {
            "path": path,
            "beforeObjectPath": old.get("objectPath") if old else None,
            "afterObjectPath": new.get("objectPath") if new else None,
            "kind": (new or old or {}).get("kind"),
        }
        if old is None or new is None:
            change["change"] = ("addedToCapturedScope" if old is None
                                else "removedFromCapturedScope")
            changes.append(change)
            continue

        fields = []
        for field in ("name", "kind", "access", "metadata"):
            if field == "metadata":
                if (_normalize_metadata(old.get(field)) !=
                        _normalize_metadata(new.get(field))):
                    fields.append(field)
            elif old.get(field) != new.get(field):
                fields.append(field)
        old_doc = a_docs.get(path)
        new_doc = b_docs.get(path)
        is_block = old.get("kind") == "block" or new.get("kind") == "block"
        is_type = old.get("kind") == "type" or new.get("kind") == "type"
        if is_block or is_type:
            if ((old_doc or {}).get("status") == "exported" and
                    (new_doc or {}).get("status") == "exported"):
                if is_block:
                    content_compared_blocks += 1
                if is_type:
                    content_compared_types += 1
                old_sig = _signatures(old_doc)
                new_sig = _signatures(new_doc)
                for part in ("interface", "logic", "document"):
                    if old_sig.get(part) != new_sig.get(part):
                        fields.append(part + "Changed")
                change["contentCompared"] = True
            else:
                if is_block:
                    content_unknown_blocks += 1
                if is_type:
                    content_unknown_types += 1
                change["contentCompared"] = False

        if not fields:
            continue
        change["change"] = "modified"
        change["changedFields"] = fields
        changes.append(change)

    changed_paths = {c.get("afterObjectPath") for c in changes
                     if c.get("afterObjectPath")}
    direct_impacts = []
    for edge in b_model.get("edges") or []:
        target = (edge.get("target") or {}).get("objectPath") or ""
        source = (edge.get("source") or {}).get("objectPath") or ""
        if target in changed_paths and source:
            direct_impacts.append({
                "kind": edge.get("kind"),
                "source": edge.get("source"),
                "target": edge.get("target"),
                "evidence": edge.get("evidence"),
            })

    impact_paths, truncated, traversal = _impact_paths(
        before, after, before_review_id, after_review_id, changes,
        impact_depth, max_impact_paths, max_impact_edge_visits)
    hmi_changes, hmi_common, hmi_unmatched = compare_hmi_models(
        before_hmi, after_hmi, changes)
    hmi_available = bool(hmi_common)

    return {
        "beforeReviewId": before_review_id,
        "afterReviewId": after_review_id,
        "changes": changes,
        "totalChanges": len(changes),
        "hmiChanges": hmi_changes,
        "totalHmiChanges": len(hmi_changes),
        "hmiModelCompared": hmi_available,
        "hmiModelCommonPaths": hmi_common,
        "hmiModelUnmatchedPaths": hmi_unmatched,
        "hmiCoverage": {
            "before": _hmi_coverage(before_hmi),
            "after": _hmi_coverage(after_hmi),
        },
        "totalChangesIncludingHmi": len(changes) + len(hmi_changes),
        "nextOffset": None,
        "contentComparedBlocks": content_compared_blocks,
        "contentUnknownBlocks": content_unknown_blocks,
        "contentComparedTypes": content_compared_types,
        "contentUnknownTypes": content_unknown_types,
        "directObservedImpacts": direct_impacts,
        "impactTruncated": truncated,
        "impactCoverageComplete": False,
        "observedImpactPaths": impact_paths,
        "impactTraversal": traversal,
        "beforeCoverage": a_model.get("coverage"),
        "afterCoverage": b_model.get("coverage"),
        "wholeProjectEquivalent": False,
        "comparisonPolicy": COMPARISON_POLICY,
        "comparisonEngine": "python-native",
        "sourceEvidencePolicy": (
            "Saved project-review.v1 snapshots only; no live TIA instance."),
        "sourceAuthenticityVerified": False,
    }


def _edge_allowed(edge: dict) -> bool:
    """Keep resolved native/parsed edges; synthetic reviews without evidence are allowed."""
    evidence = edge.get("evidence")
    if not evidence:
        return True
    if evidence.get("resolution") not in ("resolved", None):
        return False
    if evidence.get("origin") not in ("nativeConfirmed", "parsedSupported", None):
        return False
    return edge.get("kind") in IMPACT_KINDS or evidence.get("origin") is None


def _impact_paths(before: dict, after: dict, before_id: str, after_id: str,
                  changes: list[dict], depth: int, max_paths: int,
                  max_edges: int) -> tuple[list[dict], bool, dict]:
    paths: list[dict] = []
    truncated = False
    visited_edges = 0
    for side, review, review_id in (("before", before, before_id),
                                    ("after", after, after_id)):
        model = review.get("model") or {}
        incoming: dict[str, list[tuple[str, dict]]] = {}
        for edge in model.get("edges") or []:
            if not _edge_allowed(edge):
                continue
            source = (edge.get("source") or {}).get("objectPath") or ""
            target = (edge.get("target") or {}).get("objectPath") or ""
            if source and target:
                incoming.setdefault(target, []).append((source, edge))
        for change in changes:
            target = (change.get("afterObjectPath") if side == "after"
                      else change.get("beforeObjectPath"))
            if not target:
                continue
            queue = [(target, [target], [])]
            while queue:
                current, object_paths, edge_paths = queue.pop(0)
                for source, edge in incoming.get(current, []):
                    if source in object_paths:
                        continue
                    if visited_edges >= max_edges or len(paths) >= max_paths:
                        truncated = True
                        break
                    visited_edges += 1
                    new_objects = [source] + object_paths
                    new_edges = [edge] + edge_paths
                    paths.append({
                        "reviewSide": side,
                        "reviewId": review_id,
                        "changedPath": change.get("path"),
                        "changedObjectPath": target,
                        "change": change.get("change"),
                        "changedFields": change.get("changedFields"),
                        "affectedObjectPath": source,
                        "distance": len(new_edges),
                        "objectPaths": new_objects,
                        "edges": new_edges,
                    })
                    if len(new_edges) < depth:
                        queue.append((source, new_objects, new_edges))
                if truncated:
                    break
            if truncated:
                break
        if truncated:
            break

    traversal = {
        "complete": False,
        "freshness": "savedSnapshotsOnly",
        "truncated": truncated,
        "returnedPaths": len(paths),
        "visitedEdges": visited_edges,
        "limits": {
            "impactDepth": depth,
            "maxImpactPaths": max_paths,
            "maxImpactEdgeVisits": max_edges,
        },
        "policy": (
            "Simplified inbound static paths over saved snapshots. No "
            "cross-snapshot joins; no runtime-effect claim."),
    }
    return paths, truncated, traversal
