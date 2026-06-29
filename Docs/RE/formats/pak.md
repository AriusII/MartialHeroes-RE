# Format: .inf / .vfs  (VFS archive container — index + data pair)

> Clean-room spec. Neutral description only — NO sample bytes, NO decompiler pseudo-code.
> Consumed by Assets.Parsers (Assets.Vfs). Every offset an engineer cites must reference this file.
>
> **Verification:** **sample-verified** (the strongest tier — facts established by control-flow +
> operand evidence AND matched byte-for-byte against a real VFS sample).
> ida_reverified: 2026-06-27 · ida_anchor: f61f66a9ae0ec1e946105b2ecff76e8930cb1d1367df64e5688a5266f5ad9963 · evidence: [static-ida, vfs-sample]
> CYCLE 14 re-anchor (f61f66a9): 7 facts re-confirmed SAME, 0 corrected; build-delta data-page shift confirmed; all structural/behavioral claims unchanged.
> Deep-cartography pass 2026-06-29 (anchor f61f66a9): loose-file read share corrected (FILE_SHARE_READ, not exclusive; instruction-confirmed in both open routers); `field_08` role permanently bounded (not a debugger item — pack-tool artifact only); header struct-size validation 8+4+4+8 = 24 bytes (0x18) confirmed byte-exact.
> Prior re-verifications: 2026-06-16 (CYCLE 7), 2026-06-20 (CYCLE 7 final), 2026-06-24 (CYCLE 11 spec-audit: open-flags 0x10000001 instruction-confirmed; all other structural claims re-confirmed); 2026-06-24 (CYCLE 11 pak-family pass: full 43,347-entry scan — zero gaps, zero overlaps, 100% contiguous coverage; de-dup statement updated below)
> Conflicts: none vs the committed structural claims (the campaign-10 re-verification supplies a
> real sample that *promotes* the previously-"unknown content" header/TOC-trailing fields — those
> are additions, not corrections; every prior CONFIRMED offset/size/stride re-verified true).
> The earlier "small number of entries share/overlap a payload offset" de-duplication note is
> updated by the full 43,347-entry scan: zero shared offsets were found in the reference archive —
> entries tile perfectly contiguously with no gaps and no overlaps. The de-dup framing has been
> revised to reflect this (see §Two-witness result below).
>
> **Two-witness result.** The reference implementation (`MappedVfsArchive`) parsed a real
> `data.inf` (**6,241,992 bytes**) + `data/data.vfs` (**3,802,182,193 bytes**) cleanly: exactly
> **43,347 entries** (`6,241,992 = 24 + 144 × 43,347`, byte-exact), the declared payload extents
> tile the data blob to **100.0% coverage** (the last entry's `dataOffset + dataSize` equals the
> data.vfs length exactly), **zero** out-of-bounds offsets, **zero** TOC name-ordering violations
> (the binary-search key invariant holds), **zero gaps**, **zero shared/overlapping offsets** (all
> 43,347 entries tile perfectly contiguously — entries are 1:1, not de-duplicated in this archive),
> and every `dataSize` high dword is zero across the full 43,347-entry scan (low-32-bit size
> confirmed). The static-IDA read of the mount routine and the read primitive corroborates every
> structural field independently. (An earlier draft noted "a small number of entries share/overlap a
> payload offset"; the full 43,347-entry scan found zero such cases — that framing is withdrawn.)
>
> **Consolidation 2026-06-29:** `Docs/RE/vfs/archive_container.md` absorbed into this master; all structural content was already fully present here; ASCII layout diagram and explicit dedup callout folded as presentational additions only; no new facts, no verification-status change.

## Identification

- **Extension (index):** `.inf`  — default filename `data.inf`
- **Extension (data):**  `.vfs`  — default path `data/data.vfs`
- **Magic / signature (present on disk; not validated by the client):** the first 8 bytes of the
  header are a null-padded ASCII magic string **`VFS001`** (`'V','F','S','0','0','1','\0','\0'`) — this
  is **sample-verified present on disk**. The client, however, **does not validate it**: the 24-byte
  header is read in full in a single bulk operation, but **only** the `entry_count` field at offset 12
  is extracted and consumed. The magic bytes are **read-and-discarded** — the mount routine neither
  extracts them from the buffer nor compares them against any constant, and an image-wide byte search
  confirms the ASCII bytes `VFS001` are **absent from the executable**, so the client carries no
  constant to compare against (parser-verified). The client performs **no magic assertion, no version
  check, and no flags branch** on any surrounding field. The earlier reading of these 8 bytes as a
  4-char tag (`"VFS0"` / `"FVS0"`) plus a separate 2-char version (`"01"`) was a mis-split of this
  **one** 8-byte magic field; the authoritative direct byte-read is the single string `VFS001`. A
  reimplementation may assert this magic, but the original does not — so a reimplementation that wishes
  to remain bug-compatible should tolerate any header here.
- **Endianness:** little-endian throughout.
- **Compression:** none on the data path — confirmed (see §Storage model — RAW/uncompressed).
- **Encryption:** none on the data path — confirmed.

## Two-file scheme

**Quick-reference layout:**

```
[ data.inf ]
  ├── Header (24 bytes)
  └── TOC Array (144 bytes × entry_count)

[ data/data.vfs ]
  ├── Header Echo (24 bytes, identical duplicate of data.inf header)
  └── Payload Tiles (Contiguous data blocks starting at offset 24)
```

The archive is split across two physical files:

| Role | Default path | Lifecycle |
|---|---|---|
| Index / TOC | `data.inf` | Read once at startup into a heap array; file handle closed immediately after. |
| Data blob | `data/data.vfs` | Opened for read at startup; handle kept alive for the entire process lifetime. |

Opening sequence:
1. Open `data.inf` with `CreateFileA` flags `0x10000001` (`FILE_FLAG_RANDOM_ACCESS | FILE_ATTRIBUTE_READONLY`), read-only access, no write share.
2. Read the 24-byte header from `data.inf` in a single bulk read; extract `entry_count` at offset 12.
3. Allocate `144 × entry_count` bytes on the heap.
4. Read `144 × entry_count` bytes from `data.inf` starting at offset 24; populate the TOC array.
5. Close `data.inf`.
6. Open `data/data.vfs` with the same `CreateFileA` flags `0x10000001` (`FILE_FLAG_RANDOM_ACCESS | FILE_ATTRIBUTE_READONLY`) and retain the handle.

All subsequent asset reads seek within `data/data.vfs` using offsets recorded in the TOC.

### data.vfs leads with a verbatim 24-byte header echo (sample-verified)

The **first 24 bytes of `data/data.vfs` replicate the `data.inf` 24-byte header byte-for-byte**
(same `VFS001` magic, same `entry_count`, same total-blob-size). Consequently **no payload begins at
data.vfs offset 0** — every TOC `dataOffset` is `>= 24`, and the first entry's `dataOffset` is
exactly **24**. Entries then tile contiguously with no inter-entry padding (entry *N+1* `dataOffset`
= entry *N* `dataOffset` + entry *N* `dataSize`), and the last entry's `dataOffset + dataSize` equals
the data.vfs file length exactly. The runtime client never reads data.vfs offset 0 — it always seeks
straight to a per-entry `dataOffset` — so this echo is informational; a reimplementation must simply
honour `dataOffset` (which already starts at 24) and never assume payload begins at byte 0.

## Mount toggle and fixed paths (CONFIRMED)

### Packed-vs-loose is a config toggle, not a success flag

A single process-global **mounted** flag governs whether every file open routes through the archive
(packed mode) or falls back to a plain OS file (loose mode). The crucial behaviour: **this flag is a
pure configuration toggle, decided before any archive file is opened — it is NOT a success
predicate.**

- A boolean named `vfsmode` is read from the boot configuration script (the Lua boot file) early in
  startup. `vfsmode = true` selects packed (archive) mode; `vfsmode = false` selects loose
  (developer / flat-file) mode.
- The mounted flag is set **directly from that `vfsmode` boolean**, *before* the two-file open
  sequence runs. The single setter has exactly one call site (the startup routine); the flag has one
  reader API that every I/O consumer consults before deciding where bytes come from. The setter
  stores its argument byte and returns it, but the **return value is ignored** at the call site — it
  is a write, not a guard.
- The archive-open routine is invoked **unconditionally** regardless of `vfsmode`, but its **return
  value is ignored**. In loose mode the flag is already `0`, so nothing consults the TOC the open
  routine might build. If `data.inf` is absent in loose mode the open routine returns early without
  building a TOC — harmless. Loose mode is therefore robust to a missing or unused archive.

This refines an earlier (campaign-3) framing that described the mounted flag as "set elsewhere on
success." It is set elsewhere (in startup), but **not on success** — it is set unconditionally from
the config bool with no open-result predicate. (Behaviour of loose mode with a present-but-unused
archive is UNVERIFIED for lack of a sample, but the static path shows it is inert: the flag is
already 0 and the TOC, if built, is never consulted.)

| Mode | `vfsmode` | Mounted flag | File opens resolve to |
|---|---|---|---|
| **Packed** | `true` | 1 | TOC lookup in `data/data.vfs` (slurp or raw-seek per mode bit2) |
| **Loose / dev** | `false` | 0 | plain OS file at the given relative virtual path |

### The archive paths are hardcoded literals — no runtime path construction (CONFIRMED)

The two archive file names are **compiled-in string literals** read from static data; there is **no
runtime path build-up, no base-directory prefixing, and no configuration override** for them. Both
path globals are read-only at runtime (no writers; every reference feeds them straight to the OS
file-open call).

| Role | Hardcoded literal | Notes |
|---|---|---|
| Index / TOC | `data.inf` | relative to the process working directory |
| Data blob | `data/data.vfs` | relative to the process working directory |

A reimplementation should treat these as fixed names relative to the client root; there is no
override mechanism in the original to honour.

## Index file layout (`data.inf`)

### Header (24 bytes, little-endian)

The 24-byte header now reads, in struct terms:
`char magic[8] = "VFS001\0\0"; u32 field_08; u32 entry_count; u64 total_blob_size;`. Only
`entry_count` is consumed by the client; the other fields are positively confirmed
read-and-discarded by the mount routine, but a real sample has now resolved their on-disk content.

> **No FILETIME inside the 24-byte index header.** The three Windows FILETIME values
> (creation / last-access / last-write) documented below are **per-entry TOC fields** (at per-entry
> offsets 120 / 128 / 136 inside each 144-byte record), **not** index-header fields. The index header
> is exactly `magic(8) + field_08(4) + entry_count(4) + total_blob_size(u64)` with **no timestamp
> field anywhere in its 24 bytes** — in particular `field_08` at +0x08 is a single 4-byte tag (value
> 39), not a FILETIME or any timestamp.

| Offset | Size | Type | Field | Notes | Confidence |
|-------:|-----:|------|-------|-------|------------|
| 0 | 8 | char[8] | `magic` | Null-padded ASCII signature **`VFS001`** (`'V','F','S','0','0','1','\0','\0'`). **Present on disk; read-and-discarded / NOT validated by the client** — part of the single 24-byte bulk read, but the mount routine never extracts or compares it, and the ASCII bytes are absent from the executable image (no constant to compare against). Same 8 bytes appear at data.vfs offset 0. (Earlier "VFS0"/"FVS0" + "01" split readings were a mis-split of these 8 bytes.) | sample-verified present on disk; non-validation parser-verified |
| 8 | 4 | u32 LE | `field_08` | Opaque build/region/format-revision **tag** = **39 (0x27)** in the reference archive. **READ-AND-DISCARDED** — never read out of the 24-byte buffer (no consumer extracts, stores, or compares it anywhere in the client). NOT a FILETIME / timestamp (it is a single 4-byte value, not an 8-byte 100-ns tick count). NOT the entry count (the +0x08-as-count hypothesis is refuted, see below). | parser-verified (discarded); sample-verified value 39; role permanently bounded — cannot be resolved statically or at runtime; only an external pack-tool source could settle it (not a debugger item) |
| 12 | 4 | u32 LE | `entry_count` | Number of TOC entries (= **43,347** in the reference archive). The ONLY header field the mount routine extracts; drives both the heap allocation (`144 × entry_count` bytes) and the bulk read of the TOC array. | sample-verified |
| 16 | 4 | u32 LE | `total_blob_size` (lo) | Low dword of a u64 total-blob-size pair (with offset 20). = **3,802,182,193** in the reference archive — the **exact byte length of `data/data.vfs`**. Read-and-discarded by the client; useful as an integrity cross-check in a reimplementation. | sample-verified (read-and-discarded by client) |
| 20 | 4 | u32 LE | `total_blob_size` (hi) | High dword of the total-blob-size u64. = **0** in the reference archive. Read-and-discarded. | sample-verified (read-and-discarded by client) |

**Header struct-size validation:** 8 (magic) + 4 (field_08) + 4 (entry_count) + 8 (total_blob_size) = **24 bytes (0x18)** — field sum byte-exact, zero residual gap; matches the single 24-byte bulk `ReadFile` in `Vfs_Mount`.

The non-`entry_count` fields are definitively not consumed by the mount routine in the
examined client version — they are positively confirmed discarded without use (parser-verified: the
header is read once into a stack-local and only the +0x0C dword is extracted before the frame is torn
down, so no other function can observe the `+0x08` tag). Their on-disk **content** is now
sample-verified (magic / `field_08` scalar / total-blob-size), though the *meaning* of `field_08`
(= 39) is settled only as a value, not a role. Note that **none of these 24 header bytes is a
FILETIME** — the FILETIME timestamps live exclusively in the per-entry TOC records (see §TOC array).

**Entry-count position is at offset 12 (+0x0C), sample-verified both ways.** This resolves an
earlier open question (whether the count lived at +0x08 or +0x0C):
- Static-IDA witness: the count is loaded from the bulk-read buffer at buffer-relative **+0x0C**
  (the 4th dword), then stored to the global count and used as the `× 144` allocation multiplier.
- Sample witness: dword@+0x0C = 43,347 and `24 + 144 × 43,347 = 6,241,992` matches the `data.inf`
  size byte-exactly, whereas dword@+0x08 = 39 and `24 + 144 × 39 = 5,640` does **not**.
- The **+0x08-as-count reading is firmly refuted**; `field_08 = 39` is an unrelated scalar.

### TOC array (immediately follows the 24-byte header)

`entry_count` records, each exactly **144 bytes (0x90)**, read in a single bulk call. The record
stride is confirmed independently three ways: the heap-allocation size (`144 × entry_count`), the
size byte-arithmetic of the reference archive (`24 + 144 × 43,347`), and three independent consumers
that agree on the same field offsets (the name-compare key, the seek/size read path, and the
raw-seek router copy).

**VfsEntry record — 144 bytes (0x90), little-endian:**

| Offset | Size | Type | Field | Notes | Confidence |
|-------:|-----:|------|-------|-------|------------|
| 0 | 100 | char[100] | `name` | Null-terminated ASCII virtual path. Stored lowercased at build time. Serves as the binary-search key (byte-for-byte compare, lookup stops at the first null on both sides). Bytes after the null terminator may contain build-tool residue (ghost path strings / NTFS metadata) in a few entries — inert, since the compare stops at the null. | sample-verified |
| 100 | 4 | u8[4] | `pad_100` | Alignment padding between the name field and the 8-byte-aligned offset field. Never read by the client. **Zero in 43,333 / 43,347 entries; non-zero in 14 entries** (~0.03%) where build-tool path residue bled into the 4 bytes just past the name's null terminator. The anomalies are inert (the field is never consumed). | sample-verified (typically zero; 14 build-residue exceptions) |
| 104 | 8 | i64 LE | `dataOffset` | Byte offset of this entry's payload within `data/data.vfs`. Entry 0 = 24 (see the data.vfs header echo above); entries tile contiguously. Passed directly to a 64-bit seek on the in-memory slurp read path; the raw-seek router path uses only the low 32 bits (see §Open-mode dispatch). High dword is 0 across all sampled entries. | sample-verified |
| 112 | 8 | i64 LE | `dataSize` | Byte count of this entry's payload. Only the low 32 bits are consumed; a non-zero high dword causes the read to fail. Treat as a u32 in practice, stored in a 64-bit field. The raw-seek streaming path also copies this size as its per-entry read bound. High dword is 0 across the full 43,347-entry scan. | sample-verified |
| 120 | 8 | u64 LE | `creation_time` | Windows **FILETIME** (100-ns intervals since 1601-01-01 UTC) — the source file's NTFS **creation time** at pack time. Never read by the client. | sample-verified (real timestamps; never read by client) |
| 128 | 8 | u64 LE | `last_access_time` | Windows FILETIME — source file's NTFS **last-access time** at pack time. Never read by the client. | sample-verified (never read by client) |
| 136 | 8 | u64 LE | `last_write_time` | Windows FILETIME — source file's NTFS **last-write time** at pack time. Never read by the client. | sample-verified (never read by client) |

**Total: 144 bytes = 0x90.**

The 24 trailing bytes at offset 120 — previously documented as opaque `pad_120` whose content was
"could be flags, a CRC, a timestamp, or reserved" — are now resolved by a real sample as **three
8-byte Windows FILETIME values** (creation / last-access / last-write) that the build tool recorded
from the source file's NTFS metadata. The runtime client **never reads** these bytes (no find/read/
seek path touches offset 120..143), so they are inert metadata; a reimplementation may surface them
(e.g. for tooling) or ignore them with no behavioural difference.

## Storage model — RAW / uncompressed, ReadFile-into-buffer (CONFIRMED + sample-verified)

Each TOC entry's payload is stored **raw** in `data/data.vfs`: stored size equals on-disk size
equals in-memory size. The slurp read primitive allocates a buffer of exactly `dataSize` (low 32
bits) via the **CRT heap (`malloc`)**, then under a global lock performs a 64-bit absolute seek to
`dataOffset` and a single `ReadFile` of that many bytes; success requires that the number of bytes
actually read equals `dataSize` (low) and that the high dword of `dataSize` is zero, otherwise the
buffer is freed and zero bytes are reported. The caller owns the buffer and releases it with the
matching `free`. (Only the TOC array itself is allocated with `operator new`; the per-entry payload
buffer is `malloc`/`free` — a load-bearing detail for matching free semantics.) There is:

- **no decompression call** (no LZ, zlib, or custom expansion stage — no `*ompress*`/`*nflate*`/`*LZ*`
  import exists in the binary),
- **no separate uncompressed-size field** distinct from `dataSize`,
- **no per-entry codec or flag** that would select one (the only consumed entry fields are `name`@0,
  `dataOffset`@104, `dataSize`@112 — nothing at +100 or +120 is read on the I/O path), and
- **no payload deduplication** — each virtual path maps to a unique `dataOffset`; the archive does
  not alias multiple entries to the same payload range. The full 43,347-entry scan confirms zero
  shared offsets — entries are stored 1:1 (sample-verified).

This holds for **all three** read branches of the read primitive (see §The DiskFile read primitive):
the loose-file read, the raw-seek streaming read, and the in-memory slurp are each a plain
byte-for-byte transfer with no decode stage. Earlier cartography that referred to a
"read/decompress path" is a **misnomer** for this build — the entry read is a plain size-checked copy.
Per-format decoding (DDS texture, mesh geometry, etc.) happens later inside each format's own parser,
never in the archive I/O layer (confirmed by the UI/icon texture loader: it slurps the raw entry
bytes, hands the buffer + size to the in-memory D3DX texture create, then frees the buffer).

### ReadFile, not a memory-mapped view (CONFIRMED)

The entry payload is delivered by **`ReadFile` into a heap buffer**, never by a memory-mapped view.
The slurp path is `malloc` + `ReadFile`; the alternate raw-seek path uses a private OS handle with
`SetFilePointer` + `ReadFile`. The Windows memory-mapping APIs (`CreateFileMapping` /
`MapViewOfFile`) **are** imported but are used by exactly **one** unrelated routine — a self-integrity
/ anti-tamper check that maps the *executable image* and validates a keyed trailing-signature block.
That routine reads no archive entry. **Memory mapping is therefore not part of asset I/O** — a future
analyst should not mistake the lone `MapViewOfFile` site for a VFS slurp implementation. There is a
named C++ class for the slurp manager (conceptually `CVFSManager`); the boot configuration toggle
that selects packed-vs-loose is `vfsmode` in the boot Lua (see §Mount toggle).

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

The retained `data/data.vfs` handle is a **process-global** shared by all in-memory loaders. The
file pointer is global to that handle, so the slurp read primitive does, in order:
`malloc(dataSize.low)` → `EnterCriticalSection` (one global lock) → `SetFilePointerEx` (64-bit
absolute seek to `dataOffset`) → `ReadFile` (one read of `dataSize.low` bytes into the buffer) →
`LeaveCriticalSection`. The **seek and the read are thus performed as one atomic unit under a single
process-wide critical section**: a loader enters the lock, seeks, reads, then leaves. This is the
only synchronization on the read path and is what allows concurrent loaders to share one file handle
without racing the file pointer. The read is a `ReadFile`-into-buffer transfer — there is no
memory-mapped view of the data blob (see §ReadFile, not a memory-mapped view). A reimplementation that
keeps a shared handle must serialize seek+read together; alternatively it may give each reader an
independent handle or use a position-explicit (`pread`-style) read, in which case the lock is not
required.

A byte-cumulative **load-progress accumulator** is woven into the find/read functions (gated by a
tracking flag): each find/read adds the entry size to a running counter and recomputes a normalized
progress value. This is orthogonal to the read itself (it only drives the loading bar) but lives
inside the read functions; see `specs/resource_pipeline.md` for the loading-bar math.

### How to read a file (implementation sketch, format-layer only)

```
entry = BinarySearch(toc, toc_count, LowerCase(virtualPath));   // ascending byte compare on name[100]
if (entry == null) return NOT_FOUND;
buf = malloc(entry.dataSize & 0xFFFF_FFFF);       // low 32 bits only; CRT heap (caller frees)
EnterCriticalSection(&vfsReadLock);               // one process-wide lock
SeekAbsolute(vfsHandle, entry.dataOffset);        // 64-bit SetFilePointerEx
n = ReadFile(vfsHandle, buf, entry.dataSize & 0xFFFF_FFFF);
LeaveCriticalSection(&vfsReadLock);
if (n != (entry.dataSize & 0xFFFF_FFFF) || (entry.dataSize >> 32) != 0) { free(buf); return FAIL; }
return buf;                                        // raw payload; no decode, no mmap
```

The result is a freshly-allocated buffer containing the raw asset bytes. The caller owns it. There
is **no file-level cache** in the original: every open re-reads from disk into a fresh allocation.

## The DiskFile read primitive — three branches (CONFIRMED)

Every per-file read bottoms out in one unified read primitive on a DiskFile object. It takes a
caller buffer and a byte count and dispatches on the **mounted flag** and the open-mode **bit 2**
(raw-seek) into one of three branches. The DiskFile object carries a small set of fields used by
this primitive (field names are descriptive; offsets are relative to the object and are an internal
detail, not an on-disk layout):

| Field (descriptive) | Role |
|---|---|
| mode flags | bit0 read / bit1 write / bit2 raw-seek; the bit2 test selects the streaming branch |
| in-memory blob base | source pointer for the slurp branch |
| in-memory blob size | clamp bound for the slurp branch |
| file handle | OS handle for the loose-file and raw-seek branches |
| read cursor (u64) | running offset within the entry; advanced by the exact bytes consumed |
| entry size bound (u64) | per-entry length copied from the TOC `dataSize` at open; clamp for the raw-seek branch |

The three branches:

1. **Archive NOT mounted — loose file.** Read up to `n` bytes from the OS file handle; on success,
   advance the cursor by the bytes actually read. No explicit size clamp — the read is bounded by OS
   end-of-file (a short read signals EOF).

2. **Archive mounted, mode bit2 = 1 — raw-seek STREAMING.** **Size-clamp first:** if
   `cursor + n` would exceed the entry size bound, the read is refused (returns failure, cursor
   unchanged). Otherwise read up to `n` bytes from a **private** handle on `data/data.vfs` at the
   current OS file position (the handle was pre-positioned to the entry's data offset at open and is
   advanced only by prior sequential reads), then advance the cursor. This branch performs **no
   per-call re-seek** — it relies on the open-time seek plus sequential advance, so it assumes
   strictly sequential streaming (no random seek between reads). The private handle exists precisely
   so this streaming reader owns its own file pointer without contending the shared read-path handle
   and its critical section.

3. **Archive mounted, mode bit2 = 0 — in-memory slurp.** Clamp: if `cursor + n` would exceed the
   in-memory blob size, fail. Otherwise `memcpy` `n` bytes from `blob_base + cursor` into the caller
   buffer and advance the cursor. Pure memory copy from the already-slurped payload — no I/O.

Streaming-contract summary: **bytes in** = caller buffer + count; **bytes out** = up to `count`
copied/read; **cursor** = u64 advanced by exactly the bytes consumed; **size clamp** = the raw-seek
branch refuses any read crossing the entry size bound (returns failure, cursor unchanged), the
in-memory branch clamps against the blob size, the loose branch is OS-EOF bounded. **No
decompression at any branch** — consistent with the RAW verdict above.

## Open-mode dispatch — how callers request a file (CONFIRMED)

Every file open in the client goes through a single open router that takes a virtual/loose path and
an integer **mode** value. The router first asks whether the archive is mounted (the process-global
mounted flag described in §Mount toggle and fixed paths), then branches.

### Mode flag bits

The mode integer is a small bitfield. Three bits are consulted; bits above bit 2 are not examined.

| Bit | Mask | Meaning |
|----:|-----:|---------|
| 0 | 0x1 | read |
| 1 | 0x2 | write / create |
| 2 | 0x4 | **source selector (only relevant when the archive is mounted):** 0 = slurp the whole entry into memory via the lookup algorithm above; 1 = open a private archive handle and raw-seek to the entry, streaming from the archive instead of buffering the whole payload |

> Observed usage: every first-party asset loader examined opens with **bit0 = read, bit2 = 0** — i.e.
> the **slurp** branch when mounted, a loose OS file when not. The literal mode value is not always
> `1`: at least one table loader opens with mode `9` (= bit0 read + bit3). Since the router consults
> only bits 0/1/2 and ignores bits above bit2, mode 9 is behaviourally identical to mode 1 (read,
> slurp) — the load-bearing predicate is "bit0 = read, bit2 = 0", not the literal `1`. The **raw-seek
> streaming** branch (bit2 = 1) is implemented in the router but **no consumer was observed selecting
> it** across the texture / mesh / terrain / sound / effect / script / table loaders. Whether any
> caller anywhere uses bit2 = 1 is **(capture/debugger-pending)** — it appears unused on the asset
> path, but a definitive close would need a runtime open-mode census. See `specs/asset_pipeline.md`
> for the per-family loader census.

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
| bit 0 set (read) | read | `FILE_SHARE_READ` | open existing | Common case for loading assets from a loose client tree |
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
per-format parsers (`Assets.Parsers`). The full verdict — including the confirmation that there is
**no magic-byte sniffing anywhere** — is documented in `specs/asset_pipeline.md §Loader dispatch`.

## CONFLICT note — `bgtexture.lst` (binary) vs `bgtexture.txt` (text mirror)

The runtime terrain-texture index the client actually loads is a **binary** `bgtexture.lst`, not the
text `bgtexture.txt` referenced by earlier mappings. The `.txt` form is an authoring/source mirror;
the **runtime path is the `.lst` binary** (a u32 record count followed by fixed 48-byte records: a
kind byte plus a NUL-terminated relative name). The end-resolved per-texture path is the same
(`data/map000/texture/<rel>.dds`), only the intermediary index file name/format differs. The terrain
texture spec (`formats/terrain.md`) should follow the `.lst` binary, not the `.txt` mirror. The
`bgtexture.lst` record layout itself is documented in `formats/area_inventory.md` /
`specs/vfs_overview.md`; this note exists so the I/O layer's consumers point at the binary form.

## Confidence and open questions

| Question | Status |
|---|---|
| `entry_count` position (offset 12 / +0x0C of header) | sample-verified — static-IDA reads the count at buffer-relative +0x0C; sample arithmetic `24 + 144 × 43,347` is byte-exact. The +0x08-as-count hypothesis is REFUTED (`field_08` = 39, an unrelated scalar) |
| Header magic = `VFS001` (8 bytes @0) | sample-verified PRESENT on disk (`'V','F','S','0','0','1','\0','\0'`) but NOT validated by the client — read-and-discarded; the ASCII bytes are absent from the executable image (no constant to compare against), parser-verified; earlier "VFS0"/"FVS0"+"01" splits were mis-splits of these 8 bytes |
| Header `field_08` = 39 (@8) | parser-verified READ-AND-DISCARDED (never read out of the buffer; no consumer anywhere) + sample-verified value 39 — not the count, not a size, NOT a FILETIME; role permanently bounded — cannot be resolved statically or at runtime (only an external pack-tool source could settle it; no debugger census can recover a discarded build-time scalar) |
| Header `total_blob_size` u64 (@16/@20) | sample-verified — low dword = 3,802,182,193 = exact `data/data.vfs` byte length, high dword = 0; read-and-discarded by the client (usable as an integrity cross-check) |
| `name[100]` + `dataOffset[104]` + `dataSize[112]` | sample-verified — corroborated by three independent call sites, 64-bit index arithmetic, and the byte-read |
| Record stride = 144 bytes | sample-verified — by allocation arithmetic, the byte-exact archive size, and field offsets |
| Stored RAW / uncompressed | sample-verified — single size-checked read on all three branches, no decompress call, no separate uncompressed-size field; sample offsets tile to 100% with no compressed/expanded mismatch |
| Payload delivered by `ReadFile` into a `malloc` buffer, NOT a memory-mapped view | CONFIRMED — slurp = `malloc`+`ReadFile`; raw-seek = private handle + `ReadFile`; the lone `MapViewOfFile` site is an unrelated anti-tamper module-image check |
| No compression / no encryption on read path | CONFIRMED — no decompress/decrypt call in any read branch; no compression/inflate import in the binary |
| Seek+read serialized under one global critical section | CONFIRMED — single shared data handle; `EnterCS`→`SetFilePointerEx`→`ReadFile`→`LeaveCS` |
| Mounted flag is a config toggle (`vfsmode`), set before open, return-ignored | CONFIRMED — single set-site, single reader-API, set from the boot-Lua `vfsmode` bool, no success predicate |
| Archive paths `data.inf` / `data/data.vfs` are hardcoded literals | CONFIRMED — read-only path globals, no writers, no override mechanism |
| Three-way open-mode flag table (bit0 read / bit1 write / bit2 slurp-vs-raw-seek) | CONFIRMED — both router variants agree; router consults only bits 0/1/2 (higher bits ignored, so mode 9 ≡ mode 1) |
| Three-branch DiskFile read primitive (loose / raw-seek stream / in-mem slurp) | CONFIRMED — read-order verified per branch |
| Loose-file fallback disposition matrix | CONFIRMED — selected by the read/write mode bits when not mounted |
| No central parser-by-extension dispatch | CONFIRMED — multiple independent consumers each own their decode |
| Any consumer using the bit2 = 1 raw-seek streaming branch | capture/debugger-pending — none observed among the asset loaders; branch exists but appears unused on the asset path (a runtime open-mode census would close it) |
| `pad_100` (4 bytes at +100) | sample-verified — alignment padding, never read; zero in 43,333/43,347 entries, non-zero build-tool path residue in 14 entries (~0.03%), inert |
| TOC trailing 24 bytes (@120) = three Windows FILETIME (creation/last-access/last-write) | sample-verified — real source-file NTFS timestamps recorded by the build tool; never read by the client (was previously opaque `pad_120`) |
| data.vfs leads with a verbatim 24-byte header echo (entry 0 `dataOffset` = 24) | sample-verified — first 24 bytes of data.vfs are byte-identical to data.inf; entries tile contiguously, last entry tiles to the blob end |
| TOC sort order at build time | sample-verified — ascending by lowercased name; binary-search usage + 200-entry sort-order sample, zero out-of-order |
| `dataSize` high dword always zero in practice | sample-verified — zero across the full 43,347-entry scan; a non-zero high dword causes the read to fail |
| `bgtexture.lst` (binary) is the runtime terrain-texture index; `.txt` is an authoring mirror | CONFIRMED — see CONFLICT note above |
| `data.inf` / `data/data.vfs` open flags | CONFIRMED — `CreateFileA` flags `0x10000001` = `FILE_FLAG_RANDOM_ACCESS \| FILE_ATTRIBUTE_READONLY`, instruction-confirmed in the mount routine (`Vfs_Mount`) for both opens |

The campaign-10 two-witness re-verification resolved the structural questions AND the header/TOC
field content. The campaign-11 re-verification added the concrete open-flag value. The only residuals
are runtime/ambiguous: the **meaning** of `field_08` (= 39) and whether **any** consumer ever selects
the bit2 = 1 raw-seek streaming branch — both **(capture/debugger-pending)**.

## Cross-references

- Related formats: `formats/mesh.md` (payload type), `formats/texture.md` (payload type),
  `formats/terrain.md` (follows `bgtexture.lst`), `formats/area_inventory.md` (`.lst` index layouts)
- Related specs: `specs/asset_pipeline.md` (loader dispatch verdict, cache model, linkage chains,
  bulk loader), `specs/vfs_overview.md` (directory tree + extension census + manifest linkage),
  `specs/resource_pipeline.md` (runtime resource pipeline, terrain streaming, subsystem caches)
- Canonical names: see `Docs/RE/names.yaml` (`VfsHeader` {`magic`, `field_08`, `entry_count`,
  `total_blob_size`}, `VfsEntry` {`name`, `pad_100`, `dataOffset`, `dataSize`, `creation_time`,
  `last_access_time`, `last_write_time`}). New field names from the campaign-10 sample
  (`magic`/`total_blob_size`/the three FILETIME fields) are flagged for the glossary owner — names.yaml
  is orchestrator-owned and not edited here.
- Provenance: see `Docs/RE/journal.md`
