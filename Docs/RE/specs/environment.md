# Spec: environment — per-area sky, lighting, fog, and water runtime model

> Clean-room spec. Neutral description only — NO sample bytes, NO decompiler pseudo-code,
> NO binary addresses. Consumed by `Client.Application` (environment state assembly) and by
> `05.Presentation/MartialHeroes.Client.Godot` (rendering). Every value an engineer cites
> must reference this file or `Docs/RE/formats/environment_bins.md`.

<!--
verification:
  confirmed:               # control-flow + operand level in the static IDB
    - environment hub IS the 48-slot sky-colour LUT singleton; sub-domes hung off it
    - hub construction order; material + light + fog are hub-internal (NOT a separate path)
    - day/night = 48 keyframes × 1800 ms, period 86 400 ms; linear lerp(table[kf], table[kf_next], frac)
    - K_ambient = 0.0 (single reader, zero writers) → per-keyframe ambient term is inert
    - directional light applied RAW (no multiplier, no /255, float [0,1])
    - OPTION_BRIGHT additive floor: offset = floor((BRIGHT/100)·255), clamp 255; default 100, clamp [1,100]
    - BRIGHT=100 → offset 255 → full-white ambient floor over the (0,0,0) base
    - device-ambient re-applied on slider move / per-frame byte-base change / end of light load
    - fog colour byte-domain lerp, ARGB alpha forced 0xFF, pushed via render-state token 34
    - fog data_load_flag==0 → synthesize 48 keyframes from sky-LUT via high·0.75 + low·0.25
    - fog mode LINEAR: far = s·3.0, near-recip = 1/s, enabled when s>0 (per-keyframe section-C scalar)
    - device-ambient render-state token = 139; fog-colour render-state token = 34
    - OPTION_BRIGHT at options-struct offset +116 (field index 29); cached offset reused per frame
    - per-area env files keyed by the current-area-id global
  sample-verified:         # read verbatim from a real VFS file (no IDA needed for the value)
    - display.lua DISPLAY_BASE_BRIGHT_MULTI = 1.05 (world/background geometry brightness, y=1.05·x+0)
    - display.lua DISPLAY_LIGHT_RATIO = 0.5 (CHARACTER light-colour correction only, range 0..1)
  static-hypothesis:       # single static inference, not yet control-flow-tight
    - exact fallback direction numerals (-7, 7, 20) — fallback dwords present, not byte-decoded this pass
    - lighting-manager constructor zeroing the ambient base to (0,0,0) — carried, not re-read this pass
    - indoor suppression mechanism + indoor fog behaviour — outside this pass' anchor set
    - display.lua apply-path: which render stage / D3D state / shader constant each display.lua
      brightness scalar multiplies into — IDA-PENDING (the dirty IDA lane crashed before recovering
      the render-stage reads; only the loader-writes-three-fields hypothesis is carried)
  capture/debugger-pending:  # needs the live client / a frame capture to settle
    - on-screen ambient pixel colour at default brightness (the math proves white; the pixel does not)
    - matrix major-order / up-axis / unit scale
    - whether a player's saved DoOption.ini carries a brightness below 100
  ida_reverified: 2026-06-27    # CYCLE 14 re-anchor (f61f66a9): confirmatory — subsystem cleanly relocated, 1 re-confirmed SAME, 0 corrected. Prior: 2026-06-24 CYCLE 12 audit (2026-06-24, 263bd994): DisplayConfig_ParseFramerate confirmed as the display.lua loader (§9.1); OPTION_BRIGHT read/write confirmed (§6.2a). Prior CYCLE 12 (2026-06-22, 263bd994): DISPLAY_BASE_BRIGHT_MULTI=pixel-shader c0; GLOW_BRIGHT_MULTI=c1; DISPLAY_LIGHT_RATIO confirmed DEAD on world-geometry path — see §9.2/§9.4. CYCLE 11 World block (263bd994): in-world fog config (range=s×3, near=1/s, LINEAR, enabled s>0) and the closed-form trig sun/moon orbit (seconds-of-day; not a stored track or log curve) folded in. Prior (2026-06-21): ASSET-FIDELITY re-confirmed water RESOLVED-NEGATIVE, ambient floor (OPTION_BRIGHT/100)*255 with K_ambient=0, device-ambient render-state token 139, quality-mode sky LIGHT-RATIO {mode1 0.25 / mode2 0.7 / else 2.0}; CYCLE 7 (2026-06-20) static re-walk of env/lighting constants
  ida_anchor: f61f66a9ae0ec1e946105b2ecff76e8930cb1d1367df64e5688a5266f5ad9963
  readiness: IMPLEMENTATION-READY for the C# rebuild (control-flow-confirmed against IDB SHA 263bd994); items explicitly tagged debugger-pending / capture-pending / RD-* are NON-blocking runtime residuals to confirm later.
  evidence: [static-ida, sample-vfs]
  conflicts: none of substance (one §3.1/§1.1 wording tightening — material + light are hub-internal)
  cycle7_additions:        # CYCLE 7 static re-walk landed (all static-settled HIGH unless noted)
    - per-area sky LIGHT-RATIO scalar is quality-mode-dependent {mode1→0.25, mode2→0.7, else→2.0} (§6.6) — distinct from display.lua DISPLAY_LIGHT_RATIO (default 1.0)
    - light synth fallback (missing light.bin): scale 80.0, ramp intensity = kf_idx × 0.04 (=1/25) clamped, colour = 1.0 − intensity, 48 day/night keyframes (§7)
    - material synth fallback (missing material.bin): ambient 0.30000001, diffuse 0.8, specular/emissive 0.0 (§7) — material colour table is a flat 9792-byte float blob
    - char-select env builds area 0 at PINNED time ≈ 14:30 → fog keyframe 29; fog suppression for the preview row is OPEN/DBG-pending (§6.4a) — NOT statically settled
    - REFUTED (HIGH): the "ambient ×3 multiplier" is NOT a doida.exe constant — real floor = (OPTION_BRIGHT/100)×255 (§6.2a); the ×3 is port-side
    - the apparent "×0.5 chain" in the display char-brightness reader is a reused-FP-stack-slot decompiler artifact, NOT a real halving (§9.6)
-->

---

## Status block

| Attribute         | Value |
|-------------------|-------|
| `status`          | `sample-verified` for the file loading model, day/night cycle math, area 2 parameter values, and the `display.lua` brightness scalar values (§9); `partial` for indoor lighting detail; water renderer: **RESOLVED-NEGATIVE** (see §4); lighting apply-path numeric defaults: **CONFIRMED** (see §6.2a — `K_ambient` = 0.0, `OPTION_BRIGHT` default 100); `display.lua` apply-path: **IDA-pending** (see §9.4) |
| `sample_verified` | `true` — file layouts and flag values confirmed against real VFS samples for areas 0, 1, and spot-checked areas 4–35. Area 2 (the current Godot demo area) values read directly from area-2 samples. `display.lua` scalars read verbatim from the real VFS file (§9). |
| `binary_analysed` | `doida.exe` (legacy client) — sky init call chain, GRSFog/GRSLighting structs, D3D9 render-state usage, lighting/fog apply-path, ambient-gate + brightness-slider recovery, water xref exhaustive scan |
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
| Sky-dome geometry | `sky%d.box` | `skybox_enable = 1` (VFS-only; see `formats/sky.md §A`) |
| Directional + ambient light | `light%d.bin` | always |
| Point lights | `point_light%d.bin` | always (count may be 0) |
| Weather | `weather%d.bin` + `weather%d_rain.bin` | OPTION_WEATHER tier ≥ 1 |
| Wind/foliage | `wind%d.bin` | always (count may be 0) |

A further global layer, `data/script/display.lua`, supplies tunable display-config scalars
(world/background brightness, character light correction, glow/bloom, the per-state character tint
table). The world-brightness scalars of that layer are documented in §9; the glow/bloom and
per-state character-tint scalars are in `Docs/RE/specs/rendering.md`.

All file specifications (offsets, sizes, field types) are in `Docs/RE/formats/environment_bins.md`
(colour `.bin` family) and `Docs/RE/formats/sky.md` (the `sky%d.box` geometry + parser load-order view).
The **colour domains** (byte D3DCOLOR vs. float [0,1]) and the apply-path field facts are in
`Docs/RE/formats/environment_bins.md §10`; the runtime math that consumes them is in §6 of this spec.

The global `DoOption.ini` quality gates (`OPTION_SKY`, `OPTION_WATER`, etc.) impose additional
on/off switches on top of the per-area flags. In a clean reimplementation these may be treated as
optional quality tiers; the spec does not prescribe their implementation. One further
`DoOption.ini` value — `OPTION_BRIGHT`, a 1–100 player brightness slider with **default 100** — is
the source of the global additive ambient floor (see §6.2a) and is directly relevant to the
"too-dark" fix (§6.2b).

### 1.1 Environment hub — object identity and construction order

The whole sky/lighting environment for an area is owned by a single **environment hub** object.
This hub is re-run on two triggers: when the active area changes, and when the in-game SKY option is
toggled.

> **Object identity (CONFIRMED):** the environment hub object **is** the sky-colour-table singleton
> (the 48-slot per-time master colour LUT — see §2 and `formats/environment_bins.md`). It is **not**
> a separate "environment manager" allocation. The sub-domes (star, cloud, sky-box, moon) and the
> fog parameters are members hung off this one object. Any reimplementation that modelled the
> environment and the sky-colour table as two distinct objects should merge them.

On activation the hub builds its sub-systems in a fixed order; each sub-init that fails aborts the
hub (the sub-init returns 0 → the hub returns 0), and only the all-pass path sets a "ready" flag and
returns success. The order (code-confirmed):

1. Particle / effect buffer root.
2. **Star-dome** object → loads `stardome{id}.bin`.
3. **Cloud-dome** object → loads `clouddome{id}.bin` and `cloud_cycle{id}.bin`.
4. **Sun** sub-object init.
5. Read the global **sky detail-level** option (0 / 1 / 2; selects animated vs. static sky textures
   and a derived detail scalar stored on the hub). It reads the **options-struct index 7** and branches
   the detail scalar (1.0 / a constant / 2.0). NOTE: this option was previously suspected to be the
   runtime writer of the ambient gate `K_ambient` (§6.2a). That hypothesis is **DENIED** — `K_ambient`
   has zero writers anywhere; the sky-detail option reads index 7 only and does not touch `K_ambient`
   (`formats/environment_bins.md §10.4`).
6. **Moon / particle dome** init (uses `moon{id}.dds`).
7. **Sky-box** sub-object → conditionally loads `sky{id}.box` when the skybox gate is set
   (see `formats/sky.md §A`).
8. Bind the **day/night time manager** to the dome.
9. **Material** init → loads `material{id}.bin` (hub-internal — see the note below).
10. **Light** init → loads `light{id}.bin` (hub-internal — see the note below).
11. **Fog** init → loads `fog{id}.bin` (the last sub-init before the ready flag).
12. Set the environment "ready" flag.

> **Hub-internal vs. surrounding-path loads (CONFIRMED — wording tightened):** `material{id}.bin` and
> `light{id}.bin` are loaded **inside the hub body**, immediately before fog and the ready flag — they
> are NOT loaded by a separate "surrounding area-activation path". The light loader is fault-tolerant:
> on a missing file it synthesises a default 48-keyframe grey ramp plus the fallback direction and
> still succeeds (§6.2a, §7). Only `point_light{id}.bin` and `wind{id}.bin` (and `weather{id}.bin`) are
> loaded outside the hub: the point-light map data is loaded as a sub-step of the light loader, and the
> wind/weather files are loaded by separate routines on the area-activation path. Their formats are in
> `formats/terrain_layers.md` / `formats/environment_bins.md`.

> **Fog colour source (resolves the `data_load_flag = 0` question):** when `fog{id}.bin`'s
> `data_load_flag` is 0, the fog colour table is **derived from the sky-colour table** — a 48-slot
> loop that blends two source bands per channel as `high × 0.75 + low × 0.25` (a 3:1 weighted blend
> sampled per day slot). When the flag is non-zero the file carries an explicit 192-byte colour
> table instead. See `formats/sky.md §B.2` and `formats/environment_bins.md §2.4`.

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

The 48-slot model is **CONFIRMED from the runtime colour sampler**: it indexes the master
sky-colour table by time-of-day at 1800 ms (1800 s of simulated day) per slot, bilinearly
interpolates between adjacent slots, and pushes the result to the render device. (The colour table
is the same singleton object the environment hub is built around — see §1.1.)

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

BGRA u8 colour entries are interpolated **in the byte domain** by the original (the lerp result
stays 0–255 and is packed as a D3DCOLOR directly — see §6.2a; the `/255` is a port step only for a
float engine — `formats/environment_bins.md §10.1`). Float32 entries (lighting, fog scalars,
material colours) are interpolated directly in [0,1] and applied without normalisation.

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
2. Always read `fog{id}.bin` (204 B). Parse `start_dist`, `end_dist`, `data_load_flag`,
   and — when `data_load_flag ≠ 0` — `fog_colors[48]`. When `data_load_flag = 0` the colour table
   is **derived** from the sky-colour table (§1.1).
3. If `sky_gate = 1`: read `material{id}.bin` (9792 B).
4. If `stardome_enable = 1`: read `stardome{id}.bin` (9216 B).
5. If `clouddome_enable = 1`: read `clouddome{id}.bin` (23040 B) and `cloud_cycle{id}.bin`
   (70 B).
6. If `skybox_enable = 1`: read `sky{id}.box` via the archive by-name lookup (`formats/sky.md §A`).
7. Always read `light{id}.bin` (5312 B) — **loaded by the hub itself** (§1.1). The fallback at
   bytes 0x14B0–0x14BF is used if the file is absent or unreadable; the loader never fails (it
   synthesises a default grey ramp + fallback direction on a missing file). At the end of the light
   load the device ambient is recomputed and pushed via the `OPTION_BRIGHT` floor (§6.2a).
8. Read `point_light{id}.bin` if present — loaded as a sub-step of the light loader.
9. Read `wind{id}.bin` if present — loaded by a separate routine on the area-activation path.
10. If `indoor_flag = 1`: apply indoor lighting override (§5).

(The environment hub's internal construction order is in §1.1; `material{id}.bin` and `light{id}.bin`
are loaded inside the hub, immediately before fog. `point_light{id}.bin` / `wind{id}.bin` are the only
environment files loaded outside the hub.)

### 3.2 Per-frame update

Each game tick (or each rendered frame):

1. Advance `t_ms` by elapsed wall-clock time.
2. Compute `kf_index`, `frac`, `kf_next` (§2.2).
3. Sample and interpolate:
   - Directional light `color_A` from `light{id}.bin` section A.
   - Ambient light `color_A` from `light{id}.bin` section B.
   - Fog-distance scalar `s` from `light{id}.bin` section C.
   - Fog colour from `fog{id}.bin` `fog_colors[kf_index]` (linear interpolation to `kf_next`).
   - Sky/cloud material colours from `material{id}.bin` (if `sky_gate = 1`).
4. Compute `star_kf_index` and `star_frac`; interpolate star and cloud-dome tints.
5. Apply all values to the rendering state via the apply paths in §6.2a (directional raw; the
   per-keyframe ambient term is multiplied by `K_ambient = 0` and therefore contributes nothing; the
   live device ambient is the additive `OPTION_BRIGHT` floor over the ambient base; fog colour as
   byte D3DCOLOR; fog range = `s × 3.0`). See §6 for Godot mappings.
6. Update sun and moon billboard positions from the **closed-form trigonometric orbit** of the current
   time-of-day angle — see `formats/sky.md §D.1/§D.2`.

> **Sun/moon billboard orbit — closed-form trig, seconds-of-day (CYCLE 11 correction, binary-won).**
> The sun and moon billboard positions are a **closed-form trigonometric (cosine/sine) orbit** of a
> time-of-day angle measured in seconds-of-day — NOT a stored keyframe track and NOT a logarithmic
> curve (corrects an earlier CYCLE 7 reading). Day-cycle index 29 corresponds to 52200 s (14:30) =
> orbit angle 217.5°; the directional-light direction is the negated sun position. The moon is a flat
> circle (no depth-axis component); only the sun carries a depth-axis term.
> See `Docs/RE/formats/sky.md §D.2` for the full orbit spec.

---

## 4. Water — RESOLVED-NEGATIVE

> **Status: RESOLVED-NEGATIVE.** The legacy `Main.exe` client contains **no water renderer**, and
> there is **no asset-IO loader for water** — water is a render-pass / option-toggle concern only.
> This is the definitive conclusion from an exhaustive cross-reference analysis of the binary.
> See §4.3 for the full evidence summary. Reimplementations choose their own water visuals freely.
> Note: per the Campaign-5 reconciliation (`formats/environment_bins.md §1.1`), `map_option%d.bin`
> stores **no** `water_enable` / `water_y` fields — 0x00/0x04 are the dungeon flag and sight-clamp
> distance. The §4.1 placement table below is retained as historical context only.

### 4.1 Placement (historical context — superseded by `environment_bins.md §1.1`)

The pre-Campaign-5 reading mapped `map_option{id}.bin` 0x00/0x04 to `water_enable` / `water_y`.
That reading is **disproven** — those offsets are `is_dungeon` / `sight_distance`. No water surface
Y is stored anywhere the shipping client consumes. Any water plane a reimplementation renders is a
free engineering choice, not a reproduced asset value.

### 4.2 Water rendering — RESOLVED-NEGATIVE

The shipping `Main.exe` does **not** contain a dedicated water render path, and there is **no
standalone water setup or loader routine in the environment neighbourhood**. The evidence for
this negative result is:

- The single water-related global has exactly two cross-references in the entire binary: one that
  initialises it to zero, and one that passes it to the terrain streaming system as a streaming
  radius parameter. It is never passed to any renderer or draw call.
- The `OPTION_WATER` INI setting is read from `DoOption.ini` into an options struct field, and
  written back by the option-save path, but **no code path was found that reads that field back to
  alter any rendering mode**. The setting has no confirmed runtime consumer beyond load/save. (For
  comparison, the adjacent `OPTION_SKY` toggle *does* have a consumer — it re-triggers the
  environment hub of §1.1.)
- A search across all string literals in the binary for "water", "reflect", "ripple", "wave",
  and related terms returns zero matches other than `"OPTION_WATER"` itself. No water manager
  class, no water texture path, and no water draw function name exists in the binary's string
  table.
- The FX terrain overlay subsystem (`Fx1Terrain`–`Fx7Terrain`) was checked as a possible
  implementation path; no code in its initialisation chain reads any water field.

The water surface visible in footage of the original running game was most likely rendered by a
client version predating this binary, by a feature that was removed before ship, or by a
separate DLL or plugin not present in the captured client.

### 4.3 Reimplementation guidance (RESOLVED-NEGATIVE)

Because no original renderer and no asset-IO loader exists, the Godot client is free to implement
water visuals independently. There are no legacy-derived water parameters to honour (no enable flag
and no plane Y are stored anywhere — `environment_bins.md §1.1`). `OPTION_WATER` (values 1–3) from
`DoOption.ini` may be repurposed for LOD control in the reimplementation, consistent with the
original intent.

The `WaterRenderer` node's shader, texture, UV animation, and reflection approach are
engineering decisions for the Godot layer; there is no original implementation to be faithful to.

For area 2 (the current Godot demo area): no water plane is needed.

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
whether section A (directional) is also applied is unverified for indoor areas. The ambient gate
`K_ambient` and the `OPTION_BRIGHT` floor (§6.2a) still govern how that ambient reaches the device.
Note that, with `K_ambient = 0` CONFIRMED (§6.2a), the per-keyframe §B ambient term contributes
nothing even indoors — the only live ambient indoors is the same `OPTION_BRIGHT` additive floor over
the ambient base. The "characteristically dark underground atmosphere" the §B keyframes carry is
therefore not produced by those keyframes in the shipping client; indoor darkness comes from the
absence of directional sun plus whatever the brightness floor sets (and any indoor-specific ambient
base the keyframe byte table writes).

---

## 6. Godot reconstruction guidance

This section maps the legacy environment parameters to Godot 4 nodes and properties for
`05.Presentation/MartialHeroes.Client.Godot`. It is engineering guidance, not a legacy-format
spec; Godot parameters are chosen for best-fit equivalence.

### 6.1 Node mapping

| Legacy parameter | Source | Godot target |
|-----------------|--------|-------------|
| Directional light colour | `light%d.bin` §A `color_A` (RGB f32, raw — §6.2a) | `DirectionalLight3D.light_color` |
| Directional light energy | `light%d.bin` §A `color_A` intensity | `DirectionalLight3D.light_energy` |
| Directional light direction | Static fallback `(-7, 7, 20)` normalised (no per-keyframe direction — §6.2a) | `DirectionalLight3D` transform rotation |
| Ambient light colour (keyframe) | `light%d.bin` §B `color_A` (RGB f32) × `K_ambient` (= 0 in the original — §6.2a; inert) | `WorldEnvironment → Environment.ambient_light_color` |
| Ambient light floor (brightness) | `OPTION_BRIGHT / 100` additive, **default 1.0** (§6.2a/§6.2b) | `WorldEnvironment → Environment.ambient_light_energy` |
| World/background brightness multiplier | `display.lua` `DISPLAY_BASE_BRIGHT_MULTI` = **1.05** (§9.2; apply-path IDA-pending §9.4) | multiply on the world-geometry / background scene brightness |
| Fog colour | `fog%d.bin` `fog_colors[kf]` (BGRA byte → RGB, `/255` port step) | `WorldEnvironment → Environment.fog_light_color` |
| Fog distance (live) | `light%d.bin` §C scalar `s`: range = `s × 3.0` (§6.2a) | `WorldEnvironment → Environment.fog_depth_end` |
| Fog distance (baseline) | `fog%d.bin` `start_dist` / `end_dist` × view_range (seeds, overwritten per frame) | `WorldEnvironment → Environment.fog_depth_begin/end` |
| Sky colour (noon) | `material%d.bin` `ambient_sky_color` [29..32] (f32, raw) | `WorldEnvironment → Sky` background |

> Water plane Y is **not** sourced from any environment file (`environment_bins.md §1.1`); any
> Godot water placement is a free engineering choice (§4).

### 6.2 BGRA → Godot Color conversion

Legacy fog and dome colours are BGRA u8 (Blue at byte 0, Green at byte 1, Red at byte 2,
Alpha at byte 3). Convert to Godot `Color` as:

```
Color(r / 255.0, g / 255.0, b / 255.0, 1.0)
```

where `r = bgra[2]`, `g = bgra[1]`, `b = bgra[0]`. The alpha byte from the file is always 0;
use 1.0 as the Godot alpha unless transparency is intentional. (The runtime sampler likewise forces
the high byte opaque when it pushes BGRX to the render device — see §6.2a / `formats/sky.md §C`.)

Legacy material colours (`material%d.bin`) are float32 RGBA; pass directly as `Color(r, g, b, 1.0)`.
Values above 1.0 indicate HDR bloom (sun disk colour); clamp to 1.0 for non-HDR targets or use
`light_energy` scaling for the equivalent effect.

> **Colour-domain reminder (CONFIRMED — `formats/environment_bins.md §10.1`):** the original client
> does NOT uniformly divide colours by 255. Byte BGRA tables (fog, sky, star, cloud) are packed as a
> D3DCOLOR and pushed to the device **directly** — the `/255` above is a port step for a float engine,
> not original math. The `light%d.bin` directional/ambient colours are **float [0,1]** applied
> **directly** (no /255, no gamma). Treat the two domains differently.

### 6.2a Runtime lighting/fog apply path (the recovered math)

This subsection records the **runtime math** the original client applies once the per-keyframe values
of §3.2 are sampled and interpolated. The byte/field domains it relies on are pinned in
`formats/environment_bins.md §10`. **The apply-paths below are CONFIRMED / CODE-CONFIRMED, and the
two ambient numeric defaults previously flagged UNVERIFIED are now CONFIRMED statically** — the
ambient gate `K_ambient` = 0.0 (no writer) and the `OPTION_BRIGHT` default = 100. Only a possible
user-edited on-disk INI value remains as a thin runtime residual (§6.4 / §8).

**Keyframe sampling (CONFIRMED).** Every per-frame environment sampler — fog colour, directional
light, ambient light, fog scalar — uses the same 48-keyframe / 1800 ms model of §2 with linear
interpolation `lerp(table[kf], table[kf_next], frac)`. The star/cloud domes use the coarser 12-frame
(7200 ms) cadence (§2.2). Fog/light samplers run from the per-frame map-time tick.

**Fog colour — byte domain, applied directly (CONFIRMED).** The fog-colour sampler reads the byte
R/G/B at `fog_colors[kf]` / `fog_colors[kf_next]` (`fog%d.bin`, or the synthesised table when
`data_load_flag = 0` — §1.1), lerps each channel **in the byte domain** (result stays 0–255), packs
`ARGB = (0xFF << 24) | (R << 16) | (G << 8) | B` (alpha forced opaque), and pushes it as the device
fog colour via **D3D render-state token 34** (`D3DRS_FOGCOLOR`). No /255 in the original.

**Fog mode + distance — LINEAR from the section-C scalar (CODE-CONFIRMED).** The runtime fog struct
holds `type` (1 = EXP, 2 = EXP2, 3 = LINEAR), a fog colour, `start`, `end`, and `density`. The
**observed apply path is LINEAR**, driven by the per-keyframe fog scalar `s` from `light%d.bin`
section C (`formats/environment_bins.md §9.3`):

```
if s > 0:
    fog_range      = s * 3.0          // far distance, in world units
    fog_near_scale = 1.0 / s          // reciprocal/near-scale field
    fog_enabled    = true
```

The static `fog%d.bin` `start_dist` / `end_dist` fractions seed a baseline but the **per-frame
`s × 3.0` derivation overwrites the live fog range each tick**. The scalar `s` is the lerp of a
**per-keyframe float table** (`light%d.bin` section C); the previously applied value is cached and the
distance commit is **skipped when `s` is unchanged**. The commit setter is **shared** — the same single
fog-distance setter is also called from the post-load fog apply and the light-save neighbourhood; it is
the one place fog far/near are written.

The `s > 0` guard means a non-positive `s` leaves the fog struct untouched — fog is effectively
disabled until a positive `s` is sampled. **EXP/EXP2 modes are confirmed NOT driven on the lighting
tick:** the distance setter only ever writes the LINEAR shape (it sets the mode field to LINEAR and the
density field to 0.0). The struct still carries `type` (1 = EXP, 2 = EXP2, 3 = LINEAR) and a density
field, but no quality tier was observed switching to exponential density on this path — see §8.

> **In-world fog config summary (CODE-CONFIRMED).** In-world fog is configured from the per-keyframe
> environment-bin scalar `s` (from `light%d.bin` section C): the fog colour is written via the device
> fog-colour render-state (byte-lerped from a BGRA source to ARGB with the alpha forced opaque), and
> the fog distance/mode come from the shared setter — fog **range = s × 3.0**, **near = 1 / s**,
> density 0, **LINEAR** mode, **enabled when s > 0**. This in-world configuration is distinct from
> the character-select scene, which forces fog OFF (see the Addendum).

**Directional light — float, applied RAW (CONFIRMED).** The directional sampler interpolates the
float32 `color_A` of `light%d.bin` section A and writes it to the render globals **without any
multiplier**. No /255, no gamma. Sample-verified domain: a noon directional keyframe decodes to RGB ≈
(0.40, 0.40, 0.40) — floats in [0,1].

**Per-keyframe ambient light — float, MULTIPLIED by `K_ambient` = 0 (the term is inert) (CONFIRMED).**
The ambient sampler interpolates the float32 `color_A` of `light%d.bin` section B and then **multiplies
every channel by a global float `K_ambient`** before writing it to the ambient render globals:

```
ambient_kf = lerp(B[kf], B[kf_next], frac) * K_ambient
```

The **asymmetry is the key recovered fact: directional colour is used raw; the per-keyframe ambient
colour is gated by `K_ambient`.** `K_ambient` is a **single global float with static initialiser 0.0,
exactly ONE reader (this sampler) and ZERO writers anywhere in the binary** — confirmed by the
apply-path recovery (`formats/environment_bins.md §10.4`). The earlier hypothesis that the sky-detail
/ quality option (§1.1 step 4) writes it is **DENIED — nothing writes it.** It is therefore an
effective compile-time constant **0.0**, so `ambient_kf = 0` every frame: the §B ambient keyframe
table is loaded and interpolated but **contributes nothing to the device.** (Prior status "static
init 0.0; runtime value UNVERIFIED" is now CONFIRMED 0.0 at runtime — this is the resolved
"too-dark" suspect #1: the per-keyframe ambient table is dead in the shipping client.)

**Ambient base colour — static (0,0,0), then keyframe-driven (CONFIRMED).** The ambient is built on a
3-channel base colour. The lighting-manager constructor zeroes that base to `(0, 0, 0)` and forces
its alpha opaque; at area-load time the base channels are subsequently driven from a per-keyframe BYTE
colour table inside the loaded `light%d.bin` blob (interpolated each frame). So `(0,0,0)` is the
pre-load static default and the live base is the day/night byte colour. The brightness offset below is
added onto this base.

**Player brightness — additive 0–255 ambient floor from `OPTION_BRIGHT`, default 100 (CONFIRMED).**
The in-game brightness slider (`OPTION_BRIGHT`, a **1–100** value in `DoOption.ini`, **default 100**,
clamp `[1,100]` → 100 — `formats/environment_bins.md §10.5`) converts to a uniform additive white
offset on the ambient base and is pushed as the **global device ambient colour** (a byte D3DCOLOR):

```
offset = floor( (OPTION_BRIGHT / 100.0) * 255.0 )      // 0..255  (truncated)
R = min(255, ambient_base_R + offset)
G = min(255, ambient_base_G + offset)
B = min(255, ambient_base_B + offset)
device_ambient = (0xFF << 24) | (R << 16) | (G << 8) | B
```

This device ambient is the only ambient the device actually receives (the per-keyframe term above is
zeroed by `K_ambient = 0`), and it is pushed via **D3D render-state token 139** (`D3DRS_AMBIENT`).
**At the default brightness `OPTION_BRIGHT = 100`, `offset = 255`, and over a `(0,0,0)` base the device
ambient is full white `(255, 255, 255)`.** (Prior status "assumed mid-slider ~50 → +0.5 floor" is now
CONFIRMED 100 → +1.0 floor — this is the resolved "too-dark" suspect #2.)

The same conversion is re-applied on three occasions: when the user moves the slider, when the per-frame
keyframe byte base colour changes, and at the end of each per-area light-data load. The computed
**offset is cached on the hub object** after the first computation, so the per-frame re-push on a byte-
base change reuses the cached offset rather than re-reading and re-converting the slider value every
frame. The slider value itself lives at a known **options-struct offset (field index 29, byte offset
+116)**: the INI-load path writes it there, and the light loader reads it from there to seed the floor.

**Sun direction is static (CONFIRMED).** No per-keyframe sun direction is stored — the keyframe tables
encode colour only. The sun/light direction is the static fallback vector `(-7, 7, 20)` (normalised
≈ `(-0.322, 0.322, 0.920)`, `formats/environment_bins.md §9.4`). The day/night cycle does not rotate
the sun; any sun-arc in a port is a free choice.

> **The total apparent scene brightness in the original is:**
> `directional(raw)` + `ambient_keyframe × K_ambient(=0)` + `device_ambient(OPTION_BRIGHT/100 × 255)`
> = `directional(raw)` + `0` + `(offset, offset, offset)`.
> With the default `OPTION_BRIGHT = 100` the additive ambient floor is **full white** — the
> per-keyframe ambient table never contributes.

### 6.2b "Too-dark" diagnosis and recommended fidelity fix

The Godot `EnvironmentNode` reproduces the per-keyframe directional/ambient tables but is most likely
**missing the global additive ambient floor from `OPTION_BRIGHT`** and/or **mis-models the ambient
`K_ambient` gate** (treating the per-keyframe ambient as the full ambient). Reproducing only
`directional(raw) + ambient_keyframe` yields a dark scene — exactly the reported D3 symptom
(CLAUDE.md known-issue: "`EnvironmentNode` is too dark").

**Root cause (now CONFIRMED).** Two facts the apply-path recovery settled make the original's scene
*much brighter* than a naive reproduction:

1. The per-keyframe §B ambient table is **inert** in the original (`K_ambient = 0`). A faithful port
   must NOT rely on the §B ambient keyframes for ambient brightness — they produce nothing in the
   original. Reproducing only `directional + ambient_keyframe` is exactly what makes the Godot scene
   dark.
2. The original's actual ambient floor is the brightness slider, default **100** → a uniform additive
   white of level `OPTION_BRIGHT / 100 = 1.0` over a `(0,0,0)` base. The faithful default floor is
   **1.0 (full)**, NOT 0.5.

> **NOTE — `DISPLAY_BASE_BRIGHT_MULTI = 1.05` is NOT the dark-world cause (§9.5).** The
> `display.lua` world-brightness multiplier is a ~neutral +5% uplift and cannot produce a near-black
> scene; do not re-chase it as the fix. The root cause is the two facts above. See §9.5.

**Recommended fix (engineering guidance — for the Godot layer to apply).** Set the
`WorldEnvironment` ambient floor to the brightness term in [0,1], defaulting to **1.0**:

```
ambient_floor = OPTION_BRIGHT / 100.0          // in [0,1]; DEFAULT OPTION_BRIGHT = 100 → +1.0 (full)
Environment.ambient_light_color  = white (or the stored ambient base, default (0,0,0)+floor → grey)
Environment.ambient_light_energy = ambient_floor       // default 1.0
```

Do **not** layer the per-keyframe §B ambient on top expecting it to brighten the scene — it is gated
by `K_ambient = 0` and contributes nothing in the original. The day/night colour character that *does*
survive is carried entirely by the **directional** light (applied raw) and by the byte fog/sky/dome
colour tables. This concrete change — defaulting the Godot ambient floor to **1.0** rather than 0.5,
and not relying on the §B ambient keyframes — is the root-cause fix for the "too-dark" debt.

> **Remaining residual (UNVERIFIED — thin):** the value **100** is the binary/INI default for
> `OPTION_BRIGHT`. If a player's on-disk `DoOption.ini` carries a user-saved lower value, the live
> floor is lower than 1.0. Settling this is a single runtime read of the stored brightness value after
> the options-load step (expected an i32 in percent, default 100); it changes only the floor's
> magnitude, not any layout or apply-path fact above. Until that is read for a given install, default
> the floor to 1.0.
>
> **Fog fractions (numeric refresh, structure unchanged):** observed live area-1 `fog1.bin` decodes
> to `start = 0.75`, `end = 0.98` (§6.4), superseding the earlier `0.5 / 0.9` stand-ins. Read the
> parsed file at runtime rather than hard-coding either pair.

### 6.3 Day/night cycle integration

The Godot `EnvironmentNode` should maintain a running clock (`t_ms`) advanced by
`get_process_delta_time() * 1000.0` each frame. At each frame:

1. Compute `kf_index`, `frac`, `kf_next` per §2.2.
2. Lerp directional and ambient colours (§2.3).
3. Apply to `DirectionalLight3D` and `WorldEnvironment` via the apply paths in §6.2a — directional
   raw; the per-keyframe ambient is inert (`× K_ambient = 0`); the ambient energy is the
   `OPTION_BRIGHT` floor (default 1.0).
4. Convert and apply fog colour (§6.2) and fog range (`s × 3.0`, §6.2a).

For the current demo (area 2) the time clock can be seeded at any value; a noon start
(`t_ms = 24 × 1800 = 43 200`) gives the brightest directional lighting.

### 6.4 Area 2 (the current Godot demo area) — environment values

Area 2 uses `map_option2.bin` flags consistent with the standard outdoor pattern
`[0, 0, 1, 1, 1, 1, 1, 0, 0, 0]`:

- `is_dungeon = 0`, `sight_distance = 0` — outdoor, free camera range.
- `sky_gate = 1`, `stardome_enable = 1`, `clouddome_enable = 1`,
  `lensflare_enable = 1`, `sun_moon_enable = 1` — full sky subsystem active.
- `indoor_flag = 0` — outdoor area; full lighting and fog apply.

Area 2 fog (`fog2.bin`) uses the same parameter shape as area 1 (`start_dist` / `end_dist` float
fractions, `data_load_flag = 0`, full 48-step BGRA fog colour table). The exact numeric values
should be read from the parsed `fog2.bin` at runtime rather than hard-coded; if the parser is not
yet wired, the following area-1 values (the main town, also a standard outdoor area) are suitable
temporary stand-ins until area 2's file is parsed:

| Parameter | Area 1 stand-in value | Source | Note |
|-----------|----------------------|--------|------|
| `start_dist` | **0.75** | `fog1.bin` offset 0x00 | SAMPLE-VERIFIED; supersedes the earlier 0.5 stand-in (`environment_bins.md §10.2`). |
| `end_dist` | **0.98** | `fog1.bin` offset 0x04 | SAMPLE-VERIFIED; supersedes the earlier 0.9 stand-in. |
| Noon directional colour | approx (0.40, 0.40, 0.40) RGB | `light1.bin` §A kf 24 | float [0,1], applied raw (§6.2a). |
| Noon ambient colour (pre-gate) | approx (0.21, 0.21, 0.21) RGB | `light1.bin` §B kf 24 | float [0,1]; multiplied by `K_ambient = 0` → **inert**, contributes nothing (§6.2a). |
| Fallback light direction | normalise(−7, 7, 20) ≈ (−0.322, 0.322, 0.920) | `light1.bin` bytes 0x14B0–0x14BF | static; no per-keyframe direction. |
| Brightness ambient floor | `OPTION_BRIGHT / 100` = **1.0** (full) at the default | `DoOption.ini` `OPTION_BRIGHT` | **CONFIRMED default 100 → +1.0** (§6.2a/§6.2b). Lower only if a user INI override exists. |
| World-brightness multiplier | `DISPLAY_BASE_BRIGHT_MULTI` = **1.05** | `display.lua` | SAMPLE-VERIFIED value; apply-path IDA-pending (§9). ~neutral +5% uplift, NOT the dark-world cause. |

> **Numeric defaults — status:** the live fog fractions are **0.75 / 0.98** (vs. the previous
> 0.5 / 0.9 stand-ins, corrected above). The ambient gate `K_ambient` is **CONFIRMED 0.0** and the
> `OPTION_BRIGHT` default is **CONFIRMED 100** (→ +1.0 ambient floor). The only value still pending is
> a possible user-edited on-disk `DoOption.ini` brightness override (§8).

The `EnvironmentNode` debt (D3 "too dark" in the Godot known-issues list) is:

1. Wire `fog2.bin` (or `fog1.bin` as stand-in) into `WorldEnvironment.fog_*` properties at
   startup, driven by `kf_index` each frame; drive the live fog range from the `light%d.bin`
   section-C scalar as `s × 3.0` (§6.2a).
2. Wire `light2.bin` section A into `DirectionalLight3D.light_color` per frame (directional raw).
   Do **not** wire the section-B ambient keyframes as an ambient brightness source — they are gated
   by `K_ambient = 0` and contribute nothing in the original (§6.2a).
3. **Set the `OPTION_BRIGHT` additive ambient floor (`OPTION_BRIGHT / 100`, DEFAULT = 1.0) on
   `WorldEnvironment.ambient_light_energy`** — this is the root-cause fix for the "too-dark" symptom
   (§6.2b). Use 1.0 as the default; reduce only if a user INI override is read.
4. Set `DirectionalLight3D` rotation from the static fallback direction vector (no per-keyframe
   direction exists).

### 6.4a Character-select scene environment (pinned time-of-day; fog suppression OPEN)

The character-select scene does **not** drive a live day/night clock. It builds the environment with
**area 0**, a **pinned literal time-of-day ≈ 14:30** (52 200 simulated seconds), and a specific build
flag set. With the 48-keyframe / 1800-s model (§2) that literal time resolves to **fog keyframe 29,
fraction 0** — so the keyframe-29 row is consumed verbatim with no blend (the concrete area-0
keyframe-29 values are in `environment_bins.md §11`). Because area 0's tables are static and
achromatic, keyframe 29 is representative of the whole day for that scene.

> **OPEN QUESTION (LOW / DBG-pending) — whether fog is forced OFF for the char-preview row.** Static
> analysis does **not** cleanly settle whether (or where) fog is suppressed for the character preview
> in the char-select scene: the per-frame fog apply still pushes LINEAR fog at keyframe 29, and an
> IDB comment flags a char-select-specific fog-suppression site as debugger-pending. A fog-OFF
> char-select **matches the dark-stone-temple visual oracle** and is plausible, but it is **NOT
> binary-confirmed** statically. Do **not** assert "fog OFF on char-select" as a recovered fact;
> confirm it live (maintainer-driven F9; never `dbg_start`) before relying on it.

### 6.5 Water wiring (D4 in the Godot known-issues list)

The water rendering mechanism does not exist in the original client (§4 — RESOLVED-NEGATIVE); there
is no asset-IO loader, no render path, and no stored enable/Y to be faithful to
(`environment_bins.md §1.1`). The wiring task is therefore a free engineering choice:

1. Choose where (if anywhere) to render water; there is no legacy enable flag or plane Y to read.
2. The `WaterRenderer`'s `ShaderMaterial`, texture, UV animation, and reflection approach are
   free engineering choices — there is no original implementation to reproduce.

For area 2: no water plane is needed in the current demo.

### 6.6 Per-area sky light-ratio scalar — quality-mode-dependent (CONFIRMED)

> **Confidence: CONFIRMED (static immediates).** Added CYCLE 7.

The environment hub stores a per-area **sky light-ratio scalar** that is **not** a single constant
— it is selected at hub-init from the global **render-quality mode** (read from the option singleton,
the same render-detail option set at §1.1 step 5). The branch writes one of three immediates onto the
hub:

| Render-quality mode | Sky light-ratio scalar |
|---------------------|:----------------------:|
| mode 1 (low)        | **0.25** |
| mode 2 (medium)     | **0.7**  |
| else (high/default) | **2.0**  |

This is a **distinct** quantity from the `display.lua` `DISPLAY_LIGHT_RATIO` (default **1.0**, the
character light-colour correction of §9.2) and from the renderer-ctor light-ratio default (also 1.0).
A faithful port must not conflate the two: the per-area sky light-ratio above is a quality-tiered
scene scalar applied by the sky/environment hub, while `DISPLAY_LIGHT_RATIO` is the character lighting
correction. The exact render-stage this scalar multiplies into is the same display-config apply-path
that is IDA-pending in §9.4; only the three immediate values and the quality-mode selection are
statically settled here.

---

## 7. Fallback values

If any environment file is absent or unreadable:

| Missing file | Fallback behaviour |
|-------------|-------------------|
| `map_option%d.bin` | Treat as all-zero flags (no sky, no indoor override) |
| `fog%d.bin` | No fog (fog disabled) |
| `light%d.bin` | Loader **never hard-fails** — it synthesises a default 48-keyframe day/night colour ramp and a fallback light vector and still returns success. Synth values (CONFIRMED immediates): fallback direction `(−7, 7, 20)` (the fallback-vector slot also carries scale 1.0 — `environment_bins.md §9.4`); the **synth ramp scale is 80.0**; the synth colour ramp is, per keyframe, `intensity = kf_idx × 0.04` (= 1/25) clamped, `colour = 1.0 − intensity` across 48 keyframes (darker toward dusk). Ambient base stays `(0, 0, 0)` with the `OPTION_BRIGHT` floor of §6.2a as the only live ambient (the per-keyframe §B ambient is inert, `K_ambient = 0`). |
| `material%d.bin` | Loader **never hard-fails** — it synthesises a default colour table (CONFIRMED immediates): per material entry **ambient 0.30000001, diffuse 0.8, specular 0.0, emissive 0.0**. (The sun/sky colour tint is absent so sky objects effectively render neutral.) |
| `stardome%d.bin` | Stars rendered at white |
| `clouddome%d.bin` | Cloud dome rendered at white |
| `cloud_cycle%d.bin` | No cloud animation (clouds static or hidden) |
| `sky%d.box` | No skybox mesh (sky-dome geometry absent); only present when `skybox_enable = 1` |

---

## 9. `data/script/display.lua` — global display-config layer (world-brightness scalars)

> **Status: `sample-verified` for the VALUES** (read verbatim from the real VFS file). **The
> APPLY-PATH — exactly which render stage / D3D render-state / shader constant each scalar multiplies
> into — is `static-hypothesis / IDA-pending`** (see §9.4). Source layer: `data/script/display.lua`,
> a 5,100-byte CP949 Lua key/value script mounted from the client VFS. The glow / bloom and the
> per-state character-tint keys from the same file are documented in `Docs/RE/specs/rendering.md`
> (glow chain + character-tint table); this section owns only the **world / background brightness**
> scalars.

### 9.1 What this layer is

`data/script/display.lua` is a flat table of module-level global assignments (`KEY = value`, Lua
`--` comments, CRLF line endings, CP949 — key names are ASCII, the comments are Korean). It is the
shipping client's tunable display-config layer: every value below is a literal scalar in the script
text, so the values themselves need no binary confirmation — they are read directly from the file.
The client loads this script through a Lua-config loader at start-up and copies the recovered scalars
into the render pipeline's display-config fields.

**Loader canonical name (CONFIRMED, CYCLE 12 audit, IDB SHA 263bd994).** The display.lua config
loader is `DisplayConfig_ParseFramerate` — a shared display-config routine that parses both the
framerate key (`DISPLAY_FRAMERATE`) and `DISPLAY_BASE_BRIGHT_MULTI` in one pass, confirming the
shared display-config apply-path provenance that §6.3 and `rendering.md §6.6` both reference for
those two keys. Per-stage shader-constant reads remain IDA-pending (§9.4).

### 9.2 World / background brightness scalars (SAMPLE-VERIFIED values)

| Key | Value | Role | Formula | Confidence (value) |
|-----|------:|------|---------|--------------------|
| `DISPLAY_BASE_BRIGHT_MULTI` | **1.05** | World / background **geometry** brightness multiplier (terrain + buildings + static world meshes — the opaque world bucket). Applied as pixel-shader constant **c0** on the world-geometry pass. | `y = a·x + b`, with `a = 1.05`, `b = 0` (the file comment notes "default 1"). | SAMPLE-VERIFIED |
| `GLOW_BRIGHT_MULTI` | (see `rendering.md`) | Glow / bloom brightness multiplier. Applied as pixel-shader constant **c1**. | see `rendering.md` | SAMPLE-VERIFIED |
| `DISPLAY_LIGHT_RATIO` | **0.5** | **Character** light-colour correction factor only — half-scales character lighting. **NOT a world-geometry amplifier.** **DEAD for any world-geometry render path** — no live world-geometry read path consumes this field; see §9.4. | Scalar in range `0.0 .. 1.0` (file default 1.0). | SAMPLE-VERIFIED (value); CONFIRMED-DEAD (world-geometry read path) |

> **Citation breadcrumb.** A C# constant carrying either value cites this section:
> `// spec: Docs/RE/specs/environment.md §9.2`. `DISPLAY_LIGHT_RATIO` belongs to character lighting
> and is cross-referenced from the character-tint layer in `rendering.md`; it is recorded here for
> completeness because both scalars live in the same `display.lua` config layer.
>
> **CYCLE 12 NOTE (IDB SHA 263bd994, 2026-06-22) — pixel-shader constant assignments.**
> `DISPLAY_BASE_BRIGHT_MULTI` (1.05) and `GLOW_BRIGHT_MULTI` are the **pixel-shader constants c0
> and c1** respectively on the world-geometry / background render pass. `DISPLAY_LIGHT_RATIO` is
> confirmed to have **no live read path** on any world-geometry render pass — it is loaded from the
> script but never consumed by the world render pipeline. It remains active only for the character
> lighting correction path (see §9.4 and `rendering.md`). Do NOT pass `DISPLAY_LIGHT_RATIO` as a
> world-scene shader constant.

### 9.3 Relationship to the existing lighting model (§6)

These scalars are an **additional config layer on top of** the per-area lighting/fog apply-path of
§6.2a. They do not replace any value already in §6:

- `DISPLAY_BASE_BRIGHT_MULTI = 1.05` is a multiplier on the **world-geometry brightness** — the
  background scene energy. It composes with (does not override) the directional-light and
  `OPTION_BRIGHT` ambient-floor math of §6.2a.
- `DISPLAY_LIGHT_RATIO = 0.5` corrects **character** light colour only; it is orthogonal to the
  world-geometry path and to the `OPTION_BRIGHT` ambient floor. See `rendering.md` for the
  character-tint layer (`DISPLAY_CHAR_BRIGHT_*`) that the same `display.lua` defines.

### 9.4 Apply-path — PARTIALLY RESOLVED (CYCLE 12)

> **CYCLE 12 CORRECTION (IDB SHA 263bd994, 2026-06-22).** The apply-path for the two main scalars
> is now partially resolved:
>
> - **`DISPLAY_BASE_BRIGHT_MULTI = 1.05`** is written as **pixel-shader constant c0** on the
>   world/background geometry render pass. This is CODE-CONFIRMED (§9.2).
> - **`GLOW_BRIGHT_MULTI`** is written as **pixel-shader constant c1** on the same pass.
>   CODE-CONFIRMED (§9.2).
> - **`DISPLAY_LIGHT_RATIO`** — CONFIRMED **DEAD** for the world-geometry render path. No live
>   read path consumes this field on the world-geometry render pipeline. It is present in the
>   script and loaded into a display-config field, but nothing in the world-render pass reads it.
>   Its only live consumer is the **character-lighting** correction path (see `rendering.md`).
>   Do NOT use `DISPLAY_LIGHT_RATIO` as a world-geometry shader constant.

The prior "IDA-pending" status for `DISPLAY_BASE_BRIGHT_MULTI` and the pixel-shader constant
assignments is resolved. What remains open is the **full enumeration** of every display-config
field the loader writes and which render stage reads each (specifically the glow/bloom chain and
the character-tint scalars from the same file — those belong to `rendering.md`). The three-field
"adjacent display-config fields" hypothesis is superseded for c0/c1 by the concrete constant
assignments above; the remaining fields stay `static-hypothesis` until a resumed IDA pass confirms.

### 9.5 NOTE — `DISPLAY_BASE_BRIGHT_MULTI = 1.05` is NOT the cause of the near-black world

**Recorded so nobody re-chases it.** `DISPLAY_BASE_BRIGHT_MULTI = 1.05` is a **~neutral +5% uplift**
on world-geometry brightness. A 1.05× multiplier cannot, on its own, produce a near-black scene — it
brightens the world very slightly. The Godot port's "too-dark" world (D3 in the known-issues list) is
the **`OPTION_BRIGHT` additive ambient-floor + `K_ambient = 0` issue already diagnosed in §6.2b**, NOT
the absence of this 1.05 multiplier. Apply `DISPLAY_BASE_BRIGHT_MULTI = 1.05` faithfully for accuracy,
but **do not treat it as the fix for the dark world** — the root cause and its fix are in §6.2b.

> **Source citation:** all `display.lua` values in this section are from the
> `data/script/display.lua` config layer (the shipping client's display-config script). Provenance:
> see `Docs/RE/journal.md`.

### 9.6 NOTE — the display config reader's apparent "×0.5 chain" is a decompiler artifact

**Recorded so nobody re-derives a phantom halving.** The display-config reader that copies the
`display.lua` brightness scalars into the renderer's display-config fields appears, in the decompiler,
to multiply consecutive values by `0.5`. This `×0.5` is **NOT a real halving** — it is an artifact of
a single reused floating-point stack slot being shared across consecutive config-read calls; the
decompiler attributes the slot's prior contents to each read. Each scalar (including every
`DISPLAY_CHAR_BRIGHT_{MULTI|ADD|ALPHA}_{R|G|B}_<STATE>` value — states `DEFAULT`, `CHOICE`, `HIT`,
`ALPHA`, `HIDDEN`, `POISON`, `TYPE`, `ANGER`, `AUTO`) is simply read verbatim from `display.lua` into
its renderer slot. Treat the whole family as **config-driven, un-halved** (confidence HIGH on
config-driven; the apparent 0.5 is spurious). The per-state character-tint *table* itself is owned by
`rendering.md §6.7`.

---

## 8. Known unknowns

1. **Water rendering mechanism: RESOLVED-NEGATIVE.** No renderer, no asset-IO loader, and (per
   `environment_bins.md §1.1`) no stored enable/Y exist in the legacy binary; the Godot layer uses a
   free-choice shader. See §4.
2. **`fog%d.bin` `data_load_flag = 0` fog colour source:** RESOLVED — when the flag is 0 the colour
   table is **derived** from the sky-colour table via a 48-slot 0.75/0.25 blend (§1.1,
   `formats/environment_bins.md §2.4`). The exact source-band channel grouping in that blend is MED.
3. **Indoor fog behaviour:** Whether fog still applies when `indoor_flag = 1` is unverified.
4. **Per-keyframe directional light *direction*:** RESOLVED — the keyframes encode colour only; no
   direction vector is stored per keyframe. The static fallback `(-7, 7, 20)` is the only direction
   the client uses; the day/night cycle does not rotate the sun (§6.2a,
   `formats/environment_bins.md §10.6`).
5. **`light%d.bin` section D (secondary fog scalar):** Near-zero values in all sampled areas;
   exact influence on rendered haze or fog not quantified.
6. **`material%d.bin` unassigned indices:** See `environment_bins.md §3.3`.
7. **`cloud_cycle%d.bin` cloud ID 101:** Possible "no cloud" sentinel; unconfirmed.
8. **`weather%d.bin` full layout:** All-zero in sampled areas; needs a rain/snow area sample.
9. **Sky object render pass order:** The client renders sky layers as concentric domes in a
   specific draw order (cloud dome, star dome, sun/moon, lens flare). The precise draw-order
   is not relevant to the format spec but is relevant to Godot's sky setup.
10. **`sky%d.box` geometry:** VFS-only; sample-unverified vertex decode — see `formats/sky.md §A`.
11. **Lighting apply-path numeric DEFAULTS:** RESOLVED for the two ambient defaults:
    - **`K_ambient`** — the global ambient multiplier (§6.2a). **CONFIRMED 0.0** (single reader, zero
      writers, static init 0.0). The per-keyframe ambient term is inert in the shipping client; the
      prior "set by the sky-detail/quality option" hypothesis is DENIED.
    - **Ambient base colour** — **CONFIRMED static `(0,0,0)`** at init, then keyframe-driven (§6.2a).
    - **`OPTION_BRIGHT` default** — **CONFIRMED 100** (INI default; clamp `[1,100]` → 100). At the
      default the additive ambient floor is +1.0 (full white over the `(0,0,0)` base), correcting the
      earlier assumed ~50 / +0.5. The faithful Godot ambient floor default is **1.0**, not 0.5.
    - **On-disk `DoOption.ini` override (UNVERIFIED — the one remaining residual):** whether a
      player's saved INI carries a brightness below 100. A single runtime read of the stored
      brightness after options-load settles it (expected i32, percent, default 100); layout-neutral.
    - **Fog fractions** — observed live area-1 `0.75 / 0.98` (corrected in §6.4) vs. the previous
      `0.5 / 0.9` stand-ins; read the parsed file at runtime.
    - **EXP/EXP2 fog modes** — **confirmed NOT driven on the lighting tick** (the shared fog-distance
      setter only ever writes the LINEAR shape — LINEAR mode + density 0.0). The modes exist in the
      runtime struct layout, but no path was observed switching to exponential density on this tick;
      whether any other quality-tier path does remains unverified.
12. **`display.lua` brightness-scalar apply-path (§9.4): PARTIALLY RESOLVED (CYCLE 12).**
    `DISPLAY_BASE_BRIGHT_MULTI = 1.05` → **pixel-shader constant c0** (world/background geometry
    pass, CODE-CONFIRMED). `GLOW_BRIGHT_MULTI` → **pixel-shader constant c1** (same pass,
    CODE-CONFIRMED). `DISPLAY_LIGHT_RATIO = 0.5` — **DEAD on the world-geometry path** (no live
    world-geometry read path, CONFIRMED). Its only live consumer is the character-lighting
    correction path (see `rendering.md`). The full character-tint / glow chain enumeration from the
    same `display.lua` remains open (see `rendering.md §6.7`). NOTE: the 1.05 multiplier is a
    ~neutral +5% uplift and is **not** the cause of the dark world (§9.5).

---

## Cross-references

- **Format specs:** `Docs/RE/formats/environment_bins.md` (all colour `.bin` byte layouts; §10 pins
  the colour domains and apply-path field facts this spec's §6 consumes, including the CONFIRMED
  `K_ambient` = 0.0 and `OPTION_BRIGHT` default 100),
  `Docs/RE/formats/sky.md` (`sky%d.box` geometry + parser load-order / day-cycle view)
- **Render pipeline:** `Docs/RE/specs/rendering.md` (the glow/bloom config and the per-state
  character-tint table from the same `data/script/display.lua` layer — §9 here owns the
  world-brightness scalars only)
- **Terrain:** `Docs/RE/formats/terrain.md`, `Docs/RE/formats/terrain_layers.md`
- **Game loop:** `Docs/RE/specs/game_loop.md` (clock, tick rate)
- **Glossary:** `Docs/RE/names.yaml` (flag for canonicalisation: `K_ambient` ambient gate,
  `OPTION_BRIGHT` brightness slider, `DISPLAY_BASE_BRIGHT_MULTI` / `DISPLAY_LIGHT_RATIO` display-config
  scalars)
- **Provenance:** `Docs/RE/journal.md`
- **Godot implementation files:** `05.Presentation/MartialHeroes.Client.Godot/World/EnvironmentNode.cs`,
  `05.Presentation/MartialHeroes.Client.Godot/World/WaterRenderer.cs`

---

## Addendum — CYCLE 11 / Block A: char-select preview fog is forced OFF (binary-confirmed, static)

> Verification refresh, IDB SHA **263bd994**, static-only (CYCLE 11 / Block A, 2026-06-22).
> This addendum **resolves the open question** previously logged in §6.4a (the "whether fog is forced
> OFF for the char-preview row" item, formerly LOW / DBG-pending). The binary now settles it
> **statically**: **fog is forced fully off** in the character-select preview scene. The earlier
> "do NOT assert fog OFF on char-select" caveat is **superseded** — the binary wins.

**What the binary shows.** The character-select 3D scene builder, at the very top of its build, calls a
char-select-specific environment helper that does three things in order:

1. stops the ambient map sound cue (id **924000001**) on its channel;
2. zeroes two scene-environment fields; and
3. calls the **fog-blend setter** with a literal factor of **0.0**.

The fog-blend setter computes `fog_blend = base × factor × 1.4` (the same `base·factor·1.4` setter the
in-world weather tick uses). With the factor pinned to **0.0**, the resulting blend is **0.0** — fog
contributes nothing. *([CONFIRMED]* the literal-0.0 call and the multiply form.)*

**Why it stays off (the part §6.4a could not previously rule out).** The concern was that a per-frame
fog apply might re-push LINEAR fog at keyframe 29 and override the build-time 0.0. The character-select
per-frame tick was re-walked: it drives only the scene render, the deferred populate, the camera
boom/zoom, the preview turntable, tooltips and the sound tick — it does **not** call the in-world
weather/fog tick at all. The in-world dynamic-fog ramp lives on a **different scene's** per-frame tick.
Therefore the build-time `fog_blend = 0.0` **holds for the entire char-select scene lifetime**.
*([CONFIRMED]* the char-select tick's callee set contains no fog re-apply.)*

**Contrast with the in-world path.** In-world, the same fog-blend setter is driven from the weather tick
with a **live, ramped factor** (so fog is dynamic). Char-select is the only path that pins the factor to
a literal 0.0 at build. The two paths share the setter but differ entirely in the factor they feed it.

**Net for the Godot port.** The char-select preview renders with **no fog** (alongside the already-pinned
area-0 map, fixed time-of-day ≈ 14:30, and the single ambient effect). A fog-free char-select also
matches the dark-stone-temple visual oracle. Implement char-select with fog disabled; do not apply the
in-world weather fog ramp to it.

> spec path: `// spec: Docs/RE/specs/environment.md`
