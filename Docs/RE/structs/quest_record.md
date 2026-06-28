# Quest record layout (clean-room spec)

> **Verification banner.** verification: **control-flow-confirmed (static)** on doida.exe IDB SHA
> 263bd994, CYCLE 7 (2026-06-20); spec-audit pass (2026-06-24) confirmed no drift. The record
> stride (**4960 bytes / 0x1360**, on-disk == runtime byte-for-byte), the eligibility/availability
> gate fields and their evaluation order, the embedded objective sub-array base (record +0x68) and
> **240-byte objective sub-record stride**, the dialogue handle offsets (offer +0x54, turn-in +0x58),
> the `abandonable` flag (+0x1329), and the high-region gate fields (+0x1348 .. +0x135C) are all
> **CONFIRMED** at the loader, the eligibility evaluator, the requirement-notice renderer, and the
> quest-log detail renderer on `263bd994`. The spec-audit pass additionally confirmed: the
> step-ordinal lookup routing (`current_step_code` +0x065 mapped through a statically-dumpable word
> array), the AVAILABLE-tab populator's use of `prereq_chain_id` (+0x1348) as the chapter-match key
> bounded by the active-quest-range count global, and the ActorManager vtbl slot +72 call in the
> 5/73 grant path (see `specs/quests.md §7.2`).
> - **UNVERIFIED** — the full **interior layout of the 240-byte objective sub-record** (the
>   kill / collect / talk action-type plus its target NPC / item / count fields). The client does
>   **not** switch on an objective-action-type enum locally; that taxonomy is data-driven inside the
>   objective blocks and is **server-tracked**. Only two interior points of the objective sub-record
>   are located: a key word at sub-record +0x3E and a per-step reward-value array at sub-record +0x4C.
>   The step-ordinal word array contents (code → ordinal mapping) are also UNVERIFIED (statically
>   dumpable; deferred).
> - **capture/debugger-pending** — the literal integer values of the eligibility status states (they
>   are runtime-resolved globals initialised once at startup), and the exact wire byte offsets of the
>   completion reward list (the ≤10-entry reward record set the 5/73 completion verdict carries).
> - **ida_anchor:** f61f66a9ae0ec1e946105b2ecff76e8930cb1d1367df64e5688a5266f5ad9963 · **evidence:** [static-ida] · **conflicts:** none. The earlier
>   "3720-byte on-disk vs ~4960-byte runtime" stride split is **REFUTED** (see §1) — there is one
>   stride, 4960.
> - **ida_reverified:** 2026-06-20 (SHA 263bd994, CYCLE 7); spec-audit 2026-06-24; CYCLE 14 re-anchor: 2026-06-27
> - **CYCLE 14 re-anchor (f61f66a9, 2026-06-27):** 1 fact re-confirmed SAME (quest record single stride 4960/0x1360, on-disk==runtime; objective sub-array @+0x68, 240B stride; dialogue handles +0x54/+0x58; eligibility gates +0x1329/+0x1348/+0x1350/+0x1352/+0x1354/+0x1359/+0x135A/+0x135B/+0x135C).

Neutral, offset-model of the legacy client's **quest template record** — the fixed-size structure the
quest catalogue (`quests.scr`) is composed of and the same object the runtime keys by `quest_id`.
Promoted from dirty-room notes; rewritten, no decompiler identifiers, no binary addresses. Design
input for the **assets-parser-engineer** (`quests.scr` decoder) and the **core/domain engineer**
(quest eligibility evaluator, quest-log detail model). Behavioural context — the opcode flow, the
quest-log window, and the dialogue text sources — lives in `specs/quests.md`; this document is the
byte layout only.

> **Confidence tags:** `CONFIRMED` = control-flow-locked at the cited consumer (loader / evaluator /
> renderer) on `263bd994`; `UNVERIFIED` = field present but interior not individually decoded;
> `capture/debugger-pending` = needs a live capture or the runtime-resolved global values.

> **Text encoding:** all human-readable strings in the quest record (quest name, objective text) are
> **Korean CP949** (code page 949), null-terminated. Decoders must use CP949, not UTF-8.

> **Endianness:** all multi-byte fields are little-endian.

> **Offset convention:** every offset below is **record-relative** (relative to the start of the
> quest record). They are never binary addresses. Offsets inside an objective sub-record are written
> as "sub-record +0xNN" and are relative to the start of that 240-byte sub-record.

---

## 1. Record stride — single stride 4960 (0x1360)

The quest record is a **fixed 4960-byte (0x1360) structure**, and the on-disk record is **identical
to the runtime record byte-for-byte**. This is loader-proven on `263bd994`:

- The `quests.scr` loader reads **exactly 4960 bytes** per record from the VFS file into a buffer.
- It allocates a **4960-byte** runtime object for the record.
- It copies **4960 bytes** verbatim (a single fixed-size memory copy) from the disk buffer into the
  runtime object, then registers the object into the quest-id-keyed registry.

There is **no expansion, no padding, no field synthesis** between disk and runtime: the on-disk
record *is* the runtime record. All the high-region gate fields (+0x1329 .. +0x135C below) are
therefore **authored on-disk data**, not runtime scratch — they are read straight out of the loaded
record at their literal offsets.

> **The earlier "3720-byte on-disk" figure is REFUTED.** It came from a black-box VFS byte
> measurement that disagreed with the binary loader; per the ground-truth doctrine the loader wins.
> There is one stride, 4960, and no "missing 1240 bytes" — that delta was illusory.

A record slot is **empty when its leading `u16` quest id (+0) is 0**; the registry skips empties.

---

## 2. Top-of-record fields (identity, name, dialogue handles, step state)

| Offset | Type | Field | Confidence | Meaning |
|-------:|------|-------|------------|---------|
| +0x000 | u16 | `quest_id` | CONFIRMED | Quest id and registry map key. **0 = empty slot.** The evaluator reads `*(u16)` here. |
| +0x002 | u8 | `category` | CONFIRMED | Category / class byte. Used by the "same-category quest already active" eligibility scan and as the quest-grade key. |
| +0x003 | char[..] | `quest_name` | CONFIRMED | CP949 quest name, NUL-terminated. Read from `record + 3` by the list builders and the grade-label setter. Occupies the name buffer up to roughly +0x3F. |
| +0x040 | u8[6] | `step_codes` | CONFIRMED | Six step-code bytes. A raw step code is mapped through a **step-ordinal word array** (a statically-dumpable global, CONFIRMED as present; keyed by the step-code byte value) to a contiguous ordinal used for objective matching and the 1-based progress display. The meaning of each individual step code and the full contents of the step-ordinal array are **UNVERIFIED** (data-side; deferred static-dump task). |
| +0x054 | u32 | `offer_dialogue_handle` | CONFIRMED | Offer dialogue handle / chain reference A. The quest-log AVAILABLE-tab (detail mode 2) reaches the offer dialogue body through this handle. |
| +0x058 | u32 | `turnin_dialogue_handle` | CONFIRMED | Turn-in dialogue handle / chain reference B. The quest-log COMPLETABLE-tab (detail mode 1) reaches the turn-in dialogue body through this handle. (The ACTIVE-tab active dialogue + objective progress is reached through the objective sub-array at +0x68.) |
| +0x065 | u8 | `current_step_code` | CONFIRMED | Current step code. Mapped through the step-ordinal word array (keyed by this byte value) to a contiguous ordinal used to select the active objective sub-record (the sub-record whose `step_key` at sub-record +0x3E matches the ordinal) and to render the 1-based current-step counter. |
| +0x068 | objective_record[..] | `objectives` | CONFIRMED (base) / UNVERIFIED (interior) | Embedded objective sub-array. **240-byte stride** per objective sub-record. See §3. |

> The dialogue **handles** at +0x54 / +0x58 are references into the NPC-script dialogue store, not
> inline text; the 6 dialogue body lines per quest are fetched from there (see `specs/quests.md §17`).

---

## 3. Embedded objective sub-array (record +0x68, 240-byte stride)

The objective sub-array begins at record **+0x68** and is iterated with a **240-byte stride** per
objective sub-record. Only two interior points of each sub-record are located; the rest of the
240-byte interior is **UNVERIFIED**.

| Sub-record offset | Type | Field | Confidence | Meaning |
|------------------:|------|-------|------------|---------|
| sub-record +0x3E | u16 | `step_key` | CONFIRMED | Key word matched against the mapped step ordinal (from `current_step_code`, §2). The ACTIVE-tab detail renderer scans the sub-array for the sub-record whose `step_key` equals the mapped ordinal — that is the *current* objective. |
| sub-record +0x4C | u32[..] | `step_reward_values` | CONFIRMED | Per-step reward-value array, indexed as **sub-record +0x4C + 4·(step + 1)** (i.e. element `(step + 1)`). When non-zero, the value is rendered on the objective progress line via message id 73007. |
| (rest of the 240 bytes) | — | objective interior | **UNVERIFIED** | Expected to carry the objective **action type** (kill / collect / talk / reach) and its target NPC id / item id / count. The **client does not switch on an objective-action-type enum locally** — that taxonomy is data-driven inside these blocks and is server-tracked. Needs a dedicated struct decode of the 240-byte sub-record. |

> The per-step reward value's **kind** (EXP vs points vs item id) is **UNVERIFIED**. The client only
> displays it; reward granting is server-side.

---

## 4. High-region eligibility / availability gate fields

The high region of the record (from +0x1329) holds the **authored eligibility gate thresholds**. The
eligibility evaluator reads these at their literal offsets to grade each available quest into one of
~13 status states (the status-state integers themselves are runtime-resolved globals —
**capture/debugger-pending**). All offsets below are **CONFIRMED** at the evaluator and the
requirement-notice renderer.

| Offset (hex / dec) | Type | Field | Confidence | Meaning |
|-------------------:|------|-------|------------|---------|
| +0x1329 / 4905 | u8 | `abandonable_flag` | CONFIRMED | Give-up gate. The abandon-confirm panel opens (and a give-up action can be sent) **only when this flag is set**; otherwise the window shows a "cannot abandon" notice. |
| +0x1348 / 4936 | u32 | `prereq_chain_id` | CONFIRMED | Prerequisite-chain / chapter id. The eligibility evaluator compares it against the global chapter-progress state. The AVAILABLE-tab populator iterates the quest registry — bounded by the **active-quest-range count global** (caps iteration to the active range) — selecting records whose `prereq_chain_id` matches the selected chapter index; matched records contribute a row to the AVAILABLE display list (cap 10 rows). Also read by the AVAILABLE-tab detail renderer (mode 2) to confirm the chapter match before populating the detail panel. |
| +0x1350 / 4944 | u16 | `min_level` | CONFIRMED | Minimum character level. The evaluator returns "too low" when the character level word is below this. |
| +0x1352 / 4946 | u16 | `max_level` | CONFIRMED | Maximum character level. The evaluator returns "too high" when the character level word exceeds this. |
| +0x1354 / 4948 | u8[..] | `accepted_flags` | CONFIRMED | Accepted / availability flags, indexed (by an active-quest index). A zero at the indexed slot means "not available to this character slot". |
| +0x1359 / 4953 | u8 | `class_race_mask` | CONFIRMED | Class / race **bitmask**. Observed values 1 / 2 / 4 are powers of two — a quest can be open to several classes. The evaluator matches it against the character class. |
| +0x135A / 4954 | u8 | `secondary_stat_min` | CONFIRMED | Secondary-stat minimum, graded as a tier 1..7. |
| +0x135B / 4955 | u8 | `secondary_stat_max` | CONFIRMED | Secondary-stat maximum, tier 1..7. |
| +0x135C / 4956 | u8 | `tertiary_stat_bound` | CONFIRMED | Tertiary-stat bound (single threshold). |

---

## 5. Eligibility gate-chain order

The eligibility evaluator looks up the record by `quest_id`, then checks the gates **in this exact
order** and returns the first one that fails (each maps to a status state and, on the requirement
panel, to a notice message id — see `specs/quests.md §2.2`). If every gate passes, the quest is
**available**.

1. **record not found** → invalid / no such quest.
2. **min level** — character level below `min_level` (+0x1350) → too low.
3. **max level** — character level above `max_level` (+0x1352) → too high.
4. **accepted slot** — the indexed `accepted_flags` (+0x1354) entry is 0 → not available to this slot.
5. **class / race** — `class_race_mask` (+0x1359) does not match the character class → wrong class/race.
6. **secondary-stat min** — character secondary stat below `secondary_stat_min` (+0x135A) → too low.
7. **secondary-stat max** — character secondary stat above `secondary_stat_max` (+0x135B) → too high.
8. **tertiary-stat** — character tertiary stat fails `tertiary_stat_bound` (+0x135C) → fails.
9. **same-category active** — a quest of the same `category` (+0x002) is already active (scan of the
   active-id table) → a same-category quest is already active.
10. **same-id active** — this exact `quest_id` is already active (scan of a second active range) →
    already active.
11. **prerequisite-chain** — `prereq_chain_id` (+0x1348) compared against chapter progress → otherwise
    **available** (offerable / next-in-chain).

> The active-id scans (gates 9 and 10) read two adjacent runtime `u16[]` active-quest scratch tables;
> those tables are runtime state, not part of the quest record. The server-authored magnitudes the
> level / stat gates compare against are quest-data values; the *gate fields and their order* are the
> client-side CONFIRMED facts.

---

## 6. Cross-references

| Spec | Relationship |
|---|---|
| `specs/quests.md` | The behavioural quest spec — opcode flow (2/28, 2/152, 5/68, 5/73), the quest-log window, the eligibility status-state and requirement-notice taxonomies, and the two dialogue-text sources. This struct doc is its byte-layout companion. |
| `formats/config_tables.md` | The wider quest-data table family (`quests.scr` catalogue framing, `npc.scr`, `autoquestion_cl.scr`, `discript.sc`). |
| `structs/npc.md` / `structs/actor.md` | The NPC descriptor whose classifier fields drive the NPC-interaction dispatcher that opens quest dialogs. |

- **Glossary:** see `Docs/RE/names.yaml`
- **Provenance:** see `Docs/RE/journal.md`
