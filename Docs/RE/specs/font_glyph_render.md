# Spec: Font and Glyph Render — FontTable, System Font Draw Pipeline, and Text Baking

> Clean-room spec. Neutral description only — no decompiler pseudo-code, no binary addresses,
> no decompiler-generated identifiers. Promoted from dirty-room static analysis under EU
> Software Directive 2009/24/EC Art. 6, solely to achieve interoperability. This spec
> documents how the client creates and manages a fixed table of Direct3D 9 `ID3DXFont` faces
> (`FontTable`), the single shared text draw entry that every HUD overlay and GUI widget
> funnels through, the CP949 column-counting helpers used for layout and centering, and the
> secondary GDI path that bakes text into in-memory image buffers rather than drawing to the
> live screen.
>
> **Out of scope for this spec:** the damage-number bitmap-glyph atlas
> (att-font.dds / cri-font.dds / miss.tga, 12-cell UV table, JointXEffect billboards) — that
> subsystem is fully owned by `Docs/RE/specs/nameplate_render.md §3` and is referenced here
> only to clarify the scope boundary. The frame pass ordering that situates text draws in the
> pipeline is owned by `Docs/RE/specs/render_pipeline.md` and
> `Docs/RE/specs/nameplate_render.md §4.1`.

> **Verification banner**
> - **verification:** *static-confirmed* for the non-existence of a bespoke glyph-atlas or
>   `.fnt`-metrics subsystem for general text; the `ID3DXFont` / `D3DXCreateFontA` mechanism
>   as the sole HUD/widget text facility; the `FontTable` singleton lifecycle
>   (`FontTable_GetSingleton`, `Diamond_FontTable_Construct`, `Font_CreateD3DXFontFace`) and
>   build site (`WinMain_SceneStateMachine` case 1); the full structure-of-arrays object layout
>   (all six array bases, dword stride, ~372-byte footprint); the exact 15-slot
>   face / pointSize / advance / lineHeight / weight table; `D3DXCreateFontA` parameters
>   (`CharSet=129` HANGUL_CHARSET, PitchAndFamily=1, Quality=0); CP949 decode delegated to
>   GDI with no app-side glyph-index map; the shared draw entry `GUWidget_DrawTextInRect` and
>   its `ID3DXFont::DrawTextA` call signature (`pSprite=NULL`, `count=-1`,
>   `format=DT_NOCLIP=0x100`); the draw-rect math; `Diamond_Text_IsSingleByteChar`,
>   `Diamond_Text_MeasureCp949Width`, `Diamond_GULabel_ComputeCenteredX`, and
>   `Diamond_GULabel_SetFontSlot`; GULabel font-slot field at byte +228; the full caller set
>   (nameplate / HUD overlay helpers + GU widget layer); and the secondary GDI image-bake path
>   (CreateFontIndirectA + USER32 DrawTextA → 24-bpp DIBSection → CxImage pixel copy). Items
>   tagged `[debugger-confirm]` are static hypotheses awaiting a live `?ext=dbg` session before
>   treating them as implementation facts.
> - **ida_anchor:** f61f66a9ae0ec1e946105b2ecff76e8930cb1d1367df64e5688a5266f5ad9963
> - **readiness:** IMPLEMENTATION-READY for the `ID3DXFont` FontTable creation, the shared draw
>   entry and its call signature, draw-rect math, the full 15-slot table, CP949 column counting,
>   centering formula, and the GDI image-bake path standard variant. The multi-pass
>   outline/shadow recipe of the image-bake outline variant and its caller set are
>   `[open question]`. DT_NOCLIP exclusivity across all call sites is `[debugger-confirm]`.
> - **evidence:** [static-ida]
> - **cross-links:**
>   `Docs/RE/specs/nameplate_render.md §3` (damage-number bitmap-glyph atlas — owned there);
>   `Docs/RE/specs/nameplate_render.md §2.4` (CP949 text wrapping, 14 px line height, call to
>   `Diamond_Text_IsSingleByteChar`);
>   `Docs/RE/specs/nameplate_render.md §4.1` (pass order — 2D HUD after 3D scene; text drawn
>   depth-disabled);
>   `Docs/RE/specs/render_pipeline.md` (frame pass sequence);
>   `Docs/RE/specs/ui_hud_layout.md` (GU widget hierarchy and gauge atlas wiring);
>   `Docs/RE/specs/gui_framework.md` (GUComponent / GULabel widget class layout).

---

## Status

| Item | Confidence |
|---|---|
| No bespoke glyph atlas / `.fnt` subsystem for general HUD/world text — `ID3DXFont` only | CONFIRMED |
| `FontTable` singleton — 15 pre-created `ID3DXFont*` faces, built once at D3D device init | CONFIRMED |
| `FontTable` SoA layout: handle +0 / name-ptr +72 / pointSize +132 / advance +192 / lineHeight +252 / weight +312 | CONFIRMED |
| Exact 15-slot face / pointSize / advance / lineHeight / weight table | CONFIRMED |
| `D3DXCreateFontA` from `d3dx9_42.dll`: CharSet=129 (HANGUL_CHARSET), PitchAndFamily=1, Quality=0, Italic=0 | CONFIRMED |
| CP949 decode delegated to GDI — no app-side CP949→glyph-index map | CONFIRMED |
| Shared draw entry `GUWidget_DrawTextInRect` → `ID3DXFont::DrawTextA(NULL, str, -1, rect, DT_NOCLIP, color)` | CONFIRMED |
| Draw-rect math: right = x + advance[slot]·strlen(bytes); bottom = y + lineHeight[slot]·lineCount | CONFIRMED |
| `Diamond_Text_IsSingleByteChar` — single-byte test: byte < 0x80 | CONFIRMED |
| `Diamond_Text_MeasureCp949Width` — CP949 column counter for layout | CONFIRMED |
| `Diamond_GULabel_ComputeCenteredX` — centering formula using CP949 column count and advance | CONFIRMED |
| `Diamond_GULabel_SetFontSlot` — writes the label font-slot field at byte offset +228 | CONFIRMED |
| Caller set: nameplate / HUD overlay helpers + full GU widget layer (button, label, textbox, checkbox) | CONFIRMED |
| Damage-number bitmap atlas (att-font.dds / cri-font.dds / miss.tga) — owned by nameplate_render.md §3 | CONFIRMED |
| Secondary GDI image-bake path (standard variant): CreateFontIndirectA + DrawTextA → 24-bpp DIB → CxImage | CONFIRMED |
| Korean system fonts only (DotumChe / Dotum / BatangChe) — no font file shipped by the client | CONFIRMED |
| Global `IDirect3DDevice9` shared between FontTable and character cel-shaded draw path | [debugger-confirm] |
| DT_NOCLIP is the only format flag used across all `DrawTextA` call sites (no DT_CENTER / DT_RIGHT) | [debugger-confirm] |
| GDI-rendered cell metrics and kerning per face at each point size | [debugger-confirm] |
| Multi-pass outline/shadow recipe of the GDI image-bake outline variant | [open question] |
| Callers of the GDI image-bake path (which UI elements consume baked-text images) | [open question] |
| Slot→role mapping (which of the 15 slots each specific HUD/widget element uses) | [open question] |

---

## 1. Subsystem scope

The client has **no bespoke bitmap-font, glyph-atlas, or `.fnt`-metrics subsystem** for general
text. All in-world and HUD text — CP949 Korean and ASCII — is drawn through the **Direct3D 9
`ID3DXFont` facility** (`d3dx9_42.dll` `D3DXCreateFontA`), wrapped by a single app-side
singleton called the **`FontTable`**. The `FontTable` holds fifteen pre-created `ID3DXFont*`
face handles (slots 0..14) built once after D3D device initialisation. Every text draw in the
game — nameplates, overhead labels, chat, speech text, NPC titles, and every GUI widget
(buttons, labels, textboxes, checkboxes) — funnels through one shared entry point,
`GUWidget_DrawTextInRect`.

The glyph cache is opaque inside `d3dx9_42.dll`. `ID3DXFont` internally rasterizes GDI glyphs
into its own managed texture cache; the client never touches glyph bitmaps or UV coordinates
for HUD text. The app-visible font object IS the `FontTable` (fifteen `ID3DXFont*` handles
plus their metadata arrays).

A **secondary, separate** text path exists for baking text into in-memory image buffers (not
per-frame screen draws): a GDI rasterizer that creates a temporary `LOGFONTA`, calls
`CreateFontIndirectA`, renders via `DrawTextA` into a 24-bpp `DIBSection`, and copies lit pixels
into a `CxImage` object. This path is used to composite text onto in-memory image/texture
buffers — not to draw the live HUD.

The floating damage/heal/EXP number glyphs are the **only** true bitmap-glyph atlas in the
client, and they are fully documented in `Docs/RE/specs/nameplate_render.md §3`. They are not
re-derived here except by boundary reference.

**Graphics API: Direct3D 9.** The global `IDirect3DDevice9` used to create the `FontTable`
faces is shared with the character skinned cel-shaded draw path
(`Docs/RE/specs/character_rendering.md`). Its runtime value is `[debugger-confirm]`; statically
it reads as zero before device creation.

---

## 2. FontTable singleton

### 2.1 Lifecycle

`FontTable_GetSingleton` is the sole entry for obtaining the `FontTable` pointer. On first call
it tests an initialisation guard flag; if unset, it invokes `Diamond_FontTable_Construct` to
zero-initialise the fifteen `ID3DXFont*` handles and fifteen face-name pointers into static
storage, registers an `atexit` destructor, and sets the guard. Subsequent calls return the
already-constructed pointer directly.

The fifteen font faces are created in `WinMain_SceneStateMachine` (game-state case 1),
immediately after D3D device initialisation and before the login-scene loop begins. Fifteen
calls to `Font_CreateD3DXFontFace` populate all slots. If the device is later reset, the slot
handles can be rebuilt by the same creator function, which releases the prior `ID3DXFont` handle
(via its COM Release method) and frees the prior name string before overwriting both.

### 2.2 Structure-of-arrays object layout

The `FontTable` object is a **structure-of-arrays** of six parallel dword arrays, each holding
15 entries. With `slot ∈ 0..14` and `base` = the `FontTable` pointer:

| Byte offset of array base | Array contents | Notes |
|---|---|---|
| +0 | `ID3DXFont*` COM handle (one per slot) | Populated by `Font_CreateD3DXFontFace`; Released on rebuild |
| +72 | `char*` heap-copy of face name | Written by `Font_CreateD3DXFontFace` |
| +132 | Nominal point size (a4 parameter) | Informational; not passed to `DrawTextA` |
| **+192** | **Per-character advance / half-cell width** (a5, defaults to a4 if 0) | Used by `GUWidget_DrawTextInRect` for rect width |
| +252 | Line height (a6, defaults to a4 if 0); also the D3DX `Height` parameter | Used for rect height and glyph rasterization |
| +312 | GDI weight (a7) | Passed to `D3DXCreateFontA` as `Weight` |

Total object footprint: at minimum `(78 + 15) × 4 = 372 bytes`.

The per-character **advance at +192** is consistently ≈ lineHeight/2 (a half-cell). This design
means that CP949 byte-count arithmetic works directly: a one-byte ASCII character occupies one
advance unit; a two-byte CP949/Hangul character occupies two advance units (one full cell).

### 2.3 D3DXCreateFontA parameters

`Font_CreateD3DXFontFace` calls `D3DXCreateFontA` for each slot with:

| Parameter | Value | Notes |
|---|---|---|
| `pDevice` | global `IDirect3DDevice9` | Shared device; `[debugger-confirm]` runtime value |
| `Height` | slot lineHeight (+252) | GDI character cell height in logical units |
| `Width` | slot advance (+192) | GDI character width hint; 0 if not set |
| `Weight` | slot weight (+312) | FW_DONTCARE (0) / FW_NORMAL (400) / FW_BOLD (700) / FW_EXTRABOLD (800) |
| `MipLevels` | 1 | |
| `Italic` | 0 | |
| **`CharSet`** | **129 — HANGUL_CHARSET** | Instructs GDI to resolve CP949/EUC-KR double-byte sequences to Hangul glyphs |
| `OutputPrecision` | 0 — OUT_DEFAULT_PRECIS | |
| `Quality` | 0 — DEFAULT_QUALITY | |
| `PitchAndFamily` | 1 — DEFAULT_PITCH | |
| `pFaceName` | one of: "DotumChe", "Dotum", "BatangChe" | Windows-supplied Korean system fonts; no font file is shipped |

Because `CharSet=129`, GDI itself decodes the CP949 byte stream into Hangul code points and
selects the appropriate glyphs. The client needs no app-side CP949→glyph-index table.

### 2.4 The 15 font slots (exact)

Slot assignments as built in `WinMain_SceneStateMachine` case 1. Face names are GDI typeface
strings for Windows-supplied Korean fonts: `DotumChe` (돋움체, fixed-pitch gothic),
`Dotum` (돋움, proportional gothic), `BatangChe` (바탕체, fixed-pitch serif). The client ships
no font file; it relies on these fonts being present in the OS.

| Slot | Face | pointSize | advance (+192) | lineHeight (+252) | weight (+312) |
|---|---|---|---|---|---|
| 0 | DotumChe | 12 | 6 | 12 | 0 |
| 1 | Dotum | 10 | 5 | 10 | 0 |
| 2 | DotumChe | 32 | 16 | 32 | 800 |
| 3 | DotumChe | 18 | 12 | 24 | 800 |
| 4 | DotumChe | 12 | 6 | 12 | 800 |
| 5 | BatangChe | 12 | 6 | 12 | 0 |
| 6 | BatangChe | 18 | 12 | 24 | 700 |
| 7 | BatangChe | 12 | 6 | 12 | 700 |
| 8 | BatangChe | 12 | 6 | 12 | 700 |
| 9 | DotumChe | 12 | 6 | 12 | 700 |
| 10 | Dotum | 16 | 10 | 20 | 800 |
| 11 | DotumChe | 10 | 5 | 10 | 400 |
| 12 | DotumChe | 12 | 6 | 12 | 400 |
| 13 | DotumChe | 14 | 7 | 14 | 400 |
| 14 | DotumChe | 16 | 8 | 16 | 400 |

Weight 0 = FW_DONTCARE (GDI treats as normal weight). The slot→UI-role mapping — which
specific HUD element or widget class uses which slot number — is an `[open question]`
(§7.2 item 4).

---

## 3. Text draw pipeline

### 3.1 Shared draw entry — GUWidget_DrawTextInRect

`GUWidget_DrawTextInRect` is the single text draw entry for the entire game. Every nameplate
helper, HUD overlay function, and GU widget's draw method calls it. Its signature accepts the
`FontTable` pointer, screen position (x, y), the CP949/ASCII string, an ARGB colour, a slot
index (0..14), and a line count. Algorithm:

1. If the string pointer is null or the string is empty, return immediately (no-op).
2. Compute byte length: `len = strlen(str)` (CP949 **byte** count, not character count).
3. Build a `RECT`:
   - `left = x`, `top = y`
   - `right = x + advance[slot] × len`
   - `bottom = y + lineHeight[slot] × lineCount`
4. Call `ID3DXFont::DrawTextA` on the slot's font object:
   `DrawTextA(pSprite=NULL, str, count=-1, &rect, format=DT_NOCLIP, color)`

**Key call parameters:**
- `pSprite=NULL` — each call self-batches; no external `ID3DXSprite` state required.
- `count=-1` — NUL-terminated string; `DrawTextA` measures to the first null byte.
- `format=DT_NOCLIP (0x100)` — the only format flag used. Clipping is suppressed; the caller
  is responsible for positioning and any centering math. `[debugger-confirm]`: no call site has
  been observed passing a different format (e.g. DT_CENTER, DT_RIGHT), but a full sweep of all
  call sites is a remaining open item (§7.1 item 3).
- `color` — 32-bit ARGB, supplied by the caller. No outline or shadow mode exists inside this
  entry; callers that need outline/shadow redraw the string multiple times at pixel offsets in a
  dark colour, then at the main colour (see §3.4).

### 3.2 CP949 column counting and centering

The `FontTable` advance value at +192 is a **half-cell** width. Because a two-byte CP949
character occupies two bytes and two advance units, and a one-byte ASCII character occupies one
byte and one advance unit, multiplying byte count by advance gives the correct pixel width for
a fixed-pitch CP949 layout without needing character-count decoding in the caller.

Two helpers support layout:

**`Diamond_Text_IsSingleByteChar`** — returns true if `byte < 0x80`. Used by wrapping loops
to distinguish ASCII bytes (single-column) from CP949 lead bytes (opening a two-byte, two-column
sequence). Also used by `Docs/RE/specs/nameplate_render.md §2.4` for the 20-column text wrap.

**`Diamond_Text_MeasureCp949Width`** — returns the total CP949 display-column count of a
string (counting double-byte characters as two columns). Used when pixel-accurate centering is
needed rather than byte-count approximation.

**`Diamond_GULabel_ComputeCenteredX`** — computes a centered X coordinate for a label string:
1. Copy the label string (up to 512 bytes).
2. `n = Diamond_Text_MeasureCp949Width(str)` — column count.
3. Read the label's font slot: `slot = label[+228]` (see §3.3).
4. Return: `n × (advance[slot] + 1) − (n − 1) / 2`

This yields approximately half the estimated pixel width of the string, suitable for symmetric
centering around a reference x position.

### 3.3 Label font-slot field

`Diamond_GULabel_SetFontSlot` writes the label's font slot into the label object at **byte
offset +228** (this[57] as a dword field). `Diamond_GULabel_ComputeCenteredX` reads this field
to select the advance value for centering. This field is specific to the `GULabel` class; the
generic `GUComponent` leaf-widget class stores a separate font-slot field at a different offset
(+0xE8 / 232, along with a glyph-width-step field at +0xB8). The full `GUComponent` / `GULabel`
struct field map is an `[open question]` (§7.2 item 2); route to `Docs/RE/specs/gui_framework.md`
for the widget layout spec.

### 3.4 Colour, outline, and shadow

`GUWidget_DrawTextInRect` accepts one ARGB colour per call and has no outline or shadow mode.
Callers that render outlined or shadowed text issue multiple `GUWidget_DrawTextInRect` calls:
first with a dark/offset colour at shifted (x, y) positions (typically one-pixel displacement in
each direction), then the primary call with the main colour at the canonical position. The per-
overlay colour table is owned by `Docs/RE/specs/nameplate_render.md §2.6`.

### 3.5 Caller set

Every text draw in the game passes through `GUWidget_DrawTextInRect`. The confirmed caller
categories are:

**Nameplate and HUD overlay layer** (world-anchored text and overhead overlays):
- `Diamond_Hud_DrawActorNameLabel`
- `Diamond_Hud_DrawTargetActorLabel`
- `Diamond_Hud_DrawActorOverheadText`
- `Diamond_Hud_DrawWrappedSpeechText`
- `Diamond_Hud_DrawNpcTitleAndNamePlate`
- `Diamond_Hud_DrawWrappedTextAtWorldPos`
- `GUList_BuildNumberedPopup`
- Several additional HUD helper functions without recovered canonical names at the time of this
  pass (escalated to the RE domain for naming).

**GU widget layer** (immediate-mode UI controls):
- `Diamond_GUButton__onDraw`
- `Diamond_GULabel__onDraw`
- `Diamond_GUShortLabel__onDraw`
- `Diamond_GULabels__onDraw`
- `Diamond_GUTextbox__onDraw`
- `Diamond_GUCheckBox__onDraw`
- `Diamond_GUTextbox_DrawCaret` (caret draw; uses `GetTextExtentPoint32A` for caret position,
  then renders the caret indicator via the same draw entry — see §5)

HUD overlay callers and GU widget callers use the same `FontTable` singleton and the same font
slots. There is no separate widget-only or nameplate-only font object.

---

## 4. Secondary path — GDI text baking into image buffers

A separate text-rendering path bakes text into CPU-side image buffers (not per-frame screen
draws). It is used to composite text onto generated or loaded image/texture data (for example,
in-world signboards, generated icons, or similar static-content surfaces). This path operates
entirely in GDI; it does not issue any D3D9 render calls.

### 4.1 Standard image-bake variant

The standard GDI image-bake function performs the following sequence:

1. Allocate a `LOGFONTA` structure; populate face name (≤31 chars), `lfHeight`, `lfWeight`,
   `lfItalic`, `lfUnderline`.
2. Create a GDI font via `CreateFontIndirectA`.
3. Create a compatible device context (`CreateCompatibleDC`); select the font into it.
4. Set text render attributes: `SetTextColor(0xFFFFFF)`, `SetBkColor(0)`,
   `SetBkMode(OPAQUE=2)`.
5. Measure the bounding box: `DrawTextA(hdc, str, -1, &rect, DT_CALCRECT=0x400)`.
6. Create a 24-bpp `DIBSection` sized to the measured rectangle via `CreateDIBSection`.
7. Render the text into the DIB: `DrawTextA(hdc, str, -1, &rect, format)`.
8. Walk every pixel: read its colour via `Image_GetPixelColor`, write lit pixels into a
   `CxImage` instance via `CxImage_SetPixelColor`.

The result is a `CxImage` containing the rasterized text in white on a transparent field, ready
for compositing.

### 4.2 Outline-capable image-bake variant

A larger variant of the GDI image-bake function performs multiple `DrawTextA` passes with
varying `SetTextColor` values to produce outline or shadow effects in the DIB before the pixel
copy. The exact number of passes, offset deltas, and colour values for each pass are an
`[open question]` (§7.2 item 1). Callers of this variant — which surfaces use image-baked text
with outlines — are also unresolved.

---

## 5. IME and caret peripherals

Two GDI32/IMM32 functions are referenced in connection with the in-world text-input path:

**`GetTextExtentPoint32A`** — used by `Diamond_GUTextbox_DrawCaret` to measure the pixel width
of a substring from the textbox buffer, in order to position the text-insertion caret. The
textbox widget still renders its visible text via the `ID3DXFont` path (`GUWidget_DrawTextInRect`);
`GetTextExtentPoint32A` is only used for caret-placement geometry, not for screen text.

**`ImmSetCompositionFontA`** (IMM32) — referenced to set the Korean IME composition window's
font matching the active input font, so the OS-drawn composition string appears consistent with
the surrounding text. Not directly part of the per-frame text draw.

---

## 6. Render-state and pass recipe

### 6.1 Pass placement

2D HUD / widget / nameplate text is issued in the **2D HUD pass**, which runs after all 3D
render passes. Text is therefore always on top of 3D world geometry with no depth test.
Authoritative pass order: `Docs/RE/specs/nameplate_render.md §4.1`.

```
RenderPass_SkyAndBackground
RenderPass_WorldTerrainAndBuildings
RenderPass_OpaqueWorld
RenderPass_TransparentAndParticles
[2D HUD panels — after all 3D passes; no depth test]
  └─ All GUWidget_DrawTextInRect calls (nameplates, overlays, widgets)
```

### 6.2 ID3DXFont draw state

`ID3DXFont::DrawTextA` manages its own D3D9 render state internally (alpha-blended textured
quads, standard D3DX font-sprite state). The client sets **no custom render-state block** around
the font draw calls. Each call with `pSprite=NULL` self-batches within `DrawTextA`.

| Property | Value |
|---|---|
| Sprite batch | None (`pSprite=NULL`); each call self-batches |
| Format flags | `DT_NOCLIP` (0x100) only |
| Alpha blending | Managed internally by `ID3DXFont` |
| Depth test | Disabled (2D HUD pass context) |
| Colour | Single ARGB per call, supplied by the caller |

### 6.3 GDI image-bake render state

The GDI image-bake path uses a GDI compatible DC, not D3D9. No Direct3D render state is touched.
Output is a CPU-side `CxImage` pixel buffer. The resulting image may subsequently be uploaded to
a D3D9 texture by the calling system, but that upload path is outside the scope of this spec.

---

## 7. Open items

### 7.1 [debugger-confirm] items

Static-confirmed hypotheses requiring a live `?ext=dbg` session before treating them as
implementation facts. All are NON-BLOCKING for the core font port.

| # | Item | What to confirm |
|---|---|---|
| 1 | Runtime `IDirect3DDevice9` value | Statically reads as zero before device creation. Confirm the live pointer value during a running session and verify it is the same instance used by the character skinned draw path. |
| 2 | GDI cell metrics per face per size | The `FontTable` stores a flat half-width advance; actual glyph widths and kerning are produced by GDI at runtime. Confirm by reading `GetTextMetrics` or `GetCharABCWidths` in a live session for representative faces to calibrate the Godot font substitute. |
| 3 | DT_NOCLIP exclusivity | All observed callers of `GUWidget_DrawTextInRect` pass `DT_NOCLIP` (0x100) and pre-compute positioning themselves. Confirm no call site supplies DT_CENTER, DT_RIGHT, DT_WORDBREAK, or any other format flag by sweeping all `ID3DXFont::DrawTextA` call sites in a live session. |

### 7.2 Open questions (escalated to RE domain)

| # | Open item | Escalation path |
|---|---|---|
| 1 | GDI image-bake outline variant — multi-pass recipe | The outline-capable image-bake function performs multiple `DrawTextA` / `SetTextColor` passes before the CxImage copy, producing outline/shadow effects in the DIB. The exact pass count, pixel offsets, and per-pass colours are unresolved. Also unresolved: which surfaces (signboards, generated icons, etc.) consume baked-text images. Route to `re-function-analyst` for a dedicated pass on the larger image-bake variant and its call sites. |
| 2 | Full GUComponent / GULabel widget struct field map | The GULabel font-slot field is confirmed at +228 (this[57]). The generic `GUComponent` leaf-widget class stores a font-slot field at +0xE8 (232) and a glyph-width-step field at +0xB8. The full struct layout — all text-related fields across GUComponent, GULabel, GUTextbox, GUButton — is unresolved. Route to `re-struct-analyst` / `Docs/RE/specs/gui_framework.md`. |
| 3 | Slot → UI role mapping | Which of the 15 font slots each specific HUD element, overlay type, and widget class uses is not enumerated in this pass. Recoverable by collecting the `Diamond_GULabel_SetFontSlot` and `GUWidget_DrawTextInRect` slot arguments across all callers. Route to `re-function-analyst` as a supplementary pass on the widget draw path. |
| 4 | HUD helper canonical names | Several HUD overlay helper functions that call `GUWidget_DrawTextInRect` (nameplate/HUD layer) did not have canonical names at the time of this pass. Route to `ida-toolsmith` for naming and annotation in the IDB. |
