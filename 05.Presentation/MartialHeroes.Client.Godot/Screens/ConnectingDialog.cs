// Screens/ConnectingDialog.cs
//
// Centered "connecting" dialog — shown during sub-states 35/39 (channel-endpoint fetch).
// H4 fix: BootFlow.OnServerSelected() now shows this panel before jumping to the PIN modal,
// matching the official client's "Connecting…" overlay.
//
// LAYOUT (§11.4):
//   The official panel reuses the shared notice chrome:
//     C = InventWindow.dds src(318,647) 340×190, centered on the 1024×768 canvas.
//   Caption candidate: msg.xdb id 4023 (quit-confirm text repurposed, or 4033 candidate).
//   The spec notes "caption 4023-candidate" — a best guess until the real id is swept.
//   A 취소 button allows cancelling the connect.
//
// PASSIVE: zero game logic. Emits CancelRequested; BootFlow decides what to do.
//
// spec: Docs/RE/specs/frontend_scenes.md §11.4 "Connecting dialog (states 35/39)
//       C reuses notice panel (318,647) 340×190 centered, caption 4023-candidate." CODE-CONFIRMED.
//       §1.5 sub-states 35/39 "wait for server-list / endpoint reply". CODE-CONFIRMED.

using Godot;
using MartialHeroes.Client.Godot.Screens.Layout;
using MartialHeroes.Client.Godot.Screens.Widgets;

namespace MartialHeroes.Client.Godot.Screens;

/// <summary>
/// Centered connecting dialog (sub-states 35/39).
/// Shows the InventWindow.dds notice-panel chrome with a caption and a 취소 cancel button.
/// spec: Docs/RE/specs/frontend_scenes.md §11.4 / §1.5. CODE-CONFIRMED chrome rect.
/// </summary>
public sealed partial class ConnectingDialog : Control
{
    // =========================================================================
    // Outgoing intents
    // =========================================================================

    /// <summary>Raised when the 취소 (cancel) button is pressed.</summary>
    [Signal]
    public delegate void CancelRequestedEventHandler();

    // =========================================================================
    // View state
    // =========================================================================

    private UiAssetLoader _assets = null!;
    private bool _ownsAssets;

    /// <summary>Optionally inject a shared asset loader.</summary>
    public UiAssetLoader? SharedAssets { get; set; }

    // =========================================================================
    // Godot lifecycle
    // =========================================================================

    public override void _Ready()
    {
        _assets = SharedAssets ?? UiAssetLoader.Open();
        _ownsAssets = SharedAssets is null;

        try
        {
            BuildUi();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[ConnectingDialog] Build failed: {ex.Message}");
        }
    }

    public override void _ExitTree()
    {
        if (_ownsAssets) _assets?.Dispose();
    }

    // =========================================================================
    // UI construction
    // =========================================================================

    private void BuildUi()
    {
        // Full-canvas blocker — stops clicks reaching behind the dialog.
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;

        // Semi-transparent dimmer.
        var dimmer = new ColorRect { Color = new Color(0f, 0f, 0f, 0.45f) };
        dimmer.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        dimmer.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(dimmer);

        // Modal panel: centered 340×190 on the 1024×768 canvas.
        // spec §11.4 "C reuses notice panel (318,647) 340×190 centered". CODE-CONFIRMED rect.
        const int panelW = LoginLayout.ModalChromeW; // 340
        const int panelH = LoginLayout.ModalChromeH; // 190
        const int panelX = (LoginLayout.RefWidth - panelW) / 2; // 342
        const int panelY = (LoginLayout.RefHeight - panelH) / 2; // 289

        var panel = new Control
        {
            Name = "ConnectPanel",
            Position = new Vector2(panelX, panelY),
            Size = new Vector2(panelW, panelH),
        };
        AddChild(panel);

        // Chrome background: InventWindow.dds src(318,647) 340×190.
        // spec §11.4 / §11.2d. CODE-CONFIRMED.
        AtlasTexture? chrome = _assets.Slice(
            LoginLayout.AtlasInventWindow,
            LoginLayout.ModalChromeSrcX, LoginLayout.ModalChromeSrcY,
            panelW, panelH);

        if (chrome is not null)
        {
            var chromeBg = new TextureRect
            {
                Name = "Chrome",
                Texture = chrome,
                StretchMode = TextureRect.StretchModeEnum.Scale,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            chromeBg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            panel.AddChild(chromeBg);
        }
        else
        {
            var fallback = new ColorRect
            {
                Name = "ChromeFallback",
                Color = new Color(0.07f, 0.07f, 0.12f, 0.97f),
            };
            fallback.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            var border = new Panel();
            border.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            var style = new StyleBoxFlat
            {
                BgColor = new Color(0f, 0f, 0f, 0f),
                BorderColor = new Color(0.55f, 0.45f, 0.20f),
            };
            style.SetBorderWidthAll(2);
            border.AddThemeStyleboxOverride("panel", style);
            panel.AddChild(fallback);
            panel.AddChild(border);
        }

        // Caption: msg.xdb id 4023 (quit-confirm text repurposed as connecting caption candidate).
        // spec §11.4 "caption 4023-candidate". CODE-CONFIRMED (id used); exact id PLAUSIBLE.
        var caption = WidgetFactory.MakeLabel(
            _assets.Text(LoginLayout.MsgQuitConfirm1, "서버에 접속 중..."), // "Connecting to server…"
            LoginLayout.FontBodyHeight,
            new Color(0.90f, 0.90f, 0.90f));
        caption.Name = "Caption";
        caption.Position = new Vector2(10, 70);
        caption.Size = new Vector2(320, 20);
        caption.HorizontalAlignment = HorizontalAlignment.Center;
        panel.AddChild(caption);

        // 취소 (Cancel) button — uses the quit-confirm Yes button art as the stone button frame.
        // Placed at the bottom of the panel, centered. spec §11.4 "취소 button". CODE-CONFIRMED.
        var cancelBtn = WidgetFactory.MakeStateButton(
            _assets, LoginLayout.AtlasInventWindow,
            (panelW - LoginLayout.QuitConfirmYes1.W) / 2, // centered
            panelH - LoginLayout.QuitConfirmYes1.H - 12, // near bottom
            LoginLayout.QuitConfirmYes1.W, LoginLayout.QuitConfirmYes1.H,
            LoginLayout.QuitConfirmYes1.SrcX, LoginLayout.QuitConfirmYes1.SrcY, // NORMAL src(302,900)
            LoginLayout.QuitConfirmYes1HoverSrcX, LoginLayout.QuitConfirmYes1HoverSrcY, // HOVER
            LoginLayout.QuitConfirmYes1.SrcX, LoginLayout.QuitConfirmYes1.SrcY,
            LoginLayout.ActionQuitConfirmYes1,
            caption: _assets.Text(4010u, "취소"),
            captionTint: Colors.White);
        cancelBtn.Name = "CancelBtn";
        cancelBtn.ActionFired += _ =>
        {
            GD.Print("[ConnectingDialog] 취소 pressed.");
            EmitSignal(SignalName.CancelRequested);
        };
        panel.AddChild(cancelBtn);

        GD.Print("[ConnectingDialog] Built. spec: frontend_scenes.md §11.4 sub-states 35/39. CODE-CONFIRMED.");
    }
}