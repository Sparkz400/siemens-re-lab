# tia-project

File-level reader/exporter for TIA Portal V21 projects. It reads the project
directory directly; the TIA GUI is not used.

Implemented tracks:

1. **PEData.plf parser** — 98-byte `FileHeader`, 44-byte `ObjectHeader`,
   SHA-256 object hashes, full record scan, and `PEData.idx` live-object
   resolution (last entry per object id wins; deleted/destroyed entries are
   skipped).
2. **XRef graph decoder** — SQLite `parts`/`objs`/`rels`/`addrs`, 14-byte
   `"OG" + int32 TypeId + int64 InstanceId` object ids, PLF join, DOT export,
   impact traversal, and readable relation semantics.
3. **Openness/SimaticML extraction** — embedded XSD extraction
   (`Step7OpennessBody`, `SW.PlcBlocks.*`, `Step7Internal`, `SW.Common`,
   `SW.InterfaceSections`, `Values`, ObjectFrame metadata), Openness API index,
   decrypted FS2XML object export, block/source extraction, and SimaticML
   block generation from the internal interface payload + body AST.
4. **HMI model export** — restores the project's `CS_*.bak` into
   `MSSQL$WINCC` as a temporary database, exports every populated table as
   JSON, resolves text ids through `TXTTable`, exports HMI objects/screens/
   tags from PLF, and renders screens to SVG.
5. **MC7+ disassembly** — extracts `OpenImmediateData` / `MC7PlusData` from
   compiled block objects and calls the installed `MC7plus.Disassembler`
   through a .NET Framework host.
6. **Hardware/network topology** — devices, racks, slots, order numbers,
   nodes, address managers, DOT/Markdown inventory.
7. **History + domain exports** — savepoint timeline from IDX commit points,
   safety/UMAC/drives object exports, and VCI file-hash verification.

The 30-idea platform layer (semantic change control, ontology/API/IR, proof and
provenance, corpus economics, evolutionary engineering, deployment supply
chain) is documented in [`PLATFORM.md`](PLATFORM.md) and runs end-to-end with:

```powershell
tia-project platform run --project <dir> --fs2xml <project.objectframe.xml> --out out\platform
```

## Build

```powershell
dotnet build .\TiaProject.csproj -c Release
```

The executable is `bin\Release\net10.0\tia-project.exe`.

The MC7 host is prebuilt at `tools\Mc7Host\bin\Mc7Host.exe`; if it is missing,
the CLI compiles it on demand with the .NET Framework `csc.exe`.

## Commands

```powershell
# Project summary
tia-project info C:\path\to\Project

# PLF records, live/history split, payload strings/XML
tia-project plf C:\path\to\Project --json out\plf.json
tia-project plf C:\path\to\Project --strings --xml out\payload-xml

# Enrich PLF objects with class/name/attributes from a decrypted FS2XML export
tia-project plf C:\path\to\Project --fs2xml out\project.objectframe.xml --json out\plf-enriched.json

# XRef graph, DOT, impact, relation semantics
tia-project xref C:\path\to\Project --json out\xref.json --dot out\xref.dot
tia-project xref C:\path\to\Project --impact "1052700/1411"

# VCI tables / integrity verification
tia-project vci C:\path\to\Project --json out\vci.json
tia-project vci verify C:\path\to\Project --json out\vci-verify.json

# SimaticML/Openness schemas and API surface
tia-project opns schemas --bin "<TIA_BIN>" --out out\schemas
tia-project opns api --bin "<TIA_BIN>" --out out\opns-api.json

# Full project XML: FS2XML export with the deterministic default key + Deflate
tia-project opns fs2xml C:\path\to\Project --out out\project.objectframe.xml

# Block inventory, source XML, SimaticML documents
tia-project opns blocks out\project.objectframe.xml --out out\blocks
tia-project opns simaticml out\project.objectframe.xml --out out\simaticml

# MC7+ call-statement disassembly
tia-project mc7 disasm out\project.objectframe.xml --out out\mc7

# HMI SQL export + object model + SVG render + HMI IL API index
tia-project hmi export C:\path\to\Project --out out\hmi
tia-project hmi objects out\project.objectframe.xml --out out\hmi
tia-project hmi render out\project.objectframe.xml --out out\hmi\render
tia-project hmi il --bin "<TIA_BIN>" --out out\hmi\il-api.json

# Hardware topology
tia-project hardware export out\project.objectframe.xml --out out\hardware

# Project history / savepoints
tia-project history C:\path\to\Project --out out\history --fs2xml out\project.objectframe.xml
tia-project history C:\path\to\Project --out out\history --extract

# Domain exports
tia-project safety export out\project.objectframe.xml --out out\safety
tia-project umac export out\project.objectframe.xml --out out\umac
tia-project drives export out\project.objectframe.xml --out out\drives

# Everything
tia-project all C:\path\to\Project --out out\project
```

`<project>` may be the project folder, the `.ap21`, or `System\PEData.plf`.
Locked files are copied through a shared handle first.

## Format notes

### PEData.plf

Header (98 bytes):

| Offset | Size | Field |
|---:|---:|---|
| 0 | 4 | `FileType` (`0x40` = Composite) |
| 4 | 4 | `ObsoleteStoreId` |
| 8 | 16 | `FileId` GUID |
| 24 | 16 | `ProductVersion` (4 × int32) |
| 40 | 16 | `FileFormatVersion` (4 × int32) |
| 56 | 1 | `Node` |
| 57 | 8 | reserved/zero |
| 65 | 32 | SHA-256 of bytes 0..64 |
| 97 | 1 | `0xFF` terminator |

Records start at offset 98. Each record is:

```
int32 ObjectSize
  44-byte ObjectHeader
  payload (ObjectSize - 44)
32-byte SHA-256 over the ObjectSize bytes
```

ObjectHeader:

| Offset | Size | Field |
|---:|---:|---|
| 0 | 4 | `ObjectSize` |
| 4 | 4 | `ObjectTypeId` |
| 8 | 8 | `InstanceId` |
| 16 | 4 | `ClusterId` |
| 20 | 4 | `InternalObjectInfo` |
| 24 | 2 | `ObjectStates` |
| 26 | 1 | `OffsetCount` |
| 27 | 1 | `StorageMetaId` |
| 28 | 16 | GUID (native byte order) |

### PEData.idx

Same 98-byte header (`FileType` `0x80` = CompositeIndex). Entries are 38 bytes:

```
int32 TypeId
int64 InstanceId
16-byte Guid
byte  Node
int32 Position      // offset of the PLF record
int32 Cluster
byte  Flags         // 4 deleted, 8 destroyed, 0x20 TRef, 0x40 segment, 0x80 core text
```

`TypeId == 458772` (`System.CommitPoint`) is followed by `int32 Length` and
`Length` bytes (the length includes the 4-byte field). The index is append-only;
the **last** entry for an object id is the live one. Deleted/destroyed entries
have `Position = -1`.

### XRef.db

`parts.binary` is a serialized `ObjectId`:

```
"OG" (0x4F 0x47) + int32 TypeId + int64 InstanceId
```

The `(TypeId, InstanceId)` pair joins directly to the PLF object header. On the
`Scratch` project all 20 non-null parts matched a PLF record, and the live
index positions matched the PLF offsets.

`XRefRelationType` is a flags enum (`Uses`, `IsInstanceOf`, `Assigns`, `Scope`,
`Defines`, `Contains`, `Belongs`, `Mapping`), and `UsageKind` covers
`Read/Write/RW/Call/Jump/Monitor/...`. The numeric relation ids in `rels` are
extension-defined; the tool adds readable labels for the observed object pairs
and falls back to the enum base bits.

### FS2XML export

`Siemens.Automation.ObjectFrame.FileStorage.FS2XML.exe` normally exports with
`hasNativeMethods:true`, which generates a random AES key and does not save the
`.nmf` sidecar. The CLI calls the same `ConsoleExportHelper` through reflection
with `hasNativeMethods:false`, which uses `KeyGenerator.GenerateDefaultKey()`:

```
AES-128-CBC/PKCS7
key = 28 6F 76 5C 6E 3B 1E 4C D0 8E 42 31 43 7B 8E BF
iv  = 01 02 03 01 02 03 01 02 03 01 02 03 01 02 03 01
then Deflate
```

No Siemens binary is modified; the reflection host runs the installed
converter on a project copy.

The decrypted XML is an ObjectFrame store export (`<exportdata>` with
`<StorageObject>` entries), not SimaticML block XML. It is used to resolve PLF
class names, object names, attributes, interface payloads, and block sources.

### Block interface payload

`InterfaceVersionRootData` carries a UTF-8 XML `Payload` coreblob:

```xml
<Root RIdSlots="0">
  <Member ID="2" Name="Input" SubPartIndex="0" StdO="0" />
  <Member ID="3" Name="Output" SubPartIndex="1" StdO="0" />
  ...
</Root>
```

The SimaticML exporter maps these members to `<Sections><Section Name="...">`.
The body AST is `FlgNet` for LAD and `SCLSource` for SCL.

### MC7+

`MC7plus.Disassembler` exposes:

- `DisassembleCallStatements(byte[] codeBinary, IMC7plusExchange2 output)`
- `GetHighestLanguageLevel(byte[] codeBinary)`
- `GetCompilerVersion(byte[] constantsBinary)`

Compiled block data is in `LoadablePlusCodeBlockData.OpenImmediateData` and
`LoadableDependentData.MC7PlusData` (the latter is an `EF BE AD DE`-framed
container). The host implements `IMC7plusExchange2` and emits JSON instructions.
`DisassembleCallStatements` is call-statement focused; a block with no calls
produces an empty instruction list.

### Classic MC7 (S7-300)

Classic S7-300 code is stored unencrypted in
`LoadableCodeBlockData.BinaryData`. The block's `BlockDebugInformation` carries
`EndSAC`; the code is `BinaryData[78 .. 78 + EndSAC + 1]`. The
`Mc7ClassicHost` decodes it with TIA's
`Siemens.Simatic.Lang.CodeGenClassic.DOL.AwlRegenerator.RegenerateExpanded`
and emits statement addresses, byte lengths, and operations. The
Openness-created `PlantE` project (CPU 315-2 PN/DP) supplies eight real
SCL-to-MC7 pairs; `compiler classic-test` requires
`observed.distinctMc7 > 1` and `realCodegen=true`. S7-1500 plus code remains
encrypted in `MC7PlusData` and is not decoded by this path.

## Verified on Scratch

- `PEData.plf`: 1,666,646 bytes, 1,494 records, 1,376 live, 118 history,
  0 trailing bytes, all object hashes valid, 0 index GUID mismatches.
- `PEData.idx`: 1,486 entries, 7 commit points, parses exactly to EOF.
- `XRef.db`: 21 parts (20 decoded + 1 null), 15 objects, 12 relations,
  2 addresses; all decoded parts matched PLF.
- FS2XML export: 360,560 encrypted bytes → 5,494,040 plain XML bytes,
  1,317 objects.
- Blocks: `Main` (OB.ProgramCycle, LAD) and `Block_1` (FC, SCL), with source
  XML extracted and SimaticML documents generated.
- MC7: `Main` compiled code 12 bytes, language level 6; no call instructions.
- HMI SQL DB: 94 tables, 85 tags, 1 channel, 1 connection, 41 alarm blocks,
  366 messages; 38 populated tables exported.
- HMI object model: `Screen_1` (ScreenView, 1024×768), `HmiScreenItemData`,
  85 `HmiSystemTagData` entries, cycles, themes, runtime security objects.
- HMI render: `Screen_1.svg` with `Pipe elbow_1` geometry.
- Hardware: 4 devices, 15 device items, 3 nodes, 2 address managers.
- History: 8 savepoints (initial + 7 commit points), 1,486 index versions.
- Domains: 33 UMAC/access-control objects, 0 safety objects, 0 drive objects.
- VCI: 0 file records in this project (schema is present).

## Notes and limits

- PLF payloads are object-specific serialized attribute graphs. This tool
  exposes record metadata, payload strings/XML blobs, class/name/attribute
  enrichment, interface payloads, and block source ASTs; it does not claim to
  decode every object-specific payload field.
- The FS2XML reflection host requires the TIA installation (`--bin`) and
  PowerShell 5.1. It runs the installed converter, not a patched binary.
- HMI export needs the `MSSQL$WINCC` service and permission to restore a
  database. The `.bak` is copied to a temp directory and granted to
  `NT SERVICE\MSSQL$WINCC`; the temporary database is dropped in `finally`.
- MC7 disassembly depends on the installed mixed-mode
  `Siemens.Simatic.Lang.MC7Codegenerator.dll` and a .NET Framework host.
- SimaticML documents are generated shells: block metadata, interface
  sections from the internal `Payload` XML, and the body AST (`FlgNet` /
  `SCLSource`). They are well-formed XML, but have not been round-trip
  validated by TIA/Openness.
- The HMI IL index is an API-surface extraction (`RdfModelV21` /
  `RdfSerializationV21`), not a full HMI IL compiler.
- VCI tables in the sample project are empty; the verifier still supports
  SHA-256/SHA-1/MD5/SHA-512 `FileHashDescription` values.
