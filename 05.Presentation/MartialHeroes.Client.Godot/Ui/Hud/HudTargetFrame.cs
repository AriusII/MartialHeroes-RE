// Ui/Hud/HudTargetFrame.cs
//
// Selected-target / mob-info plate — MopGagePanel (HUD panel-slot array slot 35).
//
// RECOVERED in the HUD-II pass (263bd994). This supersedes the earlier
// "real target plate NOT recovered" / "OtherInfo/MopGagePanel not recovered" deferral.
// SLOT CORRECTED: ui_system.md §1.9.3/§1.9.4 binary-won reversal — real slot is 35
// (member +0x2C4 relative to MainWindow+0x238 base); slot 177 is a plain GUComponent image
// (the trailing image of the bottom command-bar cluster, §2.3). The C# naming "slot 177"
// throughout HUD-II was REFUTED. Corrected to slot 35 per the 263bd994 RTTI pass.
// spec: Docs/RE/specs/ui_system.md §1.9.3 — MopGagePanel = slot 35 (binary-won).
// spec: Docs/RE/specs/ui_system.md §1.9.4 — "prior 'MopGage = slot 177' REFUTED".
//
// Panel geometry (CYCLE 11 RESOLVED — rect fully pinned):
//   X = (screen_width − 226) / 2   → screen-width-centred (centerX(226))
//   Y = 0   → top-flush
//   W = 226
//   H = 54
//   Container is transparent; children carry chrome from uitex id 1.
//
// spec: Docs/RE/scenes/ingame.md §5 — MopGagePanel class, slot 35, geometry.
// spec: Docs/RE/specs/ui_hud_layout.md §5.5a CYCLE 11 RESOLVED — "rect 226×54 top-flush centred".
//
// Child widgets (all binding uitex id 1 unless noted):
//   HP bar fill  dst (35,5) 172×6  src (40,517)   fill width = min(172, 172·hpRatio)
//   Portrait box dst (13,55) 200×200                3D placeholder (TODO world-campaign)
//   Close btn    dst (190,2) 11×11  action 3       shows msg 16001
//   Nav up       dst (202,2) 11×11  action 1
//   Nav down     dst (214,2) 11×11  action 2
//   Status A     dst (12,2)  13×13  src (40,309)   relation icon
//   Status B     dst (12,17) 13×13  src (278,500)  secondary state icon
//   Name label   dst (0,18)  row    text
//   Relation lbl dst (0,3)   row    "[level]name" (centred, relation-coloured)
//   Level tag    dst (150,12) row   caption msg 10037/10038
//
// Populate path: CLIENT-SIDE TARGET-DRIVEN (no dedicated S2C opcode).
//   Show when TargetChanged delivers a live target; hide on TargetChangedEvent.None.
//
// spec: Docs/RE/scenes/ingame.md §5 — MopGagePanel child widget layout, HP bar, labels.
// spec: Docs/RE/specs/ui_system.md §8.6.1 — uitex id 1 = mainwindow.dds (manifest-verified).

using System.Threading.Channels;
using Godot;
using MartialHeroes.Client.Application.Contracts.Hud;
using MartialHeroes.Client.Godot.Ui.Assets;

namespace MartialHeroes.Client.Godot.Ui.Hud;

/// <summary>
///     Selected-target / mob-info plate — <c>MopGagePanel</c> (HUD panel-slot array slot 35).
///     <para>
///         PASSIVE: drains <see cref="IHudEventHub.TargetChanges" /> each frame; shows/hides the
///         plate and refills the HP bar from the delivered ratios. Zero game logic.
///     </para>
///     <para>
///         Width 226, height 54, screen-width-centred, top-flush (Y=0). Children bind uitex id 1 (chrome atlas).
///         HP fill = <c>min(172, 172 · hpRatio)</c> px wide.
///         CYCLE 11 RESOLVED: rect 226×54 fully pinned; prior "dstY/H debugger-pending" retired.
///     </para>
///     spec: Docs/RE/specs/ui_system.md §1.9.3 — MopGagePanel = slot 35 (binary-won reversal of prior "slot 177").
///     spec: Docs/RE/specs/ui_system.md §1.9.4 — "prior 'MopGage = slot 177' REFUTED".
///     spec: Docs/RE/scenes/ingame.md §5 — MopGagePanel child widget layout, HP bar fill, labels.
/// </summary>
public sealed partial class HudTargetFrame : Control
{
    // -------------------------------------------------------------------------
    // Spec-cited geometry constants
    // spec: Docs/RE/scenes/ingame.md §5 — MopGagePanel child widget layout
    // spec: Docs/RE/specs/ui_hud_layout.md §5.5a CYCLE 11 RESOLVED — W=226, H=54
    // -------------------------------------------------------------------------

    private const float FrameW = 226f; // spec: ingame.md §5 / ui_hud_layout.md §5.5a CYCLE 11 — W=226
    private const float FrameH = 54f; // spec: ui_hud_layout.md §5.5a CYCLE 11 RESOLVED — H=54, top-flush
    private const float HpBarDstX = 35f; // spec: ingame.md §5 — hpBarFill dst (35,5)
    private const float HpBarDstY = 5f; // spec: ingame.md §5 — hpBarFill dst y=5
    private const float HpBarMaxW = 172f; // spec: ingame.md §5 — fill width = 172·curHP/maxHP clamped to 172
    private const float HpBarH = 6f; // spec: ingame.md §5 — HP bar h=6
    private const int HpBarSrcX = 40; // spec: ingame.md §5 — HP-bar src (40,517)
    private const int HpBarSrcY = 517; // spec: ingame.md §5 — HP-bar src y=517
    private const float PortraitDstX = 13f; // spec: ingame.md §5 — portrait/detail container dst x=13
    private const float PortraitDstY = 55f; // spec: ingame.md §5 — portrait/detail container dst y=55
    private const float PortraitSide = 200f; // spec: ingame.md §5 — 200×200 (3D placeholder)
    private const float CloseX = 190f; // spec: ingame.md §5 — close btn (190,2) action 3
    private const float NavUpX = 202f; // spec: ingame.md §5 — nav up (202,2) action 1
    private const float NavDownX = 214f; // spec: ingame.md §5 — nav down (214,2) action 2
    private const float BtnY = 2f; // spec: ingame.md §5 — mode buttons y=2
    private const float BtnSide = 11f; // spec: ingame.md §5 — mode buttons 11×11
    private const float StatusAX = 12f; // spec: ingame.md §5 — con-icon (leveldiff) at (12,2)
    private const float StatusAY = 2f; // spec: ingame.md §5 — con-icon y=2; con-icon buckets srcX 40/53/66/79/92
    private const int StatusASrcX = 40; // spec: ingame.md §5 — con-icon src (40,309)
    private const int StatusASrcY = 309; // spec: ingame.md §5 — con-icon srcY=309 (bucket 0)
    private const float StatusBX = 12f; // spec: ingame.md §5 — grade-icon at (12,17)
    private const float StatusBY = 17f; // spec: ingame.md §5 — grade-icon y=17
    private const int StatusBSrcX = 278; // spec: ingame.md §5 — grade-icon src (278,500)
    private const int StatusBSrcY = 500; // spec: ingame.md §5 — grade-icon srcY=500
    private const int StatusIconSide = 13; // spec: ingame.md §5 — con/grade icons 13×13

    // uitex id 1 = mainwindow.dds (the in-game chrome atlas)
    // spec: Docs/RE/specs/ui_system.md §8.6.1 — uitex 1 = data/ui/mainwindow.dds
    private const int UitexChromeId = 1; // spec: ui_system.md §8.6.1

    // -------------------------------------------------------------------------
    // Child controls (built once; updated per TargetChangedEvent)
    // -------------------------------------------------------------------------

    private Control? _hpFill; // ColorRect driven by hpRatio
    private Label? _nameLabel; // "[level]name" — spec: ingame.md §5 targetNameLabel +0xD4
    private Label? _percentLabel; // "%10.2f %%" HP percent — spec: ingame.md §5 hpPercentLabel +0xD0
    private Label? _relationLabel; // owner/relation tag (msg 10037/10038) — spec: ingame.md §5 +0xD8
    private ChannelReader<TargetChangedEvent>? _targetChanges;

    // -------------------------------------------------------------------------
    // Build (geometry pass)
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Geometry pass: builds the MopGagePanel frame, screen-width-centred, top-anchored, W=226.
    ///     Graceful-null when the VFS/atlas is offline.
    ///     spec: Docs/RE/specs/ui_system.md §1.9.3 — MopGagePanel = slot 35 (binary-won; prior "slot 177" REFUTED §1.9.4).
    ///     spec: Docs/RE/scenes/ingame.md §5 — MopGagePanel geometry, child widgets, HP bar formula.
    /// </summary>
    public void Build(HudAtlasLibrary atlas)
    {
        Name = "HudTargetFrame";

        // Screen-width-centred: X = (screen_width − 226) / 2
        // spec: ingame.md §5 — MopGagePanel "screen-width-centred" / ui_hud_layout.md §5.5a "top-flush"
        AnchorLeft = 0.5f;
        AnchorTop = 0f;
        AnchorRight = 0.5f;
        AnchorBottom = 0f;
        OffsetLeft = -FrameW / 2f; // centred pivot
        OffsetRight = FrameW / 2f;
        // Height 54 — RESOLVED CYCLE 11 (PINNED). spec: ui_hud_layout.md §5.5a CYCLE 11.
        OffsetTop = 0f;
        OffsetBottom = FrameH; // spec: ui_hud_layout.md §5.5a CYCLE 11 — "rect 226×54 top-flush centred"
        MouseFilter = MouseFilterEnum.Ignore;

        // Hidden by default; shown only when a target is selected.
        Visible = false;

        // --- Chrome background (slice of uitex id 1) ---
        // Frame chrome: dst (0,0) 226×54 = the panel's own dimensions; src origin (226,17) uitex id 1.
        // The container is 226 wide × 54 tall (top-flush centred). Children carry chrome from uitex id 1.
        // spec: ingame.md §5 — "Container is transparent; children carry chrome from uitex id 1."
        // spec: ui_hud_layout.md §5.5a CYCLE 11 RESOLVED — "rect 226×54 top-flush centred"
        var topFrameTex = atlas.SliceById(UitexChromeId, 226, 17, (int)FrameW, (int)FrameH);
        if (topFrameTex is not null)
        {
            var topFrame = new TextureRect
            {
                Name = "TopFrame",
                Texture = topFrameTex,
                StretchMode = TextureRect.StretchModeEnum.Scale,
                Position = new Vector2(0f, 0f),
                Size = new Vector2(FrameW, FrameH), // spec: ingame.md §5 — panel W=226, H=54
                MouseFilter = MouseFilterEnum.Ignore
            };
            AddChild(topFrame);
        }
        else
        {
            // Offline: plain dark backdrop
            var bg = new Panel { Name = "BgFallback" };
            bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            var s = new StyleBoxFlat();
            s.BgColor = new Color(0.05f, 0.05f, 0.1f, 0.80f);
            s.SetBorderWidthAll(1);
            s.BorderColor = new Color(0.5f, 0.5f, 0.5f, 0.8f);
            bg.AddThemeStyleboxOverride("panel", s);
            AddChild(bg);
            GD.PrintErr("[HudTargetFrame] uitex id 1 unavailable (VFS offline); using fallback chrome. " +
                        "spec: Docs/RE/scenes/ingame.md §5.");
        }

        // --- HP bar fill (172×6, dst 35,5, src 40,517) ---
        // spec: ingame.md §5 — hpBarFill +0xC4; width = 172·curHP/maxHP clamped to 172
        var hpFillTex = atlas.SliceById(UitexChromeId, HpBarSrcX, HpBarSrcY, (int)HpBarMaxW, (int)HpBarH);
        if (hpFillTex is not null)
        {
            var hpRect = new TextureRect
            {
                Name = "HpFill",
                Texture = hpFillTex,
                StretchMode = TextureRect.StretchModeEnum.Scale,
                Position = new Vector2(HpBarDstX, HpBarDstY),
                Size = new Vector2(HpBarMaxW, HpBarH),
                ClipContents = true,
                MouseFilter = MouseFilterEnum.Ignore
            };
            AddChild(hpRect);
            _hpFill = hpRect;
        }
        else
        {
            // Offline fallback: colored rect fills the bar area
            var hpRect = new ColorRect
            {
                Name = "HpFill",
                Color = new Color(0.8f, 0.15f, 0.15f, 0.9f),
                Position = new Vector2(HpBarDstX, HpBarDstY),
                Size = new Vector2(HpBarMaxW, HpBarH),
                MouseFilter = MouseFilterEnum.Ignore
            };
            AddChild(hpRect);
            _hpFill = hpRect;
        }

        // --- Status icon A — con-color (leveldiff) icon (12,2) 13×13 src (40,309) ---
        // spec: ingame.md §5 — con-icon buckets srcX 40/53/66/79/92 @ srcY 309; leveldiff +0x17C
        var statusATex =
            atlas.SliceById(UitexChromeId, StatusASrcX, StatusASrcY, StatusIconSide, StatusIconSide);
        var statusA = new TextureRect
        {
            Name = "StatusIconA",
            Texture = statusATex, // null-safe: TextureRect renders nothing when Texture is null
            StretchMode = TextureRect.StretchModeEnum.Scale,
            Position = new Vector2(StatusAX, StatusAY),
            Size = new Vector2(StatusIconSide, StatusIconSide),
            MouseFilter = MouseFilterEnum.Ignore
        };
        AddChild(statusA);

        // --- Status icon B — grade icon (12,17) 13×13 src (278,500) ---
        // spec: ingame.md §5 — grade-icon srcX 278/291/304/317 @ srcY 500/309
        var statusBTex =
            atlas.SliceById(UitexChromeId, StatusBSrcX, StatusBSrcY, StatusIconSide, StatusIconSide);
        var statusB = new TextureRect
        {
            Name = "StatusIconB",
            Texture = statusBTex,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            Position = new Vector2(StatusBX, StatusBY),
            Size = new Vector2(StatusIconSide, StatusIconSide),
            MouseFilter = MouseFilterEnum.Ignore
        };
        AddChild(statusB);

        // --- 3D portrait placeholder (13,55) 200×200 ---
        // spec: ingame.md §5 — detail container +0x110 (row-bg strips +0x114/+0x118/+0x11C); children hidden until target set
        // TODO(world-campaign): live target portrait — build ArrayMesh preview; never GltfDocument.AppendFromBuffer
        var portraitPlaceholder = new ColorRect
        {
            Name = "PortraitPlaceholder",
            Color = new Color(0.1f, 0.1f, 0.1f, 0.4f),
            Position = new Vector2(PortraitDstX, PortraitDstY),
            Size = new Vector2(PortraitSide, PortraitSide),
            MouseFilter = MouseFilterEnum.Ignore
        };
        AddChild(portraitPlaceholder);

        // --- Name / relation label — dst (0,3), "[level]name" centred ---
        // spec: ingame.md §5 — targetNameLabel +0xD4 "[level]name" ("%d]%s"), recolored per relation, masked on PvP
        _nameLabel = new Label
        {
            Name = "NameLabel",
            Text = string.Empty,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Position = new Vector2(0f, 3f),
            Size = new Vector2(FrameW, 12f),
            MouseFilter = MouseFilterEnum.Ignore
        };
        // Caption font slot 0 (DotumChe 12 px) — default HUD face used for name labels.
        // spec: Docs/RE/scenes/ingame.md §14.2 — slot 0 DotumChe 12 px weight 0 = default HUD face
        HudFont.ApplyToLabel(_nameLabel, 0); // spec: ingame.md §14.2 — slot 0 default HUD face
        AddChild(_nameLabel);

        // --- Percent / HP label — dst (0,18) ---
        // spec: ingame.md §5 — hpPercentLabel +0xD0 formats HP as "%10.2f %%" — populated from HpRatio
        _percentLabel = new Label
        {
            Name = "PercentLabel",
            Text = string.Empty,
            HorizontalAlignment = HorizontalAlignment.Center,
            Position = new Vector2(0f, 18f),
            Size = new Vector2(FrameW, 12f),
            MouseFilter = MouseFilterEnum.Ignore
        };
        // Default HUD face: slot 0 = DotumChe 12 px. spec: ingame.md §14.2.
        HudFont.ApplyToLabel(_percentLabel, 0); // spec: ingame.md §14.2 — slot 0 default HUD face
        AddChild(_percentLabel);

        // --- Owner/relation label — dst (150,12) msg 10037/10038 ---
        // spec: ingame.md §5 — ownerRelationLabel +0xD8: summon (msg 10037) / pet (msg 10038) / owner-name
        _relationLabel = new Label
        {
            Name = "RelationTag",
            Text = string.Empty,
            HorizontalAlignment = HorizontalAlignment.Left,
            Position = new Vector2(150f, 12f),
            Size = new Vector2(FrameW - 150f, 12f),
            MouseFilter = MouseFilterEnum.Ignore
        };
        // Caption font slot 0 (DotumChe 12 px) — default HUD face.
        // spec: Docs/RE/scenes/ingame.md §14.2 — slot 0 DotumChe 12 px = default HUD face
        HudFont.ApplyToLabel(_relationLabel, 0); // spec: ingame.md §14.2 — slot 0 default HUD face
        AddChild(_relationLabel);

        // --- Close button — dst (190,2) 11×11 action 3 → caption msg 16001 ---
        // spec: ingame.md §5 — "3 mode buttons (actions 1/2/3)"; action 3 = close/clear target
        _BuildButton("CloseBtn", CloseX, BtnY, BtnSide,
            atlas, UitexChromeId, 310, 488, () =>
            {
                // Action id 3 = close/clear target
                // spec: ingame.md §5 — "action 3" mode button clears the selection
                GD.Print("[HudTargetFrame] Close button (action 3) pressed. " +
                         "spec: Docs/RE/scenes/ingame.md §5 — mode buttons (actions 1/2/3).");
                ClearTarget();
            });

        // --- Nav up button — dst (202,2) 11×11 action 1 ---
        // spec: ingame.md §5 — "3 mode buttons (actions 1/2/3)"; action 1 = nav-up
        _BuildButton("NavUpBtn", NavUpX, BtnY, BtnSide,
            atlas, UitexChromeId, 321, 488, () =>
            {
                GD.Print("[HudTargetFrame] Nav-up button (action 1) pressed. " +
                         "spec: Docs/RE/scenes/ingame.md §5.");
            });

        // --- Nav down button — dst (214,2) 11×11 action 2 ---
        // spec: ingame.md §5 — "3 mode buttons (actions 1/2/3)"; action 2 = nav-down
        _BuildButton("NavDownBtn", NavDownX, BtnY, BtnSide,
            atlas, UitexChromeId, 332, 488, () =>
            {
                GD.Print("[HudTargetFrame] Nav-down button (action 2) pressed. " +
                         "spec: Docs/RE/scenes/ingame.md §5.");
            });

        GD.Print("[HudTargetFrame] Built — MopGagePanel slot 35, rect 226×54 top-flush centred. " +
                 "Chrome 226×54 (FIXED — prior 175×318 transposed). " +
                 "PercentLabel wired to HpRatio. Name/level/relation labels + con/grade icons built. " +
                 "(Prior 'slot 177' REFUTED — ui_system.md §1.9.4 binary-won reversal, 263bd994 RTTI pass.) " +
                 "spec: Docs/RE/specs/ui_system.md §1.9.3 CODE-CONFIRMED. " +
                 "spec: Docs/RE/scenes/ingame.md §5 — MopGagePanel layout. " +
                 "spec: Docs/RE/specs/ui_hud_layout.md §5.5a CYCLE 11 RESOLVED (226×54 top-flush centred).");
    }

    // -------------------------------------------------------------------------
    // Hub binding
    // -------------------------------------------------------------------------

    /// <summary>Binds the target-frame to the hub's <see cref="IHudEventHub.TargetChanges" /> channel.</summary>
    public void BindHub(IHudEventHub hub)
    {
        _targetChanges = hub.TargetChanges;
        GD.Print("[HudTargetFrame] BindHub: TargetChanges channel connected.");
    }

    // -------------------------------------------------------------------------
    // Per-frame drain
    // -------------------------------------------------------------------------

    public override void _Process(double delta)
    {
        if (_targetChanges is null) return;

        while (_targetChanges.TryRead(out var ev))
        {
            if (ev is null) continue;

            if (ev.IsCleared)
                ClearTarget();
            else
                ApplyTarget(ev);
        }
    }

    // -------------------------------------------------------------------------
    // Apply / clear
    // -------------------------------------------------------------------------

    private void ApplyTarget(TargetChangedEvent ev)
    {
        Visible = true;

        // HP bar fill width = min(172, 172 · hpRatio)
        // spec: ingame.md §5 — hpBarFill +0xC4; width = 172·curHP/maxHP clamped to 172
        var fillW = Math.Min(HpBarMaxW, HpBarMaxW * ev.HpRatio);
        switch (_hpFill)
        {
            case TextureRect tr:
                tr.Size = new Vector2(fillW, HpBarH);
                break;
            case ColorRect cr:
                cr.Size = new Vector2(fillW, HpBarH);
                break;
        }

        // hpPercentLabel +0xD0 — formats as "%10.2f %%" from hpRatio.
        // spec: ingame.md §5 — "hpPercentLabel +0xD0 formats HP as '%10.2f %%'"
        if (_percentLabel is not null)
            _percentLabel.Text = $"{ev.HpRatio * 100f,10:F2} %";

        // targetNameLabel +0xD4 — "[level]name" ("%d]%s"), recolored per relation, masked on PvP maps.
        // spec: ingame.md §5 — "targetNameLabel +0xD4 '[level]name' ('%d]%s'); masked to '********' on PvP maps"
        // Level not yet in TargetChangedEvent — render name only until Application exposes level.
        // TODO(world-campaign): include level when Application surface exposes it; apply PvP mask via nameMasked.
        if (_nameLabel is not null)
            _nameLabel.Text = string.IsNullOrEmpty(ev.Name) ? string.Empty : ev.Name;

        // ownerRelationLabel +0xD8 — summon (msg 10037) / pet (msg 10038) / owner-name.
        // spec: ingame.md §5 — "ownerRelationLabel +0xD8: summon (msg 10037) / pet (msg 10038) / owner-name"
        // TODO(world-campaign): resolve msg 10037/10038 from the message catalogue (HudTextLibrary)
        if (_relationLabel is not null)
            _relationLabel.Text = string.Empty; // placeholder until msg-db is wired
    }

    private void ClearTarget()
    {
        Visible = false;
        if (_nameLabel is not null) _nameLabel.Text = string.Empty;
        if (_percentLabel is not null) _percentLabel.Text = string.Empty;
        if (_relationLabel is not null) _relationLabel.Text = string.Empty;

        // Reset bar to zero
        switch (_hpFill)
        {
            case TextureRect tr: tr.Size = new Vector2(0f, HpBarH); break;
            case ColorRect cr: cr.Size = new Vector2(0f, HpBarH); break;
        }
    }

    // -------------------------------------------------------------------------
    // Button builder
    // -------------------------------------------------------------------------

    private void _BuildButton(string nodeName, float x, float y, float side,
        HudAtlasLibrary atlas, int texId, int srcX, int srcY, Action onPress)
    {
        var normalTex = atlas.SliceById(texId, srcX, srcY, (int)side, (int)side);

        if (normalTex is not null)
        {
            // Texture-rect button using TextureButton (3-state supported)
            var btn = new TextureButton
            {
                Name = nodeName,
                TextureNormal = normalTex,
                Position = new Vector2(x, y),
                Size = new Vector2(side, side),
                MouseFilter = MouseFilterEnum.Stop
            };
            btn.Pressed += onPress;
            AddChild(btn);
        }
        else
        {
            // Offline fallback: tiny plain button
            var btn = new Button
            {
                Name = nodeName,
                Text = string.Empty,
                Position = new Vector2(x, y),
                Size = new Vector2(side, side),
                MouseFilter = MouseFilterEnum.Stop
            };
            btn.Pressed += onPress;
            AddChild(btn);
        }
    }
}