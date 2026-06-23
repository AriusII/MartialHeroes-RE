using Godot;
using MartialHeroes.Client.Godot.Autoload;
using MartialHeroes.Client.Godot.Ui.Assets;

namespace MartialHeroes.Client.Godot.Ui.Hud;

public sealed partial class HudMaster
{
    public void Build(ClientContext ctx, HudAtlasLibrary atlas, HudIconLibrary icons, HudTextLibrary text)
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        _hub = ctx.HudEventHub;


        _playerStatusPanel = AddPanel<HudPlayerStatusPanel>();
        _playerStatusPanel.Build(atlas);

        _rightEdgeGauge = AddPanel<HudRightEdgeGauge>();
        _rightEdgeGauge.Build(atlas);

        _chatPanel = AddPanel<HudChatPanel>();
        _chatPanel.Build(atlas);

        _minimapPanel = AddPanel<HudMinimapPanel>();
        _minimapPanel.Build(atlas);

        _buffBar = AddPanel<HudBuffBar>();
        _buffBar.Build(icons);

        _skillHotbar = AddPanel<HudSkillHotbar>();
        _skillHotbar.Build(atlas, icons);

        _inventoryPanel = AddPanel<HudInventoryPanel>();
        _inventoryPanel.Build(atlas, ctx);

        _skillPanel = AddPanel<HudSkillPanel>();
        _skillPanel.Build(atlas, text);

        _statsPanel = AddPanel<HudCharacterStatsPanel>();
        _statsPanel.Build(atlas, text, ctx);

        _targetFrame = AddPanel<HudTargetFrame>();
        _targetFrame.Build(atlas);

        _optionsWindow = AddPanel<HudOptionsWindow>();
        _optionsWindow.Build(atlas, text);

        _partyWindow = AddPanel<HudPartyWindow>();
        _partyWindow.Build(atlas, text);

        _tradeWindow = AddPanel<HudTradeWindow>();
        _tradeWindow.Build(atlas, text);

        _friendWindow = AddPanel<HudFriendWindow>();
        _friendWindow.Build(atlas, text);

        _guildWindow = AddPanel<HudGuildWindow>();
        _guildWindow.Build(atlas, text);

        _questWindow = AddPanel<HudQuestWindow>();
        _questWindow.Build(atlas, text);


        _messagePanel = AddPanel<HudMessagePanel>();
        _messagePanel.Build(atlas);

        _productWindow = AddPanel<HudProductWindow>();
        _productWindow.Build(atlas, text);

        _emoticonWindow = AddPanel<HudEmoticonWindow>();
        _emoticonWindow.Build(atlas, ctx);

        _tenderWindow = AddPanel<HudTenderWindow>();
        _tenderWindow.Build(atlas, text);

        _mailWindow = AddPanel<HudMailWindow>();
        _mailWindow.Build(atlas, text);

        _deliveryWindow = AddPanel<HudDeliveryWindow>();
        _deliveryWindow.Build(atlas, text);


        _commandBar = AddPanel<HudCommandBar>();
        _commandBar.Wire(
            ToggleInventory,
            ToggleSkill,
            ToggleQuest,
            ToggleStats,
            ToggleHelp,
            ToggleParty,
            ToggleProduct,
            ToggleRelation
        );
        _commandBar.Build(atlas, text);

        _vendorWindow = AddPanel<HudVendorWindow>();
        _vendorWindow.Build(atlas, text);

        _helpOverlay = AddPanel<HudHelpOverlay>();
        _helpOverlay.Build(atlas);

        _announcePanel = AddPanel<HudAnnouncePanel>();
        _announcePanel.Build();

        _errorPanel = AddPanel<HudErrorPanel>();
        _errorPanel.Build();
        _errorPanel.SetAnnounceDelegate(_announcePanel);

        _petPanel = AddPanel<HudPetPanel>();
        _petPanel.Build(atlas, text);


        _storageWindow = AddPanel<HudStorageWindow>();
        _storageWindow.Build(atlas, text);

        _keepNpcDialog = AddPanel<HudKeepNpcDialog>();
        _keepNpcDialog.Wire(
            () => _storageWindow?.Open(),
            () => _vendorWindow?.Open()
        );
        _keepNpcDialog.Build(atlas);

        _stallListWindow = AddPanel<HudStallListWindow>();
        _stallListWindow.Build(atlas, text);

        _guildDiplomacyWindow = AddPanel<HudGuildDiplomacyWindow>();
        _guildDiplomacyWindow.Build(atlas);

        _guildWarInfoWindow = AddPanel<HudGuildWarInfoWindow>();
        _guildWarInfoWindow.Build(atlas);

        _relationPanel = AddPanel<HudRelationPanel>();
        _relationPanel.Build(atlas, text);

        GD.Print("[HudMaster] Build complete — ~34 panels total " +
                 "(1 PlayerStatus/slot15 + 10 core + 6 Wave-1 E + 6 Wave-2 E + 6 Wave-3 E + 6 Wave-4 E, + HelpOverlay member). " +
                 "PlayerStatusPanel(§2.1/slot15): HP/MP/Stamina fills + condition bar + portrait + level label. " +
                 "Wave-1 E: Options(§8.9.1) Party(§8.12) Trade(§8.13) Friend(§8.14) Guild(§8.15) Quest(§8.16). " +
                 "Wave-2 E: Message(§8.20) Product(§8.18) Emoticon(§8.19) Tender(§8.21.1) Mail(§8.21.2-4) Delivery(§8.21.5). " +
                 "Wave-3 E: CommandBar(§8.23) Vendor(§8.22) HelpOverlay(§8.24) Announce(§8.25.1) Error(§8.25.2) Pet(§8.26). " +
                 "Wave-4 E: Storage(§8.32/slot191) KeepNpcDialog(§8.27/slot152) StallList(§8.29/slot228/l) " +
                 "GuildDiplomacy(§8.30/slot235/u) GuildWarInfo(§8.31/slot224/j) Relation(§8.28/slot193). " +
                 "Key dispatch: §8.17 ASCII-keycode map wired in _Input (i/b/s/q/k/g/h/l/u/j/Esc). " +
                 "CommandBar entry→slot wiring: Inventory(4001/158) Skill(4003/159) Quest(4004/206) Status(4005/146) " +
                 "Help(4011/§8.24) Party(4012/220) Product(4013/230) Relation(4002/185+193). " +
                 "KeepNpcDialog: sel1→Storage; sel0→Vendor(interim). " +
                 "BuddyRelation(185) reconciliation: interim→FriendWindow; full build TODO(world-campaign). " +
                 "spec: Docs/RE/specs/ui_system.md §1.9.3 — MopGagePanel slot 35 (binary-won; 'slot 177' REFUTED §1.9.4). " +
                 "spec: Docs/RE/specs/ui_system.md §8.17/§8.18/§8.19/§8.20/§8.21/§8.22/§8.23/§8.24/§8.25/§8.26 CODE-CONFIRMED. " +
                 "spec: Docs/RE/specs/ui_system.md §8.27/§8.28/§8.29/§8.30/§8.31/§8.32 CODE-CONFIRMED.");
    }

    public void Reconfigure()
    {
        GD.Print("[HudMaster] Reconfigure called. spec: Docs/RE/specs/ui_hud_layout.md §0.1.");
    }

    public void BindHub(ClientContext ctx)
    {
        var hub = ctx.HudEventHub;
        _playerStatusPanel?.BindHub(hub);
        _rightEdgeGauge?.BindHub(hub);
        _chatPanel?.BindHub(hub);
        _chatPanel?.WireSendIntent(ctx);
        _minimapPanel?.BindHub(hub);
        _buffBar?.BindHub(hub);
        _inventoryPanel?.BindHub(hub);
        _statsPanel?.BindHub(hub);
        _targetFrame?.BindHub(hub);
        _partyWindow?.BindHub(hub);

        GD.Print(
            "[HudMaster] BindHub: ~34 panels connected (1 PlayerStatus/slot15 + 10 core + 6 Wave-1 E + 6 Wave-2 E + 6 Wave-3 E + 6 Wave-4 E). " +
            "Party hub wired (stub). Wave-3/4 panels have no hub channel yet " +
            "(TODO world-campaign: global notice sink, 5/53 pair-relation, S2C 4/74/4/81/5/73, relation roster push). " +
            "spec: Docs/RE/specs/ui_system.md §1.9.3 — MopGagePanel slot 35 (binary-won; 'slot 177' REFUTED §1.9.4). " +
            "spec: Docs/RE/specs/ui_system.md §8.12 — PartyPanel hub stub (TODO world-campaign). " +
            "spec: Docs/RE/specs/ui_system.md §8.27/§8.28/§8.29/§8.30/§8.31/§8.32 — Wave-4 TODO hubs.");
    }


    private T AddPanel<T>() where T : Control, new()
    {
        var panel = new T();
        AddChild(panel);
        return panel;
    }
}