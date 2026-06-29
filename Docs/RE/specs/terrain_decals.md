# Spec: terrain_decals — ground-overlay and gameplay-decal systems — RESOLVED-NEGATIVE

> Clean-room spec. Neutral description only — NO sample bytes, NO decompiler pseudo-code,
> NO binary addresses. This spec supersedes the PARTIAL placeholder (synthesized from sibling
> specs in the absence of direct IDA recovery); the deep-3d wave4 pass constitutes the
> authoritative first-pass analysis of this subsystem. Cross-references to the sibling specs
> (terrain-streaming, whole_map_assembly, entity_placement) remain valid for their own domains.

<!--
verification:
  confirmed:
    - doida.exe contains no gameplay terrain-decal subsystem: no projected-texture decal
      path, no selection ring, no AoE ground indicator, no footprint, no blood/scorch mark,
      no decal lifetime/scheduler, no decal accumulation, and no decal geometry loader.
      Proven by exhaustive string + function-name + cross-reference sweep (§2).
    - The only ground-projected flat geometry is the per-actor blob-shadow quad system
      (ActorShadow_DrawBlobQuads), which projects a textured TRIANGLEFAN quad at each
      actor's feet — confirmed by static control-flow and operand analysis (§3).
    - Blob-shadow vertex format: FVF 0x102 (D3DFVF_XYZ | D3DFVF_TEX1), stride 20 bytes
      (position 12 B + UV 8 B per vertex); 4 vertices per actor, 2 primitives per call.
    - Blob-shadow quad is axis-aligned horizontal, side length 2r (r = per-actor f32 shadow
      radius from AnimCatalog entry at offset +20), Y = actor world Y + 0.4.
    - Blob shadow drawn last in RenderPass_OpaqueWorld immediately before
      RenderPass_TransparentAndParticles; render states: SRCBLEND=SRCALPHA(5),
      DESTBLEND=INVSRCALPHA(6), AlphaTestEnable ON, ZWriteEnable OFF, ZEnable ON,
      Lighting OFF, Fog OFF; textured with data/effect/tex/shadow.dds.
    - Shadow-projector singleton mode flag at struct offset +312: value 3 → blob for local
      player only; any other value → blob for every visible battle actor (in addition to the
      dynamic projected shadow map).
    - ShadowManager_Init loads data/effect/tex/shadow.dds into the projector singleton at
      offset +0x54; allocates the dynamic shadow render-to-texture surface (quality-tiered,
      skipped when mode flag == 3). Feature gated by OPTION_SHADOW.
    - Red herrings fully eliminated: DISPLAY_GLOW_RANGE_X/Y (HUD display config),
      FX Target Select strings (embedded editor dialog), Select move place (chat-log
      broadcast), and d%sx%dz%d.bmp path (per-cell minimap) all confirmed unrelated to
      gameplay decals (§2.2).
    - The FX terrain overlay classes Fx1Terrain–Fx7Terrain (sub-manager slots 2–8 in the
      terrain cell object, documented in terrain-streaming.md §11.2) and the extra-decal
      grid at cell struct offset +16332 (documented in whole_map_assembly.md §7) are
      terrain-streaming and building-placement fields, not a gameplay decal system. Their
      authoritative specs remain terrain-streaming.md and whole_map_assembly.md respectively.
    - BuildingFar_CullAndDraw (documented in entity_placement.md §8.8) is the
      building-footprint far-blend pass, not a gameplay decal path.
  static-hypothesis: []
  capture/debugger-pending:
    - Visual appearance of shadow.dds (assumed radial soft-circle silhouette; confirm against
      a real frame capture).
    - Which OPTION_SHADOW / graphics-quality tier value drives mode flag +312 to 3 (blob-only)
      vs dynamic shadow map at runtime.
    - Per-corner UV values in the 4-vertex blob quad (assumed full-range 0..1 across the quad).
    - That the battle-actor blob loop fires for non-local actors under live play at mode != 3.
  ida_reverified: 2026-06-28    # deep-3d wave4 (f61f66a9): exhaustive string + function-name
    # + cross-reference sweep; ActorShadow_DrawBlobQuads fully recovered by static analysis;
    # all candidate strings ruled out as red herrings; RESOLVED-NEGATIVE for gameplay decals
  ida_anchor: f61f66a9ae0ec1e946105b2ecff76e8930cb1d1367df64e5688a5266f5ad9963
  readiness: RESOLVED-NEGATIVE for gameplay terrain decals — no implementation required.
    Blob-shadow system is IMPLEMENTATION-READY (vertex format, geometry, render states,
    pass ordering, texture asset, and mode flag all confirmed). The four debugger-confirm
    items are optional refinements that do not block a faithful port.
  evidence: [static-ida]
  conflicts: Placeholder spec (PARTIAL, synthesized from sibling committed specs without
    direct IDA recovery) classified terrain overlay and decal rendering as a confirmed present
    subsystem and described the fx1..fx7 FX overlay layers, BuildingFar_CullAndDraw, and the
    extra-decal cell offset (+16332) as components of a terrain-decal system. The deep-3d
    wave4 analysis finds no gameplay terrain-decal code path; those structures belong to
    terrain-streaming.md, entity_placement.md, and whole_map_assembly.md (unchanged). This
    spec now records the definitive-negative verdict and the one real ground-overlay mechanism
    (actor blob shadow).
-->

---

## Status block

| Attribute | Value |
|---|---|
| `status` | **RESOLVED-NEGATIVE** — no gameplay terrain-decal subsystem; the one real ground-overlay mechanism (actor blob shadow) is IMPLEMENTATION-READY |
| `binary_analysed` | `doida.exe` (legacy client) — exhaustive string scan, function-name search, and cross-reference sweep (§2); `ActorShadow_DrawBlobQuads` fully recovered by static control-flow and operand analysis (§3) |
| `confidence` | **STATIC-CONFIRMED** — absence of gameplay decals proven by exhaustive sweep; blob-shadow geometry, render states, and pass position confirmed at control-flow + operand level |

---

## 1. Subsystem status

**RESOLVED-NEGATIVE for gameplay terrain decals.** The `doida.exe` client contains no projected-texture or decal-geometry machinery for spell/skill ground circles, footprints, blood splats, scorch marks, persistent selection rings, or AoE ground indicators. There is no decal accumulation, no decal lifetime or scheduler, and no decal asset loader. The terrain-overlay classes documented in `Docs/RE/specs/terrain-streaming.md` (fx1..fx7 animated texture-blend layers) and the building-footprint blend path in `Docs/RE/specs/entity_placement.md` (`BuildingFar_CullAndDraw`) are terrain-streaming and building-placement mechanisms, not a gameplay decal system. Any visual ground circle in gameplay content would ride the generic effect system (`data/effect/xeff/`, `xeffect.lst`) at a world position — a separate lane with no terrain-decal-specific code path.

The **single ground-projection mechanism** that exists is the **actor blob-shadow** (`ActorShadow_DrawBlobQuads`): a flat axis-aligned horizontal quad textured with `data/effect/tex/shadow.dds`, drawn at each actor's feet as the last draw in the opaque pass. This is the engine's entire "ground overlay" repertoire. It is fully recovered in §3 and is IMPLEMENTATION-READY.

This spec supersedes the PARTIAL placeholder. See §2 for the evidence sweep and §5 for reimplementation guidance.

---

## 2. Evidence of absence

### 2.1 String scan (whole image, case-insensitive)

| Pattern | Hits | Notes |
|---|---|---|
| `decal`, `splat`, `footprint` | 0 | No matches |
| `blood`, `scorch` | 0 | No matches |
| `Ring` (gameplay) | 0 gameplay | Only terrain cell-streaming ring and `std::string` library noise; no gameplay ring |
| `selection` | 0 gameplay | Only CxImage library noise (`CxImage::SelectionSet …`) |
| `projector` (texture-projection) | 0 | No matches |
| `Circle` | 0 | No matches |

### 2.2 Function-name search

| Pattern searched | Result |
|---|---|
| `Decal` | **0 functions** |
| `Circle` | **0 functions** |
| `Projector` | **0 functions** |
| `Ring` | Only terrain cell-streaming ring functions and `std::string` library noise; no gameplay ring function |
| `Shadow` | 5 functions — all belong to the actor-shadow system (§3); no decal function |
| `Blob` | Terrain and building cell loaders + `ActorShadow_DrawBlobQuads` + RSA key-blob handler; no decal function |

### 2.3 Ground-texture asset strings

The only ground-projected texture asset found in the binary string table under `data/effect/` is `data/effect/tex/shadow.dds` (the blob-shadow texture, §4). No decal atlas, no circle or ring texture, and no AoE indicator texture was found:

| String pattern | Hits | Notes |
|---|---|---|
| `data/effect/tex/shadow.dds` | 1 | The actor blob-shadow texture |
| Any circle / ring / AoE / decal texture path | 0 | No matches |

### 2.4 Red herrings eliminated

The following strings and functions appear superficially related to ground overlays but are confirmed unrelated:

| String / context | Actual role | Evidence |
|---|---|---|
| `DISPLAY_GLOW_RANGE_X`, `DISPLAY_GLOW_RANGE_Y`, `DISPLAY_GLOW_BRIGHT_MULTI` | HUD and screen-glow display-config keys, read only by `DisplayConfig_ParseFramerate` | No geometry or terrain reference |
| `FX Target Select`, `FX Target Select point Error`, `FX isn't existed in Terrain`, `Select FX and Try Initialize` | Strings inside an embedded FX/terrain **editor dialog** compiled into the client; call sites use OS message-box and dialog-item APIs | Not the runtime renderer; not gameplay decals |
| `Select move place` | Chat-log and notice-broadcast prompt for the click-to-move command (`SystemText_BroadcastToChatLogAndNotice`); no geometry | Chat UI, not a ground marker |
| `data/effect/map/d%sx%dz%d.bmp` | Per-cell minimap BMP path | Minimap, not a decal |

---

## 3. The actor blob-shadow system (the one real ground-overlay mechanism)

The actor blob shadow is the only mechanism in the client that projects a textured flat quad onto the ground. It is fully recovered by static analysis. The dynamic projected shadow map (render-to-texture actor silhouettes blended onto terrain via multitexture stage 1) is a distinct form of actor shadowing documented in `Docs/RE/specs/terrain-streaming.md §11.8`; it is not re-derived here.

### 3.1 Pass ordering

`ActorShadow_DrawBlobQuads` is the **last draw call in `RenderPass_OpaqueWorld`**, placed immediately before `RenderPass_TransparentAndParticles` (billboards, particles, lens flare). Within `RenderPass_OpaqueWorld` the sequence is:

1. Terrain, buildings, sky, and environment drawn.
2. Alpha-blend and sampler states configured for the blob pass.
3. `ActorShadow_DrawBlobQuads` — blob shadows drawn.
4. `RenderPass_TransparentAndParticles` — transparent and particle pass follows.

The shadow-projector singleton that owns the dynamic shadow-map render target (documented in `Docs/RE/specs/terrain-streaming.md §11.8.1`) also carries the blob enable byte and the blob texture pool entry. If the blob enable byte is not set, the function returns immediately without drawing.

### 3.2 Mode flag and actor iteration

The shadow-projector singleton carries a mode flag at struct offset **+312**. This field selects between two draw strategies:

| Mode flag value | Behaviour |
|---|---|
| 3 (blob-only / low quality) | Draw one blob quad for the **local player** (`g_LocalPlayer`) only |
| Any other value (dynamic-shadow quality) | Iterate the battle-actor list (via `BattleController` singleton list-head at offset +96 and the `ActorVisualGlobal` singleton); draw one blob quad for **each visible battle actor** (gated on actor visibility flags at actor struct offsets +0x54E and +0x720), in addition to the dynamic projected shadow map |

The feature is gated by the `OPTION_SHADOW` config key. When the shadow feature is disabled, the blob enable byte is clear and no blob quads are drawn.

### 3.3 Blob quad geometry

Each blob quad is a 4-vertex axis-aligned horizontal quad centered on the actor's world XZ position. The quad does not follow terrain slope; it sits at a fixed height with a constant z-bias.

| Parameter | Value |
|---|---|
| Actor world position source | Actor struct offsets +0x428 (X float), +0x42C (Y float), +0x430 (Z float) |
| Shadow radius `r` | f32 at `AnimCatalog_LookupByKey` result entry, struct offset **+20** (per-actor value) |
| Quad height | Actor world Y + **0.4** (constant z-bias; lifts the quad just above the ground plane) |
| Quad footprint | 2r × 2r, axis-aligned; NOT oriented to terrain slope |
| Vertex order | `D3DPT_TRIANGLEFAN`, 4 vertices, 2 primitives |
| Corner layout | V0 = (X−r, Y+0.4, Z−r); V1 = (X+r, Y+0.4, Z−r); V2 = (X+r, Y+0.4, Z+r); V3 = (X−r, Y+0.4, Z+r) |

### 3.4 Vertex format and draw call

| Attribute | Value |
|---|---|
| FVF constant | **0x102** (`D3DFVF_XYZ \| D3DFVF_TEX1`) |
| Vertex stride | **20 bytes** (position 12 B + UV 8 B; no normal, no diffuse) |
| UV layout | Per-corner UV at bytes +12..+19 of each 20-byte vertex (covers shadow.dds 0..1 across the quad — `[debugger-confirm]`) |
| Draw call | `DrawPrimitiveUP(D3DPT_TRIANGLEFAN, PrimitiveCount=2, Stride=20)` — one call per actor |

### 3.5 Render-state recipe

The following device states are active for the blob-shadow draw. They are configured by `RenderPass_OpaqueWorld` prior to invoking `ActorShadow_DrawBlobQuads`:

| Render state | Value | D3D9 enum code |
|---|---|---|
| AlphaBlendEnable | ON | — |
| SrcBlend | `D3DBLEND_SRCALPHA` | 5 |
| DestBlend | `D3DBLEND_INVSRCALPHA` | 6 |
| AlphaTestEnable | ON | — |
| ZWriteEnable (depth write) | **OFF** | — |
| ZEnable (depth test) | ON | — |
| Lighting | OFF | — |
| Fog | OFF | — |
| Stage-0 texture | `shadow.dds` handle (from texture pool at projector singleton offset +0x54) | — |
| Stage-0 MIPFILTER | `D3DTEXF_LINEAR` | 2 |

Net effect: a soft alpha-blended dark silhouette, depth-tested but not depth-writing, drawn flat at each actor's feet. `shadow.dds` supplies the radial dark-centre/alpha-edge silhouette shape.

---

## 4. Texture asset

| Asset path | Role | Notes |
|---|---|---|
| `data/effect/tex/shadow.dds` | The single blob-shadow texture, loaded by `ShadowManager_Init` into the shadow-projector singleton at offset +0x54 | The only ground-projected texture asset in the binary; no decal atlas, no circle/ring/AoE texture exists |

`ShadowManager_Init` loads `shadow.dds` and also allocates the dynamic shadow render-to-texture surface (not re-derived here; see `Docs/RE/specs/terrain-streaming.md §11.8.1`). The surface allocation step is skipped when the mode flag equals 3 (blob-only tier). Quality-tier selection is sourced from the graphics-quality singleton at field offset +10.

---

## 5. Reimplementation guidance

No gameplay terrain-decal subsystem exists, so there is nothing to re-implement for selection rings, AoE circles, footprints, blood splats, or scorch marks. Any such visual in a Godot re-creation is a free engineering choice using the Godot effect system — there are no legacy-derived parameters to honour.

The actor blob-shadow is the only ground-overlay with a defined legacy implementation. All confirmed parameters are implementation-ready:

| Parameter | Legacy value | Status |
|---|---|---|
| Vertex format | FVF 0x102, stride 20 B | CONFIRMED |
| Quad geometry | 2r × 2r axis-aligned, Y = actorY + 0.4, TRIANGLEFAN | CONFIRMED |
| Shadow radius source | `AnimCatalog` lookup entry offset +20 (per-actor f32) | CONFIRMED |
| Blend states | SRCALPHA / INVSRCALPHA | CONFIRMED |
| Depth write | OFF (depth test ON) | CONFIRMED |
| Lighting / fog | OFF | CONFIRMED |
| Texture asset | `data/effect/tex/shadow.dds` | CONFIRMED |
| Feature gate | `OPTION_SHADOW` | CONFIRMED |
| Mode flag (projector offset +312) | 3 = local-player-only blob; else all visible battle actors | CONFIRMED |
| UV layout per corner | Assumed full 0..1 across quad | `[debugger-confirm]` — optional refinement |
| Visual appearance of shadow.dds | Assumed radial soft circle | `[debugger-confirm]` — confirm against a real frame |
| Quality value that sets mode == 3 | Not yet traced to specific tier string | `[debugger-confirm]` — optional |
| Blob loop under live play with other actors | Static branch is clear; runtime not yet observed | `[debugger-confirm]` — optional |

None of the four debugger-confirm items block a faithful port of the blob-shadow system.

The FX terrain overlay layers (fx1..fx7), the building far-blend pass, and the extra-decal cell grid are each documented in their authoritative specs (`Docs/RE/specs/terrain-streaming.md`, `Docs/RE/specs/entity_placement.md`, `Docs/RE/specs/whole_map_assembly.md`). They are terrain-streaming and building-placement structures, not gameplay-decal code, and are unaffected by this finding.

---

## 6. Cross-references

| Spec | Section | Relation |
|---|---|---|
| `Docs/RE/specs/terrain-streaming.md` | §11.8, §11.8.1, §11.8.2 | Dynamic projected actor shadow map — the other shadow form; the blob pass documented here is its companion draw |
| `Docs/RE/specs/water.md` | entire | Sister RESOLVED-NEGATIVE spec; same evidence methodology |
| `Docs/RE/specs/whole_map_assembly.md` | §7 | Extra-decal grid (cell offset +16332) and up-terrain layer (cell offset +20436) — terrain-streaming domain, not gameplay decals |
| `Docs/RE/specs/terrain-streaming.md` | §11.2 | FX overlay sub-manager slots 2–8 (fx1..fx7) — terrain-streaming domain |
| `Docs/RE/specs/entity_placement.md` | §8.8 | `BuildingFar_CullAndDraw` — building-placement domain |
