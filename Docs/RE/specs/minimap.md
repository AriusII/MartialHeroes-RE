# Minimap & World Map — HUD Radar and Full-Screen Map, Clean-Room Specification

> Clean-room neutral spec. Promoted from dirty-room analyst notes by the asset-spec-author.
> No legacy symbols, no addresses, no pseudo-code. Routines are referred to by role only.
> Describes the two map UIs (the always-on HUD radar and the full-screen world map), the
> world→minimap pixel transform, the per-cell tile-streamed radar background, live-actor blip
> selection, the full-screen map's per-area artwork and static landmark pins, and the on-disk
> map data files — so `Client.Application` and
> `05.Presentation/MartialHeroes.Client.Godot` can rebuild both surfaces from scratch.
>
> Every magic constant an engineer cites must reference this file. Asset byte layouts for the
> data tables live in §6 and in `Docs/RE/formats/` where a dedicated format doc exists.

---

## Status block

| Attribute | Value |
|---|---|
| `status` | `code-confirmed` for both UI surfaces, the projection math, the tile-streaming model, and blip selection; `sample-verified` for the map art inventory and the `mapsetting.scr` / `regiontable*.bin` data layouts |
| `sample_verified` | `true` for the VFS reality (§5–§6): the map-art census and the `.scr`/`.bin` strides/fields were read directly from the 43,347-entry client VFS; `false` for the UI-runtime behaviour, which is static code reading only |
| `binary_analysed` | `doida.exe` (legacy client) — both map window classes, the projection helper, the per-cell tile loader, and the area-metadata / landmark tables |
| `confidence` | Per-claim tags inline: CODE-CONFIRMED / SAMPLE-VERIFIED / CAPTURE-VERIFIED / PLAUSIBLE |

> **Capture status — read this first.** No Wireshark oracle was available for this lane. Every
> behavioural claim below is **CODE-CONFIRMED** (read statically from the binary) or **PLAUSIBLE**
> (inferred), and every one of them is **CAPTURE-UNVERIFIED**. The radar consumes only client-side
> actor state (each actor's last-known world position from the most recent movement packet); no
> map-specific packet was observed, and no behaviour here has been confirmed against live traffic.
> The VFS-data claims in §5–§6 are **SAMPLE-VERIFIED** against real shipped files, independent of
> any capture.

---

## 1. Overview — two independent map surfaces

The client exposes **two distinct map UIs**. They share the in-game widget toolkit but have
separate code and separate rendering models. (CODE-CONFIRMED.)

| Surface | Role | Background source | Pins/blips | Toggle |
|---|---|---|---|---|
| **HUD radar** | "where am I + nearby actors" corner widget, always on | streamed 3×3 ring of per-cell bitmaps, re-stitched each frame around the player | **live** per-actor blips (NPC/mob/player/party/GPS + a rotated player arrow) | collapse/expand button; click body → open full map |
| **Full-screen world map** | "open the atlas" overlay, nested in an info panel | single per-area texture (or a world-overview texture) | **static** landmark pins from a data table | `b` key |

The radar is centred on the local player and redraws every visible frame. The full-screen map is a
modal overlay keyed on the current area id and decorated with point-of-interest pins read from a
precomputed table. They share nothing but the widget toolkit. (CODE-CONFIRMED, CAPTURE-UNVERIFIED.)

---

## 2. HUD radar — geometry and the world→minimap transform

### 2.1 Widget body geometry (CODE-CONFIRMED)

The radar widget is **135 px wide**; its map body is a **133 × 133 px** square. The local player is
always rendered **dead-centre** of that body. Above the body sits a title bar (with three 11×11
control buttons — see §4), and below it a footer with three labels (area name, integer coord-X,
integer coord-Z). A marker-overlay sub-panel covers the 133×133 body and holds the live blip
elements (§3.3).

### 2.2 The projection (THE core finding — CODE-CONFIRMED, BYTE-VERIFIED)

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
| Reference position | local player's **last-packet world position** | `rel` is measured against it |
| Cull rule | draw a blip only if `0 ≤ px ≤ 133` **and** `0 ≤ py ≤ 133` | actors outside the body are dropped |

The two constants `0.125` and `66.5` are **byte-verified** from the binary's immediate operands.
(CODE-CONFIRMED, BYTE-VERIFIED, CAPTURE-UNVERIFIED.)

### 2.3 Visible window (derived)

The visible half-extent in world units is `66.5 / 0.125 ≈ 532` units in each axis from the player —
a roughly `1064 × 1064`-unit square. Since one terrain cell is **1024** world units, the radar shows
approximately the player's own cell plus half a cell on every side. (PLAUSIBLE — a direct
consequence of §2.2.)

### 2.4 `rel` is measured against the last-packet position (CODE-CONFIRMED)

Both the actor position and the local-player reference position are each actor's **last-known
position from its most recent movement packet** (not an interpolated render position). The radar is
therefore a snapshot-of-network-state view; smoothing/interpolation that the world view applies is
not applied here. (CODE-CONFIRMED, CAPTURE-UNVERIFIED — the position source is a movement packet,
but the wire layout of that packet is owned by `Docs/RE/opcodes.md` / `packets/`, not this spec.)

---

## 3. HUD radar — background streaming and live blips

### 3.1 Background is streamed per-cell tiles, NOT a per-area sheet (CODE-CONFIRMED)

The radar background is **not** a single per-area texture. Each frame the radar streams a **3×3 ring
of per-cell bitmap tiles** centred on the player's current cell and re-stitches them under the fixed
centre, so the background scrolls pixel-smooth as the player moves.

Tile path template:

```
data/effect/map/d{prefix}x{cellX}z{cellZ}.bmp
```

| Token | Source |
|---|---|
| `{prefix}` | the **per-area tile-set prefix character** (one byte stored adjacent to the area-info table; see §3.5) |
| `{cellX}` | the player's current cell column index; the 3×3 loop visits this ± 1 |
| `{cellZ}` | the player's current cell row index; the 3×3 loop visits this ± 1 |

Cell math, per axis (the same cell convention used by terrain streaming, re-centred for the 3×3
stitch):

```
cellX = (int)(playerWorldX + 20480.0) / 1024 + 9980     // ± 1 across the 3×3 ring
cellZ = (int)(playerWorldZ + 20480.0) / 1024 + 9980     // ± 1 across the 3×3 ring
```

Each tile is a **128 px** source bitmap mapped 1:1 over its **1024**-unit cell. Tiles load through
the file chokepoint with animated-texture options and are kept in a **per-panel cache** (a sorted
list, find-or-insert by path); a cache miss allocates a managed texture and inserts it. On bind
failure the tile slot falls back to a default fill texture. Only the visible slice of each 128 px
tile inside the 133 px window is drawn — a scrolling tile window that keeps the player centred.
(CODE-CONFIRMED, CAPTURE-UNVERIFIED.)

### 3.2 On-disk reality of the tiles — they do NOT ship (SAMPLE-VERIFIED)

The `data/effect/map/d*x*z*.bmp` per-cell tiles are **absent from the entire VFS**: a full census of
the 43,347-entry archive found **no per-area minimap bitmaps anywhere** (zero `.dds`/`.bmp`/`.png`/
`.tga` under any `data/mapNNN/` directory outside its `texture/` subfolder, and no minimap/radar/
overview-named files except an unrelated UI progress-bar texture). Consequently, in the shipped
client the radar background-streaming path would hit the bind-failure branch and fall back to the
default fill — i.e. a **blank radar** — unless those tiles are supplied externally or generated.
(SAMPLE-VERIFIED.)

> **Reimplementation consequence.** A faithful Godot radar must either (a) re-render top-down cell
> thumbnails itself from terrain/`.ted` heightfield data, or (b) render only the live blips over a
> plain background. Streaming `data/effect/map/*.bmp` is a dead path against the shipped VFS.

### 3.3 Blip elements (CODE-CONFIRMED)

The marker overlay holds four reusable blip image elements plus a hover tooltip label. Each blip's
artwork is bound by an **integer texture-group id** through the UI texture-id resolver (the same id→
texture manifest the rest of the in-game UI uses; the id→file name mapping is gated on the VFS UI
texture manifest — see Open Questions).

| Element | Size | Texture-group id | Role |
|---|---|---|---|
| Generic actor blip | 4×4 | 52 | NPC / mob / party member dot |
| Party/lead blip | 10×10 | 30 | the active/lead tracked party member |
| Local-player arrow | 16×16 | 13 | rotated to face the player's heading |
| GPS / escort marker | 16×16 | 75 | "you are here" / escort target |
| NPC-name tooltip | label 100×16 | — | shown when the cursor hovers a blip |

### 3.4 Which actors become blips (CODE-CONFIRMED)

Each frame the radar walks the active actor list and, for every actor whose projected `(px, py)`
lands inside the 133×133 body (§2.2 cull rule), draws a blip whose texture-group id is chosen by the
actor's **class byte** (a small per-actor enum: `1 = player`, `2 = NPC`, `3 = mob`) plus a sub-type
condition. (In the binary the class byte is read at a fixed actor-struct offset and biased by +96
before mapping to a texture group; the bias is an implementation detail of the resolver, not a value
an engineer needs.)

| Actor class | Condition | Texture-group id |
|---|---|---|
| NPC (2) | escort / quest | 58 |
| NPC (2) | special | 57 |
| NPC (2) | default | 51 |
| NPC (2) | selected / targetable override | 56 |
| Mob (3) | default | 50 |
| Mob (3) | aggro / blinking (an 8-phase tick flash, phases > 3) or flagged target | 56 |
| Player (1) | same faction / party | 53 |
| Player (1) | enemy faction (war/PvP) | 55 |
| Party roster | active/lead member | (party blip, 10×10, id 30) |
| Rank/escort roster | present | 51 |
| GPS / escort target | present | 75 |
| Local player | always (drawn last, centred) | 13 (rotated arrow) |

NPC blips additionally drive the hover tooltip: the NPC's display name is looked up from the NPC
template table and shown in the 100×16 label when the cursor is over the blip. (CODE-CONFIRMED,
CAPTURE-UNVERIFIED. Texture-group ids are interop facts; the semantic role of each was inferred from
the class byte and the surrounding lookups.)

### 3.5 Player-arrow rotation (CODE-CONFIRMED)

The local-player arrow (id 13) is rotated to point the way the player faces. The update builds a
Z-rotation from the player's facing orientation, translates by **(−6, −6)** to centre the 16×16
sprite on its hotspot, and multiplies that into the element's local transform so the arrow tracks
heading. Small per-blip vertical anchor nudges are also applied (generic blip `+14` px in Y; party
blip `+11`; GPS marker `(−8, +8)`; player arrow `(+1, +16)` before rotation); these are
sprite-anchor offsets, not part of the §2.2 transform.

> **Handedness caveat (UNVERIFIED).** The exact rotation sign (clockwise vs counter-clockwise, and
> whether it matches the world's Z-negation convention noted in `CLAUDE.md`) was not confirmed
> against a running client. A Godot port should validate the arrow direction live to avoid a
> mirrored arrow.

### 3.6 Footer labels (CODE-CONFIRMED)

Each frame the footer is refreshed: coord-X = integer of the player world X, coord-Z = integer of
the player world Z, area name from the area-info table (§3.7), and a colour-coded state caption.
The area-state value selects one of three captions (drawn white / yellow / red-ish) for the
safe / contested / war states (§3.7). (CODE-CONFIRMED, CAPTURE-UNVERIFIED.)

### 3.7 Area-info table (CODE-CONFIRMED)

Area display data is read from an in-memory **area-info table** of fixed-size records (48 bytes each
= 12 dwords), indexed by area id `0..31`. A dword field within the record (the 11th dword, byte
offset +40 in the record) is an **area-state enum** (`0 = safe`, `1 = contested`, `2 = war`) driving
the footer caption colour. The **per-area tile-set prefix character** used in the §3.1 tile path is a
single byte stored adjacent to (just below) this table. The full record meaning beyond the state
enum was not enumerated. The runtime current-area id and current sub-region id are set by the
world-state packet (owned elsewhere), not by the map UI. (CODE-CONFIRMED, CAPTURE-UNVERIFIED.)

> This in-memory area-info table is **distinct** from the on-disk `mapsetting.scr` zone table
> (§6.1). The relationship between the two was not traced; the on-disk table is the durable
> sample-verified source and is the one a reimplementation should read.

---

## 4. HUD radar — chrome (collapse / drag / click)

The radar's event handler routes its control actions. (CODE-CONFIRMED, CAPTURE-UNVERIFIED.)

| Control | Behaviour |
|---|---|
| **Click on the map body** | plays a UI sound and opens the larger map/quest window (the radar's "open the big map" handoff — note this opens a separate region-map window, *not* the §5 full-screen world map directly) |
| **Collapse/expand button** | toggles the panel between expanded and a title-bar-only height, hides/shows the marker overlay, and on collapse clears the tile cache |
| **Help/zoom button** | opens a help string (the "zoom/help" label is PLAUSIBLE) |
| **Drag handle + mouse move/up** | stores a drag delta and feeds it to the window manager to reposition the radar |

There is **no continuous zoom factor** for the radar: its scale is the fixed 1:8 of §2.2. "Zoom" in
the classic sense is collapse/expand only; the zoomed-out overview is the separate full-screen
world-map overview texture (§5.3). (CODE-CONFIRMED; "zoom button" label PLAUSIBLE.)

---

## 5. Full-screen world map

### 5.1 Toggle, structure (CODE-CONFIRMED)

The full-screen map is a modal overlay toggled by the **`b` key** (key code 98). The map view is a
nested element inside a larger **info panel** that also carries side sub-panels showing the selected
landmark's name, an info value, two integer fields, and a GPS-style coordinate readout (§5.4).
Opening plays a UI sound, makes the window visible, loads the per-area background (§5.2), refreshes,
and shows the side info sub-panels. Closing hides the window and releases its texture. The window has
two sibling overlays on the same surface: a list panel (a 5-row server/area list with a 16-character
search box, atlas `data/ui/broodwarlist.dds`) and an ally-state panel (atlas
`data/ui/broodwarallystate.dds`). (CODE-CONFIRMED, CAPTURE-UNVERIFIED.)

### 5.2 Per-area background texture (CODE-CONFIRMED + SAMPLE-VERIFIED)

The map-mode background is a **single per-area texture** keyed on the current area id:

```
data/ui/map/map{areaId}.dds
```

It is loaded, bound as the window background and the preview/list slots, and released on the next
swap; a load failure falls back to a default fill. (CODE-CONFIRMED for the path template;
SAMPLE-VERIFIED that exactly one such file, `data/ui/map/map1.dds`, exists in the VFS — see §6.2.
Whether the single `map1.dds` serves as a global atlas for all areas, or whether per-area sheets
were intended but never shipped, is an Open Question.)

### 5.3 World-overview background texture (CODE-CONFIRMED + SAMPLE-VERIFIED)

The same window has a zoomed-out **world-overview** mode whose background is a single fixed texture:

```
data/ui/broodwarmap.dds
```

bound to the panel and all child rows. (CODE-CONFIRMED for the path; SAMPLE-VERIFIED that the file
exists — 1024×1024, see §6.2.)

### 5.4 Static landmark pins (CODE-CONFIRMED)

Unlike the radar's live blips, the full-screen map's dots are **static landmark / point-of-interest
pins read from a precomputed data table**. For each landmark entry the build places a **15×15 marker
sprite** at the landmark's stored on-map pixel position, plus one fixed legend marker. Each landmark
record carries:

| Landmark field | Record offset | Meaning |
|---|---|---|
| On-map pixel X | +32 | precomputed screen X of the pin (already in map-pixel space) |
| On-map pixel Y | +36 | precomputed screen Y of the pin |
| Packed GPS coordinate | +72 | a 32-bit field driving the GPS readout (§5.5) |

The table's source file and full record layout (beyond these three fields) were not traced.
(CODE-CONFIRMED, CAPTURE-UNVERIFIED.)

### 5.5 GPS `D°M′S″` readout (CODE-CONFIRMED, unit PLAUSIBLE)

The side info panel formats the selected landmark's packed 32-bit GPS field (record +72) as a
degree/minute/second-style triple by splitting it:

```
degrees = field / 1000000
minutes = (field % 1000000) / 1000
seconds = field % 1000
```

and pairs each component with a UI glyph string. The world-unit↔degree mapping (the constant
relating a world position to that packed integer) was not derived; it lives in the landmark-table
loader, which was not traced. (CODE-CONFIRMED for the split arithmetic; the degree/minute/second
interpretation is PLAUSIBLE; CAPTURE-UNVERIFIED.)

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

### 6.4 `regiontableNNN.bin` — sub-zone point-label table (SAMPLE-VERIFIED stride; field layout PLAUSIBLE)

| Attribute | Value |
|---|---|
| Path pattern | `data/mapNNN/regiontableNNN.bin` |
| File size | 1,664 bytes (identical across map001/002/003) |
| Record stride | **32 bytes** (`1664 / 32 = 52`, remainder 0) |
| Record count | **52** |
| Encoding | CP949 for the label field |

Per-record layout (32 bytes):

| Offset | Size | Type | Field | Grade | Notes |
|---|---|---|---|---|---|
| 0x00 | 4 | float32 LE | `center_x` | PLAUSIBLE | world X of the sub-zone label anchor |
| 0x04 | 4 | float32 LE | `center_z` | PLAUSIBLE | world Z of the sub-zone label anchor |
| 0x08 | 8 | — | `unknown_08` | UNKNOWN | zero in all observed records |
| 0x10 | 16 | char[16] CP949 | `sub_zone_name` | PLAUSIBLE | null-terminated Korean landmark/sub-zone label |

Example (area 1, 하왕관): `center=(−1574, 2698)` → 폐어촌 (Deserted Fishing Village);
`(2626, 655)` → 구룡부 (Dragon Port). Area 0 (`map000`) is the character-select / lobby zone; its
`regiontable000.bin` begins with the CP949 string "캐릭터선택창" (Character Select Window) and its
coordinates are dummy.

> **Open layout caveat.** Some records read as garbage floats at offset 0 while holding coherent
> CP949 text — suggesting a two-sub-type alternation (coord+name vs name-at-different-offset) sharing
> the 32-byte stride. The sub-type discriminator was not resolved; treat the coord fields as
> PLAUSIBLE pending a dedicated harness pass. (SAMPLE-VERIFIED stride and name field; coords
> PLAUSIBLE.)

### 6.5 Adjacent per-area files (partial)

| File | Size | Status |
|---|---|---|
| `data/mapNNN/regionNNN.bin` | varies (32 / 1680 / 1776 / 4096 across sampled areas) | structure not decoded; possibly a polygonal-boundary or entry-link list (distinct from the fixed-stride `regiontable*.bin`) |
| `data/mapNNN/mapNNN.bin` | 520 bytes (consistent) | near-all-zero header; purpose unknown (one non-zero byte = 16 at offset 4 in area 0) |

These are noted for completeness; neither is required to render either map surface. (PLAUSIBLE /
UNKNOWN.)

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
   pixel `+32`/`+36`), and a GPS `D°M′S″` readout from the packed field at `+72` split per §5.5.
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
7. **`regiontableNNN.bin` sub-type discriminator (§6.4)** and the relationship between `regionNNN.bin`
   and `regiontableNNN.bin`.
8. **In-memory area-info table vs on-disk `mapsetting.scr`.** The two zone tables (§3.7, §6.3) were
   not cross-linked; whether one is loaded from the other is unconfirmed.

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
