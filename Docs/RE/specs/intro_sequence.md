---
status: confirmed
sample_verified: partial   # texture sizes/paths SAMPLE-VERIFIED; animation math CODE-CONFIRMED (static); on-screen scaled rate UNVERIFIED
subsystems: [opening_window, intro_crawl, intro_slideshow]
networked: false           # the intro plays before any network/login; purely client-side
encoding_note: texture/sound paths are ASCII; no CP949 concerns in this scene
---

# Intro sequence (the pre-login `OpeningWindow` scene)

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
| Two distinct animated layers (positional crawl + frame-paged slideshow) | CODE-CONFIRMED (static) |
| Scenario crawl is a positional (destination-Y) translate, not UV-scroll, not a shader | CODE-CONFIRMED (static) |
| Crawl speed 30 design-px/s; start gate ~1000 ms; stop clamp 1843; manual nudge | CODE-CONFIRMED (static) |
| Slideshow state machine 1→2→3→4; 17500 ms dwell; vertex-alpha crossfade 0→250 | CODE-CONFIRMED (static) |
| Intro sound id 910061000 fired at scene start | CODE-CONFIRMED (static) |
| Texture paths and pixel dimensions | SAMPLE-VERIFIED (VFS) |
| On-screen realized scroll rate after the view-context design→screen scale | UNVERIFIED (debugger) |
| Visible fade duration (alpha ramp is frame-gated, not ms-gated) | UNVERIFIED (frame-rate dependent) |

---

## 0. What this scene is — and the "red ribbon" clarification

The opening scene (`OpeningWindow`) is the **pre-login intro** that runs before the login form
appears. It contains **two independent animated layers** driven by two different mechanisms — and
the long crawling calligraphy/scenario band is the element commonly referred to as the
**"red ribbon."**

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

Both re-push the position to the sprite. The two inputs are distinct UI input ids (an up id and a
down id); the engine treats them as a "review" mode for the scenario art.

### 2.3 Constants

| Quantity | Value | Notes |
|---|---:|---|
| Scroll speed | 30.0 design-px / second | delta-time scaled; immediate constant |
| Start delay | ~1000 ms | one-shot startup gate before scroll begins |
| Upper clamp / stop | 1843.0 | auto-scroll latches "done" here; no wrap |
| Lower clamp (manual) | 0.0 | floor on the manual up-nudge |
| Sprite size | 1024 × 2048 | scenario quad rect |
| Destination-Y base | `(scene field) − 200`, plus the scroll position | set at build, advanced per frame |

> **UNVERIFIED (debugger):** 30.0/sec is in **unscaled design-pixel space**. The realized on-screen
> rate depends on the design→screen scale the view context applies. A live read of the sprite's Y
> over a few seconds would pin the actual pixels-per-second on screen.

---

## 3. Splash slideshow — frame-paged sequence with alpha crossfade

A separate state machine pages the four full-screen splash panels (`openning_001` … `_004`). This
is a **paged image sequence** — each "frame" is a whole separate DDS — blended by a vertex-alpha
ramp. It is **not** a sprite-strip inside one sheet and **not** a UV-scroll.

### 3.1 State machine (CODE-CONFIRMED, static)

- A **state index** holds values 1, 2, 3, 4 and selects which panel is drawn (handle array indexed
  by state).
- Each state holds for a fixed **dwell of 17500 ms** (plus a small fixed latch offset on the
  timestamp). When the dwell expires **and** the panel has fully faded in (alpha reached its
  maximum), the state increments to the next (1→2→3→4).
- After state 4 completes, a finish flag ramps a final counter to its maximum, then the controller
  transitions out of the intro scene into the login window.

### 3.2 Alpha crossfade (CODE-CONFIRMED, static)

- A single **alpha byte** ramps between **0 and 250** with a per-frame **±1** step (an internal
  fade-in-then-fade-out direction toggle). The step is **frame-gated** (one unit per rendered
  frame), not millisecond-gated.
- The draw reads this alpha byte and **broadcasts it to all four vertex-color alpha bytes**, so the
  whole panel quad is uniformly alpha-modulated.
- The panel quad is drawn as 4 vertices / 2 triangles under an orthographic projection sized to the
  client rect, with **SRCALPHA / INVSRCALPHA** blending (standard straight-alpha blend).

### 3.3 Constants

| Quantity | Value | Notes |
|---|---:|---|
| Frame count | 4 | `openning_001` … `openning_004`; state range 1..4 |
| Per-frame source rect | full texture | whole DDS per state; no sub-rect |
| Dwell per frame | 17500 ms | state-machine compare |
| Alpha range | 0 … 250 | ramp bounds (250, not 255) |
| Alpha step | ±1 per rendered frame | frame-gated, not ms-gated |
| Blend mode | SRCALPHA / INVSRCALPHA | straight-alpha |

> **UNVERIFIED:** because the alpha ramp is **frame-gated**, the visible fade duration is
> frame-rate dependent. A live capture would pin the on-screen fade time.

---

## 4. Intro sound

At scene start the opening scene fires **one** sound: numeric sound id **910061000**. By the
project's sound-loader rule (category < 5 → 2D) this resolves to `data/sound/2d/910061000.ogg`
(the `.ogg` basename form is INFERRED from the loader prefix rule; debugger-confirmable). This is
the intro stinger / BGM for the opening scene.

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

- **Scenario crawl:** model one full-screen sprite (1024×2048) whose Y position is the animated
  state. Advance `pos += dt × 30` after a 1000 ms gate, clamp at 1843, no wrap, then allow manual
  up/down scrub at the same rate. Drive the sprite's screen Y; do not animate UVs or a shader.
- **Slideshow:** a 4-state machine with a 17500 ms dwell per state, paging four whole textures, with
  a 0→250 alpha crossfade (broadcast to the quad's vertex alpha). Advance state when dwell elapses
  and the fade completes; after state 4, transition to the login scene.
- **Sound:** fire id 910061000 (2D) once at scene start; the login-window stinger 861010105 is a
  separate later cue.
- **Scale caveat:** the 30 px/s and the 1843 clamp are in design-pixel space — apply the same
  design→screen scaling the rest of the UI uses so the crawl reaches the same visual endpoint.

---

## Known unknowns

1. **On-screen realized scroll rate** — 30.0/s is design-space; the view-context design→screen
   scale that converts it to actual pixels was not isolated. UNVERIFIED (debugger).
2. **Visible fade duration** — the alpha ramp is frame-gated (±1 per frame), so the perceived fade
   time depends on frame rate. UNVERIFIED.
3. **Which DDS carries the literal "red ribbon" art** vs. plain scenario calligraphy is a visual
   question for an asset peek; structurally the 1024×2048 scenario crawl is the long crawling band
   and `openning_001..004` are full-screen splash frames.

---

## Cross-references

- **Sound runtime** (plays id 910061000 here; id 861010105 in the login window): `specs/sound.md`
  (front-end section).
- **Front-end scene flow** (the login scene this intro transitions into): `specs/frontend_scenes.md`.
- **Front-end VFX** (confirms login/PIN have no `.xeff`, so the ribbon is NOT a particle effect):
  `formats/effects.md` (front-end VFX section).
- **DDS texture format:** the texture format spec for `data/ui/openning_*.dds`.
- **Glossary:** see `Docs/RE/names.yaml` (`OpeningWindow` and related canonical names).
- **Provenance:** see `Docs/RE/journal.md`.
