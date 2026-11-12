# Environment and toolkit

## Host / VM

| Item | Value |
|---|---|
| OS | Windows 11 Pro, build 26100 |
| Platform | VMware virtual machine, VMware Tools running |
| CPU | 2 vCPU (Intel Core Ultra 7 155H host, nested virtualization enabled) |
| RAM | 11.7 GB |
| Disk | 140 GB NVMe virtual disk; C: 138.7 GB, ~59 GB free at last check |
| Display | 1661×1040 console session |

## Siemens products

| Product | Version | Notes |
|---|---|---|
| TIA Portal | V21 | STEP 7 Professional 2025 + WinCC Unified Engineering |
| PLCSIM Advanced | V8.0 | S7-1500 simulation; virtual Ethernet adapter present, no instance running |
| WinCC Unified PC RT | 21.0 | `HMI_RT_1`; GraphQL server on port 4000 |
| WinCC RT Advanced | 17 (runtime only) | no Advanced engineering option |
| Automation License Manager | — | TIA V21, PLCSIM Advanced V8 and WinCC Unified PC RT licenses present |

No real PLC and no HMI panel are attached. All PLC work is PLCSIM Advanced; all HMI work is the
Unified PC runtime.

## Windows reverse-engineering toolkit

Installed under `C:\Programs\04_Programme` (portable unless noted):

| Tool | Purpose |
|---|---|
| Ghidra 12.1.3 | native decompiler, headless + GUI |
| IDA Free 9.5 | native disassembler/decompiler (user-installed) |
| dnSpyEx | .NET debugger/decompiler |
| ILSpy 11.2 + ilspycmd 11.2 | C# decompiler / CLI |
| de4dot-cex | .NET deobfuscation |
| x64dbg + ScyllaHide + xAnalyzer | Windows debugging, anti-anti-debug, API annotations |
| Cutter / Rizin 0.9.1 | reverse engineering GUI/CLI |
| PE-bear, Detect It Easy, capa, FLOSS | PE inspection, capability/string analysis |
| GoReSym, pyinstxtractor-ng | Go symbol recovery, PyInstaller extraction |
| jadx 1.5.6, apktool 3.0.3, Recaf 4.x | Android/Java |
| UABEA, AssetRipper, Il2CppDumper, Cpp2IL | Unity assets/IL2CPP |
| RetDec, ImHex, Strings, Resource Hacker, HxD | misc static analysis |
| Sysinternals, System Informer, Process Explorer/Monitor, Handle, API Monitor | Windows internals/tracing |
| PE-sieve, HollowsHunter, Scylla, YARA, CyberChef | unpacking, IAT rebuild, rules, encoding |
| WinDbg, WinObjEx64 | kernel/user debugging and object manager |
| REA 6.8 | agent-driven RE with Ghidra/IDA/jadx/apktool/Kuna (Windows) |
| TIA MCP v0.9.0 | 314 MCP tools over TIA Openness (see 05) |
| Python 3.12 | `requests`, `graphql-core`, `websocket-client` 1.9.2, `pypdf`, `pefile` |
| portable Git 2.56.1 | repo tooling (added for this project) |

## WSL2 Linux worker

| Item | Value |
|---|---|
| Distro | Ubuntu 24.04.5 LTS, WSL 3.0.5, VERSION 2 |
| User | `rea` (uid 1000), passwordless sudo |
| Resources | 6 GB RAM, 2 vCPU, 2 GB swap (`C:\Users\JW\.wslconfig`) |
| Node / REA | Node 24.22.3, `rea-agents` 6.9.0 |
| Ghidra | 12.1.3 + OpenJDK 21, `/opt/ghidra/ghidra_12.1.3_PUBLIC` |
| Kuna | 1.990, `/opt/kuna/kuna` (REA provider works on Linux) |
| jadx / apktool | `/opt/rea/jadx-headless-mcp-0.7.1-all.jar`, `/opt/rea/apktool.jar` |
| Rizin | 0.9.1 static, `/opt/rizin/bin/rizin` |
| Debug / network | gdb 15.1, TShark 4.2.2, Frida 17.26.2, pwntools |

MCP servers registered in OpenCode: `rea` (Windows), `rea-linux` (WSL), `tia` (TIA Openness).
Linux sees this repo at `/mnt/c/Programs/siemens-re-lab`.

## TIA MCP build environment

- Source: `Sparkz400/TIA_MCP` (public), release bundle `tia-mcp-v21-0.9.0_20261112_122441_bb02d6cf`.
- .NET SDK 10.0.401; net48 workers built with `Microsoft.NETFramework.ReferenceAssemblies.net48`
  and `FrameworkPathOverride` (Developer Pack not installed).
- Local patches are documented in [05-tia-openness.md](05-tia-openness.md): Test Suite stub and
  `hardware_add` station-name fix.
- Openness allow-list: `HKLM\SOFTWARE\Siemens\Automation\Openness\AllowList\TiaMcp.Openness.V21.exe`
  (one entry per worker path/hash).

## Credentials and data handling

- Lab runtime user and transfer password are **not** stored in this repo.
- Generated PLC credentials live only in DPAPI-protected files under the local TIA MCP
  artifacts directory.
- The GraphQL probe takes credentials as CLI arguments; tokens are kept in memory only.
- No ALM license keys, certificates, project archives or runtime packages are published.
