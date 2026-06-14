# Format: sky data files  (sky-dome geometry `.box` + sky `.bin` family — parser view)

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
> order, in case a `.box` asset ever surfaces from another archive.

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
5. **Moon / particle dome** init (uses `moon%d.dds`).
6. **Sky-box** sub-object — conditionally loads `sky%d.box` (Section A) when the skybox gate is set.
   In the shipping VFS this branch is never taken: the skybox gate is always 0 and no `.box` asset
   exists (see §A).
7. Bind the **day/night time manager** to the dome.
8. **Fog** init — loads `fog%d.bin`.

The environment object **is** the sky-colour-table singleton (it is not a separate "environment
manager" allocation); the sub-domes (star, cloud, sky-box, moon) are pointer members hung off it,
and the fog parameters and colour table are stored inline on it.

### B.2 `fog%d.bin` — parser view

`fog%d.bin` is read as: two leading 4-byte values (`start_dist`, `end_dist`), then a 4-byte
`data_load_flag`, then — **only when `data_load_flag` is non-zero** — a **192-byte (0xC0) colour
table**. So the file is **either 12 bytes** (flag = 0, colour derived at runtime) **or 204 bytes**
(flag ≠ 0, explicit 192-byte colour table). The canonical byte table (the 48-entry 4-byte BGRA
layout of the 192-byte block, and the confirmed `start_dist`/`end_dist` float typing) is in
`environment_bins.md §2`.

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

## Cross-references

- **Authoritative `.bin` byte tables:** `Docs/RE/formats/environment_bins.md`
  (`fog`, `material`, `stardome`, `clouddome`, `cloud_cycle`, `map_option`, weather).
- **Runtime model:** `Docs/RE/specs/environment.md` (load order, day-cycle math, colour conversion,
  water = render-pass concern).
- **Container:** `Docs/RE/formats/pak.md` (the `.box` open uses the archive by-name lookup).
- **Textures:** `Docs/RE/formats/texture.md` (the `.dds` payloads the names resolve to).
- **Glossary:** see `Docs/RE/names.yaml` (`SkySystem_Init`, `SkyBox_Load`, `Fog_Init`,
  `CloudDome_Init`, `StarDome_Init`, `SkyColorTable_GetSingleton`, `SkyColor_SampleByTime`).
- **Provenance:** see `Docs/RE/journal.md` (add an entry for this spec).
