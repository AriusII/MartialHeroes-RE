# Skill / Effect / Buff Subsystem Specification — clean-room neutral spec

> Clean-room neutral spec. Promoted from dirty-room analyst notes by the spec-author —
> **rewritten, never copied**. No decompiler identifiers, no binary addresses, no pseudo-code.
> This document describes *behavior, ordering, formulas, and on-disk/record layouts* so an
> engineer who never saw the disassembler can implement the cast pipeline, effect dispatch,
> and buff model from scratch.
>
> **Scope.** Skill cast pipeline (target select, range / line-of-sight, cost, cooldown),
> skill→effect dispatch (damage display, motion, AoE / fan-out, revive), the buff/debuff
> status model (duration / stack / tick), and the meaning of the `skills.scr` skill-data
> fields that drive all of the above.
>
> **Wire layouts are NOT re-derived here.** The on-the-wire packet field specs already live in
> `packets/2-52_use_skill.yaml`, `packets/5-52_actor_skill_action.yaml`, and the related skill /
> status push specs; this document references them by canonical message name and only documents
> the *client behavior* those packets drive. Runtime struct offsets referenced here are owned by
> `structs/skill.md`, `structs/actor.md`, and `structs/stats.md`.

---

## Status and verification banner

Confidence tags used throughout:

- `CONFIRMED` — consistent across multiple call sites / corroborated by the game's own debug
  strings or a recovered name.
- `LIKELY` — single consistent site, plausible, not independently cross-checked.
- `UNVERIFIED` — inferred from static analysis only; boundary, units, or semantics not pinned.

**Global caveat: `capture_verified: false`.** No live network capture was available. Every claim
here is a static inference from the legacy client; numeric record offsets, field widths, and the
HP-vs-MP cost question in particular must be reconciled against a capture before an engineer treats
them as settled. Korean text in `skills.scr` and related data files is **CP949 (Korean ANSI code
page)**, not UTF-8 — decode accordingly.

### UNVERIFIED — open questions (do not treat as settled)

1. **Cast cost field — HP or MP?** The cast-confirm path subtracts the skill-data "consumed cost"
   field from the local player's **current-HP 64-bit vital**, yet the field is labelled a generic
   cost and the *cast-gate* affordability check uses a **different** skill-data field (the MP-cost
   factor). Two distinct cost fields exist. Whether the game truly has HP-cost skills, or whether
   the consumed-cost field is an MP/ki value stored into the HP vital, **needs a capture of a known
   MP-cost cast**. Do not collapse these into a single "cost" until confirmed.
2. **`skills.scr` per-rank sub-rows.** Each skill record ends with a count-prefixed table of small
   fixed-width sub-rows (per-rank / per-level effect rows). Their individual fields were not decoded;
   they are most likely per-rank coefficients / durations. Needs the data file or a dedicated parser
   pass.
3. **Status effect-code enumeration.** Only the effect codes that change gameplay or visual state
   were identified (see §6.2). Codes above that range fall through to icon-only display; the full
   code→meaning table (poison, slow, stun, heal-over-time magnitudes) lives in `skills.scr` data,
   not in the client logic.
4. **Buff stacking.** Only refresh-by-slot was observed (one fixed slot per effect code, re-apply
   overwrites). Whether the server ever multiplexes the same effect code into multiple slot indices
   to stack it is unconfirmed.
5. **"Boost doubling" trigger.** A world/event flag (an "event-outlet" boolean) doubles AoE hit
   count and quadruples AoE area when set. The *source* of that flag (a world buff? a GM/event
   toggle?) is unknown.
6. **Damage is server-authoritative.** The client never computes skill damage. It only renders the
   per-target damage / remaining-HP / max-HP values the server sends. Any damage-formula recovery
   must come from server-side data, not this client.

---

## 1. Skill catalog and the skill-data record (`skills.scr`)

### 1.1 Source file and load

- The skill catalog is loaded from `data/script/skills.scr` during the master content-load
  sequence, alongside the item, mob, and map-setting scripts. `CONFIRMED`.
- The file is a sequence of **variable-length** skill records that the loader iterates one at a
  time; records cannot be bulk-read as a fixed array, because each record carries a trailing
  per-rank sub-table.

### 1.2 Record format (on disk)

Each skill record is:

| Part | Size | Meaning |
|---|---|---|
| Fixed header block | **1504 bytes** | The skill-data fields (see §1.4). |
| Sub-row count | **1 byte (u8)**, near the end of the block | Number of trailing per-rank sub-rows. |
| Trailing sub-rows | **count × 8 bytes** | Per-rank / per-level effect rows (raw on-disk form). |

`CONFIRMED` for the 1504-byte fixed block and the count-prefixed 8-byte sub-rows; `UNVERIFIED` for
the field meaning of each sub-row.

### 1.3 Runtime expansion

At load, each 8-byte on-disk sub-row is expanded into a **12-byte runtime row** of shape roughly
`[u16][i32 (sign-extended from an i16)][u16][u8]` — interpreted as a per-rank effect row. The
runtime skill object is **1508 bytes** (the 1504-byte record plus a small runtime tail). The domain
model should treat the per-rank rows as opaque coefficient/duration data until they are decoded.
`LIKELY`.

### 1.4 Skill-data field table (offsets within the 1504-byte record)

These offsets are **record-relative** (within the skill-data record), i.e. a data-format layout, not
binary addresses. They are read directly by the cast / effect / cooldown logic.

| Offset | Type | Field (neutral name) | Conf | Meaning / evidence |
|---|---|---|---|---|
| +1306 | u16 | `category` (skill sort) | CONFIRMED | Skill category. Observed values: 1, 2, 3, 5, 6, 7, 11, 14, 17. **14 = REVIVE** (named by the game's own debug string). Category 1 = basic-attack class. Mirrors `structs/skill.md` `skill_category`. |
| +1308 | u8 | `target_mode` (shape) | CONFIRMED | Selects target-resolution / area shape. Enumerated in §3. |
| +1312 | f32 | `base_range` (cone length) | LIKELY | Base cast range; for cone/line shapes, the cone length. Combined with caster body radius for the range check. |
| +1316 | f32 | `aoe_radius` (cast distance) | LIKELY | AoE radius / cast distance. Squared for distance gating. Doubled in area when the boost flag is set. |
| +1330 | i16 | `max_targets` | LIKELY | Base maximum target hit count. Clamped to **40**; doubled when the boost flag is set. |
| +1332 | i16 | `mp_cost_factor` | CONFIRMED | Cast-gate affordability factor. Gate fails when available MP `< 100 × mp_cost_factor`. **This is the MP gate, separate from the consumed cost at +1368.** |
| +1334 | u16 | `cooldown_centiseconds` | CONFIRMED | Cooldown in 1/100 s. Multiplied by 100 → per-slot cooldown in ms in the recast table (§4). |
| +1344 | u16 | `weapon_req_a` | LIKELY | Weapon / stance requirement A (weapon-class id). Observed values 53, 55. `0` = no requirement. |
| +1348 | u32 | `weapon_req_b` | LIKELY | Secondary weapon / stance requirement id, matched against the worn-weapon field. |
| +1352 | u8 | `weapon_req_active` | LIKELY | Flag enabling the weapon-requirement check. |
| +1368 | i16 | `consumed_cost` | UNVERIFIED | Cost subtracted on cast-confirm (§5.2). **Subtracted from the current-HP 64-bit vital** in the observed path, but labelled a generic cost. See open question 1. Distinct from `mp_cost_factor`. |
| +1370 | u16 | `stamina_cost` | LIKELY | Stamina cost per target; total = `stamina_cost × target_count × buff_factor`, floored at the base value (§5.2). |

> Two cast-window timer values are also read from the +1368 / +1370 region and compared against
> warm-up / valid-window globals; an out-of-window cast fails with the cast result code for "timing"
> (see §2.3, code 11). Whether these reuse the cost fields or are separate adjacent values is
> `UNVERIFIED`.

---

## 2. Cast pipeline (client → server skill request)

The cast routine validates a pending skill against the local player's state and, on success, sends
the use-skill request (`CmsgUseSkill`, opcode **2/52**; see `packets/2-52_use_skill.yaml`). It
returns a **numeric result code 0..21**; `0` = success (packet was sent). A UI wrapper maps each
non-zero code to a localized error string (§2.3).

### 2.1 Ordered gate sequence

Gates run in this order; the first failing gate returns its code and the request is **not** sent.
Implement them as an ordered short-circuit chain.

1. **Party / relation gate** → code **17**. Walk the local player's relation/party slot table
   (20 entries); if any referenced actor's relation byte is not the "allied" value, block.
2. **Billing / rank gate** → code **1**. A billing-state and rank-experience comparison against the
   rank-cap globals; shows a rank-cap notice.
3. **Busy / already-casting gate** → code **13**.
4. **Mounted / vehicle gate** → code **4**. Blocked while a mount-state actor is present (cannot cast
   while mounted).
5. **Map / zone-mode gate** → code **16**. Certain scene modes / sub-states forbid casting.
6. **Stun / silence gate** → code **19**.
7. **Alive gate** → code **2**. The local player's alive/can-act word must be set.
8. **Action-lock gate** → code **20**. A generic action-lock flag must be clear.
9. **Current-target hostile-state gate** → code **3**. The selected target must not be in the
   blocking hostile state.
10. **Resolve the pending skill** → code **4** if unresolved. For a basic attack (no pending skill),
    a per-class basic-attack alias skill is loaded; its category must be **1**.
11. **Weapon / stance requirement** → code **18**. Reads `weapon_req_a/_b/_active`; the worn weapon
    must satisfy them (or be covered by a class-link table).
12. **Self-cast eligibility** → code **5**.
13. **Cooldown gate** (§4). All cooldowns are ticked, then the skill's recast state is checked. If
    still cooling **and** the skill is not in the exempt category (category 1), fall through to the
    MP check / block.
14. **MP affordability gate** → code **6**. Fails when available MP `< 100 × mp_cost_factor`
    (skill-data +1332).
15. **Resolve effective target** by `target_mode`. For self / ground modes the target becomes the
    local player.
16. **Range / line-of-sight** (§2.2).
17. **Cast-window timing gate** → code **11**. The cast-window timers must be within the warm-up /
    valid window.
18. **Build target arrays** (§3) — fills the ally/PC id array and the enemy id array. If **neither**
    is populated → code **12**.
19. **Success.** Face the player toward the aim point, record the cast time, send `CmsgUseSkill`
    (2/52), return code **0**.

### 2.2 Range and line-of-sight

- **Effective range** = `base_range` (skill-data +1312) + the caster's body radius + a per-buff
  range bonus (read from a buff slot). Clamp to a minimum of **1.0**.
- **Distance test.** Use the **squared planar (XZ) distance** from the player position to the aim
  point, compared against `effective_range²`. If beyond range:
  - This is **not** an error toast. Instead the client sets a "needs approach" state, issues a move
    toward the aim point, and returns code **8** (move-closer).
- **Terrain / LoS test** on the aim point → code **9** if blocked.
- **Target-state test** (§3.1) → code **10** if the target is not valid. Skipped for the
  ground/point target mode.

### 2.3 Result-code → localized-string-id map

From the UI wrapper. String ids are the legacy localization ids; an engineer porting the UI maps
them to the project's own string table. Code **8** intentionally has no toast (it triggers
approach/move-closer instead).

| Code | String id | Code | String id |
|---|---|---|---|
| 1 | 41001 | 9 | 44016 |
| 2 | 44009 | 10 | 44017 |
| 3 | 44010 | 11 | 44018 |
| 4 | 44011 | 12 | 44019 |
| 5 | 44012 | 14 | 44024 |
| 6 | 44013 | 15 | 44025 |
| 7 | 44014 | 17 | 59002 |
| 8 | *(no toast — triggers approach / move-closer)* | | |

> Recommended for the domain layer: lift these into a `SkillCastResult` enum in `Shared.Kernel`
> (0 = `Ok`, plus the named failure reasons), with the localization mapping held in the UI layer.

### 2.4 The request packet

On success the client sends `CmsgUseSkill` (opcode **2/52**). The send-site builds the fixed
header (skill slot, aim mode, aim scale, aim X/Z, and two target-array counts) followed by the two
count-prefixed target-id arrays. **The exact field layout, element widths, and the array-A-vs-array-B
meaning are owned by `packets/2-52_use_skill.yaml`** and are not re-derived here. One detail this
pass resolved for that spec: the two id arrays correspond to the cast pipeline's **ally/PC target
array** and **enemy target array** (see §3); the count-vs-element-width discrepancy noted in that
yaml remains `UNVERIFIED`.

---

## 3. Target resolution and area shapes

Target-array construction dispatches on `target_mode` (skill-data +1308). AoE shapes walk the world
actor list and select by faction (friend/foe test), target-state validity (§3.1), and squared
distance. Hits split into two arrays: **PC/ally** hits and **enemy/mob** hits. The combined hit count
is capped at **40**; the per-skill base cap comes from `max_targets` (+1330).

| `target_mode` | Shape | Behavior |
|---|---|---|
| 0 | single (self / primary) | Uses the primary target only. |
| 1 | single target | Faction + target-state check; team-id gate. Fail → string 3004. |
| 2 | single enemy / heal | Target-state check; if the target is friendly, treat as a heal target, else an enemy. Fail → string 3002 / 3030. |
| 3 | chain / nearby AoE | Radius from `aoe_radius` (+1316), squared; the enemy radius is **halved** when the weapon-requirement fields are set; iterate actors within radius². |
| 4 | cone / forward-line AoE | Length from `base_range` (+1312) + caster radius; forward vector from the caster's last-known position; per-actor cone test; effective radius² = `2 × radius²`. |
| 5 | ground / point | No actor targets are resolved (returns immediately). Used for ground-targeted / blink casts. |
| 6 | party AoE | Walks the party roster; radius from `aoe_radius` (+1316). |
| 7 | faction / group-gated single | Style / team match against the target. Fail → string 3003. |
| 9 | PK-gated single | Team-byte gate; self → string 3005; fail → string 3001. |
| 10 (0x0A) | radial AoE, both factions | Radius² from `aoe_radius` (+1316); selects both PC and mob targets in range. |
| 11 (0x0B) | self-only | Clears the arrays, sets PC-count = 1 with the caster as the single target. |

> Modes 8 and 12+ were not observed in this subsystem; treat any unseen mode value as `UNVERIFIED`.

**Boost-doubling rule** (applies in every AoE branch): when the world/event boost flag is set **and**
the base hit count ≥ 2, the **hit count doubles** and the **area radius² quadruples** (radius ×2).
The combined count is then clamped to 40. The source of the boost flag is `UNVERIFIED` (open
question 5).

Distance math throughout uses **planar (XZ) squared distance**; radius squaring uses
exponentiate-by-squaring helpers. There is no need to compute true Euclidean distance for the gating.

### 3.1 Target-state validation

Validates a candidate target before it is added to a target array. Returns valid / invalid.

- **Null target** → invalid.
- **Target is using a tool** (its tool-id field is non-zero, e.g. gathering) → invalid.
- **REVIVE skills** (`category` == 14): the target's alive word **must be 0 (dead)** — a revive on a
  living target is invalid.
- **All other skills:** the target's alive word must be **set (alive)**. Additionally, if the target
  is a **Mob** whose mob template carries the "untargetable monster style" marker, it is invalid.

---

## 4. Cooldown ("recast") subsystem — 240 parallel slots

Cooldown state is held in **parallel 240-entry arrays**, indexed identically to the **240-slot skill
hotbar** described in `structs/skill.md`. Per slot the system tracks:

| Per-slot value | Units | Meaning |
|---|---|---|
| skill-id mirror | int | The skill id occupying the slot (mirrors the hotbar). |
| set timestamp | ms | When the cooldown was last armed. |
| duration | ms | Full cooldown length = `cooldown_centiseconds` (+1334) × 100. |
| remaining | ms | Time left; 0 when ready. |
| active / armed flag | — | Non-zero while cooling. |

Operations (each was named by the client's own debug format strings):

- **Tick-all (per frame).** For each armed slot, `remaining = set_time + duration − now`; when this
  underflows (expired), clear `remaining` to 0. `CONFIRMED`.
- **Arm a cooldown.** Linear-search the 240 id-mirror for the skill id; write `now` into its set-time
  slot and copy `duration → remaining`. Invoked from the skill-action confirm path (5/52) on a local
  cast. `CONFIRMED`.
- **Check ready (cast gate).** Tick all, then for the given skill id return *blocked* if its
  remaining / armed flag is non-zero, else *ready*. Used by the cast pipeline (§2, gate 13) and by
  the hotbar/quick-use eligibility filter.
- **Rebuild the duration table.** Walk the 240 hotbar ids, look up each skill, read
  `cooldown_centiseconds` (+1334) × 100 → per-slot duration ms. Invoked on hotbar / skill changes and
  from the periodic game-state tick.

**Quick-use / hotbar eligibility filter.** A separate gate reads the quick-use slots and a category
discriminator (certain categories are blocked while a flag is set) and then calls the ready-check
above. This is the hotbar quick-slot filter feeding the cast pipeline.

> Implementation note: 240 parallel arrays is a flat structure-of-arrays. The .NET model can use a
> single `CooldownSlot[240]` (struct-of-arrays or array-of-structs) keyed by hotbar slot index;
> category 1 (basic attack) and category 5 are **exempt from arming a cooldown** (see §2 gate 13 and
> §5.2).

---

## 5. Skill → effect dispatch (server skill-action, opcode 5/52)

The server broadcasts a skill/combat action via `SmsgActorSkillAction` (opcode **5/52**; wire layout
in `packets/5-52_actor_skill_action.yaml`). This document covers the **client behavior** that packet
drives. The packet is a header plus a repeating list of per-target hit records.

### 5.1 Header action-code dispatch

A single **action-code** byte in the header selects the top-level behavior:

| Action code | Behavior |
|---|---|
| 0 | Single-target combat result. |
| 0xC8 / 0xC9 | Motion enable / disable sub-op on the caster (skill id passed through). |
| 0xCA / 0xCB | A second motion / state sub-op on the caster. |
| 0xCC | **AoE / summon fan-out** (§5.3). |

A leading "active" flag byte of `0` means cancel / idle — the caster's motion resets and the cast is
treated as a continuation/cancel rather than a real cast.

### 5.2 Local-player cast confirm

When the caster is the **local player** and the action is a *real* cast (not a continuation):

- Set a UI cast time-out (≈ now + 550 ms).
- **Resource consumption:**
  - Subtract `consumed_cost` (skill-data +1368, i16) from the local player's **current-HP 64-bit
    vital** (recorded as a 64-bit subtract). **See open question 1 — this may be an MP/ki cost; the
    cast-gate affordability check uses the separate +1332 MP factor.** `UNVERIFIED`.
  - Subtract a **stamina** cost = `stamina_cost` (+1370, u16) × `target_count` × a factor read from a
    buff slot, floored at the base value; write the result to the current-stamina field.
  - Mirror current HP / MP / stamina into the local-player spawn descriptor.
- **Arm the cooldown** (§4) — **unless** the skill's category is 5 (cooldown-exempt).

### 5.3 Per-target effect application

The client iterates the per-target hit records (≤ 40). Each record carries the **visible damage**,
**remaining HP after the hit**, and **max HP** for that target (field layout owned by
`packets/5-52_actor_skill_action.yaml`). The client **only displays** these values and updates the
target's HP bar:

- **Damage is server-authoritative.** The client does **not** compute damage. It renders the hit
  numbers and applies the server-sent remaining-HP value. (Open question 6.)
- **REVIVE** (category 14): for each record resolving to the local player, open the
  revive/resurrection UI panel.
- **Damage feedback:** when the combat-log toggle is on, the client sums the visible-damage values
  across records and formats a "total damage" chat line (string id 2212).

### 5.4 AoE / summon fan-out (action code 0xCC)

This is a **client-side visual multiplier**, not a discrete summoned entity from the wire — there are
no extra wire fields. The handler:

- Takes the first record's actor as the AoE origin.
- For each of N sub-actor slots (bounded by a caster count field): computes a **fan position** around
  the origin's last-known position at angle `2πi / 3`, with a radius derived from a `sin`/`log`
  function of the index, scaled by 3.0 — i.e. it procedurally places sub-actors in a **ring** (the
  visual multi-hit / clone / summon effect).
- Sets each sub-actor's transform, resets its motion, and re-emits the action into that sub-actor's
  animation pipeline.
- Marks the caster's "AoE-active" visual state.

> **Teleport / blink** has **no dedicated opcode.** It is the ground/point target mode (mode 5) plus
> the move applied directly to the caster's transform — the ground-point cast moves the caster.

---

## 6. Buff / debuff (status) model

Statuses are surfaced as **state icons** (the state-icon UI panel; icon atlas
`data/ui/skillicon/stateicon.dds`, layout `data/ui/buff_icon_position.xdb`). Status data arrives via
three pushes:

| Opcode | Message (canonical) | Role |
|---|---|---|
| 5/31 | `SmsgBuffSlotUpdate` | Writes / clears the 12-byte status slots (§6.1). |
| 5/136 | `SmsgActorTimedStateUpdate` | A timed transform/stance state, separate from the slot table (§6.3). |
| 4/109 | `SmsgLocalActorSkillStateFlag` | A single local-player skill-state flag (§6.4). |

(Exact wire layouts for these pushes are owned by their packet specs / `opcodes.md`. This document
covers the client-side status state they drive.)

### 6.1 Buff-slot tables (driven by 5/31)

The status push carries a sort, an actor id, a **slot index**, an **effect code**, a **value**, and a
**param**. The slot index selects which table and which slot is written; status entries are **12 bytes**
(`[u32 effect_code][u32 value/duration][u32 param]`):

- **Per-actor buff table — 31 slots (index 0..30), 12-byte stride.** The primary status table on each
  actor. A **parallel secondary table** (offset 44 slots further) holds a u16 magnitude/percent plus a
  byte — this is the buff's **strength** (used by %-gated effects). A *set* writes all three dwords; a
  *clear* (value == 0) zeros the effect-code dword and stashes the param tail.
- **Local-player spawn-descriptor mirror** (slot index < 1,000,000): the same 12-byte entry is mirrored
  into the local-player spawn descriptor, and two UI panels are refreshed.
- **Global high-index table** (slot index ≥ 1,000,000, offset by 1,000,000): a separate 12-byte-stride
  table for a different status category (environmental / global auras).
- **Special: slot index 44 with a set param triggers actor removal (despawn)** — a "vanish"/banish
  status. `LIKELY`.
- On *set*, the effect-code applicator runs (§6.2); on *clear*, the buff tick/refresh also runs (§6.3).

### 6.2 Effect-code applicator

A switch on the slot's **effect code** (the first field of the 12-byte entry). The slot is "active"
when its value/duration field is `> 0`. Recovered mappings:

| Effect code | Effect (neutral) | Conf |
|---|---|---|
| 43 (0x2B) | Enter stance / motion state (transform-in). | LIKELY |
| 44 (0x2C) | A motion state. | LIKELY |
| 45 (0x2D) | Toggle a local control flag. | LIKELY |
| 46 (0x2E) | Model / appearance swap (petrify / transform). Builds a numeric model token; mob-style → model mapping (style 1→11, 2→22, 3→13, 4→14). | LIKELY |
| 47 (0x2F) | Movement / control restriction (root / snare). Sets a per-actor restriction flag; emits a per-tick floor/area visual stamp while active. | LIKELY |
| 48 (0x30) | **Dispel / cleanse:** clears the effect-code-43, -46, and -47 slots across the 31-slot table; resets stance to the default. | LIKELY |
| 50 (0x32) | Appearance / poison transform (variants). | LIKELY |
| 57 (0x39) | Sets an AoE-active actor state; transform using the slot's secondary value. | LIKELY |
| 64 (0x40) | Sets a flag **only when active AND the secondary magnitude < 100** (a %-gated flag). | LIKELY |
| 131 (0x83) | A motion state. | LIKELY |
| > 64 (others) | **Icon-only display** — no gameplay state change beyond showing the state icon. | LIKELY |

The **secondary magnitude / percent** for each effect comes from the parallel secondary table
(§6.1) — this is the buff's strength (e.g. the `< 100` gate on effect code 64). The full code→meaning
table for the icon-only codes (poison, slow, stun, heal-over-time magnitudes) is **not** in the client
logic; it lives in `skills.scr` data (open question 3).

### 6.3 Duration, tick, and stacking

A periodic **buff tick** walks the 31-slot per-actor status table:

- The **second dword of each slot is the remaining-duration counter.** While it is `> 1` and a global
  pause gate is not active, it **decrements by 1 per tick**.
- When it reaches 0, the effect-code applicator (§6.2) is re-run with the active flag now 0 — i.e. the
  effect **expires** and any associated state is undone.
- Negative durations clamp to 0.
- Effect-code-47 (root/snare) slots emit their per-tick visual stamp while active.

**Stacking model:** each effect code occupies **one fixed slot index**. Re-applying the same effect
**overwrites that slot (refresh, not additive stacking)** — there is no per-buff stack counter;
duration is the only per-slot timer. `LIKELY`. Whether the server ever multiplexes the same effect
code into two slot indices to stack it is `UNVERIFIED` (open question 4).

### 6.4 Timed state and local skill-state flag

- **`SmsgActorTimedStateUpdate` (5/136):** carries a **timed value** and a **state byte**, written to a
  pair of actor fields that hold a transform/stance duration **distinct from the §6.1 buff slots**.
  Mirrored into a linked combat sub-object when the actor is in combat; refreshes the actor-state UI
  panel.
- **`SmsgLocalActorSkillStateFlag` (4/109):** gated on the local-player id; writes a single
  skill-state flag byte into the local-player spawn descriptor and refreshes a skill panel.

---

## 7. Related opcodes in this subsystem (cross-reference)

These are cited from `opcodes.md` (canonical names) — not re-derived. Major:minor pairs are the
client's transport opcodes (`(major << 16) | minor`).

| Opcode (major:minor) | Canonical name | Direction | Role |
|---|---|---|---|
| 2:52 | `CmsgUseSkill` | C2S | Skill-activate request; built at the end of the cast pipeline (§2). |
| 5:52 | `SmsgActorSkillAction` | S2C | Skill/effect broadcast; drives resource consumption, motion sub-ops, AoE fan-out, hit display (§5). |
| 5:31 | `SmsgBuffSlotUpdate` | S2C | Writes the 12-byte status slots (§6.1). |
| 5:136 | `SmsgActorTimedStateUpdate` | S2C | Timed transform/stance state (§6.4). |
| 4:109 | `SmsgLocalActorSkillStateFlag` | S2C | Local skill-state flag (§6.4). |
| 5:33 | `SmsgSkillHotbarSlotSet` | S2C | Hotbar slot assignment (see `structs/skill.md`). |
| 4:41 | `SmsgSkillHotbarAssignResult` | S2C | Hotbar assignment result. |
| 4:150 | `SmsgSkillPointUpdate` | S2C | Skill-point pool update. |
| 4:97 | `SmsgAreaSkillEffectPanel` | S2C | Area skill-effect result panel. |
| 4:102 | `SmsgSkillWindowStateUpdate` | S2C | Skill window state. |
| 5:51 | `SmsgSkillGuideState` | S2C | Skill guide state. |

**Related runtime structs:** `structs/skill.md` (hotbar arrays, `category` +1306, rank field),
`structs/actor.md` (last-known position, current-HP vital, lifecycle/alive word, the buff-table and
timed-state regions), `structs/stats.md` (HP/MP auras — a **different** mechanism from the §6 buff
slots: auras feed the max-HP/MP multiplier, buff slots drive visual / control state).

**Related data / asset files** (for the asset and data-parsing passes): `data/script/skills.scr`
(this spec, §1), `data/script/skillcategory.scr`, `data/script/skillneedset.scr`,
`data/ui/skillicon/skillicon.txt`, `data/ui/skillicon/skillicon.dds`,
`data/ui/skillicon/stateicon.dds`, `data/ui/buff_icon_position.xdb`. All Korean text in these files is
**CP949**.

---

## 8. Implementation guidance (clean-room engineers)

- **Cast pipeline** → `Client.Application` use-case + `Client.Domain` gates. Model the ordered gate
  chain (§2.1) and the `SkillCastResult` enum (§2.3) in the domain layer; keep the localized-string
  mapping in the UI layer.
- **Cooldown table** → `Client.Domain`: a flat 240-slot cooldown model keyed by hotbar slot, with the
  category-1 / category-5 exemptions.
- **Effect / buff model** → `Client.Domain`: a 31-slot per-actor status array with a parallel
  magnitude array, a single-decrement tick, and refresh-by-slot semantics (no stack counter). The
  effect-code applicator (§6.2) is a switch on the effect code.
- **Wire decoding** → `Network.Protocol` decodes `CmsgUseSkill` / `SmsgActorSkillAction` / the status
  pushes per their `packets/*.yaml`; cite `// spec: Docs/RE/packets/<name>.yaml` on each offset.
- **Do not** implement the HP-vs-MP cost (§5.2) as a single value, the `skills.scr` per-rank rows
  (§1.3), or any effect-code-> meaning beyond the table in §6.2 without first resolving the
  corresponding UNVERIFIED item. Mark them as TODO with a pointer back to this spec.
