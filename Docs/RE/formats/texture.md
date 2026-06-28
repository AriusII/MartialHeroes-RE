# Format: texture assets  (D3DX9-delegated image formats — DDS, TGA, BMP, PNG)

> Clean-room spec. Neutral description only — NO sample bytes, NO decompiler pseudo-code.
> Consumed by Assets.Parsers. Every offset an engineer cites must reference this file.

> **Verification:** sample-verified (real-VFS samples corroborate every container + bgtexture.lst; DXT5,
> large-texture mip chains, RAW-BGRA DDS, and the `data/effect/texture/` TGA directory promoted from
> hypothesis to sample-verified this pass). Loader-control-flow facts (single auto-detecting create call,
> separate shader-assembler path, separate surface-load path, two mounted-read mechanisms + one ad-hoc
> overlay create site) are `confirmed` from the loader witness.
> CYCLE 14 re-anchor (f61f66a9): 3 facts re-confirmed SAME (D3DXCreateTextureFromFileInMemoryEx 2-caller census; D3DXCreateTextureFromFileExA + non-Ex caller census; surface-load wrapper and screenshot-save caller census).
> ida_reverified: 2026-06-27 · ida_anchor: f61f66a9ae0ec1e946105b2ecff76e8930cb1d1367df64e5688a5266f5ad9963 · evidence: [static-ida, vfs-sample]
> ida_reverified_prev: 2026-06-26 · ida_anchor_prev: 263bd994c927c20a38624cf0ca452eaef365057fa9db1543d8f668c14a6fd8ee
> conflicts: C1 — the "200+ call sites" figure is the fan-in of the single texture **wrapper**
> (217 distinct call sites on this build), NOT 200+ raw D3DX import calls (the D3DX import itself has
> only 2 direct callers). Reworded below; no structural conflicts remain.
> refinements (2026-06-24): (A) second `bgtexture.lst` under `data/effect/texture/` (count 1108, same
> 48-byte record model) added; (B) 24bpp BGR RAW DDS variant (DDPF_RGB only, flags 0x40, no alpha) noted
> alongside the 32bpp BGRA form; (C) DXT3 outside UI confirmed from a terrain sample — known unknown
> closed; (D) per-pool DXT census for `data/map000/texture/` added (1127 DXT1 / 117 DXT3 / 2 DXT2 /
> 4 RAW / 3 anomalous out of 1251 files). No structural corrections; all prior offsets, strides, and
> formulae re-confirmed.
> refinements (2026-06-26): `kind` render-class dispatch by range confirmed (four-way: static-copy / solid-shadow / sway-small-medium / sway-large); material state for all static-object passes confirmed (alpha-blend ON, alpha-test OFF, SRCALPHA/INVSRCALPHA, two-sided for kind==2 and sway; no billboard; no alpha-to-coverage); the "kind==1=texture-animated at load / NOT mesh-swayed at render" two-axis independence confirmed. Known unknown for render dispatch RESOLVED.

## Identification

- **Extensions:** `.dds` (dominant, game-world/item/effect), `.tga` (effect/particle textures),
  `.bmp` (terrain lightmap tiles and toon-shading LUT), `.png` (character and item skin textures).
- **Found in:** `.pak` archive. Directory patterns per format:
  - `.dds` — `data/ui/`, `data/item/effect/` (confirmed by string analysis and samples)
  - `.tga` — `data/effect/texture/`, `data/ui/` (confirmed by string analysis and samples)
  - `.bmp` — `data/effect/map/d%sx%dz%d.bmp`, `data/bigmap/d%sx%dz%d.bmp`,
    `data/shader/toonramp.bmp` (confirmed by string analysis and samples)
  - `.png` — `data/char/tex256256/`, `data/char/tex256512/`, `data/char/tex512512/`,
    `data/char/tex10241024/`, `data/item/texture/` (confirmed by string analysis and samples)
- **Endianness:** DDS — little-endian; TGA — little-endian; BMP — little-endian;
  PNG internal multi-byte fields — big-endian (per PNG spec). All decoding delegated to D3DX9.

---

## There is no proprietary texture format

The client contains no custom texture header parser and no custom pixel decoder, and there is
**no client-side header test or magic compare before the decode call** — the engine hands the raw
buffer straight to D3DX9 and lets D3DX9 recognise the container. All four formats are standard
containers. The engine extracts raw bytes from the VFS, then passes them directly to the standard
D3DX9 in-memory decode call (`D3DXCreateTextureFromFileInMemoryEx` on the VFS path;
`D3DXCreateTextureFromFileExA` on the disk-fallback path). The shared wrapper function routes
both paths to the same D3DX9 format auto-detection.

**Two distinct mounted-read mechanisms reach the same in-memory create call (CONFIRMED).** The
codebase has two texture loaders that both terminate at `D3DXCreateTextureFromFileInMemoryEx`:

- The **central texture wrapper** (the high-fan-in create routine) constructs a file handle *by
  name* — which, when the VFS is mounted, resolves through the VFS chokepoint — then reads the whole
  entry into memory via a slurp accessor that returns `{buffer pointer, length}`, and feeds that to
  the in-memory create call. When the VFS is *not* mounted, the same wrapper instead calls the
  file-from-disk variant `D3DXCreateTextureFromFileExA` with the on-disk path string.
- The **inline UI / icon loader** calls the VFS read-entry chokepoint directly (the same chokepoint
  documented in `formats/pak.md`), obtaining `{buffer, length}`, then calls the in-memory create
  call; on failure it delegates to the central wrapper.

Both mechanisms produce identical results — they differ only in *how* the in-memory buffer is
obtained, not in the decode. A re-implementation may model this as a single "load bytes → decode"
path; the two-mechanism detail is recorded only so the loader fan-in counts below make sense.

**Direct-caller census of the D3DX9 imports (CONFIRMED).** Cross-referencing each underlying D3DX9
import pins exactly how many sites reach it directly:

- `D3DXCreateTextureFromFileInMemoryEx` (the in-memory create) — **2 direct callers**: the central
  texture wrapper and the inline UI/icon loader (matches the "two mechanisms" above).
- `D3DXCreateTextureFromFileExA` (the disk-fallback create) — **2 direct callers**: the central
  texture wrapper's disk fallback and a small ad-hoc FPS-overlay loader.
- `D3DXCreateTextureFromFileInMemory` (the **non-Ex** in-memory create) — **exactly 1 direct caller**:
  the same FPS-overlay loader (the only user of the non-Ex variant anywhere in the client).

So besides the two main mechanisms there is a single third, ad-hoc create site (the FPS/diagnostic
overlay) that loads its own texture directly through the D3DX imports rather than through the central
wrapper. It is functionally identical — raw bytes (VFS read-entry chokepoint when mounted, disk read
otherwise) handed to a D3DX9 in-memory/disk create with header auto-detect — and needs no special
modelling beyond noting it exists.

`Assets.Parsers` responsibilities for textures:

1. Locate and extract the raw bytes from the VFS entry (format-transparent; see `formats/pak.md`).
2. Identify the format from the magic or extension:
   - DDS: first four bytes = `44 44 53 20` (ASCII `DDS `)
   - PNG: first eight bytes = `89 50 4E 47 0D 0A 1A 0A`
   - BMP: first two bytes = `42 4D` (ASCII `BM`)
   - TGA: no fixed magic; identify by extension or decoder heuristic (see TGA section)
3. Pass raw bytes to the appropriate standard decoder.
4. Produce a decoded pixel buffer for `Assets.Mapping`.

---

## Loading paths

Two code paths reach the same D3DX9 decode API:

**VFS path (primary):** Raw file bytes are loaded from the VFS into a heap allocation, then
passed to the D3DX9 in-memory decode function with pointer and byte-count. D3DX9
auto-detects the format and returns a Direct3D texture object.

**Disk fallback:** When the VFS is not mounted, the engine passes an on-disk file path to the
D3DX9 file-from-disk variant. Same format auto-detection applies.

Both paths share the same format auto-detection and produce equivalent output.

---

## Single-call texture passthrough — all image formats share ONE loader (CONFIRMED)

**CAMPAIGN VFS-MASTERY — CONFIRMED (two-witness: loader + black-box).**

The four raster image formats used by the client — **`.dds`, `.png`, `.tga`, and `.bmp`** — are
**all loaded through a single texture-creation call** that auto-detects the container from the
file's own header bytes. There is **no per-extension branch and no extension-keyed dispatch**: the
loader does not inspect the filename extension to choose a decoder, and there is no separate DDS
loader vs. PNG loader vs. TGA loader vs. BMP loader. One call receives the raw in-memory byte
buffer plus its length and returns a decoded texture, regardless of which of the four containers
the bytes actually are.

- The public API symbol for this call is **`D3DXCreateTextureFromFileInMemoryEx`** (the standard
  Direct3D 9 extension-library in-memory texture creator). The format is recognised from the
  buffer's leading magic bytes (`DDS ` / PNG signature / `BM` / TGA heuristic), not from the
  `.dds` / `.png` / `.tga` / `.bmp` extension.
- **Implication for `Assets.Parsers`:** a faithful re-implementation does NOT need four separate
  format dispatchers keyed on extension. It may identify the container from the leading magic
  bytes (as documented per-format below) and route to the matching decoder, but it must accept a
  `.dds`-named file that is really a TGA (and vice-versa) — extension is a hint, header bytes are
  authoritative. This is consistent with the `do.dds` mislabelled-extension caveat documented in
  `formats/ui_manifests.md §7`.

### Shaders are a SEPARATE path — `.psh` / `.vsh` never reach the texture loader (CONFIRMED)

The shader files `.psh` (pixel shader) and `.vsh` (vertex shader) are **not** image
textures and are **not** loaded through the texture-creation call above. They travel a **separate
shader-loading path** with **no cross-reference into the texture loader** — the texture passthrough
described here neither reads nor dispatches on `.psh`/`.vsh`. An engineer must keep the shader
pipeline and the texture pipeline as two independent loaders; the texture loader handles only the
four raster containers (`.dds`/`.png`/`.tga`/`.bmp`). — CONFIRMED.

**Precise shader API — the D3DX9 assembler, not a texture call (CONFIRMED).** The shader path uses
the D3DX9 **assembler** family — `D3DXAssembleShader` / `D3DXAssembleShaderFromFileA` — which means
`.psh`/`.vsh` are shader **assembly source** (text), assembled at load time, *not* pre-compiled
bytecode blobs and *not* anything that reaches `D3DXCreateTextureFromFileInMemoryEx`. The toon-shading
setup routine is a good example of the split: in the same routine it loads `toonramp.bmp` (a LUT
*texture*) **through the central texture wrapper**, but loads `dotoonshading.psh`/`.vsh` (the
*shaders*) through the assembler. Implementors: assemble `.psh`/`.vsh` as source; never route them
through a texture decoder.

### Surfaces are a SEPARATE D3DX9 API from textures (CONFIRMED)

A small number of images are loaded as Direct3D **surfaces** rather than as textures, and these use
the D3DX9 **surface** family — `D3DXLoadSurfaceFromFileInMemory` (VFS / mounted path) and
`D3DXLoadSurfaceFromFileA` (disk fallback) — *not* the texture-creation call. The same
VFS-or-disk passthrough applies (when mounted: VFS read-entry chokepoint → `{buffer, length}` →
in-memory surface load, then free the buffer; otherwise: load the surface from the on-disk path),
and the same container auto-detection by leading magic applies, so the on-disk file is still one of
the standard raster containers. The witnessed input-side surface load is the **sky cloud surface**.
This is the *load* (input) counterpart to the surface-save D3DX9 call the client uses for screenshot
export. A re-implementation should expose surface-target loads through the same "load bytes → decode"
core as textures, differing only in the destination resource type (surface vs. texture).

---

## Format: DDS — primary format for game-world, item, and effect textures

**Overall status: SAMPLE-VERIFIED** (UI DXT1 swatches; DXT5 64² and a DXT1 1024² mip-11 surface
byte-verified this pass)

### Identification

| Field | Value | Confidence |
|-------|-------|------------|
| Magic (bytes 0–3) | `44 44 53 20` (ASCII `DDS `, space included) | SAMPLE-VERIFIED |
| Endianness | Little-endian | SAMPLE-VERIFIED |
| Usage | 200+ call sites funnel through the single texture **wrapper** (217 distinct call sites on this build); the raw D3DX9 import itself has only 2 direct callers | CONFIRMED |

> **Note on the "200+" figure.** Every texture create in the client centralizes through ONE wrapper
> routine; that wrapper has 217 distinct call sites on this build. The underlying D3DX9 import
> (`D3DXCreateTextureFromFileInMemoryEx`) is called from only 2 places (the central wrapper and the
> inline UI/icon loader). So "200+ call sites" means *callers of the wrapper*, not 200 separate D3DX
> import calls — the wrapper centralizes all texture creation. This corrects the earlier "200+ raw
> callers" reading.

DDS is the dominant container. DXT5 is now byte-level sample-verified from an item/effect texture;
DXT1 is sample-verified from both UI palette swatches *and* a large character texture with a full
mip chain. DXT2/DXT3 are sample-verified from the front-end UI atlases. RAW (uncompressed)
DDS is sample-verified from effect and front-end surfaces.

**Per-pool DXT variant census for `data/map000/texture/` (SAMPLE-VERIFIED — 1251 files total):**

| Variant / flags | FOURCC | Count | Notes |
|-----------------|--------|------:|-------|
| DXT1 (BC1) | `DXT1` | 1127 | Dominant; 8 bytes/block |
| DXT3 (BC2 straight) | `DXT3` | 117 | Second-most-common; terrain and building textures confirmed |
| DXT2 (BC2 premult) | `DXT2` | 2 | BC2 premultiplied-alpha; 16 bytes/block |
| RAW uncompressed | (none) | 4 | See RAW variant detail below |
| Anomalous / truncated | — | 3 | Files shorter than a valid header; not parsed |

DXT5 is NOT present in the `data/map000/texture/` pool (0 files). DXT5 is used in item/effect
textures and UI atlases (see the atlas table below). Engineers should not assume DXT5 for terrain.

### DDS_HEADER layout (128 bytes total: 4-byte magic + 124-byte DDS_HEADER)

All integer fields are little-endian unsigned 32-bit unless otherwise noted.

| Offset | Size | Type | Field | Notes / observed values | Confidence |
|-------:|-----:|------|-------|-------------------------|------------|
| 0x00 | 4 | ASCII | magic | `44 44 53 20` ("DDS "); invariant | SAMPLE-VERIFIED |
| 0x04 | 4 | u32-LE | dwSize | 124 (0x7C); invariant per MS spec | SAMPLE-VERIFIED |
| 0x08 | 4 | u32-LE | dwFlags | 0x00001007 in single-mip UI samples (CAPS\|HEIGHT\|WIDTH\|PIXELFORMAT); **0x000A1007** on the large mipped DXT1 sample (adds LINEARSIZE 0x80000 + MIPMAPCOUNT 0x20000) | SAMPLE-VERIFIED |
| 0x0C | 4 | u32-LE | dwHeight | Image height in pixels (1024 on the large sample) | SAMPLE-VERIFIED |
| 0x10 | 4 | u32-LE | dwWidth | Image width in pixels (1024 on the large sample) | SAMPLE-VERIFIED |
| 0x14 | 4 | u32-LE | dwPitchOrLinearSize | 0 when DDSD_LINEARSIZE not set (single-mip UI samples); **524288** (top-mip linear size) on the large DXT1 sample where LINEARSIZE is set | SAMPLE-VERIFIED |
| 0x18 | 4 | u32-LE | dwDepth | 0 (not a volume texture in samples) | SAMPLE-VERIFIED |
| 0x1C | 4 | u32-LE | dwMipMapCount | 0 in single-mip UI samples; **11** (full chain) on the large 1024² DXT1 sample | SAMPLE-VERIFIED |
| 0x20–0x4B | 44 | u32-LE[11] | dwReserved1 | All zeros in samples | SAMPLE-VERIFIED |
| 0x4C | 32 | struct | DDS_PIXELFORMAT | See embedded struct below | SAMPLE-VERIFIED |
| 0x6C | 4 | u32-LE | dwCaps | 0x00001000 (TEXTURE only) in single-mip UI samples; **0x00401008** (TEXTURE\|MIPMAP\|COMPLEX) on the mipped sample; **0x00001002** on the RAW BGRA sample | SAMPLE-VERIFIED |
| 0x70 | 4 | u32-LE | dwCaps2 | 0 (no cubemap, no volume texture) | SAMPLE-VERIFIED |
| 0x74 | 4 | u32-LE | dwCaps3 | 0 (unused) | SAMPLE-VERIFIED |
| 0x78 | 4 | u32-LE | dwCaps4 | 0 (unused) | SAMPLE-VERIFIED |
| 0x7C | 4 | u32-LE | dwReserved2 | 0 (unused) | SAMPLE-VERIFIED |

**dwFlags bitmask (0x00001007 in UI samples):**

| Bit | Name | Set? |
|-----|------|------|
| 0x0001 | DDSD_CAPS | Yes |
| 0x0002 | DDSD_HEIGHT | Yes |
| 0x0004 | DDSD_WIDTH | Yes |
| 0x1000 | DDSD_PIXELFORMAT | Yes |
| 0x0008 | DDSD_PITCH | No — dwPitchOrLinearSize unused |
| 0x0080 | DDSD_LINEARSIZE | No — dwPitchOrLinearSize is 0 |
| 0x20000 | DDSD_MIPMAPCOUNT | No — dwMipMapCount is 0 / ignored in these samples |

### DDS_PIXELFORMAT (embedded at offset 0x4C, 32 bytes)

| Rel offset | Size | Type | Field | Notes / observed values | Confidence |
|-----------:|-----:|------|-------|-------------------------|------------|
| +0x00 | 4 | u32-LE | dwSize | 32; invariant per MS spec | SAMPLE-VERIFIED |
| +0x04 | 4 | u32-LE | dwFlags | 0x00000004 (DDPF_FOURCC) in block-compressed samples; **0x00000041** (DDPF_RGB\|DDPF_ALPHAPIXELS) in RAW 32bpp BGRA samples; **0x00000040** (DDPF_RGB only) in RAW 24bpp BGR samples | SAMPLE-VERIFIED |
| +0x08 | 4 | ASCII | dwFourCC | `44 58 54 31` ("DXT1"), `DXT2`, and `DXT5` all observed across samples; **`00 00 00 00`** (zero, no FourCC) in all RAW uncompressed samples | SAMPLE-VERIFIED (DXT1, DXT2, DXT5, RAW); DXT3 verified from UI atlases and terrain samples |
| +0x0C | 4 | u32-LE | dwRGBBitCount | 0 for block-compressed formats; **32** for RAW BGRA8888 (A8R8G8B8); **24** for RAW BGR (B8G8R8, no alpha) | SAMPLE-VERIFIED |
| +0x10 | 4 | u32-LE | dwRBitMask | 0 for block-compressed; 0x00FF0000 for both RAW variants (R channel) | SAMPLE-VERIFIED |
| +0x14 | 4 | u32-LE | dwGBitMask | 0 for block-compressed; 0x0000FF00 for both RAW variants (G channel) | SAMPLE-VERIFIED |
| +0x18 | 4 | u32-LE | dwBBitMask | 0 for block-compressed; 0x000000FF for both RAW variants (B channel) | SAMPLE-VERIFIED |
| +0x1C | 4 | u32-LE | dwABitMask | 0 for block-compressed; 0xFF000000 for A8R8G8B8; **0x00000000** (no alpha) for the 24bpp BGR variant | SAMPLE-VERIFIED |

### DXT block layout and file-size formula

Pixel data begins immediately after the 128-byte header (offset 0x80).

**Block parameters by variant:**

| Variant | Bytes per 4×4 block | Status |
|---------|---------------------|--------|
| DXT1 | 8 | SAMPLE-VERIFIED |
| DXT2 | 16 | SAMPLE-VERIFIED (BC2; premultiplied alpha — see front-end atlas table) |
| DXT3 | 16 | SAMPLE-VERIFIED from UI atlases (BC2; straight alpha) |
| DXT5 | 16 | SAMPLE-VERIFIED (BC3) — byte-verified from an item/effect texture this pass |

**File-size formula (SAMPLE-VERIFIED — single-mip AND full mip-chain):**

```
single-mip:  total_bytes = 128 + ceil(width / 4) * ceil(height / 4) * bytes_per_block
mip-chain:   total_bytes = 128 + Σ over each mip level of
                            ceil(w_i / 4) * ceil(h_i / 4) * bytes_per_block
```

- All three DXT1 UI swatches satisfy the single-mip formula exactly.
- The DXT5 64×64 item/effect sample satisfies the single-mip formula exactly:
  `128 + 16·16·16 = 4224` bytes.
- The DXT1 1024×1024 / 11-mip character sample satisfies the mip-chain formula exactly:
  the 11-level block-byte sum + 128 = the on-disk file size.

**DXT1 block internal structure (8 bytes per 4×4 texel block):**

| Rel offset | Size | Type | Field | Notes |
|-----------:|-----:|------|-------|-------|
| +0x00 | 2 | u16-LE | color0 | RGB565 endpoint 0 |
| +0x02 | 2 | u16-LE | color1 | RGB565 endpoint 1 |
| +0x04 | 4 | u32-LE | lookup | 2-bit index per texel; 4 rows of 4 texels, LSB first |

Alpha mode selection (standard DXT1 rule): when color0 <= color1, 1-bit punch-through
alpha is active (index 3 = fully transparent). When color0 > color1, four opaque colors
are interpolated and there is no transparency.

UI samples observed: color0 = 0x0000 < color1 = solid color in all three DXT1 files.
Punch-through alpha is therefore active in all observed UI palette swatches.
Corner texels of each palette swatch are transparent; interior texels carry the solid color.

### DDS variant summary

| Variant | Status | Notes |
|---------|--------|-------|
| DXT5 | SAMPLE-VERIFIED | Dominant variant for item/effect textures. Byte-verified from a `data/item/effect/` 64×64 DXT5 surface (FourCC `DXT5`, pf_flags 0x4, LINEARSIZE flag set, file size = single-mip formula exact). |
| DXT1 | SAMPLE-VERIFIED | `data/ui/` palette/color-swatch textures (single-mip, punch-through alpha active) **and** large character textures in `data/char/tex10241024/` (1024² with a full 11-level mip chain). |
| DXT2 | SAMPLE-VERIFIED | Front-end UI atlases (see front-end atlas table below). FourCC `DXT2`; same 16-byte BC2 block as DXT3, but with **premultiplied alpha** semantics. |
| DXT3 | SAMPLE-VERIFIED | Front-end UI atlases (see front-end atlas table below) and HUD chrome. FourCC `DXT3`; 16-byte BC2 block with **straight (non-premultiplied) alpha**. |
| DDS uncompressed RAW 32bpp | SAMPLE-VERIFIED | BGRA8888 (A8R8G8B8). Found in front-end surfaces (login base plate, character-window backing) **and** in `data/effect/tex/`. pf_flags = DDPF_RGB\|DDPF_ALPHAPIXELS (0x41), FourCC = `00 00 00 00`, dwRGBBitCount = 32. Requires a BGRA→RGBA swap on import. |
| DDS uncompressed RAW 24bpp | SAMPLE-VERIFIED | BGR (B8G8R8, no alpha). Found in `data/map000/texture/building/`. pf_flags = DDPF_RGB only (0x40), FourCC = `00 00 00 00`, dwRGBBitCount = 24, dwABitMask = 0. Requires a BGR→RGB swap on import; no alpha channel. |

### DXT2 vs DXT3 — both are BC2, alpha convention differs (CONFIRMED)

**CAMPAIGN VFS-MASTERY — CONFIRMED (two-witness: loader + black-box over all `data/ui/` entries).**

The FourCC values `DXT2` and `DXT3` denote the **same block-compressed layout** — Direct3D BC2:
a 16-byte block holding a 64-bit explicit 4-bit-per-texel alpha section followed by a DXT1-style
64-bit colour section (no punch-through; the colour endpoints are always treated as the four-colour
opaque case). The **only** difference is the alpha convention the bytes carry:

- **DXT2** — alpha is **premultiplied** into the colour channels (colour already scaled by alpha).
- **DXT3** — alpha is **straight** (non-premultiplied); colour and alpha are independent.

Both decode through the same in-memory texture-creation call (header auto-detect); a re-implementation
that decodes BC2 may treat DXT2 and DXT3 with one BC2 decoder, but must record which convention each
file uses so the compositor un-premultiplies DXT2 surfaces (or blends them with a premultiplied-alpha
blend mode) and blends DXT3 surfaces with straight alpha. Mixing the two conventions produces dark
or haloed edges on the UI atlases.

### Front-end UI atlas container table (SAMPLE-VERIFIED)

**CAMPAIGN VFS-MASTERY — SAMPLE-VERIFIED (black-box over the real `data/ui/` entries).**

The login / character-select / inventory front-end is composited from a small set of named DDS
atlases referenced by **literal path string** in the per-scene build routines (there is no manifest
that lists them - see `formats/ui_manifests.md` for the manifest-driven atlases, which are a
*different* set). Each atlas's container format, dimensions, and mip presence are tabled below so
the importer applies the correct decoder and does not expect mips where none exist.

| Atlas (logical name under `data/ui/`) | Container | Dimensions | Mips | Alpha convention | Confidence |
|---|---|---|---|---|---|
| `login_slice1` | DXT2 (BC2) | 1024x1024 | none | premultiplied | SAMPLE-VERIFIED |
| `loginwindow` | DXT5 (BC3) | 1024x1024 | none | straight (interpolated alpha) | SAMPLE-VERIFIED |
| `loginwindow_02` | DXT2 (BC2) | 1024x1024 | none | premultiplied | SAMPLE-VERIFIED |
| `InventWindow` | DXT3 (BC2) | 1024x1024 | none | straight | SAMPLE-VERIFIED |
| `password` (PIN modal art) | DXT3 (BC2) | 1024x1024 | **11 (full chain)** | straight | SAMPLE-VERIFIED |
| `blacksheet` | DXT5 (BC3) | 512x512 | **10 (full chain)** | straight | SAMPLE-VERIFIED |
| `loading` (`loading.dds`) | DXT3 (BC2) | 1024x1024 | none | straight | SAMPLE-VERIFIED |
| `loading01`-`loading05`, `loading07` | DXT2 (BC2) | per-file | none | premultiplied | SAMPLE-VERIFIED |
| `loading06`, `loading08` | DXT3 (BC2) | per-file | none | straight | SAMPLE-VERIFIED |
| `loadingbar` | DXT2 (BC2) | 256x256 | none | premultiplied | SAMPLE-VERIFIED |

- **Mips:** Only `password` (11 levels) and `blacksheet` (10 levels) carry a mip chain; **every other
  front-end UI atlas is single-mip**. The importer must NOT request generated mips for these
  single-mip atlases (doing so changes their byte-size assumptions and softens crisp UI edges).
- **RAW (uncompressed) DDS atlases** - a subset of front-end surfaces (e.g. the login base plate and
  the character-window backing among others) are **uncompressed BGRA8888** (`DDS_PIXELFORMAT` flags =
  DDPF_RGB|DDPF_ALPHAPIXELS = 0x41, format A8R8G8B8, FourCC field = `00 00 00 00`). These are stored in
  Windows/D3D9 native **B,G,R,A** byte order and require a **BGRA->RGBA byte swap** on import for engines
  that expect RGBA (consistent with the TGA/BMP BGRA handling already documented in this spec). See the
  BGRA discussion in the TGA section. **RAW DDS is not limited to the front-end:** the effects directory
  carries RAW BGRA8888 too — sample-verified from `data/effect/tex/` (e.g. an attack-font surface, 240×30,
  caps 0x1002, no FourCC). So the importer must handle the RAW-BGRA case wherever DDS is loaded, not only
  for login/char-window atlases.
- The DXT2/DXT3 split above is exactly the BC2 alpha-convention split documented in the section above;
  the unified loader auto-detects which from the FourCC, so a re-implementation needs one BC2 decoder
  plus a per-file premultiplied/straight flag.

---

## Format: TGA — effect and particle textures

**Overall status: SAMPLE-VERIFIED** (UI sub-directory swatches **and** a `data/effect/texture/` sample —
the effect TGA directory is now sample-verified this pass)

### Identification

TGA has no fixed magic bytes. The first byte is `idLength` (a variable). Identification relies
on file extension or D3DX9 auto-detection heuristics.

| Field | Value | Confidence |
|-------|-------|------------|
| Magic | None (no fixed magic) | SAMPLE-VERIFIED |
| Endianness | Little-endian (all multi-byte header fields) | SAMPLE-VERIFIED |
| imageType observed | 2 (uncompressed true-color) | SAMPLE-VERIFIED |
| TGA version | 2.0 (TRUEVISION-XFILE footer present) | SAMPLE-VERIFIED |
| Directories sampled | `data/ui/` (4×4 swatches) **and** `data/effect/texture/` (128×128) — both type-2 / 32bpp / footer | SAMPLE-VERIFIED |

> **`data/effect/texture/` now sample-verified (this pass).** A real sample from that directory is a
> 128×128 32bpp type-2 TGA with the standard 26-byte TRUEVISION-XFILE footer — same header/footer
> convention as the 4×4 UI swatches, confirming TGA dimensions are variable and the convention is
> directory-independent. This promotes the directory that the earlier spec flagged as "not yet sampled."

### TGA header layout (18 bytes)

| Offset | Size | Type | Field | Observed value | Notes | Confidence |
|-------:|-----:|------|-------|----------------|-------|------------|
| 0x00 | 1 | u8 | idLength | 0 | No image-ID field appended | SAMPLE-VERIFIED |
| 0x01 | 1 | u8 | colorMapType | 0 | No color map (true-color image) | SAMPLE-VERIFIED |
| 0x02 | 1 | u8 | imageType | 2 | Type 2 = uncompressed true-color | SAMPLE-VERIFIED |
| 0x03 | 2 | u16-LE | cmFirstIndex | 0 | Color map first index (unused) | SAMPLE-VERIFIED |
| 0x05 | 2 | u16-LE | cmLength | 0 | Color map length (unused) | SAMPLE-VERIFIED |
| 0x07 | 1 | u8 | cmEntrySize | 0 | Color map entry size in bits (unused) | SAMPLE-VERIFIED |
| 0x08 | 2 | u16-LE | xOrigin | 0 | X origin of image | SAMPLE-VERIFIED |
| 0x0A | 2 | u16-LE | yOrigin | 0 | Y origin of image | SAMPLE-VERIFIED |
| 0x0C | 2 | u16-LE | width | 4 (UI samples) | Image width in pixels | SAMPLE-VERIFIED |
| 0x0E | 2 | u16-LE | height | 4 (UI samples) | Image height in pixels | SAMPLE-VERIFIED |
| 0x10 | 1 | u8 | pixelDepth | 32 | Bits per pixel (BGRA) | SAMPLE-VERIFIED |
| 0x11 | 1 | u8 | imageDescriptor | 0x08 | bits 3-0 = 8 alpha bits; bit 5 = 0 (bottom-up row order) | SAMPLE-VERIFIED |

**imageDescriptor = 0x08 field breakdown:**

| Bits | Meaning | Value in samples |
|------|---------|-----------------|
| 3–0 | Number of alpha bits per pixel | 8 (full 8-bit alpha channel) |
| 4 | Pixel order (0 = left-to-right) | 0 |
| 5 | Scan-line order (0 = bottom-up) | 0 — bottom row stored first |

**Scan-line orientation note:** bit 5 = 0 means the first row of pixel data in the file is the
bottom row of the image. Decoders must flip vertically unless configured for TGA bottom-up
convention.

### Pixel data (immediately after 18-byte header, plus idLength bytes)

- Format: uncompressed BGRA, 4 bytes per pixel (byte order: Blue, Green, Red, Alpha)
- Channel order BGRA is Windows D3D9 native ordering (D3DFMT_A8R8G8B8 on disk)
- Row order: bottom-up (row 0 in file = bottom row of image)
- Pixel data size: `width * height * 4`
- **File-size formula (SAMPLE-VERIFIED):**
  `total_bytes = 18 + idLength + (width * height * 4) + 26`
  All three TGA samples satisfy this formula exactly.

**BGRA channel order confirmed from color semantics:**

| Sample filename | File BGRA bytes | Display color interpretation |
|----------------|-----------------|------------------------------|
| p_darkblue.tga | B=0x78, G=0x0E, R=0x00, A=0xFF | R=0, G=14, B=120 — dark blue |
| p_orange.tga | B=0x00, G=0x84, R=0xFF, A=0xFF | R=255, G=132, B=0 — orange |
| p_yellow.tga | B=0x00, G=0xD2, R=0xFF, A=0xFF | R=255, G=210, B=0 — yellow |

### TGA 2.0 footer (last 26 bytes of file)

All three TGA samples carry the standard TGA 2.0 developer footer.

| Offset from EOF−26 | Size | Type | Field | Value | Confidence |
|-------------------:|-----:|------|-------|-------|------------|
| +0x00 | 4 | u32-LE | extAreaOffset | 0 (no extension area) | SAMPLE-VERIFIED |
| +0x04 | 4 | u32-LE | devDirOffset | 0 (no developer directory) | SAMPLE-VERIFIED |
| +0x08 | 16 | ASCII | signature | `TRUEVISION-XFILE` | SAMPLE-VERIFIED |
| +0x18 | 1 | u8 | dot | 0x2E (ASCII `.`) | SAMPLE-VERIFIED |
| +0x19 | 1 | u8 | null | 0x00 (terminator) | SAMPLE-VERIFIED |

Extension and developer directory offsets are zero in all samples; no optional TGA 2.0 extension
area or developer directory is present.

---

## Format: BMP — terrain lightmap tiles, toon-shading ramp LUT, and (some) character textures

**Overall status: SAMPLE-VERIFIED** (terrain tiles + toonramp + a 512×512 character-bucket BMP;
bigmap tiles not sampled)

BMP is not a minor format in this client. It serves three distinct roles:
(1) terrain/effect lightmap tiles tiled across the world map; (2) a 1D cel-shading lookup
table (LUT) bound as a texture sampler in the toon-shading pipeline; (3) some **character texture
bucket** entries are stored as BMP rather than PNG (see "Character buckets are container-mixed" below).

### Identification

| Field | Value | Confidence |
|-------|-------|------------|
| Magic (bytes 0–1) | `42 4D` (ASCII `BM`) | SAMPLE-VERIFIED |
| DIB header type | BITMAPINFOHEADER (40 bytes; DIB v3) | SAMPLE-VERIFIED |
| Endianness | Little-endian | SAMPLE-VERIFIED |

Standard Windows BMP (Device-Independent Bitmap). No proprietary wrapper.

### BITMAPFILEHEADER (14 bytes, offset 0x00)

| Offset | Size | Type | Field | Value | Notes | Confidence |
|-------:|-----:|------|-------|-------|-------|------------|
| 0x00 | 2 | ASCII | Signature | `42 4D` ("BM") | Invariant BMP magic | SAMPLE-VERIFIED |
| 0x02 | 4 | u32-LE | FileSize | matches disk size | Actual byte size of file | SAMPLE-VERIFIED |
| 0x06 | 2 | u16-LE | Reserved1 | 0x0000 | Unused | SAMPLE-VERIFIED |
| 0x08 | 2 | u16-LE | Reserved2 | 0x0000 | Unused | SAMPLE-VERIFIED |
| 0x0A | 4 | u32-LE | PixelDataOffset | 54 (0x36) | 14 + 40; no palette block | SAMPLE-VERIFIED |

### BITMAPINFOHEADER (40 bytes, offset 0x0E)

| Offset | Size | Type | Field | Terrain tiles | Toonramp LUT | Confidence |
|-------:|-----:|------|-------|---------------|--------------|------------|
| 0x0E | 4 | u32-LE | DIBHeaderSize | 40 | 40 | SAMPLE-VERIFIED |
| 0x12 | 4 | i32-LE | Width | 128 | 256 | SAMPLE-VERIFIED |
| 0x16 | 4 | i32-LE | Height | 128 (positive) | 1 (positive) | SAMPLE-VERIFIED |
| 0x1A | 2 | u16-LE | ColorPlanes | 1 | 1 | SAMPLE-VERIFIED |
| 0x1C | 2 | u16-LE | BitDepth | 24 | 24 | SAMPLE-VERIFIED |
| 0x1E | 4 | u32-LE | Compression | 0 (BI_RGB) | 0 (BI_RGB) | SAMPLE-VERIFIED |
| 0x22 | 4 | u32-LE | ImageSize | 0 or 49152 | 770 (see note) | SAMPLE-VERIFIED |
| 0x26 | 4 | i32-LE | HorizPixelsPerMeter | 2834 or 3780 | 2868 | SAMPLE-VERIFIED |
| 0x2A | 4 | i32-LE | VertPixelsPerMeter | 2834 or 3780 | 2868 | SAMPLE-VERIFIED |
| 0x2E | 4 | u32-LE | ColorsUsed | 0 | 0 | SAMPLE-VERIFIED |
| 0x32 | 4 | u32-LE | ColorsImportant | 0 | 0 | SAMPLE-VERIFIED |

Notes on observed values:

- `ImageSize = 0` is legal for BI_RGB compression; the decoder derives the size as
  `stride * abs(Height)`. One terrain tile has ImageSize=0; another has the correct
  value 49152; both are valid. (The 512×512 character-bucket BMP carries ImageSize=786434,
  and the toonramp carries 770 — see those sub-sections; all decode by `stride * abs(Height)`.)
- **FileSize field includes the 2 trailing pad bytes.** Across all three BMP samples the
  BITMAPFILEHEADER `FileSize` field counts the 2 trailing null bytes that the art team's encoder
  appends after the pixel region: terrain tile FileSize=49208 (= 54 + 49152 + 2), toonramp FileSize=824
  (= 54 + 768 + 2), 512² char BMP FileSize=786488 (= 54 + 786432 + 2). Loaders read exactly
  `stride * abs(Height)` from `PixelDataOffset` and ignore the trailing bytes; the `FileSize` field is
  informational only. This is a stable encoder artifact, not specific to toonramp.
- `HorizPixelsPerMeter` and `VertPixelsPerMeter` vary between samples (72 DPI vs 96 DPI),
  reflecting different source tool settings. D3DX9 ignores these fields at runtime.
- `Height` is positive in all samples, which means rows are stored bottom-to-top (standard
  BMP convention: first row in the file is the bottom row of the image).
- The toonramp `ImageSize` field reads 770, but the correct size for a 256×1 24bpp image is
  768 bytes. The file carries 2 extra null bytes at the end of the pixel data region. This is a
  tool artifact from the art team's BMP encoder; standard loaders skip to `PixelDataOffset` (54)
  and read exactly `stride * abs(Height)` bytes, so the trailing bytes are silently ignored.

### Pixel data layout (24 bpp RGB, no palette)

- `PixelDataOffset`: always 54 (= 14 FileHeader + 40 InfoHeader, no palette block)
- Pixel format: RGB 24bpp, no alpha channel
- Byte order within each pixel: Blue, Green, Red (Windows BMP BGR convention)
- Row stride: DWORD-aligned — `stride = ((Width * 24 + 31) / 32) * 4` bytes
  - 128-wide terrain tile: stride = 384 bytes
  - 256-wide toonramp: stride = 768 bytes
- Row order: bottom-to-top (positive `Height` field; first byte of pixel data = bottom row)
- No compression, no RLE, no palette

### Sub-format: terrain and effect lightmap tiles

**VFS path template:** `data/effect/map/d%sx%dz%d.bmp` (and `data/bigmap/d%sx%dz%d.bmp`)

**Dimensions:** 128 × 128 pixels, 24bpp RGB.

**Tile naming convention** (`d%sx%dz%d.bmp`):

| Component | Description | Example |
|-----------|-------------|---------|
| `d` + digits | Map region ID (zero-padded, 3 digits observed) | `d010` = map 10 |
| `x` + digits | Tile X coordinate (5-digit integer; world-space origin = 10000) | `x10045` |
| `z` + digits | Tile Z coordinate (5-digit integer; world-space origin = 10000) | `z10061` |

The coordinate offset of 10000 (world-space 0 = tile index 10000) is consistent with
coordinate encoding observed in other tile-based formats (`.ted`, `.mud`) in this project.

### Sub-format: toon-shading ramp LUT (`data/shader/toonramp.bmp`)

**Dimensions:** 256 × 1 pixels, 24bpp RGB (all channels identical; grayscale content).
**Role:** 1D lookup table (LUT) for the cel/toon shading pipeline.

**Pipeline role:** the vertex shader computes per-vertex luminance (N·L dot product mapped to
[0, 1]) and writes it to a texture coordinate channel. The pixel shader samples
`toonramp.bmp` at U = luminance, retrieving the quantized shade value. This maps continuous
lighting to discrete cel-shaded bands — the standard "ramp texture" technique for toon shading.

**Ramp gradient structure (SAMPLE-VERIFIED):**

| Pixel range | Entry count | Value (R=G=B) | Interpretation |
|-------------|-------------|---------------|----------------|
| 0–56 | 57 entries | 221 (0xDD) | Shadow region: flat dark clamp (not pure black) |
| 57–74 | 18 entries | 221→255 (ramp) | Transition zone: smooth linear ramp |
| 75–255 | 181 entries | 254 or 255 | Highlight plateau: near-full white |

The non-zero shadow floor (0xDD = approximately 87% white) produces a "bright-but-not-harsh"
shadow characteristic typical of anime-style cel shading from this era. Input luminance U=0
maps to a soft shadow tone, not to black.

### Sub-format: character texture bucket BMP (`data/char/tex512512/…bmp`)

**Dimensions sampled:** 512 × 512 pixels, 24bpp RGB, BI_RGB, `PixelDataOffset` = 54.
FileSize field = 786488 (= 54 + 1536 stride × 512 rows + 2 trailing pad bytes); stride 1536.

A real character-bucket sample is a standard 24bpp BMP — i.e. the character texture buckets
(`data/char/tex512512/` etc.) are **not** PNG-exclusive; they can hold BMP too. This is consistent
with the texture-id-registry loading rule documented in the PNG section: the list-file registration
strips the extension and takes whatever the line names, and the central texture create call is
container-agnostic (D3DX9 auto-detects from the leading bytes). See "Character buckets are
container-mixed" in the PNG section for the full implication.

---

## Format: PNG — character and item skin textures

**Overall status: SAMPLE-VERIFIED** (tex256256 bucket **and** a tex10241024 1024² sample this pass;
remaining buckets confirmed from the loader)

### Identification

| Field | Value | Confidence |
|-------|-------|------------|
| Magic (bytes 0–7) | `89 50 4E 47 0D 0A 1A 0A` | SAMPLE-VERIFIED |
| Standard | ISO 15948 / RFC 2083 (standard PNG) | SAMPLE-VERIFIED |
| Endianness of PNG multi-byte fields | Big-endian (per PNG spec) | SAMPLE-VERIFIED |

No proprietary wrapper. Standard PNG with standard chunk structure.

**Correction to prior spec:** The earlier version of this spec stated "PNG — not used" and
assigned status CONFIRMED-absent based on the `.png` extension string not being found in the
binary. Real samples from `data/char/tex256256/` confirm PNG is actively used for character
skin textures. The binary string search may have missed the extension because the path strings
are constructed dynamically (with format templates and list files) rather than appearing as
literal constants.

### PNG IHDR chunk (13 bytes; chunk begins at offset 8)

Chunk wrapper fields are standard (4-byte big-endian length, 4-byte ASCII type, data, 4-byte CRC).

| Abs offset | Size | Type | Field | Observed value | Notes | Confidence |
|-----------:|-----:|------|-------|----------------|-------|------------|
| 0 | 8 | bytes | PNG signature | `89 50 4E 47 0D 0A 1A 0A` | Invariant | SAMPLE-VERIFIED |
| 8 | 4 | u32-BE | IHDR chunk length | 13 (0x0000000D) | Fixed by spec | SAMPLE-VERIFIED |
| 12 | 4 | ASCII | IHDR chunk type | `IHDR` | Fixed by spec | SAMPLE-VERIFIED |
| 16 | 4 | u32-BE | Image width | 256 (tex256256) / 1024 (tex10241024) | matches bucket dimensions | SAMPLE-VERIFIED |
| 20 | 4 | u32-BE | Image height | 256 (tex256256) / 1024 (tex10241024) | matches bucket dimensions | SAMPLE-VERIFIED |
| 24 | 1 | u8 | Bit depth | 8 | 8 bits per channel | SAMPLE-VERIFIED |
| 25 | 1 | u8 | Color type | 2 (RGB truecolor) | No alpha in any sampled bucket (incl. 1024²) | SAMPLE-VERIFIED |
| 26 | 1 | u8 | Compression method | 0 (deflate/inflate) | Fixed by PNG spec | SAMPLE-VERIFIED |
| 27 | 1 | u8 | Filter method | 0 (adaptive) | Fixed by PNG spec | SAMPLE-VERIFIED |
| 28 | 1 | u8 | Interlace method | 0 (non-interlaced) | No interlacing | SAMPLE-VERIFIED |
| 29 | 4 | u32-BE | IHDR CRC | varies | Verified correct in all samples | SAMPLE-VERIFIED |

### Observed chunk sequences

Two distinct chunk sequences were observed across three samples:

**Pattern A** (two 256×256 samples, 30,349 bytes):
`IHDR(13) + pHYs(9) + gAMA(4) + cHRM(32) + IDAT(30211) + IEND(0)`
All compressed pixel data in a single IDAT chunk.

**Pattern B** (one 256×256 sample, 59,834 bytes):
`IHDR(13) + tIME(7) + IDAT(8192)*7 + IDAT(2330) + IEND(0)`
Compressed pixel data split across eight IDAT chunks. All chunk CRCs verified correct.

The two patterns likely reflect different export tools used by the art team. Both are valid
standard PNG. A decoder that concatenates consecutive IDAT chunk data before decompression
handles both patterns correctly.

### Pixel data (after zlib decompression)

- Color type 2 (RGB), 8 bits per channel, 3 bytes per pixel
- Uncompressed scanline: 1 filter byte + `width * 3` data bytes
  - For 256-wide images: 1 + 768 = 769 bytes per row
- Total decompressed data: `(1 + width * 3) * height` bytes
  - For 256×256: 769 × 256 = 196,864 bytes (SAMPLE-VERIFIED against zlib decompress)
- Adaptive filter types used in samples: type 1 (Sub) and type 4 (Paeth)
- No alpha channel in tex256256 samples (color type = 2, not 6)
- Row order: top-to-bottom (standard PNG convention; no flip required)

### Texture resolution buckets and loading

Files are organized in per-resolution buckets. D3DX9 is called with matching explicit
dimensions per bucket.

| VFS directory | Dimensions | D3DX hint (width × height) | Status |
|---------------|------------|---------------------------|--------|
| `data/char/tex256256/` | 256 × 256 | 256 × 256 | SAMPLE-VERIFIED (PNG) |
| `data/char/tex256512/` | 256 × 512 | 256 × 512 | CONFIRMED-from-routine (no sample) |
| `data/char/tex512512/` | 512 × 512 | 512 × 512 | SAMPLE-VERIFIED (a 512² **BMP** sampled — bucket is container-mixed) |
| `data/char/tex10241024/` | 1024 × 1024 | 1024 × 1024 | SAMPLE-VERIFIED (a 1024² PNG **and** a 1024² DXT1 DDS sampled) |
| `data/item/texture/` | unknown | unknown | CONFIRMED-from-routine (no sample) |

Each bucket directory contains a companion `*list.txt` index file. A list-index loader reads
this index and dispatches a per-entry texture load with the bucket's fixed dimensions. The
id-registry model that this index builds — and how a numeric texture id resolves to a file — is
documented in the next section.

### Texture id registry — list files build a numeric-id -> file map (CODE-CONFIRMED)

Character and item textures are **not** resolved by formatting a `%d.png` filename at draw time.
Instead, a small set of plain-text **list files** is read once at boot and each line is registered
into an in-memory map keyed by a **numeric texture id**. The draw path then looks a texture up by
that id; the on-disk filename and extension are whatever the list file names.

**List files and their target directories (CODE-CONFIRMED):**

| List file | Target directory | Dimensions hint | Notes |
|-----------|------------------|-----------------|-------|
| `data/char/tex10241024list.txt` | `data/char/tex10241024/` | 1024 x 1024 | full-resolution char bucket |
| `data/char/tex512512list.txt` | `data/char/tex512512/` | 512 x 512 | |
| `data/char/tex256512list.txt` | `data/char/tex256512/` | 256 x 512 | |
| `data/char/tex256256list.txt` | `data/char/tex256256/` | 256 x 256 | |
| `data/item/texturelist.txt` | `data/item/texture/` | not fixed | item textures; dimensions taken from the file |

**Per-line registration rule (CODE-CONFIRMED):**

Each line of a list file is a bare on-disk filename of the form `<name>.<ext>` (the extension is
free-form -- `.png`, `.dds`, or another image container; it is taken from the line, **not**
hardcoded). The loader:

1. locates the first `.` in the line and strips the extension, keeping `<name>`;
2. parses `<name>` as a base-10 integer (a numeric parse of the leading digits) -- this integer is
   the **texture id**;
3. prepends the bucket's target directory prefix to form the full VFS path;
4. inserts an entry mapping `id -> (deferred texture, full path, dimensions hint)` into the texture
   registry; the underlying image is loaded lazily on first use.

So the **leading digits of the filename are the texture id**; the rest of the name and the
extension are incidental. The 9-digit numeric filenames seen in `data/char/tex256256/` (e.g.
`419000410.png`) are the textual form of these ids -- see the PNG filename convention below.

**Draw-time binding (CODE-CONFIRMED):** when a character or item part needs its texture, the engine
queries the same registry by the numeric id carried on the part (for characters this id is the
`tex_id` recovered through the skin chain -- see below) and binds the returned texture to the render
node. No filename is formatted at this point; the lookup is purely id -> registry entry.

**Character buckets are container-mixed (SAMPLE-VERIFIED).** Because the list-file registration takes
the extension from the line (not hardcoded) and the central create call auto-detects the container
from the leading bytes, a single character texture bucket may hold a mixture of containers. This is
sample-verified: `data/char/tex10241024/` holds both PNG (`*.png`) and DXT1 DDS (`*.dds`) 1024² files,
and `data/char/tex512512/` holds 24bpp BMP (`*.bmp`) as well. A faithful re-implementation must
therefore decode each bucket entry by its actual header bytes (PNG / DDS / BMP), not assume the bucket
is single-container. The id mapped to a registry entry is identical regardless of the on-disk
container.

### The skin chain that populates the per-part texture id (CODE-CONFIRMED)

A character part's `tex_id` reaches the texture registry through the skin catalogue, loaded at boot
from `data/char/skin.txt`:

1. A `.skn` part carries an appearance id (`IdA`; see `formats/mesh.md`).
2. `data/char/skin.txt` is parsed into the character appearance catalogue. Each row contributes,
   among its columns, a **mesh gid** (column 4) and a **texture id** (column 5).
3. The part's resolved catalogue entry yields its `tex_id` (the column-5 value).
4. That `tex_id` is looked up in the texture id registry above to obtain the bound texture.

The catalogue-population details (the full skin-row layout and the catalogue key) live in
`specs/skinning.md`; this spec documents only the texture-registry tail of the chain. The net
result is the chain **`.skn` `IdA` -> `skin.txt` col4/col5 -> `tex_id` -> texture id registry ->
image file**, all resolved by numeric id rather than by a runtime filename template.


### PNG texture filename convention

Observed filenames: `419000410.png`, `420000470.png`, `420002300.png`

Filenames are 9-digit zero-padded numeric IDs with no separators. Tentative field decomposition
based on observed values:

| Digit positions | Likely role | Example values |
|-----------------|-------------|----------------|
| 0–2 (NNN) | Character class or base entity ID | 419, 420 |
| 3–5 (NNN) | Sub-category or LOD level | 000, 002 |
| 6–8 (NNN) | Variation or colorway index | 410, 470, 300 |

This decomposition is **UNVERIFIED** — needs cross-reference against a character or items data
table to confirm. Flag for `Docs/RE/names.yaml`.

---

## Terrain texture catalogue: `bgtexture.lst` (and `bgtexture.txt`)

**Overall status: SAMPLE-VERIFIED** (1222-entry real-VFS sample; byte-exact size formula confirmed)

> **Correction to prior coverage:** An earlier version of this spec noted "bgtexture.lst —
> stride 76 bytes, fields unknown" based on the GHTex runtime struct size (76 bytes / 0x4C),
> which is an in-memory object size, not the on-disk record size. The on-disk record is
> **48 bytes**, confirmed by the file-size formula `4 + 1222 × 48 = 58,660` matching the
> actual sample file exactly. The 76-byte figure referred to the runtime `GHTex` object that
> the loader constructs from each 48-byte disk record — these are two different sizes for two
> different things. All field widths below are disk-record sizes.

### Identification

- **Filename:** `bgtexture.lst` (binary) and `bgtexture.txt` (text mirror — see below)
- **VFS paths (two catalogues):**
  - `data/map000/texture/bgtexture.lst` — primary terrain/building texture catalogue (1222 records)
  - `data/effect/texture/bgtexture.lst` — effect texture catalogue (1108 records); same 48-byte record
    model; path stems resolve under `data/effect/texture/` instead of `data/map000/texture/`
- **Magic / signature:** none — file begins immediately with a 4-byte count
- **Endianness:** little-endian

### File-level layout

| Offset | Size | Type | Field | Notes | Confidence |
|-------:|-----:|------|-------|-------|------------|
| `+0x00` | 4 | u32-LE | `count` | Number of texture records. Valid range: 1 ≤ count < 2000. Observed value: 1222. | SAMPLE-VERIFIED |
| `+0x04` | count × 48 | record[] | texture records | Packed immediately after count, no padding between records. | SAMPLE-VERIFIED |

**File-size formula:** `total_bytes = 4 + count × 48`
Verified:
- `data/map000/texture/bgtexture.lst`: `4 + 1222 × 48 = 58,660 bytes` — matches real-VFS sample exactly.
- `data/effect/texture/bgtexture.lst`: `4 + 1108 × 48 = 53,188 bytes` — matches real-VFS sample exactly.

### Per-record layout (48 bytes each)

| Offset within record | Size | Type | Field | Notes | Confidence |
|---------------------:|-----:|------|-------|-------|------------|
| `+0x00` | 1 | u8 | `kind` | Texture category / render-class byte. See kind enumeration below. | SAMPLE-VERIFIED |
| `+0x01` | 47 | char[47] | `path_stem` | Null-terminated ASCII relative path stem, without extension. Max observed length: 38 characters. The runtime appends `.dds` to build the full VFS path. | SAMPLE-VERIFIED |

### `kind` byte enumeration

The `kind` byte drives **two independent dispatch axes** that must not be conflated:

1. **Load-time factory split** — selects which GHTex texture-object variant to initialise for the record.
2. **Render-class split** — selects the per-frame draw bucket and mesh-sway behaviour for any `.bud` static object that references this texture record.

The two axes are **independent**: kind==1 selects the texture-animated factory variant at load time but falls in the **static-copy** render class (no mesh sway). The "animated / static" labels in the load-time table below name the factory variant, not the render class.

**Load-time dispatch — GHTex texture factory (two-way, CONFIRMED):**

| `kind` value | Load-time behaviour | Confidence |
|-------------:|---------------------|------------|
| 0 | Record skipped — no texture object is initialized; entry is inactive. No kind=0 records observed in the real sample. | CODE-CONFIRMED (loader guard), SAMPLE: not present |
| 1 | Texture initialized via the **texture-animated** GHTex factory variant. | CODE-CONFIRMED |
| ≠ 1 (including ≥ 2) | Texture initialized via the **non-animated** GHTex factory variant. | CODE-CONFIRMED |

Note: "texture-animated" here means the GHTex factory path for kind==1; it describes initialization behaviour only. It does **not** imply mesh wind-sway at render time — kind==1 objects draw as static geometry (see render-class table below).

**Render-class dispatch by `kind` range — CONFIRMED (four-way, draw-site static analysis):**

| `kind` range | Render class | Mesh sway | Culling in colour pass | Count in sample |
|---:|---|---|---|---:|
| 0x01 (1) | Static copy (else bucket) | None | D3DCULL_CW (one-sided) | 1100 |
| 0x02 (2) | Solid/shadow bucket | None | D3DCULL_NONE (two-sided) | 101 |
| 0x0A..0x0E (10–14) | Wind-sway small/medium | Per-vertex amplitude; vertex_count==9 path fully unrolled; sway divisor = 2 raised to (kind−10) | D3DCULL_NONE (two-sided) | 4 (shipped: 2+1+1 for kinds 10/11/12) |
| 0x14..0x18 (20–24) | Wind-sway large | Amplitude = AABB XZ-diagonal × 0.01 × 0.5 clamped to 2.0, then divided by 2 raised to (kind−20) | D3DCULL_NONE (two-sided) | 17 (shipped: kind 20 only) |
| all other values incl. 0x03..0x09, 0x0F..0x13, 0x19..0xFF | Static copy (else bucket) | None | D3DCULL_CW (one-sided) | 0 (range fall-through; unexercised in shipped data) |

The kind==2 "solid/shadow bucket" draws in **both** the opaque colour pass and the projected-shadow pass; no sway deformation. Stone, moss, building surfaces, and dense-foliage textures fall here. Both sway ranges allocate a writable vertex deform scratch buffer; static and solid classes draw the original 32-byte vertex block directly.

Shipped data exercises exactly four kind values: 1, 2, 10, 11, 12, and 20. The true client dispatch rule uses the ranges above; a hard-coded set {10,11,12,20} is correct for shipped data but incomplete as a general rule (the client accepts any kind 10–14 or 20–24 as sway).

**Material and culling state — all static-object draw passes (CONFIRMED):**

All static-object geometry (every kind value, every render class) draws through the same opaque world colour pass with these render states:

- **ZENABLE = 1** (depth test on).
- **ALPHATESTENABLE = 0** (alpha test **off**). There is no ALPHAREF or ALPHAFUNC on this path. Foliage cutout is achieved purely by alpha blending on the texture's own alpha channel, not by alpha testing.
- **ALPHABLENDENABLE = 1** (alpha blend **on**); SRCBLEND = SRCALPHA (5); DESTBLEND = INVSRCALPHA (6). This is standard transparency — **not additive blend**.
- Stage 0: colour = MODULATE2X(texture, diffuse); alpha = SELECTARG1(texture alpha). Stage 1 disabled.
- No alpha-to-coverage. No billboard or camera-facing orientation — wind-sway deforms vertex positions in place (world-space pre-baked geometry); there is no per-object rotation matrix.
- **FVF = 0x112** (XYZ | NORMAL | TEX1, 32-byte stride) confirmed for all static-object passes (consistent with `formats/terrain_scene.md §3.2.2`).
- **Culling**: the static/else bucket draws one-sided (D3DCULL_CW — clockwise faces culled) under the inherited state. The FX-layer sub-draws, the kind==2 bucket, and both sway buckets (0x0A..0x0E and 0x14..0x18) draw **two-sided** (D3DCULL_NONE) in the colour pass. The projected-shadow pass uses D3DCULL_CW (one-sided) for the static and kind==2 draws.

### Path resolution rule

Each record's `path_stem` is a relative sub-path without extension. The runtime constructs the
full VFS path by prepending the catalogue's directory prefix and appending `.dds`:

```
# map000 catalogue (terrain/building textures):
full_path = "data/map000/texture/" + path_stem + ".dds"

# effect catalogue:
full_path = "data/effect/texture/" + path_stem + ".dds"
```

Example: a record with `path_stem = "terrain/a-b-1"` in the map000 catalogue resolves to
`"data/map000/texture/terrain/a-b-1.dds"`.

The `data/map000/texture/` prefix is hardcoded for the terrain catalogue — there is no per-area
path substitution. All terrain and building textures for every map area are stored globally under
`map000/texture/`. This is an intentional shared-pool design, not an oversight.

### `intTexId` — 1-based indexing into this catalogue

The `.map` scene descriptor's `TERRAIN TEXTURES` and `BUILDING TEXTURES` sections each carry
an `intTexId` field (the second integer on each texture line). This value is a **1-based** index
into the bgtexture.lst record array:

- `intTexId = 1` → record index 0 (the first record)
- `intTexId = N` → record index N − 1

A value of 0 or a value greater than `count` is treated as an error. There is no 0-based
indexing interpretation — the smallest legal `intTexId` is 1. This is consistent with the
`tex_id` convention in `.bud` geometry (see `formats/terrain_scene.md §6`).

The first integer on each `TEXTURES` line (the `intFlag`) is read by the parser but its purpose
is not established. It is not the bgtexture.lst record index. See Known unknowns.

### Text-format companion: `bgtexture.txt`

A companion plain-text file exists at `data/map000/texture/bgtexture.txt`. It contains the same
1222 entries, one per line, in tab-separated format:

```
<0-based-index>TAB<kind>TAB<path_stem>
```

The text and binary files were compared across all 1222 entries: every index, kind value, and
path stem matches exactly. The text file is a human-readable export of the binary catalogue.
Only the binary `.lst` is loaded at runtime; no code path loading `bgtexture.txt` was found in
the parsed routines. Implementors should parse `bgtexture.lst`; `bgtexture.txt` is an editorial
aid only.

---

## Format status summary

| Format | Status | Sample count | Confirmed use |
|--------|--------|-------------|---------------|
| DDS (DXT1) | SAMPLE-VERIFIED | 3 UI + 1 large mipped | UI palette swatches (`data/ui/`); large mipped char textures (`data/char/tex10241024/`) |
| DDS (DXT5) | SAMPLE-VERIFIED | 1 | Item/effect/general textures; byte-verified `data/item/effect/` 64² surface (formula exact) |
| DDS (DXT2) | SAMPLE-VERIFIED | (front-end atlases) | Front-end UI atlases (BC2, premultiplied alpha) |
| DDS (DXT3) | SAMPLE-VERIFIED | (front-end atlases) | Front-end UI atlases + HUD chrome (BC2, straight alpha) |
| DDS (RAW BGRA8888, 32bpp) | SAMPLE-VERIFIED | ≥1 | Front-end surfaces **and** `data/effect/tex/` (uncompressed A8R8G8B8, flags 0x41; needs BGRA→RGBA swap) |
| DDS (RAW BGR, 24bpp) | SAMPLE-VERIFIED | ≥2 | `data/map000/texture/building/` (flags 0x40, no alpha; needs BGR→RGB swap) |
| TGA (uncompressed 32bpp) | SAMPLE-VERIFIED | 3 UI + 1 effect | UI palette swatches (`data/ui/`) **and** `data/effect/texture/` (128² sample byte-verified) |
| BMP (terrain tile 128×128) | SAMPLE-VERIFIED | 2 | `data/effect/map/` lightmap tiles |
| BMP (toonramp LUT 256×1) | SAMPLE-VERIFIED | 1 | `data/shader/toonramp.bmp` — cel-shading LUT |
| BMP (char bucket 512×512) | SAMPLE-VERIFIED | 1 | `data/char/tex512512/` — character textures (buckets are container-mixed) |
| PNG (char skin 256×256) | SAMPLE-VERIFIED | 3 | `data/char/tex256256/` character skin textures |
| PNG (char skin 1024×1024) | SAMPLE-VERIFIED | 1 | `data/char/tex10241024/` (1024², color type 2, no alpha) |
| PNG (other resolutions) | CONFIRMED-from-routine | 0 | tex256512 char bucket and item textures |
| bgtexture.lst (map000 catalogue) | SAMPLE-VERIFIED | 1 file (1222 records) | `data/map000/texture/bgtexture.lst` — global terrain/building texture index |
| bgtexture.lst (effect catalogue) | SAMPLE-VERIFIED | 1 file (1108 records) | `data/effect/texture/bgtexture.lst` — effect texture index; same 48-byte record model |

---

## Enumerations / flags

No game-specific enumerations for the pixel-format assets. The client passes standard D3D9 API
constants (`D3DFORMAT`, `D3DPOOL`, filter flags) as D3DX9 parameters. These are D3D9 SDK values
outside the scope of this spec.

For `bgtexture.lst`-specific enumerations see the `kind` byte table in the section above.

---

## Known unknowns

- **DXT5 samples:** RESOLVED this pass — a `data/item/effect/` 64×64 DXT5 surface was byte-verified
  (FourCC `DXT5`, pf_flags 0x4, LINEARSIZE flag set, single-mip file-size formula exact). The DXT5
  DDS_PIXELFORMAT block and single-mip layout are now sample-verified. Per-category path assignment
  beyond `data/item/effect/` is still only partially sampled.
- **DXT3 use outside UI:** RESOLVED — a terrain texture (`data/map000/texture/terrain/_d004_tree_01.dds`)
  is DXT3 (FOURCC `DXT3`, SAMPLE-VERIFIED). DXT3 is used in both the front-end UI atlases and
  terrain/building textures. DXT3 is the second-most-common variant in the `map000/texture/` pool
  (117 of 1251 files).
- **Per-directory DXT variant breakdown:** Sampled directories now include `data/ui/` (DXT1/DXT2/DXT3),
  `data/item/effect/` (DXT5), `data/char/tex10241024/` (DXT1 mipped + PNG), and `data/effect/tex/`
  (RAW BGRA). The exhaustive per-directory variant census across the whole VFS is still not complete.
- **Mip-map presence:** RESOLVED this pass — large character textures DO carry full pre-generated mip
  chains (`data/char/tex10241024/` DXT1 1024² with dwMipMapCount=11, dwCaps TEXTURE|MIPMAP|COMPLEX,
  dwFlags MIPMAPCOUNT|LINEARSIZE set, mip-chain file-size formula exact). UI swatches and the sampled
  DXT5 surface remain single-mip. Which specific categories ship mips is established (large char
  textures yes, small UI no); a full census is not done.
- **D3DX9 color-key transparency:** Whether any non-zero color-key value is configured at
  load time cannot be determined from file bytes alone.
- **PNG alpha in higher-resolution buckets:** The tex256256 samples and the one tex10241024 sample both
  use color type 2 (RGB, no alpha). Whether any tex512512 or tex10241024 PNG uses RGBA (color type 6)
  is still UNVERIFIED — the 1024² sample inspected this pass has no alpha, but the bucket was not
  exhaustively scanned for color type 6.
- **PNG filename encoding:** The 9-digit numeric name decomposition (class/sub/variation)
  needs cross-reference against a character or items data table.
- **Bigmap BMP tiles (`data/bigmap/d%sx%dz%d.bmp`):** Path template confirmed from binary
  string analysis; no bigmap tile has been sampled. Whether these match the 128×128 24bpp
  spec of `data/effect/map/` tiles is UNVERIFIED.
- **Toonramp shader sampler binding register:** The vertex shader writes luminance to oT1.xyz,
  implying texture stage 1; binding register in the pixel shader not confirmed.
- **bgtexture.lst `intFlag` field:** The first integer on each `TERRAIN/BUILDING TEXTURES` line
  is read by the scene-file parser but its purpose has not been established. It is not the
  bgtexture.lst record index.
- **bgtexture.lst `kind` render-class dispatch:** RESOLVED (2026-06-26) — the four-way render-class dispatch by `kind` range is confirmed from draw-site static analysis; see the render-class table and material-state subsection in `§bgtexture.lst — kind byte enumeration`. Sway parameters (amplitude formula, divisor, deform scratch buffer) and culling are confirmed for all four render classes. No open item remains for shipped kind values {1, 2, 10, 11, 12, 20}.
- **bgtexture.lst global pool vs per-area overrides:** The loader fills a single global pool
  with the hardcoded `data/map000/texture/` prefix. Whether any per-area override mechanism
  exists is unknown.

---

## Implementation guidance for Assets.Parsers

1. Extract raw bytes via the VFS layer (see `formats/pak.md`).
2. Identify the format from the first bytes:
   - DDS: bytes 0–3 = `44 44 53 20` (ASCII `DDS `)
   - PNG: bytes 0–7 = `89 50 4E 47 0D 0A 1A 0A`
   - BMP: bytes 0–1 = `42 4D` (ASCII `BM`)
   - TGA: no magic; use file extension. If extension is `.tga`, treat as TGA regardless of
     first-byte value.
   - bgtexture.lst: identified by VFS path `data/map000/texture/bgtexture.lst`; no magic.
3. For DDS: parse the standard 128-byte header, read `DDS_PIXELFORMAT.dwFlags`/`dwFourCC` to
   determine the variant. If `dwFlags` has DDPF_FOURCC (0x4), it is block-compressed (DXT1/DXT2/
   DXT3/DXT5) — use the appropriate block size and the single-mip *or* mip-chain file-size formula
   (read `dwMipMapCount`; large char textures carry full chains). For uncompressed RAW (`dwFourCC` =
   zero), inspect `dwFlags` and `dwRGBBitCount`:
   - flags 0x41 (DDPF_RGB|DDPF_ALPHAPIXELS), `dwRGBBitCount` = 32 → **BGRA8888 (A8R8G8B8)** —
     apply a **BGRA→RGBA byte swap** on import; 4 bytes per pixel.
   - flags 0x40 (DDPF_RGB only), `dwRGBBitCount` = 24 → **BGR (B8G8R8)** —
     apply a **BGR→RGB byte swap** on import; 3 bytes per pixel, no alpha channel.
   Treat DXT2 as premultiplied-alpha BC2 and DXT3 as straight-alpha BC2 (one BC2 decoder + a
   per-file premultiplied flag). Report dimensions, variant, mip count, and alpha convention to
   `Assets.Mapping`.
4. For TGA: parse the 18-byte header, note `imageDescriptor` bit 5 = 0 (bottom-up row order;
   vertical flip is required for top-down output). `pixelDepth = 32` means BGRA channel order.
5. For BMP: parse the 14-byte BITMAPFILEHEADER and 40-byte BITMAPINFOHEADER. Skip to
   `PixelDataOffset` (54) for pixel data. Apply DWORD-aligned stride. Rows are bottom-to-top
   (positive `Height` field). For toonramp, read 256 pixels as a grayscale LUT; ignore the
   2 trailing null bytes beyond `stride * abs(Height)`.
6. For PNG: decode the standard chunk stream. Concatenate all consecutive IDAT chunk data
   before zlib decompression. After decompression, remove the leading filter byte from each
   scanline before presenting pixel data.
   **Do not assume a character texture bucket is single-container.** The list-file registration is
   extension-free, so `data/char/tex*/` (and item) buckets are container-mixed — a registry entry may
   be a PNG, a DDS, or a BMP. Always dispatch on the actual header bytes of each entry, not on the
   directory or a presumed extension.
7. For bgtexture.lst: read `count` (u32-LE at offset 0); validate `1 ≤ count < 2000`. Then
   read `count` records of 48 bytes each. Each record: `kind` (u8 at `+0x00`) and `path_stem`
   (null-terminated char[47] at `+0x01`). Two catalogue instances share the identical record model;
   the path prefix differs by source:
   - `data/map000/texture/bgtexture.lst` → prefix `"data/map000/texture/"` (terrain/building)
   - `data/effect/texture/bgtexture.lst` → prefix `"data/effect/texture/"` (effects)
   Construct the full DDS path as `<prefix> + path_stem + ".dds"`. Expose `kind` as a raw byte;
   treat kind=0 as inactive. Index is 0-based internally; callers reference entries via 1-based
   `intTexId` (subtract 1 to get the array index).
8. Do NOT attempt JPEG decode for assets loaded from the VFS (JPEG import is not used by this
   client; only JPEG export for screenshots is present).
9. Report the detected format, dimensions, and pixel data to `Assets.Mapping`. If an
   unrecognized format header is encountered, log the first eight bytes and report failure
   rather than attempting a blind decode.

---

## Cross-references

- Related formats: `formats/pak.md` (container that delivers raw texture bytes)
- Related formats: `formats/terrain_scene.md` (`.bud` tex_id → BUILDING TEXTURES → bgtexture.lst)
- Related formats: `formats/terrain.md` (`.ted` TextureIndexGrid → TERRAIN TEXTURES → bgtexture.lst)
- Canonical constants: see `Docs/RE/names.yaml` (`DDS_MAGIC`, `TGA_V2_FOOTER_SIGNATURE`,
  `DDS_HEADER_SIZE`, `DXT1_BLOCK_BYTES`, `DXT3_BLOCK_BYTES`, `DXT5_BLOCK_BYTES`)
- Provenance: see `Docs/RE/journal.md`

> **Provenance — CAMPAIGN VFS-MASTERY (two-witness: loader + black-box):** confirmed that
> `.dds`/`.png`/`.tga`/`.bmp` all pass through ONE in-memory texture-creation call
> (`D3DXCreateTextureFromFileInMemoryEx`, header auto-detect, no per-extension branch), and that
> `.psh`/`.vsh` shaders take a SEPARATE loading path with no cross-reference into the texture
> loader. Promoted as neutral prose; no addresses or decompiler output crossed the firewall.

> **Provenance — CAMPAIGN VFS-MASTERY-B (two-witness reconcile: loader + black-box over `data/ui/`):**
> added the front-end UI atlas container table (login_slice1 / loginwindow / loginwindow_02 /
> InventWindow / password / blacksheet / loading* / loadingbar with per-file container, dimensions,
> and mip presence), the DXT2-vs-DXT3 BC2 alpha-convention split (premultiplied vs straight), and the
> RAW-DDS = BGRA8888 (needs BGRA->RGBA swap) note. Promoted as neutral prose; no addresses, no
> decompiler output, and no sample bytes crossed the firewall.

> **Provenance — CAMPAIGN 10 · Block D (two-witness re-verification, ida_anchor 263bd994, static-only):**
> re-confirmed every prior CONFIRMED/SAMPLE-VERIFIED offset, stride, count, and formula against the
> loader witness and real-VFS header-only samples, and PROMOTED several hypotheses to sample-verified:
> DXT5 (byte-verified `data/item/effect/` 64² surface, single-mip formula exact); large-texture full
> mip chains (`data/char/tex10241024/` DXT1 1024² with dwMipMapCount=11, mip-chain formula exact);
> RAW-BGRA8888 DDS also present in `data/effect/tex/` (not only front-end); the `data/effect/texture/`
> TGA directory (128² type-2/32bpp/footer); and a 1024² PNG. Added: the precise shader API is the
> D3DX9 **assembler** (`D3DXAssembleShader` / `D3DXAssembleShaderFromFileA`) — `.psh`/`.vsh` are
> assembly source, not textures; the texture wrapper has two distinct mounted-read mechanisms reaching
> one in-memory create call; character texture buckets are container-mixed (PNG / DDS / BMP via
> extension-free list registration); the BMP `FileSize` field includes the 2 trailing pad bytes in all
> samples. Corrected the "200+ raw callers" wording to "200+ call sites funnel through the single
> texture wrapper" (217 wrapper call sites on this build; the D3DX import itself has 2 direct callers).
> Promoted as neutral prose; no addresses or decompiler output crossed the firewall.

> **Provenance — refinement pass (static re-verification, ida_anchor 263bd994):** re-confirmed the
> loader control flow against the binary witness — every layout offset, stride, count, formula, and
> asset chain unchanged (no structural correction). Folded in three neutral refinements: (1) the
> D3DX9 import direct-caller census (in-memory create = 2 callers, disk-fallback create = 2 callers,
> non-Ex in-memory create = exactly 1 caller — the FPS/diagnostic overlay being a small third ad-hoc
> create site beyond the two main mechanisms); (2) the input-side **surface** load path uses the D3DX9
> surface family (`D3DXLoadSurfaceFromFileInMemory` / `D3DXLoadSurfaceFromFileA`, witnessed by the sky
> cloud surface), distinct from the texture-creation call; (3) reaffirmed the VFS read chokepoint as
> the shared mounted-path source (its TOC stride and lowercased-path binary search remain documented
> in `formats/pak.md`). Promoted as neutral prose; no addresses or decompiler output crossed the
> firewall.

> **Provenance — 2026-06-24 re-verification (static-only, ida_anchor 263bd994c927c20a38624cf0ca452eaef365057fa9db1543d8f668c14a6fd8ee):**
> full re-confirmation; every prior offset, stride, count, formula, and caller census re-verified —
> no structural corrections. Four additive refinements promoted from dirty notes: (A) second
> `bgtexture.lst` at `data/effect/texture/bgtexture.lst` (count 1108, 4 + 1108 × 48 = 53,188 bytes
> byte-exact, identical 48-byte record model); (B) 24bpp BGR RAW DDS variant (DDPF_RGB flags 0x40,
> `dwRGBBitCount` 24, no alpha, BGR→RGB swap required) present in `data/map000/texture/building/`
> alongside the previously documented 32bpp BGRA form; (C) DXT3 outside UI confirmed —
> `data/map000/texture/terrain/_d004_tree_01.dds` is DXT3; known unknown closed; (D) per-pool DXT
> census for `data/map000/texture/` added (1127 DXT1 / 117 DXT3 / 2 DXT2 / 4 RAW / 3 anomalous of
> 1251 total); DXT5 absent from this pool. Promoted as neutral prose; no addresses or decompiler
> output crossed the firewall.
