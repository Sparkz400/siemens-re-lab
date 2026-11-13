#!/usr/bin/env python3
"""TIA project snapshot and diff CLI.

Capture a TIA MCP ``project-review.v1`` snapshot (native hierarchy + block XML),
store it portably, and compare two snapshots offline.

  python tia_snapshot.py capture --project <project.ap21> --out snapshots/base
  python tia_snapshot.py compare --before snapshots/base --after snapshots/change \
      --out diff

Capture needs TIA Portal/Openness and starts a private TIA MCP Host with the native
worker.  Compare is pure Python over the saved review JSON: no TIA, no MCP Host.

The snapshot directory contains ``snapshot.json`` (metadata), ``review.json``
(the TIA MCP project-review.v1 content) and one ``hmi-model*.json`` per HMI
(paginated ``hmi_inspect section=model`` readback).  Keep snapshots out of git.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import sys
import time
from datetime import datetime, timezone
from pathlib import Path

from tia_compare import compare_reviews
from tia_mcp import DEFAULT_ARTIFACTS, DEFAULT_BUNDLE, McpHost, McpError

SNAPSHOT_SCHEMA = "tia-snapshot.v1"


def utc_now() -> str:
    return datetime.now(timezone.utc).isoformat(timespec="milliseconds").replace("+00:00", "Z")


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with open(path, "rb") as handle:
        for block in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def resolve_archive(project: str) -> tuple[Path, str, int]:
    path = Path(project).expanduser().resolve()
    if path.is_dir():
        matches = sorted(path.rglob("*.ap21"))
        if len(matches) != 1:
            raise SystemExit(
                f"expected exactly one .ap21 under {path}, found {len(matches)}")
        path = matches[0]
    if not path.is_file():
        raise SystemExit(f"project archive not found: {path}")
    return path, sha256_file(path), path.stat().st_size


VOLATILE_DIRS = {"Logs", "TMP"}
VOLATILE_SUFFIXES = {".log", ".tmp", ".bak"}


def folder_manifest(root: Path) -> tuple[str, int, int]:
    """Stable manifest hash of the project folder, excluding logs/temp files."""
    digest = hashlib.sha256()
    count = 0
    total = 0
    for path in sorted(root.rglob("*")):
        if not path.is_file():
            continue
        relative = path.relative_to(root)
        if any(part in VOLATILE_DIRS for part in relative.parts):
            continue
        if path.suffix.lower() in VOLATILE_SUFFIXES:
            continue
        digest.update(relative.as_posix().encode("utf-8"))
        digest.update(b"\0")
        digest.update(sha256_file(path).encode("ascii"))
        digest.update(b"\n")
        count += 1
        total += path.stat().st_size
    return digest.hexdigest(), count, total


def capture_hmi_models(host, review: dict, out: Path, args) -> list[dict]:
    """Capture hmi_inspect section=model for every HMI in the review."""
    nodes = (review.get("model") or {}).get("nodes") or []
    hmi_nodes = [n for n in nodes if n.get("kind") == "hmi"]
    results: list[dict] = []
    for index, node in enumerate(hmi_nodes, 1):
        hmi_path = node.get("objectPath")
        family = (node.get("metadata") or {}).get("family") or "unified"
        try:
            items: list[dict] = []
            offset = 0
            pages = 0
            coverage = None
            target = None
            while pages < args.hmi_max_pages:
                result = host.call_tool(
                    "hmi_inspect",
                    {"request": {
                        "section": "model",
                        "hmiPath": hmi_path,
                        "family": family,
                        "offset": offset,
                    }},
                    timeout=args.timeout)
                items.extend(result.get("items") or [])
                pages += 1
                coverage = result.get("coverage") or coverage
                target = result.get("target") or target
                next_offset = result.get("nextOffset")
                if not next_offset:
                    break
                offset = next_offset
            model = {
                "schemaVersion": "hmi-model.v1",
                "capturedUtc": utc_now(),
                "hmiPath": hmi_path,
                "target": target,
                "items": items,
                "coverage": coverage,
                "pages": pages,
                "nextOffset": next_offset,
            }
            name = ("hmi-model.json" if len(hmi_nodes) == 1
                    else f"hmi-model-{index}.json")
            data = (json.dumps(model, indent=2, ensure_ascii=False) + "\n").encode("utf-8")
            (out / name).write_bytes(data)
            results.append({
                "hmiPath": hmi_path,
                "file": name,
                "sha256": hashlib.sha256(data).hexdigest(),
                "itemCount": len(items),
                "pages": pages,
                "nextOffset": next_offset,
                "coverage": coverage,
                "target": target,
            })
            print(f"hmi model: {hmi_path} -> {len(items)} items / {pages} page(s)")
        except McpError as exc:
            results.append({"hmiPath": hmi_path, "error": str(exc)})
            print(f"hmi model failed for {hmi_path}: {exc}", file=sys.stderr)
    return results


def capture(args) -> int:
    archive, archive_hash, archive_bytes = resolve_archive(args.project)
    folder_hash, folder_files, folder_bytes = folder_manifest(archive.parent)
    out = Path(args.out).expanduser().resolve()
    if (out / "snapshot.json").exists() and not args.force:
        raise SystemExit(f"snapshot already exists: {out} (use --force to overwrite)")
    out.mkdir(parents=True, exist_ok=True)

    log_path = str(Path(args.log).expanduser().resolve()) if args.log else str(out / "host.log")
    with McpHost(args.bundle, args.artifacts, native=True,
                 timeout_seconds=args.timeout, log_path=log_path) as host:
        identity = host.call_tool("host_build_identity", {}, timeout=60)
        started = host.call_tool("tia_start", {}, timeout=args.timeout)
        opened = host.call_tool(
            "project_open",
            {"path": str(archive), "readOnly": False},
            timeout=args.timeout)
        project = (opened or {}).get("project") or {}
        project_name = args.project_name or project.get("name") or archive.stem

        request = {
            "projectName": project_name,
            "maxVisited": args.max_visited,
            "maxBlocks": args.max_blocks,
            "maxRelationshipRoots": args.max_relationship_roots,
        }
        if args.plc:
            request["plcPath"] = args.plc
        capture_result = host.call_tool(
            "project_snapshot_capture", {"request": request},
            timeout=args.timeout)

        review_id = capture_result.get("reviewId")
        artifact = capture_result.get("artifact") or {}
        artifact_path = Path(artifact.get("path", ""))
        if not artifact_path.is_absolute():
            artifact_path = Path(args.artifacts) / artifact_path
        if not review_id or not artifact_path.is_file():
            raise SystemExit(f"capture did not return a review artifact: {artifact}")
        review_bytes = artifact_path.read_bytes()
        review_doc = json.loads(review_bytes)
        hmi_models = (capture_hmi_models(host, review_doc, out, args)
                      if args.hmi_model else [])
        try:
            host.call_tool("tia_detach", {}, timeout=60)
        except McpError:
            pass

    actual_id = hashlib.sha256(review_bytes).hexdigest()
    if actual_id != review_id:
        raise SystemExit(
            f"review hash mismatch: host says {review_id}, file is {actual_id}")

    (out / "review.json").write_bytes(review_bytes)
    snapshot = {
        "schemaVersion": SNAPSHOT_SCHEMA,
        "capturedUtc": utc_now(),
        "hmiModels": hmi_models,
        "project": {
            "path": str(archive),
            "name": project_name,
            "archiveSha256": archive_hash,
            "archiveBytes": archive_bytes,
            "projectFolderSha256": folder_hash,
            "projectFolderFiles": folder_files,
            "projectFolderBytes": folder_bytes,
        },
        "tiaMcp": {
            "bundle": str(args.bundle),
            "host": {
                "productVersion": identity.get("productVersion"),
                "bundle": identity.get("bundle"),
                "runningMatchesDisk": identity.get("runningMatchesDisk"),
                "hostProcessId": identity.get("hostProcessId"),
            },
            "reviewId": review_id,
            "reviewSha256": actual_id,
            "reviewFile": "review.json",
            "capture": {
                key: capture_result.get(key)
                for key in ("nodeCount", "exportedBlocks", "exportedTypes",
                            "wholeProjectContentComplete")
            },
            "coverage": capture_result.get("capture"),
        },
        "request": request,
        "open": opened,
        "tiaStart": started,
    }
    (out / "snapshot.json").write_text(
        json.dumps(snapshot, indent=2, ensure_ascii=False) + "\n",
        encoding="utf-8")
    print(f"snapshot: {out}")
    print(f"project:  {project_name}")
    print(f"review:   {review_id}")
    print(f"nodes:    {capture_result.get('nodeCount')}  "
          f"blocks: {capture_result.get('exportedBlocks')}  "
          f"types: {capture_result.get('exportedTypes')}")
    print(f"archive:  {archive_hash}")
    print(f"folder:   {folder_hash} ({folder_files} files, {folder_bytes} bytes)")
    return 0


def load_snapshot(path: str) -> tuple[dict, dict]:
    directory = Path(path).expanduser().resolve()
    meta_file = directory / "snapshot.json"
    if not meta_file.is_file():
        raise SystemExit(f"missing {meta_file}")
    meta = json.loads(meta_file.read_text(encoding="utf-8"))
    review_name = (meta.get("tiaMcp") or {}).get("reviewFile", "review.json")
    review_file = directory / review_name
    if not review_file.is_file():
        raise SystemExit(f"missing {review_file}")
    review_id = (meta.get("tiaMcp") or {}).get("reviewId")
    actual = hashlib.sha256(review_file.read_bytes()).hexdigest()
    if review_id and actual != review_id:
        raise SystemExit(
            f"review hash mismatch in {directory}: {review_id} != {actual}")
    review = json.loads(review_file.read_text(encoding="utf-8"))
    if review.get("schemaVersion") != "project-review.v1":
        raise SystemExit(
            f"unsupported review schema in {review_file}: "
            f"{review.get('schemaVersion')!r}")
    hmi_models = []
    for entry in meta.get("hmiModels") or []:
        if not entry.get("file"):
            hmi_models.append(entry)
            continue
        hmi_file = directory / entry["file"]
        if not hmi_file.is_file():
            raise SystemExit(f"missing HMI model file: {hmi_file}")
        data = hmi_file.read_bytes()
        actual_hmi = hashlib.sha256(data).hexdigest()
        if entry.get("sha256") and actual_hmi != entry["sha256"]:
            raise SystemExit(
                f"HMI model hash mismatch in {directory}: "
                f"{entry['sha256']} != {actual_hmi}")
        hmi_models.append({**entry, "data": json.loads(data)})
    return meta, {"file": review_file, "id": review_id,
                  "sha256": actual, "data": review,
                  "hmiModels": hmi_models}


def compare(args) -> int:
    before_meta, before = load_snapshot(args.before)
    after_meta, after = load_snapshot(args.after)
    out = Path(args.out).expanduser().resolve()
    out.mkdir(parents=True, exist_ok=True)

    result = compare_reviews(
        before["data"], after["data"], before["id"], after["id"],
        allow_different_project_copy=args.allow_different_project_copy,
        impact_depth=args.impact_depth,
        max_impact_paths=args.max_impact_paths,
        max_impact_edge_visits=args.max_impact_edge_visits,
        before_hmi=before.get("hmiModels"),
        after_hmi=after.get("hmiModels"),
    )
    result["returnedChanges"] = len(result["changes"])

    (out / "compare.json").write_text(
        json.dumps(result, indent=2, ensure_ascii=False) + "\n",
        encoding="utf-8")
    report = render_markdown(result, before_meta, after_meta)
    (out / "report.md").write_text(report, encoding="utf-8")

    project_total = result.get("totalChanges", len(result["changes"]))
    hmi_total = result.get("totalHmiChanges", 0)
    print(f"changes: {project_total} project, {hmi_total} HMI model")
    print(f"report:  {out / 'report.md'}")
    print(f"json:    {out / 'compare.json'}")
    if (project_total or hmi_total) and not args.no_fail:
        return 1
    return 0


def _cell(value, limit=90) -> str:
    text = "" if value is None else str(value)
    if len(text) > limit:
        text = text[:limit - 3] + "..."
    return text.replace("|", "\\|")


def render_markdown(result: dict, before_meta: dict, after_meta: dict) -> str:
    before_project = before_meta.get("project") or {}
    after_project = after_meta.get("project") or {}
    changes = result.get("changes") or []
    counts = {"addedToCapturedScope": 0, "removedFromCapturedScope": 0,
              "modified": 0}
    for row in changes:
        change = row.get("change")
        if change in counts:
            counts[change] += 1
    total = result.get("totalChanges", len(changes))
    hmi_changes = result.get("hmiChanges") or []
    hmi_compared = result.get("hmiModelCompared", False)
    lines = [
        "# TIA project snapshot diff",
        "",
        "| | Before | After |",
        "|---|---|---|",
        f"| Captured | {_cell(before_meta.get('capturedUtc'))} "
        f"| {_cell(after_meta.get('capturedUtc'))} |",
        f"| Project | {_cell(before_project.get('name'))} "
        f"| {_cell(after_project.get('name'))} |",
        f"| Archive SHA-256 | `{_cell(before_project.get('archiveSha256'), 70)}` "
        f"| `{_cell(after_project.get('archiveSha256'), 70)}` |",
        f"| Project folder SHA-256 | `{_cell(before_project.get('projectFolderSha256'), 70)}` "
        f"| `{_cell(after_project.get('projectFolderSha256'), 70)}` |",
        f"| Review ID | `{_cell(before_meta.get('tiaMcp', {}).get('reviewId'), 70)}` "
        f"| `{_cell(after_meta.get('tiaMcp', {}).get('reviewId'), 70)}` |",
        "",
        f"**{total} project change(s), {len(hmi_changes)} HMI model change(s).** "
        f"{counts['addedToCapturedScope']} added, "
        f"{counts['removedFromCapturedScope']} removed, "
        f"{counts['modified']} modified (project scope).",
        "",
    ]
    if len(changes) < total:
        lines.append(f"> Only {len(changes)} of {total} changes are shown.")
        lines.append("")
    if changes:
        lines += [
            "| Change | Kind | Path | Changed fields |",
            "|---|---|---|---|",
        ]
        for row in changes:
            fields = ", ".join(row.get("changedFields") or []) or ""
            lines.append(
                f"| {_cell(row.get('change'))} | {_cell(row.get('kind'))} "
                f"| {_cell(row.get('path'), 160)} | {_cell(fields)} |")
        lines.append("")
    else:
        lines += ["No changes were observed in the captured project scope.", ""]

    lines += ["## HMI model changes", ""]
    hmi_unmatched = result.get("hmiModelUnmatchedPaths") or []
    if not hmi_compared:
        lines += ["HMI model not captured in both snapshots (comparison skipped).", ""]
    elif hmi_changes:
        lines += [
            "| Change | Kind | Composition | Path | Changed fields |",
            "|---|---|---|---|---|",
        ]
        for row in hmi_changes:
            fields = ", ".join(row.get("changedFields") or []) or ""
            lines.append(
                f"| {_cell(row.get('change'))} | {_cell(row.get('kind'))} "
                f"| {_cell(row.get('composition'))} "
                f"| {_cell(row.get('path'), 160)} | {_cell(fields)} |")
        lines.append("")
    else:
        lines += ["No HMI model changes were observed.", ""]
    if hmi_unmatched:
        shown = ", ".join(hmi_unmatched[:5])
        more = f" (+{len(hmi_unmatched) - 5} more)" if len(hmi_unmatched) > 5 else ""
        lines += [
            f"> HMI models present in only one snapshot (not compared): {shown}{more}",
            "",
        ]

    impacts = result.get("observedImpactPaths") or []
    if impacts:
        lines += [
            "## Observed impact paths",
            "",
            "| Changed object | Affected object | Distance | Change |",
            "|---|---|---|---|",
        ]
        for path in impacts[:50]:
            lines.append(
                f"| {_cell(path.get('changedObjectPath'))} "
                f"| {_cell(path.get('affectedObjectPath'))} "
                f"| {path.get('distance')} | {_cell(path.get('change'))} |")
        lines.append("")
        if len(impacts) > 50:
            lines += [f"> Impact paths truncated to 50 of {len(impacts)}.", ""]

    lines += [
        "## Coverage and limitations",
        "",
        f"- Content-compared blocks: {result.get('contentComparedBlocks', 0)}; "
        f"unknown content: {result.get('contentUnknownBlocks', 0)}.",
        f"- Content-compared types: {result.get('contentComparedTypes', 0)}; "
        f"unknown content: {result.get('contentUnknownTypes', 0)}.",
    ]
    hmi_coverage = result.get("hmiCoverage") or {}

    def hmi_side(rows):
        if not rows:
            return "not captured"
        return ", ".join(
            f"{row.get('status') or row.get('error') or 'unknown'} "
            f"({row.get('itemCount', 0)} items)"
            for row in rows)

    lines.append(
        f"- HMI model before: {hmi_side(hmi_coverage.get('before'))}; "
        f"after: {hmi_side(hmi_coverage.get('after'))}.")
    lines += [
        "- `wholeProjectEquivalent` is false; the comparison covers the captured "
        "scope only.",
        "- Absence from a partial capture is not proof of deletion; protected, "
        "unavailable and omitted content is explicit in the JSON.",
        f"- Comparison policy: {result.get('comparisonPolicy', '')}",
        "",
    ]
    return "\n".join(lines)


def add_capture_common(p: argparse.ArgumentParser) -> None:
    p.add_argument("--bundle", default=DEFAULT_BUNDLE,
                   help="TIA MCP bundle directory")
    p.add_argument("--artifacts", default=DEFAULT_ARTIFACTS,
                   help="TIA MCP artifacts directory")
    p.add_argument("--timeout", type=float, default=600)
    p.add_argument("-v", "--verbose", action="store_true")


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description="TIA project snapshot/diff")
    sub = ap.add_subparsers(dest="cmd", required=True)

    p_capture = sub.add_parser("capture", help="capture a TIA MCP project review")
    add_capture_common(p_capture)
    p_capture.add_argument("--project", required=True,
                           help=".ap21 file or project folder")
    p_capture.add_argument("--out", required=True)
    p_capture.add_argument("--plc", help="exact PLC path/name")
    p_capture.add_argument("--project-name", help="override the open project name")
    p_capture.add_argument("--max-visited", type=int, default=5000)
    p_capture.add_argument("--max-blocks", type=int, default=25)
    p_capture.add_argument("--max-relationship-roots", type=int, default=5)
    p_capture.add_argument("--hmi-model", action=argparse.BooleanOptionalAction,
                           default=True,
                           help="capture hmi_inspect section=model (default: on)")
    p_capture.add_argument("--hmi-max-pages", type=int, default=100)
    p_capture.add_argument("--log", help="Host stderr log path")
    p_capture.add_argument("--force", action="store_true")

    p_compare = sub.add_parser("compare", help="compare two snapshot directories")
    p_compare.add_argument("-v", "--verbose", action="store_true")
    p_compare.add_argument("--before", required=True)
    p_compare.add_argument("--after", required=True)
    p_compare.add_argument("--out", default="")
    p_compare.add_argument("--allow-different-project-copy", action="store_true")
    p_compare.add_argument("--max-items", type=int, default=200,
                           help="accepted for compatibility; native compare "
                                "returns all changes")
    p_compare.add_argument("--impact-depth", type=int, default=3)
    p_compare.add_argument("--max-impact-paths", type=int, default=100)
    p_compare.add_argument("--max-impact-edge-visits", type=int, default=5000)
    p_compare.add_argument("--no-fail", action="store_true",
                           help="exit 0 even when changes are found")

    args = ap.parse_args(argv)
    if args.cmd == "compare" and not args.out:
        args.out = f"diff-{time.strftime('%Y%m%d-%H%M%S')}"
    try:
        return capture(args) if args.cmd == "capture" else compare(args)
    except McpError as exc:
        print(f"TIA MCP error: {exc}", file=sys.stderr)
        return 2
    except ValueError as exc:
        print(f"compare error: {exc}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    sys.exit(main())
