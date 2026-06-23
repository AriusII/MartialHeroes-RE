
using Godot;

namespace MartialHeroes.Client.Godot.Ui.Hud;

public sealed partial class HudMaster
{
    public void ToggleInventory()
    {
        _inventoryPanel?.Toggle();
        _skillPanel?.Toggle();
    }

    public void ToggleSkill()
    {
        _skillPanel?.Toggle();
    }

    public void ToggleStats()
    {
        _statsPanel?.Toggle();
    }


    public void ToggleOptions()
    {
        _optionsWindow?.Toggle();
    }

    public void ToggleParty()
    {
        _partyWindow?.Toggle();
    }

    public void ShowTrade(bool show)
    {
        _tradeWindow?.Toggle(show);
    }

    public void ToggleFriend()
    {
        _friendWindow?.Toggle();
    }

    public void ToggleGuild()
    {
        _guildWindow?.Toggle();
    }

    public void ToggleQuest()
    {
        _questWindow?.Toggle();
    }


    public void ToggleMessage(bool? forceState = null)
    {
        if (forceState.HasValue && !forceState.Value)
            _messagePanel?.ShowNotice(string.Empty);
    }

    public void ShowNotice(string text)
    {
        _messagePanel?.ShowNotice(text);
    }

    public void ShowConfirm(string text, Action? onYes = null, Action? onNo = null)
    {
        _messagePanel?.ShowConfirm(text, onYes, onNo);
    }

    public void ToggleProduct()
    {
        _productWindow?.Toggle();
    }

    public void ToggleEmoticon()
    {
        _emoticonWindow?.Toggle();
    }

    public void ShowTender(bool show)
    {
        _tenderWindow?.Toggle(show);
    }

    public void ShowMail(bool show)
    {
        _mailWindow?.Toggle(show);
    }

    public void ShowDelivery(bool show)
    {
        _deliveryWindow?.Toggle(show);
    }


    public void ToggleHelp()
    {
        _helpOverlay?.Toggle();
    }

    public void ShowVendor(uint npcId = 0)
    {
        _vendorWindow?.Open(npcId);
    }

    public void ShowAnnounce(string text)
    {
        _announcePanel?.ShowAnnounce(text);
    }

    public void ShowError(string text, double seconds = 5.0)
    {
        _errorPanel?.ShowError(text, seconds);
    }

    public void ShowPetPanel(string partnerName, int partnerLevel, float gauge0 = 0f, float gauge1 = 0f)
    {
        _petPanel?.ShowPartner(partnerName, partnerLevel, gauge0, gauge1);
    }

    public void ClearPetPanel()
    {
        _petPanel?.ClearPartner();
    }


    public void ShowKeepNpcDialog(uint npcId = 0)
    {
        _keepNpcDialog?.Open(npcId);
    }

    public void ShowStorage()
    {
        _storageWindow?.Open();
    }

    public void ToggleStallList()
    {
        _stallListWindow?.Toggle();
    }

    public void ToggleGuildDiplomacy()
    {
        _guildDiplomacyWindow?.Toggle();
    }

    public void ToggleGuildWarInfo()
    {
        _guildWarInfoWindow?.Toggle();
    }

    public void ToggleRelation()
    {
        _friendWindow?.Toggle();
        _relationPanel?.Toggle();
        GD.Print("[HudMaster] ToggleRelation → RelationPanel(193) toggled; " +
                 "BuddyRelation(185) interim→FriendWindow (TODO world-campaign: HudBuddyRelation). " +
                 "spec: Docs/RE/specs/ui_system.md §8.28.5 CODE-CONFIRMED.");
    }
}