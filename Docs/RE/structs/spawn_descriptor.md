# SpawnDescriptor — full 880-byte field layout (clean-room spec)

> **Verification banner.**
> - **confirmed** — the **880-byte (0x370) total size** (every spawn does a fixed-size 0x370
>   byte-copy; the char-select per-slot read uses a 0x370 field length; the preview lineup uses an
>   880-byte stride; the 5-slot scratch is zero-cleared as 5×0x370 = 0x1130 bytes), the `name`
>   char[17] layout, the `current_xp` int64, the model-class inputs (`appearance_variant` @ +0x2C,
>   `internal_class` @ +0x34), the world coordinate floats (+0x4C / +0x50), the **HP qword** (+0x3C /
>   +0x40 = ONE 64-bit HP, see the HP-qword note below), the `equip_ref_table`
>   (20×16 @ +0x58, each leading dword = a worn-item id), and `motion_state_byte` (+0x2EE) are
>   **control-flow confirmed**.
> - **static-hypothesis** — single-site inferences (`level` as a clean u16 @ +0x3A — confirmed only
>   for the *display* read path; `inner_event_code`, `current_xp` not re-confronted this pass;
>   `in_combat_flag` @ +0x32C lands at Actor +0x3A0 but was not re-touched).
> - **capture/debugger-pending** — every *wire VALUE meaning*; the `level`/state bytes at +0x38/+0x39
>   (written by the runtime-table-dispatched 5/53 vitals handler, null at static time); the
>   `scenario_state` dword (+0x48); and the `mob_pair_partner_id` (+0x354), not re-witnessed.
> - **ida_reverified:** 2026-06-24 (actor-world audit); prior 2026-06-22 (CYCLE 11 prefix/trailer sharpening)  **ida_anchor:** 263bd994  **evidence:** [static-ida]
>   **layout/offsets:** confirmed · **value-semantics:** capture/debugger-pending.
> - **readiness:** IMPLEMENTATION-READY for the C# rebuild (control-flow-confirmed against IDB SHA 263bd994); items explicitly tagged debugger-pending / capture-pending / RD-* are NON-blocking runtime residuals to confirm later.
> - **CYCLE 9 Phase 3.1 (2026-06-21, IDB SHA 263bd994):** static re-confirmation of the two model-class
>   inputs — `appearance_variant` (u8 @ +0x2C, read zero-extended) and `internal_class` (u16 @ +0x34,
>   read sign-extended). HIGH confidence: confirmed at **four independent read sites**, and the
>   char-select preview path and the in-world spawn path feed the **same** two offsets to the **same**
>   actor factory. A port-side decode that reads `internal_class == 0` at +0x34 for a real slot is a
>   **descriptor-base alignment bug in the decoder, NOT an offset error** — proven because the `level`
>   u16 @ +0x3A in the same descriptor copy decodes correctly. The +0x2E occupied-gate vs +0x34
>   class-input attribution (the old CAMPAIGN-16 discrepancy note) is now **reconciled, not conflated**
>   (see the +0x2C / +0x34 / +0x36 rows).
> - **re-verified against doida.exe IDB SHA 263bd994, CYCLE 7 (2026-06-20)** — added the
>   "3/1 per-slot tail (CYCLE 7)" cross-ref note below (the per-slot SlotFlag at record +0x3D0 and the
>   per-slot flags word at record +0x3D1 with its single client-consumed billing bit); existing
>   descriptor offsets and the level-boundary CONFLICT untouched.
> - **conflicts:** no hard conflicts. **One hard correction applied this pass (binary wins):** SD
>   +0x3C/+0x40 are ONE 64-bit HP qword — what an earlier draft labelled `current_mp` @ SD +0x40 is
>   the **HP-HIGH dword**, not MP (the MP/stamina-class vital is the separate dword at SD +0x44). See
>   the HP-qword note. Two soft divergences carried as open items — (a) the `level` byte boundary
>   (clean u16 @ +0x3A for display vs. straddling +0x38/+0x39 on the wire), and (b) the **equipment
>   view**: the IDB exercised the **20×16 id table at SD +0x58**; the older "8-slot 16-byte array at
>   SD +0x54 (4/149 path)" was **not** re-confronted and remains unreconciled.

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

### 3/1 per-slot tail — SlotFlag (+0x3D0) and the flags word (+0x3D1) (CYCLE 7)

The two trailing fields of each 3/1 / 3/4 per-slot record (the "selectable flag" + "timing word"
above) were resolved this pass. Offsets are **record-relative** (relative to the start of the
four-part slot record, NOT SD-relative):

| Record offset | Width | Type | Field | Conf | Meaning |
|---------------|-------|------|-------|------|---------|
| +0x3D0 | 1 | uint8 | `slot_flag` (occupied / UI-facing marker) | CONFIRMED | The per-slot occupied / availability marker. **Enter Game requires this byte == 0.** The same byte also multiplexes the per-slot UI front/back facing state (the char-select window sets distinct values for face-back / face-front / deletion-pending / freshly-created). It is wire-read at load time; it is **not** a rename or relation flag. |
| +0x3D1 | 4 | uint32 (le) | `slot_flags` / `billing_state_flags` (flags word) | CONFIRMED (bit 0) / wire-reserved (bits 1..31) | Per-slot flags word — **not** a timestamp. The **only** client-consumed bit is **bit 0 (mask 0x1) = the character's billing / premium state bit**, copied verbatim into the in-game player panel at Enter Game. Bits 1..31 are received on the wire but never tested by the client (server-reserved). |

> **No rename-cooldown field in the 3/1 record.** There is no dedicated client-side rename-cooldown
> bit or byte anywhere in the per-slot record. The rename command is gated only by text validation
> (banned-word check, unchanged-name compare, length >= 2, in-flight guard) plus the occupied marker;
> the cooldown is **server-enforced** and surfaced only as an error code in the 3/6 rename result.
>
> **Caveat — the descriptor `state_byte` is separate.** The deletion-pending / locked state that
> blocks Enter Game lives in the **descriptor** `state_byte` (descriptor +0x38, value 7), not in the
> per-slot tail above. Do not confuse the descriptor-relative +0x38 with the record-relative +0x3D0.
> The account-wide billing-active state is also separate (a single boolean in the account singleton,
> set by 1/17 and read independently) — it is **not** a bit inside the 3/1 record.

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
| +0x2C     | 1    | uint8    | `appearance_variant`  | CONFIRMED (HIGH, CYCLE 9 P3.1) | Body / gender appearance variant. **The `variant` argument of the model-class formula** (read zero-extended) — the spawn factory passes this byte to the model-class resolver, and the char-select slot-valid gate reads it. Also doubles as the per-slot occupied/body marker. (Aliased `body_variant` in earlier drafts.) **CYCLE 9 P3.1:** re-confirmed at the shared spawn factory + the create-form synthesizers; distinct from the +0x2E occupied gate. |
| +0x2D     | 7    | char[7]  | `gap_2D`              | UNVERIFIED | Padding / unmapped, up to +0x34. |
| +0x34     | 2    | uint16   | `internal_class`      | CONFIRMED (HIGH, CYCLE 9 P3.1) | Internal class word (read sign-extended). **For PCs the class id {1,2,3,4}** and the `class` argument of the model-class formula (the spawn factory passes the +0x34 word to the resolver; the preview seeds it from a 1..4 switch). For mobs the same word keys the model-template lookup. **CYCLE 9 P3.1:** static-confirmed at four independent read sites; the in-world spawn path agrees. **Port action item — alignment bug, not a layout error:** a decoder reading `internal_class == 0` here for a real slot is using a misaligned descriptor base (the tell: `level` @ +0x3A in the same block decodes correctly); re-base the per-slot descriptor at the slot record's first descriptor byte and u16 @ +0x34 yields {1,2,3,4}. Live wire VALUE byte-proof = debugger-pending; offset = static-confirmed. |
| +0x36     | 2    | uint16   | `anim_class_word`     | CONFIRMED (used as a gate) / UNVERIFIED (meaning) | Read as an occupied-slot gate by the char-preview path and as a name-assignment gate in the PC spawn branch (Actor +0xAA). Exact meaning (class vs. animation variant) is capture-pending. (Aliased `gap_36` in earlier drafts.) **CAMPAIGN-16 / CYCLE 9 P3.1 reconciliation:** the char-select *preview* occupied gate is the u16 at descriptor **+0x2E** (read by the preview-lineup occupancy test), and this **+0x36** word is a **separate** PC-spawn name-assignment gate — they are two distinct gates, not a mislabel. Both are also distinct from the model-class input at **+0x34**. So the descriptor near +0x2C..+0x36 carries four separate roles: variant/occupied byte (+0x2C), preview occupied gate (+0x2E), class input (+0x34), name-assign gate (+0x36) — do not conflate them. The C# slot model is unaffected (it gates on the 3/1 selectable-flag, not this word). |
| +0x38     | 1    | uint8    | `level_state`         | capture-pending | A level/state byte written by the runtime-table-dispatched 5/53 vitals handler (not statically reachable). Its relationship to the `level` u16 below is unresolved — `level` may straddle this byte. See caveat. |
| +0x39     | 1    | uint8    | `secondary_level`     | capture-pending | Secondary level/state byte, written by the same 5/53 path. |
| +0x3A     | 2    | uint16   | `level`               | LIKELY (display) | Character level (u16). **New IDB evidence:** the char-select **display** path reads a clean u16 at +0x3A and prints it as the level number — strongly supporting a clean u16 here for the display read. The *wire* boundary vs. +0x38/+0x39 is still capture-pending (the 5/53 writes are runtime-dispatched). See caveat. |
| +0x3C     | 8    | int64    | `current_hp` (qword)  | CONFIRMED  | **Current hit points — a single 64-bit HP qword spanning +0x3C..+0x43.** +0x3C is the HP-low dword and +0x40 is the **HP-HIGH dword** (NOT MP — see the HP-qword note). Written and clamped as one qword by the 5/53 vitals path. |
| +0x44     | 4    | uint32   | `vital_b`             | CONFIRMED  | The MP / stamina-class vital — the **separate** dword after the HP qword (lands at Actor +0xB8, clamped on its own). MP-vs-stamina meaning `[capture/debugger-pending]`. (Earlier drafts mislabelled +0x40 as `current_mp` and this as `current_stamina`; corrected — the qword pushes both vitals down one dword.) |
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

### Per-opcode WIRE offset of `world_x` / `world_z` (do NOT use one rule for all three)

`world_x` @ SD +0x4C and `world_z` @ SD +0x50 are **descriptor-relative** and CONFIRMED. But the
descriptor sits at a per-handler **prefix offset** inside the frame, so the on-the-wire offset of the
world floats differs per opcode — there is **no single "+0x54/+0x58" rule** for all three:

| Opcode | Frame size | Prefix | Descriptor base in frame | `world_x` WIRE offset | `world_z` WIRE offset |
|--------|-----------|--------|--------------------------|-----------------------|-----------------------|
| 5/3 SmsgCharSpawn | 908 (0x38C) | 8 (sort u32 @+0, id u32 @+4) | frame +0x08 | **+0x54** | **+0x58** |
| 4/4 tag-1/2/3 record | 892 (0x37C) | 8 (id u32 @+0, kind/field bytes @+4/+5, 2 pad) | record +0x08 | **+0x54** | **+0x58** |
| 5/1 SmsgActorSpawnExtended | 912 (0x390) | **12** (sort @+0, id @+4, title/relation bytes @+8/+9/+0xA, pad) | frame +0x0C | **+0x58** | **+0x5C** |

The 4-byte-longer 5/1 prefix shifts its world floats to wire **+0x58 / +0x5C**. Promote the WIRE
offset **per opcode** into the packet specs; promote SD +0x4C / +0x50 into this file.

> **CYCLE 11 prefix/trailer sharpening (2026-06-22).** The per-opcode prefix and trailer fields are
> now sharpened in the respective packet specs:
>
> - **5/3 SmsgCharSpawn (8-byte prefix):** Sort u32 at frame +0x00 (low byte is the real sort selector;
>   1=PC, 2=Mob/NPC), ActorId u32 at frame +0x04. The 20-byte trailer at frame +0x37C carries:
>   trailer[0] = clone/name-rebuild byte; trailer[1..17] = 17-byte secondary CP949 name region;
>   trailer[18] = relation byte; trailer[19] = tail byte. See `packets/5-3_char_spawn.yaml`.
>
> - **5/1 SmsgActorSpawnExtended (12-byte prefix):** Sort u8 at frame +0x00 (low byte only; 1=player,
>   2=mob/NPC, 3=ground-item), 3B pad at +0x01, ActorId u32 at +0x04, TitleState u8 at +0x08,
>   TitleSlot u8 at +0x09, RelationFlag u8 at +0x0A, pad u8 at +0x0B. On the Sort==1 (player) branch
>   a 64-bit HP qword is consumed at descriptor +0x3C/+0x40 = wire +0x48/+0x4C. The 20-byte trailer
>   at frame +0x37C carries visual/combat flags (per-byte value meanings capture-pending). See
>   `packets/5-1_actor_spawn_extended.yaml`.
>
> Descriptor interior VALUES remain server-authored / capture-pending across all three spawn carriers.

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

### HP-qword correction (hard layout fact — binary wins)

The live HP is a **single 64-bit qword**, not two independent dwords:

- SD **+0x3C** = HP-LOW dword, SD **+0x40** = HP-HIGH dword. Together they are one signed 64-bit HP
  value spanning SD +0x3C .. +0x43 (Actor +0xB0 .. +0xB7 after the +0x74 copy).
- The 5/53 vitals path **clamps the qword at the live-actor mirror**: the cap compares a 64-bit max
  against the qword at Actor +0xB0 and, on overflow, sign-clamps both the low and high dwords to 0
  together — proof the +0x40 dword is the HP upper half, not an independent value.
- What an earlier draft labelled `current_mp` @ SD +0x40 is therefore the **HP-HIGH dword**. The
  MP/stamina-class vital is the **separate** dword at SD **+0x44** (Actor +0xB8), clamped on its own.
- Consistent with the 5/32 LevelUp packing of HP/MP into an i64.

> **Note for the network / domain engineer.** Decode HP as one `int64` at SD +0x3C; do **not** split
> it into `current_hp` (u32) + `current_mp` (u32). The next vital (MP / stamina) is the dword at SD
> +0x44. 5/53 delivers these vitals through its OWN flat 32-byte body (HP-low @ body +0x10, HP-high @
> body +0x14, VitalB @ body +0x18) — see `structs/net_packet_bodies.md` / the 5/53 packet spec; the
> Actor +0xB0/+0xB4/+0xB8 destinations are runtime mirrors, not wire fields.

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

## Wire-descriptor vs runtime-Actor offsets — the DO-NOT-PROMOTE-AS-WIRE register

Three coordinate spaces recur in the spawn / vitals family and **must stay separated** so a
constructed-live-Actor offset is never stamped into a `packets/*.yaml` as if it were an on-the-wire
field:

1. **WIRE-frame offset** — an offset into the bytes a handler reads off the channel. Belongs in
   `packets/*.yaml`.
2. **SpawnDescriptor (SD) offset** — an offset into this 880-byte descriptor (the wire-frame's inner
   body). Belongs in this file.
3. **Runtime-Actor offset** — an offset into the long-lived Actor object the spawn factory builds.
   The factory copies the 880-byte descriptor to **Actor +0x74**, so for descriptor fields
   `actor_offset = SD_offset + 0x74`. These offsets **never appear on the wire** and must be marked
   **DO-NOT-PROMOTE-AS-WIRE** (they belong in `actor.md`, not in a packet spec).

> **The single hard rule.** `actor_offset = SD_offset + 0x74`. Any time a handler's write destination
> sits in the Actor window **0x74 .. 0x3E3**, it numerically EQUALS a descriptor offset + 0x74 — that
> coincidence is **the trap** that makes a runtime offset look like a wire field. Only the
> descriptor-carrying pushes (4/4, 5/1, 5/3, and the respawn / game-state-tick descriptor form)
> actually put the 880-byte block on the wire. The **flat-body pushes (4/109, 5/53, 5/147, 5/121,
> 5/136) carry their fields in their OWN small bodies, not in a SpawnDescriptor** — their write
> destinations are runtime-Actor offsets only.

| Runtime / descriptor offset | Belongs to | Why it is NOT a wire field |
|-----------------------------|-----------|----------------------------|
| Actor **+0xC0 / +0xC4** | actor.md (live world-pos mirror) | Live copy of SD `world_x` / `world_z` (+0x4C / +0x50) after the +0x74 descriptor copy; the handler then overwrites them with the live interpolated position. The WIRE world floats are at the per-opcode offsets tabled above. |
| Actor **+0x379** (4/109 dest) | actor.md (live local player) | 4/109's ONLY wire field is a **flag byte @ its 12-byte body +0x08**; it writes that byte to Actor +0x379 = SD +0x305 + 0x74. (4/109 delivers via its own flat body, not a descriptor; the byte's spawn-time descriptor position SD +0x305 is a *descriptor* offset, also not a 4/109 wire field.) |
| Actor **+0xB0 / +0xB4 / +0xB8** (5/53 dest) | actor.md | 5/53 delivers vitals via its OWN flat 32-byte body (HP-low @ body +0x10, HP-high @ body +0x14, VitalB @ body +0x18). +0xB0/+0xB4 is the **HP qword** (the high dword is the upper half of HP, NOT MP); +0xB8 is the next vital. These Actor offsets are runtime mirrors of SD +0x3C (HP qword) / +0x44 (vital). |
| Actor **+0x3A0** (5/147 dest) | actor.md | 5/147's combat flag in the live actor; `= SD +0x32C + 0x74`. The wire field is the **flag dword @ body +0x04** (8-byte body). Do NOT label SD +0x32C as the 5/147 wire field — the SD offset and the runtime offset are the same byte in two coordinate spaces. |
| Actor **+0x73C** (5/121 dest) | actor.md | Pure runtime property field, **outside** the descriptor window (0x74..0x3E3), so unambiguously never on the wire and never in the descriptor. 5/121's wire field is `PropertyValue @ body +0x04` (8-byte body). |
| Actor **+0x588 / +0x584** (5/136 dest) | actor.md | Runtime mixer / timer fields, outside the descriptor window. 5/136's wire fields are Sort @+0x00, ActorId @+0x04, TimedValue @+0x08, StateByte @+0x0C (16-byte body); the +0x588/+0x584 destinations are runtime side effects. |

> All offsets / destinations above are **[confirmed]** (static-ida control flow); every wire-byte
> VALUE meaning stays **[capture/debugger-pending]**.

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
- **HP qword — RESOLVED (layout).** SD +0x3C/+0x40 is ONE 64-bit HP; +0x40 is the HP-HIGH dword, not
  MP; the MP/stamina vital is the separate dword at SD +0x44. Confirmed via the qword clamp on the
  live-actor mirror (Actor +0xB0). The MP-vs-stamina meaning of `vital_b` stays [capture-pending].
- **`world_x` / `world_z` WIRE offset — RESOLVED per opcode.** SD +0x4C/+0x50 (descriptor) ⇒ WIRE
  +0x54/+0x58 for 5/3 & 4/4 (8-byte prefix) but WIRE +0x58/+0x5C for 5/1 (12-byte prefix). Do not use
  one rule for all three.
- **4/109 / 5/147 SD-vs-Actor rebasing — RESOLVED.** 4/109 writes Actor +0x379 (= SD +0x305 + 0x74),
  wire field = flag byte @ body +0x08. 5/147 writes Actor +0x3A0 (= SD +0x32C + 0x74), wire field =
  flag dword @ body +0x04. Neither SD offset is the respective wire field — see the
  DO-NOT-PROMOTE-AS-WIRE register above.
- All offsets are static control-flow inferences; no live capture was available to confirm any
  *wire VALUE meaning* — those stay capture/debugger-pending throughout.
</content>

---

## Addendum — CYCLE 11 / Block A: the 96-byte info/stats block & the per-slot facing flag (static reach)

> Verification refresh, IDB SHA **263bd994**, static-only (CYCLE 11 / Block A, 2026-06-22).
> Pins the per-slot array layout of the character-select window and the static reach into the
> 96-byte info/stats block, with an explicit static-vs-runtime boundary.

### A.1 The three per-slot arrays (canonical, from the window block-copy)

When the character-list message populates the select window, it block-copies three parallel per-slot
arrays out of the network handler into the window object. The sizes/strides are static-confirmed:

| array | per-slot size | count | role |
|---|---|---|---|
| character descriptor | **880 bytes** | 5 | the full per-slot descriptor (this struct) |
| info / stats block | **96 bytes** (0x60) | 5 | backs the 2D info rows (name/level/position labels) |
| facing flag | **1 byte** | 5 | per-slot preview facing flag (see A.3) |

*([CONFIRMED]* the three block-copies and their per-slot strides.)* The 880-byte descriptor and the
96-byte block are **distinct regions** — do not conflate them.

### A.2 The 96-byte info/stats block — static interior reach

Statically, the only code path that writes the 96-byte block field-by-field is the **local
create-character synthesizer** (used when a freshly created character is staged into a slot). It writes
seven dwords; the rest of the block is filled only by the inbound character-list copy (so those bytes'
sizes/positions are static but their **values are runtime**).

| block off | size | type | field (static reach) | status |
|---|---|---|---|---|
| +0x00 | 4 | dword | synthesized field (create path) | static-confirmed offset, value runtime |
| +0x04 | 4 | dword | synthesized field (create path) | static-confirmed offset, value runtime |
| +0x08 | 4/qword-lo | dword | mirrors the descriptor HP (low) — derived from a `10·(1+x)` synth | static-confirmed linkage |
| +0x0C | 4 | dword | mirrors a descriptor vital — same `10·(1+x)` synth | static-confirmed linkage |
| +0x10 | 4 | dword | synthesized field (create path) | static-confirmed offset, value runtime |
| +0x30 | 4 | dword | synthesized field (create path) | static-confirmed offset, value runtime |
| +0x34 | 4 | dword | **constant 7** — freshly-created marker (same magic as the descriptor's freshly-created/locked state) | static-confirmed value |
| +0x14 … +0x2F, +0x38 … +0x5F | — | — | not touched by any static window path; filled only by the inbound character-list copy | RUNTIME-ONLY |

*([CONFIRMED]* the seven create-path dword writes, the two HP/vital linkages, and the constant-7 marker;
the untouched span is runtime-filled.)*

### A.3 The per-slot facing flag (binary-decided)

The 1-byte per-slot flag array is the **preview FACING** flag, not an occupancy flag. Its values:

| value | meaning |
|---|---|
| **0** | preview faces front (yaw 0) — set on create and on deselect |
| **1** | preview faces away / back-turned (yaw π) — the lineup spawn reads this |
| **2** | deletion-pending — set on the delete path |

**Occupancy is carried by the descriptor**, not by this flag (the roster-occupancy gate and the
preview-spawn gate are separate descriptor fields). *([CONFIRMED]* all five touch sites of the flag.)*

### A.4 Static-vs-runtime boundary (runtime residuals)

| id | item | boundary |
|---|---|---|
| RD-D1 | the inbound character-list (3/1) fills the descriptor + 96-byte block | offsets/sizes static; **VALUES runtime** |
| RD-D2 | the delete reply (3/5) updates a slot's flag + roster entry | handler reach is static; the slot index + result code are **runtime** |
| RD-D3 | the 3/7 manage-result sequence semantics | dispatch lives in the network handler; **not statically resolvable** — do not chase |
| RD-D4 | vitals values patched by the status/level messages | offsets static; **values runtime** |

> spec path: `// spec: Docs/RE/structs/spawn_descriptor.md`
