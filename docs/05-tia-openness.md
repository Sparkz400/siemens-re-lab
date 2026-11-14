# TIA Openness / TIA MCP

## What it is

[TIA MCP](https://github.com/Sparkz400/TIA_MCP) is a Windows MCP server for TIA Portal V21:
a .NET 10 host talks to isolated .NET Framework 4.8 Openness workers over named pipes. The
installed build is **v0.9.0** with **314 MCP tools**, plus a PLCSIM Advanced V8 simulator
worker.

| Item | Value |
|---|---|
| Source | `Sparkz400/TIA_MCP` (public) |
| Installed bundle | `C:\TiaMcp\releases\tia-mcp-v21-0.9.0_20261112_122441_bb02d6cf` |
| Source checkout | `C:\Programs\04_Programme\src\TIA_MCP` |
| MCP server name | `tia` (OpenCode) |
| Worker | `workers\openness\TiaMcp.Openness.V21.exe` |
| Simulator worker | `workers\simulator\TiaMcp.Simulator.V8.exe` |

The host exposes project inventory, block/type/tag editing, HMI inspection and changes,
PLCSIM control, OPC UA, translations, diagnostics and more. Feature availability is described
per tool; call `feature_catalog`, `target_capabilities` or `object_resolve` before relying on a
feature.

## Build notes (reproducible on this machine)

Requirements: TIA V21 with Openness, .NET SDK 10, TIA V21 PublicAPI, .NET Framework 4.8
reference assemblies.

```powershell
# net48 reference assemblies without the Developer Pack
# (Microsoft.NETFramework.ReferenceAssemblies.net48 extracted locally)
$env:FrameworkPathOverride = '...\net48refs\build\.NETFramework\v4.8'

# isolated Release build + worker lifecycle check
.\scripts\New-IsolatedBuild.ps1 -Configuration Release

# package + strict manifest verification + zip + mcp.local.json
.\scripts\Package.ps1 -Configuration Release -SkipBuild -IsolatedBuildDirectory <build-root>

# register the worker in the Siemens Openness AllowList (elevated, 64-bit)
.\scripts\Register-TiaOpennessApplication.ps1 -BundlePath <bundle> -ExpectedWorkerSha256 <sha256> -ReportPath <json-outside-bundle>
```

The Openness allow-list lives at
`HKLM\SOFTWARE\Siemens\Automation\Openness\AllowList\TiaMcp.Openness.V21.exe`; each worker
path/hash gets its own `Entry` subkey. TIA checks the hash, so a rebuilt worker needs a new
registration.

## Local patches in this lab

The upstream v0.9.0 assumes the **TIA Test Suite Advanced Openness API**
(`Siemens.Engineering.TestSuite.dll`) is installed. It is licensed here but the DLL is not.
Local changes (kept in the checkout, not upstream):

1. `src/TiaMcp.Openness.V21/TiaMcp.Openness.V21.csproj`
   - Test Suite project reference and `OpennessSession.TestSuite*.cs` gated on
     `TestSuiteQualification`.
2. `src/TiaMcp.TestSuite.Stub/`
   - signature-only stub in namespace `TiaMcp.TestSuite.V21`; Test Suite tools fail closed with
     `NotSupportedException` instead of breaking the worker.
3. `TiaMcp.slnx`
   - Test Suite worker project and its worker-test project excluded from local builds.

A second patch fixes HMI station creation in `OpennessSession.Changes.cs`:
`Devices.CreateWithItem` requires a **null station name** for a Unified PC/HMI device
(`CreateWithItem(type, null, itemName)`); the tool required a station name and passed the fields
swapped. The patch allows an omitted/blank `itemName` and passes `null`. Example working
payload:

```json
{ "typeIdentifier": "OrderNumber:6AV2 155-xxxxx-xxxx/21.0.0.0",
  "deviceName": "HmiDevice_A" }
```

Result: `HmiDevice_A` / `HMI_RT_1`, profile `unified-pc`.

## Tooling relevant to the four goals

| Goal | Tools / change kinds |
|---|---|
| 1. snapshot/diff | `project_inventory`, `project_index`, `project_snapshot_capture`, `project_snapshot_import`, `project_model`, `project_model_capture`, `project_model_details`, `project_text_export`, `project_copy` |
| 2. S7comm-plus | not a TIA task; use `sim_create`, `sim_select`, `simulation_download`, `sim_tags`, `sim_read`, `sim_write` for PLCSIM-side comparison |
| 3. Unified extractor | `hmi_inspect`, `hmi_capabilities`, `hmi_references`, `hmi_compile`, `change_prepare/apply` with `hmi_*` kinds; `runtime_web_*`, `runtime_ua_*` for other runtime surfaces |
| 4. panel backups | none — ProSave/panel firmware is outside Openness |

Qualified Unified PC change kinds (v0.9.0): `hmi_external_tag_create`, `hmi_screen_create`,
`hmi_dynamization_create`, `hmi_event_handler_create`, `hmi_text_list_import`. They are
create-only and pass through the HMI preservation/compile gate.

## Editor-only limits

- **G-004:** no Openness API for the Unified encrypted-transfer password. A headless-created
  Unified PC station cannot compile until an operator sets the password and start screen in the
  editor (or a configured template is imported).
- MC7 plus-code decryption and PLCSIM V21 block read-back limits are documented in
  [13-mc7-codegen.md](13-mc7-codegen.md).
- The classic project creation and the subnet/download probes are in
  [`probe/openness_classic`](../probe/openness_classic) and
  [`probe/openness_probe`](../probe/openness_probe).
- The plus-code reader and TIA assembly inventory are in
  [`probe/mc7plus_reader`](../probe/mc7plus_reader) and
  [`probe/tia_inventory.ps1`](../probe/tia_inventory.ps1); see
  [16-decompilation-roadmap.md](16-decompilation-roadmap.md).
- Some HMI exports are unavailable (no `Screen.Export`), so preservation is compared through
  typed model readback and paired exports.
- Test Suite operations are unavailable in this build (stub).

## Useful workflows

```text
project_inventory               list devices/blocks/tags
hmi_inspect section=devices     HMI identity/profile/paths
hmi_inspect section=model       screens, items, tags, alarms, runtime settings
hmi_compile                     checkpointed compile of one HMI target
change_prepare/change_apply     all edits; provide checkpointSave=true
runtime_web_connect             connect to the Unified web/GraphQL surface
```

Evidence, checkpoints and journals live under `C:\Programs\04_Programme\lab\tia-mcp-artifacts`.
Do not commit them: they contain project data and DPAPI credential metadata.
