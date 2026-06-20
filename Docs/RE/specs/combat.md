# Combat math & stat-aggregation pipeline (clean-room spec)

> **Verification banner.**
> - **verification:** client-side routing, message body sizes, struct/field offsets, and stat-weight
>   formulas are **confirmed** (control-flow-confirmed static read on build `263bd994`); single-inference
>   findings are marked **static-hypothesis**; server-authored magnitudes (damage numbers, cooldown
>   wall-clock pacing, XP/reward rates, HP scale) and on-wire VALUE meanings are **capture/debugger-pending**.
> - **re-verified against doida.exe IDB SHA 263bd994, CYCLE 7 (2026-06-20)** — Section 5 reclassified:
>   the `4/99` + `4/100` opcode bank previously specced here as `SmsgCombat*` is **NOT combat** — it is the
>   **Cube-Gamble minigame** result/spin panel (see §5 and the §5.2 redirect). The death-state field
>   convention and on-death cleanup are added (new §14). Real combat is server-authoritative via C2S `2/52`
>   (unchanged headline).
> - **ida_reverified:** 2026-06-20
> - **ida_anchor:** 263bd994
> - **evidence:** [static-ida] (no live network capture corroborates the in-world loop yet)
> - **conflicts:** (1) attack-in-progress flag clear — this build shows it cleared/re-armed in
>   `SmsgLocalPlayerStateSync` (**4/13**), not a `4/2` handler; which push *arms* vs *releases* the swing
>   window is **capture/debugger-pending**. (2) The §9.1 picked-target offsets re-pinned for `263bd994`:
>   picked target {id, sort} are at **controller+36 / +40** (the prior-build `+136/+140/+144` layout did
>   not reproduce); the 100 ms auto-repeat throttle lives on the **move-scheduler object (field +36)**, not
>   `controller+144`. The throttle **VALUE** (0x64 = 100 ms) and the walk-into-range loop are confirmed;
>   only the literal offsets moved.

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
| In-world combat loop: server-authoritative damage via a single battle controller (Sections 9–11) | HIGH (CODE-CONFIRMED static read on build 263bd994; CAPTURE-UNVERIFIED — see below) |
| Two-tier target acquisition; basic melee = skill `2/52` slot `0xFF`, default-attack id `121100050` (Sections 9–10) | HIGH (CODE-CONFIRMED) |
| Cadence constants: per-skill cadence = `100 ms × skill_cadence` (skill record `+1332`); post-cast motion lockout = `550 ms` fixed; `100 ms` auto-repeat throttle (Section 11) | HIGH — client constants CONFIRMED; whether the server re-paces them is CAPTURE/DEBUGGER-PENDING |
| Controller field offsets re-pinned to build 263bd994 (picked target id/sort at `+36/+40`; throttle on the move-scheduler `+36`, not `controller+144`) (Section 9.1) | HIGH for the 263bd994 offsets; the prior-build `+136/+140/+144` discrepancy is STATIC-HYPOTHESIS / names.yaml re-pin pending |
| Attack-in-progress flag clear opcode is `4/13` on this build (was `4/2`) (Section 11) | CODE-CONFIRMED that `4/13` clears the flag; which push arms-vs-releases the swing window is CAPTURE/DEBUGGER-PENDING |
| `4:99` + `4:100` are **NOT combat** — they are the **Cube-Gamble minigame** result/spin bank (Section 5) | HIGH (CYCLE 7 reclassification — ground-truth texture + 2/141 submit; see §5) |
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
| `5:52` | `SmsgActorSkillAction` | Carries the swing/hit animation header and per-target hit records (visible damage, remaining HP). This — together with `5:53` — is the actual combat-result path. See Sections 11–12. |

> **CYCLE 7 reclassification — the former `4/100` / `4/99` "combat" rows are removed.** An earlier draft
> of this spec listed `4:100` `SmsgCombatAttackUpdate` (188-byte body, "attack-phase" timing) and `4:99`
> `SmsgCombatResultMessage` (16-byte body, result codes `1..8` → message ids `~58001..58030`) as combat
> messages. **That attribution is wrong.** This entire opcode bank is the **Cube-Gamble minigame**
> (a wager / spinning-reel daily gambling panel), not combat:
> - The panel-init path loads the gamble UI atlas (`data/ui/cubegamble.dds` / `cubegamble_ani.dds`) into
>   every cell of its window panel — ground-truth confirmation that the bank drives a gamble panel.
> - The "result" path plays reel sounds, formats a money/wager quantity, diffs the wager against a
>   baseline, compares reel sums for win/lose, and enforces a **per-day bet-limit counter**; the
>   `~58001..58030` message ids and the bounded-random draws are **gamble outcome lines**, not damage
>   numbers.
> - The panel **submits via C2S `2/141`** (a char-status / gamble submit), not any combat opcode.
> - The handler routes its payload to the gamble panel object; only a *secondary* write touches the
>   local-player visual record (see §5.2).
>
> The opcode catalogue (`opcodes.md`) reclassification of `4/99` / `4/100` away from `SmsgCombat*` is
> owned by a separate lane and is not edited here. **For combat purposes, treat `4/99` and `4/100` as
> non-combat (Cube-Gamble).** Real combat is **server-authoritative** and uses the C2S `2/52` request
> (§10) and the `5/52` / `5/53` result pushes (§5.1, §11, §12). There is **no client-side
> damage/crit/defence apply path** — the client collects target ids, sends `2/52`, and applies inbound
> results; applied magnitudes are **RUNTIME-ONLY (server-resolved)**.

### 5.2 Attack-phase timing state — corrected (the "phase 3/5" state belongs to the gamble reel, not a combat swing)

An earlier draft attributed a "swing-start timestamp / value / phase" run on the local-player visual
record (main-window visual slot `[148]`) to a combat attack-phase update driven by `4/100`, with phase
value `3` = swing-start and phase value `5` = reset. **That timing machinery is the Cube-Gamble reel
update, not a combat swing-timing state.** The `4/100` handler is the gamble spin update; its *only*
combat-adjacent effect is a secondary write of timestamp fields onto the local-player visual record, but
those fields are driven by the **gamble** subsystem, not by a combat swing.

**Real combat cadence is owned by the battle controller, not by any `4/100` phase byte.** Attack
pacing is gated by the controller's swing-ready timestamp plus the fixed motion lockout and auto-repeat
throttle (Section 11), and the swing/hit animation is driven by the `5/52` `SmsgActorSkillAction` result
push (Section 12), not by a `4/100` phase indicator. The `items.csv` `attack_speed` coefficient (col75;
range ~0.26–0.37) is the per-weapon input the **server** uses to pace swings; the client carries no
attack-speed formula of its own. (Section 11 retains the cadence model; this subsection only removes the
mistaken `4/100`-phase-byte attribution.)

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

> **Verification banner for Sections 9–12** (ida_reverified 2026-06-16, ida_anchor `263bd994`,
> evidence [static-ida]). Everything in these sections is a **static read** of the legacy client (graded
> CODE-CONFIRMED where the control flow was followed end-to-end, STATIC-HYPOTHESIS / PLAUSIBLE where
> inferred). Client-side routing, body sizes, struct/field offsets, and timing constants are
> **confirmed**; **no live network capture corroborates the on-wire VALUE meanings or the server's
> pacing** — treat server-authored magnitudes and the swing-window owner as **CAPTURE/DEBUGGER-PENDING**
> until a capture confirms them. This is the same caveat as Sections 1–8 and as `specs/skills.md`. All
> object-relative field offsets in these sections are **re-pinned to build 263bd994** (Section 9.1).

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

The controller carries a small combat-state block. Offsets below are **object-relative field offsets**
(an interoperability fact, not a binary address), **re-pinned to build `263bd994`** and named by role.
Where an offset is also given as a dword index, the byte offset is `4 × index`.

| Controller field | Offset (build 263bd994) | Role |
|---|---|---|
| action mode | `+0` | `0` = basic-attack mode (the executor picks the per-class default attack skill, id `121100050`); non-zero = an explicit chosen skill. |
| swing-ready timestamp (ms) | `+4` (dword idx 1) | The next-allowed-swing gate. The cooldown / can-act checks compare it against the millisecond clock (Section 11). On a successful swing the executor re-arms it from the "now" snapshot (`+4 = +8`). |
| "now" / last-swing snapshot (ms) | `+8` (dword idx 2) | The millisecond clock value sampled at the cooldown check; the executor copies it into the swing-ready field to re-arm. |
| motion-lockout-end (ms) | `+12` (dword idx 3) | Stamped to `now + 550 ms` on a real cast; a fixed post-cast motion hold (Section 11). Zeroed on the `5:52` idle/cancel branch. |
| aim origin / aim target XYZ + sort | from `+36` | The cast origin and aim point written by the executor for the request; the per-controller picked target {id, sort} sits here too (see below). |
| effective skill range² | `+60` (dword idx 15, float) | The computed range² gate the executor tests target distance against; built per-skill from the skill record + bonuses. |
| "needs to move to target" flag | `+68` | Set when the picked target is out of range (drives the approach loop, Section 10.4). |
| combo-step counter / combo-active flag | `+69` / `+70` | The multi-hit combo bookkeeping for one action. |
| attack-in-progress flag | `+80` | Raised by the send paths; the held-attack tick runs only while this is `1`; cleared by the server resync (see §11 and the §-banner conflict). |
| **picked target id** | `+36` | The target id resolved from the mouse-pick at click time (the *per-controller* target — Section 10), read float-coerced by the executor. |
| **picked target sort** | `+40` | The sort byte (and held flag) of that picked target. |
| active-skill pointer | `+432` | The skill object currently being used (the default basic-attack skill, id `121100050`, in mode `0`). |

**Re-pin note (build 263bd994).** The picked-target {id, sort} pair is at **controller+36 / +40** on
this build — the prior-build `+136 / +140` layout **did not reproduce** here, and there is **no
`controller+144` last-action timestamp**: the 100 ms auto-repeat throttle is enforced on a **separate
move-scheduler object** (its field `+36`), not on the controller (Section 11). The throttle **value**
(`0x64` = 100 ms) and the walk-into-range loop are **confirmed**; only the literal offsets moved between
builds. `CODE-CONFIRMED` for the offsets above; the `+136/+140/+144` discrepancy is **static-hypothesis /
capture-pending** (flagged for a names.yaml re-pin against `263bd994`).

> **Two distinct timing objects — keep them separate.** The fields above all live on the **battle
> controller singleton**. The **`4:100` swing-start timestamp + value + phase** run (Section 5.2) lives on
> a **different object** — the local-player **visual/HUD record** (main-window slot `[148]`), at its own
> contiguous fields (swing-start ts, value, phase). A re-implementation must model these as **two
> separate objects**: the controller owns "what am I attacking and when may I swing again"; the
> visual/HUD record owns the `4:100`-driven swing-start display state.

---

## 10. Target acquisition (two-tier) and the basic-attack send

### 10.1 Two-tier "current target"

Target acquisition is **two-tier** — two distinct notions of "current target" co-exist:

| Tier | Where it lives | What it drives |
|---|---|---|
| **Global hovered/selected target id** | A single process-wide 4-byte global holding the id of the actor under the cursor / most recently selected. Resolved via the global-id actor lookup (no sort). | Drives the **target-info tooltip / overhead name+level billboard**; is **the id the basic-attack send reads** as its attack target; is required by certain skill-usability validators; is cleared/rewritten on world (re-)entry. |
| **Per-controller picked target** | The battle controller's own `{id @+36, sort @+40}` pair (Section 9.1, build 263bd994), set from the mouse-pick at click time and resolved each tick by the controller's get-target routine via the composite-key actor lookup. | Drives the **executor's** target validation, range gate, and the request's target arrays for the full-skill path. |

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
slots. The hotbar is **one 240-entry × 8-byte record array** — each record carries a skill **id**
(int32 at record `+0`) and a **points** field (int16 at record `+4`, followed by 2 pad bytes) — **not**
two parallel arrays. `CODE-CONFIRMED`.

In **basic-attack mode** (controller action-mode `0`) the executor, when no explicit skill is chosen,
resolves the default attack skill object by entity key **`121100050`** (the per-class default-attack
id) and uses it as the active skill (`+432`); the same `121100050` also appears as a fallback in a
cost-failure recovery path. `CODE-CONFIRMED`.

> **The wire layout of `2:52` is owned by `packets/2-52_use_skill.yaml` and is not re-derived here.**
> That spec already records the 24-byte header (`SkillSlot @0` with `0xFF = basic attack`, `AimMode`,
> `AimScale`, `AimX`, `AimZ`, `CountA`, `CountB`) and the two trailing count-prefixed target-id arrays.
> The basic-attack form sends `CountA = CountB = 0` (no explicit target arrays); the attack target is
> taken from the **global hovered/selected id** (Section 10.1). The two thin basic-attack builders set
> the aim-mode field to `1.0`, raise the local "attack/cast in progress" flag, and send the header with
> empty target arrays.

### 10.3 The region guard and the level gate on the basic-attack send

The send path first applies a **region/scene guard**: it compares the current scene/region state
against the expected world-state global, and if they differ it **bounces the send** (UI feedback only).
A basic attack therefore only fires while in the correct scene/region state. `CODE-CONFIRMED`.

Past the region guard, the send path **gates on the target's level requirement**: it reads the target
actor's **level-requirement byte** (the value is consulted only when it is non-zero and below 100) and
compares it against the **local player level** (a process-wide global). If the local level is `0`, a
"missing level" notice (~`57002`) is shown; if the local player **out-levels** the legal target, a "too
low level" notice (~`57003`) is shown; in either case the send is **aborted** and returns no packet. So
a basic attack on an over-low target never reaches the wire. `CODE-CONFIRMED`; the exact margin
semantics are PLAUSIBLE / CAPTURE-UNVERIFIED.

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
| **Auto-repeat click throttle** | `100 ms` (`0x64`, fixed) | The held-attack tick runs only while the controller's attack-in-progress flag (`+80`) is `1`; the **out-of-range move-to-target send** enforces a `now − scheduler[+36] < 100 ms` throttle on its own **move-scheduler object** (field `+36`). A held attack therefore re-issues at most every 100 ms, then is further gated by the per-skill cadence and the swing-ready timestamp. *(Re-pin, build 263bd994: this throttle is on the move-scheduler object, **not** at `controller+144`; the 100 ms value is **confirmed**, the offset moved between builds.)* |

Two further "can I act" gates compose with the cadence (these mirror `specs/skills.md` cast gates):

- **Can-act gate:** blocked while the swing-ready timestamp is still in the future **or** the actor's
  cast-lock field is set.
- **Can-move-or-act gate:** additionally blocked while the actor's tool/disable field is set, the
  cast-lock is set, or any of the **crowd-control status family** (the stun / root / silence / freeze
  buff-status ids — `specs/skills.md` §6 buff slots) is active.

**Server pacing — strong inference, CAPTURE/DEBUGGER-PENDING.** Whether the swing-ready timestamp is
armed purely client-side at send time, or **re-armed by a server packet** (a per-tick resync pulse,
and/or the `5/52` `SmsgActorSkillAction` result push and the `4/13` state-sync), governs whether attack
speed was *truly* server-paced. *(Note: an earlier draft floated `4/100` `SmsgCombatAttackUpdate` as a
candidate re-arm; that opcode is the Cube-Gamble spin update, not combat — see §5.2 — so it is **not** a
candidate here.)* The control flow strongly implies server pacing (the controller arms the swing window
and a server resync push releases the "attack in progress" flag), but **a capture is required to settle
the timestamp's owner.** This is the single most important open question in the combat-loop model.

> **Attack-in-progress flag clear — opcode is `4/13` on this build (was documented as `4/2`).** The
> "attack/cast in progress" flag (controller `+80`) is set by the send paths and **cleared / re-armed
> by the local-player state-sync push `4/13`** `SmsgLocalPlayerStateSync` (which manipulates the
> controller's `+68/+69/+70/+80` flags) as well as by the `5:52` idle/cancel branch. On build
> `263bd994` **no `4/2` handler touching the controller flag was located** — the prior `4/2` attribution
> is superseded here by `4/13`. It remains **capture/debugger-pending** whether `4/2` also exists as a
> bare per-tick tick and which push *arms* vs *releases* the swing window live; the wire layout of the
> resync push is out of scope here.

### 11.1 Executor outcome codes and the cadence gate (CONFIRMED, static)

The action executor returns a small **reason code** describing why the action did or did not fire:
`0` = sent; a non-zero value in the range **`1..21`** is the reason it was rejected (target / skill /
level / range / cooldown / state failures — the same gate family as §10.4 and `specs/skills.md` §2.1).
The **cadence gate** rejects a queued action when

```
(now − last-send) < 100 ms × skill_cadence        // skill_cadence = skill record field +1332
```

On a successful send the timer is **rebased** (`last-send = now`), so the minimum inter-action interval
is `100 ms × skill_cadence`. The basic-attack chain takes a fast path that fires immediately (no full
cadence gate). These are the same `100 ms × skill_cadence` constant and swing-ready re-arm already in
the Section 11 table; this subsection records the **reason-code range (`1..21`)** and the rebase rule.

**Combo model.** A skill record carries a short combo chain; the combo length is the **count of
non-zero combo entries** on the record and is capped at **≤ 5 steps**. A per-action combo-step counter
walks `0..length`, and the current step index is written into the outbound `2/52` request's
**combo / hit byte** (the second header byte). The combo-active bookkeeping lives on the controller
(`+69` / `+70`, §9.1). `CODE-CONFIRMED`.

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
    **stamina / HP for the cast** (the cast cost): the **HP cost** comes from the skill record field
    `+1368` (subtracted from the player current-HP field `+176`), and the **stamina cost** from the
    skill record field `+1370` scaled by the target count and clamped, applied to the player stamina
    field `+184`. See `specs/skills.md` §5.2 and open question 1. *(The HP/stamina **magnitudes** are
    server-authored skill data — capture/debugger-pending; the field plumbing is confirmed static.)*
  - Iterate the **per-target hit records** (bounded; `≤ 40`) and **forward each record to that actor's
    own animation / FX queue** (a per-actor anim+FX queue handle pair on the actor — described by role,
    object-relative fields `+1496` / `+1500`). This is the **per-target hit-animation / hit-FX driver**.
  - For the local player, **sum the visible damage** across records (the per-record visible-damage
    field, record `+16`, accumulated into a 64-bit total) and **render a floating "damage dealt"
    number** via the localized **damage-total notice, message-string id `2212`** (thousands-grouped,
    formatted, broadcast as a chat/notice) — gated on the per-target combat-damage HUD toggle. *(Note:
    the raw immediate `2212` also appears elsewhere in the combat panel as an object-relative **field
    offset** `+2212`; that is numerically coincidental and unrelated to the message id used here.)*
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
number from the server, never a client subtraction. The current-HP field `+176` is `CODE-CONFIRMED`
(the `5:52` cast-cost path writes it and the per-frame tick floors it at 0); the `5:53` body itself was
not re-opened on this lane (it is owned by the vitals spec), so the `5:53`→`+176` write is
**static-hypothesis** pending a vitals-lane re-read.

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

---

## 13. Local-player vitals delivery to the HUD

> **Verification banner for Section 13** (ida_reverified 2026-06-17, ida_anchor `263bd994`,
> evidence [static-ida]). This section is a **static read** of the in-game local-player vitals data
> path (how server-pushed HP / MP / stamina reach the on-screen gauges). Client-side routing, the
> write topology, the poll-per-frame read model, and the lock-step mirror are **CONFIRMED** (control
> flow followed end-to-end); the exact major/minor of a *standalone* pure-absolute-vitals push and one
> max-table index question are flagged **debugger-pending** below. No live capture corroborates the
> on-wire VALUE meanings yet. This extends -- it does not rewrite -- the server-authoritative,
> clamp-against-local-max, `4/13`-attack-flag-clear findings already in the status header and
> Sections 5 / 11 / 12.

This section adds the **precise delivery paths** for the *local player's* current HP / MP / stamina /
level to the HUD gauges. The settled findings above (incoming HP arrives as **absolute** wire values;
the client clamps current against a **locally-computed** maximum; the attack-flag clear is `4/13`)
are unchanged; here we document *which messages move vitals*, *where they are stored*, and *how the
gauge reads them*.

### 13.1 The vitals data path (prose)

```
        (server pushes absolute or delta vitals on the wire)
                              |
   +-------------+-----------+-----------+-------------+----------------+
   |             |           |           |             |                |
 5/52         5/32        4/13        4/1          item / potion    (no wire opcode)
(delta)      (absolute)  (absolute)  (world seed)   self-use          per-frame clamp
   |             |           |           |          (delta+clamp)          |
   v             v           v           v               v                 |
   +---------> core vitals writer <------+               |                 |
   |     - DELTA mode: apply signed HP/MP/stamina deltas; spawn floating    |
   |                   damage numbers                                       |
   |     - ABSOLUTE mode: set HP / MP / stamina outright                    |
   |                                                                        |
   |  writes, in LOCK-STEP, BOTH:                                           |
   |   (a) the local-player Actor vitals fields (structs/actor.md), AND     |
   |   (b) the dedicated HUD vitals globals:                                |
   |        - a packed current-HP | current-MP pair                         |
   |        - a current-stamina value                                       |
   |        - a level value                                                 |
   v                                                                        |
  HUD gauges POLL the globals every frame (13.4) <------------------------- |
                                                                            |
  Per-frame ActorManager tick: clamp local current HP/MP/stamina DOWN to <--+
  the COMPUTED max (VitalFormula, structs/stats.md); zero-floor negatives.
  THIS per-frame clamp IS the only "per-tick resync" -- there is NO
  4/2-class periodic vitals wire push.
```

Key structural facts (all **CONFIRMED** static unless tagged otherwise):

- A **single core vitals writer** is the funnel for every local-player vitals change. It runs in two
  modes: a **delta mode** (apply signed HP / MP / stamina deltas, and spawn the floating damage
  numbers) and an **absolute mode** (set HP / MP / stamina outright). The wire messages below select
  the mode.
- Every path writes the Actor vitals fields and a **dedicated set of HUD globals in lock-step** -- a
  **packed current-HP | current-MP pair**, a **current-stamina** value, and a **level** value. The HUD
  never reads the Actor struct directly for the gauge; it reads these globals.
- **Max HP / max MP / max stamina are COMPUTED on demand** via the `VitalFormula`
  (`structs/stats.md`) and are **never stored** and **never wire fields** -- only the *current* values
  and the *level* travel and are stored.
- The **per-frame ActorManager tick** clamps the local current values **down** to the computed max and
  zero-floors negatives. **This client-side per-frame clamp is the only "per-tick resync"** -- it
  reconciles the latest absolute/delta push against the locally recomputed cap each frame. There is
  **no separate `4/2`-class periodic vitals push** on the wire on this build (this agrees with the
  status-header / Section 11 correction that the attack-flag path is `4/13`, not `4/2`).

### 13.2 Which S2C messages move local-player vitals

| Tuple | Name | Vitals fields carried | Mode | Confidence |
|---|---|---|---|---|
| `5/52` | `SmsgActorSkillAction` | per-target **signed** current-HP / current-MP / current-stamina **delta** in each hit record; spawns floating damage numbers | **Delta** | CONFIRMED (control flow followed: walks the per-target hit records, applies each via the core vitals writer in delta mode; see Section 12.1) |
| `5/32` | `SmsgLevelUp` | **absolute** re-seed of HP + MP (the packed pair), stamina, and **level** (plus next-level XP) on level-up | **Absolute** | CONFIRMED (writes the Actor vitals fields and mirrors all of the HUD globals for the local player) |
| `4/13` | `SmsgLocalPlayerStateSync` | **absolute** HP + MP (packed) + stamina re-sync delivered alongside the movement / state update (the vitals write is skipped on one state value) | **Absolute** | CONFIRMED (mirrors the packed HP/MP global and the stamina global). Also the **attack-flag clear** path already documented in Section 11. |
| `4/1` | `SmsgGameStateTick` | **absolute** world-**entry** seed of vitals + level + the XP pair, taken from the world-state block -- **only** on the world-entry form | **Absolute (entry seed only)** | CONFIRMED (this is a one-time entry seed, **NOT** a periodic per-tick vitals push) |
| (item / potion self-use) | item self-use vitals delta (working name) | adds a current HP / MP / stamina **delta**, then **clamps to the computed max** and refreshes the HUD | **Delta + clamp** | CONFIRMED (drives the core writer's delta path on a local self-use action) |
| (client-side, no wire opcode) | ActorManager per-frame clamp | clamps local current HP / MP / stamina **down** to the computed max every frame; zero-floors negatives | client-side resync | CONFIRMED -- **this IS the "per-tick resync"**; there is no wire opcode behind it |

**The standalone absolute-vitals push (debugger-pending).** The *absolute set* of current HP / MP /
stamina is realized by the **absolute-mode tail of the core vitals writer**, reached by `5/32`, `4/13`,
and `4/1` above. A **standalone opcode that ONLY pushes absolute HP / MP / stamina** (i.e. a pure
vitals re-sync, candidate `5/53` `SmsgActorVitalsAndPairState` -- see Section 5.1 / Section 12.2) was
**not isolated as its own distinct vitals handler on this lane** this pass: the vitals dispatch is
partly runtime-table-dispatched, so static could not pin whether the pure absolute-vitals re-sync is a
separate `5/53` push or rides one of `5/32` / `4/13` / `4/1`. **debugger-pending:** breakpoint the core
vitals writer and the HUD-globals store, take a real damage / heal hit, and read the framed header
`[u32 size][u16 major][u16 minor]` to pin its exact major / minor (and whether it exists separately or
rides the above). `5/53`'s general role as the absolute-vitals carrier is already recorded in
Section 5.1 and Section 12.2; this open item is only about confirming a *pure-vitals* re-sync form.

### 13.3 Field semantics the Vitals HUD channel needs

The local-player vitals live in **two mirrored places written in lock-step**: the local-player Actor
vitals fields (offsets owned by `structs/actor.md`) and a dedicated set of **HUD vitals globals**. The
HUD gauge **reads the globals** and **polls them per frame** (13.4). What is stored vs. computed:

| Quantity | Stored or computed | Type | Where the HUD reads it | Confidence |
|---|---|---|---|---|
| current HP | **stored** (low half of the packed pair) | u32 | HUD packed current-HP \| current-MP global | CONFIRMED |
| current MP | **stored** (high half of the packed pair) | u32 | same packed global | CONFIRMED |
| current stamina | **stored** | i32 (zero-floored) | HUD current-stamina global | CONFIRMED |
| level | **stored** | u16 | HUD level global (also keys the max-value cache) | CONFIRMED |
| max HP | **computed on demand**, never stored, never on the wire | u32 | `VitalFormula` (`structs/stats.md`) | CONFIRMED (the getter implements the `structs/stats.md` formula) |
| max MP | **computed on demand**, never stored, never on the wire | u32 | `VitalFormula` (`structs/stats.md`) | CONFIRMED |
| max stamina | **computed on demand**, never stored, never on the wire | i32 | the max-stamina getter (`structs/stats.md`) | CONFIRMED |
| (third / secondary bar) | stored; in-game meaning open | i32 | a secondary vital global | MEDIUM (storage confirmed; meaning -- "rage" / overcharge / secondary stamina -- capture-pending) |

- **Lock-step rule:** the Actor vitals fields and the HUD globals are written together by every vitals
  path. A re-implementation may treat the HUD globals as the canonical *display* source and the Actor
  fields as the *simulation* mirror, but both must move on the same write. Actor-side offsets are owned
  by `structs/actor.md`; the gauge geometry that reads the globals is owned by `ui_hud_layout.md` Section 5.6.
- **Packed HP|MP:** current HP and current MP are carried and stored as **one packed pair** (HP in the
  low half, MP in the high half) on most paths; modelling them as a packed pair avoids carry bugs and
  matches the binary.
- **Soft divergence (debugger-pending).** The max-HP class table is indexed by **class id** per
  `structs/stats.md`, but the getter was observed indexing it by the **level value** (the level HUD
  global) on this build. Whether the table is keyed by class id or by level is **not resolved here** --
  it needs a known-class / known-level read to settle. Do not silently collapse the two; carry the
  question to the debugger.

### 13.4 Gauge read model (poll per frame)

The HUD vital readers **poll the globals every frame** and recompute the fill from the live values;
there is no per-change event that pushes a value into the gauge. The apply paths fire an on-change HUD
**refresh nudge** (to rebuild label text / reset animation timers), but the fill geometry itself is
recomputed each draw from the live globals against the on-demand max getters. The fill law is the plain
ratio `fill = current / max`, clamped to `[0, 1]`, with HP and MP each filling independently from the
two halves of the packed pair and stamina from its own global. The exact gauge rects and the
right-edge composite geometry are owned by `ui_hud_layout.md` Section 5.6; this spec records only the
**data model** (poll-per-frame, `current / max`, current stored vs. max computed) the gauge consumes.

**Cross-references for Section 13:** `structs/actor.md` (the actor-side vitals field offsets the writer
mirrors); `structs/stats.md` (the `VitalFormula` that computes max HP / max MP / max stamina on
demand); `ui_hud_layout.md` Section 5.6 (the gauge that polls the HUD vitals globals); Sections 5.1 /
12.2 (the `5/53` absolute-vitals carrier and the HP-bar-as-absolute observable); Section 11 (the `4/13`
attack-flag clear, unchanged).

---

## 14. Death-state field convention & on-death cleanup (CYCLE 7)

> **Verification banner for Section 14** (ida_reverified 2026-06-20, ida_anchor `263bd994`,
> evidence [static-ida]). The death-state field offsets, the on-death cleanup set, and the deathCause
> branch selector are **CONFIRMED** (control flow followed end-to-end). The **applied magnitudes** of
> the death penalty (XP loss, durability loss, dropped items) are **RUNTIME-ONLY / server-authoritative**
> — the client formats notice *text* only, never a penalty number. The **respawn flow** (request /
> confirm opcodes, the respawn modal, the countdown engine, ground-item lifecycle) is owned by
> `specs/world_systems.md`; this section records only the **on-actor death-state convention** that the
> combat / domain model needs, and cross-refs `world_systems.md` for the rest.

When an actor dies, its death state is recorded on the actor object itself. These are
**object-relative field offsets** (interoperability layout facts, not binary addresses):

| Actor field | Offset | Role | Death value | Alive / normal value |
|---|---|---|---|---|
| alive / active gate | `+1424` | The generic "alive & interactable" guard. Targeting, movement, and pickup paths all early-return when it is `0`. | `0` = dead | `1` = alive |
| action / motion-state enum | `+1420` | The actor's current action-state. `8` is the canonical "is this actor dead" test used elsewhere. | `8` = death / knockdown | `1` = idle / normal |
| death timestamp (ms) | `+1480` | Millisecond clock value stamped at the moment of death. | set at death | — |

The death-motion routine sets all three (alive gate `→ 0`, action-state `→ 8`, timestamp `→ now`) and
plays the death animation + death SFX; a mounted / paired entity recurses so it also plays death. The
spawn / respawn / world-entry paths reset the alive gate and action-state back to `1` / `1` when an
actor (re)enters the world alive.

**On-death cleanup (CONFIRMED).** When the death handler processes a dying actor it:
- clears that actor's **30-entry buff array** (12-byte slot stride, the per-actor buff table — removes
  timed buffs on death; see `specs/skills.md` §6 / the buffs spec),
- clears the battle controller's **current target** if the dying actor was the picked target,
- zeroes the actor's **combat resources** (the two combat-resource fields at actor `+1256` / `+1260`).

For the **local player** specifically, the same handler additionally zeroes the local resource mirror,
opens the death / respawn modal, and sets the battle controller's **combat sub-mode field (`controller +16`)
to `2`** ("dead"). **Local death does NOT leave the world** — it is distinct from a world-exit; the actor
stays in the scene with the dead state set and the respawn modal shown.

**deathCause selector (CONFIRMED).** The inbound death message carries a cause value that selects the
death presentation:

| deathCause | Meaning | Effect |
|---|---|---|
| `0` | normal death | standard death notice + town-respawn-capable modal |
| `1` | PK death (type A) | PK death visual / SFX + "killed by" notice |
| `2` | PK death (type B) | alternate PK death visual |
| `3` | special / no-modal | restricted respawn modal (no town-respawn option), short one-shot countdown |

**Death penalty = text only, magnitudes RUNTIME-ONLY.** The client formats death / "killed-by" notice
strings (localized CP949 message-db ids) but **computes no XP-loss number, no durability-loss number,
and no item-drop list** — there is no client-side death-penalty arithmetic. Any item loss arrives as
ordinary server-driven ground-item spawns / inventory updates. **XP loss, durability loss, and dropped
items are RUNTIME-ONLY / server-authoritative.**

**Cross-references for Section 14:** `specs/world_systems.md` (the full respawn flow — respawn request
C2S `2/3`, the respawn modal, the death-countdown timer engine, and the ground-item lifecycle; and the
death-detect push S2C `5/10` and respawn responses S2C `4/28` / `5/28` and vitals push `5/53`);
`structs/actor.md` (the actor-side field offsets); `specs/skills.md` §6 (the per-actor buff table that
is cleared on death). The death-detection and respawn **opcodes** themselves are reconciled in
`opcodes.md` by a separate lane.
