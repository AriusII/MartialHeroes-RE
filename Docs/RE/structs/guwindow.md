---
verification: confirmed
ida_reverified: 2026-06-16
ida_anchor: 263bd994
evidence: [static-ida]
conflicts: exact total byte-end of MainWindow (1464B) and LoginWindow (1368B) objects not re-measured this lane (capture/debugger-pending)
---

# GUWindow multiple-inheritance layout (clean-room spec)

Neutral, rewritten offset model of the legacy client's top-level UI window object. `GUWindow` is the
top of the in-house `Diamond::GU*` widget toolkit: it derives from `GUPanel` (and through it
`GUComponent`) **and** from a second event-handler base (RTTI class **"Cmdhandler"**), embedding a
command handler, an auxiliary 3D/scene view (RTTI class **`Diamond::GView`**), and a texture/skin-atlas
list. Five top-level windows in the client derive from it; the in-game HUD master ("MainMaster") is one
of them.

> **Provenance.** Recovered statically from the legacy client during Campaign 3 Phase B and
> re-verified during Campaign 10 Block B (lane B2) against IDB control-flow + immediate operands;
> rewritten into neutral prose for Phase C. **No decompiler output, no pseudo-code, no binary
> addresses.** All offsets are **byte offsets relative to the start of the object** (interoperability
> facts), never memory addresses. Citation breadcrumb: `// spec: Docs/RE/structs/guwindow.md`.

## Status header

| Aspect | State |
|---|---|
| Two-vptr multiple-inheritance shape | **CODE-CONFIRMED** — the primary vtable at +0x00 and the secondary command-handler vtable at +0xBC. |
| Embedded sub-object start offsets (command handler / view / texture list) | **CODE-CONFIRMED** — start offsets pinned (+0xBC / +0xE8 / +0x220). |
| Embedded sub-object byte spans | **CODE-CONFIRMED** — command-handler span +0xBC..+0xE7 (44B) and aux-view span +0xE8..+0x21F (312B) are derivable from the next sub-object's start; no longer "unknown". |
| Inherited base-class region (GUPanel → GUComponent) | **CODE-CONFIRMED** — see `structs/gucomponent.md`. |
| Window flag bit | **CODE-CONFIRMED** — the "is a window" mask 0x2000, OR-set **last** in the ctor. |
| Primary vtable (15 slots) + secondary vtable (3 slots) | **CODE-CONFIRMED** — slot roles tabled below; the small shared GUComponent accessor slots' exact role is [static-hypothesis]. |

Cross-reference: the concrete `MainWindow` subclass and its service-slot table are in
`structs/runtime_singletons.md §3.10`; the window-manager doctrine is in `specs/ui_system.md`.

---

## 1. Object model overview

`GUWindow` is the classic MSVC multiple-inheritance layout: **two vtable pointers in one object**.
The primary chain (`GUWindow → GUPanel → GUComponent`) occupies the start of the object; a second
event-handler base ("Cmdhandler") contributes a second vtable pointer mid-object at +0xBC.

| Range | Region |
|---|---|
| +0x00 .. ~+0xBB | GUPanel header (which contains the GUComponent header) — see `structs/gucomponent.md` |
| +0xBC .. +0xE7 | Embedded **command handler** ("Cmdhandler") sub-object (the event-handler base; carries the secondary vtable). 44 bytes. |
| +0xE8 .. +0x21F | Embedded **auxiliary view** sub-object (`Diamond::GView`, a 3D/scene view with HUD/scene callback hook slots). 312 bytes. |
| +0x220 .. (end) | Embedded **texture list** sub-object (the window's per-window texture / skin-atlas list). |

**Construction order (CODE-CONFIRMED, ordering corrected).** The base ctor runs its steps in this
order:

1. The **GUPanel base init** runs first (installing the GUPanel vtable, chaining the GUComponent
   header) — region +0x00..~+0xBB.
2. The **command-handler ("Cmdhandler") sub-object ctor** constructs in place at **+0xBC**.
3. The **primary vtable at +0x00** is overwritten with the GUWindow vtable (over the GUPanel one).
4. The **secondary vtable at +0xBC** is overwritten with the GUWindow command-handler vtable (over
   the Cmdhandler base one).
5. The **auxiliary view (`Diamond::GView`) sub-object ctor** constructs at **+0xE8**.
6. The **texture-list sub-object ctor** constructs at **+0x220**.
7. The **window flag bit** (mask 0x2000) is OR-set into `flags` at +0x08 **LAST**, after all the
   sub-objects are in place.

> **Ordering correction (was inverted).** An earlier revision narrated the flag as being set *before*
> the sub-objects were constructed. The IDB control-flow shows the flag OR is the **final** step —
> after the command-handler, primary/secondary vtable overwrites, the `GView`, and the texture list.
> The net object layout is identical; only the narrated order changed.

---

## 2. Field / sub-object table (CODE-CONFIRMED)

| Offset | Size | Type | Field / sub-object | Notes |
|-------:|-----:|------|--------------------|-------|
| +0x00 | 4 | ptr | `vftable_primary` | Overwritten with the GUWindow vtable after the GUPanel base ran. The primary interface (component/panel/window method chain) — 15 slots, see §3. |
| +0x08 | 4 | u32 | `flags` | OR-set with mask **0x2000** → "is a top-level window" (the window bit; see `structs/gucomponent.md §4`). OR-set **last** in the ctor. |
| +0xA4 .. +0xB8 | — | — | (inherited GUPanel child-list region) | See `structs/gucomponent.md §3`. |
| +0xBC | 4 | ptr | `vftable_secondary` | The **second** vtable pointer — the command-handler ("Cmdhandler") event-handler interface used to dispatch input/command events into the window. This is the MSVC multiple-inheritance adjustor-thunk layout. 3 slots, see §3. |
| +0xBC .. +0xE7 | 44 | obj | `command_handler` ("Cmdhandler") | Embedded command-handler (event-handler) sub-object, constructed in place starting here. Its first word is the secondary vtable above. The command handler routes a clicked widget's action id to the window's action dispatch. RTTI class name literally **"Cmdhandler"**. Internal layout below. |
| +0xC0 | 16 | obj | `command_handler.name` | Inline `std::string` (16-byte small-string buffer) holding the handler's name (e.g. the HUD master's "MainMaster"). |
| +0xDC | 4 | u32 | `command_handler.count_a` | First numeric ctor argument (e.g. 1000). |
| +0xE0 | 4 | u32 | `command_handler.count_b` | Second numeric ctor argument (e.g. 28158). |
| +0xE8 .. +0x21F | 312 | obj | `aux_view` (`Diamond::GView`) | Embedded auxiliary 3D/scene view sub-object (carries scene/HUD callback hook slots). Owns a **separate heap** sub-object of 0x2E0 bytes (referenced from within the inline GView, not inline). See §4 for its internal layout. |
| +0x220 | var | obj | `texture_list` | Embedded texture/skin-atlas list sub-object — the window's per-window texture list. Its ctor zero-inits a 3-pointer vector triple (a `std::vector`-shaped begin/end/cap). |

The command-handler constructor takes a short name string plus two count/size arguments (stored at
sub-object +0x20/+0x24, i.e. object +0xDC/+0xE0). The HUD master window is constructed with the name
**"MainMaster"** (and the two fixed numeric arguments **1000** and **28158**); that name is how the
master window identifies itself among the five window instances. Each derived window passes its own
name to this same ctor (e.g. the login base passes **"Loginer"** with the same 1000/28158 pair).

### 2.1 Two distinct substate fields (do not conflate)

Derived windows carry **two separate** window-substate views — they are different fields, not two
names for one:

| Offset | Set by | Role |
|-------:|--------|------|
| +0x554 | the derived window's ctor (seeded to `5`) | ctor **seed** of the page-state. |
| +0x238 | read in the command-handler `OnEvent` path | the **live page-state** the event handler reacts to. |

The command-handler `OnEvent` entry sits at the +0xBC base; it recovers the object base by subtracting
0xBC from its `this`, then reads the live page-state at object +0x238. (An earlier "+0x17C page-state"
note is the event-handler-relative view of the same +0x238 object field — consistent, just measured
from a different base.)

---

## 3. Virtual-method tables (CODE-CONFIRMED)

Both vtables are **overridden per derived window** (MSVC MI override chain: the base ctor installs the
GUWindow vtables, then each derived ctor overwrites them with its own).

### 3.1 Primary GUWindow vtable — 15 slots

| Slot | Conceptual method | Notes |
|-----:|-------------------|-------|
| 0 | vector-deleting destructor | calls the GUWindow dtor, conditional `delete`. |
| 1 | SetVisible / show-hide | arms the alpha-fade (writes the visibility byte at +0x8C/+0x8D and the alpha at +0x04). |
| 2 | tiny accessor | shared GUComponent slot. [static-hypothesis] |
| 3 | tiny accessor | shared. [static-hypothesis] |
| 4 | tiny accessor | shared. [static-hypothesis] |
| 5 | tiny accessor | shared. [static-hypothesis] |
| 6 | input/event dispatch | routes an event to children (the GUPanel dispatch slot). |
| 7 | Draw | compute transform + leaf draw + walk the child vector back-to-front. |
| 8 | onUpdate | GUComponent update + iterate children's update. |
| 9 | computeTransform | the GUComponent transform compute. |
| 10 | active-child getter | returns the field at +0xB4 (active child). The IDB falsely labels this a Concurrency thunk via an RTTI collision — it is a plain getter. |
| 11 | tiny accessor | shared. [static-hypothesis] |
| 12 | tiny accessor | shared. [static-hypothesis] |
| 13 | sweep deferred-removed children | reads each child's remove-mark byte at child +0x8D. |
| 14 | aux-view (GView) init helper | finalises the embedded `GView`: sets its size, writes owner back-pointers (at GView-relative +200/+216/+168) to the owning window, and sets the (null in the shipped client) hook function-ptr slot at GView-relative +164. |

### 3.2 Secondary command-handler vtable — 3 slots

The "Cmdhandler" event interface. The GUWindow base installs a 3-slot vtable; its second slot is the
event-handler entry (the window's `SetVisible`/event sink). Derived windows replace this whole vtable
with their own variant (e.g. the login window and the MainWindow each install a distinct 3-slot
command-handler vtable; the MainWindow's has two live entries, a null, and a runtime-library thunk).

The "Cmdhandler" **base** class (before GUWindow's override) has a 5-slot vtable; the **"Cmdhandler"**
RTTI class name is reached through that base vtable's RTTI record and is the original author's term for
this sub-object.

---

## 4. The auxiliary view sub-object (`Diamond::GView`, +0xE8..+0x21F)

`aux_view` is an embedded `Diamond::GView` — a 3D/scene view — spanning **312 bytes** inline. Recovered
internal layout (offsets relative to the GView sub-object start, i.e. object +0xE8 + the listed
delta):

| GView-relative offset | Size | Type | Value / role |
|----------------------:|-----:|------|--------------|
| +0x00 | 4 | ptr | GView vtable. |
| +0x0C | 4 | f32 | `1.0f` (init constant). |
| +0x1C | 4 | i32 | screen width (copied from a global at construction). |
| +0x20 | 4 | i32 | screen height (copied from a global at construction). |
| +0x40 | 4 | i32 | `1` (init constant). |
| +0x70 | 4 | ptr | pointer to a **heap-allocated** child sub-object of size **0x2E0** bytes (not inline). |
| +0x8C | 8 | f64 | `2.0` (init constant). |
| +0xA4 (+168 from GView, written by primary slot 14) | 4 | ptr | owner back-pointer to the owning window. |
| +0xA8 (+164 from GView, written by primary slot 14) | 4 | ptr | scene/HUD callback **hook slot** — **null in the shipped client**. [static-hypothesis on its purpose] |
| +0xC8 / +0xD8 (+200 / +216 from GView, written by primary slot 14) | 4 ea | ptr | additional owner back-pointers. |

The GView ctor zero-inits the bulk of these fields; the owner back-pointers and the (null) hook slot
are written **later** by the primary-vtable slot-14 init helper, not by the GView ctor itself.

---

## 5. The five top-level window instances (CODE-CONFIRMED)

Exactly **five** concrete classes call the `GUWindow` base ctor (proven by counting the cross-references
into that base ctor). Each is a distinct top-level scene/window:

| Window | Role | Name / args at construction |
|---|---|---|
| **CommonLoginWindow** (base of **LoginWindow**) | The login window base (login form, server selection / channel-resolve handoff). Built in **scene state 1** (LOGIN). `LoginWindow` is a further derivation of `CommonLoginWindow`, not a direct base-ctor caller. | "Loginer", 1000, 28158 |
| **MainWindow** ("MainMaster") | The in-game HUD master window (**scene state 5**). **Also the de-facto window manager** — its service-slot table holds every HUD child-panel pointer (see `structs/runtime_singletons.md §3.10` and `specs/ui_system.md`). | "MainMaster", 1000, 28158 |
| **OpeningWindow** | The opening-cinematic window (**scene state 3**). | (not re-read this lane) |
| **SelectWindow** | The character-select window (**scene state 4**); hosts the live 3D preview row and the preview camera (see `specs/frontend_scenes.md`). | (not re-read this lane) |
| **TestWindow** | A dev/test top-level window (one construction site; likely debug/dead code). | (not re-read this lane) |

> **Roster note.** The five *base-ctor callers* are `{CommonLoginWindow, MainWindow, OpeningWindow,
> SelectWindow, TestWindow}`. `LoginWindow` derives from `CommonLoginWindow` (which is itself the direct
> base-ctor caller), so the concrete top-level-window roster reads cleanly as the five above. The
> MSVC MI override chain is visible on the login path: the GUWindow base installs the command-handler
> vtable, then `CommonLoginWindow` overwrites it, then `LoginWindow` overwrites it again.

> **Window-manager finding (cross-reference).** There is **no separate "WindowManager" class**. The
> MainWindow ("MainMaster") *is* the manager: a global singleton `GUWindow` whose flat service-slot
> pointer table (documented in `structs/runtime_singletons.md §3.10` at +0x238..+0x5B4) holds every
> HUD child-panel pointer, reached through one very-high-call-count singleton accessor. Detailed in
> `specs/ui_system.md`. (The per-scene std::list teardown/dispose mechanism — distinct from this
> manager attachment — tears scene objects down at scene exit; see `specs/ui_system.md`.)

---

## 6. Residual unknowns (capture/debugger-pending)

- The exact **total byte-end** of the `MainWindow` (cross-referenced as 1464 bytes) and the
  `LoginWindow` (the +0x554 ctor seed is consistent with a ~1368-byte object) was not re-measured in
  this lane; the service-slot table occupies +0x238..+0x5B4 (`structs/runtime_singletons.md §3.10`).
- The runtime **semantics** of the `GView` hook function-ptr slot (null in the shipped client) — its
  purpose is inferred, not observed firing.
- The detailed roles of the small shared GUComponent accessor slots (primary vtable slots 2–5,
  11–12) — marked [static-hypothesis] as small shared getters.

## Cross-references

- Base-class field layout (GUPanel / GUComponent): `structs/gucomponent.md`
- Concrete `MainWindow` + the 223-slot service table: `structs/runtime_singletons.md §3.10`
- Window-manager doctrine, widget catalogue, draw/input behaviour, dispose/teardown list: `specs/ui_system.md`
- Character-select window + preview camera: `specs/frontend_scenes.md`
- Glossary: `Docs/RE/names.yaml`
- Provenance: `Docs/RE/journal.md`
