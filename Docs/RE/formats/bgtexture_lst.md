# Format: .lst (bgtexture.lst — binary terrain/effect texture index)

> Clean-room spec. Neutral description only — NO sample bytes, NO decompiler pseudo-code.
> Consumed by Assets.Parsers. Every offset an engineer cites must reference this file.
>
> verification: sample-verified — both shipped `bgtexture.lst` instances verify the size formula
>               exactly, AND the loader control flow (count validation, 48-byte record stride,
>               kind byte at +0, 47-byte relpath at +1, path construction) was independently
>               re-confirmed in the IDB.
> ida_reverified: 2026-06-16
> ida_anchor: 263bd994
> evidence: [static-ida, vfs-sample]
> conflicts: none unresolved.
> sample_verified: true — both shipped `bgtexture.lst` instances verify the size formula
>                  exactly; the per-record kind byte and the relpath layout are cross-checked
>                  against the matching `bgtexture.txt` text mirror. A full-file scan of every
>                  record in both instances (not just the head window) corrected the earlier
>                  "kind is always 0x01" reading — see §Enumerations.

---

## Scope and the authoritative-source rule

`bgtexture.lst` is the **binary** background-texture index that the terrain/effect loader reads
at startup to populate the global texture pool. A `bgtexture.txt` (a 3-column tab-delimited text
file) sits beside every `.lst` and carries the **same rows in a human-readable mirror**.

**The `.lst` BINARY is the file the loader consumes. Terrain consumers must follow the `.lst`,
not the `.txt`.** The `.txt` is a design-time / debug export, useful only for human lookup. Where
this spec and the `.txt` ever disagree, the `.lst` wins.

This spec **supersedes** the earlier inferred record layout in `terrain.md §4.1` (which estimated
a 76-byte "GHTex" record preceded by a separate type-byte array). That estimate was inferred from
loader logic and never sample-verified. The campaign-4 harness observation below is size-exact on
both shipped files and proves the real on-disk layout is a single **flat 48-byte record array**
(one inline kind byte + one inline 47-byte relpath per record). There is no separate type-byte
array and no 76-byte record.

---

## Identification

- **Extension:** `.lst` (this spec covers the `bgtexture.lst` files specifically; other `.lst`
  files in the VFS — e.g. the per-area cell manifest `d<area>.lst` — are a different format,
  documented in `terrain.md §1.2`).
- **Found in:** the VFS (`data.inf` + `data/data.vfs`). Two known instances:
  - `data/map000/texture/bgtexture.lst` — terrain background textures (global for all areas;
    textures live under `map000`).
  - `data/effect/texture/bgtexture.lst` — effect textures.
- **Magic / signature:** none. No file-level magic bytes or version header.
- **Endianness:** little-endian (the only multi-byte field is the `u32` count header).
- **String encoding:** the relpath field is printable ASCII in all observed records (treat as
  CP949 / EUC-KR for safety, since all other game text is CP949); null-terminated, zero-padded.

---

## Header layout

| Offset | Size | Type   | Field          | Notes / observed values                              | Confidence |
|-------:|-----:|--------|----------------|------------------------------------------------------|------------|
| 0      | 4    | u32 LE | `record_count` | Number of records that follow; `map000`=1222, `effect`=1108 | CONFIRMED + SAMPLE-VERIFIED |

No further file-level header fields. The record array begins immediately at offset 4.

**Count validation (loader-enforced).** The loader range-checks `record_count` before allocating:
it **rejects a count of 0** and **rejects a count `>= 2000` (0x7D0)**. A count outside the half-open
range `[1, 2000)` aborts the load with an error rather than allocating. Both shipped instances
(1222, 1108) fall inside the valid range. **CODE-CONFIRMED** (the bounds are read directly from the
loader's two comparison guards); the in-range values are **SAMPLE-VERIFIED**. A parser that mirrors
the client should apply the same `1 <= record_count < 2000` guard. (Contrast the per-area
`d<NNN>.lst` manifest loader, which performs **no** count validation — see
`area_inventory.md §1.2`.)

---

## Record / body layout

A flat array of fixed-size records, concatenated with no inter-record padding. Each record
inlines its own kind byte and relpath; there is no separate parallel array.

| Offset (in record) | Size | Type      | Field      | Notes                                                        | Confidence |
|-------------------:|-----:|-----------|------------|-------------------------------------------------------------|------------|
| +0                 | 1    | u8        | `kind`     | Material render-mode tag. Same value as `bgtexture.txt` column 1. NOT constant: across a full scan of every record in both files, `0x01` is the majority but at least six distinct values occur. Value→mode mapping in §Enumerations. | CONFIRMED (non-constant, 6 values); HIGH (render-mode semantic) |
| +1                 | 47   | char[47]  | `rel_path` | Texture path **relative to** the file's own texture directory, **without** the `.dds` extension. Null-terminated, zero-padded to the full 47 bytes. | CONFIRMED |

- **Record stride:** 48 bytes.
- **Record count source:** the `u32` header field at offset 0. It also equals
  `(file_size - 4) / 48` (header + flat array). Both expressions agree on both shipped files.

**Size formula (CONFIRMED on both instances):**

```
file_size = 4 + record_count * 48
```

| Instance                              | record_count | Computed size           | Status   |
|---------------------------------------|-------------:|-------------------------|----------|
| `data/map000/texture/bgtexture.lst`   | 1222         | 4 + 1222 × 48 = 58,660  | exact    |
| `data/effect/texture/bgtexture.lst`   | 1108         | 4 + 1108 × 48 = 53,188  | exact    |

**I/O pattern (informational).** The client reads the body as a **single bulk read** of
`record_count × 48` bytes into a temporary buffer, then walks that buffer in a per-record loop
(stepping 48 bytes per iteration) to populate its in-memory pool — it does **not** issue one read
per record. This is an I/O optimization detail, not a format fact; a parser may read the whole
body in one shot or stream record-by-record with identical results. **CODE-CONFIRMED.**

In memory each record expands to a larger fixed-size pool entry (a 76-byte in-memory stride, distinct
from the 48-byte on-disk stride). The 76-byte figure is an in-memory layout detail and does **not**
affect on-disk parsing; the on-disk record is always 48 bytes. This is noted so an engineer does not
confuse the in-memory pool-entry size with the file record size (see also `terrain.md §4.1`, whose
earlier "76-byte record" estimate referred to this in-memory entry, not the on-disk record).

---

## Text mirror — bgtexture.txt (reference only, not the parser input)

A tab-delimited (`\t`) CP949/ASCII text file with `\r\n` line endings and **no header row**
(row 0 is data). It carries exactly the same rows as the `.lst` in a different representation.

| Column | Type        | Field                         | Maps to `.lst`                          |
|-------:|-------------|-------------------------------|-----------------------------------------|
| col0   | u32 decimal | record index (0-based)        | the implicit record position in the `.lst` (the binary has no id field; records are addressed by position) |
| col1   | u8 decimal  | kind / render-mode tag        | the `kind` byte at record +0 (see §Enumerations — NOT a simple 0/1 flag) |
| col2   | string      | relpath (no `.dds` extension) | the `rel_path` field at record +1       |

Row count equals the `.lst` `record_count` (1222 for `map000`, 1108 for `effect`). The `.txt`
companion sizes are 34,479 B (`map000`) and 30,499 B (`effect`).

---

## Cross-file join (texture resolution chain)

The record index is the global pool slot that terrain and building geometry reference:

```
.ted TextureIndexGrid[patch]  (1-based byte, clamped [1,count])
  -> .map  TERRAIN{}/BUILDING{} TEXTURES[byte-1].intTexId   (the ONLY -1 in the chain: on the .ted byte)
  -> bgtexture.lst[ intTexId ]  rel_path                    (intTexId IS the 0-based record index, used DIRECTLY — NO -1)
  -> data/map000/texture/<rel_path>.dds                      (terrain instance)
     data/effect/texture/<rel_path>.dds                      (effect instance)
```

- The `.dds` extension and the `data/map000/texture/` (or `data/effect/texture/`) prefix are
  added at runtime; they are **not** stored in the record.
- **IDA-corrected (263bd994):** the `.map` `intTexId` **IS** the 0-based `.lst` record index, used
  **directly** — the pool accessor reads `pool[0] + stride*intTexId` with **NO** subtraction (IDA
  `0x445833` / `0x44a46d`; the raw `intTexId` is stored into `perCellTexList` at `0x44b267`). The **only**
  `-1` in the whole chain is on the `.ted` per-cell byte: the byte is clamped to `[1, count]` (both `<1`
  and `>count` → 1) and then indexes the cell texture list as `perCellTexList[byte-1]` (IDA `0x44b296`).
  The earlier "1-based `intTexId` minus 1" reading was **WRONG** and is **REFUTED** — it disagreed with
  `terrain.md §3.5`/`§5.6` (which were correct); this off-by-one rendered `intTexId=0` cells as missing
  textures (black world) until corrected.

---

## Enumerations / flags

**`kind` (record +0) — material render-mode tag.** A full scan of every record in both shipped
instances (2,330 records total) found the byte is **not constant**: `0x01` is the majority, but
227 records carry a value other than `0x01`. At least six distinct values occur. The earlier
"animated vs static boolean" reading is retired — a single bit would not produce a value as high
as `0x14`. Each value correlates with a recognisable family of texture relpaths, so the byte is a
**material / shader render-mode selector**, not a boolean. The default mode is `0x01` (plain
static ground).

| kind | Dec | Proposed label    | Correlated relpath family (observed)                              |
|-----:|----:|-------------------|------------------------------------------------------------------|
| 0x01 |   1 | `KIND_STATIC`     | Plain static ground tiles — stone, cliff, soil, generic terrain (the default) |
| 0x02 |   2 | `KIND_SCROLL`     | Water, lava, moss, wet surfaces — scrolling-UV / animated material |
| 0x0A |  10 | `KIND_GRASS`      | Grass tiles                                                       |
| 0x0B |  11 | `KIND_PLANT`      | Herb / plant tiles                                               |
| 0x0C |  12 | `KIND_TREE_BARK`  | Tree-bark / trunk patch                                          |
| 0x14 |  20 | `KIND_FOLIAGE`    | Dense tree foliage, branches, canopy                            |

- **Runtime pool dispatch (binary, CODE-CONFIRMED).** At load time the `kind` byte gates a binary
  branch that decides which of **two** texture-pool initialization paths each record's in-memory
  entry is wired to: `kind == 0x01` selects one pool; **any** `kind != 0x01` selects the other. The
  byte therefore does more than label a render mode — it **selects a texture-pool init path** at
  parse time. The loader's branch only distinguishes "is it 1 or not"; the finer 6-value
  enumeration above is the observed *data* spread, while the runtime *code* split is just the
  `==1` / `!=1` dichotomy. An engineer wiring the kind byte should treat `0x01` as the default
  (static-ground) pool and route every other value to the alternate pool, then layer the
  render-mode bucket on top.
- The render-mode **categories** are HIGH confidence (the value→relpath-family correlation holds
  across the full scan of both files). The **exact rendering behaviour** behind each mode
  (scroll speed, sway parameters, alpha-test vs. billboard) is INFERRED from the relpath families
  and the value spread — it is NOT confirmed against the engine's shader/material table. An
  engineer must not treat the proposed labels as confirmed shader semantics; treat them as a
  render-mode bucket and tune behaviour from the family.
- The two instances have overlapping but not identical relpath populations, so the per-file count
  of non-`0x01` records differs (105 in `effect`, 227 in `map000`). The value set is shared.
- The same correction applies to **`bgtexture.txt` col1**: it is the same render-mode tag, not a
  `0`/`1` animated flag. Do not read col1 as a boolean.

---

## Known unknowns

- The exact engine rendering behaviour selected by each non-`0x01` `kind` value (scroll vector,
  vertex-sway parameters, alpha-test threshold, billboarding) — the value→render-mode buckets are
  HIGH, but the per-bucket behaviour is INFERRED from relpath families, not read from the engine's
  material table.
- Whether `kind` values beyond the six observed (`0x01`, `0x02`, `0x0A`, `0x0B`, `0x0C`, `0x14`)
  exist in other VFS revisions; only these two shipped instances were scanned.
- Whether the relpath buffer is exactly 47 bytes or a smaller logical cap zero-padded into 47
  (the stride is fixed at 48 regardless; a parser should read up to the first NUL within the
  47-byte field).

---

## Cross-references

- Related formats: `terrain.md` (the `.ted` → `.map` → pool → `.dds` chain; this spec replaces
  the inferred 76-byte record layout in `terrain.md §4.1`), `texture.md` (the `.dds` payload).
- Glossary: see `Docs/RE/names.yaml` (proposed: `BgtextureLst`, `BgtextureLstRecord.kind`,
  `BgtextureLstRecord.relPath`, `BgtextureTxt`; render-mode labels `KIND_STATIC`, `KIND_SCROLL`,
  `KIND_GRASS`, `KIND_PLANT`, `KIND_TREE_BARK`, `KIND_FOLIAGE`).
- Provenance: see `Docs/RE/journal.md` (add an entry for this spec).
- **Campaign 10 re-verification (2026-06-16, IDB anchor 263bd994):** re-confirmed against both the
  IDB loader control flow and the two real VFS samples. No layout drift. Enrichments folded in this
  pass: the loader's explicit count-validation guard (**reject 0**, **reject `>= 2000`**); the
  runtime **two-pool dispatch** on the kind byte (`== 0x01` vs `!= 0x01`); the **single bulk read**
  I/O pattern; and the clarification that the 76-byte figure is the in-memory pool-entry stride, not
  the 48-byte on-disk record. The `kind` byte's six-value enumeration and per-mode rendering
  behaviour remain as previously tiered (enumeration sample-verified from the prior full-file scan;
  per-bucket behaviour INFERRED). No addresses, decompiler output, or sample bytes crossed the
  firewall.
