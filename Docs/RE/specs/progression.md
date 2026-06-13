# Character progression — experience, level-up, rank XP, stat allocation (clean-room spec)

Neutral, behaviour-only model of how the legacy *Martial Heroes* client drives **character
progression**: the experience bar (XP gain), the level-up event, the separate rank/honor XP channel,
the authoritative stat resync, the skill-point counter, and the player-driven **stat-point
allocation** window (the `+`/`-` editor and its commit/ack round-trip). Promoted from dirty-room
notes; rewritten in our own words — no decompiler identifiers, no binary addresses, no byte offsets
framed as binary locations.

This document is design input for the **domain engineer** (the deterministic XP/level/stat-points
state model in `Client.Domain`), the **application engineer** (the progression event handlers and the
stat-allocation use case in `Client.Application`), and the **protocol engineer** (the six progression
wire messages). It is the progression counterpart to the combat and vitals specs and shares their
actor/stat storage model.

---

## Status header (read first)

> **Every claim here is CODE-CONFIRMED but CAPTURE-UNVERIFIED.** The findings were read statically
> from the legacy client (handler flow, payload read sizes, stack-frame field placement, asset ids,
> and struct offsets are all recovered directly from the binary). **No live network capture was
> available**, so the exact on-wire byte placement of multi-field payloads — in particular the
> rank-XP tail of the level-up message and the dual use of the experience message's source-sort byte
> — must be reconciled against a capture before an engineer treats those offsets as settled. Wire
> structs are owned by the packet specs (below); this document describes the *client behaviour* those
> packets drive and the runtime state they mutate.

| Area | Confidence |
|---|---|
| Six progression channels (five S2C + one C2S) and their routing | HIGH (CODE-CONFIRMED) |
| Experience accumulation into the 64-bit current-XP and lifetime-XP accumulators | HIGH (CODE-CONFIRMED) |
| Server XP percentage-bonus split and the floating-text / exp-orb FX | HIGH (CODE-CONFIRMED for ids and flow) |
| Level-up vitals/level/points write, FX id, sound id, class-evolution gating | HIGH (CODE-CONFIRMED) |
| Stat-allocation editor: pending-delta model, `+`/`-` action ids, modifier-key step, auto-repeat | HIGH (CODE-CONFIRMED) |
| Stat-allocation commit body = five ABSOLUTE u32 stats, order STR, INT, AGI, DEX, CON | HIGH (CODE-CONFIRMED static; the absolute-vs-delta semantics and ordering are CAPTURE-UNVERIFIED) |
| Level-up payload rank-XP tail packing (the last 16 bytes) | LOW — PROVISIONAL, capture-gated (see §9 open questions) |
| Whether the experience message's source-sort field is one byte-field or two | LOW — PROVISIONAL, capture-gated (see §9) |

Korean strings referenced here (rank titles, karma labels) are **CP949 / EUC-KR** encoded, consistent
with the rest of the client.

**Cross-references**
- Stat storage, the five primary stats, and the max-HP / max-MP vitals formula: `structs/stats.md`.
- Actor vitals storage (`current_xp`, the HP/MP/stamina vitals, level): `structs/actor.md`.
- Combat-stat mirror refreshed by the same character window: `specs/combat.md`.
- Skill / buff subsystem that shares the character window's skill-point counter: `specs/skills.md`.
- Wire field specs:
  - Level-up: `packets/5-32_level_up.yaml` (`SmsgLevelUp`).
  - Stat-allocation ack: `packets/4-29_stat_update.yaml`.
  - The experience (`5/9`), rank-XP (`5/11`), authoritative stat resync (`5/67`),
    skill-point (`4/150`), and stat-allocation request (`2/29`) messages are documented by layout
    here and are candidates for promotion to new `packets/*.yaml` field specs.

Opcode notation throughout is `major/minor` (the catalogued message tuple), e.g. `5/9`.

---

## 1. Overview — six progression channels

Player progression converges on a single in-game **character / stat window**. One client routine
(the "character-window master rebuild") fully repaints that window's name, rank/title, level, class
name, karma label, the five current stats, the five editable stat rows, the available-points label,
the class-evolution art, and the skill-point counter; it is invoked by every server channel that
changes progression state. A second routine (the "window action dispatch") handles every button click
in the stat editor.

Five channels are server → client; one is client → server.

**Server → client** (all gated to the local player by matching actor sort/id against the local-player
record):

| Opcode | Name | Role |
|---|---|---|
| `5/9`  | `ExpGain`        | Adds 64-bit experience to the local player; refreshes the XP bar; floats `+xp` text and exp-orb FX. |
| `5/11` | `RankXpGain`     | Separate rank/honor XP channel (distinct accumulators; no HP/MP/level math). |
| `5/32` | `LevelUp`        | Writes new level + vitals + remaining stat points + rank-XP; fires level-up FX + sound; opens the class-evolution panel at levels 12 and 24. |
| `5/67` | `StatsUpdate`    | 36-byte authoritative resync of the five primary stats and current XP (the world-entry stat sync). |
| `4/29` | `StatUpdate` ack | Acknowledges a stat-allocation request; on success stores the echoed absolute stats + remaining points and plays a confirm sound. |
| `4/150`| `SkillPointUpdate` | Parallel skill-point total / skill-level-up notice (the window also shows a skill-point counter). |

**Client → server**:

| Opcode | Name | Role |
|---|---|---|
| `2/29` | `StatAllocate` | 20-byte body = five LE u32 **absolute** target stats. Built and sent by the editor's "Apply" button; gated on a points-available counter. |

Two adjacent acks were observed but not decoded this pass — a server-pushed stat-change notify
(`4/134`) and a rank-progress update (`4/133`). They are noted for completeness and are out of scope
here.

---

## 2. The character / stat window and its runtime state

The stat window is a single UI object. The progression-relevant fields it carries (offsets are into
the window object, recovered from the editor's read/write sites):

| Field offset (in the window object) | Field | Meaning |
|---|---|---|
| +180 | `action_id` | Action id of the most recently clicked widget (300–312). |
| +376 | `delta_STR` | Pending STR allocation (signed; built up by `+`/`-`). |
| +380 | `delta_INT` | Pending INT allocation. |
| +384 | `delta_AGI` | Pending AGI allocation. |
| +388 | `delta_DEX` | Pending DEX allocation. |
| +392 | `delta_CON` | Pending CON allocation. |
| +396 | `avail_points` | Remaining unspent points after subtracting the in-progress deltas. |
| +402 | `held_action` | Currently-held `+`/`-` action id (the auto-repeat driver; 0 = none). |
| +404 / +408 | hold timer / accumulator | Auto-repeat timing, compared against a 1.0-second threshold. |

The window also holds the row-label widgets for the five editable stats and for the read-only current
stat / HP / MP display labels; those are display-only and are repainted by the per-frame tick (§7).

The five **cached absolute** primary stats and the **remaining-points counter** are global
progression state (not stored on the window). The window reads them on rebuild and copies them when
the editor opens:

- Five cached absolute stats `STR, INT, AGI, DEX, CON` (these mirror `structs/stats.md`).
- `remaining_stat_points` — the authoritative count of unspent points.
- A second progression counter written by the level-up message ("secondary value"), whose readers
  were not located this pass (see §9, Q4).
- The rank-XP pair: a rank/honor accumulator and a within-rank value.
- The lifetime-XP accumulator.
- Level, packed HP:MP, and stamina caches for the local player.
- Rank/title byte, class index byte, sub-class/promotion byte, and the skill-point class/current
  values used by the rank-title, class-name, karma, and skill-point displays.

---

## 3. Experience gain — `5/9 ExpGain`

`5/9` carries a **32-byte** payload. The handler:

1. Adds the 64-bit experience amount to the local player's **current-XP accumulator**
   (`current_xp`, owned by `structs/actor.md`) using 64-bit add-with-carry.
2. Adds the same amount to a separate **lifetime-XP** accumulator (a 64-bit running total).
3. Invokes the character-window master rebuild to refresh the XP bar (the same rebuild used by `5/32`,
   `5/67`, and the `4/29` ack).
4. Floats `+xp` text and spawns exp-orb FX (below).

### 3.1 Server XP percentage-bonus split

A server-set **XP percentage-bonus rate** is held in a global. When the payload's source-mode field
(the low byte of the source-sort field, offset 8) equals `2`, the displayed gain is split into a base
part and a bonus part for the floating text:

```
shown_base = 100 * amount / (rate + 100)
bonus      = amount - shown_base
```

rendered as `"<base> + <bonus>"`. When the mode field is not `2`, the full amount is shown plain. The
bonus split is a **display** transformation only; the full `amount` is what is added to the
accumulator. (Whether the source-sort field is genuinely one byte-field reused as the mode, or two
adjacent fields, is CAPTURE-UNVERIFIED — see §9, Q2.)

### 3.2 Floating text and exp-orb FX

- Floating chat text uses message-string id **10085** = `"+%s xp"` for a gain and **10086** =
  `"-%s xp"` for a loss, routed through the floating system-text helper. The gain uses one text colour
  code and the loss another.
- When a second actor (the XP source — e.g. the killed mob, looked up by the payload's source
  sort/id) exists, the handler spawns paired **exp-orb** UserXEffects:
  - On the XP source: id **380001011** when a descriptor flag is set, else **380001012**.
  - On the local player: id **380002011** / **380002012** (same flag selection).

### 3.3 Proficiency / mastery slots

The payload's two trailing 32-bit fields (offsets 24 and 28) are proficiency/mastery target slots fed
to the local player's stat-channel writer at indices 3 and 4 (weapon / skill proficiency XP). A
sentinel value of `-1` means "no slot" and skips the write.

### 3.4 `5/9 ExpGain` payload (32 bytes) — CODE-CONFIRMED, CAPTURE-UNVERIFIED

| Off | Size | Type | Meaning |
|---|---|---|---|
| 0  | 4 | u32 | Actor sort (XP recipient). |
| 4  | 4 | u32 | Actor id (XP recipient; matches the local player for self). |
| 8  | 4 | u32 | Source-actor sort (XP source / killed mob). Low byte `== 2` enables the bonus split (§3.1). |
| 12 | 4 | u32 | Source-actor id. |
| 16 | 8 | i64 | Experience amount → current-XP accumulator (add-with-carry) + lifetime-XP accumulator. |
| 24 | 4 | i32 | Proficiency/mastery target slot A (`-1` = none) → stat-channel writer index 3. |
| 28 | 4 | i32 | Proficiency/mastery target slot B (`-1` = none) → stat-channel writer index 4. |

---

## 4. Rank / honor XP — `5/11 RankXpGain`

`5/11` is a **separate progression channel** distinct from character XP and performs no HP/MP/level
math. It carries a **20-byte** payload, applied to the local player only.

- A **rank accumulator** and a **within-rank** value are the two pieces of state.
- When the payload mode is `2`, the amount is added directly to the rank accumulator (no level math).
  Otherwise the amount is run through a **per-level rank-XP table** to increment the rank, carrying the
  remainder into the within-rank value. The rank is capped at **25**.
- A server-set **rank-XP percentage-bonus rate** (a sibling of the XP-bonus rate in §3.1) governs the
  displayed bonus.
- Floating chat text uses message-string id **10017** (rank-XP gain, with optional bonus) and **10015**
  (no gain).

### 4.1 `5/11 RankXpGain` payload (20 bytes) — CODE-CONFIRMED, CAPTURE-UNVERIFIED

| Off | Size | Type | Meaning |
|---|---|---|---|
| 0  | 4 | u32 | Actor id. |
| 4  | 4 | u32 | Actor sort. |
| 8  | 8 | u64 | Rank-XP amount → rank accumulator (mode 2) or fed through the per-level rank table (else). |
| 16 | 1 | u8  | Mode (`2` = direct add, no level math). |
| 17 | 3 | —   | Padding. |

---

## 5. Level-up — `5/32 LevelUp`

`5/32` carries a **48-byte** payload. The handler looks the actor up by (sort, id) and writes the
refreshed vitals into the actor record:

- New **level** (u16).
- Packed **HP:MP** (current HP and current MP as two 32-bit halves in one 64-bit value).
- Current **stamina** (i32).

When the actor is the **local player**, it additionally updates the level/HP:MP/stamina caches,
re-writes the local-player record, and updates these progression globals:

- `remaining_stat_points` — the count of unspent points.
- The "secondary value" counter (purpose unconfirmed; see §9, Q4).
- The rank-XP pair (rank accumulator and within-rank value).

### 5.1 Level-up presentation

- **FX:** a UserXEffect, id **310000002** (the level-up burst), is spawned on the leveled actor.
- **Sound:** a level-up jingle, sound id **800000002** (sound kind 5).
- A class-name string is resolved from the class index and shown.
- The character window is refreshed by the rebuild path, which **first resets any pending allocation
  deltas** and then fully rebuilds. A level-up therefore **cancels any in-progress stat allocation**.

### 5.2 Class-evolution panels

The local player's **class index byte** gates two class-evolution (promotion) panels. When that byte
holds one of the two evolution-eligible class values (`19` or `22`), a class-evolution flag is toggled,
and the panels open by level:

- **Level 12:** class-evolution panel **id 100** opens (and the class form is opened).
- **Level 24:** class-evolution panel **id 101** opens.

### 5.3 `5/32 LevelUp` payload (48 bytes) — core CODE-CONFIRMED; rank-XP tail PROVISIONAL

This matches `packets/5-32_level_up.yaml`. The HP/MP/stamina/level core is high confidence; the last
16 bytes (the rank-XP region) are modelled provisionally — the handler reads two 64-bit rank-XP values
there, but their exact packing against the 48-byte boundary is CAPTURE-UNVERIFIED (see §9, Q1).

| Off | Size | Type | Meaning |
|---|---|---|---|
| 0  | 1 | u8  | Actor sort. |
| 1  | 3 | —   | Padding. |
| 4  | 4 | u32 | Actor id. |
| 8  | 2 | u16 | New level → actor level + level cache. |
| 10 | 2 | —   | Padding. |
| 12 | 4 | i32 | Remaining stat points (local player). |
| 16 | 4 | i32 | Secondary value (local player). |
| 20 | 8 | i64 | HP:MP packed → actor vitals + HP:MP cache. |
| 28 | 4 | i32 | Current stamina → actor + stamina cache. |
| 32 | 16 | — | Rank-XP region: a within-rank value and a total. **PROVISIONAL packing** — verify against a level-up capture. |

---

## 6. Authoritative stat / XP resync — `5/67 StatsUpdate`

`5/67` carries a **36-byte** payload. It is the **world-entry stat sync**: it writes the five primary
stats into the actor descriptor and also writes the **current-XP** accumulator (priming the XP bar),
mirrors the stats for the local player through the stat-channel writers, then invokes the
character-window rebuild. It is authoritative — the re-implementation should treat `5/67` as the
source of truth for the five primary stats and reset any locally-cached values to match.

---

## 7. Stat-point allocation editor

The stat editor lives inside the character window. It models a **pending allocation** entirely on the
client; nothing is sent to the server until the player presses "Apply".

### 7.1 The pending-delta model

Each of the five stats has a **pending delta** field on the window object (§2). Pressing `+` adds the
current step to a stat's delta and subtracts it from the available-points field; pressing `-` subtracts
from the delta (clamped at 0) and returns the step to available points. The available-points field is
recomputed as `remaining_stat_points − Σ deltas`. After any change the editor repaints each stat row as
`"<base>+<pending>"` (or `"<base>"` when its delta is 0) plus the available-points label, and
invalidates the window.

### 7.2 Action-id map

The editor's action dispatch switches on the clicked widget's action id:

| Action id | Stat | Operation |
|---|---|---|
| 300 | STR | `+` increment |
| 301 | INT | `+` increment |
| 302 | AGI | `+` increment |
| 303 | CON | `+` increment |
| 304 | DEX | `+` increment |
| 305 | STR | `-` decrement |
| 306 | INT | `-` decrement |
| 307 | AGI | `-` decrement |
| 308 | CON | `-` decrement |
| 309 | DEX | `-` decrement |
| 310 | —   | Reset / cancel: clears all five deltas, restores available points, re-enables the `+`/`-` widgets. |
| 311 | —   | **Apply**: builds and sends `2/29` (§8), then disables the "Apply" widget. |
| 312 | —   | Opens the help/info message (message-string id 16006). |

> **Note the action-id stat ordering.** The `+`/`-` action ids are laid out
> `STR, INT, AGI, CON, DEX` (id 303 is **CON**, id 304 is **DEX**). This differs from the **wire
> order** of the commit body, which is `STR, INT, AGI, DEX, CON` (§8). Keep the two orderings distinct.

A click plays the increment sound (id **862020102**, sound kind 2). A hover draws a tooltip from
message-string id **2221**.

### 7.3 Modifier-key step

The increment/decrement **step** is sensitive to a held modifier key, probed against a key-state
object:

| Held key | Step |
|---|---|
| (key id 1012) | 1000 |
| (key id 1013) | 100 |
| (key id 1014) | 10 |
| none | 1 |

For `+` operations the step is clamped so it never exceeds the available points.

### 7.4 Held-button auto-repeat

The window's per-frame tick repaints the read-only current-value labels (current stats, max HP, max MP,
etc., via the formula getters) and implements **auto-repeat**: while a `+`/`-` button is held (the
`held_action` field is non-zero), once the accumulated hold time exceeds **1.0 second** the tick
re-fires the per-click increment continuously for as long as the button stays held.

---

## 8. Commit and acknowledgement — `2/29 StatAllocate` and `4/29` ack

### 8.1 The `2/29` request (client → server)

"Apply" (action id 311) builds a **20-byte** body of **five contiguous little-endian u32 ABSOLUTE
target stats** — each value is the **cached base stat plus its pending delta**, not the delta itself.
The body is gated on **two conditions**: the remaining-points counter must be greater than zero **and**
at least one of the five deltas must be non-zero. The wire order is **STR, INT, AGI, DEX, CON**.

#### `2/29 StatAllocate` body (20 bytes, absolute) — CODE-CONFIRMED, CAPTURE-UNVERIFIED

| Off | Size | Type | Meaning |
|---|---|---|---|
| 0  | 4 | u32 | Absolute STR (= cached STR + `delta_STR`). |
| 4  | 4 | u32 | Absolute INT (= cached INT + `delta_INT`). |
| 8  | 4 | u32 | Absolute AGI (= cached AGI + `delta_AGI`). |
| 12 | 4 | u32 | Absolute DEX (= cached DEX + `delta_DEX`). |
| 16 | 4 | u32 | Absolute CON (= cached CON + `delta_CON`). |

> **Re-implementation note.** Because the body is **absolute**, the server applies a snapshot of the
> player's intended stat totals rather than incrementally adding deltas. The client builds those
> totals from its cached base values; any drift between the cached base and the server's record will be
> reconciled by the `4/29` ack echo (and overridden by the authoritative `5/67` resync). Send the
> stats in **wire order STR, INT, AGI, DEX, CON** — not the editor's action-id order (§7.2).

### 8.2 The `4/29` ack (server → client)

`4/29` carries a **36-byte** payload (see `packets/4-29_stat_update.yaml`). It gates on a result flag
at offset 8: when that flag equals `1` the ack is applied. On success the handler:

1. Stores the five **absolute echoed** stats back into the cached stat globals.
2. Stores the new **remaining stat points**.
3. Plays the confirm sound (id **800000002**, the same id as the level-up jingle).
4. Refreshes and closes the editor (re-rebuilds the window and exits edit mode).

#### `4/29 StatUpdate` ack payload (36 bytes) — CODE-CONFIRMED, CAPTURE-UNVERIFIED

| Off | Size | Type | Meaning |
|---|---|---|---|
| 0  | 4 | u32 | Handle / request id. |
| 4  | 4 | u32 | Session token. |
| 8  | 1 | u8  | Result flag (`1` applies). |
| 9  | 3 | —   | Read, not decoded. |
| 12 | 4 | u32 | STR echo → cached STR. |
| 16 | 4 | u32 | INT echo → cached INT. |
| 20 | 4 | u32 | AGI echo → cached AGI. |
| 24 | 4 | u32 | DEX echo → cached DEX. |
| 28 | 4 | u32 | CON echo → cached CON. |
| 32 | 4 | u32 | Remaining stat points → remaining-points counter. |

---

## 9. Skill-point counter — `4/150 SkillPointUpdate`

The character window also shows a **skill-point counter** (`"<current> / <cap>"`, where the cap is 50
or 255 by class). `4/150` is the parallel **skill-point** channel — distinct from stat-point
allocation but sharing the window. The handler gates on a result flag and then branches on a mode:

- **Mode 1:** set the total skill-point value (stored for the skill subsystem; see `specs/skills.md`).
- **Mode 2:** a skill level-up notice, with floating chat text (message-string ids 74313 / 74314).

### `4/150 SkillPointUpdate` payload — CODE-CONFIRMED, CAPTURE-UNVERIFIED

| Off | Size | Type | Meaning |
|---|---|---|---|
| 0  | 1 | u8  | Result flag (`1` applies). |
| 4  | 4 | u32 | Local-player key (matches the local player's id key). |
| 8  | 4 | u32 | Mode (`1` = set total skill points; `2` = skill level-up notice). |
| 12 | 4 | u32 | Value (skill points, or the skill-up level). |

---

## 10. Asset ids (FX / sound / strings) — CODE-CONFIRMED

For the FX, sound, and message-string spec authors. These ids are recovered directly from the client
and resolve to entries in the effect / sound / message tables.

| Trigger | Kind | Id | Meaning |
|---|---|---|---|
| `5/32` level-up | UserXEffect | 310000002 | Level-up burst on the leveled actor. |
| `5/32` level-up | Sound (kind 5) | 800000002 | Level-up jingle. |
| `4/29` ack (success) | Sound (kind 5) | 800000002 | Stat-allocation confirm (same id as the level-up jingle). |
| `5/9` exp gain | UserXEffect | 380001011 / 380001012 | Exp-orb ring on the XP source (selected by a descriptor flag). |
| `5/9` exp gain | UserXEffect | 380002011 / 380002012 | Exp-orb ring on the local player. |
| `+`/`-` click | Sound (kind 2) | 862020102 | Stat-increment click. |
| `5/9` exp gain | Chat string | 10085 `"+%s xp"` / 10086 `"-%s xp"` | Floating XP text (gain / loss). |
| `5/11` rank XP | Chat string | 10017 / 10015 | Rank-XP gain / no-gain text. |
| `5/32` evolve | Panel id | 100 (level 12) / 101 (level 24) | Class-evolution panels. |
| Editor help | String id | 2221 (tooltip) / 16006 (info) | Stat-window help text. |
| Skill level-up | Chat string | 74313 / 74314 | Skill level-up notices. |
| Rank / title | String ids | 22007–22056 | Rank titles + descriptions. |
| Karma | String ids | 30001–30004 | Alignment / karma labels. |

---

## 11. Implementation guidance (downstream, via these clean specs only)

- **Domain** (`Client.Domain`): model `current_xp` and `lifetime_xp` as 64-bit accumulators;
  `5/9` adds the amount to both. Model `remaining_stat_points`, the five cached absolute stats, the
  rank-XP pair, and the skill-point counter as authoritative state. The level-up (`5/32`), stat resync
  (`5/67`), and ack (`4/29`) overwrite this state; treat `5/67` as the source of truth on world entry.
- **Application** (`Client.Application`): the six handlers route to domain mutations and then raise
  presentation events (XP-bar refresh, level-up FX/sound, class-evolution panel open, window rebuild).
  The stat-allocation use case owns the pending-delta editor state (the deltas, the available-points
  recomputation, the modifier-key step, and the 1.0-second auto-repeat). It commits by sending `2/29`
  with five **absolute** stats in wire order **STR, INT, AGI, DEX, CON**, gated on points-available and
  at-least-one-nonzero-delta, and applies the `4/29` echo on success.
- **Presentation** (`Client.Godot`): strictly passive — render the XP/rank bars, the editable stat
  rows, the available-points and skill-point labels, the level-up burst FX + jingle, and the
  class-evolution panels. No progression authority in layer 05.

---

## 12. Open questions (capture-gated)

- **Q1 — level-up rank-XP tail (PROVISIONAL).** The last 16 bytes of the 48-byte `5/32` payload hold
  two 64-bit rank-XP values (within-rank and total), but their exact byte placement against the
  48-byte boundary is unverified. Confirm with a capture of a level-up. (See `packets/5-32_level_up.yaml`.)
- **Q2 — experience source-sort field (PROVISIONAL).** In `5/9`, offset 8 is read both as the
  XP-source actor sort and as the bonus-split mode (low byte `== 2`). Confirm whether this is one
  byte-field or two adjacent fields with a capture.
- **Q3 — HUD exp-bar gage.** The character-window rebuild refreshes the XP bar on the character panel.
  The streaming on-HUD exp-bar gage widget (the filled bar separate from the character window) was not
  isolated this pass; it is likely driven by a separate gage-progress message. Hand to a HUD-widget
  pass.
- **Q4 — the "secondary value" counter.** The second points/counter value written by `5/32` is not
  read by the stat editor; its purpose (a level-up-pending marker? a skill-point grant?) is unconfirmed.
  Trace its readers.
- **Q5 — stat-channel writer indices.** The stat-channel writer is invoked with several indices
  (including 3 and 4 for proficiency in `5/9`); the full index → stat/proficiency map belongs to the
  struct cartographer (`structs/stats.md`).
