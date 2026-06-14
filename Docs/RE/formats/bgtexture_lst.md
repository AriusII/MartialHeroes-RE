# Format: .lst (bgtexture.lst — binary terrain/effect texture index)

> Clean-room spec. Neutral description only — NO sample bytes, NO decompiler pseudo-code.
> Consumed by Assets.Parsers. Every offset an engineer cites must reference this file.
>
> status: sample_verified
> sample_verified: true — both shipped `bgtexture.lst` instances verify the size formula
>                  exactly; the per-record kind byte and the relpath layout are cross-checked
>                  against the matching `bgtexture.txt` text mirror for the head records.

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
| 0      | 4    | u32 LE | `record_count` | Number of records that follow; `map000`=1222, `effect`=1108 | CONFIRMED  |

No further file-level header fields. The record array begins immediately at offset 4.

---

## Record / body layout

A flat array of fixed-size records, concatenated with no inter-record padding. Each record
inlines its own kind byte and relpath; there is no separate parallel array.

| Offset (in record) | Size | Type      | Field      | Notes                                                        | Confidence |
|-------------------:|-----:|-----------|------------|-------------------------------------------------------------|------------|
| +0                 | 1    | u8        | `kind`     | Observed `0x01` in every record of both files. Same value as `bgtexture.txt` column 1. Semantic UNVERIFIED — likely an animated/static flag. | CONFIRMED (value `0x01`); UNVERIFIED (semantic) |
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

---

## Text mirror — bgtexture.txt (reference only, not the parser input)

A tab-delimited (`\t`) CP949/ASCII text file with `\r\n` line endings and **no header row**
(row 0 is data). It carries exactly the same rows as the `.lst` in a different representation.

| Column | Type        | Field                         | Maps to `.lst`                          |
|-------:|-------------|-------------------------------|-----------------------------------------|
| col0   | u32 decimal | record index (0-based)        | the implicit record position in the `.lst` (the binary has no id field; records are addressed by position) |
| col1   | u8 decimal  | kind flag                     | the `kind` byte at record +0            |
| col2   | string      | relpath (no `.dds` extension) | the `rel_path` field at record +1       |

Row count equals the `.lst` `record_count` (1222 for `map000`, 1108 for `effect`). The `.txt`
companion sizes are 34,479 B (`map000`) and 30,499 B (`effect`).

---

## Cross-file join (texture resolution chain)

The record index is the global pool slot that terrain and building geometry reference:

```
.ted TextureIndexGrid[patch]  (1-based byte)
  -> .map  TERRAIN{}/BUILDING{} TEXTURES[byte-1].intTexId   (pool slot, 1-based in .map)
  -> bgtexture.lst[ intTexId - 1 ]  rel_path                (this format; 0-based record index)
  -> data/map000/texture/<rel_path>.dds                      (terrain instance)
     data/effect/texture/<rel_path>.dds                      (effect instance)
```

- The `.dds` extension and the `data/map000/texture/` (or `data/effect/texture/`) prefix are
  added at runtime; they are **not** stored in the record.
- The 1-based `.map` `intTexId` minus 1 gives the 0-based `.lst` record index.

---

## Enumerations / flags

- **`kind` (record +0):** observed `0x01` in every record of both shipped files. Candidate
  semantic: animated vs. static texture flag (the `terrain.md` text-companion notes treat
  column 1 as `1` = animated). UNVERIFIED — the value never varied in the sample.

---

## Known unknowns

- Semantic meaning of the `kind` byte (only the constant value `0x01` was observed; the
  animated/static interpretation is inferred, not confirmed).
- Whether any record in the unwalked middle/tail of either file carries a `kind` other than
  `0x01` (full-file consistency is inferred from the exact size match; only the head records
  were byte-checked against the `.txt`).
- Whether the relpath buffer is exactly 47 bytes or a smaller logical cap zero-padded into 47
  (the stride is fixed at 48 regardless; a parser should read up to the first NUL within the
  47-byte field).

---

## Cross-references

- Related formats: `terrain.md` (the `.ted` → `.map` → pool → `.dds` chain; this spec replaces
  the inferred 76-byte record layout in `terrain.md §4.1`), `texture.md` (the `.dds` payload).
- Glossary: see `Docs/RE/names.yaml` (proposed: `BgtextureLst`, `BgtextureLstRecord.kind`,
  `BgtextureLstRecord.relPath`, `BgtextureTxt`).
- Provenance: see `Docs/RE/journal.md` (add an entry for this spec).
