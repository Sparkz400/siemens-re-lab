# MC7 classic host

Decode a classic S7-300 MC7 code blob with the TIA Portal
`Siemens.Simatic.Lang.CodeGenClassic.DOL.AwlRegenerator` API.

The host is a small .NET Framework 4.x executable. It loads Siemens assemblies
from a local TIA Portal installation at runtime and prints one JSON object with
the decoded statements. No Siemens binaries are committed to this repository.

## Prerequisites

- Windows with TIA Portal V18-V21 installed (tested with V21, build
  `2100.0.121.1`).
- .NET Framework 4.x `csc.exe` at
  `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`.
- A classic S7-300 project export (objectframe XML) and the matching TIA
  installation.

## Build

```powershell
.\probe\mc7_classic_host\build.ps1 -TiaBin 'C:\Program Files\Siemens\Automation\Portal V21\Bin'
```

Or set `TIA_BIN` and omit `-TiaBin`.

## Extract the code blobs

The objectframe export is produced by the Openness `fs2xml` step (or a TIA MCP
equivalent). Classic S7-300 blocks use
`Siemens.Simatic.Lang.Model.Blocks.LoadableCodeBlockData`; the executable code
is `BinaryData[78 : 78 + EndSAC + 1]`, where `EndSAC` comes from
`BlockDebugInformation`.

```powershell
python .\probe\mc7_classic_extract.py `
    --xml .\PlantE.objectframe.xml `
    --out .\out\classic-blocks
```

The script writes `block_<n>.bin` and `manifest.json`. Each manifest row has
the binary length, `EndSAC`, code length and code SHA-256. Blocks with an
invalid or truncated range are reported with `codeLength: 0` instead of
inventing code.

## Decode

```powershell
.\probe\mc7_classic_host\bin\Mc7ClassicHost.exe `
    --tia-bin 'C:\Program Files\Siemens\Automation\Portal V21\Bin' `
    --data .\out\classic-blocks\FC_And.bin `
    --start 0 --length 26
```

Example output (abridged):

```json
{
  "ok": true,
  "codeHex": "681d682c41600000fb800002fb800004fb92000600600000682c",
  "codeLength": 26,
  "statements": [
    {"address": 0, "length": 2, "operator": "SET", "access": "UNDEF", "range": "None", "width": "None", "byteNr": 0, "bitNr": 0, "const": 0},
    {"address": 2, "length": 2, "operator": "SAVE", "access": "UNDEF", "range": "None", "width": "None", "byteNr": 0, "bitNr": 0, "const": 0},
    {"address": 4, "length": 4, "operator": "Assign", "access": "DIRECT", "range": "Local", "width": "Bit", "byteNr": 0, "bitNr": 0, "const": 0},
    {"address": 8, "length": 4, "operator": "A", "access": "PARA_D", "range": "Extern", "width": "Bit", "byteNr": 2, "bitNr": 0, "const": 2},
    {"address": 12, "length": 4, "operator": "A", "access": "PARA_D", "range": "Extern", "width": "Bit", "byteNr": 4, "bitNr": 0, "const": 4},
    {"address": 16, "length": 4, "operator": "Assign", "access": "PARA_D", "range": "Extern", "width": "Bit", "byteNr": 6, "bitNr": 0, "const": 6}
  ]
}
```

The exact operator and operand values depend on the SCL source and the TIA
version. The host is a decoder, not a symbolic decompiler.

## Notes

- Classic S7-300 code is stored unencrypted in `BinaryData`; this path does
  not work for S7-1500 plus blocks, whose `MC7PlusData` is encrypted at rest.
- The host needs the licensed TIA installation. Do not redistribute the
  Siemens assemblies.
- See `docs/13-mc7-codegen.md` for the measured classic and plus-code results.
