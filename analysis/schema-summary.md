# WinCC Unified GraphQL schema (runtime 21.0)

Types: 138 | Queries: 8 | Mutations: 11 | Subscriptions: 3

## Queries
- `session(allSessions: Boolean)` -> `[Session]`  
  Returns information about the current session. If the allSessions parameter is set to True, all sessions of the current user will be returned.
- `nonce()` -> `Nonce`  
  Returns a nonce that can be used with e.g. the UMC SWAC login method. 
- `identityProviderURL()` -> `String`  
  Returns the URL of the identity provider, which can be used for the UMC SWAC login. A client application implementing SWAC login
- `tagValues(names: [String!]!, directRead: Boolean)` -> `[TagValueResult]`  
  Queries tags, based on the provided names list. If directRead is true, values are taken directly from PLC.
- `browse(nameFilters: [String], objectTypeFilters: [ObjectTypesEnum], baseTypeFilters: [String], language: String)` -> `[BrowseTagsResult]`  
  Queries tags, elements, types, alarms, logging tags and basically anything that has a configured name, based on the provided filter criteria. Each filter parameter can be an array, and the logical relation between each item
- `loggedTagValues(names: [String]!, startTime: Timestamp, endTime: Timestamp, maxNumberOfValues: Int, sortingMode: LoggedTagValuesSortingModeEnum, boundingValuesMode: LoggedTagValuesBoundingModeEnum)` -> `[LoggedTagValuesResult]`  
  Queries logged tag values from the database. Names is a mandatory parameter, each name in this list must either be 
- `activeAlarms(systemNames: [String], filterString: String, filterLanguage: String, languages: [String])` -> `[ActiveAlarm]`  
  Query active alarms from the provided systems. 
- `loggedAlarms(systemNames: [String], filterString: String, filterLanguage: String, languages: [String], startTime: Timestamp, endTime: Timestamp, maxNumberOfResults: Int)` -> `[LoggedAlarm]`  
  Query logged alarms from the storage system. 

## Mutations
- `login(username: String!, password: String!)` -> `Session`  
  Logs a user in based on their username and password. The session token in the resulting session object have to be sent along all subsequent queries, mutations and subscriptions
- `loginSWAC(claim: String!, signedClaim: String!)` -> `Session`  
  Logs a user in based on the claim and signed claim that they received from the UMC SWAC authentication. The session token in the resulting Session object have to be sent along 
- `extendSession()` -> `Session`  
  Extends the user's current session expiry. The expiry gets increased by the 'session expires' value received by the identity provider (UMC).
- `logout(allSessions: Boolean)` -> `Boolean`  
  Logs out the current user. If the allSessions parameter is set to True, all sessions of the current user will be terminated, otherwise only the current one.
- `writeTagValues(input: [TagValueInput]!, timestamp: Timestamp, quality: QualityInput)` -> `[WriteTagValuesResult]`  
  Updates tags, based on the provided TagValueInput list.
- `acknowledgeAlarms(input: [AlarmIdentifierInput]!)` -> `[ActiveAlarmMutationResult]`  
  Acknowledge one or more alarms. 
- `resetAlarms(input: [AlarmIdentifierInput]!)` -> `[ActiveAlarmMutationResult]`  
  Reset one or more alarms. 
- `disableAlarms(names: [String]!)` -> `[AlarmMutationResult]`  
  Disable the creation of new alarm instances for one or more alarms. 
- `enableAlarms(names: [String]!)` -> `[AlarmMutationResult]`  
  Enable the creation of new alarm instances for one or more alarms. 
- `shelveAlarms(names: [String]!, shelveTimeout: Timespan)` -> `[AlarmMutationResult]`  
  Shelve all active alarm instances of the provided configured alarms. New/updated alarm instances for this configured alarm will
- `unshelveAlarms(names: [String]!)` -> `[AlarmMutationResult]`  
  Revert the Shelve action for the provided configured alarms. Unshelving causes a notification for all concerned alarm instances.

## Subscriptions
- `tagValues(names: [String!]!)` -> `TagValueNotification`  
  Subscribes to tag values for the tags based on the provided names list. Notifications will contain a notification reason, that informs about the type of the notification:
- `activeAlarms(systemNames: [String], filterString: String, filterLanguage: String, languages: [String])` -> `ActiveAlarmNotification`  
  Subscribe for active alarms matching the given filters. Notifications will contain a notification reason, that informs about the type of the notification:
- `reduState()` -> `ReduStateNotification`  
  Subscribes to redu state. Notifications will contain information about the active/passive state of the given system and will come on a state change.

## Tag value model
### TagValueResult
- `name`: `String`
- `value`: `Value`
- `error`: `Error`

### Value
- `value`: `Variant`
- `timestamp`: `Timestamp`
- `quality`: `Quality`

### Quality
- `quality`: `MainQuality`
- `subStatus`: `QualitySubStatus`
- `limit`: `QualityLimit`
- `extendedSubStatus`: `QualityExtendedSubStatus`
- `sourceQuality`: `Boolean`
- `sourceTime`: `Boolean`
- `timeCorrected`: `Boolean`

### TagValueNotification
- `name`: `String`
- `value`: `Value`
- `error`: `Error`
- `notificationReason`: `String`

### BrowseTagsResult
- `name`: `String`
- `displayName`: `String`
- `objectType`: `String`
- `dataType`: `String`
- `tagTypeName`: `String`

## Auth
- `login(username, password)` -> `Session.token`; send `Authorization: Bearer <token>`.
- `loginSWAC(claim, signedClaim)` uses `identityProviderURL` + `nonce`.
- Runtime UMC database currently has **no users**; login returns code 102 / UMC 273.
