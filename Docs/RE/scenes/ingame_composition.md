---
verification: re-pinned 2026-06-21 against the doida.exe binary (build 263bd994, static IDA,
  hexrays + dbg present; no dbg_start). This dossier reconstructs the IN-GAME scene composition (how
  the 2D HUD is layered over the 3D world) and the in-game dynamic/input model (toggle keymap, panel
  show/hide, focus, tooltips, modals, toasts). The render-bucket ORDER was independently re-walked in
  BOTH draw paths (direct + offscreen glow/post) and CONFIRMED; the named per-bucket render-callback
  installer and the LAST-callback UI/HUD placement were CODE-CONFIRMED, including the exact D3D
  renderstates the HUD bucket sets (depth test OFF / lighting OFF). The single-backbuffer "two phases,
  not two viewports" finding was SETTLED (no world sub-viewport, no dedicated HUD compositing target).
  The toggle-keymap / gate / tooltip / modal inventory was re-confirmed with refinements (panel
  open-test is the +136 & +140 pair; type-6 HUD action-id group 0x270..0x275 with the wire-touching
  click). The generic per-frame render machinery is NOT re-derived here — it lives in
  specs/rendering.md (this dossier maps how the in-game scene USES it).
scene: In-game / World (engine state 5)
evidence: [static-ida]
capture_verified: false
sources:
  - Docs/RE/specs/rendering.md            # §2 callbacks, §2.1/§2.3 paths, §3 draw order, §4.2 per-bucket state, §6 glow/post
  - Docs/RE/specs/environment.md
  - Docs/RE/specs/terrain-streaming.md
  - Docs/RE/specs/skinning.md
  - Docs/RE/specs/equipment_visuals.md
  - Docs/RE/specs/ui_system.md
  - Docs/RE/scenes/scene_state_machine.md # the dispatch spine that constructs/runs/tears down this scene
  - Docs/RE/formats/terrain.md
  - Docs/RE/formats/terrain_layers.md
  - Docs/RE/formats/terrain_scene.md
  - Docs/RE/formats/sky.md
  - Docs/RE/formats/effects.md
  - Docs/RE/formats/xobj.md
  - Docs/RE/formats/sod.md
  - Docs/RE/formats/skn.md
  - Docs/RE/formats/mesh.md
  - Docs/RE/formats/animation.md
  - Docs/RE/structs/gucomponent.md
  - Docs/RE/structs/guwindow.md
  - Docs/RE/opcodes.md
---

# In-game Scene Composition — Engine State 5 (2D HUD over the 3D world)

> **Clean-room neutral synthesis.** Rewritten in neutral prose under EU Software Directive 2009/24/EC
> Art. 6 (decompilation permitted solely to achieve interoperability). This file contains **no
> decompiler pseudo-C, no binary virtual addresses, and no decompiler identifiers.** Object byte
> offsets (relative to an object start), `GameState` case numbers, D3D renderstate IDs, message-DB
> string ids, action ids, and opcode `(major, minor)` pairs are interoperability facts and are stated
> where load-bearing. Korean UI text is **CP949**, resolved at runtime from the message database — the
> id→text mapping is a data/format concern, not recovered here.
>
> **What this document is.** The single dossier for *how the in-game frame is composed*: the order the
> 3D world and the 2D HUD are drawn into one backbuffer, the render state each bucket runs under, where
> the HUD sits relative to the 3D scene, how terrain / buildings / characters / effects are presented
> beneath it, and the runtime dynamics that drive the HUD (the toggle keymap, panel show/hide and
> focus, tooltips, modals, and toasts). It maps how the in-game scene **uses** the engine's render
> machinery and asset chains; it does **not** re-derive them — those live in the sources above.
>
> **Verification basis.** Re-confirmed against the live `doida.exe` IDB on build SHA `263bd994`
> (static IDA control-flow; hexrays + dbg present; no `dbg_start`, no live network capture). Outcome:
> the composition order, the single-backbuffer model, and the HUD-bucket render state were CONFIRMED /
> CODE-CONFIRMED; the dynamics inventory was CONFIRMED with the refinements noted in §6–§9. Open items
> that do not affect composition order are listed in §11.

---

## 1. Headline finding — two phases of one frame, not two viewports

The in-game frame draws the **3D world first** and the **2D HUD last**, both into the **same
backbuffer**. They are **not two viewports** and there is **no dedicated world render-target that the
HUD is composited over**.

- The device step walks the engine scene list, runs camera/frustum setup and the scene-draw fork for
  the single in-game scene, then issues **one** `Present` per frame. The HUD is **not** a separate
  scene or viewport — it is the **last ordered draw callback inside that one in-game scene**.
- No sub-rectangle viewport is set for the world (no `SetViewport` to a sub-rect): the camera step
  builds only the projection (from the FOV) plus the cull frustum. The world fills the **whole
  backbuffer**; the HUD overlays the **whole backbuffer**.
- The only render targets that exist are the engine's full-screen offscreen glow/post buffers
  (rendering.md §6, internally TEX0/1/2), sized from the backbuffer dimensions. On the offscreen path
  the composited world is blitted back to the full backbuffer **before** the HUD draws.

So the entire "2D-over-3D" composition reduces to: **draw the world into the backbuffer, then draw the
HUD on top of the finished backbuffer with depth test/write OFF under an orthographic projection.**
Because depth is off for the HUD bucket, the HUD never Z-fights the world and always wins on top.

---

## 2. In-game state entry and scene construction (once, on enter-world)

The scene state machine (scenes/scene_state_machine.md) dispatches to the in-game case (engine
`GameState` case **5**). That case builds three cooperating objects and then enters the shared
per-scene run loop, which blocks until the scene exits. Construction order:

1. **In-game command/input handler** ("Mainhander" / MainHandler) — the gameplay key + command
   handler; stored into a slot on the HUD root window (HUD-root `+0x500`).
2. **HUD root window** (MainWindow) — a `GUWindow` subclass whose member slots are the in-game panels.
   It owns a **178-slot panel array** (panel-slot array at HUD-root `+0x238`). This is the 2D HUD
   object. The panel objects themselves are built by the HUD panel-build routine, which fills the
   178-slot array (build-order ownership of the individual slots sits with the struct recovery for the
   panel-build routine; the toggle keymap only shows/hides slots that are already built — see §6).
3. **Second command-handler** singleton.
4. **Enter-world activation routine** — wires the world subsystems for the in-game frame:
   1. Calls the **in-game 3D scene-graph builder** (§3).
   2. Activates the world subsystems (terrain manager, actor manager, effect/target-selection
      subsystems) and restores the world selection target.
   3. Builds the lens-flare / sky geometry.
5. Pushes the scene sub-objects (the scene-draw object, MainHandler, the 2nd handler) onto the
   per-scene teardown list.
6. Enables keepalive and enters the per-scene run loop (blocks until the scene exits).

> The keymap/input dispatch (§6–§9) is runtime behaviour, not part of this scene-build order. The
> panels exist after step 2's panel-build; the keymap merely toggles their visibility.

---

## 3. The in-game 3D scene-graph (the world content drawn under the HUD)

The enter-world activation calls the **in-game scene-graph builder**, which constructs exactly **one**
3D scene (internally named "charater scene" — the engine's own spelling). It contains:

- **A perspective camera.** Field-of-view (Y) = **65 degrees** (`π·65/180`), aspect-corrected by the
  runtime `screenW / screenH`. **Near plane = 5.0, far plane = 15000.0.** (Character Select reuses the
  same camera mechanism for its preview but builds a distinct scene.) The camera's angle and sorted
  near/far range are stored once at build (angle validated `0 < angle ≤ π`); the per-frame camera
  setup reads the FOV (camera `+168`) and aspect (camera `+172`) fields each frame.
- **5 view-platform objects** bound to the camera (each world layer binds a distinct view-platform).
- **A `GScene` root** ("charater scene") attached to the engine scene driver, with the camera, plus
  **TerrainManager** and **ActorManager** attached. ActorManager is attached last.
- **A `GSwitch` group** with **5 per-layer 3D scene nodes**. Their attach order is tagged with
  message-DB string ids **2006, 2004, 2005, 2148, 2148** — the world draw layers (in attach order:
  terrain / buildings+static / world-objects / actors / effects). The exact id→layer-name mapping is
  owed to a data-side message-DB string read (see §12); the **attach order is confirmed**, only the
  printed names are owed. Refinement: only layers 2..5 have their per-layer enable byte (`+229`)
  explicitly cleared at build; the first layer (id 2006) does not get an explicit `+229` clear.

The scene-graph builder ends with the lens-flare / sky geometry prep. The world's *internals* (how
each layer parses and renders) are owned by the format/spec sources in the front matter; this section
records only how the in-game scene wires them together.

### 3.1 What each world layer presents (cross-reference, not re-derived)

| Layer (attach order) | message-DB id | What it draws | Authoritative spec |
|---|---|---|---|
| 1 terrain | 2006 | Streamed multi-texture terrain (cell grid, height, texture index → bgtexture → `.dds`) | `formats/terrain.md`, `terrain_layers.md`, `terrain_scene.md`; `specs/terrain-streaming.md`, `environment.md` |
| 2 buildings / static | 2004 | Static world meshes / buildings | `formats/xobj.md`, `mesh.md` |
| 3 world objects | 2005 | World-placed objects | `formats/xobj.md` |
| 4 actors | 2148 | Skinned characters & mobs (cel path, rendering.md §5.1a) | `formats/skn.md`, `mesh.md`, `animation.md`; `specs/skinning.md`, `equipment_visuals.md` |
| 5 effects | 2148 | Particle / billboard / additive effects | `formats/effects.md`; `specs/rendering.md §3.3` |

Sky-dome, background, and lens flare are the **pre-scene** pass (drawn before the opaque world — §4),
fed by `formats/sky.md`. Ground collision (`formats/sod.md`) and spawn placement are gameplay/state
inputs to ActorManager, not draw buckets.

---

## 4. Per-frame composition order (every frame in the run loop)

The shared per-scene run loop (rendering.md §2.0) drives the per-frame device step. For the single
registered in-game scene it runs the RTTI-driven camera/frustum setup (rendering.md §2.2 — `dynamic_cast`
to the perspective camera, reads the in-game camera's FOV/aspect, `π/4` fallback, builds the view
matrix and cull frustum), then the **scene-draw fork** (rendering.md §2.1 direct path vs §2.3
offscreen glow/post path), then a single **Present** with device-lost recovery.

### 4.1 Named render-callback installer

A render-callback installer (called from the scene-graph builder) writes the per-bucket draw callbacks
into the scene-draw object, with their context bound to MainHandler. The installed slots:

| Scene-draw slot | Bucket / role |
|---|---|
| `+172` | Transparent / particles (FX overlay) |
| `+180` | Cull / camera-phase hook (fires during camera setup, **not** the draw fork) |
| `+188` | Sky / background (pre-scene) |
| `+196` | Second pre / opaque-prep hook |
| `+204` | Opaque world (terrain → buildings → objects → actors) |
| `+212` | **UI / HUD draw** — set separately during enter-world activation, context = HUD root window |

### 4.2 Firing order — direct path

1. **Pre-scene / sky** (`+188`): background, sky-dome, lens flare.
2. Second pre / opaque-prep hook (`+196`) → texture-stage defaults.
3. Scene-root opaque draw → **opaque world** (`+204`): **terrain → buildings/static → world objects →
   characters/actors (+ shadows)**.
4. **FX / transparent overlay** (`+172`): fog, billboards, 2× / multiply, water, particles, additive
   (rendering.md §3.3 for the transparent sub-order).
5. **UI / HUD** (`+212`) — **LAST**, over the finished 3D backbuffer.
6. Optional FPS counter, then `EndScene`.

### 4.3 Firing order — offscreen glow/post path

1. The world is rendered into offscreen RT0 (the pre-scene → opaque → FX sequence above, targeting the
   offscreen RT instead of the backbuffer).
2. RT0 is released to the backbuffer; the glow extract + blur passes run; the toon/glow composite does
   the **present-blit of the composited world to the backbuffer** (rendering.md §6.2).
3. **UI / HUD** (`+212`) draws on the now-finished backbuffer.
4. Optional FPS counter, then `EndScene`.

**Both paths converge on the same invariant: UI/HUD (`+212`) is the last draw before `EndScene`, over
the completed 3D backbuffer.** The world is fully composited in steps 1–4 (direct) / 1–2 (offscreen);
the HUD paints on top. That last-callback ordering, plus the depth-OFF orthographic state in §5, is the
entire 2D-over-3D composition.

---

## 5. HUD-over-world render state (the composition crux)

The `+212` UI/HUD callback runs the HUD render-state setup, then draws the UI tree (the engine UI
manager / panel roster), then the input-focus / caret pass. The render state it enters with — read
from the binary as literal D3D renderstate IDs — is:

| Renderstate | ID | Value | Meaning |
|---|---|---|---|
| `D3DRS_ZENABLE` | 7 | 0 | **Depth test OFF** |
| `D3DRS_LIGHTING` | 15 | 0 | **Lighting OFF** |
| `D3DRS_ZWRITEENABLE` | 19 | 2 | Depth write configured off for the bucket |
| `D3DRS_SPECULARENABLE` | 26 | 0 | Specular off |
| `D3DRS_CLIPPING` | 27 | 1 | Clipping on |
| `D3DRS_COLORVERTEX` | 28 | 0 | Vertex colour off |
| `D3DRS_CLIPPLANEENABLE` | 137 | 0 | No user clip planes |

Plus texture-stage / sampler defaults. The bucket additionally runs: **cull clockwise, fill solid, an
orthographic projection (near/far ≈ −300 … +300) over an identity world/view, and alpha-blend left
disabled at bucket-enter** — each quad/glyph opts into its own translucency (canonical translucent =
`SRCALPHA / INVSRCALPHA`). See rendering.md §4.2 for the per-bucket render-state matrix this matches.

Because **depth test and write are OFF**, the HUD cannot Z-fight the world and always paints on top.

### 5.1 The one exception — 3D item-preview inset inside a 2D panel

An inventory panel can host a **3D item / equip-figure preview** inset. That inset **temporarily
re-enables depth, draws its mini-3D view, then restores depth OFF** — confirming "no depth" is the HUD
default and that this inset is the single place the in-game HUD re-enters a mini-3D view inside a 2D
panel. The preview path is exercised by the type-4 hover/equip-figure branch of the HUD root onEvent
(§7).

---

## 6. Dynamics — input dispatch chain (how keys reach the HUD)

The HUD is **static geometry composed last**; what makes it dynamic is the input pipeline that toggles
panels and routes clicks. The chain:

1. A **DirectInput8 keyboard thread** builds a normalized key record (type byte at record `+0`
   down/up; keycode at record `+4`; flags at record `+12`), sets/clears bits in the AppService
   key/action bitset (**1033 bits**), and pushes the record to a cross-thread ring buffer; the main
   thread drains the ring.
2. The **HUD root window onEvent** (the toggle host) routes a drained record by its type byte (§7).
3. A **key-down record** is routed to the **master toggle keymap switch**, which reads the keycode at
   record `+4` and dispatches to per-panel toggle/open helpers via modifier-split and range-split
   switches.
4. A separate **in-game command handler** handles non-panel **gameplay** keys (§9).

### 6.1 Three gates before any toggle fires

1. **Chat / text-input suppressor** — the system-text controller singleton exposes an "input line
   active" flag (byte `+508`); while set (the user is typing in chat), hotkeys are **swallowed**.
2. **Input-focus-manager** focused-field check — the focus manager singleton compares a widget's field
   id against the focused id; a focused text field absorbs the key.
3. **Modifier query** — action ids **1012 = Shift, 1013 = Ctrl, 1014 = Alt** are queried from the
   1033-bit state (remapped, id − 744, to internal modifier bit slots `0x10C / 0x10D / 0x10E`) so the
   keymap can branch no-modifier vs Shift/Ctrl/Alt.

---

## 7. HUD root window onEvent — record-type routing

The HUD root window's onEvent switches on the **record type byte at record `+0`**:

| Type | Routing |
|---|---|
| 1 | Key-up path |
| 2 | **Master toggle keymap** (key-down) — §8 |
| 3 | Panel drag / window-move (clamps the panel to screen W/H) |
| 4 | Hover / tooltip + equip-figure (item) preview (§5.1) |
| 5 | World click / targeting + list-reject feedback cue |
| 6 | HUD action-id click — inner switch on action ids **0x270..0x275 (624..629)** |

In the type-6 group, action **0x274** SENDS a wire packet — the quest-NPC-step Cmsg (**opcode major 8**,
wire-touching), and action **0x273** is gated on the current area id == 6. The broader HUD button-bar
click range (action ids **4000..4024**) is handled by a **separate HUD button-bar dispatcher** (§8.2),
which maps those click ids onto the same per-panel toggle helpers as the keymap; the two paths are not
in conflict.

> **Panel open-test refinement.** Whether a panel is open is tested as the **pair** (`panel+136` AND
> `panel+140`), across ~30 panel slots — `+140` is the visible flag and `+136` is a second enable gate.
> (A simpler "+140 only" reading is superseded.)

> **One open item (does not affect composition):** the record type-byte down/up numbering between the
> two onEvent consumers (HUD root vs command handler) is to be reconciled under a live key event —
> pilot the maintainer's session, never `dbg_start`.

---

## 8. Master toggle keymap, panel show/hide, focus

### 8.1 Recovered keymap (no modifier unless noted)

| Key | Action |
|---|---|
| Esc | Close the top-most open panel |
| Space | Self-target |
| b | War list |
| c | Character / status group |
| f | Relation group |
| g | Guild / family |
| h | Help / shortcut bar (also accepts `h` to close) |
| i | Inventory / character group |
| j | Guild-war / info |
| k | Close many panels |
| l | List / log |
| m | Link / map |
| n | Guild panel (permission-gated; else a "not allowed" notice) |
| o | NPC-dialog / option |
| p | Close all |
| q | Close all + transition |
| s | Inventory group |
| u / w / z | Misc panels |
| x | Auto-pickup (sends a Cmsg item-tick, **not** a panel toggle) |
| y | Toggle HUD dock / minimap cluster |
| Ctrl+a | Attack / peace stance (emote Cmsg) |
| Ctrl+z | Panel |
| Alt+1..9 | Quick-select party member |

(The full keycode → handler arithmetic is decoded in the dirty note; the table above is the
firewall-clean summary.)

### 8.2 Panel show/hide and group exclusivity

- Each panel stores its visible flag at panel `+140` (gated together with the `+136` enable byte, §7).
  Show/hide goes through a universal vtable visibility setter; a `Toggle*Panel(panel, 1/0)` wrapper
  adds layout reflow + a click sound.
- **Group-exclusive:** opening one HUD group hides its sibling slots in the same group (e.g. opening
  one of the inventory/character/skill/quest/party/buddy group hides the others). The MainWindow panel
  slot indices for those groups live in the 178-slot array at HUD-root `+0x238`.
- **Panel-group close primitives:** Esc and the "close" hotkeys early-out by first closing a transient
  open group, then toggling; plus dedicated close-all variants (`k`, `p`, `q`).
- The **central toggle switch always performs the OPEN** — a closed panel receives no events, so a
  panel can only self-handle its own CLOSE (§10).

### 8.3 Focus and mouse capture

Focus / capture flows through the input-focus-manager singleton (a widget's field id vs the focused
id). Mouse capture is taken on button-down and released on button-up.

### 8.4 Click parity (button bar)

The HUD button-bar dispatcher is the **mouse-click equivalent of the keymap**: action ids **4000..4024**
route to the **same** per-panel toggle helpers the keys use, so clicking a HUD button and pressing its
hotkey converge on one code path. Hover on a button builds the floating tooltip (§9).

---

## 9. Tooltips, modals, toasts, and gameplay keys

- **Tooltips.** A floating label is built on HUD-button hover (mouse-move, type-4 record), positioned
  by the cursor, and **auto-hides on any frame where nothing is hovered**.
- **Modal confirm popup.** A dedicated slot with is-open / dismiss helpers; **Esc dismisses it.** The
  in-game command handler also clears the confirm popup on Esc.
- **Toasts / announcements.** A central, colour-coded broadcast routed into the chat log — used for the
  hotkey-rejection "not allowed" notices (e.g. the permission-gated `n` guild panel). A list-click
  selection cue plays when a hotkey is rejected while a list/modal is up.
- **In-game command-handler gameplay keys** (the non-panel branch, separate from the toggle keymap):
  name-tag toggle `.`, chat-mode `/`, sit `a`, effect-cull `e`, target `t`, screenshot, and Esc to
  clear the confirm popup. Several of these resolve their on-screen text from the message database
  (e.g. name-tag, chat-mode, effect-cull notices).

---

## 10. Per-panel self-close

About **90 per-panel onEvent handlers** self-close on **Esc when visible**; some also re-accept their
own open-hotkey to close (e.g. Help = `h`, Quest). This is the symmetric half of §8.2's rule: the
**central toggle switch owns the OPEN** (a closed panel gets no events), while the **panel itself owns
its CLOSE** once it is visible and receiving events.

---

## 11. Open items (none block composition order)

- **World-layer names.** The message-DB string ids 2004 / 2005 / 2006 / 2148 confirm the **attach
  order** of the 5 world layers; resolving them to printed layer names is a data-side message-DB read
  (§3, §12), not an IDA question.
- **Per-quad blend bytes.** The per-panel translucent src/dest blend factors (each quad opts into its
  own translucency) are not enumerated here — `SRCALPHA/INVSRCALPHA` is the canonical case (§5).
- **Runtime aspect.** The screen W/H globals read as 0 statically (runtime-filled); the camera's
  aspect correction therefore can't be byte-pinned from static analysis — confirm under a live session
  if a numeric value is ever needed.
- **Record type-byte numbering.** Reconcile the down/up type-byte numbering across the two onEvent
  consumers under a live key event (pilot the maintainer session; never `dbg_start`).

---

## 12. Asset / message-DB linkage

This facet derives **no new asset formats** — it composes the already-committed 3D specs (front
matter). Indirect data references observed:

- **HUD action click sound** id `862020102` (the 2D sound-create-and-play path).
- **UI / notice strings** resolved at runtime via the message database by id — e.g. notices `44008`,
  `45003`, `10082`, `8034 / 8035` (name-tag), `47001 / 47002 / 47003` (chat-mode), `2065 / 2066 / 2067`
  (effect-cull), and the world-layer attach-order ids `2004 / 2005 / 2006 / 2148`. The id → CP949 text
  mapping is an asset/format concern (the message database), not recovered in this dossier.

---

## 13. Cross-references

- **specs/rendering.md** — the generic per-frame machinery this scene plugs into: §2 callbacks, §2.1
  direct path, §2.3 offscreen path, §3 draw order (incl. §3.2 opaque sub-order, §3.3 transparent
  sub-order), §4.2 per-bucket render-state matrix, §5.1a cel actor binding, §6 glow/bloom post chain.
- **scenes/scene_state_machine.md** — the dispatch spine that constructs, runs, and tears down this
  scene (engine state 5).
- **specs/environment.md, terrain-streaming.md** — world streaming / environment the terrain layer
  uses.
- **specs/skinning.md, equipment_visuals.md** — the actor (character/mob) presentation in the opaque
  world bucket.
- **specs/ui_system.md, structs/guwindow.md, structs/gucomponent.md** — the shared GU* framework the
  HUD root and its panels are built on.
- **formats/** terrain*, sky, effects, xobj, sod, skn, mesh, animation — the per-layer content formats
  the world draws.
