---
status: confirmed
sample_verified: false
subsystems: [camera_views, camera_constants, movement_collision]
networked: partial   # camera is client-only; movement uses 2/13, 5/13, 4/13
encoding_note: Korean in-game/config text is CP949 (legacy MS949 code page), not UTF-8.
verification: confirmed   # control-flow-confirmed where noted; CYCLE 12 (2026-06-22): near/far slots CONFIRMED (§A.7.1); terrain-height sampler PINNED as per-triangle plane method, NOT bilinear (§B.6); CYCLE 7 (2026-06-20) added the polymorphic class roster + struct/projection offset tables + per-mode inline constants; a few literal FOV/KF values + on-wire value meanings are capture/debugger-pending
ida_reverified: 2026-06-22
ida_anchor: 263bd994
evidence: [static-ida]
conflicts: 2   # (1) FOV stored full-angle/aspect (NO /2); (2) click marker = UserXEffect spawn, not "highlight texture manager" — both reconciled below
---

# Camera, Client Movement & Collision — Clean-Room Specification

> **Verification banner (re-verified 2026-06-16; extended CYCLE 7 2026-06-20, anchor build `263bd994`, evidence: static-ida).**
> Client-side routing, message sizes, struct/field offsets, view-mode wiring, grid/cell index
> math, and the movement-pipeline formulas below are **[confirmed]** by IDB control-flow + operands
> on this build. Server-authored magnitudes (per-map walk/run speed numbers, any damage/cooldown/XP
> magnitudes) and on-wire VALUE meanings (the exact 2/13 trailing flag-region byte split, the
> local-player position-correction channel) are **[capture/debugger-pending]** — do not hard-code
> them. Two prior-doc claims were **corrected** this pass (see "Conflicts reconciled" below).
>
> **Conflicts reconciled this pass:**
> 1. **§A.7 FOV internal storage.** The prior doc said the in-world FOV is stored "in half-angle
>    form (`π × 65 / 180 / 2 / aspect`)." It is **not**: the build path stores the **full vertical
>    FOV in radians ÷ aspect** (`π·65/180/aspect`, **no `/2`**); the half-angle (`tan(fovY/2)`) is
>    applied only later at frustum derivation. Corrected in §A.7. Not load-bearing for Godot
>    (which takes a vertical-FOV-in-degrees directly), but the storage description is now accurate.
> 2. **§B.2 / §B.4 click ground-marker.** The prior doc said the click marker is dropped "via the
>    highlight texture manager." It is actually a **ground click-marker effect (a user-effect spawn,
>    effect id 380000000)** at the click XZ with Y from the terrain sampler, after clearing the prior
>    marker. Corrected in §B.2 step 4 and §B.4.
>
> **C14 re-confirmation (world-camera follow-up).** The fixed-radius orbit follow math in
> §A.4 / §A.5 was independently **re-confirmed [confirmed]** by a later gentle re-walk: eye-offset
> seed **(−750, 0, +500)** (radius ≈ **901.39**), focus-Z **−40** (Third), default pitch **−π/6**
> clamped **[−90°, −12°]**, yaw seed **0** (Third) — no change to the numbers, only added confidence.
> (The follow-up note also observed a 60° FOV on the projection it sampled; per §A.7 the in-world
> gameplay FOV is **65°** and the 60° belongs to the separate generic projection initializer, so the
> authoritative gameplay value stays **65°** — see §A.7. Not load-bearing for Godot.) The same pass added **§A.4.1** (how the follow target is *selected*
> — a network-staged target/selection slot) and a one-line note that the world camera carries **no**
> brightness/gamma input (near-black world is an environment/ambient matter, not a camera one).

> **CYCLE 7 deepening (2026-06-20, anchor `263bd994`, static-ida).** This pass pinned the
> *mechanism* behind the view modes and added two offset tables:
> 1. **View modes are dispatched by C++ polymorphism — there is no integer mode enum and no
>    switch-case on the camera update path.** Each mode is a distinct **CameraManipulator
>    subclass** (derived from the in-house engine's transform/scene-node base); a scene controller constructs the one
>    it needs, and the active manipulator's **per-frame virtual update (vtable slot +0x40)** computes
>    the camera pose every frame. The **only** integer discriminator anywhere is the Event mode's
>    internal sub-mode field at object offset **+0x16C** (§A.5 "Event"). See §A.1 and the refined
>    §A.2. **Six** subclasses exist (the brief named 5 — Third/First/Static/Gamble/Event; the binary
>    also carries **Select**, the previously-recovered char-select dolly — §A.5 "Select").
> 2. A **CameraManipulator object-offset table** (§A.9) and a separate **projection-camera
>    (GPerspectiveCamera) offset table** (§A.7.1) were promoted as layout facts. The projection
>    object is a *separate* node from the manipulator.
> 3. Per-mode **inline re-locate constants** (§A.5.3) were extracted directly from each subclass's
>    update method — including the formerly `[capture/debugger-pending re-locate]` Third-person
>    `+3.8` lift / `+2.0` bias / terrain-clip push-out / occlusion items (now CODE-CONFIRMED), plus
>    full First / Static / Gamble / Select / Event constant sets. **Residue:** the literal FOV
>    (~65 world / ~50 select), near (~5), far (~15000), and the char-select KF1 world coords
>    (~512, 87, −9652) are **not** inline immediates — they load from read-only data at
>    scene-construction; they stay carried-from-prior-cycle / DBG-pending (§A.7, §A.5 "Select", §D).

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

Each major claim is tagged inline (these legacy tags map onto the banner's evidence tiers):

- **(CODE-CONFIRMED)** = **[confirmed]** — value lifted directly from binary immediate operands and
  confirmed by control-flow context on build `263bd994`; safe to implement.
- **(SAMPLE-VERIFIED)** = **[sample-verified]** — additionally cross-checked against real shipped asset bytes.
- **(INFERRED)** = **[static-hypothesis]** — single-source behavioural inference with supporting evidence;
  implement but keep a feature flag / make it tunable.
- **(UNVERIFIED)** = **[capture/debugger-pending]** — hypothesis only, or a server-authored / on-wire
  value meaning that needs a live capture or debugger run; do **not** hard-code. Listed again in §D.

All exact numeric tuning constants below were lifted from the binary's immediate
operands; they are **(CODE-CONFIRMED)** as *values present in the code* but
**(INFERRED)** as *the value that governs runtime feel*, because none has been
confirmed against a live capture or recording. Treat the whole constant set as
"faithful starting tune, expose as config."

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
   **One manipulator *class* per view mode**, dispatched by **C++ polymorphism**
   (see §A.2): each mode is a distinct subclass of a common `CameraManipulator`
   base (itself derived from the in-house engine's transform/scene-node base), and the
   active subclass's **per-frame virtual update method (vtable slot +0x40)** computes
   the camera pose. There is **no integer mode enum and no switch-case** on the update
   path — the polymorphic class *is* the view mode. (CODE-CONFIRMED via RTTI + vtable)
   The common base is the in-house engine's transform/scene-node class.

There are **six** manipulator subclasses. Five are in-world / event modes
(Third, First, Static, Gamble, Event); the sixth, **Select**, is non-in-world and is
used only by the character-select / create-preview window. They share a common base
that holds the shared state (§A.4) and the common per-frame skeleton. (CODE-CONFIRMED)

| Mode | Subclass (neutral RTTI name) | Role | Orbits player? | Terrain collision? | User input? | Default? |
|---|---|---|---|---|---|---|
| **Third** | `ThirdCameraManipulator` | Over-the-shoulder orbital follow boom | yes (yaw + pitch) | yes (height clamp + occlusion nudge) | full | **yes** |
| **First** | `FirstCameraManipulator` | Player-attached look cam (orientation × player head/body quat) | yaw/pitch look, very short boom | no | full | no |
| **Static** | `StaticCameraManipulator` | Player-following; yaw from player facing, not mouse | no (yaw not mouse-driven) | no | pitch + zoom only | no |
| **Gamble** | `GambleCameraManipulator` | **Fixed framing of the cube-gamble mini-game; NO user input** | no (fixed eye XZ) | partial (samples terrain for eye Y) | none | no |
| **Event** | `EventCameraManipulator` | Cinematic / cutscene; sub-mode at +0x16C selects orbit vs keyframe track | n/a (built-in orbit or track) | data-driven | none (player loses control) | no |
| *Select* | `SelectCameraManipulator` | Char-select / create-preview entry-dolly (out-of-world; framed by the select window) | entry dolly, no orbit | no | boom + preview-actor turn | n/a |

This matches the input/UI spec (`specs/input_ui.md` §4), which records 5 active
in-world view-platform slots and a single integer "active view index"; Select is the
out-of-world char-select camera and is not part of that in-world set. (CODE-CONFIRMED)

## A.2 View-mode switching model (polymorphism, not an enum/switch)

**Dispatch is by C++ polymorphism, not by an integer mode value.** There is no
`switch (mode)` on the camera update path and no central factory; instead a scene
controller **constructs the manipulator subclass it needs** for the current scene
(in-world scenes build the in-world set; the char-select scene builds Select). The
view mode IS the concrete class. The common base is the in-house engine's
transform/scene-node class. (CODE-CONFIRMED via RTTI + per-class vtable)

The in-world manipulators are constructed once at scene setup and co-exist in the
scene graph; each carries a per-mode **enable flag (object offset +0xE5)** and an
**input-active gate (+0xE4)**. The common per-frame wrapper (the manipulator's
shared per-frame virtual, vtable slot +8) **early-outs if the enable flag is 0**,
otherwise runs a shared base update and then calls the **per-mode virtual update at
vtable slot +0x40** — the subclass method that holds the mode's positioning math. So
at runtime **"switch view mode" = enable the chosen manipulator (set +0xE5) and
disable the others, then apply that mode's projection and persist the localized mode
name.** All six per-mode update methods resolve the local player singleton, use a
millisecond delta-time smoothing, compose a yaw quaternion, and sample terrain height
at the camera XZ. (CODE-CONFIRMED)

First-person and Third-person **share the same input-event handler** (mouse-look). (CODE-CONFIRMED)

### A.2.1 Default mode

**Third-person is the default.** All five in-world manipulators are constructed in
the order Third / First / Static / Gamble / Event (Third is scene-graph slot 0 and
is the initially-active mode). Each manipulator is created with a localized display
label pulled from the message database, in that exact construction order, by
**message-db string id: Third = 2006, First = 2004, Static = 2005, Gamble = 2148,
Event = 2148** (Gamble and Event share id 2148). The saved option `OPTION_VIEW_CHAR`
(INI section `[DO_OPTION]`) is **clamped to the range 1..3** on load, with the floor
value 1 mapping to Third-person. Only the three user-selectable modes (Third / First /
Static) are persisted. Gamble, Event, and Select are **not** part of the saved set.
(CODE-CONFIRMED)

### A.2.2 Mode-switch trigger points (exactly three call sites)

The mode-selector function has exactly **three** caller sites, all client-side UI or
keyboard — there is **no combat-enter camera transition and no packet that switches
the gameplay camera**: (CODE-CONFIRMED)

1. **View menu / hotkey dispatcher** — processes UI command codes (e.g. codes 300
   and 301) and a keyboard path; converts the command to a view index (1 or 2) and
   calls the selector. This is the in-game "change view" menu or bound key.
2. **ESC reset** — on ESC (key 27) with no modal panel open, the keybind handler
   calls the selector with index 0, snapping back to **Third-person**.
3. **Second keybind site** in the same handler (paired with the right-drag look gate;
   see §A.3.2).

Separately, the **video-options apply** path writes `OPTION_VIEW_CHAR` alongside
other graphics options, so the view-mode choice is also exposed as a graphics-
options setting. The only server influence on any camera is the map region index
that keys the Event camera curve (§A.5, "Event" row), which is read from the
world-state packet — it is not a view-mode switch.

### A.2.3 Cinematic lock

A separate global **cinematic-lock flag** suppresses *all* in-game camera input and
positioning while set. The gameplay cameras (Third etc.) check this flag and skip
input and repositioning while it is non-zero — i.e. during Event-camera sequences
the player loses camera control. The Event camera itself runs *during* that lock
(inverse sense). Setting and clearing this flag is what transfers control to and
from the Event camera. (CODE-CONFIRMED; the precise host field is dirty-only and
intentionally omitted from this spec)

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

> **(INFERRED/configurable)** the *polarity* within each pitch pair (1002 vs 1003)
> and each zoom pair (1000 vs 1001) — which member is "up" / "in" vs "down" /
> "out". Both members of a pair feed the same integrator with opposite sign; the
> assignment of sign-to-member is inferred, not proven. Implement as a configurable
> +/- pair.

These camera action IDs belong to the broader input-binding map and should be
cross-linked when the input subsystem is fully specced. They are *not* the same
numeric space as the movement / left-click IDs in Part B.

### A.3.2 Mouse-look (shared First/Third event handler)

The mouse-look handler dispatches on the pointer event type (move / button-down /
other-button / wheel-or-extended). Behaviour: (CODE-CONFIRMED)

- While the **suppress modifier** (action 1012) is active, look mode is disabled and
  the drag-active state is cleared.
- **Right-button drag begins:** on right-button press, set drag-active and record the
  anchor cursor position.
- **Mouse move while dragging:** horizontal cursor delta (`cursorX − anchorX`) feeds
  the yaw-rate integrator; vertical delta (`anchorY − cursorY`) scaled by the
  **mouse-drag pitch gain = 5e-4** feeds the pitch-rate integrator.
- **Direct mouse axis:** raw mouse delta scaled by the **mouse-axis sensitivity = 1e-6**
  integrates into pitch, clamped to **[−4.0, +4.0]**; overflow beyond the clamp
  spills into a pitch-overflow accumulator and is re-clamped against a ceiling of
  **27.0** (paired with a secondary **26.0** limit). (CODE-CONFIRMED for values;
  INFERRED for overflow semantics)
- **Other-button / wheel:** zoom delta is scaled by **0.01** and the drag accumulator
  is reset.

**Left-click is click-to-move (Part B), not camera look. Right-button drag is the
only mouse rotate gesture.** (CODE-CONFIRMED)

## A.4 Shared per-mode update math

Each enabled manipulator runs this skeleton per frame (Third/First/Static/Gamble):

1. Resolve the follow target (the local player), lazily cached once per mode. The *identity* of
   that target is not hard-wired — it is the actor the world handler published from the
   network-staged target/selection slot when the world scene was built (see §A.4.1). With no
   actor staged in that slot the camera resolves nothing and follows nothing. [confirmed]
2. Read the elapsed time since the previous frame in milliseconds and convert to
   seconds by multiplying by **1e-3**; a secondary smoothing path for the distance
   integrator uses **1e-4**. (CODE-CONFIRMED)
3. Multiply by the per-node **speed/time-scale factor** (default **1.0**). (CODE-CONFIRMED)
4. Integrate polled / mouse input into the yaw-rate, pitch, and zoom state.
5. Apply **gain**, **friction**, **dead-zone**, and **clamps** (table below).
6. Compose the orientation from a yaw quaternion (about the up axis) and a
   pitch/elevation component.
7. Place the eye relative to the focus point using the fixed-radius orbit model.
8. (Third only) run terrain collision (§A.6), then push the composed transform to
   the camera object and trigger a cull/refresh.

### Fixed-radius orbit model — important correction

**The camera is a FIXED-RADIUS orbit, not a spring-arm.** Eye position = focus point
plus the eye-offset vector rotated by the current orientation. The eye-offset vector
is **never scaled by any distance scalar**; its magnitude is the constant follow
radius. There is **no minimum or maximum distance clamp and no zoom-changes-radius
behaviour.** "Zoom" keys feed the elevation/orbit-step integrator, changing the
vertical angle of the orbit, not the distance to the player. (CODE-CONFIRMED)

### A.4.1 Follow-target binding — how the camera's target actor is selected

The follow math above (§A.4 / §A.5) describes *how* the eye orbits a focus point; this subsection
describes *which actor* becomes that focus. The world camera does **not** pick the local player by a
hard-coded handle — it follows whatever actor is staged in a small **target/selection slot**, which
the world handler resolves to an actor object and publishes as the camera's selection at scene build.

**The target/selection slot (neutral description).** The client keeps a small table of fixed-stride
target/selection slots in static data. Each slot holds a 32-bit actor **id** at its start; an active
target slot is paired with a 16-byte (four-float) **world position** (XYZ plus a flag/pad). One
slot is the active world/camera target: it carries the id of the actor the camera should frame, and
the paired position carries that actor's last known world XYZ. A slot id value of `0xFF` is treated
as 'no entry' and is ignored by the slot setters. [confirmed]

**Where the world handler binds the camera target.** When the in-game scene is set up (the same path
that builds the world scene graph and the five manipulators), the handler reads the active target
slot's id. If it is non-zero it resolves that id through the actor manager to a live actor object,
copies the three position floats from the paired 16-byte target position into a local vector, and
calls the publish-selection routine with that actor plus position. The publish routine writes the
selection into the world window's target block and, when its flag is set, copies the position into
the global target position (otherwise it zeroes it). This is the single site that 'sets the local
actor as the camera target' at world entry. [confirmed]

**Where the id is staged (network).** The actor id reaches that slot from the server world-state
snapshot — the S2C game-state-tick / world-snapshot handler (the `SmsgGameStateTick` family). On
each snapshot that handler sweeps both the actor mirror table and the target/selection table,
prunes stale entries, and — when the active target slot id is non-zero — runs the target-update
path on the world/actor manager and refreshes the paired target position. So the camera's follow
target is **(re-)bound from the server world-state packet** on entry and refreshed on each snapshot.
With nothing ever staged (the port never receiving/synthesising a world-state tick that names a
local actor), the slot stays empty, the bind resolves nothing, and the actor count stays 0 — the
port's observed 'Actors: 0, camera frames nothing' symptom. [confirmed]

> The **per-snapshot re-bind** (the slot being refreshed on every world-state tick rather than only
> once at entry) is a runtime detail the dirty-room note flags as derived from static control-flow
> only — treat the once-per-snapshot refresh cadence as **[static-hypothesis]** pending a live
> debugger confirmation; the *existence* of the entry bind and the slot→actor→camera path is
> **[confirmed]**.

**Brightness is not a camera input.** The world camera object is pure projection + orbit geometry
(FOV / aspect / near / far / the off-centre perspective matrix and the fixed-radius orbit transform).
It carries **no** brightness or gamma input on the camera or any manipulator. A near-black world is
therefore an **ambient / environment-lighting** matter, to be fixed independently of the follow
camera (see `specs/environment.md §6.2b`; the `DISPLAY_BASE_BRIGHT_MULTI = 1.05` display factor is
neutral and is **not** the cause). [confirmed]

**Port guidance.** To render the world the port must do *both*: (a) drive the scene state to the
in-game value so the world handler is constructed (see `specs/client_workflow.md §4` / §5.4), **and**
(b) spawn and register a local player actor into the camera's follow-target slot (id + position)
before/while the world scene builds. State alone is not enough — with no actor in the slot the camera
frames nothing and the actor count stays 0. Fix near-black on the environment/ambient side, not the
camera side.

### Shared smoothing / clamp constants (CODE-CONFIRMED as code immediates; INFERRED as runtime feel)

| Role | Value | Notes |
|---|---:|---|
| Time delta scale (ms → s) | **1e-3** | applied to per-frame elapsed ms |
| Distance-integrator smoothing | **1e-4** | secondary, slower time scale |
| No-input rate decay (friction) | **0.6** | multiplies a rate each frame when no input (Static uses **0.8**) |
| Keyboard input → rate gain | **0.3** | per-frame step added on key |
| Zoom-rate / orbit-step clamp | **[−0.1, +0.1]** | bounds the per-frame orbit-step angular velocity |
| Pitch-rate clamp | **[−4.0, +4.0]** | bounds the pitch state |
| **Elevation angle clamp (final)** | **[−90.0, −12.0]** | the real pitch limit in degrees; hard floor and ceiling |
| **Absolute-yaw clamp** | **[−π/2, +π/2] = [−1.5708, +1.5708]** | base bound; Third upper eased to **+1.4137** |
| Third yaw upper ease factor | **0.9** | Third upper-yaw = π/2 × 0.9 ≈ +1.4137 |
| Gamble yaw clamp | **symmetric ±π/2** | no 0.9 ease |
| Rate dead-zone | **1e-3** | rates with magnitude below this snap to 0 |
| Mouse-drag pitch gain | **5e-4** | cursor-Y delta → pitch rate |
| Mouse-axis sensitivity | **1e-6** | raw mouse delta → pitch |
| Pitch-overflow ceiling | **27.0** (with **26.0** re-clamp) | spill clamp for over-driven pitch; never affects the eye |
| Wheel / other-button zoom scale | **0.01** | scales zoom on the other-button/wheel path |
| Terrain-collision camera lift | **3.8** | eye Y ≥ terrain + 3.8 |
| Collision Y-bias step | **+2.0** | added to the clamped terrain Y |
| Terrain hard-hit yaw kill | **−0.01** | forced yaw-rate value on a hard terrain hit |
| Occlusion pitch nudge | **50.0 · dt** (+ 0.01 correction) | pitch step when occlusion probe blocks the line |

All values **(CODE-CONFIRMED as code immediates)**; runtime feel **(INFERRED)**. Expose as a config block.

## A.5 Per-mode behaviour

### A.5.1 Shared base seeds (all five in-world manipulators inherit these, then override)

| Field | Base seed | Notes |
|---|---:|---|
| Speed / time-scale factor | **1.0** | |
| Accumulated yaw | **−π/6 = −0.5236 rad (−30°)** | base for Third / First / Static / Event |
| Eye-offset vector | **(−750.0, 0.0, +500.0)** | magnitude **≈ 901.39 units** = fixed orbit radius for player modes |
| Focus / look-at Z | **−40.0** | base / Third |
| Pitch-overflow spill bucket | **10.0** | mouse-pitch overflow accumulator clamped [0, 27]; **not a follow distance** |

### A.5.2 Per-mode table (CODE-CONFIRMED)

| Mode | Default pitch | Focus Z | Yaw-orbit seed | Follow radius | Terrain collision |
|---|---|---:|---|---:|---|
| **Third** (DEFAULT) | −π/6 (−30°) | −40 | 0 | ≈ 901.39 | yes (height clamp + occlusion) |
| **First** (first-person) | −π/6 (−30°) | −55 | **π** (180° flip) | ≈ 901.39 | no |
| **Static** (fixed-angle follow) | −π/6 (−30°) | −55 | 0 | ≈ 901.39 | no |
| **Gamble** (orbit, betting minigame) | −π/3 (−60°) | −160 | 0 | ≈ **60 684** | partial (world-AABB probe) |
| **Event** (scripted / cutscene) | −π/6 (−30°) | −55 | **π** | built-in curve | data-driven |

Per-mode detail notes:

- **Third.** Full input pipeline (§A.3); yaw clamp **[−π/2, +1.4137]** (asymmetric, 0.9-eased upper);
  elevation **[−90°, −12°]**; friction 0.6, gain 0.3. The only mode that runs both terrain collision
  behaviours. Focus = the player's head position when available, else the player's ground position
  with vertical forced to 0.

- **First.** Shares Third's input pool and constant pool (same boom machinery). The camera
  **orientation is multiplied by the player's head/body orientation quaternion** (read from the
  local-player object at offset **+0x45C**) so the view looks where the avatar points; full
  yaw/pitch/zoom input. Eye height = sampled terrain **+3.8**, look-at offset **+2.0**. Despite the
  name it is a very short boom, not a true 0-length head cam, so the trailing follow distance nearly
  collapses. No terrain collision. Yaw-orbit seeded to π (initial 180° facing flip). Pitch limit
  **±π/2**, hard-clamped at **+4.0**; boom-distance delta clamp **±4.0**; input rate / damping /
  decay = **0.3 / 0.6 / 0.0001**. (CODE-CONFIRMED; visual effect of π-flip INFERRED)

- **Static.** Player-following but **non-mouse-yaw**: yaw is taken from the player's facing/heading
  field (the player heading), not from the mouse; only **pitch (mouse)** and **zoom (scroll)** are
  user-driven. Eye = player position (read from the local-player object at offset **+0x428**)
  projected back along the boom; eye height = sampled terrain **+2.0**, look-at offset **+2.0**.
  Pitch limit **±π/2**, hard-clamped at **−8.0**; boom-distance delta clamp **±4.0**; scroll-zoom
  rate **50.0·dt**, damping **0.8**. A fixed/over-the-shoulder framing that auto-aligns behind the
  player. No terrain collision. (CODE-CONFIRMED)

- **Gamble.** **Fixed-framing camera with NO user input** — it frames a fixed mini-game / gambling
  location (corroborated by the `data/ui/cubegamble*.dds` cube-gamble UI assets and a `Gamble` RTTI
  class). The eye XZ is **hard-pinned to literal world coordinates: X = 24097.46484375,
  Z = 55694.4296875**; the eye Y is taken from the **sampled terrain height + a height bias**
  (manipulator offset +0x164); the **look-at = the player** (read from local-player offset +0x428).
  Pitch limit **±π/2**. (This sharpens the prior "far-orbit UI camera" reading: it is a *fixed*
  framing, not an orbit, and takes no movement/UI orbit input.) (CODE-CONFIRMED for values)

- **Event (cutscene).** Runs *during* the cinematic lock (§A.2.3). The sub-mode is the **only
  integer discriminator** in the whole camera system, held at manipulator offset **+0x16C**:
  - **Player-orbit sub-mode (sub-mode == 2):** eye locks to the player's last network position and
    orbits the player with an **eased yaw rate (0.5·dt, clamped 1.0)** at a fixed **boom distance
    12.0**, with the look-at lifted **+8.0** above the player; runs for a fixed duration of
    **12 000 ms**. (CODE-CONFIRMED)
  - **Keyframe-track sub-mode (any other value):** plays an authored **2-track MotionClip cutscene**
    — track index 0 is the **eye** track and track index 1 is the **look-at** track, sampled from
    the clip's track block (clip record + 0x34). The timeline advances at **t = (elapsed_ms / 1000)
    · 3.0** (3× speed). A fixed **basis-correction quaternion (0, 0, −0.70710677, 0.70710677)**
    (a −90° rotation about the Z axis) is applied to the sampled eye/look basis. The cut **ends when
    its status reaches 100**, at which point the scene is set to mode 2 and the camera is popped.
    The look-at target is read from manipulator offset +0x188. The earlier "built-in 17-entry ×
    3-float region curve indexed by map region" reading from a prior cycle is **superseded** by this
    MotionClip-track model for the non-orbit Event path. Cinematic duration is still tied to a
    motion clip's length, not to a `.scr`/Lua script. (CODE-CONFIRMED)
  - **This camera is NOT data-file-driven, NOT Lua-scripted, and NOT driven by `.scr` files.**
    The `data/script/*.scr` files are CSV data tables (quests, items, NPC, map settings, etc.) and
    are not camera-path sources. Lua in this client is config/UI/localisation only — there is no
    camera or cinematic Lua API. A prior IDB auto-comment suggesting `.scr` file playback is
    inaccurate. (CODE-CONFIRMED)
  - The exact 17 curve triples can be recovered from the binary table if cut-scene support is
    ever needed; they are **not required for normal town/field play**, which uses only Third-person.

- **Select (char-select preview). ENTRY DOLLY — see `frontend_scenes.md` §3.5 for the authoritative model.**

  > **Two prior readings retracted (CAMPAIGN 9 re-walk).**
  > 1. The **multi-waypoint / orbiting** reading — a literal world-position waypoint table with
  >    framing positions and span constants — is **retracted and NOT the model**. Do **not** implement
  >    the old waypoints (−1532, 137, −3254) / (−1705, −3508, 87) / (−1577, −3590, 104) or the
  >    2048 / 6144 / −1536 span constants; there is **no full orbit** and **no multi-waypoint travel**
  >    armed in the char-select scene.
  > 2. The follow-on **single static camera** reading is **also retracted** — it was itself an
  >    over-correction that examined only the bare projection camera and missed the separate
  >    camera-path rig. The char-select camera is **neither a full orbit nor a static camera**.

  **What the char-select preview camera actually is (authoritative model lives in
  `frontend_scenes.md` §3.5):** an **entry dolly**. On scene-enter the camera-path rig blends from
  keyframe **KF0 → KF1** over **~2.0 s** — a **position-Lerp** plus an **orientation-Slerp** — then
  holds at KF1. The rig is a 6-keyframe path object, but only keyframes 0 and 1 are ever armed in
  char-select (the scene constructs at KF0 and the entry reset arms KF1); the remaining keyframes
  exist but are never advanced — there is **no auto-orbit and no select-focus retarget**. It has:

  - a fixed projection — **50° vertical FOV, near 5.0, far 15000.0** (note: this differs from the
    in-world gameplay camera's 65° — §A.7);
  - a fixed path base anchor at **(2048, 0, −6144)** (the select-stage backdrop anchor);
  - the recovered destination keyframe **KF1 = (512, 87, −9652)** (the eye the entry dolly settles to);
  - a continuous **manual overlay** that rides on top of the keyframe transform: a hold-to-zoom
    **camera boom/zoom** and a manual **preview-character turn** (the previewed actor — *not* the
    camera — yaws while a turn key is held). **Slot-select moves the ACTOR, not the camera** — the
    "focus" on a chosen character is the preview actor turning to face, not a camera move.

  This is **out-of-world** and is **not** part of the in-world mode set. **For the complete, current
  char-select camera/preview spec, defer to `frontend_scenes.md` §3.5** — it owns this scene (and is
  being updated to this entry-dolly model in the same wave); the rows here are only a cross-link.
  (CODE-CONFIRMED: entry dolly KF0 → KF1, pos-Lerp + orient-Slerp over ~2.0 s; FOV 50 / near 5 /
  far 15000; path anchor (2048,0,−6144); KF1 (512,87,−9652); manual boom/yaw overlay; slot-select
  moves the actor not the camera. Recovered via static RE, CAMPAIGN 9.)

  **CYCLE 7 — the dolly *math* is inline (the dolly *coords* are not).** The Select subclass's
  per-frame update computes the blend as follows (all inline immediates, CODE-CONFIRMED):
  - **Blend factor** `t = elapsed_ms · 0.0005` (i.e. `elapsed_ms / 2000`, a ≈0.5 s ramp), clamped to
    a maximum of **1.0**; the dolly start timestamp is stored at manipulator offset **+0x238**.
  - **Ease curve** applied to `t`: **`(1 − t) · (2t)`**.
  - The **eye is Lerped** between the "from" keyframe eye and the "to" keyframe eye, and the
    **orientation is Slerped** between the from/to keyframe quaternions, by the eased `t`.
  - A continuous overlay eases **yaw/pitch toward goal angles** (yaw goal at +0x1B0, pitch goal at
    +0x1B4), with input rate **0.1·dt**, dampings **0.5 / 0.8**, and a yaw/pitch clamp **±1.0**;
    scroll zoom rate **10.0·dt**, damping **0.8**; boom-distance delta clamp **±4.0**.

  **Residue (DBG-pending).** The literal char-select world coordinates of the keyframes — the known
  **KF1 ≈ (512, 87, −9652)** from prior cycles, the **KF0** start, and the **(2048, 0, −6144)** path
  anchor — are **NOT inline immediates** in the Select update method. They are **data-driven keyframe
  slots written at scene setup** (the "from" eye region at manipulator offset +0x178/+0x194 and a
  "to" region further on). Their *values* therefore remain **scene-construction / DBG-pending** and
  are carried from prior cycles; only the dolly *math* above is inline-confirmed this pass.

### A.5.3 Per-mode inline re-locate constants (CYCLE 7, read from each subclass's per-frame update)

Every value below is an **immediate operand inside the named mode's per-frame update method** on
build `263bd994` — **(CODE-CONFIRMED as a code immediate)**; runtime feel **(INFERRED)**, expose as
config. These *replace* the prior `[capture/debugger-pending re-locate]` flag on the Third-person
`+3.8` / `+2.0` / push-out / occlusion items (those are now re-located and confirmed). The shared
`dt = elapsed_ms · 0.001 · speedMultiplier(+0xE0)` conversion (§A.4) is used by **all** modes.

| Mode | Constant role | Value | Confidence |
|---|---|---:|---|
| **Third** | eye height above sampled terrain | **+3.8** | CODE-CONFIRMED |
| **Third** | look-at / boom-base height offset | **+2.0** | CODE-CONFIRMED |
| **Third** | pitch soft limit | **±(π/2 · 0.9) ≈ ±1.4137 rad (~±81°)** | CODE-CONFIRMED |
| **Third** | pitch hard clamp-down floor | clamp `> −12.0` → set **−12.0** | CODE-CONFIRMED |
| **Third** | pitch kill / zero boom-rate | `< −90.0` → zero boom-rate | CODE-CONFIRMED |
| **Third** | boom-distance delta clamp | **±4.0** | CODE-CONFIRMED |
| **Third** | yaw/pitch input rate | **0.3** (per dt) | CODE-CONFIRMED |
| **Third** | input-idle damping | **0.6** | CODE-CONFIRMED |
| **Third** | zoom auto-decay / boom-yaw free-spin decay | **0.0001** per dt | CODE-CONFIRMED |
| **Third** | terrain-clip push-out rate | **+50.0·dt** | CODE-CONFIRMED |
| **Third** | yaw/pitch deadzone clamp | **±0.1** | CODE-CONFIRMED |
| **First** | eye height above sampled terrain | **+3.8** | CODE-CONFIRMED |
| **First** | look-at height offset | **+2.0** | CODE-CONFIRMED |
| **First** | pitch limit | **±π/2 (±1.5707964)** | CODE-CONFIRMED |
| **First** | pitch hard clamp | `> +4.0` → **+4.0** | CODE-CONFIRMED |
| **First** | boom-distance delta clamp | **±4.0** | CODE-CONFIRMED |
| **First** | input rate / damping / decay | **0.3 / 0.6 / 0.0001** | CODE-CONFIRMED |
| **First** | player head-orientation source | local-player **+0x45C** | CODE-CONFIRMED |
| **Static** | eye height above sampled terrain | **+2.0** | CODE-CONFIRMED |
| **Static** | look-at height offset | **+2.0** | CODE-CONFIRMED |
| **Static** | pitch limit | **±π/2** | CODE-CONFIRMED |
| **Static** | pitch hard clamp | `> −8.0` → **−8.0** | CODE-CONFIRMED |
| **Static** | boom-distance delta clamp | **±4.0** | CODE-CONFIRMED |
| **Static** | scroll-zoom rate / damping | **50.0·dt / 0.8** | CODE-CONFIRMED |
| **Static** | player position source | local-player **+0x428** | CODE-CONFIRMED |
| **Gamble** | FIXED eye X (world) | **24097.46484375** | CODE-CONFIRMED |
| **Gamble** | FIXED eye Z (world) | **55694.4296875** | CODE-CONFIRMED |
| **Gamble** | eye Y | sampled terrain **+ height bias (+0x164)** | CODE-CONFIRMED |
| **Gamble** | pitch limit | **±π/2** | CODE-CONFIRMED |
| **Gamble** | player look-at source | local-player **+0x428** | CODE-CONFIRMED |
| **Select** | dolly blend factor | **elapsed_ms · 0.0005** (≈0.5 s ramp, clamp 1.0) | CODE-CONFIRMED |
| **Select** | dolly ease curve | **(1 − t) · (2t)** | CODE-CONFIRMED |
| **Select** | boom-distance delta clamp | **±4.0** | CODE-CONFIRMED |
| **Select** | yaw/pitch clamp | **±1.0** | CODE-CONFIRMED |
| **Select** | scroll-zoom rate / damping | **10.0·dt / 0.8** | CODE-CONFIRMED |
| **Select** | yaw/pitch input rate / damping | **0.1·dt / 0.5 / 0.8** | CODE-CONFIRMED |
| **Select** | yaw goal / pitch goal field | **+0x1B0 / +0x1B4** | CODE-CONFIRMED |
| **Event** | sub-mode discriminator | **+0x16C**: `==2` player-orbit, else keyframe-track | CODE-CONFIRMED |
| **Event (orbit)** | orbit boom distance | **12.0** | CODE-CONFIRMED |
| **Event (orbit)** | yaw ease rate | **0.5·dt** (clamp 1.0) | CODE-CONFIRMED |
| **Event (orbit)** | look-at vertical lift | **+8.0** | CODE-CONFIRMED |
| **Event (track)** | cutscene timeline scale | **t = elapsed_ms/1000 · 3.0** (3× speed) | CODE-CONFIRMED |
| **Event (track)** | basis-correction quaternion | **(0, 0, −0.70710677, 0.70710677)** (−90° about Z) | CODE-CONFIRMED |
| **Event (track)** | MotionClip track block | clip **+0x34**; track idx 0 = eye, idx 1 = look-at | CODE-CONFIRMED |
| **Event (track)** | end-of-cut | status `== 100` → set scene mode 2 + pop | CODE-CONFIRMED |
| **ALL** | dt unit conversion | **elapsed_ms · 0.001 · speedMultiplier (+0xE0)** | CODE-CONFIRMED |

> These per-mode values are consistent with — and finer-grained than — the shared
> smoothing/clamp table in §A.4. Where a mode-specific value differs (e.g. Static damping 0.8 vs
> the shared 0.6, Static/Static-zoom 50.0 vs the keyboard 0.3, Select's ±1.0 vs Third's ±0.1), the
> **mode-specific value governs that mode**; the §A.4 table is the base/Third default.

## A.6 Terrain collision for the Third-person camera

Third-person performs **two** collision behaviours; the other modes do neither:

1. **Terrain-height clamp.** Map the candidate eye's world (X, Z) into terrain-grid
   coordinates (grid origin index **10000**, cell size **1024** world units, i.e. a
   world→grid scale of **−1/1024 = −0.0009765625**, applied as `10000 − (int)(coord · −1/1024)`
   for both X and Z). When a coordinate lands **exactly on a cell boundary**
   (`coord mod 1024 == 0`), a **cell-edge nudge** subtracts **1.1** units from the
   coordinate before re-sampling so the adjacent cell is sampled instead. (This is the
   correct reading of the value previously written loosely as "secondary cell scale/bias
   1024.0 / 1.1" — it is a `−1.1`-unit edge step, not a scale.) Sample the **interpolated
   (bilinear) terrain height** at the eye (X, Z) from the cell's heightmap when the cell
   has geometry; the sampler is a dispatcher that selects between an interpolated
   (bilinear) sampler and a flat/alternate sampler on a per-cell mode field. The
   no-terrain sentinel is **−3.4028e38** (no hit). Clamp the eye's vertical so it never
   sinks below **terrain + 3.8** (with a +2.0 bias step added to the clamped value). On a
   hard hit, force the yaw-rate to **−0.01** to stop the camera fighting the ground.
   **Radius is fixed — there is no horizontal pull-in on collision.** (CODE-CONFIRMED for
   the grid math, the cell-edge `−1.1` nudge, the sentinel, and the bilinear/flat
   dispatcher. **The `+3.8` lift, `+2.0` bias, the terrain-clip push-out, and the pitch
   kill/clamp were re-located and confirmed as inline immediates in the Third-person update
   in CYCLE 7 — see §A.5.3** — so the former `[capture/debugger-pending re-locate]` flag on
   these is **CLEARED**.)
2. **Occlusion nudge.** If the focus→eye line is occluded, nudge pitch by **+50.0 · dt**
   (+ 0.01 correction) to keep the target visible. No radial change. (CODE-CONFIRMED — the
   `+50.0·dt` terrain-clip push-out is an inline immediate in the Third-person update, §A.5.3.)

> The terrain grid origin (**10000**) and cell size (**1024**) here are the **same
> constants** the movement collision system uses (§B.4) and match the terrain/cell
> tiling documented by the asset analysts — a useful cross-check. **The interpolated
> terrain-height-at-XZ sampler is now PINNED** (it is the same dispatcher the
> click-to-move commit uses to place the ground marker's Y — see §B.2 step 4); this
> closes the former open item that listed the bilinear sampler as unverified. (CODE-CONFIRMED)

## A.7 Projection / field-of-view (CODE-CONFIRMED — reconciled)

**The authoritative in-world gameplay FOV is 65° vertical, near 5.0, far 15000.0.**
These values are constructed in the in-world scene-build path (the in-game scene-graph
builder) at the same point the five manipulators are wired to the camera. The FOV is
stored internally as the **full vertical FOV in radians, divided by aspect**
(`π × 65 / 180 / aspect` at build time — **no `/2`**); the perspective-camera setter
validates that the stored angle lies in `(0, π]` and keeps it raw. The half-angle
(`tan(fovY/2)`) is applied only **later**, at frustum derivation (see the view-volume
formula at the end of this section), not at storage time. These are the values to
implement for the gameplay camera. (CODE-CONFIRMED)

> **Correction (re-verified 2026-06-16, anchor `263bd994`).** A prior revision of this
> spec described the stored value as the half-angle `π·65/180/2/aspect`. That is
> inaccurate: the build path stores the **full** vertical FOV (÷ aspect) and the `/2`
> only appears later inside the `tan(fovY/2)` of the view-volume derivation. This is not
> load-bearing for a Godot re-implementation — Godot's `Camera3D.Fov` takes a vertical
> FOV in **degrees (65)** directly — but the internal-storage description is now correct.

Two additional projection values also appear in the binary but belong to a **separate
generic projection initializer** that is not the camera the in-world manipulators
bind to: a seed of **60°** vertical FOV with a near-constant of **10000.0**, and a
**π/8 (≈ 22.5°) half-angle constant** (consistent with a 45° vertical FOV). These
three figures — 60° / 45° / 65° — are not competing in-world FOVs; the 60° and 45°
belong to the generic path; **65° is the in-world gameplay camera.**

> **Prior open item D.2 in this spec is closed.** Implement the gameplay camera
> at **65° vertical FOV, near 5, far 15000**. Live-feel still capture-unverified;
> expose as config but default to 65°.

View-volume derivation (centered case): `top = near · tan(fovY/2)`, `bottom = −top`,
`right = top · aspect`, `left = −right`, then build the 6-plane frustum.
An off-center mode re-derives field-of-view from explicit L/R/B/T. (CODE-CONFIRMED)

> **CYCLE 7 caveat — the FOV/near/far *values* are not inline immediates.** The literal
> ~65° (world) / ~50° (select) field-of-view, near ~5, and far ~15000 are **float constants
> loaded from read-only data at scene-construction time**, not immediate operands in any
> manipulator update or in the projection setter (CYCLE 7 immediate-operand searches for the
> corresponding bit patterns returned no hits in the update/setter path). The CYCLE 7
> contribution is the **field offsets** that carry these values on the projection object
> (§A.7.1); the literal *values* stay where prior cycles hold them and are **DBG-confirmable**
> by reading the fovy field (+0xA8) at runtime. Treat 65° / 50° / near 5 / far 15000 as the
> carried-forward faithful values, not as freshly re-located immediates.

### A.7.1 Projection object (GPerspectiveCamera) — field offsets (separate from the manipulator)

The projection is owned by a **separate `GPerspectiveCamera` node**, *not* by the manipulator —
the manipulator positions/orients it, but field-of-view / aspect / near / far / ortho parameters
live on the projection object. Offsets (bytes from the projection object) are layout facts:

| Offset | Bytes | Role | Confidence |
|---:|---|---|---|
| **+0x94** | byte | ortho flag (1 = ortho left/right/bottom/top; 0 = perspective fovy/aspect) | CONFIRMED |
| **+0x98** | float | ortho right | CONFIRMED |
| **+0x9C** | float | ortho left | CONFIRMED |
| **+0xA0** | float | ortho bottom | CONFIRMED |
| **+0xA4** | float | ortho top | CONFIRMED |
| **+0xA8** | float | **fovy** (perspective vertical field-of-view) | CONFIRMED |
| **+0xB0** | float | aspect ratio | CONFIRMED |
| **+0xAC** | float | **near plane** — CONFIRMED at this slot (CYCLE 12); lives in an early slot of the projection-object field block | CONFIRMED |
| **+0xB4** | float | **far plane** — CONFIRMED at this slot (CYCLE 12); lives in an early slot of the projection-object field block | CONFIRMED |

> **CYCLE 12 (IDB SHA 263bd994, 2026-06-22).** The near/far plane slots at +0xAC/+0xB4 are now
> CONFIRMED (upgraded from MEDIUM). Near and far **live in early slots** of the
> `GPerspectiveCamera` field block, not as inline immediates in any manipulator update or
> projection setter (consistent with the CYCLE 7 finding that the ~5/~15000 values are loaded
> from read-only data at scene-construction, not as code immediates). The field-describe routine
> confirms the layout. The literal FOV/near/far *values* (65° / 5.0 / 15000.0 for in-world;
> 50° / 5.0 / 15000.0 for char-select) remain carried-forward from prior cycles and are
> **DBG-confirmable by reading these fields at runtime**, not inline immediate operands.
> `// spec: Docs/RE/specs/camera_movement.md §A.7.1`

## A.8 Camera persistence (local config, not networked)

The chosen view mode is written to the **local options/config file** under INI section
`[DO_OPTION]`, key `OPTION_VIEW_CHAR`, **clamped to the integer range 1..3** (where
1 = Third, 2 = First, 3 = Static). Gamble and Event are never persisted. On load the
floor value 1 is the default. (CODE-CONFIRMED)

Two additional config tokens, **`CAMERA_XZ`** and **`CAMERA_XYZ`**, distinguish a
2-axis vs 3-axis camera-follow option in the saved options. Their exact runtime
consumer is **(UNVERIFIED)**. Any Korean labels in this option table are CP949-encoded. (INFERRED)

## A.9 CameraManipulator object layout (CYCLE 7 — offsets are layout facts)

Offsets from the manipulator object (`this`) shared by all six subclasses (some fields are
mode-specific, noted in the Role column). These are struct-layout facts, not code addresses.

| Offset | Bytes | Role | Confidence |
|---:|---|---|---|
| **+0x5C** | 64 (Matrix4) | view matrix (rebuilt each frame) | CONFIRMED |
| **+0x8C** | 12 (float[3]) | view-matrix translation row (camera eye X/Y/Z) | CONFIRMED |
| **+0x9C** | 64 (Matrix4) | inverse-orthonormal view matrix | CONFIRMED |
| **+0xE0** | float | camera move/zoom speed multiplier (the dt scale, §A.5.3) | CONFIRMED |
| **+0xE4** | byte | input-active gate (1 = allow input) | CONFIRMED |
| **+0xE5** | byte | manipulator-enabled flag (per-frame update no-ops if 0) | CONFIRMED |
| **+0xE8** | float | pitch angle accumulator (radians) | CONFIRMED |
| **+0xEC** | float | yaw angle accumulator (radians) | CONFIRMED |
| **+0xF0** | 12 (float[3]) | boom forward / look direction (scaled for zoom) | CONFIRMED |
| **+0xFC** | 12 (float[3]) | computed eye position | CONFIRMED |
| **+0x108** | 12 (float[3]) | boom / orbit base offset vector | CONFIRMED |
| **+0x110** | float | boom-pitch (degrees) working value | CONFIRMED |
| **+0x114** | float | boom-distance / zoom accumulator | CONFIRMED |
| **+0x11C** | float | pitch input-rate accumulator | CONFIRMED |
| **+0x120** | float | yaw input-rate accumulator | CONFIRMED |
| **+0x128** | 16 (Quat) | camera orientation quaternion | CONFIRMED |
| **+0x140** | 16 (Quat) | pitch sub-rotation quaternion | CONFIRMED |
| **+0x164** | float | extra eye-height bias (added to terrain sample; Gamble eye Y) | CONFIRMED |
| **+0x16C** | int | **Event sub-mode** (2 = player-orbit, else keyframe-track) | CONFIRMED |
| **+0x178** | 12 (float[3]) | Event/Select: current / "from" keyframe eye | CONFIRMED |
| **+0x184** | ptr | Event: MotionClip pointer (cutscene) / Select: from-frame quat region | CONFIRMED |
| **+0x188** | 12 (float[3]) | blended look-at / eye target | CONFIRMED |
| **+0x194** | 12 (float[3]) | Select "to" keyframe eye / Event orbit look-at point | CONFIRMED |
| **+0x1A0** | 16 (Quat) | Select "to" keyframe orientation / Event orbit quat | CONFIRMED |
| **+0x1B0** | float | Select yaw goal | CONFIRMED |
| **+0x1B4** | float | Select pitch goal | CONFIRMED |
| **+0x238** | int | Select dolly start timestamp (ms) | CONFIRMED |

> Cross-reference: the local-player object fields the manipulators read are **+0x428**
> (player world position — Static eye base, Gamble look-at) and **+0x45C** (player
> head/body orientation quaternion — First-person orientation multiply). The projection
> parameters (fovy/aspect/near/far/ortho) live on the **separate** projection object,
> not here — see §A.7.1.

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
authoritative for the 2D collision. (CODE-CONFIRMED)

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
  region is (UNVERIFIED)** and needs a click-to-move capture. (CODE-CONFIRMED behaviour; split UNVERIFIED)

### B.1.2 Heartbeat cadence (server reconciliation)

A send-worker thread loops on a short sleep (**~10 ms per iteration**) and conditionally
fires senders, logging when one is "overdue." Each iteration checks the **proxy** channel
**before** the move channel; each sender fires **at most once per distinct millisecond-clock
tick** (it sends only when the current millisecond reading differs from the last one it sent
on), so the cadence is bounded by the millisecond clock, not by an explicit rate literal:

| Channel | Overdue-warning threshold | Notes |
|---|---:|---|
| Proxy channel | **400 ms** | checked first each iteration |
| Move heartbeat | **200 ms** | warning only; a moving client emits a stream of 2/13 frames |

The move heartbeat reads the player's **last-network position** (the X from the live-position
X field and the other axis from the live-position Z field — i.e. it reports where the player
*is*, not the click target), then **alternates the reported X by +20.0 / −20.0** on a parity
counter — a deliberate ±20-unit dither so each heartbeat reports a slightly different point
(keeps the server's position state fresh / defeats static-position dedupe). It calls the 2/13
builder with **mode = 1** and the dithered (X, Z). (CODE-CONFIRMED)

> **(UNVERIFIED)** whether 200 ms is a hard rate cap or only the overdue-warning
> threshold (the worker fires at most once per distinct millisecond; no separate
> rate-limit literal was found). Implement the heartbeat as a tunable period and do
> **not** reproduce the ±20 dither or rely on the 200/400 ms warning thresholds as
> protocol guarantees.

### B.1.3 Client anti-cheat cadence telemetry

The client carries **client-side** speedhack telemetry that compares server-time and
animation-cycle deltas against a tolerance of **1025 ms** before flagging an anomaly
(emits "speedhack" diagnostic logs). This is anti-cheat, but it **bounds the
legitimate move/cycle cadence**: a faithful client must keep its move cadence within
~1 s of the server's clock. (CODE-CONFIRMED as a bound; not required for re-implementation)

> Note: there is an unrelated external DRM/anti-hack command string that *looks*
> movement-related but is **not** a movement-speed setter — ignore it for gameplay.

## B.2 Click-to-move pipeline

1. **Click handler.** Distinguishes a context/attack click from the primary
   walk-to-ground click. Guards: modal-panel-open checks, a held left-mouse-input
   gate (input id **1013**, assumed left-mouse-hold, **UNVERIFIED**), and a **100 ms
   re-issue throttle** so rapid clicks don't spam move-issues. Then calls move-init. (CODE-CONFIRMED)
2. **Screen-pixel → world ground point (unproject).** Cast the active camera to the
   perspective camera, convert the pixel to normalized device coordinates using the
   viewport size, unproject through the camera basis, and intersect the resulting ray
   with the ground plane to get the world (X, Z). A **pick ray length / max pick
   distance of 1000.0** is seeded before the pick. (CODE-CONFIRMED)
3. **Move-init / path setup.** Bail if the player is not ready or is in a lock-state.
   Compute the direction from the player's last-network position to the target; if the
   **squared distance exceeds 144.0** (i.e. **> 12 units**), **clamp the step** by
   scaling the unit direction to length **12.0** (max advance per move-issue;
   144 = 12²). Seed a mover speed of **1000.0**, set the from/to path record, and
   commit. (CODE-CONFIRMED)
   > **Two move-init clamp paths (footnote).** The screen-pick move-init uses the fixed
   > **12.0** clamp at the **>144.0** (12²) threshold described above. A second move-init
   > entry (the throttled move-to-world-XZ path) instead uses a **speed-relative** clamp:
   > when the squared distance exceeds **speed²**, it scales the direction to
   > `(speed · 0.8 − 10.0)` floored to **1.0**. Both reach the same commit step; the
   > fixed-12.0 reading is correct for the screen-pick path, the speed-derived reading for
   > the throttled path. (CODE-CONFIRMED)
4. **Commit.** Enforce a per-mover **cooldown gate** (a millisecond-clock comparison)
   and a busy/lock check. Commit only if the **squared distance from current to
   destination exceeds 4.0** (i.e. **> 2 units**) — a dead-zone that ignores tiny
   moves. On commit, step the mover and drop a **ground click-marker effect**: a
   user-effect spawn (**effect id 380000000**) placed at the click XZ with its Y taken
   from the terrain height sampler (§A.6), after first **clearing the prior marker
   effect**. (This corrects a prior reading that attributed the marker to a "highlight
   texture manager" — it is a user-effect spawn, not a texture-manager call.) (CODE-CONFIRMED)

## B.3 Per-frame movement integration (walk / run)

Invoked from the per-frame terrain update and from the move commit:

1. Face the actor toward its destination and copy the facing orientation.
2. Compute the forward step distance = **`speed · 4.0`**, where the per-frame speed
   scalar is the mover's current walk/run speed (set from config — §B.5) and **4.0**
   is a fixed step multiplier. Rotate the facing orientation to produce the candidate
   next world position (current XZ + rotated step). (CODE-CONFIRMED as the model; the
   exact `· 4.0` integrator site within the mover-advance cluster was **not re-located**
   in the 2026-06-16 pass — **[capture/debugger-pending re-locate]**, flagged for a
   focused re-walk of the mover advance.)
3. If the candidate overshoots the destination **and** the collision subsystem
   reported a block, **snap to the corrected/clamped point** from the collision result
   (the wall-slide / stop response). Otherwise snap to the destination. (CODE-CONFIRMED)
4. Write the resulting position into the actor (and into any coupled/mount-partner
   actors) and re-arm the walk motion; if the motion cycle ended, reset to the default
   motion. Some branches also emit a 2/13 frame as part of the moving-state stream. (CODE-CONFIRMED)

### Walk vs run

Walk/run manifests three ways, kept consistent:

- A **lifecycle/motion-state** value: **2 = walk, 3 = run** (the move-init path gates on
  motion-state == 3 or == 1, the target handler branches on == 2). The **8 = dead/scripted**
  value was *not* re-confirmed in the 2026-06-16 pass — **[static-hypothesis]** from a prior
  cycle; the {1, 2, 3} walk/run distinction is **[confirmed]**. (CODE-CONFIRMED for 1/2/3)
- The **run flag** on the wire (the actor's run-state byte == 1 means running). (CODE-CONFIRMED)
- A **different per-frame speed scalar** feeding the `· 4.0` step. The concrete walk
  vs run *numbers* are **table-driven** (§B.5), not code literals. (CODE-CONFIRMED)

## B.4 Collision against static solids (2D XZ swept-segment query)

Collision is **strictly 2D in the XZ plane (no vertical component).** It is a **swept
test** of the player's movement line segment (from-XZ → to-XZ) against static solid
line segments, organized in a per-cell spatial quadtree. The static solid *format*
itself is owned by the asset specs; this section describes only the **query/movement
side** the mover calls. (CODE-CONFIRMED)

Behavioural pipeline (named by role, addresses stripped):

1. **Resolve collision across cells.** Build the from/to XZ segment; query the current
   cell; if no hit, compute the neighbour cell the move crosses into and recurse across
   the boundary. The design **asserts a single move crosses at most one cell boundary.** (CODE-CONFIRMED)
2. **Cell-walkability / bounds check.** Validate the destination cell is loaded and the
   point is in-bounds; includes a point-in-solid test. Returns walkable yes/no. (CODE-CONFIRMED)
3. **Per-cell nearest-hit query.** If the cell has solids, run the sweep seeded with a
   max-float nearest-distance. (CODE-CONFIRMED)
4. **Grid query.** Map the move-segment's bounding box into a **16 × 16 grid of
   quadtrees** (indices clamped 0..15), run the recursive sweep per overlapped bin, and
   dedup by a per-frame visit tag. (CODE-CONFIRMED)
5. **Quadtree recursive descent.** Test the query box against each of 4 child quadrants
   (split on X then Z), recurse, and at a leaf run the segment sweep. (CODE-CONFIRMED)
6. **Leaf segment sweep.** Iterate the leaf's segments, keep the **nearest** intersection
   (minimum squared distance) — hit point + hit solid. (CODE-CONFIRMED)
7. **Single-segment swept test.** Box-overlap AND line-intersection AND two
   point-in-box tests. The line math uses a `Z = slope·X + intercept` form with a
   vertical-segment special case (type flag). (CODE-CONFIRMED; vertical-segment case UNVERIFIED by sample)

The resolver returns the **nearest** hit point; the mover reads it back and clamps the
player to it (the wall-slide / stop response of §B.3 step 3).

### Cell / grid index math (shared with the camera, §A.6)

- **Cell size = 1024.0** world units (reciprocal **1/1024 = 0.0009765625**).
- **Cell index base = 10000**; a cell (X or Z) index is computed roughly as
  `10000 − (int)(coord · −1/1024) − cellOrigin`. This matches the asset filename
  pattern for cells clustered around index 10000.
- **16 × 16** quadtree bins per cell.

(All **(CODE-CONFIRMED)**; same origin/scale the camera terrain clamp uses.)

### Out-of-bounds snap-back

When a target cell is invalid / out of loaded space, a snap helper rewrites **both**
the player's current position and its destination to a corrected point and re-steps
the mover. This is the local-side correction when a move would leave walkable space. (CODE-CONFIRMED)

### Walkability gate before committing a click

Before committing a click move, the from(current)→to(target) segment is tested: either
snap back (invalid cell) or return "move allowed" = *(not blocked-by-solid) OR
(target cell valid)*. (CODE-CONFIRMED)

## B.5 Speed constants (table-driven via map config)

The **concrete walk/run speeds are NOT code literals.** They come from a per-map
config table keyed **`MAP_SPEED`** (load failure logs "MAP_SPEED data load error"),
parsed by the map/scene config keyword parser, and end up in the actor/mover speed
field. So to faithfully reproduce feel you need the per-map data values, not the code.
**Actual numeric walk vs run speeds are (UNVERIFIED / [capture/sample-pending]) — they
live in map data, not the binary.** Any Korean text in this config is CP949. (CODE-CONFIRMED
that it is data-driven)

#### `MAP_SPEED` per-map record layout (CODE-CONFIRMED, re-verified 2026-06-16)

The parser allocates one **24-byte record per map-local entry** (sized
`24 × MAP_LOCAL_COUNT`) and keys the table off `MAP_START_ID` + `MAP_LOCAL_COUNT`. The
keyword parser fills the following fields within each 24-byte record:

| Field offset (within the 24 B record) | Key | Type | Meaning |
|---:|---|---|---|
| **+0** | `MAP_ID` | u32 | map identifier |
| **+4** | `MAP_DAY` | u32 | day/time-of-day flag |
| **+8** | `MAP_SEC` | u32 | section/zone code |
| **+12** | `MAP_SPEED` | u32 | the per-map movement speed value (feeds the mover speed scalar) |
| **+16** | `LOCATION` | 2× u32 | location pair |

(CODE-CONFIRMED for the record size, field offsets, and keying; the **value at +12** that
governs actual walk/run feel is still **[capture/sample-pending]** — it is map *data*, not a
code literal. This row layout is provided for the data-tables engineer to type the record.)

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

All values **(CODE-CONFIRMED as code immediates)**, governing-at-runtime **(INFERRED)**.

## B.6 Vertical / height (visual only)

World vertical (Y) is **forced to 0 for simulation** and **never sent by the server.**
The terrain heightmap (a 65 × 65 = 4225-vertex f32 grid spanning one 1024-unit cell →
**16-unit vertex spacing**) is used for **rendering / visual vertical placement** (and
for the camera height clamp of §A.6), **not** for the XZ collision, which is the
2D solid quadtree only.

> **CYCLE 12 (IDB SHA 263bd994, 2026-06-22) — terrain-height sampler IS PINNED and uses
> PER-TRIANGLE PLANE interpolation.** The terrain-height sampler is the **same dispatcher
> function** the camera height clamp (§A.6) and the click-to-move ground-marker placement
> (§B.2 step 4) both call. It is **not** a 4-corner bilinear lerp. The sampler:
> 1. Maps world XZ to a cell and finds the quad.
> 2. Determines which of the two triangles in the quad the query point falls in (using the
>    quad's diagonal split from the `.ted` block-4 direction flags — see
>    `formats/terrain.md §5.7`).
> 3. Evaluates the plane equation of that triangle to obtain Y.
>
> This is per-triangle plane interpolation (barycentric over the triangle). The former
> "(UNVERIFIED)" status for the sampler is **CLEARED** — it is confirmed pinned at the
> dispatcher. The "bilinear" description in any prior note is superseded.
> `// spec: Docs/RE/specs/camera_movement.md §B.6` and `Docs/RE/formats/terrain.md §5.4a`.

(CODE-CONFIRMED for the model; sampler now CONFIRMED pinned and per-triangle plane method)

---

# Part C — Reimplementation notes (.NET / Godot)

These are guidance, not faithful-copy mandates; deviate deliberately and document it.

- **Camera lives in Godot (presentation), not in the core.** Model the five view modes
  as a `CameraViewMode` enum + a per-mode rig. Default = Third-person. Bind a "reset
  view" (ESC) → Third. **No combat camera switch.** Gamble = minigame-only; Select =
  char-select scene only; Event = optional cinematic, can be deferred.
  - Use **65° vertical FOV, near 5, far 15000** (the CODE-CONFIRMED in-world values).
  - Fixed orbit radius ≈ 901 units. Default pitch −30°. Elevation clamp [−90°, −12°].
  - Yaw clamp [−π/2, +1.4137] for Third; symmetric ±π/2 for Gamble.
  - Right-mouse drag = orbit look; mouse wheel / other-button = elevation (not radius).
  - Ground collision = vertical lift to terrainHeight + 3.8 (slide, never snap); reuse
    the existing `.ted` bilinear height sampler the Godot terrain already has.
  - Keyboard: gain 0.3, friction 0.6, dead-zone 1e-3, rate clamps ±0.1.
  - Expose all tuning constants as exported/config values.
- **Movement simulation lives in `Client.Domain` / `Client.Application`** (engine-free,
  deterministic, server-reusable). Model: click → unproject-to-XZ (Godot supplies the
  ray) → walkability (2D solid quadtree) → integrate (`speed · 4` per fixed tick,
  clamp 12 u per issue, 2 u dead-zone) → snap on arrival → emit a 2/13 move-request.
  Keep **Y = 0** in the simulation; treat terrain height as a Godot-side visual lift.
- **Heartbeat:** implement as a tunable periodic 2/13 emitter; **do not** reproduce the
  ±20 dither or rely on the 200/400 ms warning thresholds as protocol guarantees.
- **Encoding:** decode any legacy config / option Korean text (`CAMERA_XZ`/`CAMERA_XYZ`
  labels, `MAP_SPEED` table) as **CP949**, then carry as UTF-8 internally.
- **Determinism caution:** the original integrates on a variable per-frame delta. The
  .NET core should integrate movement on the **fixed logic tick** (see
  `specs/game_loop.md` §6) and let Godot interpolate — the `speed · 4` step becomes
  `speed · stepPerTick`, retuned for the fixed rate. This is an intentional divergence.
- **Scene lifecycle:** Login → Char-Select (Select preview cam) → World (Third follow
  cam). Spawn the player at the server-supplied (x, 0, z); the camera just follows.
  Build the in-world camera rig once on entering the world scene; the Select cam
  belongs to the char-select scene. See `specs/client_runtime.md` §7 for the full
  9-state lifecycle and the enter-world spawn handshake.

---

# Part D — UNVERIFIED / open questions (consolidated)

Do **not** hard-code anything in this list without a capture or an analyst cross-check.

**Camera**

1. **Camera action polarity** — which member of the pitch pair (1002/1003) and zoom
   pair (1000/1001) is up/in vs down/out. Implement as configurable ± axes. (INFERRED/configurable)
2. **Authoritative runtime FOV — literal *values* DBG-pending (CYCLE 7).** The in-world
   gameplay FOV is **65°** vertical, near **5**, far **15000** and select is **50°** — these
   are the carried-forward faithful values and the field *offsets* that hold them are now
   pinned (fovy at projection +0xA8, §A.7.1). But CYCLE 7 found the literal *values are NOT
   inline immediates* — they load from read-only data at scene-construction, so the values
   themselves stay **[DBG-pending]** (read +0xA8 at runtime to confirm). Implement at 65°/50°
   but treat as config.
3. **`CAMERA_XZ` / `CAMERA_XYZ` semantics** — the exact 2-axis vs 3-axis follow toggle
   the saved option controls.
4. **Gamble UI driver** — n/a per CYCLE 7: Gamble takes **no user input** (fixed-XZ framing,
   §A.5 "Gamble"). The remaining open question is only *what triggers entering Gamble mode*
   (the mini-game open path), not an orbit driver.
5. ~~**Event-camera scripted-path format**~~ — **RESOLVED, refined (CYCLE 7).** The Event
   camera's non-orbit sub-mode is a **2-track MotionClip cutscene** (track 0 = eye, track 1 =
   look-at, 3× timeline, −90°-about-Z basis quaternion, ends at status 100 → scene mode 2 +
   pop; §A.5 "Event", §A.5.3). This **supersedes** the prior "built-in 17-entry × 3-float
   region curve indexed by map region" reading. Still not a data file, not Lua, not `.scr`.
   (CODE-CONFIRMED)
6. A constant mode-tag (value 2) is set in every camera constructor; its meaning is
   unknown (node/camera-type tag?). Cosmetic; not load-bearing. (Distinct from the Event
   sub-mode at +0x16C, which is a runtime field, not a constructor tag.)
6a. **Projection near/far field offsets** — +0xAC (near) / +0xB4 (far) on the projection
    object are **MEDIUM** (inferred by adjacency to fovy/aspect, not named by the
    field-describe routine, §A.7.1). Confirm by reading them at runtime.
6b. **Char-select keyframe literal coords (KF0, KF1 ≈ (512,87,−9652), path anchor
    (2048,0,−6144))** — **NOT inline immediates** in the Select update (CYCLE 7); they are
    data-driven keyframe slots (manipulator +0x178/+0x194) written at scene setup. The dolly
    *math* is inline-confirmed; the coord *values* are **[scene-construction / DBG-pending]**,
    carried from prior cycles.

**Movement**

7. **2/13 mode/run flag byte split** — mode vs run flag vs the preset `3` vs padding
   inside the trailing flag region. Needs a click-to-move capture. (mirrors the open
   item already on `packets/2-13_move_request.yaml`.)
8. **Heading angular unit** — radians assumed; could be a fixed-scale angle.
9. **Local-player position-correction channel** — 5/13 vs 4/13.
10. **200 ms / 400 ms** are warning thresholds, not proven hard rate caps; whether the
    heartbeat is additionally period-capped is unconfirmed.
11. **Per-map walk/run speed numbers** — live in `MAP_SPEED` map config data, not in
    code; require the data tables or a capture. (The 24-byte record layout is now
    documented in §B.5; only the value at +12 is data-pending.)
12. ~~**Terrain height-sample-at-XZ function**~~ — **RESOLVED (2026-06-16).** The runtime
    bilinear sampler IS pinned: a dispatcher selects between an interpolated (bilinear)
    sampler and a flat/alternate sampler on a per-cell mode field, and the click-to-move
    commit already uses it to place the ground click-marker's Y (§A.6, §B.2 step 4).
13. **`.ted` vs `.ted.post`** runtime selection (which heightmap the loader prefers).
14. **Input id 1013** assumed left-mouse-hold for the click handler; unconfirmed.
15. **Vertical (type-flag) solid segments** — handled in the line-intersection math but
    not corroborated by any sample.
16. **Re-locate the inline mover/camera constants** — the **camera-side** half is now
    **RESOLVED (CYCLE 7)**: the Third-person `+3.8` lift / `+2.0` bias / `+50·dt`
    terrain-clip push-out / pitch kill-clamp are re-located as inline immediates in the
    Third-person update (§A.5.3), and full per-mode constant sets for all six modes were
    extracted. **Still open (movement-side):** the per-frame `speed · 4.0` integrator site
    within the mover-advance cluster was **not** re-located this pass — carry as the faithful
    value; flag for a focused re-walk of the mover advance. **[capture/debugger-pending re-locate]**

---

## Provenance

Rewritten (not copied) from dirty-room recon notes (subsystem keys `camera_views`,
`camera_constants`, `movement_collision`). Updated in Mission E from the dirty-room
camera-scene recon (Mission 1G findings): FOV reconciliation (65°
CODE-CONFIRMED), fixed-radius orbit model explicitly stated, per-mode parameter tables
with confirmed numbers added, Event camera model corrected (built-in binary curve
table, not data-file/Lua/`.scr`), mode-switch trigger call sites enumerated, default
mode and OPTION_VIEW_CHAR persistence documented, Select camera model re-corrected in a CAMPAIGN 9 re-walk to an entry dolly KF0 → KF1 (per frontend_scenes §3.5) - pos-lerp + orient-slerp over ~2.0 s, KF1 (512,87,−9652); both the old multi-waypoint/orbit reading and the follow-on single-static-camera reading are retracted (it is neither a full orbit nor a static camera), with the manual boom/yaw overlay and actor-not-camera slot focus retained,
enter-world camera placement clarified.

Re-verified **2026-06-16** against build anchor **`263bd994`** (CAMPAIGN 10, Block F,
lane F10 static re-walk). Extended **2026-06-16 (CAMPAIGN 14, world-render lane)** with the
camera follow-target binding (§A.4 step 1 + new §A.4.1): the follow target is the actor staged in a
network-fed target/selection slot, resolved by the world handler and published as the camera
selection at scene build; the slot id is staged from the S2C game-state-tick world-snapshot family
and (per static control-flow) re-bound each snapshot (the per-snapshot cadence carried as
[static-hypothesis], the bind itself [confirmed]). The same pass added the one-line note that the
world camera carries no brightness/gamma input (near-black is an environment/ambient matter — see
`environment.md §6.2b`) and re-confirmed the §A.4/§A.5 fixed-radius follow orbit with no numeric
change. World scene-state entry itself is owned by `specs/client_workflow.md §4/§5.4`. Surgical corrections applied this pass: (1) §A.7 FOV
**internal storage is the full vertical FOV ÷ aspect, NOT half-angle** — the `/2` only
appears later in the `tan(fovY/2)` frustum derivation (conflict reconciled); (2) §B.2/§B.4
the click ground-marker is a **user-effect spawn (effect id 380000000)** at the click XZ
with Y from the terrain sampler and a prior-marker clear, **not** a "highlight texture
manager" call (conflict reconciled); (3) manipulator display-label **message-db ids
2006/2004/2005/2148/2148** recorded in §A.2.1; (4) §A.6 the "1024.0/1.1" was sharpened to a
**`−1.1`-unit cell-edge nudge** when a coordinate lands on a cell boundary, and the
**bilinear terrain-height sampler is now PINNED** (dispatcher + interpolated/flat samplers),
closing former open item D.12; (5) §B.5 the **`MAP_SPEED` record = 24 bytes** with field
offsets MAP_ID+0 / MAP_DAY+4 / MAP_SEC+8 / MAP_SPEED+12 / LOCATION+16; (6) §B.2 the
**second (throttled) move-init clamp path is speed-relative** (`speed·0.8 − 10.0`, floored
1.0) vs the screen-pick path's fixed 12.0; (7) §B.1.2 the send worker checks **proxy before
move** and the heartbeat reads the **live-position** fields; (8) §B.3 the **8 = dead**
motion-state value demoted to static-hypothesis (only 1/2/3 re-confirmed). Honestly flagged
**[capture/debugger-pending re-locate]**: the per-frame `speed·4.0` integrator site and the
Third-person `+3.8` lift / `+2.0` bias / `−0.01` yaw-kill / `50·dt` occlusion-nudge inline
constants were not re-located this pass. Server-authored magnitudes (per-map speed numbers)
and on-wire VALUE meanings (2/13 flag-region split, local-position-correction channel) remain
capture/debugger-pending.

Extended **CYCLE 7 (2026-06-20)**, anchor **`263bd994`**, static-ida (P-Lane 5 RENDER/UI/AUDIO),
rewritten (not copied) from the dirty-room lane note (key `camera_views`). This pass landed:
(1) the **dispatch mechanism** — view modes are **C++ polymorphism**, one `CameraManipulator`
subclass per mode dispatched via the per-frame virtual at **vtable slot +0x40**, gated by the enable
flag at **+0xE5**, with **no integer mode enum / switch** (the only integer discriminator is the
Event sub-mode at **+0x16C**) — §A.1, refined §A.2; (2) the **six neutral RTTI class names**
(Third/First/Static/Gamble/Event/Select), with Gamble re-read as a **fixed-XZ, no-input** framing of
the cube-gamble mini-game (eye X 24097.46484375 / Z 55694.4296875), First as **orientation ×
player-head-quat (+0x45C)**, Static as **yaw-from-player-facing** with player pos at **+0x428**, and
the Event non-orbit path re-read as a **2-track MotionClip cutscene** (superseding the prior "17-entry
region curve"); (3) the **per-mode inline re-locate constant table** §A.5.3 (clearing the former
camera-side `[re-locate]` flag on the Third `+3.8`/`+2.0`/push-out/clamp items); (4) the
**CameraManipulator object-offset table** §A.9 and the **separate GPerspectiveCamera projection
offset table** §A.7.1 (fovy +0xA8 / aspect +0xB0 / ortho +0x94.. CODE-CONFIRMED; near/far +0xAC/+0xB4
MEDIUM); (5) the **Select entry-dolly math** (blend `elapsed_ms·0.0005`, ease `(1−t)·(2t)`,
eye-Lerp + orientation-Slerp, goal/clamp constants). **Residue carried as DBG-pending:** the literal
FOV (~65 world / ~50 select), near (~5), far (~15000), and the char-select keyframe world coords
(KF1 ≈ (512,87,−9652), path anchor (2048,0,−6144)) are **not inline immediates** — they load from
read-only data at scene-construction; and the projection near/far offsets are MEDIUM. Proposed
canonical update-method names for the IDB are staged for the names.yaml sync (orchestrator-owned).

Per the clean-room firewall, this spec carries **neutral RTTI class names** (e.g.
`ThirdCameraManipulator`) and **struct field offsets** (layout facts) — both explicitly permitted —
but **no** code addresses, no Hex-Rays / decompiler identifiers, no mangled RTTI strings, and no
vtable data addresses. Only neutral behaviour, formulas, role-keyed constants, struct/projection
field offsets (interoperability facts), and already-cataloged opcode tuples (2/13, 5/13, 4/13 — see
`opcodes.md`) are promoted. Camera is non-networked. No new opcode is introduced by this spec.
