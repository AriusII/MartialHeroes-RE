---
status: confirmed
sample_verified: false
verification: confirmed (client-side routing/sizes/offsets confirmed by control flow; server-authored magnitudes and on-wire VALUE semantics capture/debugger-pending)
ida_reverified: 2026-06-16
ida_anchor: 263bd994
evidence: [static-ida]
conflicts: resolved (4/23 two-level selector restored; 4/25 phase/count offsets re-pinned)
---

# Inventory, Equipment, Shop & Trade — Clean-Room Specification

> Neutral, rewritten behavioural specification. No legacy symbols, no binary
> addresses, no pseudo-code. Describes the *observed behaviour*, state model,
> opcode tuples and constants of the legacy client's inventory / equip / quick-use /
> NPC-shop / item-upgrade / player-trade / ground-item (drop & pickup) subsystems, so
> the .NET core can be reimplemented from scratch.
>
> Scope: the client send-side (C2S) of the item/trade/ground-item family and the rules
> the client applies before sending or on receiving an ack. The detailed wire layouts
> of several server acks already live in `structs/item.md`; this spec references
> those and does **not** restate them field-by-field.
>
> **Revision 2026-06-13** promoted the full player-to-player **trade** three-packet flow
> (2/23, 2/24, 2/25; 4/23 phase machine) and the **ground-item** drop/pickup/render flow
> (2/14, 2/15; 4/4 tag 4, 5/14, 4/14, 4/15, 5/15) into §8 and the new §12.
>
> **Re-verification 2026-06-16 (build 263bd994).** Every load-bearing claim was
> re-confronted against the IDB by static read of the C2S send-builder family and the
> S2C ack handlers. The C2S send builders are the opcode/size oracle — each literally
> embeds its `(major, minor)` pair and its exact appended byte count — so **every C2S
> payload size in this spec is now byte-exact-confirmed**. Two ack-offset conflicts were
> resolved and re-pinned (4/23 has a two-level selector; 4/25 phase/count offsets moved),
> the **three-array** inventory model and the **bag soft-cap** were confirmed exactly, and
> the pre-send **validator message ids** for 2/24 and 2/15 were pinned. See §11 and the
> per-row notes.

## Status & confidence

- **Verification model (be honest about what is and isn't confirmed):**
  - **Client-side facts are `[confirmed]`** — opcode `(major:minor)` routing, C2S payload
    **sizes**, struct/field **byte offsets**, array strides/counts, message-db ids, the
    client-side validation gates, and the client timing constants. These are recovered
    from control flow + operands on build `263bd994` and are interoperability facts.
  - **Server-authored magnitudes and on-wire VALUE meanings are `[capture/debugger-pending]`**
    — the runtime *meaning* of enum values (e.g. 2/24 category 4 vs 1, 2/25 mode/flag byte),
    server-decided amounts (gold deltas, sell prices, +N enchant levels), and any field whose
    numeric content is server-decided. The client *places* and *reads* these; their wire
    values are not pinned without a live capture.
- **Opcode → handler routing is a hard static fact.** The `(major:minor)` tuples in the
  opcode tables below were cross-checked against the client's dispatch table, the C2S
  send-builder family, and the already-committed `opcodes.md`.
- **Per-claim confidence** is tagged inline as `CONFIRMED` (control-flow-confirmed, or
  cross-checked against an existing committed spec), `LIKELY` (single consistent site),
  or `UNVERIFIED`/`capture-pending` (inferred / boundary or wire VALUE not pinned).
- **Korean text fields use the CP949 (EUC-KR) code page.** Any inbound text in this
  subsystem (item names, NPC dialog) must be decoded as CP949, never UTF-8.
- Builds on existing committed specs: `structs/item.md` (`ItemSlotRecord` 16 B,
  `EquipSlotBody` 36 B, `EquipItemResult` 16 B, NPC-buy ack 56 B, item-actor stat
  grants), `structs/actor.md`, `structs/stats.md`, and `opcodes.md` (all the 4/x and
  5/x routing rows referenced below are `confirmed` there).
- A consolidated **UNVERIFIED / open-questions** list is in §11. Do not implement
  any field tagged `UNVERIFIED` without first widening capture evidence or asking for
  an analyst cross-check.

---

## 1. Inventory grid & equipment slot model

### 1.0 Three inventory arrays (re-pinned 2026-06-16)

The inventory is **not** a single flat array. The client maintains **three** parallel
record arrays, each entry a 16-byte `ItemSlotRecord` (layout in `structs/item.md`), all
sharing the same `0xFF` empty-slot marker convention. The shared item-expiry sweep
iterates all three back to back, which is the structural proof of the three-array model.
`CONFIRMED`.

| Array | Count | Stride | Purpose | Conf |
|---|---|---|---|---|
| **Bag** | `40 × (bag_count + 3)` | 16 B | The carried-items bag; capacity grows with the player's bag-expansion count. | CONFIRMED |
| **Equip** | 20 | 16 B | The worn-equipment + fixed-slot array (the array re-synced on equip/sell acks; §1.1). | CONFIRMED |
| **Extra** | 120 | 16 B | A third item record array on the local-player block (rental / extended / overflow records). | CONFIRMED |

- **Bag soft-cap.** The bag holds `40 × (bag_count + 3)` records, where `bag_count` is a
  small per-player bag-expansion count held as a single unsigned byte on a global. So a
  player with zero expansions has `40 × 3 = 120` bag records; each expansion adds 40.
  `CONFIRMED` (the expiry sweep walks exactly `40 × (bag_count + 3)` records at stride 16).
- **Shared `0xFF` expiry sweep.** A single sweep routine walks the bag (`40 × (bag_count+3)`),
  then the 20-entry equip array, then the 120-entry extra array, checking each record's
  empty-slot marker byte (the same `0xFF` marker used by the pickup free-slot capacity gate,
  §12.3). On a rental / timed item nearing expiry it raises a localized warning (the
  expiry-warning message family, ids in the **4043…4049** range). `CONFIRMED`.

> Implementer note: model three `ItemSlotRecord[]` arrays — `bag[40 × (bagCount + 3)]`,
> `equip[20]`, `extra[120]` — all owned by the local player, all using the `0xFF`
> empty-slot marker, and all swept by one expiry pass. The 20-entry equip array is the one
> mirrored into the inventory/trade controller (§1.1).

### 1.1 The 20-slot equip/fixed array

The 20-entry **equip array** holds worn equipment and the fixed slots. Each entry is one
16-byte `ItemSlotRecord` (layout in `structs/item.md`: a verbatim first dword, an
`item_actor_id`, then a 64-bit expiry split into low/high). The array is therefore
**320 bytes** (20 × 16). `CONFIRMED`.

- An entry is read by slot index; the runtime resolves the entry, then uses its
  `item_actor_id` to locate the **item actor**, which carries the actual stat-grant
  fields (see `structs/item.md`, "Item-actor stat-grant fields"). `CONFIRMED`.
- This array is a **copy of the local player's own item block** carried inside the
  player actor's layout. On certain acks the client re-synchronises all 20 records
  from the player actor's item block back into this array — both the **equip-change ack
  (4/16)** and the **NPC-sell ack (4/20)** run an identical `for (20) { … += 16 }` copy
  loop from the local-player item block into the equip-table on success. This is the
  structural proof that the equip model is 20 fixed slots of 16 bytes. `CONFIRMED`.

> Implementer note: model the equip array as a fixed `ItemSlotRecord[20]` owned by the
> local player and mirrored into an inventory/trade controller. Both copies are kept in
> sync; treat the player-actor block as the authority and the standalone array as a
> cache that is refreshed on inventory-changing acks (4/16, 4/20).

### 1.2 Reserved slot indices

Slot indices are a single 0–19 space shared by worn gear and bag items. Three indices
have special meaning observed in the equip/stat handlers:

| Slot index | Role | Conf |
|---|---|---|
| 15 | Visual / appearance slot. An equip whose destination slot resolves to 15 triggers a full **visual gear rebuild** (the appearance-rebuild path runs with destination 15). | CONFIRMED |
| 14 | Special weapon slot (consistent with `structs/item.md`). | LIKELY |
| 8  | Excluded from "equip-onto-other" and excluded from the **worn-item stat sum** (the equip guard rejects destination slot 8; the stat formula in `structs/stats.md` skips slot 8). The actual category slot 8 holds is **UNVERIFIED** (likely a non-gear / quick category). | LIKELY (exclusion); UNVERIFIED (meaning) |

### 1.3 Quick-use slot array

A **separate** quick-use slot array exists, distinct from the 20-slot item array. Each
quick-use entry is **8 bytes** (the array is indexed with an 8-byte stride). Before a
quick-equip / quick-use is allowed, the client checks whether the target quick-use slot
is **occupied**. `LIKELY`.

---

## 2. Wire framing & opcode conventions

All opcodes are written as `major/minor` tuples (the `(major:minor)` pair *is* the
opcode; see the header table in `opcodes.md`). Direction is from the **client's** point
of view: `C2S` = client→server, `S2C` = server→client.

- **Family 2 (game-action) is entirely client-emitted** for this subsystem: there is no
  inbound major-2 handler. Each C2S message is built by writing the `major`/`minor` into
  the frame header and then appending a fixed-length payload. The "Size" column below is
  the literal payload-bytes count written by the send builder (payload-relative, i.e. not
  counting the 8-byte frame header). `CONFIRMED` (send-only nature **and** individual
  sizes — re-verified 2026-06-16). The C2S send builders form **one contiguous self-
  documenting family**: each builder embeds its `(major, minor)` pair and appends an exact
  byte count, so opcode + payload size are control-flow-hard facts. **Every C2S size
  asserted in this spec is byte-exact in its builder.**
- **Families 3, 4, 5** carry the server acks consumed by this subsystem. Their routing is
  `confirmed` in `opcodes.md`.
- All field offsets in this spec are **payload-relative** (relative to the first payload
  byte), matching the convention in `opcodes.md` and `structs/item.md`.

---

## 3. Client-emitted (C2S) opcode map — inventory / shop / trade

The following client→server messages drive this subsystem. Sizes are payload bytes.

| Opcode | Dir | Size | Canonical name (proposed) | Purpose | Pairs with ack |
|---|---|---|---|---|---|
| 2/5  | C2S | 28 | `CmsgUseItemOrTeleport`  | Item-use / teleport-item request. | 4/5 item-use result |
| 2/14 | C2S | 8  | `CmsgDropItem`          | Drop an inventory item or coin stack to the ground. See §12. | 4/14 (20 B) |
| 2/15 | C2S | 12 | `CmsgPickupItem`        | Pick up a targeted ground item. See §12. | 4/15 (36 B) |
| 2/16 | C2S | 12 | `CmsgEquipChange`        | Equip / unequip / swap request (also inventory slot-move). See §4. | 4/16 + 4/12 |
| 2/17 | C2S | 8  | `CmsgQuickEquipSlotSet`  | Quick-equip / quick-use slot set. See §5. | 4/17 |
| 2/19 | C2S | 12 | `CmsgNpcInteractOpen`    | NPC interact / shop-open. See §7. | 4/19 (56 B) |
| 2/20 | C2S | 12 | `CmsgNpcSellItem`        | Sell one inventory item to an NPC. See §7. | 4/20 (24 B) |
| 2/22 | C2S | 8  | `CmsgUseItemOnTarget`    | Item-use targeted at an actor (tentative). | item-use acks |
| 2/23 | C2S | 8  | `CmsgTradeRequest`       | Player-trade request / accept. See §8. | 4/23 |
| 2/24 | C2S | 20 | `CmsgTradeSlotUpdate`    | Add / update money or an item in your trade-window half. See §8. | 4/24 |
| 2/25 | C2S | 2 + 4×N | `CmsgTradeConfirm`     | Confirm / lock or cancel the trade, re-transmitting the own-side offer manifest. See §8. | 4/25 |
| 2/30 | C2S | 8  | `CmsgGuildAction` (relation/guild op) | Small guild / relation action channel; **not** a trade op (corrected 2026-06-13). See §8.4. | — |
| 2/50 | C2S | 16 | `CmsgUpgradeItemCommit`  | Timed/progress-channel commit (item upgrade/enchant candidate). See §6. | 4/50 (likely) |

Confidence: routing (`major/minor` + direction) **and payload size** are `CONFIRMED` for
**every** row above (re-verified byte-exact in each send builder, 2026-06-16). For **2/50**
the *size* (16 bytes) is confirmed but its *commit purpose* (upgrade vs a shared
channel-then-commit op) is `LIKELY` and its payload field breakdown is `UNVERIFIED`. The
detailed payload field tables are in the relevant section per opcode; interior field
*order* for several opcodes remains static-hypothesis pending a capture (§11).

> **Open routing gaps (flag for follow-up):**
> - The **outbound trade finalize/confirm minor is now pinned as 2/25** (corrected
>   2026-06-13: it is the in-window confirm/lock and cancel op — a 2-byte header plus an
>   own-side offer manifest, *not* a flat 2-byte packet — see §8.1). The earlier note that
>   the confirm minor was a candidate among 2/30 or a second mode of 2/23 is superseded.
> - **2/30**'s role is resolved as a guild / relation action channel (corrected 2026-06-13);
>   it is no longer a trade-finalize candidate. See §8.4.
> - No **dedicated stack split/merge** opcode was found (§9).

---

## 4. Equip / unequip / swap (C2S 2/16; acks 4/16 and 4/12)

### 4.1 2/16 request payload — 12 bytes

| Off | Size | Field | Meaning | Conf |
|---|---|---|---|---|
| +0 | u8  | `mode`        | `0` = unequip, `1` = equip, `2` = equip-swap. | LIKELY |
| +1 | u8  | `slot`        | Equipment slot index involved. | LIKELY |
| +2 | u8  | `from`        | Source slot / sub-index. | LIKELY |
| +3 | u8  | `to`          | Destination slot / sub-index. | LIKELY |
| +4 | u8  | `sub`         | Extra sub-index (e.g. bag page). | LIKELY |
| +5 | 3 B | (pad)         | Alignment padding. Treat as zero / ignore; verify on wire. | UNVERIFIED |
| +8 | i32 | `item_index`  | The validated item / inventory index; always ≥ 0 on a real send. | LIKELY |

Field widths sum to 12 bytes (1+1+1+1+1+3+4), matching the declared size.

> **Inventory rearrange rides this opcode.** No separate "drag item A→B" message
> distinct from this slot-move mechanism was observed; an in-bag move reuses the same
> `from`/`to` slot semantics (§9). `LIKELY`.

### 4.2 Client-side pre-send validation rules

The client refuses to emit 2/16 unless all relevant gates pass. Gates observed:

1. **Valid-index gate.** `item_index` must be **≥ 0**. The client will not send an equip
   for an empty / invalid slot. `LIKELY`.
2. **State gates.** The send is gated behind the shared "am I in game / not busy / not
   dead" predicates: the main handler's in-game flags must be set, a busy-check must pass,
   and a not-dead/not-busy check must pass. A swap (`mode == 2`) or a specific guard
   predicate selects this path. `LIKELY`.
3. **Equip-onto-other relation guard (the slot-8 rejection).** On the unequip/move path
   the client resolves two player actors — the actor referenced by the chosen slot's
   record and the actor occupying fixed slot 15 — and rejects the action when **all** of
   these hold:
   - both actors exist and are **different**, and
   - both share the **same non-zero relation-context id** (a group/party/couple context
     value stored on the player actor), and
   - the destination slot is **8**.

   On rejection the client shows localized message **59003** (a "cannot move this item
   into another player's context"-class message) and does **not** send. `LIKELY` (rule
   shape); the exact meaning of the relation-context value on a player actor is
   `UNVERIFIED` (it shares an offset with an item-actor set-bonus flag in
   `structs/item.md`, but on a *player* actor it is a relation/party/couple/duel context,
   a different object type — do not conflate them).

### 4.3 Equip eligibility (level / class) is server-authoritative

No item **level / class equip-requirement** check was found on the client *send* path for
2/16. The only client-side gates are the state gates plus the slot-8 relation guard
(§4.2). Item level/class eligibility appears to be enforced by the **server** — the client
sends and waits for the 4/16 / 4/12 result. Any tooltip-time "greyed-out ineligible item"
behaviour, if it exists, lives on a different (item-template compare) path that was not on
this send path. **Mark client-side level/class equip enforcement as not-present / server
authoritative; `UNVERIFIED` whether the client greys out ineligible items at all.**

### 4.4 Result acks

- **4/12 `EquipItemResult` (S2C, 16 bytes fixed)** — layout in `structs/item.md`
  (`EquipItemResult`). The handler reads a 16-byte body, takes the **result byte at +8**,
  and the **slot type at +0x0C**; on `result == 1` it applies the slot to the item model;
  if the slot type is **15**, it then runs the visual / appearance gear rebuild. On
  `result == 0` it shows a generic error. Offsets `CONFIRMED` (re-verified 2026-06-16);
  the result/slot **values** are read straight from the wire (server-authored).
- **4/16 `EquipChangeResult` (S2C, 20 bytes fixed)** — same family; routing `confirmed`.
  The handler reads a **20-byte (0x14) body**: **result at +8**, equip sub-bytes at **+0x0A
  and +0x0B**, where the slot-15 visual-rebuild test is on the byte at **+0x0B** (the
  16-bit-equip-result slot-15 byte sits at +0x0C; the 20-bit equip-change-result slot-15
  byte sits at +0x0B). On success the handler re-syncs the **20 × 16** equip array from the
  local-player item block (§1.1). Offsets `CONFIRMED` (re-pinned 2026-06-16); the deeper
  per-field meanings of the +0x0A/+0x0B sub-bytes are `UNVERIFIED`.

---

## 5. Quick-equip / quick-use (C2S 2/17; ack 4/17)

### 5.1 2/17 request payload — 8 bytes

| Off | Size | Field | Meaning | Conf |
|---|---|---|---|---|
| +0 | u8  | `quick_slot`       | Quick-use slot index. | LIKELY |
| +1 | u8  | `mode`             | Action mode (`0/1/2…`). `mode == 1` is a "relink / increment" variant. | LIKELY |
| +2 | 2 B | (pad)              | Alignment padding to the dword. Treat as zero; verify on wire. | UNVERIFIED |
| +4 | i32 | `item_or_target`   | Item id or target actor id, depending on mode. | LIKELY |

Field widths sum to 8 bytes (1+1+2+4), matching the declared size.

### 5.2 Client-side validation (message-id resolver)

Before sending, the client runs a validator that returns either a **localized message id**
(to display and abort) or a "success / suppress message" sentinel meaning OK. The message
ids below are runtime localized **message codes** (CP949 strings), **not** opcodes:

- **`mode < 10`:** if `mode ∈ {2,3,4,5,6}` and the target quick-use slot is **occupied**,
  reject with the "slot busy"-class message. `LIKELY`.
- **`mode ≥ 10`:** reject if the quick-use slot is occupied ("slot busy"); else if the
  target actor is not found ("target not found"); else if the target actor's "usable" flag
  is false ("not usable"). `LIKELY`.
- **Global state gate (all modes):** the main handler's in-game flags must be set; if not,
  abort with the success/suppress sentinel or an "in game only"-class message. `LIKELY`.

> The "success / suppress message" sentinel is an all-ones value; treat it as "no message
> to show, proceed". Implementers should model the validator as returning an optional
> message id and only send the packet when the result is the OK sentinel.

### 5.3 Ack

- **4/17 `QuickEquipSlotAck` (S2C)** — routing `confirmed`; field layout not re-derived
  this pass. `UNVERIFIED` (layout).

---

## 6. Item upgrade / enchant (timed channel; C2S 2/50 candidate; ack 4/50)

### 6.1 The timed-channel commit pattern

Item upgrade/enchant uses a "channel a progress bar to 100 %, then commit" pattern:

1. A per-action **progress / gauge value** counts up over time. `CONFIRMED` (pattern).
2. When the gauge reaches **100.0**, the client builds a **16-byte** request and sends
   **2/50** (the commit), gated by the can-act and not-busy predicates. `LIKELY`.
3. The 16-byte payload is built from a per-action object region that was **not decoded
   field-by-field** this pass. **The 2/50 payload field breakdown is `UNVERIFIED`.**

> The same timed-bar pattern may be shared by other "channel then commit" actions
> (gathering / crafting — those have their own result opcodes, e.g. a crafting-result and
> a gathering-result). Confirm 2/50 is the upgrade commit (vs. one of those) against a
> capture before treating it as upgrade-specific. `UNVERIFIED`.

### 6.2 4/50 `UpgradeItemResult` (S2C, 32 bytes)

`prior-capture-claim`: the handler reads a fixed **32-byte (0x20)** body.

| Off | Size | Field | Meaning | Conf |
|---|---|---|---|---|
| +8 | u8 | `success` | Upgrade outcome: non-zero = success, `0` = failure. | LIKELY |

Behaviour on receive:

- **Success (≠ 0):** the local player plays the **success** motion (motion id **8**); the
  32-byte record is fed into the item-model upgrade-apply path. `LIKELY`.
- **Failure (0):** the local player plays the **fail / shatter** motion (motion id **9**).
  `LIKELY`.
- The resulting **+N enchant level** lives somewhere inside this 32-byte body; the exact
  offset is **`UNVERIFIED`** (candidate positions are the bonus dwords analogous to the
  `EquipSlotBody` bonus fields at +0x18 / +0x1C / +0x20 in `structs/item.md`). Pin against
  a capture.

### 6.3 The +N enchant value is server data, not a client formula

The numeric `+1 / +2 / …` enchant levels are sourced from the **server's item table**
(server-side item data). The client only **displays** the result and plays the
success/fail animation; there is **no client-side enchant-roll formula**. `CONFIRMED`
(behaviourally — the client has no roll logic on this path).

---

## 7. NPC shop — buy / interact-open / sell

### 7.1 Interact-open & buy (C2S 2/19; ack 4/19, 56 bytes)

**2/19 request payload — 12 bytes:**

| Off | Size | Field | Meaning | Conf |
|---|---|---|---|---|
| +0 | i32 | `npc_id`   | Target NPC actor id (non-zero on a shop-open). | LIKELY |
| +4 | u8  | `flag_a`   | Sub-flag; zero on a plain open. | UNVERIFIED |
| +5 | u8  | `flag_b`   | Sub-flag; zero on a plain open. | UNVERIFIED |
| +6 | u8  | `flag_c`   | Sub-flag; zero on a plain open. | UNVERIFIED |
| +7 | u8  | `flag_d`   | Sub-flag; zero on a plain open. | UNVERIFIED |
| +8 | i32 | `arg`      | Secondary argument; zero on a plain open. | UNVERIFIED |

Field widths sum to 12 bytes (4+1+1+1+1+4), matching the declared size.

**4/19 `NpcBuyOrAcquireAck` (S2C, 56 bytes)** — full layout in `structs/item.md`
("NpcBuy / inventory-acquire ack"). Subsystem-relevant behaviour:

- On `result == 1` (success), the acquired item is applied to the destination bag slot.
  If the gold pair in the local copy is zero, the client also plays a **purchase sound**
  (sound id **862050101**). On a plain interact-open with no purchase the item descriptor
  words are zero. `LIKELY`.
- The handler reads a **56-byte (0x38) body** with the **result byte at +8** and a
  reason byte at **+9** (re-verified 2026-06-16). On `result == 1` it applies the acquire;
  on `result == 0 && reason == 1` the client shows the **item-shop-expired** message
  (id **36003**) and then formats a **repair-cost / repair-time table** from the NPC
  template. The repair table is computed from template values using **60** and **12**
  (one column from a multiply-by-60, a duration column from a divide-by-12), selecting
  localized format strings across **two sub-tables**: **36004 … 36011** (one column) and
  **36012 … 36027** (the duration column), with **36028** as the trailer. These ids are
  the repair-price / repair-time message templates. `CONFIRMED` (offsets + id ranges).

  **Repair-table constants:** factors **60** and **12**; message-id sub-tables
  **36004…36011** and **36012…36027**; trailer id **36028**.

### 7.2 Sell one item (C2S 2/20; ack 4/20, 24 bytes)

**2/20 request payload — 12 bytes:**

| Off | Size | Field | Meaning | Conf |
|---|---|---|---|---|
| +0 | i32 | `npc_id`     | Target NPC actor id. | LIKELY |
| +4 | u8  | `sell_flag`  | Sell category / flag. | LIKELY |
| +5 | u8  | `inv_slot`   | Inventory slot index of the item being sold. | LIKELY |
| +6 | 2 B | (stale)      | Two bytes present but **not used** by the server (do not rely on them). | UNVERIFIED |
| +8 | i32 | `quantity`   | Quantity to sell. | LIKELY |

Field widths sum to 12 bytes (4+1+1+2+4), matching the declared size. The item itself is
resolved **server-side** from `npc_id` + `inv_slot`; the client does not send an item id.

**4/20 `NpcSellItemAck` (S2C, 24 bytes)** — the handler reads a **24-byte (0x18) body**;
the **result byte is at +8** and a secondary byte is read at **+0x0E** (re-verified
2026-06-16). The fuller field map below is `prior-capture-claim` for the gold/echo fields:

| Off | Size | Field | Meaning | Conf |
|---|---|---|---|---|
| +0 | 4 B | `header`      | Packet prefix. | LIKELY |
| +4 | i32 | `actor_id`    | Selling actor id. | LIKELY |
| +8 | u8  | `result`      | `0` = error, `1` = ok. | CONFIRMED (offset) |
| +10| u8  | `category`    | Echo of `sell_flag`. | LIKELY |
| +11| u8  | `inv_slot`    | Echo of `inv_slot`. | LIKELY |
| +12| i32 | `quantity`    | Quantity sold. | LIKELY |
| +14| u8  | `secondary`   | Secondary status / mode byte read by the handler. | CONFIRMED (offset) / UNVERIFIED (meaning) |
| +16| u32 | `gold_total`  | Player gold **after** the sale; the client adopts this as authoritative. | LIKELY (offset) / capture-pending (value) |
| +20| u32 | `gold_or_fame_hi` | High word of gold, or a second currency / fame value. | UNVERIFIED |

Behaviour on `result == 1`: apply the sale to the item model; when the selling actor is
the local player, **re-sync all 20 equip records** from the player actor's item block
into the standalone 20-slot equip array (§1.1). `CONFIRMED` (the self-sale 20×16 re-sync).
The post-sale gold magnitude is server-authored (`capture-pending`).

### 7.3 Related shop acks (already committed in `opcodes.md`)

A buy-variant region (4/13), an NPC shop-slot-clear ack (4/21), a shop-page-update
(3/8 `SmsgShopPageUpdate`), and the item-shop / cash-shop family (4/71, 4/113, 4/114,
4/115) are all routed `confirmed` in `opcodes.md`. They are referenced here for context
only; their layouts are out of scope for this spec.

---

## 8. Player-to-player trade

Direction of travel is from the client's point of view. All inbound trade handlers feed a
shared inventory/trade controller.

The trade is a **three-packet client→server flow**: **2/23** (request / accept),
**2/24** (add money or an item to your half), **2/25** (confirm/lock or cancel,
re-transmitting your offer manifest) (corrected 2026-06-13: previously the confirm minor was
left unpinned and 2/24's field map was undecoded — both are now resolved from the trade-panel
build / action / send-builder routines). All offsets are CODE-CONFIRMED (static read/write
order) but **CAPTURE-UNVERIFIED**.

### 8.1 Client-emitted (C2S)

- **2/23 `CmsgTradeRequest` (8 bytes)** — request / accept. Shares the family's common 8-byte
  "social op" shape: a 1-byte mode at +0, three uninitialised stack bytes at +1…+3 (write as
  zero, ignore on read), and a 4-byte value at +4. (Corrected 2026-06-13: the mode enum is now
  pinned — `2 = request`, `0 = accept` — replacing the earlier "request/accept/decline/cancel
  enum UNVERIFIED" note.)

  | Off | Size | Field | Meaning | Conf |
  |---|---|---|---|---|
  | +0 | u8  | `mode`  | `2` = initiate trade request; `0` = accept / respond-OK. Decline/cancel modes (`1`/`3`) are plausible by symmetry but were not observed on a send path. | CODE-CONFIRMED (modes 0, 2) |
  | +1 | 3 B | (pad)   | Uninitialised stack bytes; written as zero. | CODE-CONFIRMED |
  | +4 | i32 | `value` | Target actor id (on request) or response/responder context (on accept). | CODE-CONFIRMED (placement) |

  Field widths sum to 8 bytes (1+3+4). On **mode 2** (request) the client first opens the timed
  right-click confirmation popup (a shared "pending interaction" context with an **8000 ms**
  countdown, distinguished from the party-invite and relation contexts only by a small internal
  context-type code) before sending. The send is gated by the can-act predicates; on a blocked
  state the client shows the **"cannot trade now"** message (id **36030**) and does not send.

- **2/24 `CmsgTradeSlotUpdate` (20 bytes)** — add or change **money or an item** in your half
  of the trade window. Gated by busy/dead checks and by the slot validator before send. (Corrected
  2026-06-13: the field map is now decoded — a leading **category** byte selects money vs item, and
  money rides as a 64-bit value split across two dwords.)

  | Off | Size | Field | Meaning | Conf |
  |---|---|---|---|---|
  | +0 | u8  | `category` | `0` = money, `1` = item, `4` = add-item-with-confirm, `255` = set money total. | CODE-CONFIRMED |
  | +1 | u8  | `slot`     | Slot / sub-index. | CODE-CONFIRMED |
  | +2 | 2 B | (pad)      | Alignment padding to the dword. | PLAUSIBLE |
  | +4 | i32 | `a`        | Item index (item categories) **or** the low dword of the money value. | CODE-CONFIRMED (money case) / PLAUSIBLE (item case) |
  | +8 | i32 | `b`        | High dword of the money value (money is a 64-bit amount split across +4/+8) **or** extra (item categories). | CODE-CONFIRMED (money case) |
  | +12| i32 | `c`        | Quantity (item categories) or extra. | PLAUSIBLE |
  | +16| u8  | `flag0`    | Trailing flag byte. | PLAUSIBLE |
  | +17| u8  | `flag1`    | Trailing flag byte. | PLAUSIBLE |
  | +18| u8  | `flag2`    | Trailing flag byte. | PLAUSIBLE |
  | +19| 1 B | (pad)      | Alignment padding to 20 bytes. | PLAUSIBLE |

  Field widths sum to 20 bytes (1+1+2+4+4+4+1+1+1+1). The **money case is CODE-CONFIRMED** (a
  64-bit amount split across +4/+8); the item-category column order (item index vs quantity among
  `a`/`b`/`c`) is inferred from argument order and remains capture-unverified.

  **Money is a base-1000 three-digit composite.** The trade panel holds the offered money as three
  digit fields and assembles the 64-bit total as `total = d2 + 1000 × (d1 + 1000 × d0)` (the three
  fields live at trade-panel offsets +1536 / +1544 / +1552; see §8.5). Setting the money offer from
  the panel's right-click menu emits **2/24 with `category` = 255** carrying that composite.

  The pre-send slot validator rejects, with a localized message (message ids pinned
  2026-06-16):
  - **duplicate item** already present in the 40 trade slots → msg **710**;
  - **insufficient gold** (current gold below the offered amount) → msg **600**;
  - a **busy / already-confirmed** trade → a localized busy message;
  - an **open-shop conflict** → msg **406**;
  - a **non-tradeable / bound** item (category 1) → a localized non-tradeable message.

  `CONFIRMED` (the 710 / 600 / 406 ids and the gate set).

- **2/25 `CmsgTradeConfirm` (2-byte header + N × 4-byte own-side slot manifest, N ≤ 40)** —
  confirm/lock or cancel the trade. (Corrected 2026-06-13: this was previously listed as a flat
  2-byte packet and the outbound confirm minor was "not pinned". It is **not** a flat 2-byte
  packet — it is a 2-byte header followed by a variable manifest of 4-byte records, one per
  occupied own-side trade slot, up to 40. This is the trade-finalize/confirm edge the earlier
  pass left open.)

  | Off | Size | Field | Meaning | Conf |
  |---|---|---|---|---|
  | +0 | u8  | `mode`  | `1` = confirm / agree / lock; `0` = cancel / withdraw. | CODE-CONFIRMED |
  | +1 | u8  | `flag`  | Confirm-state / sub-flag carried from the panel's local lock state. | CODE-CONFIRMED |
  | +2 + 4×i | 4 B | `slot_record[i]` | One record **per occupied own-side trade slot**, `i` from 0 to N−1, N ≤ 40. Each record is `{ slot_index:u8, b0:u8, b1:u8, b2:u8 }`. | CODE-CONFIRMED (structure) |

  The send routine walks all 40 own-side trade slots and appends a 4-byte record only for each
  **occupied** slot, so the total payload size is `2 + 4 × (occupied own-side slots)`. The three
  trailing bytes per record are slot-record fields (plausibly `{category, item-sub, count}`); their
  exact meaning is capture-unverified. This is a **commit-with-manifest** packet: the client
  re-transmits its own offered-item manifest at confirm time. Whether the server treats this
  manifest as authoritative or merely advisory (recomputing the offer server-side) is an open
  question (§11).

### 8.2 Server acks (S2C; all `confirmed` routing in `opcodes.md`)

- **4/23 `UserTradeRequestResult` (20 bytes)** — a **two-level selector** (corrected
  2026-06-16, resolving CONFLICT C1). The handler reads a 20-byte body and first branches
  on an **outer selector byte at +8**, then — on the accept path — runs a **phase machine on
  the byte at +10**. The earlier (2026-06-13) note that the +8/+9 reading was "superseded"
  by the +10 phase machine was half-wrong: **both bytes are live** — +8/+9 is the
  outer accept-vs-decline gate, +10 is the window-state phase. The two trader ids are at
  **+12 / +16** (plus the requester id at **+4**).

  | Off | Size | Field | Meaning | Conf |
  |---|---|---|---|---|
  | +4  | u32 | `requester_id` | The actor that initiated the request. | CONFIRMED (offset) |
  | +8  | u8  | `select`       | **Outer selector.** `1` = accept path → copy the 20-byte block and run the +10 phase machine; `0` = decline path → display a decline-reason string chosen by `reason` at +9. | CONFIRMED |
  | +9  | u8  | `reason`       | **Decline reason** (only meaningful when `select == 0`): values **1…5** select five decline-reason strings. | CONFIRMED (offset + 1–5 mapping) / capture-pending (per-reason text) |
  | +10 | u8  | `phase`        | **Inner phase** (only when `select == 1`): `0` = cancel / decline; `2` = incoming request; `3` = accepted → open the trade window. | CONFIRMED |
  | +12 | u32 | `target_id`    | The addressed target actor. | CONFIRMED (offset) |
  | +16 | u32 | `responder_id` | The responder / canceller actor. | CONFIRMED (offset) |

  Outer behaviour:
  - **select 0 (decline):** display the decline-reason string indexed by `reason` (+9), cases
    **1…5** → five decline-reason message strings.
  - **select 1 (accept):** copy the 20-byte block into the trade context and run the +10 phase
    machine below.

  Phase behaviour (own-vs-partner resolved by comparing `requester_id` against `responder_id`):
  - **phase 2 (incoming request):** if the request is addressed to the local player and the player
    is not busy, show the "X wants to trade" prompt (id **2042**) in the request panel; if the
    player is busy, the client auto-accepts by sending **2/23 mode 0**.
  - **phase 3 (accepted):** close the confirmation popup, **open the trade window** plus the partner
    **item panel**, set both side name labels (own = local player name, partner = partner actor
    name), and hide the rest of the HUD windows.
  - **phase 0 (cancel/decline):** close the trade / wait panels; if the canceller differs from the
    requester, show a decline message; clear the trade state and zero the 20-byte trade context.
- **4/24 `UserTradeSlotUpdateResult` (44 bytes)** — the handler reads a **44-byte (0x2C)
  body** with **`result` at +8** and **`reason` at +9** (cases **1** → one message, **2** →
  another). On `result == 1` the client updates the trade-slot view; otherwise it refreshes /
  clears the view. `CONFIRMED` (size + offsets; re-verified 2026-06-16).
- **4/25 `UserTradeFullResponse` (28-byte base + item list)** — the finalize/commit
  message. (Offsets re-pinned 2026-06-16, resolving CONFLICT C2: the handler reads a
  **28-byte (0x1C) base**, the **phase byte is at +8** — not +0x10 — and **`item_count` is
  at +0x18 (24)** — not +0x14. The phase *values*, the 16-byte record, and the 40-slot cap
  were already correct; only the two byte offsets were wrong in the prior pass.)
  - `phase` at **+8**: **4 = finalize / commit**, **0 = error / cancel** (cancel shows an
    error message). `CONFIRMED` (offset + phase values).
  - On finalize: `item_count` at **+0x18 (24)** (u8, **max 40**), then `item_count` ×
    **16-byte** trade-item records read sequentially into the 40-slot table. `CONFIRMED`
    (offset + record size).
  - The trade window has a local **40-slot** capacity (a 40-entry trade array in the
    controller, slot-table walk capped at 0x28); each populated slot contributes a small UI
    record. `CONFIRMED` (capacity constant).

  **Constants:** max trade items = **40 (0x28)**; trade-item record = **16 bytes**;
  base size = **28 (0x1C)**; phase byte at **+8** (4 = finalize, 0 = cancel);
  `item_count` byte at **+0x18 (24)**.

- **5/106 `TradeStateToggle` (12 bytes)** — `sort` (u32) at +0, `id` (u32) at +4, `on`
  (u8) at +8. Toggles the target actor's **trade-busy flag**; for the local player it
  opens/closes the personal-shop UI and clears a secondary state on un-toggle, and it
  propagates the busy flag to a paired ("couple") actor. Plays trade-open SFX (sound ids
  863500001 / 863500002 / 863500003) and a visual cue (id 350000063) on enter. `LIKELY`.
- **5/42 `PlayerPairSystemNotice`** and **5/26 `LocalPlayerRelationSlot`** are adjacent
  relation / couple notices that touch the same actor relation/trade-busy fields. They are
  cross-references only and are **not** trade-core; out of scope here.

### 8.3 Trade state machine (client view)

(Updated 2026-06-13: the confirm edge is now the pinned **2/25 mode 1**, and window-open is keyed
on the **4/23 phase** byte rather than a generic result flag.)

```
IDLE
  └─(send 2/23 mode 2 request)─► REQUESTED
                              └─(recv 4/23 phase==2 incoming, to me)─► (request prompt; accept ─► send 2/23 mode 0)
                              └─(recv 4/23 phase==3 accepted)──────────► WINDOW_OPEN
WINDOW_OPEN
  ├─(send 2/24 add money/item)  ◄──(recv 4/24 slot-update result)─►  (view updates)
  ├─(recv 5/106 on)             ─► both parties marked trade-busy
  └─(send 2/25 mode 1 confirm + own-side manifest)
        └─(recv 4/25 phase==4 FINALIZE, full item list)─► apply items, close ─► IDLE

Any of: send 2/25 mode 0 cancel, recv 4/23 phase==0, 4/24 error,
        4/25 phase==0, or 5/106 off  ─► cancel / close ─► IDLE
```

`LIKELY` for the overall shape; the individual edges are CODE-CONFIRMED in placement but
CAPTURE-UNVERIFIED.

### 8.4 2/30 — guild / relation action channel (not a trade op)

(Added 2026-06-13.) **2/30 `CmsgGuildAction` (8 bytes)** is the small guild / relation action
channel, **not** the trade-finalize candidate the earlier pass guessed. Its payload is a pair of
4-byte words: `op` at +0 and an `id` at +4. The submit path resolves a target id and applies a
self-target guard (it shows a "cannot target yourself"-class message and does not send when the
target equals the local player's own actor id) before transmitting the 8 bytes verbatim. The
trade flow does **not** use 2/30; it is documented here only to close the earlier open routing
gap. `CODE-CONFIRMED` (routing + 2-word shape); the deeper guild flows are out of scope for this
spec.

### 8.5 Trade-window field map (HUD; offsets relative to the trade-panel object)

(Added 2026-06-13.) The trade window is a HUD panel object; an implementer mirroring its layout can
use the recovered field offsets below. These are **in-memory panel offsets**, not wire offsets.
`CODE-CONFIRMED` (offsets) / `PLAUSIBLE` (semantics).

| Off (from panel base) | Meaning |
|---|---|
| +1528 | Own-side name label. |
| +1532 | Partner-side name label. |
| +1536 / +1544 / +1552 | The three base-1000 money digits; total = +1552 + 1000 × (+1544 + 1000 × +1536). |
| +1588 | Own-side item slot table — 40 slots, stride 20. |
| +2388 | Partner-side item slot table — 40 slots, stride 20. |

The own-side slot region (40 slots, stride 20) is the same region the **2/25** confirm manifest
walks to build its per-occupied-slot record list (§8.1).

---

## 9. Item move / split / stack

- **In-inventory move & equip-move** reuse the equip opcode **2/16** (`mode` + `from`/`to`
  slots, §4). No separate drag-A→B message was observed; inventory rearrange rides the
  slot-move semantics. `LIKELY`.
- **Stack split / merge** has **no dedicated opcode** pinned this pass. Quantity is carried
  as an i32 in the sell (2/20), trade-slot (2/24), and item-use paths; a split would
  plausibly reuse a slot-move minor with a quantity below the full stack. The exact split
  request is **`UNVERIFIED` / not found** — candidate minors to probe next are 2/22, 2/30,
  or an unanalysed small slot op. Do not implement split until this is pinned.
- The inbound item-panel slot updates that reflect moves/splits are **4/149
  `ItemPanelSlotChunk`** (per-slot 16-byte `ItemSlotRecord` chunks, framed as
  `{ slot_index:u8, pad:u8, ItemSlotRecord:16B }` — see `structs/item.md`) and **4/153
  `ItemPanelSlotRefresh`** (routing `confirmed`). `CONFIRMED` (4/149 framing per
  `structs/item.md`).

---

## 10. Constants recovered (numeric reference)

| Constant | Meaning | Conf |
|---|---|---|
| **Three inventory arrays** (bag / equip 20 / extra 120), all 16-byte records, shared `0xFF` empty marker | Bag `40×(bag_count+3)` + equip 20 + extra 120 (extra base at local-player +1260); one expiry sweep walks all three (added 2026-06-16). | CONFIRMED |
| **Bag soft-cap = `40 × (bag_count + 3)`** records | Carried-bag capacity; `bag_count` = per-player bag-expansion count (one unsigned byte); 0 expansions ⇒ 120 records (added 2026-06-16). | CONFIRMED |
| Extra array = 120 entries × 16 bytes | Third item record array on the local-player block, base +1260 (added 2026-06-16). | CONFIRMED |
| Rental/timed-item expiry warning ids 4043…4049 | Raised by the shared `0xFF`-marker expiry sweep (added 2026-06-16). CP949. | CONFIRMED |
| Equip array = 20 entries × 16 bytes (320 B) | Worn equipment + fixed slots; the array re-synced on 4/16 / 4/20 acks. | CONFIRMED |
| Player-actor item block mirrors the 20 × 16 equip array | Re-synced on 4/16 (equip-change) and 4/20 (NPC-sell) acks. | CONFIRMED |
| Equip slot 15 | Visual / appearance slot → triggers gear rebuild. | CONFIRMED |
| Equip slot 14 | Special weapon slot. | LIKELY |
| Equip slot 8 | Excluded from equip-onto-other and from the worn-item stat sum. | LIKELY |
| Quick-use slot stride = 8 bytes | Quick-use slot occupancy array. | LIKELY |
| Trade window max items = 40 (0x28) | 4/25 `item_count` cap; 40-entry trade array; 2/25 manifest cap. | CONFIRMED |
| Trade-item record = 16 bytes | Per item in the 4/25 finalize list. | CONFIRMED |
| 4/25 base = 28 (0x1C); `phase` at **+8** (4 = finalize, 0 = cancel); `item_count` at **+0x18** | Trade finalize selector (re-pinned 2026-06-16; supersedes the old phase@+0x10 / count@+0x14 reading). | CONFIRMED |
| 2/23 `mode`: 2 = request, 0 = accept | C2S trade request/accept selector (added 2026-06-13). | CODE-CONFIRMED |
| 2/24 `category`: 0 = money, 1 = item, 4 = add-item-with-confirm, 255 = set-money | C2S trade-slot category selector (added 2026-06-13). | CODE-CONFIRMED |
| 2/24 money composite: total = d2 + 1000×(d1 + 1000×d0) | Base-1000 3-digit money encoding from panel fields +1536/+1544/+1552 (added 2026-06-13). | CODE-CONFIRMED |
| 2/25 `mode`: 1 = confirm/lock, 0 = cancel | C2S trade-confirm selector; payload = 2 + 4×(occupied own-side slots), N ≤ 40 (added 2026-06-13). | CODE-CONFIRMED |
| 4/23 two-level selector: outer `select` at **+8** (1 = accept→run +10 phase, 0 = decline→reason@+9 cases 1–5), inner `phase` at **+10** (0 = cancel, 2 = incoming, 3 = accepted→open) | S2C trade-request-result. BOTH bytes live: +8/+9 = accept-vs-decline gate, +10 = window phase; trader ids at +4/+12/+16 (re-pinned 2026-06-16, restoring the +8/+9 outer selector dropped in 2026-06-13). | CONFIRMED |
| 2/30 op pair: `op`@+0, `id`@+4 | Guild / relation action channel; not a trade op (added 2026-06-13). | CODE-CONFIRMED |
| Upgrade motion 8 = success, 9 = fail | 4/50 enchant/upgrade animation. | LIKELY |
| Repair divisors 60 and 12 | 4/19 repair time = value/60 min (rem value%60); value/12, value%12. | LIKELY |
| Message id 59003 | Equip-onto-other rejection (slot-8 relation guard). CP949. | LIKELY |
| Message id 36003 | Item-shop-expired. CP949. | CONFIRMED |
| Message ids 36004 … 36011 and 36012 … 36027, trailer 36028 | Repair price / time template — two sub-tables (price column 36004–36011, duration column 36012–36027) + trailer 36028; factors 60 and 12 (re-pinned 2026-06-16). CP949. | CONFIRMED |
| Message id 36030 | "Cannot trade now" (2/23 send gate). CP949. | CONFIRMED |
| 2/24 validator ids: dup-in-trade-slot 710, insufficient-gold 600, open-shop-conflict 406 | C2S trade-slot-update pre-send rejection messages (added 2026-06-16). CP949. | CONFIRMED |
| 2/15 validator ids: trade-open 401, too-much-gold(coin) 884, free-slot/pickable 19 / 601 | C2S pickup pre-send rejection messages (added 2026-06-16). CP949. | CONFIRMED |
| Message id 2042 | "X wants to trade" request prompt (4/23 phase 2). CP949. | CODE-CONFIRMED (string-ref) |
| 4/24 `reason` 1 / 2 → 2 messages | Trade-slot-update reason map. | LIKELY |
| Sound id 862050101 | NPC purchase SFX. | LIKELY |
| Sound ids 863500001 / 863500002 / 863500003 | Trade-open SFX. | LIKELY |
| Visual id 350000063 | Trade-open visual cue. | LIKELY |
| Ground-item fallback model id = 201011001 | Shared default world model when an item's template model lookup fails (added 2026-06-13). | CODE-CONFIRMED |
| Coin / money ground-item effect id = 217000501 | Drop mode 0xFF / pickup coin branch (added 2026-06-13). | CODE-CONFIRMED |
| Ground-item actor record = 76 bytes | Pool stride for a ground-item world actor (added 2026-06-13). | CODE-CONFIRMED |
| Ground-item float height = +1.0 unit | Item sits 1 unit above terrain (added 2026-06-13). | CODE-CONFIRMED |
| Name-label display radius = 1000.0 units (1,000,000.0 squared) | One terrain cell; label drawn at item pos + (0,2,0) (added 2026-06-13). | CODE-CONFIRMED |
| Mob-drop auto-expire ≈ 1000 ms | Ground item from a Mob dropper auto-despawns after ~1 s (added 2026-06-13). | CODE-CONFIRMED |
| Gold cap on coin pickup = 9,999,999,990,000,000 | Pickup pre-validation rejects when amount + current gold exceeds this (added 2026-06-13). | CODE-CONFIRMED |
| Pickup level requirement: player level ≥ item level requirement | Pickup pre-validation; else error 10005 (player level field +1684 vs item field +228) (added 2026-06-13). | CODE-CONFIRMED |
| Pickup free-slot capacity threshold = 20 | Inventory free-slot count gate (marker byte 0xFF across the bag arrays), NOT a distance check (added 2026-06-13). | CODE-CONFIRMED |
| Drop SFX 862030106; coin-pickup SFX 862030105 | Ground-item A/V cues (added 2026-06-13). | LIKELY |
| Drop announce string 2146; pickup announce 10006 / 37003; other-player pickup notice 10018 | Ground-item chat cues, CP949 (added 2026-06-13). | LIKELY |
| Pickup error strings 10001…10005 | 4/15 failure subtypes 1…5; 10005 = level/eligibility (added 2026-06-13). CP949. | CODE-CONFIRMED |
| Rare-drop chat strings 64001 / 64002 | 5/14 `notice_flag` one-shot line (added 2026-06-13). CP949. | CONFIRMED |
| 5/14 spawn SFX 863600001 | Played for a fixed set of effect-id ranges on ground-item spawn (added 2026-06-16). | CONFIRMED |
| 4/15 pickup announce 10006 / 37003 / 37004 (own); 902 (others) | Pickup chat cues (refined 2026-06-16). CP949. | CONFIRMED |

> The "message ids" above are runtime **localized message codes** (CP949 strings), not
> opcodes and not enum values on the wire. Keep them as display constants only.

---

## 11. UNVERIFIED / open questions

1. **No live capture either pass; static-IDA only.** Client-side facts (opcode routing, C2S
   payload sizes, ack byte offsets, array strides/counts, validator gates and message ids,
   client timing constants) are `[confirmed]` from control flow on build **263bd994**. The
   on-wire **VALUE semantics** (enum meanings, server-decided amounts, +N enchant levels) remain
   `[capture/debugger-pending]`. The 2026-06-16 re-verification made **every C2S size byte-exact**
   and re-pinned the 4/23 and 4/25 ack offsets (items 2 below); a live capture is still owed for
   the wire-value numbers.
2. **(Resolved 2026-06-13.)** The **outbound trade finalize/confirm minor is 2/25** (a 2-byte
   header plus an own-side offer manifest, §8.1), and **2/30 is a guild / relation action
   channel** (§8.4), not a trade op. Open follow-up: confirm whether the server treats the 2/25
   manifest as authoritative or merely advisory (does it recompute the offer server-side?).
3. **Stack split / merge request** — no dedicated opcode found. Quantity is an i32 in the
   sell/trade/use paths; split likely reuses a slot-move minor. Probe 2/22 and small slot ops next
   (2/30 is now ruled out — it is the guild/relation channel).
4. **Equip level / class requirement** — not enforced on the client send path (only state
   gates + slot-8 relation guard). Eligibility appears server-authoritative; confirm
   whether (and where) the client greys out ineligible items (tooltip / template-compare
   path).
5. **2/16 bytes +5…+7** — treated as alignment padding; verify they are zero on the wire.
6. **2/24 item-category column order** — the leading `category` byte and the 64-bit money case
   are now decoded (corrected 2026-06-13), but for the item categories (1 / 4) the order of item
   index vs quantity among the `a`/`b`/`c` dwords is still inferred from argument order, not
   byte-verified.
7. **4/50 32-byte body** — the exact offset of the resulting +N enchant level is unknown
   (candidates: the bonus dwords analogous to `EquipSlotBody` +0x18/+0x1C/+0x20).
8. **2/50 16-byte payload** — not decoded field-by-field; confirm it is the *upgrade*
   commit and not a shared gathering/crafting channel commit.
9. **Relation-context value on a player actor** (the equip-onto-other guard) — its exact
   semantics (party / couple / duel context id) are unknown. Note it shares an offset with
   an item-actor set-bonus flag in `structs/item.md`, but the objects differ; do not
   conflate them.
10. **Slot-8 semantics** — what gear / quick category index 8 represents is unknown.
11. **4/17 layout** — routing + 8-byte 2/17 send confirmed, but the 4/17 ack field layout is
    not re-derived; decode against a capture. (4/16 is now partly pinned — 20-byte body, result@+8,
    slot-15 byte at +0x0B, success re-syncs the 20×16 array — §4.4; deeper +0x0A/+0x0B meanings
    still open.)
12. **2/19 sub-flags (`flag_a…d`, `arg`)** — zero on a plain open; their meaning on a
    purchase path is unknown.
13. **2/25 confirm-manifest record interior** (added 2026-06-13) — the three trailing bytes of
    each 4-byte own-side slot record are plausibly `{category, item-sub, count}`; confirm against
    a capture of a multi-item trade confirm.
14. **Ground-item drop modes 0 vs 2** (added 2026-06-13) — both the C2S 2/14 drop request and the
    4/14 drop-ack mode byte distinguish `0` = bag slot vs `2` = staged/fixed slot; which UI action
    emits which mode, and the exact resolution of the "staged" slot, is inferred.
15. **5/14 spawn body holes** (added 2026-06-13) — the 48-byte body shares a wire shape with the
    combat-effect-instance spawn; the bytes the ground-item handler does not consume (the hole at
    +16 and the +33…+43 region) are capture-unverified. Confirm the server sends a full 48 bytes
    for a drop.
16. **4/15 pickup success subtypes 100 / 101 / else** (added 2026-06-13) — the three success
    sub-paths (share/party-split, plain insert, insert+echo) were read structurally; the server
    policy that selects each (party loot rules) is server-side and not fully decidable from the
    client. The echoed dwords at +12 / +0x1C / +0x20 are `PLAUSIBLE` only.
17. **Despawn authority** (added 2026-06-13) — 5/15 is the explicit ground-item despawn; the 4/15
    "else" success path also runs a local remover for the picker. Whether a 5/15 always follows a
    4/15 for the picker, or the client self-despawns and 5/15 is only for observers, is
    capture-unverified.
18. **Pickup physical proximity** (added 2026-06-13) — no explicit client-side metric distance
    gate was found on the 2/15 send path (the free-slot count of 20 is a capacity check, not
    units). Proximity is enforced by the click hit-test and presumably server-side. The C2S 2/15
    share-bytes (+4/+5/+6) carry a client-suggested destination free-slot index for non-coin
    pickups (and `(0xF0, 0, 0)` for coins); whether the server honours the suggestion or re-derives
    the slot is unknown.
19. **(Resolved 2026-06-16.)** The **4/23 two-level selector** (outer `select`@+8 accept-vs-decline
    with decline-reason@+9 cases 1–5; inner `phase`@+10) and the **4/25 offsets** (`phase`@+8,
    `item_count`@+0x18, 28-byte base) are now re-pinned (§8.2). Remaining: the per-reason decline
    text (4/23 +9 cases 1–5) and the runtime meaning of enum values stay `capture-pending`.
20. **4/14 `SmsgGroundItemDropAck` handler not located in the 2026-06-16 lane** (added 2026-06-16)
    — only the matching 2/14 C2S send builder (8 bytes) was re-confirmed. The §12.6 interior
    offsets (result@+8, mode@+10, slot@+11, count@+12, opaque@+16) are **doc-asserted /
    capture-pending** until the handler is walked or a capture confirms them.

---

## 12. Ground items — drop, pickup, and world rendering

(Added 2026-06-13.) This section covers the **ground-item lifecycle**: items dropped into the
world, the requests that drop and pick them up, and the way the client renders them. All offsets
are CODE-CONFIRMED (static read/write order or in-memory pool stride) but **CAPTURE-UNVERIFIED**;
field *meanings* are graded individually. Korean text fields are CP949. All wire offsets are
payload-relative (relative to frame +8).

> **Headline:** ground items are **first-class 3D world actors**, *not* billboards and *not* a
> single shared bag mesh. Each dropped item resolves its own per-template world model; when that
> lookup fails the engine substitutes one **shared default fallback model (id 201011001)**, a
> generic sack/bag mesh. Items are tracked in a dedicated registry (the "ItemMap") and inserted
> into the terrain spatial grid like any other actor, with a floating name label.

### 12.1 Lifecycle and opcode map

| Opcode | Dir | Size (bytes; read count) | Canonical name (proposed) | Role |
|---|---|---|---|---|
| 4/4 tag 4 | S2C | 24-byte tag record (inside the area stream) | (part of `SmsgAreaEntitySnapshot`) | Bulk area spawn: a ground item already present in the visible area on (re)entry/refresh. |
| 5/14 | S2C | 48 (read 0x30) | `SmsgGroundItemSpawn` (a.k.a. combat-effect-instance spawn) | The live "an item just dropped in the world" push (monster drops, combat-effect drops, coin drops). |
| 4/14 | S2C | 20 (read 0x14) | `SmsgGroundItemDropAck` | Result of a **drop-from-inventory** request; spawns the dropped item at the dropper's feet. |
| 4/15 | S2C | 36 (read 0x24) | `SmsgItemWorldPickupAck` | Result of a **pickup** request; inserts the item into a bag slot (or coin into gold) and announces it. |
| 5/15 | S2C | 16 (read 0x10) | `SmsgGroundItemRemove` | Despawn a ground item (someone picked it up); optional "X picked up Y" notice. |
| 2/14 | C2S | 8 | `CmsgDropItem` | Drop an inventory item / coin stack to the ground. |
| 2/15 | C2S | 12 | `CmsgPickupItem` | Pick up a targeted ground item. |

Routing for 4/14, 4/15, 5/14, 5/15 and the 5/106 trade-state toggle is `confirmed` in
`opcodes.md`; the 4/4 tag stream is the area entity snapshot. The matching packet YAMLs
(2-14, 2-15, 4-14, 4-15, 5-14, 5-15) are not yet authored — this section is their design input.

### 12.2 C2S 2/14 `CmsgDropItem` (8 bytes)

Drops an inventory item or coin stack to the ground at the local player's feet.

| Off | Size | Field | Meaning | Conf |
|---|---|---|---|---|
| +0 | u8  | `mode`  | `0` = bag slot, `1` = equip slot, `2` = staged/fixed slot, `0xFF` = coin. | CODE-CONFIRMED |
| +1 | u8  | `slot`  | Slot index in the relevant array. | CODE-CONFIRMED |
| +2 | 2 B | (pad)   | Uninitialised padding. | PLAUSIBLE |
| +4 | i32 | `count` | Item count / coin amount. | CODE-CONFIRMED |

The pre-send validator requires the in-game / action-ready state flags. For a coin drop
(`mode = 0xFF`) it checks the amount does not exceed current gold and **decrements local gold
immediately** (client-side prediction); for an item drop it resolves the item from the slot array
and requires the item's droppable flag. The drop coin id is **217000501**.

### 12.3 C2S 2/15 `CmsgPickupItem` (12 bytes)

Picks up a targeted ground item. The target is the clicked / stood-on ground-item actor.

| Off | Size | Field | Meaning | Conf |
|---|---|---|---|---|
| +0 | i32 | `ground_item_key` | Target ground-item entity id (the world key). | CODE-CONFIRMED |
| +4 | u8  | `share0` | Coin path sets `0xF0`; otherwise the destination free-slot index. | CODE-CONFIRMED |
| +5 | u8  | `share1` | Free-slot index `>> 3` (else 0 for coins). | CODE-CONFIRMED |
| +6 | u8  | `share2` | Free-slot index `& 7` (else 0 for coins). | CODE-CONFIRMED |
| +7 | 1 B | (pad)    | Padding. | PLAUSIBLE |
| +8 | i32 | `amount` | Coin amount or stack size. | CODE-CONFIRMED |

**Client-side pickup pre-validation** (in order; all CONFIRMED, all enforced before send;
message ids pinned 2026-06-16):

1. **Lock-state gate** — if the local player is locked (in trade / dialog / menu), deny.
2. **Busy gate** — if the busy predicate is set, deny.
3. **Trade-open gate** — if a trade is open, deny with msg **401**.
4. **Coin special** — if the target item id is the coin id (**217000501**): reject when
   `amount + current_gold` would exceed the **gold cap 9,999,999,990,000,000** with the
   "too much gold" msg **884**; otherwise set the share-bytes to `(0xF0, 0, 0)` and send.
5. **Non-coin** — resolve a free destination bag slot; if the item is **non-pickable** (its
   pickable flag is clear) or the inventory **free-slot capacity** gate fails (the count of empty
   bag slots, threshold **20** — a capacity gate, **not** a distance check), show msg **19**
   (and/or msg **601**); and check the **level requirement** (player level ≥ the item's
   level-requirement field — the player level field at +1684 vs the item field at +228) — on
   failure, error string **10005**.
6. On success — send 2/15 with the target key, amount, and the share-byte destination hint, and
   stamp the current time as a last-pickup rate gate.

No explicit client-side metric pickup radius was found on the send path; physical proximity is
enforced by the click hit-test (the target must be the picked ground-item actor) and presumably
server-side (§11 #18).

### 12.4 S2C 4/4 tag-4 ground-item spawn record (24 bytes, inside the area stream)

The area entity snapshot (4/4) carries a tag loop; **tag 4** is a ground item present in the
visible area on (re)entry/refresh. Each tag-4 body is 24 bytes (read 0x18).

| Off | Size | Type | Meaning | Conf |
|---|---|---|---|---|
| +0  | 4 | u32 | item **key** (entity id; the pickup target id / ground-item actor key) | CODE-CONFIRMED (placement) |
| +4  | 4 | u32 | item **template id** (resolves the 3D model; 0 / unknown → fallback 201011001) | CODE-CONFIRMED (placement) |
| +8  | 4 | — | unused (passed to the spawner but not stored in the read fields) | PLAUSIBLE |
| +12 | 4 | i32 | **opaque** (likely quantity or flags) | CODE-CONFIRMED (placement) |
| +16 | 4 | f32 | world **posX** | CODE-CONFIRMED |
| +20 | 4 | f32 | world **posZ** | CODE-CONFIRMED |

A zero / empty tag-4 body produces the fallback model 201011001 at the origin. Ground-item despawn
does **not** come from 4/4 — the area stream is additive / prune-by-rewalk, and the whole ItemMap
registry is flushed on map change.

### 12.5 S2C 5/14 `SmsgGroundItemSpawn` (48 bytes, read 0x30)

The live "an item just dropped" push (monster drops, combat-effect drops, coin drops). This opcode
shares a wire shape and handler with combat-effect-instance spawns; only the named fields below are
consumed for a ground item.

| Off | Size | Type | Meaning | Conf |
|---|---|---|---|---|
| +0  | 4 | u32 | **sort** (low byte `2` = Mob dropper ⇒ the item auto-expires ~1000 ms) | CODE-CONFIRMED |
| +4  | 4 | u32 | **dropper / source actor id** | CODE-CONFIRMED |
| +8  | 1 | i8  | **mode** (`0xFF` = coin ⇒ effect id forced to 217000501) | CODE-CONFIRMED |
| +9  | 1 | u8  | **slot** (source slot index on the dropper) | CODE-CONFIRMED |
| +12 | 4 | i32 | **effect / template id** (the ground-item 3D model id) | CODE-CONFIRMED |
| +16 | 4 | — | (hole; not read into a named field) | CODE-CONFIRMED (unread) |
| +20 | 4 | i32 | **param1** (→ ground-item actor opaque slot) | CODE-CONFIRMED |
| +24 | 4 | i32 | **param2** (→ ground-item actor key) | CODE-CONFIRMED |
| +28 | 4 | f32 | world **posX** | CODE-CONFIRMED |
| +32 | 4 | f32 | world **posZ** | CODE-CONFIRMED |
| +44 | 1 | u8  | **notice_flag** (`1` ⇒ one-shot "rare drop" chat line, strings 64001 / 64002) | CODE-CONFIRMED |

A Mob dropper (sort low byte `2`) makes the spawned item auto-expire after ~1000 ms. Certain
effect-id ranges play a spawn/pickup SFX — the spawn plays **sound id 863600001** for a fixed
set of effect-id ranges (e.g. 215003001–215003003, 215003408 / 215003708, 215004008,
217000028–217000037, 217000241–217000243, 273000080, 273000766). The bytes the handler does not
consume (+16, +33…+43) are capture-unverified (§11 #15). `CONFIRMED` (the SFX trigger and id
ranges; re-verified 2026-06-16).

### 12.6 S2C 4/14 `SmsgGroundItemDropAck` (20 bytes, read 0x14)

Result of a **drop-from-inventory** request; on success it spawns the dropped item at the local
player's current position.

> **Re-verification note (2026-06-16):** the 4/14 *handler* was **not located** in the
> 2026-06-16 re-confirm pass (only the matching 2/14 C2S send builder + its 8-byte size were
> re-verified). The offsets below remain **doc-asserted / `capture-pending`** — confirm them
> against a capture or a later handler walk before relying on the interior layout (§11 #G8).

| Off | Size | Type | Meaning | Conf |
|---|---|---|---|---|
| +0..+7 | 8 | — | header / echo region | PLAUSIBLE |
| +8  | 1 | u8  | **result** (`0` = error → UI error; `1` = ok → spawn the drop) | capture-pending |
| +10 | 1 | u8  | **mode** (`0` = bag slot, `1` = equip slot, `2` = fixed slot, `0xFF` = coin) | capture-pending |
| +11 | 1 | u8  | **slot** index | capture-pending |
| +12 | 4 | i32 | **count / coin amount** | capture-pending |
| +16 | 4 | i32 | **opaque** (→ ground-item actor opaque slot) | capture-pending |

On `result == 1` the client reads the drop sub-record (mode / slot / count / opaque), resolves the
item id (bag, equip, or coin — coin forces id 217000501), and spawns the ground item at the local
player's current world position. Drop SFX **862030106**, drop announce string **2146**. The
*behaviour* is consistent with the 2/14 send shape; the *byte offsets* are capture-pending.

### 12.7 S2C 4/15 `SmsgItemWorldPickupAck` (36 bytes, read 0x24)

Result of a **pickup** request; inserts the picked item into a bag slot (or a coin into gold) and
announces it.

| Off | Size | Type | Meaning | Conf |
|---|---|---|---|---|
| +0..+7 | 8 | — | valid / echo region | PLAUSIBLE |
| +8  | 1 | u8  | **result** (`0` = error; non-zero = ok) | CODE-CONFIRMED |
| +9  | 1 | u8  | **subtype** — see below | CODE-CONFIRMED |
| +12 | 4 | i32 | echoed dword (use-flag / slot region) | PLAUSIBLE |
| +16..+0x17 | — | — | echoed bytes (consumed by the slot insert) | PLAUSIBLE |
| +0x18 (24) | 4 | u32 | **item / actor id** (world lookup key; == 217000501 ⇒ coin) | CODE-CONFIRMED |
| +0x1C (28) | 4 | i32 | echoed dword | PLAUSIBLE |
| +0x20 (32) | 4 | i32 | echoed dword | PLAUSIBLE |

The **subtype** byte at +9 is interpreted by result:
- **Failure** (`result == 0`): subtype values `1…5` select error strings **10001…10005**
  (10005 = a level / eligibility error, also used at request time, §12.3).
- **Success** (`result != 0`): subtype selects the insert path —
  - **100** = share / party-split: find the next free bag slot, insert, refresh the slot UI; if no
    slot is free, fall back to a name announce ("you got %s", strings 10006 / 37003).
  - **101** = plain insert.
  - **else** = insert plus a local despawn/echo on the picker's grid, then announce.

If the id at +0x18 equals the coin id **217000501** the pickup is a coin: it credits gold and
plays coin SFX **862030105** instead of an item insert. The own pickup announce uses strings
**10006** / **37003** / **37004**; the announce to other players in the same context uses
**902**. The exact server policy choosing subtype 100 vs 101 vs else is server-side (§11 #16).
`CONFIRMED` (size 36, result@+8, subtype@+9, the 100/101/else split, the 1–5 → 10001–10005 failure
map, the coin branch; re-verified 2026-06-16).

### 12.8 S2C 5/15 `SmsgGroundItemRemove` (16 bytes, read 0x10)

Despawns a ground item (someone picked it up).

| Off | Size | Type | Meaning | Conf |
|---|---|---|---|---|
| +0 | 4 | u32 | **sort** (of the picker actor) | CODE-CONFIRMED |
| +4 | 4 | u32 | **id** (the actor that picked it up) | CODE-CONFIRMED |
| +8 | 4 | u32 | **tracked_id** (the ground-item key to remove from the ItemMap) | CODE-CONFIRMED |
| +0xC (12) | 1 | u8 | **notify_flag** (`1` ⇒ "X picked up Y" notice) | CODE-CONFIRMED |

The client looks the ground item up in the ItemMap by `tracked_id`; if `notify_flag` is set, the
picker is another player sharing the local player's relation / party context, and the item is not a
coin, it shows a green "X picked up Y" line (string **10018**). It then removes the item from its
terrain grid cell and frees the registry entry. (Despawn authority vs the 4/15 local-remove path is
§11 #17.)

### 12.9 Ground-item world actor and rendering

When a ground item spawns (from any of 4/4 tag 4, 5/14, or 4/14), the client:

- **Resolves a 3D model** from the item's template id. If the lookup returns nothing, it falls back
  to the **shared default model id 201011001** (a generic sack/bag mesh). So every ground item is a
  per-template 3D world actor with one shared fallback — never a billboard icon.
- **Allocates a 76-byte ground-item actor record** and inserts it into the terrain spatial grid
  cell, and registers it in the ItemMap registry. Salient in-memory record fields:

  | Off | Size | Type | Meaning |
  |---|---|---|---|
  | +0  | 4 | f32 | scale (= 2.0) |
  | +20 | 4 | f32 | world X (wire X + spiral offset) |
  | +24 | 4 | f32 | world Y (terrain height + 1.0) |
  | +28 | 4 | f32 | world Z (wire Z + spiral offset) |
  | +32 | 4 | i32 | opaque (from wire opaque / param1; likely quantity / flags) |
  | +36 | 4 | i32 | grid cell X |
  | +40 | 4 | i32 | grid cell Z |
  | +44 | 4 | i32 | key (the wire entity id; despawn / pickup lookup key) |
  | +48 | 4 | ptr | resolved 3D model pointer (the item's world model, or the 201011001 fallback) |
  | +52..+68 | 16 | f32×4 | color / tint (= 1,1,1,1) |
  | +72 | 4 | u32 | expire-at-ms (= now + 1000 when the timed flag is set, e.g. Mob drops) |

- **Floats the item +1.0 unit above terrain** (world Y = terrain-height query + 1.0).
- **Fans out overlapping drops in a log/sin spiral** so stacked drops at the same coordinate do not
  visually overlap: a ring radius starts at 1, grows by 2, and wraps back to 1 when it exceeds 6;
  the placed position is `X = (ring / 2) × logf(angle) + wireX`, `Z = (ring / 2) × sinf(angle) +
  wireZ`.
- **Auto-expires Mob drops after ~1000 ms** (the +72 expire-at-ms field is set when the dropper is
  a Mob).
- **Draws a floating name label** above each ground item when the local player is within **1000
  world units** (one terrain cell; the gate is a squared distance of 1,000,000.0). The label is
  drawn at the item position + (0, 2.0, 0) and resolves the item template's display name.

A re-implementation should model ground items as pooled scene actors keyed by the wire entity id,
with the spiral anti-overlap placement, the +1.0 float, the Mob-drop expiry timer, and the
1000-unit label radius — and use 201011001 as the fallback model whenever the per-template model is
missing.
