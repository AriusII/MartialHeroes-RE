---
verification: confirmed
ida_reverified: 2026-06-21
ida_anchor: 263bd994
evidence: [static-ida]
conflicts: none open. MainWindow size CODE-CONFIRMED at 0x5B8 = 1464 bytes (CYCLE 7, IDB SHA 263bd994) — the prior "not re-measured / capture-pending" item is RESOLVED. LoginWindow size CODE-CONFIRMED at 0x558 = 1368 bytes (scene-state-1 Login allocation).
---

# GUWindow multiple-inheritance layout (clean-room spec)

Neutral, rewritten offset model of the legacy client's top-level UI window object. `GUWindow` is the
top of the in-house `Diamond::GU*` widget toolkit. It is a multiple-inheritance class: the primary
single-inheritance chain (`GUWindow -> GUPanel -> GUComponent`) occupies the start of the object, and a
second event-handler base subobject sits at +0xBC. That second base is the **abstract** event handler
`Diamond::EventHandler`, realised through a **concrete embedded `CmdHandler` subobject** (`CmdHandler`
derives `Diamond::EventHandler`; the RTTI class name of the concrete subobject is **`CmdHandler`**).
The object also embeds an auxiliary 3D/scene view (`Diamond::GView`) and a per-window texture/skin-atlas
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
| Two-vptr multiple-inheritance shape | **CODE-CONFIRMED** — the primary vtable at +0x00, and the secondary vtable at +0xBC belonging to the concrete `CmdHandler` subobject (which realises the abstract `Diamond::EventHandler` base). |
| Embedded sub-object start offsets (command handler / view / texture list) | **CODE-CONFIRMED** — start offsets pinned (+0xBC / +0xE8 / +0x220). |
| Embedded sub-object byte spans | **CODE-CONFIRMED** — command-handler span +0xBC..+0xE7 (44B) and aux-view span +0xE8..+0x21F (312B) are derivable from the next sub-object's start; no longer "unknown". |
| Inherited base-class region (GUPanel → GUComponent) | **CODE-CONFIRMED** — see `structs/gucomponent.md`. |
| Window flag bit | **CODE-CONFIRMED** — the "is a window" mask 0x2000, OR-set **last** in the ctor. |
| Primary vtable (15 slots) + secondary vtable (**2 slots**) | **CODE-CONFIRMED** — slot roles tabled below; the small shared GUComponent accessor slots' exact role is [static-hypothesis]. The secondary (MI base) vtable holds **2 slots**; **CYCLE 8 closed the prior [UNVERIFIED]**: every one of the five derived windows OVERRIDES BOTH secondary slots (slot 0 = base/dtor entry, slot 1 = the derived window's command/event sink — action-id routing). |
| `MainWindow` total size | **CODE-CONFIRMED** (CYCLE 7) — **1464 bytes (0x5B8)**, from the zero-init routine's highest writes (dword at +0x5B0, byte at +0x5B4 → end 0x5B5 → 4-aligned 0x5B8). |
| `MainWindow` static-singleton construction | **CODE-CONFIRMED** (CYCLE 7) — Meyers-style accessor over a static buffer; one-shot guarded ctor, atexit-registered; primary vtable at +0x00, secondary (MI base) vtable at +0xBC, then the service block is zero-cleared. |
| Panel-slot array mechanism | **CODE-CONFIRMED** (CYCLE 7) — base **+0x238**, pointer-sized entries, slot index `= (offset − 0x238) / 4`. (Slot→class roster contents live in `specs/ui_system.md`.) |

Cross-reference: the concrete `MainWindow` panel-slot roster (the slot→class contents and the
slot-resolution verdicts) is in `specs/ui_system.md`; the service-slot table is cross-listed in
`structs/runtime_singletons.md §3.10`; the window-manager doctrine is in `specs/ui_system.md`. This
struct file owns the **layout mechanism** (object size, slot-array base, index formula); the **roster
contents** belong to `specs/ui_system.md`.

---

## 1. Object model overview

`GUWindow` is the classic MSVC multiple-inheritance layout: **two vtable pointers in one object**.
The primary chain (`GUWindow → GUPanel → GUComponent`) occupies the start of the object; a second
event-handler base — the concrete `CmdHandler` subobject realising the abstract `Diamond::EventHandler` — contributes a second vtable pointer mid-object at +0xBC.

> **Two real base subobjects only.** The object has exactly two genuine base subobjects with a vtable
> pointer: the primary chain at +0x00 and the `CmdHandler`/`Diamond::EventHandler` subobject at +0xBC.
> A third class-layout record (with a corrupt base-class count) surfaces during analysis but has **no**
> object vtable pointer; it is a transient dynamic-cast / exception-catch artifact, **not** a third base
> subobject. Do not model a third vptr.

| Range | Region |
|---|---|
| +0x00 .. ~+0xBB | GUPanel header (which contains the GUComponent header) — see `structs/gucomponent.md` |
| +0xBC .. +0xE7 | Embedded **command handler** (`CmdHandler`) sub-object — the concrete realisation of the abstract `Diamond::EventHandler` base; carries the secondary vtable. 44 bytes. |
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
| +0xBC | 4 | ptr | `vftable_secondary` | The **second** vtable pointer — the `CmdHandler` subobject's event-handler interface (the concrete realisation of abstract `Diamond::EventHandler`), used to dispatch input/command events into the window. This is the MSVC multiple-inheritance adjustor-thunk layout. **2 slots**, see §3. |
| +0xBC .. +0xE7 | 44 | obj | `command_handler` (`CmdHandler`) | Embedded command-handler sub-object — the concrete `CmdHandler` realising the abstract `Diamond::EventHandler` base — constructed in place starting here. Its first word is the secondary vtable above. The command handler routes a clicked widget's action id to the window's action dispatch. RTTI class name of the concrete subobject is **`CmdHandler`**; its base is **`Diamond::EventHandler`**. Internal layout below. |
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

### 2.1 The login sub-state field vs the ctor page-counter (do not conflate)

The login window carries **one** live sub-state field plus an **independent** ctor-seeded page/base
counter. These are two different fields with different roles — they are **not** a seed/live pair of one
page-state (an earlier version of this spec wrongly framed +0x554 and +0x238 as seed-and-live of the
same field):

| Offset | Set by | Role |
|-------:|--------|------|
| +0x238 | base ctor seeds it to `1`; driven/consumed by the per-frame Tick and the `OnEvent` path | the **live login sub-state** (spans 1..6 and 29..41). The login workflow machine reads and writes this field. |
| +0x554 | the derived window's ctor (seeded to `5`) | an **independent page/base counter** that the login workflow machine **never reads**. It is the **last object slot** (+0x554 is the final field; +0x554 + 4 = 0x558 = the object size — see §6). It is **not** a seed of the +0x238 sub-state. |

The live login sub-state at object +0x238 is also reached as **+0x17C relative to the command-handler
sub-object base at +0xBC** (because `0xBC + 0x17C = 0x238`): the command-handler `OnEvent` entry recovers
the object base by subtracting 0xBC from its `this`, then reads/writes the same +0x238 cell. So +0x238
(object-relative) and +0x17C (handler-relative) are **two views of one field**, measured from different
bases — distinct from the +0x554 page-counter above, which is a separate field entirely.

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

### 3.2 Secondary command-handler vtable — 2 slots

The `CmdHandler` event interface (the concrete realisation of the abstract `Diamond::EventHandler`
base). **The GUWindow secondary (MI base) vtable holds exactly 2 slots** — its boundary is pinned by
the class-layout record that immediately precedes the primary vtable, so the slot count is not a guess.
Slot 0 is the base event-handler entry; slot 1 is the window's event sink (the `SetVisible` / event
delivery path on the MI-base subobject).

**Per-derived override (CODE-CONFIRMED — CYCLE 8).** Every one of the five derived windows
(`CommonLoginWindow`, `MainWindow`, `OpeningWindow`, `SelectWindow`, `TestWindow`) **overrides BOTH
secondary slots** at +0xBC, in the standard MSVC multiple-inheritance pattern (the GUWindow base ctor
installs the GUWindow command-handler vtable, then each derived ctor overwrites it with its own 2-slot
table). **Slot 0** is the base event-handler / scalar-deleting-destructor entry; **slot 1 is the
window-specific command/event sink**. The GUWindow base slot 1 walks the active panel's child vector,
finds the visible child whose back-pointer matches the handler sub-object, routes the event into that
child's onEvent (primary slot 6), reads the child's hit action id (primary slot 10) into the window's
captured-action field, and returns "consumed" when an action id was captured; if no child captures, it
falls back to the primary-object input dispatch. Each derived window replaces slot 1 with its own sink
(e.g. the login-form logic vs the char-select logic) so the same 2-slot shape carries per-window
behaviour. (This **supersedes** a prior cycle's report of extra live/null/thunk entries in the
MainWindow secondary vtable — the bound is exactly 2 slots, both overridden.)

Both the abstract `Diamond::EventHandler` base and the concrete `CmdHandler` base each expose a 2-slot
vtable of their own (before any GUWindow override). The `CmdHandler` and `Diamond::EventHandler` class
names are reached through those base vtables' RTTI records and are the original author's terms for this
sub-object and its abstract base.

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
| **MainWindow** ("MainMaster") | The in-game HUD master window (**scene state 5**). **Also the de-facto window manager** — its panel-slot array (base +0x238, see §6) holds every HUD child-panel pointer (layout in §6; slot→class roster in `specs/ui_system.md`). | "MainMaster", 1000, 28158 |
| **OpeningWindow** | The opening-cinematic window (**scene state 3**). | (not re-read this lane) |
| **SelectWindow** | The character-select window (**scene state 4**); hosts the live 3D preview row and the preview camera (see `specs/frontend_scenes.md`). | (not re-read this lane) |
| **TestWindow** | A dev/test top-level window (one construction site; likely debug/dead code). | (not re-read this lane) |

> **Roster note.** The five *base-ctor callers* are `{CommonLoginWindow, MainWindow, OpeningWindow,
> SelectWindow, TestWindow}`. `LoginWindow` derives from `CommonLoginWindow` (which is itself the direct
> base-ctor caller), so the concrete top-level-window roster reads cleanly as the five above. The
> MSVC MI override chain is visible on the login path: the GUWindow base installs the command-handler
> vtable, then `CommonLoginWindow` overwrites it, then `LoginWindow` overwrites it again.

> **Window-manager finding (cross-reference).** There is **no separate "WindowManager" class**. The
> MainWindow ("MainMaster") *is* the manager: a static-singleton `GUWindow`-derived object whose flat
> panel-slot pointer array (base +0x238, see §6 below; cross-listed in
> `structs/runtime_singletons.md §3.10`) holds every HUD child-panel pointer, reached through one
> very-high-call-count singleton accessor. The slot→class roster is detailed in `specs/ui_system.md`.
> (The per-scene std::list teardown/dispose mechanism — distinct from this manager attachment — tears
> scene objects down at scene exit; see `specs/ui_system.md`.)

---

## 6. MainWindow ("MainMaster") — the root HUD window-manager (CODE-CONFIRMED — CYCLE 7)

`MainWindow` is the concrete top-level window that derives from `GUWindow` and serves as the in-game
HUD **window-manager** (its RTTI / command-handler name is **"MainMaster"**). It is the deepest level
of the widget hierarchy (`GUComponent → GUPanel → GUWindow → MainWindow`).

### 6.1 Static-singleton construction (not heap-allocated)

`MainWindow` is a **static singleton**, *not* an `operator new` allocation:

- A **Meyers-style accessor** returns a fixed static buffer (the single instance), guarded by a
  one-shot construction latch; the constructor is **atexit-registered** for teardown.
- The constructor chains the `GUWindow` base ctor, then **installs the primary `MainWindow` vtable at
  +0x00** (over the GUWindow one), then **installs a secondary (multiple-inheritance base-subobject)
  vtable at +0xBC** (the `CmdHandler` interface realising abstract `Diamond::EventHandler`, over the
  GUWindow command-handler vtable), then
  runs a **zero-clear of the service block** (the panel-slot array and trailing fields).
- Because it is a static buffer, the object size cannot be read from an allocation size; it is
  recovered from the zero-init routine's highest writes (see §6.2).

### 6.2 MainWindow total size — 1464 bytes (0x5B8) — CONFIRMED

The service-block zero-init routine clears the instance through the trailing fields. The **highest
fields it writes** are a dword at **+0x5B0 (1456)** and a byte at **+0x5B4 (1460)**; the object end is
therefore **0x5B5**, rounded up to 4-byte alignment = **0x5B8 = 1464 bytes**. This **confirms** the
prior-cycle 1464-byte / 0x5B8 hypothesis — the residual is resolved. Confidence: high.

### 6.3 MainWindow top-level layout

| Offset | Size | Type | Field / region | Notes |
|-------:|-----:|------|----------------|-------|
| +0x00 | 4 | ptr | primary vtable (`MainWindow`) | The GUWindow-derived primary chain (component/panel/window methods); installed over the GUWindow vtable by the ctor. |
| +0x00 .. +0xBB | 188 | (inherited) | GUWindow / GUPanel / GUComponent header | base subobject (see §1–§4 and `structs/gucomponent.md`). |
| +0xBC | 4 | ptr | secondary (MI base) vtable | the command-handler ("Cmdhandler") interface vtable, installed over the GUWindow one by the ctor. |
| +0xC0 .. +0x237 | ~376 | mixed | `MainWindow` own members / GUPanel child list etc. | zero-cleared by the ctor; not individually pinned this lane. |
| **+0x238** | 4×N | ptr[] | **PANEL-SLOT ARRAY base** (the HUD panel registry) | see §6.4. |
| +0x238 .. +0x517 | ~736 | ptr[] | panel-pointer slots 0..183 | zero-cleared by the ctor, filled by the in-game HUD-build routine. Slot→class contents: `specs/ui_system.md`. |
| +0x500 | 4 | ptr | in-game main-handler slot (slot 178) | **SKIPPED by the zero-init** and filled when the in-game world scene state is entered — see §6.5. |
| +0x518 .. +0x5A4 | — | ptr[] | trailing sparse slots (up to ~slot 218) | mostly null/reserved. |
| +0x5A8 | 4 | int | trailing field | cleared by the ctor. |
| +0x5B0 | 4 | int | trailing field (last dword) | highest dword written by the zero-init. |
| +0x5B4 | 1 | u8 | trailing field (last byte) | end of object → 0x5B5 → 4-aligned 0x5B8. |

### 6.4 The panel-slot array mechanism

The HUD panel registry is a **flat array of pointer-sized slots** embedded in the `MainWindow` object:

- **Base offset = +0x238.**
- Entries are **pointer-sized (4 bytes)** in this 32-bit client.
- **Slot index → offset:** slot `i` lives at **`+0x238 + i × 4`**.
- **Offset → slot index:** `i = (offset − 0x238) / 4`.

The array is zero-cleared by the constructor (one slot deliberately excepted — see §6.5) and
populated by the in-game HUD-build routine, which constructs each panel and stores its pointer into
its slot. **The contents of the array — which concrete panel class occupies which slot, the gaps, and
the slot-resolution verdicts — are documented in `specs/ui_system.md`, not here.** This file owns only
the array *mechanism* (base, stride, index formula).

### 6.5 Slot 178 (+0x500) — the in-game state handler

Slot 178 sits at **+0x238 + 178 × 4 = +0x500**. It is **deliberately skipped by the constructor's
zero-init** and is instead constructed and stored when the in-game world scene state is entered: the
scene state machine allocates a **200-byte (0xC8)** in-game handler object and stores its pointer into
this slot. (Its concrete class identity and role are catalogued in `specs/ui_system.md`.) Confidence:
high.

---

## 7. Residual unknowns (capture/debugger-pending)

- **`MainWindow` size — RESOLVED (CYCLE 7).** Now CODE-CONFIRMED at **1464 bytes (0x5B8)** (§6.2);
  the prior "not re-measured / capture-pending" item no longer applies.
- The **`LoginWindow` object size is CODE-CONFIRMED at `0x558` = 1368 bytes** (the scene-state-1
  Login case allocates `0x558` before running the LoginWindow ctor); +0x554 is the last slot, so the
  object ends exactly at 0x558. The MainWindow service-slot table occupies +0x238..+0x5B4
  (`specs/ui_system.md`; cross-listed in `structs/runtime_singletons.md §3.10`).
- The runtime **semantics** of the `GView` hook function-ptr slot (null in the shipped client) — its
  purpose is inferred, not observed firing.
- The detailed roles of the small shared GUComponent accessor slots (primary vtable slots 2–5,
  11–12) — marked [static-hypothesis] as small shared getters.

## Cross-references

- Base-class field layout (GUPanel / GUComponent), the 13/14/15 vtable layering, +0x8D / +0xB8: `structs/gucomponent.md`
- `MainWindow` panel-slot **roster** (slot→class contents + slot-resolution verdicts): `specs/ui_system.md`
- `MainWindow` service-slot table (cross-listed): `structs/runtime_singletons.md §3.10`
- Window-manager doctrine, widget catalogue, draw/input behaviour, dispose/teardown list: `specs/ui_system.md`
- Character-select window + preview camera: `specs/frontend_scenes.md`
- Glossary: `Docs/RE/names.yaml`
- Provenance: `Docs/RE/journal.md`
