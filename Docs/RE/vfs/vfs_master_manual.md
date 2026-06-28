# VFS Master Manual — Container Formats, Runtime Lifecycle, and Asset Linkages

This document is the consolidated specification and engineering guide for the legacy *Martial Heroes* client's Virtual File System (VFS). It describes the on-disk storage layout of `data.inf` and `data/data.vfs`, the C++ runtime lifecycle and abstractions (`CVFSManager` and `DiskFile`), the file read progress tracking, and the complete linkage map of sub-assets consumed by the game loop.

---

## 1. On-Disk Archive Container Layout

The VFS archive is split into a two-file index and data pair: `data.inf` (Index / Table of Contents) and `data/data.vfs` (Opaque payload data blob). Both files are uncompressed and unencrypted, using a flat tiling model.

### 1.1 Container Header (24 Bytes)
Both `data.inf` and `data.vfs` lead with an identical 24-byte header.

| Offset | Size (Bytes) | Type | Field Name | Description |
|:---|:---|:---|:---|:---|
| `+0x00` | 8 | `char[8]` | `magic` | Null-padded ASCII signature: `"VFS001\0\0"`. (Ignored/Not validated at runtime). |
| `+0x08` | 4 | `u32` LE | `build_version` | Opaque build/version tag. Value is `39` (`0x27`) in reference archives. |
| `+0x0C` | 4 | `u32` LE | `entry_count` | Total number of entries in the TOC. Drives heap allocation. Reference archive = `43,347`. |
| `+0x10` | 8 | `u64` LE | `total_blob_size` | Total length of `data/data.vfs` in bytes. |

### 1.2 Table of Contents Array (TOC) — 144 Bytes Stride
Immediately following the 24-byte header in `data.inf` is a contiguous array of `entry_count` records. Each record is exactly **144 bytes (0x90)**.

| Offset | Size (Bytes) | Type | Field Name | Description |
|:---|:---|:---|:---|:---|
| `+0x00` | 100 | `char[100]` | `name` | Null-terminated ASCII virtual path, lowercase-normalized at compile-time. |
| `+0x64` | 4 | `u32` | `_pad` | Alignment padding; never read. |
| `+0x68` | 8 | `i64` LE | `dataOffset` | Absolute byte offset of the payload inside `data/data.vfs`. First payload offset is `24`. |
| `+0x70` | 8 | `i64` LE | `dataSize` | Byte size of the payload. The client only reads the low 32 bits (`LowPart`). |
| `+0x78` | 8 | `u64` LE | `creation_time` | NTFS `FILETIME` creation timestamp. (Ignored at runtime). |
| `+0x80` | 8 | `u64` LE | `last_access_time`| NTFS `FILETIME` last access timestamp. (Ignored at runtime). |
| `+0x88` | 8 | `u64` LE | `last_write_time` | NTFS `FILETIME` last write timestamp. (Ignored at runtime). |

---

## 2. In-Memory Subsystem Struct Maps

### 2.1 Diamond::CVFSManager (Size: 4 Bytes)
The `CVFSManager` is a stateless class whose only member is its virtual table pointer. All VFS states (file handles, TOC pointer, read lock) are stored as global/BSS variables in the binary.

```cpp
struct Diamond::CVFSManager {
  void* vftable;   // +0x00 (0x72ffb8) -> Points to deleting destructor: unknown_libname_470 @ 0x60ab94
};
```

### 2.2 Diamond::DiskFile (Size: 88 Bytes / 0x58)
The `DiskFile` object is the logical stream handle used by all asset parsers. It encapsulates loose file, memory-slurp, and raw-seek streaming modes.

```cpp
struct Diamond::DiskFile {
  void* vtable;               // +0x00 (0)   vtable at 0x730e20
  uint32_t mode_bits;         // +0x04 (4)   Open mode bitmask (e.g. bit 2 controls raw-seek stream)
  std::string file_path;      // +0x08 (8)   MSVC STL std::string: 16B buf + 4B size + 4B capacity = 24B
  void* slurp_buffer;         // +0x28 (40)  Pointer to malloc'd payload (Slurp Mode Branch 2)
  uint64_t slurp_size;        // +0x30 (48)  Total byte size of the slurp buffer
  HANDLE file_handle;         // +0x38 (56)  OS File HANDLE (loose file or data.vfs private handle)
  uint64_t read_cursor;       // +0x40 (64)  Current byte offset position inside the file
  uint64_t file_limit;        // +0x48 (72)  Logical size limit of the sub-asset payload
  uint64_t base_offset;       // +0x50 (80)  Absolute starting payload offset inside data/data.vfs
};
```

---

## 3. VFS Runtime Manager Lifecycle & Primitives

All functions operate on a set of process-global VFS variables:
- `g_VfsMounted` (`0x8C4EC9`): 1 = Packed (TOC-mount), 0 = Loose (developer mode).
- `g_VfsTocCount` (`0x8C4ED0`): total number of entries in `g_VfsToc`.
- `g_VfsToc` (`0x8C4ECC`): pointer to the heap-allocated TOC array.
- `g_VfsDataHandle` (`0x797888`): open OS HANDLE to `data/data.vfs`.
- `g_VfsReadLock` (`0x797890`): `CRITICAL_SECTION` guarding `g_VfsDataHandle`.

### 3.1 Vfs_Mount @ `0x60A912`
Unconditionally called at startup to load the TOC index.
1. Opens `data.inf` with `CreateFileA` flags `GENERIC_READ | FILE_SHARE_READ | OPEN_EXISTING` and `FILE_FLAG_RANDOM_ACCESS | FILE_ATTRIBUTE_READONLY` (`0x10000001`).
2. Reads the 24-byte header into a stack buffer. Extracts `entry_count` from offset `12`.
3. Allocates `144 * entry_count` bytes of heap memory using `operator new`.
4. Performs a single bulk `ReadFile` of the entire index into `g_VfsToc`.
5. Closes the `data.inf` file handle.
6. Opens `data/data.vfs` using the same flags and caches the handle in `g_VfsDataHandle`.

### 3.2 Vfs_FindEntry @ `0x60AA6B`
Resolves a requested virtual path:
1. Copies path to a stack buffer of 100 bytes, converting it to lowercase via `_strlwr`.
2. Conducts a binary search over `g_VfsToc` (stride 144) using `strcmp` against the `name` field.
3. If found, returns the pointer to the TOC entry `(g_VfsToc + 144 * v3)`. If not, returns `0`.

### 3.3 Vfs_ReadEntryData @ `0x60ABB1`
Slurps raw entry bytes from the shared handle:
1. Frees any existing memory and allocates `dataSize.LowPart` bytes via `malloc`.
2. Enters `g_VfsReadLock` critical section.
3. Seeks to `dataOffset` in `g_VfsDataHandle` using `SetFilePointerEx`.
4. Reads the payload bytes into the allocated buffer.
5. Leaves `g_VfsReadLock`.
6. Validates that the bytes read match the expected size and that `dataSize.HighPart` is `0`. On failure, frees memory and returns `0`.

### 3.4 Vfs_FindAndReadEntry @ `0x60AC70`
Combines `Vfs_FindEntry` and `Vfs_ReadEntryData` into a single wrapper, storing the pointer and size inside a 16-byte descriptor (`{ void* buffer, uint32_t pad, uint64_t size }`).

---

## 4. The Three-Branch I/O Routing

`DiskFile_ReadBytes` (`0x60900d`) dispatches read requests based on the mount state and the descriptor's `mode_bits`:

```
DiskFile_ReadBytes
  │
  ├──► [g_VfsMounted == 0] ───────────────► Branch 1: Loose Developer Mode
  │                                         Direct ReadFile from OS file handle
  │
  └──► [g_VfsMounted == 1]
        │
        ├──► [Mode Bit 2 == 0] ───────────► Branch 2: In-Memory Slurp
        │                                   Copies bytes using memcpy from slurp_buffer
        │
        └──► [Mode Bit 2 == 1] ───────────► Branch 3: Raw-Seek Streaming
                                            ReadFile from private data.vfs handle at
                                            (base_offset + read_cursor)
```

- **Branch 1 (Loose-File Fallback):** Used in loose developer mode (`vfsmode` is false in `game.lua`). The client uses an individual OS handle directly on the relative path to read bytes.
- **Branch 2 (In-Memory Slurp):** Standard path for most client asset loaders. The entire payload is loaded into memory during `Open` and subsequent reads are handled via `memcpy`.
- **Branch 3 (Raw-Seek Streaming):** Used for large or streamed assets (e.g. `.ogg` music). The client calls `CreateFileA` to obtain a private handle to `data/data.vfs` specifically for this file. It bypasses the global critical section `g_VfsReadLock` and seeks relative to `base_offset`.

---

## 5. Load-Progress Accumulator Math

To drive the loading screen's progress bar:
1. Every successful VFS file lookup/read increments `g_VfsProgressBytes` by the sub-asset size.
2. A normalized progress value is calculated using integer division:
   $$\text{normalized} = \frac{\text{g\_VfsProgressBytes}}{\text{g\_VfsProgressDenominator}}$$
   where `g_VfsProgressDenominator` is fixed at `9,395,240` bytes (`0x8F5C28` bytes).
3. The UI loading bar width (in pixels) is rendered as:
   $$\text{pixel\_width} = \min\left(223 \times \frac{\text{normalized}}{100},\, 223\right)$$
4. The progress bar completes early when the count of read bytes exceeds the 9.4 MB denominator; the final transition is gated by the boot worker thread completing.

---

## 6. Sub-Asset Format Census & Parser Directory

All files in the VFS are routed to specific parsing functions in `doida.exe`. Below is the directory mapping major formats to their signatures, structures, parser functions, and memory models.

### 6.1 Terrain & World Map Formats

#### 6.1.1 `.map` (Cell Master Descriptor)
- **Content:** Text-based list placing textures, building instances, boundaries, and environmental effect groups.
- **Parser Function:** `Map_ParseDescriptor` (`0x43f626`).
- **Memory Mapping:** Populates the `TerrainSlot` grids and the scene graph lists.

#### 6.1.2 `.ted` (Heightfield Grid)
- **Signature:** Fixed `46,987` bytes.
- **Structure:** 128×128 tile vertex heights (f32) + tile texture layer index bytes.
- **Parser Function:** `Ted_LoadGeometryBlob` (`0x44ade2`) and `Ted_BuildCellGroundGrid` (`0x4412e0`).
- **Memory Mapping:** Allocated into a vertex buffer mesh managed by the terrain renderer.

#### 6.1.3 `.bud` (Mesh Geometry)
- **Content:** Static meshes for cell buildings.
- **Structure:** `[u32 objectCount]` + per-object `[type_byte, tex_id, vertex_count, VF_32 vertex block, index_count, u16 index block]`.
- **Parser Function:** `Bud_LoadBuildingBlob` (`0x44E23F`) and `BudObject_ComputeAABBAndBudget` (`0x44BF30`).
- **Memory Mapping:** Allocated as `BudObject` records (stride 116 bytes).

#### 6.1.4 `.sod` (2D Collision Walls)
- **Content:** 2D collision outline vectors.
- **Structure:** `[u32 solidCount]` + per-solid `SolidRecord` (108 bytes, includes AABB) + per-solid `QuadRecord` (48 bytes, XZ quad points).
- **Parser Function:** `Sod_LoadCollisionBlob` (`0x458f13`).
- **Memory Mapping:** Parsed into collision arrays and binned into the `TerrainSlot1_ResetMassObjectGrid` or `Sod_BuildSolidQuadtree` (`0x65a6d5`).

#### 6.1.5 `.fx1` to `.fx7` (Terrain Overlay Layers)
- **Content:** Effect/decoration overlay meshes.
- **Structure:** Identical to `.fx6`/`.fx7` (`[u32 groupCount] + 48B header + VF_32 vertices + u16 indices`).
- **Parser Function:** `Fx6_DecodeGroups` (`0x466616`) / `Fx7_DecodeGroups` (`0x468596`).
- **Memory Mapping:** Binned into 16x16 tile grids (`TerrainSlot8_ResetFx7Grid @ 0x468cd8`).

#### 6.1.6 `.mud` (Water Height Map)
- **Signature:** Fixed `32,768` bytes.
- **Parser Function:** `Mud_ReadBlob` (`0x456c84`).
- **Memory Mapping:** Slurped into a raw height block or defaults to BSS zero block (`0x836228`).

### 6.2 Character & Animation Formats

#### 6.2.1 `.skn` (Skinned Mesh)
- **Content:** Skinned and weighted model vertices.
- **Parser Function:** `SkinStub_OpenAndParseSkn` (`0x434eaa`).
- **Memory Mapping:** Instantiated into the `ItemStaticSkinRegistry` or actor visual mesh nodes.

#### 6.2.2 `.bnd` (Skeletal Joint Bind Pose)
- **Content:** Base skeleton structure.
- **Structure:** 36-byte joint records (quat/translation transforms).
- **Parser Function:** `BindPose_ParseBndFile` (`0x43009c`) and `BindStub_OpenAndParseBnd` (`0x430a55`).
- **Memory Mapping:** Handled by `CharPosePool_RegisterFromBndPath` (`0x42e7da`) to drive skeletal joints.

#### 6.2.3 `.mot` (Skeletal Motion Clip)
- **Content:** Skeletal animations at 10 fps.
- **Structure:** 28-byte keyframe tracks (f32[3] translation + f32[4] quaternion).
- **Parser Function:** `CoreMot_LoadHeader` (`0x42f2ca`) and `CoreMot_LoadFullData` (`0x42f839`).
- **Memory Mapping:** Stored in the `MotionClip` registry (`MotClip_RegisterByPath @ 0x42e56d`).

### 6.3 Meshes & Particle Effects

#### 6.3.1 `.xobj` (Mesh Particles)
- **Content:** ASCII white-space tokenized static meshes.
- **Parser Function:** `XObj_LoadFromFile` (`0x4a8353`).
- **Memory Mapping:** Parsed into a static vertex/index buffer for visual particles.

#### 6.3.2 `.eff` / `.xeff` (Particle Effect Descriptors)
- **Content:** 3D shapes and particle emitter behaviors.
- **Parser Function:** `CoreXEffect_LazyParseXeff` (`0x4a5441`).
- **Memory Mapping:** Managed by the `XEffectManager` to script spells, flows, and glows.

### 6.4 Config & Localized Tables

#### 6.4.1 `.scr` (Binary Configuration Containers)
- **Content:** Databases for items, quests, NPCs, and stat curves.
- **Structure:** Count * fixed-stride record layout.
- **Parser Function:** `ItemsScr_LoadRecord` (`0x4712d7`), `MobsScr_LoadFile` (`0x47adce`).
- **Memory Mapping:** Loaded into static registries at startup via `Boot_LoadDataTableCorpus` (`0x5369b7`).

#### 6.4.2 `.xdb` (Localized XML Catalogs)
- **Content:** UI strings and localization database.
- **Parser Function:** Loaded via binary catalog loaders. `msg.xdb` maps hash IDs directly to CP949 string entries.

#### 6.4.3 `.do` (UI & Action coordinate layouts)
- **Content:** Hardcoded coordinate grids and action values.
- **Parser Function:** `StanceDoTable_ParseRecord` (`0x484a2d`).

---

## 7. Cross-Asset Linkage Layer (The Manifests)

Rather than hardcoding VFS offsets, the client loads ASCII text manifest files from the VFS to coordinate asset routing dynamically:

```
[bgtexture.lst] ──────► Reads .dds textures
  Maps 8-bit terrain cell tile bytes to resolved path names.

[skin.txt] ───────────► Reads .skn meshes & .png skins
  Maps class and armor slot ID to skinned character parts.

[actormotion.txt] ────► Resolves base skin to spawners.

[motlist.txt] ────────► Reads .mot animation clips
  Maps motion triggers (idle, walk, slash, cast) to .mot files.

[xeffect.lst] ────────► Reads .xeff descriptors
  Maps spell/combat effect indexes to emitter descriptor files.

[bmplist.lst] ────────► Reads .tga textures
  Maps particle visual frame IDs to Targa textures.

[uitex.txt] ──────────► Reads .dds UI atlases
  Maps 4-digit UI texture widgets to resolved .dds atlas sheets.
```
