---
verification: confirmed
ida_reverified: 2026-06-24
ida_anchor: 263bd994
evidence: [static-ida]
conflicts: Ctrl-vs-Shift identity of modifier slots 1012/1013, and the exact call site that registers a text field's HWND into the IME focused-field slot, remain capture/debugger-pending. — 2026-06-20 CYCLE 7 (IDB SHA 263bd994): added §3d behavioural note on the widget on-event dispatch (vtable slot 6) + hit-test slots (4 = vector/point-in, 5 = bool) and the click→action→click-cue path; struct vtable slot numbers are owned by `structs/gucomponent.md`. — 2026-06-24 (IDB SHA 263bd994): §4 widget table corrected — +0x0C = packed tint word (not "parent id"), +0x10 = action_id (not "UNVERIFIED"), +0x08 capability bits downgraded from [static-hypothesis] to CODE-CONFIRMED; all three now agree with `structs/gucomponent.md`.
---

# Input & UI Tree — Clean-Room Specification

> Neutral, rewritten behavioural specification. No legacy symbols, no addresses,
> no pseudo-code. Describes the *observed behaviour* of the legacy client's input
> dispatch, normalised event records, UI→world chain of responsibility, and UI/widget
> tree layout, so the .NET core can be reimplemented from scratch.

## 0. Two input sources (read this first)

Input enters the client through **two separate threads**, both feeding the **same
cross-thread ring buffer**:

- **Mouse** events are produced by the **window procedure (WndProc)** on the main
  message thread, in response to Win32 mouse messages.
- **Keyboard** events are produced by a dedicated **DirectInput8 keyboard thread**
  (`__noreturn`): it creates a DirectInput8 interface (version `0x800`), opens the
  **keyboard** device with a 256-byte buffered data format, waits on an event, reads
  buffered device data, and emits keyboard events. The DirectInput device is a
  *keyboard*, not a mouse — raw HID here is the keyboard path.

> Correction vs prior doc: an earlier version described DirectInput as a secondary,
> non-primary *mouse* path. That is **inverted** — DirectInput8 is the **primary
> keyboard** path, and the mouse comes entirely from WndProc.

Both threads build a small fixed event record (§2) and hand it to the ring-buffer
push helper, which enqueues under a **critical section**; the main thread drains the
queue. The critical section is the keyboard-thread ↔ main-thread handoff.

## 1. Window-message dispatch (WndProc)

The window procedure handles messages in a fixed priority order. Two singletons are
lazily fetched on first use, each gated by an init-flag bit: a **mouse/input
context** (flag bit0) and an **IME (input method) context** (flag bit1).

### 1a. Priority order, before any per-message switch

1. **IME first.** The IME handler is offered *every* incoming message. It is active
   only when a focused text field is registered (a Korean/CJK text input is in
   focus). If the IME consumes the message (returns a non-zero "consumed" flag),
   the window procedure returns immediately and the game never sees that message.
   This lets composition and editing keystrokes be absorbed before gameplay input.
   (The IME handler's return is a one-byte "consumed" flag; treat any non-zero as
   consumed.)
2. **Otherwise**, the per-message switch runs.

### 1b. Per-message handling

| Message | Action |
|---|---|
| Window destroy | post quit message (drives loop shutdown) |
| Window close | swallowed (return 0) |
| Set-cursor | hide the cursor (set cursor to null, return 1) |
| System key down / up / char (`0x104`–`0x106`) | **Filtered**: TAB, CTRL, ALT, ESC, F4, F10 (virtual-key codes 9, 17, 18, 27, 115, 121) are swallowed; all other keys fall through to default window handling |
| System command (`0x112`) | screensaver, monitor-power, key-menu, move, size, minimize, maximize (`0xF000`, `0xF010`, `0xF030`, `0xF100`, `0xF130`, `0xF140`, `0xF170`) are swallowed; others fall through to the key filter |
| Mouse move (`0x200`) | update mouse position on the input context → emit **type 3** → consumed |
| Left button down (`0x201`) | capture the mouse + emit button-down (button = left) → consumed |
| Left button up (`0x202`) | release capture + emit button-up (button = left) → consumed |
| Right button down / up (`0x204` / `0x205`) | same as left, button = right |
| Middle button down / up (`0x207` / `0x208`) | same as left, button = middle |
| Mouse wheel (`0x20A`) | emit wheel event with delta → consumed |
| Anything else | default window handling |

**Per-message refinement (corrected):** the in-WndProc key filter targets the
**system-key messages** (`WM_SYSKEYDOWN` / `WM_SYSKEYUP` / `WM_SYSCHAR`, `0x104`–`0x106`)
and the `WM_SYSCOMMAND` fall-through. **Plain key messages (`WM_KEYDOWN`, `0x100`,
etc.) are NOT handled by WndProc at all** — they go to default window handling, and
the *real* keyboard event path is the DirectInput8 thread (§0). So the WndProc only
swallows the menu/system accelerators; gameplay keystrokes never travel through it.

Mouse-button capture is taken on press and released on release so drags continue to
be tracked when the cursor leaves the window. **Button encoding: `1 = left`,
`2 = right`, `3 = middle`.** The button index is computed from the message id and
stored both as the per-button state index and into the record's `+12` field; every
consumer keys off `record[+12] == 1` for left, etc.

Double-click is detected (in the button emitter) by comparing the previous button id
and a timestamp against pixel/time tolerances. **Tolerances recovered:** time = `300`
milliseconds, distance = `2` pixels (same button, within both → a **type 7**
double-click; the double-click latch is then reset by writing `-1` into the record's
modifier slot).

### 1c. IME layer detail

The IME handler is engaged only when a focused text-field handle is registered.
It is gated on the IME context's **focused-HWND slot** being non-null **and** the
**IME-enabled byte** being set. It routes:

- Key down (`WM_KEYDOWN` and an IME-keydown message id): in-field cursor movement
  (left `0x25` / right `0x27`), backspace (`0x08`), delete (`0x2E`), and Ctrl+V
  paste (`V` = `0x56` / `0x76` with Ctrl held).
- IME composition messages: insert composed character (`WM_CHAR`, `0x102`) into the
  edit buffer; start / update / end of the candidate window (`0x10D` / `0x10E` /
  `0x10F`); and language-change / notify / result / keydown forwarding (message ids
  `81`, `641`, `642`, `656`).

Clipboard paste (Ctrl+V) reads `CF_TEXT` from the clipboard, **bounded by the edit
field's capacity**. The IME context holds the focus/edit state at fixed offsets
(see the IME edit-field offset table in §4). The IME is pre-configured (font, cursor
position for the candidate window) when the main window is created.

## 2. Normalised event records (two shapes, type byte at +0)

WndProc / DirectInput handlers build a normalised event and push it into the
**cross-thread ring buffer**; the main thread dequeues it. There are **two record
shapes**, both leading with the **type byte at `+0`**:

### Mouse record — 20 bytes (`5 × 4`)

| Offset | Size | Type | Role |
|---|---|---|---|
| +0  | 1 | u8  | event type (see taxonomy below); record is dword-aligned, so the next field starts at +4 |
| +4  | 4 | i32 | x coordinate (screen pixels) — **also the wheel-delta field for type 8** |
| +8  | 4 | i32 | y coordinate (screen pixels) — `0` for wheel |
| +12 | 4 | i32 | button index (`1`/`2`/`3`) for move/press/release/click/double-click — `0` for wheel |
| +16 | 4 | i32 | modifier flags (and the double-click reset writes `-1` here) |

### Keyboard record — 16 bytes (`4 × 4`)

| Offset | Size | Type | Role |
|---|---|---|---|
| +0  | 1 | u8  | event type (`1` = key down, `2` = key up) |
| +4  | 4 | i32 | virtual-key code |
| +8  | 4 | i32 | translated character (VK→char via shift/Caps/Ctrl/Alt state and a shift table) |
| +12 | 4 | i32 | modifier flags |

> **Correction vs prior doc:** the previous spec described only the 20-byte record
> and put the **wheel delta at +12**. The wheel delta is actually stored at **+4 (the
> x field)**, with +8 and +12 zeroed. There are also **two** record shapes — the
> 16-byte keyboard record was missing entirely. Reimplementers mirroring the exact
> buffer layout must preserve both shapes and the 4-byte stride; the .NET port uses a
> managed event type (see §6).

### 2a. The complete event-type taxonomy (the type byte at `+0`)

The earlier doc listed only `{3, 5, 7, 8}` and mislabelled `5` as "button press".
That is **wrong/incomplete**. The full, recovered taxonomy is:

| Type | Meaning | Source | Record | Notes |
|---|---|---|---|---|
| **1** | key DOWN | DirectInput8 keyboard thread | 16-byte keyboard | pressed → 1 |
| **2** | key UP | DirectInput8 keyboard thread | 16-byte keyboard | release → 2 |
| **3** | mouse MOVE | WndProc (`0x200`) | 20-byte mouse | only emitted when the position actually changed |
| **4** | mouse button PRESS | WndProc (button-down) | 20-byte mouse | `+12` = button `1`/`2`/`3` |
| **5** | mouse button RELEASE | WndProc (button-up) | 20-byte mouse | the type the prior doc wrongly called "press" |
| **6** | CLICK (activation) — **synthesised** | base widget handler (§2b) | 20-byte mouse | re-pushed on a release that lands on the press-captured widget; the click-vs-drag discriminator |
| **7** | DOUBLE-CLICK | WndProc (button emitter) | 20-byte mouse | within `300` ms & `2` px of the previous same-button press; resets the latch (`+16 = -1`) |
| **8** | mouse WHEEL | WndProc (`0x20A`) | 20-byte mouse | **delta at `+4`**, `+8`/`+12` = `0`; the wheel word carries the notch in its high half and key-state in its low half |

Types **1, 2, 6** were entirely absent from the prior doc; **4** (press) was missing;
and **5** was mislabelled "press" (it is release).

### 2b. Click-vs-drag — the type-6 synthesis (the discriminator)

The base widget event handler implements the click-vs-drag rule using a **single
global drag-capture pointer** (the "pressed/drag-capture widget"):

- **On type 4 (press):** if the widget under the press is pressable (a per-widget
  pressable byte is set), record it as the global drag-capture target.
- **On type 5 (release):** **only if** the captured widget is still this same widget
  (i.e. the release landed on the widget that was pressed), clear the global
  drag-capture pointer **and synthesise a type-6 event** (copying the release's
  x/y/button/modifiers) which is pushed back into the ring buffer.

So a **type-6 ("click") fires only for a press-then-release on the same widget**. A
release that drifted off the originally-pressed widget produces **no type-6** — that
was a *drag*, not a click. The drag-capture pointer is **global** (one capture at a
time). The panel/tree dispatcher also clears it defensively on any stray type-5
release.

**Consequence for the HUD:** every HUD command/button handler **acts on type 6**
(it checks `record[+0] == 6 && record[+12] == 1` for a left-button activation),
using **type 4** only for press-feedback and **type 3** for hover/tooltip. Reproducing
this is load-bearing: a button "fires" on the synthesised click, not on the raw
press or release.

### 2c. Modifier-flag encoding (recovered bit positions)

The modifier flags field is built identically in every emitter from the
input-context key-state query:

- **bit3 (`0x8`)** = key slot `1014` (internal VK `0x10E`) — confirmed **Alt**
  (Alt+digit selects party quick-slots).
- **bit2 (`0x4`)** = key slot `1012` (internal VK `0x10C`).
- **bit1 (`0x2`)** = key slot `1013` (internal VK `0x10D`).
- **bit0 (`0x1`)** = keyboard auto-repeat flag (keyboard path only).

The exact **Ctrl-vs-Shift identity** of slots `1012`/`1013` is not yet disambiguated
(the world left-press chain uses slot `1013` as the entity-attack-pick modifier and
slot `1012` as the ground-special modifier). The **bit positions are confirmed**; the
Ctrl/Shift *labels* are capture/debugger-pending.

### 2d. Keyboard VK→char translation

The keyboard emitter translates the virtual-key code to a character using CapsLock /
Shift / Ctrl / Alt state and a shift table, and zeroes the function keys (F1..F12)
under the appropriate lock conditions before populating the translated-char field of
the 16-byte record.

## 3. UI → World chain of responsibility

Pointer events are dispatched **UI-first, world-second** through a chain of
responsibility. The root window's tree dispatcher runs **before** the 3D world view;
the world view is itself just another child node in the tree, reached only if no UI
panel consumed the event.

### 3a. Tree dispatch (the gate)

The window tree dispatcher:

1. If the widget is **disabled** (its panel-active gate byte at `+0x8C` is zero) →
   returns "not consumed" immediately.
2. For **pointer types `{3, 4, 5, 6, 7}`**: it walks its children **in reverse
   (topmost-first)**, calling each child's **hit-test** then its **event handler**;
   the **first child that hit-tests AND consumes wins** (returns consumed). Moves
   (type 3) are an exception — they keep propagating to all children rather than
   stopping at the first consumer. The id of the selected child is latched.
3. For **keyboard types `{1, 2}`**: it walks children **forward** and offers the
   event **without** a positional hit-test.

This is the chain-of-responsibility / **first-consumer-wins** rule, and it is what
makes "**UI is the gate**": the 3D world view's event handler runs only when no panel
consumed the pointer event.

### 3b. World view dispatch

The in-game (world) view event handler switches on the type byte. For a **left
press** it runs a strict gate-then-pick chain:

1. **Guard:** clear transient cursor-state flags, and confirm enough panels are
   closed and the local player is alive / not in a special state, before *any* world
   action.
2. **(a) world entity pick** — ray-cast against the scene entity list using
   bounding-sphere tests (with a pickable-mode mask); if an entity (player /
   monster / NPC) is hit, run selection / interaction logic.
3. **(b) ground action** — terrain ray pick for click-to-move (or target-cycle)
   when no entity was picked.

The **entity-vs-ground** choice within the world handler is **modifier-gated**: the
attack/select modifier (key slot `1013`) selects the entity-pick branch versus the
plain ground-move branch.

On a **type-5 release**, the dispatcher clears the global drag-capture pointer (drag
state is cancelled) — consistent with the type-6 click synthesis in §2b.

The **observed overall priority** is therefore:

1. **IME text-field filter** (keyboard only; absorbs before WndProc message dispatch).
2. **UI panel hit-test** — the tree dispatcher, always checked **before** world
   interaction (first-consumer-wins, topmost-first).
3. **World entity pick** — bounding-sphere ray-cast against the scene entity list.
4. **Ground move / target-cycle** — only when both UI and entity pick fail.

> The **mouse wheel (type 8)** has **no case** in the world view handler — wheel is
> consumed by UI / camera elsewhere, not by the world click chain. [static-hypothesis]
> as to which exact consumer handles the wheel.

### 3c. World entity pick details

The picker finds the frontmost entity under the cursor by ray vs bounding-sphere
tests over the scene entity list, sorting by smallest depth. Each entity carries a
type flag (`1 = player`, `2 = monster`, `3 = NPC`), a bounding-sphere centre and
radius, alive/visibility flags, and special-case flags (e.g. a pickable dead
monster). A secondary NPC pick handles non-combat interaction targets. The ray test
returns miss / hit / inside. (Exact entity field offsets are an implementation detail
of the legacy scene; the *behaviour* — frontmost pickable entity by depth — is the
contract.) [static-hypothesis] for the precise per-entity offsets.

### 3d. Widget-level dispatch — on-event + hit-test (behavioural, CODE-CONFIRMED)

At the per-widget level the tree dispatcher of §3a reaches each widget through a small fixed set of
its vtable methods. Stated behaviourally (the exact vtable **slot numbers** are owned by
`structs/gucomponent.md`; do not re-derive the slot table here):

- **Hit-test (two forms).** Each widget exposes two hit-test entries the dispatcher calls before
  offering a pointer event: a **vector / point-in form** and a plain **bool form**. The rectangular
  rule is the §4 AABB test (`rectX ≤ x < rectX + w && rectY ≤ y < rectY + h`); on a hit the widget
  sets its under-cursor byte and hover-latch and fires the mouse-enter / mouse-leave edges. A button's
  hit-test additionally short-circuits to "no hit" when its disabled byte is set.

- **On-event dispatch.** Each widget has a single **on-event handler** (the per-class event method the
  dispatcher invokes once a child hit-tests). For a panel/window this handler walks its children
  (the chain-of-responsibility of §3a); for a leaf button it toggles the pressed byte on press and,
  on the synthesised type-6 click (§2b), **performs the widget's bound action** — it reads the
  element's action / command id (set by the add-child-with-action helper) and routes it to the owning
  window's action sink.

- **Click → action → cue.** The common path for a button activation is therefore: press (type 4) sets
  the pressed feedback → release on the same widget synthesises a click (type 6) → the on-event
  handler runs the action and **commonly plays a UI click cue** (an audio feedback the front-end and
  HUD both use on button activation). A release that drifts off the pressed widget produces no click
  and no cue (it was a drag, §2b).

This is the behavioural companion to §3a's "UI is the gate": the gate decides *which* widget gets the
event; the on-event handler decides *what that widget does* with it (act + cue), keying off the
type-6 synthesised click for activation.

## 4. UI / widget tree

### Hierarchy

A base **component** is specialised into a **panel**, then a **window**, then the
**main window**. The **root is a singleton main window** ("MainMaster"), obtained
through a Meyers singleton accessor reached from everywhere. Panels hold child
widgets; windows add command-handling, an auxiliary view, and a texture list.

### Widget (component) field layout — neutral offset table (corrected)

Offsets in bytes from the widget pointer. The flag bytes around `+0x88` are
byte-granular and were re-confirmed by the hit-test and the per-handler gate checks.

| Offset | Size | Type | Role |
|---|---|---|---|
| +0x00 | 4 | ptr | vtable pointer |
| +0x04 | 4 | u32 | alpha (init = 255) |
| +0x08 | 4 | u32 | **capability flags** — bit 0 = "is a component" (GUComponent ctor); bit 2 = "is a panel" (GUPanel ctor); bit 3 = "is a button" (GUButton ctor); bit 7 = "is a label" (GULabel ctor); bit 9 = "is a list" (GUList ctor); bit 10 = 0x0400 = "is a textbox" (GUTextbox ctor); bit 13 = "is a window" (GUWindow ctor). **CODE-CONFIRMED** — see `structs/gucomponent.md §4`. |
| +0x0C | 4 | u32 | **packed tint / forced-alpha word** — low 24 bits = RGB tint; top byte = forced-alpha override (0xFF = disabled). Default **0xFFFFFFFF**. **(Corrected: the earlier "parent index / id" label is wrong — CODE-CONFIRMED, `structs/gucomponent.md §2`.)** |
| +0x10 | 4 | i32 | **`action_id`** — the per-widget action identifier; default **−1** sentinel (no action). Set by `AddChildWithAction`; read by the panel router to identify the consuming child. **(Corrected: the earlier "UNVERIFIED" label is wrong — CODE-CONFIRMED, `structs/gucomponent.md §2`.)** |
| +0x14 | 4 | i32 | local x [from GUComponent geometry layout] |
| +0x18 | 4 | i32 | local y |
| +0x1C | 4 | i32 | **WIDTH** (also the hit-rect width `w`) |
| +0x20 | 4 | i32 | **HEIGHT** (also the hit-rect height `h`) |
| +0x24 | 4 | i32 | posX |
| +0x28 | 4 | i32 | posY |
| +0x2C | 4 | i32 | **computed world x** (the hit-rect left edge `rectX`) |
| +0x30 | 4 | i32 | **computed world y** (the hit-rect top edge `rectY`) |
| +0x88 (byte) | 1 | u8 | **hit / under-cursor result** byte — written by the hit-test (init = 0) |
| +0x89 (byte) | 1 | u8 | **hover-latch** byte — set/cleared on enter/leave (init = 0) |
| +0x8A (byte) | 1 | u8 | **pressable** byte — gates the type-4 drag-capture record (init = 1) |
| +0x8C (byte) | 1 | u8 | **panel-active / enabled** gate — every handler tests this; the tree dispatch returns early if zero (init = 1) |
| +0x8D (byte) | 1 | u8 | flag (init = 0) |
| +0x95 (byte) | 1 | u8 | flag (init = 0) |
| +0x98 | 4 | i32 | clamp range low bound (init = 0) |
| +0x9C | 4 | i32 | clamp range high bound (init = 3000) |

> **Corrections vs prior doc:**
> - The geometry was transposed in the prior doc. The GUComponent layout is
>   **`+0x1C` = WIDTH, `+0x20` = HEIGHT, `+0x24` = posX, `+0x28` = posY**, with
>   `+0x14`/`+0x18` = local x/y and `+0x2C`/`+0x30` = computed world x/y.
> - `+0x88` is **NOT** the "visible flag" — it is the **hit/under-cursor result**
>   byte written by the hit-test. `+0x89` is the **hover-latch**. The flag every
>   handler actually tests for "panel is open/active" is **`+0x8C`** (which the prior
>   doc separately, and correctly, called "enabled").
> - `+0x98`/`+0x9C` are a separate clamp range, **not** the live hit rect. The live
>   hit rect is `(rectX = +0x2C, rectY = +0x30, w = +0x1C, h = +0x20)`.
> - **(2026-06-24)** `+0x0C` is the **packed tint / forced-alpha word** (low 24 bits = RGB tint,
>   top byte = forced-alpha), **not** a "parent index / id". `+0x10` is the **action_id** (default
>   −1), **not** "UNVERIFIED". Both are now CODE-CONFIRMED; see `structs/gucomponent.md §2`. The
>   `+0x08` capability-flag bit layout is also CODE-CONFIRMED (not a static hypothesis); see
>   `structs/gucomponent.md §4` for the full per-class bit table.

The hit-test rule (rectangular) is: a point `(x, y)` is inside iff
`rectX ≤ x < rectX + w  &&  rectY ≤ y < rectY + h` (half-open on the high edges). On a
hit it sets the under-cursor byte `+0x88` and the hover-latch `+0x89`, and fires the
mouse-enter handler on entering / the mouse-leave handler on leaving. A button's
hit-test first checks a **button-disabled byte at `+0xF8`** and returns "no hit" when
disabled, otherwise delegates to the rectangular test.

> There is **no sized constructor** — the GUComponent has only a default zero-init
> ctor; geometry is applied afterwards via setters/builders (consistent with the
> code-baked UI-layout doctrine; there is no on-disk UI layout manifest).

### Panel additions

| Offset | Size | Type | Role |
|---|---|---|---|
| +0xA8 | 4 | ptr | child list pointer (init = 0) |
| +0xAC | 4 | ptr | aux (init = 0) |
| +0xB0 | 4 | ptr | aux (init = 0) |
| +0xB4 | 4 | i32 | selected-child id latch / init = -1 |

### Window additions

| Offset | Role |
|---|---|
| +0xBC | embedded command handler (own secondary vtable) |
| +0xE8 | auxiliary view data |
| +0x220 | texture list |

### IME edit-field offset table (recovered)

Offsets into the **IME context** object (the singleton fetched in §1):

| Offset | Role |
|---|---|
| +0x1DC (`+476`) | IME-enabled byte (gate) |
| +0x1E0 (`+480`) | active edit-field object pointer |
| +0x284 (`+644`) | focused HWND (the focused text-field handle) |

Offsets into the **active edit-field object** (the pointer at IME `+0x1E0`):

| Offset | Role |
|---|---|
| +0xA4 (`+164`) | character-validity table |
| +0xD0 (`+208`) | paste / max-length capacity (bounds the `CF_TEXT` paste) |

Two helpers toggle IME enable/disable (one sets the enabled byte, one blurs the
input field and disables the IME).

### Main window / handler layout

The main window holds an array of **view platforms** indexed by an **active child
index**, plus the per-view camera manipulators and scene-graph nodes.

| Offset | Type | Role |
|---|---|---|
| +0x28 | ptr | view platform [0] — third-person view |
| +0x2C | ptr | view platform [1] — first-person view |
| +0x30 | ptr | view platform [2] — static view |
| +0x34 | ptr | view platform [3] — gamble view |
| +0x38 | ptr | view platform [4] — event view |
| +0x3C | ptr | view platform [5] — reserved (never assigned) |
| +0x40 | i32 | **active child index** (current view mode) |
| +0x44 | ptr | current view pointer |
| +0x60 | ptr | scene node |
| +0x64 | ptr | scene-graph switch node (active child = active view index) |
| +0x68 | i32 | switch active-child mirror |
| +0x6C | ptr | perspective camera (FOV 65°, near 5, far 15000) |
| +0x70 | ptr | third-person camera manipulator |
| +0x74 | ptr | first-person camera manipulator |
| +0x7C | ptr | static camera manipulator |
| +0x80 | ptr | event camera manipulator |
| +0x84 | ptr | gamble camera manipulator |
| +0xA0 | ptr[] | child manipulator pointer array (stride UNVERIFIED) |
| +0xB4 | ptr | child localized-name table (string array, stride 28 bytes) |

So there are **5 active view modes** (third-person, first-person, static, gamble,
event), a sixth reserved-and-unused slot, and a single integer **active child index**
selecting the current mode. The world view dispatcher gates active-view-dependent
behaviour against this index. Per-widget there are independent flag bytes near
`+0x88` (hit / hover / pressable / active) as tabulated above.

## 5. Keyboard focus

There is **no dedicated focus-chain / SetFocus dispatch** at this level. For
**keyboard** events the tree dispatcher walks children forward and offers the event
without a positional hit-test (§3a); there is no focused-widget pointer in the
dispatch path. TAB (virtual-key `9`) is swallowed by the window procedure and does
**not** cycle focus between widgets.

IME (and hence text editing) is engaged only when a text-input widget registers its
window handle into the IME context's **focused-HWND slot (`+0x284`)** with the
**enabled byte (`+0x1DC`)** set and an active edit object at **`+0x1E0`**. The exact
per-field call site that performs this registration is **capture/debugger-pending**
(the enable/disable *helpers* are identified, but the field-level register call site
is not pinned).

## 6. Reimplementation note (.NET)

- **InputBus reproduces the chain of responsibility.** The .NET `InputBus` mirrors
  the original rule: **UI consumes the event first; if not consumed, the world
  handles it** (entity pick, then ground/target action). The "UI is the gate"
  contract (§3) — topmost child first, first-consumer-wins, world view as just another
  child — is the load-bearing behaviour to preserve.
- **Reproduce the type-6 click semantics.** HUD buttons must fire on the
  **synthesised click (type 6)**, not on raw press (4) or release (5). The
  click-vs-drag rule (release must land on the press-captured widget) is what
  distinguishes a click from a drag; reproduce the single global drag-capture and the
  same-widget check.
- **Two record shapes / full taxonomy.** The 16-byte keyboard record and the 20-byte
  mouse record (§2) map to managed event values carrying type, the relevant
  coordinates / VK / character, button-or-delta, and modifiers. The numeric type codes
  (`1`–`8`) are preserved as documentation; the .NET port may use a strongly-typed
  enum rather than the raw byte. Remember the **wheel delta is in the x field**, not
  the button field. No managed string lives on any wire/buffer path.
- **Keyboard via DirectInput-equivalent.** Mirror that keyboard input is a distinct
  source from mouse (§0); the modifier flags follow the recovered bit layout (§2c).
- **View modes.** The 5 active view modes plus the single active-child index map
  cleanly onto a .NET view-mode enum + selector; the reserved sixth slot is dropped.

## 7. Residual capture/debugger-pending items

These are genuinely runtime-dependent or not control-flow-pinnable from static
analysis; everything else in this spec is control-flow confirmed against the IDB:

- **Ctrl-vs-Shift identity** of modifier slots `1012`/`1013` (bit positions are
  confirmed; only the Ctrl/Shift labels are pending).
- The exact **call site that registers a text field's HWND** into the IME
  focused-field slot (the enable/disable helpers are known; the per-field register
  call is not pinned).
- Which exact consumer handles the **mouse wheel (type 8)** (UI/camera, not the world
  click chain).
- The precise **per-entity field offsets** in the world entity-pick record.
- The **stride** of the child-manipulator pointer array in the main window.
  (Widget +0x10 = `action_id` and +0x08 capability-bit layout are now CODE-CONFIRMED — see §4 and
  `structs/gucomponent.md §2 / §4`.)
