---
verification: confirmed (re-confirmed against IDB SHA 263bd994, actor-world audit (2026-06-24))
ida_reverified: 2026-06-24
ida_anchor: 263bd994
evidence: [static-ida]
conflicts: Renderer object (§4) and 16-slot render pointer-cache (§4.1) not re-walked this pass (static-hypothesis); NetClient inner field offsets (§3.3) and ActorManager inner fields (§3.7) carried from prior dirty note (static-hypothesis); keepalive interval UNIT (ms vs s) and several MainWindow service-slot identities (§3.10) static-hypothesis. 2026-06-17 Campaign-17 re-confront (263bd994): the MainWindow +0x500 write is now CODE-CONFIRMED, and its occupant is a distinct ~0xC8-byte state-5 command handler (NOT the 16-byte MainHandler hub) — §3.10 / §3.10a / §6 corrected. 2026-06-20 CYCLE 7: display FRAMERATE config inertness RESOLVED — statically exhaustive (the only two references to the config field are both stores, no reader), so the 60 FPS cap is confirmed hardcoded/inert (§16); the Diamond base-object layout (§3.0) is added
status: code-confirmed
sample_verified: false   # layout recovered from binary analysis; no live capture
subsystems: [scene_lifecycle, render_pipeline, network, actor, sound, vfs, ui_system, effects, game_loop, input, scripting]
---

# Runtime Singletons & Binary Module Map — Clean-Room Specification

> **Neutral, rewritten specification. No decompiler identifiers, no binary addresses, no
> pseudo-code.** Promoted from dirty-room analysis notes under EU Software Directive
> 2009/24/EC Art. 6, solely to document the object layout sufficient for clean-room
> reimplementation. Field offsets below are **relative to the start of each object** and are
> file/struct offsets — they are not virtual-address constants and must never be treated as
> memory addresses in the reimplementation.
>
> **Confidence vocabulary used in this document:**
> - **CODE-CONFIRMED** — value or layout recovered from binary instruction stream and
>   corroborated by multiple use sites.
> - **PLAUSIBLE** — consistent single-source inference; implement but keep tunable.
> - **UNVERIFIED** — hypothesis only; do not hard-code.
>
> **Scope.** This file covers:
> - The Meyers-singleton objects (the 19 originally catalogued plus the `MainHandler` hub and
>   `AppService` recovered in the 2026-06-16 re-verification) and their key field maps.
> - The render-subsystem pointer-cache block initialised at scene-state 5 (static-hypothesis,
>   not re-walked this pass — see §4 / §4.1).
> - The flat VFS globals (not a singleton object) — the TOC pointer/count/handle plus a distinct
>   mount-flag byte and a three-word load-progress sub-block.
> - The construction order of all singletons.
> - The binary module map: function counts per subsystem, engine brand, compiler, embedded
>   third-party libraries, and the address-space ordering of subsystem clusters.
>
> **What this file is not.** It does not restate the full behavioural specification of any
> subsystem — cross-reference the relevant `specs/` or `formats/` file for that. This file
> documents *object identity, approximate size, and key field offsets* so that integration
> engineers know which runtime object owns which data.

---

## 1. Singleton idiom used throughout the client

The client uses the C++ "Meyers singleton" pattern uniformly: each manager exposes a
`GetSingleton()` function that tests a one-shot init guard, calls the constructor exactly once
(storing the result in a static object embedded in the data segment), and registers a destructor
via `atexit`. All static storage is embedded in the binary's global-data region. There is no
heap-allocated singleton registry or factory table.

Consequences for the reimplementation:
- Each singleton has exactly one instance; the C# reimplementation should likewise use a single
  instance (registered in a DI container or a static field as appropriate).
- The singletons have a fixed construction order driven by first-use sequencing in the main
  entry point and a second wave triggered by the render-subsystem initializer at scene-state 5
  (see §5).
- Because all statics live in the data segment, their sizes are hard-coded in the binary. The
  sizes listed below are authoritative for the legacy object but need not be reproduced in a
  managed reimplementation — they are provided so engineers understand the relative weight of
  each subsystem.

---

## 2. Master singleton table

The table lists every confirmed Meyers singleton. **`object_size`** is the byte span of the
embedded static object (CODE-CONFIRMED unless marked otherwise).
**`key field(s)`** points to the offset table in §3 for that singleton.

| Canonical name | Role | Object size (B) | Key fields | Confidence |
|---|---|---:|---|---|
| `GameState` | Scene state machine (states 0..7, 8 cases; 8 is a sub-state value); the WinMain dispatch key | 16 | §3.1 | CODE-CONFIRMED |
| `LuaConfig` | Lua 5.1.2 interpreter init and `game.lua` reader | ~44 | §3.2 | PLAUSIBLE |
| `BugTrap` | Crash-reporter init (BugTrap.dll wrapper) | ~16 | — | PLAUSIBLE |
| `NetClient` | TCP connection, cipher, LZ4, keepalive threads | 82 368 | §3.3 | CODE-CONFIRMED (size re-verified; inner offsets static-hypothesis) |
| `NetHandler` | 154-slot S2C response + push dispatch tables | 6 220 | §3.4 | CODE-CONFIRMED |
| `InputManager` | DirectInput polling + Win32 message dispatch | 116 | — | CODE-CONFIRMED |
| `BillingState` | Subscription / shop-page state machine | 256 | §3.5 | CODE-CONFIRMED |
| `AnimCatalog` | Animation clip catalog (keys `.mot` files) | 11 704 | — | CODE-CONFIRMED |
| `CoreMotManager` | Animation clip/motion manager (thin wrapper) | 16 | §3.6 | PLAUSIBLE |
| `CorePoseManager` | Pose-blending state manager | 16 | §3.6 | PLAUSIBLE |
| `CoreSkinManager` | Skinned-mesh cache manager | 16 | §3.6 | PLAUSIBLE |
| `ActorManager` | In-world entity container + spatial index | 300 | §3.7 | CODE-CONFIRMED (size re-verified; inner offsets static-hypothesis) |
| `SoundManager` | DirectSound wrapper, 3 internal sound lists | ~640 | §3.8 | CODE-CONFIRMED |
| `GHTexManager` | Primary effect/texture manager (EffectManager vtable) | 620 | §3.9 | CODE-CONFIRMED |
| `ShadowManager` | Per-actor shadow rendering | 316 | — | CODE-CONFIRMED |
| `RankProgress` | Rank/progression state | 308 | — | CODE-CONFIRMED |
| `MainWindow` ("MainMaster") | Root Diamond::GUWindow + 223-slot HUD-panel service block | 1 464 | §3.10 | CODE-CONFIRMED |
| `MainHandler` (hub) | Small 16-byte handler-hub object (distinct from `MainWindow` **and** from the ~0xC8-byte +0x500 state-5 handler; busiest accessor in the binary) | 16 | §3.10a | CODE-CONFIRMED |
| `AppService` | Core client-runtime app-service singleton | 136 | §3.13 | CODE-CONFIRMED |
| `Engine` | Main-loop aggregate: holds Renderer + InputManager ptrs | 48 | §3.11 | CODE-CONFIRMED |
| `FrameTickScheduler` | Per-subscriber tick dispatcher | 72 068 | §3.12 | CODE-CONFIRMED |

> **`MainWindow`, `MainHandler` hub, and the +0x500 state-5 handler (re-verification 2026-06-17).**
> Three distinct objects are involved, and the name "MainHandler" has been overloaded across two of
> them. `MainWindow` ("MainMaster") is the 1 464-byte root HUD window that owns the 223-slot service
> block (§3.10). The **`MainHandler` hub** is a separate **16-byte** object (§3.10a) — its accessor
> is the single busiest accessor in the binary. The object actually stored into the window's
> **+0x500** service slot is a **third, larger object: a ~0xC8-byte (≈200-byte) command-handler-derived
> state-5 in-game handler**, written at the scene-state-machine entry to the in-game state — **not**
> the 16-byte hub (§3.10 / §3.10a). The §3.10 service-slot *layout* is correct as documented; the
> +0x500 write site is now **CODE-CONFIRMED** (it was a static-hypothesis), and the occupant's
> identity is corrected to the ~0xC8-byte state-5 handler.

> **Note on the Renderer.** The Renderer object (`Diamond::GHRenderer`, ~177 860 bytes,
> roughly 174 KB) is the largest static object in the binary. It is not constructed via a
> standard Meyers singleton guard; instead it is a raw static embedded in the data segment
> and its address is cached into a pointer-slot when the render subsystem is first initialised
> at scene-state 5. See §4 for its field map and §4.1 for the pointer-cache block.

---

## 3. Key field maps (offsets relative to object start)

All offsets are within the object. "→" notation means the field holds a pointer to another
object; the pointed-to type is named but its layout is in its own spec section or file.

### 3.0 Diamond base object — shared head of every engine class (CODE-CONFIRMED)

Cross-reference: `specs/client_architecture.md §1A` for the object-model / RTTI-lifecycle behaviour.

The 3D layer is the **"Diamond" scene-graph engine** (an OpenSceneGraph-style Direct3D 9 scene
graph; ~90 engine RTTI classes). A single abstract refcounted base, `GObject`, anchors the
hierarchy `GObject → GNode → GGroup → {GScene, GTransform, GGeode, GSwitch, GLight, …}`, with
`GViewPlatform` branching off `GNode`. **Every** Diamond class (render-state `GRS*`, UI `GU*`,
pipeline, asset, camera families) begins with the same head:

| Offset | Size | Type | Field | Notes |
|-------:|-----:|------|-------|-------|
| +0x00 | 4 | void** | `vtable` | Installed by each constructor; the abstract base's slot 0 is a pure-virtual stub. |
| +0x04 | 4 | uint32 | `ref_count` | Intrusive reference count, initialised to 0. `ref()` = inlined increment; `unref()` = assert-non-zero then decrement. |
| +0x08 | 0x1C | std::string | `name` | Object name; the constructor allocates, the destructor frees. |
| +0x3C | 0x0C | std::vector<node*> | `parents` | Parent back-reference vector (introduced at the `GNode` level): begin / end / capacity. |
| +0x4C | 0x0C | std::vector<node*> | `children` | Child vector (introduced at the `GGroup` level): begin / end / capacity. `addChild` `ref()`s the child and pushes it; `removeChild` `unref()`s and erases. |

**Refcount mechanism (OpenSceneGraph "Referenced" pattern).** `ref()` is an inlined increment of
+0x04 performed when a child is added; `unref()` is a dedicated routine that asserts `ref_count`
is non-zero before decrementing. There is **no AddRef / Release vtable slot** — these are
non-virtual operations on +0x04.

**Destruction.** Virtual-table slot 0 is the MSVC "vector deleting destructor": it takes a flags
byte, runs the real destructor body, then frees the object when `(flags & 1)`. So `delete obj`
runs the destructor chain (derived → base, each releasing its owned children) then frees the
`operator new` heap block.

**Ownership.** `GGroup` / `GNode` hold the child vector and free children **by refcount**: a child's
vector-deleting destructor is invoked only when its `ref_count` reaches 0. Parent back-references
live in the separate `parents` vector (+0x3C).

**`GGroup` virtual interface (slot roles, neutral).** Slot 0 = vector-deleting destructor; a
`removeAllChildren` slot (unref + detach each); an `addChild` slot (ref the child, register the
parent, push); a `removeChild` slot (find, unregister the parent, unref, erase); and a
bounds/dirty-flag propagation slot that walks the **parent** chain via a flags byte at **+0x38**
(this dirty-flag byte is **not** the refcount).

**`GViewPlatform`** is a `GNode` subclass with object size **0x54 (84 bytes)**; `GScene` / `GGroup`
share the same +0x00 / +0x04 / +0x08 head as above.

> **Allocator note.** All Diamond and gameplay objects are allocated via plain CRT
> `operator new → malloc → HeapAlloc` on the process heap — there is **no** custom game pool /
> arena / free-list; placement-`new` appears only as STL in-place construction. (See
> `specs/client_architecture.md §1A.6`.)

### 3.1 GameState — 16 bytes (CODE-CONFIRMED)

Cross-reference: `specs/client_runtime.md §7` for the scene-state lifecycle description.

The WinMain driver runs `while(1) switch(scene_state)`, where the switch is a **bounds-checked
jump table of exactly 8 entries (top-level states 0..7)** plus a default branch. The value 8 is
**not** a 9th top-level case — it is the default/sentinel value carried in the `const_field`
sub-state slot below (e.g. state 5 sub 8, state 6 sub 8). Any earlier wording describing a
"9-state lifecycle" or "states 0..8" is corrected to **states 0..7 (8 cases); 8 is a sub-state
value**.

| Offset | Size | Type | Field | Notes |
|-------:|-----:|------|-------|-------|
| +0x00 | 4 | int32 | `scene_state` | The WinMain scene-state discriminator; values 0..7 select the 8-entry jump table. Heavily cross-referenced throughout the binary. CODE-CONFIRMED. |
| +0x04 | 4 | int32 | `const_field` | Initialised to 8 (CODE-CONFIRMED init value). Acts as the sub-state slot; the default sub-state value 8 lives here. The hypothesis that 8 also denotes a max/exit index is (static-hypothesis) — role still inferential. |
| +0x08 | 4 | int32 | `third_field` | Initialised to 0 (CODE-CONFIRMED init value); role unverified (static-hypothesis). |
| +0x0C | 1 | uint8 | `debug_mode` | Set from the `debugmode` key in `game.lua` (WinMain writes `debug_mode = (LuaConfig debugmode != 0)`). 0 in release builds. CODE-CONFIRMED. |
| +0x0D | 3 | — | (pad) | Alignment to 16-byte object end (init guard sits at +0x10). |

### 3.2 LuaConfig — ~44 bytes (PLAUSIBLE)

Cross-reference: `specs/lua_scripting.md` for full Lua scripting behaviour.

| Offset | Size | Type | Field | Notes |
|-------:|-----:|------|-------|-------|
| +0x00 | 4 | ptr | vtable or lua_State* | The static holds a thin wrapper over a heap-allocated `lua_State`. The actual `lua_State` size is not part of this static. |
| +0x04 | ~40 | bytes | (wrapper fields) | Wraps init-guard, loaded-file tracking, and the `cpp_load` export. Internal layout unverified. |

> **Open question §7.9** — the static may be only 4–8 bytes (vtable + `lua_State*`), with the
> guard sitting at byte offset 4. The ~44-byte estimate is derived from guard-offset arithmetic
> and may overshoot. Trace `LuaConfig_Init` to confirm.

### 3.3 NetClient — 82 368 bytes (CODE-CONFIRMED size; inner offsets static-hypothesis)

Cross-reference: `specs/login_flow.md`, `specs/handlers.md` for protocol behaviour.

> **Re-verification note (2026-06-16).** The object identity and the total size (82 368 bytes) are
> CODE-CONFIRMED by re-deriving the accessor static and init-guard span. The *inner field offsets*
> below were **not individually re-walked this pass** (the connection-init routine was not opened)
> and are carried forward from the prior dirty note as **(static-hypothesis)**. The keepalive
> *value* is corroborated: the `NetHandler` constructor passes `20` to the compressed-keepalive
> arming routine (see §3.4) — the numeric value 20 is CODE-CONFIRMED; whether the field stores
> 20 seconds or 20 000 ms is (static-hypothesis).

| Offset | Size | Type | Field | Notes |
|-------:|-----:|------|-------|-------|
| +0x00 | 4 | ptr | vtable | `Diamond::Network` vtable. |
| +0x28 | ~12 | obj | `server_addr_string` | `std::string` caching the last-connected server address. (static-hypothesis) |
| +0x48 | ~72 | obj | `net_conn` | Embedded connection object (TCP socket, async I/O, send/recv queues). (static-hypothesis) |
| +0x141B8 | 1 | uint8 | `connected` | Connected flag (non-zero when TCP link is established). Byte offset ~82 296 from object start. (static-hypothesis) |
| +0x141C8 | ~16 | obj | `send_thread` | ThreadSlot for the async send worker. (static-hypothesis) |
| +0x141D8 | ~16 | obj | `keepalive_thread` | ThreadSlot for the keepalive sender. (static-hypothesis) |
| +0x141E4 | 4 | uint32 | `keepalive_interval` | Keepalive interval; the arming value is 20 (CODE-CONFIRMED via the `NetHandler` ctor); seconds-vs-milliseconds unit is (static-hypothesis). |
| +0x141EC | 4 | uint32 | `outstanding_acks` | Outstanding unacknowledged-packet count. (static-hypothesis) |

### 3.4 NetHandler — 6 220 bytes (CODE-CONFIRMED)

Cross-reference: `specs/handlers.md` for opcode dispatch behaviour; `opcodes.md` for the full
opcode catalogue.

| Offset | Size | Type | Field | Notes |
|-------:|-----:|------|-------|-------|
| +0x00 | 4 | ptr | vtable (primary) | First vtable pointer (primary interface). |
| +0x28 | 4 | ptr | vtable (secondary) | Second interface vtable slot. |
| +0x2C | 4 | ptr | vtable (tertiary) | Third interface vtable slot. |
| +0x30 | 4 | ptr | `actor_manager` | Pointer to the `ActorManager` singleton; set during construction. |
| +0x34 | 8 | uint32[2] | (reserved) | Initialised to 0. |
| +0x3C | 4 400 | CharSlot[5] | `char_slots` | Five character-select scratch objects, 880 bytes each. Holds character data during the character-select screen (before entering the world). |
| +0x1378 | 616 | ptr[154] | `response_handler_table` | 154 function-pointer slots for server-response (S2C) handlers. Pre-filled with a no-op handler; real handlers installed by the response-table installer. |
| +0x15E0 | 616 | ptr[154] | `push_handler_table` | 154 function-pointer slots for server-push (S2C push) handlers. Same initialization pattern. |

> **Note on table sizing.** 154 slots × 4 bytes = 616 bytes per table. The two tables together
> account for 1 232 bytes of the 6 220-byte object. The remaining space is consumed by the five
> 880-byte `CharSlot` records (4 400 bytes total) plus the header fields.

> **Note on initialisation.** The constructor pre-fills both tables with a single no-op handler
> function, then calls two table-installer routines to overwrite the real handler slots. It also
> stages the initial client handshake packet (major=2, minor=10000) before any authentication
> flow begins.

### 3.5 BillingState — 256 bytes (CODE-CONFIRMED)

Cross-reference: `names.yaml` (client_mechanics.BillingState).

| Offset | Size | Type | Field | Notes |
|-------:|-----:|------|-------|-------|
| +0x00 | 1 | uint8 | `flag_byte` | Initialised to 0. |
| +0x04 | 4 | int32 | `field_1` | Initialised to -1. |
| +0x08 | ~56 | obj | `notification_text` | `std::string` set by a server command (minor 19). |
| +0x48 | 5 | uint8[5] | `clear5` | Zero-initialised. |
| +0x50 | 4 | uint32 | `revision` | Initialised to 1. |
| +0x54 | 4 | uint32 | `current_shop_page` | Current shop page index; written by a CharMgmt push (minor 8). |
| +0x58 | 1 | uint8 | `subscription_active` | Initialised to 1 (active). Toggled by ServerCommand minors 16 and 17. |
| +0x59 | 2 | uint8[2] | `flags_59_5A` | Initialised to 0. |
| +0x5C | 20 | uint8[20] | `billing_zone` | Zone context for billing; zero-initialised. |
| +0x70 | 20 | uint32[5] | `misc_zeros` | Zero-initialised. |
| +0x84 | 121 | uint8[121] | `billing_history` | Billing history blob; zero-initialised. |
| +0xFF | 1 | — | (object end) | Object boundary at 256 bytes. |

### 3.6 Core animation managers — 16 bytes each (PLAUSIBLE)

Three compact manager singletons in the animation pipeline. All three have the same layout
(16 bytes). Cross-reference: `specs/skinning.md`.

| Offset | Size | Type | Field | Notes |
|-------:|-----:|------|-------|-------|
| +0x00 | 4 | ptr | vtable | Virtual method table pointer. |
| +0x04 | ~12 | bytes | (manager fields) | One or two heap pointers or internal state flags. Internal layout unverified. |

**Construction order** (relative, within §5): `CoreMotManager` → `CorePoseManager` → `CoreSkinManager`,
all constructed before `ActorManager`. They are initialised during the Actor subsystem startup
(scene-state 1 or 2; see §5 item 10 below).

**Probable roles** (PLAUSIBLE — not yet confirmed by tracing animation-update call paths):
- `CoreMotManager` — animation clip / `.mot` file catalog manager; bridges `AnimCatalog` to the
  per-actor animation mixer.
- `CorePoseManager` — pose-blending state manager; supplies the blended joint transforms to the
  skinning path.
- `CoreSkinManager` — skinned-mesh cache; manages vertex-buffer sets for CPU linear blend
  skinning (see `specs/skinning.md`).

> **Open question §7.10.** None of the three have been traced into the animation-update call
> graph. Confirm roles by following the call chain from `Actor__LockVB_RebuildSkin_Unlock`
> through the per-frame update path.

### 3.7 ActorManager — 300 bytes (CODE-CONFIRMED size; inner offsets static-hypothesis)

Cross-reference: `structs/actor.md` for the entity layout; `names.yaml` (`g_ActorManager`,
`g_LocalPlayer`).

> **Re-verification note (2026-06-16).** The object identity and the total size (300 bytes) are
> CODE-CONFIRMED by re-deriving the accessor static and init-guard span; RTTI confirms class
> `ActorManager` over a base `CoreActorManager`. The *inner field offsets* below (name string,
> `local_player_slot`, `spatial_index`, etc.) were **not fully re-walked this pass** and are
> carried forward as **(static-hypothesis)**. The companion pointer-slot below **is** re-confirmed.

| Offset | Size | Type | Field | Notes |
|-------:|-----:|------|-------|-------|
| +0x00 | 4 | ptr | vtable | `ActorManager` vtable. (static-hypothesis) |
| +0x04 | 4 | uint32 | `ref_count` | Reference count; incremented to 1 during construction. (static-hypothesis) |
| +0x08 | ~44 | obj | `name_string` | `std::string` set to `"ActorManager_group"` during construction. (static-hypothesis) |
| +0x38 | 1 | uint8 | `state_byte` | Initialised to 2. (static-hypothesis) |
| +0x70 | 4 | ptr | `local_player_slot` | Pointer slot for the local-player actor; points into a nearby static slot. (static-hypothesis) |
| +0x74 | ~192 | obj | `spatial_index` | Embedded spatial-index sub-object (collision / quadtree); constructed by its own internal constructor. Opaque to the domain model. (static-hypothesis) |
| +0x10C | 4 | uint32 | (reserved) | Zero-initialised. (static-hypothesis) |
| +0x114 | 4 | uint32 | (reserved) | Zero-initialised. (static-hypothesis) |
| +0x118 | 1 | uint8 | `flag` | Zero-initialised. (static-hypothesis) |
| +0x11C | 4 | uint32 | (reserved) | Zero-initialised. (static-hypothesis) |
| +0x120 | ~16 | obj | `secondary_index` | Secondary lookup sub-object (tree/list complement to the spatial index). (static-hypothesis) |

> **Companion pointer-slot (CODE-CONFIRMED).** A separate 4-byte pointer in the global data region
> (distinct from the ActorManager object itself) is set to the ActorManager's address during
> `NetHandler` construction — re-confirmed: the `NetHandler` ctor stores the ActorManager accessor
> result into `NetHandler +0x30` (the routing pointer; this is also the first point at which the
> ActorManager singleton is constructed). It is used by the two primary actor-lookup hot paths:
> the id-keyed lookup and the `(id, sort)` composite-key lookup. This pointer is the global
> cross-reference that routes all entity accesses through the ActorManager singleton.
>
> **ActorHashMap (id-keyed map global).** A separate 4-byte pointer in the global data region holds
> the address of an actor hash-map/tree used in the id-keyed lookup hot path (`ActorManager_FindActorById`);
> node payload (actor pointer) sits at node+16 (the 4th dword of each node). The `(id, sort)` MRU
> cache is held in the first dword of `ActorManager +0x70`. The type (std::map or custom tree) is
> not yet confirmed — see open question §7.7.
>
> **Cluster-adjacent data-segment globals (actor-world audit, CODE-CONFIRMED).** Four globals in the
> same data-segment cluster back the container notes above and the mob/NPC loader specs:
>
> | Role | Notes |
> |---|---|
> | id-keyed actor map node root | pointed to by the ActorHashMap global above |
> | boss-mob secondary index | populated by the `mobs.scr` loader for records with `mob_type == 11`; keyed by mob id |
> | `npcs.scr` primary index | populated by the `npcs.scr` loader; primary u16 key @+0 |
> | `npcs.scr` secondary (relationship) index | populated by the relationship table (@record+128); first dword of each 16-byte entry |
>
> These are dirty-room labels; the clean-room references are the container roles described above and in
> `structs/npc.md §1A`.
>
> **Local buff-bar mirror.** A 360-byte global mirrors the local player's 30-slot buff-bar display state.
> On actor death the death handler applies the same protected-range clear to this mirror as it does to
> the actor's buff-slot table (see `structs/actor.md` death-state section).

### 3.8 SoundManager — ~640 bytes (CODE-CONFIRMED, size PLAUSIBLE)

Cross-reference: `specs/client_runtime.md §1` for full runtime behaviour.

| Offset | Size | Type | Field | Notes |
|-------:|-----:|------|-------|-------|
| +0x00 | — | — | (no vtable) | No vtable pointer in the constructor; SoundManager is a DirectSound wrapper struct, not a Diamond scene-graph node. |
| +0x44 | ~12 | obj | `sound_list_2d` | Internal 2D-sound list (initialised by a dedicated list-init routine). |
| +0x50 | ~12 | obj | `sound_list_3d` | Internal 3D-sound list. |
| +0x5C | ~12 | obj | `sound_list_streaming` | Internal streaming-sound list. |

> **Size note.** The ~640-byte estimate is derived from the distance to the next named item in
> the data segment. The init guard byte is located at approximately offset +0xA0 from the object
> start, which is earlier than the estimated object end. The true object span may be smaller;
> see open question §7.8.

### 3.9 GHTexManager / EffectManager — 620 bytes (CODE-CONFIRMED)

Cross-reference: `formats/effects.md`, `specs/client_runtime.md §3`.

> **IDA naming note.** The static is labelled `g_GHTexManager` in the analysis database, but
> its vtable and constructor logic identify it as the primary `EffectManager` instance. The two
> names refer to the same runtime object.

| Offset | Size | Type | Field | Notes |
|-------:|-----:|------|-------|-------|
| +0x00 | 4 | ptr | vtable | `EffectManager` vtable. |
| +0x04 | 4 | uint32 | `effect_id_A` | Hardcoded effect identifier A = 380 002 001. CODE-CONFIRMED. |
| +0x08 | 4 | uint32 | `effect_id_B` | Hardcoded effect identifier B = 380 002 002. |
| +0x0C | 4 | uint32 | `effect_id_C` | Hardcoded effect identifier C = 380 002 003. |
| +0x10 | 4 | uint32 | `effect_id_D` | Hardcoded effect identifier D = 380 001 001. |
| +0x14 | 540 | uint32[135] | `effect_id_table` | Effect-id table; zero-initialised, 135 dwords. |
| +0xC0 | 4 | uint32 | `sky_effect_A` | Sky effect id A = 390 000 002. |
| +0xCC | 4 | uint32 | `sky_effect_B` | Sky effect id B = 390 000 001. |
| +0xE4 | 4 | uint32 | `sky_effect_C` | Sky effect id C = 390 000 003. |
| +0x234 | 4 | uint32 | (pre-list dword) | Zero-initialised by the constructor immediately before the three sub-lists. CODE-CONFIRMED (re-verification 2026-06-16). |
| +0x238 | 4 | uint32 | (pre-list dword) | Zero-initialised before the sub-lists. CODE-CONFIRMED. |
| +0x23C | 4 | uint32 | (pre-list dword) | Zero-initialised before the sub-lists. CODE-CONFIRMED. |
| +0x240 | ~12 | obj | `sub_list_A` | Effect sub-list A. |
| +0x24C | ~12 | obj | `sub_list_B` | Effect sub-list B. |
| +0x258 | ~12 | obj | `sub_list_C` | Effect sub-list C. |

### 3.10 MainWindow (Diamond::GUWindow, "MainMaster") — 1 464 bytes (CODE-CONFIRMED)

Cross-reference: `specs/ui_system.md`, `specs/client_runtime.md §2`.

The `MainWindow` inherits from `Diamond::GUWindow` (which extends `Window → Panel → Component`).
Its base region reproduces the base-class field layout documented in `specs/ui_system.md`. The
constructor initialises the base via a name string `"MainMaster"`; RTTI confirms the concrete
class `MainWindow`. This object is the 1 464-byte service-slot owner and is **distinct from** the
16-byte `MainHandler` hub (§3.10a).

| Offset | Size | Type | Field | Notes |
|-------:|-----:|------|-------|-------|
| +0x00 | 4 | ptr | vtable (primary) | `MainWindow::vftable` (installed through the Component base-class chain). CODE-CONFIRMED. |
| +0x04 | ~184 | bytes | (base-class fields) | Inherited `GUWindow → Panel → Component` fields up to the secondary vptr. See `specs/ui_system.md`. |
| +0xBC | 4 | ptr | vtable (secondary) | Secondary / base-subobject vtable pointer (multiple-inheritance base vptr). CODE-CONFIRMED (re-verification 2026-06-16) — **was missing from the prior table, which listed only the +0x00 vptr.** |
| +0xC0 | ~376 | bytes | (base-class fields) | Remainder of the inherited base region up to the service-slot block. See `specs/ui_system.md`. |
| +0x238 | 892 | ptr[223] | `service_slots` | 223 pointer-width (4-byte) service-slot words; a flat array of HUD-panel pointers, read by index everywhere. Zero-initialised at construction **except the `+0x500` state-5-handler slot, which the ctor deliberately skips** (filled later — see note). Subsystem initialisers populate these during the in-game scene state. Only a subset are named — see note below. CODE-CONFIRMED count & bounds. |
| +0x500 | 4 | ptr | `state5_handler` | Pointer to the in-game **state-5 command handler** — a distinct **~0xC8-byte (≈200-byte)** command-handler-derived object, **not** the 16-byte `MainHandler` hub of §3.10a. The constructor's zero-init loop **deliberately skips this slot**; the later write happens at the scene-state-machine entry to the in-game state. **Both the ctor-skip and the later write are now CODE-CONFIRMED** (re-confront 2026-06-17): the write-site is traced and the occupant's identity is the ~0xC8-byte state-5 handler (the earlier `= MainHandler*` attribution conflated it with the hub and is corrected). |
| +0x5B0 | 4 | — | (last service-slot dword) | Last dword written by the zero-init loop. |
| +0x5B4 | 1 | uint8 | (tail byte) | Final byte written by the constructor (boundary of the service region). |
| +0x5B8 | — | — | (object end) | Object boundary at 1 464 bytes (init guard sits here). |

> **Service-slot decode status.** The block is a flat array of HUD-panel pointers, populated at the
> in-game scene state and read by index throughout the HUD code (e.g. status/character, inventory,
> skill, buddy/relation, dock-slide, quest-tracker, guild, and party panels each occupy a distinct
> index). Only a small subset of the 223 slots are identified, and the individual slot identities
> are (static-hypothesis) — inferred from the callee each pointer is passed to. The full decode
> requires tracing all write sites that populate each slot offset after the in-game-state
> initialisation. See open question §7.6. One slot is now firmly pinned: **+0x500 holds the
> ~0xC8-byte state-5 command handler** (the write site is traced — CODE-CONFIRMED).

### 3.10a MainHandler hub — 16 bytes (CODE-CONFIRMED)

Cross-reference: `specs/client_runtime.md §2`, `specs/game_loop.md`.

A small handler-hub singleton, **separate from** the 1 464-byte `MainWindow` (§3.10): it has its
own static storage and its own accessor (the single busiest accessor in the binary). Its
constructor zeroes four dwords (the full 16-byte object). It is **also distinct from** the
~0xC8-byte (≈200-byte) state-5 command handler stored into `MainWindow +0x500` (§3.10) — the name
"MainHandler" was overloaded across the two. This 16-byte hub is **not** the object written into
the +0x500 service slot. The earlier conflation of this hub with both the service-slot window and
the +0x500 occupant is corrected here and in §6.

| Offset | Size | Type | Field | Notes |
|-------:|-----:|------|-------|-------|
| +0x00 | 4 | uint32 | (field) | Zero-initialised. CODE-CONFIRMED. |
| +0x04 | 4 | uint32 | (field) | Zero-initialised. |
| +0x08 | 4 | uint32 | (field) | Zero-initialised. |
| +0x0C | 4 | uint32 | (field) | Zero-initialised (object end / init guard follows). |

### 3.11 Engine — 48 bytes (CODE-CONFIRMED)

Cross-reference: `specs/game_loop.md` for the `Engine_MainLoop` / `Engine_FrameStep` behaviour.

| Offset | Size | Type | Field | Notes |
|-------:|-----:|------|-------|-------|
| +0x00 | 4 | ptr | vtable (or null) | The constructor may not install a vtable; `Engine` may be a plain struct. See open question §7.5. |
| +0x04 | 4 | ptr | `renderer` | Pointer to the `GHRenderer` static (the ~177 860-byte Renderer object). Set to the Renderer's address during construction. |
| +0x08 | 4 | ptr | `input_manager` | Pointer obtained from `InputManager::GetSingleton()`. |
| +0x10 | ~16 | obj | `sub_object` | Small embedded sub-object constructed by its own internal constructor. Role unverified. |
| +0x1C | 4 | uint32 | `flag_1C` | Initialised to 0. |
| +0x20 | 1 | uint8 | `active` | Initialised to 1. |
| +0x21 | 11 | — | (pad) | Padding to 48-byte object end. |
| +0x2C | 4 | uint32 | `flag_2C` | Initialised to 0. |

### 3.12 FrameTickScheduler — 72 068 bytes (CODE-CONFIRMED)

Cross-reference: `specs/game_loop.md §3` for subscriber-dispatch behaviour.

| Offset | Size | Type | Field | Notes |
|-------:|-----:|------|-------|-------|
| +0x0000 | 48 004 | uint8[] | `pre_init_region` | Reserved / uninitialised region at startup. |
| +0xBBA4 | 24 000 | uint8[] | `subscriber_slot_table` | Zero-initialised subscriber slot table; holds per-subscriber state for the tick dispatcher. |
| +0x118F4 | 4 | uint32 | `subscriber_count` | Count of registered subscribers. Initialised to 0. |
| +0x118F8 | 4 | uint32 | `dispatch_cursor` | Current dispatch position within the subscriber table. Initialised to 0. |
| +0x118FC | 4 | uint32 | `interval_scale` | Interval scaling factor. Initialised to 1. |
| +0x11900 | 1 | uint8 | `paused` | Pause flag; 0 = running. |
| +0x11904 | 4 | uint32 | `created_ms` | Millisecond timestamp captured at construction via the global time function. |
| +0x11908 | 24 | uint32[6] | `control_fields` | Six additional control dwords (offsets +0x11908 .. +0x1191C); init values 0/0/0/0/1/-1 respectively. |
| +0x11920 | 4 | int32 | `field_72048` | Initialised to -1. |
| +0x11934 | 16 | uint32[4] | `tail_fields` | Four tail dwords (offsets +0x11934 .. +0x1193C); zero-initialised. |
| +0x11940 | 1 | uint8 | `enabled` | Enabled flag; initialised to 1. |

### 3.13 AppService — 136 bytes (CODE-CONFIRMED) — added 2026-06-16

A core client-runtime app-service Meyers singleton recovered in the 2026-06-16 re-verification —
**not present in the prior master table.** It follows the standard Meyers idiom (init-guard + in-place
constructor + `atexit` destructor) with its own static storage and accessor, and is moderately
cross-referenced across the client runtime (on the order of ~60 reference sites). The object size
(136 bytes) is CODE-CONFIRMED by re-deriving the accessor static and init-guard span.

| Offset | Size | Type | Field | Notes |
|-------:|-----:|------|-------|-------|
| +0x00 | 136 | bytes | (service fields) | Internal layout not yet walked; the role is a general client-runtime service hub. Field-level layout is (static-hypothesis). Total span 136 bytes is CODE-CONFIRMED. |

---

## 4. Renderer object — key fields (~177 860 bytes, static-hypothesis — NOT re-walked this pass)

> **Re-verification note (2026-06-16).** The Renderer object and the render-subsystem 16-slot
> pointer-cache block (§4.1) were **not re-walked in this pass** (lane time budget). Everything in
> §4 and §4.1 is carried forward from the prior dirty note as **(static-hypothesis)** pending a
> follow-up walk of the scene-state-5 initialiser. Treat the CODE-CONFIRMED tags inside §4/§4.1 as
> not yet re-confirmed against the 263bd994 anchor.

The `Diamond::GHRenderer` static is the largest object in the binary (~174 KB). It embeds the
D3D9 device, display configuration, all render-target surfaces, toon-shader constants, and the
D3D present parameters. It is a raw static (not a Meyers singleton with an init guard) and its
address is cached into the render-subsystem pointer-cache block at scene-state 5 (§4.1).
(static-hypothesis — not re-walked this pass.)

Cross-reference: `specs/client_runtime.md §3` for the render-pipeline behaviour.

| Offset | Size | Type | Field | Notes |
|-------:|-----:|------|-------|-------|
| +0x00 | 4 | ptr | vtable | `Diamond::GHRenderer` vtable. |
| +0x2B6C4 | 4 | uint32 | `display_width` | Viewport width in pixels. CODE-CONFIRMED. |
| +0x2B6C8 | 4 | uint32 | `display_height` | Viewport height in pixels. |
| +0x2B6CC | 4 | uint32 | `display_bpp` | Bits per pixel. |
| +0x2B6D0 | 1 | uint8 | `windowed` | 1 = windowed mode, 0 = fullscreen. |
| +0x2B734 | 4 | ptr | `d3d9_interface` | `IDirect3D9*`; used for device reset. |
| +0x2B738 | 4 | ptr | `d3d9_device` | `IDirect3DDevice9*`; the primary D3D device. This is the most-referenced pointer in the binary: every per-frame D3D draw call routes through it. CODE-CONFIRMED. |
| +0x2B974 | 64 | float[16] | `view_matrix` | Saved 4×4 VIEW matrix (column-major, IEEE-754 float). |
| +0x2B9C0 | 1 | uint8 | `view_dirty` | Flag set when the view matrix has changed and needs to be re-uploaded to D3D. |

> **Note on object size.** The size figure (~177 860 bytes = 0x2B6C4) is the span to the display
> width field; the true object end may be slightly larger. An engineer implementing a C# mirror
> does not reproduce this object; the D3D device and all render state live exclusively in layer 05
> (Godot / presentation). This table exists so the `Assets.Parsers` or `Client.Infrastructure`
> engineer knows what data the renderer owns and never looks for display parameters elsewhere.

---

## 4.1 Render-subsystem pointer-cache block (scene-state 5)

> **(static-hypothesis — not re-walked 2026-06-16.)** This entire block, including the slot
> assignments below, is carried forward from the prior dirty note and was not re-confirmed against
> the 263bd994 anchor this pass. Flagged for a follow-up walk of the scene-state-5 initialiser.

When scene-state 5 ("BuildGameWorld") is entered, a single initialiser function caches 16
subsystem pointers into a contiguous block of 4-byte pointer slots in the global data region.
The block spans 16 consecutive slots (64 bytes total). All slot contents are filled once and
remain stable for the rest of the session.

| Slot # | Pointed-to singleton | Size (B) | Role |
|-------:|---|---:|---|
| 0 | `GHRenderer` | ~177 860 | D3D9 Renderer; most-referenced global in the binary |
| 1 | `FrustumViewManager` | 704 | Frustum / view-culling manager |
| 2 | `GFrustum` | 480 | Diamond::GFrustum singleton |
| 3 | `ActorManager` | 300 | Same as §3.7 |
| 4 | `GHTexManager` | 620 | Same as §3.9 |
| 5 | `WeatherManager` | 13 244 | Rain/weather particle system (references `rains.dds`, `rain_drop.dds`) |
| 6 | `SnowManager` | 7 236 | Snow particle system (references `snow.dds`) |
| 7 | `MapXEffectManager` | 36 | Map-level VFX placement manager |
| 8 | `JointXEffectPool` | 28 | Per-bone VFX attachment pool |
| 9 | `UserXEffectPool` | 28 | User-triggered VFX pool |
| 10 | `ActorEffectBinder` | 52 | Binds AnimCatalog + ActorManager for effect routing |
| 11 | `EnvironmentLightScene` | 6 848 | Scene lighting: 5 positional lights + group node (1 288 + 5 × 184 + overhead) |
| 12 | `TerrainSkyDome` | ~2 596 | Sky dome geometry for the terrain layer |
| 13 | `ParticleEffectManager` | 24 | Particle system manager (`ParticleEffectManager` vtable) |
| 14 | `SoundManager` | ~640 | Same as §3.8 |
| 15 | `ActorSortBuckets` | 2 596 | Actor draw-order sort/batch manager |

> **GFrustum vs FrustumViewManager (open question §7.2).** Two related singletons occupy
> adjacent slots (1 and 2). The 480-byte object carries the `Diamond::GFrustum` vtable; the
> 704-byte sibling is a larger container whose exact role (camera-container or culling manager)
> is unconfirmed.

> **EnvironmentLightScene (open question §7.3).** Identified by its 5-light structure
> (`Diamond::GPositionalLight` constructor called 5 times). The `specs/environment.md` spec covers
> the on-disk `.bin` environment asset family; this runtime object is the *in-memory* lighting
> scene that that spec ultimately feeds.

---

## 5. VFS subsystem — flat globals (CODE-CONFIRMED)

The VFS uses **flat global variables** rather than a Meyers singleton object. There is no C++
constructor guard and no `GetSingleton()` function. The `Diamond::CVFSManager` wrapper class
provides named access functions (`VFS_IsMounted`, `VFS_SetMounted`) but the underlying state lives
in globals in the data segment.

> **Re-verification correction (2026-06-16).** The earlier description ("three flat globals") is
> **incomplete**. The three TOC globals below are correct, but the runtime VFS state is **more than
> three globals**: there is also a **distinct mount-flag byte** (this byte — not any of the three
> below — is what `VFS_IsMounted` actually returns; it selects packed-VFS vs loose-file access)
> plus a **three-word load-progress sub-block** (an enable flag byte, a 64-bit cumulative-bytes
> accumulator, and a load-progress fraction). The mount flag and progress sub-block are CODE-CONFIRMED.

Cross-reference: `formats/pak.md` for the on-disk archive format.

| Group | Size | Type | Field | Notes |
|---|---:|------|-------|-------|
| TOC | 4 | ptr | `toc_array` | Pointer to the table-of-contents (TOC) array. Each TOC entry is 144 bytes, sorted by lowercase file path. CODE-CONFIRMED stride (the find routine indexes `toc_array + 144 × idx`, binary-search on a lowercased path compare). |
| TOC | 4 | uint32 | `toc_entry_count` | Number of entries in the TOC array. CODE-CONFIRMED. |
| TOC | 4 | HANDLE | `vfs_file_handle` | Win32 file handle for the `.vfs` data file; kept open for the entire session. CODE-CONFIRMED. |
| Mount | 1 | uint8 | `mount_flag` | The actual `VFS_IsMounted` source byte; selects packed-VFS vs loose-file access. **Distinct global, not part of the TOC trio.** CODE-CONFIRMED (re-verification 2026-06-16). |
| Progress | 1 | uint8 | `progress_enable` | Load-progress tracking enable flag. CODE-CONFIRMED. |
| Progress | 8 | uint64 | `progress_accum` | Cumulative bytes loaded, for the load-progress bar. CODE-CONFIRMED. |
| Progress | 4 | int32/float | `progress_fraction` | Load-progress fraction (`progress_accum / total`). CODE-CONFIRMED. |

Initialisation: `VFS_OpenArchive` is called once from WinMain scene-state 0, before the
scene-state machine loop begins. It opens `data.inf` (the TOC index file), reads its **24-byte
header** and takes the **entry count from the 4th header dword (header offset +0x0C)** — NOT
+0x08 — allocates `144 × count` bytes for the `toc_array`, reads the TOC, closes `data.inf`, then
opens `data/data.vfs` (the data archive) and retains the open handle in `vfs_file_handle`. The TOC
globals are then read-only for the rest of the session.

> **Note on the `data.inf` header / TOC entry format (byte-layout authority = `formats/pak.md`).**
> The 24-byte header, the entry-count field at header offset +0x0C (the 4th dword), and the
> 144-byte TOC entry stride are all visible in `VFS_OpenArchive` and the find routine. `data.inf`
> is opened with `FILE_FLAG_RANDOM_ACCESS` (not sequential scan). These are interoperability facts;
> the canonical byte-level layout is owned by `formats/pak.md` — cite `// spec: Docs/RE/formats/pak.md`
> in code (byte-witness over a real sample pending in the VFS-witness block).

---

## 6. Singleton construction order

The sequence below reflects the first-use order as driven by the main entry point and the
scene-state machine. Items marked with a scene-state number are constructed when that state is
entered.

1. `LuaConfig` — first singleton constructed; reads `game.lua` for `vfsmode`, `launcher`,
   and `debugmode`.
2. `GameState` — `debug_mode` flag set from the `LuaConfig` result.
3. `BugTrap` — crash-reporter init.
4. **VFS globals** (not a singleton ctor) — `VFS_OpenArchive` mounts `data.inf` +
   `data/data.vfs`; the TOC globals, the mount-flag byte, and the load-progress sub-block are
   filled (see §5). This happens before the scene-state loop (driven from scene-state 0).
5. *(Scene-state loop begins — `while(1) switch(scene_state)`, 8 cases for states 0..7. All
   following steps are per-state. Note: case 0 advances `scene_state` to 1 and triggers the
   network-stack construction below.)*
6. (State 1) `MainWindow` ("MainMaster") and the `MainHandler` hub — **two distinct singletons**.
   Each has its own accessor and static storage: the `MainWindow` accessor constructs the 1 464-byte
   service-slot window (the 223 slots are zero-initialised, except the +0x500 state-5-handler slot
   which the ctor deliberately skips — see §3.10), while the separate `MainHandler` accessor
   constructs the 16-byte hub (§3.10a). The earlier wording that the *MainHandler accessor* builds
   the 1 464-byte window was a conflation and is corrected: it builds only the small hub. **The
   +0x500 slot is filled later** — at the scene-state-machine entry to the in-game state, the engine
   constructs the ~0xC8-byte (≈200-byte) command-handler-derived **state-5 handler** and writes its
   pointer into `MainWindow +0x500`. This write site is now **CODE-CONFIRMED** (re-confront 2026-06-17),
   and its occupant is the ~0xC8-byte state-5 handler — a **third** object, distinct from both the
   `MainWindow` window and the 16-byte hub.
7. (State 1) Font creation; window configured. **The Renderer static is set up during window
   configuration** (not via a Meyers init guard). (static-hypothesis — render path not re-walked
   this pass.)
8. (State 1) `NetClient` + `NetHandler` — network stack initialised (case 0 advances to state 1
   and calls the `NetHandler` accessor). The `NetHandler` constructor stores the `ActorManager`
   accessor result into `NetHandler +0x30` (see §3.4 / §3.7 note); this is the first point at
   which the `ActorManager` singleton is created.
9. (State 1) `InputManager`.
10. (State 1) `BillingState`.
    *(Also within state 1: `AnimCatalog`, `CoreMotManager`, `CorePoseManager`,
    `CoreSkinManager`, `SoundManager`, `GHTexManager`, `ShadowManager`, `RankProgress`,
    and `AppService` (§3.13) are each lazy-constructed on their respective first-use calls during
    scene-state 1 or 2. Exact first-use ordering within this group is unconfirmed.)*
11. (State 5, scene-build entry) The render-subsystem initialiser caches 16 pointer-slots
    (§4.1) — **(static-hypothesis, not re-walked this pass)**:
    - `GHRenderer` pointer cached.
    - `FrustumViewManager`, `GFrustum`, `ActorManager`, `GHTexManager`,
      `WeatherManager`, `SnowManager`, `MapXEffectManager`, `JointXEffectPool`,
      `UserXEffectPool`, `ActorEffectBinder`, `EnvironmentLightScene`,
      `TerrainSkyDome`, `ParticleEffectManager`, `SoundManager`, `ActorSortBuckets`
      — all pointer-cached into the 16-slot block.
12. (State 5) Scene-graph construction:
    - `GPerspectiveCamera` (FOV 65°, near clip 5, far clip 15 000).
    - 5 `GViewPlatform` objects and 5 camera manipulators (Third, First, Static,
      Gamble, Event).
    - `GScene` node (`"charater scene"` — note: literal string has one character typo).
    - `GSwitch` node.
13. `FrameTickScheduler` — constructed on its first use from the threading subsystem;
    exact state is unconfirmed but is before the first frame tick.
14. `Engine` — constructed after `InputManager` and `Renderer` are both available.

---

## 7. Binary module map

### 7.1 Engine brand and toolchain

| Attribute | Value | Evidence |
|---|---|---|
| Internal engine name | **Diamond** | An RTTI type descriptor naming a `CVFSManager` class inside a `Diamond` C++ namespace; corroborated by an embedded build path referencing a `diamond` source directory (project slug `do_korea_service_dx9`) in the binary's string pool. CODE-CONFIRMED. |
| Project slug | `do_korea_service_dx9` | Source path string above. |
| Compiler | **MSVC 2005** static CRT | 707 named CRT functions matching the MSVC 2005 static runtime pattern (`_malloc`, `_sprintf`, fp-math, exception handling). CODE-CONFIRMED. |
| Scripting VM | **Lua 5.1.2** statically linked | 33 named `lua_*` / `luaL_*` functions; Lua 5.1.2 internal error strings present in the string pool. CODE-CONFIRMED. |
| Lua binding helper | **LuaTinker** | `LuaTinker_Call0Int`, `LuaTinker_Call1Int_String` named functions; `lua_cpp_load` is the only native call surface exposed to Lua scripts. CODE-CONFIRMED. |
| Compression | **LZ4** statically linked | `LZ4_compress_default`, `LZ4_decompress_safe`, `LZ4_compressBound`, `LZ4_compress_safe` named functions. CODE-CONFIRMED. |
| Korean text input | **IMM32 Korean IME block** | 19 named `IME_*` functions; full IMM32 import set present. Used by `Diamond_GUTextbox` for Hangul composition. CODE-CONFIRMED. |
| Anti-cheat | **XTrap** (self-contained cluster) + **BugTrap.dll** | `fcEXP` exported entry point for the XTrap module; `XTrapSocket_*` cluster; `BT_InstallSehFilter` import. CODE-CONFIRMED. |
| Anti-cheat socket path | XTrap uses its own TCP socket separate from the game connection | `XTrapSocket_OpenTcpAndSetPeer`, `XTrapSocket_SendExact` identified; IAT scan for Winsock hooks by `AntiCheat_DetectWinsockHooks_Init`. |
| Audio | **DirectSound** (DSound 8) + **OGG Vorbis** statically linked | `DirectSoundCreate` import; `Diamond_GSoundOGG_ctor` and Vorbis codec symbols (no DLL import). |
| Input | **DirectInput 8** | `DirectInput8Create` from `DINPUT8.DLL`. |
| Render API | **Direct3D 9** | `Direct3DCreate9` from `d3d9.dll`; `D3DXCreate*` from `d3dx9_42.dll`. |
| Screenshot | **Intel JPEG Library** (`ijl11.dll`) | `ijlInit`, `ijlWrite`, `ijlFree` imports. |

### 7.2 Function counts per subsystem

Derived by sweeping all named functions (approximately 2 904 of ~25 973 total) through a prefix
classifier. The auto-named mass (~23 069 functions named `sub_*` etc.) is excluded.

| Subsystem | Named fns | Role summary |
|---|---:|---|
| CRT | 707 | MSVC 2005 static runtime: malloc/free, string, fp math, exception handling |
| Render | 209 | Diamond D3D9 scene-graph nodes, pipelines, cull/draw, toon/bloom |
| NetHandler | 188 | Per-opcode S2C / C2S dispatch handlers |
| Network | 90 | NetClient, NetConn, PacketBuf, AuthSession, LZ4/cipher wrappers |
| Terrain | 100 | Cell streaming, BUD/TED/MUD/SOD/Fx1–7 parsers, sky/env |
| Actor | 88 | ActorManager, skinning, animation mixer, `.bnd`/`.skn`/`.mot` |
| ScriptData | 78 | `.scr`/`.do`/`.xdb` table loaders, Option INI, BillingState |
| UI | 63 | Diamond GU widget subclasses (ChatPanel, MapPanel, TradePanel, …) |
| Math | 57 | Vec3, Quat, Mat4, Diamond geometry primitives |
| SceneLifecycle | 56 | MainHandler, LoginWindow, SelectWindow, Engine main loop |
| Effects | 35 | XEffect, JointXEffect, MapXEffect, ParticleEffectManager |
| Lua | 33 | LuaConfig, LuaTinker, `lua_*` / `luaL_*` C API (Lua 5.1.2 VM) |
| Sound | 20 | SoundManager, SoundTable, DSBuffer, GSoundThread |
| AntiCheat | 19 | XTrap socket, BugTrap logger, TopLevelExceptionFilter, IAT hook scanner |
| IME | 19 | Korean IME (IMM32) composition/candidate management |
| VFS | 16 | CVFSManager, VFS_FindEntry, VFS_ReadEntryData |
| Threading | 13 | Event scheduler, TickSubscriber, ThreadSlot wrappers |
| Crypto | 7 | LZ4 compress/decompress, rolling-XOR cipher, obfuscated-string decoder |
| Camera | 7 | Five camera manipulators (Event, First, Gamble, Select, Static, Third) |
| Input | 3 | InputManager, DirectInput polling thread |
| Unknown/STL | ~1 096 | C++ mangled names (STL internals, operator new/delete, …) |

### 7.3 Address-space ordering of subsystem clusters

The following describes the relative ordering of major subsystem clusters within the code
section, from lowest to highest address, **using only ordinal / band descriptions — no absolute
addresses**.

```
lowest addresses
  ├── Math / Camera cluster
  │     Vec3, Quat, Mat4, AnimTrack sampler, GTransform,
  │     five camera manipulators and shared base code
  │
  ├── Actor / Skinning / Animation
  │     ActorManager, CPU skinning hot path (three blend modes),
  │     BindPose parser, CoreSkin/Mot/Pose managers, DiskFile reader
  │
  ├── Terrain + World
  │     Cell streaming, BUD/TED/SOD/MUD parsers, Fx1–Fx7 terrain layers,
  │     SkyBox / CloudDome / StarDome, Sun/LightManager, ShadowManager
  │
  ├── Sound
  │     SoundManager, SoundTable (.wlk/.run/.bgm/.bge/.eff), DSBuffer,
  │     amplitude-curve helper
  │
  ├── ScriptData / Config-table loaders
  │     .scr / .do / .xdb loaders (quests, skills, mobs, NPCs, items, …)
  │
  ├── Effects + UI panel beginnings
  │     XEffect, JointXEffect, MapXEffect, ParticleEffectManager,
  │     early actor-state and chat panels
  │
  ├── SceneLifecycle + Network + NetHandler
  │     MainHandler, LoadHandler, OpeningWindow, SelectWindow,
  │     NetClient, NetConn, PacketBuf,
  │     188-handler NetHandler block (the dense opcode-dispatch cluster)
  │
  ├── Engine / Input / AntiCheat entry cluster
  │     WinMain, WndProc, InputManager, BugTrap init, IAT hook scanner
  │
  ├── Render engine
  │     Scene-graph nodes, pipelines, cull+draw, render-states,
  │     IME block (Korean IME, contiguous sub-cluster),
  │     D3D device/present/toon/bloom,
  │     VFS (CVFSManager, DiskFile, archive reader),
  │     Threading scheduler (FrameTickScheduler, TickSubscriber)
  │
  ├── UI widget constructors
  │     GUPanel, GUWindow, GUButton, GULabel, GUTextbox, GUList,
  │     GUScroll, GUCanvas3D, GUCheckBox, …
  │     LuaConfig / LuaTinker / Lua 5.1.2 VM (lua_open … lua_pcall …)
  │
  ├── CRT cluster
  │     MSVC 2005 static runtime: malloc/free, string, fp math,
  │     locale, stdio, exception handling
  │
  └── XTrap anti-cheat cluster  ← highest addresses, near .text section end
        fcEXP exported entry point, XTrapSocket_*, XTrap report sender
```

Mnemonic ordering: **math/camera → actor → terrain → sound → script-data → effects/UI → scene/network → render/VFS → UI-widgets/Lua → CRT → anti-cheat**

### 7.4 High-reference anchors (context for integration engineers)

The two most cross-referenced functions in the binary are:

- **Heap-allocate wrapper** (the highest-xref function, ~3 250 callers): a very short function
  (approximately 10 bytes) that wraps the global heap allocator. Its size and xref count are
  consistent with `operator new(size)` or a pool-allocate shim. Every subsystem calls it.
- **Actor component accessor / dispatcher** (~2 094 callers): a function in the Actor cluster
  that the entire binary uses to obtain per-actor sub-component pointers. It is the universal
  "get subsystem by tag" accessor for Actor objects.

These are noted because integration engineers tracing call chains will encounter both functions
at nearly every level of the call graph; they are infrastructure, not domain logic.

---

## 8. Cross-references to existing specs

| Topic | Authoritative spec |
|---|---|
| `GameState` scene_state lifecycle (states 0..7; 8 cases) | `specs/client_runtime.md §7` |
| Net protocol, opcode catalogue | `specs/handlers.md`, `opcodes.md`, `packets/*.yaml` |
| NetClient cipher / LZ4 | `specs/crypto.md` |
| `BillingState` subscription logic | `names.yaml` (client_mechanics.BillingState) |
| Animation pipeline, `.bnd`/`.skn`/`.mot` | `specs/skinning.md`, `formats/animation.md` |
| Actor struct layout, SpawnDescriptor | `structs/actor.md`, `structs/spawn_descriptor.md` |
| SoundManager runtime behaviour | `specs/client_runtime.md §1` |
| GHTexManager / Effects runtime | `formats/effects.md`, `specs/client_runtime.md §3` |
| Renderer pipeline (toon/bloom) | `specs/client_runtime.md §3` |
| VFS on-disk archive format | `formats/pak.md` |
| FrameTickScheduler dispatch | `specs/game_loop.md` |
| MainWindow / GU widget tree | `specs/ui_system.md`, `specs/client_runtime.md §2` |
| InputManager | `specs/input_ui.md` |
| Lua scripting | `specs/lua_scripting.md` |
| Camera modes | `specs/camera_movement.md`, `specs/client_runtime.md §4` |
| Environment lighting / sky `.bin` format | `specs/environment.md` |

### Deltas vs existing specs (new information in this document)

The following information is **new** with respect to the currently committed spec set:

- **(2026-06-20 CYCLE 7, new)** The **Diamond base-object layout** (§3.0): the shared
  vtable(+0x00) / `ref_count`(+0x04) / `name`(+0x08) head, the `GNode` parent vector (+0x3C) and
  `GGroup` child vector (+0x4C), the intrusive-refcount mechanism, the vector-deleting-destructor at
  vtable slot 0, owner-frees-children-by-refcount, and the CRT-only allocator model — supersedes any
  prior "object lifecycle: NONE" statement.
- **(2026-06-20 CYCLE 7, resolved)** Display FRAMERATE config inertness (§16) is now CODE-CONFIRMED
  (statically exhaustive: the config field has two references, both stores) — the 60 FPS cap is
  hardcoded / config-inert, no longer capture/debugger-pending.
- Exact object sizes for the singletons (NetClient 82 368, FrameTickScheduler 72 068,
  NetHandler 6 220, etc.) — not previously documented in any committed spec.
- **(2026-06-16 re-verification, new)** `MainWindow` ("MainMaster") and `MainHandler` are **two
  distinct singletons**, not one — the 1 464-byte service-slot window vs a 16-byte hub (§3.10 /
  §3.10a / §6 item 6).
- **(new)** `AppService` — a 136-byte Meyers singleton (§3.13) not previously catalogued.
- **(new)** The VFS state is **more than three flat globals**: a distinct mount-flag byte (the real
  `VFS_IsMounted` source) plus a three-word load-progress sub-block (§5).
- **(new)** The `MainWindow` **secondary/base-subobject vtable pointer at +0xBC** (§3.10).
- **(new)** The `MainWindow` ctor **deliberately skips the main-handler service slot** in its
  zero-init loop (filled later at the in-game scene state) (§3.10 / §6).
- **(new)** The `EffectManager` ctor **zeroes three pre-list dwords** (+0x234/+0x238/+0x23C)
  before the sub-lists (§3.9).
- **(corrected)** `GameState` is a **states-0..7 / 8-case** machine; 8 is a sub-state value, not a
  9th top-level case (§3.1).
- The render-subsystem 16-slot pointer-cache block (§4.1) and its complete slot assignment
  table — not previously documented.
- The `Engine` object (§3.11) — `specs/game_loop.md` references `Engine_MainLoop` by name but
  does not document the singleton object or its field layout.
- The `FrameTickScheduler` internal split (48 004-byte pre-init region + 24 000-byte subscriber
  slot table) — `specs/game_loop.md` notes the scheduler exists but does not give the internal
  layout.
- The full render-subsystem initialiser dependency and construction order at scene-state 5 (§6
  items 11–12) — not previously committed anywhere.
- `ActorManager +0x74` spatial-index sub-object (~192 bytes) — mentioned in struct recovery
  notes but not in any committed spec.
- `MainWindow +0x500` holds the ~0xC8-byte (≈200-byte) state-5 command handler (a third object,
  distinct from the 16-byte `MainHandler` hub); the write at the in-game-state entry is now
  CODE-CONFIRMED (2026-06-17). The earlier `= MainHandler*` reading conflated it with the hub and is
  corrected (§3.10 / §3.10a / §6).
- `CoreMotManager`, `CorePoseManager`, `CoreSkinManager` as distinct 16-byte singletons — not
  documented in `specs/skinning.md`.
- Binary module map with subsystem function counts, engine brand, toolchain, third-party library
  inventory, and address-space ordering — not previously captured in any committed spec.
- `BillingState` full field map (§3.5) — `names.yaml` has only a one-line note.
- `GHTexManager` / EffectManager dual-name note and hardcoded effect identifiers (§3.9).

---

## 9. Open questions

1. **WeatherManager / SnowManager canonical class names.** Both are identified by their
   constructor string references (rain/snow texture filenames). Neither has a confirmed class name
   from RTTI or vtable evidence. Candidate names: `Precipitation_RainEffect` / `SnowEffect`? Needs
   RTTI scan.

2. **GFrustum vs FrustumViewManager.** Two singletons in adjacent pointer-cache slots (§4.1 slots
   1 and 2): one carries the `Diamond::GFrustum` vtable (480 bytes) and the other is a 704-byte
   sibling. Is the larger one a camera-container or the main frustum-cull manager? Needs vtable
   name confirmation.

3. **EnvironmentLightScene and `specs/environment.md`.** The runtime object (§4.1 slot 11) and
   the on-disk `.bin` environment asset family are related but documented separately. `specs/
   environment.md` should be updated to cross-reference this runtime object once its exact role in
   the scene-graph lighting pipeline is confirmed.

4. **ActorSortBuckets type** (§4.1 slot 15, 2 596 bytes). No named function identified in the
   analysis for this object. Size and position suggest an actor draw-order bucket or sort-by-depth
   system. Needs further call-graph tracing.

5. **Engine object vtable.** The `Engine` constructor may not install a vtable pointer (the first
   instruction does not contain the typical vtable-install move). Either `Engine` is a plain struct,
   or the vtable is installed by a base-class constructor not yet traced. Requires a one-level
   callgraph from the `Engine` constructor.

6. **MainWindow service-slot full decode.** Only a small subset of the 223 pointer slots at
   `+0x238..+0x5B4` are identified, and the individual slot identities are (static-hypothesis) —
   inferred from the callee each pointer is passed to (status/character, inventory, skill,
   buddy/relation, dock-slide, quest-tracker, guild, party panels, etc.). A complete map requires
   tracing all write sites that populate each dword after the in-game-state initialisation. This is
   a significant analyst task.

   - **6a. `MainWindow +0x500` state-5-handler write site: RESOLVED (CODE-CONFIRMED, 2026-06-17).**
     The ctor *skips* this slot in its zero-init loop; the later write is now traced — it happens at
     the scene-state-machine entry to the in-game state and stores a pointer to the ~0xC8-byte
     (≈200-byte) command-handler-derived **state-5 handler** (a third object, **not** the 16-byte
     `MainHandler` hub of §3.10a). Both the ctor-skip and the later write are CODE-CONFIRMED.

7. **`g_ActorMap_singleton` type.** A separate actor hash-map/tree pointer (not inside the
   ActorManager object) is used in the id-keyed actor-lookup hot path. The first access writes a
   head node with `[ptr+4] = head_node` — suggesting a tree or linked-list structure. Type not yet
   confirmed (std::map, custom hash table, or otherwise).

8. **Exact SoundManager size.** The ~640-byte estimate is from neighbour analysis; the init guard
   byte is at approximately +0xA0 from the object start, which may be the guard position rather
   than the object end. The true span needs a symbol-size query on the neighbouring named item.

9. **LuaConfig static size.** The guard byte is 4 bytes after the static start, which may mean
   the static holds only a thin wrapper (vtable + `lua_State*`). Trace `LuaConfig_Init` to confirm
   whether the static is 4–8 bytes or the full ~44-byte estimate.

10. **CoreMotManager / CorePoseManager / CoreSkinManager roles.** All three are 16-byte singletons
    adjacent in the data segment. Their roles in the animation-update call graph (clip catalog,
    pose blending, skinned-mesh cache) are inferred from naming. Confirm by tracing from
    `Actor__LockVB_RebuildSkin_Unlock` through the per-frame animation update chain.

11. **The unnamed ~23 069 `sub_*` functions.** Only ~2 904 functions are named; the majority are
    unclassified. A BFS/DFS from the named anchors at depth 2–3 would improve subsystem-level
    completeness estimates significantly.

12. **DirectDraw usage.** `DirectDrawCreateEx` is present in the import table but no named function
    references DirectDraw. It may be dead code, an artefact of a compiled-out feature, or used
    exclusively by the XTrap module cluster.

13. **Renderer object and the 16-slot render pointer-cache (§4 / §4.1) — re-walk pending
    (static-hypothesis).** These were not re-confirmed against the 263bd994 anchor in the
    2026-06-16 pass. A follow-up should re-walk the scene-state-5 render-subsystem initialiser and
    re-derive the Renderer object's key field offsets.

14. **`AppService` internal layout (§3.13).** Total span (136 bytes) is CODE-CONFIRMED; the
    field-level layout and the precise service role are not yet walked.

15. **NetClient inner field offsets (§3.3) and ActorManager inner fields (§3.7).** Object identity
    and total sizes are CODE-CONFIRMED; the inner offsets are carried forward as (static-hypothesis)
    and should be re-walked from the respective init/constructor routines.

16. **Display FRAMERATE config inertness — RESOLVED (CODE-CONFIRMED, CYCLE 7 2026-06-20).** The
    per-frame loop is software frame-capped at a fixed 60 FPS: the rate lives in the engine driver
    object's framerate field, **seeded once to 60.0f in the engine constructor and never
    overwritten**; a performance-counter-measured limiter sleeps each iteration to hold the target
    period (`Sleep((1/rate − dt) × 1000 ms)`, i.e. a ~16.67 ms/frame budget — an upper cap only, so
    busy frames run uncapped). The `DISPLAY_FRAMERATE` value read from the display config is parsed
    and stored into a field of the renderer/display singleton that **has no reader**: a statically
    **exhaustive** search of that field's address finds exactly two references and **both are
    stores** (the ctor zero-init and the config parse). The throttle therefore never consults the
    config value — the cap is confirmed **hardcoded 60 FPS / config-inert** (no longer pending). The
    driver loop has four phases (message-pump+input / scene-render+present / round-robin
    tick-scheduler / frame-throttle); the behavioural detail is owned by `specs/game_loop.md`.
