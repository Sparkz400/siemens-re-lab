---
tags: [experiment, mc7, plus, csi, keys, decoded]
created: 2026-11-14
status: partial
---

# 2026-11-14 Plus Key Derivation

## Hypothesis

The two 16-byte ids in the `MC7PlusData` header are not random. They are the
CSI key ids that TIA derives from project metadata and the CPU family key.

## Finding

`Siemens.Simatic.PlcLanguages.BlockLogic.PlusPlcSecurityInfo` in
`Siemens.Simatic.Lang.SwTarget.dll`:

```csharp
m_TargetSeed = GetTargetSeed(projectAttributes);
publicKey    = OmsKeyProvider.GetCpuKey(ControllerTarget, OmsKeyType.FamilyKey, ...);
PublicId     = keyKeeper.AddKey(publicKey);
TargetId     = keyKeeper.GenKey(m_TargetSeed, PublicId);
```

and

```csharp
private byte[] GetTargetSeed(ICoreAttributes projectAttributes)
{
    byte[] ticks = BitConverter.GetBytes(projectAttributes.CreationTime.ToBinary());
    byte[] id    = ControllerTarget.Id.ToByteArray();
    return ticks.Concat(id).ToArray();
}
```

`ObjectId.ToByteArray()` is implemented by
`ObjectIdConvert.ObjectIdStructurePartsToByteArray`:

```text
4F 47 <typeId:u32 LE> <instanceId:u64 LE>
```

## Measured result

For `PlantB2 / FC_And`:

| Input | Value |
|---|---|
| project `CreationTime` | `5250988075188121259` |
| controller target | `{1052700-1411}` |
| seed | `ab6e665dd541df48 4f471c1010008305000000000000` |
| header public id | `9379cc342363e4990400000000000000` |
| header target id | `8146c72979d457820100000000000000` |

`KeyKeeper.AddKey(familyKey)` and `KeyKeeper.GenKey(seed, publicId)` reproduce
both ids **exactly**. The family key is 42 bytes and is public project key
material; its SHA-256 is in `analysis/plus-key-derivation.json`.

## Container format

```text
0x00 EF BE AD DE  magic
0x04 u32 124       header length
0x08 u32 1         version
0x0C u32 2         CSI mode (18 for lightweight binding)
0x10 u32 50
0x14 u32 1024      chunk size
0x18 u32 0
0x1C 16 bytes      public key id
0x2C 16 bytes      target key id
0x3C u32 60        mac length
0x40 mac[60]       wrapped/session data
...                EF BE AD DE + index + length + payload sections
```

For `FC_And` the sections are `0:32, 1:27, 2:0`. The code length is
`UnencryptedCodeBinaryLength - OpenImmediateData.Length = 23 - 12 = 11`, which
matches `section 1 = 16 + 11` and `section 0 = 16 + padded(11)`.

## Why decryption is still blocked

`CsiEncoderUm` (`Siemens.Simatic.Lang.MC7Codegenerator.dll`) generates a fresh
random session key for every `EncryptDataInit`. The same plaintext and tag
produce different ciphertexts and a different 60-byte header field on every
call. `Siemens.DF.CsiWrapper.dll` exports only:

```text
InitCommonSecurity
EncryptDataInit / EncryptDataUpdate / EncryptDataFinal
CalculateMACHybrid
BlockLength / CipherBufferLength / GetContextLength
```

There is no `Decrypt*` export. `CsiStatics.GetCsi()+120` is a verifier: given
the container it returns the container unchanged and a success code.

So the key ids are solved, but the payload decryption needs the **private
session-key unwrap**, which lives in the missing
`Siemens.Simatic.Lang.Mc7PlusDisassembler.dll`.

## Tools

- `probe/mc7plus_keys.py` — extracts the seed, family-key hash, MAC and
  container header from an objectframe XML.
- `probe/mc7plus_keys/` — C# host that drives TIA's `KeyKeeper` and verifies
  the ids.
- `analysis/plus-key-derivation.json` — sanitized evidence.

## Next

- Obtain `Siemens.Simatic.Lang.Mc7PlusDisassembler.dll`.
- Hook `CsiEncoderUm.Encrypt` at runtime and capture the plaintext before
  encryption.
- Try `GetMC7Plus(PlusBinaryType.Simulation/VPlc)`.
