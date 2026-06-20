---
verification: confirmed
ida_reverified: 2026-06-20
ida_anchor: 263bd994
evidence: [static-ida]
conflicts: +0x8D dual semantics (setVisible co-writes it AND the panel child-removal sweep treats ==1 as remove) — still capture/debugger-pending; +0xB8 — RESOLVED CYCLE 7 (IDB SHA 263bd994): it is a 4-byte SIGNED INT used as the per-glyph pixel width step for text centering, NOT a pointer and NOT a panel_kind byte (the old "panel_kind / subclass byte" reading is superseded)
---

# GUComponent / GUPanel byte-offset layout (clean-room spec)

Neutral, rewritten offset model of the legacy client's base UI-toolkit objects. The 2D UI is a
single in-house C++ widget toolkit (internal namespace `Diamond`, the `GU*` class family). This
file documents the two lower base classes of that family:

- **`GUComponent`** — the root widget (every UI element derives from it): a packed tint/forced-alpha
  word, a capability-flags word, an action id, a local/world coordinate pair, a width/height pair,
  computed extents, a 4×4 transform matrix, status bytes, a bound drawable handle, an auto-hide
  timer block, and a parent pointer.
- **`GUPanel`** — a container deriving from `GUComponent`: adds a child `std::vector<GUComponent*>`
  region and sets the "is a panel" flag bit.

The top-level **`GUWindow`** (which adds multiple-inheritance sub-objects) is documented separately
in `structs/guwindow.md`.

> **Provenance.** Recovered statically from the legacy client during Campaign 3 / Campaign 10 Phase B
> (IDB anchor `263bd994`); rewritten into neutral prose for Phase C. **No decompiler output, no
> pseudo-code, no binary addresses.** All offsets below are **byte offsets relative to the start of
> the object** (interoperability facts), never memory addresses. Citation breadcrumb for downstream
> code: `// spec: Docs/RE/structs/gucomponent.md`.

## Status header

| Aspect | State |
|---|---|
| GUComponent field offsets | **CODE-CONFIRMED** — read field-by-field from the single GUComponent zero-init ctor and the geometry setters (`setPosition`/`getPosition`/`hitTest`/`computeTransform`/`onUpdate`) and the draw path. |
| Geometry mapping (+0x14..+0x44) | **CODE-CONFIRMED** — +0x1C=width / +0x20=height / +0x24=posX / +0x28=posY proven from hit-test, set/get-position, and a layout-centering helper. The earlier origin/size transposition is **corrected** here. |
| Tint/forced-alpha word (+0x0C / +0x0F) | **CODE-CONFIRMED** — read from the draw/alpha-fade path. |
| Auto-hide timer block (+0x95/+0x98/+0x9C/+0xA0) | **CODE-CONFIRMED** — `setVisible` arms it, the draw path evaluates it. Resolves the old +0x9C "z-order vs timeout" question to **TIMEOUT (ms)**. |
| GUPanel child-vector region | **CODE-CONFIRMED** — a `std::vector<GUComponent*>` (begin/end/capacity) plus the active-child sentinel. |
| Capability-flag bit masks | **CODE-CONFIRMED** — the per-class OR-set bits are read directly from the base + leaf constructors. |
| +0x8D size + setter/getter pair | **CODE-CONFIRMED** (CYCLE 7) — a single 1-byte boolean with a dedicated setter/getter, compared `== 1` to gate a branch in the panel-build path and forced to `0` at one site. |
| +0x8D exact semantic | **capture/debugger-pending** — the panel removal-sweep reads `== 1` to remove; `setVisible` also co-writes it with the visible argument. Whether the build-path toggle is "enable / clip" or "pending removal" needs runtime confirmation. |
| +0xB8 = glyph/char-width step | **CODE-CONFIRMED** (CYCLE 7) — a 4-byte **signed integer** read predominantly as a dword and used as the per-character pixel width in the text-centering layout (`3 × value`, and `value × stanceScale × 3.0`, stanceScale ∈ {1.0, 2.7}). **Resolves the old `panel_kind` / pointer ambiguity: it is neither.** |
| Class hierarchy (vtable-confirmed) | **CODE-CONFIRMED** (CYCLE 7) — `GUComponent` (13 vtable slots) → `GUPanel` (14) → `GUWindow` (15) → `MainWindow` (root HUD window-manager). Confirms the "LAYERED 13/14/15" reading. |
| Packing / alignment | **CODE-CONFIRMED** — natural 4-byte alignment (legacy MSVC default), **not** byte-packed. |

This struct cross-references the richer behavioural treatment of the same fields in
`specs/ui_system.md §1.2`; that spec and this file agree. Where the two list a field at the same
offset, treat them as one fact.

---

## 1. Object model overview

**Class hierarchy (vtable-confirmed).** The widget family is a single linear inheritance chain, each
level adding exactly one virtual slot to the one below it:

`GUComponent` (base widget, **13** vtable slots) → `GUPanel` (container, **14** slots) → `GUWindow`
(top-level window, **15** slots) → `MainWindow` (the root in-game HUD window-manager singleton). This
is the load-bearing confirmation of the prior-cycle "vtable LAYERED 13/14/15" note: 13 = GUComponent,
14 = GUPanel, 15 = GUWindow. `GUWindow`'s multiple-inheritance shape and `MainWindow`'s layout are
documented in `structs/guwindow.md`.

A `GUComponent` is a single object whose used fields span at least the first **0xA4 bytes** (the last
base field is the parent pointer at +0xA0); a `GUPanel` extends it with the child-vector region from
**0xA4 .. 0xB8**.

| Range | Region |
|---|---|
| +0x00 | Primary vtable pointer |
| +0x04 .. +0x10 | Display state: alpha, capability flags, packed tint/forced-alpha, action id |
| +0x14 .. +0x40 | Geometry: local x/y, width/height, posX/posY, computed world x/y, src-rect copies, computed extents |
| +0x44 .. +0x83 | 64-byte 4×4 transform matrix (computed each frame) |
| +0x84 .. +0xA0 | Parent pointer, status/flag bytes, bound drawable handle, auto-hide timer block |
| +0xA4 .. +0xB8 | (GUPanel only) child `std::vector<GUComponent*>` + active-child sentinel + (subclass) panel-kind byte |

**There is no sized constructor.** The binary has exactly one GUComponent initialiser — a default
**zero-init ctor** that writes the vtable, clears every field, sets the default alpha/flags/sentinels
and the auto-hide timeout default, and OR-sets the "is a component" flag bit. The GUPanel ctor and
every leaf-widget ctor **chain that default ctor without geometry arguments**; geometry (position,
size) is applied afterward through the setters/builders (`setPosition`, the shared GU builders) — not
through a constructor argument list. (An earlier draft of this spec inferred a sized
`(id, posX, posY, originX, originY, width, height[, panelKind])` ctor with a specific argument
ordering; the IDB control-flow does not support any such ctor, and that narrative is removed.)

---

## 2. GUComponent (root) — field table (CODE-CONFIRMED)

| Offset | Size | Type | Field | Notes / observed values |
|-------:|-----:|------|-------|-------------------------|
| +0x00 | 4 | ptr | `vftable` | Primary virtual method table pointer (13 slots — see §6). |
| +0x04 | 4 | u32 | `alpha` | Current alpha 0..255; default-initialised to **255**. The draw/fade path chases a target byte (+0x8C ±64 per step); `setVisible` snaps it. |
| +0x08 | 4 | u32 | `flags` | Capability flags; OR-set per class as construction walks the inheritance chain (see §4). |
| +0x0C | 4 | u32 | `tint_and_forced_alpha` | Packed colour word. **Low 24 bits** = RGB tint; **top byte (+0x0F)** = forced-alpha override. Default **0xFFFFFFFF** = white tint, override disabled. The draw path forms the final colour as `(effectiveAlpha << 24) \| (tint & 0x00FFFFFF)`. **(Corrected: this is NOT a nullable id.)** |
| +0x0F | 1 | u8 | `forced_alpha` | Top byte of +0x0C. If **!= 0xFF**, it overrides the faded alpha for this frame; **0xFF** = no override (use the +0x04 fade). |
| +0x10 | 4 | i32 | `action_id` | Default-init to **−1** sentinel; the per-widget action identifier returned by the hit-action getter (vtable slot 10) and delivered on click. (See `specs/ui_system.md §1.2`.) |
| +0x14 | 4 | i32 | `local_x` | Local X relative to the parent; the offset **added to the parent's computed world X** in `computeTransform`. |
| +0x18 | 4 | i32 | `local_y` | Local Y; added to the parent's world Y. |
| +0x1C | 4 | i32 | `width` | **WIDTH.** Hit-test uses `world_x + width` as the right bound; the layout-centering helper halves it to centre. **(Corrected: previously mislabelled `origin_x`.)** |
| +0x20 | 4 | i32 | `height` | **HEIGHT.** Hit-test uses `world_y + height` as the bottom bound; centering halves it. **(Corrected: previously mislabelled `origin_y`.)** |
| +0x24 | 4 | i32 | `pos_x` | **Position X.** `setPosition` writes it; `getPosition` reads it; `onUpdate` uses it as the right-extent base. **(Corrected: previously mislabelled `width`.)** |
| +0x28 | 4 | i32 | `pos_y` | **Position Y.** `setPosition` arg; `getPosition`; `onUpdate` bottom-extent base. **(Corrected: previously mislabelled `height`.)** |
| +0x2C | 4 | i32 | `world_x` | **Computed** world X = `local_x + parent.world_x`. Written by `computeTransform`; used as the hit-test left edge. |
| +0x30 | 4 | i32 | `world_y` | **Computed** world Y = `local_y + parent.world_y`. Hit-test top edge. |
| +0x34 | 4 | i32 | `pos_x_copy` | Position-X copy / live src-rect base. `setPosition` mirrors `pos_x` here; `onUpdate` sets `+0x34 = +0x24`; the draw-submit path passes it as the sprite src-rect / geometry parameter. **(Corrected: previously mislabelled `width_copy`.)** |
| +0x38 | 4 | i32 | `pos_y_copy` | Position-Y copy. `setPosition` mirrors `pos_y`; `onUpdate` sets `+0x38 = +0x28`. **(Corrected: previously mislabelled `height_copy`.)** |
| +0x3C | 4 | i32 | `x_extent` | Right edge = `pos_x(+0x24) + width(+0x1C)`. Recomputed by `onUpdate`. **(Corrected arithmetic source.)** |
| +0x40 | 4 | i32 | `y_extent` | Bottom edge = `pos_y(+0x28) + height(+0x20)`. **(Corrected arithmetic source.)** |
| +0x44 | 64 | matrix | `transform` | 4×4 D3D transform matrix (a translation target built by `computeTransform`); spans **+0x44 .. +0x83**. Passed to the draw-submit path. **(New — explains the old +0x40→+0x84 gap.)** |
| +0x84 | 4 | ptr | `parent` | **Parent component pointer.** The ctor zeroes it; the panel `AddChild` helper stores the parent into the child's +0x84; `computeTransform` follows it to accumulate world coordinates. **(Corrected: previously mislabelled zero-filler `field_84`.)** |
| +0x88 | 1 | u8 | `hovered` | Status byte, zero-initialised; the hit-test sets **1** when the point is inside, **0** outside, firing enter/leave (vtable slots 11/12). |
| +0x8A | 1 | u8 | `interactive` | Set to **1** at construction; the **interactive / clickable gate** — `onEvent` only captures press/release on a widget whose +0x8A is set. **(Role pinned: previously value-only `flag_8A`.)** |
| +0x8B | 1 | u8 | `flag_8B` | Zero-initialised. Role unpinned. |
| +0x8C | 1 | u8 | `show_target` | Set to **1** at construction; the show/hide alpha-fade target (1 = showing, 0 = hiding). The draw/fade path chases the +0x04 alpha toward it; `setVisible` writes it. |
| +0x8D | 1 | u8 (bool) | `remove_mark` / build toggle | Zero-initialised; a **single 1-byte boolean** with a dedicated **setter/getter pair** (CYCLE 7). The panel-build path compares it **== 1** to gate a branch and one site forces it to **0**; the panel child-removal sweep removes children whose +0x8D **== 1** and clears it on survivors. **Note:** `setVisible` also co-writes +0x8D with its visible argument — see the dual-semantics item in §8. Size + accessor-pair are confirmed; the exact build-path semantic (enable/clip vs pending-removal) is the only residual. |
| +0x90 | 4 | ptr | `draw_handle` | **Bound drawable / texture handle.** The draw path submits it as the sprite texture; if **0**, nothing is drawn. The ctor zeroes it. **(Refined from the generic `id_or_index`.)** |
| +0x95 | 1 | u8 | `auto_hide_enabled` | Zero-initialised; the **auto-hide timer enable** gate. Both `setVisible` (arm) and the draw path (evaluate) gate the timer on it. |
| +0x98 | 4 | u32 | `auto_hide_start_ms` | The auto-hide **start timestamp** (milliseconds). `setVisible` stores the current ms here when arming the timer. **(Refined from zero-filler `field_98`.)** |
| +0x9C | 4 | u32 | `auto_hide_timeout_ms` | The auto-hide **timeout duration** in milliseconds. Set unconditionally to **3000** by the ctor (the default). The draw path fires when `(now − +0x98) >= +0x9C`. **(Resolved: the old z-order-vs-timeout PLAUSIBLE is now a CONFIRMED display-timeout.)** |
| +0xA0 | 4 | ptr | `on_timeout_callback` | **Function pointer fired on auto-hide timeout.** The ctor zeroes it; the draw path calls it (when non-null) before hiding the component. Last GUComponent base field. **(Refined from zero-filler `field_A0`.)** |

Two further base-widget fields beyond the GUPanel child region are confirmed on the concrete leaf
widgets (CYCLE 7):

| Offset | Size | Type | Field | Notes |
|-------:|-----:|------|-------|-------|
| +0xE8 | 4 | int/ptr | `font_slot` | Font handle / index slot (matches the prior "font-slot +0xE8" finding). |
| +0xEC | 4 | i32 | `computed_x` | Computed X position written by the alignment/centering helper. |
| +0xF0 | 4 | i32 | `computed_y` | Computed Y position. **Last dword of the base object.** |

**Leaf widget size.** The concrete leaf widgets (a base `GULabel` and a base image `GUComponent`) are
allocated at **0xF0 = 240 bytes** in the HUD-build path; +0xF0 is the last dword, so the base object
is **≈ 0xF4 (244) bytes** rounded. Confidence: high for the 0xF0 leaf allocation size.

The first GUComponent field after +0xA0 is the GUPanel child-vector region at +0xA4.

> **Field-naming note.** `specs/ui_system.md §1.2` carries the same offsets with parallel role names
> (world pos at +0x2C/+0x30, live src-rect at +0x34, transform matrix at +0x44, the timer block at
> +0x98/+0x9C/+0xA0). Both files describe the same object; the corrected geometry mapping in §2 above
> supersedes the older origin/size labelling.

---

## 3. GUPanel (extends GUComponent) — added fields (CODE-CONFIRMED)

The GUPanel constructor first chains the GUComponent zero-init ctor, overwrites the vtable pointer
with the GUPanel vtable, then initialises a `std::vector<GUComponent*>` child container and sets the
panel flag bit. The child store is a standard MSVC three-pointer vector (begin / end / capacity-end);
its size is `(end − begin) >> 2` (4-byte pointers).

| Offset | Size | Type | Field | Notes |
|-------:|-----:|------|-------|-------|
| +0x00 | 4 | ptr | `vftable` | Overwritten with the GUPanel vtable. |
| +0xA4 | 4 | ptr | `children_vector_base` | Leading word of the `std::vector<GUComponent*>` object (the vector's proxy / `_Myfirst` slot). The dtor frees the vector here. |
| +0xA8 | 4 | ptr | `children_begin` | Vector `begin` pointer. Zero-initialised by the ctor. |
| +0xAC | 4 | ptr | `children_end` | Vector `end` pointer. `AddChild` pushes here. |
| +0xB0 | 4 | ptr | `children_capacity_end` | Vector capacity-end pointer. Zero-initialised. |
| +0xB4 | 4 | i32 | `active_child` | OR-set to **−1** → "no child selected" sentinel (the active-child / tab-selection index — role is a static hypothesis; the −1 init is code-confirmed). |
| +0x08 | 4 | u32 | `flags` | OR-set with bit mask **0x0004** → "is a panel" (see §4). |
| +0x84 | 4 | ptr | `parent` | `AddChild` writes the parent component into each child's +0x84 before pushing it into the vector. |

The child region therefore spans **0xA4 .. 0xB7** as the `std::vector<GUComponent*>` object (base
word + begin/end/capacity) plus the active-child sentinel at +0xB4. The **+0xB8 field is NOT part of
the GUPanel child store** — see §3.1 below: it is a per-widget glyph-width step on the base widget,
not the old "panel_kind" byte.

### 3.1 +0xB8 — glyph / character pixel-width step (CODE-CONFIRMED — CYCLE 7)

+0xB8 is a **4-byte signed integer**, not a pointer and not a one-byte `panel_kind` tag. Although one
accessor reads only its low byte, the field is dominated by dword reads (it is loaded as a 32-bit
integer into the FPU and into general registers, and compared as a dword against 0). In the
horizontal-alignment / centering layout it is used as the **per-character pixel width step**: the
centering math takes `3 × value` (and `value × stanceScale × 3.0`, where `stanceScale ∈ {1.0, 2.7}`)
to back off the half-width when centering text inside the parent. **This supersedes the earlier
`panel_kind` / "subclass byte" reading** carried in prior revisions of this spec — that ambiguity is
resolved: +0xB8 is the text-centering glyph-width step.

| Offset | Size | Type | Field | Notes |
|-------:|-----:|------|-------|-------|
| +0xB8 | 4 | i32 | `glyph_width_step` | Per-glyph / per-character pixel width used in the text-centering layout (`3 × value`; `value × stanceScale × 3.0`, `stanceScale ∈ {1.0, 2.7}`). Confidence: high (4-byte int + layout-centering role). |

> **Implementation note.** A C# reimplementation can model the child store as a managed
> `List<GUComponent>` (the three-pointer triple is just the MSVC vector internals) and the
> active-child as an `int` index defaulting to −1.

---

## 4. Capability-flags word at +0x08 (CODE-CONFIRMED)

The 32-bit `flags` field at +0x08 accumulates one OR-set bit per class as construction walks the
inheritance chain — a base bit, a container bit, and one per-leaf "kind" tag. The confirmed bits:

| Bit | Mask | Set by | Meaning |
|----:|-----:|--------|---------|
| 0 | 0x0001 | GUComponent ctor | "is a component" / initialised |
| 2 | 0x0004 | GUPanel ctor | "is a panel" (container) |
| 3 | 0x0008 | GUButton ctor | "is a button" (leaf kind tag) |
| 7 | 0x0080 | GULabel ctor | "is a label" (leaf kind tag) |
| 9 | 0x0200 | GUList ctor | "is a list" (leaf kind tag) |
| 13 | 0x2000 | GUWindow ctor | "is a top-level window" (see `structs/guwindow.md`) |

The leaf bits (button 0x8, label 0x80, list 0x200) are accumulated up the chain alongside the
base/container/window bits; additional leaf classes set further bits. A reimplementation can model
these as a `[Flags]` enum; the literal masks are the load-bearing interoperability facts when
round-tripping legacy state.

---

## 5. Sentinels and the 3000 constant (CODE-CONFIRMED)

- **−1 sentinels** at +0x10 (`action_id`, "no action") and +0xB4 (GUPanel `active_child`, "no child
  selected") mark "unset / none".
- **+0x0C = 0xFFFFFFFF** default = white tint with the forced-alpha override (+0x0F) disabled.
- **+0x9C = 3000** is the per-component default **auto-hide timeout in milliseconds** set by the
  ctor (now confirmed as a timeout, not a z-order — see §2 and §7).

---

## 6. GUComponent primary vtable — 13 slots (CODE-CONFIRMED)

The base class exposes exactly **13 virtual slots** (indices 0..12) — re-confirmed CYCLE 7 (IDB SHA
263bd994). `GUPanel` adds slot 13 (→ 14 total) and `GUWindow` adds slot 14 (→ 15 total), the
"LAYERED 13/14/15" chain. Roles, described abstractly (no addresses):

| Slot | Role (one line) |
|-----:|-----------------|
| 0 | Scalar-deleting destructor (frees when the deleting flag is set). |
| 1 | `setVisible(bool)` — sets the show byte +0x8C (and co-writes +0x8D), snaps the +0x04 alpha, arms the auto-hide timer (+0x95/+0x98). |
| 2 | `setPosition(x, y)` — writes +0x24/+0x28 and mirrors the copies +0x34/+0x38. |
| 3 | `getPosition` — reads +0x24/+0x28. |
| 4 | `hitTest` by point vector (thin wrapper over slot 5). |
| 5 | `hitTest(x, y)` — rect test against world rect `[world_x, world_x+width] × [world_y, world_y+height]`, toggles hover +0x88, fires enter/leave (slots 11/12). |
| 6 | `onEvent` — press/release capture and action dispatch, gated by the interactive flag +0x8A. |
| 7 | `draw` — alpha-fade toward +0x8C, apply tint/forced-alpha (+0x0C/+0x0F), submit the bound handle +0x90 with the transform +0x44, and evaluate the auto-hide timer (+0x95/+0x98/+0x9C, callback +0xA0). |
| 8 | `onUpdate` — recompute extents +0x3C/+0x40 and the src-rect copies +0x34/+0x38, then call slot 9. |
| 9 | `computeTransform` — world coords +0x2C/+0x30 (adding the parent's world via +0x84) and the D3D matrix at +0x44. |
| 10 | `getHitActionId` — returns the action id +0x10. |
| 11 | `onMouseEnter` — empty stub in the base. |
| 12 | `onMouseLeave` — empty stub in the base. |

GUPanel installs its own vtable and overrides several of these (notably the draw / update / child
behaviour); its child-removal sweep (which reads +0x8D == 1 to remove a child and clears +0x8D on the
survivors) lives in the GUPanel vtable. The full GUPanel vtable is out of this struct's scope.

---

## 7. Packing / alignment (CODE-CONFIRMED)

Natural **4-byte alignment** (legacy MSVC default). Every 4-byte field sits on a 4-aligned offset;
the status/flag bytes are packed into the +0x88..+0x8D and +0x95 ranges with no forced gap removal,
while +0xB8 is a 4-aligned 4-byte int (the glyph-width step, §3.1) — not a status byte. A C# mirror
should use `StructLayout(Sequential)` with **natural alignment** — **not** `Pack = 1` (unlike the
wire/asset structs, which are byte-packed).

---

## 8. Residual runtime items (capture/debugger-pending)

These are the only genuinely runtime-dependent items; the campaign is static-only and everything
above is control-flow-confirmed.

- **+0x8D dual semantics.** The size (1 byte), the boolean nature, and the dedicated setter/getter
  pair are **CODE-CONFIRMED (CYCLE 7)**; only the exact semantic is pending. The panel-build path
  compares +0x8D **== 1** to gate a branch and forces it to **0** at one site; the panel
  child-removal sweep treats +0x8D **== 1** as "remove this child" (and clears it on survivors),
  which supports a `remove_mark` reading. But `setVisible` **also co-writes +0x8D with its visible
  argument**, so showing a widget would set +0x8D = 1. Whether the build-path toggle means
  enable/clip or "pending state change / removal", and how these writers coexist, needs runtime
  confirmation on a live session.
- **+0xB8 — RESOLVED (CYCLE 7), no longer pending.** Previously logged here as an unconfirmed
  `panel_kind` subclass byte. It is now CODE-CONFIRMED as a **4-byte signed integer glyph/char-width
  step** used by the text-centering layout (§3.1). It is neither a pointer nor a one-byte kind tag;
  the prior reading is superseded.

---

## Cross-references

- Top-level window layout (multiple inheritance): `structs/guwindow.md`
- Widget behaviour, vtable slots, draw/hit-test/fade, the leaf-widget catalogue: `specs/ui_system.md`
- Window-manager doctrine + service-slot table (the MainMaster / MainWindow manager and its
  service-slot table; note that pushing a child onto a per-scene std::list is a **dispose/teardown**
  registration, not manager attachment): `specs/ui_system.md`, `structs/runtime_singletons.md §3.10`
- UI event-record types and dispatch order: `specs/ui_system.md`
- Glossary: `Docs/RE/names.yaml`
- Provenance: `Docs/RE/journal.md`
