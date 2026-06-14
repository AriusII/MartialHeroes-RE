---
status: confirmed
sample_verified: partial   # loader-selection mechanism and cache model CODE-CONFIRMED; per-table byte layouts and a few off-by-ones UNVERIFIED
subsystems: [loader_dispatch, ghtex_cache, asset_linkage, bulk_loader]
networked: false           # the asset pipeline is entirely client-side; no wire traffic
encoding_note: Korean in-game text and config strings are CP949 (MS-949 code page), not UTF-8.
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

- **CONFIRMED** — recovered directly from control-flow / global wiring; safe to implement.
- **parser-verified** — additionally cross-checked against a known mapping or a second consumer.
- **UNVERIFIED** — hypothesis / single-source / off-by-one not yet pinned; do not hard-code.

No live sample was mounted while these findings were recovered, so byte *values* are
sample-unverified; the *selection mechanism*, *cache behaviour* and *global wiring* are read directly.

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
   path slurps the whole entry and hands the raw bytes to the D3DX "create texture from file in
   memory" API, which inspects the in-memory header itself to decide DDS-vs-other. OGG audio is
   handed to the Vorbis library (which reads its own `OggS` magic internally). Lua source is handed
   to the Lua loader. The client itself performs no header test in any of these paths. (CONFIRMED)
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

### 1.3 Implication for the reimplementation

`Assets.Vfs` stays format-agnostic (it returns bytes). `Assets.Parsers` selects a parser **at the
call site** — never by sniffing a header. A modern reimplementation may use the file extension as a
selector for convenience, but must understand that the original derives format identity purely from
*which call site* opened the path; there is no in-band format tag to validate against.

---

## 2. The GHTex named-texture cache (CONFIRMED)

This is the **only real asset cache** in the client (the file-level VFS layer has no cache — every
fetch re-reads from disk, per `formats/pak.md`). It caches GPU textures by **logical name**.

### 2.1 Identity and sharing

- A single shared manager singleton holds the cache. **The named-texture manager and the visual
  effect manager are the same singleton object** — effect/billboard manifests and named textures
  share one name-keyed container. Most callers reach it through the effect/visual paths.
- Each cache element is a small object that owns: the logical name (the cache key), an
  "enabled" gate, a "loaded" flag, a last-use timestamp, the bound GPU texture handle (null until
  loaded), a VRAM byte count, and a pointer to the texture-create option block.

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

### 2.5 Eviction — EXPLICIT / bulk-flush, NOT an LRU budget sweep (CONFIRMED)

- The last-use timestamp is **written** by the lazy resolver but **no consumer reads it** — there is
  **no evictor that scans timestamps**. The LRU stamp is **vestigial / diagnostic in this build**.
- Real eviction is two-fold:
  - **Per-element explicit unload**, called by visual/effect teardown.
  - **Bulk flush-all**, which walks a secondary name-keyed container, frees each node's payload, and
    resets the name map. It is invoked at **scene / login transitions and shutdown**.
- The live-texture and VRAM-byte totals are **accounting counters**; no budget threshold drives an
  automatic unload (none found). They appear informational/debug only. (UNVERIFIED whether any budget
  sweep exists; none observed statically.)

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
key → resolved file. Several are corroborated against the project's recovered mappings
(parser-verified); the terrain chain carries a CONFLICT note.

### A. UI scene → atlas
```
UI widget descriptor → tex_id → uitex.txt → data/ui/<name>.dds
```
`uitex.txt` is the single root of UI texture resolution: a 4-digit tex_id maps to a VFS DDS path.

### B. Terrain cell → bgtexture → DDS  (CONFLICT: the runtime index is BINARY `.lst`)
```
cell (area, cx, cz)
  → .ted TextureIndexGrid byte  → integer index into the terrain texture POOL
  → pool element[idx]           → GHTex( data/map000/texture/<rel>.dds )
```
- The runtime index table is the **binary `bgtexture.lst`** under `data/map000/texture/`, **NOT** the
  text `bgtexture.txt`. The `.txt` is an authoring mirror; the loader reads the `.lst`. (CONFLICT vs
  earlier "bgtexture.txt" framing — recorded in `formats/pak.md` and `specs/vfs_overview.md`.)
- `bgtexture.lst` layout: a u32 `count` (rejected if 0 or unreasonably large), then `count` records
  of **48 bytes** each; record byte[0] = a kind selector, bytes[1..] = a NUL-terminated relative
  name. Per record the loader builds a terrain-texture element into an **index-keyed pool** and
  resolves the texture path as `data/map000/texture/<rel>.dds`. (parser-verified — matches the known
  final terrain path.)
- Kind selector: 1 ⇒ animated texture options; ≥ 2 ⇒ static options; 0 ⇒ slot skipped (no element
  built). The `count` is propagated to the terrain layer renderers (tile / mass / overlay layers).
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

### C. Character → skin → bind → motion  (parser-verified)
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
  On a miss it formats `data/char/skin/g{gid}.skn`, loads and registers, then re-looks-up. Loading a
  skin performs the **bind-pose stitch**: it resolves the skin's bind pose **by IdB** (weight
  dedup/normalise math is part of the skin loader). (parser-verified — matches `.skn IdB → bind`.)
- **Bind-pose pool** — a *separate* cache, keyed by bind id, **pre-registered at boot** from
  `bindlist.txt` (path prefix `data/char/bind/`, names of the form `g{IdB}.bnd`).
- **Motion** — a motion-id-keyed tree map, primed from `motlist.txt` (path prefix `data/char/mot/`),
  then pre-warmed (see §3 note on `motion.cache`). `actormotion.txt` feeds an animation catalogue;
  a motion is selected from that catalogue by a per-motion index, with a per-motion texture resolved
  through the named-texture cache. (The exact actormotion column → catalogue-index mapping is owned
  by the animation analyst / `formats/actormotion.md`, not re-derived here.)

> Motion pre-warm side-channel: a `data/motion.cache` file (a u32 count + that many motion ids) is
> read **as a loose OS file (not via the VFS)** to force-load a recorded set of motions at startup; a
> companion writer records which motion ids were touched so the next launch pre-warms them. This is a
> launch-to-launch warm cache, distinct from the in-memory caches above.

### D. Mob → skin (same chain via actormotion)  (parser-verified)
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

### G. Caption id → display string
```
caption_id (u32) → data/script/msg.xdb (516-byte records: u32 id + CP949 string) → displayed string
```
All UI strings, item names, quest text, error messages and NPC dialog flow through this catalogue
(model: `MsgXdbCatalog`). Owned by `formats/msg_xdb.md`.

### H. Item equip → effect attachment
```
item in slot S → itemjointeff.txt (slot_id → effect_code) → xeffect.txt (effect_code → xeff) → xeff
```

---

## 4. The bulk asset loader (boot pre-warm thread) (CONFIRMED)

### 4.1 Thread identity, spawn, and gate

- A **dedicated single thread** pre-loads the global data tables and warms the subsystems. It is
  spawned by the **loading-screen handler's constructor** (game state 2), which also flips a
  per-handler **"running" flag to 1** just before spawning and enabling progress tracking.
- The thread, at the very end, **sleeps ~500 ms** (a grace period), then **clears the running flag to
  0** and exits.
- **Gameplay blocks on this flag.** The loading-screen per-frame render loop polls the running flag;
  when it clears, the render loop signals the engine main loop to terminate the load state, after
  which the state machine advances out of the loading screen (toward the opening/select window). So
  **the boot blocks on the loading screen until the bulk thread finishes** — no gameplay state
  proceeds while the bulk thread runs.

### 4.2 Concurrency — sequential, single thread

The thread body is **straight-line sequential** (the only branch is an optional input-method texture
step). It calls **~50 loaders one after another in a fixed compiled order**, then a handful of
subsystem inits, then sleeps, clears the flag, exits. There is **no fan-out, no worker pool, no
parallelism inside the thread** — it is purely sequential preloading, running concurrently only with
the loading-screen render loop (which just draws the bar and waits). All reads bottom out at the same
VFS open path; there is no special bulk I/O channel.

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

### 4.4 Progress meter — fixed compiled denominator

- A process-global **progress denominator is a compiled-in constant** (≈ 9.4 million bytes). It is
  **NOT** computed at runtime from a sum of entry sizes or an entry count — it is a fixed expected
  total baked into the binary.
- During the load, every **tracked** VFS read accumulates the bytes read into a cumulative total; the
  **normalised progress = accumulated_bytes / denominator**, consumed downstream as a **0..100
  percentage** and drawn as a fixed-width pixel bar.
- Tracking is **enabled** when the loading-screen handler is constructed and **disabled** when it is
  destroyed. Enabling resets the normalised value (but does **not** reset the cumulative byte total).
- A real load reaching ~9.4 MB of tracked reads drives the bar toward 100. (UNVERIFIED that a real
  load lands exactly at 100; the bar clamps regardless.) The *reason* for that exact denominator
  (presumably the original packer's measured total tracked-read bytes for a reference build) is
  UNVERIFIED. A separate, **unreferenced** alternate accumulator with a different constant exists in
  the binary but has no callers — it is dead in this build and not part of the live path.

> Reimplementation guidance: parallelising the boot loaders is safe **provided the completion gate is
> preserved** (do not advance past the loading screen until all preloads finish). The fixed-byte
> progress denominator can be replaced by a real total-bytes or item-count denominator for an
> accurate bar.

---

## 5. Cross-references

- `formats/pak.md` — `.inf`/`.vfs` container, open-mode dispatch, three-branch read primitive,
  `vfsmode` toggle, `bgtexture.lst` CONFLICT note.
- `specs/vfs_overview.md` — directory tree, per-extension census, manifest-linkage table, format gaps.
- `specs/resource_pipeline.md` — boot worker timing, loading-screen rendering, terrain streaming,
  the high-level universal cache pattern, locking model.
- `formats/terrain.md` — terrain cell formats (follow `bgtexture.lst`, see chain B).
- `formats/actormotion.md`, `formats/animation.md`, `formats/mesh.md` — character chain interiors.
- `formats/msg_xdb.md` — caption catalogue (chain G).
- `formats/effects.md`, `formats/sound_tables.md` — effect and sound chains (F, E).
- Canonical names: see `Docs/RE/names.yaml`.
- Provenance: see `Docs/RE/journal.md`.

## 6. Names to flag for `names.yaml` (selection only — not applied here)

Loader/cache concepts worth canonicalising: the named-texture manager / cache (`GHTexManager`,
`GHTex`), the terrain texture pool, the bulk asset-loader boot thread, the progress denominator, the
gid-keyed skin cache, the bind-pose pool, the motion map, the animation catalogue, and the asset
chain entry points (UI atlas, terrain bgtexture, character skin/bind/motion, sound table, effect
xeff, caption catalogue). The glossary is orchestrator-owned; these are flagged, not edited.
