---
verification: confirmed (re-confirmed against IDB SHA 263bd994, CYCLE 7 (2026-06-20)); CYCLE 14
  re-anchor (f61f66a9, 2026-06-27, static IDA only): 3 facts re-confirmed SAME (bootstrap order
  before dispatch loop, 4-phase per-frame loop with do-while run-flag, 15-slot font table), 0
  corrected; behaviour unchanged across the build delta
ida_reverified: 2026-06-27
ida_anchor: f61f66a9ae0ec1e946105b2ecff76e8930cb1d1367df64e5688a5266f5ad9963
evidence: [static-ida]
conflicts: none — the display FRAMERATE config (DISPLAY_FRAMERATE in display.lua) is RESOLVED as statically inert (parsed into a renderer field with two writers and zero readers; never reaches the limiter); the per-frame cap is a hardcoded 60.0 (CYCLE 7)
status: confirmed
sample_verified: false
---

# Game Loop & Timing — Clean-Room Specification

> Neutral, rewritten behavioural specification. No legacy symbols, no addresses,
> no pseudo-code. Describes the *observed behaviour* of the legacy client's main
> loop and time management so the .NET core can be reimplemented from scratch.
> Scope: main loop + logic/render decoupling + clock. Non-network.

> **Status upgrade (Campaign 9D / re-confirmed Campaign 10).** The per-frame loop order, the
> boot/bring-up sequence, and the FPS cap were re-derived directly from binary control-flow logic.
> Sections 0 (bootstrap) and 7 (4-phase order) below are **CODE-CONFIRMED**. Three
> prior statements are corrected: (1) the loop order is **four** phases (not the §1
> three-phase `pump → render → tick`); (2) the present path is **not** vsync-capped
> — there is an explicit software FPS throttle (see §0.5 and §7); and (3) **the throttle's
> cap rate is a hardcoded 60.0 FPS** read from the engine scene-machine driver object's
> target-frame-rate field (offset +0x30) — seeded once to the float `60.0` in that object's
> constructor and never overwritten — **NOT** the `DISPLAY_FRAMERATE` display-config value
> (Campaign-10 correction; the config is **RESOLVED dead — two writers, zero readers — in CYCLE 7**;
> see §0.5, §0.6, §5, §7.4).
> The detailed scene state machine that re-enters this loop once per screen lives in
> `specs/client_runtime.md §7` and `§"Front-end scene state machine"`.

## 0. Bootstrap and main-loop bring-up — (CODE-CONFIRMED)

Before the per-frame loop can run, the application entry point performs a one-time
bootstrap. The CRT runtime startup runs the MSVC static initialisers and then calls
the application entry point; the CRT portion carries no game substance. Everything
below is the application entry point body, in this exact order, and runs exactly **once**
regardless of which scene state is current. (The entry point body *is* the scene state
machine — see `specs/client_runtime.md §7` and its "Front-end scene state machine"
section; this section covers only the engine bring-up that precedes the state loop.)

### 0.1 Bootstrap order

1. **Keep-awake.** A keep-display-awake request is issued so the monitor and system do
   not sleep while the game runs.
2. **Termination handlers.** Two C++ `terminate`-handler registrations (housekeeping).
3. **Boot config — `game.lua`.** The Lua-config singleton loads `game.lua` and reads
   three boolean keys. They are read **only if the load reported success**; otherwise all
   three keep a pre-seeded default of **true**:
   - `vfsmode` — file-source select (packed VFS archive vs. loose files).
   - `launcher` — launcher-gate flag.
   - `debugmode` — developer-mode flag.
   The `debugmode` value is stored into the engine-state struct (the byte read later to
   decide windowed-vs-full and to gate developer overlays). The `vfsmode` value is written
   to the global mount-mode flag via a one-line setter.
4. **Developer trace scopes** are opened (a "do" / "winmain start" profiling marker).
5. **Launcher gate.** If `launcher` is on **and** the command line is **not** exactly
   `-Start`, the game does **not** run the state machine: it shells out to the external
   launcher program and returns. If that shell-out fails it shows a message box and returns.
   So a launcher-mode client without `-Start` re-launches the external updater and exits;
   the state loop runs only for the real (post-launcher) game process.
6. **VFS mount.** Once, immediately before the state loop: the index file is opened, a
   24-byte header is read, its entry count is taken, a table-of-contents array is allocated
   at 144 bytes per entry and read in full, the index file is closed, and the data archive
   is opened and its OS handle retained for the process lifetime. (Mount byte-mechanics are
   owned by `specs/resource_pipeline.md §1.5.1`; the on-disk container by `formats/pak.md`.)

After the bootstrap the state loop begins. States 0 and 1 perform the engine/device
bring-up (§0.2–§0.6); every later state constructs its own scene object and re-enters the
same per-frame loop (§7).

### 0.2 Display sizing (state 0)

State 0 reads the display-config singleton (fields: width, height, display-mode). If the
display-mode is the fullscreen value, the width is taken from the screen-metric and clamped
to a maximum of **1920**, and the height resolves from the screen-metric clamped to **1200**.
Otherwise (windowed) the configured width/height are used (a value of 0 means "use the screen
metric", with the same 1920×1200 clamps). A frame-interval global (a ~16 ms / 60 Hz interval
value) is set to **16** here; it is **write-only and dead** — its sole cross-reference in the
whole binary is this one write, so there is **no reader at all** (it does **not** feed the
frame throttle — see §5 and §7.4). State 0 then advances to state 1.

### 0.3 Window creation (state 1)

The engine registers one window class named **"diamond engine application"** with a window
proc, a default arrow cursor/icon, and a null background brush; the window caption text is
**"Do"**. The window style depends on the windowing argument (derived from display-mode and
`debugmode`): a borderless/popup style family for the fullscreen-ish path, an overlapped-popup
family otherwise; the topmost extended style is added for one windowed sub-mode. The client
rect is built from the renderer's display width/height, run through the OS adjust-rect call,
and the window is centred on the screen metrics. After creation the window is shown, updated,
focused, the cursor is shown, and the window handle is stored to a global. A main-thread sync
event is created (failure → message box "CreateEvent() is failed in main thread" then exit),
and a worker thread is started with a set priority. For the sized windowed sub-mode the window
is additionally repositioned to the configured size. A device-init wrapper, for the windowed
sub-modes, overwrites the window style to a visible-only value, stripping the caption/border
to give a **borderless windowed** surface.

### 0.4 D3D9 device creation (state 1)

The real device creator runs in this order:

1. Loads `data/script/display.lua` and a large character-brightness config table (§0.6).
2. For a true display-mode-change fullscreen path: builds a display-settings descriptor from
   the renderer's stored bpp/width/height and changes the display settings; clears the
   "windowed" flag. Otherwise sets the "windowed" flag.
3. Creates the Direct3D 9 object (SDK version **32**); on null it logs a create-fail string and
   fails.
4. Reads the adapter's current display mode; on failure it logs and fails.
5. **Back-buffer format selection** by walking a candidate format list with a device-type check:
   for 16-bpp a 3-entry 16-bit list (R5G6B5 / X1R5G5B5 / A1R5G5B5); otherwise a 2-entry 32-bit
   list (X8R8G8B8 / A8R8G8B8). The first supported format is taken.
6. Further format/capability sweeps probe **texture** support (depth lists and the DXT1/DXT3/DXT5
   compressed FourCCs) and cache the results; these are capability caches, **not** present-params.
7. The present-parameters block (56 bytes) is zeroed and filled (§0.5).
8. The device is created (adapter 0, HAL, software vertex processing); on failure a specific
   error string is logged.
9. A 2D sprite/UI batcher is created (`D3DXCreateSprite`); on failure it logs.
10. Render-state setup runs; a display-config flag may set a mode field. Returns success.

### 0.5 D3D9 present parameters and the FPS-cap implication — (CODE-CONFIRMED)

The present-parameters block is filled as follows (the format/flag *names* are the standard SDK
decode of the written values). The individual field values below were **not** re-read
byte-for-byte in the Campaign-10 re-confrontation; they are carried at their prior CODE-CONFIRMED
status. The downstream implication (the cap is the §7.4 software throttle, vsync irrelevant) is
independently re-confirmed via §7.4.

| Present-parameters field | Value set | Note |
|---|---|---|
| Windowed | the "windowed" flag (set unless the display-change fullscreen path ran) | windowed by default |
| BackBufferCount | **1** | |
| EnableAutoDepthStencil | **TRUE** | |
| SwapEffect | **DISCARD** | value 1 written |
| **PresentationInterval** | **IMMEDIATE** | the high-bit immediate value written → **vsync OFF** |
| MultiSampleType | none | block zeroed, never re-set |
| AutoDepthStencilFormat | default **D16**; in the windowed branch overridden to the first supported of {D24S8, D24X8, D16-class} via a format check | |
| BackBufferFormat | the format selected in §0.4 step 5 | |
| BackBufferWidth / Height | the renderer's stored display width / height | |
| hDeviceWindow | the main window handle | |
| Flags | 0 (cleared) in the windowed branch | |

Because the present interval is **IMMEDIATE**, the frame rate is **not** vsync-locked. The cap is
instead the §7.4 software throttle, and its rate is a **hardcoded 60.0 FPS** read from the engine
scene-machine object's framerate field (§7.4) — **not** the `DISPLAY_FRAMERATE` display-config
value. This resolves the §5 vsync question.

### 0.6 Boot-time catalogues and config files — (CODE-CONFIRMED)

Loaded during bootstrap / bring-up, with the phase each is loaded in:

| File | Phase | Role |
|---|---|---|
| `game.lua` | bootstrap top | `vfsmode` / `launcher` / `debugmode` (§0.1) |
| index + data archive (the VFS) | bootstrap, once before the loop | VFS mount (§0.1) |
| `data/script/msg.xdb` | state 1, before window | localized message catalogue; records of **516 bytes** each, each id registered into a message map |
| `data/script/display.lua` | state 1, during device init | renderer config: `DISPLAY_FRAMERATE` (read with the integer config getter and stored **raw** into a renderer field — **no zero-default is applied at the read site**, and **no code path reads that field back to feed the frame throttle**, so as observed statically it is **inert**; the §7.4 cap is a hardcoded 60.0 from the engine ctor, not this value), `DISPLAY_GLOW_RANGE_X/Y` (default 2 each, applied only here if the read returns 0), `DISPLAY_POWERSHADER` (a shader path), and a large `DISPLAY_CHAR_BRIGHT_*` per-character-state R/G/B brightness table plus base/glow/light-ratio multipliers |
| `OPENNING` / `SKIP` (an INI key) | state 2 | whether to skip the Opening scene |
| effect manifests (`bmplist.lst`, `xobj.lst`, `xeffect.lst`, `effect.cache`, `totalmugong.txt`, joint/sword-light tables) | state 2+ (LoadingWindow boot thread) | the large effect/data-table corpus is loaded **behind the loading screen on the boot thread, NOT at first window bring-up** |

> **Effect-asset boot timing — corrected.** The effect manifests are **not** loaded during the
> first window/device bring-up. During state-1 bring-up the effect manager is only warmed/flushed
> (a free-list flush of its pooled texture handles). The manifest corpus is loaded later, on the
> LoadingWindow background boot thread (state 2 onward). See `specs/resource_pipeline.md §2` and
> `specs/effects.md §3` for the corpus contents.

### 0.7 Engine / singleton init order (state 1) — (CODE-CONFIRMED)

In order: acquire the engine scene-machine object and the network-handler singleton → load
`msg.xdb` (§0.6) → construct the first front-end window (LoginWindow) and register it for teardown
→ a one-time scheduler/thread-pool init that gates window/device (failure → teardown + exit) →
create the OS window (§0.3) → create the D3D9 device (§0.4) → a post-window fixup and scheduler
release → register the **15 font slots** (§0.8) → warm/flush the effect manager (§0.6) → invoke the
first window's show virtual and enter the per-frame loop (§7) for the login scene. On loop return:
window teardown, another effect-manager flush, a final scene detach, and a read of the
addiction-warning interval config. The first per-scene loop therefore runs inside state 1 for the
first front-end window; each later state constructs its own scene and runs its own instance of the
same loop.

### 0.8 The 15-slot font table — (CODE-CONFIRMED)

15 fonts (slots 0..14) are registered once at boot on the font-table singleton, each created from a
Korean system typeface (DotumChe / Dotum / BatangChe) with the Hangul charset. This is the
authoritative text path; there is no glyph atlas in the VFS for body text. The full per-slot
face/size/weight table is owned by `specs/ui_system.md §6.2` (and summarised in
`specs/resource_pipeline.md §"Boot font load"`). The faces are CP949-rendered Korean Gothic/Batang
typefaces.

## 1. Overall loop architecture

The client reuses a single engine main loop across every interactive screen
(login, opening, character-select, in-game). When a screen becomes active it
builds its handler object, registers it as the loop's event target, and runs the
same loop; on exit it tears the handler down. So the loop body is identical
regardless of screen.

The loop runs while a run flag (a global boolean) is set. Each iteration performs
its top-level steps always in the same order:

1. **Message pump + deferred input** — drain pending Win32 window messages, then
   swap and drain a double-buffered input-event queue (each queued event dispatched to
   the active scene by event type — §7.1).
2. **Scene update + render + present** — update the active scene and draw exactly
   one frame (via one of two render paths — §7.2), ending with the back-buffer present.
3. **Logic tick** — service the per-subscriber tick scheduler.
4. **Frame throttle** — a software FPS cap (sleep the remainder of the target frame),
   measured with the high-resolution counter at a **fixed 60 FPS** target (§7.4).

> **Order correction (CODE-CONFIRMED, Campaign 9D).** The three-step `pump → render →
> tick` shape this section originally described is superseded. The byte-exact order is
> the **four phases** above — **pump-input → (scene-update + render + present) → logic
> tick → frame throttle** — with an explicit fourth throttle step the earlier reading
> omitted, and with render issued *inside* the scene-update phase rather than as a bare
> second step. The authoritative description is **§7** below; §1–§4 are retained as the
> original behavioural notes and are consistent with §7 except for this step count.

A WM_QUIT (the message pump observing that the message queue has signalled
shutdown) clears the run flag, which lets the loop exit cleanly. The window
procedure posts the quit message on the window-close path.

### Message pump shape

The pump tests the queue with a non-removing peek (`PeekMessage` with the
no-remove option) and, when a message is present, removes and processes it with a
blocking `GetMessage`, then translates and dispatches it. Key points:

- The peek is only a *test*; the actual removal/dispatch is done by `GetMessage`.
- When the peek reports the queue is **empty**, the pump simply returns. There is
  no work done inside an "else" branch of the pump.
- Because the pump just returns on an empty queue, the loop falls straight through
  to **render** and then **logic tick**. The engine core advances on the loop body
  itself, not inside the pump.

So the iteration is `pump → scene-update/render/present → tick → throttle`, repeated.
Render and the logic tick both run **every** iteration; rendering is never gated on
the message queue, only on the §7 frame throttle.

## 2. Logic / render decoupling

The render step and the logic step are two separate calls, giving a structural
decoupling between presentation and simulation.

- **Render** is pure presentation with **no delta-time math**. It walks the active
  scenes, updates each scene's camera and culling, draws each scene via **one of two
  render paths** (an offscreen render-target path or a direct-draw path, selected per
  draw by a renderer flag — see §7.2), presents, and then handles D3D9 device-lost
  recovery. The recovery path tests the device cooperative level and compares the
  result against two device-status HRESULTs — a *device-not-reset* status (which triggers
  a device reset) and a *device-lost* status (still lost; the loop waits ~1000 ms and
  retries next frame). The non-scene branch issues a default clear, a begin-scene,
  and a draw call. Render runs **unconditionally every iteration** — there is no
  per-frame time gate on rendering.
- **Logic** is the scheduler's "tick all subscribers" pass (see §3). It samples the
  clock once per iteration and pulses only the subscribers whose interval has
  elapsed.
- **No interpolation factor** (no alpha/blend between simulation states) was
  observed in the loop body. The decoupling is by per-task interval, not by a
  fixed-step accumulator with interpolation.

> **Correction (CODE-CONFIRMED, Campaign 9D; cap-rate corrected Campaign 10).** This
> paragraph's "no frame-rate cap; vsync UNVERIFIED" reading is **wrong** and is superseded
> by §7.4. There **is** an explicit software FPS cap (a fourth loop phase that measures real
> elapsed time with the high-resolution counter and sleeps the remainder of the target frame
> interval), and the present interval is **IMMEDIATE — vsync is OFF**. The cap rate is a
> **hardcoded 60.0 FPS** (seeded into the engine scene-machine object's framerate field by the
> engine constructor and never overwritten) — **not** the `DISPLAY_FRAMERATE` config value, and
> not vsync. The text below is retained only as the original (incorrect) note.

~~There is **no explicit frame-rate cap or throttle Sleep** in the normal loop path
(the only Sleep observed is the device-lost recovery wait). Whether the present
path uses vsync as a de-facto FPS cap is **UNVERIFIED** (present parameters not
inspected).~~

## 3. Tick model — per-subscriber threshold scheduler (NOT fixed-step accumulator)

The logic time model is a **subscriber scheduler with per-task fixed intervals
gated on a millisecond clock**. It is **not** a single global fixed-step
accumulator and **not** a raw whole-frame delta applied to everything.

Per iteration the scheduler:

- Samples the current time in milliseconds once and caches it.
- Holds a table of registered tick subscribers and a round-robin cursor.
- Services only a **subset** of subscribers each frame — it advances the cursor by
  roughly `floor(subscriber_count * 0.011)` (about **1.1 %** of subscribers per
  frame), with a "full sweep next frame" override flag available. Subscribers are
  thus amortised across many frames in round-robin order rather than all pulsed
  every frame.

Each tick subscriber carries this state:

| Field | Meaning |
|---|---|
| `enabled` | boolean — subscriber participates at all |
| `active` (not-paused) | boolean — subscriber currently runs |
| `interval_ms` | target tick period, in milliseconds |
| `last_tick_ms` | timestamp (ms clock) of its last dispatch |

A subscriber fires when (note the comparison is **strict greater-than**, not
greater-or-equal, and a zero interval disables the subscriber):

```
interval_ms != 0 AND enabled AND active AND (now_ms - last_tick_ms) > interval_ms
```

When it fires, its own dispatch runs (via the subscriber's dispatch virtual) and
`last_tick_ms` is advanced. Note this is a **threshold** comparison
(`now - last > interval`), **not** an accumulate-and-subtract loop: there is **no
leftover-time carry**. Each tickable therefore runs at roughly its own target
interval, with whatever jitter the frame rate imposes (it cannot "catch up" multiple
missed ticks in one frame).

### Observed cadence constants

- A global value **16** (≈ 16 ms ≈ 60 FPS) is **written** during early
  initialisation (state 0). It is **write-only and dead**: its only cross-reference
  in the binary is that single write, so it has **no reader** and does not gate the
  loop. The actual 60 FPS cap comes from the hardcoded `60.0` framerate field in the
  engine constructor that the §7.4 throttle reads — *not* from this global.
- The scheduler amortisation factor is **~0.011** (≈ 1.1 % of subscribers per
  frame).
- Millisecond is confirmed as the engine's time unit elsewhere (e.g. a
  seconds→ms conversion for a periodic warning timer baselined on the same clock).

### Network influence on the scheduler (out of scope, flagged)

Server messages can (re)configure tick scheduling — there is a network path that
touches the same scheduler singleton to adjust tick subscribers (a game-tick
config response and a game-state tick response). This is a **protocol concern** and
is documented only as a cross-reference; it does not belong in this timing spec.

## 4. Clock source

The logic/delta clock is a **monotonic millisecond clock** sourced from the
multimedia timer (`timeGetTime`-style), returning a 32-bit millisecond count.

- The clock value is optionally passed through a **time-scale factor**: a global
  float where `1.0` means realtime, `< 1` means slow-motion, `> 1` means
  fast-forward, with a small offset subtracted. This gives an engine-wide
  slow-mo / fast-forward capability.
- `GetTickCount` and `QueryPerformanceCounter` are **not** used for the logic
  delta. The frame/tick path uses only the millisecond multimedia clock.
- (UNVERIFIED: whether a high-resolution counter is used elsewhere, e.g. for
  profiling — not relevant to the loop.)

## 5. Residual / resolved items

- The **16** global is **write-only and dead** (a single write, no reader — CODE-CONFIRMED).
  The ~60 Hz framerate does **NOT** come from it. **Correction (Campaign 10):** nor does the
  cap come from `DISPLAY_FRAMERATE` — the §7.4 software throttle reads a **hardcoded 60.0**
  framerate field seeded by the engine constructor. (CODE-CONFIRMED.)
- **`DISPLAY_FRAMERATE` (display.lua) is dead config — RESOLVED (CYCLE 7).** It is read with the
  integer config getter and stored **raw** (no zero-default at the read site) into a field of the
  renderer/display singleton. An exhaustive static search of that field's displacement returns
  **exactly two sites, both writes** (the singleton constructor's zero-init and the config store)
  and **no reader anywhere in the binary** — so the configured framerate value has no consumer at
  all and never reaches the §7.4 throttle. The earlier "is it truly inert?" question is **closed:
  inert/dead**. The cap is the hardcoded 60.0 driver field, not this value. (A live runtime
  reconfirmation that 60.0 paces at 60 FPS would be RUNTIME-ONLY and is not needed for the verdict.)
- ~~Whether the D3D present path uses **vsync** as a de-facto FPS cap~~ — **RESOLVED
  (Campaign 9D)**: present interval is IMMEDIATE (vsync OFF); the FPS cap is the §7.4
  software throttle at a fixed 60 FPS. (CODE-CONFIRMED — see §0.5, §7.4.)
- The high-resolution performance counter **is** used: it is the timer the §7.4 frame
  throttle samples to measure inter-frame elapsed time. (CODE-CONFIRMED.)
- Exact write site of each subscriber's `last_tick_ms` (it is updated inside the
  subscriber's own dispatch, which is the only sensible writer, but the precise
  point was not pinned down — static-hypothesis).
- Whether the per-frame scheduler singleton and the now-ms-providing singleton are
  literally the same object instance — **RESOLVED (Campaign 10): they are the same object.**
  The scheduler singleton both holds the round-robin subscriber table and caches the
  once-per-frame millisecond timestamp (the cached `now_ms` the per-subscriber test reads).
  (CODE-CONFIRMED.)

## 7. Per-frame loop — the authoritative four-phase order — (CODE-CONFIRMED)

This supersedes the three-step shape of §1 for the *step count and ordering* (the §2–§4
behavioural detail of each step still applies). On first entry the loop lazily fetches and
caches the frame-tick-scheduler context; then on every entry it raises the multimedia timer
resolution to 1 ms and sets the engine run-flag. The body then runs
`do { … } while (run-flag)` with these **four** phases in this exact order:

### 7.1 Phase 1 — input / message pump + deferred input dispatch

A peek-gated message drain (`PeekMessage` → `GetMessage` / `TranslateMessage` /
`DispatchMessage`). A quit message clears the run-flag (so the loop exits after this iteration).
Then, under a critical section, a **double-buffered input-event queue** is swapped and its back
buffer drained, dispatching each queued input event to the active scene's input handler and
recycling the node. So this phase is **both** the Win32 pump and the queued-input dispatch.

**Input dispatch is by event-type bitmask, first consumer wins.** The dispatcher walks the
registered input-handler list and, for each handler, tests a bit selected by the event's type
(`1 << event_type`) against that handler's subscription mask. A handler whose mask bit is set is
invoked (through its handler virtual); the walk **stops at the first handler that reports the
event consumed** (returns nonzero). So an event is routed only to handlers subscribed to its type,
and only the first such handler that consumes it sees it.

### 7.2 Phase 2 — scene update + render + present

The scene pre-update runs; if the scene's draw gate is set, the device's per-frame
begin/scene-draw is issued (the scene's render). The back-buffer **present** is issued from this
same phase.

**Two render paths.** The per-scene draw dispatch chooses between an **offscreen render-target**
path and a **direct-draw** path, selected by a renderer flag read each draw (the offscreen-RT path
is the one that backs post-process / glow). The §2 "draws the scene" wording covers both; the fork
itself is the concrete detail.

**Per-scene pre-update** (the "update each scene's camera and culling" of §2) increments a frame
counter, type-checks the scene's camera for a perspective camera, reads its FOV/aspect, builds the
view matrix and its inverse, copies the camera basis into a shared render-globals block, and
constructs the view frustum used for culling.

**Present** is a single virtual call on the device wrapper that issues the D3D9 back-buffer present
and returns the device status, which the loop stores into the engine object's device-lost field to
drive recovery on the next iteration.

A **device-lost** path is handled here: the registered scene list releases/resets device resources,
the cooperative level is tested, and the result is compared against two device-status HRESULTs — a
*device-not-reset* status (the device is reset) and a *device-lost* status (still lost; the loop
sleeps ~1000 ms and retries next frame).

So render is issued **inside** this phase (not as a bare second step), and present happens here —
this is the correction to the §1 "render is step 2" framing.

### 7.3 Phase 3 — logic tick (frame-tick scheduler)

The scheduler's tick-all pass runs (§3): it samples the millisecond clock once, advances the
round-robin cursor by ~1.1 % of the subscriber count (unless a full-sweep flag forces all
subscribers this frame), and dispatches each selected subscriber, applying its per-subscriber
`(now − last) ≥ interval` threshold. This confirms the §3 model exactly — the scheduler is the
amortised, threshold-based logic clock with no leftover-time carry.

### 7.4 Phase 4 — frame throttle (the software FPS cap) — (cap rate RESOLVED, CYCLE 7)

The high-resolution performance counter measures the real elapsed seconds since the previous
frame. The target interval is `1.0 / rate`, where `rate` is the **target-frame-rate field
(offset +0x30) of the engine scene-machine driver object**. That field is seeded **once** as an
immediate to the float **60.0** by the driver object's constructor and is **never overwritten** by
config or anything else before or during the loop, so the cap is a **fixed 60 FPS**. The limiter
math is `Sleep((1.0/rate − measured) × 1000)` ms: if the target frame time exceeds the measured
delta, the loop sleeps the remainder, then re-baselines the counter anchor for the next frame. The
`× 1000` is a seconds→milliseconds **units conversion**, *not* a second cap. So the per-frame budget
is `1000/rate` ms ≈ **16.67 ms** at rate 60.

**The limiter caps only the upper FPS.** Busy frames where the measured delta already meets or
exceeds `1/60` skip the `Sleep` and run uncapped — the throttle does **not** pad slow frames, it
only holds the ceiling. The fixed-60 driver pacing is shared by the load, opening, character-select,
and in-game scene loops (each re-enters this same loop on the driver singleton). The login scene
runs the same loop on its own window-derived object's own +0x30 loop-rate field (a per-object loop
rate, also seeded per-object — **not** the dead display config field).

The throttle also **measures and stores the inter-frame delta** (the elapsed seconds) into a
last-frame delta-time field (offset +0x34) on the same driver object every frame. This is a real
per-frame delta the engine exposes — but note the logic scheduler (§3) does **not** use it; the
scheduler runs its own millisecond-threshold model. (No other observed consumer of this stored delta
was pinned down.)

This is the **software FPS cap**. Combined with the IMMEDIATE present interval (§0.5), the client
is **not** vsync-locked — it is throttled by this counter-and-sleep step to a fixed 60 FPS.

**Limiter constants:**

| Constant | Meaning |
|---|---|
| the float **60.0** | hardcoded target frame rate, seeded once into the driver object's +0x30 field by its constructor; the rate passed to the limiter |
| **1.0** | numerator of the target frame time `1.0 / rate` |
| **1000.0** | seconds→milliseconds conversion in `Sleep((1/rate − measured) × 1000)` — units only, *not* a cap |
| **1 ms** | the `timeBeginPeriod(1)` timer resolution that gives the `Sleep` its granularity |

> **Resolution (CYCLE 7) — the cap rate is NOT `DISPLAY_FRAMERATE`; the config is dead.** The
> reading that the cap is keyed to the `DISPLAY_FRAMERATE` display-config value is **wrong**, and the
> earlier "is the config truly inert?" question is now **closed: it is inert (dead)**. The throttle
> reads the driver object's hardcoded-60.0 +0x30 field. The `DISPLAY_FRAMERATE` value (parsed from
> `data/script/display.lua` with the integer config getter) is stored **raw, with no zero-default**,
> into a field of a **different** object — the renderer/display singleton (the field is initialised to
> 0 in that singleton's constructor). An exhaustive static search of that field's displacement found
> **exactly two sites, both writes** (the constructor zero-init and the config store) and **zero
> readers anywhere in the binary** — the stored value never reaches the limiter. There is therefore
> no "if the config value is 0 a default applies" behaviour at the cap. **Any spec or port that
> claims the FPS cap is configurable is WRONG; pace gameplay at a fixed 60 FPS upper bound to match.**
> (Only a live runtime confirmation that 60.0 actually paces at 60 FPS would remain — RUNTIME-ONLY,
> not needed for the verdict.)

### 7.5 Loop exit

When the message pump observes the quit message it clears the run-flag; the `while` then falls
through and the loop returns to the scene-machine case, which tears down the scene and lets the
state machine advance. The window proc posts the quit on the close path.

### 7.6 One-line summary

`pump-input + deferred-input dispatch → (scene pre-update + render + present, with device-lost
recovery) → frame-tick-scheduler tick-all → high-resolution-counter frame throttle (sleep to a
fixed 60 FPS)`, repeated while the run-flag is set.

## 6. Reimplementation note (.NET, intentional divergence)

The legacy engine drives logic with a **millisecond round-robin scheduler** where
each tickable holds its own `interval_ms` and free-running render is uncapped.
For the deterministic .NET client we adopt a different model — an **intentional,
documented divergence**, not a faithful copy:

- **Fixed-rate logic tick.** The core simulation advances on a single **fixed
  timestep** (e.g. **30 Hz** via a `PeriodicTimer`), decoupled from rendering. This
  gives deterministic, server-replayable simulation, which the original per-task ms
  thresholds (with frame-rate jitter and no leftover-time carry) do not guarantee.
- **Render decoupled from logic.** Godot owns presentation and runs at its own
  (uncapped / vsync) frame rate. Godot **has no logic clock of its own** — it
  **interpolates between simulation snapshots** produced by the fixed tick. This
  mirrors the original's logic/render split while removing the original's
  unbounded per-frame jitter.
- **Equivalence claim.** Functionally this is equivalent to the original: both
  separate "advance the world" from "draw the world". We trade the original's
  amortised round-robin (1.1 %/frame) and per-subscriber intervals for one uniform
  fixed tick — simpler, deterministic, and headless-testable on the future server.
- **Time-scale preserved.** The original's optional time-scale (slow-mo /
  fast-forward) maps naturally onto the fixed-tick model as a multiplier on the
  fixed delta, so the capability is retained.

In short: the original is *variable-cadence, per-subscriber, ms-threshold*; the
.NET core is *fixed-cadence, single-rate, snapshot-interpolated in Godot* — a
deliberate upgrade chosen for determinism and testability.
