---
verification: synthesised 2026-06-25 from Docs/RE/scenes/load.md (IDB SHA 263bd994, re-confirmed
  2026-06-21 static IDA + 2026-06-24 audit) and the committed spec corpus cited in §5. The
  vertical-fill axis (top→down, bottom-vertex Y + V-texcoord animate) is the CORRECTED reading
  established in load.md §5A.4 (GAP-1 close); the earlier "horizontal left→right" reading from the
  dirty-room pass is superseded. UV constants for the gauge sub-rect (U 0.7539..0.96875,
  V 0.4326..0.96875) are derived from the float literals recovered in the dirty-room pass and
  confirmed against load.md §5A.3. All other geometry (design-space extents, scale factors, 223
  ref-unit span, SFX id) is CODE-CONFIRMED static (IDB SHA 263bd994). Debugger-pending items
  carried forward from load.md §9: vertex stride byte-exactness; second load-pass replay vs cached.
scene: Load — render geometry (engine state 2)
evidence: [static-ida, code-confirmed]
capture_verified: false
sources:
  - Docs/RE/scenes/load.md
  - Docs/RE/specs/resource_pipeline.md
  - Docs/RE/specs/frontend_layout_tables.md
  - Docs/RE/specs/ui_system.md
---

# Loading Screen — Render Geometry Spec (engine state 2)

> **Firewall-clean geometry reference.** This spec is synthesised from the committed
> `Docs/RE/` sources listed in the front matter. It contains **no decompiler pseudo-code,
> no binary virtual addresses, and no decompiler identifiers.** Design-space coordinates,
> UV constants, and sound ids are interoperability facts and are stated where load-bearing.
> The full scene dossier (boot worker, SKIP gate, corpus, completion logic) lives in
> `Docs/RE/scenes/load.md`; this file covers only the two inline textured quads and the
> loading SFX.

---

## 1. Overview

The Load screen (engine `GameState` 2) renders **two immediate-mode textured quads** through a
per-frame callback hosted on the `LoadHandler` object's embedded `Diamond::GView` render-host.
There is **no `GUWindow` or `GUComponent` widget tree** — these are raw 4-vertex sprites with no
`actionId`, no buttons, no text, and no font draws. A single DDS texture drives both quads
simultaneously; a looping SFX plays for the duration.

Key rendering facts:
- **One texture, two quads.** The background and the gauge share the same randomly-chosen `loading*.dds`
  — the gauge is a UV sub-rect of the same image, not a separate asset.
- **Vertical fill (top→down).** The gauge grows by animating the **bottom-vertex Y and V-texcoord**
  downward from a fixed top edge. The X extents of the gauge are fixed throughout. (The earlier
  horizontal/left→right reading from the dirty-room pass is superseded; see `load.md §5A.4`.)
- **Near-static in practice.** The progress value is the integer quotient of cumulative VFS bytes
  loaded divided by the build-time denominator 9,395,240. The boot corpus is roughly one denominator
  wide, so the bar advances by at most a hair and appears static. Completion is driven by the
  worker's done-flag, never by the bar reaching full.

---

## 2. Assets

All paths are inside the user-supplied client VFS (never committed). Chosen at construction by
`rand() % 3`:

| `rand() % 3` | VFS path |
|---|---|
| 0 | `data/ui/loading.dds` |
| 1 | `data/ui/loading06.dds` |
| 2 | `data/ui/loading08.dds` |

The three DDS files are hard-coded literals loaded at **native size** via the D3DX VFS-or-disk
creator and cached in the shared `DXTextureList`. They are **not** entries in `UiTex.txt` or any
scene manifest.

**Inferred DDS geometry (sample-unverified — do not commit pixel values as ground truth):**
V = 0.75 corresponds to 768 px in a 1024-tall DDS, consistent with a 1024×1024 container that
stores a 1024×768 design-space image in its top 75 %. Exact pixel dimensions are
debugger-pending.

**Loading SFX:** sound id **920100100**, played **looping** (category 0, single voice — cannot
double-stack). Started once at construction; pumped each frame; stopped when the scene exits.

---

## 3. Background quad

A **full-screen quad** centered on the render origin in an orthographic projection sized to the
live backbuffer. The projection is D3D LH orthographic, identity world and view, built fresh each
frame in the render callback.

### 3.1 Coordinate conventions

The render operates in a **centered orthographic space** where the screen centre is `(0, 0)`. All
vertex positions are in **live screen pixels**, derived from the backbuffer dimensions at
construction:

```
halfW = screenW * 0.5
halfH = screenH * 0.5
```

### 3.2 Vertex table — background quad (centered, full-screen)

Vertex format: `[x, y, z, u, v]`, 20-byte stride, FVF `D3DFVF_XYZ | D3DFVF_TEX1`, drawn as a
triangle strip (2 primitives).

| Vertex | X | Y | Z | U | V |
|---|---|---|---|---|---|
| V0 (top-left) | `−halfW` | `−halfH` | 1.0 | 0.0 | 0.0 |
| V1 (top-right) | `+halfW` | `−halfH` | 1.0 | 1.0 | 0.0 |
| V2 (bottom-right) | `+halfW` | `+halfH` | 1.0 | 1.0 | 0.75 |
| V3 (bottom-left) | `−halfW` | `+halfH` | 1.0 | 0.0 | 0.75 |

> The background samples only the **top 75 %** of the DDS (V clamped to 0..0.75), consistent with
> a 1024×768 design image stored in a 1024×1024 container. The 0.75 value is a recovered float
> constant from the construction path; see `load.md §5A.3`.

The background quad is **drawn unconditionally every frame**, before the gauge, with the chosen
`loading*.dds` bound to texture stage 0. The source rect header registered with the engine driver
is `(0, 0, screenW, screenH)`.

---

## 4. Progress-gauge quad

A **fixed-position** quad authored in **1024×768 design space**, scaled to the live screen by two
scale factors computed once at construction:

```
xScale = screenW / 1024.0
yScale = screenH / 768.0
```

### 4.1 Design-space extents (static, set at construction)

| Edge | Design-space value | Scaled value |
|---|---|---|
| Left X | −499 | `xScale × −499` |
| Right X | −170 | `xScale × −170` |
| Top Y | −363 | `yScale × −363` (fixed; never updated per frame) |
| Bottom Y (full) | −140 | `yScale × −140` (the maximum fill position) |

The gauge spans 329 reference units in X and **223 reference units in Y**. The 223-unit Y span is
the fill magnitude: at 100 % load progress the bottom edge reaches `yScale × −140`, exactly the
ctor's static bottom limit.

### 4.2 UV sub-rect (static)

The gauge samples a **lower band** of the same `loading*.dds`. The UV constants are recovered
float literals from the construction path (see `load.md §5A.3`):

| UV coordinate | Value |
|---|---|
| U left | ≈ 0.7539 |
| U right | ≈ 0.9688 |
| V top | ≈ 0.4326 |
| V bottom (full) | ≈ 0.9688 |

The V bottom corresponds to the animated bottom vertex; V top is fixed. The U extents are fixed
throughout (the fill is vertical, not horizontal).

### 4.3 Static vertex table — gauge quad at construction

Vertex format: `[x, y, z, u, v]`, 20-byte stride, same FVF as the background quad.

| Vertex | X | Y | Z | U | V |
|---|---|---|---|---|---|
| V0 (top-left) | `xScale × −499` | `yScale × −363` | 1.0 | ≈ 0.7539 | ≈ 0.4326 |
| V1 (top-right) | `xScale × −170` | `yScale × −363` | 1.0 | ≈ 0.9688 | ≈ 0.4326 |
| V2 (bottom-right) | `xScale × −170` | `yScale × −140` | 1.0 | ≈ 0.9688 | ≈ 0.9688 |
| V3 (bottom-left) | `xScale × −499` | `yScale × −140` | 1.0 | ≈ 0.7539 | ≈ 0.9688 |

V2 and V3 (the bottom pair) are overwritten each frame by the fill formula (§4.4).

---

## 5. Per-frame fill formula

The render callback runs at approximately **10 FPS** (throttled by a `Sleep(100 ms)` per frame).
On each frame, after drawing the background, it queries the VFS load progress and — if progress
is non-zero — computes the fill and redraws the gauge.

```
progress  = VFS_GetProgress()                    // 0..100 integer (truncating quotient)

fill      = min(223 * progress / 100, 223)       // reference units; integer arithmetic; clamped

bottom_y  = (yScale * −363) + (fill * yScale)   // top_y fixed; bottom grows downward
v_bottom  = 0.4326 + (fill / 1024.0)            // V grows from V_top; max = 0.4326 + 223/1024
                                                 //   = 0.4326 + 0.2178 ≈ 0.6504
                                                 // (Note: the max v_bottom at fill=223 does NOT
                                                 //  reach the ctor's 0.9688 — that constant is
                                                 //  the UV of the full static bottom at init, not
                                                 //  the animated ceiling; see load.md §5A.4)
```

Only the **bottom two vertices** (V2, V3) are mutated each frame:

| Field updated | V2 (bottom-right) | V3 (bottom-left) |
|---|---|---|
| Y | `bottom_y` | `bottom_y` |
| V | `v_bottom` | `v_bottom` |

X and U remain fixed; V0 and V1 (top pair) are never touched after construction.

> **Axis note.** The fill grows the gauge **downward from a fixed top edge** (top Y = `yScale×−363`,
> constant). As `fill` increases from 0 to 223, `bottom_y` descends from `yScale×−363` (empty,
> zero-height quad) toward `yScale×−140` (full, 223-ref-unit height). This is a **vertical
> top→down fill** — the X extents and U texcoord do not change. The earlier dirty-room reading
> of a horizontal left→right fill (right-edge X animation) is superseded by `load.md §5A.4`.

The gauge is drawn only when `progress > 0`; at `progress == 0` the second quad call is skipped
entirely.

---

## 6. Render-state setup (per frame)

Applied by the render callback before drawing either quad:

- Orthographic LH projection sized to `(screenW, screenH)`, near 0.0, far 1.0.
- Identity world transform and view transform.
- Z-write and Z-test disabled; back-face culling off; alpha blend on.
- Background DDS bound to texture stage 0 (rebound once, shared by both quads).
- Both quads drawn as `DrawPrimitiveUP(TriangleStrip, 2 primitives)`.

No render-state changes occur between the two quad draws (both use the same texture and the
same blend configuration).

---

## 7. No-text guarantee

The Load scene renders **zero text**. No string-table lookup (`msg.xdb`), no CP949 format call,
no percentage label, no font-slot bind. Any "loading…" wording visible to the player is baked
into the background DDS artwork. See `load.md §5A.5`.

---

## 8. Godot implementation reference

The passive presentation lives in:

`05.Presentation/MartialHeroes.Client.Godot/Ui/Scenes/Load/LoadingWindow.cs`

Godot-specific constraints:
- `.tscn` script binding must be a **property line** (`script = ExtResource("…")` under the node
  header — never an inline header attribute).
- Inside `namespace MartialHeroes.Client.Godot.*`, use `global::Godot.Time`, `global::Godot.Input`,
  `global::Godot.Environment` to avoid namespace collisions.
- Never use `GltfDocument.AppendFromBuffer` — build geometry via `ArrayMesh` directly.
- Layer 05 is **strictly passive** — it renders the fill value reported by `LoadOrchestrator`; it
  never computes progress, never drives the worker, never chooses the post-load route. The
  `SceneStateMachine` (not `LoadScene`) chooses state 2→3 vs 2→4.

**Fidelity checklist for the render geometry:**

- [ ] Background quad: full-screen, `rand() % 3` selects one of the three DDS paths, bound to
      texture stage 0, drawn every frame.
- [ ] Background UV: V clamps to 0..0.75 (the 1024×768 design region of the 1024×1024 DDS).
- [ ] Gauge quad: design X [−499, −170] × design Y [−363, −140] scaled by `(screenW/1024,
      screenH/768)`; same texture as the background, UV sub-rect U ≈ [0.7539, 0.9688],
      V ≈ [0.4326, ?].
- [ ] Fill is **vertical top→down**: only `bottom_y` and `v_bottom` of V2/V3 are updated each
      frame; X extents and U texcoord are constant.
- [ ] Fill magnitude: `fill = clamp(223 × progress / 100, 0, 223)` reference units; integer
      arithmetic; at `fill == 0` the gauge quad is not drawn.
- [ ] SFX `920100100` plays looping from construction; does not double-stack.
- [ ] No text rendered; no buttons or interactive widgets built.

---

## 9. Open items (debugger-pending)

Carried forward from `load.md §9`:

1. **Vertex stride byte-exactness.** FVF `0x102` (`D3DFVF_XYZ | D3DFVF_TEX1`) implies 20 bytes
   per vertex (`[x(4), y(4), z(4), u(4), v(4)]`), consistent with the static recovery. A live
   vertex-block dump at the `DrawPrimitiveUP` call would confirm the stride and field offsets.
2. **DDS pixel dimensions.** 1024×1024 is inferred from the V = 0.75 constant; not byte-confirmed.
3. **Second load-pass replay.** Whether the in-world reload re-runs the full gauge animation or
   short-circuits (near-instant) is unconfirmed. See `load.md §9` item 1.

---

## 10. Sources

- `Docs/RE/scenes/load.md` — the full Load scene dossier: §5 (UI architecture overview), §5A.0
  (no `GUComponent` tree), §5A.2 (build sequence, scale factor computation), §5A.3 (asset
  linkages and UV constants), §5A.4 (vertical fill correction, fill formula, render-state
  setup, completion logic), §5A.5 (no-text guarantee), §9 (open items).
- `Docs/RE/specs/resource_pipeline.md` — §2.3 (BG `rand()%3`, SFX 920100100, ≈10 FPS) and
  §2.4 (progress meter, 9,395,240 denominator, integer quotient).
- `Docs/RE/specs/frontend_layout_tables.md` §5 — loading-screen layout (BG + fill-bar geometry,
  cue, grace) as wired in `LoadingWindow`.
- `Docs/RE/specs/ui_system.md` — §5 (scene machine, GameState 2 = Load), §6 (font system;
  inherited but unused by Load).
- `Docs/RE/structs/gucomponent.md` — the `GUComponent` widget base the Load screen deliberately
  does not use (§5A.0 / §5A.6 of `load.md`).
