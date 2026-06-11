---
status: hypothesis
sample_verified: false
subsystems: [camera_views, movement_collision]
networked: partial   # camera is client-only; movement uses 2/13, 5/13, 4/13
encoding_note: Korean in-game/config text is CP949 (legacy MS949 code page), not UTF-8.
---

# Camera, Client Movement & Collision — Clean-Room Specification

> Neutral, rewritten behavioural specification. No legacy symbols, no addresses,
> no pseudo-code, no decompiler identifiers. Describes the *observed behaviour*,
> formulas, state machines, and tuning constants of the legacy client's camera
> view modes and its client-side movement / collision pipeline, so the .NET core
> (`Client.Domain` / `Client.Application`) and the camera-side of the Godot
> presentation layer can be reimplemented from scratch.
>
> **Camera is purely client-side** — no camera state crosses the wire. **Movement**
> drives three already-cataloged opcodes (see §B.1); this spec describes only the
> client-side *behaviour* around them, never re-derives their byte layout (the
> committed `opcodes.md` and `packets/*.yaml` own that).

## Confidence model (read before trusting any number)

Each major claim is tagged inline:

- **(solid)** — re-derived from behaviour with corroborating evidence; safe to implement.
- **(plausible)** — single-source behavioural inference; implement but keep a feature flag / make it tunable.
- **(UNVERIFIED)** — hypothesis only; do **not** hard-code. Listed again in §D.

All exact numeric tuning constants below were lifted from the binary's immediate
operands; they are **(solid)** as *values present in the code* but **(plausible)**
as *the value that governs runtime feel*, because none has been confirmed against a
live capture or recording. Treat the whole constant set as "faithful starting tune,
expose as config."

---

# Part A — Camera system

## A.1 Architecture at a glance

The renderer is an in-house scene-graph engine. Two distinct concepts cooperate:

1. **The camera object** — a perspective-projection node that owns field-of-view,
   aspect ratio, near/far planes, and produces both the projection matrix and a
   6-plane culling frustum. It does *not* contain view-mode logic.
2. **The camera "manipulator"** — a family of scene-graph nodes that, every frame,
   *position and orient* the camera object based on the follow target (the local
   player) and on input. This is where the gameplay "view mode" behaviour lives.
   **One manipulator class per view mode.**

There are **five in-world view modes**, all sharing one common base manipulator
that holds the shared state (§A.4), plus a **sixth, non-in-world** manipulator used
only by the character-select / create-preview window:

| Mode | Role | Orbits player? | Terrain collision? | Default? |
|---|---|---|---|---|
| **Third** | Over-the-shoulder follow camera | yes (yaw + pitch) | yes (height clamp + occlusion pull-in) | **yes** |
| **First** | First-person (eye at player head) | yaw/pitch look, no follow distance | no | no |
| **Static** | Fixed-angle tracking; follows position, never rotates around the player | no | no | no |
| **Gamble** | Orbit camera for the gamble / betting minigame UI | yaw orbit, UI-driven | no | no |
| **Event** | Scripted / cutscene camera; data-driven path; player loses control | n/a (scripted) | no | no |
| *Select* | Character-select / create-preview camera (out-of-world; framed by the select window) | preview-frame | no | n/a |

This matches the input/UI spec (`specs/input_ui.md` §4), which records 5 active
view-platform slots plus a reserved-and-unused sixth slot and a single integer
"active view index." (solid)

## A.2 View-mode switching model (no pointer swap)

There is **no dispatch table that swaps a single active-manipulator pointer.**
Instead **all five in-world manipulators live in the scene graph simultaneously**,
constructed once at scene setup. Each manipulator carries a per-mode **enable flag**;
its per-frame traversal hook early-outs unless that flag is set. So **"switch view
mode" = enable the chosen manipulator and disable the others.** (solid)

The per-frame skeleton per manipulator is: a traversal hook (runs only if enabled)
which calls the mode's positioning math, then traverses scene-graph children.
First-person and Third-person **share the same input-event handler** (mouse-look). (solid)

### Cinematic lock

A separate global **cinematic-lock flag** suppresses *all* in-game camera input and
positioning while set. The gameplay cameras (Third etc.) check this flag and skip
input + repositioning while it is non-zero — i.e. during scripted Event-camera
sequences the player loses camera control. The Event camera itself runs *during*
that lock (the inverse sense). (solid; the precise host field is dirty-only and omitted)

## A.3 Input model

### A.3.1 Polled camera actions (keyboard)

The manipulator polls an input-state service ("is this action currently active?"),
behind a per-key edge/repeat gate, for these camera actions. Action IDs are stable
small integers (shown in decimal; same IDs in Third and First):

| Action ID | Camera action |
|---:|---|
| 1028 | yaw left  (rotate camera counter-clockwise) |
| 1029 | yaw right (rotate camera clockwise) |
| 1002 | pitch / elevation axis A |
| 1003 | pitch / elevation axis B |
| 1000 | zoom / distance axis A (one of in/out) |
| 1001 | zoom / distance axis B (the other) |
| 1012 | modifier: while active, mouse-look is suppressed |

> **(UNVERIFIED)** the *polarity* within each pitch pair (1002 vs 1003) and each
> zoom pair (1000 vs 1001) — which member is "up" / "in" vs "down" / "out". Both
> members of a pair feed the same integrator with opposite sign; the assignment of
> sign-to-member is inferred, not proven. Implement as a configurable +/- pair.

These camera action IDs belong to the broader input-binding map and should be
cross-linked when the input subsystem is fully specced. They are *not* the same
numeric space as the movement / left-click IDs in Part B.

### A.3.2 Mouse-look (shared First/Third event handler)

The mouse-look handler dispatches on the pointer event type (move / button-down /
other-button / wheel-or-extended). Behaviour:

- While the **suppress modifier** (action 1012) is active, look mode is disabled and
  the drag-active state is cleared.
- **Right-button drag begins:** on right-button press, set drag-active and record the
  anchor cursor position. (solid)
- **Mouse move while dragging:** horizontal cursor delta (`cursorX − anchorX`) feeds
  the yaw-rate integrator; vertical delta (`anchorY − cursorY`) scaled by the
  **mouse-drag pitch gain = 5e-4** feeds the pitch-rate integrator. (solid)
- **Direct mouse axis:** raw mouse delta scaled by the **mouse-axis sensitivity = 1e-6**
  integrates into pitch, clamped to **[-4.0, +4.0]**; overflow beyond the clamp spills
  into a pitch-overflow accumulator and is re-clamped against a ceiling of **27.0**
  (paired with a secondary **26.0** limit). (solid for values; semantics plausible)
- **Other-button / wheel:** zoom delta is scaled by **0.01** and the drag accumulator
  is reset. (solid)

## A.4 Shared per-mode update math

Each enabled manipulator runs this skeleton per frame (Third/First/Static/Gamble):

1. Resolve the follow target (the local player), lazily cached once per mode.
2. Read the elapsed time since the previous frame in milliseconds and convert to
   seconds by multiplying by **1e-3**; a secondary smoothing path for the distance
   integrator uses **1e-4**. (solid)
3. Multiply by the per-node **speed/time-scale factor** (default **1.0**). (solid)
4. Integrate polled / mouse input into the yaw-rate, pitch, and zoom state.
5. Apply **gain**, **friction**, **dead-zone**, and **clamps** (table below).
6. Compose the orientation from a yaw quaternion (about the up axis) and a
   pitch/elevation component.
7. Place the eye relative to the focus point.
8. (Third only) run terrain collision (§A.6), then push the composed transform to
   the camera object and trigger a cull/refresh.

### Shared smoothing / clamp constants

| Role | Value | Notes |
|---|---:|---|
| Time delta scale (ms → s) | **1e-3** | applied to per-frame elapsed ms |
| Distance-integrator smoothing | **1e-4** | secondary, slower time scale |
| No-input rate decay (friction) | **0.6** | multiplies a rate each frame when no input |
| Keyboard input → rate gain | **0.3** | per-frame step added on key |
| Yaw-rate clamp | **[-0.1, +0.1]** | bounds the per-frame yaw angular velocity |
| Zoom-delta clamp | **[-1.0, +1.0]** | bounds per-frame distance change |
| Pitch clamp | **[-4.0, +4.0]** | bounds the pitch state |
| Absolute-yaw clamp | **±π/2 (±1.5708)** | bounds accumulated yaw about the player |
| Yaw slerp / damping factor | **0.9** | eases accumulated yaw toward its target |
| Rate dead-zone | **1e-3** | rates with magnitude below this snap to 0 |
| Pitch/elevation limits (secondary) | **[-90.0, -12.0]** | a separate elevation-clamp pair |
| Mouse-drag pitch gain | **5e-4** | cursor-Y delta → pitch rate |
| Mouse-axis sensitivity | **1e-6** | raw mouse delta → pitch |
| Pitch-overflow ceiling | **27.0** (with **26.0** re-clamp) | spill clamp for over-driven pitch |
| Button/wheel zoom scale | **0.01** | scales zoom on the other-button/wheel path |
| Terrain-hit yaw kill | **-0.01** | forced yaw-rate value on a hard terrain hit |
| No-collision pitch correction step | **50.0** | pitch nudge when occlusion probe misses |

All values **(solid as code immediates)**; runtime feel **(plausible)**. Expose as a config block.

## A.5 Per-mode behaviour

- **Third (default).** Integrate keyboard yaw/pitch/zoom and mouse-look into the rate
  fields with the gain/friction/dead-zone above; clamp pitch to ±4, yaw-rate to ±0.1,
  zoom to ±1, and absolute yaw to ±π/2 via the 0.9 ease. Focus = the player's head
  position when available, else the player's ground position with vertical forced to
  0. Eye = focus + (a direction/offset basis rotated by the orientation) at the
  current follow distance. Then terrain collision (§A.6) adjusts the eye height.
  Defaults: **follow distance ≈ 10.0**, **pitch ≈ -π/6 (-30°)**, an eye-offset seed of
  about **(-750, 0, +500)**, focus vertical seed **≈ -40**. (solid for behaviour;
  exact seeds plausible)
- **First (first-person).** Same input vocabulary and constant pool as Third. Eye sits
  at the player's head with **no trailing follow distance** and **no terrain height
  clamp** (the eye is already inside the player volume). Zoom is largely inert. (solid)
- **Static (fixed-angle follow).** Pitch and yaw are **fixed** — the camera never
  orbits the player; only the position is tracked, and (per its branch) a zoom axis
  remains responsive. Pitch is hard-clamped to ±π/2. **No terrain collision.** Acts as
  a fixed-orientation tracking cam. (solid)
- **Gamble (orbit cam).** Builds a yaw (up-axis) quaternion each frame and orbits the
  player focus; absolute yaw clamped to ±π/2. Driven by the gamble/betting minigame UI
  rather than by movement keys; the exact UI message that rotates it is **(UNVERIFIED)**. (plausible)
- **Event (scripted / cutscene).** Runs *during* the cinematic lock. Positioning is
  data-driven from a per-mode scripted-parameter block; the player has no manual
  control. The scripted keyframe/path **format is (UNVERIFIED)** and out of scope here. (plausible)
- **Select (out-of-world preview).** Frames the character-select / create-preview
  actor in the select window. Not part of the in-world mode set. (solid)

## A.6 Terrain collision for the Third-person camera

Third-person performs **two** collision behaviours; the other modes do neither:

1. **Terrain-height clamp.** Map the candidate eye's world (X, Z) into terrain-grid
   coordinates (grid origin index **10000**, cell size **1024** world units, i.e. a
   world→grid scale of **-1/1024 = -0.0009765625**; secondary cell scale/bias
   **1024.0 / 1.1**). Sample the **interpolated (bilinear) terrain height** at the eye
   (X, Z) from the cell's heightmap when the cell has geometry. Clamp the eye's
   vertical so it never sinks below **terrain + 3.8**; on a hard hit, force the
   yaw-rate to **-0.01** to stop the camera fighting the ground. (solid)
2. **Occlusion pull-in.** Probe the line from the focus to the eye against world
   solids; if blocked, pull the eye in and/or nudge pitch (no-collision correction
   step **50.0**) to keep the target visible. (plausible)

> The terrain grid origin (**10000**) and cell size (**1024**) here are the **same
> constants** the movement collision system uses (§B.4) and match the terrain/cell
> tiling documented by the asset analysts — a useful cross-check, not an independent
> derivation. (solid)

## A.7 Projection / field-of-view (the camera object)

The perspective camera holds field-of-view (vertical), aspect, near, far, and a
computed view volume (top/bottom/left/right) plus the embedded frustum.

- **View-volume derivation** (centered case): `top = near · tan(fovYaw/2)`,
  `bottom = -top`, `right = top · aspect`, `left = -right`, then build the 6-plane
  frustum. An off-center mode re-derives field-of-view from explicit L/R/B/T. (solid)
- The client-side projection setup initializes with **vertical FOV = 60°** and a
  near-distance constant of **10000.0**; a **π/8 (≈ 0.3927 rad, ≈ 22.5°)** half-angle
  constant also appears in the same setup (consistent with a 45° vertical FOV).
  **Which FOV reaches the live projection is (UNVERIFIED).** (the input/UI spec
  separately records a main-window perspective camera at "FOV 65°, near 5, far 15000";
  these three FOV figures — 60 / 45 / 65 — are **not yet reconciled** and must be
  treated as UNVERIFIED until a single authoritative runtime value is confirmed.)

## A.8 Camera persistence (local config, not networked)

The chosen view mode is written to the **local options/config file**, never to the
server. Two config tokens, **`CAMERA_XZ`** and **`CAMERA_XYZ`**, distinguish a
2-axis vs 3-axis camera-follow option in the saved options (e.g. lock camera vertical
to the player vs free 3-axis). Their exact runtime consumer is **(UNVERIFIED)**.
Any Korean labels in this option table are CP949-encoded. (plausible)

---

# Part B — Client movement & collision

## B.0 Mental model

The local player owns a "mover" sub-object. On a left-click the input handler
**unprojects** the screen pixel through the perspective camera to a world-space
ground point (X, Z), runs a **walkability / collision test** along the straight line
from the player's current position to that point, and — if legal — sets the
destination and starts forward integration. **Each frame** the mover advances the
player by `speed · 4.0` along the facing direction, re-tests collision against the
cell's 2D solid quadtree, clamps to the nearest blocking segment when blocked, and
snaps to the destination on arrival. Independently, a worker thread emits a periodic
**move-request heartbeat** so the server can reconcile position; the server echoes
movement back. **World vertical (Y) is never on the wire and is forced to 0 for
simulation** — the terrain heightmap is a *visual / placement* surface, not
authoritative for the 2D collision. (solid)

## B.1 Networking (cite the catalog; layout owned elsewhere)

These opcodes are already in `Docs/RE/opcodes.md`; this spec adds only client-side
behaviour, **never** their byte layout:

| Opcode | Name | Dir | Catalog status | Role in movement |
|---|---|---|---|---|
| **2/13** | `CmsgMoveRequest` (16 B fixed) | C2S | draft | Client emits on click-to-move **and** as a position heartbeat; carries heading + target XZ + mode/run flags. Server echoes via 5/13. |
| **5/13** | `SmsgActorMovementUpdate` (40 B fixed) | S2C | draft | Server pushes actor movement (current XZ + dest XZ + yaw + run/speed/motion); drives remote-actor interpolation and is the authoritative correction for the local player. |
| **4/13** | `SmsgLocalPlayerStateSync` (var) | S2C | confirmed | Local-player state sync; candidate channel for local-position reconciliation (field layout not specced). |

> **(UNVERIFIED)** which channel actually corrects the *local* player's position —
> 5/13 (actor movement push) vs 4/13 (local-player state sync). This is a
> protocol-analyst question; do not assume.

### B.1.1 Move-request payload (behavioural description only)

The client builds the 2/13 payload as **heading + target X + target Z + a mode/run
flag region** (the committed `packets/2-13_move_request.yaml` owns the exact 16-byte
field split). Behavioural facts for the engineer:

- **Heading** is derived (atan2-style) from the delta between the player's
  interpolation-target XZ and a reference XZ. Angular unit assumed radians,
  **(UNVERIFIED)**.
- **Target X / Z** are the click (or heartbeat) destination.
- The **mode/run region** packs: a mode value (the click-to-move path passes **1**), a
  secondary preset value of **3** (role **UNVERIFIED**), and a **run flag** (the player
  is running when its run-state byte equals 1). The **exact byte split inside the flag
  region is (UNVERIFIED)** and needs a click-to-move capture — this matches the open
  question already recorded on the packet spec. (solid behaviour; split UNVERIFIED)

### B.1.2 Heartbeat cadence (server reconciliation)

A send-worker thread loops on a short sleep (~10 ms per iteration) and conditionally
fires senders, logging when one is "overdue":

| Channel | Overdue-warning threshold | Notes |
|---|---:|---|
| Move heartbeat | **200 ms** | warning only; a moving client emits a stream of 2/13 frames |
| Proxy channel | **400 ms** | separate channel, same loop |

The move heartbeat reads the player's last-network position, then **alternates the
reported X by +20.0 / -20.0** on a parity counter — a deliberate ±20-unit dither so
each heartbeat reports a slightly different point (keeps the server's position state
fresh / defeats static-position dedupe). It calls the 2/13 builder with **mode = 1**
and the dithered (X, Z). (solid)

> **(UNVERIFIED)** whether 200 ms is a hard rate cap or only the overdue-warning
> threshold (the worker fires at most once per distinct millisecond; no separate
> rate-limit literal was found). Implement the heartbeat as a tunable period and do
> **not** rely on the dither (a clean server should not need it).

### B.1.3 Client anti-cheat cadence telemetry

The client carries **client-side** speedhack telemetry that compares server-time and
animation-cycle deltas against a tolerance of **1025 ms** before flagging an anomaly
(emits "speedhack" diagnostic logs). This is anti-cheat, but it **bounds the
legitimate move/cycle cadence**: a faithful client must keep its move cadence within
~1 s of the server's clock. (solid as a bound; not required for re-implementation)

> Note: there is an unrelated external DRM/anti-hack command string that *looks*
> movement-related but is **not** a movement-speed setter — ignore it for gameplay.

## B.2 Click-to-move pipeline

1. **Click handler.** Distinguishes a context/attack click from the primary
   walk-to-ground click. Guards: modal-panel-open checks, a held left-mouse-input
   gate (input id **1013**, assumed left-mouse-hold, **UNVERIFIED**), and a **100 ms
   re-issue throttle** so rapid clicks don't spam move-issues. Then calls move-init. (solid)
2. **Screen-pixel → world ground point (unproject).** Cast the active camera to the
   perspective camera, convert the pixel to normalized device coordinates using the
   viewport size, unproject through the camera basis, and intersect the resulting ray
   with the ground plane to get the world (X, Z). A **pick ray length / max pick
   distance of 1000.0** is seeded before the pick. (solid)
3. **Move-init / path setup.** Bail if the player is not ready or is in a lock-state.
   Compute the direction from the player's last-network position to the target; if the
   **squared distance exceeds 144.0** (i.e. **> 12 units**), **clamp the step** by
   scaling the unit direction to length **12.0** (max advance per move-issue;
   144 = 12²). Seed a mover speed of **1000.0**, set the from/to path record, and
   commit. (solid)
4. **Commit.** Enforce a per-mover **cooldown gate** (a millisecond-clock comparison)
   and a busy/lock check. Commit only if the **squared distance from current to
   destination exceeds 4.0** (i.e. **> 2 units**) — a dead-zone that ignores tiny
   moves. On commit, step the mover and drop a ground click-marker via the highlight
   texture manager. (solid)

## B.3 Per-frame movement integration (walk / run)

Invoked from the per-frame terrain update and from the move commit:

1. Face the actor toward its destination and copy the facing orientation.
2. Compute the forward step distance = **`speed · 4.0`**, where the per-frame speed
   scalar is the mover's current walk/run speed (set from config — §B.5) and **4.0**
   is a fixed step multiplier. Rotate the facing orientation to produce the candidate
   next world position (current XZ + rotated step). (solid)
3. If the candidate overshoots the destination **and** the collision subsystem
   reported a block, **snap to the corrected/clamped point** from the collision result
   (the wall-slide / stop response). Otherwise snap to the destination. (solid)
4. Write the resulting position into the actor (and into any coupled/mount-partner
   actors) and re-arm the walk motion; if the motion cycle ended, reset to the default
   motion. Some branches also emit a 2/13 frame as part of the moving-state stream. (solid)

### Walk vs run

Walk/run manifests three ways, kept consistent:

- A **lifecycle/motion-state** value: **2 = walk, 3 = run** (8 = dead/scripted). (solid)
- The **run flag** on the wire (the actor's run-state byte == 1 means running). (solid)
- A **different per-frame speed scalar** feeding the `· 4.0` step. The concrete walk
  vs run *numbers* are **table-driven** (§B.5), not code literals. (solid)

## B.4 Collision against static solids (2D XZ swept-segment query)

Collision is **strictly 2D in the XZ plane (no vertical component).** It is a **swept
test** of the player's movement line segment (from-XZ → to-XZ) against static solid
line segments, organized in a per-cell spatial quadtree. The static solid *format*
itself is owned by the asset specs; this section describes only the **query/movement
side** the mover calls. (solid)

Behavioural pipeline (named by role, addresses stripped):

1. **Resolve collision across cells.** Build the from/to XZ segment; query the current
   cell; if no hit, compute the neighbour cell the move crosses into and recurse across
   the boundary. The design **asserts a single move crosses at most one cell boundary.** (solid)
2. **Cell-walkability / bounds check.** Validate the destination cell is loaded and the
   point is in-bounds; includes a point-in-solid test. Returns walkable yes/no. (solid)
3. **Per-cell nearest-hit query.** If the cell has solids, run the sweep seeded with a
   max-float nearest-distance. (solid)
4. **Grid query.** Map the move-segment's bounding box into a **16 × 16 grid of
   quadtrees** (indices clamped 0..15), run the recursive sweep per overlapped bin, and
   dedup by a per-frame visit tag. (solid)
5. **Quadtree recursive descent.** Test the query box against each of 4 child quadrants
   (split on X then Z), recurse, and at a leaf run the segment sweep. (solid)
6. **Leaf segment sweep.** Iterate the leaf's segments, keep the **nearest** intersection
   (minimum squared distance) — hit point + hit solid. (solid)
7. **Single-segment swept test.** Box-overlap AND line-intersection AND two
   point-in-box tests. The line math uses a `Z = slope·X + intercept` form with a
   vertical-segment special case (type flag). (solid; vertical-segment case **UNVERIFIED** by sample)

The resolver returns the **nearest** hit point; the mover reads it back and clamps the
player to it (the wall-slide / stop response of §B.3 step 3).

### Cell / grid index math (shared with the camera, §A.6)

- **Cell size = 1024.0** world units (reciprocal **1/1024 = 0.0009765625**).
- **Cell index base = 10000**; a cell (X or Z) index is computed roughly as
  `10000 − (int)(coord · -1/1024) − cellOrigin`. This matches the asset filename
  pattern for cells clustered around index 10000.
- **16 × 16** quadtree bins per cell.

(All **(solid)**; these are the same origin/scale the camera terrain clamp uses.)

### Out-of-bounds snap-back

When a target cell is invalid / out of loaded space, a snap helper rewrites **both**
the player's current position and its destination to a corrected point and re-steps
the mover. This is the local-side correction when a move would leave walkable space. (solid)

### Walkability gate before committing a click

Before committing a click move, the from(current)→to(target) segment is tested: either
snap back (invalid cell) or return "move allowed" = *(not blocked-by-solid) OR
(target cell valid)*. (solid)

## B.5 Speed constants (table-driven via map config)

The **concrete walk/run speeds are NOT code literals.** They come from a per-map
config table keyed **`MAP_SPEED`** (load failure logs "MAP_SPEED data load error"),
parsed by the map/scene config keyword parser, and end up in the actor/mover speed
field. So to faithfully reproduce feel you need the per-map data values, not the code.
**Actual numeric walk vs run speeds are (UNVERIFIED) — they live in map data, not the
binary.** Any Korean text in this config is CP949. (solid that it is data-driven)

### Fixed numeric movement constants (code immediates)

| Role | Value | Where | Meaning |
|---|---:|---|---|
| Step multiplier | **4.0** | per-frame mover step | forward distance = speed · 4.0 |
| Move-step clamp distance | **12.0** (sq = 144.0) | move-init | max advance per move-issue when far from target |
| Move dead-zone (sq) | **4.0** | commit | ignore moves shorter than 2 units |
| Pick ray length | **1000.0** | move-init | screen-pick ray length / max pick distance |
| Cell size | **1024.0** | cell index math | world units per terrain/collision cell |
| Cell index base | **10000** | cell index math | base index for cell (X, Z) ids |
| Sub-grid | **16 × 16** | grid query | quadtree bins per cell |
| Heartbeat dither | **±20.0** | heartbeat | alternating X jitter per heartbeat (do not port) |
| Click re-issue throttle | **100 ms** | click handler | min interval between re-issued click moves |
| Move-send overdue warning | **200 ms** | send worker | "send move wait > limit" |
| Proxy-send overdue warning | **400 ms** | send worker | "send proxy wait > limit" |
| Speedhack tolerance | **1025 ms** | anti-cheat telemetry | server-time / cycle-delay anomaly bound |

All values **(solid as code immediates)**, governing-at-runtime **(plausible)**.

## B.6 Vertical / height (visual only)

World vertical (Y) is **forced to 0 for simulation** and **never sent by the server.**
The terrain heightmap (a 65 × 65 = 4225-vertex f32 grid spanning one 1024-unit cell →
**16-unit vertex spacing**) is used for **rendering / visual vertical placement** (and
possibly the camera height clamp of §A.6), **not** for the XZ collision, which is the
2D solid quadtree only. The runtime function that bilinearly samples Y for an arbitrary
XZ was **not pinned** — **(UNVERIFIED)**; flag for a focused pass if visual height is
needed. (solid for the model; sampler UNVERIFIED)

---

# Part C — Reimplementation notes (.NET / Godot)

These are guidance, not faithful-copy mandates; deviate deliberately and document it.

- **Camera lives in Godot (presentation), not in the core.** The camera is purely
  client-side and never authoritative, so it belongs in the Godot layer as a passive
  rig. Model the five view modes as a `CameraViewMode` enum + a per-mode rig; preserve
  the **enable-one-disable-others** switch model conceptually but implement it as a
  single active mode. Drop the out-of-world Select mode into the character-select
  scene. The constant tables in §A.3–§A.6 are a tuning starting point — expose them as
  exported/config values, not hard-coded magic numbers.
- **Movement simulation lives in `Client.Domain` / `Client.Application`** (engine-free,
  deterministic, server-reusable). Model: click → unproject-to-XZ (Godot supplies the
  ray) → walkability (2D solid quadtree) → integrate (`speed · 4` per fixed tick,
  clamp 12u per issue, 2u dead-zone) → snap on arrival → emit a 2/13 move-request.
  Keep **Y = 0** in the simulation; treat terrain height as a Godot-side visual lift.
- **Heartbeat:** implement as a tunable periodic 2/13 emitter; **do not** reproduce the
  ±20 dither or rely on the 200/400 ms warning thresholds as protocol guarantees.
- **Encoding:** decode any legacy config / option Korean text (`CAMERA_XZ`/`CAMERA_XYZ`
  labels, `MAP_SPEED` table) as **CP949**, then carry as UTF-8 internally.
- **Determinism caution:** the original integrates on a variable per-frame delta. The
  .NET core should integrate movement on the **fixed logic tick** (see
  `specs/game_loop.md` §6) and let Godot interpolate — the `speed · 4` step becomes
  `speed · stepPerTick`, retuned for the fixed rate. This is an intentional divergence.

---

# Part D — UNVERIFIED / open questions (consolidated)

Do **not** hard-code anything in this list without a capture or an analyst cross-check.

**Camera**

1. **Camera action polarity** — which member of the pitch pair (1002/1003) and zoom
   pair (1000/1001) is up/in vs down/out.
2. **Authoritative runtime FOV** — three candidate vertical FOVs are unreconciled:
   60° and ~45° (π/8 half-angle) in the projection-setup path, and 65° recorded in the
   input/UI main-window note. Pick none until confirmed.
3. **`CAMERA_XZ` / `CAMERA_XYZ` semantics** — the exact 2-axis vs 3-axis follow toggle
   the saved option controls.
4. **Gamble / Select UI drivers** — the UI message(s) that rotate/frame these cameras.
5. **Event-camera scripted-path format** — keyframe/parameter block not decoded.
6. A constant mode-tag (value 2) is set in every camera constructor; its meaning is
   unknown (node/camera-type tag?). Cosmetic; not load-bearing.

**Movement**

7. **2/13 mode/run flag byte split** — mode vs run flag vs the preset `3` vs padding
   inside the trailing flag region. Needs a click-to-move capture. (mirrors the open
   item already on `packets/2-13_move_request.yaml`.)
8. **Heading angular unit** — radians assumed; could be a fixed-scale angle.
9. **Local-player position-correction channel** — 5/13 vs 4/13.
10. **200 ms / 400 ms** are warning thresholds, not proven hard rate caps; whether the
    heartbeat is additionally period-capped is unconfirmed.
11. **Per-map walk/run speed numbers** — live in `MAP_SPEED` map config data, not in
    code; require the data tables or a capture.
12. **Terrain height-sample-at-XZ function** — heightmap format is known, the runtime
    bilinear sampler is not pinned (visual-only).
13. **`.ted` vs `.ted.post`** runtime selection (which heightmap the loader prefers).
14. **Input id 1013** assumed left-mouse-hold for the click handler; unconfirmed.
15. **Vertical (type-flag) solid segments** — handled in the line-intersection math but
    not corroborated by any sample.

---

## Provenance

Rewritten (not copied) from two dirty-room recon notes (subsystem keys
`camera_views`, `movement_collision`). All legacy addresses, decompiler-style
identifiers, RTTI class names, vtable offsets, and raw struct offsets were
**deliberately omitted**; only neutral behaviour, formulas, role-keyed constants,
and already-cataloged opcode tuples (2/13, 5/13, 4/13 — see `opcodes.md`) were
promoted. Camera is non-networked. No new opcode is introduced by this spec.
