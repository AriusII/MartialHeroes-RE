# GUComponent / GUPanel byte-offset layout (clean-room spec)

Neutral, rewritten offset model of the legacy client's base UI-toolkit objects. The 2D UI is a
single in-house C++ widget toolkit (internal namespace `Diamond`, the `GU*` class family). This
file documents the two lower base classes of that family:

- **`GUComponent`** — the root widget (every UI element derives from it): position, size,
  alpha/tint, capability flags, a z/timeout constant, an id field.
- **`GUPanel`** — a container deriving from `GUComponent`: adds a small intrusive child-list region
  and sets the "is a panel" flag bit.

The top-level **`GUWindow`** (which adds multiple-inheritance sub-objects) is documented separately
in `structs/guwindow.md`.

> **Provenance.** Recovered statically from the legacy client during Campaign 3 Phase B; rewritten
> into neutral prose for Phase C. **No decompiler output, no pseudo-code, no binary addresses.** All
> offsets below are **byte offsets relative to the start of the object** (interoperability facts),
> never memory addresses. Citation breadcrumb for downstream code: `// spec: Docs/RE/structs/gucomponent.md`.

## Status header

| Aspect | State |
|---|---|
| GUComponent field offsets | **CODE-CONFIRMED** — read field-by-field from the two GUComponent initialisers (a default zero-init and a sized `(id, x, y, w, h, …)` variant). |
| GUPanel child-list region | **CODE-CONFIRMED** — the four cleared list slots + the sentinel + the panel-kind byte. |
| Capability-flag bit masks | **CODE-CONFIRMED** — the three set bits are read directly from the three base constructors. |
| Semantic of the field at +0x9C (z-order vs display-timeout) | **PLAUSIBLE** — offset and value are confirmed; the meaning is inferred. |
| Packing / alignment | **CODE-CONFIRMED** — natural 4-byte alignment (legacy MSVC default), **not** byte-packed. |

This struct cross-references the richer behavioural treatment of the same fields in
`specs/ui_system.md §1.2`; that spec and this file agree. Where the two list a field at the same
offset, treat them as one fact.

---

## 1. Object model overview

A `GUComponent` is a single object whose used fields span at least the first **0xA4 bytes**; a
`GUPanel` extends it with the child-list region from **0xA4 .. 0xB8**.

| Range | Region |
|---|---|
| +0x00 | Primary vtable pointer |
| +0x04 .. +0x40 | Display state: alpha, capability flags, position/size, derived extents |
| +0x84 .. +0xA0 | Status bytes (hover/focus/show-hide/remove), id field, z/timeout constant |
| +0xA4 .. +0xB8 | (GUPanel only) intrusive child-list region + panel-kind byte |

The sized constructor takes arguments in the order `(id, posX, posY, originX, originY, width,
height[, panelKind])`. The default constructor performs the same zero/sentinel init without the
geometry arguments.

---

## 2. GUComponent (root) — field table (CODE-CONFIRMED)

| Offset | Size | Type | Field | Notes / observed values |
|-------:|-----:|------|-------|-------------------------|
| +0x00 | 4 | ptr | `vftable` | Primary virtual method table pointer. |
| +0x04 | 4 | u32 | `alpha` | Current alpha 0..255; default-initialised to **255**. Show/hide fade chases a target byte (see +0x8C). |
| +0x08 | 4 | u32 | `flags` | Capability flags; OR-set per class (see §4). |
| +0x0C | 4 | i32 | `field_0C` | Default-init to **−1** sentinel; in the sized ctor it instead receives a caller-supplied value (a nullable id). |
| +0x10 | 4 | i32 | `action_id` | Default-init to **−1** sentinel; the per-widget action identifier delivered on click. (See `specs/ui_system.md §1.2`: the click dispatcher reads this field.) |
| +0x14 | 4 | i32 | `pos_x` | Sized ctor: caller `posX`. Local X relative to the parent. |
| +0x18 | 4 | i32 | `pos_y` | Sized ctor: caller `posY`. Local Y. |
| +0x1C | 4 | i32 | `origin_x` | Sized ctor: caller `originX` (left edge). Also used as the base for the right-edge extent at +0x3C. |
| +0x20 | 4 | i32 | `origin_y` | Sized ctor: caller `originY` (top edge). |
| +0x24 | 4 | i32 | `width` | Sized ctor: caller `width`. |
| +0x28 | 4 | i32 | `height` | Sized ctor: caller `height`. |
| +0x34 | 4 | i32 | `width_copy` | Sized ctor: a second store of `width`. |
| +0x38 | 4 | i32 | `height_copy` | Sized ctor: a second store of `height`. |
| +0x3C | 4 | i32 | `x_extent` | Sized ctor: `originX + width` (right edge). |
| +0x40 | 4 | i32 | `y_extent` | Sized ctor: `originY + height` (bottom edge). |
| +0x84 | 4 | u32 | `field_84` | Zero-initialised. |
| +0x88 | 1 | u8 | `hovered` | Status byte, zero-initialised; set by the hit-test. |
| +0x8A | 1 | u8 | `flag_8A` | Set to **1** at construction. |
| +0x8B | 1 | u8 | `flag_8B` | Zero-initialised. |
| +0x8C | 1 | u8 | `show_target` | Set to **1** at construction; the show/hide alpha-fade target (1 = showing, 0 = hiding). |
| +0x8D | 1 | u8 | `remove_mark` | Zero-initialised; deferred-removal flag swept by the panel child-removal pass. |
| +0x90 | 4 | i32 | `id_or_index` | Sized ctor: caller `id` (the first constructor argument — an id / texture-binding / index field). |
| +0x95 | 1 | u8 | `flag_95` | Zero-initialised. |
| +0x98 | 4 | u32 | `field_98` | Zero-initialised. |
| +0x9C | 4 | u32 | `z_or_timeout` | Set unconditionally to **3000** by both initialisers. A per-component default; most consistent with a default z-order constant or a display-timeout in milliseconds. **(offset/value CODE-CONFIRMED; semantic PLAUSIBLE.)** |
| +0xA0 | 4 | u32 | `field_A0` | Zero-initialised; last GUComponent field. |

The first GUComponent field after +0xA0 is the GUPanel child-list region at +0xA4.

> **Field-naming note.** Several offsets carry richer role names in `specs/ui_system.md §1.2`
> (e.g. +0x88 hovered, +0x8C show-target, +0x8D remove-mark, +0x90 bound-texture id). The names
> above are kept conservative where the value/role split is not fully pinned. Both files describe
> the same object.

---

## 3. GUPanel (extends GUComponent) — added fields (CODE-CONFIRMED)

The GUPanel constructor first chains the GUComponent base init, overwrites the vtable pointer with
the GUPanel vtable, then initialises a small intrusive child-list region and sets the panel flag
bit.

| Offset | Size | Type | Field | Notes |
|-------:|-----:|------|-------|-------|
| +0x00 | 4 | ptr | `vftable` | Overwritten with the GUPanel vtable. |
| +0xA4 | 4 | ptr | `child_list_anchor` | List anchor; head of the intrusive child container. |
| +0xA8 | 4 | ptr | `child_slot_a` | Zero-initialised list pointer. |
| +0xAC | 4 | ptr | `child_slot_b` | Zero-initialised list pointer. |
| +0xB0 | 4 | ptr | `child_slot_c` | Zero-initialised list pointer. |
| +0xB4 | 4 | i32 | `active_child` | OR-set to **−1** → "no child selected" sentinel (the active-child / tab-selection index). |
| +0xB8 | 1 | u8 | `panel_kind` | Sized ctor only: the caller-supplied panel type/mode byte. |
| +0x08 | 4 | u32 | `flags` | OR-set with bit mask **0x0004** → "is a panel" (see §4). |

The child-list region is therefore **0xA4 .. 0xB7**: a small intrusive child container (an anchor +
three pointer slots + a selection index), with the `panel_kind` byte at **0xB8**.

> **Implementation note.** `specs/ui_system.md §1.2` models the panel's child storage as a
> three-pointer vector (begin / end / capacity at +0xA8 / +0xAC / +0xB0) plus the active-child index
> at +0xB4. That is the same region read as a `std::vector`-style triple; this file records the four
> cleared pointer slots and the sentinel as the constructor lays them out. Use whichever view the
> implementation prefers — they describe one 0xA4..0xB8 region.

---

## 4. Capability-flags word at +0x08 (CODE-CONFIRMED)

The 32-bit `flags` field at +0x08 accumulates one OR-set bit per base class as construction walks
the inheritance chain. The confirmed bits:

| Bit | Mask | Set by | Meaning |
|----:|-----:|--------|---------|
| 0 | 0x0001 | GUComponent init | "is a component" / initialised |
| 2 | 0x0004 | GUPanel ctor | "is a panel" (container) |
| 13 | 0x2000 | GUWindow ctor | "is a top-level window" (see `structs/guwindow.md`) |

A reimplementation can model these as a `[Flags]` enum; the literal masks are the load-bearing
interoperability facts when round-tripping legacy state.

---

## 5. Sentinels and the 3000 constant (CODE-CONFIRMED offsets)

- **−1 sentinels** at +0x0C and +0x10 (GUComponent) and +0xB4 (GUPanel active-child) mark
  "unset / none". +0x0C is reused as a real value by the sized ctor, so treat it as a nullable id.
- **+0x9C = 3000** is a per-component default set by both initialisers. Semantic is PLAUSIBLE
  (z-order default vs display-timeout ms); offset and value are CODE-CONFIRMED.

---

## 6. Packing / alignment (CODE-CONFIRMED)

Natural **4-byte alignment** (legacy MSVC default). Every 4-byte field sits on a 4-aligned offset;
the status bytes are packed into the +0x88..+0x8D and +0xB8 ranges with no forced gap removal. A
C# mirror should use `StructLayout(Sequential)` with **natural alignment** — **not** `Pack = 1`
(unlike the wire/asset structs, which are byte-packed).

---

## 7. Known unknowns

- The exact role split of +0x0C vs +0x10 (the two −1-sentinel dwords): both are nullable ids, but
  which carries the click action id vs an auxiliary id is recorded above per the behavioural spec
  and should be re-confirmed if a discrepancy surfaces.
- The semantic of +0x9C = 3000 (z-order default vs display-timeout). PLAUSIBLE.
- The roles of the zero-initialised fields +0x84, +0x95, +0x98, +0xA0 are not pinned.
- Fields between +0x40 and +0x84 are not enumerated by the base initialisers (left at their prior
  state); the transform/world-rect/timer fields documented in `specs/ui_system.md §1.2` (e.g. world
  pos at +0x2C/+0x30, live src-rect at +0x34, transform matrix at +0x44, timer fields at +0x98/+0x9C/+0xA0)
  are written by the per-frame update path, not the constructor, and are owned by that spec.

## Cross-references

- Top-level window layout (multiple inheritance): `structs/guwindow.md`
- Widget behaviour, vtable slots, draw/hit-test/fade, the leaf-widget catalogue: `specs/ui_system.md`
- Window-manager doctrine + service-slot table: `specs/ui_system.md`, `structs/runtime_singletons.md §3.10`
- Glossary: `Docs/RE/names.yaml`
- Provenance: `Docs/RE/journal.md`
