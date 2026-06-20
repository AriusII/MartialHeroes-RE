// Ui/Hud/HudMessagePanel.cs
//
// In-game MessagePanel — system notice / confirm modal (slot 190).
//
// Placement (CODE-CONFIRMED):
//   X = (screen_width  − 340) / 2, Y = (screen_height − 340) / 2, W = 340, H = 190.
//   spec: Docs/RE/specs/ui_system.md §8.20.1 CODE-CONFIRMED.
//   spec: Docs/RE/specs/ui_hud_layout.md §5.13 — slot 190, screen-centred confirm family.
//
// Two modes (CODE-CONFIRMED):
//   Mode 0 — single OK notice. Shows btnOK (action 2); hides btnLeft / btnRight.
//   Mode 1 — Yes/No confirm.   Shows btnLeft (action 0, Yes) + btnRight (action 1, No); hides btnOK.
//
// Atlas binding (CODE-CONFIRMED) — does NOT use uitex 9:
//   uitex 2 (inventwindow.dds) — panel background; center OK button (src 302,860 / hover 415,860).
//   uitex 8 (skillwindow.dds)  — Left (Yes) src (660,984) / hover (187,956);
//                                 Right (No)  src (773,984) / hover (886,984).
//   spec: Docs/RE/specs/ui_system.md §8.20.2 CODE-CONFIRMED.
//
// Body text sources (CODE-CONFIRMED):
//   1. Raw string passed by caller → ShowNotice / ShowConfirm.
//   2. msginfo.do keyed record (128-byte records) — TODO(format): msginfo.do spec-pending.
//   3. msg.xdb id formatted by caller — rendered via HudTextLibrary.
//   spec: Docs/RE/specs/ui_system.md §8.20.5 CODE-CONFIRMED.
//
// ESC / action 1 / action 2 → close.  Action 0 (Yes) → fires onYes callback.
//   spec: Docs/RE/specs/ui_system.md §8.20.4 CODE-CONFIRMED.
//
// PASSIVE: zero game logic. The caller issues ShowNotice/ShowConfirm with a text string.
// Outcome intents (Yes/No) are delivered via callbacks — no domain state touched here.

using Godot;
using MartialHeroes.Client.Godot.Ui.Assets;

namespace MartialHeroes.Client.Godot.Ui.Hud;

/// <summary>
///     In-game system notice / confirm modal (MessagePanel, slot 190).
///     <para>
///         PASSIVE: receives display text via <see cref="ShowNotice" /> or
///         <see cref="ShowConfirm" /> and returns the user's choice via callbacks.
///         No game-rule logic; no domain mutation.
///     </para>
///     <para>
///         Mode 0 — single OK notice.<br />
///         Mode 1 — Yes/No confirm (onYes callback fires if the user presses Yes).
///     </para>
///     spec: Docs/RE/specs/ui_system.md §8.20 CODE-CONFIRMED.
///     spec: Docs/RE/specs/ui_hud_layout.md §5.13 — slot 190.
/// </summary>
public sealed partial class HudMessagePanel : Control
{
    // -------------------------------------------------------------------------
    // Spec-cited geometry constants
    // spec: Docs/RE/specs/ui_system.md §8.20.1 CODE-CONFIRMED
    // -------------------------------------------------------------------------

    private const float MsgW = 340f; // spec: ui_system.md §8.20.1 — W=340
    private const float MsgH = 190f; // spec: ui_system.md §8.20.1 — H=190

    // Button positions (panel-local, CODE-CONFIRMED)
    // spec: ui_system.md §8.20.3 — btnLeft (w/2−120, h−60), btnRight (w/2+7, h−60), btnOK (w/2−56, h−55)
    // All buttons: w=113, h=40.
    private const float BtnW = 113f; // spec: ui_system.md §8.20.3
    private const float BtnH = 40f; // spec: ui_system.md §8.20.3

    // Atlas uitex ids (CODE-CONFIRMED)
    // spec: ui_system.md §8.20.2 — uitex 2 = inventwindow.dds, uitex 8 = skillwindow.dds
    private const int TexInvent = 2; // spec: ui_system.md §8.20.2
    private const int TexSkill = 8; // spec: ui_system.md §8.20.2

    // Button atlas src-rects (CODE-CONFIRMED)
    // spec: ui_system.md §8.20.3 — srcX/srcY for NORMAL and HOVER
    private const int OkNormX = 302;
    private const int OkNormY = 860; // spec: §8.20.3 uitex 2
    private const int OkHoverX = 415;
    private const int OkHoverY = 860; // spec: §8.20.3 uitex 2
    private const int YesNormX = 660;
    private const int YesNormY = 984; // spec: §8.20.3 uitex 8
    private const int YesHoverX = 187;
    private const int YesHoverY = 956; // spec: §8.20.3 uitex 8
    private const int NoNormX = 773;
    private const int NoNormY = 984; // spec: §8.20.3 uitex 8
    private const int NoHoverX = 886;
    private const int NoHoverY = 984; // spec: §8.20.3 uitex 8

    private static readonly Vector2 BtnLeftPos = new(MsgW / 2f - 120f, MsgH - 60f); // spec: §8.20.3 action 0 (Yes)
    private static readonly Vector2 BtnRightPos = new(MsgW / 2f + 7f, MsgH - 60f); // spec: §8.20.3 action 1 (No)
    private static readonly Vector2 BtnOkPos = new(MsgW / 2f - 56f, MsgH - 55f); // spec: §8.20.3 action 2 (OK)
    private Button? _btnLeft; // Yes — mode 1

    // Buttons
    private Button? _btnOk;
    private Button? _btnRight; // No  — mode 1

    // Body text labels
    private Label? _label0;
    private Label? _label1;
    private Label? _label2;
    private Action? _onNo;
    private Action? _onYes;

    // -------------------------------------------------------------------------
    // View state
    // -------------------------------------------------------------------------

    private bool _open;

    // -------------------------------------------------------------------------
    // Build
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Geometry pass: builds the centered 340×190 modal with OK + Yes/No buttons.
    ///     spec: Docs/RE/specs/ui_system.md §8.20.1 / §8.20.3 CODE-CONFIRMED.
    /// </summary>
    public void Build(HudAtlasLibrary atlas)
    {
        Name = "HudMessagePanel";

        // Centered modal — X = (screen−340)/2, Y = (screen−340)/2, size 340×190.
        // spec: ui_system.md §8.20.1 — centering box uses literal 340 on both axes.
        AnchorLeft = 0.5f;
        AnchorTop = 0.5f;
        AnchorRight = 0.5f;
        AnchorBottom = 0.5f;
        OffsetLeft = -MsgW / 2f;
        OffsetTop = -MsgH / 2f;
        OffsetRight = MsgW / 2f;
        OffsetBottom = MsgH / 2f;

        Visible = false;
        MouseFilter = MouseFilterEnum.Stop; // modal — captures all input below

        // Backdrop from uitex 2 (inventwindow.dds) src ≈ (190, 318)
        // spec: ui_system.md §8.20.2 — panel background on uitex 2; src MED (push-decode)
        var backdrop = new Panel { Name = "Backdrop" };
        backdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var bdStyle = new StyleBoxFlat();
        bdStyle.BgColor = new Color(0.07f, 0.07f, 0.12f, 0.96f);
        bdStyle.SetBorderWidthAll(2);
        bdStyle.BorderColor = new Color(0.55f, 0.45f, 0.25f, 0.9f);
        backdrop.AddThemeStyleboxOverride("panel", bdStyle);
        AddChild(backdrop);

        // Three body text labels (label0/1/2 for multi-field messages)
        // spec: ui_system.md §8.20.3 — label0 (0,50,w,20) label1 (0,80,w,20) label2 (0,110,w,20)
        _label0 = new Label
        {
            Name = "Label0",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            HorizontalAlignment = HorizontalAlignment.Center,
            Position = new Vector2(0f, 50f),
            Size = new Vector2(MsgW, 20f),
            MouseFilter = MouseFilterEnum.Ignore
        };
        AddChild(_label0);

        _label1 = new Label
        {
            Name = "Label1",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            HorizontalAlignment = HorizontalAlignment.Center,
            Position = new Vector2(0f, 80f),
            Size = new Vector2(MsgW, 20f),
            MouseFilter = MouseFilterEnum.Ignore
        };
        AddChild(_label1);

        _label2 = new Label
        {
            Name = "Label2",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            HorizontalAlignment = HorizontalAlignment.Center,
            Position = new Vector2(0f, 110f),
            Size = new Vector2(MsgW, 20f),
            MouseFilter = MouseFilterEnum.Ignore
        };
        AddChild(_label2);

        // OK button (action 2, mode 0) — uitex 2 (inventwindow.dds)
        // spec: ui_system.md §8.20.3 — (w/2−56, h−55, 113, 40); NORMAL (302,860) HOVER (415,860)
        _btnOk = BuildButton("BtnOK", "OK", BtnOkPos, TexInvent, atlas);
        _btnOk.Pressed += OnOkPressed;
        AddChild(_btnOk);

        // Yes button (action 0, mode 1) — uitex 8 (skillwindow.dds)
        // spec: ui_system.md §8.20.3 — (w/2−120, h−60, 113, 40); NORMAL (660,984) HOVER (187,956)
        _btnLeft = BuildButton("BtnYes", "Yes", BtnLeftPos, TexSkill, atlas);
        _btnLeft.Pressed += OnYesPressed;
        AddChild(_btnLeft);

        // No button (action 1, mode 1) — uitex 8 (skillwindow.dds)
        // spec: ui_system.md §8.20.3 — (w/2+7, h−60, 113, 40); NORMAL (773,984) HOVER (886,984)
        _btnRight = BuildButton("BtnNo", "No", BtnRightPos, TexSkill, atlas);
        _btnRight.Pressed += OnNoPressed;
        AddChild(_btnRight);

        // Start in mode 0 (OK only) but fully hidden
        ApplyMode(0);

        GD.Print("[HudMessagePanel] Built — centered 340×190 modal, slot 190. " +
                 "Mode 0=OK notice, Mode 1=Yes/No confirm. " +
                 "spec: Docs/RE/specs/ui_system.md §8.20 CODE-CONFIRMED.");
    }

    private static Button BuildButton(string name, string fallbackText, Vector2 pos, int texId, HudAtlasLibrary atlas)
    {
        var btn = new Button
        {
            Name = name,
            Text = fallbackText,
            Position = pos,
            Size = new Vector2(BtnW, BtnH),
            MouseFilter = MouseFilterEnum.Stop
        };
        // Graceful-null: if atlas unavailable, fallback text is used
        if (atlas is not null)
        {
            // No atlas slicing here — texture not available without VFS; graceful-null per offline rule.
            // TODO(world-campaign): slice btn frames from uitex 2/8 when VFS is present.
        }

        return btn;
    }

    // -------------------------------------------------------------------------
    // Public API — ShowNotice / ShowConfirm
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Shows the modal in mode 0 (single OK notice) with the given text.
    ///     The text is already-decoded CP949 from Application; render as-is.
    ///     spec: Docs/RE/specs/ui_system.md §8.20 — mode 0: single OK notice.
    /// </summary>
    public void ShowNotice(string text)
    {
        if (_label0 is null) return;

        _label0.Text = text;
        if (_label1 is not null) _label1.Text = string.Empty;
        if (_label2 is not null) _label2.Text = string.Empty;
        _onYes = null;
        _onNo = null;

        ApplyMode(0);
        OpenModal();
    }

    /// <summary>
    ///     Shows the modal in mode 1 (Yes/No confirm) with the given text.
    ///     <paramref name="onYes" /> fires when the player confirms Yes (action 0).
    ///     <paramref name="onNo" /> fires when the player presses No (action 1) or Esc.
    ///     spec: Docs/RE/specs/ui_system.md §8.20 — mode 1: Yes/No confirm.
    /// </summary>
    public void ShowConfirm(string text, Action? onYes = null, Action? onNo = null)
    {
        if (_label0 is null) return;

        _label0.Text = text;
        if (_label1 is not null) _label1.Text = string.Empty;
        if (_label2 is not null) _label2.Text = string.Empty;
        _onYes = onYes;
        _onNo = onNo;

        ApplyMode(1);
        OpenModal();
    }

    // -------------------------------------------------------------------------
    // Mode application
    // spec: Docs/RE/specs/ui_system.md §8.20 — mode 0 hides side buttons; mode 1 hides OK
    // -------------------------------------------------------------------------

    private void ApplyMode(int mode)
    {
        var modeOk = mode == 0;
        if (_btnOk is not null) _btnOk.Visible = modeOk;
        if (_btnLeft is not null) _btnLeft.Visible = !modeOk;
        if (_btnRight is not null) _btnRight.Visible = !modeOk;
    }

    // -------------------------------------------------------------------------
    // Action handlers
    // spec: Docs/RE/specs/ui_system.md §8.20.4 CODE-CONFIRMED
    // -------------------------------------------------------------------------

    private void OnOkPressed()
    {
        // Action 2 (OK, mode 0) → close. spec: ui_system.md §8.20.4 CODE-CONFIRMED.
        CloseModal();
    }

    private void OnYesPressed()
    {
        // Action 0 (Yes, mode 1) → fire callback + close. spec: ui_system.md §8.20.4 CODE-CONFIRMED.
        var cb = _onYes;
        CloseModal();
        cb?.Invoke();
    }

    private void OnNoPressed()
    {
        // Action 1 (No, mode 1) → fire callback + close. spec: ui_system.md §8.20.4 CODE-CONFIRMED.
        var cb = _onNo;
        CloseModal();
        cb?.Invoke();
    }

    private void OpenModal()
    {
        _open = true;
        Visible = true;
    }

    private void CloseModal()
    {
        _open = false;
        Visible = false;
        _onYes = null;
        _onNo = null;
    }

    // -------------------------------------------------------------------------
    // ESC key handler
    // spec: Docs/RE/specs/ui_system.md §8.20.4 — ESC while visible → close
    // -------------------------------------------------------------------------

    public override void _Input(InputEvent @event)
    {
        if (!_open) return;
        if (@event is InputEventKey { Keycode: Key.Escape, Pressed: true })
        {
            // spec: ui_system.md §8.20.4 — ESC (while visible) → close CODE-CONFIRMED
            var cb = _onNo;
            CloseModal();
            cb?.Invoke();
            GetViewport().SetInputAsHandled();
        }
    }
}