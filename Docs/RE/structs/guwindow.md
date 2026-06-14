# GUWindow multiple-inheritance layout (clean-room spec)

Neutral, rewritten offset model of the legacy client's top-level UI window object. `GUWindow` is the
top of the in-house `Diamond::GU*` widget toolkit: it derives from `GUPanel` (and through it
`GUComponent`) **and** from a second event-handler base, embedding a command handler, an auxiliary
3D/scene view, and a texture/skin-atlas list. Five top-level windows in the client derive from it;
the in-game HUD master ("MainMaster") is one of them.

> **Provenance.** Recovered statically from the legacy client during Campaign 3 Phase B; rewritten
> into neutral prose for Phase C. **No decompiler output, no pseudo-code, no binary addresses.** All
> offsets are **byte offsets relative to the start of the object** (interoperability facts), never
> memory addresses. Citation breadcrumb: `// spec: Docs/RE/structs/guwindow.md`.

## Status header

| Aspect | State |
|---|---|
| Two-vptr multiple-inheritance shape | **CODE-CONFIRMED** — the primary vtable at +0x00 and the secondary event-handler vtable at +0xBC. |
| Embedded sub-object start offsets (command handler / view / texture list) | **CODE-CONFIRMED** — start offsets pinned; the exact byte spans of the view and texture-list sub-objects are not. |
| Inherited base-class region (GUPanel → GUComponent) | **CODE-CONFIRMED** — see `structs/gucomponent.md`. |
| Window flag bit | **CODE-CONFIRMED** — the "is a window" mask. |

Cross-reference: the concrete `MainWindow` subclass and its service-slot table are in
`structs/runtime_singletons.md §3.10`; the window-manager doctrine is in `specs/ui_system.md`.

---

## 1. Object model overview

`GUWindow` is the classic MSVC multiple-inheritance layout: **two vtable pointers in one object**.
The primary chain (`GUWindow → GUPanel → GUComponent`) occupies the start of the object; a second
event-handler base contributes a second vtable pointer mid-object.

| Range | Region |
|---|---|
| +0x00 .. ~+0xBB | GUPanel header (which contains the GUComponent header) — see `structs/gucomponent.md` |
| +0xBC .. ~+0xE7 | Embedded **command handler** sub-object (the event-handler base; carries the secondary vtable) |
| +0xE8 .. ~+0x21F | Embedded **auxiliary view** sub-object (a 3D/scene view with HUD/scene callback hook slots) |
| +0x220 .. (end) | Embedded **texture list** sub-object (the window's texture / skin-atlas list) |

Construction order: the GUPanel base init runs first (installing the GUPanel vtable), then the
GUWindow ctor overwrites the primary vtable at +0x00 with the GUWindow vtable, sets the window flag
bit, and constructs the three embedded sub-objects in place.

---

## 2. Field / sub-object table (CODE-CONFIRMED)

| Offset | Size | Type | Field / sub-object | Notes |
|-------:|-----:|------|--------------------|-------|
| +0x00 | 4 | ptr | `vftable_primary` | Overwritten with the GUWindow vtable after the GUPanel base ran. The primary interface (component/panel/window method chain). |
| +0x08 | 4 | u32 | `flags` | OR-set with mask **0x2000** → "is a top-level window" (the window bit; see `structs/gucomponent.md §4`). |
| +0xA4 .. +0xB8 | — | — | (inherited GUPanel child-list region) | See `structs/gucomponent.md §3`. |
| +0xBC | 4 | ptr | `vftable_secondary` | The **second** vtable pointer — the event-handler interface used to dispatch input/command events into the window. This is the MSVC multiple-inheritance adjustor-thunk layout. |
| +0xBC | var | obj | `command_handler` | Embedded command-handler (event-handler) sub-object, constructed in place starting at this offset. Its first word is the secondary vtable above. The command handler routes a clicked widget's action id to the window's action dispatch. |
| +0xE8 | var | obj | `aux_view` | Embedded auxiliary 3D/scene view sub-object (carries scene/HUD callback hook slots, all null in the shipped client). |
| +0x220 | var | obj | `texture_list` | Embedded texture/skin-atlas list sub-object — the window's per-window texture list. |

The command-handler constructor takes a short name string plus two count/size arguments. The HUD
master window is constructed with the name **"MainMaster"** (and two fixed numeric arguments); that
name is how the master window identifies itself among the five window instances.

---

## 3. The five top-level window instances (CODE-CONFIRMED)

Five concrete classes derive from `GUWindow`. Each is a distinct top-level scene/window:

| Window | Role |
|---|---|
| **MainWindow** ("MainMaster") | The in-game HUD master window. **Also the de-facto window manager** — its service-slot table holds every HUD child-panel pointer (see `structs/runtime_singletons.md §3.10` and `specs/ui_system.md`). |
| **CommonLoginWindow** | The login window base (login form, server selection, channel-resolve handoff). |
| **OpeningWindow** | The opening-cinematic window (scene state 3). |
| **SelectWindow** | The character-select window (scene state 4); hosts the live 3D preview row and the preview camera (see `specs/frontend_scenes.md`). |
| **TestWindow** | A dev/test top-level window (one construction site; likely debug/dead code). |

> **Window-manager finding (cross-reference).** There is **no separate "WindowManager" class**. The
> MainWindow ("MainMaster") *is* the manager: a global singleton `GUWindow` whose flat service-slot
> pointer table (documented in `structs/runtime_singletons.md §3.10` at +0x238..+0x5B4) holds every
> HUD child-panel pointer, reached through one very-high-call-count singleton accessor. Detailed in
> `specs/ui_system.md`.

---

## 4. Known unknowns

- The exact byte spans of the `aux_view` (+0xE8) and `texture_list` (+0x220) sub-objects (only their
  start offsets are pinned). The full `MainWindow` object is 1464 bytes total
  (`structs/runtime_singletons.md §3.10`); the service-slot table occupies +0x238..+0x5B4.
- The internal layout of the embedded command handler beyond its secondary vtable at +0xBC.
- The semantics of the four scene/HUD callback hook slots inside `aux_view` (null in the shipped
  client).

## Cross-references

- Base-class field layout (GUPanel / GUComponent): `structs/gucomponent.md`
- Concrete `MainWindow` + the 223-slot service table: `structs/runtime_singletons.md §3.10`
- Window-manager doctrine, widget catalogue, draw/input behaviour: `specs/ui_system.md`
- Character-select window + preview camera: `specs/frontend_scenes.md`
- Glossary: `Docs/RE/names.yaml`
- Provenance: `Docs/RE/journal.md`
