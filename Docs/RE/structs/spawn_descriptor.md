# SpawnDescriptor — full 880-byte field layout (clean-room spec)

> **Verification banner.**
> - **confirmed** — the **880-byte (0x370) total size** (every spawn does a fixed-size 0x370
>   byte-copy; the char-select per-slot read uses a 0x370 field length; the preview lineup uses an
>   880-byte stride; the 5-slot scratch is zero-cleared as 5×0x370 = 0x1130 bytes), the `name`
>   char[17] layout, the `current_xp` int64, the model-class inputs (`appearance_variant` @ +0x2C,
>   `internal_class` @ +0x34), the world coordinate floats (+0x4C / +0x50), the `equip_ref_table`
>   (20×16 @ +0x58, each leading dword = a worn-item id), and `motion_state_byte` (+0x2EE) are
>   **control-flow confirmed**.
> - **static-hypothesis** — single-site inferences (`level` as a clean u16 @ +0x3A — confirmed only
>   for the *display* read path; `inner_event_code`, `current_xp` not re-confronted this pass;
>   `in_combat_flag` @ +0x32C lands at Actor +0x3A0 but was not re-touched).
> - **capture/debugger-pending** — every *wire VALUE meaning*; the `level`/state bytes at +0x38/+0x39
>   (written by the runtime-table-dispatched 5/53 vitals handler, null at static time); the
>   `scenario_state` dword (+0x48); and the `mob_pair_partner_id` (+0x354), not re-witnessed.
> - **ida_reverified:** 2026-06-16  **ida_anchor:** 263bd994  **evidence:** [static-ida]
> - **conflicts:** no hard conflicts. Two soft divergences carried as open items — (a) the `level`
>   byte boundary (clean u16 @ +0x3A for display vs. straddling +0x38/+0x39 on the wire), and
>   (b) the **equipment view**: the IDB exercised the **20×16 id table at SD +0x58**; the older
>   "8-slot 16-byte array at SD +0x54 (4/149 path)" was **not** re-confronted and remains
>   unreconciled.

Neutral, offset-model extension of `actor.md`'s SpawnDescriptor section. Promoted from a dirty-room
note; rewritten, no decompiler identifiers, no binary addresses. This document is the design input
for the **network-protocol-engineer** decoding the 880-byte descriptor carried by 5/3 CharSpawn and
the 3/1 CharacterList slot records, and for the **domain engineer** consuming the equipment / stat /
buff sub-fields.

> **Offsets are relative to the start of the SpawnDescriptor (SD).** When the descriptor is copied
> into a live actor on spawn, every field lands at `actor_offset = SD_offset + 0x74` (see
> `actor.md`). This document gives **SD-relative** offsets only.
>
> **Total size: 880 bytes (0x370).** Confirmed by the fixed-size 0x370 copy the spawn handlers
> perform, by the 0x370-length char-select per-slot read, by the 880-byte preview stride, and by the
> 5-slot scratch being zero-cleared as 5×0x370 bytes.
>
> **Confidence tags:** `CONFIRMED` = control-flow proven / cross-checked at multiple use sites;
> `LIKELY` = single consistent use site, plausible; `UNVERIFIED` = inferred / boundary not pinned;
> needs a capture. Per the campaign tier rule, *routing, sizes and offsets* may be CONFIRMED from
> control flow, but the *VALUE semantics* of any received byte stay capture/debugger-pending.

## Wire framing — the descriptor is the inner block of a larger frame

The 880-byte SpawnDescriptor is **not** the whole packet — it is the inner block the spawn handlers
copy into the live Actor at +0x74. The recovered wire shapes:

- **5/3 CharSpawn** — the on-wire frame is **908 bytes (0x38C)**. The handler reads the larger frame
  into a stack buffer whose leading dwords are a prefix (a spawn-kind / `sort` word + the `id` dword)
  and whose trailing bytes (title byte, anim-variant byte, spawn-extra byte, …) are written to live
  Actor fields **separately** from the 0x370 copy. [confirmed size] / exact prefix/suffix field order
  **capture-pending**.
- **5/1 ActorSpawnExtended** — the on-wire frame is **912 bytes (0x390)**, same shape (prefix + the
  0x370 descriptor + suffix). Its `sort` selector branches 1/2/3 and walks the 20×16 equip-id table
  to re-attach worn items. [confirmed size] / suffix order **capture-pending**.
- **3/1 CharacterList and 3/4 SceneEntityUpdate per-slot record** — each present slot is exactly
  **`descriptor(0x370) + stats_block(0x60 = 96) + selectable_flag(1) + timing_word(4)`**, read in
  that order, iterated over a 1-byte slot mask (bit *i* = slot *i* present), **max 5 slots**. The
  scratch lives in the network-handler singleton (per-slot descriptors 5×0x370, per-slot stats
  5×0x60, per-slot flags 5×1, per-slot timing 5×4). The 96-byte stats block's interior fields are
  **not** mapped (its consumer is reached by base+index arithmetic; the char-select display reads
  only descriptor fields, not the 96-byte block) → **capture-pending**. [confirmed sizes/order].

> **Per-slot ordering note for the network engineer.** A 3/1 / 3/4 list slot is NOT just a
> descriptor — after each 880-byte descriptor comes a 96-byte stats block, a 1-byte selectable flag,
> and a 4-byte timing word, before the next slot. Decode the slot mask first, then read each present
> slot as that four-part record.

---

## Top-level field table

| SD offset | Size | Type     | Field                 | Conf       | Meaning |
|-----------|------|----------|-----------------------|------------|---------|
| +0x00     | 17   | char[17] | `name`                | CONFIRMED  | NUL-terminated actor name, up to 16 chars + NUL. |
| +0x11     | 3    | —        | (pad)                 | LIKELY     | Alignment to +0x14. |
| +0x14     | 2    | uint16   | `inner_event_code`    | LIKELY     | Internal event code; the value 55 gates a "reset to default motion" visual path. |
| +0x16     | 12   | char[12] | `gap_16`              | UNVERIFIED | Mostly opaque up to the discriminator below. |
| +0x22     | 1    | uint8    | `name_clone_discriminator` | CONFIRMED | Display-name discriminator for the player-clone path: read in the case-1 spawn branch and fed (with the +0x34 class word) into the display-name rebuild for same-named clones. (Also feeds the slot-14 visible-gear catalog key as a high decimal digit — see `actor.md`.) |
| +0x23     | 1    | char     | `gap_23`              | UNVERIFIED | Alignment to +0x24. |
| +0x24     | 8    | int64    | `current_xp`          | CONFIRMED  | Current experience points (signed 64-bit). |
| +0x2C     | 1    | uint8    | `appearance_variant`  | CONFIRMED  | Body / gender appearance variant. **The `variant` argument of the model-class formula** — the spawn factory passes this byte to the model-class resolver, and the char-select slot-valid gate reads it. (Aliased `body_variant` in earlier drafts.) |
| +0x2D     | 7    | char[7]  | `gap_2D`              | UNVERIFIED | Padding / unmapped, up to +0x34. |
| +0x34     | 2    | uint16   | `internal_class`      | CONFIRMED  | Internal class word. **For PCs the class id {1,2,3,4}** and the `class` argument of the model-class formula (the spawn factory passes byte +0x34 to the resolver; the preview seeds it from a 1..4 switch). For mobs the same word keys the model-template lookup. |
| +0x36     | 2    | uint16   | `anim_class_word`     | CONFIRMED (used as a gate) / UNVERIFIED (meaning) | Read as an occupied-slot gate by the char-preview path and as a name-assignment gate in the PC spawn branch (Actor +0xAA). Exact meaning (class vs. animation variant) is capture-pending. (Aliased `gap_36` in earlier drafts.) |
| +0x38     | 1    | uint8    | `level_state`         | capture-pending | A level/state byte written by the runtime-table-dispatched 5/53 vitals handler (not statically reachable). Its relationship to the `level` u16 below is unresolved — `level` may straddle this byte. See caveat. |
| +0x39     | 1    | uint8    | `secondary_level`     | capture-pending | Secondary level/state byte, written by the same 5/53 path. |
| +0x3A     | 2    | uint16   | `level`               | LIKELY (display) | Character level (u16). **New IDB evidence:** the char-select **display** path reads a clean u16 at +0x3A and prints it as the level number — strongly supporting a clean u16 here for the display read. The *wire* boundary vs. +0x38/+0x39 is still capture-pending (the 5/53 writes are runtime-dispatched). See caveat. |
| +0x3C     | 4    | uint32   | `current_hp`          | CONFIRMED  | Current hit points. Written by the 5/53 vitals path. |
| +0x40     | 4    | uint32   | `current_mp`          | CONFIRMED  | Current mana / ki. |
| +0x44     | 4    | uint32   | `current_stamina`     | CONFIRMED  | Current stamina. |
| +0x48     | 4    | uint32   | `scenario_state`      | capture-pending | Four bytes immediately before the world coordinates; present in the wire data, meaning unverified. Not re-touched this pass. |
| +0x4C     | 4    | float    | `world_x`             | CONFIRMED  | World X spawn origin (the spawn handler seeds the live position from this float). **Doubles as a move-speed override** for the special sorts 15/17 via a union overlay (the factory reuses Actor +0xC0 = SD +0x4C). |
| +0x50     | 4    | float    | `world_z`             | CONFIRMED  | World Z spawn origin. World Y is forced to 0.0 on spawn. |
| +0x58     | 320  | slot[20] | `equip_ref_table`     | CONFIRMED  | **Visible-gear / equipment reference table: 20 entries × 16 bytes**, walked 20× with a 16-byte stride in both the 5/1 spawn-extended handler and the char-preview lineup (each leading dword passed to "find actor by id"). Each entry's **leading dword is a worn-item actor id**; the remaining 12 bytes are unverified. Spans SD +0x58 .. SD +0x197. **This is the equipment view the IDB exercised** — see the reconciliation note below. |
| +0x74     | 2    | uint16   | `server_class`        | LIKELY     | Server-assigned class id (martial-arts style discriminator). Falls inside the equip-table range — see reconciliation note. |
| +0x76     | 6    | char[6]  | `gap_76`              | UNVERIFIED | Unmapped. |
| +0x7C     | 1    | uint8    | `race_or_skin`        | LIKELY     | Race / skin variant byte. |
| +0x7D     | 67   | char[67] | `visual_data`         | UNVERIFIED | Equipment-visual / appearance region. |
| +0xC0     | 4    | uint32   | `on_hit_item_id`      | LIKELY     | Item actor id for an on-hit effect (weapon reference). |
| +0xC8     | 8    | char[8]  | `gap_C8`              | UNVERIFIED | Unmapped. |
| +0xD0     | 4    | uint32   | `on_hit_mp_item_id`   | LIKELY     | Item actor id for an MP-on-hit effect. |
| +0xD4     | 600  | char[600]| `stat_equip_block`    | partial    | Large equipment / stat / buff block. Confirmed sub-fields below (~40% mapped). |
| +0x304    | 1    | uint8    | `buff_block_byte_304` | CONFIRMED (offset) / capture-pending (meaning) | Written PC-side from a wire byte on the 5/1 spawn-extended path (lands at Actor +0x378). A newly-mapped point inside the otherwise-opaque buff block. |
| +0x32C    | 4    | uint32   | `in_combat_flag`      | static-hypothesis | Combat state flag; lands at Actor +0x3A0. Not re-touched this pass. (Lies inside `stat_equip_block`.) |
| +0x354    | 4    | int32    | `mob_pair_partner_id` | capture-pending | Paired-mob partner actor id, for mobs that spawn in pairs. (Inside the tail region.) The re-checked companion-propagation path dereferences a separate Actor pointer (Actor +0x6F4), not this id, so the pair-id wire field was **not** re-witnessed this pass. |

> **Note on `world_x` / `world_z` overlap with the equipment table.** `actor.md` adopts +0x4C/+0x50
> as the live world coordinates; the IDB-exercised `equip_ref_table` begins at +0x58 (after the two
> coordinate floats and a 4-byte tail at +0x54). The older +0x44/+0x48 coordinate view from a
> different base point is superseded.

### Caveat — `level` byte boundary (soft conflict, carried)

Two read paths disagree on whether `level` is a clean u16 at +0x3A or a value straddling the state
byte(s) at +0x38/+0x39. **New IDB evidence (display path):** the char-select display reads a clean
u16 at +0x3A and renders it. That is a single (display) read site — it does **not** prove the wire
encoding, since the 5/53 vitals writes at +0x38/+0x39 are runtime-table-dispatched and not
statically reachable. Status: **resolved-for-display [static-hypothesis], wire boundary still
[capture-pending]**. Disambiguate against a real CharSpawn / 5-53 capture for a known-level
character before hard-coding a 16-bit `level`. Until then treat +0x38..+0x3B as a 4-byte
level/state group on the wire.

---

## Equipment table — two unreconciled views (soft conflict, carried)

There are **two recovered views of the equipment region**, from different code paths, and they have
**not** been reconciled byte-for-byte. State both honestly:

### View A (IDB-exercised this pass) — `equip_ref_table`, 20×16 @ SD+0x58 — CONFIRMED

The spawn-extended (5/1) handler and the char-preview lineup both walk a **20-entry × 16-byte id
table beginning at SD+0x58**, with a 16-byte stride, passing each entry's **leading dword to "find
actor by id"** to re-attach the worn items. This is the equipment view the control flow actually
touches; the renderer maps the per-entry gid to `data/char/skin/g{gid}.skn` for overlay slots
`{3,4,6,2,11,14}` (see `actor.md`). Spans SD+0x58 .. SD+0x197. **Confidence: CONFIRMED** for the
table base, stride, entry count, and that the leading dword is an actor id. The remaining 12 bytes
per entry are UNVERIFIED.

| Entry | SD offset | Size | Leading dword |
|-------|-----------|------|---------------|
| 0     | +0x58     | 16   | worn-item actor id |
| 1     | +0x68     | 16   | worn-item actor id |
| …     | …         | 16   | … (20 entries total, +0x10 stride) |
| 19    | +0x178    | 16   | worn-item actor id |

### View B (NOT re-confronted this pass) — 8×16 item-slot array @ SD+0x54 — capture-pending

A prior recovery, from the item-panel slot-update path (opcode 4/149, chunk-type 0 = equipment),
read an **8-record × 16-byte array beginning at SD+0x54** (write bounded at SD+0xD4 = `0x54 +
8*0x10`), where each record is an `ItemSlotRecord` per `item.md`. This view was **not** re-exercised
in this pass.

> **Reconciliation status (carried open).** View A (20×16 @ +0x58, id table) is the one the spawn /
> preview control flow exercises and is CONFIRMED. View B (8×16 @ +0x54, 4/149 slot-update path) was
> not re-confronted. The named scalar fields `server_class` (+0x74), `race_or_skin` (+0x7C),
> `visual_data` (+0x7D), `on_hit_item_id` (+0xC0), and `on_hit_mp_item_id` (+0xD0) all fall **inside**
> both views' ranges and were recovered from yet other paths. **Do not assume the views are
> simultaneously valid byte-for-byte.** A real CharSpawn / 4-149 capture is required to settle which
> path populates which bytes on the wire. Until then: treat View A (the 20×16 id table @ +0x58) as
> the authoritative spawn-time equipment layout, and the others as pending reconciliation.

---

## stat_equip_block sub-fields (SD+0xD4 .. SD+0x32B, 600 bytes)

Only confirmed sub-offsets are listed; the block is roughly 40% mapped. All offsets are
SD-relative.

| SD offset | Size | Type   | Field              | Conf       | Meaning |
|-----------|------|--------|--------------------|------------|---------|
| +0x194    | 4    | int32  | `buff_value`       | LIKELY     | Buff magnitude (the aura percentage value used in the max-HP/MP formulas; see `stats.md`). |
| +0x1AA    | 1    | uint8  | `buff_kind`        | LIKELY     | Buff-category / aura-kind discriminator (HP aura = 1, MP aura = 2 per `stats.md`). |
| +0x1F0    | 2    | uint16 | `skill_state_word` | LIKELY     | Skill / buff state flags received on spawn. A high-level state discriminator, not a per-skill cooldown. |
| +0x304    | 1    | uint8  | `buff_block_byte_304` | CONFIRMED (offset) / capture-pending (meaning) | Written PC-side from a wire byte on the 5/1 spawn-extended path (Actor +0x378). Newly mapped this pass; protocol meaning needs a capture. |
| +0x305    | 1    | uint8  | `bag_slots_count`  | LIKELY     | Number of bag slots; used by the inventory path to bound the inventory-slot array. |
| +0x32C    | 4    | uint32 | `in_combat_flag`   | static-hypothesis | Combat state flag (also listed at top level); lands at Actor +0x3A0. Not re-touched this pass. |

> The remaining ~60% of `stat_equip_block` holds equipment / stat / buff tables that are not yet
> individually mapped. The network engineer should treat the unmapped span as an opaque blob inside
> the 880-byte descriptor. Note that, per the stat-formula analysis (`stats.md`), the player's stat
> bonuses are **not** read from this block directly — they come from the separately-instanced item
> actors referenced by the equipment id table. This block carries the raw equipment-set definition
> (item ids + slot assignments) the server sends on spawn, plus buff/combat state.

---

## Tail region (SD+0x330 .. SD+0x36F, 64 bytes)

Mostly unmapped. One confirmed sub-field:

| SD offset | Size | Type  | Field                 | Conf   | Meaning |
|-----------|------|-------|-----------------------|--------|---------|
| +0x354    | 4    | int32 | `mob_pair_partner_id` | LIKELY | Partner actor id for paired-mob spawns. |

---

## Open questions

- **`level` byte boundary** (+0x38/+0x39 vs +0x3A) — display path reads a clean u16 @ +0x3A
  [static-hypothesis]; the wire boundary is [capture-pending] (the 5/53 writes are runtime-dispatched).
  Needs a known-level CharSpawn / 5-53 capture.
- **Equipment view A vs. B** — the 20×16 id table @ +0x58 (View A) is CONFIRMED and IDB-exercised; the
  8×16 array @ +0x54 (View B, 4/149 path) was not re-confronted and is unreconciled; needs a capture to
  determine which path populates which bytes on the wire.
- **`stat_equip_block`** — ~60% unmapped; one new interior byte (+0x304) mapped this pass; needs a
  dedicated pass with capture evidence.
- **96-byte char-select stats block** — confirmed to exist and be 0x60 bytes per slot in the 3/1 / 3/4
  per-slot record, but its **interior fields are unmapped** (its consumer is reached by base+index
  arithmetic; the display path reads only descriptor fields). Capture-pending.
- **`scenario_state` (+0x48)**, **`mob_pair_partner_id` (+0x354)**, **`in_combat_flag` (+0x32C)** — not
  re-witnessed this pass; carried as static-residual / capture-pending.
- **Wire prefix/suffix order** of the 5/3 (908B) and 5/1 (912B) frames around the inner 0x370
  descriptor — the field order of the leading prefix and the trailing per-Actor bytes is
  capture-pending.
- All offsets are static control-flow inferences; no live capture was available to confirm any
  *wire VALUE meaning* — those stay capture/debugger-pending throughout.
</content>
