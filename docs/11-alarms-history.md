# M2 — alarms and history

Status: **client implemented, live round-trip blocked by the lab runtime.**

The GraphQL contract exists, but on this project the alarm and history root fields return
`INTERNAL_SERVER_ERROR` even after a clean runtime stop/start. This document records the client,
the exact observations, the runtime storage layout, and the remaining fixture work.

## Tools

| Tool | Purpose |
|---|---|
| `probe/unified_alarms.py` | active alarms, `activeAlarms` subscription logger (SQLite + CSV, reconnect), ack/reset/enable/disable/shelve/unshelve |
| `probe/unified_history.py` | `loggedTagValues` and `loggedAlarms` time-range queries with optional CSV |
| `probe/tests/test_alarm_store.py` | storage unit tests (no runtime required) |

Credentials come from `--user`/`--password`, `WINCC_USER`/`WINCC_PASSWORD`, or a prompt.

```powershell
$env:WINCC_USER = 'Lab'
$env:WINCC_PASSWORD = '...'
$py = "$env:LOCALAPPDATA\Programs\Python\Python312\python.exe"

# active alarms / subscription
& $py probe/unified_alarms.py list
& $py probe/unified_alarms.py watch --duration 3600 `
    --sqlite data/alarms.sqlite --csv data/alarms.csv

# alarm actions
& $py probe/unified_alarms.py ack      --name HMI_RT_1::Alarm_1 --instance 0
& $py probe/unified_alarms.py reset    --name HMI_RT_1::Alarm_1
& $py probe/unified_alarms.py disable  --name HMI_RT_1::Alarm_1
& $py probe/unified_alarms.py enable   --name HMI_RT_1::Alarm_1
& $py probe/unified_alarms.py shelve   --name HMI_RT_1::Alarm_1 --timeout 60
& $py probe/unified_alarms.py unshelve --name HMI_RT_1::Alarm_1

# history
& $py probe/unified_history.py tags --name Tag01 --max 100 `
    --start 2026-11-13T05:00:00Z --end 2026-11-13T06:00:00Z
& $py probe/unified_history.py alarms --system HMI_RT_1 --max 100 `
    --start 2026-11-13T05:00:00Z --end 2026-11-13T06:00:00Z
```

Bare alarm names are qualified as `<system>::<name>`. `--instance 0` means all instances for
ack/reset. `shelve --timeout` is passed to the `Timespan` scalar; omit it for the runtime default.

## API shapes

Queries:

```graphql
activeAlarms(systemNames: [String], filterString: String,
             filterLanguage: String, languages: [String]): [ActiveAlarm]

loggedTagValues(names: [String]!, startTime: Timestamp, endTime: Timestamp,
                maxNumberOfValues: Int, sortingMode: LoggedTagValuesSortingModeEnum,
                boundingValuesMode: LoggedTagValuesBoundingModeEnum): [LoggedTagValuesResult]

loggedAlarms(systemNames: [String], filterString: String, filterLanguage: String,
             languages: [String], startTime: Timestamp, endTime: Timestamp,
             maxNumberOfResults: Int): [LoggedAlarm]
```

Subscriptions:

```graphql
activeAlarms(systemNames: [String], filterString: String,
             filterLanguage: String, languages: [String]): ActiveAlarmNotification
tagValues(names: [String!]!): TagValueNotification
reduState: ReduStateNotification
```

Mutations:

```graphql
acknowledgeAlarms(input: [AlarmIdentifierInput]!): [ActiveAlarmMutationResult]
resetAlarms(input: [AlarmIdentifierInput]!):       [ActiveAlarmMutationResult]
disableAlarms(names: [String]!):                    [AlarmMutationResult]
enableAlarms(names: [String]!):                     [AlarmMutationResult]
shelveAlarms(names: [String]!, shelveTimeout: Timespan): [AlarmMutationResult]
unshelveAlarms(names: [String]!):                   [AlarmMutationResult]
```

`AlarmIdentifierInput { name: String!, instanceID: Int = 0 }`.

`ActiveAlarm` and `ActiveAlarmNotification` share the alarm attributes and add
`notificationReason` on the notification type only. `LoggedAlarm` lacks `userResponse` (a
validation error if selected). Useful fields: `name`, `instanceID`, `state`,
`raiseTime`, `acknowledgmentTime`, `clearTime`, `resetTime`, `alarmClassName`, `priority`,
`eventText`, `alarmText1`, `sourceType`, `suppressionState`, `durationIso`.

## Live observations (2026-11-13)

Against the running Unified PC runtime, after a Runtime Manager **stop/start**:

| Operation | Result |
|---|---|
| `activeAlarms` (minimal fields, with/without `systemNames`, `1=1`/`LIKE` filters) | `INTERNAL_SERVER_ERROR`, `message: "Generic error"`, `data.activeAlarms: null` |
| `activeAlarms` subscription | immediate `next` with the same error; no `complete` within 12 s |
| `loggedTagValues` (bare and qualified names) | `INTERNAL_SERVER_ERROR`, `data: null` |
| `loggedAlarms` (with/without systems, time range) | `INTERNAL_SERVER_ERROR`, `data: null` |
| `browse(objectTypeFilters:[ALARM])` | **works**: built-in system alarm definitions (disk, memory, delta activation, …) |
| `browse(objectTypeFilters:[ALARMCLASS])` | `INTERNAL_SERVER_ERROR` |
| `browse(objectTypeFilters:[LOGGINGTAG])` | `INTERNAL_SERVER_ERROR` |
| `acknowledgeAlarms` unknown name | `code 2`, "Cannot resolve provided name" |
| `resetAlarms` unknown name | `code 2` |
| `disableAlarms`/`enableAlarms`/`shelveAlarms`/`unshelveAlarms` unknown name | `code 2` |

The mutation path is therefore wired correctly; the live-alarm and history reads are not. The
client records the subscription error and reconnects with backoff instead of hiding it.

A previous probe coincided with a GraphQL server restart (PID changed); the M1 logger
reconnected after a 2.5 s gap. The alarm/history errors reproduce after a clean runtime restart
and are not explained by that one process reset.

## Runtime storage evidence

The runtime keeps per-project SQLite databases:

| Path | Tables | Current content |
|---|---|---|
| `%ProgramData%\SCADAProjects\HMI_RT_1\db\APERDB\HMI_RT_1_AlarmPersistencyDatabase.db3` | `PersistedAlarm`, `PersistedAlarmState`, `SPL_NAMEDPROPERTY`, `iseg_*` | 3 `PersistedAlarm` rows, all `Active=1`, fresh `LastCameSystemTime`; 3 state rows |
| `%ProgramData%\SCADAProjects\HMI_RT_1\db\TPERDB\HMI_RT_1_TagPersistencyDatabase.db3` | `PersistedTag`, `SPL_NAMEDPROPERTY`, `iseg_*` | `PersistedTag` is empty (no tag logging) |

`PersistedAlarm` columns: `ObjectID`, `ElementID`, `SubElementID`, `LastTime`, `LastCount`,
`EnableState`, `Active`, `LastCameSystemTime`, `LastCameManagerID`, `ModificationTime`,
`ChangeReason`, `SuppressionState`. Times are Windows FILETIME (100 ns since 1601).

`SPL_NAMEDPROPERTY` reports `AlarmPersistency` / `SQLite_DBRevision` `459014` (`0.7.1.6`) and
`TagPersistency` revision `0`.

So the alarm service **has active system alarms**, but the GraphQL alarm resolver still fails.
The history API has no logged tag rows to read, but it fails at the root rather than returning an
empty result.

## Why it is blocked

The project was created headlessly and contains tags and a screen, but no user alarm classes,
discrete alarms, alarm log, data log or logging tags. `browse(ALARM)` only returns the built-in
system alarms. `hmi_capabilities` reports Unified *read* support for alarms but no alarm/logging
engineering operation, and the TIA MCP change kinds do not include them
(`hmi_external_tag_create`, `hmi_screen_create`, `hmi_dynamization_create`,
`hmi_event_handler_create`, `hmi_text_list_import` only).

The Unified Openness assembly does expose the missing objects:

- `HmiSoftware.DiscreteAlarms.Create(name)` → `HmiDiscreteAlarm`
  (`RaisedStateTag`, `RaisedStateTagBitNumber`, `TriggerMode`, `AlarmClass`, `Priority`,
  `EventText`).
- `HmiSoftware.AlarmClasses.Create(name)` / existing `AlarmClasses`.
- `HmiSoftware.AlarmLogs.Create(name)` → `HmiAlarmLog`.
- `HmiSoftware.DataLogs.Create(name)` → `HmiDataLog`.
- `HmiTag.LoggingTags.Create(name)` → `HmiLoggingTag`
  (`Source`, `DataLog`, `LoggingMode` = `Cyclic`, `Cycle`, `CycleFactor`).

A custom Openness editor needs an entry in
`HKLM\SOFTWARE\Siemens\Automation\Openness\AllowList\<exe>` (path + file hash). That requires an
elevated token; the current session is not administrator, so the fixture cannot be created this
way yet. The existing TIA MCP worker is allow-listed but exposes no alarm/logging write tool.

The compiled HMI output is already visible in the TIA project at
`IM\HMI\I\0\Saved` (`DownloadTask.xml`, `UMCData.json`, the two control-asset zips, `general/`,
`screens/`, `system/`, `config/`). A no-op rebuild from that folder plus `Download.config` and the
machine certificate was imported successfully with
`SIMATICRuntimeManager.exe -s -overwriteUmc -c fulldownload <zip>`; login and `Tag01` read worked.
So after an Openness/GUI edit + compile, the package can be rebuilt **without** TIA's
*Download to file* (procedure in [03-lab-project.md](03-lab-project.md#rebuilding-a-runtime-package-without-download-to-file)).
The remaining blocker is creating the alarm/logging objects, not packaging them.

`%ProgramData%\SCADAProjects\HMI_RT_1\currentConfiguration\UMCData.json` shows `Lab` in
`HMI Administrator`, but the exported role only lists `UM_ADMIN`, `UM_IMPORT`, `UM_EXPORT`,
`UM_VIEW`, `UM_RESETPWD`. Whether the runtime's `ACAddOn` derives alarm rights from the role or
needs explicit function rights is unresolved. The GraphQL config enables
`AccessControlPluginPHMI` and the base `AccessControlAddOn` with `sysContext: ""`; tags work, so
the base plugin itself loads.

## Next actions

1. **Preferred:** add one discrete alarm on `Tag01` bit 0, one alarm log, one data log and
   `Tag01`/`Tag02` logging tags in a **copy** of the lab project, compile, rebuild the runtime
   `.zip` from `IM\HMI\I\0\Saved` (verified), import with `-overwriteUmc`, then rerun the commands
   above.
2. **Without Openness:** perform the same edits in the TIA Portal GUI, compile, then use the
   rebuilt-package procedure; *Download to file* is no longer required.
3. If the alarm API still returns `INTERNAL_SERVER_ERROR` with a configured alarm, capture the
   GraphQL server's stderr/trace and the `WCCILals`/`ACAddOn` behavior; the resolver's generic
   error currently hides the cause.
4. After a working alarm: verify `Added`/`Modified` notification reasons, acknowledge via
   `acknowledgeAlarms`, and check that `activeAlarms` reflects the state change; then record the
   round-trip in `examples/`.

## Acceptance status

| Criterion | Status |
|---|---|
| Alarm subscribe + acknowledge round-trip | blocked (no configured alarm; resolver errors) |
| `activeAlarms` query | blocked (`INTERNAL_SERVER_ERROR`) |
| `loggedTagValues` / `loggedAlarms` | blocked (no logging configuration; resolver errors) |
| Client handles reconnects and records errors | implemented and tested against the broken runtime |
| Mutation request/response shapes | verified with `code 2` for unknown names |
| Storage schema documented | done (above) |
