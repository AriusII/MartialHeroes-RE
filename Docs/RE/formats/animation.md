# Format: .mot  (skeletal animation clip)

> Clean-room spec. Neutral description only — NO sample bytes, NO decompiler pseudo-code,
> NO binary code addresses. Consumed by Assets.Parsers. Every offset an engineer cites must
> reference this file.
>
> verification: sample-verified (layout, incl. BANI body); confirmed (loader-control-flow facts); handedness capture/debugger-pending; CYCLE 14 re-anchor (f61f66a9): 2 facts re-confirmed SAME, 0 corrected; deep-cartography deepening (f61f66a9, 2026-06-29): Q1 static bound sharpened toward id_a at per-actor registry (added after CYCLE-7 reconciliation note); no layout changes
> re-verified against doida.exe IDB SHA 263bd994, CYCLE 7 (2026-06-20): runtime stand idle = actormotion col16 (record +0x44, direction-array-A element 1), NOT col15/+0x40; death SFX/effect = motion_ids_b element b[4] (+0x74), NOT b[5]; the runtime idle catalogue lookup is keyed by the APPEARANCE KEY (player = 5·(class+4·variant)−24), NOT col2/skin_class; the a[1] motion id joins to a `.mot` clip through the motlist.txt-seeded clip registry keyed by the `.mot` header `id_b` (no `g{id}.mot` runtime sprintf exists).
> re-verified again (2026-06-21): BANI body is now DECODED (identical standard track/keyframe layout, sample residual 0); the BANI "all-files-constant" claims are CORRECTED (the `unknown_field` and `track_count` are NOT constant — three rig groups exist); the oversized standard clip is identified. See §BANI variant and §Oversized standard clip.
> re-verified (2026-06-24): static-analysis + five-sample on-disk re-confirmation pass (IDB SHA 263bd994c927c20a38624cf0ca452eaef365057fa9db1543d8f668c14a6fd8ee). Zero structural drift. Confirmed: header read order (id_a → id_b → name LenStr → frame_count), 10-fps duration rule, track-array layout (8-byte preamble + key_count×28 keyframes), keyframe 7 f32 vec3+quat XYZW, track_descriptor low-byte=bone_id (upper 3 bytes reserved/zero on all observed tracks), id_b load-time registry key, motlist.txt → data/char/mot/ prefix + id_b-keyed registry (no g{id}.mot sprintf), 80-byte clip object. Five standard samples parsed to zero residual. BANI variant accepted from prior pass (one g170350513 file confirmed BANI by magic). No corrections required.
> re-verified (2026-06-27, CYCLE 14, f61f66a9): header field layout (id_a, id_b, name LenStr, frame_count × 0.1 duration, track_count), 28-byte keyframe (7 f32: translation XYZ then quaternion XYZW), 8-byte per-track preamble (track_descriptor low-byte = bone_id, key_count), and id_b-keyed clip registry with no g{id}.mot sprintf all re-confirmed SAME. No corrections.
> ida_reverified: 2026-06-16; CYCLE 7 (2026-06-20); 2026-06-21; 2026-06-24; 2026-06-27
> ida_anchor: f61f66a9ae0ec1e946105b2ecff76e8930cb1d1367df64e5688a5266f5ad9963
> evidence: [static-ida, vfs-sample]
> status: sample_verified
> sample_verified: true
>
> **Conflicts carried (this anchor):** the per-bone `bone_id`→direction-slot meaning of the two
> 9-slot motion-id arrays is PROPOSED (not proven). Up-axis / handedness / unit-scale are
> capture/debugger-pending (this lane was static-only). The BANI body layout, previously carried as
> NOT YET DECODED, is now characterised (it reuses the standard track/keyframe layout — §BANI variant);
> only the oversized standard clip's trailing block remains undecoded (§Oversized standard clip). No
> layout/structural drift was found on build `263bd994` against the real VFS sample.

---

## Status and confidence summary

| Area | Status | Confidence |
|------|--------|------------|
| `.mot` header (`id_a`, `id_b`, `name_length`, `name_body`, `frame_count`) | Resolved | CONFIRMED (sample-verified) |
| `.mot` corpus is real, not stubs (3877/3891 are real clips) | Resolved | SAMPLE-VERIFIED (full census of 3,891 files) |
| `.mot` BANI-magic variant (11 files; different header) | Header fully recovered (re-confronted on build `263bd994`: `name_len = 10`, g170350513 `frame_count = 24`); **all 11 files now enumerated** — `version`/`anim_id`/`unknown_field`/`frame_count`/`track_count` read per-file (§BANI variant — full enumeration) | SAMPLE-VERIFIED (every header field of all 11 files) |
| `.mot` BANI body layout (post `track_count`) | **Resolved — identical to the standard track/keyframe layout** (per-track `u32 descriptor (low byte = bone_id) + u32 key_count + key_count × 28 keyframes`, 7 f32 each). Only the BANI header differs from standard. | SAMPLE-VERIFIED (one BANI file reconciles to EOF with zero residual) |
| `.mot` BANI `unknown_field` / `track_count` are NOT constant (three rig groups) | Resolved — earlier "constant 7830 / constant 52" claims REFUTED; `unknown_field` ∈ {7830, 7899, 8881}, `track_count` ∈ {52, 77, 67}, the two co-vary (rig group id ↔ bone count) | SAMPLE-VERIFIED (all 11 files) |
| `.mot` track / keyframe layout | Resolved | CONFIRMED (CAMPAIGN VFS-MASTERY two-witness: loader + black-box corpus census) |
| LenStr 4-byte u32 LE prefix, no on-disk terminator | Resolved | CONFIRMED (loader + sample) |
| `track_descriptor` upper-3-byte padding (key-count / channel-mask / interp-flag all refuted) | Resolved | CONFIRMED (loader-direct + sample-verified) |
| `id_a` vs `id_b` roles (load key vs runtime clip handle) | Resolved | CONFIRMED |
| Wrap / loop is runtime-only (no on-disk flag) | Resolved | CONFIRMED |
| `actormotion.txt` record layout (offsets + read order) | Resolved | CONFIRMED (parser-derived) |
| `actormotion.txt` per-column semantic names (cols 3–14) | Proposed | PROPOSED (offsets confirmed; field meanings inferred) |
| Runtime stand idle = actormotion **col16** (record +0x44, array-A element 1), NOT col15/+0x40 | Resolved | CONFIRMED (CYCLE 7, use-site — see §`actormotion.txt` layout, §Runtime idle slot) |
| `actormotion.txt` col15 / col16 entries hold idle `.mot` `id_a`-style motion ids | Resolved | SAMPLE-VERIFIED (89.1% hit rate on build `263bd994` for the col15 file values; §`actormotion.txt` layout) |
| Runtime idle catalogue lookup keyed by the **appearance key** (player = 5·(class+4·variant)−24), not col2/skin_class | Resolved | CONFIRMED (CYCLE 7) |
| a[1] motion id → `.mot` clip join is via the motlist.txt-seeded registry keyed by the `.mot` header **id_b** | Resolved | CONFIRMED (CYCLE 7, use-site) |
| `actormotion.txt` declared count (1084) vs parsed rows (1080) | Documented | SAMPLE-VERIFIED discrepancy (§`actormotion.txt` layout) |
| Animation mixer two-list architecture | Resolved | CONFIRMED |
| Mixer sync-phase mechanism + 1.5× rate constant | Resolved | CONFIRMED |
| Per-bone weighted-average accumulation (lerp/slerp) | Resolved | CONFIRMED |
| Per-frame clip time `t` advances (real elapsed `dt = ms × 0.001`; never pinned to 0) | Resolved | CONFIRMED (control-flow) |
| Cycle-layer advance: free-run (`local_time += rate·dt`, modulo wrap) vs sync (`t = duration · phase/range`) | Resolved | CONFIRMED (control-flow) |
| Human col15 stand idle (`g101100001.mot`) is STATIC data (a fixed pose, 0 animated tracks) | Resolved | SAMPLE-VERIFIED (production-parser keyframe diff + positive controls) |
| Runtime stand slot is col16 (a[1], +0x44); the content of the col16 clip (static vs animated) and the full live selection | Partly resolved / Open | CONFIRMED col16 is the slot (CYCLE 7 use-site); the col16 clip's content + live behaviour DEBUGGER-PENDING |
| Default layer playback speed constant (≈1.575) | Documented | UNVERIFIED (constant confirmed; full semantics open) |
| Skinning / deform pipeline that consumes `.mot` (LBS, inverse-bind, pose composition) | Cross-referenced | see `specs/skinning.md` |
| BANI standard-loader rejection (parse-error on all 11 files) | Resolved | SAMPLE-VERIFIED + CODE-CONFIRMED — BANI files are non-loadable by the shipping client |

Open items are tracked in §Known unknowns. This spec documents the on-disk `.mot` binary
(the only artifact an `Assets.Parsers` engineer must implement) plus the runtime animation
model as informative background for `Client.Application`. The runtime model describes
behaviour and constants only; it does not expose any in-memory object layout. The **math that
deforms a skinned mesh using a sampled `.mot` pose** (linear-blend skinning, inverse-bind bake,
pose composition, quaternion/handedness conventions) is documented in `specs/skinning.md`.

---

## Corpus census (sample-verified)

> Earlier revisions of this spec were written against only **three stub clips** (both counts zero)
> and gave the impression that `.mot` files are all stubs. A full census of all **3,891** `.mot` files
> in the validated VFS corrects that: the corpus is overwhelmingly **real, full-payload clips**.

| Status | Count | Share |
|--------|------:|------:|
| Total `.mot` files | 3,891 | 100% |
| Real clips (`frame_count > 0` AND `track_count > 0`) | 3,877 | 99.7% |
| Stub clips (`frame_count == 0` or `track_count == 0`) | 3 | 0.08% |
| BANI-magic variant (separate header — see §BANI variant) | 11 | 0.28% |

**Distribution highlights (real clips):**

- **Frame count** is dominated by short loops: ~59% of real clips have 11–50 frames (1.1–5.0 s at
  10 fps), consistent with combat/idle cycles. A long tail reaches into the hundreds and (rarely)
  thousands of frames.
- **Track count** confirms these are full-body skeletal clips: ~67% of real clips have **51+ bone
  tracks**. The three known stubs are outliers, not representative.
- A few very-long, low-track clips (e.g. ~7,650 frames with only 2 tracks) are cutscene / special
  motions (root + one bone), not character body clips.
- **One standard-variant clip is oversized:** of the 3,880 standard files, exactly **one** has a
  large trailing region (≈+48,719 bytes) past the end of the parsed clip (header + track array). It
  parses cleanly as a normal clip and the trailing block is left unconsumed; its content is not
  decoded (possible multi-clip / LOD / appended data). The anomalous file has been **identified** —
  it is the clip whose `id_a` is `142206011` (residual exactly 48,719 bytes, the sole standard
  anomaly in a 559-file every-7th sample). This is an isolated anomaly — see §Oversized standard clip
  and §Known unknowns. All other standard files reconcile with zero residual.

**Implication:** character skinning is **not** blocked by missing animations — the assets are present
and plentiful. The deform/skinning math that consumes them is specified in `specs/skinning.md`.

---

## Identification

- **Extension:** `.mot`
- **Found in:** `.pak` / VFS archive (see `formats/pak.md`); logical path prefix `data/char/mot/`
- **Magic / signature:** **two variants exist in the corpus.**
  - **Standard variant (3,880 of 3,891 files):** no magic — the file begins immediately with the
    header fields below (`id_a` at offset 0). This is the primary format documented in this spec.
  - **BANI variant (11 files):** begins with the 4-byte ASCII magic `"BANI"` (`42 41 4E 49`) and uses
    a different header layout before the name string. See §BANI variant. **A parser MUST sniff the
    first 4 bytes** and route BANI files separately — the standard loader in the shipping client does
    NOT detect the magic, causing a parse failure on all 11 files (see §BANI variant — loader rejection).
  - Both a binary mode and a text mode appear to be supported at runtime for the standard variant
    (see §Binary / Text duality under Header layout); the binary mode is the primary format here.
- **Endianness:** little-endian throughout (all multi-byte integers and floats are LE).
- **Confidence:** CONFIRMED; sample_verified: true (standard header sample-verified; corpus census of
  3,891 files; BANI header fully recovered across 3 cross-checked samples)

---

## Discovery and catalogue files

Three supporting text files (plain ASCII, one entry per line) control how the engine finds and
pre-loads clips:

| File | Role | Confidence |
|------|------|------------|
| `data/char/motlist.txt` | Manifest / registry index — each line is a bare on-disk filename; the engine prepends `data/char/mot/` to form the VFS path and registers the clip in a numeric-id keyed motion registry (see *Motion id registry* below). | CONFIRMED |
| `data/char/actormotion.txt` | Maps actor / visual identifiers to motion catalogue IDs. Tabular, count-prefixed, 33 columns per record; layout characterized in §`actormotion.txt` layout. Each parsed row becomes a 136-byte AnimCatalog value record filed in an **ordered map keyed by the appearance key** (NOT a flat `[index]` array — §Runtime idle slot). | CONFIRMED (record layout, parser-derived); runtime stand slot = col16 (CYCLE 7 use-site); per-column semantics for cols 3–14 PROPOSED |
| `data/motion.cache` | Pre-load ID cache. Wire layout: `[u32 count][count × u32 motion_catalogue_id]`. The engine opens this file through a direct OS file call (not the VFS) and uses the IDs to prime the in-memory clip map, triggering eager full-load for the listed IDs. | CONFIRMED (wire layout); size-math cross-checked (see §motion.cache / effect.cache size-math); magic / versioning UNVERIFIED |

The 9-digit motion IDs stored in `actormotion.txt` (see §`actormotion.txt` layout) are the same
values as the `.mot` per-file `id_a` field, which matches the numeric component of the `.mot`
filename. `actormotion.txt` is therefore the actor-facing table that names, per actor class and
motion slot, which `.mot` clip to play.

### Motion id registry (CODE-CONFIRMED)

`.mot` clips are resolved by **numeric id through a registry**, not by formatting a `g%d.mot`
filename at play time. At boot the engine reads `data/char/motlist.txt` line by line; for each line
it prepends the directory prefix `data/char/mot/` to the listed on-disk filename and registers the
resulting clip in the runtime motion registry. After the list is registered, the pre-load id cache
(`data/motion.cache`) drives eager full-load of the listed ids.

**No `g{id}.mot` (or `%d.mot`) sprintf exists anywhere in the binary (CYCLE 7, CONFIRMED).** A string
scan of every format string finds the only `g%d`-shaped asset path is the SKIN path
`data/char/skin/g%d.skn` — there is no `.mot` printf template. The `.mot` files are reached **only**
by the explicit on-disk filenames listed in `motlist.txt`, each prefixed with `data/char/mot/`. The
filenames happen to follow a `g{id}.mot` naming convention on disk, but that is a naming convention,
not a runtime template — the resolution is always *id → registry entry*, never a formatted path.

**The registry key is the `.mot` header `id_b` (CYCLE 7, CONFIRMED by use-site).** When each
`motlist.txt` clip is registered, the registration function inserts it into the ordered (balanced-tree)
motion registry keyed by the clip's `.mot` header **`id_b`** (the second header int; see §Header
layout, §Clip catalogue). At play time a motion id taken from `actormotion.txt` (the array-A element
the actor's action state selects — the stand idle uses **array-A element 1 = column 16**, see
§`actormotion.txt` layout) is the **lookup key** into this registry; the registry returns the
already-loaded clip with no per-play file open. The join is therefore: **the `actormotion.txt` motion
id equals the target `.mot` file's header `id_b`.**

> **Reconciliation — which numeric identity keys the registry (CYCLE 7, binary wins).** Earlier
> revisions of this spec describe the load-time clip map as keyed by `id_b` and call `id_a` "the
> per-file unique runtime *clip handle*" used by the mixer (see §Clip catalogue). CYCLE 7's static
> read-site evidence confirms and sharpens this: the **registry the `actormotion.txt` motion id is
> matched against at play time is the load-time clip map keyed by the `.mot` header `id_b`** — i.e.
> the motion id in array-A is itself an `id_b`-class set/group key, resolved against the same
> `id_b`-keyed map the loader builds. This is the load-time *clip map* (`id_b`-keyed), distinct from
> the mixer's per-active-layer handle bookkeeping (which addresses an already-resolved clip object by
> its `id_a`, §Mixer). Where this differs from the older "the `actormotion.txt` 9-digit ids are `id_a`
> values, looked up by the mixer's `id_a` handle" wording, the CYCLE-7 use-site evidence prevails: the
> *file-find / clip-resolve* step that turns an array-A motion id into a clip is the `id_b`-keyed
> registry lookup. The two are genuinely two maps — a load-time `id_b`-keyed clip registry and the
> mixer's per-layer `id_a` lookup over already-active cycles — and the array-A motion-id → clip join
> goes through the former. The older "`actormotion.txt` ids are `id_a`" phrasing is **superseded** for
> the play-time resolve step.
>
> *evidence: [static-ida]; ida_anchor: 263bd994; CYCLE 7.*


---

## Header layout

The header is read in two passes (see §Two-stage loading below). Both passes read the same
four fields in the same order. All fields are binary little-endian unless the text-mode flag is
active (see §Binary / Text duality). **This layout applies to the standard variant only**; the
BANI variant has a different header (see §BANI variant).

| Rel. offset | Size | Type    | Field         | Notes                                                                                                                              | Confidence |
|------------:|-----:|---------|---------------|------------------------------------------------------------------------------------------------------------------------------------|------------|
| 0           | 4    | u32 LE  | `id_a`        | Per-file unique numeric identifier. Matches the decimal integer component of the filename (e.g. `g170354502.mot` → `id_a = 170354502`). Returned as the clip handle by the registration function and used by the runtime mixer as the per-clip lookup ID; not used as the load-time catalogue key (see §Clip catalogue). | CONFIRMED (sample-verified) |
| 4           | 4    | u32 LE  | `id_b`        | Group / set load key. Shared across all clips in the same actor motion set. Used as the key when inserting into the runtime clip map at load time (see §Clip catalogue). **This `id_b` is also the registry lookup key the play-time motion-id resolve uses** — the `actormotion.txt` array-A motion id equals the target clip's `id_b` (see §Motion id registry, CYCLE 7). | CONFIRMED (sample-verified; registry-key use-site CYCLE 7) |
| 8           | 4    | u32 LE  | `name_length` | Length of the name body that follows, in bytes. 4-byte u32 LE prefix — no null terminator on disk. See §LenStr encoding. | CONFIRMED (loader + sample) |
| 12          | N    | bytes   | `name_body`   | Clip name/path string of `name_length` bytes. Form **varies**: either a class-prefixed token (e.g. `musa101100001`) or a relative source-tree path (`./do/g{id_a}.mot`). Read in both loading stages and silently discarded; parsers must read and skip it to advance the file pointer. See §Name field semantics. | CONFIRMED (sample-verified) |
| 12+N        | 4    | u32 LE  | `frame_count` | Raw frame count. Clip duration in seconds = `frame_count × 0.1` (fixed 10 fps rate; see §Timing). | CONFIRMED (sample-verified) |

`N` is the value of `name_length`. Because `name_body` is variable-width, subsequent fields have
no fixed absolute offset.

### LenStr encoding

The `name` field uses the same `LenStr` encoding that `.skn` and `.bnd` use (see `formats/mesh.md`,
§String encoding). The wire format is a 4-byte u32 LE length prefix followed by exactly `length`
bytes of string body with no null terminator on disk.

**CONFIRMED (loader + sample):** the 4-byte u32 LE length prefix with no on-disk terminator is
independently verified two ways that agree exactly. (a) **Loader-verified:** the shared
length-prefixed-string helper, in its binary branch, reads the length as a fixed 4-byte word
directly from the file and then consumes exactly `length` body bytes — it does not read an extra
terminator byte in the fixed-count case (the newline-terminated case exists only in text mode).
(b) **Sample-verified:** in the reference samples the four bytes at offset 8 decode as a u32 LE
value that equals exactly the byte count of the string body that follows, and the byte immediately
after the body is the first byte of the next field (`frame_count`), not a `00` terminator. A 1-byte
or 2-byte prefix interpretation does not align the subsequent fields and is rejected. The
terminator visible in memory is a runtime string-object artifact, not a byte stored on disk.

This is the same shared `LenStr` helper used by the `.skn` skin loader and the `.bnd` skeleton
loader, consistent with the single shared encoding noted in `formats/mesh.md`.

> Engineer note: read 4 bytes as `u32 LE` = `name_length`, then read exactly `name_length` body
> bytes and advance the cursor — do **not** skip a terminator byte. // spec: Docs/RE/formats/animation.md

The name body in the reference samples is ASCII. The field is discarded after reading, so encoding
matters only when the name body must be inspected for diagnostics. For such cases, treat as
CP949 / EUC-KR (the client's Korean locale); pure-ASCII values (numeric paths) are a subset and
decode identically under any ASCII-compatible encoding.

### Binary / Text duality

The header reader supports two modes, selected by a runtime flag on the file object. In text mode
the same four fields are parsed as whitespace-separated decimal ASCII integers rather than binary
words. Whether text-mode `.mot` files share the `.mot` extension or use a different name is
UNVERIFIED. This spec documents binary mode only; parsers should implement binary mode first and
treat text mode as a deferred concern.

### Name field semantics

The embedded name string is a build-time content identifier whose **textual form varies** across
the corpus (SAMPLE-VERIFIED on build `263bd994`):

- **Class-prefixed token form** — e.g. `musa101100001` (13 bytes): a class/family prefix
  concatenated with the numeric clip id. This is the form observed in the reference samples re-checked
  this anchor.
- **Relative source-tree path form** — `./do/g{id_a}.mot`, where the `do` directory component reflects
  the original D.O. Online game source layout.

Neither form matches the VFS load path (`data/char/mot/`); both are build-time artifacts baked into
the content at export time. The earlier "always `./do/g{id_a}.mot`" wording was too narrow — name
bodies vary (class-prefixed and/or path forms). Both loading stages read this field and discard it
immediately; the value is never stored in the runtime clip object. Parsers must consume
`name_length` bytes to keep the file pointer aligned but need not retain the value, so the form
variation does not affect parsing.

---

## Two-stage loading

The engine loads a `.mot` file in two sequential passes. Both are relevant to parsing because they
read partially overlapping regions.

| Stage | What is read | Purpose |
|-------|-------------|---------|
| **Stage 1 — header only** | Fields `id_a`, `id_b`, `name_body` (skipped), `frame_count`. Derives `duration_seconds = frame_count × 0.1` and stores it. File is kept open (or re-opened in Stage 2). | Register the clip in the catalogue map under `id_b`; mark as partially loaded. |
| **Stage 2 — full data** | Re-reads the four header fields, then continues with `track_count` and all track / keyframe data. | Populate the bone-track array; mark clip as fully loaded. |

A parser implementing `Assets.Parsers` should perform the equivalent of both stages in a single
sequential read (read the header, then immediately read the track array).

The runtime maintains a per-clip "fully loaded" flag separate from registration. The mixer skips
any cycle whose underlying clip has not reached the fully-loaded state, so stub or in-flight clips
are silently ignored during sampling rather than producing garbage.

---

## Track array layout

Immediately following the header (after `frame_count`), Stage 2 data begins with a count field,
then a sequence of variable-length track records.

### Track count

| Rel. offset (after header) | Size | Type   | Field         | Notes                                                                              | Confidence |
|---------------------------:|-----:|--------|---------------|------------------------------------------------------------------------------------|------------|
| 0                          | 4    | u32 LE | `track_count` | Number of `BoneTrack` records that follow. One track per animated bone. A value of zero is valid (stub clip). Real full-body clips carry tens of tracks (~67% have 51+); a clip may have fewer tracks than the skeleton has bones — unanimated bones are simply absent and stay at their bind pose. | CONFIRMED |

### Per-track record

Repeated `track_count` times. Each record consists of a fixed 8-byte preamble followed by a
variable-length keyframe block.

| Rel. offset (within track) | Size                | Type    | Field              | Notes                                                                                                                                   | Confidence |
|---------------------------:|--------------------:|---------|--------------------|-------------------------------------------------------------------------------------------------------------------------------------------| -----------|
| 0                          | 4                   | u32 LE  | `track_descriptor` | Low byte = `bone_id` (see §Bone-track linkage). Bytes 1–3 (bits 8–31) are reserved/unused padding: the loader reads the whole word once, slices off the low byte, and discards the rest — they carry no meaning. Strict parsers may assert that the upper three bytes are zero. See §`track_descriptor` byte decomposition. | CONFIRMED (loader-direct + sample-verified; upper bytes confirmed unused padding) |
| 4                          | 4                   | u32 LE  | `key_count`        | Number of keyframes in this track. **Sole driver** of the keyframe-array length — it is a field of its own, never derived from `track_descriptor`. | CONFIRMED |
| 8                          | `key_count × 28`   | bytes   | `keyframes`        | Inline array of keyframe records, each 28 bytes. See §Keyframe record.                                                                  | CONFIRMED |

**Track record stride:** variable — `8 + key_count × 28` bytes. The fixed 8-byte preamble
(`track_descriptor` + `key_count`) is immediately followed by exactly `key_count` keyframes of
28 bytes each; no padding or alignment is inserted between the preamble and the keyframe block,
nor between consecutive tracks. **CONFIRMED (CAMPAIGN VFS-MASTERY two-witness gate — the full-data
loader and a black-box corpus census of real clips agree on the 8-byte per-track header followed by
`key_count × 28` bytes of keyframes).**

### `track_descriptor` byte decomposition (CONFIRMED — loader-direct)

The four bytes of `track_descriptor` decompose as a single low-byte field plus reserved padding.
This decomposition is **CONFIRMED from the full-data loader directly** (independent of, and in
agreement with, the earlier parser-derived + sample confirmation):

| Bits  | Byte        | Field      | Role                                                              | Confidence |
|------:|-------------|------------|------------------------------------------------------------------|------------|
| 0–7   | byte 0 (low) | `bone_id` | Bone selector for this track; the **only** part of the word used. | CONFIRMED  |
| 8–15  | byte 1      | (reserved) | Read as part of the word, then discarded — no meaning.           | CONFIRMED  |
| 16–23 | byte 2      | (reserved) | Discarded (same as byte 1).                                       | CONFIRMED  |
| 24–31 | byte 3      | (reserved) | Discarded (same as byte 1).                                       | CONFIRMED  |

The loader consumes the descriptor word exactly once, extracts the low byte via a low-8-bits
accessor, and stores only that byte into the per-track record (it is later matched to a `.bnd`
bone `self_id`). The remaining 24 bits are never shifted, never masked, never compared, and never
stored; no downstream consumer receives them. The loader does not itself assert they are zero — it
simply ignores them, so it tolerates any value there.

**The three candidate interpretations for the upper bytes are positively refuted:**

| Candidate hypothesis for bits 8–31 | Verdict | Why |
|------------------------------------|---------|-----|
| A key / keyframe count             | REFUTED | The keyframe-array length is driven **entirely** by the separate `key_count` u32 (the field read immediately after the descriptor), not by any sub-field of the descriptor. |
| A channel / component presence mask | REFUTED | Each keyframe is a **fixed, unconditional** 7-float set (vec3 translation + vec4 quaternion = 28 bytes). No descriptor bit gates which channels are present; there is no TX/TY/TZ/RX/RY/RZ presence flag. |
| An interpolation-mode flag         | REFUTED | Interpolation (lerp for translation, slerp for rotation) is selected at the runtime per-track sampler, not from any descriptor bit. No interpolation branch is keyed on this word. |

**Engineer note:** read the 4-byte word as `u32 LE`, take `bone_id = descriptor & 0xFF`, ignore the
upper three bytes (or assert them zero for a strict validator), then read `key_count` as a separate
`u32 LE` to size the keyframe array. // spec: Docs/RE/formats/animation.md

### Keyframe record — 28 bytes, little-endian

Each keyframe encodes one sample in time as exactly **7 little-endian f32 values (28 bytes)**: a
3-component local translation vector followed by a 4-component rotation quaternion (XYZW, scalar
last). There is **no scale channel** and **no on-disk interpolation flag** — the record is fixed and
unconditional. **CONFIRMED (CAMPAIGN VFS-MASTERY two-witness gate — the full-data loader reads the
fixed 7-float / 28-byte record, and a black-box corpus census of real clips reconciles file sizes
against the `8 + key_count × 28` track stride).**

| Sub-offset | Size | Type   | Field            | Notes                            | Confidence |
|-----------:|-----:|--------|------------------|----------------------------------|------------|
| 0          | 4    | f32 LE | `translation_x`  | Local translation X.             | CONFIRMED |
| 4          | 4    | f32 LE | `translation_y`  | Local translation Y.             | CONFIRMED |
| 8          | 4    | f32 LE | `translation_z`  | Local translation Z.             | CONFIRMED |
| 12         | 4    | f32 LE | `rotation_x`     | Quaternion X component.          | CONFIRMED |
| 16         | 4    | f32 LE | `rotation_y`     | Quaternion Y component.          | CONFIRMED |
| 20         | 4    | f32 LE | `rotation_z`     | Quaternion Z component.          | CONFIRMED |
| 24         | 4    | f32 LE | `rotation_w`     | Quaternion W (scalar) component. | CONFIRMED |

**Keyframe stride: 28 bytes (7 × f32). Component order: XYZ translation, then XYZW quaternion.**
There is no eighth float and no trailing flag byte — the next keyframe begins immediately at +28.

The quaternion component order (X, Y, Z, W) with the scalar W last is consistent with the
quaternion representation used in `.bnd` bind-pose records (see `formats/mesh.md`,
§Quaternion component order).

**Scale:** there is no scale channel. The keyframe record carries only translation and rotation.
Scale is not animated in this format.

**Interpolation flag:** there is no on-disk interpolation-mode byte or bit in the keyframe (or in the
per-track header). Interpolation is fixed at runtime (lerp for translation, slerp for rotation) — see
§Timing and interpolation.

**Pose semantics (cross-reference):** a sampled keyframe is a **local replacement pose** for the
named bone, not an additive delta — it overwrites the bone's animated local translation/rotation.
Animation drives **rotation on child bones and translation on the root only**; a child bone's local
translation is held at its bind-pose value each frame. The full pose-composition and deform math is
specified in `specs/skinning.md` (§6 keyframe sampling, §5 deform).

---

## Timing and interpolation

### Timing

- **Fixed frame rate: 10 fps.**
- **Clip duration** (seconds) = `frame_count × 0.1`.
- At playback time `t` (seconds), the sample index is `floor(t × 10.0)`. The next index is
  `sample_index + 1`, clamped to `key_count − 1`. Clamp-to-last is applied within the
  per-track sample function when addressing keyframes; wrap-to-zero is handled at the clip level
  by the runtime cycle layer (see §Wrap and loop behaviour).
- **Interpolation parameter** `alpha` = `t − (sample_index / 10.0)`. This is expressed in raw
  seconds in the range `[0, 0.1]` and is passed directly as the blend factor to both the
  translation and rotation interpolators. It is not re-normalized to `[0, 1]` before use. Whether
  this is intentional design or a latent defect in the original client is UNVERIFIED. An importer
  may reproduce the raw-seconds alpha for bit-faithful legacy motion, or renormalize `alpha /= 0.1`
  for smooth playback — this choice is discussed for the Godot path in `specs/skinning.md` §8(c).

### Per-frame clip-time advance — the engine never pins `t` to zero (CONFIRMED)

> **CONFIRMED (control-flow):** the playback time `t` handed to the per-track sampler is a **live,
> advancing value, fed real elapsed wall-clock time every frame.** No code path samples an active
> layer at a fixed `t = 0`. This is the load-bearing fact behind the idle-animation question: a short
> looping idle is *alive in the original* exactly when its keyframes differ (see §Static idle clips
> below and `specs/skinning.md` §10).

The actor per-frame update reads a millisecond timestamp from the actor manager, subtracts the
timestamp it last drew at (stored on the actor), and converts the difference to seconds:

```
delta_ms      = now_ms − last_drawn_ms        # real wall-clock frame delta, milliseconds
last_drawn_ms = now_ms                       # written back for the next frame
dt            = delta_ms × 0.001              # real elapsed seconds
```

`dt` is this real elapsed delta, recomputed fresh each frame; it is fed to the mixer's per-frame pose
build (§Per-frame update sequence). The same `dt` advances each active layer's clock, so the
per-track sampler always sees a moving `t` for any active layer. (The `0.001` is the
millisecond-to-second conversion factor, confirmed as a float literal.)

The net rule for an implementer: **drive the active idle clip's clock with real per-frame `dt` and
wrap at clip end; never sample a started, weighted layer at a frozen `t = 0`.** A frozen-`t` reading
is the classic way a port shows a static character even when the clip data carries motion — which,
for the specific human stand idle, it does **not** (see §Static idle clips and `specs/skinning.md`
§10). The two layer timing modes that decide *which* `t` reaches the sampler are characterized in
§Wrap and loop behaviour and §Sync-phase mechanism.

### Wrap and loop behaviour

**CONFIRMED:** there is no loop flag or wrap flag in the `.mot` binary. The on-disk format
is indifferent to loop mode. Wrap behaviour is determined entirely at runtime by the clip layer
type:

- **CycleLayer (looping), free-running mode (CONFIRMED):** the layer advances its **own** local time
  by `local_time += rate × dt` each frame; when local time reaches `clip_duration` it is reset via
  modulo to `fmod(local_time, clip_duration)`. This wrap fires unconditionally whenever the clip is
  active and time overflows, and the sampler is fed this advancing `local_time`. A per-layer internal
  flag is set each time a wrap occurs and is used to trigger footstep sound-effect callbacks; this
  flag is a runtime state variable, not a file field. (The per-character speed scalar applied to `dt`
  before this advance is described in §Per-frame update sequence step 3.)
- **CycleLayer (looping), sync mode (CONFIRMED):** the layer does **not** use its own local time;
  instead its sample time is derived from the mixer-wide sync phase as
  `sample_time = clip_duration × (sync_phase / sync_range)` (§Sync-phase mechanism). The sync phase
  itself advances every frame, so the sample time still moves — the clip animates either way.
- **ActionLayer (one-shot):** the clip plays once to its end (no wrap). The layer expires and is
  removed from the active list. State transitions: fade-in → playing → fade-out → done.

Parsers in `Assets.Parsers` need not model either behaviour; this is a runtime concern for
`Client.Application`.

### Translation interpolation

Linear interpolation between consecutive translation samples:

```
blended_translation = lerp(key[n].translation, key[n+1].translation, alpha)
```

### Rotation interpolation

Spherical linear interpolation (SLERP) between consecutive rotation quaternions, with dot-product
sign flip to enforce the shortest-arc path:

```
if dot(key[n].rotation, key[n+1].rotation) < 0:
    negate key[n+1].rotation before slerp
blended_rotation = slerp(key[n].rotation, key[n+1].rotation, alpha)
```

Degenerate cases: nearly-identical quaternions (dot close to 1.0) fall back to normalized linear
interpolation. Antipodal quaternions (dot close to −1.0) are handled by a 90-degree perpendicular
path. These are implementation-level concerns for `Assets.Parsers` / runtime; the on-disk format
is unaffected.

---

## Bone-track linkage

Each track's `bone_id` (the low byte of `track_descriptor`) directly identifies which bone the
track drives. This numeric ID matches the `self_id` field of the corresponding bone record in the
`.bnd` skeleton file (see `formats/mesh.md`, §Bone array, field `self_id`). Linkage is purely
numeric — no bone name string is stored in the `.mot` file. The bone is resolved by **ID offset**
(`bone_array[bone_id − base_id]`), not by array position; see `specs/skinning.md` §3 and
`formats/mesh.md` §Bone addressing.

> **Bone-record strides (cross-reference, CONFIRMED + sample-verified on build `263bd994`).** The
> `.bnd` bone the `bone_id` resolves into exists at three distinct strides in the original client,
> documented in full in `formats/mesh.md`: **36 bytes on disk** (selfId + parentId + 3×f32 translation
> + 4×f32 quaternion XYZW), **72 bytes at parse time**, and **88 bytes (0x58) at runtime** (the
> resolved-record stride used by the ID-offset bone lookup, `base + 88·(bone_id − base_id)`, clamped
> to the last bone). An engineer reconstructing the ID-offset addressing for `.mot` track linkage
> needs the **88-byte runtime stride**; this is noted here only as a pointer into `formats/mesh.md`.

At runtime, if a `bone_id` in the `.mot` track array has no matching `self_id` in the loaded
`.bnd` skeleton, that track is silently skipped for that frame. Parsers and importers should
treat an unmatched `bone_id` as a non-fatal warning. (This is normal: a clip commonly animates
fewer bones than the skeleton has — e.g. a 80-track clip on an 84-bone skeleton.)

---

## Oversized standard clip (single-file anomaly)

> **One standard-variant `.mot` file carries a large trailing block past the parsed clip.**
> Of the 3,880 standard files, exactly **one** reconciles with a large positive residual
> (≈+48,719 bytes) after the end of the header + track array; every other standard file ends with
> zero residual. The file parses normally up to and including its last track's keyframes — the extra
> bytes sit *after* the structure this spec documents and are never read by the shipping loader.

**The anomalous file is identified:** it is the clip whose `id_a` is `142206011` — the single
standard-variant anomaly (residual exactly 48,719 bytes) in a 559-file (~14%) every-7th + BANI
sample where it was the only file that did not reconcile to zero residual. Earlier this file was left
anonymous.

The trailing block has **not been decoded**. Candidate interpretations (none confirmed): an appended
second clip / multi-clip container, a level-of-detail variant, or stale appended tool data. Because
the loader reads `track_count` tracks and then stops (it does not seek to EOF), the block is inert.

**Implication for Assets.Parsers:** read the header, then `track_count` tracks, then stop — do **not**
assume the track array ends exactly at EOF, and do **not** attempt to interpret trailing bytes as a
second clip. Tolerate a positive residual on this one file (and any future file like it). Decoding
the trailing block is **DBG-pending** and is not required for production parsing.

| Confidence |
|---|
| CONFIRMED present (single file, ≈+48,719 B residual; all other standard files zero residual — black-box census); trailing-block semantics DBG-pending |

---

## BANI variant

> **Format deviation — header AND body now fully characterised.**
> Of the 3,891 `.mot` files, **11 begin with a 4-byte ASCII magic `"BANI"`** (`42 41 4E 49`)
> instead of the bare standard header. The standard loader in the shipping client has **no
> magic-check branch** and cannot load these files — all 11 produce parse errors and are
> effectively dead data in the VFS. See §BANI variant — loader rejection for details. The body that
> follows the BANI header, however, **uses the identical standard track/keyframe layout** (now
> sample-decoded — see §BANI body layout), so the variant differs from the standard format **only**
> in its header.

The 11 BANI files all reside in `data/char/mot/`. They form **three rig groups** distinguished by
their `unknown_field` / `track_count` pair (see §BANI variant — full enumeration), not the two bands
an earlier revision described:

- ID band `170350513`–`170350515` (3 files, `version = 1`, 52-bone rig).
- ID band `170576814`–`170577315` (7 files, `version = 3`, 77-bone rig).
- the single file `170948714` (1 file, `version = 3`, 67-bone rig).

They account for 0.28% of the `.mot` corpus. They are plausibly specific character classes'
animations exported from a newer or different pipeline that was never integrated into the shipping
client loader.

### BANI header layout (SAMPLE-VERIFIED — all 11 files enumerated)

The header is variable-length due to the embedded LenStr name field. All fields listed below are
**SAMPLE-VERIFIED** except where noted.

| Offset | Size | Type | Field | Notes | Confidence |
|-------:|-----:|------|-------|-------|------------|
| 0x00 | 4 | u8[4] | `magic` | ASCII `"BANI"` (`42 41 4E 49`). Identifies the variant. | SAMPLE-VERIFIED |
| 0x04 | 4 | u32 LE | `version` | Sub-format variant selector. Observed values **1** (the 3-file `170350xxx` group) and **3** (the eight `170576xxx`/`170577xxx`/`170948xxx` files). Co-varies with the rig group: version 1 ↔ the 52-bone rig; version 3 ↔ both the 77-bone and 67-bone rigs (so version does NOT uniquely identify the rig). Whether version affects post-header payload layout is unknown; the one body decoded so far (a version-1 file) matches the standard track/keyframe layout. | SAMPLE-VERIFIED |
| 0x08 | 4 | u32 LE | `anim_id` | Numeric animation identifier. Matches the decimal numeric suffix of the filename (same role as standard `id_a` but at offset 8 rather than offset 0). | SAMPLE-VERIFIED |
| 0x0C | 4 | u32 LE | `rig_group_id` | **NOT constant** (an earlier "constant 7830" reading is REFUTED). Takes **three values** across the 11 files — `7830`, `7899`, `8881` — each paired one-to-one with a distinct `track_count` (7830↔52, 7899↔77, 8881↔67). The strict co-variation with bone count strongly supports the **skeleton / rig group id** interpretation. See §Per-file enumeration of all 11 BANI files. | SAMPLE-VERIFIED (values); PROPOSED (interpretation) |
| 0x10 | 4 | u32 LE | `name_len` | Byte length of the embedded name string (LenStr 4-byte u32 LE prefix, same encoding as the standard variant). **Value 10 for all 11 files** (re-confirmed on build `263bd994`; an earlier "11" reading is corrected). | SAMPLE-VERIFIED |
| 0x14 | N | u8[N] | `name` | ASCII name string of `name_len` bytes; no NUL terminator on disk. Encodes the bare animation identifier token, e.g. `"g170350513"` (**10 bytes**). | SAMPLE-VERIFIED |
| 0x14+N | 4 | u32 LE | `frame_count` | Total number of keyframes. **Varies per file** (observed range 24–96; see §BANI variant — full enumeration). An earlier "28" reading for the first file is corrected to **24**. | SAMPLE-VERIFIED |
| 0x18+N | 4 | u32 LE | `track_count` | Number of bone tracks. **NOT constant** (an earlier "constant 52" reading is REFUTED). Takes **three values** — `52`, `77`, `67` — one per rig group; equals the bone count of that rig. | SAMPLE-VERIFIED |
| 0x1C+N | variable | — | payload | Per-track and per-frame data. **Same layout as the standard variant** — `track_count` repetitions of `u32 descriptor (low byte = bone_id) + u32 key_count + key_count × 28-byte keyframes`. See §BANI body layout. | SAMPLE-VERIFIED |

After `track_count` the payload begins, and it is the standard track array (§Track array layout):
`track_count` per-track records, each an 8-byte preamble (`track_descriptor` with the low byte the
`bone_id`, then `key_count`) followed by `key_count × 28`-byte keyframes (7 f32 each — translation
XYZ then quaternion XYZW). See §BANI body layout.

### Per-file enumeration of all 11 BANI files

> The "all-files-constant" claim of an earlier revision is **REFUTED**: neither `rig_group_id`
> (offset 0x0C) nor `track_count` is constant. Full enumeration of every BANI file (SAMPLE-VERIFIED):

| File `anim_id` | `version` | `rig_group_id` | `name_len` | `frame_count` | `track_count` (bones) |
|---------------:|:---------:|:--------------:|:----------:|:-------------:|:---------------------:|
| 170350513 | 1 | 7830 | 10 | 24 | 52 |
| 170350514 | 1 | 7830 | 10 | 29 | 52 |
| 170350515 | 1 | 7830 | 10 | 38 | 52 |
| 170576814 | 3 | 7899 | 10 | 49 | 77 |
| 170576914 | 3 | 7899 | 10 | 96 | 77 |
| 170577014 | 3 | 7899 | 10 | 75 | 77 |
| 170577114 | 3 | 7899 | 10 | 41 | 77 |
| 170577214 | 3 | 7899 | 10 | 48 | 77 |
| 170577314 | 3 | 7899 | 10 | 47 | 77 |
| 170577315 | 3 | 7899 | 10 | 61 | 77 |
| 170948714 | 3 | 8881 | 10 | 59 | 67 |

**Three rig groups** emerge: `rig_group_id 7830 ↔ 52 bones (version 1)`,
`7899 ↔ 77 bones (version 3)`, `8881 ↔ 67 bones (version 3)`. `frame_count` varies freely per file.

### BANI body layout (SAMPLE-VERIFIED — identical to the standard variant)

The body that follows the BANI header reuses the **standard track/keyframe layout** exactly
(§Track array layout, §Keyframe record): for each of `track_count` tracks, a `u32 track_descriptor`
(low byte = `bone_id`, upper three bytes unused padding) and a `u32 key_count`, then `key_count`
keyframes of 28 bytes each (7 little-endian f32 — translation XYZ then quaternion XYZW). One BANI
file (the first `170350xxx` clip, a 52-bone / 24-frame, version-1 file) was decoded end-to-end with
this layout and **reconciles to EOF with zero residual** — its first track is `bone_id = 0` with 24
keyframes whose first keyframe carries an identity quaternion. Only the **header** distinguishes
BANI from the standard format; the track and keyframe encoding is the same.

The decoded BANI body uses f32 keyframes (the identity quaternion in the first track confirms the
float layout). A quantized representation in later BANI tracks was not exhaustively excluded but is
LOW RISK given the zero-residual reconciliation under the f32 assumption. The body remains
**loader-unreachable** in the shipping client (no magic-check branch — §BANI variant — loader
rejection), so it is decodable but dead data at runtime.

### BANI variant — loader rejection (SAMPLE-VERIFIED + CODE-CONFIRMED)

> **Conclusion: the shipping client cannot load BANI files. All 11 are dead/unused data.**

The standard loader reads the first four bytes as a u32 LE `id_a` field with no magic check. When
fed a BANI file:

1. The four magic bytes `42 41 4E 49` are read as a bogus `id_a` value.
2. The u32 at offset 4 (the `version` field, value 1 or 3) is read as `id_b`.
3. The u32 at offset 8 (the `anim_id` field, e.g. 170350513) is read as `name_len`.
4. The loader attempts to read 170 million bytes as a name string — far exceeding any real file
   size — causing an immediate parse failure.

Parse errors on all 11 BANI files were confirmed by the animscan census tool. No separate BANI
loader branch was found in the binary after an exhaustive search. These files are not referenced
at runtime by any code path in the shipping client.

**Implication for Assets.Parsers:**

1. **Sniff the first 4 bytes.** If they equal `42 41 4E 49` (ASCII `"BANI"`), skip or log and
   continue — do not attempt standard header parsing.
2. BANI files may be safely excluded from the production animation catalogue. If future
   completeness requires them, a dedicated BANI parser can now be built from the **header table
   above plus the standard track/keyframe body** (§BANI body layout): parse the BANI header, then
   read the body exactly as the standard track array (`track_count` × `[u32 descriptor + u32
   key_count + key_count × 28-byte keyframes]`). This body decode is SAMPLE-VERIFIED on one file.
3. Mark any BANI file in the catalogue as `NonLoadable` in the parser output — do not surface them
   to `Client.Application` as valid clips, even though their body is now decodable; the shipping
   client never plays them.

### BANI payload note

The payload after `track_count` is now **decoded** — it is the standard track/keyframe array
(§BANI body layout), not an unknown structure. The earlier "not yet decoded; possibly f16
quantized" wording is superseded: a version-1 BANI file reconciles to EOF with zero residual under
the standard f32 / 28-byte keyframe layout. A quantized variant in the version-3 (77- and 67-bone)
groups was not exhaustively excluded but is LOW RISK. Because BANI files are non-loadable by the
shipping client, decoding the body is still not required for the production `Assets.Parsers`
implementation — sniff and skip them — but the body format is no longer an open unknown.

---

## Clip catalogue

The engine maintains a runtime map from a u32 key to the loaded clip object. Two distinct numeric
identities are involved, and they are used at different stages — this distinction was previously a
known unknown and is now resolved.

**CONFIRMED:**

- **`id_b` is the load-time catalogue key.** It is passed as the lookup key when registering a clip
  into the runtime clip map during loading. It is shared across all clips belonging to the same
  actor motion set (all three earlier reference samples carried `id_b = 7741`, confirming grouping
  semantics). This is the key the loader uses to find clips by group / set.
- **`id_a` is the per-file unique identifier and the runtime clip handle.** It matches the decimal
  integer in the filename (e.g. filename `g170354502.mot` → `id_a = 170354502`). It is returned to
  callers as the clip handle after registration, and the runtime mixer uses `id_a` as the per-clip
  identifier when starting or locating an individual cycle or action layer. It is NOT used as the
  load-time catalogue key.

In short: `id_b` answers "which set does this clip belong to" (used while loading); `id_a` answers
"which exact clip is this" (used by the mixer to address an already-active layer). Both descriptions
are correct once read in their respective contexts.

> **Loaded-clip object footprint (informative, for struct recovery).** Each loaded clip is a single
> heap object of **80 bytes (0x50)**, allocated once per clip when the registry first finds-or-creates
> it. Stage 1 fills its header-derived fields (the two ids, the `frame_count × 0.1` duration as f32,
> and a "fully loaded" flag, all distinct slots); Stage 2 fills the track array. A parser in
> `Assets.Parsers` does not need to reproduce this in-memory object — it is recorded only as a hint for
> anyone recovering the runtime layout; the on-disk format above is the authority for parsing.

> **CYCLE 7 reconciliation — the `actormotion.txt` motion-id resolve uses the `id_b`-keyed map (binary
> wins).** This section previously stated "the 9-digit motion IDs in `actormotion.txt` are `id_a`
> values." CYCLE 7 static read-site evidence shows the **play-time step that turns an `actormotion.txt`
> array-A motion id into a loaded clip is a lookup in the load-time clip registry, which is keyed by
> the `.mot` header `id_b`** (see §Motion id registry). So the array-A motion id is matched against
> `id_b`, not against the mixer's `id_a` handle. The two registries are genuinely distinct: (a) the
> **load-time clip registry** keyed by `id_b` — the one the array-A motion id resolves through; and
> (b) the **mixer's per-layer lookup** keyed by `id_a` — which addresses an *already-resolved* clip
> object among the active cycles/actions (see §Mixer). The earlier "the `actormotion.txt` ids are
> `id_a`" wording is **superseded** for the *clip-resolve* step: prefer the CYCLE-7 use-site evidence
> (array-A motion id == target `.mot` header `id_b`). The sample-observed 89.1% filename hit rate for
> col15 values is unaffected — it measures that the stored ids name real `.mot` files; CYCLE 7 only
> pins *which header field* the registry matches them on.
>
> *evidence: [static-ida]; ida_anchor: 263bd994; CYCLE 7.*
>
> **SHARPENED static bound (deep-cartography pass, f61f66a9, 2026-06-29):** The per-actor clip
> registry at actor+0x510 is confirmed as a `std::map<unsigned int, CoreAnimation*>`, and
> `MotClipList_SampleByTime` sets a newly-created layer's identity field to `clip+0x44` (= id_a) and
> subsequently matches refresh attempts via `layer+0x08 == motion_id`. For the refresh-match to ever
> succeed, `motion_id` must equal `clip+0x44` (id_a), implying the **per-actor registry is id_a-keyed
> and the actormotion array-A ids are in the id_a namespace** — which contradicts the CYCLE-7 id_b-keyed
> claim above. A two-registry architecture (boot-time id_b-keyed master populated by `motlist.txt`, plus
> a per-actor id_a-keyed subset populated at first play) is one reconciliation that could make both
> observations true. **Neither this spec nor `anim_runtime.md` is overwritten** — the conflict is
> escalated to `re-validator` for a single live `?ext=dbg` read of the `motion_id` value against the
> resolved clip's id_a and id_b. See `Docs/RE/structs/anim_runtime.md` Q1 (full sharpened static bound).

Neither field encodes a format version. Both fields carry semantic identity values assigned at
content-creation time.

---

## `actormotion.txt` layout

`data/char/actormotion.txt` is the actor-facing motion table. It is a tab-separated text file with
a decimal count on the first line and one record per subsequent line. Each record has **33 columns**.

> **In-memory record size and lookup key (CODE-CONFIRMED).** Each parsed row is stored as a
> fixed **136-byte (0x88)** in-memory record. The record is filed under a lookup key derived from
> the first two columns: `key = col1 + categoryBase[col0]`, where `col0` is a small category /
> actor-group selector and `categoryBase[]` is a per-category base-offset table held on the catalogue
> object (the **same** base-offset table the skin catalogue uses — see `specs/skinning.md`). This
> turns a locally numbered row id into a globally unique motion-set key. **Column 2 (`id_b` /
> SkinClassId)** is the per-actor skeleton/skin selector. The 18 motion-id columns are stored as
> **two runs of 9** (a primary run and a secondary run), and two derived per-frame-rate fields are
> computed from the cycle-duration and frame-count columns (see *Derived fields* below). The exact
> contents of `categoryBase[]` are **UNVERIFIED** (a live array dump is needed); the record size,
> key shape, column-2 role, and 9+9 motion-id split are CODE-CONFIRMED.
>
> <!-- pending live-debugger value-edge: actormotion/skin catalogue categoryBase[] array contents -->
The byte offsets below are the offsets within the engine's in-memory record (a fixed 136-byte
record) into which each column is parsed; they are stable and parser-derived. Per-column **semantic
names for columns 3–14 are PROPOSED** — the read order, types, and offsets are confirmed, but the
meanings are inferred from sample values and usage and should be cross-checked before being relied
upon. **The runtime stand idle is column 16 (record +0x44, direction-array-A element 1) — NOT
column 15 (CYCLE 7 use-site correction; see §Runtime idle slot below).** Column 15 (+0x40, array-A
element 0) is written by the loader but has no runtime read-site; it is unused/padding for clip
selection.

### File structure

| Element | Description |
|---------|-------------|
| Line 1 | Decimal record count. The file declares **1084** records, but the production parser parses **1080** rows (see §Declared vs parsed count). |
| Lines 2…N | One record per line, 33 tab-separated values, parsed in the column order below. |

**Column count is uniform: every data line has exactly 33 columns** — SAMPLE-VERIFIED across the
whole file (no per-line variation).

### Record layout (parser-derived offsets within the 136-byte record)

| Column | Type | Record offset | Proposed field | Notes |
|-------:|------|--------------:|----------------|-------|
| 0  | u32 | (key input) | `group_type` | Actor class / visual group selector. Used together with column 1 to compute the record key (see §Record key). |
| 1  | u32 | (key input) | `row_id` | Row index within the motion set; combined with `group_type` to form the global key. |
| 2  | u32 | +0x04 | `skin_class_id` | **SkinClassId.** Selects the character's skeleton/skin: maps to `data/char/bind/g{id}.bnd` (and the `.skn` whose `id_b` equals this value). A value of **0** means no skeleton (null pointer; login/camera/special actors). SAMPLE-VERIFIED: 95.8% of rows resolve to an existing `.bnd`. |
| 3  | f32 | +0x08 | `cycle_duration_a` | Cycle duration in seconds, set A (e.g. 7.402). PROPOSED meaning. |
| 4  | u32 | +0x28 | `frame_count_a` | Frame count for set A. Clamped to a minimum of 1 (a parsed 0 is forced to 1). PROPOSED meaning. |
| 5  | f32 | +0x0C | `cycle_duration_b` | Cycle duration in seconds, set B (e.g. 16.282). PROPOSED meaning. |
| 6  | u32 | +0x2C | `frame_count_b` | Frame count for set B. Clamped to a minimum of 1. PROPOSED meaning. |
| 7  | u32 | +0x10 | `flags` | Bitfield (purpose UNVERIFIED; sample value 0). PROPOSED meaning. |
| 8  | f32 | +0x14 | `phase_a` | Phase / timing parameter A (e.g. 4.0). PROPOSED meaning. |
| 9  | f32 | +0x18 | `phase_b` | Phase / timing parameter B (e.g. 5.0). PROPOSED meaning. |
| 10 | f32 | +0x1C | `phase_c` | Phase / timing parameter C (e.g. 3.0). PROPOSED meaning. |
| 11 | f32 | +0x20 | `weight_a` | Blend weight A (e.g. 1.0). PROPOSED meaning. |
| 12 | f32 | +0x24 | `weight_b` | Blend weight B (e.g. 8.0). PROPOSED meaning. |
| 13 | f32 | +0x38 | `speed_override_a` | Playback speed override A (e.g. 4.0). PROPOSED meaning. |
| 14 | f32 | +0x3C | `speed_override_b` | Playback speed override B (e.g. 1.0). PROPOSED meaning. |
| 15 | u32 | +0x40 | `motion_ids_a[0]` (UNUSED) | **Array-A element 0 — unused/padding for clip selection (CYCLE 7).** Written by the loader, but has **zero runtime read-sites**: it is NOT the idle slot. (The earlier "`idle_motion_id` = col15/+0x40" label is REFUTED — the off-by-one; see §Runtime idle slot.) Its stored value is still a `.mot`-clip-id-shaped number (the col15 file values hit existing `.mot` files 89.1% of the time — §col15), but no runtime consumer reads +0x40. |
| 16 | u32 | +0x44 | `idle_motion_id` (`motion_ids_a[1]`) | **Idle / stand motion (the slot the runtime actually plays).** Read by every motion-kind-0 (stand) idle path at record +0x44. The stored motion id is matched against a `.mot` clip via the `id_b`-keyed registry (§Motion id registry). Zero = empty slot. **CYCLE 7 use-site CONFIRMED.** |
| 17–23 | u32 ×7 | +0x48 … +0x60 | `motion_ids_a[2..8]` | Remaining primary motion-ID array (a[2] = walk +0x48, a[3] = run +0x4C, a[4] = death +0x50, a[5] = default-idle-cycle / state-8 idle +0x54, a[6] = alt-idle / motion-kind 1 +0x58; a[7] +0x5C and a[8] +0x60 have no static consumer — OPEN-RISK). Same encoding; zero = empty slot. |
| 24–32 | u32 ×9 | +0x64 … +0x84 | `motion_ids_b[9]` | **SFX / EFFECT-event id array — NOT motion-clip ids** (CORRECTED — binary-won reversal; see §motion_ids_b note below). Lifecycle-keyed event ids fed only to the sound/effect routers, never the animation mixer/sampler. By the loader's own indexing **b[0] = +0x64** (unused/padding, no read-site), so: b[1] (+0x68) = spawn sound/event id, b[2] (+0x6C) = walk footstep SFX id, b[3] (+0x70) = run footstep SFX id, **b[4] (+0x74) = death effect/sound id**; b[5..8] (+0x78 … +0x84) have no static consumer (OPEN-RISK). |

> **§motion_ids_b — the two arrays are DIFFERENT KINDS (see also `formats/actormotion.md`).**
> These two 9-slot integer runs at `+0x40` and `+0x64` are the **same bytes** that
> `formats/actormotion.md` calls `motion_ids_a[9]` / `motion_ids_b[9]`, and both docs use those names.
> But the two arrays do **not** carry the same kind of value:
>
> - **`motion_ids_a` (+0x40) IS the `.mot`-clip array** — action-keyed, with **element 0 unused**
>   (CYCLE 7): the runtime consumers start at element 1. a[1] = stand idle (+0x44, col16), a[2] = walk
>   (+0x48), a[3] = run (+0x4C), a[4] = death (+0x50), a[5] = default-idle-cycle / state-8 idle (+0x54),
>   a[6] = alt-idle (motion-kind 1, +0x58); a[0] (+0x40, col15) and a[7]/a[8] (+0x5C/+0x60) have no
>   runtime read-site. Its non-zero slots demonstrably hold real `.mot`-clip-id values (the col15 file
>   values alone resolve to an existing clip 89.1% of the time; a random mapping would resolve ≈0%).
>   Confirmed motion ids; resolved to clips through the `id_b`-keyed registry (§Motion id registry).
> - **`motion_ids_b` (+0x64) does NOT hold motion ids.** It is a lifecycle-keyed **SFX / EFFECT-event
>   id** array, with **element 0 unused** by the loader's own indexing (b[0] = +0x64, no read-site):
>   every runtime consumer feeds its slots to the SOUND / EFFECT routers, and **no b-slot ever reaches
>   the animation mixer or sampler**. The decisive fact is the consumer — the b-slots are read on the
>   audio/effect path, not the deform path. Known slot meanings: b[1] (+0x68) = spawn sound/event id,
>   b[2] (+0x6C) = walk footstep SFX id, b[3] (+0x70) = run footstep SFX id, **b[4] (+0x74) = death
>   effect/sound id**; b[0] (+0x64) and b[5..8] (+0x78 … +0x84) are OPEN-RISK (no static consumer
>   identified). *(This `b[4]` indexing matches `formats/actormotion.md`; the earlier "death = b[5]"
>   labelling was the off-by-one — CORRECTED CYCLE 7.)*
>
> **CORRECTED CYCLE 1 (ida_anchor 263bd994, 2026-06-19): `motion_ids_b` = SFX/FX event ids, NOT
> secondary motion (spec reversal, binary-won — confirmed by use-site; see `formats/actormotion.md`).**
> This OVERRIDES the prior "secondary motion-ID array, same encoding as the primary array" reading and
> the earlier "9 compass directions plus a neutral slot" / per-direction interpretation of these
> arrays — both are dropped. The slots are action/lifecycle-keyed, never direction-keyed.
>
> *evidence: [static-ida]; ida_anchor: 263bd994.*

### col2 → `.bnd` coverage (sample-verified)

Mapping col2 (`skin_class_id`) → `data/char/bind/g{id}.bnd` resolves for **1,035 / 1,080 = 95.8%** of
rows (re-confirmed on build `263bd994`). The 45 non-resolving rows break down as: **15 rows with
`skin_class_id = 0`** (null skeleton — login/camera/special actors that take a different code path;
treat 0 as a null pointer, no skeleton), and **29 rows** referencing `.bnd` ids absent from the
preserved VFS (expected gaps). **This confirms col2 is the SkinClassId**, the actor-to-skeleton key.

### Runtime idle slot — column 16 (record +0x44, array-A element 1), NOT column 15 (CYCLE 7)

The slot the live engine actually plays for a standing actor (motion-kind 0) is **direction-array-A
element 1 = record +0x44 = actormotion column 16**. Every motion-kind-0 (stand) idle dispatcher reads
the record at +0x44; **record +0x40 (array-A element 0 = column 15) has zero runtime read-sites** and
is statically dead for clip selection. This is the same element-0-unused off-by-one the B-array carries
(b[0] = +0x64 unused; first consumed element is element 1) — see `formats/actormotion.md`. The earlier
"`idle_motion_id` = col15 / record +0x40 / `motion_ids_a[0]`" claim (and the prior memory note that
"fixed" col16→col15) is **REFUTED, binary-won (CYCLE 7)**; the correct runtime stand slot is **col16
(a[1], +0x44)**.

Sibling idle slots (all CYCLE 7 use-site confirmed): a[5] (+0x54, col20) = default-idle-cycle /
state-8 special idle; a[6] (+0x58, col21) = alt-idle (motion-kind byte == 1). So "idle" is not one
slot: the standing/stand-still idle is a[1] (+0x44, col16); the default-idle-cycle is a[5]; the
alt/combat idle is a[6].

> **Motion-kind dispatch — only kinds 0 and 1 read the AnimCatalog record (binary-won, counter-check
> IDB SHA 263bd994, static-only).** The idle/stand dispatcher switches on the actor's **motion-kind
> byte (actor field +964)** and reads the per-appearance AnimCatalog record **only for two kinds**:
> - **kind 0 (stand/idle)** → `record + 0x44` (array-A element 1 = column 16), the single idle read
>   site (record +0x40 / column 15 is NOT read);
> - **kind 1 (alt-idle)** → `record + 0x58` (a[6]).
>
> All **other** motion kinds (**4, 0x13, 0x2D, and the default case**) do **not** index the
> per-appearance AnimCatalog record at all — they index a **flat per-class motion array held on the
> `ActorVisualGlobal` singleton** (the global motion-kind table), addressed by a per-kind base offset
> (the four base offsets are **352 / 1492 / 3468 / 48**) with a **19·kind** stride and a **4·class**
> step (class read from actor +168 = descriptor +0x34). So the per-appearance `actormotion` record
> governs only the stand (kind 0) and alt-idle (kind 1) clips; the remaining motion kinds resolve
> through the global table, not this 136-byte record. The chosen clip handle is fed to the per-actor
> mixer at rate 1.0; the char-select lineup additionally scales idle playback ×3 (actor playback-rate
> field ×= 3.0). The semantics of the 4 / 0x13 / 0x2D global-table kinds are out of scope here
> (mechanism confirmed; per-kind meaning carried OPEN — do not invent).

**The runtime idle catalogue lookup is keyed by the APPEARANCE KEY, NOT col2/skin_class (CYCLE 7).**
At play time the idle dispatchers look the actor's animation record up by the actor's stored
**appearance key** (for a player, `key = 5·(class + 4·variant) − 24`), via an ordered-map lookup over
the AnimCatalog (an ordered map keyed by that integer appearance key, NOT a flat `[index]` array; its
value record is 136 bytes, value stride 136). The value record's `+0x04` sub-id chains into a second
registry (the char-visual registry) whose value is the resolved visual/skeleton handle. **col2 /
skin_class selects the `.bnd` skeleton and helps BUILD the catalogue record's key at load time — it is
NOT the runtime idle lookup key.** (See `specs/skinning.md` §8 for the two-hop spawn chain.)

### col15 / col16 → `.mot` coverage (sample-verified)

Mapping the col15 stored values → `data/char/mot/g{id}.mot` yields an **89.1% hit rate** (re-confirmed
on build `263bd994`; the remaining rows are intentionally-empty zero-id slots and ids referencing
clips absent from the preserved VFS). The first data row's col15 = 101100001, which equals exactly the
header-id integer of `g101100001.mot`. A random mapping would hit near 0%, so this **empirically
confirms the col15/col16 entries are `.mot`-clip-id references** (the stored ids name real `.mot`
files). Across the whole file, **74.5% of all non-zero motion-id slots** (cols 15–32) resolve to an
existing `.mot` file. *(Note: this sample test measures that the stored ids name real `.mot` files; it
does not by itself distinguish col15 vs col16 as the runtime slot — CYCLE 7's use-site evidence settles
that the runtime stand slot is col16. And per §Motion id registry the registry matches an array-A
motion id against the target `.mot`'s header `id_b`, not its leading-digit filename id — the filename
correspondence is a naming convention, the registry join is on `id_b`.)*

### Declared vs parsed count (discrepancy noted)

The file's line-1 count declares **1084** records, but the production parser yields **1080** parsed
rows — a difference of **4 rows**. The 4 missing rows are most likely blank/structural separator lines
silently skipped by the parser's `actorClassId` parse guard. This is a benign declared-vs-parsed
mismatch, not a layout error; parsers should not trust the declared count as the exact iteration bound
and should tolerate skipped/blank lines. SAMPLE-VERIFIED.

### Derived fields (computed after parsing, not present in the text)

| Record offset | Type | Computed value | Notes |
|--------------:|------|----------------|-------|
| +0x30 | f32 | `cycle_duration_a × 15.0 / frame_count_a` | Scaled rate for set A. |
| +0x34 | f32 | `15.0 × cycle_duration_b / frame_count_b` | Scaled rate for set B. |

The constant `15.0` here is distinct from the on-disk `.mot` 10 fps timing; `actormotion.txt`
appears to express its rates against a 15-unit base. The relationship between this 15-unit base and
the `.mot` 10 fps clip rate is UNVERIFIED.

### Record key

The global record key is formed from the two key-input columns:

```
record_key = row_id + group_base(group_type)
```

where `group_base(group_type)` is a per-group base offset looked up from the manager object using
`group_type`. This produces a globally unique motion-set index from a locally numbered `row_id`,
so different groups can reuse small `row_id` values without collision.

### Sample record (first data line)

For reference (sample-derived, illustrative): `group_type = 0`, `row_id = 1`, `skin_class_id = 1`,
`cycle_duration_a = 7.402`, `frame_count_a = 16`, `cycle_duration_b = 16.282`, `frame_count_b = 11`,
`flags = 0`, `phase_a/b/c = 4 / 5 / 3`, `weight_a/b = 1 / 8`, `speed_override_a/b = 4 / 1`,
`motion_ids_a` = several non-zero 9-digit `.mot`-clip IDs followed by zeros, `motion_ids_b` = a few
non-zero 9-digit SFX/effect-event IDs followed by zeros (event ids, not `.mot` clips — see
§motion_ids_b).

> A sibling table, `data/char/skin.txt`, uses a related but different 6-column key-and-identity
> layout and belongs to the skin / bind catalogue, not the motion catalogue. It is out of scope
> for this format and noted here only to prevent confusion.

---

## Static idle clips — the human col15 stand idle carries no motion (SAMPLE-VERIFIED)

> **Key data finding.** The standing-idle `.mot` resolved by the recovered `actormotion.txt` col15
> chain for a human class is **genuinely static data** — a fixed stand pose held across all its
> frames. A port that renders a frozen standing human from this clip is therefore **faithful to the
> asset**, not exhibiting a parser bug or a missing animation. The deform/skinning narrative of this
> finding (and the still-open runtime question) lives in `specs/skinning.md` §10; the data evidence is
> recorded here because it is a property of the `.mot` corpus.

### The subject clip

The `actormotion.txt` row for `skin_class = 1` (the first human / Musa class) has
`col15 = motion_ids_a[0] = 101100001`, which names `data/char/mot/g101100001.mot`. The keyframe-diff
below was run on **that col15 file** and shows it is static data.

> **CYCLE 7 caveat — col15 is array-A element 0, which is NOT the runtime stand slot.** The runtime
> stand idle for a standing actor is array-A **element 1 = column 16** (record +0x44), not col15/+0x40
> (see §Runtime idle slot). The "STATIC, 0-animated-track" finding below is **sample-verified for the
> specific col15 file `g101100001.mot`**; it is not a claim about whatever clip column 16 resolves to.
> Whether the col16 runtime stand clip is itself static is not established from these dirty findings —
> treat the static-data property as a property of the col15 file specifically, and flag that the
> *runtime* stand slot is col16 (its clip's animated-vs-static character is open / capture-debugger
> pending).

### Keyframe diff (SAMPLE-VERIFIED via the production parser)

The clip was decoded with the production parser and every bone track's consecutive keyframes were
diffed. The result is summarised as aggregate deltas only (no payload bytes cross the firewall):

| Property | Observed |
|----------|----------|
| `frame_count` | 3 |
| `track_count` | 84 |
| keyframes per track | 3 on **all** 84 tracks (a full-shape clip, not a stub or single-key file) |
| tracks with any animation (consecutive-keyframe delta > 1e-6) | **0 of 84** |
| maximum translation delta across all tracks | **0.0** (exactly zero on every bone) |
| maximum rotation delta `1 − |dot(q_n, q_{n+1})|` | **≈1.0e-6** (one bone, at the float-noise floor) |

The clip therefore pins the skeleton to a single fixed stand pose held identically across its three
frames. Translation never changes; the lone sub-microradian rotation delta is last-bit float noise,
not motion. **Verdict: STATIC.** SAMPLE-VERIFIED via a throwaway harness over the maintainer's own
legally-owned VFS sample, driving the production `Assets.Parsers` decoder.

### Positive controls — the metric detects motion when it is present

Decoded under the identical metric, other clips animate strongly, confirming the silence of the
col15 idle is a real property of that file and not a parser/metric blind spot:

| Clip role | `frame_count` | tracks | animated tracks | max trans Δ | max rot Δ |
|-----------|:-------------:|:------:|:---------------:|:-----------:|:---------:|
| Mob clip A | 36 | 33 | 19 / 33 | 0.337 | 0.0192 |
| Mob clip B | 121 | 61 | 59 / 61 | 0.067 | 0.0097 |
| Human `peace`-tagged slot (a different `motion_ids_a` slot, same Musa row) | 30 | 84 | 51 / 84 | 0.0059 | 0.00054 |
| Human combat slot (a different `motion_ids_a` slot, same Musa row) | 35 | 84 | 65 / 84 | 1.42 | 0.092 |

The translation deltas of the animated controls are **5–6 orders of magnitude** above the idle's
zero, and their rotation deltas **4 orders** above its 1e-6 noise floor — the metric is sound.

### Implication

- The human rig **does** carry animated idle content (a subtle breathing-sway slot animates 51 of 84
  bones), but it is **not** the specific col15 file `g101100001.mot`, which is the static stand
  snapshot.
- **CYCLE 7 update:** the runtime stand slot is array-A **element 1 = column 16** (record +0x44), not
  col15/+0x40 (§Runtime idle slot). The static-stand finding above is for the col15 *file*; the clip
  the col16 runtime slot resolves to is a separate question — its animated-vs-static character is not
  established from these findings.
- A visible "breathing" standing idle in the port would require the col16 runtime slot (or another
  animated slot) to resolve to an animated clip, which is a **runtime motion-selection + clip-content**
  question — not a parser or missing-animation fix.
- **`debugger-pending`:** the exact runtime stand behaviour for a standing human — i.e. whether the
  col16 (+0x44) clip the runtime selects is static or animated, and how its content compares to the
  static col15 file — is unresolved without the live debugger / VFS confirmation of the col16 clip.
  The on-disk content of the col15 file is settled (STATIC); the col16 runtime clip's content and the
  live selection are not. See `specs/skinning.md` §10.

| Confidence |
|---|
| Col15 idle clip is STATIC data: SAMPLE-VERIFIED (production-parser keyframe diff + positive controls). Runtime slot selection at play time: `debugger-pending`. |

---

## motion.cache / effect.cache size-math

`data/motion.cache` and `data/effect.cache` are read through a **direct OS file call** (not the VFS),
so they are absent from the VFS itself. They were not byte-observed in the validated install, but the
recorded file sizes cross-check the documented `[u32 count][count × u32 id]` layout exactly:

| File | Size | Layout check | Implied count |
|------|-----:|--------------|--------------:|
| `motion.cache` | 44 B | `4 + count × 4` → `count = (44 − 4) / 4` | 10 IDs |
| `effect.cache` | 72 B | `4 + count × 4` → `count = (72 − 4) / 4` | 17 IDs |

Both sizes are **exactly** consistent with the documented wire layout — an indirect confirmation
without reading the files. Magic / versioning remains UNVERIFIED.

---

## Animation mixer — runtime blend model

This section describes the runtime layering and blending model as observed. It is informative for
`Assets.Parsers` and mandatory background for `Client.Application`. It documents behaviour and
constants only — no in-memory object layout is exposed, and no parser needs to reproduce these
structures to decode a `.mot` file. The downstream **deform** of a skinned mesh using the accumulated
pose (linear-blend skinning, inverse-bind, pose composition) is specified in `specs/skinning.md`.

### Two clip lists

The mixer owns two independent, separately-iterated lists of active clip layers:

| List name    | Behaviour | Clip type |
|--------------|-----------|-----------|
| **Action list** | One-shot: plays once and expires. Removed from the list when it ends. | `AnimationActionLayer` |
| **Cycle list**  | Looping: replays continuously until faded out and removed. | `AnimationCycleLayer` |

Each frame the mixer advances both lists, then a separate accumulation pass samples every active
layer in both lists into a single pose. A clip is addressed within a list by its `id_a` value
(§Clip catalogue): starting or updating a cycle/action looks up an existing layer by `id_a`, and a
new layer stores the clip's `id_a` for subsequent lookups.

### Per-frame update sequence

Each frame the mixer performs the following steps in order:

1. **Advance the sync phase** (see §Sync-phase mechanism). If there are no sync-mode cycles, the
   sync phase is reset to zero.
2. **Advance the Action list.** Each action layer is ticked with the frame delta time. An action
   that has finished is removed; when the Action list becomes empty, an "actions empty" callback
   fires.
3. **Advance the Cycle list.** Each cycle layer is ticked with a delta time scaled by the owning
   visual's per-character speed factor (`scaled_dt = dt × character_speed_scale`). A cycle whose
   effective weight has decayed to zero is removed. When a looping cycle wraps, a footstep
   sound-effect callback fires.
4. **Recompute the sync range** (see §Sync-phase mechanism) from the current set of sync-mode
   cycles and their weights.
5. **Accumulate the pose** across both lists (see §Per-bone weighted accumulation), then apply
   heading smoothing and submit the skinned pose to the rendering pipeline.

The character speed factor in step 3 is a per-visual scalar (UNVERIFIED in origin — sourced from
character speed data); a value of `1.0` leaves playback unscaled.

### Sync-phase mechanism

Looping cycles can run in one of two timing modes:

- **Sync mode:** the layer's sampling time is driven by a single mixer-wide sync phase rather than
  by its own local clock. This keeps multiple simultaneously-blended cycles phase-locked (for
  example, a walk and a run blend stay foot-synchronized).
- **Free-running mode:** the layer advances its own local time independently.

The mixer maintains two scalar quantities for sync mode:

| Quantity | Meaning |
|----------|---------|
| `sync_phase` (seconds) | A mixer-wide playback phase, advanced each frame and wrapped via modulo against `sync_range`. |
| `sync_range` (seconds) | A weighted-average target duration, recomputed each frame from the active sync-mode cycles. |

**Phase advance.** Each frame, when `sync_range` is non-zero:

```
sync_phase += dt × 1.5
if sync_phase >= sync_range:
    sync_phase = fmod(sync_phase, sync_range)
    set wrap-this-frame flag
```

The advance rate **1.5** is a confirmed constant (stored as an IEEE-754 double equal to 1.5). Its
purpose is hypothesized to be a 15-to-10 frame-rate ratio (15 fps ÷ 10 fps = 1.5) but this
interpretation is UNVERIFIED.

**Range recompute.** Each frame, `sync_range` is set to the weight-weighted mean clip duration over
all sync-mode cycles:

```
sync_range = sum(clip_duration[i] × weight[i]) / sum(weight[i])   over sync-mode cycles
sync_range = 0   if the total weight is zero
```

**Sync-mode sampling time.** A sync-mode cycle is sampled at:

```
sample_time = clip_duration × (sync_phase / sync_range)
```

so all sync-mode cycles share a common normalized phase scaled to their individual durations.
A free-running cycle is sampled at its own advancing local time instead.

When the sync phase wraps during a frame, the per-frame wrap flag triggers the footstep
sound-effect callback noted in step 3 above.

### Layer weight ramping (fade in / fade out)

Each layer carries an effective weight that is smoothly ramped toward a target weight over a
remaining blend time, rather than snapping. Conceptually, per tick:

```
if blend_time_remaining > dt:
    factor = dt / max(blend_time_remaining, 0.001)
    effective_weight = lerp(effective_weight, target_weight, factor)
    blend_time_remaining -= dt
else:
    effective_weight = target_weight
    blend_time_remaining = 0
    if target_weight == 0:
        the layer is finished and is removed from its list
```

The blend-time floor constant is **0.001 seconds** (confirmed), which prevents a divide-by-zero
when a blend time of zero is requested (i.e. an instantaneous weight change). Starting or updating
a layer supplies both a target weight and a blend time; a fade-out is requested by setting the
target weight to `0.0`.

Action layers run a small state progression instead of a single target — **fade-in → playing →
fade-out → done** — but the underlying weight behaviour is the same proportional ramp: the
effective weight rises from 0 toward 1 during fade-in, holds at 1 while playing, and falls back to
0 during fade-out, after which the layer is removed. An action layer may also carry a trigger
threshold: it does not contribute to the pose until its local time reaches that threshold.

A default per-layer playback speed constant of approximately **1.575** (a confirmed float literal,
≈1.574999) is set when a layer is constructed. Its exact role relative to the `actormotion.txt`
`speed_override` columns is UNVERIFIED and is tracked as an open item.

### Per-bone weighted accumulation

Each frame, after both lists have been advanced, the mixer builds the final pose by accumulating
every active layer's contribution per bone. For each layer, for each track in the layer's clip, the
track is sampled at the layer's current sampling time to produce a (translation, rotation) pair,
and that pair is folded into the accumulator for the matching bone using the layer's effective
weight:

1. **First contributor** for a bone (accumulated weight is zero): assign translation and rotation
   directly; set accumulated weight to this layer's weight.
2. **Subsequent contributors:**
   - `lerp_factor = new_weight / max(accumulated_weight + new_weight, 0.001)`
   - `accumulated_translation = lerp(accumulated_translation, src_translation, lerp_factor)`
   - `accumulated_rotation = slerp(accumulated_rotation, src_rotation, lerp_factor)`
   - `accumulated_weight += new_weight`

The denominator is floored at the same **0.001** constant used for blend timing. This is a running
normalized weighted average. With exactly two contributors the result is order-independent; with
three or more it is **order-dependent** (the result depends on the iteration order of the layers).
Implementers reproducing this behaviour must accumulate Action-list layers and then Cycle-list
layers in the same iteration order the engine uses to match results bit-for-bit. There is no
additive blend mode and no explicit per-layer priority — ordering is the only implicit priority.

### Heading smoothing and submission

After accumulation, the mixer applies a heading (yaw) smoothing step: it blends the actor's current
facing quaternion toward the target facing by a fixed factor of **0.2** per frame, sets the actor
orientation from the smoothed result, composes it with the accumulated bind-pose rotation, and
submits the final skinned pose to the GPU skinning path. This is purely a runtime presentation
concern and has no bearing on the `.mot` file format. The composition into final bone world
transforms and the deform that follows are specified in `specs/skinning.md`.

### Activating a cycle

To start or update a looping clip: provide the clip's `id_a`, a target weight, and a blend time. If
a cycle with that `id_a` already exists in the Cycle list, its target weight and blend time are
updated in place. If it does not exist and the requested weight is non-zero, a new cycle layer is
created and appended to the Cycle list. Stopping all layers sets every layer's target weight to
`0.0`, letting the weight-ramp logic fade them out and remove them.

---

## Enumerations / flags

No enumerated fields or bitflag fields exist in the on-disk standard `.mot` format. The upper three
bytes of `track_descriptor` (bits 8–31) are reserved padding and carry no enumerated meaning. There
is no on-disk interpolation-mode enumeration in the keyframe record either. The `flags` column in
`actormotion.txt` (§`actormotion.txt` layout, column 7) is a bitfield whose bit assignments are
UNVERIFIED. The BANI variant's `version` field (offset 4) takes the discrete values 1 and 3
(§BANI variant) but its full enumeration and effect on payload layout are not characterized.

---

## Known unknowns

The following aspects are unresolved and must not be assumed by the implementing engineer:

| Item | Status | Impact |
|------|--------|--------|
| Oversized standard clip trailing block (1 file, ≈+48,719 B) | DBG-pending (block) / IDENTIFIED (file) — the anomalous file is identified as the clip with `id_a 142206011`; its large trailing region after the parsed clip is still undecoded (possible appended/LOD/multi-clip data). All other standard files end with zero residual. | Read `track_count` tracks and stop; tolerate a positive EOF residual; do not parse the trailing bytes. Decoding the block is deferred and not needed for production. |
| BANI variant payload layout (post `track_count`) | RESOLVED — the body is the standard track/keyframe array (`track_count` × `[u32 descriptor + u32 key_count + key_count × 28-byte 7-f32 keyframes]`); one version-1 file reconciles to EOF with zero residual (§BANI body layout). Only the BANI header differs from standard. (A quantized variant in the version-3 groups is LOW RISK, not exhaustively excluded.) | Sniff and skip BANI files in the standard parser (they are non-loadable by the shipping client). A dedicated BANI parser can decode the body with the standard track layout if completeness ever requires it. |
| Up-axis / handedness / unit-scale of the keyframe float triples | CAPTURE/DEBUGGER-PENDING — the translation/quaternion floats are stored verbatim; the absolute axis orientation, handedness, and unit scale are a render-frame property not decidable from the static loader bytes alone (this re-verification lane was static-only by directive). | Decode the 7-float record as specified; confirm world placement against the project's recovered world conventions (world negates Z; `.skn` mesh-local negates X) in a live debugger session. Specified for the Godot path in `specs/skinning.md`. |
| BANI `rig_group_id` (offset 0x0C) interpretation | PROPOSED (interpretation) / SAMPLE-VERIFIED (values) — NOT constant: three values (`7830`, `7899`, `8881`) each paired one-to-one with a distinct `track_count` (52 / 77 / 67). The strict co-variation with bone count strongly supports a **skeleton / rig group id**. Cross-reference with the skeleton catalogue to confirm. | Carry the value through; do not branch on it. |
| BANI `version` effect on payload | UNVERIFIED — values 1 and 3 observed (version 1 ↔ 52-bone rig; version 3 ↔ both the 77- and 67-bone rigs). The one decoded body (version 1) uses the standard layout; whether version 3 bodies differ is unconfirmed (LOW RISK). | Treat as informational metadata; decode the body with the standard track layout and verify per-file. |
| `actormotion.txt` per-column semantics (cols 3–14) | UNVERIFIED — record layout, types, and offsets are confirmed; the proposed names (`phase_*`, `weight_*`, `speed_override_*`, `flags` bit meanings) are inferred from sample values. Cols 2 and 15 are now sample-verified and removed from this caveat. | Parse by offset and type; treat cols 3–14 field names as provisional until cross-checked against the actor controller. |
| `actormotion.txt` 15-unit rate base | UNVERIFIED — derived rate fields use a `15.0` base; relationship to the `.mot` 10 fps clip rate is unknown. | Compute the derived rates as specified; do not assume the bases are interchangeable. |
| Default layer speed constant (≈1.575) | UNVERIFIED — a confirmed float literal set at layer construction; its interaction with `actormotion.txt` `speed_override` columns is unknown. | Document the constant; do not hard-wire playback speed to it until its role is confirmed. |
| Sync-phase 1.5× rate rationale | UNVERIFIED — the 1.5 multiplier is a confirmed constant; the "15 fps / 10 fps" interpretation is a hypothesis. | Implement the constant as-is; do not depend on the hypothesized rationale. |
| Character speed scale factor source | UNVERIFIED — the per-visual `scaled_dt` factor is sourced from character speed data not yet characterized. | Treat as a runtime input (default 1.0); not needed to decode `.mot` binaries. |
| `flags` bitfield meanings (`actormotion.txt` column 7) | UNVERIFIED — the column is a u32 bitfield; individual bits are uncharacterized (sample value 0). | Carry the value through; do not branch on undocumented bits. |
| Interpolation parameter normalization intent | UNVERIFIED — `alpha` is in raw seconds `[0, 0.1]`, not `[0, 1]`. Matches observed behaviour; whether intentional design or latent defect is unknown. | Implement as observed (raw seconds); document the deviation — see `specs/skinning.md` §8(c) for the Godot faithful-vs-smoothed choice. |
| `motion.cache` magic and versioning | UNVERIFIED — no header magic or version field confirmed; only the `[u32 count][u32[] ids]` layout (cross-checked by size math, §motion.cache / effect.cache size-math). | Parse defensively; treat as unversioned. |
| Variable frame rate (rates other than 10 fps) | UNVERIFIED — only 10 fps observed for `.mot` clips. | Treat 10 fps as fixed; flag any `frame_count` that produces an unexpected duration. |
| Text-mode `.mot` files in the wild | UNVERIFIED — the binary/text switch exists in the reader code, but no text-mode samples are known to exist. | Implement binary mode only. |
| Runtime standing-idle slot selection | RESOLVED (slot) / DEBUGGER-PENDING (content) — the runtime stand slot is **array-A element 1 = column 16, record +0x44** (CYCLE 7 use-site; NOT col15/+0x40, which is dead — §Runtime idle slot). The on-disk col15 *file* is settled as STATIC data (§Static idle clips); the content of the **col16** clip the runtime selects, and the full live selection, still need the debugger / VFS confirmation. | Render the **col16** runtime slot (+0x44), looked up by the appearance key, not col15. Render whatever clip col16 resolves to faithfully (do not synthesize a breathing idle if it is static). Confirm the col16 clip content live. |

The following items from previous spec revisions have been resolved and removed from the
unknowns table:

| Former item | Resolution |
|-------------|------------|
| "All `.mot` samples are stubs" | RESOLVED — a full census of 3,891 files shows 3,877 (99.7%) are real full-payload clips; only 3 are stubs and 11 are the BANI variant (§Corpus census). The track/keyframe layout is exercised by the corpus, not just by parser inference. |
| LenStr prefix width (1-byte vs 4-byte) | CONFIRMED 4-byte u32 LE prefix, no on-disk terminator — verified independently against both the loader and a real sample, which agree exactly. |
| Upper 3 bytes of `track_descriptor` | CONFIRMED unused padding — the loader reads the 4-byte word and extracts only the low byte (`bone_id`); bits 8–31 are discarded with no shift, mask, comparison, or storage. The three candidate interpretations (key/keyframe count, channel/component mask, interpolation flag) are positively REFUTED: keyframe length is driven by the separate `key_count` field, the 7-float keyframe set is fixed and unconditional, and interpolation mode is chosen at the runtime sampler. See §`track_descriptor` byte decomposition. |
| Keyframe is 7 floats / 28 B (trans XYZ + quat XYZW; no scale; no on-disk interp flag) and track stride = 8 + key_count×28 | CONFIRMED — CAMPAIGN VFS-MASTERY two-witness gate (full-data loader + black-box corpus census agree). The keyframe has no eighth scale float and no trailing interpolation byte; the track is an 8-byte preamble followed by `key_count × 28` bytes with no inter-record padding. See §Keyframe record and §Track array layout. |
| `id_a` vs `id_b` as catalogue key | CONFIRMED — `id_b` is the load-time clip-registry / group key; `id_a` is the per-file UID matching the filename integer, used by the mixer to address an already-active layer. **CYCLE 7:** the play-time step that turns an `actormotion.txt` array-A motion id into a loaded clip is the `id_b`-keyed registry lookup (the array-A motion id == target `.mot` header `id_b`), distinct from the mixer's per-layer `id_a` lookup (§Motion id registry, §Clip catalogue). |
| Wrap-to-first at clip end | CONFIRMED — no loop flag exists in the `.mot` binary; wrap is a runtime property of `AnimationCycleLayer` (modulo on local time), not of the file. |
| `actormotion.txt` column layout | CONFIRMED (record layout) — 33 columns, count-prefixed, parsed into a 136-byte record; offsets and types documented in §`actormotion.txt` layout. Per-column semantic names for cols 3–14 remain PROPOSED; col2 (SkinClassId) is sample-verified; the runtime stand-idle slot is col16 (+0x44, a[1]) per CYCLE 7. |
| Runtime stand-idle column = 16 (record +0x44, a[1]), NOT 15 | CONFIRMED (CYCLE 7, use-site) — every motion-kind-0 idle path reads +0x44; +0x40 (col15, a[0]) has zero runtime read-sites (§Runtime idle slot). REVERSES the prior "col15 = idle" claim. The col15/col16 stored values are `.mot`-clip ids (89.1% filename hit on build `263bd994`). |
| `.mot` magic: "no magic, starts with id_a" | CORRECTED — true for 3,880 standard-variant files, but 11 files use the `"BANI"` magic with a different header (§BANI variant). Parsers must sniff the first 4 bytes. |
| Animation mixer runtime blend model (provisional) | CONFIRMED — two-list architecture, sync-phase mechanism with 1.5× constant, weight ramping with 0.001 s floor, and per-bone normalized weighted-average accumulation (order-dependent for ≥3 layers) documented in §Animation mixer — runtime blend model. |
| "Is the human idle flat because the parser/loop is broken?" | RESOLVED — NO. The engine advances clip time every frame with real `dt = ms × 0.001` and interpolates between keyframes (§Per-frame clip-time advance), so the sampler is never pinned at `t = 0`; and the human col15 stand idle's keyframes are byte-identical (0 animated tracks — §Static idle clips). A frozen standing human is therefore **faithful to the static col15 asset**, not a parser bug. The runtime stand slot is now pinned to col16 (+0x44, a[1]) by CYCLE 7 use-site evidence; the only open piece is the *content* of that col16 clip (static vs animated) and the full live behaviour (DEBUGGER-PENDING, above). |
| BANI files loadable by shipping client | CONFIRMED NEGATIVE (SAMPLE-VERIFIED + CODE-CONFIRMED) — the standard loader has no magic-check branch; all 11 BANI files produce parse errors and are dead/unused data. Parsers must sniff and skip them. |
| BANI body layout "NOT YET DECODED" | RESOLVED — the body after `track_count` is the **standard track/keyframe array** (`track_count` × `[u32 descriptor + u32 key_count + key_count × 28-byte 7-f32 keyframes]`); one version-1 BANI file reconciles to EOF with zero residual (§BANI body layout). Only the header differs from the standard variant. The earlier "possibly f16-quantized / unknown packing" speculation is dropped (LOW-RISK caveat retained for version-3 groups). |
| BANI `unknown_field` / `track_count` "constant across all 11 files" | CORRECTED (binary/sample-won) — neither is constant. `unknown_field` (now named `rig_group_id`) takes `7830`/`7899`/`8881` and `track_count` takes `52`/`77`/`67`, co-varying one-to-one as three rig groups (§Per-file enumeration of all 11 BANI files). `version` is 1 for the 52-bone rig and 3 for both the 77- and 67-bone rigs. |
| Oversized standard clip anonymous | IDENTIFIED — the single oversized standard file is the clip with `id_a 142206011` (residual exactly 48,719 bytes; sole anomaly in a 559-file sample). The trailing block itself is still undecoded (DBG-pending — §Oversized standard clip). |

---

## Cross-references

- **Container:** `Docs/RE/formats/pak.md` — VFS archive that delivers `.mot` files.
- **Skeleton / skinned mesh:** `Docs/RE/formats/mesh.md` — `.bnd` bind-pose skeleton and `.skn` skin;
  the `self_id` field of each bone record is the link target for `bone_id` in `.mot` track records.
  Also defines the `LenStr` encoding (4-byte u32 LE prefix) confirmed for `.mot`.
- **Deform / skinning math:** `Docs/RE/specs/skinning.md` — how a sampled `.mot` pose is composed up
  the bone hierarchy and used to deform a skinned mesh (linear-blend skinning, inverse-bind bake,
  quaternion/handedness conventions, Godot import guidance, canonical test specimens).
- **Canonical names:** see `Docs/RE/names.yaml`
  (`MotionClip`, `BoneTrack`, `Keyframe`, `MotionClipManager`, `AnimationMixer`,
  `AnimationCycleLayer`, `AnimationActionLayer`; proposed `BaniMotClip`, `bani_magic`,
  `bani_version`, and `bani_rig_group_id` for the BANI offset-0x0C field). The shipping loaders
  re-affirmed on build `263bd994` are `CoreMot_LoadHeader`
  (Stage-1 header) and `CoreMot_LoadFullData` (Stage-2 track/keyframe data); the actor table loader is
  `ActorMotionTable_LoadFromTxt`; the shared field readers are `AssetStream_ReadInt32Field`,
  `AssetStream_ReadFloatField`, and `AssetStream_ReadLenStrToString`.
- **Provenance:** see `Docs/RE/journal.md` (entry for this spec is appended separately).
