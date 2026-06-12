---
status: hypothesis
sample_verified: partial   # sound tables, toonramp LUT, npc.arr, .sod/.ted bytes verified; runtime logic IDA-derived
subsystems: [sound_runtime, ui_system, render_pipeline, camera_constants, movement_constants, quest_data]
networked: partial         # sound/UI/render/camera are client-only; movement uses 2/13 & 5/13; quest uses 2/28, 5/68, 5/73 (cataloged elsewhere)
encoding_note: Korean in-game/config/dialog text is CP949 (legacy MS949 code page), not UTF-8.
---

# Client Runtime Subsystems — Clean-Room Specification

> Neutral, rewritten behavioural specification. No legacy symbols, no addresses, no
> pseudo-code, no decompiler identifiers. Promoted from dirty-room analyst notes by the
> spec-author: every behaviour and constant below is re-expressed in this author's own words
> and tables. It documents the **presentation-and-client-runtime** side of the legacy client
> (sound, UI widget tree + Lua binding, the "Diamond" render pipeline, the five camera modes,
> movement/collision tuning, and the quest system) so the .NET core (`Client.Domain` /
> `Client.Application` / `Client.Infrastructure`) and the Godot presentation layer can be
> reimplemented from scratch.
>
> **Cross-spec ownership.** Where a topic already has its own committed spec, that spec is the
> authority and this file only summarises and cross-links:
> - Camera view-mode behaviour and movement pipeline: `specs/camera_movement.md`.
> - Lua VM version and `.lua` data-tree model: `specs/lua_scripting.md`.
> - Per-frame loop ordering: `specs/game_loop.md`.
> - Sound on-disk table format: `formats/sound_tables.md`.
> - Shader file format: `formats/shaders.md`; terrain on-disk: `formats/terrain.md`.
> - Quest packet shapes (2/28, 5/68, 5/73): `opcodes.md` + `packets/*.yaml` + `specs/quests.md`.
>
> This spec adds the *runtime engine behaviour and pinned constants* that those specs reference
> but do not themselves enumerate. Packet byte layouts are never re-derived here.

---

## Status and verification banner

Per-section confidence model (used inline below):

- **(confirmed)** — re-derived from behaviour with corroborating static evidence; safe to implement.
- **(sample-verified)** — additionally cross-checked against real shipped asset bytes (sound
  tables, `toonramp.bmp`, `.sod`/`.ted`, `npc.arr`); the strongest tier.
- **(plausible)** — single-source behavioural inference; implement but keep tunable / behind a flag.
- **(UNVERIFIED)** — hypothesis only; do **not** hard-code. Re-listed in each section's open list.

All numeric constants below were lifted from the legacy binary's literal pool / immediate operands.
They are reliable as *values present in the code* but, absent a live capture, treat the feel-governing
ones (volume curve, smoothing, send cadence) as a faithful starting tune to expose as configuration.

---

# 1. Sound system (DirectSound runtime)

The audio engine is a DirectSound (DirectSound 8 era) wrapper with a 2D/3D split, OGG-Vorbis decoding,
a per-map five-table ambient driver, an actor-event SFX router, and a background worker thread for
streaming background music. The on-disk table format is owned by `formats/sound_tables.md`; this section
specifies the **runtime engine** that consumes it.

## 1.1 Device and buffer setup — (confirmed)

At startup the engine creates a DirectSound device, sets cooperative level to PRIORITY, creates a primary
buffer with 3D + volume control, and acquires a global 3D listener interface used for spatialisation.
A single global **master audio-enabled flag** gates the whole subsystem: when cleared, the create-sound
path and the worker thread both early-out, cheaply silencing all audio.

Five PCM `WAVEFORMATEX` templates are pre-built once and selected by channel count + sample rate:

| Role | Channels | Sample rate | Bits | Notes |
|---|---:|---:|---:|---|
| Stereo 44.1 kHz | 2 | 44100 | 16 | 2D alternate |
| Stereo 22.05 kHz | 2 | 22050 | 16 | **2D default** |
| Mono 44.1 kHz | 1 | 44100 | 16 | 3D alternate |
| Mono 22.05 kHz | 1 | 22050 | 16 | **3D default** |

**Hard codec rule (confirmed):** **3D sounds must be MONO; 2D sounds must be STEREO.** A 3D clip with the
wrong channel count is rejected at load; a 2D clip likewise. Implementations should enforce the same rule.

## 1.2 Sound object and the create path — (confirmed)

A sound is created from `(soundId, typeFlags, dirPrefix)`. The type-flags byte is a small bitfield:

| typeFlags | Meaning | Asset directory |
|---:|---|---|
| `1` | OGG-backed **2D** sound | `data/sound/2d/` |
| `3` | OGG-backed **3D** sound | `data/sound/3d/` |

Bit 0 ("is OGG-backed") is always set; bit 1 selects 3D. A second, list-based selector chooses 2D vs 3D by
a category byte (`category < 5 ⇒ 2D`, else 3D).

The on-disk filename is always built as `<dirPrefix><id>.ogg` — the id is formatted as plain decimal and the
`.ogg` extension is **unconditional**. (A few `.wav` assets exist in the 3D directory; they are reached only by
the separate list path, never by the table dispatch.) Loading prefers the mounted `.pak` VFS (in-memory
decode via Vorbis file callbacks) and falls back to a plain on-disk `fopen` in dev/editor environments.

## 1.3 Decode buffer and the streaming sentinel — (confirmed)

Decoding fills a single shared **512 KiB scratch buffer** (`0x80000` bytes — roughly 11.9 s of mono
22.05 kHz 16-bit PCM). This is a hard PCM cap with two outcomes:

- If a clip **fits** within 512 KiB, it is copied once into a fixed-size DirectSound buffer and the OGG handle
  is closed (a one-shot sound).
- If a clip **fills** the entire scratch (i.e. is longer than the cap):
  - a **3D** sound is **rejected** (3D point sounds must be short);
  - a **2D** sound switches to **streaming**: a **1 MiB ring buffer** (`0x100000`) is allocated, the OGG
    handle stays open, and the worker thread refills it incrementally. This is the background-music mechanism.

## 1.4 The five per-map tables and the ambient driver — (sample-verified)

When a map area becomes active, the loader opens five sibling files and reads exactly **12 288 bytes**
(256 entries × 48 bytes) from each, ignoring any trailing editor bytes. Extension → runtime role:

| File extension | Runtime table role | Indexed by |
|---|---|---|
| `.run` | footstep set, **running** | actor visual's run-footstep id |
| `.wlk` | footstep set, **walking** | actor visual's walk-footstep id |
| `.bgm` | background music zone | terrain mud-cell byte +0x02 |
| `.bge` | looped ambient (two slots) | terrain mud-cell bytes +0x03, +0x04 |
| `.eff` | 3D triggered point sounds (three slots) | terrain mud-cell bytes +0x05, +0x06, +0x07 |

A per-frame ambient driver reads the local player's world position, looks up the current terrain mud-cell,
and starts/stops BGM, looped ambience, and 3D point sounds when a cell byte changes (index `0` is the null
sentinel and is skipped). It caches the last-played index per slot so a sound restarts only on actual change.

Pinned ambient constants — (confirmed):

| Constant | Value | Role |
|---|---:|---|
| Ambient re-evaluation cadence | **600 000 ms (10 min)** | full forced re-pick interval (e.g. day/night / periodic) |
| Game-hour divisor | **3600** | hour-of-day = `gameSeconds / 3600`; each entry carries a 24-byte hour schedule |
| 3D ambient volume/distance scale | **× 0.7** | table volume scaled before the 3D min-distance / rolloff |
| Indoor/instanced BGM override id | **863500002** | forces this BGM when the local player's indoor flag is set |
| Music-slider-exempt cue ids | **861010109, 861010110** | played at full volume when a global toggle is set |

For a `.eff` (3D ambient) entry, the play position is `(entry.X, playerY, entry.Z)` — **X/Z from the table,
Y taken from the player**. Hour-of-day gating: an entry's 24-byte hour schedule is indexed by the current
game hour; a zero byte mutes the entry that hour.

## 1.5 Actor-event SFX router — (confirmed)

A separate router handles 3D SFX for actor / animation / network events (spawn, death, attack, level-up,
item-use, combat-effect spawn, stat update, footsteps). Behaviour:

- **Audibility cull:** for the local player it uses a fixed audible radius of **200.0 units**; for a remote
  actor it computes a cutoff from the actor's visual max-distance scaled by a context factor and does a
  **squared-distance cull** (drop if beyond the scaled radius).
- **Separate volume slots** for "my" sounds vs "others'" sounds.
- **Kind dispatch:** kinds 5/10/11 → a directional/voice pool (max 3 concurrent per key); kinds 7/8/9 →
  a footstep pool; other kinds rejected.

**Footstep selection (confirmed):** when an animation cycle wraps, the local player's footstep id is read
**from the per-character actor-visual** (separate walk-id and run-id fields), not from a mud-cell byte —
running fires kind 8 with the run id, walking fires kind 7 with the walk id. (This is why the sampled
`.wlk`/`.run` tables are empty: those sets are addressed per actor-visual, not per map cell here.)

## 1.6 Volume curve — (confirmed, endpoints certain)

Linear amplitude `X ∈ [0,1]` is mapped to a DirectSound millibel attenuation:

- `X == 0` → **−10000** (full silence; DirectSound minimum).
- otherwise → a steep perceptual taper using a nested-logarithm form, `mB ≈ int( log( log(X) · 3000 + 0.5 ) )`.

The endpoints (`0 → −10000`) are certain; the exact log base/units of the nested form are not byte-verified
(see open list). Implementations should reproduce the silence endpoint exactly and approximate the taper.

## 1.7 Asynchronous worker thread — (confirmed)

A background worker drains a mutex-guarded event queue; each event's first byte is an opcode:

| Op | Name | Action |
|---:|---|---|
| 1 | LOAD | load the sound (set dead flag on failure) |
| 2 | DELETE | release the sound |
| 3 | PLAY | play (with loop flag) |
| 4 | PLAY2D | set volume, then play |
| 5 | PLAY3D | set position + min-distance + volume, then play |
| 6 | STOP | immediate stop |
| 7 | SET_VOLUME | set volume only |
| 8 | RESET | reset playback position to 0 |
| 9 | CHANGE_STREAM | swap the streaming OGG (background-music track change) |

The worker ticks: every iteration where **more than 200 ms** elapsed it refills active streaming ring buffers,
then sleeps **100 ms** — so streaming/BGM refill runs roughly 5×/second. There are therefore **two playback
routes**: a synchronous create/play used by the ambient driver and the actor-event router, and this asynchronous
event-queue route used for streaming BGM and any caller that posts an event.

## 1.8 Sample-byte evidence — (sample-verified)

- Map-000 sound tables (`.wlk/.run/.bge` empty; `.bgm` has one entry at index 1: id 920100200, weight 1.0,
  hour schedule all-active) match the format spec exactly.
- The 3D-directory OGG samples are Vorbis **mono / 22 050 Hz**; the lone `.wav` sample is PCM **mono /
  22 050 Hz / 16-bit** — all consistent with the mono-3D rule.

## 1.9 Sound — open items

1. Exact units of the nested-log volume curve (endpoints certain; taper approximate).
2. Entry field at +0x24 (editor-fill in samples) — unused by any runtime path.
3. Entry `weight` field (+0x1C, always 1.0 in samples) — not consumed by any traced playback path.
4. 2D-directory stereo presence (runtime requires stereo; no 2D-dir sample exists to confirm).
5. How a table id whose asset is a `.wav` is played (table path always requests `.ogg`).
6. Which file format populates the actor-visual walk/run footstep-id fields (terrain/visual concern).
7. Exact voice-pool / footstep-pool concurrency sizing beyond the 3-concurrent voice cap.

---

# 2. UI widget tree and `.lua` binding

The UI is a retained-mode, single-inheritance widget tree branded "Diamond". A common base component carries
layout, flags, a transform matrix, and a timer; panels add a children vector; windows add a command-handler,
an auxiliary view, and a texture list. Layout and string content come from `.lua` config files (see
`specs/lua_scripting.md` for the VM and data-tree model).

## 2.1 Widget class hierarchy — (confirmed)

```
Component (base)
├── Panel
│   ├── Window            ── concrete: LoginWindow, SelectWindow, MainWindow, OpeningWindow, …
│   └── ScrollEx
├── ComponentEx           ── lightweight widget with an explicit float screen-rect for hit-testing
├── Button
│   └── CheckBox
├── Label
│   ├── ShortLabel
│   └── Labels
├── Textbox
├── List
├── Scroll
└── Canvas3D
```

Non-rendered helpers also exist: a string filter/validator and a chat-specific filter subclass.

## 2.2 Base component field layout — (confirmed)

Field offsets within the base component, for an implementer building an equivalent struct. (These are *engine
struct* offsets, not wire offsets — they describe the in-memory widget, and may be modelled as ordinary class
fields in the reimplementation; the table is the authoritative semantic map.)

| Offset | Size | Type | Role |
|---|---:|---|---|
| +0x00 | 4 | ptr | vtable / dispatch pointer |
| +0x04 | 4 | u32 | alpha / opacity (init 255); fades by ±64 per draw tick on show/hide |
| +0x08 | 4 | u32 | capability-flag bitmask (bit0 always set; bit2 set for panels; bit13 set for windows) |
| +0x0C | 4 | i32 | widget / action id (init −1) |
| +0x10 | 4 | i32 | panel-type id used by child lookup (init −1; set when added to a parent) |
| +0x14 / +0x18 | 4 / 4 | i32 | local X / local Y (pixels, relative to parent) |
| +0x1C / +0x20 | 4 / 4 | i32 | width / height (pixels) |
| +0x24 / +0x28 | 4 / 4 | i32 | source/default width / height (copies of width/height) |
| +0x2C / +0x30 | 4 / 4 | i32 | world (screen-space) X / Y, computed by the transform pass |
| +0x34 / +0x38 | 4 / 4 | i32 | world right / bottom edge (world + size); render-queue bounds |
| +0x3C / +0x40 | 4 / 4 | i32 | local right / bottom edge (local + size) |
| +0x44 | 64 | float[16] | 4×4 translation matrix built by the transform pass |
| +0x84 | 4 | ptr | parent pointer (NULL = root) |
| +0x88 | 1 | u8 | **hovered** flag (cursor-over result of hit-test) |
| +0x89 | 1 | u8 | hover-enter edge flag (1 on the first frame the cursor enters) |
| +0x8A | 1 | u8 | **enabled** flag (init 1) |
| +0x8B | 1 | u8 | **focused** flag (IME uses it to know the active textbox) |
| +0x8C | 1 | u8 | **visible** flag (init 1) — draw dispatch checks this `== 1` before drawing a child |
| +0x8D | 1 | u8 | pressed / drag-state flag |
| +0x90 | 4 | ptr | texture/sprite handle or action-callback context |
| +0x95 | 1 | u8 | timer-active flag |
| +0x98 | 4 | u32 | timer expiry (ms timestamp) |
| +0x9C | 4 | u32 | timer interval (default **3000 ms**) |
| +0xA0 | 4 | ptr | timer callback context |

> **Flag-byte correction (confirmed).** The four bytes at +0x88..+0x8D are a contiguous group. The
> authoritative meaning is: **+0x88 = hovered, +0x8A = enabled, +0x8B = focused, +0x8C = visible**. An earlier
> input-pipeline note inverted visible/enabled — this spec's ordering is the verified one (the draw dispatch
> proves `+0x8C == 1` means visible; the hit-test proves `+0x88` is the hovered result).

## 2.3 Panel and window additions — (confirmed)

**Panel** adds an MSVC-style child vector (begin/last/end pointers at +0xA4/+0xA8/+0xAC), a possible fourth
vector field at +0xB0 (UNVERIFIED), an **active-child index** at +0xB4 (init −1; used for tab/focus switching),
and a panel-subtype flag byte at +0xB8 (UNVERIFIED). Adding a child sets the child's parent pointer and panel-id
and pushes it into the parent's vector.

**Window** adds, after the panel fields: an embedded command-handler object (action-id callback dispatch,
~ +0xBC), an auxiliary view (~ +0xE8), and an embedded texture list (~ +0x220). Construction marks the
window-type capability bit (bit13).

**ComponentEx** adds an explicit float screen-rect for accurate hit-testing: X/Y scale factors and screen
X/Y origin (world left/top edges as floats). Its hit-test uses `screenX ≤ cursorX < screenX + width·scaleX`
(and the Y analogue), writing the result into the hovered byte.

Approximate struct sizes (UNVERIFIED as exact totals; natural 4-byte alignment, **no Pack=1 evidence** — these
are heap C++ objects, unlike the packed wire structs): component ≈ 164 B, panel ≈ 188 B, componentEx ≈ 180 B,
window ≥ ~0x240 B.

## 2.4 Draw dispatch, hit-test, focus, transform — (confirmed)

- **Draw dispatch (windows):** iterate the child vector from begin to end; for each child whose **visible**
  byte (+0x8C) is 1, call its transform-update then its draw. (Invisible children are skipped, not drawn dim.)
- **Hit-test:** two implementations. The integer base hit-test bounds-checks world position + integer size; the
  float ComponentEx hit-test bounds-checks the float screen-rect with scale. Both write the hovered byte and,
  on enter/exit, fire hover-enter / hover-exit callbacks. The top-level UI hit-test walks the tree from the root
  window and returns the topmost hit, **consuming mouse events before world-entity picking** (UI first, world
  second).
- **Focus** is tracked at two levels: an **IME/textbox focus** (sets the widget's focused byte and registers the
  widget with the IME context for composition input) and a **global click-focus** pointer (set on mouse-down for
  an enabled widget, cleared and a mouse-up event enqueued on release). The panel-level active-child index
  selects which child tab is current.
- **Transform:** world position = local position + parent world position; the result is stored and a 4×4
  translation matrix is written for the D3D render pass.

## 2.5 Lua and INI configuration binding — (confirmed; content UNVERIFIED)

- **`data/script/uiconfig.lua`** is loaded when the login scene is built. The build cascade creates roughly
  **340 widgets**. The only confirmed Lua global key is `NEW_SERVER_INDEX` (boolean, toggles server-list mode).
  Texture references seen in the same path include `data/ui/login_slice1.dds`, `data/ui/loginwindow.dds`,
  `data/ui/InventWindow.dds`, `data/ui/loginwindow_02.dds`.
- **`data/script/display.lua`** is a **renderer** config (float globals for glow range, framerate, character
  bloom/brightness multipliers) — not widget layout. See §3.
- **Chat-window config keys** read elsewhere: `CHAT_WINDOW_FONT_SIZE`, `CHAT_WINDOW_SIZE`,
  `CHAT_WINDOW_POS_X`, `CHAT_WINDOW_POS_Y`.
- **Panel-position persistence** is stored in an INI file (exact path UNVERIFIED). The INI section name is
  `"<billingStateIndex>_<spawnDesc>_PANELPOS"`; per panel index 0..8 the keys `PANEL_<i>_X` / `PANEL_<i>_Y`
  give saved positions. If a saved coordinate is −1, or if `SCREEN_WIDTH` / screen-height changed, the panel
  resets to its default position. Additional keys: `LINK_VERTICAL` (toolbar layout toggle), `MENU_OPEN`
  (initial open/close state).
- **Action-id → button mapping** (login window): roughly 200–201 = login OK, 202 = login + enter-load,
  203 = cancel, 204/207 = help, 205 = help2, 206/16 = server-list, 209/220 = quit, 221..245 = option tabs.
  Each action fires an SFX and mutates the window's state-machine variable.

## 2.6 UI — open items

1. Full bit definitions of the capability-flag mask beyond bits 0/2/13.
2. Whether the +0x24/+0x28 "source size" copies serve a distinct purpose (animation/clip rect).
3. The panel +0xB0 word and the +0xB8 panel-type flag values/roles.
4. Total window footprint (depends on the embedded texture-list layout at ~+0x220).
5. Scroll-widget internal pointer roles (up/down/thumb).
6. The actual `uiconfig.lua` / `tutor.lua` content — not in the binary; only `NEW_SERVER_INDEX` confirmed.
7. Exact panel-position INI file path.

---

# 3. Diamond render pipeline and shaders

The renderer is an in-house Direct3D 9 scene-graph engine. The headline feature is a **toggleable
four-phase render-to-texture toon + bloom post-process**; when disabled, a plain fixed-function
back-buffer path runs. Per-frame loop ordering is owned by `specs/game_loop.md`; shader file format by
`formats/shaders.md`. This section pins the runtime pipeline behaviour and constants.

## 3.1 Device, present mode, and recovery — (confirmed)

- API: **Direct3D 9** (`d3d9.dll` + `d3dx9_42.dll`), created at SDK version 32, HAL device with
  **hardware vertex processing + multithreaded** behaviour flags.
- Back-buffer format default **X8R8G8B8**; depth/stencil chosen from the fallback chain
  **D32 → D24X8 → D24S8 → D16**.
- **Presentation interval = IMMEDIATE → NO VSYNC.** The render loop is genuinely uncapped (this resolves the
  game-loop "is present a de-facto FPS cap" question: it is not).
- **Device-lost recovery:** on `DEVICELOST`, sleep ~1000 ms and retry; on `DEVICENOTRESET`, run a three-step
  recovery (release default-pool resources → reset device with saved present params → recreate default-pool
  resources, e.g. fonts).

The toon/bloom path is selected **at device-create time**: it turns on only if the toon-shading load succeeded
**and** a graphics-option config flag is set. So it is a user-togglable graphics option.

## 3.2 Plain path (toon/bloom OFF) — (confirmed)

Get the back-buffer, set the viewport from the scene rect, clear TARGET|ZBUFFER, begin scene, activate the
camera and set the VIEW transform, run optional pre-draw callbacks, configure stage 0/1/2 texture-stage state
(modulate/select chains) and linear samplers, draw the scene root, end scene, then draw the HUD/FPS overlay.
**No shaders** — pure fixed-function lighting + multitexture.

## 3.3 Toon + bloom path (ON) — the four phases — (confirmed)

Three render-target textures (TEX0 cel/scene, TEX1 edge/glow, TEX2 bloom-small) and three render-to-surface
helpers are created up front, plus a `toonramp.bmp` lookup texture (§3.6). The frame is:

- **Phase A — scene → TEX0.** Render the 3D scene into the offscreen colour target: clear, activate camera,
  set VIEW + identity WORLD, run the pre-draw callbacks, then invoke the scene-root draw callback (the
  scene-graph cull walk that draws world → actors → effects, §3.5). Resolve into TEX0.
- **Phase B — glow/edge extract + downsample → TEX1.** Full-screen ortho quad with a half-texel UV offset;
  Z/lighting/alpha-blend disabled; stage0 = SELECTARG1/TEXTURE; linear samplers; sample TEX0. **Fixed-function,
  no pixel shader bound.** Output TEX1.
- **Phase C — bloom blur/downsample → TEX2.** Smaller ortho sized by the bloom **downscale divisors**; binds a
  **config-supplied blur/glow pixel shader**; sample TEX1; output TEX2.
- **Phase D — composite TEX1+TEX2 over TEX0 with `finaldx8.psh`, then forward overlays.** Two-texture quad;
  upload two pixel-shader constants — **c0 = edge weight, c1 = bloom weight** (broadcast scalars) — matching the
  composite formula `saturate(2·edge·c0 + bloom·c1)`; bind `finaldx8.psh`; draw. Then re-establish forward 3D
  state (the toon uniforms, §3.6) and run the **post-scene transparent-overlay callback** (alpha-blended
  geometry — water/glass/decals/some FX — that must draw after the opaque composite).

**Back-buffer present stage:** clear the real back-buffer; blit the finished offscreen frame as a full-screen
quad (opaque copy); then draw the **2D UI / HUD callback LAST** in screen-ortho space; optionally draw the FPS
overlay; end scene; present.

**Net top-level draw order (confirmed):**
`[offscreen] clear → 3D scene (terrain/world → actors → effects) → glow extract → bloom blur → finaldx8
composite → post-scene transparent overlay`, then
`[back-buffer] clear → blit composited frame → UI/HUD → FPS → present`.

## 3.4 Pinned render constants — (confirmed)

| Constant | Value | Role |
|---|---:|---|
| Back-buffer format | X8R8G8B8 | default colour format |
| Depth fallback chain | D32 → D24X8 → D24S8 → D16 | depth/stencil selection |
| Present interval | IMMEDIATE (no vsync) | uncapped loop |
| Skinned-actor vertex stride | **32 bytes** | XYZ + normal + UV |
| Toonramp LUT | **256 × 1, 24-bpp, uncompressed** | 1D cel-quantisation gradient, dark→bright |
| Terrain FVF | `0x152` | animated multi-texture terrain vertex format |
| Terrain water animation gate | **≥ 50 ms** timer | static vs UV-scrolling water variant |
| Composite weights | c0 = edge, c1 = bloom | `finaldx8.psh` scalars |
| Default ("missing") texture | 2×2, bytes per texel ~ (0x78,0x78,0x78,0x50) | grey/half-alpha placeholder when an actor has no texture |
| Render-target fallback dims | 1024 × 1024 | when back-buffer dims unavailable |

## 3.5 Scene-graph traversal and drawable layers — (confirmed)

The in-scene draw order is **not** a hard-coded "terrain then actors then FX" sequence; it is the order of nodes
in the visible cull set, grouped by the render state each node installs. The scene graph is authored so opaque
world geometry draws before alpha-blended FX. Distinct per-class render routines implement each layer:

| Layer | Behaviour (confirmed values) |
|---|---|
| Terrain (animated, multi-texture) | sets FVF `0x152`; walks visible cells, 4 corner sub-passes per cell; dest-blend toggles INVSRCALPHA↔ONE between passes; static vs ≥50 ms UV-scroll (animated water); fixed-function |
| Solid batched geometry | opaque mesh batch with explicit src/dest blend |
| Skinned actors (characters) | **CPU-side skinning** (the dynamic vertex buffer is rebuilt before draw); uploads **one composite WVP matrix** to vertex-shader constant c0; stride 32; binds the toon shader pair. **Not** a bone-matrix palette |
| Billboards / sprites | camera-facing quads |
| Particle effects | default blend SRCALPHA/INVSRCALPHA, additive mode SRCALPHA/ONE; **no per-particle back-to-front sort** (relies on alpha smear) |
| UI / HUD | drawn last, on the back-buffer, in screen-ortho space, after compositing (§3.3) |

## 3.6 Cel-shading bind contract — (confirmed)

Per skinned actor, after CPU skinning and WVP upload: bind the **base albedo texture to stage 0** and the
**toonramp LUT to stage 1**, set the toon vertex shader, and pick the pixel shader — a **normal cel tone** or a
**stealth/invisible variant** by a boolean. Once per frame the toon pass uploads **7 vec4 lighting/material
uniforms to vertex-shader registers c4..c10**: two model-space light directions (c4/c5), two light colours
(c6/c7), a positive clamp scalar (c8.x), a luminance-weight vector (c9), and material diffuse (c10). The vertex
shader transforms the already-skinned vertex by WVP and emits an N·L luminance scalar on an output texcoord; the
pixel shader uses that scalar to sample the 256×1 toonramp LUT, producing quantised cel bands.

## 3.7 Sample-byte evidence — (sample-verified)

`toonramp.bmp` is a real 824-byte file: a **256 × 1, 24-bpp, uncompressed RGB** ramp running from a dark band
to a bright band — exactly the 1D LUT the luminance scalar indexes. The toon vertex shader and the glow-falloff
"power" pixel shaders were re-read and are consistent with `formats/shaders.md`.

## 3.8 Render — open items

1. The config-supplied blur/glow pixel-shader filename used in Phase C.
2. Whether the shipped `power*` glow shaders are used by an alternate/legacy or quality-gated glow path
   (the live Phase B is fixed-function).
3. Exact render-state ids passed in the full-screen-quad passes (likely a colour-write/SRGB combination).
4. Exact authored intra-scene draw order vs purely render-state/node-order separation.
5. Precise vertex-declaration element table (the trailing N·L luminance channel).
6. The composite/cel pixel-shader byte math (`finaldx8.psh` / `dotoonshading*.psh` not in the sample set).
7. Runtime values of the bloom downscale divisors.

---

# 4. Camera constants (five view modes)

The camera is **purely client-side** — no camera state crosses the wire. The **camera object** owns
projection/FOV and produces the cull frustum; a family of per-mode **manipulators** position and orient it each
frame. View-mode behaviour and the input pipeline are specified in `specs/camera_movement.md`; this section pins
the exact per-mode constants. **Correction carried into this spec:** the camera is a **fixed-radius orbit**, not
a variable-length spring arm — there is **no per-mode follow-distance scalar and no min/max distance clamp**.
"Zoom" keys change **elevation**, not radius.

## 4.1 Shared base seed and orbit model — (confirmed)

All five in-world manipulators chain a common base seed, then override a few fields. Free parameters are **yaw**
and **elevation**; the follow radius is the fixed magnitude of the eye-offset vector.

| Parameter | Seed value | Notes |
|---|---:|---|
| Speed / time-scale factor | 1.0 | |
| Accumulated **yaw** | **−π/6 = −0.5236 rad (−30°)** | base for Third/First/Static/Event |
| Yaw-orbit angle | 0.0 | (First/Event override to π) |
| Up / axis basis | (0, 0, 1) | |
| **Eye-offset seed** | **(−750.0, 0.0, +500.0)** | magnitude **≈ 901.39 units** = the fixed orbit radius |
| Focus / look-at Z | −40.0 | base/Third |
| **Elevation / pitch** | −40.0 (overridden per mode) | the orbit elevation parameter |
| Pitch-overflow spill bucket | 10.0 | **not a follow distance** — a mouse-pitch overflow accumulator clamped to [0, 27] |

> The "spill bucket" value of 10.0 is explicitly **not** a follow distance; an earlier reading mislabelled it.
> The follow radius is the eye-offset magnitude (≈ 901 units), fixed for all non-gamble modes.

## 4.2 Shared smoothing / clamp pool — (confirmed)

| Constant | Value | Role |
|---|---:|---|
| Time-delta scale | **1e-3** | ms → s |
| Secondary (slow) time scale | **1e-4** | decay path while a mouse-drag accumulator is active |
| Keyboard input gain | **0.3** | per active frame |
| No-input friction | **0.6** | rate × 0.6 each idle frame (Static uses **0.8**) |
| Static zoom gain / occlusion pitch step | **50.0** | |
| Pitch-rate clamp | **±4.0** | rate bound on the pitch integrator |
| Zoom-rate / orbit-step clamps | **±0.1** | rate bounds |
| Decay-path clamp (orbit step) | **±1.0** | applied before the ±0.1 clamp |
| **Elevation angle clamp** | **[−90.0, −12.0]** | the real pitch limit (degrees-ish) |
| **Absolute yaw clamp** | **[−π/2, +π/2]** | base bound |
| Third yaw upper ease | **0.9** | Third upper-yaw = (π/2 · 0.9) ≈ **+1.4137** |
| Rate dead-zone | **1e-3** | snap rate to 0 below this |
| Terrain-collision camera lift | **3.8** | eye Y ≥ terrain + 3.8 |
| Collision Y-bias step | **2.0** | added to clamped terrain Y |
| Yaw-rate forced value on hard hit | **−0.01** | kills drift when grounded |
| Mouse-axis sensitivity | **1e-6** | raw axis delta → pitch rate |
| Mouse-drag pitch gain | **5e-4** | cursor-Y delta → pitch rate |
| Wheel / other-button zoom scale | **0.01** | |

## 4.3 Per-mode seeds and behaviour — (confirmed)

| Mode | Default pitch | Focus Z | Eye-offset | Yaw-orbit seed | Orbit radius | Terrain collision |
|---|---|---:|---|---|---:|---|
| **Third** (default, over-shoulder) | −π/6 (−30°) | −40 | (−750, 0, 500) | 0 | ≈ 901.39 | yes (height clamp + occlusion) |
| **First** (first-person) | −π/6 (−30°) | −55 | (−750, 0, 500) | π | ≈ 901.39 | no |
| **Static** (fixed-angle follow) | −π/6 (−30°) | −55 | (−750, 0, 500) | 0 | ≈ 901.39 | no |
| **Gamble** (orbit, betting minigame) | −π/3 (−60°) | −160 | **(24097.46, 0, 55694.43)** | 0 | ≈ **60684.08** | yes (world-AABB probe) |
| **Event** (scripted / cutscene) | −π/6 (−30°) | −55 | (−750, 0, 500) | π | ≈ 901.39 | data-driven (scripted) |

Per-mode notes:

- **Third** runs the full pipeline; yaw clamp **[−π/2, +1.4137]** (asymmetric, 0.9-eased upper); elevation
  **[−90, −12]**; friction 0.6, gain 0.3. The only mode using both collision behaviours.
- **First** shares Third's input pool; the eye sits at the player head so the trailing distance collapses; no
  terrain collision; yaw-orbit seeded to π (flips initial facing 180°).
- **Static** polls only the elevation keys; gain **50.0**, friction **0.8**; yaw fixed (no orbit); no terrain
  collision; tracks the local player position only.
- **Gamble** orbits at a large radius (≈ 6e4 units) to frame a betting board; yaw clamp **symmetric ±π/2** (no
  0.9 ease); fetches the world-AABB and has a collision/height response; orbit input is UI-driven.
- **Event** positioning is data-driven from a scripted block (cutscene start timestamp + a path/keyframe state);
  the player loses manual control while it is active. Keyframe/path format not decoded.

A separate **Select** (character-select preview) manipulator is outside the in-world set: it clears the yaw /
elevation / eye-offset and builds a multi-waypoint preview camera path. Listed for completeness only.

## 4.4 Terrain collision push-in (Third; probed by Gamble) — (confirmed)

1. World (X,Z) → terrain grid: `index = 10000 − (int)(coord × −1/1024)`, cell size **1024.0**, with a −1024.0
   pre-bias for negative coords (floor emulation). (Matches `formats/terrain.md` tiling.)
2. Sample the bilinearly-interpolated terrain height at the eye (X,Z); the no-terrain sentinel is
   **−3.4028e38** (→ no hit).
3. On a hit, the eye **Y is clamped to `terrainHeight + 3.8`** with a `+2.0` bias step; on a hard hit the
   yaw-rate is forced to **−0.01**.
4. On occlusion (probe miss / line blocked), elevation is nudged by **50.0 · dt** toward keeping the target
   visible, plus the +2.0 bias and a small 0.01 correction.

So the "push-in" is a **vertical lift (+3.8, +2.0 bias)** plus an elevation nudge on occlusion — there is **no
horizontal radial pull** (the radius is fixed).

## 4.5 Projection / FOV — (UNVERIFIED)

Three candidate vertical-FOV values remain unreconciled across setup sites: **60°** (perspective-camera
setup), **45°** (π/4, the seed read from the perspective-camera object during cull), and **65°** (a main-window
camera). Near/far candidates likewise differ (near 10000 in one path; near 5 / far 15000 in another). Do **not**
hard-code an FOV; expose it as config and default to the runtime-read π/4 (45°) with a note. (Same open item as
`specs/camera_movement.md` §D.2.)

## 4.6 Camera — open items

1. Action polarity / axis binding (which ± of each input pair is up/in vs down/out; field→semantic mapping).
2. The π yaw-orbit seed's visual effect (inferred 180° initial facing flip) — not proven against a running client.
3. Authoritative runtime FOV (60 / 45 / 65 unreconciled).
4. The constant mode-tag value present in every manipulator ctor — meaning unknown.
5. Gamble far-orbit numbers' UI driver (orbit angle source not traced).
6. Event keyframe/path format.
7. `CAMERA_XZ` / `CAMERA_XYZ` saved-option semantics (2-axis vs 3-axis follow; may relate to the eye-Y clamp).

---

# 5. Movement and collision constants

Client movement is click-to-move: a screen pixel is unprojected to a world XZ ground point, tested against the
2D solid map, integrated per frame, and reported to the server as move-request packets. **World Y is forced to 0
for simulation and never sent by the server** — the terrain heightmap is a *visual* vertical-placement surface
only. The behavioural pipeline and the move-request packet shapes are owned by `specs/camera_movement.md` and
the committed `packets/*.yaml` (move-request 2/13, actor-movement-update 5/13); this section pins the constants.

## 5.1 Speed model — (confirmed)

There is **no hard-coded numeric speed**. The per-frame integrator contains exactly one code-literal speed
factor: **× 4.0** (forward step distance per frame = `moverSpeedScalar × 4.0`). The actual walk/run scalar is
**data-driven** from a per-map config table keyed `MAP_SPEED` (load-error log "MAP_SPEED data load error"), so
concrete walk-vs-run units are map config data, not code constants (hand to the asset/config-table specs).

Walk vs run is expressed three ways: a lifecycle-state field (**2 = walk, 3 = run**; also 0 = uninit,
1 = refreshing, 8 = dead/scripted), a run-flag byte (`== 1` ⇒ running; packed onto the wire), and the per-frame
speed scalar feeding the ×4.0 step. The base move-speed multiplier defaults to **1.0**.

**Turn rate:** there is **no angular-velocity constant**. The client computes an instantaneous facing each step
and snaps to it (no yaw interpolation over time).

## 5.2 Heading — (confirmed)

**Heading = atan2(Δx, Δz) in radians**, standard CRT range **−π .. +π** (produced via the CRT atan2 path, not a
fixed-scale integer angle). Inputs are the interpolation-target XZ minus the current/last-network XZ in the
engine's XZ frame. The server movement-update echo carries a yaw in the same radian convention.

## 5.3 Move-issue path constants — (confirmed, sample-verified where noted)

| Constant | Value | Role |
|---|---:|---|
| Per-frame step multiplier | **4.0** | forward step distance = speed scalar × 4.0 |
| Move-issue clamp distance | **12.0** | when target is far, the unit delta is scaled to at most 12.0 per issue |
| Clamp threshold (squared) | **144.0** (= 12²) | compare `dist² > 144.0` before clamping |
| Move dead-zone (squared) | **4.0** (= 2 units) | ignore moves with `dist² ≤ 4.0` |
| Pick-ray / mover speed seed | **1000.0** | max screen-pick distance / mover seed |
| Click re-issue throttle | **100 ms** | minimum interval between re-issued click moves |
| Click-marker texture id | **380000000** | highlight/cursor texture dropped at the destination |
| Move-request payload size | **16 bytes** | matches the committed move-request packet spec |

**Move-issue flow:** click → unproject to XZ ground (pick seeded with 1000.0) → begin-move (clamp the delta to
12.0 when `dist² > 144.0`) → commit (only if `dist² > 4.0`, i.e. ignore sub-2-unit moves; gated by a cooldown
and a busy/lock flag) → drop the click marker → integrate per frame.

Two input ids drive the click handler: id **1013** = primary walk hold (left-mouse-hold, the main walk path);
id **1012** = a secondary input whose branch sends a **stop frame** at the current position (halt in place).

## 5.4 Send cadence (server-reconciliation heartbeat) — (confirmed; hard-cap UNVERIFIED)

A send-worker thread loops with **`Sleep(10 ms)`**. Two channels each only **warn** when overdue:

| Channel | Overdue-warning threshold |
|---|---:|
| Move | **200 ms** |
| Proxy | **400 ms** |

The move heartbeat fires at most once per distinct `timeGetTime()` millisecond. On each heartbeat a global
parity counter alternates an **X dither of ±20.0 units** (odd → +20, even → −20) so each report is a slightly
different point (keeps server position state fresh / defeats a static-position dedupe). **Important:** the
200 ms / 400 ms values are *overdue-warning thresholds*, **not** proven hard rate caps — no rate-limit literal
beyond the warnings was found. Whether 200 ms is also a hard period needs a capture (UNVERIFIED).

An anti-speedhack telemetry tolerance of **1025 ms** (server-time/cycle diff limit before flag) is carried from
a prior pass (HIGH confidence, not re-byte-verified here).

## 5.5 Per-frame integration and server snap — (confirmed)

Each frame the mover faces the destination, copies the facing quaternion, and advances by
`moverSpeedScalar × 4.0` rotated by the facing. If the candidate overshoots the destination **and** the collision
subsystem reported a block, it **snaps to the corrected hit point** (wall-slide / stop response); otherwise it
snaps to the destination. The resolved position is propagated to couple/mount partner actors. A move-request
frame is emitted each step.

Server reconciliation (cite the catalog, not re-derived): C2S move-request (16 B) — client emits, server
echoes; S2C actor-movement-update (40 B) — drives interpolation for remote actors and is the **authoritative
position correction** for the local player (a specific motion-code value triggers an instant-snap branch).
World Y is never on the wire. A local snap-to-valid helper rewrites current and destination to a corrected
point when a target cell is invalid/out-of-bounds.

## 5.6 Collision against solid map (`.sod`) — (sample-verified)

Collision is **strictly 2D in XZ** (no Y). It is a swept test of the movement line segment against static solid
line segments using `Z = slope·X + intercept` (or `X = constant` for vertical segments). The on-disk `.sod`
format is owned by `formats/terrain.md` / the asset specs; the verified record strides from real samples are:
a file header `u32 solidCount`, then per-solid records of **108 bytes** each (AABB at +0x00, then a `u32
segCount` and **48-byte** segment records: AABB at +0x00, slope at +0x20, fixed-X at +0x24, intercept at +0x28,
type-flag at +0x2C). Three real `.sod` samples passed the size check; all sampled segments were non-vertical
(`type_flag = 0`); the vertical path is code-present but sample-unconfirmed.

The query maps the move-segment AABB into a **16 × 16 grid of quadtrees** (clamped 0..15) and keeps the
**nearest** intersection. Cell-grid math (confirmed):

| Constant | Value | Role |
|---|---:|---|
| Cell size | **1024.0** | world units per terrain/collision cell |
| World→grid reciprocal | **−1/1024 = −0.0009765625** | with a −1024.0 pre-bias for negative coords |
| Cell index base | **10000** | base index for cell (x,z) ids; matches `d###x100##z100##` filenames |
| Per-cell neighbour stride | **5** | center + 4 neighbours; the move is asserted to cross at most one cell |

## 5.7 Visual height sampling (`.ted`) — (sample-verified, visual-only)

The `.ted` heightmap (owned by `formats/terrain.md`) is a **65 × 65 = 4225** float grid (the first 16 900 bytes
of the file), row-major with **rows = constant Z**, at a **vertex spacing of 16.0 world units** (64 quads ×
16 = one 1024-unit cell). World vertex position `= (mapX − 10000) × 1024.0 + col × 16.0`; heights are direct
world-space Y (no Y multiplier). Normals decode as `i8 / 127.0`. A seam-continuity test across the Z boundary
of two real adjacent tiles showed `max |Δ| ≈ 0.0012` units (seamless). **This surface is rendering-only**:
server movement and collision use the 2D `.sod` quadtree exclusively; simulation Y is 0.

## 5.8 Movement — open items

1. Per-map walk/run numeric speeds (data-driven via the `MAP_SPEED` table — needs config tables or a capture).
2. Whether 200 ms is a hard rate cap or only a warning threshold (needs a capture).
3. The two trailing padding bytes of the move-request payload (almost certainly filler; capture welcome).
4. The runtime bilinear height-sample-at-arbitrary-XZ function (heightmap format pinned; sampler not isolated).
5. Vertical `.sod` segments (`type_flag == 1`) — code path present, no sample.
6. Whether the local player's authoritative correction also arrives via a separate local-state-sync channel.
7. Re-confirm the exact anti-speedhack 1025 ms limit if a clean spec needs it.

---

# 6. Quest system

The quest system is **server-authoritative**: the client holds dialog/requirement data tables and renders a
quest log + completion verdicts, but kill/collect/talk objective types and reward grants live on the server. The
client sends a single unified quest-action packet and receives a quest-list snapshot and a completion verdict.
Packet byte layouts (2/28, 5/68, 5/73) are owned by `opcodes.md` + `packets/*.yaml` + `specs/quests.md`; this
section specifies the client data model and flow. **All quest/dialog text is CP949.**

## 6.1 Networked surface — (confirmed; on-wire widths UNVERIFIED)

| Direction | Message | Role |
|---|---|---|
| C2S | quest-action (proposed name; not yet cataloged — defer to the protocol spec-author) | unified accept / proceed / give-up |
| S2C | quest-list | quest-log snapshot (452-byte body) |
| S2C | quest-complete | completion + reward verdict (344-byte body) |

The client-side quest-action body is a 12-byte block: a **sub-action byte (+3 padding)**, then a **u32 npc-kind**
and a **u32 quest-id** (both full 32-bit on the client side; on-wire zero-extension is capture-unverified). The
sub-action enum (each at a distinct send site): **2 = ACCEPT, 3 = PROCEED/CONTINUE, 4 = GIVE-UP/ABANDON**
(values 0/1 not observed). This refines an earlier note that read the trailing fields as bytes.

## 6.2 Data tables — (confirmed loaders; field offsets IDA-derived)

Three lookup tables (keyed maps) feed quests; record sizes are hard facts from the loaders:

| Table | Source file | Record size | Key |
|---|---|---:|---|
| Quest templates | `data/script/quests.scr` | **4960 bytes** (~366 records) | quest id |
| NPC dialog/step records | `data/script/npc.scr` | **404 bytes** (~2510 records) | dialog/npc id |
| Mob/NPC templates | mobs-class template (`mobs.scr`, ~1.95 MB) | large (≥ 1168 B) | actor descriptor field |

**npc.scr dialog record (404 B):** a 20-byte header followed by **6 × 64-byte CP949 text lines** (the
"6 dialog lines"). The header carries the map key (+0x00), a **group-key (+0x04)**, and a **step-index (+0x08)**.
Multi-step dialog is a chain of sibling records sharing the group-key: PROCEED finds the sibling with
`stepIndex == cursor + 1`, BACK finds `cursor − 1`; the "%d / %d" objective counter is `(cursor+1) / total`
where total counts sibling records with the same group-key.

**quests.scr template (4960 B) — requirement block** (offsets within the record; player-state compared):

| Offset | Type | Field | Gate behaviour |
|---|---|---|---|
| +0x00 | u16 | quest id | matched in the active list ("already accepted") |
| +0x02 | u8 | category | duplicate-category gate vs other active quests |
| +4936 | u32 | prereq / chapter id | prerequisite gate |
| +4944 | u16 | min level | if nonzero and > player level ⇒ "need level" |
| +4946 | u16 | max level | if nonzero and < player level ⇒ "level too high" |
| +4948 | u8[5] | required class | per-index class flags |
| +4953 | u8 | required gender/faction | mismatch ⇒ blocked |
| +4954 / +4955 | u8 / u8 | required stat min / max | 1..7 stat gate |
| +4956 | u8 | required flag | flag gate |

**quests.scr dialog handles** (each is an npc.scr map key — distinct offer / in-progress / turn-in text sets):
offer handle at **+0x48**, active handle at **+0x54**, complete handle at **+0x58**.

**In-progress objective sub-array (PARTIAL):** an objective-entry count (u8) at +100 and an array at +104 with
**240-byte stride**, each entry carrying a target/state u16 (~ +124) and a value table (~ +76). This is the
closest thing to a client-side "objective" structure, but it does **not** label the kill/collect/talk type —
that distinction is server-authoritative. (Needs a `quests.scr` sample to byte-confirm.)

## 6.3 NPC ↔ quest binding — (confirmed)

The mob/NPC template carries two parallel arrays indexed by an NPC "kind" slot:
- **+1084**: a `u16[]` of **offered quest ids** → keys into `quests.scr`.
- **+1168**: a `u32[]` of **offered dialog handles** → keys into the npc.scr dialog map.

The full data chain:

```
npc.arr[mob_id] ──key──► mobs.scr template
                              ├ +1084 u16[] offered quest ids ──► quests.scr[quest_id]
                              └ +1168 u32[] dialog handles  ───► npc.scr dialog map (6 CP949 lines)
quests.scr[quest_id]
   ├ +0x48 / +0x54 / +0x58 dialog handles ──► npc.scr dialog map
   ├ +0x02 category, +0x00 id, +4936 prereq/chapter
   └ +4944/4946 level, +4948 class[5], +4953 gender, +4954/4955 stat gates
```

**npc.arr placement record (28 bytes, little-endian) — (sample-verified):**

| Offset | Type | Field | Confidence |
|---|---|---|---|
| +0x00 | u16 | mob_id (keys the mob template) | confirmed |
| +0x02 | u16 | field_1 (level? sub-type? — identical in both samples) | UNVERIFIED |
| +0x04 | f32 | world X | confirmed |
| +0x08 | f32 | world Z | confirmed |
| +0x0C | f32 | rotation about Y (radians) | partial |
| +0x10 | u32 | spawn type | confirmed |
| +0x14 / +0x18 | u32 / u32 | unknown | UNVERIFIED |

The arr record itself carries **no direct quest id**; the binding is via `mob_id → mobs.scr +1084`. A separate
selector returns an alternate (event/tutorial) dialog kind when an NPC's spawn-type field is **7** under a
global event state — i.e. event-gated dialog keyed off `spawn_type == 7` plus global event flags.

## 6.4 Accept / proceed / give-up flow — (confirmed)

State summary (client side):

```
offer dialog (phase byte; text from the quest offer-handle → npc.scr 6 lines)
  └ accept  → C2S quest-action sub=2 → (server) → S2C quest-list refresh (quest now active)
in-progress (a step cursor walks the npc.scr step chain by (group_key, step_index))
  ├ proceed → C2S quest-action sub=3   (only when the panel dialog phase is "ready-to-proceed")
  └ give up → C2S quest-action sub=4 + local clear
turn-in (quest complete-handle dialog) → (server) → S2C quest-complete verdict
```

**Accept gate (corrected):** the accept widget passes a prerequisite/duplicate gate, then a **billing gate**:
the send happens iff the player has a positive billing/cash status **or** `playerLevel < 26`. In other words,
**26 is the level at which the premium gate engages** — below 26 quests accept free; at level ≥ 26 a positive
billing status is required. (An earlier note read this backwards as a minimum level.) The availability gate
also rejects a quest already held and a quest whose category is already held.

## 6.5 Quest-log and completion — (confirmed; capture-marked offsets UNVERIFIED)

- **Quest-list (452-byte body)** populates a local quest-log mirror: two 10-entry slot tables (32-byte stride,
  flag at entry+8), a 20-entry quest-entries table (32-byte stride, u32 quest id + a CP949 name up to 17 bytes),
  and scalar active/panel flags. When the active/tracking flag transitions 0→nonzero the tracking panel opens
  (with SFX **862300001**); nonzero→0 closes it.
- **Quest-complete (344-byte body)** acts only when a "complete-mode" field equals 1, then on a reward-state
  byte: **1 = GRANT** (show the panel, positive SFX **910036000**), **2 = DENY/FAIL** (negative SFX). The body
  remainder (beyond the mode/state fields) is copied wholesale and not field-decoded by the client — **reward
  granting is server-side**; actual item/exp/gold arrive via side-channel opcodes. The completion panel
  double-buffers the previous body and uses a phase byte {0 idle, 1 showing, 2 closed}.

## 6.6 Tutorial subsystem and `tutor.lua` — (confirmed)

- **`data/script/Tutor.scr`** (note the capital T) is a definition table of **1660-byte records** (~86 lessons):
  an id (u32) at +0x00 and a description string at +28; loaded into a tutor map at startup.
- **`data/script/tutor.lua`** is loaded as a **plain on-disk file** (not via the `.pak` VFS) into the single
  global Lua state. Two bindings use it: one builds the tutorial panel widgets and (re)loads `tutor.lua` to
  supply panel content/layout; the other pulls a **numbered string table** out of `tutor.lua` (via the script's
  `getTableSize` / `getTableString` globals, UTF-8-decoded). Lessons paginate by numeric string-table id —
  next = id+1, previous = id−1.

This matches the overall Lua model (scripts = data/config/i18n; host C++ = logic). The mapping from a
`Tutor.scr` record id to a `tutor.lua` string-table id is assumed equal but not byte-confirmed.

## 6.7 Quest — open items

1. Objective **type** (kill/collect/talk) — no client-side type enum or target-id/count fields found; the
   distinction is server-authoritative (text = npc.scr lines, progress = server counter + step cursor).
2. The bulk of the 4960-byte quest record (between ~+90 and +4936, including the +104 objective array) is
   undecoded; no reward item/exp/gold fields are client-read. Needs a `quests.scr` sample.
3. npc.scr header bytes beyond the proven group-key/step-index/key.
4. mobs.scr +1084 / +1168 array lengths (how many quest slots per NPC) — stride proven, dimension not.
5. On-wire widths/zero-extension of the quest-action npc-kind / quest-id (no quest C2S capture).
6. Active-quest list A vs B semantics (both hold held ids; split unproven — e.g. main vs repeatable).
7. Whether the quest-complete body remainder carries a reward list or is pure display.
8. npc.arr field_1 (+0x02) role (level vs sub-type vs faction).
9. Whether a `Tutor.scr` record id equals its `tutor.lua` string-table id.

---

## Implementation notes (clean-room → .NET / Godot)

- **Sound** (`Client.Infrastructure` / Godot audio): model the 2D-stereo / 3D-mono split, the 512 KiB one-shot
  vs 1 MiB-ring streaming threshold, the five per-map tables keyed by mud-cell bytes, the per-actor footstep
  ids, the ±0.7 3D scale, the −10000 silence endpoint, and the two playback routes (synchronous + worker queue).
- **UI** (Godot Control tree): the widget semantics in §2 map onto retained-mode Control nodes; the flag-byte
  meanings (visible/enabled/focused/hovered) and the UI-first / world-second hit-test precedence are the
  load-bearing rules. Layout/text come from the `.lua` tables; see `specs/lua_scripting.md`.
- **Render** (Godot): the toon+bloom contract (256×1 ramp LUT, stage0 albedo / stage1 ramp, N·L luminance →
  ramp sample, edge/bloom composite weights) and the no-vsync uncapped loop are the facts to reproduce; the rest
  is engine-specific D3D9 plumbing the Godot layer replaces.
- **Camera / Movement** (`Client.Domain` + Godot camera): treat the camera as a fixed-radius orbit (yaw +
  elevation, no distance clamp), and movement as click → unproject-to-XZ → 2D solid-quadtree walkability →
  integrate (speed×4/frame; clamp 12 u when far; dead-zone 2 u) → snap on arrival/collision → emit the
  move-request (heading = atan2 radians; constant mode byte; run flag) at sub-200 ms cadence with ±20 X dither;
  simulation Y = 0; `.ted` height is visual-only. Expose all feel constants as config.
- **Quest** (`Client.Application` / `Client.Domain`): the client is a renderer of server-authoritative state —
  model the three data tables, the (group-key, step-index) dialog chain, the accept billing-gate at level 26,
  and the quest-log/completion mirror; do not invent client-side objective-type logic.
