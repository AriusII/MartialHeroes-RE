using Godot;
using MartialHeroes.Client.Godot.Ui.Assets;

namespace MartialHeroes.Client.Godot.Ui.Hud;

public sealed partial class HudHelpOverlay : Control
{
    private const string HelpDdsVfsPath = "data/ui/help.dds";
    private TextureRect? _helpImage;


    private bool _visible;


    public void Build(HudAtlasLibrary atlas)
    {
        Name = "HudHelpOverlay";

        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;

        Visible = false;
        _visible = false;

        ZIndex = 100;

        _helpImage = new TextureRect
        {
            Name = "HelpImage",
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = MouseFilterEnum.Ignore
        };
        _helpImage.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        var helpTex = atlas?.GetByPath(HelpDdsVfsPath);
        if (helpTex != null)
        {
            _helpImage.Texture = helpTex;
        }
        else
        {
            GD.PrintErr("[HudHelpOverlay] data/ui/help.dds not found in VFS — showing fallback. " +
                        "spec: Docs/RE/specs/ui_system.md §8.24.2 CODE-CONFIRMED.");
            var fallback = new ColorRect
            {
                Name = "HelpFallback",
                Color = new Color(0.05f, 0.05f, 0.10f, 0.95f),
                MouseFilter = MouseFilterEnum.Ignore
            };
            fallback.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            AddChild(fallback);

            var fallbackLbl = new Label
            {
                Name = "HelpFallbackLabel",
                Text = "[ data/ui/help.dds — VFS asset absent ]",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                MouseFilter = MouseFilterEnum.Ignore
            };
            fallbackLbl.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            AddChild(fallbackLbl);
        }

        AddChild(_helpImage);

        var closeHint = new Label
        {
            Name = "CloseHint",
            Text = "[H] 닫기",
            Position = new Vector2(10f, 10f),
            MouseFilter = MouseFilterEnum.Ignore
        };
        AddChild(closeHint);

        GD.Print("[HudHelpOverlay] Built — full-screen help overlay (HelpPanel member §8.24). " +
                 "Image: data/ui/help.dds (literal path, not uitex id). " +
                 "Toggle: key h (§8.17.1) / DefaultMenu action 4011 (§8.23.3). " +
                 "Page count = 1, no navigation (§8.24.2). " +
                 "spec: Docs/RE/specs/ui_system.md §8.24 CODE-CONFIRMED.");
    }


    public void Toggle()
    {
        _visible = !_visible;
        Visible = _visible;
        GD.Print($"[HudHelpOverlay] Toggle → {(_visible ? "SHOWN" : "HIDDEN")}. " +
                 "spec: Docs/RE/specs/ui_system.md §8.24.3 CODE-CONFIRMED.");
    }

    public void Toggle(bool show)
    {
        _visible = show;
        Visible = show;
    }

    public override void _Input(InputEvent @event)
    {
        if (!_visible) return;
        if (@event is InputEventKey { Pressed: true })
        {
            Toggle(false);
            GetViewport().SetInputAsHandled();
        }
    }
}