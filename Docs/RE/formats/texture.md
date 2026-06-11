# Format: texture assets  (D3DX9-delegated image formats — no proprietary header)

> Clean-room spec. Neutral description only — NO sample bytes, NO decompiler pseudo-code.
> Consumed by Assets.Parsers. Every offset an engineer cites must reference this file.

## Identification

- **Extensions:** `.dds` (dominant), `.tga` (effect/particle textures), `.bmp` (minor UI assets).
  Exact per-asset-category breakdown requires a sample; the extensions and dominant format are
  confirmed from call-site analysis of the loader routines (see Concrete formats section).
- **Found in:** `.vfs` archive (logical path pattern: `data/item/effect/` for DDS,
  `data/effect/texture/` for TGA; config section label `TEXTURES` names the volume).
- **Magic / signature:** DDS files begin with ASCII `DDS ` (four bytes, `44 44 53 20`). TGA files
  have no fixed magic; D3DX9 auto-detects them from internal structure.
- **Endianness:** DDS headers are little-endian; TGA headers are little-endian. BMP is
  little-endian. All decoding is delegated to D3DX9.

## There is no proprietary texture format

The client contains no custom texture header parser and no custom pixel decoder. All texture
decoding is fully delegated to the Direct3D 9 helper library (D3DX9). The engine extracts raw
bytes from the VFS, then passes them directly to a standard D3DX9 in-memory decode call. D3DX9
auto-detects the on-disk format from the file's own header bytes and handles all pixel decoding
internally.

This means `Assets.Parsers` does not need to implement a proprietary texture format. The parser's
only responsibility for textures is:

1. Locate and extract the raw bytes from the VFS entry (format-transparent; see `formats/pak.md`).
2. Identify the format: check for DDS magic (`DDS ` / `44 44 53 20`) first; fall back to TGA,
   then BMP.
3. Pass the raw bytes to a standard image decoder (DDS-first is the high-confidence path).
4. Produce a decoded pixel buffer for `Assets.Mapping`.

## Loading paths observed in the client

Two code paths reach the same D3DX9 decode API:

**VFS path (primary):** Raw file bytes are loaded from the VFS into a heap allocation, then passed
to the D3DX9 in-memory decode function. The function receives the pointer and the byte count from
the VFS entry; D3DX9 auto-detects the format from the header bytes and returns a Direct3D texture
object. This is the normal runtime path.

**Disk fallback:** When the VFS is not mounted, the engine passes an on-disk file path to a D3DX9
file-from-disk variant. Same format auto-detection applies. This path is used during development
or when the archive is unavailable.

Both paths share the same D3DX9 format auto-detection and produce equivalent output.

## Concrete formats — CONFIRMED from call-site analysis

### DDS / DXT5 — primary format for game-world, item, and effect textures

- **Status: CONFIRMED-from-routine** (was previously inferred from platform/era).
- The dominant texture loader (used at over 200 call sites for UI, item, and effect textures)
  passes an explicit DXT5 format constant as the D3DX9 decode hint. The string `.dds` appears at
  well over 200 locations in the binary, far more than any other image extension.
- **DDS is the dominant on-disk texture format.** DXT5 block compression is the primary variant.
- DXT1 (opaque, no alpha) and DXT3 (explicit alpha) constants also appear in the binary, meaning
  specific asset sub-categories may use those variants. The caller determines the expected format
  per load; D3DX9 still auto-detects from the file header regardless.

| Variant | Status | Notes |
|---|---|---|
| DXT5 | CONFIRMED-from-routine | Primary variant; used at 200+ explicit call sites |
| DXT1 | MEDIUM | String constant found; specific asset categories unknown without sample |
| DXT3 | MEDIUM | String constant found; specific asset categories unknown without sample |
| DDS uncompressed | UNVERIFIED | Not ruled out; possible for special-purpose surfaces |

### TGA — effect and particle textures

- **Status: CONFIRMED-from-routine** (was previously MEDIUM/era-inferred).
- TGA files appear at roughly 60 path locations, concentrated under `data/effect/texture/`. They
  are loaded through the effect texture manager rather than the general item texture loader. The
  loader calls D3DX9 with no explicit format hint (auto-detect), which natively handles TGA.
- No custom TGA header parsing exists in the client; D3DX9 decodes TGA entirely.

### BMP — minor assets (UI)

- **Status: LOW** — the `.bmp` string appears at four locations, all in UI or loader-path
  contexts. Likely used for a small number of UI assets (cursor, splash, loading screen, or
  similar). D3DX9 handles BMP natively. Specific use not fully traced without a sample.

### PNG — not used

- **Status: CONFIRMED-absent** — the string `.png` does not appear in the binary. PNG is not
  used as a texture format in this client.

### JPEG — not used for inbound textures

- **Status: CONFIRMED** — the Intel JPEG library (`ijl11`) is present in the client's import
  table, but only the write and free functions are imported; the read function is not imported.
  JPEG decoding is never called for asset loading. JPEG encoding is used for output paths
  (screenshot export or similar). Inbound textures loaded from the VFS are not JPEG files.

## Enumerations / flags

None applicable — the client passes D3DX9 format and filter parameters that are standard D3D9
enumeration values (`D3DFORMAT`, `D3DPOOL`, filter flags). These are D3D9 API constants, not
game-specific values, and are outside the scope of this spec.

## Known unknowns

- Which specific VFS entry paths use DXT1 vs. DXT3 vs. DXT5: UNVERIFIED — requires a sample
  VFS-extracted texture to confirm the per-category breakdown.
- Whether any D3DX9 color-key transparency value is set to a non-zero default (i.e., whether
  a color is treated as transparent at load time): UNVERIFIED.
- Mip-level generation policy (pre-generated in-file vs. D3DX9-generated at load time): UNVERIFIED.
- Exact VFS path patterns for BMP assets: UNVERIFIED — four string occurrences noted but
  context not fully traced.

## Implementation guidance for Assets.Parsers

- Extract raw bytes via the VFS layer (see `formats/pak.md`).
- Check the first four bytes: if they are `44 44 53 20` (ASCII `DDS `), treat as DDS.
- If not DDS, attempt TGA decode (no magic; rely on decoder heuristics or file extension hint).
- If neither matches, attempt BMP.
- Do NOT attempt JPEG decode for assets loaded from the VFS.
- Report the detected format and surface dimensions to `Assets.Mapping` along with the decoded
  pixel data.
- If an unrecognized format header is encountered, log the first four bytes and report failure
  rather than attempting a blind decode.

## Cross-references

- Related formats: `formats/pak.md` (container that delivers raw texture bytes)
- Canonical names: see `Docs/RE/names.yaml` (`TextureEntry`, `Texture_LoadSimple`,
  `Texture_LoadFile_VFSorDisk`)
- Provenance: see `Docs/RE/journal.md`
