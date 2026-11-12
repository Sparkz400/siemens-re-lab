# Roadmap

## Recommended order

1. **Goal 3 — Unified extractor** (working MVP; finish subscriptions, alarms, storage).
2. **Goal 2 — S7comm-plus** (the real RE track; 2–4 weeks for a read-only MVP).
3. **Goal 1 — TIA snapshot/diff** (highest daily value if TIA engineering is the day job;
   can run in parallel because the TIA MCP already exposes the primitives).
4. **Goal 4 — panel backups** (parked until a `.psb` exists).

## Milestones

### M0 — Lab and evidence (done)
- TIA project + Unified PC runtime + 20 tags + user `Lab`.
- GraphQL schema captured; login/browse/read/write/watch verified.
- TIA MCP v0.9.0 built and installed; toolchain documented.

### M1 — Goal 3 live logger (code complete, soak running)
- [x] graphql-ws `tagValues` subscription with reconnect/backoff.
- [x] Config file (URL, user, tags, interval, storage).
- [x] SQLite writer + CSV export; session and gap records.
- [ ] 1 h / 10-tag soak test (running); gap and value cross-check against the Unified screen.
- [x] Windows service wrapper notes (Task Scheduler/NSSM) in docs/04.

### M2 — Goal 3 alarms + history (~1–2 weeks)
- `activeAlarms` subscription; acknowledge/reset/enable/shelve round-trip.
- `loggedTagValues`/`loggedAlarms` time-range queries.
- Locate history storage (`ReportingReverseProxy` vs archive files) and document it.

### M3 — Goal 2 capture + read-only client (2–4 weeks)
- Wireshark install; capture TIA ↔ PLCSIM; annotate handshake/read.
- Offset-based reader against 10 DB tags.
- Reconnect and 1 h soak; protocol notes in repo.

### M4 — Goal 1 snapshot/diff (1–2 weeks)
- Snapshot manifest from `project_model` + exports; archive hash.
- Diff engine with stable identities and ignore rules.
- Markdown/JSON report; CI exit code.

### M5 — Goal 2 symbolic + Pi/MQTT (later)
- Symbolic browse if feasible; Pi bridge; write safety remains out of scope.

### M6 — Goal 4 (when a `.psb` appears)
- Container map, extraction, two-backup diff.

## Cross-check matrix

The shared rig is most valuable when all three tracks observe the same change:

| Change | Goal 1 (project) | Goal 2 (S7comm-plus) | Goal 3 (GraphQL) |
|---|---|---|---|
| Add PLC DB tag | diff shows tag | read value by offset | external HMI tag value |
| Edit Unified screen | diff shows screen item | — | screen model/browse |
| Change tag value | — | value changes | `tagValues` changes |
| Alarm triggers | diff shows alarm config | DB bit/word | `activeAlarms` |

## Open decisions

- Runtime history: GraphQL `loggedTagValues` vs archive/ReportingReverseProxy as source of truth.
- Storage: SQLite first, InfluxDB/Grafana once the schema is stable.
- S7comm-plus: Python vs C#; offset-first vs symbolic-first.
- Goal 1 snapshot format: archive-only vs archive + native exports (archives are authoritative,
  exports are diff-friendly).
