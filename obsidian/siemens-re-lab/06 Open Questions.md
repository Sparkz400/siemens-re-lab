---
tags: [tia, open-questions]
created: 2026-11-14
---

# Open Questions

## Plus MC7

- Where is the plaintext plus code created? Compiler, loader, or disassembler?
- What exactly does `CsiStreamImpl` protect: code, signature, or both?
- Is `FamilyKey` a public key, a key ID, or a wrapped key?
- Do `PlusBinaryType.Simulation` / `VPlc` store a different blob?
- Does TIA's MC7 view call `DisassembleCallStatements` with plaintext?
- What is the correct container framing for `MC7PlusBinaryReader`?
- Does a plus block have a classic `BinaryData` fallback on any CPU family?

## PLCSIM / CPU

- Does TIA's UI Start simulation create the dependent simulation data?
- Can Openness upload a station from PLCSIM after a UI download?
- Where is the plaintext code in PLCSIM V21 process memory?
- Does a real S7-1500 upload return plus code or classic code?

## TIA IDE / add-ons

- Which managed assemblies own the plus-code decryption path?
- Which native exports are used by `CsiStreamImpl` / `CsiVerifierImpl`?
- How do Startdrive, Safety and WinCC Unified store their compiled artifacts?
- Can TIA MCP/Openness expose the plus binary types without internal reflection?

## Cross-release

- Which TIA release changed the plus container or key format?
- Can a V15.1–V20 VM capture the same project for drift?
- Which objectframe package versions differ across releases?
