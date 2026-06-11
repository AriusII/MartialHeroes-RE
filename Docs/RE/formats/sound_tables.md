# Format: .eff / .wlk / .run / .bgm / .bge  (per-map sound event and music schedule tables)

> Clean-room spec. Neutral description only — NO sample bytes, NO decompiler pseudo-code.
> Consumed by Assets.Parsers. Every offset an engineer cites must reference this file.
>
> status: sample_verified
> sample_verified: true  (file size, entry count, stride, entry field layout, and audio container
>                         formats all confirmed against real sample files — 2026-06-11)

---

## CRITICAL DISAMBIGUATION — .eff is NOT a single-purpose format

The `.eff` extension is **reused for two unrelated file types** in this engine, distinguished
only by VFS directory path:

| Path pattern | File type |
|---|---|
| `data/map*/soundtable*.eff` | Per-map sound trigger schedule table (256 × 48 B, same layout as .wlk/.run/.bgm/.bge) |
| `data/effect/obj/*.eff` | 3D triangle mesh collision / area shape (variable length, starts with a u32 triangle count) |

An engineer must never assume a `.eff` file is a sound table without first confirming the
VFS path prefix. Do NOT open `data/effect/obj/*.eff` as a sound table. See Section 6 for
the geometry shape variant.

The `.xeff` extension (used by the particle/visual-effect system) is a completely separate
third format and is unrelated to either `.eff` variant above.

---

## Identification

- **Extensions:** `.wlk`, `.run`, `.bgm`, `.bge`, `.eff` (sound table variant only)
- **Found in:**
  - `data/map<id>/soundtable<id>.<ext>` — runtime (in-game map variant)
  - `tool/sound/soundtable<id>.<ext>` — editor / tool variant
- **Magic / signature:** none — no file-level magic or version header; first byte of a
  well-formed file is the low byte of the `sound_entry_id` u32 at entry index 0
- **Endianness:** little-endian (confirmed: f32 1.0 stored as `00 00 80 3F`)
- **File size:** fixed 13312 bytes (0x3400) — confirmed across 12 real samples

---

## Semantic mapping of the five sound-table extensions

All five files for a given map share identical binary structure and identical size. They
differ only in the category of sound event they carry:

| Extension | Semantic role | Terrain-cell index byte(s) |
|---|---|---|
| `.wlk` | Walk footstep sounds | Derived from character world position (separate code path; not observed in the ambient-update function) |
| `.run` | Run footstep sounds | Derived from character world position (same note as `.wlk`) |
| `.bgm` | Background music zones | Mud-cell byte at cell offset +2 |
| `.bge` | Looped ambient sound effects | Mud-cell bytes at cell offsets +3 and +4 (up to 2 simultaneous per cell) |
| `.eff` (sound table) | Triggered point-source sound events | Mud-cell bytes at cell offsets +5, +6, and +7 (up to 3 simultaneous per cell) |

The `.wlk` and `.run` files in all available samples contain only null entries (sound_entry_id
= 0 in every slot). The terrain-cell indexing mechanism for footstep tables is confirmed to
exist but was not directly observed in the ambient-update runtime path.

---

## File layout

### Overall structure

| Region | Offset | Size (bytes) | Notes |
|---|---:|---:|---|
| Sound entry table | 0 (0x0000) | 12288 (0x3000) | 256 entries × 48 bytes; this is the only region read by the runtime loader |
| Editor metadata | 12288 (0x3000) | 1024 (0x400) | Written by the map editor tool; ignored at runtime |
| **Total on disk** | — | **13312 (0x3400)** | Confirmed across all 12 sample files |

The runtime loader opens the file and reads exactly 12288 bytes starting at offset 0. The
trailing 1024-byte editor metadata region is never consumed at runtime.

### Entry count

Fixed: **256 entries**. There is no entry-count field anywhere in the file; the loader always
reads exactly 256 × 48 = 12288 bytes. Entry index 0 is the null/disabled sentinel — a terrain
cell byte value of 0 means "no sound assigned", so the slot at index 0 is never the target of
a meaningful lookup.

---

## Per-entry layout (48 bytes, little-endian throughout)

Confidence levels reflect triangulation of two independent sources: runtime access-pattern
analysis and direct observation of 12 sample files.

| Offset | Size | Type | Field | Notes | Confidence |
|-------:|-----:|------|-------|-------|------------|
| +0x00 | 4 | u32 | `sound_entry_id` | Numeric resource key; 0 = empty/unassigned slot. Active samples carry 9-digit decimal values (see Sound ID section). | CONFIRMED |
| +0x04 | 24 | u8[24] | `hour_schedule[24]` | One flag byte per in-game hour. `hour_schedule[h]` non-zero → sound active during hour h. h = game_clock_seconds / 3600 (integer division). All 12 samples have every byte = 0x01 (unconditionally active). | CONFIRMED (structure and access pattern); value variation UNOBSERVED in samples |
| +0x1C | 4 | f32 | `weight` | Always 1.0f in all observed samples. Not accessed in the observed runtime playback path. Likely a blend weight or priority scalar initialized to 1.0 by the editor. | SAMPLE-CONFIRMED as 1.0f; semantic UNVERIFIED |
| +0x20 | 4 | f32 | `pos_x` | World-space X coordinate of the 3D DirectSound source. Passed directly to `IDirectSound3DBuffer::SetPosition` as the X argument. 0.0 in all observed samples (BGM/ambient sources may be non-positional). | CONFIRMED (runtime semantic); observed value 0.0f in all samples |
| +0x24 | 4 | u32 | `unknown_36` | Not accessed in the observed runtime playback path. Values across samples: 0x00000000 (most entries), 0x00000001 (one active entry), editor-uninitialized fill pattern (several entries). Purpose UNRESOLVED. | UNRESOLVED |
| +0x28 | 4 | f32 | `pos_z` | World-space Z coordinate of the 3D DirectSound source. Passed to `IDirectSound3DBuffer::SetPosition` as the Z argument. The Y component used for SetPosition is taken from the player's current world Y, not from this table. 0.0 in all observed samples. | CONFIRMED (runtime semantic); observed value 0.0f in all samples |
| +0x2C | 4 | f32 | `volume_factor` | Multiplied by 0.7 before being passed to the DirectSound volume control. 0.0f in all observed samples (consistent with unassigned slots; active localized sounds would carry a positive value). | CONFIRMED (f32 type and scaling factor); observed value 0.0f in all samples |

**Entry stride: 48 bytes. Confirmed.** (256 × 48 = 12288 = loader read size.)

### Byte-level field map (quick reference)

```
[+0x00..+0x03]  sound_entry_id   u32 LE    (0 = null/unassigned)
[+0x04..+0x1B]  hour_schedule    u8 × 24   (index h = game_seconds / 3600; non-zero = active)
[+0x1C..+0x1F]  weight           f32 LE    (always 1.0f observed; semantic unverified)
[+0x20..+0x23]  pos_x            f32 LE    (world-space X for DirectSound3D; 0.0 in samples)
[+0x24..+0x27]  unknown_36       u32 LE    (not accessed at runtime; purpose unresolved)
[+0x28..+0x2B]  pos_z            f32 LE    (world-space Z for DirectSound3D; 0.0 in samples)
[+0x2C..+0x2F]  volume_factor    f32 LE    (scaled × 0.7 before DS volume; 0.0f in samples)
```

---

## Sound ID semantics

Active `sound_entry_id` values observed in samples are 9-digit decimal integers:

- 910022000
- 910033000
- 910034000
- 910053002
- 920100200

All share a high byte of 0x36 (decimal 54). The pattern is consistent with a Korean MMORPG
resource catalog key of the form `<type> × 10^8 + <sequential_id>`, where the upper digits
encode asset category. The engine constructs the audio file path as:

```
data/sound/3d/<sound_entry_id>.ogg
```

The `sound_entry_id` integer is formatted as a plain decimal string (no zero-padding, no
prefix) to form the filename stem. How the engine resolves IDs that correspond to `.wav`
files rather than `.ogg` files is not determined; the path-construction format string uses
`.ogg` unconditionally in the observed runtime code. WAV files in `data/sound/3d/` may be
loaded through a different call path.

---

## Terrain-cell indexing

The terrain system stores a cell record for each map tile in `.mud` format (not yet fully
spec'd). The sound table is addressed by bytes within the mud cell record:

| Extension | Cell offset(s) used as entry index | Max simultaneous sounds per cell |
|---|---|---|
| `.bgm` | byte at mud-cell +2 | 1 |
| `.bge` | bytes at mud-cell +3 and +4, used as separate indices | 2 |
| `.eff` (sound table) | bytes at mud-cell +5, +6, and +7, used as separate indices | 3 |
| `.wlk` / `.run` | derived from character world-space position; specific formula UNVERIFIED | 1 (presumed) |

A cell byte value of 0 selects entry index 0, which is the null sentinel (sound_entry_id =
0). The runtime checks the cell byte before performing any table lookup and skips the entry
if the byte is zero.

---

## Editor metadata region (bytes 12288–13311, 1024 bytes)

This region is written by the map editor tool and is not read by the runtime loader.

Observed partial structure across samples:

| Offset within region | Size | Notes |
|---------------------:|-----:|-------|
| +0x00 | 4 | Always 0x00000000 in all observed files. Possible format version or reserved field. |
| +0x04 | 4 | Varies: 0, 1, 14, or 50 across samples. Editor-internal state (cursor position, last-edited entry, or similar). |
| +0x08 | 4 | Values: 0 or 32. Possibly an editor view parameter or entry count. |
| +0x0C onward | 1012 | All zero in all observed samples. |

The exact semantics of the +0x04 and +0x08 fields are UNRESOLVED. They do not obviously
correlate with active entry count, entry indices, or map identifiers across the sample set.

---

## Section 6 — data/effect/obj/*.eff — 3D geometry shape format

Files under `data/effect/obj/` that carry the `.eff` extension are **triangle mesh
collision/area shapes**, not sound tables. They are structurally and semantically unrelated
to the per-map sound table format described above.

These files carry NO file-level magic. Their format begins immediately with a triangle count:

| Offset | Size | Type | Field | Notes | Confidence |
|-------:|-----:|------|-------|-------|------------|
| 0 | 4 | u32 LE | `triangle_count` | Number of triangles in the mesh | CONFIRMED from samples |
| 4 | variable | u16[] | index array | `triangle_count × 3 × u16` for triangle list topology | CONFIRMED (cone, rect); ambiguous for tringle.eff |
| after indices | variable | f32[] | vertex position array | `vertex_count × 3 × f32` (XYZ per vertex) | CONFIRMED (cone, rect) |

Observed samples: `cone.eff` (496 bytes, 36 triangles, 23 vertices), `rect.eff` (148 bytes,
6 triangles, 9 vertices), `tringle.eff` (110 bytes — index/vertex boundary ambiguous; may
use a triangle-strip variant with fewer indices). The full encoding variant for `tringle.eff`
is UNRESOLVED.

A dedicated `formats/eff_geometry.md` spec should be produced once the geometry-shape
loader is traced.

---

## Section 7 — Audio container formats (.ogg and .wav)

Sound files referenced by `sound_entry_id` values in these tables are stored under
`data/sound/2d/` and `data/sound/3d/` as **standard, unmodified audio containers**. No
proprietary header, no encryption, and no additional framing wraps these files.

### 7.1 Ogg Vorbis (.ogg)

- **Magic:** ASCII `OggS` at file offset 0 (bytes `4F 67 67 53`) — standard Ogg capture pattern
- **Format:** Standard Ogg Vorbis as defined by RFC 3533 and the Vorbis I specification.
  Directly decodable by any Ogg Vorbis decoder without preprocessing.
- **Observed codec parameters (all three samples are identical):**
  - Channels: 1 (mono)
  - Sample rate: 22050 Hz
  - Nominal bitrate: 16000 bps
  - Encoder: Xiph.Org libVorbis I, build 2002-07-17
  - No embedded comment tags
- **VFS path pattern:** `data/sound/3d/<sound_entry_id>.ogg` (3D positional),
  `data/sound/2d/<sound_entry_id>.ogg` (2D non-positional; path confirmed from engine strings,
  no sample files observed from 2D directory)
- **Filename scheme:** plain decimal integer stem matching `sound_entry_id`, no zero-padding
  (e.g. `841390513.ogg`)
- **sample_verified: true** — three `.ogg` samples confirmed from `data/sound/3d/`
- **Max decoded PCM size:** the engine allocates a 512 KiB (524288 bytes) decode buffer.
  At 22050 Hz mono 16-bit, this accommodates approximately 11.9 seconds of audio. The engine
  logs an error if decoded PCM exceeds this limit.

### 7.2 RIFF/WAVE PCM (.wav)

- **Magic:** ASCII `RIFF` at file offset 0 (bytes `52 49 46 46`), form type `WAVE`
  at offset 8 (bytes `57 41 56 45`) — standard RIFF container
- **Format:** Standard RIFF/WAVE PCM (WAVE_FORMAT_PCM, audio_format = 1). Directly
  playable by any standard WAV decoder without preprocessing.
- **Observed codec parameters (one sample):**
  - Channels: 1 (mono)
  - Sample rate: 22050 Hz
  - Bit depth: 16-bit signed little-endian PCM
  - Byte rate: 44100 bytes/s
  - Duration: approximately 1.07 seconds
  - No `fact` chunk; `fmt` chunk is exactly 16 bytes (no WAVEFORMATEX extension)
- **VFS path pattern:** `data/sound/3d/<sound_entry_id>.wav` (one sample observed in the 3D
  directory; 2D directory .wav presence UNVERIFIED)
- **sample_verified: true** — one `.wav` sample confirmed from `data/sound/3d/`

### 7.3 Engine loading notes

The engine selects the `data/sound/2d/` or `data/sound/3d/` directory based on a type byte
in the internal sound descriptor object. When the VFS is mounted, audio data is loaded from
the VFS buffer and decoded using Ogg Vorbis callbacks (`ov_open_callbacks`). When the VFS is
not mounted (editor / development mode), the engine falls back to direct filesystem `fopen`.
3D sounds use DirectSound3D (`IDirectSound3DBuffer`) for spatialisation; 2D sounds do not.

A streaming playback path exists for long audio (BGM tracks), which re-opens the Ogg stream
in place rather than fully decoding it. No streaming-specific file format differences apply;
the `.ogg` container format is identical.

---

## .xeff files (visual/particle effects — separate format, not this spec)

`.xeff` files are the particle and visual effect format. They are entirely distinct from all
`.eff` variants above.

- **VFS path:** `data/effect/xeff/<name>.xeff`; an index file `data/effect/xeffect.lst`
  enumerates available assets
- **Magic:** ASCII `XEFF` at file offset 0 — confirmed from an engine error string
- **Status:** NOT documented in this spec; a dedicated `formats/xeff.md` should be
  produced once the loader is traced

---

## Known unknowns

1. **`unknown_36` at +0x24** — not accessed in the observed runtime playback path. Observed
   values include 0, 1, and the MSVC debug-fill pattern 0xCCCCCCCC (editor artifact indicating
   the field was written to disk uninitialized). Purpose UNRESOLVED.

2. **`weight` at +0x1C semantic** — always 1.0f in all 12 samples; not accessed in the
   observed runtime path. Likely a blend weight, priority, or reserved scalar. Naming is
   tentative.

3. **3D position units** — whether `pos_x` / `pos_z` use the same world-space unit scale as
   the terrain grid is not established; all values are 0.0f in available samples.

4. **sound_entry_id → filename resolution for .wav** — the engine path-construction code uses
   a `.ogg` format string unconditionally in the observed path. How `.wav` files are resolved
   (separate code path, catalog table, or runtime fallback) is UNVERIFIED.

5. **sound_entry_id integer encoding** — whether the 9-digit decimal values represent a
   direct catalog key, a CRC32, or a composite type+sequence integer is not established.

6. **Walk/run indexing formula** — the precise mapping from character world position to a
   `.wlk` / `.run` entry index is not observed; these tables contain only null entries in all
   available samples.

7. **Hour-schedule variation** — every schedule byte in every sample is 0x01. The conditional
   time-of-day muting behavior is confirmed from access-pattern analysis but has not been
   exercised by the available samples.

8. **Editor metadata region** (+0x3000..+0x33FF) — the values at region+0x04 and +0x08 are
   unexplained; the region is unused at runtime.

9. **Per-map .eff soundtable samples** — no `data/map*/soundtable*.eff` files were present in
   the sample set. The soundtable `.eff` format is confirmed as structurally identical to
   `.bgm`/`.bge` from access-pattern analysis, but byte-level verification against a real
   `.eff` soundtable is outstanding.

10. **`data/effect/obj/tringle.eff` encoding variant** — whether this file uses a
    triangle-strip or triangle-list index scheme is ambiguous from its size alone. Requires
    a trace of the effect-geometry loader.

11. **2D audio samples** — no files from `data/sound/2d/` were available. Whether this
    directory contains `.ogg` only, `.wav` only, or both is UNVERIFIED from samples.

12. **BGM streaming directory** — whether BGM tracks reside in `data/sound/2d/`,
    `data/sound/3d/`, or a dedicated directory (e.g. `data/sound/bgm/`) is UNVERIFIED.

---

## Cross-references

- Terrain cell format (source of the index bytes into this table): terrain / `.mud` format
  spec (not yet written; see `Docs/RE/formats/`)
- Effect geometry shape variant: `data/effect/obj/*.eff` — a future `formats/eff_geometry.md`
- Visual/particle effects: `data/effect/xeff/` — a future `formats/xeff.md` (magic "XEFF")
- VFS container layout: `Docs/RE/formats/pak.md`
- Configuration and catalogue tables sharing the VFS: `Docs/RE/formats/config_tables.md`
- Glossary: `Docs/RE/names.yaml`
- Provenance: `Docs/RE/journal.md`
