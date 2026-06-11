# Format: texture assets  (D3DX9-delegated image formats — no proprietary header)

> Clean-room spec. Neutral description only — NO sample bytes, NO decompiler pseudo-code.
> Consumed by Assets.Parsers. Every offset an engineer cites must reference this file.

## Identification

- **Extensions:** not fixed; texture files are referenced by logical VFS paths from scene config.
  Common extensions in D3D9-era clients include `.dds`, `.tga`, `.bmp` — actual extensions in this
  archive are SAMPLE-UNVERIFIED.
- **Found in:** `.vfs` archive (logical path pattern: `textures/*` or similar; config section label
  `TEXTURES` names the volume).
- **Magic / signature:** depends on the concrete format. DDS files begin with ASCII `DDS ` (four
  bytes); TGA files have no fixed magic. D3DX9 auto-detects both.
- **Endianness:** depends on concrete format (DDS headers are little-endian).

## There is no proprietary texture format

The client contains no custom texture header parser and no custom pixel decoder. All texture
decoding is fully delegated to the Direct3D 9 helper library (D3DX9). The engine extracts raw
bytes from the VFS, then passes them directly to a standard D3DX9 in-memory decode call. D3DX9
auto-detects the on-disk format from the file's own header bytes and handles all pixel decoding
internally.

This means `Assets.Parsers` does not need to implement a proprietary texture format. The parser's
only responsibility for textures is:

1. Locate and extract the raw bytes from the VFS entry (format-transparent; see `formats/pak.md`).
2. Pass the raw bytes to a standard image decoder (DDS-first is the high-confidence path).
3. Produce a decoded pixel buffer for `Assets.Mapping`.

## Loading paths observed in the client

Two code paths reach the same D3DX9 decode API:

**VFS path (primary):** Raw file bytes are loaded from the VFS into a heap allocation, then passed
to the D3DX9 in-memory decode function. The function receives the pointer and the byte count from
the VFS entry; it returns a Direct3D texture object. This is the normal runtime path.

**Disk fallback:** When the VFS is not mounted, the engine passes an on-disk file path to a D3DX9
file-from-disk variant. Same format auto-detection applies; D3DX9 opens and decodes the file
directly. This path is used during development or when the archive is unavailable.

Both paths share the same D3DX9 format auto-detection and produce equivalent output.

## Likely concrete formats (sample-unverified)

| Format | Confidence | Rationale |
|---|---|---|
| DDS with DXT1 block compression | HIGH | Standard D3D9-era compressed texture; smallest storage; natively supported by D3DX9 and hardware |
| DDS with DXT3 or DXT5 block compression | HIGH | Common for alpha-channel textures in this era |
| DDS uncompressed | MEDIUM | Used for render targets, palettised textures, or special-purpose surfaces |
| TGA | MEDIUM | Common in Asian MMORPG clients of this era; supported natively by D3DX9 |
| BMP | LOW | Possible for UI assets but large; supported by D3DX9 |

All confidence ratings above are based on platform and era evidence only. **No texture file from
this archive has been inspected.** Exact format(s) in use require a sample VFS-extracted texture
to confirm.

## JPEG is not an inbound texture format — confirmed

The Intel JPEG library (`ijl11`) is present in the client's import table, but only the write and
free functions are imported; the read function is not imported. This means JPEG decoding is never
called by this client. JPEG encoding is used for output paths (screenshot export or similar).
Inbound textures loaded from the VFS are not JPEG files.

## Enumerations / flags

None applicable — the client passes D3DX9 format and filter parameters that are standard D3D9
enumeration values (`D3DFORMAT`, `D3DPOOL`, filter flags). These are D3D9 API constants, not
game-specific values, and are outside the scope of this spec.

## Known unknowns

- Exact texture file extensions used in the archive: UNVERIFIED — requires a `.vfs` sample.
- Whether DDS, TGA, or a mix of both is used: UNVERIFIED — requires sample inspection.
- Whether any D3DX9 `colorKey` transparency value is set to a non-zero default (i.e., whether
  a color is treated as transparent at load time): UNVERIFIED.
- Mip-level generation policy (pre-generated in-file vs. D3DX9-generated at load time): UNVERIFIED.
- Whether all textures share the same format or different logical categories (UI vs. world vs.
  character) use different formats: UNVERIFIED.

## Implementation guidance for Assets.Parsers

Because the format is not proprietary, the parser implementation should:

- Extract raw bytes via the VFS layer (see `formats/pak.md`).
- Attempt DDS decoding first (highest-confidence format for this era); fall back to a general
  image decoder (e.g. StbImage or ImageSharp) that handles TGA, BMP, and PNG.
- Do NOT attempt JPEG decode for assets loaded from the VFS.
- If the first four bytes of the raw buffer are `44 44 53 20` (ASCII `DDS `), treat as DDS.
- Report the detected format and surface dimensions to `Assets.Mapping` along with the decoded
  pixel data.
- If an unrecognized format header is encountered, log the first four bytes and report failure
  rather than attempting a blind decode.

When a texture sample becomes available, update the "Likely concrete formats" table and promote
the highest-confidence entry to CONFIRMED.

## Cross-references

- Related formats: `formats/pak.md` (container that delivers raw texture bytes)
- Canonical names: see `Docs/RE/names.yaml` (`TextureEntry`, `Texture_LoadSimple`,
  `Texture_LoadFile_VFSorDisk`)
- Provenance: see `Docs/RE/journal.md`
