# TIA V21 add-on inventory

Source: `tia-assembly-inventory.csv` (2573 assemblies, 1640.8 MB).

Prefix-based classification. An assembly may match more than one family.

| Family | Files | Managed | Native | Size MB | Examples |
|---|---:|---:|---:|---:|---|
| wincc-unified | 952 | 941 | 11 | 470.5 | `Siemens.Simatic.Hmi.BL.ExpressionValidator.dll`, `Siemens.MP.tkWebUICli.dll`, `Siemens.MP.tkDotNetDirectProxies.dll`, `Siemens.MP.LMC.tkTIABridge.dll` |
| plc-language | 207 | 207 | 0 | 64.0 | `Siemens.Simatic.Lang.MC7Codegenerator.dll`, `Siemens.Simatic.Lang.FLDiagram.dll`, `Siemens.Simatic.Lang.Block.Compiler.dll`, `Siemens.Simatic.Lang.BlockEditorFrame.dll` |
| hardware-config | 146 | 141 | 5 | 230.3 | `Siemens.Simatic.Hwcn.BusinessLogic.Ncm.scw_mips_V6_1.dll`, `Siemens.Simatic.Hwcn.BusinessLogic.Ncm.scw_ppc460_V6_1.dll`, `Siemens.Simatic.Hwcn.BusinessLogic.Ncm.scw_mips_V6_0.dll`, `Siemens.Simatic.Hwcn.BusinessLogic.Ncm.scw_mips_V5_2.dll` |
| safety | 26 | 26 | 0 | 11.2 | `Siemens.Simatic.Safety.Compiler.dll`, `Siemens.Simatic.Safety.Tia.dll`, `Siemens.Simatic.Safety.Core.dll`, `Siemens.Simatic.Safety.Interface.dll` |
| drives-startdrive | 116 | 115 | 1 | 39.7 | `Siemens.MC.Drives.Acx.Hwcn.dll`, `Siemens.MC.Drives.Common.SafetyAcceptanceTest.dll`, `Siemens.MC.Drives.Acx.BusinessLogic.dll`, `Siemens.MC.Drives.Acx.TechTrace.dll` |
| test-suite | 0 | 0 | 0 | 0.0 |  |
| opc-ua | 10 | 10 | 0 | 11.4 | `Opc.Ua.Core.dll`, `Siemens.Simatic.OpcUa.Bl.dll`, `Siemens.Simatic.OpcUa.Ui.dll`, `Siemens.Simatic.OpcUa.Base.Bl.dll` |
| security-csi | 96 | 95 | 1 | 13.3 | `Siemens.DF.Domain.PN.Pure.Impl.BL.dll`, `Siemens.DF.Dev.GSD.Pure.Impl.Converter.BL.dll`, `Siemens.DF.Infra.ObjTModel.Pure.BL.dll`, `Siemens.DF.DG.SecurityPlus.Pure.Impl.BL.dll` |
| automation-framework | 401 | 397 | 4 | 161.1 | `Siemens.Automation.Cax.MddPublicationTool.OpennessDB.dll`, `Siemens.Automation.Rse.AwlCodeGen.dll`, `Siemens.Automation.UI.Controls.dll`, `Siemens.Automation.FrameApplication.dll` |
| motion-technology | 94 | 86 | 8 | 339.5 | `Siemens.MP.tkWebUICli.dll`, `Siemens.TechTrace.Editor.UserInterface.dll`, `Siemens.MP.tkDotNetDirectProxies.dll`, `Siemens.MP.LMC.tkTIABridge.dll` |

Unmatched assemblies: 564.

Notable families:

- `wincc-unified` includes `Siemens.Simatic.Hmi.*` and `Siemens.MP.*`
  (WinCC Unified engineering/runtime and motion proxies).
- `plc-language` includes the block compiler, MC7 codegen and the
  classic/plus language assemblies.
- `security-csi` includes `Siemens.DF.CsiWrapper.dll` and the
  `Siemens.DF.DG.SecurityPlus.*` CSI facades.

Reproduce:

```powershell
python .\probe\tia_addon_inventory.py
```
