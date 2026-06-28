---
verification: static-confirmed for the upload call routing, parameter mapping, options-struct layout,
  per-caller parameter values, lazy-trigger logic, eviction path, and no-fallback behaviour; three
  items flagged debugger-pending (VRAM-byte increment site, D3DX mip behaviour under MipLevels=1,
  and per-caller D3DFORMAT enum values when the hint is non-zero)
ida_anchor: f61f66a9ae0ec1e946105b2ecff76e8930cb1d1367df64e5688a5266f5ad9963
evidence: [static-ida]
status: partial   # confirmed items are implementation-safe; debugger-pending items are flagged below
subsystems: [texture_upload, ghtex_runtime, d3d9_integration]
networked: false
---

# Texture Upload Pipeline — Clean-Room Specification

> Clean-room neutral spec. Promoted from dirty-room analyst notes under EU Software Directive
> 2009/24/EC Art. 6. No decompiler pseudo-code, no binary virtual addresses, no decompiler
> identifiers. All byte offsets cited here are **struct-relative field offsets**, not binary
> virtual addresses.
>
> **Scope.** This spec documents the `.dds`/image-bytes → `GHTex` → `IDirect3DTexture9` **upload
> pipeline**: the single D3DX create call and its parameter sources, the lazy-load trigger, the
> fallback-absence contract, the per-caller parameter variants, device-pointer resolution, and the
> VRAM / lifetime accounting interplay. It is the **build/upload companion** to the container byte
> layout owned by `Docs/RE/formats/texture.md`. Runtime object layouts are owned by
> `Docs/RE/structs/ghtex.md` and `Docs/RE/structs/texture_manager.md` — this spec cross-references
> them without re-deriving field details.
>
> **Out of scope.** On-disk `.dds`/`.tga`/`.bmp`/`.png` byte formats → `Docs/RE/formats/texture.md`.
> VFS open / binary-search / heap-slurp mechanics → `Docs/RE/specs/resource_pipeline.md §1`. Terrain
> texture pool, `bgtexture.lst`, and two-level refcount eviction →
> `Docs/RE/specs/resource_pipeline.md §3B`. The runtime object field tables (GHTex 76-byte layout,
> GTextureManager, FrameTickScheduler) → `Docs/RE/structs/ghtex.md` and
> `Docs/RE/structs/texture_manager.md`. The renderer device layout that supplies the D3D9 device
> pointer → `Docs/RE/structs/renderer_device.md`.

---

## Status and verification banner

Evidence grades used throughout:

- **(CODE-CONFIRMED)** — behaviour or constant recovered directly from binary control-flow and
  confirmed at multiple call sites; safe to implement.
- **(DEBUGGER-PENDING)** — static analysis supports a strong hypothesis; confirm via a live
  `?ext=dbg` debugger session before treating as final.

**deep-3d-cartography (2026-06-29, static-only, ida_anchor f61f66a9):** VRAM accumulator increment
confirmed absent in static image — §10.3 and §11 item 1 updated to RESOLVED/vestigial. Format hint
for char/item/sky/terrain/panel paths confirmed `D3DFMT_UNKNOWN (0)` — §11 item 3 partially resolved;
guild-crest DXT2 honour question (§11 item 4) remains DEBUGGER-PENDING.

---

# 1. Architecture overview — (CODE-CONFIRMED)

The client has **no proprietary image decoder** and **no fallback / dummy texture** on the upload
path. Every game, character, item, and UI texture is created through a single D3DX9 in-memory API
call, **`D3DXCreateTextureFromFileInMemoryEx`** (from `d3dx9_42.dll`), which auto-detects the
container format from its leading magic bytes. A loose-file fallback path swaps in
`D3DXCreateTextureFromFileExA` when the packed VFS is not mounted.

The runtime texture handle is **`GHTex`** (76 bytes; full layout: `Docs/RE/structs/ghtex.md §1.2`).
Named textures are deduplicated by numeric texture id in the **`GTextureManager`** red-black-tree
cache (full layout: `Docs/RE/structs/texture_manager.md §2`).

The upload is **lazy**: the cache-fill path constructs a `GHTex`, fills its 40-byte D3DX parameter
block, and optionally triggers the upload immediately. The actual GPU upload is performed by
**`GHTex_Load`** (GHTex vtable slot `+0x04`) on first use.

## 1.1 The two direct callers — (CODE-CONFIRMED)

Exactly **two** call sites reach `D3DXCreateTextureFromFileInMemoryEx` directly:

| Caller | Role |
|---|---|
| `Texture_D3DXCreateFromDiskOrVfs` | Central texture create wrapper; reached via `GHTex_Load` on the main GHTex upload path. |
| `Texture_LoadFromVfsOrDisk` | Inline UI/icon loader; adds atlas textures to a per-window texture list without using the GHTex dedup cache. |

A third site (`HUD_DrawFPSCounter`, diagnostics overlay) uses the non-Ex variant
`D3DXCreateTextureFromFileInMemory` and is functionally identical; it needs no special model.

---

# 2. Read → decode → upload code path — (CODE-CONFIRMED)

## 2.1 Cache-fill: `GTextureManager_GetTexture`

The central registry entry point. Performs a red-black-tree lookup by numeric texture id:

- **Cache hit** — returns the cached `GTexture*` pointer. No refcount increment on a hit
  (`Docs/RE/structs/texture_manager.md §3`).
- **Cache miss** — allocates a 76-byte `GHTex`, runs its constructor (`GHTex_ctor`) to copy the
  VFS path into `m_strPath` (`+0x04`), write the caller's format hint into `m_dwFormat` (`+0x3C`),
  and set `m_dwTimeout` (`+0x24`) to `180,000` ms. If the timeout is non-zero, registers the
  handle in the `FrameTickScheduler` effect ring via `GHTexScheduler_RegisterSlot`. Then calls
  `GHTex_SetD3DXLoadOptions` to allocate and fill the 40-byte options block (§4). If the
  caller's load-now flag is set, calls `GHTex_Load` immediately and stamps `m_dwLastAccessTime`
  (`+0x28`). Wraps the `GHTex` in a 60-byte `GTexture` (refcount set to `1`), inserts it into
  the map, and returns it.

## 2.2 Lazy upload trigger: `GHTex_Load`

The GHTex vtable slot `+0x04`. Called on first use (or immediately when the load-now flag is set):

1. **Noop guard:** if `m_bActive` (`+0x20`) and `m_bLoaded` (`+0x21`) are both set and
   `m_pD3DTexture` (`+0x40`) is non-null, returns success immediately.
2. **Options check:** reads `m_pD3DXOptions` (`+0x48`). On null, logs a failure string and
   returns `0`.
3. **Device initialisation:** lazily caches the renderer-singleton pointer into a module-global
   (once only, on the first `GHTex_Load` call on the central path; a separate global serves the
   inline UI path). See §5 for device pointer resolution.
4. **Calls `Texture_D3DXCreateFromDiskOrVfs`** (§2.3) with the full parameter set assembled
   from the options block plus the two GHTex fields (`m_dwFormat` and the address of
   `m_pD3DTexture`).
5. **On success:** increments the global live-texture count; sets `m_bActive` and `m_bLoaded` to
   `1`; returns `1`.
6. **On failure:** logs `">> [GHTex::Load]%s load fail"` with the path; clears the loaded flags;
   zeroes `m_pD3DTexture` (`+0x40`); returns `0`. **There is no substitute or dummy texture:**
   callers receive a null handle on failure.

## 2.3 The D3DX create wrapper: `Texture_D3DXCreateFromDiskOrVfs`

Resolves the byte source and calls the appropriate D3DX9 API:

- **VFS mounted** (tested via the one global mount-flag byte — `Docs/RE/specs/resource_pipeline.md
  §1.5.5`):
  1. Opens the VFS entry by path (`DiskFile_ConstructAndOpenByName`) — the standard open router
     documented in `Docs/RE/specs/resource_pipeline.md §1.1`.
  2. Calls `DiskFile_GetMemoryBuffer` to obtain a `{bufPtr, len}` heap buffer.
  3. Calls `D3DXCreateTextureFromFileInMemoryEx(device, bufPtr, len, …)` with the 12 remaining
     parameters (§3).
  4. Frees the heap buffer via the `DiskFile` destructor.

- **VFS not mounted:** calls `D3DXCreateTextureFromFileExA(device, path, …)` with the same 12
  remaining parameters. The loose-file path is a pure dev/debug mode; shipped gameplay always
  runs with the VFS mounted.

The created `IDirect3DTexture9*` is written into the caller's out-parameter, which `GHTex_Load`
supplies as `&m_pD3DTexture` (`+0x40` on the `GHTex`).

## 2.4 Inline UI/icon loader: `Texture_LoadFromVfsOrDisk`

A separate load path for adding atlas textures into a per-window texture list (§6 in
`Docs/RE/specs/resource_pipeline.md §3A.4`). It does not use the `GHTex` dedup cache.

- **VFS mounted:** `Vfs_FindAndReadEntry` → `{bufPtr, len}` → `D3DXCreateTextureFromFileInMemoryEx`
  → `free(bufPtr)`.
- **VFS not mounted:** delegates to `Texture_D3DXCreateFromDiskOrVfs` (the disk-only variant).

This is the **second** direct caller of the in-memory create API noted in §1.1.

## 2.5 Eviction: `GHTex_Unload`

`FrameTickScheduler` sweeps the effect ring at approximately 1.1% per frame and calls
`GHTex_Unload` (vtable `+0x0C`) on any handle whose `m_dwLastAccessTime` has exceeded
`m_dwTimeout` (180,000 ms). `GHTex_Unload`:

1. Subtracts `m_dwTextureVramSize` (`+0x44`) from the global VRAM-footprint accumulator
   `g_VramFootprintAccumulator`.
2. Decrements the global live-texture count.
3. Calls `IDirect3DTexture9::Release()` on `m_pD3DTexture` (`+0x40`).
4. Zeroes `m_pD3DTexture` and clears the `m_bLoaded` flag (`+0x21`), leaving `m_bActive`
   (`+0x20`) set so the handle remains in the cache for lazy reload.

The `GTexture` wrapper and the cache map entry survive eviction; the next access re-triggers
`GHTex_Load`.

---

# 3. `D3DXCreateTextureFromFileInMemoryEx` — full 15-argument parameter mapping — (CODE-CONFIRMED)

The call signature is:

```
D3DXCreateTextureFromFileInMemoryEx(
    pDevice, pSrcData, SrcDataSize,
    Width, Height, MipLevels, Usage, Format, Pool,
    Filter, MipFilter, ColorKey, pSrcInfo, pPalette,
    ppTexture)
```

| D3DX param | Source on the GHTex upload path |
|---|---|
| `pDevice` | Renderer object `d3d9_device` field (`+0x2B738`) — see §5. |
| `pSrcData` | VFS-slurp heap buffer pointer (from `DiskFile_GetMemoryBuffer` or `Vfs_FindAndReadEntry`). |
| `SrcDataSize` | Length of the heap buffer (from the same call). |
| `Width` | `m_pD3DXOptions[0]` — caller-supplied bucket width. |
| `Height` | `m_pD3DXOptions[1]` — caller-supplied bucket height. |
| `MipLevels` | `m_pD3DXOptions[2]` — see §3.1 for per-caller values. |
| `Usage` | `m_pD3DXOptions[3]`. |
| `Format` | **`GHTex.m_dwFormat` (`+0x3C`)** — sourced directly from the handle field, **not** from the options block. |
| `Pool` | `m_pD3DXOptions[4]`. |
| `Filter` | `m_pD3DXOptions[5]`. |
| `MipFilter` | `m_pD3DXOptions[6]`. |
| `ColorKey` | `m_pD3DXOptions[7]`. |
| `pSrcInfo` | `m_pD3DXOptions[8]`. |
| `pPalette` | `m_pD3DXOptions[9]`. |
| `ppTexture` | **`&GHTex.m_pD3DTexture` (`+0x40`)** — sourced directly from the handle field, **not** from the options block. |

`Format` and `ppTexture` are the only parameters taken directly from the `GHTex` fields; all other
variable parameters pass through the 40-byte options block.

---

# 4. The 40-byte D3DX options struct (`GHTex.m_pD3DXOptions`, offset `+0x48`) — (CODE-CONFIRMED)

`GHTex_SetD3DXLoadOptions` allocates a 40-byte (`0x28`) block, zeroes it, and writes 10 dwords in
order. (It frees any prior block unless it equals the scheduler's shared no-options sentinel before
reallocating.) The layout maps directly to the trailing 10 arguments of the D3DX create call:

| Dword index | Field offset | D3DX argument |
|:---:|:---:|---|
| `[0]` | `+0x00` | `Width` |
| `[1]` | `+0x04` | `Height` |
| `[2]` | `+0x08` | `MipLevels` |
| `[3]` | `+0x0C` | `Usage` |
| `[4]` | `+0x10` | `Pool` |
| `[5]` | `+0x14` | `Filter` |
| `[6]` | `+0x18` | `MipFilter` |
| `[7]` | `+0x1C` | `ColorKey` |
| `[8]` | `+0x20` | `pSrcInfo` |
| `[9]` | `+0x24` | `pPalette` |

---

# 5. Device pointer resolution — (CODE-CONFIRMED)

`Texture_D3DXCreateFromDiskOrVfs` reads the D3D9 device from the renderer object:

```
device = *(renderer_object + 0x2B738)    // GHRenderer.d3d9_device
```

This is the canonical `d3d9_device` field at renderer object `+0x2B738` per
`Docs/RE/structs/renderer_device.md §3`. The renderer object itself is reached through two
lazily-initialised module globals that are each populated once from the renderer singleton:

- **Central path** (driven by `GHTex_Load`): a module global written on the first `GHTex_Load`
  call and reused for all subsequent uploads on this path.
- **Inline UI path** (driven by `Texture_LoadFromVfsOrDisk`): a separate module global of
  identical type and source.

Both globals resolve to the same renderer singleton and therefore to the same `IDirect3DDevice9*`
at `+0x2B738`. Neither introduces a per-call device lookup.

---

# 6. Per-caller parameter values — (CODE-CONFIRMED)

Constants: `D3DX_DEFAULT = -1 (0xFFFFFFFF)`, `D3DX_DEFAULT_NONPOW2 = -3`,
`D3DPOOL_MANAGED = 1`, `D3DX_FILTER_NONE = 1`.

| Caller / path | W | H | MipLevels | Usage | Format | Pool | Filter | MipFilter | ColorKey | pSrcInfo | pPalette |
|---|---|---|---|---|---|---|---|---|---|---|---|
| Registry char/item (`GTextureManager_GetTexture`) | caller-supplied | caller-supplied | **1** | 0 | caller hint in `m_dwFormat` | 1 | -1 | -1 | 0 | 0 | 0 |
| UI inline, VFS mounted (`Texture_LoadFromVfsOrDisk`) | caller-supplied | caller-supplied | **1** | 0 | caller-supplied | 1 | -1 | -1 | 0 | 0 | 0 |
| UI inline, loose-file fallback (same function) | caller-supplied | caller-supplied | **-3** | 0 | **-3** | 1 | -1 | -1 | 0 | 0 | 0 |
| Guild crest (`GuildCrest_LoadOneTexture`) | caller-supplied (~23) | caller-supplied (~23) | **1** | 0 | FourCC hint `DXT2` (`0x32545844`) | 1 | **1** | **1** | 0 | 0 | 0 |
| Cel/glow ramp `toonramp.bmp` | 0 | 0 | 1 | 0 | `D3DFMT_X8R8G8B8` (20) | 1 | -1 | -1 | 0 | 0 | 0 |

**Key observations:**

- **`MipLevels = 1` on essentially every game-texture upload.** Only the top mip level is requested
  even when the on-disk DDS carries a full mip chain (compare the 1024² 11-mip char sample in
  `Docs/RE/formats/texture.md`). Mip chain generation is not requested on the VFS path (`D3DX_DEFAULT`
  mips apply only to the loose-file fallback). A 1:1 reimplementation should upload only the base
  level for game/char/item/UI textures when the VFS is mounted. **(DEBUGGER-PENDING: whether D3DX,
  when given `MipLevels=1`, still reads the file's stored sublevels or uploads only level 0.)**

- **`Pool = D3DPOOL_MANAGED (1)` universally.** The D3D9 runtime keeps a system-memory backing copy
  and handles device-lost recovery without client involvement. See
  `Docs/RE/specs/resource_pipeline.md §3.4` for the managed-pool eviction model.

- **`Format` comes from `m_dwFormat` (`+0x3C`), not the options block.** The char/item registry,
  sky, terrain, and panel paths all supply `m_dwFormat = 0` (`D3DFMT_UNKNOWN`): the render-type
  globals routed as the format argument are read-only and hold literal zero in the shipped binary
  **(CODE-CONFIRMED)**. A non-zero hint is used only on three specific paths: guild crest (`DXT2`
  FourCC `0x32545844`), `toonramp.bmp` (`D3DFMT_X8R8G8B8 = 20`), and the FPS-overlay disk branch
  (`D3DFMT_R8G8B8 = 20`) — all statically known. The remaining DEBUGGER-PENDING question (§11 item 4)
  is whether D3DX honours the guild-crest DXT2 FourCC hint against the container's own FourCC, not
  what the hint value itself is.

- **`ColorKey = 0`, `pSrcInfo = NULL`, `pPalette = NULL` on every texture upload path.** The sky
  surface loader (not a `GHTex` path) is the only D3DX call in the pipeline that passes a non-zero
  colorkey (opaque-black source key for cloud surfaces, per
  `Docs/RE/specs/resource_pipeline.md §3A.1`).

---

# 7. Mip handling and the cel/glow render-target creates — (CODE-CONFIRMED)

## 7.1 No mip generation on the upload path

`MipLevels = 1` on every GHTex upload; `D3DX_DEFAULT` mip generation (`-1`) is used only by the
no-VFS loose-file fallback (`Texture_D3DXCreateFromDiskOrVfs` disk branch), which is not the
shipping runtime path. A clean-room port should import only the base mip level for sampled textures
unless confirmed otherwise by the debugger result in §6.

## 7.2 `D3DXCreateTexture` (blank render-target creates) — separate path, not a fallback

`Renderer_InitCelGlowShaders` uses `D3DXCreateTexture` (blank create, not the from-file variant) for
three post-process render-target textures: one at the device resolution, one fallback at `1024×1024`
if the device-resolution create fails, and one downscaled blur render target
(`1024 / divX × 1024 / divY`). Each is created with `(W, H, 1, 1 /*Usage=RenderTarget*/, fmt, 0,
&dst)` then wrapped by `D3DXCreateRenderToSurface`. These are glow/blur post-process surfaces with
**no connection to the image-upload path** and are **not** a 2×2 or white dummy fallback texture.

The 1024² fallback is the only texture-dimension fallback anywhere in the client; it applies only to
the cel/glow render-target, not to sampled game textures.

## 7.3 Shader assembly — separate path

`D3DXAssembleShader` / `D3DXAssembleShaderFromFileA` handle `.vsh`/`.psh` shader source; vertex/pixel
shaders never reach the texture-create calls. Consistent with `Docs/RE/formats/texture.md`.

---

# 8. No fallback texture — (CODE-CONFIRMED)

There is **no 2×2 / white / dummy / fill fallback texture** on the upload path. Static searches
for such patterns yield zero hits. On `GHTex_Load` failure the error is logged and `m_pD3DTexture`
(`+0x40`) is left as `NULL`; callers receive a null texture handle and must handle it in their own
draw logic. The sole device-level fallback is the cel/glow render-target dropping to `1024×1024`
when the device-resolution create fails (§7.2).

---

# 9. Runtime object cross-references — (CODE-CONFIRMED)

This section names the fields this spec reads/writes on `GHTex`. Full layouts are in the cited struct
specs; do not re-derive them here.

| `GHTex` field | Offset | Role in upload pipeline |
|---|---|---|
| `m_strPath` | `+0x04` | VFS path passed to `Texture_D3DXCreateFromDiskOrVfs`. |
| `m_bActive` | `+0x20` | Set by `GHTex_ctor`; cleared on object deletion. Guards the noop path in `GHTex_Load`. |
| `m_bLoaded` | `+0x21` | Set to `1` on successful upload; cleared on eviction by `GHTex_Unload`. |
| `m_dwTimeout` | `+0x24` | Idle threshold (180,000 ms); non-zero causes registration in the `FrameTickScheduler` effect ring. |
| `m_dwLastAccessTime` | `+0x28` | Written on load-now and cache-acquire paths; prevents eviction of recently used handles. |
| `m_dwFormat` | `+0x3C` | Caller-supplied `D3DFORMAT` or FourCC hint; passed directly as the `Format` argument to D3DX. |
| `m_pD3DTexture` | `+0x40` | Receives the `IDirect3DTexture9*` on success; zeroed on failure or eviction. |
| `m_dwTextureVramSize` | `+0x44` | VRAM footprint in bytes; subtracted from `g_VramFootprintAccumulator` on eviction. |
| `m_pD3DXOptions` | `+0x48` | Pointer to the 40-byte options block (§4); allocated by `GHTex_SetD3DXLoadOptions`. |

Full struct layout: `Docs/RE/structs/ghtex.md §1.2`. Cache container, refcount mechanics, and
`FrameTickScheduler` ring: `Docs/RE/structs/texture_manager.md §2–§4`.

---

# 10. Refcount and VRAM accounting interplay — (CODE-CONFIRMED / DEBUGGER-PENDING)

## 10.1 L1 cache refcount (CODE-CONFIRMED)

`GTexture.m_dwRefCount` (`+0x04`) is set to `1` at first cache insertion. There is no refcount
increment on a cache hit; callers manage subsequent releases. Cache-map removal destructs both the
`GTexture` wrapper and the underlying `GHTex`. Full refcount mechanics:
`Docs/RE/structs/texture_manager.md §5`.

## 10.2 L2 GPU-residency count (CODE-CONFIRMED)

A module-global live-texture count is incremented by `GHTex_Load` on success and decremented by
`GHTex_Unload`. This count tracks the number of `GHTex` handles that currently hold a live
`IDirect3DDevice9*` reference.

## 10.3 VRAM-footprint accumulator — increment absent in static image (vestigial)

`g_VramFootprintAccumulator` is decremented by `GHTex_Unload` (subtracting
`GHTex.m_dwTextureVramSize` at `+0x44`). Exhaustive static cross-reference analysis of the
accumulator finds **no additive (`+=`) writer anywhere in the binary**; the decrement is the sole
cross-reference. `GHTex.m_dwTextureVramSize` (`+0x44`) is zero-initialised by the constructor and
no static path writes it. The accumulator is therefore **one-sided and vestigial** in the shipped
binary; VRAM byte accounting is not operative. Residual `[debugger-confirm]` (low priority):
whether `+0x44` holds a non-zero value at runtime through an aliased write not visible to static
cross-reference analysis.

---

# 11. Open questions — debugger-pending

1. **VRAM-byte accumulator increment site — RESOLVED (absent in static image, vestigial).**
   Exhaustive static cross-reference analysis finds no additive writer for `g_VramFootprintAccumulator`
   anywhere in the binary. `GHTex.m_dwTextureVramSize` (`+0x44`) is zero-initialised and never written
   on any examined path. The accumulator is one-sided/vestigial; see §10.3. Residual
   `[debugger-confirm]` (low priority): whether `+0x44` is non-zero at runtime.

2. **D3DX mip behaviour under `MipLevels = 1`.** Does D3DX consume the file's stored sublevels or
   upload only level 0 when given `MipLevels = 1`? Affects whether a 1:1 Godot import should
   generate mips or import pre-built mip chains. Confirm by reading the created texture's level
   count on a known full-mip DDS (e.g. a `tex10241024` 1024² 11-mip char skin after `GHTex_Load`).

3. **Per-caller `D3DFORMAT` — PARTIALLY RESOLVED.** Static analysis confirms:
   - `D3DFMT_UNKNOWN (0)` for char/item registry, sky, terrain, and panel paths — the render-type
     globals routed as the format argument are read-only literal zero **(CODE-CONFIRMED)**.
   - `DXT2` FourCC (`0x32545844`) for guild-crest **(CODE-CONFIRMED statically)**.
   - `D3DFMT_X8R8G8B8 (20)` for `toonramp.bmp` **(CODE-CONFIRMED statically)**.
   - Remaining DEBUGGER-PENDING question: see item 4 (guild-crest D3DX runtime behaviour, not the
     hint value itself).

4. **Guild-crest `DXT2` FourCC format hint.** The guild-crest path passes `DXT2` (`0x32545844`) as
   a `D3DFORMAT` hint with `D3DX_FILTER_NONE`. Whether D3DX honours the explicit BC2 FourCC hint
   versus the container's own FourCC affects the premultiplied/straight-alpha split documented in
   `Docs/RE/formats/texture.md`. Confirm at a live guild-crest `GHTex_Load` breakpoint by reading
   `GHTex+0x3C` and the resulting `IDirect3DTexture9` format.
