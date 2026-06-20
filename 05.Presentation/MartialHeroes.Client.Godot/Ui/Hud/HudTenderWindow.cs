// Ui/Hud/HudTenderWindow.cs
//
// In-game TenderInfoPanel — consignment-purchase / info confirm (slot 118).
//
// Placement (CODE-CONFIRMED):
//   X = centerX(512), Y = centerY(595), W = 512, H = 595. Screen-centred.
//   spec: Docs/RE/specs/ui_system.md §8.21.1 CODE-CONFIRMED.
//   spec: Docs/RE/specs/ui_hud_layout.md §5.13 — slot 118, screen-centred.
//
// Atlas (CODE-CONFIRMED):
//   tender_window.dds — main chrome.
//   spec: Docs/RE/specs/ui_system.md §8.21.1 CODE-CONFIRMED.
//
// Actions (CODE-CONFIRMED):
//   0 = buy / confirm (checks player gold; if affordable → C2S CmsgTenderConfirm 2/118 header-only).
//   1 = request-detail (rate-limited to 30 entries; msg 66007 over cap).
//   2 = list scrollbar.
//   Esc closes.
//   spec: Docs/RE/specs/ui_system.md §8.21.1 CODE-CONFIRMED.
//
// Caption msg.xdb ids (CODE-CONFIRMED):
//   66006 — "not enough gold" error.
//   66007 — "detail request over 30-cap" error.
//   spec: Docs/RE/specs/ui_system.md §8.21.1 CODE-CONFIRMED.
//
// Network:
//   Outbound: C2S CmsgTenderConfirm (2/118, header-only) — deferred-confirm.
//     spec: Docs/RE/packets/ — CmsgTenderConfirm (2/118).
//   Inbound:  S2C tender listing populate — opcode unknown (TODO(capture)).
//   spec: Docs/RE/specs/ui_system.md §8.21.1 CODE-CONFIRMED / populate residual.
//
// PASSIVE: zero game logic. Confirm path emits use-case call (stub; no method yet).

using Godot;
using MartialHeroes.Client.Godot.Ui.Assets;

namespace MartialHeroes.Client.Godot.Ui.Hud;

/// <summary>
///     In-game consignment-purchase / info window (TenderInfoPanel, slot 118). 512×595 screen-centred.
///     <para>
///         PASSIVE: action 0 (confirm) emits C2S CmsgTenderConfirm (2/118, header-only) via
///         use-case call (stub — no method exists yet). Tender listing = TODO(capture).
///     </para>
///     spec: Docs/RE/specs/ui_system.md §8.21.1 CODE-CONFIRMED.
///     spec: Docs/RE/specs/ui_hud_layout.md §5.13 — slot 118.
/// </summary>
public sealed partial class HudTenderWindow : Control
{
    // -------------------------------------------------------------------------
    // Spec-cited constants
    // spec: Docs/RE/specs/ui_system.md §8.21.1 CODE-CONFIRMED
    // -------------------------------------------------------------------------

    private const float TenderW = 512f; // spec: §8.21.1 — W=512 CODE-CONFIRMED
    private const float TenderH = 595f; // spec: §8.21.1 — H=595 CODE-CONFIRMED

    // Detail-request cap (CODE-CONFIRMED)
    // spec: ui_system.md §8.21.1 — "rate-limited to 30 entries (caption 66007 over the cap)"
    private const int DetailRequestCap = 30; // spec: §8.21.1 CODE-CONFIRMED

    // Caption msg.xdb ids (CODE-CONFIRMED)
    // spec: ui_system.md §8.21.1 CODE-CONFIRMED
    private const int MsgNotEnoughGold = 66006; // spec: §8.21.1
    private const int MsgDetailOverCap = 66007; // spec: §8.21.1
    private int _detailRequestCount;

    // -------------------------------------------------------------------------
    // View state
    // -------------------------------------------------------------------------

    private bool _open;

    // -------------------------------------------------------------------------
    // Build
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Geometry pass: builds the centered 512×595 TenderInfoPanel.
    ///     spec: Docs/RE/specs/ui_system.md §8.21.1 CODE-CONFIRMED.
    ///     spec: Docs/RE/specs/ui_hud_layout.md §5.13 — slot 118.
    /// </summary>
    public void Build(HudAtlasLibrary atlas, HudTextLibrary text)
    {
        Name = "HudTenderWindow";

        // Centered — X = (screen_width − 512)/2, Y = (screen_height − 595)/2.
        // spec: ui_system.md §8.21.1 — "centerX(512), centerY(595)"
        AnchorLeft = 0.5f;
        AnchorTop = 0.5f;
        AnchorRight = 0.5f;
        AnchorBottom = 0.5f;
        OffsetLeft = -TenderW / 2f;
        OffsetTop = -TenderH / 2f;
        OffsetRight = TenderW / 2f;
        OffsetBottom = TenderH / 2f;

        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;

        // Backdrop (art from tender_window.dds when VFS present)
        // spec: ui_system.md §8.21.1 — "atlas tender_window.dds"
        var backdrop = new Panel { Name = "Backdrop" };
        backdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var bdStyle = new StyleBoxFlat();
        bdStyle.BgColor = new Color(0.06f, 0.05f, 0.08f, 0.97f);
        bdStyle.SetBorderWidthAll(2);
        bdStyle.BorderColor = new Color(0.50f, 0.40f, 0.55f, 0.9f);
        backdrop.AddThemeStyleboxOverride("panel", bdStyle);
        AddChild(backdrop);

        // Title
        AddChild(new Label
        {
            Name = "Title",
            Text = "Consignment Purchase",
            Position = new Vector2(10f, 10f),
            MouseFilter = MouseFilterEnum.Ignore
        });

        // 3D item-preview placeholder
        // spec: ui_system.md §8.21.1 — "carries a 3D item-preview canvas"
        var previewPlaceholder = new ColorRect
        {
            Name = "ItemPreview3D_TODO",
            Color = new Color(0.10f, 0.08f, 0.14f, 0.7f),
            Position = new Vector2(10f, 40f),
            Size = new Vector2(100f, 80f)
        };
        AddChild(previewPlaceholder);
        AddChild(new Label
        {
            Name = "PreviewLbl",
            Text = "// TODO(world-campaign): 3D item preview (GUCanvas3D)",
            Position = new Vector2(10f, 42f),
            Size = new Vector2(200f, 20f),
            LabelSettings = new LabelSettings { FontSize = 9 },
            MouseFilter = MouseFilterEnum.Ignore
        });

        // Scrollable list area (action 2 = scrollbar)
        // spec: ui_system.md §8.21.1 — "scrollable list"
        var listStub = new Label
        {
            Name = "TenderListStub",
            Text = "// TODO(capture): tender listing populate.\n" +
                   "// Inbound S2C opcode unknown — populate residual (debugger/capture-pending).\n" +
                   "// spec: Docs/RE/specs/ui_system.md §8.21.7 CODE-CONFIRMED residual.",
            Position = new Vector2(10f, 140f),
            Size = new Vector2(492f, 60f),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            LabelSettings = new LabelSettings { FontSize = 9 },
            MouseFilter = MouseFilterEnum.Ignore
        };
        AddChild(listStub);

        // Item-stat info labels (placeholder)
        AddChild(new Label
        {
            Name = "ItemStatStub",
            Text = "Item info: (populated from tender listing)", // spec: §8.21.1 — "item-stat info labels"
            Position = new Vector2(10f, 220f),
            MouseFilter = MouseFilterEnum.Ignore
        });

        // Confirm / Buy button (action 0) — gated by player gold check
        // spec: ui_system.md §8.21.1 — "action 0 = buy/confirm; checks cost vs player's gold"
        var confirmBtn = new Button
        {
            Name = "ConfirmBtn",
            Text = "Confirm Purchase",
            Position = new Vector2(10f, 530f),
            Size = new Vector2(160f, 35f),
            MouseFilter = MouseFilterEnum.Stop
        };
        confirmBtn.Pressed += OnConfirm;
        AddChild(confirmBtn);

        // Detail / info button (action 1) — rate-limited to 30 (caption 66007 over cap)
        // spec: ui_system.md §8.21.1 — "detail text built via shared InfoPanel category builder"
        var detailBtn = new Button
        {
            Name = "DetailBtn",
            Text = "Request Detail",
            Position = new Vector2(180f, 530f),
            Size = new Vector2(140f, 35f),
            MouseFilter = MouseFilterEnum.Stop
        };
        detailBtn.Pressed += OnRequestDetail;
        AddChild(detailBtn);

        // Close button (Esc also closes)
        var closeBtn = new Button
        {
            Name = "CloseBtn",
            Text = "×",
            Position = new Vector2(TenderW - 30f, 10f),
            Size = new Vector2(20f, 20f),
            MouseFilter = MouseFilterEnum.Stop
        };
        closeBtn.Pressed += () => Toggle(false);
        AddChild(closeBtn);

        GD.Print("[HudTenderWindow] Built — TenderInfoPanel slot 118 (512×595, screen-centred). " +
                 "Action 0=confirm→C2S 2/118 TODO(world-campaign). Detail cap=30 (msg 66007). " +
                 "Tender listing = TODO(capture). spec: Docs/RE/specs/ui_system.md §8.21.1 CODE-CONFIRMED.");
    }

    // -------------------------------------------------------------------------
    // Action handlers
    // -------------------------------------------------------------------------

    private void OnConfirm()
    {
        // spec: ui_system.md §8.21.1 — "checks the cost against player's gold; if affordable →
        //   C2S CmsgTenderConfirm (2/118, header-only); else msg 66006"
        // Gold check deferred to Application/server (we have no local gold state here).
        // TODO(world-campaign): IApplicationUseCases.TenderConfirmAsync (C2S 2/118, header-only).
        GD.Print("[HudTenderWindow] Confirm purchase → TODO(world-campaign): C2S 2/118 CmsgTenderConfirm. " +
                 $"Not-enough-gold path: msg.xdb {MsgNotEnoughGold}. " +
                 "spec: Docs/RE/specs/ui_system.md §8.21.1 CODE-CONFIRMED.");
    }

    private void OnRequestDetail()
    {
        // spec: ui_system.md §8.21.1 — "rate-limited to 30 entries (caption 66007 over cap)"
        if (_detailRequestCount >= DetailRequestCap)
        {
            GD.PrintErr($"[HudTenderWindow] Detail request over cap ({DetailRequestCap}). " +
                        $"msg.xdb {MsgDetailOverCap}. spec: §8.21.1 CODE-CONFIRMED.");
            return;
        }

        _detailRequestCount++;
        GD.Print($"[HudTenderWindow] Detail requested ({_detailRequestCount}/{DetailRequestCap}). " +
                 "TODO(world-campaign): detail via InfoPanel category builder. spec: §8.21.1.");
    }

    // -------------------------------------------------------------------------
    // Toggle
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Shows or hides the tender window.
    ///     Opened from NPC consignment interaction; specific open opcode = debugger-pending.
    ///     spec: Docs/RE/specs/ui_system.md §8.21.1 — "open from NPC consignment (opcode-pending)".
    /// </summary>
    public void Toggle(bool? forceState = null)
    {
        _open = forceState ?? !_open;
        Visible = _open;
        if (_open)
            // Reset detail-request counter on open
            _detailRequestCount = 0;
    }

    public override void _Input(InputEvent @event)
    {
        if (!_open) return;
        if (@event is InputEventKey { Keycode: Key.Escape, Pressed: true })
        {
            // spec: ui_system.md §8.21.1 — "Esc closes"
            Toggle(false);
            GetViewport().SetInputAsHandled();
        }
    }
}