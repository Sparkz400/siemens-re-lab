# Analysis

This directory contains **curated summaries**, not raw vendor artifacts.

- `schema-summary.md` — root fields, tag model and auth notes derived from a local
  introspection run.
- The raw `schema.runtime.graphql` / `introspection.json` are generated locally with
  `probe/dump_schema.py` and are intentionally not committed (vendor-derived material).

The GraphQL server itself is a Node.js SEA (`WCCILgraphQLServer.exe`); PHMI plugin schemas and
WebPHMI operation names are on the installed disk. See `docs/04-wincc-graphql.md`.
