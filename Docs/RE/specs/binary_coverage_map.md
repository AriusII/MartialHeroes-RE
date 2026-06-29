---
verification: static-hypothesis
evidence: [multi-modal-static-probes]
ida_anchor: f61f66a9
date: 2026-06-24
method: >
  Candidate clusters surfaced by targeted static probes across the full doida.exe function
  corpus: import-table neighbourhood walks, call-graph proximity sweeps, string-constant
  triage, and RTTI class-name enumeration. Probe tags (render-d3d, sound-dsound,
  net-ws2, crypto-anticheat, input-ime, ui-gu, actor-world, lua-script, rtti-classes)
  identify which sweep each cluster came from. All cluster roles are [static-hypothesis]
  except where a committed spec already carries a stronger verification status.
note: >
  This document is a coverage map, not a spec. It records which subsystems are committed
  to Docs/RE/ specs and which are gaps. Cluster function counts are approximations from
  static analysis; they are not precise boundaries. This map derives from committed specs
  and probe data; it does not assert binary facts not already grounded in a spec.
---

# doida.exe Binary Coverage Map

> **Verification banner**
> - verification: static-hypothesis (probe-derived coverage estimates; individual subsystems
>   carry their own stronger verification status as noted in the linked specs)
> - ida_anchor: f61f66a9
> - date: 2026-06-24
> - binary: doida.exe, ~25,792 total functions; ~5,110 named at probe time; ~18,781 autonamed
>   (unnamed) — approximately 20 % of functions carry a committed canonical name at this snapshot.
> - method: multi-modal static probes — import-neighbourhood, call-graph, string-constant,
>   RTTI-class sweeps across the full corpus.
> - scope: the ~18,800 unnamed functions are the focus; clusters already exhaustively named
>   and spec-matched are noted only for completeness.

---

## 1. Purpose and method

This document records, as of IDB SHA **f61f66a9** (2026-06-24), which subsystems of `doida.exe`
are covered by committed `Docs/RE/` specs and which remain gaps. It is the intake point for
future RE prioritisation. It does not supersede any individual spec.

Coverage was estimated by five complementary static probes applied to the ~18,800 unnamed
function pool:

- **render-d3d** — Direct3D 9 import neighbourhood + D3DX call sites + string-constant triage
- **sound-dsound** — DirectSound + libVorbis import neighbourhood + OGG/sound string triage
- **net-ws2** — Winsock2 import neighbourhood + send/recv call-graph walk
- **crypto-anticheat** — CryptoAPI import neighbourhood + anti-cheat string/RTTI sweep
- **input-ime** — IMM32 + DirectInput8 import neighbourhood + WndProc call-graph
- **ui-gu** — GU-prefixed RTTI classes + panel/window string triage
- **actor-world** — ActorManager / Actor call-graph neighbourhood + movement/combat string triage
- **lua-script** — Lua VM string constants + lua_tinker markers + script-consumer call-graph
- **rtti-classes** — RTTI class-name enumeration for classes not surfaced by other probes

---

## 2. Coverage overview

| Metric | Value |
|---|---|
| Total functions (IDB, 2026-06-24) | ~25,792 |
| Named functions at probe time | ~5,110 (~20 %) |
| Unnamed functions (the dark pool) | ~18,681 |
| Committed spec files (`Docs/RE/`) | 54 format docs + 53 subsystem/struct specs + 1 opcode catalogue + ~130 packet YAMLs |
| Estimated named + well-specced functions | ~4,800 (functions with a committed canonical name AND a backing spec) |
| Estimated gap functions (unnamed, no full spec coverage) | ~18,000–19,000 |

The binary is broadly divided into four tiers by current coverage:

1. **Fully mapped** — subsystem has a committed spec, most functions named, behavior confirmed.
   Covers the core networking stack, the wire cipher, the VFS read path, the actor family,
   the sound device + OGG pipeline, the UI widget toolkit, and Lua scripting.
2. **Partially mapped** — subsystem has a committed spec, but a function-level naming gap
   remains (the spec describes behavior correctly; individual helpers are still autonamed).
3. **Gap — un-mapped** — no committed spec for this cluster; behavior is a static hypothesis
   only.
4. **Out-of-scope / excluded** — CRT, linker thunks, third-party codec internals; confirmed
   present but not a project RE target (statically-linked library bodies, import shims).

Rough estimate: the code surface covered by at least one committed `Docs/RE/` spec represents
approximately **35–45 % of the total function count** (the named and partially-specced surface)
and approximately **65–75 % of the behaviorally significant code paths** (hot paths in
networking, rendering, asset loading, and gameplay are heavily mapped; the long tail of editor
utilities, diagnostic helpers, and third-party codec internals is not).

---

## 3. Mapped subsystems

Clusters that already have a committed `Docs/RE/` spec. Function counts are probe approximations.

| Cluster | Approx. functions | Committed spec(s) | Coverage quality |
|---|---|---|---|
| NetConn overlapped-socket I/O (game connection) | ~12 | `specs/network_dispatch.md §4.4a`, `specs/connection_topology.md`, `structs/net_client.md` | Fully named and specced |
| Wire cipher + LZ4 + session handshake | ~15 | `specs/crypto.md` | Fully specced; confirmed |
| Login/lobby blocking-query threads | ~5 | `specs/connection_topology.md`, `specs/login.md`, `specs/login_flow.md`, `packets/lobby.yaml`, `packets/login.yaml` | Fully specced |
| XTrap raw blocking-socket client | ~8 | `specs/connection_topology.md §4` (socket lifecycle) | Specced at connection level; object-offset table partial gap |
| Dynamic WS2 import thunks | ~10 | (out of scope — linker CRT thunks, not a subsystem) | Excluded |
| Actor family (Actor, ActorManager, spawn, buff, death) | ~123 | `specs/world_systems.md`, `structs/actor.md`, `specs/combat.md` | Fully named and specced |
| BattleController / combat target core | ~5 | `specs/combat.md`, `specs/world_systems.md Ch.1` | Fully named and specced |
| Click-to-move walk-step + region combat-mode gate | ~8 | `specs/world_systems.md Ch.16/18` | Specced; movement-tick function unnamed |
| Ground-item world registry (ItemMap) | ~6 | `specs/world_systems.md Ch.7`, `specs/inventory_trade.md §12` | Specced; helper functions unnamed |
| Mount visual-seat attach + leader-follower spacing | ~6 | `specs/world_systems.md Ch.20`, `specs/pets.md` | Specced; cosmetic-only, consistent |
| Companion / partner-pair propagation | ~4 | `specs/pets.md`, `structs/actor.md companion_ptr` | Specced |
| Mob/NPC AI, pathfinding, navigation | 0 (absent) | `specs/world_systems.md Ch.20` (confirmed-by-absence note) | Anti-phantom documented |
| VFS find-and-read chokepoint + texture decode wrappers | ~12 | `specs/vfs_overview.md`, `specs/resource_pipeline.md §1.5/§3A` | Fully named and specced |
| Streaming Ogg/Vorbis audio loader | ~20 | `specs/sound.md §0/§2` | Fully specced and named |
| DirectSound device + buffer engine | ~12 | `specs/sound.md §1` | Fully specced |
| libVorbis 1.3.2 static codec body | ~176 | `specs/sound.md` (version + entry points noted) | Version confirmed; internals out-of-scope |
| Per-area sound-schedule table loader + active-id map | ~18 | `specs/sound.md §6`, `formats/sound_tables.md` | Fully specced |
| Sound options UI panel | ~6 | `specs/sound.md` (volume buses), `specs/ui_system.md` | Specced; slider-bus decode debugger-pending |
| Diamond GU base widget toolkit | ~120 | `structs/gucomponent.md`, `structs/guwindow.md`, `specs/ui_system.md §1–7`, `specs/ui_event_dispatch.md` | Fully named and specced |
| GU command-handler / event-outlet / window-manager | ~25 | `specs/ui_event_dispatch.md §5`, `specs/ui_system.md §1.6/§8.17/§15.4/§15.6` | Fully mapped |
| In-game HUD panel class family | ~600 | `specs/ui_system.md §8.x`, `specs/ui_hud_layout.md` | Largely mapped |
| DirectInput8 buffered-keyboard polling thread | ~1 | `specs/input_ui.md §0` | Fully named and specced |
| Lua 5.1.2 VM core (stock, statically linked) | ~230 | `specs/lua_scripting.md §1–2` | VM confirmed; stock internals out-of-scope |
| lua_tinker C++/Lua binding glue | ~30 | `specs/lua_scripting.md §3` | Specced |
| Host script-API surface (cpp_load + table extractors) | ~6 | `specs/lua_scripting.md §4–5` | Specced; resolves prior UNVERIFIED item |
| Script-consuming game systems (UI/config/localization) | ~5 | `specs/lua_scripting.md §6.3` | Specced |
| D3D9 shader loader (cel/glow set) | ~10 | `formats/shaders.md` | Specced and sample-verified |
| Terrain streaming + height sampler | (in actor/terrain family) | `specs/terrain-streaming.md`, `formats/terrain.md` | Fully specced |
| Skinning / skeleton / animation core (loaders) | (named seam) | `specs/skinning.md`, `formats/skn.md`, `formats/animation.md`, `formats/actormotion.md` | Format + load seam specced |

---

## 4. Dark / un-mapped subsystems (the gap backlog)

Clusters confirmed present by static probe but lacking a committed `Docs/RE/` spec, or having a
material function-level gap not covered by the current spec. Ordered by priority.

### Priority: HIGH

| Cluster | Approx. functions | Evidence basis | Gap description | Suggested RE next step |
|---|---|---|---|---|
| Anti-cheat / integrity-monitor + encrypted remote-command agent | ~40 | Integrity-monitor loop, encrypted command processor, API-hook detectors, tamper-to-fatal-exit path — all identified by static probe | No spec for the agent internals: the command-decrypt channel, hook detectors, 3555 ms monitor loop, or the tamper response. `specs/connection_topology.md §4` covers the XTrap socket/host endpoint only. | `@re-function-analyst` map the monitor-thread state machine and command-processor decrypt flow; produce `specs/anticheat.md` |
| In-house anti-cheat / signed-file orchestrator (GXProtect) | ~12 | RTTI classes GXProtect / GProtect / GGGProtect; singleton constructor; obfuscated cheat-log writer | `specs/crypto.md §7.1` notes behavioral existence only; class triad, page-guard cadence, and orchestration flow are un-specced | Extend `specs/crypto.md §7.1` or produce `specs/gxprotect.md` |
| CryptoAPI signed-file verifier + RC4 content check | ~9 | All eleven ADVAPI32 CryptXxx imports concentrated here; RC4 KSA + PRGA keyed by a CryptGetHashParam digest; file-hasher at world-enter | `specs/crypto.md §7.1` is behavioral only; the RC4 identification is new vs the spec's description; function-level mapping absent | `@re-crypto-analyst` map the verifier and RC4 keying path; update `specs/crypto.md` with RC4 confirmation |
| SEED-128 block cipher + streaming modes + two consumers | ~18 | 16-round Feistel core (already named); streaming-mode wrappers and two call-site consumers identified | `specs/crypto.md §7.2` acknowledges SEED but leaves call-site subsystem unpinned | `@re-crypto-analyst` pin both consumers in the spec; close the open item in `specs/crypto.md §7.2` |
| FLINT++ bignum / RSA math library | ~213 | RTTI LINT_Base / LINT_Error / LINT_File and ten exception subclasses; ~213 named Flint_BignumHelper_* functions; the bignum substrate behind the login RSA handshake | `specs/crypto.md` pins the RSA modexp path but treats the bignum library as a black box; Montgomery reduction, modular arithmetic helpers unmapped | `@re-crypto-analyst` document the Montgomery modexp entry point and key helper primitives; extend `specs/crypto.md` |
| In-game MainHandler input / event dispatch (click-to-move / click-to-attack) | ~18 | Input/event router, click-action dispatch (move vs. attack decision), click-target resolver — identified in the actor-world probe | `specs/ui_event_dispatch.md` + `specs/input_ui.md` cover the spec-level behavior; the function cluster is mostly unnamed — naming gap | `@ida-toolsmith` name the router and dispatcher functions; `@re-validator` breakpoint the click dispatch to confirm move-vs-attack branch live |
| GHTex / GTexture texture-resource wrapper (object model) | ~70 | GHTex ctor + vtable install + pool enrol; GTextureManager registry; lazy VFS-or-disk load into a handle; ref-counted static-handle acquisition; per-panel/per-effect GHTex allocation | GHTex / GHandle class layout and lifecycle now specced in `structs/texture_manager.md`; `specs/rendering.md` covers the draw side; `formats/texture.md` + `specs/vfs_overview.md` cover the load path. COVERED/RELOCATED | No further action required; see `structs/texture_manager.md` |
| dotoonshading cel pixel-shader loader (orphan) | ~2 | Loads `data/shader/dotoonshading.psh` and `data/shader/dotoonshading2.psh` via the VFS-first assembler path; zero direct callers (virtual-dispatched or device-restore callback); separate from the named cel/glow initializer | `formats/shaders.md` documents the five-shader cel set by role but does not name `dotoonshading.psh` / `dotoonshading2.psh` — filename discrepancy and orphan loader are unresolved | `@re-asset-format-analyst` confirm whether `dotoonshading.psh` is one of the five named cel shaders under a different filename, or a sixth shader; update `formats/shaders.md` |
| IME text-composition + candidate-window manager (partial) | ~30 | 32-slot candidate array, char-validity mask engine, ~25 functions not yet pinned to spec offsets; text-field HWND registration call site pending | `specs/input_ui.md §1c/§4/§5` covers behavior and five offsets; about half the object layout and all function-level names are missing | `@re-struct-analyst` complete the IME context struct; `@ida-toolsmith` name the major IME methods |
| Unmapped GU panel / window subclasses (residual HUD tail) | ~40 | ~35 autonamed panel-subclass functions with unnamed vtables; child-widget builders using known GU primitives but no class name resolved | `specs/ui_system.md §12.6` notes ~101 unlabelled builders; panel-slot roster partial in §1.9 | `@re-function-analyst` correlate vtable addresses to the §1.9 panel-slot roster and name the residual builders |

### Priority: MEDIUM

| Cluster | Approx. functions | Evidence basis | Gap description | Suggested RE next step |
|---|---|---|---|---|
| Shadow projection + actor blob-shadow stamp | ~7 | Perspective projection matrix (pi/8 FOV, aspect 1.0, far 10000), depth-bias matrix, LookAtLH light-view pass, per-actor blob-shadow quad draw | `specs/rendering.md §3.2` notes the blob-shadow pass at behavior level; the projective-shadow matrix recipe (numeric constants) and light-view pass are not pinned in the spec | `@re-function-analyst` extract the exact matrix constants and pin them in `specs/rendering.md §3.2` |
| Third-party XTrap (XTrapVa.dll) integration | ~30 | XTrap directory enumeration, LoadLibrary of XTrapVa.dll, GetProcAddress, telemetry packet builder (XlXf V1 frame) | DLL-load path and telemetry-packet details now covered in `specs/anticheat.md`. COVERED/RELOCATED | No further action required; see `specs/anticheat.md` |
| Anti-tamper self-integrity module-image check | ~11 | Maps the EXE image, builds a keyed reference block, two trailing-blob memcmps, on failure ships a status packet via the XlXf V1 builder | `specs/connection_topology.md §4` covers the relay endpoint; `specs/crypto.md §6a/§7.1` covers page-guard; the module-image self-check mechanics are not pinned | Document as a subsection of `specs/crypto.md` or extend `specs/anticheat.md` once produced |
| Cal3D-style skeletal animation / skin runtime engine (Core* family) | ~80 | RTTI CoreActor / CoreAnimation / CorePose / CoreSkin / CoreTrack / CorePoseManager / CoreSkinManager / SkinWeight::CoreSkin; runtime evaluation functions unnamed | `specs/skinning.md` + `formats/animation.md` cover the on-disk formats and the asset-resolution chain; the Core* runtime pose-blend, track keyframe sampling, and bone interpolation internals are unmapped | `@re-struct-analyst` map the CorePose + CoreTrack evaluation internals; extend `specs/skinning.md` with a runtime-engine section |
| Embedded OLE/IE web container (CWebContainer / cash-shop bridge) | ~25 | RTTI CWebContainer + CWebEventSink + full OLE COM site shim hierarchy (IOleClientSite / IOleInPlaceSite / IDispatch / IUnknown variants) | Only referenced as a UI panel slot in `specs/ui_system.md`; OLE hosting behavior, browser-event dispatch, and cash-shop URL loading are unmapped | `@re-function-analyst` document the OLE site hosting path and navigation entry point; produce a brief `specs/cash_shop_browser.md` |
| Diamond 3D scene-graph (GNode / GGroup / GScene / GView / GCamera / GPipeline family) | ~140 | Retained-mode scene hierarchy (GNode / GGroup / GGeometry / GGeode / GDrawable), cull/traverse render pipelines, GView render-to-surface, camera/light/particle resources | Node hierarchy and pipeline stack now specced in `structs/scene_graph_nodes.md`; draw side in `specs/rendering.md`; assets in `formats/`. COVERED/RELOCATED | No further action required; see `structs/scene_graph_nodes.md` |
| Normalized input-event emitters + cross-thread ring push (partial) | ~10 | Mouse and keyboard emitters, fixed event-record builder, cross-thread ring push; internal VK remap table (256 to internal range); key-state bitset | `specs/input_ui.md §2/§2a/§2c` confirms the event-record shape; the internal-VK remap table, bitset width, and VK-to-char shift table are not pinned — leaves a Ctrl-vs-Shift modifier ambiguity in §7 | `@re-struct-analyst` read the internal-VK remap table values; update `specs/input_ui.md §7` |
| Inventory 3D actor-preview draw | ~5 | Skinned actor inset inside inventory / info panel: AnimMixer pose-build, skin deform-and-upload, multi-stage texture-stage program, per-bone draw loop | `specs/rendering.md §4.2` notes the 3D inventory inset; the exact texture-stage program for the inset is a function-level gap | `@re-function-analyst` extract the texture-stage program and pin it in `specs/rendering.md §4.2` |
| Mount / vehicle visual-seat attach (naming completeness) | ~6 | Named functions confirmed consistent with `specs/world_systems.md Ch.20`; minor naming gap in the follow-spacing helpers | Specced and consistent; movement-tick sibling unnamed | `@ida-toolsmith` name the follow-spacing helper functions |

### Priority: LOW (utility / diagnostic / third-party internals)

| Cluster | Approx. functions | Evidence basis | Gap description | Suggested RE next step |
|---|---|---|---|---|
| D3D9 device wrapper + device-create / reset | ~20 | Direct3DCreate9 call site, IDirect3DDevice9 singleton, wrapper vtable forwarding device calls | `specs/rendering.md §2.0/§2.0.2` covers behavior; device-wrapper ctor and vtable populator are unnamed | `@ida-toolsmith` name the wrapper ctor and vtable populator |
| VFS-or-disk D3DX surface / texture load helpers | ~6 | Sky surface load, one-shot UI/icon texture loader, FPS digit texture loader — all VFS-first / disk-fallback pattern | `specs/vfs_overview.md` + `formats/texture.md` mostly named; minor naming gap in one-shot loaders | Low priority; `@ida-toolsmith` name residuals opportunistically |
| Screenshot capture subsystem | ~5 | Free-index filename builder, D3DXSaveSurfaceToFile backbuffer dump (BMP path), JPEG path via ijl11 ijlWrite, software pixel-format conversion | Not in any committed spec; `formats/texture.md` notes JPEG-export in one line | Produce a brief subsection in `specs/rendering.md` or a dedicated utility note; low RE investment needed |
| DirectX version-detection probe | ~4 | Reads file-version resources of system DLLs (ddraw.dll, d3d9.dll, dinput.dll, etc.) to classify installed DirectX version; sole DDRAW import — a diagnostic probe, not a rendering path | Not in any spec; confirms engine is pure D3D9 | Record in `specs/rendering.md` as a startup-diagnostic note; no further RE needed |
| GULabels / GUScroll / GUScrollEx widget internals | ~20 | Multi-line label draw, scrollbar thumb state machine, scrollEx drag capture | `specs/ui_system.md §12.8` notes these as not fully covered | `@re-function-analyst` document thumb-drag state machine; extend `specs/ui_system.md §12.8` |
| Mouse cursor-position service (singleton) | ~6 | Meyers singleton, GetCursorPos + ScreenToClient + clamp + cache; consumed by ~40 UI / HUD / tooltip callers | `specs/input_ui.md` covers WndProc cursor-hide and mouse-move record but not this singleton | Document in `specs/input_ui.md` as a sidebar |
| Anti-cheat page-protection guard (GProtect triad) | ~24 | RTTI GProtect / GGGProtect / GXProtect; VirtualProtect RW/RX page-guard toggle vfuncs | `structs/secure_context.md` + `specs/crypto.md` touch this partially; page-guard cadence and vfunc stubs unmapped | Extend `specs/crypto.md` or `structs/secure_context.md` with the page-guard mechanism |
| Quest eligibility gate evaluator | ~1 | Native C++ quest-gate function, no Lua VM involvement; reads level/class/stance/prereq off a keyed registry; returns status-color id | Not in `specs/lua_scripting.md` (correctly excluded as non-Lua); might belong in `specs/quests.md` | Verify against `specs/quests.md`; extend if missing |
| Slot/reel gamble match evaluator | ~1 | Cube-gamble match logic driven by static byte tables; native C++, no Lua VM | Not in any committed spec; a mini-game subsystem | Produce a note in `specs/world_systems.md` or a dedicated mini-game subsection |
| libVorbis 1.3.2 internal codec stages | ~176 | ~176 contiguous functions: mdct / codebook / floor / residue / bitpack internals | `specs/sound.md` records version and entry-point flags; internals are a third-party OSS codec body | Out-of-scope for RE; entry-point table already documented |

---

## 5. Deduplication and cross-probe reconciliation

Several clusters were surfaced independently by multiple probes. The following merges and
reconciliations apply:

- **Screenshot capture** appeared in both the render-d3d and asset-vfs probes as two separate
  entries with overlapping function sets. They are merged above into a single "Screenshot
  capture subsystem" gap entry covering both the BMP and JPEG output paths.
- **XTrap / anti-cheat** was split across three probes (net-ws2, crypto-anticheat, asset-vfs).
  The clusters map to three distinct subsystems that remain separate above: (a) the raw
  blocking-socket client (net-ws2, mostly specced in `connection_topology.md §4`); (b) the
  integrity-monitor and encrypted command agent (crypto-anticheat, HIGH gap); (c) the module
  self-image check (asset-vfs, MEDIUM gap). They share the XlXf V1 packet builder but are
  otherwise distinct.
- **GXProtect vs XTrap agent** are two separate subsystems that communicate: GXProtect is the
  in-house orchestrator; XTrap (via XTrapVa.dll) is the third-party vendor component. Both are
  HIGH/MEDIUM gaps with no committed spec beyond `specs/crypto.md §7.1`.
- **Streaming OGG audio loader** appeared redundantly in both sound-dsound and asset-vfs probes.
  Merged and treated as fully specced in `specs/sound.md §0/§2`.
- **libVorbis codec body** (~176 functions) appears in sound-dsound. These are the internal
  OSS codec stages; the entry-point flags are documented in `specs/sound.md`. The internals
  are classified out-of-scope (third-party static library body), not a coverage gap to close.
- **Diamond 3D scene-graph** (GNode / GScene / GView etc.) was surfaced by the ui-gu probe
  because some scene-graph functions share naming noise with GU panel subclasses. It is a
  distinct subsystem from the 2D UI toolkit; kept separate above.
- **NpcQuest_EvaluateEligibility** and the cube-gamble evaluator appeared as false-positive
  matches in the lua-script probe (matched on substring, have no Lua VM callees). Both are
  native C++ subsystems excluded from `specs/lua_scripting.md` and recorded as LOW-priority
  gaps in their own domains (quests and mini-games respectively).

---

## 6. Reading guide — committed spec index

The following `Docs/RE/` files are the authoritative references for the mapped subsystems above.
Engineers read these specs; this coverage map is navigation only.

**Networking and crypto**
- `Docs/RE/specs/network_dispatch.md` — frame assembly, opcode dispatch, I/O thread
- `Docs/RE/specs/connection_topology.md` — all four socket subsystems (game, lobby, XTrap, relay)
- `Docs/RE/specs/crypto.md` — wire cipher, LZ4, RSA handshake, SEED-128, page guard
- `Docs/RE/specs/login.md`, `specs/login_flow.md` — login state machine and packet flow
- `Docs/RE/packets/` — per-opcode wire-field YAMLs (130+ entries)
- `Docs/RE/structs/net_client.md`, `structs/net_handler.md`, `structs/net_packet_bodies.md`

**Rendering and shaders**
- `Docs/RE/specs/rendering.md` — per-frame draw loop, render-state cache, glow/bloom chain, cel pipeline
- `Docs/RE/formats/shaders.md` — `.psh` / `.vsh` shader source format; the five runtime-assembled shaders
- `Docs/RE/specs/effects.md`, `specs/effect-scheduling.md` — particle and effect subsystems
- `Docs/RE/specs/environment.md`, `specs/camera_movement.md` — sky, weather, camera

**Asset loading and formats**
- `Docs/RE/specs/vfs_overview.md`, `specs/resource_pipeline.md` — VFS structure and load path
- `Docs/RE/formats/terrain.md`, `formats/terrain_layers.md`, `specs/terrain-streaming.md` — terrain
- `Docs/RE/formats/skn.md`, `formats/animation.md`, `formats/actormotion.md` — character assets
- `Docs/RE/formats/mesh.md`, `formats/effects.md`, `formats/texture.md` — mesh, FX, texture formats
- `Docs/RE/specs/skinning.md` — bind/weight/skeleton resolution chain

**Sound**
- `Docs/RE/specs/sound.md` — device init, OGG pipeline, worker thread, volume buses, table loader
- `Docs/RE/formats/sound_tables.md` — per-area sound-schedule binary format

**UI, input, and HUD**
- `Docs/RE/specs/ui_system.md` — widget toolkit, screen layouts, scene state machine, font system
- `Docs/RE/specs/ui_event_dispatch.md` — command-handler, event-outlet, window-manager dispatch
- `Docs/RE/specs/input_ui.md` — two input sources, ring buffer, IME, normalized event records
- `Docs/RE/structs/gucomponent.md`, `structs/guwindow.md` — widget object layouts

**Gameplay systems**
- `Docs/RE/specs/world_systems.md` — actor model, movement, combat, death/respawn, dark subsystems
- `Docs/RE/specs/combat.md` — battle controller, target collection, action execution
- `Docs/RE/specs/skills.md`, `specs/buffs.md` — skill and buff systems
- `Docs/RE/specs/quests.md` — quest state machine
- `Docs/RE/specs/inventory_trade.md` — inventory, trade, stall
- `Docs/RE/specs/lua_scripting.md` — Lua 5.1.2 VM, lua_tinker binding, cpp_load API

**Structures**
- `Docs/RE/structs/actor.md` — Actor object layout
- `Docs/RE/structs/secure_context.md` — secure-context / page-guard struct
- `Docs/RE/structs/stats.md`, `structs/skill.md`, `structs/item.md`, `structs/quest_record.md`
- `Docs/RE/structs/spawn_descriptor.md`, `structs/npc.md`, `structs/runtime_singletons.md`

**Opcode catalogue**
- `Docs/RE/opcodes.md` — all recovered opcodes with direction, name, and spec cross-reference

---

## 7. Escalated gaps (no committed spec — RE domain action required)

The following gaps require new RE work and a new or extended committed spec before implementation
can proceed. They are listed here as formal escalations from this coverage audit to the RE domain
(via the main session / `docs-tooling-orchestrator`):

1. **Anti-cheat agent internals** — RELOCATED: now specced in `specs/anticheat.md`. No further escalation.
2. **GHTex / GTexture object layout** — RELOCATED: now specced in `structs/texture_manager.md`. No further escalation.
3. **SEED-128 consumer call sites** (open item in `specs/crypto.md §7.2`) — HIGH
4. **RC4 keying path** (new finding vs `specs/crypto.md §7.1`) — HIGH
5. **GXProtect class triad and orchestration flow** (behavioral only in `specs/crypto.md §7.1`) — HIGH
6. **dotoonshading.psh filename reconciliation** (open in `formats/shaders.md`) — HIGH
7. **FLINT++ bignum internals** (black box in `specs/crypto.md`) — HIGH
8. **Cal3D Core* runtime pose-blend / keyframe sampling** (not in `specs/skinning.md`) — MEDIUM
9. **Embedded OLE/IE web container** (absent from specs) — MEDIUM
10. **Diamond 3D scene-graph node hierarchy** — RELOCATED: now specced in `structs/scene_graph_nodes.md`. No further escalation.
11. **XTrap DLL integration path and telemetry packet shape** — RELOCATED: now specced in `specs/anticheat.md`. No further escalation.
12. **Shadow projection matrix constants** (not pinned in `specs/rendering.md §3.2`) — MEDIUM
13. **Internal VK remap table values** (open in `specs/input_ui.md §7`) — MEDIUM
14. **Screenshot capture subsystem** (absent from specs) — LOW
15. **DirectX version-detection probe** (absent from specs; startup-diagnostic only) — LOW
16. **Quest eligibility gate** (possible gap in `specs/quests.md`) — LOW
17. **Cube-gamble match evaluator** (absent from specs) — LOW
18. **`.bud` per-vertex bytes 12–31 UNVERIFIED** — five f32 fields beyond the XYZ position triple are present in each vertex record but their roles are not decoded; normals and UVs are confirmed resolved in `formats/terrain_scene.md`; the remaining vertex-tail gap lives in `formats/terrain.md §8` — HIGH (`@re-asset-format-analyst`)
19. **`.fx7` header and vertex format UNVERIFIED** — the header and per-vertex layout for the `.fx7` terrain-effect layer are structurally distinct from `.fx1`–`.fx6` and are not yet specced in `formats/terrain_layers.md` — HIGH (`@re-asset-format-analyst`)

---

## 8. File-format read-path census

> **Provenance** — 2026-06-26, **reconciliation RESOLVED 2026-06-27 (CYCLE 14)**: this section was first
> promoted from a static session whose loaded `doida.exe` build (SHA-256 prefix `f61f66a9`) differed from
> the then-anchor `263bd994`. The maintainer has since designated **`f61f66a9` (3807 KB) as the canonical
> target**; `names.yaml` is re-pinned to it (see `journal.md` CYCLE 14, and `_dirty/reverify-f61f66a9/`
> for the measured build delta: a ~128-byte `.text` insertion + a ~0x1000 data-page shift, 96% of moved
> functions cleanly relocated by +0x80/+0x7e). The loader-dispatch structure recorded here is build-stable
> and holds against f61f66a9. Spec `verification:`/`ida_anchor:` banners are being re-stamped to
> `f61f66a9` **per-spec** as each subsystem is re-verified in the CYCLE 14 lane waves (not in bulk).
>
> **Coverage legend:** WELL = dedicated `formats/*.md` or `specs/*.md`, sample-verified, no open byte
> gaps; PARTIAL = covered but one or more named byte fields are UNVERIFIED; N/A = disabled stub or
> confirmed absent from the shipping client; UTILITY = write-only export path (no read spec required).

The headline finding of this census: the committed `Docs/RE/formats/` and `Docs/RE/specs/` corpus
covers **every file-format read path present in the loader dispatch** of the loaded image. No extension
lacks a spec. Residual work is narrow byte-field verification on two format families and one shader
filename question; those are formally escalated in §7 items 6, 18, and 19.

### Container and VFS

| Extension | Role | Spec | Coverage |
|---|---|---|---|
| `.inf` + `.vfs` | `data.inf` TOC plus `data/data.vfs` blob; ReadFile-into-buffer archive with no compression or encryption | `formats/pak.md`, `specs/vfs_overview.md` | WELL |

### Terrain and world cell

Sub-assets in this group are opened from the DATAFILE and BUILDING token lists that the `.map`
cell-master emits; all paths are path-template driven.

| Extension | Role | Spec | Coverage |
|---|---|---|---|
| `.map` | Per-cell scene and section descriptor; emits DATAFILE/BUILDING tokens for sub-asset expansion | `formats/terrain.md`, `formats/terrain_scene.md`, `formats/authoring_sidecars.md` | WELL |
| `.mud` | Per-cell tile/sound master; fixed 32,768-byte body (64×64 grid at 8 bytes per slot) | `formats/mud.md` | WELL |
| `.ted` | Terrain heightfield plus per-cell texture-index grid | `formats/terrain.md` | WELL |
| `.ted.post` | Terrain authoring sidecar; write-only export target (same 46,987-byte stride as `.ted`) | `formats/authoring_sidecars.md` | WELL |
| `.up` | Cell up-layer collision sidecar | `formats/cell_up.md` | WELL |
| `.exd` | Cell extra-data sidecar; name-driven sibling path | `formats/cell_exd.md` | WELL |
| `.sod` | 2D XZ collision wall segments | `formats/sod.md` | WELL |
| `.bud` | Cell static building mesh: u32 objectCount followed by MassObject records | `formats/terrain.md §8`, `formats/terrain_scene.md` | PARTIAL — per-vertex bytes 12–31 (five f32 beyond XYZ) UNVERIFIED; normals and UVs are confirmed resolved in `formats/terrain_scene.md`; see §7 item 18 |
| `.fx1`–`.fx7` | Per-cell terrain effect and decoration layer meshes | `formats/terrain_layers.md`, `specs/effects.md` | PARTIAL — `.fx1`–`.fx6` specced; `.fx7` header and vertex format UNVERIFIED; see §7 item 19 |
| `.gad` | Cell open-order slot — no-op stub in this build; the call succeeds but never opens a file | `formats/area_inventory.md`, `formats/cell_exd.md` (noted) | N/A (disabled stub) |
| `.lst` (cell key) | Per-area cell-key set driving the streaming filename rule | `formats/region_grid.md`, `formats/area_inventory.md` | WELL |

### Region, spawn, and map data

| Extension | Role | Spec | Coverage |
|---|---|---|---|
| `.bin` (region) | Region grid, region-table, and per-area map data blobs | `formats/region_grid.md` | WELL |
| `.bin` (gather) | Gather-node table | `formats/region_grid.md`, `formats/tol.md` | WELL |
| `.arr` (npc) | NPC spawn array; 28-byte records | `formats/npc_spawns.md` | WELL |
| `.arr` (mob) | Mob spawn array; 20-byte records | `formats/npc_spawns.md` | WELL |

### Character, mesh, and animation

| Extension | Role | Spec | Coverage |
|---|---|---|---|
| `.skn` | Character and item skin mesh (rigid and weighted variants) | `formats/skn.md`, `formats/mesh.md` | WELL |
| `.bnd` | Deform skeleton and bind pose | `formats/mesh.md`, `formats/animation.md`, `formats/bindlist.md` | WELL |
| `.mot` | Motion and animation clips, keyed by header id | `formats/animation.md`, `formats/actormotion.md` | WELL |
| character catalogue `.txt` | skin/skinlist/motlist/bindlist/actormotion/userjoint/tex-resolution-list/emoticon manifests | `formats/actormotion.md`, `formats/bindlist.md`, `formats/skn.md`, `formats/text_tables.md` | WELL |

### Static mesh and FX objects

| Extension | Role | Spec | Coverage |
|---|---|---|---|
| `.xobj` | ASCII whitespace-tokenized static triangle mesh | `formats/xobj.md`, `formats/mesh.md` | WELL |
| `.ion` | Effect and scene object format | `formats/ion.md` | WELL |
| `.tol` | Tool/region object-list format | `formats/tol.md` | WELL |
| `.mi` | Misc index and instance table | `formats/mi.md` | WELL |
| `.eff` (particle) | Particle emitter and effect definition | `formats/effects.md` | WELL |
| effect `.lst`/`.txt` | xeffect.lst, xobj.lst, bmplist.lst, particle_list, equipment joint-effect and sword-light catalogues | `formats/effects.md` | WELL |

### Script and table data

| Extension | Role | Spec | Coverage |
|---|---|---|---|
| `.scr` | Binary game-logic tables (items, skills, mobs, npcs, quests, events, and others) | `formats/scr.md`, `formats/items_scr.md`, `formats/events_scr.md` | WELL |
| `.do` | Sibling text and data tables (msginfo, errorinfo, emoticon, textcommand, items_extra, per-class skill stance) | `formats/config_tables.md`, `formats/text_tables.md`, `formats/scr.md` | WELL |
| `.xdb` | Structured data tables: caption DB plus small fixed-record tables (actor_size, vehicle, effectscale, buff_icon_position, creature_item) | `formats/xdb_tables.md`, `formats/msg_xdb.md` | WELL |
| `.lua` | Lua 5.1.2 config, UI, and tutorial scripts | `formats/lua.md`, `specs/lua_scripting.md`, `specs/lua-config.md` | WELL |
| `.csv` | Flat comma-delimited item table — confirmed absent from the shipping client (zero references in the loaded image) | `formats/items_csv.md` | N/A (authoring artifact; not loaded at runtime) |
| miscellaneous `.txt` | command/gmmapmove/ip/UiTex/skillicon/crestlist catalogues | `formats/macro_file.md`, `formats/ui_manifests.md`, `formats/text_tables.md` | WELL |

### Sound

| Extension | Role | Spec | Coverage |
|---|---|---|---|
| sound tables (`.eff`, `.bge`, `.bgm`, `.run`, `.wlk`) | Per-area sound-schedule tables (effect/ambient/music/run/walk variants) | `formats/sound_tables.md`, `specs/sound.md` | WELL |
| `.ogg` | Streamed Vorbis audio (2D non-positional and 3D positional) | `formats/sound_tables.md`, `specs/sound.md` | WELL |

### Texture and image

| Extension | Role | Spec | Coverage |
|---|---|---|---|
| `.dds` | Primary texture container (world, item, UI, and effect) | `formats/texture.md` | WELL |
| `.tga` | Effect and UI textures; the file header is authoritative and the extension is a hint only | `formats/texture.md` | WELL |
| `.bmp` | Terrain lightmap tiles, bigmap tiles, and toon-shading LUT | `formats/texture.md`, `formats/shaders.md` | WELL |
| `.png` | Character and item skin textures | `formats/texture.md` | WELL |
| `.jpg`/`.jpeg` | Screenshot export only, via the ijl11 write path; not an asset-read format | `formats/texture.md` (one line) | UTILITY — write-only; no read spec required |

### Sky, environment, shaders, and index

| Extension | Role | Spec | Coverage |
|---|---|---|---|
| sky `.bin` | Sky, weather, fog, light, cloud, material, dome, and map_option data blobs | `formats/sky.md`, `formats/environment_bins.md` | WELL |
| `bgtexture.lst` | Terrain texture index: u32 count followed by 48-byte kind-selected records | `formats/bgtexture_lst.md` | WELL |
| `.psh`/`.vsh` | D3D9 cel and glow shader sources assembled at load | `formats/shaders.md` | PARTIAL — `dotoonshading.psh` and `dotoonshading2.psh` are loaded by an orphan loader whose filename is not reconciled against the five named cel shaders in `formats/shaders.md`; see §7 item 6 |

### Census summary

Every file-format read path surfaced by the loader dispatch maps to at least one committed spec.
The three remaining format-level open items are:

- **`.bud` vertex tail** — per-vertex bytes 12–31 beyond XYZ UNVERIFIED; formally escalated as §7 item 18.
- **`.fx7` header and vertex format** — distinct from `.fx1`–`.fx6` and UNVERIFIED; formally escalated as §7 item 19.
- **`dotoonshading.psh` naming** — orphan loader filename not reconciled with the named cel-shader set in `formats/shaders.md`; formally escalated as §7 item 6.

Object-model struct gaps (GHTex layout, Diamond scene-graph node hierarchy) are format-independent
runtime gaps already carried as §7 items 2 and 10.
