# Spec: environment — per-area sky, lighting, fog, and water runtime model

> Clean-room spec. Neutral description only — NO sample bytes, NO decompiler pseudo-code,
> NO binary addresses. Consumed by `Client.Application` (environment state assembly) and by
> `05.Presentation/MartialHeroes.Client.Godot` (rendering). Every value an engineer cites
> must reference this file or `Docs/RE/formats/environment_bins.md`.

---

## Status block

| Attribute         | Value |
|-------------------|-------|
| `status`          | `sample-verified` for the file loading model, day/night cycle math, and area 2 parameter values; `partial` for indoor lighting detail; water renderer: **RESOLVED-NEGATIVE** (see §4) |
| `sample_verified` | `true` — file layouts and flag values confirmed against real VFS samples for areas 0, 1, and spot-checked areas 4–35. Area 2 (the current Godot demo area) values read directly from area-2 samples. |
| `binary_analysed` | `doida.exe` (legacy client) — sky init call chain, GRSFog/GRSLighting structs, D3D9 render-state usage, water xref exhaustive scan |
| `confidence`      | Per-claim confidence follows the same scale as `environment_bins.md`: CONFIRMED / SAMPLE-VERIFIED / CODE-CONFIRMED / PROPOSED |

---

## 1. Overview

Each game area has an integer **area ID**. When the client activates an area it loads a set of
binary files from `data/sky/dat/` and a global quality-tier config (`DoOption.ini`). Together
these define the area's visual environment:

| Component | Source file | When active |
|-----------|-------------|-------------|
| Water plane placement | `map_option%d.bin` | `water_enable = 1` — placement only; **no original renderer** (see §4) |
| Fog parameters | `fog%d.bin` | always (unless indoor; see §5) |
| Sky-dome colours | `material%d.bin` | `sky_gate = 1` |
| Star colours | `stardome%d.bin` | `stardome_enable = 1` |
| Cloud colours | `clouddome%d.bin` | `clouddome_enable = 1` |
| Cloud animation | `cloud_cycle%d.bin` | `clouddome_enable = 1` |
| Directional + ambient light | `light%d.bin` | always |
| Point lights | `point_light%d.bin` | always (count may be 0) |
| Weather | `weather%d.bin` + `weather%d_rain.bin` | OPTION_WEATHER tier ≥ 1 |
| Wind/foliage | `wind%d.bin` | always (count may be 0) |

All file specifications (offsets, sizes, field types) are in `Docs/RE/formats/environment_bins.md`.

The global `DoOption.ini` quality gates (`OPTION_SKY`, `OPTION_WATER`, etc.) impose additional
on/off switches on top of the per-area flags. In a clean reimplementation these may be treated as
optional quality tiers; the spec does not prescribe their implementation.

---

## 2. Day/night cycle

### 2.1 Timing constants

| Constant | Value | Derivation |
|----------|------:|-----------|
| `SKY_KEYFRAME_COUNT` | 48 | Total keyframes per day |
| `SKY_KEYFRAME_MS` | 1800 | Milliseconds per keyframe step |
| `SKY_PERIOD_MS` | 86 400 | Total simulated day length (48 × 1800 ms) |
| `STARDOME_KF_COUNT` | 12 | Keyframe count for stardome and clouddome |
| `STARDOME_KF_MS` | 7200 | Milliseconds per stardome/clouddome step (4 × 1800) |

### 2.2 Active keyframe and interpolation fraction

Given a monotonically increasing clock value `t_ms` (milliseconds elapsed since area load, or
modulo the period for a looping clock):

```
t_wrapped      = t_ms mod SKY_PERIOD_MS          // 0 .. 86399
kf_index       = t_wrapped / SKY_KEYFRAME_MS      // integer division → 0 .. 47
frac           = (t_wrapped mod SKY_KEYFRAME_MS) / SKY_KEYFRAME_MS   // 0.0 .. <1.0
kf_next        = (kf_index + 1) mod SKY_KEYFRAME_COUNT
```

For stardome and clouddome (12-frame cycle):

```
star_kf_index  = t_wrapped / STARDOME_KF_MS       // 0 .. 11
star_frac      = (t_wrapped mod STARDOME_KF_MS) / STARDOME_KF_MS
star_kf_next   = (star_kf_index + 1) mod STARDOME_KF_COUNT
```

### 2.3 Linear interpolation

All scalar and vector values derived from keyframe arrays are linearly interpolated:

```
value = lerp(table[kf_index], table[kf_next], frac)
```

where `lerp(a, b, t) = a + (b − a) × t`.

BGRA u8 colour entries should be converted to float `[0.0, 1.0]` before interpolation and
rounded or clamped back to u8 after. Float32 entries (lighting, fog scalars, material colours)
are interpolated directly.

### 2.4 Keyframe-to-simulated-time mapping

Keyframe 0 corresponds to midnight (00:00). Keyframe 24 corresponds to noon (12:00). This
mapping is consistent across all sampled areas:

| Keyframe | Simulated time |
|:--------:|:--------------:|
| 0  | 00:00 (midnight) |
| 6  | 03:00 (pre-dawn) |
| 12 | 06:00 (dawn) |
| 16 | 08:00 (morning) |
| 24 | 12:00 (noon) |
| 36 | 18:00 (dusk) |
| 40 | 20:00 (evening) |
| 47 | 23:30 (late night) |

The stardome/clouddome 12-frame mapping divides the same 86 400 ms window into 12 × 7200 ms
steps (one step ≈ 2 simulated hours).

---

## 3. Area environment assembly

### 3.1 Load sequence (per area activation)

1. Read `map_option{id}.bin` (40 B). Parse the 10 u32 flags. This controls all subsequent
   subsystem gates.
2. Always read `fog{id}.bin` (204 B). Parse `start_dist`, `end_dist`, `data_load_flag`, and
   `fog_colors[48]`.
3. If `sky_gate = 1`: read `material{id}.bin` (9792 B).
4. If `stardome_enable = 1`: read `stardome{id}.bin` (9216 B).
5. If `clouddome_enable = 1`: read `clouddome{id}.bin` (23040 B) and `cloud_cycle{id}.bin`
   (70 B).
6. Always read `light{id}.bin` (5312 B). The fallback at bytes 0x14B0–0x14BF is used if the
   file is absent or unreadable.
7. Read `point_light{id}.bin` if present.
8. Read `wind{id}.bin` if present.
9. If `indoor_flag = 1`: apply indoor lighting override (§5).

### 3.2 Per-frame update

Each game tick (or each rendered frame):

1. Advance `t_ms` by elapsed wall-clock time.
2. Compute `kf_index`, `frac`, `kf_next` (§2.2).
3. Sample and interpolate:
   - Directional light `color_A` from `light{id}.bin` section A.
   - Ambient light `color_A` from `light{id}.bin` section B.
   - Fog-distance scalar from `light{id}.bin` section C.
   - Fog colour from `fog{id}.bin` `fog_colors[kf_index]` (linear interpolation to `kf_next`).
   - Sky/cloud material colours from `material{id}.bin` (if `sky_gate = 1`).
4. Compute `star_kf_index` and `star_frac`; interpolate star and cloud-dome tints.
5. Apply all values to the rendering state (see §6 for Godot mappings).

---

## 4. Water — RESOLVED-NEGATIVE

> **Status: RESOLVED-NEGATIVE.** The legacy `Main.exe` client contains **no water renderer**.
> This is the definitive conclusion from an exhaustive cross-reference analysis of the binary.
> See §4.3 for the full evidence summary. Reimplementations choose their own water visuals freely.

### 4.1 Placement (CONFIRMED)

| Source | Field | Rule |
|--------|-------|------|
| `map_option{id}.bin` offset 0x00 | `water_enable` | If 0: no water plane for this area |
| `map_option{id}.bin` offset 0x04 | `water_y` | If `water_enable = 1`: the water surface belongs at world-space Y = `water_y` |

Known water Y values across the VFS:

| `water_y` value | Example areas |
|:--------------:|---------------|
| 300 | 11, 15, 16, 22, 23, 25, 26, 31, 34, 201–205 |
| 700 | 29, 30, 209, 210 |
| 1000 | 206, 207, 208 |

### 4.2 Water rendering — RESOLVED-NEGATIVE

The shipping `Main.exe` does **not** contain a dedicated water render path. The evidence for
this negative result is:

- The `water_y` global variable has exactly two cross-references in the entire binary: one that
  initialises it to zero, and one that passes it to the terrain streaming system as a streaming
  radius parameter. It is never passed to any renderer or D3D draw call.
- The `OPTION_WATER` INI setting is read from `DoOption.ini` into an options struct field, but
  no code path was found that reads that field back to alter any rendering mode. The setting has
  no confirmed runtime consumer.
- A search across all string literals in the binary for "water", "reflect", "ripple", "wave",
  and related terms returns zero matches other than `"OPTION_WATER"` itself. No water manager
  class, no water texture path, and no water draw function name exists in the binary's string
  table.
- The FX terrain overlay subsystem (`Fx1Terrain`–`Fx7Terrain`) was checked as a possible
  implementation path; no code in its initialisation chain reads `water_enable` or `water_y`.

The water surface visible in footage of the original running game was most likely rendered by a
client version predating this binary, by a feature that was removed before ship, or by a
separate DLL or plugin not present in the captured client.

### 4.3 Reimplementation guidance (RESOLVED-NEGATIVE)

Because no original renderer exists, the Godot client is free to implement water visuals
independently. The only legacy-derived constraints are:

- **Enable condition:** `map_option{id}.bin` `water_enable` = 1
- **Plane Y:** `map_option{id}.bin` `water_y` world-space units (same coordinate system as terrain)
- **Quality tiers:** `OPTION_WATER` (values 1–3) from `DoOption.ini` can be repurposed for LOD
  control in the reimplementation, consistent with the original intent

The `WaterRenderer` node's shader, texture, UV animation, and reflection approach are
engineering decisions for the Godot layer; there is no original implementation to be faithful to.

For area 2 (the current Godot demo area): `water_enable = 0`; no water plane is needed.

---

## 5. Indoor areas

An area is indoor when `map_option{id}.bin` `indoor_flag = 1`.

### 5.1 Suppressed subsystems

Indoor areas suppress the following sky subsystems regardless of their individual enable flags:

- Cloud dome (no `clouddome_enable`)
- Star dome (no `stardome_enable`)
- Sun and moon (no `sun_moon_enable`)
- Lens flare (no `lensflare_enable`)
- Material colour table sky objects

The exact suppression mechanism in the binary (conditional branch vs. per-flag override) is
CODE-CONFIRMED but the precise list of suppressed vs. retained subsystems is not fully
enumerated. Fog behaviour in indoor areas is **unverified** — fog parameters may still apply.

### 5.2 Indoor lighting model

Indoor areas use ambient-only lighting: a constant or slowly varying ambient colour with no
directional sun component. The `light{id}.bin` section B (ambient keyframes) is still read;
whether section A (directional) is also applied is unverified for indoor areas.

The ambient colour values in `light{id}.bin` section B are significantly lower for indoor
areas than outdoor, producing the characteristically dark underground atmosphere.

---

## 6. Godot reconstruction guidance

This section maps the legacy environment parameters to Godot 4 nodes and properties for
`05.Presentation/MartialHeroes.Client.Godot`. It is engineering guidance, not a legacy-format
spec; Godot parameters are chosen for best-fit equivalence.

### 6.1 Node mapping

| Legacy parameter | Source | Godot target |
|-----------------|--------|-------------|
| Directional light colour | `light%d.bin` §A `color_A` (RGB f32) | `DirectionalLight3D.light_color` |
| Directional light energy | `light%d.bin` §A `color_A` intensity | `DirectionalLight3D.light_energy` |
| Directional light direction | Derived from keyframe angle or fallback `(-7, 7, 20)` normalised | `DirectionalLight3D` transform rotation |
| Ambient light colour | `light%d.bin` §B `color_A` (RGB f32) | `WorldEnvironment → Environment.ambient_light_color` |
| Ambient light energy | `light%d.bin` §B `color_A` luminance | `WorldEnvironment → Environment.ambient_light_energy` |
| Fog colour | `fog%d.bin` `fog_colors[kf]` (BGRA → RGB) | `WorldEnvironment → Environment.fog_light_color` |
| Fog start distance | `fog%d.bin` `start_dist` × view_range | `WorldEnvironment → Environment.fog_depth_begin` |
| Fog end distance | `fog%d.bin` `end_dist` × view_range | `WorldEnvironment → Environment.fog_depth_end` |
| Fog density scalar | `light%d.bin` §C per-keyframe f32 | Modulates `fog_depth_end` or `fog_density` |
| Sky colour (noon) | `material%d.bin` `ambient_sky_color` [29..32] | `WorldEnvironment → Sky` background |
| Water plane Y | `map_option%d.bin` `water_y` | `WaterRenderer` node world-space Y position (when `water_enable = 1`; no original renderer — free implementation) |

### 6.2 BGRA → Godot Color conversion

Legacy fog and dome colours are BGRA u8 (Blue at byte 0, Green at byte 1, Red at byte 2,
Alpha at byte 3). Convert to Godot `Color` as:

```
Color(r / 255.0, g / 255.0, b / 255.0, 1.0)
```

where `r = bgra[2]`, `g = bgra[1]`, `b = bgra[0]`. The alpha byte from the file is always 0;
use 1.0 as the Godot alpha unless transparency is intentional.

Legacy material colours (`material%d.bin`) are float32 RGBA; pass directly as `Color(r, g, b, 1.0)`.
Values above 1.0 indicate HDR bloom (sun disk colour); clamp to 1.0 for non-HDR targets or use
`light_energy` scaling for the equivalent effect.

### 6.3 Day/night cycle integration

The Godot `EnvironmentNode` should maintain a running clock (`t_ms`) advanced by
`get_process_delta_time() * 1000.0` each frame. At each frame:

1. Compute `kf_index`, `frac`, `kf_next` per §2.2.
2. Lerp directional and ambient colours (§2.3).
3. Apply to `DirectionalLight3D` and `WorldEnvironment`.
4. Convert and apply fog colour (§6.2).

For the current demo (area 2) the time clock can be seeded at any value; a noon start
(`t_ms = 24 × 1800 = 43 200`) gives the brightest initial lighting.

### 6.4 Area 2 (the current Godot demo area) — environment values

Area 2 uses `map_option2.bin` flags consistent with the standard outdoor pattern
`[0, 0, 1, 1, 1, 1, 1, 0, 0, 0]`:

- `water_enable = 0` — no water plane in area 2.
- `sky_gate = 1`, `stardome_enable = 1`, `clouddome_enable = 1`,
  `lensflare_enable = 1`, `sun_moon_enable = 1` — full sky subsystem active.
- `indoor_flag = 0` — outdoor area; full lighting and fog apply.

Area 2 fog (`fog2.bin`) uses the same parameter shape as area 0 (start ≈ 0.5, end ≈ 0.9,
`data_load_flag = 0`, full 48-step BGRA fog colour table). The exact numeric values should be
read from the parsed `fog2.bin` at runtime rather than hard-coded; if the parser is not yet
wired, the following area-1 values (the main town, also a standard outdoor area) are suitable
temporary stand-ins until area 2's file is parsed:

| Parameter | Area 1 stand-in value | Source |
|-----------|----------------------|--------|
| `start_dist` | 0.5 | `fog1.bin` offset 0x00 |
| `end_dist` | 0.9 | `fog1.bin` offset 0x04 |
| Noon fog colour | approximately (155, 101, 57) RGB | `fog1.bin` kf 24 BGRA converted |
| Midnight fog colour | approximately (19, 20, 19) RGB | `fog1.bin` kf 0 BGRA converted |
| Noon directional colour | approx (0.40, 0.40, 0.40) RGB | `light1.bin` §A kf 24 |
| Noon ambient colour | approx (0.21, 0.21, 0.21) RGB | `light1.bin` §B kf 24 |
| Fallback light direction | normalise(−7, 7, 20) ≈ (−0.322, 0.322, 0.920) | `light1.bin` bytes 0x14B0–0x14BF |

The `EnvironmentNode` debt (D3 in the Godot known-issues list) is:

1. Wire `fog2.bin` (or `fog1.bin` as stand-in) into `WorldEnvironment.fog_*` properties at
   startup, driven by `kf_index` each frame.
2. Wire `light2.bin` sections A and B into `DirectionalLight3D.light_color` and
   `WorldEnvironment.ambient_light_color` per frame.
3. Set `DirectionalLight3D` rotation from the fallback direction vector until per-keyframe
   light direction data is recovered.

### 6.5 Water wiring (D4 in the Godot known-issues list)

The water rendering mechanism does not exist in the original client (§4 — RESOLVED-NEGATIVE).
The wiring task is therefore:

1. At area load, read `map_option{id}.bin` `water_enable` and `water_y`.
2. If `water_enable = 1`: position the `WaterRenderer` node at world-space Y = `water_y` and
   enable it.
3. If `water_enable = 0`: hide/disable the `WaterRenderer` node.
4. The `WaterRenderer`'s `ShaderMaterial`, texture, UV animation, and reflection approach are
   free engineering choices — there is no original implementation to reproduce.

For area 2: `water_enable = 0`, so no water plane is needed in the current demo.

---

## 7. Fallback values

If any environment file is absent or unreadable:

| Missing file | Fallback behaviour |
|-------------|-------------------|
| `map_option%d.bin` | Treat as all-zero flags (no water, no sky, no indoor override) |
| `fog%d.bin` | No fog (fog disabled) |
| `light%d.bin` | Use hard-coded fallback: directional direction `(−7, 7, 20)` normalised, energy 1.0; ambient colour `(0.2, 0.2, 0.2)` |
| `material%d.bin` | No sky-object colour tint (sky objects rendered at full white) |
| `stardome%d.bin` | Stars rendered at white |
| `clouddome%d.bin` | Cloud dome rendered at white |
| `cloud_cycle%d.bin` | No cloud animation (clouds static or hidden) |

---

## 8. Known unknowns

1. **Water rendering mechanism: RESOLVED-NEGATIVE.** Y-plane placement is confirmed from
   `map_option%d.bin`. No renderer exists in the legacy binary; Godot layer uses a free-choice
   shader. See §4 for the full evidence summary.
2. **`fog%d.bin` `data_load_flag = 0` fog colour source:** Whether `fog_colors[]` is used
   as-is or overridden by `material%d.bin` colours at runtime is unconfirmed. Both files
   contain consistent day-night colour sequences in all sampled areas.
3. **Indoor fog behaviour:** Whether fog still applies when `indoor_flag = 1` is unverified.
4. **Per-keyframe directional light *direction*:** The `light%d.bin` keyframes encode colour
   only (confirmed). Whether the sun direction rotates over the day-night cycle is unresolved —
   no direction vector is stored per keyframe in the binary. The fallback direction at 0x14B0
   may be the only direction the client ever uses. Godot may point the `DirectionalLight3D` at
   a fixed angle derived from the fallback direction.
5. **`light%d.bin` section D (secondary fog scalar):** Near-zero values in all sampled areas;
   exact influence on rendered haze or fog not quantified.
6. **`material%d.bin` unassigned indices:** See `environment_bins.md §3.3`.
7. **`cloud_cycle%d.bin` cloud ID 101:** Possible "no cloud" sentinel; unconfirmed.
8. **`weather%d.bin` full layout:** All-zero in sampled areas; needs a rain/snow area sample.
9. **Sky object render pass order:** The client renders sky layers as concentric domes in a
   specific draw order (cloud dome, star dome, sun/moon, lens flare). The precise draw-order
   is not relevant to the format spec but is relevant to Godot's sky setup.

---

## Cross-references

- **Format specs:** `Docs/RE/formats/environment_bins.md` (all file byte layouts)
- **Terrain:** `Docs/RE/formats/terrain.md`, `Docs/RE/formats/terrain_layers.md`
- **Game loop:** `Docs/RE/specs/game_loop.md` (clock, tick rate)
- **Glossary:** `Docs/RE/names.yaml`
- **Provenance:** `Docs/RE/journal.md`
- **Godot implementation files:** `05.Presentation/MartialHeroes.Client.Godot/World/EnvironmentNode.cs`,
  `05.Presentation/MartialHeroes.Client.Godot/World/WaterRenderer.cs`
