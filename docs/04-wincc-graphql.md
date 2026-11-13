# WinCC Unified GraphQL API

Captured from the live Unified PC runtime (V21) on 2026-11-12. The raw SDL is generated locally
and not committed; a curated summary and the generator scripts are in `probe/` and
`analysis/schema-summary.md`.

## Endpoint

```text
POST http://localhost:4000/graphql
```

- **HTTP**, not HTTPS. Port 4000 does not speak TLS.
- Introspection is enabled (`config.yaml: graphql.schemaIntrospection: true`).
- The server is `WCCILgraphQLServer.exe`, a Node.js SEA (embedded `server.js`, `authCAPI.js`,
  `authSWAC.js`, `sessions.js`, `build/`, `tags.js`, `alarms.js`, graphql-ws).
- PHMI plugin schemas ship as `.graphql` files; WebPHMI bundles contain the operation names.

## Contract size

| Kind | Count |
|---|---|
| Types | 138 |
| Queries | 8 |
| Mutations | 11 |
| Subscriptions | 3 |

### Queries

| Field | Purpose |
|---|---|
| `session(allSessions: Boolean)` | current session / user |
| `nonce` | UMC SWAC nonce |
| `identityProviderURL` | UMC SSO URL |
| `tagValues(names: [String!]!, directRead: Boolean)` | read current tag values |
| `browse(nameFilters, objectTypeFilters, baseTypeFilters, language)` | discover tags, types, alarms, logs |
| `loggedTagValues(names, startTime, endTime, maxNumberOfValues, sortingMode, boundingValuesMode)` | historical values |
| `activeAlarms(systemNames, filterString, filterLanguage, languages)` | current alarms |
| `loggedAlarms(systemNames, filterString, filterLanguage, languages, startTime, endTime, maxNumberOfResults)` | alarm history |

### Mutations

`login`, `loginSWAC`, `extendSession`, `logout`, `writeTagValues`, `acknowledgeAlarms`,
`resetAlarms`, `disableAlarms`, `enableAlarms`, `shelveAlarms`, `unshelveAlarms`.

### Subscriptions (graphql-ws)

`tagValues(names: [String!]!)`, `activeAlarms(...)`, `reduState`.

## Auth

```graphql
mutation Login($u: String!, $p: String!) {
  login(username: $u, password: $p) {
    token
    user { name }
    error { code description }
  }
}
```

Send `Authorization: Bearer <token>` on every query, mutation and subscription. `Session.token`
is 32 hex characters in the current build.

`loginSWAC(claim, signedClaim)` is the UMC identity-provider path: query `nonce`, open
`identityProviderURL`, authenticate, then exchange the claim pair.

### Local UMC user (lab)

The runtime uses **local user management** with user `Lab` in group **HMI Administrator**.
Credentials are not stored in this repo. The runtime must contain an active `UMCData.json`; see
[03-lab-project.md](03-lab-project.md) for the `-overwriteUmc` import.

Common failure: `login` returns `code 102` / `UMC error 273` for every account. Causes seen:
- `UMCData_disabled.json` (runtime imported in keep mode) — fix with `-overwriteUmc`.
- empty UMC database / disabled local user management — fix in TIA user management and
  re-download.

## Tag model

```graphql
type Value {
  value: Variant
  timestamp: Timestamp
  quality: Quality
}

type TagValueResult {
  name: String
  value: Value
  error: Error
}
```

Tag names are fully qualified: `HMI_RT_1::Tag01`. Select the object fields:

```graphql
{
  tagValues(names: ["HMI_RT_1::Tag01", "HMI_RT_1::Tag02"]) {
    name
    value { value timestamp }
    error { code description }
  }
}
```

Write:

```graphql
mutation {
  writeTagValues(input: [
    { name: "HMI_RT_1::Tag01", value: 42.5 }
  ]) { name error { code description } }
}
```

## Gotchas

- `browse` with no arguments resets the connection in this build. Use
  `browse(nameFilters: ["*"])`.
- `value` is an object, not a scalar. `value { value timestamp }`.
- Internal tags report timestamp `1970-01-01` until written.
- The server may restart its process after some requests; reconnect and re-login on reset.
- `directRead: true` reads from the PLC when the tag is PLC-backed; internal tags ignore it.
- The GraphQL endpoint is HTTP; `https://localhost:4000` fails the TLS handshake.
- History is a separate query (`loggedTagValues`) and may also live in archive files /
  `ReportingReverseProxy`, not only GraphQL.

## Live subscriptions (graphql-ws)

Captured 2026-11-12 with `websocket-client` 1.9.2 against the live runtime.

| Item | Value |
|---|---|
| URL | `ws://localhost:4000/graphql` (same path as HTTP) |
| Subprotocol | `graphql-transport-ws` |
| Auth | `connection_init.payload.Authorization = "Bearer <token>"` |
| Ack | `{"type":"connection_ack"}` |
| Framing | one `next` message per tag per change |
| Keepalive | client `{"type":"ping"}` → server `{"type":"pong"}` |

The HTTP `Authorization` header on the upgrade request is **ignored** by the resolver. Sending
only the header acknowledges the connection but the first subscription field returns
`UNAUTHENTICATED` / "User is not logged in"; the token must be in the `connection_init` payload.

Minimal transcript:

```json
→ {"type":"connection_init","payload":{"Authorization":"Bearer <token>"}}
← {"type":"connection_ack"}
→ {"id":"tags","type":"subscribe","payload":{"query":"subscription($n:[String!]!){tagValues(names:$n){name value{value timestamp} notificationReason}}","variables":{"n":["HMI_RT_1::Tag01"]}}}
← {"id":"tags","type":"next","payload":{"data":{"tagValues":{"name":"HMI_RT_1::Tag01","value":{"value":12,"timestamp":"2026-11-12T14:18:26.779Z"},"notificationReason":"Modified"}}}}
```

Observed behaviour:

- `notificationReason` values seen: `Added` (initial snapshot after subscribe **and again after
  every resubscribe**) and `Modified` (value change).
- The server sends one message per changed tag, not a batch: 10 tags at 1 Hz ≈ 10 msg/s.
- No server-initiated `ping` was observed in a idle 12 s window; client pings work, so the logger
  uses them as a liveness check.
- `complete` ends the subscription; the client reconnects and re-subscribes.
- The WebSocket endpoint is HTTP-only like the query endpoint; `wss://` is not available in this
  installation.

## Live logger (M1)

`probe/live_logger.py` subscribes to `tagValues`, writes every notification to SQLite and CSV,
reconnects with exponential backoff, re-authenticates on every reconnect and records
sessions/gaps.

```powershell
$env:WINCC_USER = 'Lab'
$env:WINCC_PASSWORD = '...'
$py = "$env:LOCALAPPDATA\Programs\Python\Python312\python.exe"

& $py probe/live_logger.py --config probe/live_logger.example.toml
& $py probe/live_logger.py --config probe/live_logger.example.toml --duration 3600
```

`probe/demo_writer.py` generates deterministic 1 Hz changes for tests and soak runs:

```powershell
& $py probe/demo_writer.py --tags Tag01,Tag02,Tag03,Tag04,Tag05,Tag06,Tag07,Tag08,Tag09,Tag10 `
    --duration 3590 --interval 1
```

### Behaviour

- **Auth:** a fresh `login` runs before every WebSocket connect, so a runtime restart or an
  expired token recovers without restarting the logger. Bad credentials / UMC errors are logged
  to `events` and retried.
- **Backoff:** 1 s → 2 s → 4 s … capped at 30 s with 20% jitter; reset after a session that
  lasted more than 30 s.
- **Liveness:** client ping every 15 s; a missing pong for 10 s forces a reconnect. The socket
  read timeout is 2 s, so `Ctrl+C`/SIGTERM stop promptly.
- **Resync:** every reconnect re-subscribes all tags; the runtime sends an `Added` snapshot per
  tag. Consumers should treat `Added` after a reconnect as a resync, not a process change.
- **Gaps:** the subscription is event-driven, so a quiet tag produces no rows. A gap is the time
  between losing the subscription and the next successful `connection_ack`; it is recorded in
  `sessions` (`disconnect_reason`, reconnect time) and as an `events` row with
  `{"kind":"gap","detail":{"seconds":10.6}}`.
- **Durability:** SQLite uses WAL and commits at most every 1 s / 200 rows; CSV is flushed per
  row. Restarting the logger with the same files appends to them.

### Storage schema

`samples` (one row per notification):

| Column | Meaning |
|---|---|
| `received_utc` | logger receive time (ISO-8601, `Z`) |
| `session_id` | FK to `sessions` |
| `tag` | fully qualified tag name |
| `value_json` / `value_num` | raw Variant as JSON / numeric convenience column |
| `value_timestamp` | runtime timestamp of the value |
| `notification_reason` | `Added` or `Modified` |
| `quality_json` | `value.quality` when the server supplies it |
| `error_code` / `error_description` | per-tag error (`0` / `Succeeded` normally) |

`sessions`: connect/disconnect timestamps, disconnect reason, and per-session
`notifications` / `added` / `modified` counts. `events`: `connect`, `gap`, `auth_failed`,
`connect_failed`, `session_error`, `subscription_error`, `stop`, with JSON detail. `meta`:
logger version, URL, tag list and start time.

CSV columns:

```text
received_utc,session_id,tag,value,value_timestamp,notification_reason,error_code,error_description
```

Analyze a capture (counts, rates, intervals, sessions, gaps, integrity):

```powershell
& $py probe/soak_report.py --sqlite data/live.sqlite --csv data/live.csv
```

### Service mode

The logger is a console program; Task Scheduler (trigger *At startup*, run as the lab user) or
NSSM can host it. Put `WINCC_USER`/`WINCC_PASSWORD` in the user/service environment and pass
`--log` to a writable path (for example `C:\ProgramData\WinccLiveLogger\logger.log`). Never put
credentials in the task XML, the TOML config or the repository.

### M1 test results

| Test | Result |
|---|---|
| 75 s, 10 tags, 1 Hz | 710 samples (10 `Added` + 700 `Modified`), 0 errors; steady-state max inter-arrival 1.068 s |
| Forced 10.6 s outage (TCP proxy) | connection reset → 3 login/backoff attempts → new session → 10 `Added` resync, 530 further notifications; gap recorded |
| 1 h soak, 10 tags, 1 Hz | **partial**: 8,521 samples / 14.3 min, 0 errors, one 2.5 s gap when the GraphQL server restarted during alarm probing. A hard stop left 9 CSV rows not yet committed to SQLite. Clean 1 h rerun pending. |

## Alarms and history (M2)

`probe/unified_alarms.py` and `probe/unified_history.py` implement the M2 client. The live
runtime currently returns `INTERNAL_SERVER_ERROR` for the alarm/history root fields; the
subscription logger records that error and reconnects. Storage evidence, API shapes and the
fixture path are in [11-alarms-history.md](11-alarms-history.md).

## Probe

`probe/unified_probe.py` implements login, browse, read, write and a polling CSV watcher.

```powershell
$py = "$env:LOCALAPPDATA\Programs\Python\Python312\python.exe"
& $py probe/unified_probe.py browse --user Lab --password '...'
& $py probe/unified_probe.py read   --user Lab --password '...' --tag Tag01
& $py probe/unified_probe.py write  --user Lab --password '...' --tag Tag01 --value 42.5
& $py probe/unified_probe.py watch  --user Lab --password '...' --tags Tag01,Tag02 --duration 10 --out tags.csv
```

`watch` polls `tagValues` at `--interval` seconds and writes
`collected_utc,tag,value,value_timestamp,error`. For long-running capture use
`live_logger.py` (subscriptions, reconnect, SQLite).

## Demo evidence

- 20 internal tags discovered (`Tag01`…`Tag20`, Real).
- Write/read round-trip verified (`42.5`, `7`, `1`).
- 10 tags at 0.5 s for 8 s → 170 CSV rows; values changed live
  (`Tag01 42.5 → 85`, `Tag10 0 → 90`).
- Sample: `examples/tags.csv`.

## Static findings

- `WCCILgraphQLServer.exe` contains a `NODE_SEA_BLOB` asset pack and a module index listing
  `server.js`, `authentication/auth*.js`, `sessions.js`, `build/`, `tags.js`,
  `tagsSubscriber.js`, `alarms.js`, `alarmsSubscriber.js`, `introspectionModifierApolloPlugin.js`.
- `bin\PHMIGraphQLConfiguration\*.graphql` contains the PHMI configuration/kiosk/monitor/RFID/
  screen-assignment/UMC schemas.
- `WebPHMI\main.*.js` contains the browser operation names (`Phmi_*`, `login`, `ExtendSession`,
  `Logout`, …).
