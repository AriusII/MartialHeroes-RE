---
status: routing-confirmed
verification: confirmed (routing/registration/sizes/offsets, control-flow proven); capture/debugger-pending (packet field VALUE semantics)
ida_reverified: 2026-06-16
ida_anchor: 263bd994
evidence: [static-ida]
sample_verified: false
struct_cross_ref_verified: true
conflicts: resolved against the IDB — 3/4<->3/14 swap fixed, 3/7 re-pointed at SmsgCharManageResult, 3/100 "case 32" removed, 4/143+4/144 added (shared handler), major-0 (0,0) handshake branch added, keepalive 2/112 distinguished from the (2,10000) handshake-arm
---

# Inbound Handler Behaviour Catalogue — Clean-Room Specification

> Neutral, rewritten behavioural specification of the legacy client's *receive-dispatch*
> handlers. One short section per opcode tuple. No legacy symbols, no binary addresses,
> no pseudo-code. Promoted from dirty-room recon notes and re-expressed from scratch.
>
> **Verification banner (Campaign 10, anchor `263bd994`, evidence `[static-ida]`).**
> The IDB was re-annotated since this doc was first authored, so the inbound dispatcher
> and BOTH 154-slot install tables were re-read at **control-flow** level. As a result the
> **ROUTING layer is now `[confirmed]`**: which handler each `(major, minor)` tuple
> dispatches to, the registration mechanism (both tables pre-filled with one inert no-op
> handler, real handlers overwriting installed minors), the per-handler bulk-read **sizes**,
> and the field **offsets** within each read are control-flow facts. **Packet field VALUE
> semantics — what a given byte *means* on the wire — remain `[capture/debugger-pending]`**:
> there was no live capture in any pass, so read order is visible but the on-wire meaning of
> each byte is not capture-confirmed. Do not over-claim: a `[confirmed]` size/offset is not a
> `[confirmed]` interpretation of the bytes it spans.
>
> **Scope.** This document complements `opcodes.md` (the routing catalogue) and the
> `packets/*.yaml` field specs. It describes **handler behaviour** — what state each
> handler mutates and what UI it drives — for opcode tuples whose field layout is not yet
> specced under `packets/`. It does **not** restate routing already in `opcodes.md`, and it
> is **not** itself a wire-layout source of truth: where it lists offsets, those are
> design hints for the spec author / engineer, not committed `packets/*.yaml` rows.
> **Do not generate structs directly from this file** — author a `packets/*.yaml` first.
>
> **Coverage.** §1–§9 cover the ~29 handlers carried over from the first recon pass.
> §10–§16 extend the catalogue with the **complete inbound-dispatch sweep** (every wired
> opcode in both table-driven families plus the inline switches), and §17 records the
> field-layout tightenings the second recon pass derived for the already-specced wire
> packets. Together these document every installed handler slot.
>
> // spec: corrections in §1, §2, §7, §12 and §16 reconciled against the Campaign-10 IDB
> re-verification (`Net_DispatchInboundByMajorMinor`, the major-4/major-5 install routines,
> and the NetHandler ctor keepalive/handshake build).

## Status header — confidence and verification

- **Routing & registration are `[confirmed]`** (control-flow, anchor `263bd994`). Which
  handler each `(major, minor)` tuple dispatches to, the fact that an unset table slot is an
  inert no-op (not a null/crash), the 154-slot per-family bound, and the two table base
  offsets are control-flow facts re-read directly from the dispatcher and the install
  routines. The per-handler **bulk-read sizes** and the **byte offsets within each read** are
  likewise control-flow-confirmed (the read discipline is unambiguous).
- **Packet field VALUE semantics are `[capture/debugger-pending]`.** No live capture was
  available in any pass. A confirmed size/offset says *where* a byte sits in the read; it does
  **not** confirm *what* the byte means on the wire. Every "this byte == 1 means success",
  "this code selects string id N", and "this f32 is world X" interpretation below is a static
  read-order inference until a capture (or the IDA debugger on a real event) pins it. Any
  "seen in N captures" remark that survives from a source recon is a pre-existing third-party
  annotation, reproduced only as a weak hint and **not** independently verified here.
- **`sample_verified: false`** — no packet sample (`.tsv`/`.pcapng`) was decoded. The
  `_dirty/samples/` tree contains extracted **asset** files only (meshes, scripts,
  textures), never a network capture.
- **`struct_cross_ref_verified: true`** — the descriptor landmark offsets this catalogue
  relies on (world coordinates, level, combat flag, equipment-slot stride/bounds, bag-slot
  count) were cross-checked against the committed `structs/spawn_descriptor.md` and agree;
  those specific cross-refs are tagged `CONFIRMED` rather than `UNVERIFIED`.
- **Routing matches `opcodes.md`.** Every routing claim below agrees with the `confirmed`
  routing in `opcodes.md`; the four corrections this pass applied (the 3/4↔3/14 swap, 3/7,
  the 3/100 code set, and the 4/143+4/144 pair) are reconciled against it — see §7.
- **Behaviour (state mutated, UI driven) is described at "LIKELY" confidence** unless noted.
  **Exact field VALUE meanings are "UNVERIFIED" / `[capture/debugger-pending]`** until a
  capture or a cross-checked struct pins them, even where the offset they sit at is confirmed.
- **Confidence tags used below:** `LIKELY` (one consistent read site, plausible behaviour) ·
  `UNVERIFIED` / `[capture/debugger-pending]` (the on-wire *meaning* is inferred — needs a
  capture, even when the offset is confirmed) ·
  `CROSS-REF` (rests on a fact already CONFIRMED in another committed spec, cited inline) ·
  `STRUCTURE-HIGH` (the handler's read order / record stride / size are unambiguous from
  the read discipline — now equivalent to `[confirmed]` for the size/offset layer, though no
  capture confirms the *values* on the wire) ·
  `[confirmed]` (routing/registration: control-flow proven against the IDB at anchor
  `263bd994`).
- **Korean text fields are CP949-encoded** (EUC-KR) NUL-terminated byte buffers on the wire,
  never managed strings. Decode CP949 → UTF-16 only at the presentation boundary.
- The consolidated open-questions list is in the final **§18 Unverified / open questions**.

---

## 1. Dispatch model (context for every handler below)

All framed game packets reach a single inbound dispatcher. The dispatcher reads `major` from
the 16-bit field at frame `+4` and `minor` from the 16-bit field at frame `+6` (8-byte frame
header `[u32 size @+0][u16 major @+4][u16 minor @+6]`; see `opcodes.md`, "Wire frame header"),
after it expands any compressed frame. Inbound payloads are **LZ4-decompress-only** on the
client receive path — there is no inverse byte cipher on receive (the cipher routine's single
cross-reference is the outbound send gate, so it is structurally unreachable here; see
`Docs/RE/specs/crypto.md` / `opcodes.md`). The dispatcher then routes on the `major` family.
Routing below is `[confirmed]` (control-flow):

- **major 0 / KeyExchange handshake** — a **separate dispatcher branch**, *not* part of the
  major switch. Only the tuple **(0, 0)** is wired: it runs the key-exchange/handshake
  completion (parses the server key blob, sends the **1/4** reply, and marks the net client
  as keyed). It is a hardwired `(0,0)` branch, **not** an inline switch. (Crypto/handshake
  semantics are owned by `crypto.md`; this catalogue records only that (0,0) is a real inbound
  branch.)
- **major 1 / ServerCommand** — a small inline switch; only inbound minors
  **16, 17, 19, 20** are wired (no default case), everything else in family 1 is
  client-emitted.
- **major 2 / GameAction** — client-emitted only; there is intentionally **no `case 2`** in
  the dispatcher (no inbound handler).
- **major 3 / CharacterMgmt** — an inline switch on `minor`, decoded by a chain of
  subtractions; only the enumerated set **1, 4, 5, 6, 7, 8, 13, 14, 23, 100, 50000** is wired.
  The set is fully enumerated (no hidden minors). **The minors 4, 7 and 14 do NOT map the way
  the first recon pass labelled them — see §2 and §7: minor 4 = `SmsgSceneEntityUpdate`,
  minor 7 = `SmsgCharManageResult`, minor 14 = `SmsgCharSpawnResponse`.**
- **major 4 / Response** and **major 5 / Push** — each table-driven by `minor`. Each table
  has a fixed bound of **154 slots** (`minor < 154` dispatches; `minor >= 154` is out of range
  and not dispatched). Both tables are pre-filled in the NetHandler constructor with a single
  inert **no-op handler** (a concrete shared function that does nothing); the install routines
  then overwrite that default at each installed minor. So an unset minor below the bound
  resolves to the no-op, never a null/crash. The install-routine parse counted **98 Response**
  (96 distinct table slots — 4/143 and 4/144 share one handler — plus the 2 specials below)
  and **65 Push** slots installed; the rest stay inert no-ops. The Response slot index is
  `NetHandler-base + 1246 + minor` (dwords) and the Push slot index is `+ 1400 + minor`; the
  two tables are contiguous (Response ends just before Push begins). These base figures are
  control-flow facts useful to the struct cartographer mapping the NetHandler layout; they are
  internal object offsets, **not** wire offsets.
- **major 4 specials:** two minors are routed *outside* the table — one shows a popup by
  code (4/500), one discards a text payload (4/50000).

### Common decode idiom

Nearly every handler bulk-reads a fixed prefix in one call and then indexes named offsets
inside that buffer. Two consequences the spec author relies on:

- **The bulk-read length is the handler's minimum fixed payload size** and is the most
  reliable size signal. It is recorded per handler as "Min fixed payload" / "Fixed read"
  and gathered in the §8 and §16 size tables.
- A leading **two-dword actor key** is extremely common: payload `+0` and `+4` form an
  actor-table lookup key of the form `(sort, id)`. `sort` distinguishes actor kinds
  (1 = player character, 2 = mob, 3 = NPC). The **field order — `sort` first vs `id` first —
  differs per handler** and is `UNVERIFIED` until a capture confirms it; where the
  distinction matters it is called out. The local player is recognised by identity, not by a
  flag byte.

The reader helpers a handler uses also signal its shape: a **bulk fixed reader** (takes a
length `N`) is the fixed-payload signal; **per-type readers** (`u8`/`u32`/`f32`) and a
**length-prefixed text reader** mark a variable handler — these have no single fixed `N`,
so they are listed as `var` with the fixed-prefix size in brackets.

A frequent result/ack stereotype: a **result byte** at payload `+8` (`0` = error → show a
UI error string; `1` = success → apply the change), often with a **reason/sub-code byte** at
`+9`. A second recurring pair is **`300` / `301`** as a server success/fail code in the
larger state handlers. All offsets below are **payload-relative** (relative to frame `+8`),
matching `opcodes.md` and `packets/*.yaml`.

---

## 2. major 3 — CharacterMgmt (inline switch, S2C)

> **Routing `[confirmed]` (control-flow).** The dispatcher's subtraction chain resolves the
> minors unambiguously: for `minor <= 8` it tests `== 8` (ShopPage) else
> `minor-1`(→1), `-3`(→4), `-1`(→5), `-1`(→6), `== 1`(→7); for `minor > 8` it tests
> `minor-13`(→13), `-1`(→14), `-9`(→23), `-77`(→100), `== 49900`(→50000). So **minor 4 =
> `SmsgSceneEntityUpdate`, minor 7 = `SmsgCharManageResult`, minor 14 =
> `SmsgCharSpawnResponse`** — definitively. The first recon pass had 3/4 and 3/14 *swapped*
> and put `SmsgCharManageResult` at 3/7; those labels are corrected here and in §7/§12/§16.
> Body field VALUE semantics for every major-3 handler remain `[capture/debugger-pending]`.

### 3/8 — `SmsgShopPageUpdate`
- **Min fixed payload: 4 bytes.** Body is a single `u32` shop-page index.
- **Behaviour (LIKELY):** stores the new current shop page in the shop/billing state. If the
  shop window is open, redraws its eight item slots. When a subscriber-discount flag is set,
  a 0.8× price multiplier is applied to the displayed prices.

### 3/13 — `SmsgCharStatusUpdate`
- **Min fixed payload: 28 bytes** (this read includes the 8-byte frame prefix; the effective
  body fields are listed payload-relative below).
- **Fields (UNVERIFIED offsets):** name CP949 `char[17]` at `+8`; status byte A at `+25`;
  status byte B at `+26` (one pad byte to `+28`).
- **Behaviour (LIKELY):** if the named actor is the local player, the two status bytes are
  written into the local-player descriptor. Otherwise the matching roster entry is found **by
  name** (roster stride ~220 bytes, up to 5 entries) and the two bytes are written there. This
  is a character status-flag update for the roster panel.

### 3/4 — `SmsgSceneEntityUpdate`
- **Routing `[confirmed]`: this is minor 4** (corrected — the first recon pass mislabelled it
  "3/14"; the dispatcher's subtraction chain proves minor 4).
- **Variable length.** 3-byte header: mode `u8` at `+0`, param `u8` at `+1`, slot-mask `u8`
  at `+2`.
- **Behaviour (LIKELY; field VALUE meanings `[capture/debugger-pending]`):**
  - **mode == 1:** for each set bit in the slot mask (up to 8 slots), read one **981-byte
    character-slot record** and write it into the indexed roster slot. Each record is
    **880-byte SpawnDescriptor** (`CROSS-REF` `structs/spawn_descriptor.md`) + a 96-byte
    stats block + a 1-byte flag + a 4-byte timing dword `= 981`. A header-only frame
    (mask = 0, total 3 bytes) is legal. Roster slot array = 5 × 880-byte char-select slots
    (the NetHandler allocates exactly 5; stats stride ~24 bytes).
    A two-slot frame is therefore `3 + 2×981 = 1965` bytes (carried hint).
  - **mode != 1:** a scene-clear that forwards `param` to a clear routine.
- **Cross-ref:** the **981-byte record shape is identical to the per-slot record the 3/1
  `SmsgCharacterList` handler reads** (`3B header + N × (880 descriptor + 96 stats + 1 flag +
  4 timing)`) — reconcile against `packets/3-1_character_list.yaml` before committing either
  layout.

### 3/14 — `SmsgCharSpawnResponse`
- **Routing `[confirmed]`: this is minor 14** (corrected — the first recon pass put a
  "`SmsgCharSpawnResult`" at 3/7; that handler is actually here at minor 14, and the 8-byte
  handler the first pass called "3/4 `SmsgCharManageResult`" is actually at minor 7 — see 3/7
  in §12).
- **Fixed read: 16 bytes.** Body byte `+0` is a presence/result gate.
- **Behaviour (LIKELY; field VALUE meanings `[capture/debugger-pending]`):** the gate byte
  selects between re-entering the enter-game builder (carrying the body fields forward into the
  select→world transition) and arming a fallback timeout. Pairs with the 3/5 `SmsgEnterGameAck`
  flow (§12) on the select-screen → in-world path.

---

## 3. major 4 — Response (table-driven, S2C)

> All entries here are currently `confirmed`-routing / `field layout not yet specced` in
> `opcodes.md`. Behaviour at `LIKELY`; offsets `UNVERIFIED`.

### 4/12 — `SmsgEquipItemResult`
- **Min fixed payload: 16 bytes.** `+0..+3` echo/valid dword; `+4` actor-key region;
  `+8` result `u8` (`0` = error → UI error, `1` = ok); `+12` slot-type `u8`.
- **Behaviour:** on success, applies an equipment-slot / visual update to the local player and
  refreshes the rendered equipment. Slot-type value `15` additionally forces a title-slot
  visual rebuild.

### 4/14 — `SmsgGroundItemSlotAck`
- **Min fixed payload: 20 bytes.** A 12-byte item-slot record at `+0..+0x0B`, then result `u8`
  at `+0x0C`.
- **Behaviour:** `0` = error → UI error; `1` = ok → consume the 12-byte slot record into the
  ground-item / inventory path.

### 4/23 — `SmsgUserTradeRequestResult`
- **Min fixed payload: 20 bytes.** `+8` result `u8`; `+9` reason `u8` (values 1..5 each map to
  a distinct decline-reason string id).
- **Behaviour:** success opens the trade window and forwards the whole 20-byte block to the
  trade-window opener; failure shows the reason string.

### 4/61 — `SmsgGuildStateChangeResult`
- **Min fixed payload: 52 bytes.** `+8` gate `u8`; `+9` result `u8`; `+10` action `u8`
  (values 1..5); a guild name string occupies roughly `+11..+28`, NUL-terminated at `+28`
  (CP949).
- **Behaviour:** if the gate byte is non-zero, the whole block is forwarded to a guild-state
  sub-handler and the handler returns. Otherwise the `(result, action)` pair selects a guild
  notice string; the `action == 5, result == 2` path computes a cooldown in minutes from a
  server-config value.

### 4/65 — `SmsgGuildInfoFullSync`
- **Min fixed payload: 1812 bytes (0x714)** — the largest Response payload in the table after
  4/1. This is the authoritative full guild roster snapshot (max 50 members).
- **Header (UNVERIFIED):** gate `u8` at `+8`; guild name CP949 `char[18]` at `+10`;
  guild id `i16` at `+28`; gold `i32` at `+32`; fund `i64` at `+36`; exp `i64` at `+44`;
  costume `i32` at `+52`.
- **Then seven parallel 50-entry member arrays (UNVERIFIED offsets):**

  | Member array | Element | Count | Approx. offset | Approx. size |
  |---|---|---|---|---|
  | ids        | `u32`        | 50 | `+60`   | 200 |
  | ranks      | `u8`         | 50 | `+260`  | 50  |
  | names      | CP949 `char[17]` | 50 | `+310`  | 850 |
  | online     | `u8`         | 50 | `+1160` | 50  |
  | points     | `u32`        | 50 | `+1212` | 200 |
  | contrib    | `u32`        | 50 | `+1412` | 200 |
  | loginTimes | `u32`        | 50 | `+1612` | 200 |

- **Behaviour:** gate `== 1` → forward the whole block to a guild-cache sub-handler (the
  normal roster-update path). gate `!= 1` is a "left/disbanded guild" path: it clears the
  local player's guild fields, optionally deducts a guild fund/penalty from XP, and shows a
  guild-left message.

### 4/80 — `SmsgPvpDeathResult`
- **Min fixed payload: 80 bytes.** `+8` status `u8`; `+9` reason `u8`;
  `+0x14` (20) target-id `u32` (an NPC actor id, looked up with sort = NPC).
- **Behaviour:** status `== 1` routes the block to a rank-progress sub-handler; otherwise it
  drives the death-result UI. The reason byte (cases 0..15) selects a distinct PvP
  death-reason string id. The target id drives a death effect on the target and a UI panel
  refresh.

### 4/81 — `SmsgActionErrorResult`
- **Min fixed payload: header only** (a switch on `+9` selects how many further bytes are
  read; exact total is `UNVERIFIED`). `+8` status `u8`; `+9` error code `u8`.
- **Behaviour:** the general "your action failed / is on cooldown" feedback channel.
  status `== 1` applies the action via a sub-handler (and, when `+9 == 100`, shows a
  finished message). Otherwise the error code selects a string id: value `0xFF` is a generic
  error; other codes map through a switch. Two codes need extra bytes —
  code `0x15` reads a **seconds `u8` at `+10`** and formats a `mm:ss` cooldown message;
  code `0x17` reads a server-config percent and formats it.

### 4/97 — `SmsgAreaSkillEffectPanel`
- **Min fixed payload: 44 bytes.** No per-field decode in the handler.
- **Behaviour:** the 44-byte block is forwarded verbatim to a result/rank panel and consumed
  downstream as an opaque panel-update record.

### 4/99 — `SmsgCombatResultMessage`
- **Min fixed payload: 16 bytes.** `+8` mode `u8`; `+9` result `u8` (values 1..8).
- **Behaviour:** mode `== 1` forwards the 16-byte block to a combat-result sub-handler.
  Otherwise the result byte selects a combat/resource result string; result cases `3` and `4`
  read a server-config drop/penalty value and format it. Mostly UI messaging.

### 4/100 — `SmsgCombatAttackUpdate`
- **Min fixed payload: 188 bytes (0xBC)** — a large fixed payload, **not** a thin ack.
  `+8` phase `u8`; `+10` sub-kind `i8` (`0xFF` = reset); `+12` value `u32`.
- **Behaviour:** drives a combat-attack / charge UI state. phase `3` starts a timed charge
  (stamps the current time and stores `value`); phase `5` ends it. The full 188-byte block is
  then forwarded to a combat-state sink. **Only `+8/+10/+12` are decoded; the remaining ~176
  bytes are opaque** and need a dedicated pass (`UNVERIFIED`).

### 4/108 — `SmsgPlayerGoldBalanceUpdate`
- **Min fixed payload: 16 bytes.** `+8` gold `qword` (low/high dwords at `+8`/`+0x0C`).
- **Behaviour:** updates the local player's gold balance, then refreshes a gold/HUD panel.
  This is the **normal** gold channel — distinct from the billing/cash channel
  (`opcodes.md` 4/82 `SmsgBillingBalanceUpdate`); do not conflate them.

### 4/109 — `SmsgLocalActorSkillStateFlag`
- **Min fixed payload: 12 bytes.** `+4` actor id-key (must equal the local player, else the
  packet is ignored); `+8` flag `u8`.
- **Behaviour:** stores the flag into the local-player descriptor flag byte that the inventory
  path uses to bound the bag-slot count (`CROSS-REF` `structs/spawn_descriptor.md`
  `bag_slots_count` at SD `+0x305`), then refreshes a UI panel.

### 4/122 — `SmsgResponseSlot122` (thin slot — representative)
- **Min fixed payload: 16 bytes.** gate `u8` at `+0x0C`; notice code `u8` at `+0x0D`.
- **Behaviour:** gate `0` closes a UI panel; otherwise the notice code selects a string id
  (a specific code maps to one fixed string; others form a base-id + code). A thin
  notice/panel handler carrying no gameplay state. See §6 for the wider thin-slot family.

### 4/139 — `SmsgItemUseEffect`
- **Variable length; 24-byte header read.** `+4` actor id (must be the local player, else the
  handler logs an error and returns); `+8` result `u8` (`0` = cancel/clear, `1` = play
  effect); `+0x0A` use-flag `u8`; `+0x0B` slot index `u8`; `+0x0C/+0x10/+0x14` echoed dwords.
- **Behaviour:** on success the slot index is resolved to an item id via the inventory slot
  (this resolution is the load-bearing step), then one of several effect/sound variants is
  chosen by **item-id ranges** (notably ids in 213060037..213060199 select equipment-use
  variants; pet/summon ids 213062504..213062577 trigger summon effects/sound). A 28-byte echo
  record (id, result, use-flag, slot, echoed fields, 17-byte name) is forwarded to an
  item-use sub-handler. The item-id range table is shared with 4/5, 5/5 and 5/139.

### 4/149 — `SmsgItemPanelSlotChunk`
- **Variable length.** `+0` must be `1` and `+4` must equal the local-player id-key, else the
  packet is rejected. `+8` chunk-type `u8` (`0` = equipment region, `1` = inventory/bag
  region); `+9` start index `u8`; `+10` count `u8`; from `+12`, `count` × **16-byte item-slot
  records** copied with a 16-byte stride into the target region.
- **Behaviour:** chunk-type `0` writes into the equipment-slot array; chunk-type `1` writes
  into the main inventory-slot array, bounded by `(bag_slots_count + 3) * 40` slots.
- **`CROSS-REF` authority:** this handler is the source for the **16-byte item-slot record
  stride** and the equipment-array bounds documented in
  `structs/spawn_descriptor.md` (equipment array at SD `+0x54`, 8 records × 16-byte stride,
  CONFIRMED there). Re-use that struct's numbers; do not independently re-derive them.

---

## 4. major 5 — Push (table-driven, S2C)

### 5/6 — `SmsgActorAutotargetOrMotion`
- **Min fixed payload: 16 bytes.** `+0/+4` actor `(sort, id)` pair; `+8` motion code `u8`.
- **Behaviour:** `0` = clear auto-target; `1` = set auto-target; any value `> 1` = play that
  motion id on the actor. A compact actor-control push.

### 5/28 — `SmsgRespawnAtPoint`
- **Min fixed payload: 12 bytes.** `+4` actor id (lookup sort fixed to player character).
- **Behaviour:** ignored when the id is the local player (own respawn is handled elsewhere).
  For a **remote** player character: snapshot the existing actor's last position / rotation /
  state, **remove** the actor from the manager, then **re-create** a fresh player-character
  actor from a freshly built spawn descriptor and copy the saved transform back. Net effect: a
  respawn = despawn + fresh spawn at the same point with the transform preserved. **The
  position is taken from the client's own cached last-known transform, not from this packet**
  (the payload carries only the id).

### 5/31 — `SmsgBuffSlotUpdate`
- **Min fixed payload: 56 bytes (0x38).** `+0/+4` actor `(sort, id)` pair; `+8` slot index
  `u32`; `+12` effect code `u32`; `+16` value `u32`; `+20` extra `u32`.
- **Behaviour:** writes 12-byte status entries `{code, value, extra}` at a 12-byte stride into
  per-actor buff tables (with a local-player mirror). Three index regimes by slot value
  (`UNVERIFIED` semantics): small slots (`<= 30`) also mirror onto the live actor object;
  very large slots (`>= 1,000,000`) target a separate global buff array; the remainder use
  the per-actor table. effect code `44` with a non-zero value on a non-local actor removes
  that actor. This is the buff/status-slot channel — **not** inventory sync.

### 5/33 — `SmsgSkillHotbarSlotSet`
- **Min fixed payload: 20 bytes (0x14).** `+0/+4` actor `(sort, id)` pair (must be the local
  player); `+8` slot `u8` (`< 240`); `+0x0C` value `u32`; `+0x10` points `i16`.
- **Behaviour:** writes an 8-byte hotbar-slot entry `{value, points}` at offset `8 * slot` in
  the skill hotbar table. An authoritative server overwrite of one hotbar slot.

### 5/67 — `SmsgStatsUpdate`
- **Min fixed payload: 36 bytes (0x24).** `+0` sort `u8` (player character); `+4` actor id
  `u32`; `+8` stat0 `u32`; `+12` stat2 `u32`; `+16` current-XP `i64`; `+24` stat6 `u32`;
  `+28` stat4 `u32`; `+32` stat5 `u32`.
- **Behaviour:** writes these into the actor's spawn-descriptor stat fields and, for the local
  player, into stat-cache mirrors. The `i64` at `+16` is current XP. This is the world-entry
  stat-sync push. The neutral stat-slot numbering (`stat0/2/4/5/6`) is preserved verbatim
  pending a mapping to named stats (`UNVERIFIED`); cross-check against `structs/stats.md`.

### 5/68 — `SmsgQuestList`
- **Min fixed payload: 452 bytes (0x1C4)** — a full quest-panel snapshot.
- **Fields (UNVERIFIED):** header `u32` at `+0`; actor id `u32` at `+4`; panel byte C `u8` at
  `+8`; active flag `u8` at `+9`; panel byte B `u8` at `+10`; slot array A `u8[10]` at `+11`;
  slot array B `u8[10]` at `+21`; quest ids `u32[20]` at `+32`; names CP949 `char[17][20]` at
  `+112`. (A 20-element quest-record table with a ~32-byte stride is implied; boundaries
  unpinned.)
- **Behaviour:** active-flag transitions drive opening/closing the quest panel and an SFX.

### 5/76 — `SmsgPartyMemberJoined`
- **Min fixed payload: 36 bytes (0x24).** `+4` actor id `u32`; `+8` event `u8` (`4` = greeting,
  `10` = combat/duel start); `+9` sort `u8`; `+18` name CP949 `char[17]`.
- **Behaviour:** looks up the joiner (sort fixed to player character) and, by event code, plays
  paired greet/combat motions and SFX between the two actors; posts a party rank-progress
  notice if the name matches the local player.

### 5/79 — `SmsgActorDeathState`
- **Min fixed payload: 20 bytes (0x14).** `+0` sort; `+4` id; `+8` op `u32`; `+12` sub-index
  `u32`; `+16` linked-id `u32`.
- **Behaviour by op:**
  - **op 0:** look up a linked NPC actor by linked-id and trigger a death-link state.
  - **op 1:** play a death effect chosen by sub-index (values 1..7 select effect ids
    350000039..350000045).
  - **op 2:** write a death-related actor state field (value 6 or 7 depending on whether
    sub-index `== 1`).
  - **op 3:** a reset.
  - Ignored for the local player.

### 5/124 — `SmsgActorVisualFlagsSet`
- **Min fixed payload: 12 bytes.** `+0` sort; `+4` id; `+8` visual flags `u8`.
- **Behaviour:** writes the flag byte into the actor's spawn-descriptor visual region and
  rebuilds the actor's visual; for the local player the local descriptor is mirrored and a
  HUD/visual panel refreshes. The recon note placed the visual region near SD `+0x230`; this
  offset is **UNVERIFIED** and is not in `structs/spawn_descriptor.md` — do not promote it.

### 5/136 — `SmsgActorTimedStateUpdate`
- **Min fixed payload: 16 bytes.** `+0` sort; `+4` id; `+8` timed value `u32`; `+12` state
  `u8`.
- **Behaviour:** writes the timed value and the state byte into two adjacent live-actor state
  fields, mirrors through a linked-actor state when the actor is in combat, and refreshes a UI
  panel. The exact panel identity is `UNVERIFIED` — keep the neutral label "timed state". The
  recon note places the destinations in the live-actor runtime record (a timed-value slot and
  an adjacent state-byte slot); those are runtime-object offsets, **not** wire offsets and
  **not** SpawnDescriptor offsets — do not promote them to a struct spec.

### 5/147 — `SmsgActorCombatFlagUpdate`
- **Min fixed payload: 8 bytes** — the smallest non-trivial push. `+0` actor id `u32` (sort
  fixed to player character for the lookup); `+4` combat flag `i32`.
- **Behaviour:** writes the flag into the actor's `in_combat_flag` (`CROSS-REF`
  `structs/spawn_descriptor.md` SD `+0x32C`, CONFIRMED). For the local player it also mirrors
  to the local descriptor and shows an enter-combat or leave-combat message.

---

## 5. Actor-key field-order summary (UNVERIFIED — capture would resolve)

The leading two-dword actor key appears in most handlers above, but the **dword order is not
uniform**. The table records, per handler, the order inferred from read sites. **Every row is
UNVERIFIED**: confirm against wire bytes before hard-coding which dword is `sort`. The second
recon pass re-confirmed the *lookup argument order* uniformly as `(sort@+0, id@+4)` for the
Push actor handlers, but that is the call-argument order, not the proven on-wire byte order.

| Handler | Inferred leading key | Notes |
|---|---|---|
| 5/6 `ActorAutotargetOrMotion`   | `(sort@+0, id@+4)` | generic actor pair |
| 5/31 `BuffSlotUpdate`           | `(sort@+0, id@+4)` | generic actor pair |
| 5/33 `SkillHotbarSlotSet`       | `(sort@+0, id@+4)` | must be local player |
| 5/67 `StatsUpdate`              | `(sort@+0, id@+4)` | sort fixed to player char |
| 5/79 `ActorDeathState`          | `(sort@+0, id@+4)` | generic actor pair |
| 5/124 `ActorVisualFlagsSet`     | `(sort@+0, id@+4)` | generic actor pair |
| 5/136 `ActorTimedStateUpdate`   | `(sort@+0, id@+4)` | generic actor pair |
| 5/5 `ActorStateEvent`           | `(sort@+0, id@+4)` | inner event code from actor, not body (§10) |
| 5/76 `PartyMemberJoined`        | `(id@+4, sort@+9)` | id and sort not adjacent |
| 5/147 `ActorCombatFlagUpdate`   | `(id@+0)` only      | sort fixed to player char; no sort dword |
| 5/28 `RespawnAtPoint`           | `(id@+4)` only      | sort fixed to player char |
| 4/109 `LocalActorSkillStateFlag`| `(id-key@+4)`       | must equal local player |
| 4/139 `ItemUseEffect`           | `(id@+4)`           | must equal local player |
| 4/149 `ItemPanelSlotChunk`      | `(flag@+0=1, id-key@+4)` | id-key must equal local player |
| 4/4 `AreaEntitySnapshot`        | per-tag entity key (`u32`) | per-record key, sort = the tag (§10) |
| 5/3 `CharSpawn`                 | `(sort@+0, id@+4)` | descriptor split; lookup arg `(id, sort)` (§17) |
| 5/1 `ActorSpawnExtended`        | `(sort@+0, id@+4)` | sort is a u8 switch selector (§17) |

---

## 6. The "thin slot" Response family (semantics uncertain — characterised generically)

`opcodes.md` lists many `SmsgResponseSlotNN` entries (4/40, 4/47, 4/57, 4/58, 4/60, 4/66,
4/72, 4/123, 4/125, 4/126, 4/135, 4/142, 4/151, 4/152, and similar). Sampling several (e.g.
4/122, decoded above) shows a **shared shape**: read a small fixed block (commonly 12 or 16
bytes), test a gate byte near `+0x0C`, and either close/refresh a specific UI panel or show a
notice string keyed by a code byte near `+0x0D`. They mutate **UI panel state only** and carry
no gameplay/world state. **Model them as opaque "panel notice" packets** until a capture
justifies a per-field decode. They are distinct from the inert no-op table slots (which are
truly empty). **Each individual `NN` was sampled, not exhaustively decoded** (`UNVERIFIED`).

> **Reclassification flag (for the spec author / `opcodes.md` curator).** Two entries that
> `opcodes.md` currently lists in the thin-slot family are **not** thin slots — their fixed
> reads are large structured panel snapshots and they must be reclassified:
> **4/56 (1552-byte block + gate 1)** and **4/71 (1092-byte block)**. The reads of 1552 / 1092
> bytes are payload sizes, not string ids. See §13 Group H.

---

## 7. Channel disambiguation & routing corrections (do not merge / do not regress these)

- **Major-3 minor swap `[confirmed]` (the load-bearing correction this pass applied).** The
  first recon pass had three major-3 minors mislabelled; the dispatcher's subtraction chain
  proves the correct mapping, which also matches `opcodes.md` exactly:
  - **3/4 = `SmsgSceneEntityUpdate`** (the 3-byte-header + N×981 char-slot handler) — NOT 3/14.
  - **3/7 = `SmsgCharManageResult`** (the 8-byte subtype/cooldown handler) — NOT 3/4.
  - **3/14 = `SmsgCharSpawnResponse`** (the 16-byte spawn-confirm handler) — NOT 3/7.

    Any doc/YAML that puts `SmsgSceneEntityUpdate` at 3/14, or `SmsgCharManageResult` at 3/4,
    is wrong (this includes the misnamed `packets/3-4_char_manage_result.yaml` — 3/4 is
    `SmsgSceneEntityUpdate`; the char-manage-result packet is 3/7). See §2 and §12.
- **3/100 code set `[confirmed]` (control-flow): there is NO `case 32`.** The 3/100
  `SmsgCharActionResult` switch handles codes `{0, 1-5, 7, 9-11, 16, 22, 23, 200-211,
  220-227, +202/203/232}` (the 202/203/232 trio additionally drives `GameState = LOADING`).
  The first recon pass's "32" is spurious and the set it listed was a too-narrow subset — see
  §12.
- **4/143 + 4/144 share one handler — `SmsgTrackedItemPanelPair`.** Both Response minors 143
  and 144 are installed to the same handler (this is why the distinct-slot count is 96 while
  98 handler entry points are counted). The first recon pass listed neither minor; they are
  added here (§13 Group F) and routing is `[confirmed]`, behaviour
  `[capture/debugger-pending]`.
- **Normal gold vs billing/cash.** 4/108 `PlayerGoldBalanceUpdate` (normal gold HUD) and 4/82
  `BillingBalanceUpdate` (billing/cash) are separate channels with separate handlers. Keep
  both names; do not merge.
- **4/13 `LocalPlayerStateSync`.** A stale "Push/5-100" suffix in old notes is a mislabel; the
  handler is `[confirmed]` installed at Response minor 13 (proven by the install table).
  `opcodes.md` already carries this note — no further action.
- **Keepalive (2/112) is distinct from the (2/10000) handshake-arm — `[confirmed]`.** These
  are two different C2S frames on the **same** major 2 (both send-side, not in the receive
  tables):
  - **2/10000** is built **once** in the NetHandler constructor (a compressed frame armed at
    ~20000 ms) — the initial handshake-arm.
  - **2/112** is the **runtime keepalive**: a 1-byte toggle frame gated by a master-enable
    flag (the flag is set on world-enter and cleared on leave; arg 1 = enable, arg 2 =
    disable, otherwise send only if enabled). `opcodes.md` carries a 2/112 row alongside the
    (2,10000) entry. Which is sent on-wire and at what cadence is `[capture/debugger-pending]`.
- **Major-0 (0,0) handshake is a real inbound branch.** It is not part of any switch — see §1.
- **Billing has multiple distinct inbound channels.** Keep them separate: 1/16 / 1/17
  (subscription off / on notices), 1/19 (expiry notice), 4/3 (billing-info block), 4/82
  (billing/cash balance), 4/83 (billing item-use), 5/34 (billing banner toggle). None of these
  is the normal gold channel (4/108).

---

## 8. Minimum-fixed-payload size table (first recon pass — §2–§4 handlers)

The value is the handler's bulk-read length (payload-relative; the value is the read argument
as observed). It is the **minimum** fixed payload, not necessarily the exact frame size — see
§18. All sizes `UNVERIFIED` pending a capture. The full sweep size table (every gap opcode) is
in §16.

| Opcode | Name | Dir | Min fixed payload | Key fields |
|--------|------|-----|-------------------|-----------|
| 3/4   | `SmsgSceneEntityUpdate`        | S2C | var | 3B header + N × 981B char-slot record (corrected: minor 4, not 14) |
| 3/8   | `SmsgShopPageUpdate`            | S2C | 4   | single `u32` page index |
| 3/13  | `SmsgCharStatusUpdate`         | S2C | 28  | name@8 + status bytes@25/26 |
| 3/14  | `SmsgCharSpawnResponse`        | S2C | 16  | gate byte@0 → re-enter enter-game builder (corrected: minor 14, not 3/7) |
| 4/12  | `SmsgEquipItemResult`          | S2C | 16  | result@8, slot-type@12 |
| 4/14  | `SmsgGroundItemSlotAck`        | S2C | 20  | 12B item record + result@12 |
| 4/23  | `SmsgUserTradeRequestResult`   | S2C | 20  | result@8, reason@9 |
| 4/61  | `SmsgGuildStateChangeResult`   | S2C | 52  | gate@8, result@9, action@10, name..28 |
| 4/65  | `SmsgGuildInfoFullSync`        | S2C | 1812| guild header + 7 × 50-entry member arrays |
| 4/80  | `SmsgPvpDeathResult`           | S2C | 80  | status@8, reason@9, target-id@20 |
| 4/81  | `SmsgActionErrorResult`        | S2C | hdr+| status@8, error@9 (+10 seconds for 0x15) |
| 4/97  | `SmsgAreaSkillEffectPanel`     | S2C | 44  | opaque panel record |
| 4/99  | `SmsgCombatResultMessage`      | S2C | 16  | mode@8, result@9 |
| 4/100 | `SmsgCombatAttackUpdate`       | S2C | 188 | phase@8, sub-kind@10, value@12 |
| 4/108 | `SmsgPlayerGoldBalanceUpdate`  | S2C | 16  | gold `qword`@8 |
| 4/109 | `SmsgLocalActorSkillStateFlag` | S2C | 12  | actor@4 (local), flag@8 |
| 4/122 | `SmsgResponseSlot122`          | S2C | 16  | gate@0x0C, notice@0x0D |
| 4/139 | `SmsgItemUseEffect`            | S2C | 24 hdr | actor@4, result@8, slot@0x0B |
| 4/149 | `SmsgItemPanelSlotChunk`       | S2C | var | type@8, start@9, count@10, 16B records@12 |
| 5/6   | `SmsgActorAutotargetOrMotion`  | S2C | 16  | actor@0/4, motion@8 |
| 5/28  | `SmsgRespawnAtPoint`           | S2C | 12  | actor id@4 (transform from client cache) |
| 5/31  | `SmsgBuffSlotUpdate`           | S2C | 56  | actor@0/4, slot@8, code@12, value@16, extra@20 |
| 5/33  | `SmsgSkillHotbarSlotSet`       | S2C | 20  | actor@0/4 (local), slot@8, value@0x0C, points@0x10 |
| 5/67  | `SmsgStatsUpdate`              | S2C | 36  | sort@0, id@4, stats + XP `i64`@16 |
| 5/68  | `SmsgQuestList`                | S2C | 452 | full quest-panel snapshot (CP949 names) |
| 5/76  | `SmsgPartyMemberJoined`        | S2C | 36  | id@4, event@8, sort@9, name@18 |
| 5/79  | `SmsgActorDeathState`          | S2C | 20  | sort@0, id@4, op@8, sub-index@12, linked-id@16 |
| 5/124 | `SmsgActorVisualFlagsSet`      | S2C | 12  | sort@0, id@4, visual-flags@8 |
| 5/136 | `SmsgActorTimedStateUpdate`    | S2C | 16  | sort@0, id@4, timed-value@8, state@12 |
| 5/147 | `SmsgActorCombatFlagUpdate`    | S2C | 8   | actor-id@0, combat-flag@4 |

---

## 9. Unverified / open questions — first pass (a capture would resolve these)

- **No capture this session.** Every offset above is a static read-order inference; the
  "seen in N captures" remarks are pre-existing third-party annotations, not re-verified here.
- **Actor-key field order (sort-first vs id-first)** differs per handler (§5) and is not
  cross-checked against wire bytes. Confirm before hard-coding which dword is `sort`.
- **Major-3 minor labels — RESOLVED `[confirmed]`.** The first pass's "3/14 vs 3/4" caveat is
  settled by the dispatcher subtraction chain (§2/§7): minor 4 = `SmsgSceneEntityUpdate`,
  minor 7 = `SmsgCharManageResult`, minor 14 = `SmsgCharSpawnResponse`. No longer an open
  question. The 3/4 (ex-"3/14") 981-byte char-slot record must still be reconciled with
  `packets/3-1_character_list.yaml` (record shape, not routing).
- **Exact frame sizes for 4/80, 4/81, 4/99.** The listed reads (80, header, 16) are
  **minimums**; whether the server sends exactly those bytes or a larger frame the read
  truncates is unverified.
- **4/100 `CombatAttackUpdate`:** only `+8/+10/+12` are decoded; the remaining ~176 bytes are
  opaque and need a dedicated pass + capture.
- **5/68 `QuestList`:** the quest-record stride (~32 bytes) and CP949 name-array boundaries
  are from third-party notes and need a capture to pin.
- **5/31 `BuffSlotUpdate` index regimes:** the `30` and `1,000,000` thresholds are control-flow
  facts; the meaning of the three buff arrays (per-actor / global / local mirror) is inferred.
- **5/124 visual region offset.** The recon's SD `+0x230` is not present in
  `structs/spawn_descriptor.md` and is left `UNVERIFIED`; do not promote it.
- **5/136 destination offsets / panel identity** are deferred — keep the neutral "timed state"
  label.
- **Thin-slot Response family (§6):** characterised generically as UI-panel notices; each `NN`
  was sampled, not exhaustively decoded.
- **Structural facts considered safe to rely on:** the major-3 and major-1 inline switches are
  fully enumerated (no hidden minors); each major-4/major-5 table is bounded at 154 slots with
  inert no-op fill for unset minors and no dispatch at or above 154.

---

# Part II — Complete inbound-dispatch sweep (second recon pass)

> §10–§16 extend the catalogue to **every wired opcode** in both table-driven families plus the
> inline switches, derived by parsing the two install routines mechanically and extracting, per
> handler, the bulk-read length (= minimum fixed payload), the gate/case constants, the
> sub-handler targets, and the string-id-class immediates. Three gameplay-critical handlers
> (4/1, 4/4, 5/5) were read in full and are described in §10. **Confidence is per-handler;**
> sizes are generally `STRUCTURE-HIGH` (the read length is unambiguous) while the field
> *meanings* are `LIKELY`/`UNVERIFIED` and the *byte boundaries inside the block* are
> `UNVERIFIED` until a capture pins them.

## 10. Fully-read structural handlers (highest-value gap fills)

### 4/1 — `SmsgGameStateTick` (the master state-refresh packet)
- **Fixed read: 9100 bytes (0x238C) — `[confirmed]` size** — the **largest Response payload**
  in the table.
- **Branch on body byte `+0` (`[confirmed]`, 3-way):** byte0 `== 0` → **form A**; byte0 `== 1`
  → **form B** (the world-entry path); any other value → **form C**. The world-entry form B is
  the load-bearing one: it bulk-copies two large sub-blocks (`0xFCC` and `0xC10` bytes) into
  the world/actor tables, builds the local player, selects the area BGM, and sets the
  enter-world-ready state. The byte-0 selector position is confirmed; the *meaning* of the
  two copied sub-blocks is `[capture/debugger-pending]`.
- **Behaviour (size `[confirmed]`; interior VALUE semantics `[capture/debugger-pending]`):** a
  periodic full world/game-state snapshot. The handler touches almost every subsystem — game
  state, billing state, rank/progress, the frame-tick scheduler, the net client, the actor
  factory, the texture manager — and **recomputes the skill-cooldown table from the skill
  catalog** as a side effect (a load-bearing observable). Gate / case bytes seen include
  `0, 1, 3, 4, 6, 12, 16, 20, 25, 240, 255`, plus the recurring `300 / 301` success/fail pair.
  The 9100-byte block is a packed multi-section record (stats + cooldowns + flags + several
  sub-tables); its internal section layout is **not decomposed** — this is the single
  highest-value remaining decode target. Do **not** attempt a `packets/*.yaml` for 4/1 until a
  capture + dedicated pass pins the sections.

### 4/4 — `SmsgAreaEntitySnapshot` (the in-world spawn / update stream)
- **Variable length: a 17-byte area header, then a tag loop terminated by a zero tag.**
  This is the most important gap recovered — the in-world area spawn/update stream.
- **Area header (17 bytes, STRUCTURE-HIGH):**

  | Off | Size | Field | Type | Notes |
  |-----|------|-------|------|-------|
  | +0x00 | 1 | hdr-flag | u8 | read then discarded (does not gate rendering) |
  | +0x01 | 4 | viewer entity id | u32 | read then discarded |
  | +0x05 | 4 | area grid | u32 | read then discarded |
  | +0x09 | 4 | area centre Z | f32 | recenters the actor grid |
  | +0x0D | 4 | area centre X | f32 | recenters the actor grid |

  Only the two `f32` coordinates are consumed; the three leading values are read-then-discarded
  (`UNVERIFIED` — a capture should confirm they truly never matter).

- **Tag loop (STRUCTURE-HIGH on the per-tag sizes):** read one `tag u8`; the loop ends when
  `tag == 0`. Each non-zero tag selects a record size and an action:

  | Tag | Record size | Action |
  |-----|-------------|--------|
  | 1 | 892 bytes (0x37C) descriptor + name | create/update a **player character** actor (sort = 1); also handles the local-player and reposition-existing branches |
  | 2 | 892-byte descriptor | remove any existing actor with that key, then create with sort = mob (2) |
  | 3 | 892-byte descriptor | as tag 2 with sort = NPC (3); additionally orients the actor |
  | 4 | 24-byte ground-item record | spawn a ground item (record fields below) |
  | 6 | 36-byte guild record | set the actor's guild-name string slot |
  | 9 | 24-byte title record | set the actor's title / name overlay |

- **Tag-4 ground-item record (24 bytes, UNVERIFIED field meaning):** id-key `u32` at `+0`;
  template `u32` at `+4`; unused `u32` at `+8`; opaque `i32` at `+12`; pos X `f32` at `+16`;
  pos Z `f32` at `+20`. A zero body spawns a phantom default model (id 201011001) at origin.
- **Tag-6 guild record (36 bytes):** entity key `u32` at `+0`; guild name CP949 asciiz at `+5`.
- **Tag-9 title record (24 bytes):** entity key `u32` at `+0`; state `u8` at `+4`; sub-flag
  `u8` at `+5`; name CP949 asciiz at `+6`. State values 2 and 4 are special.
- **The 892-byte (0x37C) actor body** is the SpawnDescriptor family. **`CROSS-REF`/FLAG:**
  reconcile this **892** figure with the **880-byte** `structs/spawn_descriptor.md`, the
  **908-byte** 5/3 block and the **912-byte** 5/1 block (the differences are
  prefix/trailer framing around the same 880-byte descriptor core); do **not** re-derive the
  descriptor internals here.
- **Behaviour:** rebuilds/updates the visible area — spawns player characters, mobs and NPCs
  from 892-byte descriptors, inserts ground items, prunes stale actors, and applies guild /
  title overlays.

### 5/5 — `SmsgActorStateEvent` (generic per-actor state-event trigger)
- **Fixed read: 32 bytes.** `+0/+4` actor `(sort, id)` pair.
- **Behaviour (field HIGH — read in full):** triggers an effect / sound / visual change on the
  looked-up actor. The **inner event code that selects the effect comes from the actor's own
  spawn descriptor, not from the packet body** (`CROSS-REF` `structs/spawn_descriptor.md`
  `inner_event_code` at SD `+0x14`). Event codes observed and their roles:
  `1001` cast effect · `1011` title change · `1020` name change · `1021` visual-slot change ·
  `1023` buff effect · `1041` set combat flag. Item-id ranges
  213060037..213060104 and 213062504..213062577 add summon / pet effects + sound. Net effect:
  a generic per-actor state-event push whose specifics are driven by the target's descriptor.

---

## 11. major 1 — ServerCommand (inline switch, S2C) — gap fills

These run against the billing and mail subsystems. **Routing `[confirmed]`** (only minors 16,
17, 19, 20 dispatch; no default case). Behaviour `LIKELY`; field VALUE meanings
`[capture/debugger-pending]`.

### 1/16 — `SrvBillingDeactivated`
- **Variable (text-formatting handler; no single fixed read).** Reads a small
  account/expiry-class block, formats a notice and posts it to the main UI; sets the
  billing-active flag **OFF**. No actor key. Behaviour HIGH; field offsets LOW.

### 1/17 — `SrvBillingActivated`
- **Variable (mirror of 1/16).** Same shape; sets the billing-active flag **ON** and shows the
  activated notice. Behaviour HIGH; offsets LOW.

### 1/19 — `SrvBillingExpiryNotice`
- **Fixed read: 20 bytes.** A kind/threshold byte (compared against `33`) gates which expiry
  message shows; updates billing state and shows the expiry notice. Size HIGH; semantics MED.

### 1/20 — `SrvLetterReceived`
- **Variable: a 76-byte fixed header, then a length-prefixed text body.** The header is a
  sender/title/flags region; the length-prefixed blob is the letter body/subject. A presence
  gate (compare against `0`) guards the empty case. The letter is appended to the mail-inbox
  queue and drives the mail UI. Header size HIGH; field meaning MED.

---

## 12. major 3 — CharacterMgmt (select/lobby screen, S2C) — gap fills

These run on the character-select / lobby screen (a different state object than the in-world
singletons), so their precise field VALUE meanings are `[capture/debugger-pending]`; the
**sizes and routing are `[confirmed]`**. String id `5000` recurs as a generic select-screen
message id. **The minor labels here are corrected from the first recon pass — see the §7 swap
note.** (3/4 `SmsgSceneEntityUpdate` is specced in §2, not here.)

### 3/5 — `SmsgEnterGameAck`
- **Fixed read: 44 bytes `[confirmed]`** (a 40-byte block + a 4-byte trailing count). Sets
  `GameState = LOADING`. Body landmarks (offsets `[confirmed]`; meanings
  `[capture/debugger-pending]`): a BillingFlag region near `+0x1C` and a character-count word
  near `+0x28`. Begins the select-screen → world load handshake; pairs with 3/14
  `SmsgCharSpawnResponse`.

### 3/6 — `SmsgRenameCharResult`
- **Fixed read: 19 bytes.** A 12-byte region + name/result tail (a length/slot bound compared
  against `12`). Drives a rename sub-handler and a select-screen message; updates the slot name.
  Size `[confirmed]`; field meaning `[capture/debugger-pending]`.

### 3/7 — `SmsgCharManageResult`
- **Routing `[confirmed]`: this is minor 7** (corrected — the first recon pass put
  `SmsgCharManageResult` at 3/4; the 8-byte handler is at minor 7).
- **Fixed read: 8 bytes `[confirmed]`.** A subtype byte at `+2` distinguishes outcomes
  (`0 / 1 / 2`); the delete-confirm subtype decrements the account's character count and
  formats a same-day cooldown, then shows a select-screen message (id 5000 family) and updates
  the slot state. Size `[confirmed]`; field VALUE meanings `[capture/debugger-pending]`.
  Carries a `Result` byte at `+0`, the `Subtype` byte at `+2`, and a `ReadyTime` region at
  `+4` (offsets `[confirmed]`; semantics pending).

### 3/23 — `SmsgCharStatusBytesByName`
- **Routing `[confirmed]`: this is minor 23** (the first recon pass left it as a "generic wired
  slot" / `SmsgCharCreateResult`; it is actually a status-bytes-by-name update closely
  paralleling 3/13).
- **Fixed read: 28 bytes (0x1C) `[confirmed]`.** If a global gate flag is set, two trailing
  status bytes are written to a global pair; otherwise the handler finds the matching roster
  slot **by name** (byte-wise CP949 name comparison, roster stride 880 bytes, ≤ 5 slots) and
  writes the two status bytes
  there. Size/structure `[confirmed]`; the exact status-byte positions and their meaning are
  `[capture/debugger-pending]`. (Name proposal flagged for the names.yaml curator — not
  applied here.)

### 3/100 — `SmsgCharActionResult`
- **Fixed read: 4 bytes `[confirmed]`** = a single action/result `u32` code. A large switch
  maps the code to select-screen / generic action outcomes; shows messages (id ranges near
  5000 / 10000 / 10001) and can refresh game state / textures / the main handler. A catch-all
  lobby action ack; no actor key, no name field. **Code set `[confirmed]` (control-flow):**
  `{0, 1-5, 7, 9-11, 16, 22, 23, 200-211, 220-227, +202/203/232}`. **There is NO `case 32`** —
  the first recon pass's "32" was spurious and its set was a too-narrow subset. The `202 /
  203 / 232` codes additionally drive `GameState = LOADING`. The code→message mapping
  (which code shows which string) is `[capture/debugger-pending]`.

### 3/50000 — `SmsgGmChatMessage`
- **Variable: a channel/kind `u8`, then a length-prefixed text body.** Posts a GM / system chat
  line into the chat window (CP949 text). Shape HIGH; kind-byte semantics MED.

---

## 13. major 4 — Response gap fills (table-driven, S2C)

Sizes are the handler's bulk-read length (= minimum fixed payload). `var` = no single fixed
read (the handler loops or reads length-prefixed text); the bracketed number is the fixed
prefix it reads first. Gate/case constants are the immediates the handler compares against.

### Group A — large structured payloads (gameplay-critical)
4/1 and 4/4 are described in full in §10. The remaining structured Response payloads:

| Opcode | Name | Fixed read | Behaviour (neutral) | Conf |
|---|---|---|---|---|
| 4/2 | `SmsgGameTickConfig` | 52 | 52-byte config block → frame-tick scheduler + rank/progress state (tick/timing intervals or schedule). No actor key. gate `0`. | size HIGH; fields LOW |
| 4/3 | `SmsgBillingInfo` | 60 | 60-byte billing/subscription block → billing state; shows a billing message + sound. gate `3`. | size HIGH; fields MED |

### Group B — item / inventory / shop / NPC result acks
Mostly result+echo acks: look up an actor, apply a slot change, show a result string on
failure.

| Opcode | Name | Fixed read | Gates / codes | Behaviour (neutral) | Conf |
|---|---|---|---|---|---|
| 4/5  | `SmsgItemUseResult`        | 44 | result 1/7; item-id ranges 213060037.. / 213062504..; sound kind 90 | Item-use result for an actor; on success plays effect+sound chosen by **item-id range** and updates item/HUD. Shares the item-id range table with 4/139, 5/5, 5/139. | size HIGH |
| 4/15 | `SmsgItemWorldPickupAck`   | var | 7, 100, 101 | World item-pickup ack; looks up the actor by id; codes 100/101 are pickup outcomes. | MED |
| 4/16 | `SmsgEquipChangeResult`    | 20 | 0,1,2,4,15,20 | Equip-change result; looks up actor; applies an equipment-slot change; slot code 15 = title slot (as in 4/12). | size HIGH |
| 4/17 | `SmsgQuickEquipSlotAck`    | 16 | 1,10 | Quick-equip slot ack; small result block; no actor lookup. | size HIGH |
| 4/19 | `SmsgNpcBuyOrAcquireAck`   | var | 1 | NPC buy / inventory-acquire ack; looks up an NPC template; shows result message. | MED |
| 4/20 | `SmsgNpcSellItemAck`       | 24 | 0 | NPC sell ack; looks up actor; 24-byte block (item + result). | size HIGH |
| 4/21 | `SmsgNpcShopSlotClearAck`  | 12 | — | Clears an NPC shop slot; thin 12-byte block. | size HIGH |
| 4/22 | `SmsgItemSlotStateAck`     | var | — | Item-slot state ack; per-field reads; updates a slot's state. | LOW |
| 4/24 | `SmsgUserTradeSlotUpdate`  | var | 1,2 | Updates one trade-window slot (own/partner side via 1/2); pairs with 4/23, 4/25. | MED |
| 4/25 | `SmsgUserTradeFullResponse`| 28 (+16) | 0,4,40 | Full trade response; 28-byte block + a 16-byte sub-record; drives the trade window. | size MED |
| 4/137| `SmsgGatheringResult`      | 24 | 0,1,2,4,255 | Gathering result; looks up actor; 255 = generic-fail code; updates gather UI. | size HIGH |
| 4/138| `SmsgNoticeError`          | 12 | — | Generic notice/error; looks up actor; shows a message (~id 1014). | size HIGH |
| 4/140| `SmsgColoredSystemText`    | 112| 11 | Colored system-text line; 112-byte block (color + text region); chat/system UI. CP949 text. | size HIGH |
| 4/148| `SmsgPlaytimeRewardResult` | var (28 hdr) | — | Playtime-reward result; 28-byte header; reward/notice UI. | MED |

### Group C — party / social / interaction (RankProgress-routed cluster)
A large cluster forwards a fixed block to the rank/progress (social/party/rank panel)
aggregator. The fixed read is the panel-record size; behaviour is uniformly "update the named
social/party/rank panel from the block; show a result string on the failure branch."

| Opcode | Name | Fixed read | Notes |
|---|---|---|---|
| 4/30 | `SmsgSocialPanelTarget`        | 20  | sets the social-panel target. |
| 4/35 | `SmsgPartyInviteState`         | 56  | party-invite state panel. |
| 4/36 | `SmsgPartyMemberRemoveResult`  | 56  | gate 1 = ok; party roster panel. |
| 4/37 | `SmsgPartyLeaderActionResult`  | 56  | gate 1; leader-action panel. |
| 4/41 | `SmsgSkillHotbarAssignResult`  | 24  | gates 1,7; skill-hotbar assign ack → rank/progress + skill panel. |
| 4/42 | `SmsgPlayerInteractionState`   | 56  | interaction-state panel. |
| 4/43 | `SmsgPlayerInteractionResult`  | 60  | gate 1; actor lookup + sound; interaction result. |
| 4/47 | `SmsgSocialAckDrain`           | 16  | thin social-ack drain (see §6). |
| 4/48 | `SmsgRankProgressEvent`        | 236 | 236-byte event record forwarded to the rank/progress response-event entry point. |
| 4/49 | `SmsgRankProgressPanelUpdate`  | 32  | forwarded to the rank/progress panel-update entry point. |
| 4/72 | `SmsgResponseSlot72`           | 40  | gate 1; rank/progress panel-slot. |
| 4/76 | `SmsgPartyAcceptResult`        | 52  | gates 1,4,10; actor lookup + facing + sound; party-accept (event 4/10, like 5/76). |
| 4/96 | `SmsgActorGuildRosterEntry`    | 52  | actor lookup; 52-byte roster entry. |
| 4/133| `SmsgRankProgressUpdate`       | 12  | gate `-1` sentinel; rank/progress. |

### Group D — guild result acks (the 0x714-family cluster, minus 4/65 in §3)

| Opcode | Name | Fixed read | Notes |
|---|---|---|---|
| 4/54 | `SmsgGuildRankSlotUpdate`        | 44  | gate 1; updates a guild-rank slot. |
| 4/55 | `SmsgGuildInfoUpdateResult`      | 44  | actor lookup; applies a guild-info update. |
| 4/62 | `SmsgGuildInviteJoinState`       | 80  | guild invite/join state panel. |
| 4/63 | `SmsgGuildMemberRemoveResult`    | 52  | gates 1,6; guild member remove. |
| 4/64 | `SmsgGuildPositionChangeResult`  | 20  | gates 1,2; position-change result. |
| 4/103| `SmsgGuildPanelTextUpdate`       | 204 | 204-byte guild-panel text block → main UI. |

### Group E — billing / cash / shop balance

| Opcode | Name | Fixed read | Notes |
|---|---|---|---|
| 4/82 | `SmsgBillingBalanceUpdate`    | 16  | billing/cash balance `qword` → billing state (distinct from 4/108 normal gold). |
| 4/83 | `SmsgBillingItemUseResult`    | 12  | gates 11,12,16; billing item-use result. |
| 4/84 | `SmsgRecordSlotConsumeResult` | 12  | gates 0,1,7; consume/clear a record slot. |
| 4/113| `SmsgItemShopPurchaseResult`  | 12  | gate 1; item-shop purchase ack. |
| 4/114| `SmsgCashShopActionResult`    | 12  | cash-shop action ack. |
| 4/115| `SmsgItemShopBalanceUpdate`   | var (24 hdr) | gate 0; item-shop balance update. |

### Group F — miscellaneous Response acks

| Opcode | Name | Fixed read | Notes |
|---|---|---|---|
| 4/13 | `SmsgLocalPlayerStateSync`  | 56  | gates 1,5; actor lookup (local); 56-byte local-player state block → frame-tick scheduler + main UI. Wired at Response minor 13 (the stale "5-100" autoname is a mislabel — see §7). |
| 4/28 | `SmsgRespawnConfirm`        | 20  | gates 0,1; removes + recreates an actor (despawn/respawn confirm). |
| 4/39 | `SmsgDiscard39`             | 32  | reads 32 bytes and drops them (drain slot). |
| 4/44/45/46 | `SmsgActorTickTableOpA/B/C` | 16 each | 16-byte actor tick-table ops; 4/44 gate 2; write into per-actor tick tables. |
| 4/50 | `SmsgUpgradeItemResult`     | var | item-upgrade result (per-field reads). |
| 4/74 | `SmsgResponseSlot74`        | 36  | gate 1; 36-byte block → main UI. |
| 4/75 | `SmsgResponseSlot75`        | 184 | 184-byte block → main UI (large panel record). |
| 4/78 | `SmsgServerTimeNotification`| 12  | server-time notification (12-byte time block). |
| 4/79 | `SmsgCraftingResult`        | var | gate 1; crafting result → main UI. |
| 4/93 | `SmsgUserVoteTallyUpdate`   | 16  | 16-byte vote-tally block. |
| 4/95 | `SmsgLocalPlayerBattleMode` | 12  | local-player battle-mode update. |
| 4/101| `SmsgGlobalScalarPairUpdate`| 16  | two scalar values (global pair) update. |
| 4/102| `SmsgSkillWindowStateUpdate`| 476 | 476-byte skill-window state block → main UI. |
| 4/105| `SmsgGagePanelProgressUpdate`| 12 | gate 0; gage/progress panel update. |
| 4/107| `SmsgEventLotteryPickResult`| var (28 hdr) | event lottery number-pick result. |
| 4/120| `SmsgActorTableBatchResult` | var | batch actor-table result → main UI. |
| 4/132| `SmsgGMNoticeError`         | 12  | gate 16; GM notice/error → main UI. |
| 4/134| `SmsgStatChangeNotify`      | var (12 hdr) | gate 7; stat-change notify. |
| 4/143 + 4/144 | `SmsgTrackedItemPanelPair` | var | **both minors share ONE handler** (`[confirmed]` — added this pass; the first recon pass listed neither). A tracked-item / panel-pair update. This is why the Response table holds 96 distinct slots but 98 handler entry points. Behaviour `[capture/debugger-pending]`. |
| 4/146| `SmsgShowMessage51027`      | var | shows message string id 51027 via main UI. |
| 4/150| `SmsgSkillPointUpdate`      | var | gate 1; skill-point update → main UI. |
| 4/153| `SmsgItemPanelSlotRefresh`  | var | code 255 sentinel; refreshes an item-panel slot. |

### Group G — Response-table specials (routed outside the table)

| Opcode | Name | Fixed read | Notes |
|---|---|---|---|
| 4/500 | `SmsgShowPopupByCode` | 4   | reads one `u32` popup code and shows the popup (main UI). |
| 4/50000 | `SmsgDiscardText`   | var | reads a length-prefixed string and discards it (drain). |

### Group H — the "thin slot" family — per-handler sizes (semantics generic, see §6)

| Opcode | Name | Fixed read | Gate/notes |
|---|---|---|---|
| 4/40 | `SmsgResponseSlot40` | var | thin slot. |
| 4/56 | `SmsgResponseSlot56` | 1552| **NOT thin** — 1552-byte block + gate 1 → main UI / rank-progress (large structured panel snapshot; reclassify in `opcodes.md`). |
| 4/57 | `SmsgResponseSlot57` | 28  | thin. |
| 4/58 | `SmsgResponseSlot58` | 52  | thin → rank/progress. |
| 4/60 | `SmsgResponseSlot60` | 16  | gate 1; thin → rank/progress. |
| 4/66 | `SmsgResponseSlot66` | 24  | gate 1; thin → rank/progress. |
| 4/70 | `SmsgResponseSlot70` | 140 | 140-byte block (medium panel record). |
| 4/71 | `SmsgResponseSlot71` | 1092| **NOT thin** — 1092-byte block (large structured panel snapshot; reclassify in `opcodes.md`). |
| 4/123| `SmsgResponseSlot123`| 12  | thin → main UI. |
| 4/125| `SmsgResponseSlot125`| var | thin → main UI. |
| 4/126| `SmsgResponseSlot126`| 12  | gates 0,1; thin → main UI. |
| 4/135| `SmsgResponseSlot135`| 80  | 80-byte block → main UI. |
| 4/142| `SmsgResponseSlot142`| 28  | thin → main UI. |
| 4/151| `SmsgResponseSlot151`| var | thin → main UI. |
| 4/152| `SmsgResponseSlot152`| var | gates 3,101; thin. |

> **String-id caution.** Some immediates near a large read (e.g. `1092`/`1096` by 4/71,
> `1552`/`1556` by 4/56) are almost certainly the **read sizes**, not string ids; they are
> excluded from string-id interpretation above.

---

## 14. major 5 — Push gap fills (table-driven, S2C)

5/5 is described in full in §10. The remaining Push gap handlers, by cluster.

### Group A — actor state / combat / motion pushes (actor-key idiom)

| Opcode | Name | Fixed read | Behaviour (neutral) | Conf |
|---|---|---|---|---|
| 5/9  | `SmsgExpGain`                | 32 | actor key; gates 1,2,3,5 + the 300/301 pair; updates experience and shows an exp/gain effect. | size HIGH |
| 5/10 | `SmsgCharDeath`              | var (20 hdr) | actor key; many state/animation codes (13,16,19,36,80,130,134) + 300/301; plays death animation + sound, sets death state. | MED |
| 5/11 | `SmsgRankXpGain`             | var (20 hdr) | actor key; gates 2,3,5,7 + 300; adds rank XP and shows the gain. | MED |
| 5/12 | `SmsgActorVisualSlotSet`     | var (20 hdr) | actor key; gates 0,2,4,12,15 = visual slot ids (15 = title); sets a visual/equipment slot and rebuilds the actor visual. | MED |
| 5/14 | `SmsgCombatEffectInstanceSpawn` | var (48 body) | actor key; spawns a combat effect instance; effect-id constants select the fx; sound; ground-item tag path. A combat-VFX spawn. | MED |
| 5/15 | `SmsgTrackedWorldObjectRemove` | var (16 hdr) | actor key; removes a tracked world object / effect instance. | MED |
| 5/16 | `SmsgActorVisualSlotClear`   | var (16 hdr) | actor key; gates 0,1,2,4,20; clears a visual/equipment slot and rebuilds visual. | MED |
| 5/42 | `SmsgPlayerPairSystemNotice` | var (16 hdr) | actor key; player-pair (couple) system notice; pairs with 5/53 pair state. | MED |
| 5/61 | `SmsgActorNameOverlaySet`    | var (84 body) | actor key; 84-byte block sets the actor's name-overlay/title region. | MED |
| 5/64 | `SmsgRemoteActorRelationPair`| var (16 body) | actor key; sets a remote actor's relation-pair (couple/friend) id. | MED |
| 5/87 | `SmsgDungeonEventActorClear` | var (16 hdr) | clears a dungeon-event actor state. | LOW |
| 5/88 | `SmsgPvpStateBytes`          | var (16 hdr) | small PvP state-bytes block. | LOW |
| 5/93 | `SmsgActorMobIdChange`       | var (16 hdr) | actor key; changes an actor's mob-id/template (polymorph/transform). | MED |
| 5/94 | `SmsgVoteResult`             | var (16 hdr) | vote-result push. | LOW |
| 5/98 | `SmsgActorClassFormRefresh`  | var (20 hdr) | actor key; refreshes class/form → rank/progress. | MED |
| 5/106| `SmsgTradeStateToggle`       | var (12 hdr) | actor key; gate 1; toggles trade state; sound + texture + main UI. | MED |
| 5/121| `SmsgCharPropertySet`        | var | actor key; sets a character property field. | LOW |
| 5/123| `SmsgGiftCharReceiveConfirm` | 16 | 16-byte gift-character receive confirm → main UI. | size HIGH |
| 5/126| `SmsgActorStanceSet`         | var (12 hdr) | actor key; sets the actor's stance. | MED |
| 5/127| `SmsgStealthToggle`          | var (12 hdr) | actor key; gate 0; toggles stealth and rebuilds the actor skin. | MED |
| 5/139| `SmsgAttackEffect`           | var (12 hdr) | actor key; plays an attack effect + sound by item-id range (same table as 4/5, 5/5). | MED |

### Group B — world / clock / dungeon / billing pushes

| Opcode | Name | Fixed read | Behaviour | Conf |
|---|---|---|---|---|
| 5/18 | `SmsgGameClockUpdate`     | 8  | 8-byte game-clock value → main UI. | size HIGH |
| 5/34 | `SmsgBillingBannerToggle` | var (4 body) | gates 0,1; toggles the billing banner on/off. | size MED |
| 5/39 | `SmsgNopMin39`            | var (12) | reads 12 bytes and does nothing meaningful (no-op slot). | size MED |
| 5/51 | `SmsgSkillGuideState`     | 24 | gate 3; skill-guide panel state. | size HIGH |
| 5/85 | `SmsgDungeonEventStateSyncA` | var (20 hdr) | gates 1,2,4,7 + 300/301; dungeon event state sync A. | MED |
| 5/86 | `SmsgDungeonEventStateSyncB` | var (20 hdr) | gates 1..6 + 300/301; dungeon event state sync B. | MED |
| 5/131| `SmsgPvpRankScoreUpdate`  | var (8 body) | PvP rank-score update → main UI. | MED |
| 5/146| `SmsgPacketResponseAckRequest` | 8 | 8-byte ack-request; replies via the net handler (a keepalive/ack-request channel). | size HIGH |

### Group C — guild / party / quest pushes

| Opcode | Name | Fixed read | Behaviour | Conf |
|---|---|---|---|---|
| 5/21 | `SmsgPartyRosterEvent`      | var (12 body) | actor key; forwards to the party-roster-event entry point (party roster change + notice). | MED |
| 5/26 | `SmsgLocalPlayerRelationSlot`| var | actor key; relation/friend slot update. | MED |
| 5/38 | `SmsgPartyMemberStats`      | 100 | 100-byte party-member stats block → party-panel member-stats update. | size HIGH |
| 5/55 | `SmsgGuildNameDisplayUpdate`| 40  | actor key; 40-byte guild-name display update → main UI / rank-progress. | size HIGH |
| 5/57 | `SmsgTrackedPanelSlotUpdate`| var (24 hdr) | tracked panel-slot update. | MED |
| 5/59 | `SmsgRankProgressSfxEvent`  | 76  | 76-byte rank-progress SFX event → the rank/progress push-SFX entry point. | size HIGH |
| 5/65 | `SmsgGuildMemberRosterUpdate`| var (32 body) | actor key; 32-byte guild-member roster entry → main UI. | MED |
| 5/73 | `SmsgQuestComplete`         | 344 | gate 2; 344-byte quest-complete block → main UI (quest panel + reward). | size HIGH |
| 5/77 | `SmsgRankProgressPanelBulk` | 400 | actor key; 400-byte block → the rank/progress bulk-payload entry point (bulk panel refresh). | size HIGH |

### Group D — PvP cluster

| Opcode | Name | Fixed read | Behaviour | Conf |
|---|---|---|---|---|
| 5/80 | `SmsgPvpDeathFx`        | 16  | actor key; gates 1,6; plays a PvP death FX + facing. | size HIGH |
| 5/89 | `SmsgPvpRevengeRoster`  | 188 | 188-byte PvP revenge-roster snapshot. | size HIGH |
| 5/90 | `SmsgPvpCounters`       | 32  | 32-byte PvP counters block. | size HIGH |
| 5/91 | `SmsgPvpScoreUpdate`    | 124 | 124-byte PvP score block. | size HIGH |
| 5/92 | `SmsgPvpRequestOrNotice`| 40  | 40-byte PvP request/notice block. | size HIGH |

### Group E — monster notices

| Opcode | Name | Fixed read | Behaviour | Conf |
|---|---|---|---|---|
| 5/129| `SmsgMonsterNoticeByMobId`  | 4  | 4-byte mob-id; looks up the mob template and shows a notice → main UI. | size HIGH |
| 5/130| `SmsgMonsterNoticeWithText` | 24 | 24-byte block (mob-id + text region); mob-template lookup + notice. | size HIGH |

---

## 15. Name-reconciliation flags raised by the sweep (for the `opcodes.md`/`names.yaml` curator)

These are flags for the orchestrator who owns `opcodes.md` and `names.yaml`; **this document
does not change those files.**

- **4/56 / 4/71 — reclassify.** `opcodes.md` currently lists both as thin UI-slot updaters with
  uncertain semantics. Their fixed reads are **1552** and **1092** bytes — they are large
  structured panel snapshots, not thin slots. Mark as structured panel records (still S2C).
- **4/13 `LocalPlayerStateSync`.** The handler's autoname carries a stale "5-100" suffix; it is
  installed at Response minor 13. `opcodes.md` already carries this note — no change.
- **4/48 / 4/49 / 5/59 / 5/77** forward to named rank/progress entry points (response-event /
  panel-update / push-SFX / bulk-payload). The existing names are accurate — keep.
- **5/39 `NopMin39`** confirmed as a read-12-then-discard no-op slot — the label is accurate.
- The PvP cluster names (4/80, 5/80, 5/89..5/92) match their fixed sizes/behaviour — keep.

---

## 16. Aggregate fixed-read size table — full sweep (gap opcodes)

Sizes are the bulk-read length (payload-relative, the read argument as observed). `var` =
loops / length-prefixed text; the bracketed number is the fixed prefix only. The §2–§4
handlers are sized in §8. **All sizes are `STRUCTURE-HIGH` (read length unambiguous) but
capture-unverified.**

| Opcode | Name | Dir | Fixed read |
|---|---|---|---|
| 1/16 | `SrvBillingDeactivated`     | S2C | var (text) |
| 1/17 | `SrvBillingActivated`       | S2C | var (text) |
| 1/19 | `SrvBillingExpiryNotice`    | S2C | 20 |
| 1/20 | `SrvLetterReceived`         | S2C | 76 + lp-string |
| 3/4  | `SmsgSceneEntityUpdate`     | S2C | var (3B hdr + N×981B) — corrected (minor 4, not 14) |
| 3/5  | `SmsgEnterGameAck`          | S2C | 44 (40 + 4) — sets GameState=LOADING |
| 3/6  | `SmsgRenameCharResult`      | S2C | 19 |
| 3/7  | `SmsgCharManageResult`      | S2C | 8 — corrected (minor 7, not 3/4) |
| 3/14 | `SmsgCharSpawnResponse`     | S2C | 16 — corrected (minor 14, not 3/7) |
| 3/23 | `SmsgCharStatusBytesByName` | S2C | 28 — corrected (status-bytes-by-name, not CharCreateResult) |
| 3/100| `SmsgCharActionResult`      | S2C | 4 |
| 3/50000 | `SmsgGmChatMessage`      | S2C | var (u8 + lp-string) |
| 4/1  | `SmsgGameStateTick`         | S2C | 9100 |
| 4/2  | `SmsgGameTickConfig`        | S2C | 52 |
| 4/3  | `SmsgBillingInfo`           | S2C | 60 |
| 4/4  | `SmsgAreaEntitySnapshot`    | S2C | var (17B hdr + tag loop; bodies 892/24/36/24) |
| 4/5  | `SmsgItemUseResult`         | S2C | 44 |
| 4/13 | `SmsgLocalPlayerStateSync`  | S2C | 56 |
| 4/15 | `SmsgItemWorldPickupAck`    | S2C | var |
| 4/16 | `SmsgEquipChangeResult`     | S2C | 20 |
| 4/17 | `SmsgQuickEquipSlotAck`     | S2C | 16 |
| 4/19 | `SmsgNpcBuyOrAcquireAck`    | S2C | var |
| 4/20 | `SmsgNpcSellItemAck`        | S2C | 24 |
| 4/21 | `SmsgNpcShopSlotClearAck`   | S2C | 12 |
| 4/22 | `SmsgItemSlotStateAck`      | S2C | var |
| 4/24 | `SmsgUserTradeSlotUpdate`   | S2C | var |
| 4/25 | `SmsgUserTradeFullResponse` | S2C | 28 (+16) |
| 4/28 | `SmsgRespawnConfirm`        | S2C | 20 |
| 4/30 | `SmsgSocialPanelTarget`     | S2C | 20 |
| 4/35 | `SmsgPartyInviteState`      | S2C | 56 |
| 4/36 | `SmsgPartyMemberRemoveResult`| S2C | 56 |
| 4/37 | `SmsgPartyLeaderActionResult`| S2C | 56 |
| 4/39 | `SmsgDiscard39`             | S2C | 32 |
| 4/40 | `SmsgResponseSlot40`        | S2C | var |
| 4/41 | `SmsgSkillHotbarAssignResult`| S2C | 24 |
| 4/42 | `SmsgPlayerInteractionState`| S2C | 56 |
| 4/43 | `SmsgPlayerInteractionResult`| S2C | 60 |
| 4/44 | `SmsgActorTickTableOpA`     | S2C | 16 |
| 4/45 | `SmsgActorTickTableOpB`     | S2C | 16 |
| 4/46 | `SmsgActorTickTableOpC`     | S2C | 16 |
| 4/47 | `SmsgSocialAckDrain`        | S2C | 16 |
| 4/48 | `SmsgRankProgressEvent`     | S2C | 236 |
| 4/49 | `SmsgRankProgressPanelUpdate`| S2C | 32 |
| 4/50 | `SmsgUpgradeItemResult`     | S2C | var |
| 4/54 | `SmsgGuildRankSlotUpdate`   | S2C | 44 |
| 4/55 | `SmsgGuildInfoUpdateResult` | S2C | 44 |
| 4/56 | `SmsgResponseSlot56`        | S2C | 1552 |
| 4/57 | `SmsgResponseSlot57`        | S2C | 28 |
| 4/58 | `SmsgResponseSlot58`        | S2C | 52 |
| 4/60 | `SmsgResponseSlot60`        | S2C | 16 |
| 4/62 | `SmsgGuildInviteJoinState`  | S2C | 80 |
| 4/63 | `SmsgGuildMemberRemoveResult`| S2C | 52 |
| 4/64 | `SmsgGuildPositionChangeResult`| S2C | 20 |
| 4/66 | `SmsgResponseSlot66`        | S2C | 24 |
| 4/70 | `SmsgResponseSlot70`        | S2C | 140 |
| 4/71 | `SmsgResponseSlot71`        | S2C | 1092 |
| 4/72 | `SmsgResponseSlot72`        | S2C | 40 |
| 4/74 | `SmsgResponseSlot74`        | S2C | 36 |
| 4/75 | `SmsgResponseSlot75`        | S2C | 184 |
| 4/76 | `SmsgPartyAcceptResult`     | S2C | 52 |
| 4/78 | `SmsgServerTimeNotification`| S2C | 12 |
| 4/79 | `SmsgCraftingResult`        | S2C | var |
| 4/82 | `SmsgBillingBalanceUpdate`  | S2C | 16 |
| 4/83 | `SmsgBillingItemUseResult`  | S2C | 12 |
| 4/84 | `SmsgRecordSlotConsumeResult`| S2C | 12 |
| 4/93 | `SmsgUserVoteTallyUpdate`   | S2C | 16 |
| 4/95 | `SmsgLocalPlayerBattleMode` | S2C | 12 |
| 4/96 | `SmsgActorGuildRosterEntry` | S2C | 52 |
| 4/101| `SmsgGlobalScalarPairUpdate` | S2C | 16 |
| 4/102| `SmsgSkillWindowStateUpdate` | S2C | 476 |
| 4/103| `SmsgGuildPanelTextUpdate`   | S2C | 204 |
| 4/105| `SmsgGagePanelProgressUpdate`| S2C | 12 |
| 4/107| `SmsgEventLotteryPickResult` | S2C | var (28 hdr) |
| 4/113| `SmsgItemShopPurchaseResult` | S2C | 12 |
| 4/114| `SmsgCashShopActionResult`   | S2C | 12 |
| 4/115| `SmsgItemShopBalanceUpdate`  | S2C | var (24 hdr) |
| 4/120| `SmsgActorTableBatchResult`  | S2C | var |
| 4/123| `SmsgResponseSlot123`        | S2C | 12 |
| 4/125| `SmsgResponseSlot125`        | S2C | var |
| 4/126| `SmsgResponseSlot126`        | S2C | 12 |
| 4/132| `SmsgGMNoticeError`          | S2C | 12 |
| 4/133| `SmsgRankProgressUpdate`     | S2C | 12 |
| 4/134| `SmsgStatChangeNotify`       | S2C | var (12 hdr) |
| 4/135| `SmsgResponseSlot135`        | S2C | 80 |
| 4/137| `SmsgGatheringResult`        | S2C | 24 |
| 4/138| `SmsgNoticeError`            | S2C | 12 |
| 4/140| `SmsgColoredSystemText`      | S2C | 112 |
| 4/142| `SmsgResponseSlot142`        | S2C | 28 |
| 4/143| `SmsgTrackedItemPanelPair`   | S2C | var (shares one handler with 4/144) |
| 4/144| `SmsgTrackedItemPanelPair`   | S2C | var (shares one handler with 4/143) |
| 4/146| `SmsgShowMessage51027`       | S2C | var |
| 4/148| `SmsgPlaytimeRewardResult`   | S2C | var (28 hdr) |
| 4/150| `SmsgSkillPointUpdate`       | S2C | var |
| 4/151| `SmsgResponseSlot151`        | S2C | var |
| 4/152| `SmsgResponseSlot152`        | S2C | var |
| 4/153| `SmsgItemPanelSlotRefresh`   | S2C | var |
| 4/500| `SmsgShowPopupByCode`        | S2C | 4 |
| 4/50000 | `SmsgDiscardText`        | S2C | var (lp-string) |
| 5/5  | `SmsgActorStateEvent`       | S2C | 32 |
| 5/9  | `SmsgExpGain`               | S2C | 32 |
| 5/10 | `SmsgCharDeath`             | S2C | var (20 hdr) |
| 5/11 | `SmsgRankXpGain`            | S2C | var (20 hdr) |
| 5/12 | `SmsgActorVisualSlotSet`    | S2C | var (20 hdr) |
| 5/14 | `SmsgCombatEffectInstanceSpawn`| S2C | var (48 body) |
| 5/15 | `SmsgTrackedWorldObjectRemove` | S2C | var (16 hdr) |
| 5/16 | `SmsgActorVisualSlotClear`  | S2C | var (16 hdr) |
| 5/18 | `SmsgGameClockUpdate`       | S2C | 8 |
| 5/21 | `SmsgPartyRosterEvent`      | S2C | var (12 body) |
| 5/26 | `SmsgLocalPlayerRelationSlot`| S2C | var |
| 5/34 | `SmsgBillingBannerToggle`   | S2C | var (4 body) |
| 5/38 | `SmsgPartyMemberStats`      | S2C | 100 |
| 5/39 | `SmsgNopMin39`              | S2C | var (12) |
| 5/42 | `SmsgPlayerPairSystemNotice`| S2C | var (16 hdr) |
| 5/51 | `SmsgSkillGuideState`       | S2C | 24 |
| 5/55 | `SmsgGuildNameDisplayUpdate`| S2C | 40 |
| 5/57 | `SmsgTrackedPanelSlotUpdate`| S2C | var (24 hdr) |
| 5/59 | `SmsgRankProgressSfxEvent`  | S2C | 76 |
| 5/61 | `SmsgActorNameOverlaySet`   | S2C | var (84 body) |
| 5/64 | `SmsgRemoteActorRelationPair`| S2C | var (16 body) |
| 5/65 | `SmsgGuildMemberRosterUpdate`| S2C | var (32 body) |
| 5/73 | `SmsgQuestComplete`         | S2C | 344 |
| 5/77 | `SmsgRankProgressPanelBulk` | S2C | 400 |
| 5/80 | `SmsgPvpDeathFx`            | S2C | 16 |
| 5/85 | `SmsgDungeonEventStateSyncA`| S2C | var (20 hdr) |
| 5/86 | `SmsgDungeonEventStateSyncB`| S2C | var (20 hdr) |
| 5/87 | `SmsgDungeonEventActorClear`| S2C | var (16 hdr) |
| 5/88 | `SmsgPvpStateBytes`         | S2C | var (16 hdr) |
| 5/89 | `SmsgPvpRevengeRoster`      | S2C | 188 |
| 5/90 | `SmsgPvpCounters`           | S2C | 32 |
| 5/91 | `SmsgPvpScoreUpdate`        | S2C | 124 |
| 5/92 | `SmsgPvpRequestOrNotice`    | S2C | 40 |
| 5/93 | `SmsgActorMobIdChange`      | S2C | var (16 hdr) |
| 5/94 | `SmsgVoteResult`            | S2C | var (16 hdr) |
| 5/98 | `SmsgActorClassFormRefresh` | S2C | var (20 hdr) |
| 5/106| `SmsgTradeStateToggle`      | S2C | var (12 hdr) |
| 5/121| `SmsgCharPropertySet`       | S2C | var |
| 5/123| `SmsgGiftCharReceiveConfirm`| S2C | 16 |
| 5/126| `SmsgActorStanceSet`        | S2C | var (12 hdr) |
| 5/127| `SmsgStealthToggle`         | S2C | var (12 hdr) |
| 5/129| `SmsgMonsterNoticeByMobId`  | S2C | 4 |
| 5/130| `SmsgMonsterNoticeWithText` | S2C | 24 |
| 5/131| `SmsgPvpRankScoreUpdate`    | S2C | var (8 body) |
| 5/139| `SmsgAttackEffect`          | S2C | var (12 hdr) |
| 5/146| `SmsgPacketResponseAckRequest`| S2C | 8 |

---

## 17. Tightened field tables for the already-specced wire packets

> The second recon pass re-read the spawn / movement / vitals / level / skill / chat wire
> packets and tightened (and in places **corrected**) their field tables. **These are
> design hints for whoever owns the `packets/*.yaml` files — this document does not edit those
> YAMLs.** Offsets are payload-relative; `STRUCTURE-HIGH` = the read/write order is
> unambiguous (still capture-unverified). The SpawnDescriptor cross-refs below are
> `CONFIRMED` against `structs/spawn_descriptor.md`.

### 17.1 SpawnDescriptor landmark offsets (descriptor-relative, shared by 5/3, 5/1, 3/1, 3/14)
These agree with `structs/spawn_descriptor.md` (880 bytes total) and resolve earlier wire
ambiguities. **`CONFIRMED` where the committed struct also lists them:**

| Off | Size | Field | Status |
|---|---|---|---|
| +0x00 | 17 | name (CP949 `char[17]`, NUL-terminated) | CONFIRMED (struct) |
| +0x14 | 2  | inner_event_code `u16` (drives 5/5) | CONFIRMED (struct) |
| +0x24 | 8  | current_xp `i64` | CONFIRMED (struct) |
| +0x3A | 2  | level `u16` | **CONFIRMED** — resolves the prior "UNVERIFIED level boundary" caveat |
| +0x3C | 4  | current_hp `u32` | CONFIRMED (struct) |
| +0x40 | 4  | current_mp `u32` | CONFIRMED (struct) |
| +0x44 | 4  | current_stamina `u32` (overloaded as an item id on the ground-item sort path) | CONFIRMED (struct) |
| +0x4C | 4  | world_X `f32` | **CONFIRMED** — resolves the 5/3 world-coordinate ambiguity (NOT +0x44/+0x48, NOT +0x50/+0x54) |
| +0x50 | 4  | world_Z `f32` (world Y forced to 0 on spawn) | **CONFIRMED** |
| +0x54 | 128| equipment slot array (8 records × 16-byte stride) | CONFIRMED (struct; written by 4/149 chunk-type 0) |
| +0x305| 1  | bag_slots_count `u8` (bounds the inventory-slot array) | CONFIRMED (struct) |
| +0x32C| 4  | in_combat_flag `u32` (set by 5/147) | CONFIRMED (struct) |

> **FLAG for the struct cartographer (not a wire change):** a mob-model-id `u16` is read from
> descriptor `+0x34` on the mob-spawn path (sort = 2), which lies inside the struct's
> `+0x2C..+0x39` gap region — reconcile with the named members before promoting.

### 17.2 5/3 `SmsgCharSpawn` — fixed read 908 (prefix 8 + descriptor 880 + trailer 20)

| Off | Size | Field | Type | Conf | Notes |
|---|---|---|---|---|---|
| +0x00 | 4 | Sort | u32 | STRUCTURE-HIGH | low byte = sort (1=PC, 2=mob); lookup arg order `(id@4, sort@0)` |
| +0x04 | 4 | ActorId | u32 | STRUCTURE-HIGH | |
| +0x08 | 880 | SpawnDescriptor | bytes[880] | CROSS-REF | see §17.1 |
| +0x378| 20 | Trailer | bytes[20] | MED | partially consumed (a name-region flag byte) |

- Behaviour: removes any existing actor with the same `(sort,id)`, creates the actor, inserts
  it keyed by `(id,sort)`, sets world position from descriptor +0x4C/+0x50. **Resolves the YAML
  world-coordinate UNKNOWN.**

### 17.3 5/1 `SmsgActorSpawnExtended` — fixed read 912 (12 + descriptor 880 + trailer 20)
Confirms the YAML's 12 + 880 + 20 split exactly. The `Sort` here is a **`u8` switch selector**.

| Off | Size | Field | Type | Conf | Notes |
|---|---|---|---|---|---|
| +0x00 | 1 | Sort | u8 | STRUCTURE-HIGH | 1=PC, 2=mob/NPC, 3=ground item |
| +0x01 | 3 | pad | bytes[3] | STRUCTURE-HIGH | |
| +0x04 | 4 | ActorId | u32 | STRUCTURE-HIGH | |
| +0x08 | 1 | TitleState | u8 | STRUCTURE-HIGH | |
| +0x09 | 1 | TitleSlot/Flag | u8 | MED | |
| +0x0A | 1 | RelationFlag | u8 | MED | guild/relation flag |
| +0x0B | 1 | pad | u8 | STRUCTURE-HIGH | |
| +0x0C | 880 | SpawnDescriptor | bytes[880] | CROSS-REF | same map as 5/3 |
| +0x37C| 20 | Trailer | bytes[20] | MED | byte@+0x37C = combat flag (mirrored to a linked actor); byte@+0x37D = a 60-frame timer gate; byte@+0x37E = a visual byte |

- For sort = 3 (ground item), the descriptor's +0x44 slot is treated as an item id and drives a
  ground-item FX (the overload noted in §17.1).

### 17.4 5/0 `SmsgCharDespawn` — fixed read 12 (confirms the YAML)
`+0` Sort `u32` (low byte meaningful; on the wire almost certainly 1 sort byte + 3 pad),
`+4` ActorId `u32`, `+8` Flags `u8` (`==1` plays a leave SFX + a "%s left" chat line),
`+9..+11` pad. Removes the matching actor unless it is the local player.

### 17.5 5/13 `SmsgActorMovementUpdate` — fixed read 40 — **correction**

| Off | Size | Field | Type | Conf | Notes |
|---|---|---|---|---|---|
| +0x00 | 1 | Sort | u8 | STRUCTURE-HIGH | 1 ⇒ interp path A; 2..3 ⇒ path B |
| +0x01 | 3 | pad | bytes[3] | STRUCTURE-HIGH | |
| +0x04 | 4 | ActorId | u32 | STRUCTURE-HIGH | |
| +0x08 | 4 | Yaw | f32 | STRUCTURE-HIGH | fed to a yaw (axis-Y) quaternion builder |
| +0x0C | 4 | PosX | f32 | STRUCTURE-HIGH | current world X |
| +0x10 | 4 | PosZ | f32 | STRUCTURE-HIGH | current world Z (no world Y on wire) |
| +0x14 | 4 | DestX | f32 | STRUCTURE-HIGH | move-to X |
| +0x18 | 4 | DestZ | f32 | STRUCTURE-HIGH | move-to Z |
| +0x1C | 1 | RunFlag | u8 | STRUCTURE-HIGH | `==1` affects speed/anim |
| +0x1D | 3 | pad | bytes[3] | MED | |
| +0x20 | 4 | **reserved/echo** | bytes[4] | MED | **CORRECTION: not a live speed field.** The handler stores a constant 1.0 here regardless of the wire bytes — model +0x20 as a reserved/echo region, not "SpeedScale". |
| +0x24 | 1 | MotionCode | u8 | STRUCTURE-HIGH | `==5` ⇒ instant-snap branch |
| +0x25 | 1 | pad | bytes[1] | MED | |
| +0x26 | 1 | StanceByte | u8 | STRUCTURE-HIGH | secondary state |
| +0x27 | 1 | pad | bytes[1] | MED | trailing pad to 40 |

### 17.6 2/13 `CmsgMoveRequest` — fixed write 16 — **correction (ModeByte)**
`+0x00` Heading `f32` (atan2-style on the XZ delta to target; angular unit unconfirmed),
`+0x04` TargetX `f32`, `+0x08` TargetZ `f32`, `+0x0C` ModeByte `u8`, `+0x0D` RunFlag `u8`,
`+0x0E..+0x0F` pad. **CORRECTION:** the 4-byte `ModeFlags u32` region decodes to
`ModeByte@+0x0C` + `RunFlag@+0x0D` + 2 pad. The traced builder writes **`ModeByte = 3`** (not
1); drop the "low byte = 1" claim. The first send-arg `mode` is a behaviour gate, not the wire
byte. MED (only one builder traced). Server echoes via S2C 5/13.

### 17.7 5/53 `SmsgActorVitalsAndPairState` — fixed read 32 — **corrections**
`+0x00` Sort `u8` (a wire value of 8 is normalised to 1), `+0x01..+0x03` pad, `+0x04` ActorId
`u32`, **`+0x08/+0x09` = unused filler** (CORRECTION: the YAML's "Byte08/Byte09 record fields"
overstates them — they are read but never consumed), `+0x0A` LevelOrState `u8`, `+0x0B`
StateByte `u8`, `+0x0C` PartnerId `u32` (couple system, when Sort==2), `+0x10` CurrentHp `u32`,
**`+0x14` CurrentMp `u32`** (CORRECTION: resolves "VitalB order unconfirmed" — this is MP),
`+0x18` Stamina `u32`, `+0x1C` VitalC `u32`. Final order HP/MP/Stamina/VitalC is HIGH.

### 17.8 5/32 `SmsgLevelUp` — fixed read 48 — **correction (rank-XP tail)**
`+0x00` Sort `u8`, `+0x01..+0x03` pad, `+0x04` ActorId `u32`, `+0x08` NewLevel `u16`
(class-evolution UI at level 12 and 24), `+0x0A..+0x0B` pad, `+0x0C` RemainingStatPoints `i32`
(local only), `+0x10` Value `i32`, `+0x14` HpMpPacked `i64` (HP low / MP high), `+0x1C` Stamina
`i32`, **`+0x20` RankXpTotal `i64`**, **`+0x28` RankXpWithin `i64`**. **CORRECTION:** the
16-byte tail is **two clean `i64`** values (total @+0x20, within @+0x28) summing exactly to 48 —
replace the YAML's provisional "Tail20 + i64 @+0x24 + tail @+0x2C" split. Core HIGH; the two
i64 assignments MED.

### 17.9 4/29 `SmsgStatUpdate` — fixed read 36 (confirms the YAML offsets)
`+0x00` Handle `u32` (LOW; read, not consumed), `+0x04` SessionToken `u32` (LOW),
`+0x08` ResultOk `u8` (`==1` gate; only then the stat echoes apply), `+0x09..+0x0B` reserved,
`+0x0C` Stat0, `+0x10` Stat1, `+0x14` Stat2, `+0x18` Stat3, `+0x1C` Stat4 (all `u32`),
`+0x20` RemainingStatPoints `u32` (the same global as 5/32 +0x0C). Pairs with C2S 2/29
StatAllocate (5 absolute `u32` stats, 20-byte payload).

### 17.10 2/52 `CmsgUseSkill` — variable (24-byte header + two `u32` arrays) (confirms the YAML)
`+0x00` SkillSlot `u8` (`0xFF` = basic attack), `+0x01..+0x03` pad, `+0x04` AimMode `u32`,
`+0x08` AimScale `f32`, `+0x0C` AimX `f32`, `+0x10` AimZ `f32`, `+0x14` CountA `u16`,
`+0x16` CountB `u16`, then `ArrayA u32[CountA]` at `+0x18` and `ArrayB u32[CountB]` after it.
Total payload = `24 + (CountA + CountB) * 4`.

### 17.11 5/52 `SmsgActorSkillAction` — variable (24-byte header + N × 36-byte records) — **correction**
Header (payload-relative): `+0x00` CasterSort `u8`, `+0x01..+0x03` pad, `+0x04` CasterId `u32`,
`+0x08` CastFlag `u8` (`==0` ⇒ cancel/idle), `+0x09` BasicSelector `u8` (`0xFF` = basic attack),
`+0x0A..+0x0B` pad, `+0x0C` SkillId `u32`, **`+0x10` ActionCode `u8`** (`<0xC8` normal;
`0xC8..0xCB` motion sub-ops; `0xCC` AoE fan-out), `+0x11..+0x13` reserved,
**`+0x14` TargetCount `u8`** (bounded ≤ 40), `+0x15..+0x17` reserved.
**CORRECTION:** ActionCode is at `+0x10` and TargetCount at `+0x14` — **distinct bytes**; the
header ends cleanly at `+0x18` and the records start at `+0x18` with no overlap. Re-model the
YAML's "SkillArg u32 @+0x10": +0x0C = SkillId, +0x10 low byte = ActionCode.

- **Target record (36 bytes, stride confirmed):** `+0x00` TargetSort `u8`, `+0x04` TargetId
  `u32`, `+0x08` HitState `i32` (1 = landed hit), `+0x0C` record word `i32`,
  `+0x10` DamageOrHpDelta `i64` (the live damage-sum loop reads `+0x10`/`+0x14` as a signed
  64-bit accumulator), `+0x18` record word `i32`, `+0x1C..+0x23` reserved.
  **CONFLICT to record:** an older record map placed visible-damage@+0x0C and remaining-HP@+0x14;
  the live damage loop instead sums the 64-bit quantity at +0x10/+0x14. Keep both readings until
  a single-hit capture settles it. Stride is unambiguously 36 bytes.

### 17.12 5/7 `SmsgChatBroadcast` — variable (36-byte header + length-prefixed body) — **refinement**
`+0x00` SenderSort `u8`, `+0x01..+0x03` pad, `+0x04` SenderId `u32`, `+0x08` ContextId `u32`
(target/room/whisper-peer), `+0x0C` reserved `u8`, `+0x0D` SubCommand `u8`, `+0x0E` Channel `u8`
(`==6`/`==7` ⇒ whisper), `+0x0F` reserved `u8`, `+0x10` SenderName CP949 `char[20]`
(+0x10..+0x23), then **body = length-prefixed text** at `+0x24`. **REFINEMENT:** the body is a
length-prefixed block (matching the C2S chat senders), NOT "rest of frame". CP949 (unverified).

### 17.13 C2S chat framing (2/83, 3/21) — confirmations
- **2/83 `CmsgChatContextual`:** 24-byte opaque ContextHeader + `[u32 len][text]` body
  (`len = strlen + 1`, gated `0 < len < 200`). Framing HIGH; header internals LOW.
- **3/21 `CmsgChatChannel`:** 56-byte header (`ChannelSelector u32` at `+0x04`; the rest opaque)
  + `[u32 len][text]` body (`len = strlen + 1`). Gate: ordinary channels reject empty or
  `≥ 200`; `selector % 10 == 5` bypasses the gate. ChannelSelector HIGH; rest LOW.

---

## 18. Unverified / open questions — full sweep (a capture would resolve these)

- **No network capture in either pass.** Every offset/size is a static read inference. The IDB
  "capture xN" / "full-play 2026-06-07" annotations (notably on 4/4) are reproduced as hints,
  not re-verified.
- **4/1 `GameStateTick` (9100B) interior** is the single highest-value remaining target — only
  the size and the cooldown-recompute side effect are recovered; the section layout is not
  decomposed. Do not spec it until a capture + dedicated pass.
- **4/4 `AreaEntitySnapshot` 892-byte tag body** — header + tag loop + per-tag sizes are
  structurally confirmed, but the 892-byte actor body is by cross-ref to the SpawnDescriptor
  family only; reconcile 892 vs the 880/908/912-byte figures before committing, and confirm on
  the wire that the three read-then-discarded header values truly never matter.
- **~40 `var` handlers** (length-prefixed / per-field readers) have no single fixed-N; only the
  fixed prefix is sized — the variable tail (text / arrays) is not.
- **Actor-key field order (sort@+0 vs id@+0)** — uniformly inferred as `(sort@+0, id@+4)` for the
  Push actor handlers from the lookup argument order, but not cross-checked against wire bytes.
- **Gate/case constants vs full code maps** — the listed compares enumerate the case codes; the
  exact code→message/effect mapping was fully read only for 5/5 and 4/4. The big switches (3/100,
  5/10) are MED/LOW.
- **String-id classes** — immediates in 1000..99999 are heuristically "string-id-class"; some are
  read sizes, not ids (e.g. 1092/1096 by 4/71, 1552/1556 by 4/56) and are excluded.
- **4/56 / 4/71 are large structured panels, not thin slots** — flagged for reclassification in
  `opcodes.md` (§15).
- **Major-3 minor labels — RESOLVED `[confirmed]`** (no longer open): minor 4 =
  `SmsgSceneEntityUpdate`, minor 7 = `SmsgCharManageResult`, minor 14 =
  `SmsgCharSpawnResponse` (the first recon pass's swap is corrected). The 3/100 code set has
  **no `case 32`**. The select-screen handlers (3/5, 3/6, 3/7, 3/23, 3/100) mutate lobby/select
  state (a different object than the in-world singletons); their **field VALUE meanings** were
  not read in full and stay `[capture/debugger-pending]` (sizes/routing are confirmed).
- **Keepalive 2/112 vs handshake-arm 2/10000 — distinguished `[confirmed]`** (both C2S
  send-side, same major 2): 2/10000 is built once in the NetHandler ctor (handshake-arm);
  2/112 is the runtime 1-byte keepalive toggle gated by the master-enable flag. Their on-wire
  timing/cadence and the 2/10000 body content stay `[capture/debugger-pending]`.
- **5/52 record internals** are contested (prior IDB map vs the live damage loop). The exact
  ActionCode byte position (+0x10) and the two reserved record words need a single-hit + AoE
  capture.
- **2/13 ModeByte value space** — this builder writes 3; whether a one-shot click writes a
  different value than the heartbeat is unverified (only one builder traced).
- **Chat header internals** for 2/83 (24-byte) and 3/21 (56-byte) are opaque; need caller traces.
- **5/1 / 5/3 trailer per-byte meanings** beyond the combat/visual bytes, and the descriptor
  `+0x34` mob-model-id vs the struct's gap region, need a struct-cartographer reconciliation.
- **Charset confirmation (CP949)** for all name/text fields needs a capture with Korean content.
- **Structural facts considered safe to rely on:** the major-3/major-1 inline switches are fully
  enumerated (no hidden minors); each major-4/major-5 table is bounded at 154 slots with inert
  no-op fill for unset minors and no dispatch at or above 154; the install routines installed
  98 Response and 65 Push handlers.
