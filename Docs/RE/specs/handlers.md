---
status: hypothesis
sample_verified: false
---

# Inbound Handler Behaviour Catalogue — Clean-Room Specification

> Neutral, rewritten behavioural specification of the legacy client's *receive-dispatch*
> handlers. One short section per opcode tuple. No legacy symbols, no binary addresses,
> no pseudo-code. Promoted from a dirty-room recon note and re-expressed from scratch.
>
> **Scope.** This document complements `opcodes.md` (the routing catalogue) and the
> `packets/*.yaml` field specs. It describes **handler behaviour** — what state each
> handler mutates and what UI it drives — for opcode tuples whose field layout is not yet
> specced under `packets/`. It does **not** restate routing already in `opcodes.md`, and it
> is **not** itself a wire-layout source of truth: where it lists offsets, those are
> design hints for the spec author / engineer, not committed `packets/*.yaml` rows.
> **Do not generate structs directly from this file** — author a `packets/*.yaml` first.

## Status header — confidence and verification

- **No live capture was available** for this analysis. Every byte offset below is a
  static read-order inference. Any "seen in N captures" remark that survives from the
  source recon is a pre-existing third-party annotation, reproduced only as a weak hint
  and **not** independently verified here. Treat all offsets/sizes as hypotheses.
- **Routing is the strong part.** Which handler a tuple dispatches to (and the fact that an
  unset table slot is an inert "ignore", not a crash) is a structural fact; it agrees with
  the `confirmed` routing already in `opcodes.md`.
- **Behaviour (state mutated, UI driven) is described at "LIKELY" confidence** unless noted.
  **Exact field offsets are "UNVERIFIED"** until a capture or a cross-checked struct pins them.
- **Confidence tags used below:** `LIKELY` (one consistent read site, plausible) ·
  `UNVERIFIED` (inferred, boundary not pinned — needs a capture) ·
  `CROSS-REF` (rests on a fact already CONFIRMED in another committed spec, cited inline).
- **Korean text fields are CP949-encoded** NUL-terminated byte buffers on the wire, never
  managed strings. Decode CP949 → UTF-16 only at the presentation boundary.
- The consolidated open-questions list is in the final **§9 Unverified / open questions**.

---

## 1. Dispatch model (context for every handler below)

All framed game packets reach a single inbound dispatcher. After it expands any compressed
frame (see `opcodes.md`, "Wire frame header"), it routes on the `major` family:

- **major 0 / KeyExchange** and **major 1 / ServerCommand** — handled by small inline
  switches; only a fixed set of minors is wired. major 1 dispatches only a handful of
  inbound minors; everything else in family 1 is client-emitted.
- **major 2 / GameAction** — client-emitted only; there is intentionally no inbound handler.
- **major 3 / CharacterMgmt** — an inline switch on `minor`; only an enumerated set of
  minors is wired. The set is fully enumerated by the dispatcher (no hidden minors).
- **major 4 / Response** and **major 5 / Push** — each table-driven by `minor`. Each table
  has a fixed bound of **154 slots**; a minor below the bound that was never installed
  resolves to an inert no-op handler (a slot pre-filled to do nothing), and a minor **at or
  above 154 is out of range and is not dispatched at all**. This caps the legal minor space
  per family at 0..153.
- **major 4 specials:** two minors are routed *outside* the table — one shows a popup by
  code, one discards a text payload. (`opcodes.md`: 4/500, 4/50000.)

### Common decode idiom

Nearly every handler bulk-reads a fixed prefix in one call and then indexes named offsets
inside that buffer. Two consequences the spec author relies on:

- **The bulk-read length is the handler's minimum fixed payload size** and is the most
  reliable size signal. It is recorded per handler as "Min fixed payload" and gathered in
  the §8 size table.
- A leading **two-dword actor key** is extremely common: payload `+0` and `+4` form an
  actor-table lookup key of the form `(sort, id)`. `sort` distinguishes actor kinds
  (1 = player character, 2 = mob, 3 = NPC). The **field order — `sort` first vs `id` first —
  differs per handler** and is `UNVERIFIED` until a capture confirms it; where the
  distinction matters it is called out. The local player is recognised by identity, not by a
  flag byte.

A frequent result/ack stereotype: a **result byte** at payload `+8` (`0` = error → show a
UI error string; `1` = success → apply the change), often with a **reason/sub-code byte** at
`+9`. All offsets below are **payload-relative** (relative to frame `+8`), matching
`opcodes.md` and `packets/*.yaml`.

---

## 2. major 3 — CharacterMgmt (inline switch, S2C)

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

### 3/14 — `SmsgSceneEntityUpdate`
- **Variable length.** 3-byte header: mode `u8` at `+0`, param `u8` at `+1`, slot-mask `u8`
  at `+2`.
- **Behaviour (LIKELY):**
  - **mode == 1:** for each set bit in the slot mask (up to 8 slots), read one **981-byte
    character-slot record** and write it into the indexed roster slot. Each record is
    **880-byte SpawnDescriptor** (`CROSS-REF` `structs/spawn_descriptor.md`) + a 96-byte
    stats block + a 1-byte flag + a 4-byte timing dword `= 981`. A header-only frame
    (mask = 0, total 3 bytes) is legal. Roster actor stride ~220 bytes; stats stride ~24 bytes.
  - **mode != 1:** a scene-clear that forwards `param` to a clear routine.
- **Naming caveat (`UNVERIFIED`):** the source recon carried a stale comment labelling this
  handler "3/4". Routing analysis places it at **minor 14**, which agrees with
  `opcodes.md` (3/14 `SmsgSceneEntityUpdate`). Treat the "3/4" label as a typo, **not** a
  routing fact, until a capture of a scene-update frame settles it. **This 981-byte record
  shape mirrors the per-slot record in `packets/3-1_character_list.yaml` (3/1)** — reconcile
  the two specs before committing either layout.

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
- **Min fixed payload: 1812 bytes (0x714)** — the largest Response payload recovered. This is
  the authoritative full guild roster snapshot (max 50 members).
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
  `bag_slots_count`), then refreshes a UI panel.

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
  item-use sub-handler.

### 4/149 — `SmsgItemPanelSlotChunk`
- **Variable length.** `+0` must be `1` and `+4` must equal the local-player id-key, else the
  packet is rejected. `+8` chunk-type `u8` (`0` = equipment region, `1` = inventory/bag
  region); `+9` start index `u8`; `+10` count `u8`; from `+12`, `count` × **16-byte item-slot
  records** copied with a 16-byte stride into the target region.
- **Behaviour:** chunk-type `0` writes into the equipment-slot array; chunk-type `1` writes
  into the main inventory-slot array, bounded by `(bag_slots_count + 3) * 40` slots.
- **`CROSS-REF` authority:** this handler is the source for the **16-byte item-slot record
  stride** and the equipment-array bounds documented in
  `structs/spawn_descriptor.md` (equipment array at SD `+0x54`, 16-byte stride). Re-use that
  struct's numbers; do not independently re-derive them.

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
  panel. The exact panel identity and the destination struct offsets are `UNVERIFIED` — keep
  the neutral label "timed state".

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
UNVERIFIED**: confirm against wire bytes before hard-coding which dword is `sort`.

| Handler | Inferred leading key | Notes |
|---|---|---|
| 5/6 `ActorAutotargetOrMotion`   | `(sort@+0, id@+4)` | generic actor pair |
| 5/31 `BuffSlotUpdate`           | `(sort@+0, id@+4)` | generic actor pair |
| 5/33 `SkillHotbarSlotSet`       | `(sort@+0, id@+4)` | must be local player |
| 5/67 `StatsUpdate`              | `(sort@+0, id@+4)` | sort fixed to player char |
| 5/79 `ActorDeathState`          | `(sort@+0, id@+4)` | generic actor pair |
| 5/124 `ActorVisualFlagsSet`     | `(sort@+0, id@+4)` | generic actor pair |
| 5/136 `ActorTimedStateUpdate`   | `(sort@+0, id@+4)` | generic actor pair |
| 5/76 `PartyMemberJoined`        | `(id@+4, sort@+9)` | id and sort not adjacent |
| 5/147 `ActorCombatFlagUpdate`   | `(id@+0)` only      | sort fixed to player char; no sort dword |
| 5/28 `RespawnAtPoint`           | `(id@+4)` only      | sort fixed to player char |
| 4/109 `LocalActorSkillStateFlag`| `(id-key@+4)`       | must equal local player |
| 4/139 `ItemUseEffect`           | `(id@+4)`           | must equal local player |
| 4/149 `ItemPanelSlotChunk`      | `(flag@+0=1, id-key@+4)` | id-key must equal local player |

---

## 6. The "thin slot" Response family (semantics uncertain — characterised generically)

`opcodes.md` lists many `SmsgResponseSlotNN` entries (4/40, 4/56..4/60, 4/66, 4/70..4/75,
4/122, 4/123, 4/125, 4/126, 4/135, 4/142, 4/151, 4/152, and similar). Sampling several (e.g.
4/122, decoded above) shows a **shared shape**: read a small fixed block (commonly 16 bytes),
test a gate byte near `+0x0C`, and either close/refresh a specific UI panel or show a notice
string keyed by a code byte near `+0x0D`. They mutate **UI panel state only** and carry no
gameplay/world state. **Model them as opaque "panel notice" packets** until a capture
justifies a per-field decode. They are distinct from the inert no-op table slots (which are
truly empty). **Each individual `NN` was sampled, not exhaustively decoded** (`UNVERIFIED`).

---

## 7. Channel disambiguation notes (do not merge these)

- **Normal gold vs billing/cash.** 4/108 `PlayerGoldBalanceUpdate` (normal gold HUD) and 4/82
  `BillingBalanceUpdate` (billing/cash) are separate channels with separate handlers. Keep
  both names; do not merge.
- **4/13 `LocalPlayerStateSync`.** A stale "Push/5-100" suffix in old notes is a mislabel; the
  handler is wired as Response 4/13. `opcodes.md` already carries this note — no further action.
- **3/14 label.** See the 3/14 caveat in §2 — the "3/4" comment is a typo; routing is minor 14.

---

## 8. Minimum-fixed-payload size table

The value is the handler's bulk-read length (payload-relative; the value is the read argument
as observed). It is the **minimum** fixed payload, not necessarily the exact frame size — see
§9. All sizes `UNVERIFIED` pending a capture.

| Opcode | Name | Dir | Min fixed payload | Key fields |
|--------|------|-----|-------------------|-----------|
| 3/8   | `SmsgShopPageUpdate`            | S2C | 4   | single `u32` page index |
| 3/13  | `SmsgCharStatusUpdate`         | S2C | 28  | name@8 + status bytes@25/26 |
| 3/14  | `SmsgSceneEntityUpdate`        | S2C | var | 3B header + N × 981B char-slot record |
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

## 9. Unverified / open questions (a capture would resolve these)

- **No capture this session.** Every offset above is a static read-order inference; the
  "seen in N captures" remarks are pre-existing third-party annotations, not re-verified here.
- **Actor-key field order (sort-first vs id-first)** differs per handler (§5) and is not
  cross-checked against wire bytes. Confirm before hard-coding which dword is `sort`.
- **3/14 vs "3/4" label.** Routing is minor 14 (agrees with `opcodes.md`); the "3/4" comment
  is treated as a typo. A captured scene-update frame would settle it. The 981-byte
  char-slot record must be reconciled with `packets/3-1_character_list.yaml`.
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
