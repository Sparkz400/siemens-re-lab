# Goals and acceptance criteria

Four independent projects share one lab target: a TIA V21 project with an S7-1500 CPU on
PLCSIM Advanced and a WinCC Unified PC RT station. The shared rig lets a project change be
observed through TIA (goal 1), S7comm-plus (goal 2) and the Unified GraphQL API (goal 3), and
cross-checked for agreement.

---

## Goal 1 — TIA project snapshot / diff

**End goal:** a `tia-snapshot` CLI/library that captures the state of an offline TIA project and
produces a readable diff between two captures, without opening TIA twice or relying on manual
exports.

**Snapshot contents**
- Immutable copy/archive of the project (`.zap21`) with SHA-256.
- Manifest: project name, TIA version, devices, hardware/order numbers, firmware.
- PLC software: blocks (name, number, type, language, consistency, protection), tag tables/tags,
  UDTs, technology objects.
- HMI: Unified/Classic devices, screens and items, tags, connections, alarms, logs, runtime
  settings.
- Per-object hashes from native export where available; stable identity paths.

**Diff report**
- Added / removed / modified / moved objects.
- Block source or interface changes summarized; optional unified diff of exported XML/SCL.
- Screen/tag/connection/alarm changes.
- Ignore volatile fields (timestamps, document IDs, transient metadata).
- Markdown and JSON output; exit code non-zero on differences for CI.

**Acceptance**
1. Two snapshots of an unchanged project produce an empty diff.
2. Adding one tag, changing one screen property and editing one block each produce exactly one
   expected change entry.
3. Snapshot is reproducible from the same project archive.
4. Diff works without a live TIA instance (archive-based).

**Current status:** CLI implemented (2026-11-13). `probe/tia_snapshot.py` captures a TIA MCP
project review into a portable snapshot (`snapshot.json` + `review.json`) and compares two
snapshots offline via `--no-native-workers`, producing Markdown/JSON with a CI exit code. Real
project acceptance captures are pending. Details in [12-tia-snapshot-diff.md](12-tia-snapshot-diff.md).

**Risks:** object exports are not available for every protected/safety object; HMI screen export
coverage differs between Classic and Unified; generated system objects must be filtered.

---

## Goal 2 — S7comm-plus client

**End goal:** a read-only tag client that speaks S7comm-plus to an S7-1500 (or PLCSIM Advanced)
without TIA/WinCC, initially using known DB offsets, later symbolic browsing. A second phase
bridges values to MQTT on a Raspberry Pi.

**Scope**
- S7-1500 / PLCSIM Advanced V8 first; classic S7-300/400 requires a Snap7 server or real CPU.
- Read-only MVP; write safety is a separate, later decision.
- Capture TIA ↔ PLCSIM download/online traffic with Wireshark + S7comm-plus dissector.

**Acceptance**
1. Read 10 configured DB tags at 1 Hz for 1 hour from PLCSIM Advanced.
2. Values match the TIA watch table at sampled instants.
3. Reconnect after PLCSIM stop/start without restarting the client.
4. No write request is emitted in the MVP.
5. Capture contains the handshake and tag reads, documented in the repo.

**Current status:** planned. PLCSIM Advanced V8 is installed with the virtual Ethernet adapter;
no instance is running. No real PLC and no HMI panel. Wireshark is not installed (installer
available). Prior art to study: S7CommPlusDriver (C#), the Wireshark S7comm-plus dissector, and
python-snap7 for classic S7comm.

**Risks:** plus handshake/key exchange, firmware/protection variants, symbolic browsing
complexity, and the absence of real hardware.

---

## Goal 3 — WinCC Unified extractor

**End goal:** a CLI/library that logs selected Unified tags and alarms to CSV/SQLite/InfluxDB,
with Grafana on top. The archive/history extractor is a later phase; the live GraphQL API is the
first deliverable.

**Phases**
1. **MVP (done):** schema introspection, `login`, `browse`, `tagValues`, `writeTagValues`, CSV
   watcher.
2. **Live subscriptions (done):** graphql-ws `tagValues` subscription with reconnect/backoff,
   SQLite + CSV logging, session/gap records and a 14.3 min soak (clean 1 h rerun pending).
3. **Alarms (history/control working, activeAlarms blocked):** `activeAlarms` query/subscription
   still returns `INTERNAL_SERVER_ERROR`; acknowledge/reset/enable/disable/shelve/unshelve and
   the `loggedAlarms` history work. A history-polling feed covers live alarms (see
   [11-alarms-history.md](11-alarms-history.md)).
4. **History (working):** `loggedTagValues` and `loggedAlarms` time-range queries verified live;
   storage is in the project's `APERDB`/`TPERDB` SQLite databases plus the configured logs.
5. **Storage/dashboard:** SQLite or InfluxDB writer + Grafana.
6. **Packaging:** config file, service mode, schema-driven tag discovery.

**Acceptance**
1. 10 tags at 1 Hz for 1 hour with no gap > 2 s.
2. Values match the Unified screen / API at sampled instants.
3. Alarm subscribe + acknowledge round-trip recorded.
4. SQLite is restart-safe and queryable; CSV format documented.
5. Login uses a local UMC user; no credentials in files.

**Current status:** live logger working; tag/alarm history and alarm control verified (2026-11-13).
138 types / 8 queries / 11 mutations / 3 subscriptions; user `Lab`; 20 internal tags; graphql-ws
`tagValues` with reconnect/backoff, SQLite + CSV, and a 14.3 min soak. `loggedTagValues` and
`loggedAlarms` return live data; acknowledge/reset/enable/disable/shelve/unshelve work. The direct
`activeAlarms` query/subscription still returns `INTERNAL_SERVER_ERROR`; see
[11-alarms-history.md](11-alarms-history.md).

**Risks:** UMC configuration (local vs central), editor-only transfer password for headless
Unified stations (G-004), GraphQL connection resets on an unfiltered `browse`, and history
possibly living outside the GraphQL endpoint.

---

## Goal 4 — Panel backup unpacker

**End goal:** a `.psb` panel backup unpacker: identify the container format, compression and
any encryption, extract screens/tags/alarms/configuration, and compare two backups of the same
panel where one screen changed.

**Scope**
- Only backups we own or are authorized to inspect.
- No bypass of panel protection, passwords or licensing.
- ProSave / TIA panel firmware for context; ImHex for structural analysis.

**Acceptance**
1. One known `.psb` parses into a documented container map.
2. Extracted assets include at least screens and tag configuration.
3. Two backups of the same panel diff into a readable screen-level change report.
4. Findings documented with hashes and offsets.

**Current status:** **blocked.** No HMI panel, no `.psb` sample, and ProSave cannot create a
backup without hardware. Downloadable panel firmware would allow image analysis but not backup
extraction.

**Risks:** proprietary/encrypted container, no sample, legal constraints on firmware.

---

## Shared lab project

| Item | Value |
|---|---|
| TIA project | `TiaMcpScratch_20261112_123345_9eb052ce` |
| PLC | CPU 1511-1 PN V4.0, station `ScratchPLC` (PLCSIM Advanced target) |
| HMI | `HMI_RT_1`, WinCC Unified PC RT 21.0.0.0, `PC-System_1` |
| Screen | `Overview_A`, 1920×1080, start screen |
| Tags | `Tag01`…`Tag20`, Real, internal |
| Runtime user | `Lab`, HMI Administrator (local UMC) |
| GraphQL | `http://localhost:4000/graphql` |

A change made through TIA Openness should be visible in the GraphQL model after download, and
the same values should eventually be readable over S7comm-plus from the PLC side. That
cross-check is the main reason the four goals share one rig.
