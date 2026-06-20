# Skill structures — disk catalog, wire, and runtime layouts (clean-room spec)

> **Verification banner.** verification: **confirmed** for the loader/handler control-flow facts —
> the `skills.scr` `1504 + N×8` record framing (sub-entry count `N` is a u8 at +1500), the 8→12-byte
> sub-row runtime expansion, the **1508-byte runtime object** (1504-byte fixed block + a trailing
> sub-entry-array pointer at +1504), the +0 `SkillId` / +4 `GlobalCategory` index keys, the four
> skill-wire opcode→handler routings and their body sizes (5/33 = 20B, 4/41 = 24B, 4/150 ≥ 16B, and
> the 5/52 **24-byte header**), and the per-handler ownership gates + primary result/mode/slot byte
> offsets; **static-hypothesis** for the on-disk combat-stat block (§A.2.5, sample-verified but not
> re-read from a consumer this pass), `skillneedset.scr`, and the runtime offsets carried from prior
> analysis; **capture/debugger-pending** for the 5/52 per-target damage / HP / stamina **value
> semantics** and any field-value meaning that needs a live wire capture.
> ida_reverified: 2026-06-16; re-verified against doida.exe IDB SHA 263bd994, CYCLE 7 (2026-06-20)
> · ida_anchor: 263bd994 · evidence: [static-ida] · conflicts: none for
> the skill layouts; the 5/52 §E.4 stub is **superseded** by the statically-recovered 24-byte header
> (see §E.4 and `packets/5-52_actor_skill_action.yaml`).
>
> **CYCLE 7 update.** The combat-stat block (§A.2.5) was re-recovered from the *consumer* side — the
> battle controller's cast gate, the AoE target resolver, and the 2/52 builder all read these fields
> at the offsets below — so the rows promoted in §A.2.5 move from **SAMPLE-VERIFIED** to
> **CONSUMER-CONFIRMED**. Key refinement: **+1316 is dual-purpose** — a radius (game units, compared
> squared) for shape modes 3/6/0xA, but a **cone half-angle in DEGREES** for shape mode 4 (see
> §A.2.5 and §A.5).

Neutral, offset-model of the skill subsystem the legacy client used: the on-disk skill catalog
(`skills.scr`), its prerequisite-graph sidecar (`skillneedset.scr`), the runtime hotbar tables, and
the skill-related wire packets. Promoted from dirty-room notes; rewritten, no decompiler identifiers,
no binary addresses. Design input for the **network-protocol-engineer** (skill packets), the
**assets-parser-engineer** (`skills.scr` / `skillneedset.scr` decoders), and the **domain engineer**
(skill model, hotbar, `SkillType` / `SkillSort` enums, effect/buff catalog).

> **Confidence tags:** `CONFIRMED` = cross-checked at multiple sites or directly reproduced from
> sample bytes; `SAMPLE-VERIFIED` = read back from the committed sample with consistent values;
> `LIKELY` = single consistent site; `UNVERIFIED` = inferred / boundary not pinned.

> **Text encoding:** all human-readable strings in `skills.scr` (skill name, descriptions) are
> **Korean CP949** (code page 949), null-terminated. Decoders must use CP949, not UTF-8.

> **Endianness:** all multi-byte fields are little-endian.

---

## Status header — confidence summary

| Area | State |
|------|-------|
| `skills.scr` record framing (1504 fixed + N×8 sub-entries, count `N` = u8 at +1500) | **CONFIRMED** (control-flow) — the loader reads the 1504-byte fixed block, takes `N` (u8) at +1500, allocates / reads `8 × N` sub-entry bytes, then advances by the total length; cross-checked by a sample walk (3,737 records, file fully consumed, 0 remainder) |
| `skills.scr` sub-row expansion (8B disk → 12B runtime) + 1508B runtime object | **CONFIRMED** (control-flow) — runtime object = `new(1508)` (1504 fixed + sub-entry-array pointer at +1504); 8→12B expansion is byte-exact (disk +0→rt +0 word; disk +2 i16 → rt +4 i32 sign-extended; disk +4→rt +8 word; disk +6→rt +0xA byte) |
| `skills.scr` index keys (+0 SkillId, +4 GlobalCategory) | **CONFIRMED** (control-flow) — primary catalog index keyed on +0; a secondary index keyed on +4 when non-zero |
| Skill ID (+0), global category (+4), name (+8), class flag (+516) | **SAMPLE-VERIFIED** |
| Tier byte (+520) | **CONSUMER-CONFIRMED** (CYCLE 7) — the learn-gate orders chain forms by +520 and the cast gate compares it; sample histogram retained |
| Constant marker +260 = `0x30000000` | **SAMPLE-VERIFIED** — identical across all 2,000 valid records |
| Skill SORT (+1306), target/shape mode (+1308) | **CONSUMER-CONFIRMED** (CYCLE 7) |
| Prerequisite array (+1280, 3 entries) | **CONSUMER-CONFIRMED** (CYCLE 7) — the learn-gate scans 3 entries from +1280 for the parent skill |
| Combat-stat block (+1304..+1370): range, AoE, max-hit, cadence gate, recast, weapon req, timed-cost gates | **CONSUMER-CONFIRMED** (CYCLE 7) — read at these offsets by the cast gate / AoE resolver; sample values retained |
| Movement-cooldown (+1372) and movement-range (+1412) | **SAMPLE-VERIFIED** — mutually exclusive with +1334 across all valid records |
| Sub-entry disk row (8 bytes) + runtime expansion (12 bytes) | **CONFIRMED** (control-flow framing); effect-code semantics mostly `LIKELY`/`UNVERIFIED` |
| `skillneedset.scr` 4-byte edge records | **SAMPLE-VERIFIED** — 22 edges, exact (not re-read from a consumer this pass) |
| Skill wire opcode→handler routing + body sizes (5/33=20B, 4/41=24B, 4/150≥16B, 5/52 24B header) | **CONFIRMED** (control-flow) — each via its dispatch slot and the handler's fixed read size; ownership gates + primary result/mode/slot byte offsets confirmed; field *value* semantics where noted remain capture-pending |
| Hotbar runtime tables (240-slot id array, 8-byte stride; parallel i16 points) | **CONFIRMED** (control-flow) — both 5/33 and 4/41 write the same dual arrays |

**Open items (full list at end):** prerequisite composite-ID decode; effect-code 1..42 semantics;
magnitude `30001` sentinel; level-threshold-as-negative-duration; `+1372` reader code path; exact
name-buffer length (24 vs 32); `+1368` cost semantics; 5/52 **per-target value semantics** (the 5/52
*header* is now statically recovered — see §E.4).

---

## Part A — `skills.scr` disk catalog

`skills.scr` is the master skill-definition table loaded from disk into a per-id catalog. It is the
source of every static skill property (name, class, cost, range, cooldown, effects). Skill instances
are looked up by skill id through this catalog, which is **separate** from the world-actor map.

### A.1 File / record framing  — SAMPLE-VERIFIED

Each record is **variable length**:

```
[ 1504-byte fixed block ] + [ N × 8-byte sub-entry rows ]
```

- The sub-entry count `N` is a `u8` at fixed-block offset **+1500**.
- If `N == 0`, the record is exactly 1504 bytes and nothing trails it.
- If `N > 0`, the record is `1504 + N × 8` bytes on disk.
- Decode loop: read 1504 bytes, read `N` from byte +1500, then read `N × 8` more bytes if `N > 0`,
  then advance by the total record length.

Sample evidence (`skills.scr`, 5,631,640 bytes): a sequential walk yields **3,737 records with the
file fully consumed (0 bytes remainder)**, of which **2,000 carry a valid skill id** (`0 < id < 10,000,000`).
The remaining records hold ids ≥ 10,000,000, zero ids, or unaligned filler — treat as padding/obsolete
slots and skip. Observed sub-entry counts in this sample: `N ∈ {0,1,2,3}` (1424 records with 0,
511 with 1, 52 with 2, 13 with 3). Larger counts may exist in the full retail table; the count is a
`u8` so the format permits 0..255.

### A.2 Fixed-block field layout (1504 bytes)

Offsets are relative to the start of the fixed block. Regions not listed are zero in valid sample
records (text overflow aside) and are reserved/unverified.

#### A.2.1 Header and identity (+0 .. +520)

| Offset | Size | Type | Field | Sample evidence | Conf |
|------:|-----:|------|-------|-----------------|------|
| +0   | 4 | u32 | **SkillId** — catalog key | Values 11, 12, 13, 21..43, 111..141, 1011..1311, up to 951676 | SAMPLE-VERIFIED |
| +4   | 4 | u32 | **GlobalCategory** — skill family/tree index (see A.3.1) | 150..158 (class skills), 21..30, 47..51, 60..65, 80..131 (combo/chain), 0 (mob skills) | SAMPLE-VERIFIED |
| +8   | ≤24 | char[] CP949 | **Name** (null-terminated) | Longest observed = 28 bytes before NUL; byte at +32 always 0 in valid records | SAMPLE-VERIFIED (presence); buffer length UNVERIFIED (24 vs 32) |
| +32 .. +259 | 228 | — | Reserved / name-overflow region | Zero in valid records aside from CP949 multi-byte name spill | UNVERIFIED |
| +260 | 4 | u32 | **TypeMarker** = `0x30000000` (constant) | Identical in all 2,000 valid records | SAMPLE-VERIFIED (value); semantic UNVERIFIED (likely internal type/version tag) |
| +261 .. +515 | 255 | — | Reserved | Zero in valid records | UNVERIFIED |
| +516 | 4 | u32 | **ClassFlag** = `classId << 16` (see A.3.3) | `0x00010000`..`0x00040000`; `0` = universal/mob | SAMPLE-VERIFIED |
| +520 | 1 | u8 | **TierByte** — tier / chain-form index (see A.4); the learn-gate orders chain forms by +520 and the cooldown-ready compare reads `(+520)+1 == other.(+520)` | Values 1..6 in valid sample records | CONSUMER-CONFIRMED |

> The text at +521 (long description) and a short description near +1032 are CP949 strings. When a
> previous hexdump showed "integer fields" in the +524..+572 region, those bytes were description
> text read as integers — not numeric fields. Decoders should treat +521 onward as text until the
> structured block at +1072.

#### A.2.2 Description text (+521 .. ~+1071)

| Offset | Size | Type | Field | Evidence | Conf |
|------:|-----:|------|-------|----------|------|
| +521  | ≤510 | char[] CP949 | **LongDescription** (null-terminated) | First 20 valid records confirmed; max observed ~82 bytes | CONFIRMED |
| ~+1032/+1033 | ~40 | char[] CP949 | **ShortDescription / action label** (null-terminated) | Non-null at +1033 for all checked records; a one-byte pad/null at +1032 | LIKELY (exact start offset UNVERIFIED) |

The short description's exact start offset (+1032 vs +1033) is unpinned; a parser should scan for the
NUL-terminated CP949 string in this window rather than hard-coding the start.

#### A.2.3 Chain / reference block (+1072 .. +1279)

This block holds chain-link references and effect-scale floats. Most slots are skill-id composites
(decimal-packed ids) whose decode schema is not yet recovered.

| Offset | Size | Type | Field | Evidence | Conf |
|------:|-----:|------|-------|----------|------|
| +1072 | 4 | u32 | **Reserved/marker** — value `0x00003000` (12288) in many records | NOT universal: several records carry CP949 text overflow here instead | LIKELY (presence); value NOT a reliable constant (downgraded from dirty note) |
| +1116 | 4 | u32 | **ChainRef[0]** — composite skill-id reference | e.g. 141100041, 181002108 | CONFIRMED (presence); decode UNVERIFIED |
| +1120 | 4 | u32 | **ChainRef[1]** | composite id or 0 | CONFIRMED (presence) |
| +1124 | 4 | u32 | **ChainRef[2]** | — | CONFIRMED (presence) |
| +1128 | 4 | u32 | **ChainRef[3]** | — | CONFIRMED (presence) |
| +1132 | 4 | u32 | **ChainRef[4]** | — | CONFIRMED (presence) |
| +1136 | 4 | u32 | **ChainRef[5]** (`3xxxxxxx` prefix family) | e.g. 341100111 | CONFIRMED (presence) |
| +1156 | 4 | u32 | **ChainRef[6]** (second `3xxxxxxx`) | e.g. 341100032 | CONFIRMED (presence) |
| +1176 | 4 | f32 | **ScaleFactorA** — effect/AoE/damage multiplier | 1.0 (movement), 2.0, 1.2, 0.533 | CONFIRMED (presence); semantic UNVERIFIED |
| +1180 | 4 | u32 | **ChainRef[7]** (`8xxxxxxx` prefix) | e.g. 841100111 | CONFIRMED (presence) |
| +1200 | 4 | u32 | **ChainRef[8]** (`88xxxxxxx` prefix) | e.g. 881307201 | CONFIRMED (presence) |
| +1220 | 4 | f32 | **ScaleFactorB** — secondary multiplier | 1.2, 1.4, 0.533 | CONFIRMED (presence); semantic UNVERIFIED |
| +1276 | 4 | u32 | **SlotCount / max tier** — small integer | 7 observed | UNVERIFIED |

Gaps between the listed slots (e.g. +1140..+1155, +1184..+1199, +1224..+1275) are zero in valid
sample records — reserved or additional chain slots, UNVERIFIED.

#### A.2.4 Prerequisite / chain-forward block (+1280 .. +1303)

| Offset | Size | Type | Field | Evidence | Conf |
|------:|-----:|------|-------|----------|------|
| +1280 | 4×3 | u32[3] | **PrerequisiteSkillId[0..2]** — a 3-entry prerequisite array; entry 0 is the primary required parent (0 for base skills). The learn-gate scans all three entries for the parent skill already on the hotbar | skill 13 → 11; skill 300132 → 300131; skill 310031 → 131307011 (composite) | CONSUMER-CONFIRMED (3-entry array); composite decode UNVERIFIED |
| +1292 | 2 | u16 | **SkillPointCost** — skill points to learn / level this skill | 4 / 8 / 12 for tier 1/2/3; 1 for some special skills; 0 for mob skills | SAMPLE-VERIFIED |
| +1294 | 2 | u16 | **NextTier / chain-forward ref** | 0 or composite id | CONFIRMED (presence); encoding UNVERIFIED |
| +1296 | 4 | u32 | **ChainUpgradePath[0] / rank-selected variant** — the cast path resolves a per-rank child variant first, then falls back to +1296 | single id or composite | CONSUMER-CONFIRMED (structure); per-rank field meanings UNVERIFIED |
| +1300 | 4 | u32 | **ChainUpgradePath[1]** | similar | CONFIRMED (presence) |

> **Important `+1292` reconciliation.** In the **disk** record, +1292 is the **SkillPointCost to
> learn**. In the **runtime** skill object (Part C), the field at the same +1292 offset is read back
> as the current **skill rank/level** and mirrored into the hotbar slot-points array. Same byte
> offset, two contexts: the disk value is the *cost*; the runtime value is the *current rank* after
> the object is populated and a skill is trained. Earlier clean specs labelled +1292 `skill_rank`
> from runtime access — that remains correct for the runtime context.

#### A.2.5 Combat-stats block (+1304 .. +1370)  — SAMPLE-VERIFIED

These are the fields the cast pipeline, target resolver, and cooldown rebuilder read. The CYCLE 7
consumer pass (battle controller cast gate + AoE resolver + 2/52 builder) read each of the rows
tagged **CONSUMER-CONFIRMED** below directly at the listed offset; sample values are retained from
the prior sample walk.

> **+1316 is dual-purpose (CYCLE 7).** For TargetShapeMode (+1308) values **3 / 6 / 0xA**
> (circle / party / radial) it is a **radius in game units**, compared squared. For TargetShapeMode
> **4** (cone) it is a **cone half-angle in DEGREES**, multiplied by the degree→radian constant
> (π/180 ≈ 0.0174533) to obtain radians. The meaning is selected entirely by +1308. See §A.5.

> **+1332 vs +1334 (CYCLE 7 reconciliation).** The CYCLE 7 *consumer* read shows the cast gate's
> **cast-cadence / cooldown** check compares elapsed time since the last action against
> **100 ms × (+1332)** — i.e. +1332 is the **cast-cadence factor** the gate enforces, NOT an MP-pool
> subtract. The earlier "MpCostGateFactor" framing (cast blocked if available MP < `100 × factor`) is
> a sample-era interpretation of the same field; the control-flow-confirmed role is the cadence gate
> (`elapsed_ms < 100 × (+1332)` → blocked). +1334 remains the per-slot recast-table duration source
> (1/100 s × 100 → ms), populated when the cooldown table is rebuilt from the hotbar. The two are
> related but read by different code: +1332 is the inline cadence gate inside the cast routine; +1334
> is the duration written into the 240-slot recast table.

| Offset | Hex | Size | Type | Field | Sample values | Conf |
|------:|-----|-----:|------|-------|---------------|------|
| +1304 | 0x518 | 2 | u16 | **MotionIndexA** — animation index | 1, 20, 46, 72 (steps of ~26 across tiers) | CONFIRMED |
| +1306 | 0x51A | 2 | u16 | **SkillSort** — internal discriminator (see A.3.2) | movement=7, combat=2, mob=5, passive(심법)=6, chain=11, revive=14, plus 0/1/3/17 | CONSUMER-CONFIRMED |
| +1308 | 0x51C | 1 | u8 | **TargetShapeMode** — targeting/shape (see A.5); the switch selector in the AoE resolver (0..0xB) | self/move=0, single-enemy=2, chain-AoE=3, combo=8 | CONSUMER-CONFIRMED |
| +1309 | 0x51D | 3 | — | Padding (always 0) | — | CONFIRMED |
| +1312 | 0x520 | 4 | f32 | **BaseRange / cone length** (game units); cast range = `+1312 + caster body radius`; cone length for shape 4 | movement: 0.0 (uses +1412); combat: 45.0–60.0; mob: 10.0 | CONSUMER-CONFIRMED |
| +1316 | 0x524 | 4 | f32 | **AoeRadius (shape 3/6/0xA) OR cone half-angle in DEGREES (shape 4)** — radius is compared squared; cone half-angle is × π/180 to radians (see callout above and §A.5) | combat: 30.0–60.0; mob: equals +1312 | CONSUMER-CONFIRMED (dual use) |
| +1320 | 0x528 | 4 | u32 | Reserved A (always 0) | — | UNVERIFIED |
| +1324 | 0x52C | 4 | u32 | Reserved B (always 0) | — | UNVERIFIED |
| +1328 | 0x530 | 4 | u32 | **ClassFlagSecondary** = `classId << 16` | mirrors +516 for single-class skills; 0 universal | CONFIRMED (pattern) |
| +1330 | 0x532 | 2 | i16 | **MaxTargetHits** — AoE hit-count cap; engine clamps to **40**; the event-boost flag doubles it (×2) | movement/passive: 1; some combat: 3 | CONSUMER-CONFIRMED |
| +1332 | 0x534 | 2 | i16 | **CastCadenceFactor** — cast-cadence / cooldown gate: blocked while `elapsed_ms < 100 × (+1332)`. NOT an MP-pool subtract (see callout above) | movement/passive: 0; combat/mob: 10 (→ 1000 ms cadence) | CONSUMER-CONFIRMED |
| +1334 | 0x536 | 2 | u16 | **CombatRecast** — recast-table duration in 1/100-second units (×100 → ms); source for the 240-slot recast table | movement: 0; combat: e.g. 600 (=6.00 s), 3 (=0.03 s chain reuse); mob: 14 (=0.14 s) | SAMPLE-VERIFIED |
| +1336 | 0x538 | 4 | u32 | Reserved C (always 0) | — | UNVERIFIED |
| +1340 | 0x53C | 4 | u32 | Reserved D (always 0) | — | UNVERIFIED |
| +1344 | 0x540 | 2 | u16 | **WeaponReqIdA** — required worn-weapon class; weapon-req gate vs worn weapon; also halves the enemy radius in shape 3 when set with +1348; basic-attack match in the hotbar scan | movement/passive: 0; combat: nonzero (e.g. 7, 46) | CONSUMER-CONFIRMED |
| +1346 | 0x542 | 2 | u16 | Padding / unknown (always 0) | — | UNVERIFIED |
| +1348 | 0x544 | 4 | u32 | **WeaponReqIdB** (secondary, vs target); paired with +1344 as the shape-3 enemy-radius-halving trigger | 0 in all sample records | CONSUMER-CONFIRMED (structure); value 0 in sample |
| +1352 | 0x548 | 1 | u8 | **WeaponReqActiveFlag** — gates the +1344 special-weapon branch | movement/passive: 0; some combat: 1 | CONSUMER-CONFIRMED |
| +1353 | 0x549 | 1 | u8 | Unknown flag byte | 1 for some combat skills; else 0 | UNVERIFIED |
| +1354 | 0x54A | 2 | u16 | Unknown (always 0) | — | UNVERIFIED |
| +1356 | 0x54C | 4 | u32 | Reserved E (always 0) — within the weapon-req block walked in 12-byte steps (+1344 → +1368) | — | UNVERIFIED |
| +1360 | 0x550 | 4 | u32 | Reserved F (always 0) | — | UNVERIFIED |
| +1364 | 0x554 | 4 | u32 | Reserved G (always 0) | — | UNVERIFIED |
| +1368 | 0x558 | 2 | i16 | **CastCostTimerThreshold** — a **timed-charge gate** compared against a running global clock (blocks when `>0`, the clock delta is negative, and the value `< 30000`); NOT a flat pool subtract on this path. Magnitude server-authored | 0 in all sample records | CONSUMER-CONFIRMED (structure); semantic CAPTURE-PENDING |
| +1370 | 0x55A | 2 | u16 | **StaminaCostTimerThreshold** — a **timed-charge gate** compared against a separate running global clock (blocks when `>0` and the clock delta is negative); NOT a flat pool subtract on this path | movement/passive: 0; combat: 20–50, scaling per tier | CONSUMER-CONFIRMED (structure); semantic CAPTURE-PENDING |

#### A.2.6 Secondary timing / range block (+1372 .. +1495)

| Offset | Hex | Size | Type | Field | Sample values | Conf |
|------:|-----|-----:|------|-------|---------------|------|
| +1372 | 0x55C | 4 | u32 | **MovementCooldown** (seconds) — used by a *different* code path than +1334 | movement (sort=7): 8–16 s; all non-movement: 0 | SAMPLE-VERIFIED (value pattern); reader code path UNVERIFIED |
| +1376 .. +1408 | — | — | Reserved (u32/f32 slots, all 0 in valid records) | — | UNVERIFIED |
| +1412 | 0x584 | 4 | f32 | **MovementRange** — primary range for sort=7 skills | movement: 30.0–40.0; passive: ~0; combat: 0.0 | SAMPLE-VERIFIED |
| +1416 .. +1495 | 80 | — | Reserved (mostly 0) | — | UNVERIFIED |

> **Cooldown mutual exclusion.** Across all 2,000 valid sample records, **+1334 and +1372 are never
> both non-zero**: combat/mob skills use +1334 (centi-seconds), movement skills (sort=7) use +1372
> (seconds). The two fields express the same concept for different subsystems keyed on SkillSort.
> Likewise +1312 (combat range) and +1412 (movement range) are read by different code paths.

#### A.2.7 Tail (+1496 .. +1503)

| Offset | Size | Type | Field | Evidence | Conf |
|------:|-----:|------|-------|----------|------|
| +1496 | 4 | f32 | **TailFloat** — 1.0 when the record has sub-entries, 0.0 when none | skill 11 (0 subs): 0.0; skill 1011 (1 sub): 1.0 | LIKELY |
| +1500 | 1 | u8 | **SubEntryCount `N`** — number of trailing 8-byte rows | 0..3 in sample; `u8` overall | SAMPLE-VERIFIED |
| +1501 | 3 | — | Padding (always 0) | — | CONFIRMED |

### A.3 Category / sort / class encodings

The record carries **two** independent classification fields plus a class bitfield.

#### A.3.1 GlobalCategory at +4 (u32) — skill family/tree

Groups skills into named trees. Approximate sample mapping:

| Range | Meaning |
|-------|---------|
| 150 / 151 / 152 / 153 | Class 1 / 2 / 3 / 4 movement-and-기공 (lightfoot) skill family |
| 154–157 | Class 1–4 심법 (heart-method / passive-buff) family |
| 158–161 | Class 1–4 환마술 (illusion / debuff) family |
| 21–30 | Inner-cultivation skill tiers |
| 47–51 | Cross-class chain tiers |
| 60–65 | Advanced skill chains |
| 80–91 / 94 / 121–125 / 131 | Combo / combo-chain skills |
| 0 | Mob-only skills (no class) |

#### A.3.2 SkillSort at +1306 (u16) — internal discriminator  — SAMPLE-VERIFIED

The value all game logic keys on (cast pipeline, target resolution, UI routing, cooldown). Distinct
from +4. Sample histogram over the 2,000 valid records: `5→1477, 2→238, 3→142, 0→91, 11→24, 7→12,
17→7, 6→4, 1→4, 14→1`.

| Value | Meaning | Notes |
|------:|---------|-------|
| 0 | Generic / unclassified (passive icon only) | |
| 1 | Standard active skill | |
| 2 | **Combat active** — has MP gate, target resolution, AoE | |
| 3 | Chain tier | |
| 5 | **Mob skill** (NPC-only combat) | dominant in this sample |
| 6 | **Passive buff** (심법 heart-method) | |
| 7 | **Movement skill** (lightfoot / 경공 family) | uses +1372 cooldown and +1412 range |
| 11 | **Combo-chain slot** | |
| 14 | **Revive** | requires a dead target (engine checks target dead-state) |
| 17 | Reserved/unknown active variant — **present in sample (7 records)** | semantic UNVERIFIED |

> Correction vs prior dirty note: SkillSort value **17 is present in the sample** (7 records); it is
> not "no sample seen". Its exact behaviour is UNVERIFIED.

#### A.3.3 ClassFlag at +516 (u32)

`classId << 16`:

| Value | Class |
|-------|-------|
| `0x00010000` | Class 1 (무사 / Warrior) |
| `0x00020000` | Class 2 (자객 / Assassin) |
| `0x00030000` | Class 3 (도사 / Taoist) |
| `0x00040000` | Class 4 (승려 / Monk) |
| `0x00000000` | Universal / mob skill |

### A.4 TierByte at +520 (u8)

Chain-form / tier index. Sample histogram: `1→1620, 2→109, 3→89, 4→83, 5→60, 6→39` (no value 0 in
valid sample records).

| Value | Meaning |
|------:|---------|
| 1 | Base / first form |
| 2 | Advanced (third-tier in the movement family; second chain link in chains) |
| 3 | Third / fifth chain slot |
| 4 | Second-tier movement |
| 5 | Fifth chain |
| 6 | Sixth chain |

For chain skills the tier byte tracks the chain-form index (1초식=1, 2초식=2, …). For movement skills
the upgrade order observed is base=1, upgrade=4, advanced=2.

### A.5 TargetShapeMode at +1308 (u8)

Targeting / shape dispatch. The CYCLE 7 consumer pass recovered the geometry of each AoE branch from
the resolver; the exact algebra is given below.

| Value | Shape | Behaviour |
|------:|-------|-----------|
| 0  | Self / single | primary target only; movement skills use this |
| 1  | Single ally | faction/team gate + target-state check |
| 2  | Single enemy | target-state check; heal if friendly else damage |
| 3  | Chain / nearby AoE | radius² from `+1316` (squared); enemy radius **halved** when both +1344 and +1348 are set; include actors with `distXZ² < radius²` |
| 4  | Cone / forward line AoE | length `L = +1312 + caster radius`; cone = circular sector (see algebra below) |
| 5  | Ground / point only | no actor targets resolved here |
| 6  | Party AoE | walks party roster; radius² from `+1316` (squared) |
| 7  | Faction/group-gated single | style/team match |
| 8  | Combo-chain trigger | triggers chain animation sequence |
| 9  | PK-gated single | team-byte gate |
| 10 (0xA) | Radial AoE (both factions) | radius² from `+1316` (squared); loops all actors, classifies PC vs mob |
| 11 (0xB) | Self-only | clears arrays; PC-count = 1 with caster id |

Sample correlation: movement skills (sort=7) → mode 0; combat AoE → mode 3; single-target combat →
mode 2; combo skills → mode 8; mob skills → mode 2.

**AoE shape algebra (CYCLE 7, consumer-confirmed).** Distance tests are planar (XZ) and squared
throughout; the **combined hit count is clamped to 40** after the event-boost doubling.

- **Circle (mode 3).** `radius²` derived from `+1316` (squared). When the weapon-requirement fields
  +1344 and +1348 are both set, the **enemy radius is halved** (`(+1316 × 0.5)²`). Loop the actor
  list; include each actor whose squared XZ distance to the center (the primary target) is `< radius²`.
- **Cone (mode 4) — a circular sector.** Length `L = +1312 + caster body radius`. The forward axis is
  the normalized vector from the primary target to the caster, scaled by `L / 5.0`, with the origin
  offset to the caster. The range test uses an effective squared range of `2·L²`. The **half-angle**
  is `+1316` interpreted as DEGREES, multiplied by π/180 to radians. Per actor: include iff the
  squared XZ distance is within `2·L²` AND the bearing `atan2(actor.x − center.x, actor.z − center.z)`
  falls inside the `facing ± half-angle` window. So the sector's range is `√(2·L²)` and its total
  angular width derives from `+1316°`.
- **Party (mode 6).** `radius²` from `+1316` (squared); walk the party roster and include members
  within `radius²`.
- **Radial (mode 0xA).** `radius²` from `+1316` (squared); loop all actors, classify each into the PC
  list or the mob/NPC list within range.
- **Event-boost.** When the event-boost flag (the local-player record's boost flag) is set and the
  base hit count is ≥ 2, the hit count doubles (×2) and the area quadruples (radius ×2, area ×4);
  the combined count is then clamped to 40.

### A.6 Sub-entry / effect rows (trailing 8-byte entries)

Each record's trailing rows (count `N` at +1500) describe the skill's effects/buffs. The disk row is
8 bytes; the runtime expands it to a 12-byte slot.

#### A.6.1 Disk row (8 bytes)  — CONFIRMED framing

| Offset | Size | Type | Field | Evidence | Conf |
|------:|-----:|------|-------|----------|------|
| +0 | 2 | u16 | **EffectTypeCode** (see A.6.3) | 0..68 | CONFIRMED |
| +2 | 2 | i16 | **Magnitude / parameter** | small ints; can be negative via sign | CONFIRMED |
| +4 | 2 | u16 | **LevelThreshold / duration parameter** | 0..30001; see note on negative-duration encoding | CONFIRMED |
| +6 | 1 | u8 | **SubParam** (secondary, usually 0) | — | CONFIRMED |
| +7 | 1 | — | Padding (always 0) | — | CONFIRMED |

#### A.6.2 Runtime expansion (12 bytes)

| Runtime offset | Size | Type | Source |
|---------------:|-----:|------|--------|
| +0x00 | 2 | u16 | disk +0 (effect type) |
| +0x02 | 2 | — | zero pad |
| +0x04 | 4 | i32 | disk +2 i16, sign-extended |
| +0x08 | 2 | u16 | disk +4 (level threshold) |
| +0x0A | 1 | u8  | disk +6 (sub-param) |
| +0x0B | 1 | — | zero pad |

#### A.6.3 Observed EffectTypeCode values

Semantics are inferred from skill names (CP949) and effect-applicator behaviour. Codes roughly 43+
have recovered applicator logic; codes below ~42 are mostly icon/stat effects whose precise handling
is UNVERIFIED (may be applied server-side or in a different client path).

| Code | Inferred meaning | Conf |
|-----:|------------------|------|
| 0 | Null / no-op placeholder | LIKELY |
| 1 | HP-rate buff (event) | UNVERIFIED |
| 2 | Attack-stat boost | UNVERIFIED |
| 3 | Defense-stat boost | UNVERIFIED |
| 4 | EXP / drop-rate buff | UNVERIFIED |
| 5 | Stat A (mob) | UNVERIFIED |
| 6 | Stamina regen | UNVERIFIED |
| 7 | Internal-energy (내공) increase | LIKELY |
| 9 | Inner-cultivation increase | LIKELY |
| 14 | MP restoration | UNVERIFIED |
| 15 | Max-MP increase | LIKELY |
| 16 | Max-HP increase | LIKELY |
| 20 | Mob movement/speed buff | UNVERIFIED |
| 21 | Offense-stat increase (scales per tier) | LIKELY |
| 22 | HP regen / vitality | LIKELY |
| 25 | Special mob/boss boost | UNVERIFIED |
| 30 | Max-HP increase (secondary) | LIKELY |
| 42 (0x2A) | Armor / defense self-buff | LIKELY |
| 43 (0x2B) | **Stance enter** (motion state 11) | CONFIRMED (applicator) |
| 44 (0x2C) | **Stealth / invisibility** (motion state 12) | CONFIRMED (applicator) |
| 46 (0x2E) | **Transform / petrify** (model swap) | CONFIRMED (applicator) |
| 47 (0x2F) | **Root / snare** (movement restriction) | CONFIRMED (applicator) |
| 48 (0x30) | **Dispel / cleanse** (clears codes 43/46/47, resets stance) | CONFIRMED (applicator) |
| 49 (0x31) | Blind / confusion | LIKELY |
| 50 (0x32) | Poison / DoT (appearance transform) | LIKELY |
| 51 (0x33) | Revival-condition flag | LIKELY |
| 57 (0x39) | Transform / special state | CONFIRMED (applicator: sets a transform state) |
| 58 (0x3A) | Summon / raise undead | LIKELY |
| 60 (0x3C) | Boss resistance | UNVERIFIED |
| 63 (0x3F) | Defense penetration | LIKELY |
| 66 (0x42) | Defense reduction (debuff) | LIKELY |
| 67 (0x43) | Bleed / DoT A | LIKELY |
| 68 (0x44) | Bleed / DoT B (paired with 67) | LIKELY |

> **Magnitude `30001` sentinel.** This exact magnitude recurs across passive/심법 families and likely
> means "permanent / always-active / no expiry" rather than a literal numeric magnitude. UNVERIFIED.

> **LevelThreshold as signed duration.** Values ≥ ~65500 (e.g. 65496, 65516, 65523) read as small
> negative `i16` (−40, −20, −13). The pattern suggests the field doubles as a signed duration (in
> ticks) for debuff/DoT effects, while small positive values are a minimum skill level to unlock.
> Whether the loader sign-extends is UNVERIFIED — decoders should keep the raw `u16` and let the
> domain layer interpret.

---

## Part B — `skillneedset.scr` prerequisite graph  — SAMPLE-VERIFIED

A small sidecar table of prerequisite edges. Fixed 4-byte records, no header.

| Offset | Size | Type | Field |
|------:|-----:|------|-------|
| +0 | 2 | u16 | **PrerequisiteSkillId** (must be learned first) |
| +2 | 2 | u16 | **DependentSkillId** (unlocked by the prerequisite) |

Sample (`skillneedset.scr`, 88 bytes) = exactly **22 edges**, reproduced byte-for-byte:

```
303→305  303→307  304→306  304→308  310→311  311→312  312→313  313→316
316→317  317→318  317→319  318→319  319→320  320→321  321→326  326→329
329→331  329→332  331→332  332→333  331→333  333→334
```

These form a directed acyclic graph with branching and merging (multiple unlock routes — e.g. skill
332 via 329→332 directly or via 331). The skill ids 303–334 do **not** appear in this sample's
`skills.scr`; they reference a wider retail skill set. The edge format and DAG structure are
confirmed; the referenced definitions are simply absent from the sample extract.

---

## Part C — Runtime skill object and catalog

The disk fixed block is loaded into a runtime skill object whose field offsets are **identical to the
disk offsets** for the fixed region (+0 .. +1503). The runtime object is **4 bytes larger** than the
disk block (1508 vs 1504): the extra trailing word holds a **pointer to the heap-allocated sub-entry
array** (12-byte stride), referenced at runtime offset **+1504**. The sub-entry count is mirrored from
disk +1500.

The runtime skill catalog is a **map keyed by skill id**, distinct from the world-actor map. Skills
are resolved through a dedicated catalog lookup, never through the general actor lookup. This is the
universal skill accessor used by the cast pipeline, the hotbar-apply path, and the cooldown rebuilder.

Two runtime fields the hotbar-apply path reads from a catalog entry (offsets relative to the entry
start, i.e. same as disk offsets):

| Offset | Size | Field | Conf | Meaning |
|------:|-----:|-------|------|---------|
| +1292 (0x50C) | u16 | **SkillRank** (runtime) | LIKELY | Current rank/level; mirrored into the hotbar slot-points array. (Disk context of this offset = SkillPointCost; see A.2.4.) |
| +1306 (0x51A) | u16 | **SkillSort** | CONFIRMED | The discriminator from A.3.2; drives UI routing (passive/AoE vs active-skill panels). |

`SkillSort` (A.3.2) is the field to back a domain `SkillSort`/`SkillType` enum. A separate per-skill
**activation-mode byte** (resolved by skill id through the catalog, distinct from SkillSort) selects
which UI panel slot an active skill populates — treat as UNVERIFIED.

---

## Part D — Runtime hotbar tables  (unchanged, previously committed)

The skill hotbar state is held in two parallel runtime arrays:

| Array | Element / stride | Capacity | Meaning |
|-------|------------------|----------|---------|
| skill-id array | int32 per slot, **8-byte stride** (2 ints per slot) | 240 slots (0..239) | Skill id assigned to each hotbar slot. Each slot occupies 2 ints; only the first is written by the hotbar-set path. |
| slot-points array | int16 (rank/points) per slot, parallel | 240 slots | Skill point / rank per slot; mirrors the skill catalog entry's rank field (+1292). |

The second int of each 8-byte skill-id pair is **not** written by any analyzed handler — purpose
UNVERIFIED (possibly a skill sub-id or a cooldown mirror). The domain hotbar model should reserve the
pair but depend only on the first int as the skill id.

Hotbar capacity is **240 slots (0xF0)**; the slot index is a `uint8` in the range 0..239.

Update rule (from the hotbar-set push, opcode 5/33):

```
slot_index   = packet hotbar_slot (u8, must be < 240)
skill_id     = packet skill_id (int32)
skill_points = packet skill_points (int16)

hotbar.skill_id[slot_index]    = skill_id
hotbar.slot_points[slot_index] = skill_points
```

---

## Part E — Skill wire packets

> Opcode→handler **routing** and packet **body sizes** below are control-flow confirmed (each via its
> dispatch slot and the handler's fixed read size); the **field value semantics** noted as `LIKELY` /
> `UNVERIFIED` remain capture/debugger-pending (no live capture this campaign). §E.4 (5/52) was
> rewritten this pass from statically-recovered control flow.

### E.1 SkillHotbarSlotSet — 20-byte wire packet (opcode 5/33)

Authoritative hotbar-slot update push (server → client).

| Offset | Size | Type | Field | Conf | Meaning |
|--------|------|------|-------|------|---------|
| +0x00 | 4 | int32 | `sort` | LIKELY | Actor sort (category in the low byte). |
| +0x04 | 4 | int32 | `actor_id` | LIKELY | Actor id (identity check vs. local player). |
| +0x08 | 1 | uint8 | `hotbar_slot` | CONFIRMED | Hotbar slot index (0..239). |
| +0x09 | 3 | char[3] | `pad` | LIKELY | Alignment. |
| +0x0C | 4 | int32 | `skill_id` | CONFIRMED | Skill id to assign to this slot. |
| +0x10 | 2 | int16 | `skill_points` | CONFIRMED | Skill point allocation / rank for this skill. |
| +0x12 | 2 | char[2] | `pad_end` | LIKELY | Padding to 20 bytes. |

Total: 20 bytes. (Field widths sum to 20.)

### E.2 SkillHotbarAssignResult — 24-byte wire packet (opcode 4/41)

Result of a client-initiated hotbar assignment (server → client).

| Offset | Size | Type | Field | Conf | Meaning |
|--------|------|------|-------|------|---------|
| +0x00 | 4 | uint32 | `header` | LIKELY | Packet prefix (value 1 expected). |
| +0x04 | 4 | uint32 | `actor_id` | LIKELY | Actor id. |
| +0x08 | 1 | uint8 | `gate` | CONFIRMED | `1` = apply/ok; `0` = error. |
| +0x09 | 1 | uint8 | `result_code` | LIKELY | Error reason (1..8) → localized error strings. |
| +0x0A | 2 | char[2] | `pad` | LIKELY | Alignment. |
| +0x0C | 4 | int32 | `hotbar_slot_echo` | LIKELY | Echo of the requested hotbar slot. |
| +0x10 | 4 | int32 | `skill_id_echo` | LIKELY | Echo of the requested skill id. |
| +0x14 | 4 | uint32 | `skill_point_pool` | LIKELY | Remaining skill points after assignment. |

Total: 24 bytes. Behaviour: `gate==1` → look up the slot's skill, refresh hotbar + skill UI;
`gate==0` → clear the slot and show the error string.

### E.3 SkillPointUpdate — variable-length wire packet (opcode 4/150), minimum 16 bytes

| Offset | Size | Type | Field | Conf | Meaning |
|--------|------|------|-------|------|---------|
| +0x00 | 1 | uint8 | `valid` | CONFIRMED | Must equal 1. |
| +0x01 | 3 | — | (pad) | LIKELY | Alignment to +0x04. |
| +0x04 | 4 | int32 | `idkey` | LIKELY | Actor-id key, matched against the local player. |
| +0x08 | 4 | uint32 | `mode` | CONFIRMED | `1` = set total skill points; `2` = level-up notice. |
| +0x0C | 4 | uint32 | `value` | CONFIRMED | `mode==1` → new total skill-point pool. `mode==2` → new character level. |

Minimum 16 bytes; `mode==2` paths read additional level-up data from a runtime singleton (not part of
this packet's fixed prefix). The displayed skill-point pool is capped at 255 (0xFF) **for the UI
string only** — the wire value may exceed 255; do not clamp the protocol value.

### E.4 ActorSkillAction — skill-action broadcast (opcode 5/52), variable-length

The richest skill handler: drives AoE projectile spawning, visual effects, and hit application.
**Header layout now statically recovered** (this supersedes the prior "field positions not yet
pinned" stub). The handler reads a **fixed 24-byte header** (payload @0x00..0x17), then `target_count`
× 36-byte per-target records beginning at payload @0x18. The 24-byte header read and the 36-byte
record stride are **control-flow confirmed**; the per-target *value* meanings are capture-pending.

> **Authoritative wire spec.** This struct view is reconciled with — and must stay consistent with —
> `Docs/RE/packets/5-52_actor_skill_action.yaml`, which carries the full corrected header and the
> 36-byte target-record layout. Do not implement 5/52 from this section alone; use that YAML.

#### E.4.1 Fixed 24-byte header (payload-relative) — CONFIRMED (control-flow), values capture-pending

| Offset | Size | Type | Field | Conf | Meaning |
|--------|------|------|-------|------|---------|
| +0x00 | 1 | u8  | `caster_sort` | CONFIRMED | Caster actor sort (low byte of the composite actor key). |
| +0x01 | 3 | —   | (pad) | CONFIRMED | Padding to +0x04. |
| +0x04 | 4 | u32 | `caster_id` | CONFIRMED | Caster actor id (composite key high); resolved via the cached actor lookup. |
| +0x08 | 1 | u8  | `cast_flag` | CONFIRMED | `0` selects the cancel/idle (motion) branch; non-zero = active cast. |
| +0x09 | 1 | u8  | `basic_selector` | CONFIRMED | Basic/alias selector — value **0xFF = basic melee** (matches the C2S 2/52 "melee = slot 0xFF" anchor); also fires when the caster's actor state field == 14. |
| +0x0A | 2 | —   | (pad) | CONFIRMED | Padding to +0x0C. |
| +0x0C | 4 | u32 | `skill_id` | CONFIRMED | Skill id / entity key, resolved against the client skill/entity table. |
| +0x10 | 1 | u8  | `action_code` | CONFIRMED | Action-shape code: `0` = single target; `0xC8..0xCB` = motion sub-ops; `0xCC` = AoE. The handler tests this byte `>= 0xC8` and `!= 0xCC` (the 200/202/203/204/232 result-code family). **There is no separate "SkillArg" dword between `skill_id` and `action_code`** — the prior mislabelled "SkillArg@0x10" is this `action_code`. |
| +0x11 | 3 | —   | (pad / sub-field?) | UNVERIFIED | Pad-vs-subfield split capture-pending. |
| +0x14 | 1 | u8  | `target_count` | CONFIRMED | Number of 36-byte records that follow; bounded `(0, 0x28]` (≤ 40). Drives the `36 × count` record read. Distinct from `action_code` — not co-located in one 0x14..0x17 dword. |
| +0x15 | 3 | —   | (pad / sub-field?) | UNVERIFIED | Pad-vs-subfield split capture-pending. |

#### E.4.2 Per-target record (36 bytes each, repeated `target_count` times)

Stride 36 is **control-flow confirmed**; each record is forwarded to the target actor's per-actor
animation/FX queue and feeds a floating damage number. The per-field *value* semantics below are
**capture/debugger-pending** (carried from the packet YAML):

| Record offset | Size | Type | Field | Meaning (capture-pending) |
|---------------|------|------|-------|---------------------------|
| +0x00 | 1 | u8  | `target_sort` | Target actor sort (lookup key). |
| +0x04 | 4 | u32 | `target_id` | Target actor id (lookup key). |
| +0x08 | 4 | i32 | `anim_hit_state` | `1` = a hit landed; selects the hit animation. |
| +0x0C | 4 | i32 | `visible_damage` | Number rendered as a floating damage figure. |
| +0x10 | 4 | — | (reserved) | Reserved word. |
| +0x14 | 8 | i64 | `remaining_hp` | Remaining HP after hit (signed; negative on overkill; feeds the HP bar). |
| +0x1C | 4 | i32 | `max_hp` | Feeds the HP bar. |
| +0x20 | 4 | — | (reserved) | Tail / reserved. |

On cast-confirm this handler also consumes the per-skill costs from the catalog entry (CastCost
+1368, StaminaCost +1370) and arms the runtime recast slot. When `action_code == 0xCC` the handler
treats the first record as an AoE origin and procedurally fans out sub-actors (no extra wire fields);
`0xC8..0xCB` are motion sub-ops (animation toggles, usually no damage records).

---

## Part F — Cooldown system reconciliation

Two catalog cooldown fields serve different subsystems, mutually exclusive per skill:

| Field | Offset | Type | Units | Used by | Skills |
|-------|-------:|------|-------|---------|--------|
| CombatRecast | +1334 | u16 | 1/100 s (×100 → ms) | runtime recast table | combat / mob skills |
| MovementCooldown | +1372 | u32 | seconds | a separate movement-activation path | movement skills (sort=7), 8–16 s |

The runtime **recast table** is sized to the 240-slot hotbar, populated from +1334 × 100 (ms), ticked
per frame, armed on 5/52 cast-confirm, and queried as part of the cast gate. Its exact in-memory
layout beyond the 240-slot indexing is not fully recovered (UNVERIFIED). The reader of +1372 was not
located this pass.

### Skill state in the SpawnDescriptor

Per `spawn_descriptor.md`: `skill_state_word` (SD+0x1F0, u16) is a high-level skill/buff state
discriminator received on spawn — **not** a per-skill cooldown table. Exact semantics UNVERIFIED.

---

## Notes for the engineers

**Assets-parser engineer (`skills.scr` / `skillneedset.scr`):**

1. `skills.scr` records are variable length: read 1504 bytes, take `N` = `u8` at +1500, then read
   `N × 8` more bytes. Advance by `1504 + N × 8`. Skip records whose +0 id is 0 or ≥ 10,000,000.
2. All strings are **CP949**, null-terminated. Name at +8 (buffer ≤ 32 bytes), long description at
   +521, short description ~+1032/+1033 (scan for the NUL-terminated string).
3. `skillneedset.scr` is a flat array of 4-byte (u16,u16) prerequisite→dependent edges.
4. Expand each 8-byte sub-entry into the 12-byte runtime form per A.6.2 (or keep the disk form and
   let the domain interpret) — keep `LevelThreshold` as a raw `u16`; do not pre-sign-extend.

**Domain engineer:**

5. Back a `SkillSort` enum on +1306 (A.3.2); back class on +516 (`classId << 16`, A.3.3). Treat the
   GlobalCategory at +4 as a separate family/tree grouping.
6. Skill ids are 32-bit; hotbar slots are `uint8` (0..239); the hotbar has 240 slots. The skill-point
   pool is a `uint32` — the 255 cap is a UI-only display clamp; do not clamp the protocol value.
7. Watch the +1292 dual meaning: disk = SkillPointCost-to-learn; runtime = current SkillRank.
8. Model the skill catalog as a separate id-keyed catalog (disk-loaded), not the world-actor map.
9. Effect/buff behaviour comes from the sub-entry rows (A.6); codes 43+ have known applicator
   semantics, codes below ~42 are provisional. The `30001` magnitude is a likely "permanent" sentinel.

## Open questions

- 5/52 ActorSkillAction — the **24-byte header layout is now statically recovered and
  control-flow confirmed** (see §E.4 and `packets/5-52_actor_skill_action.yaml`); the per-target
  36-byte record **stride** is confirmed, but the per-target *value* semantics (damage / HP / stamina
  deltas) and the header pad-vs-subfield split at @0x11..0x13 / @0x15..0x17 remain
  **capture/debugger-pending**.
- PrerequisiteSkillId composite-id decode (e.g. 131307011) — UNVERIFIED schema.
- ChainRef[0..8] and ChainUpgradePath[0..1] composite-id decode — UNVERIFIED.
- EffectTypeCode semantics for codes ~1..42 — inferred from names only, provisional.
- `30001` magnitude sentinel and LevelThreshold-as-signed-duration — inferred, not confirmed.
- Reader code path for MovementCooldown (+1372) — not located this pass.
- CastCost (+1368) / StaminaCost (+1370) — CYCLE 7 consumer pass: on the cast-gate path both are
  **timed-charge gates** compared against running global clocks, NOT flat pool subtracts. The gate
  magnitude is server-authored — CAPTURE-PENDING. (0 across the sample.)
- Exact Name buffer length (24 vs 32 bytes) — byte at +32 is always 0 in valid records, boundary unpinned.
- SkillSort value 17 (present in sample, 7 records) — behaviour UNVERIFIED.
- The unused second int of each 8-byte hotbar slot pair — purpose UNVERIFIED.
- Per-skill activation-mode byte semantics (UI panel routing) — UNVERIFIED.
- Runtime recast-table layout beyond 240-slot indexing — not fully recovered.
- `skillcategory.scr` (a likely category→name cross-reference table) — not yet analyzed.
- All disk offsets are sample-verified against the committed `skills.scr` extract; the loader-read
  framing facts (1504+N×8, N@+1500, 8→12B expansion, 1508B runtime object, +0/+4 index keys) are
  **control-flow confirmed**. Runtime offsets not exercised by the loader/handlers are inferences from
  prior analysis. The wire-packet **routings and body sizes are control-flow confirmed**; no live
  network capture was available, so the wire-field **value semantics** remain capture/debugger-pending.
