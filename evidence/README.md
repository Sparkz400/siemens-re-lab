# Evidence bundle

Sanitized JSON summaries from the TIA Portal V21 lab runs. No Siemens
binaries, project archives (`.ap21`/`.zap21`), objectframe exports, credentials
or customer data are included.

## Placeholders

| Placeholder | Meaning |
|---|---|
| `<LAB>` | local checkout of this repository / the `tia-project` harness |
| `<PROJECTS>` | local TIA project directory (for example `Scratch`) |
| `<TIA_BIN>` | local TIA Portal `Bin` directory |
| `<TEMP>` | local temporary working directory |

The original JSON contained absolute Windows paths. Only the top-level project
and output paths were rewritten; block names, code hashes and measured values
are unchanged.

## Files

| File | Content |
|---|---|
| `platform-all/platform-summary.json` | 29/29 platform steps on `Scratch` |
| `platform-classic/platform-summary.json` | 29/29 platform steps with the classic compiler export |
| `platform-classic/compiler-test.json` | compiler gate from the platform run (`distinct=8`, `realCodegen=true`) |
| `plantE-classic-gate/compiler-test.json` | explicit classic S7-300 gate |
| `plantE-classic-gate/probe.json` | observed SCL-to-MC7 records, source features, code hashes |
| `plantE-classic-gate/model.json` | ridge model rows and weights |
| `plantE-classic-gate/differential.json` | differential probe rows |
| `plantE-negative2/probe.json` | negative extraction test (`FC_And` truncated, `withMc7=7`) |
| `realpair/realpair-summary.json` | 8/8 real-pair bundle |
| `realpair/compiler-test.json` | compiler evidence inside the real-pair bundle |

## Headline numbers

```text
platform-all:      status=pass steps=29 failed=0
platform-classic:  status=pass steps=29 failed=0
realpair:          status=pass steps=8  failed=0

classic gate:
  observed=8 withMc7=8 distinct=8 realCodegen=True observedLoo=7/8
  r2=0.9969 rmse=0.72 mae=0.875 jaccard=0.768
```

## Reproduction

1. Create the classic S7-300 project with
   [`probe/openness_classic`](../probe/openness_classic). It creates CPU
   `6ES7 315-2EH14-0AB0/V3.2`, the four tags and eight SCL functions, then
   compiles.
2. Export it to objectframe XML with the Openness `fs2xml` step.
3. Extract and decode the code with
   [`probe/mc7_classic_extract.py`](../probe/mc7_classic_extract.py) and
   [`probe/mc7_classic_host`](../probe/mc7_classic_host).
4. Run the compiler gate from the harness:

```powershell
.\tools\tia-project\bin\Release\net10.0\tia-project.exe compiler classic-test `
    --fs2xml <PlantE.objectframe.xml> `
    --bin '<TIA_BIN>' `
    --out out\plantE-classic-gate
```

The plus-code evidence is documented in
[`docs/13-mc7-codegen.md`](../docs/13-mc7-codegen.md). It is a blocked result:
the S7-1500 `MC7PlusData` is encrypted at rest, and the public Openness surface
does not expose a decryption or read-back path.
