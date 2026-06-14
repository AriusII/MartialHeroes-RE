# Format: .inf / .vfs  (VFS archive container — index + data pair)

> Clean-room spec. Neutral description only — NO sample bytes, NO decompiler pseudo-code.
> Consumed by Assets.Parsers (Assets.Vfs). Every offset an engineer cites must reference this file.
>
> **Status: CONFIRMED against a real archive.** The reference implementation (`MappedVfsArchive`)
> parsed a real `data.inf` (6,241,992 bytes) + `data/data.vfs` (3.8 GB) cleanly: exactly
> **43,347 entries** (`6,241,992 = 24 + 144 × 43,347`, byte-exact), the declared payload extents
> tile the data blob to **100.0% coverage** (sum of sizes == blob length within the trailing 24 B),
> **zero** out-of-bounds offsets, **zero** TOC name-ordering violations (the binary-search key
> invariant holds), and every `data_size` high dword is zero (low-32-bit size confirmed). A small
> number of entries (≈65) share/overlap a payload offset — benign de-duplication of identical files.

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
- **Compression:** none on the data path — confirmed (see §Storage model — RAW/uncompressed).
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
| 12 | 4 | u32 LE | `entry_count` | Number of TOC entries. Drives both the heap allocation (`144 × entry_count` bytes) and the bulk read of the TOC array. | CONFIRMED |
| 16 | 4 | u32 LE | `unknown_16` | CONFIRMED-read-and-discarded. Part of the same bulk read; never extracted separately. | CONFIRMED-read-and-discarded |
| 20 | 4 | u32 LE | `unknown_20` | CONFIRMED-read-and-discarded. Last dword of the 24-byte bulk read; never consumed. | CONFIRMED-read-and-discarded |

The five non-`entry_count` dwords are definitively not consumed by the mount routine in the
examined client version. The distinction from "UNVERIFIED" is meaningful: these fields are
not merely unexamined — they are positively confirmed to be discarded without use. Their
on-disk content remains unknown without a sample.

### TOC array (immediately follows the 24-byte header)

`entry_count` records, each exactly **144 bytes (0x90)**, read in a single bulk call. The record
stride is confirmed independently three ways: the heap-allocation size (`144 × entry_count`), the
size byte-arithmetic of the reference archive (`24 + 144 × 43,347`), and three independent consumers
that agree on the same field offsets (the name-compare key, the seek/size read path, and the
raw-seek router copy).

**VfsEntry record — 144 bytes (0x90), little-endian:**

| Offset | Size | Type | Field | Notes | Confidence |
|-------:|-----:|------|-------|-------|------------|
| 0 | 100 | char[100] | `name` | Null-terminated ASCII virtual path. Stored lowercased at build time. Serves as the binary-search key. | CONFIRMED |
| 100 | 4 | u8[4] | `pad_100` | Alignment padding between the name field and the 8-byte-aligned offset field. Never read. Likely zero. | UNVERIFIED (structurally expected) |
| 104 | 8 | i64 LE | `dataOffset` | Byte offset of this entry's payload within `data/data.vfs`. Passed directly to a 64-bit seek call on the in-memory read path; the raw-seek router path uses only the low 32 bits (see §Open-mode dispatch). | CONFIRMED |
| 112 | 8 | i64 LE | `dataSize` | Byte count of this entry's payload. Only the low 32 bits are consumed; a non-zero high dword causes the read to fail. Treat as a u32 in practice, stored in a 64-bit field. | CONFIRMED |
| 120 | 24 | u8[24] | `pad_120` | Trailing bytes never accessed by any examined code path. Purpose entirely unknown — could be flags, a CRC, a timestamp, or reserved padding. | CONFIRMED-never-accessed |

**Total: 144 bytes = 0x90.**

## Storage model — RAW / uncompressed (CONFIRMED)

Each TOC entry's payload is stored **raw** in `data/data.vfs`: stored size equals on-disk size
equals in-memory size. The read worker allocates a buffer of exactly `dataSize` (low 32 bits) and
performs a single read of that many bytes; success requires that the number of bytes actually read
equals `dataSize` and that the high dword of `dataSize` is zero. There is:

- **no decompression call** (no LZ, zlib, or custom expansion stage),
- **no separate uncompressed-size field** distinct from `dataSize`, and
- **no per-entry codec or flag** that would select one.

Earlier cartography that referred to a "read/decompress path" is a **misnomer** for this build —
the entry read is a plain size-checked copy. Per-format decoding (DDS texture, mesh geometry, etc.)
happens later inside each format's own parser, never in the archive I/O layer.

## Lookup algorithm

Virtual path → data bytes in four steps:

1. **Normalize:** lowercase the requested virtual path (the stored `name` values are already
   lowercase; normalization is applied to the caller-supplied string before the search). Virtual
   paths are therefore effectively case-insensitive.
2. **Binary search:** search the TOC array in ascending order using a byte-for-byte string
   comparison on the 100-byte `name` field. The TOC must be sorted ascending by lowercased name at
   build time; this is assumed by the binary-search implementation and confirmed by its use.
3. **Seek:** on a hit, call a 64-bit seek to `entry.dataOffset` from the beginning of the open
   `data/data.vfs` handle.
4. **Read:** allocate `entry.dataSize` (low 32 bits) bytes and read that many bytes from the current
   position into the allocation. The seek+read pair is protected by a critical section (mutual
   exclusion on the shared file handle).

There is **no hash table**, **no compression**, and **no encryption** on this path.

### Concurrency contract — shared handle, serialized seek+read (CONFIRMED)

The retained `data/data.vfs` handle is shared by all in-memory loaders. The file pointer is global
to that handle, so the **seek and the read are performed as one atomic unit under a single
process-wide critical section**: a loader enters the lock, seeks to `dataOffset`, reads `dataSize`
bytes, then leaves the lock. This is the only synchronization on the read path and is what allows
concurrent loaders to share one file handle without racing the file pointer. A reimplementation that
keeps a shared handle must serialize seek+read together; alternatively it may give each reader an
independent handle or use a position-explicit (`pread`-style) read, in which case the lock is not
required.

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

The result is a freshly-allocated buffer containing the raw asset bytes. The caller owns it. There
is **no file-level cache** in the original: every open re-reads from disk into a fresh allocation.

## Open-mode dispatch — how callers request a file (CONFIRMED)

Every file open in the client goes through a single open router that takes a virtual/loose path and
an integer **mode** value. The router first asks whether the archive is mounted (a process-global
"mounted" flag set when the two-file open sequence above succeeds), then branches.

### Mode flag bits

The mode integer is a small bitfield. Three bits are consulted; bits above bit 2 are not examined.

| Bit | Mask | Meaning |
|----:|-----:|---------|
| 0 | 0x1 | read |
| 1 | 0x2 | write / create |
| 2 | 0x4 | **source selector (only relevant when the archive is mounted):** 0 = slurp the whole entry into memory via the lookup algorithm above; 1 = open a private archive handle and raw-seek to the entry, streaming from the archive instead of buffering the whole payload |

### Branch A — archive mounted

Sub-branch on **bit 2**:

- **bit 2 = 0 (slurp):** run the lookup algorithm (binary search → seek → read) and hand the caller
  the fully-buffered payload (data pointer + size). This is the normal asset read path.
- **bit 2 = 1 (raw archive seek):** open a **private** read handle on `data/data.vfs`, binary-search
  the TOC for the entry, copy the entry's offset/size into the file object, and seek the private
  handle to the entry's start. The caller then streams from the archive at that offset rather than
  holding the whole blob. This path seeks with a 32-bit seek using only the low 32 bits of
  `dataOffset` (it assumes the entry start fits in 32 bits, consistent with the read worker treating
  a non-zero `dataSize` high dword as a failure).

### Branch B — archive NOT mounted (loose-file fallback)

When the archive is not mounted, the TOC is bypassed entirely and a plain OS file is opened by the
given path. The open disposition is selected by the **read/write mode bits**:

| Mode bits | Access | Share | Disposition | Notes |
|-----------|--------|-------|-------------|-------|
| bit 0 set (read) | read | exclusive | open existing | Common case for loading assets from a loose client tree |
| bit 1 set, bit 0 clear (write) | write | exclusive | create-always (truncate/create) | Tools and saves |
| neither bit 0 nor bit 1 set | read + write | exclusive | open existing | Read/modify of an existing file |

On a successful loose-file open the logical size is taken from the OS file size. This fallback is
what lets the client run against an un-packed asset tree (loose files on disk) when no `data.inf` /
`data.vfs` pair is mounted — the same virtual path resolves to a real file at that relative path.

### Format-agnostic I/O — no central parser dispatch (CONFIRMED)

The open router decides only **where the bytes come from** (slurp / raw archive seek / loose file).
It does **not** decide **how to parse** them: there is no extension switch in the I/O layer. Each
format consumer obtains bytes through the router (or the by-name lookup) and then drives its own
reader. Format identity is implicit in the call site, not discriminated centrally. A reimplementation
should keep the archive/VFS layer (`Assets.Vfs`) format-agnostic and locate all decoding in the
per-format parsers (`Assets.Parsers`).

## Confidence and open questions

| Question | Status |
|---|---|
| `entry_count` position (offset 12 of header) | CONFIRMED — stored directly into global count variable and used as multiplier |
| `name[100]` + `dataOffset[104]` + `dataSize[112]` | CONFIRMED — corroborated by three independent call sites and by 64-bit index arithmetic |
| Record stride = 144 bytes | CONFIRMED — by allocation arithmetic, the byte-exact archive size, and field offsets |
| Stored RAW / uncompressed | CONFIRMED — single size-checked read, no decompress call, no separate uncompressed-size field |
| No compression / no encryption on read path | CONFIRMED — no decompress or decrypt call in the hot path |
| Seek+read serialized under a critical section | CONFIRMED — single shared data handle; seek and read bracketed by one lock |
| Three-way open-mode flag table (bit0 read / bit1 write / bit2 slurp-vs-raw-seek) | CONFIRMED — both router variants agree |
| Loose-file fallback disposition matrix | CONFIRMED — selected by the read/write mode bits when not mounted |
| No central parser-by-extension dispatch | CONFIRMED — multiple independent consumers each own their decode |
| Header `unknown_0`, `unknown_4`, `unknown_8`, `unknown_16`, `unknown_20` | CONFIRMED-read-and-discarded — all five are part of the bulk 24-byte read; none is extracted to a register, global, or branch condition; content unknown without a sample |
| `pad_100` (4 bytes at +100) | UNVERIFIED — expected alignment padding; not accessed |
| `pad_120` (24 bytes at +120) | CONFIRMED-never-accessed — no code reads these bytes; content unknown without a sample |
| TOC sort order at build time | CONFIRMED by binary-search usage; ascending by lowercased name |
| `dataSize` high dword always zero in practice | CONFIRMED in the reference archive (all entries) — a non-zero high dword causes the read to fail |

A single `.inf` + `.vfs` sample pair would resolve the content of the unknown header fields and
the trailing TOC padding. (The reference archive above resolves the structural questions; only the
*meaning* of the discarded header dwords and the trailing TOC padding remains open.)

## Cross-references

- Related formats: `formats/mesh.md` (payload type), `formats/texture.md` (payload type)
- Canonical names: see `Docs/RE/names.yaml` (`VfsHeader`, `VfsEntry`, `VfsEntry.name`,
  `VfsEntry.dataOffset`, `VfsEntry.dataSize`)
- Provenance: see `Docs/RE/journal.md`
