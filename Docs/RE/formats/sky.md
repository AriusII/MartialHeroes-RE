# Format: sky data files  (sky-dome geometry `.box` + sky `.bin` family — parser view)

```
verification:   sample-verified   # .box absence + fog file size + cloud_cycle size matched against a real VFS sample; sun→screen→flare transform + lensflare.txt carried [confirmed] from CYCLE 7 static IDA; orbit/day-cycle math carried [confirmed] from prior IDA
ida_reverified: 2026-06-21         # IDB SHA 263bd994 — ASSET-FIDELITY (2026-06-21) re-confirmed the .box skybox ABSENCE: the .box by-name open path is wired but gated by the map_option SKYBOX flag (reset to 0 before each area load); the sun/moon orbiting billboards are a separate system (Section D); prior CYCLE 7 (2026-06-20) added the sun→screen→lens-flare transform + lensflare.txt
ida_anchor:     263bd994
evidence:       [static-ida, vfs-sample]
conflicts:      none-open         # campaign-10 conflict C5 (fog "12 or 204 bytes") RESOLVED into §B.2 below; CYCLE 7 refined the §D.2 sun-orbit model (log-curve, not sin) and resolved the §D.4 flare transform
```

> Two-witness re-verification on build `263bd994` (fog loader read-sequence + VFS sample scans)
> RE-CONFIRMED the `.box` absence, the fog colour-derivation loop, and the env-bin sizes, and applied
> one wording correction (C5): **fog files are always 204 bytes on disk** — the "either 12 or 204
> bytes" the earlier §B.2 quoted describes the loader's READ VOLUME when `data_load_flag = 0` (it reads
> only the leading 12 bytes), not the on-disk file size. The authoring tool always writes the full
> 204-byte record; the trailing 192 bytes are simply unread on the `flag = 0` branch.
>
> **CYCLE 7 (2026-06-20, IDB SHA `263bd994`, static-only) added/refined Section D:** the full
> **sun → screen → lens-flare anchor transform** (§D.4, previously UNRESOLVED — now pinned in shape),
> the **`data/sky/lensflare.txt`** config layout and the flare ghost-chain draw (§D.4), the **sun
> billboard texture/size** facts (§D.5), and a refinement of the **sun/moon orbit model** (§D.2) from
> the earlier `sin`-based reading to the client's actual **natural-log-curve** form (the X-sign
> opposition stands; the position vector also doubles, negated, as the directional-light direction).

> Clean-room spec. Neutral description only — NO sample bytes, NO decompiler pseudo-code,
> NO binary addresses. Consumed by `Assets.Parsers`. Every offset an engineer cites must
> reference this file.
>
> **Scope split — read this first.** The per-area sky **colour `.bin` family** (`fog%d.bin`,
> `material%d.bin`, `stardome%d.bin`, `clouddome%d.bin`, `cloud_cycle%d.bin`, `map_option%d.bin`,
> weather) is **already specified, sample-verified, against real archive bytes** in
> `Docs/RE/formats/environment_bins.md` — that document is **authoritative** for those byte tables.
> This document adds:
> - the **`sky%d.box` sky-dome geometry format** (new here — VFS-only, no sample available), and
> - a **parser read-order / load-order view** of the `.bin` family and the **48-slot day cycle**,
>   cross-referenced to `environment_bins.md` so the two never contradict.
>
> Where a `.bin` byte layout below is also in `environment_bins.md`, this document defers to that
> file for the canonical table and notes only the parser-side facts (read order, derivation math).

## Identification

- **Logical paths:** `data/sky/dat/<name><area_id>.bin` for the colour `.bin` family;
  `data/sky/dat/sky<area_id>.box` for the sky-dome geometry; sky textures expand to
  `data/sky/texture/<name>.dds`. `<area_id>` is the active integer area id.
- **Found in:** the `.pak` archive / VFS (see `formats/pak.md`). The `.box` file is opened through
  the **archive by-name lookup** — but see §A: no `.box` entry exists in the production VFS.
- **Endianness:** little-endian throughout.
- **Magic / version:** none — these files have no magic number and no version field. File identity
  comes from the path; field meaning comes from the per-file loader.

---

## Section A — `sky%d.box`  (sky-dome geometry, NEW)

> **Status: CONFIRMED-ABSENT from the production VFS.** A census of the full 43,347-entry
> production VFS (the archive set the real client mounts) returned **zero `.box` files** of any
> kind. The skybox enable flag in `map_option%d.bin` (the `SKYBOX` field at offset 0x1C — see
> `environment_bins.md §1`) is **0 in every observed area**. The skybox feature was defined in the
> client's parser source but **no skybox asset was ever authored** for any area: the read sequence
> exists, the asset does not.
>
> **The hypothesized byte layout below (§A.1–§A.6) remains UNVERIFIED.** Because no `.box` sample
> exists anywhere in the VFS, the layout can be neither confirmed nor denied by byte inspection; it
> rests on the parser read-sequence alone. Engineers must NOT treat any field below as load-bearing.
>
> **Engineering guidance:** since no `.box` asset exists, do not implement a `.box` loader for
> faithful reproduction. A **synthetic sky-dome** (procedurally generated dome geometry, tinted by
> the sample-verified colour tables in `environment_bins.md`) is the correct engineering path for
> the sky — there is no original asset to reproduce here, only the colour/lighting data, which is
> already specified. The §A.1–§A.6 tables are retained only as a record of the parser-side read
> order, in case a `.box` asset ever surfaces from another archive. (The visible sun/moon BILLBOARDS
> that DO orbit the sky — separate from this absent skybox mesh — are specified in **Section D**.)

A `.box` file (if one existed) would describe a set of sky-dome **textured meshes** (one per skybox
texture). It is opened via the archive by-name lookup. The hypothesized layout is a count-prefixed
sequence of three parallel sections: texture-name records, per-mesh vertex arrays, and per-mesh
index arrays.

### A.1 Header (hypothesized — UNVERIFIED)

| Offset | Size | Type | Field | Notes | Confidence |
|-------:|-----:|------|-------|-------|------------|
| 0x00 | 4 | u32 | `texture_count` | number of skybox textures = number of meshes | UNVERIFIED (no sample) |

### A.2 Texture-name records (× `texture_count`) (hypothesized — UNVERIFIED)

Read `texture_count` fixed-width name records, in order:

| Offset | Size | Type | Field | Notes | Confidence |
|-------:|-----:|------|-------|-------|------------|
| +0x00 | 47 | char[47] | `texture_name` | fixed-width texture name; would expand to `data/sky/texture/<name>.dds` | UNVERIFIED (no sample) |

- **Record stride:** 47 bytes (the name field is the whole record) — parser-derived, UNVERIFIED.
- Each name would allocate a texture wrapper at load time; the global sky detail-level option selects
  an animated vs. static default texture for the slot.

### A.3 Per-mesh vertex array (× `texture_count` meshes) (hypothesized — UNVERIFIED)

For each mesh, in order:

1. Read `u32 vertex_count`. **Cap: 300 (0x12C)** — a larger value would be a load error.
2. Read `vertex_count` vertices, each **20 bytes** (the in-memory buffer would be
   `4 + 20 × vertex_count` bytes: a count prefix followed by the packed vertices).

- **Record count source:** the per-mesh `vertex_count` u32 immediately preceding the array.
- **Vertex stride:** 20 bytes — parser-derived, UNVERIFIED.

### A.4 Vertex decode (20-byte stride) — UNVERIFIED

The 20-byte vertex most plausibly decodes as position + UV:

| Sub-offset | Size | Type | Field | Confidence |
|-----------:|-----:|------|-------|------------|
| 0x00 | 12 | f32[3] | `position` (x, y, z) | UNVERIFIED |
| 0x0C | 8 | f32[2] | `uv` (u, v) | UNVERIFIED |

`12 + 8 = 20`. An alternative packing (12-byte position + 4-byte packed colour + 4-byte single UV
pair) also fits 20 bytes; neither can be confirmed without a sample.

> Coordinate note: world geometry in this client negates Z when mapping to the target engine (see the
> project coordinate conventions). The same Z handling would apply to `.box` positions; this is moot
> while no `.box` asset exists.

### A.5 Per-mesh index array (× `texture_count` meshes) (hypothesized — UNVERIFIED)

For each mesh, in order:

1. Read `u32 index_count`. **Cap: 900 (0x384)** — a larger value would be a load error.
2. Read `index_count` indices, each **2 bytes (u16)** (the buffer would be `2 × index_count` bytes).

- **Index width:** 16-bit (u16) — parser-derived, UNVERIFIED.
- **Record count source:** the per-mesh `index_count` u32 immediately preceding the array.

### A.6 Overall `.box` structure (hypothesized — UNVERIFIED)

```
u32 texture_count
texture_name[texture_count]                 // 47 bytes each
for each mesh m in 0..texture_count-1:       // vertex arrays, one per mesh, in order
    u32 vertex_count (cap 300)
    vertex[vertex_count]                     // 20 bytes each
for each mesh m in 0..texture_count-1:       // index arrays, one per mesh, in order
    u32 index_count (cap 900)
    u16 index[index_count]
```

The exact interleave of the vertex-array block vs. the index-array block (whether all vertex arrays
precede all index arrays, or they alternate per mesh) was read in two passes over the mesh count in
the parser; the order above (all vertex arrays, then all index arrays) is the parser read order.
None of this is sample-confirmed.

---

## Section B — sky `.bin` colour family (parser read-order view)

> The **byte layouts** for the files in this section are authoritative in
> `Docs/RE/formats/environment_bins.md`. This section adds only the parser-side facts: the
> environment load order, the fog 192-byte colour-table derivation, and the 48-slot day cycle.

### B.1 Environment load order (per area activation)

When an area is activated (or the sky option is toggled), an environment hub constructs the sky in a
fixed order. Each sub-init that fails aborts the hub; on full success a "ready" flag is set. Order:

1. Particle/effect buffer root.
2. **Star-dome** object — then loads `stardome%d.bin`.
3. **Cloud-dome** object — then loads `clouddome%d.bin` + `cloud_cycle%d.bin`.
4. Read the global **sky detail-level** option (values 0 / 1 / 2; selects animated vs. static sky
   textures and a derived detail scalar).
5. **Sun and Moon billboard** init (the moon uses `moon%d.dds`). These are point/particle
   billboards that, after init, **orbit the sky every frame** on the time-of-day angle — see
   **Section D** (the per-frame orbit, the 15-phase moon, and lens flare).
6. **Sky-box** sub-object — conditionally loads `sky%d.box` (Section A) when the skybox gate is set.
   In the shipping VFS this branch is never taken: the skybox gate is always 0 and no `.box` asset
   exists (see §A).
7. Bind the **day/night time manager** to the dome.
8. **Fog** init — loads `fog%d.bin`.

The environment object **is** the sky-colour-table singleton (it is not a separate "environment
manager" allocation); the sub-domes (star, cloud, sky-box, moon) are pointer members hung off it,
and the fog parameters and colour table are stored inline on it.

> **CYCLE 7 build-order confirmation (build `263bd994`).** The sky-system init constructs, in order:
> **sun → star-dome → cloud-dome → moon → material → light → fog.** (The §B.1 list above is the wider
> environment-hub sequence including the particle root, the detail-level read, the conditional sky-box
> branch and the time-manager bind; this CYCLE 7 order is the sky-dome sub-object construction inside
> it. The two are consistent: the dome objects come up first — sun, star, cloud, moon — then the
> colour/shading data — material, light, fog — is loaded onto them.)

### B.1a Sky data-file family (all `<n>` = active area id) — CONFIRMED (CYCLE 7)

The sky system references the following per-area data files (`<n>` is the active integer area id) and
shared textures. The byte layouts of the `.bin` files are canonical in `environment_bins.md`; this
table is the **file-family enumeration** that belongs to `formats/sky.md`.

| Data file (`data/sky/dat/`) | Role | Byte layout |
|---|---|---|
| `sky<n>.box` | sky-dome geometry (CONFIRMED-ABSENT in shipping VFS) | §A (UNVERIFIED) |
| `wind<n>.bin` | wind | `environment_bins.md §9` |
| `weather<n>.bin` | weather | `environment_bins.md` (weather) |
| `point_light<n>.bin` | point lights | `environment_bins.md` |
| `cloud_cycle<n>.bin` | per-step cloud texture-id table | §B.3 / `environment_bins.md §6` |
| `material<n>.bin` | sun/colour material table | `environment_bins.md` (material) |
| `light<n>.bin` | directional/ambient light table | `environment_bins.md §10` |
| `fog<n>.bin` | fog near/far + colour table | §B.2 / `environment_bins.md §2` |
| `clouddome<n>.bin` | two-tier cloud colour grid | §B.3 / `environment_bins.md §5` |
| `stardome<n>.bin` | star colour grid | §B.4 / `environment_bins.md §4` |
| `map_option<n>.bin` | per-area option flags (skybox, lens-flare, …) | `environment_bins.md §1` |
| `map/map<n>.txt` | map text (referenced by the sky load) | — |

Shared textures under `data/sky/texture/`:

| Texture | Use |
|---|---|
| `cloud<n>.dds` | cloud-dome (ping-pong animated) |
| `star.dds` | star-dome |
| `sun.dds` | sun billboard (§D.5) |
| `moon<n>.dds` | moon billboard, phase-selected `n = 0..14` (§D.3) |
| `lensflare<n>.dds` | lens-flare ghost sprites, 1-based (§D.4) |
| `wind<n>.dds` | wind effect |
| `rain_drop.dds`, `rains.dds`, `snow.dds` | weather precipitation |

### B.2 `fog%d.bin` — parser view

`fog%d.bin` is read as: two leading 4-byte values (`start_dist`, `end_dist`), then a 4-byte
`data_load_flag`, then — **only when `data_load_flag` is non-zero** — a **192-byte (0xC0) colour
table**. So the loader's **read volume** is **either 12 bytes** (flag = 0, the trailing colour table is
not read and the colour is derived at runtime) **or 204 bytes** (flag ≠ 0, explicit 192-byte colour
table read verbatim).

> **CORRECTED (sample-verified, build `263bd994`, conflict C5) — on-disk size is always 204 bytes.**
> Every `fog%d.bin` on disk is **uniformly 204 bytes regardless of the flag** (a flag = 0 file still
> carries the unused 192-byte block on disk — the authoring tool always writes the full record). The
> "either 12 or 204 bytes" above describes the loader's **read volume** (12 bytes consumed when
> flag = 0), NOT two on-disk file sizes. A real-data witness of the flag = 1 branch exists
> (`fog11.bin` ships `data_load_flag = 1`), so the verbatim-read branch is live, not merely inferred.
> Sample scan: **82 fog files, all exactly 204 bytes.**

The canonical byte table (the 48-entry 4-byte BGRA layout of the 192-byte block, and the confirmed
`start_dist`/`end_dist` float typing) is in `environment_bins.md §2`.

**Fog colour derivation when `data_load_flag = 0` (parser-confirmed):** the fog colour table is
**derived from the sky-colour table** rather than read from the file. The derivation loop runs
**48 iterations** (one per day slot), walks the sky-colour table at a **192-byte stride per slot**,
and for each slot writes a blended colour into the fog colour buffer using a fixed
**0.75 / 0.25 weighted blend** of two source bands per channel:

```
fog_channel = source_high * 0.75 + source_low * 0.25
```

This confirms that the 48 × 192-byte sky-colour table is the **master per-time colour LUT** for the
whole sky system, and that fog, when not given an explicit table, is a 3:1 weighted blend of it
sampled per day slot. The structure is HIGH; the exact channel grouping of the source bands is MED.

> This resolves the `environment_bins.md §2.5` open question on the `data_load_flag = 0` colour
> source: when the flag is 0 the fog colour is **derived** (the 0.75/0.25 blend above), not read from
> a `fog_colors[]` array in the 12-byte file.

### B.3 `clouddome%d.bin` + `cloud_cycle%d.bin` — parser view

- `clouddome%d.bin` is read as **two equal halves** (a two-tier A/B colour grid blended over the
  day). The canonical sizes are in `environment_bins.md §5` (`clouddome%d.bin` is 23040 bytes —
  two 11520-byte halves).
- `cloud_cycle%d.bin` is read as a single **70-byte (0x46)** per-step cloud texture-id table. The
  per-cloud row is consumed at a **7-wide stride** (`base + 7×row + col`); `70 = 10 × 7` (the
  7-column stride is HIGH; the "10 variants" reading is MED). Cloud textures are
  `data/sky/texture/cloud%d.dds`, ping-pong animated across two surfaces. Canonical table:
  `environment_bins.md §6`.

> **Correction carried forward:** the cloud-dome loader was previously mislabelled as a wind loader
> in an older note. It loads `clouddome%d.bin` + `cloud_cycle%d.bin`, **not** wind. Wind is a
> separate routine (`wind%d.bin`) outside the sky-colour lane — see `environment_bins.md §9` /
> `terrain_layers.md`.

### B.4 `stardome%d.bin` — parser view

Read as a single **9216-byte (0x2400)** star colour grid into the star-dome object. After the read,
a sentinel is cleared and a two-tier brightness-modulation pass (similar to the cloud grid) drives
the night-sky star brightness. The canonical byte table (12 keyframes × 192 stars × 4 bytes BGRA) is
in `environment_bins.md §4`. The exact grid factoring at the parser level is MED.

---

## Section C — 48-slot day cycle (CONFIRMED)

The sky colour system is sampled on a **48-slot day cycle**:

| Constant | Value | Notes |
|----------|------:|-------|
| slots per day | 48 | one slot per half-hour of a 24 h simulated day |
| seconds per slot | 1800 | `0x708` |
| day length | 86400 s | `48 × 1800` |

The runtime sampler indexes the master sky-colour table by time-of-day using 1800 s per slot,
**bilinearly interpolates** between the two adjacent slots, and pushes the resulting colour to the
render device. The pushed colour is in **BGRX** byte order with the high byte forced opaque (byte 2 =
R, byte 1 = G, byte 0 = B, byte 3 = 0xFF). This is the runtime consumer that proves the colour table
is **time-indexed, 48 slots covering a full day**.

> The detailed keyframe-to-time mapping, the BGRA→engine colour conversion, and the per-frame update
> loop are specified in `Docs/RE/specs/environment.md §2` and `environment_bins.md §0`; this section
> only fixes the slot count (48), the slot period (1800 s), and the day length (86400 s) as confirmed
> from the sampler. (Note the coarser **12-slot** cycle used for star-dome / cloud-dome tints — see
> `environment.md §2.1`.)

---

## Section D — sun / moon billboards (time-of-day orbit, moon phase, lens flare) — NEW

> **Status: genuine new content; does NOT contradict `environment_bins.md §10.6`.** §10.6 establishes
> that the directional **shading LIGHT** is static (`(-7, 7, 20)`, colour-only over the day cycle) —
> that finding stands. The visible **sun/moon BILLBOARDS** are a **separate subsystem** that DOES arc
> across the sky on a time-of-day angle, recomputed every frame by the map-clock tick. The two are
> independent: the shaded light vector is fixed; the billboard sprites orbit. A faithful port keeps the
> directional light fixed **and** moves the sun/moon sprites.

The sun and moon are **point/particle billboards** (not dome meshes). Each frame, when the map clock
advances, a tick updates the sun billboard position, then the moon billboard position, then the
star/cloud domes, and only then the directional/ambient **light colour** (the §10.6 colour-only path).

### D.1 Time-of-day orbit angle (CONFIRMED)

Both billboards share one orbit angle derived from the time-of-day (the current second-of-day, range
`0 .. 86399`):

```
angle_deg = (time_of_day / 86400) * 360.0      # fraction-of-day → degrees, 0..360 over one day
angle_rad = angle_deg * DEG2RAD                 # DEG2RAD = pi/180 ≈ 0.0174533
```

- `DEG2RAD` is `pi/180 ≈ 0.0174533` — **CONFIRMED** (read directly from the client's degree→radian
  constant).
- `angle_deg` sweeps a full `0..360°` over one simulated day (the same 86400 s day length as the
  48-slot colour cycle, Section C). **CONFIRMED.**

### D.2 Billboard position (sun vs moon, mirrored) — REFINED (CYCLE 7)

> **CYCLE 7 refinement.** A closer static re-walk of the sun-orbit update on build `263bd994` shows the
> orbit is driven by a **natural-log curve of a time-of-day angle**, NOT a plain sine. The earlier
> `sin`-based reading is superseded for the per-axis math; what survives unchanged is the **±3200
> scaling and the sun/moon X-sign opposition** (the sun and moon ride opposite sides of the sky).

The billboard position is computed from a finer time-of-day angle than the 86400 s second-of-day used
for colour: a **millisecond-resolution** day angle, scaled by a day-speed factor:

```
dayAngle = time_ms × 360.0 × 0.00001157407405116828        # constant ≈ 360 / 86,400,000 = one revolution per 24 h (in ms)
dayAngle = dayAngle × day_speed_factor                      # per-area / global day-speed scalar
```

The position is then a **log-curve of `dayAngle` scaled by ±3200 world units**; the **sun uses −3200**
and the **moon uses +3200**:

| Axis | Sun billboard | Moon billboard | Confidence |
|------|---------------|----------------|------------|
| X (horizontal) | `log_curve(dayAngle) × −3200.0` | `log_curve(dayAngle) × +3200.0` | **HIGH** (the ±3200 scale and the sun/moon −/+ sign opposition) |
| vertical arc | `log_curve(dayAngle) × −3200.0`, further scaled by precomputed **log-trig seeds** | the same curve scaled by `+3200.0` | **MED** (an asymmetric arc, not a clean semicircle) |

- The horizontal term `±3200 · log_curve(dayAngle)` is **HIGH**: the sun and moon trace **opposite**
  horizontal arcs (sun negative, moon positive).
- The **vertical arc** uses the same `−3200.0`-scaled log curve, further multiplied by a pair of
  **precomputed log-trig seeds**; those seeds are initialised **once** from `day_speed_factor × 45.0`
  (the `45.0` is a fixed tilt-angle seed). So the path is an **asymmetric arc, not a perfect
  semicircle**. The per-axis exact math is **MED** (it may be a cheap developer shortcut, not true
  astronomy).
- **Constants (HIGH, static immediates):** orbit radius/scale **±3200.0**; day-angle constant
  **0.00001157407405116828** (≈ 360 / 86 400 000); seed tilt angle **45.0**.
- The static default orientation the billboard is seeded with is overwritten by this per-frame orbit
  update each tick.

#### D.2.1 The sun position doubles as the directional-light direction (CYCLE 7, HIGH)

After the sun world position vector is computed, it is **negated** and stored in **two** places at once:

1. the **directional shading-light direction** (on the light object, member offset **+184**), and
2. the scene's **sun-direction global triple** (three consecutive floats).

So the **sun billboard position IS the directional-light direction, negated** — they are not
independent. This is a *deliberate* re-confirmation refining the earlier §D / §10.6 note: the visible
billboard sprite and the shading light track the same orbit vector (one negated from the other), rather
than the light being a fixed `(-7, 7, 20)` constant. The fixed `(-7, 7, 20)` value documented in
`environment_bins.md §10.6` is the **synth fallback** used only when `light%d.bin` is absent and the
sun-orbit path is not driving the light; when the sky/sun subsystem is live, the orbit vector above
drives the light direction. (HIGH for the coupling and the negate; the precedence between the
orbit-driven direction and the per-area `light%d.bin` direction is **MED** — sample/runtime-pending.)

> **Porting guidance.** A faithful port reproduces the `±3200 · log_curve(dayAngle)` arc with the
> sun/moon sign opposition, drives the **directional light direction from the negated sun position**,
> and may treat the exact vertical-arc seeding as a tunable (a smooth asymmetric arc is acceptable
> given the MED tag). The moon rides the opposite side on the same curve.

### D.3 Moon phase — 30-day, 15-texture lunar cycle (HIGH)

The moon billboard texture is selected from a **day counter** (a separate value from the time-of-day
angle):

```
phase_index = floor((day_counter mod 30) / 2)   # integer 0..14
moon_texture = "data/sky/texture/moon{phase_index}.dds"
```

- This is a **30-day lunar cycle producing 15 distinct phase textures** (`moon0.dds .. moon14.dds`);
  two consecutive days share a phase texture. **HIGH.**
- The phase texture is **reloaded once per night, near the middle of the moon's arc** (when the orbit
  angle has passed the half-way point and the day index has changed), so the swap happens while the
  moon is up rather than producing a visible texture pop. **HIGH** (gate), MED (intent).

### D.4 Lens flare (screen-space, anchored to the sun) — RESOLVED (CYCLE 7)

Lens flare is a set of **screen-space layered sprites** ("ghosts") anchored to the sun's projected
screen position. The flare draw is a singleton call **tail-called from the transparent/particle render
pass**, so the flare composites after the world is drawn. CYCLE 7 pinned the full sun → screen → anchor
transform (previously UNRESOLVED) and the config-file layout.

#### D.4.1 World → screen projection of the sun (transform SHAPE: HIGH)

The flare draw projects the sun world position through the current view-projection matrix and maps it
to screen pixels. Algorithm in neutral terms:

1. **Fetch sun world position** `S = (Sx, Sy, Sz)` from the sun object; store it on the flare object at
   offsets **+68 / +72 / +76**.
2. **Horizon cull:** if `Sy < 0.0` (sun below the world-up axis), **skip the flare entirely**.
3. **Fetch the current view-projection matrix** `M` — 16 floats, **row-major** storage, with the
   translation in `m[12..14]`.
4. **Transform** `S` by `M` to clip space via a **row-vector × 4×4 affine multiply**. Using
   matrix-index notation:
   - `clip.x = m[0]·Sx + m[4]·Sy + m[8]·Sz  + m[12]`
   - `clip.y = m[1]·Sx + m[5]·Sy + m[9]·Sz  + m[13]`
   - `clip.w = m[2]·Sx + m[6]·Sy + m[10]·Sz + m[14]`
   *(this engine uses the **3rd matrix row** as the `w`/depth term, not the 4th.)*
5. **Clip cull:** if `clip.w >= 0.0`, **skip** — the engine's visible-side test is `clip.w < 0.0`.
6. **Perspective divide + viewport map.** Note the **X axis divides by `−w`** (matching this engine's
   X-negation convention), while **Y divides by `+w`**:
   - `screen_x = (clip.x / −clip.w) · scaleX + biasX`   (scaleX = flare+48, biasX = flare+56)
   - `screen_y = (clip.y /  clip.w) · scaleY + biasY`   (scaleY = flare+52, biasY = flare+60)
   where `scaleX / scaleY` are the **half-viewport extents** and `biasX / biasY` are the
   **viewport-centre pixel coordinates**.
7. Hand `(screen_x, screen_y)` plus the world position `S` to the flare ghost-chain draw.

> **Confidence:** the transform **shape** is HIGH (the row-vector multiply, the 3rd-row-as-`w` term,
> the `Sy < 0` horizon cull, the `clip.w < 0` visible test, the X-by-`−w` / Y-by-`+w` divide). The
> **exact `scaleX/Y` and `biasX/Y` values are DBG-pending** — they come from the runtime viewport
> (half-extents and centre in pixels), not from a static immediate.

#### D.4.2 Flare ghost-chain draw (HIGH)

Given the sun screen anchor `(screen_x, screen_y)` and world position `S`:

- **Brightness attenuation by screen-edge distance.** Compute the clamped screen distance `dist` of the
  sun anchor from the viewport edges, then:
  ```
  brightness = 1.0 − (1.0 / INTENSITY_BORDER) × dist
  ```
  If `brightness ≤ 0` the whole flare is **skipped**. (`1.0 / INTENSITY_BORDER` is precomputed at
  config-load time — see §D.4.3.)
- **Terrain occlusion:** the terrain manager is queried with the **sun world position** `S`; if the sun
  is **occluded by terrain**, the flare is suppressed.
- **Ghost chain placement.** The flare sprites are spaced along the line **from the sun screen-anchor
  toward the viewport centre**: the direction is `dir = (centre − anchor)` where `centre = (biasX,
  biasY)`. Each flare sprite `i` is placed at:
  ```
  position_i = anchor + POSITION[i] × dir
  ```
  and drawn with its own `RADIUS[i]` and `TEXTURE_ID[i]`. So `POSITION[i]` is a **fraction along the
  anchor → centre line** (0 = at the sun anchor, 1 = at screen centre), not a world coordinate.

#### D.4.3 Lens-flare config file `data/sky/lensflare.txt` (HIGH)

A key/value text script, parsed once at flare load. Header keys followed by a packed per-spot record
array:

| Field | Type | Meaning | Confidence |
|---|---|---|---|
| `SPOT_COUNT` | int | number of flare ghost sprites (**cap 16**) | HIGH |
| `TEXTURE_COUNT` | int | number of flare textures (**cap 16**) | HIGH |
| `INTENSITY_BORDER` | f32 | fade border; the reciprocal `1.0 / INTENSITY_BORDER` is **cached at load** (used in the §D.4.2 brightness formula) | HIGH |

Then **`SPOT_COUNT` per-spot records of 20 bytes each**:

| Sub-offset | Size | Type | Field | Notes | Confidence |
|-----------:|-----:|------|-------|-------|------------|
| +0x04 | 4 | int | `TEXTURE_ID` | 1-based flare-texture index → `data/sky/texture/lensflare<n>.dds` | HIGH |
| — | 4 | f32 | `RADIUS` | sprite radius `RADIUS[i]` | HIGH |
| — | 4 | f32 | `POSITION` | fraction along the anchor → centre line `POSITION[i]` | HIGH |
| — | 4 | u32 | `colour` | per-spot tint (ARGB) | HIGH |

- **Record stride: 20 bytes** (the parser allocates `20 × SPOT_COUNT` for the spot array). `TEXTURE_ID`
  sits at sub-offset `+0x04`; the remaining `RADIUS` / `POSITION` / `colour` fields fill the record (the
  field ordering after `+0x04` is HIGH; the precise sub-offset of each is parser-derived).
- **Flare textures:** `data/sky/texture/lensflare<n>.dds`, **1-based** index.

#### D.4.4 Gating

- The flare **enable flag is set per map-option during area load** (a per-flare boolean, set to 0/1 from
  the area's map-option). Lens flare renders only when this flag is set **and** the global sky-detail
  tier permits it (the highest detail-off tier suppresses it). See `environment_bins.md §1.1`
  (`map_option` `LENSFLARE` field, offset 0x08).
- Combined with §D.4.1–§D.4.2: the flare also requires the sun above the horizon (`Sy ≥ 0`), on the
  visible side (`clip.w < 0`), inside the screen-edge brightness band (`brightness > 0`), and not
  terrain-occluded.

### D.5 Sun billboard texture + sprite sizes (CYCLE 7)

The sun and moon are particle billboards with fixed sprite sizes (a sprite radius / billboard-size
scalar each); these are **sprite sizes, not dome geometry**. CYCLE 7 pinned the sun billboard's texture
and size constants:

| Asset / constant | Value | Notes | Confidence |
|---|---|---|---|
| Sun billboard texture | `data/sky/texture/sun.dds` | the sun sprite face | HIGH |
| Sun billboard size | `2048.0` | billboard-size scalar (world units) | HIGH (immediate) |
| Sun particle buffer extents | `4096.0 × 512.0` | the particle/billboard buffer extents the sun draws into | HIGH (immediate) |

The moon billboard texture is the phase-selected `data/sky/texture/moon<n>.dds` (§D.3). The numeric sun
sizes above are static immediates; a port may keep them or size the sprites to taste, but the texture
paths are load-bearing.

---

## Known unknowns

1. **`sky%d.box` does not exist in the production VFS (CONFIRMED-ABSENT).** The entire §A layout is
   **UNVERIFIED** and unverifiable without a sample. Because the skybox feature was never shipped
   (no `.box` asset in any of the 43,347 VFS entries; `SKYBOX` flag 0 in every area), the position(12)
   + UV(8) vs. position(12) + colour(4) + UV(4) vertex ambiguity, the section interleave, and every
   other §A field can be neither confirmed nor denied. A synthetic sky-dome is the correct
   engineering path (see §A status block).
2. **Fog source-band channel grouping** in the 0.75/0.25 blend (MED) — which bytes of the 192-byte
   sky-colour-table slot are the "high" and "low" source bands.
3. **Cloud `cloud_id_table` row count** — the 7-wide stride is HIGH; the "10 rows / variants" reading
   is MED.
4. **Star / cloud grid factoring** at the parser level (the BGRX-per-instance vs. keyframe grouping)
   — MED; defer to `environment_bins.md` sample-verified sizes.
5. **Sun/moon billboard vertical-arc math (§D.2, REFINED CYCLE 7)** — the orbit is a **natural-log
   curve** of a millisecond day angle (`dayAngle = time_ms × 360 × 0.00001157407405116828 ×
   day_speed`), scaled by **±3200.0** with the sun/moon X-sign opposition (HIGH). The vertical arc uses
   the same `−3200.0` curve further scaled by precomputed log-trig seeds (seeds from `day_speed × 45.0`)
   — an asymmetric arc, MED. A port may treat the vertical arc as a tunable (smooth asymmetric arc
   acceptable). (Supersedes the earlier `sin`-based reading.)
6. **Sun-position ↔ directional-light precedence (§D.2.1)** — the negated sun orbit vector is stored
   both as the directional-light direction and the sun-direction global (coupling + negate are HIGH);
   whether the orbit-driven direction or a per-area `light%d.bin` direction wins when both are present
   is **MED — sample/runtime-pending**.
7. **Lens-flare viewport scale/bias (§D.4.1)** — the sun → screen transform **shape** is now pinned
   (HIGH): row-vector × 4×4 multiply, 3rd-row `w` term, `Sy < 0` horizon cull, `clip.w < 0` visible
   test, X-by-`−w` / Y-by-`+w` divide. The exact `scaleX/Y` (half-viewport extents) and `biasX/Y`
   (viewport-centre pixels) come from the runtime viewport — **DBG-pending** (no static immediate).
8. **`lensflare.txt` per-spot sub-field offsets (§D.4.3)** — record stride 20 bytes and `TEXTURE_ID` at
   `+0x04` are HIGH; the precise sub-offsets of `RADIUS` / `POSITION` / `colour` within the record are
   parser-derived (a real `lensflare.txt` sample would pin them).
9. **Moon-phase day-counter source (§D.3)** — confirmed a day index distinct from the time-of-day
   angle, and the `floor((day mod 30)/2)` 15-phase scheme is HIGH; the exact day-counter derivation
   is not fully pinned.

## Cross-references

- **Authoritative `.bin` byte tables:** `Docs/RE/formats/environment_bins.md`
  (`fog`, `material`, `stardome`, `clouddome`, `cloud_cycle`, `map_option`, weather).
- **Runtime model:** `Docs/RE/specs/environment.md` (load order, day-cycle math, colour conversion,
  water = render-pass concern).
- **Container:** `Docs/RE/formats/pak.md` (the `.box` open uses the archive by-name lookup).
- **Textures:** `Docs/RE/formats/texture.md` (the `.dds` payloads the names resolve to).
- **Glossary:** see `Docs/RE/names.yaml` (`SkySystem_Init`, `SkyBox_Load`, `Fog_Init`,
  `CloudDome_Init`, `StarDome_Init`, `SkyColorTable_GetSingleton`, `SkyColor_SampleByTime`;
  Section D adds the sun/moon billboard-orbit, moon-phase-load, and lens-flare-host subsystems.
  CYCLE 7 adds the sun-orbit update, the lens-flare project-and-draw host, the flare ghost-chain
  draw, the lens-flare config reader, and the negate-and-store light coupling — flag these canonical
  names for the glossary, do not edit it here).
- **Provenance:** see `Docs/RE/journal.md` (add an entry for this spec). **CAMPAIGN 10 Block D6
  two-witness re-verification (build `263bd994`)** RE-CONFIRMED: `.box` absent (0 of 43,347 VFS
  entries); the fog colour-derivation loop (48 iterations, 192-byte per-slot stride, 0.75/0.25 blend);
  `cloud_cycle%d.bin` = 70 bytes (10 × 7). One wording correction (C5): fog files are **always 204
  bytes on disk**; the "12-byte" form is the loader's read volume when `data_load_flag = 0`, not an
  on-disk size — and `data_load_flag = 1` is now witnessed in real data (`fog11.bin`). The day-cycle
  (§C) and sun/moon orbit (§D) facts were not re-traced this lane and are carried [confirmed] from the
  prior IDA reading.
  **CYCLE 7 (2026-06-20, build `263bd994`, static-only)** added the sun → screen → lens-flare anchor
  transform (§D.4.1, transform shape HIGH; viewport scale/bias DBG-pending), the flare ghost-chain
  draw + terrain-occlusion + screen-edge brightness (§D.4.2), the `data/sky/lensflare.txt` config
  layout (§D.4.3, 20-byte spot records, caps 16), the sun billboard texture/size constants
  (§D.5: `sun.dds`, size 2048.0, buffer 4096.0×512.0), the sky data-file family enumeration (§B.1a),
  the build order sun→star→cloud→moon→material→light→fog (§B.1), and refined the sun/moon orbit (§D.2)
  to the natural-log-curve model with the negate-and-store directional-light coupling (§D.2.1).
