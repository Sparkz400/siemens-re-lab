#!/usr/bin/env python3
"""Summarize an introspection.json into a Markdown operation map."""
import argparse
import json

from graphql import build_client_schema


def fmt(name, f):
    args = ", ".join(f"{a}: {f.args[a].type}" for a in f.args)
    return f"- `{name}({args})` -> `{f.type}`"


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--introspection", default="introspection.json")
    ap.add_argument("--out", default="schema-summary.md")
    a = ap.parse_args()

    raw = json.load(open(a.introspection, encoding="utf-8"))
    schema = build_client_schema(raw["data"])
    lines = ["# WinCC Unified GraphQL schema (runtime)", ""]
    lines.append(f"Types: {len(schema.type_map)} | Queries: {len(schema.query_type.fields)} | "
                 f"Mutations: {len(schema.mutation_type.fields)} | "
                 f"Subscriptions: {len(schema.subscription_type.fields)}")
    lines.append("")
    for title, root in [("Queries", schema.query_type), ("Mutations", schema.mutation_type),
                        ("Subscriptions", schema.subscription_type)]:
        lines.append(f"## {title}")
        for name, f in root.fields.items():
            desc = (f.description or "").strip().split("\n")[0]
            lines.append(fmt(name, f) + (f"  \n  {desc}" if desc else ""))
        lines.append("")
    for tname in ["TagValueResult", "Value", "Quality", "TagValueNotification", "BrowseTagsResult"]:
        t = schema.type_map.get(tname)
        if t and hasattr(t, "fields"):
            lines.append(f"### {tname}")
            for n, f in t.fields.items():
                lines.append(f"- `{n}`: `{f.type}`")
            lines.append("")
    open(a.out, "w", encoding="utf-8").write("\n".join(lines))
    print("wrote", a.out)


if __name__ == "__main__":
    main()
