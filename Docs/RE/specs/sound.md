---
status: confirmed
sample_verified: partial   # VFS census SAMPLE-VERIFIED; runtime logic CODE-CONFIRMED from binary
subsystems: [sound_runtime, audio_device, ambient_driver, sfx_router, worker_thread, volume_buses]
networked: false            # sound is purely client-side; no sound opcodes on the wire
encoding_note: audio file IDs are plain decimal integers; no text encoding concerns
---

# Sound subsystem (runtime audio engine)

> **Clean-room specification.** Neutral description only — no decompiler identifiers, no binary
> virtual addresses, no pseudo-code. Promoted from dirty-room analyst notes under EU Software
> Directive 2009/24/EC Art. 6 (decompilation solely to achieve interoperability). No decompiler
> output appears in this file. Re-expressed entirely in the spec-author's own words.
>
> **Ownership:** this spec owns the *runtime* behaviour of the audio engine. The *on-disk binary
> format* of the five per-map sound-schedule tables (`.bgm`, `.bge`, `.eff`, `.wlk`, `.run`) is
> owned by `formats/sound_tables.md`; field offsets and file layout there are the authority an
> engineer cites for those bytes. This file describes how the runtime reads and plays those tables.
>
> **Relation to `specs/client_runtime.md`:** section §1 of that file is an accurate high-level
> summary. This document supersedes it in detail — all measurements here are CODE-CONFIRMED and
> SAMPLE-VERIFIED as indicated. Engineers implementing the sound layer should use this file as
> the primary reference and treat `client_runtime.md §1` as a summary cross-link only.

---

## Status banner

| Area | Confidence |
|---|---|
| Audio API (DirectSound only, no third-party middleware) | CODE-CONFIRMED |
| Codec (statically linked Ogg Vorbis 1.3.2; all clips `.ogg`) | CODE-CONFIRMED |
| Class family (`SoundManager`, `GSound`, `GSoundOGG`, `GSoundThread`) | CODE-CONFIRMED (RTTI + log strings) |
| Device init: PRIORITY coop level, 3D listener, five WAVEFORMATEX templates | CODE-CONFIRMED |
| Codec rule: 3D MUST be mono, 2D MUST be stereo | CODE-CONFIRMED |
| 512 KiB scratch / 1 MiB streaming ring; 3D over-size reject | CODE-CONFIRMED |
| Volume curve: −10000 at X=0, nested-log form ×3000+0.5 otherwise | CODE-CONFIRMED (exact expression) |
| Ambient driver: 600 000 ms cadence, hour/3600, ×0.7 volume, indoor id 863500002 | CODE-CONFIRMED |
| Footstep id source: actor-visual fields (+108 walk, +112 run), NOT mud cells | CODE-CONFIRMED |
| Async worker: 9 opcodes, >200 ms tick interval, 100 ms sleep | CODE-CONFIRMED |
| Four volume buses (music / terrain+ambient / char / mob); value/100 linear | CODE-CONFIRMED |
| VFS census: 2107 .ogg (178 2D / 1929 3D), 1 .wav | SAMPLE-VERIFIED |
| BGM in `data/sound/2d/`; 3D SFX in `data/sound/3d/` | SAMPLE-VERIFIED + CODE-CONFIRMED |
| `SOUND_KIND` integer values | UNVERIFIED (names recovered; values not byte-confirmed) |
| `.wlk`/`.run` all-null in this VFS; mud-cell footstep path is editor-only at runtime | CODE-CONFIRMED (runtime trace) + SAMPLE-VERIFIED (all-zero tables) |
| `SoundManager` object field offsets | CODE-CONFIRMED from binary stores (implementation guide only) |
| `GSoundOGG` object size (776 bytes) | CODE-CONFIRMED |
| Front-end cue map (BGM 920100200, UI click 861010101, intro stinger 861010105) | CODE-CONFIRMED (static) |
| Front-end buttons are silent; owner-window action handler plays the cue | CODE-CONFIRMED (static, absence-scan) |
| Per-FE-button exact click cue (login / PIN widgets) | UNVERIFIED (debugger-only) |

---

## 0. Audio engine — headline architecture

The legacy client's audio layer uses **Microsoft DirectSound** exclusively. There is no Miles Sound
System, no FMOD, no XAudio, no OpenAL, and no Bass. A single import — `DirectSoundCreate` from
`DSOUND.dll` — is the sole audio API entry point.

All audio clips are **Ogg Vorbis**. The Vorbis decoder (version **Xiph.Org libVorbis 1.3.2**,
build label "Schaufenugget", date 2010-11-01) is **statically linked** into the client binary:
no `ov_*` symbols appear in the import table. Decoding uses `ov_open_callbacks` (in-memory reader
when the VFS is mounted) or `ov_open` + `fopen` (dev/editor direct-file fallback). A single orphan
RIFF/PCM `.wav` file exists in the `data/sound/3d/` directory; it is never reached by the runtime
table-dispatch path and is presumed an editor artifact (see §10 open item 3).

Four C++ classes form the audio family:

| Class | Role |
|---|---|
| `SoundManager` | High-level facade: owns the listener, all volume buses, the per-map ambient driver, and the actor-event SFX router |
| `GSound` | One DirectSound secondary buffer, including play/stop/isPlaying and volume/position helpers |
| `GSoundOGG` | Ogg-Vorbis-backed `GSound` subclass: loads, decodes, and (for long clips) streams via an open Vorbis handle |
| `GSoundThread` | Background Win32 worker thread: drains a mutex-guarded event queue and refills streaming ring buffers |

An **editor-only** `SoundTester` class (with methods `loadTerrainSound`, `loadTotalMugongSound`,
and a tab-separated dump logger) is compiled into the binary but is **not** part of the runtime
path. References to it in this spec are marked as editor-only.

A **single global audio-enabled byte** gates the whole system. When cleared (zero), both the sound
create factory and the worker thread early-out, silencing all audio at negligible CPU cost.

---

## 1. Device and buffer initialisation

On startup `SoundManager::initialize` calls the DirectSound device-init routine, which performs:

1. `DirectSoundCreate(NULL, &device, NULL)` — creates the default DirectSound device.
2. `device.SetCooperativeLevel(hwnd, DSSCL_PRIORITY)` — **PRIORITY cooperative level** (required
   for primary buffer manipulation).
3. `CreateSoundBuffer` for a **primary buffer** with capability flags `0x10011`:
   `DSBCAPS_PRIMARYBUFFER` | `DSBCAPS_CTRL3D` | `DSBCAPS_CTRLVOLUME`.
4. `QueryInterface(IID_IDirectSound3DListener)` on the primary buffer → stored as the global 3D
   listener. The listener interface is acquired here at init, not lazily.

If any step fails the device is shut down and initialisation returns failure.

### 1.1 Five WAVEFORMATEX templates

Five PCM `WAVEFORMATEX` descriptors are pre-built once at init and kept as globals. They are
selected at sound-create time by channel count and sample rate:

| Role | Channels | Sample rate | Bit depth | Block align | Notes |
|---|---:|---:|---:|---:|---|
| 3D mono 22.05 kHz | 1 | 22050 | 16 | 2 | **3D default** |
| 3D mono 44.1 kHz  | 1 | 44100 | 16 | 2 | 3D alternate |
| 2D stereo 22.05 kHz | 2 | 22050 | 16 | 4 | **2D default** |
| 2D stereo 44.1 kHz | 2 | 44100 | 16 | 4 | 2D alternate (also used for primary buffer) |
| 2D stereo 44.1 kHz | 2 | 44100 | 16 | 4 | (duplicate; primary-buffer variant) |

Two `DSBUFFERDESC` templates wrap the appropriate `WAVEFORMATEX` — one for 2D secondary buffers,
one for 3D secondary buffers.

### 1.2 Hard codec rule (CODE-CONFIRMED)

The Vorbis channel count reported by the file header is checked against a sentinel:

- A sound loaded with the **3D flag** must have **exactly 1 channel (mono)**. A clip with any other
  channel count is rejected at load with a logged error and is not played.
- A sound loaded with the **2D flag** must have **exactly 2 channels (stereo)**. A mono 2D clip is
  rejected similarly.
- Within each family, the sample rate selects the 44.1 kHz or 22.05 kHz template.

Implementations must enforce this same rule: route each clip's ID to the correct directory (§3) and
reject mismatched files rather than attempting channel conversion.

---

## 2. Sound object and the create path

A sound is created by the `SoundManager::createSound` factory method, which takes:
`(sound_id, type_flags, dir_prefix)`.

The `type_flags` byte is a small bitfield:

| Bit | Meaning |
|---|---|
| bit 0 | Always set: this is an Ogg-backed clip |
| bit 1 | Set for 3D (positional); clear for 2D (non-positional) |

Common concrete values: `1` = 2D OGG, `3` = 3D OGG.

A **second, list-based selector** (used by the ambient and SFX router paths) chooses 2D vs 3D from
a per-entry descriptor category byte: **`category < 5` → 2D** (flags `1`, `data/sound/2d/`),
**`category ≥ 5` → 3D** (flags `3`, `data/sound/3d/`).

The on-disk filename is always constructed as:

```
<dir_prefix><sound_id>.ogg
```

The sound ID is formatted as a **plain decimal integer** (no zero-padding). The `.ogg` extension is
**unconditional** — the factory never builds a `.wav` path. The VFS is tried first (in-memory
`ov_open_callbacks` decode); the plain `fopen` path is a dev/editor fallback.

The created object is a `GSoundOGG` instance, **776 bytes** in size.

---

## 3. VFS audio census (SAMPLE-VERIFIED)

### 3.1 Directory layout and file counts

```
data/sound/
  2d/   178 .ogg files   (≈44.5 MB total)
  3d/  1929 .ogg files   (≈28.5 MB total)
  3d/     1 .wav file    (47,130 bytes; orphan — see §10 item 3)
```

All 2108 audio files reside exclusively under these two subdirectories. No audio files appear
elsewhere in the VFS.

### 3.2 2D directory (`data/sound/2d/`)

- 178 `.ogg` files; no `.wav` files.
- File sizes range from ~4.6 KB to ~1.9 MB.
- **Large files (> 300 KB, 53 total):** BGM (background music) tracks — stereo 44.1 kHz Vorbis.
- **Small files (< 50 KB, 62 total):** non-positional 2D SFX (UI sounds, voice, ambient triggers).
- **Mid-range (50–300 KB, 63 total):** mixed; some BGM variants, some longer SFX.
- All conform to the 2D channel rule: stereo (2 channels).

### 3.3 3D directory (`data/sound/3d/`)

- 1929 `.ogg` files + the 1 orphan `.wav`.
- File sizes range from ~3.2 KB to ~250 KB; median ~9.8 KB; 96% are under 50 KB.
- All short enough to fit in the 512 KiB decode scratch (§4), confirming the 3D size constraint.
- All sampled files conform to the 3D channel rule: mono (1 channel), 22050 Hz.

### 3.4 Sound ID naming scheme

Every audio file uses a **9-digit decimal integer stem** with no zero-padding:
`data/sound/{2d|3d}/{sound_id}.{ogg|wav}`.

The mapping from a `sound_entry_id` integer to a filename is **purely implicit** — the runtime
formats the integer as decimal and appends `.ogg`. No text manifest or lookup table exists in the
VFS; a search of all 619 `.txt` files and 65 `.lst` files found no sound catalog.

**Observed ID prefix groups (from the VFS census):**

| Prefix range | Directory | Count | Interpretation |
|---|---|---:|---|
| `910xxx` | `2d/` | 104 | BGM tracks (large stereo files) |
| `924xxx` | `2d/` | 5 | BGM premium/boss variants (largest files, up to 1.9 MB) |
| `920xxx` | `2d/` | 3 | BGM special |
| `86x` series | `2d/` | ~52 | Short 2D SFX (UI/system sounds; includes the two music-exempt IDs) |
| `841–847xxx` | `3d/` | ~411 | Largest 3D group (player-character sounds, likely) |
| `850–856xxx` | `3d/` | ~726 | Mob sounds |
| `831–834xxx` | `3d/` | ~185 | Creature family group |
| `871–874xxx` | `3d/` | ~180 | Creature family group |
| `881–887xxx` | `3d/` | ~295 | Creature family group |
| `800xxx` | `3d/` | 8 | Generic environmental |
| `910xxx` | `3d/` | 31 | 3D variants sharing the `910` prefix space |

A hypothesis (`AAA-BBB-CCC` structured decomposition of the 9-digit integer) has been proposed in
the analyst notes but is **UNVERIFIED**. The runtime treats the ID as an opaque integer formatted
to a filename; no decomposition code was found in the runtime path. See §10 item 2.

---

## 4. Decode buffer and the streaming sentinel (CODE-CONFIRMED)

### 4.1 Load sequence

`GSoundOGG::load` follows this sequence:

1. Build filename `<dir_prefix><sound_id>.ogg` (decimal, unconditional `.ogg`).
2. Open the source: if the VFS is mounted, load the file bytes via the VFS and open a Vorbis stream
   over an in-memory reader; otherwise fall back to `fopen` + plain Vorbis file open.
3. Decode the Vorbis stream into the **shared 512 KiB scratch buffer** (`0x80000` bytes) using
   repeated `ov_read` calls, stopping at EOF or when the scratch is full.
4. Check the Vorbis channel count against the channel-rule sentinel (§1.2); reject on mismatch.
5. Apply the **size sentinel** (§4.2).
6. Call `CreateSoundBuffer`; for a 3D clip additionally call `QueryInterface(IID_IDirectSound3DBuffer)`
   to acquire the 3D interface.
7. Lock the DirectSound buffer, copy the decoded PCM from scratch, unlock.

### 4.2 Size sentinel — the streaming threshold

After decoding, if `decoded_bytes == 0x80000` (the scratch is exactly full, meaning the clip is at
least as long as the scratch can hold — roughly **11.9 seconds** of mono 22.05 kHz 16-bit PCM):

- **3D clip:** **rejected**. The engine logs "3d over size id(N)" and marks the GSound as dead. 3D
  point sounds must be short enough to fit in the scratch entirely.
- **2D clip:** **switches to streaming mode**. The DirectSound buffer is allocated with
  `dwBufferBytes = 0x100000` (a **1 MiB ring**). The Vorbis handle is kept open. Three streaming
  flags are set on the GSound object (streaming active, streaming type, active flag). The worker
  thread's `updateStream` method refills this ring via further `ov_read` calls (§7.3).

If `decoded_bytes < 0x80000` (clip fits entirely in the scratch): the Vorbis handle is closed and
the DirectSound buffer is allocated with `dwBufferBytes = decoded_bytes` — a **one-shot buffer**.

This is the **background-music mechanism**: BGM tracks are stereo 44.1 kHz files (178–1900+ KB),
which always overflow the scratch and therefore always switch to the 1 MiB streaming path. Short
3D SFX (median ~9.8 KB) always fit and are one-shot.

`GSoundOGG::changeStream` (used by the worker's CHANGE_STREAM opcode, §7.2) mirrors this but first
stops playback, relocks the ring, re-opens the new clip's Vorbis stream, and re-decodes the first
`0x80000` bytes — this is the BGM track-swap operation.

---

## 5. Volume curve (CODE-CONFIRMED — exact expression)

Linear amplitude `X ∈ [0, 1]` is converted to a DirectSound millibel attenuation and passed to
`IDirectSoundBuffer::SetVolume`:

```
if X == 0.0:
    millibel = −10000          # DSBVOLUME_MIN; full silence
else:
    millibel = (int) logf( logf(X) × 3000.0 + 0.5 )
```

The expression uses two nested `logf` (natural logarithm) calls with a scale factor of **3000.0**
and a bias of **0.5** before integer truncation. This is a steep perceptual taper; near-silence
values map to deeply negative millibel values very quickly.

**Implementation contract:**
- Reproduce the **silence endpoint exactly**: amplitude `0.0` must map to `−10000` mB (full silence).
- The taper shape should be reproduced faithfully when possible; approximate otherwise.
- All bus gain calculations produce a linear amplitude in `[0, 1]` before calling this function
  (e.g. `option_value / 100.0`, further scaled by any applicable multiplier such as the ×0.7
  3D-ambient scale from §6.3).

---

## 6. Per-map ambient driver (CODE-CONFIRMED + SAMPLE-VERIFIED)

`SoundManager::updateAmbientByPosition` is called **every frame** by the main render/game-update
loop and also forced immediately from each volume-apply function.

### 6.1 Entry conditions

- If no local player exists, early-out immediately.
- Movement gate: if the player has not moved by at least **2.0 units** since the last evaluation,
  the positional re-pick is skipped (sound state remains unchanged).

### 6.2 Listener update and cell lookup

The driver reads the local player's world XYZ, updates the DirectSound 3D listener position
(`IDirectSound3DListener::SetPosition`), and looks up the terrain **MUD cell** for the player's
(X, Z) coordinates. The MUD cell is a small record in the terrain data; the bytes within it that
index into the sound tables are at cell offsets **+2**, **+3**, **+4**, **+5**, **+6**, and **+7**
(see `formats/sound_tables.md` for the per-extension mapping).

### 6.3 Three in-memory sound tables

The ambient driver maintains three runtime arrays in memory (one per sound table family), each
holding the deserialized contents of the corresponding on-disk table. Each array uses a **48-byte
stride** per entry (matching the on-disk record size from `formats/sound_tables.md`). The entry
fields read at runtime are:

- `sound_entry_id` at entry+0x00 (u32): the audio resource ID to play.
- `hour_schedule[hour]` at entry+0x04+hour (u8): non-zero means active this hour.
- `pos_x` at entry+0x20 (f32): world-space X for 3D positioning.
- `pos_z` at entry+0x28 (f32): world-space Z for 3D positioning.
- `volume_factor` at entry+0x2C (f32): volume multiplied by ×0.7 before use.

The three table families are:

| Table | Indexed by mud-cell byte(s) | Concurrent slots | Audio kind |
|---|---|---:|---|
| BGM (`.bgm`) | mud+0x02 | 1 | Non-positional background music |
| Looped ambient (`.bge`) | mud+0x03, mud+0x04 | 2 | Looped non-positional environment sounds |
| 3D triggered points (`.eff`) | mud+0x05, mud+0x06, mud+0x07 | 3 | Looped positional 3D sound sources |

A cell byte of **0** is the null sentinel — index 0 is never played.

### 6.4 Hour-of-day gating

The current in-game hour is computed as:
```
hour = game_clock_seconds / 3600          # integer division
```
Each entry's `hour_schedule` array (24 bytes, one per hour) is indexed by this value. If the byte
at `hour_schedule[hour]` is zero, that entry is muted for the current hour. A change in the hour
value forces a full re-pick of all hour-gated entries.

### 6.5 Re-evaluation cadence

The driver implements a throttle: a full forced re-pick of all BGM/ambient slots is triggered at
most once every **600 000 ms (10 minutes)** regardless of cell changes. Between forced ticks, slots
are re-picked only when the relevant mud-cell byte actually changes. This prevents unnecessary
`start/stop` churn during normal movement.

### 6.6 BGM zone change

When the BGM slot changes (mud+0x02 byte differs from the cached value):

1. Stop and release the current BGM sound (`stopMusicZone`).
2. **Indoor/instanced override:** if the local player's indoor flag is set (a flag on the player
   actor signalling an instanced area), the BGM is forced to ID **863500002** instead of the table
   entry's `sound_entry_id`.
3. Otherwise use the table entry's `sound_entry_id`.
4. Start the new BGM (`playMusicZone`), which calls `createSound` with the music-volume bus gain.

`playMusicZone` deduplicates: if the requested BGM ID is already playing, it does not restart.

### 6.7 Looped ambient slot change (`.bge`)

When either of the two ambient slots changes (mud+0x03 / mud+0x04), the old clip for that slot is
stopped and the new one started via `playMusicZone` / `stopMusicZone`, gated by the hour-active
byte for the new entry.

### 6.8 3D point source slot change (`.eff`)

When any of the three 3D-point slots changes (mud+0x05–+0x07), `playEffPoint` is called:

- The 3D play position is **(entry.pos_x, playerY, entry.pos_z)** — X and Z come from the table
  entry, Y is taken from the player's current world Y. This anchors the point sound to the
  ground-plane position specified by the artist while keeping it at the player's altitude.
- Volume = `entry.volume_factor × 0.7`, then converted to millibels by the volume curve (§5).
- The clip is played **looped** with a min-distance sourced from the table entry.
- The terrain/ambient bus gain (`this+0x78`) is applied.
- Hour-gated by the entry's 24-byte hour schedule.

---

## 7. Async worker thread (CODE-CONFIRMED)

### 7.1 Thread overview

`GSoundThread` runs as a dedicated Win32 thread with a mutex-guarded event queue. The mutex
protects all queue push/pop operations; the worker holds the lock only while dequeuing one event at
a time. The global audio-enabled byte (§0) is also checked at the top of the worker loop — when
cleared, the loop exits.

### 7.2 Event queue — 9 opcodes

Each event in the queue carries an opcode byte as its first field. The full event payload is:

| Field offset | Type | Role |
|---:|---|---|
| +0 | u8 | opcode (1..9) |
| +4 | ptr | `GSound*` target object |
| +0x2C | u8 | loop flag (used by PLAY) |
| +0x30 | f32 | volume (linear amplitude; used by PLAY2D, PLAY3D, SET_VOLUME) |
| +0x34 | f32 | min-distance (used by PLAY3D) |
| +0x38 | f32[3] | 3D position XYZ (used by PLAY3D) |

| Opcode | Name | Action |
|---:|---|---|
| 1 | LOAD | Call `GSoundOGG::load`; on failure, mark the GSound as dead |
| 2 | DELETE | Release the GSound (calls the virtual destructor with argument 1) |
| 3 | PLAY | Call `GSound::play(loop = event.loop_flag)` |
| 4 | PLAY2D | Call `setVolume(event.volume)`, then `GSound::play` |
| 5 | PLAY3D | Call `setPosition(event.position)`, `setMinDistance(event.min_distance)`, `setVolume(event.volume)`, then play |
| 6 | STOP | Call `GSound::stop(1)` |
| 7 | SET_VOLUME | Call `setVolume(event.volume)` only |
| 8 | RESET | Call `IDirectSoundBuffer::SetCurrentPosition(0)` — reset playback position to the start |
| 9 | CHANGE_STREAM | Call `GSoundOGG::changeStream` — BGM track swap (see §4, `changeStream`) |

### 7.3 Worker cadence — streaming refill

Each iteration of the worker loop:

1. Drain all pending events from the queue, processing one event per dequeue.
2. If **more than 200 ms** have elapsed since the last `tickActiveSounds` call:
   - Walk the active-sound list; for each GSound whose streaming flag is set, call its virtual
     `updateStream` method. `GSoundOGG::updateStream` calls `ov_read` to refill the 1 MiB ring
     buffer with new decoded PCM.
3. Call `Sleep(100 ms)`.

The result is that streaming rings are refilled roughly **5 times per second** (once per >200 ms
window, sleeping 100 ms between iterations). This is the background-music keep-alive mechanism.

There are therefore **two playback routes**:

- **Synchronous route**: used by the ambient driver (§6) and the actor-event SFX router (§8). The
  sound is created, positioned, and played directly on the calling thread.
- **Asynchronous worker route**: used for streaming BGM and any caller that posts to the event
  queue. The play only happens when the worker drains the event.

---

## 8. Actor-event SFX router (CODE-CONFIRMED)

### 8.1 Overview

`SoundManager::triggerSfxByKind(kind, sound_id, distance, block)` is called from approximately
**27 sites** across the visual, animation, and combat subsystems — spawn, death, attack, level-up,
item-use, combat-effect spawn, stat update, and footsteps.

### 8.2 Audibility cull

Before creating any sound, the router performs a distance cull:

- **Local player's own sounds**: a fixed audible radius of **200.0 units** (no squared-distance
  computation — the constant is applied directly).
- **Remote actor sounds**: a **squared-distance cull** using the visual actor's registered
  max-distance, scaled by a per-context factor. If the squared distance to the actor's world
  position exceeds the scaled threshold, the sound is dropped silently.

### 8.3 Volume bus selection

Two separate volume slots exist within the router:

- **"My" sounds** (local player, actor flag == 1): use the char gain bus (`this+0x7C`).
- **"Others'" sounds** (remote actors, mobs): use the mob gain bus (`this+0x80`).

### 8.4 Kind dispatch

The `kind` argument is the integer `SOUND_KIND` value (§9). The router dispatches by kind:

| Kind values | Pool | Concurrent cap | Notes |
|---|---|---:|---|
| **5, 10, 11** | Directional / voice 3D pool | ~3 | Character voice, skill sounds, NPC audio |
| **7, 8, 9** | Footstep pool | pool-sized | Walk and run footsteps |
| Any other value | Rejected | — | Not routed; no sound created |

Sounds are instantiated via `createSound(sound_id, 3, "data/sound/3d/")`, then positioned
(`setPosition`, `setMinDistance`), volume-set, and played via `GSound::play`.

### 8.5 Footstep source — actor-visual fields (CODE-CONFIRMED; closes `client_runtime.md §1.9.6`)

Footsteps are fired from `AnimMixer::onCycleLoop`, which is called each time an animation cycle
wraps. It reads the footstep ID **from the per-character actor-visual object**, at two fixed field
offsets relative to the visual's base pointer:

| Actor-visual offset | Type | Field | Used when |
|---:|---|---|---|
| +108 | u32 (or compatible int) | walk-footstep sound ID | Actor is NOT running |
| +112 | u32 (or compatible int) | run-footstep sound ID | Actor IS running |

The running/walking selection is made by checking **actor-visual +0x414** (a running flag, value 1
= running):

- If the running flag is **1**: call `triggerSfxByKind(kind=8, id = visual+112)`.
- Otherwise: call `triggerSfxByKind(kind=7, id = visual+108)`.

**This is why all `.wlk` and `.run` sound table files in the VFS contain only null entries.** The
per-map walk/run tables are addressed by an editor-only `SoundTester` path, not by the in-game
footstep mechanism. In the runtime the footstep audio ID is a per-character property (sourced from
the actor-visual / character config), not a terrain cell property. The mud-cell-based footstep
lookup path exists only in the editor tool and should be considered **dead code at runtime**.

The source of the values at visual+108 and visual+112 (which config file or wire packet populates
these fields) is outside the scope of this spec — it belongs to the struct/asset cartographers
responsible for the actor-visual layout.

---

## 9. SOUND_KIND enumeration (names CODE-CONFIRMED; values UNVERIFIED)

The following names were recovered from editor/tool debug format strings that enumerate the kind
enum. They correspond to the `kind` argument to `triggerSfxByKind`. The **integer values assigned
to each name are not byte-confirmed** — the enum definition resides in an editor-tool region that
was not fully traced. The names themselves are the developers' own labels.

| Name | Approximate runtime mapping | Notes |
|---|---|---|
| `SOUND_KIND_NONE` | — | Null/unused |
| `SOUND_SKILL` | likely kind 5, 10, or 11 | Skill-cast audio |
| `SOUND_ACTION` | likely kind 5, 10, or 11 | General action audio |
| `SOUND_DASH` | likely kind 5, 10, or 11 | Dash movement audio |
| `SOUND_RUN` | kind 8 | Running footstep (confirmed from router) |
| `SOUND_WALK` | kind 7 | Walking footstep (confirmed from router) |
| `SOUND_BG3D` | kind 9 | 3D background/ambient SFX |
| `SOUND_SYSTEM3D` | likely kind 5, 10, or 11 | System 3D sounds |
| `SOUND_NPC` | likely kind 5, 10, or 11 | NPC audio |
| `SOUND_WEATHER` | — | Weather audio |
| `SOUND_SYSTEM2D` | — | System 2D sounds |
| `SOUND_BG` | — | 2D background |

The router's confirmed play-kind integers are **5, 7, 8, 9, 10, 11**. The mapping of name to
integer must be confirmed by tracing the editor-tool enum loader before any implementation
hard-codes a specific integer for a named kind.

---

## 10. Volume buses and options (CODE-CONFIRMED)

### 10.1 Four buses

The sound system maintains **four independent volume buses**, each controlling a category of
sounds:

| Bus | `SoundManager` field | Option key(s) | Sounds it governs |
|---|---|---|---|
| Music | `+0x74` (gain), `+0x84` (enabled byte) | `OPTION_SOUNDBOL_MUSIC` (vol), `OPTION_SOUND_MUSIC` (on/off) | The active BGM `GSound*` at `+0x34`; gain also scaled by master multiplier `+0x1C` |
| Terrain / ambient | `+0x78` (gain), `+0x85` (enabled byte) | `OPTION_SOUNDVOL_BACK` (vol), `OPTION_SOUND_TERRAIN` (on/off) | The 3-slot ambient array (`+0x38`) + the char-sound linked list (`+0x48`) |
| Char SFX | `+0x7C` (gain), `+0x86` (enabled byte) | `OPTION_SOUNDVOL_CHAR` (vol), `OPTION_SOUND_CHAR` (on/off) | Char-sound linked list head (`+0x54`) |
| Mob SFX | `+0x80` (gain), `+0x86` (enabled byte) | `OPTION_SOUNDVOL_MOB` (vol), `OPTION_SOUND_MOB` (on/off) | Mob-sound linked list head (`+0x60`) |

Note: char and mob share the `+0x86` enabled byte (a single "char/mob enabled" byte is stored;
the two option keys both reference it).

### 10.2 Volume conversion

Each option value is an integer in the range `0..100`. The bus gain stored in the manager is:
```
bus_gain = option_value / 100.0        # linear amplitude in [0, 1]
```

On each change, the gain is applied to every live buffer in that bus via `setVolume(bus_gain ×
any_extra_scale)`, converting through the curve in §5.

### 10.3 Bus enable/disable

When a bus's enabled byte is set to 0 (the sound-category is toggled off), its live buffers are
**stopped and freed** — not merely silenced. Re-enabling the bus requires the ambient driver to
re-create the clips on the next ambient evaluation cycle.

### 10.4 Music master multiplier

The BGM bus has an additional scalar at `+0x1C` (initialised to `1.0`). The effective BGM
amplitude is:
```
bgm_amplitude = bus_gain_+0x74 × master_multiplier_+0x1C
```
This second multiplier is the mechanism for per-scene or per-event BGM amplitude adjustments
without touching the user-facing slider value.

### 10.5 User-facing options (option-store index mapping)

The flat settings array (indexed by small integers) carries the audio options at these confirmed
indices:

| Index | Option key | Meaning |
|---:|---|---|
| 17 | `OPTION_SOUND_CHAR` | Character SFX enabled (0/1) |
| 18 | `OPTION_SOUND_MOB` | Mob SFX enabled (0/1) |
| 19 | `OPTION_SOUND_TERRAIN` | Terrain/ambient enabled (0/1) |
| 20 | `OPTION_SOUND_MUSIC` | Music enabled (0/1) |
| 21 | `OPTION_SOUNDVOL_CHAR` | Char SFX volume 0..100 |
| 22 | `OPTION_SOUNDVOL_MOB` | Mob SFX volume 0..100 |
| 23 | `OPTION_SOUNDVOL_BACK` | Terrain/ambient volume 0..100 |
| 24 | `OPTION_SOUNDBOL_MUSIC` | Music volume 0..100 (note the "BOL" typo in the original key name) |
| 27 | (unnamed toggle) | Full-volume override for the two music-exempt IDs (§10.6) |

A dedicated in-game Sound options panel (`OptionPanel_Sound`) is bound to these keys. Saving is
performed by four save functions: `Option_Save_SOUND_CHAR_AndVol`, `Option_Save_SOUND_MOB_AndVol`,
`Option_Save_SOUND_TERRAIN_AndVol`, `Option_Save_SOUND_MUSIC_AndVol`.

### 10.6 Music-slider-exempt IDs

Two specific 2D sound IDs are played at **full volume** (amplitude `1.0`) when a global toggle
(options index 27) is set, bypassing the music bus gain:

| ID | Notes |
|---|---|
| **861010109** | Music-slider-exempt cue A |
| **861010110** | Music-slider-exempt cue B |

These are played via the `play2D` path with a full-volume override rather than the standard bus
gain. The purpose is to allow specific cues (likely diegetic or event-critical audio) to always
be heard regardless of the user's music volume setting.

---

## 11. `SoundManager` object field summary (implementation guide)

The following field offsets within the `SoundManager` heap object are CODE-CONFIRMED from the
binary and are provided to orient an implementer mapping legacy behaviour to a reimplementation.
They are engine-internal C++ heap offsets — not wire offsets, not file offsets.

| Offset | Size | Type | Field | Notes |
|---:|---:|---|---|---|
| +0x00..+0x0B | 12 | f32[3] | Player XYZ cache | Updated each ambient tick |
| +0x0E | 1 | u8 | Last BGM mud-cell index | Change-detect for `.bgm` slot |
| +0x0F, +0x10 | 2 | u8[2] | Last `.bge` ambient slot indices | Change-detect for 2 ambient slots |
| +0x11..+0x13 | 3 | u8[3] | Last `.eff` 3D-point slot indices | Change-detect for 3 point slots |
| +0x14 | 1 | u8 | Last-played BGM zone index | Dedup for `playMusicZone` |
| +0x18 | 4 | u32 | SFX slot count = **12** | Set at init |
| +0x1C | 4 | f32 | Master multiplier | BGM amplitude multiplier (init 1.0) |
| +0x20 | 1 | u8 | BGM "music on" byte | — |
| +0x24 | 4 | u32 | Current hour-of-day cache | `game_seconds / 3600` |
| +0x34 | 4 | ptr | Active BGM `GSound*` | The currently playing BGM clip |
| +0x38 | 4 | ptr | Desired BGM id + loop flag | — |
| +0x38..+0x43 | 12 | ptr[3] | Ambient bus `GSound*` array | 3 looped ambient clips |
| +0x48 | 4 | ptr | Char-sound linked list | Used by terrain volume bus too |
| +0x54 | 4 | ptr | Char-sound list head | Iterated by char-volume apply |
| +0x60 | 4 | ptr | Mob-sound list head | Iterated by mob-volume apply |
| +0x68 | 4 | ptr | Actor-event voice/cell map root | `triggerSfxByKind` cell lookup |
| +0x74 | 4 | f32 | Music gain | `OPTION_SOUNDBOL_MUSIC / 100` |
| +0x78 | 4 | f32 | Terrain/ambient gain | `OPTION_SOUNDVOL_BACK / 100` |
| +0x7C | 4 | f32 | Char gain | `OPTION_SOUNDVOL_CHAR / 100` |
| +0x80 | 4 | f32 | Mob gain | `OPTION_SOUNDVOL_MOB / 100` |
| +0x84 | 1 | u8 | Music enabled | `OPTION_SOUND_MUSIC` |
| +0x85 | 1 | u8 | Terrain enabled | `OPTION_SOUND_TERRAIN` |
| +0x86 | 1 | u8 | Char/mob enabled | `OPTION_SOUND_CHAR` || `OPTION_SOUND_MOB` |
| +0x88 | 4 | u32 | Mixer channel count = **10** | Set at init |
| +0x8C | 1 | u8 | Indoor/global BGM toggle latch | Flipped on indoor-flag transition |
| +0x90 | 4 | u32 | Ambient-driver last-eval time (ms) | Throttle anchor for 600 000 ms cadence |

---

## 12. Implementation guidance (clean-room reimplementation)

This section maps the legacy model to a Godot / .NET reimplementation without prescribing any
specific C# API.

### 12.1 Layer ownership

- **`Client.Infrastructure`**: own the audio bus state (four bus gain/enable values), the ambient
  driver logic, the footstep trigger, the event-queue model. This layer is engine-free.
- **Godot `05.Presentation`**: bind each bus to Godot `AudioBus`, `AudioStreamPlayer` (2D) or
  `AudioStreamPlayer3D` (3D). Map the linear bus gain through a Godot equivalent of the volume
  curve. The 512 KiB / streaming split maps directly to Godot's loop-stream vs. preload modes for
  `OggVorbisStream`.

### 12.2 Critical invariants to preserve

| Invariant | Why it matters |
|---|---|
| 3D clips must be **mono**; 2D clips must be **stereo** | Hard codec rule; violating it produces silence or a rejected load |
| BGM always streams (2D, > 512 KiB threshold met by all BGM tracks) | Memory budget; BGM tracks are up to 1.9 MB |
| 3D SFX always one-shot (all under 250 KB, well within 512 KiB limit) | Confirmed by VFS census |
| Volume = 0.0 maps to full silence (−10 000 mB equivalent) | Audio drop must be hard, not a near-silent bleed |
| Footstep ID comes from actor-visual fields (+108/+112), NOT from `.wlk`/`.run` tables | Those tables are all-null; reading them returns silence |
| BGM indoor override = **863500002** | Must be applied before `playMusicZone` dedup check |
| Music-exempt IDs **861010109** / **861010110** bypass the music bus gain | Play at amplitude 1.0 regardless of the music slider |

### 12.3 Ambient driver re-implementation checklist

1. Run every frame (or on each position update).
2. Gate on player existence; skip if no movement (< 2.0 unit threshold).
3. Update 3D listener position.
4. Look up the MUD cell at player (X, Z); read bytes at offsets +2 through +7.
5. For BGM (byte +2): on change, stop old → apply indoor override if needed → start new (dedup).
6. For ambient (bytes +3, +4): on change per slot, stop old → check hour → start new if hour-active.
7. For 3D point (bytes +5, +6, +7): on change per slot, stop old → check hour → play new at
   (entry.pos_x, playerY, entry.pos_z) with volume `entry.volume_factor × 0.7`.
8. Honour 600 000 ms forced-re-eval cadence.
9. Hour = `game_clock_seconds / 3600`.

---

## 13. `formats/sound_tables.md` known-unknown resolutions

The following items were listed as open questions in `formats/sound_tables.md`. They are now
resolved by the dirty-room sound-system lane and are recorded here for cross-reference.

| `sound_tables.md` known-unknown | Resolution |
|---|---|
| §known-unknown 11: 2D audio samples (was UNVERIFIED) | RESOLVED: 178 `.ogg` in `data/sound/2d/`; no `.wav` |
| §known-unknown 12: BGM streaming directory (was UNVERIFIED) | RESOLVED: BGM is in `data/sound/2d/`; confirmed SAMPLE-VERIFIED |
| §known-unknown 6: walk/run indexing formula | RESOLVED (runtime, not format): footsteps come from actor-visual +108/+112, not from `.wlk`/`.run` table cells at runtime |
| §known-unknown 2: weight +0x1C semantic | CONFIRMED unused at runtime (no traced consumer) |
| §known-unknown 1: unknown_36 at +0x24 | CONFIRMED unused at runtime (no traced consumer) |
| `sound_tables.md` sound ID path (`data/sound/3d/` unconditional) | CORRECTED: `.bgm` IDs resolve via `data/sound/2d/`; `.eff` IDs via `data/sound/3d/`; the 2D branch was present but untraced in the earlier spec pass |

---

## 14. `client_runtime.md §1` open-item resolutions

Items listed in `client_runtime.md §1.9`:

| `client_runtime.md` §1.9 item | Resolution |
|---|---|
| §1.9.1: Exact units of nested-log volume curve | RESOLVED: `(int) logf( logf(X) × 3000.0 + 0.5 )` — exact expression CODE-CONFIRMED |
| §1.9.2: Entry field +0x24 (editor-fill) | CONFIRMED unused at runtime; editor-fill only |
| §1.9.3: Entry weight +0x1C | CONFIRMED unused at runtime; always 1.0f, no runtime consumer found |
| §1.9.4: 2D directory stereo presence | CONFIRMED: 178 stereo `.ogg` in `data/sound/2d/` SAMPLE-VERIFIED |
| §1.9.5: How table id for `.wav` is played | Still UNVERIFIED at runtime; the one `.wav` is unreachable from the table dispatch path; presumed orphan |
| §1.9.6: Source of actor-visual walk/run footstep-id fields | CONFIRMED: actor-visual+108 (walk) and +112 (run); the actor-visual config is the authority, not sound tables |
| §1.9.7: Voice-pool/footstep-pool concurrency | PARTIALLY CONFIRMED: voice pool capped at ~3 concurrent; global 10-channel pool eviction policy not traced |

---

## 15. Front-end (login / PIN / server-list / char-select) cue map

> **Confidence:** the cue ids and the silent-button architecture are CODE-CONFIRMED from static
> analysis of the front-end scene action handlers; the per-FE-widget exact click cue is
> UNVERIFIED (debugger-only). VFS presence of each `<id>.ogg` is SAMPLE-VERIFIED. This section
> records only the durable front-end facts; the intro-scene crawl/slideshow that fires id
> 910061000 is specified separately in `specs/intro_sequence.md`.

### 15.1 Architecture finding — front-end buttons are silent; the owner window plays the cue

The shared front-end button base (the generic `GUButton` class) plays **no sound at all** — not on
click, hover, press, or release. An absence-scan of every method of that class (constructors, the
input/event handler, the press-tracker, the hit/hover test, the render path, the enable/state
setter, and the focus helpers) found **no call to any sound player** and **no referenced sound id**.
The string field the button stores is its caption text (consumed by the font draw), never a sound id.

Instead, on a completed click the button packages an **action event** carrying its action id and
posts it to the input manager, which routes it to the **owning window's action handler**. That
handler is where any click cue is played, at the **head of each action branch**, via the single
2D play call shape `play(SoundManager, category, sound_id, loop_flag)`.

**Implementation consequence:** do not attach a click sound to a generic button widget. Model the
cue as an action-handler concern — the window that owns the button chooses (or omits) the cue per
action.

### 15.2 Confirmed front-end cues

| Cue | Sound id | Category / dir | Role | Confidence |
|---|---:|---|---|---|
| Front-end / title BGM | **920100200** | 2D (`data/sound/2d/920100200.ogg`) | Background music for the map000 "global terrain" zone, which the VFS layout uses for the login/title front-end (no separate title zone directory exists). | CODE-CONFIRMED (sound-table byte-decode, §6) + SAMPLE-VERIFIED |
| Generic UI click | **861010101** | 2D (`data/sound/2d/861010101.ogg`) | The project's universal "a button did something" click. Fired by the char-create / char-select action handler on essentially every button action (appearance steppers, name/create/delete/rename confirms, slot-select confirm, back/cancel transitions). | CODE-CONFIRMED (static; literal id at the play call) |
| Enter-world / confirm-slot | **920100200** | 2D | Fired when the player enters the selected character from char-select (the same id as the front-end BGM, played as a 2D one-shot confirm cue on the enter action). | CODE-CONFIRMED (static) |
| Login intro stinger | **861010105** | 2D (`data/sound/2d/861010105.ogg`) | Auto-fired (not a click) at the login state machine's own intro state. Distinct from the **opening-scene** stinger id 910061000 (see `specs/intro_sequence.md`). | CODE-CONFIRMED (static) |
| Forced-full-volume cues | **861010109**, **861010110** | 2D | The two music-slider-exempt cues; played at amplitude 1.0 when the system-2D option (index 27) is set, bypassing the music bus gain. See §10.6 / §15.2 cross-reference. | CODE-CONFIRMED (special-cased in the 2D player) |

### 15.3 The 862030101–107 cue pool (registered, not 1:1-bound)

A family of UI/system cues **862030101 … 862030107** (7 ids, all category 2 = 2D) is **registered**
in the system-cue registry (the registry is keyed by id and played by passing the id to the 2D
player). However, **no static binding** maps any of these ids to a specific front-end widget event
(e.g. a particular dialog-open, checkbox, or keypad press). They form a **generic cue pool** drawn
by id; only the universal click (861010101, §15.2) and the enter cue (920100200) are statically
1:1-bound to concrete front-end actions. Do not assume a keypad-digit-to-862030xxx mapping; it is
unestablished. (The 7-file count is a plausible match for 10 digits + special keys, but that is a
size coincidence, not a confirmed binding.)

### 15.4 What is UNVERIFIED (debugger-only)

- **Per-login-button click cue.** The login window's action handler was walked branch-by-branch
  (Confirm, Refresh with its cooldown, Cancel, the EULA/save-id gates, server-row select, channel
  select) and **no play call appears on any login action branch**. If the live login form plays a
  click, it is not visible statically. Mark the per-login-button cue **UNVERIFIED** — close it with
  the live debugger by breakpointing the 2D player and clicking Confirm / Refresh / Cancel.
- **PIN keypad press / OK / cancel cue.** The PIN / second-password class is silent in static
  analysis (digit append, reset, OK, cancel — no play call seen). Whether any keypad cue exists is
  **UNVERIFIED**; debugger-only.
- **Button hover cue.** The button base has no hover sound and no front-end window was seen to play
  a hover cue; likely none exists. **UNVERIFIED**.

### 15.5 Category → path rule (restatement)

The front-end cues all use the standard rule (§2): `category < 5` → `data/sound/2d/<id>.ogg`. All
the cues in §15.2 are 2D. The `.ogg` basename form is the project-wide convention (INFERRED from
the loader prefix rule; debugger-confirmable).

---

## Open questions

1. **`SOUND_KIND` integer values.** The enum names are recovered from editor debug strings. The
   exact integer assigned to each name (e.g., which integer value `SOUND_SKILL` maps to) is not
   byte-confirmed from the runtime path. The router proves the integers **5, 7, 8, 9, 10, 11** are
   used; the mapping of those integers to the named enum members must be confirmed by tracing the
   editor-tool enum initializer before any implementation should hard-code name-to-integer bindings.

2. **9-digit sound ID decomposition.** The `AAA-BBB-CCC` grouping hypothesis is inferred from the
   VFS census but is not confirmed by any code path. The runtime treats the ID as an opaque integer.
   Whether the upper digits encode a structured type/category key or are a sequential assignment
   scheme is unconfirmed.

3. **Orphan `.wav` file (`data/sound/3d/850901075.wav`).** This is the only non-OGG audio file in
   the VFS. The runtime create factory builds `<id>.ogg` unconditionally; no `.wav` fallback branch
   was found in the runtime path. The file is presumed an editor artifact. Before implementing a
   `.wav` loader, confirm no separate `.wav` resolution path exists.

4. **`.wlk`/`.run` mud-cell footstep path status.** The editor `SoundTester` addresses these tables
   by mud cell, but all runtime-path traces show footstep IDs coming from actor-visual fields. Whether
   the mud-cell path is ever active at runtime (e.g. as a fallback when the visual ID is 0) cannot
   be determined from the current analysis. All sampled `.wlk`/`.run` tables are all-zero, making
   exercise impossible. Presumed editor-only, but not proven absent.

5. **5 two-dimensional `play2D` sub-categories.** The `play2D` function takes a 0..4 category
   selector that routes to a specific bus or handling variant. The full mapping of those five 2D
   sub-categories to the `SOUND_KIND` names and their bus routing has not been fully traced (would
   require surveying ~200 call sites).

6. **Global 10-channel cap eviction policy.** The `SoundManager` sets a mixer channel cap of **10**
   at init. The per-kind pools (voice ~3 concurrent, footstep pool-sized) operate within this cap.
   The eviction/voice-stealing policy when all 10 channels are simultaneously busy was not traced.

7. **Per-entry min-distance and rolloff values.** The min-distance passed to
   `IDirectSound3DBuffer::SetMinDistance` for 3D point sounds is sourced from the table entry or
   the calling site. The global DirectSound rolloff factor on the 3D buffer/listener was not isolated.
   Implementations should expose this as a tunable rather than hard-coding a value.

8. **Actor-visual field sources (+108 walk-id, +112 run-id).** The identity of the config file or
   wire packet that populates these fields is outside the scope of this spec. The struct/asset
   cartographers should document which `.skn` / character-config column or which actor-update
   packet field writes these values.

9. **`SoundManager::initialize` failure vs. partial-audio mode.** If DirectSound init fails (missing
   `DSOUND.dll`, no audio device), the engine clears the audio-enabled byte and continues — confirmed.
   Whether any graceful "no audio" UI notification exists (a warning dialog, log entry, or options
   panel flag) was not confirmed.

10. **Per-front-end-widget click cue.** The login window and PIN keypad have no static play call on
    any action branch; whether each plays a click cue at runtime is UNVERIFIED (debugger-only, §15.4).
    The 862030101–107 pool is registered but not 1:1-bound to any front-end widget (§15.3).

---

## Cross-references

- **On-disk sound table format** (`.bgm`, `.bge`, `.eff`, `.wlk`, `.run`): `formats/sound_tables.md`
  (entry layout, file size, stride, field offsets — the authority for those bytes).
- **VFS container** that delivers sound files and tables: `formats/pak.md`.
- **Terrain cell bytes** that index into sound tables: terrain / `.mud` format (not yet fully
  spec'd; see `formats/terrain.md` and the `.ted`/`.mud` family).
- **Actor-visual layout** (source of walk/run footstep IDs at +108/+112): `structs/actor.md`
  (if/when the actor-visual struct is cartographed there).
- **Skinning / animation cycle trigger** (the event that fires footstep SFX): `specs/skinning.md`
  (animation cycle-wrap event) and `formats/animation.md` (`.mot` clip timing).
- **Sound table terrain-cell integration** (mud-cell byte layout): `formats/terrain.md`.
- **Front-end intro scene** (the opening crawl/slideshow that fires intro sound 910061000):
  `specs/intro_sequence.md`.
- **Front-end scene flow** (login state machine, char-select chrome): `specs/frontend_scenes.md`.
- **Canonical names**: see `Docs/RE/names.yaml` (`SoundManager`, `GSound`, `GSoundOGG`,
  `GSoundThread`, `SoundKind`, `SoundEvent`, `IndoorBgmOverrideId`, `MusicSliderExemptIds`,
  `DecodeScratchBytes`, `StreamRingBytes`, `AmbientReevalMs`).
- **Provenance**: see `Docs/RE/journal.md`.
