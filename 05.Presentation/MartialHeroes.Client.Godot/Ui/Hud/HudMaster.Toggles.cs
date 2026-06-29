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
    }

    public void ToggleMap()
    {
        _minimapPanel?.ToggleCollapse();
    }

    public void ToggleGuildN()
    {
        _guildWindow?.Toggle();
    }

    public void ToggleNpcDialog()
    {
    }

    public void CloseAll()
    {
        if (_inventoryPanel?.Visible == true) _inventoryPanel.Toggle();
        if (_skillPanel?.Visible == true) _skillPanel.Toggle();
        if (_statsPanel?.Visible == true) _statsPanel.Toggle();
        _optionsWindow?.Toggle(false);
        _partyWindow?.Toggle(false);
        _friendWindow?.Toggle(false);
        _guildWindow?.Toggle(false);
        _questWindow?.Toggle(false);
        _productWindow?.Toggle(false);
        _emoticonWindow?.Toggle(false);
        _tenderWindow?.Toggle(false);
        _mailWindow?.Toggle(false);
        _deliveryWindow?.Toggle(false);
        _helpOverlay?.Toggle(false);
        if (_vendorWindow?.Visible == true) _vendorWindow.Toggle(false);
        _stallListWindow?.Toggle(false);
        _guildDiplomacyWindow?.Toggle(false);
        _guildWarInfoWindow?.Toggle(false);
        _relationPanel?.Toggle(false);
    }

    public void ToggleMiscW()
    {
    }

    public void ToggleMiscZ()
    {
    }

    public void OnSectorLoaded(int mapX, int mapZ)
    {
        _minimapPanel?.OnSectorLoaded(mapX, mapZ);
    }

    public void OnSectorUnloaded(int mapX, int mapZ)
    {
        _minimapPanel?.OnSectorUnloaded(mapX, mapZ);
    }

    public void OnWorldArea(int areaId)
    {
        _minimapPanel?.SetAreaId(areaId);
    }

    public void UpdateMinimapPlayerPosition(float worldX, float worldZ)
    {
        _minimapPanel?.UpdateLocalPlayerPosition(worldX, worldZ);
    }
}