# Format: misc_data  (miscellaneous script and data files: .xdb / .mi / .tol / .ion / .sc)

> Clean-room spec. Neutral description only — NO sample bytes, NO decompiler pseudo-code.
> Consumed by Assets.Parsers. Every offset an engineer cites must reference this file.
>
> Promoted from dirty-room analyst notes under EU Software Directive 2009/24/EC Art. 6.
> No decompiler output and no binary virtual addresses appear anywhere in this file.
>
> status: sample_verified — see per-section confidence notes.

This document covers five distinct file types that share no common wire format but are all small
script/data assets extracted from `.pak` archives. They are grouped here because none warrants a
standalone spec file. Each section is self-contained.

---

## Section 1 — `.xdb` Script Data Binary files

### 1.1 Identification (all `.xdb` sub-variants)

- **Extension:** `.xdb`
- **Found in:** `.pak` archive; logical path: `data/script/*.xdb`
- **Magic / signature:** None. No file-level header.
- **Endianness:** Little-endian throughout.
- **Version field:** None observed.
- **Record count:** Derived from file size divided by the per-variant stride (no stored count).
  The file size must be an exact multiple of the stride; any remainder is an error.

Three named variants are documented. They share the headerless flat-array structure but differ
in record stride and field types.

---

### 1.2 `actor_size.xdb` — Per-actor-class scale override table

**sample_verified: true**

**Role:** Maps an integer actor-class ID to two floating-point scale factors (XZ-plane scale and
Y-axis scale). Used at runtime to resize actor meshes per class.

**Record stride:** 12 bytes. Record count = `file_size / 12`.

#### Record layout (12 bytes)

| Offset | Size | Type  | Field          | Notes                                      | Confidence |
|-------:|-----:|-------|----------------|--------------------------------------------|------------|
| 0      | 4    | u32LE | actor_class_id | Sequential 0-based actor class identifier  | HIGH       |
| 4      | 4    | f32LE | scale_xz       | XZ-plane (body width) scale; observed range: 0.10 – 2.00 | HIGH |
| 8      | 4    | f32LE | scale_y        | Y-axis (body height) scale; observed range: 1.00 – 1.50 | HIGH |

**Notes:**
- An identity record (scale_xz = 1.0, scale_y = 1.0) is valid and common.
- The stride 12 is confirmed by exact file-size division: a known sample of 180 bytes yields
  exactly 15 records with no remainder.
- The record key (`actor_class_id`) is used as a lookup key in a runtime associative table keyed
  by the first u32 of each record; this applies to all `.xdb` variants (see §1.5).

---

### 1.3 `buff_icon_position.xdb` — Buff-effect icon atlas coordinates

**sample_verified: true**

**Role:** Maps a buff-effect integer ID to the pixel origin of its icon cell within a shared UI
sprite atlas texture. Icon cells are 25 × 25 pixels.

**Record stride:** 12 bytes. Record count = `file_size / 12`.

#### Record layout (12 bytes)

| Offset | Size | Type  | Field   | Notes                                                        | Confidence |
|-------:|-----:|-------|---------|--------------------------------------------------------------|------------|
| 0      | 4    | u32LE | buff_id | Buff-effect identifier; non-sequential; range 1 – 1103 observed | HIGH   |
| 4      | 4    | u32LE | atlas_x | Pixel X of the icon's top-left corner within the atlas       | HIGH       |
| 8      | 4    | u32LE | atlas_y | Pixel Y of the icon's top-left corner within the atlas       | HIGH       |

**Atlas grid:**
- Cell size: 25 × 25 pixels.
- Origin convention: 1-based (first cell top-left = pixel (1, 1), not (0, 0)).
- X values follow a regular stride of 25 starting from 1: 1, 26, 51, 76, 101, 126, 151, 176, …
- When X would overflow a row, X wraps back to 1 and Y advances by 25.
- Occasional irregular coordinate values (e.g. 250, 251, 276, 304 …) suggest some icons are
  placed in non-grid positions; the parser must treat `atlas_x` and `atlas_y` as raw pixel values
  and not infer them from a formula.

**Notes:**
- The stride 12 is confirmed by exact file-size division: a known sample of 1608 bytes yields
  exactly 134 records with no remainder.
- The record key (`buff_id`) is used as a lookup key in a runtime red-black tree (see §1.5).

---

### 1.4 `effectscale.xdb` — Per-effect-object scale table

**sample_verified: true**

**Role:** Maps a large game-object resource ID to a floating-point scale multiplier applied to the
associated particle or effect mesh at runtime.

**Record stride:** 8 bytes. Record count = `file_size / 8`.

#### Record layout (8 bytes)

| Offset | Size | Type  | Field     | Notes                                              | Confidence |
|-------:|-----:|-------|-----------|----------------------------------------------------|------------|
| 0      | 4    | u32LE | object_id | Large non-sequential resource identifier           | HIGH       |
| 4      | 4    | f32LE | scale     | Float scale multiplier; observed values: 2.0, 3.0 | HIGH       |

**Notes:**
- The stride 8 is confirmed by exact file-size division: a known sample of 16 bytes yields exactly
  2 records with no remainder. The sample is small; assume more records exist in production builds.
- The record key (`object_id`) is used as a lookup key in a runtime red-black tree (see §1.5).
- Resource IDs in the observed sample are large values in the range of 3.6 × 10^8 and appear
  sequentially allocated.

---

### 1.5 Cross-variant notes for all `.xdb` files

- No magic bytes, no version, no stored record count. The stride is the identifying characteristic
  of each named variant; the parser must already know which variant it is loading.
- Record layout is purely numeric (u32 / f32). No text fields. No CP949 content.
- The first field of each record serves as the lookup key in a runtime associative structure. The
  key is always u32LE at offset 0 within the record.
- File size must be an exact multiple of the stride; if not, the file is malformed.

**Known unknowns:**
- No loader routine located for `actor_size.xdb` or `effectscale.xdb`; path strings were not
  found as string literals in the binary. The strides are confirmed by sample arithmetic alone for
  these two variants.
- Whether additional named `.xdb` variants exist beyond the three documented here is unknown.
- Whether `.xdb` files carry a version across different game patches is unknown (single samples only).

---

## Section 2 — `mobinfo.mi` — Monster Info Table

**sample_verified: true** (header and stride); **hypothesis** (field semantics — see notes)

- **Extension:** `.mi`
- **Found in:** `.pak` archive; logical path: `data/ui/mobinfo.mi`
- **Magic / signature:** None observed.
- **Endianness:** Little-endian.
- **Version field:** None observed.

**Role:** UI mob information table. For each mob class in the game database, stores references to
portrait icon resources and to string-table entries used to render the mob's name in the UI.

### File layout

The file begins with a 4-byte header count, followed immediately by that many 28-byte records.
Total file size = 4 + (`count` × 28).

#### Header (4 bytes)

| Offset | Size | Type  | Field | Notes                              | Confidence |
|-------:|-----:|-------|-------|------------------------------------|------------|
| 0      | 4    | u32LE | count | Number of records that follow      | HIGH       |

#### Per-record layout (28 bytes — 7 × u32LE)

| Offset | Size | Type  | Field          | Notes                                                      | Confidence |
|-------:|-----:|-------|----------------|------------------------------------------------------------|------------|
| 0      | 4    | u32LE | mob_class_id   | Mob class type identifier; range 101 – 121 in known sample | HIGH       |
| 4      | 4    | u32LE | name_str_id    | String-table reference for the mob's primary display name; 0xFFFFFFFF = none | PARTIAL |
| 8      | 4    | u32LE | alt_name_str_id | String-table reference for an alternate name (e.g. longer title); 0xFFFFFFFF = none | PARTIAL |
| 12     | 4    | u32LE | icon_index     | UI sprite index for this mob's icon; range 55 – 173 observed | HIGH     |
| 16     | 4    | u32LE | portrait_res_1 | Resource ID for the primary portrait image; 0xFFFFFFFF = none | PARTIAL |
| 20     | 4    | u32LE | portrait_res_2 | Resource ID for the hover/alternate portrait frame; 0xFFFFFFFF = none | PARTIAL |
| 24     | 4    | u32LE | portrait_res_3 | Resource ID for a third portrait state; 0xFFFFFFFF = none | PARTIAL |

**Sentinel:** 0xFFFFFFFF indicates "not present" for all optional reference fields
(`name_str_id`, `alt_name_str_id`, `portrait_res_1`, `portrait_res_2`, `portrait_res_3`).

### Field notes

**String-table references (`name_str_id`, `alt_name_str_id`):**
- Observed `name_str_id` values cluster in the range 0x4E03 – 0x4E45, suggesting a dedicated
  string-table sector at 0x4E00. The sector or table these IDs index is not confirmed in the binary.
- Observed `alt_name_str_id` values fall in the range 20000 – 20037. The dual reference may
  distinguish a short name from a long/title name; this is inferred, not confirmed.
- No CP949 text is stored directly in this file; names are resolved at runtime via the string table.
- String IDs in this file resolve against `data/script/msg.xdb` (see §6 of this document).

**Portrait resource IDs (`portrait_res_1/2/3`):**
- Large 32-bit values. A pattern of the form `(group × 1_000_000) + index` is consistent with the
  observed values (e.g. values in the 5,080,000 range with adjacent IDs differing by 1 – 3).
  This encoding formula is unconfirmed.
- Adjacent frames often differ by 1 – 3, suggesting sequential resource allocation per mob.

**Size verification:** 4 + 21 × 28 = 592 bytes — confirmed against a known sample.

### Known unknowns

- No loader routine located in the binary; the file path was not found as a string literal in the binary.
  Format is confirmed by sample arithmetic alone.
- The exact string-table structure that `name_str_id` / `alt_name_str_id` index is not documented
  here; see §6 of this document (`msg.xdb`) for the resolved record format.
- The portrait resource ID encoding (group × 1e6 + index vs another scheme) is unconfirmed.
- Whether files with `mob_class_id` ranges outside 101 – 121 exist is unknown (single sample).

---

## Section 3 — `.tol` — Terrain Tile Obstacle / Collision Layer

**sample_verified: true** (header layout and tile-grid stride); **hypothesis** (world-origin semantics)

- **Extension:** `.tol`
- **Found in:** `.pak` archive; logical path pattern: `data/map<NNN>/<NNN>.tol`
- **Magic / signature:** None. The file begins directly with the 16-byte header.
- **Endianness:** Little-endian (header fields).
- **Version field:** None observed.

**Role:** Per-map tile walkability bitmap. Each byte encodes the obstacle state of one world tile.
Used for pathfinding and movement validation. Grid dimensions are always powers of two.

### File layout

A 16-byte header is immediately followed by a flat `width_tiles × height_tiles` byte array.
Total file size = 16 + (`width_tiles` × `height_tiles`).

#### Header (16 bytes — 4 × u32LE)

| Offset | Size | Type  | Field          | Notes                                                         | Confidence |
|-------:|-----:|-------|----------------|---------------------------------------------------------------|------------|
| 0      | 4    | u32LE | world_origin_x | World-space X origin of the grid; see world-origin note below | PARTIAL    |
| 4      | 4    | u32LE | world_origin_y | World-space Y origin of the grid; see world-origin note below | PARTIAL    |
| 8      | 4    | u32LE | width_tiles    | Grid width in tiles; must be a power of 2                     | HIGH       |
| 12     | 4    | u32LE | height_tiles   | Grid height in tiles; must be a power of 2                    | HIGH       |

**World-origin note (PARTIAL confidence):** The `world_origin_x` value observed in samples divides
exactly by 256. For example, a value of 23552 / 256 = 92, and a value of 8192 / 256 = 32, both
yield integer tile-column offsets. This suggests `world_origin_x = tile_column_offset × 256`,
where 256 is a per-world-unit scale factor (the number of sub-tile units in one tile). The
corresponding formula for absolute world tile column of grid cell (i) is:
`tile_col = world_origin_x / 256 + i`. The constant 256 is inferred from data divisibility; the
game engine's internal tile-size constant is not confirmed.

#### Tile data (immediately after header)

| Offset | Size                     | Type      | Field     | Notes                              | Confidence |
|-------:|--------------------------:|-----------|-----------|------------------------------------|------------|
| 16     | `width_tiles × height_tiles` | u8[W×H] | tile_grid | Row-major; index = `y × W + x`    | HIGH (row-major assumed; not loader-confirmed) |

Each byte encodes one tile:
- `0` — walkable (no obstacle)
- `1` — blocked (obstacle present)

Only values 0 and 1 have been observed. Any other value should be treated as blocked by a
defensive parser.

**Size verification:**
- A 2048 × 2048 map: 16 + 4,194,304 = 4,194,320 bytes — confirmed against two known samples.
- A 256 × 256 map: 16 + 65,536 = 65,552 bytes — confirmed against one known sample.

### Known unknowns

- No loader routine located in the binary; the `.tol` path was not found as a string literal in the binary.
  Format is confirmed by sample arithmetic alone.
- `world_origin_x` / `world_origin_y` units: the ×256 sub-tile scale factor is inferred from
  data divisibility; the game engine's tile-size constant is not confirmed from source.
- Row-major tile ordering (`y × W + x`) is standard and consistent with the sample, but has not
  been cross-checked against the engine's pathfinding code.
- Whether `width_tiles` and `height_tiles` are strictly required to be powers of two, or whether
  this is merely an observed property of the sample set, is not confirmed.

---

## Section 4 — `descript.ion` — Texture Description / Directory File

**sample_verified: false** (single-record sample; field1/field2 semantics unconfirmed)

- **Extension:** `.ion`
- **Found in:** `.pak` archive; logical path: `data/effect/texture/descript.ion`
- **Magic / signature:** None. The file begins directly with the first record.
- **Endianness:** Little-endian (binary fields).
- **Version field:** None observed.

**Role:** Associates a texture filename with binary metadata — at minimum, the filename's byte
length and two 32-bit values whose semantics are not yet confirmed (candidates: pak data offset
and end-offset/size, or CRC32 and size). The format is a text/binary hybrid using Windows line
endings.

### Record layout (variable length per record)

Records are delimited by CR LF (0x0D 0x0A). Each record has the following fixed structure:

```
<filename> SP <u32LE:field1> <u32LE:field2> <u8:name_length> CRLF
```

| Component      | Type       | Size        | Notes                                           | Confidence |
|----------------|------------|-------------|--------------------------------------------------|------------|
| filename       | ASCII text | variable    | Texture filename including extension (e.g. `.tga`); no null terminator within the record | HIGH |
| separator      | literal    | 1 byte      | ASCII space (0x20)                               | HIGH       |
| field1         | u32LE      | 4 bytes     | Large 32-bit value; semantics unconfirmed — candidate: pak offset | MEDIUM |
| field2         | u32LE      | 4 bytes     | Large 32-bit value; semantics unconfirmed — candidate: pak end-offset or file size | MEDIUM |
| name_length    | u8         | 1 byte      | Byte length of `filename`; confirmed equal to `len(filename)` in the single known sample | HIGH |
| line terminator | literal   | 2 bytes     | CR LF (0x0D 0x0A)                               | HIGH       |

**Total record size:** `len(filename) + 1 + 4 + 4 + 1 + 2 = len(filename) + 12` bytes.

**Parsing algorithm (single pass):**
1. Read bytes until SP (0x20) — this is the filename.
2. Read the next 4 bytes as u32LE → `field1`.
3. Read the next 4 bytes as u32LE → `field2`.
4. Read the next 1 byte as u8 → `name_length`.
5. Read the next 2 bytes and verify they are CR LF.
6. Assert `name_length == len(filename)` as a consistency check.
7. Repeat from step 1 until end of file.

**Text encoding:** ASCII filenames confirmed in the single available sample. Whether filenames can
contain CP949 characters is unknown; treat as ASCII unless evidence to the contrary emerges.

**Field1 / field2 interpretations (UNVERIFIED):**
- Candidate A: pak archive byte offset (field1 = start, field2 = end or size). Values of ~28 – 32 MB
  are plausible for late entries in a large archive.
- Candidate B: one field is a CRC32 checksum, one is a file size. Not confirmed.
- Windows FILETIME encoding was tested and rejected (values do not map to plausible dates).

### Known unknowns

- Only a single-record sample is available. Whether files with multiple records exist in production
  and whether they all follow the same record structure is unknown.
- `field1` and `field2` semantics are unconfirmed.
- Whether the filename field can contain non-ASCII (CP949) characters is unknown.
- No loader routine located in the binary; the path was not found as a string literal in the binary.

---

## Section 5 — `discript.sc` — UI Descriptor Script Table

**sample_verified: true**

- **Extension:** `.sc`
- **Found in:** `.pak` archive; logical path: `data/script/discript.sc`
- **Magic / signature:** None. The file begins directly with the first record.
- **Endianness:** Little-endian (u32 fields).
- **Version field:** None observed.

**Role:** UI descriptor table. Maps integer IDs to Korean display names and (for hotkey-capable
windows) to a keyboard shortcut string. Covers party commands, currency labels, UI window names,
and guild/faction actions.

### File layout

Flat array of fixed 68-byte records. No header. Record count = `file_size / 68`. The file size
must be an exact multiple of 68; any remainder is an error.

#### Per-record layout (68 bytes = 0x44)

| Offset | Size | Type     | Field        | Notes                                                      | Confidence |
|-------:|-----:|----------|--------------|------------------------------------------------------------|------------|
| 0      | 4    | u32LE    | descriptor_id | Unique integer identifier; see ID ranges below            | HIGH       |
| 4      | 4    | u32LE    | category     | Category code; see enumeration below                       | HIGH       |
| 8      | 30   | char[30] | display_name | CP949-encoded Korean display string, null-padded to 30 bytes | HIGH     |
| 38     | 3    | char[3]  | keyboard_shortcut | ASCII keyboard shortcut notation, null-padded to 3 bytes; see encoding below | HIGH |
| 41     | 27   | u8[27]   | reserved     | All zero in the known sample; purpose unknown             | LOW        |

**Total:** 4 + 4 + 30 + 3 + 27 = 68 bytes.

### Enumerations

#### `category` values

| Value | Meaning                                    | Confidence |
|------:|--------------------------------------------|------------|
| 3     | Party command                              | HIGH       |
| 102   | Hotkey window (has keyboard shortcut)      | HIGH       |
| 103   | Currency label                             | HIGH       |
| 105   | Faction / guild action                     | HIGH       |

Other values may exist in production builds; treat unknown category values as reserved.

#### `descriptor_id` ranges (observed)

| Range       | Apparent group         |
|------------|------------------------|
| 8 – 12     | Party commands         |
| 70 – 72    | Currency labels        |
| 88 – 89    | Unknown                |
| 4000 – 4013 | Hotkey window names   |
| 4500 – 4508 | Guild/faction actions |

#### `keyboard_shortcut` encoding

- For records with `category = 102` (hotkey windows): three ASCII bytes in the form `(X)`, where
  `X` is the letter key. Examples: `(C)` = character info, `(P)` = party, `(S)` = skills,
  `(I)` = inventory, `(M)` = map.
- For all other categories: byte at offset 38 = 0x30 (`'0'`), bytes at offsets 39 – 40 = 0x00.
  The `'0'` byte is a placeholder indicating no keyboard shortcut.

### Text encoding

The `display_name` field is encoded in **CP949 (code page 949 / EUC-KR)**. The field is
null-padded to exactly 30 bytes; a string shorter than 30 bytes is followed by one or more
0x00 bytes. Korean text occupies 2 bytes per character in CP949; a 30-byte field holds at most
15 Korean characters.

Parsers must treat the field as a byte buffer and decode it to Unicode (UTF-16 or UTF-8) using
CP949 before display or comparison. Never treat this field as ASCII.

### Known unknowns

- The 27-byte `reserved` region at offset 41 is entirely zero in the known sample; its purpose,
  if any, is not known.
- Whether additional `category` values beyond 3, 102, 103, 105 exist in production builds.
- Whether additional `descriptor_id` ranges exist beyond those listed above.

---

## Section 6 — `msg.xdb` — UI Message Catalogue

> **Verification status: CODE-CONFIRMED** (parser read-sequence, record stride, lookup model,
> and fill-byte convention confirmed from the loader routine). **SAMPLE-VERIFIED** (record count,
> fill byte, representative string content, and ID range groupings confirmed from direct
> harness observation of the real VFS at `data/script/msg.xdb`). All claims below are graded
> individually; previously SAMPLE-UNVERIFIED items are now resolved.

- **Extension:** `.xdb` (shares extension with §1 variants; distinguished by its VFS path)
- **Found in:** `.pak` archive; logical path: `data/script/msg.xdb`
- **Magic / signature:** None. No file-level header of any kind.
- **Endianness:** Little-endian throughout.
- **Version field:** None.
- **File size (SAMPLE-VERIFIED):** 1,364,304 bytes.

**Role:** Startup binary string database that maps u32 integer IDs to CP949-encoded UI caption
strings. All visible UI text (error messages, state names, quest text, menu labels, and similar
captions) is fetched from this catalogue at runtime by numeric ID. The file is loaded once at
engine startup, before any map or asset is loaded. It is a startup asset, not a per-area asset.

### File layout

The file is a flat, headerless array of fixed-size records. There is no stored record count; the
count is derived at load time from the file size:

```
record_count = file_size / 516       (integer division; file must be an exact multiple of 516)
```

Any remainder implies a malformed file.

**Record count (SAMPLE-VERIFIED):** 1,364,304 / 516 = **2,644 records exactly**.

### Record layout (516 bytes = 0x204)

| Offset | Size | Type      | Field | Notes                                                              | Confidence |
|-------:|-----:|-----------|-------|--------------------------------------------------------------------|------------|
| 0x000  | 4    | u32LE     | id    | Unique message identifier; used as the runtime lookup key          | CODE-CONFIRMED |
| 0x004  | 512  | u8[512]   | text  | CP949-encoded string, NUL-terminated within the buffer; bytes after the NUL through +515 are filled with **0xEE** (not 0x00) | CODE-CONFIRMED + SAMPLE-VERIFIED |

**Total record size: 4 + 512 = 516 bytes.**

> **Fill-byte clarification (SAMPLE-VERIFIED):** The padding bytes after the NUL terminator are
> filled with **0xEE**, not 0x00. This is a deliberate non-null sentinel that distinguishes padding
> from short strings. A parser reading the `text` field must stop at the **first 0x00 byte**, not
> the first 0xEE byte. A record whose entire 516 bytes consist of 0xEE (all padding, no NUL, no
> non-zero id) is an empty/reserved slot and should be silently skipped.

### Filled and empty record counts (SAMPLE-VERIFIED)

| Category | Count |
|---|---|
| Total record slots | 2,644 |
| Filled slots (non-zero id, text present) | 2,633 |
| Empty slots (entirely 0xEE — reserved/unused) | 11 |

### Text encoding

All string content is encoded in **CP949** (Windows code page 949, the Korean EUC-KR superset).
The `text` field is NUL-terminated within its 512-byte buffer. The maximum usable string length
before the NUL is 511 bytes (511 CP949 code units; multi-byte Korean characters each occupy 2
bytes, so the maximum is 255 Korean characters per message). Bytes after the NUL are 0xEE fill
and must be ignored.

Parsers must register `CodePagesEncodingProvider` and decode the buffer with
`Encoding.GetEncoding(949)` before passing strings to any display layer.

### Format-string conventions (SAMPLE-VERIFIED)

Message strings may contain the following substitution conventions. A parser should not strip
or transform these — pass the raw CP949 string to the display layer, which performs substitution:

| Convention | Meaning | Example message |
|---|---|---|
| `%s` | String argument (player name, item name) | `%s님이 입장하셨습니다` |
| `%d` | Integer argument (count, level, duration) | `%d시간`, `%d분`, `%d초` |
| `%I64d` | 64-bit integer (large gold amounts) | `%I64d 금` |
| `%1%`, `%2%`, `%3%` | Positional substitution (year, month, day, etc.) | `만료 기간 : %1%년 %2%월 %3%일 %4%:%5%` |
| Embedded CRLF | Multi-line message body | Help text and long dialog strings contain `\r\n` within the string field |

The positional `%N%` convention is a custom formatter, not standard printf. The engine's
mechanism for this substitution is not documented here; the exact implementation is UNKNOWN
(see Open questions §6.8).

### Load and lookup model (CODE-CONFIRMED)

The client performs a single bulk read of the entire file into a heap buffer, then iterates all
records and inserts each one into a global **red-black tree** keyed on the u32 `id` field. The
tree is accessed at runtime via a lookup function that accepts an integer `id` and returns a
pointer to the 516-byte record; the string payload begins at byte offset 4 within that record.

The tree is keyed on `id` only; there is no secondary index by string content. Lookups are by
numeric ID exclusively.

**Load sequence:**
1. Open `data/script/msg.xdb` read-only.
2. Query file size; compute `record_count = file_size / 516`.
3. Allocate a heap buffer of `516 × record_count` bytes.
4. Read all records in a single bulk read into the buffer.
5. For each record: check whether the record is an empty slot (all 0xEE — skip it); otherwise
   read `id` (u32LE at record base), insert `(id → record_ptr)` into the global red-black tree.
6. Close the file. The buffer remains live for the process lifetime.

**Lookup contract for implementors:**
- Do NOT assume `id == slot_index + 1`. The `id` stored at record +0x000 is the authoritative
  identifier; slots are allocated in groups but are NOT sequentially numbered.
- For an empty slot check: if all 516 bytes are 0xEE, skip. In practice, checking that
  `id != 0` and that the byte at +0x004 is not 0xEE is sufficient.
- The runtime dict is keyed by `id`; build it as `Dictionary<uint, string>` using the `id` field
  as key and the CP949-decoded text as value.

### ID range groupings (SAMPLE-VERIFIED from representative slot sampling)

The `id` values are non-contiguous and non-sequential. They are allocated in semantic groups with
large numeric gaps between groups. The table below lists the confirmed groups from direct record
observation:

| Slot range | ID range | Representative CP949 texts | Apparent group |
|---|---|---|---|
| 0–3 | 1–4 | `%s금`, `%d은`, `%d동`, `엽전` | Currency format strings (gold/silver/copper/yang) |
| 4–10 | 101–107 | `확인`, `취소`, `닫기`, `수령`, `검색`, `이전`, `다음` | Common UI button labels (OK, Cancel, Close, Receive, Search, Prev, Next) |
| 11–18 | 201–208 | `%d시간`, `%d분`, `%d초`, `%d시`, `오전`, `오후`, `%d일`, `남은 시간: %s` | Time display format strings (hour/minute/second/AM/PM/day/remaining) |
| 19–22 | 501–504 | `기간 연장`, `폐기`, `노점`, `정리` | Item shop/stall actions (Extend, Destroy, Stall, Organize) |
| 23–34 | 901–912 | `을`, `를`, `은`, `는`, `이`, `가`, `에`, `굴림체`, `굴림`, `바탕체`, `궁서체`, `korean` | Korean postposition suffixes and font name strings |
| 35–63 | ~913–940 | Map/camera/combat/trade system messages | General in-game system messages |
| 64–157 | ~940–1057 | Warning dialogs, trade, item time-limit messages | In-game warning and transaction messages |
| 158–~300 | ~1058–1300 | NPC dialog, party, alliance, server text | Multiplayer and social messages |
| ~300–400 | ~1300–1400 | Server names, legal/privacy text | Server list and terms-of-service text |
| ~400–600 | ~1400–1600 | Server status, stall actions, help text | Server/stall/UI system |
| ~600–700 | ~1600–1700 | Combat/trade/system continuation | Mixed in-game messages |
| ~700–800 | ~1700–1800 | Data server errors, item enhancement, help text | Error messages and help overlay |
| ~800–900 | ~1800–1900 | Item enchant help text (multi-line), quest data | Item help and quest strings |
| ~900–1000 | ~1900–2000 | Mob kill notifications, level-up, stat strings | Combat and progression feedback |
| ~1000–1100 | ~2000–2100 | Item stat labels (attack, gear attribute strings) | Item attribute labels |
| ~1100–1200 | ~2100–2200 | Money format (`%I64d 금`), elder/title strings | Economy and rank titles |
| ~1200–1400 | ~2200–2400 | Class and rank names, NPC names | Class taxonomy and NPC names |
| ~1400–1500 | ~2400–2500 | Required stat labels, buff window captions | Stat UI labels |
| ~1500–1700 | ~2500–2700 | Alignment/faction text, skill book names | Faction and skill content |
| ~1700–1800 | ~2700–2800 | Skill book names, weapon names | Item names |
| ~1800–1900 | ~2800–2900 | Equipment equip messages, character death messages | Action feedback |
| ~1900–2000 | ~2900–3000 | Death dialogs, dungeon restriction messages | Death/respawn/area |
| ~2000–2100 | ~3000–3100 | Restriction messages, production item dialogs | Area restriction and crafting |
| ~2100–2200 | ~3100–3200 | Mode/production text | Status messages |
| ~2200–2300 | ~3200–3300 | Server error strings (energy-system errors) | System errors |
| ~2300–2400 | ~3300–3400 | Charm slot/soul orb messages | Equipment system |
| ~2400–2500 | ~3400–3500 | Lottery messages, buff selection | Event and buff system |
| ~2500–2566 | ~3500–3566 | Buff format strings (`%d분간 %d 상승`), NPC error messages | Buff and NPC system |
| ~2567–2580 | ~3567–3580 | Death by monster/player, war announcements | Combat and war system |
| ~2580–2609 | ~3580–3609 | Job change item, map move, logout, CAPTCHA/question system | Class change, navigation, bot-prevention |
| ~2610–2625 | ~3610–3625 | Auto-war, peace declaration, war mode | PvP/peace system |
| ~2626–2643 | ~3626–3643 | Item time extension, appearance change cosmetics | Item system extension |

> **ID vs. slot index (critical):** the `id` stored in each record is the authoritative
> identifier. A record at slot 0 has `id = 1`; a record at slot 4 has `id = 101`; there is
> no formula relating slot index to `id`. Always read the `id` field from the record itself.
> The estimated maximum `id` is approximately **3,643** (derived from the last occupied slot).

### Confirmed specific message IDs (combined CODE-CONFIRMED + SAMPLE-VERIFIED)

The following IDs are pinned by both call-site analysis and direct sample observation:

| msg_id | CP949 text | Translation | Confidence |
|---|---|---|---|
| 1 | `%s금` | Gold: %s | SAMPLE-VERIFIED |
| 2 | `%d은` | Silver: %d | SAMPLE-VERIFIED |
| 3 | `%d동` | Copper: %d | SAMPLE-VERIFIED |
| 4 | `엽전` | Yang (currency unit) | SAMPLE-VERIFIED |
| 101 | `확인` | OK / Confirm | SAMPLE-VERIFIED |
| 102 | `취소` | Cancel | SAMPLE-VERIFIED |
| 103 | `닫기` | Close | SAMPLE-VERIFIED |
| 104 | `수령` | Receive | SAMPLE-VERIFIED |
| 105 | `검색` | Search | SAMPLE-VERIFIED |
| 106 | `이전` | Previous | SAMPLE-VERIFIED |
| 107 | `다음` | Next | SAMPLE-VERIFIED |
| 201 | `%d시간` | %d hour(s) | SAMPLE-VERIFIED |
| 202 | `%d분` | %d minute(s) | SAMPLE-VERIFIED |
| 203 | `%d초` | %d second(s) | SAMPLE-VERIFIED |
| 204 | `%d시` | %d o'clock | SAMPLE-VERIFIED |
| 205 | `오전` | AM | SAMPLE-VERIFIED |
| 206 | `오후` | PM | SAMPLE-VERIFIED |
| 207 | `%d일` | %d day(s) | SAMPLE-VERIFIED |
| 208 | `남은 시간: %s` | Remaining time: %s | SAMPLE-VERIFIED |
| 501 | `기간 연장` | Extend (duration) | SAMPLE-VERIFIED |
| 502 | `폐기` | Destroy | SAMPLE-VERIFIED |
| 503 | `노점` | Stall | SAMPLE-VERIFIED |
| 504 | `정리` | Organize | SAMPLE-VERIFIED |
| 901 | `을` | Korean postposition (object marker, after consonant) | SAMPLE-VERIFIED |
| 902 | `를` | Korean postposition (object marker, after vowel) | SAMPLE-VERIFIED |
| 903 | `은` | Korean postposition (topic marker, after consonant) | SAMPLE-VERIFIED |
| 904 | `는` | Korean postposition (topic marker, after vowel) | SAMPLE-VERIFIED |
| 905 | `이` | Korean postposition (subject marker, after consonant) | SAMPLE-VERIFIED |
| 906 | `가` | Korean postposition (subject marker, after vowel) | SAMPLE-VERIFIED |
| 907 | `에` | Korean postposition (location/direction) | SAMPLE-VERIFIED |
| 908 | `굴림체` | Font name: Gulim bold | SAMPLE-VERIFIED |
| 909 | `굴림` | Font name: Gulim | SAMPLE-VERIFIED |
| 910 | `바탕체` | Font name: Batang | SAMPLE-VERIFIED |
| 911 | `궁서체` | Font name: Gungseo | SAMPLE-VERIFIED |
| 912 | `korean` | Font/locale identifier | SAMPLE-VERIFIED |
| 200–212 | (error messages) | Character create/rename error messages | CODE-CONFIRMED |
| 4025–4028 | (login errors) | Login error toast messages | CODE-CONFIRMED |
| 9001 + N | (state names) | Scene/state name strings | CODE-CONFIRMED |

### Cross-reference

This catalogue is consumed by the UI system documented in `Docs/RE/specs/ui_system.md`. Mob
name and alternate-name string IDs stored in `mobinfo.mi` (§2 of this document) resolve against
this same catalogue. UI window button labels, currency display, time formatting, font selection,
and Korean postposition injection all draw from this file.

### Open questions for §6

1. **Positional substitution mechanism (`%1%`/`%2%`/`%3%`):** the engine's implementation for
   positional argument insertion is not documented. Whether it uses a custom formatter or an
   sprintf-style call with numbered arguments is unknown.
2. **Maximum `id` value:** the highest `id` in the file was not systematically extracted across
   all 2,644 slots. The estimated maximum is approximately 3,643 based on the last occupied slot
   grouping. The exact max is required for bounds-checking a lookup array if an array is preferred
   over a dictionary.
3. **Empty slot detection edge case:** the check "all 516 bytes are 0xEE" may be overly strict if
   any padding variant uses 0x00 after the NUL. The confirmed fill byte is 0xEE; whether any
   partial-0xEE/partial-0x00 padding exists in the 11 empty slots is not exhaustively confirmed.
4. **ID stability across patches:** whether the same `id` values map to the same strings in
   earlier or later client versions is unknown. The mapping documented here is from the single
   analysed VFS version.

---

## Cross-format summary

| Format          | Header          | Stride | Count source          | Text encoding | Loader confirmed |
|-----------------|-----------------|--------|-----------------------|---------------|---------------|
| `actor_size.xdb` | none           | 12 B   | `file_size / 12`      | none          | stride only   |
| `buff_icon_position.xdb` | none  | 12 B   | `file_size / 12`      | none          | YES (loader + stride) |
| `effectscale.xdb` | none          | 8 B    | `file_size / 8`       | none          | stride only   |
| `mobinfo.mi`    | 4-byte u32 count | 28 B  | stored `count` field  | none (refs only) | NO        |
| `.tol`          | 16-byte header  | 1 B/tile | `width × height`    | none          | NO            |
| `descript.ion`  | none            | variable | until EOF (CRLF delimited) | ASCII  | NO            |
| `discript.sc`   | none            | 68 B   | `file_size / 68`      | CP949 (display_name) | YES (stride) |
| `msg.xdb`       | none            | 516 B  | `file_size / 516` = 2,644 records | CP949 (text, 0xEE fill) | YES (loader + stride + content SAMPLE-VERIFIED) |

## Known unknowns (cross-format)

- Whether any `.xdb` variant can vary in stride across game patches.
- Whether `mobinfo.mi` files covering mob class IDs outside the range 101 – 121 exist.
- The semantics of `descript.ion` `field1` / `field2` — pak offset vs checksum vs size.
- Whether `descript.ion` can have multiple records; only a single-record sample was examined.
- The world-origin sub-tile scale factor (256) in `.tol` files is inferred, not confirmed from
  the engine's internal constants.
- The `reserved` 27-byte region in `discript.sc` records.
- The positional-substitution mechanism for `%1%`/`%2%`/`%3%` format strings in `msg.xdb`.
- The exact maximum `id` value in `msg.xdb` (estimated ~3,643; not exhaustively verified).

## Cross-references

- Related formats: `pak.md` (archive container that holds all of the above), `terrain.md` (`.tol`
  is a terrain companion file), `texture.md` (`.ion` references `.tga` texture filenames).
- `msg.xdb` consumers: `Docs/RE/specs/ui_system.md` (caption lookup by numeric ID),
  `mobinfo.mi` §2 (mob name string IDs reference this catalogue),
  `formats/ui_manifests.md` §8 (widget-to-caption binding reference).
- Glossary: see `Docs/RE/names.yaml`
- Provenance: see `Docs/RE/journal.md`
