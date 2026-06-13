---
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
> - The 19 Meyers-singleton objects and their key field maps.
> - The render-subsystem pointer-cache block initialised at scene-state 5.
> - The three flat VFS globals (not a singleton object).
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
| `GameState` | 9-state scene machine; the WinMain dispatch key | 16 | §3.1 | CODE-CONFIRMED |
| `LuaConfig` | Lua 5.1.2 interpreter init and `game.lua` reader | ~44 | §3.2 | PLAUSIBLE |
| `BugTrap` | Crash-reporter init (BugTrap.dll wrapper) | ~16 | — | PLAUSIBLE |
| `NetClient` | TCP connection, cipher, LZ4, keepalive threads | 82 368 | §3.3 | CODE-CONFIRMED |
| `NetHandler` | 154-slot S2C response + push dispatch tables | 6 220 | §3.4 | CODE-CONFIRMED |
| `InputManager` | DirectInput polling + Win32 message dispatch | 116 | — | CODE-CONFIRMED |
| `BillingState` | Subscription / shop-page state machine | 256 | §3.5 | CODE-CONFIRMED |
| `AnimCatalog` | Animation clip catalog (keys `.mot` files) | 11 704 | — | CODE-CONFIRMED |
| `CoreMotManager` | Animation clip/motion manager (thin wrapper) | 16 | §3.6 | PLAUSIBLE |
| `CorePoseManager` | Pose-blending state manager | 16 | §3.6 | PLAUSIBLE |
| `CoreSkinManager` | Skinned-mesh cache manager | 16 | §3.6 | PLAUSIBLE |
| `ActorManager` | In-world entity container + spatial index | 300 | §3.7 | CODE-CONFIRMED |
| `SoundManager` | DirectSound wrapper, 3 internal sound lists | ~640 | §3.8 | CODE-CONFIRMED |
| `GHTexManager` | Primary effect/texture manager (EffectManager vtable) | 620 | §3.9 | CODE-CONFIRMED |
| `ShadowManager` | Per-actor shadow rendering | 316 | — | CODE-CONFIRMED |
| `RankProgress` | Rank/progression state | 308 | — | CODE-CONFIRMED |
| `MainWindow` | Root Diamond::GUWindow + service-slot block | 1 464 | §3.10 | CODE-CONFIRMED |
| `Engine` | Main-loop aggregate: holds Renderer + InputManager ptrs | 48 | §3.11 | CODE-CONFIRMED |
| `FrameTickScheduler` | Per-subscriber tick dispatcher | 72 068 | §3.12 | CODE-CONFIRMED |

> **Note on the Renderer.** The Renderer object (`Diamond::GHRenderer`, ~177 860 bytes,
> roughly 174 KB) is the largest static object in the binary. It is not constructed via a
> standard Meyers singleton guard; instead it is a raw static embedded in the data segment
> and its address is cached into a pointer-slot when the render subsystem is first initialised
> at scene-state 5. See §4 for its field map and §4.1 for the pointer-cache block.

---

## 3. Key field maps (offsets relative to object start)

All offsets are within the object. "→" notation means the field holds a pointer to another
object; the pointed-to type is named but its layout is in its own spec section or file.

### 3.1 GameState — 16 bytes (CODE-CONFIRMED)

Cross-reference: `specs/client_runtime.md §7` for the 9-state lifecycle description.

| Offset | Size | Type | Field | Notes |
|-------:|-----:|------|-------|-------|
| +0x00 | 4 | int32 | `scene_state` | The WinMain scene-state discriminator (0 = init … 8 = exit). Heavily cross-referenced throughout the binary. |
| +0x04 | 4 | int32 | `const_field` | Initialised to 8; secondary role unverified. |
| +0x08 | 4 | int32 | `third_field` | Initialised to 0; role unverified. |
| +0x0C | 1 | uint8 | `debug_mode` | Set from the `debugmode` key in `game.lua`. 0 in release builds. |
| +0x0D | 3 | — | (pad) | Alignment to 16-byte object end. |

### 3.2 LuaConfig — ~44 bytes (PLAUSIBLE)

Cross-reference: `specs/lua_scripting.md` for full Lua scripting behaviour.

| Offset | Size | Type | Field | Notes |
|-------:|-----:|------|-------|-------|
| +0x00 | 4 | ptr | vtable or lua_State* | The static holds a thin wrapper over a heap-allocated `lua_State`. The actual `lua_State` size is not part of this static. |
| +0x04 | ~40 | bytes | (wrapper fields) | Wraps init-guard, loaded-file tracking, and the `cpp_load` export. Internal layout unverified. |

> **Open question §7.9** — the static may be only 4–8 bytes (vtable + `lua_State*`), with the
> guard sitting at byte offset 4. The ~44-byte estimate is derived from guard-offset arithmetic
> and may overshoot. Trace `LuaConfig_Init` to confirm.

### 3.3 NetClient — 82 368 bytes (CODE-CONFIRMED)

Cross-reference: `specs/login_flow.md`, `specs/handlers.md` for protocol behaviour.

| Offset | Size | Type | Field | Notes |
|-------:|-----:|------|-------|-------|
| +0x00 | 4 | ptr | vtable | `Diamond::Network` vtable. |
| +0x28 | ~12 | obj | `server_addr_string` | `std::string` caching the last-connected server address. |
| +0x48 | ~72 | obj | `net_conn` | Embedded connection object (TCP socket, async I/O, send/recv queues). |
| +0x141B8 | 1 | uint8 | `connected` | Connected flag (non-zero when TCP link is established). Byte offset ~82 296 from object start. |
| +0x141C8 | ~16 | obj | `send_thread` | ThreadSlot for the async send worker. |
| +0x141D8 | ~16 | obj | `keepalive_thread` | ThreadSlot for the keepalive sender. |
| +0x141E4 | 4 | uint32 | `keepalive_interval_ms` | Keepalive interval; initialised to 20 000 ms (20 seconds). CODE-CONFIRMED. |
| +0x141EC | 4 | uint32 | `outstanding_acks` | Outstanding unacknowledged-packet count. |

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

### 3.7 ActorManager — 300 bytes (CODE-CONFIRMED)

Cross-reference: `structs/actor.md` for the entity layout; `names.yaml` (`g_ActorManager`,
`g_LocalPlayer`).

| Offset | Size | Type | Field | Notes |
|-------:|-----:|------|-------|-------|
| +0x00 | 4 | ptr | vtable | `ActorManager` vtable. |
| +0x04 | 4 | uint32 | `ref_count` | Reference count; incremented to 1 during construction. |
| +0x08 | ~44 | obj | `name_string` | `std::string` set to `"ActorManager_group"` during construction. |
| +0x38 | 1 | uint8 | `state_byte` | Initialised to 2. |
| +0x70 | 4 | ptr | `local_player_slot` | Pointer slot for the local-player actor; points into a nearby static slot. |
| +0x74 | ~192 | obj | `spatial_index` | Embedded spatial-index sub-object (collision / quadtree); constructed by its own internal constructor. Opaque to the domain model. |
| +0x10C | 4 | uint32 | (reserved) | Zero-initialised. |
| +0x114 | 4 | uint32 | (reserved) | Zero-initialised. |
| +0x118 | 1 | uint8 | `flag` | Zero-initialised. |
| +0x11C | 4 | uint32 | (reserved) | Zero-initialised. |
| +0x120 | ~16 | obj | `secondary_index` | Secondary lookup sub-object (tree/list complement to the spatial index). |

> **Companion pointer-slot.** A separate 4-byte pointer in the global data region (distinct from
> the ActorManager object itself) is set to the ActorManager's address during `NetHandler`
> construction and is used by the two primary actor-lookup hot paths: the id-keyed lookup and
> the `(id, sort)` composite-key lookup. This pointer is the global cross-reference that routes
> all entity accesses through the ActorManager singleton.
>
> **ActorHashMap.** A separate 4-byte pointer in the global data region holds the address of an
> actor hash-map/tree used in the id-keyed lookup hot path. Its type (std::map or custom tree)
> is not yet confirmed — see open question §7.7.

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
| +0x240 | ~12 | obj | `sub_list_A` | Effect sub-list A. |
| +0x24C | ~12 | obj | `sub_list_B` | Effect sub-list B. |
| +0x258 | ~12 | obj | `sub_list_C` | Effect sub-list C. |

### 3.10 MainWindow (Diamond::GUWindow) — 1 464 bytes (CODE-CONFIRMED)

Cross-reference: `specs/ui_system.md`, `specs/client_runtime.md §2`.

The `MainWindow` inherits from `Diamond::GUWindow` (which extends `Window → Panel → Component`).
Its first 0x237 bytes reproduce the base-class field layout documented in `specs/ui_system.md`.

| Offset | Size | Type | Field | Notes |
|-------:|-----:|------|-------|-------|
| +0x00 | 4 | ptr | vtable | `MainWindow::vftable` (installed through the Component base-class chain). |
| +0x04 | ~564 | bytes | (base-class fields) | Inherited `GUWindow → Panel → Component` fields. See `specs/ui_system.md`. |
| +0x238 | 892 | ptr[223] | `service_slots` | 223 pointer-width (4-byte) service-slot words, zero-initialised at construction. Subsystem initializers populate these during scene-state 5. Only a small subset are named — see note below. |
| +0x500 | 4 | ptr | `main_handler` | Pointer to the `MainHandler` object; assigned when scene-state 5 is entered. |
| +0x5B4 | 4 | — | (last service slot) | Byte boundary of the service-slot region at offset 1 460. |
| +0x5B8 | 4 | — | (tail pad) | Tail to the 1 464-byte object end. |

> **Service-slot decode status.** Only approximately 6 of the 223 slots (including `+0x500 =
> MainHandler*`) are identified. The full decode requires tracing all write sites that populate
> each slot offset after scene-state 5 initialisation. See open question §7.6.

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

---

## 4. Renderer object — key fields (~177 860 bytes, CODE-CONFIRMED)

The `Diamond::GHRenderer` static is the largest object in the binary (~174 KB). It embeds the
D3D9 device, display configuration, all render-target surfaces, toon-shader constants, and the
D3D present parameters. It is a raw static (not a Meyers singleton with an init guard) and its
address is cached into the render-subsystem pointer-cache block at scene-state 5 (§4.1).

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

## 5. VFS subsystem — three flat globals (CODE-CONFIRMED)

The VFS uses **three flat global variables** rather than a Meyers singleton object. There is no
C++ constructor guard and no `GetSingleton()` function. The `Diamond::CVFSManager` wrapper class
provides named access functions (`VFS_IsMounted`, `VFS_SetMounted`) but the underlying state lives
in three globals in the data segment.

Cross-reference: `formats/pak.md` for the on-disk archive format.

| Global # | Size | Type | Field | Notes |
|---:|---:|------|-------|-------|
| 1 | 4 | ptr | `toc_array` | Pointer to the table-of-contents (TOC) array. Each TOC entry is 144 bytes, sorted by lowercase file path. |
| 2 | 4 | uint32 | `toc_entry_count` | Number of entries in the TOC array. |
| 3 | 4 | HANDLE | `vfs_file_handle` | Win32 file handle for the `.vfs` data file; kept open for the entire session. |

Initialisation: `VFS_OpenArchive` is called once from WinMain scene-state 0, before the
scene-state machine loop begins. It opens `data.inf` (the TOC index file) and `data/data.vfs`
(the data archive), reads the TOC into the `toc_array`, sets `toc_entry_count`, and stores the
open file handle in `vfs_file_handle`. These three globals are then read-only for the rest of
the session.

> **Note on the TOC entry format.** The 144-byte stride of each TOC entry is confirmed by
> `formats/pak.md`, which owns the byte-level layout of `data.inf`.

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
   `data/data.vfs`; the three globals are filled. This happens before the scene-state loop.
5. *(Scene-state loop begins. All following steps are per-state.)*
6. (State 1) `MainWindow` — `Diamond_MainHandler_GetSingleton()` triggers lazy
   initialisation; the MainWindow static is constructed and the 223 service slots are
   zero-initialised.
7. (State 1) Font creation; window configured (`Engine_ConfigureWindow`). **The Renderer
   static is set up during `Engine_ConfigureWindow`** (not via a Meyers init guard).
8. (State 1) `NetClient` + `NetHandler` — network stack initialised. `NetHandler` constructor
   sets the `ActorManager` pointer (see §3.4 note); this is the first point at which the
   `ActorManager` singleton is created.
9. (State 1) `InputManager`.
10. (State 1) `BillingState`.
    *(Also within state 1: `AnimCatalog`, `CoreMotManager`, `CorePoseManager`,
    `CoreSkinManager`, `SoundManager`, `GHTexManager`, `ShadowManager`, `RankProgress`
    are each lazy-constructed on their respective first-use calls during scene-state 1 or 2.
    Exact first-use ordering within this group is unconfirmed.)*
11. (State 5, scene-build entry) The render-subsystem initialiser caches 16 pointer-slots
    (§4.1):
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
| `GameState` scene_state lifecycle (9 states) | `specs/client_runtime.md §7` |
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

- Exact object sizes for all 19 singletons (NetClient 82 368, FrameTickScheduler 72 068,
  NetHandler 6 220, etc.) — not previously documented in any committed spec.
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
- `MainWindow +0x500 = MainHandler*` assignment at scene-state 5 — noted in analysis but not in
  any committed spec.
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

6. **MainWindow service-slot full decode.** Only ~6 of the ~223 pointer slots at `+0x238..+0x5B4`
   are identified. A complete map requires tracing all write sites that populate each dword after
   scene-state 5 initialisation. This is a significant analyst task.

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
