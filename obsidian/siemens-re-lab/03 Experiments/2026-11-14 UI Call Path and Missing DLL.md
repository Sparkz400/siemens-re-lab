---
tags: [experiment, mc7, plus, ui, disassembler, missing-dll]
created: 2026-11-14
status: blocked-on-artifact
---

# 2026-11-14 UI Call Path and Missing DLL

## Finding

TIA's MC7 view logic contains the exact plus-code call path. From
`Siemens.Simatic.Lang.NetworkEditorFrame.dll`,
`Siemens.Simatic.PlcLanguages.NetworkEditorFrame.Editor.Logic.MC7ViewLogic`:

```csharp
if (m_CodeBlock.LoadableBinary is IPlusLoadableCodeBlockBinary plusLoadableCodeBlockBinary
    && plusLoadableCodeBlockBinary.GetMC7Plus() != null)
{
    string path = Path.Combine(
        PlatformEnvironment.BinDirectory,
        "Siemens.Simatic.Lang.Mc7PlusDisassembler.dll");
    Assembly assembly = Assembly.LoadFile(path);
    if (assembly != null)
    {
        object obj = assembly.CreateInstance(
            "Mc7PlusDisassember.Disassembler.Disassembler",
            ignoreCase: true,
            BindingFlags.CreateInstance,
            null,
            new object[2]
            {
                plusLoadableCodeBlockBinary.GetMC7Plus(),
                plusLoadableCodeBlockBinary.ImmediateLongTable
            },
            CultureInfo.InvariantCulture,
            null);
        List<string> list = (List<string>)obj.GetType().InvokeMember(
            "GetDisassembledCode",
            BindingFlags.DeclaredOnly | BindingFlags.Instance |
            BindingFlags.Public | BindingFlags.InvokeMethod,
            null, obj, new object[0], CultureInfo.InvariantCulture);
        ...
    }
}
```

This tells us:

1. The UI passes **the encrypted `GetMC7Plus()` blob and `ImmediateLongTable`**
   directly to a separate disassembler assembly.
2. The separate assembly is `Siemens.Simatic.Lang.Mc7PlusDisassembler.dll`.
3. The type is `Mc7PlusDisassember.Disassembler.Disassembler`.
4. The method is `GetDisassembledCode()` returning `List<string>`.

## The blocker

`Siemens.Simatic.Lang.Mc7PlusDisassembler.dll` is **not installed** on this
TIA V21 system.

Search results:

```text
D:\Arbeit\Siemens\Portal V21\Bin\*.dll
  no Mc7PlusDisassembler.dll

D:\Arbeit\Siemens (recursive)
  no Mc7PlusDisassembler.dll
  no "Mc7PlusDisassember" string in any DLL

C:\Program Files\Siemens\Automation (recursive)
  no Mc7PlusDisassembler.dll
  no "Mc7PlusDisassember" string in any DLL
```

`MC7ViewLogic` checks `assembly != null` but `Assembly.LoadFile` throws when
the file is missing; the surrounding catch returns `"n/a"`. The plus MC7 view
is therefore unavailable on this installation.

## Why this matters

This is the strongest plus-code lead so far:

- It is TIA's own official plus-code path.
- It receives exactly the blobs we already extract.
- It must handle the decryption/decoding internally.
- It is a separate assembly, not the `MC7Codegenerator.Disassembler` we tried.

If we obtain that DLL from a TIA update, optional package, or another release,
we can load it directly with the extracted `MC7PlusData` and
`OpenImmediateData` and call `GetDisassembledCode()`.

## Other call sites

`GetMC7Plus` is referenced by:

- `Siemens.Simatic.Lang.Block.Compiler.dll`
  - `CallPatcher` calls `PlusCodegenerator.GetCallStatements(mC7Plus, ...)`
  - `GetHighestLanguageLevel(mC7Plus)`
  - `PatchCall` reads `GetMC7Plus`, `ImmediateLongTable`,
    `GetClosedImmediateLongTable`, `GetOptimizeInfo`, `OwnSignature`
- `Siemens.Simatic.Lang.BlockHandling.TIA.Impl.dll`
  - `PlusLoadableBinary.GetMC7Plus`
- `Siemens.Simatic.Lang.NetworkEditorFrame.dll`
  - `MC7ViewLogic` (the missing-DLL path)
- `Siemens.Simatic.Lang.SCLEditor.dll`
- `Siemens.Simatic.Lang.PLInterface.dll`
- `Siemens.Simatic.Lang.Mc7PlusCommon.dll`

`DisassembleCallStatements` is only referenced by:

- `Siemens.Simatic.Lang.MC7Codegenerator.dll`
- `Siemens.Simatic.Lang.MC7plus.Adapter.dll`
- `Siemens.Simatic.Lang.MC7plus.Internal.dll`

## Next

1. Search TIA installer media / update packages for
   `Siemens.Simatic.Lang.Mc7PlusDisassembler.dll`.
2. Try a different TIA release/VM where the plus MC7 view is installed.
3. If the DLL is unavailable, replicate its logic from
   `MC7plus.Adapter`/`MC7Codegenerator` or capture the decrypted input at the
   `MC7ViewLogic` boundary.
4. Keep the `GetMC7Plus()` + `ImmediateLongTable` pair as the input contract.

## Status

Blocked on the missing `Mc7PlusDisassembler.dll` artifact. This is not a
crypto dead end; it is an installation/artifact gap.
