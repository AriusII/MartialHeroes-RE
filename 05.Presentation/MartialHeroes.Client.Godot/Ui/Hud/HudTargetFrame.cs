// Ui/Hud/HudTargetFrame.cs
//
// Selected-target / mob-info plate — MopGagePanel (HUD service slot 177).
//
// RECOVERED in the HUD-II pass (263bd994). This supersedes the earlier
// "real target plate NOT recovered" / "OtherInfo/MopGagePanel not recovered" deferral.
//
// Panel geometry (CODE-CONFIRMED width + anchor; dstY / total-H debugger-pending):
//   X = (screen_width − 226) / 2   → screen-width-centred
//   W = 226
//   Top-anchored (Y ≈ 0)
//   Container is transparent; children carry chrome from uitex id 1.
//
// spec: Docs/RE/specs/ui_hud_layout.md §5.5b — MopGagePanel class, slot 177, geometry.
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
// spec: Docs/RE/specs/ui_hud_layout.md §5.5b CODE-CONFIRMED.
// spec: Docs/RE/specs/ui_system.md §8.6.1 — uitex id 1 = mainwindow.dds (manifest-verified).

using System.Threading.Channels;
using Godot;
using MartialHeroes.Client.Application.Hud;
using MartialHeroes.Client.Godot.Ui.Assets;

namespace MartialHeroes.Client.Godot.Ui.Hud;

/// <summary>
/// Selected-target / mob-info plate — <c>MopGagePanel</c> (HUD slot 177).
///
/// <para>PASSIVE: drains <see cref="IHudEventHub.TargetChanges"/> each frame; shows/hides the
/// plate and refills the HP bar from the delivered ratios. Zero game logic.</para>
///
/// <para>Width 226, screen-width-centred, top-anchored. Children bind uitex id 1 (chrome atlas).
/// HP fill = <c>min(172, 172 · hpRatio)</c> px wide.</para>
///
/// spec: Docs/RE/specs/ui_hud_layout.md §5.5b CODE-CONFIRMED.
/// </summary>
public sealed partial class HudTargetFrame : Control
{
    // -------------------------------------------------------------------------
    // Spec-cited geometry constants
    // spec: Docs/RE/specs/ui_hud_layout.md §5.5b
    // -------------------------------------------------------------------------

    private const float FrameW = 226f; // spec: ui_hud_layout.md §5.5b — W=226
    private const float HpBarDstX = 35f; // spec: ui_hud_layout.md §5.5b — dst (35,5)
    private const float HpBarDstY = 5f; // spec: ui_hud_layout.md §5.5b
    private const float HpBarMaxW = 172f; // spec: ui_hud_layout.md §5.5b — 172×6 fill
    private const float HpBarH = 6f; // spec: ui_hud_layout.md §5.5b
    private const int HpBarSrcX = 40; // spec: ui_hud_layout.md §5.5b — src (40,517)
    private const int HpBarSrcY = 517; // spec: ui_hud_layout.md §5.5b
    private const float PortraitDstX = 13f; // spec: ui_hud_layout.md §5.5b — dst (13,55)
    private const float PortraitDstY = 55f; // spec: ui_hud_layout.md §5.5b
    private const float PortraitSide = 200f; // spec: ui_hud_layout.md §5.5b — 200×200
    private const float CloseX = 190f; // spec: ui_hud_layout.md §5.5b — (190,2)
    private const float NavUpX = 202f; // spec: ui_hud_layout.md §5.5b — (202,2)
    private const float NavDownX = 214f; // spec: ui_hud_layout.md §5.5b — (214,2)
    private const float BtnY = 2f; // spec: ui_hud_layout.md §5.5b
    private const float BtnSide = 11f; // spec: ui_hud_layout.md §5.5b — 11×11
    private const float StatusAX = 12f; // spec: ui_hud_layout.md §5.5b — (12,2)
    private const float StatusAY = 2f; // spec: ui_hud_layout.md §5.5b
    private const int StatusASrcX = 40; // spec: ui_hud_layout.md §5.5b — src (40,309)
    private const int StatusASrcY = 309; // spec: ui_hud_layout.md §5.5b
    private const float StatusBX = 12f; // spec: ui_hud_layout.md §5.5b — (12,17)
    private const float StatusBY = 17f; // spec: ui_hud_layout.md §5.5b
    private const int StatusBSrcX = 278; // spec: ui_hud_layout.md §5.5b — src (278,500)
    private const int StatusBSrcY = 500; // spec: ui_hud_layout.md §5.5b
    private const int StatusIconSide = 13; // spec: ui_hud_layout.md §5.5b — 13×13

    // uitex id 1 = mainwindow.dds (the in-game chrome atlas)
    // spec: Docs/RE/specs/ui_system.md §8.6.1 — uitex 1 = data/ui/mainwindow.dds
    private const int UitexChromeId = 1; // spec: ui_system.md §8.6.1

    // -------------------------------------------------------------------------
    // Child controls (built once; updated per TargetChangedEvent)
    // -------------------------------------------------------------------------

    private Control? _hpFill; // ColorRect driven by hpRatio
    private Label? _nameLabel; // "[level]name"
    private Label? _relationLabel; // relation tag (msg 10037/10038)
    private ChannelReader<TargetChangedEvent>? _targetChanges;

    // -------------------------------------------------------------------------
    // Build (geometry pass)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Geometry pass: builds the MopGagePanel frame, screen-width-centred, top-anchored, W=226.
    /// Graceful-null when the VFS/atlas is offline.
    ///
    /// spec: Docs/RE/specs/ui_hud_layout.md §5.5b — MopGagePanel slot 177, geometry.
    /// </summary>
    public void Build(HudAtlasLibrary atlas)
    {
        Name = "HudTargetFrame";

        // Screen-width-centred: X = (screen_width − 226) / 2
        // spec: ui_hud_layout.md §5.5b — "screen-width-centred, top-anchored"
        AnchorLeft = 0.5f;
        AnchorTop = 0f;
        AnchorRight = 0.5f;
        AnchorBottom = 0f;
        OffsetLeft = -FrameW / 2f; // centred pivot
        OffsetRight = FrameW / 2f;
        // Height is debugger-pending; use portrait bottom + pad as a reasonable placeholder.
        // spec: ui_hud_layout.md §5.5b — "final dstY / total visible H debugger-pending"
        OffsetTop = 0f;
        OffsetBottom = PortraitDstY + PortraitSide + 20f; // placeholder; TODO(debugger): pin total H
        MouseFilter = MouseFilterEnum.Ignore;

        // Hidden by default; shown only when a target is selected.
        Visible = false;

        // --- Chrome background (slice of uitex id 1) ---
        // Top frame sub-panel: dst (0,0) 175×318 src (226,17) id 1
        // spec: ui_hud_layout.md §5.5b — "Top frame sub-panel (226,17)"
        AtlasTexture? topFrameTex = atlas.SliceById(UitexChromeId, 226, 17, 175, 318);
        if (topFrameTex is not null)
        {
            var topFrame = new TextureRect
            {
                Name = "TopFrame",
                Texture = topFrameTex,
                StretchMode = TextureRect.StretchModeEnum.Scale,
                Position = new Vector2(0f, 0f),
                Size = new Vector2(175f, 318f),
                MouseFilter = MouseFilterEnum.Ignore,
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
                        "spec: Docs/RE/specs/ui_hud_layout.md §5.5b.");
        }

        // --- HP bar fill (172×6, dst 35,5, src 40,517) ---
        // spec: ui_hud_layout.md §5.5b — HP bar fill width = min(172, 172·curHP/maxHP)
        AtlasTexture? hpFillTex = atlas.SliceById(UitexChromeId, HpBarSrcX, HpBarSrcY, (int)HpBarMaxW, (int)HpBarH);
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
                MouseFilter = MouseFilterEnum.Ignore,
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
                MouseFilter = MouseFilterEnum.Ignore,
            };
            AddChild(hpRect);
            _hpFill = hpRect;
        }

        // --- Status icon A — relation icon (12,2) 13×13 src (40,309) ---
        // spec: ui_hud_layout.md §5.5b — "Status icon A (relation) src (40,309) id 1"
        AtlasTexture? statusATex =
            atlas.SliceById(UitexChromeId, StatusASrcX, StatusASrcY, StatusIconSide, StatusIconSide);
        var statusA = new TextureRect
        {
            Name = "StatusIconA",
            Texture = statusATex, // null-safe: TextureRect renders nothing when Texture is null
            StretchMode = TextureRect.StretchModeEnum.Scale,
            Position = new Vector2(StatusAX, StatusAY),
            Size = new Vector2(StatusIconSide, StatusIconSide),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(statusA);

        // --- Status icon B — secondary state (12,17) 13×13 src (278,500) ---
        // spec: ui_hud_layout.md §5.5b — "Status icon B src (278,500) id 1"
        AtlasTexture? statusBTex =
            atlas.SliceById(UitexChromeId, StatusBSrcX, StatusBSrcY, StatusIconSide, StatusIconSide);
        var statusB = new TextureRect
        {
            Name = "StatusIconB",
            Texture = statusBTex,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            Position = new Vector2(StatusBX, StatusBY),
            Size = new Vector2(StatusIconSide, StatusIconSide),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(statusB);

        // --- 3D portrait placeholder (13,55) 200×200 ---
        // spec: ui_hud_layout.md §5.5b — "Portrait box (3D) dst (13,55) 200×200 initially hidden"
        // TODO(world-campaign): live target portrait — build ArrayMesh preview; never GltfDocument.AppendFromBuffer
        var portraitPlaceholder = new ColorRect
        {
            Name = "PortraitPlaceholder",
            Color = new Color(0.1f, 0.1f, 0.1f, 0.4f),
            Position = new Vector2(PortraitDstX, PortraitDstY),
            Size = new Vector2(PortraitSide, PortraitSide),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(portraitPlaceholder);

        // --- Name / relation label — dst (0,3), "[level]name" centred ---
        // spec: ui_hud_layout.md §5.5b — "Label: name/relation [level]name centred; relation colour"
        _nameLabel = new Label
        {
            Name = "NameLabel",
            Text = string.Empty,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Position = new Vector2(0f, 3f),
            Size = new Vector2(FrameW, 12f),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(_nameLabel);

        // --- Percent / relation-tag label — dst (0,18) ---
        // spec: ui_hud_layout.md §5.5b — "Label: percent (0,18)"
        var percentLabel = new Label
        {
            Name = "PercentLabel",
            Text = string.Empty,
            HorizontalAlignment = HorizontalAlignment.Center,
            Position = new Vector2(0f, 18f),
            Size = new Vector2(FrameW, 12f),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(percentLabel);

        // --- Relation tag label — dst (150,12) msg 10037/10038 ---
        // spec: ui_hud_layout.md §5.5b — "Label: relation tag (150,12) msg 10037/10038"
        _relationLabel = new Label
        {
            Name = "RelationTag",
            Text = string.Empty,
            HorizontalAlignment = HorizontalAlignment.Left,
            Position = new Vector2(150f, 12f),
            Size = new Vector2(FrameW - 150f, 12f),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(_relationLabel);

        // --- Close button — dst (190,2) 11×11 action 3 → caption msg 16001 ---
        // spec: ui_hud_layout.md §5.5b — close (190,2) action 3, shows caption msg 16001
        _BuildButton("CloseBtn", CloseX, BtnY, BtnSide,
            atlas, UitexChromeId, 310, 488, onPress: () =>
            {
                // Action id 3 = close/clear target (shows msg 16001)
                // spec: ui_hud_layout.md §5.5b — "action 3; caption msg 16001"
                GD.Print("[HudTargetFrame] Close button (action 3) pressed. " +
                         "spec: Docs/RE/specs/ui_hud_layout.md §5.5b caption msg 16001.");
                ClearTarget();
            });

        // --- Nav up button — dst (202,2) 11×11 action 1 ---
        // spec: ui_hud_layout.md §5.5b — nav up (202,2) action 1
        _BuildButton("NavUpBtn", NavUpX, BtnY, BtnSide,
            atlas, UitexChromeId, 321, 488, onPress: () =>
            {
                GD.Print("[HudTargetFrame] Nav-up button (action 1) pressed. " +
                         "spec: Docs/RE/specs/ui_hud_layout.md §5.5b.");
            });

        // --- Nav down button — dst (214,2) 11×11 action 2 ---
        // spec: ui_hud_layout.md §5.5b — nav down (214,2) action 2
        _BuildButton("NavDownBtn", NavDownX, BtnY, BtnSide,
            atlas, UitexChromeId, 332, 488, onPress: () =>
            {
                GD.Print("[HudTargetFrame] Nav-down button (action 2) pressed. " +
                         "spec: Docs/RE/specs/ui_hud_layout.md §5.5b.");
            });

        GD.Print("[HudTargetFrame] Built — MopGagePanel slot 177, W=226, screen-centred, top. " +
                 "spec: Docs/RE/specs/ui_hud_layout.md §5.5b CODE-CONFIRMED.");
    }

    // -------------------------------------------------------------------------
    // Hub binding
    // -------------------------------------------------------------------------

    /// <summary>Binds the target-frame to the hub's <see cref="IHudEventHub.TargetChanges"/> channel.</summary>
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

        while (_targetChanges.TryRead(out TargetChangedEvent? ev))
        {
            if (ev is null) continue;

            if (ev.IsCleared)
            {
                ClearTarget();
            }
            else
            {
                ApplyTarget(ev);
            }
        }
    }

    // -------------------------------------------------------------------------
    // Apply / clear
    // -------------------------------------------------------------------------

    private void ApplyTarget(TargetChangedEvent ev)
    {
        Visible = true;

        // HP bar fill width = min(172, 172 · hpRatio)
        // spec: ui_hud_layout.md §5.5b — "fill width driven min(172, 172·curHP/maxHP)"
        float fillW = Math.Min(HpBarMaxW, HpBarMaxW * ev.HpRatio);
        switch (_hpFill)
        {
            case TextureRect tr:
                tr.Size = new Vector2(fillW, HpBarH);
                break;
            case ColorRect cr:
                cr.Size = new Vector2(fillW, HpBarH);
                break;
        }

        // "[level]name" label — level not carried by TargetChangedEvent (no level field yet)
        // TODO(world-campaign): include level when the Application surface exposes it
        if (_nameLabel is not null)
            _nameLabel.Text = string.IsNullOrEmpty(ev.Name) ? string.Empty : ev.Name;

        // Relation tag: msg ids 10037 / 10038 drive the text in the original; we don't have
        // a live msg-db here — render a placeholder.
        // spec: ui_hud_layout.md §5.5b — "caption msg 10037/10038"
        // TODO(world-campaign): resolve msg 10037/10038 from the message catalogue
        if (_relationLabel is not null)
            _relationLabel.Text = string.Empty; // placeholder until msg-db is wired
    }

    private void ClearTarget()
    {
        Visible = false;
        if (_nameLabel is not null) _nameLabel.Text = string.Empty;
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
        HudAtlasLibrary atlas, int texId, int srcX, int srcY, System.Action onPress)
    {
        AtlasTexture? normalTex = atlas.SliceById(texId, srcX, srcY, (int)side, (int)side);

        if (normalTex is not null)
        {
            // Texture-rect button using TextureButton (3-state supported)
            var btn = new TextureButton
            {
                Name = nodeName,
                TextureNormal = normalTex,
                Position = new Vector2(x, y),
                Size = new Vector2(side, side),
                MouseFilter = MouseFilterEnum.Stop,
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
                MouseFilter = MouseFilterEnum.Stop,
            };
            btn.Pressed += onPress;
            AddChild(btn);
        }
    }
}