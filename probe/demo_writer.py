#!/usr/bin/env python3
"""Write a deterministic sine pattern to WinCC Unified tags.

Used to generate 1 Hz value changes for `live_logger.py` soak tests.

  python demo_writer.py --user Lab --tags Tag01,Tag02 --duration 3600
"""
from __future__ import annotations

import argparse
import getpass
import json
import logging
import math
import os
import signal
import sys
import threading
import time
from datetime import datetime, timezone

import requests

WRITE = """mutation($i:[TagValueInput]!){
  writeTagValues(input:$i){ name error { code description } }
}"""

LOGIN = """mutation($u:String!,$p:String!){
  login(username:$u,password:$p){ token error { code description } }
}"""

log = logging.getLogger("wincc.demo")


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description="Write a sine pattern to Unified tags.")
    ap.add_argument("--url", default="http://localhost:4000/graphql")
    ap.add_argument("--system", default="HMI_RT_1")
    ap.add_argument("--tags", default=",".join(f"Tag{i:02d}" for i in range(1, 11)))
    ap.add_argument("--interval", type=float, default=1.0)
    ap.add_argument("--duration", type=float, default=0.0,
                    help="seconds to run (0 = forever)")
    ap.add_argument("--period", type=float, default=60.0,
                    help="sine period in seconds")
    ap.add_argument("--user", help="UMC user (env WINCC_USER)")
    ap.add_argument("--password", help="UMC password (env WINCC_PASSWORD)")
    ap.add_argument("-v", "--verbose", action="store_true")
    args = ap.parse_args(argv)

    logging.basicConfig(
        level=logging.DEBUG if args.verbose else logging.INFO,
        format="%(asctime)s %(levelname)s %(message)s", datefmt="%H:%M:%S")

    user = args.user or os.environ.get("WINCC_USER")
    password = args.password or os.environ.get("WINCC_PASSWORD")
    if not user:
        log.error("no user (use --user or WINCC_USER)")
        return 2
    if password is None:
        password = getpass.getpass(f"WinCC password for {user}: ")

    tags = [t if "::" in t else f"{args.system}::{t}"
            for t in (x.strip() for x in args.tags.split(",")) if t]
    if not tags:
        log.error("no tags given")
        return 2

    stop = threading.Event()

    def on_signal(signum, _frame):
        log.info("signal %s received, stopping", signum)
        stop.set()

    signal.signal(signal.SIGINT, on_signal)
    if hasattr(signal, "SIGTERM"):
        try:
            signal.signal(signal.SIGTERM, on_signal)
        except OSError:
            pass

    session = requests.Session()
    token = None
    start = time.monotonic()
    tick = 0
    failures = 0

    def login():
        r = session.post(args.url, json={
            "query": LOGIN,
            "variables": {"u": user, "p": password},
        }, timeout=20)
        r.raise_for_status()
        data = r.json()["data"]["login"]
        if not data.get("token"):
            raise RuntimeError("login rejected: " +
                               json.dumps(data.get("error")))
        return data["token"]

    while not stop.is_set():
        if args.duration and time.monotonic() - start >= args.duration:
            break

        if token is None:
            try:
                token = login()
                log.info("logged in, writing %d tags every %.1fs",
                         len(tags), args.interval)
            except Exception as exc:
                failures += 1
                log.error("login failed: %s", exc)
                if stop.wait(5):
                    break
                continue

        t = time.monotonic() - start
        values = [{
            "name": tag,
            "value": round(50 + 45 * math.sin(
                2 * math.pi * t / args.period + i * 2 * math.pi / len(tags)), 2),
        } for i, tag in enumerate(tags)]

        try:
            r = session.post(args.url, headers={
                "Authorization": "Bearer " + token}, json={
                "query": WRITE, "variables": {"i": values}}, timeout=20)
            r.raise_for_status()
            payload = r.json()
            if payload.get("errors"):
                raise RuntimeError(json.dumps(payload["errors"])[:300])
            failures = 0
            tick += 1
        except Exception as exc:
            failures += 1
            log.warning("write failed (%d in a row): %s", failures, exc)
            if failures >= 3:
                token = None
            if stop.wait(min(5.0, args.interval)):
                break
            continue

        if tick % 60 == 0:
            log.info("tick %d, %d tags, %s", tick, len(tags),
                     datetime.now(timezone.utc).isoformat(timespec="seconds"))

        elapsed = time.monotonic() - start
        sleep = args.interval - (elapsed % args.interval) if args.interval else 0
        if stop.wait(sleep):
            break

    log.info("stopped after %d ticks (%.1fs)", tick, time.monotonic() - start)
    return 0


if __name__ == "__main__":
    sys.exit(main())
