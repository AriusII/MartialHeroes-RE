# Structure: GHandle / GHTex / GTextureManager (Resource caching system)

> Clean-room struct layout specification. Derived-truth from static analysis of the client binary.
>
> **Verification:** sample-verified (VTables, offsets, and cache registration logic verified against the live database).
> ida_reverified: 2026-06-27 · ida_anchor: f61f66a9ae0ec1e946105b2ecff76e8930cb1d1367df64e5688a5266f5ad9963
> subsystems: [resource_handles, texture_caching, direct3d_integration]
> C# implementation: `MartialHeroes.Assets.Vfs`

---

## 1. Class Structure Mappings

### 1.1 `Diamond::GHandle` (Base Class)
- **Size:** 48 bytes (`0x30`)
- **Purpose:** Abstract base class managing resource file paths, VRAM footprints, and loading flags.

| Offset | Size (Bytes) | Type | Field Name | Description |
|:---:|:---:|:---|:---|:---|
| `+0x00` | 4 | `void*` | `vptr` | Virtual table pointer for `GHandle`. |
| `+0x04` | 28 | `std::string` | `m_strPath` | File path name of the resource (VFS virtual path). |
| `+0x20` | 1 | `bool` | `m_bActive` | Status flag. Initialized to `1`. |
| `+0x21` | 1 | `bool` | `m_bLoaded` | Set to `1` when the asset is loaded in VRAM; otherwise `0`. |
| `+0x22` | 1 | `bool` | `m_bUnknown` | Initialized to `0`. |
| `+0x24` | 4 | `uint32_t` | `m_dwTimeout` | Timeout threshold in milliseconds. Initialized to `180,000` (3 minutes). |
| `+0x28` | 4 | `uint32_t` | `m_dwLastAccessTime` | System tick count (`GetTickCount`) of the last access. |
| `+0x2C` | 4 | `uint32_t` | `m_dwVramSize` | Size of the asset in VRAM. |

### 1.2 `Diamond::GHTex` (Inherits from `Diamond::GHandle`)
- **Size:** 76 bytes (`0x4C`)
- **Purpose:** Concrete texture resource wrapper containing the Direct3D texture interface and formatting options.

| Offset | Size (Bytes) | Type | Field Name | Description |
|:---:|:---:|:---|:---|:---|
| `+0x00` | 48 | `—` | `GHandle` | Base class payload. |
| `+0x34` | 4 | `int32_t` | `m_nSchedulerIndex` | Index of the scheduler slot, set to `-1` if inactive. |
| `+0x38` | 4 | `int32_t` | `m_nUnknown` | Initialized to `-1`. |
| `+0x3C` | 4 | `D3DFORMAT` | `m_dwFormat` | Direct3D texture format (e.g. `D3DFMT_A8R8G8B8`). |
| `+0x40` | 4 | `LPDIRECT3DTEXTURE9`| `m_pD3DTexture` | Pointer to the active Direct3D texture interface. NULL if unloaded. |
| `+0x44` | 4 | `uint32_t` | `m_dwTextureVramSize` | Specific VRAM allocation footprint in bytes. |
| `+0x48` | 4 | `D3DXOptions*` | `m_pD3DXOptions` | Pointer to texture loading configuration structure. |

### 1.3 `Diamond::GTexture` (Cache Wrapper)
- **Size:** 60 bytes (`0x3C`)
- **Purpose:** Reference-counted wrapper object placed inside the cache mapping tree.

| Offset | Size (Bytes) | Type | Field Name | Description |
|:---:|:---:|:---|:---|:---|
| `+0x00` | 4 | `void*` | `vptr` | Virtual table pointer for `GTexture`. |
| `+0x04` | 4 | `uint32_t` | `m_dwRefCount` | Reference counter. |
| `+0x34` | 4 | `Diamond::GHTex*` | `m_pGHTex` | Pointer to the underlying resource handle. |
| `+0x38` | 4 | `void*` | `m_pKey` | Identifier key used for cache lookup (typically the pointer to the path). |

### 1.4 `Diamond::GTextureManager`
- **Size:** 16 bytes (`0x10`)
- **Purpose:** Global singleton cache manager storing active textures.

| Offset | Size (Bytes) | Type | Field Name | Description |
|:---:|:---:|:---|:---|:---|
| `+0x00` | 4 | `void*` | `vptr` | Virtual table pointer for `GTextureManager`. |
| `+0x04` | 12 | `std::map<void*, GTexture*>` | `m_mapTextures` | Red-black tree caching active textures by lookup keys. |

---

## 2. Virtual Table Definitions

### 2.1 `GHandle` Virtual Table
- **`Slot 0 (+0x00)`**: `Diamond_GHandle_deleting_dtor`
- **`Slot 1 (+0x04)`**: pure-virtual `Load`
- **`Slot 2 (+0x08)`**: pure-virtual `LoadWrapper`
- **`Slot 3 (+0x0c)`**: pure-virtual `Unload`

### 2.2 `GHTex` Virtual Table
- **`Slot 0 (+0x00)`**: `Diamond_GHTex_scalar_deleting_dtor`
- **`Slot 1 (+0x04)`**: `GHTex_Load`
- **`Slot 2 (+0x08)`**: `Diamond_GHTex_LoadWrapper`
- **`Slot 3 (+0x0c)`**: `GHTex_Unload`

---

## 3. Cache & Lifecycle Logic

### A. Load Pipeline (`Diamond_GTextureManager_GetTexture`)
1. Performs lookup in `m_mapTextures` tree.
2. If hit: Returns cached `GTexture*` wrapper.
3. If miss:
   - Allocates 76 bytes on heap for `GHTex`.
   - Constructs `GHTex` and populates path.
   - If loading immediately: Calls `GHTex_Load`.
     - Checks VFS mount status. If mounted, loads via `D3DXCreateTextureFromFileInMemoryEx` from VFS. If not mounted, loads from loose files using `D3DXCreateTextureFromFileExA`.
     - Sets `m_bLoaded = 1` and updates `m_dwLastAccessTime`.
   - Allocates 60 bytes for `GTexture` wrapper, setting reference count to 1 and wrapping `GHTex`.
   - Inserts `GTexture*` into `m_mapTextures`.

### B. Garbage Collection & Eviction
- A periodic scan sweeps `m_mapTextures`.
- If a texture wrapper's reference count is 0 and the elapsed time since `m_dwLastAccessTime` exceeds `m_dwTimeout` (3 minutes), the manager triggers `GHTex_Unload`.
- `GHTex_Unload` calls `Release()` on the D3D texture pointer, frees it, resets `m_bLoaded = 0`, and subtracts its footprint from the global VRAM byte counter (`g_vramBytesAllocated`).

### C. Clean Up / Shutdown
On manager destruction:
- Traverses `m_mapTextures` map.
- Invokes deleting destructors for both the wrapped `GHTex` handles and the `GTexture` wrapper structures.
- Deallocates the tree sentinels.
