---
status: hypothesis
sample_verified: false
---

# Inventory, Equipment, Shop & Trade — Clean-Room Specification

> Neutral, rewritten behavioural specification. No legacy symbols, no binary
> addresses, no pseudo-code. Describes the *observed behaviour*, state model,
> opcode tuples and constants of the legacy client's inventory / equip / quick-use /
> NPC-shop / item-upgrade / player-trade subsystems, so the .NET core can be
> reimplemented from scratch.
>
> Scope: the client send-side (C2S) of the item/trade family and the rules the
> client applies before sending or on receiving an ack. The detailed wire layouts
> of several server acks already live in `structs/item.md`; this spec references
> those and does **not** restate them field-by-field.

## Status & confidence

- **No live network capture was available for this pass.** Every field offset and
  message size below is a **static inference** unless a field is explicitly tagged
  `prior-capture-claim` (a previous analyst recorded it as observed on the wire).
  Treat all offsets/sizes as hypotheses to confirm against a capture.
- **Opcode → handler routing is a hard static fact.** The `(major:minor)` tuples in
  the opcode tables below were cross-checked against the client's dispatch table and
  the already-committed `opcodes.md`. The *direction* and *family* of each opcode are
  reliable; the *payload field layouts* are the hypotheses.
- **Per-claim confidence** is tagged inline as `CONFIRMED` (cross-checked at multiple
  sites or against an existing committed spec), `LIKELY` (single consistent site), or
  `UNVERIFIED` (inferred / boundary not pinned).
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

### 1.1 The flat 20-slot item array

The client keeps a single flat **20-slot item array** that holds both worn equipment
and bag items. Each entry is one 16-byte `ItemSlotRecord` (layout in `structs/item.md`:
a verbatim first dword, an `item_actor_id`, then a 64-bit expiry split into low/high).
The array is therefore **320 bytes** (20 × 16). `CONFIRMED`.

- An entry is read by slot index; the runtime resolves the entry, then uses its
  `item_actor_id` to locate the **item actor**, which carries the actual stat-grant
  fields (see `structs/item.md`, "Item-actor stat-grant fields"). `CONFIRMED`.
- This array is a **copy of the local player's own item block** carried inside the
  player actor's layout. On certain acks the client re-synchronises all 20 records
  from the player actor's item block back into this array (the NPC-sell ack does
  exactly this re-sync of 20 × 16 bytes on a successful self-sale — this is the
  structural proof that the model is 20 fixed slots of 16 bytes). `CONFIRMED`.

> Implementer note: model this as a fixed `ItemSlotRecord[20]` owned by the local
> player and mirrored into an inventory/trade controller. Both copies are kept in
> sync; treat the player-actor block as the authority and the standalone array as a
> cache that is refreshed on inventory-changing acks.

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
  counting the 8-byte frame header). `CONFIRMED` (send-only nature); `LIKELY`/inferred
  (individual sizes).
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
| 2/16 | C2S | 12 | `CmsgEquipChange`        | Equip / unequip / swap request (also inventory slot-move). See §4. | 4/16 + 4/12 |
| 2/17 | C2S | 8  | `CmsgQuickEquipSlotSet`  | Quick-equip / quick-use slot set. See §5. | 4/17 |
| 2/19 | C2S | 12 | `CmsgNpcInteractOpen`    | NPC interact / shop-open. See §7. | 4/19 (56 B) |
| 2/20 | C2S | 12 | `CmsgNpcSellItem`        | Sell one inventory item to an NPC. See §7. | 4/20 (24 B) |
| 2/22 | C2S | 8  | `CmsgUseItemOnTarget`    | Item-use targeted at an actor (tentative). | item-use acks |
| 2/23 | C2S | 8  | `CmsgTradeRequest`       | Player-trade request / accept / decline / cancel. See §8. | 4/23 |
| 2/24 | C2S | 20 | `CmsgTradeSlotUpdate`    | Add / update an item in your trade-window half. See §8. | 4/24 |
| 2/30 | C2S | 8  | (trade/social — role UNVERIFIED) | Social/interaction op near the trade path; possibly trade-finalize or a social-panel op. | — |
| 2/50 | C2S | 16 | `CmsgUpgradeItemCommit`  | Timed/progress-channel commit (item upgrade/enchant candidate). See §6. | 4/50 (likely) |

Confidence: routing (`major/minor` + direction) `CONFIRMED` for all of the above except
**2/30** (role `UNVERIFIED`) and **2/50** (commit purpose `LIKELY`, payload `UNVERIFIED`).
The detailed payload tables are in the relevant section per opcode.

> **Open routing gaps (flag for follow-up):**
> - **2/30**'s role is unknown; it sits near the trade path and could be a trade
>   finalize/confirm or a social op.
> - The **outbound trade-finalize/confirm minor is not pinned.** The *inbound* finalize
>   is observed as 4/25 with phase = 4 (§8); the matching C2S confirm minor is a candidate
>   among 2/30 or a second mode of 2/23.
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
  (`EquipItemResult`). On `result == 1` the client applies the slot to the item model;
  if the destination slot type is **15**, it then runs the visual / appearance gear
  rebuild. On `result == 0` it shows a generic error. `prior-capture-claim` for the
  layout; behaviour `LIKELY`.
- **4/16 `EquipChangeResult` (S2C)** — same family; routing `confirmed`. Behaves as the
  equip-move/swap result. Field layout not separately re-derived this pass; defer to a
  capture before decoding individual fields. `UNVERIFIED` (layout).

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
- On `result == 0 && reason == 1` the client shows the **item-shop-expired** message
  (id **36003**) and then formats a **repair-cost / repair-time table** from the NPC
  template. The repair table is computed from template values using the divisors **60**
  and **12** (i.e. `value / 60` and `value % 60` for one column, `value / 12` and
  `value % 12` for another), selecting localized format strings in the id range
  **36004 … 36028** (an eight-way branch per ones/tens, applied twice for two cost
  columns, with **36028** as the trailer). These ids are the repair-price / repair-time
  message templates. `LIKELY`.

  **Repair-table constants:** divisors **60** and **12**; message-id base **36004**;
  trailer id **36028**.

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

**4/20 `NpcSellItemAck` (S2C, 24 bytes)** — `prior-capture-claim`:

| Off | Size | Field | Meaning | Conf |
|---|---|---|---|---|
| +0 | 4 B | `header`      | Packet prefix. | LIKELY |
| +4 | i32 | `actor_id`    | Selling actor id. | LIKELY |
| +8 | u16 | `result`      | `0` = error, `1` = ok. | LIKELY |
| +10| u8  | `category`    | Echo of `sell_flag`. | LIKELY |
| +11| u8  | `inv_slot`    | Echo of `inv_slot`. | LIKELY |
| +12| i32 | `quantity`    | Quantity sold. | LIKELY |
| +16| u32 | `gold_total`  | Player gold **after** the sale; the client adopts this as authoritative. | LIKELY |
| +20| u32 | `gold_or_fame_hi` | High word of gold, or a second currency / fame value. | UNVERIFIED |

Behaviour on `result == 1`: apply the sale to the item model; when the selling actor is
the local player, **re-sync all 20 inventory records** from the player actor's item block
into the standalone 20-slot array (§1.1). `LIKELY`.

### 7.3 Related shop acks (already committed in `opcodes.md`)

A buy-variant region (4/13), an NPC shop-slot-clear ack (4/21), a shop-page-update
(3/8 `SmsgShopPageUpdate`), and the item-shop / cash-shop family (4/71, 4/113, 4/114,
4/115) are all routed `confirmed` in `opcodes.md`. They are referenced here for context
only; their layouts are out of scope for this spec.

---

## 8. Player-to-player trade

Direction of travel is from the client's point of view. All inbound trade handlers feed a
shared inventory/trade controller.

### 8.1 Client-emitted (C2S)

- **2/23 `CmsgTradeRequest` (8 bytes)** — request / accept / decline / cancel.

  | Off | Size | Field | Meaning | Conf |
  |---|---|---|---|---|
  | +0 | u8  | `mode`  | Encodes request vs accept vs decline vs cancel. Exact enum `UNVERIFIED`. | LIKELY |
  | +1 | 3 B | (pad)   | Alignment padding to the dword. | UNVERIFIED |
  | +4 | i32 | `value` | Target actor id (on request) or a response code (on accept/decline). | LIKELY |

  Field widths sum to 8 bytes (1+3+4). Sent under can-act gating; on a blocked state the
  client shows the **"cannot trade now"** message (id **36030**) and does not send.
  `LIKELY`.

- **2/24 `CmsgTradeSlotUpdate` (20 bytes)** — put / change an item + quantity in your half
  of the trade window. Gated by busy/dead checks before send.

  | Off | Size | Field | Meaning | Conf |
  |---|---|---|---|---|
  | +0 | u8  | `mode` | Operation mode. | LIKELY |
  | +1 | u8  | `sub`  | Sub-operation index. | LIKELY |
  | +2 | 2 B | (pad)  | Alignment padding to the dword. | UNVERIFIED |
  | +4 | i32 | `a`    | One of {slot, item, quantity}. Field assignment `UNVERIFIED`. | UNVERIFIED |
  | +8 | i32 | `b`    | One of {slot, item, quantity}. Field assignment `UNVERIFIED`. | UNVERIFIED |
  | +12| i32 | `c`    | One of {slot, item, quantity}. Field assignment `UNVERIFIED`. | UNVERIFIED |
  | +16| u8  | `x`    | Extra byte. | UNVERIFIED |
  | +17| u8  | `y`    | Extra byte. | UNVERIFIED |
  | +18| u8  | `z`    | Extra byte. | UNVERIFIED |
  | +19| 1 B | (pad)  | Alignment padding to 20 bytes. | UNVERIFIED |

  Field widths sum to 20 bytes (1+1+2+4+4+4+1+1+1+1). **The order of slot / item /
  quantity among `a`/`b`/`c` is `UNVERIFIED`** — pin against a capture before decoding.

- **Trade finalize / confirm (C2S)** — **not pinned this pass.** The inbound finalize is
  observed as 4/25 phase = 4 (§8.2); the matching outbound confirm minor is a candidate
  among **2/30** or a second mode of **2/23**. `UNVERIFIED`.

### 8.2 Server acks (S2C; all `confirmed` routing in `opcodes.md`)

- **4/23 `UserTradeRequestResult` (20 bytes)** — `result` at +8 (`0` = error, `1` = ok),
  `reason` at +9 (values 1…5 map to **five distinct decline-reason** messages). On
  `result == 1` the client opens the trade window (copies the 20-byte body into the trade
  controller and runs window setup). On `result == 0` it closes the panel and shows the
  reason message. `LIKELY`.
- **4/24 `UserTradeSlotUpdateResult` (44 bytes)** — `result` at +8, `reason` at +9
  (value 1 → one message, value 2 → another). On `result == 1` the client updates the
  trade-slot view; otherwise it refreshes / clears the view. `LIKELY`.
- **4/25 `UserTradeFullResponse` (28-byte base + item list)** — the finalize/commit
  message.
  - `phase` at +0x10: **4 = finalize / commit**, **0 = error / cancel** (cancel shows an
    error message). `LIKELY`.
  - On finalize: `item_count` at +0x14 (u8, **max 40**), then `item_count` × **16-byte**
    trade-item records read sequentially. `LIKELY`.
  - The trade window has a local **40-slot** capacity (a 40-entry trade array in the
    controller); each populated slot contributes a small UI record. `CONFIRMED` (capacity
    constant).

  **Constants:** max trade items = **40 (0x28)**; trade-item record = **16 bytes**;
  finalize phase = **4**; cancel phase = **0**.

- **5/106 `TradeStateToggle` (12 bytes)** — `sort` (u32) at +0, `id` (u32) at +4, `on`
  (u8) at +8. Toggles the target actor's **trade-busy flag**; for the local player it
  opens/closes the personal-shop UI and clears a secondary state on un-toggle, and it
  propagates the busy flag to a paired ("couple") actor. Plays trade-open SFX (sound ids
  863500001 / 863500002 / 863500003) and a visual cue (id 350000063) on enter. `LIKELY`.
- **5/42 `PlayerPairSystemNotice`** and **5/26 `LocalPlayerRelationSlot`** are adjacent
  relation / couple notices that touch the same actor relation/trade-busy fields. They are
  cross-references only and are **not** trade-core; out of scope here.

### 8.3 Trade state machine (client view)

```
IDLE
  └─(send 2/23 request)─► REQUESTED
                              └─(recv 4/23 result==1)─► WINDOW_OPEN
WINDOW_OPEN
  ├─(send 2/24 add/update)  ◄──(recv 4/24 slot-update result)─►  (view updates)
  ├─(recv 5/106 on)         ─► both parties marked trade-busy
  └─(confirm; minor UNVERIFIED)
        └─(recv 4/25 phase==4 FINALIZE, full item list)─► apply items, close ─► IDLE

Any of: 4/23 error, 4/24 error, 4/25 phase==0, or 5/106 off  ─► cancel / close ─► IDLE
```

The outbound **confirm** transition is the only unpinned edge (§8.1). `LIKELY` for the
overall shape; the confirm edge is `UNVERIFIED`.

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
| Item array = 20 entries × 16 bytes (320 B) | Flat equip + bag item array. | CONFIRMED |
| Player-actor item block mirrors the 20 × 16 array | Re-synced on inventory-changing acks. | CONFIRMED |
| Equip slot 15 | Visual / appearance slot → triggers gear rebuild. | CONFIRMED |
| Equip slot 14 | Special weapon slot. | LIKELY |
| Equip slot 8 | Excluded from equip-onto-other and from the worn-item stat sum. | LIKELY |
| Quick-use slot stride = 8 bytes | Quick-use slot occupancy array. | LIKELY |
| Trade window max items = 40 (0x28) | 4/25 `item_count` cap; 40-entry trade array. | CONFIRMED |
| Trade-item record = 16 bytes | Per item in the 4/25 finalize list. | CONFIRMED |
| 4/25 `phase` at +0x10: 4 = finalize, 0 = cancel | Trade finalize selector. | LIKELY |
| Upgrade motion 8 = success, 9 = fail | 4/50 enchant/upgrade animation. | LIKELY |
| Repair divisors 60 and 12 | 4/19 repair time = value/60 min (rem value%60); value/12, value%12. | LIKELY |
| Message id 59003 | Equip-onto-other rejection (slot-8 relation guard). CP949. | LIKELY |
| Message id 36003 | Item-shop-expired. CP949. | LIKELY |
| Message ids 36004 … 36028 | Repair price / time template table (base 36004, trailer 36028). CP949. | LIKELY |
| Message id 36030 | "Cannot trade now". CP949. | LIKELY |
| 4/23 `reason` 1…5 → 5 decline messages | Trade-request decline reason map. | LIKELY |
| 4/24 `reason` 1 / 2 → 2 messages | Trade-slot-update reason map. | LIKELY |
| Sound id 862050101 | NPC purchase SFX. | LIKELY |
| Sound ids 863500001 / 863500002 / 863500003 | Trade-open SFX. | LIKELY |
| Visual id 350000063 | Trade-open visual cue. | LIKELY |

> The "message ids" above are runtime **localized message codes** (CP949 strings), not
> opcodes and not enum values on the wire. Keep them as display constants only.

---

## 11. UNVERIFIED / open questions

1. **No live capture this pass.** Every field offset/size is a static inference unless
   tagged `prior-capture-claim`. All sizes should be byte-confirmed against a capture.
2. **2/30 role** and the **outbound trade finalize/confirm minor** — not pinned. 4/25
   phase = 4 is the inbound finalize; the matching outbound minor is a candidate among
   2/30 or an alternate mode of 2/23.
3. **Stack split / merge request** — no dedicated opcode found. Quantity is an i32 in the
   sell/trade/use paths; split likely reuses a slot-move minor. Probe 2/22, 2/30, and
   small slot ops next.
4. **Equip level / class requirement** — not enforced on the client send path (only state
   gates + slot-8 relation guard). Eligibility appears server-authoritative; confirm
   whether (and where) the client greys out ineligible items (tooltip / template-compare
   path).
5. **2/16 bytes +5…+7** — treated as alignment padding; verify they are zero on the wire.
6. **2/24 trade-slot fields `a`/`b`/`c`** — the slot / item / quantity ordering is
   unknown.
7. **4/50 32-byte body** — the exact offset of the resulting +N enchant level is unknown
   (candidates: the bonus dwords analogous to `EquipSlotBody` +0x18/+0x1C/+0x20).
8. **2/50 16-byte payload** — not decoded field-by-field; confirm it is the *upgrade*
   commit and not a shared gathering/crafting channel commit.
9. **Relation-context value on a player actor** (the equip-onto-other guard) — its exact
   semantics (party / couple / duel context id) are unknown. Note it shares an offset with
   an item-actor set-bonus flag in `structs/item.md`, but the objects differ; do not
   conflate them.
10. **Slot-8 semantics** — what gear / quick category index 8 represents is unknown.
11. **4/16, 4/17 layouts** — routing confirmed but field layouts not re-derived this pass;
    decode against a capture.
12. **2/19 sub-flags (`flag_a…d`, `arg`)** — zero on a plain open; their meaning on a
    purchase path is unknown.
