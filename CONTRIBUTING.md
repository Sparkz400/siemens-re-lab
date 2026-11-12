# Contributing

This is a research lab, not a product. Contributions should keep the evidence reproducible and
the repository free of vendor binaries, credentials and customer data.

## Before opening a PR

1. Do not add Siemens installers, DLLs, project archives (`.ap21`/`.zap21`), runtime packages,
   panel backups (`.psb`), firmware, licenses or certificates.
2. Do not add passwords, tokens, DPAPI files, packet captures or logs from a real environment.
3. Prefer a script + a short synthetic example over a large captured artifact.
4. Document the exact product versions and steps used for a result.
5. Keep credentials out of command examples; use `'...'` placeholders.

## Code style

- Python: standard library where possible; `requests` + `graphql-core` for the probe.
- Markdown: one topic per file under `docs/`; link back to the goals.
- Commit messages: `area: what changed` (for example `graphql: add subscription watcher`).

## Evidence

A finding is more useful with:
- product/version,
- exact command or tool path,
- expected vs observed result,
- sanitized log/CSV excerpt,
- date and host class (VM/physical).

Do not include secrets or customer identifiers in evidence.
