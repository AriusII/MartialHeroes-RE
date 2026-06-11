# Format: .eff / .wlk / .run / .bgm / .bge  (per-map sound event and music schedule tables)

> Clean-room spec. Neutral description only — NO sample bytes, NO decompiler pseudo-code.
> Consumed by Assets.Parsers. Every offset an engineer cites must reference this file.
>
> status: hypothesis
> sample_verified: false

---

## CRITICAL DISAMBIGUATION — .eff is NOT a visual-effect format

The `.eff` extension in this engine refers exclusively to **per-map sound event schedule
tables**. It does NOT contain particle or visual effect data. Visual effects use the `.xeff`
extension with a distinct binary magic (see Section 4). An engineer must never open a `.eff`
file expecting visual-effect content.

---

## Identification

- **Extensions:** `.wlk`, `.run`, `.bgm`, `.bge`, `.eff` (five variants, same binary layout)
- **Found in:** VFS paths `tool/sound/soundtable%s.<ext>` (editor / tool variant) and
  `data/map%s/soundtable%s.<ext>` (in-game map variant), where `%s` is a map identifier
- **Magic / signature:** none — no file-level magic or version header
- **Endianness:** little-endian

---

## Semantic mapping of the five extensions

All five files for a given map share identical binary structure and size. They differ only in
the type of sound event they schedule:

| Extension | Semantic role | Terrain indexing |
|---|---|---|
| `.wlk` | Walk footstep sounds (character walking) | Character position on terrain grid |
| `.run` | Run footstep sounds (character running) | Character position on terrain grid |
| `.bgm` | Background music zones | Terrain cell byte +2 |
| `.bge` | Looped ambient sound effects | Terrain cell bytes +3 and +4 |
| `.eff` | Triggered sound events | Terrain cell bytes +5, +6, and +7 |

The terrain cell format that supplies these index bytes is documented in the `.mud` / terrain
format spec (if available). The bytes cited above are offsets within a terrain cell record, not
within the sound table itself.

---

## File layout

### Overall structure

| Region | Offset | Size (bytes) | Notes |
|---|---:|---:|---|
| Sound entry table | 0 | 12288 (0x3000) | 256 entries × 48 bytes; engine reads this region |
| Editor metadata | 12288 | 1024 (0x400) | Ignored by the runtime loader; tool-only |
| **Total on disk** | — | **13312 (0x3400)** | Observed total; UNVERIFIED (no sample) |

The engine reads exactly 12288 bytes starting at offset 0. The trailing 1024 bytes are never
consumed at runtime.

### Entry count

Fixed: **256 entries**. There is no count field; the loader always reads exactly 256 × 48 bytes.

---

## Per-entry layout (48 bytes)

All field interpretations below are UNVERIFIED — they are inferred from access-site analysis,
not confirmed by sample inspection.

| Offset | Size | Type | Field | Notes | Confidence |
|-------:|-----:|------|-------|-------|------------|
| +0x00 | 4 | u32 | Sound / event identifier | Index into the runtime sound asset map | UNVERIFIED |
| +0x04 | 24 | u8[24] | Hour-of-day schedule flags | One flag byte per hour; index = `current_second / 3600` | UNVERIFIED |
| +0x1C | 4 | ? | Unknown field | Purpose UNVERIFIED | UNVERIFIED |
| +0x20 | 4 | u32 | Spatial parameter 0 | Likely position X or trigger radius | UNVERIFIED |
| +0x24 | 4 | ? | Unknown field | Purpose UNVERIFIED | UNVERIFIED |
| +0x28 | 4 | u32 | Spatial parameter 1 | Likely position Z | UNVERIFIED |
| +0x2C | 4 | float | Unknown float | Partial inference from access site | UNVERIFIED |

Total per entry: 48 bytes (verified from 256 × 48 = 12288 = bytes read by the loader).

The hour-of-day schedule array allows per-hour enable/disable of a sound: if the flag byte at
index `h` is non-zero, the sound is active during hour `h` of the in-game clock. The engine
derives the current hour as `current_second / 3600`, implying `current_second` is an integer
game clock value.

---

## Indexing — how the terrain selects entries

The terrain system provides a cell record for every map tile. The sound table is indexed by
fields embedded in the `.mud` terrain cell (see the terrain format spec):

| Extension | Cell byte(s) used as table index |
|---|---|
| `.bgm` | byte at cell offset +2 |
| `.bge` | bytes at cell offsets +3 and +4 |
| `.eff` | bytes at cell offsets +5, +6, and +7 |
| `.wlk` / `.run` | derived from the character's world-space position mapped to a terrain cell |

Whether the multi-byte indices for `.bge` and `.eff` are used as a combined value or as
separate lookups is UNVERIFIED.

---

## Section 4 — .xeff files (visual effects — separate format, not this spec)

`.xeff` files are the visual / particle effect format. They are entirely distinct from the
five sound-table extensions above.

- **VFS path:** `data/effect/xeff/<name>.xeff`; an index file `data/effect/xeffect.lst`
  enumerates available `.xeff` assets
- **Magic:** ASCII `XEFF` at file offset 0 (engine validates this; a mismatch produces an error)
- **Confidence:** magic value CONFIRMED from the engine error string; all other fields UNVERIFIED
- **Format:** NOT documented in this session; a dedicated `formats/xeff.md` spec should be
  produced once the loader is traced

---

## Known unknowns

1. **Field at +0x1C** — purpose unknown.
2. **Field at +0x24** — purpose unknown.
3. **Field at +0x2C** — type confirmed as float from an access-site comment; semantic unknown.
4. **Spatial parameter semantics** — whether the values at +0x20 and +0x28 are world-space
   coordinates, cell-grid indices, or something else.
5. **Multi-byte index semantics** for `.bge` (bytes +3, +4) and `.eff` (bytes +5, +6, +7) —
   whether they index as a combined integer or as separate lookups.
6. **Editor metadata region** (bytes 12288–13311) — structure unknown; ignored at runtime.
7. **Total on-disk size** — 13312 bytes is inferred from engine context; UNVERIFIED without a
   sample file.
8. **Walk/run indexing formula** — the precise mapping from character world position to a table
   entry index for `.wlk` / `.run` is UNVERIFIED.
9. **Sound asset ID semantics** — how the `u32` at +0x00 maps to an audio file or runtime
   sound handle.
10. **All .xeff fields** beyond the magic — format not traced; requires a dedicated spec.

---

## Cross-references

- Terrain cell format (source of the index bytes): terrain / `.mud` format spec (not yet written)
- Visual effects: `data/effect/xeff/` — a future `formats/xeff.md` spec (magic "XEFF")
- VFS container layout: `Docs/RE/formats/pak.md`
- Configuration and catalogue tables sharing the VFS: `Docs/RE/formats/config_tables.md`
- Glossary: `Docs/RE/names.yaml`
- Provenance: `Docs/RE/journal.md`
