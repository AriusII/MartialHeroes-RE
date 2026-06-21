// Ui/Widgets/HudPanel.cs
//
// HUD panel widget — reimplementation of GUPanel: an atlas-backed container.
//
// Behaviour:
//   - Optional atlas sprite drawn as a full-panel background TextureRect.
//   - Children added in insertion order; paint back→front.
//   - Reverse hit-test: children added LATER (painted on top) win input.
//   - Deferred-removal sweep inherited from HudWidget.
//
// spec: Docs/RE/specs/ui_system.md §1 — GUPanel: container, child-pointer vector, active-child.
// spec: Docs/RE/specs/ui_system.md §3.1 — render path: "child vector front→end, back→front".
// spec: Docs/RE/specs/ui_system.md §4.4 — GUList/GUPanel reverse hit-test for z-order.
// spec: Docs/RE/structs/gucomponent.md — GUPanel child vector region +0xA4..+0xB8.

using Godot;

namespace MartialHeroes.Client.Godot.Ui.Widgets;

/// <summary>
///     GUPanel-faithful atlas-backed container for HUD widgets.
///     <para>
///         Wraps a Godot <see cref="Control" /> and optionally a background
///         <see cref="TextureRect" />. Children are added as Godot child nodes; paint order =
///         insertion order (back→front).
///     </para>
///     spec: Docs/RE/specs/ui_system.md §1 (GUPanel role), §3.1 (render order), §4.4 (hit-test).
/// </summary>
public sealed class HudPanel : HudWidget
{
    private readonly Control _root;

    // -------------------------------------------------------------------------
    // Construction
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Creates a HudPanel, optionally backed by an atlas texture sub-rect.
    /// </summary>
    /// <param name="x">Screen-local X on the 1024×768 canvas.</param>
    /// <param name="y">Screen-local Y.</param>
    /// <param name="w">Width in pixels.</param>
    /// <param name="h">Height in pixels.</param>
    /// <param name="background">
    ///     Optional atlas texture for the panel background.
    ///     When null the panel is transparent (container only).
    /// </param>
    public HudPanel(int x, int y, int w, int h, Texture2D? background = null, bool modalFlag = false)
    {
        _root = new Control
        {
            Position = new Vector2(x, y),
            Size = new Vector2(w, h),
            CustomMinimumSize = new Vector2(w, h),
            ClipContents = modalFlag,
            MouseFilter = modalFlag ? Control.MouseFilterEnum.Stop : Control.MouseFilterEnum.Pass
        };

        if (background is not null)
        {
            // Background drawn first (lowest z-order in this panel).
            // spec: §3.1 — "DrawSelf then children back→front".
            var bg = new TextureRect
            {
                Texture = background,
                AnchorsPreset = (int)Control.LayoutPreset.FullRect,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                // The atlas texture is already a W×H slice, so native draw size is the destination size.
                // spec: Docs/RE/specs/frontend_scenes.md — source extent equals destination W×H.
                StretchMode = TextureRect.StretchModeEnum.Keep,
                MouseFilter = Control.MouseFilterEnum.Ignore
            };
            _root.AddChild(bg);
        }
    }

    // -------------------------------------------------------------------------
    // HudWidget
    // -------------------------------------------------------------------------

    /// <inheritdoc />
    public override Control? GetControl()
    {
        return _root;
    }
}