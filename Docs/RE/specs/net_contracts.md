# Net Contracts — Client↔Server Request/Response master pairing (authoritative)

<!--
verification: routing/sizes [confirmed] (opcode->handler ROUTING, the C2S send sizes, the S2C
  handler read sizes/offsets, and the install-table slot occupancy are all control-flow-confirmed);
  pairing [confirmed] where a request maps to a named/installed reply slot, otherwise
  static-hypothesis (a best-consumer / in-flight-latch inference, not a captured round-trip);
  value-semantics [capture/debugger-pending] (every wire VALUE semantic — what a reply byte MEANS,
  which result code is success vs fail — has no live capture this campaign).
ida_reverified: 2026-06-22 (re-verified against doida.exe IDB SHA 263bd994, CYCLE 12 Phase 0; prior CYCLE 8 2026-06-21). CYCLE 12 Phase 0: §A.4 3/100 SmsgCharActionResult code set reconciled -> canonical set {0,1-5,7,10,11,16,22,23,200-211,220-227,202/203/232} (no case 9, no case 32); select-mode codes 22/23 RECOVERABLE (state 7/sub 5), not fatal; cross-ref handlers.md §23.1 as canonical 3/100 table. CYCLE 8 re-confirmed: NO 12-byte create-result opcode exists (1/6 create acked by 3/7 + 3/1 + 3/4, 3/23 = 28B status-bytes-by-name) — the C# port must retire any SmsgCharCreateResult; and the lobby/server-list connect (port 10000) uses inet_addr on a dotted-quad while the game socket uses gethostbyname (DNS).
ida_anchor: f61f66a9ae0ec1e946105b2ecff76e8930cb1d1367df64e5688a5266f5ad9963
ida_reverified: 2026-06-27   # CYCLE 14 re-anchor (f61f66a9): confirmatory - subsystem cleanly relocated, 1 re-confirmed SAME, 0 corrected
evidence: [static-ida]
capture_verified: false
cycle7: re-verified against doida.exe IDB SHA 263bd994, CYCLE 7 (2026-06-20). Gameplay/dark-subsystem
  reconciliation folded in (the binary wins): combat melee/use-skill = C2S 2/52 (no "5/52" combat
  opcode — that bank is the Cube-Gamble result panel 4/99+4/100, NOT combat); buff slot update = S2C
  5/31 SmsgBuffSlotUpdate 56B (supersedes the older "4/102" label); 5/73 = SmsgQuestComplete (the
  "GuildWar" label is REFUTED); 3/23 = SmsgCharStatusBytesByName 28B (NOT a 12B create-result; create
  is acked via the in-flight latch cleared by 3/7 + a refreshed char list); 2/60 = couple/marriage
  relation request (reply 5/53), NOT mail; crafting commit = 2/151 select -> 2/153 COMMIT -> 4/79
  SmsgCraftingResult; death push 5/10, respawn 2/3 -> 4/28 + 5/28, ground pickup 2/15 -> 4/15. New
  contract rows surfaced: mail/delivery (2/71 -> 4/70, 2/70), 2/106 creature-item tick, character
  mgmt (1/6, 3/7, 3/23, 5/32), skills (2/145, 2/41), relations/faction (2/62, 2/66, 5/64, 4/126),
  2/118 tender confirm. NO combat-pet/summon subsystem exists in this build (the dev class named
  "PetPanel" is the COUPLE/PARTNER window, driven by 5/53).
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

> **Nagle coalescing (carried for implementers).** Neither end disables Nagle's algorithm. **The client
> does NOT set `TCP_NODELAY` on the game connection — Nagle stays ON by default** (re-confirmed static
> IDA, 2026-06-21). An exhaustive census of every socket-option call in the client finds **exactly one**
> `setsockopt` site anywhere, and it sets **`SO_RCVBUF`** (receive-buffer size) on the **game socket**;
> there is **no** `setsockopt` with the TCP-level `TCP_NODELAY` option on any socket (not the game path,
> not the lobby path). The server likewise leaves `TCP_NODELAY` unset (a maintainer-observed runtime fact,
> most pronounced for the **World Server** major-4/major-5 traffic). This **confirms** the live replica
> fact that the working game connection runs with Nagle ON (`Socket.NoDelay = false`). So **multiple
> framed messages may arrive coalesced in a single TCP segment, and one message may be split across
> segments** — in both directions. Always reassemble on the 8-byte header `size` word; never treat one
> `recv` (or one `send`) as one message.
>
> **Socket blocking mode (re-confirmed static IDA, 2026-06-21).** The **game socket** is switched to
> **non-blocking only during connect** (the connect is timed with a `select`, roughly a 2-second timeout,
> plus a socket-error check), then **reverted to blocking** for steady-state operation. Steady-state
> receive is **overlapped**: a dedicated I/O thread drives overlapped receives (with overlapped sends and
> completion waits). The **lobby socket** (the server-list / channel-endpoint query on port 10000) is
> plain blocking throughout with **no** socket options set. Full discussion: `network_dispatch.md §4.4a`.

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

### 1.3 No request-id anywhere — the single in-flight latch is the only pending primitive

**There is NO per-request request-id, sequence number, or transaction key anywhere in the playable
slice.** A reply is never matched to its request by a correlation token on the wire — routing is purely
by the `(major,minor)` dispatch tables (§1.1). The only generic pending primitive in the protocol is a
**single global boolean in-flight latch** scoped to the char-management handshake. It does **not** route
or match responses; it is a one-deep "a char-management request is outstanding" flag.

- **It is one-deep.** Char-management is one-request-at-a-time, so a single boolean suffices — there is
  no need for, and no evidence of, a per-request map.
- **Its SOLE reader is the keepalive timer.** The latch's only consumer suppresses the 20-second idle
  heartbeat while a char-management request is outstanding (so the keepalive does not fire on top of an
  in-flight request). Nothing else reads it; it never selects a handler.
- **Implementer guidance.** A C# `RequestTracker` MUST model this as **a single boolean pending flag
  scoped to the char-management handshake, NOT a per-request id→request map.** Building a correlation map
  would be inventing protocol structure the binary does not have.

A single **outstanding-request guard** ("in-flight latch") byte on the net-client singleton is the
request↔response coupling for the lifecycle/char-mgmt family — it is how a result handler knows a
char-management request is in flight while it completes. The full SET/CLEAR roster (CYCLE 4
netcode-deep, control-flow-confirmed):

- **SET** by the send builders **`1/6`, `1/7`, `1/9`, `1/13`, `1/14`, `2/2`** (CmsgPortalTravel), and
  by the **`(0,0)→1/4`** inbound secure-auth branch (the reactive credential reply sets it on send).
- **CLEARED** by the result handlers **`3/1`, `3/4`, `3/6`, `3/7`, `3/13`, `3/14`, `4/1`**.
- **READ** only by the keepalive timer (it consults the latch to decide whether to suppress the
  20-second idle heartbeat while a request is pending) — it does NOT route or match any response.
- **`1/0 CmsgLogout` sets NO latch** (fire-and-forget — the one lifecycle builder that does not arm it).

**The enter ladder (CORRECTED 2026-06-21 against the live server `211.196.150.4`).** Enter-world is
the chain **`1/9 CmsgEnterGameRequest → 4/1 SmsgGameStateTick`** — the **`4/1` IS the enter
confirmation** (the world-state snapshot), with **NO enter-ladder `3/5` in between**. A live headless
run observed, after the roster: `3/4 (roster) → 3/5 (44B, the post-login account-ack, BEFORE any 1/9)`,
then `1/9 (enter request) → 4/1 (9100B, the enter confirmation) → 5/121`. The `1/9` send SETS the
enter-game latch; **`4/1` (not `3/5`) CLEARS it** — clearing the latch is `4/1`'s very first statement,
unconditional. A **latch-armed `4/1` IS the enter confirmation** and drives the scene into the in-world
state (Select/Load → InGame). The **`3/5 SmsgEnterGameAck` is ONLY the unsolicited post-login
account-ack** the replica pushes right after the `3/4`/`3/1` roster (it arrives **before** any `1/9`,
so the latch is NOT armed at that `3/5`); it nudges the scene toward Load but **is NOT a step in the
enter ladder** and must NOT, by itself, enter the world. (The earlier reading of a three-step
`1/9 → 3/5 → 4/1` ladder where the *enter-ladder* `3/5` armed the world-load is **REFUTED for this
server**: that `3/5` never arrives between the `1/9` and the `4/1`.) *spec:* `handlers.md`
(Group I — in-flight latch census; §4/1 latch note), `opcodes.md` (1/0, 1/9, 2/2 notes),
`network_dispatch.md §4` (outstanding-request guard + keepalive read), `login_flow.md §1 step 9 / §3.4`.

### 1.4 Master coverage statement

The in-scope netcode is **100% DTO-covered at the STRUCTURE level** — every opcode carries a complete
DTO (size / offset / field-table / contract-class / canonical-name / pairing-status) across the
handshake (0/0) and the five majors: 0/0 ✓ · major-1 (4 S2C billing/letter + 7 C2S lifecycle + the
0/0-paired 1/4) ✓ · major-2 (95 distinct C2S opcodes / 96 builders, set-equal to the send census) ✓ ·
major-3 (11) ✓ · major-4 (100 installed slots + 2 specials {4/500, 4/50000}, 99 distinct handlers —
143/144 share) ✓ · major-5 (65 installed Push slots) ✓. **There is no slot/builder lacking a complete
structural DTO.** The single uniform residual is wire-byte **VALUE semantics** — the 45-item
`[capture/debugger-pending]` register in **Appendix C** — which by doctrine is NOT a DTO gap (sizes and
field layouts are recovered; only the *meaning* of certain bytes is unsettled). *source:* the CYCLE 4
netcode-deep completeness roll-up (dirty-room).

### 1.5 Contract-class tally (whole-netcode, rolled up)

The per-domain tables tag every opcode with one contract shape (§1.2). Rolled up across all five majors
(from the CYCLE 4 netcode-deep completeness roll-up; counts are **±2 approximate** because the same
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
| **1/6** CmsgCreateCharacter *(52-byte appearance blob)* | 52 | create-ack = the in-flight latch cleared by **3/7** SmsgCharManageResult (8B) + a refreshed char list (**3/4** SmsgSceneEntityUpdate) (+ **3/23** SmsgCharStatusBytesByName roster patch) | 8 / var / 28 | inferred/med — sets the latch; **CORRECTED (binary wins):** there is **no 12-byte create-result opcode** — the create round-trip is acked by the latch-clearing **3/7 SmsgCharManageResult (8B)** plus a refreshed char list; **3/23 = SmsgCharStatusBytesByName (28B)** (17B name key + status bytes; patches the roster BY NAME — NOT a create-result, prior "SmsgCharSelectStatusUpdate" label dropped). | `cmsg_char_create.yaml`, `3-7_char_manage_result.yaml`, `3-4_scene_entity_update.yaml` |
| **1/7** CmsgSelectCharacterSlot *(2-byte `[u8 slot][u8 mode]`; both sites are SELECT paths)* | 2 | result on the major-3 ladder (no dedicated per-opcode reply); removal via **3/7** SmsgCharManageResult subtype 2 | 8 | **CORRECTED — binary wins (Phase 2b, build 263bd994):** 1/7 is a character-**SELECT** commit, NOT a delete carrier. A single send-builder has exactly two call sites in the select-window command handler: the "play / select this slot" confirm writes **mode = 1** (select-and-play); the slot-lock / pre-play confirm writes **mode = 0** (slot-lock). The prior "CmsgManageCharacter / DELETE overloads mode=1" reading is **REFUTED** — there is **no major-1 char-delete opcode** on this build (the major-1 C2S builder family is exactly 1/0, 1/2, 1/6, 1/7, 1/9, 1/13, 1/14). Character removal is surfaced only via the inbound major-3 result ladder (**3/7** SmsgCharManageResult subtype 2). Builder identity, the two call sites, and the per-site mode literals are static-HIGH; the runtime *meaning* of mode 1 vs mode 0 (and which major-3 reply each elicits) remains capture-pending. The earlier 3/4-vs-3/7 CONFLICT is RESOLVED (binary won). | `cmsg_char_select.yaml`, `3-7_char_manage_result.yaml` |
| **1/9** CmsgEnterGameRequest | 40 | **4/1** SmsgGameStateTick *(world snapshot, form B — the enter confirmation)* | var | **CORRECTED 2026-06-21 (live `211.196.150.4`):** the live enter ladder is **`1/9 → 4/1`** — the **`4/1` IS the enter confirmation**, with **NO enter-ladder `3/5` in between** (the only `3/5` is the unsolicited post-login account-ack, which arrives BEFORE the `1/9`). 1/9 SETS the enter-game latch; **`4/1` (not 3/5) CLEARS it** as its first statement, and a **latch-armed `4/1` drives Select/Load → InGame**. 4/1 = installed Response slot 1, the form-B world-entry packet. (The prior `1/9 → 3/5 → 4/1` reading is REFUTED for this server — see §1.3 "The enter ladder".) | `cmsg_char_enter.yaml`, `4-1_game_state_tick.yaml`, `3-5_enter_game_response.yaml` |
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

**Combat is server-authoritative — the real path is `2/52 → server resolves → server pushes results`.**
The client only **gathers target ids** (it walks the local party/friendly-fire guard array) and emits
**C2S 2/52**; the server resolves damage and pushes the outcome inbound. There is **no client-side
damage formula** (`base−defense`/crit) on the apply path — the applied magnitude is runtime-only.
Move emitter = **C2S 2/13 (16-byte body)**, event-driven (arrival-stop + long-frame resync + gated
heartbeat, **not** a fixed interval); server position push = **S2C 5/13 (40-byte body)**, reconciled by
squared-distance bands (interp / catch-up / hard-teleport). NPC interact = **C2S 2/16** (§2.4).
The 20-slot party / friendly-fire id array the skill executor walks is keyed off the **local-player
record +204** (20 slots × 16-byte stride) — the per-member stat fields ride 5/38 (§2.8).

**There is NO "5/52" combat opcode, and 4/99 + 4/100 are NOT combat (binary wins, CYCLE 7).** The brief's
"5/52 melee" was a transposition for the **2/52** use-skill builder. Separately, the **4/99 + 4/100**
slot bank is the **Cube-Gamble minigame result panel** (a wager/reel daily-bet minigame submitted via
**C2S 2/141**, panel `data/ui/cubegamble.dds`), **not** combat — any "SmsgCombat*" label on 4/99/4/100,
and the "phase byte 3/5 / 0xFF reset" reading, describe the **gamble reel**, not a combat result.
*spec:* `combat.md`, `handlers.md`, `opcodes.md`.

**In-game pairs are fire-and-forget broadcasts — no latch, no request-id (the actor-id is the key).**
The three in-game request→broadcast pairs — **2/13 → 5/13** (movement), **2/52 → 5/52** (skill action),
**2/7 → 5/7** (chat broadcast, dispatched further by channel code) — set **no in-flight latch** and carry
**no correlation token**. The server simply re-broadcasts the world event, and the client applies it by
**resolving the actor id** carried in the Push (it locates the existing actor object and mutates it).
This is the in-session counterpart to the char-management latch (§1.3): the latch governs the
lobby/char-mgmt handshake only; in-session play is pure broadcast-and-resolve. The **actor-id field
VALUE semantics** in 5/13 / 5/52 / 5/7 (exactly which on-wire field carries the actor id and how it
keys the local actor table) are **live-pending (6-D)**. *spec:* `handlers.md`, `opcodes.md`.

### 2.4 Inventory / Item

| Request (C2S) | size | Response(s) (S2C) | size | Evidence / confidence | packets/ |
|---|---|---|---|---|---|
| **2/14** CmsgDropItem | 8 | **4/14** SmsgGroundItemSlotAck | 20 | inferred/med — mirror-minor + `opcodes.md` "server answers with the 4/14 drop ack"; 4/14 installed. | `2-14_drop_item.yaml`, `4-14_ground_item_drop_ack.yaml` |
| **2/15** CmsgPickupItem | 12 | **4/15** SmsgItemWorldPickupAck | 36 | inferred/med — mirror-minor + `opcodes.md` "4/15 pickup ack"; 4/15 installed. | `2-15_pickup_item.yaml`, `4-15_item_world_pickup_ack.yaml` |
| **2/44** CmsgItemQuickMove | 12 | inventory/slot push — **NOT 4/44** | — | pending — **REFUTED naive mirror:** 4/44 is installed but is `SmsgActorTickTableOpA`. The quick-move result rides the inventory-slot acks (4/16 EquipChangeResult / 4/22 ItemSlotStateAck / 4/149 ItemPanelSlotChunk / 4/153 ItemPanelSlotRefresh — all installed). | `2-44_item_quick_move.yaml` |
| **2/46** CmsgItemMove | 12 | inventory/slot push — **NOT 4/46** | — | pending — same as 2/44: 4/46 is installed but is `SmsgActorTickTableOpC`. The drag-move result rides the inventory-slot acks. | `2-46_item_move.yaml` |
| **2/16** CmsgActorInteract *(**NOT** an equip request — see note)* | var | actor-interaction push (**not an equip ack**) | — | pending — **CORRECTED:** 2/16 is `CmsgActorInteract`, not the equip request. The equip/item results below are reached by opcode, not as same-opcode replies to 2/16. | *(opcodes.md, 2/16)* |

*Notes (load-bearing).*

**2/16 is CmsgActorInteract — NOT an equip request (prior "2/16 = equip family" framing CORRECTED).**
2/16 is an actor-interaction send, not an item/equip request. The equip/item results — **4/12
SmsgEquipItemResult**, **4/16 SmsgEquipChangeResult**, **4/22 SmsgItemSlotStateAck** — are
**result-code-keyed responses reached BY OPCODE**: the response opcode *is* the category, and a leading
result byte in the body selects success vs failure. They are **NOT same-opcode replies to 2/16**, and
they are not paired off 2/16 at all. The **exact eliciting C2S send** for each (an item-panel /
quick-equip / item-move send) is **live-pending (6-D)**, as are the **result-byte values beyond {0,1}**
in those equip responses.

The canonical 12-byte item-model is shared by 2/44/2/46 (and partly 2/15), but
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

### 2.10 Social / Friend / Relations / Faction

| Request (C2S) | size | Response(s) (S2C) | size | Evidence / confidence | packets/ |
|---|---|---|---|---|---|
| **2/49** CmsgFriendAddRemove *(0=add / 1=cut)* | 19 | **5/26** SmsgLocalPlayerRelationSlot *(friend-slot populate)* | var | inferred/med — 5/26 installed; the populate role is flagged UNVERIFIED in `handlers.md §5/26` (best static match, not capture-confirmed). | *(opcodes.md 0x20031)* |
| **2/54** CmsgFriendListRefresh | 1 | **5/26** SmsgLocalPlayerRelationSlot | var | inferred/med — same candidate as 2/49 (role UNVERIFIED); 180000 ms client throttle. | *(opcodes.md 0x20036)* |
| **2/60** *(couple / marriage relation request — `{mode/action byte 0..4, partner actor id}`)* | 8 | **5/53** SmsgActorVitalsAndPairState *(partner vitals + pair-state)* | var | inferred/med — **CORRECTED (binary wins, CYCLE 7):** 2/60 is the **couple/marriage relation command** built from the couple UI (the dev `Cmsg_LetterRequest_Send` autoname is misleading — it is **NOT mail**). Its reply is **5/53**, which drives the couple/partner window. | *(opcodes.md 2/60)* |
| **2/62** Cmsg_RelationNamedRequest *(19-byte body — relation op keyed by a target name)* | 19 | relation push (**5/64** SmsgRemoteActorRelationPair candidate) | 16 | inferred/med — NEW; named relation request; the remote-actor pair byte is set by 5/64 (16B). | *(opcodes.md 2/62)* |
| **2/66** Cmsg_RelationToggle *(1-byte toggle)* | 1 | relation/pair push (**5/64** SmsgRemoteActorRelationPair candidate) | 16 | inferred/med — NEW; 1-byte relation toggle. | *(opcodes.md 2/66)* |

*Relations / faction notes (CYCLE 7).* **5/64 SmsgRemoteActorRelationPair (16-byte body)** is the
server Push that sets the relation pair-state byte on two remote actors (it is reactive to the relation
sends above, not a per-request ack — pairing is selector/context-dependent). **4/126
SmsgFactionSideAssign** is a server Push carrying a single faction/side byte (the brood-war side
assignment); there is no matching C2S request.

**No combat-pet / summon subsystem exists in this build (CYCLE 7 caveat).** The dev class literally
named `PetPanel` is the **COUPLE / PARTNER window** — its bind function is driven by **5/53** and it
displays a *partner player actor* (self + partner HP bars, couple/marriage notices). It is **not** a pet
UI. There is **no creature-pet, companion, or summon** protocol surface: the only "creature" is the
cosmetic attached-prop (the **2/106 Cmsg_CreatureItemTick** keepalive, §3), and the "summon" effect-ids
are item-use particle ids (item-use effect pushes), not creature spawns.

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
server/NPC-service driven (capture-pending). **2/41 = Cmsg_HudSelect41 (single 12-byte record)** and
**2/145 = Cmsg_SkillListSubmit (`[u32 count][12B record × count]`)** are NEW finds — staged for
`opcodes.md` + `names.yaml` (do not apply here). On a level threshold the server pushes **5/32
SmsgLevelUp (48-byte body)** — levels 12 / 24 are the **class-evolution panel trigger** (no C2S
request; pure server Push).

### 2.12b Buff / Status / Debuff

The buff/status channel is a pure server Push — buffs are server-applied (item/skill use, area effects)
and broadcast inbound; there is no dedicated C2S "request buff" send. *spec:* `buffs.md`, `structs/actor.md`.

| Request (C2S) | size | Response(s) (S2C) | size | Evidence / confidence | packets/ |
|---|---|---|---|---|---|
| *(none — server-applied)* | — | **5/31** SmsgBuffSlotUpdate *(buff/status slot table delta)* | 56 | pure-Push/high — **CORRECTED (binary wins, CYCLE 7):** the buff/status update opcode is **S2C 5/31, 56-byte body** — this **SUPERSEDES the older "4/102" buff label**. The local buff table is a 30-slot array (12-byte slot stride), ticked client-side ~4000 ms. The eliciting cause (which item/skill/area applied a given slot) is push-only; CC-code VALUE meanings are `[capture/debugger-pending]`. | *(5/31 push)* |

### 2.12c Death / Respawn / Ground pickup

| Request (C2S) | size | Response(s) (S2C) | size | Evidence / confidence | packets/ |
|---|---|---|---|---|---|
| *(none — server-detected)* | — | **5/10** *(death push)* | var | pure-Push/high — the server detects death (alive-gate cleared, action-state = death on the actor record) and pushes **5/10** inbound; there is no C2S "I died" send. | *(5/10 push)* |
| **2/3** *(respawn choice)* | var | **4/28** + **5/28** *(respawn result + world re-place)* | var / var | inferred/med — the respawn-choice send answers with the 4/28 result and the 5/28 world re-placement push; both installed. | *(opcodes.md 2/3)* |
| **2/15** CmsgPickupItem *(ground pickup)* | 12 | **4/15** SmsgItemWorldPickupAck | 36 | inferred/med — mirror-minor + `opcodes.md` "4/15 pickup ack"; 4/15 installed. (Also listed under §2.4 Inventory.) | `2-15_pickup_item.yaml`, `4-15_item_world_pickup_ack.yaml` |

*spec:* `world_systems.md`, `combat.md`, `world_exit.md`.

### 2.12d Crafting / Production

The "production" minigame is a two-step C2S: a select/toggle sub-step then the commit; the commit (not
the select) is what the server answers. *spec:* `crafting.md`.

| Request (C2S) | size | Response(s) (S2C) | size | Evidence / confidence | packets/ |
|---|---|---|---|---|---|
| **2/151** CmsgProductBuy *(production select/toggle, 1-byte selector — shared with the cash-shop selector form, §2.13)* | 1 | (sub-step — no terminal ack; UI/list refresh on a panel channel) | — | confirmed/high (routing) — 2/151 is a **select/toggle sub-step**, NOT the production commit. | `2-151_product_buy.yaml` |
| **2/153** *(production COMMIT, 4-byte body; arms a 60 s timeout)* | 4 | **4/79** SmsgCraftingResult | var | confirmed/high — **CORRECTED (binary wins, CYCLE 7):** the production **commit is 2/153 (4B)** and the server answers **4/79 SmsgCraftingResult** (the commit arms a 60-second timeout). 2/153 also carries the cash-shop `CmsgProductConfirm` reading (§2.13) — the **same 4-byte builder** serves both the crafting commit and the product confirm; which path a given send drives is selector/context-dependent (`[capture/debugger-pending]`, R-38). | `2-153_product_confirm.yaml`, *(4/79 craft result)* |

### 2.13 Billing / Market / Mail-Delivery / Product

**Mail / delivery (CYCLE 7).** The real mail feature is the **delivery-inbox + carrier-pigeon**, not 2/60
(2/60 is the couple/marriage request, §2.10). *spec:* `mail.md`.

| Request (C2S) | size | Response(s) (S2C) | size | Evidence / confidence | packets/ |
|---|---|---|---|---|---|
| **2/71** Cmsg_DeliveryClaim *(4-byte claim index)* | 4 | **4/70** Smsg_DeliveryRecord *(inbox record)* | 140 | inferred/med — NEW; claims an inbox slot, server returns the 140-byte delivery record. | *(opcodes.md 2/71)* |
| **2/70** Cmsg_CarrierPigeonSend *(132-byte body)* | 132 | delivery/inbox push (no dedicated same-minor ack) | — | inferred/med — NEW; carrier-pigeon send (the recipient name uses the char[17] short-name cell, the message body the ~84-byte buffer). | *(opcodes.md 2/70)* |
| **2/118** CmsgTenderConfirm *(header-only, stall buy-confirm)* | 0 | tender-listing push (no 4/118 ack) | — | pending — no dedicated 4/118; the server populates the tender listing on a separate channel. | `2-118_tender_confirm.yaml` |
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

\* `2/8 CmsgHotbarSync` (241-byte snapshot upload) and `2/106 Cmsg_CreatureItemTick` (1-byte keepalive
for the cosmetic attached prop — the only "creature" surface in this build; see §2.10 PetPanel caveat)
are likely fire-and-forget (no consumer-side wait found) but are marked **pending** — capture-required.

---

## Appendix A — Result / status code map

> **All human MEANINGS of these codes are `[capture/debugger-pending]`.** The tables give each code's
> **structural role** (what it does to control flow), not its message text. Source: the CYCLE 4
> result-code maps (consolidated from the wave-1 handler reads, dirty-room). **Phantom
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

`3/100` reads one 4-byte action/result code at payload offset 0 and runs a large switch with **two
top-level modes**: a **lobby/select-screen mode** (local player not yet present — the
connect/char-action path that primes the conn-state machine) and an **in-world mode** (local player
present — switch {1,2,3,4,5,6,7,22} → in-game tooltip popups). The full set is
`{0, 1–5, 7, 10, 11, 16, 22, 23, 200–211, 220–227, 202/203/232}` (no `case 9` — a `> 9` guard; no
`case 32`). **The CANONICAL per-code (state, sub-state, error-detail, side effect, string-id,
timer-arm) table for both modes is `handlers.md` §23.1 — see there.** Summary of the lobby-mode
structural roles:

| code(s) | structural role (lobby mode) |
|---|---|
| 0 | "return to prior scene" → GameState 6 / sub-state 8. |
| 1, 2, 3, 4, 5, 7, 22, 23 | **recoverable** char-op error → GameState 7 / sub-state 5 (error-detail = code). **Code 23 also shows status string-id 1604 and is EXCLUDED from the retry-timer arm.** Recoverable, NOT a fatal hard error. (in-world: 1-7/22 are distinct tooltip popups; 23 falls through.) |
| 10, 11, 16 | PUBLISH outward via the select-screen handler's publish-code method (mid-range publish set). |
| 200, 201, 204–211, 220–227 | PUBLISH outward via the same publish method — the largest cluster, one contiguous fan to the publisher (no GameState change). |
| 202, 203, 232 | PUBLISH outward AND set the connect-progress state (GameState=2) — these are the codes the conn-state machine later CONSUMES/CLEARS from its pending slot. |
| 212–219, 228–231 | jumptable default — no publish-code call (the true out-of-range tail). |

*Cross-link:* the same numbers 201/202/203/232 also appear as conn-state-machine state codes — they
**share the numbers, not the code path**. 3/100 PRIMES/publishes; the conn-state machine RESOLVES. The
runtime "which code = connect / ok / lost / error" mapping is `[capture-pending]` in both.

---

## Appendix B — CP949 text-field rules

All game text is CP949 (EUC-KR). Two on-wire forms — *source:* the CYCLE 4 CP949 text-field study
(dirty-room), `packets/2-7_whisper.yaml`, `packets/2-83_chat_contextual.yaml`,
`packets/3-21_chat_channel.yaml`, `handlers.md §17`.

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
and is consolidated in **Appendix C — capture/debugger-pending register (R-01..R-45)**. The two
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

## Appendix C — capture/debugger-pending register (R-01..R-45)

> **The honest residual.** Every item below is a wire-byte **VALUE-semantics** question (what a value
> *means* / which code is success vs fail / unit / conditional presence / opaque interior / pairing not
> statically pinned), NOT a structure gap — sizes, offsets and field tables are recovered. This register
> is the future-debugger (`?ext=dbg`) worklist; consolidated (never copied) from the CYCLE 4
> netcode-deep completeness study (dirty-room). Grouped by kind.

### C.1 Enumerations / code-meanings (which numeric value means what)

| R | Opcode(s) | Pending VALUE |
|---|---|---|
| R-01 | 0/0 | meaning of the two trailing u32 server scalars A/B (handshake) |
| R-02 | 3/100 | full action-result-code → user-meaning map (`{0,1-5,7,10,11,16,22,23,200-211,220-227,202/203/232}`; structural roles confirmed in handlers.md §23.1 — only the human VALUE meaning of each code is pending) |
| R-03 | 3/7 | Result(0/1) and Subtype(0=refresh/1=rename-applied/2=delete) polarity confirmation |
| R-04 | 3/6 | ErrorCode bucket strings (`{0xC8/0xC9}/{0xCC..0xD2}/{0xCE}/{0xD4}`) |
| R-05 | 3/5 | BillingFlag enum @+0x1C |
| R-06 | 4/61 | guild action(1..5)×result(1..7) 2-D verdict map; cooldown-days arm |
| R-07 | 4/81 | error-code byte map (`0xFF`/`1..0x18`/`0x15` mm:ss/`0x17` percent) |
| R-08 | 4/15 | outcome selector `{100=shared,101=plain,else}` and failure 1..5→ids |
| R-09 | 4/82 vs 4/108 | confirm billing-points vs gold field identity at runtime |
| R-10 | 2/142 | Op enum (deposit/withdraw/move) = widget-action-id − 7 |
| R-11 | 4/99 / 4/100 | **Cube-Gamble result panel** (NOT combat) — reel phase(3/5)/sub-kind(`0xFF` reset)/wager-value byte meanings |
| R-12 | 5/79, 5/80 | DeathOp/mode byte sets; effect-id selectors |
| R-13 | 5/5 | actor-record visual-state codes (1001/1011/1020/1021/1023/1041) — server-driven vs purely local |
| R-43 | 4/12, 4/16, 4/22 | the leading result-byte values **beyond {0,1}** in the equip/item-slot responses (which code is success vs each failure) — **live-pending (6-D)** |

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
| R-44 | 5/13, 5/52, 5/7 | the **actor-id field VALUE semantics** in the in-game broadcasts (which field carries the actor id and how the client keys its local actor table) — **live-pending (6-D)** |
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
| R-45 | 4/12, 4/16, 4/22 (equip results) | the **exact equip/item C2S send** that elicits each result (an item-panel / quick-equip / item-move send — NOT 2/16 CmsgActorInteract) — **live-pending (6-D)** |

**Register size: 45 distinct capture/debugger-pending VALUE-semantic items** (R-01..R-45), spanning all
five majors — R-01..R-42 from CYCLE 4, plus three **live-pending (6-D)** additions this pass: R-43
(equip result-byte values beyond {0,1}), R-44 (the in-game broadcast actor-id field semantics), and R-45
(the exact equip/item C2S that elicits each 4/x equip result). Every item is a *value* question, not a
*structure* gap. (Plus the standing **CP949 message-DB string-text** items — message ids such as
10006/51027/36004-36028/45xxx/54xxx/65xxx/58xxx/74xxx — which are a **string-table/asset task**, not a
netcode debugger task.) *source:* the CYCLE 4 netcode-deep completeness study (dirty-room).

---

## Open Conflicts (carried forward to a future lane)

- **C1 — `1/2 CmsgLobbyPing` routing.** On this build the lobby ping rides the **shared NetClient send
  convergence** (the same send path as the in-scope char-mgmt builders), in tension with the "separate
  port-10000 lobby socket" doctrine that put the lobby out of scope. 1/2 is kept out of the char-mgmt
  family and sets no in-flight latch. This is an **open arbitration item for a future lobby-socket
  lane** — surfaced to the maintainer (it may mean the lobby ping genuinely shares the game send
  convergence on this build). It is **NOT a netcode-DTO blocker**. *source:* the CYCLE 4 netcode-deep
  Tier-1 ledger (dirty-room, Open Conflicts C1).

---

## Provenance

This master was authored **LAST** in the CYCLE 4 Netcode Deep-Cartography promotion wave — after the
seven neighbour files had already been rewritten — and every pairing was re-derived against those
now-updated neighbours and cited inline. **CYCLE 7 (2026-06-20)** re-verified the file against the live
`doida.exe` IDB SHA `263bd994` and folded in the gameplay (Block C) + dark-subsystem (Block D)
reconciliations: combat melee = 2/52 (no 5/52; 4/99+4/100 = Cube-Gamble, not combat); buff = 5/31
(supersedes 4/102); 5/73 = SmsgQuestComplete (GuildWar refuted); 3/23 = SmsgCharStatusBytesByName (not
a create-result); 2/60 = couple/marriage (not mail); crafting commit 2/153 → 4/79; death 5/10, respawn
2/3 → 4/28+5/28, pickup 2/15 → 4/15; new rows 2/71→4/70, 2/70, 2/106, 1/6/3/7/3/23/5/32, 2/145/2/41,
2/62/2/66/5/64/4/126, 2/118; and the PetPanel = couple-window caveat (no combat-pet/summon subsystem).

- **Pairings** synthesized (never copied) from the CYCLE 4 netcode-deep corpus (dirty-room — the per-wave
  req↔resp lanes + the install-table partition), validated against the install tables, with the
  install-table refutations respected: **no 4/44/4/46 item-move ack** (4/44/4/46 = actor tick-table
  ops), **no 4/30 guild-op ack** (4/30 = SmsgSocialPanelTarget), **no 4/143 quest-keep ack** (4/143/4/144
  = one shared tracked-item-panel handler; the real quest replies are the 5/68/5/73 pushes).
- **Binary-won pairing corrections** applied this pass (the binary wins): `2/81 → 4/61
  SmsgGuildStateChangeResult` (the stale `4/81 = SmsgGuildDiplomacyResult` DROPPED; 4/81 = generic
  SmsgActionErrorResult); `2/151` selector 0 → 3/8, selector 200 → 4/113/4/114/4/115 (the "1/20" tie
  DROPPED); `2/146` reply = `[u32 echoed req_id][u32 local state global]` (8B); `2/153 =
  CmsgProductConfirm` reply opcode `[capture/debugger-pending]` (R-38); `5/73 = SmsgQuestComplete` (vs
  the stale SmsgGuildWarInfoUpdate). *source:* the CYCLE 4 netcode-deep Tier-1 ledger (dirty-room —
  Major-2 / Major-4 worklists + the in-flight latch census + Open Conflicts).
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
- **Capture/debugger register** (Appendix C, R-01..R-45) consolidated from the CYCLE 4 netcode-deep
  completeness study (dirty-room), extended with the CYCLE 7 gameplay/dark-subsystem reconciliation.
- All facts STATIC on anchor **263bd994**. No debugger, no capture this campaign — every wire VALUE
  semantic and every result-code polarity is `[capture/debugger-pending]`.
