#!/usr/bin/env python3
"""WinCC Unified history queries (logged tag values and logged alarms).

  python unified_history.py tags --name Tag01 --start 2026-11-13T05:00:00Z
  python unified_history.py alarms --system HMI_RT_1 --start 2026-11-13T05:00:00Z

The lab runtime currently returns INTERNAL_SERVER_ERROR for both operations
because the project has no tag/alarm logging configured.  The CLI prints the
GraphQL error verbatim and exits non-zero.
"""
from __future__ import annotations

import argparse
import csv
import getpass
import json
import logging
import os
import sys
from datetime import datetime, timedelta, timezone
from pathlib import Path

from unified_probe import URL, SYSTEM, Client

log = logging.getLogger("wincc.history")

TAGS_QUERY = """query($n:[String]!,$s:Timestamp,$e:Timestamp,$m:Int,
                       $o:LoggedTagValuesSortingModeEnum,
                       $b:LoggedTagValuesBoundingModeEnum){
  loggedTagValues(names:$n, startTime:$s, endTime:$e,
                  maxNumberOfValues:$m, sortingMode:$o,
                  boundingValuesMode:$b){
    loggingTagName
    error { code description }
    values {
      value { value timestamp quality { quality subStatus limit } }
      flags
    }
  }
}"""

ALARMS_QUERY = """query($s:[String],$f:String,$fl:String,$l:[String],
                        $a:Timestamp,$b:Timestamp,$m:Int){
  loggedAlarms(systemNames:$s, filterString:$f, filterLanguage:$fl,
               languages:$l, startTime:$a, endTime:$b,
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


def iso(value: str | None, default: datetime) -> str:
    if value:
        return value
    return default.isoformat(timespec="milliseconds").replace("+00:00", "Z")


def default_window(args) -> tuple[str, str]:
    now = datetime.now(timezone.utc)
    start = datetime.fromisoformat(args.start.replace("Z", "+00:00")) \
        if args.start else now - timedelta(hours=1)
    end = datetime.fromisoformat(args.end.replace("Z", "+00:00")) \
        if args.end else now
    return (start.isoformat(timespec="milliseconds").replace("+00:00", "Z"),
            end.isoformat(timespec="milliseconds").replace("+00:00", "Z"))


def hint(payload: dict) -> None:
    for err in payload.get("errors") or []:
        if (err.get("extensions") or {}).get("code") == "INTERNAL_SERVER_ERROR":
            print("hint: the runtime's alarm/logging subsystem returned an "
                  "internal error; this project has no tag/alarm logging "
                  "configured (see docs/04-wincc-graphql.md).", file=sys.stderr)
            return


def add_common(p: argparse.ArgumentParser) -> None:
    p.add_argument("--url", default=URL)
    p.add_argument("--user", help="UMC user (env WINCC_USER)")
    p.add_argument("--password", help="UMC password (env WINCC_PASSWORD)")
    p.add_argument("-v", "--verbose", action="store_true")


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description="WinCC Unified history client")
    sub = ap.add_subparsers(dest="cmd", required=True)

    p_tags = sub.add_parser("tags", help="loggedTagValues")
    add_common(p_tags)
    p_tags.add_argument("--name", action="append", default=[])
    p_tags.add_argument("--names", help="comma-separated names")
    p_tags.add_argument("--start")
    p_tags.add_argument("--end")
    p_tags.add_argument("--max", type=int, default=100)
    p_tags.add_argument("--sort", default="TIME_ASC",
                        choices=["TIME_ASC", "TIME_DESC"])
    p_tags.add_argument("--bounding", default="NO_BOUNDING_VALUES",
                        choices=["NO_BOUNDING_VALUES", "LEFT_BOUNDING_VALUES",
                                 "RIGHT_BOUNDING_VALUES",
                                 "LEFTRIGHT_BOUNDING_VALUES"])
    p_tags.add_argument("--out", help="write rows to this CSV")

    p_alarms = sub.add_parser("alarms", help="loggedAlarms")
    add_common(p_alarms)
    p_alarms.add_argument("--system", default=SYSTEM)
    p_alarms.add_argument("--filter", default="")
    p_alarms.add_argument("--filter-language", default="en-US")
    p_alarms.add_argument("--languages", default="en-US")
    p_alarms.add_argument("--start")
    p_alarms.add_argument("--end")
    p_alarms.add_argument("--max", type=int, default=100)
    p_alarms.add_argument("--out", help="write rows to this CSV")

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

    client = Client(args.url)
    try:
        client.login(user, password)
    except Exception as exc:
        log.error("login failed: %s", exc)
        return 1

    start, end = default_window(args)

    if args.cmd == "tags":
        names = list(args.name)
        if args.names:
            names += [x.strip() for x in args.names.split(",") if x.strip()]
        names = [n if "::" in n else f"{SYSTEM}::{n}" for n in names]
        if not names:
            log.error("no tags (use --name or --names)")
            return 2
        payload = client.post_raw(TAGS_QUERY, {
            "n": names, "s": start, "e": end, "m": args.max,
            "o": args.sort, "b": args.bounding,
        })
        print(json.dumps(payload, indent=2, ensure_ascii=False)[:20000])
        hint(payload)
        if payload.get("errors"):
            return 1
        if args.out:
            Path(args.out).parent.mkdir(parents=True, exist_ok=True)
            with open(args.out, "w", newline="", encoding="utf-8") as f:
                w = csv.writer(f)
                w.writerow(["logging_tag_name", "error_code",
                            "error_description", "value", "value_timestamp",
                            "flags"])
                for block in payload["data"]["loggedTagValues"] or []:
                    err = block.get("error") or {}
                    for item in block.get("values") or []:
                        val = (item.get("value") or {})
                        w.writerow([
                            block.get("loggingTagName"),
                            err.get("code", ""), err.get("description", ""),
                            json.dumps(val.get("value"), ensure_ascii=False),
                            val.get("timestamp"),
                            ",".join(item.get("flags") or []),
                        ])
            log.info("wrote %s", args.out)
        return 0

    payload = client.post_raw(ALARMS_QUERY, {
        "s": [args.system], "f": args.filter,
        "fl": args.filter_language,
        "l": [x for x in args.languages.split(",") if x],
        "a": start, "b": end, "m": args.max,
    })
    print(json.dumps(payload, indent=2, ensure_ascii=False)[:20000])
    hint(payload)
    if payload.get("errors"):
        return 1
    if args.out:
        Path(args.out).parent.mkdir(parents=True, exist_ok=True)
        rows = payload["data"]["loggedAlarms"] or []
        fields = ["name", "instanceID", "alarmGroupID", "state", "raiseTime",
                  "acknowledgmentTime", "clearTime", "resetTime",
                  "modificationTime", "alarmClassName", "priority",
                  "sourceType", "suppressionState"]
        with open(args.out, "w", newline="", encoding="utf-8") as f:
            w = csv.writer(f)
            w.writerow(fields + ["event_text", "alarm_text1"])
            for row in rows:
                w.writerow([row.get(k, "") for k in fields] + [
                    json.dumps(row.get("eventText"), ensure_ascii=False),
                    json.dumps(row.get("alarmText1"), ensure_ascii=False),
                ])
        log.info("wrote %s", args.out)
    return 0


if __name__ == "__main__":
    sys.exit(main())
