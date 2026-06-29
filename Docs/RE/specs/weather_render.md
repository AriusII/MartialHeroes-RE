# Spec: Weather Rendering — Rain and Snow Precipitation Particle Systems

> Clean-room spec. Neutral description only — no decompiler pseudo-code, no binary addresses,
> no decompiler-generated identifiers. Promoted from dirty-room static analysis under EU
> Software Directive 2009/24/EC Art. 6, solely to achieve interoperability. This spec
> documents the **CPU-driven billboard precipitation systems** (rain and snow): singleton
> lifecycle, particle geometry and texture paths, fall simulation, rain splash subsystem,
> weather grid state selection, quality-tier alpha scaling, and the D3D9 render-pass recipe.
>
> The `weather%d.bin` binary format and dead `weather_rain.bin` data are owned by
> `Docs/RE/formats/environment_bins.md §7–§8`. Sky and background pass ordering is in
> `Docs/RE/formats/sky.md`. The shared `GParticleBuffer` billboard infrastructure used here
> is documented at `Docs/RE/formats/sky.md §A.4` and `§D.5.1`.
>
> Every weather-system constant cited in C# must reference this file:
> `// spec: Docs/RE/specs/weather_render.md`.

> **Verification banner**
> - **verification:** *static-confirmed* for all principal findings in this document, recovered
>   by following the control flow of `RainSystem_ConstructAndLoad`, `SnowSystem_ConstructAndLoad`,
>   `RainSystem_Start`, `SnowSystem_Start`, `RainSystem_Update`, `SnowSystem_Update`,
>   `RainStreaks_Draw`, `RainSplash_Draw`, `SnowFlakes_Draw`, `Weather_InitFromBin`,
>   `Weather_SelectDayRow`, `Weather_ApplyCode`, and `EnvScene_TickThrottled`. The full call
>   chain from `Env_MapSetAndLoadArea` through `RenderPass_TransparentAndParticles` was traced.
>   Items tagged `[debugger-confirm]` are static hypotheses awaiting a live `?ext=dbg` session
>   before treating them as implementation facts.
> - **ida_anchor:** f61f66a9ae0ec1e946105b2ecff76e8930cb1d1367df64e5688a5266f5ad9963
> - **readiness:** IMPLEMENTATION-READY for all items including the D3D9 texture-stage
>   `COLOROP`/`ALPHAOP` values (`D3DTOP_MODULATE2X` (5) / `D3DTOP_SELECTARG1` (2)) and the
>   weather-grid column derivation (day-counter rows × 10-day cycle, naturally bounded
>   hour-of-day columns 0–23), both confirmed static (IDB anchor f61f66a9, 2026-06-29).
> - **Deepening pass (2026-06-29):** deep-3D cartography static pass closed DBG items #1
>   (texture-stage: COLOROP=MODULATE2X / ALPHAOP=SELECTARG1, exact TSS state table added to §8.2)
>   and #2 (weather grid: timeBlock is the day-counter with 10-day cycle, TOD_sec/3600 gives
>   naturally bounded hour column 0–23; §6.1 updated). Quality option source pinned in §7
>   (OPTION_WEATHER = quality singleton dword index 8). No values changed from the prior pass.
> - **evidence:** [static-ida]
> - **resolves:** `Docs/RE/formats/sky.md §E.5` — the step claiming precipitation draws in
>   the sky pass under an additive blend is **overridden**: all confirmed precipitation draws
>   (`RainStreaks_Draw`, `RainSplash_Draw`, `SnowFlakes_Draw`) execute in
>   `RenderPass_TransparentAndParticles` with standard alpha blend (SRCALPHA/INVSRCALPHA).
>   No active precipitation draw was found in the sky pass (see §8 and §11).
> - **refines:**
>   `Docs/RE/formats/environment_bins.md §7.5` — the quality-tier alpha immediates recovered
>   on IDB anchor f61f66a9 differ from the committed table; see §7 of this spec for the
>   binary-won correction;
>   `Docs/RE/formats/sky.md §E.5` — the precipitation draw pass and blend mode are corrected
>   as above; the `WeatherParticle_Draw` label in that step is a naming overlap with snow flakes
>   (see §11 item 2).
> - **cross-links:**
>   `Docs/RE/formats/environment_bins.md §7` (weather grid format and code encoding),
>   `Docs/RE/formats/environment_bins.md §7.5` (quality alpha table — corrected here in §7),
>   `Docs/RE/formats/environment_bins.md §8` (weather_rain.bin confirmed dead data),
>   `Docs/RE/formats/sky.md §A.4`, `§D.5.1` (shared 20-byte billboard quad + GParticleBuffer),
>   `Docs/RE/formats/sky.md §D.6` (moon billboard — also in the transparent/particle pass),
>   `Docs/RE/formats/sky.md §E.5` (sky pass — corrected by §11 item 2 of this spec),
>   `Docs/RE/specs/render_pipeline.md` (pass ordering context).

---

## Status

| Item | Confidence |
|---|---|
| Two independent precipitation systems (rain, snow) exist | CONFIRMED |
| Both are lazily-constructed singletons | CONFIRMED |
| Hardcoded texture paths (`rains.dds`, `rain_drop.dds`, `snow.dds`) | CONFIRMED |
| Particle capacity: 500 per system; 5 batches × (intensity × 100) quads | CONFIRMED |
| Billboard sizes: rain streak 64.0, rain splash 0.5, snow flake 2.0 | CONFIRMED |
| Spawn volume is player-position-locked; recycle plane = player.y − 8.0 | CONFIRMED |
| Fall speeds: rain = intensity×4+4; snow = intensity×4 | CONFIRMED |
| Intensity range gate [0.1, 1.0] | CONFIRMED |
| Rain falls straight; snow drifts via 48-float private turbulence table | CONFIRMED |
| Turbulence table values (3 banks × 16 floats) | CONFIRMED |
| Rain splash subsystem (terrain-intersection ground splashes, `rain_drop.dds`) | CONFIRMED |
| Rain ambient sound id 924000001; snow has no sound | CONFIRMED |
| No coupling to `wind%d.bin` or `.bud`-sway wind | CONFIRMED |
| `weather_rain.bin` is dead data — not read by either system ctor | CONFIRMED |
| Weather grid decoding: row = timeBlock%10, col = TOD/3600; type = code/10 | CONFIRMED |
| State hysteresis: current code / last-applied stored separately | CONFIRMED |
| Quality tier alpha scalars (corrects `environment_bins.md §7.5`) | CONFIRMED |
| Draw pass: `RenderPass_TransparentAndParticles`, standard alpha blend | CONFIRMED |
| Blend: SRCALPHA/INVSRCALPHA; alpha-test on; Z-write off | CONFIRMED |
| Draw order: rain streaks → rain splashes → snow flakes | CONFIRMED |
| Fog coupling: precipitation ramps fog density proportional to particle count | CONFIRMED |
| ~50 ms update throttle; 120-tick day-row reselect cadence | CONFIRMED (static) |
| D3D9 texture-stage COLOROP = MODULATE2X (5), ALPHAOP = SELECTARG1 (2) | CONFIRMED (static, 2026-06-29) |
| timeBlock = day-counter (10-day cycle); col = TOD_sec/3600, hour 0–23, naturally bounded | CONFIRMED (static, 2026-06-29) |
| Snow path exercised against a real snowing area at runtime | `[debugger-confirm]` |

---

## 1. System Overview

**Confidence: CONFIRMED.**

Weather rendering is a **real CPU-driven billboard particle subsystem** — not absent and not
a subfunction of `.bud`-sway or cloud tinting. There are two independent precipitation systems:

- **RAIN**: falling streaks (`rains.dds`) plus terrain-intersection ground splashes
  (`rain_drop.dds`), with a looping ambient sound.
- **SNOW**: drifting flakes (`snow.dds`), no sound.

Both systems are **lazily-constructed singletons** that share the `GParticleBuffer` billboard
infrastructure also used by the sun and moon sky sprites (see `Docs/RE/formats/sky.md §A.4`).
Particle geometry is built entirely from hardcoded constants and a pseudo-random seed; neither
system reads the `weather_rain.bin` data file, confirming that file is dead editor data
(`Docs/RE/formats/environment_bins.md §8`).

A per-area 240-byte `weather%d.bin` grid (§6) controls WHEN each system is active and at what
intensity. Only one precipitation type is active at a time (mutually exclusive by type code).

---

## 2. Lifecycle and State Machine

**Confidence: CONFIRMED.**

### 2.1 Construction (lazy, on first singleton access)

Each system is constructed on its first `GetSingleton` call
(`RainSystem_GetSingleton`, `SnowSystem_GetSingleton`). The constructor:

1. Allocates the internal particle position arrays (see §3.1).
2. Seeds the template array from the pseudo-random number generator (see §3.2).
3. Loads the hardcoded textures with a lifetime hint of 10000.
4. Creates and sizes the `GParticleBuffer`.

No `weather%d.bin` or `weather_rain.bin` bytes are read during construction.

### 2.2 Per-Area Initialisation (`Weather_InitFromBin`, called from `Env_MapSetAndLoadArea`)

1. Reads the 240-byte `weather%d.bin` body into the weather state object.
2. Reads the current quality option and applies the per-tier alpha scalars to the rain particle
   alpha, snow particle alpha, and fog alpha (see §7).
3. Calls `Weather_SelectDayRow` to pick the initial active precipitation type.

### 2.3 State Selection (`Weather_SelectDayRow` → `Weather_ApplyCode`)

`Weather_SelectDayRow` looks up the grid entry for the current `(timeBlock, TOD)` pair
(see §6) and forwards the resulting code to `Weather_ApplyCode`. Hysteresis: the current code
is stored at weather-state offset +240 and the last-applied code at +241; `Weather_ApplyCode`
only performs a transition when the code has changed.

**On a type transition**, `Weather_ApplyCode`:
- Stops the previously active system (ramps particle count to zero, stops rain sound).
- Clears the lens-flare suppression byte (lens flare is suppressed while precipitation is active).
- Starts the new system at the decoded intensity (re-sets the suppression byte, calls
  `RainSystem_Start` or `SnowSystem_Start`).

### 2.4 Activation — `RainSystem_Start` / `SnowSystem_Start`

Guard: intensity must be in the range [0.1, 1.0] — calls outside this range are no-ops.

1. Sets the **fall-speed scalar**: rain = `intensity × 4.0 + 4.0`; snow = `intensity × 4.0`.
2. Sets the **active per-batch count**: `floor(intensity × 100)` particles per batch (five
   batches drawn per frame → up to 500 quads at full intensity).
3. Spawns live particle positions in a grid around the local player's world position
   (template offsets + player position + column/row grid offsets). A fallback default position
   is used if no local player is available.
4. Sets the active flag so the update loop begins ramping up.
5. **RAIN ONLY:** plays looping 2D ambient sound id 924000001 on channel 3.

### 2.5 Deactivation — `RainSystem_Stop` / `SnowSystem_Stop`

Sets the count target to zero; the update loop ramps the active count down gracefully (not an
instant clear). **RAIN ONLY:** stops ambient sound id 924000001 on channel 3.

### 2.6 Per-Frame Tick (`EnvScene_TickThrottled`)

On a **~50 ms accumulator throttle** (confirmed static; live cadence is `[debugger-confirm]`):
- Calls `RainSystem_Update` (= `Weather_StepPrecipitation`) and `SnowSystem_Update`.
- Every **120 ticks**, re-runs `Weather_SelectDayRow` so weather can change as the in-game day
  advances without requiring a map reload.
- A tick counter wraps at 60000.

Changing the quality option mid-session (via the options menu) re-runs `Weather_InitFromBin`,
re-applying tier alpha scalars and re-selecting the day row.

---

## 3. Particle Geometry and Textures

**Confidence: CONFIRMED.**

### 3.1 Internal Arrays

**Rain system** internal arrays (sequential):
- Template: 100 vec3 entries — `rand()`-seeded spawn offsets (see §3.2).
- Live streak positions: 400 vec3 entries drawn as four 100-entry batches.
- Live streak positions: 100 vec3 entries — the fifth batch.
- Splash positions: 500 vec3 entries — built each frame by terrain intersection (see §5).

**Snow system** internal arrays:
- Template: 100 vec3 entries — `rand()`-seeded spawn offsets.
- Live flake positions: 400 vec3 entries drawn as four 100-entry batches.
- Live flake positions: 100 vec3 entries — the fifth batch.
  (Snow has no splash array; three position arrays total.)

Both systems use the same **5-batch draw shape**: four 100-stride chunks from the 400-entry
block plus the 100-entry block, yielding up to 500 quads at full intensity.

### 3.2 Template Seed (ctor, 100 entries per system)

For entry index `i` in 0..99:

| System | y component | x component | z component |
|--------|------------|-------------|-------------|
| Rain | `i + 16.0` (range 16.0–115.0) | `−(rand() % 100)` | `−(rand() % 100)` |
| Snow | `i + 30.0` (range 30.0–129.0) | `−(rand() % 100)` | `−(rand() % 100)` |

Snow spawns higher than rain (30 vs 16 base y offset) to allow flakes more drift time before
reaching the kill plane.

### 3.3 GParticleBuffer Construction

| System | Max particles | sizeA | sizeB | Texture | Billboard size |
|--------|:------------:|------:|------:|---------|:-------------:|
| Rain (streaks) | 500 | 64.0 | 4.0 | `rains.dds` | 64.0 world units |
| Snow | 500 | 8.0 | 2.0 | `snow.dds` | 2.0 world units |

The rain splash buffer (`rain_drop.dds`) uses billboard size 0.5; it is set per-draw and
restored to 64.0 after the splash draw (see §5).

### 3.4 Hardcoded Texture Paths

All texture paths are resolved via the VFS (not from `weather%d.bin`):

| Texture | Path | Used by |
|---------|------|---------|
| Falling rain streaks | `data/sky/texture/rains.dds` | Rain streak draw |
| Rain ground splashes | `data/sky/texture/rain_drop.dds` | Rain splash draw |
| Falling snow flakes | `data/sky/texture/snow.dds` | Snow flake draw |

### 3.5 Vertex Format (shared with sun/moon; see `Docs/RE/formats/sky.md §A.4`)

Each particle quad is a camera-facing billboard with four vertices at 20 bytes per vertex
(12-byte XYZ position + 8-byte UV). Quads are CPU-built by the `GParticleBuffer` lock/append
path and submitted as indexed triangle lists.

---

## 4. Fall Simulation

**Confidence: CONFIRMED.**

The fall update runs inside the ~50 ms tick (§2.6). The **kill plane** is `player.y − 8.0`; any
particle whose y-component falls at or below this plane is immediately recycled to the top
of the spawn column (`template_base + player_position + grid_offset`).

**Drawn particle count** per frame is controlled by the intensity ramp: the active count
increments each tick until it reaches the count target (`floor(intensity × 100)`).

### 4.1 Rain Fall (straight vertical)

`RainSystem_Update`: for each live particle, `y −= fall_speed`. No horizontal drift. On kill,
respawn to template + player position. The system also builds the splash array on each tick
(see §5).

### 4.2 Snow Fall (drifting with turbulence)

`SnowSystem_Update`: uses the private 48-float turbulence table described in §4.3.
A global rotating index `k` (range 0..15) advances per particle. Per particle:

```
y −= table_fall[k]  × fall_speed
x −= table_fall[k]  × fall_speed × table_xdrift[k]
z −= table_fall[k]  × fall_speed × table_zdrift[k]
```

On kill, respawn relative to the local player position. This produces the swirling, varied
descent visible for snow; rain has no equivalent horizontal component.

### 4.3 Snow Turbulence Table (private constant, no data coupling)

The turbulence table is a **hardcoded private constant** — it is not read from `wind%d.bin`
and has no coupling to the `.bud`-sway wind. The table contains 48 floats in three 16-entry
banks:

| Bank | Indices | Role | Values (0..15) |
|------|---------|------|----------------|
| 0 | 0–15 | z-drift scalar (`table_zdrift`) | 0.3, 0.2, 0.1, 0.0, 0.1, 0.2, 0.1, 0.1, 0.3, −0.2, 0.1, 0.0, −0.1, −0.2, −0.1, −0.1 |
| 1 | 16–31 | x-drift scalar (`table_xdrift`) | (same 16 values as bank 0) |
| 2 | 32–47 | fall magnitude scalar (`table_fall`) | 1.0, 1.1, 0.9, 1.0, 1.1, 1.0, 0.9, 1.0, 1.0, 1.1, 0.9, 1.0, 1.1, 1.0, 0.9, 1.0 |

The rotating index `k` is a global integer that advances per particle and wraps after 16,
creating the impression of varied snowfall behavior across different flakes.

---

## 5. Rain Splash Subsystem

**Confidence: CONFIRMED.**

After the fall simulation each tick, `RainSystem_Update` builds the splash array for the
`rain_drop.dds` draw:

1. For each live rain streak position, sample terrain height at the particle's world XZ
   coordinates via the terrain manager.
2. Where the terrain height is at or above the particle's y (the streak has reached the ground),
   record a splash entry `(x, terrain_y, z)` in the splash array and increment the splash count.
3. The splash array is drawn as a single quad-batch at billboard size 0.5 (restored to 64.0
   after the draw).

The splash array capacity matches the streak capacity (500 entries). The per-frame splash count
is bounded by the number of active streak particles.

---

## 6. Weather Grid State Selection

**Confidence: CONFIRMED. Corroborates `Docs/RE/formats/environment_bins.md §7`.**

The active precipitation type and intensity are read from a per-area `weather%d.bin` file
(where `%d` is the area identifier). The binary is 240 bytes: a 10×24 grid of unsigned 8-bit
code values (10 time-block rows × 24 hour columns).

### 6.1 Grid Lookup

```
row  = timeBlock % 10          (timeBlock is the global day-counter, wraps at 360000)
col  = TOD_sec / 3600          (integer-truncated hour of day 0–23)
code = grid[row * 24 + col]   (unsigned byte)
```

- `type      = code / 10`         (integer division; 0 = clear, 1 = rain, 2 = snow)
- `intensity = (code % 10) × 0.1` (range 0.0–0.9)

**CONFIRMED static, 2026-06-29.** The day-counter modulo 10 yields the row, implementing a 10-day rotating weather pattern. Integer division of seconds-of-day by 3600 yields the hour column (0–23). Natural bounds: row ≤ 9, column ≤ 23 → byte index ≤ 239 < 240; no out-of-bounds risk and no clamp is applied. **Note:** the global time-of-day variable is internally named with an `ms` suffix but holds **seconds** of day — the name is a misnomer confirmed statically. This resolves the open item in `Docs/RE/formats/environment_bins.md §7.6`.

### 6.2 State Hysteresis

The weather state object stores the current decoded code (offset +240) and the last-applied
code (offset +241). `Weather_ApplyCode` only performs a precipitation type transition when the
two values differ, preventing unnecessary start/stop cycling on repeated `Weather_SelectDayRow`
calls.

---

## 7. Quality-Tier Alpha Scaling

**Confidence: CONFIRMED. Corrects `Docs/RE/formats/environment_bins.md §7.5`.**

`Weather_InitFromBin` reads the weather quality option (three tiers: 1 highest, 3 lowest) from the
graphics-quality option singleton at dword index 8 (`+0x20`), labelled `OPTION_WEATHER`. The tier
value is stored into the weather-state object at byte offset +0xF4 (244). The function then applies
per-tier f32 scalars to the rain particle alpha, snow particle alpha, and fog/sky alpha.
The decoded immediates on IDB anchor f61f66a9 are:

| Tier | Rain particle alpha | Snow particle alpha | Fog / sky alpha |
|:----:|:-------------------:|:-------------------:|:---------------:|
| 1 (highest) | 1.0 | 1.0 | 1.0 |
| 2 | 0.4 | 0.4 | 0.8 |
| 3 (lowest) | 0.15 | 0.15 | 0.7 |

> **Correction to `environment_bins.md §7.5`:** the committed table lists tier-2 values of
> approximately 0.7/0.825 and tier-3 values of approximately 0.65/0.8125. The values decoded
> from this build (f61f66a9) are 0.4/0.8 (tier 2) and 0.15/0.7 (tier 3). The earlier reading
> either targeted a different build or was an approximation. The immediates recovered here are
> the binary-won ground truth for this IDB anchor; `environment_bins.md §7.5` must be updated
> to match (see §11 item 1).

The rain particle alpha scalar feeds the per-draw alpha render parameter of the rain
`GParticleBuffer`; the fog/sky scalar feeds the fog density setter. The snow alpha scalar
feeds the snow `GParticleBuffer`.

---

## 8. Render Pass Recipe

**Confidence: CONFIRMED (blend state and draw order); `[debugger-confirm]` for texture-stage
COLOROP/ALPHAOP enum.**

### 8.1 Pass Placement

All three precipitation draws execute in `RenderPass_TransparentAndParticles` — the same pass
as the moon billboard (see `Docs/RE/formats/sky.md §D.6`) — and run AFTER transparent world
geometry and BEFORE the general particle-effect list and the lens-flare halo.

> **This overrides `Docs/RE/formats/sky.md §E.5 step 3`**, which claims a `WeatherParticle_Draw`
> in the sky/additive pass. No active precipitation draw was found in the sky pass; the
> xref-confirmed draws are all in the transparent/particle pass with standard alpha blend.
> The label `WeatherParticle_Draw` in that step is a naming overlap with the snow flake draw
> function; see §11 item 2 for the required correction.

### 8.2 D3D9 Render State (established before weather draws)

| Render state | D3D9 enum (int) | Value | D3D9 constant |
|---|---|---|---|
| `D3DRS_ALPHABLENDENABLE` | 27 | 1 | TRUE |
| `D3DRS_SRCBLEND` | 19 | 5 | `D3DBLEND_SRCALPHA` |
| `D3DRS_DESTBLEND` | 20 | 6 | `D3DBLEND_INVSRCALPHA` |
| `D3DRS_ALPHATESTENABLE` | 15 | 1 | TRUE |
| `D3DRS_ZWRITEENABLE` | 14 | 0 | FALSE |
| `D3DRS_LIGHTING` | 137 | 0 | FALSE |
| `D3DRS_FOGENABLE` | 28 | 0 | FALSE |
| `D3DRS_CULLMODE` | 22 | 1 | `D3DCULL_CW` |

Z-test remains enabled (Z-write is off; Z-test is on for transparent sorting).

The following texture-stage state is applied immediately before the sky-particle and weather draws
(CONFIRMED static, 2026-06-29):

| Texture-stage state | D3D9 TSS slot | Value | D3D9 constant |
|---|---|---|---|
| `COLOROP` (TSS 1) | 1 | **5** | `D3DTOP_MODULATE2X` |
| `COLORARG1` (TSS 2) | 2 | 2 | `D3DTA_TEXTURE` |
| `COLORARG2` (TSS 3) | 3 | 0 | `D3DTA_DIFFUSE` |
| `ALPHAOP` (TSS 4) | 4 | **2** | `D3DTOP_SELECTARG1` |
| `ALPHAARG1` (TSS 5) | 5 | 2 | `D3DTA_TEXTURE` |

Result: colour = texture × diffuse × 2 (`MODULATE2X`); alpha = texture alpha only (`SELECTARG1`). After the weather draws complete, `COLOROP` is reset to `D3DTOP_MODULATE` (4) before `ParticleEffectList_DrawAll` runs. The prior static hypothesis of plain `D3DTOP_MODULATE` is therefore **CORRECTED** — the actual value is `D3DTOP_MODULATE2X` (5). The `D3DTOP_SELECTARG1` alpha-op means precipitation particle alpha comes entirely from the texture, not from the vertex diffuse alpha channel.

### 8.3 Draw Order (consecutive, all via `GParticleBuffer`)

1. **Rain streaks** (`RainStreaks_Draw`, rain singleton) — lock buffer; append five 100-entry
   quad-batches from the streak arrays; set buffer texture to `rains.dds`; draw as indexed
   triangle list; billboard size 64.0.

2. **Rain splashes** (`RainSplash_Draw`, rain singleton) — gated by the current splash count;
   set billboard size to 0.5; append one quad-batch from the splash array; set buffer texture
   to `rain_drop.dds`; draw; restore billboard size to 64.0.

3. **Snow flakes** (`SnowFlakes_Draw`, snow singleton) — lock buffer; append five 100-entry
   quad-batches from the flake arrays; set buffer texture to `snow.dds`; draw.

Only the systems that are currently active (non-zero count) produce visible draws; inactive
systems emit zero quads.

---

## 9. Fog Coupling

**Confidence: CONFIRMED.**

Both update routines push a fog density scalar into the fog singleton on each ramp step:

```
fog_scalar = count_current × 0.01
```

Rain and snow use distinct fog setter paths, allowing the fog system to distinguish the source.
The scalar ramps proportionally as the active particle count ramps up or down, so precipitation
onset and fade-out smoothly modulate ambient fog density. Quality-tier fog alpha (§7) is applied
on top of this scalar at initialisation time.

---

## 10. Debugger-Confirm Items

| # | Item | Status / What to confirm |
|---|---|---|
| 1 | Texture-stage COLOROP/ALPHAOP | **CLOSED (static, 2026-06-29).** COLOROP = `D3DTOP_MODULATE2X` (5); ALPHAOP = `D3DTOP_SELECTARG1` (2). Exact TSS state table added to §8.2. |
| 2 | `timeBlock` unit and `TOD_ms` column derivation | **CLOSED (static, 2026-06-29).** timeBlock is the global day-counter (wraps at 360000); col = `TOD_sec / 3600` (hour 0–23); naturally bounded, no clamp needed. §6.1 updated. Resolves `environment_bins.md §7.6`. |
| 3 | ~50 ms throttle and 120-tick cadence at runtime | Confirm wall-clock behaviour of `EnvScene_TickThrottled` tick accumulator and the 120-tick reselect interval under normal game frame rates. |
| 4 | Snow path against a real snowing area | The 95-file `weather%d.bin` corpus surveyed in `environment_bins.md §7.5` contained no snow-type codes. Confirm the `SnowSystem_Update` / `SnowFlakes_Draw` path is exercised against an area where a snow code is present at the current TOD. |
| 5 | Splash recycling cap under heavy rain | Confirm whether the per-frame splash count is clamped when all 500 streak particles intersect terrain simultaneously (the build loop appends up to the active streak count; capacity is 500). |

---

## 11. Corrections to Sibling Specs

The following changes to committed specs are indicated by the findings in this document.
Each must be applied as a separate edit to the named file.

| Spec | Section | Required change |
|---|---|---|
| `Docs/RE/formats/environment_bins.md` | §7.5 | Replace the tier-2 and tier-3 alpha scalars with the binary-won values: tier-2 rain/snow = 0.4, fog/sky = 0.8; tier-3 rain/snow = 0.15, fog/sky = 0.7. Note the IDB anchor f61f66a9 as the source. |
| `Docs/RE/formats/sky.md` | §E.5 step 3 | Correct the pass assignment: precipitation draws (`RainStreaks_Draw`, `RainSplash_Draw`, `SnowFlakes_Draw`) execute in `RenderPass_TransparentAndParticles` with standard alpha blend (SRCALPHA/INVSRCALPHA), NOT in the sky/background pass under additive blend. Note that the function label `WeatherParticle_Draw` in that step is the snow flake draw; it belongs to the transparent/particle pass. |

> **IDB annotation note for toolsmith (do not edit `names.yaml` here):** the following
> functions carry mislabels in IDB anchor f61f66a9 and should be retagged:
> `RainSystem_GetSingleton` is currently labeled as a battle-controller singleton;
> `RainStreaks_Draw` and `RainSplash_Draw` are currently labeled as sky-particle draw
> functions. Proposed canonical names for `names.yaml`: `RainSystem_GetSingleton`,
> `RainSystem_Start`, `RainSystem_Stop`, `RainSystem_Update`, `RainStreaks_Draw`,
> `RainSplash_Draw`, `SnowSystem_GetSingleton`, `SnowSystem_Start`, `SnowSystem_Stop`,
> `SnowSystem_Update`, `SnowFlakes_Draw`, `Weather_ApplyCode`, `Weather_InitFromBin`,
> `Weather_SelectDayRow`, `EnvScene_TickThrottled`, `RainSystem_LoadTexturesAndParticles`,
> `SnowSystem_LoadTextureAndParticles`, `SnowSystem_Destructor`.
