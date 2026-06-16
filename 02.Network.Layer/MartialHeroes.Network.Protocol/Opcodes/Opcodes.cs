// spec: Docs/RE/opcodes.md — the authoritative opcode catalog (182 ids).
// Each constant is the packed (major << 16 | minor) value verbatim from the catalog's `Opcode`
// column. Routing is dispatch-table-confirmed except where the per-id comment says otherwise;
// every linked FIELD layout remains CAPTURE-UNVERIFIED. Do not edit values without re-citing
// opcodes.md.
//
// Status legend (from opcodes.md): draft = field layout hypothesized; confirmed = opcode->handler
// routing cross-checked against the binary's dispatch table; implemented = C# struct + handler exist.

namespace MartialHeroes.Network.Protocol.Opcodes;

/// <summary>
/// Named constants for every opcode in <c>Docs/RE/opcodes.md</c>. Values are the packed
/// <c>(major &lt;&lt; 16) | minor</c> form. spec: Docs/RE/opcodes.md.
/// </summary>
public static class Opcodes
{
    // --- major 0: KeyExchange (S2C handshake) ---
    /// <summary>0:0 — handshake key exchange. status: draft. spec: Docs/RE/opcodes.md.</summary>
    public const uint SmsgKeyExchange = 0x0;

    // --- major 1: Account / CharacterMgmt (C2S request builders 0/6/7/9/13/14) ---
    /// <summary>1:0 — client logout / quit-from-in-game request (header-only, 0 B). status: draft. spec: packets/cmsg_logout.yaml.</summary>
    public const uint CmsgLogout = 0x10000;

    /// <summary>
    /// 1:6 — client character-CREATE request (52 B appearance/creation record). status: draft.
    /// spec: packets/cmsg_char_create.yaml. The prior "1/6 login-or-create collision" is RESOLVED:
    /// 1/6 is character-create ONLY (single emitter). The login credential is a separate sub-opcode
    /// 0x2B on the secure 1/4 frame (packets/login.yaml), NOT here. The 52-byte body's intra-payload
    /// field map is capture-unverified, so the struct models it as one opaque buffer. spec: opcodes.md.
    /// </summary>
    public const uint CmsgCreateCharacter = 0x10006;

    /// <summary>
    /// 1:7 — client character-MANAGE request (2 B: slot + mode). status: confirmed (routing+size).
    /// DELETE OVERLOADS this op — there is NO dedicated delete opcode in major 1: <c>{slot, 0}</c> =
    /// select/view, <c>{slot, 1}</c> = delete (code-confirmed literal). The delete/manage RESULT
    /// returns on S2C 3/7 SmsgCharManageResult (subtype 2). The C# struct keeps the
    /// <c>CmsgSelectCharacter</c> identifier; the catalog name is CmsgManageCharacter. spec:
    /// packets/cmsg_char_select.yaml.
    /// </summary>
    public const uint CmsgSelectCharacter = 0x10007;

    /// <summary>1:9 — client enter-world request (40 B: slot + version blob + version-check u32). status: draft. spec: packets/cmsg_char_enter.yaml.</summary>
    public const uint CmsgEnterGameRequest = 0x10009;

    /// <summary>1:13 — client rename-character request (18 B: slot + 17 B CP949 name). status: draft. spec: packets/cmsg_char_rename.yaml.</summary>
    public const uint CmsgRenameCharacter = 0x1000d;

    /// <summary>
    /// 1:14 — client slot-MOVE / relocate request (1 B target slot). status: draft.
    /// spec: packets/cmsg_char_move.yaml. The workflow-spine analysis re-attributes 1/14 from the
    /// earlier "delete" reading to slot-move; the move-vs-delete canonical reconciliation is Tier-1's.
    /// </summary>
    public const uint CmsgMoveCharacter = 0x1000e;

    /// <summary>1:16 — billing deactivated. status: confirmed.</summary>
    public const uint SmsgSrvBillingDeactivated = 0x10010;

    /// <summary>1:17 — billing activated. status: confirmed.</summary>
    public const uint SmsgSrvBillingActivated = 0x10011;

    /// <summary>1:19 — billing expiry notice. status: confirmed.</summary>
    public const uint SmsgSrvBillingExpiryNotice = 0x10013;

    /// <summary>1:20 — letter/mail received. status: confirmed.</summary>
    public const uint SmsgSrvLetterReceived = 0x10014;

    // --- major 2: GameAction (C2S) ---
    /// <summary>2:7 — client whisper / named private message (19 B header + text). status: draft. spec: packets/2-7_whisper.yaml.</summary>
    public const uint CmsgWhisper = 0x20007;

    /// <summary>2:13 — client click-to-move / position-sync request (16 B). status: draft. spec: packets/2-13_move_request.yaml.</summary>
    public const uint CmsgMoveRequest = 0x2000d;

    /// <summary>2:52 — client skill-activate request. status: draft.</summary>
    public const uint CmsgUseSkill = 0x20034;

    /// <summary>2:83 — client contextual chat (24 B header + text). status: draft. spec: packets/2-83_chat_contextual.yaml.</summary>
    public const uint CmsgChatContextual = 0x20053;

    /// <summary>
    /// 2:112 — on-demand keepalive enable/toggle (1-byte body, a single constant 1). status: confirmed.
    /// DISTINCT from the timer-driven 2/10000 keepalive (<see cref="CmsgKeepalive"/>); the client has
    /// TWO keepalive-family C2S sends on major 2. Gated by a master-enable flag, emitted on the
    /// leave-to-logout path, the game-state tick, and WinMain. spec: Docs/RE/opcodes.md.
    /// </summary>
    public const uint CmsgKeepaliveToggle = 0x20070;

    /// <summary>
    /// 2:10000 — timer-armed keepalive (4-byte zero body). status: confirmed. Frame template built
    /// once in the NetHandler ctor and armed at a ~20000 ms interval; compress-only send path (the
    /// cached buffer is LZ4-compressed once and never byte-ciphered). FIRST of two keepalive-family
    /// C2S messages; the second is the on-demand 2/112 toggle (<see cref="CmsgKeepaliveToggle"/>).
    /// spec: Docs/RE/opcodes.md; packets/2-10000_keepalive.yaml.
    /// </summary>
    public const uint CmsgKeepalive = 0x22710;

    // --- major 3: CharacterMgmt (S2C; C2S chat 3/21) ---
    /// <summary>3:1 — character-select list. status: draft.</summary>
    public const uint SmsgCharacterList = 0x30001;

    /// <summary>3:21 — client general / channel chat (56 B header + text). status: draft. spec: packets/3-21_chat_channel.yaml.</summary>
    public const uint CmsgChatChannel = 0x30015;

    /// <summary>
    /// 3:4 — scene / entity / char-slot update (variable-length slot-scratch refill / scene-clear).
    /// status: confirmed. De-swapped on build 263bd994 (Campaign 10): the minor-3 receive ladder
    /// routes minor 4 HERE — NOT the 8-byte char-manage result (that is 3/7 = SmsgCharManageResult)
    /// and NOT the 16-byte spawn confirm (that is 3/14 = SmsgCharSpawnResult). spec: Docs/RE/opcodes.md.
    /// </summary>
    public const uint SmsgSceneEntityUpdate = 0x30004;

    /// <summary>3:5 — enter-world acknowledgement (44 B). status: draft. spec: packets/3-5_enter_game_response.yaml.</summary>
    public const uint SmsgEnterGameAck = 0x30005;

    /// <summary>3:6 — character rename result. status: confirmed.</summary>
    public const uint SmsgRenameCharResult = 0x30006;

    /// <summary>
    /// 3:7 — char delete/select/rename MANAGE result (8-byte block). status: confirmed.
    /// De-swapped on build 263bd994 (Campaign 10): the minor-3 ladder routes minor 7 to this 8-byte
    /// manage/delete result; the variable scene-entity update is 3/4 and the 16-byte enter/spawn
    /// confirm is 3/14. spec: Docs/RE/opcodes.md.
    /// </summary>
    public const uint SmsgCharManageResult = 0x30007;

    /// <summary>3:8 — shop page update. status: confirmed.</summary>
    public const uint SmsgShopPageUpdate = 0x30008;

    /// <summary>3:13 — character status update. status: confirmed.</summary>
    public const uint SmsgCharStatusUpdate = 0x3000d;

    /// <summary>
    /// 3:14 — char enter-into-world bridge / spawn confirm response (16-byte block). status: confirmed.
    /// De-swapped on build 263bd994 (Campaign 10): the minor-3 ladder routes minor 14 to this 16-byte
    /// spawn-confirm handler (the catalog name is SmsgCharSpawnResponse; the C# struct/constant retain
    /// the SmsgCharSpawnResult identifier to avoid a cross-layer rename ripple — naming reconciliation
    /// is Tier-1-owned). The variable scene-entity update is 3/4; the 8-byte manage result is 3/7.
    /// NOTE: this is NOT the local-player world spawn (that is 4/1). spec: Docs/RE/opcodes.md.
    /// </summary>
    public const uint SmsgCharSpawnResult = 0x3000e;

    /// <summary>3:23 — character create result. status: confirmed.</summary>
    public const uint SmsgCharCreateResult = 0x30017;

    /// <summary>3:100 — generic character action result. status: confirmed.</summary>
    public const uint SmsgCharActionResult = 0x30064;

    /// <summary>3:50000 — GM chat / system message. status: confirmed.</summary>
    public const uint SmsgGmChatMessage = 0x3c350;

    // --- major 4: Response (S2C, table-driven) ---
    /// <summary>4:1 — periodic game-state tick. status: confirmed.</summary>
    public const uint SmsgGameStateTick = 0x40001;

    /// <summary>4:2 — game-tick configuration. status: confirmed.</summary>
    public const uint SmsgGameTickConfig = 0x40002;

    /// <summary>4:3 — billing info. status: confirmed.</summary>
    public const uint SmsgBillingInfo = 0x40003;

    /// <summary>4:4 — area entity snapshot. status: confirmed.</summary>
    public const uint SmsgAreaEntitySnapshot = 0x40004;

    /// <summary>4:5 — item-use result. status: confirmed.</summary>
    public const uint SmsgItemUseResult = 0x40005;

    /// <summary>4:12 — equip-item result. status: confirmed.</summary>
    public const uint SmsgEquipItemResult = 0x4000c;

    /// <summary>4:13 — local player state sync. status: confirmed.</summary>
    public const uint SmsgLocalPlayerStateSync = 0x4000d;

    /// <summary>4:14 — ground-item slot ack. status: confirmed.</summary>
    public const uint SmsgGroundItemSlotAck = 0x4000e;

    /// <summary>4:15 — world item pickup ack. status: confirmed.</summary>
    public const uint SmsgItemWorldPickupAck = 0x4000f;

    /// <summary>4:16 — equip change result. status: confirmed.</summary>
    public const uint SmsgEquipChangeResult = 0x40010;

    /// <summary>4:17 — quick-equip slot ack. status: confirmed.</summary>
    public const uint SmsgQuickEquipSlotAck = 0x40011;

    /// <summary>4:19 — NPC buy / acquire ack. status: confirmed.</summary>
    public const uint SmsgNpcBuyOrAcquireAck = 0x40013;

    /// <summary>4:20 — NPC sell-item ack. status: confirmed.</summary>
    public const uint SmsgNpcSellItemAck = 0x40014;

    /// <summary>4:21 — NPC shop slot clear ack. status: confirmed.</summary>
    public const uint SmsgNpcShopSlotClearAck = 0x40015;

    /// <summary>4:22 — item slot state ack. status: confirmed.</summary>
    public const uint SmsgItemSlotStateAck = 0x40016;

    /// <summary>4:23 — user trade request result. status: confirmed.</summary>
    public const uint SmsgUserTradeRequestResult = 0x40017;

    /// <summary>4:24 — user trade slot update. status: confirmed.</summary>
    public const uint SmsgUserTradeSlotUpdate = 0x40018;

    /// <summary>4:25 — full user-trade response. status: confirmed.</summary>
    public const uint SmsgUserTradeFullResponse = 0x40019;

    /// <summary>4:28 — respawn confirmation. status: confirmed.</summary>
    public const uint SmsgRespawnConfirm = 0x4001c;

    /// <summary>4:29 — stat update. status: confirmed.</summary>
    public const uint SmsgStatUpdate = 0x4001d;

    /// <summary>4:30 — social panel target. status: confirmed.</summary>
    public const uint SmsgSocialPanelTarget = 0x4001e;

    /// <summary>4:35 — party invite state. status: confirmed.</summary>
    public const uint SmsgPartyInviteState = 0x40023;

    /// <summary>4:36 — party member remove result. status: confirmed.</summary>
    public const uint SmsgPartyMemberRemoveResult = 0x40024;

    /// <summary>4:37 — party leader action result. status: confirmed.</summary>
    public const uint SmsgPartyLeaderActionResult = 0x40025;

    /// <summary>4:39 — thin discard/drain slot. status: confirmed.</summary>
    public const uint SmsgResponseDiscard39 = 0x40027;

    /// <summary>4:40 — thin UI-slot updater. status: confirmed.</summary>
    public const uint SmsgResponseSlot40 = 0x40028;

    /// <summary>4:41 — skill hotbar assign result. status: confirmed.</summary>
    public const uint SmsgSkillHotbarAssignResult = 0x40029;

    /// <summary>4:42 — player interaction state. status: confirmed.</summary>
    public const uint SmsgPlayerInteractionState = 0x4002a;

    /// <summary>4:43 — player interaction result. status: confirmed.</summary>
    public const uint SmsgPlayerInteractionResult = 0x4002b;

    /// <summary>4:44 — actor tick-table op A. status: confirmed.</summary>
    public const uint SmsgActorTickTableOpA = 0x4002c;

    /// <summary>4:45 — actor tick-table op B. status: confirmed.</summary>
    public const uint SmsgActorTickTableOpB = 0x4002d;

    /// <summary>4:46 — actor tick-table op C. status: confirmed.</summary>
    public const uint SmsgActorTickTableOpC = 0x4002e;

    /// <summary>4:47 — social acknowledgement drain. status: confirmed.</summary>
    public const uint SmsgSocialAckDrain = 0x4002f;

    /// <summary>4:48 — rank progress event. status: confirmed.</summary>
    public const uint SmsgRankProgressEvent = 0x40030;

    /// <summary>4:49 — rank progress panel update. status: confirmed.</summary>
    public const uint SmsgRankProgressPanelUpdate = 0x40031;

    /// <summary>4:50 — item upgrade result. status: confirmed.</summary>
    public const uint SmsgUpgradeItemResult = 0x40032;

    /// <summary>4:54 — guild rank slot update. status: confirmed.</summary>
    public const uint SmsgGuildRankSlotUpdate = 0x40036;

    /// <summary>4:55 — guild info update result. status: confirmed.</summary>
    public const uint SmsgGuildInfoUpdateResult = 0x40037;

    /// <summary>4:56 — thin UI-slot updater. status: confirmed.</summary>
    public const uint SmsgResponseSlot56 = 0x40038;

    /// <summary>4:57 — thin UI-slot updater. status: confirmed.</summary>
    public const uint SmsgResponseSlot57 = 0x40039;

    /// <summary>4:58 — thin UI-slot updater. status: confirmed.</summary>
    public const uint SmsgResponseSlot58 = 0x4003a;

    /// <summary>4:60 — thin UI-slot updater. status: confirmed.</summary>
    public const uint SmsgResponseSlot60 = 0x4003c;

    /// <summary>4:61 — guild state change result. status: confirmed.</summary>
    public const uint SmsgGuildStateChangeResult = 0x4003d;

    /// <summary>4:62 — guild invite/join state. status: confirmed.</summary>
    public const uint SmsgGuildInviteJoinState = 0x4003e;

    /// <summary>4:63 — guild member remove result. status: confirmed.</summary>
    public const uint SmsgGuildMemberRemoveResult = 0x4003f;

    /// <summary>4:64 — guild position change result. status: confirmed.</summary>
    public const uint SmsgGuildPositionChangeResult = 0x40040;

    /// <summary>4:65 — guild info full sync. status: confirmed.</summary>
    public const uint SmsgGuildInfoFullSync = 0x40041;

    /// <summary>4:66 — thin UI-slot updater. status: confirmed.</summary>
    public const uint SmsgResponseSlot66 = 0x40042;

    /// <summary>4:70 — thin UI-slot updater. status: confirmed.</summary>
    public const uint SmsgResponseSlot70 = 0x40046;

    /// <summary>4:71 — thin UI-slot updater. status: confirmed.</summary>
    public const uint SmsgResponseSlot71 = 0x40047;

    /// <summary>4:72 — thin UI-slot updater. status: confirmed.</summary>
    public const uint SmsgResponseSlot72 = 0x40048;

    /// <summary>4:74 — thin UI-slot updater. status: confirmed.</summary>
    public const uint SmsgResponseSlot74 = 0x4004a;

    /// <summary>4:75 — thin UI-slot updater. status: confirmed.</summary>
    public const uint SmsgResponseSlot75 = 0x4004b;

    /// <summary>4:76 — party accept result. status: confirmed.</summary>
    public const uint SmsgPartyAcceptResult = 0x4004c;

    /// <summary>4:78 — server time notification. status: confirmed.</summary>
    public const uint SmsgServerTimeNotification = 0x4004e;

    /// <summary>4:79 — crafting result. status: confirmed.</summary>
    public const uint SmsgCraftingResult = 0x4004f;

    /// <summary>4:80 — PvP death result. status: confirmed.</summary>
    public const uint SmsgPvpDeathResult = 0x40050;

    /// <summary>4:81 — action error result. status: confirmed.</summary>
    public const uint SmsgActionErrorResult = 0x40051;

    /// <summary>4:82 — billing balance update. status: confirmed.</summary>
    public const uint SmsgBillingBalanceUpdate = 0x40052;

    /// <summary>4:83 — billing item-use result. status: confirmed.</summary>
    public const uint SmsgBillingItemUseResult = 0x40053;

    /// <summary>4:84 — record slot consume/clear result. status: confirmed.</summary>
    public const uint SmsgRecordSlotConsumeResult = 0x40054;

    /// <summary>4:93 — user vote tally update. status: confirmed.</summary>
    public const uint SmsgUserVoteTallyUpdate = 0x4005d;

    /// <summary>4:95 — local player battle-mode update. status: confirmed.</summary>
    public const uint SmsgLocalPlayerBattleMode = 0x4005f;

    /// <summary>4:96 — actor guild roster entry update. status: confirmed.</summary>
    public const uint SmsgActorGuildRosterEntry = 0x40060;

    /// <summary>4:97 — area skill-effect result panel. status: confirmed.</summary>
    public const uint SmsgAreaSkillEffectPanel = 0x40061;

    /// <summary>4:99 — combat result message. status: confirmed.</summary>
    public const uint SmsgCombatResultMessage = 0x40063;

    /// <summary>4:100 — combat attack update. status: confirmed.</summary>
    public const uint SmsgCombatAttackUpdate = 0x40064;

    /// <summary>4:101 — global scalar-pair update. status: confirmed.</summary>
    public const uint SmsgGlobalScalarPairUpdate = 0x40065;

    /// <summary>4:102 — skill window state update. status: confirmed.</summary>
    public const uint SmsgSkillWindowStateUpdate = 0x40066;

    /// <summary>4:103 — guild panel text update. status: confirmed.</summary>
    public const uint SmsgGuildPanelTextUpdate = 0x40067;

    /// <summary>4:105 — gage panel progress update. status: confirmed.</summary>
    public const uint SmsgGagePanelProgressUpdate = 0x40069;

    /// <summary>4:107 — event lottery number-pick result. status: confirmed.</summary>
    public const uint SmsgEventLotteryPickResult = 0x4006b;

    /// <summary>4:108 — player gold balance update. status: confirmed.</summary>
    public const uint SmsgPlayerGoldBalanceUpdate = 0x4006c;

    /// <summary>4:109 — local actor skill-state flag. status: confirmed.</summary>
    public const uint SmsgLocalActorSkillStateFlag = 0x4006d;

    /// <summary>4:113 — item-shop purchase result. status: confirmed.</summary>
    public const uint SmsgItemShopPurchaseResult = 0x40071;

    /// <summary>4:114 — cash-shop action result. status: confirmed.</summary>
    public const uint SmsgCashShopActionResult = 0x40072;

    /// <summary>4:115 — item-shop balance update result. status: confirmed.</summary>
    public const uint SmsgItemShopBalanceUpdate = 0x40073;

    /// <summary>4:120 — actor table batch result. status: confirmed.</summary>
    public const uint SmsgActorTableBatchResult = 0x40078;

    /// <summary>4:122 — thin UI-slot updater. status: confirmed.</summary>
    public const uint SmsgResponseSlot122 = 0x4007a;

    /// <summary>4:123 — thin UI-slot updater. status: confirmed.</summary>
    public const uint SmsgResponseSlot123 = 0x4007b;

    /// <summary>4:125 — thin UI-slot updater. status: confirmed.</summary>
    public const uint SmsgResponseSlot125 = 0x4007d;

    /// <summary>4:126 — thin UI-slot updater. status: confirmed.</summary>
    public const uint SmsgResponseSlot126 = 0x4007e;

    /// <summary>4:132 — GM notice / error. status: confirmed.</summary>
    public const uint SmsgGmNoticeError = 0x40084;

    /// <summary>4:133 — rank progress update. status: confirmed.</summary>
    public const uint SmsgRankProgressUpdate = 0x40085;

    /// <summary>4:134 — stat change notify. status: confirmed.</summary>
    public const uint SmsgStatChangeNotify = 0x40086;

    /// <summary>4:135 — thin UI-slot updater. status: confirmed.</summary>
    public const uint SmsgResponseSlot135 = 0x40087;

    /// <summary>4:137 — gathering result. status: confirmed.</summary>
    public const uint SmsgGatheringResult = 0x40089;

    /// <summary>4:138 — notice / error. status: confirmed.</summary>
    public const uint SmsgNoticeError = 0x4008a;

    /// <summary>4:139 — item-use effect. status: confirmed.</summary>
    public const uint SmsgItemUseEffect = 0x4008b;

    /// <summary>4:140 — coloured system text. status: confirmed.</summary>
    public const uint SmsgColoredSystemText = 0x4008c;

    /// <summary>4:142 — thin UI-slot updater. status: confirmed.</summary>
    public const uint SmsgResponseSlot142 = 0x4008e;

    /// <summary>4:146 — show message (string id 51027). status: confirmed.</summary>
    public const uint SmsgShowMessage51027 = 0x40092;

    /// <summary>4:148 — playtime reward result. status: confirmed.</summary>
    public const uint SmsgPlaytimeRewardResult = 0x40094;

    /// <summary>4:149 — item panel slot chunk. status: confirmed.</summary>
    public const uint SmsgItemPanelSlotChunk = 0x40095;

    /// <summary>4:150 — skill point update. status: confirmed.</summary>
    public const uint SmsgSkillPointUpdate = 0x40096;

    /// <summary>4:151 — thin UI-slot updater. status: confirmed.</summary>
    public const uint SmsgResponseSlot151 = 0x40097;

    /// <summary>4:152 — thin UI-slot updater. status: confirmed.</summary>
    public const uint SmsgResponseSlot152 = 0x40098;

    /// <summary>4:153 — item panel slot refresh. status: confirmed.</summary>
    public const uint SmsgItemPanelSlotRefresh = 0x40099;

    /// <summary>4:500 — special: shows a popup by code (routed outside the table). status: confirmed.</summary>
    public const uint SmsgShowPopupByCode = 0x401f4;

    /// <summary>4:50000 — special: discards a text payload (routed outside the table). status: confirmed.</summary>
    public const uint SmsgResponseDiscardText = 0x4c350;

    // --- major 5: Push (S2C, table-driven world events) ---
    /// <summary>5:0 — actor leaves the world (12 B). status: draft. spec: packets/5-0_char_despawn.yaml.</summary>
    public const uint SmsgCharDespawn = 0x50000;

    /// <summary>5:1 — extended actor spawn. status: confirmed.</summary>
    public const uint SmsgActorSpawnExtended = 0x50001;

    /// <summary>5:3 — actor enters the world (908 B; SpawnDescriptor). status: draft. spec: packets/5-3_char_spawn.yaml.</summary>
    public const uint SmsgCharSpawn = 0x50003;

    /// <summary>5:5 — actor state event. status: confirmed.</summary>
    public const uint SmsgActorStateEvent = 0x50005;

    /// <summary>5:6 — actor auto-target / motion. status: confirmed.</summary>
    public const uint SmsgActorAutotargetOrMotion = 0x50006;

    /// <summary>5:7 — chat broadcast. status: draft.</summary>
    public const uint SmsgChatBroadcast = 0x50007;

    /// <summary>5:9 — experience gain. status: confirmed.</summary>
    public const uint SmsgExpGain = 0x50009;

    /// <summary>5:10 — character death. status: confirmed.</summary>
    public const uint SmsgCharDeath = 0x5000a;

    /// <summary>5:11 — rank XP gain. status: confirmed.</summary>
    public const uint SmsgRankXpGain = 0x5000b;

    /// <summary>5:12 — actor visual slot set. status: confirmed.</summary>
    public const uint SmsgActorVisualSlotSet = 0x5000c;

    /// <summary>5:13 — actor movement update (40 B). status: draft. spec: packets/5-13_actor_movement_update.yaml.</summary>
    public const uint SmsgActorMovementUpdate = 0x5000d;

    /// <summary>5:14 — combat effect instance spawn. status: confirmed.</summary>
    public const uint SmsgCombatEffectInstanceSpawn = 0x5000e;

    /// <summary>5:15 — tracked world object remove. status: confirmed.</summary>
    public const uint SmsgTrackedWorldObjectRemove = 0x5000f;

    /// <summary>5:16 — actor visual slot clear. status: confirmed.</summary>
    public const uint SmsgActorVisualSlotClear = 0x50010;

    /// <summary>5:18 — game clock update. status: confirmed.</summary>
    public const uint SmsgGameClockUpdate = 0x50012;

    /// <summary>5:21 — party roster event. status: confirmed.</summary>
    public const uint SmsgPartyRosterEvent = 0x50015;

    /// <summary>5:26 — local player relation slot update. status: confirmed.</summary>
    public const uint SmsgLocalPlayerRelationSlot = 0x5001a;

    /// <summary>5:28 — respawn at point. status: confirmed.</summary>
    public const uint SmsgRespawnAtPoint = 0x5001c;

    /// <summary>5:31 — buff slot update. status: confirmed.</summary>
    public const uint SmsgBuffSlotUpdate = 0x5001f;

    /// <summary>5:32 — level up. status: confirmed.</summary>
    public const uint SmsgLevelUp = 0x50020;

    /// <summary>5:33 — skill hotbar slot set. status: confirmed.</summary>
    public const uint SmsgSkillHotbarSlotSet = 0x50021;

    /// <summary>5:34 — billing banner toggle. status: confirmed.</summary>
    public const uint SmsgBillingBannerToggle = 0x50022;

    /// <summary>5:38 — party member stats. status: confirmed.</summary>
    public const uint SmsgPartyMemberStats = 0x50026;

    /// <summary>5:39 — no-op slot. status: confirmed.</summary>
    public const uint SmsgPushNop39 = 0x50027;

    /// <summary>5:42 — player-pair system notice. status: confirmed.</summary>
    public const uint SmsgPlayerPairSystemNotice = 0x5002a;

    /// <summary>5:51 — skill guide state. status: confirmed.</summary>
    public const uint SmsgSkillGuideState = 0x50033;

    /// <summary>5:52 — actor skill action / combat result. status: draft.</summary>
    public const uint SmsgActorSkillAction = 0x50034;

    /// <summary>5:53 — actor vitals (HP/MP/stamina) and pair state. status: confirmed.</summary>
    public const uint SmsgActorVitalsAndPairState = 0x50035;

    /// <summary>5:55 — guild name display update. status: confirmed.</summary>
    public const uint SmsgGuildNameDisplayUpdate = 0x50037;

    /// <summary>5:57 — tracked panel slot update. status: confirmed.</summary>
    public const uint SmsgTrackedPanelSlotUpdate = 0x50039;

    /// <summary>5:59 — rank progress SFX event. status: confirmed.</summary>
    public const uint SmsgRankProgressSfxEvent = 0x5003b;

    /// <summary>5:61 — actor name overlay set. status: confirmed.</summary>
    public const uint SmsgActorNameOverlaySet = 0x5003d;

    /// <summary>5:64 — remote actor relation pair update. status: confirmed.</summary>
    public const uint SmsgRemoteActorRelationPair = 0x50040;

    /// <summary>5:65 — guild member roster update. status: confirmed.</summary>
    public const uint SmsgGuildMemberRosterUpdate = 0x50041;

    /// <summary>5:67 — stats update. status: confirmed.</summary>
    public const uint SmsgStatsUpdate = 0x50043;

    /// <summary>5:68 — quest list. status: confirmed.</summary>
    public const uint SmsgQuestList = 0x50044;

    /// <summary>5:73 — quest complete. status: confirmed.</summary>
    public const uint SmsgQuestComplete = 0x50049;

    /// <summary>5:76 — party member joined. status: confirmed.</summary>
    public const uint SmsgPartyMemberJoined = 0x5004c;

    /// <summary>5:77 — rank progress panel bulk. status: confirmed.</summary>
    public const uint SmsgRankProgressPanelBulk = 0x5004d;

    /// <summary>5:79 — actor death state. status: confirmed.</summary>
    public const uint SmsgActorDeathState = 0x5004f;

    /// <summary>5:80 — PvP death FX. status: confirmed.</summary>
    public const uint SmsgPvpDeathFx = 0x50050;

    /// <summary>5:85 — dungeon event state sync A. status: confirmed.</summary>
    public const uint SmsgDungeonEventStateSyncA = 0x50055;

    /// <summary>5:86 — dungeon event state sync B. status: confirmed.</summary>
    public const uint SmsgDungeonEventStateSyncB = 0x50056;

    /// <summary>5:87 — dungeon event actor state clear. status: confirmed.</summary>
    public const uint SmsgDungeonEventActorClear = 0x50057;

    /// <summary>5:88 — PvP state bytes. status: confirmed.</summary>
    public const uint SmsgPvpStateBytes = 0x50058;

    /// <summary>5:89 — PvP revenge roster. status: confirmed.</summary>
    public const uint SmsgPvpRevengeRoster = 0x50059;

    /// <summary>5:90 — PvP counters. status: confirmed.</summary>
    public const uint SmsgPvpCounters = 0x5005a;

    /// <summary>5:91 — PvP score update. status: confirmed.</summary>
    public const uint SmsgPvpScoreUpdate = 0x5005b;

    /// <summary>5:92 — PvP request / notice. status: confirmed.</summary>
    public const uint SmsgPvpRequestOrNotice = 0x5005c;

    /// <summary>5:93 — actor mob-id change. status: confirmed.</summary>
    public const uint SmsgActorMobIdChange = 0x5005d;

    /// <summary>5:94 — vote result. status: confirmed.</summary>
    public const uint SmsgVoteResult = 0x5005e;

    /// <summary>5:98 — actor class/form refresh. status: confirmed.</summary>
    public const uint SmsgActorClassFormRefresh = 0x50062;

    /// <summary>5:106 — trade state toggle. status: confirmed.</summary>
    public const uint SmsgTradeStateToggle = 0x5006a;

    /// <summary>5:121 — character property set. status: confirmed.</summary>
    public const uint SmsgCharPropertySet = 0x50079;

    /// <summary>5:123 — gift character receive confirm. status: confirmed.</summary>
    public const uint SmsgGiftCharReceiveConfirm = 0x5007b;

    /// <summary>5:124 — actor visual flags set. status: confirmed.</summary>
    public const uint SmsgActorVisualFlagsSet = 0x5007c;

    /// <summary>5:126 — actor stance set. status: confirmed.</summary>
    public const uint SmsgActorStanceSet = 0x5007e;

    /// <summary>5:127 — stealth toggle. status: confirmed.</summary>
    public const uint SmsgStealthToggle = 0x5007f;

    /// <summary>5:129 — monster notice by mob id. status: confirmed.</summary>
    public const uint SmsgMonsterNoticeByMobId = 0x50081;

    /// <summary>5:130 — monster notice with text. status: confirmed.</summary>
    public const uint SmsgMonsterNoticeWithText = 0x50082;

    /// <summary>5:131 — PvP rank score update. status: confirmed.</summary>
    public const uint SmsgPvpRankScoreUpdate = 0x50083;

    /// <summary>5:136 — actor timed-state update. status: confirmed.</summary>
    public const uint SmsgActorTimedStateUpdate = 0x50088;

    /// <summary>5:139 — attack effect. status: confirmed.</summary>
    public const uint SmsgAttackEffect = 0x5008b;

    /// <summary>5:146 — packet response / ack request. status: confirmed.</summary>
    public const uint SmsgPacketResponseAckRequest = 0x50092;

    /// <summary>5:147 — actor combat-flag update. status: confirmed.</summary>
    public const uint SmsgActorCombatFlagUpdate = 0x50093;
}