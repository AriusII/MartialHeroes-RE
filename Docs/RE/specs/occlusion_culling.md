# Spec: Occlusion Culling — Definitive Negative

> Clean-room spec. Neutral description only — no decompiler pseudo-code, no binary addresses,
> no decompiler-generated identifiers. Promoted from dirty-room static analysis under EU
> Software Directive 2009/24/EC Art. 6, solely to achieve interoperability. This spec
> documents the **definitive absence of any occlusion-culling mechanism** in the Direct3D 9
> rendering pipeline of this client build, with the evidence that establishes the negative,
> and the three visibility regimes (frustum + distance + player-centered grid) that replace it.
>
> The frame-level draw-pass sequence and Z-test/Z-write conventions are in
> `Docs/RE/specs/render_pipeline.md` §4/§5/§12. The scene-graph frustum cull is in
> `Docs/RE/specs/scene_graph.md`. The struct layouts for the cull set are in
> `Docs/RE/structs/cull_pipeline.md`. Terrain streaming, cell-ring sizing, and far-plane
> coupling are in `Docs/RE/specs/terrain-streaming.md` and `Docs/RE/specs/whole_map_assembly.md`.
> Mass-object grid construction and per-object distance budgets are in
> `Docs/RE/specs/entity_placement.md` §8. This spec cross-links rather than duplicates those.
>
> Every C# visibility-budget constant must reference this file or the relevant cross-linked spec;
> cite both as appropriate: `// spec: Docs/RE/specs/occlusion_culling.md`.

> **Verification banner**
> - **verification:** *static-confirmed* for all findings in this document, recovered by three
>   independent lines of static analysis — string scan, import-table audit, and an exhaustive
>   full-image vtable-call byte scan — plus direct decompilation of the bounding-sphere primitive
>   functions to confirm the absence of any depth-buffer or GPU read-back in the visibility path.
>   Items explicitly tagged `[debugger-confirm]` are static hypotheses awaiting a live `?ext=dbg`
>   session. The verdict (no occlusion culling of any kind) is airtight; the residuals are
>   non-blocking parameterisation nuances.
> - **ida_anchor:** f61f66a9ae0ec1e946105b2ecff76e8930cb1d1367df64e5688a5266f5ad9963
> - **readiness:** IMPLEMENTATION-READY. The three visibility regimes (§3) and the distance/
>   far-plane constants (§4) are the complete authoritative account of draw-set selection. No
>   occlusion stage exists to implement.
> - **evidence:** [static-string-scan], [static-import-audit], [static-vtable-byte-scan],
>   [static-decompile-bounding-sphere-primitives]
> - **deep-3d-wave10 (2026-06-28):** New spec. Proves the definitive negative (no occlusion /
>   portal / PVS / hardware occlusion query) for the Direct3D 9 build and documents the three
>   visibility regimes — frustum hierarchy, player-centered cell ring with 16×16 mass-object
>   grid, and per-object squared-distance budgets — that together constitute all draw-set pruning
>   in this client. Companion to `transparency_sort.md`, `water.md`, and `minimap_render.md` as
>   the same class of IDA-proven definitive negative.
> - **deep-3d-cartography-deepening (2026-06-29):** No-occlusion verdict re-confirmed by a fresh
>   exhaustive full-image vtable-call scan (0 hits across 1,078,885 instructions decoded). Terrain
>   subtile AABB quadtree documented under Regime 2 (§3) — the blanket "no quadtree anywhere" claim
>   narrowed to "no quadtree in the scene-graph or mass-object paths; terrain subtile frustum cull
>   IS a quadtree accelerator." Regime 1 frustum description expanded with plane-index ordering
>   (near=index 1, far=index 3, static-strong inference) and scalar x87 implementation note.
>   Status table updated accordingly.
> - **cross-links:**
>   `Docs/RE/specs/render_pipeline.md` (frame draw-pass sequence, §4/§5/§12);
>   `Docs/RE/specs/scene_graph.md` (bounding-sphere hierarchy, GNode layout, frustum visitor);
>   `Docs/RE/structs/cull_pipeline.md` (GCull set, GDrawablePair, GRangeObject layouts);
>   `Docs/RE/specs/terrain-streaming.md` (cell ring, stream radius, far-plane coupling, §1/§5/§10/§11);
>   `Docs/RE/specs/whole_map_assembly.md` (ring sizing by quality, §3/§5);
>   `Docs/RE/specs/entity_placement.md` (mass-object grid, per-object distance budgets, §8.3/§8.5);
>   `Docs/RE/specs/camera_movement.md` (camera far/near/FOV);
>   `Docs/RE/specs/effects.md` (effects proximity cull, §17);
>   `Docs/RE/specs/nameplate_render.md` (nameplate distance cull, §2.7);
>   `Docs/RE/specs/sound.md` (sound squared-distance cull);
>   `Docs/RE/specs/transparency_sort.md` (GRangeObject distance field as sort key, §5);
>   `Docs/RE/specs/bud_loader.md` (BudObject AABB and budget computation).

---

## Status

| Item | Confidence |
|---|---|
| No hardware occlusion query — definitive negative | CONFIRMED (string scan + import audit + full-image vtable-call byte scan) |
| No portal / cell PVS / sector visibility table | CONFIRMED (string scan — zero hits on portal/pvs/sector/cell-vis vocabulary) |
| No software depth buffer / HOM / coverage buffer | CONFIRMED (bounding-sphere primitive decompile — CPU-only geometry math, no GPU read-back) |
| No quadtree / octree / BSP / kd-tree in scene-graph or mass-object cull paths | CONFIRMED (string scan zero hits; no quadtree code in those paths) |
| Terrain subtile AABB quadtree (frustum-cull accelerator for 16×16 subtile grid per cell) | CONFIRMED (code analysis — `Terrain_QuadtreeCullRecurse` / `TerrainCell_BuildSubtileQuadtreeBounds`) |
| Frustum cull: bounding-sphere hierarchy, sphere-vs-6-plane scalar x87, mask 0x3F | CONFIRMED (scene_graph.md §3; frustum math confirmed static — no SIMD) |
| Terrain draw-set: player-centered 3×3 or 5×5 cell ring | CONFIRMED (terrain-streaming.md §1, whole_map_assembly.md §3) |
| Terrain ground window: 7×7 subtile (±192 units, 64-unit step) | CONFIRMED (terrain-streaming.md §11.1/§11.2) |
| Mass-object grid: 16×16 = 256 squares per cell, 64×64 units per square | CONFIRMED (entity_placement.md §8.3) |
| Per-object thinning: squared XZ distance vs size-keyed budget | CONFIRMED (entity_placement.md §8.5) |
| Stream radius = terrain-cull far plane = fog far (one coupled value) | CONFIRMED (terrain-streaming.md §5) |
| Camera world far 15000, shadow far 10000 | CONFIRMED (camera_movement.md, character_rendering.md) |
| TerrainManager frustum role at offsets +148 / +360 (draw-cull vs fog coupling) | [debugger-confirm] |
| Exact OPTION_VIEW_CHAR / OPTION_VIEW_BACK (1..3) → radius/budget mapping | [debugger-confirm] |

---

## 1. The Definitive Negative — No Occlusion Culling of Any Kind

**Confidence: CONFIRMED (three independent static proofs).**

This client build performs no occlusion culling. There is:

- **No hardware occlusion query.** `IDirect3DDevice9::CreateQuery` / `IDirect3DQuery9` /
  `D3DQUERYTYPE_OCCLUSION` are never used. Proven three independent ways (§2).
- **No portal / cell PVS.** No portal graph, no precomputed visibility set, no sector or
  cell visibility table. No supporting strings, data structure, or code path.
- **No software depth / occlusion buffer (HOM / coverage buffer).** Nothing reads back depth
  or rasterizes occluders on the CPU. The bounding-sphere primitive functions perform pure
  CPU scalar geometry math with no render-target read, no depth-buffer lock, and no GPU
  read-back anywhere in the visibility path.
- **No occlusion-purpose spatial partition (quadtree / octree / BSP / kd-tree).** The
  scene-graph cull and mass-object grid paths contain no quadtree or spatial partition; the
  scene-graph uses a bounding-sphere parent/child graph for frustum recursion. A real
  **per-cell terrain subtile AABB quadtree** is present (see Regime 2 §3 Step 1b), but it is a
  *frustum-cull accelerator* — it prunes which of a cell's 16×16 subtile patches are tested for
  visibility, not an occlusion or depth mechanism. No depth comparison, no GPU feedback, and no
  occlusion stage exist anywhere.

Visibility is decided entirely by **(1) frustum culling** of the bounding-sphere scene-graph
hierarchy, **(2) a player-centered uniform cell ring and per-cell 16×16 regular grid** as the
coarse draw-candidate bound, and **(3) per-object squared-distance budgets** as the fine
thinning pass. No depth comparison, no GPU feedback, and no occlusion stage exist at any point
in the pipeline.

This is the same class of proven definitive negative as `water.md`, `minimap_render.md`, and
`transparency_sort.md` — stated with the evidence of what was searched and what was found instead.

---

## 2. Evidence for the Negative

### 2.1 String Table Scan

A regex scan over all strings in the binary for the vocabulary
`occlu|portal|pvs|visib|quadtree|octree|D3DQUERY|IDirect3DQuery` returned exactly one hit:
`RectVisible` — the GDI32 API name for a 2D screen-clip test, unrelated to 3D visibility. A
separate scan for `octree|bsp|cell.*vis|grid|kdtree|sector` returned only bounding-sphere debug
strings (the engine's culling vocabulary is *bounding sphere*, not *occluder / portal / query*).

### 2.2 Import Table Audit

The renderer is Direct3D 9 (established corpus fact). The `d3d9` import table holds a single
symbol: `Direct3DCreate9`; the device is a COM object reached via vtable thereafter. The
`d3dx9_42` import table carries 24 symbols — all matrix, texture, font, surface, and sprite
helpers (`D3DXMatrix*`, `D3DXCreateTexture*`, `D3DXCreateFontA`, `D3DXCreateRenderToSurface`,
`D3DXSaveSurfaceToFileA`, and similar). None is query- or occlusion-related.

### 2.3 Full-Image Vtable-Call Byte Scan for CreateQuery

`IDirect3DDevice9::CreateQuery` is vtable index 118, byte offset `118 × 4 = 0x1D8` (D3D9 SDK
vtable layout; cross-verified against the device-vtable index map in `render_pipeline.md §11`).
An exhaustive sweep for the `call dword ptr [reg+0x1D8]` encoding across all eight register
forms found **zero matches across 1,078,885 instructions decoded** in the entire image. A guard
scan at offset `+0x1E8` also returned zero hits. The constant `D3DQUERYTYPE_OCCLUSION` (value
9) pushed as a 32-bit immediate also returned zero hits. This scan was repeated independently
on the same IDB anchor (SHA prefix `f61f66a9`), re-confirming the initial result. There is no
call site that could create or use an occlusion query object.

### 2.4 Bounding-Sphere Primitives Are CPU-Only Geometry

Direct decompilation of the bounding-sphere primitive functions — including the primary sphere
intersection primitive and the bounding-sphere traversal function that backs the picking and
frustum-cull model — shows only scalar plane, sphere, and segment arithmetic plus a fast
inverse-sqrt lookup table. There is no render-target read, no depth-buffer lock, no GPU
read-back, and no query object anywhere in the visibility path.

---

## 3. What Actually Prunes the Draw Set — Three Regimes

All draw-set selection operates through three regimes. None is occlusion-based.

### Regime 1 — Scene-Graph Frustum Cull (Statics and Skinned Actors)

Owner: `Diamond::GCull` (the cull-set at view offset `+112`/`+116`), driven by
`GCull_CullScene` in the pipeline's Phase A pass. See `scene_graph.md §3` and
`structs/cull_pipeline.md`.

A recursive `GTraverser` visit descends the scene-graph object tree. Each node carries a
bounding sphere (centre and radius per `scene_graph.md §3` — `GNode` layout). At each node the
sphere is tested against the six frustum planes using **pure scalar x87 arithmetic** (no SSE /
SIMD): `d = dot(plane.normal, sphere.center) + plane.D`; `d − r > 0` ⇒ inside (ret 2, subtree
visible); `d + r < 0` ⇒ outside (ret 0, subtree culled); else straddle (ret 1, intersecting /
visible). A negative radius is treated as always-visible (ret 1 with all plane bits set). Individual
drawables use the AABB classifier (same scalar math) after their node passes.

- `dist < −R` → whole subtree culled; recursion stops.
- `dist > R` for all six planes → fully inside; node and children are visible.
- `−R ≤ dist ≤ R` for any plane → intersecting / visible.

Node visibility mask = `(1 << planeCount) − 1`; with six planes this is `0x3F`. **Plane-index
ordering (static-strong inference):** the terrain `SkipNearFar` classifier skips indices 1 and 3,
placing **near at index 1** and **far at index 3** in the camera's 6-plane layout; the four side
planes occupy indices 0, 2, 4, 5. This ordering is a strong static inference — the camera
plane-build function was not located this pass (see `structs/cull_pipeline.md §6`).

This is the *only* hierarchical scene-graph pruning — a bounding-sphere hierarchy over the object
parent/child graph, **not** a spatial partition. Visible drawables are binned into the opaque or
transparent pipeline (see `transparency_sort.md §2`/`structs/cull_pipeline.md`). No depth sort
and no occlusion stage follow.

### Regime 2 — Terrain / Mass-Object Pass

This pass does not go through `GCull`. Its candidate set is bounded by a player-centered
regular cell grid, then thinned by squared distance. See `terrain-streaming.md`,
`whole_map_assembly.md §3/§5`, and `entity_placement.md §8`.

**Step 1 — Cell ring (coarse spatial bound).** `TerrainManager` keeps a fixed player-centered
active-cell grid: **3×3** when stream radius ≤ 1000, **5×5** when stream radius > 1000
(`terrain-streaming.md §1`). Only resident cells can draw. The ring shifts as the player crosses
cell boundaries (leading-edge load / trailing-edge evict via a ">2 cells from centre" predicate).

**Step 1b — Per-cell terrain subtile AABB quadtree (frustum pre-pass).** Each active cell holds a
pre-built **AABB quadtree** over its 16×16 subtile grid. `TerrainCell_BuildSubtileQuadtreeBounds`
constructs the quadtree bottom-up from per-subtile bounding boxes; interior nodes merge child
bounds with `ExpandBySphere`. Node bounding spheres are stored at **cell byte offset +10756,
stride 16 bytes** (4 × f32 per node), indexed as `4×parent+1+k`. A complete quadtree over
16×16 leaves has depth 4 and **341 nodes** (≈5,456 bytes from +10756). `Terrain_QuadtreeCullRecurse`
recurses the quadtree, testing each node sphere against the frustum with the **SkipNearFar
classifier** (skips near/far planes — indices 1 and 3 — since terrain extent is bounded by the
stream radius). When a leaf node is visible, the function sets the corresponding byte in the
**16×16 subtile visibility grid at cell bytes +0..+255** (indexed as `row + 16×col`) and
increments the **visible-subtile counter at cell +512**. A fully-inside node (incoming mask = 0)
short-circuits without testing. This quadtree is a **frustum-cull accelerator only** — it prunes
which subtiles are candidates for the draw window below; it is not an occlusion, depth, or HW
query mechanism.

**Step 2 — Terrain ground draw window.** Within the active ring, `Terrain_TickCellsAroundPlayer`
draws a **7×7 subtile window** (±192 units at 64-unit steps = 49 patches) centred on the player
over each cell's 16×16 subtile grid — one `DrawIndexedPrimitiveUP` call per 64-unit² patch
(`terrain-streaming.md §11.1/§11.2`). This is a box/distance window gated by the subtile
visibility flags written by Step 1b.

**Step 3 — Mass-object 16×16 grid (per-cell spatial bin).** `Map_BuildMassObjectGrid` partitions
each terrain cell into a **16×16 = 256-square** grid of **64×64 world-unit** squares. Each `.bud`
object is binned into every square its world-space AABB overlaps (4-comparison AABB test); each
square carries a growable object list and two 256-entry min/max-Y arrays (`entity_placement.md §8.3`,
`bud_loader.md`). This is a **uniform regular grid**, not a quadtree.

**Step 4 — Per-object squared-distance cull.** `BuildingTree_CullAndDraw` iterates objects in
the visible squares and applies an **XZ squared-distance** test against a size-keyed budget. Over
budget → cull flag set, skip this frame. Under budget → near (stage 0) if `distSq ≤ 0.7 × budget`;
far (stage 1) if `distSq > 0.7 × budget` (`entity_placement.md §8.5`). See §4 for budget values.

Two frustums exist in `TerrainManager` (at struct offsets `+148` "terrain draw-cull" and `+360`),
both rebuilt with the stream radius as their far plane. Their exact per-frame role — whether they
frustum-test cell squares or only feed fog/far-plane coupling — is `[debugger-confirm]`.

### Regime 3 — Per-Object Distance Culls Elsewhere (No Spatial Structure)

Squared-distance proximity culling is the universal cheap cull for non-terrain subsystems.
No spatial structure is involved; all tests are direct distance² comparisons:

- **Effects:** skip geometry build when the local-player → origin distance² exceeds the
  render-distance threshold (`effects.md §17`).
- **Nameplates:** per-type pre-squared XZ distance threshold (`nameplate_render.md §2.7`).
- **Sounds:** squared-distance cull on remote-actor sounds (`sound.md`).
- **Transparency:** `GRangeObject +4` stores the squared camera-space distance — used as a
  potential sort key but **never read by any comparison or ordering function** in the shipping
  build (`transparency_sort.md §5`).

---

## 4. Distance and Far-Plane Constants

### 4.1 Mass-Object Size-Keyed Draw Budget

Source: `entity_placement.md §8.5` / `BudObject_ComputeAABBAndBudget`. Size metric
`s = max(0.6 × XZ_diagonal(AABB), AABB_height)`. Near/far split at `0.7 × budget`.

| Size metric `s` | Squared budget (struct `+0x40`) | Draw radius |
|---:|---:|---:|
| s < 8 | 90 000 | 300 |
| 8 ≤ s < 16 | 250 000 | 500 |
| 16 ≤ s < 32 | 1 000 000 | 1 000 |
| 32 ≤ s < 64 | 2 250 000 | 1 500 |
| s ≥ 64 | 3 240 000 | 1 800 |

Cull flag at struct offset `+0x3D`; set to 1 when `distSq > budget` this frame. FX-group
equivalent budget at FX record `+0x60`, flag at `+0x64`.

### 4.2 Stream Radius / Cell Ring / Far-Plane Coupling

Source: `terrain-streaming.md §5`, `whole_map_assembly.md §5`. The stream radius is a single
coupled value (`Terrain_SetStreamRadius`) that simultaneously governs the active cell ring,
the terrain-cull far plane, and the fog far distance.

| Quality level | Stream radius | Cell ring | Grid mode / cells-per-side |
|---|---:|---|---|
| High (1) | 1 800 | 5×5 | 0 / 5 |
| Medium (2) | 1 000 | 3×3 | 1 / 4 |
| Low (3) | 600 | 3×3 | 1 / 4 |

Map-option override is allowed; upper clamp 15 000 → effective 1 000-style behaviour per
`terrain-streaming.md §10`.

### 4.3 Camera and Shadow Far Planes

Source: `camera_movement.md`, `character_rendering.md`, `terrain-streaming.md §11.8`.

| Plane | Value |
|---|---:|
| World camera far | 15 000 |
| World camera near | 5 |
| Camera vertical FOV | 65° |
| Shadow projector far | 10 000 |
| Shadow projector FOV | π/8 |
| Frustum node visibility mask (6 planes) | 0x3F |

### 4.4 Draw-Distance User Options

`OPTION_VIEW_CHAR` and `OPTION_VIEW_BACK` (values 1..3) bias the character and background draw
distance respectively (`client_runtime.md`). Exactly how each value 1..3 maps to the radius /
budget numbers above is `[debugger-confirm]`.

---

## 5. No Occlusion Pass — Render-State Perspective

There is no occlusion pass and no query pass to document; that is the finding. For completeness,
the only "visibility-gating" D3D9 interactions are the ordinary frame-path ones already specced:

- Frustum is built on the CPU; the visible set is submitted directly with no pre-pass.
- Z-test (`D3DRS_ZENABLE`) remains active for draw-order correctness; Z-write
  (`D3DRS_ZWRITEENABLE`) is masked for transparent drawables (see `transparency_sort.md §3`).
  No color-write disable (`D3DRS_COLORWRITEENABLE = 0`) occluder pre-pass exists.
- No `BeginScene`-bracketed occluder pre-pass.
- No query object create, begin, end, or get-data calls (`IDirect3DQuery9` is never
  instantiated — see §2.3).

See `render_pipeline.md §12` for the full per-pass blend/depth mode table.

---

## 6. Debugger-Confirm Items

The following are static-confirmed hypotheses requiring a live `?ext=dbg` session before
they are treated as implementation facts. All are NON-BLOCKING — the visibility verdict and
all budget constants are resolved. Route to `re-validator`; never use `dbg_start`.

| # | Item | What to confirm |
|---|---|---|
| 1 | TerrainManager frustum at `+148` / `+360` | Breakpoint `BuildingTree_CullAndDraw` and the terrain draw entry; determine whether the frustums at these offsets actively frustum-test cells or squares per frame, or only feed the fog/far-plane coupling. The per-object distance² test is the confirmed thinner; this resolves the cell-level frustum role. |
| 2 | `OPTION_VIEW_CHAR` / `OPTION_VIEW_BACK` (1..3) → radius/budget | Read the option fields and break at the budget-selection site mid-frame to map each 1..3 value to the specific radius or budget entry it selects. |
