# Net Contracts — Client↔Server Request/Response master pairing (authoritative)

<!--
verification: routing/sizes [confirmed] (opcode->handler ROUTING, the C2S send sizes, the S2C
  handler read sizes/offsets, and the install-table slot occupancy are all control-flow-confirmed);
  pairing [confirmed] where a request maps to a named/installed reply slot, otherwise
  static-hypothesis (a best-consumer / in-flight-latch inference, not a captured round-trip);
  value-semantics [capture/debugger-pending] (every wire VALUE semantic — what a reply byte MEANS,
  which result code is success vs fail — has no live capture this campaign).
ida_reverified: 2026-06-20
ida_anchor: 263bd994
evidence: [static-ida]
capture_verified: false
re-pairing (CYCLE 4 Netcode Deep-Cartography promotion, the binary wins): authored LAST in the
  promotion wave, after the seven neighbour files (opcodes.md, handlers.md, network_dispatch.md, the
  packets/*.yaml majors 0-5, the structs) were updated; every pairing re-derived against those
  now-updated neighbours and cited. Pairing corrections applied: (1) 2/81 -> 4/61
  SmsgGuildStateChangeResult (4/81 = generic SmsgActionErrorResult; the stale 4/81 diplomacy reply is
  DROPPED) — resolves the old §2.9 Open-Q; (2) 2/151 selector 0 -> 3/8 SmsgShopPageUpdate, selector
  200 -> 4/113/4/114/4/115 (the "1/20" tie is DROPPED) — resolves the old §Open-Q2; (3) 2/146 reply =
  [u32 echoed req_id][u32 local state global] (8B), built inside the 5/146 handler; (4) 2/153
  CmsgProductConfirm reply opcode [capture/debugger-pending]; (5) 5/73 = SmsgQuestComplete (vs the
  stale SmsgGuildWarInfoUpdate). The §2.4/§2.9/§2.11 install-table refutations (4/44/4/46, 4/30,
  4/143) HOLD.
-->

The clean-room **contracts** view of the Martial Heroes wire protocol: it pairs every client
**Request** (C2S) with the server **Response(s)** (S2C) it provokes, as DTO-style contracts grouped
by feature domain. It is the request↔response companion to the flat `opcodes.md` catalog (the opcode
inventory) and `handlers.md` (the per-handler read map). When this file and `opcodes.md` disagree on
a name or a size, **`opcodes.md` is the catalog of record** and the divergence is carried in §Open
Questions below.

> **Routing/sizes/offsets are confirmed; wire VALUE semantics are capture/debugger-pending.** No live
> network capture was available during this analysis. Every opcode→routing link and every send/read
> size below is a hard static fact (read from the dispatch installer, the packet builders, and the
> handler read-sites). What stays **capture/debugger-pending** is the *meaning* of an individual wire
> byte and the **success-vs-fail polarity** of result codes. Each pairing also carries an explicit
> evidence/confidence column because most replies are **inferred**, not captured.

**Clean-room rules for this file:**
- **No IDA addresses, no Hex-Rays artifacts** (`sub_`/`loc_`/`_DWORD`/`__thiscall`/pseudo-C). Byte
  offsets and sizes are facts and are allowed.
- Direction is from the client's point of view: `C2S` (client→server) or `S2C` (server→client).
- Every opcode links to its field spec under `packets/`; cite the source spec for each derived fact.

---

## 1. Frame + contract model

### 1.1 The opcode and the frame

Every frame on the primary game connection begins with an 8-byte little-endian header — see
`opcodes.md §Wire frame header`:

| Offset | Size | Field   | Meaning |
|--------|------|---------|---------|
| +0     | u32  | `size`  | Total frame size in bytes including the 8-byte header. |
| +4     | u16  | `major` | Opcode high part / message family. |
| +6     | u16  | `minor` | Opcode low part / message id. |
| +8     | …    | payload | Body, read sequentially from payload offset 0. |

The **`(major,minor)` pair IS the opcode** (two separate LE u16 words on the wire; the `major<<16|minor`
hex used as a key is a catalog convention only). Payload offsets in this file are body-relative
(frame +8). The transform pipeline is asymmetric (outbound: timestamp→keyless cipher→LZ4; inbound:
LZ4-decompress only, **no inverse cipher** — `opcodes.md §Wire frame header`).

> **Framing-width note (carried for implementers).** The send builders write the frame `size` as a
> **u32**, but the inbound reassembly path reads a **u16 frame-length** when framing. The two agree in
> practice because every real frame is well under 64 KB (the high u16 of `size` is always zero), so the
> u16 read is safe; an implementer must nonetheless not assume a single width on both paths. *spec:*
> `network_dispatch.md`, `opcodes.md §Wire frame header`.

> **Nagle coalescing (carried for implementers).** Neither end disables Nagle's algorithm: the server
> leaves `TCP_NODELAY` unset (a maintainer-observed runtime fact, most pronounced for the **World
> Server** major-4/major-5 traffic), and the client sets only `SO_RCVBUF` — **never** `TCP_NODELAY`
> (`[confirmed]` from the client's sole `setsockopt` site). So **multiple framed messages may arrive
> coalesced in a single TCP segment, and one message may be split across segments** — in both
> directions. Always reassemble on the 8-byte header `size` word; never treat one `recv` (or one
> `send`) as one message. Full discussion: `network_dispatch.md §4.4a`.

### 1.2 Contract categories

Every C2S opcode falls into one of these contract shapes; the per-domain tables tag each row:

- **Request → Response (synchronous-style).** A dedicated same-family reply exists and is *installed*
  (a real handler at that slot). Example: `2/19 CmsgNpcBuyOrAcquire → 4/19 SmsgNpcBuyOrAcquireAck`.
  This is the strongest contract — both the route and the reply slot are confirmed.
- **Request → async-channel.** No same-minor ack; the server answers asynchronously on a shared
  world/state **Push** (major 5) or panel/inventory channel. Example: `2/13 CmsgMoveRequest → 5/13
  SmsgActorMovementUpdate` (the server re-broadcasts to all viewers); `2/28 CmsgQuestAction → 5/68
  SmsgQuestList`. The reply slot is installed but it is not a dedicated per-request ack.
- **Request → pending.** A reply clearly happens (a UI/panel updates, an in-flight latch clears) but
  the exact answering minor is **not statically pinned** — it rides an inventory/money/panel push.
  These need a capture to resolve.
- **Push (S2C, reactive, unsolicited).** Server-initiated world events with no matching request
  (e.g. another actor spawns, a buff ticks). Listed in `opcodes.md` major 5; out of contract scope
  here except where a request triggers one.
- **Fire-and-forget (C2S, no reply awaited).** Logout teardown, the two keepalives.
- **Reactive C2S (a reply, not a request).** Three sends are *built inside an inbound handler* — they
  are the client's automatic answer to a server message, not user commands: **1/4** (answers 0/0),
  **2/65** (answers a game-tick / guild-member-remove), **2/146** (answers 5/146 ack-request). The
  **2/146 reply** is built inside the 5/146 handler and carries an **8-byte body = `[u32 echoed req_id]
  [u32 local state global]`** — NOT the inbound token. (field-0 = the echoed request id; the field-1
  identity — a local context/state global vs a "counter" — is `[capture/debugger-pending]`, register
  item R-30.) *spec:* `network_dispatch.md §4.5a`, `handlers.md §5/146`.

### 1.3 The in-flight latch (the major-1/3/4 coupling mechanism)

A single **outstanding-request guard** ("in-flight latch") byte on the net-client singleton is the
request↔response coupling for the lifecycle/char-mgmt family — it is how a result handler knows which
request it is completing. The full SET/CLEAR roster (CYCLE 4 netcode-deep, control-flow-confirmed):

- **SET** by the send builders **`1/6`, `1/7`, `1/9`, `1/13`, `1/14`, `2/2`** (CmsgPortalTravel), and
  by the **`(0,0)→1/4`** handshake branch (the reactive credential reply sets it on send).
- **CLEARED** by the result handlers **`3/1`, `3/4`, `3/6`, `3/7`, `3/13`, `3/14`, `4/1`**.
- **READ** by the keepalive thread (it consults the latch when deciding whether a request is pending).
- **`1/0 CmsgLogout` sets NO latch** (fire-and-forget — the one lifecycle builder that does not arm it).

**The enter ladder.** Enter-world is the load-bearing chain `1/9 CmsgEnterGameRequest → 3/5
SmsgEnterGameAck → 4/1 SmsgGameStateTick`. The 1/9 send SETS the enter-game latch; **`4/1` (not `3/5`)
CLEARS it** — clearing the latch is `4/1`'s very first statement, before its form-A/form-B branch,
unconditional. `3/5` advances the scene but does **not** clear the latch. *spec:* `handlers.md`
(Group I — in-flight latch census; §4/1 latch note), `opcodes.md` (1/0, 1/9, 2/2 notes),
`network_dispatch.md §4` (outstanding-request guard + keepalive read).

### 1.4 Master coverage statement

The in-scope netcode is **100% DTO-covered at the STRUCTURE level** — every opcode carries a complete
DTO (size / offset / field-table / contract-class / canonical-name / pairing-status) across the
handshake (0/0) and the five majors: 0/0 ✓ · major-1 (4 S2C billing/letter + 7 C2S lifecycle + the
0/0-paired 1/4) ✓ · major-2 (95 distinct C2S opcodes / 96 builders, set-equal to the send census) ✓ ·
major-3 (11) ✓ · major-4 (100 installed slots + 2 specials {4/500, 4/50000}, 99 distinct handlers —
143/144 share) ✓ · major-5 (65 installed Push slots) ✓. **There is no slot/builder lacking a complete
structural DTO.** The single uniform residual is wire-byte **VALUE semantics** — the 42-item
`[capture/debugger-pending]` register in **Appendix C** — which by doctrine is NOT a DTO gap (sizes and
field layouts are recovered; only the *meaning* of certain bytes is unsettled). *source:*
`_dirty/netcode_deep/oq/completeness.md §1.6`.

### 1.5 Contract-class tally (whole-netcode, rolled up)

The per-domain tables tag every opcode with one contract shape (§1.2). Rolled up across all five majors
(from `_dirty/netcode_deep/oq/completeness.md §4.3`; counts are **±2 approximate** because the same
opcode can be classed F&F-at-builder / Req→Resp-by-intent — e.g. the quest submits):

| Contract class | Count | Where |
|---|---|---|
| **Fire-and-Forget** (C2S, no reply awaited) | ≈ 22 | 1/0, 1/2, ~20 major-2 builders |
| **Request → Response** (dedicated/installed reply, either direction) | ≈ 24 | 5 major-1 C2S, ~15 major-2, 4 major-3 (the result/ack S2C slots are the response half) |
| **async-channel / Request → pending** (reply rides a push/panel channel, no dedicated same-minor ack) | ≈ 68 | ~58 major-2 + ~10 major-5 async-answer + 3 major-3 |
| **pure Push** (S2C, server-initiated, no request) | ≈ 62 | 4 major-1 notices + 4 major-3 push + ~54 major-5 push + the major-4 notice/panel-push slots |
| **reactive** (a reply built inside an inbound handler) | 4 | 1/4 (answers 0/0), 2/65, 2/146 (answers 5/146), 5/146 |
| **out-of-table specials** | 2 | 4/500, 4/50000 |

The **dominant client-side shape is async/pending** (the in-session HUD/panel submits whose result rides
a shared channel); the **dominant server-side shape is pure Push** (the major-5 world/state broadcasts).
Only **4 truly reactive** opcodes exist (1/4, 2/65, 2/146, 5/146).

---

## 2. Per-domain contracts

Legend for the **Evidence / confidence** column:

- **confirmed/high** — pairing is a named `opcodes.md` row AND the reply slot is installed.
- **inferred/med** — reply slot is installed and is the best consumer match, but the round-trip is
  static inference only (not capture-confirmed).
- **async-channel** — no same-minor ack; the server answers on a shared Push/panel channel (slot
  installed).
- **pending** — a reply happens but no answering minor is statically resolvable (capture-required).
- **fire-and-forget / reactive** — see §1.2.

All wire VALUE meanings are `[capture/debugger-pending]` throughout; all routing/sizes are
`[confirmed]`.

### 2.1 Login / Handshake / Session

| Request (C2S) | size | Response(s) (S2C) | size | Evidence / confidence | packets/ |
|---|---|---|---|---|---|
| **0/0** SmsgKeyExchange *(inbound trigger)* | 62 | reactive: client sends **1/4** | var | confirmed/high — 0/0 is the hardwired handshake branch; the client immediately builds the 1/4 reply inside it. Frame major/minor + 0x2B prefix debugger-verified (prior cycle). | `0-0_key_exchange.yaml` → `login.yaml` |
| **1/4** CmsgAuthReply *(reactive credential carrier)* | var | (no awaited reply; connect outcome surfaces via the conn-state machine / 3/100 codes) | — | confirmed/high (send) — 1/4 is the REACTIVE reply to 0/0 (built in the inbound major-0 branch), carrying the `[0x2B][u32len account]([u32len PIN])` pre-image + the RSA-ciphered password. | `login.yaml` |
| **1/2** CmsgLobbyPing | 0 | server pong/ack (minor not pinned) | — | pending — a reply exists (the proxy worker clears its in-flight slot on the pong and stamps RTT) but the inbound minor is not statically isolated. | `cmsg_lobby_ping.yaml` |

*Notes.* The account **password** rides inside the 1/4 RSA ciphertext, not 1/6 (the "1/6 login-or-create
collision" is resolved — 1/6 is character-create only). The connect/disconnect progression is driven by
the conn-state machine and the 3/100 `SmsgCharActionResult` code set (§Appendix A, families 2 & 4), not
by a dedicated login-result opcode. *spec:* `opcodes.md` (0x10004, 0x10006), `crypto.md §6`.

**LIVE-CONFIRMED login-success sequence (CYCLE 4, replica `211.196.150.4`; IDA + live agree).** On a
successful `1/4`, the server pushes — unsolicited, no client follow-up — the **character roster on
`3/4`** (the in-place-refill form, header `form_byte0==1`), a `3 + popcount(slot_mask)×981` payload
(same decode as `3/1`; §2.2 + `login_flow.md §5.1`), then **`3/5`** a 44-byte account-ack (name@0,
billing@28, char_count@40). A live login returned `3/4` (2946B = 3 + 3×981, 3 characters, first name
"jeonsa") → `3/5` ("xwdvg26") → later `3/100` code 21. ⟹ the success path is **`3/4` (roster) → `3/5`
(account-ack) → `3/100`**, NOT a single `3/1`. (The `1/4` itself was the long-standing wall: a CYCLE-4
fix to the reply whitening span + the header-B-as-`k` block size — `crypto.md §6.2.2/§6.4` — flipped a
password-independent `3/100 code 10` rejection into this full success. Crypto/handshake now validated
1:1 against the live server.) Wire VALUE semantics (code 21, billing field, descriptor internals) stay
live-pending.

### 2.2 Character Management

The major-3 reply minors use the campaign-10 de-swapped labels: **3/4 = SmsgSceneEntityUpdate**,
**3/7 = SmsgCharManageResult**, **3/14 = SmsgCharSpawnResponse**, **3/100 = SmsgCharActionResult**.

| Request (C2S) | size | Response(s) (S2C) | size | Evidence / confidence | packets/ |
|---|---|---|---|---|---|
| **1/0** CmsgLogout | 0 | none (server drops the session) | — | fire-and-forget — the only char-mgmt builder that does not set the in-flight latch. | `cmsg_logout.yaml` |
| **1/6** CmsgCreateCharacter | 52 | **3/6** SmsgRenameCharResult *(create-result, shared handler)* + **3/4** SmsgSceneEntityUpdate refresh (+ possible **3/23** SmsgCharSelectStatusUpdate) | 12 / var / 28 | inferred/med — sets the latch; 3/4, 3/6, 3/23 all wired in the major-3 switch; the exact create-result minor is not capture-confirmed. | `cmsg_char_create.yaml`, `3-6_rename_char_result.yaml`, `3-4_scene_entity_update.yaml` |
| **1/7** CmsgManageCharacter *(select; DELETE overloads mode=1)* | 2 | **3/4** SmsgSceneEntityUpdate *(manage result, subtype 2)* — committed; **UNVERIFIED 3/7** alternative | var / 8 | inferred/med + **CONFLICT** — `opcodes.md` attributes the manage/delete result to 3/4 subtype 2 (authoritative); a fresh static read suggests an UNVERIFIED 3/7. Do not re-point on static evidence. The mode=1==DELETE semantic itself is capture-pending. (§Open Questions Q1) | `cmsg_char_select.yaml`, `3-4_scene_entity_update.yaml`, `3-7_char_manage_result.yaml` |
| **1/9** CmsgEnterGameRequest | 40 | **3/5** SmsgEnterGameAck → then **4/1** SmsgGameStateTick *(world snapshot, form B)* | 44 / var | confirmed/high — the documented enter ladder `1/9 → 3/5 → 4/1`. 1/9 SETS the enter-game latch; **`4/1` (not 3/5) CLEARS it** as its first statement (`handlers.md` Group I + §4/1). 4/1 = installed Response slot 1, the form-B world-entry packet. | `cmsg_char_enter.yaml`, `3-5_enter_game_response.yaml`, `4-1_game_state_tick.yaml` |
| **1/13** CmsgRenameCharacter | 18 | **3/6** SmsgRenameCharResult (+ **3/4** subtype-1 slot refresh) | 12 / var | inferred/med — sets the latch; 3/6 clears the pending latch and applies the new name on success. | `cmsg_char_rename.yaml`, `3-6_rename_char_result.yaml` |
| **1/14** CmsgMoveCharacter *(slot-move)* | 1 | **3/4** SmsgSceneEntityUpdate *(subtype refresh; exact subtype not pinned)* | var | inferred/med — slot-relocate rides the same 3/4 subtype-refresh family the other char-mgmt ops use; no dedicated 1/14 reply. | `cmsg_char_move.yaml`, `3-4_scene_entity_update.yaml` |

*Notes.* Server-pushed account notifications (`1/16` billing-off, `1/17` billing-on, `1/19` expiry,
`1/20` letter) are S2C-only major-1 pushes with no matching request — see §2.13 / §3. The 3/6 + 3/13
result handlers share the char-mgmt fail-code class (§Appendix A, family 3). *spec:* `handlers.md §2/§7`,
`opcodes.md`.

### 2.3 Movement / Combat

| Request (C2S) | size | Response(s) (S2C) | size | Evidence / confidence | packets/ |
|---|---|---|---|---|---|
| **2/13** CmsgMoveRequest *(click-move + arrival-stop + idle-resync + combat-reposition — all reuse this)* | 16 | **5/13** SmsgActorMovementUpdate *(server re-broadcasts to viewers)* | 40 | async-channel/high — 5/13 installed Push slot 13; the documented echo. | `2-13_move_request.yaml`, `5-13_actor_movement_update.yaml` |
| **2/52** CmsgUseSkill *(basic melee = 2/52 with SkillSlot 0xFF; no separate attack opcode)* | var | **5/52** SmsgActorSkillAction *(server broadcasts the skill action/damage)* | var | async-channel/high — 5/52 installed Push slot 52. | `2-52` *(see opcodes.md 0x20034)*, *(5/52 push)* |

*Notes (load-bearing).* The world move/combat stack is **closed on two opcodes**. Stop / turn / jump /
attack-target / skill-cancel / auto-attack are all folded into 2/13 or 2/52; there is **no dedicated
send** for any of them, so no additional movement/combat pairings exist. *spec:* `handlers.md`,
`opcodes.md` (2/13, 2/52 notes).

### 2.4 Inventory / Item

| Request (C2S) | size | Response(s) (S2C) | size | Evidence / confidence | packets/ |
|---|---|---|---|---|---|
| **2/14** CmsgDropItem | 8 | **4/14** SmsgGroundItemSlotAck | 20 | inferred/med — mirror-minor + `opcodes.md` "server answers with the 4/14 drop ack"; 4/14 installed. | `2-14_drop_item.yaml`, `4-14_ground_item_drop_ack.yaml` |
| **2/15** CmsgPickupItem | 12 | **4/15** SmsgItemWorldPickupAck | 36 | inferred/med — mirror-minor + `opcodes.md` "4/15 pickup ack"; 4/15 installed. | `2-15_pickup_item.yaml`, `4-15_item_world_pickup_ack.yaml` |
| **2/44** CmsgItemQuickMove | 12 | inventory/slot push — **NOT 4/44** | — | pending — **REFUTED naive mirror:** 4/44 is installed but is `SmsgActorTickTableOpA`. The quick-move result rides the inventory-slot acks (4/16 EquipChangeResult / 4/22 ItemSlotStateAck / 4/149 ItemPanelSlotChunk / 4/153 ItemPanelSlotRefresh — all installed). | `2-44_item_quick_move.yaml` |
| **2/46** CmsgItemMove | 12 | inventory/slot push — **NOT 4/46** | — | pending — same as 2/44: 4/46 is installed but is `SmsgActorTickTableOpC`. The drag-move result rides the inventory-slot acks. | `2-46_item_move.yaml` |

*Notes (load-bearing).* The canonical 12-byte item-model is shared by 2/44/2/46 (and partly 2/15), but
**its replies are NOT same-minor acks** — the item-move acks fold into the inventory-slot-update channel.
**Do not auto-generate 4/44 / 4/46 reply structs.** Adjacent item-state Response slots in this channel:
4/5 ItemUseResult, 4/12 EquipItemResult, 4/16 EquipChangeResult, 4/17 QuickEquipSlotAck, 4/22
ItemSlotStateAck, 4/50 UpgradeItemResult, 4/108 PlayerGoldBalanceUpdate, 4/149 ItemPanelSlotChunk,
4/153 ItemPanelSlotRefresh. *spec:* `opcodes.md`, `handlers.md`.

### 2.5 NPC / Shop

| Request (C2S) | size | Response(s) (S2C) | size | Evidence / confidence | packets/ |
|---|---|---|---|---|---|
| **2/19** CmsgNpcBuyOrAcquire *(inventory-driven buy)* | 12 | **4/19** SmsgNpcBuyOrAcquireAck | 56 | confirmed/high — `opcodes.md` routed; 4/19 installed. (Storefront 2/115 also lands here.) | `2-19_npc_buy_or_acquire.yaml`, `4-19_npc_buy_or_acquire_ack.yaml` |
| **2/20** CmsgNpcSell | 12 | **4/20** SmsgNpcSellItemAck | 24 | confirmed/high — `opcodes.md` routed; 4/20 installed. | `2-20_npc_sell.yaml`, `4-20_npc_sell_item_ack.yaml` |
| **2/115** CmsgShopBuy *(storefront)* | 8 | **4/19** SmsgNpcBuyOrAcquireAck (+ balance **4/115** SmsgItemShopBalanceUpdate) | 56 / 24 | confirmed/high — `opcodes.md` routed; 4/19 + 4/115 installed; `row*2` derivation confirmed at caller. DISTINCT from inventory-buy 2/19. | `2-115_shop_buy.yaml`, `4-19_npc_buy_or_acquire_ack.yaml`, `4-115_item_shop_balance_update.yaml` |
| **2/113** CmsgRepairCommit | 8 | **4/113** SmsgItemShopPurchaseResult *(4/113-family ack)* | 12 | inferred/med — `opcodes.md` "answers via the 4/113-family ack"; 4/113 installed. | `2-113_repair_commit.yaml`, `4-113_item_shop_purchase_result.yaml` |
| **2/100** CmsgNpcOpenReq *(service-NPC open; FIXED 1-byte body)* | 1 | service-NPC panel content push (no 4/100 ack) | — | pending — no dedicated 4/100; the server answers a service-NPC open by pushing panel content (shop/page channels, e.g. 3/8 money refresh). | `2-100_npc_open_req.yaml` |
| **2/142** CmsgStorageOp | 16 | inventory/slot + money pushes (no 4/142 ack) | — | pending — no 4/142 catalogued; storage state refreshes via 4/16/4/22 item-slot acks + money pushes. | `2-142_storage_op.yaml` |

### 2.6 Trade (player↔player)

| Request (C2S) | size | Response(s) (S2C) | size | Evidence / confidence | packets/ |
|---|---|---|---|---|---|
| **2/23** CmsgTradeRequest *(mode 2 = request, 0 = accept; FIXED 8-byte body)* | 8 | **4/23** SmsgUserTradeRequestResult *(phase byte drives the trade-request state machine)* | 20 | confirmed/high — `opcodes.md` routed; 4/23 installed. | `2-23_trade_request.yaml`, `4-23_trade_request_result.yaml` |
| **2/24** CmsgTradeSlotAdd | 20 | **4/24** SmsgUserTradeSlotUpdate (+ bulk **4/25** SmsgUserTradeFullResponse) | 44 / var | confirmed/high — `opcodes.md` routed; 4/24 + 4/25 installed. | `2-24_trade_slot_add.yaml`, `4-24_trade_slot_update.yaml`, `4-25_trade_full_response.yaml` |
| **2/25** CmsgTradeConfirm | var | **4/25** SmsgUserTradeFullResponse *(final bulk)* and/or **4/23** *(phase, on cancel)* | var / 20 | inferred/med — trade lifecycle; 4/23/4/24/4/25 all installed; exact commit-vs-cancel mapping pending. | `2-25_trade_confirm.yaml`, `4-25_trade_full_response.yaml` |

### 2.7 Stall / Personal-shop

| Request (C2S) | size | Response(s) (S2C) | size | Evidence / confidence | packets/ |
|---|---|---|---|---|---|
| **2/56** CmsgStallEnter | 4 | **4/74** SmsgStallListRefill *(stall window populate)* | var | inferred/med — census ties the stall lane to 4/74; installed. | *(opcodes.md 0x20038)*, `4-74_stall_list_refill.yaml` |
| **2/74** CmsgStallListRequest | 32 | **4/74** SmsgStallListRefill | var | inferred/med — same family as 2/56; whether server distinguishes enter vs list by a reply sub-field is pending. | *(opcodes.md 0x2004a)*, `4-74_stall_list_refill.yaml` |

### 2.8 Party

The three party ops share one 8-byte envelope `{u8 mode @0, u32 id @4}`; the minor selects the
category and the mode byte sub-selects. The party Push **5/21 SmsgPartyRosterEvent** carries the
broadcast roster change for all three.

| Request (C2S) | size | Response(s) (S2C) | size | Evidence / confidence | packets/ |
|---|---|---|---|---|---|
| **2/35** CmsgPartyInvite *(mode 2=invite / 0=accept)* | 8 | **4/35** SmsgPartyInviteState + **4/76** SmsgPartyAcceptResult + **5/21** SmsgPartyRosterEvent *(join)* | var / var / 12 | confirmed/high — `opcodes.md` "replies via 4/35/4/76"; 4/35, 4/76, 5/21 all installed. | `2-35_party_invite.yaml`, `5-21_party_event.yaml` |
| **2/36** CmsgPartyLeaveKick *(0=leave / 1=kick)* | 8 | **4/36** SmsgPartyMemberRemoveResult + **5/21** SmsgPartyRosterEvent *(leave/disband)* | 56 / 12 | confirmed/high — `opcodes.md` "pairs with 4/36"; 4/36 + 5/21 installed. | `2-36_party_leave_kick.yaml`, `4-36_party_member_remove_result.yaml`, `5-21_party_event.yaml` |
| **2/37** CmsgPartyLeaderOp | 8 | **4/37** SmsgPartyLeaderActionResult (+ **5/21** roster update) | var / 12 | confirmed/high — `opcodes.md` "pairs with 4/37"; 4/37 + 5/21 installed. | `2-37_party_leader_op.yaml`, `5-21_party_event.yaml` |

### 2.9 Guild

| Request (C2S) | size | Response(s) (S2C) | size | Evidence / confidence | packets/ |
|---|---|---|---|---|---|
| **2/30** CmsgGuildOp | 8 | **5/55** GuildNameDisplayUpdate / **5/65** GuildMemberRosterUpdate / **4/103** GuildPanelTextUpdate *(selector-dependent)* — **NO 4/30 ack** | var | pending — **REFUTED naive mirror:** 4/30 is installed but is `SmsgSocialPanelTarget`, not a guild-op ack. The guild updates ride 5/55/5/65/4/103 (all installed); which answers a given op is selector-dependent. | *(opcodes.md 0x2001e)* |
| **2/81** Cmsg_GuildDiplomacyDeclare *(state 0/1)* | 18 | **4/61** SmsgGuildStateChangeResult *(the diplomacy verdict)* | 52 | confirmed/high — **CORRECTED (binary wins, resolves the old §2.9 Open-Q):** the guild-diplomacy verdict lands on the major-4 guild state-change result at **response slot 61 (52-byte body: gate@+8, result@+9 ∈ 1..7, action@+10 ∈ 1..5, CP949 guild name @+11..+27, NUL@+28; action==5/result==2 → cooldown-in-DAYS = server-minutes/1440)**. The stale `4/81 = SmsgGuildDiplomacyResult` pairing is **DROPPED** — 4/81 is the generic `SmsgActionErrorResult` (status@+8, error@+9; 0xFF generic; 0x15 reads seconds@+10 mm:ss; 0x17 server-config percent), not a diplomacy reply. The `submitDiplomacy STATE[%d] target_guild_name_[%s]` string is **SEND-side only** — it stamps the 2/81 builder; it is NOT a receive handler. | `2-81_guild_diplomacy_declare.yaml`, `4-61_guild_state_change_result.yaml` |

### 2.10 Social / Friend

| Request (C2S) | size | Response(s) (S2C) | size | Evidence / confidence | packets/ |
|---|---|---|---|---|---|
| **2/49** CmsgFriendAddRemove *(0=add / 1=cut)* | 19 | **5/26** SmsgLocalPlayerRelationSlot *(friend-slot populate)* | var | inferred/med — 5/26 installed; the populate role is flagged UNVERIFIED in `handlers.md §5/26` (best static match, not capture-confirmed). | *(opcodes.md 0x20031)* |
| **2/54** CmsgFriendListRefresh | 1 | **5/26** SmsgLocalPlayerRelationSlot | var | inferred/med — same candidate as 2/49 (role UNVERIFIED); 180000 ms client throttle. | *(opcodes.md 0x20036)* |

### 2.11 Quest

Quest verbs are send-only (major 2 is C2S-only; no inbound case 2); the server answers asynchronously
on the quest Push channels.

| Request (C2S) | size | Response(s) (S2C) | size | Evidence / confidence | packets/ |
|---|---|---|---|---|---|
| **2/28** CmsgQuestAction *(sub 2=proceed / 3=accept / 4=give-up; FIXED 12-byte body)* | 12 | **5/68** SmsgQuestList *(log refresh)*; on turn-in **5/73** SmsgQuestComplete *(quest turn-in verdict)* | 452 / 344 | async-channel/high — 5/68 + 5/73 both installed (Push slots 68, 73). **5/73 = SmsgQuestComplete (binary wins; the stale IDB autoname SmsgGuildWarInfoUpdate is dropped).** | `2-28_quest_action.yaml`, `5-73_quest_complete.yaml` |
| **2/110** CmsgQuestNpcStep | 4 | **5/68** SmsgQuestList | 452 | async-channel/high — Tutorial/TutorTalk dialogue advance; 5/68 installed. | `2-110_quest_npc_step.yaml` |
| **2/152** CmsgQuestRowRequest *(per-row detail / claim; two u32)* | 8 | **5/68** SmsgQuestList (on claim/turn-in **5/73** SmsgQuestComplete) | 452 / 344 | async-channel/high — sole caller is the QuestPanel list rows; 5/68 + 5/73 installed. (The 2/152 = ProductPanel paging reading is withdrawn — that panel sends only 2/151.) | `2-152_quest_row_request.yaml` |

*Note.* `2/143 CmsgQuestItemKeep` (FIXED 4-byte body) folds its result into the quest channels; there
is **NO dedicated 4/143 quest-keep ack** — 4/143 is installed but is the shared `SmsgTrackedItemPanelPair`
(143/144 panel handler). Do not pair 2/143 → 4/143. *spec:* `2-143_quest_item_keep.yaml`.

### 2.12 Stat / Skill

| Request (C2S) | size | Response(s) (S2C) | size | Evidence / confidence | packets/ |
|---|---|---|---|---|---|
| **2/29** CmsgStatAllocate *(5×u32 absolute STR/INT/AGI/DEX/CON)* | 20 | **4/29** SmsgStatUpdate | 36 | confirmed/high — the handler gates on ResultOk @0x08, echoes the five stats, applies on success and plays the stat-confirm cue. Tight consumer match. | `2-29_stat_allocate.yaml`, `4-29_stat_update.yaml` |
| **2/41** CmsgSkillLearn *(NEW; not yet in opcodes.md — flagged for names lane)* | 12 | **4/150** SmsgSkillPointUpdate | var | inferred/med — SkillConfirmPanel learn (action 50); 4/150 mutates the local skill-point total (the skill-learn ack). Per-arg field meaning opaque. | `4-150_skill_point_update.yaml` |
| **2/145** CmsgSkillHotbarRegister *(NEW; not yet in opcodes.md)* | var *(`[u32 count][12B rec × count]`)* | **4/41** SmsgSkillHotbarAssignResult | var | inferred/med — SkillConfirmPanel commit (action 51); 4/41 mutates a hotbar slot (result @0x08, reason @0x09 → string ids 3020..3032). Distinguished from 4/150 by behaviour (4/41 = hotbar slot; 4/150 = skill-point total). | *(4/41 response slot)* |

*Note.* No stat-reset / skill-reset / respec C2S sender exists statically; any respec is
server/NPC-service driven (capture-pending). 2/41 and 2/145 are NEW finds — staged for `opcodes.md` +
`names.yaml` (do not apply here).

### 2.13 Billing / Market / Mail / Product

| Request (C2S) | size | Response(s) (S2C) | size | Evidence / confidence | packets/ |
|---|---|---|---|---|---|
| **2/118** CmsgTenderConfirm *(header-only)* | 0 | tender-listing push (no 4/118 ack) | — | pending — no dedicated 4/118; the server populates the tender listing on a separate channel. | `2-118_tender_confirm.yaml` |
| **2/122** CmsgGiftCharSend *(gift-character send w/ 2nd-password)* | 12 | **5/123** SmsgGiftCharReceiveConfirm | var | inferred/med — 5/123 installed (gift-char flow). | *(opcodes.md 0x2007a)* |
| **2/123** CmsgGiftCharReceiveConfirm | 12 | **5/123** SmsgGiftCharReceiveConfirm | var | inferred/med — 5/123 installed. | *(opcodes.md 0x2007b)* |
| **2/151** CmsgProductBuy *(1-byte selector: 0 = open/refresh, 200 = confirm)* | 1 | **selector 0 → 3/8** SmsgShopPageUpdate *(list/money refresh)*; **selector 200 → 4/113/4/114/4/115** *(product/cash-shop result + balance)* | 4 / 12·12·24 | confirmed/high (selector-0 → 3/8) + inferred/med (selector-200 → 4/113-family) — **CORRECTED (binary wins, resolves the old §Open-Q2):** the refuted **"1/20" reply tie is DROPPED** (1/20 = unrelated mail/letter). The two selector branches answer on different channels. | `2-151_product_buy.yaml`, `3-8_shop_page_update.yaml`, `4-113_item_shop_purchase_result.yaml`, `4-115_item_shop_balance_update.yaml` |
| **2/153** CmsgProductConfirm *(FIXED 4-byte; ONE builder; 60 s cooldown)* | 4 | **[capture/debugger-pending]** — reply opcode not stamped by the builder; best hypothesis = the product family **4/113 ItemShopPurchaseResult / 4/114 CashShopActionResult / 4/115 balance** | 12 / 12 / 24 | pending — **CORRECTED (binary wins):** `opcodes.md` now catalogs 2/153 as **CmsgProductConfirm** (the prior `CmsgPetSummon` reading is REFUTED). The builder stamps no reply opcode, so the answering minor stays **[capture/debugger-pending]** (register item R-38). | `2-153_product_confirm.yaml` |

*Note.* Server-pushed billing/mail notifications are S2C-only major-1 pushes with no matching request:
**1/16** SmsgSrvBillingDeactivated (0), **1/17** SmsgSrvBillingActivated (0), **1/19**
SmsgSrvBillingExpiryNotice (**22 bytes**, corrected from 21/20 — `opcodes.md`), **1/20**
SmsgSrvLetterReceived (var). **1/20 is mail only** — the prior "2/151 → 1/20" census tie has been
DROPPED (it is unrelated to the product flow; see §2.13 2/151). *spec:* `opcodes.md`,
`1-19_srv_billing_expiry_notice.yaml`, `1-20_srv_letter_received.yaml`.

### 2.14 PvP / Revenge

The PvP requests answer on the PvP Push family (no same-minor acks were isolated); the candidate
reply minors are static-inferred, not pinned.

| Request (C2S) | size | Response(s) (S2C) | size | Evidence / confidence | packets/ |
|---|---|---|---|---|---|
| **2/92** CmsgRevengeSummon *(revenge "summon target" confirm)* | 4 | PvP/revenge push (**5/89** PvpRevengeRoster / 5/92 PvpRequestOrNotice candidates) | var | pending — candidates static-inferred. | *(opcodes.md 0x2005c)* |
| **2/93** CmsgRevengeVote *(yes/no)* | 1 | vote-result push (**5/94** VoteResult `SmsgVoteResult` / 4/93 candidates) | var | pending — candidates static-inferred. | *(opcodes.md 0x2005d)* |

### 2.15 Keepalive / Link-health

| Request (C2S) | size | Response(s) (S2C) | size | Evidence / confidence | packets/ |
|---|---|---|---|---|---|
| **2/112** CmsgKeepaliveToggle *(on-demand)* | 1 | none | — | fire-and-forget — keepalive toggle; no reply. | `2-112_keepalive_toggle.yaml` |
| **2/10000** CmsgKeepalive *(timer-driven, 4-byte zero body, compress-only)* | 4 | none | — | fire-and-forget — timer keepalive frame; no reply. | `2-10000_keepalive.yaml` |

---

## 3. Fire-and-forget / reactive index

These C2S sends await **no reply**, or are themselves a reply (not a request):

| Opcode | Name | Category | Counterpart | Note |
|---|---|---|---|---|
| **1/0** | CmsgLogout | fire-and-forget | — | server drops the session; teardown drives GameState→6/8. |
| **2/0** | CmsgLeaveWorld | fire-and-forget* | teardown ack (minor not pinned) — pending | header-only leave-world notice. |
| **2/112** | CmsgKeepaliveToggle | fire-and-forget | — | on-demand keepalive toggle. |
| **2/10000** | CmsgKeepalive | fire-and-forget | — | timer keepalive frame. |
| **1/4** | CmsgAuthReply | **reactive** | answers **0/0** SmsgKeyExchange | built inside the inbound major-0 branch (the credential carrier). |
| **2/65** | *(reactive context ack)* | **reactive** | answers a game-tick / guild-member-remove handler | a 1-byte ack built inside an inbound handler; not a player command. |
| **2/146** | *(reactive packet ack)* | **reactive** | answers **5/146** `SmsgPacketResponseAckRequest` | the client's automatic ack to the server's ack-request, built inside the 5/146 handler. **8-byte body `[u32 echoed req_id][u32 local state global]`** (NOT the inbound token); field-1 identity `[capture/debugger-pending]` (R-30). *spec:* `network_dispatch.md §4.5a`. |

\* `2/8 CmsgHotbarSync` (241-byte snapshot upload) and `2/106 CmsgCreatureItemTick106` (effect-tick)
are likely fire-and-forget (no consumer-side wait found) but are marked **pending** — capture-required.

---

## Appendix A — Result / status code map

> **All human MEANINGS of these codes are `[capture/debugger-pending]`.** The tables give each code's
> **structural role** (what it does to control flow), not its message text. Source:
> `_dirty/netcode/result_code_maps.md` (consolidated from the wave-1 handler reads). **Phantom
> refuted:** there is **no 5000 / 10000 / 10001 "string-id class"** — those three integers are a
> display-duration, a keepalive minor + timer, and a timed-event tag respectively (family 2 below).

### A.1 Family — the 4/1 GameStateTick `{0 | 300 | 301}` end-gate

A 4-byte action/result code at the world-entry tick's trailing field gates the handler's final UI
branch. This is **4/1-local**, not a cross-handler family.

| code | structural role |
|---|---|
| 0 | default "normal apply" path. |
| 300 | "alternate UI" branch (panel-close + restricted-apply path). |
| 301 | the SAME alternate branch as 300 (300/301 share the target; which is success vs fail is `[capture-pending]`). |

### A.2 Family — 5000 / 10000 / 10001 (NOT a string-id class — phantom refuted)

| integer | role |
|---|---|
| **5000** (0x1388) | UI display duration / timer-delay in **milliseconds** (the show-for-5s timeout on result popups and status lines; also the short retry-timer delay). NOT a coded outcome. |
| **10000** (0x2710) | (a) the opcode MINOR of `2/10000 CmsgKeepalive`; (b) the 10 s timer-delay for the retry/timeout timer. |
| **10001** (0x2711) | the timed-event TAG (event id) for the connection/scene retry-or-timeout tick. |

The select-screen messages are driven by message-DB string-id GLOBALS (shown for 5000 ms), not by a
5000-based string-id family.

### A.3 Family — the major-3 char-mgmt result fail-code class

The `3/6` rename-result and `3/13` status-update handlers share one error-code→message switch (the
sub-code byte selects one of four message-DB string globals, shown on the select-screen status line
for 5000 ms):

| code(s) | structural role |
|---|---|
| 200, 201 | fail → shared message slot A. |
| 206 | fail → its OWN distinct slot. |
| 204, 205, 207, 208, 209, 210 | fail → ONE shared "generic char-mgmt rejection" slot (the contiguous block all collapse here). |
| 212 | fail → its OWN distinct slot. |
| (subtype) 1 | success — applies the new name/status (no error message). |

### A.4 Family — the 3/100 SmsgCharActionResult code set

`3/100` reads one 4-byte action/result code and runs a large switch with **two top-level modes**: a
**lobby/select-screen mode** (local player not yet present — the connect/char-action path that primes
the conn-state machine) and an **in-world mode** (local player present — small switch {1,2,3,4,5,7,22}
→ in-game tooltip popups). The full set is `{0, 1–5, 7, 9–11, 16, 22, 23, 200–211, 220–227,
202/203/232}`:

| code(s) | structural role (lobby mode) |
|---|---|
| 0 | "return to prior scene" → GameState 6 / sub-state 8. |
| 1, 2, 3, 4, 7 | advance scene → GameState 7 / sub-state 5. (in-world: each is a distinct tooltip popup.) |
| 5, 22 | (in-world: distinct tooltip popups; lobby 22 falls to the 7/5 advance.) |
| 9, 10, 11, 16 | PUBLISH outward via the select-screen handler's publish-code method (mid-range publish set). |
| 200, 201, 204–211, 220–227 | PUBLISH outward via the same publish method — the largest cluster, one contiguous fan to the publisher (no GameState change). |
| 202, 203, 232 | PUBLISH outward AND set the connect-progress state (GameState=2) — these are the codes the conn-state machine later CONSUMES/CLEARS from its pending slot. |
| 23 | select a status string and show it on the select-screen status line; explicitly EXCLUDED from the trailing retry-timer arm. |

*Cross-link:* the same numbers 201/202/203/232 also appear as conn-state-machine state codes — they
**share the numbers, not the code path**. 3/100 PRIMES/publishes; the conn-state machine RESOLVES. The
runtime "which code = connect / ok / lost / error" mapping is `[capture-pending]` in both.

---

## Appendix B — CP949 text-field rules

All game text is CP949 (EUC-KR). Two on-wire forms — *source:* `_dirty/netcode/cp949_text_fields.md`,
`packets/2-7_whisper.yaml`, `packets/2-83_chat_contextual.yaml`, `packets/3-21_chat_channel.yaml`,
`handlers.md §17`.

### B.1 The two forms

- **Form A — fixed NUL-terminated buffer.** A field of declared byte width `W`, zero-filled then
  capped-`strncpy` (≤ `W−1` usable + NUL). The NUL is **inside the width**. There is no
  inclusive/exclusive question for fixed buffers.
- **Form B — length-prefixed body.** A trailing `[u32 len][text]` block (the prefix is **always u32** —
  the shared appender writes a 4-byte size word then the bytes). The decisive question is whether `len`
  counts the terminating NUL.

### B.2 The length-prefixed NUL rule (the load-bearing answer)

| Opcode | Direction | NUL rule | Confidence |
|---|---|---|---|
| **2/7** CmsgChat | C2S | **EXCLUDES NUL** (`len = strlen`; text capped at 119) | HIGH (decompiler-direct) — **2/7 is the OUTLIER**, the only length-prefixed body that excludes the NUL. |
| **2/83** CmsgChatContextual | C2S | **INCLUDES NUL** (`len = strlen + 1`; gate `0 < len < 200`) | HIGH (decompiler-direct) |
| **3/21** CmsgChatChannel | C2S | **INCLUDES NUL** (`len = strlen + 1`; ordinary channels reject empty/≥200) | HIGH (decompiler-direct) |
| 5/7 SmsgChatBroadcast | S2C | PENDING (body framing itself unconfirmed) | low |
| 3/21 *(S2C echo)* | S2C | PENDING (length-prefixed confirmed; incl/excl not read) | low |
| 3/50000 SmsgGmChatMessage | S2C | PENDING (NUL rule not read) | low |

Engineers MUST NOT assume a single chat NUL convention — it is a real per-opcode difference.

### B.3 Fixed-buffer width quick-reference

| width | who uses it |
|---|---|
| 16 | 2/49 friend name. |
| **17** | **the standard short-name cell** — C2S: 1/13 rename, 2/70 recipient, 2/81 guild, 2/7 whisper TargetName; S2C: spawn-descriptor name, 4/4 tag-9 title, 4/65 member names, 5/68/5/73/5/77 quest/rank names, 5/89/5/91/5/92 PvP names, ~17 for 4/61 guild name and 4/65 guild title. |
| 18 | 1/6 create-char name; 5/38 party-member name; 4/48 rank-event name (inside a 28-byte record). |
| 20 | 5/7 & 3/21-echo SenderName. |
| 33 / 38 | 4/135 roster strings A / B (delimiter-encoded CP949, not plain asciiz). |
| 49 | 4/103 guild-panel text lines (×4). |
| ~84 | 2/70 carrier-pigeon message body (boundary inferred). |
| u32-prefixed | 2/7 (excl-NUL), 2/83 (incl-NUL), 3/21 (incl-NUL) C2S chat bodies. |

---

## Open Questions / UNVERIFIED

CYCLE 4 settled the two pairing-conflict Open-Qs (guild-diplomacy 4/81→4/61; 2/151 reply / "1/20" tie);
they are **RESOLVED** above and no longer carried here. The 5/73 catalog name is reconciled
(`opcodes.md` now carries SmsgQuestComplete) and the 2/153 name is reconciled (CmsgProductConfirm). What
remains open is genuinely-unsettled **VALUE semantics**, which by doctrine is `[capture/debugger-pending]`
and is consolidated in **Appendix C — capture/debugger-pending register (R-01..R-42)**. The two
structural items still worth flagging here:

1. **1/7 result-minor + the mode semantic.** `opcodes.md` now reads 1/7 as **CmsgSelectCharacterSlot**
   `[u8 slot][u8 mode]` (mode 1 = select-and-play, mode 0 = slot-lock) — the prior "delete-mode" reading
   is **REFUTED** (there is no major-1 char-delete opcode on this build; deletes ride the major-3 result
   ladder). The exact result minor among the char-mgmt major-3 ladder (3/4 subtype-refresh vs 3/7
   SmsgCharManageResult) is not capture-pinned; both are installed and clear the in-flight latch. The
   mode-byte polarity (R-03 result/subtype polarity for 3/7) is `[capture/debugger-pending]`.
2. **Staged-name collisions (flagged for the names lane — do NOT apply here).** `2/41 = CmsgSkillLearn`
   and `2/145 = CmsgSkillHotbarRegister` (NEW C2S opcodes, not yet in `opcodes.md`); several §8-swept
   `Cmsg*` proposals collide by role and must be disambiguated before any IDB rename. Owned by
   `ida-toolsmith` + `names.yaml`; this file only records them. (`5/73 = SmsgQuestComplete` and
   `2/153 = CmsgProductConfirm` are already applied in `opcodes.md` and are no longer name-collisions.)

---

## Appendix C — capture/debugger-pending register (R-01..R-42)

> **The honest residual.** Every item below is a wire-byte **VALUE-semantics** question (what a value
> *means* / which code is success vs fail / unit / conditional presence / opaque interior / pairing not
> statically pinned), NOT a structure gap — sizes, offsets and field tables are recovered. This register
> is the future-debugger (`?ext=dbg`) worklist; consolidated (never copied) from
> `_dirty/netcode_deep/oq/completeness.md §3`. Grouped by kind.

### C.1 Enumerations / code-meanings (which numeric value means what)

| R | Opcode(s) | Pending VALUE |
|---|---|---|
| R-01 | 0/0 | meaning of the two trailing u32 server scalars A/B (handshake) |
| R-02 | 3/100 | full action-result-code → user-meaning map (`{0,1-5,7,9-11,16,22,23,200-211,220-227,232}`) |
| R-03 | 3/7 | Result(0/1) and Subtype(0=refresh/1=rename-applied/2=delete) polarity confirmation |
| R-04 | 3/6 | ErrorCode bucket strings (`{0xC8/0xC9}/{0xCC..0xD2}/{0xCE}/{0xD4}`) |
| R-05 | 3/5 | BillingFlag enum @+0x1C |
| R-06 | 4/61 | guild action(1..5)×result(1..7) 2-D verdict map; cooldown-days arm |
| R-07 | 4/81 | error-code byte map (`0xFF`/`1..0x18`/`0x15` mm:ss/`0x17` percent) |
| R-08 | 4/15 | outcome selector `{100=shared,101=plain,else}` and failure 1..5→ids |
| R-09 | 4/82 vs 4/108 | confirm billing-points vs gold field identity at runtime |
| R-10 | 2/142 | Op enum (deposit/withdraw/move) = widget-action-id − 7 |
| R-11 | 4/100 | combat phase(3/5)/sub-kind(`0xFF` reset)/value byte meanings |
| R-12 | 5/79, 5/80 | DeathOp/mode byte sets; effect-id selectors |
| R-13 | 5/5 | actor-record visual-state codes (1001/1011/1020/1021/1023/1041) — server-driven vs purely local |

### C.2 Flag-bit meanings

| R | Opcode(s) | Pending VALUE |
|---|---|---|
| R-14 | 2/112 / keepalive-enabled flag | the toggle byte arg cadence / who flips it |
| R-15 | 1/16/1/17 | (none on wire — local billing latch; confirm the displayed text only) |
| R-16 | 5/124, 5/1 trailer, 5/53 StateByteA/B | actor visual-flag / title / relation bit meanings |
| R-17 | 4/12/4/16 slot-type==15 | confirm the title/weapon-drawn bit set beyond 15 |
| R-18 | 5/127 | StealthFlag bit semantics |
| R-19 | 2/23/2/35/2/36/2/37 mode bytes | invite/accept/decline/leave/kick mode-value meanings |

### C.3 Unit / scale of a value

| R | Opcode(s) | Pending VALUE |
|---|---|---|
| R-20 | 5/52 rec +0x14/+0x18 | whether the 64-bit accumulator is literal damage vs another signed magnitude |
| R-21 | 5/53, 5/32 | HP i64 vs MP/stamina split unit confirmation (structure agreed i64; semantics pending) |
| R-22 | 4/108 / 4/115 | gold qword vs the three balance dwords (displayed total vs stored component) |
| R-23 | 5/9, 5/11 | xp/rank-xp i64 sign + the server-config bonus/penalty split rates |
| R-24 | 3/7 ReadyTime, 4/61/4/63 cooldowns | epoch vs delta; minutes-vs-days divisor confirmation |
| R-25 | 5/18 | clock field0/field1 (seconds vs packed date/time) |

### C.4 Conditional field presence / which-builder discriminator

| R | Opcode(s) | Pending VALUE |
|---|---|---|
| R-26 | 1/4 | PIN field present only when 2nd-password cap configured (confirm live) |
| R-27 | 2/52 short vs full | whether server keys on count words / struct+4 float / struct+1 to branch |
| R-28 | 2/7 vs 2/83 vs 3/21 | which UI action drives each chat path + exact channel/target prefix layout |
| R-29 | 2/151 selector | confirm sel 0 (open→3/8) vs 200 (confirm→4/113-family) at runtime |
| R-30 | 5/146 / 2/146 field-1 | confirm the echoed field-1 is the local context/state global, not the inbound token |

### C.5 Opaque-forward interiors (size known; interior field split needs capture/struct)

| R | Opcode(s) | Pending interior |
|---|---|---|
| R-31 | 4/1 | the ~9100-byte GameStateTick interior layout (deliberately not decomposed) |
| R-32 | 4/56 / 4/71 | the deep mirror-relative sub-tables past the decoded front sections |
| R-33 | 5/89/90/91/92 | PvP-state object blocks (roster/counters/score/request) — struct-lane |
| R-34 | 5/59 / 5/77 | rank-progress 76B / 400B blobs (only one selector byte / +4 id decoded) |
| R-35 | 2/40/2/50/2/55/2/141 | the panel-context HUD record blobs (72/16/32/76 bytes) |
| R-36 | 2/8 | 241-byte hotbar snapshot per-slot layout |
| R-37 | 2/75 (80B) / 2/80 (48B) | embedded fixed records → re-struct-analyst |

### C.6 Pairing not statically pinned (which reply minor answers which request)

| R | Opcode(s) | Pending pairing |
|---|---|---|
| R-38 | 2/153 CmsgProductConfirm | reply opcode (4/113/4/114/4/115 best hypothesis; the builder stamps none) |
| R-39 | the ~16 major-2 "Request→pending" submits | exact answering minor per panel/push channel |
| R-40 | 2/92/2/93 | PvP/revenge reply minors (5/89/5/92/5/94 static-inferred) |
| R-41 | 1/2 CmsgLobbyPing | the server pong/ack minor (lobby socket — see §Open Conflicts C1) |
| R-42 | 5/55/5/65/5/123 | confirm the selector-dependent async-answer relationship to 2/30 / gift flow |

**Register size: 42 distinct capture/debugger-pending VALUE-semantic items** (R-01..R-42), spanning all
five majors. This is the single honest residual after CYCLE 4 — every item is a *value* question, not a
*structure* gap. (Plus the standing **CP949 message-DB string-text** items — message ids such as
10006/51027/36004-36028/45xxx/54xxx/65xxx/58xxx/74xxx — which are a **string-table/asset task**, not a
netcode debugger task.) *source:* `_dirty/netcode_deep/oq/completeness.md §3`.

---

## Open Conflicts (carried forward to a future lane)

- **C1 — `1/2 CmsgLobbyPing` routing.** On this build the lobby ping rides the **shared NetClient send
  convergence** (the same send path as the in-scope char-mgmt builders), in tension with the "separate
  port-10000 lobby socket" doctrine that put the lobby out of scope. 1/2 is kept out of the char-mgmt
  family and sets no in-flight latch. This is an **open arbitration item for a future lobby-socket
  lane** — surfaced to the maintainer (it may mean the lobby ping genuinely shares the game send
  convergence on this build). It is **NOT a netcode-DTO blocker**. *source:*
  `_dirty/netcode_deep/_TIER1_LEDGER.md` (Open Conflicts C1).

---

## Provenance

This master was authored **LAST** in the CYCLE 4 Netcode Deep-Cartography promotion wave — after the
seven neighbour files had already been rewritten — and every pairing was re-derived against those
now-updated neighbours and cited inline.

- **Pairings** synthesized (never copied) from the CYCLE 4 `_dirty/netcode_deep/` corpus (the per-wave
  req↔resp lanes + the install-table partition), validated against the install tables, with the
  install-table refutations respected: **no 4/44/4/46 item-move ack** (4/44/4/46 = actor tick-table
  ops), **no 4/30 guild-op ack** (4/30 = SmsgSocialPanelTarget), **no 4/143 quest-keep ack** (4/143/4/144
  = one shared tracked-item-panel handler; the real quest replies are the 5/68/5/73 pushes).
- **Binary-won pairing corrections** applied this pass (the binary wins): `2/81 → 4/61
  SmsgGuildStateChangeResult` (the stale `4/81 = SmsgGuildDiplomacyResult` DROPPED; 4/81 = generic
  SmsgActionErrorResult); `2/151` selector 0 → 3/8, selector 200 → 4/113/4/114/4/115 (the "1/20" tie
  DROPPED); `2/146` reply = `[u32 echoed req_id][u32 local state global]` (8B); `2/153 =
  CmsgProductConfirm` reply opcode `[capture/debugger-pending]` (R-38); `5/73 = SmsgQuestComplete` (vs
  the stale SmsgGuildWarInfoUpdate). *source:* `_dirty/netcode_deep/_TIER1_LEDGER.md` (Major-2 / Major-4
  worklists + the in-flight latch census + Open Conflicts).
- **Result/status codes** (Appendix A) from the CYCLE 4 result-code maps (phantom 5000/10000/10001
  string-id class refuted; real families = the 300/301 4/1 end-gate and the 3/100 code set).
- **CP949 rules** (Appendix B): 2/7 excludes NUL; 2/83 & 3/21 include; u32 length; char[17] dominant cell.
- **Opcode names/sizes/routing** from committed `opcodes.md`; **per-handler reads** (incl. the Group-I
  in-flight latch census, 4/61, 4/81, 5/146) from `handlers.md`; the **in-flight latch + dispatch shape +
  the 5/146→2/146 ack** from `network_dispatch.md`; field specs under `packets/` (incl.
  `packets/2-81_guild_diplomacy_declare.yaml`, `packets/4-61_guild_state_change_result.yaml`,
  `packets/2-151_product_buy.yaml`, `packets/3-8_shop_page_update.yaml`,
  `packets/2-153_product_confirm.yaml`); struct layouts under `structs/` (incl.
  `structs/spawn_descriptor.md`, `structs/net_client.md`, `structs/net_handler.md`).
- **Capture/debugger register** (Appendix C, R-01..R-42) consolidated from
  `_dirty/netcode_deep/oq/completeness.md §3`.
- All facts STATIC on anchor **263bd994**. No debugger, no capture this campaign — every wire VALUE
  semantic and every result-code polarity is `[capture/debugger-pending]`.
