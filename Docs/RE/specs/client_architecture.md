---
verification: confirmed (re-confirmed against IDB SHA 263bd994, CYCLE 7 (2026-06-20))
ida_reverified: 2026-06-27   # CYCLE 14 re-anchor (f61f66a9): confirmatory — Single Win32 window / VFS-mount / scene-machine cleanly relocated, 1 re-confirmed SAME, 0 corrected; prior 2026-06-18: scene re-confirmation campaign (build 263bd994)
ida_anchor: f61f66a9ae0ec1e946105b2ecff76e8930cb1d1367df64e5688a5266f5ad9963
evidence: [static-ida, vfs-sample]
conflicts: none
---

# Client Architecture — Master Synthesis (CAMPAIGN 10)

> **Clean-room neutral synthesis.** Promoted from dirty-room analyst notes under EU Software
> Directive 2009/24/EC Art. 6 (decompilation permitted solely to achieve interoperability). This
> file contains **no decompiler pseudo-code, no binary virtual addresses, no decompiler identifiers**
> (`sub_`, `loc_`, `dword_`, `__thiscall`, `_DWORD`, mangled names). Struct/record byte offsets,
> `GameState` case numbers, opcode `(major, minor)` pairs, frame-header layouts, and file sizes are
> interoperability facts and are stated where load-bearing.
>
> **What this document is.** The single top-level map of how the legacy `doida.exe` client (build
> SHA `263bd994`) is *constructed*, how it *boots*, and how it *runs* — from OS process start through
> every interactive scene into the live game world, and across every runtime subsystem. It is a
> **synthesis**: each section describes its slice of the architecture in neutral prose and
> **cross-links the authoritative subject spec** that owns the detail. It deliberately does **not**
> duplicate the satellite specs — read this file first for the whole-client picture, then follow a
> cross-link for the exact constants you need.
>
> **Verification basis (Campaign 10).** The entire `Docs/RE` knowledge base was re-confronted against
> the live IDB on build `263bd994` (static IDA) and corroborated against the shipped VFS sample where
> a real asset witnessed a fact. No live network capture and no debugger session were used this
> campaign; wire-byte *value* semantics therefore remain capture/debugger-pending (see §12).
>
> **Evidence grades used inline:** **CODE-CONFIRMED** (re-derived from binary control-flow logic) ·
> **SAMPLE-VERIFIED** (additionally cross-checked against real shipped VFS bytes) ·
> **CAPTURE/DEBUGGER-PENDING** (not exercised this campaign — do not hard-code).

---

## 0. The client in one paragraph

`doida.exe` is a **single-process, single-threaded-driver Win32 application** built on the in-house
**"Diamond" engine** (Direct3D 9, DirectSound, an embedded Lua 5.1.2 VM). Its application entry point
(`WinMain`) **is** the scene state machine: after a one-time bootstrap (Lua config → launcher gate →
VFS mount → message catalogue) it runs an infinite `while(1) switch(GameState)` over eight cases
(`0..7`), and **each case constructs its own scene-handler object and re-enters one shared four-phase
per-frame loop** until that scene signals completion. The flow is **login (1) → load (2) →
opening (3) → character-select (4) → world (5)**. All presentation is retained-mode "Diamond" UI
windows over a code-baked layout; all asset bytes come from a single mounted VFS through one
file-open chokepoint; all networking is an asymmetric framed protocol (outbound cipher+LZ4, inbound
LZ4-only) dispatched by `(major, minor)` opcode. Background work is minimal: a **dormant** terrain
streamer, a boot data-table worker behind the loading screen, and a streaming-BGM refill thread.

---

## 1. Process & threading model

(see `specs/client_runtime.md` §0/§8, `specs/game_loop.md` §0/§7, `specs/resource_pipeline.md` §5)

- **Single-threaded driver.** The client is driven by **one main thread**. `WinMain` is both the
  bootstrap and the scene state machine; the per-scene blocking run loop (§5) lives on this thread,
  and all rendering, input, scene logic, and the logic-tick scheduler run there. (CODE-CONFIRMED)
- **Background threads are few and special-purpose:**
  - **Boot data-table worker** — installed by the load-handler constructor, *started* by the
    loading-screen sub-initialiser at **ABOVE_NORMAL** priority; loads the ~50-table global corpus
    in a fixed compiled order, then a 500 ms grace, then clears its done-flag and exits (§6).
  - **Terrain streamer worker** — created before world entry, but **DORMANT in this build**: the
    constructor arms its keep-running flag and `init` immediately re-clears it, so the worker exits
    at once; its request FIFO has no producer. Peripheral cell streaming is therefore effectively
    inert and the synchronous 3×3 ring (§6) is what populates the world. (CODE-CONFIRMED — see
    `structs/terrain-manager.md`.)
  - **Streaming-BGM refill** — the sound worker drains an event queue and refills the streaming ring
    buffer (~5×/s); see §9 and `specs/client_runtime.md` §1.7.
  - **Network workers** — the network client spawns a **receive worker** (a pure producer that pops
    a received packet and posts a dispatch event) plus the connection's I/O-side worker (§7).
- **The clock.** Logic time is a **monotonic millisecond multimedia clock** (`timeGetTime`-style),
  optionally scaled by an engine-wide time-scale float (slow-mo / fast-forward). The frame *throttle*
  separately samples the high-resolution performance counter. (CODE-CONFIRMED — `specs/game_loop.md`
  §4.)

---

## 1A. Object model, RTTI & memory lifecycle

(see `structs/runtime_singletons.md` — the Diamond base-object layout; `specs/game_loop.md`,
`specs/client_runtime.md` for the frame loop)

The 3D layer **is** the **"Diamond" scene-graph engine** — an OpenSceneGraph-style Direct3D 9
scene graph (the engine name is proven by an embedded build-path string referencing a `diamond`
source directory). Roughly **90 engine RTTI classes** are present. This section supersedes any
earlier "object lifecycle: NONE" statement: the engine has a single, uniform refcounted object
model. (CODE-CONFIRMED — static.)

### 1A.1 Class hierarchy

A single abstract refcounted base anchors the whole engine. The root chain is:

```
GObject  (abstract refcounted base)
  └─ GNode  (adds a parent back-reference vector)
       ├─ GViewPlatform  (per-frame view processing; drives cull/draw)
       └─ GGroup  (adds a child vector)
            └─ { GScene, GTransform, GGeode, GSwitch, GLight, … }
```

Every Diamond class — render-state (`GRS*`: depth-test / alpha-test / blending / material / fog /
…), UI (`GU*`: GUWindow / GUComponent / GULabel / …), pipeline (GPipeline / GCullPipeline / GCull /
GTraverser / …), assets (GTexture / GSound / CVFSManager / DiskFile / File / …), and camera
(GCamera / GPerspectiveCamera / GFrustum / …) — derives from `GObject` and therefore shares the
same object head described below.

### 1A.2 Shared base layout (the head of EVERY Diamond class)

| Offset | Size | Field | Notes |
|-------:|-----:|-------|-------|
| +0x00 | 4 | vtable pointer | Installed by each constructor; the abstract base's slot 0 is a pure-virtual stub. |
| +0x04 | 4 | `ref_count` | Intrusive reference count, initialised to 0. |
| +0x08 | 0x1C | `name` | An MSVC `std::string` object name; its constructor allocates, its destructor frees the buffer. |

`GNode` extends this head with a **parent back-reference vector** and `GGroup` adds a **child
vector** (offset table in `structs/runtime_singletons.md`). `GViewPlatform` is a `GNode` subclass
with an 84-byte (0x54) object size; `GScene` / `GGroup` share the same +0x00 / +0x04 / +0x08 head.

### 1A.3 Reference-count mechanism (the OpenSceneGraph "Referenced" pattern)

- **`ref()`** is an **inlined increment** of the `ref_count` field (+0x04), performed when a child
  is added to a parent.
- **`unref()`** is a **dedicated routine** that first **asserts the count is non-zero**, then
  decrements `ref_count`.
- There is **no separate AddRef / Release vtable slot** — `ref()` / `unref()` are non-virtual
  operations on +0x04.

### 1A.4 Destruction shape (MSVC vector-deleting destructor)

Virtual-table **slot 0** of every Diamond class is the MSVC **"vector deleting destructor"**: it
takes a flags byte, runs the real destructor body, then frees the object when `(flags & 1)`. So
`delete obj` = run the virtual destructor chain (derived → base, each releasing its owned children)
then free the heap block originally returned by `operator new`.

### 1A.5 Ownership: owner-frees-children by refcount

`GGroup` / `GNode` hold a child vector:

- **addChild** increments the child's `ref_count` and registers a parent back-reference.
- **removeChild** / **removeAllChildren** decrement the child's `ref_count` and detach it.
- On destruction or removal, a child's vector-deleting destructor is invoked **only when that
  child's `ref_count` has reached 0** — objects are freed at refcount 0. Parent back-references
  live in a second (parent) vector.

### 1A.6 Allocator model

The engine and gameplay allocate through **plain CRT `operator new` → `malloc` → Win32 `HeapAlloc`
on the process heap**. There is **no custom game pool / arena / free-list allocator**;
placement-`new` appears only as STL in-place construction into pre-sized container storage. (Each
class news its own members and children — allocation is decentralised, not funneled through a
central pool.) The bundled third-party static libraries carry their own internal allocators and are
tagged separately; they are not the game's pattern. Steady-state per-frame heap churn is low: the
render path reuses persistent draw / render-bin / particle containers (cleared-and-refilled), so
the dominant heap activity is at **scene-mutation** time (spawn / despawn, area load, effect build),
handled by the CRT new/delete + refcounted node lifecycle above. (Exact container reset-vs-reserve
thresholds are a runtime detail — capture/debugger-pending.)

### 1A.7 Frame-rate cap

The main loop is paced by a **hardcoded fixed 60 FPS upper cap**, seeded once into the engine
driver object and consumed directly by the frame throttle; the `DISPLAY_FRAMERATE` display-config
value is parsed but has **no reader** and is therefore inert. See `specs/game_loop.md` /
`specs/client_runtime.md` for the limiter detail and §5 below.

---

## 2. Boot & init scopes

(see `specs/client_runtime.md` §0, `specs/game_loop.md` §0, `specs/resource_pipeline.md` §1–§2,
`specs/lua-config.md` / `specs/lua_scripting.md`)

The ordered bring-up from process start to the first interactive frame (all CODE-CONFIRMED):

1. **`start` → CRT static initializers.** MSVC runtime runs all C++ static constructors before
   transferring to `WinMain`. The one load-bearing static object is a **billing/anti-cheat scheduler
   proxy** that gates engine entry (failure at state 1 → `exit(1)`). There is **no `setlocale` /
   `_setmbcp`** — CP949 is handled per-string at use-sites (§ below).
2. **`WinMain` top — `game.lua`.** A Lua-config singleton reads three booleans, each defaulting to
   `true` if the file is absent: `vfsmode` (packed VFS vs loose files), `launcher` (require the
   external launcher unless `-Start`), `debugmode` (developer gate, stored in the engine-state byte).
3. **Launcher gate.** If `launcher` is set **and** the command line is not exactly `-Start`, the
   client shells out to the external updater `dostart.exe` (`WinExec`) and returns. The scene machine
   runs only for the real `-Start`/post-launcher process.
4. **VFS mount — ONCE, before the loop.** `data.inf` (index) is opened `FILE_FLAG_RANDOM_ACCESS`, a
   **24-byte header** is read, the **entry count is the 4th dword (byte +0x0C)**, a TOC of
   **144 bytes/entry** is read into memory, the index handle is closed, and `data/data.vfs` is opened
   and its handle retained for the process lifetime. (Container byte layout owned by `formats/pak.md`;
   runtime mount mechanics by `specs/resource_pipeline.md` §1.5.)
5. **State-0 / state-1 device bring-up.** `DoOption.ini` parses ~30 `[DO_OPTION]` keys (default
   1024×768×32, all clamped); the D3D9 device is created (§5); the **15 Korean font slots**
   (DotumChe / Dotum / BatangChe, Hangul charset) are registered; **`data/script/msg.xdb`** (the
   CP949 UI string catalogue, fixed **516-byte** records keyed by a 4-byte id) is loaded
   synchronously on the main thread.
6. **The `GameState` ladder begins** at state 0, which advances immediately to state 1 (Login).

> **CP949 model.** Korean game/config/dialog text is the legacy MS-949 code page, decoded
> **per-string** at each read site (`MultiByteToWideChar(949, …)`), never as a global locale switch.
> A clean-room port registers `CodePagesEncodingProvider` once and calls `Encoding.GetEncoding(949)`
> per site. (CODE-CONFIRMED — `specs/client_runtime.md` §0.6.)

---

## 3. The GameState scene machine (0..7)

(see `specs/client_workflow.md` §4 — the master flow; `specs/client_runtime.md` §7;
`specs/frontend_scenes.md`; `specs/intro_sequence.md`; `specs/login_flow.md`)

`WinMain` mounts the VFS once, then runs `while(1) switch(GameState)` over a 3-int record
`[state, sub-state, reason]`. The switch is **bounds-checked (`state ≤ 7`) over a jump table of
exactly 8 entries (states 0..7) plus a default**. (CODE-CONFIRMED)

| State | Name | Handler | Role |
|------:|------|---------|------|
| 0 | Init | inline | device sizing; advances to 1 |
| 1 | Login | `LoginWindow` | first interactive screen; credential + PIN handshake; lobby fetch |
| 2 | Load | load-handler / loading-screen | boot data-table corpus behind the loading screen; intro **SKIP gate** |
| 3 | Opening | opening window | optional intro cinematic (post-login) |
| 4 | Character Select | `SelectWindow` | 5 spawn-descriptor slots; live 3D preview; pick/enter |
| 5 | In-game | `MainHandler` + scene graph | the live world |
| 6 | Quit | inline | engine shutdown; writes the exit sentinel |
| 7 | Error | inline | modal dialog from the `reason` field; closes the net client; writes the exit sentinel |

**The value `8` is a terminal exit SUB-state, not a 9th case** — states 6 and 7 write `8` so the next
`while(1)` iteration falls into the switch **default** (reverse-singleton teardown + `WinMain`
return). (CODE-CONFIRMED — earlier "states 0..8 / nine-state" phrasing is wrong.)

**Per-case contract:** each case (1) pre-writes the *next* state (intent), (2) constructs its scene
handler, (3) calls the shared four-phase loop (§5) until a one-byte **run-flag** is cleared, (4)
destructs the handler, (5) falls through to re-dispatch. A transition is effected by writing the next
state and calling the run-flag-clear (break) routine; both network-driven and user-driven transitions
use this same mechanism, often bridged by a universal **timed-engine-event** (`10001`, delays
1000/1500/5000/10000/30000 ms — e.g. the 30000 ms login watchdog).

**Non-obvious edges:** in-game (5) drops back to **character-select (4)**, *not* login, on
logout/disconnect; **login (1) and the post-login load (2) are visited once per process**; the
opening (3) is skipped when the `[OPENNING]`/`SKIP` INI key is non-zero (2 → 4 directly).

---

## 4. The Diamond UI / window framework

(see `specs/ui_system.md`; `structs/guwindow.md`; `structs/gucomponent.md`; `specs/input_ui.md`;
`formats/ui_manifests.md`)

A **custom retained-mode widget toolkit**, the `Diamond::GU*` family, single-inheritance from
`GUComponent` with `GUPanel` (adds a child vector + active-child index) and `GUWindow` (top-level)
above it. (CODE-CONFIRMED)

- **`GUWindow` is a two-vtable MSVC multiple-inheritance object:** the primary vtable at **+0x00**
  (component/panel/window chain, 15 slots), a secondary **command-handler** ("Cmdhandler") vtable at
  **+0xBC** (3 slots, 44-byte sub-object +0xBC..+0xE7), the embedded auxiliary **GView** at **+0xE8**
  (312 bytes), and the per-window **texture list** at **+0x220**. The "is-a-window" flag bit
  (mask `0x2000`) is OR-set last in the constructor. (CODE-CONFIRMED — `structs/guwindow.md`.)
- **`GUComponent` geometry fields:** width **+0x1C** / height **+0x20** / local X **+0x14** / local Y
  **+0x18**; the live atlas src-RECT at **+0x34** and the D3D translation matrix at **+0x44**. The
  **tint/colour is +0x0C** and the **actionId is +0x10** (a critical distinction — reading +0x0C as an
  action id routes to the wrong handler). Alpha fades ±64/tick (`GUComponent`) or ±32/tick
  (`GUComponentEx`). (CODE-CONFIRMED — `specs/ui_system.md` §1.2, `structs/gucomponent.md`.)
- **Layout is CODE-BAKED.** Every screen is built by a per-window `BuildScene` (vtable slot 14) that
  issues direct constructor calls with literal pixel coordinates on a fixed **1024×768** canvas —
  there is **no on-disk UI manifest** (the recovered coordinates are interop facts). Widgets are
  textured atlas sub-rects blitted through a single shared `ID3DXSprite`; captions come from `msg.xdb`
  by numeric id; text is GDI/D3DX Hangul fonts (the 15 boot slots). (CODE-CONFIRMED)
- **The five top-level windows** derive from `GUWindow`: LoginWindow, the server-select surface,
  the opening window, SelectWindow, and the in-game HUD master **MainMaster** — the latter being the
  window manager that owns a **~223-slot service-slot table** of HUD panels. (CODE-CONFIRMED — see
  `structs/runtime_singletons.md`.)
- **Event taxonomy.** Input dispatch is by event-type bitmask (`1 << type`), first-consumer-wins;
  types 1 key-down … 8 wheel, with type **6 = synthesised click**. The top-level UI hit-test walks
  from the root window and consumes mouse events **before** world-entity picking (UI first). The Korean
  IME registers the focused textbox for composition input. (CODE-CONFIRMED — `specs/input_ui.md`.)
- **Scene-dispose list.** The per-window texture list is released on scene unload by the
  scene/window teardown (the *window*, not the manager, owns disposal of its eager-loaded atlases —
  the third texture-lifetime tier of §6). (CODE-CONFIRMED — `specs/resource_pipeline.md` §3A.4.)

---

## 5. The per-frame loop & render pipeline

(see `specs/game_loop.md` §7; `specs/client_runtime.md` §3/§8; `specs/rendering.md`)

Every scene re-enters **one shared engine main loop**. On first entry it caches the tick-scheduler
context; on every entry it raises the multimedia timer to 1 ms and sets the run-flag, then runs
`do { … } while (run-flag)` over **four phases in this exact order** (CODE-CONFIRMED):

1. **Pump + input** — `PeekMessage` → `GetMessage`/`Translate`/`Dispatch` until empty (a quit message
   clears the run-flag), then a critical-section swap-and-drain of a **double-buffered input-event
   queue**, dispatching each queued event to the active scene by event type.
2. **Scene update + render + present** — per-scene pre-update (frame counter, camera/view-matrix
   rebuild, frustum cull), then the scene render via **one of two render paths** (offscreen
   render-target vs direct-draw, chosen by a renderer flag), then the back-buffer **Present**; a
   **device-lost** branch handles `DEVICELOST` (sleep ~1000 ms, retry) and `DEVICENOTRESET`
   (release → reset → recreate default-pool resources).
3. **Logic tick** — the round-robin tick scheduler samples the ms clock once, advances ~**1.1 %** of
   subscribers per frame, and dispatches each whose `(now − last) > interval` (a **threshold** model,
   no leftover-time carry).
4. **Frame throttle** — the high-resolution counter measures the real frame delta; the target is
   `1/framerate` where `framerate` is the engine object's field **seeded to 60.0 and never
   overwritten** → an **effective fixed ~60 FPS software cap**. The throttle sleeps the remainder.

**Frame pacing is the §-4 throttle, not vsync:** the present interval is **IMMEDIATE (vsync OFF)**.
The `DISPLAY_FRAMERATE` display-config value is read raw and is **statically inert** (no consumer
reaches the throttle) — treat the cap as the hardcoded 60 FPS (whether a runtime path re-wires it is
CAPTURE/DEBUGGER-PENDING).

**Render pipeline (Diamond, D3D9).** Device = HAL, hardware vertex processing + multithreaded, SDK
version 32, back-buffer X8R8G8B8, depth fallback D32 → D24X8 → D24S8 → D16. The headline feature is a
**toggleable render-to-texture toon + glow/bloom post chain** (CODE-CONFIRMED — `specs/rendering.md`,
`specs/client_runtime.md` §3):

- **Plain path (toon OFF):** fixed-function multitexture into the back buffer; no shaders.
- **Toon path (ON):** Phase A renders the scene into an offscreen colour target; Phase B is a
  **fixed-function glow/edge extract + downsample** (no pixel shader); Phase C is the **bloom
  blur/downsample** (config-supplied blur shader, 3 RTs total); Phase D composites with `finaldx8.psh`
  uploading **c0 = edge weight, c1 = bloom weight**, then runs the transparent overlay; finally the
  composited frame is blitted to the back buffer and the **2D UI/HUD draws LAST** before Present.
- Cel quantisation uses a **256×1, 24-bpp `toonramp.bmp` LUT** bound to stage 1; skinned actors use a
  **32-byte vertex stride** and upload **one composite world·view·projection matrix** (CPU skinning,
  not a GPU bone palette — see §9).

---

## 6. VFS / resource pipeline

(see `formats/pak.md`; `specs/vfs_overview.md`; `specs/asset_pipeline.md`;
`specs/resource_pipeline.md`; `specs/terrain-streaming.md`; `structs/terrain-manager.md`)

- **Container.** `data.inf` + `data/data.vfs`: a **24-byte header** (entry count at +0x0C), a
  **144-byte TOC stride** (path at +0, i64 data-offset at +104, i64 data-size at +112), **RAW
  (uncompressed) storage**, FILETIME timestamps. The 43,347-entry shipped archive is byte-confirmed.
  (`formats/pak.md` — SAMPLE-VERIFIED.)
- **Open chokepoint.** Every open (~150+ sites) routes through **one open router** gated by the
  mount-flag and a raw/seek bit: mounted → binary-search the sorted lowercased TOC then `malloc` +
  **`ReadFile` into a fresh heap buffer** (the blob is **never memory-mapped**), under a read critical
  section; unmounted → loose-file `CreateFile`. **No file-level cache** — two opens do two reads (a
  clean-room port may freely add an LRU). (CODE-CONFIRMED — `specs/resource_pipeline.md` §1.)
- **Asset → runtime object.** Per-subsystem managers are grow-only **find-or-load** sorted maps
  (skin / motion / bind-pose / named-texture / terrain pool), **no eviction during a session**, torn
  down on scene exit. GPU textures are **D3DPOOL_MANAGED** (D3D9 owns VRAM residency). Three texture
  tiers: two **global boot pools** (`UiTex.txt` id pool + `bmplist.lst` effect pool), per-subsystem
  caches, and **per-scene window-owned texture lists** (eager-load atlases, release on scene unload).
  (CODE-CONFIRMED — `specs/resource_pipeline.md` §3/§3A.)
- **Boot corpus + loading screen.** State 2 runs a **~50-entry data-table corpus** (events, items,
  skills, npc, mobs, quests, …) on the ABOVE_NORMAL worker behind a loading screen whose progress bar
  divides a cumulative byte counter by a **hardcoded 9,395,240** denominator by *integer* division —
  so the bar is **near-static**; completion is gated solely by the worker's done-flag (+ 500 ms
  grace), never by the bar. Loading renders at ~10 FPS (`Sleep(100)`). (CODE-CONFIRMED.)
- **Terrain streaming.** **Two singletons** point at each other: a **TerrainLoader** ("the streamer")
  owning the dormant worker, the request FIFO, the load Event, and a **34-slot cell pool** + the
  area cell-key red-black-tree set; and a **TerrainManager** owning a **25-slot ring**, the centre
  cell, two view frustums, and the stream radius. Both index the same 34 heap cells via independent
  pointer arrays. The cell key is `mapZ + 100000·mapX`. World entry **synchronously** loads the **3×3
  ring** (5×5 above a quality radius) before the first frame; the peripheral streamer is **dormant**
  in this build. (CODE-CONFIRMED — `structs/terrain-manager.md`, `specs/resource_pipeline.md` §4.)

---

## 7. Network spine

(see `specs/network_dispatch.md`; `specs/crypto.md`; `specs/handlers.md`; `opcodes.md`)

- **Frame header (8 bytes, little-endian):** `[u32 size @+0][u16 major @+4][u16 minor @+6]`, payload
  from +8. `size` is a **true u32** (long-standing u16-vs-u32 question RESOLVED); header-only frames
  (`size == 8`, heartbeats) bypass all transforms. The `(major, minor)` pair **is** the opcode; there
  is no sequence number or checksum. (CODE-CONFIRMED — `opcodes.md`, `specs/crypto.md` §2.)
- **Asymmetric transform pipeline.** **Outbound (C2S):** prepend a `GetTickCount` timestamp →
  **keyless byte cipher** → **LZ4 compress** → enqueue/send. **Inbound (S2C): LZ4-decompress ONLY**
  into a fixed **11680-byte** buffer, then route — **no inverse byte cipher** (proven by a positive
  single-caller fact: the cipher's only cross-reference is the outbound gate). (CODE-CONFIRMED —
  `specs/crypto.md`, `specs/network_dispatch.md` §6.)
- **Dispatch fan-out by major family** (CODE-CONFIRMED routing — `specs/network_dispatch.md` §1):
  - **major 0** — a **hardwired `(0,0)` handshake branch** (not a switch): parses the inbound RSA/FLINT
    key blob (`[54-byte key blob][u32][u32]`) and emits the **C2S `1/4`** credential reply (PKCS#1 v1.5
    type-2 + per-dword whitening).
  - **majors 1 and 3** — inline switches on the minor (major-1 wires only S2C billing/letter minors
    16/17/19/20; major-3 enumerates 1/4/5/6/7/8/13/14/23/100 + the 50000 GM special).
  - **majors 4 (Response) and 5 (Push)** — **table-driven**, two physically adjacent **154-slot**
    tables (Response then Push); unset slots are inert no-ops, `minor ≥ 154` is undispatched;
    ~99 Response + 65 Push slots installed; `4/500` and `4/50000` routed outside the table.
  - **major 2** is **C2S-only** (no inbound `case 2`).
- **Lifecycle.** The network client does Winsock 2.2 bring-up, spawns a **receive worker** (pure
  producer → posts a dispatch event) and the connection I/O worker, and runs a separate
  **connection-state machine** keyed on internal codes (201/202/203/232 — meanings inferred,
  CAPTURE/DEBUGGER-PENDING) with no packet-body reads.
- **Keepalive — two distinct mechanisms** (both real; on-wire cadence CAPTURE/DEBUGGER-PENDING): a
  **ctor-armed `(2, 10000)` periodic frame** at a 20 s interval, and a **runtime C2S `2/112` toggle**
  gated by a master-enable flag (set on world-enter, cleared on leave).

---

## 8. Gameplay systems (GameState 5)

(see `specs/world_systems.md` as the index; per-subsystem specs cited below)

GameState 5 is **server-authoritative**: the client emits *intent* and *presents* server-pushed
state. The subsystems (routing/sizes CODE-CONFIRMED; wire-byte values CAPTURE/DEBUGGER-PENDING):

- **Combat** (`specs/combat.md`) — server-authoritative damage; basic **melee = C2S skill `2/52`,
  slot `0xFF`**, default-attack id `121100050`; derived stats compose equipment + per-character
  modifier slots with documented skip rules.
- **Skills / buffs** (`specs/skills.md`) — a `skills.scr`-driven catalog, a cast pipeline, a
  **240-slot parallel cooldown ("recast") table** (skill hotbar entries are 8 bytes each), and a
  skill→effect dispatch on S2C `5/52`; buff/debuff status on `4/102`.
- **Inventory / equipment / shop / trade** (`specs/inventory_trade.md`) — grid + equipment-slot
  model; equip/unequip/swap, quick-use, timed upgrade/enchant channels; storage on `2/142`.
- **Progression** (`specs/progression.md`) — six channels: exp `5/9`, rank/honor `5/11`, level-up
  `5/32`, stat-allocate C2S `2/29` / S2C `4/29`, authoritative resync `5/67`.
- **Quests & NPC interaction** (`specs/quests.md`, `specs/npc_interaction.md`) — a central click
  router by NPC **KIND**, pre-built service panels (shop/repair/storage), quest-action C2S `2/28`
  (fixed 12 bytes), quest-log snapshot `5/68`, completion `5/73`.
- **Chat / social** (`specs/chat.md`, `specs/social.md`) — say `2/7`, channel byte model, overhead
  bubbles; party/guild/friend; chat channel S2C `3/21`, broadcast `5/7`.
- **Minimap** (`specs/minimap.md`) — HUD radar (world→minimap transform + live blips) and a
  full-screen world map.
- **Camera & movement** (`specs/camera_movement.md`) — **five view modes** (Third / First / Static /
  Gamble / Event) switched without a pointer swap; FOV 65°, near 5, far 15000; movement intent C2S
  `2/13`, actor movement S2C `5/13`; collision via `.sod` segments + `.ted` ground height (§10).
- **Lua** (`specs/lua_scripting.md`) — an embedded **Lua 5.1.2** VM via a LuaTinker binding for UI
  events and config trees (33 bound functions; `game.lua`/`uiconfig.lua`/`display.lua`).

---

## 9. Render / effects / skinning / environment / sound runtime

(see `specs/effects.md`, `formats/effects.md`, `specs/effect-scheduling.md`; `specs/skinning.md`;
`specs/environment.md`; `specs/sound.md`)

- **Effects** — class hierarchy `XEffect` → `UserXEffect` / `JointXEffect` / `MapXEffect` /
  `CoreXEffect`; two coexisting subsystems (`ParticleEffectManager` for `resource_id ≥ 10000`,
  `SwordLightManager` for weapon trails); pool-allocated. The per-keyframe `.xeff` curve **passes
  2/3/4 are diffuse R/G/B** (not scale — renderer tints by real diffuse). Timed effects are armed via
  a **`10001` deadline RB-tree / per-frame tick spine** with a `+64` elapsed-origin convention.
  **The original applies NO Z-negation to a sub-effect anchor** — the historical "flying pixels" bug
  was the port forgetting to negate the sub-effect offset Z like the anchor (`Position = (Vx,Vy,−Vz)`).
  (CODE-CONFIRMED.)
- **Skinning** — **CPU linear-blend skinning with unit quaternions + 3-vectors, no 4×4 matrix and no
  GPU bone palette**; the **inverse-bind is baked once at load into each vertex's per-influence local
  position**, so the rest pose reproduces exactly (the cancellation identity that a naïve setup lacks
  — this is the avatar-explosion fix target). Deformed vertices upload as a dynamic vertex buffer; the
  GPU sees one composite WVP. (CODE-CONFIRMED — `specs/skinning.md` §0.)
- **Environment** — per-area sky/light/fog/material binaries; a day/night driver gated ≥ 50 ms;
  the recovered "too dark" fix is a **white ambient floor** (achromatic, fog OFF for the char-select
  stone temple). **Water is RESOLVED-NEGATIVE** — the legacy client has no water renderer and no water
  loader; any water plane is a free port choice. (CODE-CONFIRMED — `specs/environment.md`.)
- **Sound** — DirectSound 2D/3D split by category (`category < 5 ⇒ 2D`), mono-3D / stereo-2D codec
  rule, OGG-Vorbis decode into a 512 KiB scratch (1 MiB ring for streaming BGM), a per-map five-table
  ambient driver (`.run`/`.wlk`/`.bgm`/`.bge`/`.eff`), and an actor-event SFX router. (CODE-CONFIRMED
  / SAMPLE-VERIFIED — `specs/sound.md`, `specs/client_runtime.md` §1.)

---

## 10. Recovered asset chains & coordinate conventions

(see the `martial-heroes-domain` knowledge index + the cited format specs)

**Asset resolution chains** (the mappings that make the world render — SAMPLE-VERIFIED where a real
VFS file witnessed the hop):

- **Terrain texture:** cell `.ted` `TextureIndexGrid` byte → cell `.map` terrain/building
  textures[idx−1].`intTexId` → `bgtexture.txt[id]` → `data/map000/texture/<rel>.dds`. Textures are
  **global under `map000`** for all areas. (`formats/terrain.md`, `formats/bgtexture_lst.md`)
- **Character skin:** `.skn` `IdA` → `data/char/skin.txt` col4 → col5 `tex_id` →
  `data/char/tex{512512|10241024|…}/{id}.png`. (`specs/skinning.md`)
- **Character skeleton / idle:** skeletons are pre-loaded by **name** from
  `data/char/bind/bindlist.txt` (only `g1..g4.bnd` exist) and SELECTED via the AnimCatalog keyed by
  the appearance slot `IdB`; idle motion via `actormotion.txt` (col2 == IdB → col16) →
  `data/char/mot/g{id}.mot`. There is **no `g{IdB}.bnd` rule**. (`formats/bindlist.md`,
  `formats/actormotion.md`)
- **Mob → skin:** `mob_id` → `actormotion.txt` col1 → col2 `skin_class` → the `.skn` whose
  `IdB == skin_class`; skeleton resolves through the same catalog/IdB lookup.
- **Spawns:** `npc{tag}.arr` = 28-byte records; `mob{tag}.arr` = 20-byte records
  (`formats/npc_spawns.md`).
- **Collision:** `.sod` = 2D XZ wall segments (ray-parity point-in-polygon); ground height from `.ted`
  bilinear interpolation. (`formats/terrain.md`)

**Coordinate conventions** (get these wrong and the world mirrors):

- **World geometry negates Z** — `Helpers/WorldCoordinates.ToGodot`: `(x,y,z) → (x,y,−z)`.
- **Mesh-local `.skn` geometry negates X.**
- Cells are **1024 units**, on a **65×65** grid, spacing **16**.

---

## 11. Spec index

Each subject spec mapped to the section here that summarizes it.

| Subject spec | Owns | Section |
|---|---|---|
| `specs/client_runtime.md` | boot, singletons, sound, UI struct, render constants, scene lifecycle | §1, §2, §3, §4, §5, §9 |
| `specs/game_loop.md` | bootstrap + four-phase loop + timing/clock | §1, §2, §5 |
| `specs/client_workflow.md` | master end-to-end flow + scene state machine + transitions | §3 (master cross-link) |
| `specs/frontend_scenes.md` | login/server/select/create scene detail + dolly camera + ambient FX | §3, §4 |
| `specs/intro_sequence.md` | opening cinematic | §3 |
| `specs/login_flow.md` | login handshake sub-machine + PIN modal + server records | §3, §7 |
| `specs/ui_system.md` | widget toolkit, vtable contract, per-screen layouts, font table | §4 |
| `structs/guwindow.md` | two-vtable window layout (+0xBC / +0xE8 / +0x220) | §4 |
| `structs/gucomponent.md` | base widget field offsets + flag bits | §4 |
| `specs/input_ui.md` | input event taxonomy + IME + hit-test ordering | §4 |
| `formats/ui_manifests.md` | `UiTex.txt` / `uitex.txt` id-resolved atlases | §4, §6 |
| `structs/runtime_singletons.md` | singleton construction order + MainMaster service slots + Diamond base-object layout | §1A, §2, §4 |
| `specs/rendering.md` | per-frame draw loop, draw order, glow/bloom chain | §5 |
| `formats/pak.md` | `.inf`/`.vfs` container byte layout | §6 |
| `specs/vfs_overview.md` | VFS overview | §6 |
| `specs/asset_pipeline.md` | asset request → decode | §6 |
| `specs/resource_pipeline.md` | open chokepoint, caches, boot loader, terrain streaming | §6 |
| `specs/terrain-streaming.md` | streaming lifecycle | §6 |
| `structs/terrain-manager.md` | TerrainLoader + TerrainManager (34-pool / 25-ring) | §1, §6 |
| `opcodes.md` | wire frame header + opcode→handler catalogue | §7 |
| `specs/network_dispatch.md` | dispatcher, installers, lifecycle, connection-state machine | §7 |
| `specs/crypto.md` | byte cipher, LZ4, RSA/FLINT handshake | §7 |
| `specs/handlers.md` | per-handler behaviour + dispatch model | §7 |
| `specs/world_systems.md` | world-scene gameplay index | §8 |
| `specs/combat.md` | combat / melee / derived stats | §8 |
| `specs/skills.md` | skills, cooldowns, buffs | §8 |
| `specs/inventory_trade.md` | inventory / equipment / shop / trade | §8 |
| `specs/progression.md` | exp / rank / level / stat allocation | §8 |
| `specs/quests.md` | quests | §8 |
| `specs/npc_interaction.md` | NPC dialogue / services router | §8 |
| `specs/chat.md`, `specs/social.md` | chat / whisper / party / guild | §8 |
| `specs/minimap.md` | HUD radar + world map | §8 |
| `specs/camera_movement.md` | five view modes + movement + collision | §8, §10 |
| `specs/lua_scripting.md`, `specs/lua-config.md` | Lua VM + config trees | §2, §8 |
| `specs/effects.md`, `formats/effects.md`, `specs/effect-scheduling.md` | effects runtime + `.xeff` + scheduler | §9 |
| `specs/skinning.md` | CPU LBS + inverse-bind bake | §9, §10 |
| `specs/environment.md` | sky/light/fog/water | §9 |
| `specs/sound.md` | runtime audio engine | §9 |
| `formats/terrain.md`, `formats/terrain_scene.md`, `formats/terrain_layers.md` | terrain cell formats | §6, §10 |
| `formats/bgtexture_lst.md`, `formats/bindlist.md`, `formats/actormotion.md`, `formats/npc_spawns.md` | asset-chain tables | §10 |
| `formats/msg_xdb.md` / `formats/misc_data.md`, `formats/config_tables.md`, `formats/scr.md` | UI strings + config/`.scr` tables | §2, §8 |
| `structs/actor.md`, `structs/npc.md`, `structs/item.md`, `structs/skill.md`, `structs/stats.md`, `structs/spawn_descriptor.md` | runtime entity/record layouts | §3, §8 |
| `formats/game_ver.md` | client version gate | §3 |

---

## 12. Verification status

- **Re-verification basis.** This synthesis was written against the Campaign-10 re-confrontation of
  the whole `Docs/RE` base on build SHA `263bd994` using **static IDA** (control-flow + immediate
  operands), corroborated by the **shipped VFS sample** wherever a real asset witnessed a fact. No
  live network capture and no debugger session were exercised this campaign.
- **CONFIRMED (safe to implement).** The boot/bring-up sequence, the `WinMain` scene machine (8 cases
  0..7; `8` = exit sub-state), the four-phase per-frame loop and the fixed ~60 FPS software cap, the
  Diamond UI two-vtable window layout and code-baked geometry, the VFS container layout + open
  chokepoint + cache tiers + terrain singletons, the network frame header + asymmetric cipher/LZ4
  pipeline + dispatch fan-out + installer counts, the gameplay opcode *routing/sizes/offsets*, and the
  effects/skinning/environment/sound *runtime structure* are all control-flow CODE-CONFIRMED.
- **SAMPLE-VERIFIED.** The 43,347-entry VFS container, the per-area file-coverage / missing-sidecar
  patterns, the sound-table records, and the terrain/skin/spawn asset-chain hops are additionally
  byte-checked against the real archive.
- **Residual CAPTURE / DEBUGGER-PENDING (no live capture this campaign — do not hard-code):**
  - **all packet field VALUE semantics** — what each wire byte *means* (routing/sizes/offsets are
    firm; meanings are not);
  - the two trailing `u32` server scalars of the `(0,0)` key blob;
  - the connection-state code meanings (`201 / 202 / 203 / 232`);
  - the keepalive on-wire cadence (`(2,10000)@20s` vs `2/112`) and the form-arrival ordering of the
    world-entry `4/1` handler vs the major-1 billing family;
  - whether the `DISPLAY_FRAMERATE` config value is truly inert (so the 60 FPS cap is unconditional)
    and whether the in-world state-2 pass replays the full boot corpus or short-circuits caches;
  - the literal on-disk filename behind the `[OPENNING]`/`SKIP` lookup (the section/key/source field
    are confirmed; the path string is runtime-populated).
- **Conflicts:** none open. Every prior cross-spec swap (e.g. the major-3 ladder `3/4` =
  SceneEntityUpdate / `3/7` = CharManageResult / `3/14` = CharSpawnResponse, the resolved
  login-vs-create `1/6` non-collision, the u32 frame size, the dormant terrain worker, the inert
  `DISPLAY_FRAMERATE`) has been reconciled across the cited subject specs.
