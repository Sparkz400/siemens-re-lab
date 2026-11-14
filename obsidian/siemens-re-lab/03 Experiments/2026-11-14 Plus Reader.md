---
tags: [experiment, mc7, plus, mc7plus, reader]
created: 2026-11-14
status: negative
---

# 2026-11-14 Plus Reader

## Hypothesis

`MC7PlusBinaryReader` was previously called with an empty immediate table.
`PlusLoadableBinary.ImmediateLongTable` returns `OpenImmediateData`, and
`MC7PlusBinaryReader` uses `m_Immediates` to resolve `ImmediateLong` operands.
Passing `OpenImmediateData` (and section 0 as an alternative table) might make
section 1 decode.

## Method

1. Extract each plus block from `PlantB2.objectframe.xml`:
   - `OpenImmediateData` -> `<block>.open.bin`
   - `MC7PlusData` -> `<block>.mc7plus.bin`
   - `EF BE AD DE` sections -> `<block>.sec0.bin`, `.sec1.bin`, `.sec124.bin`
2. Run a collector with `MC7PlusBinaryReader` for:
   - section 1 with `open`, `sec0`, `sec1`, and no immediate table
   - section 0 with the same tables
   - the full `MC7PlusData` container
3. Also run `MC7plus.Disassembler.DisassembleCallStatements` on the same
   inputs.

## Result

| Code | Immediate | Reader | Disassembler |
|---|---|---|---|
| `FC_And.sec1` | open | `Read not allowed type code` | 0 instructions |
| `FC_And.sec1` | sec0 | `Read not allowed type code` | 0 instructions |
| `FC_And.sec1` | none | `Read not allowed type code` | 0 instructions |
| `FC_And.sec0` | open/sec0/none | `IndexOutOfRangeException` | `Operation not found` |
| `FC_And.mc7plus` | open/sec0/none | `Instruction code has no match` | `Operation not found` |
| `FC_LogicB.sec1` | any | `IndexOutOfRangeException` | 0 instructions |
| `FC_Mux.sec1` | any | `Destination array was not long enough` | `Operation not found` |
| `Main.sec1` | any | `ArgumentOutOfRangeException` | `Operation not found` |

The immediate table does not change the failure mode. Section 1 is not a raw
instruction stream, or it is encrypted/compressed, or the container framing is
wrong.

## Conclusion

`MC7PlusBinaryReader` with `OpenImmediateData` is **not sufficient** for the
observed sections. The immediate-table hypothesis does not explain the
section-1 failure by itself.

## Next

- Test `GetMC7Plus(PlusBinaryType.Simulation)` and `VPlc` — a different
  dependent blob may be unencrypted.
- Hook `Disassembler.DisassembleCallStatements` at runtime and dump the
  `codeBinary` argument.
- Dump PLCSIM/CPU memory after download.
