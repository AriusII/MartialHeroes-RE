<!--
verification: routing/wire-layout [confirmed] (the delivery-inbox claim opcode and its 4-byte body,
  the delivery-record inbound opcode and its 140-byte fixed size with the 8-slot sender/money/5-item
  panel model, the carrier-pigeon send opcode and its 132-byte fixed size, and the NPC-kind-23 entry
  point that opens the carrier-pigeon read panel are control-flow / size-confirmed on build 263bd994
  by static analysis);
  the carrier-pigeon RECEIVE / read-side inbound opcode was NOT found distinct from the delivery
  record and is UNVERIFIED (capture/debugger-pending); the carrier-pigeon send body field split past
  the attached-item array is static-only and partial (exact widths capture/debugger-pending);
  all on-wire VALUE meanings (the claim-index selection, the mode/op selector, the subAction codes,
  the per-item record contents) are RUNTIME-ONLY (server-authoritative / capture-debugger-pending).
ida_anchor: f61f66a9ae0ec1e946105b2ecff76e8930cb1d1367df64e5688a5266f5ad9963
ida_reverified: 2026-06-20
ida_reverified: 2026-06-27
evidence: [static-ida]
sample_verified: false
note: |
  IDB SHA 263bd994, CYCLE 7 (2026-06-20) — new spec covering the delivery-inbox / attachment flow
  (claim 2/71, record 4/70) and the carrier-pigeon SEND (2/70), plus the NPC-kind-23 entry point.
  EXCLUDED by design: opcode 2/60 is the couple / marriage request, NOT mail — see §6.
  CYCLE 14 re-anchor (f61f66a9, 2026-06-27) — confirmatory pass: CarrierPigeonPanal / CarrierPigeonReadPanel / CarrierPigeonSendPanel / DeliveryPanel RTTI and carrier-pigeon UI asset strings cleanly relocated, NPC-kind-23 subsystem intact. 1 re-confirmed SAME, 0 corrected.
-->

# Mail — Delivery Inbox & Carrier-Pigeon — Clean-Room Specification

> Neutral, rewritten specification promoted from dirty-room analyst notes under **EU Software
> Directive 2009/24/EC Art. 6** (decompilation permitted solely to achieve interoperability). It
> contains **no decompiler output, no pseudo-code, no legacy symbol names, and no binary virtual
> addresses**.
>
> **Spec path (cite this):** `// spec: Docs/RE/specs/mail.md`
>
> **Scope.** The client's player-to-player **delivery / attachment** subsystem — how the player
> claims a queued delivery (an inbox of sender + money + attached items), and how the player composes
> and sends a **carrier-pigeon** message. Two distinct but related flows live here:
> - **Delivery inbox** — claim a queued delivery slot (C2S `2/71`); receive the inbox state (S2C `4/70`).
> - **Carrier-pigeon send** — compose and dispatch a message to another player (C2S `2/70`).
>
> The opcode framing/dispatch is owned by neighbours and cited, not duplicated:
> - `opcodes.md` — the 8-byte wire frame header + the opcode catalogue of record.
> - `npc_interaction.md` — the NPC interaction dispatcher and the NPC-kind table (Lane 2): the
>   **kind-23 carrier-pigeon NPC** entry point is documented there and cross-referenced here.
> - `social.md` — the relation / couple / marriage subsystem that owns opcode `2/60` (explicitly
>   **excluded** from this spec — see §6).

---

## 1. The two mail flows — one shared inbox model

The subsystem has **two distinct player-facing flows**, both built around the same per-slot model of
*sender name + attached money + up to five attached item records*:

| Opcode | Dir | Name | Wire size | Flow | Status |
|--------|-----|------|-----------|------|--------|
| **2/71** | C2S | **CmsgDeliveryClaim** | 4 bytes (fixed) | claim a delivery-inbox slot | CONFIRMED (static) |
| **4/70** | S2C | **SmsgDeliveryRecord** | 140 bytes (fixed) | the 8-slot delivery inbox state | CONFIRMED (static) |
| **2/70** | C2S | **CmsgCarrierPigeonSend** | 132 bytes (fixed) | compose & send a carrier-pigeon | CONFIRMED size (static); field split partial |

The **delivery inbox** (`2/71` + `4/70`) is the receive-and-claim half: the player opens the inbox,
the server pushes the inbox contents, and the player claims a slot to take its attachment. The
**carrier-pigeon send** (`2/70`) is the compose-and-dispatch half. A dedicated carrier-pigeon
*receive* opcode distinct from `4/70` was **not** recovered — see §5.

---

## 2. Delivery-inbox claim — outbound `2/71 CmsgDeliveryClaim`

The client requests / claims a delivery-inbox slot with **C2S `2/71`**, a **fixed 4-byte body**
carrying a single inbox-slot index.

| offset | size | type | field | notes |
|--------|------|------|-------|-------|
| +0 | 4 | i32 | `claimIndex` | the delivery-slot index to claim — a selection into the inbox list |

The same request serves two purposes: it is sent **on open** (with the index `0`) to ask the server
for the inbox contents, and it is sent again with a **selected slot index** when the player confirms a
claim on a specific entry. The server answers each claim with another `4/70` reflecting the new inbox
state (§3). *([confirmed]* the opcode and the 4-byte single-index body.)*

> **RUNTIME-ONLY:** the exact semantics of the index value (which slot maps to which entry, and the
> server's interpretation of the open-vs-claim distinction) are server-contract-dependent and stay
> capture/debugger-pending.

---

## 3. Delivery-inbox record — inbound `4/70 SmsgDeliveryRecord`

The server delivers the inbox state with **S2C `4/70`**, a **fixed 140-byte body**. The client holds
an **8-slot inbox**; each record updates one slot, and each slot carries a **sender name**, an
**attached money** amount, and **up to five attached item records**.

### 3.1 Body layout (body-relative, after the 8-byte frame header)

| offset | size | type | field | notes |
|--------|------|------|-------|-------|
| +8 | 1 | u8 | `resultCode` | `0` = fail (raises an inbox notice), `1` = ok / apply |
| +10 | 1 | u8 | `subAction` | selects how the record mutates the slot — see the sub-action table below |
| +11 | 17 | char[17] CP949 | `senderName` | the sender's name (CP949, NUL-terminated within the field) |
| +28 | 8 | i64 | `money` | attached money amount (credited / debited per `subAction`) |
| +56 | 80 | record[5] | `attachedItems` | up to five attached item records, **16 bytes each**, copied in order |
| +136 | 4 | i32 | `entryKey` | the delivery/entry identity — the match key against the 8-slot inbox |

The total body is **140 bytes** = the header-relative span through +136 (`entryKey`) plus its 4 bytes.
*([confirmed]* the 140-byte size and the field offsets/sizes above by static analysis of the apply
path.)*

### 3.2 Sub-action selector (`subAction`, +10)

The `subAction` byte selects how the incoming record is applied to the addressed inbox slot:

| value | meaning |
|-------|---------|
| **0** | add / replace the inbox entry (fills sender + money + items into a slot) |
| **1** | credit money (apply the `money` field at +28 as a gain) |
| **2** | item update |
| **3** | panel refresh (re-render the inbox without a slot-content change) |
| **4** | debit money (apply the `money` field at +28 as a loss) |
| **5** | item update (alternate) |

*([confirmed]* the sub-action codes drive the apply path; their exact server-side semantics are
RUNTIME-ONLY.)*

### 3.3 The 8-slot inbox model

The delivery panel maintains an **8-entry inbox**. Each entry is keyed by its `entryKey` (+136) so an
incoming `4/70` can match and update an existing slot rather than always appending. Per entry the panel
stores: the **sender name**, the **attached money**, and an array of **five attached item records**
(each a fixed 16-byte record). A `resultCode` of `0` raises an inbox notice ("nothing to claim /
failed") rather than mutating a slot. *([confirmed]* the 8-slot capacity and the per-slot
sender/money/5-item shape.)*

> **RUNTIME-ONLY:** the internal layout of each 16-byte attached-item record (item id, count, and any
> per-item metadata) is not asserted here — it is capture/debugger-pending.

---

## 4. Carrier-pigeon send — outbound `2/70 CmsgCarrierPigeonSend`

The player composes a carrier-pigeon message in the compose panel and dispatches it with **C2S
`2/70`**, a **fixed 132-byte body** carrying a recipient name, an optional attached money amount, and
up to five attached item slots. The compose path validates the buffer before sending (recipient name
non-empty, attached money within the player's balance, at least one non-empty item slot).

### 4.1 Body layout (partial — static-only past the item array)

| offset | size | type | field | notes |
|--------|------|------|-------|-------|
| +0 | 1 | u8 | `mode` | compose mode / operation selector (a small enumerated value, e.g. send vs clear) |
| +1 | 17 | char[17] CP949 | `recipientName` | the addressee's name (CP949, NUL-terminated within the field) |
| +20 | 8 | i64 | `money` | attached money (gold) — validated against the player's balance |
| +28 | 20 | i32[5] | `attachedItemSlotIds` | five item-slot references; a value of `-1` marks an empty slot |
| +48 | 84 | — | item metadata / padding | not fully field-split; the body is a fixed 132 bytes total |

The body size is **132 bytes (fixed)**. The fields through the five item-slot references (+28..+47)
are static-confirmed; the remaining span to 132 bytes is **static-only** (per-item metadata and/or
padding) and its exact internal widths are **capture/debugger-pending**. *([confirmed]* the 132-byte
size and the fields through the item-slot array; the tail split is UNVERIFIED.)*

> **RUNTIME-ONLY:** the `mode` selector's exact value set and the contents of the item-metadata tail
> are server-contract-dependent and stay capture/debugger-pending.

---

## 5. NPC entry point — kind 23 opens the carrier-pigeon read panel

The carrier-pigeon UI is reached through an **NPC of kind 23** (a carrier-pigeon NPC). The NPC
interaction dispatcher branches on the NPC's kind byte; the **kind-23 case opens the carrier-pigeon
read panel**, from which the player reaches the compose (send) panel. The NPC-kind dispatch table and
the kind→panel routing are owned by `npc_interaction.md` (Lane 2) and cross-referenced here rather than
re-documented.

Recovered flow (neutral prose):

1. **Delivery / attachment inbox.** Reached from a keep / storage NPC sub-menu, which opens the
   delivery panel. On open the panel auto-issues `2/71 CmsgDeliveryClaim` (index `0`) to request the
   inbox; the server pushes one or more `4/70 SmsgDeliveryRecord` (140 bytes each) that fill the
   8-slot inbox with sender + money + up to five attached item records. Selecting an entry and
   confirming sends `2/71 CmsgDeliveryClaim` with the chosen slot index; the server answers with
   another `4/70`. A `resultCode` of `0` raises an inbox notice.
2. **Carrier-pigeon send (compose).** Talk to the **kind-23** carrier-pigeon NPC → the carrier-pigeon
   root panel → the compose panel. The player fills in the recipient name, an optional attached money
   amount, and up to five item slots; submit validates the buffer and sends `2/70
   CmsgCarrierPigeonSend` (132 bytes).
3. **Carrier-pigeon read (receive).** The read panel is opened from the same kind-23 NPC. Static
   evidence shows it consuming actor / state pushes; **no dedicated carrier-pigeon inbox-list S2C was
   found distinct from `4/70`** — the receive side is **UNVERIFIED (capture/debugger-pending)**.

---

## 6. EXCLUSION — opcode `2/60` is NOT mail

> **Opcode `2/60` is the couple / marriage request — it belongs to the social / relation system, NOT
> to mail.** Although an early seed labelled it a "letter request", the binary shows it is built from
> the couple / marriage relation panel: its body carries a small **action selector** (propose / accept
> / decline / divorce family) plus a **target actor id**, and its inbound reply is the couple-pair
> relation push, not a delivery record. It has **no inbox, no money, and no attached items**.

**Do not re-add `2/60` to this spec.** It is owned by `social.md` (the relation / couple / marriage
subsystem). It is recorded here only as an explicit exclusion so a future reader does not re-conflate
the marriage "request" with mail. *([confirmed]* `2/60` is the couple/marriage request and is not part
of the mail/delivery family.)*

---

## 7. Capture / debugger residue (RUNTIME-ONLY)

The following remain open and require a capture or a live debugger session to settle:

- **Carrier-pigeon receive opcode** — no inbound carrier-pigeon inbox-list message distinct from
  `4/70` was recovered (§5). UNVERIFIED.
- **Carrier-pigeon send body tail** — the field split past the five item-slot references (the +48..+131
  metadata/padding span) is static-only (§4.1). UNVERIFIED widths.
- **All on-wire VALUE meanings** — the `claimIndex` selection (`2/71`), the `mode` selector (`2/70`),
  the `subAction` codes and the per-item 16-byte record contents (`4/70`) — are server-authoritative
  and stay capture/debugger-pending.

---

## 8. Cross-reference map

| This spec covers | Owned elsewhere — cite, don't duplicate |
|---|---|
| The delivery-inbox claim `2/71` and record `4/70`, the carrier-pigeon send `2/70` (§1–§4) | `opcodes.md` (catalogue of record), `packets/` (field specs) |
| The 8-byte wire frame header / opcode framing | `opcodes.md` |
| The NPC-kind-23 carrier-pigeon entry point and the NPC interaction dispatcher (§5) | `npc_interaction.md` (Lane 2) |
| The couple / marriage request `2/60` — **excluded** from mail (§6) | `social.md` (relation / couple / marriage subsystem) |
