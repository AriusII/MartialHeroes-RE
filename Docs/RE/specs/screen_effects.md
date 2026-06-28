# Spec: screen_effects — full-screen post/overlay effects — RESOLVED-NEGATIVE

> Clean-room spec. Neutral description only — NO sample bytes, NO decompiler pseudo-code,
> NO binary addresses. This spec is the authoritative record that **no general full-screen
> post/overlay screen-effect subsystem exists** in this client, and documents where each
> effect family actually lives. Cross-references: `Docs/RE/specs/character_rendering.md §4.3`
> (per-character status tints), `Docs/RE/specs/render_pipeline.md §5` (composite/present
> quads), `Docs/RE/formats/effects.md` (world-space particle FX for skill cues).

<!--
verification:
  confirmed:
    - no full-screen tint/fade/flash/vignette/desaturate pass exists anywhere in the binary
    - string sweep for fade|flash|blink|tint|vignette|desatur|fadein|fadeout|transition|
      warp|whiteout|blackout|levelup|level_up returns zero relevant hits; the single
      "Flash" result is the Win32 import FlashWindowEx (taskbar attention, not a render
      effect); no SetGammaRamp import or wiring
    - the complete shader inventory is exactly the documented cel+glow set: power1dx8.psh
      (glow blur), finaldx8.psh (composite), dotoonshading.vsh, dotoonshading.psh,
      dotoonshading2.psh (cel normal/stealth), toonramp.bmp — no tint, fade, vignette,
      or desaturate pixel shader exists; cross-confirms post_processing.md §3 and
      render_pipeline.md §8
    - DISPLAY_CHAR_BRIGHT_* matrix keys (states DEFAULT/CHOICE/HIT/ALPHA/HIDDEN/POISON/
      TYPE/ANGER/AUTO, channels MULTI_R/G/B and ADD_R/G/B and ALPHA) plus
      DISPLAY_BASE_BRIGHT_MULTI, DISPLAY_POWERSHADER, DISPLAY_LIGHT_RATIO are all
      data-referenced exclusively by DisplayConfig_ParseFramerate (the display.lua loader);
      this is the per-character status-tint palette loader — a config parser, not a render
      pass; values land in the global scene/post singleton at the offsets documented in §4
    - MULTI_R/G/B channels are halved on load (the multiply tint is therefore halved;
      a display.lua value of 2.0 yields a ×1.0 identity multiply); ADD channels are raw
    - LoadingScreen_ctor builds a full-screen static image overlay (one of three
      data/ui/loading*.dds chosen at random) + progress bar + looping sound; vertex
      diffuse colours are constant opaque white — there is no alpha ramp; loading is a
      static image, not a fade-to-black
    - LensFlare_BuildGeometry is the one genuine screen-space sprite overlay: FVF 0x144
      (D3DFVF_XYZRHW | D3DFVF_DIFFUSE | D3DFVF_TEX1), 4 verts × 28-byte stride =
      112 bytes per flare; it is a sky/sun additive FX, not a state tint, flash, or fade
    - the HUD ortho layer base state has alpha-blend disabled; no full-screen damage
      vignette or status overlay panel, asset, or quad exists anywhere in the 2D layer
    - level-up and skill cues are world-space particle FX (effect managers, effects.md),
      not screen overlays; no string, no quad, no shader maps to such an overlay
  static-hypothesis: []
  capture/debugger-pending:
    - runtime authored values of the status-tint palette (display.lua ships them; the
      non-identity per-state numbers and any per-frame animation of the HIT-flash decay
      are not isolated statically); confirm by reading the palette in the live scene/post
      singleton or inspecting the shipped display.lua
    - precise visual sub-meaning of state index 6 (TYPE colouring)
    - whether the HIT-flash (state 2) has a timed decay or is a simple on/off state swap;
      confirm by breakpointing the actor status-index computation during a hit event
    - whether the loading image is ever cross-faded between zones or always a hard cut
      (static evidence says hard cut; vertex alpha is constant opaque white; [low-priority])
    - whether any data-driven UI effect table can add a brief full-screen additive HUD
      sprite for skill cues (none found statically; [low-priority])
  ida_reverified: 2026-06-28    # deep-3d wave 7 (f61f66a9): three-negative string/shader/
    config sweep plus positive attribution of loading screen, lens flare, HUD ortho layer,
    and per-character CHAR_BRIGHT palette; verdict is RESOLVED-NEGATIVE
  ida_anchor: f61f66a9ae0ec1e946105b2ecff76e8930cb1d1367df64e5688a5266f5ad9963
  readiness: RESOLVED-NEGATIVE — no screen-effect implementation is needed; the three
    remaining debugger-confirm items (palette live values, TYPE state meaning, HIT decay)
    are non-blocking for the Godot port; they belong to character_rendering.md §4.3
  evidence: [static-ida]
  resolves: character_rendering.md §4.3 open item #5 — the 9 status-tint state names are
    now confirmed from the display.lua key names (see §4); the palette offset layout
    (§4.1) extends render_pipeline.md §8 and character_rendering.md §4
  cross-links:
    - Docs/RE/specs/character_rendering.md §4.3 (per-character mul/add status-tint palette,
      cel PS constants c0/c1, dotoonshading2.psh stealth swap)
    - Docs/RE/specs/render_pipeline.md §5 and §8 (composite/present quads; scene/post
      singleton layout; DISPLAY_GLOW_RANGE_X/Y and DISPLAY_FRAMERATE at +178752/+178756/
      +178760; DISPLAY_BASE_BRIGHT_MULTI at +179016; DISPLAY_GLOW_BRIGHT_MULTI at +179020;
      DISPLAY_POWERSHADER filename buffer at +179028)
    - Docs/RE/specs/post_processing.md (full shader-handle slot table; glow/composite
      pass mechanics; scene/post singleton COM array)
    - Docs/RE/formats/effects.md (world-space particle FX for skill and level-up cues)
-->

---

## Status block

| Attribute | Value |
|---|---|
| `status` | **RESOLVED-NEGATIVE** — no full-screen tint, fade, flash, vignette, or desaturate pass exists; damage/status/stealth effects are per-character mesh modulation; zone transitions are a static loading image; skill cues are world-space particle FX |
| `binary_analysed` | `doida.exe` (legacy client, IDB anchor f61f66a9) — string sweep, shader inventory, config-key cross-reference audit, LoadingScreen_ctor walk, LensFlare_BuildGeometry walk, HUD ortho layer walk |
| `debugger_required` | NON-BLOCKING for porting — four low-priority items (palette live values, TYPE state, HIT decay timing, loading cross-fade) do not alter the verdict or the Godot port structure |
| `godot_guidance` | Do NOT implement any screen-effect subsystem from this spec. Reproduce: (a) status tints as per-character shader constants per `character_rendering.md §4.3`; (b) zone transition as a static full-screen loading image + progress bar; (c) skill cues as world-space particle FX per `effects.md` |

---

## 1. Verdict — No General Screen-Effect Subsystem

**Confidence: RESOLVED-NEGATIVE.**

There is no general full-screen post/overlay screen-effect subsystem in this client. The four
effect families the spec request enumerates (damage flash, status tint, zone-transition fade,
level-up / skill cue) do not exist as full-screen tints, fades, flashes, or vignettes drawn
over the back buffer. Each one is either a per-character mesh colour modulation already owned
by `character_rendering.md`, absent entirely, or a distinct already-documented mechanism.

| Effect family | Reality | Owner |
|---|---|---|
| Damage hit flash (screen red tint) | Per-character mesh tint, state `HIT` — the struck actor reddens, not the screen | `character_rendering.md §4.3` |
| Status tint (poison / rage / etc.) | Per-character mesh tint, states `POISON` / `ANGER` / `TYPE` | `character_rendering.md §4.3` |
| Stealth tint | Per-character mesh tint, state `HIDDEN`, plus cel-PS swap to `dotoonshading2.psh` | `character_rendering.md §4.3` + `post_processing.md §3` |
| Zone / map transition fade | **ABSENT** — no fade; zone load shows a static full-screen loading image + progress bar + looping sound (`LoadingScreen_ctor`); no alpha ramp | §3.1 of this spec |
| Level-up / skill full-screen flash | **ABSENT** as a screen overlay — no string, no quad; such cues are world-space particle FX via the effect managers | `effects.md` |
| Vignette / desaturate | **ABSENT** — no shader, no string, no quad | — |

The only genuinely screen-space / full-screen overlays that exist are: the loading image (§3.1),
the lens flare (§3.2, a sky/sun FX), and the already-documented post-process composite and
present quads (`render_pipeline.md §5`). All 2D overlays draw through the HUD ortho layer (§3.3)
whose base state has alpha-blend disabled.

---

## 2. Evidence for Absence

Three independent lines of static evidence support the verdict.

### 2.1 No Screen-Effect Strings (N1)

A full string sweep for `fade|flash|blink|tint|vignette|desatur`, `fadein|fadeout|transition|
warp|teleport`, `whiteout|blackout`, and `levelup|skill_effect` returns nothing relevant. The
single "Flash" hit is the Win32 import `FlashWindowEx` (taskbar attention flash, not a render
effect). There is no fade or transition vocabulary anywhere in the binary, and no `SetGammaRamp`
import or wiring.

### 2.2 No Screen-Effect Shaders (N2)

The complete shader set is exactly the documented cel and glow chain and nothing else:
`power1dx8.psh` (glow blur), `finaldx8.psh` (composite), `dotoonshading.vsh`,
`dotoonshading.psh`, `dotoonshading2.psh` (cel normal / stealth), and `toonramp.bmp`. There
is no tint, fade, vignette, or desaturate pixel shader. This cross-confirms `post_processing.md §3`
and `render_pipeline.md §8` — the shader-handle slots documented there are the full inventory.

### 2.3 Damage / Status / Stealth Effects Are Per-Character Config (N3)

`DisplayConfig_ParseFramerate` (the `display.lua` loader) reads the complete
`DISPLAY_CHAR_BRIGHT_*` matrix into the global scene/post singleton. The prefix is
**CHAR\_BRIGHT** (character brightness) — the values drive the per-character status-tint palette
that `character_rendering.md §4.3` already documents as the cel pixel-shader constants **c0**
(multiply) and **c1** (add), uploaded per actor during the skinned-character draw, never as a
screen pass. These are applied inside the per-actor draw loop; they produce no screen-space output.

---

## 3. Genuine Screen-Space Overlays

### 3.1 Loading Screen (Static Image — Not a Fade)

`LoadingScreen_ctor` builds the full-screen loading/zone-transition overlay:

- Selects one of three images at random from `data/ui/loading.dds`, `data/ui/loading06.dds`,
  `data/ui/loading08.dds` and loads it into a texture set.
- Plays a looping 2D sound (id 920100100) via the SoundManager.
- Builds screen-space quad geometry sized to the current screen dimensions.
- Installs a per-frame draw callback that renders a **progress bar** as a sub-quad, positioned
  by ratio of current screen size to the 1024×768 authoring reference (x-coords scaled by
  screen width ÷ 1024, y-coords scaled by screen height ÷ 768, with fixed authoring offsets).
- Runs its loader work on a boot thread at above-normal priority.

Vertex diffuse colours are constant opaque white — **there is no alpha ramp**. A zone/map
change in Godot must be reproduced as a full-screen static loading image + progress bar, not
as a tint or fade-to-black.

### 3.2 Lens Flare (Screen-Space Additive Sun Sprite — a Sky FX, Not a Tint)

`LensFlare_BuildGeometry` is the one genuine screen-space sprite overlay:

- Loads `data/sky/texture/lensflare%d.dds` (numbered flare sprites); count and per-flare
  parameters are driven by a config script.
- Vertex buffer layout: **FVF `0x144` (D3DFVF\_XYZRHW | D3DFVF\_DIFFUSE | D3DFVF\_TEX1)**,
  **112 bytes per flare = 4 verts × 28-byte stride** (16 bytes XYZRHW + 4 bytes DIFFUSE +
  8 bytes UV).
- Projects the sun / light source through the active perspective camera to a screen position;
  flares are laid along the screen-centre-to-sun axis; caches half-screen centre and per-axis
  scale each frame.

This is a camera/sun-driven additive sky FX. It is screen-space but is **not** a state tint,
flash, or fade. Do not mistake it for a screen-effect subsystem.

### 3.3 HUD Ortho 2D Layer (Base State — Alpha-Blend Disabled)

The HUD ortho layer is the pass every 2D panel and overlay draws through:

| Parameter | Value |
|---|---|
| Projection | `D3DXMatrixOrthoOffCenterLH` from (0, 0) to (screen width, screen height); Y-down; Z near −300, Z far 300 |
| World transform | Identity (set twice) |
| View transform | Identity |
| Z-buffer | Disabled |
| Cull mode | CCW (back-face cull) |
| Lighting | Disabled |
| Fill mode | Solid |
| Alpha-blend (base state) | **Disabled** — individual sprites enable blend as needed |

A full-screen damage vignette or status overlay, if it existed, would be a panel drawn here.
None exists — no asset, no string, no quad. This is the final corroboration of the
RESOLVED-NEGATIVE verdict.

---

## 4. Per-Character Status-Tint State Index

**Confidence: CONFIRMED (resolves `character_rendering.md §4.3` open item #5).**

`DisplayConfig_ParseFramerate` names the nine status-tint states via their `display.lua` key
suffixes. This resolves the open question of what each state index means.

### 4.1 State Index → Meaning

| State index | display.lua key suffix | Meaning |
|---|---|---|
| 0 | `_DEFAULT` | Normal / no status |
| 1 | `_CHOICE` | Currently-selected or targeted actor |
| 2 | `_HIT` | Damage flash (actor just received a hit) |
| 3 | `_ALPHA` | Translucent / semi-transparent actor |
| 4 | `_HIDDEN` | Stealth / concealment (pairs with the `dotoonshading2.psh` cel-PS swap) |
| 5 | `_POISON` | Poisoned status |
| 6 | `_TYPE` | Type / class colouring (specific sub-meaning `[debugger-confirm]`) |
| 7 | `_ANGER` | Rage / berserk status |
| 8 | `_AUTO` | Auto-attack / auto mode |

### 4.2 Palette Layout in the Global Scene/Post Singleton

Seven component arrays, each holding nine floats (one per state, state stride 4 bytes),
contiguous from `+178764`; three trailing scalars and the powershader filename buffer follow.
All offsets are byte offsets from the start of the global scene/post singleton (the ~179 KB
renderer object; see `render_pipeline.md §8` for the full field table and sibling fields
`+178752..+179028`).

| Component array | Base offset | Per-state step | Load-time math |
|---|---|---|---|
| MULTI\_R | +178764 | +4 per state | value **× 0.5** at load |
| MULTI\_G | +178800 | +4 per state | value **× 0.5** at load |
| MULTI\_B | +178836 | +4 per state | value **× 0.5** at load |
| ADD\_R | +178872 | +4 per state | raw |
| ADD\_G | +178908 | +4 per state | raw |
| ADD\_B | +178944 | +4 per state | raw |
| ALPHA | +178980 | +4 per state | raw |

The MULTI channels are halved on load by `DisplayConfig_ParseFramerate`. A `display.lua` value
of 2.0 therefore yields a ×1.0 identity multiply. The ADD and ALPHA channels are stored raw.

Values are uploaded as cel pixel-shader constants **c0** (MULTI\_R/G/B, 1.0) and **c1**
(ADD\_R/G/B, ALPHA) per actor during the skinned-character draw. Full details are owned by
`character_rendering.md §4.3`; the table above extends that spec with the state name mapping
and the halve-on-load math for the MULTI channels.

---

## 5. Debugger-Confirm Items

| # | Item | What to confirm | Blocking? |
|---|---|---|---|
| 1 | Authored palette values | Read the MULTI/ADD/ALPHA entries for each of the nine states in the live scene/post singleton, or inspect the shipped `data/script/display.lua` directly | Non-blocking (palette structure is known; only authored numbers are unknown) |
| 2 | TYPE state visual meaning (state index 6) | Observe the in-game colour applied to an actor in the TYPE state | Non-blocking |
| 3 | HIT-flash timed decay vs on/off | Breakpoint the actor status-index computation during a hit event and observe whether a lifetime timer drives the state exit or whether it is toggled off on the next frame | Non-blocking |
| 4 | Loading cross-fade | Confirm whether any path alpha-animates the loading image vertex colours across zone changes (static evidence says hard cut) | Non-blocking, low-priority |

---

## 6. Cross-Links and Corrections

### 6.1 Cross-Links

| Doc | Relationship |
|---|---|
| `Docs/RE/specs/character_rendering.md §4.3` | Primary owner of the per-character status-tint palette and the cel PS constant upload; §4 of this spec extends that section with state names and load-time math |
| `Docs/RE/specs/render_pipeline.md §5` | Owns the composite/present quad passes (the only genuine full-screen draw calls) |
| `Docs/RE/specs/render_pipeline.md §8` | Owns the scene/post singleton field table; fields +178752..+178760 (GLOW\_RANGE\_X/Y, FRAMERATE) and +179016..+179028 (BASE\_BRIGHT\_MULTI, GLOW\_BRIGHT\_MULTI, LIGHT\_RATIO, POWERSHADER) are listed there with their display.lua key names confirmed by this session |
| `Docs/RE/specs/post_processing.md §3` | Owns the shader-handle slot table; confirms the stealth cel-PS slot (dotoonshading2.psh at +178328) |
| `Docs/RE/formats/effects.md` | Owner of world-space particle FX for skill and level-up cues |

### 6.2 Correction — character_rendering.md §4.3

The open item `[debugger-confirm] #5` in `character_rendering.md §4.3` ("real meaning of each
state index") is now resolved by static analysis of the `display.lua` key names. The RE domain
should update that item's status to STATIC-CONFIRMED and add a reference to §4.1 of this spec
for the full state-name table.
