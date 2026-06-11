# Format: .inf / .vfs  (VFS archive container — index + data pair)

> Clean-room spec. Neutral description only — NO sample bytes, NO decompiler pseudo-code.
> Consumed by Assets.Parsers (Assets.Vfs). Every offset an engineer cites must reference this file.

## Identification

- **Extension (index):** `.inf`  — default filename `data.inf`
- **Extension (data):**  `.vfs`  — default path `data/data.vfs`
- **Magic / signature:** none confirmed. The 24-byte header is read in full in a single bulk
  operation, but only the `entry_count` field at offset 12 is extracted and consumed. The five
  surrounding dwords are read-and-discarded by the mount routine — the client performs no magic
  assertion, no version check, and no flags branch on any of them. Whether those fields carry
  a meaningful magic tag, version number, or file count in the archive as built is unknown without
  a sample; it cannot be inferred from the parser alone.
- **Endianness:** little-endian throughout.
- **Compression:** none on the data path — confirmed.
- **Encryption:** none on the data path — confirmed.

## Two-file scheme

The archive is split across two physical files:

| Role | Default path | Lifecycle |
|---|---|---|
| Index / TOC | `data.inf` | Read once at startup into a heap array; file handle closed immediately after. |
| Data blob | `data/data.vfs` | Opened for read at startup; handle kept alive for the entire process lifetime. |

Opening sequence:
1. Read the 24-byte header from `data.inf` in a single bulk read; extract `entry_count` at offset 12.
2. Allocate `144 × entry_count` bytes on the heap.
3. Read `144 × entry_count` bytes from `data.inf` starting at offset 24; populate the TOC array.
4. Close `data.inf`.
5. Open `data/data.vfs` and retain the handle.

All subsequent asset reads seek within `data/data.vfs` using offsets recorded in the TOC.

## Index file layout (`data.inf`)

### Header (24 bytes, little-endian)

| Offset | Size | Type | Field | Notes | Confidence |
|-------:|-----:|------|-------|-------|------------|
| 0 | 4 | u32 LE | `unknown_0` | CONFIRMED-read-and-discarded. Present in the single 24-byte bulk read; the mount routine does not extract or branch on this value. Possibly a magic tag or build identifier — content unknown without a sample. | CONFIRMED-read-and-discarded |
| 4 | 4 | u32 LE | `unknown_4` | CONFIRMED-read-and-discarded. Same treatment: read as part of the bulk read, never extracted to a register or global afterward. Possibly a format version. | CONFIRMED-read-and-discarded |
| 8 | 4 | u32 LE | `unknown_8` | CONFIRMED-read-and-discarded. Third dword; never consumed by any examined code path. | CONFIRMED-read-and-discarded |
| 12 | 4 | u32 LE | `entry_count` | Number of TOC entries. Drives both the heap allocation (`144 × entry_count` bytes) and the bulk `ReadFile` call for the TOC array. | CONFIRMED |
| 16 | 4 | u32 LE | `unknown_16` | CONFIRMED-read-and-discarded. Part of the same bulk read; never extracted separately. | CONFIRMED-read-and-discarded |
| 20 | 4 | u32 LE | `unknown_20` | CONFIRMED-read-and-discarded. Last dword of the 24-byte bulk read; never consumed. | CONFIRMED-read-and-discarded |

The five non-`entry_count` dwords are definitively not consumed by the mount routine in the
examined client version. The distinction from "UNVERIFIED" is meaningful: these fields are
not merely unexamined — they are positively confirmed to be discarded without use. Their
on-disk content remains unknown without a sample.

### TOC array (immediately follows the 24-byte header)

`entry_count` records, each exactly **144 bytes (0x90)**, read in a single bulk call. The record
stride is confirmed independently by the heap-allocation size (`144 × entry_count`), a literal
stride constant used in pointer arithmetic, and the field offsets below.

**VfsEntry record — 144 bytes (0x90), little-endian:**

| Offset | Size | Type | Field | Notes | Confidence |
|-------:|-----:|------|-------|-------|------------|
| 0 | 100 | char[100] | `name` | Null-terminated ASCII virtual path. Stored lowercased at build time. Serves as the binary-search key. | CONFIRMED |
| 100 | 4 | u8[4] | `pad_100` | Alignment padding between the name field and the 8-byte-aligned offset field. Never read. Likely zero. | UNVERIFIED (structurally expected) |
| 104 | 8 | i64 LE | `dataOffset` | Byte offset of this entry's payload within `data/data.vfs`. Passed directly to a 64-bit seek call. | CONFIRMED |
| 112 | 8 | i64 LE | `dataSize` | Byte count of this entry's payload. Only the low 32 bits are consumed; a non-zero high dword causes the read to fail. Treat as a u32 in practice, stored in a 64-bit field. | CONFIRMED |
| 120 | 24 | u8[24] | `pad_120` | Trailing bytes never accessed by any examined code path. Purpose entirely unknown — could be flags, a CRC, a timestamp, or reserved padding. | CONFIRMED-never-accessed |

**Total: 144 bytes = 0x90.**

## Lookup algorithm

Virtual path → data bytes in four steps:

1. **Normalize:** lowercase the requested virtual path (the stored `name` values are already
   lowercase; normalization is applied to the caller-supplied string before the search).
2. **Binary search:** search the TOC array in ascending order using a byte-for-byte string
   comparison on the 100-byte `name` field. The TOC must be sorted ascending by lowercased name at
   build time; this is assumed by the binary-search implementation and confirmed by its use.
3. **Seek:** on a hit, call a 64-bit seek to `entry.dataOffset` from the beginning of the open
   `data/data.vfs` handle.
4. **Read:** allocate `entry.dataSize` (low 32 bits) bytes and read that many bytes from the current
   position into the allocation. The seek+read pair is protected by a critical section (mutual
   exclusion on the shared file handle).

There is **no hash table**, **no compression**, and **no encryption** on this path.

### How to read a file (implementation sketch, format-layer only)

```
// All operations inside a lock on the shared .vfs file handle.
entry = BinarySearch(toc, toc_count, LowerCase(virtualPath));
if (entry == null) return NOT_FOUND;
SeekAbsolute(vfsHandle, entry.dataOffset);        // 64-bit seek
buf = Allocate(entry.dataSize & 0xFFFF_FFFF);     // low 32 bits only
Read(vfsHandle, buf, entry.dataSize & 0xFFFF_FFFF);
return buf;
```

The result is a freshly-allocated buffer containing the raw asset bytes. The caller owns it.

## Confidence and open questions

| Question | Status |
|---|---|
| `entry_count` position (offset 12 of header) | CONFIRMED — stored directly into global count variable and used as multiplier |
| `name[100]` + `dataOffset[104]` + `dataSize[112]` | CONFIRMED — corroborated by two independent call sites and by 64-bit index arithmetic |
| Record stride = 144 bytes | CONFIRMED — by allocation arithmetic, stride literal, and field offsets |
| No compression / no encryption on read path | CONFIRMED — no decompress or decrypt call in the hot path |
| Header `unknown_0`, `unknown_4`, `unknown_8`, `unknown_16`, `unknown_20` | CONFIRMED-read-and-discarded — all five are part of the bulk 24-byte read; none is extracted to a register, global, or branch condition; content unknown without a sample |
| `pad_100` (4 bytes at +100) | UNVERIFIED — expected alignment padding; not accessed |
| `pad_120` (24 bytes at +120) | CONFIRMED-never-accessed — no code reads these bytes; content unknown without a sample |
| TOC sort order at build time | CONFIRMED by binary-search usage; expected ascending by lowercased name |
| `dataSize` high dword always zero in practice | ASSUMED — high dword causes read failure if nonzero; almost certainly zero for all entries |

A single `.inf` + `.vfs` sample pair would resolve the content of the unknown header fields and
the trailing TOC padding.

## Cross-references

- Related formats: `formats/mesh.md` (payload type), `formats/texture.md` (payload type)
- Canonical names: see `Docs/RE/names.yaml` (`VfsHeader`, `VfsEntry`, `VfsEntry.name`,
  `VfsEntry.dataOffset`, `VfsEntry.dataSize`)
- Provenance: see `Docs/RE/journal.md`
