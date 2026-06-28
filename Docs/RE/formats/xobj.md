# Format: .xobj (effect primitive mesh — ASCII indexed triangle list)

> Clean-room spec. Neutral description only — NO sample bytes, NO decompiler pseudo-code.
> Promoted from dirty-room notes under EU Software Directive 2009/24/EC Art. 6, solely to
> achieve interoperability. No decompiler output and no binary addresses appear below.
> Consumed by Assets.Parsers (parse/load side) and the client effect runtime (the effect
> subsystem's mesh-particle path). Every offset an engineer cites must reference this file.
> This spec covers BOTH the `.xobj` mesh body (ASCII text) AND its companion `data/effect/xobj.lst`
> index list (binary), because the two form one logical load chain. It supersedes the brief
> placeholder note in `effects.md §A.11` (which only flagged `.xobj` as ASCII text without the
> full layout); for the broader effect subsystem context see `effects.md` Section A.
>
> verification: fully verified — the `.xobj` body field order/types, the V-flip, the discarded
>               leading marker (now confirmed to be the slot-id echo, not a format tag), the discarded
>               per-vertex normal triplet, the u16 indices, and the 24-byte runtime XObj / 24-byte
>               runtime vertex layouts were all recovered from the loader control flow. The `.xobj`
>               body is sample-verified against 30 real files (including `(0000)-plane.xobj`,
>               `(0001)-spear.xobj`, `(0004)-triangurate.xobj`, `(0002)-cone.xobj` — all byte-exact).
>               The `xobj.lst` binary layout (count header + 34-byte records) is ALSO sample-confirmed:
>               the real `xobj.lst` is 1092 bytes = 4 + 34 × 32, with records 0..5 matching slot
>               indices 0..5 and filenames `(0000)-plane.xobj` .. `(0005)-squarehorn.xobj`. The
>               leading `(NNNN)` filename prefix == `xobj.lst` slot_index == body marker line is
>               byte-confirmed across all 30 bodies and 32 lst records.
> ida_anchor: f61f66a9ae0ec1e946105b2ecff76e8930cb1d1367df64e5688a5266f5ad9963
> ida_reverified: 2026-06-24
> ida_reverified: 2026-06-27 — CYCLE 14 re-anchor (f61f66a9): confirmatory - xobj.lst manifest and per-file ASCII .xobj loader cleanly relocated, 1 re-confirmed SAME, 0 corrected
> evidence: [static-ida, vfs-sample]
> conflicts: none. One remaining open item: the exact `.xeff` body field that stores the referenced
>            xobj slot index (see Linkages → "xeff ↔ xobj join (OPEN)").

---

## Scope and authoritative-source rule

`.xobj` files are the **static primitive triangle meshes** used by the effect subsystem's
mesh-particle emitters (the `emitter_type == 1` / mesh-particle path of an `.xeff`; see
`effects.md §A.12`). Each `.xobj` is a **plain ASCII text** file — one numeric token per line,
whitespace/newline delimited — read through the same dual binary/text asset-stream readers the
CP949 text tables use. It is **not** a binary-header format; the binary-header documentation
template does not apply.

The meshes are NOT discovered by a directory scan. The effect-subsystem boot reads a fixed
manifest, `data/effect/xobj.lst` (a **binary** index list), which names each `.xobj` file and
assigns it a runtime slot. The slot index is the runtime id other code uses to reference a mesh.

Two related-but-distinct primitive-mesh formats exist; do not confuse them:

| Path pattern | Format | Parser | Spec |
|---|---|---|---|
| `data/effect/xobj/<name>.xobj` | **ASCII text** indexed triangle list (this spec) | text reader | this file |
| `data/effect/obj/<name>.eff` | **binary** indexed triangle list (32-byte vertex) | binary reader | `effects.md` Section B |

They share the same logical shape (index list + vertex array) but one is text and one is binary;
they are NOT interchangeable and must be dispatched by directory + extension.

---

## Part 1 — `data/effect/xobj.lst` (the index list, BINARY)

The manifest the effect boot reads to enumerate every `.xobj`. Opened in binary mode (no
text-token flag). Little-endian.

### Header

| Offset | Size | Type   | Field   | Notes |
|-------:|-----:|--------|---------|-------|
| 0x00   | 4    | u32 LE | `count` | Number of xobj entries. The manager allocates `count` runtime XObj records (each 24 bytes; the array is array-length-prefixed). The record array begins immediately at offset 4. |

### Record (34-byte stride)

A flat array of `count` fixed-size records, concatenated with no inter-record padding.

| Offset (in record) | Size | Type      | Field        | Notes |
|-------------------:|-----:|-----------|--------------|-------|
| +0x00              | 4    | u32 LE    | `slot_index` | Destination slot in the runtime XObj array for this file. Range-validated `0 <= slot_index < count`; an out-of-range entry is **skipped** (not loaded), not clamped. |
| +0x04              | 30   | char[30]  | `name`       | NUL-padded base file name (the read buffer is zeroed to the full 30 bytes first). The full path is built as `data/effect/xobj/<name>`. CP949 for Korean names. |

- **Record stride:** 34 bytes (4 + 30).
- The 30-byte name-field width matches the convention used by the sibling effect manifests
  `bmplist.lst` and `xeffect.lst` (see `bgtexture_lst.md` → ".lst family" note and `effects.md §A.9`).
- **Slot index = runtime id.** The parsed `.xobj` is stored at `xobjArray[slot_index]`, so the
  `xobj.lst` `slot_index` IS the integer id by which any other code (notably an `.xeff` mesh-particle
  element) references a given mesh. This is the JOIN KEY (see Linkages).
- **Status:** parser-verified and sample-confirmed. The real on-disk `xobj.lst` is 1092 bytes
  (= 4 + 34 × 32), records 0..5 carry slot indices 0..5 with names `(0000)-plane.xobj` through
  `(0005)-squarehorn.xobj`. The manifest holds 32 entries; 30 corresponding `.xobj` bodies were
  present in the extract (the last 2 entries are absent from this extract but irrelevant to the
  layout). The `slot_index` field is byte-confirmed to equal the leading `(NNNN)` filename prefix.

---

## Part 2 — `.xobj` mesh body (ASCII TEXT)

Opened in text-token mode (the disk-file text flag). The reader returns **one numeric token per
line** (whitespace/newline delimited). The body is parsed strictly in the read order below; there
is no magic, no version field that the parser retains, no padding, no checksum, no compression, and
no encryption.

### Read order (on-disk token stream)

| Step | Token kind | Field | Type | Notes |
|------|-----------|-------|------|-------|
| 1 | int | `marker` (unused) | int | First token. Read into a local and **discarded** — the parser keeps no copy. Byte-confirmed to equal the file's `xobj.lst` slot index (which also equals the leading `(NNNN)` filename prefix): it is the object's own slot-id echoed into the body as an author-aid, not a format/version tag. Values observed across all 30 samples match their respective slot indices exactly. |
| 2 | int | `tri_count` | int | Triangle count. `index_count = 3 × tri_count`. |
| 3 | (allocate) | — | — | Allocates `2 × index_count` bytes for the u16 index buffer. |
| 4 | int × `index_count` | `indices[i]` | u16 | One index token per line; parsed as int, **narrowed to u16** on store. Total `3 × tri_count` tokens, in order. |
| 5 | u32 | `vert_count` | u32 | Vertex count. |
| 6 | (allocate) | — | — | Allocates `vert_count` runtime vertices (24 bytes each; array-length-prefixed). |
| 7 | float × 8, per vertex | (see below) | f32 | `vert_count` vertices, each described by exactly 8 float tokens (one per line). |

### Per-vertex line (8 floats, one vertex)

Each text vertex is 8 floats in this fixed order: `posX posY posZ nX nY nZ u v`.

| Token | Type | File field | Stored as | Notes |
|------:|------|-----------|-----------|-------|
| 1 | f32 | `pos.x` | vertex +0x00 | |
| 2 | f32 | `pos.y` | vertex +0x04 | |
| 3 | f32 | `pos.z` | vertex +0x08 | |
| 4 | f32 | `n.x` | — | **DISCARDED** (read into scratch, not retained). |
| 5 | f32 | `n.y` | — | **DISCARDED**. |
| 6 | f32 | `n.z` | — | **DISCARDED**. |
| 7 | f32 | `u` | vertex +0x10 | UV U, stored as-is. |
| 8 | f32 | `v` | vertex +0x14 | UV V, **stored as `1.0 − v`** (V flip applied on load). |

So the middle three floats (the per-vertex normal) are parsed but thrown away — effect meshes are
unlit / additive billboard-style geometry — and the V coordinate is flipped on store. Status: the
field order, types, V-flip, discarded normals, and u16 narrowing are **parser-verified AND
sample-verified** (the one real file matches exactly).

---

## Part 3 — Runtime structures (in-memory)

Provided so an engineer understands the runtime's view; these are not on-disk layouts. Both are
24 bytes.

### Runtime XObj record (24 bytes)

| Offset | Size | Type    | Field              | Notes |
|-------:|-----:|---------|--------------------|-------|
| +0x00  | 4    | u16*    | `index_ptr`        | Heap pointer to the `index_count` u16 indices. |
| +0x04  | 4    | Vertex* | `vertex_ptr`       | Heap pointer to the vertex array (array-length-prefixed). |
| +0x08  | 4    | u32     | `index_count`      | `= 3 × tri_count`. |
| +0x0C  | 4    | u32     | `vert_count`       | Number of vertices. |
| +0x10  | 4    | u32     | `vertex_byte_size` | `= 24 × vert_count`. |
| +0x14  | 4    | ptr     | `d3d_index_buffer` | GPU 16-bit index buffer handle; null until the index-buffer upload step fills it (see Read algorithm step 6). |

### Runtime Vertex (24 bytes) — FVF = position + diffuse + one UV pair

| Offset | Size | Type | Field     | Source |
|-------:|-----:|------|-----------|--------|
| +0x00  | 4    | f32  | `x`       | `pos.x` from file. |
| +0x04  | 4    | f32  | `y`       | `pos.y` from file. |
| +0x08  | 4    | f32  | `z`       | `pos.z` from file. |
| +0x0C  | 4    | u32  | `diffuse` | NOT read from file. Set by the vertex constructor to opaque-default: the three low bytes (B,G,R) = 0 and the high byte (A) = 0xFF → packed `0xFF000000` (ARGB: opaque, black RGB). The mesh is expected to be modulated by its texture/material at draw time. |
| +0x10  | 4    | f32  | `u`       | `u` from file. |
| +0x14  | 4    | f32  | `v`       | `1.0 − v` from file (flipped). |

This is a position + 32-bit-diffuse + single-UV vertex layout. Normals from the file are not
retained. Status: parser-verified (offsets recovered from the loader and the index-buffer upload
path); in-memory only, so not independently sample-checkable. The exact ARGB byte order of the
default diffuse dword (B,G,R,A → A=0xFF, RGB=0 → opaque black) should be re-confirmed at
implementation time, as should whether any later path overwrites it.

---

## Read algorithm (prose)

For one `.xobj` file:

1. Open `data/effect/xobj/<name>.xobj` in **text-token mode**. If the open fails, log a "cannot find
   file" diagnostic and bail (the slot is left empty).
2. Read the `marker` int and **discard** it. Read `tri_count`; compute `index_count = 3 × tri_count`.
3. Allocate a u16 index buffer of `index_count` entries. Read `index_count` int tokens (one per
   line), narrowing each to u16, into the buffer in order.
4. Read `vert_count` (u32). Allocate `vert_count` runtime vertices; each vertex's `diffuse` is set to
   the opaque-default `0xFF000000` by its constructor.
5. For each vertex, read 8 float tokens: keep `pos.xyz` and `u`, store `v` as `1.0 − v`, and
   **drop** the three normal floats.
6. Close the file, then perform the index-buffer upload: create a GPU **16-bit** index buffer sized
   `2 × index_count` bytes, lock it, copy the parsed u16 index array into it, and unlock. The
   vertices stay in system memory (the effect draws via an immediate indexed-primitive path over the
   system-memory vertices + the GPU index buffer; no GPU vertex buffer is created).

Validation: only the `xobj.lst` `slot_index` range check (Part 1). The `.xobj` body parser trusts
its own token counts — there is no length/checksum guard inside the mesh file.

---

## Linkages

### Load chain

```
Effect-subsystem boot
  ├─ data/effect/bmplist.lst    → effect texture pool (see effects.md §A.10)
  ├─ data/effect/xobj.lst       → enumerate .xobj → parse each into XObjArray[slot_index]
  │       slot_index (u32 in xobj.lst)  ==  runtime XObj-array index        <-- JOIN KEY
  │       name (char[30])               →  data/effect/xobj/<name>.xobj
  ├─ data/effect/xeffect.lst    → .xeff effect descriptors (see effects.md §A.9)
  └─ totalmugong / *jointeff / *swordlight text tables
```

### What references `.xobj` (the join key)

- `data/effect/xobj.lst` is the **only** enumerator of `.xobj` files. The **JOIN KEY is the
  `xobj.lst` `slot_index`**: whatever value the manifest record carries is the index at which the
  parsed mesh lands in `XObjArray`. Effect descriptors that draw a mesh reference an xobj by this
  integer index.
- Filename convention (byte-confirmed): the leading `(NNNN)` in every `.xobj` filename equals
  the file's `xobj.lst` `slot_index`, which also equals the body's opening `marker` integer. This
  is a human-readable author hint embedded in the filename; it is not load-critical (the manifest
  `slot_index` field drives the runtime slot, not the filename). Confirmed across all 30 bodies
  and all 32 lst records in the real extract.

### xeff ↔ xobj join (OPEN)

- An `.xeff` mesh-particle element (`emitter_type == 1`, `effects.md §A.12`) draws an `.xobj` mesh
  selected by an integer that resolves to an `XObjArray` slot index. `effects.md §A.4.0` /
  `§A.11` describe the element's `resource_id` as the dispatch gate (`< 10000` selects a direct index
  into a shared mesh table); the shared mesh table is this XObj array.
- **NOT YET FULLY RECOVERED:** the exact `.xeff` body field that stores the `.xobj` slot index, and
  the precise point where the effect render path indexes `XObjArray[...]`, were not traced. The
  `.xeff` boot list loader only primes the leading effect id; the full per-element / keyframe parse
  that pulls a mesh reference happens deeper in the effect descriptor's activate path. Closing this
  requires a follow-up trace of the effect descriptor's full load/activate path (and, if needed, a
  live-debugger breakpoint on the index-buffer upload / the `XObjArray` read site while an in-game
  mesh-bearing effect plays). Tracked here so an engineer does not assume the link is settled.

### Builder / factory / consumer

- **Builder:** the xobj-list loader builds the `XObjArray` from `xobj.lst`.
- **Factory:** the per-file `.xobj` loader parses one file into one runtime XObj record.
- **GPU consumer:** the index-buffer upload step (Read algorithm step 6) creates the 16-bit GPU index
  buffer. The actual draw site is the effect render path invoked by the effect descriptor — to be
  confirmed alongside the xeff ↔ xobj join above.

---

## Named constants

| Name | Value | Context |
|------|------:|---------|
| `XOBJ_LST_RECORD_STRIDE` | 34 (0x22) | `xobj.lst` record size: u32 `slot_index` + 30-byte `name`. |
| `XOBJ_LST_NAME_LEN` | 30 (0x1E) | Bytes per `name` field in an `xobj.lst` record (matches `bmplist.lst` / `xeffect.lst`). |
| `XOBJ_RECORD_SIZE` | 24 (0x18) | Runtime XObj record size. |
| `XOBJ_VERTEX_SIZE` | 24 (0x18) | Runtime vertex size (position + diffuse dword + one UV pair). |
| `XOBJ_VERTEX_FLOATS_ON_DISK` | 8 | Float tokens per vertex on disk: `posX posY posZ nX nY nZ u v` (the 3 normal floats are discarded). |
| `XOBJ_VERTEX_DEFAULT_DIFFUSE` | 0xFF000000 | Constructor-default vertex diffuse (ARGB; opaque, black RGB). Not read from file. |
| `XOBJ_INDEX_FORMAT` | 16-bit | GPU index buffer index width (u16 indices). |

---

## Known unknowns

- The exact `.xeff` field that stores the referenced `.xobj` slot index, and the `XObjArray` draw-site
  index expression (see Linkages → "xeff ↔ xobj join (OPEN)"). This is the one remaining open item
  for the format family.
- The exact ARGB byte order of the default diffuse dword and whether any path overwrites it (the
  geometry is expected to be texture/material-modulated; in-memory only, not independently
  sample-checkable).

---

## Cross-references

- Related formats: `effects.md` (the effect subsystem — Section A `.xeff` particle descriptors that
  reference `.xobj` meshes via `resource_id`; Section B `data/effect/obj/*.eff`, the BINARY sibling
  primitive-mesh format; `§A.9`/`§A.10` companion manifests; `§A.11`, the placeholder note this spec
  supersedes), `bgtexture_lst.md` (the binary `.lst` family overview, including `xobj.lst`).
- Subsystem behaviour: `specs/effects.md` (effect spawn / tick / draw runtime).
- Glossary: see `Docs/RE/names.yaml` (orchestrator-owned; proposed entries: the runtime `XObj`
  record, the `XObjVertex` vertex, the `xobj.lst` manifest record with its `slot_index` / `name`
  fields, and the xobj-list loader / per-file loader / index-buffer-upload roles).
- Provenance: see `Docs/RE/journal.md` (orchestrator-owned; add an entry for this new spec).
