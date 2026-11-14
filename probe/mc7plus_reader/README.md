# MC7 plus reader probe

Test `MC7PlusBinaryReader` and `MC7plus.Disassembler` on S7-1500 plus-code
sections extracted from an objectframe XML export.

The tool loads Siemens assemblies from a local TIA Portal installation at
runtime; no Siemens binaries are committed.

## Build

```powershell
.\probe\mc7plus_reader\build.ps1 -TiaBin '<TIA_BIN>'
```

## Extract sections

```powershell
python .\probe\mc7plus_extract.py `
    --xml <PlantB2.objectframe.xml> `
    --out out\plus-sections
```

The extractor writes, per block:

- `<block>.open.bin` — `OpenImmediateData`
- `<block>.mc7plus.bin` — full `MC7PlusData`
- `<block>.sec0.bin`, `.sec1.bin`, `.sec124.bin` — `EF BE AD DE` sections

## Run

```powershell
.\probe\mc7plus_reader\bin\Mc7PlusReader.exe `
    --tia-bin '<TIA_BIN>' `
    --code out\plus-sections\FC_And.sec1.bin `
    --immediate out\plus-sections\FC_And.open.bin `
    --label 'FC_And sec1 + open'
```

## Measured result

The immediate table does **not** unlock the observed sections:

```text
FC_And sec1 + open:
  reader ERROR ApplicationException: Read not allowed type code
  disassembler OK instructions=0

FC_And full + open:
  reader ERROR ApplicationException: Instruction code has no match
  disassembler ERROR InvalidOperationException: Operation not found.
```

Other blocks fail differently (`IndexOutOfRangeException`,
`Destination array was not long enough`, `ArgumentOutOfRangeException`), which
suggests the section is not a plain instruction stream or the framing is
wrong.

## Next

- `GetMC7Plus(PlusBinaryType.Simulation)` / `VPlc`
- runtime hook of `Disassembler.DisassembleCallStatements`
- PLCSIM/CPU memory capture

See `obsidian/siemens-re-lab/03 Experiments/2026-11-14 Plus Reader.md`.
