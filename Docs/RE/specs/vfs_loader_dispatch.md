---
verification: confirmed (IDB SHA f61f66a9, CYCLE 14 re-anchor; instruction-level deepening of the read->decode->build dispatch path; wave-11 deep-dive 2026-06-28: bit-3 text mode resolved, raw-seek CODE-CONFIRMED at open+read, full 24-byte header table, full 144-byte TOC record with statically-proven-never-read FILETIMEs, full DiskFile field map + vtable role map, per-extension consumer fan-out table)
ida_anchor: f61f66a9ae0ec1e946105b2ecff76e8930cb1d1367df64e5688a5266f5ad9963
ida_reverified: 2026-06-28
evidence: [static-ida, instruction-level]
status: confirmed
subsystems: [vfs_loader, disk_file, open_router]
conflicts: none
networked: false
deep_cartography: 2026-06-29 — static re-verification at anchor f61f66a9; §4.1/§6 slurp descriptor field order corrected to {ptr@0, status/unused@+4, sizeLow@+8, sizeHigh@+12} (binary-won, matches §10); §9 struct-size validation added (144 bytes, zero residual gap); field_08 role permanently bounded; loose-file FILE_SHARE_READ confirmed; pak.md loose-table and field_08 entries corrected
consolidation: 2026-06-29 — absorbed offline .pre/.post patching-pipeline detail from Docs/RE/vfs/io_subsystem.md (§7); source carried no IDB anchor, content folded as static-hypothesis/unverified at f61f66a9 into new §14
---

# VFS Loader Dispatch — Clean-Room Specification

> Clean-room neutral spec. Promoted from dirty-room analyst notes under EU Software Directive
> 2009/24/EC Art. 6. No decompiler pseudo-code, no binary virtual addresses, no decompiler
> identifiers. Every behaviour described below is expressed in the spec-author's own words and
> tables, based on mechanically neutral analyst annotations.
>
> **Scope.** This spec documents the **function-level read→decode→build dispatch path** for 3D
> assets: the exact stages a file path traverses from a consumer request to a live runtime object.
> It deepens the overview-level description in `specs/vfs_overview.md §0.1` and the chokepoint
> summary in `specs/resource_pipeline.md §1` with instruction-level detail recovered in CYCLE 14.
>
> **Out of scope / cross-references.** The on-disk `.inf`/`.vfs` container byte layout (full TOC
> record, magic, flags) is owned by `formats/pak.md`. Per-format field layouts are owned by the
> `formats/*.md` family. GHTex/TerrainPool two-level reference-counting and subsystem-cache
> internals are owned by `specs/resource_pipeline.md §3B`. Boot-load corpus and progress-tracking
> mechanics are owned by `specs/resource_pipeline.md §2`.

---

## Evidence grades

- **(CODE-CONFIRMED)** — behaviour recovered directly from binary control-flow; safe to implement.
- **(STATIC-HYPOTHESIS)** — inferred from static analysis without a live debug witness; implement but mark tunable.
- **(DEBUGGER-PENDING)** — hypothesis not exercised in this pass; do **not** hard-code.

---

# 1. Dispatch architecture — no central format switch — (CODE-CONFIRMED)

There is **no central "parse by extension or magic" dispatcher** in the client. The read→decode→build
chain is three separable stages:

1. **Consumer formats the canonical path.** Each subsystem (skin manager, terrain pool, texture
   loader, etc.) builds its own VFS-relative path string before calling any open routine.
2. **Open router selects the byte source.** A single open router picks among three I/O backends by
   examining the mount flag and a 3-bit mode field.
3. **Family decoder runs at the call site.** The open router is format-agnostic; the consuming loader
   already knows which decoder to invoke on the returned bytes or stream.

The effective dispatch chain is: `id/name → consumer-formatted path → open router → byte source → consumer's own decoder`.

This is proven by the consumer fan-out: the find-and-read chokepoint (`Vfs_FindAndReadEntry`) has
approximately 9 direct callers, `DiskFile_OpenByName` approximately 20, and `DiskFile_OpenByValue`
approximately 26 — each a distinct family loader that decodes at its own call site.

---

# 2. Stage 0 — Mount (`Vfs_Mount`) — (CODE-CONFIRMED)

Called once from the application entry point before the scene loop. Steps in order:

1. Open the **index file** (`data.inf`) for reading (`CreateFileA`, `GENERIC_READ`,
   `FILE_SHARE_READ`, `OPEN_EXISTING`).
2. Read a **24-byte header block** from `data.inf` into a stack buffer. The **entry count** is
   taken from the fourth 32-bit field (header offset `+0x0C`). The remaining header fields are
   consumed but not validated — magic is not checked.

   **data.inf 24-byte header layout (little-endian) — (CODE-CONFIRMED):**

   | Offset | Size | Type | Field | Runtime handling |
   |---:|---:|---|---|---|
   | `+0x00` | 8 | char[8] | `magic` (`"VFS001\0\0"`) | present on disk; **not extracted or compared** by `Vfs_Mount` |
   | `+0x08` | 4 | u32 | `field_08` (value 39 in shipped build) | read-and-discarded; meaning unknown (pack-tool artifact; see §12) |
   | `+0x0C` | 4 | u32 | `entry_count` | the **only consumed field**: drives `144 × entry_count` allocation and TOC bulk read |
   | `+0x10` | 8 | u64 | `total_blob_size` | read-and-discarded; matches `data.vfs` file length in the shipped build |

   Field meanings on disk are authoritative in `formats/pak.md`; this table records only what `Vfs_Mount` does with each field at runtime.
3. Allocate the in-memory TOC array: `operator new(144 × entry_count)`.
4. Read the entire TOC (`144 × entry_count` bytes) into the allocated array in one `ReadFile` call.
5. Close the `data.inf` handle.
6. Open the **data archive** (`data/data.vfs`, same `CreateFileA` flags) and retain the resulting
   OS handle (`g_VfsDataHandle`) for the lifetime of the process.

The **mount-flag byte** (`g_VfsMounted`) is set by the caller from the `vfsmode` key in `game.lua`;
`Vfs_Mount` itself does not set it. The is-mounted predicate (`Vfs_IsMounted`) simply returns that
byte.

The 24-byte header read, the 144-byte TOC stride, and the retained `g_VfsDataHandle` confirm
`specs/resource_pipeline.md §1.5.1` at the instruction level.

---

# 3. Stage 1 — Consumer path resolution — (CODE-CONFIRMED)

Each subsystem formats its own canonical VFS path before calling the open router or chokepoint.
There is **no global per-request path table** for 3D assets. The boot worker pulls a contiguous
global pointer table of literal data-table paths (see `specs/resource_pipeline.md §2.1a`), but 3D
assets are path-formatted on demand by their manager.

Representative examples recovered from the consumer fan-out:

| Family | Example path |
|---|---|
| Skinned mesh | `data/char/skin/g{id}.skn` (CoreSkinManager cache-miss path) |
| Bind pose | `data/char/bind/g{id}.bnd` |
| Terrain texture index | `data/map000/texture/bgtexture.lst` (TerrainPool initialisation) |
| Terrain cell scene | paths derived from the `.map` text descriptor (`formats/terrain_scene.md`) |
| Texture / icon / sky surface | consumer passes the already-resolved relative path |

---

# 4. Stage 2 — Open router — (CODE-CONFIRMED)

Two router entry points exist: `DiskFile_OpenByValue` (takes the path by value) and
`DiskFile_OpenByName` (copies a `std::string` first). Both stamp the mode field into the
`Diamond::DiskFile` object and execute the **same three-way branch** body; they differ only in the
initial copy step.

## 4.1 Branch table

| Condition | I/O path chosen | Mechanism |
|---|---|---|
| `Vfs_IsMounted()` returns false (loose) | **Loose OS file** | `CreateFileA` on the bare path; access flags chosen by the request's read/write mode bits: read → `GENERIC_READ` / `OPEN_EXISTING`; write → `GENERIC_WRITE` / `CREATE_ALWAYS`; read+write → `GENERIC_READ\|GENERIC_WRITE` / `OPEN_EXISTING`. File size retrieved and progress accumulated via `Vfs_AccumulateLoadProgress`. |
| Mounted, `(mode & 4) == 0` (slurp) | **In-memory entry** | `Vfs_FindAndReadEntry` loads the entry into a heap buffer and stores a 4-field descriptor `{ptr@0, status/unused@+4, sizeLow@+8, sizeHigh@+12}` in the `DiskFile` object. Succeeds iff `ptr ≠ null`. |
| Mounted, `(mode & 4) ≠ 0` (raw-seek) | **Private-handle stream** | A private `CreateFileA` on `data/data.vfs`; `Vfs_FindEntry` locates the TOC metadata; the entry's `dataOffset` (u64) and `dataSize` (u64) are stored in the `DiskFile` object; file pointer is positioned to `dataOffset.low` via `SetFilePointer` (32-bit seek, low dword only). |

## 4.1a Loose-file `CreateFileA` flag matrix — (CODE-CONFIRMED)

The full Win32 flag combination selected by the open router when the VFS is not mounted:

| Mode bits | Access mask | Share | Disposition | Flags |
|---|---|---|---|---|
| bit 0 set (read) | `GENERIC_READ` | `FILE_SHARE_READ` | `OPEN_EXISTING` | `FILE_FLAG_RANDOM_ACCESS \| FILE_ATTRIBUTE_READONLY` |
| bit 1 set, bit 0 clear (write) | `GENERIC_WRITE` | exclusive (0) | `CREATE_ALWAYS` | `FILE_FLAG_RANDOM_ACCESS` |
| neither bit 0 nor bit 1 (read+write) | `GENERIC_READ \| GENERIC_WRITE` | exclusive (0) | `OPEN_EXISTING` | `FILE_FLAG_RANDOM_ACCESS` |

The loose read path uses **`FILE_SHARE_READ`**, not exclusive sharing. The loose-table entry in `formats/pak.md §Open-mode dispatch` has been corrected to reflect this. After `CreateFileA`, `GetFileSize` populates `DiskFile +0x48` (size low; high written as 0); progress is accumulated via `Vfs_AccumulateLoadProgress`.

## 4.2 Mode-bit semantics

Bits 0–2 of the mode word are the load-bearing selectors:

| Bit | Meaning |
|---|---|
| bit 0 | Read intent |
| bit 1 | Write / create intent |
| bit 2 | Source selector: 0 = slurp to memory, 1 = raw-seek stream |
| bit 3 | **TEXT MODE** — at read time only: char-by-char read with CR/LF → LF normalisation; no effect on open routing |

Bits 0–2 govern open routing. **Bit 3 is load-bearing at read time** — `DiskFile_ReadVirtual` tests `mode & 8`: if set it calls `DiskFile_ReadBytesClampedToEof` (text, char-by-char CR/LF normalisation via `DiskFile_GetCharTranslated`); otherwise it calls vtable slot 1 `DiskFile_ReadBytes` (binary block). Mode 1 (read, binary) and mode 9 (read + text) are not interchangeable at read time: mode 9 collapses `\r\n` and lone `\r` to `\n`; mode 1 reads raw blocks. CP949 text-table loaders open with mode 9; binary asset loaders open with mode 1. This corrects the earlier claim that higher bits are ignored — that applies to open routing only, not the full read path. This matches and extends `specs/vfs_overview.md §0.1`.

## 4.3 Raw-seek branch — (CODE-CONFIRMED at open and read time)

The raw-seek branch is **fully implemented and statically confirmed** at both open time and read time.

**Open time:** a private `CreateFileA` on `data/data.vfs` is opened with `FILE_SHARE_READ`;
`Vfs_FindEntry` retrieves the TOC record; `dataOffset` and `dataSize` are copied into the `DiskFile`
object; `SetFilePointer` positions to `dataOffset.low` using the **32-bit low dword only**
(`lpDistanceToMoveHigh = NULL`). This assumes all entry starts fit within 32 bits — consistent with
the `dataSize.high == 0` hard gate in `Vfs_ReadEntryData`.

**Read time:** vtable slot 1 (`DiskFile_ReadBytes`) checks `cursor + n ≤ size` (clamp to entry
boundary) then calls `ReadFile(privateHandle, buf, n)` and advances the cursor by `nRead`. There is
**no per-call re-seek** — correctness relies on the open-time positioning and a strictly sequential
cursor advance.

**Consumer census — [DEBUGGER-CONFIRM], tightly bounded:** No static call site was observed passing
mode bit 2 = 1 across the recovered consumer roster (texture, skin, bind, terrain, sound, script,
and table loaders all use mode 1 or 9; see §8.4). The raw-seek path appears to be dead code on the
shipped asset path. A live open-mode census (breakpoint `DiskFile_OpenByName` /
`DiskFile_OpenByValue` during a real area + character load, observe `mode & 4`) is required to
confirm zero usage. `specs/resource_pipeline.md §8 item 15` tracks this item.

---

# 5. Stage 3 — Metadata lookup (`Vfs_FindEntry`) — (CODE-CONFIRMED)

`Vfs_FindEntry` performs a **pure metadata lookup** — it returns the TOC record pointer (offset +
size) without transferring any bytes.

Steps:

1. `memset` a 100-byte stack key buffer; `strcpy` the requested path into it; `_strlwr` to
   lowercase.
2. If `g_VfsTocCount ≤ 0` (treated as a signed 32-bit integer in loop bounds), return null.
3. **Ascending binary search** over the 144-byte-stride TOC array:
   `mid = lo + (hi − lo) / 2`; `strcmp(TOC_base + 144 × mid, key)`;
   on `< 0` advance `lo = mid + 1`; on `> 0` retreat `hi = mid`; on `== 0` — hit.
4. On a **hit** with progress tracking enabled: accumulate the entry's `dataSize` into the
   cumulative bytes counter and recompute the normalised progress quotient.
5. Returns the matching TOC record pointer, or null on a miss.

There is **no hash table and no filename interning** — names are stored already-lowercased in the
TOC and the runtime lowercases the query before comparing. Binary-search complexity is O(log N)
over the approximately 43 000-entry shipped VFS. This confirms and sharpens
`specs/resource_pipeline.md §1.2`.

---

# 6. Stage 4 — Find-and-read chokepoint (`Vfs_FindAndReadEntry`) — (CODE-CONFIRMED)

`Vfs_FindAndReadEntry` is the combined entry point used by most callers. Steps:

1. Lowercase the requested path (same `memset` / `strcpy` / `_strlwr` sequence as Stage 3).
2. **Zero the 16-byte output descriptor** `{ptr@0, status/unused@+4, sizeLow@+8, sizeHigh@+12}` at the
   caller-supplied address before doing anything else.
3. Only if `g_VfsDataHandle` is valid, run the ascending binary search (Stage 3 find); on a match
   call `Vfs_ReadEntryData`.
4. Accumulate `desc.sizeLow` into the progress counter and recompute the normalised value.
5. Return the populated descriptor; a miss leaves the descriptor zeroed.

The **zero-initialised output block** is a clean-room contract: a miss descriptor is all-zero
(`ptr == null`, `sizeLow == 0`), never a partially filled value. This matches and sharpens
`specs/resource_pipeline.md §1.5.4`.

---

# 7. Stage 5 — RAW transfer primitive (`Vfs_ReadEntryData`) — (CODE-CONFIRMED)

`Vfs_ReadEntryData` is the only function that moves bytes from the archive to a heap buffer.

Steps:

1. If the output descriptor already holds a buffer pointer, `free` it (per-descriptor buffer
   recycling — not a cache).
2. `malloc(dataSize.low)`. On allocation failure, return without writing the descriptor.
3. `EnterCriticalSection(&g_VfsReadLock)` — the **single process-global read lock**.
4. `SetFilePointerEx(g_VfsDataHandle, dataOffset_u64, NULL, FILE_BEGIN)` — 64-bit absolute seek on
   the shared retained handle.
5. `ReadFile(g_VfsDataHandle, buf, dataSize.low, &nRead, NULL)`.
6. `LeaveCriticalSection(&g_VfsReadLock)`.
7. **Success gate:** succeeds iff `nRead == dataSize.low` AND `dataSize.high == 0`. On failure,
   `free` the buffer, null `ptr`, zero `status`.

Key properties:

- **ReadFile into heap buffer, never `MapViewOfFile`.** The data is always materialised in a fresh
  `malloc` allocation.
- **No decompression, no decryption, no per-entry codec.** Exactly `dataSize` raw bytes are
  transferred.
- **The lock brackets only the seek + read pair** because all readers share the single retained
  `g_VfsDataHandle` file pointer, whose position is a shared global state.
- **`dataSize.high == 0` is a hard gate.** Entries whose logical size would require the high dword
  of the u64 to be non-zero are treated as read failures. All shipped entries have
  `dataSize.high == 0` in practice.

This matches and sharpens `specs/resource_pipeline.md §1.5.3`.

---

# 8. Stage 6 — Decode + build (per family, at the call site) — (CODE-CONFIRMED)

The byte source from Stage 4/5 is format-agnostic. The consuming loader decodes. Three
representative 3D-family decode tails:

## 8.1 Texture family (`Texture_LoadFromVfsOrDisk`) — DDS/TGA/PNG — (CODE-CONFIRMED)

- **Mounted:** `Vfs_FindAndReadEntry` slurps the entry into `{ptr, size}`. The loader calls
  `D3DXCreateTextureFromFileInMemoryEx(device, ptr, size, width, height, MipLevels, usage, format,
  pool, filter, mipFilter, colorKey, imageInfo, &out)` to decode directly from the in-memory
  buffer. The resulting `IDirect3DTexture9*` handle is pushed into the calling window's
  `GUTextureList` vector. The heap buffer is `free`d immediately after the D3DX call. The device
  pointer is read from the global Direct3D 9 device holder.
- **Loose (not mounted):** the loader forwards the bare path to `D3DXCreateTextureFromFileExA`.
- **Codec:** 100% D3DX9 — the client carries no custom image codec for this path. Confirms
  `specs/resource_pipeline.md §3A.3` / `§3B.5`. The renderer is Direct3D 9; D3D8 is not involved.

## 8.2 Skin family (`SkinStub_OpenAndParseSkn` → `CoreSkin_LoadFromFile`) — `.skn` — (CODE-CONFIRMED)

1. Construct a `Diamond::DiskFile` stream object.
2. `DiskFile_OpenByName(diskFile, path, mode = 1)` (read; slurp branch when mounted).
3. On success: `operator new(0x50)` → `CoreSkin` constructor → `CoreSkin_LoadFromFile(coreSkin,
   diskFile)`, which reads `.skn` fields straight off the stream via plain u32/float/bulk read
   methods — no inflate, no LZ.
4. Insert `(id, CoreSkin*)` into the `CoreSkinManager` integer-keyed map.
5. `DiskFile_Close`; `Diamond::DiskFile` destructor.

The `.bnd` bind-pose path mirrors this via `BindStub_OpenAndParseBnd` into the bind-pose pool.
Full `.skn` field layout is owned by `formats/skn.md` and `formats/mesh.md`; the skinning chain is
`specs/skinning.md`.

## 8.3 Generic slurp-to-buffer (`Resource_SlurpFileToObjectBuffer`) — config/script tables — (CODE-CONFIRMED)

Used by table loaders that need the whole file in a fresh heap buffer:

1. `Diamond::DiskFile` constructor; form a `std::string` from the name.
2. `DiskFile_OpenByValue(diskFile, str, mode = 1)`.
3. `DiskFile_GetSize` → `operator new(size)` + `memset 0`.
4. `DiskFile_ReadVirtual(diskFile, buf, size)`.
5. `DiskFile_Close`.

The helper stores the result size at caller-object offset `+0x108` (decimal 264) and the buffer
pointer at `+0x10C` (decimal 268). This is the "load entire asset into a fresh heap buffer" helper
used by many table loaders.

## 8.4 Per-extension consumer fan-out — (CODE-CONFIRMED)

The I/O layer is **format-agnostic**: every consumer formats its own path and selects its own open
mode. The full fan-out recovered from the three entry-point callers:

### Via `Vfs_FindAndReadEntry` (slurp chokepoint, direct)

| Loader | Asset / decoder | Open mode |
|---|---|---|
| `Texture_LoadFromVfsOrDisk` | DDS/TGA/PNG → `D3DXCreateTextureFromFileInMemoryEx` | slurp |
| `Surface_LoadFromVfsOrDisk` | image surface (D3DX in-memory) | slurp |
| `Icon_LoadFileVFSorDisk` | UI icon texture | slurp |
| `HUD_DrawFPSCounter` | HUD glyph/number texture | slurp |
| `Map_LoadCellDescriptor` | `.map` terrain cell descriptor | slurp |
| `Diamond::GSoundOGG` (two vtable paths) | `.ogg` sound stream | slurp |

### Via `DiskFile_OpenByName`

| Loader | Asset | Open mode |
|---|---|---|
| `SkinStub_OpenAndParseSkn` | `.skn` skinned mesh | 1 (binary) |
| `BindStub_OpenAndParseBnd` | `.bnd` bind-pose skeleton | 1 (binary) |
| char-asset loader | character binary asset | 1 (binary) |
| `AreaInventory_LoadLst` | `.lst` area inventory index | 1 (binary) |
| `TerrainPool_InitFromBgtextureLst` | `bgtexture.lst` binary index | 1 (binary) |
| `SkyBox_LoadFromFile` | sky-box surface | 1 (binary) |
| `SoundTable_LoadFiveTables` | `.wlk`/`.run`/`.bgm`/`.bge`/`.eff` 256×48-byte binary blobs | 1 (binary, register-carried) |
| `SoundTable_LoadArea` | per-area sound tables | 1 (binary, register-carried) |
| `Map_LoadAreaBinaries` | area binary sub-files | 1 (binary) |
| `Diamond_TextScript_LoadFromVfs` | CP949 text definition/script (line-by-line) | **9 (text)** |

### Via `DiskFile_OpenByValue`

| Loader | Asset | Open mode |
|---|---|---|
| `Map_ParseDescriptor` (VFS variant) | `.map` descriptor referencing 12 sub-files | per-call |
| `Map_ParseDescriptor` (loose-disk variant) | `.map` descriptor (loose variant, 12 sub-files) | per-call |
| `Resource_SlurpFileToObjectBuffer` | config/script whole-file slurp | 1 (binary) |
| `.xeff` / `.mot` loaders (via wrapper) | effect / motion clips | pass-through |

**Text-vs-binary open-mode correlation:** loaders that parse CP949 text tables line-by-line open
with mode 9 (bit 3 set; CR/LF → LF normalisation) — confirmed for `Diamond_TextScript_LoadFromVfs`
(the generic `.txt`/`.scr` definition reader: opens mode 9, reads via `DiskFile_ReadLineToStdString`,
trims, drops `$`/`;`/blank comment lines, accumulates into a heap buffer, lowercasing each token).
Binary asset loaders open with mode 1. Mode 9 is the client's text-mode-fopen equivalent for CP949
tables; mode 1 is the raw binary reader. They are not interchangeable at read time (see §4.2).

---

# 9. TOC runtime offsets — read-path view — (CODE-CONFIRMED)

The TOC stride is **144 bytes per entry**. The offsets the read path actually consumes:

| Offset | Size | Field | Evidence |
|---:|---:|---|---|
| `+0x00` | 100 bytes | `name` — lowercased, NUL-padded ASCII path | Binary-search `strcmp` at record base; the lookup zeroes a 100-byte key buffer |
| `+0x64` | 4 bytes | `pad_100` — alignment padding (4 bytes) | Not read by any of the 4 runtime `g_VfsToc` consumers |
| `+0x68` | 8 bytes | `dataOffset` — i64 LE absolute offset into `data.vfs` | Passed as a `LARGE_INTEGER` to `SetFilePointerEx` (slurp path, full u64); low dword only to `SetFilePointer` (raw-seek path); progress tracking reads this qword |
| `+0x70` | 8 bytes | `dataSize` — i64 LE byte count | Passed to `malloc` and `ReadFile`; success gate checks `dataSize.high == 0` |
| `+0x78` | 8 bytes | `creation_time` — FILETIME | **Statically proven never read** by the runtime |
| `+0x80` | 8 bytes | `last_access_time` — FILETIME | **Statically proven never read** by the runtime |
| `+0x88` | 8 bytes | `last_write_time` — FILETIME | **Statically proven never read** by the runtime |

**Struct-size validation:** 100 (name) + 4 (pad_100) + 8 (dataOffset) + 8 (dataSize) + 8 (creation_time) + 8 (last_access_time) + 8 (last_write_time) = **144 bytes (0x90)** — field sum byte-exact, zero residual gap; matches the `144 × entry_count` heap allocation stride.

The `g_VfsToc` global is touched by exactly **4 functions** (`Vfs_Mount` write, `VFS_Teardown_Unmount`
read+clear, `Vfs_FindEntry` read, `Vfs_FindAndReadEntry` read). None access offsets `+0x64` through
`+0x8F` — the "trailing bytes never read by the runtime" claim is now **statically proven**, not
merely inferred. Field meanings on disk (magic, padding, FILETIMEs) are authoritative in
`formats/pak.md`; this table records only the runtime consumption view.

---

# 10. `Diamond::DiskFile` — the three-backend stream object — (CODE-CONFIRMED)

`Diamond::DiskFile` is the uniform stream abstraction filled by the open router and consumed by
family decoders. Its per-field layout after a successful open:

| Offset | Field | Set by / used by |
|---:|---|---|
| `+0x00` | vtable pointer (`Diamond::DiskFile` vtable) | constructor |
| `+0x04` | mode flags (bits 0/1/2/3) | open router (stored at open; tested at read and close) |
| `+0x08`..`+0x27` | path (`std::string`, MSVC SSO layout) | open router |
| `+0x24` | CR/LF pending byte (within string SSO region; text-mode carry state) | `DiskFile_GetCharTranslated` (bit-3 text path only) |
| `+0x28` | slurp buffer `ptr` | slurp branch (`Vfs_ReadEntryData` output) |
| `+0x2C` | slurp descriptor `status/unused` | slurp branch |
| `+0x30` | slurp size low | slurp branch |
| `+0x34` | slurp size high | slurp branch |
| `+0x38` | OS handle (loose file or raw-seek private handle); `−1` sentinel when unused | constructor / loose branch / raw-seek branch |
| `+0x40` / `+0x44` | read cursor (u64 low / high) | `DiskFile_ReadBytes` (vtable slot 1) |
| `+0x48` / `+0x4C` | logical size (u64 low / high) | all open branches; `DiskFile_GetSize` |
| `+0x50` / `+0x54` | raw-seek `dataOffset` (u64 low / high) | raw-seek branch: TOC entry base offset within `data.vfs` |

`DiskFile_GetSize`, `DiskFile_ReadVirtual`, and `DiskFile_Close` dispatch on the mode flags at
`+0x04`, selecting among the three backends:

- **`DiskFile_GetSize`:** if not mounted or bit 2 set → return `size@+0x48`; if mounted and bit 2
  clear (slurp) → return `slurpSize@+0x30`.
- **`DiskFile_ReadVirtual`:** calls vtable slot 9 (validity check); then if `mode & 8` (bit 3 set) →
  `DiskFile_ReadBytesClampedToEof` (text, char-by-char CR/LF normalisation); else → vtable slot 1
  `DiskFile_ReadBytes` (binary block: slurp = `memcpy` from `blobPtr@+0x28`; raw-seek = `ReadFile`
  on `handle@+0x38` with cursor-clamp, no per-call re-seek; loose = `ReadFile` on `handle@+0x38`).
- **`DiskFile_Close`:** if mounted and bit 2 set → close private `handle@+0x38`; if mounted and bit
  2 clear → `free` slurp buffer at `+0x28` and zero descriptor; if not mounted → close
  `handle@+0x38`. Always resets handle to `−1` sentinel and zeroes cursor.

This matches and deepens `specs/resource_pipeline.md §1.5.6`.

### `Diamond::DiskFile` vtable — role map (by slot index, CODE-CONFIRMED)

| Slot index | Role |
|---|---|
| 1 | `DiskFile_ReadBytes` — three-branch binary block read (slurp `memcpy` / raw-seek `ReadFile` / loose `ReadFile`) |
| 3 | `DiskFile_GetChar` — raw single-byte read |
| 5 | `DiskFile_Close` — three-backend close / cursor reset |
| 8 | EOF / compare-order-key predicate |
| 9 | `DiskFile_IsGood` — pre-read validity check |
| 10 | `DiskFile_GetSize` — size dispatch (slurp vs raw/loose) |
| 11 | `DiskFile_GetPosition` — cursor accessor |
| 0, 2, 4, 6, 7, 12 | constructor-helper / seek / write / destructor family; not traced this pass |

---

# 11. Cache / refcount interplay — (CODE-CONFIRMED)

**At the open layer there is no cache.** Two opens of the same path perform two independent binary
searches and two independent `malloc` + `ReadFile` calls — `Vfs_FindAndReadEntry` and
`Vfs_ReadEntryData` contain no global file map or previously-seen-path guard.

All caching and deduplication lives one layer up in the per-subsystem managers:

| Manager | Cache key | Lifetime |
|---|---|---|
| `CoreSkinManager` | Skin ID (integer-keyed ordered map) | Scene / session; grow-only |
| `CoreMotManager` | Motion ID or path; populated at boot via list-text registries | Session; grow-only |
| Bind-pose pool | `IdB` integer | Session; grow-only |
| `GHTexManager` | Texture name (string-keyed sorted array) | Scene |
| `TerrainPool` | Background-texture ID | Scene; two-level ref-counted with unload-on-last-release (see `specs/resource_pipeline.md §3B`) |

The **progress accumulator** is layered onto both `Vfs_FindEntry` (metadata lookup), the
find-and-read chokepoint, and the loose-file path via `Vfs_AccumulateLoadProgress`. It is not a
separate pass over the file set.

---

# 12. Open questions and debugger-pending items

| Item | Status | Notes |
|---|---|---|
| **Raw-seek consumer census** — does any consumer pass mode bit 2 during a real area+character load? | **[DEBUGGER-CONFIRM]** (tightly bounded) | Open AND read paths are CODE-CONFIRMED (§4.3). All statically observed openers use mode 1 or 9 (bit 2 clear); the path appears dead on the shipped asset path. A live open-mode census (breakpoint `DiskFile_OpenByName` / `DiskFile_OpenByValue`, observe `mode & 4`) is required to confirm zero usage. `specs/resource_pipeline.md §8 item 15` tracks this. |
| **`field_08` (value 39) — meaning** | Permanently bounded — not a debugger item | Read-and-discarded by `Vfs_Mount`; value sample-confirmed (= 39). Role permanently bounded as a pack-tool artifact: no consumer anywhere in the binary extracts, stores, or compares this scalar, so no runtime census can recover its meaning. Only an external pack-tool source could settle it (sub-version / build-region / section tag). |
| **TOC trailing bytes `+0x78`..`+0x8F`** — field names | Closed (statically proven never-read) | The three trailing FILETIMEs (`creation_time`, `last_access_time`, `last_write_time`) are present on disk (see `formats/pak.md`) and are statically proven never read by the runtime (only 4 functions consume `g_VfsToc`; none access `+0x64`..`+0x8F`). See §9. |
| **`Diamond::CVFSManager` virtual methods** | Closed (scope guard only) | The RAII constructor/destructor stamps a vtable on a stack temporary in the slurp branch; no load-bearing virtual behaviour was observed. The process-global `g_VfsReadLock` inside `Vfs_ReadEntryData` is the actual concurrency primitive. |
| **`DiskFile_ReadVirtual` backend dispatch** | Closed (CODE-CONFIRMED) | `DiskFile_ReadVirtual` calls vtable slot 9 (validity check) then dispatches on bit 3: if set → `DiskFile_ReadBytesClampedToEof` (text, char-by-char CR/LF normalisation); else → vtable slot 1 `DiskFile_ReadBytes` (three-branch binary block). See §4.2 and §10. |
| **D3DX width/height/MipLevels/format sentinel values for terrain GHTex** | **[DEBUGGER-CONFIRM]** | Exact arguments come from a shared sentinel block at runtime. Owned by `specs/resource_pipeline.md §8 item 15` and `formats/texture.md`; out of scope for this spec. |

---

# 13. Cross-references

- `specs/vfs_overview.md §0.1` — runtime behaviour summary (mount/lookup/read/open-router); this
  spec is the instruction-level deepening of it.
- `specs/resource_pipeline.md §1`, `§1.5.1`–`§1.5.9` — chokepoint, three-backend seek, progress
  tracking, cache architecture (the base that this spec deepens).
- `specs/resource_pipeline.md §3`, `§3B` — subsystem caches, GHTex/TerrainPool two-level
  reference counting.
- `specs/resource_pipeline.md §2.1a` — boot data-table corpus (the global pointer table of literal
  paths distinct from 3D-asset on-demand path formatting).
- `formats/pak.md` — authoritative container and 144-byte TOC byte layout.
- `formats/skn.md`, `formats/mesh.md`, `specs/skinning.md` — skin/bind-pose field layouts and
  deform chain.
- `formats/texture.md` — texture on-disk formats; `D3DXCreateTextureFromFileInMemoryEx` parameter
  guidance.
- `formats/bgtexture_lst.md`, `formats/terrain.md`, `formats/terrain_scene.md` — terrain asset
  chains.
- `formats/sound_tables.md` — sound asset family.
- `specs/asset_pipeline.md`, `specs/asset_linkages.md` — loader-selection verdict and linkage
  chains.

---

# 14. Offline compile pipeline — `.pre` / `.post` tool artifacts — (STATIC-HYPOTHESIS, unverified at f61f66a9)

The offline map-editor toolchain uses temporary draft and delta files during the compilation of
final VFS packs. These files are not referenced by the client at runtime; the client engine
contains no `.pre` or `.post` path literals.

- **Heightfield modification (`.ted.post` → `.ted`):** The editor writes a complete terrain mesh
  backup to `*.ted.post`, then patches baked diffuse colour values at byte offset `30 087` within
  the live `.ted` file, preserving the raw height data.
- **Collision compilation (`.sod.pre` → `.sod`):** Collision polygon outlines are compiled by
  calculating axis-aligned bounding-box bounds (`SolidRecord` elements) and expanding polygon
  edges into bidirectional edge quads (`QuadRecord` elements) to produce the runtime `.sod` file.
- **Scene mesh optimisation (`.bud.pre` → `.bud`):** The editor welds vertices, optimises vertex
  lists for GPU cache coherence, updates vertex tag flags at stride offset `+12`, and serialises
  the optimised runtime `.bud` binary.

`.ted` field layout is owned by `formats/terrain.md`. `.sod` and `.bud` runtime layouts are
owned by their respective format specs. This section describes tool-side behavior; it is not
IDA-verifiable against the runtime client and is carried here as a static hypothesis pending
a tool-binary RE pass.
