# World-scene gameplay systems — master index (clean-room spec)

> Clean-room neutral spec — a navigable **overview / index** of the in-world ("World scene")
> gameplay systems recovered in Cycle 3. No legacy symbols, no binary addresses, no decompiler
> pseudo-code. Promoted by synthesising the already-firewall-clean detail specs under `Docs/RE/`;
> every chapter cross-links to the committed spec that owns its byte tables.
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
| `4/100` | S2C | `SmsgCombatAttackUpdate` — combat-phase / attack-swing timing (server pacing). |
| `4/99` | S2C | `SmsgCombatResultMessage` — combat/training session result (reward/EXP strings). |
| `4/2` | S2C | per-tick server resync pulse — releases the local "attack in progress" flag. |

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

| Opcode | Dir | Role |
|---|---|---|
| (none) | — | The UI toolkit is local presentation; it carries no opcode of its own. It hosts the windows that emit/consume every opcode in Chapters 1–12 (action-id → window dispatcher → the relevant C2S send). |

- **Driving VFS data:** `uitex.txt`, `skillicon.txt`, `crestlist.txt`, `texturelist.txt` (UI texture /
  icon manifests), per-class stance `.do` files (e.g. `musajung.do`), `msg.xdb` (all captions) —
  `formats/ui_manifests.md`, `formats/config_tables.md`.
- **See also:** `specs/ui_system.md` (widget hierarchy, render path, input/capture, per-screen
  layouts, scene state machine); `formats/ui_manifests.md` (the UI texture-id resolver); the
  `specs/frontend_scenes.md` / `specs/client_workflow.md` front-end counterparts.

---

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
| `4/99` | S2C | var | `SmsgCombatResultMessage` | Combat (1) |
| `4/100`| S2C | var | `SmsgCombatAttackUpdate` | Combat cadence (1) |
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
| `formats/config_tables.md` | `.scr` / `.do` / `.csv` catalogues (items, skills, NPCs, stat curves, quests, `msg.xdb`). |
| `formats/misc_data.md` | `.xdb` / `.mi` / `.tol` / `.ion` / `.sc` small data files (incl. `actor_size.xdb`). |
| `formats/effects.md` | `.xeff` / `.eff` / `.xobj` descriptors and the effect-manifest list files. |
| `formats/ui_manifests.md` | `uitex.txt` / `skillicon.txt` / `crestlist.txt` / `texturelist.txt` UI manifests. |
| `opcodes.md` | The authoritative `(major:minor)` catalogue (the consolidated table above is a per-system index of it). |
| `packets/*.yaml` | Per-opcode wire field specs (linked per chapter). |

- **Glossary:** see `Docs/RE/names.yaml`
- **Provenance:** see `Docs/RE/journal.md`
