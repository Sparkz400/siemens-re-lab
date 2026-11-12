# Goal 2 — S7comm-plus client

## Objective

Read tags from an S7-1500 (PLCSIM Advanced first) over S7comm-plus without TIA or WinCC, then
offer the values to higher-level systems (CSV, MQTT on a Raspberry Pi). The first milestone is
**read-only**: no writes, no online changes.

## Why it is hard

S7comm-plus is the S7-1500 protocol. Unlike classic S7comm, it uses a TLS-like handshake/key
exchange and a more complex item/addressing model. Tag browsing is symbolic and firmware
dependent. The project should not re-derive the crypto; it should reuse documented prior art and
captured traffic.

## Test rig

| Piece | State |
|---|---|
| S7-1500 CPU | CPU 1511-1 PN V4.0 in the TIA project |
| Simulator | PLCSIM Advanced V8, virtual Ethernet adapter present |
| Capture | Wireshark + S7comm-plus dissector; Npcap installed, Wireshark installer available |
| Real PLC | none |
| Classic S7-300/400 | none; classic S7comm needs a Snap7 server or hardware |

TIA downloads/online traffic to PLCSIM is S7comm-plus, so the same rig provides both the target
and the capture source.

## Plan

1. Start a PLCSIM Advanced instance with the lab CPU and a small DB (10 mixed tags).
2. Capture TIA ↔ PLCSIM download and online traffic; annotate the handshake and a tag read.
3. Build a Python/.NET read-only client against known DB offsets first.
4. Add symbolic browsing only after the offset path is stable.
5. Long-run test (1 h), reconnect after PLCSIM stop/start.
6. Phase 2: Pi/MQTT bridge; writes remain out of scope.

## Prior art to study

- `S7CommPlusDriver` (C#) — protocol/connection implementation.
- Wireshark S7comm-plus dissector — field layout and handshake.
- `python-snap7` — classic S7comm only; useful for 300/400 and as an API design reference.

## Acceptance

1. 10 DB tags read at 1 Hz for 1 hour from PLCSIM Advanced.
2. Values match the TIA watch table at sampled instants.
3. Reconnect works after PLCSIM restart.
4. No write request in the MVP.
5. Capture and protocol notes committed (pcap may be excluded for size; hashes/annotations kept).

## Risks

- Handshake/key exchange and firmware variants.
- PLCSIM only emulates S7-1500; real hardware/protection is untested.
- Symbolic browsing may be much harder than offset reads.
- No real PLC means no validation of optimized/block-protected configurations.

## Status

Not started. The capture/test rig and prior-art list are ready; PLCSIM Advanced is installed and
licensed. Wireshark is not installed yet (installer available in `_Installers`).
