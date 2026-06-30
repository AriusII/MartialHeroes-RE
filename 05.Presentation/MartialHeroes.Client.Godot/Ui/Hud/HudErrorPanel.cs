using Godot;
using MartialHeroes.Client.Godot.Ui.Assets;

namespace MartialHeroes.Client.Godot.Ui.Hud;

public sealed partial class HudErrorPanel : Control
{
    private const float PanelW = 330f;
    private const float PanelH = 100f;
    private const int DefaultTimeoutMs = 5000;
    private HudAnnouncePanel? _announceDelegate;
    private Label? _countdownLabel;
    private HudTextLibrary? _text;

    private int _lastDisplayedSec = -1;


    private Label? _messageLabel;
    private Button? _okButton;


    private bool _open;
    private double _remainingSecs;


    public void Build()
    {
        Name = "HudErrorPanel";

        AnchorLeft = 0.5f;
        AnchorTop = 0f;
        AnchorRight = 0.5f;
        AnchorBottom = 0f;
        OffsetLeft = -PanelW / 2f;
        OffsetTop = 300f;
        OffsetRight = PanelW / 2f;
        OffsetBottom = 300f + PanelH;

        Visible = false;
        _open = false;
        MouseFilter = MouseFilterEnum.Stop;

        var backdrop = new Panel { Name = "Backdrop" };
        backdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var bdStyle = new StyleBoxFlat();
        bdStyle.BgColor = new Color(0.06f, 0.04f, 0.04f, 0.95f);
        bdStyle.SetBorderWidthAll(2);
        bdStyle.BorderColor = new Color(0.80f, 0.20f, 0.10f, 0.9f);
        backdrop.AddThemeStyleboxOverride("panel", bdStyle);
        AddChild(backdrop);

        _messageLabel = new Label
        {
            Name = "MessageLabel",
            Text = string.Empty,
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.Word,
            Position = new Vector2(10f, 15f),
            Size = new Vector2(PanelW - 20f, 50f),
            MouseFilter = MouseFilterEnum.Ignore
        };
        AddChild(_messageLabel);

        _countdownLabel = new Label
        {
            Name = "CountdownLabel",
            Text = string.Empty,
            HorizontalAlignment = HorizontalAlignment.Center,
            Position = new Vector2(10f, 60f),
            Size = new Vector2(PanelW - 20f, 16f),
            MouseFilter = MouseFilterEnum.Ignore
        };
        AddChild(_countdownLabel);

        _okButton = new Button
        {
            Name = "OkButton",
            Text = "확인",
            Position = new Vector2((PanelW - 80f) / 2f, PanelH - 28f),
            Size = new Vector2(80f, 24f),
            MouseFilter = MouseFilterEnum.Stop
        };
        _okButton.Pressed += OnOk;
        AddChild(_okButton);

        GD.Print("[HudErrorPanel] Built — timed notice/error modal slot 168 (§8.25.2). " +
                 "Countdown label + OK button. Default timeout 5000 ms. " +
                 "Delegates banner to AnnouncePanel (slot 221) when present. " +
                 "Inbound WIRED: 4/81 ActionErrorResult rides the single-consumer IClientEventBus " +
                 "(drained only by GameLoop) -> HudMaster.OnActionError -> ShowActionError(status,error); " +
                 "4/500 popup-by-code rides PopupCodeEvent -> HudMaster.OnPopupCode -> " +
                 "AnnouncePanel.ShowPopupCode (global notice sink). " +
                 "OK/Esc dismiss the modal locally — no C2S intent exists for closing an error popup. " +
                 "spec: Docs/RE/specs/net_contracts.md §A.3/§A.5 (4/81 error-code->string-id); " +
                 "Docs/RE/specs/ui_system.md §8.25.2 CODE-CONFIRMED.");
    }

    public void SetAnnounceDelegate(HudAnnouncePanel? announcePanel)
    {
        _announceDelegate = announcePanel;
    }

    public void SetTextLibrary(HudTextLibrary text)
    {
        _text = text;
    }

    public void ShowActionError(byte status, byte error)
    {
        if (status == 1)
        {
            if (error == 100)
            {
                var applyMsg = _text?.GetCaption(51026) ?? string.Empty;
                if (!string.IsNullOrEmpty(applyMsg))
                    ShowError(applyMsg);
            }
            return;
        }

        var stringId = ResolveActionErrorStringId(error);
        if (stringId == 0) return;

        var msg = _text?.GetCaption(stringId) ?? string.Empty;
        if (string.IsNullOrEmpty(msg)) return;

        if (error == 0x11)
        {
            var secondary = _text?.GetCaption(51019) ?? string.Empty;
            if (!string.IsNullOrEmpty(secondary))
                _announceDelegate?.ShowAnnounce(secondary);
        }

        ShowError(msg);
    }

    private static int ResolveActionErrorStringId(byte error)
    {
        if (error == 0xFF) return 21078;
        return error switch
        {
            0x01 => 21074,
            0x02 => 21075,
            0x03 => 21076,
            0x04 => 21077,
            0x05 => 2163,
            0x06 => 21097,
            0x07 => 2164,
            0x08 => 2165,
            0x09 => 2166,
            0x0A => 2167,
            0x0B => 2168,
            0x0C => 2169,
            0x0D => 2170,
            0x0E => 2171,
            0x0F => 51016,
            0x10 => 51017,
            0x11 => 51018,
            0x12 => 51020,
            0x13 => 51023,
            0x14 => 51019,
            0x15 => 51025,
            0x17 => 51029,
            0x18 => 51033,
            _ => 0
        };
    }


    public void ShowError(string text, double seconds = DefaultTimeoutMs / 1000.0)
    {
        if (_messageLabel != null) _messageLabel.Text = text;
        _remainingSecs = seconds;
        _lastDisplayedSec = -1;
        _open = true;
        Visible = true;
        UpdateCountdownLabel();

        _announceDelegate?.ShowAnnounce(text);

        GD.Print($"[HudErrorPanel] ShowError: \"{text}\", timeout={seconds}s. " +
                 "Delegating to AnnouncePanel (slot 221) if present. " +
                 "spec: Docs/RE/specs/ui_system.md §8.25.2 CODE-CONFIRMED.");
    }


    public override void _Process(double delta)
    {
        if (!_open) return;

        _remainingSecs -= delta;
        if (_remainingSecs <= 0.0)
        {
            _remainingSecs = 0.0;
            Close();
            return;
        }

        UpdateCountdownLabel();
    }

    private void UpdateCountdownLabel()
    {
        var sec = (int)_remainingSecs;
        if (sec == _lastDisplayedSec) return;
        _lastDisplayedSec = sec;
        if (_countdownLabel != null)
            _countdownLabel.Text = sec.ToString();
    }


    private void OnOk()
    {
        Close();
        GD.Print("[HudErrorPanel] OK pressed (action 2) — closed. " +
                 "spec: Docs/RE/specs/ui_system.md §8.25.2 CODE-CONFIRMED.");
    }

    private void Close()
    {
        _open = false;
        Visible = false;
        _remainingSecs = 0.0;
        _lastDisplayedSec = -1;
        if (_messageLabel != null) _messageLabel.Text = string.Empty;
        if (_countdownLabel != null) _countdownLabel.Text = string.Empty;
    }

    public override void _Input(InputEvent @event)
    {
        if (!_open) return;
        if (@event is InputEventKey { Keycode: Key.Escape, Pressed: true })
        {
            OnOk();
            GetViewport().SetInputAsHandled();
        }
    }
}