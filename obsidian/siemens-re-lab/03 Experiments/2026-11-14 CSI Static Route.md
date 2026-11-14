---
tags: [experiment, mc7, plus, csi, static]
created: 2026-11-14
status: negative
---

# 2026-11-14 CSI Static Route

## Hypothesis

The managed CSI wrappers might decrypt `MC7PlusData`:

- `CsiStreamImpl` — a `Stream` that could be read as plaintext.
- `CsiVerifierImpl` — a verifier that might expose the plaintext.
- `KeyKeeper` / `FamilyKey` — a key path into the cipher.

## Findings

### CsiStreamImpl

Decompiled from `Siemens.Simatic.Lang.MC7Codegenerator.dll`:

```text
CsiStreamImpl : CsiStream
  Position / Length / Seek / SetLength -> NotSupportedException("Write only stream")
  CanRead -> false
  Read -> NotImplementedException("Write only stream")
  Write -> EncryptCurrentBuffer -> CsiEncoderUm.Encrypt
  Flush -> CsiEncoderUm.Finalize
  GetMac -> 16-byte tag
```

Constructor:

```text
csiMode 2 or 18 only; otherwise "Only encryption modes are supported"
creates CsiEncoderUm, not a decoder
```

There is no read/decrypt path.

### CsiVerifierImpl

```text
IsValid(byte[] data)
  -> native CsiStatics function pointer at +120
  -> returns bool
```

It verifies a signature; it does not return plaintext.

### KeyKeeper

```text
AddKey(byte[] key) -> CsiKeyId
GenKey(byte[] seed, object publicKeyId) -> CsiKeyId
```

It manages the native key buffer. `FamilyKey` can be added as a key, but the
disassembler still fails (see the Plus Reader experiment).

### Native CsiWrapper

Exports:

```text
BlockLength
CalculateMACHybrid
CipherBufferLength
EncryptDataFinal
EncryptDataInit
EncryptDataUpdate
GetContextLength
InitCommonSecurity
```

No `Decrypt*` export. The managed API is an encoder/verifier surface, not a
decoder.

## Conclusion

The static CSI route is **closed** for the public API. `CsiStreamImpl` is a
write-only encryption stream; `CsiVerifierImpl` only verifies. There is no
public decrypt path to feed `MC7PlusData` into.

The plaintext must be captured at runtime (disassembler boundary, UI MC7 view)
or from CPU/PLCSIM memory.

## Next

- Runtime hook of `Disassembler.DisassembleCallStatements`.
- TIA UI MC7 view.
- PLCSIM/CPU memory dump.
- Internal `GetMC7Plus(PlusBinaryType.Simulation/VPlc)` if a simulation blob
  exists.
