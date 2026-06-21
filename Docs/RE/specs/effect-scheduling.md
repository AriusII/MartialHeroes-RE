---
status: code-confirmed
sample_verified: false   # static comprehension; no live debugger run was driven for this dossier
subsystems: [effects, combat, game_loop]
---

# Spec: Timed-Effect Scheduling — The Per-Frame Tick Spine & Deadline Conventions

> **Clean-room spec. Neutral description only — NO decompiler pseudo-code, NO binary addresses,
> NO decompiler-generated identifiers.** Promoted from dirty-room runtime comprehension under EU
> Software Directive 2009/24/EC Art. 6, solely to achieve interoperability. Consumed by
> `Client.Application` (effect dispatch / combat-FX) and, for the death sequence, the combat
> outcome handler. Object field offsets below are **relative to the start of each object**; they
> are not memory addresses and must never be treated as such.
>
> **Confidence vocabulary:**
> - **CODE-CONFIRMED** — layout/behaviour recovered from the instruction stream and corroborated
>   across multiple use sites.
> - **HIGH** — consistent across the traced sites for this subsystem.
> - **MED** — structurally inferred but not yet confirmed against a live run or a second source.
> - **UNVERIFIED** — hypothesis only; do not hard-code.
>
> **Scope.** This file documents the *scheduling architecture* of timed visual effects: the
> single per-frame clock sample, the four-manager fan-out, the deadline-arm primitive, the two
> distinct deadline-field conventions, the non-priority-queue model **of the effect lists**, the
> separate **sorted-tree** timed-EVENT queue (the universal 10001 deferred trigger), and the
> death/respawn timing state machine. It does **not** restate the effect class hierarchy, the
> pool layout, or the descriptor formats — for those see `specs/effects.md` and
> `formats/effects.md`. Citing engineers: `// spec: Docs/RE/specs/effect-scheduling.md`.

> **Verification banner.**
> - **Status mix:** *confirmed* where control-flow was traced (the single per-frame now-ms sample,
>   the four-manager linear fan-out, the `+64` arm = `now + delay`, the `+48` particle start-gate +
>   1-second life, the spawn-and-arm factory append, and the enqueue + container of the 10001
>   sorted-tree timed-event queue); *static-hypothesis* where one inference stands (the 10001 queue
>   DRAIN/dispatch consumer was not isolated this pass); *capture/debugger-pending* where a runtime
>   witness is needed (the two death-FSM in-state clock reads' precise role; the per-kind particle
>   colour/semantic labels).
> - **ida_reverified:** 2026-06-20 (CYCLE 7 runtime trigger-dispatch spine added; prior 2026-06-16)
> - **ida_anchor:** 263bd994
> - **evidence:** [static-ida]
> - **conflicts:** none with the stated effect-list claims — the §3 "NOT a priority queue" model is
>   correct **for the effect element lists**. The newly documented **universal 10001 timed-EVENT
>   queue IS a sorted tree** keyed by fire-time; it is a **distinct sibling mechanism** hosted on the
>   effect manager, not a contradiction of the effect-list model. §3 is now scoped accordingly and
>   §5A documents the tree.
> - **CYCLE 7 (2026-06-20, IDB SHA 263bd994):** added §10 — the **runtime trigger-dispatch chain**
>   (server effect packet → handler → spawn factory → descriptor resolve → first-tick particle build
>   → insert into the **owner-actor list** at +0x240, or **self-destruct** when the effective lifetime
>   is zero). Confirms the shared central per-frame tick is **vtable slot 1**, driving all pooled
>   effects, and that the per-effect lifetime deadline = `spawn_arg + clock_ms` stored at effect
>   **+0x40** — the same `now + delay` arm primitive as §4.1, on a different object offset. The pool
>   family, the per-actor list, and the JointXEffect bone-attach detail are owned by `specs/effects.md`
>   and are cross-referenced, not duplicated here.

---

## Status

| Item | Confidence |
|------|------------|
| Single now-ms capture per frame, fanned by value to four managers | CODE-CONFIRMED |
| Four managers are linear active-list walks (NOT a heap / priority queue) | CODE-CONFIRMED |
| Universal 10001 timed-EVENT queue IS a sorted tree keyed by fire-time (distinct sibling of the effect lists) | CODE-CONFIRMED (enqueue + container) / drain UNVERIFIED |
| Deadline-arm primitive (`baseline = now + delay`) | CODE-CONFIRMED |
| Effect-object `+64` elapsed-origin convention | CODE-CONFIRMED |
| Particle-element `+48` start-gate + 1-second life convention | CODE-CONFIRMED |
| Reconciliation of the two conventions (two distinct object layouts) | HIGH |
| Death/respawn FSM state graph + cycle-driven advance | HIGH |
| Per-kind particle colour/semantic labels (impact vs blood vs decal) | MED |
| Runtime now-ms equals the raw system millisecond timer (scale path dead) | CODE-CONFIRMED |
| Trigger-dispatch chain: packet handler → spawn factory → descriptor resolve → first-tick build → owner-actor-list insert / zero-lifetime self-destruct (CYCLE 7, §10) | CODE-CONFIRMED |
| Shared central per-frame tick = vtable slot 1; per-effect deadline = `spawn_arg + clock_ms` at effect +0x40 (CYCLE 7, §10) | CODE-CONFIRMED |

---

## 1. Time Base — the now-ms source

**Confidence: CODE-CONFIRMED.**

All effect timing uses the engine's wall-clock millisecond counter, a thin wrapper over the
operating system's multimedia millisecond timer (`timeGetTime()` equivalent). The wrapper carries
an optional time-scale branch (multiply by a global scale, subtract a rebase offset) that only
engages when the scale differs from `1.0`. In this build the **only writer of that scale is an
unreachable orphan**, so the scale stays at its `1.0` default and:

> **At runtime, now-ms equals the raw system millisecond timer.** The scale path is dead code.
> An interoperable reimplementation may use a plain monotonic millisecond clock with no scaling.

This same clock is consumed across hundreds of call sites (animation, FX, network throttle); this
spec covers only its role in effect scheduling.

---

## 2. The Per-Frame Tick Spine

**Confidence: CODE-CONFIRMED.**

A single master routine drives the whole timed-effect system once per frame. Its contract:

1. **One-time baseline.** On its first ever call it latches a one-time baseline millisecond value
   into a global, guarded by an "already-initialised" bit in a flag word. Subsequent frames do not
   re-latch it.
2. **One clock sample per frame.** It reads the now-ms **exactly once** and then passes that single
   value **by value** into every subordinate manager tick. There is **no per-manager re-sampling of
   the clock** — every effect in the frame is evaluated against the same now-ms.
3. **Inline shared-list walk first.** Before fanning out, it walks its own shared active list (a
   doubly-linked list of effect elements). For each node it invokes the element's per-frame virtual,
   passing the now-ms and a **half-scaled frame-delta** (the global frame-delta multiplied by `0.5`).
   If the element's "still-alive" byte is clear, the element is virtually destroyed and unlinked from
   the list. This is a plain linear consume-or-reap loop; the list is not ordered.

### 2.1 The four subordinate managers (the fan-out)

The one now-ms is fanned into four managers, **each of which is the same flat shape**: walk a
doubly-linked active list, call each element's per-frame tick virtual, then unlink-and-destroy any
element whose active flag is clear. No manager sorts its list and none traverses in deadline order.

| # | Manager (role) | What it ticks | Notes |
|---|----------------|---------------|-------|
| 1 | **Shared particle-list manager** | Per-particle billboard elements (see §5) | Walks **two** sibling lists (a primary and a secondary), each a linear doubly-linked walk; per node it ticks then reaps when the element's active byte is clear. Calls the particle tick worker described in §5. |
| 2 | **Per-area ambient/environment effect manager** | Map-effect descriptors for the active area | On area change it reloads the area's effect manifest (`data/effect/map<N>.txt`). It proximity- and time-of-day-gates each descriptor against the local player position and a culled view radius (radius clamped to 1000, scaled by 0.8, then squared), spawning an ambient effect when a descriptor enters range and removing it when it leaves, then ticks its own active list. **Its time-of-day gate uses a wall-clock seconds-of-day value (hours, minutes), not the now-ms.** |
| 3 | **Joint-effect manager, channel A** | Effects bound to actor bones/joints | Gated by a global option flag (a display-option index). When the option turns the feature off, it tears down once and early-exits; otherwise it walks its active list, calling each element's tick virtual with the now-ms, the half-frame-delta, and the option value, reaping cleared elements. |
| 4 | **Joint-effect manager, channel B** | Second joint-effect channel | Structurally identical to channel A (same option gate, same tick virtual, same reap). A sibling channel. |

**Net architecture:** ONE clock sample per frame → fanned by value into four managers → each
manager is an O(n) sweep of an **unordered active list** with a per-element virtual tick and an
inline reap of cleared elements.

---

## 3. NOT a Priority Queue — the iteration model (load-bearing), for the EFFECT lists

**Confidence: CODE-CONFIRMED.**

> **The combat-timer / effect-element scheduler is a linear active-list walk with inline
> now-ms-vs-deadline gates. It is NOT a min-heap, NOT a sorted timer queue, and NOT a timer wheel.
> Do not model it as one.**

> **Scope note (load-bearing).** This non-priority-queue statement applies to the **effect element
> lists** (the spine's shared list and the four subordinate managers of §2). It does **NOT** describe
> the *separate* **universal 10001 timed-EVENT queue**, which IS a sorted tree keyed by fire-time —
> a distinct sibling mechanism hosted on the same effect manager. See **§5A**. Do not assume every
> timer in the effect subsystem is an unordered list: the per-frame effect ticks are unordered walks;
> the deferred scene/connection event scheduler is an ordered tree.

The evidence that fixes this model (for the effect lists):

- The spine and all four managers **iterate doubly-linked active lists in list order**, ticking
  *every live element every frame*. There is no sorted insert, no top-of-heap pop, and no
  deadline-ordered traversal.
- The **deadline comparisons happen INSIDE each element's tick, not in the iteration.** The
  now-ms-vs-`+48` start gate (§5) and the `now-ms − +64` elapsed computation (§4) are per-element
  inline conditionals. A deadline never reorders a list; it only decides what that one element does
  this frame and whether to flag itself for reaping.
- **Insertion appends** to the active list. It does not sort by deadline.

For a faithful reimplementation: keep effects in an unordered active collection and, each frame,
sample the clock **once**, then sweep the collection evaluating each element's own deadline gate
inline. Reap (remove) elements that cleared their active flag.

---

## 4. The Deadline-Arm Primitive & the `+64` Elapsed-Origin Convention

**Confidence: CODE-CONFIRMED.**

### 4.1 Arming a timed effect ("schedule at now + delay")

A timed effect object (the descriptor-driven, keyframe-animated, pooled effect — distinct from the
particle element of §5) is armed by a setup routine. The arm:

1. Resolves the effect descriptor from the registry by id. On failure it clears the object's
   "valid" word and returns (the spawn factory then destroys the object back to the pool).
2. On success it builds the per-element particle set, then writes the timing/lifetime fields below.

**The single arm write:**

> `effect.baseline = now_ms + delay`  — the now-ms is captured at the arm site, the caller's
> duration is added, and the sum is stored as the timing baseline at object offset **`+64`**.

A positional variant performs the identical arm logic but additionally stores a world-position
vec3 (three floats) into the object before arming; it writes the same `now + delay` baseline.

**Effect-object field set written by the arm** (offsets into the timed-effect object):

| Offset | Size | Type  | Field | Meaning |
|-------:|-----:|-------|-------|---------|
| +12 | 4 | u32   | valid word | Non-zero on the success path; `0` on descriptor-resolve failure (signals the factory to destroy). |
| +16 | 12 | f32×3 | spawn position | World position vec3 (`+16/+20/+24`) — written by the positional arm variant only. |
| +60 | 1 | u8    | persistent flag | "No lifetime cap / persistent" flag; also influences the elapsed-vs-lifetime expiry gate in the consumer. |
| +64 | 4 | u32   | timing baseline | **The arm: `now_ms + delay`.** Consumed as the elapsed origin (§4.2). |
| +72 | 4 | f32   | lifetime/scale | Descriptor base-lifetime multiplied by the caller's scale argument. Per-frame speed/lifetime multiplier. |
| +76 | 4 | u32   | start cursor | Reset to `0` at arm (start/phase accumulator). |
| +80 | 4 | u32   | rate/speed payload | Per-element rate/speed used by the consumer's elapsed scaling. |
| +84 | 4 | u32   | payload anchor | Passed through from the spawning handler. |
| +88 | 1 | u8    | kind byte | Passed through from the spawning handler. |
| +92 | 4 | u32   | payload anchor | Passed through (owner ref). |
| +96 | 1 | u8    | kind byte | Passed through. |

### 4.2 Consuming the baseline (elapsed = now − baseline)

The per-element tick virtual for these effect objects is a **now-ms consumer, not a scheduler**. It
does not sample the clock and does not compare against an armed deadline. Instead it receives the
frame's now-ms inside a per-frame context block and computes:

> `elapsed = now_ms − effect.baseline(+64)`

That elapsed value drives keyframe phase: it is scaled, taken modulo the descriptor's keyframe
period, used to select/interpolate keyframes, rotate velocity by the element orientation, scale,
place in world, and build the billboard / GPU-particle geometry submitted to render. When the
elapsed exceeds the descriptor's lifetime cap (and the effect is not flagged looping via the
persistent flag at `+60`), it clears the element's alive byte so the owning manager reaps it on the
next pass.

### 4.3 The spawn-and-arm factory

A factory routine creates and registers a timed effect: pool-allocate the object, run its
constructor, call the arm (descriptor resolve + `now + delay` baseline). If the valid word (`+12`)
is zero (descriptor resolve failed) it virtually destroys the object back to the pool; otherwise it
**appends** the armed object to the effect system's shared active list (the list the spine walks).
This factory has many call sites, the bulk of which are network effect/combat handlers (skill hits,
combat FX) firing a timed visual on a server event.

---

## 5. The Particle Element & the `+48` Start-Gate Convention

**Confidence: CODE-CONFIRMED** (split-site, 1-second life, reconciliation); **MED** (per-kind
semantic labels).

This is a **separate, smaller object layout** from the §4 effect object — it is the impact / blood
/ decal **billboard particle element** owned by the shared particle-list manager (§2.1 manager #1).
Its per-element tick worker receives `(element, now_ms)`.

### 5.1 The start gate (the cluster core)

> `if (now_ms > element.start_deadline(+48)) { … }`

**`element+48` is the armed start-deadline** — the absolute now-ms timestamp at/after which the
particle becomes due to animate. Until now-ms exceeds it, the worker does nothing and returns. Once
now-ms passes `+48`, the worker sets the element's "has-started/visible" byte (`+46 = 1`) and enters
the per-kind animation body.

### 5.2 The 1-second life

Inside every animation case the worker computes:

> `progress = (now_ms − element.start_deadline(+48)) / 1000.0`
>
> `if (progress > 1.0) { progress = 1.0; element.active(+45) = 0; }`

So once **one second** has elapsed since the start-deadline, the worker clamps the lerp parameter to
`1.0` (the particle reaches its final size/fade) **and clears the element's active flag at `+45`** —
exactly the flag the owning manager tests to unlink-and-destroy the element on its next pass. Each
particle therefore lives ~1000 ms after it triggers, then is reaped.

### 5.3 Particle-element field layout

| Offset | Size | Type | Field | Meaning |
|-------:|-----:|------|-------|---------|
| +8  | 4 | u32 | kind | Kind selector, a switch over `0..7` (eight billboard kinds, §5.4). |
| +12 | 4 | u32 | colour variant | Selects an alternate base colour within a kind (observed values 2, 3 used by several kinds). |
| +44 | 1 | u8  | alpha | Per-vertex alpha packed into the billboard colour word. |
| +45 | 1 | u8  | active flag | Cleared after the 1-second life elapses; the reap predicate the manager tests. |
| +46 | 1 | u8  | started/visible flag | Set to `1` when now-ms first crosses the start-deadline. |
| +48 | 4 | u32 | start-deadline | **The armed start gate**: now-ms timestamp at/after which the particle animates. |
| +52 | 4×6×4 | f32 | vertex array | Four billboard vertices (`+52/+76/+100/+124` region), 6-dword stride each, facing the camera. |
| +148 | 1 | u8 | red-burst flag | Used by kind 2 to select a red base colour instead of white (observed). |

### 5.4 The eight particle kinds (`element+8`, switch 0..7)

Each kind is a colour-coded billboard quad with its own start scale, growth, height offset, and RGBA
tint; each lerps a size vector over the normalised elapsed and packs a colour word per vertex (alpha
from `+44`). Observed per-kind characteristics — **colour/semantic labels are MED** (the
impact/blood/decal interpretation is plausible but not yet confirmed against which kind fires on
which combat input):

| Kind | Observed base colour(s) | Height offset | Notes |
|------|-------------------------|---------------|-------|
| 0 | red base; blue when `+12 == 2`; pale when `+12 == 3` | 0 | White→red-ish impact spark. |
| 1 | tinted by a runtime global colour word | 30 | "Tinted impact" variant; same geometry as kind 0. |
| 2 | white; red when the red-burst flag (`+148`) is set | — | Large burst; uses lazily-initialised static start/end size globals; wider quad on higher detail. |
| 3 | blue/orange blood-ish | — | Low-detail path adds a random jitter to the size; distinct start-size table. |
| 4 | yellow; cyan when `+12 == 2` | 30 | Randomised size on low detail. |
| 5 | white | — | Randomised size blend on low detail; larger start size. |
| 6 | green | 30 | Randomised size on low detail. |
| 7 | green | — | Two size profiles by detail option; larger start. |

The exact per-kind RGBA constants and size tables are render detail; a faithful FX reimplementation
should treat the kind→semantic mapping as tunable until a live catalog confirms which kind fires on
which input.

---

## 5A. The Universal 10001 Timed-EVENT Queue — a SORTED TREE (distinct from the effect lists)

**Confidence: CODE-CONFIRMED** for the enqueue primitive and the container shape; **UNVERIFIED** for
the per-frame DRAIN/dispatch consumer (not isolated this pass).

Separate from the unordered effect-element active lists (§2–§4), the effect manager **hosts** a second,
**ordered** timer mechanism: a **sorted tree** (a red-black-tree-style ordered container) keyed by an
absolute **fire-time** millisecond value. This is the **universal 10001 deferred-event scheduler** —
a general "do this scene/connection action when the deadline arrives" mechanism that merely lives on
the effect-manager singleton; **it is NOT an effect-spawn list.**

> **Do not conflate this with the effect lists.** The effect element lists are **unordered linear
> walks** ticked every frame (§2, §3). The 10001 timed-event queue is an **ordered tree** keyed by
> fire-time. They are different data structures with different traversal models, both hosted on the
> same manager object.

### 5A.1 The enqueue primitive

An enqueue routine builds a **24-byte event record** and inserts it into the manager's sorted tree:

| Offset | Size | Type | Field | Meaning |
|-------:|-----:|------|-------|---------|
| +0  | 4 | u32 | `fire_time` | The armed deadline: `delay + now_ms` (captured at the enqueue site). This is the **tree's sort key** — entries are ordered by fire-time. |
| +4  | 4 | u32 | `event_id` | The deferred event selector — `10001` for the scene/connection-state deferred trigger. |
| +8  | 16 | u32×4 | `payload` | Four payload dwords passed through from the enqueuing site (zero for the bare 10001 trigger). |

The `10001` event id is a **generic deferred SCENE / CONNECTION-state trigger**, not an effect: it is
enqueued from network/scene state sites (for example a connection-state transition, and a character
action-result handler) to **defer** a scene or connection action by a fixed delay (observed delays
include 5000 ms and 10000 ms — e.g. a login-curtain / logout / action-result delay). The effect
manager is merely the **host** of the timer queue; the event itself drives scene/connection logic, not
the effect renderer.

### 5A.2 The arm convention matches §4.1

The fire-time is armed with the **same `now + delay` primitive** used for effect objects (§4.1): the
now-ms is sampled at the enqueue site, the caller's delay is added, and the sum becomes `fire_time`.
The difference is the **container** — here the armed record is inserted into a **sorted tree by
fire-time**, so the earliest-due event is at the tree's front, whereas effect elements are appended to
an **unordered** list and gated inline per element.

### 5A.3 The drain/dispatch consumer — UNVERIFIED

The per-frame consumer that **pops** entries whose `fire_time ≤ now_ms` and **dispatches** the
`event_id` (advancing the scene/connection state for the 10001 trigger) was **not isolated** in this
analysis pass — only the enqueue primitive and the tree container are confirmed. An interoperable
reimplementation should model the queue as an ordered (by fire-time) collection on the effect manager,
drained each frame against the single per-frame now-ms (§1) by popping all entries with
`fire_time ≤ now_ms` and dispatching each. The exact pop/dispatch site and whether dispatch is
in-order-only-until-the-first-future-entry remain **UNVERIFIED** (capture/debugger-pending). See §8.

---

## 6. Reconciliation — `+48` vs `+64` are two different objects

**Confidence: HIGH.** The two deadline conventions are **not a contradiction**; they belong to two
different object layouts owned by two different active lists:

| Aspect | Timed-effect object (§4) | Particle element (§5) |
|--------|--------------------------|-----------------------|
| Owning list | Effect-system shared list + Map/Joint managers | Shared particle-list manager |
| Timing field | `+64` = `now + delay` (baseline / elapsed origin) | `+48` = absolute start-deadline (start gate) |
| Compare | `elapsed = now − (+64)`, then keyframe + lifetime-cap expiry | `now > (+48)` gate, then a hard 1-second life |
| Active/valid byte | `+12` valid word, `+72` lifetime | `+45` active flag, `+46` started flag |
| Animation | Descriptor keyframes, pooled, persistent flag at `+60` | Eight fixed billboard kinds, fixed 1-second life |

Both are now-ms-domain comparisons; **neither is a heap.** An engineer must keep these as two
distinct object types with two distinct timing semantics.

> **A third, ordered mechanism also exists** — the universal **10001 timed-EVENT queue** (§5A) — but
> it is **not** an effect-element layout. It is a 24-byte event record in a **sorted tree** keyed by
> fire-time, hosted on the same manager, driving deferred scene/connection actions rather than
> rendering an effect. It uses the **same `now + delay` arm** as the `+64` effect baseline, but an
> **ordered tree** container instead of an unordered list. Keep all three distinct.

---

## 7. Death / Respawn Timing FSM

**Confidence: HIGH** (state graph, SFX/UI wiring); **MED** (the precise role of the two in-state
clock reads).

A small finite-state machine on the visual actor object sequences a creature/player's
**death → spirit/respawn → revive → destroy** animation-and-SFX cycle. It is a combat-**outcome**
handler — what plays out after a kill.

- **State field:** a single state byte on the visual actor object (at object offset `+0x594`).
- **Advance pacing:** invoked from the actor's per-motion-cycle handlers (one fires when a motion
  finishes a cycle, one on a gear-refresh + motion replay). **Each state advance is paced by the
  death animation actually completing a cycle, not by a free-running millisecond timer.** Two states
  (2 and 3) additionally read the clock; see the note below.

### 7.1 State graph (11 states, `0..0xA`)

| State | Role | Advances to | Animation played |
|-------|------|-------------|------------------|
| 0 | Idle/cleanup entry: clears a counter, falls through to full-destroy cleanup. | (cleanup) | — |
| 1 | Death-start. | 2 | death base + 1 |
| 2 | Death-continue: **samples the clock**; fires death SFX keyed by the anim id; if the actor is the local player, raises the UI death notification. | 3 | death base + 1 or + 2 (branch) |
| 3 | Death-continue tail: **samples the clock**; fires death SFX. | (tail) | death base + 1 or + 2 |
| 5 | Respawn-prep. | 8 | death base + 3 |
| 6 | Spirit-form. | 9 | death base + 3 |
| 7 | Revive. | 9 | death base + 4 |
| 8 | Post-death loop (lingering corpse): replays the base death motion + SFX and runs a per-frame helper. | 1 | death base |
| 9 | Faded/teardown: runs the full-destroy pair; if local player, resets a singleton and pushes a respawn-UI teardown. | 0xA | — |
| 0xA | Full cleanup: runs the destroy pair and stops. | (end) | — |

(States 4 is not part of the observed graph; the advance edges above are what the FSM exercises.)

### 7.2 Death-animation base id by race

The death-animation base id is derived from the visual's race id, with a `+5` stride per race:

| Race id | Death-anim base |
|---------|-----------------|
| 1024 | 24 |
| 1025 | 29 |
| 1026 | 34 |
| 1027 | 39 |

All per-state motion ids are `base + {1, 2, 3, 4}`.

### 7.3 Death SFX selection

The death sound is selected by `(race resource id, death-anim slot)` and played positionally: when
the actor's resource id is ≥ 1024 and the anim id is in the death range, a death-sound table is
indexed by `4 × ((anim − 24) mod 5) + (resourceId − 1024)` and triggered through the positional 3D
sound dispatch (sound kind 10) at the actor position. The sound-dispatch internals belong to the
sound subsystem (`specs/sound.md`); only the death-side trigger is in scope here.

### 7.4 Timing character

This FSM times the death sequence by **animation-cycle completion** (driven from the cycle-end
handler), with two clock reads inside the death-continue states used to pace those phases. Whether
those two reads pace the death phase or merely seed a debounce is **MED** pending a live run.

---

## 8. Known unknowns

- **Per-kind particle semantics (MED):** which of the eight billboard kinds fires on which combat
  input (melee hit vs skill hit vs kill) is not catalogued; the impact/blood/decal labels are
  plausible only. A live combat-input run would confirm.
- **The exact armed value of the particle `+48` start-deadline** relative to the captured now-ms,
  and that the `+45` clear fires ~1000 ms later, is statically derived; not live-confirmed.
- **The two death-FSM clock reads (states 2/3):** death-phase pacing vs debounce is unresolved.
- **The 10001 timed-EVENT queue drain (§5A.3):** the per-frame consumer that pops `fire_time ≤ now_ms`
  entries from the sorted tree and dispatches `event_id 10001` (advancing the scene/connection state)
  was not isolated this pass; only the enqueue primitive and the tree container are confirmed. The
  exact pop/dispatch site and whether it stops at the first future-dated entry are
  capture/debugger-pending.
- The per-kind RGBA constants and size tables are render detail and are not exhaustively documented
  here (they are FX tuning, not scheduling).

---

## 9. Runtime Trigger Dispatch — the spawn-to-list chain (CYCLE 7)

**Confidence: CODE-CONFIRMED** (the trigger → spawn → first-tick → insert chain was traced; the
DBG-pending residue is the pool capacities and the numeric bone-source values, both owned by
`specs/effects.md`).

This section adds the **runtime trigger dispatch** that precedes the deadline-arm primitive of §4 —
i.e. *how a server event becomes an armed, listed effect*. It is the scheduling-side companion to
the effect-system detail in `specs/effects.md` (pool family §4/§4B, per-actor list §5.2, trigger-site
families §7.1, JointXEffect bone-attach §9.4); those are **cross-referenced, not restated** here.

### 9.1 The trigger → spawn → ctor → insert chain

A gameplay effect is **never** spawned by a direct "skill cast" call. The chain is:

```
server effect packet            (an S2C handler — attack/skill-impact, item-use, actor-state/buff,
  ↓                              level-up, exp-gain, spawn, death, periodic game-state tick, …)
spawn factory                   (called with the effect id taken FROM the packet)
  ↓
descriptor resolve              (sorted-map lower-bound lookup keyed by the raw effect id;
  ↓                              lazy-loaded on first use; FIRST-WINS on a duplicate id.
  ↓                              A miss clears the object's valid word → the factory destroys it
  ↓                              back to the pool and the spawn is silently abandoned.)
first-tick particle build       (builds the per-element particle resources; an element whose
  ↓                              resource selector is ≥ 10000 bridges to the particle registry)
insert OR self-destruct         (if the effective lifetime ≠ 0: APPEND the armed effect to the
                                 OWNER ACTOR's doubly-linked effect list at actor offset +0x240;
                                 if the lifetime == 0: self-destruct immediately — a zero-lifetime
                                 one-shot is never listed.)
```

The owner-actor list (head at actor +0x240, allocator at +0x244) is the *per-actor* container that
the per-frame tick spine (§2) walks — there is **no single global active pool**. The handler families,
the pool family, and the per-actor list are documented in `specs/effects.md` (§7.1, §4B, §5.2).

### 9.2 The shared central tick is vtable slot 1

Every pooled effect is driven each frame by the **shared central tick/dispatch routine reached
through vtable slot 1** of the effect object (slot 0 = destructor/cleanup, slot 2 = the type-specific
update). The spine of §2 invokes this slot-1 tick per live instance against the **single per-frame
now-ms** (§1); the slot-1 tick is the §4.2 elapsed-consumer (`elapsed = now − baseline`) for the
descriptor-driven effect object. This confirms, from the trigger side, the §2/§4 model: one clock
sample fanned out, an O(n) walk of unordered lists, deadline gates evaluated *inside* each element's
tick.

### 9.3 The per-effect lifetime deadline = `spawn_arg + clock_ms` at effect +0x40

The spawn factory writes the effect's **lifetime deadline** as `lifetime_arg + clock_ms` (the
caller's duration added to the now-ms captured at the spawn site) into the effect object at offset
**+0x40**. This is the **same `now + delay` arm primitive** documented in §4.1 — only the object
**offset differs** between the two effect-object layouts this subsystem uses:

| Object | Deadline / baseline offset | Convention |
|---|---|---|
| Descriptor-driven timed effect (the spawn-and-arm factory of §4.3) | **+64 (0x40)** baseline = `now + delay` | §4.1 — elapsed origin, consumed as `elapsed = now − baseline` |
| The XEffect-family pooled instance (this section's spawn factories) | **+0x40** lifetime = `spawn_arg + clock_ms` | same arm; expiry when `now > lifetime`, bypassed by the loop/persist flag |
| Particle billboard element (§5) | **+48 (0x30)** absolute start-deadline | §5 — start gate then a hard 1-second life |

`+64` (decimal) and `+0x40` (hex) are the **same offset**; the two §4 / §10 descriptions are the
same arm primitive seen from the scheduling side and the trigger side. The loop/persist flag (effect
+0x3C, set from the setup argument) bypasses the lifetime-cap expiry so a looping effect (e.g. a
cast-channel aura) lives until it is explicitly soft-stopped — see `specs/effects.md §15.4/§15.5`.

### 9.4 DBG-pending residue (owned by `specs/effects.md`)

The runtime witnesses this chain still needs — the six pool capacity counts, the emitter-registry
record count, the descriptor lazy-load timing, and the numeric `bone_source` → bone-name mapping —
are tracked in `specs/effects.md §14` (items 1, 2, 2b). They are runtime-only values; static code does
not settle them.

---

## 10. Cross-references

- Effect class hierarchy, pools, descriptor formats: `specs/effects.md`, `formats/effects.md`.
- Combat outcome / damage flow: `specs/combat.md`.
- Positional sound dispatch (the death SFX path): `specs/sound.md`.
- The visual actor object layout: `structs/actor.md`.
- Glossary: `Docs/RE/names.yaml`.
- Provenance: `Docs/RE/journal.md`.
