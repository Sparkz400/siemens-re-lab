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
`collected_utc,tag,value,value_timestamp,error`. A future version adds graphql-ws subscriptions.

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
