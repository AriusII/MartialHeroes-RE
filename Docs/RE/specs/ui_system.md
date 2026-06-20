---
verification: confirmed
ida_reverified: 2026-06-20
ida_anchor: 263bd994
evidence: [static-ida]
conflicts: server-record +6 open-time wire packing (capture-unverified); PIN keypad runtime seed/permutation (debugger-pending — clock-seeded shuffle, mechanism confirmed); account/save flag gating entry into login sub-state 31 (debugger-pending); GUCanvas3D render-target wiring untraced; in-game GUButton caption font-slot byte offset not pinned; skill-hotbar overlay-rect VALUES data-driven (debugger-pending — shape confirmed) — 2026-06-20 CYCLE 7 (IDB SHA 263bd994): full 178-slot panel-slot→class roster landed (§1.9); SLOT REVERSALS — the real selected-target/MopGage frame is **slot 35 (MopGagePanel)** and the real pet window is **slot 52 (PetPanel)**; prior "MopGage = slot 177" and "pet = slot 110" are REFUTED (slot 177 = base GUComponent image, slot 110 = Gamble); slot 135 = UpgradeProcessPanel CONFIRMED; slot 178 (+0x500) = MainHandler — 2026-06-17 Campaign-17 in-game-HUD re-confront (263bd994): inventory bag = ItemPanel 8x5/40-cell grid CODE-CONFIRMED (closes campaign-12 inventory grid), §8.10 GatherSlotPanel role-relabel, §8.8 skill-pipe = 4 panels (not 50), §8.6.1 reconciled to uitex.txt VFS manifest, §8.7 StatusPanel cosmetic drifts corrected
---

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
| 3-state | arg `(sX, sY)` | = NORMAL | arg `(pX, pY)` | = NORMAL |
| 7-state | arg `(sX, sY)` | arg `(hX, hY)` | arg `(pX, pY)` | = NORMAL |

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
  window on first use (a Meyers/one-shot-guarded singleton). This accessor is **the single busiest
  UI accessor in the whole client — exactly 1874 code call sites** — the de-facto "HUD orchestrator
  handle".
- **A flat service-slot registry.** On construction the master window **zero-initialises** a
  contiguous run of pointer-width service slots in its tail region. Each subsystem (inventory,
  skills, chat, map, party, quest, guild, trade, options, …) later **registers its constructed
  panel pointer into a fixed slot**. The HUD is the sum of those slots hanging off the one
  "MainMaster" window. The same zero-init routine runs again on teardown to clear the registry.
- **Slot-table bounds (re-pinned, CODE-CONFIRMED).** The service-slot region is a contiguous
  dword run from byte **+0x238 (568)** through byte **~0x5B0 (1456)** — i.e. **~222–223 pointer-width
  slots** plus a short tail of small zeroing memsets to ~+1460. The very same zero-init routine runs
  from **both** the master ctor and the master teardown. One concrete slot owner is now anchored:
  the **in-game scene/HUD handler is registered at slot index 320 = byte +0x500 (1280)** — the
  state-5 in-game build re-reads that slot to drive `BuildScene` on the in-game scene graph (§11.2
  state 5, §15.2). The full slot→subsystem map is otherwise still an open item (the other slot owners
  are not yet enumerated). These offsets and the ~6 currently-identified slot owners (including the
  back-reference to the owning HUD handler) are documented in `structs/runtime_singletons.md §3.10`.
- **MI shape re-pinned (CODE-CONFIRMED).** The master window's ctor writes its primary
  component/panel/window vtable at **+0x00** and the secondary event-handler vtable at **+0xBC (188)**,
  confirming the two-vtable `GUWindow` multiple-inheritance shape (§1.8). The embedded sub-objects sit
  at the same `GUWindow` offsets as every other window: command handler +0xBC, auxiliary view +0xE8,
  texture list +0x220.

So the manager is **not** a dispatcher object with a window list — it is a single top-level window
whose embedded pointer table holds every HUD child panel. A reimplementation can model this as one
root HUD node owning a fixed-index array of child-panel references, reached through a singleton
accessor.

> **Two registries, neither is a dispatcher (CODE-CONFIRMED).** The client has exactly two pointer
> registries and **no global "WindowManager" dispatcher object** between them. (1) the master window's
> flat service-slot table above (the live HUD), and (2) a **per-scene disposable `std::list`** used only
> for teardown ordering (§15.1, §11.2). The helper sometimes framed elsewhere as "register the window
> into the manager" is in fact a push onto that disposable teardown list — it is **not** manager
> attachment and **not** a window-tree attach. See §15.1 and the IDB-symbol note in §15.6.

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

## 1.9 The full panel-slot → class roster (CODE-CONFIRMED — the HUD panel registry)

The in-game HUD is the set of panels stored into the master window's **panel-slot array**. That
array lives at master-window byte offset **+0x238**; the slot index is `(byte_offset − 0x238) / 4`
(pointer-sized) — see `structs/guwindow.md` for the array base/index derivation and the master-window
size. A single in-game HUD-build routine walks the panel list once and, per panel, **allocates the
object, runs its constructor, and stores the resulting pointer into a fixed slot** of that array. The
concrete class behind each slot is read from the vtable each constructor installs at object `+0x00`
(its RTTI type, neutralised here).

> **Slot-table bases (do not confuse two numbering schemes).** This roster numbers slots from the
> **panel-slot array base +0x238** — slot `i` = byte `+0x238 + 4·i`. §1.6 separately reports the
> in-game scene/HUD handler "at slot index 320 = byte +0x500": that **320** counts pointer slots from
> object `+0x00` (byte 1280 / 4 = 320), whereas the same word is **slot 178** here (byte 1280, i.e.
> `(0x500 − 0x238)/4 = 178`, the +0x500 main-handler slot of §1.9.2). Both refer to the same pointer;
> this roster uses the **+0x238-relative** index throughout.

### 1.9.1 Slot population summary (CODE-CONFIRMED)

- **178 slots are filled** by the HUD-build routine; the highest populated index is **218**.
- **Interior gaps (never built here): slots 42, 104, 147.** **Trailing gaps:** 179–184, 187–217,
  219–230. The scanned range is 0..230.
- A separate **per-game-state reconfigure routine** re-stores only an *existing* fixed subset of slots
  on a scene-state change (it sets text/colour/visibility/sounds, never geometry). It **adds no new
  slots**, so the gaps above are genuinely null/reserved — not lazily filled later (the one exception
  is slot 178, §1.9.2). This reconciles with `ui_hud_layout.md §0.1`, which counts the same 178 stores.

### 1.9.2 Slot 178 — the in-game MainHandler (filled lazily)

Slot **178** (master byte **+0x500**, a 200-byte object) is **skipped by the master window's
slot-zeroing constructor** and is **not** filled by the HUD-build routine. It is created and stored
**only when the in-game world scene state is entered** (the scene state machine of §11). Its class is
**MainHandler**, the in-game state handler. See `structs/guwindow.md` for the +0x500 layout detail.

### 1.9.3 The roster (slot index → concrete panel class → purpose)

Toolkit primitives are kept as `GULabel` / `GUButton` / `GUPanel` / `GUComponent` / `GXCursor2D`;
game panels carry their neutral class name. Confidence is **CODE-CONFIRMED** for every row
(call-immediately-precedes-store attribution verified; class resolved from each constructor's `+0x00`
vtable RTTI).

| Slot | Concrete panel class | Purpose |
|-----:|----------------------|---------|
| 0 | `ActorChatPanel` | Floating actor speech / chat bubble |
| 1 | `DropItemPanel` | Drop-item confirm |
| 2 | `Gage3DPanel` | 3D world gauge (HP bars over actors) |
| 3 | `hyubhengView` (GUPanel subclass) | "hyubheng" view panel |
| 4 | `ItemPanel` | Inventory item window |
| 5 | `TradePanel` | NPC trade window |
| 6 | `DefaultMenu` | Default right-click context menu |
| 7 | `NpcPanel` | NPC dialog root |
| 8 | `RepairNpcPanel` | NPC repair |
| 9 | `QuestNpcPanel` | NPC quest |
| 10 | `KeepNpcPanel` | NPC warehouse / keep |
| 11 | `GuildNpcPanel` | NPC guild |
| 12 | `ConfessionNpcPanel` | NPC confession |
| 13 | `GatherNpcPanel` | NPC gather |
| 14 | `LinkPanel` | Link / hyperlink panel |
| 15 | `GagePanel` | Player HP/MP gauge bar |
| 16 | `StatusPanel` | Character status window |
| 17 | `SkillPanel` | Skill window |
| 18 | `ChatPanel` | Chat input panel |
| 19 | `MapPanel` | Minimap panel |
| 20 | `TotalMapPanel` | Full / world map |
| 21 | `ChatOutputPanel` | Chat log / output |
| 22 | `OptionPanel` | Options root |
| 23 | `ExitPanel` | Exit / quit confirm |
| 24 | `ReAskPanel` | Re-ask / re-confirm |
| 25 | `GuildReaskPanel` | Guild re-ask confirm |
| 26 | `ErrorPanel` | Error message popup |
| 27 | `ActorStatePanel` | Actor buff / state icons |
| 28 | `ActorStatePassivePanel` | Actor passive-state icons |
| 29 | `ActorStateCashPanel` | Actor cash / premium-buff icons |
| 30 | `ActorStateSkillPanel` | Actor skill-state icons |
| 31 | `TalkPanel` | Talk / dialogue panel |
| 32 | `ConfirmPanel` | Generic yes/no confirm |
| 33 | `Descriptor` | Tooltip / item descriptor |
| 34 | `HelpPanel` | Help window |
| **35** | **`MopGagePanel`** | **Target-mob gauge frame — the real "target frame" (see §1.9.4)** |
| 36 | `PcPanel` | Other-player info panel |
| 37 | `PcTradePanel` | Player-to-player trade |
| 38 | `StallKeeperPanel` | Personal stall (vendor) keeper |
| 39 | `ProductPanel` | Product / goods listing |
| 40 | `DeliveryPanel` | Delivery / mail send |
| 41 | `RequestPanel` | Request panel |
| 42 | *(gap — null / reserved)* | Not built by this routine |
| 43 | `PartyPanel` | Party window |
| 44 | `MiniParty` | Mini party HUD |
| 45 | `PartyReqPanel` | Party invite request |
| 46 | `OtherInfo` | Other-info panel |
| 47 | `ShowDownReq` | Duel / showdown request |
| 48 | `MessagePanel` | Message box |
| 49 | `KeepPanel` | Warehouse / keep window |
| 50 | `SetupPanel` | Setup / config panel |
| 51 | `RelationPanel` | Relation / friend-foe panel |
| **52** | **`PetPanel`** | **Pet window — the real pet-window slot (see §1.9.4)** |
| 53 | `SecretMemPanel` | Secret-memo (private notes) |
| 54 | `ItemConfirmPanel` | Item-use confirm |
| 55 | `LetterPanel` | Letter / mail read |
| 56 | `SurvivePanel` | Survival-mode panel |
| 57 | `StackCountPanel` | Stack-split count input |
| 58 | `CountInputPanel` | Numeric count input |
| 59 | `SkillConfirmPanel` | Skill-learn confirm |
| 60 | `CastTimePanel` | Cast-time / progress bar |
| 61 | `NameInputPanel` | Name text input |
| 62 | `PriceInputPanel` | Price input |
| 63 | `InvenSortPanel` | Inventory sort control |
| 64 | `QuestPanel` | Quest log window |
| 65 | `GuildAPanel` | Guild "A" panel (main) |
| 66 | `GuildCreateAPanel` | Guild create |
| 67 | `GuildShowPanel` | Guild show / info |
| 68 | `GuildMemberPosSetPanel` | Guild member rank set |
| 69 | `GuildFameDonatorPanel` | Guild fame donate |
| 70 | `PublicPeacePanel` | Public-peace / law panel |
| 71 | `QuestGiveUpPanel` | Quest abandon |
| 72 | `QuestItemKeepPanel` | Quest-item keep |
| 73 | `NpcQuestPanel` | NPC quest list |
| 74 | `NpcQuestMsgPanel` | NPC quest message |
| 75 | `KeyBackSetup` | Key-binding setup |
| 76 | `NpcQuestTalkPanel` | NPC quest talk |
| 77 | `QuestResultPanel` | Quest reward / result |
| 78 | `EmoticonPanel` | Emoticon picker |
| 79 | `AnnouncePanel` | Announcement banner |
| 80 | `LottoPanel` | Lotto / lottery |
| 81 | `WarInfoPanel` | War info |
| 82 | `GuildWarInfoPanel` | Guild-war info |
| 83 | `BigAlarmPanel` | Big / centre alarm banner |
| 84 | `GMPanel` | GM panel |
| 85 | `ComboPanel` | Combo / dropdown panel |
| 86 | `StallListPanel` | Stall (vendor) listing |
| 87 | `PotalListPanel` | Portal / teleport list |
| 88 | `GoodsPanel` | Goods / shop panel |
| 89 | `StatusDistributePanel` | Stat-point distribution |
| 90 | `GatherSlotPanel` | Gathering slots |
| 91 | `WarStoneInfoPanel` | War-stone info |
| 92 | `BroodWarMapInfoPanel` | Brood-war map info |
| 93 | `BroodWarListPanel` | Brood-war list |
| 94 | `BroodWarAllyStatePanel` | Brood-war ally state |
| 95 | `MediatePanel` | Mediation / arbitration |
| 96 | `CarrierPigeonPanal` | Carrier-pigeon root (in-source spelling "Panal") |
| 97 | `CarrierPigeonSendPanel` | Carrier-pigeon send |
| 98 | `CarrierPigeonReadPanel` | Carrier-pigeon read |
| 99 | `ActorPotalPanel` | Actor portal panel |
| 100 | `FontSeePanel` | Cinematic "font-see" subtitle root |
| 101 | `FontSeeSubTitlePanel` | Font-see subtitle line |
| 102 | `FontSeeTimePanel` | Font-see timer line |
| 103 | `FontSeeSubjectPanel` | Font-see subject line |
| 104 | *(gap — null / reserved)* | Not built by this routine |
| 105 | `Pandemonium` (GUPanel subclass) | "Pandemonium" event panel |
| 106 | `Revengesummons` | Revenge-summon panel |
| 107 | `revengevote` | Revenge-vote panel |
| 108 | `FameStatePanel` | Fame state |
| 109 | `RankStatePanel` | Rank state |
| **110** | **`Gamble`** (GUPanel subclass) | **Gambling panel — NOT a pet window (see §1.9.4)** |
| 111 | `Gmfuntion` | GM function panel (in-source spelling) |
| 112 | `GmCharactor` | GM character panel (in-source spelling) |
| 113 | `Autochect` (GUPanel subclass) | Auto-check panel (in-source spelling) |
| 114 | `NpcSearch` (GUPanel subclass) | NPC search |
| 115 | `FameBuffNpcPanel` | NPC fame-buff |
| 116 | `EventPopupPanel` | Event popup |
| 117 | `SubscriptionPanel` | Subscription / billing |
| 118 | `TenderInfoPanel` | Tender / auction info |
| 119 | `EventItemConfirmPanel` | Event-item confirm |
| 120 | `PlaytimePanel` | Playtime / fatigue |
| 121 | `GameClassPanel` (subclass) | Game-class select panel |
| 122 | `AutoQuestionPanel` | Auto-question (anti-bot) |
| 123 | `AutoCheckPanel` | Auto-check (anti-bot) |
| 124 | `GameAddictionWarningPanel` | Addiction warning |
| 125 | `GfitCharNameInputPanel` | Gift-char name input (in-source spelling) |
| 126 | `GiftCharConfirmStep1` | Gift-char confirm step 1 |
| 127 | `GiftCharConfirmStep2` | Gift-char confirm step 2 |
| 128 | `GiftCharConfirmWaiting` | Gift-char waiting |
| 129 | `GiftCharReceiveConfirm` | Gift-char receive confirm |
| 130 | `GiftCharSecondPassword` | Gift-char second password |
| 131 | `UpgradePanel` | Item upgrade window |
| 132 | `ItemRepairPanel` | Item repair |
| 133 | `ItemTransformPanel` | Item transform |
| 134 | `UpgradeResultPanel` | Upgrade result |
| **135** | **`UpgradeProcessPanel`** | **Upgrade in-progress window — CONFIRMS the prior slot-135 finding (see §1.9.4)** |
| 136 | `AlarmSelectCharacterKind` | Alarm: select character kind |
| 137 | `SelectCharacterKind` | Select character kind |
| 138 | `NoPkPenaltyAlarmPanel` | No-PK penalty alarm |
| 139 | `AutoPenaltyAlarmPanel` | Auto-penalty alarm |
| 140 | `OptionPanel_Other` | Options: other tab |
| 141 | `OptionPanel_Character` | Options: character tab |
| 142 | `OptionPanel_Graphic` | Options: graphic tab |
| 143 | `OptionPanel_Sound` | Options: sound tab |
| 144 | `GreetPanel` | Greeting panel |
| 145 | `TutorTalkPanel` | Tutorial talk |
| 146 | `GUButton` (Diamond) | Inline HUD button (toolkit primitive) |
| 147 | *(gap — null / reserved)* | Not built by this routine |
| 148 | `GUButton` (Diamond) | Inline HUD button |
| 149 | `GUPanel` (Diamond) | Stat-info group container (≈180-byte) — see §1.9.5 |
| 150 | `GULabel` (Diamond) | Stat-info text label |
| 151 | `GUButton` (Diamond) | Inline button |
| 152 | `GUButton` (Diamond) | Inline button |
| 153 | `GUPanel` (Diamond) | Stat-info group container |
| 154 | `GULabel` (Diamond) | Stat-info text label |
| 155 | `GULabel` (Diamond) | Stat-info text label |
| 156 | `GULabel` (Diamond) | Stat-info text label |
| 157 | `GULabel` (Diamond) | Stat-info text label |
| 158 | `GUPanel` (Diamond) | Stat-info group container |
| 159 | `GULabel` (Diamond) | Stat-info text label |
| 160 | `GULabel` (Diamond) | Stat-info text label |
| **161** | **`GULabel` (Diamond)** | **Stat-info text label (240-byte object; caption from the message table) — NOT a distinct panel class (see §1.9.5)** |
| 162 | `GULabel` (Diamond) | Stat-info text label |
| 163 | `GUPanel` (Diamond) | Stat-info group container (≈180-byte) |
| **164** | **`GULabel` (Diamond)** | **Stat-info text label (240-byte object; caption from the message table) — NOT a distinct panel class (see §1.9.5)** |
| 165 | `GULabel` (Diamond) | Stat-info text label |
| 166 | `GULabel` (Diamond) | Stat-info text label |
| 167 | `GULabel` (Diamond) | Stat-info text label |
| 168 | `GUPanel` (Diamond) | Stat-info group container |
| 169 | `GULabel` (Diamond) | Stat-info text label |
| 170 | `GULabel` (Diamond) | Stat-info text label |
| 171 | `GULabel` (Diamond) | Stat-info text label |
| 172 | `GULabel` (Diamond) | Stat-info text label |
| 173 | `GUPanel` (Diamond) | Stat-info group container |
| 174 | `GULabel` (Diamond) | Stat-info text label |
| 175 | `GULabel` (Diamond) | Stat-info text label |
| 176 | `GULabel` (Diamond) | Stat-info text label |
| **177** | **`GUComponent` (Diamond, base image widget)** | **A base image widget — NOT MopGage / NOT a target frame (see §1.9.4)** |
| 178 | `MainHandler` | In-game state handler (master byte +0x500; filled lazily — see §1.9.2) |
| 179–184 | *(gap — null / reserved)* | Not built |
| 185 | `GXCursor2D` | 2D mouse cursor |
| 186 | `GULabel` (Diamond) | Inline text label |
| 187–217 | *(gap — null / reserved)* | Not built |
| 218 | `GUButton` (Diamond) | Inline HUD button |
| 219–230 | *(gap — null / reserved)* | Not built |

### 1.9.4 Binary-won slot reversals (CORRECTIONS — these supersede prior cross-references)

Reading the actual store sites of the HUD-build routine settles several slot identities that earlier
passes (and `ui_hud_layout.md`) had placed elsewhere. All are **CODE-CONFIRMED** from the store-site
disassembly:

| Slot | Real identity | Prior (refuted) claim | Verdict |
|-----:|---------------|-----------------------|---------|
| **35** | **`MopGagePanel`** — the real target-mob gauge / "target frame" | "MopGage / target frame = slot 177" | **The target frame is slot 35, NOT 177.** |
| **52** | **`PetPanel`** — the real pet window | "pet = slot 110" | **The pet window is slot 52, NOT 110.** |
| **110** | **`Gamble`** — the gambling panel | "pet window" | Slot 110 is the gambling panel. |
| **135** | **`UpgradeProcessPanel`** — upgrade-in-progress window | (same) | **CONFIRMS** the prior slot-135 finding. |
| **177** | **`GUComponent`** base image widget | "the real MopGage target frame" | **Slot 177 is a plain base image widget, NOT MopGage.** |

> **Downstream note.** `ui_hud_layout.md §5.5a` correctly identifies slot 135 as `UpgradeProcessPanel`
> and flags the real selected-target plate as the `MopGagePanel` / `GagePanel` family — this roster now
> pins that plate to **slot 35** (`MopGagePanel`). Any note that placed "MopGage at slot 177" or "pet at
> slot 110" is superseded by the table above.

### 1.9.5 The stat-info readout block (slots 149–176)

Slots **149–176** are not distinct named panel classes — they are a **repeating stat / info readout
block** built entirely from toolkit primitives: roughly **5–6 groups**, each group = **one `GUPanel`
container (≈180-byte object) + four `GULabel` text labels**, with a few **inline `GUButton`s**
interleaved (slots 146, 148, 151, 152). The labels are 240-byte `GULabel` instances whose caption text
is sourced by id from the message table. In particular, **slots 161 and 164 are plain `GULabel`
instances** inside this block — not distinct panel classes (an earlier reading singled them out as if
named). An engineer should model 149–176 as a data-fed grid of container+label groups, not as ~28
bespoke classes. The HUD-layout view of this block is in `ui_hud_layout.md §3.3a`.

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
| 15 | +0x3C | **SetShown alias** | a secondary show/hide entry that targets the same +0x8C visible byte as slot 1 (CODE-CONFIRMED — the vtable has **16** slots; slot 15 is the second SetShown entry) |

> **Slot count (CODE-CONFIRMED, Campaign 9D).** The shared vtable has **16** slots (0..15), not 15.
> Slot 15 is a secondary `SetShown` alias writing the same visible byte (+0x8C) as slot 1. Slot 14
> (`BuildScene`) is where each concrete window/panel installs its hardcoded widget tree (the per-class
> build override — e.g. the login and char-select build routines of §8). The container `Draw` (slot 7)
> calls each child's slot 9 (`UpdateTransform`) then slot 7 (`Draw`), matching this table exactly.

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

> **Master widget manifest — single image-component constructor (CODE-CONFIRMED).** All **73**
> login widgets are produced through a small set of factory helpers that all funnel through **one
> shared image-component constructor** with the fixed argument order **(tex, x, y, w, h, srcX, srcY,
> alpha)**. The displayed atlas sub-rect of each widget is therefore `(srcX, srcY)` to
> `(srcX + w, srcY + h)` in the bound atlas, with no scaling. The load-bearing literal rectangles on
> the form atlas `data/ui/login_slice1.dds` are: **ID textbox** dst `(390, 32, 102, 13)` action
> **109** maxlen 6; **password textbox** dst `(568, 32, 102, 13)` action **110** maxlen 129;
> **OK/Login button** dst `(456, 64, 112, 39)` action **103**; **Save-ID checkbox** dst
> `(694, 86, 13, 13)` action **104**. These match the front-form table below — that table remains
> the authoritative per-widget src-rect source; this note records only the count and the
> constructor's argument contract.

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
> structural rectangles (tabs, the single shared left character-info panel, stat grid,
> Create/Delete/Enter, the create-form, the confirm modals) are tabulated below; the per-cell
> decorative strips are summarised, not row-listed. **There is no set of five distinct 2D "slot
> plate" widgets over the 3D preview row** — see the slot-occupancy note below.

**Atlas assignment (CODE-CONFIRMED — the chrome composites from several shared atlases):**

| Atlas DDS | Used for |
|---|---|
| `data/ui/loginwindow.dds` | **Primary** chrome: backgrounds, the shared left character-info panel, stat-grid icons, appearance ± steppers, tab buttons, Create/Delete/Enter |
| `data/ui/mainwindow.dds` | Several slot-row action buttons + create-form widgets (steppers, confirm/cancel, name title) |
| `data/ui/InventWindow.dds` | Detail / confirm sub-panels (647 × 340 dialog frames) shared with login |
| `data/ui/CarrierPigeonPerson.dds` | Appearance selector accents; gender/class preview swatches |
| `data/ui/CarrierPigeonAll.dds`, `data/ui/tradekeepwindow.dds`, `data/ui/blacksheet.dds` | Small create-form accents. `blacksheet.dds` is loaded by the window builder but is used by a later create-form sub-panel — it is **not** a corner-X frame close (that prior claim is REFUTED; see the verified-facts note after the structural table). |

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
| Server tab button | 67 | 17 | 113 | 40 | `loginwindow.dds` 3-state; PRESSED src `(483, 883)` | CODE-CONFIRMED |
| Channel tab button | 232 | 7 | 113 | 40 | `loginwindow.dds` 3-state; PRESSED src `(483, 923)` | CODE-CONFIRMED |
| Back tab button | 393 | 17 | 113 | 40 | `loginwindow.dds` 3-state; PRESSED src `(483, 963)` | CODE-CONFIRMED |
| **Per-slot stat-icon grid col 1** | 154 | **191** (base), stride **24** | 24 | 16 | `loginwindow.dds`; HOVER src-X **548** | CODE-CONFIRMED |
| **Per-slot stat-icon grid col 2** | 178 | 191 (base), stride 24 | 24 | 16 | `loginwindow.dds`; HOVER src-X **572** | CODE-CONFIRMED |
| Per-slot stat value labels | 51 | 193 (base), stride 24 | 35 | 12 | text-only | CODE-CONFIRMED |
| **Create button** (action 4) | 130 | 112 | 59 | 20 | create name-plate atlas; src V=1004 — NORMAL `(0, 1004)` / PRESSED `(59, 1004)` | CODE-CONFIRMED |
| **Delete button** (action 5) | 42 | 112 | 59 | 20 | create name-plate atlas; src V=1004 — NORMAL `(118, 1004)` / PRESSED `(177, 1004)` | CODE-CONFIRMED |
| **Enter/select button** (action 6) | 112 | 112 | 59 | 20 | create name-plate atlas; src V=1004 — NORMAL `(236, 1004)` / PRESSED `(295, 1004)` | CODE-CONFIRMED |
| **Create-form Confirm** (action 35) | 42 | 325 | 59 | 20 | create-plate row art (V=1004) — NORMAL `(354, 1004)` / HOVER src-X `413` / PRESSED `(354, 1004)`; this is the create-name-modal Confirm, **not** the Create button | CODE-CONFIRMED |
| **Create-form Cancel** (action 36) | 112 | 325 | 59 | 20 | create-plate row art (V=1004) — NORMAL `(472, 1004)` / HOVER src-X `531` / PRESSED `(472, 1004)`; this is the create-name-modal Cancel, **not** the Delete button | CODE-CONFIRMED |
| Confirm modal (Yes/No) | 55 / 174 | 136 | 113 | 40 | `InventWindow.dds` | CODE-CONFIRMED |
| Name-entry textbox | 60 | 80 | 274 | 18 | `GUTextbox`; CP949 character name | CODE-CONFIRMED |
| Name-entry OK | 55 | — | 113 | 40 | `InventWindow.dds` | CODE-CONFIRMED |
| Name-entry Cancel | 174 | — | 113 | 40 | `InventWindow.dds` | CODE-CONFIRMED |
| ~~Corner close (atlas-blit widget)~~ | — | — | — | — | **REFUTED — there is NO discrete corner-X close widget built from `blacksheet.dds` in the char-select window.** The select-window builder constructs no close-button rect (no atlas-blit "X"), and `blacksheet.dds`, while loaded by the window builder, is consumed by a later create-form sub-panel, **not** by a frame close. The close is a **message-handler branch**, not a clicked button: a system-close message (ESC / system close) drives the scene to **state 6, sub-state 8** (return to login). See the verified-facts note below. | REFUTED (no widget); close-as-message-branch CODE-CONFIRMED |
| Detail / confirm dialog frames | 318 | 190 | 647 | 340 | `InventWindow.dds` (×5 near-identical panels) | CODE-CONFIRMED |

> **Window close — NO corner-X widget (CODE-CONFIRMED; premise REFUTED, build 263bd994).** The
> earlier rows asserting a discrete 23 × 23 corner-X close button blitted from `blacksheet.dds` are
> **wrong and have been struck.** The select-window builder constructs **no close-button widget** —
> there is no atlas-blit "X" rect for the frame, and no register-fed source for one. `blacksheet.dds`
> *is* loaded by the window builder, but it is consumed by a **later create-form sub-panel**, not by a
> frame close. The window close is instead a **message-handler branch**: a system-close message
> (ESC / system close) is handled directly and drives the scene to **state 6, sub-state 8** (return to
> login). It is **not** a clicked atlas-blit button.
>
> Verified about the char-select window chrome and its real buttons:
> - The frame chrome is drawn from the **login-window / main-window chrome sheets**
>   (`loginwindow.dds` dominant, plus `mainwindow.dds` / `InventWindow.dds`), **not** from
>   `blacksheet.dds`.
> - The window's real navigation is a **trio of action-bound buttons** — **new-character (action 4)**,
>   **enter-game (action 6)**, and a **panel toggle** — these are the visible buttons; **none of them
>   is a window-frame "X".**
> - **Window origin = screen-center minus a fixed offset**, so the window's absolute screen position is
>   resolution-dependent / runtime-fed. The window-local destination rects of the nav buttons are
>   fixed (tabulated above), but their **absolute pixel positions** and the exact **hover / pressed
>   atlas source rects** are **live-pending (6-D / visual-oracle)** — do not treat any concrete pixel
>   rect as confirmed for those.
> - Whether the official UI even displays a corner-X at all is a **visual-oracle question**
>   (**live-pending (6-D)**), since the binary builds none.

**Slot occupancy and the shared left panel (CODE-CONFIRMED).** The character-select screen does
**not** build five distinct 2D "plate" widgets, one per roster slot. Slot selection is the 3D
camera-unproject ray-pick (see the ray-pick note below). The per-slot 2D chrome is a **single shared
left character-info panel** (the stat-icon grid plus its value labels) that reflects the
**currently-selected** slot and is **refreshed on selection** — not five parallel plates.

At build head the routine copies the per-slot character list out of the network-handler's character
list into the window's own slot array. Each per-slot character record carries a **class/state word at
record offset `+0x66`** that is the **occupancy marker: a value of 0 means an empty slot** (the
slot-scan stops at the first slot whose `+0x66` word is 0). When non-zero, that word's class value
(1…4) selects the per-class starter-gear / preview fill. The slot-count caption is formatted via
**msg.xdb id 2209**.

**Stat grid (CODE-CONFIRMED).** The left-column character-info stat grid is a fixed block of
**10 three-state icon-button cells (2 columns × 5 rows)** plus paired value labels. The columns sit
at x = 154 (col 1) and x = 178 (col 2), base-Y **191**, stride **24**, each cell **24 × 16**; the
value labels are at x = 51, base-Y **193**, stride **24**, **35 × 12** (numeric, integer-to-string).

Every cell's **NORMAL and PRESSED source origin is a build-time literal, identical within a column**:
col 1 NORMAL/PRESSED `(500, 770)`, col 2 NORMAL/PRESSED `(524, 770)`. Only the **HOVER src-X
distinguishes the columns** — col 1 HOVER src-X **548**, col 2 HOVER src-X **572** (both at src-Y
770). There is **no per-cell or per-stat source origin and no stat-table read** feeding the grid-button
glyphs; the per-stat variation lives in the numeric value labels and the appearance-string fill, not
in the button artwork. So **none of the stat-grid glyph origins are debugger-pending** — they all
resolve statically. (The earlier "18-cell / per-cell glyph chosen at runtime from a stat table /
debugger-pending" reading was a spec error; this corrects it.)

The 10 grid cells **double as the create-form point-buy ± buttons** (action ids 25–34): the five
left-column cells and five right-column cells are bound to the per-stat increment/decrement controls
(§8.2 action-id map, §item below).

**Create-form layout (CODE-CONFIRMED).** The new-character create sub-form is a self-contained
panel tree shown/hidden as a unit by the scene-reset path:
- **Four class buttons** (3-state, 19 × 30, y = 45, on `loginwindow.dds`) selecting the playable
  class. The four buttons emit class-button command ids 10, 11, 12, 13 (left-to-right) into an
  internal class selector; selecting a class rebuilds the 3D preview actor with that class's
  starter gear and BGM, and sets the class-name caption (msg.xdb 14003–14007). **Src rects (LITERAL):
  NORMAL src-Y = 1005** for all four, NORMAL src-X **590 / 635 / 680 / 725** (buttons 1–4); HOVER
  src-X **815 / 860 / 905** for buttons 1–3. The destination X is **right-anchored and COMPUTED**
  (stride 48 between buttons), so the per-button dst-X is not a build-time literal.
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
| **4** | **Create button** | dst `(130, 112, 59, 20)`; older notes showed 413 — see correction below: 413 is the HOVER src-X of the create-form Confirm widget (action 35), not this id |
| **5** | **Delete button** | dst `(42, 112, 59, 20)`; older notes showed 531 — see correction below: 531 is the HOVER src-X of the create-form Cancel widget (action 36), not this id |
| **6** | **Enter/select button** | — |
| 10 / 11 / 12 / 13 | Create-form class buttons (4) | class selector 0..3 left-to-right |
| 21 / 22 | Face increment ± | 2D portrait only; no 3D rebuild |
| 25…34 | Create stat point-buy ± | per-stat increment/decrement with class floor |
| 35 / 36 | Create-form Confirm / Cancel | dst `(42, 325)` / `(112, 325)` on create-plate row art (V=1004); HOVER src-X 413 / 531. Confirm validates + sends; Cancel returns via the scene-reset path. These two widgets carry ± stepper artwork but functionally **are** Confirm/Cancel. |
| 54 / 55, 59 / 60 | Name-entry OK / Cancel pairs | — |
| 61…74 | Per-slot / stat-grid actions | selection and stat-grid interactive buttons |

> **Correction (settled, CODE-CONFIRMED).** The values **413 and 531** that appeared in earlier
> versions of this spec are **not action ids at all** — they are the **HOVER src-X of the two
> create-form Confirm / Cancel widgets** (action ids **35** and **36**), at dst `(42, 325)` and
> `(112, 325)` on the create-plate row art (V=1004). Those two widgets carry ± stepper artwork but
> functionally are the create-name-modal Confirm and Cancel buttons. The real roster action row is the
> **Create = 4 / Delete = 5 / Enter = 6** buttons at dst-Y **112**. Do not treat 413 / 531 as ids, and
> do not bind the `(42, 325)` / `(112, 325)` widgets to the Create or Delete buttons — they are
> Confirm (35) and Cancel (36).

> **CYCLE 6b CORRECTION (2026-06-20) -- the per-slot info row + the four selectable-gated slot buttons
> (CODE-CONFIRMED, build 263bd994).** `// confirmed: static IDA 2026-06-20`
> - **Shared info row = exactly 3 labels.** The single shared left character-info panel's per-slot text
>   is a contiguous **3-label block**, refreshed for the currently-selected slot: (1) name, (2) level,
>   (3) **position**, formatted as the literal `"%d , %d"` over the two world-position floats (descriptor
>   +0xA0 / +0xA4) truncated to int. **There is NO class label on the info row** -- earlier notes that
>   implied a class line are corrected. The class value drives the 3D preview visual only.
> - **The 10-cell stat grid is NOT fed from the wire 96-byte stats block.** On the select screen the
>   grid is the create-form point-buy control (action ids 25..34, see above) and its glyphs are
>   build-time literals; the per-slot 96-byte stats block parsed by `3/1` is carried through and only
>   consumed by the **in-game** Character-info window (8.7) after Enter. Do not populate a select-screen
>   stat readout from the wire stats block.
> - **Four selectable-gated slot buttons (action ids 4 / 5 / 6 / 61).** Adjacent to the 3-label block,
>   the window holds four 3-state buttons gated by a **per-slot selectable byte** (the server-supplied
>   per-slot flag, `frontend_scenes.md §3.4`). Default after build: the locked base button (id 4) and the
>   highlight art (id 61) shown; the two enter/confirm buttons (ids 5, 6) hidden. When the slot is
>   selectable the polarity flips: {id 61, 5, 6 shown; id 4 hidden}; not-selectable shows {id 4} only.
>   A freshly created character forces its selectable byte clear until the server resolves location, so a
>   just-created slot first shows the locked state. (The "61..74 = per-slot / stat-grid actions" row in
>   the action map above is refined by this: ids 4/5/6/61 are the per-slot button quartet; the role names
>   -- locked base / enter-confirm / highlight art -- are inferred from action-id + atlas + show/hide
>   polarity, the visibility behaviour is confirmed.)

**Slot hit-test is a 3D ray-pick — NOT 2D rects (CODE-CONFIRMED).** Selecting one of the five
character preview actors is a **3D camera-unproject ray test against a per-slot axis-aligned
bounding box**, not a 2D rectangle hit-test and not a viewport-column partition. The click pixel is
unprojected through the perspective camera into a world ray; that ray is intersected (3-axis slab
test) against one small box per preview slot. Each box is centered on its character's world X/Z
(half-extent ±6 in X and Z), with a fixed vertical band (Y 70…92); because the five slots are 12
units apart in X and each box is ±6 wide, the boxes tile edge-to-edge with no gap/overlap. The first
slot whose box the ray hits becomes the selection. See `specs/frontend_scenes.md §3.3.3` for the
camera/ray geometry and the five stage positions; this refines any earlier "2D rect" assumption.

**Lineup placement, ray-pick box, and facing (CODE-CONFIRMED — campaign-frontend re-walk).**
The five preview actors stand at fixed world positions on the select stage:

| Slot | World X | World Y | World Z (shallow bow) | Confidence |
|---|---|---|---|---|
| 0 | 488 | 0 | ≈ −9737 | CODE-CONFIRMED |
| 1 | 500 | 0 | ≈ −9738 | CODE-CONFIRMED |
| 2 | 512 | 0 | ≈ −9738.5 | CODE-CONFIRMED |
| 3 | 524 | 0 | ≈ −9738 | CODE-CONFIRMED |
| 4 | 536 | 0 | ≈ −9737 | CODE-CONFIRMED |

- **Slot spacing** is **12.0** in X (centred near X = 512); **lineup scale** is **70.0**.
- **Per-slot facing:** a per-slot facing byte selects the actor yaw — **yaw = π when the byte is 1,
  else yaw = 0** (front-facing, +Z forward). The default lineup faces front.
- **Ray-pick box (refines §8.2 above):** each slot's box has **half-extents ±6 in X and ±6 in Z**
  (centred on the actor's X/Z) and a **fixed world-Y band [70.0, 92.0]** (the Y band is *not*
  actor-centred). The first box the unprojected click ray hits wins; because the boxes are ±6 wide and
  the slots are 12 apart, they tile edge-to-edge with no gap or overlap.
- These world-absolute placements are the campaign-frontend re-walk view; the camera/ray geometry and
  any mesh-local stage offsets are owned by `specs/frontend_scenes.md §3.3.3` and must not be restated
  here (this spec records only the values the select-window build/hit-test code uses directly).

**Class button → class enum (CODE-CONFIRMED).** The four create-form class buttons (command ids
10/11/12/13, left-to-right → selector index 0/1/2/3, §8.2 above) map to the playable classes as:

| Button index | Class (enum value) |
|---|---|
| 0 | Monk (4) |
| 1 | Musa (1) |
| 2 | Dosa (3) |
| 3 | Salsu (2) |

So the left-to-right button order is **Monk / Musa / Dosa / Salsu** with enum values **4 / 1 / 3 / 2**.
The class-name caption is set from msg.xdb ids **14003…14007**; the per-class description text comes
from `data/script/npc.scr` keys **1…4** (loaded into the keyed node map at boot; the class-select
handler assigns three description labels per class).

**Create-preview actor placement (CODE-CONFIRMED — ACTOR-ONLY, no camera move).** Entering the create
sub-form does **not** move, dolly, or re-aim the camera in this build (no boom/FOV/near/far write on any
create path). Instead the single create-preview actor is placed **≈ 56.5 units nearer** the lineup
centre (toward the camera, in world −Z) and **scaled 81.0** versus the lineup's **70.0** (ratio 81/70).
Reimplementations should keep one fixed camera and move/scale only the preview actor.

**Name validation message ids and bound (CODE-CONFIRMED — extends the create-form validation note).**
On create-confirm the client validates the name before sending: **min length 2**; allowed bytes are
lowercase ASCII (`a`–`z`) and digits (`0`–`9`) — **no uppercase, no spaces** — or valid CP949 lead+trail
double-byte pairs; a banned-word filter; no client-side duplicate check. The failure paths raise these
msg.xdb ids:

| Condition | msg.xdb id |
|---|---|
| Empty / `@BLANK@` name | 2190 |
| Banned word match | 2075 |
| Charset or length violation | 12012 |

The name field is bounded to **17 bytes including the NUL terminator (16 payload bytes)**. On a clean
validation the name is staged and the create-character request is sent (wire layout owned by
`specs/login_flow.md` / packets, not this spec).

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
| Char-select stat-icon grid | **10 cells (2 cols × 5 rows)**, base-Y **191**, stride **24**; col 1 x=154, col 2 x=178; each cell 24×16. NORMAL/PRESSED src are **build-time literals identical within a column** — col 1 `(500, 770)`, col 2 `(524, 770)`; only HOVER src-X differs (col 1 **548**, col 2 **572**, src-Y 770). No per-cell/per-stat src origin, no stat-table read — all static. These cells double as the point-buy ± buttons (action ids 25–34). |
| Char-select stat value labels | Base-Y **193**, stride **24**, 5 rows; x=51, w=35, h=12 |
| Char-select class buttons | NORMAL src-Y **1005**; NORMAL src-X **590/635/680/725**; HOVER src-X **815/860/905** (buttons 1–3); dst-X right-anchored COMPUTED, stride **48** |
| Char-select appearance selector | Base y=30, w=45, h=19; NORMAL src-X base **590** stepping **+45** (590/635/680/725); HOVER base **815** (+45) |
| Char-select Create/Delete/Enter | roster action row, **all at dst-Y 112**, w 59 × h 20: Create (action 4) dst-X 130, Delete (action 5) dst-X 42, Enter (action 6) dst-X 112. src V=**1004** on the create name-plate atlas: Create N(0,1004)/P(59,1004); Delete N(118,1004)/P(177,1004); Enter N(236,1004)/P(295,1004) |
| Char-select create-form Confirm/Cancel | two widgets at dst-Y **325**, w 59 × h 20: Confirm (action 35) dst-X 42 — N/P(354,1004), HOVER src-X 413; Cancel (action 36) dst-X 112 — N/P(472,1004), HOVER src-X 531. Same create-plate row art (V=1004); these are **not** Create/Delete and **not** a separate ± stepper action family |

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

**Provenance of this table (re-confirmed 2026-06-17, 263bd994).** The integer `uitex.txt` ids each
builder uses are **CODE-CONFIRMED** (read directly from the in-game builders). The **id→filename
mapping is asset ground truth** — it is read from the shipped `data/ui/uitex.txt` manifest in the
VFS (~37 rows), **not** from the client binary. The binary's literal string pool only carries a few
of these DDS names (e.g. `skillwindow.dds`); the rest are resolved purely by integer id through the
runtime `uitex.txt` registry. **A filename's absence from the binary string pool is therefore NOT a
drift** — the manifest is the authority for the name. (This settles a prior apparent conflict over
`skill_window_1.dds` and `skillpipe_02.dds`: both are absent from the binary string pool but present
in the manifest at ids 3 and 11, so the spec filenames are correct, manifest-driven values.)

The authoritative id→DDS dimension/format table lives in `formats/ui_manifests.md`. The subset below
is the binding used by the windows specified in this section (`*` = manifest-sourced filename;
in-game id-usage is CODE-CONFIRMED):

| `uitex.txt` id | Resolved DDS (manifest ground truth) | In-game windows that bind it (this spec) |
|---|---|---|
| 1 | `data/ui/mainwindow.dds`* | Options Character sub-panel chrome + checkbox glyphs (§8.9); Skill/Status close buttons (§8.8/§8.7); front-end atlas (NOT in-game status chrome) |
| 2 | `data/ui/inventwindow.dds`* | Status-window base panel + **apply** button (§8.7); inventory cells/body (per-instance, §8.10); inventory sort menu (§8.10); Skill-window apply button (§8.8) |
| 3 | `data/ui/skill_window_1.dds`* | Skill-window main backdrop + class/tab/help/footer (§8.8); weapon-enhancement (UpgradeProcessPanel) backdrop |
| 4 | `data/ui/tradekeepwindow.dds`* | Status-window stat-row sprites + stat +/- glyphs (§8.7) |
| 8 | `data/ui/skillwindow.dds`* | Skill-window texture-rebind path (the one skill DDS name carried as a **literal in the binary**) |
| 9 | `data/ui/messagewindow.dds`* | Options 4-tab container tab buttons + Options Apply/Close buttons (§8.9) |
| 10 | `data/ui/skillpipe.dds`* | skill-pipe atlas variant (manifest sibling of id 11) |
| 11 | `data/ui/skillpipe_02.dds`* | Skill-window hotbar / skill-pipe rows — **4 panels** (§8.8) |
| 14 | `data/ui/blacksheet.dds`* (dim/overlay sprites) **and** live 3D-canvas **render target** (preview) — see note | Skill-window `GUCanvas3D` preview rects (§8.8); inventory count/quantity overlay (§8.10) |
| 26 | `data/ui/skillicon/stateicon.dds`* | buff-bar / state-icon atlas (shared 30-slot icon sheet) |
| 41 | `data/ui/map_userpoint.tga`* | minimap player-marker glyph (manifest-listed; the binary draws the marker as a glyph-by-id, it does **not** bind this path as a literal) |
| 69 | `data/ui/yellow.dds`* | inventory cell state glyph (§8.10) |
| 71 | `data/ui/green.dds`* | inventory cell rarity-frame glyph (§8.10) |
| 78 | `data/ui/edge.dds`* | inventory cell highlight glyph (§8.10) |

> **id-14 dual meaning (not a contradiction).** The manifest binds id **14 = `data/ui/blacksheet.dds`**
> — the literal id-14 lookup that builders use for dim/overlay sprites resolves to that file. The
> "live 3D-canvas" the skill/inventory previews draw into is a **runtime render target**, a separate
> in-memory surface, not the manifest's id-14 file. So id 14 has two distinct meanings: the manifest
> `blacksheet.dds` overlay sprite **and** the runtime preview render-target. An implementer should
> treat the preview as a render-to-texture viewport, and bind `blacksheet.dds` for the literal
> id-14 overlay sprites.

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
to a different other-player info popup. See Open item 14.) The **Apply** button is on
`inventwindow.dds` (uitex 2); the **Close** button alone is on `mainwindow.dds` (uitex 1)
(re-confirmed 2026-06-17 — an earlier summary line swapped these two; the widget table below was
already correct). The `five.tga` star strip is loaded standalone, not via `uitex.txt`.

**Decorative base sprites (no action):**

| dst (x, y, w, h) | src (srcX, srcY) | Atlas (uitex → DDS) |
|---|---|---|
| 0, 36, 318, 50 | 0, 683 | 2 → `inventwindow.dds` |
| 0, 85, 318, 625 | 634, 0 | 4 → `tradekeepwindow.dds` |
| 114, 60, 93, 17 | 194, 704 | 4 → `tradekeepwindow.dds` |
| 87, 460, 224, 17 | 400, 627 | 4 → `tradekeepwindow.dds` (divider strip) |

**Stat-row value labels** are text-only (atlas id 0; the value text is filled at runtime from
network character data). They sit on the 318-wide panel at fixed positions in two columns
(left column x ≈ 91–94, right column x ≈ 247–249), most 12 × 12, stepping down the panel. Two
**standalone** description rows are wider (226 × 12, at y 528 and y 555). A separate **looped** block
of four description rows starts at (74, 555) with a **+27** y-stride (y = 555 / 582 / 609 / 636) and
is **250** wide (not 226 — the 226-wide rows are the two standalone rows above). One label — the
(94, 491) left-column row specifically — carries a red colour override (`0xFFFF0000`). Exact
per-label positions are recoverable from the builder; they are not load-bearing for behaviour and
are summarised rather than tabulated row-by-row here.

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
**311** = the stat-icon/avatar button, **312** = Close. (The builder constructs **ten** stat +/-
buttons total — five increment + five decrement — plus the Apply, Close, and stat-icon buttons. An
earlier prose count of "eleven" was an off-by-one; the table above is authoritative at ten +/- rows.)

**Title:** the `StatusPanel` header is set from the shared empty-init scratch string and then filled
at runtime from network character data (player name + class). There is **no fixed `msg.xdb` title
id** for this window. The static stat *names* (as opposed to the title) come from a different draw
path and use `msg.xdb` ids in the **60001–60022** range (the full contiguous stat-name id table; an
earlier note quoted only the 60005–60022 sub-range).

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
the live skill-effect / character previews; the skill-pipe / hotbar assignment rows are **4 panels**
(re-confirmed 2026-06-17): a loop builds a 242 × 65 panel at x = 163, src (0, 358), on
`skillpipe_02.dds` (uitex 11), with y starting at **50** and stepping **+125** (y = 50, 175, 300,
425 → exactly 4 panels). An earlier version called this a "50-step loop"; the **50** was the loop's
starting Y coordinate, not the iteration count — there are **4** skill-pipe panels, not 50.

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

### 8.10 In-game Inventory bag (`ItemPanel`), gather/craft panel (`GatherSlotPanel`), sort menu (CODE-CONFIRMED)

> **Role correction (2026-06-17, 263bd994).** The in-game item-storage **bag** is built by the
> **`ItemPanel`** family — an 8 × 5 = 40-cell item grid (§8.10.1 below). **`GatherSlotPanel`** is a
> **different** window: the **gathering / crafting progress** panel (three craft rows with animated
> progress bars; on completion it plays UI sound 862100101). An earlier version of this section
> attributed the inventory to `GatherSlotPanel` — that widget table (slot rows 502–504, category row
> 505–507, close 509, minimise 501, sort 508, the (296, Y) 4-row icon table) is **real and all
> CODE-CONFIRMED**, but it is the **GatherSlotPanel** (gather/craft) layout, **not** the item bag. It
> is re-labelled accordingly in §8.10.2.

#### 8.10.1 In-game inventory bag — `ItemPanel` (CODE-CONFIRMED)

The item-storage bag (the `[I]` window) is built by the **`ItemPanel`** family. Its cell grid stride
and count are **byte-confirmed** (recovered three independent ways: the main grid builder's nested
8 × 5 loop; a coordinate-table seeder with the same 8 × 5 / +38 lattice; and the 318 × 623 backdrop
body). **This closes the long-standing campaign-12 "PLAUSIBLE grid" / untraced-bind open item
(Open item 16) — now CODE-CONFIRMED.**

**Main item grid:**

- **8 columns × 5 rows = 40 cells.** Each cell is a **38 × 38 px** square button.
- **Cell pitch is +38 px on both axes** — cells are flush, no gutter.
- **Cell action-ids run 0..39** (a second 8 × 5 loop parents the 40 cells using the running flat
  index as the action id).
- Each cell is **multi-layered**, all sharing the cell's (x, y): a **38 × 38** icon button, a
  **16 × 21** sub-image, **two 38 × 11** labels (count / durability), **two 38 × 11** overlay images,
  and a **36 × 36** highlight image.
- The grid sits inside a **318 × 623** backdrop body that frames the 8 × 38 = 304-wide /
  5 × 38 = 190-tall cell field plus the header.

**Equip / quick sub-grid:** a separate builder lays **20 cells** (38 × 38 buttons), parented with
action ids **50..69**.

**Equipment paperdoll:** a **hand-placed per-slot** block of equip-slot positions — explicit
per-slot (x, y) coordinates, **not** a uniform grid.

**Per-instance atlas bind (now traced):** the window's own atlas, `data/ui/InventWindow.dds`, is
registered into the window's per-window texture list (the `GUTextureList` holder at window offset
+0x220) at the top of the in-game HUD panel builder. The cells then resolve their texture handles by
integer `uitex.txt` id — **2** (inventory body + cells), **14** (count/quantity overlay; the
render-target/`blacksheet` family, see §8.6.1), and **69 / 71 / 78** (cell state / rarity-frame /
highlight glyphs). See §8.6.1 for the id→DDS resolution.

**Hotkey toggle:** the `[I]` hotkey toggles the bag and the skill panel **together** — it toggles the
HUD master service slots **158** (inventory) and **159** (skill panel) and plays UI sound **862020102**.

#### 8.10.2 Gather / craft progress panel — `GatherSlotPanel` (CODE-CONFIRMED)

The gathering/crafting progress window (three craft slot-rows with animated progress bars; on
completion it plays UI sound **862100101**). This is the layout previously mis-attributed to the
inventory bag. This builder binds **texture id 0** for every widget — the slot/icon atlas is assigned
to the panel *per instance* after the build runs (the slot sprites read their frame origins from
panel fields pre-seeded to (296, 0), (296, 64), (296, 128), (296, 192) — a 4-row icon table,
resolving to the per-instance atlas at runtime). Recovered geometry:

- Three slot-row buttons (65 × 64), x stepping **+84** from 32, y = 96, action ids **502, 503, 504**.
- Full-window base backdrop sprite (0, 0, panelWidth, panelHeight).
- Header / divider sprites at (21, 174, 253, 34) src (0, 372) and (33, 189, 239, 8) src (0, 407).
- Close button at (panelW − 13, 2, 11, 12) src (410, 0) / (399, 0), action **509**; minimise button
  at (panelW − 26, 2, 11, 12) src (388, 0) / (377, 0) pressed, action **501**.
- Sort/menu button at (114, 25, 66, 19) src (0, 416), action **508**.
- A looped row of **3** slot-category buttons (80 × 31, x stepping **+84** from 26 → x = 26 / 110 /
  194, y = 207, action ids **505 / 506 / 507**) with paired 40 × 23 labels, and two footer labels
  (104 × 21) at y = 344 (x = 37 and x = 181).

#### 8.10.3 Inventory sort menu — `InvenSortPanel` (CODE-CONFIRMED)

The sort menu panel (`InvenSortPanel`) binds **`inventwindow.dds` (uitex 2)** and uses caption
`msg.xdb` ids **37101** (sort-inventory / "행낭 정리"), **37107**, and **37108** (sort options).

**Title:** the bag/gather builders make **no `msg.xdb` title call**. The window title is therefore
**baked into the `inventwindow.dds` chrome art (the panel edge region)**, not fetched as a caption
(CODE-CONFIRMED absence of a title lookup; the baked-into-art conclusion is PLAUSIBLE). The
associated label strings are the sort-menu captions (**msg 37101 / 37107 / 37108**) and the hotkey
toggle label (**msg 25017**, "show inventory window"); none of these is the window title itself.

### 8.11 In-game window title `msg.xdb` ids — summary (CODE-CONFIRMED ids)

| Window | Title source | Grade |
|---|---|---|
| Skill window (`SkillPanel`) | `msg.xdb` **3027** (title label at 133, 618, 400 × 20) | CODE-CONFIRMED (id); CP949 text VFS-only |
| Inventory bag (`ItemPanel`) | **no `msg.xdb` title** — baked into `inventwindow.dds` edge art; sort labels msg 37101 / 37107 / 37108, toggle msg 25017 | CODE-CONFIRMED (absence) / PLAUSIBLE (baked-in-art) |
| Character-info / Stat window (`StatusPanel`) | runtime player name (empty-init scratch buffer); static stat *names* msg 60001–60022 | CODE-CONFIRMED |
| Options window (`OptionPanel`) | tabs are sprite-only (no title caption); Character-tab option labels msg 8009–8039 | CODE-CONFIRMED |

> All `msg.xdb` ids above are CODE-CONFIRMED (read from the builders); the in-game window protocol
> behaviour is static and **CAPTURE-UNVERIFIED** (no Wireshark oracle). The CP949 caption *text* for
> each id is VFS-only and must be supplied from a `msg.xdb` extract (`formats/misc_data.md §6`).


### 8.17 In-game window-OPEN dispatch + bottom command strip (CODE-CONFIRMED)

There is **no dedicated toolbar / command-bar class**. Every in-game window is OPENED by a single
**global action-id dispatcher** owned by the master HUD window (`MainWindow`, ctor-named
`MainMaster`). The dispatcher reads an integer action id from the raised event and routes it to a
family of per-window toggle handlers; each handler reads **one fixed master service slot** (the
`+0x238` service table of §0.1 in `ui_hud_layout.md`), and flips that panel's visible flag. **No
in-game window is reconstructed on open** — the open buttons/keys only toggle an already-built
panel's visibility, close any conflicting panel group, and play UI click sound **862020102**
(category 2). Most handlers are gated by a global input-blocked / cinematic flag (no window opens
while it is set).

**Action ids ARE ASCII keycodes.** By construction a button's stored action id is the same integer as
its default hotkey's keycode — this is *why* a toolbar button and a keyboard key open the same window
(both raise the same integer). The factory key chars below are therefore the action ids read as ASCII.

**Hotkeys are remappable** through the per-account INI section **`[<account>_KEYSET]`** (the format
string is `%s_KEYSET`). Each command's key is stored as a single ASCII character (`%c`). So the
default action-ids below are the **factory** key chars; a user can rebind them. This sits alongside
the position-persistence INI of §8.6.

**Two distinct dispatch surfaces reach the SAME slots.** The persistent ASCII-keycode toolbar /
hotkeys (this section) AND the `DefaultMenu` radial popup (its own internal action ids, ~4000–4024 —
see below) are **two separate input surfaces** that both toggle the same `MainWindow` service slots.
An implementer should wire each open-button to the **target slot**, not to one id space.

#### 8.17.1 ASCII-keycode action → window → service slot → default hotkey (CODE-CONFIRMED unless noted)

Each handler reads master service slot `N` and toggles that panel's visibility. Default hotkey char =
the action id as ASCII (rebindable via `[<account>_KEYSET]`).

| Action (dec / ASCII) | Opened / toggled window | Service slot N | Default hotkey | Conf |
|---|---|---:|---|---|
| 97 `a` | Battle / attack-mode toggle (combat-mode net send; **not a window**) | 324 | `a` | CODE-CONFIRMED |
| 98 `b` | Inventory bag (`ItemPanel`) — opens, closes conflicting panels | 158 | `b` | CODE-CONFIRMED |
| 103 `g` | Default command MENU (`DefaultMenu`) — the radial quick-menu that fans out to the others | 191 | `g` | CODE-CONFIRMED |
| 104 `h` | Help window (`HelpPanel`, literal `data/ui/help.dds`) | 322 (+ button slot 176) | `h` | CODE-CONFIRMED |
| 105 `i` | Inventory open (group-toggle; gates on status slot) | 146 / 158 group | `i` | CODE-CONFIRMED |
| 106 `j` | GuildWarInfo window (`GuildWarInfoPanel`) | 224 | `j` | CODE-CONFIRMED |
| 107 `k` | Party open (`PartyPanel`) + close-others | 220 | `k` | CODE-CONFIRMED |
| 108 `l` | StallList window (`StallListPanel`, personal-shop list) | 228 | `l` | CODE-CONFIRMED |
| 109 `m` | Toggle panel at slot 161 (chat / quick-slot family; class not yet RTTI'd) | 161 | `m` | fn CODE-CONFIRMED; slot-161 class debugger-pending |
| 110 `n` | Pet window (gated on scene-state ≠ 11) | **UNRESOLVED — debugger-pending; NOT slot 230** | `n` | fn CODE-CONFIRMED; class + slot UNVERIFIED |
| 111 `o` | Toggle panel at slot 164 (group-gated) | 164 | `o` | fn CODE-CONFIRMED; slot-164 class debugger-pending |
| 112 `p` | Close-all / escape (hides the full HUD panel group) | 185/186 + group | `p` | CODE-CONFIRMED |
| 113 `q` | Quest open (`QuestPanel`) + close-all + UI transition | 206 + group | `q` | CODE-CONFIRMED |
| 115 `s` | Skill window (`SkillPanel`) toggle group | 159 + group | `s` | CODE-CONFIRMED |
| 117 `u` | BroodWarList / PC-info window (`BroodWarListPanel`) | 235 | `u` | CODE-CONFIRMED |
| 119 `w` | Toggle slot-223 panel (war/guild-info family; class not RTTI-named) | 223 | `w` | fn + slot CODE-CONFIRMED; class debugger-pending |
| 120 `x` | Sit / rest (combat net send; gates on a map flag; **not a window**) | — | `x` | CODE-CONFIRMED |
| 121 `y` | Toggle HUD frame-group hide/show (UI declutter; **not a single window**) | dock + frames | `y` | CODE-CONFIRMED |
| 122 `z` | Trade / party-trade request (INI key name `BATTLE_MODE`; **not a window**) | — | `z` | CODE-CONFIRMED |
| 27 `Esc` | Close inventory group / collapse dock-frame group | 146 + group | `Esc` | CODE-CONFIRMED |
| 32 `Space` | Help close + toggle the help button state | 322 / 176 | `Space` | CODE-CONFIRMED |
| 99 | Chat input commit / open chat line (`ChatPanel` input box) | chat-input slot | `Enter` | CODE-CONFIRMED |

> The per-button strip source rects (NORMAL / HOVER / PRESSED) and the exact dst positions of the
> individual bottom-strip buttons were **not** individually decoded (the HUD builder is very large and
> the strip buttons interleave with many other sites) — **debugger-pending**. Which action ids have a
> *physical* toolbar button (vs being key-only or menu-only) is likewise debugger-pending, because the
> dispatcher is global. Slots **161 / 164 / 223** classes are debugger-pending.

#### 8.17.2 Authoritative service-slot → window identities (CODE-CONFIRMED)

These come from the named, class-specific toggle handlers the dispatcher calls (NOT from build-routine
call order, which interleaves construction):

| Slot N | Window (class) |
|---:|---|
| 146 | `StatusPanel` (character-info / stat) |
| 147 | dock-slide panel (toolbar dock / link-bar) |
| 158 | `ItemPanel` (inventory bag) |
| 159 | `SkillPanel` (skill window) |
| 185 | buddy / relation panel |
| 186 | `MiniParty` (compact party widget) |
| 191 | `DefaultMenu` (radial quick-menu; no RTTI name) |
| 193 | `RelationPanel` |
| 206 | `QuestPanel` (quest tracker) |
| 207 | `GuildAPanel` (guild window) |
| 220 | `PartyPanel` (party window) |
| 224 | `GuildWarInfoPanel` |
| 228 | `StallListPanel` (personal-shop list) |
| 230 | `ProductPanel` (production / crafting — see §8.18) |
| 235 | `BroodWarListPanel` |
| 240 | warstone panel |
| 322 | `HelpPanel` (literal `data/ui/help.dds`) |

#### 8.17.3 DefaultMenu radial action map (4000–4024) (CODE-CONFIRMED)

The `DefaultMenu` radial popup (slot 191, opened by `g`) has its **own** internal action ids in the
4000-range, routed in its event handler. These are **not** ASCII keycodes — they are the radial
menu's own button ids. Each case toggles a `MainWindow` service slot (mutually-exclusively closing
sibling panels), so the two id spaces converge on the same slots:

| DefaultMenu action | Opens / toggles | Service slot |
|---:|---|---:|
| 4000 | battle-stance group expand/collapse (the radial itself) | self |
| 4001 | Inventory (`ItemPanel`) | 158 |
| 4002 | Buddy / relation panel | 185 |
| 4003 | Skill (`SkillPanel`) | 159 |
| 4004 / 4024 | Quest tracker (`QuestPanel`) | 206 |
| 4007 | chat-input gated action | 202 |
| 4008 | relation / focus-gated panel | 193 |
| 4009 | panel via slot 161 (`m` candidate) | 161 |
| 4010 | panel via slot 164 (`o` candidate) | 164 |
| 4011 | Help toggle (checks slot 322; presses button slot 176) | 322 / 176 |
| 4012 | Party (`PartyPanel`) | 220 |
| 4013 | `ProductPanel` (production / crafting), billing-gated, blocked in scene-state 11 | 230 |
| 4014 / 4015 | combat / attack-stance net toggle (OFF / ON) | 324 / 328 |
| 4022 | warstone / slot-240 panel | 240 |
| (also) | Guild panel | 207 |
| (also) | dock-slide | 147 |

> Several DefaultMenu cases (4005, 4006, 4019–4021, 4023) are mode toggles / quick-slot helpers that
> open no top-level window. Slots **161 / 164 / 223** classes remain debugger-pending.

#### 8.17.4 Bottom command-strip placement (CODE-CONFIRMED formula)

The literal bottom button strip is built inside the HUD-build routine. Placement (matching
`ui_hud_layout.md §5.7`): **X = centerX(1024), Y = screen_height − 60, W = 1024, H = 60,
innerY = 957** (mixed: centred-X + bottom-anchored). The `DefaultMenu` quick-menu (slot 191) is built
in the bottom area (build height arg 220) and is the closest thing to a command-bar host. Absolute
pixels depend on the runtime screen-size globals — **capture/debugger-pending** (same caveat as
`ui_hud_layout.md §5.11`).

> **Failure captions.** Handlers that fail to open broadcast a `msg.xdb` notice string (e.g. ids
> **2064**, **45003**, **2227**, **54094**, **59004**, **29017** seen on busy / cannot-open paths).
> These are failure notices, not per-button tooltips. A per-button hover-tooltip id table was **not**
> recovered — debugger-pending.

### 8.18 In-game ProductPanel — NPC production / crafting window (CODE-CONFIRMED role)

`ProductPanel` (method-label family `GoodsPanel`) is the **NPC PRODUCTION / CRAFTING (item-make)**
panel — **not** a buy/sell vendor. (The buy/sell vendor is the distinct `KeepNpcPanel` family, which
pairs with the shop sell/buy opcodes; do not conflate the two.) It carries a recipe grid, a quantity
textbox, a 3D item preview, recipe-detail and item-comparison sub-panels, a paged order list, a
web-link button, and a location-gated production-collect action. It lives at **master service slot
230**. Reference canvas 1024×768; child rects below are panel-local (top-left origin, +X right, +Y
down). The root window's absolute origin is set by the master-window machinery — debugger-pending.

#### 8.18.1 Atlas binding (CODE-CONFIRMED)

The panel binds a multi-DDS skin set on its texture handle (children inherit the bound sheet; most
build sites pass texture id 0 so the sprite inherits the window's bound atlas):

| Atlas (logical) | DDS file | Used for |
|---|---|---|
| itemshop | `data/ui/itemshop.dds` | main window chrome / list backdrop |
| product | `data/ui/product.dds` | recipe grid cells, buttons |
| itemshoppopup | `data/ui/itemshoppopup.dds` | popup / detail sub-panels |
| buywindow | `data/ui/buywindow.dds` | order / confirm sub-window chrome |

> `data/ui/productnpc.dds` is a **distinct, simpler sibling panel** (a single-atlas NPC-side
> production storefront / entry that leads into this full panel) — not the same class. Role MED.

#### 8.18.2 Recipe grid + sub-panels (CODE-CONFIRMED)

- **8 recipe cells** in a **4-column × 2-row** arrangement, at X = `{29, 212, 395, 578}` and Y rows
  `{172, 364}` (panel-local). Each cell = a frame button + a name label + two value labels + an icon
  image + a make button. This is a **recipe / order grid**, not a buy-side/sell-side split — the
  player picks a recipe, sets a quantity, and submits a production order.
- Each recipe row is a sub-record of **40-byte (0x28) stride** inside the panel object, holding three
  label pointers (class / price / count), a buy button, and an icon. A per-recipe status word selects
  one of three production-state captions (`msg.xdb` 714 / 729 / 744).
- **Quantity textbox** (action 70, max length 16) with +/- adjustment.
- **Price label** fed by `msg.xdb` 45002, re-driven each repaint from the player's gold; tinted red
  when the cost is unaffordable, with a ×0.8 discount branch. **Have-count** via `msg.xdb` 45004.
- **Page label** = current-page / total-page (`"%s / %s"`).
- Notable sub-panel container rects (panel-local): recipe-detail `(280,250) 200×150` and
  `(310,359) 120×150`; scrollbar column `(430,359) 17×150`; item-preview / order panel
  `(20,0..) 781×630`; item-detail sub-panel `(160,243) 512×164`; comparison panel `(229,233) 339×190`;
  a hidden slide-in tip panel `(202,−227) 426×227`.

#### 8.18.3 Action-id map (CODE-CONFIRMED)

All actions are routed in the panel's event handler on click-release, reading the active child's
action id:

| Action id | Widget | Behaviour |
|---|---|---|
| 0..7 | per recipe-cell select (8 cells) | select recipe slot, fill detail labels |
| 8..15 | per recipe-cell buy/make (low) | add / queue recipe (mode 1) |
| 16 | bottom close button | close detail / close window |
| 17 / 18 | up / down arrow | scroll list up / down |
| 19 | 6 category tab buttons | select category tab / KIND → rebuild list |
| 25..34 | 10 page-jump buttons | jump to page index |
| 35 | next-page | page +1 (bounded by total pages) |
| 36 | make / confirm button | open / commit production order |
| 45..52 | per recipe-cell action (high) | alt add (mode 2) |
| 53 | order-window close | hide order sub-panel |
| 54 | detail-panel toggle | show recipe list area |
| 55 | scrollbar host | scroll-thumb track |
| 56 / 57 / 58 | scroll up / track / down (sub-panel) | scroll the order list |
| 60..69 | 30 order-list rows | pick a queued order row |
| 70 | quantity textbox | focus the qty editbox (IME) |
| 72 | preview-panel close | hide 3D preview / order panel |
| 73 | preview canvas | refresh 3D item preview |
| 74 / 75 | order +/- buttons | adjust order amount |
| 76 / 77 | compare-panel buttons | item-compare toggles |
| 78 | compare confirm | apply comparison |
| 79..86 | 8 per-cell aux buttons | production-collect (area-1 gated) |
| 88 | web-link button | open external `http` URL in browser |
| 89 | tip-panel prev | slide tip panel |
| 90 | tip-panel confirm | stage confirm; emit C2S production request (selector value 200) |

> Source-rect origins on the build sites (e.g. tab buttons, per-row +/-/buy buttons, order-confirm,
> arrow nav, item-detail and compare buttons) are recorded as the build-site immediates against the
> bound atlas; the exact per-glyph src table is left to a focused build-site sweep — debugger-pending.

#### 8.18.4 Captions (CODE-CONFIRMED ids; CP949 text VFS-only)

`msg.xdb` ids used: **45002** (price integer format), **45004** (have-count / quantity),
**45011..45014** (item-detail field labels), **45017 / 45019** (production-collect error notices —
area-1 gate, count gate), **45026** (order-confirm prompt), **45031 / 45032** (compare / order-button
captions), **45101** (right-side button caption), **714 / 729 / 744** (per-recipe production-state
captions), **103** (generic Cancel/OK on the tip-panel button).

#### 8.18.5 Open mechanism + network (CODE-CONFIRMED)

The panel is a persistent HUD child **toggled from the `DefaultMenu` radial** (action **4013**:
billing-check, then toggle slot 230) and from a keybind path (gated by scene-state ≠ 11 and a billing
check; `msg.xdb` 45003 on failure). It is **not** opened by a dedicated NPC opcode.

- On open it rebinds textures, shows, populates, and emits **C2S `CmsgProductBuy` (2/151)** with a
  **1-byte selector body = 0** to request the current production list / money.
- Action 90 emits the same **2/151** channel with selector body **200 (0xC8)**.
- All page-nav and tab changes are **local repaints** of a cached recipe list (filtered by the
  selected tab / KIND).
- The recipe list refresh is driven by inbound **S2C `SmsgShopPageUpdate` (3/8)**, which carries a
  single 4-byte money value: the handler stores the player's gold and repaints the active panel (it
  does NOT carry a grid/page block). Capture-unverified (no capture this lane); the money semantic is
  static-confirmed from the consumer that reads the same field.
- Production-collect (actions 79..86) is gated to **area id == 1** (`msg.xdb` 45017 otherwise) and to a
  non-zero collect count (`msg.xdb` 45019).

> Wire field tables for `CmsgProductBuy` (2/151, 1-byte selector) and `SmsgShopPageUpdate` (3/8, 4-byte
> money) are owned by the protocol spec — see `opcodes.md` / `packets/`. **This panel's sole network
> send is 2/151; it never emits 2/152** (2/152 is the QuestPanel row-request, a different consumer).

### 8.19 In-game EmoticonPanel — emote / chat-emoticon picker (CODE-CONFIRMED)

`EmoticonPanel` is the in-game character-emote / chat-emoticon SELECTOR — a right-dock 318-column
window with two tabbed grids: a **graphical over-head emoticon-balloon grid** and a **text-macro
("chat shortcut") grid**. The panel object is **432 bytes (0x1B0)**; it is built once by the HUD-build
routine and stored at master window field **+0x370** (it is a persistent HUD child, not per-open
construction). Reference canvas 1024×768; child rects are panel-local.

#### 8.19.1 Geometry (CODE-CONFIRMED formula)

| Property | Value | Notes |
|---|---|---|
| Texture | none (0) | Container; visible art is on its children |
| Anchor X | **screen_width − 318** | Right-dock 318-column family (same X-formula as the inventory rail) |
| Anchor Y | **0** | Top-flush |
| Width W | **318** | |
| Height H | **732** | |

> The same right-dock 318×732 rail as `ui_hud_layout.md §5.3`. Absolute pixel X depends on the runtime
> screen-width global — debugger-pending; the formula is CODE-CONFIRMED.

#### 8.19.2 Atlas binding — uitex integer ids (CODE-CONFIRMED ids; DDS names VFS-only)

The panel binds no literal `emoticon.dds` name; every image/button uses a `uitex.txt` **integer id**
resolved at build time through the in-game uitex registry (§8.6.1; fallback to id 1). Ids used:

| `uitex.txt` id | Use in this panel |
|---:|---|
| 1 | close-button frames; uitex default-fallback |
| 2 | top-bar strip; bottom button |
| 3 | page-1 emote-cell backgrounds + the 29×29 status overlay |
| 4 | header icon; both tab buttons; page-0 button frames |
| 8 | main window backdrop (318×627 at src 318,0) |
| 27 | over-head emote-balloon glyph atlas (page-1 emote-icon buttons + the balloon display); page-0 label images |

> The concrete DDS behind each id resolves only via the runtime `data/ui/uitex.txt` manifest
> (`formats/ui_manifests.md`) — VFS-pending for ids 3 / 8 / 27.

#### 8.19.3 Chrome children (CODE-CONFIRMED)

| Child | dst (x,y) | w×h | src (x,y) | atlas id | action |
|---|---|---|---|---|---|
| Background image | (0, 85) | 318 × 627 | (318, 0) | 8 | — |
| Top-bar image | (0, 36) | 318 × 50 | (0, 683) | 2 | — |
| Header icon | (125, 60) | 69 × 16 | (921, 669) | 4 | — |
| Close button (3-state) | (286, 46) | 29 × 26 | up/over (354,596) down (354,622) | 1 | **204** (press → `msg.xdb` 16011) |
| Grid page panel 0 (text-macro container) | (0, 127) | 318 × 605 | — | — | — |
| Grid page panel 1 (graphical container) | (0, 127) | 318 × 605 | — | — | — |
| Tab button 0 (3-state) | (10, 96) | 149 × 29 | (677,694)/(677,724) | 4 | **200** (page 0) |
| Tab button 1 (3-state) | (159, 96) | 149 × 29 | (677,754)/(677,784) | 4 | **201** (page 1) |
| Bottom button (3-state) | (259, 655) | 59 × 77 | up/over (301,947) down (360,947) | 2 | **203** (toggle/close) |
| Input textbox | (60, 80) | 297 × 23 | (518, 669) | — | **202** (max len 47; IME-disabled) |

#### 8.19.4 The two pages (CODE-CONFIRMED)

The active page index is stored in the panel object; a tab switcher hides the old grid, shows the new,
and swaps the two tab buttons' frame origins.

- **Page 0 — text-macro ("chat shortcut") grid.** Up to 9 macro slots; each cell = a 3-state button
  (92×29, src 827,668 / 827,697), a label image (87×13), and a wide phrase button (297×23, src
  518,669). The 9 phrase strings come from a per-class INI section `%s_CHATSHORTCUT`, keys
  `SHIFT_1..SHIFT_9`. Picking a page-0 macro **submits the phrase through the chat pipeline** — it is
  literally typed-and-sent as chat text (reference: chat C2S 2/7 with a channel byte). Rate-limited to
  one per 5000 ms.
- **Page 1 — graphical emoticon grid.** Iterates `emoticon.do`, filtering records by the page byte.
  Per matched record, four sub-widgets are built at the record's own base XY (the grid is **data-driven
  per-record coordinates, NOT a fixed rows×cols pitch**):

| Widget | dst offset from cell base | w×h | src (x,y) | atlas id |
|---|---|---|---|---|
| Cell background image | (+0, +0) | 146 × 49 | (63, 661) | 3 |
| Emote icon button (3-state) | (+23, +11) | 23 × 23 | per-record icon src | 27 |
| Label image | (+48, +16) | 87 × 13 | per-record label src | 27 |
| Status overlay image | (+20, +8) | 29 × 29 | (763, 655) | 3 |

The bound action id is a per-record field; the emote id actually played is a separate per-record field
(see §8.19.6). Up to 3 logical pages exist in the data; the panel exposes 2 tabs (click handler gates
page ≤ 2 and action < 200).

#### 8.19.5 Data tables — row schemas (layouts CODE-CONFIRMED; field meanings CODE-INFERRED)

> `data/script/emoticon.do` warrants its own `formats/` spec (the binary record below is the
> spec-author summary; meanings are debugger/VFS-pending against real bytes).

- **`data/script/emoticon.do`** — binary, **fixed 40-byte (0x28) records, no count header** (read to
  EOF), inserted into two lookup trees (one keyed on field +0, one on field +8):

| Offset | Size | Field (working name) |
|---:|---:|---|
| +0x00 | 4 | primary key |
| +0x04 | 1 | page / category byte (pages 0..2) |
| +0x08 | 4 | action id (bound to the icon button; < 200) |
| +0x0C | 4 | emote / animation id (the value PLAYED) |
| +0x10 | 4 | cell base X |
| +0x14 | 4 | cell base Y |
| +0x18 | 4 | icon button src X |
| +0x1C | 4 | icon button src Y |
| +0x20 | 4 | label image src X |
| +0x24 | 4 | label image src Y |

- **`data/char/emoticon.txt`** — text, count-headed, **76-byte stride per id** (id is the array
  index, must be < 47). Per row: `id` (int32), a name/text string (CP949), value A (int32), value B
  (int32), array A[4] (int32), array B[4] (int32). This is the per-emote actor-animation table (the
  two int[4] arrays are candidate motion / timing ids); it is loaded by the char-manifest path, not by
  the panel.
- **`data/char/sameemoticon.txt`** — text, count-headed, **32-byte alias records** (int32 key + a
  string); maps an emote id → a related text/group ("same emoticon" aliasing). Loaded by the boot
  data-table corpus.

#### 8.19.6 How an emote is sent (CODE-CONFIRMED)

- **Page-0 text macro → sent as CHAT.** The selected phrase is copied into the chat input line and
  submitted through the normal chat send pipeline (chat C2S 2/7 with a channel byte). No dedicated
  emote opcode; the macro is typed-and-sent chat text.
- **Page-1 graphical emoticon → CLIENT-LOCAL display, NO packet on this path.** The click handler
  validates page ≤ 2 and action < 200, finds the matching `emoticon.do` record, plays sound
  **862030103**, reads the record's emote id, and sets a client-local over-head emote-balloon on the
  master window at service slot **327** (uitex atlas id 27). The balloon setters are pure local UI;
  none calls a network send. **The picker itself sends nothing for graphical emotes.** Whether the
  balloon is also server-broadcast to other players is debugger/capture-pending.

#### 8.19.7 Open / toggle mechanism

Built once and stored at master window field **+0x370**; it is a persistent HUD child. The event
handler toggles/closes: bottom button (action 203) and Esc (key 27) both close. Tab buttons 200/201
switch pages; 202 focuses the textbox. The toolbar action id / hotkey that SHOWS the panel is a
residual — debugger-pending (one of the master action ids that toggles the +0x370 slot's visibility).

### 8.20 In-game MessagePanel — system notice / confirm modal (CODE-CONFIRMED)

`MessagePanel` is a single reusable **screen-centered modal** (a `GUPanel` subclass) with **two
display modes**, at master service slot **190**. It is NOT a scrolling message log; it is a transient
modal that grabs focus and eats Esc. It is built and stored once by the HUD-build routine. Reference
canvas 1024×768; child rects are panel-local.

- **Mode 0 — single-OK notice.** Shows only the centered OK button (action 2); hides the side buttons.
- **Mode 1 — two-button Yes/No confirm.** Shows the left button (action 0, Yes) and right button
  (action 1, No); hides the center OK button.

> **Distinct from its sibling peers.** `ConfirmPanel`, `ReAskPanel`, `BigAlarmPanel`, `AnnouncePanel`,
> `ErrorPanel`, and the PK-penalty alarm panels each have their **own** class, vtable, and ctors — they
> are independent peer classes (all `GUPanel` subclasses), **NOT aliases** of MessagePanel.
> MessagePanel folds confirm into its mode 1; it does not bind the namesake `messagewindow.dds`
> (uitex 9) — see §8.20.2.

#### 8.20.1 Geometry (CODE-CONFIRMED)

- **Centered modal.** X = `(screen_width − 340)/2`, Y = `(screen_height − 340)/2` (the centering box
  uses the literal 340 on both axes). **Width = 340, Height = 190.** Buttons are placed at
  `w/2 ± offset`, `h − 55..60`.
- On the 1024×768 reference canvas: X ≈ 342, Y ≈ 214, size 340×190.

#### 8.20.2 Atlas binding (CODE-CONFIRMED) — does NOT use uitex 9

Every texture is fetched through the in-game `uitex.txt` integer-id registry (§8.6.1):

| Element | `uitex.txt` id | Resolved DDS |
|---|---:|---|
| Panel background / chrome | **2** | `data/ui/inventwindow.dds` |
| Center OK button face | **2** | `data/ui/inventwindow.dds` |
| Left (Yes) + Right (No) button faces | **8** | `data/ui/skillwindow.dds` |

> uitex **9 = `messagewindow.dds`** is the *namesake* file but is bound by the Options/Friend/Quest
> windows (§8.6.1), NOT by MessagePanel.

#### 8.20.3 Widgets, src-rects, actions (CODE-CONFIRMED)

(`w`, `h` = 340 / 190. For all buttons PRESSED src == NORMAL src; only HOVER differs.)

| Child | Type | dst (x, y, w, h) | atlas | NORMAL src | HOVER src | action | mode |
|---|---|---|---|---|---|---|---|
| label0 | GULabel | (0, 50, w, 20) | — (text) | — | — | — | both |
| label1 | GULabel | (0, 80, w, 20) | — (text) | — | — | — | both |
| label2 | GULabel | (0, 110, w, 20) | — (text) | — | — | — | both |
| bodyMulti | GULabel (multi-line) | (0, 50, w, 20) | — (text) | — | — | — | mode 0 (free-string) |
| btnLeft (Yes) | GUButton 3-state | (w/2−120, h−60, 113, 40) | 8 → skillwindow | (660, 984) | (187, 956) | **0** | mode 1 |
| btnRight (No) | GUButton 3-state | (w/2+7, h−60, 113, 40) | 8 → skillwindow | (773, 984) | (886, 984) | **1** | mode 1 |
| btnOK | GUButton 3-state | (w/2−56, h−55, 113, 40) | 2 → inventwindow | (302, 860) | (415, 860) | **2** | mode 0 |

> Panel-background sub-rect on uitex 2 is at src origin ≈ (190, 318) — MED confidence (push-decode);
> confirm against the `inventwindow.dds` atlas.

#### 8.20.4 Action dispatch (CODE-CONFIRMED)

- **Esc** (while visible) → close.
- **Action 1 or 2** (No / OK) → close; if a busy/confirm flag is set, it rebuilds a follow-up confirm
  prompt instead.
- **Action 0** (Yes) → if the stored message code == **103**, run a state-dependent affirmative (e.g.
  return-to-town: plays sound 862020102, resets the skill-bar slot), then close.

#### 8.20.5 Body-text sources (CODE-CONFIRMED) — all CP949

1. **Raw runtime string** — caller passes a C-string; placed in the multi-line label, mode forced to 0.
2. **Canned record from `data/script/msginfo.do`** — fixed **128-byte (0x80) records**, keyed by
   record field +0. Confirmed fields: **+0** key, **+4** button-mode, **+8** primary body string, **+68**
   secondary caption (shown unless the literal "0"). The remaining fields are not decoded — `msginfo.do`
   warrants its own `formats/` spec (residual). `msginfo.do` is distinct from `msg.xdb`.
3. **`msg.xdb` ids formatted by the caller** — e.g. the hotbar/skill-link confirm path uses ids
   **2041** (with a format arg), **16024**, **16025**; the secondary confirm prompt uses **16034**
   (format), **16035** and is gated by a global game-state value ∈ {18, 24, 30, 33, 42}.

#### 8.20.6 Open mechanism (CODE-CONFIRMED static; populate-opcode MED)

MessagePanel is reached as master slot **190** and raised by **client-side** code paths at decision
points (e.g. the hotbar/skill-link confirm). **No inbound S2C system-notice opcode was found
statically routing into MessagePanel** — server-driven notices more likely target `AnnouncePanel` /
`ErrorPanel`. Whether any inbound opcode routes a notice specifically through MessagePanel is a
residual (debugger/capture-pending).

### 8.21 In-game Tender + Carrier-Pigeon mail family (CODE-CONFIRMED)

A family of NPC-service windows for consignment/tender purchase and the carrier-pigeon mail / delivery
system. All are persistent in-game HUD children built once by the HUD-build routine. Reference canvas
1024×768; the screen-centred modals use `center = (screen − size) / 2` on both axes; absolute pixels
depend on the runtime screen-size globals (debugger-pending). Flows are **deferred-confirm**: a panel
action stages data and opens an InfoPanel confirm dialog; only the confirm "Yes" emits the network
send.

> Wire field tables for the opcodes referenced below are owned by the protocol spec — see `opcodes.md`
> / `packets/`. Referenced by canonical name + major/minor only: `CmsgTenderConfirm` (2/118),
> `CmsgCarrierPigeonSend` (2/70), `CmsgDeliveryClaim` (2/71), `CmsgLetterRequest` (2/60), and the
> existing `SmsgSrvLetterReceived` (1/20) arrival notification.

#### 8.21.1 TenderInfoPanel — consignment-purchase / info confirm (CODE-CONFIRMED)

A screen-centred consignment / item-purchase info window at master service slot **118**. It carries a
3D item-preview canvas, a scrollable list, item-stat info labels, and a confirm/purchase button gated
by a gold cost. On confirm it checks the cost against the player's gold and, if affordable, emits
**C2S `CmsgTenderConfirm` (2/118, header-only)**; otherwise it shows caption **66006** (not enough
gold). The detail/info button (action 1) is rate-limited to **30** entries (caption **66007** over the
cap), with detail text built via the shared InfoPanel category builder.

- **Geometry:** X = centerX(512), Y = centerY(595), **W = 512, H = 595**. Atlas `tender_window.dds`.
- **Actions:** buy / confirm **0**; request-detail **1** (30-cap → `msg.xdb` 66007); list scrollbar
  **2**; Esc closes.
- Whether the panel is an auction bid board vs an NPC consignment-buy is a semantic residual
  (debugger/capture-pending). A dedicated S2C tender-listing populate opcode was not isolated.

#### 8.21.2 CarrierPigeonPanal — mailbox menu (CODE-CONFIRMED)

> `CarrierPigeonPanal` is the binary's class name (the misspelling is preserved as the recovered name).

A small mailbox menu at top-left **(0, 0), 140 × 195**, atlas `carrierpigeon.dds`, master service slot
**96**. Three stacked 3-state buttons, all 90 × 25:

| Button | dst (x,y) | src N/H | src P | action | role |
|---|---|---|---|---|---|
| top | (25, 64) | (141,156) | (141,182) | **1** | open mail LIST / receive (opens the ReadPanel for a selected letter) |
| middle | (25, 103) | (141,50) | (141,75) | **2** | open WRITE/SEND panel |
| bottom | (25, 142) | (141,0) | (141,25) | **3** | close |

Esc also closes.

#### 8.21.3 CarrierPigeonSendPanel — compose letter (CODE-CONFIRMED)

A compose sub-window (child off the Panal; positions are parent-relative). Atlases
`CarrierPigeonAll.dds` / `CarrierPigeonPerson.dds`.

- **Recipient-name textbox** (138×16), **max length 16**, focus action 20.
- **Message-body textbox** (265×16), **max length 199**, multiline, focus/edit action 21.
- **Send** 3-state button (90×25), action **1**.
- **Attach-item button** (22×22), action **2**, opening an item-attach list of **up to 30 rows** (each
  row a button, action **6 + row index** = 6..35), with list scrollbar actions **3** (down) / **4**
  (up) / **5** (thumb drag). `TAB` toggles focus between the two textboxes.
- **Send flow (deferred-confirm):** action 1 validates recipient (else `msg.xdb` 52001) and body (else
  52002), and requires gold ≥ **200000** postage (else format the shortfall via 55001/55002/55003 +
  52019), then opens an InfoPanel confirm dialog; the confirm "Yes" emits **C2S
  `CmsgCarrierPigeonSend` (2/70)**. Captions: **52001 / 52002 / 52003 / 52019** and **55001 / 55002 /
  55003**; postage literal **200000**.

#### 8.21.4 CarrierPigeonReadPanel — read received letter (CODE-CONFIRMED)

A view sub-window at master service slot **98**, atlas `CarrierPigeonAll.dds` /
`CarrierPigeonPerson.dds`. It shows sender / date / subject / body labels and an attached-gold / item
panel, with a **reply textbox** (277×25, **max length 199**, multiline, action 2) and an **OK/reply**
button (90×25, action 1). Four tab buttons (Reply / Delete / Next / Prev). Reply/delete net sends
route through the same deferred-confirm dispatcher as send.

#### 8.21.5 DeliveryPanel — consignment / delivery retrieve box (CODE-CONFIRMED)

A large window at master service slot **40**, atlas `delivery.dds` — the **retrieve box** for
delivered/consigned items (and sale proceeds).

- **40-slot item grid (5 × 8):** display cells are action ids **500..539** (one per cell), with
  category-7 item tooltips (same path as inventory tooltips).
- **Page tabs:** actions **573..578** (own / others / sale categories).
- **Scrollbar:** thumb **583**, down **584**, up **585**.
- **Quantity +/-** on the retrieve confirm: **601** (decrement), **602** (increment); a select-all /
  list button **580**.
- Each item record is 16 bytes (item-id, key, two scalars) plus an owner-id field (compared to the
  local player id to decide my-side vs other-side).
- **Claim flow (deferred-confirm):** view-switch (actions 541–548) and claim-prepare (565–572) stage a
  recipient name + up to 5 item records, then open an InfoPanel confirm dialog (own-retrieve vs
  other-side claim); the confirm "Yes" emits **C2S `CmsgDeliveryClaim` (2/71)**. Captions: **40010**,
  **16005** (no content), **55016** (no free bag slots when claiming).

#### 8.21.6 LetterPanel — compact letter view (role MED)

A small letter-view window, atlas `letter.dds`. Its action-1 path emits **C2S `CmsgLetterRequest`
(2/60)** — likely an open/read/acknowledge round-trip for a specific letter.

#### 8.21.7 Open + populate residuals

- The CarrierPigeonPanal (slot 96) and DeliveryPanel (slot 40) open from their NPC-service
  interactions (the generic NPC-open path); the specific open opcode / NPC kind was not isolated
  (debugger-pending).
- **S2C populate** for the mailbox list and the delivery box was not walked to its dispatch slot this
  lane — residual (debugger/capture-pending). `SmsgSrvLetterReceived` (1/20) is only the arrival
  notification, not the list content.

### 8.22 In-game NPC vendor — item-shop buy/sell window (CODE-CONFIRMED role)

The **NPC vendor / item-shop** is the buy-and-sell storefront opened by talking to a merchant NPC. It
is a **distinct** class from both `ProductPanel` (§8.18, NPC production / crafting) and the NPC dialog
menu (§8.22.6, `KeepNpcPanel`): it is the only one of the three that puts a buy/sell transaction on the
wire. It lives at **master service slot 259** and binds `data/ui/itemshop.dds`. The window object is
**360 bytes (0x168)**. Reference canvas 1024×768; child rects below are panel-local (top-left origin,
+X right, +Y down). The root window's absolute origin is set by the master-window machinery —
debugger-pending.

> The class is the buy/sell vendor regardless of its source-name; the in-engine class name does not
> indicate a subscription/billing feature. The cash (billing) shop is a separate result path (§8.22.5,
> opcode `SmsgCashShopActionResult` 4/114) — do not conflate it with this gold-currency vendor.

#### 8.22.1 Geometry (CODE-CONFIRMED child rects)

| Widget | dst (x, y, w, h) | atlas (uitex id) | NORMAL src | PRESSED src | action |
|---|---|---|---|---|---|
| Backdrop image | (0, 0, 360, 280) | 8 | (300, 722) | — | — |
| Confirm / close button (3-state) | (135, 200, 90, 25) | 2 | (837, 815) | (837, 775) | **0** |
| Item-row buy button (per row, 3 visible) | (264, rowY, 54, 25) | 2 | (798, 540) | (798, 566) | **100..102** (row index + 100) |
| Item-row name label (per row) | (114, rowY + 5, 100, 15) | — (text) | — | — | — |
| Status label | (210, 170, 100, 15) | — (text) | — | — | — |

- **Row layout.** The visible buy-row loop runs `rowY` from **70** in steps of **30** while `rowY < 160`,
  producing **3 visible rows** at y = 70 / 100 / 130. The buy-row buttons inherit the bound atlas (uitex 2).
- **Populate vs visible-row mismatch.** The populate routine fills **6** entries from the client-side
  shop script map, but BuildScene only lays out **3** visible rows. Whether a scroll/page control exposes
  rows 4..6 on this small panel, or rows beyond 3 are unreachable, is unresolved (debugger/UI-pending).

#### 8.22.2 Atlas binding — uitex integer ids (CODE-CONFIRMED ids; DDS via uitex.txt VFS-only)

The vendor binds sprites through the in-game `uitex.txt` integer-id registry (§8.6.1; fallback to id 1).
Ids requested: **2** and **8**. The single shop DDS the vendor binds is `data/ui/itemshop.dds`
(the same atlas `ProductPanel` also rebinds). **There is no `sellwindow.dds`** — the vendor sells out of
the same item-shop atlas. The concrete DDS behind each integer id resolves only through the runtime
`data/ui/uitex.txt` manifest (`formats/ui_manifests.md`) — VFS-pending.

> `data/ui/buywindow.dds` and `data/ui/itemshoppopup.dds` are bound by `ProductPanel` (§8.18.1), not by
> this vendor.

#### 8.22.3 Money / price widgets (CODE-CONFIRMED)

- **Money label** — formatted via `msg.xdb` **45015** from the local player's gold; seeded on open and
  re-driven on every balance push (§8.22.5, opcode `SmsgItemShopBalanceUpdate` 4/115).
- **Per-item price** — formatted via `msg.xdb` **45016**, with the price value **divided by 1,000,000**
  for a major-unit / grouped display.
- **Quantity** — buy is a single-row order keyed by the selected row index; this small panel has no
  in-panel quantity stepper (the order body carries `row × 2`; the inventory-driven acquire/sell path
  carries an explicit quantity field — §8.22.4).

#### 8.22.4 Action-id map (CODE-CONFIRMED)

| Action id | Widget | Behaviour |
|---|---|---|
| 0 | Confirm / close button | close / hide the vendor panel |
| 100..102 | item-row buy buttons | select the row (store selected index); show the item's detail via the shared InfoPanel category builder |
| (Esc, key 27) | — | close the panel |

> The 100..102 row buttons only **select** a row and show its info; the panel onEvent does **not** itself
> send the buy. The buy/sell **send** is raised from the NPC shop-data manager (the storefront-list buy
> emits `CmsgShopBuy` 2/115 with `{npc_id, row × 2}`; the inventory-driven NPC transaction path —
> right-click an inventory item with the shop open — emits `CmsgNpcBuyOrAcquire` 2/19 / `CmsgNpcSell`
> 2/20). Both feed the same NPC shop manager and ack family.

#### 8.22.5 Open mechanism + network (CODE-CONFIRMED)

The vendor is opened by **NPC interaction, not a HUD hotkey**. The world NPC-interaction dispatcher reads
the clicked NPC's KIND byte and switches on it: **KIND 32 (0x20)** opens the item-shop vendor (slot 259).
Other KIND values open the sibling NPC panels (dialog / quest / repair / gather / guild …).

- On open, the panel rebinds `itemshop.dds`, runs the **6-entry populate from a client-side shop script
  map keyed by the NPC id**, shows the window, and seeds the money label. **The open sends no packet** —
  the shop stock is a local script table; only the buy/sell transaction and the balance refresh go on the
  wire.
- **Network (referenced by canonical name + major/minor only; wire-field tables are owned by the protocol
  spec — see `opcodes.md` / `packets/`):**
  - C2S `CmsgNpcBuyOrAcquire` (**2/19**) — inventory-driven buy/acquire; pairs with the 4/19 ack.
  - C2S `CmsgNpcSell` (**2/20**) — inventory-driven sell; pairs with the 4/20 ack.
  - C2S `CmsgShopBuy` (**2/115**) — the storefront-list buy/order (`{npc_id, selected-row × 2}`).
  - S2C `SmsgNpcBuyOrAcquireAck` (**4/19**) — apply acquired item on success; on failure formats item
    duration / time-remaining notices (`msg.xdb` 36003..36028).
  - S2C `SmsgNpcSellItemAck` (**4/20**) — apply the sell; refresh the equip/inventory table when the
    seller is the local player.
  - S2C `SmsgNpcShopSlotClearAck` (**4/21**) — apply or clear a shop-slot change.
  - S2C `SmsgItemShopPurchaseResult` (**4/113**) — purchase success (`msg.xdb` 65008) / failure-code map
    (`msg.xdb` 65006 / 65007 / 65010 / 65011 / 65012).
  - S2C `SmsgCashShopActionResult` (**4/114**) — the **separate CASH (billing) shop** result
    (`msg.xdb` 54127..54130). Not the gold vendor.
  - S2C `SmsgItemShopBalanceUpdate` (**4/115**) — the vendor money refresh (commits player balances and
    repaints the money label). This is the vendor's money opcode; **`ProductPanel` instead uses 3/8** —
    the two are different opcodes and different panels.

#### 8.22.6 Disambiguation — the NPC dialog menu is not the vendor (CODE-CONFIRMED)

`KeepNpcPanel` is one of a family of small **NPC interaction / dialog-menu panels** (siblings:
NpcPanel / FameBuffNpcPanel / QuestNpcPanel / RepairNpcPanel / GuildNpcPanel / ConfessionNpcPanel /
GatherNpcPanel). It is the "talk to an NPC" root menu: a **167×176 backdrop** (uitex 4) at (0,0), a
**167×63 header strip** (uitex 4) at (0,176), a **90×25 close button** (action 1) at (37,37), and **four
106×40 service buttons** (uitex 2/4) stacked at x=25, y = {69, 109, 149, 189}. Its event handler routes
the buttons to the NPC dialog / NPC quest menu / HUD-panel toggles — it has **no item grid and emits
nothing on the wire**. It is NOT the buy/sell vendor (slot 259) and NOT the bottom command strip (§8.23).

#### 8.22.7 Captions (CODE-CONFIRMED ids; CP949 text VFS-only)

`msg.xdb` ids used: **45015** (money/gold label), **45016** (per-item price), **45020** (generic shop
failure notice), **45021..45024** (specific shop failure reasons), **65008** (purchase-success),
**65006 / 65007 / 65010 / 65011 / 65012** (purchase-failure reasons), **54127..54130** (cash-shop
action-result notices — separate cash shop), **36003..36028** (buy/acquire item duration / time-remaining
notices), **18022** (a list/column header label reused by the NPC service panels).

#### 8.22.8 Residuals

- Vendor root-window absolute origin/size (master-window-placed HUD child; only panel-local child rects
  are literal) — debugger-pending.
- The 6-vs-3 row paging (populate loads 6, BuildScene shows 3) — scroll/page control unresolved.
- uitex integer-id (2 / 8) → DDS file mapping — VFS `uitex.txt` pending.
- Per-field VALUE semantics of every buy/sell/balance opcode — capture-unverified (no capture this lane).

### 8.23 In-game DefaultMenu — bottom command strip (CODE-CONFIRMED)

`DefaultMenu` is the **persistent horizontal bottom command bar** — a button strip anchored to the
bottom of the screen, NOT a radial / ring menu. It lives at **master service slot 148** and is built
unconditionally by the HUD builder as an always-registered child (not a pop-open modal). Its "self-expand"
(action 4000) is a **vertical bar-height toggle** (30 px collapsed ↔ 254 px expanded), not a ring.
Reference canvas 1024×768; runtime screen-width / screen-height globals drive the bottom-anchor and a
hi-res horizontal scale fix.

> **Corrects a prior premise.** Earlier waves listed slot 191 as a "DefaultMenu radial". The binary
> places `DefaultMenu` at **slot 148** as a horizontal strip; **slot 191 is a different class
> (`KeepPanel`, NPC-keep — role TBD)**. See `ui_hud_layout.md §5.13`.

#### 8.23.1 Geometry (CODE-CONFIRMED)

| Element | dst (x, y, w, h) | atlas (uitex id) | src (x, y) | notes |
|---|---|---|---|---|
| Main bar panel | (0, 0, 1024, 45) | 1 | (0, 957) | the 45-px-tall bottom command bar — host for the entry buttons |
| Sub-strip panel | (0, 45, 1024, 20) | 1 | (0, 1002) | a 20-px secondary strip below the bar |
| Collapse toggle (corner, 3-state) | (1008, 30, 11, 11) | 1 | (939, 809) / pressed (950, 809) | small corner toggle, action 4023 |
| TimePanel child | (0, 0, 158, 39) | — | — | embedded clock |
| Two backdrop image panels | (—, —, 1024, 230) | (face) | — | large decorative emote/face backdrops; hidden by default |
| Animated face / emote texture | — | (face) | — | drawn per-frame; see §8.23.2 |

- **Anchor.** The component origin is set so the 45-px bar sits at the screen bottom (`y = screen_height − 60`);
  the expand grows it upward (bar-height field toggles between 30 and 254). This matches the
  `ui_hud_layout.md §5.13` bottom-anchored formula.
- **Entry-button row.** Each entry is a 3-state button **29×29 at y = 10**, laid out in two x-clusters
  (left cluster ≈ 218..373, right cluster ≈ 622..808). The three quick-slot/stance buttons (actions
  4019/4020/4021) are **21×21 at y = 14**, x = 856 / 898 / 940. The two attack/peace mode buttons
  (actions 4014/4015) are **128×46 at x = 448, y = 0**. The exact per-button src origins on the bound
  atlas are recorded against uitex id 1 (the main HUD button atlas).

#### 8.23.2 Atlas binding — uitex integer ids (CODE-CONFIRMED ids; DDS via uitex.txt VFS-only)

| `uitex.txt` id | Use in this panel |
|---:|---|
| 1 | main HUD button atlas — all 29×29 entry buttons + the bar panels (bar src (0,957), sub-strip src (0,1002)) |
| 26 | quick-slot button atlas — the three 21×21 stance buttons (actions 4019/4020/4021) |
| 4 | secondary chrome atlas — the collapse toggle (4023), the sub-strip edges (4016/4018), the large face panel |
| 14 | label / text atlas — the hidden text label |

- **Hardcoded (non-registry) textures.** The animated face/emote uses literal path
  `data/ui/face/anger/ani_000.dds` (an animated atlas; sibling frames `ani_001..ani_015.dds` in the same
  directory). The attack/peace mode bar uses literal 18-frame strips `data/ui/mode/attackmode-01..18.tga`
  and `data/ui/mode/peacemode-01..18.tga`, selected by the global attack-mode flag.
- The DDS each uitex id maps to is data-driven from `uitex.txt` at load — id→file is VFS-pending.

#### 8.23.3 The complete entry → action-id → MainWindow-slot map (CODE-CONFIRMED)

The dispatcher (vtable slot 6) switches on the pressed child's action id. Every action (except 4000 and
the mode toggles) **opens/toggles its target MainWindow service slot and mutually-exclusively closes the
visible siblings**. A "is-visible" byte on each panel gates the toggle.

| Action | Entry / role | Effect | MainWindow slot |
|---:|---|---|---:|
| 4000 | the bar itself (self-expand) | toggle bar height 30↔254; show/hide the six sub-group entries; when expanding, mutually-close the visible HUD groups | self |
| 4001 | Inventory | toggle inventory; close siblings | **158** (ItemPanel) |
| 4002 | Buddy / Relation | toggle buddy-relation; close siblings | **185** |
| 4003 | Skill | toggle skill; close siblings | **159** (SkillPanel) |
| 4004 | Quest tracker | toggle quest tracker (mode 2); close siblings | **206** (QuestPanel) |
| 4005 | attack-stance / status group | open StatusPanel group; close siblings | **146** (StatusPanel) |
| 4006 | conversation / relation group | open relation group (state-gated) or select a UI-list entry | **193** |
| 4007 | attack-stance (alt) | chat-busy → notice `msg.xdb` 10082 + abort; else open the status group without keep-open | **146** (chat-output **202** check) |
| 4008 | relation / focus panel | open the relation/whisper input (focus-manager gated) | **193** |
| 4009 | panel via slot 161 | generic visibility toggle | **161** |
| 4010 | panel via slot 164 | generic visibility toggle | **164** |
| 4011 | Help toggle | press/unpress the docked help button (slot 176); reflects the help overlay (§8.24) | **322** (help) / **176** (button) |
| 4012 | Party | toggle party; close siblings | **220** (PartyPanel) |
| 4013 | ProductPanel (crafting) | scene-gated (scene-state ≠ 11) + billing-gated (`msg.xdb` 45003 on fail) + chat-busy gated; toggle ProductPanel | **230** (ProductPanel) |
| 4014 | attack-mode OFF | player-state gated; send attack-mode-off + refresh weapon FX / idle motion | 324/328 (mode-bar buttons) |
| 4015 | attack-mode ON | symmetric to 4014; send attack-mode-on | 324/328 (mode-bar buttons) |
| 4016 / 4017 / 4018 | sub-strip edges / label | the expanded-bar caption / number-format label tail (uses `msg.xdb` 2222) — only when the bar is expanded | — (decorative; role MED for 4017/4018) |
| 4019 / 4020 / 4021 | quick-slot / stance buttons 0/1/2 | per-index stance dispatcher — reads a stored mode byte, presses button 176, toggles status/quest/guild/dock per mode | quick-slot state (in-panel) |
| 4022 | warstone button | toggle the warstone panel (when its button is visible) | **240** |
| 4023 | collapse toggle (corner) | toggle a layout/expand sub-state + relayout | self |
| 4024 | Quest tracker (mode 7) | open the quest tracker in mode 7; close siblings | **206** (QuestPanel) |

> Other sibling slots are **closed** (never opened) by the mutually-exclusive close: 147 (dock-slide),
> 207 (guild). 202 is the chat-output panel used only as a busy check. Wire each entry button to its
> MainWindow service slot — not to a single id space — and reproduce the mutually-exclusive-close on
> every open.

#### 8.23.4 Open / close + mutual-exclusivity (CODE-CONFIRMED)

The strip itself is persistent (always registered at slot 148). Individual groups open/close via the
keyboard hotkey dispatcher (which reaches the slot-148 menu and calls a group handler) and via the entry
buttons. Each group handler is **mutually-exclusive**: it opens its own panel (or expands the bar) and
walks the sibling slots, toggling any open one off. Common gating on every action: abort if a global
"input captured / busy" byte is set; abort if a close-all helper already consumed the key; if the chat
input box is active, beep / select a UI-list entry instead of acting. Every action plays UI click sound
**862020102**. The keymap that turns a physical key into an action id is a separate option/keybind table
(config / debugger-pending — not in the build routine).

#### 8.23.5 Captions (CODE-CONFIRMED ids; CP949 text VFS-only)

`msg.xdb` ids: **10082** (chat-busy notice, action 4007), **45003** (ProductPanel-unavailable notice,
action 4013), **2222** (number-format template in the expanded-bar label tail). Per-entry tooltip captions
are not statically embedded in the build routine (config / debugger-pending).

#### 8.23.6 Residuals

- Keymap → action-id binding (which physical key, including the maintainer-reported toggle key, maps to
  each action) — config / debugger-pending.
- uitex `id → DDS` for ids 1 / 4 / 14 / 26 — VFS `uitex.txt` pending.
- Absolute on-screen pixels depend on the runtime screen-size globals (bottom-anchor formula confirmed).
- Cases 4016/4017/4018 precise visible role (expanded-bar caption/number strip) — MED.

### 8.24 In-game HelpPanel — full-screen help overlay (CODE-CONFIRMED)

The **HelpPanel** is **not a separate panel class and not a `+0x238` service-slot panel**. It is a
lazily-built **full-screen image overlay** that lives as a direct member of the root in-game HUD window
(the "MainMaster" `MainWindow`). When shown it draws a single image `data/ui/help.dds` stretched to the
full screen at (0,0) with opaque white tint — the entire help/manual content is the picture itself.

- **Member layout (direct DWORD members of the root HUD window, not a service-slot-table entry):**
  the help texture wrapper, the help image component (the member the earlier slot map called "slot 322"),
  and the docked help menu button (the member earlier called "slot 176"). They are plain members, not
  entries in the `+0x238` service-slot table.

#### 8.24.1 Geometry (CODE-CONFIRMED)

| Property | Value |
|---|---|
| dst position | (0, 0) |
| dst size | (screen_width, screen_height) — the runtime screen-size globals (the client runs 1024×768) |
| src rect | (0, 0) to (W, H) — the whole texture, 1:1 stretch |
| tint | opaque white (ARGB 0xFFFFFFFF) |
| z-order field | high (drawn above the HUD) |

#### 8.24.2 Content / atlas (CODE-CONFIRMED)

- **Single literal image `data/ui/help.dds`** — bound by direct path, **not** a uitex integer id. There
  is no registry lookup and no src-rect carving: the whole DDS is the help screen.
- **Page count = 1. Navigation = none.** No prev/next, no tab/topic list, no scroll, **no `msg.xdb` feed
  and no help table.** Any nav-looking chrome is baked into the artwork. Verify the file exists and
  inspect its painted layout — that is an asset task, not code (VFS-pending).

> Unrelated and NOT part of this overlay: `data/ui/cubegamble_help.dds` (a different feature's help image)
> and `data/script/tiphelp.scr` (the loading-tip / tip-of-the-day system).

#### 8.24.3 Open / close mechanism (CODE-CONFIRMED)

- **Toggle = key `h` (0x68)** on the keyboard hotkey dispatcher: pressing `h` while the overlay is hidden
  shows it; pressing `h` while visible hides it. **The "Space" trigger is REFUTED on this dispatcher**
  (debugger-pending only if another input layer maps Space → help elsewhere).
- **Suppressed in one app/game-state** (a state gate disables the toggle — the exact blocked state is
  debugger-pending; likely a cutscene / blocked / billing state).
- **Mirrored by the docked help menu button** and by `DefaultMenu` action **4011** (§8.23.3): the show
  path presses the docked button when the overlay opens, and DefaultMenu 4011 reflects that button — so
  the button and the overlay stay in sync. (Whether 4011 also reaches the overlay through the button's
  own event chain is MED / debugger-pending; the literal overlay show/hide is the `h`-key path.)
- **Forced hide** when leaving the world to logout.

#### 8.24.4 Captions (CODE-CONFIRMED)

**None.** The overlay carries no code-driven caption / title / topic label; it is a static picture.

#### 8.24.5 Residuals

- The exact app/game-state that suppresses the toggle — debugger-pending.
- Live confirmation that `h` fires the toggle and that the menu-button click reaches the overlay (button
  ↔ overlay sync) — debugger-pending.
- "Space opens help" — REFUTED in static code; confirm no other input layer maps it.
- Whether the global Esc / close-all also dismisses the overlay — MED.
- The docked help button's src-rect / caption / bound action id (inside the large HUD builder) — MED.

### 8.25 In-game system-message peers — AnnouncePanel + ErrorPanel (CODE-CONFIRMED)

Two HUD service-slot panels are the server-message display peers of the client-local `MessagePanel`
(§8.20). They are both `GUPanel` subclasses and 16-slot-vtable windows. Reference canvas 1024×768.

> **The three peers and the routing rule.** Server **notice / error** text routes through a single global
> notice sink to (a) the chat log channel **and** (b) **ErrorPanel (slot 168)**, which delegates the
> banner to **AnnouncePanel (slot 221)** when present. Server **GM / system** text routes to the chat-log
> windows (slots 222 / 163), NOT the modals. **MessagePanel (slot 190)** is **never** a wire destination —
> it is raised only by client-side decision logic (e.g. the level-milestone branch on `SmsgLevelUp` 5/32).
> There is no dedicated "show this panel" S2C opcode for any of the three. (Opcodes named below by
> major/minor only; wire-field tables are owned by `opcodes.md` / `packets/`.)

#### 8.25.1 AnnouncePanel — scrolling announce banner (slot 221, CODE-CONFIRMED)

A **text-only scrolling banner**, non-interactive (its event handler consumes no input). It binds **no
atlas** (texture id 0 — purely scrolling text labels); its draw runs a custom animated text rotator keyed
by a client game-state word.

| Property | Value |
|---|---|
| Master service slot | **221** |
| Anchor / geometry | **(11, 455), 120 × 85** — lower-left of the reference canvas |
| Texture | none (0) — text-only / transparent |
| Widgets | **8 `GULabel`s** (two batches: 3 + 5), each 110 × 12, stacked vertically; **no buttons, no close control** |
| Captions | supplied by the caller (the notice sink) + a rotating-text source keyed by game-state; **no hardcoded `msg.xdb` id** |

It is built once by the HUD builder and is the **banner delegate** of the notice sink: the ErrorPanel
notice show forwards its text to AnnouncePanel when slot 221 exists.

#### 8.25.2 ErrorPanel — timed floating notice / error modal (slot 168, CODE-CONFIRMED)

The **timed floating notice / error modal** — the single global on-screen sink for almost every server
notice / error. It shows text with a **per-second countdown caption**, an OK button, and an on-expiry
auto-action; it auto-dismisses when the countdown elapses. It is built in **three scenes** (in-game HUD,
character-select, and login) — the cross-scene error modal.

| Property | Value |
|---|---|
| Master service slot | **168** |
| Atlas (uitex id) | **2** → `data/ui/inventwindow.dds` (the same chrome as MessagePanel) |
| Geometry | **screen-centred** with a 340 box: X = `(screen_width − 340)/2`, Y = `(screen_height − 340)/2`, **W = 340, H = 190** (≈ X 342 / Y 214 on the 1024×768 canvas) |
| Background src origin | (318, 647) on uitex 2 — MED (push-decode; confirm against the atlas) |

**Widgets (CODE-CONFIRMED):**

| Child | Type | dst (x, y, w, h) | atlas | NORMAL src | HOVER src | action |
|---|---|---|---|---|---|---|
| Title label | GULabels (multi-line) | (w/2−6, h, …) | — (text) | — | — | 670 |
| OK button (3-state) | GUButton | (125, 151, 90, 25) | panel chrome (uitex 2) | (417, 943) | (507, 943) | **671** |
| Body labels (×4) | GULabels (multi-line) | (0, h, …) | — (text) | — | — | — |

- **Countdown + auto-dismiss** (the distinguishing behaviour vs MessagePanel): per ~1000 ms it decrements
  a remaining-seconds counter and formats a caption `"<text> - <seconds>"` from `msg.xdb` **101**, then
  re-centres it on the title label. When the countdown reaches 0 it runs an on-expiry action and hides.
- **OK button (action 671)** closes the modal; **Esc** does not auto-dismiss but the timer does.
- **On-expiry action** (mode-selected): one of a forced C2S send (`2/35`), an alternate net send, or a
  return-to-town path. The three mode constants that pick which one fires are **MED** (debugger-pending —
  which server-message KIND sets which mode at runtime).

#### 8.25.3 S2C notice/error routing (CODE-CONFIRMED routing; codes/values capture-pending)

Server-message KINDs route through the global notice sink unless noted. Opcodes by canonical name +
major/minor only (wire-field tables owned by the protocol spec):

| Opcode | Routes to | Note |
|---|---|---|
| `SmsgShowPopupByCode` (**4/500**) | **ErrorPanel (168)** directly (5000 ms) | a `u32` popup code selects one of seven preset strings; codes 1..7 — VALUE meaning capture-pending |
| `SmsgGmNoticeError` (**4/132**), `SmsgNoticeError` (**4/138**), `SmsgColoredSystemText` (**4/140**), `SmsgShowMessage51027` (**4/146**) | ErrorPanel (168) via the sink | the result-with-message family; per-field VALUE semantics capture-pending |
| (broad result family: item/skill/guild/party/billing/combat results, etc.) | ErrorPanel (168) via the sink | identical routing mechanism; the sink also posts to the chat log |
| `SmsgGmChatMessage` (**3/50000**) | chat-log (222 / 163), **NOT** the modals | length-prefixed text (≤ 121 bytes); login/select fallback sink out-of-game |
| `SmsgLevelUp` (**5/32**) | **MessagePanel (190)** via client level logic + ErrorPanel (168) general | the **only** confirmed handler that opens MessagePanel — keyed on a client-derived level (level 12 → `msginfo.do` code 100; level 24 → code 101), not a wire message-code field |

- **The global notice sink** posts the text to the chat log channel **and**, when slot 168 exists, raises
  ErrorPanel as a timed notice (default timeout 5000 ms), which delegates the banner to AnnouncePanel
  (slot 221) when present. Its colour argument is an ARGB sentinel and its mode argument selects
  banner-vs-transient — the exact colour table and mode enum are **MED** (debugger/capture-pending).

#### 8.25.4 Residuals

- Notice-sink colour table + mode enum — MED (debugger/capture).
- ErrorPanel on-expiry mode constants (which KIND fires which net action) — MED.
- `SmsgShowPopupByCode` (4/500) popup-code 1..7 string contents — capture-pending.
- ErrorPanel background sub-rect on uitex 2 — MED (confirm against the atlas).
- All per-field VALUE semantics — capture-unverified (no capture this lane); routing / sizes / offsets are
  control-flow-confirmed.

### 8.26 In-game PetPanel — player-couple / pair-relation window (CODE-CONFIRMED)

`PetPanel` (its retained in-engine name) is the **player-couple / pair-relation companion window** —
**not** a tamed-creature / companion-pet window. There is no creature-pet feature in this build (no pet
data table, no pet DDS, a single matching class). It shows a **partnered actor's** name and level, two
relationship gauges, four command buttons, and an info button. It lives at **master service slot 194**;
the object is **1488 bytes (0x5D0)**. It is built unconditionally by the HUD builder as a persistent but
**hidden-until-fed** child.

> **Slot correction.** The pet/couple-window service slot is **194** — REFUTING the earlier candidates
> 161/164 (generic toggles), 223 (a guess), and 230 (ProductPanel). See `ui_hud_layout.md §5.13`.

#### 8.26.1 Geometry (CODE-CONFIRMED — absolute, not screen-relative)

The window is built at **fixed (80, 200), 228 × 337** on the 1024×768 canvas (these are absolute — there
is no screen-relative origin in the constructor). Two uitex atlases are bound (§8.26.2). Child rects below
are window-local (origin = window top-left).

**Title bar** — a 228×16 caption strip at (0, 0), src (692, 0). Its three 11×11 corner buttons are
title-bar-local:

| Child | dst (x, y, w, h) | NORMAL src | PRESSED src | action |
|---|---|---|---|---|
| Title button (help) | (189, 2, 11, 11) | (310, 488) | (386, 534) | **2** |
| Title button (minimize) | (200, 2, 11, 11) | (321, 488) | (105, 309) | **0** |
| Title button (close) | (212, 2, 11, 11) | (332, 488) | (116, 309) | **1** |

**Body panel** — a 228×319 main body at (0, 16), src (692, 16). Body-local children:

| Child | Type | dst (x, y, w, h) | src | role |
|---|---|---|---|---|
| Gauge-bar #1 background | image | (113, 116, 103, 17) | (920, 208) | relation gauge 1 |
| Gauge-bar #2 background | image | (113, 96, 103, 17) | (920, 225) | relation gauge 2 |
| Gauge #1 overlay | GULabel | (113, 116, 103, 17) | — | numeric / text over gauge 1 |
| Gauge #2 overlay | GULabel | (113, 96, 103, 17) | — | numeric / text over gauge 2 |
| Partner NAME | GULabel | (79, 43, 128, 17) | — | partner display name |
| Partner LEVEL | GULabel | (79, 70, 86, 17) | — | partner level |
| Command button A | GUButton | (15, 182, 91, 25) | (921, 104) | action **3** |
| Command button B | GUButton | (124, 182, 91, 25) | (921, 26) | action **4** |
| Command button C | GUButton | (15, 215, 91, 25) | (921, 52) | action **5** |
| Command button D | GUButton | (124, 215, 91, 25) | (921, 78) | action **6** |
| Info / details button | GUButton | (110, 253, 110, 39) | (849, 337) | action **7** |

#### 8.26.2 Atlas binding — uitex integer ids (CODE-CONFIRMED ids; DDS via uitex.txt VFS-only)

| `uitex.txt` id | Use in this panel |
|---:|---|
| 1 | shared HUD button / chrome atlas — the three 11×11 title buttons |
| 9 | the couple/pair panel art — the title-bar + body panels, both gauge backgrounds, all four command buttons, the info button |

The DDS each id maps to resolves through the runtime `uitex.txt` manifest (`formats/ui_manifests.md`) —
VFS-pending. No literal pet DDS path exists in the binary.

#### 8.26.3 Action-id map (CODE-CONFIRMED)

| Action | Source widget | Behaviour |
|---:|---|---|
| 0 | title button | minimize-family (no-op toggle path) — role MED |
| 1 | title button | collapse the body panel (keep the title bar) |
| 2 | title button | press the docked help button (slot 176) + set its caption from `msg.xdb` **16002** — the shared help mechanism (same button DefaultMenu uses) |
| 3..6 | command buttons | select a local command mode 0..3, re-skin the four button icons to show the selected one pressed, then route through the generic UI-list select / net-send path |
| 7 | info / details button | open the shared **InfoPanel** (slot 166) with a category/detail page |
| (drag on title bar) | — | begins window drag (stores grab offset on the master window) |

> **No dedicated C2S pet opcode.** The four command buttons route through a generic select/send helper
> shared by ~100 panels (the "pick a command, then the framework selects/targets an actor" pattern) — not
> a pet-specific packet.

#### 8.26.4 Open / populate / close (CODE-CONFIRMED) — data-driven, NOT a hotkey

- **Auto-shown by data.** The panel pops when the server pushes a pair-relation for the local player:
  `SmsgActorPairRelation` (**S2C 5/53**) on the relation-set path populates the panel from the partnered
  actor and calls SetVisible(1).
- **Populate** (from the partnered actor record): partner display name, partner level, and the two gauges
  (gauge maxima resolved from an actor-data map by the partner's appearance/mob id). On set it also builds
  a localized **couple system notice from `msg.xdb` 10025..10032** (posted to the chat log / notice, NOT a
  panel caption).
- **Hidden** on relation clear (the 5/53 clear path), on a confirm-click into the panel, and on the
  partner's death (`SmsgCharDeath`).
- **NOT a hotkey window.** Static shows the `n` key (action id 110) opening **slot 230 (ProductPanel)**,
  not this slot — slot 194 has no hotkey entry. (A live `n` → pet-window observation would be a runtime
  keymap remap — debugger-pending.)

> Opcodes by canonical name + major/minor only (wire-field tables owned by `opcodes.md` / `packets/`):
> `SmsgActorPairRelation` (**5/53**, populate+show; reads a 32-byte field block),
> `SmsgPlayerPairSystemNotice` (**5/42**, a related player-pair system notice; role MED),
> `SmsgRemoteActorRelationPair` (**5/64**, other players' couple state; role MED). C2S: none dedicated.

#### 8.26.5 Captions (CODE-CONFIRMED ids; CP949 text VFS-only)

`msg.xdb` ids: **16002** (help-button caption, action 2), **10025..10032** (couple/pair system-notice
strings — routed to the chat log / notice, not panel widgets), **3014 / 3015** (pair-interaction-gate
notices that reference the slot-194 bound-target probe). Per-button tooltip captions for the command /
info buttons are not statically embedded (icon-only; config / debugger-pending).

#### 8.26.6 Residuals

- `n`-key live behaviour (static says `n` → slot 230, not 194) — debugger / config-pending.
- The `5/53` 32-byte payload's exact per-field offsets/types and the `relationKind` value meanings —
  capture-unverified.
- uitex id 1 / 9 → DDS file mapping — VFS `uitex.txt` pending.
- Whether a creature-pet feature ever existed — REFUTED statically; no further static signal.


### 8.27 KeepNpcPanel — the NPC storage/keep dialog menu (CODE-CONFIRMED)

`KeepNpcPanel` is the **5-option vertical dialog menu** shown when the player clicks a storage/keep NPC
(the central NPC-interaction router classifies it as a **KIND-9** NPC and opens this panel). It is the
menu root from which the player chooses "open storage", a keep-service option list, NPC dialog text, a
quest-offer list, or "close". It lives at **master service slot 152**, and it is a master-window-placed
HUD child (panel-local child rects below; absolute root origin debugger-pending).

> **Cross-reference — the full NPC dispatch lives elsewhere.** The complete NPC click →
> KIND-classifier → target-window map (the 35-case dispatch covering every NPC kind, plus the two
> router-emitted sends) is documented in `specs/npc_interaction.md §2.2`. KeepNpcPanel is only the
> KIND-9 leaf of that map. This section documents the KeepNpcPanel **option layout** and its
> selector mechanism; it does NOT restate the dispatch table — see `npc_interaction.md §2.2`.

#### 8.27.1 Geometry (CODE-CONFIRMED rects; panel-local origin)

The menu is two backdrop images plus five buttons stacked vertically. Reference canvas is panel-local
(top-left origin, +X right, +Y down). The small top button sits at y=37; the four full-width
(106 × 40) option buttons run y = 69 / 109 / 149 / 189.

| Widget | dst (x, y, w, h) | atlas (uitex id) | NORMAL src | HOVER src | PRESSED src | Action | Selector |
|---|---|---:|---|---|---|---:|---:|
| Backdrop image | (0, 0, 167, 176) | 4 | (0, 695) | — | — | (no action) | — |
| Header strip image | (0, 176, 167, 63) | 4 | (0, 871) | — | — | (no action) | — |
| Top button (open storage) | (37, 37, 90, 25) | 4 | (642, 836) | (642, 836) | (755, 836) | **1** | sel 1 |
| Service button (keep-service list) | (25, 69, 106, 40) | 4 | (316, 816) | (316, 816) | (856, 828) | **2** | sel 2 |
| Service button (NPC dialog) | (25, 109, 106, 40) | 2 | (825, 927) | (825, 927) | (825, 687) | **0** | sel 0 |
| Service button (quest offer) | (25, 149, 106, 40) | 2 | (825, 967) | (825, 967) | (825, 727) | **3** | sel 3 |
| Bottom button (close) | (25, 189, 106, 40) | 2 | (825, 807) | (825, 807) | (825, 767) | **4** | sel 4 |

The on-screen blit extent is the rect `(w, h)`; the source extent equals `(w, h)` (1:1 blit) from the
named source origin. The button faces are pure atlas sprites — **no `msg.xdb` caption id is baked into
the KeepNpcPanel buttons themselves** (the captions are part of the DDS art). CODE-CONFIRMED.

#### 8.27.2 Atlas binding — uitex integer ids (CODE-CONFIRMED ids; DDS via uitex.txt VFS-pending)

| `uitex.txt` id | Use in this panel |
|---:|---|
| 4 | the two backdrop images, the open-storage top button, the keep-service option button |
| 2 | the NPC-dialog, quest-offer and close buttons |

The DDS each id maps to resolves through the runtime `uitex.txt` manifest (`formats/ui_manifests.md`)
— VFS-pending.

#### 8.27.3 Selector mechanism + action map (CODE-CONFIRMED)

When a button is clicked, the window dispatch latches the clicked child's **action id** into the
panel's selector field (`panel+180`). KeepNpcPanel's onEvent then reads that selector and branches:

| Selector / action | Behaviour | Target |
|---:|---|---|
| **0** | NPC dialog-text path; populates and shows the NPC dialog only if the active NPC descriptor KIND == 9 | NPC dialog sub-view |
| **1** | **OPEN STORAGE** — bag-count gate (KIND-9 limit 50); on pass resets the storage grid, toggles the storage view on, hides the world HUD panels | **KeepPanel (slot 191, §8.32)** |
| **2** | open the keep-service option list (a 9-entry service menu; captions `msg.xdb` **40033..40041**); hides the world HUD panels | keep-service option list (slot 182) |
| **3** | open the quest-offer list; shows a "no quest" feedback notice if empty | quest-offer list (slot 215) |
| **4** | **CLOSE** the panel | (UI close) |
| ≥ 5 | ignored (no-op) | — |
| ESC (key 27) when shown | same CLOSE path | (UI close) |

> **Correction vs an earlier dirty draft.** A prior note swapped the top button and the close button.
> The binary is authoritative: the (37, 37) top button is **action 1 = OPEN STORAGE** (→ slot 191),
> and the bottom (25, 189) button is **action 4 = CLOSE**. The action ids and routing above are the
> ground-truth reading.

> **Emits nothing on the wire from this panel.** KeepNpcPanel itself sends no C2S packet — its
> selectors only open/close other windows. The storage-open request (C2S 2/142) and the per-item
> moves (C2S 2/46 / 2/44) are emitted from the opened windows / router, not from this menu. See
> `npc_interaction.md §2.2 / §7` and §8.32 below.

#### 8.27.4 Residuals

- KeepNpcPanel root-window absolute origin/size — debugger-pending (master-window-placed child).
- uitex id 2 / 4 → DDS file mapping — VFS `uitex.txt` pending.
- The CP949 text of `msg.xdb` 40033..40041 (keep-service option captions) and the per-entry wiring of
  the 9 service options — VFS / capture-pending.

### 8.28 RelationPanel + BuddyRelation — TWO distinct social windows (CODE-CONFIRMED)

There are **two separate social windows** that an earlier stub conflated. They have distinct classes
and distinct master service slots:

- **`RelationPanel` = master service slot 193** — the relation / teacher / fate window (a paged,
  tabbed roster with add/remove and master-set actions). This is the class literally named
  `RelationPanel`.
- **`BuddyRelation` = master service slot 185** — the buddy / social sibling window, a **separate
  class** with its own constructor and toggle. It is opened by the `DefaultMenu` radial **action
  4002**.

> **Load-bearing 185 ≠ 193 distinction.** The class named `RelationPanel` is at slot **193**, NOT 185.
> Slot 185 holds the distinct `BuddyRelation` sibling. The group-open dispatcher reached from
> `DefaultMenu` toggles BOTH the slot-185 buddy window and the slot-193 RelationPanel together, which
> is why they were historically confused. Treat them as two windows. (`BuddyRelation`'s own layout is
> a separate deliverable; this section documents `RelationPanel` in full and pins the slot split.)

#### 8.28.1 Geometry (RelationPanel, slot 193 — CODE-CONFIRMED, panel-local origin)

Master-window placement builds the panel at build-time dst (80, 200), size **295 × 393**; the runtime
absolute on-screen origin may be re-anchored by the master-window placement logic (debugger-pending).
Child rects are panel-local; all source rects index the single relation atlas (§8.28.2).

| Widget | dst (x, y, w, h) | NORMAL src | PRESSED src | Action |
|---|---|---|---|---:|
| Close button (top-right) | (panelW−12, 2, 11, 11) | (307, 11) | (307, 11) | 16 |
| Tab buttons ×4 | (x, y∈{6,63,120,177}, 50, 55), step y+57 | per-tab origins (data-driven) | — | 19..22 |
| Header image | (15, 74, 49, 21) | (296, 341) | — | — |
| Name textbox (IME off, maxlen 16) | (74, 74, 136, 21) | (296, 363) | — | 9 (submit) |
| Name OK / search button | (235, 74, 45, 21) | (296, 191) | (296, 213) | 10 |
| Bottom button A | (58, 337, 68, 20) | (352, 23) | (352, 44) | 11 |
| Bottom button B | (165, 337, 68, 20) | (352, 65) | (352, 86) | 14 |
| Bottom button C (overlay) | (165, 337, 68, 20) | (352, 191) | (352, 212) | 15 |
| Status-bar image (hidden by default) | (0, 0, 182, 19) | (0, 448) | — | — |
| Member list rows ×6 | base y=118, step y+21 (while y<244) | rowImg (0, 487); miniBtn (296, 385)/(296, 400) | — | rowBtn 0..5; miniBtn 34..39 |
| Big list button | (29, 259, 224, 18) | (0, 429) | (0, 429) | 12 |
| Secondary row image | (16, 94, 251, 18) | (0, 487) | — | — |
| Secondary row button | (16, 94, 185, 18) | (blank) | — | 13 |
| Secondary mini-button | (206, 96, 59, 14) | (296, 385) | (296, 400) | 40 |
| Page-up button | (17, 303, 26, 26) | (296, 235) | (296, 262) | 7 |
| Page-down button | (241, 303, 26, 26) | (322, 235) | (322, 262) | 8 |
| Page indicator label | (17, 340, 15, 13) | — | — | — |
| Numeric page buttons ×10 | y=309, x=57+15·i, 15×14 (while x<207) | src x=352+15·(i%5), y=247 (rows 0..4) / 275 (rows 5..9) | — | 23..32 |
| Page "more" button | (207, 309, 30, 14) | (352, 233) | (352, 233) | 33 |
| Confirm / close button | (123, 361, 45, 25) | (296, 289) | (296, 315) | 6 |

Each button uses the same `(w, h)` blit extent for all three (normal/hover/pressed) states from the
named source origins.

#### 8.28.2 Atlas binding (CODE-CONFIRMED)

`RelationPanel` uses a single shared atlas at the **literal path `data/ui/relation.dds`** (a hard-coded
path, NOT a uitex integer id — uitex.txt indirection does not apply to this panel). On lazy-init the
atlas handle is bound into the panel and into **every child image component**, so each child's `texId`
build argument is **0** — every widget draws from the one `relation.dds` atlas. The per-widget source
rects in §8.28.1 index directly into `relation.dds`.

#### 8.28.3 Tab / list structure (CODE-CONFIRMED structure; tab meanings MED)

- **4 tabs** (active-tab index 0..3, set by actions 19..22). Each tab reconfigures which widget
  clusters are visible (member-add row vs. info row vs. fate/training row) and re-skins the mode
  button with a different atlas source pair. Inferred tab meaning (capture/debugger-pending to name
  each exactly): tab 0 = master/teacher, tab 1 = parent/family, tab 2 = marriage/spouse, tab 3 =
  friend/fate. The associated detail page categories are 40 / 41 / 35 / 37.
- **Paged member list**: 6 visible rows × paging, driven by page-up/down (7/8), numeric page buttons
  (23..32) and page-more (33).

#### 8.28.4 Action-id map (RelationPanel onEvent — CODE-CONFIRMED unless flagged)

| Action | Behaviour | Conf |
|---:|---|---|
| 0..5 | member-list row click (rows 0..5) → select roster entry | MED |
| 6 | close / confirm — hide + rebuild | CODE |
| 7 / 8 | page-up / page-down | CODE |
| 9 | name-textbox submit (routes input focus, shows modal focus) | CODE |
| 10 | ADD / REMOVE a relation by typed name (emits a chat-command line, §8.28.6) | CODE |
| 11 | open the detail / info context for the selected member, keyed by the active tab | CODE |
| 12 / 13 | big list-button / secondary-row action | MED |
| 14 | timed action (3-minute cooldown); emits a CP949 chat-command line + system text (likely a master/training request) | CODE (text) / MED (meaning) |
| 15 | set teacher/master — validates against the client roster, builds a detail page; on failure shows `msg.xdb` **2089** / **10074** | CODE |
| 16 | window close box (top-right) | CODE |
| 19..22 | select tab 0..3 | CODE |
| 23..33 | numeric page selection + page-more | CODE |
| 34..39 | per-row mini-button (rows 0..5) | MED |
| 40 | secondary-row mini-button | MED |
| TAB key | cycle active tab (index + 1 mod 4) | CODE |
| ESC key (when focused) | close the panel | CODE |

#### 8.28.5 Open / populate (CODE-CONFIRMED path)

- **DefaultMenu route:** the `DefaultMenu` group-open dispatcher (reached on a social-group menu
  action) toggles the social-window group, opening BOTH slot 185 (`BuddyRelation`) AND slot 193
  (`RelationPanel`). The `DefaultMenu` **action 4002** named for the buddy window reaches the group
  through this dispatcher (the exact 4002 → dispatcher binding is owned by the DefaultMenu lane — MED).
- **Hotkey route:** a relation/social toggle hotkey closes `RelationPanel` (slot 193) if visible, else
  closes sibling panels and opens the relation group (tab reset to 0). The literal key is keymap-pending.
- **Populate (S2C):** a relation-roster push fills/updates one roster record per message via the
  populate handler on slot 193 (the populate record carries a relation-type selector byte and a
  member name; relation-type VALUE semantics are capture-pending). Opcode numbers and wire-field
  tables are owned by `opcodes.md` / `packets/` — not restated here.

#### 8.28.6 Emit (C2S) — chat-command text, not binary opcodes (CODE-CONFIRMED)

`RelationPanel` does **not** call binary packet builders directly. Its add/remove/master actions are
emitted as **slash-command-style chat text** through the chat-command submit path (which the
chat-command parser later turns into the actual wire opcode):

| Action | Command text (format) | Meaning |
|---:|---|---|
| 10 (add) | `friend %s %s` | add a friend / relation by name |
| 10 (remove) | `cut %s` | remove / cut a relation by name |
| 14 | a CP949 chat-command line | timed request (3-min cooldown) — likely master/training request |
| 15 | local teacher-set + detail page | set teacher/master locally |

The verb → wire-opcode mapping (`friend` / `cut` / the CP949 verb) is a chat-command-table concern
(owned by the chat / command-table lane), not a packet this panel builds.

#### 8.28.7 Captions (CODE-CONFIRMED ids; CP949 text VFS-pending)

`msg.xdb` ids consumed: **2089** / **10074** (teacher-set failure toasts, action 15); **10066**
(relation removed/declined), **10067** (relation-request template), **10078** / **10079**
(relation-fee money-deducted templates), **2115** / **2116** (confirmation templates) — all from the
populate path. BuildScene labels have no inline caption arg; member-name text is assigned dynamically
at populate time.

#### 8.28.8 Residuals

- Final absolute on-screen origin (build-time dst (80, 200) may be re-anchored) — debugger-pending.
- The literal opcode numbers + relation-type VALUE semantics of the populate push — capture-pending.
- C2S verb → opcode mapping for `friend` / `cut` / the CP949 command — chat-command-table lane.
- Per-tab exact meaning (master/parent/spouse/fate) — inferred; confirm against `npc.scr` / `msg.xdb`.
- The exact `DefaultMenu` action-4002 → group-open dispatcher binding — DefaultMenu lane.
- Slot-185 `BuddyRelation` full layout — a separate deliverable.

### 8.29 StallListPanel — personal-stall / market list (key `l`) (CODE-CONFIRMED)

`StallListPanel` is the **personal-stall / player-vendor MARKET LIST** window — a searchable, sortable,
paged list of player-run stalls (distinct from the NPC item-shop vendor at slot 259). It lives at
**master service slot 228** and is toggled by the **`l` key**. It is a master-window-placed HUD child
(panel-local child rects below; absolute root origin debugger-pending).

#### 8.29.1 Geometry (CODE-CONFIRMED rects; panel-local origin)

Panel overall extent ≈ **375 × 481**. A header sub-panel (375 × 18 at (0, 0)) hosts the three title
buttons; a body sub-panel (375 × 463 at (0, 18)) hosts the list and controls. All source rects index
the single `stalllist.dds` atlas (§8.29.2).

| Widget | dst (x, y, w, h) | NORMAL src | PRESSED src | Action |
|---|---|---|---|---:|
| Title button A (refresh / own-list) | (333, 2, 11, 11) | (427, 160) | (427, 171) | 12 |
| Title button B (drag toggle) | (345, 2, 11, 11) | (438, 160) | (438, 171) | 13 |
| Title button C (close) | (357, 2, 11, 11) | (449, 160) | (449, 171) | 14 |
| Page button (prev) | (16, 355, 26, 26) | (375, 160) | (401, 160) | 15 |
| Page button (next) | (332, 355, 26, 26) | (375, 186) | (401, 186) | 16 |
| Page label ("cur / total") | (60, 363, 15, 13) | — | — | — |
| Row image strips ×58 | (18, 58+25·i, 339, 23), i=0..57 (y<308) | (2, 485) | — | — |
| Row hit buttons ×58 | (23, 58+25·i, 337, 21) | (transparent) | — | = row index |
| Search textbox (maxlen 30, IME on) | (25, 328, 325, 22) | (511, 511) | — | 28 |
| Action button L (search / submit) | (268, 321, 44, 28) | (375, 286) | (375, 286) | 29 |
| Action button R (reset / all) | (313, 321, 44, 28) | (375, 286) | (375, 286) | 30 |
| Column-sort buttons ×10 | (112+15·i, 361, 15, 14), i=0..9 | 15·(c+25), 226 (+28 if i≥5) | — | 17..26 |
| Sort button (wide name/owner) | (262, 361, 59, 14) | (375, 212) | — | 27 |
| Big button (left — ENTER selected) | (37, 410, 113, 40) | (375, 0) | (375, 40) | 11 |
| Big button (right — CLOSE) | (224, 410, 113, 40) | (375, 80) | (375, 120) | 10 |
| Header-drag strip (no action) | (0, 325, panelW, 26) | — | — | — |

#### 8.29.2 Atlas binding (CODE-CONFIRMED)

`StallListPanel` uses a single atlas at the **literal path `data/ui/stalllist.dds`** (hard-embedded
near the class data; the per-widget build argument is the default-atlas sentinel, not a uitex id). All
source rects in §8.29.1 index this one DDS. The numeric uitex id and its VFS DDS resolution are
VFS-pending; the literal path is certain.

#### 8.29.3 Row model (CODE-CONFIRMED)

- **Visible page = 10 rows**. A backing widget pool of **58** row widgets (image strip + hit button
  each) is built, but only the first 10 are populated/shown per page.
- **Row store** is a dynamic array of fixed-size records; each record holds a stall key/id, an
  availability/state byte (used for greying a row), and a CP949 backtick-delimited
  ``"stallName`ownerActorId"`` string. The render path splits on the backtick, resolves the owner
  actor id to a display name, and composes the row text as `"<stallName> - <ownerName>"`.
- **Pagination**: two arrow buttons (actions 15/16) + a "cur / total" page label; total pages =
  count/10 + 1.
- Fallback captions: `msg.xdb` **29022** (unknown owner), **29023** (placeholder / empty-row),
  **29018** (own-stall marker).

#### 8.29.4 Action-id map (onEvent — CODE-CONFIRMED)

| Action | Behaviour |
|---:|---|
| 0..9 | select visible row N → set selected id, highlight, show selection caption |
| (row double-click, rows 0..9) | set selected id AND emit ENTER-stall request (C2S 2/56), then close |
| 10 | CLOSE window |
| 11 | ENTER / open the selected stall → emit ENTER-stall request (C2S 2/56); `msg.xdb` **29010** if none selected |
| 12 | refresh: fetch the player's own-stall list (`msg.xdb` 16014) and repopulate |
| 13 | header drag (caches drag delta on the master window) |
| 14 | dismiss the body sub-panel |
| 15 / 16 | page up / page down → re-render |
| 17..26 | column-sort header buttons (10 columns) → re-render |
| 27 | wide name/owner sort header → re-render |
| 28 | focus the search textbox (enables IME) |
| 29 | SEARCH / SUBMIT: validate input, 10-second rate gate (`msg.xdb` 29017/29019/29020/29021), then emit the list-search request (C2S 2/74) |
| 30 | RESET / ALL: clear the filter, restore the placeholder (`msg.xdb` 29017/29018), re-issue the default list |
| ESC (key 27) | close window |

#### 8.29.5 Open mechanism (CODE-CONFIRMED) — key `l` toggle

The `l` key (ASCII 108) toggles slot 228: plays the standard UI cue, then if slot 228 is visible it
closes the panel, else it opens it (resets the search caption to the placeholder, un-greys, shows, and
re-renders). It is a pure toggle. The initial list request is issued by the search-submit / refresh
path — an open with an empty filter shows the last/empty store until a list-refill response returns.

#### 8.29.6 Opcodes (canonical names + major/minor only)

> Wire-field tables are owned by `opcodes.md` / `packets/`; only the names + ids are noted here:
> `CmsgStallListRequest` (**C2S 2/74**, search/request with a 30-byte CP949 name filter, empty filter =
> "all") ↔ `SmsgStallListRefill` (**S2C 4/74**, N×36-byte row records — the primary list round-trip);
> `CmsgStallEnter` (**C2S 2/56**, 4-byte selected stall id) — enter / open a selected stall. The
> opcode rows for 2/74, 4/74 and 2/56 are being added by the protocol lane.

#### 8.29.7 Captions (CODE-CONFIRMED ids; CP949 text VFS-pending)

`msg.xdb` ids: **105** (search/submit caption), **102** (reset/all caption), **16014** (own-list
refresh), **29010** (no stall selected), **29017** (search placeholder), **29018** (own-stall marker),
**29019** (search instruction), **29020** (rate-limit countdown), **29021** (input invalid), **29022**
(unknown owner), **29023** (empty-row placeholder).

#### 8.29.8 Residuals

- Absolute screen origin of slot 228 — debugger-pending.
- `stalllist.dds` numeric uitex id and VFS resolution — VFS-pending.
- Exact meaning of the C2S 2/56 id field (stall primary key vs. owner actor id) — capture-pending.
- Wire flag-bit usage in the 4/74 record (open / sold-out) — capture-pending.
- Which field each of the 10 sort columns sorts (data-driven) — not statically resolvable.

### 8.30 BroodWarListPanel — guild-diplomacy / brood-war relations list (key `u`) (CODE-CONFIRMED)

> **Scope correction (load-bearing).** Despite its class name, `BroodWarListPanel` is the
> **guild-diplomacy / brood-war RELATIONS list** — the roster of allies / war-declarations / enemies,
> keyed by a relation-state byte, with declare-war / declare-ally / cancel actions. It is NOT a list of
> scheduled brood-war events with per-row join buttons. Several actions are gated on the global
> brood-war phase byte (must equal 7 to create, ≥ 6 to register/declare); relation-state 5 is the
> brood-war / enemy bucket.

It lives at **master service slot 235** and is toggled by the **`u` key**. Master-window-placed HUD
child; panel-local child rects below; absolute root origin debugger-pending.

#### 8.30.1 Geometry (CODE-CONFIRMED rects; panel-local origin)

| Widget group | Count | dst (x, y, w, h) | NORMAL src | PRESSED src | Action |
|---|---:|---|---|---|---:|
| Top category tabs | 3 | (x∈{11,91,171}, 24, 89, 20) | (x, 73) | (x, 52) | 15..17 |
| Row check buttons | 6 | (30, 113+30·i, 23, 23) | (0, 0) | — | 6..11 |
| Row name buttons | 6 | (70, 113+30·i, 147, 14) | (0, 0) | — | 0..5 |
| Prev-page (◄) | 1 | (23, 303, 25, 25) | (277, 120) | (251, 120) | 13 |
| Next-page (►) | 1 | (202, 303, 25, 25) | (277, 146) | (251, 146) | 14 |
| Page label ("%s / %s") | 1 | (23, 330, 15, 13) | — | — | — |
| Refresh / list button | 1 | (194, 375, 45, 25) | (251, 94) | (297, 94) | 12 |
| Declare-war button | 1 | (30, 346, 64, 25) | (251, 26) | (251, 0) | 28 |
| Declare-ally button | 1 | (96, 346, 64, 25) | (446, 26) | (446, 0) | 29 |
| Cancel-relation button (init hidden) | 1 | (141, 346, 64, 25) | (381, 26) | (381, 0) | 30 |
| Tab-A button | 1 | (11, 346, 64, 25) | (251, 198) | (251, 172) | 31 |
| Tab-B button | 1 | (76, 346, 64, 25) | (316, 198) | (316, 172) | 32 |
| Tab-C button | 1 | (141, 346, 64, 25) | (381, 198) | (381, 172) | 33 |
| Header image strip (no action) | 1 | (52, 303, 146, 26) | (52, 303) | — | — |
| Footer image (no action) | 1 | (10, 375, 146, 26) | (251, 224) | — | — |
| Name input textbox (maxlen 16) | 1 | (13, 383, 146, 26) | (0, 0) | — | 34 |
| Descriptor child (no action) | 1 | (0, 0, 220, 200) | — | — | — |

Row blocks step y+30 (rows 0..5). Buttons whose source is `(0, 0)` and the header/footer images are
non-interactive decoration whose face is set per-row at bind time.

#### 8.30.2 Atlas binding (CODE-CONFIRMED)

`BroodWarListPanel` uses a single atlas at the **literal path `data/ui/broodwarlist.dds`**, bound to
every child on the show/refresh path. Sibling diplomacy windows use distinct atlases
(`data/ui/broodwarallystate.dds`, `data/ui/broodwarmap.dds`) and are out of scope. The numeric uitex
id / VFS DDS resolution is VFS-pending; the literal path is certain.

#### 8.30.3 Row model (CODE-CONFIRMED)

- **Visible window = 6 rows** per page. Page math: 10 pages per "wide page"; total page count =
  count/6 + 1; page label "%s / %s" = current linear page / total.
- Each visible row = a check/select button + a name button, populated from a source relation vector of
  fixed-size records (each carries a guild/target id, a relation-state value, and a CP949 name).
- **Filter / tabs:** a filter context selects the bucket — relation state < 5 (allies / diplomacy) vs.
  state == 5 (brood-war / enemy). The top tabs (actions 15/16/17 and 31/32/33) flip this filter.
- **Row colour by relation state** (host-int ARGB; exact byte order to confirm in the renderer):
  state 1 → blue-ish, 3 → orange, 4 → green, 5 → purple, else white.
- Selecting a row stores the target name + id + an aux field and positions the descriptor label.

#### 8.30.4 Action-id map (onEvent — CODE-CONFIRMED routing; VALUE semantics capture-pending)

| Action | Behaviour | Sends? |
|---:|---|---|
| 0..5 | row name buttons — select row N | no |
| 6..11 | row check buttons — also row select | no |
| 12 | refresh / re-page the visible list | indirect |
| 13 / 14 | prev / next page | no |
| 15 / 16 | top tabs — select category (filter) | no |
| 17..27 | tab / wide-page nav step | no |
| 28 | DECLARE-WAR — validates (phase ≥ 6, level, gold, banned name, time window) → opens a confirm popup | leads to send |
| 29 | DECLARE-ALLY / register — validates (phase ≥ 6, guild-master, cooldown, gold) → confirm popup | leads to send |
| 30 | CANCEL-RELATION — broadcasts a notice (`msg.xdb` **51014**), no send | no |
| 31 | validates (phase == 7, level, gold) → declare-ally request | leads to send |
| 32 | validates (phase == 7, guild-master) → confirm popup | leads to send |
| 33 | confirm-popup path | no |
| 34 | name-input textbox commit | no |
| ESC (key 27, when focused) | close panel | no |

> The confirm popup is a **sibling confirmation dialog at its own slot (167)**, NOT slot 235; the
> actual network SEND fires from that dialog's OK callback (which routes through the diplomacy C2S
> builder), not from this panel directly.

#### 8.30.5 Open mechanism (CODE-CONFIRMED) — key `u` toggle

The `u` key (ASCII 117) toggles slot 235: no-op if a panel group is open or chat is busy, else play
the standard UI cue and toggle the panel (the show path loads `broodwarlist.dds` and issues a list
request).

#### 8.30.6 Opcodes (canonical names + major/minor only)

> Wire-field tables owned by `opcodes.md` / `packets/`: `CmsgGuildDiplomacyDeclare` (**C2S 2/81**,
> 18-byte body = 1-byte action/state + 17-byte CP949 target-guild name) ↔ `SmsgGuildDiplomacyResult`
> (**S2C 4/81**, the relation-result push that rebuilds the roster; the existing IDB name on this
> handler is being adjusted by the protocol lane). The list-open request reuses the same C2S path with
> an action-byte value; whether a distinct minor exists for pure list-fetch is capture-pending. The
> 2/81 ↔ 4/81 rows are being added by the protocol lane.

#### 8.30.7 Captions (CODE-CONFIRMED ids; CP949 text VFS-pending)

Confirm-dialog labels (`msg.xdb` 49046..49056, 51002..51013, 21064..21066); action-30 notice **51014**;
precheck/validation notices (21025..21035, 2085, 51028..51031); S2C error-code → notice ids
(21074..21077, 2163..2171, 51016..51023, 51032; rank/gold 21044..21046, 21069).

#### 8.30.8 Residuals

- Absolute panel screen origin — debugger-pending.
- C2S action-byte value map (declare-war vs declare-ally vs list-request) — capture-pending.
- Whether list-fetch uses a distinct minor from declaration — capture-pending.
- Exact ARGB byte order of the row-state colours — confirm in renderer.
- `broodwarlist.dds` numeric uitex id / VFS resolution — VFS-pending.

### 8.31 GuildWarInfoPanel — guild-war info (display / read-only, key `j`) (CODE-CONFIRMED)

`GuildWarInfoPanel` is a **display / read-only** guild-war information window: a two-wide list of up to
**10 entries**, each = an item/icon + a name + a numeric value, plus an overall active flag. It emits
**no C2S packet** — the row buttons select/show, OK/Close dismiss, drag moves. It lives at **master
service slot 224**, is toggled by the **`j` key**, and is also opened by a server event. Object size
716 bytes; master-window-placed (HUD builder uses width 618 / 309). Absolute origin debugger-pending.

#### 8.31.1 Geometry (CODE-CONFIRMED rects; panel-local origin)

| Child | dst (x, y, w, h) | role |
|---|---|---|
| Title-bar panel | (0, 0, 618, 36) | top chrome (drag-handle host) |
| Body panel | (0, 36, 618, 273) | list host |
| Close button | (584, 11, 11, 11) | action 31 (close / drag) |
| OK button | (253, 235, 94, 27) | action 30 (OK / close) |
| Embedded descriptor widget | (0, 0, 220, 200) | tooltip / detail |
| Row buttons (10 rows × 3) | see below | item / name / value columns |

Row layout is a **two-wide list**: even rows in a left sub-column, odd rows in a right sub-column. Per
row group, the row Y = 34·(r/2) + 49 (34-px pitch, two entries share each step). Each row has three
buttons:

- **Left sub-column (even r):** col0 icon at x=62 (23×23); col1 name at x=95 (102×23); col2 value at
  x=211 (78×23).
- **Right sub-column (odd r):** col0 icon at x=326 (23×23); col1 name at x=359 (102×23); col2 value at
  x=475 (78×23).
- Per-row actions: col0 (icon) → action `r`; col2 (value) → action `r+10`; col1 (name) → action
  `r+20` (so icon = 0..9, value = 10..19, name = 20..29).

#### 8.31.2 Atlas binding (CODE-CONFIRMED)

`GuildWarInfoPanel` uses a single atlas at the **literal path `data/ui/moonpa.dds`**, assigned to every
child on show. Static source rects: Close button (199, 309)/(199, 320) 11×11; OK button (0, 309)/(94,
309) 94×27. The row icon buttons take their actual frame origins at bind time from the resolved item
descriptor (fallback origin (23, 0) when no item resolves). The numeric uitex id / VFS DDS resolution
is VFS-pending; the literal path is certain.

#### 8.31.3 Action-id map (CODE-CONFIRMED)

| Action | Behaviour | Sends? |
|---:|---|---|
| 0..9 | row icon (per row) — selection / display only | no |
| 10..19 | row value (per row) — numeric value display only | no |
| 20..29 | row name (per row) — name display only | no |
| 30 | OK button → close window | no |
| 31 | Close button + title-bar drag (records drag origin on the master window); ESC (key 27) also closes | no |

> **No C2S from this panel.** It is display/read-only. Any guild-war DECLARE / ACCEPT actions belong to
> the sibling guild-war declaration UI, not this info window.

#### 8.31.4 Info-field shape (CODE-CONFIRMED shape; VALUE meaning capture-pending)

The window is data-driven entirely from a server info block copied into the panel object. The populate
routine renders up to 10 entries; per entry: a presence/active flag, an item/icon id (resolved to a
frame origin), a name (a 17-byte string per entry), and a numeric value. Which value is score vs. timer
vs. wager, which name is "our guild" vs. "opposing guild", and the war-status enum are capture- /
debugger-pending — static gives the row SHAPE, not the meaning of each value.

#### 8.31.5 Captions (CODE-CONFIRMED)

This panel's construction consumes **no** `msg.xdb` caption ids — it is bare textured chrome with all
text data-bound. (Related war-state notice strings `msg.xdb` 42003..42006 live in a sibling notice /
banner flow that can OPEN this window on the "war" notice — cross-reference only, not this panel's
chrome.)

#### 8.31.6 Open mechanism (CODE-CONFIRMED) — key `j` toggle + server event

- **Key `j`** (ASCII 106) toggles slot 224: plays the standard UI cue; if open → close, else → open.
- **Server event:** the populate handler (S2C **5/73**) refreshes slot 224 (copies the info block, sets
  the open-state from a block byte, populates the 10-row list) and drives a sibling alarm/banner panel.
  A chat-notice → war-state parser can also open this window on the "war" notice.

> **Populated by S2C 5/73.** Opcode rows are owned by `opcodes.md` / `packets/`. **Naming conflict
> being arbitrated:** the 5/73 handler currently carries a quest-related name in the IDB, but slot
> 224's RTTI is `GuildWarInfoPanel` and the handler demonstrably refreshes this guild-war info window;
> the genuinely-quest handler is the neighbour (5/68 → the quest log at slot 223). The 5/73 → name
> reconciliation is being settled by the protocol lane / journal — both readings are recorded; do not
> silently reconcile.

#### 8.31.7 Residuals

- Absolute screen origin — debugger-pending.
- VALUE semantics of the 5/73 body (war status / opposing guild / score / timer / wager) —
  capture- / debugger-pending.
- `moonpa.dds` numeric uitex id / VFS resolution — VFS-pending.
- The 5/73 → handler-name conflict — protocol lane / journal to settle.

### 8.32 KeepPanel — player storage / warehouse (60-cell grid) (CODE-CONFIRMED)

`KeepPanel` is the **player STORAGE / WAREHOUSE ("keep") window**: a **60-cell item grid laid out 10
rows × 6 columns**, plus money deposit/withdraw, two page tabs, and a close button. It lives at
**master service slot 191**. It is **NOT a hotkey window** and has no `DefaultMenu` entry — it is opened
**only** from the storage NPC flow (KIND-9 NPC → KeepNpcPanel "open storage", §8.27 / §8.27.3). The
object is master-window-placed (constructor id 318, width 732); absolute origin debugger-pending.

#### 8.32.1 Geometry — the storage cell grid (CODE-CONFIRMED)

The grid is **60 cells = 10 rows × 6 columns**, each cell **38 × 38**. Cell `i` (i = 0..59): col = i % 6,
row = i / 6, **x = 45 + 38·col**, **y = 162 + 38·row**, action id = **200 + i** (so cell actions
**200..259**, row-major). Three parallel 60-element arrays sit over the grid: the 60 cell buttons (the
ones with actions 200..259), a 60-element overlay set (11 × 11 quantity labels at cell (x+20, y+26),
hidden by default), and 60 item-icon image components (38 × 38, hidden until populated). A separate
60-entry array hosts an optional rendered 3D item-actor per cell for the storage preview.

#### 8.32.2 Geometry — chrome (CODE-CONFIRMED rects; panel-local origin)

| Widget | dst (x, y, w, h) | atlas (uitex id) | NORMAL src | PRESSED src | Action |
|---|---|---:|---|---|---:|
| Image backdrop | (0, 85, 318, 625) | 4 | (317, 0) | — | — |
| Title / header bar image | (0, 36, 318, 50) | 2 | (0, 683) | — | — |
| Header glyph plate | (140, 60, 39, 17) | 4 | (248, 686) | — | — |
| Page/tab button A | (25, 105, 65, 20) | 4 | (612, 735) | (612, 714) | **261** |
| Page/tab button B | (90, 105, 65, 20) | 4 | (612, 777) | (612, 756) | **262** |
| Info-line label | (51, 598, 128, 15) | — | — | — | — |
| Deposit-money button | (183, 592, 53, 22) | 4 | (608, 812) | (608, 812) | **263** |
| Withdraw-money button | (238, 592, 53, 22) | 4 | (608, 812) | (608, 812) | **264** |
| Close / OK big button | (259, 655, 59, 77) | 2 | (301, 947) | (360, 947) | **260** |
| Quality-tint cell overlays ×4 | (0, 0, 38, 38) | 70 / 71 / 72 / 74 | (0, 0) | — | — |
| 3D item-preview canvas | — | — | — | — | — |

Window width = 318 (backdrop); the cell grid (cols x=45..235, rows y=162..504) fits inside. The four
quality-tint overlays carry hard-set ARGB colour words (rarity/quality border colours painted over a
cell).

#### 8.32.3 Atlas binding — uitex integer ids (CODE-CONFIRMED ids; DDS via uitex.txt VFS-pending)

`KeepPanel` uses **no** literal DDS path; all art is requested by uitex integer id through the runtime
`uitex.txt` manifest (`formats/ui_manifests.md`). Ids used:

| `uitex.txt` id | Use |
|---:|---|
| 2 | title/header bar, close button — primary keep chrome |
| 4 | backdrop, header glyph, the page/money button3states — secondary chrome |
| 14 | label / text / font atlas |
| 78 | the 60 cell item-icon image components |
| 70 / 71 / 72 / 74 | the four quality-tint cell overlays |

DDS resolution is VFS-pending.

#### 8.32.4 Action-id map (onEvent — CODE-CONFIRMED)

| Action | Behaviour |
|---:|---|
| 200..259 (hover) | build an item tooltip (storage container-type, cell slot = action − 200) over the cell |
| 200..259 (click, holding an item) | place / move the held item into this cell → **C2S 2/46** (move) or **C2S 2/44** (quick-move), gated by held-source vs. cell-target and the storage-mode flag |
| 200..259 (click, not holding) | pick up the cell's item (begin a local drag from the storage slot) |
| 260 | close / OK — close the window, restore the world HUD |
| 261 | page/tab button A → set active page 0 (swap tab frames, relayout) |
| 262 | page/tab button B → set active page 1 |
| 263 | deposit money — if player gold ≥ 1,000,000 → open the number-entry dialog (deposit mode, caption `msg.xdb` 2215); else error `msg.xdb` 45023 |
| 264 | withdraw money — if stored money > 0 → open the number-entry dialog (withdraw mode, caption `msg.xdb` 2216); else error `msg.xdb` 45023 |
| ESC (key 27, when visible) | close (same as 260) |

> **Storage slot math.** A cell click maps to the unified inventory slot space as
> `slot = action + 60·page + 56` — storage occupies a contiguous block starting at unified-slot
> base **+56**. The quick-move path additionally blocks a few specific quest-bound item ids (notice
> `msg.xdb` 2142, "cannot store this"); both send lanes guard on the keep-window-open flag (else post
> notice `msg.xdb` 38004 and abort).

#### 8.32.5 Open mechanism (CODE-CONFIRMED) — NOT a hotkey

KeepPanel is opened **only** via the storage NPC flow: click a **KIND-9** NPC → KeepNpcPanel (slot 152,
§8.27) → "open storage" (selector 1), which is **bag-count-gated (KIND-9 limit 50)** and emits the
storage open request **C2S 2/142** (reading the active-target NPC id from the router's stored field);
on pass it shows KeepPanel (slot 191), sets the keep-open / storage-mode flags, and hides the world HUD
panels. There is no keyboard hotkey and no DefaultMenu action for this slot.

#### 8.32.6 Opcodes (canonical names + major/minor only)

> Wire-field tables owned by `opcodes.md` / `packets/`. Item movement into / out of the storage cells
> uses the shared item-controller sends: **C2S 2/46** (move, 12-byte body) and **C2S 2/44**
> (quick-move, 12-byte body). The storage **open request and money deposit/withdraw** use **C2S 2/142**
> (16-byte body; op-byte = widget action − 7; the i64 amount sent only when > 0). The op-byte enum
> (deposit vs. withdraw vs. move value) is capture-pending. The opcode rows are owned by the protocol
> lane.
>
> **Refinement:** the two item-move lanes (2/46 / 2/44) are distinct from the money/open-request lane
> (2/142) — an earlier reading attributed all of deposit/withdraw to 2/142 alone. The binary shows
> item drag/quick-move into the grid uses 2/46 / 2/44; only the money buttons (263/264) and the
> open-request reach 2/142.

#### 8.32.7 Populate (S2C)

The 60 storage cells are filled by the **shared item-controller / item-panel server acks** (the same
per-slot item-refresh family that fills the bag), routed to the storage container view via the
unified-slot base +56 and the storage-mode flag — there is **no dedicated storage-snapshot opcode** in
this object. The exact shared item-panel S2C minor that targets the storage view is
capture- / debugger-pending.

#### 8.32.8 Captions (CODE-CONFIRMED ids; CP949 text VFS-pending)

`msg.xdb` ids: **2213** / **2214** (page/tab button A / B captions), **2215** (deposit-money dialog
title), **2216** (withdraw-money dialog title), **2219** (quantity-split dialog title), **2142**
("cannot store this bound item" notice), **38004** (quest-keep guard notice), **45023** ("not enough
gold / nothing stored" error). Per-cell tooltip text is built from the item record, not a fixed caption.

#### 8.32.9 Residuals

- Absolute on-screen origin — debugger-pending (placement depends on a runtime parent-panel anchor).
- uitex ids 2 / 4 / 14 / 70 / 71 / 72 / 74 / 78 → DDS — VFS `uitex.txt` pending.
- The storage send op-byte enum (deposit / withdraw / move value) — capture-pending.
- The shared item-panel S2C minor that targets the storage view — capture- / debugger-pending.
- Page/tab count (2 pages × 60 vs. 60 with 2 tab views; the slot math adds 60 per page) — debugger-pending.
- The four quality-tint overlay ARGB constants → rarity-tier mapping — config-pending.

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

The login window owns **two separate texture lists**, not one shared list (CODE-CONFIRMED load
model; VFS-VERIFIED paths):

- The **LoginWindow texture list** holds **four atlases**, all loaded **eagerly** in `BuildScene`
  (append-only, load-order keyed, no dedup, no ref-count).
- The **PIN / second-password window** owns its **own separate list of two atlases**, loaded
  **lazily** when the PIN modal is built — it does **not** share the LoginWindow list.

**LoginWindow list (4 atlases, eager):**

| Path | Role | Confidence |
|---|---|---|
| `data/ui/login_slice1.dds` | **Primary form atlas** (slot 0): OK/Login, Server-list, ID/PW textboxes, Save-ID checkbox, Quit/Help strip | VFS-VERIFIED (path) / CODE-CONFIRMED (role) |
| `data/ui/loginwindow.dds` | (slot 1) Backdrop, EULA panel background, plate base, version image, option/tab buttons, server name-strip, decorative elements | VFS-VERIFIED / CODE-CONFIRMED |
| `data/ui/InventWindow.dds` | (slot 2) Modal chrome (quit-confirm / exit / error composites) — shared with the in-game inventory window | VFS-VERIFIED / CODE-CONFIRMED |
| `data/ui/loginwindow_02.dds` | (slot 3) Server-plate parchment + spinner column; panning intro banner strips | VFS-VERIFIED / CODE-CONFIRMED |

**PIN-window list (2 atlases, lazy):**

| Path | Role | Confidence |
|---|---|---|
| `data/ui/password.dds` | PIN keypad sprite-sheet (its own texture list, separate from the LoginWindow list) | VFS-VERIFIED / CODE-CONFIRMED |
| `data/ui/InventWindow.dds` | PIN modal chrome (loaded again into the PIN window's own list — not reused from the LoginWindow list) | VFS-VERIFIED / CODE-CONFIRMED |

**Version gate asset:** `data/cursor/game.ver` — a 28-byte binary blob (7 × u32 LE) that is the
source of the login version GATE (not a displayed label). The OK/Login click compares the
VFS-embedded `data/cursor/game.ver` against the on-disk `game.ver` at the client root; a mismatch
raises the version-error modal (msg id 2204) and may quit. The field-level byte layout is specified
in `formats/config_tables.md §7`; here it is referenced only as the login-gate asset. Confidence:
VFS-VERIFIED (path/size) / CODE-CONFIRMED (gate role).

### 9.2 Character-select screen

The character-select view, the character-create form, and the live 3D preview are all one window
object; its 2D chrome composites from **four atlases** (VFS-VERIFIED paths):

| Path | Role | Confidence |
|---|---|---|
| `data/ui/loginwindow.dds` | **Primary** chrome: backgrounds, slot-row plates, stat-icon grids, appearance ± steppers, tab buttons, Create/Delete | VFS-VERIFIED / CODE-CONFIRMED |
| `data/ui/mainwindow.dds` | Slot-row plates + the **create name-plate** atlas (Create/Delete/Enter button strips at src V=1004) and other create-form widgets | VFS-VERIFIED / CODE-CONFIRMED |
| `data/ui/InventWindow.dds` | Modal chrome (confirm / delete / name-entry dialog frames), shared with login | VFS-VERIFIED / CODE-CONFIRMED |
| `data/ui/blacksheet.dds` | Loaded by the window builder for a **later create-form sub-panel** (and the dim/blackout overlay). **NOT a corner-X frame close** — that prior claim is REFUTED (the close is a system-close message-handler branch → state 6 sub-state 8, not an atlas-blit widget; see §8.2 verified-facts note). | VFS-VERIFIED; close-as-widget REFUTED |
| (GUCanvas3D live 3D viewports) | Character previews; not a 2D atlas | CODE-CONFIRMED |

> **Correction.** Earlier versions of this section listed `carrierpigeonperson.dds`,
> `carrierpigeonall.dds`, and `tradekeepwindow.dds` as char-select atlases. The reconciled builder
> walk shows **no character-select reference** to those three — they are dropped from the
> char-select manifest. The char-select chrome is the four atlases above.

### 9.3 Opening cinematic (state 3) screen

| Path | Role | Confidence |
|---|---|---|
| `data/ui/openning_001.dds` through `openning_004.dds` | Cinematic frames 1–4 (768 × 1024) | SAMPLE-VERIFIED |
| `data/ui/openning_scenario.dds` | Large intro splash (2048 × 1024) | SAMPLE-VERIFIED |

### 9.4 Loading (state 2) screen

| Path | Role | Confidence |
|---|---|---|
| `data/ui/loading.dds` | Generic loading screen (DXT3) | VFS-VERIFIED |
| `data/ui/loading01.dds` through `loading08.dds` | Area-specific loading screens (8 variants, DXT2) | VFS-VERIFIED |
| `data/ui/loadingbar.dds` | Progress bar texture (256 × 256) | VFS-VERIFIED |

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

### 9.6 Front-end sound cues (id list)

The front-end screens drive their audio by **integer sound id** through the 2D sound subsystem; the
id → file resolution rule and the full cue catalogue live in `specs/sound.md` (do not duplicate that
table). The cue ids the login / opening / loading / character-select screens use are listed here so
an implementer can wire the triggers (all VFS-VERIFIED present under `data/sound/2d/`; the
trigger/event role is SPEC-CARRIED from `sound.md` / `frontend_scenes.md`):

| Cue id | Where used | Confidence |
|---|---|---|
| `910061000` | Opening / intro looped BGM (opening-cinematic state) | VFS-VERIFIED + SPEC-CARRIED |
| `910062000` … `910065000` | Per-class create-form preview BGM (one per class; replaces the scene BGM on the single category-0 voice) | VFS-VERIFIED + SPEC-CARRIED |
| `920100100` | Loading-screen looped BGM | VFS-VERIFIED + SPEC-CARRIED |
| `920100200` | Character-select looped BGM (also the enter-world confirm cue on the same category-0 voice) | VFS-VERIFIED + SPEC-CARRIED |
| `861010101` | Generic UI click / confirm SFX (char-select dispatch, stepper/confirm clicks) | VFS-VERIFIED + SPEC-CARRIED |
| `861010105` | Login intro stinger (fired at login-window intro sub-state) | VFS-VERIFIED + SPEC-CARRIED |

> Per-class preview cues and the scene/loading BGM all play on a **single category-0 music voice**,
> so a later cue **replaces** the earlier one rather than overlaying it (see `sound.md`). The exact
> per-class id↔class mapping (it is **not** the identity) is owned by `frontend_scenes.md`.

### 9.7 Cursors (front-end and in-game)

Cursors live under `data/cursor/` as **DXT2-compressed DDS textured quads** — there is **no `.cur`
or `.ani`** file anywhere in the VFS (the cursor is drawn as a textured quad, not registered as a
Win32 cursor resource). VFS-VERIFIED cursor set:

| Path | Role | Confidence |
|---|---|---|
| `data/cursor/stand.dds` | Default / pointer cursor (front-end default) | VFS-VERIFIED |
| `data/cursor/battle.dds` | Combat / attack cursor | VFS-VERIFIED |
| `data/cursor/hand-jap-01.dds` … `hand-jap-04.dds` | Hand / interact cursor variants (4) | VFS-VERIFIED |
| `data/cursor/repaircursor.dds` | Repair / craft cursor | VFS-VERIFIED |
| `data/cursor/rotate.dds` | Camera-rotate cursor | VFS-VERIFIED |

> The exact cursor-state → DDS routing table is not pinned from the VFS alone (the per-state binding
> is **debugger-pending**); the texture set above is VFS-confirmed complete.

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
- `0xC8`–`0xD4` (200–212 decimal) — time / duration format-strings (VFS-VERIFIED correction; an earlier version of this spec mislabelled this range as "character create / rename error messages", which is wrong — the strings at ids 200–212 are time/duration format templates, not create/rename errors)

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
| 5 | **In-game** | Allocates the main handler (size 0xC8); builds the game world; registers **three** event targets (the master window, the main handler, and a third "view" object) onto the per-scene disposable list, each with a matched un-register; runs the engine main loop; on logout/return ends and destroys | **→ 4** (returns to character select, NOT to login) | CODE-CONFIRMED |
| 6 | Quit | Tears down the engine | → 8 | CODE-CONFIRMED |
| 7 | Error | Closes the network connection and shows a message box. The message id is **branchy** (not a flat `9001 + [1]`) — see the note below | → 8 | CODE-CONFIRMED |
| 8 | Exit | Engine shutdown; WinMain returns (the `default` case also forces state 8) | (loop ends) | CODE-CONFIRMED |

> **Key non-obvious edge:** in-game (state 5) transitions **back to character select (state 4)**,
> not to login. Login is only visited once per process lifetime. The load / opening gate (state 2)
> is also visited only once, after the first successful login.

> **Re-walk confirmation (CODE-CONFIRMED).** An independent campaign-frontend re-walk of the master
> loop re-confirms this table exactly: the loop mounts the data archive once, then runs a switch on the
> primary scene state. **State 0 = cold bootstrap** (net-handler + top window + display-mode select),
> **state 1 = Login** (where the login window and the 15-slot font table are built and the engine/device
> is brought up; a device/window failure routes to 7), **state 2 = the opening-or-skip gate** (reads
> INI `[OPENNING] SKIP`: skip → 4, otherwise build the loading window → 3), **state 3 = Opening
> cinematic**, **state 4 = character-select specifically** (the login window is built at the state-1
> init case, not here), **state 5 = in-game → returns to 4 (never to Login)**, **state 6 = Quit → 8**,
> **state 7 = net/engine guard** (closes the connection, shows a message box → 8), **state 8 =
> clean Exit** (the `default` case also forces 8). The switch selector is effectively off-by-one from
> destination-named labels: each case writes the state for the phase it is about to build (state 0
> writes 1, state 1 writes 2, state 2 writes 3 or 4, state 3 writes 4, state 4 writes 5, state 5 writes
> 4, state 6 writes 8, state 7 writes 8). The state-8 check lives in the shared loop tail: when the
> selector reads 8, the loop performs the final shutdown and returns.

> **State-7 error message-id is branchy, not a flat `9001 + [1]` (CODE-CONFIRMED).** The error case
> has **two** id paths: (a) if the secondary-state slot `[1] == 8` **and** the explicit error-id slot
> `[2]` is non-zero, it shows the message keyed by `[2]` directly (an arbitrary caller-supplied id);
> (b) otherwise it maps the secondary state `[1]` through a **state-name / error-string mapper** to
> obtain the caption. The "`9001 + [1]`" form quoted in an earlier brief is the *effect* of one branch
> only, not the universal formula. After the message box it closes the network connection (via the net
> client's release vtable entry) and advances to state 8.

> **Per-scene engine main-loop runner (CODE-CONFIRMED).** "Runs the engine main loop" in the table
> above is a single shared runner the loop calls once per scene. Its shape: it raises timer resolution
> (`timeBeginPeriod(1)`), sets a module-global **run flag** to 1, then spins
> `do { tick(scene_driver); render-step; } while(run-flag)` — ticking the active scene driver and
> rendering each frame until the run flag is cleared (a type-13 loop-break event, §15.4, clears it).
> This is the literal "ticked by the engine's main loop until done" contract; an `Application`-layer
> reimplementation should model the per-scene tick/exit-flag loop, not a single global frame pump.

> **State-5 in-game build / three-object registration (CODE-CONFIRMED).** State 5 first sets the
> *next* state to 4, allocates the in-game main handler (size 0xC8), then obtains two more objects: the
> master window (via its singleton accessor) and a third **in-world "view"** object. All **three** are
> pushed onto the per-scene disposable list (§15.1) — the master window's secondary handler, the main
> handler, and the view — each paired with a matching un-register on scene exit. The concrete HUD is
> then materialised by a large **in-game build/activation routine** that invokes `BuildScene`
> (vtable slot 14 / byte +56) on the master window's embedded sub-windows and builds the in-game scene
> graph from the **service slot 320 (byte +0x500 / 1280)** handler (§1.6). That activation routine is
> what turns the zero-initialised service-slot table into a populated HUD; the matching teardown re-zeros
> the slot registry (§1.6) and releases the world refs. The identity of the third "view" object is
> static-tentative (likely the in-game world / 3D view) and is the lone residual here.

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

> **PIN show-gate confirmation (CODE-CONFIRMED — independent re-walk).** An independent campaign-frontend
> re-walk confirms that the field driving this Tick/workflow machine (the same substate field this table
> documents) **does** hold the values **31** and **32**, and that the PIN/second-password child panel's
> visibility is set on the **31 → 32 edge** (reachable only after the substate-29 credential gate).
> Substate 32 polls `(panel visible AND submitted)` and advances to 33. **This Tick/workflow substate is
> the field that gates PIN visibility.** This resolves a cross-spec conflict in which `frontend_scenes.md`
> had stated "neither 31 nor 32 is a login sub-state" — that observation was about a *separate form/page*
> state (which indeed never holds 31/32); the *Tick/workflow* substate documented in this table does. The
> account/save flag that gates entry into substate 31 is debugger-pending.

> **PIN keypad digit→slot scramble (mechanism CODE-CONFIRMED; runtime seed + permutation
> DEBUGGER-PENDING).** The mapping of which digit appears on which keypad tile is **not** a static table
> and **not** a constant-seeded shuffle. It is a **clock-seeded Fisher–Yates shuffle** of the 10-digit
> pool: the routine seeds the standard library RNG from the current wall-clock time, then shuffles the
> pool, applying the platform 15-bit-RNG range-extension to widen each draw, so tile position `p` displays
> `permutation[p]`. The pool is **re-rolled on modal open, on Reset (keypad tag 11), and after an
> OK-submit (keypad tag 12)**. The keypad-internal control tags are **11 = Reset / reshuffle, 12 = OK /
> submit, 13 = Cancel**. The shuffle **mechanism** is code-confirmed; the **runtime seed value and the
> resulting permutation are clock-derived and therefore debugger-pending** (not a recoverable code
> immediate).

> **Two coupled state fields — workflow `+0x238` vs handler page-state `+0x17C` (CODE-CONFIRMED).**
> There are **two** state fields on the login window, and they are coupled. (1) the **Tick/workflow
> sub-state at +0x238** — the field this table documents, driven and consumed by the per-frame Tick.
> (2) a **handler page-state at +0x17C** on the embedded event-handler sub-object — the field the
> **click/key OnEvent override writes**. On this build the OnEvent handler writes the workflow targets
> (the literal values **29, 34, 37, 38, 5**) into the **+0x17C** page-state, and the Tick loop reflects
> them into / consumes them from **+0x238**; the two move together (the handler drives, the Tick
> follows). The earlier "separate form/page state" footnote is now pinned: that separate field is
> **+0x17C**, and it is the one the OnEvent switch mutates. A reimplementation that prefers a single
> state enum can collapse both, but must drive the enum from the OnEvent click/key path the way +0x17C
> is driven here. The OnEvent edges confirmed on this build: Enter/OK at the form-active state advances
> to **29** (only after the version gate, below); plate-pick at the server state advances to **37→38**;
> the help/quit control advances to **34**; the option-tab path advances to **5**.

> **Sub-state 29 entry is gated by the `game.ver` version check (CODE-CONFIRMED).** The OK/Login
> action (id 103 / `g`) and the Enter key at the form-active state set the workflow sub-state to **29**
> **only after** a `data/cursor/game.ver` version gate passes; a failed gate raises the version-mismatch
> error modal (msg **2204**) instead of advancing. This confirms the 103/`g` → version-gate → credential
> sub-state path (§8.1 action map).

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

12. **Inventory in-game window full layout: RESOLVED.** The in-game inventory bag is the `ItemPanel`
    8 × 5 = 40-cell grid (38 × 38 cells, +38 px pitch, action ids 0..39), the 20-cell equip/quick
    sub-grid (action ids 50..69), and a hand-placed equipment paperdoll — all CODE-CONFIRMED in §8.10.1.

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

16. **Inventory bag runtime atlas: RESOLVED (2026-06-17, CODE-CONFIRMED).** The bag is the
    `ItemPanel` grid (§8.10.1), and its per-instance atlas bind site is now traced: the window's
    `data/ui/InventWindow.dds` atlas is registered into the per-window texture list at the top of the
    in-game HUD panel builder, and the cells resolve handles by `uitex.txt` id 2 / 14 / 69 / 71 / 78.
    (The earlier texture-id-0 / untraced-runtime-bind note referred to the `GatherSlotPanel` gather/
    craft panel of §8.10.2, whose slot sprites still resolve their per-instance atlas at runtime.)

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

---

## 15. Diamond UI framework — verified lifecycle, attach primitives, and event model (CODE-CONFIRMED)

> Campaign 9D promoted a byte-exact re-verification of the whole toolkit against the binary. The base
> component, the 16-slot vtable, Panel/Button/Label, the sprite render path, and the click→action
> dispatch all **confirm** §1–§7 at the byte level (offsets re-pinned, no contradictions). This section
> records the additions that the §1–§7 tables did not already state: the universal **attach
> primitives**, the **two-phase widget destroy/End** lifecycle, and the **input-event struct** the
> dispatch reads. All confirmed unless noted.

### 15.1 The universal widget-attach primitives — (CODE-CONFIRMED)

There is **no widget-manager object** — a parent panel owns its children directly. Two helpers are
the only universal attach primitives (both already named in §7.4; restated here as the framework
contract):

| Primitive | Effect | Use |
|---|---|---|
| **AddChild(parent, child)** | sets `child.parent (+0x84) = parent`; pushes the child pointer into the parent's child vector (+0xA4). Paint order = insertion order. | decorative / non-clickable widgets |
| **AddChildWithAction(parent, child, actionId)** | same, **plus** `child.actionId (+0x10) = actionId`. | clickable widgets — this is how a widget gets its command id |

Both are extremely high-frequency call sites (each in the four-figure range), which is why every
hardcoded `BuildScene` is a long sequence of these two calls. The action id stored by the second
primitive at **+0x10** is exactly the id the slot-10 getter returns and the window dispatcher routes
on a click (§1.4, §15.4).

> **Disposable-tracking is NOT attach — (CODE-CONFIRMED, corrected).** A separate per-scene helper
> pushes a freshly-constructed window/sub-object onto a **scene-teardown list** so a scene's owned
> objects are released together when its tick loop returns. Earlier notes mislabelled that helper a
> "universal attach helper" (and one IDB symbol still mis-names it an "OpeningWindow ctor", §15.6); it
> is a **register-for-teardown / disposable-tracking push**, not a widget-tree attach. The real attach
> primitives are the two AddChild* functions above. (See `specs/client_runtime.md §7.9.2`.)
>
> **The teardown list is a `std::list`, held by a process-global singleton (CODE-CONFIRMED).** The
> registry is a `std::list<void*>` reached through a one-shot-guarded **process-global singleton**
> (atexit-registered), **not** a per-window field. The push helper builds a 12-byte intrusive
> `{next, prev, ptr}` node and bumps a count (the count-grow path throws the standard
> `length_error("list<T> too long")` — the definitive proof it is `std::list`, not a manager); the
> matched erase walks the list head, matches the node's pointer key, unlinks, frees, and decrements.
> WinMain pairs every push with an erase. The singleton has **~35 registration sites** — the loading,
> opening, character-select, and main windows, **plus several network S2C handlers** (the enter-game
> ack, the character-list handler, and a game-state-tick handler). So some UI objects that are
> constructed in response to a server packet are torn down through this same per-process scene-object
> list. This is the concrete identity behind §11.2's "ticked … until done, then ended and destroyed".

### 15.2 Widget lifecycle — create → attach → show/hide → End/destroy — (CODE-CONFIRMED)

- **Create.** `new(classSize)` then the class constructor: the constructor calls the shared base
  image-component constructor (sets the common fields of §1.2), overwrites +0x00 with the class
  vtable, then inits the class-specific fields. Construction is complete after the constructor — there
  is no separate "init" call. A concrete window additionally overrides **slot 14 (BuildScene)**, which
  is invoked once to populate the widget tree **eagerly** (e.g. the login window preloads its atlases
  into its per-scene texture list, then constructs all of its widgets in one BuildScene pass — §8.1,
  §9.0).
- **Attach.** via the two §15.1 primitives.
- **Show / hide.** `SetShown(bool)` (vtable slot 1, or its slot-15 alias) writes the visible byte
  (+0x8C) and the edge byte (+0x8D) to the bool, and **snaps** alpha (+0x04) to 255 (show) or 0 (hide);
  the per-frame leaf draw then maintains/animates alpha ±64/tick (±32 for the Ex variant), clamped
  [0, 255]. A parent's draw loop skips any child whose visible byte ≠ 1. Slide-in / curtain transitions
  (e.g. the login curtain, §7.7) are **not** a vtable feature — they are the per-state machine moving a
  child's local Y each tick, distinct from the alpha fade.
- **Destroy / End — two phases.** Teardown is **(1) a panel-specific `End`/reset, then (2) the base
  destructor chain**:
  1. **`End`/reset.** A panel walks its child vector clearing each child's active byte, then **chains
     the base End** (via the per-class hook, vtable slot 8); if the panel holds an auxiliary object
     (e.g. a 3D view), it releases that aux (the aux object's own release call) and nulls the slot.
     This is the standard "reset children → chain base → release held aux" shape.
  2. **Scalar-deleting destructor (slot 0).** Resets the vtable and frees the object if the
     deleting-flag is set. A window's teardown also walks its per-scene texture list (+0x220) and
     releases each atlas handle once (no ref-count) — see `specs/resource_pipeline.md §"Per-scene
     window texture lists"` and §9.0.

### 15.3 The 2D render path and insertion-order z-model — (CODE-CONFIRMED — confirms §3, §7.3)

The container `Draw` (slot 7) does: `UpdateTransform(self)` (slot 9) → leaf draw of the panel's own
sprite → then, **forward** from the child-vector begin to end, for each child call slot 9
(`UpdateTransform`) and — if the child's visible byte (+0x8C) == 1 — slot 7 (`Draw`). **Forward
iteration = back-to-front paint; later-added children paint on top. There is no z-index field.** The
leaf draw integrates alpha toward the visible target, then (if a texture is bound and visible) submits
one sprite via the shared `ID3DXSprite` batch path: `SetState(alpha-blend)` → `SetTransform(matrix
@+0x44)` → `Draw(texId @+0x90, srcRect @+0x34, color = (alpha<<24)|(tint @+0x0C & 0xFFFFFF))` →
`Flush()`. The src-RECT selects the atlas sub-rect; the matrix is a pure translation to the world
(x, y) on the 1024×768 reference canvas. This is the **D3DXSprite batch path**, not manual vertex
quads (confirms §3.3). The ortho/2D projection is the engine's frame-begin, not per-widget.

### 15.4 Click → hit-test → action-id → OnEvent dispatch chain — (CODE-CONFIRMED)

The complete dispatch pipeline, end to end:

1. **Input-event struct (CODE-CONFIRMED event-type catalogue).** The event the dispatch reads is a
   small struct whose first byte is the **event type**. The full type catalogue:

   | Type byte +0 | Event | Notes |
   |---|---|---|
   | 1 | key-down | dword[+4] = key code |
   | 2 | key-up | dword[+4] = key code |
   | 3 | mouse-move | broadcast to all children for hover update |
   | 4 | button-press | pointer down |
   | 5 | button-release | pointer up |
   | 6 | **CLICK** | **synthesised** only when the release lands on the same widget that was pressed (the click-vs-drag discriminator); dword[+12] is a button/flag value gated `== 1` |
   | 7 | double-click | (the front-end repurposes the second pointer-hit branch for plate-pick, below) |
   | 8 | wheel | wheel delta at dword[+4] |

   **Source of each class:** **keyboard events (1/2) come from a DirectInput8 keyboard thread**;
   **mouse events come from the window message handler (WndProc)**. The type-6 click is never raw — it
   is manufactured by the capture logic (§4.2) only on a same-widget press-then-release, which is what
   makes it the genuine click-vs-drag discriminator. **Dispatch is topmost-child-first, first-consumer-
   wins, and runs before the 3D world view** so a UI click never falls through to world-entity
   selection while a widget consumes it. Move/key events (1/2/3) are broadcast to all children for
   hover-state update; the pointer/click events (4/5/6/7) consume on the first hit.
   - **This-build specifics (login window).** The key path reads the key code at dword[+4]: **9 =
     field-swap / Tab**, **10 = Enter / OK**; **27 = ESC** drives the window's cancel/close path when
     the cancel-enabled byte is set. The login window also uses the second pointer-hit branch (type 7)
     as a **server-plate pick**, and recognises an internal **type-13 "loop-break" event (key value
     10001)** that clears the engine run-flag to exit the scene loop (§11.2 main-loop runner).
2. **Panel hit-test route (slot 6, the shared panel/window dispatch).** If the panel is invisible
   (+0x8C == 0) it returns "not consumed". For move/key events it iterates children **in reverse**
   (end → begin) calling each child's slot 6; on a child returning consumed, it stores that child's
   `GetActionId()` (slot 10, = field +0x10) into the panel's active-child index (+0xB4) and returns
   consumed. **Reverse order = topmost-painted child wins input** (the inverse of the forward paint
   walk). Click events do the same reverse hit-test and read the first consuming child's action id.
3. **Mouse-down/up capture.** Mouse-down on a focus-eligible widget sets the global click-capture
   pointer to that widget (§4.2). Mouse-up while still captured by the **same** widget builds a
   synthetic **type-6** "click-released" event (with dword[+12] = 1) and routes it back through the
   window's OnEvent.
4. **Window OnEvent switch.** The window-level event override calls the base panel dispatch first; if
   it consumed a type-6 click (and dword[+12] == 1) it reads the window's selected command field and
   **switches on the action id**, invoking the matching handler (e.g. char-select Create = 4 /
   Delete = 5 / Enter = 6, §8.2). For a key event (type 1/2) with code 27 (ESC) and the window's
   cancel-enabled byte set, it invokes the cancel/close path. On the login window the same switch
   reads key code 9 (field-swap / Tab) and 10 (Enter / OK) — see §15.4 step 1 this-build specifics.

So the one-line chain is: **click pixel → reverse child hit-test → first-hit child's GetActionId
(+0x10) → synthetic type-6 event → window OnEvent switch on the action id → concrete handler.**

### 15.5 D3DXFont / MessageDB text path — (CODE-CONFIRMED — confirms §6)

Text is drawn through the 15 D3DX font slots (§6.2), created once at boot from Korean system typefaces
with the Hangul charset (**no VFS glyph atlas for body text**). The shared text helper computes a
fixed-advance destination rect (`charWidth(slot) × strlen` wide, `rowHeight(slot) × scale` tall — a
**monospace grid even with proportional faces**) and calls the font's draw-text. Captions are resolved
through the message catalogue (`msg.xdb`, 516-byte records keyed by id, §10) and are raw CP949 byte
strings; the OS code page handles the Hangul composition via the Hangul charset. Caption colour by
state: disabled = grey, hovered = yellow, else the per-widget highlight (+0xF4 if set) or the tint
(+0x0C). The front-end uses font **slot 0** universally (no per-widget slot write in the front-end
build routines — §6.3); in-game windows select larger/bold slots in their own builders.

### 15.6 Modal / focus model — global input-focus manager, not a modal window class — (CODE-CONFIRMED)

There is **no separate "modal window" class or modal-layer object**. "Modal focus" is a small,
uniform protocol applied by a shared **show-modal helper** on `GUPanel` that virtually every concrete
panel (the login ID/PW textboxes, chat input, stall, guild, GM, …) calls at show-time:

1. Fetch the single **process-global UI input/focus manager** (one singleton — the IME-target owner).
2. **Register `this`** panel into that manager (so it becomes the focused input target).
3. **Enable IME composition** (the IME-context enable call) so CP949 Hangul composition routes here.
4. **Set the focused byte at +0x8B = 1** on the panel.
5. Run a manager refresh and assign the panel's class-name caption.

So focus/modality = **register-into-the-global-input-manager + focused byte (+0x8B) + IME-on**, with
no z-locking or input-eating layer beyond the ordinary "topmost-painted child wins the reverse
hit-test" rule (§15.4 step 2) plus this focused-input registration. Only one panel is the focus /
IME target at a time (registering a new one supersedes the previous). This refines §5.1 (textbox IME
registration) and §4.2 (global click-capture): the click-capture pointer is the *pointer* capture for
the press/release click discriminator, while this focus registration is the *keyboard/IME* target —
two independent global pointers. The login window arms this path through the field-focus actions
(`m`/`n`) and the Tab key (code 9).

### 15.7 IDB-symbol mislabels (analyst note — NOT spec errors) — (CODE-CONFIRMED)

The semantics above are confirmed; this note records that **three IDB symbol names** are misleading
so a future reader of the database is not misled. These are database-symbol issues only — **the spec
framing in §1.6 / §11.2 / §15.1 / §15.4 is correct** and needs no change:

- The function the IDB currently names as an **"OpeningWindow constructor"** is **not** a constructor
  and **not** opening-window-specific — it is the **scene-dispose-list push** of §15.1 (insert a
  `{next, prev, ptr}` node into the per-process scene-teardown `std::list`). Proposed rename:
  `Diamond_SceneDisposeList_Push`.
- The two functions the IDB currently names as a **"GUCmdHandler" dispatch/find pair** are **not**
  an action-id command table — they are **generic intrusive-list find/erase plumbing** (walk a list
  head, match a node key, act), invoked from window/handler ctors with the dispose-list singleton as
  `this`. They are the same disposable-registry plumbing, **not** the per-window command dispatch.
  The real action-id dispatch is the **inline window `OnEvent` switch** (§15.4 step 4) — it must
  **not** be associated with these two list helpers. Proposed renames:
  `Diamond_List_FindByKey_Wrapper` / `Diamond_List_FindByKey`.

(The renames belong to `ida-naming-sync` / `names.yaml`; this spec only records that the symbols are
stale so the §1.4 "embedded command handler" / §15.1 "disposable push" prose stays authoritative.)
