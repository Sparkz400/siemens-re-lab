#!/usr/bin/env python3
"""Minimal WinCC Unified GraphQL probe/logger.

  python unified_probe.py browse --user Lab --password '...'
  python unified_probe.py read   --user Lab --password '...' --tag Tag01
  python unified_probe.py write  --user Lab --password '...' --tag Tag01 --value 42.5
  python unified_probe.py watch  --user Lab --password '...' --tags Tag01,Tag02 --duration 10 --out tags.csv
"""
import argparse
import csv
import json
import sys
import time

import requests

URL = "http://localhost:4000/graphql"
SYSTEM = "HMI_RT_1"


class Client:
    def __init__(self, url=URL):
        self.url = url
        self.token = None
        self.s = requests.Session()

    def _post(self, query, variables=None):
        headers = {"Content-Type": "application/json", "Connection": "close"}
        if self.token:
            headers["Authorization"] = "Bearer " + self.token
        r = self.s.post(self.url, json={"query": query, "variables": variables or {}},
                        headers=headers, timeout=30)
        r.raise_for_status()
        payload = r.json()
        if "errors" in payload:
            raise RuntimeError(json.dumps(payload["errors"], ensure_ascii=False))
        return payload["data"]

    def login(self, user, password):
        data = self._post(
            "mutation($u:String!,$p:String!){login(username:$u,password:$p){token error{code description}}}",
            {"u": user, "p": password},
        )["login"]
        if not data.get("token"):
            raise RuntimeError("login failed: " + json.dumps(data.get("error")))
        self.token = data["token"]
        return self.token

    def browse(self):
        return self._post('{ browse(nameFilters:["*"]) { name displayName objectType dataType tagTypeName } }')["browse"]

    def tag_values(self, names, direct=False):
        return self._post(
            "query($n:[String!]!,$d:Boolean){tagValues(names:$n,directRead:$d){name value{value timestamp} error{code description}}}",
            {"n": names, "d": direct},
        )["tagValues"]

    def write(self, name, value):
        return self._post(
            "mutation($i:[TagValueInput]!){writeTagValues(input:$i){name error{code description}}}",
            {"i": [{"name": name, "value": value}]},
        )["writeTagValues"]


def qname(name):
    if "::" in name:
        return name
    return f"{SYSTEM}::{name}"


def scalar(v):
    if isinstance(v, dict):
        return v.get("value")
    return v


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("command", choices=["browse", "read", "write", "watch"])
    ap.add_argument("--url", default=URL)
    ap.add_argument("--user")
    ap.add_argument("--password")
    ap.add_argument("--tag", action="append", default=[])
    ap.add_argument("--tags")
    ap.add_argument("--value", default=None)
    ap.add_argument("--out", default="tags.csv")
    ap.add_argument("--interval", type=float, default=1.0)
    ap.add_argument("--duration", type=float, default=0)
    a = ap.parse_args()

    c = Client(a.url)
    if a.user:
        c.login(a.user, a.password)

    if a.command == "browse":
        for row in c.browse():
            print("\t".join(str(row.get(k)) for k in ("name", "objectType", "dataType", "displayName")))
        return

    names = [qname(t) for t in a.tag]
    if a.tags:
        names += [qname(t.strip()) for t in a.tags.split(",") if t.strip()]
    if not names:
        sys.exit("no tags given")

    if a.command == "write":
        if a.value is None:
            sys.exit("--value is required")
        for row in c.write(names[0], json.loads(a.value)):
            print(json.dumps(row, ensure_ascii=False))
        return

    if a.command == "read":
        for row in c.tag_values(names, direct=True):
            print(json.dumps(row, ensure_ascii=False))
        return

    start = time.time()
    with open(a.out, "w", newline="", encoding="utf-8") as f:
        w = csv.writer(f)
        w.writerow(["collected_utc", "tag", "value", "value_timestamp", "error"])
        print("writing", a.out, "Ctrl+C to stop")
        try:
            while True:
                now = time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime())
                for row in c.tag_values(names):
                    v = row.get("value") or {}
                    w.writerow([now, row.get("name"), scalar(v),
                                v.get("timestamp"),
                                json.dumps(row.get("error"), ensure_ascii=False) if row.get("error") else ""])
                f.flush()
                if a.duration and time.time() - start >= a.duration:
                    break
                time.sleep(a.interval)
        except KeyboardInterrupt:
            pass


if __name__ == "__main__":
    main()
