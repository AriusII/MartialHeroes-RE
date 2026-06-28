# Format: .lst (bgtexture.lst — binary terrain/effect texture index)

> Clean-room spec. Neutral description only — NO sample bytes, NO decompiler pseudo-code.
> Consumed by Assets.Parsers. Every offset an engineer cites must reference this file.
>
> verification: sample-verified — both shipped `bgtexture.lst` instances verify the size formula
>               exactly, AND the loader control flow (count validation, 48-byte record stride,
>               kind byte at +0, 47-byte relpath at +1, path construction) was independently
>               re-confirmed in the IDB.
> ida_reverified: 2026-06-24
> ida_reverified: 2026-06-27 — CYCLE 14 re-anchor (f61f66a9): confirmatory - TerrainPool_InitFromBgtextureLst cleanly relocated, loader byte-exact structure unchanged, 1 re-confirmed SAME, 0 corrected
> ida_anchor: f61f66a9ae0ec1e946105b2ecff76e8930cb1d1367df64e5688a5266f5ad9963
> evidence: [static-ida, vfs-sample]
> conflicts: none unresolved.
> sample_verified: true — both shipped `bgtexture.lst` instances verify the size formula
>                  exactly; the per-record kind byte and the relpath layout are cross-checked
>                  against the matching `bgtexture.txt` text mirror. A full-file scan of every
>                  record in both instances (not just the head window) corrected the earlier
>                  "kind is always 0x01" reading — see §Enumerations.
>
> **CORRECTED CYCLE 1 (ida_anchor 263bd994, evidence [static-ida]):** the kind byte drives a single
> `==1` (static render-object) vs `!=1` (scroll/animated render-object) dispatch at load time; the
> 6-value enum is **data-only and is never re-branched in the loader** (CONFIRMED — a full scan of the
> loader found exactly one kind-byte comparison, no per-value branch and no jump table). Every pool
> entry joins the shared texture scheduler pool regardless of kind. The §Cross-file join `-1`
> inventory is re-confirmed exactly (no drift) — and the IDA addresses that had leaked into that note
> are removed (functions are now named by role). [2026-06-19]
>
> **RE-CONFIRMED + FAMILY NOTE (2026-06-21, evidence [static-ida, vfs-sample]):** the loader
> (`record_count` at +0 with **reject 0 / reject `>= 2000`** guard; single bulk read of
> `48 × record_count`; 48-byte record = 1-byte `kind` + 47-byte relpath; the single `==1` vs `!=1`
> render-object dispatch; the `data/map000/texture/<relpath>.dds` path build) was re-read and matches
> this spec with **no layout drift, no correction needed**. Added to §Identification a `.lst`-family
> orientation note (five binary `.lst` kinds: `d<area>.lst`, the two `bgtexture.lst`, `bmplist.lst`,
> `xobj.lst`, `xeffect.lst`) and pinned that `motlist/skinlist/bindlist` are **`.txt`, not `.lst`**.
> Recorded the concrete shared-scheduler lifetime value (**180000** terrain vs **120000** bmplist) as
> informational. No addresses or decompiler output crossed the firewall.
>
> **HISTOGRAM CORRECTION (2026-06-24, evidence [vfs-sample], ida_anchor 263bd994c927c20a38624cf0ca452eaef365057fa9db1543d8f668c14a6fd8ee):**
> a full byte scan of every record across both shipped instances (2,330 records total) established the
> correct per-file non-`0x01` counts: **122** in `map000` and **105** in `effect`. The previous figure
> of **227** for `map000` (§Enumerations) was a stale mis-count — corrected in §Enumerations body and
> in the per-file count note. The kind value set `{1, 2, 10, 11, 12, 20}`, the six-entry enumeration,
> and the `0x01`-majority claim all remain correct. No layout or loader semantics changed. No addresses
> or decompiler output crossed the firewall.

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
  files in the VFS are a different format — see the **`.lst` family note** below).
- **The binary `.lst` family (orientation).** The client reads exactly **five logical kinds** of
  binary `.lst`, all sharing the same outer shape (LE, **no magic / version**, a leading `u32`
  count, then a fixed-stride record array immediately after the count). They differ only in record
  body:
  - `data/map<area>/dat/d<area>.lst` — per-area **cell manifest**; record = a 4-byte `u32` cell key
    (no path). Documented in `area_inventory.md` / `terrain.md §1.2`.
  - `data/map000/texture/bgtexture.lst` and `data/effect/texture/bgtexture.lst` — **this spec**;
    48-byte record (1-byte `kind` + 47-byte relpath).
  - `data/effect/bmplist.lst` — effect texture pool; 30-byte name record (implicit slot index).
  - `data/effect/xobj.lst` — effect xobj manifest; 34-byte record (explicit `u32` id + 30-byte name).
  - `data/effect/xeffect.lst` — effect xeff manifest; 30-byte name record (effect id sourced from the
    referenced `.xeff` file, not the `.lst`).
- **NOT `.lst` (recurring confusion).** `data/char/motlist.txt`, `data/char/bindlist.txt`,
  `data/char/skinlist.txt`, and `data/item/skinlist.txt` are **CP949 delimited-text tables (`.txt`)**,
  a different format family entirely — they are *not* binary `.lst` and must not be parsed with this
  spec. (Listed here only to pin the distinction; their `lst`-suggesting names cause repeated mix-ups.)
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
- **IDA-corrected (anchor 263bd994); re-confirmed CYCLE 1, no drift:** the `.map` `intTexId` **IS**
  the 0-based `.lst` record index, used **directly** — the global texture-pool accessor reads
  `pool_base + stride * intTexId` with **NO** subtraction, and the raw `intTexId` is stored into the
  per-cell texture list with **NO** subtraction by the per-cell terrain-id register routine. The
  **only** `-1` in the whole chain is on the `.ted` per-cell byte: the per-cell texture finalize
  routine clamps the byte to `[1, count]` (both `<1` and `>count` → 1) and then indexes the cell
  texture list as `perCellTexList[byte-1]`. That `-1` is purely the difference between the resolve
  base and the write base (the list is written from one slot higher than it is read), with the clamped
  index ranging 1..count; it is a fixed, statically isolable code site. The earlier "1-based
  `intTexId` minus 1" reading was **WRONG** and is **REFUTED** — it disagreed with `terrain.md §3.5`/
  `§5.6` (which were correct); this off-by-one rendered `intTexId=0` cells as missing textures (black
  world) until corrected. (See also `terrain.md`, which states this `-1` resolution as RESOLVED.)

---

## Enumerations / flags

**`kind` (record +0) — material render-mode tag.** A full scan of every record in both shipped
instances (2,330 records total) found the byte is **not constant**: `0x01` is the majority, but
122 records in `map000` (and 105 in `effect`) carry a value other than `0x01`. At least six distinct
values occur. The earlier "animated vs static boolean" reading is retired — a single bit would not
produce a value as high as `0x14`. Each value correlates with a recognisable family of texture relpaths, so the byte is a
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

- **kind == 0 → record SKIPPED, no pool entry created (CODE-CONFIRMED, deep-3d-cartography pass).**
  The loader's loop body is gated on `kind != 0`: any record whose `kind` byte is zero is skipped
  entirely and contributes no in-memory pool entry. No kind=0 records are present in either shipped
  instance. This gate fires before the static/non-static dispatch described below.
- **Runtime pool dispatch (binary, CODE-CONFIRMED; CYCLE 1).** For records that pass the kind≠0
  gate, a **single binary branch** decides which of **two** engine-wide render-object descriptor types
  each pool entry carries: **`kind == 0x01`** wires the **STATIC** render-object type (a plain static
  ground-texture object — the default and majority); **any** `kind != 0x01` wires the **NON-STATIC**
  render-object type (the scroll / animated material family). Both descriptor types are zero-valued
  globals in the shipped binary (the render-type global serving as the format argument is a read-only
  literal zero for both paths); the distinction is meaningful only by symbol identity to downstream
  renderer consumers. The loader's branch only distinguishes "is it 1 or not".
- **The 6-value enum is DATA-ONLY and is never re-branched in the loader (CONFIRMED, CYCLE 1).** A
  full scan of the loader found **exactly one** comparison against the kind byte (the `==1` test);
  there is **no** per-value branch (nothing tests 2 / 0x0A / 0x0B / 0x0C / 0x14) and **no jump table**
  on the kind byte anywhere in the loader. The finer six-value enumeration exists **only as data** —
  and the raw kind byte is additionally preserved verbatim in a parallel per-slot byte array, leaving
  open that a *downstream* renderer could re-read it per value; but within the load/dispatch path the
  distinction is strictly **static (==1) vs non-static (!=1)**. So any per-value render nuance (scroll
  speed, sway, foliage billboarding) must be reconstructed by the engineer from the relpath family —
  the original loader does not switch on it.
- **Shared scheduler pool (CODE-CONFIRMED).** Every pool entry — **both** kinds — is registered into
  the engine's shared texture effect/scheduler pool with a fixed non-zero lifetime parameter at
  per-entry init. For these terrain/effect `bgtexture.lst` entries that lifetime parameter is
  **180000** (informational, not an on-disk field; for contrast the sibling `bmplist.lst` effect-pool
  loader uses lifetime **120000**, which is one cue that distinguishes the two effect loaders). The
  kind byte only changes **which** of the two render-object descriptor types the entry carries,
  **not** whether it joins the pool. Even the static (`==1`) entries talk to that shared pool for
  their per-frame texture handle. An engineer wiring the kind byte should treat `0x01` as the default
  (static-ground) render-object type and route every other value to the alternate (scroll/animated)
  type, then layer the render-mode bucket on top.
- The render-mode **categories** are HIGH confidence (the value→relpath-family correlation holds
  across the full scan of both files). The **exact rendering behaviour** behind each mode
  (scroll speed, sway parameters, alpha-test vs. billboard) is INFERRED from the relpath families
  and the value spread — it is NOT confirmed against the engine's shader/material table. An
  engineer must not treat the proposed labels as confirmed shader semantics; treat them as a
  render-mode bucket and tune behaviour from the family.
- The two instances have overlapping but not identical relpath populations, so the per-file count
  of non-`0x01` records differs (105 in `effect`, 122 in `map000`). The value set is shared.
- The same correction applies to **`bgtexture.txt` col1**: it is the same render-mode tag, not a
  `0`/`1` animated flag. Do not read col1 as a boolean.

---

## Known unknowns

- The exact engine rendering behaviour selected by each non-`0x01` `kind` value (scroll vector,
  vertex-sway parameters, alpha-test threshold, billboarding) — the value→render-mode buckets are
  HIGH, but the per-bucket behaviour is INFERRED from relpath families, not read from the engine's
  material table.
- Whether a downstream renderer re-reads the preserved per-slot kind byte to distinguish the six
  values per-value (the loader itself does not — see §Enumerations); not investigated, low priority.
- Whether `kind` values beyond the six observed (`0x01`, `0x02`, `0x0A`, `0x0B`, `0x0C`, `0x14`)
  exist in other VFS revisions; only these two shipped instances were scanned.
- Whether the relpath buffer is exactly 47 bytes or a smaller logical cap zero-padded into 47
  (the stride is fixed at 48 regardless; a parser should read up to the first NUL within the
  47-byte field).

---

## Cross-references

- Related formats: `terrain.md` (the `.ted` → `.map` → pool → `.dds` chain, incl. the RESOLVED `-1`
  on the `.ted` byte; this spec replaces the inferred 76-byte record layout in `terrain.md §4.1`),
  `texture.md` (the `.dds` payload).
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
- **CYCLE 1 re-verification (2026-06-19, IDB anchor 263bd994):** sharpened the kind-byte dispatch to
  CONFIRMED single `==1`/`!=1` branch with the 6-value enum as data-only (no per-value branch / jump
  table); recorded that every entry joins the shared texture scheduler pool regardless of kind;
  re-confirmed the `-1` inventory and removed the IDA addresses that had leaked into the §Cross-file
  join note (functions named by role). No addresses or decompiler output crossed the firewall.
- **`.lst`-family pass (2026-06-21, evidence [static-ida, vfs-sample]):** re-confirmed the
  `bgtexture.lst` loader and layout with **no drift / no correction**; folded in a §Identification
  orientation note placing `bgtexture.lst` within the five-kind binary `.lst` family
  (`d<area>.lst`, the two `bgtexture.lst`, `bmplist.lst`, `xobj.lst`, `xeffect.lst`) and pinned that
  `motlist/skinlist/bindlist` are `.txt`, not `.lst`; recorded the shared-scheduler lifetime value
  (180000 terrain / 120000 bmplist) as informational. The four sibling `.lst` kinds warrant their own
  family/per-kind spec (e.g. `manifests_lst.md`); they are out of scope for this `bgtexture`-only
  spec and are only referenced here for orientation. No addresses or decompiler output crossed the
  firewall.
- **deep-3d-cartography (2026-06-29, static-only, ida_anchor f61f66a9):** kind==0 skip confirmed —
  the loader loop body is gated on `kind != 0`; kind=0 records produce no pool entry (added to
  §Enumerations dispatch bullet). Confirmed both render-type descriptor globals are zero-valued in
  the shipped binary (read-only literal zero); the static/non-static dispatch is distinguishable
  only by symbol identity downstream, not by value. No layout drift; all prior offsets, strides, and
  formulae re-confirmed. No addresses or decompiler output crossed the firewall.
