#!/usr/bin/env python3
"""WinCC Unified alarms: query, subscribe/log, and acknowledge/reset/shelve.

  python unified_alarms.py list --user Lab
  python unified_alarms.py watch --duration 3600 --sqlite data/alarms.sqlite
  python unified_alarms.py ack --name HMI_RT_1::Alarm_1 --user Lab
  python unified_alarms.py disable --name HMI_RT_1::Alarm_1 --user Lab

The lab runtime currently returns INTERNAL_SERVER_ERROR for activeAlarms and
loggedAlarms because the project has no alarm/logging configuration.  The
subscription logger records that error (and reconnects) instead of hiding it.
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
from datetime import datetime, timezone
from pathlib import Path

import websocket

from unified_probe import URL, SYSTEM, Client

PROTOCOL = "graphql-transport-ws"
VERSION = "0.1.0"

ALARM_FIELDS = """
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
  userResponse
  hostName
  userName
  value
  durationIso
"""

ACTIVE_QUERY = """query($s:[String],$f:String,$fl:String,$l:[String]){
  activeAlarms(systemNames:$s, filterString:$f, filterLanguage:$fl, languages:$l){
    %s
  }
}""" % ALARM_FIELDS

ALARM_SUB = """subscription($s:[String],$f:String,$fl:String,$l:[String]){
  activeAlarms(systemNames:$s, filterString:$f, filterLanguage:$fl, languages:$l){
    %s
    notificationReason
  }
}""" % ALARM_FIELDS

ACK = """mutation($i:[AlarmIdentifierInput]!){
  acknowledgeAlarms(input:$i){ alarmName alarmInstanceID error{code description} }
}"""
RESET = """mutation($i:[AlarmIdentifierInput]!){
  resetAlarms(input:$i){ alarmName alarmInstanceID error{code description} }
}"""
BY_NAME = """mutation($n:[String]!){
  %s(names:$n){ alarmName error{code description} }
}"""
SHELVE = """mutation($n:[String]!,$t:Timespan){
  shelveAlarms(names:$n, shelveTimeout:$t){ alarmName error{code description} }
}"""

log = logging.getLogger("wincc.alarms")


def utc_now() -> str:
    return datetime.now(timezone.utc).isoformat(timespec="milliseconds").replace("+00:00", "Z")


def ws_url(http_url: str) -> str:
    from urllib.parse import urlparse, urlunparse
    p = urlparse(http_url)
    scheme = {"http": "ws", "https": "wss"}.get(p.scheme)
    if not scheme:
        raise ValueError(f"unsupported URL scheme: {http_url}")
    return urlunparse((scheme, p.netloc, p.path or "/graphql", "", "", ""))


class AlarmStore:
    """SQLite + CSV sink for alarm notifications."""

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
            CREATE TABLE IF NOT EXISTS meta (key TEXT PRIMARY KEY, value TEXT);
            CREATE TABLE IF NOT EXISTS sessions (
              id INTEGER PRIMARY KEY AUTOINCREMENT,
              connected_utc TEXT NOT NULL,
              disconnected_utc TEXT,
              disconnect_reason TEXT,
              notifications INTEGER NOT NULL DEFAULT 0
            );
            CREATE TABLE IF NOT EXISTS alarms (
              id INTEGER PRIMARY KEY AUTOINCREMENT,
              received_utc TEXT NOT NULL,
              session_id INTEGER,
              name TEXT,
              instance_id INTEGER,
              alarm_group_id INTEGER,
              state TEXT,
              raise_time TEXT,
              acknowledgment_time TEXT,
              clear_time TEXT,
              reset_time TEXT,
              modification_time TEXT,
              alarm_class TEXT,
              priority INTEGER,
              event_text_json TEXT,
              alarm_text1_json TEXT,
              source_type TEXT,
              suppression_state TEXT,
              user_response TEXT,
              host_name TEXT,
              user_name TEXT,
              value_json TEXT,
              duration_iso TEXT,
              notification_reason TEXT,
              error_code TEXT,
              error_description TEXT
            );
            CREATE INDEX IF NOT EXISTS idx_alarms_name_time
              ON alarms(name, received_utc);
            CREATE INDEX IF NOT EXISTS idx_alarms_session
              ON alarms(session_id);
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
                    "received_utc", "session_id", "name", "instance_id", "state",
                    "raise_time", "acknowledgment_time", "clear_time", "reset_time",
                    "notification_reason", "event_text", "alarm_text1",
                    "suppression_state", "user_response",
                ])
                self.csv_file.flush()

    def meta(self, **values) -> None:
        self.db.executemany(
            "INSERT OR REPLACE INTO meta(key, value) VALUES(?, ?)",
            [(k, str(v)) for k, v in values.items()])
        self.db.commit()

    def begin_session(self, connected_utc: str) -> int:
        cur = self.db.execute(
            "INSERT INTO sessions(connected_utc) VALUES(?)", (connected_utc,))
        self.db.commit()
        return int(cur.lastrowid)

    def end_session(self, session_id: int, reason: str, notifications: int) -> None:
        self.db.execute(
            "UPDATE sessions SET disconnected_utc=?, disconnect_reason=?,"
            " notifications=? WHERE id=?",
            (utc_now(), reason, notifications, session_id))
        self.db.commit()

    def event(self, kind: str, detail=None, session_id=None) -> None:
        self.db.execute(
            "INSERT INTO events(utc, session_id, kind, detail) VALUES(?,?,?,?)",
            (utc_now(), session_id, kind,
             json.dumps(detail, ensure_ascii=False) if detail is not None else None))
        self.db.commit()

    @staticmethod
    def _json(value):
        return json.dumps(value, ensure_ascii=False) if value is not None else None

    def alarm(self, session_id: int, note: dict) -> None:
        received = utc_now()
        error = note.get("error") or {}
        event_text = note.get("eventText")
        alarm_text1 = note.get("alarmText1")
        self.db.execute(
            "INSERT INTO alarms(received_utc, session_id, name, instance_id,"
            " alarm_group_id, state, raise_time, acknowledgment_time, clear_time,"
            " reset_time, modification_time, alarm_class, priority,"
            " event_text_json, alarm_text1_json, source_type, suppression_state,"
            " user_response, host_name, user_name, value_json, duration_iso,"
            " notification_reason, error_code, error_description)"
            " VALUES(?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?)",
            (received, session_id, note.get("name"), note.get("instanceID"),
             note.get("alarmGroupID"), note.get("state"), note.get("raiseTime"),
             note.get("acknowledgmentTime"), note.get("clearTime"),
             note.get("resetTime"), note.get("modificationTime"),
             note.get("alarmClassName"), note.get("priority"),
             self._json(event_text), self._json(alarm_text1),
             note.get("sourceType"), note.get("suppressionState"),
             note.get("userResponse"), note.get("hostName"), note.get("userName"),
             self._json(note.get("value")), note.get("durationIso"),
             note.get("notificationReason"), error.get("code"),
             error.get("description")))

        if self.csv:
            self.csv.writerow([
                received, session_id, note.get("name"), note.get("instanceID"),
                note.get("state"), note.get("raiseTime"),
                note.get("acknowledgmentTime"), note.get("clearTime"),
                note.get("resetTime"), note.get("notificationReason"),
                json.dumps(event_text, ensure_ascii=False) if event_text else "",
                json.dumps(alarm_text1, ensure_ascii=False) if alarm_text1 else "",
                note.get("suppressionState"), note.get("userResponse"),
            ])
            self.csv_file.flush()

        self.pending += 1
        if (self.pending >= 100 or
                time.monotonic() - self.last_commit >= self.commit_interval):
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


class AlarmLogger:
    def __init__(self, cfg: dict, user: str, password: str):
        self.cfg = cfg
        self.user = user
        self.password = password
        self.http_url = cfg["url"]
        self.ws_url = ws_url(self.http_url)
        self.stop = threading.Event()
        self.duration = float(cfg["duration"])
        self.client = Client(self.http_url)

        self.store = AlarmStore(cfg["sqlite"], cfg["csv"],
                                float(cfg["commit_interval_s"]))
        self.attempt = 0
        self.sessions = 0
        self.notifications = 0
        self.errors = 0
        self.downtime = 0.0
        self.max_gap = 0.0
        self.gap_start = None
        self.started = time.monotonic()
        self.last_stats = self.started

    def login(self) -> str:
        self.client.login(self.user, self.password)
        return self.client.token

    def connect(self, token: str):
        ws = websocket.create_connection(
            self.ws_url, subprotocols=[PROTOCOL],
            timeout=float(self.cfg["recv_timeout_s"]))
        if ws.subprotocol != PROTOCOL:
            ws.close()
            raise RuntimeError(f"unexpected subprotocol {ws.subprotocol!r}")
        ws.send(json.dumps({"type": "connection_init",
                            "payload": {"Authorization": "Bearer " + token}}))
        deadline = time.monotonic() + float(self.cfg["ack_timeout_s"])
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
                raise RuntimeError("connection_error: " +
                                   json.dumps(msg.get("payload"))[:400])
        ws.close()
        raise RuntimeError("no connection_ack")

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
        self.store.event("connect", {"filter": self.cfg["filter"]}, session_id)

        reason = "unknown"
        notifications = 0
        ping_sent = None
        last_ping = now
        started = now
        try:
            ws.send(json.dumps({
                "id": "alarms",
                "type": "subscribe",
                "payload": {
                    "query": ALARM_SUB,
                    "variables": {
                        "s": [self.cfg["system"]],
                        "f": self.cfg["filter"],
                        "fl": self.cfg["filter_language"],
                        "l": self.cfg["languages"],
                    },
                },
            }))
            while not self.stop.is_set():
                if self.duration and time.monotonic() - self.started >= self.duration:
                    reason = "duration reached"
                    break

                now = time.monotonic()
                if ping_sent is None and now - last_ping >= float(
                        self.cfg["ping_interval_s"]):
                    ws.send(json.dumps({"type": "ping"}))
                    ping_sent = now
                    last_ping = now
                elif ping_sent is not None and now - ping_sent > float(
                        self.cfg["pong_timeout_s"]):
                    reason = "pong timeout"
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
                msg = json.loads(raw)
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
                        code = (err.get("extensions") or {}).get("code")
                        log.error("subscription error: %s (%s)",
                                  err.get("message"), code)
                        self.store.event("subscription_error",
                                         payload["errors"], session_id)
                        reason = "subscription error"
                        break
                    note = (payload.get("data") or {}).get("activeAlarms")
                    if note:
                        self.store.alarm(session_id, note)
                        self.notifications += 1
                        notifications += 1
                        log.debug("alarm %s %s", note.get("name"),
                                  note.get("notificationReason"))
                    self.maybe_stats()
                    continue
                if mtype == "complete":
                    reason = "subscription completed"
                    break
                if mtype in ("error", "connection_error"):
                    self.errors += 1
                    reason = f"server {mtype}"
                    self.store.event(mtype, msg.get("payload"), session_id)
                    log.error("%s: %s", mtype,
                              json.dumps(msg.get("payload"))[:400])
                    break
        except Exception as exc:
            reason = f"{type(exc).__name__}: {exc}"
            self.errors += 1
            self.store.event("session_error", reason, session_id)
            log.warning("session error: %s", reason)
        finally:
            if self.stop.is_set() and reason == "unknown":
                reason = "stop requested"
            try:
                ws.close()
            except Exception:
                pass
            if time.monotonic() - started > 30:
                self.attempt = 0
            self.store.end_session(session_id, reason, notifications)
            self.store.commit()
            self.gap_start = time.monotonic()
            if not self.stop.is_set():
                log.warning("disconnected: %s (%.1fs, %d notifications)",
                            reason, time.monotonic() - started, notifications)
        return reason

    def maybe_stats(self) -> None:
        now = time.monotonic()
        if now - self.last_stats < float(self.cfg["stats_interval_s"]):
            return
        self.last_stats = now
        elapsed = now - self.started
        log.info("stats: %d notifications, %d sessions, %.1fs downtime,"
                 " max gap %.1fs", self.notifications, self.sessions,
                 self.downtime, self.max_gap)

    def run(self) -> None:
        self.store.meta(logger_version=VERSION, started_utc=utc_now(),
                        url=self.http_url, ws_url=self.ws_url,
                        system=self.cfg["system"], filter=self.cfg["filter"])
        log.info("watching alarms from %s (system %s)",
                 self.ws_url, self.cfg["system"])
        try:
            while not self.stop.is_set():
                if self.duration and time.monotonic() - self.started >= self.duration:
                    break
                try:
                    self.session()
                except Exception as exc:
                    self.errors += 1
                    self.store.event("connect_failed",
                                     f"{type(exc).__name__}: {exc}")
                    log.error("connection failed: %s: %s",
                              type(exc).__name__, exc)
                if self.stop.is_set():
                    break
                if self.duration and time.monotonic() - self.started >= self.duration:
                    break
                if self.gap_start is None:
                    self.gap_start = time.monotonic()
                delay = min(float(self.cfg["reconnect_max_s"]),
                            float(self.cfg["reconnect_min_s"]) *
                            (2 ** min(self.attempt, 8)))
                delay *= random.uniform(0.8, 1.2)
                self.attempt += 1
                log.info("reconnect in %.1fs (attempt %d)", delay, self.attempt)
                wait = delay
                if self.duration:
                    remaining = self.duration - (time.monotonic() - self.started)
                    if remaining <= 0:
                        break
                    wait = min(wait, remaining)
                if self.stop.wait(wait):
                    break
        finally:
            if self.gap_start is not None and self.sessions:
                gap = time.monotonic() - self.gap_start
                self.downtime += gap
                self.max_gap = max(self.max_gap, gap)
            elapsed = time.monotonic() - self.started
            summary = {
                "elapsed_s": round(elapsed, 1),
                "notifications": self.notifications,
                "sessions": self.sessions,
                "errors": self.errors,
                "downtime_s": round(self.downtime, 2),
                "max_gap_s": round(self.max_gap, 2),
            }
            self.store.event("stop", summary)
            self.store.close()
            log.info("summary: %s", json.dumps(summary))
            print("SUMMARY " + json.dumps(summary))


def run_mutation(client: Client, query: str, variables: dict, key: str) -> int:
    payload = client.post_raw(query, variables)
    print(json.dumps(payload, indent=2, ensure_ascii=False))
    if payload.get("errors"):
        return 1
    rows = (payload.get("data") or {}).get(key) or []
    bad = 0
    for row in rows:
        err = row.get("error") or {}
        if str(err.get("code", "0")) not in ("0", "None", ""):
            bad += 1
    return 1 if bad else 0


def add_common(p: argparse.ArgumentParser) -> None:
    p.add_argument("--url", default=URL)
    p.add_argument("--system", default=SYSTEM)
    p.add_argument("--user", help="UMC user (env WINCC_USER)")
    p.add_argument("--password", help="UMC password (env WINCC_PASSWORD)")
    p.add_argument("--log", help="also write logs to this file")
    p.add_argument("-v", "--verbose", action="store_true")


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description="WinCC Unified alarm client")
    sub = ap.add_subparsers(dest="cmd", required=True)

    p_list = sub.add_parser("list", help="query active alarms (blocked on this runtime; see docs/11)")
    add_common(p_list)
    p_list.add_argument("--filter", default="")
    p_list.add_argument("--filter-language", default="en-US")
    p_list.add_argument("--languages", default="en-US")

    p_watch = sub.add_parser("watch", help="log the activeAlarms subscription (blocked; use alarm_history_watch.py)")
    add_common(p_watch)
    p_watch.add_argument("--sqlite", default="data/alarms.sqlite")
    p_watch.add_argument("--csv", default="data/alarms.csv")
    p_watch.add_argument("--duration", type=float, default=0)
    p_watch.add_argument("--filter", default="")
    p_watch.add_argument("--filter-language", default="en-US")
    p_watch.add_argument("--languages", default="en-US")
    p_watch.add_argument("--stats-interval", type=float, default=60)
    p_watch.add_argument("--reconnect-min", type=float, default=1)
    p_watch.add_argument("--reconnect-max", type=float, default=30)
    p_watch.add_argument("--commit-interval", type=float, default=1)

    for name in ("ack", "reset"):
        p = sub.add_parser(name, help=f"{name} active alarms")
        add_common(p)
        p.add_argument("--name", action="append", required=True)
        p.add_argument("--instance", type=int, default=0)

    for name in ("disable", "enable", "unshelve"):
        p = sub.add_parser(name, help=f"{name} configured alarms")
        add_common(p)
        p.add_argument("--name", action="append", required=True)

    p_shelve = sub.add_parser("shelve", help="shelve configured alarms")
    add_common(p_shelve)
    p_shelve.add_argument("--name", action="append", required=True)
    p_shelve.add_argument("--timeout", type=int, default=None,
                          help="numeric Timespan for the shelve timeout (e.g. 60)")

    args = ap.parse_args(argv)

    logging.basicConfig(
        level=logging.DEBUG if args.verbose else logging.INFO,
        format="%(asctime)s %(levelname)s %(message)s", datefmt="%H:%M:%S")
    if args.log:
        Path(args.log).parent.mkdir(parents=True, exist_ok=True)
        fh = logging.FileHandler(args.log, encoding="utf-8")
        fh.setFormatter(logging.Formatter("%(asctime)s %(levelname)s %(message)s"))
        log.addHandler(fh)

    user = args.user or os.environ.get("WINCC_USER")
    password = args.password or os.environ.get("WINCC_PASSWORD")
    if not user:
        log.error("no user (use --user or WINCC_USER)")
        return 2
    if password is None:
        password = getpass.getpass(f"WinCC password for {user}: ")

    client = Client(args.url)
    try:
        client.login(user, password)
    except Exception as exc:
        log.error("login failed: %s", exc)
        return 1

    if args.cmd == "list":
        payload = client.post_raw(ACTIVE_QUERY, {
            "s": [args.system], "f": args.filter,
            "fl": args.filter_language,
            "l": [x for x in args.languages.split(",") if x],
        })
        print(json.dumps(payload, indent=2, ensure_ascii=False))
        return 1 if payload.get("errors") else 0

    if args.cmd == "watch":
        cfg = {
            "url": args.url, "system": args.system,
            "filter": args.filter, "filter_language": args.filter_language,
            "languages": [x for x in args.languages.split(",") if x],
            "sqlite": args.sqlite, "csv": args.csv,
            "duration": args.duration,
            "stats_interval_s": args.stats_interval,
            "reconnect_min_s": args.reconnect_min,
            "reconnect_max_s": args.reconnect_max,
            "commit_interval_s": args.commit_interval,
            "ping_interval_s": 15.0, "pong_timeout_s": 10.0,
            "recv_timeout_s": 2.0, "ack_timeout_s": 10.0,
        }
        logger = AlarmLogger(cfg, user, password)

        def stop(_sig, _frame):
            logger.stop.set()

        signal.signal(signal.SIGINT, stop)
        if hasattr(signal, "SIGTERM"):
            try:
                signal.signal(signal.SIGTERM, stop)
            except OSError:
                pass
        logger.run()
        return 0

    names = [n if "::" in n else f"{args.system}::{n}" for n in args.name]
    if args.cmd in ("ack", "reset"):
        variables = {"i": [{"name": n, "instanceID": args.instance} for n in names]}
        return run_mutation(client, ACK if args.cmd == "ack" else RESET,
                            variables,
                            "acknowledgeAlarms" if args.cmd == "ack"
                            else "resetAlarms")

    if args.cmd == "shelve":
        variables = {"n": names}
        if args.timeout is not None:
            variables["t"] = args.timeout
        return run_mutation(client, SHELVE, variables, "shelveAlarms")

    key = {"disable": "disableAlarms", "enable": "enableAlarms",
           "unshelve": "unshelveAlarms"}[args.cmd]
    return run_mutation(client, BY_NAME % key, {"n": names}, key)


if __name__ == "__main__":
    sys.exit(main())
