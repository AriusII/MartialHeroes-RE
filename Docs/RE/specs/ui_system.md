# UI System — Widget Toolkit, Screen Layouts, and Scene State Machine

> Clean-room neutral spec. Promoted from dirty-room analyst notes by the asset-spec-author.
> No legacy symbols, no addresses, no pseudo-code.
> Describes the widget class hierarchy, vtable method contract, render path, input/capture
> model, per-screen hardcoded layout coordinates, font system, string database lookup, master
> scene state machine, and Godot reconstruction guidance — so the presentation layer can be
> rebuilt faithfully.
>
> status: CODE-CONFIRMED (class hierarchy, constructor signature, vtable slot semantics, render
>         path, hit-test/capture/drag, font table, atlas-frame layout, state machine, widget tables);
>         SAMPLE-VERIFIED (atlas file presence in VFS — see formats/ui_manifests.md);
>         coordinates are CODE-CONFIRMED interop facts extracted from the per-screen build routines.

>
> Implementation targets: `05.Presentation/MartialHeroes.Client.Godot`; string lookups via
> `Assets.Parsers`; scene flow lives in `Client.Application` use-case boundaries.

---

## 0. Summary

The legacy client UI is a **custom retained-mode widget toolkit** in the `Diamond::GU*` C++ class
family. Key architectural facts:

- **All widget layouts are hardcoded.** Every login, character-select, and in-game screen is built
  by a dedicated `BuildScene` / `BuildUI` routine that issues direct widget-constructor calls with
  literal integer pixel coordinates. There is no XML, JSON, or external layout file for any screen.
  The coordinates recovered in Sections 2 and 3 are therefore **interop facts**, not guesses.
- **Reference canvas is 1024 × 768** (top-left origin, +X right, +Y down). Widget coordinates are
  in pixels, relative to their parent panel's origin.
- **Rendering uses a single shared `ID3DXSprite`** per frame. Every visible widget submits exactly
  one textured-quad blit (atlas sub-rect + translation transform + ARGB tint). There is no custom
  vertex format for UI geometry.
- **Text rendering** is GPU-side via `D3DXCreateFontA` with `HANGUL_CHARSET = 129`, using the
  standard Korean Windows system fonts. There is no VFS-shipped glyph atlas for body text.
- **UI captions** are fetched by numeric ID from `data/script/msg.xdb`, encoded CP949.
- The **master scene machine** has 9 primary states driven by a WinMain switch; in-game (state 5)
  returns to character-select (state 4), not to login.

---

## 1. Widget class hierarchy

The following classes form the complete widget tree. Every class derives ultimately from
`Diamond::GUComponent`.

| Class | Base | Role | Confidence |
|---|---|---|---|
| `GUComponent` | — | Base widget: local position, size, alpha, world-rect, transform matrix, timer, visible/enabled/hover/focus/remove flags | CODE-CONFIRMED |
| `GUComponentEx` | GUComponent | Extended base: float screen-rect, scale factors, rotation; uses a 2D transform matrix; slower fade (±32/tick vs ±64) | CODE-CONFIRMED |
| `GUPanel` | GUComponent | Container: owns a child-pointer vector and an active-child index; hit-test walks children in reverse | CODE-CONFIRMED |
| `GUWindow` | GUPanel | Top-level window: embeds a command handler (action-id dispatch), an auxiliary view, and a texture list; adds the `BuildScene` virtual slot | CODE-CONFIRMED |
| `GUButton` | GUComponent | Clickable button; up to three distinct sprite frames (normal/hover/pressed; disabled reuses normal); caption text with state-dependent colour | CODE-CONFIRMED |
| `GUCheckBox` | GUButton | Toggle button; checked state = the PRESSED frame; chains the 3-state button constructor | CODE-CONFIRMED |
| `GULabel` | GUComponent | Read-only text display; owns two CP949 string fields (caption and an auxiliary text) | CODE-CONFIRMED |
| `GULabels` | GULabel | Multi-line label variant | CODE-CONFIRMED |
| `GUTextbox` | GUComponent | Editable text field; registers itself into the IME focus slot so Korean composition works; password masking; caret blink | CODE-CONFIRMED |
| `GUList` | GUComponent | List / listbox; 11 specialised virtual slots; vertical scroll clipping | CODE-CONFIRMED |
| `GUScroll` | GUComponent | Simple scrollbar (up/down button children + thumb) | CODE-CONFIRMED |
| `GUScrollEx` | GUPanel | Extended scrollbar with panel-hosted children | CODE-CONFIRMED |
| `GUCanvas3D` | GUComponent | 3D viewport widget — renders a live model into a 2D UI rectangle; used for character previews on the select screen | CODE-CONFIRMED |

### 1.1 Universal constructor argument order

All widget subtypes share the same argument order after the implicit instance pointer:

```
Constructor(textureId, x, y, w, h, srcX, srcY, actionId)
```

| Argument | Instance field offset | Meaning | Confidence |
|---|---|---|---|
| `textureId` | +0x90 | Bound texture / sprite-sheet handle (`uitex.txt` ID or 0 when set later) | CODE-CONFIRMED |
| `x` | +0x14 | Local X in pixels, relative to parent panel origin | CODE-CONFIRMED |
| `y` | +0x18 | Local Y in pixels, relative to parent panel origin | CODE-CONFIRMED |
| `w` | +0x1C | Width in pixels | CODE-CONFIRMED |
| `h` | +0x20 | Height in pixels | CODE-CONFIRMED |
| `srcX` | +0x24 | Source atlas X: the authored U origin (copied by the button draw into the live RECT at +0x34) | CODE-CONFIRMED |
| `srcY` | +0x28 | Source atlas Y: the authored V origin | CODE-CONFIRMED |
| `actionId` | **+0x10** | Integer action identifier delivered to `OnAction` on click-release — **not +0x0C** (see §1.2) | CODE-CONFIRMED |

So each widget encodes: "draw the sub-rect at atlas pixel `(srcX, srcY)` of size `(w, h)` at
screen-local `(x, y)`; on click fire `actionId`." This is a classic atlas sub-rect / sprite-sheet
UI. The 7-state button constructor additionally stores up to two extra `(srcX, srcY)` pairs
covering hover and pressed sprite frames (DISABLED always reuses the NORMAL origin — see §1.5).

### 1.2 Widget field offsets (selected load-bearing fields)

These are instance-field offsets on the base `GUComponent`, shared by all subclasses.

| Offset | Size | Type | Role | Confidence |
|---|---|---|---|---|
| +0x00 | 4 | ptr | vtable pointer | CODE-CONFIRMED |
| +0x04 | 4 | u32 | Current alpha (0–255); fade target chased by show/hide integrator | CODE-CONFIRMED |
| +0x08 | 4 | u32 | Capability flags (bit 0 = enabled; panel-child bit; window high-bit) | CODE-CONFIRMED |
| **+0x0C** | 4 | u32 | **Tint/colour (RGB, low 24 bits)** — used as `(alpha<<24) \| (tint & 0x00FFFFFF)` in the ARGB blit; **NOT the actionId** | CODE-CONFIRMED |
| **+0x10** | 4 | i32 | **actionId** — set by `Panel_AddChildWithAction`; read by the vtable slot-10 getter; the window dispatcher routes this on click-release | CODE-CONFIRMED |
| +0x14 | 4 | i32 | Local X | CODE-CONFIRMED |
| +0x18 | 4 | i32 | Local Y | CODE-CONFIRMED |
| +0x1C | 4 | i32 | Width | CODE-CONFIRMED |
| +0x20 | 4 | i32 | Height | CODE-CONFIRMED |
| +0x24 | 4 | i32 | Authored atlas source X (primary) | CODE-CONFIRMED |
| +0x28 | 4 | i32 | Authored atlas source Y (primary) | CODE-CONFIRMED |
| +0x2C | 4 | i32 | World X (absolute screen position, set by UpdateTransform) | CODE-CONFIRMED |
| +0x30 | 4 | i32 | World Y | CODE-CONFIRMED |
| +0x34 | 4 | i32 | Live src-RECT left (= srcX of the currently selected frame, written before every blit) | CODE-CONFIRMED |
| +0x38 | 4 | i32 | Live src-RECT top (= srcY) | CODE-CONFIRMED |
| +0x3C | 4 | i32 | Live src-RECT right (= srcX + w) | CODE-CONFIRMED |
| +0x40 | 4 | i32 | Live src-RECT bottom (= srcY + h) | CODE-CONFIRMED |
| +0x44 | 64 | f32[16] | D3D translation matrix — built by UpdateTransform from world (x, y); passed to `ID3DXSprite::SetTransform` | CODE-CONFIRMED |
| +0x0F (byte) | 1 | u8 | Forced-alpha override (0xFF = no override; any other value pins alpha immediately, bypassing the fade) | CODE-CONFIRMED |
| +0x88 (byte) | 1 | u8 | Hovered flag (set by hit-test) | CODE-CONFIRMED |
| +0x89 (byte) | 1 | u8 | Hover-edge flag (fires enter/exit on the first frame a hover state changes) | CODE-CONFIRMED |
| +0x8A (byte) | 1 | u8 | Focus-eligible flag (1 = clicking sets the global capture pointer) | CODE-CONFIRMED |
| +0x8B (byte) | 1 | u8 | Focused flag (1 = this widget has keyboard/IME focus) | CODE-CONFIRMED |
| +0x8C (byte) | 1 | u8 | Show/hide target (1 = visible/showing, 0 = hiding; alpha chases this) | CODE-CONFIRMED |
| +0x8D (byte) | 1 | u8 | Remove-mark flag (1 = sweep this child out on the next RemoveMarkedChildren pass) | CODE-CONFIRMED |
| +0x90 | 4 | u32 | Bound texture ID | CODE-CONFIRMED |
| +0x98 | 4 | u32 | Timer expiry (ms) | CODE-CONFIRMED |
| +0x9C | 4 | u32 | Timer interval (default 3000 ms) | CODE-CONFIRMED |
| +0xA0 | 4 | ptr | Timer callback pointer (null if unused) | CODE-CONFIRMED |

Per-text-widget font-slot fields (see §6.3):

| Offset | Size | Type | Role | Confidence |
|---|---|---|---|---|
| +0xDC | 4 | i32 | **`GUTextbox` font-slot index** — zero-initialised by the textbox constructor; selects the font-table slot at draw time | CODE-CONFIRMED |
| +0xE4 | 4 | i32 | **`GULabel` font-slot index** — zero-initialised by the label constructor; selects the font-table slot at draw time | CODE-CONFIRMED |

Panel additions:

| Offset | Size | Role | Confidence |
|---|---|---|---|
| +0xA8 | 4 | Child-vector begin pointer | CODE-CONFIRMED |
| +0xAC | 4 | Child-vector end pointer | CODE-CONFIRMED |
| +0xB0 | 4 | Child-vector capacity pointer | CODE-CONFIRMED |
| +0xB4 | 4 | Active-child index (init = −1) | CODE-CONFIRMED |

Window additions (beyond panel):

| Offset | Size | Role | Confidence |
|---|---|---|---|
| +0xBC | — | Embedded command handler (action-id dispatch; own secondary vtable) | CODE-CONFIRMED |
| +0xE8 | — | Auxiliary view data | CODE-CONFIRMED |
| +0x220 | — | Texture list (GUTextureList) | CODE-CONFIRMED |

### 1.3 Draw-from-atlas semantics (CODE-CONFIRMED)

Each frame the engine builds a per-widget D3D translation matrix from the widget's world `(x, y)`
and submits the sprite to the single shared `ID3DXSprite`. The **live src-RECT at +0x34** holds
`{left=srcX, top=srcY, right=srcX+w, bottom=srcY+h}` in atlas pixels and is passed as
`pSrcRect` to `ID3DXSprite::Draw`. The blit color is `(alpha<<24) | (tint & 0x00FFFFFF)` where
alpha is the current fade alpha (+0x04) and tint is +0x0C. Atlas pixels map directly to screen
pixels at 1:1 on the reference 1024 × 768 canvas.

### 1.4 Action-id dispatch (CODE-CONFIRMED)

When a button is clicked (pressed and released inside its bounds), the GUWindow's embedded command
handler walks the active panel's child vector and reads each child's action id from field **+0x10**
via the vtable slot-10 getter. The first hit child's id is routed to the window's `OnAction`.
The 7-state button stores the hover/pressed/disabled sprite origins but fires the same `actionId`
regardless of interaction state. **`actionId` lives at +0x10; the field at +0x0C is the tint/colour
RGB** (see §1.2 field table).

### 1.5 GUButton frame-state machine (CODE-CONFIRMED)

GUButton stores up to four `(srcX, srcY)` atlas-frame origin pairs at fixed offsets. The draw
routine selects one pair per frame based on the interaction state and copies it into the live
src-RECT at +0x34. **The three button constructor variants always leave DISABLED equal to NORMAL**
— there is no distinct disabled sprite origin; the draw instead applies a grey caption colour.

| Field offset | Pair holds | Selected when |
|---|---|---|
| +0xC8 / +0xCC | **NORMAL** (srcX, srcY) | default; also DISABLED |
| +0xD0 / +0xD4 | **HOVER** (srcX, srcY) | hovered byte (+0x88) == 1 |
| +0xD8 / +0xDC | **PRESSED** (srcX, srcY) | pressed byte (+0xC0) == 1 |
| +0xE0 / +0xE4 | **DISABLED** (srcX, srcY) | disabled byte (+0xF8) != 0 — always equals NORMAL from the ctor |

Additional button fields:

| Offset | Role |
|---|---|
| +0xC4 | State-count field (2, 3, or 7) — informational only; does not change draw logic |
| +0xC0 | Pressed byte (1 = button is depressed) |
| +0xF8 | Disabled byte (non-zero = disabled) |
| +0xA4 | Caption string (CP949 `std::string`) |
| +0xEC / +0xF0 | Caption draw offset (dx, dy) added to world position |
| +0xF4 | Highlight/selected colour override (−1 = none; used for tab-selected buttons) |

**Frame-selection precedence** (later conditions win): disabled > pressed > hovered > normal.

**Constructor variants and the frame they fill:**

| Constructor | NORMAL | HOVER | PRESSED | DISABLED |
|---|---|---|---|---|
| 2-state | arg `(sX, sY)` | = NORMAL | = NORMAL | = NORMAL |
| 3-state | arg `(sX, sY)` | = NORMAL | arg `(a9, a10)` | = NORMAL |
| 7-state | arg `(sX, sY)` | arg `(a11, a12)` | arg `(a9, a10)` | = NORMAL |

So **all three constructors produce at most 3 distinct sprite frames** (normal, hover, pressed).
Where HOVER equals NORMAL the button gives caption-only feedback on hover; where both equal
NORMAL the sprite never changes. The "7-state" label refers to the state-count field value,
not to a count of distinct sprites.

> **Front-end 3-state argument order (CODE-CONFIRMED).** The 3-state button builder used by the
> login and character-select scenes takes its three frame origins in the order
> **NORMAL, PRESSED, HOVER** (`(N_x, N_y, P_x, P_y, H_x, H_y)`). When reading a front-end widget
> table (§8.1 / §8.2), apply this order: the second pair is the PRESSED frame and the third is
> HOVER. On most front-end buttons PRESSED equals NORMAL, so the only distinct extra frame is HOVER.

**Caption colour by interaction state:**
- Disabled → grey (`0xFF666666`)
- Hovered → yellow (`0xFFFFFF00`)
- Normal/pressed → per-widget tint at +0x0C (default white)

### 1.6 Window-manager doctrine — "MainMaster" IS the manager (CODE-CONFIRMED)

There is **no separate "WindowManager" class** in the toolkit. The in-game HUD master window — a
single global `GUWindow` instance constructed with the name **"MainMaster"** — *is* the window
manager. Its layout is the multiple-inheritance `GUWindow` shape (`structs/guwindow.md`) plus a
**flat service-slot pointer table** in its tail region.

- **A singleton accessor with a very high call-count.** Every subsystem reaches the master window
  through one global accessor ("give me the master HUD window"), which lazily constructs the master
  window on first use. This accessor is one of the most-called functions in the whole client
  (on the order of ~1.8k call sites) — the de-facto "HUD orchestrator handle".
- **A flat service-slot registry.** On construction the master window **zero-initialises** a
  contiguous run of pointer-width service slots in its tail region. Each subsystem (inventory,
  skills, chat, map, party, quest, guild, trade, options, …) later **registers its constructed
  panel pointer into a fixed slot**. The HUD is the sum of those slots hanging off the one
  "MainMaster" window. The same zero-init routine runs again on teardown to clear the registry.
- **Slot-table bounds.** The service-slot region spans the master window's tail (~223 pointer-width
  slots); its exact byte offsets and the ~6 currently-identified slot owners (including the
  back-reference to the owning HUD handler) are documented in `structs/runtime_singletons.md §3.10`.
  The full slot→subsystem map is an open item (which subsystem owns which slot index is not yet
  enumerated).

So the manager is **not** a dispatcher object with a window list — it is a single top-level window
whose embedded pointer table holds every HUD child panel. A reimplementation can model this as one
root HUD node owning a fixed-index array of child-panel references, reached through a singleton
accessor.

### 1.7 RTTI-confirmed widget / panel catalogue (CODE-CONFIRMED class names)

The toolkit's class names were read directly from the binary's own type metadata (MSVC RTTI), so
these are exact class identities, not heuristics. Roughly **202** distinct widget/panel classes were
recovered this way. The catalogue below groups them by clean role. Each class has a default and/or a
sized `(x, y, w, h, …)` constructor; both install the same per-class vtable.

**Leaf widgets** (direct `GUComponent` subclasses unless noted) — the reusable toolkit controls:

| Role group | Classes |
|---|---|
| Buttons | `GUButton` (with several sized-constructor overloads of differing arg shapes), `GUCheckBox` (derives `GUButton`) |
| Text / labels | `GULabel`, `GULabels` (multi-line), `GUShortLabel`, `GUTextbox` (editable, IME-routed) |
| Lists / scroll | `GUList`, `GUScroll`, `GUScrollEx` (**derives `GUPanel`**, a scroll container that is itself a panel) |
| Containers | `GUPanel` (base container), `GUComponentEx` (extended-base: float rect, scale, rotation), `GUCanvas3D` (live 3D viewport widget) |
| Windows | `GUWindow` (top-level; see §1.8 / `structs/guwindow.md`) |

**Game panels** (~165 application screens, each one C++ class deriving from `GUPanel`, named here by
clean role). This is a representative grouping, not an exhaustive list — the full enumeration is
class-by-class but the role buckets are what an implementer needs:

| Panel family | Representative panels |
|---|---|
| Actor-state / vitals HUD | actor-state (HP/MP) panel, cast-time panel, upgrade-process panel, gauge panel |
| Map | map panel (minimap), total-map / full-map panel |
| Chat | chat-output panel |
| Social | friend panel, mini-party, party panel, relation panel |
| Stall / market | stall-keeper panel, stall-list panel |
| Skills / status | skill panels, status panel, war-info panel |
| War UI | the "brood-war" war-UI family (including nested war map-info / map-state panels) |
| Guild | the guild panel family |
| Mail / carrier | the carrier-pigeon (mail/delivery) family |
| Options | option sub-panels (Character, Graphic, Sound, Other) |
| Items / trade | item panels, trade panel, product panel, goods panel, gift-character flow |
| NPC dialog | the NPC-dialog panel family |
| Menus / misc | default menu, tutorial / help / greeting / announce / lottery panels |

> The exact per-class names are CP949-context Korean-game class identities read from RTTI; the role
> grouping above is the load-bearing fact for a reimplementation (which panels exist and what they
> do). Specific panels' widget layouts and atlas bindings are tabulated in §8 where recovered.

### 1.8 GUWindow multiple inheritance (cross-reference)

`GUWindow` is the only class with **two vtable pointers**: the primary component/panel/window chain
at +0x00 and a secondary event-handler vtable at +0xBC. It embeds a command handler (from +0xBC), an
auxiliary 3D/scene view (from +0xE8), and a texture/skin-atlas list (from +0x220). Five concrete
windows derive from it (MainWindow/"MainMaster", the login window, the opening window, the
character-select window, and a dev/test window). The full offset table is in `structs/guwindow.md`;
the base-class field layout is in `structs/gucomponent.md`.

---

## 2. Vtable method layout (shared slot semantics) (CODE-CONFIRMED)

All GU classes share a regular vtable shape. Slot index = (byte offset / 4). The table below is the
authoritative "what is each virtual" map for the toolkit engine.

| Slot | Byte offset | Method name | Notes |
|---|---|---|---|
| 0 | +0x00 | Destructor | scalar-deleting |
| 1 | +0x04 | **SetShown(bool)** | sets the alpha-fade target (+0x8C); `GUList` overrides for its own children |
| 2 | +0x08 | (small accessor) | base read-only |
| 3 | +0x0C | (small accessor) | base read-only |
| 4 | +0x10 | (rect setter) | base integer version; `GUComponentEx` overrides with float rect |
| 5 | +0x14 | **HitTest(x, y) → bool** | AABB against world pos+size; sets hovered byte; fires enter/exit edges. `GUButton` disables when +0xF8 set; `GUComponentEx` uses float rect + scale factors |
| 6 | +0x18 | **OnEvent(evt) → consumed** | per-class event handler; Panel/Window walk children; Button toggles pressed byte; Textbox/Checkbox/List override |
| 7 | +0x1C | **Draw()** | per-frame render; Panel/Window/List iterate children front→end (back-to-front); leaf widgets submit one sprite blit |
| 8 | +0x20 | (per-class hook) | base no-op; Panel/Window = child-management variant |
| 9 | +0x24 | **UpdateTransform()** | computes world pos from local + parent; builds D3DXMatrixTranslation at +0x44 |
| 10 | +0x28 | **GetActionId() → int** | returns field **+0x10**; Panel/Window override with a child-count variant |
| 11 | +0x2C | **OnHoverEnter()** | base no-op; Button = re-press if the widget is the current capture target |
| 12 | +0x30 | **OnHoverExit()** | base no-op; Button = un-press if the widget is the current capture target |
| 13 | +0x34 | **RemoveMarkedChildren()** | Panel/Window/ScrollEx only; sweeps children with remove-flag +0x8D == 1 |
| 14 | +0x38 | **BuildScene()** | Window only; the concrete subclass overrides with its hardcoded widget tree |

---

## 3. Render path — the draw-submit pipeline (CODE-CONFIRMED)

### 3.1 Top-level traversal

The window/panel Draw (vtable slot 7) is a depth-first walk:

1. `UpdateTransform(self)` — build world rect and the D3D translation matrix at +0x44.
2. `DrawSelf(self)` — if a texture id is bound (+0x90) and alpha > 0, submit one sprite blit.
3. For each child in the child vector (front → end, i.e. back-to-front paint order):
   - if the child's show/hide target (+0x8C) == 1: call `child.UpdateTransform()` then `child.Draw()`.

**Paint order = child-vector insertion order.** A child added later draws on top. This is the only
z-ordering mechanism — there is no explicit z-index field.

### 3.2 The leaf draw-submit

Every leaf widget's base draw does:

1. **Alpha fade.** The current alpha at +0x04 chases the show/hide target at +0x8C: showing →
   alpha climbs +64 per tick toward 255; hiding → alpha falls −64 toward 0 (clamped [0,255]).
   The `GUComponentEx` variant uses ±32 per tick (slower fade). If the forced-alpha byte at +0x0F
   is not 0xFF, it pins alpha immediately (bypasses the fade entirely — used for blackout overlays).
2. **Submit** (only if a texture id is bound at +0x90 and the widget is visible):
   call `UISprite_DrawRect(g_render, textureId, pSrcRect=+0x34, pMatrix=+0x44, color)` where
   `color = (alpha<<24) | (tint+0x0C & 0x00FFFFFF)`.

### 3.3 The sprite blit

The UI uses one **`ID3DXSprite`** singleton (lives on the engine render object). Per submit:
- `sprite.SetTransform(pMatrix)` — the translation matrix at +0x44 (screen x, y).
- `sprite.SetState(48)` — alpha-blend enabled mode.
- `sprite.Draw(texture, pSrcRect, NULL, NULL, color)` — the single draw call.
- `sprite.Flush()` — immediate submission.

The `pSrcRect` is the live RECT at +0x34 in atlas pixels. **There is no custom UI vertex format**;
`ID3DXSprite` builds its own transformed textured quads internally.

For `GUComponentEx` a richer blit uses `D3DXMatrixTransformation2D` (center, scale, rotation,
translation) with `SetState(16)` — this is the "can be rotated/scaled" sprite path.

---

## 4. Input — hit-testing, mouse capture, and drag (CODE-CONFIRMED)

### 4.1 Integer hit-test (vtable slot 5)

AABB test of cursor `(x, y)` against the widget's world position+size fields (+0x2C/+0x30 origin,
+0x1C/+0x20 size). Result written to the hovered byte (+0x88). On a hover state **edge** it sets
the edge flag (+0x89) and fires `OnHoverEnter` (slot 11) on the first inside frame or `OnHoverExit`
(slot 12) on the first outside frame.

A **disabled** button (+0xF8 != 0) never reports a hover hit — `GUButton::HitTest` returns false
immediately for disabled widgets.

### 4.2 Global click-capture pointer

There is **one global capture pointer** (`g_ClickCapture`):

- **Mouse-down** on a focus-eligible widget (+0x8A == 1) → `g_ClickCapture = this`.
- **Mouse-up** while `g_ClickCapture == this` → clear it, then build a synthetic "click-released"
  event (type 6) and enqueue it to the input manager. The window command-dispatch turns this
  synthetic event into an `OnAction(actionId)` call.

A click fires its action **only if pressed and released on the same widget** (classic press-inside /
release-inside semantics).

### 4.3 Button drag behaviour

While a button is the capture target:
- Moving the cursor **off** the button → `OnHoverExit` (slot 12) clears the pressed byte
  (+0xC0 = 0); the button visually pops up.
- Moving **back** onto the button → `OnHoverEnter` (slot 11) re-presses it (+0xC0 = 1).
- **Releasing outside** → the capture is cleared without a type-6 event reaching the button, so
  **no action fires** — the standard "drag off to cancel" behaviour.

### 4.4 GUList reverse hit-test (z-order for input)

`GUList::OnEvent` walks children **end → front** (reverse of paint order) so the topmost-painted
child wins input. For move events it continues scanning to update all hover states; for click events
it returns on the first consuming child.

---

## 5. Text entry — `GUTextbox` (CODE-CONFIRMED)

### 5.1 Focus and IME registration

On focus: the textbox fetches the IME context singleton, registers itself, enables IME composition
(`IME_SetEnabled(1)`), and sets its focused byte (+0x8B = 1). Korean composition (CP949) then flows
through the WndProc IME handler into this textbox's edit buffer. Only one textbox is the IME target
at a time.

### 5.2 Draw — password mask, scroll, and caret blink

- **Password mode** (flag bit at +0xA4-region): draws one `'*'` glyph per character, advancing
  **6 pixels** per character (fixed, regardless of actual glyph metrics).
- **Normal mode**: draws the visible substring from a scroll offset (+0xD8), starting at
  `fieldX + pad (+0xAC)`, `fieldY + pad (+0x30) + 2`.
- **Caret**: when focused (+0x8B == 1), a blink toggle (flips sign every **500 ms**) gates the
  caret glyph. The caret is drawn at the end of the visible text (or at `6 × visibleLen` from the
  field origin in masked mode).

### 5.3 Click handling

Left-click into a textbox focuses it and **consumes** the click event, preventing it from falling
through to world-entity selection.

---

## 6. Font system (CODE-CONFIRMED)

### 6.1 Mechanism

Font objects are created at startup (master scene state 1 — Login) using the Direct3D font API with:
- **charset = 129 = HANGUL_CHARSET** — the load-bearing Korean encoding constant. The client
  renders Korean glyphs through the OS Hangul code page (CP949). No bitmap glyph atlas is shipped
  in the VFS for body text.
- The creation call signature is `Font_Create(table, slotByteOffset, faceName, sizeFallback,
  charWidth, rowHeight, weight)` where the D3DX API receives **Height = rowHeight** and
  **Width = charWidth**. The `sizeFallback` field is stored in the slot descriptor but is not the
  D3DX Height.
- Font objects are re-created after a device reset by re-issuing all 15 slots.

### 6.2 The 15-slot font descriptor table

Built once in the Login state (state 1).

| Slot | Face | D3DX Height (rowHeight) | D3DX Width (charWidth) | Weight | Confidence |
|---|---|---|---|---|---|
| 0 | DotumChe | 12 | 6 | 0 | CODE-CONFIRMED |
| 1 | Dotum | 10 | 5 | 0 | CODE-CONFIRMED |
| 2 | DotumChe | 32 | 16 | 800 | CODE-CONFIRMED |
| 3 | DotumChe | 24 | 12 | 800 | CODE-CONFIRMED |
| 4 | DotumChe | 12 | 6 | 800 | CODE-CONFIRMED |
| 5 | BatangChe | 12 | 6 | 0 | CODE-CONFIRMED |
| 6 | BatangChe | 24 | 12 | 700 | CODE-CONFIRMED |
| 7 | BatangChe | 12 | 6 | 700 | CODE-CONFIRMED |
| 8 | BatangChe | 12 | 6 | 700 | CODE-CONFIRMED |
| 9 | DotumChe | 12 | 6 | 700 | CODE-CONFIRMED |
| 10 | Dotum | 20 | 10 | 800 | CODE-CONFIRMED |
| 11 | DotumChe | 10 | 5 | 400 | CODE-CONFIRMED |
| 12 | DotumChe | 12 | 6 | 400 | CODE-CONFIRMED |
| 13 | DotumChe | 14 | 7 | 400 | CODE-CONFIRMED |
| 14 | DotumChe | 16 | 8 | 400 | CODE-CONFIRMED |

Column definitions:
- **Face** — the TrueType face name passed to the font API. **DotumChe** (돋움체) is
  fixed-pitch sans-serif; **Dotum** (돋움) is proportional sans-serif; **BatangChe** (바탕체) is
  fixed-pitch serif. These are standard Korean Windows system fonts, not VFS assets.
- **D3DX Height** — the `Height` parameter to `D3DXCreateFontA` (the row height in pixels).
  **Note:** an earlier version of this spec swapped Height and Width. The values above are correct:
  `D3DXCreateFontA` Height = rowHeight (the taller/larger value per slot).
- **D3DX Width** — the `Width` parameter (the character cell width in pixels).
- **Weight** — the GDI weight value (0 = default, 400 = regular, 700 = bold, 800 = extra-bold).

### 6.3 Text rendering — fixed-advance grid; per-widget font-slot selection

`Font_DrawString(table, x, y, str, color, fontSlot, scale)` computes a bounding rect as
`{x, y, x + charWidth(slot)*strlen, y + rowHeight(slot)*scale}` and calls
`ID3DXFont::DrawTextA(NULL, str, -1, &rect, DT_SINGLELINE, color)`. Horizontal layout is
therefore **fixed-advance** (charWidth per character, not proportional metrics), so labels are
laid out on a monospace grid even with proportional faces. This matters for matching legacy text
positioning.

**Per-widget font-slot index (CODE-CONFIRMED).** Each text widget stores its font slot as an
instance field and supplies it to the shared text-draw helper at draw time:
- `GULabel` — slot at **+0xE4**.
- `GUTextbox` — slot at **+0xDC**.
- `GUButton` caption — the button's own caption-slot field (offset not byte-pinned; see §12 open
  item 4); the caption draws through the same shared font helper.

Both label and textbox constructors **zero-initialise** their slot field, so the default slot is
**0** (DotumChe, height 12 / width 6 / weight 0 — §6.2 slot 0).

**Front-end resolution (CODE-CONFIRMED — closes the long-open "per-widget font slot" question for
login and character-select).** A full sweep of the login `BuildScene` found **no write** to any
text widget's font-slot field after construction; the label/textbox slot fields are left at their
ctor default. Therefore **every login label, caption, and textbox draws with font slot 0**
(DotumChe 12 / 6 / weight 0). Button captions on the login window are mostly sprite-only or empty,
so slot 0 is the default for any caption that does draw. The same field mechanism applies on the
character-select scene; the front-end as a whole uses slot 0 universally for its text — there is
no per-widget font differentiation on the login or character-select screens at the build level.

> **In-game caveat.** In-game windows (Section 8.7+) DO use larger / bold slots (e.g. titles via
> slots 2 / 3 / 10 per §6.2); per-widget font variety lives in those builders, not the front-end.
> The slot-0-universal finding is scoped to the **front-end** (login + character-select).

### 6.4 Effect bitmap fonts (not UI body text)

Two bitmap font strips exist in the VFS for **in-world floating combat numbers** only; they are
not part of the UI text system:
- `data/effect/tex/att-font.dds` — attack number strip (30 × 240 px, likely 10 digits × 24 px)
- `data/effect/tex/cri-font.dds` — critical-hit number strip (30 × 240 px)

---

## 7. Show/hide lifecycle and z-order (CODE-CONFIRMED)

### 7.1 Alpha-fade show/hide

- The **show/hide target byte at +0x8C** is the animation target; the current alpha at +0x04
  chases it ±64 per tick (±32 for `GUComponentEx`). A hidden widget fades to alpha 0; the parent's
  draw loop skips children whose target is 0 (or alpha is 0). So hide is a **fade-out**, not an
  instant disappearance.
- **Fade law (CODE-CONFIRMED).** Each frame, the leaf draw integrates alpha toward the target:
  when the target byte is 1 (showing) and alpha < 255, alpha increases by 64; when the target is 0
  (hiding) and alpha > 0, alpha decreases by 64. After the step, alpha is explicitly clamped to
  the range **[0, 255]**. The `GUComponentEx` variant uses **±32 per tick** (slower fade). So the
  base step is **±64/tick** and the `Ex` step is **±32/tick**, both clamped [0, 255].
- The `SetShown(bool)` call (vtable slot 1) additionally **snaps** alpha to the endpoint on the
  state change — it writes the target byte and immediately sets alpha to 255 (showing) or 0
  (hiding). The ±64 integrator in the draw then merely holds it at the endpoint. So a SetShown
  toggle jumps to the final alpha at once; the per-tick fade is the steady-state maintainer.

### 7.2 Forced-alpha override (present but unarmed on the front-end)

- The **forced-alpha byte at +0x0F** is a per-widget override field. After the ±64 fade integrator
  runs and clamps, the leaf draw reads this byte: if it is **not 0xFF**, it pins the current alpha
  to that value immediately, **bypassing the fade** — the mechanism used for dimmed / blackout
  overlays. **0xFF = no override** (the default).
- **Front-end status (CODE-CONFIRMED): present but UNARMED.** The login `BuildScene` writes **no**
  non-0xFF value into +0x0F for any login widget — every login widget keeps the default 0xFF, so
  the login screen never uses the forced-alpha blackout path; its show/hide is the ordinary ±64
  fade plus the SetShown alpha-snap. The forced-alpha mechanism is real and fully wired, but the
  front-end does not arm it. (The earlier "forced-alpha on some login widgets" hypothesis is
  REFUTED for the front-end; the mechanism exists engine-wide for in-game overlays.)

### 7.3 Z-order

- **Paint order = insertion order:** later children paint on top.
- **Input order = reverse:** hit-test walks children end → front; the topmost-painted child
  wins input.
- There is **no z-index field**. Reordering means removing and re-adding a child, or using the
  active-child index (+0xB4) for tab panels.

### 7.4 Child management

- `Panel_AddChildWithAction(parent, child, actionId)`: sets `child[+0x84] = parent`,
  `child[+0x10] = actionId`, pushes into the parent's child vector at +0xA8.
- `Panel_AddChild(parent, child)`: same without an action id (decorative widgets).
- `RemoveMarkedChildren` (vtable slot 13): sweeps children with remove-flag +0x8D == 1 and
  compacts the vector — the deferred-removal mechanism.

### 7.5 GUList vertical scroll

The list draws self, then for each child computes visibility against the scroll offset (+0xDC) and
viewport height (+0x20): children outside the viewport are hidden (SetShown(0)); visible ones have
their draw position offset by `childY − scrollY`. Clipping is by show/hide + position offset,
not a hardware scissor.

### 7.6 Login scene draw / z-order (CODE-CONFIRMED)

The login window's Draw (vtable slot 7) is the generic container walk of §3.1: it draws itself
(alpha-fade submit), then iterates its child-pointer vector front → end, drawing each child whose
show/hide target (+0x8C) is 1. There is **no per-state draw branch** — the per-state appearance is
produced entirely by the state machine (§11.3) toggling child visibility. Z-order is therefore the
child-insertion order of the login `BuildScene`.

**Top-level paint order (back → front).** The login window adds its top-level panels directly, in
this order (index 1 paints first / furthest back; the last paints on top):

| Paint order | Top-level panel | Notes |
|---|---|---|
| 1 | Full-screen background image (loginwindow.dds, ~`1024 × 490` at y 110) | added hidden, revealed by the flow |
| 2 | Login-form / option-EULA container (`270, 85, 483 × 490`) | hosts decorative spinners/dots + the EULA-style label block |
| 3 | Upper intro-curtain panel (login_slice1.dds, `0, 0, 1024 × 398`) | animated Y; see §7.7 intro curtain |
| 4 | Server-name-strip / intro-banner container (`270, 85, 483 × 490`) | hosts the server-strip buttons + the panning banner + Quit/Help strip |
| 5 | Quit-confirm popup #1 (InventWindow.dds, `342, 289, 340 × 190`) | prompt + Yes |
| 6 | Quit-confirm popup #2 (same chrome) | prompt + Yes |
| 7 | Lower panel (login_slice1.dds, screen-height-scaled, `1024 × 442`) | hosts the Server-list button + the account/PW form sub-panel |
| 8 | Server-list widget (`347, 173, 329 × 422`) | the list-like server picker |
| 9 | Option-page panel (`356, 531, 313 × 132`) | option tab buttons |
| 10 | Exit-confirm modal panel (InventWindow.dds, `342, 289, 340 × 190`) | quit-confirm composite |
| 11 | Error modal panel (InventWindow.dds, same chrome) | drawn last = topmost |

The account/PW form sub-panel, the Server-list button, and the panning banner image are **nested**
inside the lower panel (#7); the server-name-strip buttons and the banner are nested inside the
banner container (#4) — so they paint within their parent's slot in this order, not as top-level
siblings.

### 7.7 Login intro curtain (CODE-CONFIRMED)

The login opening "split" animation is **not an alpha ramp** — it is a **two-panel vertical slide**
driven by the login state machine (§11.3, sub-states 1→2→3). A module-global slide accumulator
starts at 0; on the slide sub-state it increases by **+5 per tick**, and each tick the upper curtain
panel's local Y is set to `−accumulator` (slides up/off-screen) while the lower panel's local Y is
set to `accumulator + 326` (slides down). When the accumulator passes **200** the server-list
widget is repositioned to begin revealing; when it passes **222** the slide is complete (~45 ticks)
and the machine advances. There is a **curtain instant-open / skip path**: when the flow leaves the
intro sub-state before the slide finishes (an action fires, or the screen is re-entered), the
curtain is **snapped** to its open position at once (upper Y = 0, lower Y = 326; background shown,
form/option/popup panels hidden) rather than animated.

---

## 8. Screen construction — hardcoded layouts

No external layout file is read for login or character-select. Each screen has a dedicated
`BuildScene` / `BuildUI` method that calls widget constructors in sequence with literal integer
pixel arguments. The coordinate tables below are **interop facts** extracted from those routines.

### 8.0 Coordinate space

- Reference canvas: **1024 × 768** pixels, top-left origin, +X right, +Y down.
- `(x, y)` in the tables is the widget's **screen-local** position (relative to its immediate
  parent panel's top-left). Add ancestor panel positions to find the world position.
- `srcX`, `srcY` is the atlas **UV origin** for the widget's sprite sub-rect; the sprite
  sub-rect is `(srcX, srcY)` to `(srcX + w, srcY + h)` within the named atlas.

### 8.1 Login screen — `BuildScene` (CODE-CONFIRMED)

> **Widget-object count (CODE-CONFIRMED, corrected).** The login `BuildScene` constructs
> **73 widget objects** in total. An earlier version of this spec listed "21 ctor sites" — that
> figure counted only the *visible distinct widget roles* on the front login form. The full object
> count includes the EULA-style scroller label block (~25 labels), the per-strand EULA group loop
> (×2), the intro/banner slide images (4), the five server-name-strip buttons, two server pager
> spinners, the banner-overlay images, the version-info panel, and three modal sub-panels (two
> quit-confirm popups plus exit/error composites). The "21" front-form roles are the table below;
> the remaining objects are decorative/container/looped widgets folded into the role groups.

**Atlas assignment (CODE-CONFIRMED from builder source):**

| Atlas DDS | Used for |
|---|---|
| `data/ui/login_slice1.dds` | OK/Login button, Server-list button, ID/PW textboxes, Save-ID checkbox, Quit/Help strip, account/PW form chrome |
| `data/ui/loginwindow.dds` | Option/tab buttons, server name-strip buttons, decorative spinners/dots, server pager spinners, EULA panel background, intro image backdrop |
| `data/ui/loginwindow_02.dds` | Intro / EULA panning banner slide strips + the version/build strip |
| `data/ui/InventWindow.dds` | Quit-confirm modal chrome and buttons; exit/error modal composites (shared 340 × 190 chrome at src 318, 647) |

> **Correction:** the OK/Login button, Server-list button, ID/PW textboxes, and Save-ID checkbox
> are bound to **`login_slice1.dds`**, not `loginwindow.dds`. An earlier version of this spec
> attributed them to `loginwindow.dds` in error.

**Front-form widget table (CODE-CONFIRMED):**

| Site | Type | dst (x,y,w,h) | NORMAL (srcX,srcY) | HOVER (srcX,srcY) | PRESSED (srcX,srcY) | Atlas | actionId / role |
|---|---|---|---|---|---|---|---|
| Intro banner | BTN7 | (—,97,202,372)¹ | (9,6) | (220,6) | (9,6) | loginwindow_02.dds | panning banner strip |
| Quit/Help strip | BTN7 | (456,**−3**,111,38) | (792,398) | (602,416) | (792,398) | login_slice1.dds | **act 105** |
| Quit-confirm "Yes" #1 | BTN7 | (120,136,113,40) | (302,900) | (415,900) | (302,900) | InventWindow.dds | **act 113** |
| Quit-confirm "Yes" #2 | BTN7 | (120,136,113,40) | (302,860) | (415,860) | (302,860) | InventWindow.dds | **act 114** |
| **Server-list button** | BTN7 | (456,166,112,39) | (154,398) | (378,398) | (154,398) | login_slice1.dds | **act 102** |
| **ID/account textbox** | TEXTBOX | (390,32,102,13) | (615,404) | — | — | login_slice1.dds | IME 16, maxlen 6, act 109 |
| **Password textbox** | TEXTBOX | (568,32,102,13) | (615,404) | — | — | login_slice1.dds | IME 12, maxlen 129, act 110 |
| **Save-ID checkbox** | CHECK | (694,86,13,13) | (717,398) unchecked | — | (730,398) checked | login_slice1.dds | **act 104** |
| **OK / Login button** | BTN7 | (456,64,112,39) | (266,398) | (490,398) | (266,398) | login_slice1.dds | **act 103** |
| **Option/tab button 1** | BTN7 | (40,82,110,38) | (520,492) | (635,492) | (520,492) | loginwindow.dds | **act 111** |
| **Option/tab button 2** | BTN7 | (164,82,110,38) | (750,492) | (865,492) | (750,492) | loginwindow.dds | **act 112** |
| Decorative spinner/arrow A | BTN2 | (467,86,13,10) | (483,490) | =N | =N | loginwindow.dds | decorative (EULA scroll) |
| Decorative spinner/arrow B | BTN2 | (467,455,13,10) | (505,490) | =N | =N | loginwindow.dds | decorative (EULA scroll) |
| Decorative dot | BTN2 | (469,98,9,9) | (496,490) | =N | =N | loginwindow.dds | decorative (EULA grip) |
| **Server name-strip** | BTN7 | (13+47·i,66,47,18) | (596,985) | (643,985) | (596,985) | loginwindow.dds | **acts 115…** (generator, see §8.4) |
| Banner/title label | LABEL | (50,100,383,50) | — | — | — | — | text from msg.xdb |
| EULA notice label A | LABEL | (—,390,174,21)² | — | — | — | — | EULA/notice |
| EULA notice label B | LABEL | (—,410,174,20)² | — | — | — | — | EULA/notice |
| EULA notice label C | LABEL | (—,430,174,20)² | — | — | — | — | EULA/notice |
| Quit-confirm prompt #1 | LABEL | (10,100,330,20) | — | — | — | — | msg 4023 |
| Quit-confirm prompt #2 | LABEL | (10,100,330,20) | — | — | — | — | msg 4024 |

¹ x is register-fed (centre-computed); resolves to a fixed value on the 1024 × 768 reference canvas.
² x is the EULA-panel-relative computed position.

> **Frame-order note.** The BTN7 rows above are written in the front-end builder's
> **NORMAL, PRESSED, HOVER** argument order (§1.5). On these front-form buttons PRESSED equals
> NORMAL, so the only distinct extra frame is HOVER (the middle column above). The intro-banner and
> server-name-strip generators likewise carry only NORMAL + HOVER distinct.

**Login action-id / ASCII-char map (CODE-CONFIRMED):**

| actionId | Widget | Behaviour |
|---|---|---|
| 102 (`f`) | Server-list button | reveal server-list panel |
| 103 (`g`) | OK/Login button | version gate → credential-validation sub-state |
| 104 (`h`) | Save-ID checkbox | persist/clear saved account id |
| 105 (`i`) | Quit/Help strip | throttled re-fetch |
| 109 (`m`) | Focus ID box | clear PW focus, focus ID |
| 110 (`n`) | Focus PW box | clear ID focus, focus PW |
| 111 (`o`) | Option/tab 1 | option page select / intro reveal |
| 112 (`p`) | Option/tab 2 | option page toggle / reveal list |
| 113 (`q`) | Quit-confirm Yes #1 | hide popup, restart server-list fetch |
| 114 (`r`) | Quit-confirm Yes #2 | same |
| 115…124 | Server pager / name-strip set | server entry select + paging (see §8.4 / §8.4.1) |

The two intro-banner pager buttons additionally carry their own EULA/intro banner-pager action ids
(distinct from the 102–124 front-form range); they advance the panning banner panes.

### 8.2 Character-select screen — `BuildUI` (CODE-CONFIRMED)

> **Widget-object count (CODE-CONFIRMED, corrected).** The character-select build path constructs
> roughly **124 widget objects** (the full enumeration of the select-window chrome builder), not the
> "~77" figure an earlier version of this spec cited. The extra mass over the ~77 front-of-screen
> roles is the per-cell stat-grid icon strips and the appearance stepper grid. The load-bearing
> structural rectangles (tabs, slot plates, stat grid, Create/Delete/Enter, the create-form, the
> confirm modals) are tabulated below; the per-cell decorative strips are summarised, not row-listed.

**Atlas assignment (CODE-CONFIRMED — the chrome composites from several shared atlases):**

| Atlas DDS | Used for |
|---|---|
| `data/ui/loginwindow.dds` | **Primary** chrome: backgrounds, slot-row plates, stat-grid icons, appearance ± steppers, tab buttons, Create/Delete |
| `data/ui/mainwindow.dds` | Several slot-row action buttons + create-form widgets (steppers, confirm/cancel, name title) |
| `data/ui/InventWindow.dds` | Detail / confirm sub-panels (647 × 340 dialog frames) shared with login |
| `data/ui/CarrierPigeonPerson.dds` | Appearance selector accents; gender/class preview swatches |
| `data/ui/CarrierPigeonAll.dds`, `data/ui/tradekeepwindow.dds`, `data/ui/blacksheet.dds` | Small create-form accents; a 23 × 23 corner close button (blacksheet) |

> **§8.2 atlas correction.** The character-select chrome is **not** a single dedicated atlas. It
> composites from at least three primary shared UI atlases (`loginwindow.dds` dominant, plus
> `mainwindow.dds` and `InventWindow.dds`) and four small accent atlases. Each widget binds a source
> sub-rect of its atlas via `(srcX, srcY)` with the slice size equal to the widget's `w × h` (no
> scaling).

**Structural recovery — key rectangles (CODE-CONFIRMED):**

| Region | x | y | w | h | Atlas note | Confidence |
|---|---|---|---|---|---|---|
| Top title bar panel | 0 | 0 | 577 | 58 | `mainwindow.dds` | CODE-CONFIRMED |
| Left character-info panel | 0 | 0 | 244 | 187 | `mainwindow.dds` | CODE-CONFIRMED |
| Server tab button | 67 | 17 | 113 | 40 | `loginwindow.dds` 3-state | CODE-CONFIRMED |
| Channel tab button | 232 | 7 | 113 | 40 | `loginwindow.dds` 3-state | CODE-CONFIRMED |
| Back tab button | 393 | 17 | 113 | 40 | `loginwindow.dds` 3-state | CODE-CONFIRMED |
| **Per-slot stat-icon grid col 1** | 154 | **191** (base), stride **24** | 24 | 16 | `loginwindow.dds` | CODE-CONFIRMED |
| **Per-slot stat-icon grid col 2** | 178 | 191 (base), stride 24 | 24 | 16 | `loginwindow.dds` | CODE-CONFIRMED |
| Per-slot stat value labels | 51 | 193 (base), stride 24 | 35 | 12 | text-only | CODE-CONFIRMED |
| **Create button** | 42 | 325 | 59 | 20 | `loginwindow.dds` | CODE-CONFIRMED |
| **Delete button** | 112 | 325 | 59 | 20 | `loginwindow.dds` | CODE-CONFIRMED |
| **Enter/select button** | 112 | 112 | 59 | 20 | `loginwindow.dds` | CODE-CONFIRMED |
| Confirm modal (Yes/No) | 55 / 174 | 136 | 113 | 40 | `InventWindow.dds` | CODE-CONFIRMED |
| Name-entry textbox | 60 | 80 | 274 | 18 | `GUTextbox`; CP949 character name | CODE-CONFIRMED |
| Name-entry OK | 55 | — | 113 | 40 | `InventWindow.dds` | CODE-CONFIRMED |
| Name-entry Cancel | 174 | — | 113 | 40 | `InventWindow.dds` | CODE-CONFIRMED |
| Corner close | 610 | 23 | 23 | 23 | `blacksheet.dds` | CODE-CONFIRMED |
| Detail / confirm dialog frames | 318 | 190 | 647 | 340 | `InventWindow.dds` (×5 near-identical panels) | CODE-CONFIRMED |

**Stat grid (CODE-CONFIRMED).** The left-column character-info stat grid is a fixed block of
icon strips + value labels populated from table lookups: stat-icon buttons in two columns (col 1
x = 154, col 2 x = 178) at base-Y **191** stride **24**, with paired value labels at x = 51 base-Y
**193** stride **24**. An appearance-string loader fills the ~18 label cells per appearance branch.

**Create-form layout (CODE-CONFIRMED).** The new-character create sub-form is a self-contained
panel tree shown/hidden as a unit by the scene-reset path:
- **Four class buttons** (3-state, 19 × 30, y = 45, on `loginwindow.dds`) selecting the playable
  class. The four buttons emit class-button command ids 10, 11, 12, 13 (left-to-right) into an
  internal class selector; selecting a class rebuilds the 3D preview actor with that class's
  starter gear and BGM, and sets the class-name caption (msg.xdb 14003–14007).
- **Name input** — a `GUTextbox` (with an underlay image) for the CP949 character name.
- **Appearance face ± steppers** — a small +/- pair driving a face/appearance index (range 1–7),
  plus a point-buy stepper grid for the create stats. Cycling the face index updates only the 2D
  portrait/labels — it does **not** rebuild the 3D preview actor.
- **Confirm / Cancel** buttons (on `mainwindow.dds`). Confirm validates the name client-side (min
  length 2; lowercase ASCII + digits + CP949 Korean only; banned-word filter; no client duplicate
  check) and, on success, sends the create-character request; Cancel returns to the slot view via
  the scene-reset path.

**Char-select action-id map (CODE-CONFIRMED — corrected from prior notes):**

| actionId | Widget | Note |
|---|---|---|
| 1 | Server tab | — |
| 2 | Channel tab | — |
| 3 | Back tab | — |
| **4** | **Create button** | older notes showed 413 — that was the HOVER src-X, not the action id |
| **5** | **Delete button** | older notes showed 531 — that was the HOVER src-X, not the action id |
| **6** | **Enter/select button** | — |
| 10 / 11 / 12 / 13 | Create-form class buttons (4) | class selector 0..3 left-to-right |
| 21 / 22 | Face increment ± | 2D portrait only; no 3D rebuild |
| 25…34 | Create stat point-buy ± | per-stat increment/decrement with class floor |
| 35 / 36 | Create-form Confirm / Cancel | Confirm validates + sends; Cancel returns |
| 54 / 55, 59 / 60 | Name-entry OK / Cancel pairs | — |
| 61…74 | Per-slot / stat-grid actions | selection and stat-grid interactive buttons |

> **Correction.** The values 413 and 531 that appeared in earlier versions of this spec are the
> **atlas src-X coordinates** of the Create-button HOVER frame and the Delete-button HOVER frame,
> not action ids. The actual ids are **Create = 4, Delete = 5, Enter = 6** (CODE-CONFIRMED).

**Slot hit-test is a 3D ray-pick — NOT 2D rects (CODE-CONFIRMED).** Selecting one of the five
character preview actors is a **3D camera-unproject ray test against a per-slot axis-aligned
bounding box**, not a 2D rectangle hit-test and not a viewport-column partition. The click pixel is
unprojected through the perspective camera into a world ray; that ray is intersected (3-axis slab
test) against one small box per preview slot. Each box is centered on its character's world X/Z
(half-extent ±6 in X and Z), with a fixed vertical band (Y 70…92); because the five slots are 12
units apart in X and each box is ±6 wide, the boxes tile edge-to-edge with no gap/overlap. The first
slot whose box the ray hits becomes the selection. See `specs/frontend_scenes.md §3.3.3` for the
camera/ray geometry and the five stage positions; this refines any earlier "2D rect" assumption.

### 8.3 Shared panel chrome — `InventWindow.dds` modal

Both the login quit-confirm popup and the character-select confirm/delete popups use the **same
340 × 190 chrome at source `(318, 647)`** from `data/ui/inventwindow.dds`. The inventory window
chrome is a shared atlas reused across all three screens (login, select, and in-game inventory).

### 8.4 Generator patterns for looped / register-fed widgets

Several widgets are built in a loop or with a centre-computed X so the construction site has no
literal for those coordinates. The recovered generator rules:

| Widget group | Rule |
|---|---|
| Login server name-strip / pager set | Base (13,66,47,18); NORMAL src (596,985), HOVER (643,985); X advances **+47** per entry (`x = 13 + 47·i`); each registers action **115 + i** (see §8.4.1 for the confirmed live range) |
| Char-select stat-icon grid | Base-Y **191**, stride **24**, 5 rows; col 1 at x=154, col 2 at x=178 |
| Char-select stat value labels | Base-Y **193**, stride **24**, 5 rows; x=51, w=35, h=12 |
| Char-select appearance selector | Base y=30, w=45, h=19; NORMAL src-X base **590** stepping **+45** (590/635/680/725); HOVER base **815** (+45) |

#### 8.4.1 Login server-strip / pager range (CODE-CONFIRMED)

The server-strip generator builds the strip buttons from the same base and stride, each registering
action `115 + i`. The **confirmed live action range is 115…124 inclusive (10 entries)** — this is
the contiguous pager/strip family the server-select sub-state handles (it supersedes an earlier
"115–119, 5 entries" reading). On the server-select sub-state, the two server **plates** carry
distinct selection action ids (the left and right server records of the current page), and the
115…124 buttons are the **pager** controls: clicking a pager re-pages the two-plate view (page index
= action − 115) without committing a selection. Plate select commits a server, persists the selected
server id, and advances the flow. The plate/pager record-resolution and the server-record layout are
the server-select sub-window's concern — see §11.4.1.

### 8.5 In-game windows — `uitex.txt` integer texture binding (CODE-CONFIRMED)

In-game panels are not constructed at scene start. Each panel has a "already built?" guard field;
the first time the player opens the panel, the build routine runs and the guard is set.

**Atlas binding model differs from login/select:** every in-game window builder binds atlas by an
**integer `uitex.txt` texture-id**, not by a DDS string. The per-widget atlas DDS name must be
resolved by joining against `formats/ui_manifests.md` (the `uitex.txt` id→DDS table). The
destination rectangles and 4-frame src origins are recoverable from the binary; only the DDS name
is gated on the VFS manifest.

**Inventory of in-game builders (117 total, ≥4 ctor sites, CODE-CONFIRMED from a full builder
scan):**

| Builder category | Count | Ctor sites (BTN7/LABEL/etc.) | Identification evidence |
|---|---|---|---|
| Large data window (skill/quest/guild class) | 1 | 61 (44 BTN7, 17 LABEL) | heavy msg.xdb usage |
| Web-link window (cash-shop/billing/help) | 1 | 50 (23 BTN7, 24 LABEL, 1 TEXTBOX) | refs `"iexplorer.exe"`, `AssocQueryStringA` |
| **Guild window** | 1 | 43 (26 BTN7, 16 LABEL, 1 TEXTBOX) | key `CHAR_GUILDCAP_ENABLE` |
| **Character-info / Stat window (StatusPanel)** | 1 | 37 (13 BTN7, 24 LABEL) | binds uitex 2 + 4; `five.tga` star-rating strip loaded standalone (corrected 2026-06-13: this 37-ctor builder is the **StatusPanel** char-info/stat window — earlier labelled "dice/gamble panel" by `five.tga` presence; the `five.tga` strip is the five-star rating glyph icon, not the window chrome — see §8.7) |
| **Quest window** | 1 | 35 (22 BTN7, 1 CHECK, 12 LABEL) | key `CHAR_QUEST_TRACKING` |
| **Emotion/expression panel** | 1 | 28 (22 BTN7, 3 BTN2, 3 LABEL) | atlas `data/ui/face/anger/ani_000.dds` |
| **Options — Character sub-panel (OptionPanel_Character)** | 1 | 26 (12 CHECK, 12 LABEL, 2 BTN7) | 12 checkboxes; binds uitex 1 + 9 (corrected 2026-06-13: this 26-ctor builder is specifically the **Character** sub-tab of the Options window, hosted by the 4-tab `OptionPanel` container — see §8.9) |
| Toolbar/hotbar | 1 | 25 (19 BTN7, 6 BTN2) | many 2-state buttons |
| **Friend window (FriendPanel)** | 1 | 24 (21 BTN7, 1 LABEL, 2 TEXTBOX) | binds uitex 2 (`inventwindow.dds`); 2 textboxes (corrected 2026-06-13: this 24-ctor, 2-textbox builder is the **FriendPanel** friend-list window, **not** a "trade/mail/search input window" — the two textboxes are the friend-name add/search fields) |
| **Chat window** | 1 | 22 (15 BTN7, 7 CHECK) | keys `CHAT_WINDOW_POS_X/Y/SIZE/FONT_SIZE` |
| **Tender/auction window** | 1 | 21 (2 BTN7, 19 LABEL) | atlas `tender_window.dds` |
| Options sub-page | 1 | 21 (7 CHECK, 10 LABEL, 4 BTN7) | many checkboxes |
| **Skill window (SkillPanel)** | 1 | 15 | binds uitex 3 (`skill_window_1.dds`) + 2 + 1 + 11 + 14; skill-icon atlas `data/ui/skillicon/musajung.dds` loaded standalone (corrected 2026-06-13: this 15-ctor builder is the **SkillPanel** skill window — earlier labelled "Musajung (NPC/duel) window" because it loads `musajung.dds`; that DDS is the skill-icon atlas, not a duel-window chrome — see §8.8) |
| **Password entry window** | 1 | 5 | atlas `password.dds`, `InventWindow.dds` |
| Additional windows (unlabelled) | ~103 | varies | — |

The **identified windows** are: chat, guild, quest, options (the 4-tab container + its sub-panels),
web-link, password, emotion, tender, toolbar/hotbar, the friend window, the large data window, the
character-info/stat window, plus the skill and inventory windows. The remaining ~101 builders are
inventoried by ctor count and are DDS-attribution-gated on the VFS manifest (Open item 6).

> **Corrected 2026-06-13.** An earlier version of this list named "dice/gamble", "musajung", and
> "trade/mail input" as three distinct identified windows. Reading the builders directly (via vtable
> slot 14 = `BuildScene` for each window class) reclassified all three: the 37-ctor `five.tga`
> builder is the **character-info/stat window** (StatusPanel, §8.7), the 15-ctor `musajung.dds`
> builder is the **skill window** (SkillPanel, §8.8), and the 24-ctor 2-textbox builder is the
> **friend window** (FriendPanel). `five.tga` and `musajung.dds` are *icon* atlases loaded standalone
> by those windows, not duel/gamble chrome.

The **chat window** is the **only** widget whose initial size and position come from a data file
rather than hardcoded literals: it reads `CHAT_WINDOW_POS_X`, `CHAT_WINDOW_POS_Y`,
`CHAT_WINDOW_SIZE`, `CHAT_WINDOW_FONT_SIZE` from `data/script/uiconfig.lua`.

### 8.6 Movable-panel position persistence (in-game only)

Login and character-select panel positions are entirely hardcoded. In-game movable panels start at
their hardcoded default, but the player's dragged position is saved and restored via a per-user INI:

- **App-name key format:** `"%d_%s_PANELPOS"` where `%d` is the billing-state index and `%s` is a
  local player descriptor. This key scopes the position record to the character.
- **Per panel, indices 0..8:** keys `PANEL_%d_X` and `PANEL_%d_Y`.
- **Extra keys:** `LINK_VERTICAL` (toolbar orientation), `MENU_OPEN` (initial open/closed state).
- **Reset condition:** if a saved coordinate is −1, or if `SCREEN_WIDTH` / `SCREEEN_HEIGTH` (note:
  the legacy key name contains a typo — two E's) differ from the stored value, the panel resets to
  its hardcoded default position.

### 8.6.1 In-game chrome atlas binding — `uitex.txt` integer-id key table (CODE-CONFIRMED)

Unlike the login and character-select builders (§8.1–§8.2), which bind atlas sheets by a literal
DDS path string, **every in-game window builder binds its chrome by an integer `uitex.txt`
texture-id**. The build routine fetches a texture handle through the `uitex.txt` registry lookup
keyed by that integer id; the source rectangles (`srcX, srcY, w, h`) it then issues are pixels
within whichever DDS that id resolves to. So an in-game source rect is only fully resolved once the
integer id is joined against the `uitex.txt` id→DDS manifest.

This table is the **key** that un-gates the in-game window rects recovered in §8.7–§8.10. The
authoritative id→DDS dimension/format table lives in `formats/ui_manifests.md` (the manifest
author owns it); the subset below is the binding used by the windows specified in this section.
Confidence is CODE-CONFIRMED for the integer-id binding (read directly from the builders);
the DDS-name resolution is per the committed `ui_manifests.md` manifest.

| `uitex.txt` id | Resolved DDS (`formats/ui_manifests.md`) | In-game windows that bind it (this spec) |
|---|---|---|
| 1 | `data/ui/mainwindow.dds` | Options Character sub-panel chrome + checkbox glyphs (§8.9); Skill-window close button (§8.8); Status-window apply button (§8.7) |
| 2 | `data/ui/inventwindow.dds` | Status-window base panel + apply button (§8.7); inventory slot grid (per-instance, §8.10); inventory sort menu (§8.10); Skill-window apply button (§8.8) |
| 3 | `data/ui/skill_window_1.dds` | Skill-window main backdrop + class/tab buttons (§8.8) |
| 4 | `data/ui/tradekeepwindow.dds` | Status-window stat-row sprites + stat +/- buttons (§8.7) |
| 9 | `data/ui/messagewindow.dds` | Options 4-tab container tab buttons + Options Apply/Close buttons (§8.9) |
| 11 | `data/ui/skillpipe_02.dds` | Skill-window hotbar / skill-pipe assignment rows (§8.8) |
| 14 | (live 3D-canvas render texture) | Skill-window `GUCanvas3D` preview rects (§8.8) |

> To resolve any in-game source rect below: read its `(srcX, srcY, w, h)` and its `uitex` id from
> §8.7–§8.10, then look the id up in this table for the DDS. The DDS *dimensions* (mip-0 surface
> size, pixel format) are not duplicated here — see `formats/ui_manifests.md`.

### 8.7 In-game Character-info / Stat window — `StatusPanel` builder (CODE-CONFIRMED)

The character-info / stat window (the panel showing the player's primary attributes and the stat
+/- distribution controls). Its builder is the `StatusPanel` window class's `BuildScene` (vtable
slot 14).

> **Corrected 2026-06-13.** This window was previously catalogued in §8.5 as a "dice/gamble panel"
> on the strength of its standalone `five.tga` load. That was a mis-identification: `five.tga` is the
> **five-star rating glyph strip** drawn over the panel, not gamble chrome. The builder is the
> character-info/stat window (`StatusPanel`).

**Chrome atlas binding (corrected 2026-06-13):** the window chrome is composed from
**`inventwindow.dds` (uitex 2)** (panel base) and **`tradekeepwindow.dds` (uitex 4)**
(stat-row sprites and the +/- glyphs), **not** from `characwindow.dds`. The builder does not bind
`characwindow.dds` at all. (An earlier asset hypothesis attributed this window's chrome to
`characwindow.dds`; that file is not bound here — it is plausibly an unused/legacy asset or belongs
to a different other-player info popup. See Open item 14.) The apply button alone is on
`mainwindow.dds` (uitex 1). The `five.tga` star strip is loaded standalone, not via `uitex.txt`.

**Decorative base sprites (no action):**

| dst (x, y, w, h) | src (srcX, srcY) | Atlas (uitex → DDS) |
|---|---|---|
| 0, 36, 318, 50 | 0, 683 | 2 → `inventwindow.dds` |
| 0, 85, 318, 625 | 634, 0 | 4 → `tradekeepwindow.dds` |
| 114, 60, 93, 17 | 194, 704 | 4 → `tradekeepwindow.dds` |
| 87, 460, 224, 17 | 400, 627 | 4 → `tradekeepwindow.dds` (divider strip) |

**Stat-row value labels** are text-only (atlas id 0; the value text is filled at runtime from
network character data). They sit on the 318-wide panel at fixed positions in two columns
(left column x ≈ 91–94, right column x ≈ 247–249), most 12 × 12, stepping down the panel; the lower
description rows are wider (e.g. 226 × 12 at y 528 / 555). One label carries a red colour override
(`0xFFFF0000`). A looped block of four description rows starts at (74, 555) with a **+27** y-stride.
Exact per-label positions are recoverable from the builder; they are not load-bearing for behaviour
and are summarised rather than tabulated row-by-row here.

**Stat +/- buttons and action ids (the load-bearing interactive table)** — all 7-state buttons;
the +/- pairs are on `tradekeepwindow.dds` (uitex 4) at 9 × 9, the apply button is on
`inventwindow.dds` (uitex 2), the close button is on `mainwindow.dds` (uitex 1):

| dst (x, y, w, h) | NORMAL (srcX, srcY) | PRESSED (srcX, srcY) | Atlas (uitex → DDS) | Action id | Role |
|---|---|---|---|---|---|
| 286, 46, 29, 26 | 354, 596 | 354, 622 | 1 → `mainwindow.dds` | **312** | Close |
| 150, 210, 9, 9 | 296, 702 | 287, 702 | 4 → `tradekeepwindow.dds` | **300** | increment (+) |
| 301, 210, 9, 9 | 296, 702 | 287, 702 | 4 → `tradekeepwindow.dds` | **301** | increment (+) |
| 150, 237, 9, 9 | 296, 702 | 287, 702 | 4 → `tradekeepwindow.dds` | **302** | increment (+) |
| 301, 237, 9, 9 | 296, 702 | 287, 702 | 4 → `tradekeepwindow.dds` | **303** | increment (+) |
| 150, 264, 9, 9 | 296, 702 | 287, 702 | 4 → `tradekeepwindow.dds` | **304** | increment (+) |
| 150, 220, 9, 9 | 296, 711 | 287, 711 | 4 → `tradekeepwindow.dds` | **305** | decrement (−) |
| 301, 220, 9, 9 | 296, 711 | 287, 711 | 4 → `tradekeepwindow.dds` | **306** | decrement (−) |
| 150, 247, 9, 9 | 296, 711 | 287, 711 | 4 → `tradekeepwindow.dds` | **307** | decrement (−) |
| 301, 247, 9, 9 | 296, 711 | 287, 711 | 4 → `tradekeepwindow.dds` | **308** | decrement (−) |
| 150, 274, 9, 9 | 296, 711 | 287, 711 | 4 → `tradekeepwindow.dds` | **309** | decrement (−) |
| 271, 262, 40, 35 | 370, 630 | 208, 669 | 4 → `tradekeepwindow.dds` | **311** | stat-icon / avatar button |
| 259, 655, 59, 77 | 301, 947 | 360, 947 | 2 → `inventwindow.dds` | **310** | big **Apply** (confirm stat distribution) |

The +/- design is one increment glyph row (atlas y 702) and one decrement glyph row (atlas y 711)
on `tradekeepwindow.dds`, reused per stat line. The two action-id runs are therefore: **300–304**
= the five increment buttons, **305–309** = the five decrement buttons, **310** = big Apply,
**311** = the stat-icon/avatar button, **312** = Close. (Eleven stat +/- buttons total across the
two runs, plus Apply and Close.)

**Title:** the `StatusPanel` header is set from the shared empty-init scratch string and then filled
at runtime from network character data (player name + class). There is **no fixed `msg.xdb` title
id** for this window. The static stat *names* (as opposed to the title) come from a different draw
path and use `msg.xdb` ids in the 60005–60022 range.

### 8.8 In-game Skill window — `SkillPanel` builder (CODE-CONFIRMED)

The skill window (class/tab selector, scrollable skill list, two live 3D previews, and a
skill-pipe / hotbar assignment column). Its builder is the `SkillPanel` window class's `BuildScene`.

> **Corrected 2026-06-13.** This window was previously catalogued in §8.5 as a "Musajung (NPC/duel)
> window" because it loads `musajung.dds`. That DDS is the **skill-icon atlas**
> (`data/ui/skillicon/musajung.dds`), loaded standalone; the window itself is the skill window
> (`SkillPanel`), with its chrome on `skill_window_1.dds`.

**Atlas binding:** main chrome on **`skill_window_1.dds` (uitex 3)**; the universal close button
on **`mainwindow.dds` (uitex 1)**; the apply button on **`inventwindow.dds` (uitex 2)**; the
skill-pipe rows on **`skillpipe_02.dds` (uitex 11)**; the two 3D previews on the live 3D-canvas
texture (uitex 14). The skill-icon atlas `data/ui/skillicon/musajung.dds` is loaded standalone.

**Chrome, close, apply, and title:**

| Type | dst (x, y, w, h) | NORMAL (srcX, srcY) | PRESSED (srcX, srcY) | Atlas (uitex → DDS) | Action / role |
|---|---|---|---|---|---|
| base sprite | 0, 0, 964, 655 | 0, 0 | — | 3 → `skill_window_1.dds` | window backdrop (964 × 655) |
| 7-state | 938, 48, 29, 26 | 354, 596 | 354, 622 | 1 → `mainwindow.dds` | **812** Close (same close glyph as §8.7) |
| 7-state | 846, 60, 72, 22 | 292, 720 | 292, 742 | 3 → `skill_window_1.dds` | **814** Help / info |
| 7-state | 865, 599, 50, 77 | 301, 947 | 360, 947 | 2 → `inventwindow.dds` | **811** Apply / confirm |
| 7-state | 726, 616, 31, 16 | 0, 0 | 0, 0 | 0 (text-only button) | **813** |
| base sprite | 587, 606, 251, 34 | 212, 662 | — | 3 → `skill_window_1.dds` | footer strip |
| LABEL | 133, 618, 400, 20 | — | — | 0 (text-only) | **title caption = msg 3027** |

**Nine class/tab buttons** (7-state, y = 71, h = 21, x-stride **62**, on `skill_window_1.dds`
uitex 3), action ids **802–810**:

| dst (x, y, w, h) | NORMAL (srcX, srcY) | PRESSED (srcX, srcY) | Action id |
|---|---|---|---|
| 3, 71, 62, 21 | 0, 770 | 0, 791 | 802 |
| 65, 71, 62, 21 | 62, 770 | 62, 791 | 803 |
| 127, 71, 62, 21 | 124, 770 | 124, 791 | 804 |
| 189, 71, 63, 21 | 0, 959 | 63, 959 | 805 |
| 251, 71, 62, 21 | 187, 833 | 187, 854 | 806 |
| 313, 71, 62, 21 | 249, 833 | 249, 854 | 807 |
| 375, 71, 62, 21 | 311, 833 | 311, 854 | 808 |
| 437, 71, 62, 21 | 214, 959 | 214, 959 | 809 |
| 499, 71, 62, 21 | 222, 980 | 222, 980 | 810 |

**Other skill widgets:** nine looped sub-list containers (dst 0, 92, 964, 563 src 0, 0) host the
scrollable skill-list rows; two `GUCanvas3D` preview rects (dst 0, 0, 220, 200, uitex 14) render
the live skill-effect / character previews; a 50-step loop (dst x = 163, y stepping **+125** from
50, 242 × 65, on `skillpipe_02.dds` uitex 11 src 0, 358) builds the skill-pipe / hotbar
assignment rows.

**Title:** the skill window's title label caption is **`msg.xdb` id 3027** (CODE-CONFIRMED id; the
CP949 caption text itself is VFS-only and is not transcribed here). Adjacent records 3028 and 3029
are the skill-window level headers ("current level" / "next level"); 3027 is the adjacent window
title. The CP949 string must be supplied from a `msg.xdb` extract.

### 8.9 In-game Options window — 4-tab container + Character sub-panel (CODE-CONFIRMED)

The options window is a **tab-host container** (`OptionPanel`) presenting four tabs, each switching
to a dedicated sub-panel. The Character sub-tab (`OptionPanel_Character`) is the one fully recovered
here; the Sound and Graphic sub-panels (and a fourth tab) bind the same atlases (uitex 1 + 9) but
their widget tables were not swept (Open item 15).

> **Note (2026-06-13):** the §8.5 "Options/settings panel, 26 ctors" builder is specifically this
> **Character sub-panel**, not the tab host; the tab host is a separate builder.

**Tab container — four tab buttons** (7-state, x = 15, 186 × 40, y-stride **40**), on
**`messagewindow.dds` (uitex 9)**. HOVER equals NORMAL (caption-only hover feedback):

| dst (x, y, w, h) | NORMAL (srcX, srcY) | PRESSED (srcX, srcY) | Action id | Tab |
|---|---|---|---|---|
| 15, 30, 186, 40 | 833, 517 | 460, 916 | **0** | Character |
| 15, 70, 186, 40 | 833, 557 | 460, 876 | **1** | Sound |
| 15, 110, 186, 40 | 833, 597 | 460, 956 | **2** | Graphic |
| 15, 150, 186, 40 | 833, 637 | 646, 516 | **3** | fourth tab |

**Character sub-panel — Apply / Close** (two 7-state buttons on **`messagewindow.dds`, uitex 9**;
added as the first two children, action indices 0 and 1):

| dst (x, y, w, h) | NORMAL (srcX, srcY) | PRESSED (srcX, srcY) | child action index | Role |
|---|---|---|---|---|
| 60, 415, 186, 40 | 462, 757 | 646, 796 | **0** | Apply |
| 60, 455, 186, 40 | 462, 837 | 646, 876 | **1** | Close |

**Character sub-panel — 12 checkboxes** (24 × 24, right-aligned column at `panelWidth − 50`, base
y = **50**, y-stride **30**), on **`mainwindow.dds` (uitex 1)** — UNCHECKED src (372, 730),
CHECKED src (372, 754), action ids **2–13**:

| dst (x, y, w, h) | Action id |
|---|---|
| (panelW − 50), 50, 24, 24 | **2** |
| (panelW − 50), 80, 24, 24 | **3** |
| (panelW − 50), 110, 24, 24 | **4** |
| (panelW − 50), 140, 24, 24 | **5** |
| (panelW − 50), 170, 24, 24 | **6** |
| (panelW − 50), 200, 24, 24 | **7** |
| (panelW − 50), 230, 24, 24 | **8** |
| (panelW − 50), 260, 24, 24 | **9** |
| (panelW − 50), 290, 24, 24 | **10** |
| (panelW − 50), 320, 24, 24 | **11** |
| (panelW − 50), 350, 24, 24 | **12** |
| (panelW − 50), 380, 24, 24 | **13** |

Each checkbox is a `GUCheckBox` (checked = the PRESSED frame), so UNCHECKED uses the NORMAL origin
and CHECKED uses the PRESSED origin (per §1).

**Character sub-panel — caption labels** (text-only, x = 40, 115 × 24, base y = **55**, y-stride
**30**), captions fetched from `msg.xdb`:

| dst (x, y, w, h) | Caption `msg.xdb` id |
|---|---|
| 40, 55, 115, 24 | **8009** |
| 40, 85, 115, 24 | **8010** |
| 40, 115, 115, 24 | **8011** |
| 40, 145, 115, 24 | **8012** |
| 40, 175, 115, 24 | **8013** |
| 40, 205, 115, 24 | **8014** |
| 40, 235, 115, 24 | **8018** |
| 40, 265, 115, 24 | **8016** |
| 40, 295, 115, 24 | **8017** |
| 40, 325, 115, 24 | **8037** |
| 40, 355, 115, 24 | **8039** |
| 40, 385, 115, 24 | **8015** |

(Two further caption labels at the same x and y-stride were register-fed and not reduced to literals
here; their ids are within the same 8xxx options bank — Open item 15.) Each caption row also has a
small text-only value/status glyph to its right (drawn from `mainwindow.dds`, uitex 1, src
(140, 668)) indicating that option's current value. Two decorative header strips (no action) sit at
(109, 20, 92, 11) src (405, 868) and (55, 33, 195, 8) src (186, 1014) on uitex 1.

The full Character-tab option-label range is therefore **`msg.xdb` 8009–8039** (a subset of the
broader 8001–8047 options-string bank).

> **Atlas resolution (confirms a prior open question):** the options window has no dedicated
> `option*.dds` sheet. Its tab buttons and Apply/Close are sub-regions of **`messagewindow.dds`
> (uitex 9)**; its checkbox glyphs, value indicators, and header strips are sub-regions of
> **`mainwindow.dds` (uitex 1)**.

### 8.10 In-game Inventory window — slot grid + sort menu (CODE-CONFIRMED)

The inventory ("bag") window is built by two cooperating builders: the slot-grid panel
(`GatherSlotPanel`) and a small sort-menu panel (`InvenSortPanel`).

**Slot-grid panel.** This builder binds **texture id 0** for every widget — the slot/icon atlas is
assigned to the panel *per instance* after the build runs (the slot sprites read their frame origins
from panel fields pre-seeded to (296, 0), (296, 64), (296, 128), (296, 192) — a 4-row icon table,
plausibly resolving to `inventwindow.dds` at runtime; the exact runtime bind site was not traced —
Open item 16). Recovered geometry:

- Three slot-row buttons (65 × 64), x stepping **+84** from 32, y = 96, action ids **502, 503, 504**.
- Full-window base backdrop sprite (0, 0, panelWidth, panelHeight).
- Header / divider sprites at (21, 174, 253, 34) src (0, 372) and (33, 189, 239, 8) src (0, 407).
- Close button at (panelW − 13, 2, 11, 12) src (410, 0) / (399, 0), action **509**; minimise button
  at (panelW − 26, 2, 11, 12) src (388, 0), action **501**.
- Sort/menu button at (114, 25, 66, 19) src (0, 416), action **508**.
- A looped row of slot-category buttons (80 × 31, x stepping **+84**, y = 207, action ids 505+) with
  paired 40 × 23 labels, and two footer labels (104 × 21) at y = 344.

**Sort menu panel** (`InvenSortPanel`) binds **`inventwindow.dds` (uitex 2)** and uses caption
`msg.xdb` ids **37101** (sort-inventory / "행낭 정리"), **37107**, and **37108** (sort options).

**Title:** the inventory window builder makes **no `msg.xdb` title call**. Its window title is
therefore **baked into the `inventwindow.dds` chrome art (the panel edge region)**, not fetched as a
caption (CODE-CONFIRMED absence of a title lookup; the baked-into-art conclusion is PLAUSIBLE). The
associated label strings are the sort-menu captions (**msg 37101 / 37107 / 37108**) and the hotkey
toggle label (**msg 25017**, "show inventory window"); none of these is the window title itself.

### 8.11 In-game window title `msg.xdb` ids — summary (CODE-CONFIRMED ids)

| Window | Title source | Grade |
|---|---|---|
| Skill window (`SkillPanel`) | `msg.xdb` **3027** (title label at 133, 618, 400 × 20) | CODE-CONFIRMED (id); CP949 text VFS-only |
| Inventory window (`GatherSlotPanel`) | **no `msg.xdb` title** — baked into `inventwindow.dds` edge art; sort labels msg 37101 / 37107 / 37108, toggle msg 25017 | CODE-CONFIRMED (absence) / PLAUSIBLE (baked-in-art) |
| Character-info / Stat window (`StatusPanel`) | runtime player name (empty-init scratch buffer); static stat *names* msg 60005–60022 | CODE-CONFIRMED |
| Options window (`OptionPanel`) | tabs are sprite-only (no title caption); Character-tab option labels msg 8009–8039 | CODE-CONFIRMED |

> All `msg.xdb` ids above are CODE-CONFIRMED (read from the builders); the in-game window protocol
> behaviour is static and **CAPTURE-UNVERIFIED** (no Wireshark oracle). The CP949 caption *text* for
> each id is VFS-only and must be supplied from a `msg.xdb` extract (`formats/misc_data.md §6`).

---

## 9. Per-screen asset manifests

All paths are VFS-relative. All large UI sheet atlases are DDS with DXT3 pixel format and a
1024 × 1024 (or occasionally 512 × 512) mip-0 surface. See `formats/ui_manifests.md` for the
file-level format of `uitex.txt` and related manifests.

### 9.0 Front-end atlas loader lifecycle (CODE-CONFIRMED)

The front-end scenes (login and character-select) load their atlas sheets through one shared loader
with a deliberately simple lifecycle. An implementer should model it as **append-only,
window-owned, no shared cache**:

- **No cache, no dedup.** The loader performs **no name-keyed lookup** before creating a texture.
  Every call unconditionally reads the named DDS and creates a brand-new GPU texture. Two calls with
  the same path therefore produce **two independent textures** — there is no path-keyed map and no
  re-use across windows.
- **No ref-count.** Acquiring a texture does not increment a reference count, and releasing it does
  not decrement-and-test — each handle is released exactly once. Lifetime is tied to the **owning
  window**, not shared/ref-counted across windows.
- **Eager preload.** All of a scene's atlases are loaded **up front, in one burst at the top of the
  window's `BuildScene`**, before any widget is constructed. There is no lazy / first-draw load;
  every later widget simply references an already-loaded handle. (Login preloads four atlases:
  `login_slice1.dds`, `loginwindow.dds`, `InventWindow.dds`, `loginwindow_02.dds`; character-select
  preloads its primary + accent set per §8.2.)
- **Handles held on the window object.** Each loaded handle is appended to a **texture-handle list
  embedded in the owning window object** (the `GUTextureList` holder at window offset +0x220). The
  list is a plain append-only ownership vector — not a cache index.
- **Released when the window Ends.** On window teardown the embedded texture-handle list's
  destructor walks the list and releases each texture outright (once per handle, no ref-count
  decrement), then frees the list's backing storage. Release is **automatic on scene-exit** via the
  window's own teardown, not an explicit "End" call on the loader. The decode path (VFS find-and-read
  when the archive is mounted, else loose-disk fallback) is the generic engine texture loader, so
  this lifecycle is **shared by all UI windows**, not login-specific.

> **Godot mapping.** Load each front-end scene's atlases once at scene build, hold them on the scene
> node, and free them on scene exit. Do **not** emulate a shared texture cache or ref-counting — the
> original's model is per-window, append-only ownership.

### 9.1 Login screen

| Path | Role | Confidence |
|---|---|---|
| `data/ui/login_slice1.dds` | **Primary form atlas**: OK/Login, Server-list, ID/PW textboxes, Save-ID checkbox, Quit/Help strip | CODE-CONFIRMED |
| `data/ui/loginwindow.dds` | Option/tab buttons, server name-strip, decorative elements | CODE-CONFIRMED |
| `data/ui/loginwindow_02.dds` | Panning intro banner source | CODE-CONFIRMED |
| `data/ui/inventwindow.dds` | Shared popup / button chrome (quit-confirm modal) | CODE-CONFIRMED |

### 9.2 Character-select screen

| Path | Role | Confidence |
|---|---|---|
| `data/ui/loginwindow.dds` | Tab buttons, stat-icon grids, Create/Delete/Enter button strips | CODE-CONFIRMED |
| `data/ui/mainwindow.dds` | Slot-row action buttons + create-form widgets | CODE-CONFIRMED |
| `data/ui/inventwindow.dds` | Popup panels + buttons (confirm / delete / name-entry chrome) | CODE-CONFIRMED |
| `data/ui/blacksheet.dds` | Corner close button; dim/blackout overlay | CODE-CONFIRMED |
| `data/ui/carrierpigeonperson.dds`, `data/ui/carrierpigeonall.dds`, `data/ui/tradekeepwindow.dds` | Appearance selector ±, gender/class preview swatches, create-form accents | CODE-CONFIRMED |
| (GUCanvas3D live 3D viewports) | Character previews; not a 2D atlas | CODE-CONFIRMED |

### 9.3 Opening cinematic (state 3) screen

| Path | Role | Confidence |
|---|---|---|
| `data/ui/openning_001.dds` through `openning_004.dds` | Cinematic frames 1–4 (768 × 1024) | SAMPLE-VERIFIED |
| `data/ui/openning_scenario.dds` | Large intro splash (2048 × 1024) | SAMPLE-VERIFIED |

### 9.4 Loading (state 2) screen

| Path | Role | Confidence |
|---|---|---|
| `data/ui/loading.dds` | Generic loading screen | SAMPLE-VERIFIED |
| `data/ui/loading01.dds` through `loading08.dds` | Area-specific loading screens (8 variants) | SAMPLE-VERIFIED |
| `data/ui/loadingbar.dds` | Progress bar texture (256 × 256) | SAMPLE-VERIFIED |

### 9.5 In-game — confirmed atlas set (selected; not exhaustive)

Window chrome: `mainwindow.dds`, `inventwindow.dds`, `skillwindow.dds`, `messagewindow.dds`,
`tradekeepwindow.dds`, `buywindow.dds`, `itemshop.dds`, `itemshoppopup.dds`, `product.dds`,
`productnpc.dds`, `guildnpc.dds`, `confessionnpc.dds`, `guildcreate.dds`, `guildnewwindow.dds`,
`guildmemberposition.dds`, `relation.dds`, `letter.dds`, `delivery.dds`, `carrierpigeon.dds`,
`password.dds`, `tender_window.dds`, `fame_buff_window.dds`, `stalllist.dds`, `mediate.dds`,
`publicpeace.dds`, `cubegamble.dds`, `cubegamble_ani.dds`, `cubegamble_help.dds`.

Icons / HUD: `skillicon/stateicon.dds`, `slotboard.dds`, `gage.dds`, `combo.dds`,
`battleboard.dds`, `blacksheet.dds`, `no_pk_penalty.dds`, `autopenalty.dds`.

Minimap: `map/map%d.dds` (one sheet found: `map/map1.dds`, 512 × 512); `map_userpoint.tga`
(player dot, 64 × 64 TGA).

Animated frame sequences (loaded as numbered series, not sprite sheets):
- `ui/mode/attackmode-01` through `attackmode-18.tga`, `peacemode-01` through `peacemode-18.tga`
  (18 frames each, 64 × 128, 32-bit TGA)
- `ui/face/anger/ani_000` through `ani_015.dds` (16 animation frames)
- `ui/face/fire/face_fire_NN.tga` (18 frames, 64 × 64 TGA)
- `ui/dice/jusa_001` through `jusa_021.dds` (21 frames, 128 × 128)
- `ui/diceresult/jusai_win_001` through `jusai_win_022.dds` (22 frames)
- `ui/upgrade/weap_madeNN.dds` (28 frames, 128 × 128)

Solid-colour fill patches: `p_green.dds`, `p_red.dds`, `p_white.dds`, `p_blue.dds`,
`p_darkblue.tga`, `p_black.tga`, `p_orange.tga`, `p_purple.tga`, `p_yellow.tga`, `green.dds`,
`red.dds`, `white.dds`, `blue.dds`, `yellow.dds`, `blacksheet.dds`, `edge.dds`,
`inactivemember.dds`.

> **Texture format note:** the loaders pass the FourCC constant `DXT2` as a load hint for DDS
> atlases. Actual pixel format on all sampled large sheets is DXT3 (see `formats/ui_manifests.md`).
> Some HUD sprites are TGA. Cursor assets live under `data/cursor/` and are rendered as textured
> quads, not registered as Windows cursor resources. There is no `.cur` or `.ani` file in the VFS.

---

## 10. String database — msg.xdb

All visible UI captions are fetched by numeric ID from `data/script/msg.xdb` (loaded at the start
of state 1, alongside the font table). The lookup helper accepts an integer message ID and returns
the CP949-encoded string.

Known ID ranges in use:
- `9001 + stateIndex` — scene/state name strings (used by the error dialog)
- `4001–4022` — login form static label captions
- `4023–4024` — login quit-confirm prompts
- `4025–4028` (and likely `4029`) — login error toast messages
- `2204` — version-mismatch Win32 error box
- `101` — timed-popup countdown suffix
- `5001–5040` (+ locale banks) — localized server names
- `14003–14007` — character class labels (create form)
- `0xC8`–`0xD4` (200–212 decimal) — character create / rename error messages

> **Format:** `msg.xdb` is a flat binary array of 516-byte records: `u32 id` + `u8[512]` CP949
> NUL-terminated string. Record count = `file_size / 516`. The file has no header and no magic.
> Records are inserted into a runtime red-black tree keyed on `id`. The format is
> CODE-CONFIRMED; record content is SAMPLE-UNVERIFIED (VFS probe was unavailable at analysis time).
> Full specification: `Docs/RE/formats/misc_data.md §6`.

---

## 11. Master scene state machine

### 11.1 State variable

A global array of small integers acts as the scene state. The relevant slots:

| Array index | Role | Confidence |
|---|---|---|
| [0] | Primary scene state (the switch selector) | CODE-CONFIRMED |
| [1] | Secondary state / error sub-code (which init step failed) | CODE-CONFIRMED |
| [2] | Error message ID (used by the error dialog path) | CODE-CONFIRMED |
| [+12] (byte offset) | Debug-mode flag (from Lua `debugmode` key) | CODE-CONFIRMED |

> The login window's own internal sub-state machine (Section 11.3) is stored in a **separate** field
> on the login window object, not in this global array. The two must not be confused.

### 11.2 The 9 primary states

The main loop runs a switch on `state[0]`. Each scene object is allocated, ticked by the engine's
main loop until done, then ended and destroyed before the next scene is created.

| State | Name | Created / ticked / destroyed | Transition target | Confidence |
|---|---|---|---|---|
| 0 | Init | Reads screen resolution (or 1920-clamped fullscreen when config = 2); sets back-buffer and 16-bit depth | → 1 | CODE-CONFIRMED |
| 1 | **Login** | Loads `data/script/msg.xdb`; allocates the login window (size 0x558); registers it as an event target; creates the 15 font objects; runs the engine main loop; on completion ends, unregisters, and destroys the window | → 2 on success; → 7 on window/config failure (sets `[1]` = 1 or 3) | CODE-CONFIRMED |
| 2 | Load / opening gate | Allocates a load handler (size 0x218); runs the loading screen; reads INI key `[OPENNING] SKIP` to decide whether to skip the cinematic | → 4 when `SKIP` is set; → 3 otherwise | CODE-CONFIRMED |
| 3 | Opening cinematic | Allocates the opening window (size 0x2D0); plays the `openning_*.dds` intro sequence; ends and destroys | → 4 | CODE-CONFIRMED |
| 4 | **Character select** | Allocates the select window (size 0x1888); registers it as an event target; runs the engine main loop; on completion ends, unregisters, and destroys | → 5 on enter-game | CODE-CONFIRMED |
| 5 | **In-game** | Allocates the main handler (size 0xC8); builds the game world; registers three event targets (main handler, sub-handler, view); runs the engine main loop; on logout/return ends and destroys | **→ 4** (returns to character select, NOT to login) | CODE-CONFIRMED |
| 6 | Quit | Tears down the engine | → 8 | CODE-CONFIRMED |
| 7 | Error | Builds an error string from the state-name table (msg ID = 9001 + `[1]`), shows a message box, closes the network connection | → 8 | CODE-CONFIRMED |
| 8 | Exit | Engine shutdown; WinMain returns | (loop ends) | CODE-CONFIRMED |

> **Key non-obvious edge:** in-game (state 5) transitions **back to character select (state 4)**,
> not to login. Login is only visited once per process lifetime. The load / opening gate (state 2)
> is also visited only once, after the first successful login.

### 11.3 Login window internal sub-state machine (CODE-CONFIRMED, corrected)

Stored at offset `+0x238` on the login window object. This drives the lobby discovery and
credential flow described in `specs/login_flow.md` and `specs/frontend_scenes.md`. The machine spans
sub-state values 1..6 and 29..41 (with gaps); the field is initialised to 1.

| Sub-state | Meaning | Confidence |
|---|---|---|
| 1 | Intro start — play login-enter SFX **861010105**; seed curtain (upper panel Y = 0, lower panel Y = 326, reset slide accumulator) | CODE-CONFIRMED |
| 2 | Curtain open slide — accumulator +5/tick; upper Y = −acc, lower Y = acc + 326; reveal server list past 200; complete past 222 (see §7.7) | CODE-CONFIRMED |
| 3, 4, 5 | Settle the curtain and reveal the login form widgets | CODE-CONFIRMED |
| 6 | **Login form active** — waiting for user input | CODE-CONFIRMED |
| **29** | **OK-button credential validation** — checks ID len ≥ 4 (else msg **4025** → 6); PW len ≥ 1 (else msg **4026** → 6); persist Save-ID; advance to 31. **Correction: this was incorrectly labelled "server-list trigger" in an earlier version of this spec.** | CODE-CONFIRMED |
| 30 | Quit-confirm "Yes" path — writes engine state **6 / substate 8** (quit) | CODE-CONFIRMED |
| **31** | **Raise the second-password (PIN) modal** — shows the PIN keypad child, hides the login buttons; advances toward 32. **Correction: this was previously labelled "Help screen" and later "EULA/terms accept"; this build raises the PIN/second-password keypad child here, with no separate EULA-accept flag. The Help button is the separate control at action id 105 (`i`), not a sub-state.** | CODE-CONFIRMED |
| 32 | Poll the PIN modal — keypad visible AND submitted/confirmed → advance to 33 | CODE-CONFIRMED |
| 33 | Press-OK transition → begin server-list fetch (set 34) | CODE-CONFIRMED |
| 34 | Start server-list fetch thread (port 10000) | CODE-CONFIRMED |
| 35 | Wait for server-list reply | CODE-CONFIRMED |
| 36 | Consume server list: empty → msg **4027** → 6; connect-fail → msg **4028** → 6; else render + set 37 | CODE-CONFIRMED |
| 37 | Server selected (resting on the plate list) — plates and pagers are live only here | CODE-CONFIRMED |
| 38 | Start channel-endpoint fetch thread (port 10000 + selected offset); persist selected server id | CODE-CONFIRMED |
| 39 | Wait for endpoint reply | CODE-CONFIRMED |
| 40 | Consume endpoint; build TAB-delimited credential string; rebuild secure context; hand off to game connection | CODE-CONFIRMED |
| 41 | Transition complete — login window exits | CODE-CONFIRMED |

> **Note on the corrected literal indices.** The server-select branch keys on literal sub-state
> values 32–38 as above; an earlier brief that quoted 34/35/36 for fetch/wait/consume was off by
> one against the live machine (fetch-start = 33, request = 34, wait = 35, consume = 36 → 37, pick =
> 37, endpoint = 38). The values above are the live ones.

### 11.4 Character-select screen lifecycle note

The character-select widget builder is not called at scene creation. It is invoked when the
`SmsgCharacterList` (opcode 3/1) packet arrives on the network — the packet handler forces the
primary scene state into the select scene and triggers the builder. This means the select screen
widget tree does not exist until the packet is received. The server character list is ingested into
five 880-byte per-slot records, and the five 3D preview actors are materialised (deferred to an
early tick frame) from those records by the scene-reset path; subsequent rebuilds run whenever the
character-manage result packet arrives.

### 11.4.1 Server-select sub-window (CODE-CONFIRMED)

The server-select surface is a **visibility sub-state of the single login window** (sub-state 37 of
§11.3), not a separate window. All its widgets are built once at login scene build; the tick toggles
them. The selectable servers are presented as **two "plates" per page** (the left and right server
records of the current page), with the **115…124 pager buttons** (§8.4.1) walking the server-record
array in two-record windows. A plate click commits its server only when the record passes the
selection guard, persists the selected server id, and advances the flow (sub-state 38); a pager click
only re-pages (no commit). The server-record array is a list of **8-byte records**:

| Offset | Size | Field | Notes | Confidence |
|---|---|---|---|---|
| +0 | 2 (u16) | server_id | valid range 1..40 (`(id − 1) ≤ 39`), else a fallback caption; compared against the persisted "last server" id for the highlight | CODE-CONFIRMED |
| +2 | 2 (i16) | status_code | drives the status label; special values 3 (open-time), 24 (preparing), 100 (current-selection sentinel); plate commit requires status_code == 0 | CODE-CONFIRMED |
| +4 | 2 (i16) | load / population | when status_code == 0, thresholded to a coloured load label | CODE-CONFIRMED |
| +6 | 2 (i16) | open_time / extra | used only for the open-time schedule presentation (status 3) | MEDIUM (static-only; CAPTURE-UNVERIFIED) |

Load thresholds (applied when status_code == 0): `load > 1200` → red label; `800 < load ≤ 1200` →
orange; `500 < load ≤ 800` → yellow; `load ≤ 500` → green. Plate commit additionally requires
`load < 2400` (a separate hard cap distinct from the colour thresholds). The threshold constants
(1200 / 800 / 500 / 2400) and the four label colours are CODE-CONFIRMED; the exact wire packing of
the +6 open-time field is static-only and CAPTURE-UNVERIFIED.

---

## 12. Known unknowns / open items

1. **Quit button Y on login: RESOLVED.** The Y coordinate is **−3** (CODE-CONFIRMED from the
   widget sweep literal dump). The earlier note "passed through a register" was an incomplete
   extraction; the sweep recovered the literal.

2. **Per-slot row base-Y on char-select: RESOLVED.** The stat-icon grid base-Y is **191**, stride
   **24**, 5 rows (CODE-CONFIRMED from the sweep). The stat value label base-Y is **193**, stride
   **24**. Both are confirmed literals, not register-fed.

3. **GUCanvas3D slot count and positions on char-select: RESOLVED.** There are **5** preview
   slots. Stage X offsets are **{−1560, −1548, −1536, −1524, −1512}** (12 units apart), Z ≈ −3593,
   scale ×3.0. Built by the select window's slot-actor builder. The preview is a live in-world
   actor (same player actor factory as in-game) — not a 2D atlas sprite. No new asset path.

4. **Per-widget font-slot index: RESOLVED for the front-end; in-game residual.** The slot is an
   instance field (`GULabel` +0xE4, `GUTextbox` +0xDC), zero-init by the ctor → default slot 0.
   The **entire front-end** (login + character-select) uses **slot 0 universally** for text (§6.3):
   the login `BuildScene` writes no slot, so every login label/caption/textbox draws with slot 0
   (DotumChe 12 / 6 / weight 0). The remaining residual is purely in-game: the exact GUButton
   caption-slot offset was not byte-pinned, and the in-game windows that use larger/bold slots
   (titles via slots 2 / 3 / 10) were not individually swept for which slot each label uses.

5. **`msg.xdb` format: RESOLVED.** The format is CODE-CONFIRMED (flat binary, 516-byte records,
   `u32 id` + `u8[512]` CP949 text, no header, count from `file_size / 516`, red-black tree at
   load). Record content is SAMPLE-UNVERIFIED pending VFS probe repair. Full spec at
   `Docs/RE/formats/misc_data.md §6`.

6. **In-game window DDS attribution (the big open debt — now partially resolved).** Almost all
   in-game builders bind atlas by integer `uitex.txt` texture-id, not DDS string. The
   character-info/stat, skill, options, and inventory windows are **now un-gated** via the
   `uitex.txt` id→DDS key table in §8.6.1 (joined against `formats/ui_manifests.md`). The remaining
   ~101 unlabelled builders still need their per-widget atlas DDS names resolved through that
   manifest; their destination rects and 4-frame src origins are recoverable from the binary now.
   See `formats/ui_manifests.md`.

7. **Exact `Font_Create` slot record stride.** The per-slot descriptor stores the font pointer,
   face name, charWidth, rowHeight, sizeFallback, and weight. The exact byte stride of the
   descriptor (estimated ≥ ~80 dwords / ~320 bytes) was not byte-confirmed.

8. **`GULabels`, `GUScroll`, `GUScrollEx` internals.** The multi-line label and the two scrollbar
   widgets have their own draw/event virtuals that were not covered in this pass.

9. **`GUCanvas3D` render-target wiring.** The mechanism by which the 3D preview widget binds a
   render target / viewport for its live scene (the D3D device viewport/scene-graph wiring) was not
   traced.

10. **Tint field +0x0C per-widget provenance.** Confirmed as the low-24-bit RGB tint in the draw
    ARGB, but where each widget's tint is set (ctor default vs per-screen override) was not swept.

11. **`ID3DXSprite::SetState` flag values.** `SetState(48)` vs `SetState(16)` — the precise
    `D3DXSPRITE_*` flag-bit semantics were not decoded; both clearly enable alpha blending.

12. **Inventory in-game window full layout.** The full in-game inventory builder's individual grid
    slot coordinates are not recovered; only the chrome (shared `InventWindow.dds` 340 × 190 panel)
    is confirmed.

13. **DISABLED frame runtime write.** No ctor passes a distinct disabled sprite origin (DISABLED
    always equals NORMAL). If any window code writes a distinct origin to +0xE0/+0xE4 at runtime
    (post-ctor), the sweep would not see it. Not observed in login/select but unverified in-game.

14. **`characwindow.dds` provenance.** The in-game character-info/stat window (§8.7) is **not** built
    on `characwindow.dds` — its chrome is `inventwindow.dds` (uitex 2) + `tradekeepwindow.dds`
    (uitex 4). `characwindow.dds` is not bound by that builder; it is plausibly an unused/legacy
    asset, or belongs to a different other-player info popup (a distinct builder not swept here).

15. **Options Sound / Graphic / fourth sub-panels.** Only the Options **Character** sub-panel (§8.9)
    was fully recovered. The Sound and Graphic sub-panels (and the fourth tab's sub-panel) bind the
    same atlases (uitex 1 + 9) but their widget tables were not dumped; their option-label `msg.xdb`
    ids are in the same 8xxx bank. Two of the Character-tab caption labels were register-fed and not
    reduced to literals.

16. **Inventory slot-grid runtime atlas.** The inventory slot grid (§8.10) binds texture id 0 at
    build time; the slot/icon atlas (and its 4-row frame table seeded to origins (296, 0) / (296, 64)
    / (296, 128) / (296, 192)) is assigned to the panel per-instance after build. The exact runtime
    bind site and DDS (PLAUSIBLE `inventwindow.dds`) were not traced.

17. **Server-record +6 open-time wire packing.** The +6 field of the 8-byte server-select record
    (§11.4.1) is used only for the open-time schedule presentation; its exact wire packing is
    static-only (CAPTURE-UNVERIFIED — no live lobby capture).

---

## 13. Godot reconstruction guidance

### 13.1 Reference canvas and scaling

The legacy canvas is **1024 × 768** pixels at 1:1 pixel density. Implement the UI root as a
`SubViewport` or a root `Control` node with reference size `(1024, 768)`, then use Godot's
`CanvasLayer` / `stretch_mode` to scale to the actual window size. The atlas sub-rects map to
Godot `AtlasTexture` resources: `atlas = preload("res://…/login_slice1.dds")`,
`region = Rect2(srcX, srcY, w, h)`.

### 13.2 Button state frames

For each GUButton (7-state), keep **three** separate `AtlasTexture` regions: NORMAL, HOVER,
PRESSED. DISABLED reuses the NORMAL region with a grey caption tint (`Color(0.4, 0.4, 0.4)`).
HOVER tints the caption yellow (`Color(1, 1, 0)`). Normal/pressed use the per-widget tint (default
white). The NORMAL region is the `Rect2(srcX, srcY, w, h)` from the ctor arguments; the HOVER
and PRESSED regions use their respective `(srcX, srcY)` origins at the same `(w, h)` size. For the
front-end widget tables, read the three frame origins in **NORMAL, PRESSED, HOVER** order (§1.5).

### 13.3 Login textboxes

Two `LineEdit` (or equivalent) nodes at screen positions `(390, 32)` and `(568, 32)`, each
102 × 13 pixels. The second (password) field should mask input. Both accept CP949 Korean text
through Godot's native IME support (`DisplayServer.ime_active = true` when focused). The account
field is IME field 1; the password field is field 2.

### 13.4 Fonts

Map the 15 font slots to system-installed or bundled Korean TrueType fonts. Use the **D3DX Height
(rowHeight)** column from §6.2 as the Godot font pixel size; the **D3DX Width (charWidth)** column
is the per-character advance width for the fixed-advance text drawing:
- **DotumChe** → 돋움체 (NanumGothicCoding or similar fixed-pitch Korean sans)
- **Dotum** → 돋움 (NanumGothic or similar proportional Korean sans)
- **BatangChe** → 바탕체 (NanumMyeongjo or similar fixed-pitch Korean serif)

Use `FontFile` nodes loaded with the listed height as the `size` argument. CP949 string rendering
requires fonts covering the Hangul Syllables Unicode block (U+AC00–U+D7A3). Translate msg.xdb IDs
to Unicode strings in `Assets.Parsers` and pass them to Godot as UTF-8.

All text drawing is **fixed-advance** (charWidth per character); use `charWidth * strlen` for the
horizontal extent of any label, not measured glyph widths. On the **front-end** (login + character-
select), every label/caption/textbox uses **font slot 0** (DotumChe, height 12 / width 6, weight 0)
— there is no per-widget font variety to model there (§6.3).

### 13.5 Scene flow

Model the 9 primary states as a Godot `SceneTree` scene-change sequence or as an
`Application`-layer state machine (preferred, to keep game logic engine-free):

- State 1 (Login) → state 2 (Load/opening gate) → optional state 3 (cinematic) → state 4
  (Select) → state 5 (In-game) → **state 4** (Select, on logout).
- The `[OPENNING] SKIP` INI key controls the 2 → 3/4 branch; default to skip in the revival build.
- State 7 (Error) shows a modal dialog and proceeds to state 8 (Exit).

### 13.6 Char-select 3D preview

Build 5 `SubViewport` + scene nodes from each slot's SpawnDescriptor using the same actor build
path as in-world. Empty slot → create form (class 1..4, face 1..7, gender). Stage X
offsets per slot: {−1560, −1548, −1536, −1524, −1512}; Z ≈ −3593; scale ×3.0. Cache the 880-byte
descriptor on enter-confirm and spawn from it after the server ack. The **slot hit-test is a 3D
ray-pick** against per-slot AABBs (±6 in X/Z, Y band 70…92), not 2D rectangles — replicate the
camera-unproject + AABB test rather than placing 2D click regions (§8.2, `frontend_scenes.md §3.3.3`).

### 13.7 Chat window

The chat window is the **only** UI element with a data-driven initial size and position. Read
`CHAT_WINDOW_POS_X`, `CHAT_WINDOW_POS_Y`, `CHAT_WINDOW_SIZE`, `CHAT_WINDOW_FONT_SIZE` from Lua
(or an equivalent config key in the revival config), and apply them to the chat control at
scene-create time.

### 13.8 Movable-panel persistence

In-game panels that the player can drag should persist their drag offset to per-character storage
(keyed by player descriptor). The reset condition — saved coordinate = −1, or screen size changed
— must be implemented to match the legacy behaviour. Default positions come from each panel's
hardcoded build coordinates.

### 13.9 In-game HUD (atlas-gated contract)

All in-game window builders bind atlas by `uitex.txt` integer id. The Godot presentation engineer
**must** resolve each integer id to a DDS filename via the `uitex.txt` manifest before implementing
per-widget atlas regions. The destination rects and 4-frame src origins for all in-game windows are
recoverable from the binary once DDS names are supplied. This is the §12 open item 6 dependency —
the implementation of the remaining (unlabelled) in-game HUD sub-windows is gated on
`formats/ui_manifests.md`.

For the **character-info/stat, skill, options, and inventory windows**, the binding is already
resolved: use the `uitex.txt` id→DDS key table in **§8.6.1** to turn each builder's integer id into
a DDS path, then build an `AtlasTexture` per source rect (`region = Rect2(srcX, srcY, w, h)` on that
DDS). For each 7-state button keep NORMAL / HOVER / PRESSED regions (§8.7–§8.9); for each
`GUCheckBox`, UNCHECKED = NORMAL region and CHECKED = PRESSED region. Window titles come from
`msg.xdb` (skill window = id 3027; the inventory and character-info/stat windows have no title id —
see §8.11), so the Godot side should fetch those captions through `Assets.Parsers` rather than
baking title text, except the inventory title which is part of the `inventwindow.dds` chrome art.

### 13.10 Front-end atlas loading

Match the front-end loader lifecycle of §9.0: load each scene's atlas set **once, eagerly, at scene
build**, hold the handles on the scene node, and free them on scene exit. Do **not** build a shared
texture cache or reference-count atlas handles across scenes — the original model is per-window,
append-only ownership with an outright release on window teardown.

---

## 14. Cross-references

- Base widget byte-offset structs: `structs/gucomponent.md`, `structs/guwindow.md`
- Master window object + service-slot table: `structs/runtime_singletons.md §3.10`
- In-game HUD panel placement + buff bar: `specs/ui_hud_layout.md`
- Widget hit-test / IME routing detail: `specs/input_ui.md`
- Login network flow: `specs/login_flow.md`
- Front-end scene flow (incl. char-select 3D slot ray-pick §3.3.3): `specs/frontend_scenes.md`
- Cryptography: `specs/crypto.md`
- UI asset file formats: `formats/ui_manifests.md` (uitex.txt, skillicon.txt, crestlist.txt)
- Texture format: `formats/texture.md`
- VFS lookup: `formats/pak.md`
- String encoding: all CP949 strings; register `CodePagesEncodingProvider` before any decode
- msg.xdb format spec: `formats/misc_data.md §6`
- Skinning / character preview: `specs/skinning.md`, `formats/mesh.md`
- Glossary: `Docs/RE/names.yaml`
- Provenance: `Docs/RE/journal.md`
