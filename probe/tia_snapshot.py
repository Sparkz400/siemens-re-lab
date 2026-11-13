#!/usr/bin/env python3
"""TIA project snapshot and diff CLI.

Capture a TIA MCP ``project-review.v1`` snapshot (native hierarchy + block XML),
store it portably, and compare two snapshots offline.

  python tia_snapshot.py capture --project <project.ap21> --out snapshots/base
  python tia_snapshot.py compare --before snapshots/base --after snapshots/change \
      --out diff

Capture needs TIA Portal/Openness and starts a private TIA MCP Host with the native
worker.  Compare uses the Host's ``--no-native-workers`` mode and does not start TIA.

The snapshot directory contains ``snapshot.json`` (metadata) and ``review.json``
(the TIA MCP project-review.v1 content).  Keep snapshots out of git.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import shutil
import sys
import tempfile
import time
from datetime import datetime, timezone
from pathlib import Path

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


def capture(args) -> int:
    archive, archive_hash, archive_bytes = resolve_archive(args.project)
    out = Path(args.out).expanduser().resolve()
    if (out / "snapshot.json").exists() and not args.force:
        raise SystemExit(f"snapshot already exists: {out} (use --force to overwrite)")
    out.mkdir(parents=True, exist_ok=True)

    log_path = str(Path(args.log).expanduser().resolve()) if args.log else str(out / "host.log")
    with McpHost(args.bundle, args.artifacts, native=True,
                 timeout_seconds=args.timeout, log_path=log_path) as host:
        identity = host.call_tool("host_build_identity", {}, timeout=60)
        opened = host.call_tool(
            "project_open",
            {"path": str(archive), "readOnly": True},
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
    actual_id = hashlib.sha256(review_bytes).hexdigest()
    if actual_id != review_id:
        raise SystemExit(
            f"review hash mismatch: host says {review_id}, file is {actual_id}")

    (out / "review.json").write_bytes(review_bytes)
    snapshot = {
        "schemaVersion": SNAPSHOT_SCHEMA,
        "capturedUtc": utc_now(),
        "project": {
            "path": str(archive),
            "name": project_name,
            "archiveSha256": archive_hash,
            "archiveBytes": archive_bytes,
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
    return meta, {"file": review_file, "id": review_id, "sha256": actual}


def compare(args) -> int:
    before_meta, before = load_snapshot(args.before)
    after_meta, after = load_snapshot(args.after)
    out = Path(args.out).expanduser().resolve()
    out.mkdir(parents=True, exist_ok=True)

    with tempfile.TemporaryDirectory(prefix="tia-compare-") as temp:
        reviews = Path(temp) / "project-reviews"
        reviews.mkdir(parents=True)
        shutil.copyfile(before["file"], reviews / f"{before['id']}.json")
        shutil.copyfile(after["file"], reviews / f"{after['id']}.json")
        with McpHost(args.bundle, temp, native=False,
                     timeout_seconds=args.timeout,
                     log_path=str(out / "host.log")) as host:
            changes = []
            first = None
            offset = 0
            while True:
                result = host.call_tool(
                    "project_snapshot_compare",
                    {
                        "beforeId": before["id"],
                        "afterId": after["id"],
                        "allowDifferentProjectCopy": args.allow_different_project_copy,
                        "offset": offset,
                        "maxItems": min(200, args.max_items),
                        "impactDepth": args.impact_depth,
                        "maxImpactPaths": args.max_impact_paths,
                        "maxImpactEdgeVisits": args.max_impact_edge_visits,
                    },
                    timeout=args.timeout)
                if first is None:
                    first = dict(result)
                changes.extend(result.get("changes") or [])
                next_offset = result.get("nextOffset")
                if next_offset is None:
                    break
                offset = next_offset
            assert first is not None
            first["changes"] = changes
            first["returnedChanges"] = len(changes)

    (out / "compare.json").write_text(
        json.dumps(first, indent=2, ensure_ascii=False) + "\n",
        encoding="utf-8")
    report = render_markdown(first, before_meta, after_meta)
    (out / "report.md").write_text(report, encoding="utf-8")

    total = first.get("totalChanges", len(changes))
    print(f"changes: {total} ({len(changes)} returned)")
    print(f"report:  {out / 'report.md'}")
    print(f"json:    {out / 'compare.json'}")
    if total and not args.no_fail:
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
        f"| Review ID | `{_cell(before_meta.get('tiaMcp', {}).get('reviewId'), 70)}` "
        f"| `{_cell(after_meta.get('tiaMcp', {}).get('reviewId'), 70)}` |",
        "",
        f"**{total} change(s):** {counts['addedToCapturedScope']} added, "
        f"{counts['removedFromCapturedScope']} removed, "
        f"{counts['modified']} modified.",
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
                f"| {_cell(row.get('path'))} | {_cell(fields)} |")
        lines.append("")
    else:
        lines += ["No changes were observed in the captured scope.", ""]

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
        "- `wholeProjectEquivalent` is false; the comparison covers the captured "
        "scope only.",
        "- Absence from a partial capture is not proof of deletion; protected, "
        "unavailable and omitted content is explicit in the JSON.",
        f"- Comparison policy: {result.get('comparisonPolicy', '')}",
        "",
    ]
    return "\n".join(lines)


def add_common(p: argparse.ArgumentParser) -> None:
    p.add_argument("--bundle", default=DEFAULT_BUNDLE,
                   help="TIA MCP bundle directory")
    p.add_argument("--artifacts", default=DEFAULT_ARTIFACTS,
                   help="TIA MCP artifacts directory (capture)")
    p.add_argument("--timeout", type=float, default=600)
    p.add_argument("-v", "--verbose", action="store_true")


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description="TIA project snapshot/diff")
    sub = ap.add_subparsers(dest="cmd", required=True)

    p_capture = sub.add_parser("capture", help="capture a TIA MCP project review")
    add_common(p_capture)
    p_capture.add_argument("--project", required=True,
                           help=".ap21 file or project folder")
    p_capture.add_argument("--out", required=True)
    p_capture.add_argument("--plc", help="exact PLC path/name")
    p_capture.add_argument("--project-name", help="override the open project name")
    p_capture.add_argument("--max-visited", type=int, default=5000)
    p_capture.add_argument("--max-blocks", type=int, default=25)
    p_capture.add_argument("--max-relationship-roots", type=int, default=5)
    p_capture.add_argument("--log", help="Host stderr log path")
    p_capture.add_argument("--force", action="store_true")

    p_compare = sub.add_parser("compare", help="compare two snapshot directories")
    add_common(p_compare)
    p_compare.add_argument("--before", required=True)
    p_compare.add_argument("--after", required=True)
    p_compare.add_argument("--out", default="")
    p_compare.add_argument("--allow-different-project-copy", action="store_true")
    p_compare.add_argument("--max-items", type=int, default=200)
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


if __name__ == "__main__":
    sys.exit(main())
