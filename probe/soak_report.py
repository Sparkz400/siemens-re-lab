#!/usr/bin/env python3
"""Summarize a live_logger SQLite capture.

  python soak_report.py --sqlite data/live.sqlite
  python soak_report.py --sqlite ... --csv ... --json
"""
from __future__ import annotations

import argparse
import json
import sqlite3
import sys


def seconds(expr: str) -> str:
    return f"({expr}) * 86400.0"


def build_report(db: sqlite3.Connection, csv_path: str | None) -> dict:
    report: dict = {}
    report["integrity"] = db.execute("PRAGMA integrity_check").fetchone()[0]
    report["meta"] = dict(db.execute("SELECT key, value FROM meta"))

    total, tags, first, last, dur = db.execute(
        "SELECT COUNT(*), COUNT(DISTINCT tag), MIN(received_utc),"
        " MAX(received_utc),"
        f" {seconds('julianday(MAX(received_utc))-julianday(MIN(received_utc))')}"
        " FROM samples").fetchone()
    report["samples"] = total
    report["tags"] = tags
    report["first_sample"] = first
    report["last_sample"] = last
    report["span_s"] = round(dur or 0.0, 2)
    report["rate_per_s"] = round(total / dur, 2) if dur else 0.0

    report["reasons"] = dict(db.execute(
        "SELECT COALESCE(notification_reason,'(null)'), COUNT(*)"
        " FROM samples GROUP BY 1"))
    report["bad_errors"] = db.execute(
        "SELECT COUNT(*) FROM samples WHERE error_code IS NOT NULL"
        " AND error_code NOT IN ('0','')").fetchone()[0]
    report["missing_timestamps"] = db.execute(
        "SELECT COUNT(*) FROM samples WHERE value_timestamp IS NULL").fetchone()[0]

    report["sessions"] = [
        dict(zip(("id", "connected_utc", "disconnected_utc", "reason",
                  "notifications", "added", "modified"), row))
        for row in db.execute(
            "SELECT id, connected_utc, disconnected_utc, disconnect_reason,"
            " notifications, added, modified FROM sessions ORDER BY id")
    ]
    report["events"] = [
        dict(zip(("utc", "session_id", "kind", "detail"), row))
        for row in db.execute(
            "SELECT utc, session_id, kind, detail FROM events ORDER BY id")
    ]

    per_tag = []
    for row in db.execute(f"""
        SELECT tag, COUNT(*),
               SUM(CASE WHEN notification_reason='Modified' THEN 1 ELSE 0 END),
               MIN(value_num), MAX(value_num), ROUND(AVG(value_num), 3),
               ROUND(MAX(dt), 3)
        FROM (
          SELECT tag, notification_reason, value_num,
                 {seconds('julianday(received_utc)-julianday('
                          'LAG(received_utc) OVER (PARTITION BY tag ORDER BY id))')} dt
          FROM samples WHERE notification_reason='Modified'
        ) GROUP BY tag ORDER BY tag"""):
        per_tag.append(dict(zip(
            ("tag", "rows", "modified", "min", "max", "avg",
             "max_interval_s"), row)))
    report["per_tag"] = per_tag

    overall = db.execute(f"""
        SELECT ROUND(MAX(dt), 3), ROUND(AVG(dt), 3) FROM (
          SELECT {seconds('julianday(received_utc)-julianday('
                          'LAG(received_utc) OVER (PARTITION BY tag ORDER BY id))')} dt
          FROM samples WHERE notification_reason='Modified'
        )""").fetchone()
    report["modified_interval_max_s"] = overall[0]
    report["modified_interval_avg_s"] = overall[1]

    if csv_path:
        try:
            with open(csv_path, encoding="utf-8") as f:
                report["csv_rows"] = sum(1 for _ in f) - 1
        except OSError as exc:
            report["csv_error"] = str(exc)

    return report


def print_markdown(r: dict) -> None:
    print(f"integrity: {r['integrity']}")
    print(f"samples: {r['samples']} across {r['tags']} tags, "
          f"{r['span_s']} s, {r['rate_per_s']} msg/s")
    print(f"reasons: {r['reasons']}")
    print(f"bad error codes: {r['bad_errors']}, "
          f"missing timestamps: {r['missing_timestamps']}")
    print(f"modified-only interval: avg {r['modified_interval_avg_s']} s, "
          f"max {r['modified_interval_max_s']} s")
    if "csv_rows" in r:
        print(f"csv rows: {r['csv_rows']}")
    print("\nsessions:")
    for s in r["sessions"]:
        print(f"  {s['id']}: {s['connected_utc']} -> "
              f"{s['disconnected_utc']} ({s['reason']}), "
              f"{s['notifications']} notifications "
              f"[{s['added']} added, {s['modified']} modified]")
    gaps = [e for e in r["events"] if e["kind"] == "gap"]
    if gaps:
        print("gaps:")
        for g in gaps:
            print(f"  {g['utc']}: {g['detail']}")
    print("\nper tag:")
    print(f"  {'tag':24} {'rows':>6} {'modified':>8} {'min':>10} "
          f"{'max':>10} {'avg':>10} {'max_dt':>7}")
    for t in r["per_tag"]:
        print(f"  {t['tag']:24} {t['rows']:6} {t['modified']:8} "
              f"{str(t['min']):>10} {str(t['max']):>10} {str(t['avg']):>10} "
              f"{str(t['max_interval_s']):>7}")


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description="Report on a live_logger capture.")
    ap.add_argument("--sqlite", required=True)
    ap.add_argument("--csv")
    ap.add_argument("--json", action="store_true")
    args = ap.parse_args(argv)

    db = sqlite3.connect(args.sqlite)
    try:
        report = build_report(db, args.csv)
    finally:
        db.close()

    if args.json:
        print(json.dumps(report, indent=2, ensure_ascii=False))
    else:
        print_markdown(report)
    return 0


if __name__ == "__main__":
    sys.exit(main())
