# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What This Project Is

A clean-room, open-source reconstruction of **Martial Heroes** (originally *D.O. Online*), an Asian martial-arts MMORPG that ran 2004–2008 and died with its servers. The legacy 32-bit client (`Main.exe`) is reverse-engineered with IDA Pro purely to document packet layouts, opcodes, and asset formats; everything here is re-implemented from scratch in **.NET 10 / C# 14** with a **Godot 4.6** presentation layer.

**`PRESERVATION_AND_ARCHITECTURE.md` is the authoritative blueprint** — legal framework (EU interoperability exception), per-project technical mandates, and the zero-allocation pipeline. Read it before significant architectural work.

### Clean-room and non-distribution rules (non-negotiable)

- Never copy or transcribe decompiled C++ pseudo-code from the legacy binary. Document the format/protocol, then implement fresh.
- Never commit original game files or network captures. `*.pak`, `*.pcapng`, `*.tsv`, `Main.exe`, `*.exe`, `*.dll`, and `/LegacyClient/` are all gitignored; keep user-supplied originals in `/LegacyClient/`.

## Commands

```powershell
dotnet build MartialHeroes.slnx          # build everything (needs a .NET 10 SDK: net10.0 + .slnx format)
dotnet test MartialHeroes.slnx           # run all tests (xUnit is the mandated framework)
dotnet test --filter "FullyQualifiedName~MyTestClass.MyTest"   # single test
```

The Godot client (`05.Presentation/MartialHeroes.Client.Godot/`) is opened/run via the Godot 4.6 editor (Forward Plus, Jolt physics, D3D12 on Windows). Its `.godot/` directory is editor cache — gitignored, never commit it.

## Current State (June 2026 — greenfield skeleton)

The architecture below is blueprint intent, not implemented reality:

- All 12 class libraries contain only a placeholder `Class1.cs`. **No `ProjectReference`s are wired yet** — add them per the dependency map below as you implement.
- The Godot project exists (`project.godot`) but has no generated `.csproj` yet and is not referenced in `MartialHeroes.slnx` (its solution folder is empty). It gets a csproj once a C# script is attached in Godot.
- No test projects exist yet (the `add-test-project` skill scaffolds them). `Docs/RE/` holds the clean-room RE knowledge base and firewall (see below).
- Naming drift: the blueprint says `Network.Transport.Pipe`; the real project is `MartialHeroes.Network.Transport.Pipelines`. Disk reality wins.

## Target Architecture

Five numbered layer folders, mirrored as solution folders in `MartialHeroes.slnx`. Dependencies flow strictly downward (lower numbers never reference higher ones):

| Layer | Projects | Role |
|---|---|---|
| `01.Infrastructure.Shared` | `Shared.Kernel`, `Shared.Diagnostics` | Primitives, enums, strongly-typed IDs; source-generated logging |
| `02.Network.Layer` | `Network.Abstractions`, `Network.Protocol`, `Network.Crypto`, `Network.Transport.Pipelines` | Transport contracts; packet struct layouts + opcode routing; in-place decryption; `System.IO.Pipelines` socket I/O |
| `03.Storage.Assets` | `Assets.Vfs`, `Assets.Parsers`, `Assets.Mapping` | Memory-mapped `.pak` virtual filesystem; binary decoders (mesh/terrain/anim); conversion to glTF/PNG |
| `04.Client.Core` | `Client.Domain`, `Client.Application`, `Client.Infrastructure` | Pure deterministic game-state/formulas; use cases + packet handlers + `Channels` event buses; SQLite/local config |
| `05.Presentation` | `Client.Godot` (Godot project, not a classic csproj) | Strictly passive rendering — zero game-rule authority |

The intended data path is a zero-allocation pipeline: socket → `Transport.Pipelines` (`PipeReader` framing) → `Crypto` (in-place `Span<byte>` mutation) → `Protocol` (source-generated opcode→handler routing, no reflection) → `Application` → `Domain` → Godot node updates on next frame.

Core design constraints from the blueprint:

- Everything below layer 05 is rendering-free and engine-free, so the whole core can be reused by a future server (`MartialHeroes.Server.Console`) and tested headlessly.
- Strongly-typed IDs as `readonly record struct` (e.g. `PlayerId(Guid Value)`) in `Shared.Kernel`; `[LoggerMessage]` source-generated logging in `Shared.Diagnostics`.
- Packets: `[StructLayout(LayoutKind.Sequential, Pack = 1)]` with C# `[InlineArray]` fixed buffers — no managed strings in wire structs.
- Crypto/parsing operate on `Span<byte>`/`ReadOnlyMemory<byte>` slices; no heap allocations on hot paths.
- `Assets.Parsers` must stay free of any rendering dependency; `Assets.Mapping` is the only bridge to modern formats.

Intended project references (per blueprint): `Abstractions`/`Protocol`/`Crypto` → `Kernel`; `Transport.Pipelines` → `Abstractions`; `Parsers` → `Vfs`; `Mapping` → `Parsers`; `Domain` → `Kernel`; `Application` → `Domain` + `Network.Abstractions`; `Infrastructure` → `Application`; `Client.Godot` → `Application` + `Assets.Mapping`.

## Reverse-Engineering Tooling

An IDA Pro MCP server runs locally at `http://127.0.0.1:13337/mcp` when IDA is open, exposing the live analysis of the legacy client (tools namespaced `mcp__ida__*`). It is registered for Claude Code via the committed root `.mcp.json`. If the tools are missing (e.g. a headless run that skipped the file), register it once with:

```powershell
claude mcp add --transport http ida http://127.0.0.1:13337/mcp
```

The server is only reachable when IDA is open on the database. Run `/ida-mcp-connect` to verify connectivity before RE work.

## Claude Code Tooling

This repo ships a large, shared Claude Code setup under `.claude/` (committed via `.gitignore` negations; only `settings.local.json` and `hooks/state/` stay local).

**Clean-room firewall (`Docs/RE/`)** — the legal backbone. RE analysis that looks at decompiler output writes ONLY to `Docs/RE/_dirty/` (gitignored, tainted). Spec-author agents *rewrite* (never copy) those findings into committed neutral specs (`opcodes.md`, `packets/*.yaml`, `formats/*.md`, `structs/*.md`, `specs/*.md`). Implementation agents read ONLY the clean specs and never touch `_dirty/` or IDA. See `Docs/RE/README.md`.

**Hooks** (`.claude/hooks/`, Python, advisory-only — they warn/inject context, never block): `session_primer`/`re_intent_primer` (orientation), `clean_room_guard` (flags pasted decompiler code), `protect_artifacts` (flags committing copyrighted binaries), `layer_dependency_guard` (flags upward refs / `using Godot;` in core), `cs_post_edit` (zero-alloc + StructLayout nudges; opt-in build check via `MH_BUILD_ON_EDIT=1`), `re_provenance_logger` (hashes IDA output for the audit trail), `precompact_note`, `stop_loose_ends`.

**Skills** (`/name`) — RE/IDA (run IDAPython via the MCP): `ida-recon`, `ida-decompile-export`, `ida-struct-recovery`, `ida-opcode-map`, `ida-naming-sync`, `ida-script-runner`, `ida-crypto-hunt`, `ida-mcp-connect`. Protocol/captures: `pcap-extract`, `packet-diff`, `opcode-catalog`, `packet-codegen`. Assets: `pak-explore`, `asset-format-doc`. Scaffolding: `new-layer-project`, `wire-references`, `add-test-project`, `godot-csproj-bootstrap`. Quality/docs: `re-workspace-init`, `clean-room-audit`, `clean-room-firewall-check`, `re-session-log`, `preservation-readme`.

**Agents** (`@name`) — *dirty-room RE* (have `mcp__ida__*`, write only `_dirty/`): `re-static-analyst`, `re-protocol-analyst`, `re-crypto-analyst`, `re-struct-cartographer`, `re-asset-format-analyst`, `ida-script-author`. *Spec authors* (the dirty→clean bridge): `protocol-spec-author`, `asset-spec-author`. *Clean-room engineers* (one per project, no IDA): `kernel-`, `network-abstractions-`, `network-protocol-`, `network-crypto-`, `network-transport-`, `assets-vfs-`, `assets-parser-`, `assets-mapping-`, `domain-`, `application-`, `client-infrastructure-`, `godot-presentation-engineer`. *Quality*: `clean-room-auditor`, `architecture-guardian`, `perf-reviewer`, `csharp-reviewer`, `test-engineer`, `build-doctor`, `preservation-archivist`.
