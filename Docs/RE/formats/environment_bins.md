# Format: environment bins  (per-area sky, fog, and sky-dome colour data)

> Clean-room spec. Neutral description only — NO sample bytes, NO decompiler pseudo-code,
> NO binary addresses. Consumed by Assets.Parsers. Every offset an engineer cites must
> reference this file.
>
> Scope note: this document covers the **per-area environment binary family** that lives under
> `data/sky/dat/`. It specifies `map_option%d.bin`, `fog%d.bin`, `material%d.bin`,
> `stardome%d.bin`, `clouddome%d.bin`, `cloud_cycle%d.bin`, `weather%d.bin`, and
> `weather%d_rain.bin`. The closely related per-map directional-light, point-light, and wind
> files (`light%d.bin`, `point_light%d.bin`, `wind%d.bin`) are formally specified in
> `Docs/RE/formats/terrain_layers.md §6–8`; supplementary sample-verified / loader-resolved
> corrections to those sections are noted at the end of this document (§9 for `light%d.bin`,
> §12 for `wind%d.bin`). The **colour domains** of these
> fields (which colours are byte D3DCOLOR vs. float [0,1]) and how they feed the runtime
> lighting/fog math are pinned in §10 and consumed by `Docs/RE/specs/environment.md`.

---

## Status block

```
verification:   sample-verified   # all 8 env-bin family sizes + fog start/end + ambient-floor chain matched against a real VFS sample
ida_reverified: 2026-06-20         # CYCLE 7 (2026-06-20), IDB SHA 263bd994 — static re-walk added the in-memory fog struct layout + the material/light synth-default immediates
ida_anchor:     263bd994
evidence:       [static-ida, vfs-sample]
conflicts:      none-open         # campaign-10 D6 found NO conflicts on the env-bin tables; all core numerics RE-CONFIRMED
cycle7_additions:                  # CYCLE 7 static re-walk (all HIGH immediates unless noted)
  - in-memory engine fog struct layout pinned: type@+44 (1=EXP/2=EXP2/3=LINEAR), colour@+48 (4-byte ARGB), start@+52 f32, end@+56 f32, density@+60 f32 (§10.3)
  - material SYNTH default (missing material.bin): ambient 0.30000001, diffuse 0.8, specular 0.0, emissive 0.0 (§3.4)
  - light SYNTH ramp (missing light.bin): scale 80.0; per-keyframe intensity = kf_idx × 0.04 (=1/25) clamped, colour = 1.0 − intensity, 48 keyframes (§9.5)
  - fog on-disk start/end also describable as struct-relative (start@+208, end@+212, then data_load flag + 192-byte colour block; 204 B total) — same bytes as §2.1's 0x00/0x04/0x08/0x0C
```

> **CAMPAIGN 10 Block D6 two-witness re-verification (build `263bd994`).** Every CORE numeric claim of
> this document RE-CONFIRMS **[sample-verified]** against a real VFS sample and the build-`263bd994`
> loaders, with **NO conflicts on any env-bin byte table**:
> - **All eight env-bin family sizes are byte-exact** across the full per-area corpus (no size drift):
>   `fog%d.bin` 204 B (82 files), `material%d.bin` 9792 B (82), `stardome%d.bin` 9216 B (55),
>   `clouddome%d.bin` 23040 B (59), `cloud_cycle%d.bin` 70 B (59), `light%d.bin` 5312 B (164),
>   `map_option%d.bin` 40 B (83), `weather%d.bin` 240 B (83), `weather%d_rain.bin` 240 B (34).
> - **Fog** `start_dist`/`end_dist` are f32 fractions (area-1 = 0.75 / 0.98; area-0 = 0.5 / 0.9), and
>   the `data_load_flag = 0` synthesis is the 48-iteration, 192-byte-per-slot, 0.75/0.25 sky-LUT blend.
>   **NEW witness:** `data_load_flag = 1` (verbatim-read branch) is now seen in real data — `fog11.bin`
>   ships flag = 1 (start 0.5 / end 0.98) — so the verbatim branch (§2.4) is live, not merely inferred.
> - **Ambient floor = 1.0 at default** RE-CONFIRMED end-to-end: `OPTION_BRIGHT` is read from the INI
>   with a literal default of **100**; the sampled `DoOption.ini` **omits the key** (so the default 100
>   applies); `floor(100/100 × 255) = 255` → device ambient **full white** (§10.4–§10.5, §11.5).
> - **Carried, not re-witnessed this lane (flagged honestly):** the `K_ambient` = 0.0
>   single-reader/zero-writer global, the `OPTION_WATER` dead-consumer search, and the per-field
>   interior layouts of `material`/`light`/`stardome`/`clouddome` (only their fixed total sizes were
>   re-verified here). These stand from the campaign VFS-DEEP-II recovery (§10).

| Attribute         | Value |
|-------------------|-------|
| `status`          | `sample-verified` for `map_option%d.bin`, `fog%d.bin`, `material%d.bin`, `stardome%d.bin`, `clouddome%d.bin`, `cloud_cycle%d.bin`; `partial` for `weather%d.bin`; `weather%d_rain.bin`: **NO LOADER — dead editor data** (see §8); water renderer: **RESOLVED-NEGATIVE** (see §1.4); lighting apply-path numeric defaults: **CONFIRMED** (see §10.4–§10.5, §10.7) |
| `sample_verified` | `true` — formats confirmed against real sample files extracted from the live VFS for areas 0 and 1, with additional spot-checks across areas 4–35 for `map_option`. `weather` samples are zero-dominated; full decode awaits an area with active precipitation. |
| `binary_analysed` | `doida.exe` (legacy 32-bit client build, x86 LE) — used to identify the sky-init call chain, file-read sequences, GRSFog/GRSLighting struct offsets, the lighting/ambient apply-path, and exhaustive cross-reference scan confirming the water-renderer negative result. No addresses appear in this spec. |
| `confidence`      | `CONFIRMED` = field corroborated by parser read-sequence and real sample bytes. `SAMPLE-VERIFIED` = confirmed by observed sample data alone. `CODE-CONFIRMED` = identified in parser only, no sample cross-check. `LOADER-RESOLVED` = the loader read-sequence settled an interpretation the bytes left opaque. `PROPOSED` = structurally inferred, no direct evidence. |

---

## Overview

Each map area is identified by an integer area ID. On area activation the client loads a family
of binary files from `data/sky/dat/` using the area ID as the substitution for `%d` in each
filename. Together these files define:

- which sky subsystems are enabled for the area,
- which sky subsystems are gated per area (dungeon flag, sight clamp, lens flare, star/cloud dome, sun, moon, skybox, indoor) via `map_option%d.bin` — see §1 (note: this file holds NO water field; the old water_enable/water_y reading was a misread, reconciled Campaign 5),
- the fog start/end distances and per-keyframe fog colours,
- the per-keyframe sun/sky/cloud material colours,
- per-keyframe star and cloud-dome tints, and
- the cloud animation schedule and weather effects.

All files in this family are **little-endian**, have **no magic number**, and have **no version
field** unless explicitly noted below.

> ### Sibling tolerance — the environment hub default-tolerates absent siblings (LOADER-RESOLVED)
>
> On area activation a single environment hub drives the loads of the sibling family files
> (`map_option%d.bin`, `light%d.bin`, the stardome grid, `fog016`/the fog file, and the other
> per-area `.bin` siblings). The hub **default-tolerates a missing sibling**: when any one of these
> files is absent the hub does not abort the area load — it leaves that subsystem at its built-in
> default and proceeds. This is the confirmed behaviour for at least the `map_option`, `light`,
> stardome, and fog siblings. **A faithful C# port must default-tolerate every absent sibling**
> (skip-and-default, never throw) rather than requiring the full set to be present.
>
> **Tolerance is PER-LOADER, not a hub-level ignore (CORRECTED, LOADER-RESOLVED).** The
> skip-and-default behaviour is implemented **inside each sibling loader** — each loader synthesises
> or skips its own subsystem when its file is absent, and then returns success to the hub. The hub
> itself is **not** lenient about a sub-init *failure*: the sky-system init step **hard-fails the
> area load if any sub-init returns a failure (returns 0)**. The distinction matters for a port: do
> not model a single hub-level "ignore all missing files" flag — model per-loader synth/skip that
> still reports success, and treat a genuine sub-init failure as fatal to the area load (the safe
> port baseline mirrors the original: absent-file → per-loader default + success; real failure →
> abort).
>
> **DBG-pending:** exactly one indoor area takes a distinct sky-init bypass path when its fog
> sibling is absent (a single-area special case in the sky-init routine). The behaviour of that
> bypass — what it substitutes, and whether it suppresses the fog subsystem entirely — is **not
> resolved statically and is documented honestly as DBG-pending**. Do not invent its behaviour;
> the general default-tolerant rule above is the safe port baseline until the bypass is confirmed
> live.

A global quality-tier file (`DoOption.ini`, section `[DO_OPTION]`) gates rendering subsystems
by tier: `OPTION_WATER`, `OPTION_SKY`, `OPTION_WEATHER`, `OPTION_WEATHER_RAIN`, `OPTION_LENS`,
`OPTION_SUN`. The per-area `map_option%d.bin` flags operate on top of this global gate. Note:
`OPTION_WATER` is stored and written to disk but has **no confirmed runtime consumer in the
shipping binary** — see §1.4. A further `DoOption.ini` value, `OPTION_BRIGHT` (a 1–100 player
brightness slider, default 100 — see §10.5), is the source of the global additive ambient offset
described in §10.4–§10.5.

**Day/night cycle:** 48 keyframes, each 1800 ms, total period 86 400 ms (one simulated day). The
active keyframe index and fractional position are derived as:

```
keyframe_index = (current_time_ms / 1800) mod 48
frac           = (current_time_ms mod 1800) / 1800.0
```

Values between adjacent keyframes are linearly interpolated at runtime. Star-dome and cloud-dome
colour tables use a coarser 12-keyframe cycle (one keyframe = 7200 ms = four lighting keyframes).

**Path pattern for all files in this family:** `data/sky/dat/<name><area_id>.bin`
(and `.txt` companions where noted).

---

## Section 1: `map_option%d.bin` — Per-Area Master Flags

**Status:** CONFIRMED (field semantics RECONCILED Campaign 5 against the `.txt` companions)
**File size:** exactly **40 bytes** (10 × u32 little-endian, fixed, no header)
**Sample coverage:** 64 `.bin` files (one per area), each with a sibling `.txt`; cross-referenced
field-by-field for area 11; flag patterns spot-checked across many areas

> ### ⚠️ Conflict reconciled — these are NOT water fields
>
> A prior revision of this table named offset 0x00 `water_enable` and 0x04 `water_y`, an
> interpretation **derived from IDA function names** (e.g. water/sight helpers). A
> `.txt` ↔ `.bin` cross-reference over the 64 area pairs **disproves** that reading. Every
> `map_option%d.bin` ships with a sibling `map_option%d.txt` listing the author's field names and
> values in `FIELD_NAME <tab> value` form; reading area 11's `.txt` against its `.bin` matches all
> ten u32 words **in order** to the names below. The bytes `[1, 300, …]` at 0x00/0x04 mean
> "**this area IS a dungeon** (`MOVE_DUNGEON = 1`), **camera sight is clamped to 300**
> (`SIGHT_FIX = 300`)" — **not** "water enabled, water surface at Y = 300." Both witnesses agree on
> the numeric values at each offset; the semantics are where the old IDA-name reading was wrong.
>
> **There are NO water fields anywhere in `map_option%d.bin`.** The old `water_enable` / `water_y`
> interpretation is a misread and is removed from this table. (Water placement is unrelated to this
> file; on the renderer side the shipping client has no water render path at all — that separate
> negative result is unchanged and is recorded in §1.4.)
>
> **⚠️ C# wiring action required:** `MapOptionBinParser.cs` and every consumer that reads
> `water_enable` / `water_y` from this 40-byte blob must be corrected to the layout below (especially:
> 0x00 is the dungeon flag, 0x04 is a sight-clamp distance, and there is no water Y here). The C#
> wiring wave will handle this correction; this spec is the authority it cites.

### 1.1 Field table (CONFIRMED — `.txt` ↔ `.bin` cross-referenced)

The author field names come from the `.txt` companion (`FIELD_NAME <tab> value`). All ten u32 words
are little-endian.

| Offset | Size | Type | `.txt` field | Proposed code name | Notes | Confidence |
|-------:|-----:|------|--------------|--------------------|-------|------------|
| 0x00 | 4 | u32 | `MOVE_DUNGEON` | `is_dungeon` | 0 = outdoor / field; 1 = dungeon (dungeon-type movement zone). | CONFIRMED |
| 0x04 | 4 | u32 | `SIGHT_FIX` | `sight_distance` | Camera sight-clamp distance. 0 = free range; otherwise a fixed clamp (observed e.g. 300, 800). NOT a water surface Y. | CONFIRMED |
| 0x08 | 4 | u32 | `LENSFLARE` | `lensflare_enable` | 0 = off; 1 = sun lens-flare screen-space sprites. | CONFIRMED |
| 0x0C | 4 | u32 | `STARDOME` | `stardome_enable` | 0 = off; 1 = star-dome point sprites rendered. | CONFIRMED |
| 0x10 | 4 | u32 | `CLOUDDOME` | `clouddome_enable` | 0 = off; 1 = cloud-dome hemisphere rendered. | CONFIRMED |
| 0x14 | 4 | u32 | `SUN` | `sun_enable` | 0 = off; 1 = sun billboard. Stored separately from MOON below. | CONFIRMED |
| 0x18 | 4 | u32 | `MOON` | `moon_enable` | 0 = off; 1 = moon billboard. **Stored as its own u32 word**, distinct from SUN at 0x14, even though both carry the same value in every observed area. Whether the client reads them independently is UNVERIFIED; the storage is two separate words. | CONFIRMED (as stored) |
| 0x1C | 4 | u32 | `SKYBOX` | `skybox_enable` | 0 = off; 1 = load `sky%d.box`. **Always 0 in every observed area (CONFIRMED-ABSENT).** A VFS census found 0 `.box` files in all 43,347 entries — the skybox feature was parsed but never shipped. See `formats/sky.md §A`. | CONFIRMED (value always 0) |
| 0x20 | 4 | u32 | `MAPHIDE` | `indoor_flag` | 0 = outdoor; 1 = indoor / dungeon lighting (suppresses most sky subsystems). | CONFIRMED |
| 0x24 | 4 | u32 | _(unnamed in `.txt`)_ | `_reserved` | Always 0 in all sampled areas. | SAMPLE-VERIFIED (value always 0) |

### 1.2 Observed flag patterns

The ten u32 values, in offset order, are
`[is_dungeon, sight_distance, lensflare, stardome, clouddome, sun, moon, skybox, indoor, reserved]`:

| Pattern (offset order) | Representative areas | Meaning |
|------------------------|----------------------|---------|
| `[0,0,1,1,1,1,1,0,0,0]` | 0*, 1, 2, 3, 6, 8, 12, 19, 21, 32, 35 | Standard outdoor area: lensflare + stardome + clouddome + sun + moon on. |
| `[0,0,0,1,1,0,1,0,0,0]` | 4, 7 | Outdoor: stardome + clouddome + moon on; no lensflare, no sun. |
| `[0,0,0,0,0,0,0,0,1,0]` | 5, 17, 20, 24, 27, 33 | Indoor / dungeon: all sky gates off, `indoor_flag` set. |
| `[1,300,0,0,0,0,0,0,1,0]` | 11, 15, 16, 22, 23, 25, 26, 31, 34 | Dungeon, sight clamped to 300, indoor lighting, sky off. |
| `[1,800,0,0,0,0,0,0,1,0]` | (dungeon areas with a wider clamp) | Dungeon, sight clamped to 800, indoor lighting, sky off. |

\* **Area-0 anomaly:** `map_option0.bin` does not match its own `.txt` field ordering (its raw
words do not line up with the `.txt` values). Area 0 appears to be a special-case default / template
entry written with a different ordering and must not be used to validate the field table. This does
not affect the CONFIRMED layout for all other areas.

> The previously tabulated `[1,300,…]` / `[1,700,…]` / `[1,1000,…]` "water at Y=N" patterns were a
> consequence of the old water misread. Under the reconciled layout these are dungeon areas with a
> `SIGHT_FIX` (sight-clamp) value at 0x04, not a water height. The exact value distribution per area
> is in the dirty-room census, not reproduced here.

### 1.3 Relationship to the water renderer (unchanged negative result)

`map_option%d.bin` carries **no water fields**. Water surface placement is not stored in this file
(nor, per prior analysis, anywhere the shipping renderer consumes). The separate, independently
established result — that the shipping client has **no water render path** — is unaffected by this
reconciliation and is retained in §1.4 as historical context. Engineers should not look to
`map_option%d.bin` for any water parameter.

### 1.4 Water renderer — RESOLVED-NEGATIVE (water is a non-feature)

> **Status: RESOLVED-NEGATIVE — water is a non-feature in the shipping client.** There is **no
> dedicated water renderer, no water plane (no water-Y and no extent field), and no
> reflection/refraction render target**. This conclusion is independent of the §1.1 field
> reconciliation above and is now strengthened by a Campaign-5B render-side census: no water-Y is
> stored in `map_option%d.bin` (§1.1) **and** nothing in the renderer creates a water surface or a
> water reflection target.

Key evidence (from cross-reference analysis):

- **No water primitive, no water strings.** Searches across all string literals for "water",
  "reflect", "ripple", "wave", "refract", and "mirror" return no water texture path, no water
  vertex-buffer name, and no water draw-function name in the binary. The only "water" token anywhere
  is the INI option key `OPTION_WATER` (see below) — there is no water class string and no
  `water*.dds` texture in the VFS.
- **The "water/reflective FX" bucket is just the transparent terrain-overlay FX layer.** What
  `rendering.md §4.2` calls the "water/reflective FX" transparent bucket is the **generic transparent
  terrain-overlay FX layer** — the per-cell `fx1`..`fx7` overlay meshes, drawn SRCALPHA/INVSRCALPHA.
  Those overlay layers are selected per cell by a **texture index** taken from the cell `.map`
  TEXTURES section (the same mechanism as base/mass terrain texture indices), **not** by a water
  FX-name and **not** as a dedicated water primitive. Where a "water-looking" surface appears, it is
  such an FX-textured overlay sitting at the **terrain mesh Y of that overlay layer** — there is **no
  separate water-Y or extent field anywhere**, consistent with this section finding no water-Y in
  `map_option%d.bin` or `.map`.
- **No reflection/refraction render target — exactly four offscreen targets, none for water.** The
  only render-to-surface creator is called from exactly two systems: the **shadow map** (1 target)
  and the **cel/glow shading chain** (3 targets) = **4 render targets total**, with **no water
  reflection or refraction target**. The display/glow config carries the full glow / char-bright /
  framerate / light-ratio key set and **zero** water or reflection keys.
- **`OPTION_WATER` is a dead/vestigial INI slot.** `OPTION_WATER` is a real graphics-quality INI
  option (loaded into the option singleton, saved back, default 1, clamped 1..3), but it has **zero
  read sites** — nothing in the renderer consumes it. By contrast the sibling quality slots GROUND,
  SKY, WEATHER, and SHADOW are each consumed by their subsystems; only WATER is orphaned. The option
  made it *look* like water rendering exists, but the slot drives nothing.

**Implication for Assets.Parsers and a faithful port:** there are no water fields to parse out of this
file, and no Assets.Parsers code need implement water geometry, a water plane, a reflection probe, or
a refraction pass. A faithful 1:1 port needs **no water plane, no reflection probe, and no
refraction** — at most a translucent terrain-overlay FX layer when a cell `.map` assigns a
translucent FX texture index. Any standalone water surface a reimplementation chooses to render in
`05.Presentation` is a free engineering decision, not a faithful reproduction of an original asset.

### 1.5 Remaining known unknowns

- **`SUN` (0x14) vs. `MOON` (0x18) independence:** stored as two separate u32 words; in every
  observed area they hold the same value. Whether the runtime ever reads them independently (i.e.
  whether a sun-on / moon-off area is possible) is UNVERIFIED — only the two-word storage is confirmed.
- **`indoor_flag = 1` suppression set:** indoor areas suppress at minimum cloud dome, star dome,
  sun/moon, and lens flare. Whether fog parameters still apply indoors is unverified.
- **Area-0 ordering anomaly:** the cause of area 0's mismatch between `.bin` words and `.txt` values
  is unresolved (template/default candidate).
- **Indoor-area sky-init bypass (DBG-pending):** one indoor area takes a distinct sky-init bypass
  when its fog sibling is absent (see Overview "Sibling tolerance"). Its substituted behaviour is
  DBG-pending; the default-tolerant rule is the safe baseline until confirmed live.

---

## Section 2: `fog%d.bin` — Per-Area Fog Parameters

**Status:** CONFIRMED  
**File size:** exactly **204 bytes** (fixed, no header magic)

### 2.1 Field table

| Offset | Size | Type | Field | Notes | Confidence |
|-------:|-----:|------|-------|-------|------------|
| 0x00 | 4 | f32 | `start_dist` | Fog start distance. A **float fraction** in 0.0–1.0, interpreted as a fraction of the configured view range. Observed area-1 value: **0.75** (sample-verified; see §10.2 — supersedes the 0.5 stand-in once quoted for area 0/1). | CONFIRMED |
| 0x04 | 4 | f32 | `end_dist` | Fog end distance, same scale as `start_dist`. Observed area-1 value: **0.98** (sample-verified; see §10.2). | CONFIRMED |
| 0x08 | 4 | u32 | `data_load_flag` | Selects the **source** of `fog_colors[]`, NOT whether fog renders. **0 = synthesise** the colour table at load time from the sky/material colour table (the 3:1 blend in §2.4) — the in-file `fog_colors[]` bytes are NOT read. **1 (non-zero) = read** the explicit `fog_colors[]` array straight from the file and use it verbatim — including, if those bytes are all zero, a **literal black** fog colour (§2.4). Areas 0 and 1 both have value 0. | CONFIRMED |
| 0x0C | 192 | u8[192] | `fog_colors[48]` | 48 BGRA colour entries, one per day/night keyframe. Each entry is 4 bytes. Bytes are used as a packed D3DCOLOR directly — see §2.2 and §10.1 (NO /255 in the client). | CONFIRMED |

Total: 4 + 4 + 4 + 192 = 204 bytes.

### 2.2 BGRA colour encoding

Each 4-byte fog colour entry uses BGRA byte order (matching the legacy D3D `D3DCOLOR` convention):

| Byte within entry | Semantic | Notes |
|:-----------------:|----------|-------|
| [0] | Blue | u8, 0–255 |
| [1] | Green | u8, 0–255 |
| [2] | Red | u8, 0–255 |
| [3] | Alpha | u8, always 0 in all sampled data (forced opaque on apply — §10.1) |

Keyframe 0 corresponds to midnight; keyframe 24 to noon. Colour transitions follow the day/night
cycle defined in §0. These bytes are the D3DCOLOR channels **used directly** (no normalisation) when
the client pushes the fog colour to the device — see §10.1.

### 2.3 Fog colour sample (area 0 — illustrative, not an asset reproduction)

Representative colour character at four keyframes of area 0 (all samples are dark/muted outdoor
atmospheres; exact numeric values are in the dirty-room samples, not in this spec):

| Keyframe | Approximate character |
|----------|-----------------------|
| kf 0  (midnight) | Very dark cool grey |
| kf 16 (pre-dawn) | Warm blue-orange transition |
| kf 24 (noon) | Muted sky-blue with orange tint |
| kf 40 (evening) | Dark, near-neutral |

### 2.4 `data_load_flag` semantics — RESOLVED (flag selects source; flag==1 + zero = literal black)

`data_load_flag` selects **where the fog colour table comes from** — it does NOT decide whether fog
is enabled, and it is NOT a "synthesise the colour when it is missing" toggle.

- **`data_load_flag = 0` — synthesise.** The 192-byte `fog_colors[]` table is **not** read from
  `fog%d.bin`; instead it is **synthesised at load time from the sky/material master colour table**
  with a per-slot **3:1 weighted blend**: for each of the 48 keyframe slots and each colour channel,

  ```
  fog_byte[slot][chan] = high_band[slot][chan] × 0.75 + low_band[slot][chan] × 0.25
  ```

  (two source bands from the sky colour LUT, blended, then truncated to a byte via float→int).
  **Synthesis happens ONLY on this `flag == 0` branch.**

- **`data_load_flag = 1` (non-zero) — read verbatim, no synthesis.** The explicit 192-byte
  `fog_colors[]` array is read straight from the file and used as-is. **There is no synthesis on this
  branch.** In particular, when the flag is `1` **and** the in-file colour bytes are all zero, the
  result is **literal black** fog — the client renders the black it was given; it does **not** fall
  back to the sky-LUT blend. (REFUTES any earlier "synthesise when flag == 1" / "synthesise on a
  zero colour" reading — the synthesis path is exclusively the `flag == 0` branch.)

CONFIRMED, pinned to the fog loader read-sequence (two witnesses: loader source-of-table branch +
black-box behaviour on a zero-colour flag-1 file). The runtime apply of these bytes (whether read or
synthesised) is the byte-D3DCOLOR path in §10.1. See also `specs/environment.md §1.1`.

> **Real-data witness of the `data_load_flag = 1` branch (sample-verified, build `263bd994`).** Areas
> 0 and 1 both ship `data_load_flag = 0`, so the earlier revision could only describe the flag = 1
> verbatim-read branch behaviourally. A corpus scan now finds **`fog11.bin` ships
> `data_load_flag = 1`** (with `start = 0.5`, `end = 0.98`), so the verbatim-read branch is **live in
> production data**, not merely inferred. The synthesis branch (flag = 0) and the verbatim branch
> (flag = 1) are both exercised by shipping areas.

> The `0.75` / `0.25` blend weights are CONFIRMED as the blend ratio. The exact mapping of which two
> sky-LUT source bands play the `high_band` / `low_band` roles per channel is MED confidence — see
> §2.5 and `specs/environment.md §8`.

### 2.5 Known unknowns

- **`data_load_flag = 0` blend source bands:** the 0.75/0.25 blend ratio is CONFIRMED (§2.4); the
  exact pair of sky-LUT source bands feeding `high_band` / `low_band` per channel is MED.
- **Fog mode (EXP / EXP2 / LINEAR):** the D3D9 fog type (1 = EXP, 2 = EXP2, 3 = LINEAR) is stored in
  the runtime fog struct (with a colour, a start, an end, and a density field) but is **not** loaded
  from this file. The **observed apply path is LINEAR** (range derived from a per-keyframe scalar —
  see §10.3 and `specs/environment.md §6`). Whether any area/quality tier ever switches the struct to
  an exponential-density mode is unverified.

---

## Section 3: `material%d.bin` — Sun / Sky Material Colour Table

**Status:** CONFIRMED  
**File size:** exactly **9792 bytes** (48 × 51 × 4 bytes, fixed)

### 3.1 File layout

The file contains a single flat array with no header:

| Offset | Size | Type | Field | Notes | Confidence |
|-------:|-----:|------|-------|-------|------------|
| 0x0000 | 9792 | f32[48][51] | `color_table` | 48 keyframes × 51 float32 values per keyframe, row-major. Row `k` starts at byte `k × 204` | CONFIRMED |

### 3.2 Per-keyframe colour group layout

Each row of 51 float32 values (204 bytes) encodes colour and intensity for multiple sky objects.
The confirmed index assignments within one keyframe row are:

| Index range | Name | Notes | Confidence |
|:-----------:|------|-------|------------|
| [0..3]  | `sky_haze` (RGBA) | Atmospheric haze tint + pad | CODE-CONFIRMED |
| [4..7]  | `sun_color` (RGBA) | Sun disk colour; values may exceed 1.0 for HDR bloom effect | CODE-CONFIRMED |
| [12..15] | `secondary_sky_color` (RGBA) | Secondary sky band colour | CODE-CONFIRMED |
| [17..20] | `cloud_color_A` (RGBA) | Primary cloud layer colour | CODE-CONFIRMED |
| [21..24] | `cloud_color_B` (RGBA) | Secondary cloud layer colour | CODE-CONFIRMED |
| [29..32] | `ambient_sky_color` (RGBA) | Sky ambient contribution (RGBA) | CODE-CONFIRMED |
| [34..36] | `emissive_sky` (RGB, no alpha) | Sky self-illumination | CODE-CONFIRMED |
| [38..40] | `specular_sky` (RGB, no alpha) | Sky specular highlight | CODE-CONFIRMED |

Indices not listed above (e.g. [8..11], [25..28], [33], [37], [41..50]) are loaded into memory
but their runtime usage was not traced. They are **PROPOSED** as reserved or as additional sky
object colours (e.g. moon, distant haze layers).

Float components are in RGBA order (R at lowest index). Alpha components are generally 0.0 or
1.0. Sun colour values above 1.0 are intentional HDR bloom values.

### 3.3 Known unknowns

- **Indices [8..11], [25..28], [33], [37], [41..50]:** Loaded into memory, usage not traced.
- **Area 0 static table:** All 48 keyframes of area 0's material file appear identical (static
  colour, no day/night variation). Whether this is a test/unused area artefact or a deliberate
  constant-sky design is unverified.

### 3.4 Synth default when `material%d.bin` is absent (CYCLE 7, CONFIRMED)

The material loader **never hard-fails** on a missing file — it synthesises a default colour table in
a per-entry loop. The CONFIRMED default immediates are:

| Material channel | Synth default | Confidence |
|------------------|:-------------:|------------|
| ambient   | **0.30000001** (the float32 nearest 0.3) | CONFIRMED (immediate) |
| diffuse   | **0.8** | CONFIRMED (immediate) |
| specular  | **0.0** | CONFIRMED (immediate) |
| emissive  | **0.0** | CONFIRMED (immediate) |

A faithful parser should mirror this: when `material%d.bin` is absent, populate the colour table with
`(ambient 0.3, diffuse 0.8, specular 0.0, emissive 0.0)` rather than erroring. The runtime consumes
these floats directly (float [0,1] domain — §10.1).

---

## Section 4: `stardome%d.bin` — Star Colour Grid

**Status:** CONFIRMED  
**File size:** exactly **9216 bytes** (12 × 192 × 4 bytes, fixed)  
**Gated by:** `map_option[stardome_enable]`

### 4.1 File layout

| Offset | Size | Type | Field | Notes | Confidence |
|-------:|-----:|------|-------|-------|------------|
| 0x0000 | 9216 | u8[12][192][4] | `star_colors` | 12 keyframes × 192 star instances × 4 bytes BGRA each | CONFIRMED |

The star-dome cycle uses **12 keyframes** at 7200 ms each (4 × 1800 ms lighting keyframes per
star-dome step), covering one simulated day in 86 400 ms.

### 4.2 BGRA colour encoding

Same byte order as fog colours (§2.2): Blue, Green, Red, Alpha. Alpha is always 0.

### 4.3 Usage notes — tint is PER-STAR (CONFIRMED)

**The star tint is per-star, NOT a single per-keyframe tint.** The consumer walks the 192 star
instances of the active keyframe and **advances 4 bytes (one BGRA entry) per star**, reading a
distinct colour for each star instance. Each of the 192 entries in a keyframe is therefore the tint
of an individual star, and per-instance colour variation is fully supported by the format and by the
consumer. **CONFIRMED** (two witnesses: the consumer's 4-byte-per-star advance + the per-instance
array layout). This **refutes** the earlier "uniform tint across all stars per keyframe" reading
(§4.3 previously inferred a single per-frame colour from samples in which the 192 entries happened to
share a value — that uniformity is a property of those particular samples, not a constraint of the
format or the consumer).

Stars are point-sprite particles textured from `data/sky/texture/star.dds`.

Day keyframes contain bright orange/amber values — these pre-colour the stars even during
daylight when they are invisible (overridden by sky brightness), so that stars immediately show
the correct colour at dusk without a cross-fade lag.

---

## Section 5: `clouddome%d.bin` — Cloud Dome Colour Grid

**Status:** CONFIRMED  
**File size:** exactly **23040 bytes** (2 × 11520 bytes, fixed)  
**Gated by:** `map_option[clouddome_enable]`

### 5.1 File layout

The file contains two equal-sized sections, one per cloud layer:

| Offset | Size | Type | Field | Notes | Confidence |
|-------:|-----:|------|-------|-------|------------|
| 0x0000 | 11520 | u8[12][240][4] | `cloud_layer1_colors` | Inner cloud layer: 12 keyframes × 240 BGRA entries | CONFIRMED |
| 0x2D00 | 11520 | u8[12][240][4] | `cloud_layer2_colors` | Outer/haze layer: 12 keyframes × 240 BGRA entries | CONFIRMED |

Section 2 starts at byte offset 0x2D00 = 11520.

### 5.2 BGRA colour encoding

Same byte order as §2.2 (Blue, Green, Red, Alpha). Alpha is always 0 in all sampled data.

### 5.3 Keyframe cycle

Same 12-keyframe, 7200 ms-per-step cycle as `stardome%d.bin` (§4).

### 5.4 Geometry

The cloud dome is a **hemispheric mesh** (not a particle system). The 240 BGRA entries per
keyframe represent per-vertex tints applied to the dome mesh vertices. The total vertex count
of the cloud-dome mesh is 240 (confirmed by the array size).

### 5.5 Known unknowns

- **240-vertex mesh layout:** The sphere tessellation scheme (UV-sphere, icosphere, or other)
  is not documented in these binary files; it is implicit in the client's cloud-dome geometry
  generator.
- **Layer separation (inner vs. outer):** The assignment of layer 1 to the inner and layer 2 to
  the outer dome is inferred from rendering order; not directly verified from binary analysis.

---

## Section 6: `cloud_cycle%d.bin` — Cloud Animation Schedule

**Status:** CONFIRMED  
**File size:** exactly **70 bytes** (10 × 7 u8 values, fixed)  
**Companion file:** `cloud_cycle%d.txt` — human-readable TSV with the same content; both files
are present in the VFS

### 6.1 File layout

The file is a flat 10 × 7 array of unsigned bytes. No header, no magic:

| Row × Col | Absolute offset | Type | Field | Notes | Confidence |
|:---------:|:--------------:|------|-------|-------|------------|
| row[r] col[0] | r × 7 + 0 | u8 | `speed` | Cloud drift speed integer, observed range 1–10 | CONFIRMED |
| row[r] col[1] | r × 7 + 1 | u8 | `cloud1_id_0to12h` | Cloud texture ID for layer 1 during simulated hours 0–12 | CONFIRMED |
| row[r] col[2] | r × 7 + 2 | u8 | `cloud1_id_12to24h` | Cloud texture ID for layer 1 during hours 12–24 | CONFIRMED |
| row[r] col[3] | r × 7 + 3 | u8 | `cloud2_id_0to6h` | Cloud texture ID for layer 2 during hours 0–6 | CONFIRMED |
| row[r] col[4] | r × 7 + 4 | u8 | `cloud2_id_6to12h` | Cloud texture ID for layer 2 during hours 6–12 | CONFIRMED |
| row[r] col[5] | r × 7 + 5 | u8 | `cloud2_id_12to18h` | Cloud texture ID for layer 2 during hours 12–18 | CONFIRMED |
| row[r] col[6] | r × 7 + 6 | u8 | `cloud2_id_18to24h` | Cloud texture ID for layer 2 during hours 18–24 | CONFIRMED |

`r` ranges from 0 to 9. Total: 70 bytes.

### 6.2 Semantics

The 10 rows represent 10 pseudo-random **day patterns** the client cycles through. The client
selects one pattern per simulated day and uses its cloud texture IDs and speed for that day.

Cloud texture IDs index into `data/sky/texture/cloud%d.dds`:
- Layer 1 cloud textures use a 2-frame ping-pong animation.
- Layer 2 cloud textures use a 4-frame animation.

### 6.3 Known unknowns

- **Day-pattern selection algorithm:** How the client advances through or selects among the 10
  rows (sequential, random, weather-driven) is not confirmed.
- **Cloud ID 101:** Appears in some areas' cloud cycle schedules. No `cloud101.dds` was found in
  the VFS sky texture directory. ID 101 may be a sentinel meaning "no cloud" or "use default".
  This is **PROPOSED**, not confirmed.
- **`speed` units:** The integer speed value's mapping to world-space drift velocity (UV offset
  per frame or per second) is unverified.

---

## Section 7: `weather%d.bin` — Weather Parameters

**Status:** PARTIAL — file size confirmed, content not decoded  
**File size:** exactly **240 bytes** (fixed)

### 7.1 File layout

No header magic, no version field. The 240-byte body is expected to encode rain or snow type,
particle intensity, and related parameters. All sampled files (areas 0 and 1) contain only zeros.

| Offset | Size | Type | Field | Notes | Confidence |
|-------:|-----:|------|-------|-------|------------|
| 0x00 | 240 | u8[240] | _body_ | Content entirely unknown — all zeros in areas 0–1 | SAMPLE-VERIFIED (size only) |

### 7.2 Known unknowns

- **Full field layout:** No populated sample has been decoded. At minimum 60 u32 or 60 f32 values
  fit in 240 bytes; the actual type and semantics are unverified.
- **Relationship to `weather%d_rain.bin`:** none at runtime — the `_rain.bin` companion has **no
  loader** and is never read by the shipping client (§8). Whether `weather%d.bin` itself has a live
  runtime consumer is unverified (its samples are all-zero).

---

## Section 8: `weather%d_rain.bin` — Rain Editor Data (NO LOADER — dead data)

**Status:** NO LOADER — rain is generated at runtime from constants + RNG; the on-disk
`weather%d_rain.bin` files are dead editor data. (LOADER-RESOLVED.)  
**File size:** exactly **240 bytes** (fixed)  
**Count in VFS:** 33 `_rain.bin` files

### 8.1 No runtime loader — the files are not consumed

> **The shipping client has NO loader for `weather%d_rain.bin`.** A cross-reference scan finds no
> read-site that opens or parses these files. Instead, rain is **built at runtime from hard-coded
> constants and a random-number generator** (drop counts, spawn positions, fall velocity, and streak
> appearance are produced procedurally — they are not read from disk). The 33 `_rain.bin` files
> present in the VFS are therefore **dead editor data**: authoring-tool output that the runtime never
> reads. **LOADER-RESOLVED** (the absence of a read-site is the witness).
>
> **A faithful 1:1 port must NOT load `weather%d_rain.bin`.** Reproduce rain procedurally
> (constants + RNG) exactly as the original does; do not build a parser for these files and do not
> drive rain from their contents. Treating them as live input would diverge from original behaviour.

### 8.2 File layout (for archival completeness only — not parsed by the client)

The on-disk record is the same 240-byte size as `weather%d.bin`. Because the runtime never reads it,
no field semantics are pinned and none are needed for a faithful port.

| Offset | Size | Type | Field | Notes | Confidence |
|-------:|-----:|------|-------|-------|------------|
| 0x00 | 240 | u8[240] | _body_ | Editor-authored; **never read by the shipping client**. Largely zeros; first few bytes non-zero in some files. Not consumed — do not parse. | LOADER-RESOLVED (no read-site) |

### 8.3 Known unknowns

- **Editor field layout:** unrecovered and intentionally not pursued — the runtime never reads these
  files, so their internal layout has no bearing on a faithful port.

---

## Section 9: Supplementary corrections to `terrain_layers.md §6` (`light%d.bin`)

`terrain_layers.md §6` documented `light%d.bin` from parser evidence only (`sample_verified:
false`). Real samples are now available for areas 0 and 1. The following corrections and
additions are **sample-verified** and should be considered authoritative over the earlier
parser-only entries in `terrain_layers.md §6`:

> ### ⚠️ 9.0 CORRECTION — the `light%d.bin` LOADER is an opaque verbatim slurp (LOADER-RESOLVED)
>
> The file-loading step for `light%d.bin` **does not parse fields at all.** The loader performs a
> single **opaque verbatim slurp** of the whole **5312-byte (0x14C0)** file into one memory block and
> stops — it reads no offsets, decodes no colours, and makes no per-field decisions at load time. The
> region map in §9.1 and every per-field offset in §9.2–§9.4 (the directional/ambient `color_A`,
> `color_B`, `color_C` groups, the fog scalars, the fallback light vector) describe **how a downstream
> CONSUMER interprets the slurped blob**, recovered by sample-byte decode and the per-frame
> apply-path — **not** how the loader reads the file (the loader treats it as one byte block).
>
> **Consequence for confidence grading:** any field below tagged as "read/consumed by the loader" or
> "CONFIRMED by the loader read-sequence" is reframed as a **CONSUMER/render-side** fact. The byte
> layout itself stays **SAMPLE-VERIFIED** (it is decoded from real area-0/area-1 files); but the exact
> per-field roles inside the keyframe groups — specifically the diffuse / specular / `color_C`
> assignments in §9.2 — are **DBG-pending**: they are best confirmed by breakpointing the per-frame
> apply under a real area load (maintainer-driven F9; never `dbg_start`). A faithful parser should
> **slurp the 5312 bytes verbatim** and may surface the regions below as SAMPLE-VERIFIED structure,
> but must not assert the diffuse/specular/`color_C` *meanings* as loader-proven until that live
> witness lands. (Two witnesses settle the slurp: the load step's single bounded read of 0x14C0
> bytes, and the constant 5312-byte size across all 61 sampled files.)

### 9.1 Revised section layout

The 5312-byte file has six identified regions, not three as previously enumerated:

| Byte range | Size | Region | Description | Confidence |
|:----------:|-----:|--------|-------------|------------|
| 0x0000–0x08FF | 2304 | A — Directional light | 48 keyframes × 48 bytes | CONFIRMED |
| 0x0900–0x092F | 48   | Gap A | All zeros in sampled data; likely a 49th wrap-around interpolation slot or alignment pad | SAMPLE-VERIFIED (all zeros) |
| 0x0930–0x122F | 2304 | B — Ambient light | 48 keyframes × 48 bytes | CONFIRMED |
| 0x1230–0x125F | 48   | Gap B | All zeros; same interpretation as Gap A | SAMPLE-VERIFIED (all zeros) |
| 0x1260–0x131F | 192  | C — Fog-distance scalar | 48 keyframes × 1 f32 | CONFIRMED |
| 0x1320–0x13DF | 192  | D — Secondary fog scalar | 48 keyframes × 1 f32; values near 0.0 in all sampled areas (haze intensity) | SAMPLE-VERIFIED |
| 0x13E0–0x14A7 | 200  | E — Reserved f32 array | All zeros in all sampled data | SAMPLE-VERIFIED (all zeros) |
| 0x14A8–0x14AF | 8    | Padding | Four zero bytes | SAMPLE-VERIFIED (all zeros) |
| 0x14B0–0x14BF | 16   | Fallback light | f32[4]: scale, dir_X, dir_Y, dir_Z | CONFIRMED |

Total: 2304 + 48 + 2304 + 48 + 192 + 192 + 200 + 8 + 16 = 5312 bytes.

### 9.2 Keyframe structure (sections A and B) — revised

Each 48-byte keyframe consists of **three float4 colour groups** (not the sun/moon field
assignments listed in `terrain_layers.md §6.2`). The dirty-room analysis that produced those
assignments used a narrower read-site scope; the sample-verified interpretation is:

| Slot offset | Size | Type | Field | Confidence |
|:-----------:|-----:|------|-------|------------|
| +0x00 | 16 | f32[4] | `color_A` (RGBA) | Primary colour — diffuse for section A, ambient for section B. RGBA, alpha always 0. Float in **[0,1]**, applied directly (no /255) — see §10.4. **CONSUMED** (read and applied by the time-update path). | CONFIRMED |
| +0x10 | 16 | f32[4] | `color_B` (RGBA) | Secondary colour — specular for section A. **CONSUMED** (read by the time-update path). | CODE-CONFIRMED |
| +0x20 | 16 | f32[4] | `color_C` (RGBA) | **Present-but-UNREAD.** Only `color_A` (the diffuse colour at +0x00) and `color_B` (the specular colour at +0x10) are read by the consumer; the third group at +0x20 has **no read-site** and is never consumed. All zeros in all sampled data. A faithful parser may surface the bytes but must NOT feed them to the lighting math (the original ignores them). | LOADER-RESOLVED (no read-site; unconsumed) |

The per-field sun/moon color assignments from `terrain_layers.md §6.2` (indices 0–2 as
`sun_colour`, index 5 as `moon_colour[1]`, etc.) remain the best parser-side interpretation and
are not contradicted by the sample bytes. They are retained in `terrain_layers.md` as
CODE-CONFIRMED; the float4-group view above is a complementary, sample-verified structural view.

> **color_C consumption — LOADER-RESOLVED.** Two witnesses settle that only the first two colour
> groups are consumed: the loader/time-update read-sequence touches `color_A` (+0x00) and `color_B`
> (+0x10) and stops — there is no read of the third group — and the sampled bytes for `color_C` are
> uniformly zero. The third group is **present in the layout but unread**; treat it as reserved/dead.

### 9.3 Section C fog scalar — revised interpretation

The f32 values in section C represent a **fog distance scale in world units**, not a normalised
density. Sampled range for area 1: approximately 8 to 43 world units. The value 1.0 noted in
`terrain_layers.md §6.4` as a "no-override sentinel" is not observed in real samples; the
sentinel behaviour may apply only in parser edge cases. Engineers should treat section C as the
per-keyframe **world-unit fog scalar `s`** that drives the runtime LINEAR fog range
(`range = s × 3.0`, `near-scale = 1.0 / s`, fog enabled when `s > 0`) — see §10.3 and
`specs/environment.md §6`. This per-frame derivation overwrites the static `fog%d.bin`
`start_dist` / `end_dist` baseline each tick.

### 9.4 Fallback directional light (bytes 0x14B0–0x14BF)

| Index | Field | Value | Confidence |
|:-----:|-------|-------|------------|
| [0] | `scale` | 1.0 | CONFIRMED |
| [1] | `dir_X` | −7.0 | CONFIRMED |
| [2] | `dir_Y` | 7.0 | CONFIRMED |
| [3] | `dir_Z` | 20.0 | CONFIRMED |

This is the unnormalised world-space direction vector used when no runtime light is active. The
Godot-side normalised vector is `normalize((-7, 7, 20))` ≈ `(-0.322, 0.322, 0.920)`. CONFIRMED that
the day/night cycle does **not** rotate the sun — the keyframe tables (§A/§B) encode colour only;
this static fallback is the only light direction the client uses (see §10.5 and
`specs/environment.md §6`).

### 9.5 Synth colour ramp + scale when `light%d.bin` is absent (CYCLE 7, CONFIRMED)

The light loader **never hard-fails** on a missing file — it synthesises a default 48-keyframe
day/night colour ramp and a fallback light vector, then returns success. The CONFIRMED synth
immediates are:

| Synth quantity | Value | Notes |
|----------------|-------|-------|
| Fallback light direction | `(−7.0, 7.0, 20.0)` | same vector as §9.4 (this synth path writes it into the runtime light object). |
| Synth ramp **scale** | **80.0** | the light intensity scale used by the synth path (distinct from the `1.0` scale carried in the §9.4 fallback-vector slot). |
| Synth colour ramp | per keyframe: `intensity = kf_idx × 0.04` (= 1/25) **clamped**; `colour = 1.0 − intensity` | 48 keyframes; the ramp darkens monotonically across the day index. |

Confidence: CONFIRMED (immediates `0.04`, `1.0 −`, `80.0`). A faithful parser should synthesise this
ramp on a missing `light%d.bin` rather than failing the area load. The ambient gate `K_ambient = 0`
still applies (the synth ambient is inert at runtime — §10.4); the live ambient floor remains the
`OPTION_BRIGHT` offset (§10.5).

> **Light specular/ambient default mask (MED).** A constant ARGB mask `0xFF7FFFFF` is also written by
> the synth path into the light object's specular/ambient default slots. Its precise per-channel
> effect is MED confidence (the value is recovered; its exact consumption is not fully traced).

---

## Section 10: Colour domains and lighting/fog apply-path field facts

> This section pins **which fields are byte-D3DCOLOR vs. float [0,1]**, and which file fields feed
> the runtime lighting/fog math. It is the byte/field half of the lighting recovery; the runtime
> math, the asymmetric ambient gate, the brightness slider, and the Godot "too-dark" fix are in
> `Docs/RE/specs/environment.md §6`. Apply-paths below are CONFIRMED. As of the campaign VFS-DEEP-II
> apply-path recovery, the two ambient numeric defaults previously flagged UNVERIFIED are now
> **CONFIRMED statically** (the ambient gate `K_ambient` = 0.0 with no writer; `OPTION_BRIGHT`
> default = 100); only a possible user-edited on-disk INI value remains as a thin runtime residual
> (§10.7).

### 10.1 Colour domains — TWO domains, NOT uniformly /255

The environment colour fields fall into two distinct numeric domains. They are **not** uniformly
divided by 255 in the original client:

| Field family | Source | On-disk type | Domain | Apply in original | Port note |
|--------------|--------|--------------|--------|-------------------|-----------|
| Fog colour | `fog%d.bin` `fog_colors[48]` (§2) | u8 BGRA | bytes 0–255 | packed as a D3DCOLOR (ARGB, alpha forced opaque) and pushed to the device **directly** — NO /255 | a float renderer must `/255` to land in [0,1]; this is a porting step, not original math |
| Sky / fog / star / cloud colour tables | `material%d.bin`, `stardome%d.bin`, `clouddome%d.bin`, derived fog (§2.4) | u8 BGRA | bytes 0–255 | same packed D3DCOLOR byte path, alpha forced opaque | same `/255` port step |
| Directional / ambient light colour | `light%d.bin` §A / §B `color_A` | f32 RGBA | float [0,1] | written to render globals and fed to the device's light/material structures as floats **directly** — NO /255 | pass straight through; do NOT multiply or divide by 255 |

CONFIRMED. The byte colour tables and the float light colours must be treated differently by a
parser/mapping layer: byte tables convert with `/255`, float light colours pass through unchanged.

### 10.2 Fog distance fractions — sample-verified numeric refresh

The `fog%d.bin` `start_dist` / `end_dist` are **float fractions** of the view range (§2.1). The
live area-1 file decodes to:

| Field | Area-1 value | Confidence |
|-------|:------------:|------------|
| `start_dist` (0x00) | **0.75** | SAMPLE-VERIFIED |
| `end_dist` (0x04) | **0.98** | SAMPLE-VERIFIED |

> **UNVERIFIED stand-in conflict (numeric, not structural):** earlier revisions of
> `specs/environment.md §6.4` quoted area-1 fog as `start = 0.5`, `end = 0.9` as temporary
> stand-ins. The live `fog1.bin` decodes to **0.75 / 0.98**. The byte layout is identical; only the
> stand-in numbers were drift. Engineers should read the parsed file at runtime rather than
> hard-code either pair. The corrected values are propagated into `specs/environment.md §6.4`.

### 10.3 Fog mode + distances — the applied path is LINEAR from the section-C scalar

The runtime fog struct carries a `type` (1 = EXP, 2 = EXP2, 3 = LINEAR), a fog colour, a `start`,
an `end`, and a `density`. The **observed applied path is LINEAR**, driven by the per-keyframe fog
scalar `s` from `light%d.bin` section C (§9.3): when `s > 0` the runtime sets a fog **range**
`= s × 3.0`, a reciprocal/near-scale `= 1.0 / s`, and **enables** fog on the device. The static
`fog%d.bin` `start_dist` / `end_dist` seed a baseline but are overwritten by the per-frame
`s × 3.0` derivation each tick. CODE-CONFIRMED. The runtime math and its Godot mapping are in
`specs/environment.md §6`.

**In-memory engine fog struct layout (CYCLE 7, CONFIRMED).** The runtime fog object's field offsets,
recovered from the struct's debug-describe accessor, are:

| Struct offset | Size | Type | Field | Notes |
|--------------:|-----:|------|-------|-------|
| +44 | 4 | u32/enum | `type` | Fog mode: **1 = EXP, 2 = EXP2, 3 = LINEAR**. The observed apply path writes LINEAR (§10.3 above). |
| +48 | 4 | ARGB | `color` | 4-byte packed D3DCOLOR (alpha forced opaque on apply — §10.1). |
| +52 | 4 | f32 | `start` | Fog start distance (world units, written by the apply path). |
| +56 | 4 | f32 | `end` | Fog end distance (= `s × 3.0` from the section-C scalar on the live path). |
| +60 | 4 | f32 | `density` | Exponential-mode density; written **0.0** on the LINEAR apply path. |

These are **runtime struct** offsets, distinct from the `fog%d.bin` on-disk offsets in §2.1. (The
on-disk `start_dist` / `end_dist` may equivalently be described as struct-relative `+208` / `+212`
within the loading object, followed by the `data_load` flag and the 192-byte colour block — the same
204 bytes as §2.1.)

### 10.4 Light colour domains feeding lighting math (float [0,1]) — ambient gate CONFIRMED 0.0

`light%d.bin` §A (directional) and §B (ambient) `color_A` groups are **float32 in [0,1]** (§9.2) and
feed the lighting math:

- **Directional** colour is applied **raw** (no multiplier).
- **Ambient** colour is **gated by a global float multiplier** (the asymmetric ambient gate,
  canonical name `K_ambient`) before use. This multiplier is a runtime global, NOT an on-disk field —
  its apply rule is in `specs/environment.md §6`.

> **CONFIRMED (apply-path recovery, campaign VFS-DEEP-II) — `K_ambient` is statically 0.0 with no
> writer.** The ambient gate is a single global float whose static initialiser is **0.0** (it lives
> in the zero-initialised data region), and it has **exactly one reader (the per-frame ambient
> sampler) and ZERO writers anywhere in the binary**. The earlier hypothesis that the sky-detail /
> quality option writes it at runtime is **DENIED** — nothing writes it. The gate is therefore an
> effective compile-time constant **0.0**, and the per-keyframe ambient term
> (`ambient = lerp(B[kf], B[kf_next], frac) × K_ambient`) evaluates to **0 every frame**. The §B
> ambient keyframe table is loaded and interpolated but contributes nothing to the device. This
> upgrades the prior "static init 0.0; runtime value UNVERIFIED" to **CONFIRMED 0.0 at runtime**.

> **CONFIRMED — the ambient BASE colour is static (0, 0, 0).** The lighting-manager constructor
> zeroes the three ambient base colour channels (R = G = B = 0) and forces the base alpha opaque.
> At area-load time the base channels are subsequently driven from a per-keyframe BYTE colour table
> inside the loaded `light%d.bin` blob (interpolated each frame), so `(0,0,0)` is the pre-load static
> default and the live base is the day/night byte colour. Either way the base is added to the
> brightness offset (§10.5) before the device push.

Consequence: with `K_ambient = 0`, the per-keyframe ambient table is inert in the shipping client.
The entire ambient floor the device receives is the additive brightness offset of §10.5.

### 10.5 Brightness slider source — `OPTION_BRIGHT` (DoOption.ini), default 100 — CONFIRMED

The global additive ambient offset originates from `OPTION_BRIGHT`, a player brightness value in
`DoOption.ini` (§Overview). The conversion to a 0–255 additive ambient offset and the device-ambient
apply are runtime math (in `specs/environment.md §6`); the on-disk facts pinned here are the value
range, the default, and the apply rule.

| Property | Value | Confidence |
|----------|-------|------------|
| Source key | `OPTION_BRIGHT` in `DoOption.ini` `[DO_OPTION]` | CONFIRMED |
| Stored type / units | i32, percent (player brightness slider) | CONFIRMED |
| Valid range / clamp | `[1, 100]` — any value `< 1` or `> 100` is reset to **100** on load | CONFIRMED |
| **Default value** | **100** (the INI-read default argument; not the previously assumed ~50) | CONFIRMED |
| Additive offset derivation | `offset = floor( (OPTION_BRIGHT / 100.0) × 255.0 )` → 0–255 | CONFIRMED |
| Device push | packed ARGB (alpha forced opaque) via the device ambient render-state | CONFIRMED |

> **CONFIRMED (apply-path recovery) — `OPTION_BRIGHT` default is 100, NOT ~50.** The consolidated
> options loader reads the key from the INI with a **default argument of 100** and clamps the result
> to `[1, 100]` (out-of-range → 100). At the default brightness the additive offset is
> `floor(100/100 × 255) = 255`; over the static `(0,0,0)` ambient base (§10.4) this makes the device
> ambient **full white `(255, 255, 255)`**. This is a material correction: earlier spec text assumed
> mid-slider ~50 (→ +0.5 floor). The faithful default ambient floor is **1.0**, not 0.5 — see
> `specs/environment.md §6.2b/§6.4`. (The byte offset of the field within the options struct is a
> dirty-room implementation detail and is intentionally not reproduced here.)

The same brightness conversion (`offset = floor(bright/100 × 255)`, add to the ambient base, clamp
each channel, pack ARGB, push to the device ambient render-state) is re-applied on three occasions:
when the user moves the slider (slider-save path), when the per-frame keyframe base colour changes
(day/night tick), and at the end of each per-area light-data load. So the brightness floor is
refreshed on slider change, on keyframe base-colour change, and on area load. CONFIRMED.

### 10.6 Sun direction is static (no per-keyframe direction)

CONFIRMED: no per-keyframe sun **direction** is stored anywhere in `light%d.bin` — the keyframe
tables (§A/§B) encode colour only. The only direction is the static fallback vector at bytes
0x14B0–0x14BF (§9.4): `(-7, 7, 20)`, normalised ≈ `(-0.322, 0.322, 0.920)`. The day/night cycle does
not rotate the sun; any sun-arc in a port is a free choice.

### 10.7 Known unknowns (apply-path)

- **Ambient multiplier `K_ambient`:** RESOLVED — **CONFIRMED 0.0** (single reader, zero writers,
  static init 0.0 — §10.4). The per-keyframe ambient table is inert in the shipping client; the prior
  "runtime writer suspected (sky-detail option)" hypothesis is DENIED.
- **Ambient base colour:** RESOLVED — **CONFIRMED static `(0,0,0)`** at init, then driven by the
  per-keyframe byte colour table at runtime (§10.4).
- **`OPTION_BRIGHT` default:** RESOLVED — **CONFIRMED 100** (INI default; clamp `[1,100]` → 100 —
  §10.5). At default brightness the device ambient is full white.
- **On-disk `DoOption.ini` override (UNVERIFIED — thin runtime residual):** the value **100** is the
  binary/INI default. If a real `DoOption.ini` on the player's disk carries a user-saved lower value,
  the live `OPTION_BRIGHT` differs from 100. Settling this requires a single runtime read of the
  stored brightness value after the options-load step (expected i32, units = percent, default 100);
  it does not change any layout or apply-path fact above.
- **EXP/EXP2 fog modes:** supported by the struct but not observed driven on the lighting tick (§2.5).
- **`fog%d.bin` start/end vs. section-C scalar:** both seed fog distance; the per-frame `s × 3.0`
  appears to win each tick. Whether start/end are ever used live (e.g. when section C is zero) is
  unverified (§10.3).
- **Indoor-area sky-init bypass (DBG-pending):** one indoor area takes a distinct sky-init bypass
  path when its fog sibling is absent (Overview "Sibling tolerance" / §1.5). The substituted
  behaviour of that single-area bypass is not resolved statically; document it honestly, do not
  guess it.

---

## Section 11: Area-0 keyframe-29 reference VALUE TABLE (sample-verified)

> **Status:** SAMPLE-VERIFIED — decoded directly from the real area-0 binaries
> (`light0.bin`, `fog0.bin`, `material0.bin`) in the live VFS and cross-checked byte-decode against
> the production parser. Recovered via the black-box harness, CAMPAIGN 9. These are concrete numeric
> values; they are neutral facts an engineer may use as a fixture / plausibility reference. The
> companion `.txt` files for area 0 are **editorial / out of sync** with the `.bin` and must NOT be
> trusted — the `.bin` floats are ground truth.
>
> This section is the value half of the area-0 char-select scene; the loader/apply contract is the
> same one specified in section 2 (fog), section 3 (material), section 9 (`light%d.bin`), and
> section 10 (apply-path). The char-select scene consumes **exactly this area-0 set at keyframe 29**.

### 11.1 Keyframe index derivation (no interpolation)

The char-select scene drives the environment with a **fixed literal time-of-day value** rather than a
live clock. With that literal time = **52200 ms** and the 48-keyframe / 1800-ms-per-keyframe cycle
(section 0), the active keyframe index and fraction are:

```
keyframe_index = floor( 52200 / 1800 )          = 29
frac           = ( 52200 mod 1800 ) / 1800.0     = 0.0   (exact -- neighbour keyframe never weighted)
```

(The same derivation at the alternate millisecond scale used elsewhere in the recon -
`52200000 / 1800000 = 29`, remainder 0 - yields the identical index 29, fraction 0.) Because the
fraction is exactly 0, the keyframe-29 row is consumed verbatim with no blend.

### 11.2 STATIC + ACHROMATIC finding (CONFIRMED for area 0)

Two area-0-specific facts make keyframe 29 representative of the entire day:

- **STATIC.** All 48 keyframe rows of `light0.bin` Section A (directional) and all 48 rows of
  `material0.bin` are **byte-for-byte identical**. Area 0 has **no day/night variation** - every
  keyframe index resolves to the same values, so the scene looks the same at any time of day. This
  confirms the section 3.3 "area-0 static table" note.
- **ACHROMATIC.** Every non-zero colour group in the area-0 material table has **R = G = B** (pure
  grey). The sky is a neutral grey tone throughout. The only channel asymmetry is in alpha
  components, which are not colour.

### 11.3 `light0.bin` keyframe-29 values (directional, ambient, fog scalar)

Float colours are in the **[0, 1]** domain, applied directly (no /255) - section 10.1. Section A is
the directional light, Section B the ambient light, Section C the per-keyframe fog-distance scalar.

| Quantity | Section / slot | R | G | B | A | Notes |
|---|---|---:|---:|---:|---:|---|
| Directional `color_A` (diffuse) | A, +0x00 | 0.047333 | 0.047333 | 0.047333 | 0.047333 | very dark grey; all channels equal |
| Directional `color_B` (specular) | A, +0x10 | 0.511818 | 0.511818 | 0.511818 | 0.580667 | medium grey; alpha differs |
| Directional `color_C` (unread) | A, +0x20 | 0.0 | 0.0 | 0.0 | 0.0 | present-but-unread (section 9.2) - do NOT feed to the math |
| Ambient `color_A` | B, +0x00 | 0.207843 | 0.207843 | 0.207843 | 0.787091 | RGB equal; **inert at runtime** (see note below) |
| Ambient `color_B` (secondary) | B, +0x10 | 0.752666 | 0.752666 | 0.752666 | 0.752666 | inert at runtime |
| Ambient `color_C` (unread) | B, +0x20 | 0.0 | 0.0 | ~ -0.0196 | 0.0 | unread; the tiny non-zero B is unconsumed noise |

| Scalar quantity | Section / offset | Value | Derived |
|---|---|---:|---|
| Fog-distance scalar `s` | C, keyframe 29 | 25.0 (world units) | LINEAR fog range = `s x 3.0` = **75.0**; near-scale = `1/s` = 0.04 (section 10.3) |
| Secondary fog scalar | D, keyframe 29 | 0.0 | no haze contribution at keyframe 29 |

**fog-scalar synthesis note (`data_load_flag = 0`):** see section 11.4. **Ambient-inert note:** the
Section-B ambient table is loaded and interpolated but the per-keyframe ambient is multiplied by the
global ambient gate `K_ambient`, which is **CONFIRMED 0.0** at runtime (section 10.4). So the
keyframe-29 ambient table **contributes nothing** to the device; the entire device-ambient floor
comes from the brightness slider (section 11.5).

The static fallback sun direction (the only sun direction the client uses - section 9.4, section
10.6) is unchanged in area 0: scale 1.0, direction `(-7, 7, 20)`, normalised approx
`(-0.3137, 0.3137, 0.8962)`. There is no per-keyframe sun rotation.

### 11.4 `fog0.bin` keyframe-29 values (LUT-synthesis branch)

| Field | Offset | Value | Notes |
|---|---|---:|---|
| `start_dist` | 0x00 | 0.500000 | fraction of view range (area-0 baseline) |
| `end_dist` | 0x04 | 0.900000 | fraction of view range (area-0 baseline) |
| `data_load_flag` | 0x08 | 0 | **synthesise** - the in-file `fog_colors[]` are NOT read (section 2.4) |
| `fog_colors[29]` (on-disk only) | 0x80 | BGRA = (57, 101, 155, 0) | author's reference colour (R=155, G=101, B=57 approx float 0.608/0.396/0.224); **NOT consumed** because the flag is 0 |

Because `data_load_flag = 0`, the 192-byte `fog_colors[]` table is **synthesised at load time from the
sky-material LUT** via the per-slot 0.75/0.25 blend (section 2.4); the on-disk BGRA above is an
editorial artefact that survives only as a plausibility check on the synthesis result. The area-0 fog
`start`/`end` fractions are confirmed as **0.5 / 0.9** (these are the real area-0 values, not the
area-1 pair 0.75 / 0.98 in section 10.2). Per section 10.3 the per-frame Section-C scalar (75.0-unit
range) overwrites these static fractions each tick, so the fractions are a secondary baseline only.

### 11.5 Device-ambient floor at keyframe 29

With the per-keyframe ambient inert (section 11.3) and the ambient base static `(0, 0, 0)` (section
10.4), the **entire** device-ambient floor for the char-select scene is the additive brightness offset
(section 10.5): at the default `OPTION_BRIGHT = 100` the offset is `floor(100/100 x 255) = 255`, i.e.
the device ambient saturates to **full white `(1.0, 1.0, 1.0)`**. So the area-0 char-select scene is
lit primarily by this white ambient floor, **not** by the dark directional keyframe colour. (A
user-saved lower `OPTION_BRIGHT` in `DoOption.ini` would lower this floor - a layout-neutral runtime
residual, section 10.7.)

### 11.6 `material0.bin` keyframe-29 values (sun / sky / cloud colour groups)

Row 29 (identical to every other row in area 0). Float groups in **RGBA** order (section 3.2). The sun
colour exceeds 1.0 - that is the **intended HDR bloom** value (section 3.2), not an error.

| Group | Indices | R | G | B | A | Notes |
|---|---|---:|---:|---:|---:|---|
| `sky_haze` | [0..3] | 0.004303 | 0.004303 | 0.004303 | 0.004303 | achromatic near-zero (approx 1/232) |
| `sun_color` | [4..7] | **1.260243** | **1.260243** | **1.148363** | 1.200000 | **HDR > 1.0** - intentional bloom |
| `secondary_sky_color` | [12..15] | 0.004303 | 0.004303 | 0.004303 | 0.004303 | equals `sky_haze` |
| `cloud_color_A` | [17..20] | 0.331212 | 0.331212 | 0.331212 | 0.331212 | mid grey (approx 84/255) |
| `cloud_color_B` | [21..24] | 0.795697 | 0.795697 | 0.795697 | 0.795697 | light grey (approx 203/255) |
| `ambient_sky_color` | [29..32] | 0.219333 | 0.219333 | 0.219333 | 0.219333 | grey (approx 56/255) |
| `emissive_sky` | [34..36] | 0.298039 | 0.298039 | 0.298039 | - | grey (approx 76/255), no alpha |
| `specular_sky` | [38..40] | 0.800000 | 0.800000 | 0.800000 | - | pure grey, no alpha |

All other indices ([8..11], [16], [25..28], [33], [37], [41..50]) are **0.0** in area 0 - consistent
with the section 3.2 "loaded but usage not traced / proposed reserved" entries.

### 11.7 Known unknowns (area-0 value table)

- The area-0 `.txt` companions (`light0.txt`, `fog0.txt`, `material0.txt`) **disagree** with the
  `.bin` for the colour tables. `fog0.txt` matches the `.bin` scalar fields (`FOG_START=0.5`,
  `FOG_END=0.9`), but `light0.txt` and `material0.txt` colour columns use an integer / signed-byte
  editorial encoding that does **not** map linearly to the `.bin` floats (the `.bin` appears to be a
  constant table the editor `.txt` was never re-baked into). The `.bin` is authoritative; the `.txt`
  must not be used to derive runtime values. UNVERIFIED whether any other area exhibits the same
  `.txt`/`.bin` divergence.
- The exact pair of sky-LUT source bands feeding the section 2.4 fog 0.75/0.25 blend is still MED
  (section 2.5); the on-disk `fog_colors[29]` reference colour above can sanity-check the synthesis
  result but does not pin the source bands.

---

## Section 12: Supplementary corrections to `terrain_layers.md §8` (`wind%d.bin`)

`terrain_layers.md §8` documents `wind%d.bin` as an 8-byte header plus a `count`-long array of
24-byte records (file size `8 + count × 24`). That overall layout is **CONFIRMED unchanged** by a
loader re-walk and a full-corpus size scan. This section adds the **loader-resolved and
sample-verified refinements** that §8 left open — the header `source_flag` semantics, the per-record
texture-id join key, the proposed record field roles, and (importantly) the **fail-on-missing**
exception to the family-wide sibling-tolerance rule. Where these refinements differ from the
`terrain_layers.md §8` text, treat **this section as authoritative** (it is loader-proven against
build `263bd994` plus the full 57-file VFS corpus).

> **Loader vs. consumer (LOADER-RESOLVED).** The wind loader does **two** things: (1) it slurps the
> whole `count × 24`-byte record block verbatim into one heap allocation (fields at +0x00..+0x10 stay
> raw, decoded later by the foliage-sway update/draw path), and (2) it conditionally decodes **only**
> the per-record texture id at +0x14 — and only when the header `source_flag` is non-zero. So the
> record-field roles in §12.3 below are CONSUMER-side proposals (corroborated by constructor defaults),
> while the header semantics and the texture-id join key in §12.1–§12.2 are loader-proven.

### 12.1 Header semantics (8 bytes) — `source_flag` gates texture binding, NOT wind on/off

| Offset | Size | Type | Field | Notes | Confidence |
|-------:|-----:|------|-------|-------|------------|
| 0x00 | 4 | u32 | `record_count` | Number of 24-byte records. `0` ⇒ no record block; the file is exactly 8 bytes. The empty 8-byte form is a **valid, common shipping form** (whole 200-series areas ship it) — a parser must NOT treat an 8-byte file as truncated. | CONFIRMED (loader + 57 samples) |
| 0x04 | 4 | u32 | `source_flag` | **Gates the per-record texture-binding loop only** — it is NOT a "wind enabled / disabled" flag. `0` ⇒ records are still read, but each record's `tex_id` (+0x14) is NOT registered as a texture. Non-zero ⇒ the loader walks the records and registers each `tex_id` as a `data/sky/texture/wind%d.dds` texture (§12.2). **`source_flag` is `0` in 100% of the 57-file corpus**, so the texture-binding path is effectively dead data in shipping. (This is the `flag2` of `terrain_layers.md §8.1`; its "activates foliage-sway curve seeding" gloss is refined here to "gates per-record texture registration".) | CONFIRMED (loader); SAMPLE-VERIFIED (always 0) |

Two witnesses for the 8-byte header: the loader reads two consecutive u32 words before any record
block, and every sample file size equals `8 + record_count × 24` exactly (57/57 files).

### 12.2 Per-record texture-id join key (+0x14 → `wind%d.dds`)

The word at record offset **+0x14** is the **texture id** (this matches `terrain_layers.md §8.2`'s
CONFIRMED `texture_id` and supersedes its earlier `sway_seed` reading). The loader reads this dword
and, **only when `source_flag` is non-zero**, registers it with the foliage-sway material lookup:

- **Join key:** `record.tex_id` → **`data/sky/texture/wind<tex_id>.dds`** (the same `data/sky/texture/`
  family as cloud/star/lensflare/moon/sun/rain textures — add `wind<id>.dds` to that chain).
- The id is registered through a small per-area wind-texture set that is **clamped to a maximum of 16
  textures** (ids beyond the 16th are dropped); duplicate ids are de-duplicated before the DDS is
  built and cached.
- **Float-vs-int duality:** the file stores the +0x14 word; the loader consumes it as an **integer**
  texture id (with a `>= 1` validity check). In every sample the word is `0.0` (= integer 0), so even
  if `source_flag` were set the texture path would be skipped. Whether the field is authored as a
  float or an int on disk is **UNVERIFIED** — the corpus has no non-zero / `source_flag = 1` example.

### 12.3 Proposed record field roles (consumer-side, +0x00..+0x10)

The five words preceding `tex_id` are slurped raw and consumed by the foliage-sway update/draw path
(not decoded at load time). Their roles are **PROPOSED** from observed value ranges plus the
correlation with the per-emitter float defaults seeded in the wind-object constructor (the constructor
seeds `speed`-like, world-coordinate-like, and `scale`-like immediates that line up with the populated
record values). Records appear to **override** those constructor defaults per emitter:

| Rec offset | Size | Type | Proposed field | Observed values (populated sample, count=4) | Confidence |
|-----------:|-----:|------|----------------|----------------------------------------------|------------|
| +0x00 | 4 | f32 | `_pad0` / unknown_0 | always `0.0` in samples | SAMPLE-VERIFIED (0.0) |
| +0x04 | 4 | f32 | `speed` (proposed) | `3.0, 3.0, 4.0, 6.0` — matches the per-emitter speed defaults seeded in the constructor | PROPOSED |
| +0x08 | 4 | f32 | `_pad2` / unknown_1 | always `0.0` in samples | SAMPLE-VERIFIED (0.0) |
| +0x0C | 4 | f32 | `coord` (proposed) | `0.0, 1024.0, 512.0, 1536.0` — multiples of 512; 1024 = the MH cell unit; looks like a world coordinate / lane offset | PROPOSED |
| +0x10 | 4 | f32 | `scale` (proposed) | `1.0, 1.0, 1.5, 1.0` — matches a scale default seeded in the constructor | PROPOSED |
| +0x14 | 4 | u32 | `tex_id` | `0` in all samples | CONFIRMED (loader reads +0x14); SAMPLE-VERIFIED (0) |

This refines `terrain_layers.md §8.2`'s "possibly time key / direction X/Y/Z / wind speed / frequency"
guesses: +0x00 and +0x08 read as **pad** (always zero), +0x04 as **speed**, +0x0C as a **world
coordinate / lane offset**, +0x10 as **scale**. These roles remain PROPOSED until the per-frame
foliage-sway draw consumer is read (or confirmed live).

### 12.4 ⚠️ FAIL-ON-MISSING — `wind%d.bin` is the EXCEPTION to family sibling-tolerance (LOADER-RESOLVED)

> **The Overview "Sibling tolerance" rule (skip-and-default on an absent sibling) does NOT apply to
> `wind%d.bin`.** Unlike the material/light loaders (which synthesise defaults on a missing file and
> return success — §3.4, §9.5), the wind loader **returns failure (0) when the file cannot be opened**,
> and the area-load hub **propagates that result as the area-load outcome** (the hub's final step is
> "succeed only if the wind load succeeded"). So a *missing* `wind%d.bin` would **fail the area load**.

In practice this never triggers, because **every shipping area ships a `wind%d.bin`** — at minimum the
empty 8-byte form (§12.1). The port consequence is concrete: a faithful client must **ship/emit a
per-area `wind%d.bin` for every area (the empty 8-byte form is sufficient)** rather than omitting the
file. This also corrects `terrain_layers.md §8`'s "Missing file or zero-count file produces no foliage
sway" — a *zero-count* file is fine (it is read successfully and yields no sway), but a *missing* file
is a load failure, not a silent no-sway. (The empty-file vs. missing-file distinction is the load
witness; the failure-propagation is flagged for a live `?ext=dbg` confirmation, though the static read
is unambiguous.)

### 12.5 Runtime ownership and load order

- The wind data is owned by a **process-global wind manager singleton** (lazily constructed on first
  use), not a per-area throwaway object. Besides the area-load hub, it is also pulled by the terrain
  ring-streaming path, i.e. the wind/foliage geometry participates in the terrain/world draw.
- In the per-area `.bin` load sequence the wind load runs **LAST**, after the map-option, fog,
  material, sound, weather, and sky-system steps. The `%d` substitution is the **current area id**
  (set by the hub) — there is no `.txt` companion that drives the binary (the `wind%d.txt` siblings
  are editor exports only, per `terrain_layers.md §8`).

### 12.6 Known unknowns (`wind%d.bin`)

- **Record field roles (+0x04/+0x0C/+0x10 = speed/coord/scale):** PROPOSED from value ranges +
  constructor-default correlation; not yet confirmed from the per-frame foliage-sway draw consumer.
  Confirming +0x00/+0x08 are truly pad also awaits that consumer.
- **`tex_id` authored type (float vs int):** all samples `0`, so the on-disk authoring convention is
  unverified; the loader consumes the word as an int. Needs a non-zero / `source_flag = 1` sample.
- **`source_flag` non-zero in production:** not observed in 57 files; whether any shipped area enables
  the per-record texture-binding path is unverified (likely a never-shipped editor feature, like
  `weather%d_rain.bin`).

---

## Sky texture assets

All sky textures are **DDS** files under `data/sky/texture/`. Confirmed entries in the VFS:

| Path pattern | Description | Animated | Notes |
|-------------|-------------|:--------:|-------|
| `data/sky/texture/sun.dds` | Sun billboard texture | No | Single file |
| `data/sky/texture/moon%d.dds` | Moon phase textures | No | Numbered variants |
| `data/sky/texture/cloud%d.dds` | Cloud dome textures | Ping-pong 2-frame (layer 1) / 4-frame (layer 2) | Indexed by `cloud_cycle%d.bin` |
| `data/sky/texture/star.dds` | Star point-sprite texture | No | Single file |
| `data/sky/texture/lensflare%d.dds` | Lens-flare layers | No | Numbered; gated by `lensflare_enable` |
| `data/sky/texture/wind%d.dds` | Foliage-sway / wind material | No | Indexed by `wind%d.bin` record `tex_id` (+0x14), bound only when `source_flag != 0` — §12.2; never exercised in the shipping corpus (`source_flag = 0` everywhere) |
| `data/sky/texture/rain_drop.dds` | Rain drop splash | No | Weather only; rain built procedurally at runtime (§8) |
| `data/sky/texture/rains.dds` | Rain streak | No | Weather only; rain built procedurally at runtime (§8) |
| `data/sky/texture/snow.dds` | Snow particle | No | Weather only |

Note: `.box` skybox mesh files (`data/sky/dat/sky%d.box`) are **not** present in the VFS.
`skybox_enable` is 0 in all validated areas; the skybox subsystem is unused.

Note: No water texture files (e.g. `water*.dds`) were found anywhere in the VFS. This is
consistent with the confirmed negative result: the shipping client contains no water renderer
and therefore has no water texture assets (§1.4).

---

## Known unknowns (format family level)

1. **Water: RESOLVED-NEGATIVE, and NO water fields in `map_option%d.bin`.** The shipping
   client contains no water renderer (§1.4), and the Campaign-5 `.txt` ↔ `.bin`
   reconciliation (§1.1) proved `map_option%d.bin` stores no water enable/height — the old
   `water_enable` / `water_y` at 0x00/0x04 were a misread of the dungeon flag and sight-clamp
   distance. No source of a water surface Y is established anywhere; any water a reimplementation
   renders is a free engineering choice, not a reproduced asset value.
2. **`fog%d.bin` data_load_flag = 0 code path:** RESOLVED — when the flag is 0 the colour table is
   synthesised from the sky LUT via a per-slot 0.75/0.25 blend (§2.4); the exact source-band channel
   grouping in that blend is MED. When the flag is non-zero the table is read verbatim (an all-zero
   table renders literal black — synthesis is the `flag==0` branch only).
3. **`light%d.bin` gaps at 0x0900 and 0x1230:** All-zero in samples; may be wrap-around
   interpolation slots or alignment padding.
4. **`light%d.bin` sections D and E:** Section D (secondary fog scalar) contains near-zero
   values; its exact influence on the rendered haze intensity is not quantified. Section E is
   all-zero. (Section A/B's third colour group `color_C` is present-but-unread — §9.2 — and is not
   an unknown but a resolved no-op.)
5. **`cloud_cycle%d.bin` cloud ID 101:** No matching texture found. Likely a "no cloud" sentinel
   but unconfirmed.
6. **`weather%d.bin` full layout:** Zero-dominated samples; needs a rain/snow area sample for
   decode. (`weather%d_rain.bin` is NOT in this category — it has no loader and is dead editor data,
   §8.)
7. **`wind%d.bin` record field roles:** RESOLVED — layout (8-byte header + 24-byte records,
   `8 + count × 24`) is CONFIRMED, the header `source_flag` gates per-record texture binding (not
   wind on/off), the +0x14 `tex_id` joins to `data/sky/texture/wind%d.dds`, and the loader
   **fails-on-missing** (the family-wide sibling-tolerance exception) — see §12. Still open: the
   consumer-side roles of record words +0x04/+0x0C/+0x10 (proposed speed/coord/scale) and whether any
   shipped area sets `source_flag != 0` (none in the 57-file corpus).
8. **`point_light%d.bin` record position fields (+0x24–+0x30):** Documented in
   `terrain_layers.md §7`; positional interpretation (scaled coordinates vs. normalised index)
   unverified.
9. **Indoor area lighting path:** When `indoor_flag = 1`, the exact ambient-only lighting
   configuration has not been fully traced in the binary. One indoor area's sky-init fog-absent
   bypass is DBG-pending (§1.5, §10.7).
10. **`material%d.bin` unassigned indices [8..11], [25..28], [33], [37], [41..50]:** Loaded but
    usage not traced.
11. **Lighting apply-path numeric defaults:** RESOLVED — the ambient gate `K_ambient` is CONFIRMED
    0.0 (no writer), the ambient base is CONFIRMED static `(0,0,0)`, and the `OPTION_BRIGHT` default
    is CONFIRMED 100 (§10.4–§10.5). The single remaining residual is whether a user's on-disk
    `DoOption.ini` overrides the 100 default — a one-time runtime read, layout-neutral (§10.7).

---

## Cross-references

- **Related format specs:**
  - `Docs/RE/formats/terrain_layers.md §6–8` — formal specs for `light%d.bin`,
    `point_light%d.bin`, `wind%d.bin` (§9 of this document adds sample-verified corrections to the
    `light%d.bin` section; §12 adds loader-resolved corrections to the `wind%d.bin` section)
  - `Docs/RE/formats/terrain.md` — terrain cell formats (`.ted`, `.map`, `.sod`)
  - `Docs/RE/formats/texture.md` — DDS texture container
- **Runtime assembly spec:** `Docs/RE/specs/environment.md` (the runtime lighting/fog math, the
  asymmetric ambient gate `K_ambient` = 0.0, the `OPTION_BRIGHT` brightness slider default 100, and
  the "too-dark" fix; §6 there consumes the colour domains and apply-path fields pinned in §10 here)
- **Glossary:** `Docs/RE/names.yaml`
- **Provenance:** `Docs/RE/journal.md`
- **Implementation target:** `Assets.Parsers` (layer `03.Storage.Assets`). Cite this file as
  `// spec: Docs/RE/formats/environment_bins.md` on every offset reference in the parser.
  Conversion of colours to engine types is `Assets.Mapping`'s responsibility — and the colour-domain
  table (§10.1) is exactly what `Assets.Mapping` must honour (byte tables `/255`, float light colours
  pass-through).

---

## Provenance note (this revision)

**`wind%d.bin` loader-resolved promotion (build `263bd994`; wind loader re-walk + full 57-file VFS
corpus).** Added §12 — supplementary loader-resolved / sample-verified corrections to
`terrain_layers.md §8`: the 8-byte header `source_flag` **gates per-record texture binding only** (not
wind on/off; `0` across all 57 samples); the per-record texture-id join key `tex_id (+0x14) →
data/sky/texture/wind%d.dds` (clamped to 16, de-duplicated), with the int-vs-float authoring left
UNVERIFIED; the proposed consumer-side record roles (+0x04 speed, +0x0C world coord, +0x10 scale,
+0x00/+0x08 pad); and the **fail-on-missing** carve-out — the wind loader is the EXCEPTION to the
family sibling-tolerance rule (it returns failure on an absent file and the hub propagates it), so a
faithful port must ship a per-area `wind%d.bin` (the empty 8-byte form suffices). Also: added
`wind%d.dds` to the sky-texture asset table; refined family-level known-unknown #7; updated the scope
note and cross-references. No addresses, no pseudo-code.

**CAMPAIGN 10 — Block D6 two-witness re-verification (build `263bd994`; area/sky loaders + a full
per-area VFS sample scan + a `DoOption.ini` sample).** NO conflicts found on any env-bin byte table;
every core numeric claim RE-CONFIRMED [sample-verified]. Deltas applied this revision (all
affirmations / enrichments, no layout change): banner added (Status block); all eight env-bin family
sizes RE-CONFIRMED byte-exact across the full corpus (counts in the Status block); the
`data_load_flag = 1` verbatim-read branch is now a real-data witness (`fog11.bin` ships flag = 1),
upgrading §2.4 from behaviourally-inferred to sample-verified; the ambient-floor chain
(`OPTION_BRIGHT` default 100 → `floor(100/100 × 255) = 255` → white) RE-CONFIRMED against a sampled
`DoOption.ini` that omits the key (§10.4–§10.5, §11.5). Carried-not-re-witnessed this lane (flagged):
`K_ambient` = 0.0, the `OPTION_WATER` dead-consumer search, and the per-field interiors of
`material`/`light`/`stardome`/`clouddome` (sizes only re-verified). No addresses, no pseudo-code.

**CAMPAIGN VFS-MASTERY — Phase P promotion (two-witness gate: loader read-sequence + black-box
behaviour).** Surgical deltas applied that revision: (1) `light%d.bin` `color_C` (+0x20) marked
present-but-unread / LOADER-RESOLVED (§9.2); (2) `fog%d.bin` `data_load_flag` clarified — synthesis
is the `flag==0` branch only, `flag==1` + all-zero colour renders literal black, refuting "synthesise
when flag==1" (§2.4); (3) `weather%d_rain.bin` — NO LOADER, rain built from constants + RNG, the 33
files are dead editor data, do not parse (§8); (4) stardome tint is PER-STAR (consumer advances 4
bytes BGRA per star), CONFIRMED, refuting the "uniform per keyframe" reading (§4.3); (5) environment
hub default-tolerates absent siblings (skip-and-default), with the single indoor-area sky-init bypass
marked DBG-pending (Overview, §1.5, §10.7). No addresses, no pseudo-code.
