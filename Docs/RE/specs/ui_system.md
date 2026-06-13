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

**Caption colour by interaction state:**
- Disabled → grey (`0xFF666666`)
- Hovered → yellow (`0xFFFFFF00`)
- Normal/pressed → per-widget tint at +0x0C (default white)

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

### 6.3 Text rendering — fixed-advance grid

`Font_DrawString(table, x, y, str, color, fontSlot, scale)` computes a bounding rect as
`{x, y, x + charWidth(slot)*strlen, y + rowHeight(slot)*scale}` and calls
`ID3DXFont::DrawTextA(NULL, str, -1, &rect, DT_SINGLELINE, color)`. Horizontal layout is
therefore **fixed-advance** (charWidth per character, not proportional metrics), so labels are
laid out on a monospace grid even with proportional faces. This matters for matching legacy text
positioning.

> The per-widget font-slot index (which slot each `GULabel` / `GUTextbox` draws with) lives in
> the instance data and was not exhaustively recovered — see Section 9, open item 4.

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
- The **forced-alpha byte at +0x0F** (≠ 0xFF) pins alpha immediately, bypassing the fade —
  used for dimmed/blackout overlays.

### 7.2 Z-order

- **Paint order = insertion order:** later children paint on top.
- **Input order = reverse:** hit-test walks children end → front; the topmost-painted child
  wins input.
- There is **no z-index field**. Reordering means removing and re-adding a child, or using the
  active-child index (+0xB4) for tab panels.

### 7.3 Child management

- `Panel_AddChildWithAction(parent, child, actionId)`: sets `child[+0x84] = parent`,
  `child[+0x10] = actionId`, pushes into the parent's child vector at +0xA8.
- `Panel_AddChild(parent, child)`: same without an action id (decorative widgets).
- `RemoveMarkedChildren` (vtable slot 13): sweeps children with remove-flag +0x8D == 1 and
  compacts the vector — the deferred-removal mechanism.

### 7.4 GUList vertical scroll

The list draws self, then for each child computes visibility against the scroll offset (+0xDC) and
viewport height (+0x20): children outside the viewport are hidden (SetShown(0)); visible ones have
their draw position offset by `childY − scrollY`. Clipping is by show/hide + position offset,
not a hardware scissor.

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

### 8.1 Login screen — `BuildScene` — 21 ctor sites (CODE-CONFIRMED)

**Atlas assignment (CODE-CONFIRMED from builder source):**

| Atlas DDS | Used for |
|---|---|
| `data/ui/login_slice1.dds` | OK/Login button, Server-list button, ID/PW textboxes, Save-ID checkbox, Quit/Help strip |
| `data/ui/loginwindow.dds` | Option/tab buttons, server name-strip buttons, decorative spinners/dots, EULA panel background |
| `data/ui/loginwindow_02.dds` | Panning intro banner strip |
| `data/ui/InventWindow.dds` | Quit-confirm modal chrome and buttons |

> **Correction:** the OK/Login button, Server-list button, ID/PW textboxes, and Save-ID checkbox
> are bound to **`login_slice1.dds`**, not `loginwindow.dds`. An earlier version of this spec
> attributed them to `loginwindow.dds` in error.

**Widget table (full 21 ctor sites, CODE-CONFIRMED):**

| Site | Type | dst (x,y,w,h) | NORMAL (srcX,srcY) | HOVER (srcX,srcY) | PRESSED (srcX,srcY) | Atlas | actionId / role |
|---|---|---|---|---|---|---|---|
| Intro banner | BTN7 | (—,97,202,372)¹ | (9,6) | (220,6) | (220,6) | loginwindow_02.dds | panning banner strip |
| Quit/Help strip | BTN7 | (456,**−3**,111,38) | (792,398) | (602,416) | (602,416) | login_slice1.dds | **act 105** |
| Quit-confirm "Yes" #1 | BTN7 | (120,136,113,40) | (302,900) | (415,900) | (302,900) | InventWindow.dds | **act 113** |
| Quit-confirm "Yes" #2 | BTN7 | (120,136,113,40) | (302,860) | (415,860) | (302,860) | InventWindow.dds | **act 114** |
| **Server-list button** | BTN7 | (456,166,112,39) | (154,398) | (378,398) | (378,398) | login_slice1.dds | **act 102** |
| **ID/account textbox** | TEXTBOX | (390,32,102,13) | (615,404) | — | — | login_slice1.dds | IME 16, maxlen 6 |
| **Password textbox** | TEXTBOX | (568,32,102,13) | (615,404) | — | — | login_slice1.dds | IME 12, maxlen 129 |
| **Save-ID checkbox** | CHECK | (694,86,13,13) | (717,398) unchecked | — | (730,398) checked | login_slice1.dds | **act 104** |
| **OK / Login button** | BTN7 | (456,64,112,39) | (266,398) | (490,398) | (490,398) | login_slice1.dds | **act 103** |
| **Option/tab button 1** | BTN7 | (40,82,110,38) | **(520,492)** | (635,492) | (520,492) | loginwindow.dds | **act 111** |
| **Option/tab button 2** | BTN7 | (164,82,110,38) | **(750,492)** | (865,492) | (750,492) | loginwindow.dds | **act 112** |
| Decorative spinner/arrow A | BTN2 | (467,86,13,10) | (483,490) | =N | =N | loginwindow.dds | decorative |
| Decorative spinner/arrow B | BTN2 | (467,455,13,10) | (505,490) | =N | =N | loginwindow.dds | decorative |
| Decorative dot | BTN2 | (469,98,9,9) | (496,490) | =N | =N | loginwindow.dds | decorative |
| **Server name-strip** | BTN7 | (13+47·i,66,47,18) | (596,985) | (643,985) | (643,985) | loginwindow.dds | **acts 115..119** (generator, see §8.4) |
| Banner/title label | LABEL | (50,100,383,50) | (0,0) | — | — | — | text from msg.xdb |
| EULA notice label A | LABEL | (—,390,174,21)² | — | — | — | — | EULA/notice |
| EULA notice label B | LABEL | (—,410,174,20)² | — | — | — | — | EULA/notice |
| EULA notice label C | LABEL | (—,430,174,20)² | — | — | — | — | EULA/notice |
| Quit-confirm prompt #1 | LABEL | (10,100,330,20) | — | — | — | — | msg 4023 |
| Quit-confirm prompt #2 | LABEL | (10,100,330,20) | — | — | — | — | msg 4024 |

¹ x is register-fed (centre-computed).
² x is the EULA-panel-relative computed position.

**Login action-id / ASCII-char map (CODE-CONFIRMED):**

| actionId | Widget | Behaviour |
|---|---|---|
| 102 (`f`) | Server-list button | reveal server-list panel |
| 103 (`g`) | OK/Login button | version gate → sub-state 29 |
| 104 (`h`) | Save-ID checkbox | persist/clear `OPTION_ID` in DoOption.ini |
| 105 (`i`) | Quit/Help strip | throttled re-fetch (sub-state 34) |
| 109 (`m`) | Focus ID box | clear PW focus, focus ID |
| 110 (`n`) | Focus PW box | clear ID focus, focus PW |
| 111 (`o`) | Option/tab 1 | option page select |
| 112 (`p`) | Option/tab 2 | option page toggle |
| 113 (`q`) | Quit-confirm Yes #1 | hide popup, advance to sub-state 34 |
| 114 (`r`) | Quit-confirm Yes #2 | same |
| 115–119 | Server name-strip ×5 | server entry select (active at sub-state 37) |

### 8.2 Character-select screen — `InitFromCharListAndBuildUI` — 77 ctor sites (CODE-CONFIRMED)

**Atlas assignment:**

| Atlas DDS | Used for |
|---|---|
| `data/ui/loginwindow.dds` | Tab buttons, stat-icon grids, Create/Delete buttons, appearance selector strip |
| `data/ui/InventWindow.dds` | Confirm/modal chrome and buttons (shared with login) |
| `data/ui/CarrierPigeonPerson.dds` | Appearance selector ±, gender/class preview swatches |
| `data/ui/blacksheet.dds` | Corner close button |

**Structural recovery — key rectangles (CODE-CONFIRMED):**

| Region | x | y | w | h | Atlas note | Confidence |
|---|---|---|---|---|---|---|
| Top title bar panel | 0 | 0 | 577 | 58 | `mainwindow.dds` | CODE-CONFIRMED |
| Left character-info panel | 0 | 0 | 244 | 187 | `mainwindow.dds` | CODE-CONFIRMED |
| Server tab button | 67 | 17 | 113 | 40 | NORMAL (675,795), HOVER (483,883) | CODE-CONFIRMED |
| Channel tab button | 232 | 7 | 113 | 40 | NORMAL (640,742), HOVER (483,923) | CODE-CONFIRMED |
| Back tab button | 393 | 17 | 113 | 40 | NORMAL (625,691), HOVER (483,963) | CODE-CONFIRMED |
| **Per-slot stat-icon grid col 1** | 154 | **191** (base), stride **24** | 24 | 16 | NORMAL (500,770), PRESSED (548,770) | CODE-CONFIRMED (base Y resolved) |
| **Per-slot stat-icon grid col 2** | 178 | 191 (base), stride 24 | 24 | 16 | NORMAL (524,770), PRESSED (572,770) | CODE-CONFIRMED |
| Per-slot stat value labels | 51 | 193 (base), stride 24 | 35 | 12 | — | CODE-CONFIRMED |
| **Create button** | 42 | 325 | 59 | 20 | NORMAL (354,1004), HOVER (413,1004) | CODE-CONFIRMED |
| **Delete button** | 112 | 325 | 59 | 20 | NORMAL (472,1004), HOVER (531,1004) | CODE-CONFIRMED |
| **Enter/select button** | 112 | 112 | 59 | 20 | NORMAL (236,1004), HOVER (295,1004) | CODE-CONFIRMED |
| Confirm modal (Yes/No) | 55 / 174 | 136 | 113 | 40 | `InventWindow.dds` (302,860)/(302,900) | CODE-CONFIRMED |
| Name-entry textbox | 60 | 80 | 274 | 18 | `GUTextbox`; CP949 character name | CODE-CONFIRMED |
| Name-entry OK | 55 | — | 113 | 40 | `InventWindow.dds` (302,860) | CODE-CONFIRMED |
| Name-entry Cancel | 174 | — | 113 | 40 | `InventWindow.dds` (302,900) | CODE-CONFIRMED |
| Corner close | 971 | 610 | 23 | 23 | `blacksheet.dds` (941,910) | CODE-CONFIRMED |

**Char-select action-id map (CODE-CONFIRMED — corrected from prior notes):**

| actionId | Widget | Note |
|---|---|---|
| 1 | Server tab | — |
| 2 | Channel tab | — |
| 3 | Back tab | — |
| **4** | **Create button** | **Note: older notes showed 413 — that was the HOVER src-X, not the action id** |
| **5** | **Delete button** | **Note: older notes showed 531 — that was the HOVER src-X, not the action id** |
| **6** | **Enter/select button** | — |
| 10 | Create-form Create | inside create sub-form |
| 11 | Create-form Delete | inside create sub-form |
| 12 | Create-form OK | — |
| 13 | Create-form Cancel | — |
| 21 / 22 | Face/gender increment ± | — |
| 25..36 | Appearance increments | appearance selector range |
| 54 / 55 | Name-entry OK / Cancel | first pair |
| 59 / 60 | Name-entry OK / Cancel | second pair |
| 61..74 | Per-slot / stat-grid actions | selection and stat-grid interactive buttons |

> **Correction.** The values 413 and 531 that appeared in earlier versions of this spec
> (§2.2, char-select Create/Delete action ids) are the **atlas src-X coordinates** of the
> Create-button HOVER frame (354+59 = 413) and the Delete-button HOVER frame (472+59 = 531),
> not action ids. The actual `Panel_AddChildWithAction` ids are **Create = 4, Delete = 5,
> Enter = 6** (CODE-CONFIRMED from the widget-sweep literal dump).

### 8.3 Shared panel chrome — `InventWindow.dds` modal

Both the login quit-confirm popup and the character-select confirm/delete popups use the **same
340 × 190 chrome at source `(318, 647)`** from `data/ui/inventwindow.dds`. The inventory window
chrome is a shared atlas reused across all three screens (login, select, and in-game inventory).

### 8.4 Generator patterns for looped / register-fed widgets

Several widgets are built in a loop or with a centre-computed X so the construction site has no
literal for those coordinates. The recovered generator rules:

| Widget group | Rule |
|---|---|
| Login server name-strip | Base (13,66,47,18); NORMAL src (596,985); X advances **+47** per entry (`x = 13 + 47·i`) for up to 5 server entries (acts 115..119) |
| Char-select stat-icon grid | Base-Y **191**, stride **24**, 5 rows; col 1 at x=154, col 2 at x=178 |
| Char-select stat value labels | Base-Y **193**, stride **24**, 5 rows; x=51, w=35, h=12 |
| Char-select appearance selector | Base y=30, w=45, h=19; NORMAL src-X base **590** stepping **+45** (590/635/680/725); HOVER base **815** (+45) |

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
| Dice/gamble panel | 1 | 37 (13 BTN7, 24 LABEL) | atlas `five.tga` |
| **Quest window** | 1 | 35 (22 BTN7, 1 CHECK, 12 LABEL) | key `CHAR_QUEST_TRACKING` |
| **Emotion/expression panel** | 1 | 28 (22 BTN7, 3 BTN2, 3 LABEL) | atlas `data/ui/face/anger/ani_000.dds` |
| **Options/settings panel** | 1 | 26 (12 CHECK, 12 LABEL, 2 BTN7) | 12 checkboxes |
| Toolbar/hotbar | 1 | 25 (19 BTN7, 6 BTN2) | many 2-state buttons |
| Trade/mail/search input window | 1 | 24 (21 BTN7, 1 LABEL, 2 TEXTBOX) | 2 textboxes |
| **Chat window** | 1 | 22 (15 BTN7, 7 CHECK) | keys `CHAT_WINDOW_POS_X/Y/SIZE/FONT_SIZE` |
| **Tender/auction window** | 1 | 21 (2 BTN7, 19 LABEL) | atlas `tender_window.dds` |
| Options sub-page | 1 | 21 (7 CHECK, 10 LABEL, 4 BTN7) | many checkboxes |
| Musajung (NPC/duel) window | 1 | 15 | atlas `musajung.dds` |
| **Password entry window** | 1 | 5 | atlas `password.dds`, `InventWindow.dds` |
| Additional windows (unlabelled) | ~103 | varies | — |

The **16 identified windows** are: chat, guild, quest, options (main + sub-page), web-link,
password, emotion, tender, toolbar/hotbar, trade/mail input, large data window, dice/gamble,
musajung, plus the skill and inventory windows (confirmed atlas from §3.5 of the prior asset
manifest). The remaining ~101 builders are inventoried by ctor count and are DDS-attribution-gated
on the VFS manifest (Open item 6).

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

---

## 9. Per-screen asset manifests

All paths are VFS-relative. All large UI sheet atlases are DDS with DXT3 pixel format and a
1024 × 1024 (or occasionally 512 × 512) mip-0 surface. See `formats/ui_manifests.md` for the
file-level format of `uitex.txt` and related manifests.

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
| `data/ui/inventwindow.dds` | Popup panels + buttons (confirm / delete / name-entry chrome) | CODE-CONFIRMED |
| `data/ui/blacksheet.dds` | Corner close button; dim/blackout overlay | CODE-CONFIRMED |
| `data/ui/carrierpigeonperson.dds` | Appearance selector ±, gender/class preview swatches | CODE-CONFIRMED |
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
credential flow described in `specs/login_flow.md` and `specs/frontend_scenes.md`.

| Sub-state | Meaning | Confidence |
|---|---|---|
| 2 | Connect / play login-enter SFX **861010105**; reset banner Y | CODE-CONFIRMED |
| 3, 4, 5 | Animate the two banner panels into place | CODE-CONFIRMED |
| 6 | **Login form active** — waiting for user input | CODE-CONFIRMED |
| **29** | **OK-button credential validation** — checks ID len ≥ 4 (else msg **4025** → 6); PW len ≥ 1 (else msg **4026** → 6); persist Save-ID; advance to 31. **Correction: this was incorrectly labelled "server-list trigger" in an earlier version of this spec.** | CODE-CONFIRMED |
| 30 | Quit-confirm "Yes" path — writes engine state **6 / substate 8** (quit) | CODE-CONFIRMED |
| **31** | **Show EULA / terms-of-service accept overlay** — advances toward 32. **Correction: this was incorrectly labelled "Help screen" in an earlier version of this spec.** The Help button is the separate control at action id 105 (`i`), not a sub-state. | CODE-CONFIRMED |
| 32 | Wait for EULA "agree" — overlay visible AND accept flag set → advance to 33 | CODE-CONFIRMED |
| 33 | Press-OK transition → begin server-list fetch (set 34) | CODE-CONFIRMED |
| 34 | Start server-list fetch thread (port 10000) | CODE-CONFIRMED |
| 35 | Wait for server-list reply | CODE-CONFIRMED |
| 36 | Consume server list: empty → msg **4027** → 6; connect-fail → msg **4028** → 6; else render + set 37 | CODE-CONFIRMED |
| 37 | Server selected | CODE-CONFIRMED |
| 38 | Start channel-endpoint fetch thread (port 10000 + selected offset) | CODE-CONFIRMED |
| 39 | Wait for endpoint reply | CODE-CONFIRMED |
| 40 | Consume endpoint; build TAB-delimited credential string; rebuild secure context; hand off to game connection | CODE-CONFIRMED |
| 41 | Transition complete — login window exits | CODE-CONFIRMED |

### 11.4 Character-select screen lifecycle note

The `InitFromCharListAndBuildUI` builder is not called at scene creation. It is invoked when the
`SmsgCharacterList` (opcode 3/1) packet arrives on the network — the packet handler forces the
primary scene state into the select scene and triggers the builder. This means the select screen
widget tree does not exist until the packet is received.

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

4. **Per-widget font-slot index.** Which of the 15 slots each `GULabel` or `GUTextbox` instance
   uses lives in the instance data and was not exhaustively extracted.

5. **`msg.xdb` format: RESOLVED.** The format is CODE-CONFIRMED (flat binary, 516-byte records,
   `u32 id` + `u8[512]` CP949 text, no header, count from `file_size / 516`, red-black tree at
   load). Record content is SAMPLE-UNVERIFIED pending VFS probe repair. Full spec at
   `Docs/RE/formats/misc_data.md §6`.

6. **In-game window DDS attribution (the big open debt).** 115 of 117 builders bind atlas by
   integer `uitex.txt` texture-id, not DDS string. Resolving each in-game window's per-widget
   atlas DDS name requires the VFS `data/ui/uitex.txt` id→DDS manifest. The destination rects and
   4-frame src origins are recoverable from the binary now; only the DDS name resolution is gated.
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
and PRESSED regions use their respective `(srcX, srcY)` origins at the same `(w, h)` size.

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
horizontal extent of any label, not measured glyph widths.

### 13.5 Scene flow

Model the 9 primary states as a Godot `SceneTree` scene-change sequence or as an
`Application`-layer state machine (preferred, to keep game logic engine-free):

- State 1 (Login) → state 2 (Load/opening gate) → optional state 3 (cinematic) → state 4
  (Select) → state 5 (In-game) → **state 4** (Select, on logout).
- The `[OPENNING] SKIP` INI key controls the 2 → 3/4 branch; default to skip in the revival build.
- State 7 (Error) shows a modal dialog and proceeds to state 8 (Exit).

### 13.6 Char-select 3D preview

Build 5 `SubViewport` + scene nodes from each slot's SpawnDescriptor using the same actor build
path as in-world. Empty slot (`@BLANK@`) → create form (class 1..4, face 1..7, gender). Stage X
offsets per slot: {−1560, −1548, −1536, −1524, −1512}; Z ≈ −3593; scale ×3.0. Cache the 880-byte
descriptor on enter-confirm and spawn from it after the server ack.

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

All 117 in-game window builders bind atlas by `uitex.txt` integer id. The Godot presentation
engineer **must** resolve each integer id to a DDS filename via the `uitex.txt` manifest before
implementing per-widget atlas regions. The destination rects and 4-frame src origins for all in-game
windows are recoverable from the binary once DDS names are supplied. This is the §12 open item 6
dependency — the implementation of in-game HUD sub-windows is gated on `formats/ui_manifests.md`.

---

## 14. Cross-references

- Widget hit-test / IME routing detail: `specs/input_ui.md`
- Login network flow: `specs/login_flow.md`
- Front-end scene flow: `specs/frontend_scenes.md`
- Cryptography: `specs/crypto.md`
- UI asset file formats: `formats/ui_manifests.md` (uitex.txt, skillicon.txt, crestlist.txt)
- Texture format: `formats/texture.md`
- VFS lookup: `formats/pak.md`
- String encoding: all CP949 strings; register `CodePagesEncodingProvider` before any decode
- msg.xdb format spec: `formats/misc_data.md §6`
- Skinning / character preview: `specs/skinning.md`, `formats/mesh.md`
- Glossary: `Docs/RE/names.yaml`
- Provenance: `Docs/RE/journal.md`
