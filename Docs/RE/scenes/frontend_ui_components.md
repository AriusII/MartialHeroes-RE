---
verification: written 2026-06-21 against doida.exe binary IDB SHA 263bd994; updated 2026-06-22 per
  reconciled dossier promotion: Login panel group corrected (EULA/agreement → notice/agreement panel;
  eulaPanel distinct from serverGridPanel; PIN action range 0..99 per build-order; no EULA gate).
  Loading gauge axis corrected to vertical top→down fill; single-object architecture clarified.
  Updated 2026-06-24: Login textbox length/charset corrected per login.md §5.1 (binary-won 2026-06-22):
  ID cap 16/mask 6, PW cap 12/mask 0x81 — the prior "maxlen 6/129" framing conflated charset-mask with
  length cap. CharSelect preview scale/rate corrected per charselect.md §6.2: lineup scale 70.0/motion-
  rate 3.0, zoom scale 81.0 — the prior "@3×/@2×" shorthand misread the motion-rate as a scale factor.
  This is a CROSS-SCENE INDEX, not a fresh recovery — it ties together the per-scene dossiers
  (scenes/login.md, scenes/load.md, scenes/opening.md, scenes/charselect.md, scenes/ingame.md,
  scenes/ingame_composition.md), the shared widget-framework struct specs
  (structs/gucomponent.md, structs/guwindow.md) and the UI subsystem spec (specs/ui_system.md), all of
  which were re-confirmed at element / member-offset / atlas-src-rect level in their own passes against
  the same IDB. The shared GU* class hierarchy (RTTI-confirmed roots GUComponent → GUPanel → GUWindow),
  the 13/14-slot virtual interface, the GUButton 3-state sprite model, the GUTextbox password mask, the
  per-scene build-virtual / sub-FSM creation-teardown story, the MainWindow 178-slot HUD panel array,
  the shared 2D-over-3D one-backbuffer composition, and the scene FSM ordering (0 INIT → 1 Login →
  2 Loading → 3 Opening → 4 CharSelect → 5 InGame) are all derived from those pinned sources. No new
  IDA read was performed for this index; nothing here is inferred beyond what the linked dossiers proved.
scene: Cross-scene front-end + in-game 2D GUI index (engine states 1–5)
evidence: [static-ida, debugger-confirmed-handshake]
capture_verified: false
sources:
  - Docs/RE/scenes/login.md          # Login (state 1) — LoginWindow tree, sub-FSM, atlases
  - Docs/RE/scenes/load.md           # Loading (state 2) — LoadHandler/LoadingScreen immediate-mode
  - Docs/RE/scenes/opening.md        # Opening (state 3) — COpeningWindow tree, banner/crawl/skip
  - Docs/RE/scenes/charselect.md     # CharSelect (state 4) — SelectWindow tree + 3D preview scene
  - Docs/RE/scenes/ingame.md         # InGame (state 5) — MainWindow 178-slot HUD panel roster
  - Docs/RE/scenes/ingame_composition.md  # InGame 2D-over-3D composition (one backbuffer, two phases)
  - Docs/RE/structs/gucomponent.md   # GUComponent base layout + virtual interface
  - Docs/RE/structs/guwindow.md      # GUWindow/GUPanel container layout + sub-objects
  - Docs/RE/specs/ui_system.md       # UI subsystem behaviour (widget framework, msg.xdb, fonts)
  - Docs/RE/scenes/scene_state_machine.md  # WinMain scene FSM (state ordering / routing gates)
---

# 2D GUI — Cross-Scene Component Index (Login · Loading · Opening · CharSelect · InGame)

> **Clean-room neutral dossier.** Synthesised under EU Software Directive 2009/24/EC Art. 6
> (decompilation permitted solely to achieve interoperability). It contains **no decompiler
> output, no addresses, and no pseudo-C** — only neutral prose derived from the binary and from
> the already-promoted scene/struct/spec dossiers it links to.

This index sits **above** the per-scene dossiers. Read it to understand how all six engine scenes
relate as one GUI family: which top-level component groups each scene owns, the **shared widget
vocabulary** they all draw from, the **2D-over-3D composition** the two world-bearing scenes use,
and the **global lifetime story** — when each window's components are created and destroyed as the
engine walks its scene state machine. For the element-level detail (member offsets, atlas source
rects, action-ids, sub-FSM substates, the 178-slot HUD roster) follow the link into the owning
scene dossier.

---

## 1. Overview — scene → top component groups

The GUI comprises **six** engine scenes (1–5, plus the INIT bootstrap state 0). Four of them (Login,
Opening, CharSelect, InGame) are **GUWindow-rooted component trees** built from the shared GU* widget
family; Loading is a deliberate exception — an **immediate-mode 2D render object** that owns **no GU
child widgets at all**. Two of them (CharSelect, InGame) are **composite 2D-over-3D scenes**: a full
3D world is drawn first, then the 2D GUI window is painted on top in the same frame (see §4).

| Engine state | Scene / root object | Root class chain | Top component groups | Interactive? | Owning dossier |
|---|---|---|---|---|---|
| **1** | **Login** — `LoginWindow` | `LoginWindow : CommonLoginWindow : GUWindow : GUPanel : GUComponent : EventHandler` | **server-list panel** (header img, enter btn, login-form sub-panel, quit/help); **login-form panel** (ID/PW label imgs, account `GUTextbox` [cap 16, charset-mask 6], password `GUTextbox` [cap 12, charset-mask 0x81, masked], save-ID `GUCheckBox`, OK `GUButton`); **notice/agreement panel** (`eulaPanel` at +0x2BC, 22 `GULabel` rows msg 4001..4022 + scroll up/down/thumb buttons + track img — a static stacked text column, **NOT an EULA gate**, distinct from the server-select grid at +0x328; see `specs/frontend_scenes.md §1.4c`); **server-grid panel** (per-row name/flag/select/pop/ping, status icons, 10 name-strip buttons); **confirm panels A/B** (`GULabel` + OK); **second-password PIN modal** (`LoginSecondPassword` — scrambled keypad 100 `GUButton` in a 10×10 over-build / 5-col×2-row visible grid, OK/Clear/Cancel, embedded `ExitPanel`); **ExitPanel** (quit confirm); **ErrorPanel** (notice host). ~73 widgets total. | Yes (action-ids 102–124, plate 400/401, PIN actions 0..99 / tags 11/12/13) | [scenes/login.md](login.md) |
| **2** | **Loading** — `LoadHandler` + `LoadingScreen` | single `LoadHandler` ("Loader") heap object embedding a `Diamond::GView` sub-object — **NOT** a `GUWindow`; the "LoadingScreen" init is a second-phase initializer on the **same** allocation | **background quad** (immediate-mode textured quad, random of `loading.dds`/`loading06.dds`/`loading08.dds`); **progress-gauge quad** (immediate-mode textured quad, **vertical top→down fill**, sub-rect of the same bg DDS; near-static / decorative); **embedded scene-view** (`Diamond::GView` @ +0xC8); **looping SFX** (id 920100100, category 0). **No `GUButton`/`GULabel`/`GUPanel`/`GUTextbox`/modal — no action-ids. Completion is flag-driven (done-flag @ +0x200), never bar-driven.** | No (no interactive widgets, no action-ids) | [scenes/load.md](load.md) |
| **3** | **Opening** — `COpeningWindow` | `COpeningWindow : GUWindow : GUPanel : GUComponent` | **scenario crawl image** (`GUComponent` image, `openning_scenario.dds`, 1024×2048, vertical crawl); **skip/close button** (`GUButton` 3-state, `mainwindow.dds`, action-id 100); **banner slideshow** (4 raw texture handles `openning_001..004.dds` — drawn directly, **not** child widgets); **looping intro SFX** (id 910061000). Exactly **two** GU child components. | Partly (skip button action-id 100; Enter/ESC/Space keys; wheel scroll) | [scenes/opening.md](opening.md) |
| **4** | **CharSelect** — `SelectWindow` | `SelectWindow : GUWindow : GUPanel : GUComponent` (window name "Selecter") | **slot-frame group panels A/B** (5-slot character frames + ENTER/class buttons); **create-form panels** (class strip/nav, input sub-panel, back/confirm, masked name `GUTextbox`); **appearance/stat grid panel** (digit/value image cells + ~14 spinner `GUButton`s + per-row `GULabel`s); **name/confirm sub-panels**; standalone class-select / create / delete / enter-world / server / world-channel buttons; **`Descriptor` tooltip**, **`ErrorPanel`**, **`ExitPanel`** modals. ~124 widgets, built lazily by the window build virtual; **plus a full 3D character-preview scene** drawn beneath (see §4). | Yes (action-ids 1–6, 10–13, 21–36, 54/55, 59–74; 3D actor pick) | [scenes/charselect.md](charselect.md) |
| **5** | **InGame** — `MainWindow` (+ `MainHandler`) | `MainWindow ("MainMaster") : GUWindow : GUPanel : GUComponent`; sibling `MainHandler` ("Mainhander") command handler | **178-slot HUD panel array** (base `MainWindow+0x238`, slot = (off−0x238)/4), grouped: **core gauges** (`PlayerStatusPanel`/GagePanel HP-MP-stamina-condition, `MopGagePanel` target gauge, `Gage3DPanel` floating damage); **buff/state strips** (`ActorStatePanel` + Passive/Cash/Skill variants); **hotbar** (`LinkPanel` 7-cell quickslot over a 240-record model); **inventory/equipment** (`ItemPanel` bag+doll+3D equip actor); **skills/stats** (`SkillPanel`, Fame/Rank badges, `ComboPanel`); **minimap + world-map** (`MapPanel`/`TotalMapPanel`); **chat/message/announce** (`ChatPanel` input, `ChatOutputPanel` log, `MessagePanel`, `AnnouncePanel`, `ActorChatPanel` overhead); **social** (Party/Guild/Friend/Relation/CarrierPigeon families); **economy** (Trade/Keep/Stall/Delivery/Letter/Goods families); **special** (Quest/Pet/Gamble/Upgrade/Option/Help); bottom command bar + 2D cursor. **Plus the full 3D world ("charater scene") drawn beneath** (see §4). | Yes (toggle keymap b/c/f/g/h/i/j/k/l/m/n/o/p/q/s/…; per-panel action-ids; hotbar keys 1–7) | [scenes/ingame.md](ingame.md) · [scenes/ingame_composition.md](ingame_composition.md) |

**State 0 (INIT)** is the WinMain bootstrap that brackets the front-end (no scene window yet — but the
shared msg.xdb string DB and the 15-slot D3DX font table are stood up here, in the state-1 transition
block, so both are resident before any GUI draws). **State 5 (InGame)** is the terminal scene; entry
is gated by the login handshake (GameState→7) and the character/world enter flow out of CharSelect.

**Read of the table:** Login and InGame are the heavyweights — Login for text input, modals, paging
and its per-frame sub-FSM; InGame for the sheer breadth of its 178-slot HUD panel roster. CharSelect
is the first **2D-over-3D** scene (a full preview world under a "Selecter" frame), and InGame is the
second (the live world under the HUD). Opening is minimal (one image, one button, directly-blitted
banners). Loading is the architectural outlier — it does not participate in the GU* widget framework
at all; its "UI" is two raw quads, so it has no shared widgets, no hit-testing of child widgets, and
no action-id dispatch.

---

## 2. The shared widget framework — common vocabulary

Login and Opening are built from one C++ class family in namespace `Diamond`, rooted at
**`GUComponent`**. This is the vocabulary every per-scene dossier uses; consult
[structs/gucomponent.md](../structs/gucomponent.md) and [structs/guwindow.md](../structs/guwindow.md)
for the offset/size tables and [specs/ui_system.md](../specs/ui_system.md) for the behaviour.

### 2.1 Class hierarchy (RTTI-confirmed)

- **`GUComponent`** — abstract base of every widget; also mixes in `EventHandler`. Leaf object = 240
  bytes (0xF0), naturally aligned (4-byte, **not** `Pack=1`).
- **`GUComponentEx : GUComponent`**.
- **`GUPanel : GUComponent`** — child container (holds a child vector + selected-index).
- **`GUWindow : GUPanel, EventHandler`** — multiple inheritance (two complete-object-locators / two
  vtables). Embeds `Cmdhandler`, `GView`, `GUTextureList` sub-objects. The **top scene container**;
  `LoginWindow` and `COpeningWindow` derive from it.
- **`GUButton : GUComponent`** — 3-state sprite + label (see §2.3).
- **`GUCheckBox : GUButton : GUComponent`** — adds a toggle/checked flag, reuses the 3-state sprite
  (off/on origins). Used by Login's save-ID checkbox.
- **`GULabel` / `GUShortLabel` / `GULabels` : GUComponent** — static text (single / short / multi-line).
- **`GUTextbox : GUComponent`** — the only editable input widget; carries the password mask (§2.4).
- **`GUList` / `GUScroll` : GUComponent**, **`GUScrollEx : GUPanel`** — list + scrollbars.
- **`GUCanvas3D : GUComponent`** — a 3D render region embedded in the 2D GUI (character preview;
  used by CharSelect, not the three front-end scenes).
- **`GUTextureList`** = a `std::vector<IDirect3DTexture9*>` wrapper — a utility atlas container on
  the window, **not a widget**.

There is deliberately **no dedicated MessageBox / Dialog / Toast / Gauge / Slider / Tooltip widget
class.** Modal message boxes and progress UI are **composed from the primitives** — a `GUWindow`/
`GUPanel` host plus `GULabel`(s) and `GUButton`(s) (e.g. Login's `ExitPanel` / `ErrorPanel` /
confirm panels A/B). The RTTI `*Edit` classes (`ToolLight*Edit`) are dev-tool editors, not runtime
widgets. The only true OS dialog reachable from this front-end is a single Win32 `MessageBoxA` on
the Login **client-version mismatch** path (message 2204); it is not a widget.

### 2.2 Shared virtual interface (GUComponent base vtable)

Every widget overrides slots from one canonical 13-slot interface (0..12); container subclasses
(`GUPanel`/`GUWindow`) append slot 13 (child-sweep / maintenance):

| slot | role | slot | role |
|---|---|---|---|
| 0 | destructor | 7 | onDraw |
| 1 | setVisible | 8 | onUpdate |
| 2 | setPosition | 9 | computeTransform / layout |
| 3 | getPosition | 10 | getHitActionId |
| 4 | hitTest(vec) | 11 | onMouseEnter |
| 5 | hitTest(x,y) | 12 | onMouseLeave |
| 6 | onEvent | 13 | (container) sweepRemovedChildren |

Each widget overrides 0/6/7 (and 5/8/11/12 where relevant). Geometry is set via **setPosition (slot
2)** after construction; a scene's window is re-centred at runtime for its 1024×768 design canvas.

### 2.3 GUButton 3-state sprite model (shared by Login, Opening)

A button stores **four** sprite source-coordinate pairs (normal / hover / pressed / disabled) and
picks one per frame by priority **disabled > pressed > hover > normal**. A CLICK is synthesised on
press-then-release over the same widget; it emits a **UI event (type 6) carrying the widget's
actionId** (the value returned by slot 10 getHitActionId). This is the single mechanism behind
every action-id in the table above: Login's 102–124 and 400/401, Opening's 100.
`GUCheckBox` reuses this sprite machinery for its off/on states.

### 2.4 GUTextbox password mask (shared mechanism, exercised by Login)

A style-flags bit (`0x80`) marks a textbox as a **password field**: it draws `"*"` per character
(6-px step, by character count) instead of the backing text string. The widget also carries
`maxLength` and a charset-mask field (IME-mode), and blinks a caret (~500 ms) while focused.
Login's **ID (account) textbox**: per-keystroke cap = **16** characters (GUTextbox +0xD0), charset
mask = **6** (+0xA4), not masked. Login's **password textbox**: per-keystroke cap = **12**
characters, charset mask = **0x81** (the `0x80` password-mask flag | mode 1), masked.
The charset-mask value `6` and the password-flag value `0x81` (≈ 129) are style-flag / IME-mode
constants, **not length caps** — do not conflate them with `maxLength`. (See `scenes/login.md §5.1`
for the binary-won layout.) Opening and Loading own no textbox.

### 2.5 Constructor / child-placement convention

Per-widget constructors call the `GUComponent` base ctor, then install their own vtable. The
universal **image builder** argument shape is `(textureId, dstX, dstY, w, h, srcX, srcY, color)` —
the trailing value is a **color/tint** (−1 = opaque white), **not** an action-id. Derived builders
extend this: Panel inserts an opaque/clip flag; the **3-state button** adds three source origins
(normal/pressed/hover, shared w/h); the **checkbox** adds off/on origins; the **textbox** adds
IME-mode + maxlen; a **label** carries no texture (text comes from the message DB). Each sub-rect is
a **1:1 width/height copy** of its atlas region (the component stores `srcRight = srcX+w`,
`srcBottom = srcY+h`). **Action-ids are never ctor arguments** — they are bound by the parent's
"add-child-with-action" call `(parent, child, actionId)` or written to the child's action field.
The scene `GUWindow` ctor takes `(name, x, y, w, h, …)` — the window names are literally
**"Loginer"** (Login) and **"Opening"** (Opening); Loading's command handler is named **"Loader"**.

### 2.6 Shared text & font fabric (set up once, inherited by all scenes)

Both the string DB and the font table are created in the **state-1 transition block** (INIT→Login),
so they are resident for the whole front-end:

- **`data/script/msg.xdb`** — fixed **516-byte** records (4-byte caption id + 512-byte CP949 text),
  inserted into a sorted id→string map; a single `GetString(id)` accessor is the app-wide gateway.
  Login consumes ids 4001–4024 (EULA + confirm), 4029–4032 (status legend), 5901 (unknown server),
  6001–6005 (load tiers / count). **CharSelect** sources **every** caption from msg.xdb (no inline
  Korean literals in its build virtual — only texture paths): ids 2206/2209, 14001/14002, 46001/46002,
  48001/48003/48004/48005, 63030, 2007 (ExitPanel), plus create-name captions 14003–14007 by class.
  **InGame** likewise resolves all HUD labels by id through the same accessor (~700 references binary-
  wide). **Opening and Loading draw NO captions** — their on-screen "text" is baked into DDS artwork
  (no font slot, no msg.xdb lookup).
- **15-slot D3DX font table** — created once via `D3DXCreateFontA` with **charset 129 (HANGUL)**;
  faces are the Korean system fonts `DotumChe` / `Dotum` / `BatangChe`. Labels default to **slot 0**
  (DotumChe 12) unless a per-label override is set (Login's per-server count label → slot 4 bold;
  CharSelect's title/count labels → slots 2/4 bold; InGame big numbers/titles → slot 2 DotumChe 32px
  bold). Text binds via the GULabel font-slot field (+0xE4) + a shared rasterizer with CP949
  double-byte-safe ellipsis; quantities use plain sprintf templates (`%d/%d`, 64-bit `%I64d` for
  currency — no thousands separators).

---

## 3. Global creation / teardown story (driven by the scene FSM)

The WinMain scene state machine (see [scenes/scene_state_machine.md](scene_state_machine.md)) walks
**0 INIT → 1 Login → 2 Loading → 3 Opening → 4 CharSelect → 5 InGame**, with a routing gate that can
skip Opening. For each GUWindow scene the FSM **allocates the window → runs its ctor (which builds NO
children, only the window shell + scalar/FSM state) → calls the window's class-specific build/show
virtual (where every child is actually created) → enters the engine scene loop → on exit tears the
window down**. This rule holds across all four GUWindow scenes: CharSelect's ~124 widgets and InGame's
178 HUD panels are likewise built by a single build virtual (`SelectWindow`'s slot-14 build, and
`MainWindow`'s `MainHud_BuildAndRegisterPanels`), not by their ctors. Loading is the exception (no GU
children — see §3.2).

### 3.0 Universal pattern (the rule the dossiers all confirm)

- **Ctor builds nothing visible.** `LoginWindow`/`CommonLoginWindow` and `COpeningWindow` ctors only
  chain the `GUWindow` base ctor (setting the window name), install vtables, zero a field block, and
  seed scalar/FSM/flag state. They do **not** create child widgets.
- **One build virtual creates the whole tree.** Children are created in the window's class-specific
  build/show virtual, invoked by the FSM **after** construction: Login's ~73-widget construct (its
  child-build virtual, ≈vtable slot 14 / secondary +0xBC slot), and `COpeningWindow`'s build/init
  (primary vtable slot 14, +56). This is the "ordre de création" each dossier walks.
- **Per-frame drivers run only while the scene is live.** A render callback + an onUpdate/tick advance
  the scene each frame; Login additionally runs a **sub-FSM** that shows/hides already-built panels.
- **Teardown destroys the tree on scene exit.** Closing sets a window closing flag, an optional fade
  ramps alpha to 250, then the engine run-flag is cleared, the scene loop exits, and the window
  (with its children and texture list) is released. Control returns to the FSM, which advances.

### 3.1 Per-scene lifetime

| Phase | Login (state 1) | Loading (state 2) | Opening (state 3) | CharSelect (state 4) | InGame (state 5) |
|---|---|---|---|---|---|
| **Built when** | FSM enters state 1; window ctor (name "Loginer") spawns roster/channel worker threads but **no widgets**; the ~73-widget tree is created by the build virtual when the window opens. | FSM enters state 2; two ctors on the same object (`LoadHandler` then `LoadingScreen` visual phase) build the two quads inline + start the boot worker thread. | FSM enters state 3; window ctor (name "Opening") seeds fade=250 / wait-flag=1, **no widgets**; the build virtual (slot 14, +56) loads atlases and creates the 2 children. | Window driven into state 4 by the server character-list packet; ctor (name "Selecter") only zero-inits (5×880-byte preview block, no widgets); slot-14 build virtual copies the 5-slot roster out of NetHandler, loads atlases, builds ~124 widgets, then tail-builds the 3D preview scene. | Scene machine builds `MainHandler` ("Mainhander", slot 178, built outside the panel loop), the `MainWindow` singleton ("MainMaster", 1464 B, panel array at +0x238), and the world scene-graph; `MainHud_BuildAndRegisterPanels` then `new`s + ctors + slot-stores **178** panels (max index 218; gaps at 147, 178–217). |
| **What's created** | Server-list/login-form/EULA/server-grid/confirm panels + ExitPanel + ErrorPanel + the `LoginSecondPassword` PIN modal (built at construct, hidden). 4 atlases preloaded (`login_slice1`, `loginwindow`, `InventWindow`, `loginwindow_02`; PIN modal adds `password.dds`). | 1 random background texture + 1 looping SFX + 2 immediate-mode quads + embedded `GView`. **No widgets.** | `openning_scenario.dds` crawl image + a `mainwindow.dds` 3-state skip button; 4 `openning_001..004.dds` banner textures held raw + 1 looping SFX. | Slot-frame groups A/B, create-form panels, appearance/stat grid, name/confirm sub-panels, standalone create/delete/enter/server/world buttons, `Descriptor` tooltip + `ErrorPanel` + `ExitPanel`. 7 atlases (`loginwindow`/`mainwindow`/`InventWindow`/`tradekeepwindow`/`CarrierPigeon*`/`blacksheet`). **Plus** a full 3D preview scene (camera FOV 50°, env pinned 14:30, 5 lineup actors — scale **70.0**, idle motion-rate **3.0** — / 1 zoom actor — scale **81.0** — , ambient XEffect 380003000). (See `scenes/charselect.md §6.2`.) | All 178 HUD panels (core gauges, buff strips, `LinkPanel` hotbar, `ItemPanel`, skills/stat panels, `MapPanel`/`TotalMapPanel`, chat/announce panels, social/economy/special families, command bar, `GXCursor2D`). Atlases via the UiTex.txt registry + per-window lists (`InventWindow`/`slotboard`/`mainwindow`/`skillwindow`/`cubegamble*`…). **Plus** the 3D world ("charater scene", camera FOV 65°). |
| **Shown / hidden by** | A per-frame **sub-FSM** (substates 1→41) shows/hides panels: intro curtain → idle fields → credential validate → optional PIN modal poll → roster fetch → plate-pick → channel-endpoint fetch → join hand-off (build login key, secure context, GameState→7). Most panels are built hidden and revealed by substate. | No show/hide choreography — the two quads draw every frame; the gauge quad is skipped when progress is 0. | A timed banner slideshow FSM (4 states, ~17.5 s dwell, crossfade) cycles the raw banners; the crawl auto-scrolls then accepts manual scroll. Both children stay visible until skip/complete. | A window-level in-flight latch + per-slot state byte (0 normal / 1 locked / 2 deleted-this-session) gate every action; modals (create-name, class strip, rename, delete-confirm, select-confirm, status banner) raised via show-as-modal-and-grab-focus; create vs lineup swaps the 3D actor set. | A master toggle keymap + per-panel visible flag (offset +140); `DiamondHud_Toggle*Panel` wraps the vtable visibility setter (group-exclusive — opening one HUD group hides its siblings); tooltips auto-hide each frame, modals dismiss on Esc; chat/announce route through the central broadcast helper. |
| **Destroyed when** | The handshake reaches the join (substate 39/40): network starts, GameState→7; or the user exits (ExitPanel) / hits a fatal error. Window + children released on scene-loop exit. | The boot worker clears the busy flag → next tick clears the engine run-flag → scene loop exits → teardown (background texture COM-Released). | Skip (action-id 100 **or** Enter/ESC/Space) **or** natural slideshow+crawl completion → closing flag → fade alpha to 250 → run-flag cleared → both children hidden, window torn down. | Select/enter-confirm sends select-slot then (after the camera boom settles) the enter-game message → stops BGM, copies the chosen slot's descriptor/stats into the live-player globals → world entry. Window torn down on scene-loop exit. | Scene exit (logout / disconnect / return) clears the engine run-flag; the world scene-graph, MainWindow + its 178 panels, and MainHandler are released back through the FSM teardown list. |
| **Routes to** | Game session (state 7 hand-off) after credentials accepted; this front-end window is then gone. | **Opening (3)** or **CharSelect (4)** — decided **before loading starts** by the `[OPENNING] SKIP` ini gate read at the end of state 2 (SKIP≠0 → state 4; SKIP=0 → state 3). | **CharSelect (4)** (`SelectWindow`). On skip, also **persists `[OPENNING] SKIP=1`** so the Opening is bypassed on the next launch. | **InGame (state 5)** once a slot is entered (enter-game committed after the camera settles). | Terminal scene — returns to the front-end FSM on exit (logout / disconnect). |

### 3.2 Why Loading has no creation/teardown of widgets

Loading is intentionally **not** a `GUWindow` populated through the universal child constructor. It is
a single composite render object (`LoadHandler` + `LoadingScreen`) that draws two textured quads in
immediate mode while a background worker streams the data-table corpus. Consequently it has **no
child widgets to build or destroy, no hit-testing of child widgets, and no action-ids** — the shared
cursor hit-test still runs each frame (as in Login/Opening) but finds no registered interactive
handler, so it is inert. Its "teardown" is simply releasing the one background texture (COM Release)
and the embedded `GView` on scene exit. This is the load-bearing distinction between Loading and the
four GUWindow scenes, and the reason the §1 table marks it "no widgets / no action-ids".

---

## 4. 2D-over-3D composition (CharSelect · InGame)

Two scenes are not pure 2D GUI: a full 3D world is drawn *underneath* the GUI window. The
load-bearing finding is the same for both — **the 3D world and the 2D GUI are NOT two viewports;
they are two phases of one frame into one backbuffer.** The whole 3D scene is drawn first (optionally
through the offscreen glow/post chain, then present-blit), then the GUI window is drawn **last** under
an **orthographic 2D projection with depth test/write OFF**, painting on top of the finished 3D image.
Because depth is off, the GUI never Z-fights the world and always wins on top. There is no dedicated
world render-target the GUI is composited over; the GUI frame art's cutout regions are what reveal the
world beneath. This composition machinery is shared with the generic per-frame renderer
([specs/rendering.md](../specs/rendering.md) §2/§3/§4.2/§6) — the two scenes only differ in **which**
3D scene they build under the GUI.

| Aspect | CharSelect (state 4) | InGame (state 5) |
|---|---|---|
| 2D GUI layer | `SelectWindow` ("Selecter") — the frame, class buttons, name modal, stat grid, labels | `MainWindow` ("MainMaster") — the 178-slot HUD panel array |
| 3D scene beneath | A distinct preview scene built by `SelectWindow`'s scene-build facet (same scene-graph helpers as InGame) | One scene named **"charater scene"** built by the in-game scene-graph builder |
| Camera | Perspective, vertical FOV **50°** (overrides the 60° class default), near 5 / far 15000 | Perspective, vertical FOV(Y) **65°** (aspect-corrected), near 5 / far 15000 |
| World content | Streamed terrain (3×3 cold-start), shared `EnvironmentLightScene` light, env pinned to **14:30** (keyframe 29), ambient XEffect 380003000, **preview actors** (5 lineup: scale **70.0**, idle motion-rate **3.0**; 1 zoom: scale **81.0**) spawned via the same actor factory as the live world (see `scenes/charselect.md §6.2`) | Full live world via a `GScene` root + `TerrainManager` + `ActorManager`, a `GSwitch` of 5 draw layers (terrain / buildings+static / world-objects / actors / effects), sky + lens-flare prep |
| Actor spawn path | `ActorManager_SpawnActorFromDescriptor` — the **identical** skin/skeleton/anim/idle chain as the live `SmsgCharSpawn` path | the live `SmsgCharSpawn` spawn factory |
| Composition order | Build preview scene → draw 3D → draw "Selecter" frame last (no sub-viewport; full backbuffer, frame art masks the non-preview regions) | pre-scene/sky → opaque world (terrain→buildings→objects→actors+shadows) → FX/transparent → (offscreen) glow composite + present-blit → **HUD last** → optional FPS → EndScene |
| HUD-over-world state | 2D frame composed over the 3D preview in the same render-state pass | HUD bucket: depth OFF, lighting OFF, ortho (~−300..+300), alpha-blend off at enter (each quad/glyph opts into translucency); one inventory item-preview inset re-enables depth then restores it |

Both scenes therefore reuse the committed 3D specs (terrain / sky / effects / skinning / rendering)
**as-is** for the world content — only the GUI layer and the per-scene camera/anchor constants differ.
See [scenes/charselect.md](charselect.md) for the preview scene and [scenes/ingame_composition.md](ingame_composition.md)
for the in-game wiring.

---

## 5. Cross-references

- **Per-scene component trees & creation order:**
  [scenes/login.md](login.md) · [scenes/load.md](load.md) · [scenes/opening.md](opening.md) ·
  [scenes/charselect.md](charselect.md) · [scenes/ingame.md](ingame.md) ·
  [scenes/ingame_composition.md](ingame_composition.md)
- **Shared widget framework layout:**
  [structs/gucomponent.md](../structs/gucomponent.md) (GUComponent base + 13/14-slot vtable) ·
  [structs/guwindow.md](../structs/guwindow.md) (GUWindow/GUPanel container + Cmdhandler/GView/
  GUTextureList sub-objects)
- **UI subsystem behaviour:**
  [specs/ui_system.md](../specs/ui_system.md) (widget framework, 3-state sprite model, GUTextbox mask,
  msg.xdb 516-byte records, 15-slot HANGUL font table, 178-slot HUD roster, atlas mechanisms)
- **2D-over-3D rendering machinery:**
  [specs/rendering.md](../specs/rendering.md) (per-frame callbacks §2, draw order §3, per-bucket state §4.2,
  glow/post §6) — the world both composite scenes draw under their GUI.
- **Scene ordering & routing gates:**
  [scenes/scene_state_machine.md](scene_state_machine.md) (WinMain FSM 0→1→2→3/4→5, `[OPENNING] SKIP`)

---

## 6. Open / debugger-pending items (inherited from the source dossiers)

These are not new to this index — they are the unresolved items each source dossier flagged, surfaced
here so the cross-scene picture is honest:

- **Login:** which of the 3-state button frame pairs is *pressed* vs *hover* at sites where
  frame1==frame0; exact textbox IME/maxlen field widths and the PIN modal visible/submitted flag
  widths against a live instance; whether the PIN modal fires for the live account (gated by a
  per-account flag). (Layout itself is static ground truth.)
- **Loading:** the byte-exact per-vertex stride of the two quads (XYZRHW+color vs XYZ+color+UV) and
  the exact gauge texel sub-rect; the progress-fill direction reading (the load dossier supersedes the
  earlier horizontal-fill note — see its GAP-1).
- **Opening:** the literal client-rect right/top metrics feeding child positions (formula
  `x = right−120`, `y = top−200` is settled; literal pixels are runtime-resolved); the precise
  flag hand-off between the skip-requested byte and the fade-out byte.
- **CharSelect:** register-valued (label-centered) button dst/wh args need a live read for pixel-exact
  geometry; msg.xdb caption ids → CP949 strings to be dumped by id; whether a sub-viewport rect is set
  on the render-view at runtime (none seen statically). (Class hierarchy, creation order, action-ids
  and the 3D-preview constants are static ground truth.)
- **InGame:** full per-panel geometry for the ~75 manifest-key-driven panels resolves through the
  UI-manifest map ([formats/ui_manifests.md](../formats/ui_manifests.md)); per-panel field-offset
  tables for the heavyweight economy panels (Trade/Stall) and the still-auto-named panel ctors;
  reconciliation of the slot-8 reading (RepairNpcPanel vs ItemPanel) against the canonical 178-slot
  roster. (Slot indices, object sizes, vtable shape, class identity and creation order are static
  ground truth.)

All scenes are **static-settled** for class hierarchy, component tree / panel roster, creation order
and action-ids; the items above are precision confirmations best read live, not gaps in the structure.
