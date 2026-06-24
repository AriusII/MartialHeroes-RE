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

> **verification:** `confirmed` for client-side routing / record framing / runtime offsets / cast-gate
> ordering / cooldown & buff logic (control-flow-confirmed); `static-hypothesis` for single-inference
> field meanings; `capture/debugger-pending` for server-authored magnitudes (skill damage, cooldown
> wall-clock, skill-point/XP rates, HP/stamina scale) and the actual on-wire VALUE bytes of 5/33, 4/41,
> 4/150, 5/52, 5/31.
> **ida_reverified:** 2026-06-16; re-verified against doida.exe IDB SHA 263bd994, CYCLE 7 (2026-06-20);
> spec-audit corrections applied 2026-06-24 (reversed +1368/+1370 timed-charge framing; pinned boost
> flag offset +1516; added AoE actor offsets +1828/+1832; noted executor-direct vs UI-wrapper string sets).
> **ida_anchor:** 263bd994
> **evidence:** [static-ida]
> **conflicts:** none — Campaign-10 Lane-F2 re-verification reproduced every framing constant
> (1504 / +1500 / N×8 / 1508 / +0 SkillId / +4 GlobalCategory / 240 / 8-byte stride / i16 points /
> slot<240) from the loader and handler code. Refinements only (the hotbar "two parallel arrays" are
> physically ONE 240×8-byte record array; new concrete string-id / message-id tables; 4/102 is a
> 476-byte snapshot rebuild). All applied below.
>
> **CYCLE 7 (2026-06-20):** the entire cast pipeline was re-recovered from the *consumer* side (the
> battle controller's cast gate, the AoE target resolver, the 2/52 builder), which consumer-confirms
> the `skills.scr` combat-stat columns (see `structs/skill.md §A.2.5`). Corrections applied below:
> the **+1332 field is the cast-CADENCE / cooldown gate** (blocked while elapsed `< 100 ms × (+1332)`),
> NOT an MP-pool subtract; **+1368 is an HP cost (i16) and +1370 is a stamina cost (u16)** — both
> affordability-gated and subtracted on the 5/52 cast-confirm path (the CYCLE 7 "timed-charge gate"
> framing was wrong and is reversed here); the **event-boost flag is at local-player record +1516
> (u8 == 1)**; the **default basic-attack skill id is 121100050**; the AoE cone (mode 4) is a circular
> SECTOR whose half-angle is `+1316` in DEGREES (× π/180 = 0.0174533). The earlier "4/102 is
> the buff opcode" framing is superseded: the canonical live buff-slot stream is **5/31
> `SmsgBuffSlotUpdate`** (4/102 is a 476-byte skill-window snapshot rebuild — a different thing). The
> 30-slot per-actor buff table is now owned by the dedicated `specs/buffs.md`; §6 here cross-references
> it and is not the buff-model authority.

Confidence tags used throughout:

- `CONFIRMED` — consistent across multiple call sites / corroborated by the game's own debug
  strings or a recovered name (control-flow-confirmed on build 263bd994).
- `LIKELY` — single consistent site, plausible, not independently cross-checked (static-hypothesis).
- `UNVERIFIED` — inferred from static analysis only; boundary, units, or semantics not pinned.
- `CAPTURE-PENDING` — server-authored magnitude or on-wire VALUE byte; the client-side routing/size
  is confirmed but the live value needs a capture / debugger run.

**Global caveat: `capture_verified: false`.** No live network capture was available. Client-side
routing, record offsets, field widths, and gate ordering are control-flow-confirmed on build
263bd994; but server-authored magnitudes (skill damage, real cooldown wall-clock, skill-point / XP
rates, HP/stamina scale) and the actual on-wire VALUE bytes of the pushes are **capture/debugger-pending**.
The HP-vs-MP cost question in particular must be reconciled against a capture of a known MP-cost cast.
Korean text in `skills.scr` and related data files is **CP949 (Korean ANSI code page)**, not UTF-8 —
decode accordingly.

### UNVERIFIED — open questions (do not treat as settled)

> **RESOLVED (Campaign 10, build 263bd994):** the prior open question "the unused second int of each
> 8-byte hotbar slot pair" is **closed** — there is only ONE 240×8-byte hotbar record array; the
> "second int" is **not unused**: its low word (record +4) is the skill-rank / points value and only
> its high word (record +6) is pad. See §1.5. The per-rank sub-row count byte was also pinned to
> fixed-block **+1500** (see §1.2).

1. **Cast cost semantics — HP and stamina costs (corrected).** Skill-data field **+1368 is an HP
   cost (i16)** and **+1370 is a stamina cost (u16)**. The cast gate blocks the cast if the local
   player's current HP is less than the +1368 value (with a `< 30000` client guard, to skip
   abnormally large values) or current stamina is less than the +1370 value; string ids 3011 / 3012
   are shown for the respective block. On 5/52 cast-confirm the handler subtracts +1368 from the
   player's current-HP field (+176, 64-bit) and subtracts `+1370 × target_count × buff_factor`
   (floored at the base +1370 value) from the player's current-stamina field (+184). The "running
   global clocks" referenced in the CYCLE 7 reframe (the current-HP global, 64-bit, and the
   current-stamina global, 32-bit) are **the
   current HP and current stamina globals** — written by 5/52, 5/32, 5/53, and the death handler —
   not timers. The CYCLE 7 "timed-charge gate" framing is **reversed**. The +1332 cadence gate
   framing is correct and unchanged. Server-authored magnitude scaling remains `CAPTURE-PENDING`.
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
5. **"Boost doubling" trigger. (LOCATION RESOLVED, CYCLE 7.)** The client flag that doubles AoE hit
   count (×2) and quadruples AoE area (radius ×2) lives in the **local-player record (an event-boost
   boolean)**: when it is set AND the base hit count is ≥ 2, the count doubles and the area
   quadruples; the combined count is then clamped to **40**. The *origin* of that flag (a world buff?
   a GM / event toggle that sets it?) is still unknown — only the consuming flag location is pinned.
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
  per-rank sub-table. The loader reads exactly **1504 bytes** for the fixed block, then (gated on
  `count != 0`) reads `8 × count` trailing bytes. `CONFIRMED`.
- **Two catalog containers** are built from the same records (both balanced-tree maps, not the
  world-actor registry): a **primary id-keyed catalog** keyed on **SkillId (+0)**, and a **secondary
  category-keyed index** keyed on **GlobalCategory (+4)** that is populated **only for records whose
  `+4 != 0`** (mob / universal skills are skipped from the secondary index). The secondary index lets
  the client enumerate skills by family / tree without scanning by id. `CONFIRMED`.
- **Catalog id-validity is consumer-side, not loader-side.** The loader inserts every record it
  parses; the "skip id 0 / id ≥ 10,000,000 (padding / obsolete)" rule cited elsewhere is a black-box
  file-walk heuristic, **not** enforced by the loader code. `static-hypothesis`.

### 1.2 Record format (on disk)

Each skill record is:

| Part | Size | Meaning |
|---|---|---|
| Fixed header block | **1504 bytes** | The skill-data fields (see §1.4). |
| Sub-row count | **1 byte (u8) at fixed-block +1500** | Number of trailing per-rank sub-rows. When `0`, the record is exactly 1504 bytes and nothing trails. |
| Trailing sub-rows | **count × 8 bytes** | Per-rank / per-level effect rows (raw on-disk form). |

`CONFIRMED` for the 1504-byte fixed block, the **count byte at +1500**, and the count-prefixed
8-byte sub-rows; `UNVERIFIED` for the field meaning of each sub-row.

### 1.3 Runtime expansion

At load, each 8-byte on-disk sub-row is expanded into a **12-byte runtime row**. The expansion is
exact (confirmed in the loader):

| Disk field | Disk size | → Runtime field | Runtime size |
|---|---|---|---|
| disk +0 | u16 | runtime +0 | u16 |
| (pad) | — | runtime +2 | u16 = 0 |
| disk +2 | i16 | runtime +4 | i32 (**sign-extended** from the i16) |
| disk +4 | u16 | runtime +8 | u16 |
| disk +6 | u8 | runtime +10 | u8 |
| (pad) | — | runtime +11 | u8 = 0 |

Disk stride 8, runtime stride 12. The runtime skill object is **1508 bytes** = the 1504-byte record
plus a **4-byte runtime tail at +1504** holding a pointer to the expanded sub-entry array. The object
is allocated at 1508 bytes; its constructor copies the 1504-byte record then stores the sub-entry
array pointer at +1504. `CONFIRMED` for the layout / sizes; the domain model should treat the per-rank
rows as opaque coefficient / duration data until they are decoded (`UNVERIFIED` field meaning).

### 1.4 Skill-data field table (offsets within the 1504-byte record)

These offsets are **record-relative** (within the skill-data record), i.e. a data-format layout, not
binary addresses. They are read directly by the cast / effect / cooldown logic.

| Offset | Type | Field (neutral name) | Conf | Meaning / evidence |
|---|---|---|---|---|
| +0 | i32 | `skill_id` (catalog primary key) | CONFIRMED | The skill id; primary key of the id-keyed catalog (§1.1). |
| +4 | i32 | `global_category` (family / tree index) | CONFIRMED | Secondary catalog key (§1.1) — the skill *family / tree* index, distinct from the per-cast `category` at +1306. Indexed only when non-zero. Drives the hotbar de-dup rule (§4.1). Mirrors `structs/skill.md`. |
| +520 | u8 | `tier_byte` (tier / chain-form) | CONFIRMED | Per-form tier discriminator within a family; used as the `< form` comparison key in the hotbar de-dup path (§4.1). |
| +1292 | u16 | `skill_rank` | CONFIRMED | The skill rank; copied into the hotbar points array when a slot is applied (§1.5). |
| +1306 | u16 | `category` (per-cast sort) | CONFIRMED | Per-cast skill category (the cast/effect discriminator, distinct from `global_category` at +4). Observed values: 1, 2, 3, 5, 6, 7, 11, 14, 17. **14 = REVIVE** (named by the game's own debug string). Category 1 = basic-attack class. Mirrors `structs/skill.md` `skill_category`. |
| +1308 | u8 | `target_mode` (shape) | CONFIRMED | Selects target-resolution / area shape. Enumerated in §3. |
| +1312 | f32 | `base_range` (cone length) | CONFIRMED | Base cast range; effective range = `+1312 + caster body radius`. For cone/line shapes (mode 4) it is the cone length `L`. |
| +1316 | f32 | `aoe_radius` **OR** `cone_half_angle_deg` | CONFIRMED | **Dual-purpose.** For shape modes 3/6/0xA it is an AoE **radius** in game units (squared for distance gating). For shape mode 4 (cone) it is a **cone half-angle in DEGREES**, × π/180 → radians. See §3. Radius doubles in extent (area ×4) when the boost flag is set. |
| +1330 | i16 | `max_targets` | CONFIRMED | Base maximum target hit count. Clamped to **40**; doubled when the boost flag is set. |
| +1332 | i16 | `cast_cadence_factor` | CONFIRMED | **Cast-cadence / cooldown gate.** The gate blocks while elapsed time since the last action is `< 100 ms × cast_cadence_factor`. **This is NOT an MP-pool subtract** (corrected CYCLE 7 — the earlier "MP gate" framing of this field is withdrawn). |
| +1334 | u16 | `cooldown_centiseconds` | CONFIRMED | Recast-table duration in 1/100 s. Multiplied by 100 → per-slot cooldown in ms in the recast table (§4). |
| +1344 | u16 | `weapon_req_a` | CONFIRMED | Weapon / stance requirement A (weapon-class id). Observed values 53, 55. `0` = no requirement. Also (with +1348) the shape-3 enemy-radius-halving trigger. |
| +1348 | u32 | `weapon_req_b` | CONFIRMED | Secondary weapon / stance requirement id, matched against the worn-weapon field. |
| +1352 | u8 | `weapon_req_active` | CONFIRMED | Flag enabling the +1344 special-weapon requirement check. |
| +1368 | i16 | `hp_cost` | CONFIRMED | Flat HP cost. Cast gate blocks if current HP `< hp_cost` (with a `< 30000` client guard). On 5/52 cast-confirm the value is subtracted from the player current-HP field (+176, 64-bit). Magnitude is `CAPTURE-PENDING`. |
| +1370 | u16 | `stamina_cost` | CONFIRMED | Flat stamina cost (base). Cast gate blocks if current stamina `< stamina_cost`. On 5/52 cast-confirm the effective cost is `stamina_cost × target_count × buff_factor`, floored at the base value, subtracted from the player current-stamina field (+184). Magnitude is `CAPTURE-PENDING`. |

> The +1368 and +1370 fields are flat resource costs checked at cast time and consumed on 5/52
> cast-confirm. The CYCLE 7 "timed-charge gate against running global clocks" framing was wrong
> (those "clocks" are the current HP and current stamina globals). See open question 1.

### 1.5 Skill hotbar — ONE 240×8-byte record array (was "two parallel arrays")

The skill hotbar is a single contiguous array of **240 eight-byte records** (total 1920 bytes). It is
**not** two independent parallel arrays — the two "arrays" the code touches are simply two *views* into
the **same** backing storage:

| Record offset | Type | Field | Written by |
|---|---|---|---|
| +0 | i32 | `skill_id` (the skill occupying the slot; `0` = empty) | hotbar-set / quick-slot apply |
| +4 | i16 | `points` (the skill's rank — `skill_rank` from skill-data +1292) | hotbar-set / quick-slot apply |
| +6 | i16 | pad (the only unwritten part of the record) | — |

`CONFIRMED`. This **resolves the prior open question** ("the unused second int of each 8-byte slot
pair"): the second int is not unused — its **low word is the points value** (record +4); only its
**high word (record +6) is pad**. Slot index is a `u8` in `0..239`; every reader/writer gates
`slot < 240 (0xF0)` and skips/returns otherwise. The cooldown ("recast") arrays in §4 are indexed by
the same 240-slot scheme. Mirrors `structs/skill.md` Part D (dual-view refinement).

---

## 2. Cast pipeline (client → server skill request)

The cast routine validates a pending skill against the local player's state and, on success, sends
the use-skill request (`CmsgUseSkill`, opcode **2/52**; see `packets/2-52_use_skill.yaml`). It
returns a **numeric result code 0..21**; `0` = success (packet was sent). A UI wrapper maps each
non-zero code to a localized error string (§2.3).

### 2.1 Ordered gate sequence

Gates run in this order; the first failing gate returns its code and the request is **not** sent
(unless noted as a *soft-fail* that falls back to the basic attack). Implement them as an ordered
short-circuit chain. The order and codes below are consumer-confirmed (CYCLE 7).

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
   blocking hostile (dead-body) state.
10. **Resolve the pending skill** → code **4** if unresolved. For a basic attack (no pending skill),
    a per-class basic-attack alias skill is loaded (default skill id **121100050**); its category
    must be **1**.
11. **Special-weapon vs target** → code **18**. When `weapon_req_a` (+1344) and `weapon_req_active`
    (+1352) gate a special-weapon branch (e.g. weapon-class 53 / 55), the target must wear that
    weapon class or share the weapon group.
12. **Weapon / stance requirement** → code **5**. Reads the weapon-req block (`weapon_req_a/_b/_active`
    at +1344 / +1348 / +1352, walked in 12-byte steps); the worn weapon must satisfy it (or be covered
    by a class-link table).
13. **Cooldown / cast-cadence gate** → code **6**. Blocked while elapsed time since the last action is
    `< 100 ms × cast_cadence_factor` (skill-data **+1332**). The per-slot recast table (§4) is also
    ticked/checked here; category-1 (basic attack) is exempt. **This gate is a cadence/cooldown gate,
    not an MP-pool affordability check** (corrected CYCLE 7 — there is no `available MP < 100 × factor`
    subtract on this path).
14. **Resolve effective target** by `target_mode`. For self / ground modes the target becomes the
    local player.
15. **Range / line-of-sight** (§2.2). Out of range → move-closer (code **8**) or code **21**; terrain
    blocked → code **9**; invalid target-state → code **10**.
16. **Cast-cost affordability gates** (skill-data +1368 / +1370). The gate blocks if current HP
    `< hp_cost (+1368)` (string id 3011; with a `< 30000` client guard) or current stamina
    `< stamina_cost (+1370)` (string id 3012). These are **flat resource-affordability checks**, not
    timer comparisons. Both use the executor-direct string ids 3011/3012 (see §2.3 note on the two
    string-id layers).
17. **Build target arrays** (§3) — fills the ally/PC id array and the enemy id array. If **neither**
    is populated → code **12**.
18. **Success.** Face the player toward the aim point, **record the cast time as "now"** (the
    cast-cadence timestamp is stamped to now **only** on a successful send), send `CmsgUseSkill`
    (2/52), return code **0**.

### 2.2 Range and line-of-sight

- **Effective range** = `base_range` (skill-data +1312) + the caster's body radius + a per-buff
  range bonus (read from a buff slot). Clamp to a minimum of **1.0** game units. (Special-weapon
  targets, e.g. weapon-class 53 / 55, override the range to a target-derived value.)
- **Distance test.** Use the **squared planar (XZ) distance** from the player position to the aim
  point, compared against `effective_range²`. If beyond range either the client sets a "needs
  approach" state, issues a move toward the aim point, and returns code **8** (move-closer, no toast),
  or it returns code **21** when auto-approach does not apply.
- **Terrain / line-of-walk test** on the aim point → code **9** if the terrain blocks the path.
- **Target-state test** (§3.1) → code **10** if the target is not valid. Skipped for the
  ground/point target mode (shape 5).

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

> **Two string-id layers.** The table above is from the **UI wrapper** that displays user-facing
> toasts. The **executor itself** (the battle controller's action executor and the AoE collector)
> emits a different set of MessageDB ids directly: 3001/3002/3003/3004/3005/3007/3009/3010/3011/3012/
> 3030/2157/59006/74307/41001/41002/4050/2162/3006. Both layers are real and distinct. The 3011/3012
> ids correspond to the +1368 HP cost and +1370 stamina cost gates respectively.
>
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
| 3 | chain / nearby AoE | Radius² from `aoe_radius` (+1316), squared; the enemy radius is **halved** (`(+1316 × 0.5)²`) when both weapon-req fields (+1344 and +1348) are set; iterate actors with squared XZ distance to the primary target `< radius²`. |
| 4 | cone / forward-line AoE | A circular **sector**. Length `L = base_range (+1312) + caster radius`; the forward axis is the normalized vector from the primary target to the caster, scaled by `L / 5.0`, origin offset to the caster; effective squared range = `2 × L²`. The **half-angle = +1316 in DEGREES × π/180** (radians). Per actor: include iff squared XZ distance ≤ `2·L²` AND the bearing `atan2(actor.x − center.x, actor.z − center.z)` is inside the `facing ± half-angle` window. |
| 5 | ground / point | No actor targets are resolved (returns immediately). Used for ground-targeted / blink casts. |
| 6 | party AoE | Walks the party roster; radius from `aoe_radius` (+1316). |
| 7 | faction / group-gated single | Style / team match against the target. Fail → string 3003. |
| 9 | PK-gated single | Team-byte gate; self → string 3005; fail → string 3001. |
| 10 (0x0A) | radial AoE, both factions | Radius² from `aoe_radius` (+1316); selects both PC and mob targets in range. |
| 11 (0x0B) | self-only | Clears the arrays, sets PC-count = 1 with the caster as the single target. |

> Modes 8 and 12+ were not observed in this subsystem; treat any unseen mode value as `UNVERIFIED`.

**Boost-doubling rule** (applies in every AoE branch): when the event-boost flag — **`*(u8*)(g_LocalPlayer+1516) == 1`** — is set **and** the base hit count ≥ 2, the **hit count doubles** (×2) and the **area quadruples** (radius ×2, area ×4). The combined count is then clamped to **40**. The offset +1516 is pinned (CONFIRMED); the *origin* of the flag (world buff / event toggle) is still `UNVERIFIED` (open question 5).

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

### 4.1 Hotbar assignment de-duplication (family / tier rule)

The quick-slot **apply-or-clear** path enforces a family de-dup rule when placing a skill into a
hotbar slot (`CONFIRMED`, pure client logic — no capture needed):

- It resolves the slot's skill via the id-keyed catalog (§1.1), then scans the other 240 hotbar slots.
- **A skill is refused if another hotbar slot already holds a skill of the same `global_category`
  (+4) at a *higher* `tier_byte` (+520)** — i.e. **you cannot slot a lower form of a family when a
  higher form of the same family is already slotted.** The comparison is `candidate.tier_byte <
  occupant.tier_byte` for occupants sharing the candidate's `global_category`.
- On success the path writes `skill_id` (+0) and `points` = `skill_rank` (skill-data +1292) into the
  record (§1.5) and refreshes the relevant panels; otherwise it clears the slot.

> Implementation note: 240 parallel arrays is a flat structure-of-arrays. The .NET model can use a
> single `CooldownSlot[240]` (struct-of-arrays or array-of-structs) keyed by hotbar slot index;
> category 1 (basic attack) and category 5 are **exempt from arming a cooldown** (see §2 gate 13 and
> §5.2). The hotbar de-dup gate (§4.1) belongs in `Client.Domain` as part of hotbar assignment.

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
- **Resource consumption (CONFIRMED):**
  - **HP cost:** `hp_cost` (+1368, i16) is subtracted from the player's current-HP field (+176,
    64-bit). The same +1368 value that gated the cast (§2.1 gate 16) is now the consumed amount.
    Magnitude is server-authored — `CAPTURE-PENDING`.
  - **Stamina cost:** `stamina_cost` (+1370, u16) × `target_count` × `buff_factor` (a value from
    a buff slot keyed on buff key 76, field +4; else 0), floored at the base +1370 value, is
    subtracted from the player's current-stamina field (+184). Magnitude is `CAPTURE-PENDING`.
  - Both HP and stamina globals (the current-HP global, 64-bit, and the current-stamina global,
    32-bit) are mirrored back from the actor
    fields after subtraction.
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
- Marks the caster's AoE-active state: sets **actor+1828 (dword idx 457)** to 3 (AoE-active); the
  clone count comes from **actor+1832 (dword idx 458)**. `CONFIRMED` (see `specs/buffs.md §3.2`).

> **Teleport / blink** has **no dedicated opcode.** It is the ground/point target mode (mode 5) plus
> the move applied directly to the caster's transform — the ground-point cast moves the caster.

---

## 6. Buff / debuff (status) model

> **Authority cross-reference (CYCLE 7).** The full buff / status model — the **30-slot per-actor
> buff table at actor+520 (12-byte stride)**, the canonical buff-slot push **5/31
> `SmsgBuffSlotUpdate`**, and the **4000 ms** buff tick — is owned by the dedicated **`specs/buffs.md`**.
> This section retains the skill-side view of buff slots and the effect-code applicator for
> implementers of the cast / skill subsystem, but `buffs.md` is the authority and must be consulted
> for the buff-table model. Note: the live buff-slot stream is **5/31**; the earlier framing that tied
> buffs to **4/102** is superseded — 4/102 is a 476-byte skill-window *snapshot rebuild* (a different
> thing; §6A.4), not the per-slot buff opcode.

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

- **Per-actor buff table — 30-slot table at actor+520, 12-byte stride** (slot indices span 0..30; the
  full slot-count / capacity model is owned by `buffs.md`). The primary status table on each actor. A
  **parallel secondary table** holds a u16 magnitude/percent plus a byte — this is the buff's
  **strength** (used by %-gated effects). A *set* writes all three dwords; a *clear* (value == 0) zeros
  the effect-code dword and stashes the param tail. See `buffs.md` for the authoritative table model.
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
| 48 (0x30) | **Dispel / cleanse:** clears the effect-code-43, -46, and -47 slots across the buff table; resets stance to the default. | LIKELY |
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

A periodic **buff tick** (the 4000 ms tick owned by `buffs.md`) walks the per-actor status table:

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

## 6A. Hotbar, skill-point, and skill-window pushes (5/33, 4/41, 4/150, 4/102)

These S2C pushes drive the hotbar / skill-point / skill-window UI. **Structure and routing are
control-flow-confirmed on build 263bd994; the actual on-wire VALUE bytes are `CAPTURE-PENDING`** (no
capture available). Payload-relative offsets below are the field positions within each push body
(wire field positions / widths are owned by `packets/*.yaml` / `opcodes.md` — referenced here, not
re-derived).

### 6A.1 `SmsgSkillHotbarSlotSet` (5/33) — set one hotbar slot

A 20-byte push, **gated to the local player**: the actor is resolved by a composite (sort, actor-id)
key and must equal the local-player handle. On `slot < 240` it writes the slot record (§1.5):

| Payload offset | Field | Action |
|---|---|---|
| +0 / +4 | sort / actor-id | composite key → must resolve to the local player |
| +8 | `slot` (u8, 0..239) | target hotbar slot |
| +0x0C | `skill_id` (i32) | written to record +0 |
| +0x10 | `points` (i16) | written to record +4 |

### 6A.2 `SmsgSkillHotbarAssignResult` (4/41) — assignment result + reason-string table

A 24-byte push. A **gate byte at +0x08** (`1` = ok) decides apply-vs-clear: on `1` the assignment is
applied; otherwise the echoed slot (slot at **+0x0C**) is **cleared** (record +0 set to `0`). A
**reason byte at +0x09** (values `1..8`) selects a localized notice broadcast to chat / notice via the
message database:

| Reason byte | Message-DB string id |
|---|---|
| 1 | 3020 |
| 2 | 3021 |
| 3 | 3022 |
| 4 | 3023 |
| 5 | 3024 |
| 6 | 3025 |
| 7 | 3026 |
| 8 | 3032 |

`CONFIRMED` (the reason→id table is concrete in the binary).

### 6A.3 `SmsgSkillPointUpdate` (4/150) — skill-point pool / level-up

Gated on the local player: a **valid byte at +0** must be `1` **and** an **idkey at +4** must equal the
**local-player id stored at player struct +92 (0x5C)** (the id field, *not* the player object pointer).
A **mode byte at +8** then branches:

- **mode == 1 (set total):** stores `value` (payload **+0x0C**) as the displayed skill-point pool
  (the local-player skill-point total field). `CONFIRMED`.
- **mode == 2 (level-up):** shows a level-up notice via the message database — string **74313** on the
  `value == 0` path and string **74314** on the `value != 0` path — formatted with class / level data
  read from the local player (a gate word and a class byte). `CONFIRMED`.

> The display-only **255 skill-point cap** (if applied) lives in the window-render formatter, **not**
> in this store path — the handler stores the raw `value`. `static-hypothesis`. The actual numeric
> skill-point / level values on the wire are `CAPTURE-PENDING`.

### 6A.4 `SmsgSkillWindowStateUpdate` (4/102) — 476-byte snapshot rebuild

**Not a window toggle:** 4/102 carries a **476-byte snapshot** (the player stat block plus a set of
fixed active-buff records) and **fully rebuilds** the skill / state window and the buff bar via a large
UI formatter (`%d / %d` text). `CONFIRMED` for the size / role; the **internal layout of the 476-byte
snapshot** (per-stat / per-buff-record offsets) is out of scope here — flagged for the
struct-cartographer (`static-hypothesis`), and the live values are `CAPTURE-PENDING`.

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
| 5:33 | `SmsgSkillHotbarSlotSet` | S2C | Sets one hotbar slot (local-player-gated); writes the 8-byte record (§1.5, §6A.1). |
| 4:41 | `SmsgSkillHotbarAssignResult` | S2C | Hotbar assignment result; reason byte → string-id table 3020..3032 (§6A.2). |
| 4:150 | `SmsgSkillPointUpdate` | S2C | Skill-point pool set / level-up (msgs 74313/74314); idkey @ player+92 (§6A.3). |
| 4:97 | `SmsgAreaSkillEffectPanel` | S2C | Area skill-effect result panel. |
| 4:102 | `SmsgSkillWindowStateUpdate` | S2C | 476-byte stat/buff snapshot that rebuilds the skill/state window + buff bar (§6A.4). |
| 5:51 | `SmsgSkillGuideState` | S2C | Skill guide state. |

**Related runtime structs:** `structs/skill.md` (the 240×8-byte hotbar record array — §1.5 dual-view
refinement; the catalog primary/secondary indexes; `skill_id` +0 / `global_category` +4 / `tier_byte`
+520 / `skill_rank` +1292 / `category` +1306), `structs/actor.md` (last-known position, current-HP
vital, lifecycle/alive word, the buff-table and timed-state regions; the local-player id field at
player +92), `structs/stats.md` (HP/MP auras — a **different** mechanism from the §6 buff slots: auras
feed the max-HP/MP multiplier, buff slots drive visual / control state).

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
- **Skill catalog** → `Client.Domain` / data-tables: model the runtime skill object as the 1504-byte
  record + per-rank rows (§1.2–1.4). Build **two indexes** — id-keyed (`skill_id` +0) and
  family-keyed (`global_category` +4, skip `+4 == 0`) — mirroring §1.1.
- **Hotbar** → `Client.Domain`: a single `HotbarSlot[240]` array of 8-byte records (`{int32 skill_id;
  int16 points; int16 pad}`, §1.5), **not** two parallel arrays. Implement the §4.1 family/tier de-dup
  gate (refuse a lower `tier_byte` form when a higher form of the same `global_category` is slotted) as
  part of hotbar assignment.
- **Cooldown table** → `Client.Domain`: a flat 240-slot cooldown model keyed by hotbar slot, with the
  category-1 / category-5 exemptions.
- **Effect / buff model** → `Client.Domain`: a per-actor status array (30-slot table at actor+520,
  12-byte stride) with a parallel magnitude array, a single-decrement tick, and refresh-by-slot
  semantics (no stack counter). The effect-code applicator (§6.2) is a switch on the effect code.
  **`buffs.md` is the authority for the buff-table model (5/31 + the 4000 ms tick); build the buff
  model from it.**
- **Wire decoding** → `Network.Protocol` decodes `CmsgUseSkill` / `SmsgActorSkillAction` / the hotbar /
  skill-point / window pushes per their `packets/*.yaml`; cite `// spec: Docs/RE/packets/<name>.yaml`
  on each offset. The 4/41 reason→string-id table (§6A.2) and the 4/150 level-up message ids (§6A.3)
  map to the project's own message-DB / string table.
- **Model the cast costs (§5.2 / +1368 / +1370) as flat resource-affordability checks**: gate on
  `current_hp >= hp_cost (+1368)` (with a `< 30000` guard) and `current_stamina >= stamina_cost (+1370)`;
  subtract the confirmed amounts on the 5/52 confirm path. The +1332 field is the cast-**cadence**
  gate (`elapsed_ms < 100 × cast_cadence_factor`), not an HP/MP affordability check. Do not implement
  the `skills.scr` per-rank rows (§1.3) or any effect-code meaning beyond the table in §6.2 without
  first resolving the corresponding UNVERIFIED item. Implement the range floor as **1.0**, the default
  basic-attack skill id as **121100050**, and clamp the AoE hit count to **40**. The boost flag is at
  local-player +1516 (u8 == 1). The actual on-wire VALUE bytes (5/33, 4/41, 4/150, 5/52, 5/31) and
  all server-authored magnitudes are `CAPTURE-PENDING` — gate any magnitude assumption on a capture.
  Mark them as TODO with a pointer back to this spec.
