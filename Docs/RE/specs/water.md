# Spec: water — rendering subsystem — RESOLVED-NEGATIVE

> Clean-room spec. Neutral description only — NO sample bytes, NO decompiler pseudo-code,
> NO binary addresses. This is the dedicated expanded record for the water subsystem; the
> summary entry is in `Docs/RE/specs/environment.md §4`. No water implementation is required
> by this spec — see §4 for Godot guidance.

<!--
verification:
  confirmed:
    - doida.exe contains no water renderer, no water geometry build, no animated-UV / scroll
      math, no planar-reflection render-to-texture pass, and no water asset-IO loader
    - OPTION_WATER is parsed from DoOption.ini into the options singleton at field index 9
      (struct offset +0x24), clamped to [1,3], default 1; loaded by Options_LoadFromIni and
      written back by the options save helper — the OPTION_WATER string literal has exactly
      two cross-references in the binary (load + save); zero render references
    - complete options-accessor call-site sweep (85 sites total): field +0x24 (OPTION_WATER,
      index 9) has 0 consumers; field +0x1C (OPTION_SKY, index 7) has 7 consumers —
      SkyBox_LoadFromFile, SkySystem_Init, Stardome_BuildGeometryAndTexture (×2),
      Stardome_RebuildBuffersAndTexture (×2), QuestPanel__onEvent — confirming the sweep
      detects field reads and the water field is genuinely dead
    - D3DXCreateRenderToSurface has exactly 4 call sites in 2 functions:
      ShadowManager_Init (shadow map, ×1) and Renderer_InitCelGlowShaders (cel/toon + glow/bloom,
      ×3); no reflection or planar-mirror render pass exists
    - shader assets in Renderer_InitCelGlowShaders are cel/toon and glow only; no water shader
    - terrain FX overlay classes Fx1Terrain–Fx7Terrain read no water field in their init chains
    - no Water / Wave / Reflect / Ocean / River RTTI descriptor, vtable, or type name exists
    - string scan (whole image, case-insensitive): "water" → 1 hit (OPTION_WATER only);
      wave/ripple/reflect/refract/caustic/foam/aqua → 0; scroll/flow/uv-anim → 0
      (water-related); RenderTarget/RenderToTexture/RTT/Mirror/Planar → 0; river/ocean/lake/pond
      → 0; "sea" → 1 substring hit inside NpcSearch, unrelated
    - spot-confirmed: sky-system initialisation reads OPTION_SKY (index 7); field index 5
      (OPTION_VIEW_BACK, +0x14) drives TerrainManager_SelectStreamRadiusByQuality; terrain
      stream radius is NOT driven by a water global
    - conclusion is RESOLVED-NEGATIVE at control-flow + operand level; no live debugger
      confirmation is needed (a feature with no code, no type, no string, and no option consumer
      cannot execute at runtime)
  static-hypothesis: []
  capture/debugger-pending: []
  ida_reverified: 2026-06-28    # deep-3d wave 3 (f61f66a9): exhaustive option-consumer sweep
    (85 sites) + RTT call-site audit + shader asset audit + RTTI / string scan — all negative;
    terrain stream radius re-confirmed driven by OPTION_VIEW_BACK, not a water global
  ida_anchor: f61f66a9ae0ec1e946105b2ecff76e8930cb1d1367df64e5688a5266f5ad9963
  readiness: RESOLVED-NEGATIVE — no water implementation is needed; the Godot port is free to
    author water visuals without any legacy-derived constraints
  evidence: [static-ida]
  conflicts: environment.md §4.2 previously noted "a single water-related global passed to terrain
    streaming as a radius parameter"; the deep-3d wave 3 pass (2026-06-28) confirms that radius
    parameter is OPTION_VIEW_BACK (index 5), not a water field — the RESOLVED-NEGATIVE conclusion
    in environment.md §4 is unchanged; only the phrasing of that historical note is imprecise
-->

---

## Status block

| Attribute | Value |
|---|---|
| `status` | **RESOLVED-NEGATIVE** — no water renderer, no water geometry, no reflection RTT, and no water asset loader exist in the binary |
| `binary_analysed` | `doida.exe` (legacy client) — complete options-accessor sweep (85 sites), RTT call-site audit, shader asset audit, RTTI / string scan |
| `confidence` | **STATIC-CONFIRMED** — the negative is settled at control-flow + operand level; no live debugger pass is needed |

---

## 1. Subsystem status

**RESOLVED-NEGATIVE.** The `doida.exe` client contains **no water renderer** — no water geometry build, no animated-UV or scroll math, no planar-reflection render-to-texture pass, and no water asset-IO loader. A river, sea, or lake surface is not a rendered feature of this client build. This is the exhaustive re-confirmation (with a complete consumer sweep) of the conclusion carried in `Docs/RE/specs/environment.md §4`.

The sole water-shaped token in the binary is the `OPTION_WATER` quality-tier setting read from `DoOption.ini`. It is parsed into the options singleton and written back on save. **No code path anywhere reads that field to alter any rendering mode, geometry buffer, or draw call** — proven by sweeping every call site of the options singleton accessor across the whole binary (§3.2).

A 1:1 Godot re-creation has **no original water implementation to be faithful to**. Any water plane the Godot client renders is a free engineering choice with no legacy-derived parameters to honour — see §4 for guidance.

---

## 2. The only water-shaped datum: OPTION_WATER

The only water-related state in the binary is one integer field on the global options singleton.

### 2.1 Options singleton field layout

| Field | Struct index | Struct offset | Size | Type | Clamped range | Default |
|---|---|---|---|---|---|---|
| `OPTION_WATER` | 9 | +0x24 | 4 B | u32 | [1, 3] | 1 |

For context, the neighbouring quality fields and their render-consumer status:

| Field | Struct index | Struct offset | Render consumers |
|---|---|---|---|
| `OPTION_VIEW_BACK` | 5 | +0x14 | `TerrainManager_SelectStreamRadiusByQuality` (stream radius 1800 / 1000 / 600 by tier) |
| `OPTION_SKY` | 7 | +0x1C | 7 consumers (sky hub, star/cloud domes — see §3.2) |
| `OPTION_WATER` | 9 | +0x24 | **0 consumers** |

### 2.2 Load and save paths

**Load.** `Options_LoadFromIni` reads `DoOption.ini` key `"OPTION_WATER"` under section `"DO_OPTION"`, stores the result at options struct offset +0x24, then clamps: any value outside [1, 3] is replaced by 1. The clamped range implies three intended quality tiers; no rendering code ever selects among them.

**Save.** The options save helper clamps its argument to [1, 3], stores it at options struct offset +0x24, and writes it back to `DoOption.ini`. The singleton and its water field persist for the lifetime of the process.

**Cross-reference count.** The `"OPTION_WATER"` string literal has exactly **two** references in the binary: the load path and the save path. There is no third (render) reference.

---

## 3. Evidence — exhaustive negative sweep

### 3.1 String scan (whole image, case-insensitive)

| Pattern | Hits | Notes |
|---|---|---|
| `water` | 1 | `"OPTION_WATER"` only |
| `wave`, `ripple`, `reflect`, `refract`, `caustic`, `foam`, `aqua` | 0 | No matches anywhere |
| `scroll`, `uv-anim`, `flow` | 0 water-related | Matches exist only for UI scrollbars, Lua stack messages, and codec strings |
| `RenderTarget`, `RenderToTexture`, `RTT`, `Mirror`, `Planar` | 0 | Only generic RTTI error strings |
| `river`, `ocean`, `lake`, `pond` | 0 | |
| `sea` | 1 (substring) | Inside `NpcSearch` — unrelated to water rendering |

No water manager class name, water texture file path, water draw function name, or reflection setup string survives in the binary's string table.

### 3.2 Options-accessor consumer sweep (the decisive test)

The options singleton is accessed throughout the client via a single lazy get-or-init accessor. That accessor has **85 call sites** across the whole binary. A sweep of every call site — examining the instruction window after each call for a field read at struct offset +0x24 — produced:

| Field | Offset | Consumers found | Result |
|---|---|---|---|
| `OPTION_WATER` | +0x24 | **0** | No code path reads the water field back |
| `OPTION_SKY` (positive control) | +0x1C | **7** | `SkyBox_LoadFromFile`, `SkySystem_Init`, `Stardome_BuildGeometryAndTexture` (×2), `Stardome_RebuildBuffersAndTexture` (×2), `QuestPanel__onEvent` |

The positive control (`OPTION_SKY`) confirms the sweep detects field reads. The water field (+0x24) is a genuine dead field: it is parsed from INI, clamped, and written back, but never read by any renderer.

Two specific render/environment readers were spot-checked to confirm they do not touch the water field:
- The sky-system initialisation routine reads field index 7 (`OPTION_SKY`) to select a sky detail scalar.
- `TerrainManager_SelectStreamRadiusByQuality` reads field index 5 (`OPTION_VIEW_BACK`) to select the terrain stream radius (1800 / 1000 / 600 world units by tier).

Neither reads from index 9 (`OPTION_WATER`).

### 3.3 Render-to-texture audit

`D3DXCreateRenderToSurface` is the client's only mechanism for creating off-screen render targets. It has exactly **4 call sites** across the whole binary, in exactly **2 functions**:

| Function | Purpose | Call sites |
|---|---|---|
| `ShadowManager_Init` | Shadow-map surface | 1 |
| `Renderer_InitCelGlowShaders` | Cel/toon post-process + glow/bloom (screen-res colour targets + downsampled blur buffer) | 3 |

There is no planar-reflection pass, no camera rendered a second time into a reflection texture, and no mirror render target. The binary's entire render-to-texture budget is consumed by shadow mapping and the cel-shading post-process pipeline.

### 3.4 Shader audit

The shader assets loaded by `Renderer_InitCelGlowShaders` (the client's sole shader pipeline) are:

- `data/shader/power1dx8.psh`
- `data/shader/dotoonshading.vsh`
- `data/shader/dotoonshading.psh`
- `data/shader/dotoonshading2.psh`
- `data/shader/finaldx8.psh` (final glow composite)
- `data/shader/toonramp.bmp` (cel-shading ramp texture)

None of these is a water, reflection, or refraction shader. No water-specific shader file path appears anywhere in the binary string table. The client's entire shader budget is the cel-shaded "DO toon" look plus bloom.

### 3.5 Terrain FX overlay check

The terrain FX overlay classes `Fx1Terrain` through `Fx7Terrain` were examined as a candidate path for water implemented as a terrain-blend overlay. None of their initialisation or texture-assignment paths reads a water field or references any water-related asset. They are decorative texture-blend overlays on terrain tiles and are unrelated to water rendering.

### 3.6 Class / RTTI check

A search for MSVC RTTI descriptors matching `Water`, `Wave`, `Reflect`, `Ocean`, and `River` returns zero hits. There is no water object class, no water vtable, and no water RTTI metadata anywhere in the binary. The client has no water subsystem object lifecycle — no constructor, no destructor, no per-frame update, no draw call slot.

---

## 4. Reimplementation guidance

Because no original renderer and no asset-IO loader exists, the Godot port is free to implement water visuals independently. There are no legacy-derived parameters to honour:

| Parameter | Legacy value | Status |
|---|---|---|
| Water enable flag | none | No such field exists in `map_option{id}.bin` (see `Docs/RE/specs/environment.md §4.1` and `Docs/RE/formats/environment_bins.md §1.1` — offsets 0x00/0x04 are the dungeon flag and sight-clamp distance) |
| Water plane Y | none | No surface Y is stored anywhere the shipping client consumes |
| UV scroll / flow rate | none | No scroll or flow math exists in the binary |
| Reflection setup | none | No reflection RTT, no secondary camera pass |
| Water shader | none | No water shader file is referenced |
| Quality tiers | `OPTION_WATER` ∈ {1, 2, 3} | Parsed and clamped but never consumed; may be repurposed for LOD control in a reimplementation |

The `WaterRenderer` node's shader, texture, UV animation, geometry placement, and reflection approach are purely engineering decisions for the Godot layer. There is no original implementation to be faithful to.

> **Note on historical footage.** Water surfaces visible in old footage of the live game were most likely rendered by a client version predating this binary, by a feature removed before ship, or by a separate component not present in the captured client. No artifact of any such feature survives in `doida.exe` at the confirmed IDB anchor.
