---
status: hypothesis
sample_verified: false
---

# Input & UI Tree — Clean-Room Specification

> Neutral, rewritten behavioural specification. No legacy symbols, no addresses,
> no pseudo-code. Describes the *observed behaviour* of the legacy client's input
> dispatch, normalised mouse event, UI→world chain of responsibility, and UI/widget
> tree layout, so the .NET core can be reimplemented from scratch.

## 1. Window-message dispatch (WndProc)

The window procedure handles messages in a fixed priority order. Two singletons are
lazily fetched on first use: a **mouse/input context** and an **IME (input method)
context**.

### 1a. Priority order, before any per-message switch

1. **IME first.** The IME handler is offered *every* incoming message. It is active
   only when a focused text field is registered (a Korean/CJK text input is in
   focus). If the IME consumes the message (returns a non-zero "consumed" flag),
   the window procedure returns immediately and the game never sees that message.
   This lets composition and editing keystrokes be absorbed before gameplay input.
2. **Otherwise**, the per-message switch runs.

### 1b. Per-message handling

| Message | Action |
|---|---|
| Window destroy | post quit message (drives loop shutdown) |
| Window close | swallowed |
| Set-cursor | hide the cursor |
| Key down / up / char range | **Filtered**: TAB, CTRL, ALT, ESC, F4, F10 are swallowed; all other keys fall through to default window handling |
| System command | screensaver, monitor-power, key-menu, move, size, minimize, maximize are swallowed; others fall through to the key filter |
| Mouse move | update mouse position on the input context → consumed |
| Left button down | capture the mouse + emit button-down (button = left) → consumed |
| Left button up | release capture + emit button-up (button = left) → consumed |
| Right button down / up | same as left, button = right |
| Middle button down / up | same as left, button = middle |
| Mouse wheel | emit wheel event with delta → consumed |
| Anything else | default window handling |

Mouse-button capture is taken on press and released on release so drags continue to
be tracked when the cursor leaves the window. Button encoding: `1 = left`,
`2 = right`, `3 = middle`. Double-click is detected by comparing the previous
button and a timestamp against pixel/time tolerances (exact thresholds
**UNVERIFIED** — driven by config values).

### 1c. IME layer detail

The IME handler is engaged only when a focused text-field handle is registered.
It routes:

- Key down: in-field cursor movement (left/right), backspace, delete, and Ctrl+V
  paste.
- IME composition messages: insert composed character into the edit buffer; start,
  update, and end of the candidate window; context and notify forwarding.

Clipboard paste (Ctrl+V) reads CF_TEXT from the clipboard, bounded by the edit
field's capacity (exact capacity field **UNVERIFIED**). The IME is pre-configured
(font, cursor position for the candidate window) when the main window is created.

## 2. Normalised mouse event (20-byte record)

WndProc handlers build a single normalised event and push it into a **ring buffer**;
the main thread dequeues it. The record is **5 × 4 bytes = 20 bytes total**:

| Offset | Size | Type | Role |
|---|---|---|---|
| +0  | 1 | u8  | event type (`3` = move, `5` = button press, `7` = double-click, `8` = wheel) |
| +4  | 4 | i32 | x coordinate (screen pixels) |
| +8  | 4 | i32 | y coordinate (screen pixels) |
| +12 | 4 | i32 | button index **or** wheel delta |
| +16 | 4 | i32 | modifier flags |

> Note: the type byte sits at +0 but the next field begins at +4 — the record is
> dword-aligned, so the total is 20 bytes (not 17). Reimplementers must preserve
> this 4-byte stride if they ever mirror the exact wire/buffer layout, though the
> .NET port uses a managed event type (see §6).

Modifier-flag bit layout (Shift / Ctrl / Alt) is queried from the keyboard state
but the **exact bit mapping is UNVERIFIED** (bit0 / bit1 / bit2 candidates only).

A parallel DirectInput / raw-HID thread also exists alongside the window-message
path; it is noted here only for completeness and is not the primary mouse path.

## 3. UI → World chain of responsibility

Pointer events are dispatched by the main in-game handler, keyed on the event-type
byte (move, left down/up, right down/up, scroll → next-view/tab switch). The core
routing rule for a **left click** is a strict chain of responsibility, **UI first,
world second**:

1. **On release**: if the UI hit-test finds a widget under the cursor, the click is
   consumed (and any drag state is cancelled). Done.
2. **On press**: after clearing transient cursor-state flags and checking the local
   player is alive / not in a special state:
   - **(a) UI hit-test** — walk the widget tree; if a widget is hit, the UI consumes
     the click and world interaction is skipped.
   - **(b) Otherwise, world entity pick** — ray-cast against the scene entity list
     using bounding-sphere tests; if an entity (player / monster / NPC) is hit, run
     selection / interaction logic.
   - **(c) Otherwise, ground action** — click-to-move on the ground or target-cycle.

The **observed priority** is therefore:

1. IME text-field filter (keyboard only, absorbs before message dispatch).
2. **UI panel hit-test** — always checked **before** world interaction.
3. **World entity pick** — bounding-sphere ray-cast against the scene entity list.
4. **Ground move / target-cycle** — only when both UI and entity pick fail.

> Internal ordering caveat: in the press path the entity-pick and UI hit-test calls
> appear interleaved at the code level, but the **effective** rule is "UI consumes
> the click before a ground/target world action is taken" — the UI is the gate.
> The exact interleave of entity-pick vs UI hit-test on press is partially
> **UNVERIFIED**; treat "UI before world" as the contract.

### World entity pick details

The picker finds the frontmost entity under the cursor by ray vs bounding-sphere
tests over the scene entity list, sorting by smallest depth. Each entity carries a
type flag (`1 = player`, `2 = monster`, `3 = NPC`), a bounding-sphere centre and
radius, alive/visibility flags, and special-case flags (e.g. a pickable dead
monster). The ray test returns miss / hit / inside. (Exact entity field offsets are
an implementation detail of the legacy scene and are not required for the .NET
port; the *behaviour* — frontmost pickable entity by depth — is the contract.)

## 4. UI / widget tree

### Hierarchy

A base **component** is specialised into a **panel**, then a **window**, then the
**main window**. The **root is a singleton main window** ("MainMaster"), obtained
through the main handler's singleton accessor. Panels hold child widgets; windows
add command-handling, an auxiliary view, and a texture list.

### Widget (component) field layout — neutral offset table

Offsets in bytes from the widget pointer, as recovered. Byte-granularity flags live
inside the dword region around +0x88.

| Offset | Size | Type | Role |
|---|---|---|---|
| +0x00 | 4 | ptr | vtable pointer |
| +0x04 | 4 | u32 | flags / alpha (init = 255) |
| +0x08 | 4 | u32 | capability flags (bit0 = enabled; a "panel" bit set on panels; a high bit set on windows) |
| +0x0C | 4 | i32 | parent index / id (init = -1) |
| +0x10 | 4 | i32 | init = -1 (role **UNVERIFIED**) |
| +0x14..+0x40 | — | — | child/aux slots, init = 0 |
| +0x88 | 4 | u32 | z-order? (init = 0) |
| +0x88 (byte) | 1 | u8 | **visible** flag (init = 0) |
| +0x8A (byte) | 1 | u8 | flag (init = 1) |
| +0x8B (byte) | 1 | u8 | flag (init = 0) |
| +0x8C (byte) | 1 | u8 | **enabled** flag (init = 1) |
| +0x8D (byte) | 1 | u8 | flag (init = 0) |
| +0x95 (byte) | 1 | u8 | flag (init = 0) |
| +0x98 | 4 | i32 | rect / size low bound (init = 0) |
| +0x9C | 4 | i32 | rect / size high bound (init = 3000) |

The value `enabled == 1` at +0x8C doubles as a "widget is in drag/pressed state"
check during left-click handling. Several individual flag bytes near +0x88 have
**UNVERIFIED** precise roles.

### Panel additions

| Offset | Size | Type | Role |
|---|---|---|---|
| +0xA8 | 4 | ptr | child list pointer (init = 0) |
| +0xAC | 4 | ptr | aux (init = 0) |
| +0xB0 | 4 | ptr | aux (init = 0) |
| +0xB4 | 4 | i32 | init = -1 |

### Window additions

| Offset | Role |
|---|---|
| +0xBC | embedded command handler (own secondary vtable) |
| +0xE8 | auxiliary view data |
| +0x220 | texture list |

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
| +0xA0 | ptr[] | child manipulator pointer array (stride **UNVERIFIED**) |
| +0xB4 | ptr | child localized-name table (string array, stride 28 bytes) |

So there are **5 active view modes** (third-person, first-person, static, gamble,
event), a sixth reserved-and-unused slot, and a single integer **active child
index** selecting the current mode. Per-widget there are independent **enabled** and
**visible** bytes.

## 5. Keyboard focus

There is **no dedicated focus-chain / SetFocus dispatch** at this level. IME (and
hence text editing) is engaged only when a text-input widget registers its window
handle into the IME context's focused-field slot; the call site that does so was
**not traced** (UNVERIFIED). TAB is swallowed by the window procedure and does
**not** cycle focus between widgets. No observable "focused widget pointer" exists
in the main-handler layout (it may live in an untraced service-slot region —
UNVERIFIED).

## 6. Reimplementation note (.NET)

- **InputBus reproduces the chain of responsibility.** The .NET `InputBus` mirrors
  the original rule: **UI consumes the event first; if not consumed, the world
  handles it** (entity pick, then ground/target action). The "UI is the gate"
  contract from §3 is the load-bearing behaviour to preserve.
- **Godot captures raw input and pushes it into the bus.** Godot's input layer
  produces raw pointer/key events and feeds them into the `InputBus`; the bus then
  walks UI → world. Godot owns presentation only and holds no gameplay authority.
- **Normalised event maps to a managed type.** The 20-byte legacy record (§2) maps
  to a managed event value carrying type, x, y, button/delta, and modifiers. The
  numeric type codes (3/5/7/8) are preserved as documentation but the .NET port may
  use a strongly-typed enum rather than the raw byte. No managed string lives on any
  wire/buffer path.
- **View modes.** The 5 active view modes plus the single active-child index map
  cleanly onto a .NET view-mode enum + selector; the reserved sixth slot is dropped.

## 7. UNVERIFIED items

- Exact modifier-flag **bit mapping** (Shift / Ctrl / Alt).
- Double-click **pixel/time tolerances** (config-driven).
- Edit-field **paste capacity** field.
- Roles of several **flag bytes** near widget +0x88, and +0x10's role.
- **Stride** of the child manipulator pointer array in the main window.
- Where the **focused text-field handle** is registered (the call site enabling IME
  routing).
- Exact **interleave** of entity-pick vs UI hit-test on the press path (the
  "UI before world" contract holds regardless).
- Whether a **focused-widget pointer** exists in an untraced service-slot region.
