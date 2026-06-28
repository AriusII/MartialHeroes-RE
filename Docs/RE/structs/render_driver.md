---
verification: static-confirmed; complete 56-byte (0x38) layout recovered from the
  constructor, destructor chain, and four frame-spine routines
  (Engine_RunSceneLoop, Engine_DeviceStepAndPresent, Device_TestCooperativeLevelAndRecover,
  Engine_FrameRateLimiter). Two fields remain [needs-confirm] pending a live ?ext=dbg session
  (see §5). Corrects and supersedes specs/render_pipeline.md §1.1 on two points:
  (a) field +0x18 is the embedded std::list view-count (_Mysize), not an opaque "active flag";
  (b) the driver object carries no vtable.
ida_anchor: f61f66a9ae0ec1e946105b2ecff76e8930cb1d1367df64e5688a5266f5ad9963
evidence: [static-ida]
conflicts: specs/render_pipeline.md §1.1 documents only four fields and carries two errors
  corrected here. This file is the authoritative struct reference; §1.1 should be updated to
  cross-link here rather than consulted independently for the driver layout.
status: implementation-ready (except [needs-confirm] items in §5)
---

# EngineSceneMachine (render_driver) — Frame Driver Struct — Clean-Room Specification

> **Neutral, rewritten specification. No decompiler identifiers, no binary addresses, no
> pseudo-code.** Promoted from dirty-room analysis notes under EU Software Directive
> 2009/24/EC Art. 6, solely to document the object layout sufficient for clean-room
> reimplementation. Field offsets are **relative to the start of the object** — they are
> struct offsets, not memory addresses, and must never be treated as virtual-address
> constants in the reimplementation.
>
> **Confidence vocabulary used in this document:**
> - **CODE-CONFIRMED** — value or layout recovered from the binary instruction stream and
>   corroborated by multiple use sites.
> - **PLAUSIBLE** — consistent single-source inference; implement but keep tunable.
> - **UNVERIFIED** — hypothesis only; do not hard-code.
>
> **Scope.** This file documents the `EngineSceneMachine` struct (also referred to as the
> `render_driver` object in `specs/render_pipeline.md §1`): its complete 56-byte field
> layout, the embedded MSVC `std::list<RenderView*>` controller, the 12-byte list-node
> shape, the device-reset callback mechanism, and the frame-rate/device-lost bookkeeping.
> It corrects two errors in `specs/render_pipeline.md §1.1` (field semantics at +0x18
> and the absence of a vtable).
>
> **What this file is not.** It does not restate the full frame-orchestration behaviour —
> for that, see `specs/render_pipeline.md §1–§1.3` and `specs/game_loop.md`. The
> render-view descriptor object (the payload at list node+8) is documented in
> `specs/render_pipeline.md §7`.

---

## 1. Object identity and lifetime

The `EngineSceneMachine` is the engine's top-level **frame driver and scene-state machine
object**. It owns the circular list of render views processed each frame, the cached
renderer pointer, the input/window context, the frame-rate-limit target, the last
Present HRESULT, and the two device-reset callback hooks.

**Singleton pattern.** The object follows the Meyers singleton idiom described in
`structs/runtime_singletons.md §1`: the accessor `Engine_GetDriverSingleton` tests a
one-shot init guard, runs `EngineSceneMachine_Construct` in-place exactly once, and
registers an `atexit` destructor. The static storage for the object sits in the binary's
global-data region. The Meyers init guard is located immediately after the object's
56-byte span.

**Multi-instance note (resolves specs/render_pipeline.md §13 item 4).** The
`EngineSceneMachine` class is instantiated in two contexts:

1. **Global singleton** — accessed through `Engine_GetDriverSingleton`; used by the
   opening, character-select, and in-game scenes. The view-registrar routine always
   targets this instance, so all registered render views accumulate in its list.
2. **Embedded in scene-window objects** — confirmed for the login scene, which runs its
   per-frame loop on a private instance embedded within the login window object. This
   private instance operates an independent loop but does not participate in the global
   view list.

**Constructor** (`EngineSceneMachine_Construct`): calls `EngineSceneMachine_InitFields`
on the +0x10 sub-region to initialise the embedded `std::list` controller (allocates
the circular sentinel node into +0x14 and zeroes the view count at +0x18), then writes:
+0x04 = renderer singleton pointer; +0x08 = input/window context pointer; +0x1C = 0
(display mode, overwritten by the window-create path); +0x2C = 0 (last HRESULT); +0x30
= 60.0f (frame-rate cap); +0x20 = 1 (bool latch); +0x34 = 0.0f (frame delta). Fields
+0x0C (HINSTANCE) and +0x24/+0x28 (device-reset hooks) are **not set by the
constructor** — see §5.

**Destructor** (atexit-registered), in order:
1. `Engine_ShutdownDriver` — stops the DirectInput keyboard thread and closes the main
   window via the +0x08 input/window context.
2. View-list teardown and VFS unmount — drains the input/window context's deferred input
   queues; walks the +0x14 circular list and invokes each view descriptor's virtual
   destructor (`vtable[0](descriptor, 1)`); runs `std::list` cleanup on the +0x10
   controller; closes the VFS data handle and frees the VFS table-of-contents.
3. A second `std::list` clear pass on the +0x10 controller.

**Per-frame loop** (`Engine_RunSceneLoop`): while the global run flag is set — pump
input and Win32 messages via the +0x08 context; call `Engine_DeviceStepAndPresent(this)`;
call `FrameTickScheduler_TickAll` (a separately-cached scheduler global, not a field of
this struct — see §4); call `Engine_FrameRateLimiter(this, *(this+0x30))`.

**Device step** (`Engine_DeviceStepAndPresent`): if the view count at +0x18 is non-zero
and the last HRESULT at +0x2C is zero, walk the circular list from the sentinel at +0x14
— for each view node call `Renderer_SetupCameraAndFrustum` then `Renderer_DrawScene_Fork`
on the view descriptor at node+8 — then call `Renderer_Present` once on the +0x04
renderer, store the HRESULT into +0x2C, and on failure call
`Device_TestCooperativeLevelAndRecover`. If +0x18 is zero (no views registered), only
`GDevice_ClearBackbufferDefault` + `EndScene` run — no list walk, no Present.

**Device-lost recovery** (`Device_TestCooperativeLevelAndRecover`): tests the D3D device
cooperative level; on `D3DERR_DEVICENOTRESET` calls the reset-dispatch trampoline, which
fires the pre-reset hook (+0x28), calls `Renderer_ResetD3DDevice`, then fires the
post-reset hook (+0x24); on `D3DERR_DEVICELOST` the caller sleeps 1 000 ms and retries
on the next iteration.

---

## 2. Full field layout (total size: 56 bytes)

Cross-reference: `specs/render_pipeline.md §1.1` (predecessor four-field summary,
superseded by this table); `specs/game_loop.md` (frame-loop control flow).

| Offset | Size | Type | Field | Notes |
|-------:|-----:|------|-------|-------|
| +0x00 | 4 | reserved | (unused) | No constructor write or frame-spine read observed; holds zero throughout. The object carries **no vtable** — the constructor writes no function pointer here and teardown routes through concrete (non-virtual) trampolines. Reserved to preserve the +0x04 renderer-pointer alignment. CODE-CONFIRMED (absence of vtable confirmed by constructor analysis). |
| +0x04 | 4 | ptr | `renderer` | Pointer to the `GHRenderer` / GDevice singleton (see `structs/runtime_singletons.md §4`). Holds the cached `IDirect3DDevice9` at a deep offset within that object. Target of the per-frame `Renderer_Present` call and of the device clear in the no-view branch. CODE-CONFIRMED. |
| +0x08 | 4 | ptr | `input_window_ctx` | Pointer to the engine's **input/window context** singleton — owns the Win32 message pump, the DirectInput keyboard thread, the main HWND, and the per-scene dispose queue. Shutdown stops its keyboard thread and closes the window; used as the message-pump argument each frame. CODE-CONFIRMED. |
| +0x0C | 4 | HINSTANCE | `app_instance` | Application `HINSTANCE` handle. Written by the window-create path from the `WinMain` `hInstance` argument; read when registering the window class and creating the main window. Not set by the constructor. CODE-CONFIRMED (write site traced). |
| +0x10 | 4 | std::list controller slot | `list_ctrl_base` | Leading slot of the embedded **MSVC `std::list<RenderView*>`** controller (allocator/proxy slot; left zero by the initialiser). The list size-bump helper addresses the controller at this base; `_Mysize` is at +0x18. CODE-CONFIRMED (MSVC list identified by its `0x3FFFFFFF` size guard and "list<T> too long" throw). |
| +0x14 | 4 | node ptr | `list_head` (`_Myhead`) | Pointer to the circular list **sentinel node** — the head of the per-frame render-view list. The device step walks from `sentinel->_Next` around until it returns to the sentinel. Initialised by `EngineSceneMachine_InitFields`. CODE-CONFIRMED. |
| +0x18 | 4 | uint32 | `view_count` (`_Mysize`) | Number of currently registered render views. **This is the active gate for the per-frame walk**: the full list walk + single Present runs only when this is non-zero; otherwise only Clear + EndScene run. Guarded against `0x3FFFFFFF` with a "list<T> too long" throw. **Corrects `specs/render_pipeline.md §1.1`**, which labelled this field "active flag" — the gate behaviour is the same but the field is the list's element count. CODE-CONFIRMED. |
| +0x1C | 4 | int32 | `display_mode` | Window/display mode: 1 = windowed at the requested size; 2 = fullscreen/borderless. Resolved from the `game.lua` configuration and written by the window-create path; drives the `CreateWindow` style flags and the D3D device sub-mode. CODE-CONFIRMED (write site traced). |
| +0x20 | 1 (+3 pad to +0x24) | bool | `ready_latch` | Boolean latch, **initialised to 1 (true)** by the constructor. No read site was located along any of the four traced frame-spine routines (run loop, device step, recovery, window-create, device-init). Provisional role: a device-or-loop-ready latch read by a path not yet traced. `[needs-confirm]` — see §5. PLAUSIBLE. |
| +0x24 | 4 | fn ptr | `post_reset_hook` | **Post-device-reset callback**. Fired with no arguments *after* a successful `Renderer_ResetD3DDevice` call (intended use: re-create device-dependent resources). Not set by the constructor; writer not located on the traced spine. `[needs-confirm: writer]` — see §5. PLAUSIBLE. |
| +0x28 | 4 | fn ptr | `pre_reset_hook` | **Pre-device-reset callback**. Fired with no arguments *before* the `Reset` call (intended use: release device-dependent resources). Fired first by the reset-dispatch trampoline, before `Renderer_ResetD3DDevice`, then `post_reset_hook` is fired. Not set by the constructor; writer not located on the traced spine. `[needs-confirm: writer]` — see §5. PLAUSIBLE. |
| +0x2C | 4 | HRESULT | `last_present_result` | Last result code from `Renderer_Present` or the cooperative-level test. Non-zero short-circuits the next frame into the recovery branch. `D3DERR_DEVICELOST` → sleep 1 000 ms + retry; `D3DERR_DEVICENOTRESET` → reset-dispatch trampoline. Initialised to 0 (S_OK) by the constructor. CODE-CONFIRMED. |
| +0x30 | 4 | float | `fps_cap` | Frame-rate cap in frames per second. **Seeded to 60.0f by the constructor and never overwritten elsewhere** — the cap is effectively fixed at ~60 FPS for this build. Passed to `Engine_FrameRateLimiter` each frame as the target. CODE-CONFIRMED (exhaustive static scan of all write sites confirmed only the constructor write; see `specs/client_runtime.md` for the FRAMERATE config inertness finding). |
| +0x34 | 4 | float | `frame_delta` | Last measured frame delta in seconds. Written every frame by `Engine_FrameRateLimiter` (QPC elapsed / QPC frequency); the limiter then sleeps the remainder of 1/`fps_cap`. The persistent QPC last-tick timestamp is a **separate companion global**, not a field of this struct (see §4). CODE-CONFIRMED. |

---

## 3. Embedded render-view list node (MSVC `std::list` node, 12 bytes)

Each node in the render-view list is a standard MSVC doubly-linked list node. The list
is circular: the last node's `_Next` points back to the sentinel at driver +0x14, and the
sentinel's `_Prev` points to the last node. The sentinel itself carries no view payload.

Cross-reference: `specs/render_pipeline.md §7` for the GView render-view descriptor
layout (the per-view object the device step operates on).

| Offset | Size | Type | Field | Notes |
|-------:|-----:|------|-------|-------|
| node+0 | 4 | node ptr | `_Next` | Next node in the circular list; the last content node points back to the sentinel at driver +0x14. CODE-CONFIRMED. |
| node+4 | 4 | node ptr | `_Prev` | Previous node in the circular list. CODE-CONFIRMED. |
| node+8 | 4 | RenderView ptr | `_Myval` (view ptr) | Pointer to the **GView render-view descriptor** — the per-view object on which `Renderer_SetupCameraAndFrustum` and `Renderer_DrawScene_Fork` are called each frame. The descriptor is **polymorphic**: teardown invokes its `vtable[0](descriptor, 1)` (MSVC vector-deleting destructor). Resolves `specs/render_pipeline.md §13 item 5` ("descriptor vtable at +0 unconfirmed"). CODE-CONFIRMED. |

Views are registered into the global singleton's list by the view-registrar routine: it
calls `Engine_GetDriverSingleton()`, allocates a 12-byte node, populates `_Next`/`_Prev`/
`_Myval`, links the node before the sentinel, and increments `view_count` at +0x18. The
view registrar always targets the global singleton — private instances embedded in
scene-window objects are not targeted.

---

## 4. No-vtable note and companion globals

**No vtable.** The `EngineSceneMachine` object is a plain struct — not a Diamond
scene-graph node and not a polymorphic class. Its constructor writes no function pointer
to +0x00, and all teardown and recovery dispatch through concrete (non-virtual) trampolines.
The two function pointers the struct *holds* (+0x24 post-reset hook, +0x28 pre-reset hook)
are device-reset callbacks called directly, not virtual methods. The polymorphism in this
subsystem resides in the **GView render-view descriptor** at list node+8, whose vtable
slot 0 is the vector-deleting destructor — that is a property of the descriptor object,
not of the driver.

**Companion globals used by the frame loop (not driver fields).** Several globals in
the binary's data region work alongside the driver struct but are not part of its 56-byte
span:

- **QPC last-tick timestamp** — the frame-rate limiter's persistent 64-bit performance
  counter value, used each frame to compute elapsed time. It is a global in the data
  region, not a field of this struct; the per-frame delta stored at driver +0x34 is
  derived from it.
- **FrameTickScheduler cache pointer** — a global pointer caching the `FrameTickScheduler`
  singleton (see `structs/runtime_singletons.md §3.12`). `Engine_RunSceneLoop` calls
  `FrameTickScheduler_TickAll` through this cache after each device step.
- **Main-loop init guard** — a 1-bit guard controlling the one-time population of the
  `FrameTickScheduler` cache; gated on first entry to the run loop.

These three globals are located immediately before the driver object in the data segment.
None are driver-struct fields; a C# reimplementation should hold them as separate fields
on the enclosing loop-controller class.

---

## 5. Open items (`[needs-confirm]` — live ?ext=dbg session required)

The following two static-hypothesis items must be confirmed in a live `?ext=dbg` session
before treating them as implementation facts.

1. **`ready_latch` at +0x20.** Initialised to `true` by the constructor; no read site
   found along the run loop, device step, recovery, window-create, or device-init paths.
   Likely read by a code path not yet traced (possibly an early-exit, pause, or
   device-init gate). Resolve the reader before relying on the field. Until confirmed,
   model it as a `bool` initialised to `true` with role unknown.

2. **Device-reset hooks at +0x24 (`post_reset_hook`) and +0x28 (`pre_reset_hook`).** Both
   are called by the reset-dispatch trampoline (triggered from
   `Device_TestCooperativeLevelAndRecover` on `D3DERR_DEVICENOTRESET`), but neither is
   set by the constructor and no writer was found on the traced spine. They may be
   installed by the device-create or scene-setup path (analogous to how the
   view-registrar installs default callbacks into descriptors). In the live `?ext=dbg`
   session, place a breakpoint on the reset-dispatch trampoline and inspect driver +0x24
   and +0x28 to confirm they are non-null and to identify their target functions. If both
   are null at reset time, the device-lost recovery path performs no resource
   teardown/recreation on this build.

---

## Cross-references

| Topic | Location |
|---|---|
| Frame orchestration, multi-view loop, per-frame call sequence | `specs/render_pipeline.md §1–§1.3` |
| GView render-view descriptor layout (the object at list node+8) | `specs/render_pipeline.md §7` |
| Frame-loop phases and `FrameTickScheduler` dispatch | `specs/game_loop.md §3` |
| `GHRenderer` / GDevice object layout | `structs/runtime_singletons.md §4` |
| `FrameTickScheduler` singleton layout | `structs/runtime_singletons.md §3.12` |
| Meyers singleton idiom used throughout the client | `structs/runtime_singletons.md §1` |
| Device-lost recovery full lifecycle | `specs/rendering.md §2.0.2` |
| FRAMERATE config inertness (60 FPS cap confirmed static) | `specs/client_runtime.md` |
