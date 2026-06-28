# VFS I/O Subsystem Runtime Mechanics

The legacy client interacts with the file system using a specialized manager class `Diamond::CVFSManager`. The VFS subsystem acts as a format-agnostic virtual transport layer.

---

## 1. Subsystem Life Cycle

### A. Initialization & Mount Mode Selection
Early during startup, the client checks the boot configuration script `game.lua` for the boolean flag `vfsmode`.
- If `vfsmode` is `true`, the client sets a process-global `g_VfsMounted` flag to `1` (Packed Mode).
- If `vfsmode` is `false`, `g_VfsMounted` is `0` (Loose/Developer Mode). If `game.lua` fails to load, the mount-mode local keeps its pre-initialized default of `1` (Packed) and the Lua values are never consulted.

### B. The Mount Process (`Vfs_Mount`)
Regardless of `vfsmode`, the client unconditionally calls `Vfs_Mount`. The routine performs the following sequence:

1. Opens the index file `data.inf` using `CreateFileA` with flags `GENERIC_READ | FILE_SHARE_READ | OPEN_EXISTING` and `FILE_FLAG_RANDOM_ACCESS | FILE_ATTRIBUTE_READONLY` (`0x10000001`).
   - If `data.inf` is missing (common in loose mode), the call fails and the routine early-returns `INVALID_HANDLE_VALUE`.
2. Reads the 24-byte header into a stack-local buffer, extracting the `entry_count` at offset `12`.
3. Allocates `144 * entry_count` bytes of heap memory using `operator new` to hold the Table of Contents array (`g_VfsTocBase`).
4. Performs a single bulk `ReadFile` of the entire `144 * entry_count` TOC bytes from `data.inf` into the allocated array.
5. Closes the `data.inf` handle.
6. Opens the data blob file `data/data.vfs` using the same `CreateFileA` flags, storing the file handle in the process-global `g_VfsDataHandle`. This handle remains open for the entire process lifetime.

---

## 2. Lookup Algorithm (`Vfs_FindEntry`)

When a virtual file path is requested by a game loader (e.g. `data/char/skin.txt`), it is resolved using a binary search:

1. **Path Normalization:** The input path string is copied to a local buffer of 100 bytes, lowercased using `_strlwr` to support case-insensitive searches, and null-padded.
2. **Case-Insensitive Binary Search:** An ascending binary search is performed over the sorted TOC array using `strcmp` on each record's name field.
3. **Hit / Miss:** 
   - If a match is found, the routine returns a pointer to the matching `VfsEntry` record inside the memory array.
   - If not found, it returns `0` (null).

---

## 3. Read Operations (`Vfs_ReadEntryData`)

Once an entry is located, reading its bytes follows a serialized, locked sequence to guarantee thread safety over the shared file handle:

1. **Buffer Allocation:** The client allocates exactly `dataSize.LowPart` bytes of memory from the CRT heap using `malloc`. (Any prior buffer in the descriptor is freed first).
2. **Handle Lock:** The client enters the process-global read lock `g_VfsReadLock` (`CRITICAL_SECTION`).
3. **Seek:** Performs a 64-bit absolute seek to `dataOffset` within `data/data.vfs` using `SetFilePointerEx`.
4. **Read:** Reads `dataSize.LowPart` bytes into the allocated buffer.
5. **Unlock:** Leaves the critical section.
6. **Integrity Guard:** The read succeeds only if the bytes read equal `dataSize.LowPart` and the high part of `dataSize` is zero. Otherwise, it frees the buffer and reports failure.

---

## 4. The Three-Branch DiskFile Reader

Callers requesting a file receive a virtual file handle (represented as a `DiskFile` object). When a read primitive (`DiskFile_ReadBytes`) is executed, it dispatches into one of three execution branches based on `g_VfsMounted` and the open mode bitfield:

### Branch 1: Archive Not Mounted (Loose-File Fallback)
Used when `g_VfsMounted == 0`. Bypasses the VFS entirely.
- Opens a direct OS handle to the relative path on disk.
- Reads bytes directly using standard OS file functions.
- Logical file size and EOF are governed directly by the Windows filesystem.

### Branch 2: Archive Mounted, Mode Bit 2 = 0 (In-Memory Slurp)
The standard path for game assets.
- The entire entry payload is loaded into memory during `Open`.
- Subsequent reads copy bytes using `memcpy` from the pre-buffered data using a running cursor. Clamped by the entry's `dataSize`.

### Branch 3: Archive Mounted, Mode Bit 2 = 1 (Raw-Seek Streaming)
Designed for streaming larger files.
- Opens a **private** handle to `data/data.vfs` for this file instance.
- Seeks to the entry's start offset.
- Sequential reads stream directly from the private handle without calling `SetFilePointerEx` per read.
- Clamps the read cursor at the entry's size limit. Bypasses the global `g_VfsReadLock` since the handle is private.

*Note: In the shipping client, all first-party asset loaders use Mode Bit 2 = 0 (Slurp).*

---

## 5. Load-Progress Accumulator

A cumulative load-progress tracking mechanism is embedded in the lookup and read paths (enabled by the flag `g_VfsProgressTracking`).

- Each VFS lookup/read increments the global 64-bit accumulator `g_VfsProgressBytes` by the file size.
- A normalized progress value is calculated using integer division:
  $$\text{normalized} = \frac{\text{g\_VfsProgressBytes}}{\text{g\_VfsProgressDenominator}}$$
  Where `g_VfsProgressDenominator` is fixed at `9,395,240` bytes (~9.4 MB).
- The loading bar width is computed in UI rendering as:
  $$\text{pixel\_width} = \min\left(223 \times \frac{\text{normalized}}{100},\, 223\right)$$
- Because the total game load-set exceeds this denominator, the bar completes early; loading screen completion is gated by the boot worker thread's completion flag rather than the progress bar hitting `223` pixels.

---

## 6. Integration with GTextureManager Caching Layer

The `GTextureManager` singleton manages memory allocations and texture cache mapping in coordinate with the VFS. When a texture is requested:
1. `GTextureManager::GetTexture` queries its cache map `m_mapTextures` for the asset.
2. On cache miss:
   - Allocates the 76-byte `GHTex` resource.
   - Invokes `GHTex_Load` which dispatches the read request.
   - If `g_VfsMounted == 1`, the load request routes through the VFS `Vfs_FindAndReadEntry` to slurp the raw bytes. The buffer is passed to `D3DXCreateTextureFromFileInMemoryEx` to build the Direct3D texture pointer in VRAM.
   - If `g_VfsMounted == 0`, the load request falls back to loose files, calling `D3DXCreateTextureFromFileExA` directly on the local path.
   - Once loaded, the VRAM footprint is recorded in the handle (`m_dwTextureVramSize` @ `+0x44`) and added to the global foot-print accumulator `g_VramFootprintAccumulator`.

---

## 7. The Offline Patching Pipeline (.pre and .post files)

To compile the final client VFS packs, the offline map editors use temporary outline and backup delta files:

- **Heightfield Modification (`.ted.post` ──► `.ted`):** The editor dumps the complete terrain mesh to a backup `*.ted.post` file, then patches only the baked diffuse color values (baked shadow map) at offset `30,087` on the live client `.ted` file, preserving raw height data.
- **Collision Compilation (`.sod.pre` ──► `.sod`):** The collision outlines (2D polygon vertices) are compiled by calculating AABB bounds (`SolidRecord` elements) and expanding polygon edges into bidirectional edge quads (`QuadRecord` elements) to write the runtime `.sod` file.
- **Scene Mesh Optimization (`.bud.pre` ──► `.bud`):** The editor welds vertices, optimizes vertex lists for GPU cache coherence, updates vertex tag flags (offset `+12` in stride), and serializes the optimized runtime `.bud` binary.

*Note: The client engine references no `.pre` or `.post` path literals; they are completely ignored at runtime.*

