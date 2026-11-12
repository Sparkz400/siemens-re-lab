#!/usr/bin/env python3
"""WinCC Unified live tag logger (graphql-ws subscriptions).

Logs every tag change notification to SQLite and CSV, reconnects with
exponential backoff, re-authenticates on each reconnect, and records
connection sessions/gaps.

  python live_logger.py --config live_logger.example.toml
  python live_logger.py --url http://localhost:4000/graphql --tags Tag01,Tag02

Credentials come from --user/--password, WINCC_USER/WINCC_PASSWORD, or an
interactive prompt. They are never written to the config or the log.

Protocol notes (WinCC Unified PC RT 21.0):
  * WebSocket path is the same /graphql endpoint, subprotocol
    "graphql-transport-ws".
  * The Bearer token must be sent in connection_init.payload.Authorization;
    an HTTP Authorization header on the upgrade is ignored by the resolver.
  * The server sends one `next` message per tag per change; notificationReason
    is "Added" for the initial snapshot and "Modified" for later changes.
"""
from __future__ import annotations

import argparse
import csv
import getpass
import json
import logging
import os
import random
import signal
import sqlite3
import sys
import threading
import time
import tomllib
from datetime import datetime, timezone
from pathlib import Path
from urllib.parse import urlparse, urlunparse

import requests
import websocket

PROTOCOL = "graphql-transport-ws"
VERSION = "0.1.0"

SUBSCRIBE = """subscription($n:[String!]!){
  tagValues(names:$n){
    name
    value {
      value
      timestamp
      quality { quality subStatus limit }
    }
    error { code description }
    notificationReason
  }
}"""

LOGIN = """mutation($u:String!,$p:String!){
  login(username:$u,password:$p){ token error { code description } }
}"""

DEFAULTS = {
    "wincc": {
        "url": "http://localhost:4000/graphql",
        "system": "HMI_RT_1",
        "tags": [],
    },
    "storage": {
        "sqlite": "data/live.sqlite",
        "csv": "data/live.csv",
    },
    "connection": {
        "reconnect_min_s": 1.0,
        "reconnect_max_s": 30.0,
        "ping_interval_s": 15.0,
        "pong_timeout_s": 10.0,
        "recv_timeout_s": 2.0,
        "ack_timeout_s": 10.0,
        "commit_interval_s": 1.0,
    },
    "run": {
        "duration_s": 0.0,
        "stats_interval_s": 60.0,
    },
}

log = logging.getLogger("wincc.live")


def utc_now() -> str:
    return datetime.now(timezone.utc).isoformat(timespec="milliseconds").replace("+00:00", "Z")


def deep_update(base: dict, extra: dict) -> None:
    for key, value in extra.items():
        if isinstance(value, dict) and isinstance(base.get(key), dict):
            deep_update(base[key], value)
        else:
            base[key] = value


def ws_url(http_url: str) -> str:
    p = urlparse(http_url)
    scheme = {"http": "ws", "https": "wss"}.get(p.scheme)
    if not scheme:
        raise ValueError(f"unsupported URL scheme: {http_url}")
    return urlunparse((scheme, p.netloc, p.path or "/graphql", "", "", ""))


def qualify(tag: str, system: str) -> str:
    return tag if "::" in tag else f"{system}::{tag}"


class Store:
    """SQLite + CSV sink shared across reconnects."""

    def __init__(self, sqlite_path: str, csv_path: str, commit_interval: float):
        self.commit_interval = commit_interval
        self.last_commit = time.monotonic()
        self.pending = 0

        if sqlite_path != ":memory:":
            Path(sqlite_path).parent.mkdir(parents=True, exist_ok=True)
        self.db = sqlite3.connect(sqlite_path, timeout=30.0)
        self.db.execute("PRAGMA journal_mode=WAL")
        self.db.execute("PRAGMA synchronous=NORMAL")
        self.db.executescript(
            """
            CREATE TABLE IF NOT EXISTS meta (
              key TEXT PRIMARY KEY,
              value TEXT
            );
            CREATE TABLE IF NOT EXISTS sessions (
              id INTEGER PRIMARY KEY AUTOINCREMENT,
              connected_utc TEXT NOT NULL,
              disconnected_utc TEXT,
              disconnect_reason TEXT,
              notifications INTEGER NOT NULL DEFAULT 0,
              added INTEGER NOT NULL DEFAULT 0,
              modified INTEGER NOT NULL DEFAULT 0
            );
            CREATE TABLE IF NOT EXISTS samples (
              id INTEGER PRIMARY KEY AUTOINCREMENT,
              received_utc TEXT NOT NULL,
              session_id INTEGER,
              tag TEXT NOT NULL,
              value_json TEXT,
              value_num REAL,
              value_timestamp TEXT,
              notification_reason TEXT,
              quality_json TEXT,
              error_code TEXT,
              error_description TEXT
            );
            CREATE INDEX IF NOT EXISTS idx_samples_tag_time
              ON samples(tag, received_utc);
            CREATE INDEX IF NOT EXISTS idx_samples_session
              ON samples(session_id);
            CREATE TABLE IF NOT EXISTS events (
              id INTEGER PRIMARY KEY AUTOINCREMENT,
              utc TEXT NOT NULL,
              session_id INTEGER,
              kind TEXT NOT NULL,
              detail TEXT
            );
            """
        )
        self.db.commit()

        self.csv_file = None
        self.csv = None
        if csv_path:
            Path(csv_path).parent.mkdir(parents=True, exist_ok=True)
            new = not os.path.exists(csv_path) or os.path.getsize(csv_path) == 0
            self.csv_file = open(csv_path, "a", newline="", encoding="utf-8")
            self.csv = csv.writer(self.csv_file)
            if new:
                self.csv.writerow([
                    "received_utc", "session_id", "tag", "value",
                    "value_timestamp", "notification_reason",
                    "error_code", "error_description",
                ])
                self.csv_file.flush()

    def meta(self, **values) -> None:
        self.db.executemany(
            "INSERT OR REPLACE INTO meta(key, value) VALUES(?, ?)",
            [(k, str(v)) for k, v in values.items()],
        )
        self.db.commit()

    def begin_session(self, connected_utc: str) -> int:
        cur = self.db.execute(
            "INSERT INTO sessions(connected_utc) VALUES(?)", (connected_utc,))
        self.db.commit()
        return int(cur.lastrowid)

    def end_session(self, session_id: int, reason: str, counters: dict) -> None:
        self.db.execute(
            "UPDATE sessions SET disconnected_utc=?, disconnect_reason=?,"
            " notifications=?, added=?, modified=? WHERE id=?",
            (utc_now(), reason, counters.get("notifications", 0),
             counters.get("added", 0), counters.get("modified", 0), session_id),
        )
        self.db.commit()

    def event(self, kind: str, detail=None, session_id=None) -> None:
        self.db.execute(
            "INSERT INTO events(utc, session_id, kind, detail) VALUES(?,?,?,?)",
            (utc_now(), session_id, kind,
             json.dumps(detail, ensure_ascii=False) if detail is not None else None),
        )
        self.db.commit()

    def sample(self, session_id: int, note: dict) -> None:
        value = note.get("value") or {}
        raw = value.get("value")
        timestamp = value.get("timestamp")
        reason = note.get("notificationReason")
        quality = value.get("quality")
        error = note.get("error") or {}
        received = utc_now()

        value_num = None
        if isinstance(raw, (int, float)) and not isinstance(raw, bool):
            value_num = float(raw)

        self.db.execute(
            "INSERT INTO samples(received_utc, session_id, tag, value_json,"
            " value_num, value_timestamp, notification_reason, quality_json,"
            " error_code, error_description) VALUES(?,?,?,?,?,?,?,?,?,?)",
            (received, session_id, note.get("name"),
             json.dumps(raw, ensure_ascii=False) if raw is not None else None,
             value_num, timestamp, reason,
             json.dumps(quality, ensure_ascii=False) if quality else None,
             error.get("code"), error.get("description")),
        )

        if self.csv:
            if raw is None:
                cell = ""
            elif isinstance(raw, (str, int, float, bool)):
                cell = raw
            else:
                cell = json.dumps(raw, ensure_ascii=False)
            self.csv.writerow([
                received, session_id, note.get("name"), cell, timestamp, reason,
                error.get("code") or "", error.get("description") or "",
            ])
            self.csv_file.flush()

        self.pending += 1
        now = time.monotonic()
        if self.pending >= 200 or now - self.last_commit >= self.commit_interval:
            self.commit()

    def commit(self) -> None:
        if self.pending:
            self.db.commit()
            self.pending = 0
        self.last_commit = time.monotonic()

    def close(self) -> None:
        self.commit()
        self.db.close()
        if self.csv_file:
            self.csv_file.flush()
            self.csv_file.close()


class AuthError(RuntimeError):
    pass


class Logger:
    def __init__(self, cfg: dict, user: str, password: str):
        self.cfg = cfg
        self.user = user
        self.password = password
        self.http_url = cfg["wincc"]["url"]
        self.ws_url = ws_url(self.http_url)
        self.system = cfg["wincc"]["system"]
        self.tags = [qualify(t, self.system) for t in cfg["wincc"]["tags"]]
        self.stop = threading.Event()
        self.duration = float(cfg["run"]["duration_s"])
        self.stats_interval = float(cfg["run"]["stats_interval_s"])
        self.http = requests.Session()

        self.store = Store(
            cfg["storage"]["sqlite"], cfg["storage"]["csv"],
            float(cfg["connection"]["commit_interval_s"]),
        )

        self.attempt = 0
        self.samples = 0
        self.sessions = 0
        self.reconnects = 0
        self.errors = 0
        self.downtime = 0.0
        self.max_gap = 0.0
        self.gap_start = None
        self.started = time.monotonic()
        self.last_stats = self.started

    # -- HTTP auth ---------------------------------------------------------
    def login(self) -> str:
        try:
            r = self.http.post(
                self.http_url,
                json={"query": LOGIN, "variables": {
                    "u": self.user, "p": self.password}},
                timeout=20,
            )
            r.raise_for_status()
            payload = r.json()
        except Exception as exc:
            raise AuthError(f"login request failed: {exc}") from exc
        if "errors" in payload:
            raise AuthError("login GraphQL errors: " +
                            json.dumps(payload["errors"])[:500])
        data = (payload.get("data") or {}).get("login") or {}
        token = data.get("token")
        if not token:
            raise AuthError("login rejected: " +
                            json.dumps(data.get("error"), ensure_ascii=False))
        return token

    # -- WebSocket ---------------------------------------------------------
    def connect(self, token: str) -> websocket.WebSocket:
        conn_cfg = self.cfg["connection"]
        ws = websocket.create_connection(
            self.ws_url,
            subprotocols=[PROTOCOL],
            timeout=float(conn_cfg["recv_timeout_s"]),
        )
        if ws.subprotocol != PROTOCOL:
            ws.close()
            raise RuntimeError(
                f"server negotiated {ws.subprotocol!r}, expected {PROTOCOL!r}")
        ws.send(json.dumps({
            "type": "connection_init",
            "payload": {"Authorization": "Bearer " + token},
        }))
        deadline = time.monotonic() + float(conn_cfg["ack_timeout_s"])
        while time.monotonic() < deadline:
            try:
                raw = ws.recv()
            except websocket.WebSocketTimeoutException:
                continue
            if not raw:
                raise RuntimeError("socket closed during handshake")
            msg = json.loads(raw)
            if msg.get("type") == "connection_ack":
                return ws
            if msg.get("type") == "connection_error":
                ws.close()
                raise AuthError("connection_error: " +
                                json.dumps(msg.get("payload"))[:500])
        ws.close()
        raise RuntimeError("no connection_ack")

    # -- one subscription session -----------------------------------------
    def session(self) -> str:
        token = self.login()
        ws = self.connect(token)
        session_id = self.store.begin_session(utc_now())
        self.sessions += 1
        now = time.monotonic()

        if self.gap_start is not None:
            gap = now - self.gap_start
            self.downtime += gap
            self.max_gap = max(self.max_gap, gap)
            self.store.event("gap", {"seconds": round(gap, 3)}, session_id)
            log.info("connected after %.1fs gap (session %d)", gap, session_id)
        else:
            log.info("connected (session %d)", session_id)
        self.gap_start = None
        self.store.event("connect", {"tags": len(self.tags)}, session_id)

        counters = {"notifications": 0, "added": 0, "modified": 0}
        reason = "unknown"
        ping_sent = None
        last_ping = now
        session_started = now

        try:
            ws.send(json.dumps({
                "id": "tags",
                "type": "subscribe",
                "payload": {"query": SUBSCRIBE, "variables": {"n": self.tags}},
            }))

            while not self.stop.is_set():
                if self.duration and time.monotonic() - self.started >= self.duration:
                    reason = "duration reached"
                    break

                now = time.monotonic()
                if ping_sent is None and now - last_ping >= float(
                        self.cfg["connection"]["ping_interval_s"]):
                    ws.send(json.dumps({"type": "ping"}))
                    ping_sent = now
                    last_ping = now
                elif ping_sent is not None and now - ping_sent > float(
                        self.cfg["connection"]["pong_timeout_s"]):
                    reason = "pong timeout"
                    log.warning("no pong for %.0fs, reconnecting",
                                now - ping_sent)
                    break

                try:
                    raw = ws.recv()
                except websocket.WebSocketTimeoutException:
                    self.maybe_stats()
                    continue
                except websocket.WebSocketConnectionClosedException:
                    reason = "socket closed"
                    break

                if not raw:
                    reason = "socket closed"
                    break

                try:
                    msg = json.loads(raw)
                except json.JSONDecodeError:
                    self.errors += 1
                    log.warning("non-JSON frame ignored: %.100r", raw)
                    continue

                mtype = msg.get("type")
                if mtype == "ping":
                    ws.send(json.dumps({"type": "pong"}))
                    continue
                if mtype == "pong":
                    ping_sent = None
                    continue
                if mtype == "next":
                    payload = msg.get("payload") or {}
                    if payload.get("errors"):
                        self.errors += 1
                        err = payload["errors"][0]
                        detail = (err.get("extensions") or {}).get("code")
                        log.error("subscription error: %s (%s)",
                                  err.get("message"), detail)
                        self.store.event("subscription_error",
                                         payload["errors"], session_id)
                        if detail == "UNAUTHENTICATED":
                            reason = "unauthenticated"
                            break
                        continue
                    note = (payload.get("data") or {}).get("tagValues")
                    if not note:
                        continue
                    self.store.sample(session_id, note)
                    self.samples += 1
                    counters["notifications"] += 1
                    if note.get("notificationReason") == "Added":
                        counters["added"] += 1
                    elif note.get("notificationReason") == "Modified":
                        counters["modified"] += 1
                    self.maybe_stats()
                    continue
                if mtype == "complete":
                    reason = "subscription completed"
                    break
                if mtype in ("error", "connection_error"):
                    reason = f"server {mtype}"
                    self.errors += 1
                    log.error("%s: %s", mtype, json.dumps(msg.get("payload"))[:500])
                    self.store.event(mtype, msg.get("payload"), session_id)
                    break
        except Exception as exc:
            reason = f"{type(exc).__name__}: {exc}"
            self.errors += 1
            log.warning("session error: %s", reason)
            self.store.event("session_error", reason, session_id)
        finally:
            if self.stop.is_set() and reason == "unknown":
                reason = "stop requested"
            try:
                ws.close()
            except Exception:
                pass
            lifetime = time.monotonic() - session_started
            if lifetime > 30:
                self.attempt = 0
            self.store.end_session(session_id, reason, counters)
            self.store.commit()
            self.gap_start = time.monotonic()
            if not self.stop.is_set():
                log.warning("disconnected: %s (%.1fs, %d notifications)",
                            reason, lifetime, counters["notifications"])
        return reason

    def maybe_stats(self) -> None:
        now = time.monotonic()
        if now - self.last_stats < self.stats_interval:
            return
        self.last_stats = now
        elapsed = now - self.started
        log.info("stats: %d samples, %.1f msg/s, %d sessions, %d reconnect"
                 " attempts, %.1fs downtime, max gap %.1fs",
                 self.samples, self.samples / elapsed if elapsed else 0.0,
                 self.sessions, self.reconnects, self.downtime, self.max_gap)

    def run(self) -> None:
        self.store.meta(
            logger_version=VERSION, started_utc=utc_now(), url=self.http_url,
            ws_url=self.ws_url, tags=json.dumps(self.tags),
            protocol=PROTOCOL,
        )
        log.info("logging %d tags from %s", len(self.tags), self.ws_url)
        try:
            while not self.stop.is_set():
                if self.duration and time.monotonic() - self.started >= self.duration:
                    break
                try:
                    self.session()
                except AuthError as exc:
                    self.errors += 1
                    log.error("authentication failed: %s", exc)
                    self.store.event("auth_failed", str(exc))
                except Exception as exc:
                    self.errors += 1
                    log.error("connection failed: %s: %s",
                              type(exc).__name__, exc)
                    self.store.event("connect_failed",
                                     f"{type(exc).__name__}: {exc}")

                if self.stop.is_set():
                    break
                if self.duration and time.monotonic() - self.started >= self.duration:
                    break
                if self.gap_start is None:
                    self.gap_start = time.monotonic()

                min_s = float(self.cfg["connection"]["reconnect_min_s"])
                max_s = float(self.cfg["connection"]["reconnect_max_s"])
                delay = min(max_s, min_s * (2 ** min(self.attempt, 8)))
                delay *= random.uniform(0.8, 1.2)
                self.attempt += 1
                self.reconnects += 1
                log.info("reconnect in %.1fs (attempt %d)", delay, self.attempt)
                if self.stop.wait(delay):
                    break
        finally:
            self.finish()

    def finish(self) -> None:
        if self.gap_start is not None and self.sessions:
            gap = time.monotonic() - self.gap_start
            self.downtime += gap
            self.max_gap = max(self.max_gap, gap)
        elapsed = time.monotonic() - self.started
        summary = {
            "elapsed_s": round(elapsed, 1),
            "samples": self.samples,
            "sessions": self.sessions,
            "reconnects": max(0, self.sessions - 1),
            "errors": self.errors,
            "downtime_s": round(self.downtime, 2),
            "max_gap_s": round(self.max_gap, 2),
        }
        self.store.event("stop", summary)
        self.store.close()
        log.info("summary: %s", json.dumps(summary))
        print("SUMMARY " + json.dumps(summary))


def load_config(path: str | None) -> dict:
    cfg = json.loads(json.dumps(DEFAULTS))
    if path:
        with open(path, "rb") as f:
            deep_update(cfg, tomllib.load(f))
    return cfg


def parse_args(argv=None):
    ap = argparse.ArgumentParser(
        description="Log WinCC Unified tag changes over graphql-ws.")
    ap.add_argument("--config", help="TOML config file")
    ap.add_argument("--url", help="GraphQL HTTP URL")
    ap.add_argument("--system", help="HMI system name used to qualify tags")
    ap.add_argument("--tags", help="comma-separated tag names")
    ap.add_argument("--tag", action="append", default=[],
                    help="tag name (repeatable)")
    ap.add_argument("--sqlite", help="SQLite output path ('' to disable)")
    ap.add_argument("--csv", help="CSV output path ('' to disable)")
    ap.add_argument("--duration", type=float, help="seconds to run (0 = forever)")
    ap.add_argument("--user", help="UMC user (env WINCC_USER)")
    ap.add_argument("--password", help="UMC password (env WINCC_PASSWORD)")
    ap.add_argument("--log", help="write logs to this file as well")
    ap.add_argument("-v", "--verbose", action="store_true")
    return ap.parse_args(argv)


def main(argv=None) -> int:
    args = parse_args(argv)
    logging.basicConfig(
        level=logging.DEBUG if args.verbose else logging.INFO,
        format="%(asctime)s %(levelname)s %(message)s",
        datefmt="%H:%M:%S",
    )
    if args.log:
        Path(args.log).parent.mkdir(parents=True, exist_ok=True)
        fh = logging.FileHandler(args.log, encoding="utf-8")
        fh.setFormatter(logging.Formatter(
            "%(asctime)s %(levelname)s %(message)s"))
        log.addHandler(fh)

    cfg = load_config(args.config)
    if args.url:
        cfg["wincc"]["url"] = args.url
    if args.system:
        cfg["wincc"]["system"] = args.system
    if args.sqlite is not None:
        cfg["storage"]["sqlite"] = args.sqlite
    if args.csv is not None:
        cfg["storage"]["csv"] = args.csv
    if args.duration is not None:
        cfg["run"]["duration_s"] = args.duration

    tags = list(cfg["wincc"]["tags"])
    if args.tags:
        tags = [t.strip() for t in args.tags.split(",") if t.strip()]
    tags.extend(args.tag)
    if not tags:
        log.error("no tags configured (config wincc.tags or --tags)")
        return 2
    # de-duplicate, preserve order
    cfg["wincc"]["tags"] = list(dict.fromkeys(tags))

    user = args.user or os.environ.get("WINCC_USER")
    password = args.password or os.environ.get("WINCC_PASSWORD")
    if not user:
        log.error("no user (use --user or WINCC_USER)")
        return 2
    if password is None:
        password = getpass.getpass(f"WinCC password for {user}: ")

    logger = Logger(cfg, user, password)

    def on_signal(signum, _frame):
        log.info("signal %s received, stopping", signum)
        logger.stop.set()

    signal.signal(signal.SIGINT, on_signal)
    if hasattr(signal, "SIGTERM"):
        try:
            signal.signal(signal.SIGTERM, on_signal)
        except OSError:
            pass
    if hasattr(signal, "SIGBREAK"):
        try:
            signal.signal(signal.SIGBREAK, on_signal)
        except OSError:
            pass

    logger.run()
    return 0


if __name__ == "__main__":
    sys.exit(main())
