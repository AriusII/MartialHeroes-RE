---
verification: confirmed (initial, IDB SHA f61f66a9, CYCLE 14 (2026-06-28))
ida_anchor: f61f66a9ae0ec1e946105b2ecff76e8930cb1d1367df64e5688a5266f5ad9963
evidence: [static-ida]                 # in-memory C++ object layout; no packed-file format
sample_verified: false                 # runtime heap layout — no VFS sample tier applies
subsystems: [render_pipeline, scene_graph]
conflicts: scene_graph.md GView section — four field interpretations corrected (§6); render_pipeline.md §13 item 5 resolved (§6)
---

# Structs: GView — Per-View Render Object

> **Clean-room spec. Neutral description only — NO decompiler pseudo-code, NO binary addresses,
> NO decompiler-generated identifiers.** Promoted from dirty-room static comprehension under EU
> Software Directive 2009/24/EC Art. 6, solely to document object layout for clean-room
> reimplementation. **All offsets are byte offsets relative to the start of the embedded
> `Diamond::GView` sub-object** (i.e., relative to host-window byte +0xE8 / +232) — they are
> struct/layout offsets, NOT memory addresses, and must never be treated as such.
> Citing engineers: `// spec: Docs/RE/structs/gview.md`.
>
> **Confidence vocabulary:** `[confirmed]` = recovered from static control-flow and operand analysis,
> corroborated across the GView constructor, destructor, the four spine render routines,
> `Diamond_GView_BeginRenderFrame`, the render-pass installer, and the frame-driver loop;
> `[static-hypothesis]` = inferred or seen only as a zero-init — not independently re-isolated,
> do not assume without further confirmation; `[debugger-pending]` = flagged for live `?ext=dbg`
> session confirmation (never `dbg_start`).

---

## 1. Object identity

`Diamond::GView` is the **per-view render object** that the frame driver walks once per frame
to execute camera setup, frustum culling, and the ordered render-pass sequence. The class is
**confirmed polymorphic** via RTTI (class name `Diamond::GView`). Its vtable exposes exactly one
virtual — the deleting destructor (§4). All per-frame work is performed by free functions
operating on GView's fields and by the richer vtables of its embedded `Diamond::GCull` sub-object
and the scene-root node; GView itself provides no per-frame virtual dispatch.

`Diamond::GView` is **not heap-standalone**. It is embedded as a sub-object inside each
scene-state window object at a fixed intra-struct byte offset (+0xE8 / +232 from the start of the
containing window). One GView exists per scene-state window:

| Window class | Role |
|---|---|
| `MainWindow` | In-world 3D view |
| `OpeningWindow` | Opening-cinematic view |
| `SelectWindow` | Character-select view |
| `LoginWindow` | Login-screen view |

Object size is approximately **308 bytes** (last owned field spans bytes +304..+307). [confirmed]

---

## 2. Ownership and lifetime

### Construction

The GView constructor zeroes all fields, writes the vtable pointer at +0, allocates the primary
`Diamond::GCull` cull pipeline into +112 (736 bytes, heap-owned), caches the global scene/post
object back-reference at +288, initialises the viewport dimensions at +28/+32 to the screen
dimensions, sets the clear-Z float at +12 to `1.0`, sets the stats report-interval threshold at
+280 to `2.0`, and timestamps the stats-interval start at +220. [confirmed]

### Activation and registration

A separate register/activate helper (invoked from each window's scene-init path) performs the
operational wiring that the constructor defers:

- Sets the default viewport (0, 0, screen-width, screen-height) via the viewport setter.
- Sets the default clear color (`0xFF000000`, opaque black) at +8.
- Installs the no-op extra callback pair at +164/+168 (fn = no-op, ctx = host window).
- Sets the overlay/UI callback context at +216 to the host window.
- Registers the GView into the frame driver's circular linked list via the driver singleton
  accessor and the list-wiring helper. [confirmed]

For the in-world view, `MainWindow_SceneInit` subsequently calls
`MainHandler_BuildInGameSceneGraph`, which runs the render-pass installer. The installer writes
the five world render-pass callback pairs into +172..+212 and caches the sub-system manager
singletons into the render-globals block (see `specs/render_pipeline.md §6`). The scene root at
+36 is set via a refcounted setter; the camera holder at +40 is set via a plain store with no
refcount. [confirmed]

### Destruction

The GView destructor enforces the following ownership at teardown:

| Field | Teardown action | Ownership |
|---|---|---|
| +36 scene root | `GObject::unref` | Owned via refcount |
| +112 primary cull pipeline | Virtual deleting destructor | Owned (heap-allocated) |
| +116 secondary cull pipeline | Virtual deleting destructor | Owned (heap-allocated, if non-null) |
| +304 FPS-counter texture | COM `Release` | Owned COM resource |
| +120 cull-scratch deque | `GBlockDeque_Destroy` | Owned embedded sub-object |
| +40 camera holder | — (not released) | **Borrowed** (non-owning pointer) |

[confirmed]

---

## 3. Field offset table

All offsets are relative to the start of the embedded `Diamond::GView` sub-object
(i.e., relative to host-window +0xE8 / +232).

| Byte offset | Size | Type | Field | Notes / evidence | Confidence |
|------------:|-----:|------|-------|------------------|------------|
| +0   | 4  | vtable ptr | vtable | Polymorphic: class `Diamond::GView` confirmed via RTTI. One virtual slot (deleting destructor, §4). | confirmed |
| +4   | 4  | —   | (reserved) | Not initialised by the constructor; no refcount use observed. GView does not appear to inherit `GObject`'s refcount. Treat as reserved/unknown. | static-hypothesis |
| +8   | 4  | D3DCOLOR (ARGB) | `clearColor` | Default `0xFF000000` (opaque black). Passed by address to the per-frame clear call. | confirmed |
| +12  | 4  | float | `clearZ` | Default `1.0`. Passed alongside `clearColor` to the clear call. | confirmed |
| +16  | 1  | bool/byte | `clearEnable` | Default `1` (auto-clear enabled). | confirmed |
| +17  | 3  | —   | padding | Alignment gap. | — |
| +20  | 4  | int  | `viewportX` | Default 0. | confirmed |
| +24  | 4  | int  | `viewportY` | Default 0. | confirmed |
| +28  | 4  | int  | `viewportW` | Default screen width. Also the viewport-width input to `GView_ComputeDetailScale`. | confirmed |
| +32  | 4  | int  | `viewportH` | Default screen height. Also the viewport-height input to `GView_ComputeDetailScale`. | confirmed |
| +36  | 4  | `GGroup*` (refcounted) | `sceneRoot` | Renderable-list head; a `GGroup`-derived node (in-world: a `GScene`). Guard condition for `Diamond_GView_BeginRenderFrame` and the cull call. Set by a refcounted setter; released via `GObject::unref` in the destructor. **Corrects `scene_graph.md`, which labels this offset a `GCamera*`.** | confirmed |
| +40  | 4  | `GViewPlatform*` (borrowed) | `cameraHolder` | The `GNode` whose world transform drives camera setup. `*(+40)` = the GNode used for `ComputeWorldTransform`; `*(+40)+80` = the `GCamera` (RTTI-cast to `GPerspectiveCamera` for FOV and aspect). Set by plain store (no refcount); not released in the destructor. | confirmed |
| +44  | 64 | float[16] | `viewMatrix` | Orthonormal inverse of the camera-holder world transform. Written by `ComputeWorldTransform`; read by `SetViewTransform` each pass. | confirmed |
| +108 | 1  | byte | `cullSelector` | 0 → use primary cull pipeline at +112; non-zero → use secondary at +116. | confirmed |
| +109 | 3  | —   | padding | Alignment gap. | — |
| +112 | 4  | `Diamond::GCull*` (owned) | `cull` | Primary cull pipeline. Heap-allocated in the constructor (736 bytes). Provides the visible draw-item list per frame via its draw-visible-items virtual slot. Deleted via virtual deleting destructor at teardown. **Corrects `scene_graph.md`, which labels this a "platform structure (736 bytes)".** | confirmed |
| +116 | 4  | `GCullPipeline*` (owned) | `cullSecondary` | Secondary cull pipeline; default null, built on demand. Carries accumulated cull/draw time statistics at its own internal offset +736. Deleted via virtual deleting destructor at teardown if non-null. **Corrects `scene_graph.md`, which labels this `GPipeline* m_pPipeline`.** | confirmed |
| +120 | ~20–24 | `GBlockDeque` (embedded) | `cullDeque` | Cull-scratch / block-deque, embedded in-place. Passed by address to `GCull_BeginView`. Destroyed via `GBlockDeque_Destroy` in the destructor. Spans approximately +120..+144; internal field layout not yet sub-fielded — defer to a dedicated cull-queue struct lane. | confirmed |
| +140 | 4  | int  | (cull param) | Initialised to `-1` by the constructor. Read individually by the cull path; likely the trailing word of the deque region. | confirmed |
| +144 | 4  | int  | (cull param) | Cull-traverse parameter read individually by the cull path. | confirmed |
| +148 | 16 | —   | (aux region) | Zero-initialised in the constructor (+148..+163). Sub-fields not yet recovered; resolve when the cull-queue struct is recovered as a dedicated lane. | static-hypothesis |
| +164 | 4  | fn ptr | (extra callback fn) | Defaulted to a no-op by the activate helper. Not observed to be invoked in the four spine render routines — likely a pre-frame or platform hook. | confirmed |
| +168 | 4  | ptr  | (extra callback ctx) | Context for the +164 callback. Defaulted to the host window by the activate helper. | confirmed |
| +172 | 4  | fn ptr | `passTransparent` | Render pass: transparent objects and particles (`RenderPass_TransparentAndParticles`). Wired by the render-pass installer. | confirmed |
| +176 | 4  | ptr  | (ctx) | Context for +172. | confirmed |
| +180 | 4  | fn ptr | `passFrameSetup` | Render pass: frame-setup / UPDATE phase. Wired by the installer. | confirmed |
| +184 | 4  | ptr  | (ctx) | Context for +180. | confirmed |
| +188 | 4  | fn ptr | `passSky` | Render pass: sky and background (`RenderPass_SkyAndBackground`). Wired by the installer. | confirmed |
| +192 | 4  | ptr  | (ctx) | Context for +188. | confirmed |
| +196 | 4  | fn ptr | `passTerrain` | Render pass: terrain and buildings (`RenderPass_WorldTerrainAndBuildings`). Wired by the installer. | confirmed |
| +200 | 4  | ptr  | (ctx) | Context for +196. Default host window from the constructor; overwritten to the shared render context by the installer. | confirmed |
| +204 | 4  | fn ptr | `passOpaqueExtras` | Render pass: opaque world extras (`RenderPass_OpaqueWorld`). Wired by the installer. | confirmed |
| +208 | 4  | ptr  | (ctx) | Context for +204. | confirmed |
| +212 | 4  | fn ptr | `passOverlayUI` | Callback: overlay / UI / HUD. Default null; wired separately from the main installer. | confirmed |
| +216 | 4  | ptr  | (ctx) | Context for +212. Defaulted to the host window by the activate helper. | confirmed |
| +220 | 8  | 2×u32 (sec/nsec) | `statsIntervalStart` | Wall-clock timestamp (seconds + nanoseconds) for the stats accumulation interval. Set by the constructor. | confirmed |
| +228 | 8  | 2×u32 | `cullBeginTime` | Wall-clock timestamp: start of the cull phase. | confirmed |
| +236 | 8  | 2×u32 | `drawBeginTime` | Wall-clock timestamp: end of cull / start of draw. | confirmed |
| +244 | 8  | 2×u32 | `drawEndTime` | Wall-clock timestamp: end of the draw phase. | confirmed |
| +256 | 8  | double | `accumCullTime` | Accumulated cull duration (seconds) for the current stats interval. | confirmed |
| +264 | 8  | double | `accumDrawTime` | Accumulated draw duration (seconds) for the current stats interval. | confirmed |
| +272 | 4  | uint | `frameCounter` | Count of drawn frames; incremented each frame by `Renderer_SetupCameraAndFrustum`. | confirmed |
| +276 | 4  | —   | padding | 8-byte alignment for the following double. | — |
| +280 | 8  | double | `statsIntervalSec` | Stats report-interval threshold in seconds. Default `2.0`. | confirmed |
| +288 | 4  | ptr (borrowed) | `postObjectBackRef` | Back-reference to the global scene/post object. The route to the D3D device, RT0 surface, render-to-surface helper, composite textures, and the offscreen-vs-direct path flag. Not released in the destructor (borrowed pointer). See `specs/render_pipeline.md §8`. | confirmed |
| +292 | 1  | bool/byte | `fpsEnable` | FPS-counter display enable flag. Initialised by the constructor. | confirmed |
| +293 | 3  | —   | padding | Alignment gap. | — |
| +296 | 8  | double | `fpsAverage` | Computed FPS average (frames per second). Updated on each stats-interval boundary. | confirmed |
| +304 | 4  | `IDirect3DTexture9*` (COM, owned) | `fpsCounterTexture` | FPS-counter digit texture. Loaded lazily from `data/ui/counter.dds` on the first FPS draw frame. Released via COM `Release` in the destructor. **Corrects `scene_graph.md`, which labels +0x130 as "COM IUnknown* (Direct3D Device)".** | confirmed |

**Total size: approximately 308 bytes** (last field spans +304..+307; true boundary subject to alignment rounding). [confirmed]

### Quick-reference

- **Vtable / RTTI:** +0 (one virtual: deleting destructor; class `Diamond::GView`).
- **Clear state:** color +8 (default opaque black), Z +12 (default 1.0), enable +16.
- **Viewport:** X/Y/W/H at +20/+24/+28/+32.
- **Scene root (refcounted GGroup/GScene):** +36. Camera holder (borrowed GViewPlatform): +40.
- **View matrix (float[16]):** +44..+107.
- **Cull selector:** +108. Primary cull pipeline (owned GCull, 736 bytes): +112. Secondary: +116.
- **Cull-scratch deque (embedded):** +120..~+144.
- **Extra callback pair (no-op default):** fn +164, ctx +168.
- **Five render-pass callback pairs:** +172..+216 (transparent, frame-setup, sky, terrain, overlay).
- **Timing / stats:** timestamps +220..+251, accumulated times +256/+264, frame counter +272, interval threshold +280.
- **Global post-object back-ref (borrowed):** +288.
- **FPS counter:** enable +292, average (double) +296, digit texture (COM) +304.

---

## 4. Vtable layout

`Diamond::GView` is confirmed polymorphic via RTTI. The vtable contains exactly **one** virtual
function slot:

| Slot | Role |
|---|---|
| [0] | Scalar/vector deleting destructor. Unrefs the scene root (+36), deletes the primary and secondary cull pipelines (+112/+116), COM-releases the FPS-counter texture (+304), and destroys the cull-scratch deque (+120). |

All per-frame work — camera/frustum setup, cull dispatch, draw-pass sequencing — is performed by
free functions operating on GView's fields and by the richer vtables of the embedded
`Diamond::GCull` sub-object and the scene-root node. GView itself provides no per-frame virtuals.

> **Adjacent vtables note.** Immediately following the GView vtable in the read-only data segment
> are the vtables of the `GCullPipeline` and `Diamond::GCull` classes (the cull-pipeline family).
> Those adjacent entries are independent class vtables, NOT additional GView virtual slots.
> The `Diamond::GCull` vtable (the second adjacent table) has its own deleting destructor at slot 0
> and carries the "draw visible items" dispatch at slot 1.

---

## 5. Frame-driver link node (GViewListNode)

`Diamond::GView` carries **no** embedded next/prev pointer. The frame driver chains multiple views
through a **separate 12-byte intrusive link node**, allocated by the frame driver and wired into a
circular doubly-linked list. The driver singleton's list-head sentinel is the anchor; the driver's
per-frame loop (`Engine_DeviceStepAndPresent`) walks each node, reads the GView pointer at node +8,
and dispatches camera setup + draw on the referenced GView.

| Node offset | Size | Type | Role |
|---:|---:|---|---|
| +0 | 4 | node ptr | Next node pointer. The frame-driver loop advances on this; wraps to the list-head sentinel. |
| +4 | 4 | node ptr | Prev / maintenance pointer (doubly-linked list upkeep). |
| +8 | 4 | `Diamond::GView*` | The registered GView object driven by this node. |

The count of nodes in the driver's list at runtime equals the number of scene-state windows
currently registered with the frame driver. [debugger-pending — confirm the in-world count vs
character-select count and whether an inset preview camera adds a node or uses a separate mechanism.]

---

## 6. Reconciliation with existing specs

### 6.1 scene_graph.md "GView" section

The `scene_graph.md` GView entry describes the same class (vtable identity confirmed). Four of its
field interpretations are incorrect and the reported size is short:

| scene_graph.md claim | Correct interpretation |
|---|---|
| +0x24 = `GCamera* m_pCamera` | +0x24 (+36) is the **scene root** (`GGroup*`/`GScene*`), set by a refcounted setter. The `GCamera` is reached indirectly via the camera holder at +0x28 (+40), then `*(cameraHolder)+80`. |
| +0x70 = "platform structure (736 bytes)" | +0x70 (+112) is the **primary `Diamond::GCull` cull pipeline** (736 bytes, heap-allocated, owned). Not a "platform" struct. |
| +0x74 = `GPipeline* m_pPipeline` | +0x74 (+116) is the **secondary cull pipeline** (`GCullPipeline`-family; default null; carries `GStats` at its own internal +736). |
| +0x130 = "COM IUnknown* (Direct3D Device)" | +0x130 (+304) is the **FPS-counter digit texture** (`IDirect3DTexture9*`, loaded from `data/ui/counter.dds`). The D3D device is on the global scene/post object, reached via GView +288. |
| Size "296+ bytes" | Actual size is approximately **308 bytes**. |

### 6.2 render_pipeline.md §7 "GView Render-View Object Layout"

The §7 table is correct for the fields it covers. This spec extends it with the following fields not
yet recorded there:

- +0: vtable; confirms polymorphism and class identity (`Diamond::GView`) — resolves §13 item 5.
- +16: `clearEnable` byte (the §7 table begins at +8).
- +112/+116: concrete types — `Diamond::GCull` (736 bytes, owned) / `GCullPipeline` (secondary).
- +120: embedded `GBlockDeque` cull-scratch; extent approximately +120..+144.
- +164/+168: extra callback fn/ctx pair (no-op default, purpose under investigation).
- +304: FPS-counter digit texture (`IDirect3DTexture9*`, lazily loaded, COM-owned).

> **Resolved:** render_pipeline.md §13 item 5 asked whether the render-view object is the same
> class as — or a sibling of — `scene_graph.md`'s GView. **It is the same class: `Diamond::GView`**,
> confirmed via RTTI. The register/activate helper is the operational-wiring step; the real
> constructor precedes it and writes the vtable at +0.

The pass-fn table in §7 begins at +172. Offsets +164/+168 (extra callback pair) were not yet
recorded there. The §7 field at +36 labelled "GView sub-object pointer" is the scene root
(`GGroup*`/`GScene*`) — that provisional label reflected the field's role as an argument to
`Diamond_GView_BeginRenderFrame` before the class identity was established.

---

## 7. Open items and pending confirmation

The following are `[static-hypothesis]` or flagged `[debugger-pending]` for live `?ext=dbg`
session confirmation (never `dbg_start`):

- **+4 (reserved word):** purpose unknown; no write or refcount use observed in the constructor.
- **+148..+163 (aux zero-init region):** sub-fields not yet recovered; resolve with cull-queue
  struct analysis.
- **GBlockDeque internal layout (+120..~+144):** exact field sub-structure not recovered this pass;
  resolve when `GBlockDeque` is documented as a dedicated struct lane.
- **+164/+168 extra callback:** defaulted to no-op + host-window; no call site observed in the four
  spine routines. Confirm whether any other path invokes this pair (possibly a pre-frame or
  platform hook).
- **GViewListNode count at runtime:** confirm how many nodes are in the driver's circular list
  in-world vs at character-select.
- **Live GView read candidates (`?ext=dbg`, host-window +0xE8):** confirm +8 color byte order,
  +16 clear-enable flag semantics, the exact GBlockDeque extent, and that +304 is an
  `IDirect3DTexture9*` (COM `Release` at vtable slot 2).

---

## 8. Cross-references

- Render-pass frame sequence, spine routines, and installer: `specs/render_pipeline.md`.
- Scene graph class hierarchy (`GGroup`, `GScene`, `GViewPlatform`, `GCamera`, `GFrustum`,
  `GCullPipeline`): `specs/scene_graph.md`.
- Terrain streaming and cell management: `specs/terrain-streaming.md`.
- Runtime singletons (global scene/post object, frame driver): `structs/runtime_singletons.md`.
- FPS-counter digit asset (`data/ui/counter.dds`): `formats/texture.md`.
- Glossary: `Docs/RE/names.yaml`.
- Provenance: `Docs/RE/journal.md`.
