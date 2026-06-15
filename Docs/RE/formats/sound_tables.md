# Format: .eff / .wlk / .run / .bgm / .bge  (per-map sound event and music schedule tables)

> Clean-room spec. Neutral description only — NO sample bytes, NO decompiler pseudo-code.
> Consumed by Assets.Parsers. Every offset an engineer cites must reference this file.
>
> status: sample_verified
> sample_verified: true  (file size, entry count, stride, entry field layout, and audio container
>                         formats all confirmed against real sample files — 2026-06-11;
>                         on-disk 48-byte record stride two-witness-confirmed — 2026-06-15:
>                         the loader advances 48 bytes per record across 256 records and reads
>                         exactly 12288 bytes, leaving a 1024-byte unread trailer.)

---

## CRITICAL DISAMBIGUATION — .eff is NOT a single-purpose format

The `.eff` extension is **reused for two unrelated file types** in this engine, distinguished
only by VFS directory path:

| Path pattern | File type |
|---|---|
| `data/map*/soundtable*.eff` | Per-map sound trigger schedule table (256 records × 48 B read by the loader, then a 1024-byte unread trailer; same layout as .wlk/.run/.bgm/.bge) |
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
  - `data/map<id>/soundtable<id>.<ext>` — runtime (in-game map variant). `<id>` is a
    zero-padded 3-digit area number matching the owning cell's `.mud` path (e.g. `map001`,
    `soundtable001.bgm` for area 1; `map000` / `soundtable000.*` for the global / lobby area).
  - `tool/sound/soundtable<id>.<ext>` — editor / tool variant
- **Table inventory (VFS census, 2026-06-14):** five table extensions × ~60 areas =
  ~300 per-area binary tables, plus the `map000` global set. Every table is binary (never
  CP949 text) and every table is exactly 13312 bytes. — confidence: SAMPLE-VERIFIED
- **Magic / signature:** none — no file-level magic or version header; first byte of a
  well-formed file is the low byte of the `sound_entry_id` u32 at record index 0
- **Endianness:** little-endian (confirmed: f32 1.0 stored as `00 00 80 3F`)
- **File size:** fixed 13312 bytes (0x3400) — confirmed across 12 runtime samples (2026-06-11)
  and re-confirmed across all ~300 tables by VFS census (2026-06-14). Of this, the loader reads
  only the first 12288 bytes (0x3000); see File layout.

---

## Semantic mapping of the five sound-table extensions

All five files for a given map share identical binary structure and identical size. They
differ only in the category of sound event they carry:

| Extension | Semantic role | Terrain-cell index byte(s) |
|---|---|---|
| `.wlk` | Walk footstep sounds | PLAUSIBLY mud-cell byte at cell offset +0 (see `mud.md` bytes-0/1 hypothesis; UNVERIFIED) |
| `.run` | Run footstep sounds | PLAUSIBLY mud-cell byte at cell offset +1 (see `mud.md` bytes-0/1 hypothesis; UNVERIFIED) |
| `.bgm` | Background music zones | Mud-cell byte at cell offset +2 |
| `.bge` | Looped ambient sound effects | Mud-cell bytes at cell offsets +3 and +4 (up to 2 simultaneous per cell) |
| `.eff` (sound table) | Triggered point-source sound events | Mud-cell bytes at cell offsets +5, +6, and +7 (up to 3 simultaneous per cell) |

The `.wlk` and `.run` files in all available samples contain only null records (sound_entry_id
= 0 in every slot). The terrain-cell indexing mechanism for footstep tables is confirmed to
exist but its index source in the `.mud` tile was not directly observed; the leading two mud
bytes are the leading hypothesis — see the cross-reference to `mud.md`.

---

## File layout

### Overall structure — loader stride reconciliation (two-witness)

A 2026-06-15 two-witness reconciliation (a static loader reading plus an independent black-box
file measurement) settled the record stride. The loader advances **48 bytes (0x30)** per record
and iterates over **256 records**, reading exactly **12288 bytes (0x3000 = 256 × 48)** from the
start of the file. The remaining **1024 bytes** of the fixed 13312-byte file form a trailer that
the loader never reads.

This **corrects** an earlier (2026-06-14) on-disk reading that proposed a uniform 52-byte stride
with no trailer (256 × 52 = 13312). That 52-byte figure is wrong: the loader's per-record advance
is 48 bytes, not 52, and the final 1024 bytes are a separate unread region rather than a 4-byte
per-record tail. Both readings reconcile to the same 13312-byte total file size, and both agree on
the field layout of the first 0x2C bytes of every record (see the per-record table below). The
48-byte record stride plus the 1024-byte unread trailer is the authoritative layout for a parser.

| Region | Offset | Size (bytes) | Notes |
|---|---:|---:|---|
| Record table (read) | 0 (0x0000) | 12288 (0x3000) | 256 records × **48 bytes** — loader reads this region |
| Unread trailer | 12288 (0x3000) | 1024 (0x0400) | Present in every file; the loader never reads it. Purpose UNRESOLVED. |
| **Total on disk** | — | **13312 (0x3400)** | Confirmed across 12 runtime samples and ~300 census tables |

- **Record count source:** fixed at **256** — there is no count field; the loader iterates 256
  times. (12288 / 48 = 256, exact.) — confidence: CONFIRMED
- **Record stride:** **48 bytes**. — confidence: CONFIRMED (two-witness: loader advance + file
  measurement)

### Record index 0 — null sentinel

Record index 0 is the null/disabled sentinel — a terrain cell byte value of 0 means
"no sound assigned", so the slot at index 0 is never the target of a meaningful lookup.

---

## Per-record layout (48 bytes, little-endian throughout)

Confidence levels reflect triangulation of three sources: a static reading of the loader's
record-advance and field accesses, direct observation of 12 runtime samples (2026-06-11), and a
256-record field census of `.bgm` / `.bge` / `.eff` tables in area 001 (2026-06-14).

The record is exactly 48 bytes (offsets +0x00 .. +0x2F). There is no per-record tail beyond
+0x2F; the bytes formerly attributed to a per-record tail belong to the file-level 1024-byte
unread trailer (see File layout).

| Offset | Size | Type | Field | Notes | Confidence |
|-------:|-----:|------|-------|-------|------------|
| +0x00 | 4 | u32 | `sound_entry_id` | Numeric resource key; 0 = empty/unassigned slot. Active records carry 9-digit decimal values (range ~900000000..999999999; see Sound ID section). | CONFIRMED |
| +0x04 | 24 | u8[24] | `hour_schedule[24]` | One byte per slot, indexed 0..23. All runtime samples have every byte = 0x01; the area-001 census sees per-byte 0x00/0x01 patterns that vary by record. The gating consumer that reads this mask is a separate function not located statically, so the mask **semantics are DBG-pending** — do NOT assume an hour-of-day meaning until the consumer is confirmed live. | CONFIRMED (structure and presence); mask semantics DBG-pending |
| +0x1C | 4 | f32 | `weight` | Volume / attenuation / blend scalar. 1.0f (`00 00 80 3F`) for BGM and BGE records. Not accessed in the observed runtime playback path. | SAMPLE-VERIFIED type/value; semantic UNVERIFIED |
| +0x20 | 4 | f32 | `pos_x` | World-space X of the 3D source. Populated (non-zero) only in `.eff` (3D) records; 0.0 for BGM / BGE. Passed to the DirectSound 3D position as the X argument. | CONFIRMED (runtime semantic); EFF-only population SAMPLE-VERIFIED |
| +0x24 | 4 | — | `unlabeled_24` | The loader does NOT read these 4 bytes. The earlier `pos_y` label is incorrect — no read site assigns a meaning to this offset. Left unlabeled; observed values are EFF-record fills in one area but their role is unestablished. | NOT-READ by loader; meaning UNRESOLVED |
| +0x28 | 4 | f32 | `pos_z` | World-space Z of the 3D source. Populated only in `.eff` records; 0.0 for BGM / BGE. Passed to the DirectSound 3D position as the Z argument. | CONFIRMED (runtime semantic); EFF-only population SAMPLE-VERIFIED |
| +0x2C | 4 | f32 | `radius` | Audibility radius of the 3D source (formerly labelled `volume_factor`). Populated only in `.eff` records; 0.0 for BGM / BGE. For the BGM playback path the runtime applies a 0.7 volume scaling at a separate stage. | CONFIRMED f32 type; EFF radius role SAMPLE-VERIFIED area 001 |

**Record stride: 48 bytes. CONFIRMED.** (256 × 48 = 12288 = bytes read by the loader; the
remaining 1024 bytes are the unread file trailer.)

> Field-naming reconciliation: an earlier spec named offsets +0x20 / +0x28 / +0x2C as
> `pos_x` / `pos_z` / `volume_factor` and +0x24 as `unknown_36` (later mislabelled `pos_y`). The
> 2026-06-14 EFF field census resolved the runtime-read axes as +0x20 = X (read), +0x28 = Z
> (read), +0x2C = radius (read). The two-witness loader reading confirms +0x24 is NOT read by the
> loader on any path, so it carries no recovered position meaning — it is left unlabeled. These
> read fields are only populated by `.eff` (3D) records; BGM / BGE records leave them zero.

### Byte-level field map (quick reference)

```
[+0x00..+0x03]  sound_entry_id   u32 LE    (0 = null/unassigned)
[+0x04..+0x1B]  hour_schedule    u8 × 24   (per-byte 0x00/0x01 mask; consumer not located → semantics DBG-pending)
[+0x1C..+0x1F]  weight           f32 LE    (1.0f for BGM/BGE; blend/attenuation scalar)
[+0x20..+0x23]  pos_x            f32 LE    (3D world X; read; EFF records only, else 0.0)
[+0x24..+0x27]  unlabeled_24     4 bytes   (NOT read by the loader; meaning unresolved)
[+0x28..+0x2B]  pos_z            f32 LE    (3D world Z; read; EFF records only, else 0.0)
[+0x2C..+0x2F]  radius           f32 LE    (3D audibility radius; read; EFF records only, else 0.0)
--- end of 48-byte record; next record begins at +0x30 ---
```

After the 256th record the file carries a 1024-byte trailer the loader never reads (see File layout).

---

## Resolution chain — `.mud` tile → soundtable record → leaf audio

The `.mud` per-cell ambient-sound zone grid stores byte indices that select records in these
tables. The mud byte value is used as a **direct 0-based row index** into the table whose
extension matches the byte's role. A mud byte of 0 selects record index 0 (the null sentinel)
and plays nothing. (Cross-map census: max bgmZoneId = 10, max bgeAmbientId = 20, max effId = 154,
all well under the 256 records per table — SAMPLE-VERIFIED across 1578 `.mud` files.)

```
.mud tile byte +2 (bgmZoneId = N)
  └─► data/map<AAA>/soundtable<AAA>.bgm  record[N] (0-based), +0x00 sound_entry_id (u32 LE)
        └─► data/sound/2d/{sound_entry_id}.ogg          (BGM = 2D / stereo)

.mud tile bytes +3, +4 (bgeAmbientId0/1 = N)
  └─► data/map<AAA>/soundtable<AAA>.bge  record[N] (0-based), +0x00 sound_entry_id (u32 LE)
        └─► data/sound/2d/{sound_entry_id}.ogg          (BGE ambient = 2D)

.mud tile bytes +5, +6, +7 (effId0/1/2 = N)
  └─► data/map<AAA>/soundtable<AAA>.eff  record[N] (0-based), +0x00 sound_entry_id (u32 LE)
        └─► data/sound/3d/{sound_entry_id}.ogg          (EFF = 3D positional)
              3D source position = record +0x20 (X), +0x28 (Z) as f32 (note: +0x24 is NOT read)
              audibility radius  = record +0x2C as f32

.mud tile byte +0  (PLAUSIBLE walk index — UNVERIFIED)
  └─► data/map<AAA>/soundtable<AAA>.wlk  record[N] (0-based) → footstep walk sound

.mud tile byte +1  (PLAUSIBLE run index — UNVERIFIED)
  └─► data/map<AAA>/soundtable<AAA>.run  record[N] (0-based) → footstep run sound
```

`<AAA>` is the zero-padded 3-digit area number from the `.mud` path (e.g. `map001` for area 1).
The 0-based direct-index rule is SAMPLE-VERIFIED for BGM / BGE / EFF across the full area-001 mud
census (77 files) and globally corroborated by the cross-map census. The `.wlk` / `.run` index
source (mud bytes +0 / +1) is PLAUSIBLE only — see `mud.md` and the IDA cross-check request below.

---

## Sound ID semantics

Active `sound_entry_id` values observed in samples are 9-digit decimal integers in the range
~900000000 .. 999999999:

- 910022000
- 910033000
- 910034000
- 910053002
- 920100200

All share a high byte of 0x36 (decimal 54). The pattern is consistent with a Korean MMORPG
resource catalog key of the form `<type> × 10^8 + <sequential_id>`, where the upper digits
encode asset category. The `sound_entry_id` integer is formatted as a plain decimal string
(no zero-padding, no prefix) to form the filename stem.

**The audio directory is NOT universal — it depends on the table extension** (SAMPLE-VERIFIED +
CODE-CONFIRMED; cross-reference `specs/sound.md §13`):

| Table extension | Audio directory | Confidence |
|---|---|---|
| `.bgm` | `data/sound/2d/` | SAMPLE-VERIFIED: `.bgm` IDs confirmed present under `data/sound/2d/` |
| `.bge` | `data/sound/2d/` | SAMPLE-VERIFIED (2026-06-14): BGE IDs confirmed present under `data/sound/2d/` |
| `.eff` (sound table) | `data/sound/3d/` | SAMPLE-VERIFIED: `.eff` sound table IDs confirmed present under `data/sound/3d/` |
| `.wlk` | UNDETERMINED | All sampled `.wlk` records are null (sound_entry_id = 0); directory unverifiable |
| `.run` | UNDETERMINED | All sampled `.run` records are null; directory unverifiable |

The directory selection (`data/sound/2d/` vs `data/sound/3d/`) is encoded in the table
extension, not in the `sound_entry_id` value itself. The engine selects the directory via an
internal per-extension code path. For the `.bgm` case, the IDs in the range `910xxxxx` map
to BGM tracks (stereo, 44100 Hz Vorbis) which reside exclusively in `data/sound/2d/`. For
the `.eff` sound table case, the IDs map to short 3D positional effects stored in
`data/sound/3d/`.

How the engine resolves IDs that correspond to `.wav` files rather than `.ogg` files is
not determined; the principal path-construction code uses `.ogg` unconditionally. WAV files
in `data/sound/3d/` may be loaded through a different call path.

---

## Terrain-cell indexing (summary; full chain above)

The terrain system stores a per-tile record in `.mud` format (see `mud.md`). The sound table is
addressed by bytes within the mud tile:

| Extension | Mud-tile byte offset(s) used as record index | Max simultaneous sounds per tile | Confidence |
|---|---|---|---|
| `.bgm` | +2 | 1 | CONFIRMED |
| `.bge` | +3 and +4 (separate indices) | 2 | CONFIRMED |
| `.eff` (sound table) | +5, +6, +7 (separate indices) | 3 | CONFIRMED |
| `.wlk` | +0 (PLAUSIBLE) | 1 (presumed) | PLAUSIBLE / UNVERIFIED |
| `.run` | +1 (PLAUSIBLE) | 1 (presumed) | PLAUSIBLE / UNVERIFIED |

A mud byte value of 0 selects record index 0 (the null sentinel). The runtime checks the mud
byte before performing any table lookup and skips the slot if the byte is zero.

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
- **VFS path pattern:** `data/sound/3d/<sound_entry_id>.ogg` (3D positional — `.eff` table
  IDs); `data/sound/2d/<sound_entry_id>.ogg` (2D non-positional — `.bgm` / `.bge` table IDs; 178
  `.ogg` files confirmed in `data/sound/2d/`, SAMPLE-VERIFIED)
- **Filename scheme:** plain decimal integer stem matching `sound_entry_id`, no zero-padding
  (e.g. `841390513.ogg`)
- **sample_verified: true** — three `.ogg` samples confirmed from `data/sound/3d/`; directory
  `data/sound/2d/` additionally SAMPLE-VERIFIED (178 files, including stereo 44100 Hz BGM tracks)
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
- **Status:** documented in `formats/effects.md`

---

## Known unknowns

1. **1024-byte unread trailer** — the final 1024 bytes (file offset 0x3000 .. 0x33FF) are present
   in every file but the loader never reads them. Their content/purpose is UNRESOLVED — plausibly
   editor-side metadata or reserved padding. A faithful parser may skip them.

2. **`unlabeled_24` at +0x24** — these 4 bytes are NOT read by the loader on any path. The earlier
   `pos_y` / `unknown_36` labels are withdrawn; no recovered meaning is assigned. Candidate for an
   IDA cross-check only if a consumer is ever found.

3. **`weight` at +0x1C semantic** — 1.0f in all BGM / BGE samples; not accessed in the observed
   runtime path. Likely a blend weight, priority, or attenuation scalar. Naming tentative.

4. **`hour_schedule` per-byte mask semantics** — DBG-pending. Every byte is 0x01 in the runtime
   samples, but the area-001 census shows per-record 0x00/0x01 patterns that vary by record. The
   gating consumer that reads this mask is a separate function not located statically, so its
   meaning (time-of-day mask, sub-area enable mask, weather filter, …) stays opaque pending a live
   debugger confirmation. Do not implement a meaning for it.

5. **EFF 3D position units** — whether `pos_x` / `pos_z` and `radius` use the same world-space
   unit scale as the terrain grid is not established; verified populated only in EFF records of one
   area (001).

6. **`.wlk` / `.run` index source** — the mapping from the `.mud` tile to a `.wlk` / `.run`
   record index is UNVERIFIED. The leading hypothesis is mud byte +0 (walk) and +1 (run); see
   `mud.md`. All sampled `.wlk` / `.run` tables are entirely null, so the leaf audio directory is
   also undetermined. IDA / debugger cross-check requested.

7. **sound_entry_id integer encoding** — whether the 9-digit decimal values represent a direct
   catalog key, a CRC32, or a composite type+sequence integer is not established.

8. **sound_entry_id → filename resolution for .wav** — the engine path-construction code uses a
   `.ogg` format string unconditionally in the observed path. How `.wav` files are resolved is
   UNVERIFIED.

9. **Per-map .eff soundtable byte-level sample** — RESOLVED (2026-06-14): the area-001 `.eff`
   table was field-censused (256 records), confirming the read 3D-position axes plus radius.

10. **`data/effect/obj/tringle.eff` encoding variant** — triangle-strip vs triangle-list index
    scheme is ambiguous from size alone. Requires a trace of the effect-geometry loader.

---

## Provenance note — stride correction (preserved for audit)

The original (2026-06-11) interpretation, recovered from runtime access-pattern analysis and 12
all-2D runtime samples, described the file as a 256 × 48 = 12288-byte record table followed by a
1024-byte trailing region. A later (2026-06-14) black-box file measurement proposed a uniform
52-byte stride with no trailer (256 × 52 = 13312). The 2026-06-15 two-witness reconciliation — a
static reading of the loader's per-record advance together with an independent file measurement —
settled the matter in favour of the original split: the loader advances **48 bytes** per record,
iterates **256 records**, reads **12288 bytes (0x3000)**, and leaves a **1024-byte trailer
unread**. The 52-byte reading is therefore withdrawn, and the per-record `tail_unknown` it implied
does not exist. The first 0x2C bytes of every record are unaffected by the correction.

---

## Cross-references

- Source of the index bytes into these tables (per-cell ambient-sound zone grid): `formats/mud.md`
- Effect geometry shape variant: `data/effect/obj/*.eff` — a future `formats/eff_geometry.md`
- Visual/particle effects: `data/effect/xeff/` — `formats/effects.md` (magic "XEFF")
- VFS container layout: `Docs/RE/formats/pak.md`
- Sound-subsystem behaviour (directory resolution, streaming): `Docs/RE/specs/sound.md`
- Glossary: `Docs/RE/names.yaml`
- Provenance: `Docs/RE/journal.md`

---

## Names flagged for names.yaml (orchestrator to record)

- Formats: `soundtable_bgm`, `soundtable_bge`, `soundtable_eff`, `soundtable_wlk`,
  `soundtable_run` (per-area sound index tables)
- Struct: `SoundTableRecord` (48 bytes): `sound_entry_id` (u32 @+0x00), `hour_schedule` (u8[24]
  @+0x04), `weight` (f32 @+0x1C), `pos_x`/`pos_z` (f32 @+0x20/+0x28, EFF only), `radius`
  (f32 @+0x2C, EFF only), `unlabeled_24` (4 bytes @+0x24, not read by the loader)
- Constants: `SOUNDTABLE_FILE_SIZE = 13312`, `SOUNDTABLE_RECORD_COUNT = 256`,
  `SOUNDTABLE_RECORD_STRIDE = 48`, `SOUNDTABLE_READ_SIZE = 12288`,
  `SOUNDTABLE_TRAILER_SIZE = 1024`
