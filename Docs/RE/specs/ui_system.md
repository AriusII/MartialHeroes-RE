# UI System — Widget Toolkit, Screen Layouts, and Scene State Machine

> Clean-room neutral spec. Promoted from dirty-room analyst notes by the asset-spec-author.
> No legacy symbols, no addresses, no pseudo-code.
> Describes the widget class hierarchy, hardcoded screen layout coordinates, per-screen
> asset manifests, font system, string database lookup, master scene state machine, and
> Godot reconstruction guidance — so the presentation layer can be rebuilt faithfully.
>
> status: CODE-CONFIRMED (class hierarchy, constructor signature, state machine, font table,
>         asset paths); SAMPLE-VERIFIED (atlas file presence in VFS — see formats/ui_manifests.md);
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
| `GUComponent` | — | Base widget: local position, size, alpha, world-rect, transform matrix, timer, visible/enabled flags | CODE-CONFIRMED |
| `GUPanel` | GUComponent | Container: owns a child-pointer vector and an active-child index; hit-test walks children | CODE-CONFIRMED |
| `GUWindow` | GUPanel | Top-level window: embeds a command handler (action-id dispatch), an auxiliary view, and a texture list | CODE-CONFIRMED |
| `GUButton` | GUComponent | Clickable button; two constructor variants: **2-state** (one sprite for all interaction states) and **7-state** (separate sprite coords for normal, hover, pressed, disabled) | CODE-CONFIRMED |
| `GUCheckBox` | GUButton | Toggle button; maintains checked/unchecked state | CODE-CONFIRMED |
| `GULabel` | GUComponent | Read-only text display; owns two CP949 string fields (caption and an auxiliary text) | CODE-CONFIRMED |
| `GUTextbox` | GUComponent | Editable text field; registers itself into the IME focus slot so Korean composition works | CODE-CONFIRMED |
| `GUList` | GUComponent | List / listbox; 11 specialised virtual slots | CODE-CONFIRMED |
| `GUScroll` | GUComponent | Simple scrollbar (up/down button children + thumb) | CODE-CONFIRMED |
| `GUScrollEx` | GUPanel | Extended scrollbar with panel-hosted children | CODE-CONFIRMED |
| `GUCanvas3D` | (GUComponent) | 3D viewport widget — renders a live model into a 2D UI rectangle; used for character previews on the select screen | CODE-CONFIRMED |

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
| `srcX` | +0x24 / +0x34 | Source atlas X: the U origin into the sprite sheet for this widget's sprite | CODE-CONFIRMED |
| `srcY` | +0x28 / +0x38 | Source atlas Y: the V origin into the sprite sheet | CODE-CONFIRMED |
| `actionId` | +0x0C | Integer action identifier delivered to `OnAction` on click-release | CODE-CONFIRMED |

So each widget encodes: "draw the sub-rect at atlas pixel `(srcX, srcY)` of size `(w, h)` at
screen-local `(x, y)`; on click fire `actionId`." This is a classic atlas sub-rect / sprite-sheet
UI. The 7-state button constructor additionally stores up to three extra `(srcX, srcY)` pairs
covering hover, pressed, and disabled sprite frames.

### 1.2 Widget field offsets (selected load-bearing fields)

These are instance-field offsets on the base `GUComponent`, shared by all subclasses.

| Offset | Size | Type | Role | Confidence |
|---|---|---|---|---|
| +0x00 | 4 | ptr | vtable pointer | CODE-CONFIRMED |
| +0x04 | 4 | u32 | Flags / alpha word (init = 255) | CODE-CONFIRMED |
| +0x08 | 4 | u32 | Capability flags (bit 0 = enabled; panel-bit; window-high-bit) | CODE-CONFIRMED |
| +0x0C | 4 | i32 | `actionId` / parent index (init = −1) | CODE-CONFIRMED |
| +0x14 | 4 | i32 | Local X | CODE-CONFIRMED |
| +0x18 | 4 | i32 | Local Y | CODE-CONFIRMED |
| +0x1C | 4 | i32 | Width | CODE-CONFIRMED |
| +0x20 | 4 | i32 | Height | CODE-CONFIRMED |
| +0x24 | 4 | i32 | Atlas source X (primary) | CODE-CONFIRMED |
| +0x28 | 4 | i32 | Atlas source Y (primary) | CODE-CONFIRMED |
| +0x88 (byte) | 1 | u8 | Visible flag (init = 0) | CODE-CONFIRMED |
| +0x8C (byte) | 1 | u8 | Enabled flag (init = 1) | CODE-CONFIRMED |
| +0x90 | 4 | u32 | Bound texture ID | CODE-CONFIRMED |

Panel additions:

| Offset | Size | Role | Confidence |
|---|---|---|---|
| +0xA8 | 4 | Child-list pointer | CODE-CONFIRMED |
| +0xB4 | 4 | Active-child index (init = −1) | CODE-CONFIRMED |

Window additions (beyond panel):

| Offset | Size | Role | Confidence |
|---|---|---|---|
| +0xBC | — | Embedded command handler (action-id dispatch; own secondary vtable) | CODE-CONFIRMED |
| +0xE8 | — | Auxiliary view data | CODE-CONFIRMED |
| +0x220 | — | Texture list (GUTextureList) | CODE-CONFIRMED |

### 1.3 Draw-from-atlas semantics

Each frame the engine builds a per-widget D3D translation matrix from `(x, y)` and submits the
sprite to a render queue. The sprite source is the sub-rect `(srcX, srcY, srcX+w, srcY+h)` in the
bound atlas texture. Atlas pixels map directly to screen pixels at 1:1 on the reference 1024 × 768
canvas.

### 1.4 Action-id dispatch

When a button is clicked (pressed and released inside its bounds), the GUWindow's embedded command
handler calls `OnAction(actionId)`. The window subclass overrides `OnAction` to dispatch on the
integer id. The 7-state button stores the hover/pressed/disabled sprite offsets but fires the same
`actionId` regardless of interaction state.

---

## 2. Screen construction — hardcoded layouts

No external layout file is read for login or character-select. Each screen has a dedicated
`BuildScene` / `BuildUI` method that calls widget constructors in sequence with literal integer
pixel arguments. The coordinate tables below are **interop facts** extracted from those routines.

### 2.0 Coordinate space

- Reference canvas: **1024 × 768** pixels, top-left origin, +X right, +Y down.
- `(x, y)` in the tables is the widget's **screen-local** position (relative to its immediate
  parent panel's top-left). Add ancestor panel positions to find the world position.
- `srcX`, `srcY` is the atlas **UV origin** for the widget's sprite sub-rect; the sprite
  sub-rect is `(srcX, srcY)` to `(srcX + w, srcY + h)` within the named atlas.

### 2.1 Login screen — `BuildScene`

This routine makes **44 widget-constructor calls**. The atlases used are listed in Section 4.1.

Key widget rectangles (on-screen position and atlas source; full 44-row dump is in the analyst's
dirty query artifact, not committed):

| Element | x | y | w | h | srcX | srcY | actionId / note | Confidence |
|---|---|---|---|---|---|---|---|---|
| Root backdrop panel | 0 | 0 | 1024 | 398 | — | — | Full-width top band; no sprite | CODE-CONFIRMED |
| Animated login slice | — | 110 | 490 | 1024 | (see note) | (see note) | Panning intro banner; src from `loginwindow_02.dds` | CODE-CONFIRMED |
| **Account / ID textbox** | 390 | 32 | 102 | 13 | 615 | 404 | `GUTextbox`; IME field 1; first CP949 input | CODE-CONFIRMED |
| **Password textbox** | 568 | 32 | 102 | 13 | 615 | 404 | `GUTextbox`; IME field 2; masked display | CODE-CONFIRMED |
| Save-ID checkbox | 694 | 86 | 13 | 13 | 717 | — | `GUCheckBox` | CODE-CONFIRMED |
| Label strip — "ID" | 340 | 30 | 38 | 13 | 398 | — | Static caption from atlas | CODE-CONFIRMED |
| Label strip — "PW" | 507 | 30 | 49 | 13 | 38 | 398 | Static caption from atlas | CODE-CONFIRMED |
| Label strip — third field | 619 | 86 | 67 | 13 | 87 | 398 | Static caption | CODE-CONFIRMED |
| OK / Login button (7-state) | 456 | 64 | 112 | 39 | 266 | — | Actions 200/201/202; triggers sub-state 6 | CODE-CONFIRMED |
| Server-list button (7-state) | 456 | 166 | 112 | 39 | 154 | — | Actions 206/16 | CODE-CONFIRMED |
| Quit button (7-state) | 456 | PARTIAL | 111 | 38 | 792 | 398 | Actions 209/220; Y coord from register, not literal | CODE-CONFIRMED (all except Y) |
| Quit-confirm popup panel | 342 | 289 | 340 | 190 | 318 | 647 | Modal overlay; chrome from `InventWindow.dds` | CODE-CONFIRMED |
| Quit-confirm Yes button | 120 | 136 | 113 | 40 | 415 | — | Inside popup panel | CODE-CONFIRMED |
| Help button (7-state) | 66 | 47 | 18 | 596 | — | — | Actions 204/207 | CODE-CONFIRMED |
| EULA/accept panel | 0 | 0 | 1024 | 442 | 582 | — | Terms-of-service overlay | CODE-CONFIRMED |
| Option/tab button 1 | 40 | 82 | 110 | 38 | 635 | — | Tab action 221–245 range | CODE-CONFIRMED |
| Option/tab button 2 | 164 | 82 | 110 | 38 | 865 | — | Tab action 221–245 range | CODE-CONFIRMED |

> **PARTIAL note on Quit button Y:** the Y coordinate was passed through a register rather than a
> literal immediate in the analyst's extraction pass; the exact value was not recovered. All other
> Quit button fields are CODE-CONFIRMED. See Section 7, open item 1.

### 2.2 Character-select screen — `InitFromCharListAndBuildUI`

This is the largest UI builder in the binary: **124 widget-constructor calls**. It is invoked when
the `SmsgCharacterList` (opcode 3/1) packet arrives, not at scene-create time. The atlases used are
listed in Section 4.2.

Structural recovery (key rectangles; per-row loop values marked PARTIAL):

| Region | x | y | w | h | Atlas note | Confidence |
|---|---|---|---|---|---|---|
| Top title bar panel | 0 | 0 | 577 | 58 | `mainwindow.dds` | CODE-CONFIRMED |
| Left character-info panel | 0 | 0 | 244 | 187 | `mainwindow.dds` | CODE-CONFIRMED |
| Server tab button | 67 | 17 | 113 | 40 | src 483 | CODE-CONFIRMED |
| Channel tab button | 232 | 7 | 113 | 40 | src 883/923 | CODE-CONFIRMED |
| Back tab button | 393 | 17 | 113 | 40 | src 963 | CODE-CONFIRMED |
| Char-info portrait box | 0 | 12 | 200 | 46 | src (608, 793) | CODE-CONFIRMED |
| Stat-row label — Lv | 60 | 37 | 70 | 12 | — | CODE-CONFIRMED |
| Stat-row label — HP | 60 | 61 | 70 | 12 | — | CODE-CONFIRMED |
| Stat-row label — class | 60 | 85 | 70 | 12 | — | CODE-CONFIRMED |
| Stat-icon — Lv | 20 | 33 | 34 | 18 | src 771 | CODE-CONFIRMED |
| Stat-icon — HP | 20 | 57 | 34 | 18 | src 771 | CODE-CONFIRMED |
| Stat-icon — class | 20 | 81 | 34 | 18 | src 771 | CODE-CONFIRMED |
| Big character list panel | 0 | 0 | 244 | 474 | Left column, scrollable | CODE-CONFIRMED |
| Per-slot stat-icon grid | 12 | PARTIAL (base + row × 24 px) | 34 | 18 | src 771; loop step 24 px | CODE-CONFIRMED (stride); PARTIAL (base Y) |
| Per-slot value labels | 46 | PARTIAL (base + row × 24 px) | 157 | 18 | src (140, 980) | CODE-CONFIRMED (stride); PARTIAL (base Y) |
| Per-slot action button 1 | 154 | PARTIAL | 24 | 16 | src 548 | CODE-CONFIRMED (x); PARTIAL (y) |
| Per-slot action button 2 | 178 | PARTIAL | 24 | 16 | src 572 | CODE-CONFIRMED (x); PARTIAL (y) |
| Create button | 42 | 325 | 59 | 20 | action 413 | CODE-CONFIRMED |
| Delete button | 112 | 325 | 59 | 20 | action 531 | CODE-CONFIRMED |
| Confirm popup (enter/delete) | — | — | 340 | 190 | src (318, 647) — `InventWindow.dds` chrome | CODE-CONFIRMED |
| Confirm Yes button | 55 | 136 | 113 | 40 | src 415 | CODE-CONFIRMED |
| Confirm No button | 174 | 136 | 113 | 40 | src 415 | CODE-CONFIRMED |
| **Name-entry sub-window** | 430 | 100 | 176 | 42 | src (132, 295); `tradekeepwindow.dds` | CODE-CONFIRMED |
| **Name-entry textbox** | 60 | 80 | 274 | 18 | `GUTextbox`; new character name, CP949 | CODE-CONFIRMED |
| Name-entry OK button | 55 | 136 | 113 | 40 | src 415 | CODE-CONFIRMED |
| Name-entry Cancel button | 174 | 136 | 113 | 40 | src 415 | CODE-CONFIRMED |

> **PARTIAL note on per-slot rows:** the stat-icon Y, per-slot label Y, and action-button Y values
> are computed in a loop with a 24-pixel step. The loop base-Y values for two grids were passed via
> registers in the analyst's extraction run. The stride (24 px) is CODE-CONFIRMED; the exact base
> offsets are PARTIAL. See Section 7, open item 2.

> The character-select screen also hosts **GUCanvas3D** 3D-viewport widgets for live character
> previews. These are not 2D atlas sprites; they render a live scene model into the UI rect. The
> number of viewport slots and their exact positions were not recovered in the current pass — see
> Section 7, open item 3.

### 2.3 Shared panel chrome — `InventWindow.dds` modal

Both the login quit-confirm popup and the character-select confirm/delete popups use the **same
340 × 190 atlas sub-rect at source `(318, 647)`** from `data/ui/inventwindow.dds`. This confirms
the inventory window chrome is a shared atlas reused across all three screens (login, select, and
in-game inventory).

### 2.4 In-game windows (lazily built)

In-game panels are not constructed at scene start. Each panel has a "already built?" guard field;
the first time the player opens the panel, the build routine runs and the guard is set.

| Window | Atlas(es) | Notes | Confidence |
|---|---|---|---|
| **Skill window** | `skillwindow.dds`, `skillicon/stateicon.dds`, `blacksheet.dds` | Iterates child panels via dynamic type test; binds skill-window atlas to each; 30 skill-icon slots bound to `stateicon.dds`. Build guard prevents rebuild. | CODE-CONFIRMED |
| **Inventory window** | `inventwindow.dds` | Same 340 × 190 chrome panel as the login/select modal (src `(318,647)`); movable — drag position persisted to INI. | CODE-CONFIRMED |
| **Chat window** | (text + frame; no single named atlas) | **Data-driven position**: reads `CHAT_WINDOW_POS_X`, `CHAT_WINDOW_POS_Y`, `CHAT_WINDOW_SIZE`, `CHAT_WINDOW_FONT_SIZE` from `data/script/uiconfig.lua`. This is the **only** widget whose initial position/size comes from a data file. | CODE-CONFIRMED |

### 2.5 Movable-panel position persistence (in-game only)

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

## 3. Per-screen asset manifests

All paths are VFS-relative. All large UI sheet atlases are DDS with DXT3 pixel format and a
1024 × 1024 (or occasionally 512 × 512) mip-0 surface. See `formats/ui_manifests.md` for the
file-level format of `uitex.txt` and related manifests.

### 3.1 Login screen

| Path | Role | Confidence |
|---|---|---|
| `data/ui/loginwindow.dds` | Main backdrop / button atlas | CODE-CONFIRMED (xref from builder) |
| `data/ui/loginwindow_02.dds` | Secondary atlas (text strips, animated banner source) | CODE-CONFIRMED |
| `data/ui/login_slice1.dds` | Panning intro slice | CODE-CONFIRMED |
| `data/ui/inventwindow.dds` | Shared popup / button chrome (quit-confirm modal) | CODE-CONFIRMED |

### 3.2 Character-select screen

| Path | Role | Confidence |
|---|---|---|
| `data/ui/mainwindow.dds` | Window frame + header | CODE-CONFIRMED |
| `data/ui/inventwindow.dds` | Popup panels + buttons (confirm / delete / create) | CODE-CONFIRMED |
| `data/ui/tradekeepwindow.dds` | Name-entry sub-window chrome | CODE-CONFIRMED |
| `data/ui/blacksheet.dds` | Dim/blackout overlay | CODE-CONFIRMED |
| `data/ui/loginwindow.dds` | Reused button strips | CODE-CONFIRMED |
| `data/ui/carrierpigeonall.dds` | Small shared button atlas | CODE-CONFIRMED |
| `data/ui/carrierpigeonperson.dds` | Small shared button atlas | CODE-CONFIRMED |
| (GUCanvas3D live 3D viewports) | Character previews; not a 2D atlas | CODE-CONFIRMED (mechanism); PARTIAL (slot positions) |

### 3.3 Opening cinematic (state 3) screen

| Path | Role | Confidence |
|---|---|---|
| `data/ui/openning_001.dds` through `openning_004.dds` | Cinematic frames 1–4 (768 × 1024) | SAMPLE-VERIFIED |
| `data/ui/openning_scenario.dds` | Large intro splash (2048 × 1024) | SAMPLE-VERIFIED |

### 3.4 Loading (state 2) screen

| Path | Role | Confidence |
|---|---|---|
| `data/ui/loading.dds` | Generic loading screen | SAMPLE-VERIFIED |
| `data/ui/loading01.dds` through `loading08.dds` | Area-specific loading screens (8 variants) | SAMPLE-VERIFIED |
| `data/ui/loadingbar.dds` | Progress bar texture (256 × 256) | SAMPLE-VERIFIED |

### 3.5 In-game — confirmed atlas set (selected; not exhaustive)

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

Solid-colour fill patches (tiled fills / tint overlays across all windows): `p_green.dds`,
`p_red.dds`, `p_white.dds`, `p_blue.dds`, `p_darkblue.tga`, `p_black.tga`, `p_orange.tga`,
`p_purple.tga`, `p_yellow.tga`, `green.dds`, `red.dds`, `white.dds`, `blue.dds`, `yellow.dds`,
`blacksheet.dds`, `edge.dds`, `inactivemember.dds`.

> **Texture format note:** the loaders pass the FourCC constant `DXT2` as a load hint for DDS
> atlases. Actual pixel format on all sampled large sheets is DXT3 (see `formats/ui_manifests.md`).
> Some HUD sprites are TGA. Cursor assets live under `data/cursor/` and are rendered as textured
> quads, not registered as Windows cursor resources — `SetCursor(NULL)` hides the OS cursor in
> the WndProc. There is no `.cur` or `.ani` file in the VFS.

---

## 4. Font system

### 4.1 Mechanism

Font objects are created at startup (master scene state 1 — Login) using the Direct3D font API
with:
- **charset = 129 = HANGUL_CHARSET** — this is the load-bearing Korean encoding constant. The
  client renders Korean glyphs through the OS Hangul code page (CP949). No bitmap glyph atlas is
  shipped in the VFS for body text.
- Font objects are re-created after a device reset by iterating the full 15-slot descriptor table
  and re-issuing `D3DXCreateFontA`. The device handle is a single global.

### 4.2 The 15-slot font descriptor table

Built once in the Login state (state 1). Each slot describes one `ID3DXFont` instance:

| Slot | Face | Height | Width | Weight | Likely use | Confidence |
|---|---|---|---|---|---|---|
| 0 | DotumChe | 6 | 12 | 0 | Default small fixed-pitch | CODE-CONFIRMED |
| 1 | Dotum | 5 | 10 | 0 | Small proportional | CODE-CONFIRMED |
| 2 | DotumChe | 16 | 32 | 800 | Large bold title | CODE-CONFIRMED |
| 3 | DotumChe | 12 | 24 | 800 | Bold heading | CODE-CONFIRMED |
| 4 | DotumChe | 6 | 12 | 800 | Bold body | CODE-CONFIRMED |
| 5 | BatangChe | 6 | 12 | 0 | Serif body | CODE-CONFIRMED |
| 6 | BatangChe | 12 | 24 | 700 | Serif heading bold | CODE-CONFIRMED |
| 7 | BatangChe | 6 | 12 | 700 | Serif body bold | CODE-CONFIRMED |
| 8 | BatangChe | 6 | 12 | 700 | Serif body bold (alt) | CODE-CONFIRMED |
| 9 | DotumChe | 6 | 12 | 700 | Semibold fixed | CODE-CONFIRMED |
| 10 | Dotum | 10 | 20 | 800 | Bold proportional | CODE-CONFIRMED |
| 11 | DotumChe | 5 | 10 | 400 | Tiny | CODE-CONFIRMED |
| 12 | DotumChe | 6 | 12 | 400 | Regular fixed | CODE-CONFIRMED |
| 13 | DotumChe | 7 | 14 | 400 | Medium fixed | CODE-CONFIRMED |
| 14 | DotumChe | 8 | 16 | 400 | Large fixed | CODE-CONFIRMED |

Column definitions:
- **Face** — the TrueType face name passed to the font API. **DotumChe** (돋움체) is
  fixed-pitch sans-serif; **Dotum** (돋움) is proportional sans-serif; **BatangChe** (바탕체) is
  fixed-pitch serif. These are standard Korean Windows system fonts, not VFS assets.
- **Height** — the font height in pixels (the `Height` parameter to `D3DXCreateFontA`).
- **Width** — the font width in pixels (the `Width` parameter).
- **Weight** — the GDI weight value (0 = default, 400 = regular, 700 = bold, 800 = extra-bold).

> A faithful rebuild must supply CJK-capable fonts at these pixel heights and weights. The
> per-widget font-slot index (which slot each `GULabel` / `GUTextbox` draws with) lives in the
> instance data and was not exhaustively recovered — see Section 7, open item 4.

### 4.3 Effect bitmap fonts (not UI body text)

Two bitmap font strips exist in the VFS for **in-world floating combat numbers** only; they are
not part of the UI text system:
- `data/effect/tex/att-font.dds` — attack number strip (30 × 240 px, likely 10 digits × 24 px)
- `data/effect/tex/cri-font.dds` — critical-hit number strip (30 × 240 px)

---

## 5. String database — msg.xdb

All visible UI captions are fetched by numeric ID from `data/script/msg.xdb` (loaded at the start
of state 1, alongside the font table). The lookup helper accepts an integer message ID and returns
the CP949-encoded string.

Known ID ranges in use:
- `9001 + stateIndex` — scene/state name strings (used by the error dialog)
- `4025`–`4028` — login error toast messages
- `0xC8`–`0xD4` (200–212 decimal) — character create / rename error messages

> **Format:** `msg.xdb` is a flat binary array of 516-byte records: `u32 id` + `u8[512]` CP949
> NUL-terminated string. Record count = `file_size / 516`. The file has no header and no magic.
> Records are inserted into a runtime red-black tree keyed on `id`. The format is
> CODE-CONFIRMED; record content is SAMPLE-UNVERIFIED (VFS probe was unavailable at analysis time).
> Full specification: `Docs/RE/formats/misc_data.md §6`.

---

## 6. Master scene state machine

### 6.1 State variable

A global array of small integers acts as the scene state. The relevant slots:

| Array index | Role | Confidence |
|---|---|---|
| [0] | Primary scene state (the switch selector) | CODE-CONFIRMED |
| [1] | Secondary state / error sub-code (which init step failed) | CODE-CONFIRMED |
| [2] | Error message ID (used by the error dialog path) | CODE-CONFIRMED |
| [+12] (byte offset) | Debug-mode flag (from Lua `debugmode` key) | CODE-CONFIRMED |

> The login window's own internal sub-state machine (Section 6.3) is stored in a **separate** field
> on the login window object, not in this global array. The two must not be confused.

### 6.2 The 9 primary states

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

### 6.3 Login window internal sub-state machine

Stored at offset `+0x238` on the login window object. This drives the lobby discovery and
credential flow described in `specs/login_flow.md`. The table below lists the recovered sub-states;
some states' exact meanings are CODE-CONFIRMED, others are inferred from surrounding context.

| Sub-state | Meaning | Confidence |
|---|---|---|
| 2 | Connect / start intro | CODE-CONFIRMED |
| 3, 4, 5 | Animate banners into place | CODE-CONFIRMED |
| 6 | Login form active — waiting for user input | CODE-CONFIRMED |
| 29 | Server-list trigger point | CODE-CONFIRMED |
| 30 | Quit-confirm "yes" — sets `state[0]` = 6 and `state[1]` = 8 (quit) | CODE-CONFIRMED |
| 31 | Help screen | CODE-CONFIRMED |
| 32 | Accept-EULA screen | CODE-CONFIRMED |
| 33 | Press-OK transition | CODE-CONFIRMED |
| 34 | Start server-list fetch thread (port 10000) | CODE-CONFIRMED |
| 35 | Wait for server-list reply | CODE-CONFIRMED |
| 36 | Consume server-list data | CODE-CONFIRMED |
| 37 | Server selected | CODE-CONFIRMED |
| 38 | Start channel-endpoint fetch thread (port 10000 + selected offset) | CODE-CONFIRMED |
| 39 | Wait for endpoint reply | CODE-CONFIRMED |
| 40 | Consume endpoint; build TAB-delimited credential string `account⟨TAB⟩…⟨TAB⟩host port`; rebuild secure context; hand off to game connection | CODE-CONFIRMED |
| 41 | Transition complete — login window exits | CODE-CONFIRMED |

> Sub-state 40 is the junction documented in `specs/login_flow.md` §1.1/§4. The login OK button
> (action id 202) sets this sub-state machine in motion. The two synchronous lobby fetch threads
> occupy offset slots at `+960` and `+972` on the login window object.

### 6.4 Character-select screen lifecycle note

The `InitFromCharListAndBuildUI` builder is not called at scene creation. It is invoked when the
`SmsgCharacterList` (opcode 3/1) packet arrives on the network — the packet handler forces the
primary scene state into the select scene and triggers the builder. This means the select screen
widget tree does not exist until the packet is received.

---

## 7. Known unknowns / open items

1. **Quit button Y on login:** the exact on-screen Y coordinate of the quit button was passed
   through a register rather than a literal in the builder. All other arguments are recovered.
2. **Per-slot row base Y on character select:** the base Y positions for the two looped grids
   (stat-icon grid and per-slot value label column) are register-fed loop parameters; the 24-pixel
   step is confirmed, the base offsets are not.
3. **GUCanvas3D slot count and positions on character select:** the number of 3D-viewport widgets
   and their exact screen rectangles were not recovered.
4. **Per-widget font-slot index:** which of the 15 font slots each `GULabel` or `GUTextbox`
   instance uses lives in the instance data and was not exhaustively extracted.
5. **`msg.xdb` format: RESOLVED.** The format is CODE-CONFIRMED (flat binary, 516-byte records,
   `u32 id` + `u8[512]` CP949 text, no header, count from `file_size / 516`, red-black tree at
   load). Record content is SAMPLE-UNVERIFIED pending VFS probe repair. Full spec at
   `Docs/RE/formats/misc_data.md §6`.
6. **Inventory in-game window layout:** the full in-game inventory builder was not isolated; its
   chrome uses the shared 340 × 190 `InventWindow.dds` panel (confirmed), but individual inventory
   grid slot coordinates are not recovered.
7. **Exact FourCC note:** the loader passes `DXT2` as the format hint internally, while sample
   bytes show DXT3. This may reflect an older API convention where DXT2 and DXT3 were
   interchangeable (both are 4-bit alpha BC2); a parser should accept both FourCCs.

---

## 8. Godot reconstruction guidance

### 8.1 Reference canvas and scaling

The legacy canvas is **1024 × 768** pixels at 1:1 pixel density. Implement the UI root as a
`SubViewport` or a root `Control` node with reference size `(1024, 768)`, then use Godot's
`CanvasLayer` / `stretch_mode` to scale to the actual window size. The atlas sub-rects map to
Godot `AtlasTexture` resources: `atlas = preload("res://…/loginwindow.dds")`, `region =
Rect2(srcX, srcY, w, h)`.

### 8.2 Login textboxes

Two `LineEdit` (or equivalent) nodes at screen positions `(390, 32)` and `(568, 32)`, each
102 × 13 pixels. The second (password) field should mask input. Both accept CP949 Korean text
through Godot's native IME support (`DisplayServer.ime_active = true` when focused). The account
field is IME field 1; the password field is field 2.

### 8.3 Atlas sub-rect mapping

For each widget, the sprite is the sub-rect `Rect2(srcX, srcY, w, h)` of the atlas named in
Section 4. At the reference canvas scale this is a 1:1 pixel copy. For GUButton 7-state, keep
separate `AtlasTexture` regions for normal, hover, pressed, and disabled states.

### 8.4 Fonts

Map the 15 font slots to system-installed or bundled Korean TrueType fonts at the listed pixel
heights and weights:
- **DotumChe** → 돋움체 (NanumGothicCoding or similar fixed-pitch Korean sans)
- **Dotum** → 돋움 (NanumGothic or similar proportional Korean sans)
- **BatangChe** → 바탕체 (NanumMyeongjo or similar fixed-pitch Korean serif)

Use `FontFile` nodes loaded with the listed `height` as the `size` argument. CP949 string
rendering requires the fonts to cover the Hangul Syllables Unicode block (U+AC00–U+D7A3).
Translate msg.xdb IDs to Unicode strings in `Assets.Parsers` and pass them to Godot as UTF-8.

### 8.5 Scene flow

Model the 9 primary states as a Godot `SceneTree` scene-change sequence or as an
`Application`-layer state machine (preferred, to keep game logic engine-free):

- State 1 (Login) → state 2 (Load/opening gate) → optional state 3 (cinematic) → state 4
  (Select) → state 5 (In-game) → **state 4** (Select, on logout).
- The `[OPENNING] SKIP` INI key controls the 2 → 3/4 branch; default to skip in the revival build.
- State 7 (Error) shows a modal dialog and proceeds to state 8 (Exit).

### 8.6 Chat window

The chat window is the **only** UI element with a data-driven initial size and position. Read
`CHAT_WINDOW_POS_X`, `CHAT_WINDOW_POS_Y`, `CHAT_WINDOW_SIZE`, `CHAT_WINDOW_FONT_SIZE` from Lua
(or an equivalent config key in the revival config), and apply them to the chat control at
scene-create time.

### 8.7 Movable-panel persistence

In-game panels that the player can drag should persist their drag offset to per-character storage
(keyed by player descriptor). The reset condition — saved coordinate = −1, or screen size changed
— must be implemented to match the legacy behaviour. Default positions come from each panel's
hardcoded build coordinates.

---

## 9. Cross-references

- Widget class detail: `specs/input_ui.md` (widget field layout, hit-test, IME routing)
- Login network flow: `specs/login_flow.md` (lobby ports, credential TAB-string, sub-states 34–41)
- Cryptography: `specs/crypto.md` (session handshake wired at sub-state 40)
- UI asset file formats: `formats/ui_manifests.md` (uitex.txt, skillicon.txt, crestlist.txt)
- Texture format: `formats/texture.md`
- VFS lookup: `formats/pak.md`
- String encoding: all CP949 strings; register `CodePagesEncodingProvider` before any decode
- msg.xdb format spec: `formats/misc_data.md §6`
- Glossary: `Docs/RE/names.yaml`
- Provenance: `Docs/RE/journal.md`
