---
name: dotnet-csharp14
description: Use when writing or refactoring C# in the engine-free core (layers 01–04 — Shared.Kernel/Diagnostics, Network.Abstractions/Protocol/Crypto/Transport.Pipelines, Assets.Vfs/Parsers/Mapping, Client.Domain/Application/Infrastructure). Conventions for C# 14 / .NET 10: strongly-typed record-struct IDs, Pack=1/InlineArray wire & asset structs, zero-alloc Span hot paths, CP949 text, source-gen logging, and the downward-only layer DAG.
user-invocable: false
paths: 01.Infrastructure.Shared/**, 02.Network.Layer/**, 03.Storage.Assets/**, 04.Client.Core/**
---

# dotnet-csharp14

House rules for the engine-free core (layers 01–04). This is a checklist, not a procedure — the
authoritative detail lives in `CLAUDE.md` ("Core engineering constraints" + "Architecture — five
numbered layers") and `PRESERVATION_AND_ARCHITECTURE.md`; consult those when a rule needs depth.

## Layer DAG (acyclic, downward-only)

- Dependencies flow **strictly downward**: a lower-numbered layer never references a higher one.
  `01 ← 02 ← 03 ← 04 ← 05`. Wired refs are listed in `CLAUDE.md`.
- **Engine-free below 05:** no `using Godot;` anywhere in layers 01–04. The core stays reusable by a
  future headless server and unit-testable without an engine.
- `Assets.Parsers` carries **no rendering dependency**; `Assets.Mapping` is the **only** bridge to
  modern formats (glTF/PNG). Keep that boundary.

## Identifiers & types

- **Strongly-typed IDs** as `readonly record struct` in `Shared.Kernel`, e.g.
  `public readonly record struct PlayerId(Guid Value);`. Never pass raw `Guid`/`int` across APIs.
- Use **primary constructors** for terse value/service types; **collection expressions** (`[..]`) for
  array/span/collection init.
- Use **`ref struct`** for transient, stack-only network/parse contexts (reader/writer cursors over a
  buffer) so they can't escape to the heap.

## Wire & asset structs

- `[StructLayout(LayoutKind.Sequential, Pack = 1)]` on every wire/asset struct; fixed buffers via C#
  `[InlineArray]` — **no managed strings on the wire**.
- Decode text fields explicitly from bytes (CP949, below); don't model them as `string` members.

## Zero-alloc hot paths

- Operate on `Span<byte>` / `ReadOnlyMemory<byte>` slices. **No** heap allocations, **no** LINQ,
  **no** closures, **no** boxing on hot paths (socket → framing → crypto → routing → handlers).
- In-place mutation over copying (e.g. decrypt over a `Span<byte>`).

## Text (CP949)

- All legacy game text is **CP949** (Korean). Register the provider **once** at startup:
  `Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);` then
  `Encoding.GetEncoding(949)`. Never assume UTF-8/ASCII for legacy strings.

## Logging

- Source-generated `[LoggerMessage]` logging defined in `Shared.Diagnostics`. No string-interpolated
  log calls on hot paths.

## Citations & firewall (the Ground-Truth Doctrine)

- **The committed `Docs/RE/` specs are the DERIVED truth** — the rewritten, firewall-clean record of
  what IDA proved about `doida.exe` — and the **only** thing this code reads. Every layout, stride,
  opcode, and magic constant here traces back to a spec, which traces back to the binary.
- Every magic constant / byte offset cites its source spec inline: `// spec: Docs/RE/formats/terrain.md`.
  An uncited constant is unverified — find its spec or do not write it.
- **Never invent a missing fact.** If a value is absent, ambiguous, or contradicted by behavior, the
  code is NOT the place to guess it — **escalate to RE** (the spec authors / IDA analysts settle it in
  IDA, the binary wins, the spec is corrected). C# is measured against IDA + specs, never the reverse.
- Reimplement from neutral `Docs/RE/` specs only. **Never** paste decompiler output (`sub_*`, `loc_*`,
  `_DWORD`, `__thiscall`, mangled names) or copyrighted bytes into any committed file.
