# Openness probe

Small Openness CLI for the download/PLCSIM experiments.

| Command | Purpose |
|---|---|
| `close` | close all open projects |
| `hold <project.ap21>` | open a project and keep it open until `opnshold.stop` appears next to it |
| `config <project.ap21>` | dump the `DownloadProvider` modes, PC interfaces, targets and addresses |
| `subnet <project.ap21>` | connect `PROFINET interface_1` node `X1` to `PROFINET_1` and set `192.168.0.1/24` |
| `download <project.ap21> <address>` | create a target address and call `Download(..., DownloadOptions.Software)` |

## Build

```powershell
.\probe\openness_probe\build.ps1
```

If TIA is installed outside `C:\Program Files\Siemens\Automation\Portal V21`,
pass `-PublicApi` and `-BinApi`.

## Examples

```powershell
# Inspect the PLCSIM target
.\probe\openness_probe\bin\OpennessProbe.exe config .\PlantB2-plcsim\PlantB.ap21

# Add a subnet and IP to a project copy
.\probe\openness_probe\bin\OpennessProbe.exe subnet .\PlantB2-plcsim\PlantB.ap21

# Try the Openness download
.\probe\openness_probe\bin\OpennessProbe.exe download .\PlantB2-plcsim\PlantB.ap21 192.168.0.1

# Close a project left open by a failed run
.\probe\openness_probe\bin\OpennessProbe.exe close
```

TIA Portal must already be running. The first call shows the **Openness access**
prompt; choose **Yes to all** for the lab.

## Measured result

`config` on the S7-1500 `PlantB2` copy reports:

```text
mode PN/IE pcInterfaces=1
  pc PLCSIM number=1 addresses=0 targets=2
    target 1 X1 addresses=0
    target 1 X2 addresses=0
```

`subnet` creates and connects `PROFINET_1`, but the target still reports
`addresses=0`. `download` creates a `192.168.0.1` address and fails with:

```text
An error has occured during download:
'Connect to module PLC_B failed.'
```

See [`docs/13-mc7-codegen.md`](../../docs/13-mc7-codegen.md) for the PLCSIM
V21 context. No Siemens binaries are committed.
