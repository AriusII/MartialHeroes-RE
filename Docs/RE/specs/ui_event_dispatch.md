---
verification: confirmed — CYCLE 14 re-anchor (f61f66a9): 1 fact re-confirmed SAME (press/release click-synth FSM; single global click-capture pointer confirmed relocated, behavior unchanged)
ida_reverified: 2026-06-21   # CYCLE 8: event-dispatch FSM (8 types, reverse-child topmost-first, single global click-capture, synthetic CLICK, action-id->active-child->window switch, ESC=27/Tab=9/Enter=10) re-confirmed CODE-CONFIRMED, zero conflicts
ida_reverified: 2026-06-27
ida_anchor: f61f66a9ae0ec1e946105b2ecff76e8930cb1d1367df64e5688a5266f5ad9963
evidence: [static-ida]
---

# GU widget event dispatch — hit-test, hover, press, click-synth, container routing (clean-room spec)

Neutral, rewritten model of how the legacy client's in-house `Diamond::GU*` widget toolkit turns a raw
pointer/keyboard event into a widget action. This is the **load-bearing 1:1 input behaviour**: get the
state machine wrong and clicks register on the wrong widget, drags do not cancel, hover effects flicker,
and windows switch panels incorrectly.

> **Provenance.** Recovered statically from the legacy client (IDB anchor `263bd994`) and rewritten into
> neutral prose. **No decompiler output, no pseudo-code, no binary addresses.** Field references are
> **byte offsets relative to the start of the widget object** (interoperability facts), and vtable slot
> numbers refer to the layered GU vtable documented in `structs/gucomponent.md §6` and `specs/ui_system.md
> §2`. Citation breadcrumb: `// spec: Docs/RE/specs/ui_event_dispatch.md`.

This spec owns the **dispatch state machine**; the object field offsets live in `structs/gucomponent.md`
and the vtable-slot roster in `specs/ui_system.md §2`. Where they list the same fact, treat them as one.

## 0. The one-line chain

A click travels: **cursor pixel → container reverse child hit-test (slot 5) → first-hit child's onEvent
(slot 6) press/release capture → a synthetic CLICK event → the child's action id (field +0x10, slot 10)
stored into the panel's active-child field (+0xB4) → the window's onEvent switch on that action id → the
concrete command handler.** Each step is detailed below.

## 1. Event types (CODE-CONFIRMED)

The dispatcher branches on a one-byte event-type tag carried by the event record:

| Type | Meaning | Dispatch behaviour |
|---:|---|---|
| 1 | key-down | key group — routed to children in reverse, first consumer wins |
| 2 | key-up | key group |
| 3 | mouse-move | pointer group — **broadcast** (keep scanning all children to refresh hover) |
| 4 | button-press | pointer group — **arms** the click capture |
| 5 | button-release | pointer group — **clears** capture and synthesises a CLICK |
| 6 | CLICK | pointer group — **synthetic**, produced by a press+release on the same widget; first consumer wins |
| 7 | double-click / plate-pick | pointer group — first consumer wins |
| 8 | mouse-wheel | pointer group |

The **pointer/click group** is `{3, 4, 5, 6, 7}` (and 8 for wheel); the **key group** is `{1, 2}`. Move
events (type 3) are the only pointer events that keep scanning after a consumer is found (they exist to
broadcast hover to every widget); all other pointer events stop at the first consuming widget.

## 2. The hover machine — slot-5 hit-test (CODE-CONFIRMED)

The leaf hit-test (vtable slot 5) is an **axis-aligned bounding-box** test of the cursor `(x, y)` against
the widget's world rect: `[world_x (+0x2C), world_x + width (+0x1C)] × [world_y (+0x30), world_y +
height (+0x20)]`. It maintains **two** hover fields:

- **`+0x88` (`hovered`)** — the steady, moment-to-moment hover state (1 while inside, 0 while outside).
- **`+0x89` (`hover_edge`)** — the **enter/leave edge latch**, the memory that fires the enter/leave
  callbacks **exactly once per transition**.

The machine:

- **Cursor INSIDE:** set `hovered (+0x88) = 1`. If `hover_edge (+0x89) == 0` (this is the **enter** edge):
  set `hover_edge = 1` and call **slot 11 `onMouseEnter`**. Return "hit".
- **Cursor OUTSIDE:** if `hover_edge (+0x89) == 1` (this is the **leave** edge): set `hover_edge = 0`,
  call **slot 12 `onMouseLeave`**, then set `hovered (+0x88) = 0`. Return "no hit".

So `+0x88` is the live hover state and `+0x89` is the latch that prevents the enter/leave callbacks from
re-firing every frame the cursor lingers. A **disabled** button (its disabled byte set) short-circuits its
hit-test wrapper and reports "no hit" immediately, so a disabled widget neither hovers nor captures.

## 3. The press machine + the one global click-capture (CODE-CONFIRMED)

The leaf onEvent (vtable slot 6) implements press/release capture and click synthesis. There is **exactly
one process-global click-capture pointer** for the whole client (a single global slot that holds "the
widget currently being pressed"). Reading the event-type tag:

- **Press (type 4):** if the widget is interactive (`+0x8A != 0`), set the global click-capture pointer to
  **this** widget. (ARMED.)
- **Release (type 5):**
  - If the global capture pointer **== this** widget (pressed and released on the *same* widget): clear the
    global capture to null, then **build a synthetic CLICK event (type 6)** copying the source release
    event's payload words, fetch the input-manager singleton, and **enqueue** that synthetic CLICK. This is
    the genuine "click" — **press-inside + release-inside-the-same-widget**.
  - If the global capture pointer **!= this** (released elsewhere, or the cursor was dragged off the
    pressed widget): **no** synthetic CLICK is produced — the action is **canceled**.

The drag-off / drag-back visual is handled by the hover machine (§2): while a button is the capture
target, moving off it fires `onMouseLeave` (slot 12) and pops the button up (clears its pressed byte);
moving back on fires `onMouseEnter` (slot 11) and re-presses it. Releasing while dragged off the button
clears the capture without a synthetic CLICK reaching the button — the standard "drag off to cancel".

## 4. The container router — slot-6 input dispatch (CODE-CONFIRMED)

A panel/window overrides onEvent (slot 6) to route an event into its children. The router:

**Visibility gate.** If the container's own visible byte (`+0x8C`) is 0, return "not consumed"
immediately (an invisible container swallows nothing and passes nothing).

**Key events (types 1, 2).** Walk the child vector **in REVERSE** (from `children_end (+0xAC)` down to
`children_begin (+0xA8)`). For each child whose visible byte (`+0x8C`) is 1, call the child's onEvent
(slot 6). The **first** child that consumes the event: read that child's action id via its getter (slot
10, field `+0x10`), store it into the container's **active-child** field (`+0xB4`), and return "consumed".
If no child consumes, set the active-child field to **−1** and return "not consumed".

**Pointer / click events (types 3, 4, 5, 6, 7).** First set the active-child field (`+0xB4`) to **−1**,
then walk the child vector **in REVERSE**. Per child: refresh the child (its slot-10 getter is called and
the result discarded), then if the child is visible (`+0x8C == 1`) call its hit-test (slot 5) with the
event's cursor coordinates; if it hits, call its onEvent (slot 6). When a child consumes:

- If the container's active-child (`+0xB4`) is still −1, set it to that child's action id (slot 10).
- For **non-move** pointer events (type != 3), **return "consumed"** at this first consumer
  (first-consumer-wins, topmost child first).
- For **move** events (type 3), **keep scanning** the remaining children — move is a hover **broadcast**,
  so every child gets a chance to update its hover state.

After the child loop, the container calls its **own** slot-5 hit-test (so the container itself registers
hover). On a **release** (type 5) it also clears the global click-capture pointer.

**Why reverse order matters.** Children are painted **forward** (insertion order, back-to-front — later
child paints on top; see `specs/ui_system.md §3`). Input is walked **in reverse**, so the **topmost-painted
child (the last added) receives input first** — the inverse of the paint order. This is what makes a
button drawn on top of a panel actually catch the click instead of the panel beneath it.

## 5. The window switch — onEvent action dispatch (CODE-CONFIRMED)

A concrete window's onEvent override first calls the base container router (§4). If that produced a
consumed **CLICK** (type 6) whose payload flag word is set, the window reads its selected command field
and **switches on the action id** (the value the router stored into the active-child field, `+0x10` of the
clicked child), invoking the matching command handler. For example, the character-select window maps its
create / delete / enter command ids to the corresponding handlers; the login window maps its plate-pick
and submit ids likewise (see `specs/frontend_scenes.md` and `specs/frontend_layout_tables.md` for the
per-window action-id maps).

**Key handling at the window level:**
- **ESC (key code 27)** drives cancel / close when the window's cancel-enabled flag is set.
- The **login window** additionally reads **Tab (key code 9)** to swap focus between the ID and PW
  fields and **Enter / OK (key code 10)** to confirm.

The **synthetic CLICK (type 6)** is the genuine click-vs-drag discriminator: only a press and release on
the same widget produces it (§3), so a drag-off release never reaches the window switch.

## 6. End-to-end summary (CODE-CONFIRMED)

1. A pixel event enters the top window's onEvent.
2. The container router (§4) walks children in **reverse** (topmost-painted first), hit-testing (slot 5)
   and dispatching onEvent (slot 6) to the first hit.
3. The hit child's onEvent (§3) arms the **one** global click-capture on press, and on a same-widget
   release synthesises a **CLICK (type 6)**.
4. The router stores the consuming child's **action id** (field +0x10, slot 10) into the panel's
   **active-child** field (+0xB4).
5. The window's onEvent switch (§5) reads that action id and invokes the concrete command handler.
6. Hover (§2) is broadcast on every **move** event via the +0x88 steady flag and the +0x89 enter/leave
   edge latch; drag-off/back toggles the button's pressed state through the same hover callbacks.

## Cross-references

- Widget field offsets (+0x88 hovered, +0x89 hover_edge, +0x8A interactive, +0x8C visible, +0x10 action id,
  +0xB4 active-child, child vector +0xA8/+0xAC): `structs/gucomponent.md`
- The layered 13/14/15 vtable slot roster: `structs/gucomponent.md §6`, `specs/ui_system.md §2`
- GUWindow multiple inheritance + the command-handler subobject: `structs/guwindow.md`
- Draw / paint order (forward, back-to-front), the render-submit pipeline: `specs/ui_system.md §3`
- Per-window action-id maps (login, char-select): `specs/frontend_layout_tables.md`, `specs/frontend_scenes.md`
- Glossary: `Docs/RE/names.yaml`
- Provenance: `Docs/RE/journal.md`
