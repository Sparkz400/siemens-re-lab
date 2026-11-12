#!/usr/bin/env python3
"""Dump the WinCC Unified GraphQL schema from a running runtime."""
import argparse
import json
import os

import requests
from graphql import build_client_schema, get_introspection_query, print_schema


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--url", default="http://localhost:4000/graphql")
    ap.add_argument("--out", default=".")
    a = ap.parse_args()
    os.makedirs(a.out, exist_ok=True)

    r = requests.post(a.url, json={"query": get_introspection_query(descriptions=True)}, timeout=60)
    r.raise_for_status()
    payload = r.json()
    if "errors" in payload:
        raise SystemExit("GraphQL errors: " + json.dumps(payload["errors"])[:2000])

    with open(os.path.join(a.out, "introspection.json"), "w", encoding="utf-8") as f:
        json.dump(payload, f)

    schema = build_client_schema(payload["data"])
    sdl = print_schema(schema)
    with open(os.path.join(a.out, "schema.runtime.graphql"), "w", encoding="utf-8") as f:
        f.write(sdl)

    print("types:", len(schema.type_map))
    print("queries:", len(schema.query_type.fields))
    print("mutations:", len(schema.mutation_type.fields) if schema.mutation_type else 0)
    print("subscriptions:", len(schema.subscription_type.fields) if schema.subscription_type else 0)
    print("sdl bytes:", len(sdl))


if __name__ == "__main__":
    main()
