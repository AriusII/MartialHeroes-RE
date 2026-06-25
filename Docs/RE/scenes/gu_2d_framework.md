---
verification: written 2026-06-25 against doida.exe binary (IDB SHA 263bd994). Sources: dirty-room
  recovery notes from the GU framework and login-deep cartography passes (static IDA). Covers the
  shared Diamond::GU* 2D toolkit layer used by all three frontend scenes (Login, Load, CharSelect):
  texture-handle resolution, BuildImageComponent field contract, three alignment modes, and the
  15-slot HANGUL font table. CONFIRMED-LIVE this campaign: the BuildImageComponent rect contract
  (field writes verified element-level from the build pass). The font-slot registration (15 slots,
  CharSet 129, faces DotumChe/Dotum/BatangChe) is STATIC-CONFIRMED from the WinMain init sequence.
  All byte offsets are relative to the object start (interoperability facts, never memory addresses).
  Cross-referenced to: structs/gucomponent.md (GUComponent base layout), structs/guwindow.md
  (GUTextureList ownership), specs/ui_system.md (widget framework overview), scenes/login.md
  (login-scene atlas and label usage), scenes/charselect.md (charselect usage + 15-slot font summary).
scene: shared — all frontend scenes (engine states 1, 2, 3, 4)
evidence: [static-ida, debugger-confirmed-g2]
capture_verified: false
sources:
  - Docs/RE/structs/gucomponent.md      # GUComponent base field layout (+0x00..+0xA0)
  - Docs/RE/structs/guwindow.md         # GUWindow GUTextureList sub-object (+0x220 / window+4)
  - Docs/RE/specs/ui_system.md          # UI subsystem overview, widget framework, font singleton
  - Docs/RE/specs/frontend_scenes.md    # frontend scene placement (states 1..4)
  - Docs/RE/specs/vfs_overview.md       # VFS mount mechanics, TOC record, progress accumulator
  - Docs/RE/specs/resource_pipeline.md  # disk-fallback path, VFS-mounted path
  - Docs/RE/scenes/login.md             # login scene atlas usage and label build pattern
  - Docs/RE/scenes/charselect.md        # charselect atlas usage, 15-slot font table summary (§8.2)
---

# Diamond::GU 2D Framework — Shared Reference (Frontend Scenes)

> **Firewall-clean synthesis.** Rewritten in neutral prose from the committed `Docs/RE/` specs listed
> in the front matter and from dirty-room recovery notes (static IDA). Contains **no decompiler
> pseudo-C, no decompiler autonames, and no binary virtual addresses.** All byte offsets are
> interoperability facts (offsets relative to an object start). Korean text is **CP949** throughout.

---

## 1. Overview

The three frontend scenes — Login (engine state 1), Opening (state 3), and Character Select (state 4)
— all build their 2D widgets on top of the same shared in-house retained-mode toolkit: the
`Diamond::GU*` widget family. The toolkit has four load-bearing shared mechanisms that this dossier
documents in full:

1. **Texture-handle resolution** — how a VFS file path becomes a handle used by image components.
2. **`BuildImageComponent` contract** — the canonical image-widget builder; every other builder
   (panel, button, label) delegates to it.
3. **Alignment modes** — three text/component positioning modes driven by `GUComponent_PositionByAlignment`
   and `GULabel_SetTextAndAlign`.
4. **Font table** — the 15-slot process-global D3DX font table, registered once in `WinMain` before
   any scene runs.

The `GUComponent` base field layout (vtable, alpha, flags, color, action-id, position, size, extents,
matrix, status bytes, drawable handle) is documented in `structs/gucomponent.md`. The `GUWindow`
multiple-inheritance layout and the `GUTextureList` sub-object are in `structs/guwindow.md`. This
dossier covers only the framework-level mechanics; per-scene widget trees, atlas paths, and geometry
tables live in the per-scene dossiers (`scenes/login.md`, `scenes/charselect.md`).

---

## 2. Texture-handle resolution

### 2.1 Per-window GUTextureList

Each `GUWindow`-derived scene root owns a **`GUTextureList`** sub-object (documented in
`structs/guwindow.md`; located at window byte **+4**, i.e. the field immediately after the primary
vtable pointer). This sub-object is a growable vector of loaded texture records. The texture
loader (`Texture_LoadFromVfsOrDisk`) — called once per atlas during a scene's init sequence —
**push_backs** a new record into that vector on success and returns the new entry's **vector index**
as the texture handle.

### 2.2 Handle = load-order (the cardinal rule)

Because handles are produced by sequential `push_back`, the **N-th `Texture_LoadFromVfsOrDisk`
call** in a scene's init yields **handle N** (0-based). This returned integer is exactly what is
later passed as the texture-handle argument to `BuildImageComponent` and to the label builder
(`Diamond_GULabel_BuildText`). To recover which file an image component draws from: count the
texture-load calls in that scene's init in order; the handle equals the call ordinal. There is no
deduplication — the same atlas path may be loaded twice and receive two distinct handles (confirmed:
`InventWindow.dds` is loaded twice in the CharSelect build, see `scenes/charselect.md §5.1`).

### 2.3 VFS-path resolution (`Texture_LoadFromVfsOrDisk`)

The loader branches on whether the VFS archive is mounted (see `specs/vfs_overview.md §4`):

**VFS mounted (normal runtime path):**

1. Constructs a transient VFS manager object.
2. Calls `Vfs_FindAndReadEntry(descriptor, sourcePath)`, which:
   - Lowercases `sourcePath` into a 100-byte stack buffer. **All VFS lookups are case-insensitive
     (path is lowercased before the search).**
   - Binary-searches the sorted TOC (loaded at VFS mount time, stride **144 bytes per record**) by
     `strcmp` against the lowercased path.
   - On a hit, allocates a heap buffer (`malloc(size)`), takes a global critical section,
     `SetFilePointerEx`s to the entry's 64-bit offset in the archive, and `ReadFile`s the raw bytes.
     Updates a global progress accumulator when a progress flag is set (this drives the loading-bar
     during the load scene).
   - Returns a `{ptr, size}` descriptor in the caller's stack frame.
3. Passes the in-memory blob to the D3D texture factory to produce the GPU texture object.
4. On success: `push_back`s the record into the window's `GUTextureList` and returns the new index.
   On failure: frees the blob and returns **0**.

**VFS not mounted (disk fallback):**

Calls the direct-disk texture factory with `sourcePath` as the file path. On success: `push_back`s
and returns the index. On failure: returns **0**.

The literal VFS path string (e.g. `data/ui/loginwindow.dds`) **is** the VFS lookup key — no path
transform is applied beyond the mandatory lowercasing. See `specs/vfs_overview.md` and
`specs/resource_pipeline.md §1.5` for the mount mechanics and TOC record layout.

---

## 3. `BuildImageComponent` contract

`Diamond_GU_BuildImageComponent` is the base image-widget builder. Every other widget builder in
the frontend scenes — panels, 3-state buttons, and labels — delegates to it, so its argument
contract and field map are the canonical template for the entire 2D widget ABI.

### 3.1 Argument order (CONFIRMED-LIVE this campaign)

```
BuildImageComponent(this, texHandle, srcX, srcY, w, h, dstX, dstY, color)
```

| Argument | Meaning |
|---|---|
| `this` | the component object being built (the callee writes directly into it) |
| `texHandle` | index into the owning window's `GUTextureList` (from `Texture_LoadFromVfsOrDisk`) |
| `srcX` | source X in the atlas texture (top-left of the source sub-rect) |
| `srcY` | source Y in the atlas texture (top-left of the source sub-rect) |
| `w` | width in pixels (shared by source and destination rect — 1:1 unscaled blit by default) |
| `h` | height in pixels (shared by source and destination rect) |
| `dstX` | destination X on screen (top-left of the draw rect) |
| `dstY` | destination Y on screen (top-left of the draw rect) |
| `color` | ARGB tint word; **`0xFFFFFFFF` = no tint** (full-opacity white = pass-through) |

> **Prior ABI mis-readings (corrected by the CYCLE 10 completeness pass).** Two earlier ABI
> readings circulated in dirty-room notes and must not be ported: (a) `(tex, x, y, w, h, srcX,
> srcY, action)` — has `w/h` and `srcX/srcY` swapped; (b) `(sentinel −1, dstX, dstY, srcX, srcY,
> w, h, atlas)` — the leading `−1` is the builder's internal `+0x10` field default, not an
> argument, and the texture handle is **argument 1**, not the last. The order above is the one
> confirmed by reading the field writes in both the panel builder and a concrete `InventWindow.dds`
> modal call. See `scenes/charselect.md §4.2`.

### 3.2 Field map (byte offsets relative to the component object start)

The builder writes these fields. DWORD indices are given for cross-reference with the dirty-room
notes (`idx N` = byte offset `4*N`).

| Byte offset | DWORD idx | Field | Written value |
|---:|---:|---|---|
| +0x00 | 0 | vtable pointer | `GUComponent::vftable` |
| +0x04 | 1 | alpha | 255 |
| +0x08 | 2 | flags | 0 initially; bit 0 set (`\|= 1`) at the end of the builder |
| +0x0C | 3 | **color** | `color` argument |
| +0x10 | 4 | action-id / handle | −1 (default sentinel) |
| +0x14 | 5 | **srcX** | `srcX` argument (atlas source-rect left edge) |
| +0x18 | 6 | **srcY** | `srcY` argument (atlas source-rect top edge) |
| +0x1C | 7 | **w** | `w` argument (draw width) |
| +0x20 | 8 | **h** | `h` argument (draw height) |
| +0x24 | 9 | **dstX** (position) | `dstX` argument — live on-screen X; written by `setPosition` |
| +0x28 | 10 | **dstY** (position) | `dstY` argument — live on-screen Y; written by `setPosition` |
| +0x34 | 13 | dstX (position mirror) | `dstX` argument; written by `setPosition` |
| +0x38 | 14 | dstY (position mirror) | `dstY` argument; written by `setPosition` |
| +0x3C | 15 | **right** (dest rect right edge) | `dstX + w` |
| +0x40 | 16 | **bottom** (dest rect bottom edge) | `dstY + h` |
| +0x84 | 33 | sub-state | 0 |
| +0x88 | byte 136 | flag byte | 0 |
| +0x8A | byte 138 | flag byte | 1 |
| +0x8B | byte 139 | flag byte | 0 |
| +0x8C | byte 140 | flag byte | 1 |
| +0x8D | byte 141 | flag byte | 0 |
| +0x90 | 36 | **texHandle** | `texHandle` argument (window `GUTextureList` index) |
| +0x95 | byte 149 | flag byte | 0 |
| +0x98 | 38 | (init) | 0 |
| +0x9C | 39 | z / draw order | 3000 (constant) |
| +0xA0 | 40 | (init) | 0 |

Field semantics:

- **`+0x24` / `+0x28` (`dstX`, `dstY`)** — the component's on-screen draw position (top-left
  corner of the destination rectangle), and exactly the fields the literal `setPosition` path
  (§3.4 below) writes — which is what pins them as the destination position rather than the size.
  `+0x34` / `+0x38` mirror the same `(dstX, dstY)`.
- **`+0x1C` / `+0x20` (`w`, `h`)** — the draw-rect size, shared by the source and destination rects.
- **`+0x3C` / `+0x40` (right, bottom)** — the lower-right corner of the **destination** rect:
  `right = dstX + w`, `bottom = dstY + h`.
- **`+0x14` / `+0x18` (`srcX`, `srcY`)** — the atlas source sub-rect origin; the source rect is
  `[srcX, srcY]..[srcX+w, srcY+h]`.
- **`+0x90` (texHandle)** — the index into the owning window's `GUTextureList`; resolved per §2.

### 3.3 Source → destination rect model

The component draws the atlas **sub-rectangle** `[srcX, srcY]..[srcX+w, srcY+h]` into the **screen
rectangle** `[dstX, dstY]..[dstX+w, dstY+h]`, tinted by `color`. Because source and destination
share the same `(w, h)` pair, the default is a **1:1 unscaled blit**. Scaling requires an external
mutation of the live width/height fields after construction. With `srcX = srcY = 0` the entire
atlas tile from its top-left corner is blitted; non-zero `srcX`/`srcY` select a sub-tile (an atlas
sprite).

### 3.4 Literal position setter (`Diamond_GUComponent__setPosition`)

`Diamond_GUComponent__setPosition` (vtable slot 2) is the simple literal position override. It
writes `x` → `+0x24` and `y` → `+0x28` (the live position fields) **and** mirrors them to
`+0x34` and `+0x38` (the base position fields) — both pairs are set simultaneously, with no
alignment math. Scenes use this when they already hold final screen coordinates and do not need
the center-math path.

---

## 4. Alignment modes

`GUComponent_PositionByAlignment` computes final positioned coordinates from a base position and an
alignment mode, writing the result to the component's positioned-coordinate fields at byte **+0xEC**
(x) and **+0xF0** (y). It first runs a measure helper on `this+41` (a member also used by the label
path). Let `compW = idx7` (the `srcX` field, reused as a width metric in this context), `compH = idx8`,
and `idx46` = the alignment metric at `+0xB8` (a per-glyph pixel width step, resolved from the font;
see `structs/gucomponent.md` for the field's `SIGNED INT` type note).

| Mode (`a3`) | Name | Positioned x (`+0xEC`) | Positioned y (`+0xF0`) |
|---:|---|---|---|
| 0, 1, or any other | Left / literal | `baseX` (verbatim) | `baseY + 4` |
| 2 | Center | `baseX + compW/2 − 3·idx46` | `compH/2 + baseY − 6` |
| 3 | Center-right | `compW/2 + compW/3 − 3·idx46` | `compH/2 − 6` |

Plain-language description:

- **Mode 0/1 (left / literal):** place the component at `(baseX, baseY+4)` — no centering, +4 px
  vertical nudge only.
- **Mode 2 (center):** horizontally center within the component width starting from `baseX`, pulled
  left by `3·idx46` (approximately 3 character-advance widths). Vertically centered within `compH`
  relative to `baseY`, −6 px nudge.
- **Mode 3 (center-right):** a stronger horizontal push (`compW/2 + compW/3 ≈ 5/6 of compW`) pulled
  left by `3·idx46`. The `baseY` argument does not affect the x computation in this mode; y is
  `compH/2 − 6` (independent of `baseY`).

### 4.1 Label text + alignment setter (`GULabel_SetTextAndAlign`)

`GULabel_SetTextAndAlign` is the label-specific counterpart, taking `(this, textString, alignMode,
baseY)`. It assigns the string into the label's text member (at byte **+0xA4**), optionally
ellipsizes, then applies the same three-mode alignment math — writing computed x to byte **+220**
and y to byte **+224** (the label's positioned draw coordinates) and storing the mode at byte
**+148**:

- **Mode 0/1/other (literal):** no center math; the `dstX`/`dstY` values placed by `BuildImageComponent`
  (§3.2) remain in effect.
- **Mode 2 (center):** `x = compW/2 − measureMetric·scale·3`; `y = compH/2 + baseY − 6`.
  `scale = 1.0` normally, `2.7` when a wide-mode flag at byte `+228` equals 2.
- **Mode 3 (center-right):** `x = compW − (2·measureMetric·scale·3 + 4)` (same scale rule);
  `y = compH/2 + baseY − 6`. `measureMetric` is the label's measured advance at byte **+184**
  (`idx46`) — the same alignment metric `GUComponent_PositionByAlignment` uses.

---

## 5. GULabel — text component build path

A `GULabel` is the text-rendering widget. Its RTTI class is `Diamond::GULabel`; it derives from
`GUComponent`, adding two `std::string` members and a font-slot reference.

### 5.1 `Diamond_GULabel_BuildText` (the parameterised constructor)

`Diamond_GULabel_BuildText` (proposed canonical name; the parameterised label ctor) **delegates
directly to `BuildImageComponent`** with the same nine-argument shape:

```
Diamond_GULabel_BuildText(this, texHandle, dstX, dstY, srcX, srcY, w, h, color)
```

For a pure-text label, `texHandle` is typically **−1** or **0** (no backing texture atlas needed;
the font renderer draws the glyphs directly). `dstX/dstY` and `w/h` establish the text bounding
box exactly as they do for an image component.

After the base `BuildImageComponent` call the ctor:

- Overrides the vtable pointer to `GULabel::vftable` (making it a label).
- Constructs two `std::string` members: the primary text string at byte **+0xA4** (`idx41`) and an
  ellipsized/secondary string at byte **+0xC0** (`idx48`).
- Zeroes label state bytes at `+220`, `+224`, `+228`, `+232`, `+233`, `+236`.
- Sets flag bit `idx2 |= 0x80` (the "is-text" flag in the flags word at `+0x08`).

Because the label's screen rect and color are laid out by the same `BuildImageComponent` call, the
**label's 2D placement contract is identical to the image-component contract** (§3).

### 5.2 Font slot reference (`Diamond_GULabel_SetFontSlot`)

`Diamond_GULabel_SetFontSlot` writes the chosen slot index into byte **+0xE4** (`idx57`). The
default after construction is **slot 0**. At draw time the label renderer picks the `D3DXFont`
object from the global font table using this index and renders the `+0xA4` string through it.

### 5.3 Full build pattern

The canonical scene pattern for a text label:

1. Allocate the label object, call `Diamond_GULabel_BuildText` to establish the bounding rect and
   color.
2. Call `Diamond_GULabel_SetFontSlot(slotIndex)` to select the typeface.
3. Fetch the CP949 caption from `msg.xdb` by numeric id
   (`MessageDB_GetString(id)` → a `std::string`).
4. Call `GULabel_SetTextAndAlign(label, string, alignMode, baseY)` to assign the text and apply
   alignment.
5. Call `AddChild(parent, label)` to attach to the parent panel (insertion order = paint order).

The `AddChildWithAction` variant additionally writes `child + 0x10 = actionId` (the action-id
field at `+0x10`, GUComponent DWORD idx 4) and stores the child pointer into the owning window's
bound-member slot (see `scenes/charselect.md §4.3` for the bound-member roster).

---

## 6. Font table — 15 slots, HANGUL_CHARSET

### 6.1 Process-global font singleton

A process-global font singleton (`FontTable_GetSingleton`) holds a fixed array of **15 font slots**
registered once in `WinMain` before any scene constructs. The singleton is lazily initialised on
first access and torn down on process exit. Each slot record is **88 DWORDs (352 bytes, 0x160 bytes)
wide**; slot `N` begins at `tableBase + N · 88` DWORD positions.

### 6.2 Slot registration

`FontTable_RegisterSlot(tablePtr, slotIndex, faceName, nominalSize, width, height, weight)`:

1. Releases any prior font object at `slot + 18 DWORDs` (frees the old face-name heap buffer).
2. Copies `faceName` into a heap buffer stored at `slot + 18 DWORDs`.
3. Writes:
   - `slot + 48 DWORDs` = `width ? width : nominalSize` (effective character width)
   - `slot + 63 DWORDs` = `height ? height : nominalSize` (effective character height)
   - `slot + 33 DWORDs` = `nominalSize`
   - `slot + 78 DWORDs` = `weight`
4. Calls `D3DXCreateFontA(device, Height=height, Width=width, Weight=weight, MipLevels=1,
   Italic=0, CharSet=129, OutputPrecision=0, Quality=0, PitchAndFamily=1, FaceName=faceName,
   &slotFont)`.

**`CharSet = 129 = HANGEUL_CHARSET`** on every slot — the entire UI text pipeline is Korean /
**CP949**. The two font APIs imported are `D3DXCreateFontA` (used here) and `CreateFontIndirectA`
(the GDI path, used elsewhere). Face name strings are the ASCII names below; no Korean-encoded face
name string is used.

### 6.3 The 15 registered slots (indices 0..14)

Registered in `WinMain` (the `OPENNING/SKIP` init case, font-bring-up block) before any scene
runs. All three frontend scenes therefore share the complete table from the moment they build.

| Slot | Face | nominalSize | width | height | weight | Notes |
|---:|---|---:|---:|---:|---|---|
| 0 | DotumChe | 12 | 6 | 12 | 0 | default label face; `GULabel` slot default |
| 1 | Dotum | 10 | 5 | 10 | 0 | |
| 2 | DotumChe | 32 | 16 | 32 | 800 | large/heavy; CharSelect slot-count label |
| 3 | DotumChe | 18 | 12 | 24 | 800 | |
| 4 | DotumChe | 12 | 6 | 12 | 800 | bold small DotumChe; CharSelect msg-2206 label |
| 5 | BatangChe | 12 | 6 | 12 | 0 | |
| 6 | BatangChe | 18 | 12 | 24 | 700 | |
| 7 | BatangChe | 12 | 6 | 12 | 700 | |
| 8 | BatangChe | 12 | 6 | 12 | 700 | |
| 9 | DotumChe | 12 | 6 | 12 | 700 | |
| 10 | Dotum | 16 | 10 | 20 | 800 | |
| 11 | DotumChe | 10 | 5 | 10 | 400 | |
| 12 | DotumChe | 12 | 6 | 12 | 400 | |
| 13 | DotumChe | 14 | 7 | 14 | 400 | |
| 14 | DotumChe | 16 | 8 | 16 | 400 | |

`D3DXCreateFontA` argument semantics: `Height = height`, `Width = width`, `Weight = weight` (GDI
weight; 0 = default / system, 400 = normal, 700 = bold, 800 = extra-bold). Whether slots beyond
index 14 are registered elsewhere is unconfirmed (none observed in the `WinMain` init pass).

---

## 7. C# / Godot port notes

These notes apply to **all** layers of the port; they do not override the general architecture
rules documented in `CLAUDE.md`.

### 7.1 Layer placement

- The font table, VFS resolution, and widget field layout are **pure interoperability data** that
  lives in layers 01–04. Layer 05 (Godot) is **passive rendering only** — it reads the field
  values but applies no game-rule logic.
- Nothing in layers 01–04 may carry `using Godot;`. The widget ABI is reconstructed as C# structs /
  records in the appropriate Core layer; Godot nodes consume the results.
- Dependency edges flow strictly downward; layer 05 never references a higher-numbered project.

### 7.2 VFS path convention

The literal string passed to `Texture_LoadFromVfsOrDisk` (e.g. `data/ui/loginwindow.dds`) is the
**VFS key after mandatory lowercasing**. The port must resolve atlas textures by the same lowercased
key against the mounted `Assets.Vfs` index. This is the chain documented in
`specs/vfs_overview.md §4` and `specs/resource_pipeline.md §1.5`.

### 7.3 Godot pitfalls (avoid these)

- **`.tscn` script bindings** must be a **property line** under the node header (`script =
  ExtResource("1")`), not an inline header attribute — the inline form is silently ignored, leaving
  the node with no script and no `_Ready`.
- **Namespace collisions** inside `namespace MartialHeroes.Client.Godot.*`: bare `Input.`,
  `Time.`, and `Environment.` resolve to the sibling project namespace, not the Godot class. Use
  `global::Godot.Input`, `global::Godot.Time`, `global::Godot.Environment`.
- **Never use `GltfDocument.AppendFromBuffer`** for generated GLBs — it crashes natively on this
  project's output. Build `ArrayMesh` directly instead.

### 7.4 `EnvLogin` auto-fill

The `EnvLogin` environment variable is consumed by the login scene's real credential-entry path to
pre-fill the ID/password fields in development. This is **real client behaviour**, not a stub;
preserve it in the port.

---

## 8. Open items

- **Alignment metric `idx46` (+0xB8) identity** — confirmed as a 4-byte signed int holding the
  per-glyph pixel width step for text centering (see `structs/gucomponent.md` conflicts note);
  whether it is seeded by `D3DXCreateFontA` glyph metrics or by a fixed font-slot field is
  unconfirmed. Consequence for the port: verify centering visually against the official captures
  (`godot-fidelity-check`).
- **Whether font slots beyond index 14 are registered** at another site in the binary is unconfirmed
  (none seen in the `WinMain` init pass).
- **Exact CP949 string content** behind `msg.xdb` numeric ids referenced in the frontend scenes —
  extract via the asset-format lane from the on-disk `data/script/msg.xdb`.

---

## 9. Cross-references

| Topic | Spec |
|---|---|
| `GUComponent` and `GUPanel` byte-offset layout; `+0xB8` signed-int type note | [`structs/gucomponent.md`](../structs/gucomponent.md) |
| `GUWindow` multiple-inheritance layout; `GUTextureList` at window `+4` / `+0x220` | [`structs/guwindow.md`](../structs/guwindow.md) |
| UI subsystem overview (widget framework, `msg.xdb`, action dispatch, font singleton lifecycle) | [`specs/ui_system.md`](../specs/ui_system.md) |
| VFS TOC layout, 144-byte stride, mount mechanics, progress accumulator | [`specs/vfs_overview.md`](../specs/vfs_overview.md) |
| Disk-fallback path; `data.vfs` raw-read (not memory-mapped) | [`specs/resource_pipeline.md`](../specs/resource_pipeline.md) |
| Login scene: atlas list, label build pattern, 15-slot font table usage | [`scenes/login.md`](login.md) |
| CharSelect scene: atlas list, `BuildImageComponent` ABI corrections, 15-slot font summary §8.2 | [`scenes/charselect.md`](charselect.md) |
| Frontend scene state machine (engine states 1..4); `OPENNING/SKIP` gate | [`specs/frontend_scenes.md`](../specs/frontend_scenes.md) |
