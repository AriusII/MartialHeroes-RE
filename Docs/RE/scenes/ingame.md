---
verification: written 2026-06-21 against the doida.exe binary (build 263bd994, full in-game 2D-HUD
  cartography pass, static IDA). The in-game HUD root (the MainWindow singleton "MainMaster"), its
  178-slot panel-pointer array (base = MainWindow+0x238, slot = (memberOffset − 0x238) / 4), the single
  HUD builder virtual (MainHud_BuildAndRegisterPanels) and its complete ordered build sequence, the
  per-panel RTTI class identities (178/178 slots now resolved to concrete classes), the CORE-HUD member
  layout (PlayerStatusPanel gauges + condition bar, target MopGagePanel), the 7-cell paged LinkPanel
  hotbar over the 240-entry quickslot model, the chat/announce family, the minimap/world-map pair, the
  inventory/storage family, the skills/stats family, the social/economy/special rosters, the
  toggle-keymap + show/hide dynamics, and the msg.xdb caption DB + 15-slot D3DX font table were ALL
  read at the slot / member-offset / RTTI / atlas-src-rect level and CONFIRMED against the same IDB.
  This dossier is the scene-level cartography of the in-game HUD; it REFERENCES specs/ui_system.md for
  the full 178-slot roster and the shared widget framework rather than re-listing every slot.
scene: InGame HUD (engine state 5)
evidence: [static-ida]
capture_verified: false
sources:
  - Docs/RE/specs/ui_system.md             # full 178-slot panel roster + widget framework + msg.xdb + fonts
  - Docs/RE/specs/ui_event_dispatch.md     # event routing, toggle keymap, focus/modal/tooltip dynamics
  - Docs/RE/specs/frontend_layout_tables.md # geometry/atlas-src-rect tables for the GU* widgets
  - Docs/RE/structs/gucomponent.md         # GUComponent base layout + 15-slot virtual interface
  - Docs/RE/structs/guwindow.md            # GUWindow/GUPanel container layout + sub-objects
  - Docs/RE/formats/ui_manifests.md        # UiTex.txt registry, skillicon.txt, manifest-key → geometry
---

# In-Game HUD Scene Dossier — Engine State 5 (`MainWindow` / "MainMaster")

> **Firewall-clean synthesis.** This dossier is rewritten in neutral prose from the binary and from
> the committed `Docs/RE/` specs listed in the front matter. It contains **no addresses-as-truth, no
> decompiler pseudo-C, and no mangled symbols.** Object offsets are interoperability facts (byte
> offsets relative to an object start), never memory addresses. Korean text is **CP949**. Where a
> per-panel internal field layout is still unrecovered (RTTI identity + slot + size only), the gap is
> flagged §12.

---

## 1. Overview — what the in-game HUD is

The in-game HUD is a **single top-level window object**, the **`MainWindow` singleton** (window-name
literal **"MainMaster"**), constructed once when the engine enters **state 5** (the in-game state of
the top-level scene FSM). It is a concrete subclass of the shared widget framework, with the standard
five-level base chain shared by every top-level front-end window:

```
MainWindow : Diamond::GUWindow : Diamond::GUPanel : Diamond::GUComponent : Diamond::EventHandler
```

(See `structs/guwindow.md` and `structs/gucomponent.md` for the base subobject layout and the shared
15-slot virtual interface; only slot 0 — per-class destructor — and slot 14 — the per-class scene-build
override — differ from the base.)

The MainWindow object is **0x5B8 (1464) bytes**. Its load-bearing member is a **flat pointer array of
HUD panels** based at member offset **+0x238**, one `GUComponent*` per slot. Capacity is ~220 (the
zero-init covers +0x238..+0x5B4); **exactly 178 slots are filled** by the build override. The slot
index for a stored pointer is:

> **slot = (memberOffset − 0x238) / 4**

The array is **non-contiguous**: index space runs 0..218 but with deliberate gaps at slots **42, 104,
147, 178–184, 187–217**. Slot **178** is special — it is **not** filled by the HUD builder (see §1.3).

### 1.1 The build pipeline

Three distinct objects are involved at state-5 entry, in this order:

1. **MainHandler** ("Mainhander") — a separate `Diamond::Cmdhandler` subclass (0xC8 / 200 bytes,
   command id 28158). It is the **in-game command/gameplay-key handler**, constructed by the scene
   state machine. It builds **no panels**; it only zeroes its own members and timestamps itself. It
   occupies panel **slot 178** (its array cell at +0x500 is deliberately skipped by the builder and
   filled here). **MainHandler is NOT MainWindow** — do not conflate the window with its handler.
2. **MainWindow singleton** — accessed via a Meyers one-shot (one static instance, atexit destructor).
   Its constructor runs the base `GUWindow` ctor (name "MainMaster"), installs the MainWindow primary
   vtable + a secondary vtable, and zeroes its service slots. The constructor builds **no panels**.
3. **MainHud_BuildAndRegisterPanels** — one large MainWindow virtual (vtable slot 14). It runs a flat
   sequence of `operator new(size)` → constructor → store-pointer-into-slot → (optional) `AddChild`.
   It fills the 178 panel slots. **Paint order = insertion (AddChild) order.** Slot writes are
   interleaved, NOT strictly ascending — the construction index and the slot index differ (see §11).

### 1.2 The universal panel build contract

Every panel is built through the shared GU* builders (documented in `structs/gucomponent.md` and
`specs/ui_system.md`). The **universal child argument order** is:

```
(textureId, x, y, w, h, srcX, srcY, actionId)
```

with the 1:1 sub-rect blit contract — source rect `(srcX,srcY)..(srcX+w,srcY+h)` blits **un-scaled**
to dest `(x,y,w,h)`. The `textureId` is **either** a global UiTex registry id **or** a per-window
texture-list handle (see §13 and `formats/ui_manifests.md`). About **75 of the 178 panels** are not
positioned by literal geometry but by a small **manifest key** (e.g. key 2, 9) looked up in the global
UI manifest map; for those the geometry lives in `formats/ui_manifests.md`, not in the build call.

### 1.3 Reading note — slot anchors vs. legacy anchors (binary-won)

A completeness pass resolved **all 178/178** slots to concrete RTTI classes and corrected several
earlier slot readings. **The binary wins.** Load-bearing corrections adopted here:

- **`ItemPanel` (inventory) = slot 4** (member +0x248), **not** slot 8. **Slot 8 = `RepairNpcPanel`.**
- The item-flow companion slots are: `DropItemPanel`=1, `TradePanel`=5, `RepairNpcPanel`=8,
  `KeepNpcPanel`=10, `PcTradePanel`=37, `StallKeeperPanel`=38, `DeliveryPanel`=40, `KeepPanel`=49,
  `StackCountPanel`=57, `QuestItemKeepPanel`=72, `ItemRepairPanel`=132, `ItemTransformPanel`=133.
  (Slot 53 = `RelationPanel`; slot 42 is an unused gap.)
- The full corrected 178-slot roster is in **`specs/ui_system.md`** — that is the canonical roster;
  this dossier groups it by **panel group** and gives only the anchor slots per group.

---

## 2. Core HUD group

The always-on player-status surface. These panels are not toggled — they live for the whole session.

| Component | Role | Type | Slot |
|---|---|---|---|
| `PlayerStatusPanel` ("GagePanel") | HP/MP/stamina gauges + portrait + level/condition bar | `GUPanel` subclass, 0x230 (560 B) | **15** |
| `PlayerBuffStatePanel` ("ActorStatePanel") | player buff/condition icon strip (dynamic icon vector) | `GUPanel` subclass, 0x544 (1348 B) | **27** (+ passive 28 / cash 29 / skill 30 variants) |
| Bottom command-bar cluster | 2 main action buttons + 5 expandable label-group sub-panels + trailing image | composite of `Button3State` / `GUPanel` / `GULabel` | **146–177** |
| `Gage3DPanel` | floating combat-damage 3D gauge overlay (world-projected) | `GUPanel` subclass, 0xE4 | **2** |

> The **target** gauge (`MopGagePanel`, slot 35) is a separate group — see §5.

### 2.1 `PlayerStatusPanel` — internal layout

The heart of the core HUD. Built top-left (dst 0,0; w=285, h=88; base atlas = UI-manifest key **4**).
Child-widget pointers and stat fields (offsets relative to the panel object; **naturally aligned, NOT
Pack=1** — i64 stat fields on 8-aligned offsets, child pointers on 4-aligned):

- **Fill / widget pointers:** HP-fill `+0xBC`, MP-fill `+0xC0`, stamina-fill `+0xC4`, condition
  vertical bar `+0xC8`, portrait button `+0xCC`, three hit-region buttons `+0xD0/+0xD4/+0xD8`, three
  labels `+0xDC/+0xE0/+0xE4`, two overlay buttons `+0xE8/+0xEC`. Each fill is a `GUComponent` image
  (size 0xA4; its width `+0x1C` and height `+0x20` are driven by the gauge math).
- **Stat values (8-byte pairs):** HP cur `+0xF0` / max `+0x108`; MP cur `+0xF8` / max `+0x110`;
  stamina cur `+0x100` / max `+0x118`. **Gauge fill width = `172 · current / max`, clamped to 172**
  (172 = the bar pixel width).
- **Level / condition vertical bar (내공/기력):** scale `+0x168`, current `+0x16C`, range-hi `+0x170`,
  level value `+0x174`. **Bar fill = `44 · cur / range`.** There is **no separate EXP fill bar** — the
  numeric level/exp is drawn as flag-gated text (flag byte `+0x154`).
- **Label text:** six 30-byte in-place CP949 char buffers `+0x178..+0x20E` hold the "cur/max" strings.
- **Portrait fire overlay:** a per-panel texture list at `(this+0x4E)` loads 18 low-HP warning frames
  `data/ui/face/fire/face_fire_01.tga` … `face_fire_18.tga`.

Vtable overrides: slot 7 = onDraw (updates gauges + labels + condition then paints), slot 14 = build
all child widgets.

### 2.2 `PlayerBuffStatePanel` family

One shared 1348-byte shape with four positioned views: primary buff strip (slot 27), passive (28),
cash/premium (29), skill-derived (30) — see §8 for the stats-column variants. The icon vector is
empty at build and populated at runtime by state pushes; visibility per icon is gated by a
has-state(local-player, id) query. Vtable: 21 slots; slot 6 onEvent, slot 7 onDraw (delegates to slot
18 = per-frame icon-draw loop), slot 14 reset, slot 16 rebuild icon vector.

### 2.3 Bottom command-bar cluster (slots 146–177)

Two main action buttons (atlases `data/ui/chunrihojung.dds` and `data/ui/InventWindow.dds`, 113×40, at
dst x=215 and x=334, y=236) followed by **5 identical 80×180 expandable label-group sub-panels** (each
holding 4 caption labels at y=15/35/55/75), then a trailing image element (slot 177). These are the
HUD's bottom toggle/readout strip.

### 2.4 Core-HUD assets

- `PlayerStatusPanel` base atlas = UI-manifest key 4; gauge/portrait sub-rects index into that atlas
  (HP src 331,694; MP src 504,694; stamina src 331,712; condition bar src 598,736; portrait src
  933,715).
- Low-HP overlay: 18 frames `data/ui/face/fire/face_fire_01.tga`…`face_fire_18.tga`.
- Command bar: `data/ui/chunrihojung.dds`, `data/ui/InventWindow.dds` (acquired via the static texture
  pool).
- Captions resolve through msg.xdb numeric ids (see §14). Atlas binaries are user-supplied originals —
  only path strings and sub-rect coordinates are recorded.

---

## 3. Hotbar / quickslot group

Two pieces: a **flat data model** (global) and an **on-screen toolbar panel** (HUD slot).

| Component | Role | Type | Slot |
|---|---|---|---|
| `LinkPanel` | on-screen quickslot toolbar; 7 visible cells = keys **1..7**, paged over the model | `GUPanel` subclass, 0x258 (600 B) | **14** (member +0x270) |
| Hotbar quickslot model | flat 240-entry quickslot data | global struct array (1920 B) | — (global, not a slot) |
| Hotbar page/tab table | runtime page/tab definitions | global struct array (~21 × 8 B) | — (global) |

### 3.1 The quickslot data model

A global flat array of **240 records × 8 bytes** (1920 B), naturally aligned. Record:
`+0x00 u32 entryKey` (entity/skill registry key; 0 = empty), `+0x04 u16 points` (skill points / charge
/ stack), `+0x06 u16 pad`. The server push handler reads a 20-byte block and, for the local player with
`slot < 240`, writes `{entryKey, points}` at stride `8·slot`. The bound `< 240 (0xF0)` appears in every
accessor.

### 3.2 `LinkPanel` — the toolbar

Built with geometry dstY=105, src 40,311; stored at MainWindow+0x270 (slot 14). It renders a fixed
visible row of **7 cells** mapped to number keys 1..7; the model index for a column is
`col + 7 · pageIndex`. Cells are drawn as icons onto a child **`GUCanvas3D`**, not as 7 separate child
widgets. LinkPanel-specific fields beyond the GUPanel base: `pageIndex` +0x1C0, `canvas3D` +0x19C,
`skillTimerWidget` +0xD4, `overlayImage` +0x13C, `hoverOrSelCell` +0x144 (−1 idle), `cellChild[7]`
+0x1A0, drag state at +0x1BC/+0x1C9/+0x1CC/+0x1D0/+0x1D4, three 7-element per-cell aux arrays at
+0x148/+0x164/+0x180.

Its build-subwidgets virtual (slot 14) creates a top frame strip + main body, then **3 `Button3State`
page tabs** (11×11, action ids **514/515/516**) on the frame, plus the `GUCanvas3D`, an overlay image,
and a `SkillTimerComponent` (cooldown widget). A runtime page/tab table (~21 records × 8 B:
`u8 kind` where 2/3/4 = tab/arrow cell, ≥0x0A = special; `u32 linkedSlot`) is zero-init in the image
and filled at runtime.

Vtable (15 slots, GUPanel shape) — class overrides: slot 6 onEvent (cell-click→cast, drag-link, tab
switch), slot 7 onDraw (poll keys 1..7, draw 7 page icons + cooldown + drag, auto-repeat cast), slot 14
build-subwidgets.

### 3.3 Hotbar network coupling & assets

- S2C **5/33** sets one quickslot entry; S2C **4/41** is the assign-result (clears the slot on failure,
  shows reason strings for reason byte 1..8). Wire framing is owned by the protocol specs.
- Textures resolved via the UI manifest map: key **1** → toolbar frame/background sheet (top strip src
  40,52; main body src 40,259; tabs src ~321–365,488–499; overlay 763,655), key **3** → skill/item icon
  sheet for the canvas-drawn cell icons. Related skill-subsystem assets: `data/ui/skillwindow.dds`,
  `data/ui/skillicon/skillicon.txt`, `data/ui/skillicon/stateicon.dds`. Cross-check the manifest keys
  against `formats/ui_manifests.md`.

---

## 4. Chat / message / announce group

All `GUPanel` subclasses (RTTI-named) built by the HUD builder.

| Component | Role | Type | Slot |
|---|---|---|---|
| `ActorChatPanel` | base/world layer; per-actor over-head overlay (guild crest + mini HP bar), world-projected | `GUPanel` subclass, 316 B | **0** |
| `ChatPanel` | chat INPUT line (active channel + edit buffer + cursor) | `GUPanel` subclass, 236 B | **18** |
| `ChatOutputPanel` | scrollback chat LOG (two 1000-line ring buffers) | `GUPanel` subclass, 72440 B | **21** |
| `MessagePanel` | transient system MESSAGE / toast line | `GUPanel` subclass, 228 B | **48** |
| `AnnouncePanel` | ANNOUNCE / notice banner (5-line string array) | `GUPanel` subclass, 604 B | **79** |
| `GUStringChatFilter` | chat-text input filter (length/profanity) | `GUStringFilter` subclass | — (filter object) |

> Slot 16 (member +0x278) is `StatusPanel`, **NOT** chat — excluded from this group.

### 4.1 Key layouts

- **`ChatOutputPanel`** (0x11AF8): `window_pos_x` +0x14, `window_pos_y` +0x18 (GUComponent base);
  `ring_buffer_all` +0x168 (1000 × 36-byte line records), `ring_buffer_view` +0x8E08 (1000 × 36);
  config tail `selected_line` +0x11AC0 (−1), `line_height_px` +0x11ACC (24), `window_size_lines`
  +0x11AD0 (1), `font_size` +0x11AF0 (12). The config is persisted to an ini section `%d_%s_CHAT` with
  keys CHAT_WINDOW_POS_X / POS_Y / SIZE / FONT_SIZE — chat font size is config-set, not a fixed font
  slot (see §14).
- **`ChatPanel`** (0xEC): callback reg +0xC4, edit-buffer triple +0xD8, `active_channel_index` u16
  +0xE8. Quick-macros SHIFT_1..9 persist as `%s_CHATSHORTCUT` (9-entry array).
- **`AnnouncePanel`** (0x25C): 5× std::string `line_text[5]` +0x164 (0x1C each) + two byte blocks
  +0xBC(0x3C) and +0xFC(0x64) for per-line timers/colors.

Vtable (shared 15-slot shape): slot 6 onEvent (ChatPanel = keyboard/IME + Enter-to-send), slot 7
onDraw, slot 14 class-specific (ChatOutputPanel = addLine + settings I/O; MessagePanel = pushMessage;
AnnouncePanel = pushNotice).

### 4.2 Assets

`data/effect/tex/chat.tga` (chat UI atlas), `data/cursor/cursechat.txt` (chat cursor); ActorChatPanel
overlay `data/ui/guildcrestback.dds` + `data/effect/tex/minibar.tga`. Persistence: ini `%d_%s_CHAT` and
`%s_CHATSHORTCUT`.

### 4.3 Toggle / dynamic

Enter opens chat input (and the chat suppressor swallows all hotkeys while typing — see §12); the chat
log/input/message/announce paint above the slot-0 actor overlay (insertion order). Toasts/announce are
the broadcast path used for hotkey-rejection "not allowed" notices.

---

## 5. Target group

| Component | Role | Type | Slot |
|---|---|---|---|
| `MopGagePanel` | selected-target status panel (mob/summon/pet **and** player target) | `GUPanel` subclass, ~0x1AE (430 B) | **35** |

`MopGagePanel : Diamond::GUPanel : Diamond::GUComponent`. Own fields start at +0xBC (base occupies
0x00..0xBB); naturally aligned. Highlights:

- **+0xC4 hpBarFill** — target HP bar; **width = `172 · curHP / maxHP` clamped to 172**.
- **+0xD0 hpPercentLabel** — formats HP as `"%10.2f %%"`.
- **+0xD4 targetNameLabel** — `"[level]name"` (`"[%d]%s"`), recolored per relation, masked to
  `********` on PvP maps.
- **+0xD8 ownerRelationLabel** — summon (msg 10037) / pet (msg 10038) / owner-name text.
- **+0x110 detailContainer** (row-bg strips +0x114/+0x118/+0x11C, **+0x120 detailRowLabels[15]**) — a
  hidden 15-row buff/detail sub-panel; one path writes a `"HP: %27s"` detail line into row index 1.
- **State:** +0x160 targetKey (0 = no target), +0x168 curHP, +0x16C maxHP, +0x178 level, +0x17C
  levelDiff (drives con-color icon +0xC8), +0x1AC targetActive, +0x1AD nameMasked.

The panel serves both kinds via `SetMobTarget` (mob/summon/pet) and `SetPlayerTarget` (masks name on
PvP maps). Vtable overrides: slot 6 onEvent, slot 7 onDraw (HP bar refreshes every draw), slot 14
buildLayout. Build order (slot 14): button-bar sub-panel → info sub-panel → hidden detail container →
3 row-bg strips → 15 row labels → 3 mode buttons (actions 1/2/3) → HP-fill/con-icon/grade-icon → name
/percent/owner labels (children empty + hidden until a target is set).

Assets: composed from runtime UI-atlas ids passed to child ctors (textureId resolved via UI-manifest
key 1), not a named asset chain. Child src-rects (HP-bar src 40,517; con-icon buckets srcX
40/53/66/79/92; grade-icon srcX 278/291/304/317 @ srcY 500/309) index that atlas — trace through
`formats/ui_manifests.md` for the concrete .dds.

---

## 6. Minimap / world-map group

| Component | Role | Type | Slot |
|---|---|---|---|
| `MapPanel` | on-screen minimap (always-on, top-right) | `GUPanel` subclass, 0x134 (308 B) | **161** (member +0x284) |
| `TotalMapPanel` | full-screen world-map window (opened on demand) | `GUPanel` subclass, 0xE0 (224 B) | **162** (member +0x288) |

Both 15-slot GUPanel-shape vtables overriding slot 6 onEvent, slot 7 onDraw, slot 14 build-children.

### 6.1 Minimap (`MapPanel`)

Built via the universal child ctor with args `(0, screenW−135, 0, 135, 195, 0, 0, 0)`.

- **Projection:** `screen = world · 0.125 + 66.5` per X and Z (**scale = 1/8 px per world unit**;
  interior 133×133, center 66.5; blips culled outside [0..133]).
- **Surface:** outer panel 135×195 top-right; inner map-body 135×138 @0,16 holds the scrolling tile
  image (133×133), tiled 3×3 from `data/effect/map/d%sx%dz%d.bmp` (128-px tiles, region-keyed).
- **Blips:** rotating player arrow (16×16) +0x108 (rotated by heading); actor blip (4×4) +0x100; party
  blip (10×10) +0x104; quest blip (16×16) +0x10C — colored via UI-map ids (player 13, default/target
  52/29, party 30, faction 53/55, npc 57/58, quest 75; flashing 50/56). Faction/PK display gated by a
  map-setting flag.
- **Chrome:** region-name +0xEC, coord-X +0xF0, coord-Z +0xF4, status +0xBC, hovered-target +0xD8;
  title-bar panel +0xD4 with three 3-state buttons (open-worldmap **5003** +0xE0, minimize **5001**
  +0xE4, close **5002** +0xE8); full-area drag button (action **5000**) +0xFC; drag-delta +0x110/+0x114,
  blink phase +0x118, last-region +0x11C, two display-toggle bools +0x120/+0x130.

### 6.2 World map (`TotalMapPanel`)

- **Projection:** `screen.x = mapW·0.5 + (world.x−player.x)·0.125 + pan_x`;
  `screen.z = mapH·0.5 + (world.z−player.z)·0.125 + pan_z` (same 1/8 base scale, centered on full-map
  dims).
- **Pan/zoom:** drag adds delta to `pan_x` (+0xD8) / `pan_z` (+0xDC); zoom-pan input ids 1000–1003
  nudge ±25; both reset to 0 on open. Tiles stepped at 1024 world units.
- **Members:** tile-handle cache +0xBC, default tile texture +0xC8, reusable tile image +0xCC, player
  arrow +0xD0, quest marker +0xD4.

### 6.3 Toggle / dynamic

World map opens from the minimap "world-map" button (action 5003) and from MainHandler dispatch action
id 32 (sets slot 162 visible + resets pan). **ESC (key 27)** or a click closes it. The `m` key toggles
the link/map cluster (see §12).

---

## 7. Inventory / equipment / storage group

| Component | Role | Type | Slot |
|---|---|---|---|
| `ItemPanel` | inventory window: 40-cell bag grid + 20 equipment slots + live Actor equipment doll | `GUPanel` subclass, 0xB00 (2816 B) | **4** |
| `KeepPanel` | warehouse/storage window, 60-cell grid (the warehouse equivalent — there is no WarehousePanel) | `GUPanel` subclass | **49** |
| `KeepNpcPanel` | NPC warehouse-keeper dialog (opens `KeepPanel`) | `GUPanel` subclass | **10** |
| `StackCountPanel` / `InvenSortPanel` / `QuestItemKeepPanel` / `DropItemPanel` | qty-split / sort / quest-item store / drop | `GUPanel` subclasses | 57 / 67 / 72 / 1 |

### 7.1 `ItemPanel` — internal layout

Built 318 wide × 732 tall (portrait). It owns **three cell collections** in one object:

- **40-cell bag grid** = 5 rows × 8 columns (`row = cell/8`, `col = cell%8`). Parallel pointer arrays
  (each 40 entries): icon (+0x418), hit-button (+0x680), held-item-object (+0x9D4), plus qty label,
  second label, frame widget, and a per-cell screen-position cache (40 × {i32 x, i32 y} at +0x7E4).
- **20-cell equipment-slot block** (the wearable doll slots): parallel pointer arrays (each 20) for
  widget / button / held-item-object (+0xA74).
- **Equipment doll** = a **live spawned `Actor`** (field +0x9C8 / 2504) with a per-class camera distance
  (+0x9CC / 2508; class id at actor+168 selects zoom) — it applies idle motion + weapon/joint FX, not a
  static image.

Paging: `currentPage` u8 +0xAD9 (2777); the player bag item table is 16-byte records, 40 per page;
global bag index = `cell + 40 · page`. Selection/drag state: `selectedFlag` +0xACC (2764, 1 on pickup),
`dragSrcPage` +0xAE0 (2784), `dragSrcCell` +0xAE4 (2788), `activeCell` +0xAF4 (2804) — all −1 idle.

Vtable (15 slots): slot 6 onEvent (item pickup/drop/use), slot 7 onDraw (grid + icons + doll), slot 10
getHitActionId, slot 14 layout/refresh.

### 7.2 Storage & companions

`KeepPanel` (slot 49) is a 60-cell storage grid (held-object array near +684), built the same way
(placement ctor + grid) and opened on demand by `KeepNpcPanel`. There is **no** WarehousePanel /
BankPanel / VaultPanel — storage is the Keep* family.

### 7.3 Assets

UI cell textures route through the GUComponent `textureId` field (+0x24) and the existing UI-manifest
chain (`formats/ui_manifests.md`); the item-slot grid uses the shared `data/ui/slotboard.dds` atlas
(each slot acquires the same pooled handle, then carries its own per-slot src-rect). The inventory frame
+ sort/tab buttons use `data/ui/InventWindow.dds`.

---

## 8. Skills / stats group

| Component | Role | Type | Slot |
|---|---|---|---|
| `SkillPanel` | skill-pipe / skill quick-bar panel (the skills facet) | `GUPanel` subclass, 0x1E4 (484 B) | **17** |
| `ActorStatePanel` | primary character-stats column (shared 1348-B base) | `GUPanel` subclass | **27** |
| `ActorStatePassivePanel` | passive/secondary stat column (left) | `ActorStatePanel` subclass, 0x544 | **28** |
| `ActorStateCashPanel` | cash/premium stat column (far right, taller) | `ActorStatePanel` subclass, 0x544 | **29** |
| `ActorStateSkillPanel` | skill-derived stats column | `ActorStatePanel` subclass, 0x544 | **30** |
| `ComboPanel` | combo-counter HUD widget | `GUPanel` subclass, 0x288 (648 B) | **85** |
| `FameStatePanel` | fame/reputation badge (100×25) | `GUPanel` subclass, 0xCC (204 B) | **108** |
| `RankStatePanel` | rank badge (100×25, beside fame) | `GUPanel` subclass, 0xD0 (208 B) | **109** |
| `StatusDistributePanel` | stat-point allocate | `GUPanel` subclass | **89** |

> The four `ActorState*` panels are one shared 1348-byte shape (the buff/condition strip of §2.2)
> reused as positioned stat columns — primary (slot 27, x=180), passive (28, x=50), cash (29, x=287,
> taller h=231), skill-derived (30, x=50, y=147). They share the base ctor and only swap vtable + run
> their own interior builder.

`SkillPanel` is anchored off-screen-top (dstY = −655) and slides in. Field highlights: four 0x24
candidate per-cell skill-pipe slot tables at +0xCC/+0x118/+0x13C and a 0x10 block at +0x100; three −1
sentinels at +0x19C / +0x1A0 / +0x1D8 (selectedSlot / hoverSlot / dragSourceSlot — a skill quick-bar
tracking selected/hover/drag); embedded std::string at +0x180.

Toggle: `k` opens/closes the skill window family (the skill window atlas is `data/ui/skillwindow.dds`).

---

## 9. Social group

All `GUPanel` subclasses built by the HUD builder, **except** the two buddy/relation sub-panels which
are children of the party container (created lazily, not build-array slots).

| Component | Role | Slot |
|---|---|---|
| `PartyPanel` | main party window; container that hosts the buddy/relation toggle + sub-panels | **43** |
| `MiniParty` | compact party HUD strip (8-element member sub-array, stride 0x10) | **44** |
| `PartyReqPanel` | incoming party-invite prompt | **45** |
| `RequestPanel` | generic accept/decline request prompt (shared social invites) | **41** |
| `OtherInfo` | other-player info popup (target inspect) | **46** |
| `ShowDownReq` | duel / show-down (PvP challenge) request | **47** |
| `GuildNpcPanel` | guild NPC dialog (create/join) | **11** |
| `GuildReaskPanel` | guild confirm/re-ask; entry for the guild-diplomacy-declare C2S | **25** |
| `GuildAPanel` | main guild window; two 50-entry member tables (stride 0x40) | **65** |
| `GuildFameDonatorPanel` | guild fame / donator list | **69** |
| `PublicPeacePanel` | public-peace / diplomacy-peace declaration | **70** |
| `BroodWarMapInfoPanel` | guild-war (brood-war) map-info / war status | **92** |
| `MediatePanel` | inter-guild mediation / negotiation | **95** |
| `revengevote` | revenge-vote (PK/relation revenge) | **107** |
| `CarrierPigeonPanal` | carrier-pigeon hub (in-game mail) | **96** |
| `CarrierPigeonSendPanel` | carrier-pigeon compose/send (4 string fields) | **97** |
| `CarrierPigeonReadPanel` | carrier-pigeon read/inbox (8 string fields) | **98** |
| `FriendPanel` | friend/buddy list (sub-panel of `PartyPanel`, **not** a build slot) | — |
| `RelationPanel` | relation panel (sub-panel of `PartyPanel`, **not** a build slot; slot 53 is the relation build-slot) | — |

`FriendPanel` / `RelationPanel` are created only by their own slot-0 "create" vfunc; the buddy/relation
toggle (sets a +548 visible flag) is invoked from `PartyPanel`'s onEvent. **Correction (binary-won):**
build-slot **185 is `GXCursor2D`** (the 2D mouse cursor), **not** a relation panel — a legacy
scene-table addressing of the *toggle* conflated the two.

Diplomacy / war wiring: `GuildReaskPanel` (25) is the diplomacy-declare entry; `PublicPeacePanel` (70) =
peace; `MediatePanel` (95) = mediation; `BroodWarMapInfoPanel` (92) = war map/status. Assets:
`data/ui/relation.dds` (RelationPanel), `data/ui/friendmove.dds` (friend-list cursor); captions resolve
through the CP949 text-resource lookup, so RTTI class names are the strongest neutral identifiers.

---

## 10. Economy group

All 16 `GUPanel` subclasses (`<Panel> : GUPanel : GUComponent`); each overrides slot 0 (dtor), 6
(onEvent), 7 (onDraw), 14 (open/refresh). Built eagerly at HUD construction, visibility toggled when the
relevant NPC/trade action fires.

| Component | Role | Slot | Obj size |
|---|---|---|---|
| `TradePanel` | NPC shop / vendor trade (buy+sell); embeds a 1000-element item grid (stride 0x24) | **5** | 0x9840 |
| `RepairNpcPanel` | NPC blacksmith / repair dialog | **8** | 0xD4 |
| `KeepNpcPanel` | NPC warehouse-keeper (storage) dialog | **10** | 0xD8 |
| `PcTradePanel` | player-to-player trade window | **37** | 0x1050 |
| `StallKeeperPanel` | own-stall management/setup (player vendor); large embedded item array | **38** | 0x9FE8 |
| `ProductPanel` | product/goods list rows (vendor item list) | **39** | 0x5CC |
| `DeliveryPanel` | item delivery / parcel send | **40** | 0xE88 |
| `KeepPanel` | warehouse storage contents grid | **49** | 0x6AC |
| `LetterPanel` | mail / letter compose-read | **55** | 0x10C |
| `PriceInputPanel` | price-entry helper (sell / stall pricing) | **62** | 0x120 |
| `StallListPanel` | browse list of open player stalls | **86** | 0x1D8 |
| `GoodsPanel` | NPC vendor buy panel (goods to purchase) | **88** | 0x868 |
| `CarrierPigeonSendPanel` | carrier-pigeon (mail) send | **97** | 0x30C |
| `CarrierPigeonReadPanel` | carrier-pigeon (mail) read | **98** | 0x228 |
| `TenderInfoPanel` | tender / bid (auction-like) info | **118** | 0x1F0 |
| `ItemRepairPanel` | item-repair confirm/result | **132** | 0x128 |

Per-class internal field-offset tables are not yet recovered for the two heavyweight panels
(`TradePanel`, `StallKeeperPanel`) — the static span of the embedded item array is known; element
layout wants a live read (§12). Assets (binary-confirmed load sites): `data/ui/itemshop.dds`,
`data/ui/buywindow.dds`, `data/ui/itemshoppopup.dds` (vendor buy flow / GoodsPanel),
`data/ui/delivery.dds` (DeliveryPanel), `data/ui/stalllist.dds` (StallListPanel).
`data/ui/tradekeepwindow.dds` belongs to the char-select SelectWindow, **not** the in-game HUD.

---

## 11. Special group (upgrade / pet / gamble / quest / options / help / dialogs)

The remaining toggled functional windows. All share the standard 15-slot GUComponent vtable, overriding
slot 0 (build/init), 6 (onEvent), 7 (onDraw), 14 (main handler / show+refresh).

| Component | Role | Slot | Obj size |
|---|---|---|---|
| `HelpPanel` | help / help-overlay window | **34** | 0x228 |
| `PetPanel` | pet / companion window | **52** | 0x5D0 |
| `QuestPanel` | quest log / tracker (40-entry record array) | **64** | 0xAE8 |
| `NpcQuestPanel` | NPC quest dialog / offer | **73** | 0x2BC |
| `NpcQuestMsgPanel` | NPC quest message sub-panel | **74** | 0xE0 |
| `Gamble` (CubeGamble) | Cube-Gamble panel; 20-reel slot array | **110** | 0x8C0 |
| `UpgradePanel` | item upgrade / enchant | **131** | 0x134 |
| `ItemRepairPanel` | item repair (upgrade family) | **132** | 0x128 |
| `ItemTransformPanel` | item transform (upgrade family) | **133** | 0x11C |
| `UpgradeProcessPanel` | upgrade-in-progress overlay (texture-list member) | **135** | 0x10C |
| `OptionPanel` (base/container) | options-menu base container | **22** | 0xD0 |
| `OptionPanel_Character` | options: Character tab | **141** | 0x174 |
| `OptionPanel_Graphic` | options: Graphics tab | **142** | 0x1E0 |
| `OptionPanel_Sound` | options: Sound tab | **143** | 0x16C |
| `OptionPanel_Other` | options: Other tab | **140** | 0x100 |

Additional modal/dialog/legal panels recovered in the completeness pass (RTTI identity + slot + size;
internal layouts deferred, §12) — a representative selection grouped by family (full list in
`specs/ui_system.md`):

- **Confirm/input modals:** `ReAskPanel` 24, `ConfirmPanel` 32, `ItemConfirmPanel` 54, `CountInputPanel`
  58, `SkillConfirmPanel` 59, `EventItemConfirmPanel` 119, `Descriptor` (item-tooltip float) 33.
- **NPC dialog:** `GatherNpcPanel` 13, `PcPanel` 36, `NpcQuestTalkPanel` 76, `NpcSearch` 114,
  `FameBuffNpcPanel` 115.
- **Quest:** `QuestGiveUpPanel` 71, `QuestResultPanel` 77.
- **Guild/war:** `GuildCreateAPanel` 66, `GuildShowPanel` 67, `GuildMemberPosSetPanel` 68, `WarInfoPanel`
  81, `GuildWarInfoPanel` 82, `WarStoneInfoPanel` 91, `BroodWarListPanel` 93, `BroodWarAllyStatePanel`
  94, `Pandemonium` 105, `Revengesummons` 106.
- **Gift-character flow (6):** `GfitCharNameInputPanel` 125, `GiftCharConfirmStep1/2` 126/127,
  `GiftCharConfirmWaiting` 128, `GiftCharReceiveConfirm` 129, `GiftCharSecondPassword` 130.
- **Anti-bot / legal:** `AutoQuestionPanel` 122, `AutoCheckPanel` 123, `GameAddictionWarningPanel` 124,
  `NoPkPenaltyAlarmPanel` 138, `AutoPenaltyAlarmPanel` 139, `PlaytimePanel` 120.
- **Misc:** `DefaultMenu` (context menu) 6, `SurvivePanel` 56, `KeyBackSetup` 75, `GMPanel` 84,
  `GameClassPanel` 121, `UpgradeResultPanel` 134, `GreetPanel` 144, `TutorTalkPanel` 145.

**Lazy / not slot-registered:** `ProductPanel` cash-shop product window is instantiated on demand (16 ×
0x14 entry array + 3 texture lists). Assets: `data/ui/itemupgrade.dds` (upgrade family),
`data/ui/cubegamble.dds` / `_ani` / `_help` (CubeGamble, slot 110), `data/ui/itemrepair.dds`.

---

## 12. Creation order

The single builder runs a flat, deterministic sequence (construction index → slot). The full numbered
table (180 records / 178 slots, with per-row ctor / captured args / object size / add-child flag) lives
in the source dossiers; the high-level phase breakdown:

| Phase (construction idx) | Slots / content |
|---|---|
| 0–1 | top overlay label + manifest header label (173/174) |
| 2–5 | always-on world overlays, AddChild'd to root: ActorChat(0), DropItem(1), Gage3D(2), overlay(67) |
| 6–12 | process/upgrade family incl. **UpgradeProcessPanel slot 135** |
| 13–19 | big NPC-shop windows: Trade(5), Keep(49), PcTrade(37), StallKeeper(38, ~40 KB), Delivery(40), Emoticon(78) |
| 20–37 | inventory/quickslot/item family — incl. **ItemPanel slot 4**, the huge ChatOutput (slot 21, ~72 KB), **PetPanel slot 52** |
| 38–48 | actor-state / stat panels (base + Passive/Cash/Skill) |
| 49–65 | **target gauge MopGage slot 35** + a long run of manifest-driven small HUD panels |
| 66–83 | shop/skill/quest/guild big windows + party/mini-party + manifest panels |
| 84–105 | gather + secondary panels, TotalMap(162), Announce(79), big-alarm |
| 106–122 | top-overlay labels, carrier-pigeon trio, portal, FontSee title/subtitle/time/subject, fame/rank |
| 123–147 | **CubeGamble slot 110**, GM panel, event popups, centered alarm/confirm panels |
| 148–179 | late manifest panels, 2 texture-pool static handles, **4 identical 4-row label groups** (BuildPanel 80×180 + 4 labels each), the `GXCursor2D` cursor (last AddChild), a final tooltip pair (slot 218) |

Within `PlayerStatusPanel` the child paint order is: portrait/overlay buttons → HP/MP/stamina
hit-regions → HP/MP/stamina fill images → three labels (fills paint over the static background, labels
on top). The **arg convention varies** per ctor: literal geometry for `Build*` / `GULabel` /
explicit-xywh panels; a **manifest key** for the ~75 manifest-lookup panels (geometry in
`formats/ui_manifests.md`); a screen-center-helper result (`(screenW−w)/2` or `(screenH−h)/2`) for
centered panels.

---

## 13. Toggle keymap & dynamic behavior

Event routing (see `specs/ui_event_dispatch.md` for full detail): a DirectInput8 keyboard thread builds
a normalized record and sets/clears bits in a 1033-bit key/action bitset; the main thread drains it. The
**HUD root window onEvent** routes a key-down record to the **master toggle switch**; the in-game
**command handler** (MainHandler) handles non-panel gameplay keys.

**Three gates** run before any toggle fires: (1) the **chat input-line suppressor** (swallows hotkeys
while typing in chat); (2) the **input-focus manager** focused-field check; (3) the **modifier query**
(action-ids 1012=Shift, 1013=Ctrl, 1014=Alt).

### 13.1 Recovered keymap (no modifier)

| Key | Action |
|---|---|
| Esc | close topmost open panel |
| Space | self-target |
| b | war list |
| c | character/status group |
| f | relation group |
| g | guild family |
| h | help / shortcut bar |
| i | inventory/character group |
| j | guild-war / info |
| k | close-many-panels (and skill-window family) |
| l | list / log |
| m | link / map cluster |
| n | guild panel (permission-gated; else "not allowed" notice) |
| o | NPC-dialog / option |
| p | close-all |
| q | close-all + transition |
| s | inventory group |
| u / w / z | misc panels |
| x | auto-pickup (sends a C2S, not a panel) |
| y | toggle HUD dock / minimap cluster |

**Ctrl:** Ctrl+a = attack/peace stance (emote C2S), Ctrl+z = panel. **Alt+1..9** = quick-select party
member.

### 13.2 Show/hide, focus, tooltips, modals

- Each GU panel stores its **visible flag at +140**; show/hide uses the universal vtable visibility
  setter; the toggle helpers wrap it with layout + sound. Opening one HUD group **hides its sibling
  slots** (group-exclusive).
- Focus/capture goes through the input-focus-manager singleton (field id vs focused id); mouse capture
  taken on button-down, released on up.
- **Tooltip** = a floating label built on HUD-button hover, positioned by the cursor, auto-hidden when
  nothing is hovered that frame.
- **Modal confirm** = a dedicated slot with is-open/dismiss helpers; Esc dismisses.
- ~90 `*Panel` onEvent handlers self-close on Esc when visible (some also re-accept their open-hotkey to
  close, e.g. Help = `h`); the central switch always performs the OPEN (a closed panel receives no
  events).
- HUD action click sound id 862020102; UI strings resolved at runtime via the caption getter (notices
  44008, 45003, 10082; name-tag 8034/8035; chat-mode 47001/47002/47003; effect-cull 2065/2066/2067).

---

## 14. Text / font

The in-game HUD draws all localized text through one caption DB and one font table — both stood up
**before** the HUD scene (at boot / state 1), so they are resident the whole in-game session. (See
`specs/ui_system.md` for the framework-level treatment.)

### 14.1 Caption source — `msg.xdb`

`data/script/msg.xdb` is loaded once at boot. Record format: **fixed 516-byte records** — the first 4
bytes are the **numeric message id** (the key), the next 512 bytes are the CP949 caption.
`record_count = filesize / 516`. Records go into a heap blob plus a sorted **id-keyed map**. The single
caption-by-id getter returns the cached caption or, for a missing id, a one-time placeholder
`"Id[%d] msg not found."` — proof captions are keyed by **numeric id**, never by string name. Every
localized HUD label resolves an id through this getter.

### 14.2 Font slots — 15-entry D3DX table

A lazily-built singleton **15-slot font table**, fetched by all GU-widget draw routines (the single
source of HUD faces/metrics). Each slot is created via `D3DXCreateFontA` with **HANGEUL_CHARSET (129)**;
the per-slot record stores face name, base size, char-width metric, row-height metric and weight; a
rebuild routine recreates all 15 on a device reset. Faces used: **DotumChe, Dotum, BatangChe**.

| Slot | Face | Size | Note |
|---|---|---|---|
| 0 | DotumChe | 12 px, weight 0 | **default** HUD face |
| 2 | DotumChe | 32 px, bold | big numbers / titles |
| 3, 6 | DotumChe | 24 px, bold | headings |
| 5–9 | BatangChe | (serif) | serif body text |

### 14.3 How captions bind to components

One shared text rasterizer is used by every widget's onDraw: it takes the string, an ARGB color, and a
**per-widget font-slot index**, computes the dest rect from that slot's char-width/row-height glyph
metrics, and submits the D3DX font draw. `GULabel`'s onDraw selects its slot from instance field +0xE4,
color = (alpha)<<24 | RGB, text from a raw std::string (+0xA4) or an ellipsized copy (+0xC0). The
fit/ellipsize routine computes `max chars = widget_width / slot_charWidth`, truncates and appends a
2-byte ellipsis with **CP949 double-byte boundary handling** (never splits a Korean character).

### 14.4 Number / quantity formatting

No locale comma-grouping — plain `sprintf` templates: `%d/%d` (HP/MP/stamina gauges), `%d/%I64d`,
`%s[%d/%d]` / `%s [%d/%d]` (named cur/max bars). Currency / large quantities are **64-bit** (`%I64d`),
shown raw without thousands separators.

### 14.5 Floating combat captions (separate path)

In-world damage numbers use a **distinct sprite renderer** (NOT the D3DX font path):
`data/effect/tex/att-font.dds` (normal digits), `data/effect/tex/cri-font.dds` (critical digits),
`data/effect/tex/miss.tga` (MISS), with a 12-frame rise-and-fade (alpha/scale += 1/12 per frame).

### 14.6 Chat font

Config-driven: `ChatOutputPanel` reads `CHAT_WINDOW_FONT_SIZE` (with POS_X/POS_Y/SIZE) from a UI config
map keyed by `%d_%s_CHAT` — chat text size is config-set, not a fixed font slot (§4.1).

---

## 15. 2D asset mechanisms (summary)

Two texture-reference mechanisms feed the panel `textureId` argument (full detail in
`formats/ui_manifests.md`):

- **A — global `UiTex.txt` registry.** `data/ui/UiTex.txt` is parsed at boot into a global ordered map
  keyed by int `tex_id` (script block `UI_TEXTURE { DDS {…} MSK {…} }`; `#` comment; quotes stripped;
  negative ids skipped; 1-based sequential index; DDS+MSK rows with the same id pair color atlas with
  alpha mask). Lookup defaults to key 1 when missing.
- **B — direct per-window load.** A loader reads `data/ui/<X>.dds` via the VFS chokepoint, makes a D3D
  texture, and appends the handle to the owning window's texture list (window+0x220). Used by the HUD
  for dedicated atlases (`InventWindow.dds`, `blacksheet copy.dds`, `slotboard.dds`, …).

A component's `textureId` is **either** a UiTex global id (A) **or** a per-window list handle (B) — not
interchangeable. The **1:1 sub-rect blit** contract (§1.2) applies in both cases. DDS format is
loader-requested by a render-quality tier (DXT5/DXT3/DXT2); `slotboard.dds` is DXT2 unconditionally.
Manifests beyond UiTex.txt: `data/ui/skillicon/skillicon.txt` (SKILL block, 4 cols),
`data/ui/guildicon/crestlist.txt` (23×23 crest tiles). Atlas binaries are user-supplied originals,
never committed — only path strings and src-rect coordinates are recorded here.

---

## 16. Open items / gaps (deferred — debugger-confirmable, NOT blocking)

- **Per-panel internal field layouts** for the ~55 modal/dialog/special panels resolved in the
  completeness pass (RTTI identity + slot + size only; no member walks). Owed: member walks (or a live
  `dbg_read` of an open instance).
- **Heavyweight item-array element layout** inside `TradePanel` and `StallKeeperPanel` (static gives the
  span; the element fields want a live read).
- **`SkillPanel` / `ActorStatePanel` interior field semantics** — the SkillPanel per-cell slot tables
  and the ActorState* stat-row child widgets (built by the per-class interior builder) are not fully
  enumerated.
- **Exact UiTex.txt id→path/mask table + atlas pixel dimensions** — sample-unverified (need legally
  owned .dds/.txt bytes).
- **`InventWindow.dds` button alternate-state src origins** — hover-vs-pressed assignment of the two
  alternate origins; the unusually large w/h of the two command-bar buttons reads like a window-frame
  click region and wants a live confirm.

> These are confirmations, not unknowns: the scene-level cartography (slot formula, 178/178 RTTI roster,
> build order, core-HUD member layout, hotbar model, caption/font path, asset mechanisms) is **settled**
> statically. The single live item flagged across passes is reconciling the event record type-byte
> numbering between the HUD-root and command-handler onEvent consumers under a real key event — pilot the
> maintainer's session; never `dbg_start`.
