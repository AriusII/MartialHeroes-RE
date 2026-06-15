# Format: texture assets  (D3DX9-delegated image formats — DDS, TGA, BMP, PNG)

> Clean-room spec. Neutral description only — NO sample bytes, NO decompiler pseudo-code.
> Consumed by Assets.Parsers. Every offset an engineer cites must reference this file.

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

The client contains no custom texture header parser and no custom pixel decoder. All four
formats are standard containers. The engine extracts raw bytes from the VFS, then passes them
directly to the standard D3DX9 in-memory decode call
(`D3DXCreateTextureFromFileInMemoryEx` on the VFS path;
`D3DXCreateTextureFromFileExA` on the disk-fallback path). The shared wrapper function routes
both paths to the same D3DX9 format auto-detection.

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

The compiled shader files `.psh` (pixel shader) and `.vsh` (vertex shader) are **not** image
textures and are **not** loaded through the texture-creation call above. They travel a **separate
shader-loading path** with **no cross-reference into the texture loader** — the texture passthrough
described here neither reads nor dispatches on `.psh`/`.vsh`. An engineer must keep the shader
pipeline and the texture pipeline as two independent loaders; the texture loader handles only the
four raster containers (`.dds`/`.png`/`.tga`/`.bmp`). — CONFIRMED.

---

## Format: DDS — primary format for game-world, item, and effect textures

**Overall status: SAMPLE-VERIFIED** (UI DXT1 samples; DXT5 confirmed by call-site analysis only)

### Identification

| Field | Value | Confidence |
|-------|-------|------------|
| Magic (bytes 0–3) | `44 44 53 20` (ASCII `DDS `, space included) | SAMPLE-VERIFIED |
| Endianness | Little-endian | SAMPLE-VERIFIED |
| Usage | 200+ explicit call sites across UI, item, and effect texture loaders | CONFIRMED-from-routine |

DXT5 is specified by the dominant loader (200+ call sites for item/effect textures); DXT1 is
confirmed from UI palette-swatch samples. DXT3 constants appear in the binary but no
DXT3 samples have been inspected.

### DDS_HEADER layout (128 bytes total: 4-byte magic + 124-byte DDS_HEADER)

All integer fields are little-endian unsigned 32-bit unless otherwise noted.

| Offset | Size | Type | Field | Notes / observed values | Confidence |
|-------:|-----:|------|-------|-------------------------|------------|
| 0x00 | 4 | ASCII | magic | `44 44 53 20` ("DDS "); invariant | SAMPLE-VERIFIED |
| 0x04 | 4 | u32-LE | dwSize | 124 (0x7C); invariant per MS spec | SAMPLE-VERIFIED |
| 0x08 | 4 | u32-LE | dwFlags | 0x00001007 in UI samples (CAPS\|HEIGHT\|WIDTH\|PIXELFORMAT) | SAMPLE-VERIFIED |
| 0x0C | 4 | u32-LE | dwHeight | Image height in pixels | SAMPLE-VERIFIED |
| 0x10 | 4 | u32-LE | dwWidth | Image width in pixels | SAMPLE-VERIFIED |
| 0x14 | 4 | u32-LE | dwPitchOrLinearSize | 0 when DDSD_LINEARSIZE not set (all UI samples) | SAMPLE-VERIFIED |
| 0x18 | 4 | u32-LE | dwDepth | 0 (not a volume texture in samples) | SAMPLE-VERIFIED |
| 0x1C | 4 | u32-LE | dwMipMapCount | 0 in UI samples (single mip; larger textures unverified) | SAMPLE-VERIFIED |
| 0x20–0x4B | 44 | u32-LE[11] | dwReserved1 | All zeros in samples | SAMPLE-VERIFIED |
| 0x4C | 32 | struct | DDS_PIXELFORMAT | See embedded struct below | SAMPLE-VERIFIED |
| 0x6C | 4 | u32-LE | dwCaps | 0x00001000 (TEXTURE only; no MIPMAP/COMPLEX) in UI samples | SAMPLE-VERIFIED |
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
| +0x04 | 4 | u32-LE | dwFlags | 0x00000004 (DDPF_FOURCC only, in DXT samples) | SAMPLE-VERIFIED |
| +0x08 | 4 | ASCII | dwFourCC | `44 58 54 31` ("DXT1") in UI samples; "DXT3"/"DXT5" expected elsewhere | SAMPLE-VERIFIED (DXT1); UNVERIFIED (DXT3, DXT5) |
| +0x0C | 4 | u32-LE | dwRGBBitCount | 0 (not used for block-compressed formats) | SAMPLE-VERIFIED |
| +0x10 | 4 | u32-LE | dwRBitMask | 0 (not used for block-compressed formats) | SAMPLE-VERIFIED |
| +0x14 | 4 | u32-LE | dwGBitMask | 0 (not used for block-compressed formats) | SAMPLE-VERIFIED |
| +0x18 | 4 | u32-LE | dwBBitMask | 0 (not used for block-compressed formats) | SAMPLE-VERIFIED |
| +0x1C | 4 | u32-LE | dwABitMask | 0 (not used for block-compressed formats) | SAMPLE-VERIFIED |

### DXT block layout and file-size formula

Pixel data begins immediately after the 128-byte header (offset 0x80).

**Block parameters by variant:**

| Variant | Bytes per 4×4 block | Status |
|---------|---------------------|--------|
| DXT1 | 8 | SAMPLE-VERIFIED |
| DXT3 | 16 | UNVERIFIED (standard value; not sampled) |
| DXT5 | 16 | UNVERIFIED from samples (confirmed via call-site analysis) |

**File-size formula (SAMPLE-VERIFIED for DXT1):**

```
total_bytes = 128 + ceil(width / 4) * ceil(height / 4) * bytes_per_block
```

All three DXT1 UI samples satisfy this formula exactly.

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
| DXT5 | CONFIRMED-from-routine | Primary variant per 200+ loader call sites (item/effect textures); no DXT5 sample inspected |
| DXT1 | SAMPLE-VERIFIED | Used in `data/ui/` palette/color-swatch textures; punch-through alpha active |
| DXT3 | MEDIUM | FourCC string constant present in binary; specific use categories unknown |
| DDS uncompressed | UNVERIFIED | Not ruled out; possible for special-purpose surfaces |

---

## Format: TGA — effect and particle textures

**Overall status: SAMPLE-VERIFIED** (UI sub-directory samples; `data/effect/texture/` not yet sampled)

### Identification

TGA has no fixed magic bytes. The first byte is `idLength` (a variable). Identification relies
on file extension or D3DX9 auto-detection heuristics.

| Field | Value | Confidence |
|-------|-------|------------|
| Magic | None (no fixed magic) | SAMPLE-VERIFIED |
| Endianness | Little-endian (all multi-byte header fields) | SAMPLE-VERIFIED |
| imageType observed | 2 (uncompressed true-color) | SAMPLE-VERIFIED |
| TGA version | 2.0 (TRUEVISION-XFILE footer present) | SAMPLE-VERIFIED |

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

## Format: BMP — terrain lightmap tiles and toon-shading ramp LUT

**Overall status: SAMPLE-VERIFIED** (terrain tiles + toonramp; bigmap tiles not sampled)

BMP is not a minor format in this client. It serves two distinct and important roles:
(1) terrain/effect lightmap tiles tiled across the world map; (2) a 1D cel-shading lookup
table (LUT) bound as a texture sampler in the toon-shading pipeline.

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
  value 49152; both are valid.
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

---

## Format: PNG — character and item skin textures

**Overall status: SAMPLE-VERIFIED** (tex256256 bucket; other resolution buckets not yet sampled)

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
| 16 | 4 | u32-BE | Image width | 256 (0x00000100) | For tex256256 bucket | SAMPLE-VERIFIED |
| 20 | 4 | u32-BE | Image height | 256 (0x00000100) | For tex256256 bucket | SAMPLE-VERIFIED |
| 24 | 1 | u8 | Bit depth | 8 | 8 bits per channel | SAMPLE-VERIFIED |
| 25 | 1 | u8 | Color type | 2 (RGB truecolor) | No alpha in samples | SAMPLE-VERIFIED |
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
| `data/char/tex256256/` | 256 × 256 | 256 × 256 | SAMPLE-VERIFIED |
| `data/char/tex256512/` | 256 × 512 | 256 × 512 | CONFIRMED-from-routine (no sample) |
| `data/char/tex512512/` | 512 × 512 | 512 × 512 | CONFIRMED-from-routine (no sample) |
| `data/char/tex10241024/` | 1024 × 1024 | 1024 × 1024 | CONFIRMED-from-routine (no sample) |
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

<!-- source: _dirty/campaign5/character-appearance-assembly.md -->

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
- **VFS path:** `data/map000/texture/bgtexture.lst`
- **Magic / signature:** none — file begins immediately with a 4-byte count
- **Endianness:** little-endian

### File-level layout

| Offset | Size | Type | Field | Notes | Confidence |
|-------:|-----:|------|-------|-------|------------|
| `+0x00` | 4 | u32-LE | `count` | Number of texture records. Valid range: 1 ≤ count < 2000. Observed value: 1222. | SAMPLE-VERIFIED |
| `+0x04` | count × 48 | record[] | texture records | Packed immediately after count, no padding between records. | SAMPLE-VERIFIED |

**File-size formula:** `total_bytes = 4 + count × 48`
Verified: `4 + 1222 × 48 = 58,660 bytes` — matches the real-VFS sample file exactly.

### Per-record layout (48 bytes each)

| Offset within record | Size | Type | Field | Notes | Confidence |
|---------------------:|-----:|------|-------|-------|------------|
| `+0x00` | 1 | u8 | `kind` | Texture category / render-class byte. See kind enumeration below. | SAMPLE-VERIFIED |
| `+0x01` | 47 | char[47] | `path_stem` | Null-terminated ASCII relative path stem, without extension. Max observed length: 38 characters. The runtime appends `.dds` to build the full VFS path. | SAMPLE-VERIFIED |

### `kind` byte enumeration

The `kind` byte drives two levels of dispatch. At load time, the loader uses it to select
initialization options for the runtime texture object; separately, the `kind` value is stored
in a parallel array for later use by the render/update loop.

**Load-time dispatch (two-way):**

| `kind` value | Load-time behaviour | Confidence |
|-------------:|---------------------|------------|
| 0 | Record skipped — no texture object is initialized; entry is inactive. No kind=0 records observed in the real sample. | CODE-CONFIRMED (loader guard), SAMPLE: not present |
| 1 | Texture initialized as **animated** (wind-sway capable). | CODE-CONFIRMED |
| ≥ 2 | Texture initialized as **static**. | CODE-CONFIRMED |

**Observed fine-grained `kind` values in the real sample (1222 records):**

| `kind` | Count in sample | Semantic category | Confidence |
|-------:|----------------:|-------------------|------------|
| 1 | 1100 | Animated — general ground cover, grass, and foliage | SAMPLE-VERIFIED |
| 2 | 101 | Static — stone, moss, building surfaces, dense jungle textures | SAMPLE-VERIFIED |
| 10 | 2 | Animated subtype — short grass (specific sway parameters) | SAMPLE-VERIFIED (stored); render dispatch UNVERIFIED |
| 11 | 1 | Animated subtype — herbs / low-canopy foliage | SAMPLE-VERIFIED (stored); render dispatch UNVERIFIED |
| 12 | 1 | Animated subtype — small tree | SAMPLE-VERIFIED (stored); render dispatch UNVERIFIED |
| 20 | 17 | Animated subtype — large trees / heavy foliage (sway amplitude driven by XZ bounding box) | SAMPLE-VERIFIED (stored); render dispatch UNVERIFIED |

Values 10, 11, 12, and 20 are stored in the per-entry kind array for use by the render/update
loop but no render-path branch confirming their finer semantics was recovered from the loader
function itself. Their enumeration and sway-parameter assignments are tentative pending
analysis of the per-frame update functions.

### Path resolution rule

Each record's `path_stem` is a relative sub-path without extension. The runtime constructs the
full VFS path as:

```
full_path = "data/map000/texture/" + path_stem + ".dds"
```

Example: a record with `path_stem = "terrain/a-b-1"` resolves to
`"data/map000/texture/terrain/a-b-1.dds"`.

The prefix `data/map000/texture/` is hardcoded — there is no per-area path substitution.
All terrain and building textures for every map area are stored globally under `map000/texture/`.
This is an intentional shared-pool design, not an oversight.

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
| DDS (DXT1) | SAMPLE-VERIFIED | 3 | UI palette swatches (`data/ui/`) |
| DDS (DXT5) | CONFIRMED-from-routine | 0 | Item, effect, and general textures (200+ call sites) |
| DDS (DXT3) | MEDIUM | 0 | Binary constant present; use unknown |
| TGA (uncompressed 32bpp) | SAMPLE-VERIFIED | 3 | UI palette swatches (`data/ui/`); also `data/effect/texture/` per call-site analysis |
| BMP (terrain tile 128×128) | SAMPLE-VERIFIED | 2 | `data/effect/map/` lightmap tiles |
| BMP (toonramp LUT 256×1) | SAMPLE-VERIFIED | 1 | `data/shader/toonramp.bmp` — cel-shading LUT |
| PNG (char skin 256×256) | SAMPLE-VERIFIED | 3 | `data/char/tex256256/` character skin textures |
| PNG (other resolutions) | CONFIRMED-from-routine | 0 | Higher-resolution char buckets and item textures |
| bgtexture.lst (binary catalogue) | SAMPLE-VERIFIED | 1 file (1222 records) | `data/map000/texture/bgtexture.lst` — global terrain/building texture index |

---

## Enumerations / flags

No game-specific enumerations for the pixel-format assets. The client passes standard D3D9 API
constants (`D3DFORMAT`, `D3DPOOL`, filter flags) as D3DX9 parameters. These are D3D9 SDK values
outside the scope of this spec.

For `bgtexture.lst`-specific enumerations see the `kind` byte table in the section above.

---

## Known unknowns

- **DXT5 samples:** No DXT5 file has been inspected at byte level. DXT5 is confirmed as the
  dominant format from call-site analysis (200+ sites) but the DDS_PIXELFORMAT block, mip
  layout, and per-category path assignment have not been validated against a real asset.
- **DXT3 use:** The DXT3 constant appears in the binary but the specific asset category
  using it is unknown.
- **Per-directory DXT variant breakdown:** Only `data/ui/` has been sampled (DXT1 confirmed).
  The DXT variant used in `data/item/effect/`, `data/char/`, and terrain DDS assets is
  UNVERIFIED from samples.
- **Mip-map presence:** All sampled DDS files are single-mip (DDSD_MIPMAPCOUNT not set).
  Larger world textures likely have pre-generated mipmaps but this has not been verified.
- **D3DX9 color-key transparency:** Whether any non-zero color-key value is configured at
  load time cannot be determined from file bytes alone.
- **PNG alpha in higher-resolution buckets:** The tex256256 samples use color type 2 (RGB,
  no alpha). Whether tex512512 or tex10241024 PNG files use RGBA (color type 6) is UNVERIFIED.
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
- **bgtexture.lst `kind` values 10/11/12/20 render dispatch:** These values are stored in the
  per-entry kind array. The specific sway parameters or render-loop branches they trigger have
  not been recovered from static analysis of the loader; they require analysis of the per-frame
  terrain/building update functions.
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
3. For DDS: parse the standard 128-byte header, read `DDS_PIXELFORMAT.dwFourCC` to determine
   the block compression variant, and compute pixel data length using the file-size formula
   above. Report dimensions, variant, and mip count to `Assets.Mapping`.
4. For TGA: parse the 18-byte header, note `imageDescriptor` bit 5 = 0 (bottom-up row order;
   vertical flip is required for top-down output). `pixelDepth = 32` means BGRA channel order.
5. For BMP: parse the 14-byte BITMAPFILEHEADER and 40-byte BITMAPINFOHEADER. Skip to
   `PixelDataOffset` (54) for pixel data. Apply DWORD-aligned stride. Rows are bottom-to-top
   (positive `Height` field). For toonramp, read 256 pixels as a grayscale LUT; ignore the
   2 trailing null bytes beyond `stride * abs(Height)`.
6. For PNG: decode the standard chunk stream. Concatenate all consecutive IDAT chunk data
   before zlib decompression. After decompression, remove the leading filter byte from each
   scanline before presenting pixel data.
7. For bgtexture.lst: read `count` (u32-LE at offset 0); validate `1 ≤ count < 2000`. Then
   read `count` records of 48 bytes each. Each record: `kind` (u8 at `+0x00`) and `path_stem`
   (null-terminated char[47] at `+0x01`). Construct the full DDS path as
   `"data/map000/texture/" + path_stem + ".dds"`. Expose `kind` as a raw byte; treat kind=0 as
   inactive. Index is 0-based internally; callers reference entries via 1-based `intTexId`
   (subtract 1 to get the array index).
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
