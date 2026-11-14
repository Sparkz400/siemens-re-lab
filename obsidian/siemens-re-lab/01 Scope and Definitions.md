---
tags: [tia, scope, definitions]
created: 2026-11-14
---

# Scope and Definitions

## A — engineering data

The project graph as TIA understands it:

- blocks, interfaces, SCL/LAD/STL source, compile metadata
- tags, tag tables, constants, DBs
- hardware topology, modules, addresses, subnets
- HMI screens, tags, alarms, dynamizations, scripts
- safety, UMAC, drives, VCI, history, savepoints

**Target:** a typed graph that can be exported, queried, diffed and
round-tripped without TIA. This is mostly an Openness + PLF/IDX/XML problem.

## B — compiled artifacts

The bytes the CPU/HMI actually execute:

- classic S7-300/400 MC7
- S7-1200/1500 plus MC7
- HMI JavaScript / runtime packages
- drive firmware/parameter images
- safety F-blocks

**Target:** decode to instructions or a typed IR, with a verified round-trip
and, where possible, an equivalence check.

## C — TIA Portal and add-ons

The application itself:

- managed .NET assemblies (Openness, block logic, MC7, HMI, PLCSIM adapters)
- native DLLs (CSI crypto, compiler, runtime, graphics)
- add-on packages (Startdrive, Safety, WinCC Unified, Test Suite, TIA MCP)

**Target:** not a full source reconstruction. The target is a **map** of the
interfaces, formats, keys and call paths that A and B depend on. Decompiled
vendor source stays local; the repo gets metadata, hashes, scripts and notes.

## Why this order

A and B are the useful artifacts. C is the means to unlock the remaining B
surfaces (plus MC7, HMI scripts, drive/safety formats), not an end in itself.
