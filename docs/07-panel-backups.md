# Goal 4 — Panel backup (`.psb`) unpacker

## Objective

Unpack a Siemens HMI panel backup (`.psb`) into its container contents: identify compression and
any encryption, extract screens, tags and configuration, and diff two backups of the same panel
where one screen changed.

## Current blocker

- No HMI panel is connected to this lab.
- No `.psb` sample exists on the machine.
- ProSave cannot create a backup without hardware.

Downloadable panel firmware allows image analysis but **not** backup extraction; the useful
artifact is a real backup from a panel we own or are authorized to service.

## Plan (once a sample exists)

1. Inventory the file: magic, header, section table, entropy, embedded ZIP/CAB/TAR signatures.
2. Try standard containers first (`zip`, `cab`, `7z`) and vendor-specific wrappers.
3. Identify compression (zlib/LZ4/LZMA) and encryption markers; document offsets in ImHex.
4. Extract screen and tag payloads; map XML/RDF/JSON/proprietary records.
5. Compare two backups of the same panel, one screen changed, and produce a readable diff.
6. Keep the parser read-only; never write back to a panel.

## Acceptance

1. One known `.psb` parses into a documented container map with hashes.
2. At least screens and tag configuration are extracted.
3. Two backups diff into a screen-level report.
4. No protection/password bypass; only artifacts we are authorized to inspect.

## Legal / safety

Panel backups can contain customer data, credentials and licensed firmware. Do not publish
backups or extracted proprietary assets. Document structure and hashes, not vendor payloads.

## Status

Blocked. Parked until a `.psb` (with permission) is available. ProSave is installed; it needs a
physical panel to create a backup.
