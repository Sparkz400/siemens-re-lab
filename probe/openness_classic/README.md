# Openness classic project

Create and compile the classic S7-300 `PlantE` lab project through TIA
Openness. This is the reproducible source of the eight SCL-to-MC7 pairs in
[`docs/13-mc7-codegen.md`](../../docs/13-mc7-codegen.md).

## Prerequisites

- TIA Portal V21 running (Openness V21 installed).
- The project root must be writable. The program deletes an existing
  `<root>\<name>` directory before creating the project.
- The first Openness call shows the **Openness access** prompt. Choose
  **Yes to all** for the lab.

## Build

```powershell
.\probe\openness_classic\build.ps1
```

If TIA is installed outside `C:\Program Files\Siemens\Automation\Portal V21`,
pass `-PublicApi` and `-BinApi`.

## Run

```powershell
$root = 'C:\lab\projects'
New-Item -ItemType Directory -Force $root | Out-Null
.\probe\openness_classic\bin\OpennessClassic.exe `
    $root PlantE `
    .\probe\openness_classic\PlantE.scl
```

Expected output:

```text
processes=1
created PlantE
plc=PLC_PlantE
tags=4
blocks=9
  Main OB number=1
  FC_LogicB FC number=1
  FC_And FC number=2
  ...
compile state=Success errors=0 warnings=0
saved
closed
```

## Next step

Export the project to objectframe XML with the harness's `opns fs2xml` step
(or the TIA MCP equivalent), then run:

```powershell
python .\probe\mc7_classic_extract.py --xml .\PlantE.objectframe.xml --out .\out\classic-blocks
.\probe\mc7_classic_host\build.ps1 -TiaBin '<TIA_BIN>'
.\probe\mc7_classic_host\bin\Mc7ClassicHost.exe --tia-bin '<TIA_BIN>' `
    --data .\out\classic-blocks\FC_And.bin --start 0 --length 26
```

No Siemens binaries or project archives are committed.
