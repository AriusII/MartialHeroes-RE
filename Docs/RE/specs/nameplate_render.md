# Spec: Nameplate Render — World-Anchored 2D Overlays and Floating Damage Numbers

> Clean-room spec. Neutral description only — no decompiler pseudo-code, no binary addresses,
> no decompiler-generated identifiers. Promoted from dirty-room static analysis under EU
> Software Directive 2009/24/EC Art. 6, solely to achieve interoperability. This spec
> documents the nameplate / overhead-overlay subsystem: the per-frame 2D HUD panel that draws
> name labels, HP/MP bars, overhead status icons, and speech text over every visible actor, and
> the separate 3D billboard system that renders floating damage, heal, and EXP digit glyphs
> inside the transparent/particle render pass.
>
> The per-actor cel draw path (skinning, shader bind, status-tint palette) is owned by
> `Docs/RE/specs/character_rendering.md`. The world render-pass sequence (pass installer,
> scene-graph order) is owned by `Docs/RE/specs/render_pipeline.md`. The HUD master-window
> panel hierarchy and gauge atlas wiring are owned by `Docs/RE/specs/ui_hud_layout.md`. This
> spec adds the overlay-specific draw detail: world→screen projection, overlay anchors and
> offsets, CP949 text layout, HP/MP bar widgets, faction colour table, display toggles, and
> the floating-number glyph atlas and billboard record.

> **Verification banner**
> - **verification:** *static-confirmed* for all findings in this document: the two-mechanism
>   split and render-pass z-order, the world→screen projector math and viewport field map, the
>   full overlay anchor/offset table, CP949 text wrapping, HP/MP bar widget layout, the faction
>   colour table, all CHAR_* display toggles, the damage-number atlas (UV stride, texture VFS
>   paths, 12-cell layout), digit decomposition, glyph billboard record layout, and the combat
>   spawn triggers. Items explicitly tagged `[debugger-confirm]` are static hypotheses awaiting
>   a live `?ext=dbg` session before treating them as implementation facts.
> - **ida_anchor:** f61f66a9ae0ec1e946105b2ecff76e8930cb1d1367df64e5688a5266f5ad9963
> - **readiness:** IMPLEMENTATION-READY for the 2D HUD overhead-overlay layer, the
>   world→screen projector, anchor/offset constants, colour table, display toggles, glyph atlas
>   asset paths and UV layout, digit decomposition, and glyph billboard record. Items tagged
>   `[debugger-confirm]` are NON-BLOCKING.
> - **evidence:** [static-ida]
> - **cross-links:**
>   `Docs/RE/specs/render_pipeline.md` (frame pass order, `RenderPass_TransparentAndParticles`);
>   `Docs/RE/specs/character_rendering.md` (per-actor draw path, 2D HUD panel orchestration);
>   `Docs/RE/specs/skinning.md` (AnimCatalog head-height record field +36);
>   `Docs/RE/specs/ui_hud_layout.md` (HUD master-window panel hierarchy, gauge atlas wiring);
>   `Docs/RE/specs/rendering.md` (global render-state matrix);
>   `Docs/RE/specs/combat.md §12.3/§12.4` (damage-number behaviour: kind 0..7, ~1 s rise/fade,
>   multi-hit cap ≤ 7 — owned there, cross-referenced here).

---

## Status

| Item | Confidence |
|---|---|
| Graphics API — Direct3D 9 (PDB path `do_korea_service_dx9`) | CONFIRMED |
| Two-mechanism split (2D HUD overlay vs. 3D billboard) | CONFIRMED |
| Render-pass z-order (HUD drawn after 3D scene; billboards inside transparent pass) | CONFIRMED |
| World→screen projector math and HUD-context viewport field map (+196..+212) | CONFIRMED |
| World anchor — actor XYZ at struct fields +1064/+1068/+1072 | CONFIRMED |
| Vertical offset constants (K = 7/8/9/10/12 by overlay role; head-height + 6 for emote glyph) | CONFIRMED |
| Screen-space Y nudge values (−72 NPC plate, −50 local-player bar, −25/−65 bar rows) | CONFIRMED |
| CP949-aware text wrapping (double-byte = 2 columns; 14 px line height) | CONFIRMED |
| System-font text path (FontTable / D3DXCreateFontA) — no 3D bitmap font for names | CONFIRMED |
| No speech-balloon sprite — "chat bubbles" are plain wrapped coloured text | CONFIRMED |
| HP/MP bar widget layout (bar-style index field +884; map bar-disable flag) | CONFIRMED |
| Local-player triple-bar geometry (110×17 frame; HP/MP ratio bars; UV strip origins) | CONFIRMED |
| Faction colour table (ARGB, 12 entries) | CONFIRMED |
| CHAR_* INI display toggles gating each overlay class | CONFIRMED |
| Distance-cull threshold (per-type squared XZ distance, pre-stored float) | CONFIRMED |
| Speech out-of-range guard (distance > 175 world units → system notice id 23008) | CONFIRMED |
| Damage-number glyph atlas (att-font.dds / cri-font.dds / miss.tga; 12-cell UV; U step ≈ 1/12) | CONFIRMED |
| Digit decomposition (decimal string → 11-entry glyph-index table) | CONFIRMED |
| Style/colour byte encoding (normal / miss-heal / crit/special) | CONFIRMED |
| Glyph billboard record layout (JointXEffect node fields +0..+60) | CONFIRMED |
| Expiry model: expiry timestamp = Time_GetMs() + lifetime | CONFIRMED |
| Combat spawn triggers (SmsgActorSkillAction, Actor_TickBuffSlots, SmsgBuffSlotUpdate_Handler) | CONFIRMED |
| Exact glyph rise curve and alpha-fade per frame | [debugger-confirm] |
| Glyph lifetime in ms per damage type (normal / crit / DoT) | [debugger-confirm] |
| Display-configuration layout-mode byte value at runtime | [debugger-confirm] |
| Viewport scale/centre field values at a known resolution | [debugger-confirm] |
| Atlas cell pixel dimensions for att-font.dds and cri-font.dds | [debugger-confirm] |
| Numeric value of the maximum display-distance threshold in world units | [debugger-confirm] |

---

## 1. Subsystem scope

The nameplate render subsystem produces all world-anchored text and graphic overlays visible
during in-world play. It splits into two independent mechanisms that share a world-anchor model
but differ in render pass, z-order, and drawing technique:

- **Overhead overlay layer** — entity name labels, HP/MP bars, overhead status icons, and
  chat/speech text. These are drawn by a single HUD panel (`ActorChatPanel`) as 2D screen-space
  elements after the 3D world render, making the entire layer effectively depth-disabled and
  always on top of world geometry. See §2.

- **Floating damage/heal/EXP numbers** — pooled billboard quads (`JointXEffect` nodes) anchored
  to each target actor's world position and head height, drawn inside the 3D
  `RenderPass_TransparentAndParticles` pass alongside weapon-trail effects. They are depth-tested
  against world geometry and can be occluded by closer surfaces. See §3.

There is no speech-balloon sprite or background plate behind "chat bubbles" — speech text is
rendered as multi-line wrapped coloured text by `Diamond_Hud_DrawWrappedSpeechText` (calling
`ChatPanel_ComposeFormattedLine`), with no backing quad.

There is no 3D bitmap font for name labels, titles, or HP/MP bars. Names and overhead text use
the engine system font (`FontTable`, backed by `D3DXCreateFontA`-class GDI text). Only the
floating damage numbers use a bitmap-glyph atlas.

---

## 2. Overhead overlay layer (2D HUD)

### 2.1 Per-frame driver

The overhead overlay layer is driven by `ActorChatPanel.onDraw`, a child panel of the in-game
HUD master window (see `Docs/RE/specs/ui_hud_layout.md` for panel hierarchy). Because it is a
HUD panel, all overlay drawing occurs in the 2D HUD pass that runs after the 3D scene.

`ActorChatPanel.onDraw` calls the overhead-overlay master loop once per frame. That loop:

1. Caches `ActorVisualGlobal`, the local player reference, `AppService`, and `BattleController`.
2. Reads the current millisecond timestamp (the animation/expiry clock for timed floating-text
   fields).
3. Reads the "show-all-names" key state; holding both designated keys forces show-all on.
4. Reads the per-type display toggles from the `ActorVisualGlobal` display-configuration block
   (the CHAR_* INI keys — see §2.7).
5. Iterates every actor in the scene-graph intrusive list. Per actor:
   - Expires timed floating-text fields whose stored expiry timestamp has elapsed.
   - Distance-culls actors beyond the per-type maximum display distance (§2.7). Actors without a
     valid visible/spawned state flag are also skipped.
   - Dispatches by entity type to the appropriate overlay helper (§2.1.1).
6. After the actor loop, draws the local player's own overlays using the same helpers, anchored
   to the local player reference.
7. Walks the tracked-speaker list (maintained in the `ActorVisualGlobal` singleton) and draws
   each speaker's chat/speech text over their head in cyan. If the speaker is beyond 175 world
   units, a "target too far" system notice (message database id 23008) is shown instead.

All world→screen projection goes through `Diamond_Hud_ProjectWorldToScreen` (§2.2). All 2D bar
and icon quad draws go through `Hud_DrawTransformed2dQuad`. All system-font text draws go
through `GUWidget_DrawTextInRect` via `FontTable_GetSingleton`.

#### 2.1.1 Overlay helper routing

| Overlay role | Consumer |
|---|---|
| Local player overhead HP/MP triple-bar (frame + HP + MP bars) | Local player bar helper |
| Overhead status icon (quest/help widget vs. equip-buff widget) | Overhead status icon helper |
| Battle-target name + HP/MP bar | `Diamond_Hud_DrawTargetActorLabel` |
| Generic actor name + HP/MP bar | `Diamond_Hud_DrawActorOverheadText` |
| Local-player stance gauge overhead (by stance type and level) | Stance gauge helper |
| Overhead icon (16×16) + wrapped emote/system/guild text | Emote/system/guild text helper |
| Name + faction icon plate (relationship-tinted) | Faction plate helper |
| Wrapped multi-line speech/chat text (no balloon sprite) | `Diamond_Hud_DrawWrappedSpeechText` |
| NPC title + name (title from script-data table) | `Diamond_Hud_DrawNpcTitleAndNamePlate` |
| Stall/vendor shop-title overhead text (orange) | Stall title helper |
| Per-actor type dispatcher | Routes each actor type to the appropriate helper above |

All helpers reach `Diamond_Hud_ProjectWorldToScreen` to convert the world anchor to screen space.

### 2.2 World→screen projection

`Diamond_Hud_ProjectWorldToScreen` implements a hand-rolled perspective divide against the
camera's combined view×projection matrix. Algorithm:

1. Copy the camera's 16-float combined **view×projection** matrix from the camera object
   referenced by the HUD context.
2. Transform the input world point by the matrix (row-vector × 4×4, DirectX convention):
   - `clip.x = m0·x + m4·y + m8·z + m12`
   - `clip.y = m1·x + m5·y + m9·z + m13`
   - `clip.w = m2·x + m6·y + m10·z + m14`
3. **Behind-camera reject:** if `clip.w ≥ 0`, return false. Visible points have `clip.w < 0`
   in this convention. Set `d = −clip.w` (positive depth value).
4. `screenX = (clip.x / d) × scaleX + centreX`
   `screenY = centreY − (clip.y / d) × scaleY`  *(Y axis flipped; top-left screen origin)*
5. Return integer screen (X, Y) and a success flag.

**HUD context object viewport fields** (offsets into the camera/HUD context object):

| Offset | Role |
|---|---|
| +196 | Viewport X scale (half-width × projection scale) |
| +200 | Viewport Y scale |
| +204 | Screen centre X |
| +208 | Screen centre Y |
| +212 | Camera object reference (source of the combined view×projection matrix) |

The scale and centre fields are resolution-dependent. Their values at a specific output
resolution are `[debugger-confirm]` (see §5.1 item 4).

### 2.3 World anchor and vertical offsets

Each overlay is projected from a world-space anchor derived from the actor's world position
(actor struct fields +1064 / +1068 / +1072, XYZ). Before projection, a vertical world-space
offset is added to that position. The Y component of the offset is one of two forms:

- **AnimCatalog head height** — the AnimCatalog record for the actor's appearance (field +36),
  giving the head height calibrated to the mesh. Used by overlays that must track the head
  precisely. Cross-link: `Docs/RE/specs/skinning.md`.
- **Actor scale × K** — the actor scale (actor struct field +100) multiplied by a constant K
  that varies by overlay role:

| K value | Overlay role |
|---|---|
| 7.0 | Emote / chat-input text |
| 8.0 | Speech text (tracked-speaker) |
| 9.0 | Guild / party text |
| 10.0 | System text |
| 12.0 | Name-with-icon plate |
| head-height + 6.0 | Floating emote glyph |

After projection to screen space, additional **screen-space Y nudges** are applied:

| Nudge (px) | Context |
|---|---|
| −72 | NPC title plate |
| −50 | Local-player overhead HP/MP bar |
| −25 | HP/MP bar rows (standard actor) |
| −65 | HP/MP bar rows (large actor type) |
| −20 | Name text (standard) |
| −60 | Name text (large actor) |

Horizontal centring: `screenX += −3 × maxCharsPerLine / 4` (approximately −3 pixels per
character cell; effectively half the estimated text width for a 6 px character cell).

### 2.4 CP949-aware text wrapping and system font

All text overlays use the engine system font (`FontTable`, backed by `D3DXCreateFontA`-class
GDI text). No 3D bitmap font is used for name labels or titles.

Text is wrapped by a CP949-aware column counter using `Diamond_Text_IsSingleByteChar` per
character:
- A single-byte ASCII character counts as 1 column.
- A double-byte CP949 character counts as 2 columns.
- A newline is inserted once the running column count reaches `maxCharsPerLine` (20 columns for
  most overhead text).

Speech text uses a **14 px line height**. Multi-line blocks are vertically centred:
`y += 14 − 14 × lineCount`.

### 2.5 HP/MP bar widgets

A bar is drawn only when the actor's bar-style index (actor struct field +884) is non-zero AND
the current map does not suppress overhead bars (map-setting record field +80 equal to 1
disables bars). When field +884 is zero, the actor shows a floating name label only (no bar).

Bar widget arrays reside on the `ActorChatPanel` panel object:
- HP bar widget array: `panel + 224 + 4 × styleIndex`
- MP bar widget array: `panel + 260 + 4 × styleIndex`
- Actor struct field +150 selects the HP family (value 1) or MP family (value 2).
- Nine reused GUI controls per family (`panel + 228` and `panel + 264`, nine each) are
  positioned as 70 × 20 px cells per row.
- Each bar's screen rectangle is read from the widget record (field +52, four ints) and drawn as
  a textured 2D quad via `Hud_DrawTransformed2dQuad`.

**Local player triple-bar layout** (projected position offset −50 px Y):

| Element | Size | UV strip origin |
|---|---|---|
| Frame quad | 110 × 17 px | (10, 22) |
| HP bar | `HPRatio × 100` px wide × 4 px tall | (15, 43) |
| MP bar | `MPRatio × 100` px wide × 4 px tall | (15, 48) |

Blend state: SrcBlend = SRCALPHA (5), DestBlend = INVSRCALPHA (6).

HP and MP ratios are computed from the respective current/maximum fields on the actor. The
overhead bar gauge texture source is a `[debugger-confirm]` / open item (§5.2 item 1).

### 2.6 Faction and role colour table

All ARGB values are 32-bit, format `0xAARRGGBB`.

| ARGB | Colour | Overlay context |
|---|---|---|
| `0xFF00FF00` | Green | Friendly/normal — relationship type 1, non-negative value |
| `0xFFFFFF00` | Yellow | Party member — relationship type 2; also system text |
| `0xFFFF00FF` | Magenta | Hostile — negative value |
| `0xFFFF0000` | Red | PK/criminal (actor field +880 ≥ 2); muted/suppressed name |
| `0xFFFFFFFF` | White | Default fallback |
| `0xFF00FFFF` | Cyan | Speech/chat bubble text; NPC title lines |
| `0xFF814200` | Orange | Standard NPC name label; stall/vendor title |
| `0xFF028E66` | Teal-green | Standard player/mob name plate |
| `0xFFFF0095` | Pink | Emote/chat-input overhead text |
| *(two entries)* | Guild text tints | Two states, toggled by actor field +1724 |
| `0xB30000FF` | Blue at alpha 0xB3 | Local-player HP/MP bar background |
| `0x80000000` | Black at alpha 0x80 | Overhead icon backing quad |

### 2.7 Display toggles and distance culling

Each overlay class is gated by a per-type display toggle read from the `ActorVisualGlobal`
display-configuration block (sourced from the CHAR_* INI keys). The full set of confirmed
toggle names (11 keys, loaded and saved by dedicated INI routines):

| INI key | Overlay class gated |
|---|---|
| `CHAR_PLAYER` | Player name/bar overlays |
| `CHAR_MOB` | Mob name/bar overlays |
| `CHAR_NPC` | NPC name plates |
| `CHAR_STALL` | Stall/vendor title overlays |
| `CHAR_GUILD` | Guild overhead text |
| `CHAR_HELPTIP` | Help-tip/quest icon overhead |
| `CHAR_UI_ANIMATION` | UI animation overlays |
| `CHAR_QUEST_ICON` | Quest icon overhead |
| `CHAR_DROP_ITEM` | Dropped item label overlays |
| `CHAR_DAMAGE_MSG` | Floating damage/heal/EXP numbers |
| `CHAR_AUTOTARGET` | Auto-target indicator |

When a toggle is off, the corresponding overlay class is suppressed for all actors of that type.

A squared XZ distance check is applied per actor before any overlay draw. The maximum display
distance is stored as a pre-squared float threshold. Actors beyond this threshold are skipped
entirely. The numeric value in world units is `[debugger-confirm]`.

Speech text applies a separate linear distance guard: speakers beyond 175 world units suppress
the speech draw and trigger a "target too far" system notice (message id 23008) instead.

Name masking: on PK-enabled maps the PK/criminal state flag (actor field +880 ≥ 2) overrides
the normal name colour with red (`0xFFFF0000`).

---

## 3. Floating damage/heal/EXP numbers (3D billboards)

### 3.1 Glyph atlas and font textures

The damage-number system uses three bitmap-atlas textures loaded during boot by
`DamageNumberFont_LoadTextures`, called from `Boot_LoadDataTableCorpus`. The singleton managing
these assets is owned by `SwordLightManager` and is shared with weapon-trail effects.

| VFS asset path | D3D9 format | Role |
|---|---|---|
| `data/effect/tex/att-font.dds` | D3DFMT_A8R8G8B8 | Normal / attack digit glyphs |
| `data/effect/tex/cri-font.dds` | D3DFMT_A8R8G8B8 | Critical hit digit glyphs |
| `data/effect/tex/miss.tga` | D3DFMT_A8R8G8B8 | "MISS" indicator |

Each atlas is divided into a **12-cell UV table** (12 columns, full texture height per cell).
The U step per cell is ≈ 1/12 (0.083330). The UV table occupies 384 bytes (12 × 32-byte quad
entries) and is shared across all three textures. A shared vertex buffer created by
`SwordLightManager` holds the quad geometry for billboard rendering.

The pixel dimensions of each atlas cell require reading the VFS `.dds` texture headers and are
`[debugger-confirm]` (§5.1 item 5).

### 3.2 Spawn chain and triggers

The floating-number spawn chain is independent of the 2D HUD overlay layer and operates
entirely within the 3D effect system:

```
Effect_SpawnPeriodicBuffVisual
  → FloatingNumber_SpawnDigitGlyphs
      → FloatingNumberGlyph_SpawnAsJointEffect  (one call per digit)
```

**Spawn triggers** (callers of `Effect_SpawnPeriodicBuffVisual`):
- `SmsgActorSkillAction` — network skill/attack result packet handler.
- `Actor_TickBuffSlots` — periodic DoT/HoT tick processing.
- `SmsgBuffSlotUpdate_Handler` — buff slot update packet handler.
- Multiple combat-result paths that cover damage, heal, absorb, and EXP grant events.

### 3.3 Digit decomposition and layout

`FloatingNumber_SpawnDigitGlyphs` converts the numeric value to a decimal string, then maps
each character to a glyph atlas cell index via an 11-entry digit glyph index table. The table
character order is `'1'` through `'9'`, then `'0'`, then `'!'` — giving glyph indices 0..10
respectively (glyph index 9 → `'0'`; glyph index 10 → `'!'`; the 12th atlas cell is spare).
One `FloatingNumberGlyph_SpawnAsJointEffect` call is made per digit.

**Layout parameters:**
- A layout-mode byte from the display-configuration singleton selects glyph scale and horizontal
  spacing. Mode 1 → glyph scale 7.0. Modes 2 and 3 → glyph scale 1.0. Horizontal spacing base
  is 1.2, with wider-spacing branches selected by a size-class argument (values 3–7).
- Starting X offset: `−2 × digitCount` for centred layout; `+3` for offset layout (flag-controlled).
- **Style/colour byte** selects atlas texture and tint at draw time:
  - Default → style 2 (normal, `att-font.dds`).
  - Spawn type 1 and value ≤ 0 → style 1 (miss/heal, `miss.tga` or green tint).
  - Spawn type 1 and value > 0 → style 3 (critical/special, `cri-font.dds`).

The layout-mode byte value at runtime and the size-class argument per damage type are
`[debugger-confirm]` (§5.1 items 3 and 3).

### 3.4 Glyph node record layout (JointXEffect)

Each digit glyph is allocated as one node from the JointXEffect node pool and added to the
effect manager's active list. The target actor is resolved via
`ActorManager_FindByCompositeKeyCached`; if not found, the anchor is zeroed.

| Offset | Field |
|---|---|
| +0 | Actor composite key |
| +4 | Actor sub-key |
| +8 | Size-class argument |
| +12 | Colour/style byte |
| +16 / +20 / +24 | Anchor world XYZ (copied from target actor fields +1064/+1068/+1072) |
| +28 | Head-height offset (AnimCatalog record field +36) |
| +32 | Type argument |
| +36 | Glyph index argument |
| +44 | Alive sentinel (value −1) |
| +45 / +46 | Alive flag / auxiliary byte |
| +48 | **Expiry timestamp** = `Time_GetMs() + lifetime (ms)` |
| +60 onward | Four billboard corner vertices (six floats each: XYZ position + UV), initialized from the EffectManager quad template |

The JointXEffect manager updates each active node per frame: it advances the glyph position
upward and fades its alpha over the remaining lifetime, then prunes nodes whose expiry timestamp
has elapsed. The exact rise curve and alpha-fade curve are `[debugger-confirm]` (§5.1 item 1).

---

## 4. Render-state and pass recipe

### 4.1 Pass placement and z-order

The installed world render-pass order (authoritative sequence in
`Docs/RE/specs/render_pipeline.md`, installed by `Renderer_InstallWorldRenderPasses`):

```
RenderPass_SkyAndBackground
RenderPass_WorldTerrainAndBuildings
RenderPass_OpaqueWorld
RenderPass_TransparentAndParticles   ← floating damage-number billboards (3D, depth-tested)
[2D HUD panels — drawn after all 3D passes]
  └─ ActorChatPanel.onDraw           ← overhead overlays (no depth test, always on top)
```

Floating damage numbers are drawn inside `RenderPass_TransparentAndParticles` and are
depth-tested against world geometry. The 2D overhead overlay layer runs entirely after the 3D
scene and carries no depth test.

### 4.2 2D quad draw state (overhead overlay)

Each bar or icon quad is drawn by `Hud_DrawTransformed2dQuad`, which builds a 2D affine
transform using `D3DXMatrixTransformation2D` (scale + rotation + translation), sets it as the
D3D9 device world transform, and issues a `DrawPrimitive` call for a single textured quad.

| State | Value | D3D9 name |
|---|---|---|
| Alpha-blend | Enabled | D3DRS_ALPHABLENDENABLE = true |
| SrcBlend | 5 | SRCALPHA |
| DestBlend | 6 | INVSRCALPHA |
| Depth test | Disabled (2D HUD pass) | — |

### 4.3 Billboard draw state (damage numbers)

Damage-number glyphs are rendered as billboard quads by the JointXEffect manager inside the 3D
transparent/particle pass. The vertex buffer is shared with `SwordLightManager` weapon-trail
effects.

Textures are sourced from the three atlas texture handles on the `DamageNumberFont_LoadTextures`
singleton. The colour/style byte on each glyph node selects which atlas texture and what colour
tint is applied at draw time. Billboard quads are depth-tested against the world scene (same
pass as particle and weapon-trail effects).

---

## 5. Open items and [debugger-confirm]

### 5.1 [debugger-confirm] items

Static-confirmed hypotheses requiring a live `?ext=dbg` session before treating them as
implementation facts. All are NON-BLOCKING for the core port work.

| # | Item | What to confirm |
|---|---|---|
| 1 | Glyph rise curve and alpha-fade | The per-frame JointXEffect tick advances Y and fades alpha from the +48 expiry vs. current timestamp. Confirm by breakpointing the JointEffect tick and reading a live node record across multiple frames. |
| 2 | Glyph lifetime per damage type | The lifetime ms argument supplied per spawn call. Confirm by logging the argument at `FloatingNumber_SpawnDigitGlyphs` for a normal hit, a critical hit, and a DoT tick. |
| 3 | Display-configuration layout-mode byte | The layout-mode byte from the display-configuration singleton. Read its value during a normal in-world session to confirm which scale/spacing branch runs. |
| 4 | Viewport scale and centre field values | HUD context fields +196, +200, +204, +208 are resolution-dependent. Read at a known output resolution (e.g. 1024×768) to provide an implementation reference. |
| 5 | Atlas cell pixel dimensions | Read the `.dds` headers for `att-font.dds` and `cri-font.dds` from the VFS. Cell width = atlas width / 12. |
| 6 | Maximum display-distance threshold | The pre-squared float threshold used for distance-culling actors. Read the value and compute the world-unit radius. |

### 5.2 Open questions (escalated to RE / asset analysis domain)

| # | Open item | Escalation path |
|---|---|---|
| 1 | HP/MP overhead bar gauge texture | The bar fill strip textures used by the overhead bar widget arrays were not positively identified in this pass. The right-edge HUD gauge (`chunrihojung.dds` per `Docs/RE/specs/ui_hud_layout.md §5.6`) is a separate asset. Confirm the overhead bar atlas source via VFS asset inspection and route finding to the ui_hud_layout RE track. |
| 2 | cri-font.dds vs. att-font.dds cell metrics | Whether the critical-font atlas differs from the normal-font atlas in cell dimensions or only in colour/glow. Requires VFS asset inspection, not IDA. |
| 3 | Full damage-spawn type enumeration | The style byte encoding is partially recovered for normal/miss/crit via value sign and type argument. The complete enumeration across all spawn sites (damage, heal, absorb, EXP grant, etc.) requires the combat-result packet spec for `SmsgActorSkillAction`. Route to re-protocol-analyst / `Docs/RE/specs/combat.md`. |
