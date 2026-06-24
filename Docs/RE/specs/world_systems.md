# World-scene gameplay systems — master index (clean-room spec)

<!--
verification:          confirmed (client-side routing/sizes/offsets/timing constants are control-flow-confirmed
                       against the IDB); capture/debugger-pending (server-authored magnitudes — damage, cooldown
                       wall-clock, XP rates, HP scale — and on-wire VALUE meanings / exact field boundaries).
ida_reverified:        2026-06-24
ida_anchor:            263bd994
readiness:             IMPLEMENTATION-READY for the C# rebuild (control-flow-confirmed against IDB SHA 263bd994); items explicitly tagged debugger-pending / capture-pending / RD-* are NON-blocking runtime residuals to confirm later.
evidence:              [static-ida]
this_pass:             IDB SHA 263bd994, CYCLE 7 (2026-06-20) — added the movement / targeting / pathfinding
                       model (Ch. 18), the death / respawn flow (Ch. 19, with a concise cross-ref to
                       world_exit.md), and the dark / absent-subsystems section (Ch. 20: mounts, auction,
                       instanced dungeons, housing, arena/ladder all confirmed-absent by static exhaustion).
                       No prior claim refuted; the Ch. 16 zone-type enum {0,1,2} is reconfirmed by the
                       movement/combat-gate census. Offsets / opcodes / payload sizes CONFIRMED via static
                       IDA; on-wire VALUE meanings and exact thresholds beyond those listed stay
                       capture/debugger-pending.
CYCLE 11 World block (263bd994): §13.1 scene-state corrected — World is switch case-index 5 / writes value 4 on ENTRY, CharSelect is case-index 4 / writes value 5; layer-node count corrected 4→5 (id 2148 reused). Per-frame loop = 4 phases (input pump → per-view device step+present → round-robin scheduler → frame-rate limiter); the in-game scene-graph camera is FOV 65 / near 5 / far 15000 with 5 view-platforms (re-confirmed).
conflicts:             (1) 5/53 routing CONFIRMED at family-5 slot 1453; the recovered handler NAME is contested —
                           the IDB emphasises an actor-pair-relation role, this index emphasises absolute vitals
                           (HP/MP/stamina). Routing is settled; the canonical NAME + the body's full semantics are
                           capture/debugger-pending (Chapter 1, §14).
                       (2) F1 attack-in-progress flag: on build 263bd994 the clear/re-arm is observed in
                           SmsgLocalPlayerStateSync (4/13), NOT a 4/2 handler. Which push arms-vs-releases the swing
                           window is capture/debugger-pending (Chapter 1).
CORRECTED CYCLE 1 (ida_anchor 263bd994, 2026-06-19): §17 "indoor BGM override" RELABELLED = trade/exchange-busy
                       override (per-local-player actor flag set by the trade state-toggle handler forces a fixed
                       BGM over the .mud→.bgm table and suppresses .bge); no map/region "indoor" attribute is on
                       this path.
-->

> Clean-room neutral spec — a navigable **overview / index** of the in-world ("World scene")
> gameplay systems. No legacy symbols, no binary addresses, no decompiler pseudo-code. Promoted by
> synthesising the already-firewall-clean detail specs under `Docs/RE/`; every chapter cross-links to
> the committed spec that owns its byte tables. **Re-verified against build `263bd994` (Campaign 10,
> Block F) — every World-scene opcode→handler binding in §14 maps EXACTLY to the **two flat 154-slot
> dispatch tables** (family-4 / family-5, base minor 0 each, pre-filled with a default no-op and
> selectively overwritten — §0.1); the GameState-5 in-game roster (§13.1) and the Chapter-16 region
> byte claims are control-flow confirmed. No routing, size, or model claim was refuted; only two
> NAME-level items remain contested (see the banner above).**
>
> This document is the one-stop map for the **protocol engineer** (`Network.Protocol`), the
> **application engineer** (`Client.Application` use cases / handlers), the **domain engineer**
> (`Client.Domain` deterministic state), and the **Godot presentation engineer** (layer 05). It
> does **not** restate field-by-field wire layouts — those live in the per-system detail specs and
> the `packets/*.yaml` field specs. Read this first to find the right detail spec, then go there.

---

## Status block (read first)

| Attribute | Value |
|---|---|
| `status` | `index` — overview/cross-link layer over the Cycle 3 World-scene detail specs |
| `confidence model` | Per-chapter tags inherited from the source specs: CODE-CONFIRMED / SAMPLE-VERIFIED / CAPTURE-VERIFIED / PLAUSIBLE |
| `source basis` | Built from the committed clean specs only (combat, chat, npc_interaction, quests, social, inventory_trade, minimap, progression, equipment_visuals, effects, ui_system; formats config_tables, misc_data, effects, ui_manifests; `opcodes.md`; the new `packets/*.yaml`). No `_dirty/` material was read for this index. |

> **CAPTURE-UNVERIFIED — read this prominently.** **No live network capture was available for any
> World-scene system in Cycle 3.** Every opcode→routing link below is a **hard static fact** read
> from the client's dispatch / send-builder / handler-install sites and is graded **CODE-CONFIRMED**.
> Total payload **sizes** (literal byte counts) are likewise hard facts. But **field meanings,
> signedness, and most field boundaries** inside a payload are **static inferences** and are
> **CAPTURE-UNVERIFIED** until confirmed against a real Wireshark capture. The on-disk **data-table**
> claims (the VFS files each chapter lists) are independently **SAMPLE-VERIFIED** against the real
> 43,347-entry client VFS where a chapter says so, and do **not** depend on a capture. Treat every
> wire body in the linked detail specs as a hypothesis; treat the routing, sizes, and data-file
> linkage as reliable.

All Korean text in these systems is **CP949 / EUC-KR** encoded (no BOM), NUL-padded in fixed
buffers, and is modelled as fixed byte blobs on the wire / CP949 byte runs in memory — never as
managed strings. Register the code page once
(`Encoding.RegisterProvider(CodePagesEncodingProvider.Instance)` → `Encoding.GetEncoding(949)`).

---

## 0. The model in one paragraph — server-authoritative; client = intent + presentation

The World scene is **panel-driven and server-authoritative**. The client holds almost no gameplay
authority: it formats lists, applies *client-side* gates (state / cost / range / bag-count) to avoid
pointless sends, emits **intent** messages (family `2` is entirely client-emitted, with no inbound
major-2 handler), and then **applies** the results the server pushes back (families `3` / `4` / `5`).
Damage, reward grants, equip eligibility, enchant rolls, trade settlement, and currency totals are
all resolved by the server and arrive as **absolute** new values the client copies in (clamping the
local player's vitals against its own locally-computed maxima). What the client computes locally is a
**display/parity mirror** — derived combat stats, the XP bar, the stat-allocation pending-delta
editor — never the authoritative resolution. A re-implementation of the server we are rebuilding must
own every resolution; a re-implementation of the client must reproduce these intents, gates, and
presentation behaviours. This headline is consistent across all chapters below.

Opcodes are written as `major/minor` tuples consistent with the `opcodes.md` frame model (8-byte
header: `size` @+0, `major` @+4, `minor` @+6, payload @+8). **All wire field offsets cited in the
detail specs are payload-relative** (relative to frame +8) unless explicitly stated as in-memory
struct offsets.

### 0.1 The dispatcher is two flat indexed handler tables (not a switch) — CONFIRMED

The inbound router is **not** a `switch`. The network handler singleton installs exactly **two
receive tables — one for family 4, one for family 5** — and **no family-2 receive table exists
anywhere** (this is the structural proof of the "family 2 is entirely client-emitted" claim in
§0). Each table is a **flat array of 154 handler slots indexed directly by the minor opcode**
(family-4 slot base = minor 0; family-5 slot base = minor 0; the two tables sit 154 slots apart in
the singleton). On construction **every slot in both tables is pre-filled with one shared default
no-op handler**, then the two installers selectively overwrite the slots that have a real handler.
A minor opcode with no installed handler therefore resolves to the no-op, not to a crash. A
re-implementation should model the major-4 / major-5 routers as **two flat 154-entry dispatch
arrays with a default fallback**, indexed by minor — not as a switch and not as a hash map.
[CONFIRMED]

The **family-3** handlers are **separate named routines**, not entries in either of these two
tables; their install path sits in the char-select / billing / scene-management band and was not
re-walked in the Campaign-10 Block-F re-verification (flagged for a follow-up lane if `3/8` /
`3/50000` need slot-exact confirmation). [CONFIRMED handler exists; family-3 install path
static-hypothesis]

### 0.2 The network handler is a guest/member pair, with a keep-alive arming — CONFIRMED

The singleton that owns the two dispatch tables is itself a **pair**: it constructs a **guest
sub-handler** and a **member sub-handler** (two instances of the same handler interface — the
likely pre-auth-vs-in-world split), and on construction it **arms a compressed keep-alive** (a
periodic 20-byte `major-2 / minor-10000` packet) that feeds the connection-state model (the
disconnect / conn-state path also funnels into the shared chat-log/notice sink — see §2 and §13.1).
A re-implementation models the connection as guest→member handler promotion plus a periodic
keep-alive heartbeat. [CONFIRMED]

---

## 1. Combat loop (target → attack → result → cadence)

The in-world combat loop is driven by **one global battle-controller** that owns "what am I attacking
and when may I swing again". Target acquisition is **two-tier** (a global hovered/selected id for the
UI tooltip + a per-controller picked target for the action executor). Basic melee **is a skill** —
the same `2/52` `CmsgUseSkill` send carrying skill-slot byte `0xFF`; there is **no separate
plain-attack opcode**. Damage is server-authoritative: the client sends intent, the server returns
the swing/hit animation and **absolute** HP values, and the client renders floating numbers (an 0..7
animation-kind table keyed by element class + crit) and HP bars. Cadence is **server-paced** (a
swing-ready timestamp gated by the per-tick resync pulse / combat-phase update).

| Opcode | Dir | Role |
|---|---|---|
| `2/52` | C2S | `CmsgUseSkill` — skill / basic-melee activate (slot `0xFF` = basic attack); intent only. |
| `2/13` | C2S | `CmsgMoveRequest` — emitted by the click-to-attack approach loop when the target is out of range. |
| `5/52` | S2C | `SmsgActorSkillAction` — swing/hit animation header + per-target hit records (≤40). |
| `5/53` | S2C | `SmsgActorVitalsAndPairState` — **absolute** current HP/MP/stamina (the "damage lands here" observable). |
| `4/100` | S2C | `SmsgCubeGambleReelUpdate` — Cube-Gamble minigame reel/state update (NOT combat-attack timing). |
| `4/99` | S2C | `SmsgCubeGambleResultMessage` — Cube-Gamble minigame result (reward strings; NOT a combat/training result). |
| `4/2` | S2C | per-tick server game-tick/resync pulse (server pacing). |

> **Attack-in-progress flag (capture/debugger-pending).** The local "swing in progress" flag's
> clear/re-arm is **observed in the local-player state-sync push (`5/13`, the movement/state
> update)** on build `263bd994`, **not** in a `4/2` handler. Which push *arms* versus *releases*
> the swing window is capture/debugger-pending; treat `4/2` as the server cadence/tick pulse and
> `5/13` as the observed flag-clear site (see the banner, conflict 2).

- **Driving VFS data:** `items.csv` (`attack_speed`, weapon attack columns), `users.scr` /
  `userlevel.scr` / `userpoint.scr` / `exp.scr` (stat & XP curves) — all in `formats/config_tables.md`.
- **See also:** `specs/combat.md` (combat math + the in-world loop §§9–12); `specs/skills.md`
  (cast gates, AoE shapes, cooldown slots); `packets/2-52_use_skill.yaml`,
  `packets/5-52_actor_skill_action.yaml`, `packets/5-53_actor_vitals_and_pair_state.yaml`.

---

## 2. Chat, whisper & overhead bubbles

Everyday chat — say, shout, party, guild, alliance, event, special, and whisper — is **one C2S
message `2/7`** emitted by a single chat sender, distinguished only by the **first payload byte =
channel code** (`0`/`1`/`2`/`3`/`6`/`7`/`9`/`15`). The channel code is the spine of the subsystem: it
selects routing, the log-line ARGB colour, and the overhead-bubble slot. The scrollback is a
**1000-line ring** (36-byte records, 12 visible lines, 7 channel-filter toggles, CP949 word-wrap).
Overhead "speech bubbles" are fields **on the Actor struct**, one slot per channel family, with a
fixed **5000 ms** lifetime, lifted `scale × {7,8,9,10,12}` above the head. The openers `2/82` / `2/83`
/ `2/84` / `3/21` are **not** say-chat — they are friend-note / announce / relation submits (Chapter 5).

| Opcode | Dir | Role |
|---|---|---|
| `2/7` | C2S | `CmsgChat` (catalog name `CmsgWhisper`) — all everyday channels; first byte = channel code; whisper adds a 16-byte target-name buffer. |
| `5/7` | S2C | `SmsgChatBroadcast` — inbound chat display (36-byte header + variable text body); appends to the log ring and stamps the speaker's overhead bubble. |
| `3/50000` | S2C | `SmsgGmChatMessage` — GM / announcement line. |
| `4/140` | S2C | `SmsgColoredSystemText` — coloured system / notice line. |

- **Driving VFS data:** `msg.xdb` (localized captions / notices); per-account chat-window position in
  the local INI (`<billing_id>_<charname>_CHAT`). UI captions resolved through `formats/config_tables.md`.
- **See also:** `specs/chat.md` (the three components + the per-actor bubble block);
  `specs/social.md` (the wider social protocol); `packets/2-7_whisper.yaml`,
  `packets/5-7_chat_broadcast.yaml`.

---

## 3. NPC interaction (dialogue, shops, repair, storage, services)

Clicking an NPC runs **one central click router** that reads the NPC's **KIND** classifier byte
(descriptor +0x22) and opens exactly one of ~14 **pre-built service panels** from a fixed pool of 117
(it never constructs a panel on click). The default KIND is the **quest giver**, gated by the NPC's
**JOB** code (+0x34) against player rank. Shop and repair catalogues are **baked into the NPC template**
(`npc.scr` template +128), not server-pushed; the price divisor is **1,000,000**. Opening a service
panel is gated by a **bag-count** threshold, not a level gate. There is **no client teleport/warp
opcode** — NPC map travel is server-resolved off the generic interact.

| Opcode | Dir | Role |
|---|---|---|
| `2/19` | C2S | `CmsgNpcInteractOpen` — NPC interact / shop-open (also gather-open). (Body in inventory_trade §7.) |
| `2/20` | C2S | `CmsgNpcSellItem` — sell one item to an NPC. (Body in inventory_trade §7.) |
| `2/100` | C2S | KIND 0x19 "request open NPC" round-trip (1-byte body). |
| `2/110` | C2S | QuestNpcPanel step request (4-byte body; distinct from the unified `2/28`). |
| `2/113` | C2S | Repair / refine commit. |
| `2/115` | C2S | Sundry / product buy / order commit. |
| `2/142` | C2S | Storage ("keep") deposit / withdraw (16-byte body; `op = action − 7`, `amount > 0` to send). |
| `2/143` | C2S | KIND 0x23 quest-item-keep open request (4-byte body). |
| `2/151` / `2/152` / `2/153` | C2S | ProductPanel buy/confirm, page/select, action. |
| `4/19` / `4/20` / `4/21` | S2C | NPC buy-acquire / sell / shop-slot-clear acks. |
| `3/8` | S2C | `SmsgShopPageUpdate` — shop page update. |

- **Driving VFS data:** `npc.scr` (404-byte description records + the +128 shop/repair catalog),
  `npcs.scr` (catalogue), `discript.sc` (context-menu labels), `msg.xdb` (rank / shop / repair
  notices) — `formats/config_tables.md`, `structs/npc.md`; per-area `.arr` placement in
  `formats/npc_spawns.md`.
- **See also:** `specs/npc_interaction.md` (the full KIND→panel table, the panel inventory, the C2S
  send map); `specs/inventory_trade.md` §7 (shop bodies); `specs/quests.md` (the quest-giver path);
  `packets/2-100_npc_open_req.yaml`, `packets/2-110_quest_npc_step.yaml`,
  `packets/2-113_repair_commit.yaml`, `packets/2-115_shop_buy.yaml`, `packets/2-142_storage_op.yaml`,
  `packets/2-143_quest_item_keep.yaml`, `packets/2-151_product_buy.yaml`, `packets/2-19_npc_interact.yaml`,
  `packets/2-20_npc_sell.yaml`.

---

## 4. Quests & dialog

Quest interaction is panel-driven and server-authoritative. A unified C2S **`2/28`** carries all three
quest-dialog actions (accept / proceed / give-up, selected by a sub-action byte: `2`/`3`/`4`). The
server pushes a full **quest-log snapshot** (`5/68`, 452 B; 20 quest ids + names + two 10-entry flag
tables) and a **completion + reward verdict** (`5/73`, 344 B; `apply == 1`, `reward_state` grant/deny)
— but the **reward itself (items/exp/gold) arrives via side-channel opcodes**, not `5/73`. Quest text
has **two distinct sources**: UI captions/headers/notices are numeric **message ids** from `msg.xdb`;
the dialogue **body** lines come from the `npc.scr` script records. The in-game quest log is a
three-tab browser (ACTIVE / COMPLETABLE / AVAILABLE) with a per-tab detail renderer. A **second C2S
quest channel** (AVAILABLE-tab list-row request) exists but its opcode tuple was not traced.

| Opcode | Dir | Role |
|---|---|---|
| `2/28` | C2S | `CmsgQuestAction` — unified accept (2) / proceed (3) / give-up (4); send-only, no inbound major-2 slot. |
| `5/68` | S2C | `SmsgQuestList` — full quest-log snapshot (452 B). |
| `5/73` | S2C | `SmsgQuestComplete` — completion + reward verdict (344 B; UI verdict only). |
| (2nd C2S) | C2S | Quest list-row request — separate send-builder; **opcode/body untraced**. |

- **Driving VFS data:** `quests.scr` (3720-byte quest templates, 488 slots / 122 occupied),
  `npc.scr` (dialogue body), `autoquestion_cl.scr` (anti-bot quiz), `discript.sc`, `msg.xdb` /
  `msginfo.do` (quest captions: 16003, 18022, 18027/18028, 18031/18032/18033, …), `Tutor.scr` +
  `tutor.lua` (sibling tutorial system) — `formats/config_tables.md`, `specs/lua_scripting.md`.
- **See also:** `specs/quests.md` (the unified action, the two pushes, the three-tab window, the
  two text sources); `specs/npc_interaction.md` (completes the KIND→panel table);
  `packets/2-28_quest_action.yaml`, `packets/5-68_quest_list.yaml`, `packets/5-73_quest_complete.yaml`.

---

## 5. Social — party, guild, friend/relation

The client folds friend list, block list, party, and special-bond ("FATE") relationships into **one
relationship model** backed by a flat 16-byte-per-slot table, while a genuinely separate **party
roster** exists at the display layer. Genuine **party** is the C2S `2/35`/`2/36`/`2/37` plus the S2C
`4/35`/`4/36`/`5/21`/`5/38`/`5/76` party-panel family (party cap ≤ 8; a client-side auto-disband
collapses a one-person party). **Guild** is its own cluster (member cap 50, struct-of-arrays full sync
`4/65` at 1812 B). **Friend/relation** submits ride the `2/47`–`2/128` cluster, with the local-player
relation-slot push `5/26`. A **self-target guard** (error id `862010101`) refuses self/stale submits
client-side and sends nothing.

| Opcode | Dir | Role |
|---|---|---|
| `2/35` / `2/36` / `2/37` | C2S | Party invite / leave-or-kick / leader-op (8 B each, `[u8 mode][u32 id]`). |
| `4/35` / `4/36` / `4/37` / `4/76` | S2C | Party invite-state / member-remove-result / leader-action-result / accept-result. |
| `5/21` / `5/38` / `5/76` | S2C | Party roster event / per-member stats (100 B) / member-joined. |
| `2/30` / `2/54`–`2/58` / `2/81` / `2/103` | C2S | Guild action / member-ops / diplomacy / panel-text submits. |
| `4/62` / `4/65` / `5/65` | S2C | Guild invite-join state / full sync (1812 B) / per-actor roster patch. |
| `2/122` / `2/123` / `2/128` / `2/47`–`2/76` | C2S | Friend / relation / FATE submits. |
| `5/26` | S2C | `SmsgLocalPlayerRelationSlot` — relation-slot update (28 B). |

- **Driving VFS data:** `msg.xdb` (party / guild / relation notices: 862010101, 2119–2122, 23004/23005,
  10011/2183, …). Largely wire-driven; few static catalogues.
- **See also:** `specs/social.md` (the full social opcode catalogue + membership-state model);
  `specs/chat.md` (reclassifies `2/82`/`2/83`/`2/84`/`3/21`); `packets/2-30_guild_op.yaml`,
  `packets/2-35_party_invite.yaml`, `packets/2-36_party_leave_kick.yaml`, `packets/2-37_party_leader_op.yaml`,
  `packets/4-36_party_member_remove_result.yaml`, `packets/5-21_party_event.yaml`,
  `packets/2-23_trade_request.yaml` (the shared party/trade confirm-popup context).

---

## 6. Inventory, equipment, shop & trade

The item model is a flat **20-slot × 16-byte** array (worn gear + bag) mirrored from the player actor;
slot 15 = visual/appearance (triggers a gear rebuild), slot 8 is excluded from the worn-item stat sum.
Equip / unequip / swap rides **`2/16`**; quick-use rides `2/17`; item upgrade is a channel-then-commit
pattern (`2/50`); equip eligibility and the +N enchant level are **server data**, not a client roll.
**Player-to-player trade** is a three-packet C2S flow — `2/23` (request/accept), `2/24` (add money/item;
money is a base-1000 three-digit composite), `2/25` (confirm/lock or cancel, re-transmitting the
own-side offer manifest) — with the `4/23` phase machine driving the window. Trade capacity is **40
slots**.

| Opcode | Dir | Role |
|---|---|---|
| `2/5` / `2/16` / `2/17` / `2/50` | C2S | Item-use / equip-change / quick-equip / upgrade-commit. |
| `2/23` / `2/24` / `2/25` | C2S | Trade request-accept / slot-update / confirm-with-manifest. |
| `4/12` / `4/16` / `4/17` | S2C | Equip-item / equip-change / quick-equip acks. |
| `4/23` / `4/24` / `4/25` | S2C | Trade request-result (phase @+10) / slot-update / full finalize (≤40 items). |
| `4/50` | S2C | `SmsgUpgradeItemResult` — enchant/upgrade result (motion 8 = success, 9 = fail). |
| `5/106` | S2C | Trade-state toggle (trade-busy flag; opens/closes personal-shop UI). |
| `4/149` / `4/153` | S2C | Item-panel slot chunk / refresh (moves, splits, storage view). |

- **Driving VFS data:** `items.csv` / `items_extra.do` / `citems.scr` (item stats, attack/defence,
  attack_speed, droppable/pickable flags), `npc.scr` template +128 (shop / repair lists), `msg.xdb`
  (item / repair / trade notices: 36003, 36004–36028, 36030, 59003) — `formats/config_tables.md`,
  `structs/item.md`.
- **See also:** `specs/inventory_trade.md` (the full item/equip/shop/trade/ground-item model + §12
  ground items); `specs/combat.md` §2 (the worn-item stat sum); `specs/equipment_visuals.md`
  (how a worn item changes the rendered avatar); `packets/2-14_drop_item.yaml`,
  `packets/2-15_pickup_item.yaml`, `packets/2-23_trade_request.yaml`, `packets/2-24_trade_slot_add.yaml`,
  `packets/2-25_trade_confirm.yaml`, `packets/2-151_product_buy.yaml`, `packets/4-23_trade_request_result.yaml`.

---

## 7. Ground items (drop / pickup / world render)

Ground items are **first-class 3D world actors**, not billboards — each dropped item resolves its own
per-template model (shared fallback id **201011001** on a lookup miss), floats **+1.0 unit** above
terrain, fans overlapping drops out in a spiral, draws a name label within **1000 units**, and is
tracked in a registry ("ItemMap"). Drop (`2/14`) and pickup (`2/15`) are C2S; the client pre-validates
lock/busy/trade state, a free bag slot (capacity gate, threshold 20 — **not** a distance check), the
pickable flag, the level requirement (error `10005`), and the coin gold cap. Coins use item id
**217000501**.

| Opcode | Dir | Role |
|---|---|---|
| `2/14` | C2S | `CmsgDropItem` — drop bag/equip/staged item or coin (`mode 0xFF` = coin). |
| `2/15` | C2S | `CmsgPickupItem` — pick up a targeted ground item. |
| `4/4` tag 4 | S2C | Ground item present in the area snapshot (bulk spawn). |
| `4/14` | S2C | `SmsgGroundItemDropAck` (20 B) — spawns the dropped item at the dropper's feet. |
| `4/15` | S2C | `SmsgItemWorldPickupAck` (36 B) — inserts the item / credits coin; subtype selects the insert path. |
| `5/14` | S2C | `SmsgGroundItemSpawn` (48 B) — live "an item just dropped" push (mob/combat/coin drops). |
| `5/15` | S2C | `SmsgGroundItemRemove` (16 B) — despawn (someone picked it up). |

- **Driving VFS data:** item template models (per-template id → world mesh; fallback 201011001),
  `items.csv` (droppable/pickable flags, level requirement) — `structs/item.md`, `formats/config_tables.md`.
- **See also:** `specs/inventory_trade.md` §12 (the full ground-item lifecycle + rendering);
  `packets/2-14_drop_item.yaml`, `packets/2-15_pickup_item.yaml`, `packets/4-14_ground_item_drop_ack.yaml`,
  `packets/4-15_item_world_pickup_ack.yaml`, `packets/5-14_ground_item_spawn.yaml`,
  `packets/5-15_ground_item_remove.yaml`, `packets/4-4_ground_item_tag4.yaml`.

---

## 8. Character progression (XP, level-up, rank, stat allocation)

Progression converges on a single character/stat window rebuilt by every channel that changes
progression state. Six channels: **five S2C** — experience (`5/9`, 64-bit add-with-carry + exp-orb FX),
rank/honor XP (`5/11`, capped at rank 25), level-up (`5/32`, vitals + points + rank-XP, class-evolution
panels at levels 12/24), authoritative stat resync (`5/67`, world-entry source of truth), the
stat-allocation ack (`4/29`), and the skill-point counter (`4/150`) — and **one C2S**: the
stat-allocation commit (`2/29`, **five absolute u32 stats**, wire order STR/INT/AGI/DEX/CON). The stat
editor models a **pending-delta** allocation entirely client-side until "Apply"; the body is absolute
(server applies a snapshot), and the `4/29` echo + the `5/67` resync reconcile any drift.

| Opcode | Dir | Role |
|---|---|---|
| `5/9` | S2C | `ExpGain` (32 B) — adds experience; `+xp` floating text + exp-orb FX. |
| `5/11` | S2C | `RankXpGain` (20 B) — separate rank/honor channel. |
| `5/32` | S2C | `SmsgLevelUp` (48 B) — level + vitals + points + rank-XP tail; level-up FX/sound. |
| `5/67` | S2C | `StatsUpdate` (36 B) — authoritative five-stat + current-XP resync (world entry). |
| `4/29` | S2C | `SmsgStatUpdate` (36 B) — stat-allocation ack (five echoed stats + remaining points). |
| `4/150` | S2C | `SkillPointUpdate` — skill-point total / skill level-up notice. |
| `2/29` | C2S | `CmsgStatAllocate` (20 B) — five absolute u32 stats, order STR/INT/AGI/DEX/CON. |

- **Driving VFS data:** `exp.scr` (XP per level), `userlevel.scr` (base stats per level),
  `userpoint.scr` (allocation curve), `users.scr` (per-class stat grid), rank-XP table; `msg.xdb`
  (rank titles 22007–22056, karma 30001–30004, floating XP text 10085/10086) — `formats/config_tables.md`.
- **See also:** `specs/progression.md` (the six channels + the pending-delta editor);
  `specs/combat.md` (the derived combat-stat mirror the same window refreshes); `specs/skills.md`
  (the shared skill-point counter); `packets/5-32_level_up.yaml`, `packets/4-29_stat_update.yaml`,
  `packets/5-9_exp_gain.yaml`, `packets/5-11_rank_xp_gain.yaml`, `packets/5-67_stats_update.yaml`,
  `packets/2-29_stat_allocate.yaml`, `packets/4-150_skill_point_update.yaml`.

---

## 9. Buffs & state bar

Buff/aura stacking is **entirely server-driven**: one push rebuilds the whole skill/state window and
the buff bar together. `4/102` carries a fixed **476-byte** snapshot — a 116-byte player stat block
followed by **30 fixed 12-byte active-buff records** (id + two coords/params) starting at payload +116;
the handler clears all 30 slots and re-shows only the active ones. A separate buff-slot push (`5/31`)
updates the buff/aura table that feeds the per-stat buff terms of the combat aggregate and the
crowd-control status family (stun / root / silence / freeze) that gates the can-move-or-act check.

| Opcode | Dir | Role |
|---|---|---|
| `4/102` | S2C | `SmsgSkillWindowStateUpdate` — 476-byte skill/state-window + buff-bar snapshot (30 × 12-byte buff records @+116). |
| `5/31` | S2C | `SmsgBuffSlotUpdate` — buff/aura table update (per-stat buff terms; CC status slots). |

- **Driving VFS data:** buff-icon placement (`data/script/buff_icon_position.xdb`) and the buff/skill
  catalogues — `formats/config_tables.md` (`skills.scr`, `skillneedset.scr`), `formats/effects.md`
  (buff/aura FX descriptors).
- **See also:** `specs/skills.md` §6 (the buff slot model + CC status ids); `specs/combat.md` §2
  (buff contributions to the derived combat aggregate); `packets/4-102_buff_state.yaml`.

---

## 10. Minimap & world map

Two independent map surfaces share only the widget toolkit. The **HUD radar** is a 133×133 body with
the local player dead-centre; its projection is the byte-verified `px = relX × 0.125 + 66.5`,
`py = relZ × 0.125 + 66.5` (exactly 1:8), culling blips outside `[0,133]`. Its per-cell streamed
background (`data/effect/map/d*x*z*.bmp`) **does not ship** in the VFS, so a faithful radar renders
blips over a generated/plain background. The **full-screen world map** (`b` key) draws a per-area
texture plus **static** landmark pins from a data table and a GPS `D°M′S″` readout. The radar consumes
only client-side actor positions (each actor's last-packet world position) — **no map-specific packet
exists**.

| Opcode | Dir | Role |
|---|---|---|
| (none) | — | The map UIs consume only client-side actor state (last-known position from each actor's most recent movement update); no dedicated map opcode was found. Position source = `2/13` / `5/13` movement, owned by `opcodes.md`. |

- **Driving VFS data:** `mapsetting.scr` (84-byte zone bounding-box + fog records, 52 zones; **zone
  names live here, not in `msg.xdb`**), `regiontableNNN.bin` (32-byte sub-zone label records),
  `data/ui/map/map1.dds`, `data/ui/broodwarmap.dds`, `data/ui/direction.dds`,
  `data/ui/map_userpoint.tga` — `specs/minimap.md` §6.
- **See also:** `specs/minimap.md` (the projection, tile-streaming, blip table, full-screen map, and
  the SAMPLE-VERIFIED VFS reality); `formats/terrain.md` (the 1024-unit cell convention);
  `specs/environment.md` (fog).

---

## 11. Equipment visuals (worn gear → rendered avatar)

Equipping does **not** swap the body. The avatar is a **per-part mesh recomposition** under **one
shared skeleton**: on any equip change the visual is torn down (all part nodes destroyed) and a fixed
set of part slots (`{2,3,4,6,11,14}`; slot 14 = weapon; local player adds the weapon attach) is rebound
from the visual part table (Visual +204, 16-byte records). Weapon meshes attach to a hand bone; a
two-piece weapon (skin bind class 3) builds main + off-hand nodes. A **weapon enchant-aura glow** is
driven by the item-actor `weapon_effect_grade` (+231): stored `101..109` normalize to tiers `1..9`,
`0` = none. Two equip-result messages drive the rebuild.

| Opcode | Dir | Role |
|---|---|---|
| `4/12` | S2C | `SmsgEquipItemResult` (16 B) — writes the slot, runs the local-player part rebuild. |
| `4/16` | S2C | `SmsgEquipChangeResult` (20 B) — slot change; unless skip-visual, applies the visual refresh + other-actor rebuild. |

- **Driving VFS data:** `skin.txt`, `class.txt`, the animation-catalog GID→skin map, item-actor mesh
  pointers, weapon-glow loc-strings 57019–57024 — `specs/skinning.md`, `formats/mesh.md`,
  `structs/item.md`.
- **See also:** `specs/equipment_visuals.md` (the recomposition model, GID derivation, weapon attach,
  enchant aura); `specs/skinning.md` (bone addressing); `specs/effects.md` §12 (the separate
  sword-light weapon trail — not the enchant aura); `specs/inventory_trade.md` §4 (the equip wire path).

---

## 12. Effects (skill-cast FX, hit bursts, attachments)

The effect runtime is a class family (`UserXEffect` / `JointXEffect` / `MapXEffect`) booted from eight
manifest files in fixed order, with three object pools and a lazy-parse `.xeff` descriptor cache. The
**skill-cast effect chain** is driven by the same `5/52` `SmsgActorSkillAction` message as combat: its
**action-code byte** selects cast-enable vs cast-disable (a looping cast-channel effect that soft-stops
on disable), and AoE action code `0xCC` fans sub-actor effects in a ring. The **hit-burst** leg fires
on the `AttackEffect` message. Floating damage numbers and the per-target hit-FX queue are the combat
result presentation (Chapter 1). All effect ids are exact decimals; trigger sites map to network
handlers.

| Opcode | Dir | Role |
|---|---|---|
| `5/52` | S2C | `SmsgActorSkillAction` — drives the cast-channel effect via its action-code byte (enable/disable; `0xCC` AoE ring). |
| (AttackEffect) | S2C | Post-contact hit-burst MapXEffect (template inner-event 1001); see effects §7. |
| `5/14` | S2C | Combat-effect-instance spawn (shares the ground-item-spawn wire shape). |

- **Driving VFS data:** `data/effect/` manifests — `bmplist.lst`, `xobj.lst`, `xeffect.lst`,
  `totalmugong.txt`, `itemjointeff.txt`, `mobjointeff.txt`, `itemswordlight.txt`, `mobswordlight.txt`;
  the `.xeff` / `.eff` / `.xobj` descriptors — `formats/effects.md`.
- **See also:** `specs/effects.md` (the full runtime model, boot pipeline, trigger table, skill-cast
  chain §15); `specs/skills.md` §5 (action-code map); `specs/combat.md` §12 (floating numbers);
  `packets/5-52_actor_skill_action.yaml`.

---

## 13. UI system (the toolkit every World-scene window is built on)

Every World-scene panel (chat, inventory, NPC service panels, quest log, trade, stat window, minimap,
buff bar) is a window in a **custom retained-mode widget toolkit** (the `Diamond::GU*` family). All
layouts are **hardcoded** in per-screen build routines (literal pixel coordinates on a 1024×768
reference canvas); there is no external layout file. Each widget is an atlas sub-rect blit with an
integer `actionId` fired on click-release through the window's command dispatcher. **UI captions** are
fetched by numeric id from `msg.xdb` (CP949); text is GPU-side Korean system fonts (`HANGUL_CHARSET`),
not a shipped glyph atlas. The master scene machine has 9 states; in-game (state 5) returns to
character-select (state 4).

### 13.1 The GameState-5 in-game roster (how the World scene is built) — CONFIRMED

The program entry point is a single **`while(1) switch(GameState)`** scene machine over **states
0..8** (the ~9 states above). The VFS is mounted once before the loop; each state builds (and on
exit tears down) its scene. The dispatcher selects the case whose **index equals the stored game-state value**, and each case **pre-writes the next value on ENTRY** before building its scene. **CharSelect is switch case-index 4 and writes value 5 on entry**; **the World/in-game scene is switch case-index 5 and writes value 4 on entry** (the no-network default = return to character-select). So 'state 5 = in-game' is loosely 'value 5 selects case-index 5' — but case-index 5 also leaves value 4 behind, so on a normal scene-loop exit the machine re-enters case-index 4 (character-select). The value-4 write happens on ENTRY (pre-loop), not on teardown. An explicit leave-world/logout overrides this default with the exit value (the logout path writes the quit-prep value, so a deliberate logout exits the client and never returns to character-select). States **6** (quit-prep → exit), **7** (error → exit), and **8** (exit) close the machine. This is the same entry-point state machine the front-end specs
describe — see `specs/game_loop.md` / `specs/client_runtime.md`; this chapter records only the
in-game (state-5) members. [CONFIRMED]

The in-game scene is built from a small set of cooperating objects:

| Member | Size | Role | Confidence |
|---|---|---|---|
| **HUD host window** (the in-game `MainWindow`) | 6280-byte object | The retained-mode root that hosts every World-scene panel in Chapters 1–12 (chat, inventory, stat window, minimap, buff bar, …). Built in the char-select→in-game transition; the buff/state window is one of its children (the `4/102` rebuild target — see §9, §13.2). | CONFIRMED |
| **In-game handler** (`MainHandler`) | 200-byte object | Constructed last in the scene sequence and attached to the HUD host; drives the in-game frame logic. | CONFIRMED |
| **In-game scene-graph builder** | — | A separate routine that allocates the 3D scene: a perspective camera (**FOV 65°, near 5, far 15000**), **5 view-platform objects** (consistent with the 5 camera view modes — Chapter 15 / `specs/camera_movement.md`), a GScene root, the terrain-manager singleton, and **5 layer nodes captioned by message-table ids 2006 / 2004 / 2005 / 2148 / 2148** (five distinct node objects; id 2148 is reused for the last two, so the node COUNT is 5 while the DISTINCT-id count is 4 — corrects the earlier '4 layer nodes' reading). | CONFIRMED (count + constants); view-mode semantics owned by `camera_movement.md` |
| **Local-player actor singleton** | pointer | The hub the Actor family (visual refresh, motion, buff release) reads; destroyed on scene exit. The combat / movement / chat / buff chapters all converge on this actor. | CONFIRMED (local-player/Actor singleton) |
| **Skill/hotbar state hub** | global | The table the HUD skill/hotbar panels (skill-panel toggle, combo / link panels) read; consistent with the one 240×8-byte hotbar record array (id @+0, points @+4 — Tier-1 fact). | CONFIRMED (HUD skill-panel hub) |

### 13.2 The World HUD event hub (the synthesis headline) — CONFIRMED

The in-game HUD host (§13.1) owns **one shared chat-log / notice sink** that S2C handlers **and**
local system events both funnel into: the disconnect / connection-state path (§0.2), the region
type-2 movement-gate denial (§16.3, localized message id 74309), and the chat / coloured-system-text
paths (§2) all fetch the in-game handler singleton and **broadcast into this same sink**. This is the
"World HUD event hub" the chapters above keep referring back to: routing fans **in** to one notice
log regardless of source. The buff/state window is a **child of the HUD host** that the `4/102`
handler reaches by fixed child index, **clears, then rebuilds** from the 476-byte snapshot (§9).
[CONFIRMED]

| Opcode | Dir | Role |
|---|---|---|
| (none) | — | The UI toolkit is local presentation; it carries no opcode of its own. It hosts the windows that emit/consume every opcode in Chapters 1–12 (action-id → window dispatcher → the relevant C2S send). |

- **Driving VFS data:** `uitex.txt`, `skillicon.txt`, `crestlist.txt`, `texturelist.txt` (UI texture /
  icon manifests), per-class stance `.do` files (e.g. `musajung.do`), `msg.xdb` (all captions) —
  `formats/ui_manifests.md`, `formats/config_tables.md`.
- **See also:** `specs/ui_system.md` (widget hierarchy, render path, input/capture, per-screen
  layouts, scene state machine); `formats/ui_manifests.md` (the UI texture-id resolver); the
  `specs/frontend_scenes.md` / `specs/client_workflow.md` front-end counterparts.

### 13.3 The `4/1` world-entry seed tables — roster, scene-entity slots, hotbar — CONFIRMED

The in-game scene's three big in-memory tables are seeded in one shot by the `4/1` world-entry form
(`specs/world_entry.md §2.3a`; byte offsets in `packets/4-1_game_state_tick.yaml`). Internal strides
were recovered from the form's own consumers (two stale-slot sweeps + the verbatim hotbar copy),
control-flow-confirmed and counter-confirmed on build 263bd994 (CYCLE 12 / Phase 3):

| Seed table | Stride / shape | Role | Confidence |
|---|---|---|---|
| **Roster table (4/1 table A)** | 16-byte records (cap 193; 120 swept); each = an actor id + a keep-guard (eviction gate, also the displayed member number) + an aux value | The membership/roster panel slot source (the GameState-5 in-game roster of §13.1). | CONFIRMED (stride + evict predicate) |
| **Scene-entity table (4/1 table B)** | heterogeneous: 240 × 16-byte actor-slot records (same shape as table A) + a small gap + a 21 × 8-byte category-entry list (category code + value) + a 16-byte world-target selection record | The scene's tracked-entity / actor-slot table; party & spawn-group queries read it. | CONFIRMED (stride + sub-region arithmetic 3840+20+168+16=4044) |
| **Hotbar (4/1 hotbar block)** | 240 × 8-byte slots; each = an entry key (0 = empty) + a count; **no inline type byte** | The quick-slot bar restored on entry — the same 240×8 hotbar record array §13.1 names as the skill/hotbar state hub. Skill-vs-item is resolved by a skill-catalogue lookup (catalogue category 5 = skill). | CONFIRMED (stride + catalogue discriminator) |

The shared eviction predicate for the two 16-byte tables: a slot is cleared only when its id is
non-zero, its referenced actor resolves, that actor's stale flag is set, and its keep-guard is 0.
Category codes, hotbar non-skill family meanings, and keep-guard values beyond the membership display
are data-driven VALUE details (capture-pending) — they do not block the table layouts.

## 14. Consolidated opcode table (all World-scene rows)

Direction is from the **client's** point of view (`C2S` = client→server, `S2C` = server→client). Sizes
are payload byte counts where pinned (`var` = not a fixed size; `+text` = a length-prefixed CP949
tail). **Routing and sizes are CODE-CONFIRMED; every body field is CAPTURE-UNVERIFIED** (Status block).

### 14.1 Client → server (intent)

| Opcode | Dir | Size | Name | System (chapter) |
|---|---|---|---|---|
| `2/7`  | C2S | 19 + text | `CmsgChat` (whisper-named) | Chat (2) |
| `2/13` | C2S | 16 | `CmsgMoveRequest` | Combat approach loop (1) |
| `2/14` | C2S | 8  | `CmsgDropItem` | Ground items (7) |
| `2/15` | C2S | 12 | `CmsgPickupItem` | Ground items (7) |
| `2/16` | C2S | 12 | `CmsgEquipChange` | Inventory/equip (6) |
| `2/17` | C2S | 8  | `CmsgQuickEquipSlotSet` | Inventory (6) |
| `2/19` | C2S | 12 | `CmsgNpcInteractOpen` | NPC interaction (3) |
| `2/20` | C2S | 12 | `CmsgNpcSellItem` | NPC interaction (3) |
| `2/23` | C2S | 8  | `CmsgTradeRequest` | Trade (6) |
| `2/24` | C2S | 20 | `CmsgTradeSlotUpdate` | Trade (6) |
| `2/25` | C2S | 2 + 4×N | `CmsgTradeConfirm` | Trade (6) |
| `2/28` | C2S | var | `CmsgQuestAction` | Quests (4) |
| `2/29` | C2S | 20 | `CmsgStatAllocate` | Progression (8) |
| `2/30` | C2S | 8  | `CmsgGuildAction` | Social/guild (5) |
| `2/35` | C2S | 8  | `CmsgPartyInvite` | Social/party (5) |
| `2/36` | C2S | 8  | `CmsgPartyLeaveOrKick` | Social/party (5) |
| `2/37` | C2S | 8  | `CmsgPartyLeaderOp` | Social/party (5) |
| `2/50` | C2S | 16 | `CmsgUpgradeItemCommit` | Inventory/upgrade (6) |
| `2/52` | C2S | var | `CmsgUseSkill` (slot `0xFF` = basic melee) | Combat (1) |
| `2/100`| C2S | 1  | NPC "request open" | NPC interaction (3) |
| `2/110`| C2S | 4  | Quest-NPC step | NPC interaction (3) |
| `2/113`| C2S | 8* | Repair commit | NPC interaction (3) |
| `2/115`| C2S | 8* | Shop / product buy commit | NPC interaction (3) |
| `2/122`/`2/123`/`2/128` | C2S | 12/12/4 | Friend / relation submits | Social (5) |
| `2/142`| C2S | 16 | Storage deposit / withdraw | NPC interaction (3) |
| `2/143`| C2S | 4  | Quest-item-keep open | NPC interaction (3) |
| `2/151`/`2/152`/`2/153` | C2S | 8* | ProductPanel buy / page / action | NPC interaction (3) |

### 14.2 Server → client (push / ack)

| Opcode | Dir | Size | Name | System (chapter) |
|---|---|---|---|---|
| `3/8`  | S2C | var | `SmsgShopPageUpdate` | NPC interaction (3) |
| `3/50000` | S2C | var | `SmsgGmChatMessage` | Chat (2) |
| `4/2`  | S2C | var | per-tick resync pulse | Combat cadence (1) |
| `4/12` | S2C | 16 | `SmsgEquipItemResult` | Equipment visuals (11) |
| `4/16` | S2C | 20 | `SmsgEquipChangeResult` | Equipment visuals (11) |
| `4/19`/`4/20`/`4/21` | S2C | var | NPC buy / sell / slot-clear acks | NPC interaction (3) |
| `4/23` | S2C | 20 | `SmsgUserTradeRequestResult` (phase @+10) | Trade (6) |
| `4/24`/`4/25` | S2C | var | Trade slot-update / finalize | Trade (6) |
| `4/29` | S2C | 36 | `SmsgStatUpdate` (allocation ack) | Progression (8) |
| `4/35`/`4/36`/`4/37`/`4/76` | S2C | 56/56/56/var | Party invite-state / remove / leader / accept | Social/party (5) |
| `4/50` | S2C | 32 | `SmsgUpgradeItemResult` | Inventory/upgrade (6) |
| `4/62`/`4/65` | S2C | 80/1812 | Guild invite-join / full sync | Social/guild (5) |
| `4/99` | S2C | var | `SmsgCubeGambleResultMessage` | Cube-Gamble minigame (1) |
| `4/100`| S2C | var | `SmsgCubeGambleReelUpdate` | Cube-Gamble minigame (1) |
| `4/102`| S2C | 476 | `SmsgSkillWindowStateUpdate` (buff/state bar) | Buffs (9) |
| `4/140`| S2C | var | `SmsgColoredSystemText` | Chat (2) |
| `4/149`/`4/153` | S2C | var | Item-panel slot chunk / refresh | Inventory (6) |
| `4/150`| S2C | var | `SkillPointUpdate` | Progression (8) |
| `4/4` tag 4 | S2C | 24 (in stream) | Ground item in area snapshot | Ground items (7) |
| `4/14` | S2C | 20 | `SmsgGroundItemDropAck` | Ground items (7) |
| `4/15` | S2C | 36 | `SmsgItemWorldPickupAck` | Ground items (7) |
| `5/7`  | S2C | 36 + text | `SmsgChatBroadcast` | Chat (2) |
| `5/9`  | S2C | 32 | `ExpGain` | Progression (8) |
| `5/11` | S2C | 20 | `RankXpGain` | Progression (8) |
| `5/14` | S2C | 48 | `SmsgGroundItemSpawn` / combat-effect spawn | Ground items (7) / Effects (12) |
| `5/15` | S2C | 16 | `SmsgGroundItemRemove` | Ground items (7) |
| `5/21`/`5/38`/`5/76` | S2C | 12/100/36 | Party roster / member-stats / joined | Social/party (5) |
| `5/26` | S2C | 28 | `SmsgLocalPlayerRelationSlot` | Social/relation (5) |
| `5/31` | S2C | var | `SmsgBuffSlotUpdate` | Buffs (9) |
| `5/32` | S2C | 48 | `SmsgLevelUp` | Progression (8) |
| `5/52` | S2C | var | `SmsgActorSkillAction` | Combat (1) / Effects (12) |
| `5/53` | S2C | var | `SmsgActorVitalsAndPairState` | Combat (1) |
| `5/65` | S2C | 32 | `SmsgGuildMemberRosterUpdate` | Social/guild (5) |
| `5/67` | S2C | 36 | `StatsUpdate` (resync) | Progression (8) |
| `5/68` | S2C | 452 | `SmsgQuestList` | Quests (4) |
| `5/73` | S2C | 344 | `SmsgQuestComplete` | Quests (4) |
| `5/106`| S2C | 12 | Trade-state toggle | Trade (6) |

> Sizes marked `*` are inferred from the send-builder's write-length argument and not re-confirmed
> from the dispatch table; `12` / `16` / `8` / `4` / `1` are exact where stated. A "(2nd C2S)" quest
> list-row request exists (Chapter 4) with an **untraced** opcode tuple. The authoritative per-opcode
> rows (with the `0x` packed encoding and packet-YAML links) live in `opcodes.md`; this table is a
> navigation aid grouped by system.

---

## 15. Cross-reference map

| System / detail spec | What it owns |
|---|---|
| `specs/combat.md` | Combat math, the in-world loop, target acquisition, cadence, floating numbers. |
| `specs/skills.md` | Skill cast pipeline, cooldown slots, AoE shapes, the buff slot model, action-code map. |
| `specs/chat.md` | Chat input/log/bubble components; the channel model; the `2/7` / `5/7` framing. |
| `specs/social.md` | Party / guild / friend-relation wire protocol and membership state. |
| `specs/npc_interaction.md` | The KIND→panel router, the panel inventory, the NPC-lane C2S send map. |
| `specs/quests.md` | Quest action `2/28`, the two pushes, the three-tab quest log, the two text sources. |
| `specs/inventory_trade.md` | The 20-slot item model, equip/quick-use/upgrade, trade, ground items. |
| `specs/progression.md` | XP / rank-XP / level-up / stat resync / stat allocation. |
| `specs/equipment_visuals.md` | Per-part avatar recomposition, weapon attach, enchant aura. |
| `specs/effects.md` | Effect runtime, boot pipeline, trigger table, skill-cast effect chain. |
| `specs/minimap.md` | HUD radar projection, tile streaming, blip table; full-screen world map. |
| `specs/ui_system.md` | The `Diamond::GU*` widget toolkit hosting every World-scene window. |
| `world_systems.md` Ch. 16 | The 256-unit region grid, the **zone-type enum (safe/PvP/closed), CONFIRMED-COMPLETE at 3 values**, the `regiontable` record (zoneName + zoneType + tail), the PvP/movement gates, the **server-authoritative quest/event verdict**, the gather-anchor table, and `.mud` as a per-cell audio-zone grid. |
| `world_systems.md` Ch. 17 | **Per-area background-music selection** — the five per-map sound tables, the `.mud +2` music-zone byte driver, the trade/exchange-busy BGM override (§17.2 — formerly mislabelled "indoor override"), and the cross-fade play path. |
| `world_systems.md` Ch. 18 | **Movement, targeting & pathfinding** — the `2/13` movement-intent emitter (16-byte body, event-driven), the two-click targeting model (`2/12` engage + `2/79` confirm, locked target stored on the actor), the no-navmesh straight-line + reactive-`.sod`/`.ted` walk model, and the server reconciliation push `5/13` with its catch-up / teleport distance bands. |
| `world_systems.md` Ch. 19 | **Death & respawn (concise)** — the death actor-state fields, the `5/10` death push + cause codes, the level-gated local death modal, the region-gated respawn countdown timer, and the `2/3` respawn-choice request with its `4/28` / `5/28` responses. World-exit detail (death does NOT leave the world) is owned by `world_exit.md`. |
| `world_systems.md` Ch. 20 | **Dark / absent subsystems** — mounts, auction house, instanced dungeons, housing, and arena/ladder, each recorded as **confirmed-absent** by static exhaustion (so engineers never build a phantom system). |
| `specs/world_exit.md` | The world-exit / logout flow; also owns the death-vs-exit distinction (death does not leave the world; the dead combat sub-mode). Ch. 19 here records the death actor-field facts and cross-refs there. |
| `formats/region_grid.md` | The `region<area>.bin` grid + the `regiontable<area>.bin` record (zoneName/zoneType/tail) byte layouts that Ch. 16 reads. |
| `formats/config_tables.md` | `.scr` / `.do` / `.csv` catalogues (items, skills, NPCs, stat curves, quests, `msg.xdb`). |
| `formats/misc_data.md` | `.xdb` / `.mi` / `.tol` / `.ion` / `.sc` small data files (incl. `actor_size.xdb`). |
| `formats/effects.md` | `.xeff` / `.eff` / `.xobj` descriptors and the effect-manifest list files. |
| `formats/ui_manifests.md` | `uitex.txt` / `skillicon.txt` / `crestlist.txt` / `texturelist.txt` UI manifests. |
| `opcodes.md` | The authoritative `(major:minor)` catalogue (the consolidated table above is a per-system index of it). |
| `packets/*.yaml` | Per-opcode wire field specs (linked per chapter). |

---

## 16. Regions & zones (PvP / safe / closed gating) — Campaign 5 / 5B

> **Campaign-5 addition, Campaign-5B upgrade** (Lane 3/4 — Regions & Triggers). This chapter
> documents the third world grid scale — the **256-unit region grid** — and the **zone-type enum**
> it indexes, which feed the same server-authoritative world-state tick and combat machinery the
> chapters above describe. The world is gridded at **three scales**: 1024-unit **terrain cells**
> (`formats/terrain.md`), 256-unit **region cells** (this chapter), and 16-unit **sub-tiles** (the
> `.mud` per-cell audio grid below). Engine code citing any constant here writes
> `// spec: Docs/RE/specs/world_systems.md` (Ch. 16). The byte layouts of `region<area>.bin` and
> `regiontable<area>.bin` are owned by `Docs/RE/formats/region_grid.md`.

The client keeps a **per-area region grid** that quantizes the world into 256-unit cells, and a
parallel **region-properties table** that maps each region id to a small **zone-type enum**. Combat
permission, movement barriers, and the world-marker overlay are driven off these two structures, but
the **authoritative active region** comes from the **network layer** (server-pushed into the
world-state tick), not from the local grid — the local grid is consulted to classify *target*
positions and to gate the player's own intended moves. This is consistent with the chapter-0 model:
server-authoritative resolution, client-side gating for presentation and to avoid pointless sends.

### 16.1 The region grid (per-area, `region<area>.bin`)

The per-area region file is loaded once when the active map area is set, alongside the area's other
binary tables (the per-area map header, the region-properties table, and the gather/anchor table).
Its on-disk shape is a **flat byte grid plus dimensions and a world origin**:

| Element | Size / form | Meaning | Confidence |
|---|---|---|---|
| grid width | 4-byte unsigned integer | number of columns | CONFIRMED |
| grid height | 4-byte unsigned integer | number of rows | CONFIRMED |
| grid buffer | width x height bytes, **1 byte per cell** | each byte = a **region id** (0..31) | CONFIRMED |
| world-X origin | 4-byte unsigned integer | subtracted before the 256-unit quantize | CONFIRMED |
| world-Z origin | 4-byte unsigned integer | subtracted before the 256-unit quantize | CONFIRMED |

Read order in the file is: width, height, then `width x height` cell bytes, then the X origin, then
the Z origin. **Each grid cell is one byte holding a region id (0..31)** — it is *not* a flag byte.
The flags/semantics live in the region-properties table (16.2), indexed by that id. Full byte layout
in `formats/region_grid.md` (Layout A).

**World position to region id** (the lookup every gating site uses):

```
col   = (worldX - originX) / 256          # integer cell column
row   = (worldZ - originZ) / 256          # integer cell row
index = col + row * width
region_id = gridBuffer[index]             # unsigned byte, 0..31
```

- **Cell size = 256 world units per axis** (a finer quantization layered over the 1024-unit terrain
  cell; 256 = a quarter of a terrain cell). [CONFIRMED]
- If the grid is not loaded, or `index >= width x height`, the lookup is treated as a failure and
  yields region id 0. [CONFIRMED]
- Cell math is integer division; world-coordinate handling follows the project's standard convention.

### 16.2 The region-properties (zone) table (`regiontable<area>.bin`)

A fixed **32 records x 48 bytes = 1536 bytes** table, indexed directly by region id (0..31). A region
id >= 32 has no record and is treated as the default. [CONFIRMED]

| Offset | Size | Type | Field | Notes | Confidence |
|---:|---:|---|---|---|---|
| +0  | 40 | char[40] | **`zoneName`** | NUL-terminated zone display-name string — the **minimap sub-zone caption**. Three minimap/HUD label sites read the record base pointer directly as a C-string from offset +0. This is NOT opaque/unread; it is the zone-name field. | HIGH (consumed as `char*` by 3 UI sites; the 40-byte width is parser-derived from `48 − 4 − 4`, not sample-confirmed) |
| +40 | 4  | u32 | **`zoneType`** | The only numeric field consumed in region logic; an **enum**, not a bitmask. | CONFIRMED |
| +44 | 4  | (opaque) | `_tail` | Remainder of the 48-byte stride; **no reader found** in any path. | UNVERIFIED |

- **Record count:** fixed 32. **Record stride:** 48 bytes. **Index source:** the region id byte from
  the grid (16.1) or the network-pushed active region id (16.3). [CONFIRMED]
- **One file, two roles — unified.** This is the same `regiontable<area>.bin` referenced as the
  minimap sub-zone **label** source in Chapter 10. The **`zoneName` string at +0** is the label; the
  **`zoneType` word at +40** is the region-gating field. They are the two named fields of one 48-byte
  record, not two unrelated reads. The earlier "`+0..+39` opaque/unread" description is **superseded**.
  Byte layout owned by `formats/region_grid.md`.

### 16.3 Zone-type enum (record offset +40) — CONFIRMED-COMPLETE at three values

The zone type is a small **enumerated value**, **never a packed bitmask** — every consuming site does
an **equality / inequality / truthiness compare** (`== 1`, `!= 1`, `== 2`, `!= 0`), never a bit test.
A census of every site that reads the +40 word (movement gate, combat-mode arbiter, attack/target-
validity gates, skill-use validator, the action gates, and the minimap) found **no comparison against
any value ≥ 3 and no bitmask test anywhere**. The richest consumer — the minimap renderer — does a
`switch` on the +40 word with arms for **0, 1, 2, and a default only** (no case for 3..31). A missing
record (region id >= 32) is treated by the combat arbiter as type `1`.

The set of values the client distinguishes is therefore **exactly `{0, 1, 2}`**; values `3..31` fall
through every site to the default and are treated like the safe (type-0) case.

| Value | Meaning | Confidence |
|---|---|---|
| `0`  | **Safe / no-combat** zone — combat arbiter yields the "denied" result; the minimap renders this as a distinct safe caption/colour. | CONFIRMED (behaviour); label PLAUSIBLE |
| `1`  | **Open PvP / combat-enabled** zone — combat is permitted (subject to the faction check). | CONFIRMED (1 = combat-permitted) |
| `2`  | **Movement-restricted / closed** zone — entry/movement into a type-2 cell is **denied** (the move is rejected, a localized message is shown, and the actor is snapped back). Also a distinct non-open combat mode. | CONFIRMED (2 = movement-restricted) |
| `3..31` | **Not modelled.** No site compares against any value ≥ 3; all such ids behave as the default (safe). | CONFIRMED-COMPLETE (3..31 = default) |

> **Residual doubt removed.** The earlier "`3+` not observed / UNVERIFIED" entry is replaced by the
> exhaustive census result: the enum is **complete at three values**. A faithful port models exactly
> `{0 safe, 1 open-PvP, 2 closed}` and treats anything else as safe.

**Where the enum is consumed:**

- **Active region is server-authoritative.** The player's current region id is a separate global
  written **only** by an area/state setter that is called **solely from two network handlers** (the
  local-player status push and the per-tick game-state push). It is *not* read from the local grid for
  the player's own position. The local grid (16.1) is used to classify a **target actor's** position.
  [CONFIRMED]
- **Combat / PvP arbiter.** Reads the player's self-region type and the target region's type (each
  defaulting to `1` if the record is missing), then resolves a combat-mode result: if either side is
  type `1`, combat is the open/permitted mode; if both sides are non-safe (non-zero) it is a distinct
  restricted combat mode; otherwise it is the safe/denied mode. The target region is computed from the
  target actor's world XZ via the 16.1 lookup. [CONFIRMED]
- **Attack / target validity gate.** Refuses the action unless **both** the active region and the
  target's region are type `1`, and additionally cross-checks a faction/team value — i.e. flagged PvP
  is allowed only in type-1 zones, subject to faction. [CONFIRMED]
- **Skill-use validator and action gates.** A family of cast/action permission sites each test the
  active region against `== 1` / `!= 1` / `== 2`, refusing with a localized message when the zone does
  not permit the action. [CONFIRMED]
- **Movement gate.** If the destination cell's region type is `2`, the move is denied: a localized
  message is shown (message id 74309) and the actor is snapped back; otherwise the normal facing /
  move proceeds and the client emits the movement intent. [CONFIRMED] On build `263bd994` the
  per-move gate and the combat-mode selection are observed in the **same routine** (the move-gate
  body both rejects type-2 destinations and selects the combat mode), so the "movement gate" and
  "combat/PvP arbiter" described separately here may be **one fused site** plus its callers rather
  than two independent functions — a structural-naming detail, not a behavioural change. The
  type-2 deny + msg 74309 + snap-back behaviour is exactly what that routine does. [CONFIRMED]
- **Minimap overlay.** The minimap reads the active region's +40 word and switches `{0,1,2,default}`
  to pick the caption text and colour (the three captions are localized zone-type labels; the default
  is an unlabeled white zone). It separately prints the `zoneName` string (16.2) as the sub-zone
  caption. [CONFIRMED]

### 16.4 Quest / event triggers are server-authoritative — CONFIRMED

No region grid cell and no region-table record carries a quest or event trigger id in **any** client
path. **Enter-region quest / event / script / PK-rule dispatch is server-authoritative** — the active
region id itself is server-pushed (16.3), and no client path keys a quest, event, script, or PK-rule
trigger off the region id or any `regiontable` field. [CONFIRMED — upgraded from PLAUSIBLE]

The **only** region-id-keyed client-side uses are **cosmetic / presentation**, never a quest dispatch:

- a specific region's **ambient sound id** (a "you are in region N" jingle played once on entry),
- a region-keyed **UI icon index**, and
- a **cinematic event-camera curve selector** (the cutscene camera path is chosen by region id) —
  a presentation effect, not a script entrypoint.

The localized strings that DO fire on region interaction (the type-2 movement block; the minimap
zone-type captions; the action-gate refusals) are all **gating / UI** messages, not quest/event
triggers. There is **no** "you entered zone X" quest notice, **no** client PK-rule toggle, and **no**
client script entrypoint keyed off a region id.

- **Quest/event scripts** load from **global text scripts at startup** (the quest and event `.scr`
  scripts), pulled by the bulk asset-preload thread — area-independent, not per-cell.
- **Gather/anchor table** (`gathertable<area>.bin`, **28-byte records**) holds **named world-anchor
  markers** (gather nodes / quest-NPC anchors). Each record's leading 2 bytes are an id key; a record
  is found by **lookup-by-id**, yielding a world position + display name that the world-state tick
  turns into on-screen labeled markers. This is a static-anchor lookup, **not** an on-enter trigger.
- Whether a **server-side** script keys off a region id at parse time is, by construction, not
  observable in the client binary — out of scope, and does not affect the client-side verdict.

### 16.5 `.mud` is a per-cell AUDIO-ZONE grid — not region, not collision

`.mud` is a **per-terrain-cell** blob (loaded per cell with the cell's `.map` scene and a `.gad`
stub), used for **per-cell audio zoning** — music, looped ambience, and event SFX, plus footstep
surface selection. It is **distinct** from the region/PvP system (`region<area>.bin` +
`regiontable<area>.bin`) and from collision/walkability (`.sod` 2D wall segments + `.ted` ground
height). [CONFIRMED]

| `.mud` fact | Value / verdict | Confidence |
|---|---|---|
| file scope | per terrain cell (loaded with `.gad` / `.map`) | CONFIRMED |
| size | fixed 32 KiB (32768 bytes) | CONFIRMED |
| grid | 64 x 64 tiles, **8 bytes/tile**, **16 world units/tile** (1024 / 64) | CONFIRMED (geometry) |
| purpose | **per-cell audio zoning**: music + looped ambient + event SFX + footstep surface | CONFIRMED (the music/ambient/event byte roles below) |
| region / PvP? | NO — that is `region.bin` + `regiontable` | CONFIRMED |
| collision / walkability? | NO — that is `.sod` / `.ted` | CONFIRMED |
| music? | NO via region — music is driven by the `.mud` per-cell **music-zone byte (+2)**, not the region grid (see Ch. 17) | CONFIRMED |

**Audio-byte roles within the 8-byte tile** (the per-cell audio-zone map; see Ch. 17 for how they are
consumed and which per-map table each indexes):

| Tile byte(s) | Role | Indexes | Confidence |
|---|---|---|---|
| +2 | **music-zone id** | the per-map `.bgm` table (Ch. 17) | CONFIRMED |
| +3, +4 | looped ambient SFX zone id(s) | the per-map `.bge` table | HIGH |
| +5, +6, +7 | event / 3D point SFX id(s) | the per-map `.eff` table | HIGH |
| (other) | footstep surface selection → `.wlk` / `.run` tables (the more commonly documented role) | — | CONFIRMED (role) |

Runtime lookup subtracts the cell's `(cellX x 1024, cellZ x 1024)` world origin and quantizes to a
tile, returning the one 8-byte tile under a given world XZ.

> **Flag for `formats/mud.md` / `formats/terrain.md` (not owned by this chapter).** The 8-byte
> `.mud` tile's internal layout was previously marked UNVERIFIED. The audio-byte roles above (+2
> music, +3/+4 ambient, +5/+6/+7 event SFX) are a genuine upgrade and should be promoted into the
> owning `.mud` cell-format spec when that file is next revised. This chapter records them so the
> regions and audio stories are not conflated; the byte-table itself belongs to the format spec.

### 16.6 Known unknowns (so the engineer never guesses)

- The exact label for zone type `0` ("safe" is inferred from the arbiter's denied result, never seen
  named). [UNVERIFIED] — but the **enum is complete at three values** (16.3); there is no type `3+`.
- The `zoneName` exact byte width / encoding (asserted 40 bytes, consumed as a `char*`; CP949) — not
  byte-sampled. [MED — see `formats/region_grid.md`]
- The `regiontable` record `_tail` (+44) — the only genuinely-unread dword in the record. [UNVERIFIED]
- The remaining bytes of the 8-byte `.mud` tile beyond the +2/+3/+4/+5/+6/+7 audio roles (e.g. the
  footstep-surface byte position and any flags). [UNVERIFIED — owned by the `.mud` format spec]
- The contents of the 520-byte per-area map header (loaded with the region tables but not part of
  region gating). [UNVERIFIED]
- The in-game meaning of the specific cosmetic region-id special-case (the region whose entry plays a
  fixed ambient jingle / selects a distinct icon). [UNVERIFIED — confirmed cosmetic, meaning unpinned]

- **Driving VFS data:** `region<area>.bin` (256-unit region-id grid), `regiontable<area>.bin`
  (32 x 48-byte records: zoneName + zoneType + tail; the same file the minimap reads for sub-zone
  labels, Chapter 10), `gathertable<area>.bin` (28-byte named world-anchor records), per-cell `.mud`
  (64x64 audio-tile grid); `msg.xdb` (the movement-denied message id 74309) —
  `formats/region_grid.md`, `formats/config_tables.md`.
- **See also:** Chapter 1 (combat — the PvP arbiter feeds the same loop), Chapter 10 (minimap —
  shares `regiontable<area>.bin`), Chapter 17 (per-area music — the `.mud +2` driver),
  `formats/region_grid.md` (the region grid + record byte layouts), `formats/terrain.md` (1024-unit
  terrain cells + `.ted`/`.sod` ground/collision the region grid is layered over). The
  `map_option<area>.bin` byte layout is owned by `formats/environment_bins.md`.

---

## 17. Per-area background music & audio zones — Campaign 5B

> **Campaign-5B addition** (Lane 3 — world). Background music (BGM) is **not** chosen per-AREA by a
> single map-header field, nor by `map_option`, nor by the region grid. It is chosen **per-CELL**,
> driven by the `.mud` audio-zone grid (Ch. 16.5). This chapter documents the per-map sound tables,
> the per-cell music-zone driver, and the cross-fade play path. Engine code citing a constant here
> writes `// spec: Docs/RE/specs/world_systems.md` (Ch. 17).

### 17.1 The five per-map sound tables

On area activation the client loads **five same-format sound tables** for the active map id, all
under `data/map<id>/`:

| File | Role | Indexed by |
|---|---|---|
| `soundtable<id>.wlk` | footstep — walk surface | `.mud` footstep-surface byte |
| `soundtable<id>.run` | footstep — run surface | `.mud` footstep-surface byte |
| `soundtable<id>.bgm` | **background music** tracks | `.mud` cell byte **+2** (music-zone id) |
| `soundtable<id>.bge` | looped ambient SFX | `.mud` cell bytes +3 / +4 |
| `soundtable<id>.eff` | event / 3D point SFX | `.mud` cell bytes +5 / +6 / +7 |

- **Table shape:** each table is **256 entries × 48 bytes** ( = the 0x3000 bytes the client reads
  per table). On disk the file is slightly larger; the trailing bytes beyond the 256×48 body are
  editor metadata and are ignored by the runtime. [CONFIRMED]
- **Per-entry layout (probable):** the leading dword of each 48-byte entry is the sound/track id; a
  following region of the entry is a **per-hour active-flag schedule** (a zone can carry a different
  track by hour-of-day, e.g. a day track vs a night track), with remaining param/position fields. The
  hour-schedule existence is HIGH; the exact per-field meanings are MED. [MED]

### 17.2 The per-frame music-zone driver

Each frame the sound manager picks the active music from the player's **current terrain cell**:

1. Take the local player's world position.
2. Look up the `.mud` grid tile under that XYZ (terrain-cell audio grid, Ch. 16.5). If no tile, abort.
3. Read the **music-zone id = `.mud` cell byte +2**.
4. On a **music-zone change** (the +2 id under the player differs from the current one), or when a
   **forced periodic re-pick** fires (~600 s), select and **cross-fade** to the new track:
   - **Trade/exchange-busy override (formerly mislabelled "indoor override"):** when the local player
     is in an **active player-to-player trade (exchange in progress)**, music is forced to a **fixed
     constant BGM id** (a constant, not a table lookup) — a trade-ambience / exchange-mode track — and
     the looped-ambient (`.bge`) slots are **suppressed** for the duration; the event/3D-point (`.eff`)
     slots are **unaffected**. The gate is a **per-local-player actor trade/exchange-busy flag**, set
     while a trade is open and cleared when it ends; it is written by the trade state-toggle handler
     (and propagated to the trade partner), **not** by any map cell, map-header, area-id, or region
     attribute. When the trade ends, the normal per-map table music + ambience resume. [CONFIRMED for
     the mechanism — fixed constant over the table, `.bge` suppressed, `.eff` untouched; HIGH for the
     trade-busy semantic label]
   - **Normal (not trading):** the music-zone id indexes the per-map `.bgm` table (entry = `12 dwords ×
     zone_id`, i.e. the first dword of the 48-byte `.bgm` record is the track id), and that track is
     played via the cross-fade path. [CONFIRMED]
5. The looped-ambient (`.bge`, bytes +3/+4) and event/3D SFX (`.eff`, bytes +5/+6/+7) zones are
   handled in the same per-cell-change loop. (During the trade-busy override above, the `.bge` slot
   start is skipped; the `.eff` slots still run.)

> **OPEN-RISK (semantic label only — the wiring is pinned):** a writer-scan found only the trade /
> interaction-state handlers setting this actor flag; **no distinct map/area/region "indoor" attribute
> was found feeding this override branch**. The absence of a separate map-indoor flag is carried at
> MEDIUM confidence (negative result); the gate wiring (this per-player actor flag forces the constant
> id and suppresses `.bge`) is HIGH/CONFIRMED. If a true map-indoor override exists elsewhere, it is
> not on this code path.

- **Movement throttle:** the driver early-outs unless the player has moved at least a small threshold
  since the last evaluation (or the ~600 s timer fired) — music is re-evaluated on movement, not every
  idle frame. [HIGH]

### 17.3 The play path — 2D cross-fade from `data/sound/2d/`

- The BGM play wrapper **cross-fades**: if a stream is already playing and the requested track id
  equals the current id, it only restores volume (no restart) — so re-entering a zone with the same
  track does not restart it. Otherwise it starts the new track. [CONFIRMED]
- Music tracks are **2D (non-positional) clips** loaded from **`data/sound/2d/`**. (3D positional SFX
  use `data/sound/3d/` via the event/footstep path — that is **not** music.) [CONFIRMED]
- A quality/volume option gate exempts two specific track ids from the music-volume slider (they look
  like always-audible UI/login jingles). [MED]

### 17.4 Regions do NOT trigger music — stated explicitly

To keep the regions story (Ch. 16) and the audio story separate:

- **The 256-unit region grid / `regiontable` does NOT drive music.** No music selection keys off the
  region id or any `regiontable` field. [CONFIRMED-NEGATIVE for `regiontable`]
- **Music selection is the finer per-CELL `.mud +2` music-zone byte**, looked up by world position
  each frame. [CONFIRMED-POSITIVE for `.mud`]
- A "music zone" in the loose sense (a contiguous run of cells sharing the same `.mud +2` id) is what
  changes the track — but the mechanism is the per-cell `.mud` byte and the per-map `.bgm` table, not
  the region grid. A single map can therefore contain many music zones.
- **Nor does any map/area/region "indoor" attribute drive an override.** The only thing that bypasses
  the per-cell `.mud +2` → `.bgm` table is the per-local-player **trade/exchange-busy** actor flag of
  §17.2 — there is no map-indoor attribute on this path. This reinforces the negative result above:
  neither regions nor a map-indoor flag select music; only the cell driver and the trade-busy override
  do. [CONFIRMED-NEGATIVE for a map-indoor attribute, MED on the absence]

### 17.5 Known unknowns

- `.bgm` per-entry field semantics beyond the leading track-id dword and the per-hour schedule
  (the position/param/float fields). [MED]
- The exact movement threshold and the precise periodic re-pick interval (~600 s observed). [MED]
- The two volume-exempt track ids' origin (UI/login jingles suspected). [MED]
- **Cross-references:** the `.mud` audio-byte roles are recorded in Ch. 16.5 and flagged for the
  owning `.mud` / `formats/terrain.md` spec; the per-map `soundtable<id>.{wlk,run,bgm,bge,eff}` table
  byte layout is not yet owned by a committed format spec and should be promoted to one when sampled.

---

## 18. Movement, targeting & pathfinding — CYCLE 7

> **Cycle-7 addition** (Block C). This chapter records the in-world locomotion and target-acquisition
> model: how the client announces movement, how it picks a target, why there is no client pathfinder,
> and how the server corrects client drift. It complements Chapter 1 (which only noted `2/13` as the
> combat-approach send) and `specs/camera_movement.md` (the local-prediction integration math). Engine
> code citing a constant here writes `// spec: Docs/RE/specs/world_systems.md` (Ch. 18). Movement is
> **local-prediction with periodic server reconciliation** — never lock-step. The headline of Chapter
> 0 holds: the client moves freely by prediction and merely *announces* intent; the **server owns the
> authoritative world position** and corrects the client out-of-band.

### 18.1 The movement-intent emitter (`2/13`, 16-byte body) — CONFIRMED

Movement is announced to the server by a single C2S message, `2/13` `CmsgMoveRequest`, with a
**fixed 16-byte body**. It is **event-driven** — there is **no fixed millisecond send interval / tick**.
It fires when the local player's movement *intent* changes: at motion start/stop ("arrival"), and as a
re-sync after a long frame stall.

| Body offset | Size | Type | Field | Notes | Confidence |
|---:|---:|---|---|---|---|
| +0 | 4 | f32 | **heading** | the facing angle, computed from the (destX − curX, destZ − curZ) delta — points from current position toward the destination | CONFIRMED |
| +4 | 4 | f32 | **destination X** | the target world X the player is walking toward | CONFIRMED |
| +8 | 4 | f32 | **destination Z** | the target world Z the player is walking toward | CONFIRMED |
| +12 | 1 | u8 | **tag** | a constant value `3` written inline on every emit | CONFIRMED |
| +13 | 1 | u8 | **runState** | `1` when the player is in the running movement state, else `0` (walking) | CONFIRMED |
| +14 | 2 | — | _pad | the unused tail of the 16-byte copy block; not explicitly written | CONFIRMED (size); contents UNVERIFIED |

- **Send cadence is event-driven, not a timer.** The client emits on motion-end ("arrived, here are my
  final coords"), and as a forced re-sync after a long inter-frame stall (a frame gap greater than the
  local-prediction window snaps the interpolation target and re-announces). A separate gated
  move-heartbeat path can also emit a liveness `2/13` while a move gate is open; its exact wall-clock
  cadence is **RUNTIME-ONLY**. No single fixed move interval is hard-coded. [CONFIRMED that it is
  event-driven; heartbeat wall-clock RUNTIME-ONLY]
- **Movement speed is data-driven.** Per-frame ground speed is read from the actor's animation/motion
  record at **+0x30 (walk rate)** or **+0x34 (run rate)** depending on the run/mount flag — i.e. the
  walk/run speed values live in the `actormotion` / animation-catalogue data tables (owned by the asset
  lane), **not** as inline constants. [CONFIRMED that the source is the motion record at +0x30/+0x34;
  the numeric speeds are RUNTIME/data-driven]

### 18.2 Two-tier targeting (`2/12` engage, `2/79` confirm) — CONFIRMED

Target acquisition is a **two-click model** with two distinct C2S opcodes:

| Opcode | Dir | Size | Role | Confidence |
|---|---|---|---|---|
| `2/12` | C2S | 3 | **Tier-1 — engage** (the first click): pick an actor in the scene / from the published cursor-target, run the local candidate filter, lock it, and notify the server. The 3-byte body packs a source-kind + index + slot selector into the actor tables. | CONFIRMED |
| `2/79` | C2S | 16 | **Tier-2 — confirm** (the second click): a UI-list highlight→confirm. The first click on a row only highlights locally (no send); a second click on the **same** already-highlighted row sends the 16-byte target descriptor. | CONFIRMED |
| `2/82` | C2S | 28 | The parallel name-search / named-target request (social / name panel). | CONFIRMED |
| `5/6`  | S2C | 16 | `SmsgActorAutotargetOrMotion` — the server-side auto-target / motion push (attack-mode change / motion change / clear) that confirms target/combat state. | CONFIRMED |

- **Locked target storage.** The current locked target's actor id is stored on the **local-player
  actor at +444**; the actor is re-resolved each access by id lookup. A separate published
  world-target descriptor (a 16-byte global block) holds the cursor/hover selection that the engage
  tier consumes. [CONFIRMED]
- **Selection is an index/id pick, not an in-routine raycast.** The cursor / mouse pick is done
  upstream (the hover-pick publishes the highlighted target into a global), and the targeting tiers
  consume that selection by reference. The Tier-1 engage runs a **candidate filter** (target must
  exist; a faction/group gate; a party/self alive gate over the tracked-slot table; a zone/region gate)
  before it locks and sends. [CONFIRMED]

### 18.3 No client pathfinder — straight-line + reactive collision — CONFIRMED

There is **no client-side A\* / waypoint / navmesh pathfinder.** A string search for
`astar / pathfind / waypoint / navmesh` yields **zero hits**, and the click-to-move path reads a
**single pending destination** and walks the local player **straight toward it** by per-frame forward
integration. When a destination is reached the client pulls the next one from a small destination list
(a straight-line follow of queued destinations, not a graph search).

- **Collision is reactive, not planned.** The straight walk is tested against the recovered `.sod` 2D
  XZ wall segments (ray-parity point-in-polygon), and the predicted Y is snapped to the `.ted` ground
  height every frame (bilinear). There is **no precomputed nav graph**. [CONFIRMED]
- **Model:** the client sends the *destination* (in the `2/13` body), walks straight there locally
  with reactive `.sod` wall collision and `.ted` ground-snap, and any real routing / validation is the
  **server's** responsibility — the server streams authoritative positions back via `5/13`. [CONFIRMED]

### 18.4 Server reconciliation (`5/13`, distance bands) — CONFIRMED

The server pushes authoritative actor positions via **`5/13` `SmsgActorMovementUpdate`** (a **40-byte
body**: XZ coords + heading + mode/run flags). The handler reconciles the client position against the
pushed position by **distance band**:

| Band (client–server divergence) | Client reaction | Confidence |
|---|---|---|
| small drift | smooth interpolation toward the server coordinate | CONFIRMED |
| divergence **> 200 units** | a **faster catch-up correction** (accelerated convergence to the server position) | CONFIRMED (200-unit threshold) |
| divergence **> 300 units** | a **hard teleport snap** to the server position | CONFIRMED (300-unit threshold) |
| already at the coordinate | position set exactly (no interpolation) | CONFIRMED |

- **Boundary statement.** The client moves the local player immediately by local prediction (forward
  integration + ground-snap) and merely *notifies* the server of heading + destination + run-state via
  `2/13`. The **server's position wins** whenever the client has drifted past the interpolation band:
  small drift is absorbed as interpolation, drift past ~200 units triggers a fast catch-up, and drift
  past ~300 units is a hard teleport. [CONFIRMED]

| Opcode | Dir | Size | Role |
|---|---|---|---|
| `2/13` | C2S | 16 | `CmsgMoveRequest` — movement-intent emitter (heading + dest + tag=3 + runState). |
| `2/12` | C2S | 3  | Tier-1 engage target. |
| `2/79` | C2S | 16 | Tier-2 confirm target. |
| `2/82` | C2S | 28 | Named-target request. |
| `5/6`  | S2C | 16 | `SmsgActorAutotargetOrMotion` — auto-target / motion confirm. |
| `5/13` | S2C | 40 | `SmsgActorMovementUpdate` — authoritative position push + reconciliation bands. |

- **Driving VFS data:** `actormotion.txt` / the animation catalogue (walk/run speed at the motion
  record +0x30 / +0x34), `.sod` wall segments + `.ted` ground height (collision / ground-snap) —
  `formats/config_tables.md`, `formats/terrain.md`.
- **See also:** Chapter 1 (combat approach loop also emits `2/13`); Chapter 16 (the zone/region gate
  the engage filter consults; the same movement gate denies entry into a type-2 cell); Chapter 7
  (ground-item pickup also depends on the alive/active actor gate); `specs/camera_movement.md` (the
  local-prediction integration math, the click-to-move follow, the reconciliation constants).

---

## 19. Death & respawn — CYCLE 7 (concise; world-exit owns the exit distinction)

> **Cycle-7 addition** (Block C). This chapter records the **death-state actor fields**, the death
> push, the local death modal, the respawn-countdown timer, and the respawn-choice request/response.
> The **death-vs-exit distinction** (local death does NOT leave the world; it enters a dead combat
> sub-mode) is owned by `specs/world_exit.md` — this chapter cross-refs there and keeps its own
> coverage concise. The death/respawn outcome is server-authoritative: the client renders the death,
> shows a respawn modal, and only sends a *choice*; the respawn location and restored HP are decided by
> the server. Engine code citing a constant here writes `// spec: Docs/RE/specs/world_systems.md`
> (Ch. 19).

### 19.1 Death-state actor fields — CONFIRMED

Death is recorded on the actor object by two state fields (and a timestamp), written when the death
motion plays:

| Actor offset | Field | Dead value | Alive / normal value | Confidence |
|---:|---|---|---|---|
| **+1424** | alive / active gate flag | **0 = dead** | `1` = alive | CONFIRMED |
| **+1420** | action / motion-state | **8 = death** | `1` = idle / normal | CONFIRMED |
| **+1484** | death timestamp (ms) | set at the moment of death | — | CONFIRMED (actor-world audit 2026-06-24; corrects earlier "+1480" reading) |

- `+1420 == 8` is the canonical "is this actor dead" test; `+1424` is the generic "alive & interactable"
  guard — the movement, targeting, and pickup paths all early-return when it is `0`, which is the
  implicit input lockout while dead (there is no separate "input disabled" boolean). [CONFIRMED]
- The full `lifecycle_state` (+1420) value set confirmed by the actor-world audit includes values
  **0** (uninit), **1** (idle), **2** (walk), **3** (run), **4** (motion state), **8** (dead), **11/12/13**
  (buff-transform poses), **15** (relation-refresh), and **17** (special-sort/motion). Only value 8 is
  relevant to the death path; the others govern motion gating. See `structs/actor.md`.
- These fields are (re)set alive (`+1424 = 1`, `+1420 = 1`) by the spawn / respawn / network handlers
  when an actor (re)enters the world alive. [CONFIRMED]

### 19.2 The death push (`5/10`, 20-byte body) — CONFIRMED

Inbound death is `S2C 5/10 SmsgCharDeath`, a **20-byte body**. Its key field is a **death-cause** code
that selects the local reaction:

| deathCause | Meaning | Confidence |
|---|---|---|
| `0` | normal death | CONFIRMED |
| `1` / `2` | PK-type death (player-kill) — drives the PK death VFX and notice text | CONFIRMED |
| `3` | special death — no town-respawn option (modal mode 3 only) | CONFIRMED |

On death the handler plays the death motion (setting the §19.1 fields), **partially clears** the
dying actor's buff table — slots with buff_id in the protected range **[80..130] ∪ {13} ∪ [132..134]**
are preserved; all others are cleared (buff_id 131 additionally sets the motion-suppress flag); the
same clear is applied to the local buff-bar mirror — and clears the combat target. If the dying actor
is the local player, the handler opens the death/respawn modal and enqueues the respawn countdown
(§19.3). The battle-controller's locked target (BC+9 / BC+40) is also cleared if the dying actor
matches it, and BC+16 is set to 2 (death state) with BC+68/+70 zeroed. Death notices and
"killed by &lt;name&gt;" lines are localized `msg.xdb` ids; the exact wording is CP949 table data =
RUNTIME-ONLY.

### 19.3 Local death modal + respawn countdown — CONFIRMED

- **Local death modal:** when the local player dies normally, the modal opens in **mode 1 if the
  player's level is ≥ 36, else mode 3** (a lower-vs-higher-level respawn-option layout). `deathCause 3`
  forces mode 3. (A mode-2 "revive by another player" variant is reached from a separate revive-offer
  path.) [CONFIRMED]
- **Respawn countdown:** driven by a timed event (event id **10003**). For the local player it
  decrements a countdown (showing the remaining seconds / minutes from `msg.xdb`) and, on reaching `0`,
  auto-sends the default respawn choice. The normal countdown is **600 seconds** (a region-gated
  alternative of 20 seconds applies in a specific PvP-zone context — RUNTIME context); `deathCause 3`
  uses a short one-shot instead. Remote actors are revived visually by the same timer without any
  network round-trip. [CONFIRMED that the timer is event 10003 and the normal duration is 600 s; the
  region/alt selection is region-gated, RUNTIME context]

### 19.4 Respawn request / response — CONFIRMED

The respawn choice is sent through the generic dialog-response sender as **C2S 2/3**, body = a single
**int16 choice** (`0` / `1` / `2` / `3`) selecting which respawn option (e.g. town / accept-revive /
in-place / variant). The server answers with two pushes:

| Opcode | Dir | Size | Role | Confidence |
|---|---|---|---|---|
| `2/3` | C2S | 2 (int16 choice) | respawn / dialog response — the respawn choice (and the auto-send on countdown 0). | CONFIRMED |
| `4/28` | S2C | 20 | respawn confirm — **local-player** relocate/respawn (recreate the local actor at the new server position, re-resolve the cell). | CONFIRMED |
| `5/28` | S2C | 12 | respawn-at-point — recreate a **remote** actor at a respawn point. | CONFIRMED |

- **Respawn location & HP are server-authoritative.** The client only sends the choice; the new
  position arrives in the `4/28` / `5/28` body, and restored HP/MP arrive via the vitals push (`5/53`).
  There is **no client-side respawn-position or HP arithmetic**, and no client-side death-penalty
  computation (XP / durability / item-drop magnitudes are all server-authoritative = RUNTIME-ONLY).
  [CONFIRMED — absence of client penalty math]

| Opcode | Dir | Size | Role |
|---|---|---|---|
| `5/10` | S2C | 20 | `SmsgCharDeath` — death push + deathCause {0 normal, 1/2 PK, 3 special}. |
| `2/3`  | C2S | 2  | respawn / dialog-response choice (0/1/2/3). |
| `4/28` | S2C | 20 | respawn confirm (local relocate). |
| `5/28` | S2C | 12 | respawn-at-point (remote actor). |

- **Driving VFS data:** `msg.xdb` (death notices, "killed by" lines, countdown seconds/minutes text)
  — `formats/config_tables.md`.
- **See also:** `specs/world_exit.md` (death does NOT leave the world; the dead combat sub-mode);
  `specs/combat.md` (the death-state field convention + on-death cleanup); Chapter 1 (the combat loop
  whose result lands the killing blow); Chapter 9 (buffs cleared on death); Chapter 7 (ground items —
  any item loss arrives as ordinary server-driven spawns, not client-computed).

---

## 20. Dark / absent subsystems — CYCLE 7 (load-bearing negative results)

> **Cycle-7 addition** (Block D). This section records subsystems that are **confirmed absent** from
> the client, so a faithful re-implementation never builds a phantom system. Each verdict is backed by
> an exhaustive static census — a CP949 raw byte-scan for the relevant Korean keywords across the whole
> image, plus RTTI / panel-class and opcode decode of the closest "near-miss" features. **No
> corresponding subsystem was found in static analysis (CYCLE 7).** Each item is stated as a negative
> result by exhaustion, not a positive design claim. [All CONFIRMED-ABSENT]

| Subsystem | Verdict | What exists instead (the near-miss) |
|---|---|---|
| **Mounts** | **ABSENT** | A `vehicle.xdb` table exists but is a **visual seat table only** — it carries no mount-movement or mount-stat system. No ride locomotion, no mount stats. |
| **Auction house** | **ABSENT** | Only **personal stalls** exist (player-run fixed-price shops). The closest opcode, `2/118`, is an **empty-body stall buy-confirm**, not a bid — there are no listings, no competing bids, and no win-bid resolution. **stall ≠ auction.** |
| **Instanced dungeons** | **ABSENT** | The "brood-war" maps are **shared, scheduled** event maps joined by many parties — **no instance id, no lockout, no per-party private copy**. Maps are addressed by global indices. **special-map ≠ instance.** |
| **Housing** | **ABSENT** | Only the personal **stall** (a temporary vendor placement) and the **keep** (item storage) exist — neither is an owned, decoratable dwelling. **stall / keep ≠ housing.** |
| **Arena / ranking ladder** | **ABSENT (as a system)** | "ShowDown" is a **peer 1-v-1 duel** issued through the generic context-action (operation **42**), resolved peer-to-peer — no matchmaking, no persistent standings. "RankStatePanel" is merely a **buff-icon status strip** (the player's fame/rank tier painted as icons), **not** a leaderboard. **duel ≠ arena-ladder.** |

- **Evidence basis:** a raw CP949 keyword scan (auction / bid / win-bid / dungeon / instance / house /
  furniture / ranking / rank-order / arena / duel / ladder) returned **zero hits** for every absent
  system; the only related hit anywhere was the single "duel" string behind the peer ShowDown feature.
  Mounts, auction, instances, housing, and arena/ladder are confirmed-absent by exhaustion in static
  IDA (CYCLE 7). [CONFIRMED]
- **Why this matters:** these are the systems most likely to be assumed present by analogy with other
  MMORPGs. The re-implementation should **not** scaffold a mount system, an auction house, an instance
  manager, a housing system, or an arena/ladder matchmaker. The faithful surfaces are the **stall**
  (vendor), the **keep** (storage), the shared **brood-war** event maps, and the peer **ShowDown**
  duel — already covered by the inventory/NPC and social chapters.
- **See also:** Chapter 6 (inventory / trade — the stall and keep surfaces); Chapter 5 (social — the
  context-action `42` channel ShowDown rides; the buff-icon RankState strip is a HUD status panel,
  Chapter 9 / 13).

---

- **Glossary:** see `Docs/RE/names.yaml`
- **Provenance:** see `Docs/RE/journal.md`
