#!/usr/bin/env python3
"""Poll loggedAlarms and persist new alarm transitions.

The lab runtime's `activeAlarms` query/subscription returns INTERNAL_SERVER_ERROR, but
`loggedAlarms` works.  This watcher is the fallback live alarm feed: it polls the alarm history
and stores each new transition in the same SQLite/CSV schema as `unified_alarms.py`.

  python alarm_history_watch.py --duration 3600 --interval 1 \
      --sqlite data/alarm_history.sqlite --csv data/alarm_history.csv
"""
from __future__ import annotations

import argparse
import getpass
import json
import logging
import os
import signal
import sys
import threading
import time
from datetime import datetime, timedelta, timezone
from pathlib import Path

from unified_alarms import AlarmStore
from unified_probe import URL, SYSTEM, Client

log = logging.getLogger("wincc.alarm-history")

QUERY = """query($s:[String],$a:Timestamp,$b:Timestamp,$m:Int){
  loggedAlarms(systemNames:$s, startTime:$a, endTime:$b,
               maxNumberOfResults:$m){
    name
    instanceID
    alarmGroupID
    state
    raiseTime
    acknowledgmentTime
    clearTime
    resetTime
    modificationTime
    alarmClassName
    priority
    eventText
    alarmText1
    sourceType
    suppressionState
  }
}"""


def utc_now() -> str:
    return datetime.now(timezone.utc).isoformat(timespec="milliseconds").replace("+00:00", "Z")


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description="Poll loggedAlarms into SQLite/CSV")
    ap.add_argument("--url", default=URL)
    ap.add_argument("--system", default=SYSTEM)
    ap.add_argument("--user")
    ap.add_argument("--password")
    ap.add_argument("--sqlite", default="data/alarm_history.sqlite")
    ap.add_argument("--csv", default="data/alarm_history.csv")
    ap.add_argument("--interval", type=float, default=1.0)
    ap.add_argument("--duration", type=float, default=0.0)
    ap.add_argument("--lookback", type=float, default=60.0,
                    help="initial history window in seconds")
    ap.add_argument("--start", help="explicit ISO start time")
    ap.add_argument("--max", type=int, default=500)
    ap.add_argument("-v", "--verbose", action="store_true")
    a = ap.parse_args(argv)

    logging.basicConfig(
        level=logging.DEBUG if a.verbose else logging.INFO,
        format="%(asctime)s %(levelname)s %(message)s", datefmt="%H:%M:%S")

    user = a.user or os.environ.get("WINCC_USER")
    password = a.password or os.environ.get("WINCC_PASSWORD")
    if not user:
        log.error("no user (use --user or WINCC_USER)")
        return 2
    if password is None:
        password = getpass.getpass(f"WinCC password for {user}: ")

    client = Client(a.url)
    try:
        client.login(user, password)
    except Exception as exc:
        log.error("login failed: %s", exc)
        return 1

    store = AlarmStore(a.sqlite, a.csv, 1.0)
    session_id = store.begin_session(utc_now())
    stop = threading.Event()

    def on_signal(_sig, _frame):
        stop.set()

    signal.signal(signal.SIGINT, on_signal)
    if hasattr(signal, "SIGTERM"):
        try:
            signal.signal(signal.SIGTERM, on_signal)
        except OSError:
            pass

    if a.start:
        start = a.start
    else:
        start = (datetime.now(timezone.utc) - timedelta(seconds=a.lookback)) \
            .isoformat(timespec="milliseconds").replace("+00:00", "Z")

    seen: set[str] = set()
    stored = 0
    started = time.monotonic()
    failures = 0
    try:
        while not stop.is_set():
            elapsed = time.monotonic() - started
            if a.duration and elapsed >= a.duration:
                break
            end = utc_now()
            try:
                payload = client.post_raw(QUERY, {
                    "s": [a.system], "a": start, "b": end, "m": a.max})
                failures = 0
            except Exception as exc:
                failures += 1
                log.warning("history poll failed: %s", exc)
                store.event("history_error", str(exc), session_id)
                if stop.wait(min(30.0, a.interval * (2 ** min(failures, 5)))):
                    break
                try:
                    client.login(user, password)
                except Exception as relogin:
                    log.warning("re-login failed: %s", relogin)
                continue

            if payload.get("errors"):
                log.error("history error: %s",
                          json.dumps(payload["errors"])[:400])
                store.event("history_error", payload["errors"], session_id)
            else:
                rows = (payload.get("data") or {}).get("loggedAlarms") or []
                new = 0
                for row in rows:
                    signature = json.dumps(row, sort_keys=True,
                                           ensure_ascii=False)
                    if signature in seen:
                        continue
                    seen.add(signature)
                    note = dict(row)
                    note["notificationReason"] = "History"
                    note["error"] = {"code": "0", "description": "Succeeded"}
                    store.alarm(session_id, note)
                    stored += 1
                    new += 1
                store.commit()
                if new:
                    log.info("stored %d new transition(s), %d total",
                             new, stored)

            # Overlap the next window slightly so a transition at the boundary
            # is not lost; the signature set removes duplicates.
            start = (datetime.now(timezone.utc) - timedelta(seconds=2)) \
                .isoformat(timespec="milliseconds").replace("+00:00", "Z")
            if stop.wait(a.interval):
                break
    finally:
        store.end_session(session_id, "stopped", stored)
        store.close()
        summary = {"elapsed_s": round(time.monotonic() - started, 1),
                   "transitions": stored, "seen": len(seen)}
        log.info("summary: %s", json.dumps(summary))
        print("SUMMARY " + json.dumps(summary))
    return 0


if __name__ == "__main__":
    sys.exit(main())
