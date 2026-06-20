# Minimap & World Map — HUD Radar and Full-Screen Map, Clean-Room Specification

> Clean-room neutral spec. Promoted from dirty-room analyst notes by the asset-spec-author.
> No legacy symbols, no addresses, no pseudo-code. Routines are referred to by role only.
> Describes the map UIs (the always-on HUD radar, the BroodWar full-screen world map, and the
> separate zoomable tiled big-map), the world→minimap pixel transform, the per-cell tile-streamed
> radar/big-map background, live-actor blip selection, the full-screen map's per-area artwork and
> static landmark pins, the per-area map binaries (the `region*.bin` region grid + the
> `regiontable*.bin` region-attribute table), and the on-disk map data files — so
> `Client.Application` and `05.Presentation/MartialHeroes.Client.Godot` can rebuild every surface
> from scratch.
>
> Every magic constant an engineer cites must reference this file. Asset byte layouts for the
> data tables live in §6 and in `Docs/RE/formats/` where a dedicated format doc exists.

---

## Verification banner

| Attribute | Value |
|---|---|
| `verification` | **confirmed** for both radar/big-map UI surfaces, the projection math, the per-cell tile-streaming model, blip selection, the per-area map-binary loader, the `region*.bin` grid layout, the `regiontable*.bin` 32×48 attribute-table layout, and the BroodWar full-screen-map texture/landmark chain (all control-flow-confirmed + operands byte-present). **sample-verified** for the on-disk map-art inventory and the `mapsetting.scr` zone table. **capture/debugger-pending** for: the precise semantics of the region-attribute enum values `{0,1,2}` (safe / PvP-open / closed are control-flow-inferred labels, not server-confirmed), whether actor world-pos `+1064` is exactly the last-packet value vs a post-integration value, the writers of the current-area / current-sub-region ids (world-state packet origin), and the player-arrow rotation handedness vs the world Z-negation. |
| `ida_reverified` | 2026-06-20 (IDB SHA 263bd994, CYCLE 7) |
| `ida_anchor` | 263bd994 |
| `evidence` | [static-ida] (IDA static control-flow + operand reading; no debugger, no live capture in this lane — the VFS §6 facts reuse the prior sample-verified census. CYCLE 7 (2026-06-20) re-confirmed the corner-minimap / total-map world→pixel transform, the player-centred mosaic-scroll math, the total-map arrow-key pan, and the BroodWar authored-pixel marker records against build 263bd994, byte-present operands — see §2.2a, §3.1a, §5.6, §5.7) |
| `conflicts` | RESOLVED against build 263bd994: (1) §6.4 `regiontable*.bin` is the **32×48 region-ATTRIBUTE table** loaded straight into the in-memory region table, NOT a 52×32 sub-zone label table — the old layout was a different/conflated file; (2) the **`region*.bin` region GRID** file (width/height/byte-grid/origin, 256-unit cells) was entirely undocumented and is now §6.4; (3) the radar footer-state colour order is **0=yellow, 1=white, 2=red(gated)**, not the prior "white/yellow/red for safe/contested/war"; (4) the "+96 class-byte bias" was a misreading — `+96` is the class-byte **offset**, no additive bias exists; (5) the party blip id is the set **{29,30,51,52}**, not a single `30`; (6) the tile-prefix token is a **`%s` string** (the area-tag), not a single `%c`; (7) open-question #8 RESOLVED — the in-memory region/attribute table is loaded from `regiontable*.bin`, independent of `mapsetting.scr`; (8) a **second** zoomable/scrolling tiled big-map surface (distinct from the BroodWar full-screen map) exists and is now §5.6. |

> **Status legend.** Per-claim tags inline: **CONFIRMED** (IDA control-flow + operands on build
> 263bd994) / **SAMPLE-VERIFIED** (also matches a real shipped VFS file) / **STATIC-HYPOTHESIS**
> (single inference) / **CAPTURE/DEBUGGER-PENDING** (server-authored magnitudes or on-wire value
> meanings that need a live capture/debugger read).

> **Capture status — read this first.** No Wireshark oracle was available for this lane. Every
> behavioural claim below is **CONFIRMED** (read statically from the binary, control-flow + operands)
> or **STATIC-HYPOTHESIS** (single inference). The radar consumes only client-side actor state (each
> actor's world position from the actor's last-known world vec3, the same field a movement packet
> writes); no map-specific packet was observed, and the on-wire *value* meanings (e.g. the concrete
> semantics of the region-attribute enum) are **CAPTURE/DEBUGGER-PENDING**. The VFS-data claims in
> §5–§6 are **SAMPLE-VERIFIED** against real shipped files, independent of any capture.

---

## 1. Overview — three independent map surfaces

The client exposes **three distinct map UIs** (the prior two-surface count missed the tiled
big-map). They share the in-game widget toolkit but have separate code and separate rendering
models. (CONFIRMED.)

| Surface | Role | Background source | Pins/blips | Toggle |
|---|---|---|---|---|
| **HUD radar** | "where am I + nearby actors" corner widget, always on | streamed 3×3 ring of per-cell bitmaps, re-stitched each frame around the player | **live** per-actor blips (NPC/mob/player/party/GPS + a rotated player arrow) | collapse/expand button; click body → open the big map |
| **BroodWar full-screen world map** | "open the atlas" overlay, nested in an info panel | single per-area texture (or a world-overview texture) | **static** landmark pins from a data table | `b` key |
| **Tiled big-map** (§5.6) | a zoomable, scrolling, per-cell-tiled big map | streams the **same** `data/effect/map/*.bmp` per-cell tiles as the radar (1024-unit cell stride) | a 64×64 player arrow; keyboard pan + integer zoom | opened from the radar's click-body handoff |

The radar is centred on the local player and redraws every visible frame. The BroodWar full-screen
map is a modal overlay keyed on the current area id and decorated with point-of-interest pins read
from a precomputed table. The tiled big-map is a separate class with its own scrolling/zooming model
that re-uses the radar's tile path. They share nothing but the widget toolkit. (CONFIRMED.)

> **Naming note.** §5 covers the **BroodWar** full-screen map (single per-area DDS + static landmark
> pins + GPS side panel). The §5.6 **tiled big-map** is a *different* class with a per-cell-tiled
> scrolling/zooming background — do not conflate the two.

---

## 2. HUD radar — geometry and the world→minimap transform

### 2.1 Widget body geometry (CONFIRMED)

The radar widget is **135 px wide**; its map body is a **133 × 133 px** square. The local player is
always rendered **dead-centre** of that body. Above the body sits a title bar (with three 11×11
control buttons — see §4), and below it a footer with three labels (area name, integer coord-X,
integer coord-Z). A marker-overlay sub-panel covers the 133×133 body and holds the live blip
elements (§3.3).

### 2.2 The projection (THE core finding — CONFIRMED, operands byte-present)

The minimap projection routine maps an actor's world position to a body pixel. It operates on the
**X and Z** world components only (the world is planar in X-Z; world Y is ignored):

```
rel.X = actorWorldX − localPlayerWorldX
rel.Z = actorWorldZ − localPlayerWorldZ

px = rel.X × 0.125 + 66.5
py = rel.Z × 0.125 + 66.5
```

| Element | Value | Meaning |
|---|---|---|
| Scale | `0.125` minimap-px per world-unit | exactly **1:8** |
| Origin | `(66.5, 66.5)` | centre of the 133×133 body — the local player |
| Reference position | local player's **world position** (the actor's last-known world vec3) | `rel` is measured against it |
| Cull rule | draw a blip only if `0 ≤ px ≤ 133` **and** `0 ≤ py ≤ 133` | actors outside the body are dropped |

The projection operates on world vector components **X (index 0)** and **Z (index 2)**; world Y
(index 1) is ignored. The reference position is the local player actor's world vector (the same
vec3 field movement traffic writes; when there is no local player a global fallback player pointer
is used). The two constants `0.125` and `66.5` are byte-present in the binary's immediate operands.
(CONFIRMED; the *meaning* of the reference vec3 as "last packet" vs a post-integration value is
CAPTURE/DEBUGGER-PENDING — see §2.4.)

### 2.2a Panel-local absolute form of the transform (CYCLE 7 — CONFIRMED, operands byte-present)

The projection helper of §2.2 can be read in its **absolute panel-local** form, which is how the
binary actually computes it: a world point is mapped directly to a panel pixel without an explicit
"subtract the player first" step — the player-centring is folded into the additive origin.

```
mapPixelX = worldX × 0.125 + 66.5        // panel-local pixel X
mapPixelY = worldZ × 0.125 + 66.5        // panel-local pixel Y; uses world Z, drops world Y
```

This is the same `0.125` scale and `+66.5` origin as §2.2, now expressed as a direct
world→panel-pixel map. It writes vector component **0 (X)** and component **2 (Z)** of the output;
component 1 (the Y/height axis) is never written. The helper is invoked several times inside the
radar's draw to place the player arrow, the actor dots, and the region captions. The two float
literals `0.125` and `66.5` are byte-present immediates. (CONFIRMED.)

- **Scale `0.125` = 1/8** ⇒ **8 world units per minimap pixel**.
- **Origin `+66.5` px** = the panel half-size; it centres the local player in the body.

> Both forms are equivalent: §2.2's `rel = actor − player` then `rel × 0.125 + 66.5` and the absolute
> `world × 0.125 + 66.5` differ only by where the player-centring is applied. A reimplementation may
> use either; the absolute form matches the binary's icon/marker placement most directly.

### 2.3 Visible window (derived)

The visible half-extent in world units is `66.5 / 0.125 ≈ 532` units in each axis from the player —
a roughly `1064 × 1064`-unit square. Since one terrain cell is **1024** world units, the radar shows
approximately the player's own cell plus half a cell on every side. (STATIC-HYPOTHESIS — a direct
consequence of §2.2.)

### 2.4 `rel` is measured against the actor's world vec3 (CONFIRMED offset / semantic CAPTURE-PENDING)

Both the actor position and the local-player reference position are each actor's **last-known world
vec3** (the same field movement traffic writes), read directly — not an interpolated render
position. The radar is therefore a snapshot-of-network-state view; smoothing/interpolation that the
world view applies is not applied here. The position field lives at a fixed actor-struct offset
(world vec3 at actor `+1064`, i.e. `+0x428`; facing quaternion at `+1076`). (CONFIRMED for the
offset and that the raw field is used. CAPTURE/DEBUGGER-PENDING for the precise semantics: whether
`+1064` is exactly the last-packet position vs a post-integration/step value would need a live read
of the movement-write site under traffic. The wire layout of the movement packet is owned by
`Docs/RE/opcodes.md` / `packets/`, not this spec.)

---

### 2.5 Constants table — corner minimap / total-map transform (CYCLE 7 — CONFIRMED)

Every constant below is a byte-present immediate or decoded literal on build 263bd994. An engineer
citing any of these must reference this spec.

| Constant role | Value | Confidence |
|---|---|---|
| World→map pixel scale | `0.125` (= 1/8) ⇒ 8 world units per pixel | HIGH |
| Map pixel origin / offset | `+66.5` px (icon/marker placement) / `−66` (mosaic scroll) | HIGH |
| Cell size / tile-index divisor | `1024` (`>> 10`) | HIGH |
| Tile-index world bias | `20480.0` (= `20 × 1024`) | HIGH |
| Global cell-index filename origin | `9980` | HIGH |
| Minimap pixels per cell tile | `128` (= `1024 × 0.125`) | HIGH |
| Corner-minimap window | 2×2 tiles = `256 × 256` px | HIGH |
| Total-map pan step | `±25` px/frame at panel `+0xD8` (X) / `+0xDC` (Y) | HIGH |
| Total-map pan→world factor | `8 ×` pan (inverse of `0.125`) | HIGH |
| Total-map viewport extent | render-target W ÷ 256, H ÷ 256 (`256 = 2 × 128`) | HIGH |
| Player position source | local-player actor `+0x428` (X) / `+0x430` (Z) | HIGH |
| Tile mosaic path template | `data/effect/map/d{prefix}x{cellX}z{cellZ}.bmp` | HIGH |

### 2.6 Struct offsets touched by the map UIs (CYCLE 7 — CONFIRMED)

**Corner minimap panel** (`this` base):

| Offset | Field role |
|---|---|
| +0x8C | visible / enabled flag (early-out) |
| +0xC0 | parent / owner panel reference |
| +0xC4 | tile-texture pool head |
| +0xD0 | default / fallback texture handle |
| +0xF8 | render / draw-target object (quad blitter) |

**Total-map panel** (`this` base):

| Offset | Field role |
|---|---|
| +0x8C | visible flag |
| +0xD8 | pan offset X (arrow-key scroll, ±25/frame) |
| +0xDC | pan offset Y (arrow-key scroll, ±25/frame) |

**Local-player actor** (position source for both panels):

| Offset | Field role |
|---|---|
| +0x428 | world X (float) |
| +0x430 | world Z (float, Vec3 index 2) |

(The BroodWar 40-byte marker record layout is in §5.7. The +0x428/+0x430 actor offsets are consistent
with the §2.4 / §3.7 actor-struct facts elsewhere in this spec.)

### 2.7 Z-convention port note (CYCLE 7 — MEDIUM)

The minimap math is computed in **engine-Z**: the binary reads the world **Z** component directly
(Vec3 index 2) and applies no negation. The Godot port negates Z for world geometry
(`Helpers/WorldCoordinates.ToGodot`, `(x,y,z) → (x,y,−z)`), so a faithful port must apply the **same
Z convention it uses for actor positions** *before* this transform, then compute `0.125 · Z + 66.5`.
The player-arrow rotation uses a Z-rotation matrix (compass-like); the exact rotation-angle source is
not fully traced (likely the camera yaw) — **MEDIUM / DBG-pending** (see §3.5). (MEDIUM — static
cannot show the camera frame.)

## 3. HUD radar — background streaming and live blips

### 3.1 Background is streamed per-cell tiles, NOT a per-area sheet (CONFIRMED)

The radar background is **not** a single per-area texture. Each frame the radar streams a **3×3 ring
of per-cell bitmap tiles** centred on the player's current cell and re-stitches them under the fixed
centre, so the background scrolls pixel-smooth as the player moves. The **same tile path is also
streamed by the §5.6 tiled big-map** — so the "tiles absent → blank background" consequence (§3.2)
applies to both surfaces, not just the radar.

Tile path template:

```
data/effect/map/d{prefix}x{cellX}z{cellZ}.bmp
```

| Token | Source |
|---|---|
| `{prefix}` | the **per-area tile-set prefix string** — the per-area map-area tag (a `%s` string, NOT a single character), the same tag that names all four per-area map binaries (§6); see §3.7 |
| `{cellX}` | the player's current cell column index; the 3×3 loop visits this ± 1 |
| `{cellZ}` | the player's current cell row index; the 3×3 loop visits this ± 1 |

The format string is `data/effect/map/d%sx%dz%d.bmp` — the prefix is formatted with `%s` (the
area-tag string), not `%c`. (Correction over the prior "prefix character" wording.)

Cell math, per axis (the same cell convention used by terrain streaming, re-centred for the 3×3
stitch):

```
cellX = (int)(playerWorldX + 20480.0) / 1024 + 9980     // ± 1 across the 3×3 ring
cellZ = (int)(playerWorldZ + 20480.0) / 1024 + 9980     // ± 1 across the 3×3 ring
```

Each tile is a **128 px** source bitmap mapped 1:1 over its **1024**-unit cell. Tiles load through
the file chokepoint with animated-texture options and are kept in a **per-panel cache** (a sorted
string-keyed list, find-or-insert by path); a cache miss allocates a managed texture and inserts it.
On bind failure the tile slot falls back to a default fill texture. Only the visible slice of each
128 px tile inside the 133 px window is drawn — a scrolling tile window that keeps the player
centred. (CONFIRMED.)

### 3.1a Mosaic-scroll math and the 2×2 blit window (CYCLE 7 — CONFIRMED, operands byte-present)

CYCLE 7 re-traced the background-mosaic addressing exactly. The local player position is read from
the local-player actor struct at **`+0x428` (world X)** and **`+0x430` (world Z, Vec3 index 2)**;
when there is no local player a global zero-vector fallback is used. From that position the radar
computes **two quantities per axis**.

**(i) Cell index — which tiles to load** (the `{cellX}`/`{cellZ}` of the §3.1 path template):

```
cellX = ((int)(worldX + 20480.0) >> 10) + 9980        // >>10 = ÷1024
cellZ = ((int)(worldZ + 20480.0) >> 10) + 9980
```

- `>> 10` divides by **1024** — the world cell size.
- `+20480.0` (= `20 × 1024`) is a bias so negative world coordinates still index a positive tile
  range.
- `+9980` is the **global cell-index origin** baked into the tile filenames.

**(ii) Sub-cell pixel scroll offset — so the player stays centred between tiles:**

```
subPixX = worldX × 0.125 − 66        // integer-truncated; same 1/8 scale as §2.2a
subPixZ = worldZ × 0.125 − 66
```

The `−66` centring constant is the same offset family as `+66.5` (integer-truncated); the two
derivations agree. So the background mosaic **scrolls by `worldPos × 0.125` modulo the 128-px cell
tile**, the tile grid is **indexed by `worldPos / 1024`**, and the player arrow is drawn at the panel
centre (~`66.5, 66.5`) via the §2.2a transform.

**On-screen tile blit — the corner-minimap window size.** After computing the cell indices and the
sub-cell scroll, the radar runs a **2×2 nested tile loop**. Each tile is stepped by **128 px**
(one 1024-unit cell = `1024 × 0.125 = 128` minimap pixels — internally consistent with the scale),
loads `data/effect/map/d{prefix}x{cellX}z{cellZ}.bmp` for that cell, and blits it with a computed
destination rect and clip. The result is a **256×256-px (2×2 × 128) drawable window**, clipped to the
133-px panel body. The corner minimap is player-centred and **not** pannable. (CONFIRMED.)

### 3.2 On-disk reality of the tiles — they do NOT ship (SAMPLE-VERIFIED)

The `data/effect/map/d*x*z*.bmp` per-cell tiles are **absent from the entire VFS**: a full census of
the 43,347-entry archive found **no per-area minimap bitmaps anywhere** (zero `.dds`/`.bmp`/`.png`/
`.tga` under any `data/mapNNN/` directory outside its `texture/` subfolder, and no minimap/radar/
overview-named files except an unrelated UI progress-bar texture). Consequently, in the shipped
client the radar background-streaming path would hit the bind-failure branch and fall back to the
default fill — i.e. a **blank radar** — unless those tiles are supplied externally or generated.
(SAMPLE-VERIFIED.)

> **Reimplementation consequence.** A faithful Godot radar (and the §5.6 tiled big-map) must either
> (a) re-render top-down cell thumbnails itself from terrain/`.ted` heightfield data, or (b) render
> only the live blips over a plain background. Streaming `data/effect/map/*.bmp` is a dead path
> against the shipped VFS for **both** the radar and the tiled big-map (both stream the same path).

### 3.3 Blip elements (CONFIRMED)

The marker overlay holds four reusable blip image elements plus a hover tooltip label. Each blip's
artwork is bound by an **integer texture-group id** through the UI texture-id resolver (the same id→
texture manifest the rest of the in-game UI uses; the id→file name mapping is gated on the VFS UI
texture manifest — see Open Questions).

| Element | Size | Texture-group id | Role |
|---|---|---|---|
| Generic actor blip | 4×4 | 52 | NPC / mob / near-lead party-member dot |
| Party/lead blip | 10×10 | 30 | the active/lead tracked party member (lead-roster branch) |
| Local-player arrow | 16×16 | 13 | rotated to face the player's heading |
| GPS / escort marker | 16×16 | 75 | "you are here" / escort target |
| NPC-name tooltip | label 100×16 | — | shown when the cursor hovers a blip |

> **Party-blip id nuance (CORRECTION).** The party/lead marker is not a single id `30`. The lead
> party member uses id **29** when it is *far* (distance² ≥ `30625` = 175² world units) and id **52**
> when *near*; id **30** is the party-roster lead in its own branch, and id **51** marks ordinary
> roster / escort members. The full party-blip id set is therefore **{29, 30, 51, 52}** (CONFIRMED).

### 3.4 Which actors become blips (CONFIRMED)

Each frame the radar walks the active actor list and, for every actor whose projected `(px, py)`
lands inside the 133×133 body (§2.2 cull rule), draws a blip whose texture-group id is chosen by the
actor's **class byte** (a small per-actor enum: `1 = player`, `2 = NPC`, `3 = mob`) plus a sub-type
condition. The class byte is read at a fixed actor-struct offset (`+96`, i.e. `0x60`) and switched
directly on (`== 2` → NPC branch, `== 3` → mob branch, `== 1` → player branch).

> **CORRECTION — there is no "+96 bias".** A prior reading described the class byte as "biased by
> +96 before mapping to a texture group". That is a misreading: **`+96` is simply the actor-struct
> *offset* of the class byte itself** — there is no additive `+96` arithmetic on the class value
> (CONFIRMED).

| Actor class | Condition | Texture-group id |
|---|---|---|
| NPC (2) | escort / quest | 58 |
| NPC (2) | special | 57 |
| NPC (2) | default | 51 |
| NPC (2) | selected / targetable override | 56 |
| Mob (3) | default | 50 |
| Mob (3) | aggro / blinking (an 8-phase tick flash `(tick/100) & 7`, phases > 3) or flagged target | 56 |
| Player (1) | same faction / party | 53 |
| Player (1) | enemy faction (war/PvP) | 55 |
| Party roster | lead member, far (dist² ≥ 30625 = 175²) | 29 |
| Party roster | lead member, near | 52 |
| Party roster | active/lead member (lead-roster branch) | (party blip, 10×10, id 30) |
| Rank/escort roster | present | 51 |
| GPS / escort target | present | 75 |
| Local player | always (drawn last, centred) | 13 (rotated arrow) |

The mob aggro/blink flash is an 8-phase tick cycle `(currentTick / 100) & 7`, lit when the phase
exceeds 3. NPC blips additionally drive the hover tooltip: the NPC's display name is looked up from
the NPC template table (an NPC name key at the NPC struct) and shown in the 100×16 label, placed at
`(px − 32, py − 2)`, when the cursor is over the blip; a per-template flag selects a single-line vs
three-line label. (CONFIRMED. Texture-group ids are interop facts; the semantic role of each was
inferred from the class byte and the surrounding lookups.)

### 3.5 Player-arrow rotation (CONFIRMED)

The local-player arrow (id 13) is rotated to point the way the player faces. The update builds a
Z-rotation from the player's facing orientation (the facing quaternion at actor `+1076`), translates
by **(−6, −6)** to centre the 16×16 sprite on its hotspot, multiplies the two transforms, and copies
the result into the element's local transform so the arrow tracks heading. Small per-blip vertical
anchor nudges are also applied (generic blip `+14` px in Y; party/roster blip `+11`; GPS marker
`(−8, +8)`; player-arrow text/anchor `(+1, +16)` before rotation); these are sprite-anchor offsets,
not part of the §2.2 transform.

> **Handedness caveat (CAPTURE/DEBUGGER-PENDING).** The exact rotation sign (clockwise vs
> counter-clockwise, and whether it matches the world's Z-negation convention noted in `CLAUDE.md`)
> was not confirmed against a running client. A Godot port should validate the arrow direction live
> to avoid a mirrored arrow.

### 3.6 Footer labels (CONFIRMED)

Each frame the footer is refreshed: coord-X = integer of the player world X, coord-Z = integer of
the player world Z, area name from the region-attribute table for the **current sub-region**
(§3.7), and a colour-coded state caption. The state value is the region-attribute enum at record
offset `+0x28` (the 11th dword) of the current sub-region's record (§3.7 / §6.5).

**State → colour mapping (CORRECTED).** The prior wording said "white / yellow / red-ish for
safe / contested / war"; the actual control flow keys the colour on the enum value as follows
(CONFIRMED):

| Enum value | Caption colour | Caption message-db id |
|---|---|---|
| `0` | yellow | 35001 |
| `1` | white | 35002 |
| `2` | red-ish | 35003 — but only when a per-panel flag is set; otherwise it falls back to yellow / 35001 |

So the colour order is **0 = yellow, 1 = white, 2 = red(gated)**, and the red state is conditional on
an additional per-panel flag. The concrete *labels* for the `{0,1,2}` enum (safe / PvP-open / closed
— shared with the combat-mode resolver, §6.5) are control-flow-inferred; the server-intended meaning
of each value is CAPTURE/DEBUGGER-PENDING. (Message-db ids and the colour order are CONFIRMED.)

> **Area-1 sub-region jingle (CONFIRMED side effect).** When the current area id is `1` and the
> current sub-region id is `12`, the footer-refresh path triggers a 2D sound (sound id `910001000`)
> once — a sub-region-entry jingle. Minor, but a real side effect of the radar draw.

### 3.7 Region-attribute table (CONFIRMED) — loaded from `regiontable*.bin`

Area/zone display data is read from an in-memory **region-attribute table** of fixed-size records
(**48 bytes each**, indexed by region index `0..31`). This table is loaded **directly from the
per-area `data/map{tag}/regiontable{tag}.bin` file** (a fixed `0x600` = 1536-byte read = 32 records
× 48 bytes — see §6.5), NOT from `mapsetting.scr`. Each record begins with a **name string** (the
radar footer area-name is formatted from this record), followed by attribute dwords. The dword at
record offset **`+0x28`** (the 11th dword, byte offset +40) is the **region-attribute enum** (`{0,1,2}`)
that drives both the footer caption colour (§3.6) and the combat-mode resolver (§6.5).

> **Open-question #8 RESOLVED.** The in-memory region/attribute table is loaded from
> `regiontable*.bin`, **independent of** the on-disk `mapsetting.scr` zone table (§6.3). They are two
> parallel tables: `regiontable*.bin` is the runtime radar/combat-mode source (32×48 attribute
> records), `mapsetting.scr` is the durable 52×84 zone bounding-box / fog table. Neither is loaded
> from the other.

The **per-area tile-set prefix** used in the §3.1 tile path is the **map-area tag** — the same `%s`
string the per-area loader uses to build *all four* per-area file basenames (`map{tag}.bin`,
`regiontable{tag}.bin`, `region{tag}.bin`, `npc{tag}.arr` — see §6.5). In memory this tag byte sits
just below the region-attribute table. It is the file-name tag, not a value pulled from inside a
record (a refinement over the prior "single byte stored adjacent to the table" wording — adjacency
is confirmed; its role is the loader tag). The runtime current-area id and current sub-region id are
read here but **set by the world-state packet** (owned elsewhere), not by the map UI. (CONFIRMED for
the layout and loader; the world-state-packet writers were not traced this lane — STATIC-HYPOTHESIS /
CAPTURE-PENDING for the packet origin.)

---

## 4. HUD radar — chrome (collapse / drag / click)

The radar's event handler routes its control actions. (CONFIRMED, except where tagged.)

| Control | Behaviour |
|---|---|
| **Click on the map body** | plays a UI sound and opens the larger map window (the radar's "open the big map" handoff — this opens the §5.6 tiled big-map, *not* the §5 BroodWar full-screen map directly). (STATIC-HYPOTHESIS — routed by the radar event handler but not exhaustively traced this lane) |
| **Collapse/expand button** | toggles the panel between expanded and a title-bar-only height, hides/shows the marker overlay, and on collapse clears the tile cache |
| **Help/zoom button** | opens a help string (the "zoom/help" label is STATIC-HYPOTHESIS) |
| **Drag handle + mouse move/up** | stores a drag delta and feeds it to the window manager to reposition the radar |

There is **no continuous zoom factor** for the radar: its scale is the fixed 1:8 of §2.2. "Zoom" in
the classic sense is collapse/expand only. (Note: the §5.6 *tiled big-map* DOES zoom — see §5.6.)
The zoomed-out overview is the separate BroodWar full-screen world-map overview texture (§5.3).
(CONFIRMED for the radar's fixed scale; "zoom button" label STATIC-HYPOTHESIS.)

---

## 5. BroodWar full-screen world map

> This section covers the **BroodWar** full-screen map (single per-area DDS + static landmark pins +
> GPS side panel). The separate **tiled, zoomable big-map** is §5.6.

### 5.1 Toggle, structure (CONFIRMED)

The full-screen map is a modal overlay toggled by the **`b` key** (key code 98 = `0x62`). The map
view is a nested element inside a larger **info panel** that also carries side sub-panels showing the
selected landmark's name, an info value, two integer fields, and a GPS-style coordinate readout
(§5.4). Opening plays a UI sound, makes the window visible, loads the per-area background (§5.2),
refreshes, and shows the side info sub-panels. Closing hides the window and releases its texture. The
window has two sibling overlays on the same surface: a list panel (a 5-row server/area list with a
16-character search box, atlas `data/ui/broodwarlist.dds`) and an ally-state panel (atlas
`data/ui/broodwarallystate.dds`). (CONFIRMED.)

### 5.2 Per-area background texture (CONFIRMED + SAMPLE-VERIFIED)

The map-mode background is a **single per-area texture** keyed on the current area id:

```
data/ui/map/map{areaId}.dds
```

It is loaded (FourCC `DXT2`), bound as the window background and the preview/list slots, and released
on the next swap; a load failure falls back to a default fill. (CONFIRMED for the path template and
the DXT2 codec; SAMPLE-VERIFIED that exactly one such file, `data/ui/map/map1.dds`, exists in the VFS
— see §6.2. Whether the single `map1.dds` serves as a global atlas for all areas, or whether per-area
sheets were intended but never shipped, is an Open Question.)

### 5.3 World-overview background texture (CONFIRMED + SAMPLE-VERIFIED)

The same window has a zoomed-out **world-overview** mode whose background is a single fixed texture:

```
data/ui/broodwarmap.dds
```

bound to the panel and all child rows (FourCC `DXT2`). (CONFIRMED for the path and codec;
SAMPLE-VERIFIED that the file exists — 1024×1024, see §6.2.)

### 5.4 Static landmark pins (CONFIRMED for the info fields; pin-placement fields not re-traced this lane)

Unlike the radar's live blips, the full-screen map's dots are **static landmark / point-of-interest
pins read from a precomputed data table**. For each landmark entry the build places a **15×15 marker
sprite** at the landmark's stored on-map pixel position, plus one fixed legend marker.

The selected-landmark **info** handler reads the following fields from a landmark record:

| Landmark field | Record offset | Meaning | Grade |
|---|---|---|---|
| Name id | +60 | display-name lookup id for the selected landmark | CONFIRMED |
| Sub-name | +64 | i16 sub-name field | CONFIRMED |
| Info field A | +66 | i16 field | CONFIRMED |
| Info field B | +68 | i16 field | CONFIRMED |
| Packed GPS coordinate | +72 | a **signed** 32-bit field driving the GPS readout (§5.5) | CONFIRMED |
| On-map pixel X | +32 | precomputed screen X of the pin (already in map-pixel space) | NOT RE-TRACED this lane |
| On-map pixel Y | +36 | precomputed screen Y of the pin | NOT RE-TRACED this lane |

The `+72` GPS field and the `+60/+64/+66/+68` info fields are confirmed in the selected-landmark info
handler. The `+32/+36` pin-placement pixel fields belong to the pin-draw routine, which was **not
re-traced this lane** (carried over from the prior pass — treat as STATIC-HYPOTHESIS pending a draw-
routine pass). The table's source file and full record layout beyond these fields were not traced.

### 5.5 GPS `D°M′S″` readout (CONFIRMED arithmetic; degree/minute/second unit STATIC-HYPOTHESIS)

The side info panel formats the selected landmark's packed **signed** 32-bit GPS field (record +72)
as a degree/minute/second-style triple by splitting it:

```
degrees = field / 1000000
minutes = (field % 1000000) / 1000
seconds = field % 1000
```

and pairs each component with a UI glyph string (message-db ids 2138 / 2139 / 2140 for the three
components; the degrees value is thousands-grouped). The world-unit↔degree mapping (the constant
relating a world position to that packed integer) was not derived; it lives in the landmark-table
loader, which was not traced. (CONFIRMED for the split arithmetic and the glyph message ids; the
degree/minute/second interpretation is STATIC-HYPOTHESIS.)

### 5.6 The tiled, zoomable big-map (CONFIRMED) — a separate surface

Distinct from the BroodWar full-screen map of §5.1–§5.5, the client has a **second** big-map surface:
a **zoomable, scrolling, per-cell-tiled** map that streams the **same** `data/effect/map/d{tag}x{cellX}z{cellZ}.bmp`
tiles as the radar (§3.1) — 1024-unit cell stride, 128×128 draws. It is opened from the radar's
click-body handoff (§4). Its model differs from both the radar and the BroodWar map:

- **Keyboard pan** via four directional input actions (the application-input pan ids), each moving
  the view by **±25 px**.
- **Integer zoom factor** held in two member fields; the cell math multiplies by the factor (a `×8`
  zoom step), so unlike the radar this surface *does* zoom.
- A **64×64** player arrow translated by **(−32, −32)** to centre it on its hotspot.

Because it streams the same `data/effect/map/*.bmp` tiles, the "tiles absent → blank background"
consequence (§3.2) applies here too. (CONFIRMED.)

#### 5.6a Total-map transform with pan applied (CYCLE 7 — CONFIRMED, operands byte-present)

The total map uses the **same** world→tile transform and scale as the corner minimap (`0.125`,
`÷1024`, `+20480` bias, `+9980` cell-index origin) — it is **the same transform at a larger extent
plus a user pan**, NOT a different zoom. The per-cell pixel size stays **128 px**. Two pan offsets
are stored on the panel struct: **`+0xD8` (pan X)** and **`+0xDC` (pan Y)**, each adjusted **±25 per
frame** by the four arrow-key input action codes (one code per direction, ±25 on the matching pan
axis). The cell-index math with pan applied:

```
cellX = ((worldX − 8×panX + 20480) >> 10) + 9980          // >>10 = ÷1024
cellZ = ((worldZ − 8×panY + 20480) / 1024) + 9980
```

- The pan term is **`8 × pan`** — the `8` is the inverse of the `0.125` scale, converting a map-pixel
  pan back into world units so the pan accumulates in screen pixels but is applied in world space.
- **Tile-count / viewport extent** is the render-target width ÷ 256 and height ÷ 256 (`256 = 2 × 128`,
  two-tile granularity) — this governs how many cell tiles wide and tall the total map draws, versus
  the fixed 2×2 window of the corner minimap.
- The total map can also be **rotated** (Z-rotation / translation / multiply matrices are present),
  the same matrix family the corner minimap uses for the player arrow.

So the total map differs from the corner minimap only by (a) a viewport-sized tile window
(render-target ÷ 256) instead of the fixed 2×2, and (b) the user pan offset at `+0xD8`/`+0xDC`. The
zoom level (per-cell pixel size) is unchanged. (CONFIRMED.)

### 5.7 BroodWar region map — authored-pixel marker records (CYCLE 7 — CONFIRMED)

The BroodWar full-screen map (§5.1–§5.5) is a **discrete region-selection screen, NOT a scaled world
map** — it has no world→pixel scale/offset transform of any kind. Per area it loads a single
`data/ui/map/map{areaId}.dds` plus the shared `data/ui/broodwarmap.dds` frame, then overlays
clickable **region markers** built as **15×15-px image components**.

**Marker hit-test.** A click (in panel-local pixel space) iterates the marker list and returns the
record whose **authored pixel box** `[markerX, markerX + 15] × [markerY, markerY + 15]` (16-px cells)
contains the click **and** whose region-id field matches the current area. A matching click issues a
move to that region (its destination/teleport target field).

**Marker source — a 40-byte fixed-record table loaded at boot.** The markers are loaded at boot from
a binary data table of **40-byte (`0x28`) fixed records**:

| Offset | Size | Field | Notes |
|---|---|---|---|
| +0x00 | 4 | `id` / region id | matched against the current area in the hit-test |
| +0x04 | 20 | `name[20]` (CP949) | region display name |
| +0x08 | 4 | destination / teleport target | the move target on a marker click |
| +0x10 | 4 | runtime extra-data pointer | allocated/freed at runtime; not on-disk content |
| +0x20 | 4 | marker **pixel X** | authored screen X of the marker (already in map-pixel space) |
| +0x24 | 4 | marker **pixel Y** | authored screen Y of the marker |

The pixel positions at `+0x20`/`+0x24` are **authored directly in the data file** — they are **not**
computed from any world→pixel scale. This map is a hand-placed region-selection screen, which is why
it carries no `0.125`/`+66.5`-style transform. (CONFIRMED.)

> **Corner-minimap caption table (CONFIRMED).** A separate **stride-48 region-name/caption table**
> (max 32 entries, indexed by region id `< 32`) is read by the radar draw to label tiles — this is
> the same in-memory region-attribute table loaded from `regiontable{tag}.bin` (§6.5), reused here
> for the footer/tile caption. It is unrelated to the BroodWar 40-byte marker records above.

---

## 6. VFS reality — map art and on-disk map data (SAMPLE-VERIFIED)

This section is the on-disk ground truth, read directly from the real client VFS (43,347 entries),
independent of any code path. All findings here are **SAMPLE-VERIFIED** unless tagged otherwise.

### 6.1 What is NOT there (the load-bearing negative)

- **No per-area minimap bitmaps exist anywhere in the VFS.** No `.dds`/`.bmp`/`.png`/`.tga` lives
  under any `data/mapNNN/` directory outside its `texture/` subfolder, and the `data/effect/map/
  d*x*z*.bmp` tiles the radar streams (§3.1) are absent. Keyword searches for minimap / worldmap /
  radar / navi / thumb / zoom / overview on file names returned zero hits (the one near-match,
  `data/effect/tex/minibar.tga`, is an unrelated UI progress-bar texture). The radar therefore renders
  blank-with-blips against the shipped data unless the tiles are generated or supplied externally.
- **Zone display names do NOT live in `msg.xdb`.** There is no area-name string range in the message
  database (no IDs in a `10000`-style block naming areas); zone names are embedded directly in
  `mapsetting.scr` records (§6.3).

### 6.2 Map art that DOES exist (SAMPLE-VERIFIED)

| VFS path | Format | Dimensions | Codec | Size (bytes) | Role |
|---|---|---|---|---|---|
| `data/ui/map/map1.dds` | DDS | 512 × 512 | DXT2 | 262,272 | in-game world-map panel art (§5.2) |
| `data/ui/broodwarmap.dds` | DDS | 1024 × 1024 | DXT2 | 1,048,704 | world-overview background (§5.3) |
| `data/ui/direction.dds` | DDS | 16 × 16 | uncompressed RGBA | 640 | compass / direction arrow icon |
| `data/ui/map_userpoint.tga` | TGA | 64 × 64 | 32 bpp uncompressed | 16,428 | player / waypoint pin marker |

DDS dimensions were read directly from the file header (height at byte 0x0C, width at 0x10, FourCC at
0x54). Each file is a single instance in the VFS. Only `map1.dds` exists in `data/ui/map/` — there is
no `map2.dds`…`map9.dds`. (SAMPLE-VERIFIED.)

### 6.3 `mapsetting.scr` — zone bounding-box + fog table (SAMPLE-VERIFIED)

| Attribute | Value |
|---|---|
| Path | `data/script/mapsetting.scr` |
| File size | 4,368 bytes |
| Record stride | **84 bytes** (`4368 / 84 = 52`, remainder 0) |
| Record count | **52** |
| Encoding | CP949 for the name field |

Per-record layout (84 bytes):

| Offset | Size | Type | Field | Grade | Notes |
|---|---|---|---|---|---|
| 0x00 | 4 | int32 LE | `zone_id` | SAMPLE-VERIFIED | sequential `1..` with gaps (id 5 skipped; 100, 203–208, 300 present); matches area ids |
| 0x04 | 36 | char[36] CP949 | `zone_name` | SAMPLE-VERIFIED | null-terminated Korean display name (e.g. 하왕관, 염무진, 사해주) |
| 0x28 | 4 | int32 LE | `world_min_x` | PLAUSIBLE | zone bounding-box X lower bound (world units) |
| 0x2C | 4 | int32 LE | `world_min_z` | PLAUSIBLE | zone bounding-box Z lower bound |
| 0x30 | 4 | int32 LE | `world_max_x` | PLAUSIBLE | zone bounding-box X upper bound |
| 0x34 | 4 | int32 LE | `world_max_z` | PLAUSIBLE | zone bounding-box Z upper bound |
| 0x38 | 4 | int32 LE | `flags_a` | UNKNOWN | constant `0x012C0001` in 50 of 52 records (two exceptions) |
| 0x3C | 4 | int32 LE | `flags_b` | UNKNOWN | usually `0x00000001`; one record `0` |
| 0x40 | 4 | float32 LE | `fog_density` | PLAUSIBLE | clusters at 1.70 (outdoor), 1.30 (interior/cave), 1.50 (rare) |
| 0x44 | 4 | int32 LE | `unknown_0x44` | UNKNOWN | first record `1`, rest `0` |
| 0x48 | 4 | int32 LE | `unknown_0x48` | UNKNOWN | typically `0` or `-1` |
| 0x4C | 4 | int32 LE | `unknown_0x4C` | UNKNOWN | high byte constant `0x64` (=100); low 24 bits vary |
| 0x50 | 4 | int32 LE | `unknown_0x50` | UNKNOWN | always `0` in all 52 records |

**Zone display names are authoritative here, not in `msg.xdb`.** The 52 decoded records cover the
playable zones (areas 1–47 with gaps, plus special ids 100/203/204/205/208/300). Example: id 1 =
하왕관, bounds (−10240, −7168)–(5120, 10240), fog 1.70. (SAMPLE-VERIFIED for stride and the
`zone_id` / `zone_name` fields; the bounding-box and fog fields are PLAUSIBLE.)

### 6.4 The per-area map binaries — the 4-file loader (CONFIRMED)

The per-area map loader opens **four coupled files** per area, all keyed on the same map-area tag
(used as the `%s` for *both* the `map{tag}` directory and the file basename — §3.7). A failure on any
one frees everything and aborts the load.

| # | File | Read | Destination / role | Grade |
|---|---|---|---|---|
| 1 | `data/map{tag}/map{tag}.bin` | fixed **520 bytes** (`0x208`) | a 520-byte header blob; near-all-zero in samples; full purpose unknown | CONFIRMED (size) |
| 2 | `data/map{tag}/regiontable{tag}.bin` | fixed **1536 bytes** (`0x600`) | the in-memory **region-ATTRIBUTE table** = 32 records × 48 bytes (§6.5) | CONFIRMED |
| 3 | `data/map{tag}/region{tag}.bin` | variable | the **region GRID** (width/height/byte-grid/origin — §6.6) | CONFIRMED |
| 4 | `data/map{tag}/npc{tag}.arr` | `size/28 + 1` records × **28 bytes** | the NPC spawn array (matches the `npc{tag}.arr` 28-byte record convention) | CONFIRMED |

> **MAJOR CORRECTION over the prior §6.4.** A prior pass described `regiontableNNN.bin` as a
> **52-record × 32-byte sub-zone *label* table** (float `center_x` / `center_z` + a CP949 name). The
> binary's loader does **not** read it that way: it reads a **fixed 1536-byte (32 × 48) region-
> ATTRIBUTE table** straight into the in-memory region table (§6.5), indexed by the region-grid byte
> `0..31`, carrying the combat-mode/zone-type enum at record `+0x28` and a leading name string. The
> old "1664 bytes / 32-byte stride / 52 records" measurement does not match the code's hard
> 1536-byte read — either the prior pass measured a different file/build, or it conflated
> `regiontable*.bin` with a different per-area table. The grid file `region{tag}.bin`
> (width/height/byte-grid/origin) is a **separate third file** the prior §6.4/§6.5 did not describe at
> all (now §6.6).

### 6.5 `regiontable{tag}.bin` — the 32×48 region-attribute table (CONFIRMED)

| Attribute | Value |
|---|---|
| Path pattern | `data/map{tag}/regiontable{tag}.bin` |
| Read size | fixed **1,536 bytes** (`0x600`) |
| Record stride | **48 bytes** |
| Record count | **32** (indexed by region index `0..31`) |
| Encoding | CP949 for the leading name string |

Each 48-byte record is loaded straight into the in-memory region-attribute table (§3.7) and indexed
by the region-grid byte (§6.6). Known fields:

| Offset | Size | Type | Field | Grade | Notes |
|---|---|---|---|---|---|
| 0x00 | — | char[] CP949 | `region_name` (leading) | CONFIRMED | the record begins with the name string the radar footer formats (`%s`) for the current sub-region |
| 0x28 | 4 | int32 LE | `region_attribute` (combat-mode / zone-type enum) | CONFIRMED layout / CAPTURE-PENDING value semantics | the 11th dword; `{0,1,2}` drives both the footer caption colour (§3.6) and the combat-mode resolver (§6.6); control-flow labels safe / PvP-open / closed are inferred, server intent capture-pending |

The remaining record dwords were not enumerated. (CONFIRMED for the read size, 48-byte stride,
32-record count, the leading name string, and the `+0x28` enum's *role*; the enum's concrete *value
semantics* are CAPTURE/DEBUGGER-PENDING.)

> **Historical VFS measurement (carried, flagged DISCREPANT).** A prior VFS sample read measured the
> shipped `regiontableNNN.bin` as **1,664 bytes** (identical across map001/002/003), which factors as
> 52 × 32. The binary instead reads a fixed 1,536 bytes (32 × 48) — i.e. the loader ignores any bytes
> beyond `0x600`. This discrepancy is unresolved (different file/build, or a sample-vs-loader
> mismatch); the **loader's 32×48 reading is authoritative for a reimplementation**, as it is what
> the client actually consumes. The prior pass's CP949 sub-zone names (e.g. area 1 하왕관 →
> 폐어촌 / 구룡부; area 0 `map000` lobby table leading with "캐릭터선택창") were observed in the
> file bytes but their offsets do not match the loader's record layout and are not used by the radar/
> combat path.

### 6.6 `region{tag}.bin` — the region GRID (CONFIRMED, NEW)

The region GRID file (the file the "region.bin" lookup actually reads) was undocumented by the prior
spec. It is the third file opened by the §6.4 loader and provides a world-XZ → region-index lookup.

| Attribute | Value |
|---|---|
| Path pattern | `data/map{tag}/region{tag}.bin` |
| Encoding | binary, little-endian; no text |

On-disk layout, read in order:

| Order | Size | Type | Field | Notes |
|---|---|---|---|---|
| 1 | 4 | uint32 LE | `width` | grid columns |
| 2 | 4 | uint32 LE | `height` | grid rows |
| 3 | `width × height` | byte[] | `grid` | one byte per cell; each cell byte is a **region INDEX `0..31`** into the §6.5 attribute table |
| 4 | 4 | int32 LE | `origin_x` | **signed** world-X of grid cell (0,0) |
| 5 | 4 | int32 LE | `origin_z` | **signed** world-Z of grid cell (0,0) |

**The grid lookup (world XZ → region-index byte).** Given a world position `(X, Z)`:

```
if grid not loaded -> log "region data is not loaded", return 0
col = (X - origin_x) / 256        // SIGNED subtract, SIGNED divide
row = (Z - origin_z) / 256
idx = col + row * width           // row-major
if idx < (width * height) -> return grid[idx]   // unsigned byte = the region index 0..31
else -> log error, return 0
```

- The grid cell stride is **256 world units** (NOT the 1024-unit terrain cell).
- Layout is **row-major**: `col + row * width`.
- Both the unloaded case and an out-of-bounds index return region index `0` (with an error-log line).

(CONFIRMED — control flow + operands.)

> **How the region index is consumed (CONFIRMED).** The grid byte indexes the §6.5 attribute table;
> the attribute record's `+0x28` enum then drives:
> - **Combat-mode resolution.** The combat resolver looks up the **current** sub-region record AND the
>   **target-tile** record (via the grid lookup at the destination), reads each `+0x28`, and returns
>   `1` (open / PvP) if either is `1`, `2` (closed) if both are non-zero, else `0` (safe).
> - **Movement gating.** A destination tile whose attribute record `+0x28 == 2` is movement-restricted
>   (closed): the move is rejected with a chat notice (message-db id 74309).
> - **The radar footer colour** (§3.6), keyed on the *current* sub-region record's `+0x28`.
>
> The `{0,1,2}` value labels (safe / PvP-open / closed) are control-flow-inferred; the server-intended
> meaning is CAPTURE/DEBUGGER-PENDING.

### 6.7 Adjacent per-area files (partial)

| File | Size | Status |
|---|---|---|
| `data/mapNNN/regionNNN.bin` | varies (32 / 1680 / 1776 / 4096 across sampled areas) | the **region GRID** (now decoded — §6.6: `u32 width`, `u32 height`, `width×height`-byte grid, `i32 origin_x`, `i32 origin_z`); the variable size matches different per-area grid dimensions |
| `data/mapNNN/mapNNN.bin` | 520 bytes (consistent) | the fixed `0x208` = 520-byte header blob the loader reads (§6.4 file 1); near-all-zero in samples, full purpose unknown (one non-zero byte = 16 at offset 4 in area 0) |

These are noted for completeness; the grid (§6.6) is required for the combat-mode / movement-gating
path but neither is required to *render* either map surface. (CONFIRMED size / UNKNOWN payload.)

---

## 7. Godot reconstruction guidance

Engineering guidance for `05.Presentation/MartialHeroes.Client.Godot`; not a legacy-format spec.

### 7.1 HUD radar

1. **Projection.** Reproduce §2.2 exactly: for each tracked actor compute
   `rel = actorWorldXZ − localPlayerWorldXZ`, then `px = rel.X × 0.125 + 66.5`,
   `py = rel.Z × 0.125 + 66.5`; draw only when both are in `[0, 133]`. The local player is always at
   the body centre `(66.5, 66.5)`. // spec: Docs/RE/specs/minimap.md §2.2
2. **Background.** The per-cell `data/effect/map/*.bmp` tiles do not exist in the VFS (§3.2). Render
   either a runtime-generated top-down cell thumbnail from the terrain heightfield, or a plain
   background with blips only. Do not depend on the streamed-tile path.
3. **Blips.** Place one sprite per in-range actor, choosing the marker by the §3.4 class/condition
   table; rotate the local-player arrow from the player's heading (§3.5). Apply the small per-blip
   anchor nudges. The blip-art atlas ids are interop facts; the id→file mapping is gated on the UI
   texture manifest.
4. **Chrome.** Collapse/expand, drag-to-move, and a click-body→open-region-map handoff (§4). The
   radar has a fixed 1:8 scale — there is no continuous zoom.

### 7.2 Full-screen world map

1. Toggle on the **`b` key**.
2. Background = `data/ui/map/map{areaId}.dds` for map mode (only `map1.dds` ships — §6.2), or
   `data/ui/broodwarmap.dds` for the overview mode.
3. Draw **static** landmark pins from a data table (15×15 sprites at each entry's precomputed map
   pixel `+32`/`+36` — these pin-placement offsets were not re-traced this lane; treat as
   STATIC-HYPOTHESIS, §5.4), and a GPS `D°M′S″` readout from the packed field at `+72` (CONFIRMED)
   split per §5.5.
4. Zone names come from `mapsetting.scr` (§6.3), **not** `msg.xdb`.

---

## 8. Open questions

1. **Blip / map-art atlas file names (gated).** All radar blips and the map base bind by an integer
   texture-group id (13/30/40/50/51/52/53/55/56/57/58/75) through the UI texture-id resolver, not a
   file-name string. Resolving each id to its actual DDS requires the VFS UI texture manifest. Sizes,
   coordinates, and roles are recovered (§3.3–§3.4); only the art-file name is gated.
2. **Are the `data/effect/map/*.bmp` per-cell tiles shipped, generated, or removed?** The code only
   loads them with a per-panel cache; they are absent from the shipped VFS (§3.2). Whether the
   original ever shipped them per area, produced them with an offline tool, or generated them at
   runtime, is unresolved. This determines whether a faithful client must re-render cell thumbnails.
3. **`map1.dds` — global atlas or missing per-area sheets?** Only `data/ui/map/map1.dds` exists, yet
   the code keys the path on the area id. Either the single sheet is reused for all areas, or per-area
   sheets were intended but never shipped.
4. **Landmark-table and area-info-table sources.** The static-pin landmark table (with precomputed
   map pixels at `+32/+36` and the GPS field at `+72`) and the 32-entry in-memory area-info table
   (with the state enum and the tile-prefix byte) are populated by an untraced loader. Their source
   file(s) and full record layouts were not recovered.
5. **GPS unit constant.** The packed GPS field is split `/1000000`, `%1000000/1000`, `%1000` and
   formatted as degree/minute/second glyphs (§5.5), but the world-unit↔degree constant was not
   derived.
6. **Player-arrow rotation handedness (§3.5).** Sign/handedness vs the world's Z-negation should be
   checked against a running client to avoid a mirrored arrow.
7. **RESOLVED — `regiontableNNN.bin` layout and its relationship to `regionNNN.bin`.** The loader
   reads `regiontable{tag}.bin` as a fixed 32 × 48-byte **region-ATTRIBUTE** table (§6.5) and
   `region{tag}.bin` as the separate **region GRID** file (§6.6); the grid's byte cells (region index
   `0..31`) index the attribute table. The only residual is the **remaining attribute-record dwords**
   beyond the leading name string and the `+0x28` enum, which were not enumerated.
8. **RESOLVED — in-memory region table vs on-disk `mapsetting.scr`.** The runtime region-attribute
   table is loaded **directly from `regiontable{tag}.bin`**, independent of `mapsetting.scr` (§3.7,
   §6.5). The two are parallel tables; neither is loaded from the other.
9. **Region-attribute enum value semantics (CAPTURE/DEBUGGER-PENDING).** The `{0,1,2}` labels
   (safe / PvP-open / closed) are control-flow-inferred from the combat-mode resolver and footer
   colouring (§6.6); the server-intended meaning of each value needs a live capture/debugger read.

---

## Cross-references

- **Terrain & cell convention:** `Docs/RE/formats/terrain.md` (1024-unit cells, cell index math)
- **Environment / fog:** `Docs/RE/specs/environment.md`, `Docs/RE/formats/environment_bins.md`
  (`fog_density` also surfaces in `mapsetting.scr` §6.3)
- **UI toolkit & texture-id resolution:** `Docs/RE/specs/ui_system.md`, `Docs/RE/specs/input_ui.md`
- **Message database:** `data/script/msg.xdb` (zone names are NOT here — see §6.1)
- **World coordinate conventions:** `CLAUDE.md` (negate-Z world geometry)
- **Glossary:** `Docs/RE/names.yaml` · **Provenance:** `Docs/RE/journal.md`
- **Godot implementation targets:** `05.Presentation/MartialHeroes.Client.Godot/HUD/` (radar),
  `05.Presentation/MartialHeroes.Client.Godot/World/` (full-screen map surface)
