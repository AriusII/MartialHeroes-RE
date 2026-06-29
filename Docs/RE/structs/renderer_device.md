---
verification: confirmed; derived from IDB static analysis (IDB SHA f61f66a9) by tracing the
  device-init, device-reset, device-lost-release, and per-frame spine routines, cross-confirmed
  against the camera-setup and direct-draw code paths
ida_anchor: f61f66a9ae0ec1e946105b2ecff76e8930cb1d1367df64e5688a5266f5ad9963
evidence: [static-ida]
status: code-confirmed
conflicts: >
  The individual identity of the ten post-process COM slots (+0x2B9DC..+0x2BA00) and the five
  device-dependent COM slots (+0x2B6D4, +0x2B6D8, +0x2B890, +0x2B894, +0x2B898) was
  originally recovered for release order only. Both per-slot identity mappings are now
  RESOLVED (static) by Docs/RE/specs/post_processing.md: §9 Q#1 (glow_chain array) resolved
  by post_processing.md §2; §9 Q#2 (device_com_slot_a..e shader handles) resolved by
  post_processing.md §3. The unmapped regions (+0x2B740..+0x2B88F and the +0x2B6EC
  gap) are not yet enumerated. View-matrix major-order and world up-axis remain
  debugger-pending (see rendering.md §2.2). Supersedes runtime_singletons.md §4 (the
  camera region was mis-labelled there — see §6 reconciliation note).
subsystems: [render_pipeline]
---

# Structs: GHRenderer — Direct3D 9 Device Wrapper / Renderer Object

> **Clean-room spec. Neutral description only — no decompiler pseudo-code, no binary
> addresses, no decompiler-generated identifiers.** Promoted from dirty-room static analysis
> under EU Software Directive 2009/24/EC Art. 6, solely to document the object layout for
> clean-room reimplementation. All byte offsets are **relative to the start of the object**
> and are struct offsets, not virtual-address constants. They must never be used as memory
> addresses in the reimplementation.
>
> Citing engineers: `// spec: Docs/RE/structs/renderer_device.md`.
>
> **Confidence vocabulary:**
> - **CONFIRMED** — value or layout recovered from the binary instruction stream, corroborated
>   across multiple call sites.
> - **STATIC-HYPOTHESIS** — consistent single-source inference; implement but keep tunable.
> - **DEBUGGER-PENDING** — flagged for a live `?ext=dbg` session before treating as final.
>
> **Scope.** This file documents the `Diamond::GHRenderer` object: its field layout, the
> D3D9 present-parameters sub-layout, the `IDirect3DDevice9` and `IDirect3D9` vtable method
> roles exercised through it, the camera-basis cache, the offscreen/post-process enable path,
> and the proven absences (no flat render-state shadow array, no texture-stage/sampler
> shadow-cache array on this object).
>
> **What this file is not.** It does not restate the per-pass render-state matrix, the
> glow/bloom shader chain, or the full frame-orchestration driver — those belong in
> `Docs/RE/specs/rendering.md` and `Docs/RE/specs/render_pipeline.md`. The per-view draw
> object layout is in `Docs/RE/structs/gview.md`. The 18-slot GRenderState polymorphic cache
> lives on the per-view/cull machinery; see `Docs/RE/specs/rendering.md §4`.

---

## 1. Object identity and role

`Diamond::GHRenderer` is the **single large renderer object** that the `Diamond` engine (an
OpenSceneGraph-style Direct3D 9 scene graph) uses as both its D3D9 device wrapper and its
scene/post-processing state container. What earlier analysis described as a "thin device
wrapper" and what it separately called a "~179 KB scene/post object" are **one and the same
static allocation** in this build.

The object:

- directly owns the `IDirect3D9` factory pointer, the `IDirect3DDevice9` device pointer, the
  `ID3DXSprite` sprite object, the D3D present parameters, the adapter display mode, the probed
  surface-format records, the default 2×2 fallback texture, the ten post-process render-target
  COM slots, the offscreen/post-process enable flag, and the current-camera basis cache;
- exposes all D3D9 convenience setters (`SetRenderState`, `SetTransform`, `SetLight`,
  `LightEnable`, `SetMaterial`, `SetTexture`, `SetTextureStageState`, `SetSamplerState`,
  `SetStreamSource`, `SetFVF`, `SetIndices`, `DrawIndexedPrimitive`, `Present`, `Clear`,
  `BeginScene`, `EndScene`) as instance methods whose body reaches the device through the
  `d3d9_device` field at +0x2B738.

**Engine object holds the renderer at +0x04.** The `Engine` singleton (+0x04 holds the
renderer pointer — see `Docs/RE/structs/runtime_singletons.md §3.11`) and the frame-driver
object (which presents each frame and runs scene steps through the same pointer) both carry the
renderer address in their +0x04 slot. CONFIRMED.

**Engine-global aliases.** The device pointer (`d3d9_device`) is also cached in one or more
engine-global pointer slots initialised once from the renderer's field. Treat those aliases as
references to the single object documented here, not separate instances.

---

## 2. Full offset table — `Diamond::GHRenderer` (byte offsets from object start)

All offsets are CONFIRMED unless marked otherwise.

| Offset | Size (B) | Type | Field | Role |
|-------:|:--------:|------|-------|------|
| +0x2B6C4 | 4 | uint32 | `display_width` | Configured backbuffer width; source for `BackBufferWidth` in the present parameters. |
| +0x2B6C8 | 4 | uint32 | `display_height` | Configured backbuffer height. |
| +0x2B6CC | 4 | uint32 | `display_bpp` | Bits per pixel (16 or 32); selects the format-candidate probe table. |
| +0x2B6D0 | 1 | uint8 | `windowed` | 1 = windowed, 0 = fullscreen; also seeds `D3DPRESENT_PARAMETERS::Windowed`. |
| +0x2B6D4 | 4 | ptr (COM) | `device_com_slot_a` | **Glow-blur pixel shader handle** (default `data/shader/power1dx8.psh`; overridable via `display.lua DISPLAY_POWERSHADER`). Device-dependent; released (device-gated) during lost-device cleanup. RESOLVED (static) — see `Docs/RE/specs/post_processing.md §3`. |
| +0x2B6D8 | 4 | ptr (COM) | `device_com_slot_b` | **Composite pixel shader handle** (`finaldx8.psh`). Device-dependent; released (device-gated) during lost-device cleanup. RESOLVED (static) — see `Docs/RE/specs/post_processing.md §3`. |
| +0x2B6DC | 16 | D3DDISPLAYMODE | `display_mode` | Adapter display mode read at init and reset: Width (+0x2B6DC), Height (+0x2B6E0), RefreshRate (+0x2B6E4), Format (+0x2B6E8). |
| +0x2B6EC | 16 | bytes | *(unmapped gap)* | Gap between display mode and present parameters. Not enumerated this pass. |
| +0x2B6FC | 56 | D3DPRESENT_PARAMETERS | `present_params` | Full present-parameters block; field map in §3. |
| +0x2B734 | 4 | ptr | `d3d9` | `IDirect3D9*` factory. Used for `GetAdapterDisplayMode`, format probing, and device creation. |
| +0x2B738 | 4 | ptr | `d3d9_device` | **`IDirect3DDevice9*`** — the cached D3D9 device. Every per-frame D3D call is routed through this field. CONFIRMED. |
| +0x2B73C | 4 | ptr | `sprite` | `ID3DXSprite*` created by `D3DXCreateSprite` at init. |
| +0x2B740 | ~336 | bytes | *(unmapped)* | Region not enumerated this pass. |
| +0x2B890 | 4 | ptr (COM) | `device_com_slot_c` | **Cel vertex shader handle** (`dotoonshading.vsh`). Device-dependent; released (device-gated) during lost-device cleanup. RESOLVED (static) — see `Docs/RE/specs/post_processing.md §3`. |
| +0x2B894 | 4 | ptr (COM) | `device_com_slot_d` | **Cel pixel shader handle — normal mode** (`dotoonshading.psh`). Device-dependent; released (device-gated). RESOLVED (static) — see `Docs/RE/specs/post_processing.md §3`. |
| +0x2B898 | 4 | ptr (COM) | `device_com_slot_e` | **Cel pixel shader handle — stealth / concealment mode** (`dotoonshading2.psh`). Device-dependent; released (device-gated). RESOLVED (static) — see `Docs/RE/specs/post_processing.md §3`. |
| +0x2B89C | 4 | D3DFORMAT | `probed_backbuffer_format` | Probed adapter/backbuffer format (fullscreen path). |
| +0x2B8A0 | 4 | D3DFORMAT | `probed_format_a` | Probed depth/stencil or texture-format tier result from the init probe loop. |
| +0x2B8A4 | 4 | uint32 (ARGB) | `clear_color` | Default background clear color; passed to `IDirect3DDevice9::Clear` (flags = 3). |
| +0x2B974 | 12 | float[3] | `camera_world_pos` | **Current camera world position** (X, Y, Z); refreshed each frame by `Renderer_SetupCameraAndFrustum`. |
| +0x2B980 | 64 | D3DMATRIX | `camera_orientation` | **Current camera orientation matrix** (translation cleared; rotation/scale only); refreshed each frame. Used by billboards and effect systems as the engine-wide "current camera" basis. |
| +0x2B9C0 | 1 | uint8 | `view_dirty` | Set by the view-transform setter when the VIEW matrix has changed; signals a re-upload is needed. |
| +0x2B9C4 | 4 | ptr | `input_context` | Pointer to the input-context singleton; cached on the renderer. (STATIC-HYPOTHESIS) |
| +0x2B9C8 | 4 | D3DFORMAT | `probed_format_b` | Additional probed format-tier result. |
| +0x2B9CC | 4 | D3DFORMAT | `probed_format_c` | Additional probed format-tier result. |
| +0x2B9D0 | 4 | D3DFORMAT | `probed_format_d` | Additional probed format-tier result. |
| +0x2B9D4 | 4 | D3DFORMAT | `probed_format_e` | Additional probed format-tier result. |
| +0x2B9D8 | 4 | ptr | `default_texture` | **`IDirect3DTexture9*`** — 2×2 grey X8R8G8B8 fallback texture created at init by `Renderer_CreateDefaultTexture`; bound automatically when a draw call passes a null texture. |
| +0x2B9DC | 40 | ptr[10] (COM) | `glow_chain` | **Post-process glow-chain COM array** — ten consecutive 4-byte pointer slots (+0x2B9DC..+0x2BA00, inclusive last slot). Hold TEX0/TEX1/TEX2 render-target textures, surfaces, render-to-surface helpers, and the toon-ramp LUT. Released first during lost-device cleanup. Per-slot identity: RESOLVED (static) — full ten-slot table in `Docs/RE/specs/post_processing.md §2`. |
| +0x2BA3C | 4 | uint32 | `offscreen_enable` | **Offscreen / post-process enable flag.** 0 = direct draw path; 2 = offscreen render-target + glow chain enabled. Set to 2 at device init when the global offscreen option is on; cleared during lost-device cleanup. |

> **Camera region note (correction to runtime_singletons.md §4).** The earlier `runtime_singletons.md §4`
> entry labelled +0x2B974 as "`view_matrix` (64 bytes, float[16])". The correct layout is two
> distinct fields: a 12-byte world-position vec3 at +0x2B974 followed immediately by a 64-byte
> orientation matrix at +0x2B980. The view-dirty byte at +0x2B9C0 ends both fields. This spec
> is authoritative; `runtime_singletons.md §4` is a superseded summary for this region.

---

## 3. D3DPRESENT_PARAMETERS sub-layout (base +0x2B6FC)

The 56-byte block at +0x2B6FC is a standard `D3DPRESENT_PARAMETERS` struct (14 × 4-byte fields,
zero-initialised before being filled). All fields and recovered values are CONFIRMED.

| Object offset | D3DPRESENT_PARAMETERS field | Recovered value / source |
|--------------:|----------------------------|--------------------------|
| +0x2B6FC | `BackBufferWidth` | `display_width` (+0x2B6C4) |
| +0x2B700 | `BackBufferHeight` | `display_height` (+0x2B6C8) |
| +0x2B704 | `BackBufferFormat` | 22 (D3DFMT\_X8R8G8B8) fullscreen; display-mode Format (+0x2B6E8) windowed; or `probed_backbuffer_format` (+0x2B89C) |
| +0x2B708 | `BackBufferCount` | 1 |
| +0x2B70C | `MultiSampleType` | 0 |
| +0x2B710 | `MultiSampleQuality` | 0 |
| +0x2B714 | `SwapEffect` | 1 (D3DSWAPEFFECT\_DISCARD) |
| +0x2B718 | `hDeviceWindow` | engine HWND (windowed: the passed window handle) |
| +0x2B71C | `Windowed` | from `windowed` flag (+0x2B6D0) |
| +0x2B720 | `EnableAutoDepthStencil` | 1 |
| +0x2B724 | `AutoDepthStencilFormat` | probed; default 80 (D3DFMT\_D16), candidates 71 (D3DFMT\_D32) / 77 (D3DFMT\_D24X8) / 75 (D3DFMT\_D24S8) |
| +0x2B728 | `Flags` | 0 |
| +0x2B72C | `FullScreen_RefreshRateInHz` | 0 (windowed); 60 (fullscreen, set during device reset) |
| +0x2B730 | `PresentationInterval` | 0x80000000 (D3DPRESENT\_INTERVAL\_IMMEDIATE) |

The block ends at +0x2B734, immediately preceding the `d3d9` factory pointer.

---

## 4. IDirect3D9 methods invoked through the renderer

The following `IDirect3D9` methods are called through the `d3d9` field (+0x2B734). Vtable byte
offsets equal `4 × vtable-slot-index` (public Direct3D 9 SDK vtable ordering).

| `IDirect3D9` vtbl offset | Method | When called |
|---------:|---------|-------------|
| +32 (slot 8) | `GetAdapterDisplayMode` | At device init and each reset to refresh `display_mode` (+0x2B6DC). |
| +36 (slot 9) | `CheckDeviceType` | Format-probe loop at init. |
| +40 (slot 10) | `CheckDeviceFormat` | Format-probe loop at init. |
| +48 (slot 12) | `CheckDepthStencilMatch` | Depth/stencil format probe loop at init. |
| +64 (slot 16) | `CreateDevice` | Called once at init to create the `IDirect3DDevice9`. |

---

## 5. IDirect3DDevice9 methods invoked through the renderer

All calls reach `IDirect3DDevice9` through the `d3d9_device` field (+0x2B738). Vtable byte
offsets equal `4 × vtable-slot-index` (public Direct3D 9 SDK vtable ordering). All rows
CONFIRMED.

| `IDirect3DDevice9` vtbl offset | Method | Wrapper / observed arguments |
|------:|---------|------------------------------|
| +64 (slot 16) | `Reset` | Called by the device-lost recovery path after rebuilding `present_params`. |
| +68 (slot 17) | `Present` | `Renderer_Present` — `Present(NULL, NULL, NULL, NULL)`. |
| +92 (slot 23) | `CreateTexture` | Default fallback texture and render-target creation. |
| +164 (slot 41) | `BeginScene` | `GDevice_BeginScene` — returns `HRESULT ≥ 0` on success. |
| +168 (slot 42) | `EndScene` | End-of-frame and inactive-branch call. |
| +172 (slot 43) | `Clear` | Flags = 3 (`D3DCLEAR_TARGET | D3DCLEAR_ZBUFFER`); depth usually 1.0; color from `clear_color` (+0x2B8A4). |
| +176 (slot 44) | `SetTransform` | View setter: state 2 (`D3DTS_VIEW`), also sets `view_dirty` (+0x2B9C0). Projection setter: state 3 (`D3DTS_PROJECTION`). |
| +180 (slot 45) | `GetTransform` | Projection getter: state 3. |
| +192 (slot 48) | `GetViewport` | Viewport snapshot taken at frame start. |
| +196 (slot 49) | `SetMaterial` | `GDevice_SetMaterial` — passes a `D3DMATERIAL9*`. |
| +204 (slot 51) | `SetLight` | `Renderer_SetDeviceLight` — `SetLight(index, D3DLIGHT9*)`. |
| +212 (slot 53) | `LightEnable` | Called on the blended-drawable path: `LightEnable(index, TRUE)`. |
| +228 (slot 57) | `SetRenderState` | Universal state setter; see §5.1 for the state-type catalogue. |
| +260 (slot 65) | `SetTexture` | `GDevice_SetStageTexture` — binds the supplied texture, or `default_texture` (+0x2B9D8) when null. |
| +268 (slot 67) | `SetTextureStageState` | Per-frame imperative setup for stages 0, 1, 2 (color-op/args, alpha-op). |
| +276 (slot 69) | `SetSamplerState` | Per-frame imperative setup for sampler 0 (MINFILTER=3, MAGFILTER=3, MIPFILTER=2). |
| +328 (slot 82) | `DrawIndexedPrimitive` | `RenderDevice_DrawIndexedPrimitive` |
| +356 (slot 89) | `SetFVF` | `RenderDevice_SetFVF` |
| +400 (slot 100) | `SetStreamSource` | `RenderDevice_SetStreamSource` — `(stream, vertex_buffer, 0, stride)`. |
| +416 (slot 104) | `SetIndices` | `RenderDevice_SetIndices` |

### 5.1 SetRenderState — state type catalogue

Each call forwards `SetRenderState(stateType, rawValue)` with the integer value unchanged.
This is the mechanism that makes `rendering.md`'s blend/Z-write byte readings exact. The
following state types have confirmed wrappers:

| `D3DRS` type value | Public enum name | Wrapper role |
|-------------------:|-----------------|--------------|
| 7 | `D3DRS_ZENABLE` | Z-test enable / disable |
| 14 | `D3DRS_ZWRITEENABLE` | Z-write enable / disable |
| 19 | `D3DRS_SRCBLEND` | Source blend factor (raw `D3DBLEND` value) |
| 20 | `D3DRS_DESTBLEND` | Destination blend factor |
| 22 | `D3DRS_CULLMODE` | Cull mode setter |
| 27 | `D3DRS_ALPHABLENDENABLE` | Alpha-blend enable |
| 137 | `D3DRS_LIGHTING` | Lighting enable / disable |
| 139 | `D3DRS_AMBIENT` | Device ambient (`Light_SetDeviceAmbient`) |

Additional state types (alpha-test enable, dither, fog enable, fill mode, texture factor, fog
vertex mode/start, range-fog enable) follow the same pass-through pattern and are enumerated
in `Docs/RE/specs/rendering.md §3`.

---

## 6. Camera-basis cache

Each frame `Renderer_SetupCameraAndFrustum` performs an RTTI cast of the bound camera node
(`GCamera` → `GPerspectiveCamera`), reads FOV (+168 from the camera object) and aspect
(+172), defaulting to π/4 ≈ 0.785398 on cast failure, computes the camera world transform
into the per-view draw object's matrix slot, inverts it, then writes:

- the camera **world translation** into `camera_world_pos` (+0x2B974, 12 bytes, float[3]);
- the **rotation-only orientation** (translation cleared) into `camera_orientation`
  (+0x2B980, 64 bytes, D3DMATRIX).

These two fields are the engine-wide "current camera" back-reference values read by billboards
and effect systems. The per-view draw object carries a back-pointer to the renderer (at +288
from the start of the GView sub-object) used to read the `offscreen_enable` flag
(+0x2BA3C) when choosing the draw path. See `Docs/RE/structs/gview.md` for the full per-view
object layout.

---

## 7. Direct-draw state programming — absence of shadow arrays

The direct-draw path (`Renderer_DrawScene_Direct`) performs all texture-stage and sampler
programming **imperatively each frame**; there is no mirrored shadow-cache array for these
states on this object. The path:

1. Snapshots the viewport via `GetViewport` (+192).
2. Sets the viewport.
3. Calls `Clear` (TARGET | ZBUFFER, depth from the per-view draw object).
4. Calls `BeginScene`.
5. Sets the VIEW transform.
6. Fires the ordered scene callback slots.
7. Imperatively programs:
   - **Texture stages** (via `SetTextureStageState` +268): stage 0 color-op and color/alpha
     args; stage 1 and stage 2 color/alpha ops.
   - **Sampler 0** (via `SetSamplerState` +276): MINFILTER=3 (linear), MAGFILTER=3
     (linear), MIPFILTER=2 (linear).

The values are written fresh every frame. There is **no** texture-stage or sampler shadow-cache
array on `Diamond::GHRenderer`.

The 18-slot polymorphic `GRenderState` cache (documented in `Docs/RE/specs/rendering.md §4`)
lives on the per-view / cull machinery (`GCull_CullScene` draw stage) — it is **not** on this
renderer object.

---

## 8. Object lifetime

### 8.1 Creation — `Renderer_CreateD3DDevice`

Reads the windowed/bpp config (via `DisplayConfig_ParseFramerate`), optionally calls
`ChangeDisplaySettingsA` for fullscreen, creates the `IDirect3D9` factory via
`Direct3DCreate9`, reads the adapter display mode via `GetAdapterDisplayMode`, probes
adapter formats / depth-stencil formats / multisample tiers through a series of
`CheckDeviceType` / `CheckDeviceFormat` / `CheckDepthStencilMatch` loops over
format-candidate tables, fills `present_params` (+0x2B6FC), calls `IDirect3D9::CreateDevice`,
then `D3DXCreateSprite` (stores result in `sprite`, +0x2B73C), `Renderer_CreateDefaultTexture`
(stores in `default_texture`, +0x2B9D8), and `Renderer_InitCelGlowShaders` (populates
`glow_chain`, +0x2B9DC). On success, if the global offscreen option is set, writes
`offscreen_enable` (+0x2BA3C) = 2.

### 8.2 Device reset — `Renderer_ResetD3DDevice`

Re-reads the adapter display mode into `display_mode` (+0x2B6DC), rebuilds `present_params`
(fullscreen sets `FullScreen_RefreshRateInHz` to 60), and calls `IDirect3DDevice9::Reset`.

### 8.3 Lost-device cleanup — `Renderer_ReleaseDeviceResources`

First releases the ten `glow_chain` COM slots (+0x2B9DC..+0x2BA00) and clears
`offscreen_enable` (+0x2BA3C). Then, gated on the device still being present, releases the
five device-dependent COM slots (+0x2B6D4, +0x2B6D8, +0x2B890, +0x2B894, +0x2B898).

### 8.4 Per-frame — `Engine_DeviceStepAndPresent`

The per-iteration device step. When the scene is active it walks the frame-driver's scene list
(each entry's node[2] is a per-view draw object), runs `Renderer_SetupCameraAndFrustum` then
`Renderer_DrawScene_Fork` per view, then calls `Renderer_Present`. On a failed `Present` it
runs `TestCooperativeLevel` recovery and sleeps 1 000 ms. When the scene is inactive it merely
`Clear`s, `BeginScene`, then `EndScene`.

---

## 9. Open questions and absences proven

> **Wave-5 reconciliation (2026-06-28):** Q#1 and Q#2 below were both resolved by static
> analysis of `Renderer_InitCelGlowShaders` documented in
> `Docs/RE/specs/post_processing.md`. See §2 of that spec for the full ten-slot identity
> table and §3 for the five shader-handle identities.

| Item | Status |
|---|---|
| Per-slot identity of `glow_chain` (+0x2B9DC..+0x2BA00) — **Q#1** | **RESOLVED (static).** Full ten-slot identity table (slot 0 = toon-ramp LUT, slots 1–3 = TEX0/TEX1/TEX2 textures, slots 4–6 = surfaces, slots 7–9 = render-to-surface helpers) in `Docs/RE/specs/post_processing.md §2`. |
| Per-slot identity of `device_com_slot_*` (+0x2B6D4/+0x2B6D8/+0x2B890/+0x2B894/+0x2B898) — **Q#2** | **RESOLVED (static).** Five shader handles: glow-blur PS, composite PS, cel VS, cel PS normal, cel PS stealth. Full slot-to-handle mapping in `Docs/RE/specs/post_processing.md §3`. |
| Unmapped region +0x2B740..+0x2B88F (~336 bytes) | Not walked this pass; likely holds further cached scalars or handles. |
| Unmapped gap +0x2B6EC (16 bytes) | Between `display_mode` and `present_params`; contents unknown. |
| True object end | The confirmed last field (+0x2BA3C, 4 bytes) implies a minimum extent of +0x2BA40 (178 752 bytes). The actual object boundary has not been derived from the init-guard span. |
| 18-slot GRenderState cache | **PROVEN ABSENT** on this object. Resides on the per-view/cull machinery. |
| Texture-stage / sampler shadow-cache array | **PROVEN ABSENT** on this object. States are set imperatively per frame (§7). |
| View-matrix major-order and world up-axis | DEBUGGER-PENDING; see `Docs/RE/specs/rendering.md §2.2`. |
| `D3DTS_PROJECTION` vs. `D3DTS_WORLD` naming | The engine's non-view transform setter passes state 3, which is the `D3DTS_PROJECTION` enum value in the D3D9 SDK. Treat the literal state index per call site; the engine's internal naming does not match the `D3DTRANSFORMSTATETYPE` semantics. (Static-hypothesis.) |

---

## 10. Cross-references

| Topic | Authoritative source |
|---|---|
| Per-pass render-state matrix, glow/bloom chain, UI blend model | `Docs/RE/specs/rendering.md` |
| Frame orchestration, multi-view loop, direct and offscreen draw orders | `Docs/RE/specs/render_pipeline.md` |
| Ten-slot glow_chain COM array identity (Q#1) and five shader-handle slot identity (Q#2) | `Docs/RE/specs/post_processing.md` (§2 and §3 respectively) |
| Per-view draw object (`Diamond::GView`) full layout | `Docs/RE/structs/gview.md` |
| Frame-driver object layout | `Docs/RE/structs/render_driver.md` |
| Runtime singleton table (all Meyers singletons; `Engine` at §3.11) | `Docs/RE/structs/runtime_singletons.md` |
| Cel vertex-shader and composite/glow shader constants | `Docs/RE/formats/shaders.md` |
| Scene-graph class hierarchy and cull/draw mechanism | `Docs/RE/structs/scene_graph_nodes.md` |
| Skinning, deform skeleton, camera idle motion | `Docs/RE/specs/skinning.md` |
