# Opcode Catalog (authoritative)

The clean-room source of truth for the Martial Heroes wire protocol. Consumed by the
`network-protocol-engineer` and validated by the `opcode-catalog` skill.

> **Capture-unverified, prominently.** No live network capture was available during this
> analysis. Every opcode->routing link below is a hard static fact (read from the client's
> dispatch installers), but the **field layouts** in the linked `packets/*.yaml` specs are
> static inferences. Treat all field offsets/sizes as hypotheses until a capture confirms them.

**Clean-room rules for this file:**
- **No IDA addresses** here. Address-level discovery lives in gitignored `_dirty/`; this catalog
  carries only promoted, neutral facts.
- Direction is from the client's point of view: `C2S` (client->server) or `S2C` (server->client).
- Each opcode with a known payload links to its field spec under `packets/`.

## Wire frame header

Every frame on the primary game connection begins with an 8-byte header, **little-endian**,
followed by the payload. The receive loop frames purely on these 8 bytes.

| Offset | Size | Field   | Meaning |
|--------|------|---------|---------|
| +0     | u16  | `size`  | Total frame size in bytes, including this 8-byte header. The field is physically a u32, but the I/O loop frames on the **low 16 bits only**; the upper 2 bytes (@+2) are unused/zero for framing. |
| +4     | u16  | `major` | Opcode high part / message family. Selects the dispatch group. |
| +6     | u16  | `minor` | Opcode low part / message id. Selects the handler within the group. |
| +8     | ...  | payload | Message body, read sequentially from payload offset 0. |

Key facts an implementer must rely on:

- **The `(major:minor)` pair IS the opcode.** There is no separate opcode field.
- **No sequence number, no checksum, no socket-layer cipher.** The header carries only size +
  major + minor; frames are copied verbatim by the transport layer. Any per-session encryption is
  established by the 0/0 key exchange and is out of scope for this catalog.
- **Some frames are compressed** (LZ4-family). A compressed frame is expanded into a fresh buffer
  *before* the `(major:minor)` dispatch, so handlers always see the decompressed payload.
- Minimum 8 bytes are required to parse a header. All packet-spec field offsets are
  **payload-relative** (relative to frame +8).

### Opcode encoding in this catalog

The `Opcode` column encodes the `(major:minor)` tuple as a single hex literal
`major << 16 | minor` (e.g. 5/13 -> `0x5000d`, 4/13 -> `0x4000d`, 0/0 -> `0x0`). The
human-readable `major:minor` form is repeated at the start of every `Notes` cell. The major
family is the high 16 bits; the minor is the low 16 bits.

## Major families

| major | Family | Direction | Routing |
|-------|--------|-----------|---------|
| 0 | KeyExchange | S2C (handshake) | Handled inline; triggers a C2S 1/4 auth reply. |
| 1 | ServerCommand | S2C | Direct switch on minor (billing / letter). |
| 2 | GameAction | **C2S** | Client-emitted; no inbound handler exists. |
| 3 | CharacterMgmt | S2C | Direct switch on minor. |
| 4 | Response | S2C | Table-driven (acks / results). Minors 500 and 50000 are special-cased outside the table. |
| 5 | Push | S2C | Table-driven (world events). |

## Catalog

| Opcode | Name | Direction | Size (bytes) | Packet spec | Status | Notes |
|---|---|---|---|---|---|---|
| `0x0` | SmsgKeyExchange | S2C | var | packets/0-0_key_exchange.yaml | draft | (0:0) Handshake; first secure-session frame. Carries server asymmetric key material; client replies with a C2S 1/4 auth. Layout capture-unverified. |
| `0x10004` | CmsgAuthReply | C2S | var | packets/1-4_auth_reply.yaml | draft | (1:4) Client auth reply in the login handshake; sent over the secure-send path after the 0/0 key exchange. Carries the XOR-ciphered login credential body. Layout crypto-dependent; hand-off to Network.Crypto. |
| `0x10006` | CmsgLoginRequest | C2S | var | - | draft | (1:6) Account/login credential blob (~52-byte form) in the major-1 account family. Field layout not yet specced; re-probe the caller before committing a struct. |
| `0x10009` | CmsgEnterGameRequest | C2S | 40 | packets/1-9_enter_game_request.yaml | draft | (1:9) Enter-world / select-character request: selected slot index + client-version token. Server replies with 3/5 EnterGameAck. Layout capture-unverified. |
| `0x10010` | SmsgSrvBillingDeactivated | S2C | var | - | confirmed | (1:16) Billing turned off. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x10011` | SmsgSrvBillingActivated | S2C | var | - | confirmed | (1:17) Billing turned on. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x10013` | SmsgSrvBillingExpiryNotice | S2C | var | - | confirmed | (1:19) Billing expiry notice. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x10014` | SmsgSrvLetterReceived | S2C | var | - | confirmed | (1:20) In-game letter/mail received. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x20007` | CmsgWhisper | C2S | var | packets/2-7_whisper.yaml | draft | (2:7) Client whisper: 19-byte header (16-byte target name) + length-prefixed text. Layout capture-unverified. |
| `0x2000d` | CmsgMoveRequest | C2S | 16 | packets/2-13_move_request.yaml | draft | (2:13) Client click-to-move / position sync: heading + target XZ + mode/run flags (16-byte fixed payload). Server echoes via 5/13. Layout capture-unverified. |
| `0x20034` | CmsgUseSkill | C2S | var | packets/2-52_use_skill.yaml | draft | (2:52) Client skill-activate request: 24-byte header (skill slot + aim) + two optional u32 target-id arrays. Send-site now analyzed. Server replies with 5/52. Layout capture-unverified. |
| `0x20053` | CmsgChatContextual | C2S | var | packets/2-83_chat_contextual.yaml | draft | (2:83) Client contextual chat: 24-byte context header + length-prefixed text. Header fields not yet decoded; layout capture-unverified. |
| `0x30001` | SmsgCharacterList | S2C | var | packets/3-1_character_list.yaml | draft | (3:1) Character-select list; switches to the select screen. 3-byte header + per-slot 981-byte records gated by a slot bitmask. |
| `0x30004` | SmsgCharManageResult | S2C | var | - | confirmed | (3:4) Char create/delete/manage response. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x30005` | SmsgEnterGameAck | S2C | 44 | packets/3-5_enter_game_response.yaml | draft | (3:5) Enter-world acknowledgement; transitions client into the in-world game state. |
| `0x30006` | SmsgRenameCharResult | S2C | var | - | confirmed | (3:6) Character rename result. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x30007` | SmsgCharSpawnResult | S2C | var | - | confirmed | (3:7) Spawn-into-world response. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x30008` | SmsgShopPageUpdate | S2C | var | - | confirmed | (3:8) Shop page update. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x3000d` | SmsgCharStatusUpdate | S2C | var | - | confirmed | (3:13) Character status update. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x3000e` | SmsgSceneEntityUpdate | S2C | var | - | confirmed | (3:14) Scene / entity update. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x30015` | CmsgChatChannel | C2S | var | packets/3-21_chat_channel.yaml | draft | (3:21) Client general/channel chat: 56-byte context header (channel selector at +4) + length-prefixed text. Header partly decoded; layout capture-unverified. |
| `0x30017` | SmsgCharCreateResult | S2C | var | - | confirmed | (3:23) Character create result. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x30064` | SmsgCharActionResult | S2C | var | - | confirmed | (3:100) Generic character action result. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x3c350` | SmsgGmChatMessage | S2C | var | - | confirmed | (3:50000) GM chat / system message. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x40001` | SmsgGameStateTick | S2C | var | - | confirmed | (4:1) Periodic game-state tick. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x40002` | SmsgGameTickConfig | S2C | var | - | confirmed | (4:2) Game-tick configuration. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x40003` | SmsgBillingInfo | S2C | var | - | confirmed | (4:3) Billing info. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x40004` | SmsgAreaEntitySnapshot | S2C | var | - | confirmed | (4:4) Area entity snapshot update; carries SpawnDescriptor-family records. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x40005` | SmsgItemUseResult | S2C | var | - | confirmed | (4:5) Item-use result. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x4000c` | SmsgEquipItemResult | S2C | var | - | confirmed | (4:12) Equip-item result. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x4000d` | SmsgLocalPlayerStateSync | S2C | var | - | confirmed | (4:13) Local player state sync. Handler is mislabelled in raw notes with a stale Push/5-100 suffix; it is wired as Response 4/13. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x4000e` | SmsgGroundItemSlotAck | S2C | var | - | confirmed | (4:14) Ground-item slot acknowledgement. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x4000f` | SmsgItemWorldPickupAck | S2C | var | - | confirmed | (4:15) World item pickup acknowledgement. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x40010` | SmsgEquipChangeResult | S2C | var | - | confirmed | (4:16) Equip change result. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x40011` | SmsgQuickEquipSlotAck | S2C | var | - | confirmed | (4:17) Quick-equip slot acknowledgement. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x40013` | SmsgNpcBuyOrAcquireAck | S2C | var | - | confirmed | (4:19) NPC buy / inventory acquire acknowledgement. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x40014` | SmsgNpcSellItemAck | S2C | var | - | confirmed | (4:20) NPC sell-item acknowledgement. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x40015` | SmsgNpcShopSlotClearAck | S2C | var | - | confirmed | (4:21) NPC shop slot clear acknowledgement. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x40016` | SmsgItemSlotStateAck | S2C | var | - | confirmed | (4:22) Item slot state acknowledgement. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x40017` | SmsgUserTradeRequestResult | S2C | var | - | confirmed | (4:23) User trade request result. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x40018` | SmsgUserTradeSlotUpdate | S2C | var | - | confirmed | (4:24) User trade slot update. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x40019` | SmsgUserTradeFullResponse | S2C | var | - | confirmed | (4:25) Full user-trade response. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x4001c` | SmsgRespawnConfirm | S2C | var | - | confirmed | (4:28) Respawn confirmation. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x4001d` | SmsgStatUpdate | S2C | 36 | packets/4-29_stat_update.yaml | draft | (4:29) Stat-allocation ack: result byte + five u32 stat echoes + remaining points (36-byte fixed block). Pairs with C2S 2/29. Routing dispatch-table-confirmed; field layout capture-unverified. |
| `0x4001e` | SmsgSocialPanelTarget | S2C | var | - | confirmed | (4:30) Social panel target. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x40023` | SmsgPartyInviteState | S2C | var | - | confirmed | (4:35) Party invite state. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x40024` | SmsgPartyMemberRemoveResult | S2C | var | - | confirmed | (4:36) Party member remove result. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x40025` | SmsgPartyLeaderActionResult | S2C | var | - | confirmed | (4:37) Party leader action result. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x40027` | SmsgResponseDiscard39 | S2C | var | - | confirmed | (4:39) Thin discard/drain slot; semantics uncertain. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x40028` | SmsgResponseSlot40 | S2C | var | - | confirmed | (4:40) Thin UI-slot updater; semantics uncertain. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x40029` | SmsgSkillHotbarAssignResult | S2C | var | - | confirmed | (4:41) Skill hotbar assign result. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x4002a` | SmsgPlayerInteractionState | S2C | var | - | confirmed | (4:42) Player interaction state. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x4002b` | SmsgPlayerInteractionResult | S2C | var | - | confirmed | (4:43) Player interaction result. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x4002c` | SmsgActorTickTableOpA | S2C | var | - | confirmed | (4:44) Actor tick-table op A. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x4002d` | SmsgActorTickTableOpB | S2C | var | - | confirmed | (4:45) Actor tick-table op B. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x4002e` | SmsgActorTickTableOpC | S2C | var | - | confirmed | (4:46) Actor tick-table op C. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x4002f` | SmsgSocialAckDrain | S2C | var | - | confirmed | (4:47) Social acknowledgement drain. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x40030` | SmsgRankProgressEvent | S2C | var | - | confirmed | (4:48) Rank progress event. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x40031` | SmsgRankProgressPanelUpdate | S2C | var | - | confirmed | (4:49) Rank progress panel update. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x40032` | SmsgUpgradeItemResult | S2C | var | - | confirmed | (4:50) Item upgrade result. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x40036` | SmsgGuildRankSlotUpdate | S2C | var | - | confirmed | (4:54) Guild rank slot update. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x40037` | SmsgGuildInfoUpdateResult | S2C | var | - | confirmed | (4:55) Guild info update result. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x40038` | SmsgResponseSlot56 | S2C | var | - | confirmed | (4:56) Thin UI-slot updater; semantics uncertain. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x40039` | SmsgResponseSlot57 | S2C | var | - | confirmed | (4:57) Thin UI-slot updater; semantics uncertain. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x4003a` | SmsgResponseSlot58 | S2C | var | - | confirmed | (4:58) Thin UI-slot updater; semantics uncertain. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x4003c` | SmsgResponseSlot60 | S2C | var | - | confirmed | (4:60) Thin UI-slot updater; semantics uncertain. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x4003d` | SmsgGuildStateChangeResult | S2C | var | - | confirmed | (4:61) Guild state change result. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x4003e` | SmsgGuildInviteJoinState | S2C | var | - | confirmed | (4:62) Guild invite/join state. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x4003f` | SmsgGuildMemberRemoveResult | S2C | var | - | confirmed | (4:63) Guild member remove result. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x40040` | SmsgGuildPositionChangeResult | S2C | var | - | confirmed | (4:64) Guild position change result. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x40041` | SmsgGuildInfoFullSync | S2C | var | - | confirmed | (4:65) Guild info full sync. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x40042` | SmsgResponseSlot66 | S2C | var | - | confirmed | (4:66) Thin UI-slot updater; semantics uncertain. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x40046` | SmsgResponseSlot70 | S2C | var | - | confirmed | (4:70) Thin UI-slot updater; semantics uncertain. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x40047` | SmsgResponseSlot71 | S2C | var | - | confirmed | (4:71) Thin UI-slot updater; semantics uncertain. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x40048` | SmsgResponseSlot72 | S2C | var | - | confirmed | (4:72) Thin UI-slot updater; semantics uncertain. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x4004a` | SmsgResponseSlot74 | S2C | var | - | confirmed | (4:74) Thin UI-slot updater; semantics uncertain. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x4004b` | SmsgResponseSlot75 | S2C | var | - | confirmed | (4:75) Thin UI-slot updater; semantics uncertain. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x4004c` | SmsgPartyAcceptResult | S2C | var | - | confirmed | (4:76) Party accept result. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x4004e` | SmsgServerTimeNotification | S2C | var | - | confirmed | (4:78) Server time notification. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x4004f` | SmsgCraftingResult | S2C | var | - | confirmed | (4:79) Crafting result. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x40050` | SmsgPvpDeathResult | S2C | var | - | confirmed | (4:80) PvP death result. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x40051` | SmsgActionErrorResult | S2C | var | - | confirmed | (4:81) Action error result. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x40052` | SmsgBillingBalanceUpdate | S2C | var | - | confirmed | (4:82) Billing balance update. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x40053` | SmsgBillingItemUseResult | S2C | var | - | confirmed | (4:83) Billing item-use result. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x40054` | SmsgRecordSlotConsumeResult | S2C | var | - | confirmed | (4:84) Record slot consume/clear result. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x4005d` | SmsgUserVoteTallyUpdate | S2C | var | - | confirmed | (4:93) User vote tally update. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x4005f` | SmsgLocalPlayerBattleMode | S2C | var | - | confirmed | (4:95) Local player battle-mode update. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x40060` | SmsgActorGuildRosterEntry | S2C | var | - | confirmed | (4:96) Actor guild roster entry update. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x40061` | SmsgAreaSkillEffectPanel | S2C | var | - | confirmed | (4:97) Area skill-effect result panel update. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x40063` | SmsgCombatResultMessage | S2C | var | - | confirmed | (4:99) Combat result message. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x40064` | SmsgCombatAttackUpdate | S2C | var | - | confirmed | (4:100) Combat attack update. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x40065` | SmsgGlobalScalarPairUpdate | S2C | var | - | confirmed | (4:101) Global scalar-pair update. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x40066` | SmsgSkillWindowStateUpdate | S2C | var | - | confirmed | (4:102) Skill window state update. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x40067` | SmsgGuildPanelTextUpdate | S2C | var | - | confirmed | (4:103) Guild panel text update. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x40069` | SmsgGagePanelProgressUpdate | S2C | var | - | confirmed | (4:105) Gage panel progress update. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x4006b` | SmsgEventLotteryPickResult | S2C | var | - | confirmed | (4:107) Event lottery number-pick result. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x4006c` | SmsgPlayerGoldBalanceUpdate | S2C | var | - | confirmed | (4:108) Player gold balance update. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x4006d` | SmsgLocalActorSkillStateFlag | S2C | var | - | confirmed | (4:109) Local actor skill-state flag. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x40071` | SmsgItemShopPurchaseResult | S2C | var | - | confirmed | (4:113) Item-shop purchase result. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x40072` | SmsgCashShopActionResult | S2C | var | - | confirmed | (4:114) Cash-shop action result. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x40073` | SmsgItemShopBalanceUpdate | S2C | var | - | confirmed | (4:115) Item-shop balance update result. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x40078` | SmsgActorTableBatchResult | S2C | var | - | confirmed | (4:120) Actor table batch result. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x4007a` | SmsgResponseSlot122 | S2C | var | - | confirmed | (4:122) Thin UI-slot updater; semantics uncertain. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x4007b` | SmsgResponseSlot123 | S2C | var | - | confirmed | (4:123) Thin UI-slot updater; semantics uncertain. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x4007d` | SmsgResponseSlot125 | S2C | var | - | confirmed | (4:125) Thin UI-slot updater; semantics uncertain. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x4007e` | SmsgResponseSlot126 | S2C | var | - | confirmed | (4:126) Thin UI-slot updater; semantics uncertain. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x40084` | SmsgGmNoticeError | S2C | var | - | confirmed | (4:132) GM notice / error. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x40085` | SmsgRankProgressUpdate | S2C | var | - | confirmed | (4:133) Rank progress update. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x40086` | SmsgStatChangeNotify | S2C | var | - | confirmed | (4:134) Stat change notify. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x40087` | SmsgResponseSlot135 | S2C | var | - | confirmed | (4:135) Thin UI-slot updater; semantics uncertain. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x40089` | SmsgGatheringResult | S2C | var | - | confirmed | (4:137) Gathering result. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x4008a` | SmsgNoticeError | S2C | var | - | confirmed | (4:138) Notice / error. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x4008b` | SmsgItemUseEffect | S2C | var | - | confirmed | (4:139) Item-use effect. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x4008c` | SmsgColoredSystemText | S2C | var | - | confirmed | (4:140) Coloured system text. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x4008e` | SmsgResponseSlot142 | S2C | var | - | confirmed | (4:142) Thin UI-slot updater; semantics uncertain. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x40092` | SmsgShowMessage51027 | S2C | var | - | confirmed | (4:146) Show message (string id 51027). Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x40094` | SmsgPlaytimeRewardResult | S2C | var | - | confirmed | (4:148) Playtime reward result. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x40095` | SmsgItemPanelSlotChunk | S2C | var | - | confirmed | (4:149) Item panel slot chunk. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x40096` | SmsgSkillPointUpdate | S2C | var | - | confirmed | (4:150) Skill point update. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x40097` | SmsgResponseSlot151 | S2C | var | - | confirmed | (4:151) Thin UI-slot updater; semantics uncertain. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x40098` | SmsgResponseSlot152 | S2C | var | - | confirmed | (4:152) Thin UI-slot updater; semantics uncertain. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x40099` | SmsgItemPanelSlotRefresh | S2C | var | - | confirmed | (4:153) Item panel slot refresh. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x401f4` | SmsgShowPopupByCode | S2C | var | - | confirmed | (4:500) Special: routed outside the response table; shows a popup by code. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x4c350` | SmsgResponseDiscardText | S2C | var | - | confirmed | (4:50000) Special: routed outside the response table; discards a text payload. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x50000` | SmsgCharDespawn | S2C | 12 | packets/5-0_char_despawn.yaml | draft | (5:0) Actor leaves the world. 12-byte fixed block. |
| `0x50001` | SmsgActorSpawnExtended | S2C | 912 | packets/5-1_actor_spawn_extended.yaml | draft | (5:1) Extended actor spawn (player/mob/item): 12-byte prefix + 880-byte SpawnDescriptor + 20-byte trailer. Routing dispatch-table-confirmed; field layout capture-unverified. |
| `0x50003` | SmsgCharSpawn | S2C | 908 | packets/5-3_char_spawn.yaml | draft | (5:3) Actor enters the world; payload is a SpawnDescriptor record. 908-byte fixed block. |
| `0x50005` | SmsgActorStateEvent | S2C | var | - | confirmed | (5:5) Actor state event. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x50006` | SmsgActorAutotargetOrMotion | S2C | var | - | confirmed | (5:6) Actor auto-target / motion. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x50007` | SmsgChatBroadcast | S2C | var | packets/5-7_chat_broadcast.yaml | draft | (5:7) Chat broadcast: 36-byte header + variable text body. |
| `0x50009` | SmsgExpGain | S2C | var | - | confirmed | (5:9) Experience gain. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x5000a` | SmsgCharDeath | S2C | var | - | confirmed | (5:10) Character death. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x5000b` | SmsgRankXpGain | S2C | var | - | confirmed | (5:11) Rank XP gain. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x5000c` | SmsgActorVisualSlotSet | S2C | var | - | confirmed | (5:12) Actor visual slot set. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x5000d` | SmsgActorMovementUpdate | S2C | 40 | packets/5-13_actor_movement_update.yaml | draft | (5:13) Actor movement update. 40-byte fixed block (XZ-plane, float coords). |
| `0x5000e` | SmsgCombatEffectInstanceSpawn | S2C | var | - | confirmed | (5:14) Combat effect instance spawn. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x5000f` | SmsgTrackedWorldObjectRemove | S2C | var | - | confirmed | (5:15) Tracked world object remove. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x50010` | SmsgActorVisualSlotClear | S2C | var | - | confirmed | (5:16) Actor visual slot clear. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x50012` | SmsgGameClockUpdate | S2C | var | - | confirmed | (5:18) Game clock update. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x50015` | SmsgPartyRosterEvent | S2C | var | - | confirmed | (5:21) Party roster event. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x5001a` | SmsgLocalPlayerRelationSlot | S2C | var | - | confirmed | (5:26) Local player relation slot update. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x5001c` | SmsgRespawnAtPoint | S2C | var | - | confirmed | (5:28) Respawn at point. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x5001f` | SmsgBuffSlotUpdate | S2C | var | - | confirmed | (5:31) Buff slot update. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x50020` | SmsgLevelUp | S2C | 48 | packets/5-32_level_up.yaml | draft | (5:32) Level up: new level + packed HP/MP + stamina + rank-XP tail (48-byte fixed block). Routing dispatch-table-confirmed; field layout capture-unverified. |
| `0x50021` | SmsgSkillHotbarSlotSet | S2C | var | - | confirmed | (5:33) Skill hotbar slot set. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x50022` | SmsgBillingBannerToggle | S2C | var | - | confirmed | (5:34) Billing banner toggle. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x50026` | SmsgPartyMemberStats | S2C | var | - | confirmed | (5:38) Party member stats. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x50027` | SmsgPushNop39 | S2C | var | - | confirmed | (5:39) No-op slot. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x5002a` | SmsgPlayerPairSystemNotice | S2C | var | - | confirmed | (5:42) Player-pair system notice. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x50033` | SmsgSkillGuideState | S2C | var | - | confirmed | (5:51) Skill guide state. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x50034` | SmsgActorSkillAction | S2C | var | packets/5-52_actor_skill_action.yaml | draft | (5:52) Actor skill action / combat result. 24-byte header + N x 36-byte target records. |
| `0x50035` | SmsgActorVitalsAndPairState | S2C | 32 | packets/5-53_actor_vitals_and_pair_state.yaml | draft | (5:53) Actor vitals (HP/stamina/third vital) + level/state + pair (couple) id (32-byte fixed block). Routing dispatch-table-confirmed; field layout capture-unverified. |
| `0x50037` | SmsgGuildNameDisplayUpdate | S2C | var | - | confirmed | (5:55) Guild name display update. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x50039` | SmsgTrackedPanelSlotUpdate | S2C | var | - | confirmed | (5:57) Tracked panel slot update. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x5003b` | SmsgRankProgressSfxEvent | S2C | var | - | confirmed | (5:59) Rank progress SFX event. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x5003d` | SmsgActorNameOverlaySet | S2C | var | - | confirmed | (5:61) Actor name overlay set. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x50040` | SmsgRemoteActorRelationPair | S2C | var | - | confirmed | (5:64) Remote actor relation pair update. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x50041` | SmsgGuildMemberRosterUpdate | S2C | var | - | confirmed | (5:65) Guild member roster update. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x50043` | SmsgStatsUpdate | S2C | var | - | confirmed | (5:67) Stats update. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x50044` | SmsgQuestList | S2C | var | - | confirmed | (5:68) Quest list. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x50049` | SmsgQuestComplete | S2C | var | - | confirmed | (5:73) Quest complete. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x5004c` | SmsgPartyMemberJoined | S2C | var | - | confirmed | (5:76) Party member joined. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x5004d` | SmsgRankProgressPanelBulk | S2C | var | - | confirmed | (5:77) Rank progress panel bulk. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x5004f` | SmsgActorDeathState | S2C | var | - | confirmed | (5:79) Actor death state. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x50050` | SmsgPvpDeathFx | S2C | var | - | confirmed | (5:80) PvP death FX. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x50055` | SmsgDungeonEventStateSyncA | S2C | var | - | confirmed | (5:85) Dungeon event state sync A. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x50056` | SmsgDungeonEventStateSyncB | S2C | var | - | confirmed | (5:86) Dungeon event state sync B. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x50057` | SmsgDungeonEventActorClear | S2C | var | - | confirmed | (5:87) Dungeon event actor state clear. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x50058` | SmsgPvpStateBytes | S2C | var | - | confirmed | (5:88) PvP state bytes. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x50059` | SmsgPvpRevengeRoster | S2C | var | - | confirmed | (5:89) PvP revenge roster. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x5005a` | SmsgPvpCounters | S2C | var | - | confirmed | (5:90) PvP counters. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x5005b` | SmsgPvpScoreUpdate | S2C | var | - | confirmed | (5:91) PvP score update. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x5005c` | SmsgPvpRequestOrNotice | S2C | var | - | confirmed | (5:92) PvP request / notice. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x5005d` | SmsgActorMobIdChange | S2C | var | - | confirmed | (5:93) Actor mob-id change. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x5005e` | SmsgVoteResult | S2C | var | - | confirmed | (5:94) Vote result. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x50062` | SmsgActorClassFormRefresh | S2C | var | - | confirmed | (5:98) Actor class/form refresh. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x5006a` | SmsgTradeStateToggle | S2C | var | - | confirmed | (5:106) Trade state toggle. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x50079` | SmsgCharPropertySet | S2C | var | - | confirmed | (5:121) Character property set. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x5007b` | SmsgGiftCharReceiveConfirm | S2C | var | - | confirmed | (5:123) Gift character receive confirm. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x5007c` | SmsgActorVisualFlagsSet | S2C | var | - | confirmed | (5:124) Actor visual flags set. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x5007e` | SmsgActorStanceSet | S2C | var | - | confirmed | (5:126) Actor stance set. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x5007f` | SmsgStealthToggle | S2C | var | - | confirmed | (5:127) Stealth toggle. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x50081` | SmsgMonsterNoticeByMobId | S2C | var | - | confirmed | (5:129) Monster notice by mob id. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x50082` | SmsgMonsterNoticeWithText | S2C | var | - | confirmed | (5:130) Monster notice with text. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x50083` | SmsgPvpRankScoreUpdate | S2C | var | - | confirmed | (5:131) PvP rank score update. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x50088` | SmsgActorTimedStateUpdate | S2C | var | - | confirmed | (5:136) Actor timed-state update. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x5008b` | SmsgAttackEffect | S2C | var | - | confirmed | (5:139) Attack effect. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x50092` | SmsgPacketResponseAckRequest | S2C | var | - | confirmed | (5:146) Packet response / ack request. Routing dispatch-table-confirmed; field layout not yet specced. |
| `0x50093` | SmsgActorCombatFlagUpdate | S2C | var | - | confirmed | (5:147) Actor combat-flag update. Routing dispatch-table-confirmed; field layout not yet specced. |

Status legend: `draft` (field layout hypothesized) - `observed` (seen in a capture) -
`confirmed` (cross-checked against the binary's dispatch table; here, opcode->handler routing is
confirmed even where the field layout is not yet specced) - `implemented` (C# struct + handler
exist).
