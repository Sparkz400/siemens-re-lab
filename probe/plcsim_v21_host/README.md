# PLCSIM V21 host diagnostic

Power on an S7-1500 virtual PLC through the **PLCSIM V21 standard** public API,
set its IP address, and keep it alive for an external Openness download test.

This is a diagnostic, not a block download/upload tool. The public PLCSIM V21
API exposes runtime control (scan, process values, memory reset, power) but no
block transfer.

> This is **not** PLCSIM Advanced V8. The repo's goal 2 uses Advanced V8; this
> probe only documents the standard V21 behavior on this host.

## Build

```powershell
dotnet build .\probe\plcsim_v21_host\plcsimhost.csproj -c Release
```

The project references `Siemens.Simatic.PlcSim.VplcApi.dll`, `Google.Protobuf.dll`
and `Grpc.Core.Api.dll` from the local PLCSIM V21 installation. No Siemens
binaries are committed.

## Run

```powershell
.\probe\plcsim_v21_host\bin\Release\net10.0\plcsimhost.exe
```

The program:

1. loads `Siemens.Simatic.PlcSim.Vplc1500.dll` and the matching motion DLL,
2. calls `SimulationController.PowerOn(Family.S71500, ..., CycleTimeoutMode.IgnoreConfiguration, 0)`,
3. sets `192.168.0.1` on port 1 through the private `SetAddress_DEBUG_ONLY` path,
4. waits until `%TEMP%\plcsim-v21.stop` exists, then powers off.

Measured state after boot:

```text
powerOn=True
OperatingMode=Stop
IsBooting=False
IsPoweredOn=True
```

## Measured Openness result

With the vPLC running, the Openness download target `1 X1` still reports
`addresses=0`; `ApplyConfiguration` rejects a created address with
`The configuration provided is invalid`, and `Download(target, address, ...)`
fails with:

```text
An error has occured during download:
'Connect to module PLC_B failed.'
```

The API-created vPLC is not reachable through TIA's `PLCSIM` PC interface in
this setup. The TIA UI **Start simulation** path creates the instance TIA can
talk to; that is the remaining read-back route. See
[`docs/13-mc7-codegen.md`](../../docs/13-mc7-codegen.md).

## Cleanup

Create the stop file and wait a few seconds:

```powershell
New-Item -ItemType File "$env:TEMP\plcsim-v21.stop" -Force | Out-Null
```
