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

> **Verification:** the client-side routing, payload read sizes, handler control-flow, stack-frame
> field placement, asset ids, and progression-window field offsets here are **[confirmed]** — read
> from the binary's control flow and operands. The **server-authored magnitudes** (XP rates, rank-XP
> divisor/cap table contents, the XP/rank percentage-bonus rate values, level boundaries) and the
> exact **on-wire byte packing** of two multi-field tails (the level-up rank-XP region and the dual
> use of the experience message's source-sort field) are **[capture/debugger-pending]** — the client
> only reads those values; their meaning on the wire and their runtime magnitudes need a live witness.
>
> - **ida_reverified:** 2026-06-16; re-verified against doida.exe IDB SHA 263bd994, CYCLE 7 (2026-06-20)
> - **ida_anchor:** 263bd994
> - **evidence:** [static-ida]
> - **conflicts:** none open. This pass re-pinned the stat-editor delta-offset labels and the
>   `+`/`-` action-id → stat ordering (both had drifted in the prior committed doc; the on-wire
>   `2/29` commit order STR,INT,AGI,DEX,CON was already correct and is unchanged). CYCLE 7 added the
>   boot-time stat-curve loader story (§13): base HP/MP are absent-by-design (server-supplied), the
>   level cap is data-driven, and the client owns no XP→level formula — all consistent with this
>   document's existing server-authoritative model.

Wire structs are owned by the packet specs (below); this document describes the *client behaviour*
those packets drive and the runtime state they mutate.

| Area | Confidence |
|---|---|
| Six progression channels (five S2C + one C2S) and their routing | [confirmed] |
| Experience accumulation into the 64-bit current-XP and lifetime-XP accumulators | [confirmed] |
| Server XP percentage-bonus split mechanism, and the floating-text / exp-orb FX ids + flow | [confirmed] |
| Level-up vitals/level/points write, FX id, sound id, chat id 10081, class-evolution gating | [confirmed] |
| Stat-allocation editor: pending-delta model, delta offsets, `+`/`-` action ids, modifier-key step | [confirmed] |
| Stat-allocation commit body = five ABSOLUTE u32 stats, build is self-consistent | [confirmed] (build); on-wire order / absolute-vs-delta semantics [capture/debugger-pending] |
| 1.0-second auto-repeat threshold (in the window per-frame tick) | [static-hypothesis] (tick not deep-walked) |
| Level-up payload rank-XP 16-byte tail packing | [capture/debugger-pending] (see §12, Q1) |
| Whether the experience message's source-sort field is one byte-field or two | [capture/debugger-pending] (see §12, Q2) |
| Server-authored values: XP/rank bonus rates, rank-XP divisor & cap tables, level boundaries | [capture/debugger-pending] (client reads only) |

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
| +380 | `delta_DEX` | Pending DEX allocation. |
| +384 | `delta_INT` | Pending INT allocation. |
| +388 | `delta_AGI` | Pending AGI allocation. |
| +392 | `delta_CON` | Pending CON allocation. |
| +396 | `avail_points` | Remaining unspent points after subtracting the in-progress deltas. |
| +402 | `held_action` | Currently-held `+`/`-` action id (the auto-repeat driver; 0 = none). |
| +404 / +408 | hold timer / accumulator | Auto-repeat timing, compared against a 1.0-second threshold. |

> **Delta-offset → stat mapping (re-pinned, anchor 263bd994).** The five pending-delta fields are
> **not** laid out in `STR, INT, AGI, DEX, CON` order. The order is **STR (+376), DEX (+380),
> INT (+384), AGI (+388), CON (+392)**. This was proven independently two ways: (1) each editable
> row's base-value getter reads exactly one stat-base cache (the five caches map cleanly to descriptor
> stat ids 70–74 = STR, INT, AGI, DEX, CON), and the `2/29` Apply build pairs each cache with a
> specific window delta offset so that every slot is a self-consistent `base + delta` of **one** stat
> (§8.1); (2) the per-row repaint in the `+`/`-` handler and in the reset handler pairs the same row
> base getter with the same delta offset, internally consistent only with this mapping. The on-wire
> `2/29` body remains `STR, INT, AGI, DEX, CON` (§8.1) — the editor delta layout and the wire layout
> are different orderings; keep them distinct.

The window also holds the row-label widgets for the five editable stats and for the read-only current
stat / HP / MP display labels; those are display-only and are repainted by the per-frame tick (§7).

The five **cached absolute** primary stats and the **remaining-points counter** are global
progression state (not stored on the window). The window reads them on rebuild and copies them when
the editor opens:

- Five cached absolute stats `STR, INT, AGI, DEX, CON` (these mirror `structs/stats.md`).
- `remaining_stat_points` — the authoritative count of unspent points.
- A second progression counter written by the level-up message ("secondary value"), whose readers
  were not located this pass (see §12, Q4).
- The rank-XP pair: a rank/honor accumulator and a within-rank remainder value.
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

composed as a `"<base> + <bonus>"` piece that is then fed into the 10085 floating-text message. When
the mode field is not `2`, the full amount is shown plain. The bonus split is a **display**
transformation only; the full `amount` is what is added to the accumulator. (Whether the source-sort
field is genuinely one byte-field reused as the mode, or two adjacent fields, is
**[capture/debugger-pending]** — see §12, Q2. The split mechanism itself is **[confirmed]**; the
server-set `rate` value is **[capture/debugger-pending]**.)

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

### 3.4 `5/9 ExpGain` payload (32 bytes) — layout [confirmed]; source-sort field semantics [capture/debugger-pending]

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

- A **rank accumulator** and a **within-rank remainder** value are the two pieces of state.
- When the payload mode is `2`, the amount is added directly to the rank accumulator (no level math).
  Otherwise the amount is run through a **per-level rank-XP table** to increment the rank, carrying the
  remainder into the within-rank value. The rank progression caps at **25**.
- A server-set **rank-XP percentage-bonus rate** (a sibling of the XP-bonus rate in §3.1) governs the
  displayed bonus.
- Floating chat text uses message-string id **10017** (rank-XP gain, with optional bonus) and **10015**
  (no gain).

> **The accumulation engine is shared, not in the `5/11` handler (re-pinned, anchor 263bd994).** The
> cap-25 logic and the per-level rank-XP table math do **not** live inside the `5/11` handler — they
> live in a **shared rank-XP accumulation routine** that `5/11` calls (and which has at least one
> other caller). That routine uses **two i64-stride tables** indexed by the local-player **level
> cache**: a *divisor* ("level") table that gives the XP-per-rank-step, and a *cap* table that bounds
> the within-rank value. The non-mode-2 path computes `rank_acc += (remainder + amount) / divisor[idx]`
> and `within = (remainder + amount) % divisor[idx]`. If the divisor for a level is `0`, a
> "leveltable error" diagnostic fires. The table contents and the divisor/cap values are
> **[capture/debugger-pending]** (server-config / data-driven; no client VFS table drives them).
>
> **The table index is the level cache, not an independent rank counter (clarification).** The index
> into both tables — and the value tested against `25` for the cap special-case — is the **local-player
> level cache** (the same `u16` the level-up message writes as the new level, and the same value the
> class-evolution gate tests against `12` / `24` in §5.2). So this is effectively a **per-level** table
> keyed by level; the "25" is a level-cache value, not a standalone rank value. The exact server-side
> rank-vs-level semantics are **[capture/debugger-pending]**.

### 4.1 `5/11 RankXpGain` payload (20 bytes) — layout [confirmed]; on-wire VALUE meanings [capture/debugger-pending]

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
- The "secondary value" counter (purpose unconfirmed; see §12, Q4).
- The rank-XP pair (rank accumulator and within-rank remainder value).

### 5.1 Level-up presentation

- **FX:** a UserXEffect, id **310000002** (the level-up burst), is spawned on the leveled actor.
- **Sound:** a level-up jingle, sound id **800000002** (sound kind 5).
- A class-name string is resolved from the class index and shown.
- **Chat broadcast:** a level-up notice using message-string id **10081** is broadcast to the chat log
  / notice channel, gated on a window condition flag. (This is in addition to the FX and sound.)
- The character window is refreshed by the rebuild path, which **first resets any pending allocation
  deltas** and then fully rebuilds. A level-up therefore **cancels any in-progress stat allocation**.
  (The rebuild path itself was not deep-walked this pass; the delta-reset behaviour matches the
  editor's reset handler — **[static-hypothesis]**.)

### 5.2 Class-evolution panels

The local player's **class index byte** gates two class-evolution (promotion) panels. When that byte
holds one of the two evolution-eligible class values (`19` or `22`), a class-evolution flag is toggled,
and the panels open by level:

- **Level 12:** class-evolution panel **id 100** opens (and the class form is opened).
- **Level 24:** class-evolution panel **id 101** opens.

### 5.3 `5/32 LevelUp` payload (48 bytes) — core [confirmed]; rank-XP tail [capture/debugger-pending]

This matches `packets/5-32_level_up.yaml`. The frame field layout (sort, id, level, points, secondary
value, packed HP:MP, stamina) is **[confirmed]** from the handler's stack-frame reads; the last 16
bytes (the rank-XP region) hold two 64-bit rank-XP values (a within-rank value and a total), but their
exact on-wire packing against the 48-byte boundary is **[capture/debugger-pending]** — verify against
a level-up capture (see §12, Q1).

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
| 32 | 16 | — | Rank-XP region: two 64-bit values (a within-rank value and a total). On-wire packing **[capture/debugger-pending]** — verify against a level-up capture. |

---

## 6. Authoritative stat / XP resync — `5/67 StatsUpdate`

`5/67` carries a **36-byte** payload. It is the **world-entry stat sync**: it writes the five primary
stats into the actor descriptor and also writes the **current-XP** accumulator (priming the XP bar),
mirrors the stats for the local player through the stat-channel writers, then invokes the
character-window rebuild. It is authoritative — the re-implementation should treat `5/67` as the
source of truth for the five primary stats and reset any locally-cached values to match.

> **Field map and writer indices (re-pinned, anchor 263bd994).** The five stats land in the actor
> descriptor at a mix of widths — two of them as **bytes**, three as **dwords** — and the current-XP
> 64-bit accumulator is written alongside. The local-player mirror does **not** use contiguous
> stat-channel writer indices `0..4`: it issues only **four** stat-channel writes, at non-contiguous
> indices **{0, 5, 2, 6}** (two through one writer entry point, two through another). Because five
> stat values are reconciled through four non-contiguous channel indices, the full **writer-index →
> stat / proficiency map is not yet resolved**; it belongs to the struct cartographer (`structs/stats.md`)
> and relates to the same writer used for the proficiency slots in `5/9` (§3.3, and §12 Q5). The exact
> per-field actor offsets are owned by `structs/actor.md`.

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

The editor's action dispatch switches on the clicked widget's action id. The action-id → stat
mapping is **re-pinned (anchor 263bd994)** from the `+`/`-` handler, using the corrected delta-offset
layout above (§2) — each action id pairs a specific window delta offset with its row's stat:

| Action id | Stat | Window delta offset | Operation |
|---|---|---|---|
| 300 | STR | +376 | `+` increment |
| 301 | DEX | +380 | `+` increment |
| 302 | INT | +384 | `+` increment |
| 303 | CON | +392 | `+` increment |
| 304 | AGI | +388 | `+` increment |
| 305 | STR | +376 | `-` decrement |
| 306 | DEX | +380 | `-` decrement |
| 307 | INT | +384 | `-` decrement |
| 308 | CON | +392 | `-` decrement |
| 309 | AGI | +388 | `-` decrement |
| 310 | —   | —   | Reset / cancel: clears all five deltas, restores available points, re-enables the `+`/`-` widgets. |
| 311 | —   | —   | **Apply**: builds and sends `2/29` (§8), then disables the "Apply" widget. |
| 312 | —   | —   | Opens the help/info message (message-string id 16006). |

> **Note the action-id stat ordering (corrected).** The editor's `+`/`-` action ids are laid out in
> stat order **STR, DEX, INT, CON, AGI** (id 301 is **DEX**, id 302 is **INT**, id 304 is **AGI**).
> This is yet a third ordering, distinct from both the delta-field layout (STR, DEX, INT, AGI, CON —
> §2) and the **wire order** of the commit body (STR, INT, AGI, DEX, CON — §8). A Godot stat-editor
> UI that mapped its `+`/`-` buttons by the *previous* (mislabeled) order would label three rows wrong;
> the downstream protocol / domain path (the `2/29` body) is unaffected because the wire order was
> always correct. Keep all three orderings distinct.

A click plays the increment sound (id **862020102**, sound kind 2). A hover draws a tooltip from
message-string id **2221** (the tooltip is **[confirmed]**; the help action 312 / info string 16006
pairing is **[static-hypothesis]** — not re-located this pass).

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
`held_action` field at window+402 is non-zero — set on mouse-down for action ids 300–309, cleared on
mouse-up, both **[confirmed]**), once the accumulated hold time exceeds the threshold the tick
re-fires the per-click increment continuously for as long as the button stays held. The threshold is
modelled as **1.0 second** — that value lives in the window per-frame tick routine, which was **not
deep-walked** this pass (the threshold is **[static-hypothesis]**; the held-action field and the
mouse-down/up arming are **[confirmed]**).

---

## 8. Commit and acknowledgement — `2/29 StatAllocate` and `4/29` ack

### 8.1 The `2/29` request (client → server)

"Apply" (action id 311) builds a **20-byte** body of **five contiguous little-endian u32 ABSOLUTE
target stats** — each value is the **cached base stat plus its pending delta**, not the delta itself.
The body is gated on **two conditions**: the remaining-points counter must be **non-zero** (the
instruction is a zero-test — for a non-negative unspent-points count "non-zero" and "> 0" coincide)
**and** at least one of the five deltas must be non-zero. The wire order is **STR, INT, AGI, DEX, CON**.

Each slot is built by pairing a window delta offset with its matching stat-base cache, so that every
slot is a self-consistent `base + delta` of one stat. With the re-pinned delta offsets (§2), the build
reads `STR(+376), INT(+384), AGI(+388), DEX(+380), CON(+392)` — i.e. the build deliberately reorders
the editor delta fields into the wire order STR, INT, AGI, DEX, CON. The build is **[confirmed]**; the
on-wire byte order and the absolute-vs-delta semantics remain **[capture/debugger-pending]** (no live
`2/29` capture this pass).

#### `2/29 StatAllocate` body (20 bytes, absolute) — build [confirmed]; on-wire order / absolute-vs-delta [capture/debugger-pending]

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

#### `4/29 StatUpdate` ack payload (36 bytes) — layout [confirmed]; on-wire VALUE meanings [capture/debugger-pending]

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

### `4/150 SkillPointUpdate` payload — [confirmed] layout; on-wire VALUE meanings [capture/debugger-pending]

| Off | Size | Type | Meaning |
|---|---|---|---|
| 0  | 1 | u8  | Result flag (`1` applies). |
| 4  | 4 | u32 | Local-player key (matches the local player's id key). |
| 8  | 4 | u32 | Mode (`1` = set total skill points; `2` = skill level-up notice). |
| 12 | 4 | u32 | Value (skill points, or the skill-up level). |

---

## 10. Asset ids (FX / sound / strings) — [confirmed]

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
| `5/9` exp gain | Chat string | 10085 `"+%s xp"` / 10086 `"-%s xp"` | Floating XP text (gain / loss). The base+bonus split (§3.1) composes a `"<base> + <bonus>"` piece into the 10085 message. |
| `5/11` rank XP | Chat string | 10017 / 10015 | Rank-XP gain / no-gain text. |
| `5/32` level-up | Chat string | 10081 | Level-up notice broadcast to the chat log / notice channel (gated on a window condition flag). |
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
  class-evolution panels. No progression authority in layer 05. **Bind the `+`/`-` editor buttons by
  the corrected action-id → stat map (§7.2): 300/305 = STR, 301/306 = DEX, 302/307 = INT, 303/308 =
  CON, 304/309 = AGI.** Mapping the buttons by the *previous* ordering would mislabel three rows.
  Keep the three orderings distinct: the editor delta-field layout (STR, DEX, INT, AGI, CON — §2), the
  editor action-id order (STR, DEX, INT, CON, AGI — §7.2), and the `2/29` wire order (STR, INT, AGI,
  DEX, CON — §8.1).

---

## 12. Open questions ([capture/debugger-pending])

- **Q1 — level-up rank-XP tail.** The last 16 bytes of the 48-byte `5/32` payload hold two 64-bit
  rank-XP values (within-rank and total), but their exact on-wire byte placement against the 48-byte
  boundary is **[capture/debugger-pending]**. Confirm with a capture of a level-up.
  (See `packets/5-32_level_up.yaml`.)
- **Q2 — experience source-sort field.** In `5/9`, offset 8 is read both as the XP-source actor sort
  and as the bonus-split mode (low byte `== 2`). Whether this is one byte-field reused as the mode or
  two adjacent fields is **[capture/debugger-pending]** — confirm with a `5/9` capture.
- **Q3 — HUD exp-bar gage.** The character-window rebuild refreshes the XP bar on the character panel.
  The streaming on-HUD exp-bar gage widget (the filled bar separate from the character window) was not
  isolated this pass; it is likely driven by a separate gage-progress message. Two situational `5/9`
  side-writes (a UI-state branch that writes the XP amount into a main-window field, and another that
  writes it into a UI object) feed HUD / quest-progress side-channels — out of progression scope but
  part of this same HUD-widget pass. Hand to a HUD-widget pass.
- **Q4 — the "secondary value" counter.** The second points/counter value written by `5/32` is not
  read by the stat editor and no readers were located this pass; its purpose (a level-up-pending
  marker? a skill-point grant?) is unconfirmed — **[static-hypothesis: written-but-unread]**. Trace
  its readers.
- **Q5 — stat-channel writer indices.** The stat-channel writer is invoked with non-contiguous indices
  — `{0, 5, 2, 6}` from the `5/67` resync (four writes for five stat values, §6) and `{3, 4}` for the
  proficiency slots in `5/9` (§3.3). The full index → stat / proficiency map is unresolved and belongs
  to the struct cartographer (`structs/stats.md`).
- **Q6 — server-authored magnitudes.** The XP and rank-XP percentage-bonus rate values, the rank-XP
  per-level divisor and cap table contents, and the level boundaries are **server-authored / config**
  — the client only reads them, and no client VFS table drives them. Level-boundary numbers are not
  present in the client binary as constants (they arrive via the `5/32` level/points fields and the
  server's XP tables). All **[capture/debugger-pending]**.

---

## 13. Boot-time stat-curve loader — what the client tables actually hold (CYCLE 7)

A single boot step loads the four-file **stat-curve family** in one pass and builds an in-memory
scaling-coefficient grid (the layout facts and on-disk record strides live in `structs/stats.md` §
"Stat-curve table family"). Three behavioural conclusions matter here, and all three **confirm and
reinforce the server-authoritative model** the rest of this document describes:

### 13.1 Base HP/MP are ABSENT-BY-DESIGN — server-supplied, not in any client table [confirmed]

The stat-curve loader reads `users.scr`, `userlevel.scr`, `userpoint.scr`, and `exp.scr` and builds a
per-class scaling grid. **There is no HP/MP magnitude column anywhere in this file family** — every
scalar position is an unnamed scaling coefficient or a per-level stat-point/allocation value. The base
HP/MP magnitudes a character actually has are **server-supplied at runtime**: they arrive in the
actor/character snapshot (the major-`4` packets) and, for the local player, in the `5/67` resync and the
`5/32` level-up vitals (§5–§6). This is consistent with `structs/stats.md`, where `max_hp`/`max_mp` are
computed-on-demand and the two external `level_base`/`server_base` terms are flagged as server inputs.

**Faithful-port consequence:** a C# stat catalogue that returns **0 base HP/MP** from the parsed `.scr`
tables is **correct, not a bug** — the magnitudes are meant to come from the wire, never from the client
data files. Re-implementations should not synthesise base HP/MP from these tables.

### 13.2 No client-side XP→level formula — only the XP-bar display window [confirmed]

There is **no client routine that increments the player level by comparing current XP to a threshold**.
Level-up is **server-authoritative** (the new level arrives in the `5/32` level-up message; the client
recomputes derived stats on receipt — see §5). The client owns **only the XP-bar display**, not the
progression decision.

The XP-bar fill is computed in the HUD vitals/gauge routine as:

```
bar_pixels = 44 * current / (rangeHi − base)
```

where `rangeHi` and the range component come from the **`userpoint.scr` record at +20 and +22 (both u16)**
(see `structs/stats.md`), and the live `current` / total-XP values are **RUNTIME-ONLY** (server-supplied,
the same accumulators `5/9 ExpGain` feeds in §3). The "curve" the client stores is therefore just the
per-level XP-bar denominator window (`userpoint` +20/+22) plus the two level-keyed value streams parsed
out of `exp.scr` (XP threshold / range) — it is **not** an XP→level function. Cross-ref §3 (`ExpGain`),
§12 Q6 (server-authored magnitudes), and `structs/stats.md`.

### 13.3 Level cap is DATA-DRIVEN — `.scr` row count, not a binary constant [confirmed]

The level cap is **the largest level key present in the four stat-curve tables**, enforced by the
loader's count-assert: the last level key of `userlevel.scr`, `userpoint.scr`, and `exp.scr` must all
agree, or the load aborts. **No hardcoded "max level" comparison exists** anywhere in the
level-consuming code — every level use is a per-item / per-quest requirement test (`level >= min`,
`level <= max`) against a data field, never a clamp against a global cap. The shipped data caps at
**level 300** (the exact `300` is **UNVERIFIED** at the byte level — it is the on-disk `.scr` row count,
readable only from the VFS files, not from a code immediate). The current level itself is
**RUNTIME-ONLY** (set from the server character snapshot, major-`4`, and the `5/32` level field), held in
two client mirrors: a HUD/eligibility level cache and the local-player struct's level field. So the
operative ceiling is whatever the server plus the shipped `.scr` row count allow — consistent with the
fully server-authoritative progression model above.
