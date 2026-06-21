# Format: `.ted.post` (cell_post — post-processed terrain-cell editor twin)

> Clean-room spec. Neutral description only — NO sample bytes, NO decompiler pseudo-code.
> Consumed by `Assets.Parsers` as a **negative** instruction: build NO runtime parser for it.
> If any C# ever touches this family it must cite `// spec: Docs/RE/formats/cell_post.md`.
> Full per-block decode rules live in `terrain.md §5.1–§5.10`; this doc does not duplicate them.

---

## Status

```
verification:   sample-verified + parser-verified  # 46 987-byte five-block layout, the no-wrapper /
                                                    #   no-magic shape, and the exporter copy-then-patch
                                                    #   save protocol all confirmed against the legacy
                                                    #   terrain loader/exporter AND a real VFS pair
                                                    #   (a `.ted.post` byte-identical to its companion `.ted`)
ida_reverified: 2026-06-21
ida_anchor:     263bd994
evidence:       [static-ida, vfs-sample]
conflicts:      none. Corroborates terrain.md §5.10 and authoring_sidecars.md. Two refinements folded
                in vs the earlier inference: (1) the runtime never loads `.ted.post` because no `.map`
                DATAFILE ever names one (the loader is filename-agnostic), NOT because of any
                extension-blocking router logic; (2) "post" = post-PROCESSED, concretely the baked
                per-vertex lighting written into the diffuse block — confirmed from the exporter, no
                longer an inference.
loader_resolved: true              # NO runtime loader binds to this extension; the producer (terrain
                                   #   editor exporter) is the only code path that emits it.
```

---

## 1. Identification

`.ted.post` is the **post-processed twin** of a `<cell>.ted` terrain-geometry cell — the editor's
baked-lighting workspace save. It is a **full, drop-in copy of the companion `.ted` blob**: the
identical fixed five-block layout, the identical total size of **46 987 bytes (0xB78B)**, with **no
header, no magic, no version field, and no wrapper/delta framing**. Offset 0 is the first heightmap
float, exactly as in a bare `.ted`.

The **shipped runtime client never opens `.ted.post`.** The terrain streamer loads whatever path the
cell's `.map` `TERRAIN { DATAFILE … }` token names, and every observed DATAFILE names the bare
`.ted`. So no runtime parser is required: if a preservation/authoring tool must read one, it parses
**byte-for-byte as a `.ted`** (see `terrain.md §5.1–§5.10`).

This is a corroboration of `terrain.md §5.10` and `authoring_sidecars.md`, not a contradiction. It
belongs to the editor/build-time sidecar family alongside `*.sod.pre`, `*.bud.pre`, `*.fx<N>.pre`.

---

## 2. On-disk layout (offset table) — identical for `.ted` and `.ted.post`

NO file header, NO magic, NO version, NO inter-block padding. Five fixed-size blocks read in
sequence. Total = **46 987 bytes (0xB78B)**. Endianness: little-endian (x86).

| Block | Offset | Size (B) | Hex    | Read order | Element type | Element count   | Meaning |
|------:|-------:|---------:|--------|-----------:|--------------|-----------------|---------|
| 1 | 0      | 16 900 | 0x4204 | #1 | f32 LE | 65×65 = 4 225 | Height map (vertex world-Y, raw; no scale) |
| 2 | 16 900 | 12 675 | 0x3183 | #2 | i8 × 3 | 65×65 = 4 225 | Vertex normals (signed bytes) |
| 3 | 29 575 | 256    | 0x100  | #3 | u8     | 16×16 = 256   | Per-patch texture-index grid (RAW, 1-based) |
| 4 | 29 831 | 256    | 0x100  | #4 | u8     | 16×16 = 256   | Per-patch direction / UV-flip flags |
| 5 | 30 087 | 16 900 | 0x4204 | #5 | u8 × 4 (RGBA) | 65×65 = 4 225 | Per-vertex diffuse colour (baked lighting) |

Sum: 16 900 + 12 675 + 256 + 256 + 16 900 = **46 987** (zero remainder).

Block decode rules are NOT duplicated here — see `terrain.md`:
- Block 1 height — `terrain.md §5.3` (f32 LE, row-major `[row*65 + col]`; col = X stride 1, row = Z stride 65).
- Block 2 normals — `terrain.md §5.4` (each signed byte ÷ 127.0; channel order Nx, Ny-up, Nz; flat → (0,1,0)).
- Block 3 texidx — `terrain.md §5.5/§5.6` (RAW 1-based at load; the `idx-1` decrement and `[1,count]` clamp are applied later during patch-texture resolution, not in the loader).
- Block 4 direction — `terrain.md §5.7` (low 2 bits: 0x01 = mirror S/U, 0x02 = mirror T/V; values 0..3).
- Block 5 diffuse — `terrain.md §5.8` (4 bytes per vertex R,G,B,A; loader scales colour ×0.5 on read, the exporter stores ×2 so the round-trip is identity; the +3 alpha byte is alignment pad, always 0, never read).

The per-patch "steep" flag is loader-derived runtime state, NOT an on-disk field (`terrain.md §5.0`).

### Why block 5 sits at offset 30 087

"Post-processing" the editor performs is precisely **baking per-vertex lighting into the diffuse
block (block 5)**. The exporter writes the full `.ted.post`, then re-opens the companion `.ted` and
seeks to **30 087** (the block-5 offset) to patch the recomputed diffuse block in place. So the live
`.ted`'s diffuse block already holds the post-bake result; the `.ted.post` is the standalone archived
copy of that same result.

---

## 3. Read algorithm (raw bytes → runtime grid)

`.ted.post` itself is **never read at runtime**. When a tool reads one, it follows the same path the
runtime uses for a `.ted`:

1. Open the blob (a fixed-layout, raw file — no decompression, no encryption, no checksum).
2. Five sequential fixed-size reads into scratch buffers, in order height → normals → texidx →
   direction → diffuse, using the sizes in §2. A short read on any block is a load failure.
3. Walk the grid as nested patches: outer 16×16 patches (steps of 4 across the 64-quad grid), inner
   5×5 vertices per patch. For each vertex (linear index `row*65 + col`):
   - world X = `(mapX - 10000) * 1024.0 + col * 16.0`
   - world Z = `(mapZ - 10000) * 1024.0 + row * 16.0`
   - Y = height f32 (raw)
   - colour = diffuse bytes × 0.5
   - normal = normal bytes ÷ 127.0
   - per patch: store the texidx byte RAW and the direction byte
   while tracking per-cell min/max Y.
4. Finalize (runtime only, applies to the `.ted` load): resolve patch texture indices against the
   per-cell `.map` `TERRAIN { TEXTURES{…} }` list (the `idx-1` / `[1,count]` clamp), then build the
   cell ground grid and the FX/water layers.

The cell `(mapX, mapZ)` is biased at 10 000; one cell = 1024×1024 world units; cell origin =
`((mapX-10000)*1024, (mapZ-10000)*1024)`. See coordinate conventions in `terrain.md`.

---

## 4. Linkages (join keys; producer; what references it)

```
<cell>.map  ( TERRAIN { DATAFILE <path>  TEXTURES{ flag texId "devpath" … } } )
   │  DATAFILE token names the geometry blob path  ── join key = the literal VFS path
   ▼
<cell>.ted  (the blob the runtime actually loads)        ← terrain geometry loader
   │  block-3 texidx byte (1-based)
   ▼
.map TERRAIN TEXTURES[byte-1].intTexId   (per-cell list, registration order, cap 128)
   ▼
bgtexture.txt / bgtexture.lst [intTexId]  (global pool, loaded once for map000)
   ▼
data/map000/texture/<relPath>.dds

<cell>.ted.post  ── sibling on disk: SAME base path stem + ".post" suffix
   • PRODUCER: the terrain editor exporter (writes the full .ted, then the full .ted.post,
     then re-seeks the .ted to offset 30 087 and patches in the baked diffuse block).
   • join to its .ted = identical base path (strip the trailing ".post").
   • NOT named by any .map DATAFILE → never entered by the runtime loader.
   • lives beside the other authoring sidecars: .sod.pre / .bud.pre / .fx<N>.pre
     (see authoring_sidecars.md).
```

- **`.ted.post` ↔ `.ted` tie.** They are produced by the **same exporter call** for the same cell,
  in the same 5-block format. The exporter writes the standalone `.ted.post` AND patches the same
  diffuse block into the `.ted` at offset 30 087 — so the live `.ted`'s diffuse block *is* the
  post-processed result and the `.ted.post` is the archived copy. Round-trip key: identical base path
  minus the `.post` suffix.
- **Runtime consumer (`.ted` only):** the terrain geometry loader builds the in-memory cell record,
  finalized by the `.map` cell-descriptor load tail (patch-texture resolution + ground grid).
- **Format family:** see `authoring_sidecars.md` for the consolidated "runtime never opens these"
  index covering the `.pre` family and `.post`.

---

## 5. Verification / confidence note

- **Layout (sample-verified, two-witness):** a real `.ted.post` (map016) measured exactly 46 987
  bytes with block boundaries landing at 0 / 16 900 / 29 575 / 29 831 / 30 087, and was
  **byte-for-byte identical** to its companion `.ted` (0 differing bytes over the whole file). All
  `.ted.post` files in the sampled directory were 46 987 bytes each.
- **No runtime read (parser-verified):** the terrain geometry loader is filename-agnostic — it loads
  whatever path the `.map` DATAFILE token supplies, with no `.ted`/`.post` awareness. In the sampled
  directory, 22/22 DATAFILE lines named the bare `.ted` and 0 named a `.ted.post`. The runtime simply
  never receives a `.ted.post` path. (This refines `terrain.md §3.2`: the protection is the *content*
  of the `.map`, not router-level extension logic.)
- **"post" semantics (confirmed from the exporter):** "post" = post-PROCESSED, specifically the
  baked per-vertex lighting written into the diffuse block; the editor save protocol is
  copy-the-full-`.post` then in-place diffuse patch of the `.ted`. This upgrades the earlier
  `terrain.md §5.10` "post-edit / save protocol UNVERIFIED" inference to CONFIRMED.
- **No new on-disk fields** versus the `.ted` spec; all block decode rules match `terrain.md §5.3–§5.8`.

### Open / inherited unknowns
- Whether any `.ted.post` elsewhere in the VFS ever **diverges** from its companion `.ted` (the
  sampled map016 pair was identical). Does not affect the layout verdict.
- Absolute within-cell Z orientation (row 0 = min-Z vs max-Z) — inherited from `.ted`
  (`terrain.md`).
- The diffuse ×2-store / ×0.5-load identity is code-confirmed; the sampled cell is uniform white, so
  the ×2 store is not independently sample-observable (inherited from `terrain.md §5.8`).

---

*Source format: `Docs/RE/_dirty/formats/cell_post.raw.md`. Cross-references: `terrain.md`
(authoritative `.ted` / `.ted.post` block decode, §5.1–§5.10, §3.2, §16), `authoring_sidecars.md`
(sidecar-family index), `bgtexture_lst.md` (texture pool resolution).*
