# MC7+ key derivation probe

This probe reproduces the two CSI key ids that TIA stores in an S7-1500
plus-code container.

The derivation is implemented by TIA in
`Siemens.Simatic.PlcLanguages.BlockLogic.PlusPlcSecurityInfo`
(`Siemens.Simatic.Lang.SwTarget.dll`):

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

`ObjectId.ToByteArray()` is `4F 47` (`"OG"`) followed by the 32-bit type id
and the 64-bit instance id, both little-endian:

```text
ObjectIdConvert.ObjectIdStructurePartsToByteArray
  = 4F 47 <typeId:u32 LE> <instanceId:u64 LE>
```

## 1. Extract the inputs

```powershell
python .\probe\mc7plus_keys.py `
    --xml out\real-projects\PlantB2.objectframe.xml `
    --block FC_And `
    --out analysis\plus-key-derivation.json
```

The JSON contains `seedHex`, `familyKeySha256`, the container header ids and
the per-block MAC. The family key itself is public key material from the
project; only its hash is written to the repo.

## 2. Derive the key ids

Build the host (uses the TIA `KeyKeeper` from the installed Portal):

```powershell
.\probe\mc7plus_keys\build.ps1
```

Run it with the seed and family key from the export:

```powershell
$env:TIA_BIN = 'D:\Arbeit\Siemens\Portal V21\Bin'
.\probe\mc7plus_keys\bin\mc7plus-keys.exe `
    --family-key <familyKeyHex> `
    --seed <seedHex> `
    --public-id 9379cc342363e4990400000000000000 `
    --target-id 8146c72979d457820100000000000000
```

Expected result for `PlantB2 / FC_And`:

```text
publicId=9379cc342363e4990400000000000000
targetId=8146c72979d457820100000000000000
publicIdMatch=True
targetIdMatch=True
```

The key ids are stored little-endian in the container header at offsets
`0x1C` (public) and `0x2C` (target). They are identifiers, not the keys
themselves; the CSI key material stays in TIA's native `CsiBuffer`.

## 3. Container layout

```text
0x00  EF BE AD DE              magic
0x04  u32 header length       124
0x08  u32 version             1
0x0C  u32 csi mode            2 (18 for lightweight binding)
0x10  u32                     50
0x14  u32 chunk size          1024
0x18  u32                     0
0x1C  16 bytes                public key id
0x2C  16 bytes                target key id
0x3C  u32 mac length          60
0x40  mac[mac length]         wrapped/session data
then repeated:
      EF BE AD DE
      u32 section index
      u32 section length
      section bytes
```

The encryption itself is performed by TIA's native CSI engine
(`Siemens.DF.CsiWrapper.dll`, `CsiEncoderUm`). The engine only exposes
encryption (`EncryptDataInit/Update/Final`) and a verifier; the decryption
path lives in the missing `Siemens.Simatic.Lang.Mc7PlusDisassembler.dll`.
