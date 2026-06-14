# CLAUDE.md

This file guides Claude Code (claude.ai/code) when working in this repository. It is the master onboarding doc — read it first, then `PRESERVATION_AND_ARCHITECTURE.md` for the deep blueprint.

## What This Project Is

A clean-room, open-source **fan revival** of **Martial Heroes** (originally *D.O. Online*), an Asian martial-arts MMORPG that ran ~2003/2004–2008 and died when its servers shut down. The goal is to make it playable again in Europe.

The legacy 32-bit MSVC client (`Main.exe`) is reverse-engineered in IDA Pro **purely to document** packet layouts, opcodes, and asset/file formats. Nothing from the binary is copied. Everything is re-implemented fresh in **.NET 10 / C# 14**, with a **Godot 4.6.3-mono** presentation layer.

**Legal basis:** EU Software Directive **2009/24/EC Art. 6** (decompilation permitted solely to achieve interoperability). This is the project's legal backbone and the reason the clean-room firewall below is non-negotiable.

- **Repo root:** `C:/Users/Arius/RiderProjects/MartialHeroes`
- **Solution:** `MartialHeroes.slnx` (the new XML `.slnx` format)
- **Real client VFS (user-supplied, never committed):** project-local `05.Presentation/MartialHeroes.Client.Godot/clientdata/` (`data.inf` + `data/data.vfs`, gitignored — see `clientdata/README.md`); legacy fallback `D:/MartialHeroesClient`. Resolution order (`Dev/ClientPathResolver.cs`): `MH_CLIENT_DIR` env → `client_dir.cfg` → `clientdata/` → external installs.
- **Authoritative blueprint:** `PRESERVATION_AND_ARCHITECTURE.md`

### Non-distribution rules (non-negotiable)

- Never commit original game assets, binaries, or captures. Gitignored: `*.pak`, `*.vfs`, `*.exe`, `*.dll`, `*.pcapng`, `*.tsv`, `*.scr`, `*.mot`, `*.ted`, `*.bud`, client `*.png`, and `Main.exe`. Keep user-supplied originals outside the tree (e.g. `D:/MartialHeroesClient`, `/LegacyClient/`).
- This is a labour-of-love preservation project. Keep everything professional, accurate, and self-documenting.

## Build & Test Commands

```powershell
dotnet build MartialHeroes.slnx          # build everything (needs a .NET 10 SDK; net10.0 TFM)
dotnet test MartialHeroes.slnx           # run all xUnit tests (xUnit is the mandated framework)
dotnet test --filter "FullyQualifiedName~MyTestClass.MyTest"   # a single test
```

The Godot client (`05.Presentation/MartialHeroes.Client.Godot/`) is opened/run via the **Godot 4.6.3-mono** editor (Forward Plus, Jolt physics, D3D12 on Windows). Its `.godot/` directory is editor cache — gitignored, never commit it. See **Headless Verify Loop** below to run it without the editor.

## Clean-Room Firewall (`Docs/RE/`) — the legal backbone

A strict **dirty → spec → engineer** pipeline keeps decompiler output out of the shipped code:

1. **Dirty-room RE** (IDA analysts) writes **only** to `Docs/RE/_dirty/` — gitignored, tainted, never shipped.
2. **Spec-author agents rewrite** (never copy) those findings into committed, neutral specs:
   - `Docs/RE/opcodes.md` — opcode catalogue (no addresses)
   - `Docs/RE/packets/*.yaml` — wire-field specs
   - `Docs/RE/formats/*.md` — asset/file formats
   - `Docs/RE/structs/*.md` — struct offset tables
   - `Docs/RE/specs/*.md` — subsystem behaviour (combat, login, quests, …)
3. **Implementation agents read only the clean specs.** They never touch `_dirty/` or IDA.

Hard rules:
- **Never paste IDA / Hex-Rays pseudo-C** (`sub_xxxx`, `loc_xxxx`, `_DWORD`, `__thiscall`, `*(_DWORD*)…`, mangled names) into any committed file or any C#.
- Every magic constant / byte offset in C# cites its source spec: `// spec: Docs/RE/formats/terrain.md`.
- `Docs/RE/journal.md` (provenance audit trail) and `Docs/RE/names.yaml` (canonical glossary) are **orchestrator-owned** — don't edit them as an authoring agent.

See `Docs/RE/README.md`. Current spec inventory: ~20 packet YAMLs, ~14 format docs, ~6 struct tables, ~14 subsystem specs.

## Architecture — five numbered layers, downward-only DAG

Five numbered layer folders, mirrored as solution folders in `MartialHeroes.slnx`. **Dependencies flow strictly downward** — a lower-numbered layer never references a higher one (acyclic).

| Layer | Projects | Role |
|---|---|---|
| `01.Infrastructure.Shared` | `Shared.Kernel`, `Shared.Diagnostics` | Primitives, enums, strongly-typed IDs; source-generated logging |
| `02.Network.Layer` | `Network.Abstractions`, `Network.Protocol`, `Network.Crypto`, `Network.Transport.Pipelines` | Transport contracts; packet struct layouts + opcode routing; in-place decryption; `System.IO.Pipelines` socket I/O |
| `03.Storage.Assets` | `Assets.Vfs`, `Assets.Parsers`, `Assets.Mapping` | Memory-mapped `.pak`/VFS; binary decoders (mesh/terrain/anim/texture); conversion to glTF/PNG |
| `04.Client.Core` | `Client.Domain`, `Client.Application`, `Client.Infrastructure` | Pure deterministic game-state/formulas; use cases + packet handlers + `Channels` event buses; SQLite/local config |
| `05.Presentation` | `Client.Godot` (Godot project) | Strictly passive rendering — **zero game-rule authority** |

**Engine-free below 05:** nothing in layers 01–04 may contain `using Godot;`. The whole core is rendering-free and engine-free so it can be reused by a future headless server (`MartialHeroes.Server.Console`) and unit-tested without an engine.

**Project references (wired and acyclic):** `Abstractions`/`Protocol`/`Crypto` → `Kernel`; `Transport.Pipelines` → `Abstractions`; `Parsers` → `Vfs`; `Mapping` → `Parsers`; `Domain` → `Kernel`; `Application` → `Domain` + `Network.Abstractions`; `Infrastructure` → `Application`; `Client.Godot` → `Application` + `Assets.Mapping`.

**Intended zero-allocation data path:** socket → `Transport.Pipelines` (`PipeReader` framing) → `Crypto` (in-place `Span<byte>` mutation) → `Protocol` (source-generated opcode→handler routing, no reflection) → `Application` → `Domain` → Godot node updates next frame.

### Core engineering constraints

- **Strongly-typed IDs** as `readonly record struct` (e.g. `PlayerId(Guid Value)`) in `Shared.Kernel`. `[LoggerMessage]` source-generated logging in `Shared.Diagnostics`.
- **Wire structs:** `[StructLayout(LayoutKind.Sequential, Pack = 1)]` with C# `[InlineArray]` fixed buffers — no managed strings in wire structs.
- **Zero-alloc hot paths:** operate on `Span<byte>` / `ReadOnlyMemory<byte>` slices; no heap allocations, no LINQ, no closures on hot paths.
- **All game text is CP949** (Korean). Register it once: `Encoding.RegisterProvider(CodePagesEncodingProvider.Instance)` then `Encoding.GetEncoding(949)`.
- `Assets.Parsers` stays free of any rendering dependency; `Assets.Mapping` is the only bridge to modern formats (glTF/PNG).

### Current implementation state

The core (layers 01–04) is built and covered by **10 xUnit test projects** (one per core library, under `tests/`, registered in the `/Tests/` slnx folder). The Godot client (layer 05) is present and renders — see below. RE specs are populated. This is well past the original greenfield skeleton.

> Note on drift: the blueprint mentions `Network.Transport.Pipe`; the real project is `MartialHeroes.Network.Transport.Pipelines`. **Disk reality always wins** over blueprint naming.

## Godot Pipeline — current state (commit `c266e7e`)

### What works
- **Textured multi-texture terrain** (per-patch 16×16), area-aware multi-sector streaming.
- **Populated walled town** (area 2: 779 buildings + 40 monsters/NPCs).
- **Camera:** free / orbital.
- **HUD:** inventory (key `I`), skills (key `K`); click-to-move + WASD via `PlayerController`.
- **Character:** upright, textured humanoid player.

### Debts (known, unfinished)
1. **Character skinning explodes the mesh** — the legacy bind/weight convention isn't recovered yet, so the avatar is rendered **static** (no animation).
2. **NPCs spawn at a fallback Y** before async terrain finishes loading (ground placement race).
3. **`EnvironmentNode` is too dark** (atmosphere/lighting needs tuning).
4. **Water is unwired.**

### Recovered asset mappings (the chains that make the world render)
- **Terrain texture:** cell `.ted` `TextureIndexGrid` byte → cell `.map` `TERRAIN/BUILDING TEXTURES[idx-1].intTexId` → `bgtexture.txt[id]` → `data/map000/texture/<rel>.dds`. Textures are **global under `map000`** for all areas.
- **Character skin:** `.skn` `IdA` → `data/char/skin.txt` col4 → col5 `tex_id` → `data/char/tex{512512|10241024|…}/{id}.png`.
- **Character skeleton (CORRECTED — there is NO `g{IdB}.bnd` rule):** IDA proved the binary has no `g%d.bnd` printf. Skeletons are pre-loaded by NAME from `data/char/bind/bindlist.txt` (only `g1..g4.bnd` exist: Musa/Salsu/Dosa/Monk), registered into a pose pool, then SELECTED via the AnimCatalog visual map keyed by the appearance slot `IdB = 5·(class + 4·variant) − 24 ∈ {1,11,16,26}` → the skeleton handle in that catalog record. `classGroup` 6/11 is only an outfit/texture-family tag inside the overlay skin gids — it never picks a skeleton, which is why `g6.bnd`/`g11.bnd` don't exist. Idle motion via `data/char/actormotion.txt` (col2 == IdB → col16) → `data/char/mot/g{id}.mot`. spec: `Docs/RE/specs/frontend_scenes.md`, `_dirty/campaign4/charselect3d/skeleton-resolution.md`.
- **Mob → skin:** `mob_id` → `actormotion.txt` col1 → col2 `skin_class` → the `.skn` whose `IdB == skin_class`; the skeleton resolves through the same catalog/IdB lookup above (not a literal `g{skin_class}.bnd`).
- **Spawns:** `npc{tag}.arr` = 28-byte records; `mob{tag}.arr` = 20-byte records.
- **Collision:** `.sod` = 2D XZ wall segments (ray-parity point-in-polygon). Ground height from `.ted` bilinear interpolation.

### Coordinate conventions (get these wrong and the world mirrors)
- **World geometry negates Z** — `Helpers/WorldCoordinates.ToGodot`: `(x,y,z) → (x,y,-z)`.
- **Mesh-local `.skn` geometry negates X.**
- Cells are **1024 units**, on a **65×65** grid, spacing **16**.

## Headless Verify Loop (verify without the user)

Run the Godot **console** exe headless to check scripts/assets and capture all `GD.Print`/errors to stdout, no editor needed:

```powershell
& "C:/Users/Arius/Desktop/Godot_v4.6.3-stable_mono_win64/Godot_v4.6.3-stable_mono_win64_console.exe" `
  --headless --path "05.Presentation/MartialHeroes.Client.Godot" --quit-after 150
```

For a **real screenshot**, run **windowed** with a temporary GDScript autoload that does:
`get_viewport().get_texture().get_image().save_png("…")`. A GDScript autoload is the most reliable in-engine probe.

## Reverse-Engineering & MCP Tooling

Two local MCP servers are registered in the committed root `.mcp.json`. Both connect only when their host app is open **and** a fresh Claude session starts. Discover tool names at runtime (they're deferred).

- **IDA Pro MCP** — live analysis of `Main.exe` (tools `mcp__ida__*`). Reachable only when IDA is open on the database. Run `/ida-mcp-connect` to verify before RE work. **Two endpoints expose DIFFERENT toolsets — prioritize `?ext=dbg`:**
  - `http://127.0.0.1:13337/mcp` — base endpoint (static-analysis tools only).
  - `http://127.0.0.1:13337/mcp?ext=dbg` — **debugger-extended endpoint**: surfaces the debugger tools (`mcp__ida__dbg_*`) **in addition to** the static tools. The committed `.mcp.json` registers THIS one — it is a superset (static recovery *and* dynamic confirmation from one connection). If the `dbg_*` tools are absent, the session is on the wrong (base) endpoint. Re-register on `?ext=dbg`:
  ```powershell
  claude mcp add --transport http ida "http://127.0.0.1:13337/mcp?ext=dbg"
  ```
  Static analysis forms the hypothesis; the IDA **debugger** (run the real client, breakpoint, read registers/memory, step) confirms it against ground truth. Both modes are dirty-room: findings cross the firewall only as neutral prose — see `Docs/CAMPAIGN_TEMPLATE.md` §0.3/§4.4.
- **Godot MCP** (`slangwald/godot-mcp`, registered as `godot`) — **editor tools on port 9600** (`get_scene_tree`, `run_project`, `get_output`, `modify_node`, …) and **game tools on port 9601** (`screenshot`, `click`, `get_runtime_tree`, …). Tool names are `mcp__godot__*`. Connects only with the Godot editor open + a fresh session.

## Known Godot Pitfalls (each cost real time — heed them)

- **`.tscn` script binding must be a PROPERTY LINE**, not a header attribute. Use `script = ExtResource("1")` under the node header. Writing `[node … script=ExtResource(...)]` is **silently ignored** → node has no script → no `_Ready` → gray screen.
- **Namespace collision:** inside `namespace MartialHeroes.Client.Godot.*`, a bare `Input.` / `Environment.` / `Time.` resolves to the sibling project namespace, not the Godot class → `CS0234`. Use **`global::Godot.Input`**, `global::Godot.Environment`, etc.
- **`GltfDocument.AppendFromBuffer` crashes natively** on this project's generated GLBs. **Never use it.** Build Godot `ArrayMesh` directly (`BudMeshBuilder` / `SknMeshBuilder` pattern).

## Tooling Map (`.claude/`)

This repo ships a large shared Claude Code setup under `.claude/` (committed via `.gitignore` negations; only `settings.local.json` and `hooks/state/` stay local). **53 agents, 53 skills, 27 hooks** (counts as of 2026-06-13; the bullet lists below are *representative*, not exhaustive — `ls .claude/agents/` and `.claude/skills/` are the source of truth). The authoritative kit design — the **orchestrator fleet**, the per-role **`model` + `effort`** policy, and the **agent↔skill linking fabric** (`skills:` preload + knowledge-skill `paths:`) — lives in **`.claude/KIT.md`**; read it before authoring or refining any agent/skill/hook. Every agent declares an explicit `model:` (`opus` for judgement/orchestration, `sonnet` for execution) and `effort:` (`high` for orchestrators/judgement/precision, `medium` for mechanical).

### Orchestration doctrine — prefer Tier-2 Orchestrator-Agents for big / simultaneous work

For any multi-lane objective (a research wave, a per-cluster sweep, a staged engineering pipeline, several independent goals at once), **route through a Tier-2 Orchestrator-Agent rather than driving every lane from the main session**. An Orchestrator-Agent owns one big block, fans out its own single-deliverable Tier-3 sub-agents, maintains the file-ownership ledger (one writer per path per wave), and reports **one** rolled-up result. This is the proven way to run wide and stay safe — see `Docs/CAMPAIGN_TEMPLATE.md` §2 (the three command tiers) and §3 (concurrency).

- **Use Opus 4.8 for the orchestrators** (judgment-heavy decomposition / reconciliation / gating); workers may run a lighter model where the task is mechanical.
- **Two levels of orchestration max** (Tier-1 main session → Tier-2 Orchestrator-Agent → Tier-3 worker). A Tier-2 agent never spawns another Tier-2.
- **IDA fan-out is capped at sub-waves of ~3** (single IDB, MCP saturation); **WRITE to the IDB is strictly serialized — exactly one writer at a time**.
- **The active campaign** is tracked in `Docs/PLAN.md` (method/charter) + `Docs/ROADMAP.md` (live run record), specialising `Docs/CAMPAIGN_TEMPLATE.md`. **CAMPAIGN 3** continues the `doida.exe` reverse (workflow / UI-UX / VFS) end-to-end: dirty recovery → clean specs → IDB annotation → wired client. The Campaign-2 comprehension→annotation apparatus (`re-comprehension-orchestrator`, `re-annotation-orchestrator`, `re-ida-annotator`, `/ida-annotate-batch`) is reused for its IDB phases. Prior cycles live in git history + `Docs/RE/journal.md`.

### Hooks — `.claude/hooks/` (Python, std-lib only, **advisory-only & fail-open**; they warn / inject context, never block)
| Hook | When it fires |
|---|---|
| `session_primer`, `re_intent_primer` | Orientation on session/prompt start |
| `clean_room_guard` | Flags pasted decompiler-shaped code |
| `protect_artifacts` | Flags committing copyrighted binaries/captures |
| `layer_dependency_guard` | Flags upward refs / `using Godot;` in core layers |
| `cs_post_edit` | Zero-alloc + `StructLayout` nudges (opt-in build check via `MH_BUILD_ON_EDIT=1`) |
| `re_provenance_logger` | Hashes IDA output for the audit trail |
| `memory_persist_nudge`, `stop_loose_ends` | Persist-knowledge (Stop/PreCompact) / loose-end reminders |
| `agent_md_guard`, `skill_md_guard`, `hook_advisory_guard`, `settings_wiring_nudge` | Keep the `.claude/` kit self-consistent (valid agent/skill frontmatter, advisory-only hooks, sane settings wiring) |
| `_hooklib.py` | Shared helpers — `import _hooklib as h` |

### Skills (`/name`)
- **RE / IDA** (run IDAPython via the MCP, write only `_dirty/`): `ida-recon`, `ida-decompile-export`, `ida-struct-recovery`, `ida-struct-apply`, `ida-vtable-recover`, `ida-opcode-map`, `ida-naming-sync`, `ida-rename-batch`, `ida-annotate-batch`, `ida-py`, `ida-script-runner`, `ida-crypto-hunt`, `ida-xref-map`, `ida-callgraph-map`, `ida-data-flow`, `ida-batch-analyze`, `ida-string-hunt`, `ida-mcp-connect`.
  - `ida-annotate-batch` is the **Campaign 2 IDB-write applier** — dry-run → apply a rename+comment+type manifest slice from `_dirty/campaign2/glossary.yaml`; idempotent; firewall-safe (IDB never commits).
- **Protocol / captures:** `pcap-extract`, `packet-diff`, `opcode-catalog`, `packet-codegen`.
- **Assets:** `pak-explore`, `asset-format-doc`.
- **Scaffolding:** `new-layer-project`, `wire-references`, `add-test-project`, `godot-csproj-bootstrap`.
- **Quality / docs:** `re-workspace-init`, `clean-room-audit`, `clean-room-firewall-check`, `re-session-log`, `preservation-readme`, `spec-citation-audit`, `memory-curate`.
- **Knowledge** (auto-load via `paths:`, `user-invocable: false` — they surface conventions without being invoked): `dotnet-csharp14` (C#14/.NET10 core conventions), `godot-engine` (Godot 4.6 pitfalls + coordinate conventions), `ida-pro-re` (RE methodology + clean-room firewall), `martial-heroes-domain` (recovered protocol/asset-chain index).
- **North-star (the two goals — N1 live RE, N2 1:1 port):** `ida-debugger-drive` (pilot the **live** IDA debugger to confirm a static hypothesis against ground truth — never `dbg_start`), `godot-fidelity-check` (verify the Godot client renders/behaves **1:1** vs the original), `asset-chain-trace` (walk an asset id through its recovered mapping chain to the on-disk VFS file).

### Agents (`@name`)
- **Orchestrators** (Tier-2, hold the `Agent` tool, **`opus` + `effort: high`**, each with a linked roster — see `.claude/KIT.md` §2): **per-lane** — `re-cleanroom-orchestrator` (general dirty→spec RE), `network-stack-orchestrator` (layer 02), `assets-pipeline-orchestrator` (layer 03), `client-core-orchestrator` (layer 04), `godot-client-orchestrator` (layer 05), `tooling-orchestrator` (the `.claude/` kit itself), `quality-gate-orchestrator` (pre-commit validation); **Campaign 2** — `re-comprehension-orchestrator` (READONLY comprehension), `re-annotation-orchestrator` (serialized IDB WRITE, Tier-3 worker `re-ida-annotator`). For a single-deliverable task, delegate straight to the worker; route an orchestrator only for a multi-lane objective.
- **Dirty-room RE** (have `mcp__ida__*`, write only `_dirty/`): `re-static-analyst`, `re-protocol-analyst`, `re-crypto-analyst`, `re-struct-cartographer`, `re-asset-format-analyst`, `re-animation-analyst`, `re-analyst` (one-off generalist), `vfs-data-analyst`, `ida-script-author`.
- **Spec authors** (the dirty→clean bridge): `protocol-spec-author`, `asset-spec-author`.
- **Clean-room engineers** (one per project, **no IDA**): `kernel-`, `network-abstractions-`, `network-protocol-`, `network-crypto-`, `network-transport-`, `assets-vfs-`, `assets-parser-`, `assets-mapping-`, `domain-`, `application-`, `client-infrastructure-`, `dotnet-engineer` (cross-layer generalist), `data-tables-engineer` (CP949 tables→typed C#), `godot-presentation-engineer`, `godot-ui-engineer`, `godot-input-engineer`, `godot-skinning-specialist`, `godot-shader-specialist`.
- **Quality:** `clean-room-auditor`, `architecture-guardian`, `perf-reviewer`, `csharp-reviewer`, `csharp-modernizer` (C#14/.NET10 idiom refactors), `test-engineer`, `build-doctor`, `preservation-archivist`, `godot-render-reviewer`, `tooling-auditor`.
- **Meta-authors** (grow/maintain the fleet): `agent-author`, `skill-author`, `hook-author`; plus `godot-mcp-operator` (drives the live Godot editor/game).

## Commit Discipline

- **Commit only when the user explicitly asks.** If on the default branch, branch first.
- **Never commit originals:** `*.pak`, `*.vfs`, `*.exe`, `*.dll`, `*.pcapng`, `*.tsv`, `*.scr`, `*.mot`, `*.ted`, `*.bud`, client `*.png`, `Main.exe`, anything under `_dirty/`, or the Godot `.godot/` cache.
- Don't edit orchestrator-owned files as an authoring agent: `settings.json`, `.mcp.json`, `Docs/RE/journal.md`, `Docs/RE/names.yaml`.
