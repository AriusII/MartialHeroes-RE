---
verification: confirmed                # animation/scene-state facts CODE-CONFIRMED (static); residual on-screen-rate/fade-duration/sound-extension are capture/debugger-pending
ida_reverified: 2026-06-16
ida_anchor: 263bd994
evidence: [static-ida, vfs-sample]     # texture sizes/paths SAMPLE-VERIFIED (VFS); behaviour STATIC-IDA
conflicts: on-screen realized scroll rate (design→screen scale); visible fade duration (frame-gated, frame-rate dependent); sound 910061000 .ogg basename/extension; display FRAMERATE config inertness
status: confirmed
sample_verified: partial   # texture sizes/paths SAMPLE-VERIFIED; animation math CODE-CONFIRMED (static); on-screen scaled rate UNVERIFIED
subsystems: [opening_window, intro_crawl, intro_slideshow]
networked: false           # the Opening renders client-side; it plays AFTER login (GameState 3), before char-select
encoding_note: texture/sound paths are ASCII; no CP949 concerns in this scene
---

# Intro sequence (the post-login `OpeningWindow` scene)

> **Clean-room specification.** Neutral description only — no decompiler identifiers, no binary
> virtual addresses, no pseudo-code. Promoted from dirty-room static analysis notes under EU
> Software Directive 2009/24/EC Art. 6 (decompilation solely to achieve interoperability). No
> decompiler output appears in this file. Re-expressed entirely in the spec-author's own words.
>
> **Ownership:** this spec owns the *behaviour* of the pre-login opening/intro scene — the
> scenario crawl and the four-panel slideshow. The *on-disk format* of the DDS textures it loads
> is owned by the DDS/texture format spec; the *sound runtime* that plays its stinger is owned by
> `specs/sound.md`. This file describes the animation mechanism only.

---

## Status banner

| Area | Confidence |
|---|---|
| Scene is the boot scene-machine's GameState 3 (branched-to from the SKIP read in GameState 2; character-select is GameState 4, login is the earlier GameState 1) | CODE-CONFIRMED (static) |
| Two distinct animated layers (positional crawl + frame-paged slideshow) | CODE-CONFIRMED (static) |
| Scenario crawl is a positional (destination-Y) translate, not UV-scroll, not a shader | CODE-CONFIRMED (static) |
| Crawl speed 30 design-px/s; start gate ~1000 ms; stop clamp 1843; manual keyboard nudge (1004/1005) | CODE-CONFIRMED (static) |
| Second manual scrub path (mouse-wheel/drag, UI event type 8, ±30, clamp 30..1833) | CODE-CONFIRMED (static) |
| Slideshow state machine 1→2→3→4; 17500 ms dwell (+92 latch); alpha crossfade bounds 0..250, field INITIALISED to 250 | CODE-CONFIRMED (static) |
| Alpha applied via the D3D TEXTUREFACTOR render-state (single device factor), not per-vertex color | CODE-CONFIRMED (static) |
| Skip button anchored TOP-right (y=10, x=clientWidth−120) | CODE-CONFIRMED (static) |
| Intro sound id 910061000 fired at scene start | CODE-CONFIRMED (static) |
| Texture paths and pixel dimensions | SAMPLE-VERIFIED (VFS) |
| On-screen realized scroll rate after the view-context design→screen scale | capture/debugger-pending |
| Visible fade duration (alpha ramp is frame-gated, not ms-gated) | capture/debugger-pending (frame-rate dependent) |
| Sound 910061000 `.ogg` basename/extension (loader prefix-rule inference) | capture/debugger-pending |

---

## 0. What this scene is — and the "red ribbon" clarification

The opening scene (`OpeningWindow`) is the **post-login intro** that runs *after* the player logs in
(GameState 1) and *before* the character-select scene (GameState 4). It contains **two independent
animated layers** driven by two different mechanisms — and
the long crawling calligraphy/scenario band is the element commonly referred to as the
**"red ribbon."**

### 0.1 Scene placement in the boot scene state machine (A0 reconciliation)

The boot driver is a single scene state machine (`WinMain`) that runs `while(1) switch(GameState)`
with **8 cases (GameState 0..7)** — the value 8 is a *sub-state* value, not a 9th top-level case.
The Opening/intro scene is **GameState 3**, and it is reached as follows:

- **GameState 0 — window/display init.** Create the window and set display metrics.
- **GameState 1 — the LOGIN scene.** Load `data/script/msg.xdb`, construct the `LoginWindow`, and run
  it interactively (the login sub-state machine: credential validation, the PIN/second-password
  modal, the server-list, the join). The login **completes here**, before the Opening — which is why
  the Opening is a *post-login* intro, not a pre-login splash.
- **GameState 2 — the load / intro-SKIP gate.** Construct the loading window; read the
  `[OPENNING] SKIP` key from the client config INI. If `SKIP != 0` the machine jumps **straight to
  GameState 4** (skip the Opening, go to the character-select scene); if `SKIP == 0` it advances to
  GameState 3 (play the Opening). *(IDB-confirmed: case 2 writes GameState=4 on SKIP≠0, else GameState=3.)*
- **GameState 3 — the Opening intro (THIS scene).** Allocate and construct the Opening scene
  (`COpeningWindow`, a 0x2D0-byte object), show it, then set the next state to 4. This is where the
  scenario build (§1), the crawl (§2) and the slideshow (§3) run.
- **GameState 4 — the CHARACTER-SELECT scene** (`SelectWindow`, a distinct, larger 0x1888-byte
  object). The intro therefore transitions *into* GameState 4 once its slideshow finishes (§3.1).

So the strict ordering is: **window init (0) → LOGIN (1) → load + SKIP decision (2) → Opening intro
(3) → character-select (4)** → in-game (5). The Opening plays **after** login, not before. The
persisted `[OPENNING] SKIP=1` flag (set on skip, §2.2/§6) is what GameState 2 reads to bypass this
scene on subsequent runs. See `specs/client_workflow.md` / `specs/frontend_scenes.md` for the full
flow and `specs/game_loop.md` / `specs/client_runtime.md` for the GameState enumeration.

> **"Red ribbon" verdict (CONFIRMED reframe):** the red ribbon is **this intro scene's scrolling
> scenario art** (`openning_scenario.dds`, a single 1024×2048 sprite translated vertically). It is
> **NOT** a login-window effect, **NOT** a UV-scrolled shader, and **NOT** an `.xeff` particle
> effect. The login/PIN/server-list scenes have no dedicated particle VFX (see `formats/effects.md`
> front-end section). The "ribbon" is a positional crawl of one whole DDS quad.

The two layers are:

1. **Scenario crawl** — one full-size 1024×2048 sprite whose on-screen destination Y is translated
   downward over time (the "ribbon" band; §2).
2. **Splash slideshow** — four separate full-screen DDS panels (`openning_001.dds` …
   `openning_004.dds`) shown in sequence with an alpha crossfade (§3).

> Note on filename spelling: the original client spells the stem **`openning`** (double-n) — this
> is the legacy typo and is the literal VFS name; preserve it.

---

## 1. Scene assets (textures loaded once at scene build)

All textures are loaded once when the opening scene is built, via the simple DDS texture loader.
Paths are concrete VFS paths.

| Texture (VFS path) | Pixel size | Role |
|---|---|---|
| `data/ui/openning_scenario.dds` | 1024 × 2048 | The scrolling scenario backdrop ("red ribbon" band); one whole-texture quad (no frame slicing) |
| `data/ui/openning_001.dds` | 1024 × 768 | Splash panel, slideshow state 1 |
| `data/ui/openning_002.dds` | 1024 × 768 | Splash panel, slideshow state 2 |
| `data/ui/openning_003.dds` | 1024 × 768 | Splash panel, slideshow state 3 |
| `data/ui/openning_004.dds` | 1024 × 768 | Splash panel, slideshow state 4 |
| `data/ui/mainwindow.dds` | (chrome) | Chrome layer (skip-button sprite + frame) |

- The four `openning_00N` handles are stored as a **contiguous 4-entry array**; the slideshow draw
  indexes that array by the current state value (state 1 → panel 001 … state 4 → panel 004).
- `openning_scenario.dds` is built as a single static-image sprite of rect **width 1024, height
  2048**, centered horizontally; its destination Y is derived from a class field minus 200 and then
  has the animated scroll position added each frame. There is **no sub-rect / frame slicing** of
  the 2048-tall sheet — the whole texture is one quad.

---

## 2. Scenario crawl (the "ribbon") — positional vertical scroll

The crawl is a **destination-Y translate** of the single scenario quad. The animated quantity is
the sprite's on-screen Y position, pushed each frame through the sprite's "set destination
position" call. It is **not** a source-rect change, **not** a UV offset, and **not** a shader.

### 2.1 Per-frame math (CODE-CONFIRMED, static)

1. **Startup gate:** for the first ~**1000 ms** after the scenario panel becomes active, the crawl
   does nothing (a one-shot wait flag consumes the first second, then resets the time baseline).
2. **Delta time:** each subsequent frame computes `dt_s = (now_ms − last_ms) × 0.001`.
3. **Advance:** the scroll position advances `pos += dt_s × 30.0` — i.e. **30 design-pixels per
   second**, frame-rate independent.
4. **Push to sprite:** each frame the new `pos` is written as the sprite's destination Y via the
   "set destination position" call (the X argument stays 0; only Y is animated).
5. **Stop clamp:** when `pos ≥ 1843.0`, a "done" latch is set and the auto-scroll stops. There is
   **no wrap-around** — it is a single one-way crawl from 0 to ~1843 down the 2048-tall sheet
   (1843 ≈ 2048 − 205, so it reveals nearly the full sheet top-to-bottom and halts).

### 2.2 Manual review nudge (after the crawl latches done)

Once the auto-crawl has stopped, the player may scrub the backdrop at the same rate:

- **Nudge up** input held → `pos −= dt_s × 30.0`, floored at **0.0**.
- **Nudge down** input held → `pos += dt_s × 30.0`, capped at **1843.0**.

Both re-push the position to the sprite. The two inputs are distinct UI input ids — **up = action
1004, down = action 1005** — the engine treats them as a "review" mode for the scenario art.

The skip button (§1 / §6) is sliced from `data/ui/mainwindow.dds` at src **(761,165)** normal /
**(634,165)** pressed, **110×32**, and is anchored to the **TOP-right** corner — built at
**x = clientWidth − 120, y = 10** (NOT lower-right). Keyboard skip is Enter / ESC / Space, and on
skip the client persists `[OPENNING] SKIP=1` (the GameState-2 gate reads it to bypass the intro).

### 2.3 Second manual scrub path — mouse-wheel / drag (UI event type 8)

Independent of the action-1004/1005 keyboard nudge above, the scenario child also responds to a
**UI event of type 8** (mouse wheel / drag) as a manual review scrub. Each such event moves the
scenario position by **±30.0 per event** and re-pushes it to the sprite. This path uses a **different
position field** from the crawl-update path and has **different clamps**: a floor of **30.0** and a
ceiling of **1833.0** (note: **1833**, not the 1843 used by the keyboard path). The two review
mechanisms (event-type-8 mouse scrub vs. action-1004/1005 keyboard nudge) are distinct and clamp to
slightly different ranges.

### 2.4 Constants

| Quantity | Value | Notes |
|---|---:|---|
| Scroll speed | 30.0 design-px / second | delta-time scaled; immediate constant (keyboard auto-crawl) |
| Start delay | ~1000 ms | one-shot startup gate before scroll begins |
| Upper clamp / stop (auto + keyboard nudge) | 1843.0 | auto-scroll latches "done" here; no wrap |
| Lower clamp (keyboard nudge) | 0.0 | floor on the manual up-nudge (action 1004) |
| Scrub step (mouse-wheel/drag, event type 8) | ±30.0 per event | second review path (§2.3) |
| Scrub clamp (mouse-wheel/drag) | 30.0 … 1833.0 | distinct from the keyboard path's 0.0 … 1843.0 |
| Sprite size | 1024 × 2048 | scenario quad rect |
| Destination-Y base | `(scene field) − 200`, plus the scroll position | set at build, advanced per frame |

> **capture/debugger-pending:** 30.0/sec is in **unscaled design-pixel space**. The realized on-screen
> rate depends on the design→screen scale the view context applies. A live read of the sprite's Y
> over a few seconds would pin the actual pixels-per-second on screen.

### 2.5 Skip dispatch — two event types into one teardown

The skip request reaches the scenario child's UI-event handler as **two distinct event types**, both
of which run the same teardown:

- **Keyboard event** — key codes **10 / 27 / 32** (Enter / ESC / Space).
- **Button-click event** — a click carrying the skip button's **action-tag 100** (the tag assigned
  to the top-right skip button, §2.2).

Either path: persists `[OPENNING] SKIP=1` to the client config INI (so the GameState-2 gate skips
the intro next run, §0.1), sets the scene's "closing" flag, and tears down the scene's child
objects. The same handler also carries the mouse-wheel/drag scrub (UI event type 8, §2.3) as a
separate, non-closing branch.

---

## 3. Splash slideshow — frame-paged sequence with alpha crossfade

A separate state machine pages the four full-screen splash panels (`openning_001` … `_004`). This
is a **paged image sequence** — each "frame" is a whole separate DDS — blended by a vertex-alpha
ramp. It is **not** a sprite-strip inside one sheet and **not** a UV-scroll.

### 3.1 State machine (CODE-CONFIRMED, static)

- A **state index** holds values 1, 2, 3, 4 and selects which panel is drawn (handle array indexed
  by state).
- Each state holds for a fixed **dwell of 17500 ms**. The dwell compare adds a small fixed latch
  offset of **+92** to the stored start timestamp (i.e. the compare is against `start + 17500`, and
  the start latch carries a constant **+92** offset). When the dwell expires **and** the panel has
  reached its alpha extreme (alpha at its maximum, 250), the state increments to the next (1→2→3→4).
- The state index advances **1→2→3→4** by the dwell gate, but **there is NO auto-finish after state
  4** — panel 4 holds and loops its alpha fade **indefinitely**. The slideshow FSM never advances past
  4 and never sets the finish flag on its own. The **only** exit from the Opening scene is an
  **explicit skip** (§2.5): the skip handler is the *sole* writer of the finish flag (`=1`), which
  ramps the final alpha and clears the run-flag so WinMain re-enters the pre-set state 4
  (character-select, §0.1). *(CAMPAIGN 16 correction — the earlier "after state 4 completes the
  controller auto-transitions to character-select" is **REFUTED**: an exhaustive writer scan shows the
  finish flag is set in exactly two places, both inside the skip handler. Without a skip the intro
  never ends. This also settles the old "movie-complete vs loading-done (type 13/id 10001) vs timer"
  exit question — it is **none of those**, only an explicit skip.)*

### 3.2 Alpha crossfade (CODE-CONFIRMED, static)

- A single **alpha byte** moves between the bounds **0 and 250** with a per-frame **±1** step. The
  direction is chosen by an internal **direction-toggle** (a process-global byte), so the field
  fades down then back up between the two bounds. The step is **frame-gated** (one unit per rendered
  frame), not millisecond-gated.
- **The alpha field is INITIALISED to its maximum, 250** (by the Opening-scene constructor — see
  §3.4), not to 0. The bounds 0..250 are correct, but the *first* visible phase is a **fade-OUT**
  driven by the direction toggle, **not** a fade-in ramping up from 0. (The earlier "0→250 fade-in"
  reading was wrong on the starting value and the initial direction.)
- The draw reads this alpha byte and applies it via the **Direct3D TEXTUREFACTOR render-state** — a
  **single device-wide blend factor**. The byte is **broadcast into all four colour bytes of one
  TEXTUREFACTOR colour value** (the same level in each channel) and set as that one render-state.
  This is **NOT** written into per-vertex colour alpha; the whole panel quad is uniformly modulated
  by that one device factor.
- The panel quad is drawn as 4 vertices / 2 triangles under an orthographic projection sized to the
  client rect. The blend is a standard straight-alpha blend; the exact source/destination blend
  factors read as **SRCALPHA / INVSRCALPHA** (this enum reading is plausible from the raw
  render-state arguments but unproven — *capture/debugger-pending*).

### 3.3 Constants

| Quantity | Value | Notes |
|---|---:|---|
| Frame count | 4 | `openning_001` … `openning_004`; state range 1..4 |
| Per-frame source rect | full texture | whole DDS per state; no sub-rect |
| Dwell per frame | 17500 ms | state-machine compare (against `start + 17500`) |
| Dwell start-latch offset | +92 | constant offset carried on the stored dwell-start timestamp |
| Alpha bounds | 0 … 250 | ramp bounds (250, not 255) |
| Alpha initial value | 250 (max) | seeded by the constructor (§3.4); first phase is a fade-OUT |
| Alpha step | ±1 per rendered frame | frame-gated, not ms-gated; direction from a global toggle |
| Alpha application | TEXTUREFACTOR render-state | single device factor; broadcast into all 4 colour bytes (not per-vertex) |
| Blend mode | SRCALPHA / INVSRCALPHA | straight-alpha (enum reading *capture/debugger-pending*) |

> **capture/debugger-pending:** because the alpha ramp is **frame-gated**, the visible fade duration
> is frame-rate dependent. A live capture would pin the on-screen fade time.

### 3.4 State seeded at scene construction (CODE-CONFIRMED, static)

The Opening-scene constructor (the real one used by GameState 3, distinct from the trivial base
constructor) sets the window's display name to `"Opening"`, writes the scene's vtable, and **seeds
the animation state**:

- done-latch (auto-crawl finished) = **0**;
- finish-flag (slideshow complete) = **0**;
- scroll position cleared to **0**;
- startup wait-flag = **1** (arms the ~1000 ms crawl start gate, §2.1);
- **alpha byte = 250 (maximum)** — so the slideshow opens at full alpha and the direction-toggle
  drives the first fade-out (§3.2), not a fade-in from 0.

> Two integer parameters (1000, 28158) are passed to the base window initializer — likely a z-order
> / window-id pair; their exact roles are *capture/debugger-pending* and not load-bearing here.

---

## 4. Intro sound

At scene start the opening scene fires **one** sound: numeric sound id **910061000** — created and
played **once during scene build** (it is the last meaningful call in the scenario-build routine),
not from a per-frame tick. By the project's sound-loader rule (category < 5 → 2D) this resolves to
`data/sound/2d/910061000.ogg` (the `.ogg` basename/extension form is INFERRED from the loader prefix
rule — *capture/debugger-pending*). This is the intro stinger / BGM for the opening scene.

> This is **distinct** from the login-window intro stinger id **861010105**, which fires later from
> the login path at the login state machine's own state 1 (see `specs/sound.md` front-end section).
> The opening scene (910061000) and the login window (861010105) are two different scenes with two
> different cues.

---

## 5. The shared timed-event scheduler is NOT the animator (clarification)

Both the intro render callback and the login per-frame tick end by calling a **generic timed-event
queue** (a shared "fire effect X when its scheduled deadline elapses" dispatcher). That scheduler
walks a list of pending entries and fires each whose stored deadline has passed; it does **not**
compute source rects, UV offsets, or panel positions. All of the ribbon/scenario crawl math (§2)
and the slideshow math (§3) live entirely in the opening scene's own update routines — not in the
shared scheduler. Do not attribute the crawl to that queue.

---

## 6. Implementation guidance (clean-room reimplementation)

- **Scene placement:** wire this scene as the boot machine's GameState 3, reached only when the
  GameState-2 SKIP read is 0; on skip it persists `[OPENNING] SKIP=1` and the gate jumps to the
  character-select scene (GameState 4). See §0.1.
- **Scenario crawl:** model one full-screen sprite (1024×2048) whose Y position is the animated
  state. Advance `pos += dt × 30` after a 1000 ms gate, clamp at 1843, no wrap. Then offer **two**
  review inputs: the keyboard nudge (actions 1004/1005, ±dt·30, clamp 0..1843) and the mouse-wheel/
  drag scrub (UI event type 8, ±30 per event, clamp 30..1833). Drive the sprite's screen Y; do not
  animate UVs or a shader.
- **Skip button:** sliced from `mainwindow.dds` (src normal (761,165) / pressed (634,165), 110×32),
  anchored **top-right** at `x = clientWidth − 120, y = 10`, carrying action-tag 100. Honour both
  the click (tag 100) and the keyboard keys (Enter/ESC/Space) as skip.
- **Slideshow:** a 4-state machine with a 17500 ms dwell per state (start latch +92), paging four
  whole textures. Seed the alpha field at **250 (max)** so the first phase is a fade-OUT, step ±1
  per frame between bounds 0..250 with a direction toggle, and apply the alpha via a **single
  device-wide TEXTUREFACTOR colour** (the byte broadcast into all four channels) — **not** per-vertex
  colour. Advance state when dwell elapses and the alpha reaches its extreme; after state 4,
  transition to the character-select scene.
- **Sound:** create+play id 910061000 (2D) once at scene **build**; the login-window stinger
  861010105 is a separate later cue.
- **Scale caveat:** the 30 px/s and the 1843/1833 clamps are in design-pixel space — apply the same
  design→screen scaling the rest of the UI uses so the crawl reaches the same visual endpoint.

---

## Known unknowns

1. **On-screen realized scroll rate** — 30.0/s is design-space; the view-context design→screen
   scale that converts it to actual pixels was not isolated. (capture/debugger-pending)
2. **Visible fade duration** — the alpha ramp is frame-gated (±1 per frame), so the perceived fade
   time depends on frame rate. (capture/debugger-pending)
3. **Sound 910061000 basename/extension** — `data/sound/2d/910061000.ogg` is inferred from the
   loader prefix rule, not re-derived here. (capture/debugger-pending)
4. **Slideshow blend-factor enum** — SRCALPHA / INVSRCALPHA is a plausible reading of the raw
   render-state arguments, unproven. (capture/debugger-pending)
5. **Which DDS carries the literal "red ribbon" art** vs. plain scenario calligraphy is a visual
   question for an asset peek; structurally the 1024×2048 scenario crawl is the long crawling band
   and `openning_001..004` are full-screen splash frames.

---

## Cross-references

- **Sound runtime** (plays id 910061000 here; id 861010105 in the login window): `specs/sound.md`
  (front-end section).
- **Boot scene state machine** (the GameState 0..7 driver; login = GameState 1, SKIP gate = GameState
  2, intro = GameState 3, character-select = GameState 4 — §0.1): `specs/game_loop.md` / `specs/client_runtime.md`.
- **Front-end scene flow** (the character-select scene this intro transitions into): `specs/frontend_scenes.md`.
- **Front-end VFX** (confirms login/PIN have no `.xeff`, so the ribbon is NOT a particle effect):
  `formats/effects.md` (front-end VFX section).
- **DDS texture format:** the texture format spec for `data/ui/openning_*.dds`.
- **Glossary:** see `Docs/RE/names.yaml` (`OpeningWindow` and related canonical names).
- **Provenance:** see `Docs/RE/journal.md`.
