---
status: confirmed
verification: confirmed (re-confirmed against IDB SHA 263bd994c927c20a38624cf0ca452eaef365057fa9db1543d8f668c14a6fd8ee, CYCLE 7 (2026-06-20)); CYCLE 11 spec-audit (2026-06-24): §3 chain B bgtexture kind polarity corrected to binary truth (kind==1 ⇒ static; other non-zero ⇒ non-static); all other confirmed claims re-confirmed
sample_verified: partial   # loader-selection mechanism, cache model and table strides CODE-CONFIRMED; the progress-bar visual outcome is debugger-pending
ida_reverified: 2026-06-27   # CYCLE 14 re-anchor (f61f66a9): confirmatory — boot bulk-loader progress denominator / global constant cleanly relocated, 1 re-confirmed SAME, 0 corrected; prior 2026-06-24: CYCLE 11 spec-audit bgtexture kind polarity corrected; prior 2026-06-16
ida_anchor: f61f66a9ae0ec1e946105b2ecff76e8930cb1d1367df64e5688a5266f5ad9963
evidence: [static-ida, vfs-sample]
subsystems: [loader_dispatch, ghtex_cache, asset_linkage, bulk_loader]
networked: false           # the asset pipeline is entirely client-side; no wire traffic
encoding_note: Korean in-game text and config strings are CP949 (MS-949 code page), not UTF-8.
conflicts: progress-bar visual outcome (does the ~9 MB boot set ever visibly fill the bar?) is debugger-pending — the arithmetic is confirmed, the on-screen result is not.
---

# Asset Loading Model — Clean-Room Specification

> Clean-room neutral spec. Promoted from dirty-room analyst notes under EU Software Directive
> 2009/24/EC Art. 6. No decompiler pseudo-code, no binary virtual addresses, no decompiler
> identifiers. Every behaviour below is re-expressed in the spec-author's own words and tables.
>
> **Scope.** Four things the asset layer needs, above the raw VFS byte fetch:
> (1) how a loader is *selected* for a file (the dispatch verdict), (2) the one real asset cache
> (the named-texture / GHTex cache), (3) the inter-asset *linkage chains* that stitch the VFS into
> a coherent graph, and (4) the *bulk boot loader* that pre-warms the world.
>
> **Out of scope / cross-references.** The `.inf`/`.vfs` container byte layout and the open-mode
> dispatch belong to `formats/pak.md`. The directory tree, per-extension census and manifest table
> belong to `specs/vfs_overview.md`. The boot worker timing, loading-screen rendering, terrain
> streaming and the high-level "universal cache pattern" are owned by `specs/resource_pipeline.md`;
> this spec deepens the *loader-selection* and *GHTex* detail rather than restating those.

---

## Verification banner

- **verification: confirmed** — `ida_reverified: 2026-06-27` against `ida_anchor: f61f66a9ae0ec1e946105b2ecff76e8930cb1d1367df64e5688a5266f5ad9963`; CYCLE 14 re-anchor: confirmatory — boot bulk-loader progress denominator cleanly relocated, 1 re-confirmed SAME, 0 corrected. Prior `ida_reverified: 2026-06-24` against `ida_anchor: 263bd994c927c20a38624cf0ca452eaef365057fa9db1543d8f668c14a6fd8ee`;
  `evidence: [static-ida, vfs-sample]`. CYCLE 11 spec-audit: §3 chain B bgtexture kind polarity corrected (binary wins — kind==1 ⇒ static); prior reverification 2026-06-16.
- Tiers used inline below:
  - **[confirmed]** — recovered directly from control-flow + operand facts (the dispatch verdict,
    the cache model, the global wiring, the boot-thread order, the progress arithmetic).
  - **[sample-verified]** — a control-flow fact *also* matched against a real VFS sample (the
    bgtexture.lst 48-byte record stride / `data/map000/texture/<rel>.dds` path, the msg.xdb 516-byte
    record stride, the terrain `idx-1` cell-byte chain). This is the strongest tier.
  - **[static-hypothesis]** — heap-layout / single-source items with no second witness (e.g. the
    "no budget sweep" claim rests on the absence of any reader of the accounting counters).
  - **[debugger-pending]** — genuinely runtime outcomes a live session would settle (the progress
    bar's on-screen fill behaviour during a real boot — see §4.4 / CONFLICT).
- The Campaign-10 re-verification (lane C3) re-drove every claim below against control-flow; the
  *selection mechanism*, *cache behaviour*, *global wiring*, *boot order*, and *progress arithmetic*
  are read directly from operands. No live debugger pass was taken this lane, so the *visual* progress
  outcome is the one debugger-pending residual.

---

## 1. Loader dispatch — the verdict (CONFIRMED)

> **Selection of which loader/parser handles a file is ALWAYS by call-site / hardcoded path
> template / extension. There is NO magic-byte sniffing anywhere in the client, and NO central
> "switch on file extension → loader" helper.**

The I/O layer (the VFS open router, `formats/pak.md`) answers only **where the bytes come from**
(slurp / raw archive seek / loose file). **How to parse** is fixed at the call site that chose the
path. Format identity is implicit in the caller.

Three independent confirmations:

1. **No image magic in the client.** A byte search for the DDS signature (ASCII `DDS ` and its
   byte-reverse) returns **zero matches** anywhere in the image. The client never compares a loaded
   buffer's leading bytes against a texture signature. (byte-search-verified)
2. **Format recognition, where it happens at all, lives inside a third-party codec.** The texture
   path slurps the whole entry and hands the raw bytes to the D3DX "create texture from file **in
   memory**" API, which inspects the in-memory header itself to decide DDS-vs-other. The two precise
   D3DX entry points are now pinned: the **VFS-mounted slurp branch** uses
   `D3DXCreateTextureFromFileInMemoryEx` (the *Ex* variant, fed the full option list from the texture
   element's option block), while the **loose-file branch** uses `D3DXCreateTextureFromFileExA`. OGG
   audio is handed to the Vorbis library (which reads its own `OggS` magic internally). Lua source is
   handed to the Lua loader. The client itself performs no header test in any of these paths.
   [confirmed]
3. **Every first-party loader picks its reader by the call site.** Each consumer formats a hardcoded
   path template (e.g. a skybox path, a numbered OGG path, an effect manifest path), opens it through
   the format-agnostic router, then drives its own structure-aware reader. The extension in the
   literal is purely descriptive — nothing branches on it. First-party container blobs (the map
   config, the skybox container, the `.lst` manifests, the fixed-size sound tables) are parsed by
   **internal section/record structure** (counts, fixed strides, positional sections), never by a
   leading file magic. (parser-verified)

### 1.1 Family → loader path-template table

"Open mode" is `1` (read, slurp when mounted / loose OS file when not) for every first-party loader
examined; none requests the raw-seek streaming branch. Container-section rows (`.ted` / `.bud` /
`.sod`) are **not** separate file opens — see §1.2.

| Family | Extension(s) | Path template / selection | Who recognises the format |
|---|---|---|---|
| UI / icon texture (one-shot) | `.dds` | call-site path literal | D3DX self-sniff (no client magic) |
| Managed named texture | `.dds` | call-site path string → named-texture handle | D3DX self-sniff |
| Surface / image blit | `.dds` (+ other D3DX-recognised) | call-site path | D3DX self-sniff |
| Character skin mesh | `.skn` | `data/char/skin/g{gid}.skn` | first-party structure reader |
| Bind-pose skeleton | `.bnd` | `data/char/bind/{name}` (from `bindlist.txt`, `g{IdB}.bnd`) | first-party structure reader |
| Map / scene config | `.map` | `data/map{NNN}/dat/d{aaa}x{cx}z{cz}.map` | first-party config parser; fans to sections |
| Terrain cell blob | `.ted` | **section inside `.map`** (positional) | parsed from the slurped `.map` |
| Mass-object scene | `.bud` | **section inside `.map`** (positional) | parsed from the slurped `.map` |
| Collision solids | `.sod` | **section inside `.map`** (positional) | parsed from the slurped `.map` |
| Sound (OGG) | `.ogg` | numbered template (`<prefix>{id}.ogg`) | Vorbis self-sniff |
| Sound tables | `.wlk/.run/.bgm/.bge/.eff` | five call-site templates per area | fixed-size read (no magic) |
| Effect manifests / registry | `.lst`, `.txt` (and `.xeff` records) | per-manifest path literal (`bmplist.lst`, `xobj.lst`, `xeffect.lst`, …) | u32 count + fixed-stride records; the xeffect list is explicitly **headerless** |
| Skybox container | `.box` | `data/sky/dat/sky{n}.box` | u32 texture-count + per-texture/per-mesh counts (internal structure) |
| Script (Lua) | `.lua` | call-site path (`game.lua`, `uiconfig.lua`, …) | Lua loader parses |
| Data tables | `.scr`, `.txt`, `.xdb` | **exact path literal per table** | first-party CP949-text / fixed-record reader |
| Message DB | `.xdb` | `data/script/msg.xdb` (+ per-table `.xdb` openers) | first-party structure reader |
| UI atlas + manifests | `.dds` + `.txt` (`uitex.txt`, …) | call-site path for manifest and each atlas | D3DX self-sniff for the image |

### 1.2 Why container-section rows are not a dispatch

`.ted`, `.bud`, `.sod` do not exist as independently-opened VFS entries on the hot path — they are
**sections inside the `.map` config blob**. The map loader opens ONE file (the `.map`), slurps it
whole, wraps it in a file object, and hands it to the config parser. That parser advances a cursor
through the blob and invokes the terrain / mass-object / collision section readers **in a fixed order
by position in the container** — not by any per-section magic tag and not by a second file open. The
"selection" for those families is therefore *positional within the container*, which is still
call-site logic (the parser hardcodes the section order), not a magic sniff. (Per-cell `.ted` and
texture assets referenced *by* the map config are then resolved by their own hardcoded path templates
through the same router — again call-site selection.)

### 1.3 The concrete open router (where the three-way branch lives) [confirmed]

The format-agnostic router is a single open function (with a by-name sibling) that answers **only**
where the bytes come from. It branches on the VFS-mounted flag crossed with a request **mode bit**:

| Branch | Condition | Behaviour |
|---|---|---|
| **Slurp** | mounted, read mode | TOC find-and-read: locate the entry, read its whole payload into a heap buffer (the `formats/pak.md` read primitive — global lock, 64-bit seek, `ReadFile`). |
| **Loose + VFS offset seek** | mounted, raw-seek mode bit set | open the packed `data.vfs` as a loose handle, read the entry's `dataOffset` / `dataSize` from the TOC record, and `SetFilePointer` to that 64-bit offset inside the archive (this is the concrete wiring of the "raw archive seek" branch referenced by `formats/pak.md`). |
| **Plain file** | not mounted | ordinary `CreateFileA` (read / write / create) against a loose on-disk path. |

Format identity is **not** decided here. The TOC offset/size fields the loose-offset branch reads
are the same `dataOffset` (+0x68) / `dataSize` (+0x70) fields of the 144-byte TOC record documented
in `formats/pak.md` / `specs/vfs_overview.md`.

> **Progress hook lives in this router.** The boot progress accumulator (§4.4) is incremented from
> **inside the open router** on every tracked read — this is the concrete tie between the read path
> and the boot progress meter. The accumulation is gated by the tracking flag, so reads outside the
> loading screen do not move the meter. Cross-reference `formats/pak.md` (read primitive) ↔ §4.4.

### 1.4 Implication for the reimplementation

`Assets.Vfs` stays format-agnostic (it returns bytes). `Assets.Parsers` selects a parser **at the
call site** — never by sniffing a header. A modern reimplementation may use the file extension as a
selector for convenience, but must understand that the original derives format identity purely from
*which call site* opened the path; there is no in-band format tag to validate against.

---

## 2. The GHTex named-texture cache [confirmed]

This is the **only real asset cache** in the client (the file-level VFS layer has no cache — every
fetch re-reads from disk, per `formats/pak.md`). It caches GPU textures by **logical name**.

### 2.1 Identity and sharing

- A single shared manager singleton holds the cache. **The named-texture manager and the visual
  effect manager are the same singleton object** — effect/billboard manifests and named textures
  share one name-keyed container. Most callers reach it through the effect/visual paths.
- **The sorted name pool is the `bmplist`-derived effect texture-name pool.** The name-keyed vector
  the binary search walks is the one populated from the `bmplist` manifest at effect-boot; i.e. the
  named-texture cache is keyed primarily off `bmplist.lst` entries. (This pins the provenance the
  earlier "name pool" phrasing left generic — cross-reference `formats/effects.md`.) [confirmed]
- Each cache element is a small object that owns: the logical name (the cache key), an
  "enabled" gate, a "loaded" flag, a last-use timestamp, the bound GPU texture handle (null until
  loaded), a VRAM byte count, and a pointer to the texture-create option block. The recovered element
  layout (offsets relative to the element base) is:

  | Offset | Field | Notes |
  |---|---|---|
  | +0x08 / +0x1C | logical name (key) | small-string layout: length at +0x1C; inline buffer at +0x08 when `len < 0x10`, else a heap pointer at +0x08 |
  | +0x20 | enabled gate | 1 = element may load |
  | +0x21 | loaded flag | 1 = texture is bound |
  | +0x3C | last-use / option stamp field | written by the lazy resolver (see §2.4) |
  | +0x40 | bound GPU texture handle | `IDirect3DTexture9*`, null until loaded |
  | +0x44 | VRAM byte count | subtracted from the global VRAM total on unload |
  | +0x48 | texture-create option-block pointer | drives the D3DX *Ex* option list |

  (Offset table only — the full GHTex layout is in `structs/texture_manager.md`.)

### 2.2 Keying — name string, binary-searched

- The key is the texture's **logical name / virtual path string** (a small-string-optimised string:
  stored inline when short, on the heap when long).
- Lookup is a **binary search over a name-sorted vector** of element pointers, comparing the key
  string byte-for-byte. The element count is derived from the vector's begin/end pointers.
- On a **miss**, the client logs a "cannot find" diagnostic and the consumer falls back to **element
  zero** (slot 0) — effectively a default / placeholder texture rather than a hard failure.

### 2.3 Dedup model — register-then-lookup (build-time dedup)

Dedup is by **pre-registration**, not find-or-create-on-demand. Texture elements are constructed and
inserted into the sorted name vector at **manifest / list load time**. Render and visual code then
resolves a logical name via the binary-search lookup and reuses the existing element — a second
request for the same name returns the **same** element (no reload). In practice most subsystems hold
a direct pointer to their element obtained at their own load time, so the pointer itself is the dedup
handle after the first resolve; the named lookup is used comparatively rarely.

### 2.4 Lazy load + the (vestigial) LRU stamp + live counters

- A per-element **lazy resolver** loads the texture on first bind: if the element is enabled and not
  yet loaded, it invokes the element's load method; on success it returns the bound GPU texture. It
  **always stamps a last-use timestamp** from a global frame/animation clock.
- The actual loader reads the element's name and option block and calls the shared
  VFS-aware texture loader (which, when mounted, slurps the entry and hands the bytes to the D3DX
  "from memory" API; when not mounted uses the loose-file D3DX variant). On success it sets
  loaded/enabled and **increments a live-texture counter**; on failure it logs.
- Unload releases the GPU texture, clears the loaded flag and the bound handle, **decrements the live
  counter**, and **subtracts the element's VRAM byte count** from a global VRAM accounting total.

### 2.5 Eviction — EXPLICIT / bulk-flush, NOT an LRU budget sweep [confirmed]

- The last-use timestamp is **written** by the lazy resolver but **no consumer reads it** — there is
  **no evictor that scans timestamps**. The LRU stamp is **vestigial / diagnostic in this build**.
- Real eviction is two-fold:
  - **Per-element explicit unload**, called by visual/effect teardown.
  - **Bulk flush-all**, which walks a secondary name-keyed container, frees each node's payload, and
    resets the name map. It is invoked at **scene / login transitions and shutdown**.
- The live-texture and VRAM-byte totals are **accounting counters**, each mutated *only* by the load
  (increment / add VRAM) and the unload (decrement / subtract VRAM); no budget threshold drives an
  automatic unload. The "no sweep" conclusion rests on the absence of any *reader* of these two
  globals among their cross-references — load and unload are their only touchers. They appear
  informational/debug only. [static-hypothesis] (no scanning evictor was found; a reader could in
  principle exist on an un-walked path.)

> **Net cache policy: register at load, hold for the scene, bulk-flush on transition.** A
> reimplementation can model this as a name-keyed dictionary populated at manifest load, with an
> explicit `Clear()` at scene/login boundaries and optional per-item disposal — no time-based LRU.

### 2.6 The terrain texture pool is a SEPARATE, index-keyed cache

Runtime terrain textures do **not** go through the name-keyed cache. They live in a **dedicated
index-keyed pool** built once per global texture set (see §3, chain B). Same texture element type and
same lazy-load + VRAM accounting, but keyed by an **integer index** (the cell's terrain texture
index), not by name. This is why the named cache and the terrain pool must be treated as two distinct
caches.

---

## 3. Linkage chains (the asset dependency graph)

These are the inter-asset stitches the parser/renderer must follow. Each is expressed as
key → resolved file. Several are corroborated against the project's recovered mappings and the real
VFS layout ([sample-verified]); the terrain chain carries a resolved CONFLICT note on its runtime
index source.

### A. UI scene → atlas
```
UI widget descriptor → tex_id → uitex.txt → data/ui/<name>.dds
```
`uitex.txt` is the single root of UI texture resolution: a 4-digit tex_id maps to a VFS DDS path.

### B. Terrain cell → bgtexture → DDS  (RESOLVED: the runtime index is BINARY `bgtexture.lst`)
```
cell (area, cx, cz)
  → .ted TextureIndexGrid byte  → integer index into the terrain texture POOL
  → pool element[idx]           → GHTex( data/map000/texture/<rel>.dds )
```
- The runtime index table is the **binary `bgtexture.lst`** under `data/map000/texture/`, **NOT** the
  text `bgtexture.txt`. The string `bgtexture.lst` is present in the image; `bgtexture.txt` is
  **absent** from the image — there is no `.txt` mirror referenced at runtime. The loader opens
  `data/map000/texture/` + `bgtexture.lst`. (Resolved vs the earlier "bgtexture.txt" framing — also
  recorded in `formats/pak.md` and `specs/vfs_overview.md`.) [confirmed]
- `bgtexture.lst` layout: a u32 `count`, **rejected if `count == 0` or `count >= 2000`** (the exact
  bound), then `count` **on-disk records of 48 bytes each**; record byte[0] = a kind selector,
  bytes[1..] = a NUL-terminated relative name. Per non-zero record the loader builds a terrain-texture
  element into an **index-keyed pool** and resolves the texture path as
  `data/map000/texture/<rel>.dds`. [sample-verified — the 48-byte stride and the resolved path match
  the real VFS terrain layout.]
- **The 48-byte figure is the DISK record. The in-memory pool element is a SEPARATE, larger
  structure (76 bytes / 0x4C), with the index stored at element +0x38.** The loader allocates and
  reads `48 * count` bytes of disk records, then constructs one 76-byte pool element per non-zero
  record. Do **not** conflate the two strides: parse the file with a 48-byte stride; the 76 is purely
  the runtime object size and never appears on disk. [confirmed]
- **Kind selector: `1` ⇒ static-render-object; any other non-zero value ⇒ non-static (scroll/animated);
  `0` ⇒ slot skipped (no element built).** Binary-confirmed: `kind == 1` selects the static branch;
  other non-zero values select the non-static branch. The earlier "1 = animated; ≥ 2 = static" wording
  was backwards — the binary wins and is corrected here. (`specs/asset_linkages.md §5` has always
  stated the correct polarity.) The `count` is propagated to the terrain layer renderers (the loader
  passes it to all nine tile / mass / overlay layer-renderer inits). [confirmed]
- **Cell index byte is `idx-1` (RESOLVED in favour of `idx-1`).** The earlier raw-vs-`idx-1`
  question is settled: the per-cell `.ted` texture-index byte is **1-based**, and the texture is
  resolved as `per_cell_texture_list[byte - 1]`, with byte value **0 = no-texture sentinel**. The
  loader stores the block-3 byte RAW (no decrement at load); the `- 1` happens downstream at the
  cell-attach / render-resolution step. The decrement is structurally forced: the on-disk block-3
  bytes are 1-based (observed 1..11, never 0) while the per-cell texture list is built **0-based by
  registration order** (one entry per `.map` `TEXTURES{}` line, slot 0 first). This mirrors the
  already-CONFIRMED **BUILDING** path (`BUILDING TEXTURES[tex_id - 1]`) — the same
  `setTextureId` registration machinery, the same `- 1`. Confidence: **HIGH**. *Residual:* the
  literal `- 1` was not pinned to a single instruction, because the runtime draw resolves each
  patch to a texture-node pointer at cell-attach rather than re-subscripting the per-cell list per
  frame; the mapping is structurally certain, but the instruction-exact decrement site is the one
  thin residual (a debugger pass would make it instruction-exact). Use `texlist[byte - 1]`
  (byte 0 = no texture), not `texlist[byte]`. The full block-3 chain is documented in
  `formats/terrain.md` §5.6. (This resolves the raw-vs-`idx-1` question only; the separate
  `bgtexture.txt`-vs-`.lst` runtime-source note above is unrelated and stands.) The global texture
  repository under `data/map000/texture/` serves **all** areas — terrain textures are not
  area-local.

### C. Character → skin → bind → motion  [sample-verified]
```
PC class+sex+age → skin.txt → tex_ids → data/char/tex{res}/{id}.png   (skin texture)
                 → skin_class
                 → data/char/skin/g{gid}.skn                          (skin mesh, gid-keyed cache)
                 → data/char/bind/g{IdB}.bnd                          (bind pose, by IdB)
                 → actormotion.txt (col2=skin_class, col16+=motion)
                 → data/char/mot/g{skin_class}{motion}.mot            (animation clip)
```
- Boot priming loads, in order: `bindlist.txt`, `motlist.txt`, `skin.txt`, `actormotion.txt` (plus
  emoticon / userjoint tables) to populate the keyed manager maps below.
- **Skin cache** — keyed by integer **gid** (the `.skn` group id), a find-or-load-on-demand tree map.
  On a miss it formats `data/char/skin/g{gid}.skn`, loads and registers, then re-looks-up (returning
  the cached element thereafter). Loading a skin performs the **bind-pose stitch**: it resolves the
  skin's bind pose **by IdB** (weight dedup/normalise math is part of the skin loader). [sample-verified
  — matches the `.skn IdB → bind` chain and the on-disk `data/char/skin/g{gid}.skn` layout.]
- **Bind-pose pool** — a *separate* cache, keyed by bind id, **pre-registered at boot** from
  `bindlist.txt` (path prefix `data/char/bind/`, names of the form `g{IdB}.bnd`).
- **Motion** — a motion-id-keyed tree map, primed from `motlist.txt` (path prefix `data/char/mot/`),
  then pre-warmed (see §3 note on `motion.cache`). `actormotion.txt` feeds an animation catalogue;
  a motion is selected from that catalogue by a per-motion index, with a per-motion texture resolved
  through the named-texture cache. (The exact actormotion column → catalogue-index mapping is owned
  by the animation analyst / `formats/actormotion.md`, not re-derived here.)

> Motion pre-warm side-channel: a `motion.cache` file (a u32 count + that many motion ids) is
> read **as a loose OS file (not via the VFS)** to force-load a recorded set of motions at startup; a
> companion writer records which motion ids were touched so the next launch pre-warms them. This is a
> launch-to-launch warm cache, distinct from the in-memory caches above. [static-hypothesis — the
> `motion.cache` literal is present in the image; the loader body was not re-walked this lane.]

### D. Mob → skin (same chain via actormotion)  [sample-verified]
```
mob_id → actormotion.txt (col1=mob_id → col2=skin_class) → same skin/bind/motion chain as C
```

### E. Sound event → OGG
```
player cell (area, cx, cz)
  → data/mapNNN/soundtableNNN.{bgm|bge|eff|run|wlk}  (grid index → sound_id)
  → data/sound/2d/{sound_id}.ogg  OR  data/sound/3d/{sound_id}.ogg
```
Table type selects the semantic (BGM = music, BGE = ambient, EFF = effect, RUN = run footstep,
WLK = walk footstep). Sound buffers are **not cached** — a fresh sound object is built per play; the
five sound tables are fixed-size binary arrays reloaded per area into static buffers.

### F. Effect id → .xeff → texture
```
effect_slot_index → xeffect.txt (line N → name.xeff)
  → data/effect/xeff/{name}.xeff  (particle descriptor, contains a texture id)
  → bmplist.txt (texId → tga stem)
  → data/effect/tex/{name}.tga
```
The effect manifests (`xeffect.lst`/`.txt`, `bmplist.lst`/`.txt`, `xobj.lst`) are each loaded by exact
path literal; the `.lst` binaries are the runtime form (count + fixed-stride records), the `.txt`
forms are mirrors.

### G. Caption id → display string  [sample-verified]
```
caption_id (u32) → data/script/msg.xdb (516-byte records: u32 id + CP949 string) → displayed string
```
All UI strings, item names, quest text, error messages and NPC dialog flow through this catalogue
(model: `MsgXdbCatalog`). The loader reads fixed **516-byte (0x204)** records, derives the record
count as `file_size / 516`, and inserts the first u32 of each record as the map key. Owned by
`formats/msg_xdb.md`. **Loaded on the WinMain state-machine path — NOT inside the bulk boot thread**
(see §4): the caption catalogue is primed directly during scene-machine startup, separately from the
~47-table corpus thread. [confirmed]

### H. Item equip → effect attachment
```
item in slot S → itemjointeff.txt (slot_id → effect_code) → xeffect.txt (effect_code → xeff) → xeff
```

---

## 4. The bulk asset loader (boot pre-warm thread) [confirmed]

### 4.0 There are TWO loading-handler classes — only one spawns the thread

The client has **two distinct loading-screen handler classes**, both of which enable progress
tracking; they must not be conflated:

| Handler | Spawns the bulk corpus thread? | Role |
|---|---|---|
| **Full load handler** | **Yes** | The heavy boot loading screen (game state 2): its constructor sets the running flag, spawns the corpus thread, and enables progress tracking. This is the one §4.1–§4.3 describe. |
| **Simple load handler** | **No** | A lighter loading handler with **no thread spawn** — it enables progress tracking but loads nothing on a background thread. Used for lighter transition screens. |

Earlier text spoke of "the loading-screen handler" as one object; in fact only the **full** handler
owns the corpus thread. [confirmed]

### 4.1 Thread identity, spawn, and gate

- A **dedicated single thread** pre-loads the global data tables and warms the subsystems. It is
  spawned by the **full loading-screen handler's constructor** (game state 2), which also flips a
  per-handler **"running" flag to 1** just before spawning and enabling progress tracking.
- The thread, at the very end, **sleeps ~500 ms** (a grace period), then **clears the running flag to
  0** and exits.
- **Gameplay blocks on this flag.** The loading-screen per-frame render loop polls the running flag;
  when it clears, the render loop signals the engine main loop to terminate the load state, after
  which the state machine advances out of the loading screen (toward the opening/select window). So
  **the boot blocks on the loading screen until the bulk thread finishes** — no gameplay state
  proceeds while the bulk thread runs. The render loop reads the normalised progress through a
  dedicated getter (the *only* consumer of that value); the actual gate is the running flag, **not**
  the progress value.

### 4.2 Concurrency — sequential, single thread

The thread body is **straight-line sequential** (the only branch is an optional input-method texture
step). It calls **~47 loaders one after another in a fixed compiled order**, then a handful of
subsystem inits, then sleeps, clears the flag, exits. There is **no fan-out, no worker pool, no
parallelism inside the thread** — it is purely sequential preloading, running concurrently only with
the loading-screen render loop (which just draws the bar and waits). All reads bottom out at the same
VFS open path; there is no special bulk I/O channel.

> Note: the caption catalogue `msg.xdb` (chain G) is **not** in this thread's table run — it is
> loaded on the WinMain state-machine path, separately from the corpus thread.

### 4.3 What it preloads (families, in rough compiled order)

1. **Script `.scr` data tables (the bulk).** A long run of fixed-record-stride table loaders: events,
   system control, map setting, playtime reward, items, skills, skill category, users, products
   (+ collect / random-name), helps, NPC/NPCs, mobs, repair, upgrade-items, quests, chivalry,
   letters, nick-to-fame, guild-crest, descript, tip-help, set-item-name, object list, cash items,
   tutor, war-stone info, statue, skill-need-set, VIP levels, item-scale, item-effect.
2. **Per-class record tables.** Class stance scripts, items-extra, emoticon, text-command.
3. **Char `.txt` lists.** Skin list, same-emoticon, and the char list-text family over the animation
   catalogue singleton.
4. **Skill-icon table.**
5. **Guild-crest icon pool** (a small fixed-size icon pool).
6. **`.xdb` tables.** Effect-scale, creature-item, vehicle, buff-icon-position.
7. **Subsystem inits (interleaved at the tail).** Input-method texture bind, shadow-manager init,
   effect-boot asset init (on the named-texture/effect manager), terrain-world singleton touch, and a
   few one-shot engine warm-ups.

### 4.4 Progress meter — fixed compiled denominator + the exact arithmetic [confirmed]

- A process-global **progress denominator is a compiled-in constant — exactly `9,395,240` bytes**
  (stored as a 64-bit value with the high dword = 0). It is **NOT** computed at runtime from a sum of
  entry sizes or an entry count — it is a fixed expected total baked into the binary.
- During the load, every **tracked** VFS read accumulates the bytes read into a **cumulative 64-bit
  byte total**, and the accumulator recomputes a **normalised value** on each read.
- **The exact arithmetic (this is the load-bearing correction):**
  ```
  cumulativeBytes += bytesRead                          // 64-bit cumulative tracked-read total
  value            = cumulativeBytes / 9,395,240        // INTEGER division
  barPx            = clamp( 223 * value / 100 , 223 )   // loading-bar pixel width, max 223 px
  ```
  Because both steps are integer, the `value` produced for the boot corpus (~9 MB of tracked reads)
  is only about **1**, which yields `barPx = 223*1/100 = 2` — i.e. the bar is **effectively
  near-static / near-empty for the whole boot read-set**. The bar fills meaningfully only when
  `value` approaches 100, which would require on the order of **~939 MB** of tracked reads — far
  beyond the boot set. **The earlier framing — "normalised progress is a 0..100 percentage" and
  "~9.4 MB of tracked reads drives the bar toward 100" — is WRONG** under this arithmetic.
- **Completion is driven by the worker done-flag, not by the bar.** The loading screen ends when the
  boot thread clears its running flag (§4.1), *regardless* of the bar pixel width. The progress value
  is read by exactly one consumer (the loading-screen render, via the getter) and is best understood
  as a cosmetic/near-static indicator in this build, not the gate.
- Tracking is **enabled** when a loading-screen handler is constructed (both the full and simple
  handlers enable it) and **disabled** when it is destroyed. Enabling **resets the normalised value
  to 0** but does **not** reset the cumulative byte total.
- The *reason* for that exact denominator (presumably the original packer's measured total
  tracked-read bytes for some reference build) is unexplained. A separate, **unreferenced** alternate
  accumulator with a different constant exists in the binary but has no callers — it is dead in this
  build and not part of the live path. [static-hypothesis — re-confirmation of the dead accumulator
  was not re-driven this lane.]

> **Residual (debugger-pending):** whether the bar ever *visibly* moves during a real boot is a
> runtime outcome the arithmetic alone cannot settle. The arithmetic above is [confirmed] from
> operands; a live session watching the byte total / normalised value during a real boot would
> confirm the on-screen near-static behaviour (pilot only — never `dbg_start`).

> Reimplementation guidance: parallelising the boot loaders is safe **provided the completion gate is
> preserved** (advance only when the boot worker signals done — do **not** key the transition off the
> bar). For a faithful 1:1 loading screen, reproduce the near-static bar (the original genuinely
> barely moves); for a *nicer* bar, replace the fixed denominator with a real total-bytes or
> item-count denominator. Bar geometry for a 1:1 port: max width **223 px**, `barPx = 223*value/100`.

### 4.5 The authoritative boot corpus ORDER lives in `resource_pipeline.md`

§4.3 above lists the boot pre-warm families **in rough compiled order**. The **authoritative,
fully-numbered ~57-step boot-load sequence** — the exact call order of the corpus worker, file by file,
including the interleaved subsystem-init steps and the per-class stance `.do` files in the path-global
table — is owned by `specs/resource_pipeline.md §2.1a` (with the stat-curve family load and the mob
catalogue at steps 10 and 18 detailed in `resource_pipeline.md §2.7 / §2.8`). Treat that list as the
single source of truth for boot order; this spec deepens loader *selection* and the *cache* model, not
the ordering.

### 4.6 RED-HERRING caveats — two seed strings that are NOT the asset manifest / a catalogue parser

So that no future asset-loading lane mis-identifies the boot manifest, two seed strings are explicitly
**not** part of the asset pipeline:

- **(a) The tab-separated header `ID / TYPE / Load Size / Stream / script / script id` is NOT the boot
  corpus manifest.** It is a **column header written by a dead sound-tester debug logger** (a routine
  with no callers that opens a log file and writes that header). It is a sound-loading debug dump
  header — not the resource boot list and not a loader-dispatch table.
- **(b) The `MAXHP: %24s` line (and the companion `SORT / ID / SCR` line) is NOT a stat-table /
  catalogue parser.** It is a **live on-screen DEBUG OVERLAY** that reads from a live actor record at
  runtime (and pulls MAXHP from the mob catalogue record for mob actors); it loads no catalogue file.
  Do not mistake it for an asset/catalogue loader.

(Both are stated in full at `specs/resource_pipeline.md §2.1a` red-herring caveats.)

---

## 5. Cross-references

- `formats/pak.md` — `.inf`/`.vfs` container, open-mode dispatch, three-branch read primitive,
  `vfsmode` toggle, `bgtexture.lst` CONFLICT note. **The progress accumulator (§4.4) lives inside the
  open router described here (§1.3) — see the read-path ↔ progress-meter cross-link.**
- `specs/vfs_overview.md` — directory tree, per-extension census, manifest-linkage table, format gaps.
- `specs/resource_pipeline.md` — boot worker timing, loading-screen rendering, terrain streaming,
  the high-level universal cache pattern, locking model. **The authoritative full ~57-step boot corpus
  ORDER is `§2.1a` (with the stat-curve family at step 10 / §2.7 and the mob catalogue at step 18 /
  §2.8, and the two RED-HERRING caveats — the dead sound-tester header and the live debug overlay —
  recorded there); see §4.5 / §4.6 above.** **The progress-bar arithmetic correction in §4.4
  (value = bytes / 9,395,240 integer; bar = clamp(223·value/100); near-static for the boot set;
  completion via the worker done-flag) is cross-confirmed by this lane.**
- `formats/config_tables.md` — the stat-curve column layouts (`users.scr` / `userlevel.scr` /
  `userpoint.scr` / `exp.scr`) and the position→stat (HP/MP) mapping open questions; the boot
  stat-curve loader is `resource_pipeline.md §2.7`.
- `formats/terrain.md` — terrain cell formats (follow `bgtexture.lst`, see chain B; §5.6 block-3
  `idx-1` chain).
- `formats/actormotion.md`, `formats/animation.md`, `formats/mesh.md` — character chain interiors.
- `formats/msg_xdb.md` — caption catalogue (chain G; 516-byte records).
- `formats/effects.md` — the GHTex name pool is the `bmplist`-derived effect texture-name pool (§2.1);
  effect chain F.
- `formats/sound_tables.md` — sound chain E.
- `structs/texture_manager.md` — the recovered GHTex element offset table (§2.1).
- Canonical names: see `Docs/RE/names.yaml`.
- Provenance: see `Docs/RE/journal.md`.

## 6. Names to flag for `names.yaml` (selection only — not applied here)

Loader/cache concepts worth canonicalising: the named-texture manager / cache (`GHTexManager`,
`GHTex`) and its load/unload pair, the terrain texture pool (`bgtexture.lst` parser), the **two**
loading-handler classes (full vs simple), the bulk asset-loader boot thread, the progress denominator
(`9,395,240`) + the byte-total / normalised-value / tracking-gate globals + the live-texture and
VRAM-byte accounting counters, the open router with its progress hook, the gid-keyed skin cache, the
bind-pose pool, the motion map, the animation catalogue, the `msg.xdb` caption loader, and the asset
chain entry points (UI atlas, terrain bgtexture, character skin/bind/motion, sound table, effect xeff,
caption catalogue). The glossary is orchestrator-owned; these are flagged, not edited.
