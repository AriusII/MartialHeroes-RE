---
verification: confirmed (static IDA pass, deep-3d-structs 2026 pass + wave-11 deep-dive, IDB SHA f61f66a9ae0ec1e946105b2ecff76e8930cb1d1367df64e5688a5266f5ad9963)
ida_reverified: 2026-06-28
ida_anchor: f61f66a9ae0ec1e946105b2ecff76e8930cb1d1367df64e5688a5266f5ad9963
evidence: [static-ida]
sample_verified: false
subsystems: [sky_rendering, environment_sky, cloud_dome, star_dome, particle_billboards]
conflicts: star_colour_grid_slot_count (RESOLVED wave-11: 48 slots confirmed by interpolator); sun_struct_orientation_params (RESOLVED wave-11: write-once constants confirmed; draw-time axis [debugger-confirm]); skybox_geometry_disk_reader_location (RESOLVED wave-11: confirmed dead code, gate provably always 0)
deepened: wave-11 closed §11 items 1/3-values/4/6; added StarDome +0x06C4/+0x06C8/+0x06CC colour-grid base and +0x2ACC–+0x2ADC; filled CloudDome +0x71AC–+0x7397; corrected SunBillboard_Struct +0x14/+0x34 and confirmed static-singleton ownership; confirmed cloud+star alt-animator default; confirmed seconds-of-day clock consumer path
atm_deep_pass: 2026-06-29 — closed §11 item 2 (EnvSky_Manager+0x2B4 CONFIRMED reserved, no traced path in SkySystem_Init or SkySystem_UpdatePerFrame); closed §11 item 8 (g_EnvTimeBlock full layout recovered, clock CONFIRMED local wall-clock, no server writer — see specs/environment.md §2.5)
related_specs: Docs/RE/formats/sky.md, Docs/RE/formats/environment_bins.md, Docs/RE/specs/environment.md, Docs/RE/structs/environment_light_scene.md
---

# Structs: EnvSky_Manager — Environment-sky runtime object layouts

> **Clean-room spec. Neutral description only — NO decompiler pseudo-code, NO binary addresses,
> NO decompiler-generated identifiers.** Promoted from dirty-room static comprehension under EU
> Software Directive 2009/24/EC Art. 6, solely to document object layout for clean-room
> reimplementation. **All offsets below are byte offsets relative to the start of the named object** —
> they are struct/layout offsets, NOT memory addresses, and must never be treated as such.
>
> **Scope.** This file documents the RUNTIME object layouts for the sky / environment-sky subsystem:
> the singleton container (`EnvSky_Manager`), the embedded `SkyBoxMesh_Object`, the embedded
> `MoonBillboard_Struct`, the heap `SunBillboard_Struct`, the heap `StarDome_Object`, the heap
> `CloudDome_Object`, and the `GParticleBuffer` used by the sun and moon billboards. On-disk format
> grammar for `sky%d.box` and the `.bin` colour family lives in `Docs/RE/formats/sky.md` and
> `Docs/RE/formats/environment_bins.md`. Subsystem behaviour (orbit math, render pass, cloud
> animation) lives in `Docs/RE/formats/sky.md` and `Docs/RE/specs/environment.md`.
>
> **Confidence vocabulary:** `[confirmed]` = recovered from static control-flow and operand analysis
> corroborated across multiple call sites (ctor + loader + draw/update where noted);
> `[static-hypothesis]` = inferred from zero-init or a single observed write, not independently
> re-isolated; `[open]` = unmapped or ambiguous, requires further work. No item is debugger-confirmed
> (static-only pass); live `?ext=dbg` confirmation of the singleton is pending (see §11).

---

## 1. Object map and ownership

The environment-sky subsystem centres on a single static-lifetime `EnvSky_Manager` object (~704 bytes).
It is a Meyers-style singleton: an accessor function (called by the render-global sky-manager
initialiser) lazy-constructs the object once on first call and registers a cleanup on process exit.
The accessor's result is cached in a render-global sky-manager pointer used by the per-frame driver.

Two of the five sub-objects are **embedded directly inside `EnvSky_Manager`** (not heap-allocated):
the `MoonBillboard_Struct` at offset +0x04 (~28 bytes) and the `SkyBoxMesh_Object` at offset +0x28
(644 bytes). The remaining three — `SunBillboard_Struct`, `StarDome_Object`, `CloudDome_Object` —
are stored separately from `EnvSky_Manager`; `EnvSky_Manager` holds only pointers to them (§2).
`SunBillboard_Struct` is a **static-lifetime singleton** (not heap-allocated; confirmed wave-11):
`EnvSky_Manager`+0x00 caches the accessor's result, not an `operator new` pointer. The
`GParticleBuffer` that the sun struct owns (at sun struct +0x00) is itself heap-allocated. `StarDome_Object`
and `CloudDome_Object` are heap-allocated in the normal sense.

Corrected ownership note vs. the wave-1 render note in `Docs/RE/formats/sky.md §E`: the MOON
billboard is the **embedded** mini-struct at +0x04; the SUN billboard is a **heap** struct whose
pointer lives at +0x00. The per-frame update path proves this distinction.

```
EnvSky_Manager object (~704 B, static singleton)
  +0x00  ptr → SunBillboard_Struct      (static singleton; §5)
  +0x04  MoonBillboard_Struct           (EMBEDDED, ~28 B; §4)
  +0x20  ptr → StarDome_Object          (heap, 11,164 B; §6)
  +0x24  ptr → CloudDome_Object         (heap, 29,592 B; §7)
  +0x28  SkyBoxMesh_Object              (EMBEDDED, 644 B; §3)
  +0x2AC sky-ready master flag + timers/detail scalars
```

---

## 2. EnvSky_Manager — ~704-byte singleton container

Constructed by the environment-sky constructor; the embedded sub-objects at +0x04 and +0x28 are
initialised inline during construction. The heap pointers at +0x00, +0x20, +0x24 and all `.bin` colour
data are wired during area activation by `SkySystem_Init` (called from `Env_MapSetAndLoadArea`), which
sets the sky-ready flag at +0x2AC on success.

The per-frame driver `SkySystem_UpdatePerFrame` gates all work on the +0x2AC flag, calls
`Sky_UpdateClouds` every frame, and throttles sun/moon orbit, star interpolation, cloud tint, material,
lighting, and fog updates by comparing `g_EnvTime_TODms − last_update(+0x2B0)` against the
`throttle_interval(+0x2BC) × time_scale_multiplier` product.

| Offset | Size | Type | Role | Confidence |
|-------:|-----:|------|------|------------|
| +0x00 | 4   | ptr   | `SunBillboard_Struct*` — static-singleton sun struct (§5); deref'd by per-frame update | [confirmed] |
| +0x04 | 28  | struct | `MoonBillboard_Struct` — **embedded** (§4); built by the moon billboard initializer | [confirmed] |
| +0x20 | 4   | ptr   | `StarDome_Object*` — heap star-dome (§6); built by `Stardome_BuildGeometryAndTexture` | [confirmed] |
| +0x24 | 4   | ptr   | `CloudDome_Object*` — heap cloud-dome (§7); built by `CloudDome__ctor` | [confirmed] |
| +0x28 | 644 | struct | `SkyBoxMesh_Object` — **embedded** SoA (§3); built by `SkyBoxMesh_ctor` | [confirmed] |
| +0x2AC | 1  | u8    | sky-ready master flag (gates every draw and update branch) | [confirmed] |
| +0x2AD | 3  | —     | alignment padding | [confirmed] |
| +0x2B0 | 4  | u32   | last-update time snapshot (`g_EnvTime_TODms` copy; throttle gate) | [confirmed] |
| +0x2B4 | 4  | u32   | reserved / dead — ctor-zero only; no read or write site in any traced sky path (`SkySystem_Init`, `SkySystem_UpdatePerFrame`, or subordinate call chains) — atmosphere deep-cartography pass 2026-06-29 | [confirmed] |
| +0x2B8 | 4  | u32   | sky detail level (0 / 1 / 2; sourced from render-config singleton) | [confirmed] |
| +0x2BC | 4  | f32   | throttle interval (detail 1 → 0.25 s; detail 2 → ≈0.667 s; else → 2.0 s) | [confirmed] |

Total ≈ 704 bytes (constructor memset extents + `SkySystem_Init` writes cover +0x00..+0x2BC).

---

## 3. SkyBoxMesh_Object — 644 bytes, embedded at EnvSky_Manager+0x28

A structure-of-arrays holding six parallel 32-slot arrays. Offsets below are **relative to the
SkyBoxMesh_Object base** (i.e., add 0x28 for the absolute offset within `EnvSky_Manager`).

All offsets confirmed three ways: `SkyBoxMesh_ctor` (zeroes the entire 644-byte region), `SkyBox_LoadFromFile` (populates mesh_count, texture handles, and allocates/fills vertex/index buffers), and `Sky_DrawModelMeshes` (reads all six arrays to issue draw calls). The 32-slot ceiling is a
compile-time constant baked into the array dimensions; there is no runtime enlargement.

| Offset | Size | Type | Role | Confidence |
|-------:|-----:|------|------|------------|
| +0x000 | 128 | ptr[32]  | texture handle array — each entry is a `GHTex*` for mesh _i_'s stage-0 texture | [confirmed] |
| +0x080 | 4   | u32      | mesh_count — live mesh count (= `texture_count` from the `.box` header) | [confirmed] |
| +0x084 | 128 | ptr[32]  | vertex-buffer pointer array — each entry points past an inline u32 count prefix to the first 20-byte vertex | [confirmed] |
| +0x104 | 128 | u32[32]  | vertex_count array — per-mesh vertex count (hard cap 300) | [confirmed] |
| +0x184 | 128 | ptr[32]  | index-buffer pointer array — each entry points to a `u16[]` index run | [confirmed] |
| +0x204 | 128 | u32[32]  | index_count array — per-mesh index count (hard cap 900) | [confirmed] |

- SoA dword layout: handles[0..31] at words 0–31, mesh_count at word 32, vbuf_ptrs[0..31] at words
  33–64, vcounts[0..31] at words 65–96, ibuf_ptrs[0..31] at words 97–128, icounts[0..31] at words
  129–160 → 161 dwords = 644 bytes.
- Vertex layout: 20 bytes per vertex with FVF `D3DFVF_XYZ | D3DFVF_TEX1` (0x102) — position (12 B)
  + UV (8 B). Indices are 16-bit (`D3DFMT_INDEX16`). Draw mode: `DrawIndexedPrimitiveUP`,
  `TRIANGLELIST`, `PrimCount = index_count / 3`. [confirmed]
- Hard ceiling: 32 mesh slots in every array regardless of on-disk texture_count. [confirmed]

### 3.1 On-disk sky%d.box header — loader-confirmed grammar

`SkyBox_LoadFromFile` opens `data/sky/dat/sky<area_id>.box` via the VFS archive by-name lookup, gated
by a skybox-mesh draw-gate global that `Env_MapSetAndLoadArea` holds at zero — meaning the loader is
never reached in the shipping client (no `.box` asset exists in the production VFS; this absence is
confirmed in `Docs/RE/formats/sky.md §A`). The loader code nonetheless confirms the header grammar:

| Offset | Size | Type | Field | Confidence |
|-------:|-----:|------|-------|------------|
| 0x00 | 4   | u32     | `texture_count` — read into SkyBoxMesh_Object `mesh_count` | [confirmed] |
| 0x04 | 47×N | char[47] | texture-name records (× `texture_count`) — each name resolves to `data/sky/texture/<name>.dds` | [confirmed] |
| …    | per-mesh | u32 + vtx×20 | `vertex_count` (cap 300) then `vertex_count` × 20-byte vertices | [static-hypothesis] |
| …    | per-mesh | u32 + u16×n  | `index_count` (cap 900) then `index_count` × u16 indices | [static-hypothesis] |

> **Open question.** The traced `SkyBox_LoadFromFile` reads only `texture_count` and the
> 47-byte name records, then allocates vertex and index buffers sized from the mesh object's
> already-resident count arrays. The disk read that populates those count arrays and the vertex/index
> payload is not present in this function body. A companion reader or a separate load phase is likely;
> since no `.box` asset exists in the VFS, this cannot be verified. The caps (300 / 900) and vertex
> stride (20 bytes, u16 indices) are confirmed from the allocation and draw paths. See §11 item 1.

---

## 4. MoonBillboard_Struct — ~28 bytes, embedded at EnvSky_Manager+0x04

Built by the moon billboard initializer; orbited per-throttle-cycle by `SkyMoon_UpdateBillboardOrbit`.
Offsets are **relative to the MoonBillboard_Struct base** (= EnvSky_Manager+0x04).

| Offset | Size | Type | Role | Confidence |
|-------:|-----:|------|------|------------|
| +0x00 | 4  | ptr  | `GParticleBuffer*` — the moon billboard's particle buffer (§8) | [confirmed] |
| +0x04 | 4  | f32  | X world position (`sin(angle) × 3200`) | [confirmed] |
| +0x08 | 4  | f32  | Y world position (`cos(angle) × 3200`; constructor default 3264.0) | [confirmed] |
| +0x0C | 4  | f32  | Z — moon is a flat circle; no Z-depth term is written by the orbit function | [confirmed] |
| +0x10 | 4  | f32  | scratch orbit angle (degrees, then radians) | [confirmed] |
| +0x14 | 4  | ptr  | moon texture `GHTex*` (phase-selected; see below) | [confirmed] |
| +0x18 | 4  | u32  | last moon-phase day index (texture-reload gate) | [confirmed] |

Moon phase texture selection: `floor((day mod 30) / 2)` selects from the fifteen `moon{0..14}.dds`
textures. The texture is reloaded only when the day index changed AND the orbit angle has passed 180°
(mid-arc). [confirmed]

Moon `GParticleBuffer` extents: billboard extents 4096 × 1024 (set by the moon billboard initializer
calling `GParticleBuffer_setBillboardSize`). During drawing the billboard size is temporarily set to
0.5, then restored to 64.0. See `Docs/RE/formats/sky.md §D.5.1` for the draw mechanics.

---

## 5. SunBillboard_Struct — static singleton, pointer at EnvSky_Manager+0x00

Built by `SkySun_Init`; orbited per-throttle-cycle by `SkySun_UpdateBillboardOrbit`; drawn by
`SkyBillboard_DrawQuadBatch`. The struct shares the first-five-field shape of the moon struct but
extends it with a 45°-tilted arc plane and additional orientation parameters. Offsets are **relative to
the SunBillboard_Struct base**.

| Offset | Size | Type | Role | Confidence |
|-------:|-----:|------|------|------------|
| +0x00 | 4  | ptr  | `GParticleBuffer*` — the sun billboard's particle buffer (§8) | [confirmed] |
| +0x04 | 4  | f32  | X world position (`sin(angle) × −3200`) | [confirmed] |
| +0x08 | 4  | f32  | Y world position (`cos(angle) × −3200 × cos45°`) | [confirmed] |
| +0x0C | 4  | f32  | Z world position (`Y × sin45°`) — 45°-tilted arc plane | [confirmed] |
| +0x10 | 4  | f32  | scratch orbit angle | [confirmed] |
| +0x14 | 4  | ptr  | sun primary texture `GHTex*` (`sun.dds`) — bound to the billboard at construction (CORRECTS earlier "draw-gate" label) | [confirmed] |
| +0x18 | 4  | f32  | orientation basis constant = 0.0 (write-once by ctor and `SkySun_Init`; never animated by orbit) | [confirmed] |
| +0x1C | 4  | f32  | orientation basis constant = 0.0 | [confirmed] |
| +0x20 | 4  | f32  | orientation basis constant = 1.0 | [confirmed] |
| +0x24 | 4  | f32  | orientation basis constant = −1.0 | [confirmed] |
| +0x28..+0x33 | 12 | — | unlisted gap (struct total ≈56 B / 0x38 confirmed wave-11; these bytes fall within that bound) | [open] |
| +0x34 | 4  | ptr  | second sky-texture handle (shared sky-texture cache slot 174; set by `SkySun_Init` each area load) | [confirmed] |

Orbit constants (public math, not addresses): `DEG_TO_RAD ≈ 0.0174533`; tilt angle 45° (`cos45°` and
`sin45°` stored as float globals); orbit scale ±3200.0. These are hard-coded immediates in the orbit
function. [confirmed]

After the sun position is computed, `SkyLight_SetDirectionFromSun` negates the sun position vector
and writes it to the directional-light sub-object's direction field (EnvironmentLightScene +0xB8, see
`Docs/RE/structs/environment_light_scene.md`) AND to the sun-direction global triple (three adjacent
f32 globals; the negated sun XYZ). This write is gated by a light-scene lock flag. [confirmed — see §10]

Sun `GParticleBuffer` extents: billboard extents 4096 × 512; default billboard size 2048.0. Confirmed
via the `SkySun_Init` particle-buffer construction call (wave-11). See `Docs/RE/formats/sky.md §D.5`.

---

## 6. StarDome_Object — 11,164 bytes (0x2B9C), heap, pointer at EnvSky_Manager+0x20

Built by `Stardome_BuildGeometryAndTexture`; per-throttle interpolation by
`StarDome_InterpolatePerStar`. The star dome uses 72 vertices / 132 triangles; FVF 0x142
(`D3DFVF_XYZ | D3DFVF_DIFFUSE | D3DFVF_TEX1`) → 24 bytes per vertex
(position 12 B + BGRA diffuse 4 B + UV 8 B). Offsets are **relative to the StarDome_Object base**.

| Offset  | Size  | Type     | Role | Confidence |
|--------:|------:|----------|------|------------|
| +0x0000 | 1728  | vtx[72]  | star mesh vertex array (24 B each; 72 verts) — per-frame day-tint pass writes BGRA diffuse into each vertex (at per-vertex offset +12), then copies to the static VB | [confirmed] |
| +0x06C0 | 4     | ptr      | `IDirect3DVertexBuffer9*` — static vertex buffer; receives the updated vertex array each throttle cycle | [confirmed] |
| +0x06C4 | 4     | ptr      | `IDirect3DIndexBuffer9*` — index buffer (864 B allocated / 432 u16 indices) | [confirmed] |
| +0x06C8 | 4     | ptr      | `star.dds` `GHTex*` — star dome texture | [confirmed] |
| +0x06CC | 9216  | BGRA[48][48] | star colour grid: 48 day-slots × 48 BGRA entries (192 B/slot); base confirmed wave-11; loaded from `stardome<area_id>.bin` | [confirmed] |
| +0x2ACC | 4     | BGRA     | base/global star colour A (initialised to (0,0,0,0xFF)) | [confirmed] |
| +0x2AD0 | 4     | u32      | alternate-path flag (0 = inline 48-slot lerp; non-zero → alternate star-dome animator) | [confirmed] |
| +0x2AD4 | 4     | BGRA     | base/global star colour B | [confirmed] |
| +0x2AD8 | 4     | u32      | status / scratch (initialised to 0) | [static-hypothesis] |
| +0x2ADC | 192   | BGRA[48] | secondary 48-entry star colour table (populated by the alternate-tint path) | [confirmed] |

**Star colour grid base confirmed (wave-11):** offset +0x06CC within the `StarDome_Object`, 9,216 bytes
(48 slots × 192 B/slot = 48 × 48 BGRA entries). Day index = `floor(g_EnvTime_TODms / 1800)`, wrap at
48; fractional lerp between adjacent slots applied to 48 of the 72 dome vertices. See §11 item 4 (conflict RESOLVED).

> **Env-bins reconciliation required (resolved here).** The wave-11 interpolator trace confirms 48
> day-slots × 192 B/slot. `Docs/RE/formats/environment_bins.md §4` previously described this block as
> "12 keyframes × 192 stars × 4 B" — that wording is refuted by the interpolator. The env-bins spec
> owner should update that prose; the runtime figure (48 slots) is definitive.

---

## 7. CloudDome_Object — 29,592 bytes (0x7398), heap, pointer at EnvSky_Manager+0x24

Built by `CloudDome__ctor`; animated every frame by `Sky_UpdateClouds` (UV scroll + texture
ping-pong); tinted per-throttle-cycle by the cloud day-tint update function; alternate colour path via
`CloudDome_AnimateVertexColors` if the flag at +0x71A8 is non-zero (constructor sets it to 0).

Cloud vertex format: 24 bytes, FVF 0x142 (`D3DFVF_XYZ | D3DFVF_DIFFUSE | D3DFVF_TEX1`) — position
(12 B) + BGRA diffuse (4 B) + UV (8 B). Each layer: 60 vertices / 108 triangles.

Offsets are **relative to the CloudDome_Object base**.

| Offset  | Size  | Type        | Role | Confidence |
|--------:|------:|-------------|------|------------|
| +0x0000 | 4     | ptr         | dynamic vertex buffer (`IDirect3DVertexBuffer9*`; 1,440 bytes = 60 verts × 24 B; refilled per layer per frame) | [confirmed] |
| +0x0004 | 4     | ptr         | index buffer (`IDirect3DIndexBuffer9*`) | [confirmed] |
| +0x0008 | 1440  | vtx[60]     | layer A live vertices (24 B each; memcpy'd to the VB at draw) | [confirmed] |
| +0x05A8 | 1440  | vtx[60]     | layer B live vertices | [confirmed] |
| +0x0B48 | 1440  | vtx[60]     | layer A reference vertices (static UV base; live = reference + scroll each frame) | [confirmed] |
| +0x10E8 | 1440  | vtx[60]     | layer B reference vertices | [confirmed] |
| +0x1688 | 4     | ptr         | layer A cloud texture (`IDirect3DTexture9*`, 512×1024) | [confirmed] |
| +0x168C | 4     | ptr         | layer B cloud texture | [confirmed] |
| +0x1690 | 4     | ptr         | layer A render surface (`GetSurfaceLevel(0)`) | [confirmed] |
| +0x1694 | 4     | ptr         | layer B render surface | [confirmed] |
| +0x1698 | 4     | ptr         | layer A offscreen plain surface (512×512) | [confirmed] |
| +0x169C | 4     | ptr         | layer B offscreen plain surface | [confirmed] |
| +0x16A0 | 4     | f32         | layer A UV-scroll cursor (V offset; incremented each frame, wraps at 1.0) | [confirmed] |
| +0x16A4 | 4     | f32         | layer B UV-scroll cursor (V offset; scrolls at 2× layer A rate, wraps at 0.5) | [confirmed] |
| +0x16A8 | 11520 | BGRA[48][60] | layer A 48-slot per-vertex day-colour table (48 day-slots × 60 verts × 4 bytes BGRA) — loaded from `clouddome<area_id>.bin` band 0 | [confirmed] |
| +0x43A8 | 11520 | BGRA[48][60] | layer B 48-slot per-vertex day-colour table — loaded from `clouddome<area_id>.bin` band 1 | [confirmed] |
| +0x70A8 | 70    | u8[10][7]   | cloud_cycle table (10 day-rows × 7 columns: Speed, Cloud_1[0–12 h], Cloud_1[12–24 h], Cloud_2[0–6 h], Cloud_2[6–12 h], Cloud_2[12–18 h], Cloud_2[18–24 h]) — loaded from `cloud_cycle<area_id>.bin` | [confirmed] |
| +0x70F0 | 4     | u32         | layer A surface slot index (mod-2 ping-pong) | [confirmed] |
| +0x70F4 | 4     | u32         | layer B surface slot index (mod-4 ping-pong) | [confirmed] |
| +0x70F8 | 4     | u32         | layer A last frame index | [confirmed] |
| +0x70FC | 4     | u32         | layer B last frame index | [confirmed] |
| +0x7100 | 16    | RECT        | blt source rectangle {0, 0, 512, 512} | [confirmed] |
| +0x7110 | 16    | RECT/pt     | blt destination parameters {0, 0, 0, 512} | [static-hypothesis] |
| +0x7120 | ~64   | char[]      | path scratch buffer (template: `data/sky/texture/cloud<n>.dds`) | [confirmed] |
| +0x71A0 | 4     | u32         | last cloud-animation time (frame skipped when current time == this value) | [confirmed] |
| +0x71A4 | 1     | u8          | rebuild/disabled gate (animation skipped when set) | [confirmed] |
| +0x71A8 | 4     | u32         | **alternate colour-animator flag** (0 = inline 48-slot lerp path; non-zero → `CloudDome_AnimateVertexColors`) | [confirmed] |
| +0x71AC | 4     | BGRA    | single-colour scratch A (alternate-path working colour) | [confirmed] |
| +0x71B0 | 4     | BGRA    | single-colour scratch B | [confirmed] |
| +0x71B4 | 4     | u32     | alternate-path status (set to 3 while running) | [static-hypothesis] |
| +0x71B8 | 240   | BGRA[60]| alternate-path output colour table, layer A (60 verts × BGRA; +0x71B8 + 240 + 240 = +0x7398 = object end) | [confirmed] |
| +0x72A8 | 240   | BGRA[60]| alternate-path output colour table, layer B | [confirmed] |

The default colour path (flag at +0x71A8 = 0, set by both the constructor and `CloudDome_InitFromBin`
on every area load) uses an inline 48-slot linear interpolation between the adjacent day-colour table
entries. No located static code path sets either the cloud flag (+0x71A8) or the star alternate flag
(StarDome_Object+0x2AD0) to non-zero; the inline lerp is the confirmed shipping path for both subsystems.
This resolves KU #11 (default path) in `Docs/RE/formats/sky.md`; runtime confirmation that neither flag
is flipped by an unlisted path remains a live debugger task — see §11 item 5.

---

## 8. GParticleBuffer — 248 bytes (0xF8)

Used by both the sun and moon billboards to build and submit billboard quads. The object carries its
own vtable pointer (polymorphic). Sizes are pinned from the `operator new` call in
`GParticleBuffer_ctor_full`. Offsets are **relative to the GParticleBuffer base**.

| Offset | Size | Type       | Role | Confidence |
|-------:|-----:|------------|------|------------|
| +0x00 | 4    | ptr        | vtable pointer (`GParticleBuffer` virtual dispatch) | [confirmed] |
| +0x04 | 4    | u32        | D3DFVF value (0x102 = `D3DFVF_XYZ\|D3DFVF_TEX1` for textured billboard; 0x002 = point mode) | [confirmed] |
| +0x08 | 4    | u32        | vertex stride (20 bytes in textured-billboard mode, 12 bytes in point mode) | [confirmed] |
| +0x0C | 1    | u8         | textured-billboard mode flag (non-zero → 4-corner quad build) | [confirmed] |
| +0x10 | 4    | u32        | max particle capacity | [confirmed] |
| +0x14 | 4    | ptr        | bound texture (`GHTex*`) | [confirmed] |
| +0x18 | 4    | ptr        | shared static vertex buffer holder | [confirmed] |
| +0x1C | 4    | ptr        | CPU scratch vertex array (capacity × 4 verts; prefixed by a u32 count) | [confirmed] |
| +0x20 | 48   | f32[4][3]  | size-template corner offsets — TL / TR / BR / BL of a `±size/2` quad (4 corners × 3 floats) | [confirmed] |
| +0x50 | 48   | f32[4][3]  | working/oriented corner offsets — source for `GParticleBuffer_appendQuadBatch` (center + corner per particle) | [confirmed] |
| +0x80 | 32   | f32[4][2]  | corner UV coordinates — TL / TR / BR / BL (4 corners × 2 floats) | [confirmed] |
| +0xA0..+0xDF | 64 | — | gap; additional billboard state not mapped this pass | [open] |
| +0xE0 | 4    | f32        | current billboard size | [confirmed] |
| +0xE8 | 4    | f32        | max billboard-size clamp | [confirmed] |
| +0xF0 | 4    | u32        | vertex write cursor (count of quads appended since last lock) | [confirmed] |
| +0xF4 | 4    | ptr        | locked dynamic vertex buffer pointer (active during `GParticleBuffer_appendQuadBatch`) | [confirmed] |

### 8.1 GParticleBuffer vtable (partial)

`GParticleBuffer` is the only polymorphic object in the sky subsystem; all other runtime objects
(EnvSky_Manager, SkyBoxMesh_Object, CloudDome_Object, StarDome_Object, MoonBillboard_Struct,
SunBillboard_Struct) are plain structs with no vtable pointer.

| Slot (byte offset) | Role | Confidence |
|-------------------:|------|------------|
| +0x08 | Release / free — called on the shared static VB holder when capacity grows | [static-hypothesis — only this slot observed in traced paths] |

Full GParticleBuffer vtable enumeration is out of scope here; a complete slot map belongs in the
particle-system struct spec. The D3D device vtable byte offsets (SetTransform, SetRenderState,
SetTexture, DrawIndexedPrimitive, etc.) used throughout the sky draw pass are catalogued in
`Docs/RE/formats/sky.md §E`.

---

## 9. Feature-enable globals and per-frame update driver

`Env_MapSetAndLoadArea` sets six per-feature boolean globals during area activation:

| Global name | Controls |
|-------------|----------|
| `g_StardomeEnable` | Whether the star-dome receives draw calls this area |
| `g_ClouddomeEnable` | Whether the cloud-dome receives draw calls this area |
| sun-orbit enable | Gates `SkySun_UpdateBillboardOrbit` in the throttle path |
| sun-draw enable | Gates the sun draw call in the sky render pass |
| skybox-mesh draw-gate | Set to **0** for all areas — `SkyBox_ResetAndLoadIfEnabled` short-circuits before reaching `SkyBox_LoadFromFile` |
| (moon / star: no explicit gate; controlled via the sky-ready flag and the star enable) | — |

`SkySystem_UpdatePerFrame` is the per-frame driver (called from the main render loop). It:
1. Checks the sky-ready flag at EnvSky_Manager+0x2AC; returns immediately if clear.
2. Calls `Sky_UpdateClouds` unconditionally every frame (UV scroll + texture ping-pong).
3. Computes elapsed time = `g_EnvTime_TODms − last_update(+0x2B0)`.
4. Compares elapsed against `throttle_interval(+0x2BC) × time_scale_multiplier`.
5. If the threshold is met: updates sun/moon orbit positions, runs star colour interpolation, runs
   cloud day-tint (or alternate animator), updates environment material, coupling, fog, and resets
   the last-update snapshot.

---

## 10. Sun-to-directional-light coupling

After `SkySun_UpdateBillboardOrbit` computes the sun world position, `SkyLight_SetDirectionFromSun`
negates the sun position vector and writes it as the directional-light direction. The write goes to
two locations simultaneously:

- The directional-light sub-object of the `EnvironmentLightScene` singleton at sub-object-relative
  offset +0xB8 (see `Docs/RE/structs/environment_light_scene.md` §3 for the `GDirectionalLight`
  layout at hub +0x184).
- A three-float global holding the global sun-direction vector (used by other subsystems such as
  terrain and character lighting).

Both writes are gated by a light-scene lock flag. This confirms the committed §D.2.1 in
`Docs/RE/formats/sky.md` at the field-offset level.

---

## 11. Known unknowns and open conflicts

1. **Skybox geometry disk-read location** (`[closed — dead code, wave-11]`). `SkyBox_LoadFromFile`
   reads only `texture_count` and the 47-byte name records; the disk read that would populate the
   vertex/index count arrays and payload is absent from this function. This is now moot: the
   skybox-mesh draw-gate global has exactly one writer in the binary, which unconditionally clears it;
   no code path sets it non-zero. `SkyBox_ResetAndLoadIfEnabled` never reaches `SkyBox_LoadFromFile`
   in the shipping client. The missing reader is irrelevant — confirmed dead code, not a gap requiring
   further tracing.

2. **EnvSky_Manager+0x2B4** (`[CLOSED — CONFIRMED reserved, atmosphere deep-cartography pass 2026-06-29]`).
   `SkySystem_Init` (which fills the sky detail level, throttle interval, sun pointer, star/cloud heap
   pointers) and `SkySystem_UpdatePerFrame` (the per-frame throttle and update path) both bypass this
   field entirely — confirmed by tracing every init and update write-site. The constructor zeros it;
   no traced path reads or writes it. **Reserved/dead** between `last_update(+0x2B0)` and
   `detail_level(+0x2B8)`.

3. **SunBillboard_Struct +0x18..+0x24 draw-time consumption** (`[debugger-confirm]`). Confirmed
   wave-11: these four floats are write-once constants (0.0, 0.0, 1.0, −1.0), written identically by
   both the static ctor and `SkySun_Init` and never updated by the orbit function. They constitute a
   fixed billboard orientation basis (up-vector (0,0,1) + facing sign −1.0 orienting the quad in the
   45°-tilted arc). The exact axis assignment at draw time — how `SkyBillboard_DrawQuadBatch` reads
   them — requires the live `?ext=dbg` debugger to confirm.

4. **Star colour grid slot count conflict** (`[RESOLVED — wave-11]`). The interpolator
   (`StarDome_InterpolatePerStar`) conclusively proves 48 day-slots × 192 B/slot (1,800 s/slot, wrap
   at 48). The colour grid base is confirmed at StarDome_Object+0x06CC. The `environment_bins.md §4`
   description ("12 keyframes × 192 stars × 4 B") is refuted by the runtime divisor; the env-bins spec
   owner should update that prose. This item is closed for this spec.

5. **Cloud/star alternate animators in shipping content** (`[debugger-confirm]`). Wave-11 confirms
   both the cloud alternate-animator flag (CloudDome_Object+0x71A8) and the star alternate-path flag
   (StarDome_Object+0x2AD0) are set to 0 by their constructors AND by the respective `*_InitFromBin`
   functions on every area load. No located static code path sets either flag non-zero — the inline
   48-slot lerp is the confirmed shipping path for both subsystems. Remaining open question: whether an
   unlisted area-event or network path flips either flag at runtime. Confirm with the live debugger
   after several area loads and a full day cycle.

6. **SunBillboard_Struct size and ownership** (`[RESOLVED — wave-11]`). The sun struct is a
   **static-lifetime singleton** (not heap-allocated); its pointer is cached at EnvSky_Manager+0x00.
   Confirmed size ≈ 56 bytes (0x38). The `GParticleBuffer` the sun struct owns at sun+0x00 is itself
   heap-allocated (248 bytes). This resolves the §11 size estimate and corrects the earlier "heap"
   labelling in §1/§2/§5.

7. **Live singleton confirmation** (`[debugger-confirm]`). All offsets are static-confirmed from
   constructor, loader, update, and draw paths. Live confirmation against the running `EnvSky_Manager`
   instance is pending: read the sun-singleton pointer at +0x00, the embedded moon sub-struct at +0x04,
   star/cloud heap pointers at +0x20/+0x24, one bound cloud texture (512×1024), and the star colour
   grid base at StarDome_Object+0x06CC.

8. **Day/night clock writer** (`[CLOSED — atmosphere deep-cartography pass 2026-06-29]`).
   `g_EnvTimeBlock` is **fully recovered** — the complete 7-field struct layout is in
   `Docs/RE/specs/environment.md §2.5`. **Clock is local wall-clock driven**: the advancer reads
   `timeGetTime()` each tick, computes a ms delta, optionally scales by the time-scale multiplier at
   +0x08, and accumulates into the seconds-of-day field (+0x04, [0, 86400)). `EnvTime_Set` is the
   only TOD seeder; its two static callers are `Env_MapSetAndLoadArea` (area activation) and a
   developer TOD dialog (control id 1233). **No network/server writer of `EnvTime` exists statically.**
   Remaining `[debugger-confirm]`: the exact numeric time-scale value seeded at area load (+0x08),
   and live confirmation that no runtime path calls `EnvTime_Set` outside the two located static sites.

9. **Shared sky-texture cache (sun +0x34, slot 174)** (`[open]`). `SkySun_Init` fetches a second
   texture from a pre-loaded sky-texture array at index 174. The array builder was not characterised
   this pass. The role of this second texture at draw time (sun disc vs. glow/flare) and how it pairs
   with the primary `sun.dds` at +0x14 remain unknown; requires further static tracing or a live read.

10. **IB fill vs. allocation discrepancy** (`[open]`). Star IB allocates 864 bytes (432 u16 indices /
    144 triangles) and cloud IB 720 bytes (360 u16 indices / 120 triangles); the geometry builders fill
    132 and 108 triangles respectively. Buffers are over-allocated — not a contradiction — but the
    exact filled index counts (and the 12×2 derived-vertex copy in the star path) should be confirmed
    from `Stardome_FillGeometryBuffers` and `CloudDome_BuildDomeMesh`.
