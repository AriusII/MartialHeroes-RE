---
verification: confirmed
ida_reverified: 2026-06-16
ida_anchor: 263bd994
evidence: [static-ida]
conflicts: per-icon buff payload 8-byte split (needs a sample file); absolute pixel resolution of every screen-relative panel (needs a runtime screen-size read); stat sibling A/B/C immediates + minimap call site + Party↔slot binding not re-pinned this pass
---

# In-Game HUD Layout — Clean-Room Specification

> Neutral, rewritten behavioural spec, promoted from dirty-room analyst notes under **EU Software
> Directive 2009/24/EC Art. 6** (decompilation permitted solely to achieve interoperability).
> **No decompiler output, no pseudo-code, no legacy symbol names, no binary addresses.** Screen
> coordinates and struct/record byte offsets are retained because they are interoperability facts.
>
> **Scope.** The in-game (state 5) HUD panel placement that is statically recovered today, the
> data-driven buff-icon bar, and an explicit list of the HUD panels whose coordinates are **not yet
> extracted**. The widget toolkit itself (GUComponent/GUPanel/GUWindow, draw/hit-test/fonts), the
> full in-game window catalogue, the master-window service-slot table, and the login/select screens
> are owned by `specs/ui_system.md`, `structs/gucomponent.md`, `structs/guwindow.md`,
> `structs/runtime_singletons.md`, and `specs/frontend_scenes.md`. This file is the **placement**
> layer for the in-game HUD; it does not re-derive widget mechanics.
>
> **Reference canvas: 1024 × 768** (top-left origin, +X right, +Y down), per `specs/ui_system.md`.
> All text is **CP949** (Korean). Citation breadcrumb: `// spec: Docs/RE/specs/ui_hud_layout.md`.

---

## 0. The HUD is owned by the master window ("MainMaster")

The in-game HUD is the set of child panels hanging off the single master window ("MainMaster") — the
de-facto window manager (`structs/guwindow.md §3`, `specs/ui_system.md`, `structs/runtime_singletons.md §3.10`).
"MainMaster" is a 1464-byte multiple-inheritance window object: it installs a primary window vtable at
object `+0x00` and a secondary event-handler vtable at `+0xBC`, carries a per-window texture cache at
`+0x220`, and owns a **223-slot service-slot table** running from object `+0x238` to `+0x5B4`. Each
subsystem (inventory, chat, stats, minimap, party, skill, trade, …) is constructed once and its panel
pointer stored into a fixed slot of that table. The HUD is the sum of those slots.

### 0.1 The HUD is built by THREE distinct routines (do not conflate them)

An earlier pass blurred construction into "a single HUD-build routine." The IDB shows three separate
passes, and a 1:1 port needs to respect the boundary between them (which is rect-bearing and which is
not):

| Routine | Role | Builds rects? |
|---|---|---|
| **MainMaster ctor** | Sets the window name `"MainMaster"`, installs the primary `+0x00` and secondary `+0xBC` vtables, creates the base sub-objects (event-handler, command/aux sub-object, the `+0x220` texture cache), then calls the **slot-table zeroer**. It builds **no panels**. | No — vtables + name only |
| **Service-slot zeroer** | Zero-initialises the 223-slot service block (`+0x238`..`+0x5B4`). It **deliberately skips `+0x500`** (slot 178, the main-handler slot) — the zeroing run jumps over that one word. This is slot-table init, **not** the HUD build. | No — only zeroes |
| **HUD-build routine** | The actual in-game (state 5) HUD assembly: a flat sequence of `allocate panel object → call the panel's sized constructor with its rectangle → attach it as a child of MainMaster → store the pointer into a service slot`. It populates **178 distinct slots** — service indices **0..177** (`+0x238`..`+0x4FC`), then **185 (`+0x51C`), 186 (`+0x520`), 218 (`+0x5A0`)**. It does **not** store into `+0x500` (the main-handler slot, slot 178), so that slot is filled by neither the ctor nor the build routine — it is filled lazily later. | **Yes — all panel rectangles** |
| **Per-GameState reconfigure routine** | Runs on each scene-state transition. It re-reads the already-built panels and sets their **label text (CP949, from the message database), font slots, colours, visibility flags, and entry sounds**, and arms HUD timers. It assigns **no rectangles** and constructs no panels. | No — text/sound/flags only |

> **Construction is in the build routine; appearance/text is in the reconfigure routine.** When an
> earlier note said the placements are "passed as arguments at the build call site," that is correct in
> substance — but the call site is the dedicated **HUD-build routine**, not the MainMaster ctor and not
> the per-state reconfigure routine. The ctor builds nothing but vtables, name and zeroed slots; the
> reconfigure routine touches only text/sound/flags on already-built panels. A faithful port is two
> distinct passes: one geometry/construction pass, one per-state appearance pass.

For most panels the position and size are passed as arguments at the build call site — **not** stored in
the panel constructor body. Each panel class exposes a **sized / rect-taking** constructor variant (its
parameterless default ctor carries no coordinates); the sized variant is the real positioning site the
HUD-build routine invokes. The placements for **inventory** and **chat** are documented in §1; the
**stats, minimap, party, trade** placements and the data-driven **skill bar** are documented in §3. The
**complete** in-game HUD panel inventory — every placement site the build routine emits, grouped by
region — is documented in §5.

### 0.2 Window manager vs the master service table

The window **manager** is this MainMaster window and its 223-slot service-slot table (slots from object
`+0x238`). A panel becomes part of the live HUD by being stored into one of those service slots. (A
separate helper that an earlier pass framed as "register window into the manager" is, in other code
paths, an append onto a per-scene **dispose/teardown** linked list — used to tear scene objects down,
not to attach them to the manager. Do not conflate the two: HUD membership = slot-store into the
service table; dispose-list membership = scheduled teardown.) The HUD itself is torn down by a separate
reset routine that clears the slots, tears down the command handler and actor manager, and re-zeroes the
table on leaving the in-game scene.

---

## 1. Statically-recovered panel placement (CODE-CONFIRMED)

These two panels have their size and anchor recovered from the HUD-assembly call site (decoded by
hand from the assembly region). The other in-game panels do not expose their coordinates this way.

### 1.1 Inventory panel (right-anchored)

| Property | Value | Notes |
|---|---|---|
| Width | **318** px | Confirmed literal. The same 318 value is **also** the additive offset to the live screen-width global that computes the panel's X (see Anchor X). The earlier "Width = 732" reading was a transposition of width and height — 732 is the **height** (see below). |
| Height | **732** px | Confirmed literal. |
| Anchor X | **screen_width + 318** | **Right-anchored** — the X is the live screen-width global plus the 318-px width, i.e. the panel hugs the right edge regardless of resolution. (On the 1024-wide reference canvas this resolves relative to the right edge; the 318 is an additive screen-width-relative offset, **not** an absolute X.) |
| Anchor Y | **0** | Top-flush. (There is no separate "secondary 318" Y value; the only 318 at the site is the width, which is also added to screen-width for X. The earlier "secondary 318 as a Y-like value" reading was an artifact of mis-reading the argument list.) |

Confidence: **CONFIRMED** (decoded from the HUD-assembly call site; the slot-arg convention of §3.1).
The defining facts for a reimplementation are **W = 318, H = 732, X = screen_width + 318, Y = 0**. This
now agrees with §5.3, which already listed the inventory column as **318 × 732**.

### 1.2 Chat — two objects: the output host frame + the input/edit child

The chat HUD is **two separate panels**, not one. An earlier pass documented only the 290 × 18 edit
strip; the IDB shows it is the **child** of a larger chat-output host frame that is built immediately
before it. Both are recovered below.

**Chat output host frame (the parent — newly recovered):**

| Property | Value | Notes |
|---|---|---|
| Width | **268** px | Confirmed literal. |
| Height | **462** px | Confirmed literal. |
| Anchor X | **screen_width / 2 − 512** | Centred-ish (half-screen minus 512). |
| Anchor Y | **screen_height − 324** | Bottom-anchored (screen-height-relative). |

**Chat input / edit child (the 290 × 18 strip — anchored to the host above):**

| Property | Value | Notes |
|---|---|---|
| Width | **290** px | Confirmed literal. |
| Height | **18** px | Confirmed literal — the per-line / edit-box height. |
| Anchor X | **(chat-output host field) + 94** | Computed relative to the chat-output host frame's stored geometry, plus a **94-px** inset. |
| Anchor Y | **(chat-output host field)** | Taken from the chat-output host frame's geometry field. |

Confidence: **CONFIRMED** (decoded from the build call sites). The edit child is **anchored to the chat
output host frame** (its parent), not placed at absolute coordinates: width **290**, height **18**, X/Y
derived from the host's stored geometry plus a **+94** inset. The host frame (268 × 462) supplies those
geometry fields and is itself the bottom-anchored chat window.

> **Reconciliation with `specs/ui_system.md`.** That spec notes the chat window is the only in-game
> window whose **initial** size/position can come from `data/script/uiconfig.lua`
> (`CHAT_WINDOW_POS_X/Y/SIZE/FONT_SIZE`). The 290×18 / parent+94 values here are the **hardcoded
> default** placement used when those config keys are not overriding; the config keys, when present,
> reposition the same panel. No conflict.

---

## 2. Buff-icon bar — data-driven from `buff_icon_position.xdb`

The on-screen buff/state icon bar is **not** laid out by hardcoded coordinates. Each buff icon's
screen position is looked up at draw time from a data file.

### 2.1 The data file and its record (CODE-CONFIRMED layout; payload-field split MEDIUM)

- **File:** `data/script/buff_icon_position.xdb` (in the VFS).
- **Record stride:** **12 bytes**.
- **Record count source:** `file_size / 12` (no header, no magic).
- **Record layout:**

| Offset | Size | Type | Field | Notes |
|-------:|-----:|------|-------|-------|
| +0x00 | 4 | u32 | `key` | The icon / buff identifier (the lookup key). |
| +0x04 | 8 | bytes | `position_payload` | Per-icon screen-position data (slot / x / y). The exact int-vs-float split of these 8 bytes is **not yet confirmed** — needs a real sample file. |

### 2.2 Load and lookup chain (CODE-CONFIRMED)

```
data/script/buff_icon_position.xdb
  → loader: count = filesize/12; bulk-read; insert each 12-byte record into a red-black tree keyed on `key`
  → tree lookup accessor(key) → returns the record's position payload (or 0 if absent)
  → in-game actor-state buff-icon draw loops → per-icon screen position
```

The loader runs from the bulk asset-loader thread at startup. The lookup accessor has many call
sites concentrated in the actor-state / buff-icon draw loops; this firmly establishes the table as a
**HUD / actor-state buff-icon-position input**. A teardown path clears the tree at engine shutdown.

> **Runtime buff state.** Buff-slot state itself arrives over the network (the buff-slot update push,
> `4/102`, already catalogued in `opcodes.md`). That push feeds the buff-slot state which the draw
> loops then position via the `buff_icon_position.xdb` lookup. The wire shape is owned by
> `opcodes.md` / `packets`; this spec owns only the icon-placement data path.

### 2.3 C3 de-misnomer note — distinct from `actor_size.xdb` (CODE-CONFIRMED)

This file (`buff_icon_position.xdb`) is **distinct from `actor_size.xdb`**, even though both happen
to use a 12-byte `u32 key + 8 payload-bytes` record shape. They have **different semantics and
different destination tables**:

- `buff_icon_position.xdb` → the buff-icon-position tree (the 8 payload bytes are **position / slot**
  data). This is the file the loader documented above consumes.
- `actor_size.xdb` → a separate consumer; for it the 8 payload bytes would be size data (e.g. width /
  height). It is loaded by a different, indirect path (no direct reference to a loader; likely driven
  from a script-bound filename table) and is **not** the buff bar's input.

An earlier analysis pass mislabelled the buff-icon loader as an "actor size" loader. The
de-misnomer is **CODE-CONFIRMED** here: the loader consumes the `buff_icon_position.xdb` filename.
**Do not** carry over any "float width/height" payload interpretation from the `actor_size` reading
to the buff-icon payload — the buff-icon payload is position/slot data.

> The `actor_size.xdb` consumer is a separate follow-up (a data-table / script-binding lane); it is
> not part of the HUD-layout contract.

---

## 3. Core HUD panel placement — stats, minimap, party, trade, skill bar (CODE-CONFIRMED-static)

The five panels that earlier passes flagged as "coordinates pending" are now recovered. They are all
positioned inside the single in-game HUD-build routine (the skill bar via a dedicated sub-builder it
hands off to). Their **sized / rect-taking** constructor variants receive the placement immediates
listed below.

### 3.1 The panel rect-slot argument convention (CONFIRMED)

Every fixed-rect panel is built by the same idiom: allocate the panel object; if the allocation
succeeded, call the panel's **sized constructor** with the rectangle; store the resulting pointer into a
MainMaster service slot via a null-guarded slot-store helper; then attach it (a show/attach virtual, or
the panel-AddChild helper). After the object pointer, the sized constructor's argument slots are, in
order:

```
(texture, X, Y, W, H, innerX, innerY, flag, -1)
```

where the trailing **`-1` is a fixed sentinel present at every site**. These arguments forward unchanged
down the common base chain (panel constructor → panel base wrapper → base component). The base
component stores the rectangle into these object fields:

| Field offset | Stored value |
|---|---|
| `+0x14` | `left = X` |
| `+0x18` | `top = Y` |
| `+0x1C` | `width = W` |
| `+0x20` | `height = H` |
| `+0x24`, `+0x34` | `innerX` |
| `+0x28`, `+0x38` | `innerY` |
| `+0x3C` | `right = innerX + W` |
| `+0x40` | `bottom = innerY + H` |
| `+0x90` | `texture` handle |
| `+0x9C` | default depth `z = 3000` |

(This matches the base-component geometry contract in `structs/gucomponent.md`: `+0x1C` = width,
`+0x20` = height, with `+0x24`/`+0x28` the position-relative inner origins. There is no rect-bearing
ctor on the base component itself — the geometry above is applied through the sized panel constructor /
builders.)

**Three-state buttons** use the same head of the argument list and then take three extra frame
arguments — the normal/hover(over)/pressed(down) state textures — baked at construction.

This slot mapping is **CONFIRMED**, triangulated against two independent in-routine ground truths:
the inventory panel's **318 × 732** right-edge column (§1.1) and the chat panel's **290 × 18** edit
strip (§1.2). All values below are in pixels, decoded from immediates.

> **Child registration & event binding.** A child is attached to its parent by the panel-AddChild
> helper, which writes the parent pointer into the child's `+0x84` field and pushes the child into the
> parent's child vector. **Paint order = child insertion order** (children are drawn in the order they
> were added). A second variant, AddChild-with-action, does the same and *additionally* writes an
> **action / command id into the child's `+0x10` field** — this is how an element binds the event it
> raises when clicked. In the HUD build, 21 children are attached with an explicit action id; the rest
> are attached passively (visual only). This corroborates the UI event model in `specs/ui_system.md`.

### 3.2 Anchor conventions

There are **two live screen-size globals** — `screen_width` and `screen_height` — that the engine adds
into the X/Y expressions of anchored panels. They are confirmed as the inputs to every right/edge,
bottom and centred placement below; their runtime values (and hence the resolved absolute pixels of
every screen-relative panel) are the one item that is genuinely **capture/debugger-pending** (it needs a
runtime read of the screen-size globals at a known resolution — see §3.3, §5.11).

- **Absolute** — plain literal X/Y (no screen-size term in the expression).
- **Screen-width-relative** — `X = screen_width ± offset`. With `+ panelWidth` the panel pins to the
  inner-right column; with `− panelWidth` it pins flush to the right edge.
- **Screen-height-relative** — `Y = screen_height − offset` (bottom-anchored).
- **Parent-relative** — X/Y read from an already-built panel's stored rectangle field (e.g. trade
  reads the inventory panel's stored X).

### 3.3 Recovered placements

| Panel (class) | X | Y | W | H | Anchor | Confidence |
|---|---|---|---|---|---|---|
| **Stats / character-state (ActorStatePanel)** | **180** | **95** | **130** | **196** | Absolute (plain literals; pinned in the upper-left area) | CONFIRMED |
| **Minimap (total-map panel)** | **screen_width − 135** | **0** | **135** | **195** | Screen-width-relative, top-flush — **top-right corner** | static-hypothesis (X-formula idiom present; exact call site + 135/195 immediates not re-pinned this pass) |
| **Party (right-dock party window family)** | **screen_width + 318** | **0** | **318** | **732** | Screen-width-relative (right column) — same X-formula and 318 × 732 size as inventory; differs only in Y = 0 | CONFIRMED-formula for the right-dock family; the specific Party↔slot binding is **not** re-pinned (see note) |
| **Trade (TradePanel)** | **= inventory panel's stored X** | **0** | **318** | **732** | Parent-relative — reads the inventory panel's stored X and overlays the same 318 × 732 right column | CONFIRMED |
| **Skill BAR** (data-driven container) | **container origin 349** | **13** | data-driven | data-driven | Absolute container origin + runtime-laid-out child slots — see §3.5 | container origin CONFIRMED; per-slot data-driven |

> **Pixel-resolution caveat.** The minimap and party X are **formulas** (`screen_width − 135` and
> `screen_width + 318`). Their absolute pixel X depends on the runtime screen-size global and is
> therefore **capture/debugger-pending**: a runtime read of the screen-width global at a known
> resolution would resolve these to fixed pixels. On the 1024-wide reference canvas, treat them as
> right-edge-relative offsets, not absolute literals.

> **Party↔slot binding (residual, low stakes).** The `screen_width + 318` / 318 × 732 right-dock
> *family* is confirmed (it is the inventory column geometry, see §5.3); but the specific HUD widget an
> earlier pass labelled "PartyPanel" resolves to a **mini-party** HUD widget at its own slot, distinct
> from the full 318 × 732 right-dock party window. The right-column formula and size are firm; the exact
> Party-window↔service-slot binding is **not** nailed this pass and is a follow-up.

> **Skill BAR vs skill BOOK — do not confuse them.** The data-driven container at **(349, 13)** in this
> row is the player **skill hot-bar** (§3.5). It is a **different** object from the **skill-book**
> window (the parked 964 × 655 panel listed in §5.2), which is a centred/parked spell-book window, not
> the bar. See the §5.2 note correcting the earlier "off-screen parked panel" mislabel.

### 3.4 Stats sub-panels (four absolute sibling panels)

The stats group is built as a family of absolute panels in the same routine. In addition to the
primary ActorState panel at (180, 95, 130, 196), three sibling stat sub-panels are positioned with
plain literal coordinates:

| Sub-panel | X | Y | W | H | Anchor | Confidence |
|---|---|---|---|---|---|---|
| ActorState (primary) | 180 | 95 | 130 | 196 | Absolute | CONFIRMED (re-decoded directly) |
| Stat sub-panel A | 50 | 95 | 130 | 196 | Absolute | static-hypothesis (sibling ctors wrap the ActorState ctor; immediates not re-decoded this pass) |
| Stat sub-panel B | 287 | 14 | 130 | 231 | Absolute | static-hypothesis (as above) |
| Stat sub-panel C | 50 | 147 | 130 | 196 | Absolute | static-hypothesis (as above) |

The primary ActorState panel's rectangle (180, 95, 130, 196) is **CONFIRMED** (re-decoded directly from
its sized constructor at its slot). The three sibling stat sub-panels are built nearby by ctors that
wrap the same ActorState constructor; their immediates were **not** re-decoded this pass, so they remain
**static-hypothesis** pending a re-decode at their adjacent sites. The roles of the three sibling
sub-panels (e.g. which is HP/MP vs stamina vs status) are not yet individually labelled — see Known
unknowns.

### 3.5 Skill bar — data-driven (no single static rect)

> This is the player skill **hot-bar** — distinct from the skill-**book** window (the 964 × 655 parked
> panel in §5.2). The bar is the data-driven container described here; the book is a centred spell-book
> window.

The player skill bar is **not** a single fixed rectangle. A dedicated builder (reached from the
in-game HUD hand-off and from several network / respawn paths) creates a thin **container / anchor
component** at absolute origin **(349, 13)** with a tiny anchor extent (**width 7, height 504** — an
anchor strip, not the visible bar). It then runs a loop over **nine skill slots**,
and each slot is laid out by a per-slot builder that reads a **runtime skill-slot registry** (a
global linked list of slot records). Each slot's **base X/Y come from its registry record**, and the
builder composes that slot's sub-widgets (icon, frame, count text, cooldown overlays, key-label,
status icons) from **fixed pixel offsets added to the per-slot base**.

Several layout variants are selected by a slot **kind** field, so the per-slot base position is data,
not a constant. Observed icon-cell sizes across the layout branches are **146 × 49**, **297 × 50**,
and **58 × 58**; a three-button cluster sits at base offsets **763 / 792 / 821** px from the slot
base.

**Model for the Godot UI:** a container anchored at **(349, 13)** holding a **data-driven grid of
nine slots**, each slot's absolute position resolved at runtime from the slot registry plus the fixed
per-widget pixel offsets — **not** a single static rectangle. If a static fallback is needed for a
first render, use the icon-cell sizes above. The skill-slot registry record layout (per-slot base
X/Y, kind, sub-widget flags) is a struct worth a separate struct-cartographer pass if the Godot skill
bar must match 1:1 (see Known unknowns).

### 3.6 Scope note — superseded by §5

An earlier pass noted the full HUD-build routine positions 40-or-more panels and that only the five
core panels had been swept. That full sweep is now complete: §5 documents the in-game HUD panel
inventory organised by region (~152 distinct placement sites; see the count caveat below). The five
core panels above (§3.3) remain the anchored ground truth that the slot-argument convention (§3.1) was
triangulated against; §5 is the additive full inventory built on that same convention.

> **Count caveat (honest).** The build routine populates **178 distinct service slots** (service
> indices 0..177 plus 185, 186, 218), which is the slot-population count confirmed by a separate element
> walk. The "~152 placement sites" of §5 below is the geometric sweep of *literal-rect* sites and is
> close but **not** exhaustively reconciled against the 178-slot population (some slots are degenerate
> 0 × 0 logical containers, some are data-driven composites, and a few sites straddle lane seams). Treat
> 178 as the confirmed slot count and ~152 as the confirmed literal-rect-site count; the "223" figure is
> the **size of the whole service-slot table** (`+0x238`..`+0x5B4`), of which ~178 are populated by this
> routine and the rest are filled lazily or unused.

---

## 5. Full HUD panel inventory (complete sweep, by region)

This section is the in-game HUD layout sweep: the placement sites emitted by the HUD-build routine
(§0.1), grouped by screen region. **~152 distinct literal-rect placement sites** were recovered across
the whole routine (see the §3.6 count caveat — the routine populates 178 service slots in total, of
which the literal-rect sites are ~152). All values are decimal pixels; `screen_width` / `screen_height`
denote the two live screen-size globals; `centerX(W) = (screen_width − W) / 2` and
`centerY(H) = (screen_height − H) / 2` are the two engine centring helpers (§5.1). The five core panels
of §3.3 (inventory, chat, stats, minimap, party, trade, skill) are referenced inline where they belong
to a region and marked **[§3]** — they are **not** re-counted here; §5 adds the ~140 net-new panels
around them.

Every placement site uses the same construction idiom and slot-argument convention already defined in
§3.1: `(texture, X, Y, W, H, innerX, innerY, flag, -1)`. Section §5 only adds the **coordinate
inventory**; the convention itself is unchanged.

### 5.1 The four (+ derived) anchor conventions and their distribution (CONFIRMED)

§3.2 named four anchor conventions; the full sweep confirms them and surfaces a small number of mixed
and composite variants. The four primary conventions, in plain terms:

1. **Absolute** — plain literal X/Y, no screen-size term.
2. **Screen-centred (both axes)** — `X = centerX(W)` **and** `Y = centerY(H)`. This is a
   screen-centred modal/dialog and is the **overwhelmingly dominant** HUD idiom.
3. **Screen-width-relative** — `X = screen_width ± offset` (or `W = screen_width`). Right/edge-anchored.
4. **Screen-height-relative** — `Y = screen_height − offset` (or `H = screen_height`). Bottom-anchored.

Two further patterns appear in practice:

- **Parent-relative** — X/Y read from an already-built panel's stored rectangle field (chat children,
  trade reading inventory's X, the status strip, the target frame).
- **Data-driven / composite** — no single static rectangle: a container origin plus a loop that
  adds child widgets from a runtime registry (§5.10).

**Distribution across the whole routine (after seam dedupe):**

| Convention | Approx. count | Where it lands |
|---|---:|---|
| Screen-centred (both axes) | ~81 | Almost every modal / dialog (§5.8). The dominant HUD population. |
| Absolute (literal rect, incl. origin-(0,0)-placed) | ~33 | World-frame layer, stats cluster, assorted literal panels, plus ~13 degenerate 0×0 logical containers. |
| Screen-width-relative | ~14 | Right-dock 318-column windows, `screen_width − k` buttons/gauges, full-width bars. |
| Parent-relative | ~10 | Chat children, status strip, target frame, right-dock siblings reading inventory's rect. |
| Mixed (centred-X / absolute-Y, or width+height dual) | ~8 | Tab strips, bottom action bar, chat host frame, corner mini-bar. |
| Screen-height-relative (pure) | ~3 | Bottom action bar, bottom-left panel, corner mini-bar Y. |
| Full-window backdrop (`W = screen_width`, `H = screen_height`) | 1 | A single full-screen overlay. |
| Data-driven / composite | 3 | Skill bar, message/notice icon-strip, buff/status icon-strip. |

The convention helpers and the two screen-size globals are **CONFIRMED-static** (triangulated and
re-read at the two lane seams during the sweep). All screen-relative panels are
**CONFIRMED-formula**; their absolute pixel resolution is **pending a known-resolution read** of the
screen-size globals (§5.11).

### 5.2 Root / world-frame layer (top-left & viewport)

| Panel role | X | Y | W | H | Anchor | Conf |
|---|---|---|---|---|---|---|
| Root image badge / hotspot sprite (first element built; slot idx173) | 0 | 0 | 180 | 80 | Absolute (forced-alpha byte set to 0xFF) | High |
| Top-left status strip / clock (label, child of the root badge) | 6 | 5 | (root W) | 20 | Parent-relative (child of root) | Med |
| Centre viewport / scene frame | 40 | 140 | 288 | 288 | Absolute | High |
| Top-left textured badge | 0 | 0 | 285 | 88 | Absolute | High |
| Top-left textured panel | 0 | 0 | 220 | 200 | Absolute | High |
| **Skill-BOOK window (parked, slide-in)** | 43 | −655 | 964 | 655 | Absolute (negative Y; parked one screen-height above the viewport, shown later) | High |
| Top-left corner mini-icon (handle) | −15 | −15 | 23 | 23 | Absolute (negative, off-canvas) | High |

> **Label correction — the parked 964 × 655 panel is the SKILL-BOOK window, not an anonymous panel.**
> An earlier pass called the (43, −655, 964, 655) site an "off-screen parked panel (slide-in)". It is in
> fact the **skill-book** window's own construction (its sized ctor, stored into its dedicated service
> slot). The rectangle is correct; the identity was the unknown. This is the **book** window — it is
> **not** the data-driven skill **bar** at (349, 13) of §3.5. The negative Y parks it one screen-height
> above the viewport so it can slide in when opened.

> **Root badge + the first three logical containers (B11 element walk, CONFIRMED).** The very first
> element the build routine emits is the **root image badge** above (slot idx173, rect (0,0,180,80),
> with the component's forced-alpha byte set to 0xFF). The top-left status/clock label (6, 5, …, 20) is
> then attached as a **child** of that root badge (parent-relative). Immediately after, the routine
> builds the **first three service slots as degenerate 0 × 0 self-sizing logical containers**, each
> attached to MainMaster: slot 0 = the **chat panel container** (the visible 290 × 18 edit strip and its
> parent+94 anchor of §1.2 come from this panel's own deferred layout, **not** from this all-zero
> construction call), slot 1 = the **drop-item panel container**, slot 2 = the **3D-gauge / actor-view
> panel container**. These three are the lead examples of the "~13 all-zero self-sizing containers" of
> §5.10.

### 5.3 Right-dock column (the 318-wide right rail; inventory family)

A stack of windows sharing the inventory column's geometry: width **318**, height **732** (one
**697**-tall variant), top-flush at Y = 0. Some sites carry the explicit `screen_width + 318`
formula; others read the inventory panel's already-stored X (parent-relative). The two are equivalent
placements of the same right rail. Named members of this family in the IDB include the inventory panel,
the trade panel, the quest panel, and a guild panel (the 697-tall variant).

| Panel role | X | Y | W | H | Anchor | Conf |
|---|---|---|---|---|---|---|
| Inventory item panel (InventoryPanel) **[§3]** | screen_width + 318 | 0 | 318 | 732 | Screen-width-rel | High |
| Trade panel (TradePanel) **[§3]** | (inventory stored X) | 0 | 318 | 732 | Parent-relative | High |
| Party window (right-dock party family) **[§3]** | screen_width + 318 | 0 | 318 | 732 | Screen-width-rel | High |
| Quest panel (QuestPanel) | screen_width + 318 | 0 | 318 | 732 | Screen-width-rel | High |
| Right-dock windows ×~4 (parent-relative) | (inventory stored X) | 0 | 318 | 732 | Parent-relative | High |
| Right-dock window (shorter variant — GuildA panel) | screen_width + 318 | 0 | 318 | **697** | Screen-width-rel | High |
| Right-dock windows ×~4 (explicit formula) | screen_width + 318 | 0 | 318 | 732 | Screen-width-rel | High |
| Right-dock landscape window | screen_width + 318 | 0 | 697 | 318 | Screen-width-rel | High |
| Wide right-region panel | screen_width/2 + 318 | 0 | 732 | 318 | Screen-width-rel | High |
| Wide right-region panel #2 | screen_width/2 + 318 | 0 | 408 | 318 | Screen-width-rel | High |

> **Shorter-variant height correction.** An earlier draft listed the shorter right-dock variant as
> **709**; the re-decoded adjacent site (the guild panel) is **697**. The verified short-variant height
> is **697**. Source texture names that appear at this routine's build sites include
> `data/ui/InventWindow.dds` (the inventory window) and `data/ui/chunrihojung.dds` (the right-edge gauge
> of §5.6).

### 5.4 Top bars / corners (minimap, status bars, top-right buttons)

| Panel role | X | Y | W | H | Anchor | Conf |
|---|---|---|---|---|---|---|
| Minimap (total-map panel) **[§3]** | screen_width − 135 | 0 | 135 | 195 | Screen-width-rel (top-right corner) | Med (static-hypothesis — the `screen_width − k` top-right idiom is present, but the exact call site + 135/195 immediates were not re-pinned this pass) |
| Top full-width status bar | 0 | 120 | screen_width | 20 | Screen-width-rel (spans full width) | High |
| Wide thin top bar | centerX(226) | 0 | 226 | 54 | Centred-X, top | High |
| Right-edge square button #1 | screen_width − 200 | 0 | 64 | 64 | Screen-width-rel | High |
| Right-edge square button #2 | screen_width − 200 | 0 | 64 | 64 | Screen-width-rel (toggled-state pair with #1) | High |
| Right-anchored panel | screen_width − 406 | 0 | 406 | 119 | Screen-width-rel (content 618×468; texture idx 1) | High |

### 5.5 Stats / actor-state cluster (top-left HUD area)

The four absolute stat panels of §3.4 belong here. One additional derived-position frame:

| Panel role | X | Y | W | H | Anchor | Conf |
|---|---|---|---|---|---|---|
| Stats / actor-state (primary) **[§3]** | 180 | 95 | 130 | 196 | Absolute | High |
| Stats sub-panel A **[§3]** | 50 | 95 | 130 | 196 | Absolute | High |
| Stats sub-panel B **[§3]** | 287 | 14 | 130 | 231 | Absolute | High |
| Stats sub-panel C **[§3]** | 50 | 147 | 130 | 196 | Absolute | High |
| Target / close-up actor frame | (inventory W field)/2 + 606 | 400 | 227 | 227 | Parent-relative (derived; texture idx 3) | Med |

The `/2` term in the target-frame X (in-panel centring vs half-screen shortcut) is not disambiguated
statically — see §5.12.

### 5.6 Right-edge stacked gauge (HP/MP-style composite)

Two short gauge strips stacked at the right edge form one widget. Source texture `chunrihojung.dds`.

| Panel role | X | Y | W | H | Anchor | Conf |
|---|---|---|---|---|---|---|
| Gauge strip A (top row) | screen_width − 135 | 200 | 140 | 35 | Screen-width-rel | High |
| Gauge strip B (bottom row, Y + 50) | screen_width − 135 | 250 | 140 | 35 | Screen-width-rel | High |
| **Stacked-gauge composite** | screen_width − 135 | 200 | 140 | 2 rows × 35 | Screen-width-rel composite | High |

Treat the two rows as a single composite gauge widget (top row at Y = 200, second row at Y = 250).

### 5.7 Bottom action / command area

| Panel role | X | Y | W | H | Anchor | Conf |
|---|---|---|---|---|---|---|
| Bottom action / command bar | centerX(1024) | screen_height − 60 | 1024 | 60 | Mixed (centred-X + bottom; innerY = 957) | High |
| Bottom-left panel | 10 | screen_height − 220 | 215 | 147 | Screen-height-rel (X = 10 absolute) | High |
| Corner / edge mini-bar | screen_width − 146 | screen_height − (runtime panel H) − 70 | 136 | 60 | Dual width+height (Y runtime-dependent, §5.12) | High (literals) / Med (abs Y) |
| Player skill bar **[§3]** | container origin (349, 13) | — | — | — | Data-driven (nine-slot loop) — see §3.5 / §5.10 | High |

### 5.8 Screen-centred modal / dialog families (the dominant population)

The largest HUD population is the screen-centred modals: each is built at `X = centerX(W)`,
`Y = centerY(H)`. Rather than ~81 near-identical rows, the recurring **families** are listed by size,
with the count of distinct construction sites (often a paired-tab or per-mode duplicate of the same
rectangle). The centring formula is always `center = (screen − size) / 2` on both axes.

| Family / role | W | H | Sites | Texture / flag note |
|---|---:|---:|---:|---|
| Skill / spell-book window | 362 | 212 | 2 | paired tabs |
| Skill / spell-book (inner pad) | 362 | 280 | 1 | innerX = 20, innerY = 22 |
| **Confirm / info dialog family** | 340 | 190 | ~12 | tex idx 2; the most common dialog rect, recurs widely |
| **Quest / log family** | 442 | 280 | ~6 | tex idx 9 (NPC-quest / log) |
| Dialog family | 360 | 280 | ~4 | tex idx 8 |
| Small textured panels | 167 | 163–246 | ~6 | tex idx 2 region; innerX/innerY vary (243/203/163/239/246 heights) |
| Small panel | 114 | 146 | 1 | flag = 1 |
| Textured panel | 91 | 285 | 1 | tex idx 1; innerX = 620, innerY = 709 |
| Textured panel | 215 | 204 | 1 | tex idx 1; innerX = 186, innerY = 810 |
| Textured panel | 215 | 403 | 1 | tex idx 1; innerX = 405, innerY = 62 |
| Textured panel quad | 306 | 503 | 4 | tex idx 9; innerY = 320 (four identical rects) |
| Panel | 447 | 190 | 1 | flag = 1 |
| Windows | 321×352 / 618×309 | — | 2 | one each |
| Windows | 318×233 / 274×233 / 228×337 | — | 3 | one each |
| Paired-tab windows | 375 | 481–483 | 2 | 375×481 + 375×483 |
| Panel | 208 | 243 | 1 | tex idx 77 |
| Panel | 233 | 274 | 1 | flag = 1 (reconciled to the A/B lane seam, §5.9) |
| Panel | 285 | 243 | 1 | flag = 1 |
| Panel | 140 | 156 | 1 | flag = 1 |
| Panel | 322 | 160 | 1 | flag = 1 |
| Panel | 452 | 298 | 1 | flag = 1 |
| Large panel | 798 | 592 | 1 | flag = 1 |
| Panel | 250 | 411 | 1 | flag = 1 |
| Panel | 293 | 233 | 1 | flag = 1; inner +250 offset |
| Textured panel | 243 | 288 | 1 | tex idx 8 (content 472×660) |
| Panel | 448 | 274 | 1 | flag = 1 |
| Panel | 444 | 201 | 1 | flag = 1 |
| Panels | 472×480 / 390×560 / 595×512 | — | 3 | flag = 0, one each |
| Panel | 395 | 443 | 1 | flag = 0; inner 375/483 — Med conf (§5.12) |
| Panel | 329 | 422 | 1 | flag = 1 |
| Large window | 651 | 423 | 1 | innerX = 6, innerY = 19 |
| Centred-with-offset panel | 317 | 551 | 1 | placed at (screen_width/2 − 158, screen_height/2 − 275) — explicit offset from centre |

All rows in this table are **screen-centred (both axes)** unless a row's note says otherwise (the
final row is an explicit centre-offset variant). Confidence: **High** on the size literals;
**CONFIRMED-formula** on the centring; absolute pixels pending a known-resolution read (§5.11).

### 5.9 Centred-X / absolute-Y strips & tabs, and assorted absolute panels

**Centred-X / absolute-Y (mixed):**

| Panel role | X | Y | W | H | Conf |
|---|---|---|---|---|---|
| Tab / strip | centerX(454) | 100 | 454 | 452 | High |
| Tab / strip #1 | centerX(274) | 50 | 274 | 448 | Med |
| Tab / strip #2 | centerX(274) | 0 | 274 | 448 | Med |
| Wide centred list | centerX(389) | 50 | 389 | 520 | High (innerX = 370) |

**Assorted absolute literal-rect panels:**

| Panel role | X | Y | W | H | Conf |
|---|---|---|---|---|---|
| Centred dialog (literal) | 80 | 200 | 295 | 393 | High |
| Centred window (literal) | 80 | 200 | 228 | 337 | High (innerX = 692) |
| Tall narrow strip (left edge) | 0 | 105 | 40 | 311 | High |
| Near-fullscreen board | 113 | 30 | 798 | 655 | High (innerY = 12) |
| Mid panel | 455 | 136 | 277 | (inner) | Med (H slot ambiguous, §5.12) |
| Absolute panel | 295 | 372 | 120 | 100 | High |
| Narrow strip (textured) | 207 | 80 | 535 | 40 | High (tex idx 1, inner 12) |
| Absolute panel | 120 | 455 | 85 | 11 | High |
| Absolute panel | 466 | 191 | 189 | 304 | High |
| Origin panel | 0 | 0 | 195 | 140 | High |
| Origin panels ×2 | 0 | 0 | 252 | 440 | High (duplicate rect) |
| Origin panel | 0 | 0 | 156 | 140 | High |
| Absolute panel | 0 | 520 | 499 | 632 | High |
| Small marker widget | 836 | 370 | 10 | 10 | Med |
| Small marker widgets ×3 | 0 | 0 | 10 | 10–20 | Med |

**Reconciliation (seam dedupe).** The sweep was partitioned into three address-range lanes that met
at two seam panels; both seams were re-read directly to remove duplicates. The 233×274 centred panel
(§5.8) and the 340×190 centred dialog (§5.8) each straddled a lane boundary and were each counted
**once**. These two dedupes are why the net distinct-site total is **152** rather than the raw
per-lane sum.

### 5.10 Full-window backdrop, logical containers, and data-driven composites

**Full-window backdrop & degenerate containers:**

| Panel role | X | Y | W | H | Anchor | Conf |
|---|---|---|---|---|---|---|
| Full-screen backdrop | 0 | 0 | screen_width | screen_height | Full-window (both axes) | High |
| Self-sizing containers ×~13 | 0 | 0 | 0 | 0 | Absolute (degenerate) — logical group/manager nodes | High |

The ~13 all-zero constructors are **not drawn rectangles**: they are logical group / manager nodes
whose real extents come from later layout calls outside this routine. The first three of them are the
**chat panel container (slot 0), drop-item panel container (slot 1), and 3D-gauge / actor-view panel
container (slot 2)** — see the §5.2 B11 note. One of them sets a child-count field of **110**.

**Data-driven / composite widgets (no single static rect):**

- **Player skill bar [§3]** — container origin **(349, 13)**; nine-slot loop over a runtime slot
  registry; per-slot base from the registry record plus fixed per-widget pixel offsets; icon-cell
  variants **146×49 / 297×50 / 58×58**; three-button cluster at slot-base **+763 / +792 / +821**.
  Full mechanics in §3.5.
- **Right-edge message / notice icon-strip** — the container is a texture strip; child glyphs sit on
  a **20-px-pitch** grid (X offsets **15 / 35 / 55 / 75**, Y ≈ 8, each **12×12**) plus header glyphs
  at **Y = 35**. Each child's Y is read from the container's stored height field → **parent-relative
  composite**.
- **Final status / buff icon-strip** — the container sits at base origin **X = 545** and is populated by
  three-state-button cell children. The B11 element walk pinned the first two cells: one of **W = 276,
  H = 91, innerX = 32** at the container base, then a sibling cell of **W = 92, H = 91, innerX = 32** at
  **Y = 92** (the per-child Y step). Both are stored in the high HUD slots (idx 218 onward). Each child's
  Y is offset down from the container base in roughly 20-px cells. **Parent-relative composite.** (This
  is the placement container for the buff-icon bar whose per-icon lookup is the
  `buff_icon_position.xdb` data path of §2.)

### 5.11 Pixel-resolution status (CONFIRMED-formula vs pixel-pending)

Every panel positioned with a screen-size term — all **screen-width-relative**,
**screen-height-relative**, **screen-centred**, full-window-backdrop, and the centred strips/tabs —
is **CONFIRMED as a formula** (the arithmetic in terms of `screen_width` / `screen_height` and the
two centring helpers is recovered with High confidence). Their **absolute pixel** resolution is
**pending a known-resolution read**: reading the two screen-size globals at a known resolution
(a debugger pass, out of scope for this static sweep) would resolve every screen-relative panel from
**CONFIRMED-formula** to **CONFIRMED-pixels** in one step. On the 1024-wide reference canvas, treat
these as edge/centre-relative offsets, not absolute literals.

The **absolute** literal-rect panels (§5.2, §5.5 stats, §5.9 assorted) are already
**CODE-CONFIRMED-static** in pixels — they carry no screen-size term.

### 5.12 Partial / runtime-dependent items (honest follow-ups)

A small number of sites are firm in their anchor classification but carry a residual ambiguity:

1. **Corner / edge mini-bar Y** (136×60, §5.7) — `Y = screen_height − (runtime panel-height field) − 70`.
   The literals are solid; the absolute Y depends on a source panel's runtime height (High on
   literals, Med on absolute Y).
2. **Target / close-up frame X** (§5.5) — `(inventory W field)/2 + 606`; the `/2` (in-panel centring
   vs half-screen shortcut) is not disambiguated statically. Low stakes.
3. **Tinted notice / popup** — the centring inputs (536, 327) differ from the built size (300, 200),
   so the panel is deliberately offset from true centre; its non-sentinel final argument is a packed
   constant (a tint colour or a style id — semantics unresolved). A window-manager / struct follow-up.
4. **Inner-W-vs-content ambiguities** on a few sites (the 455/136/277 mid panel; the 293×233 +250
   inner; the 395×443 inner 375/483): the X/Y anchor classification is firm; the precise inner-W
   assignment is Med confidence.
5. **Self-sizing 0×0 containers (~13, §5.10)** — logical group/manager nodes, not drawn rects; their
   real extents come from later layout calls outside this routine.
6. **Skill-slot registry record layout** (§3.5) — a struct worth a separate struct-cartographer pass
   for a 1:1 skill bar.

No site in the sweep is fully undecodable; the items above are the ~6 partial / runtime-dependent
residuals out of the 152 sites.

---

## 4. Known unknowns

- The 8-byte `position_payload` field split in `buff_icon_position.xdb` (int16 pair vs int32 pair vs
  float) — needs a real sample file **(capture/sample-pending)**.
- **Absolute pixel resolution for every screen-relative panel** — the minimap (`screen_width − 135`),
  party (`screen_width + 318`), and all screen-centred / screen-height-relative / full-width-bar
  panels of §5 have **confirmed formulas** but their resolved pixel values depend on the runtime
  screen-size globals; **capture/debugger-pending** — needs a read of the two screen-size globals at a
  known resolution (§3.3, §5.11).
- The **minimap call site** (§3.3 / §5.4) was not re-pinned this pass — the `screen_width − 135` /
  135 × 195 top-right idiom is present, but the exact site and immediates need a confirming decode
  (static-hypothesis).
- The **Party-window↔service-slot binding** (§3.3) — the `screen_width + 318` / 318 × 732 right-dock
  family is confirmed, but which specific service slot is the full party window (vs the distinct
  mini-party widget) is not nailed.
- The **stat sibling sub-panels A/B/C** immediates (§3.4) — only the primary ActorState rect was
  re-decoded this pass; the three siblings (whose ctors wrap the ActorState ctor) are static-hypothesis
  pending a re-decode, and their per-sub-panel semantic roles (HP/MP vs stamina vs status) are not yet
  assigned.
- The **skill-slot registry record layout** (per-slot base X/Y, kind, sub-widget flags) that drives
  the nine-slot skill bar (§3.5) — a struct-cartographer follow-up if 1:1 fidelity is required.
- A **full slot → panel-class → rect harvest** of the build routine would turn the §5 "anon panel"
  rows into a named slot map: the IDB carries explicit panel-class constructors at most sites (trade,
  PC-trade, keep/storage, stall-keeper, delivery/mail, item-repair, item-transform, upgrade, emoticon,
  default/context-menu, and a family of NPC-dialog panels — generic / repair / quest / keep / guild /
  confession). A struct-cartographer / `ui_system.md` follow-up.
- The **~6 partial / runtime-dependent §5 items** (§5.12): corner mini-bar runtime Y, target-frame
  `/2` semantics, tinted-popup packed-flag semantics, a few inner-W-vs-content assignments, the ~13
  self-sizing 0×0 logical containers, and the skill-slot registry struct.

## Cross-references

- Widget toolkit + in-game window contents + master-window service slots: `specs/ui_system.md`
- Base widget structs: `structs/gucomponent.md`, `structs/guwindow.md`
- Master window object + service-slot table: `structs/runtime_singletons.md §3.10`
- Buff-slot network push (`4/102`): `opcodes.md`, `specs/handlers.md`
- Actor-state struct (buff/state fields the draw loops read): `structs/actor.md`
- Login / character-select front-end: `specs/frontend_scenes.md`
- Glossary: `Docs/RE/names.yaml`
- Provenance: `Docs/RE/journal.md`
