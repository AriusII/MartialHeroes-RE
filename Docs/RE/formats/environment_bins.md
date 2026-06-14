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
> `Docs/RE/formats/terrain_layers.md §6–8`; supplementary sample-verified corrections to
> those sections are noted at the end of this document (§9).

---

## Status block

| Attribute         | Value |
|-------------------|-------|
| `status`          | `sample-verified` for `map_option%d.bin`, `fog%d.bin`, `material%d.bin`, `stardome%d.bin`, `clouddome%d.bin`, `cloud_cycle%d.bin`; `partial` for `weather%d.bin` / `weather%d_rain.bin`; water renderer: **RESOLVED-NEGATIVE** (see §1.4) |
| `sample_verified` | `true` — formats confirmed against real sample files extracted from the live VFS for areas 0 and 1, with additional spot-checks across areas 4–35 for `map_option`. `weather` samples are zero-dominated; full decode awaits an area with active precipitation. |
| `binary_analysed` | `doida.exe` (legacy 32-bit client build, x86 LE) — used to identify the sky-init call chain, file-read sequences, GRSFog/GRSLighting struct offsets, and exhaustive cross-reference scan confirming the water-renderer negative result. No addresses appear in this spec. |
| `confidence`      | `CONFIRMED` = field corroborated by parser read-sequence and real sample bytes. `SAMPLE-VERIFIED` = confirmed by observed sample data alone. `CODE-CONFIRMED` = identified in parser only, no sample cross-check. `PROPOSED` = structurally inferred, no direct evidence. |

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
field** unless explicitly noted below. Missing files are silently ignored — the client falls back
to defaults for any subsystem whose file is absent.

A global quality-tier file (`DoOption.ini`, section `[DO_OPTION]`) gates rendering subsystems
by tier: `OPTION_WATER`, `OPTION_SKY`, `OPTION_WEATHER`, `OPTION_WEATHER_RAIN`, `OPTION_LENS`,
`OPTION_SUN`. The per-area `map_option%d.bin` flags operate on top of this global gate. Note:
`OPTION_WATER` is stored and written to disk but has **no confirmed runtime consumer in the
shipping binary** — see §1.4.

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

### 1.4 Water renderer — RESOLVED-NEGATIVE (context, unchanged)

> **Status: RESOLVED-NEGATIVE.** The shipping client contains **no dedicated water render path**.
> This conclusion is independent of the §1.1 field reconciliation above and remains valid: even if a
> water enable/height existed somewhere, no renderer consumes it. (As of the Campaign-5
> reconciliation, no water enable/height is stored in `map_option%d.bin` at all — see §1.1.)

Key evidence (from cross-reference analysis):

- Searches across all string literals for "water", "reflect", "ripple", and "wave" return no water
  texture path, water vertex-buffer name, or water draw-function name in the binary.
- No water texture files (e.g. `water*.dds`) exist anywhere in the VFS.

**Implication for Assets.Parsers:** there are no water fields to parse out of this file, and no
Assets.Parsers code need implement water geometry or texture lookup. Any water surface a
reimplementation chooses to render in `05.Presentation` is a free engineering decision, not a
faithful reproduction of an original asset.

### 1.5 Remaining known unknowns

- **`SUN` (0x14) vs. `MOON` (0x18) independence:** stored as two separate u32 words; in every
  observed area they hold the same value. Whether the runtime ever reads them independently (i.e.
  whether a sun-on / moon-off area is possible) is UNVERIFIED — only the two-word storage is confirmed.
- **`indoor_flag = 1` suppression set:** indoor areas suppress at minimum cloud dome, star dome,
  sun/moon, and lens flare. Whether fog parameters still apply indoors is unverified.
- **Area-0 ordering anomaly:** the cause of area 0's mismatch between `.bin` words and `.txt` values
  is unresolved (template/default candidate).

---

## Section 2: `fog%d.bin` — Per-Area Fog Parameters

**Status:** CONFIRMED  
**File size:** exactly **204 bytes** (fixed, no header magic)

### 2.1 Field table

| Offset | Size | Type | Field | Notes | Confidence |
|-------:|-----:|------|-------|-------|------------|
| 0x00 | 4 | f32 | `start_dist` | Fog start distance. Observed range 0.0–1.0; interpreted as a fraction of the configured view range. Area 0 sample: 0.5 | CONFIRMED |
| 0x04 | 4 | f32 | `end_dist` | Fog end distance. Same scale as `start_dist`. Area 0 sample: 0.9 | CONFIRMED |
| 0x08 | 4 | u32 | `data_load_flag` | 0 = derive fog colour from the material colour table at runtime; 1 = use the `fog_colors[]` array directly. Area 0 and area 1 both have value 0 | CONFIRMED |
| 0x0C | 192 | u8[192] | `fog_colors[48]` | 48 BGRA colour entries, one per day/night keyframe. Each entry is 4 bytes. See §2.2 | CONFIRMED |

Total: 4 + 4 + 4 + 192 = 204 bytes.

### 2.2 BGRA colour encoding

Each 4-byte fog colour entry uses BGRA byte order (matching the legacy D3D `D3DCOLOR` convention):

| Byte within entry | Semantic | Notes |
|:-----------------:|----------|-------|
| [0] | Blue | u8, 0–255 |
| [1] | Green | u8, 0–255 |
| [2] | Red | u8, 0–255 |
| [3] | Alpha | u8, always 0 in all sampled data |

Keyframe 0 corresponds to midnight; keyframe 24 to noon. Colour transitions follow the day/night
cycle defined in §0.

### 2.3 Fog colour sample (area 0 — illustrative, not an asset reproduction)

Representative colour character at four keyframes of area 0 (all samples are dark/muted outdoor
atmospheres; exact numeric values are in the dirty-room samples, not in this spec):

| Keyframe | Approximate character |
|----------|-----------------------|
| kf 0  (midnight) | Very dark cool grey |
| kf 16 (pre-dawn) | Warm blue-orange transition |
| kf 24 (noon) | Muted sky-blue with orange tint |
| kf 40 (evening) | Dark, near-neutral |

### 2.4 `data_load_flag` semantics

When `data_load_flag = 0` the client may override the `fog_colors[]` array by sampling from the
material colour table (`material%d.bin`). The exact blend or override logic is not confirmed —
both files contain valid day-night colour sequences in sampled areas regardless of this flag. The
flag's role in the runtime fog colour pipeline remains a known unknown (§2.5).

### 2.5 Known unknowns

- **`data_load_flag = 0` vs. `= 1` code path:** Whether the fog colour array is ignored,
  overwritten, or blended with material table values when `data_load_flag = 0` is unresolved.
  All sampled areas use value 0 yet contain populated colour arrays.
- **Fog mode (EXP / EXP2 / LINEAR):** The D3D9 fog type (1 = EXP, 2 = EXP2, 3 = LINEAR) is
  stored in the runtime `GRSFog` struct but is not loaded from this file; its source is untraced.

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

### 4.3 Usage notes

In all sampled data all 192 star instances within a given keyframe share the **same** BGRA value
(uniform tint across all stars per sky-hour). Whether per-instance colour variation is possible
(i.e. whether individual stars can have different colours) is not determinable from the available
samples — the tint may simply be overridden by a single per-frame colour read.

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
- **Interaction with `weather%d_rain.bin`:** Whether both files must be present simultaneously
  for weather to activate is unconfirmed.

---

## Section 8: `weather%d_rain.bin` — Rain Weather Variant

**Status:** PARTIAL — file size confirmed, content sparse  
**File size:** exactly **240 bytes** (fixed)

### 8.1 File layout

Layout identical in size to `weather%d.bin`. Present as a companion for areas with explicit
rain configurations.

| Offset | Size | Type | Field | Notes | Confidence |
|-------:|-----:|------|-------|-------|------------|
| 0x00 | 240 | u8[240] | _body_ | Largely zeros; first few bytes non-zero in some sampled files (character consistent with a small header or colour value) | SAMPLE-VERIFIED (size only) |

### 8.2 Known unknowns

- **Full field layout:** Awaiting a sample from an area with active rain for decode.
- **`weather%d.bin` vs. `weather%d_rain.bin` relationship:** May be a base + variant pair, or
  the rain file may entirely replace the base file for rain areas.

---

## Section 9: Supplementary corrections to `terrain_layers.md §6` (`light%d.bin`)

`terrain_layers.md §6` documented `light%d.bin` from parser evidence only (`sample_verified:
false`). Real samples are now available for areas 0 and 1. The following corrections and
additions are **sample-verified** and should be considered authoritative over the earlier
parser-only entries in `terrain_layers.md §6`:

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
| +0x00 | 16 | f32[4] | `color_A` (RGBA) | Primary colour — diffuse for section A, ambient for section B. RGBA, alpha always 0. | CONFIRMED |
| +0x10 | 16 | f32[4] | `color_B` (RGBA) | Secondary colour — possibly specular for section A. Read by the time-update path. | CODE-CONFIRMED |
| +0x20 | 16 | f32[4] | `color_C` (RGBA) | All zeros in all sampled data. Reserved or unused. | SAMPLE-VERIFIED (all zeros) |

The per-field sun/moon color assignments from `terrain_layers.md §6.2` (indices 0–2 as
`sun_colour`, index 5 as `moon_colour[1]`, etc.) remain the best parser-side interpretation and
are not contradicted by the sample bytes. They are retained in `terrain_layers.md` as
CODE-CONFIRMED; the float4-group view above is a complementary, sample-verified structural view.

### 9.3 Section C fog scalar — revised interpretation

The f32 values in section C represent a **fog distance scale in world units**, not a normalised
density. Sampled range for area 1: approximately 8 to 43 world units. The value 1.0 noted in
`terrain_layers.md §6.4` as a "no-override sentinel" is not observed in real samples; the
sentinel behaviour may apply only in parser edge cases. Engineers should treat section C as a
world-unit fog range modifier applied on top of `fog%d.bin` `start_dist` / `end_dist`.

### 9.4 Fallback directional light (bytes 0x14B0–0x14BF)

| Index | Field | Value | Confidence |
|:-----:|-------|-------|------------|
| [0] | `scale` | 1.0 | CONFIRMED |
| [1] | `dir_X` | −7.0 | CONFIRMED |
| [2] | `dir_Y` | 7.0 | CONFIRMED |
| [3] | `dir_Z` | 20.0 | CONFIRMED |

This is the unnormalised world-space direction vector used when no runtime light is active. The
Godot-side normalised vector is `normalize((-7, 7, 20))` ≈ `(-0.322, 0.322, 0.920)`.

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
| `data/sky/texture/rain_drop.dds` | Rain drop splash | No | Weather only |
| `data/sky/texture/rains.dds` | Rain streak | No | Weather only |
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
2. **`fog%d.bin` data_load_flag = 0 code path:** Whether `fog_colors[]` is overridden by the
   material table or used directly when this flag is 0 is not confirmed.
3. **`light%d.bin` gaps at 0x0900 and 0x1230:** All-zero in samples; may be wrap-around
   interpolation slots or alignment padding.
4. **`light%d.bin` sections D and E:** Section D (secondary fog scalar) contains near-zero
   values; its exact influence on the rendered haze intensity is not quantified. Section E is
   all-zero.
5. **`cloud_cycle%d.bin` cloud ID 101:** No matching texture found. Likely a "no cloud" sentinel
   but unconfirmed.
6. **`weather%d.bin` and `weather%d_rain.bin` full layout:** Zero-dominated samples; needs a
   rain/snow area sample for decode.
7. **`wind%d.bin` keyframe field layout:** Documented in `terrain_layers.md §8`; no non-zero
   samples available.
8. **`point_light%d.bin` record position fields (+0x24–+0x30):** Documented in
   `terrain_layers.md §7`; positional interpretation (scaled coordinates vs. normalised index)
   unverified.
9. **Indoor area lighting path:** When `indoor_flag = 1`, the exact ambient-only lighting
   configuration has not been fully traced in the binary.
10. **`material%d.bin` unassigned indices [8..11], [25..28], [33], [37], [41..50]:** Loaded but
    usage not traced.

---

## Cross-references

- **Related format specs:**
  - `Docs/RE/formats/terrain_layers.md §6–8` — formal specs for `light%d.bin`,
    `point_light%d.bin`, `wind%d.bin` (§9 of this document adds sample-verified corrections)
  - `Docs/RE/formats/terrain.md` — terrain cell formats (`.ted`, `.map`, `.sod`)
  - `Docs/RE/formats/texture.md` — DDS texture container
- **Runtime assembly spec:** `Docs/RE/specs/environment.md`
- **Glossary:** `Docs/RE/names.yaml`
- **Provenance:** `Docs/RE/journal.md`
- **Implementation target:** `Assets.Parsers` (layer `03.Storage.Assets`). Cite this file as
  `// spec: Docs/RE/formats/environment_bins.md` on every offset reference in the parser.
  Conversion of colours to engine types is `Assets.Mapping`'s responsibility.
