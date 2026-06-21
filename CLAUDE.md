# CLAUDE.md

This file guides Claude Code (claude.ai/code) when working in this repository. It is the master onboarding doc — read it first, then `PRESERVATION_AND_ARCHITECTURE.md` for the deep blueprint.

## What This Project Is

A clean-room, open-source **fan revival** of **Martial Heroes** (originally *D.O. Online*), an Asian martial-arts MMORPG that ran ~2003/2004–2008 and died when its servers shut down. The goal is the **total reverse-engineering of the ENTIRE `doida.exe` game client and its faithful 1:1 re-creation on the Godot engine**, so the whole game is playable again in Europe.

**Mission (north-star):** reverse-engineer **all of `doida.exe`** — every subsystem, opcode, asset/file format and runtime behavior, leaving nothing un-mapped — and re-implement it **1:1 on Godot 4.6.3-mono** so the complete game runs again. Total RE coverage and full 1:1 fidelity to the original are the measure of success; the reverse runs **wide and hard** (see the unbridled-IDA doctrine below).

The legacy 32-bit MSVC client (`doida.exe` — the active target; the older `Main.exe` build is the historical reference) is reverse-engineered **in its entirety** in IDA Pro **purely to document** the whole client — packet layouts, opcodes, asset/file formats, and runtime behavior. Nothing from the binary is copied. Everything is re-implemented fresh in **.NET 10 / C# 14**, with the **Godot 4.6.3-mono** layer carrying the **1:1 presentation**.

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

## Source of truth — the Ground-Truth Doctrine (read before trusting anything)

There is exactly **one** ground truth for what the original game *is*: the legacy binary **`doida.exe`, observed through IDA Pro 9.3 + the IDA MCP** (static analysis **and** the `?ext=dbg` live debugger). Every fact we rely on, and everything we build, is measured against it. Three tiers, in strict order of authority:

1. **IDA / `doida.exe` — the single absolute truth (behavior, data, layout).** Whenever a question about the original's wire protocol, struct layout, asset format, opcode, constant, or runtime behavior is open or disputed, **the answer is whatever the binary says — confirm it in IDA, never from memory, analogy, or "what seems reasonable."** Static analysis forms the hypothesis; the **live debugger confirms it against ground truth** (the maintainer F9-launches the client; you pilot the live session — **never `dbg_start`**). If the IDA MCP is down or the wrong/empty DB is loaded, **STOP and report — never fabricate IDA output.** The reverse runs **unbridled** (massively parallel reads *and* IDB writes; retry on conflict).
2. **`Docs/RE/` specs — the committed derived truth.** The clean specs — **`Docs/RE/formats/`, `Docs/RE/packets/`, `Docs/RE/structs/`, `Docs/RE/specs/`** (+ `opcodes.md`) — are the **rewritten, firewall-clean** record of what IDA proved, and the **only** thing implementation is allowed to read. They are authoritative *because* they were derived from the binary: when a spec and `doida.exe` disagree, **the binary wins, the spec is corrected, and the change is journaled.** Treat these four trees as the load-bearing, **hyper-important** knowledge base and keep them in sync with the binary.
3. **C# / Godot — measured against (1) and (2), never the reverse.** The .NET 10 core and the Godot port are *faithful re-creations*; their correctness is judged by fidelity to IDA + the specs. The code is **never its own source of truth** — if it diverges from the spec, the code is wrong (unless the spec is precisely what IDA just disproved). **One exception, for rendered pixels only:** the official screenshots/captures are the visual oracle, and **oracle > spec** for how a scene actually *looks* (a spec-faithful render can still diverge from the real client). IDA + specs govern behavior and data; the captures govern the final image.

**`Docs/` is now a trustworthy map of what has already been done — start there, then verify against the binary.** The `Docs/RE/` specs are mature and re-verified (each carries a `verification:` banner pinned to the current IDB SHA), and `Docs/ROADMAP.md` + `Docs/RE/journal.md` + `Docs/PLAN.md` are an accurate, load-bearing record of completed work, decisions, and where to resume. Trust them to orient fast and to **avoid re-doing RE that is already settled**. They remain **derived** truth, though: the **`ida` MCP on `doida.exe` is the absolute authority on how the client actually parses and behaves** — on any open or disputed question, confirm it in IDA and let the binary correct the doc (tier 1 always overrides tier 2).

**Creed:** *Confirm it in IDA → write it in the spec → build to the spec → verify against the binary (and the visual oracle for pixels).* The clean-room firewall below is the mechanism that keeps this lawful.

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

See `Docs/RE/README.md`. The spec corpus is comprehensive — packet YAMLs, format docs, struct tables, and subsystem specs (+ `opcodes.md`), every file banner-pinned to the current IDB SHA. Read it rather than re-deriving.

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

**Project references (wired and acyclic):** `Abstractions`/`Protocol`/`Crypto` → `Kernel`; `Transport.Pipelines` → `Abstractions`; `Parsers` → `Vfs`; `Mapping` → `Parsers`; `Domain` → `Kernel`; `Application` → `Domain` + `Network.Abstractions` + `Network.Protocol` + `Network.Crypto` (Application *is* the packet-handling + login layer — its handlers consume the wire structs/opcodes and its login flow consumes the session handshake); `Infrastructure` → `Application` + `Assets.Parsers` + `Assets.Vfs` (Infrastructure builds the local catalogues from the decoded binary tables off the VFS); `Client.Godot` → `Application` + `Assets.Mapping` + `Client.Infrastructure` (the layer-05 composition root `ClientContext` wires the concrete infrastructure stores). `Shared.Diagnostics` is package-only (no project references). Every edge here is **downward and acyclic** — these are the *accepted by-design* edges the dependency checker (`/wire-references` → `check_dag.py`) enforces; they are deliberately broader than a strict "Abstractions-only" reading because the core's real responsibilities (packet handling, catalogue loading, composition) need them, and the clean-room/engine-free invariants still hold (no `using Godot;` below 05, no cycles, no upward edges). *(CAMPAIGN 11 Phase 3b: accepted + documented rather than abstracted away.)*

**Intended zero-allocation data path:** socket → `Transport.Pipelines` (`PipeReader` framing) → `Crypto` (in-place `Span<byte>` mutation) → `Protocol` (source-generated opcode→handler routing, no reflection) → `Application` → `Domain` → Godot node updates next frame.

### Core engineering constraints

- **Strongly-typed IDs** as `readonly record struct` (e.g. `PlayerId(Guid Value)`) in `Shared.Kernel`. `[LoggerMessage]` source-generated logging in `Shared.Diagnostics`.
- **Wire structs:** `[StructLayout(LayoutKind.Sequential, Pack = 1)]` with C# `[InlineArray]` fixed buffers — no managed strings in wire structs.
- **Zero-alloc hot paths:** operate on `Span<byte>` / `ReadOnlyMemory<byte>` slices; no heap allocations, no LINQ, no closures on hot paths.
- **All game text is CP949** (Korean). Register it once: `Encoding.RegisterProvider(CodePagesEncodingProvider.Instance)` then `Encoding.GetEncoding(949)`.
- `Assets.Parsers` stays free of any rendering dependency; `Assets.Mapping` is the only bridge to modern formats (glTF/PNG).

### Current implementation state

The whole clean-room client is reconstructed: the .NET core (layers 01–04) **and** the Godot client (05), which renders the world and runs the networking **1:1 live** vs the replica (login → char-select → enter-world → world render). For the **authoritative live state** — what's done, what's in flight, where to resume — read **`Docs/ROADMAP.md` + `Docs/RE/journal.md`**; the project moves fast, so don't trust a hard-coded snapshot here. xUnit is the mandated test framework for the core, but the test/project layout is being reshaped by the active re-architecture (see `Docs/ROADMAP.md`) — verify the gate (build + headless, below) rather than assuming a fixed test count.

> Note on drift: the blueprint mentions `Network.Transport.Pipe`; the real project is `MartialHeroes.Network.Transport.Pipelines`. **Disk reality always wins** over blueprint naming.

## Godot Pipeline

For the **live** state of the Godot client — what renders, what's wired, the open fidelity debts — read `Docs/ROADMAP.md` + `Docs/RE/journal.md` (it moves too fast for a static snapshot). In broad strokes the world renders (multi-texture terrain with area-aware streaming, a populated walled town, free/orbital camera, HUD with inventory `I` / skills `K`, click-to-move + WASD) and the client drives login → enter-world → world-render **live** against the replica. The durable chains and conventions that make it render are below.

### Recovered asset mappings (the chains that make the world render)
- **Terrain texture:** cell `.ted` `TextureIndexGrid` byte → cell `.map` `TERRAIN/BUILDING TEXTURES[idx-1].intTexId` → `bgtexture.txt[id]` → `data/map000/texture/<rel>.dds`. Textures are **global under `map000`** for all areas.
- **Character skin:** `.skn` `IdA` → `data/char/skin.txt` col4 → col5 `tex_id` → `data/char/tex{512512|10241024|…}/{id}.png`.
- **Character skeleton:** the deform skeleton is **`data/char/bind/g{SkinClassId}.bnd`**, where `SkinClassId ∈ {1,2,3,4}` (Musa/Salsu/Dosa/Monk) is the `.skn` **header** class — only `g1..g4.bnd` exist. (The engine pre-loads those four by name from `bindlist.txt` and selects via the AnimCatalog map keyed by the appearance-slot `IdB = 5·(class + 4·variant) − 24`; there is **no** `g11.bnd`/`g16.bnd`, and `classGroup` 6/11 is only an outfit/texture-family tag, never a skeleton.) **Idle motion = `data/char/actormotion.txt` `motion_ids_a[1]` = column 16 (record `+0x44`); column 15 / `+0x40` is statically DEAD** (binary-won, CYCLE 7 — reverses an earlier "col15" reading). `.mot` clips are registered from `motlist.txt` and keyed by the `.mot` header id — there is **no** `g{id}.mot` printf. The full deform chain (bind/weights, inverse-bind, Y-up import) is recovered. spec: `Docs/RE/specs/skinning.md`, `formats/animation.md`, `formats/skn.md`.
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

The local MCP servers are registered in the committed root `.mcp.json`. They connect only when their host app is open **and** a fresh Claude session starts. Discover tool names at runtime (they're deferred).

- **IDA Pro MCP** — live analysis of `doida.exe` (tools `mcp__ida__*`). Reachable only when IDA is open on the database. Run `/ida-mcp-connect` to verify before RE work. **Two endpoints expose DIFFERENT toolsets — prioritize `?ext=dbg`:**
  - `http://127.0.0.1:13337/mcp` — base endpoint (static-analysis tools only).
  - `http://127.0.0.1:13337/mcp?ext=dbg` — **debugger-extended endpoint**: surfaces the debugger tools (`mcp__ida__dbg_*`) **in addition to** the static tools. The committed `.mcp.json` registers THIS one — it is a superset (static recovery *and* dynamic confirmation from one connection). If the `dbg_*` tools are absent, the session is on the wrong (base) endpoint. Re-register on `?ext=dbg`:
  ```powershell
  claude mcp add --transport http ida "http://127.0.0.1:13337/mcp?ext=dbg"
  ```
  Static analysis forms the hypothesis; the IDA **debugger** (run the real client, breakpoint, read registers/memory, step) confirms it against ground truth. Both modes are dirty-room: findings cross the firewall only as neutral prose — see `Docs/CAMPAIGN_TEMPLATE.md` §0.3/§4.4.
- **Godot MCP** (`slangwald/godot-mcp`, registered as `godot`) — **editor tools on port 9600** (`get_scene_tree`, `run_project`, `get_output`, `modify_node`, …) and **game tools on port 9601** (`screenshot`, `click`, `get_runtime_tree`, …). Tool names are `mcp__godot__*`. Connects only with the Godot editor open + a fresh session.
- **x32dbg MCP** (`mcp__x32dbg__*`) — the live user-mode debugger, used to byte-prove the client's sends against the real `doida.exe` when static analysis needs runtime confirmation (complements the IDA `?ext=dbg` debugger).

## Known Godot Pitfalls (each cost real time — heed them)

- **`.tscn` script binding must be a PROPERTY LINE**, not a header attribute. Use `script = ExtResource("1")` under the node header. Writing `[node … script=ExtResource(...)]` is **silently ignored** → node has no script → no `_Ready` → gray screen.
- **Namespace collision:** inside `namespace MartialHeroes.Client.Godot.*`, a bare `Input.` / `Environment.` / `Time.` resolves to the sibling project namespace, not the Godot class → `CS0234`. Use **`global::Godot.Input`**, `global::Godot.Environment`, etc.
- **`GltfDocument.AppendFromBuffer` crashes natively** on this project's generated GLBs. **Never use it.** Build Godot `ArrayMesh` directly (`BudMeshBuilder` / `SknMeshBuilder` pattern).

## Tooling Map (`.claude/`)

This repo ships a shared Claude Code setup under `.claude/` (committed via `.gitignore` negations; only `settings.local.json` and `hooks/state/` stay local), rationalized into **five domain orchestrators** (Planning · Reverse Engineering · C# Porting [00→04 + Tools] · Godot [05] · Documentation & Tooling): **31 agents, 34 skills, 12 hook files** (11 advisory hooks + `_hooklib`). The bullet lists below are *representative* — `ls .claude/agents/` and `.claude/skills/` are the source of truth. The authoritative kit design — the **5 domain orchestrators**, the per-role **`model` + `effort`** policy, and the **agent↔skill linking fabric** (`skills:` preload + knowledge-skill `paths:`) — lives in **`.claude/KIT.md`**; read it before authoring or refining any agent/skill/hook. Every agent declares an explicit `model:` (`opus` for judgement/orchestration, `sonnet` for execution) and `effort:` (`high` for orchestrators/judgement/precision, `medium` for mechanical).

### Orchestration doctrine — prefer Tier-2 Orchestrator-Agents for big / simultaneous work

For any multi-lane objective (a research wave, a per-cluster sweep, a staged engineering pipeline, several independent goals at once), **route through a Tier-2 Orchestrator-Agent rather than driving every lane from the main session**. An Orchestrator-Agent owns one big block, fans out its own single-deliverable Tier-3 sub-agents, maintains the file-ownership ledger (one writer per path per wave), and reports **one** rolled-up result. This is the proven way to run wide and stay safe — see `Docs/CAMPAIGN_TEMPLATE.md` §2 (the three command tiers) and §3 (concurrency).

- **Plan a big initiative with the Planning domain first.** For a multi-phase mandate, route to `planning-orchestrator` (works *with* plan mode): it deeply **reformulates** the request, decomposes it into a hierarchical TODO/roadmap tree, detects knowledge gaps, and **pre-wires which domain orchestrator + agents + skills each phase needs** — emitting an approve-ready plan (the `/plan-campaign` skill). It **PLANS** the routing; once you approve, the **main session (Tier-1) executes** by invoking the named domain orchestrators (two-levels-max preserved). See `.claude/KIT.md` §2.
- **Use Opus 4.8 for the orchestrators** (judgment-heavy decomposition / reconciliation / gating); workers may run a lighter model where the task is mechanical.
- **Two levels of orchestration max** (Tier-1 main session → Tier-2 Orchestrator-Agent → Tier-3 worker). A Tier-2 agent never spawns another Tier-2.
- **IDA runs UNBRIDLED — fan out hard.** Read analysts run **massively parallel** and IDB **writes run in parallel too**: there is **no `~3` sub-wave cap and no one-writer-at-a-time rule** anymore. Push the reverse of the whole `doida.exe` as wide and fast as the IDA MCP server sustains; if a call fails or conflicts, **retry it** rather than throttling back. (The clean-room firewall, the dry-run→apply discipline, and idempotency all still hold — only the throughput throttle is lifted. The single real ceiling is whatever the live IDA MCP server can absorb, not a policy number.)
- **The active campaign** is tracked in `Docs/PLAN.md` (method/charter) + `Docs/ROADMAP.md` (live run record), specialising `Docs/CAMPAIGN_TEMPLATE.md` — read those for the current cycle and where to resume (don't rely on a campaign number hard-coded here). The RE domain's IDB-annotation path (`re-orchestrator` → `ida-toolsmith` with the `/ida-annotate` skill) handles its IDB phases. Prior cycles live in git history + `Docs/RE/journal.md`.

### Hooks — `.claude/hooks/` (Python, std-lib only, **advisory-only & fail-open**; they warn / inject context, never block)
| Hook | When it fires |
|---|---|
| `session_primer` | Orientation context on session start |
| `prompt_primer` | RE-intent + Godot-render-state + planning-mode context on each user prompt |
| `firewall_guard` | Flags pasted decompiler code / committing copyrighted artifacts / risky git / IDA provenance (PreToolUse) |
| `layer_dependency_guard` | Flags upward refs / `using Godot;` in core layers |
| `cs_post_edit` | `StructLayout` + slnx-sync + breadcrumb nudges (opt-in build check via `MH_BUILD_ON_EDIT=1`) |
| `csharp_guard` | CP949 / zero-alloc / `// spec:` citation (+ broken-citation) / test-after-core-edit nudges |
| `godot_guard` | `.tscn` script-line / namespace / `GltfDocument` / uid / coordinate / layer-05 authority-leak nudges |
| `kit_guard` | Keeps `.claude/` self-consistent (agent/skill frontmatter, advisory-only hooks, settings wiring) |
| `python_tooling_lint` | `ast.parse` on edited `Tools/**` + `.claude/hooks/*.py` (syntax-error advisory) |
| `re_provenance_logger` | Hashes IDA output for the audit trail |
| `session_end` | Loose-end + persist-knowledge reminders (Stop / SubagentStop / PreCompact) |
| `_hooklib.py` | Shared helpers — `import _hooklib as h` |

### Skills (`/name`)
- **RE / IDA** (run IDAPython via the MCP, write only `_dirty/`): `ida-mcp-connect` (+ the capability-map toolbox — categorized live `mcp__ida__*` + which tool per RE angle), `re-brainstorm` (RE ideation / attack-plan, gate G0), `ida-recon`, `ida-explore` (xref/callgraph/data-flow/batch/**decompile-one**), `ida-struct-recovery`, `ida-annotate` (the IDB-write applier — dry-run→apply rename/comment/type; idempotent; firewall-safe), `ida-opcode-map`, `ida-crypto-hunt`, `ida-py`.
- **Protocol / captures:** `pcap-extract`, `packet-codegen`.
- **Assets / VFS:** `pak-explore`, `asset-format-doc`.
- **C# / scaffolding / build:** `scaffold-project` (layer / generator / Tools / test project + reference wiring), `dotnet-build-test`, `csharp-tooling` (build/validate/extend the `Tools/` projects + `00.SourcesGenerators`), `godot-run-headless` (builds layer 05 + headless/screenshot), `godot-scene-author`, `godot-mcp-connect`.
- **Docs / tooling / quality:** `doc-authoring` (broad firewall-neutral doc corpus), `python-tooling` (std-lib scripts/harnesses + hooks), `preservation` (READMEs + session logs), `clean-room-check` (firewall + citation audit), `re-promote` (dirty→spec), `re-handoff` (the IDA→C# readiness gate G4 — STAMP/CHECK a spec is implementation-ready), `memory-curate`.
- **Knowledge** (auto-load via `paths:`, `user-invocable: false` — they surface conventions without being invoked): `dotnet-csharp14` (C#14/.NET10 core conventions), `godot-engine` (Godot 4.6 pitfalls + coordinate conventions), `ida-pro-re` (RE methodology + clean-room firewall), `martial-heroes-domain` (recovered protocol/asset-chain index, `paths: Docs/RE/**`).
- **North-star (the two goals — N1 live RE, N2 1:1 port):** `ida-debugger-drive` (pilot the **live** IDA debugger to confirm a static hypothesis against ground truth — never `dbg_start`), `godot-fidelity-check` (verify the Godot client renders/behaves **1:1** vs the original), `asset-chain-trace` (walk an asset id through its recovered mapping chain to the on-disk VFS file).
- **Planning:** `plan-campaign` (turn a user mandate into an approve-ready plan — reformulated request + decomposition + the routing map; the `/plan-campaign` command the `planning-orchestrator` runs).

### Agents (`@name`) — five domains
- **Domain orchestrators** (hold the `Agent` tool, **`opus` + `effort: high`**, each with a linked roster — see `.claude/KIT.md` §2): `planning-orchestrator` (PLAN-mode: reformulate → decompose → FINAL PLAN as a phase/objective workflow), `re-orchestrator` (clean-room RE — the IDA liaison: dirty→spec + IDB annotation + batch IDAPython), `csharp-port-orchestrator` (C# layers 00→04 + Tools, **no Godot**), `godot-orchestrator` (layer 05 only + the godot MCP, wiring the C# seam), `docs-tooling-orchestrator` (documentation + C#/Python tools + the `.claude/` kit). For a single-deliverable task, delegate straight to the worker; route an orchestrator only for a multi-worker objective. Two levels of orchestration max.
- **Planning & Analysis** (clean, no IDA): `requirement-analyst` (reformulate/scope/risk), `todo-architect` (decompose → phase/objective workflow tree + deps), `knowledge-gap-detector` (route RE/spec gaps), `plan-reviewer` (validate plans).
- **Reverse Engineering** (dirty-room `mcp__ida__*`, write only `_dirty/`): `re-function-analyst`, `re-protocol-analyst`, `re-crypto-analyst`, `re-struct-analyst`, `re-asset-format-analyst`, `ida-toolsmith` (the only IDB-write agent — rename/comment/type + batch IDAPython); **bridge** `spec-author` (rewrite `_dirty/`→committed specs, no IDA); **debugger** `re-validator` (confirm vs the live `?ext=dbg` session — never `dbg_start`).
- **C# Porting** (clean-room, no IDA, read only committed specs; layers 00→04 + Tools): `dotnet-foundation-engineer` (deputy — layer 01 + `00.SourcesGenerators` + Tools-as-code + slnx/csproj map), `network-engineer` (02), `assets-engineer` (03), `core-engineer` (04); quality `code-reviewer` (correctness + perf + layer-DAG + clean-room firewall + artifacts; **shared** with Godot for layer-05 C#), `test-engineer` (xUnit + whole-solution build; **shared**).
- **Godot Porting** (clean-room, layer 05 only): `godot-world-engineer` (terrain/world/shaders), `godot-ui-engineer` (HUD/menus/input/camera), `godot-character-specialist` (skinning/bind/motion), `render-reviewer` (Godot fidelity + live Godot MCP); plus the shared `code-reviewer`.
- **Documentation & Tooling** (clean, no IDA): `docs-engineer` (committed doc corpus, firewall-neutral), `tooling-engineer` (C# `Tools/` + `00.SourcesGenerators` + Python harnesses), `kit-author` (author `.claude/` agents+skills+hooks), `tooling-auditor` (read-only kit audit).

## Commit Discipline

- **Commit only when the user explicitly asks.** If on the default branch, branch first.
- **Never commit originals:** `*.pak`, `*.vfs`, `*.exe`, `*.dll`, `*.pcapng`, `*.tsv`, `*.scr`, `*.mot`, `*.ted`, `*.bud`, client `*.png`, `Main.exe`, anything under `_dirty/`, or the Godot `.godot/` cache.
- Don't edit orchestrator-owned files as an authoring agent: `settings.json`, `.mcp.json`, `Docs/RE/journal.md`, `Docs/RE/names.yaml`.
