# Format: .bud  (cell building blob — per-tile static-object geometry)

> Clean-room spec. Neutral description only — NO sample bytes, NO decompiler pseudo-code.
> Consumed by Assets.Parsers. Every offset an engineer cites must reference this file.
>
> **Status:** sample_verified — all CONFIRMED fields were cross-checked against three real
> .bud files (byte-exact matches, 195 bytes each). PARTIAL and UNVERIFIED fields are
> noted in-line. See "Open Questions" for remaining unknowns.

---

## Identification

- **Extension:** `.bud`
- **Found in:** `.pak` archive; logical path pattern: `map/d{map}x{tile_x}z{tile_z}.bud`
  (e.g. `d026x10023z10035.bud`)
- **Magic / signature:** none — the file starts directly with the 4-byte object count.
- **Version field:** none observed; no version discriminator in the header.
- **Endianness:** little-endian (x86 Windows client universal convention).
- **Role:** Carries all static-object (building / prop) geometry for one map cell.
  Loaded as the `DATAFILE` entry of the `BUILDING` section inside the enclosing
  scene text file (`.map`). It is not the terrain heightmap (`.ted`) or solid-collision
  blob (`.sod`); those are separate formats loaded by separate loaders.

---

## Scene-file context

A `.map` scene text file groups assets into named sections. Only the `BUILDING` section
references `.bud` files:

```
BUILDING {
    DATAFILE  path/to/cell.bud
    TEXTURES {
        <flag>  <tex_index>  "texture/path.xxx"
        ...
    }
}
```

Other sections (`TERRAIN`, `SOLID`, `FX1`–`FX7`, `UP_TERRAIN`, `EXTRA_TERRAIN`) use
their own DATAFILE formats and loaders. The `.bud` loader is invoked only from the
`BUILDING` branch.

Texture identifiers inside the `.bud` binary are integer indices that resolve against
the `TEXTURES` list declared in the same `BUILDING` section. The `.bud` file carries
no texture strings of its own.

---

## File-level header

| Offset | Size | Type   | Field          | Notes / observed values              | Confidence  |
|-------:|-----:|--------|----------------|--------------------------------------|-------------|
| 0x00   | 4    | uint32 | `object_count` | Number of MassObject records to follow. Observed: 1. No parser cap observed (only vertex count is capped per-object). | CONFIRMED |

The file header is exactly 4 bytes. There are no padding fields, no magic, and no
additional header metadata. `object_count` may be zero (empty cell), though no
zero-count sample was available for verification.

---

## Per-object layout (repeated `object_count` times)

Objects are stored back-to-back with no inter-object padding. The layout of each
object entry is:

```
[Object Header — 9 bytes]
[Vertex Array — 32 × vertex_count bytes]
[Index Header — 4 bytes]
[Index Array — 2 × index_count bytes]
```

### Object header

| Offset within object | Size | Type   | Field          | Notes / observed values                   | Confidence  |
|---------------------:|-----:|--------|----------------|-------------------------------------------|-------------|
| +0x00                | 1    | uint8  | `type_byte`    | Object sub-class. Observed value: 0 (building/static prop). Full enumeration unknown — see Open Questions. | PARTIAL |
| +0x01                | 4    | uint32 | `tex_id`       | 1-based index into the `TEXTURES` list of the enclosing `BUILDING` section. Observed value: 1. The 1-based convention is inferred from the only observed value; see Open Questions. | PARTIAL |
| +0x05                | 4    | uint32 | `vertex_count` | Number of vertices in this object's vertex array. Must be ≤ 3072 (0xC00); the loader logs an error and continues if this cap is exceeded. Observed: 5. | CONFIRMED |

### Vertex array

Immediately follows the object header at byte offset `+0x09` within the object entry.
Contains exactly `vertex_count` vertex records, each **32 bytes** (stride is fixed —
no per-file stride field exists).

#### Vertex record (32 bytes, all fields float32 little-endian)

| Byte within vertex | Size | Type    | Field      | Notes                                              | Confidence  |
|-------------------:|-----:|---------|------------|----------------------------------------------------|-------------|
| +0x00              | 4    | float32 | `pos_x`    | World-space X position. Observed range: ~24 000–37 000. | CONFIRMED |
| +0x04              | 4    | float32 | `pos_y`    | World-space Y (height / up-axis). Observed range: ~67–93 for sampled cell. | CONFIRMED |
| +0x08              | 4    | float32 | `pos_z`    | World-space Z position.                            | CONFIRMED |
| +0x0C              | 4    | float32 | `normal_x` | Surface normal X component.                        | CONFIRMED |
| +0x10              | 4    | float32 | `normal_y` | Surface normal Y component.                        | CONFIRMED |
| +0x14              | 4    | float32 | `normal_z` | Surface normal Z component.                        | CONFIRMED |
| +0x18              | 4    | float32 | `uv_u`     | Texture U coordinate. Observed: values in range 24–29 (tiled / world-scale mapping). May exceed [0,1]. | CONFIRMED |
| +0x1C              | 4    | float32 | `uv_v`     | Texture V coordinate. Same range behaviour as `uv_u`. | CONFIRMED |

Normal vectors are unit-length (magnitude 1.0) as confirmed by numerical verification
of all five normals in the three sample files. No compact (INT8 / INT16) encoding was
observed; all normals are stored as full float32 components.

UV coordinates are world-scale tiled values, not normalised [0, 1] atlas coordinates,
for the observed samples. Whether [0, 1] UV coordinates can appear in other cells is
unknown — see Open Questions.

### Index header

Immediately follows the vertex array.

| Offset relative to end of vertex array | Size | Type   | Field         | Notes                                               | Confidence  |
|----------------------------------------:|-----:|--------|---------------|-----------------------------------------------------|-------------|
| +0x00                                   | 4    | uint32 | `index_count` | Number of uint16 indices to follow. Must be a multiple of 3 (triangle list). Observed: 9 (= 3 triangles). | CONFIRMED |

### Index array

Immediately follows the index header. Contains exactly `index_count` values, each
a **uint16** little-endian. Indices are 0-based; valid range is `[0, vertex_count − 1]`.

- **Primitive topology:** triangle list. Every three consecutive indices form one
  triangle. No strip, fan, or adjacency structure is present.
- **Index type:** uint16 (2 bytes each). No 32-bit index variant was observed.

---

## File-size formula

```
total_bytes = 4                                           // file header
            + sum over all objects of:
                (1 + 4 + 4)                               // object header (type, tex_id, vertex_count)
              + (32 × vertex_count)                       // vertex array
              + 4                                         // index_count
              + (2 × index_count)                         // index array
```

The three available samples produce `total_bytes = 195`, which matches the formula
exactly for one object with `vertex_count = 5` and `index_count = 9`.

---

## Coordinate system

Positions are pre-baked into absolute world-space — no tile-relative offset is applied
by the loader at run time. The map tile naming scheme embeds integer tile coordinates
offset by 10 000 (e.g. tile X 10023 → world origin ≈ 23 × 1024 = 23 552). Building
geometry coordinates align with those ranges but are stored as final world-space floats
and require no post-load transformation.

Axis convention observed: X and Z are horizontal plane axes; Y is the vertical / height
axis.

---

## LOD / culling distances (runtime — informational, not stored in file)

The loader computes an axis-aligned bounding box (AABB) from the vertex positions after
reading each object and derives a visibility-culling distance squared based on the
object's XZ footprint extent. This is runtime state and is **not encoded in the `.bud`
file**; the information is provided here only so that implementors understand the intent
behind the `pos_x/y/z` fields.

| XZ extent threshold | Cull distance (units) | Distance² |
|---------------------|-----------------------|-----------|
| < 8                 | 300                   | 90 000    |
| < 16                | 500                   | 250 000   |
| < 32                | 1 000                 | 1 000 000 |
| < 64                | 1 500                 | 2 250 000 |
| ≥ 64                | ~1 800                | 3 240 000 |

---

## Enumerations / flags

### `type_byte` (uint8, first byte of each object header)

| Value | Meaning                          | Confidence  |
|------:|----------------------------------|-------------|
| 0     | Static building / prop (default) | PARTIAL — only value observed; inferred from `BUILDING` section context |
| 1–255 | Unknown — not seen in samples    | UNVERIFIED  |

No other type_byte values were observed across the three available samples.

---

## Known unknowns

1. **`type_byte` full enumeration.** Only value 0 has been observed. Scene sections
   `FX1`–`FX7`, `UP_TERRAIN`, and `EXTRA_TERRAIN` each call distinct loaders, so
   type_byte probably distinguishes sub-classes within the BUILDING domain. No sample
   with type_byte ≠ 0 is available.

2. **`tex_id` indexing base (0-based vs. 1-based).** The only observed value is 1.
   Treating it as 1-based is the working assumption; a sample with tex_id = 0 would
   confirm or refute this.

3. **Multi-object files.** All three samples have `object_count = 1`. The parser
   contains no object-count cap, so files with multiple objects are theoretically valid,
   but no such sample is available to verify record-boundary alignment or any trailing
   padding.

4. **`object_count = 0` (empty cell).** No sample demonstrates this; the parser
   likely handles it gracefully (allocates an empty array), but this has not been
   confirmed.

5. **Normalised UV coordinates.** Observed UV values are world-scale tiled (range
   ~24–29). Whether [0, 1] UV values can appear in other map cells is unconfirmed.

6. **No string / name fields.** Object naming, if used at all, is conveyed through the
   scene text file, not the `.bud` binary. Confirmed by exhaustive parse of all 195
   sample bytes with no unaccounted regions.

7. **Index array alignment.** The index array starts immediately after the uint32
   `index_count` field with no padding byte. This holds for the single observed sample
   (one object, even vertex count). For objects with odd vertex counts the index array
   would start at an odd byte boundary — there is no evidence of any alignment padding,
   but this has only been exercised with vertex_count = 5.

---

## Open questions for the Assets.Parsers engineer

- Should the parser enforce the `vertex_count ≤ 3072` cap (matching the legacy loader)
  or treat it as a warning only? The legacy loader logs and continues; a strict parser
  may prefer to return an error.
- Should tex_id = 0 be treated as "no texture" (null) or as an out-of-range error?
  The 1-based interpretation is assumed until a counter-example surfaces.
- Should the parser expose `type_byte` as an enum or as a raw `byte` until the full
  enumeration is confirmed?

---

## Cross-references

- **Related formats:**
  - `.ted` — terrain heightmap and normal blob for the `TERRAIN` DATAFILE; separate
    format and loader.
  - `.sod` (tentative extension) — solid-collision blob for the `SOLID` DATAFILE;
    separate loader.
  - `.pak` — archive container; see `Docs/RE/formats/pak.md`.
  - `.map` — scene text file that references `.bud` via the `BUILDING { DATAFILE … }`
    section; parsed as plain text (CP949/EUC-KR encoding for Korean path strings).
- **Glossary:** see `Docs/RE/names.yaml`
- **Provenance:** see `Docs/RE/journal.md`
