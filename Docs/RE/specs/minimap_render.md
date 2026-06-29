# Spec: minimap_render — minimap rendering method — STATIC-CONFIRMED

> Clean-room spec. Neutral description only — NO sample bytes, NO decompiler pseudo-code,
> NO binary addresses. This is the dedicated record for the minimap rendering method.
> Blip data, asset paths, and layout details are covered in `Docs/RE/specs/minimap.md`.

<!--
verification:
  confirmed:
    - all three map surfaces (corner radar MapPanel, tiled big-map TotalMapPanel,
      full-screen BroodWarMapInfoPanel) render by loading pre-baked image files
      (BMP tiles or DDS) as Diamond::GHTex file-backed texture handles and blitting
      them through the 2D GUI toolkit draw slot; no render-target switch, no secondary
      camera, no scene submission, and no terrain geometry appear anywhere on any of the
      three draw paths or their loaders
    - Diamond::GHTex is a deferred file-backed texture handle (asset path at struct
      offset +4, codec hint at +60, pool/scheduler link at +36); it is not a render-target
      object and is used identically for minimap tiles and all other GUI textures
    - every minimap asset path has consumers only and no in-client producer — the
      per-cell BMP loader (MapPanel draw and TotalMapPanel draw) and the DDS loaders
      (per-area map and overview) all read from disk; nothing writes a rendered result
      to any of these paths; this eliminates any render-to-texture tile-generation stage
    - asset paths confirmed: per-cell BMP tiles data/effect/map/d%sx%dz%d.bmp (codec
      hint 0 = uncompressed BMP); per-area full DDS data/ui/map/map%d.dds (FourCC
      844388420 = "DXT2"); overview DDS data/ui/broodwarmap.dds (FourCC 844388420 = "DXT2")
    - per-cell BMP tiles are absent from the shipped VFS (established in
      specs/minimap.md §3.2/§6.1); the load path is present and genuine, but against
      shipped data the background falls to a default fill
    - blip overlay: world-relative actor displacement projected to screen via x0.125 +
      66.5 (radar) / render-target-centred x0.125 + pan (big-map); cull range 0..133 px;
      class byte at actor struct +96 selects texture group; drawn via GUI draw slot
    - player arrow: heading derived from quaternion at actor struct +1076;
      D3DXMatrixRotationZ builds a Z-rotation; D3DXMatrixTranslation centres the hotspot
      (radar: -6,-6; big-map: -32,-32); D3DXMatrixMultiply writes the result into the
      element's 4x4 UI transform at element struct +68; drawn via GUI draw slot — the
      only Direct3D symbols on the entire minimap path are these CPU matrix helpers
    - big-map pan: keyboard action codes 1000/1001 (Y-axis, minus/plus) and 1002/1003
      (X-axis, plus/minus) update pan state at panel members +216 (pan X) and +220
      (pan Y) by +/-25 px per frame; projection applies a fixed 0.125 (1:8) scale
      centred on render-target dimensions, plus the pan offset; no per-pixel zoom
      multiply was found anywhere on the draw or projection code path
  static-hypothesis: []
  capture/debugger-pending:
    - player-arrow rotation handedness (CW vs CCW vs world Z-negation) — confirm live
      to avoid a mirrored arrow in the port; carried from specs/minimap.md §3.5
    - whether actor struct +1064 (world XZ source for the player position read in both
      draw functions) is the last-received network vec3 or a post-integration value
      (snapshot semantics affect blip lag)
    - blip texture-group id to DDS file mapping — gated on the UI texture manifest, not
      on the render method; does not affect the method verdict
  ida_reverified: 2026-06-28    # deep-3d wave 6 (f61f66a9): full callee enumeration
    of all three map-surface draw functions and two DDS loaders; producer/consumer
    cross-reference audit on all minimap asset paths; Diamond::GHTex struct confirmed;
    baked-2D-blit verdict is definitive
  ida_anchor: f61f66a9ae0ec1e946105b2ecff76e8930cb1d1367df64e5688a5266f5ad9963
  readiness: STATIC-CONFIRMED — rendering method is fully settled; three items remain
    for live debugger confirmation but none alter the method verdict or the Godot port
    structure
  evidence: [static-ida]
  conflicts:
    - specs/minimap.md §5.6 claims an integer zoom factor in two member fields giving
      a x8 step; the deep-3d wave 6 draw/projection path audit found only a fixed 0.125
      (1:8) scale plus a render-target-sized viewport and keyboard pan with no per-pixel
      zoom multiply; the big-map should be described as fixed-scale, pan-only, and
      viewport-sized unless a zoom multiply is confirmed in an input or update handler;
      this spec supersedes the §5.6 zoom claim pending that confirmation
    - terrain_decals.md described the per-cell BMP path as a "red herring"; this is
      refined: the path is a genuine live minimap asset load in the draw code, not
      decal data; it is a "red herring" only in the sense that the asset files are
      absent from the shipped VFS, so against real data the background draws as a
      default fill — not decal data, not a 3D render
-->

---

## Status block

| Attribute | Value |
|---|---|
| `status` | **STATIC-CONFIRMED** — baked-image 2D blit + procedural 2D sprite overlay; no 3D render-to-texture, no render-target switch, no scene submission |
| `binary_analysed` | `doida.exe` (legacy client) — full callee enumeration of all three map-surface draw functions and both DDS loaders; producer/consumer cross-reference audit on all minimap asset paths |
| `confidence` | **STATIC-CONFIRMED** — render method is settled at control-flow + operand level; three items remain for live debugger confirmation (§6.2) but do not alter the method verdict |

---

## 1. Verdict

**STATIC-CONFIRMED.** The minimap is rendered exclusively as a **2D blit of pre-baked image files** (BMP tiles and DDS panel backgrounds) decorated with a **procedural 2D sprite overlay** (actor blips and a rotated player-arrow sprite). It is:

- **NOT** a live 3D top-down render-to-texture of the world geometry.
- **NOT** a procedural draw of region or terrain geometry.

All three map surfaces use the same baked-image method. The only Direct3D symbols on any minimap code path are the D3DX CPU matrix helpers (`D3DXMatrixRotationZ`, `D3DXMatrixTranslation`, `D3DXMatrixMultiply`) used to rotate the player-arrow sprite inside its 2D UI transform. None of these operations submit geometry or modify the device render target.

The definitive negative proof is that every minimap asset path has **consumers only and no in-client producer**: the tile-loading code in the corner-radar draw function and the tiled-big-map draw function, and the two DDS loaders for the full-screen map, all read from disk. Nothing in the client renders the 3D world top-down to produce those files. A render-to-texture tile-generation stage does not exist.

---

## 2. Three map surfaces

All three surfaces share a common architecture: construct a `Diamond::GHTex` file-backed texture handle for each background tile or panel image, resolve it to a Direct3D 9 texture, bind it into a 2D GUI image element, and call the element's draw slot to produce an alpha-blended textured-quad blit. Variation between surfaces is limited to asset type and tile/panel dimensions.

### 2.1 Corner radar (`MapPanel`)

The `MapPanel` draw function:

1. **Early-out** on the surface's own visible flag and its parent's visible flag.
2. **Player position** is read from the local-player actor at struct offsets +0x428 (world X) and +0x430 (world Z). When no local player is active, a global zero-vector fallback is substituted.
3. **Cell index** is computed from world position: `cell = ((int)(world + 20480.0) >> 10) + 9980`. Sub-cell scroll for mosaic alignment: `subPix = (int)(world × 0.125) − 66`.
4. **2×2 tile loop** builds each per-cell BMP filename (`data/effect/map/d%sx%dz%d.bmp`, with an area-tag prefix and the cell X/Z indices). For each tile:
   - A per-panel string-keyed texture cache (sorted list at panel struct +196/+200) is checked. On a miss a `Diamond::GHTex` is constructed (codec hint 0 = uncompressed BMP) and inserted.
   - The texture handle is resolved to a Direct3D 9 texture via the texture-pool handle resolver; on failure the panel's default texture (member +208) is used.
   - The resolved texture is bound into a 2D GUI image element at panel member +248 (texture field at element +144), the element's screen rect and clip are set via `GuiControl_SetRect`, and the element's draw slot is called: **2D textured-quad blit**.
5. **Actor blip overlay**: the active actor list is walked. Per actor, the world-relative displacement from the player is projected to screen (×0.125 + 66.5), culled to the range 0..133 px. The actor's class byte at struct +96 selects a blip texture group (values 1, 2, 3). The texture is bound into the blip image element at panel member +256 and drawn via the draw slot: **2D sprite**.
6. **Party and GPS blips**: same projection constants and 2D sprite draw.
7. **Player arrow** (panel member +264): the player's heading is derived from the quaternion at actor struct +1076. `D3DXMatrixRotationZ` produces a heading rotation; `D3DXMatrixTranslation(−6, −6, 0)` centres the hotspot; `D3DXMatrixMultiply` combines them into the element's 4×4 UI transform at element struct +68. The draw slot renders the result: **2D rotated sprite, no 3D**.
8. **Footer labels**: player world X/Z and the area name (region record state enum at field +0x28, switching to message-db entries 35001 / 35002 / 35003) are written to GUI label elements. Area-1 sub-region-12 triggers jingle sound 910001000.

### 2.2 Tiled big-map (`TotalMapPanel`)

The `TotalMapPanel` draw function applies the same baked-BMP tile path and `Diamond::GHTex` / handle-resolver / `GuiControl_SetRect` / draw-slot pipeline through a single reused image element (panel member +204), repositioned per tile with each tile occupying a 128×128 px rect. Key differences from the radar:

- **Keyboard pan**: the input manager is polled for action codes 1000/1001 (Y-axis, minus/plus) and 1002/1003 (X-axis, plus/minus). Each active code adjusts pan state at panel members +216 (pan X) and +220 (pan Y) by ±25 px per frame.
- **Tile-window extent**: (render-target width ÷ 256) tiles wide × (render-target height ÷ 256) tiles tall, providing a viewport-sized grid (256 = 2 × 128 px per tile pair). This determines how many cells are visible; it is not a zoom.
- **Projection** (big-map projection helper): `screenX = 0.5 × renderTargetW + (worldX − playerX) × 0.125 + panX`; `screenZ = 0.5 × renderTargetH + (worldZ − playerZ) × 0.125 + panZ`. Cell index with pan: `cell = ((int)(world − 8 × pan + 20480) / 1024) + 9980`. The scale is the same fixed 0.125 as the radar. No per-pixel zoom multiply was found anywhere on the draw or projection code path.
- **Player arrow** (panel member +208): same D3DX matrix rotation, but with a 64×64 px hotspot centred via `D3DXMatrixTranslation(−32, −32, 0)`.

### 2.3 Full-screen map (`BroodWarMapInfoPanel` and related)

Two distinct loaders provide the DDS backgrounds:

**Per-area background loader**: keyed on the current area identifier, formats the path `data/ui/map/map%d.dds`, constructs a `Diamond::GHTex` with FourCC 844388420 ("DXT2"), and binds the texture handle into GUI image elements at panel members +49, +50, and a child element list at +52 (texture field at element +144). The element's refresh slot completes the bind. On load failure a fallback fill element (panel member +48) / null is used.

**Overview loader**: loads `data/ui/broodwarmap.dds` (FourCC "DXT2"), traverses the `Diamond::GUPanel` / `GUComponent` child hierarchy via type-safe downcasting, and binds the texture handle to every non-label child element at element texture field +144. Pure 2D panel background.

**Static pin markers**: 15×15-pixel sprite markers drawn from a 40-byte-per-record marker table. These are static 2D image components at authored pixel positions — no projection, no transform. Fully covered in `Docs/RE/specs/minimap.md §5.4/§5.7`.

---

## 3. Texture-handle type: `Diamond::GHTex`

`Diamond::GHTex` is a **deferred file-backed texture handle**, not a render-target object. Relevant struct fields:

| Field offset | Size | Content |
|---|---|---|
| +4 | string | Asset file path (copied from the constructor argument) |
| +36 | 4 B | FourCC / scheduler flag |
| +60 | 4 B | Render-type / codec hint (`g_RenderObjType_Static` for BMP; 844388420 = "DXT2" for DDS) |

The handle is registered with the `Diamond::GHTex` pool/scheduler. Resolution to a live Direct3D 9 texture pointer occurs via the texture-pool handle resolver; on failure the caller substitutes a default texture. This same handle type is used identically for minimap tiles and all other GUI textures — it is a standard deferred-load mechanism, not a render-target allocation.

This confirms the complete pipeline for every minimap background: **baked image file → Direct3D 9 texture → 2D textured-quad blit**.

---

## 4. Low-level constants and math

| Role | Value |
|---|---|
| World-to-screen scale | `0.125` (1/8 — 8 world units per pixel), all surfaces |
| Radar blip centre offset | `+66.5` px |
| Radar mosaic scroll offsets | `−66` / `67` px |
| Cell size | 1024 world units (right-shift 10) |
| Tile-index world bias | `20480.0` (= 20 × 1024) |
| Global cell-index origin | `9980` |
| Minimap pixels per cell tile | 128 px (= 1024 × 0.125) |
| Radar tile window | 2×2 tiles = 256×256 px |
| Big-map tile-window extent | (render-target W ÷ 256) wide × (render-target H ÷ 256) tall |
| Big-map pan step | ±25 px per frame |
| Pan member offsets | panel +216 (pan X), +220 (pan Y) |
| Big-map pan-to-world factor | ×8 (inverse of 0.125) |
| Big-map projection centre | 0.5 × render-target dimensions |
| Player position source | actor struct +0x428 (world X), +0x430 (world Z) |
| Player facing source | actor struct +1076 (quaternion) |
| Arrow hotspot translate | radar (−6, −6, 0); big-map (−32, −32, 0) |
| Blip cull range | 0 to 133 px |
| Blip class selector | actor struct +96 (class byte; values 1, 2, 3) |
| DDS FourCC | 844388420 (= "DXT2") |
| Pan input action codes | 1000/1001 (Y-axis), 1002/1003 (X-axis) |

**Cell-index formula:** `cell = ((int)(world + 20480.0) >> 10) + 9980`

**Radar sub-cell scroll:** `subPix = (int)(world × 0.125) − 66`

**Big-map cell with pan:** `cell = ((int)(world − 8 × pan + 20480) / 1024) + 9980`

---

## 5. Render-state / pass recipe

There is **no minimap-specific Direct3D render pass**. Every background tile and sprite is drawn through the shared in-game 2D GUI toolkit (`Diamond::GU*` controls), riding the engine's existing 2D UI render path (alpha-blended textured quads, the same path used by all HUD widgets).

| Step | Operation |
|---|---|
| 1. Load / cache | `Diamond::GHTex` constructed with the asset path and codec hint; inserted into the panel's texture cache or bound into a DDS panel element |
| 2. Resolve | Texture-pool handle resolver returns the live Direct3D 9 texture pointer; on failure a default-fill texture is substituted |
| 3. Bind | Texture handle stored at GUI image element texture field (+144); screen rect and clip set via `GuiControl_SetRect`; for the rotated arrow only, a 4×4 UI transform is written at element +68 |
| 4. Draw | Element's draw slot → standard 2D alpha-blended textured-quad blit |

The minimap **never** sets a render target, begins a sub-scene, binds a view-projection matrix, or submits world geometry. It is entirely a 2D GUI operation.

---

## 6. Confirmation status

### 6.1 Statically confirmed (this pass — settled)

| Claim | Evidence |
|---|---|
| Render method = baked-image 2D blit + 2D sprite overlay (all three surfaces) | Full callee enumeration of all three draw functions and both DDS loaders |
| No render-target / camera / scene / terrain geometry / depth on any minimap path | Full callee enumeration — only D3DX CPU matrix helpers and GUI toolkit calls present |
| Every minimap asset path: consumers only, no in-client producer | Cross-reference audit on all three asset-path strings |
| `Diamond::GHTex` is a file-backed handle, not a render-target object | Struct field analysis (+4 asset path, +60 codec hint, +36 scheduler) |
| Blip projection constants (×0.125, +66.5, 0..133 cull, class byte +96) | Operand recovery from draw and projection helper code |
| Big-map: ±25 px pan at members +216/+220, fixed 0.125 scale, no zoom multiply found | Operand recovery from `TotalMapPanel` draw and projection functions |
| Player arrow rotated via D3DXMatrixRotationZ into element UI transform (+68) | Callee and operand analysis; hotspot translates confirmed |

### 6.2 Pending live-debugger confirmation

| Item | Why needed for the port |
|---|---|
| Player-arrow rotation handedness (CW vs CCW vs world Z-negation) | Required to avoid a mirrored arrow; carried from `Docs/RE/specs/minimap.md §3.5` |
| Actor struct +1064 (world XZ) — last-received packet vec3 vs post-integration value | Snapshot semantics affect blip position lag |
| Blip texture-group id to DDS filename mapping | Gated on the UI texture manifest; does not affect the method verdict |

---

## 7. Reimplementation guidance

The minimap rendering method is fully specified. A 1:1 port has no 3D render path to reproduce; all minimap content is a 2D UI overlay.

| Component | Original behaviour | Port notes |
|---|---|---|
| Background — corner radar | 2×2 grid of 128×128 px BMP tiles from `data/effect/map/d%sx%dz%d.bmp` | Tiles absent from shipped VFS; port must pre-render top-down cell thumbnails offline or draw blips on a plain background, matching the original fallback |
| Background — tiled big-map | Viewport-sized grid of the same BMP tiles at 128×128 px each | Same absence; fixed 1:8 scale, pan-only, no zoom |
| Background — full-screen map | Single per-area DDS (`data/ui/map/map%d.dds`, DXT2) + overview DDS (`data/ui/broodwarmap.dds`, DXT2) | DXT2 DDS files ship for at least area 1 |
| Scale | Fixed 0.125 (1 px = 8 world units), all surfaces | No zoom mechanism exists on the draw path |
| Pan (big-map only) | ±25 px/frame, keyboard action codes 1000–1003, stored at panel +216/+220 | |
| Blip projection | `screen = centre + (worldDelta × 0.125) + pan`; cull to 0..133 px | |
| Player arrow | Heading from actor quaternion (+1076); Z-rotation in 2D UI transform; hotspot (−6,−6) radar / (−32,−32) big-map | Handedness pending live-debugger confirmation (§6.2) |
| Actor class selector | Class byte at actor struct +96; values 1/2/3 select blip texture group | |
| No 3D subsystem | No render-to-texture, no camera, no scene call, no terrain geometry | All minimap content is drawn as a 2D GUI layer |
