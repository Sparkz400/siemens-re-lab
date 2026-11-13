# Lab project and runtime

## TIA project

| Item | Value |
|---|---|
| Project | `TiaMcpScratch_20261112_123345_9eb052ce` |
| Project file | `...\TiaMcpScratch_20261112_123345_9eb052ce.ap21` |
| PLC | CPU 1511-1 PN V4.0, station `ScratchPLC`, rack `Profilschiene_0` |
| HMI | `PC-System_1` / `HMI_RT_1`, WinCC Unified PC RT 21.0.0.0 |
| HMI profile | `unified-pc` |
| Screen | `Overview_A`, 1920×1080, set as start screen |
| Tags | `Tag01` … `Tag20`, Real, internal HMI tags |
| Runtime user | `Lab`, group **HMI Administrator**, local user management |
| Runtime package | TIA **Download to file** → `HMI_RT_1[...] - Full <date>.zip` |
| Runtime endpoint | `http://localhost:4000/graphql` |

The project was created with TIA MCP (`tia_create_scratch`, CPU 1511-1 PN V4.0), then edited in
TIA Portal to add the Unified PC station, screen, transfer password and UMC user. The PLC-only
base can be recreated; the editor-only steps are listed below.

## Compile state

`hmi_compile` on the Unified target:

```json
{"errorCount":0,"warningCount":0,"scope":"Unified: the owning device's hardware and software.",
 "compilerSucceeded":true,"targets":[{"target":".../PC-System_1","state":"Success"}]}
```

## Editor-only prerequisites (G-004)

A Unified PC station created through Openness does **not** compile until these are set in the
TIA editor:

1. A screen exists and is configured as the **start screen**.
2. **Runtime settings → General → Encrypted transfer**: matching password + confirmation that
   satisfies the password policy.

There is no public Openness API for the transfer password. Siemens documents this gap in the
TIA MCP repo (`docs/reference/openness-gaps.md`, G-004). The lab project has both configured;
do not commit the password.

## Runtime package and UMC

TIA **Download to device → Download to file** writes a runtime `.zip` (contains
`Download.config`, `UMCData.json`, `general/`, `screens/`, `system/`). This is the only archive
format the Runtime Manager accepts; an ES `.zap21` is rejected with
“does not contain a Runtime project”.

Runtime Manager CLI (console mode, `-s` is required or it opens the GUI):

```powershell
$rm = 'C:\Program Files\Siemens\Automation\WinCCUnified\bin\SIMATICRuntimeManager.exe'

# list projects and IDs
& $rm -s -o projectlist.txt -c projectlist ALL

# full download with UMC data applied
& $rm -s -o download.log -overwriteUmc -c fulldownload "<runtime>.zip"

# start / stop / remove
& $rm -s -o start.log -c start 1b9e76d7-4eac-4040-bebb-a8189f736d37
& $rm -s -o stop.log  -c stop  1b9e76d7-4eac-4040-bebb-a8189f736d37
& $rm -s -o remove.log -c remove 1b9e76d7-4eac-4040-bebb-a8189f736d37
```

**Critical:** importing/updating the package in the Runtime Manager's default keep mode renames
the user data to `UMCData_disabled.json`; local UMC then has no users and GraphQL login returns
UMC 273. A `fulldownload` with **`-overwriteUmc`** installs `UMCData.json` correctly. Deleting
the runtime project and re-adding it in the GUI is not sufficient — the GUI import uses keep
semantics.

After the overwrite, `C:\ProgramData\SCADAProjects\HMI_RT_1\currentConfiguration\UMCData.json`
exists (not `_disabled.json`), and login succeeds.

## Rebuilding a runtime package without Download to file

The TIA project keeps the compiled HMI output under
`<project>\IM\HMI\I\0\Saved\`. Its `DownloadTask.xml` lists the files TIA packages. A no-op
rebuild was verified on 2026-11-13:

1. Read the `FullDownload` entries from the shipped package's `DownloadTask.xml`.
2. Take each file from `Saved\`; take `config\CertStore\own\private\PW.0.cert.pem` from the
   current runtime (or the shipped package).
3. Add the package's `Download.config` and `DownloadTask.xml`; store paths with forward slashes.
4. `-c fulldownload -overwriteUmc <zip>`, then `-c start <project-id>`.

The 30-entry, 3,669,931-byte rebuild imported successfully, and login plus `Tag01` read worked.
This is a lab procedure, not a supported Siemens workflow. Rebuild only after a successful HMI
compile, and add any new output files (for example alarm/logging RDFs) to the manifest; a missing
manifest entry makes the import fail.

`probe/rebuild_runtime_package.py` automates the procedure:

```powershell
& $py probe/rebuild_runtime_package.py `
    --saved "...\<project>\IM\HMI\I\0\Saved" `
    --reference "$env:USERPROFILE\Documents\Simatic.HMI\RT_Projects\<last package>.zip" `
    --out artifacts\runtime-m2.zip
```

It reads the project manifest, adds `Download.config` and the machine certificate from the
reference package, and writes a flat runtime `.zip`. The tool output was also imported and
started successfully.

## Runtime layout

```text
C:\ProgramData\SCADAProjects\HMI_RT_1\
  currentConfiguration\      screens, general, system, UMCData.json
  config\                    config, config.level, certstore
  graphqlSessions\           session database (local)
  log\                       runtime logs (empty on this install)
```

The GraphQL server is `C:\Program Files\Siemens\Automation\WinCCUnified\bin\WCCILgraphQLServer.exe`
(Node.js SEA); its global config is
`C:\Program Files\Siemens\Automation\WinCCUnified\config\config.yaml` (port 4000, introspection,
CAPI/SWAC auth plugins). Leave `umcDomain` at the installed value (`UMC4linux_domain`); the
project’s UMC data uses the same domain.

## Recreating the rig

1. Create a scratch project with CPU 1511-1 PN V4.0 (TIA MCP `tia_create_scratch`).
2. Add the Unified PC RT station in TIA or via `hardware_add` (item name `HmiDevice_A`,
   auto station name; the local MCP build has the station-name fix).
3. Add a screen, set it as start screen, set the encrypted-transfer password.
4. Add tags and the local UMC user `Lab` in **Runtime settings → User management**.
5. Compile, **Download to file**, then import with `-overwriteUmc` and start.
6. Run `probe/unified_probe.py` against `http://localhost:4000/graphql`.
