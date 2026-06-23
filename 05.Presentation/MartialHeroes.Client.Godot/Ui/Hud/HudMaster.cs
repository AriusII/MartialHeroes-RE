
using Godot;
using MartialHeroes.Client.Application.Contracts.Hud;

namespace MartialHeroes.Client.Godot.Ui.Hud;

public sealed partial class HudMaster : Control
{
    private HudAnnouncePanel? _announcePanel;
    private HudBuffBar? _buffBar;
    private HudChatPanel? _chatPanel;


    private HudCommandBar? _commandBar;

    private HudDeliveryWindow? _deliveryWindow;

    private HudEmoticonWindow? _emoticonWindow;

    private HudErrorPanel? _errorPanel;

    private HudFriendWindow? _friendWindow;

    private HudGuildDiplomacyWindow? _guildDiplomacyWindow;

    private HudGuildWarInfoWindow? _guildWarInfoWindow;

    private HudGuildWindow? _guildWindow;

    private HudHelpOverlay? _helpOverlay;


    private IHudEventHub? _hub;

    private HudInventoryPanel? _inventoryPanel;


    private HudKeepNpcDialog? _keepNpcDialog;

    private HudMailWindow? _mailWindow;


    private HudMessagePanel? _messagePanel;
    private HudMinimapPanel? _minimapPanel;


    private HudOptionsWindow? _optionsWindow;

    private HudPartyWindow? _partyWindow;

    private HudPetPanel? _petPanel;

    private HudPlayerStatusPanel? _playerStatusPanel;

    private HudProductWindow? _productWindow;

    private HudQuestWindow? _questWindow;

    private HudRelationPanel? _relationPanel;

    private HudRightEdgeGauge? _rightEdgeGauge;
    private HudSkillHotbar? _skillHotbar;
    private HudSkillPanel? _skillPanel;

    private HudStallListWindow? _stallListWindow;
    private HudCharacterStatsPanel? _statsPanel;

    private HudStorageWindow? _storageWindow;

    private HudTargetFrame? _targetFrame;

    private HudTenderWindow? _tenderWindow;

    private HudTradeWindow? _tradeWindow;

    private HudVendorWindow? _vendorWindow;


    public override void _Process(double delta)
    {
    }


    public bool HitTest(int x, int y)
    {
        var pt = new Vector2(x, y);
        for (var i = 0; i < GetChildCount(); i++)
            if (GetChild(i) is Control c && c.Visible && c.GetRect().HasPoint(pt))
                return true;

        return false;
    }
}