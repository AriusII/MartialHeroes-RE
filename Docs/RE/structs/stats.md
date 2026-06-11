# Vital stats — max HP / max MP formula (clean-room spec)

Neutral, data-only model of how the legacy client derives a character's **maximum HP** and
**maximum MP** from primary stats, equipment, and active auras. Promoted from a dirty-room note;
rewritten, no decompiler identifiers, no binary addresses. This document is the design input for
the **domain engineer** (the deterministic vitals formula in `Client.Domain`).

> **What is pinned vs. what needs data.** The stat weights, the per-class HP growth table, the
> aura rules, the floor/truncation order, and the equipment-skip rule are all recovered as concrete
> data and are implementable now. Two inputs — the **level base** and the **server base** — are
> externally supplied (they come from server messages / a catalog the legacy client received at
> runtime). They are flagged UNVERIFIED throughout; without a data file the formula is correct in
> structure but cannot produce the same absolute numbers the live game did.

Key facts the domain engineer must carry over from `actor.md`:

- `max_hp` and `max_mp` are **not** wire fields and are **not** stored on the actor. They are
  computed on demand from base stats + equipment + auras, for the **local player only**. Remote
  actors carry only `current_hp` / `current_mp` as sent by the server (the server enforces the cap).
- The formula has **no direct level input**. Level affects the result only through the
  externally-supplied "level base" term (below), which the legacy client updated when a level-up
  message arrived.

---

## Inputs

### Primary stats (effective values)

The formula consumes the five **effective** primary stats — the final value of each stat after
base-from-level, equipment flat bonuses, set bonuses, and buff/debuff contributions are folded in:

- `STR`, `DEX`, `AGI`, `INT`, `CON` — each an integer.

How each effective stat is assembled (recovered, for completeness; the domain model may treat the
five effective stats as already-resolved inputs):

```
effective_stat = base_from_level_and_server      (externally supplied; see "External inputs")
               + equipment_flat_bonus            (sum over worn items; see below)
               + set_bonus                       (all-or-nothing; see below)
               + buff_debuff_value               (a shared generic buff term applied to every stat)
               + flat_global_addend              (per-stat global; source unverified, often 0)
```

The **equipment flat bonus** for a stat is the sum, over every worn item, of that item's
stat-grant field (see `item.md`, "Item actor stat-grant fields"). **Equipment slot index 8 is
skipped** in every accumulation — it is a non-stat-contributing slot (mount / pet / special-effect
slot). This skip rule applies to all five stats and to the HP and MP flat bonuses below.

### External inputs (UNVERIFIED — require catalog/server data)

These two terms per quantity are written by the legacy client when it processed level-up and
server-state messages. They are **not** derivable from the binary alone and must be supplied by the
re-implementation's server/catalog layer:

| Term            | Applies to            | Source (legacy)                              |
|-----------------|-----------------------|----------------------------------------------|
| `level_base`    | HP, MP, and each stat | written on level-up; scales with level        |
| `server_base`   | HP, MP, and each stat | server-overridden base value                  |

Until a data file pins these, treat them as `0` (structurally correct, numerically incomplete) and
flag any computed max as **provisional**.

---

## Three-stage pipeline (shared by HP and MP)

Both maxima follow the same shape:

```
Stage 1 — stat-weighted score (floating point):
    score = STR*w_str + DEX*w_dex + AGI*w_agi + CON*w_con + INT*w_int + 30.0

Stage 2 — flat base (integer):
    base = floor(score)
         + equipment_flat_bonus          (sum over worn items, slot 8 skipped)
         + set_bonus                     (all-or-nothing)
         + level_base                    (UNVERIFIED external input)
         + server_base                   (UNVERIFIED external input)

Stage 3 — percentage multiplier, then truncate:
    max = floor(base * pct_mult)
```

`floor` is truncation toward zero of a non-negative quantity (the legacy code uses a
float-to-integer truncation at both the Stage 2 and Stage 3 boundaries — apply `floor` exactly at
those two points, not anywhere else, to reproduce rounding behaviour).

---

## Max HP

### Stage 1 — stat weights

```
score_hp = STR*2.2 + DEX*2.5 + AGI*2.4 + CON*1.5 + INT*1.6 + 30.0
```

| Stat   | Weight | Exact 32-bit-literal value (if bit-exact reproduction matters) |
|--------|--------|----------------------------------------------------------------|
| STR    | 2.2    | 2.2000000476837158 |
| DEX    | 2.5    | 2.5 |
| AGI    | 2.4    | 2.4000000953674316 |
| CON    | 1.5    | 1.5 |
| INT    | 1.6    | 1.6000000238418579 |
| (const)| +30.0  | 30.0 |

The weights originate as single-precision (`f32`) literals and are accumulated in double/extended
precision. For bit-exact parity, store the weights as `float` and widen to `double` for the
accumulation; for a faithful-but-clean re-implementation, the decimal values above are sufficient.

### Stage 2 — HP flat base

```
base_hp = floor(score_hp)
        + equip_hp_flat_bonus            (sum of each worn item's HP-grant field, slot 8 skipped)
        + set_bonus_hp                   (all-or-nothing set bonus)
        + level_base_hp                  (UNVERIFIED external input)
        + server_base_hp                 (UNVERIFIED external input)
        + equip_slot_hp                  (two extra equipment HP bonus slots; see note)
```

`equip_slot_hp` is an additional flat contribution drawn from the actor's stat-slot system: two
distinct HP-bonus slots are each read twice (once via the linear-scan slot table, once via the
direct-index slot table) and summed. Treat this as an extra flat HP addend whose value is
data-driven by the equipped gear. Mark **UNVERIFIED** which gear populates these slots.

### Stage 3 — class multiplier + aura terms

```
pct_mult_hp = CLASS_HP_TABLE[class_id]
            + (slot81_value / 100.0)          (optional %HP buff slot; 0 if absent)
            + sum over active auras of (aura_value / 100.0)   (HP auras only; see below)

max_hp = floor(base_hp * pct_mult_hp)
```

**Per-class HP growth table** (indexed by the local player's class id, a single byte):

| Class id | Multiplier | Note |
|----------|-----------|------|
| 0        | 0.0       | Sentinel / "no class assigned". Index-0 yields zero max HP; treat as out-of-range guard, not a real class. UNVERIFIED. |
| 1        | 0.3       | |
| 2        | 0.2       | |
| 3        | 0.15      | |
| 4        | 0.1       | |

The table has 5 entries (indices 0–4), implying at most 4 in-game classes plus the sentinel.
**UNVERIFIED:** whether the class-id byte maps directly onto this domain class enum or is offset by
one; confirm against a capture before binding class ids to the table.

**Aura terms (HP):** the client tracks up to two active aura actors (a companion and a secondary
buff source). Each contributes a percentage **only if its buff-kind discriminator marks it as an HP
aura** (recovered kind value: HP aura = `1`). Per qualifying aura:
`pct_mult_hp += aura_percent_value / 100.0`. If no aura slots are active, the aura contribution is 0.

---

## Max MP

### Stage 1 — stat weights

```
score_mp = STR*1.4 + DEX*1.5 + AGI*1.7 + CON*1.5 + INT*3.5 + 30.0
```

| Stat   | Weight | Exact 32-bit-literal value |
|--------|--------|----------------------------|
| STR    | 1.4    | 1.399999976158142 |
| DEX    | 1.5    | 1.5 |
| AGI    | 1.7    | 1.700000047683716 |
| CON    | 1.5    | 1.5 |
| INT    | 3.5    | 3.5 |
| (const)| +30.0  | 30.0 |

(INT is the dominant MP driver, as expected; class differentiation for MP comes entirely from each
class's stat distribution, not from a multiplier table — see Stage 3.)

### Stage 2 — MP flat base

```
base_mp = floor(score_mp)
        + equip_mp_flat_bonus            (sum of each worn item's MP-grant field, slot 8 skipped)
        + set_bonus_mp                   (all-or-nothing set bonus)
        + level_base_mp                  (UNVERIFIED external input)
        + server_base_mp                 (UNVERIFIED external input)
        + equip_slot_mp                  (two extra equipment MP bonus slots; same pattern as HP)
```

### Stage 3 — aura terms only (no class table)

The MP formula has **no per-class multiplier table**. The multiplier starts at `1.0` (100%) and
only auras adjust it:

```
pct_mult_mp = 1.0
            + sum over active auras of (aura_value / 100.0)   (MP auras only)

max_mp = floor(base_mp * pct_mult_mp)
```

**Aura terms (MP):** same two aura slots as HP, but a qualifying aura must mark its buff-kind
discriminator as an MP aura (recovered kind value: MP aura = `2`). Per qualifying aura:
`pct_mult_mp += aura_percent_value / 100.0`.

---

## The set-bonus rule (all-or-nothing)

A set bonus is added in Stage 2 (to HP, MP, and each stat) **only when every piece of the set is
currently worn**. A partial set grants only the individual per-piece bonuses, never the set bonus.
The set-bonus accumulator matches worn items by a set-type id present on each item actor (see
`item.md`). The exact per-stat set-bonus magnitudes are data on the item actors and are not
hard-coded here — they come from item definitions.

---

## Implementation guidance for the domain engineer

1. Model the formula as a pure function of: five effective stats, the per-quantity flat bonuses
   (equipment sum, set bonus, the two extra equip slots), the two external bases (level / server),
   the class id, the optional %HP buff slot, and the active aura list. Keep it deterministic and
   engine-free.
2. Apply `floor` (truncate toward zero) at exactly two points: end of Stage 2 (`floor(score)`),
   and end of Stage 3 (`floor(base * pct_mult)`). Do not round elsewhere.
3. The class id `0` row is a guard, not a class — guard against out-of-range ids rather than
   returning a zero max in normal play.
4. **Pinned and implementable now:** the stat weights, the +30.0 constants, the class HP table, the
   `/100.0` aura math, the HP-aura-kind = 1 / MP-aura-kind = 2 discriminators, the slot-8 skip, and
   the floor/truncation order.
5. **Blocked on external data (mark provisional until supplied):** `level_base` and `server_base`
   for HP, MP, and each stat; the magnitudes of the two extra equip HP/MP slots; per-stat set-bonus
   values; and the class-id → class-enum mapping.
6. Compute max lazily (when the UI or a cap-check needs it); the legacy client did not cache it.
</content>
</invoke>
