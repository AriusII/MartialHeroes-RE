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

The basic **melee-attack** request is a sibling client→server action under the same C2S action major
(major 2) as the skill request, but its exact minor was **not pinned** (UNVERIFIED #7). In all cases
the client sends intent; the server computes and returns the result.

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
7. The basic melee-attack C2S request (the action minor distinct from `2:52` skill use) was not
   pinned.
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
