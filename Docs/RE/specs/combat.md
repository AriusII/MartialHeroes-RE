# Combat math & stat-aggregation pipeline (clean-room spec)

Neutral, data-only model of how the legacy *Martial Heroes* client derives its **combat stats**
(attack rating, hit/accuracy rating, defence, critical, damage range, hit/defence/damage rates) from
primary stats + equipment + set bonuses + buffs/auras + weapon proficiency, and of where **damage
itself** is resolved. Promoted from dirty-room notes; rewritten in our own words — no decompiler
identifiers, no binary addresses, no byte offsets framed as binary locations.

This document is design input for the **domain engineer** (the deterministic combat/stat model in
`Client.Domain`) and the **protocol engineer** (the combat/stat wire messages). It is the combat
counterpart to the vitals spec and shares the same aggregation pipeline.

---

## Status header (read first)

> **Headline finding — the client is server-authoritative for damage resolution.**
> No client-side per-hit damage roll (attacker damage vs. defender defence/evasion/crit → an HP
> delta) was found. Incoming HP changes arrive over the wire as new **absolute** values; the client
> copies them in and clamps the local player's value against its own locally-computed maximum. What
> the client computes locally is a **derived combat-stat mirror** (attack rating, hit rating,
> defence, crit, damage range, the rate terms) shown on the character sheet and held as a single
> aggregate — the client's mirror of the server's combat math. Re-implementations must therefore
> treat client combat math as **display/parity only**; the authoritative resolution lives on the
> server we are rebuilding.

| Area | Confidence |
|---|---|
| Server-authoritative damage; no client damage roll found | HIGH (no roll located; cannot prove absence of an unanalysed path — see UNVERIFIED #1) |
| Derived combat-stat field set (the named aggregate) | HIGH — names are the developers' own debug labels |
| Attack-base & secondary-base stat-weight coefficients (Section 3) | HIGH — bit-exact literals recovered from the client |
| Weapon-proficiency hit-penalty table (Section 4) | MEDIUM — banding recovered; one boundary band ambiguous |
| Three-source aggregation architecture (Section 2) | HIGH — matches the vitals pipeline exactly |
| Per-item field → derived-stat mapping (Section 2.3) | MEDIUM — corroborated against the item catalogue from the consuming side; treat exact per-item source columns as best-fit until a capture confirms |
| `critical_rate_` vs `critical_hit_` split; rate-pair `[0]/[1]` semantics; `order_special_` element map | LOW — flagged UNVERIFIED throughout |
| Combat-phase timing (Section 5) is server-paced, not a client cooldown formula | HIGH |
| In-world combat loop: server-authoritative damage via a single battle controller (Sections 9–11) | HIGH (CODE-CONFIRMED static read; CAPTURE-UNVERIFIED — see below) |
| Two-tier target acquisition; basic melee = skill `2/52` slot `0xFF` (Sections 9–10) | HIGH (CODE-CONFIRMED) |
| Attack cadence is server-paced (swing-ready timestamp armed/re-armed by the server) (Section 11) | MEDIUM — strong inference; the timestamp owner (client-on-send vs. server packet) is CAPTURE-UNVERIFIED |
| Damage-kind → floating-number motion/colour selection; multi-hit split (Section 12) | HIGH (CODE-CONFIRMED) |

**UNVERIFIED list** is consolidated in Section 7. Korean strings referenced here are **CP949 /
EUC-KR** encoded (no BOM), consistent with `formats/config_tables.md`.

**Cross-references**
- Vitals (max HP / max MP, the shared three-stage pipeline): `structs/stats.md` (VitalFormula).
- Item stat-grant fields and the set-bonus pairs: `structs/item.md`.
- Client-side catalogues that supply the numeric inputs: `formats/config_tables.md`
  (`items.csv` weapon/armour/gem stats; `users.scr` class grid; `userlevel.scr` / `userpoint.scr`
  stat curves; `exp.scr`).
- Actor vitals storage and the on-wire vitals fields: `structs/actor.md`.
- Skill activation path: `packets/2-52_use_skill.yaml`, `packets/5-52_actor_skill_action.yaml`.
- Skill cast pipeline, target-resolution shapes, skill `category` / `target_mode` kinds, and the
  cooldown ("recast") subsystem: `specs/skills.md`. Sections 9–11 below describe the **in-world
  combat loop** (target acquisition → attack send → result application → cadence) that drives that
  cast pipeline; the skill cast gates and AoE shapes themselves are owned by `specs/skills.md`.

---

## 1. The derived combat-stat aggregate (field dictionary)

The client holds **one combat-stat aggregate** for the local player. It is fully cleared and
re-accumulated whenever inputs change (see Section 2). Each entry below is the summed (or, where
noted, maxed) contribution from worn gear + active buffs/auras. The names are the developers' own
internal labels recovered from a debug-dump string set, so they are stable vocabulary for our model.

| Field (our name)        | Type   | Meaning                                                       |
|-------------------------|--------|--------------------------------------------------------------|
| `Str`                   | i32    | Aggregated STR contribution                                  |
| `Dex`                   | i32    | Aggregated DEX contribution                                  |
| `Vital`                 | i32    | Aggregated CON / vitality contribution                      |
| `Inte`                  | i32    | Aggregated INT contribution                                 |
| `Agil`                  | i32    | Aggregated AGI contribution                                 |
| `MaxStamina`            | i16    | Max-stamina contribution                                     |
| `CriticalValue`         | i32    | Critical **damage** bonus (flat extra damage on a crit)     |
| `MinDamage`             | i32    | Minimum weapon/attack damage                                 |
| `MaxDamage`             | i32    | Maximum weapon/attack damage                                 |
| `Damage`                | i32    | Flat damage / attack-power add                              |
| `Defence`               | i32    | Flat defence (armour) value                                  |
| `HuntDamageRate[0..1]`  | i32×2  | PvE damage-rate terms (vs. monsters)                        |
| `PvpDamageRate[0..1]`   | i32×2  | PvP damage-rate terms (vs. players)                         |
| `MaxLife`               | i32    | Max-HP flat contribution (see `structs/stats.md`)           |
| `MaxEnergy`             | i32    | Max-MP flat contribution                                     |
| `MaxLifeRate`           | f32    | Max-HP percentage multiplier add                            |
| `MaxEnergyRate`         | f32    | Max-MP percentage multiplier add                            |
| `CriticalRate`          | f32    | Critical **chance** rate (probability term)                 |
| `HitRate`               | f32    | Hit / accuracy rate                                          |
| `DefenceRate`           | f32    | Defence rate (mitigation %)                                 |
| `CriticalHit`           | f32    | Second, distinct critical float (see note)                  |
| `OrderSpecial[0..3]`    | f32×4  | Four element-/school-specific special-rate buckets          |
| `Range`                 | f32    | Attack range                                                 |

Notes:
- `CriticalRate` and `CriticalHit` are **two distinct float fields**. Most likely one is
  "chance to crit" and the other is "crit severity/multiplier", but which is which is **UNVERIFIED**.
  Model both; do not collapse them.
- The PvE vs. PvP split is real and named: `HuntDamageRate*` applies vs. monsters, `PvpDamageRate*`
  vs. players. Both pairs accumulate from gear/buffs identically; the **selection** between them
  happens at damage application — which is server-side. The client merely carries both rate sets.
  The `[0]/[1]` index roles (min/max? additive/multiplicative? two schools?) are **UNVERIFIED**.
- `OrderSpecial[0..3]` are four element/school buckets; each buff contributes
  `value / 100.0` into one bucket selected by a per-buff element discriminator. The mapping of
  bucket index → element/school is **UNVERIFIED**.
- The client renders **MISS** and **CRITICAL** combat floating-text, but it **receives** those
  outcomes from the server; it does not roll them locally.

The character sheet surfaces, in order: max stamina, max HP, max MP, name/title, then
**attack rating** and **hit rating** (Section 3). Defence/crit/rate fields are held in the aggregate
for tooltips and server-parity but are not presented as a damage-vs-defence resolution.

---

## 2. Aggregation pipeline (how each stat is assembled)

Every derived stat is built from the same three sources, identical in shape to the vitals pipeline
in `structs/stats.md`:

```
derived_stat = equipment_sum     (sum one per-item grant field over all worn equipment slots,
                                   skipping the non-stat slot — see §2.1)
             + buff_sum           (sum/accumulate contributions from active buff/aura entries)
             + slot_modifiers      (per-character modifier slots, looked up by a stat key — §2.2)
             + global_addends      (two flat per-stat globals; often 0)
```

Effective primary stats (STR/DEX/AGI/INT/CON) are computed **lazily, per stat, on demand** — there
is **no monolithic "recompute everything" function** for them; each accessor re-sums its sources on
every call and nothing is cached. The derived **combat** block (Section 1) is the exception: it is
rebuilt as a whole during a periodic recompute (~1 s cadence) and on input-change events.

The effective primary-stat accessors add, on top of the three-source helper:
- a per-stat **buff term** keyed by a primary buff kind, and
- a shared **all-stats buff term** keyed by a single shared buff kind, and
- a **server-supplied base** value per stat (written by the stat-update ack; see Section 6).

This matches `structs/stats.md`:
`effective_stat = server_base + equipment_flat + set_bonus + buff_value + flat_global_addend`.

### 2.1 Equipment slot iteration & the skip rule

Accumulators iterate the worn-equipment slot table and read one grant field per item.
**One slot is skipped by every stat accumulator** — the non-stat-contributing slot (mount / pet /
special-effect slot). The set-bonus distributor additionally **skips a second slot** (the
visual/weapon-refresh slot). These two skips match the slot semantics in `structs/item.md` and the
equip-slot reference in `formats/config_tables.md` §4.5.

### 2.2 Per-character modifier slots (stat key → value)

A small fixed-size table of per-character modifier slots is scanned by a **stat key** (a stable
small integer). When a slot with the matching key is present, its value is added to the running
total. Stat keys observed feeding combat/stat math (this list is **partial** — only keys touched by
analysed accessors are known):

| Stat key | Role |
|---|---|
| 70 | STR source |
| 71 | AGI source |
| 72 | DEX source |
| 73 | INT source |
| 74 | CON / vitality source |
| 93 | Shared **all-stats** add (added by every primary-stat accessor) |
| 7, 2 | Max-HP flat adds |
| 81 | %HP buff (value / 100 added to the HP percentage multiplier) |
| 15, 94, 5 | Hit-rating terms |
| 83 | Hit % multiplier — applied as `(value − 100) %` adjustment to the running hit total |
| 61 | Hit-rating final flat add (when present) |
| 16, 20 | Accuracy-rating terms |

The same value may be read twice (once via a linear scan, once via a direct-index variant) and both
results summed — model this as a single conceptual "read this slot" that returns the slot's value.

### 2.3 Per-item grant fields → derived stat

Each derived stat has a dedicated accumulator that sums **one per-item grant field** across worn
gear. The item record carries these grant fields (corroborated from the consuming side against
`structs/item.md` and the `items.csv` columns in `formats/config_tables.md` §4.3):

| Derived stat | Item grant source (clean catalogue reference) |
|---|---|
| STR | item STR-grant field (`structs/item.md`) |
| AGI | item AGI-grant field |
| DEX | item DEX-grant field |
| INT | item INT-grant field |
| CON | item CON-grant field |
| `MaxLife` (HP flat) | item HP-grant field (= `items.csv` `bonus_HP`, col65) |
| `MaxEnergy` (MP flat) | item MP-grant field |
| `MaxStamina` | item 16-bit stamina-grant field (**UNVERIFIED**: stamina vs. set-name id) |
| `Damage` / attack | **sum of two** item attack fields (a base-attack and a weapon-attack component); aligns with `items.csv` `bonus_atk` (col64), `bonus_ext_atk` (col68), `weapon_stat_A/B/C` (col85/86/95), `min_attack`/`max_attack` (col87/90) |
| `Defence` | item defence field (= `items.csv` `phys_defense` col94 / `armor_defense` col96) |

Buff/aura entries feed a **wider** field set into the aggregate than gear does — every named field
in Section 1 can receive a buff contribution. Buff floats (`HitRate`, `DefenceRate`, `CriticalHit`,
`Range`, `OrderSpecial`) accumulate as floats; integer fields accumulate as integers. The
`OrderSpecial` bucket for a given buff is chosen by that buff's element discriminator (observed
discriminator values: 0, 1, 2, 3, 5).

**UNVERIFIED:** the precise per-item source column for the attack pair and the exact stamina field
(see Section 7). The set of *which* fields exist is solid; binding each to a single `items.csv`
column should be confirmed against a capture or a parsed item sample.

### 2.4 Set bonus (all-or-nothing)

The set-bonus distributor produces a per-stat set-bonus vector. It runs in two phases:

1. **Per-piece phase (always):** iterate worn items (with both skip slots excluded); register each
   item flagged as a set item; add each registered item's **per-piece** bonus column to the matching
   output stat.
2. **Set-complete phase (gated):** read each candidate item's **set-type id** and its **required set
   piece count**, count how many registered items share the same set-type id, and **only if the
   matched count equals the required count** add the item's **set-complete** bonus column to the same
   outputs.

So a partial set grants per-piece bonuses only; a complete set grants per-piece **plus** the full-set
bonus. This is the same all-or-nothing rule as `structs/stats.md` and the same set-type-id / set-flag
/ required-count fields described in `structs/item.md`. The 10-element output vector covers, in
order, STR / AGI / DEX / INT / CON / HP / MP / a 16-bit quantity / attack / defence.

**UNVERIFIED:** the exact output-index → stat mapping beyond index 0 (≈ STR); disambiguate by each
consuming accumulator's argument position. The per-stat **magnitudes** are item data, not constants
— they come from the item catalogue, not from this spec.

---

## 3. Attack-rating and hit-rating formulas

Two stat-weighted "base" helpers convert the five primary stats into a damage base and an
accuracy/secondary base. **The coefficients are bit-exact** (recovered as 32-bit float literals) and
are implementable now; they are the closest thing to a client-side combat formula.

### 3.1 Physical attack base (returns float)

```
attack_base = ( STR*2.5 + DEX*2.0 + AGI*2.3 + CON*1.0 + INT*1.0 ) * 0.2
```

| Stat   | Weight | Exact 32-bit-literal value (for bit-exact parity) |
|--------|--------|----------------------------------------------------|
| STR    | 2.5    | 2.5                |
| DEX    | 2.0    | 2.0                |
| AGI    | 2.3    | 2.299999952316284  |
| CON    | 1.0    | 1.0                |
| INT    | 1.0    | 1.0                |
| (scale)| ×0.2   | 0.20000000298023224 |

### 3.2 Secondary base (returns float — magic/ki or accuracy base)

```
secondary_base = ( STR*1.4 + DEX*2.65 + AGI*1.5 + CON*2.1 + INT*1.1 ) * 0.2
```

| Stat   | Weight | Exact 32-bit-literal value |
|--------|--------|----------------------------|
| STR    | 1.4    | 1.399999976158142  |
| DEX    | 2.65   | 2.650000095367432  |
| AGI    | 1.5    | 1.5                |
| CON    | 2.1    | 2.099999904632568  |
| INT    | 1.1    | 1.100000023841858  |
| (scale)| ×0.2   | 0.20000000298023224 |

This base is DEX-dominant and INT-weighted (a distinct distribution from §3.1), consistent with a
second damage/accuracy school. As with the vitals weights in `structs/stats.md`, store the weights as
`float` and widen to `double` for accumulation if bit-exact parity matters; otherwise the decimal
values above are sufficient.

### 3.3 Attack-rating getter (returns int)

The full attack rating composes the base with slot modifiers, equipment, weapon, level, and class
terms, in this order:

```
attack_rating =
      slot[15] + slot[94 if nonzero] + slot[5]      (hit/attack modifier slots, §2.2)
    + weapon_term                                    (a per-equipped-weapon integer lookup)
    + attack_base                                    (§3.1)
    + weapon_grade * 0.1                             (weapon-grade helper returns 1.0 → contributes +0.1)
    + damage_equip_sum                               (the two-field attack accumulator, §2.3)
    + level_term * 0.5                               (a level/grade byte × 0.5)
    + 2.0   if the class/grade byte >= 8
    + apply (slot[83] − 100) % as a multiplier on the running total
    + slot[61] flat add (when present)
```

### 3.4 Hit / accuracy-rating getter (returns int)

```
hit_rating =
      slot[16] + slot[20]                            (accuracy modifier slots, §2.2)
    + weapon_term
    + secondary_base                                 (§3.2)
    + weapon_grade * 0.1                             (+0.1)
    + defence/accuracy_equip_sum                     (the defence-family equip accumulator, §2.3)
    + level_term * 0.5
    + 300.0                                          (flat accuracy baseline)
    + 300.0   if a rank-progress gate is set
    + 2.0     if the class/grade byte >= 8
    - apply proficiency_penalty %  on the running total   (§4)
    + 300.0                                          (a second flat accuracy baseline)
```

The two large `+300.0` flat terms and the percentage penalty are characteristic of an
accuracy/hit-rating formula (a big base that gear/level/penalty modulate), not a damage formula.
**CONFIRMED shape; UNVERIFIED:** the individual role of each `300.0` term (base accuracy vs. UI
offset) and the exact source of the `weapon_term` per-weapon lookup (see Section 7).

---

## 4. Weapon-proficiency hit penalty (table)

A percentage **reduction** is applied to hit rating, keyed on a weapon-proficiency / weapon-type byte
carried on the local player's equipment/stat state. Higher proficiency bands impose larger
penalties in the recovered banding (an unproficient weapon can zero out the hit bonus entirely):

| Proficiency-key range | Penalty (%) |
|---|---|
| 4 .. 10 | 25 |
| 11 .. 30 | 50 |
| 31 .. 74 | 0 or 75 (see note) |
| 75 and above | 100 |

Applied as:

```
hit_rating *= (1.0 - penalty / 100.0)
```

A 100 % penalty means an unproficient weapon contributes no hit bonus.

**UNVERIFIED:** the `31..74` band has two recovered exit paths (one returns 0, one returns 75); the
exact boundary semantics are not pinned. Also UNVERIFIED whether the keying byte is a *weapon mastery
level* or a *weapon-type id*. Treat this whole table as MEDIUM confidence and confirm against a
capture before binding penalties to specific weapon classes (`items.csv` weapon subtypes,
`formats/config_tables.md` §4.6).

---

## 5. Damage application & attack-phase timing

### 5.1 Where damage lands (wire-driven, absolute values)

| Opcode (major:minor) | Name | Role in combat |
|---|---|---|
| `5:53` | `SmsgActorVitalsAndPairState` | Writes **absolute** current HP / MP / stamina into the target actor. For the local player, each value is clamped against the **locally-computed maximum** (`structs/stats.md`) and negatives floor to 0. This is where "damage taken/dealt" becomes visible — as a new absolute HP value from the server, not a client subtraction. |
| `4:100` | `SmsgCombatAttackUpdate` | Carries a combat **phase** indicator, a sub-kind selector (a reset sentinel exists), and a value. Drives **attack-swing timing** state on the local player; see §5.2. Server-paced — not a client cooldown formula. |
| `4:99` | `SmsgCombatResultMessage` | A combat/training **session result** — formats reward / EXP-delta strings (localized message series, CP949). UI messaging, no client math. |

Field layouts for `4:99` and `4:100` are **not yet specced** (no packet YAML); they are listed in
`opcodes.md` as routing-confirmed only. They are scoped enough for a future packet spec but are
**out of scope for combat math** here.

### 5.2 Attack-phase timing state

The local player carries a small attack-timing block (a swing-start timestamp, a value field, and a
phase indicator, in one contiguous run). `SmsgCombatAttackUpdate` (`4:100`) advances it: on the
"swing-start" phase the client stamps a millisecond timestamp and stores the message value; a later
phase resets it. Cadence (attack speed) is therefore **gated by server phase messages**, so attack
speed in the live game was server-paced. There is **no client-side attack-speed formula**, though the
`items.csv` `attack_speed` coefficient (col75; range ~0.26–0.37) is the per-weapon input the server
would have used.

### 5.3 Combat visuals & local input (no math)

| Opcode | Name | Role |
|---|---|---|
| `5:139` | `SmsgAttackEffect` | Plays particle FX + SFX keyed on an effect id. Pure presentation. |
| `5:14` | `SmsgCombatEffectInstanceSpawn` | Spawns a combat effect instance. Presentation. |
| `5:147` | `SmsgActorCombatFlagUpdate` | Sets/clears an in-combat flag. Presentation/state. |
| `5:52` | `SmsgActorSkillAction` | Skill action / combat-result header + per-target records (see `packets/5-52_actor_skill_action.yaml`). |
| `2:52` (C2S) | `CmsgUseSkill` | The client **request** to activate a skill (skill slot + target ids). Sends **intent only** — never a damage number. See `packets/2-52_use_skill.yaml`. |

The basic **melee-attack** request **is the same `2:52` `CmsgUseSkill` message** carrying a skill-slot
byte of `0xFF` — there is **no separate plain-attack opcode**. *(corrected 2026-06-13: an earlier
draft listed the basic-attack minor as "not pinned (UNVERIFIED #7)"; the send path is now
CODE-CONFIRMED to reuse `2:52` with slot `0xFF`. UNVERIFIED #7 is resolved — see Section 10.)* In all
cases the client sends intent; the server computes and returns the result.

---

## 6. Recompute triggers & the stat-update chain

Because primary stats are lazy (Section 2), a "recompute" is just: mutate an input, then let the next
accessor / the periodic combat recompute re-read it. The combat aggregate (Section 1) is rebuilt by a
periodic recompute (~1 s) and after input-change messages.

Inputs that mutate, and the messages that change them:

| Opcode (major:minor) | Name | What it changes |
|---|---|---|
| `2:29` (C2S) | `StatAllocate` (request) | Sends five **absolute** stat values (current base + pending allocation) when the player applies the stat panel. |
| `4:29` | `SmsgStatUpdate` | The allocate **ack**: writes the five server-authoritative stat bases, then triggers a stat-panel refresh. This is the canonical "stats changed, re-read everything" event. Layout drafted in `packets/4-29_stat_update.yaml`. |
| `5:32` | `SmsgLevelUp` | New level, vitals, and stat points; refresh + FX. Layout drafted in `packets/5-32_level_up.yaml`. |
| `4:134` | `SmsgStatChangeNotify` | Stat-change notification + refresh. |
| `5:31` | `SmsgBuffSlotUpdate` | Updates the buff/aura table (the source of the per-stat and shared buff terms, §2.2). |
| `5:67` | `SmsgStatsUpdate` | Bulk stat-block update + refresh. |
| `5:53` | `SmsgActorVitalsAndPairState` | Writes current vitals (clamped to computed maxima). |

Equipment-change messages (equip/unequip acks and inventory slot updates) mutate the worn-equipment
slot table; the next accessor call reflects new gear with no explicit recompute call.

### 6.1 Stat slot wire ordering (hand-off to the protocol engineer)

The five stats are **not** packed on the wire in the same order they sit in client memory. The
allocate request (`2:29`) packs each stat as `server_base + pending_allocation`, but the wire slot
order interleaves STR / AGI / DEX / INT / CON differently from memory order. The `4:29` ack echoes the
same five slots. **The exact wire-slot permutation must be confirmed against a capture before binding
`packets/4-29_stat_update.yaml` field names to slots** (UNVERIFIED #8); this spec flags the
reordering but does not pin the permutation.

### 6.2 Server-base origin

The per-stat server bases (written by `4:29`) and the per-level stat curves originate from the
client-side catalogues, not from the binary alone: `userlevel.scr` (base stat per level),
`userpoint.scr` (allocation curve), and `users.scr` (per-class stat grid) in
`formats/config_tables.md`. The class-specific multipliers in `users.scr` (e.g. a +10 % / +15 %
deviation for certain classes) are the per-class stat differentiation; binding each `users.scr` float
position to a named stat is **UNVERIFIED** there and inherited here.

---

## 7. UNVERIFIED / open questions

1. **No client-side damage roll** (attacker damage vs. defender defence/evasion/crit → HP delta) was
   found; believed server-authoritative. Cannot prove no unanalysed path exists, but the named-stat
   mirror strongly implies the client only mirrors the server's model.
2. `CriticalRate` vs. `CriticalHit` — which is "chance" vs. "multiplier/severity".
3. `HuntDamageRate[0/1]` (PvE) vs. `PvpDamageRate[0/1]` (PvP) — the `[0]/[1]` index roles. The PvE/PvP
   split itself is CONFIRMED (separate named field pairs); only the index semantics are open.
4. `OrderSpecial[0..3]` element buckets — which element/school each index maps to (selected by
   per-buff discriminator values 0,1,2,3,5).
5. Exact per-item source columns for the attack pair (§2.3) and the 16-bit stamina/set-name field;
   reconcile against `structs/item.md` and a parsed `items.csv` sample before treating any single
   column as authoritative.
6. Per-character stat-key enumeration (§2.2) is partial — only keys queried by analysed accessors.
7. ~~The basic melee-attack C2S request (the action minor distinct from `2:52` skill use) was not
   pinned.~~ **RESOLVED (2026-06-13):** there is **no distinct minor** — basic melee is `2:52`
   `CmsgUseSkill` with skill-slot byte `0xFF`. See Section 10.
8. Stat-slot **wire ordering** for `2:29` / `4:29` (§6.1) — the memory→wire permutation must be
   confirmed by capture.
9. `weapon_term` (the per-equipped-weapon integer added inside §3.3/§3.4) — its source table was not
   fully traced; likely a per-weapon column in `items.csv` (candidate: `weapon_stat_A/B/C`).
10. Weapon-proficiency penalty `31..74` band boundary (Section 4): 0 vs. 75; and whether the keying
    byte is mastery level or weapon-type id.
11. Set-bonus output-index → named-stat mapping for indices 1..9 (index 0 ≈ STR).
12. All findings are **static** (no live capture). The stat-weight coefficients (Section 3) are
    bit-exact from the client; the **combination into a final damage number happens server-side** and
    cannot be confirmed from the client alone.

---

## 8. Implementation guidance

- **Damage resolution is server-side.** In `Client.Domain`, model the combat-stat aggregate
  (Section 1) and the attack/hit-rating formulas (Section 3) as a **pure, deterministic, engine-free**
  display/parity model. Do not implement a client damage roll. The server we are rebuilding owns
  resolution and must reproduce these formulas authoritatively.
- Reuse the vitals pipeline. The three-source aggregation (Section 2), the slot-8 skip, the
  all-or-nothing set rule, and the lazy per-stat accessors are the **same** machinery as
  `structs/stats.md`; share the code path. Apply float→int truncation at the same boundaries the
  vitals spec specifies.
- The coefficients in Section 3, the proficiency banding in Section 4, and the named field set in
  Section 1 are **implementable now**. The numeric magnitudes (per-item grants, set bonuses, per-stat
  server bases) are **data**, sourced from the catalogues in `formats/config_tables.md`; treat any
  formula output as **provisional** until those catalogues are parsed and a capture corroborates the
  UNVERIFIED items above.
- Keep `CriticalRate`/`CriticalHit` and the PvE/PvP rate pairs as distinct modelled fields even while
  their exact roles are open, so the model is forward-compatible when captures resolve them.

---

## 9. In-world combat loop & the battle controller (architecture)

> **Verification banner for Sections 9–12.** Everything in these sections is a **static read** of the
> legacy client (graded CODE-CONFIRMED where the control flow was followed end-to-end, PLAUSIBLE where
> inferred). **No live network capture corroborates any of it** — treat every opcode role, byte width,
> and timing constant as **CAPTURE-UNVERIFIED** until a capture confirms it. This is the same caveat as
> Sections 1–8 and as `specs/skills.md`.

The in-world combat loop is driven by **one global battle-controller object** — a process-wide
singleton (described here by role; we do not name it by address). It is reached through a guarded
one-shot getter and read from the combat input, send, cooldown, and result paths alike. It is the
single owner of "what am I attacking and when may I swing again". The loop is **fully
server-authoritative for damage**: the client sends *intent* (a skill / melee activate carrying a
target id), the server resolves the hit, and the client only *applies* the results it receives
(motion, FX, floating numbers, and absolute HP values). This is the same headline finding as the
status header and Section 5; this section adds the *control-flow mechanism* behind it.

End-to-end, one attack is:

1. **Acquire a target** (Section 10) — a global hovered/selected id, plus the controller's own
   per-controller picked target.
2. **Mouse-click → the controller's click handler** validates and resolves the picked target, then
   runs the action executor.
3. **The action executor** runs an ordered gate chain (alive / cost / range / cooldown — these gates
   are the same family documented in `specs/skills.md` §2.1) and, on success, **sends the request**
   (`2:52`, Section 10). If the target is out of range it emits a move-to-target request (`2:13`) and
   retries next tick — the **click-to-attack → walk-into-range → attack** loop.
4. **The server replies** with `5:52` `SmsgActorSkillAction` (swing/hit animation + per-target hit
   records → floating numbers and HP bars) and `5:53` `SmsgActorVitalsAndPairState` (absolute current
   HP) — Section 5.1 and Section 11 below.
5. **Repeat** while the attack input is held, throttled and cadence-gated (Section 11).

### 9.1 Battle-controller state (role-named field map)

The controller carries a small, contiguous combat-state block. Offsets below are **object-relative
field offsets** (an interoperability fact, not a binary address), named by role:

| Controller field | Role |
|---|---|
| action mode | `0` = basic-attack mode (the executor picks the per-class default attack skill); non-zero = an explicit chosen skill. |
| swing-ready timestamp (ms) | The next-allowed-swing gate. The cooldown / can-act checks compare it against the millisecond clock (Section 11). |
| "now" snapshot (ms) | The millisecond clock value sampled at each cooldown check. |
| motion-lockout-end (ms) | Stamped to `now + 550 ms` on a real cast; a fixed post-cast motion hold (Section 11). |
| aim origin / aim target XYZ + sort | The cast origin and aim point written by the executor for the request. |
| effective skill range | The computed range² gate the executor tests target distance against. |
| "needs to move to target" flag, combo-step counter, combo-active flag | The approach / multi-hit combo bookkeeping for one action. |
| **picked target id** (`+136`) | The target id resolved from the mouse-pick at click time (the *per-controller* target — Section 10). |
| **picked target sort** (`+140`) | The sort byte (and a held flag) of that picked target. |
| last-action timestamp (ms) (`+144`) | The auto-repeat throttle clock (Section 11). |
| active-skill pointer | The skill object currently being used (the default basic-attack skill in mode `0`). |

Field offsets `+136` / `+140` are called out explicitly because they are the **per-controller picked
target {id, sort}** pair that Section 10 contrasts with the global hovered/selected id. `CODE-CONFIRMED`.

---

## 10. Target acquisition (two-tier) and the basic-attack send

### 10.1 Two-tier "current target"

Target acquisition is **two-tier** — two distinct notions of "current target" co-exist:

| Tier | Where it lives | What it drives |
|---|---|---|
| **Global hovered/selected target id** | A single process-wide 4-byte global holding the id of the actor under the cursor / most recently selected. Resolved via the global-id actor lookup (no sort). | Drives the **target-info tooltip / overhead name+level billboard**; is **the id the basic-attack send reads** as its attack target; is required by certain skill-usability validators; is cleared/rewritten on world (re-)entry. |
| **Per-controller picked target** | The battle controller's own `{id @+136, sort @+140}` pair (Section 9.1), set from the mouse-pick at click time and resolved each tick by the controller's get-target routine. | Drives the **executor's** target validation, range gate, and the request's target arrays for the full-skill path. |

The two are normally the same actor but are stored and resolved independently. A re-implementation
should keep them as **two separate fields** (a UI/selection target and a combat-action target) and not
collapse them. `CODE-CONFIRMED`.

The target tooltip / overhead billboard reads the target actor's **name** (actor name field, CP949
asciiz) and **level byte** to render the name+level label near the cursor; the overhead name colour is
by faction (see `specs/combat_overlay.md` if/when authored, and the floating-text findings in
Section 12). The selection itself is set by the world entity-pick on hover/click.

### 10.2 Basic melee **is** a skill — `2:52` with slot `0xFF`

There is **no separate plain-attack opcode.** The basic melee attack and every explicit skill share
the **same** client→server message: `2:52` `CmsgUseSkill` (`packets/2-52_use_skill.yaml`). The *only*
difference is the **skill-slot byte** in the header:

- A real hotbar skill sends its **hotbar slot index** (`0..239`).
- A basic attack — a skill **not on the hotbar** — sends slot **`0xFF`**.

The slot byte is produced by scanning the **240-slot skill hotbar** (8-byte stride) for the skill id;
a hit returns the slot index, a miss returns `0xFF` (the basic-attack sentinel). This is the same
240-slot hotbar and the same `0xFF = basic attack` convention `packets/2-52_use_skill.yaml` documents
(field `SkillSlot`, "0xFF = basic attack") and that `specs/skills.md` §4 ties to the 240 cooldown
slots. `CODE-CONFIRMED`.

> **The wire layout of `2:52` is owned by `packets/2-52_use_skill.yaml` and is not re-derived here.**
> That spec already records the 24-byte header (`SkillSlot @0` with `0xFF = basic attack`, `AimMode`,
> `AimScale`, `AimX`, `AimZ`, `CountA`, `CountB`) and the two trailing count-prefixed target-id arrays.
> The basic-attack form sends `CountA = CountB = 0` (no explicit target arrays); the attack target is
> taken from the **global hovered/selected id** (Section 10.1). The two thin basic-attack builders set
> the aim-mode field to `1.0`, raise the local "attack/cast in progress" flag, and send the header with
> empty target arrays.

### 10.3 The level gate on the basic-attack send

Before emitting the basic-attack `2:52`, the send path **gates on the target's level requirement**: it
reads the target actor's **level-requirement byte** and compares it against the **local player level**
(a process-wide global). If the target is too low (i.e. the local player out-levels the legal target by
the gated margin), the send is **aborted** and a "too low level" notice is shown (legacy localization
string ids ~57002 / 57003). So a basic attack on an over-low target never reaches the wire.
`CODE-CONFIRMED`; the exact margin semantics are PLAUSIBLE / CAPTURE-UNVERIFIED.

### 10.4 Click handler & the approach loop

The world mouse-click enters the controller's click handler with a click context that distinguishes a
**move/face** click from an **attack** click:

- It **self-throttles auto-repeat**: a held attack repeats no faster than every **100 ms** (Section 11).
- It **picks and validates** the actor under the cursor and resolves it into the per-controller
  `{id, sort}` target.
- It runs the **action executor** (the §9 gate chain). If the executor reports **out of range**, it
  issues a **move-to-target** request (`2:13`, a separate movement message carrying a facing angle plus
  the target X/Z) and plays a run motion, then retries the attack next tick — the click-to-attack →
  walk-into-range → attack loop.
- If the picked actor is an **NPC** with a dialog/shop role, the click routes to the NPC dialog/shop
  panel **instead of** attacking.

Target validity (the executor's target-state gate) rejects: a null target; a target flagged
disabled/in-use (e.g. gathering); a **REVIVE** skill aimed at a **living** target; a normal skill aimed
at a **dead** target; or a monster of an **untargetable/passive** template style. An actor is "alive"
iff its **alive flag** is non-zero; HP reaching 0 (from `5:53`, clamped to 0 — Section 5.1) is the
death condition. These are the same gates and the same alive-word semantics documented in
`specs/skills.md` §2.1 and §3.1. `CODE-CONFIRMED`.

---

## 11. Attack cadence & repeat throttle (server-paced)

Attack speed in the live game was **paced by the server**, not by a free client-side timer. The client
carries the timing *state* and *enforces* the gaps, but the controller's **swing-ready timestamp** is
the gate, and the strong inference is that the server arms / re-arms it (via the per-tick resync pulse
and/or the combat-phase update message, Section 5.1). Three independent throttles compose:

| Throttle | Value | Mechanism |
|---|---|---|
| **Per-skill cooldown / cadence** | `100 ms × skill_cadence` | `skill_cadence` is a halfword on the skill object (record-relative field `+1332`, the `cooldown`/cadence units; see `specs/skills.md` §1.4 / §4). The cooldown check blocks while `swing-ready-timestamp + 100 ms × skill_cadence > now`. The same `100 ms × skill_cadence` window is the minimum gap the executor enforces between swings. |
| **Post-cast motion lockout** | `550 ms` (fixed) | On a real cast the controller stamps `motion-lockout-end = now + 550 ms` — a fixed post-cast motion hold, independent of the per-skill cadence. Cleared when the cast ends (the `5:52` idle/cancel branch). |
| **Auto-repeat click throttle** | `100 ms` (fixed) | The click handler refuses a repeated attack faster than every 100 ms (compares `now` against the controller's last-action timestamp `+144`). A held attack therefore fires at most every 100 ms, then is further gated by the per-skill cadence and the swing-ready timestamp. |

Two further "can I act" gates compose with the cadence (these mirror `specs/skills.md` cast gates):

- **Can-act gate:** blocked while the swing-ready timestamp is still in the future **or** the actor's
  cast-lock field is set.
- **Can-move-or-act gate:** additionally blocked while the actor's tool/disable field is set, the
  cast-lock is set, or any of the **crowd-control status family** (the stun / root / silence / freeze
  buff-status ids — `specs/skills.md` §6 buff slots) is active.

**Server pacing — strong inference, CAPTURE-UNVERIFIED.** Whether the swing-ready timestamp is armed
purely client-side at send time, or **re-armed by a server packet** (the per-tick resync pulse `4:2`,
or the combat-phase update `4:100` `SmsgCombatAttackUpdate`, Section 5.2), governs whether attack speed
was *truly* server-paced. The control flow strongly implies server pacing (the controller arms the
swing window and the server's resync pulse releases the "attack in progress" flag), but **a capture is
required to settle the timestamp's owner.** This is the single most important open question in the
combat-loop model.

> The "attack/cast in progress" flag is set by the send paths and **cleared by the per-tick server
> resync pulse** (`4:2`) as well as by the `5:52` idle/cancel branch — i.e. `4:2` is the server's
> per-tick "action settled / resync" signal that releases the local attack flag. `CODE-CONFIRMED`;
> the wire layout of `4:2` is out of scope here.

---

## 12. Incoming results: animation, floating numbers, and the damage-kind table

This section documents the **client behavior** the result packets drive. It refines Section 5.1 (where
the values *land*) with *how they are rendered*. The wire layouts remain owned by
`packets/5-52_actor_skill_action.yaml` and the (un-specced) `5:53` push.

### 12.1 `5:52` `SmsgActorSkillAction` — swing, hit anim, hit records

On `5:52` the client looks up the caster and branches on a cast-active flag:

- **Idle / cancel** (flag 0): reset the caster to its default motion; for the local player, clear the
  "attack in progress" flag and reset the controller's swing-ready / motion-lockout fields.
- **Active cast** (flag non-zero), dispatched by an **action-code** byte (the same action-code map as
  `specs/skills.md` §5.1: motion sub-ops, single-target, and `0xCC` AoE fan-out):
  - Play the caster's **swing motion**.
  - For the **local player**, stamp the 550 ms motion lockout and drain the local player's own
    **stamina / HP for the cast** (the cast cost — see `specs/skills.md` §5.2 and open question 1).
  - Iterate the **per-target hit records** (bounded; `≤ 40`) and **forward each record to that actor's
    own animation / FX queue** (a per-actor anim+FX queue handle pair on the actor — described by role,
    object-relative fields `+1496` / `+1500`). This is the **per-target hit-animation / hit-FX driver**.
  - For the local player, **sum the visible damage** across records and **render a floating "damage
    dealt" number** (when the per-target combat-damage HUD toggle is on).
  - **AoE (action-code `0xCC`):** treat the first record as the origin and procedurally **fan
    sub-actors in a ring** around it (the visual multi-hit / clone effect of `specs/skills.md` §5.4).

The per-target hit record (36 bytes; the wire authority is `packets/5-52_actor_skill_action.yaml`)
carries, among its fields, the **visible damage**, the **remaining HP after the hit**, and the
**max HP** for that target. The HP-bar values come from here combined with `5:53`. `CODE-CONFIRMED`.

### 12.2 `5:53` — absolute current HP is the HP-bar source

`5:53` `SmsgActorVitalsAndPairState` (Section 5.1) writes the target actor's **absolute current HP**
into the actor's current-HP field (object-relative field `+176`; the HP-bar reads this field), along
with the secondary vital, stamina, and level/state bytes. For the **local player** the values are
**clamped against the client-computed maxima** (Section 5.1, Sections 1–4) and negatives floor to 0.
**This is the canonical "damage lands here" observable** — the HP bar's value is a new absolute HP
number from the server, never a client subtraction. `CODE-CONFIRMED`.

### 12.3 Damage-kind selection → floating-number motion & colour

The floating damage / heal numbers are **per-digit camera-facing billboards** spawned by a combat
**damage applier** at the moment a hit is applied. The applier chooses an **animation kind in the range
0..7**, and that kind selects both the **motion curve** (rise / spread / jitter) and the **colour** of
the number. The kind is a function of the **damage element class** and the **crit flag**:

| Element class | Crit / variant | Animation kind | Floating-number colour family |
|---|---|---|---|
| `0` (physical) | normal, non-skill | 0 | red (sub-discriminated by value sign: blue / pale-yellow) |
| `0` (physical) | normal, skill | 3 | bluish (larger, with jitter) |
| `2` | non-skill | 5 | white (crit-style size) |
| `2` | skill | 4 | gold |
| `3` | (heal / effect) | — | the heal/effect path (separate colour discriminator) |
| `5` | non-skill | 7 | green (crit-style size) |
| `5` | skill | 6 | green (rising; delayed SFX) |

So the four element classes the applier distinguishes are **`0`, `2`, `3`, `5`**, and the crit/skill
variant within each yields the **8 motion/colour kinds**. A self/HP-loss indicator uses a dedicated
single-quad kind (white, red on the crit/over flag). The **crit/PvP flag** is set when both attacker
and victim are players. The number rises and **alpha-fades over a ~1-second lifetime**. `CODE-CONFIRMED`
for the kind→colour mapping and the element-class set; the precise per-element gameplay meaning of
classes `2` and `3` and `5` (which is "magic", "heal", "ki", etc.) is **PLAUSIBLE / CAPTURE-UNVERIFIED**.

### 12.4 Multi-hit damage split

A single applied hit is **split into up to 7 multi-hit chunks**, each spawned as its own floating
number (a staggered multi-hit / combo display). The chunk count is derived from the skill descriptor's
hit-type: skill hit-type **1 or 5** yields **2 chunks**; otherwise up to **7**. Each chunk is delayed by
a fraction of the motion start so the numbers stagger. The MISS and over-low outcomes are rendered from
their own graphic / glyph (a dedicated "miss" texture and a special overflow glyph); the client
**receives** MISS / CRITICAL outcomes from the server and does not roll them locally (Section 1 note).
A "buff / damage-over-time" tick path also spawns floating numbers for DoT ticks (driven by the buff
push `5:31`; the DoT status slot is documented in `specs/skills.md` §6). `CODE-CONFIRMED`.

> **Where this lands for the presentation engineer.** The floating-number model (per-digit billboard at
> the target's head, glyph atlas, the 0..7 kind→colour table, ~1 s rise-and-fade), the overhead HP/MP
> minibar, and the faction-coloured name labels are presentation concerns; their full pixel/atlas
> detail belongs in a combat-overlay format/spec, not in this combat-math+flow document. This section
> records only the **model** (kind selection, multi-hit split, HP-bar source) that the domain / protocol
> layers must reproduce.
