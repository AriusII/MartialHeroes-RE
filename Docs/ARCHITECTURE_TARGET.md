# ARCHITECTURE_TARGET — Phase 2 Design (CAMPAIGN: STRICT 1:1 RECONSTRUCTION & C#/GODOT EXCELLENCE)

> **Status:** DESIGN ONLY. This document defines the target project graph. No
> projects are created, no files moved, no code edited by Phase 2. Phase 3
> executes this design under the file-ownership ledger.
> **Charter:** `Docs/PLAN.md` · **Run record:** `Docs/ROADMAP.md` · **Branch:** `major-campaign`.
> **DAG checker:** `.claude/skills/scaffold-project/scripts/check_dag.py` (today hard-codes
> the current 12-project graph — Phase 3 Wave 0 must rewrite it to the target; see §7).
> **Review:** validated by `plan-reviewer` (CONCERNS → all four fixes folded in: count,
> `check_dag.py` rewrite task, generator scope, provenance note).

## 0. Decision & invariants

**User decision:** re-architect with **MAXIMAL decomposition** (~20+ fine-grained
projects) — *but* a split that would create a dependency cycle or violate the DAG
is **not forced**; those stay merged and are justified in §6. "Maximal" = as
decomposed as is *cleanly* possible.

**Invariants this design provably preserves (proof in §2, §6):**
1. **Downward-only acyclic DAG** 01→02→03→04→05 — within and across layers.
2. **Engine-free below 05** — no `using Godot;` in any 01–04 project; the new
   engine-free `Client.Presentation` lib (layer 04.x) stays Godot-free too.
3. **Godot single-assembly reality** — every `Node`/`Control`/`Node3D`-derived
   script stays in `MartialHeroes.Client.Godot`. Only NON-Node logic extracts.
4. **Clean-room firewall** unaffected; the documented layer-05→02
   `Network.Transport.Pipelines` edge stays.

**Grounding:** every seam below was confirmed by greping actual `namespace`/
`using`/type references between candidate sub-groups (six READONLY dependency
scouts). The evidence is recorded inline and the rejected splits are in §6.

**Project count: 14 → 34** (13 game projects + 1 source-generator today → 33
game projects + 1 source-generator). See §1 for the full list; §6 for what was
deliberately kept merged so the number is *clean*, not forced.

---

## 1. Target project list (by layer folder)

Legend: **K** = kept as-is · **R** = renamed/re-homed · **N** = NEW. File/line
counts are approximate, taken from the scout census.

### 01.Infrastructure.Shared (2 → 2)
| Project | St | Responsibility | ~files / lines |
|---|---|---|---|
| `MartialHeroes.Shared.Kernel` | K | Strongly-typed IDs, enums, fixed-point numerics, constants. Zero deps. | ~30 |
| `MartialHeroes.Shared.Diagnostics` | K | Source-generated `[LoggerMessage]` logging. Package-only. | small |

> Kept un-split: Kernel is already the shared substrate every layer references;
> splitting it would only multiply edges. It has no internal seams worth a project.

### 02.Network.Layer (5 → 8)
| Project | St | Responsibility | ~files / lines |
|---|---|---|---|
| `MartialHeroes.Network.Abstractions` | K | Transport/session/handler/frame-sink contracts. | small |
| `MartialHeroes.Network.Crypto` | K | In-place `Span<byte>` rolling cipher. | small |
| `MartialHeroes.Network.Protocol.Generators` | K | Roslyn source-gen → `PacketRouter.g.cs` (analyzer, `ReferenceOutputAssembly=false`). | 1 |
| `MartialHeroes.Network.Protocol.Core` | **N** | Shared protocol primitives: `FrameHeader`, `PacketOpcode`(+attribute), `Opcodes` constants, `ActorSort`, `TypedPacketView`, `PacketWireSizes`, `IPacketHandler`, `PacketRouter` (+ generated partial). | ~10 / ~800 |
| `MartialHeroes.Network.Protocol.Packets.Login` | **N** | Major 0/1/3 packet structs (handshake, login, char-manage). Leaf structs. | ~35 |
| `MartialHeroes.Network.Protocol.Packets.World` | **N** | Major 4/5/6 packet structs (world/actor/combat/inventory/stats). Leaf structs. | ~85 |
| `MartialHeroes.Network.Protocol.Packets.Social` | **N** | Major 2 + social/trade/guild/party/relation Cmsg structs. Leaf structs. | ~30 |
| `MartialHeroes.Network.Transport.Pipelines` | K | `System.IO.Pipelines` length-prefixed framing. | small |

> `Network.Protocol` (the old 161-file project) is **dissolved** into `.Core` +
> the three `.Packets.*` family projects. Evidence: scout found **zero
> cross-major packet references**; the only intra-packet refs are sub-records
> within one opcode domain (`SmsgCharacterListHeader`↔`…Record`, `SmsgGameStateTick`↔`…Seed`)
> — both land in the same family project, so no cross-project edge.
> **Family count decision:** 3 (Login/World/Social), not one-project-per-major —
> per-major (0..6) would mint ~7 near-empty projects with no isolation benefit
> (majors never cross-reference, so 3 cohesive families is the *clean* maximum).
>
> **⚠ Source-generator scope (Wave-1 verification gate).** Today the analyzer ref
> lives on `Network.Protocol.csproj` and the generator scans *that* compilation —
> which contains all packet structs. After the dissolve, the packets live in the
> `.Packets.*` family projects, and `.Core` is **below** them in the DAG (Core
> does NOT reference the families). So an analyzer attached to `.Core` alone would
> **not see** the packet attributes, and the emitted router would be empty. The
> generator must be attached to a project whose compilation includes every packet
> struct. Two viable resolutions for Phase 3 to choose and verify:
> (a) **emit the router where the packets are visible** — attach the analyzer to a
>     thin top `Protocol.Routing` project that references all three `.Packets.*` +
>     `.Core`, and have `Application` consume the router from there; OR
> (b) **runtime-registration** — keep `IPacketHandler`/`PacketRouter` in `.Core`
>     and have each `.Packets.*` contribute its switch arm via a generated partial
>     in its *own* compilation, composed at the consumer.
> Resolution (a) is the smaller change and keeps a single router. Phase 3
> Wave 1 must build-verify the generated router is non-empty before declaring the
> wave green — this is a real build-correctness risk, not yet settled by this doc.

### 03.Storage.Assets (3 → 11)
| Project | St | Responsibility | ~files / lines |
|---|---|---|---|
| `MartialHeroes.Assets.Vfs` | K | Memory-mapped `.pak`/VFS → `ReadOnlyMemory<byte>` slices. | small |
| `MartialHeroes.Assets.Parsers.Core` | **N** | Shared parser primitives: `LenStrReader`, `Models/Vec2`,`Vec3`,`Quat`. References `Assets.Vfs`. | ~4 |
| `MartialHeroes.Assets.Parsers.Mesh` | **N** | `.skn`/`.bnd`/`.mot`/`.xobj` + Skeleton/SkinnedMesh/AnimationClip/StaticMesh models. | ~8 |
| `MartialHeroes.Assets.Parsers.Terrain` | **N** | `.ted`/terrain-layer/terrain-scene/`.bud`/`.map`/`.sod`/`.mud` blob + models. | ~16 |
| `MartialHeroes.Assets.Parsers.Character` | **N** | `skin.txt`/`actormotion.txt`/`bindlist.txt` catalogues. | ~7 |
| `MartialHeroes.Assets.Parsers.DataTables` | **N** | `items`/`npc`/`quests`/`msgxdb`/`citems`/`events`/`config`/`dotable`/`dostance` CP949 tables. | ~24 |
| `MartialHeroes.Assets.Parsers.Effects` | **N** | `.xeff` + particle-emitter + EffectData. | ~3 |
| `MartialHeroes.Assets.Parsers.World` | **N** | region grid/bin/table/zone + npc/mob spawn (`.arr`) tables. | ~14 |
| `MartialHeroes.Assets.Parsers.Audio` | **N** | sound-table + `.mud` sound-grid. | ~4 |
| `MartialHeroes.Assets.Parsers.Texture` | **N** | texture-list/`bgtexture`/UI-tex/detector/shader-container/skybox. | ~14 |
| `MartialHeroes.Assets.Mapping` | K | glTF/PNG/JSON converter bridge (`GltfConverter` shared facade) + AreaComposer. **Stays merged.** | ~14 / ~5.3k |

> `Assets.Parsers` (97 files) is **dissolved** into `.Core` + 8 format-family
> projects. Evidence: scout found **zero cross-family structural edges** — every
> family touches only `.Core` primitives + `Assets.Vfs`; cross-family references
> are *data fields* (`uint ModelRefKey`), never type imports. A `UiMisc` 9th
> family (buff/skill-icon/map-setting/misc/env-bin) was **folded into Texture +
> DataTables** rather than minting a low-cohesion grab-bag (see §6).
> `Assets.Mapping` **kept merged** — 14 files, all converters share the
> `GltfConverter` facade; splitting buys nothing (see §6).

### 04.Client.Core (3 → 12) — incl. the engine-free presentation lib
*(12 = 8 Domain aggregates + Application.Contracts + Application + Infrastructure + Presentation.)*
| Project | St | Responsibility | ~files / lines |
|---|---|---|---|
| `MartialHeroes.Client.Domain.Stats` | **N** | Stat/combat/vital math + aggregation tables. Hub leaf (no Domain deps). | ~14 / ~1.5k |
| `MartialHeroes.Client.Domain.Simulation` | **N** | Movement, sector grid, region catalog, camera modes, collision, regen tick. No Domain deps. | ~9 / ~1.3k |
| `MartialHeroes.Client.Domain.Actors` | **N** | Actor aggregate. → Stats, Simulation. | ~5 |
| `MartialHeroes.Client.Domain.Skills` | **N** | Skill cast + buff system. → Stats. | ~14 / ~1.3k |
| `MartialHeroes.Client.Domain.Inventory` | **N** | Equipment + item state + equip rules. → Stats. | ~7 |
| `MartialHeroes.Client.Domain.Progression` | **N** | XP/rank math. Leaf. | ~4 |
| `MartialHeroes.Client.Domain.Quests` | **N** | Quest log/objectives/state. Leaf. | ~3 |
| `MartialHeroes.Client.Domain.Social` | **N** | Party/chat/friends (opaque id-based). Leaf. | ~3 |
| `MartialHeroes.Client.Application.Contracts` | **N** | The downward seam: `IClientEventBus` + event records, `IHudEventHub`, `IApplicationUseCases`, shared DTOs/state holders (`LocalPlayerState`, `WorldEntryState`, `InFlightLatch`), `SceneStateMachine`. | ~20 / ~1.5k |
| `MartialHeroes.Client.Application` | R | Use-cases + **packet handlers** (incl. `GamePacketHandler`, split by *partial class*) + login flow + world registry + engine loop + ingestion. Stays one project (cycle — see §6). → Contracts. | ~37 / ~7.5k |
| `MartialHeroes.Client.Infrastructure` | K | SQLite/local config/offline cache/macro parsing. | small |
| `MartialHeroes.Client.Presentation` | **N** | **Engine-free** presentation logic extracted from layer 05: view-model adapters, layout tables, coordinate math, skinning math, path resolver, server-record holders. **No `using Godot;`.** | ~13 / ~? |

> `Client.Domain` (62 files) **dissolves** into 8 aggregate projects. Evidence:
> only 4 inter-aggregate edges, all downward to Stats/Simulation; no cycles.
> `Client.Application` (64 files) is **partly** split: the well-isolated
> contracts/events/hub/scene types move down into `Application.Contracts`; the
> handler+usecase+world core **stays one project** because of a real
> Handlers↔UseCases micro-cycle + shared mutable `Progression` state (see §6).
> `Client.Presentation` is the **only** lib extracted *out of* the Godot project —
> it is layer 04.x and Godot references it downward (§2).

### 05.Presentation (1 → 1)
| Project | St | Responsibility | ~files / lines |
|---|---|---|---|
| `MartialHeroes.Client.Godot` | K | The single Godot game assembly — all `Node`/`Control`/`Node3D`-derived scripts, scenes, shaders. Re-organized by pattern-folders; god-classes split via **partial classes**. | ~104 / ~42k after extracting the 13 engine-free files |

> **Stays ONE assembly** (hard Godot constraint). 68 of 117 files are
> Node-derived → cannot move. 13 Godot-free files extract *downward* to
> `Client.Presentation`. ~3 borderline builder files (return Godot `Mesh`) stay.

**Layer-folder count: 01=2 · 02=8 · 03=11 · 04=12 · 05=1 → 34 projects**
(33 game + the `Protocol.Generators` source-generator, counted in 02). The
campaign mandate's "~20+" target is exceeded *cleanly*; nothing below was split
past the point of acyclicity.

---

## 2. Reference graph (acyclic, downward-only)

A **topological ordering** is the proof of acyclicity: every `ProjectReference`
points from a higher-listed project to a lower-listed one (strictly downward),
**except the one documented layer-05→02 edge** (called out explicitly).

**Topological order (top depends only on things below it):**

```
05  Client.Godot
        │  → Application, Application.Contracts, Infrastructure,
        │    Assets.Mapping, Client.Presentation,
        │    Network.Transport.Pipelines   ◄── DOCUMENTED 05→02 EXCEPTION
        ▼
04  Client.Presentation            (engine-free; → Application.Contracts, Assets.* read-models, Mapping)
04  Client.Infrastructure          (→ Application, Assets.Parsers.*, Assets.Vfs)
04  Client.Application             (→ Application.Contracts, Domain.*, Network.Abstractions/Protocol.*/Crypto)
04  Client.Application.Contracts   (→ Domain.* selected, Shared.Kernel)
04  Client.Domain.Actors           (→ Domain.Stats, Domain.Simulation)
04  Client.Domain.Skills           (→ Domain.Stats)
04  Client.Domain.Inventory        (→ Domain.Stats)
04  Client.Domain.Stats            (→ Shared.Kernel)            ── hub leaf
04  Client.Domain.Simulation       (→ Shared.Kernel)            ── leaf
04  Client.Domain.Progression/Quests/Social  (→ Shared.Kernel)  ── leaves
        ▼
03  Assets.Mapping                 (→ Assets.Parsers.* families)
03  Assets.Parsers.{Mesh,Terrain,Character,DataTables,Effects,World,Audio,Texture}
                                   (→ Assets.Parsers.Core)
03  Assets.Parsers.Core            (→ Assets.Vfs)
03  Assets.Vfs                     (→ Shared.Kernel)
        ▼
02  Network.Transport.Pipelines    (→ Network.Abstractions)
02  Network.Protocol.Packets.{Login,World,Social}  (→ Network.Protocol.Core)
02  Network.Protocol.Core          (→ Shared.Kernel; analyzer scope: see §1 ⚠)
02  Network.Crypto                 (→ Shared.Kernel)
02  Network.Abstractions           (→ Shared.Kernel)
02  Network.Protocol.Generators    (analyzer-only, no runtime ref)
        ▼
01  Shared.Diagnostics             (package-only)
01  Shared.Kernel                  (zero deps)
```

**DAG: acyclic, downward-only.** Proof: the ordering above is a valid topological
sort — no project references one printed above it. `Application.Contracts` sits
strictly below `Application` (Application → Contracts; Contracts → Domain only),
so the seam does not cycle. `Client.Presentation` references only
Contracts/Assets/Mapping, never layer 05 — no upward edge.

The single upward-*folder* edge (`Client.Godot` 05 → `Network.Transport.Pipelines`
02) is the **pre-existing, documented exception** carried unchanged from today's
graph (CLAUDE.md / PLAN.md §3): it is acyclic (02 never references 05) and is the
live-networking wiring from the front-end campaign. **Note:** `check_dag.py`
ignores layer 05 entirely (`Client.Godot` is in its `IGNORED_PROJECTS`), so this
edge — and the new 05→04.x `Client.Presentation` edge — are **unpoliced by the
checker, not validated by it**; their correctness rests on this design + the
Phase-3 build, not on `check_dag.py`.

**Edge-list deltas vs today (what Phase 3 wires):**
- `Application` → split: now also references the new `Application.Contracts`;
  its old refs to Domain/Network are re-pointed at the split Domain projects.
- Every old `using MartialHeroes.Network.Protocol;` consumer now references
  `Protocol.Core` + whichever `Protocol.Packets.*` it actually uses.
- Every old `using MartialHeroes.Assets.Parsers;` consumer (`Assets.Mapping`,
  `Client.Infrastructure`) now references `Parsers.Core` + the specific family
  projects it consumes (transitive via the dissolved umbrella is *not* used —
  explicit edges keep the DAG legible).
- `Client.Godot` adds a downward ref to `Client.Presentation`.

---

## 3. File→project move map

### 3.1 Network.Protocol → Core + 3 family projects
- **Rule:** a packet struct goes to the family of its `[PacketOpcode(major,…)]`.
- `FrameHeader.cs`, `Opcodes/*`, `Routing/*` (`IPacketHandler`, `PacketRouter`,
  `TypedPacketView`, `PacketWireSizes`), `Packets/ActorSort.cs`,
  `Packets/ItemSlotRecord.cs` → **Protocol.Core** (the router itself may move to a
  thin top `Protocol.Routing` project — see §1 ⚠ generator-scope resolution).
- `Packets/Cmsg/Smsg*` for major 0/1/3 (Login*, CharacterList*, CharManage*,
  CharSpawn*, Create/Select/Rename/Manage character, Login credential) → **Packets.Login**.
- major 4/5/6 (Smsg/Cmsg actor/combat/move/skill/stat/inventory/enter-game/
  game-state) → **Packets.World**.
- major 2 + trade/guild/party/relation/friend/letter/stall Cmsg → **Packets.Social**.
- The 140 `.g.cs` generated structs follow the same family rule (their source
  attributes carry the major).

### 3.2 Assets.Parsers → Core + 8 family projects
- **Rule:** `XParser.cs` + its `Models/XData.cs` move together to the family.
- `LenStrReader.cs`, `Models/Vec2|Vec3|Quat.cs` → **Parsers.Core**.
- `Skn|Bnd|Animation|Xobj` + `SkinnedMesh|Skeleton|AnimationClip|StaticMesh` → **Mesh**.
- `Ted|TerrainLayer|TerrainScene|Bud|MapDescriptor|Sod|Mud(blob)` + their models → **Terrain**.
- `SkinTxt|Actormotion|Bindlist` + models → **Character**.
- `Items*|Npc*(scr)|Quests|MsgXdb|Citems|Events|Config|DoTable|DoStance|MobInfoPanel|AutoQuestion|ChatFilter|GameVer` + models → **DataTables**.
- `Xeff|ParticleEmitter` + `EffectData` → **Effects**.
- `Region*|*Spawn(npc/mob `.arr`)` + models → **World**.
- `SoundTable|MudSoundGrid` + models → **Audio**.
- `TextureList|BgTexture*|UiTex|TextureDetector|ShaderContainer|SkyBox|SkillIcon|BuffIconPosition|MapSettingScr|Misc|EnvironmentBin` + models → **Texture** (absorbs the former "UiMisc" family — §6).

### 3.3 Client.Domain → 8 aggregate projects
- `Stats/*` → **Domain.Stats**. `Simulation/*` → **Domain.Simulation**.
  `Actors/*` → **Domain.Actors**. `Skills/*` → **Domain.Skills**.
  `Inventory/*` → **Domain.Inventory**. `Progression/*` → **Domain.Progression**.
  `Quests/*` → **Domain.Quests**. `Social/*` → **Domain.Social**.

### 3.4 Client.Application → Contracts + (kept) Application
- **Move down to Application.Contracts:** `Events/*` (`IClientEventBus` + 28
  event records), `Hud/IHudEventHub.cs` (+ HUD DTOs), `Scene/*`
  (`SceneStateMachine`, `SceneStateChangedEvent`), `UseCases/IApplicationUseCases.cs`,
  and the shared mutable state holders (`LocalPlayerState`, `WorldEntryState`,
  `InFlightLatch`, `ProgressionState`).
- **Stay in Application:** `Handlers/GamePacketHandler.cs` (split by partial
  class — §4), `UseCases/ApplicationUseCases.cs` + support, `World/*`, `Engine/*`,
  `Login/*`, `Net/*`, `Input/*`, `Ingestion/*`, `Assets/*`, `Diagnostics/*`.

### 3.5 Godot (05) — Node-stays vs non-Node-extracts (the headline)
- **STAYS (Node-derived, 68 files):** everything inheriting `Node`/`Control`/
  `Node3D`/`Camera3D`/`MeshInstance3D`/`CanvasLayer`/`GpuParticles3D`/`Resource`/
  `RefCounted` — all of `Ui/Hud/*`, `Ui/Widgets/*`, `Ui/Scenes/*`,
  `Scene/Controllers/*`, most of `World/*`, `Autoload/*`, the renderers, `GameLoop`,
  `ClientContext`, `AudioService`, `CameraController`, `EnvironmentNode`.
- **EXTRACTS → `Client.Presentation` (Godot-free, 13 files):**
  - `Helpers/WorldCoordinates.cs` (coordinate math)
  - `World/SkinningMath.cs` (quaternion/bone algebra)
  - `Screens/Layout/LoginLayout.cs` (layout-constant tables)
  - `Screens/ServerEntry.cs` (server-record holder)
  - `Screens/ClassAppearanceResolver.cs` (pure lookup)
  - `Dev/ClientPathResolver.cs` (VFS path resolution; rename out of `Dev/`)
  - `Input/HudInputHandler.cs` (`IInputHandler`, delegate hit-test gate)
  - `Adapters/AssembledAreaViewAdapter.cs`, `AssembledCellViewAdapter.cs`,
    `RebindableAreaAssemblySource.cs`, `VfsAreaAssemblySource.cs`,
    `VfsRegionSource.cs`, `VfsTerrainSectorSource.cs` (engine-free view adapters /
    assembly pipelines — verify each has no `using Godot;` before moving).
- **BORDERLINE (class C — STAY in 05):** `World/BudMeshBuilder.cs`,
  `World/SkinnedCharacterBuilder.cs`, `World/CelShadeMaterialFactory.cs` — they
  return Godot `Mesh`/`ShaderMaterial`. *Optionally* their pure math (vertex/
  bone composition) can be a Godot-free helper in `Client.Presentation` with the
  thin Mesh-constructor wrapper staying in 05; treat as a Phase-3 stretch, not a
  required move.

> **Move-safety rule:** before any file moves to `Client.Presentation`, Phase 3
> greps it for `using Godot;`/`global::Godot.` and any Godot type in a signature.
> A single hit reclassifies it as class C (stays in 05). The 13-file list is the
> *candidate* set; the gate is the grep, not this document.

---

## 4. God-class split map (12 oversized files → focused files)

**12 total = 11 in layer 05 (each inherits a Godot Node type) + `GamePacketHandler`
in layer 04 (holds shared handler state).** Each is split **into focused files
inside its current project** via `partial class`, never into a new assembly.
Folder = current folder unless noted.

| God-class (lines) | Project / folder | Inherits | → focused files |
|---|---|---|---|
| `RealWorldRenderer.cs` (2415) | 05 World/ | Node3D | `RealWorldRenderer` (frame/_Process/_ExitTree) · `…Initializer` · `…Streaming` · `…TextureResolver` · `…ModelLoader` · `…Composer` · `…Config` (config readers — candidate for `Client.Presentation` if Godot-free) |
| `GamePacketHandler.cs` (1849) | 04 Application Handlers/ | (shared state) | partial files by domain: `GamePacketHandler.Login` · `.CharManage` · `.World` · `.Inventory` · `.StatsProgression` · `.CombatSkills` · `.Actors` · `.Chat` — **one project, partial class** (shared `Progression`/`_world`/`_eventBus`/`InFlightLatch` forbid separate projects — §6) |
| `LoginWindow.cs` (1583) | 05 Ui/Scenes/Login/ | Control | `LoginWindow` (frame/signals) · `…StateMachine` · `…Widgets` · `…Input` · `…ServerList` |
| `CharSelectWindow.cs` (1416) | 05 Ui/Scenes/Select/ | Control | `CharSelectWindow` (frame/mode) · `…ListView` · `…CreateForm` · `…Input` · `…Composition` (3D viewport wiring) |
| `ClientContext.cs` (1292) | 05 Autoload/ | Node | `ClientContext` (frame/singleton/_Ready) · `…NetworkSetup` · `…ApplicationGraph` · `…InputSetup` · `…CatalogLoaders` |
| `EffectRenderer.cs` (1180) | 05 World/ | Node3D | `EffectRenderer` (frame/pool) · `…Caster` · `…KeyframeAnimator` · `…EmitterRenderer` |
| `HudMaster.cs` (1171) | 05 Ui/Hud/ | Control | `HudMaster` (frame/toggle) · `…Builder` · `…Reconfigurer` · `…EventSubscriber` |
| `NpcRenderer.cs` (1074) | 05 World/ | Node3D | `NpcRenderer` (frame/API) · `…SpawnResolver` · `…Populator` |
| `EnvironmentNode.cs` (931) | 05 World/ | Node3D | `EnvironmentNode` (frame/API) · `…Configurator` · `…Cycle` |
| `GameLoop.cs` (929) | 05 World/ | Node | `GameLoop` (frame/children) · `…EventDrain` · `…VitalsAndRegion` |
| `AudioService.cs` (919) | 05 Audio/ | Node | `AudioService` (frame/singleton) · `…StreamCache` · `…Playback` · `…EventSubscriber` |
| `CameraController.cs` (915) | 05 World/ | Camera3D | `CameraController` (frame/props) · `…Modes` · `…Orbit` · `…Input` |

Net: 12 god-classes → **~48 focused files** (target ≤ ~400 lines each), all
within their existing assemblies. Only `GamePacketHandler` is layer 04; the other
11 are layer 05 and stay in the single Godot assembly.

---

## 5. Folder vocabulary (canonical pattern-folders)

**Rule: `namespace` == folder path** (e.g. a file in `Ui/Hud/` is
`namespace MartialHeroes.Client.Godot.Ui.Hud`). One pattern-folder = one concern.

| Folder | Meaning | Used in |
|---|---|---|
| `Contracts/` | Interfaces + DTOs others depend on (the downward seam) | Application.Contracts |
| `Handlers/` | Inbound packet/event handler sinks | Application |
| `Codecs/` | Wire struct (de)serialization | Network.Protocol.* |
| `Routing/` | Opcode→handler dispatch | Protocol.Core / Protocol.Routing |
| `Models/` | Pure parsed data holders | Assets.Parsers.* |
| `Catalogs/` | Id→record lookup tables | Application, Presentation |
| `Composition/` | Object-graph wiring / composition root | Godot Autoload/, Mapping |
| `Rendering/` | Mesh/shader/effect construction | Godot World/ |
| `Scenes/` | Scene controllers + per-scene windows | Godot Scene/, Ui/Scenes/ |
| `Hud/` | In-game HUD panels | Godot Ui/Hud/ |
| `Widgets/` | Reusable Control primitives | Godot Ui/Widgets/ |
| `Adapters/` | Boundary adapters (engine↔core) | Godot Adapters/, Presentation |
| `Input/` | Input routing + intents | Godot Input/, Application |
| `Audio/` | Sound playback | Godot Audio/ |

> Existing good folders kept: `World/`, `Ui/Hud/`, `Ui/Widgets/`, `Ui/Scenes/`,
> `Ui/Assets/`, `Scene/Controllers/`, `Autoload/`, `Helpers/`. Phase 3 renames
> `Dev/` → `Composition/` (or moves its one real file to `Client.Presentation`)
> and `Screens/` content is sorted into `Ui/Scenes/` or `Adapters/`.

---

## 6. Cycle/seam risks & kept-merged decisions (every rejected "maximal" split)

1. **`Client.Application` handlers NOT split into separate projects — CYCLE +
   shared mutable state.** Evidence: `UseCases/CatalogueVitalsResolver` reads
   `GamePacketHandler.VitalsResolver` (a public property) → a real
   **Handlers↔UseCases micro-cycle**; and the `Progression` field is mutated by
   *both* the 5/9 (ExpGain) and 5/11 (RankXpGain) handler branches plus
   `InFlightLatch` is cleared by CharManage *and* World *and* Inventory branches.
   Separate projects would need to externalize this shared state and break the
   cycle — out of scope for a re-org. **Decision:** keep `Application` as one
   project; split `GamePacketHandler` by **partial class** (§4); extract only the
   genuinely-isolated `Events/Hud/Scene/Contracts` down to `Application.Contracts`.

2. **`Assets.Mapping` kept merged (14 files).** Evidence: `TerrainGltfConverter`,
   `BudSceneGltfConverter`, `CollisionLayerGltfConverter` are thin layers over the
   shared `GltfConverter` facade; `AreaComposer`/`AssembledArea` are a tight unit.
   Splitting would extract `GltfConverter` as a base project for ~3 trivial
   dependents — pure ceremony, no isolation gained. **Decision:** stay one project.

3. **`Shared.Kernel` kept un-split.** It *is* the shared substrate; splitting it
   multiplies edges without isolating anything. **Decision:** one project.

4. **Protocol families = 3, not 7 (per-major).** Majors never cross-reference, so
   per-major would be acyclic — but it mints near-empty projects. **Decision:**
   group into Login/World/Social cohesive families (the *clean* maximum).

5. **Parsers "UiMisc" family folded into Texture+DataTables.** A 9th
   buff-icon/skill-icon/map-setting/misc/env-bin family is a low-cohesion
   grab-bag. Its texture-ish members go to **Texture**, its table-ish members to
   **DataTables**. **Decision:** 8 families, not 9.

6. **Borderline Godot builders stay in 05.** `BudMeshBuilder`,
   `SkinnedCharacterBuilder`, `CelShadeMaterialFactory` return Godot
   `Mesh`/`ShaderMaterial` → extracting them would drag `using Godot;` into the
   engine-free lib. **Decision:** stay in 05 (optional pure-math sub-extraction is
   a Phase-3 stretch, gated by the no-`using Godot;` grep).

7. **`Domain.Actors` depends on Simulation + Stats — not a leaf.** Acyclic
   (downward), so it splits cleanly, but it must be created **after** Stats and
   Simulation in the execution order (§7). No merge needed.

**Open risk (route to RE if it blocks):** the carry-over `AreaComposer.Spawns`
vs server-4/4 question (ROADMAP Phase 1) is a *behavioral* question, not an
architecture one — it does not affect the project graph. Flagged, not assumed.

---

## 7. Execution order (Phase 3 — build green at every step)

Create leaf/contract projects first, move files into them, re-point references,
verify `check_dag.py` + build-nuke after each wave. **One writer per path per
wave** (ledger seeds below).

**Wave 0 — scaffolds + checker rewrite (no file moves yet):**
- 0a Create empty new project shells + solution-folder entries; wire the
  `Protocol.Generators` analyzer ref per the §1 ⚠ resolution. Build stays green
  (shells are empty).
- 0b **Rewrite `.claude/skills/scaffold-project/scripts/check_dag.py`** — its
  `INTENDED` / `LAYER` / `SUBORDER` tables hard-code today's 12-project graph and
  will fail-by-design the instant Wave 1 creates the new projects (it emits
  `unknown core project` + `UNEXPECTED edge`). Update those tables to the §2
  target 34-project graph **before** Wave 1's gate runs. **Tier-1-owned** (the
  file lives under `.claude/`, orchestrator-adjacent — not a worker path). Until
  0b lands, the "DAG clean" gates below cannot pass.

**Wave 1 — bottom layers (02 + 03 leaves), parallel:**
- 1a `Network.Protocol.Core` (+ optional `Protocol.Routing` per §1 ⚠) ← move
  Frame/Opcodes/Routing/ActorSort; re-home the generator; **build-verify the
  generated router is non-empty.** *(writer: network-engineer; paths: 02/Protocol.Core|Routing/**)*
- 1b `Protocol.Packets.{Login,World,Social}` ← move packet structs by family.
  *(writer: network-engineer; paths: 02/Protocol.Packets.*/**)*
- 1c `Assets.Parsers.Core` ← move primitives. *(writer: assets-engineer; 03/Parsers.Core/**)*
- 1d `Assets.Parsers.{8 families}` ← move parser+model pairs. *(writer:
  assets-engineer; one family per sub-wave path to avoid collision)*
- **Gate:** re-point `Assets.Mapping` + `Application` refs; build-nuke 0/0;
  `check_dag.py` (rewritten) clean; **generated router non-empty.**

**Wave 2 — Domain (04), respecting the aggregate DAG:**
- 2a `Domain.Stats`, `Domain.Simulation`, `Domain.Progression/Quests/Social`
  (all leaves) in parallel. *(writer: core-engineer; one project per path)*
- 2b `Domain.Actors`, `Domain.Skills`, `Domain.Inventory` (depend on Stats/Sim)
  **after** 2a. *(writer: core-engineer)*
- **Gate:** build-nuke 0/0; DAG clean.

**Wave 3 — Application split (04):**
- 3a `Application.Contracts` ← move Events/Hud/Scene/UseCases-interface + state
  holders; re-point `Application` to reference it. *(writer: core-engineer)*
- 3b split `GamePacketHandler` into partial files (same project, no ref change).
  *(writer: core-engineer; serialized — single file family)*
- **Gate:** build-nuke 0/0; headless spine 0→5; DAG clean.

**Wave 4 — engine-free extraction + Godot re-org (05 → 04.x):**
- 4a create `Client.Presentation`; move the 13 grep-verified Godot-free files
  down; add the 05→04.x ref. *(writer: dotnet-foundation-engineer + godot-* per
  file family; grep-gate each file first)*
- 4b split the 11 layer-05 god-classes into partial files (per file, disjoint).
  *(writers: godot-world-engineer [World/ renderers, camera, env, gameloop],
  godot-ui-engineer [LoginWindow, CharSelectWindow, HudMaster],
  godot-foundation [ClientContext, AudioService] — one god-class per writer per
  sub-wave; never two writers on one file)*
- 4c rename `Dev/`→`Composition/`, sort `Screens/` into `Ui/Scenes/`+`Adapters/`.
- **Gate:** build-nuke 0/0 (incl. Godot csproj); headless spine 0→5 clean;
  `/clean-room-check` PASS; `check_dag.py` clean.

**Wave 5 — consolidation:** ROADMAP statuses; verify the 34-project graph against
the rewritten `check_dag.py`; final build-nuke + headless + (replica-up) live
login. **Provenance:** this campaign re-organizes the project graph and touches
**no `Docs/RE/` spec**, so the spec→journal pairing does not bite. But note
`Docs/RE/journal.md` was **deleted** (commit `49a9a01`): if any Phase-3 wave ends
up correcting a `Docs/RE/` spec (e.g. the §6 open `AreaComposer.Spawns` question,
if it forces a spec edit), the provenance loop must be re-initialized — restore
`journal.md` (via the `re-promote` workspace-init) and journal the change before
any commit. Otherwise the journal stays deleted (maintainer decision §8.5).

**Ledger seeds (one writer per path per wave):** each bullet above names its
single owning engineer and its disjoint path glob. No two bullets in the same
wave share a path. The shared files (`MartialHeroes.slnx`, any `Directory.Build.*`,
`check_dag.py`) are **Tier-1-serialized** — wired by the main session between
waves, never by a worker in parallel.

---

## 8. Decisions needed from the maintainer before Phase 3 executes

1. **Project count 14→34 acceptable?** The clean maximum is 34 (33 game +
   generator). If you want fewer, the cheapest merges are the Protocol families
   (8→6: fold Social into World) and the Parsers families (11→7: fold
   Effects/Audio into Texture/World). Confirm 34, or name a target ceiling.
2. **`Application` staying one project (handlers not separated)** — confirm you
   accept the cycle/shared-state justification (§6.1), i.e. `GamePacketHandler`
   split is partial-class-only, not a per-domain project.
3. **`Client.Presentation` engine-free lib** — confirm you want the 13-file
   downward extraction (it adds a new layer-04.x project + a 05→04.x edge). If you
   prefer zero new layer-04 presentation lib, those 13 files stay in 05 as a
   `Presentation/` pattern-folder (still re-organized, just not a separate
   assembly).
4. **Borderline mesh builders (§6.6)** — leave in 05 (recommended) or attempt the
   pure-math sub-extraction stretch?
5. **`Docs/RE/journal.md`** — confirm it stays deleted for this pure-reorg
   campaign (restored only if a wave is forced to correct a spec — §7 Wave 5), or
   restore it up front.

---

*Phase 2 — design only. Phase 3 executes under `port-orchestrator` (engineering)
with Tier-1 owning the ledger, `MartialHeroes.slnx` wiring, the `check_dag.py`
rewrite, and the gates.*
