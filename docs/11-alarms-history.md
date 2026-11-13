# M2 — alarms and history

Status: **history, logging and alarm control work; the `activeAlarms` query/subscription is
still broken on this runtime.**

The lab runtime now contains a real alarm and logging configuration. `loggedTagValues`,
`loggedAlarms` and all alarm mutations were verified live. The only missing piece is the
`activeAlarms` root resolver, which returns `INTERNAL_SERVER_ERROR` even while an alarm is
active. `probe/alarm_history_watch.py` provides a working live alarm feed by polling
`loggedAlarms`.

## Tools

| Tool | Purpose |
|---|---|
| `probe/unified_alarms.py` | `activeAlarms` query (blocked), acknowledge/reset/enable/disable/shelve/unshelve |
| `probe/alarm_history_watch.py` | poll `loggedAlarms` and store new transitions in SQLite + CSV |
| `probe/unified_history.py` | `loggedTagValues` and `loggedAlarms` time-range queries with optional CSV |
| `probe/tests/test_alarm_store.py` | alarm SQLite/CSV sink unit tests (no runtime required) |

Credentials come from `--user`/`--password`, `WINCC_USER`/`WINCC_PASSWORD`, or a prompt.

```powershell
$env:WINCC_USER = 'Lab'
$env:WINCC_PASSWORD = '...'
$py = "$env:LOCALAPPDATA\Programs\Python\Python312\python.exe"

# working live alarm feed (history poller)
& $py probe/alarm_history_watch.py --duration 3600 --interval 1 `
    --sqlite data/alarm_history.sqlite --csv data/alarm_history.csv

# history queries
& $py probe/unified_history.py tags --names 'Tag01:Archivvariable_1' --max 100
& $py probe/unified_history.py alarms --system HMI_RT_1 --max 100 `
    --start 2026-11-13T06:40:00Z --end 2026-11-13T08:10:00Z

# alarm actions (use the canonical name from browse(ALARM))
& $py probe/unified_alarms.py ack      --name HMI_RT_1::Tag00:Alarm_Tag01 --instance 1
& $py probe/unified_alarms.py reset    --name HMI_RT_1::Tag00:Alarm_Tag01 --instance 0
& $py probe/unified_alarms.py disable  --name HMI_RT_1::Tag00:Alarm_Tag01
& $py probe/unified_alarms.py enable   --name HMI_RT_1::Tag00:Alarm_Tag01
& $py probe/unified_alarms.py shelve   --name HMI_RT_1::Tag00:Alarm_Tag01 --timeout 60
& $py probe/unified_alarms.py unshelve --name HMI_RT_1::Tag00:Alarm_Tag01
```

The canonical alarm name is the **browse name**, including the trigger-tag prefix:
`HMI_RT_1::Tag00:Alarm_Tag01`. `loggedAlarms` returns the short name `Alarm_Tag01`; that short
form is **not** accepted by the mutation input. `shelveTimeout` is a JSON number, not a string;
`--timeout 60` is sent as `60`.

## Live fixture

The alarm/logging fixture was created in a copy of the lab project and downloaded on 2026-11-13
07:41:

| Object | Name |
|---|---|
| Trigger tag | `Tag00` (Bool), alarm on bit 0 |
| Discrete alarm | `HMI_RT_1::Tag00:Alarm_Tag01`, class `Alarm`, priority 12 |
| Logging tags | `HMI_RT_1::Tag00:Archivvariable_1` … `Tag04:Archivvariable_1` |
| Data log | `DataLog_1` (SQLite/default storage) |
| Alarm log | `AlarmLog_1` |

The rebuilt package (`... - Full 2026-11-13 - 07.41.19.zip`, 36 entries) added
`TagLogging.rdf`, `AlarmLogging.rdf`, `LogStorageConfiguration.rdf`, `backupStrategies.rdf`,
`config_TagLogging.rdf` and `reporting.rdf`. It was imported with `-overwriteUmc`; login and
`Tag01` read worked.

## Live observations (2026-11-13)

| Operation | Result |
|---|---|
| `activeAlarms` query | `INTERNAL_SERVER_ERROR`, `message: "Generic error"`, even with a raised alarm |
| `activeAlarms` subscription | immediate `next` with the same error; no `complete` within 12 s |
| `loggedTagValues` | **works**: `Tag01:Archivvariable_1` values at ~1 Hz after writes, quality `GOOD_CASCADE` |
| `loggedAlarms` | **works**: returns the transition history (`RAISED`, `RAISED_ACKNOWLEDGED`, `NORMAL`, `RAISED_CLEARED`, `REMOVED`, `SHELVED`) |
| `acknowledgeAlarms` canonical name, instance 1 | `code 0`; history shows `RAISED_ACKNOWLEDGED` and an acknowledgment time |
| `resetAlarms` canonical name | `code 0`; the clear transition is visible in history |
| `disableAlarms` / `enableAlarms` | `code 0` |
| `shelveAlarms --timeout 60` | `code 0`; history shows `suppressionState: SHELVED` |
| `unshelveAlarms` | `code 0`; history returns to `UNSUPPRESSED` |
| `shelveAlarms` with a string timeout | `Argument error`; with `0`/omitted, `code 1` (“Shelving the alarm failed”) |
| `browse(ALARM)` | returns system alarms and `HMI_RT_1::Tag00:Alarm_Tag01` |
| `browse(LOGGINGTAG)` | returns `HMI_RT_1::Tag0x:Archivvariable_1` |
| `browse(ALARMCLASS)` | returns the default classes and `LabAlarm` |

Timeline:

- `06:43:07Z` `Tag00=true` → alarm `RAISED`
- `06:43:33Z` acknowledge → `RAISED_ACKNOWLEDGED`
- `06:43:57Z` `Tag00=false` → `NORMAL` / `RAISED_CLEARED` / `REMOVED`
- `06:44:26–06:44:34Z` `Tag01` demo writer → 1 Hz values in `Tag01:Archivvariable_1`
- `06:45:51Z` raise + shelve → `RAISED` with `suppressionState: SHELVED`; unshelve → `UNSUPPRESSED`
- `06:48:03–06:48:10Z` history watcher captured clear, raise and acknowledge transitions

The history watcher stored 10 existing transitions in a 5 s lookback run, then 4 live
transitions during a 14 s clear/raise/ack sequence.

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

`ActiveAlarm`/`ActiveAlarmNotification` add `notificationReason` on the notification type only.
`LoggedAlarm` lacks `userResponse` (a validation error if selected).

## Storage

The runtime keeps per-project SQLite databases:

| Path | Tables | Content |
|---|---|---|
| `%ProgramData%\SCADAProjects\HMI_RT_1\db\APERDB\...AlarmPersistencyDatabase.db3` | `PersistedAlarm`, `PersistedAlarmState`, `SPL_NAMEDPROPERTY`, `iseg_*` | persisted alarm and state rows |
| `%ProgramData%\SCADAProjects\HMI_RT_1\db\TPERDB\...TagPersistencyDatabase.db3` | `PersistedTag`, `SPL_NAMEDPROPERTY`, `iseg_*` | persisted tag values |
| Data log storage | SQLite database configured by `DataLog_1` | logged tag values |
| Alarm log storage | SQLite database configured by `AlarmLog_1` | logged alarm transitions |

`PersistedAlarm` columns: `ObjectID`, `ElementID`, `SubElementID`, `LastTime`, `LastCount`,
`EnableState`, `Active`, `LastCameSystemTime`, `LastCameManagerID`, `ModificationTime`,
`ChangeReason`, `SuppressionState`. Times are Windows FILETIME (100 ns since 1601).
`SPL_NAMEDPROPERTY` reports the alarm/tag persistency database revisions.

## Remaining blocker

`activeAlarms` and its subscription fail at the root resolver even when an alarm is raised,
acknowledged or shelved. The failure is not access control: history queries and all mutations
succeed with the same user. It is not a missing alarm configuration: the alarm is visible through
`browse(ALARM)` and `loggedAlarms`, and the runtime has active system alarms.

The practical workaround is `probe/alarm_history_watch.py`, which polls `loggedAlarms` and stores
new transitions in SQLite/CSV. The next step for the direct subscription is to capture the
GraphQL server's stderr/trace or inspect the SEA alarm module; the resolver currently discards
the underlying error as “Generic error”.

## Acceptance status

| Criterion | Status |
|---|---|
| Alarm history subscribe-equivalent + acknowledge round-trip | **working** via `loggedAlarms` polling + `acknowledgeAlarms` |
| `activeAlarms` query/subscription | blocked (`INTERNAL_SERVER_ERROR`) |
| `loggedTagValues` | **working** |
| `loggedAlarms` | **working** |
| enable/disable/reset/shelve/unshelve | **working** with canonical names; shelve needs a numeric timeout and an active instance |
| Client reconnect and error recording | implemented (tag logger and alarm subscription logger) |
| Storage documented | done (above) |
