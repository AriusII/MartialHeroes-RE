using Godot;
using MartialHeroes.Client.Godot.Ui.Assets;

namespace MartialHeroes.Client.Godot.Ui.Hud;

public sealed partial class HudAnnouncePanel : Control
{
    private const int LabelCount = 8;
    private const float LabelW = 110f;
    private const float LabelH = 12f;
    private const float LabelSpacing = 13f;


    private readonly Label[] _labels = new Label[LabelCount];
    private int _nextSlot;
    private double _scrollTimer;
    private HudTextLibrary? _text;


    public void Build()
    {
        Name = "HudAnnouncePanel";

        AnchorLeft = 0.5f;
        AnchorTop = 0f;
        AnchorRight = 0.5f;
        AnchorBottom = 0f;
        OffsetLeft = -LabelW / 2f;
        OffsetTop = 60f;
        OffsetRight = LabelW / 2f;
        OffsetBottom = 60f + LabelH * LabelCount + LabelSpacing * (LabelCount - 1);

        MouseFilter = MouseFilterEnum.Ignore;

        for (var i = 0; i < LabelCount; i++)
        {
            _labels[i] = new Label
            {
                Name = $"AnnounceLabel{i}",
                Text = string.Empty,
                HorizontalAlignment = HorizontalAlignment.Center,
                Position = new Vector2(0f, i * LabelSpacing),
                Size = new Vector2(LabelW, LabelH),
                MouseFilter = MouseFilterEnum.Ignore,
                Visible = false
            };
            AddChild(_labels[i]);
        }

        Visible = false;

        GD.Print("[HudAnnouncePanel] Built — scrolling announce banner slot 221 (8×110×12 labels, " +
                 "two batches 3+5, no atlas, non-interactive; no buttons/close per §8.25.1). " +
                 "Global notice sink (§8.25.3) wired upstream — this is the leaf banner delegate fed by method call " +
                 "(same pattern as HudErrorPanel; no self-subscribe, no input to route): " +
                 "4/500 SmsgShowPopupByCode → PopupCodeEvent → GameLoop drain → HudMaster.OnPopupCode → ShowPopupCode (caption via text library); " +
                 "4/132/4/138 notice family → ActionErrorEvent → HudMaster.OnActionError → HudErrorPanel.ShowActionError/ShowError → ShowAnnounce; " +
                 "HudMaster.ShowAnnounce → ShowAnnounce. " +
                 "No direct IHudEventHub sub: no dedicated notice channel exists and ChatLines is SingleReader (owned by HudChatPanel). " +
                 "spec: Docs/RE/specs/ui_system.md §8.25.1/§8.25.3 / chat.md / social.md CODE-CONFIRMED.");
    }


    public void SetTextLibrary(HudTextLibrary text)
    {
        _text = text;
    }

    public void ShowPopupCode(uint popupCode)
    {
        var msg = _text?.GetCaption((int)popupCode);
        if (!string.IsNullOrEmpty(msg))
            ShowAnnounce(msg);
    }

    public void ShowAnnounce(string text)
    {
        if (string.IsNullOrEmpty(text)) return;

        var slot = _nextSlot % LabelCount;
        _nextSlot = (_nextSlot + 1) % LabelCount;

        if (_labels[slot] != null)
        {
            _labels[slot].Text = text;
            _labels[slot].Visible = true;
        }

        Visible = true;
        _scrollTimer = 0.0;

        GD.Print($"[HudAnnouncePanel] ShowAnnounce slot={slot}: \"{text}\". " +
                 "spec: Docs/RE/specs/ui_system.md §8.25.1 CODE-CONFIRMED.");
    }


    public override void _Process(double delta)
    {
        if (!Visible) return;

        _scrollTimer += delta;
        if (_scrollTimer > 8.0)
        {
            _scrollTimer = 0.0;
            var oldest = _nextSlot % LabelCount;
            if (_labels[oldest] != null) _labels[oldest].Visible = false;

            var anyVisible = false;
            foreach (var lbl in _labels)
                if (lbl.Visible)
                {
                    anyVisible = true;
                    break;
                }

            if (!anyVisible) Visible = false;
        }
    }
}