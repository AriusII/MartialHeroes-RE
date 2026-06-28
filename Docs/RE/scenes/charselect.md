---
verification: written 2026-06-21 against the doida.exe binary (build 263bd994, full Character-Select
  scene cartography pass, static IDA). The SelectWindow object (RTTI .?AVSelectWindow@@, single base
  Diamond::GUWindow, total size 0x1888 = 6280 bytes, window name literal "Selecter"), its deferred
  component-tree builder (the slot-14 build virtual), the universal GU child-build ABI, the exact
  widget census, the complete action-id roster + bound-member-offset map, the 2D atlas linkages, the
  dynamic create/select/enter/delete/rename flow + modals, the msg.xdb caption + 15-slot HANGUL font
  binding, and the full 3D character-preview scene composition were ALL re-read at the element /
  member-offset / atlas-src-rect level and CONFIRMED. The five 0x370 (880-byte) per-slot
  character-preview sub-records at +0x238 (5-iteration init loop, stride 880) anchor the slot model.
  This dossier consolidates six recovery facets (component tree, creation order, 2D assets, dynamic
  flow/modals, text/font, 3D preview) reconciled by a completeness pass that settled the load-bearing
  builder ABI and the 127-widget census. Cross-referenced to the shared GU* framework
  (structs/gucomponent.md, structs/guwindow.md, specs/ui_system.md) and to the character-render chains
  (specs/skinning.md, specs/equipment_visuals.md). (Companion: scenes/login.md for state-1 Login,
  scenes/frontend_ui_components.md for the cross-scene GUI index.)
  2026-06-24 audit: §7.4 enter-game copy sequence extended with the extra NetHandler flag byte (high
  byte of roster word 206 + boolean from field 1237) written post-send alongside descriptor/stats/level;
  semantic is capture-pending (§9 item 5). IDB SHA 263bd994 re-confirmed; no structural drift.
  2026-06-24 debugger-session (live `?ext=dbg`, IDB SHA 263bd994): live `dbg_read` confirmed the
  slot-record stride 880 with name@+568 (CP949), occupancy@+614, class@+620 (1=Musa/2=Salsu/3=Dosa/
  4=Monk), default-equip ids ≈+656; observed a 3/5-occupied sample. GAP recorded: the 3/1
  CharacterList handler did not fire while slots were populated (delivery-timing vs NetHandler-
  persistence follow-up). See §2.1.
  CYCLE 14 re-anchor (f61f66a9): confirmatory — subsystem cleanly relocated, 1 re-confirmed SAME, 0 corrected.
ida_anchor: f61f66a9ae0ec1e946105b2ecff76e8930cb1d1367df64e5688a5266f5ad9963
ida_reverified: 2026-06-27
scene: Character Select (engine state 4)
evidence: [static-ida]
capture_verified: false
sources:
  - Docs/RE/structs/gucomponent.md            # GUComponent/GUPanel base layout + virtual interface
  - Docs/RE/structs/guwindow.md               # GUWindow multiple-inheritance layout + sub-objects
  - Docs/RE/specs/ui_system.md                # UI subsystem (widget framework, msg.xdb, 15-slot fonts)
  - Docs/RE/specs/frontend_scenes.md          # §3 character-selection sub-machine; state-4 placement
  - Docs/RE/specs/frontend_layout_tables.md   # numeric oracle for front-end geometry
  - Docs/RE/specs/character_creation.md       # create form: 52-byte appearance body, point-buy, name checks
  - Docs/RE/specs/skinning.md                 # preview actor skin/skeleton/idle-motion chain
  - Docs/RE/specs/equipment_visuals.md        # preview per-class starter gear / worn-gear visuals
  - Docs/RE/structs/spawn_descriptor.md       # 880-byte per-slot descriptor parsed from 3/1
  - Docs/RE/opcodes.md                        # 3/1, 3/5, 3/6, 3/7, 1/6, 1/7, 1/9, 1/13, 1/14 opcode catalogue
  - Docs/RE/packets/3-1_character_list.yaml   # roster-population wire spec
  - Docs/RE/packets/3-5_enter_game_response.yaml  # SmsgEnterGameAck (reply to 1/9)
  - Docs/RE/packets/3-6_rename_char_result.yaml   # SmsgRenameCharResult (12-byte, create-success slot-write)
  - Docs/RE/packets/3-7_char_manage_result.yaml   # SmsgCharManageResult (8-byte, delete-confirm + cooldown)
---

# Character-Select Scene Dossier — Engine State 4 (`SelectWindow` / "Selecter")

> **Firewall-clean synthesis.** This dossier is rewritten in neutral prose from dirty-room recovery
> notes and the committed `Docs/RE/` specs listed in the front matter. It contains **no
> addresses-as-truth, no decompiler pseudo-C, and no credential / key literals.** Object offsets are
> interoperability facts (byte offsets relative to an object start), never memory addresses. Korean
> text is **CP949** and lives in `data/script/msg.xdb` on the VFS — only numeric message ids appear
> here. Where a pixel-exact geometry value or a runtime visibility gate is still unconfirmed, the gap
> is flagged in §9.

---

## 1. Overview

The **Character-Select scene** is the front end the engine constructs and runs at **engine state 4**
— it is the scene that follows Opening (state 3) in the WinMain scene state machine (`0 INIT → 1
Login → 2 Loading → 3 Opening → 4 CharSelect`, see `frontend_scenes.md` §3 and
`scene_state_machine.md`). It is a single top-level window object, **`SelectWindow`** (RTTI class name
`.?AVSelectWindow@@`, window name literal **"Selecter"**), one of exactly **five** `GUWindow`-derived
top-level windows in the client. Its responsibilities, end to end:

- **roster display** — paint up to **five character slots** populated from the inbound `3/1
  SmsgCharacterList` packet (name / level / world position / appearance);
- **slot selection** — pick a slot either by clicking a 2D slot button **or** by clicking the
  character's **3D preview model** (a world-ray vs per-slot box hit test);
- **create** — open a class-selection strip + a name-entry modal, run a local point-buy / name-check
  pass, and send the create-character request (`1/6`, see `character_creation.md`);
- **delete** — a delete-confirm modal that issues a slot-keyed "move-out" delete request, honouring a
  server-side delete cooldown;
- **rename** — a rename panel (a SelectWindow child at +0x179C/+0x17AC) that runs banned-word /
  charset / duplicate checks and sends opcode 1/13 directly;
- **enter world** — commit the selected slot, settle the camera, copy the slot's descriptor/stats to
  the live-player globals, and send the enter-game request (`1/9`).

**Critical structural finding.** The character "3D preview" is **not** a small render-target tucked
under the UI — it is a **full real 3D world scene** (camera + streamed terrain + environment light +
ambient effect + lit/skinned actors), with the 2D "Selecter" window painted over it in the same
back-buffer pass. The preview characters are ordinary world **Actors** spawned through the same
spawn factory the live `SmsgCharSpawn` path uses (§6). This matches `frontend_scenes.md` §3 ("the
char-select scene is a full 3D world, not a 2D screen").

**The bridge into the scene** is the inbound **`3/1 SmsgCharacterList`**: it arrives during the live
login loop (after the secure handshake), populates the five-slot scratch, and drives the engine
**GameState = 4** (see `login.md` §1 — Login is state 1, char-select is state 4; do not conflate).
**The bridge out** is the enter-game commit (`1/9`), after which the engine leaves state 4 for world
entry.

---

## 2. Object & ownership inventory

`SelectWindow` is allocated by the scene state machine when the roster packet drives the engine into
state 4. Its object is **CODE-CONFIRMED at `0x1888` = 6280 bytes**. The base chain is the shared
five-level GU* hierarchy (identical to LoginWindow / MainWindow):

```
SelectWindow → Diamond::GUWindow → Diamond::GUPanel → Diamond::GUComponent → Diamond::EventHandler
```

Only vtable slot 0 (per-class destructor) and slot 14 (per-class scene-build override) are
class-specific; every other slot of the 15-slot virtual interface shares the common GUWindow code.
The GUWindow base sub-objects (primary vtable +0x00; window flags +0x08; embedded `CmdHandler`
command-handler sub-object vtable +0xBC; auxiliary `Diamond::GView` scene view +0xE8; per-window
`GUTextureList` atlas list +0x220) are documented in `structs/guwindow.md` — not re-derived here.

### 2.1 The five character-slot sub-records (`+0x238`, stride 0x370)

The load-bearing data structure is an **array of five `0x370` (880-byte) per-slot
character-preview sub-records based at `+0x238`** — proven by a 5-iteration init loop with stride
880. Each sub-record carries the slot's spawn descriptor / stats handles and per-slot preview state
(facing, actor handle, occupied flag). The remainder of the object is init-only sentinel / bound-
widget-pointer fields (zeros and `-1` "no-selection" indices). The non-array fields' widths come
from memset lengths; their exact semantics (count vs handle vs sub-struct) want a live `dbg_read`
of an instance to settle (§9). Proposed canonical name: `SelectWindow.char_slot_groups`.

> **2026-06-24 debugger-session — live slot-record layout (DEBUGGER-CONFIRMED).** A live `?ext=dbg`
> `dbg_read` of the SelectWindow slot array confirmed the per-slot record stride is **880 bytes
> (0x370)** and read these in-record field positions: the **character name at +568 (CP949)**, an
> **occupancy word at +614**, the **class word at +620** (with **1 = Musa, 2 = Salsu, 3 = Dosa,
> 4 = Monk**), and **per-class default-equipment ids beginning ≈ +656**. Observed sample: an account
> with **3 of 5 slots occupied** (slots 3 and 4 zeroed) — slot 0 class 1 (Musa), slot 1 class 1
> (Musa), slot 2 class 3 (Dosa); the top-left info panel corroborated the slot-2 character's
> power/level and saved world position. These offsets are now DEBUGGER-CONFIRMED for the live
> SelectWindow instance (settling part of §9 item 4 for the named fields above). — confidence:
> DEBUGGER-CONFIRMED.
>
> **GAP (delivery-timing follow-up).** In the same session the `3/1` CharacterList handler did **not**
> fire a probe even though the slots were nonetheless populated — i.e. the visible slot data was
> readable live regardless of the `3/1` path firing. This points to a
> delivery-timing-vs-NetHandler-persistence question: confirm the exact `3/1` delivery timing relative
> to NetHandler scratch persistence. (Consistent with §7.1: the authoritative per-slot data lives in
> the NetHandler, and the SelectWindow slot records are readable live independent of the `3/1` handler
> firing this run.) Flagged for follow-up.

### 2.2 Bound child-widget pointer fields

`SelectWindow` stores a pointer to each interactive child in a dedicated member slot (this is how the
window's command dispatcher resolves an action-id back to its widget). The complete bound-field map is
in §4.3. The **container** panels and the standalone class objects:

| Member offset | Object / role | Class |
|---|---|---|
| +0x1550 | `slotFrameGroupA` — character-slot frame group A (3 slot image frames + 3 ENTER buttons) | `GUPanel` |
| +0x1554 | `slotFrameGroupB` — character-slot frame group B (frames + class icons) | `GUPanel` |
| +0x15B0 | `relocateOverlayPanel` — move/relocate overlay (actions 62/63: confirm = send opcode 1/14 MoveCharacter; cancel = hide) | `GUPanel` |
| +0x15C8 | `overlayClosePanel` — plain overlay panel (action 64: close/hide only; no message send) | `GUPanel` |
| +0x1604 | `createFormInputPanel` — create-form input: name label + class buttons (actions 10/11/12/13) | `GUPanel` |
| +0x16AC | `appearanceGridPanel` — main appearance/stat grid (image cells + 14 adjust buttons + value labels) | `GUPanel` |
| +0x16B0 | `nameInputTextbox` — masked character-name input (parent = appearanceGridPanel) | `GUTextbox` |
| +0x1770 | `errorPanel` — Error dialog sub-panel (class object) | `ErrorPanel` |
| +0x1774 | `exitPanel` — Exit/quit-confirm dialog sub-panel (class object) | `ExitPanel` |
| +0x179C | `renameConfirmPanel` — rename panel (own textbox + buttons, actions 59/60: confirm = send opcode 1/13 RenameCharacter; cancel = hide) | `GUPanel` |
| +0x17AC | rename panel textbox (parent = renameConfirmPanel; source of 17-byte name payload for opcode 1/13) | `GUTextbox` |
| +0x17B8 | extra create sub-panel (action 74) | `GUPanel` |
| +0x17D0 | select-slot confirm panel (actions 54/55: confirm = send opcode 1/7 SelectCharacterSlot + hide; cancel = hide) | `GUPanel` |
| +0x1868 | `tooltipDescriptor` — hover tooltip/description panel (added directly to window) | `Descriptor` |

> **Distinction (completeness-confirmed).** `errorPanel` (the `ErrorPanel` **class object** at +0x1770)
> and the **select-slot confirm panel** (a plain `GUPanel` at +0x17D0, holding action buttons 54/55) are
> **two different objects** — an earlier reading that placed "errorConfirmPanel" at +0x17D0 conflated
> them. The **rename panel** (+0x179C) is a `SelectWindow` child textbox panel (not a shared global
> singleton): `SelectWindow` owns the rename input at +0x17AC and sends opcode 1/13 directly from it
> (see §4.3 and §7.4).

The window drives its sub-views by **visibility toggling**, not by spawning separate scenes — all
children are built once at scene build and shown/hidden by the build/reset routine and per-frame tick.

---

## 3. Component tree

`SelectWindow` (the window root) directly parents the container panels, the standalone class-select /
create / delete / enter-world / server / world buttons, the two text input fields, the standalone
dialog class objects (`Descriptor`, `ErrorPanel`, `ExitPanel`), and the appearance grid. Each
container panel in turn parents its own image cells, labels, textbox, and buttons. Paint order =
insertion order (depth-first, parent before children).

### 3.1 Widget base classes used (all RTTI + vtable confirmed)

| Widget type | RTTI class | instance size |
|---|---|---|
| Image frame | `Diamond::GUComponent` | 164 B |
| Panel / container | `Diamond::GUPanel` | 188 B |
| 3-state button | `Diamond::GUButton` | 252 B |
| Text label | `Diamond::GULabel` | 240 B |
| Textbox (input) | `Diamond::GUTextbox` | 228 B |
| Tooltip panel | `Descriptor` | 444 B |
| Error dialog | `ErrorPanel` | 232 B |
| Exit / confirm dialog | `ExitPanel` | 204 B |

### 3.2 Exact widget census (settled)

The completeness pass settled the widget count by reading the `operator new` count inside the build
virtual: **127 top-level widgets** built by `SelectWindow`'s build/init. The exact builder census:

| Builder | Count |
|---|---|
| `GUButton` (3-state buttons) | 46 |
| `GUComponent` (image frames) | 37 |
| `GULabel` (text labels) | 29 |
| `GUPanel` (containers) | 10 |
| `GUTextbox` (text inputs) | 2 |
| `Descriptor` (tooltip) | 1 |
| `ErrorPanel` (error dialog) | 1 |
| `ExitPanel` (exit/confirm dialog) | 1 |
| **Total top-level** | **127** |

Attach calls: **71 `AddChild` + 42 `AddChildWithAction`**. (The 42 with-action attaches correspond to
the 42 action-ids in §4.3.) The modal class objects build their **own** internal children in their own
constructors (the count gap that an order-only walk misses): **`ExitPanel`** builds a caption label + 2
buttons (OK/Cancel, actions 50/51) + a panel; **`ErrorPanel`** builds a panel; **`Descriptor`** builds
a panel. The earlier "~124 / 13-button" figures undercounted (the 13-button spinner is one *cluster*;
there are 46 buttons total); the 279 / 284 / 489 figures elsewhere were **call-instruction** counts,
not widget counts.

### 3.3 Component groups (role + parent)

| Group | Members | Parent | Actions |
|---|---|---|---|
| **Backdrop** | root backdrop panel → nested container panel | window → backdrop | — |
| **Top tab strip** | 3 image frames + 3 top-right buttons | window | 1, 2, 3 |
| **Slot-frame group A** | 3 slot image frames + 3 ENTER buttons | `slotFrameGroupA` (+0x1550) | 1, 2, 3 |
| **Slot-frame group B** | frames + class icons + class buttons | `slotFrameGroupB` (+0x1554) | 4, 5, 6 |
| **Left nameplate frame** | 3 image frames + 2 digit/icon strips (6 images) + 3 nameplate labels | window | — |
| **Class-select buttons** | 3 buttons (create-form class choice) | window | 4, 5, 6 |
| **Move/relocate overlay** | move/relocate confirm + cancel | `relocateOverlayPanel` (+0x15B0) | 62, 63 |
| **Overlay-close panel** | plain close / back button | `overlayClosePanel` (+0x15C8) | 64 |
| **Create-form input** | name label + class buttons | `createFormInputPanel` (+0x1604) | 10, 11, 12, 13 |
| **Extra create sub-panel** | one button | extra create panel (+0x17B8) | 74 |
| **Appearance/stat grid** | rows of small image cells (digits/value tiles) + 9 stat-icon images + 7 stat-value bar images + 7 stat-value labels + 14 adjust/spinner buttons + 2 page-nav buttons + masked name textbox | `appearanceGridPanel` (+0x16AC) | 21, 22, 25–34 (face ±, stat spinners), 35 (create-confirm), 36 (create-cancel) |
| **Rename confirm panel** | rename textbox + 2 buttons | `renameConfirmPanel` (+0x179C) | 59, 60 |
| **Select-slot confirm panel** | 2 buttons | select-slot confirm (+0x17D0) | 54, 55 |
| **Actor-yaw button cluster** | 4 small +/− yaw buttons (normal + hold variants) | window | 66, 67, 68, 69 |
| **Actor-yaw drag / camera-zoom drag** | actor-yaw drag-hold (2 dir) + camera boom-zoom drag-hold (2 dir) | window | 70, 71, 72, 73 |
| **Carrier-pigeon / mail cluster** | button cluster + close button + carrier panel + textbox | window | 65 |
| **Relocate/open-overlay** | 1 button (window level, opens `relocateOverlayPanel` when slot picked) | window | 61 |
| **Tooltip** | `Descriptor` hover panel | window | — |
| **Error dialog** | `ErrorPanel` class object | window | — |
| **Exit/confirm dialog** | `ExitPanel` class object (own OK/Cancel) | window | 50, 51 |

---

## 4. Creation order, geometry & action-ids

### 4.1 Where the children are built (deferred build)

The `SelectWindow` constructor is **field-zero only**: it installs the vtable, calls the base GUWindow
ctor, inits the five `0x370` per-slot sub-records (the `+0x238` block) and the "Selecter" window name,
and memsets its state fields. **It creates zero UI children.** The entire 2D component tree is built
**lazily by vtable slot 14** — the per-class scene-build override (proposed name
`SelectWindow::BuildComponentTree`). That builder:

1. memcpys the **five-slot server character list** out of the NetHandler singleton (five `0x370`
   descriptor records + five 96-byte stats blocks + five state bytes) into the slot sub-records;
2. loads the UI atlases (§5);
3. builds all 2D chrome in insertion order (§4.2);
4. tail-calls the **3D preview-scene builder** (§6) and a slot-count label refresh;
5. shows the children.

A sibling routine builds **only** the 3D viewport (§6) and creates no 2D widgets.

### 4.2 The universal GU child-build ABI (SETTLED — load-bearing)

The panel builder and the 3-state-button builder both forward verbatim to the base image-component
builder. Decoding the base builder's field writes (and proving it on a real panel call — the
`InventWindow.dds` modal frame) gives the **canonical argument order**:

```
BuildImageComponent(textureId, dstX, dstY, srcX, srcY, w, h, color/flag/actionId)
```

- `textureId` is a per-window **texture handle** (an entry in the window's `GUTextureList`), **not** a
  0..N index.
- The atlas sub-rect is `[srcX, srcY] .. [srcX + w, srcY + h]` — a 1:1 unscaled blit.
- The 3-state button builder takes the same first 8 args for the **normal** image, plus two extra args
  = the **hover** and **pressed** atlas-rect origins (same w×h), plus the action id.
- A panel adds an opaque/clip flag.
- The action-id is **bound separately**: `AddChild(parent, child)` reparents; `AddChildWithAction
  (parent, child, actionId)` additionally writes `child + 0x10 = actionId` (the GUComponent action-id
  field) **and** stores the child pointer into the owning `SelectWindow` member slot (§4.3).

> **Earlier-reading corrections (resolved by the completeness pass).** Two prior ABI readings were
> wrong and any geometry table decoded under them must be re-read against the canonical order above:
> (a) the scene-anchor hint `(tex, x, y, w, h, srcX, srcY, action)` has **w/h and srcX/srcY swapped**;
> (b) a reading of `(sentinel −1, dstX, dstY, srcX, srcY, w, h, atlas)` is wrong twice — the leading
> `−1` is the builder's **default `+4` field**, not a sentinel argument, and the texture handle is
> **arg 0**, not the last argument (the last is color/flag/action). The `srcX,srcY` precede `w,h`.

### 4.3 Action-id → bound-member-offset roster (complete, 42 entries)

The window's command dispatcher latches the consuming widget's action-id into the window's
current-action field; the dispatcher then resolves the action to its bound member pointer. The
`+offset` is the `SelectWindow` member that stores each bound child-widget pointer.

| Action | Member | Role |
|---|---|---|
| 1 | +0x1564 | slot-A ENTER button 1 |
| 2 | +0x1568 | slot-A ENTER button 2 |
| 3 | +0x156C | slot-A ENTER button 3 |
| 4 | +0x15A0 (parent `slotFrameGroupB` +0x1554) | class-select / slot button — create-form class choice |
| 5 | +0x15A4 (parent `slotFrameGroupB` +0x1554) | class-select / slot button — create-form class choice |
| 6 | +0x15A8 (parent `slotFrameGroupB` +0x1554) | class-select / slot button — create-form class choice |
| 10 | +0x1690 | class pick MONK — `ApplyClassSelection(0)` → internal class id 4 |
| 11 | +0x1694 | class pick MUSA — `ApplyClassSelection(1)` → internal class id 1 |
| 12 | +0x1698 | class pick DOSA — `ApplyClassSelection(2)` → internal class id 3 |
| 13 | +0x169C | class pick SALSU — `ApplyClassSelection(3)` → internal class id 2 |
| 21 | +0x1714 | FACE+ — increments face index, clamped max 7; 2D portrait only, no 3D rebuild |
| 22 | +0x1710 | FACE− — decrements face index, clamped min 1; 2D portrait only, no 3D rebuild |
| 25 | +0x1720 | Stat0 INCREMENT — edit `createBlob+0x1C`; requires budget > 0 |
| 26 | +0x1724 | Stat1 INCREMENT — edit `createBlob+0x20`; requires budget > 0 |
| 27 | +0x1728 | Stat2 INCREMENT — edit `createBlob+0x24`; requires budget > 0 |
| 28 | +0x172C | Stat4 INCREMENT — edit `createBlob+0x2C`; requires budget > 0 |
| 29 | +0x1730 | Stat3 INCREMENT — edit `createBlob+0x28`; requires budget > 0 |
| 30 | +0x1734 | Stat0 DECREMENT — floor 10; requires budget < 5 |
| 31 | +0x1738 | Stat1 DECREMENT — floor 10; requires budget < 5 |
| 32 | +0x173C | Stat2 DECREMENT — floor 10; requires budget < 5 |
| 33 | +0x1740 | Stat4 DECREMENT — floor 10; requires budget > 0 (guard differs) |
| 34 | +0x1744 | Stat3 DECREMENT — floor 10; requires budget < 5 |
| 35 | +0x1748 | CREATE-CONFIRM — name validate (non-empty / reserved / banned-word / charset / min-len 2) → stage name+appearance to createBlob → busy-latch → send `Cmsg_CreateCharacter_Send` (`1/6`, 52 B). The create send. |
| 36 | +0x174C | CREATE-CANCEL / reset-to-select-scene — calls `SelectWindow_ResetScene` + restarts scene BGM; no send. |
| 50 | (ExitPanel-internal) | quit-confirm OK |
| 51 | (ExitPanel-internal) | quit-confirm Cancel |
| 54 | +0x17D8 | select-slot CONFIRM: sends opcode 1/7 SelectCharacterSlot ([committed-slot, 0]), copies picked char name to MainWindow, hides panel |
| 55 | +0x17DC | select-slot CANCEL: hides the select-slot confirm panel |
| 59 | +0x17B0 | rename CONFIRM: runs banned-word/charset/duplicate checks then sends opcode 1/13 RenameCharacter (18-byte payload: 1-byte target slot + 17-byte name from +0x17AC textbox) |
| 60 | +0x17B4 | rename CANCEL: blur + disable-IME, hide rename input panel |
| 61 | +0x15AC (parent `slotFrameGroupB` +0x1554) | conditionally OPENS the move/relocate overlay (`relocateOverlayPanel`, +0x15B0) when a slot is currently picked; otherwise shows error tooltip |
| 62 | +0x17D8 | move/relocate CONFIRM: hides panel then sends opcode 1/14 MoveCharacter/slot-reorder (1-byte slot) |
| 63 | +0x17DC | move/relocate CANCEL: hide the relocate overlay panel |
| 64 | +0x15D4 | plain panel-close: hides overlay panel +0x15C8 (cancel/back; no message send) |
| 65 | +0x15D8 | carrier-pigeon textbox / close |
| 66 | +0x1654 | actor-yaw button + (writes `this+6068`; button press, positive yaw) |
| 67 | +0x1658 | actor-yaw button + hold (writes `this+6068`; hold variant) |
| 68 | +0x1678 | actor-yaw button − (writes `this+6072`; button press, negative yaw) |
| 69 | +0x167C | actor-yaw button − hold (writes `this+6072`; hold variant) |
| 70 | +0x1680 | actor-yaw drag-hold direction A (writes `this+6084 = 70`; cleared on drag-release type-5/6) |
| 71 | +0x1684 | actor-yaw drag-hold direction B (writes `this+6084 = 71`; cleared on drag-release type-5/6) |
| 72 | +0x1688 | camera boom-zoom drag-hold direction A (writes `this+6088 = 72`; press-and-hold) |
| 73 | +0x168C | camera boom-zoom drag-hold direction B (writes `this+6088 = 73`; press-and-hold) |
| 74 | +0x17C0 | extra create sub-panel button |

> **Shared-slot fact (confirmed real).** Members **+0x17D8 / +0x17DC** are reused by the
> move/relocate overlay (actions 62/63) **and** the select-slot confirm panel (actions 54/55). The two
> panels are mutually exclusive (never visible at once), so they deliberately share the same two
> bound-pointer cells.

> **Action-id reconciliation (build 263bd994, static dispatcher read).** 54/55 operate on the
> select-slot confirm panel and send opcode 1/7, not an error-confirm; 59/60 operate on the rename
> panel (+0x17AC) and send opcode 1/13; 62/63 operate on the relocate overlay and send opcode 1/14;
> 64 is a plain close with no send; 61 opens the relocate overlay (no enter-world). Enter-world is
> reached via `SelectWindow_EnterGame` on message type-4 / secondary-codes 99, 120, or 10 — not via
> an action-id button. Actions **66–69** are actor-yaw **button** presses (two +/− buttons × normal/hold
> variants, writing `this+6068`/`this+6072`); actions **70/71** are actor-yaw **drag-hold** accumulators
> (writing `this+6084`, cleared on drag-release type-5/6, read by `SelectWindow_TickSelectedPreviewYaw`);
> actions **72/73** are camera boom-zoom **drag-hold** accumulators (writing `this+6088`, read by
> `SelectWindow_TickCameraBoomZoom`). These are three distinct widgets and three distinct tick functions.
>
> **CYCLE-12 correction (create-confirm/cancel mis-listing).** Prior to the CYCLE-12 dispatcher walk,
> actions 35 and 36 were listed among the point-buy stat spinners (as if the spinner block ran 25–36).
> The complete `SelectWindow_HandleCommand` switch body confirms: spinner ids run **25–34** only (10
> ids, five stats × +/−); **35 = CREATE-CONFIRM** (the sole send site for opcode 1/6, 52 B); **36 =
> CREATE-CANCEL** (`SelectWindow_ResetScene` + scene BGM restart, no send). The prior listing was
> erroneous. Note also that the +/− spinner pairs are NOT sequentially paired: Stat0=(25+,30−),
> Stat1=(26+,31−), Stat2=(27+,32−), Stat3=(29+,34−), Stat4=(28+,33−). Source: CYCLE-12 dirty-room
> recovery `Docs/RE/_dirty/cycle12/charselect/create_flow_actions.md`, build 263bd994.

### 4.4 Creation order (≈127 top-level widgets, in insertion order)

The build virtual lays widgets out in this order (each carries a decoded `(dstX, dstY, srcX, srcY,
w, h, atlas)`):

1. root backdrop panel → nested container
2. top tab strip (3 images)
3. top-right 3 buttons (actions **1, 2, 3**)
4. left nameplate frame (3 images)
5. two digit/icon strips (6 images)
6. three nameplate labels
7. move/relocate overlay (actions **62, 63**)
8. extra create sub-panel (action **74**)
9. overlay-close panel (action **64**)
10. nav/action cluster (actions **61, 4, 5, 6**)
11. lower portrait/stat frame panel + 5 frame images
12. 9 stat-icon images
13. 7 stat-value bar images
14. 7 stat-value labels
15. appearance/stat grid buttons: face-adjust (actions **22, 21**) + stat spinners (actions **25–34**, 10 ids) + create-confirm/cancel (actions **35, 36**) + 2 page-nav buttons
16. detail sub-panel (nameplate + 3 labels + actions **10, 11, 12, 13**)
17. actor-yaw button cluster (actions **66, 67, 68, 69**)
18. actor-yaw drag-hold + camera boom-zoom drag-hold widgets (actions **70, 71, 72, 73**)
19. carrier-pigeon button cluster + close/corner button + carrier panel + textbox (action **65**)
20. error/confirm popup (actions **54, 55**)
21. name/confirm popup (actions **59, 60**)
22. name-entry image frame + textbox + label
23. **tail**: slot-count label refresh + the 3D preview-scene build (§6)

Geometry note: several arguments resolve to **runtime-centred** values (a button X is often centred
against a msg.xdb-sized caption, using the screen-dimension globals + a text-width helper), so a
handful of `dstX/dstY` and `w/h` values are not statically pixel-exact. Those want a live read
(§9) — but they do not affect 1:1 layout faithfulness because the 2D frame art masks the
non-preview regions.

---

## 5. 2D asset linkages

### 5.1 The texture → VFS chain

Atlases are loaded by the texture loader: if the VFS is mounted it does a VFS find-and-read then
creates the D3D texture from the in-memory bytes, else it falls back to a loose disk/VFS path. **The
literal `data/ui/<name>.dds` string IS the VFS key** (no transform), and the handle is appended to
the window's `GUTextureList` (no dedup — `InventWindow.dds` is loaded **twice**). All resolution is
through the mounted VFS index by the logical path string.

### 5.2 Atlases referenced by the CharSelect build

| Atlas (VFS path) | Role |
|---|---|
| `data/ui/loginwindow.dds` | **Primary CharSelect chrome**: left character-list frame, slot rows, class/info icon strips, top tab buttons, bottom buttons, name/level/class text strips |
| `data/ui/mainwindow.dds` | Bottom action-bar buttons |
| `data/ui/InventWindow.dds` | Per-slot info-card frame + its buttons; **loaded a 2nd time** for the error/confirm modal frame (atlas sub-rect `src[340,190] 318×647`) |
| `data/ui/CarrierPigeonPerson.dds` | Carrier-pigeon / mail button group |
| `data/ui/CarrierPigeonAll.dds` | Paired carrier-pigeon atlas |
| `data/ui/tradekeepwindow.dds` | Small (≈23×23) icon-button cluster |
| `data/ui/blacksheet.dds` | Full-screen dim/overlay sheet drawn behind modals |

### 5.3 The component → sub-rect contract

Per §4.2: a widget's pixel source is `atlas[srcX, srcY] .. atlas[srcX + w, srcY + h]`, blitted 1:1 to
`(dstX, dstY)`. A 3-state button additionally stores its **hover** and **pressed** atlas-rect origins
(same w×h). Concrete per-widget dst/src/size tables (left list frame, 3 class-icon strips, 3 top tabs,
the 5-slot info-card pattern, the bottom action bar, the toggle clusters, the `ExitPanel` OK/Cancel
rows at `srcY` 860 / 900, the create-name modal frame) live in the dirty cartography notes; promote
those into `Docs/RE/specs/asset_linkages.md` if a per-pixel table is needed by the port.

---

## 6. The 3D character-preview composition

The single load-bearing finding (§1): the preview is a **full 3D world scene**, built with the *same*
scene-graph helpers as the in-game HUD builds the live world (same perspective-camera class, same
`EnvironmentLightScene` light singleton, same scene-node / boom-root construction, same terrain
streamer, same actor spawn factory). Composition each frame is: render the 3D world (camera +
streamed terrain + environment light + ambient effect + lit/skinned actors), then draw the 2D
"Selecter" window over it in the same render-state pass — **one back buffer, world first, UI overlay
after.** No sub-viewport rect is set statically; the 3D scene uses the full back buffer and the 2D
frame art defines the visible "preview window" cutout (matching the original's look — world visible
through the frame).

### 6.1 Scene build (one-time, at window init)

- **Environment / hour** — env time-of-day is pinned to **14:30** (the keyframe at that hour), area 0.
  Fixed lighting hour, not a live clock.
- **Terrain** — the standard terrain streamer cold-starts a **3×3** block around the scene anchor; the
  preview characters stand on **real streamed terrain**, not a flat stage. (Chain: `formats/terrain.md`,
  `terrain_layers.md`, `bgtexture_lst.md`; `specs/terrain-streaming.md`, `rendering.md`.)
- **Camera** — a perspective camera (same class as in-game) with **vertical FOV overridden to 50°**
  (the class default is 60°), aspect from the screen-dimension globals, **near = 5.0, far = 15000.0**.
  The camera rig is a **6-keyframe free-look dolly**: six keyframes store independent Euler angles
  (pitch in indices 0–5, yaw in indices 6–11) with no look-at target; the pivot is baked to world
  origin (0, 0, 0). An entry dolly plays from keyframe 0 (set at ctor) to keyframe 1 (set at each
  scene reset) — the dolly holds at KF1 thereafter. No further keyframes are driven during normal
  char-select idling; the boom-zoom tick (actions 72/73, see §4.3) modifies the camera distance
  field independently.
- **Lighting** — the shared global `EnvironmentLightScene` singleton (the same object the live world
  uses); preview lighting is the standard environment/sky model at the pinned 14:30 hour, **no bespoke
  studio rig** (`specs/environment.md`).
- **Ambient effect** — a single user XEffect (**id 380003000**, the char-select ambient effect) is
  spawned **after `ClearAllUserXEffects`** at world position **(508.483, 69.887, −9758.569)** with
  identity direction, scale 1.0, and loop = 1 (`specs/effects.md`).
- **Scene anchor** (world origin for all preview placement) — **X = 2048.0, Y = 0.0, Z = −6144.0**.

### 6.2 Character placement & rendering

All preview actors go through the **shared spawn factory** — the exact same factory the live
`SmsgCharSpawn` path uses — so the preview resolves the identical skin / skeleton / appearance-key /
AnimCatalog / pose-build / idle-motion chain as the in-world model (no separate preview mesh path).
See `specs/skinning.md`, `specs/equipment_visuals.md`, `structs/spawn_descriptor.md`.

- **Lineup (existing characters)** — five actors spawned from their stored **880-byte** spawn
  descriptors, laid out in a single **row of five**, **12-unit** horizontal spacing, the row slightly
  bowed toward the camera in the middle, each spawned with scale field +1160 = **70.0** (confirmed:
  `SelectWindow_SpawnPreviewLineup` writes 70.0 to each lineup actor's scale field). The idle-motion
  playback-rate multiplier on each lineup actor is **3.0** (written separately to actor+100) — this is
  a motion-rate override, distinct from the scale value. Weapon/joint effects refreshed. The per-slot
  facing byte selects yaw **0** (front) or **π / 180°** (back).
- **Zoom / create preview** — a single actor synthesised from the chosen **class + body**, with
  per-class **starter gear baked into the descriptor** (preview-only cosmetic item ids — see
  `equipment_visuals.md`). Placed centred, ≈**56 units closer** to the camera than the lineup row,
  scale fields +1160 and +1164 both set to **81.0** (confirmed: `SelectWindow_BuildZoomPreviewActor`
  writes 81.0 to both scale fields). Initial facing from the stored preview-yaw.

### 6.3 Interaction (per-frame tick)

- **Drag-to-rotate spins the model, not the camera** — input maps to `yaw −= 2·dt` or `yaw += 2·dt`,
  applied only to the selected / zoom actor's facing direction + quaternion (camera and lineup
  untouched).
- **Camera boom-zoom** — actions 72 / 73 store the action-id into a tick-state field (main+6276);
  `SelectWindow_TickCameraBoomZoom` reads that field each frame and increments (72) or decrements (73)
  the camera object's distance field (+276) by `dt × 10`; the press-and-hold is released by resetting
  the field to 0 on mouse-up. No clamp is applied. Camera object pointer is held at main+6204. (Distinct
  from actions 70/71, which store into main+6272 and drive `SelectWindow_TickSelectedPreviewYaw` — the
  yaw of the selected create-preview actor.)
- **The 2D UI overlay** is drawn after the scene, inside the same D3D fixed-function render-state pass
  (texture-stage / alpha-blend states programmed on the device wrapper, then the window draw vtable
  entry).

> Composition note: **oracle > spec for pixels** — a spec-faithful preview can still diverge from the
> real client's image; verify against the official char-select captures (`godot-fidelity-check`).

---

## 7. Dynamic flow, modals & the slot model

### 7.1 Roster population (`3/1 SmsgCharacterList`)

The character-list handler (major **3**, minor **1**) is the population path and re-confirms
`packets/3-1_character_list.yaml`. Wire shape: a 3-byte header `[server, channel, slotMask]`, then the
five-slot scratch is zeroed, then **for each set bit of `slotMask`** it reads: an **880-byte**
spawn/appearance descriptor, a **96-byte** stats block, a **1-byte** selectable/occupied flag, and a
**4-byte** timing word (delete-cooldown ready-time / lock), and builds a trimmed name (max 17 bytes,
leading/trailing spaces stripped). On completion it drives the engine to state 4. The NetHandler
singleton holds a back-pointer to the `SelectWindow` so all inbound handlers can refresh it.

> The per-slot **96-byte stats block is NOT consumed on the char-select screen** (the info row shows
> only name / level / world-XZ). It is parsed and stored for the enter-game copy (§7.4).

### 7.2 Slot selection & highlight

Two selection mechanisms coexist:

- **2D** — clicking a slot button (action-id), routed by the GUWindow base dispatcher (topmost-child-
  first, first-consumer-wins; the consuming widget's action-id is latched into the window's
  current-action field).
- **3D** — clicking the preview actor: the dispatcher unprojects the click to a world ray and tests it
  against **five per-slot axis-aligned boxes** (centred on each actor's world X/Z, ±6 in X and Z, a
  fixed Y band 70..92, tiled edge-to-edge).

The window tracks three slot indices: **highlighted** (hover/visual, −1 = none), **current** (last
picked), and **committed** (the one actually sent in select/enter messages). Selecting rotates the
chosen preview actor to face front (yaw 0); deselecting snaps it to face back (yaw 180°); non-selected
actors return to idle motion.

### 7.3 Per-slot state byte & the in-flight latch (sub-states)

| Slot-state byte | Meaning |
|---|---|
| **0** | normal / selectable |
| **1** | in-flight / locked (a request pending for that slot) |
| **2** | deleted-this-session |

A window-level **in-flight latch** gates every outbound action so a second click cannot double-send
before the server result arrives; the result handlers clear it. The slot-info-row refresh enables /
disables each slot's button cluster from the slot-state byte (normal 3-state vs disabled/alternate).

Appearance editing during create has its own sub-states: **face index** clamped **1..7** (2D portrait
only — no 3D rebuild); per-stat **spinners** drawing from a shared point budget (max 5 spend, per-stat
floor of 10 for variants 1..4); a **class/appearance-variant selector** over the four classes.

### 7.4 Create / Delete / Enter / Rename

- **Create** — an empty-slot or class button opens the class strip + the create-name modal. The
  create-confirm button (**action 35**) validates the typed name (non-empty, not a reserved/placeholder
  name, passes a banned-word table check, passes a charset validator, minimum length 2), stages name +
  appearance into a create blob, and sends the **create-character** request (`1/6`, a 52-byte appearance
  body — see `character_creation.md`). The in-flight latch blocks duplicate sends. The create-cancel
  button (**action 36**) calls `SelectWindow_ResetScene` and restarts the scene BGM without sending.
- **Enter / Select** — the select-confirm action (gated by the busy guard and the slot-occupied flag)
  sends the **select-character-slot** request (opcode 1/7, via action 54). The actual world entry
  commits once the camera boom/zoom has settled, in this order: (1) stop the scene BGM (id 920100200);
  (2) guard against an `@BLANK@` descriptor name (the empty-slot sentinel — re-opens the create modal
  instead of entering, cf. `character_creation.md`); (3) build the 40-byte enter-game body; (4) read
  the game version from `data/cursor/game.ver`; (5) **send the enter-game request** (opcode `1/9`);
  (6) **then** copy the chosen slot's 880-byte descriptor + 96-byte stats block + level into the
  live-player globals (the copy is POST-send). Beyond those three documented blocks, the commit also
  writes an **additional flag byte** from the NetHandler roster into the live-player globals: the high
  byte of NetHandler roster word 206 is written to a live-player global immediately after the stats
  copy, and a boolean derived from NetHandler field 1237 is written to a derived slot in the live
  MainWindow. The semantic of this flag (likely a PvP/relation or appearance flag seeded for the live
  session) is not yet pinned — flagged as an open item (§9). *([CONFIRMED]* the extra write occurs
  after the `1/9` send, co-located with the descriptor/stats/level copy, static control-flow on build
  263bd994; `scenes/scene_state_machine.md §3.2` and the entry-order summary in §7.8 below list only
  descriptor/stats/level — this addition supersedes that summary for completeness.)*
- **Delete** — implemented as a slot-keyed **"move-out"** request, gated behind a delete-confirm modal
  (requires a valid highlighted slot with an actor present; otherwise an error is shown). A server-side
  **delete cooldown** can defer it.
- **Rename** — the rename-confirm action (action 59) runs banned-word / charset / duplicate checks and
  sends opcode **1/13 RenameCharacter** (18-byte payload: 1-byte target slot from main+6252, then the
  17-byte CP949 name strcpy'd from the +0x17AC textbox). The rename panel is a **SelectWindow child**
  at +0x179C / +0x17AC — not a shared global `NameInputPanel` singleton (§2.2).

### 7.5 Server result feedback & scene refresh

There is **no standalone create-result opcode**. Create success surfaces via the rename-apply path
(3/6) that writes the new name into the slot record, followed by a refreshed `3/1` roster and the
`3/23 SmsgCharStatusBytesByName` status update — there is no dedicated create-ack body.

- **Enter-game ack** (minor **5**, `SmsgEnterGameAck`) — **44 bytes** (40-byte account/billing
  confirm block + trailing 4-byte char-count). This is the **direct reply to C2S 1/9**: it sets the
  client engine GameState to `LOADING` and ends the char-select scene. It is NOT a create-result. See
  `opcodes.md` `0x30005` and `packets/3-5_enter_game_response.yaml`.
- **Rename-char result** (minor **6**, `SmsgRenameCharResult`) — **12 bytes** (result u8 @0,
  error-code u8 @1, pad @0x02, two IEEE-float placement values @0x04/@0x08). On success (result = 1,
  subtype 1), applies the new name to the slot record via the slot writer; on failure, maps the
  error-code to a localized message shown in the char-select status line. Also carries the
  create-success slot-write path (places the new character's name into the first empty slot, writes
  class-specific default-equipment id blocks per class 1..4, bumps the account character count,
  refreshes the slot-count label, and replays the scene BGM). See `opcodes.md` `0x30006`.
- **Char-manage result** (minor **7**, `SmsgCharManageResult`) — **8 bytes** `{result u8 @0,
  reserved @1, subtype u8 @2, reserved @3, ready_time u32 @4}`. Subtype 2 = delete-confirmed: wipes
  the slot (zeroes descriptor + stats, despawns the actor, marks the slot deleted-this-session,
  decrements the account char count); a not-yet-ready delete cooldown formats an **HH:MM** same-day
  "try again later" notice from `ready_time`. See `opcodes.md` `0x30007`.
- All three result handlers end by **resetting the scene** and **clearing the in-flight latch**.

### 7.6 Modals & error/feedback

Modals are GU panels raised with a generic **show-as-modal-and-grab-focus** call. The recovered modals:
**create-name** modal, **class-selection** strip, **rename panel** (SelectWindow child at +0x179C,
action 59 confirm / action 60 cancel with IME blur), **move/relocate overlay** (+0x15B0, actions
62/63), **select-slot confirm panel** (+0x17D0, actions 54/55), the **status-line / tooltip**
banner (`Descriptor`), the **`ErrorPanel`** notice host, and the **`ExitPanel`** quit-confirm
(its own OK/Cancel, actions 50/51, caption msg.xdb id 2007). Errors and feedback go through a
multi-line message-box / status-banner helper that resolves localized message ids (default display
duration **5000 ms**); the per-frame tick also polls two global async-error flags and surfaces their
messages.

UI sounds: a **click sound** (category 2, id 861010101) on most actions; the **scene BGM** (category 0,
id 920100200) started on entry/reset and stopped on leave.

### 7.7 Runtime sequence (start to enter-world)

1. Server sends `3/1`; the inbound dispatcher routes it to the roster handler, which fills the
   five-slot scratch and drives the engine into state 4.
2. The scene state machine constructs `SelectWindow` and runs its build virtual (§4). The per-frame
   tick begins; on its **5th frame** it spawns the scenery actor and performs the first scene reset,
   which spawns the five preview-actor lineup, hides children, and zeroes selection bookkeeping.
3. The slot-info rows refresh per slot (occupied → name/level/world-XZ + normal cluster; locked/
   deleted → disabled/alternate cluster, selected by the slot-state byte).
4. User interaction: hover drives button 3-state visuals; clicking a 2D slot button or the 3D actor
   sets highlighted/current and faces that actor front.
5. Action flows open their modals on demand (create-name + class strip + appearance spinners, rename,
   delete-confirm, select/enter-confirm), each raised via show-modal-and-grab-focus.
6. A confirm action validates locally (name checks for create/rename), sets the in-flight latch, and
   sends the matching C2S message.
7. The server result (`3/6` rename/create-name-apply, or `3/7` manage-result for select/deselect/delete)
   mutates the slot model (place/clear/face), shows any error/cooldown in the status line, resets the
   scene, and clears the latch — returning the UI to step 3.
8. On enter confirm, once the camera boom/zoom settles, the enter-game routine: stops the BGM
   (920100200), guards against `@BLANK@`, builds the 40-byte body, reads `game.ver`, sends `1/9`
   (`SmsgEnterGameAck` 3/5 confirms), then copies the chosen slot descriptor + stats + level **and
   one additional NetHandler flag byte** into the live-player globals (post-send), leaving the scene
   (see §7.4 for the complete copy sequence).

---

## 8. Text & font

### 8.1 Captions come from `msg.xdb`, not inline

Every CharSelect caption is sourced from the message database **`data/script/msg.xdb`** (CP949),
resolved at runtime by **numeric id** through a global message-lookup accessor (returns the cached
string, or an `Id[%d] msg not found.` placeholder on miss). The build virtual contains **no hardcoded
Korean caption literals** — its only inline string literals are texture paths (§5) and a texture key.
Dynamic content (slot counts, character data) is substituted via `_snprintf` into msg.xdb **format**
strings (the templates themselves also come from msg.xdb).

msg.xdb ids consumed by the CharSelect build/init (the **binary stores only the ids**; dump the Korean
text from the on-disk msg.xdb by these ids — hand to the asset/format lane):

**2206, 2209** (a `%d` format), **14001, 14002, 14003..14007** (class-intro captions by appearance
variant), **46001, 46002, 48001, 48003, 48004, 48005, 63030**, plus **2007** (the embedded
`ExitPanel`). Name-error feedback resolves further ids (e.g. 2075 / 12012 / 14001-reserved /
13055 / 13056 / 2206).

### 8.2 Font system — 15-slot D3DX table, charset 129 (HANGUL)

A font-manager singleton owns a **15-slot D3DX font table** (confirmed both by the device-lost
rebuilder's `count = 15` loop and by WinMain's 15 consecutive slot-configurator calls at startup, slot
indices 0..14, immediately after fetching the singleton). Each slot is created with
`D3DXCreateFontA(device, height, width, weight, italic, mipLevels, charset = 129, …, faceName)` —
**charset 129 = HANGUL_CHARSET** on every slot. Faces used across the 15 slots: **"DotumChe"**
(default), **"Dotum"**, **"BatangChe"**; GDI weights 400 (normal) / 700 (bold) / 800 (extra-bold). One
common pixel height is carried in a register rather than a literal — that single value is the only open
font item (§9). The 15 slots are created once at startup, **before** any scene runs, so all slots
exist by the time CharSelect builds. This is the shared front-end font table (see `ui_system.md` and
`login.md` §"message-DB + 15-slot font table").

### 8.3 Text-to-component binding chain

`GULabel` zero-inits its font-slot field at **+0xE4 → default slot 0**; a one-line setter overrides it.
The canonical CharSelect label build is: layout-table position lookup → create `GULabel` →
SetFontSlot(N) → set label color ARGB (`GUComponent +0x0C`; e.g. opaque yellow `0xFFFFFF00`) →
MessageDB_GetString(id) → SetTextAndAlign(label, str, align) → AddChild (paint order = insertion).
Observed alignments: **0 = left, 2 = center**.

Explicit font-slot overrides in CharSelect: **slot 4** (the msg-2206 label, DotumChe weight-800),
**slot 2** (large DotumChe size-0x20 weight-800, paired with the dynamic slot-count refresh). All
other CharSelect labels keep **default slot 0** (small DotumChe). The **slot-count label** is a hybrid:
msg.xdb id 2209 is a `%d` format, `_snprintf` fills the live count, bound centred (align 2), refreshed
both at build and whenever a slot record is written.

---

## 9. Open items (live-confirm if needed)

All are runtime-computed geometry / visibility / values; **none affect 1:1 layout faithfulness**
(the 2D frame art masks non-preview regions, and the per-pixel geometry is centred against
msg.xdb-sized captions). Hand to `re-validator` via the live `?ext=dbg` session (never `dbg_start`):

1. **Pixel-exact `dstX/dstY` and some `w/h`** for buttons centred against msg.xdb-sized captions
   (runtime-computed from screen-dimension globals + the text-width helper).
2. **The single font pixel-height** carried in a register inside the 15-slot font configurator (§8.2).
3. **Whether the `CarrierPigeon*` / `blacksheet` atlases paint on CharSelect** or stay hidden behind
   the children-visibility gate.
4. **The non-array `SelectWindow` member semantics** (the init-only 0 / −1 fields outside the five
   `0x370` sub-records) — count vs handle vs sub-struct — want a `dbg_read` of a live instance (§2.1).
   *(2026-06-24 debugger-session: the named slot-record fields — name@+568, occupancy@+614,
   class@+620, default-equip ≈+656 — are now DEBUGGER-CONFIRMED at stride 880; the remaining init-only
   non-array member semantics stay open. A new GAP was added: confirm `3/1` delivery timing vs
   NetHandler persistence, since the slot records were readable live without the `3/1` handler firing.)*
5. **The semantic of the extra enter-game flag byte** (§7.4) — the high byte of NetHandler roster word
   206 and the boolean from NetHandler field 1237, both written to live-player globals post-send.
   Likely a PvP/relation or appearance flag; the exact meaning is capture/debugger-pending.

Settled hand-offs that are **not** open: msg.xdb id → CP949 string extraction (→ asset/format lane,
§8.1); the per-pixel atlas sub-rect tables (→ `asset_linkages.md`, §5.3); proposed canonical names
(→ names.yaml via `ida-toolsmith`): `SelectWindow_HandleCommand`, `SelectWindow_TickCameraBoomZoom`,
`SelectWindow_TickSelectedPreviewYaw`, `SelectWindow_BuildZoomPreviewActor`,
`SelectWindow_SpawnPreviewLineup`, `Cmsg_RenameCharacter_Send`, `Cmsg_MoveCharacter_Send`,
`Cmsg_SelectCharacterSlot_Send`.

---

## 10. Cross-references

| Topic | Spec |
|---|---|
| GUComponent / GUPanel base offsets + virtual interface | [`structs/gucomponent.md`](../structs/gucomponent.md) |
| GUWindow multiple-inheritance layout + sub-objects (vtable, CmdHandler +0xBC, GView +0xE8, GUTextureList +0x220) | [`structs/guwindow.md`](../structs/guwindow.md) |
| UI subsystem (widget framework, msg.xdb, 15-slot HANGUL fonts, action dispatch) | [`specs/ui_system.md`](../specs/ui_system.md) |
| Front-end scene FSM (§3 character-selection; state-4 placement) | [`specs/frontend_scenes.md`](../specs/frontend_scenes.md) |
| Numeric oracle for front-end geometry | [`specs/frontend_layout_tables.md`](../specs/frontend_layout_tables.md) |
| Create form: 52-byte `1/6` body, point-buy, name checks, `@BLANK@` sentinel | [`specs/character_creation.md`](../specs/character_creation.md) |
| Preview actor skin / skeleton / idle-motion chain | [`specs/skinning.md`](../specs/skinning.md) |
| Preview per-class starter gear / worn-gear visuals | [`specs/equipment_visuals.md`](../specs/equipment_visuals.md) |
| 880-byte per-slot spawn descriptor (parsed from `3/1`) | [`structs/spawn_descriptor.md`](../structs/spawn_descriptor.md) |
| Roster wire spec | [`packets/3-1_character_list.yaml`](../packets/3-1_character_list.yaml) |
| Opcodes (`3/1`, `3/5` SmsgEnterGameAck, `3/6` SmsgRenameCharResult, `3/7` SmsgCharManageResult, `1/7`, `1/9`, `1/13`, `1/14`) | [`opcodes.md`](../opcodes.md) |
| Login scene (state 1) — companion front-end dossier | [`scenes/login.md`](login.md) |
| Cross-scene front-end GUI index | [`scenes/frontend_ui_components.md`](frontend_ui_components.md) |
