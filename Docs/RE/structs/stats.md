# Vital stats — max HP / max MP formula (clean-room spec)

> **Verification banner.**
> - **confirmed** — that this is a behavioral *formula* spec (not a layout table) and that the
>   "stat-slot table is a **separate external global**, not an Actor field" is consistent with the
>   IDB: the spawn factory never indexes a stat-slot array inside the Actor; the only inline gear
>   array is the 20×16 equipment-id table at Actor +0xCC (see `actor.md` / `spawn_descriptor.md`).
> - **static-residual** — the numeric constants (HP weights 2.2/2.5/2.4/1.5/1.6; MP weights
>   1.4/1.5/1.7/1.5/3.5; the +30.0 constants; the per-class HP table 0.3/0.2/0.15/0.1) are loaded
>   from the read-only float pool (`fld` from `.rdata`, **not** inline immediates — a scan for the
>   2.2f bit pattern returned no hits). They were carried from the prior pass and were **not
>   re-witnessed** this pass; the doc is **not refuted**, but treat the exact constants as
>   static-residual until the formula function's float pool is re-read.
> - **capture/debugger-pending** — the two external inputs (`level_base`, `server_base`) are
>   server-supplied and inherently capture-pending; the class-id → class-enum mapping; and the
>   gear that populates the two extra HP/MP equip slots.
> - **ida_reverified:** 2026-06-16; re-verified against doida.exe IDB SHA 263bd994, CYCLE 7 (2026-06-20)
>   **ida_anchor:** 263bd994  **evidence:** [static-ida]
> - **conflicts:** none raised against this doc this pass. CYCLE 7 added the "Stat-curve table family"
>   section below (on-disk record layouts for users/userlevel/userpoint/exp.scr + the in-memory scaling
>   grid). It does not change the max-HP/MP formula above; it documents where the per-level scaling
>   coefficients come from and confirms that **base HP/MP magnitudes are absent from these tables**
>   (server-supplied), reinforcing the formula's `level_base`/`server_base` external-input flags.

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

---

## Note — two distinct "stats" structures (do not conflate)

This formula consumes the **player stat-slot table** (slot id → value), which is a separate external
global, **not** an Actor field and **not** the char-select stats record. Keep these three apart:

- **Stat-slot table** (this doc + `actor.md` "Stat-slot table") — the equipment/buff slot system
  (12-byte entries, slot ids 7 / 2,3,9 / 70–74 / 81 / 93 / …) the max-HP/MP and primary-stat
  formulas read. A separate keyed collection owned by the player/stat subsystem. **confirmed-as-external.**
- **96-byte char-select stats record** — a per-slot **0x60-byte** block read on the wire right after
  each character's 880-byte descriptor in the 3/1 CharacterList / 3/4 SceneEntityUpdate per-slot
  record (see `spawn_descriptor.md` "Wire framing"). It is confirmed to exist and be 0x60 bytes, but
  its **interior fields are unmapped** (the char-select display path reads only descriptor fields, not
  this block) → **capture-pending**. It is most likely the detailed stat sheet shown on a
  character-info sub-panel; it is **not** the formula's stat-slot table.
- **Effective primary stats** — the resolved STR/DEX/AGI/INT/CON the formula's Stage 1 consumes,
  assembled from base + equipment + set + buff (see "Inputs").

---

## Stat-curve table family (on-disk layouts) — CYCLE 7

A single boot step loads a **four-file stat-curve family** in one pass and builds an in-memory
scaling-coefficient grid. These tables hold **per-level scaling coefficients and per-level stat-point /
XP-bar values — NOT base HP/MP magnitudes**. The base HP/MP a character actually has is server-supplied
at runtime (it arrives in the major-`4` snapshot, the `5/67` resync, and the `5/32` level-up vitals); a
parser of these files that yields **0 base HP/MP is faithful, not a bug**. See `specs/progression.md` §13
for the behavioural story (absent-by-design base HP/MP, data-driven level cap, no client XP→level
formula). The per-level **derived-stat-A / derived-stat-B contributions** (`userpoint` +24 / +28 below)
are the per-level *coefficients* the max-HP-like / max-MP-like sums add in; they scale runtime stat totals
but do not themselves carry the server-supplied base magnitudes — consistent with the formula's
`level_base` / `server_base` external inputs above.

The four files load together (cross-ref `formats/config_tables.md`):

| File | On-disk record stride | Keying | Role |
|---|---|---|---|
| `users.scr`     | one 496-byte blob (4 × 124-byte per-class windows; no per-record stride) | per-class window index | per-class divisor (`A`, u16) + ratio (`B`, f32) inputs to the scaling grid |
| `userlevel.scr` | **60 bytes** per record | level key | per-level scaling coefficients (level-keyed) |
| `userpoint.scr` | **32 bytes** per record | level key (`+0` u16) | per-level stat-point allocation + XP-bar window + derived-stat contributions |
| `exp.scr`       | **20 bytes** per record | sequential level index 1..N (`+0` u16) | two parallel level-keyed XP value streams (threshold / range) |

### `userpoint.scr` record (32 bytes) [confirmed layout]

| Offset | Size | Field | Meaning |
|---|---|---|---|
| +0  | u16   | level key | the level this record applies to (1..N). |
| +4  | 4×dword | stat-point block | a **2×2 dword block** addressed `+4 + 4·(sub + 2·group)`, `group`,`sub` ∈ {0,1} — four per-level stat-point / allocation values keyed by a stat-group selector and a sub-index. |
| +20 | u16   | XP-bar range hi | numerator-bound term for the XP-bar fill (see `specs/progression.md` §13.2). |
| +22 | u16   | XP-bar range component | second XP-bar range term. |
| +24 | dword | derived-stat-A per-level contribution | per-level base contribution to the max-HP-like derived-stat sum (a coefficient, not a base magnitude). |
| +28 | dword | derived-stat-B per-level contribution | per-level base contribution to the max-MP-like derived-stat sum. |

### `exp.scr` record (20 bytes) [layout confirmed; interior split UNVERIFIED]

| Offset | Size | Field | Meaning | Tag |
|---|---|---|---|---|
| +0 | u16 | level key | sequential level index 1..N; the loader rejects any out-of-sequence key, and the last key becomes the table's level count. | [confirmed] |
| +2 | 18 bytes | two value streams | the remaining 18 bytes are split into **two parallel level-keyed value streams** (an XP threshold stream and an XP range stream). The exact byte split between the two streams is **UNVERIFIED** (resolved inside the level-keyed container builders; settle with a debugger read of a populated node). | [UNVERIFIED] |

### `users.scr` (496 bytes = 4 × 124-byte per-class windows) [size confirmed; interior UNVERIFIED]

`users.scr` is read as one flat 496-byte blob. The grid-build divisor `A` (u16) and ratio `B` (f32)
helpers index **124-byte per-class windows** — one window read two ways: 62 u16 divisors and 31 f32
ratios over the **same** 124-byte span. Within a window, a `(group, tier)` pair (`group` 0..2,
`tier` 0..2) indexes a **3×3 sub-grid**.

| Window | Offset | Size |
|---|---|---|
| class window 0 | +0   | 124 |
| class window 1 | +124 | 124 |
| class window 2 | +248 | 124 |
| class window 3 | +372 | 124 |

> **UNVERIFIED — do not promote the 124-byte window's internal field meaning as confirmed.** Static
> analysis shows a **discrepancy between the users.scr load destination and the divisor/ratio base
> offsets** the grid-build helpers read from (the loaded blob lands offset from where the `A`/`B`
> helpers index). Either the on-disk destination overlaps the `A`/`B` base in a way the static view
> mislabels, or the `A`/`B` tables are populated by a path not statically resolved, or `A` is `0` for
> this build (which would leave the whole grid `0`). This must be settled by a live (`?ext=dbg`) read
> of the loaded region after the boot load step. Until then, treat the per-class window's interior
> field meaning as **UNVERIFIED**.

### In-memory scaling grid (built at boot) — 5×3×3 [dimensions confirmed; consumer UNVERIFIED]

The loader builds a **45-float scaling grid** via a triple loop = **5 × 3 × 3**:

| Dimension | Size | Meaning |
|---|---|---|
| 1st | 5 | class slot (4 real classes — matching the 4 `users.scr` windows — plus 1 spare slot present in the loop). |
| 2nd | 3 | stat group. |
| 3rd | 3 | tier / sub-grade within the group. |

Each cell is computed as:

```
cell = (10 / A) * B
```

where `A` is the per-class u16 divisor and `B` the per-class f32 ratio, both drawn from the matching
`users.scr` 124-byte window. A cell is written **only when `A > 0`**; when `A == 0` the slot is left at
its zero-initialised value (so the 5th/spare class slot, which has no `users.scr` window, stays 0).

Conceptual in-memory globals the loader maintains:

- the **5×3×3 scaling grid** (45 floats);
- the **saved level count** (= the last level key, the data-driven level cap — `specs/progression.md`
  §13.3);
- the **current-level mirrors** (a HUD/eligibility level cache and the local-player struct's level
  field), both **RUNTIME-ONLY** (server-supplied).

> **UNVERIFIED — grid consumer not statically resolvable.** The only references to the scaling grid are
> inside the loader itself (the zero-init and the per-cell write); no statically-resolvable routine reads
> the grid back. Candidate per-level / derived-stat routines were checked and do **not** read it.
> Register this for a live (`?ext=dbg`) read: dump the grid after the boot load step, then watch which
> routine loads from it. This couples with the users.scr base-offset discrepancy above — if `A` is
> always `0`, the grid is all-zero and effectively unused in this build, which must be ruled in or out
> live.
