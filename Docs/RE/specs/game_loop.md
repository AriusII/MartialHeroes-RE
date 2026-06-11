---
status: hypothesis
sample_verified: false
---

# Game Loop & Timing — Clean-Room Specification

> Neutral, rewritten behavioural specification. No legacy symbols, no addresses,
> no pseudo-code. Describes the *observed behaviour* of the legacy client's main
> loop and time management so the .NET core can be reimplemented from scratch.
> Scope: main loop + logic/render decoupling + clock. Non-network.

## 1. Overall loop architecture

The client reuses a single engine main loop across every interactive screen
(login, opening, character-select, in-game). When a screen becomes active it
builds its handler object, registers it as the loop's event target, and runs the
same loop; on exit it tears the handler down. So the loop body is identical
regardless of screen.

The loop runs while a run flag (a global boolean) is set. Each iteration performs
exactly three steps, always in this order:

1. **Message pump** — drain pending Win32 window messages.
2. **Render** — draw exactly one frame (no timing math).
3. **Logic tick** — service the per-subscriber tick scheduler.

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

So the iteration is: `pump → render → tick`, repeated. Render and the logic tick
both run **every** iteration; rendering is never gated on time here.

## 2. Logic / render decoupling

The render step and the logic step are two separate calls, giving a structural
decoupling between presentation and simulation.

- **Render** is pure presentation with **no delta-time math**. It walks the active
  scenes, updates each scene's camera and culling, draws the scene, presents, and
  then handles D3D9 device-lost recovery (when the device is lost it waits ~1000 ms
  and attempts a reset). The non-scene branch issues a default clear, a begin-scene,
  and a draw call. Render runs **unconditionally every iteration** — there is no
  per-frame time gate on rendering.
- **Logic** is the scheduler's "tick all subscribers" pass (see §3). It samples the
  clock once per iteration and pulses only the subscribers whose interval has
  elapsed.
- **No interpolation factor** (no alpha/blend between simulation states) was
  observed in the loop body. The decoupling is by per-task interval, not by a
  fixed-step accumulator with interpolation.

There is **no explicit frame-rate cap or throttle Sleep** in the normal loop path
(the only Sleep observed is the device-lost recovery wait). Whether the present
path uses vsync as a de-facto FPS cap is **UNVERIFIED** (present parameters not
inspected).

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

A subscriber fires when:

```
enabled AND active AND (now_ms - last_tick_ms) >= interval_ms
```

When it fires, its own dispatch runs and `last_tick_ms` is advanced. Note this is
a **threshold** comparison (`now - last >= interval`), **not** an
accumulate-and-subtract loop: there is **no leftover-time carry**. Each tickable
therefore runs at roughly its own target interval, with whatever jitter the frame
rate imposes (it cannot "catch up" multiple missed ticks in one frame).

### Observed cadence constants

- A global value **16** (≈ 16 ms ≈ 60 FPS) is **written** during early
  initialisation. It is a plausible target frame-interval, but only a *write* was
  observed — **no proven consumer**. The 16 / ~60 Hz target is **UNVERIFIED**.
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

## 5. UNVERIFIED items

- The **16 / ~60 Hz** target interval has no proven consumer — it is only written,
  never observably read; its gating role is unconfirmed.
- Whether the D3D present path uses **vsync** as a de-facto FPS cap (present
  parameters not inspected).
- Whether the high-resolution performance counter is used anywhere outside the loop.
- Exact write site of each subscriber's `last_tick_ms` (it is updated inside the
  subscriber's own dispatch, which is the only sensible writer, but the precise
  point was not pinned down).
- Whether the per-frame scheduler singleton and the now-ms-providing singleton are
  literally the same object instance (very likely the same singleton).

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
