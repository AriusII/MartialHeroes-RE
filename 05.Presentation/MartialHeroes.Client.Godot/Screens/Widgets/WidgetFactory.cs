// Screens/Widgets/WidgetFactory.cs
//
// Static factory helpers consumed by all screens.  Creates:
//   - StateButton instances with proper atlas slicing (via UiAssetLoader.Slice)
//   - Labels with the legacy fixed-advance text feel
//   - Alpha-fade show/hide helpers that target any Control
//
// PASSIVE: all factory methods are pure constructors / configurators.
// Zero game logic, zero per-frame allocations, zero domain state.
//
// FONT DEVIATION NOTE:
//   The original client used Korean Windows system faces (DotumChe / Dotum / BatangChe) via
//   D3DXCreateFontA with charset 129 (HANGUL_CHARSET).  Those are Windows-only GDI fonts and
//   are not available in Godot's renderer.  We substitute NanumGothicCoding (or the best
//   available Godot system font) at the matching pixel heights from the spec table and set
//   monospace-ish spacing via fixed Label SizeFlagsHorizontal so layout matches the
//   fixed-advance grid.  The deviation is intentional and documented here.
//   spec: Docs/RE/specs/ui_system.md §6.2 (15-slot font table), §6.3 (fixed-advance grid),
//         §13.4 (Godot font mapping guidance).
//
// ATLAS SLICING:
//   Slice() delegates to UiAssetLoader.Slice which sets FilterClip=true on the resulting
//   AtlasTexture. This is a Godot-side mitigation against atlas bleed at non-integer scale
//   factors — ui_system.md §13.1 provides Godot reconstruction guidance but does not mandate
//   FilterClip as a legacy spec value.
//
// ALPHA FADE:
//   BeginShow/BeginHide target a Control's alpha-equivalent: Modulate.A chased at ±64/tick.
//   The per-tick advance matches the GUComponent fast-path value (not the ±32 GUComponentEx
//   slow path which is for background/overlay elements).
//   spec: Docs/RE/specs/ui_system.md §7.1 (alpha-fade show/hide ±64/tick).
//
// spec: Docs/RE/specs/ui_system.md §1.5, §4, §6, §7.1, §13.2, §13.4.

using Godot;

namespace MartialHeroes.Client.Godot.Screens.Widgets;

/// <summary>
/// Static factory and helper methods for creating GU-faithful UI widgets.
///
/// <para>All methods require a <see cref="UiAssetLoader"/> for VFS-backed atlas slicing.
/// They degrade gracefully (no atlas sprite, placeholder label) when the VFS is offline.</para>
///
/// <para>No per-frame allocations occur in any method here; factories are called at construction
/// time only.</para>
/// </summary>
public static class WidgetFactory
{
    // -------------------------------------------------------------------------
    // StateButton factory
    // -------------------------------------------------------------------------

    /// <summary>
    /// Creates a <see cref="StateButton"/> pre-wired with atlas frames from the VFS.
    ///
    /// <para>All three frames (NORMAL, HOVER, PRESSED) are sliced from the same atlas at the same
    /// <c>(w, h)</c> size but different <c>(srcX, srcY)</c> origins, matching the legacy 7-state
    /// button constructor.  When any frame origin equals the NORMAL origin the
    /// <see cref="StateButton"/> automatically uses NORMAL for that state (2-state / 3-state
    /// fallback behaviour).</para>
    ///
    /// <para><b>Frame-selection precedence</b> (spec §1.5):
    /// disabled ≻ pressed ≻ hovered ≻ normal.</para>
    ///
    /// <para>The returned button is NOT yet added to any parent; add it and connect
    /// <see cref="StateButton.ActionFired"/> to your intent handler.</para>
    /// </summary>
    /// <param name="loader">Active <see cref="UiAssetLoader"/>; may be offline (VFS absent).</param>
    /// <param name="atlasPath">VFS-relative path to the DDS atlas sheet.</param>
    /// <param name="destX">Button screen-local X on the reference canvas (spec §8.0).</param>
    /// <param name="destY">Button screen-local Y on the reference canvas.</param>
    /// <param name="w">Button width in pixels (spec §1.1 field +0x1C).</param>
    /// <param name="h">Button height in pixels (spec §1.1 field +0x20).</param>
    /// <param name="normalSrcX">Atlas X of the NORMAL frame (spec §1.5 +0xC8).</param>
    /// <param name="normalSrcY">Atlas Y of the NORMAL frame (spec §1.5 +0xCC).</param>
    /// <param name="hoverSrcX">Atlas X of the HOVER frame (spec §1.5 +0xD0). Pass same as normal for 2-state.</param>
    /// <param name="hoverSrcY">Atlas Y of the HOVER frame (spec §1.5 +0xD4).</param>
    /// <param name="pressedSrcX">Atlas X of the PRESSED frame (spec §1.5 +0xD8). Pass same as normal for 2-state.</param>
    /// <param name="pressedSrcY">Atlas Y of the PRESSED frame (spec §1.5 +0xDC).</param>
    /// <param name="actionId">Integer action id delivered to <see cref="StateButton.ActionFired"/> on click
    ///   (spec §1.2 field +0x10).</param>
    /// <param name="caption">Optional caption text.  Arrives already-decoded from msg.xdb; rendered as-is.</param>
    /// <param name="captionTint">Per-widget tint for the normal/pressed caption (default white).
    ///   spec §1.2 — "+0x0C tint/colour RGB".</param>
    /// <returns>A configured <see cref="StateButton"/> ready to be added to a parent
    /// <see cref="Control"/>.</returns>
    public static StateButton MakeStateButton(
        UiAssetLoader loader,
        string atlasPath,
        int destX, int destY, int w, int h,
        int normalSrcX, int normalSrcY,
        int hoverSrcX, int hoverSrcY,
        int pressedSrcX, int pressedSrcY,
        int actionId,
        string caption = "",
        Color? captionTint = null)
    {
        // Slice the three frames from the atlas.
        // UiAssetLoader.Slice sets FilterClip=true as a Godot-side atlas-bleed mitigation.
        AtlasTexture? normalFrame = loader.Slice(atlasPath, normalSrcX, normalSrcY, w, h);
        AtlasTexture? hoverFrame = loader.Slice(atlasPath, hoverSrcX, hoverSrcY, w, h);
        AtlasTexture? pressedFrame = loader.Slice(atlasPath, pressedSrcX, pressedSrcY, w, h);

        var btn = new StateButton
        {
            Name = $"StateBtn_{actionId}",
            Position = new Vector2(destX, destY),
            Size = new Vector2(w, h),
            CustomMinimumSize = new Vector2(w, h),
            NormalFrame = normalFrame,
            HoverFrame = hoverFrame,
            PressedFrame = pressedFrame,
            ActionId = actionId,
            Caption = caption,
            CaptionTint = captionTint ?? Colors.White,
        };

        return btn;
    }

    /// <summary>
    /// Convenience overload for a <b>2-state</b> button (no distinct HOVER or PRESSED frame —
    /// all states use the NORMAL sprite; only the caption tint changes on hover).
    /// spec: Docs/RE/specs/ui_system.md §1.5 — "2-state: HOVER = NORMAL; PRESSED = NORMAL".
    /// </summary>
    public static StateButton MakeStateButton2(
        UiAssetLoader loader,
        string atlasPath,
        int destX, int destY, int w, int h,
        int normalSrcX, int normalSrcY,
        int actionId,
        string caption = "",
        Color? captionTint = null)
        => MakeStateButton(
            loader, atlasPath,
            destX, destY, w, h,
            normalSrcX, normalSrcY,
            normalSrcX, normalSrcY, // HOVER = NORMAL (spec §1.5 2-state)
            normalSrcX, normalSrcY, // PRESSED = NORMAL
            actionId, caption, captionTint);

    // -------------------------------------------------------------------------
    // Label factory
    // -------------------------------------------------------------------------

    /// <summary>
    /// Creates a <see cref="Label"/> styled to approximate the legacy fixed-advance text look.
    ///
    /// <para><b>Font deviation note:</b> the original client drew text via <c>D3DXCreateFontA</c>
    /// with <c>HANGUL_CHARSET = 129</c> using Korean Windows system faces (DotumChe / Dotum /
    /// BatangChe).  Those GDI faces are unavailable in Godot.  We set the Godot font size to the
    /// <c>D3DX Height (rowHeight)</c> value from the spec §6.2 table and leave the font family to
    /// the project theme, which should point at a CJK-capable face (NanumGothicCoding or similar).
    /// Horizontal extent remains <c>charWidth × strlen</c> as the spec requires (fixed-advance
    /// grid), so callers must size the label container accordingly.
    /// spec: Docs/RE/specs/ui_system.md §6.2 (font table), §6.3 (fixed-advance grid), §13.4.</para>
    ///
    /// <para>CP949 strings arriving from <see cref="UiAssetLoader.Text"/> are already decoded
    /// to .NET strings; they are passed directly to <c>Label.Text</c> — we never decode bytes
    /// in the UI layer.</para>
    /// </summary>
    /// <param name="text">Initial text (may be a decoded CP949 string from msg.xdb).</param>
    /// <param name="fontPixelHeight">Pixel height from the spec §6.2 <c>D3DX Height</c> column.
    ///   Use the slot that matches the widget's context (e.g. 12 for DotumChe slot 0).</param>
    /// <param name="color">Text colour (ARGB).</param>
    /// <param name="multiline">When true, enables auto-wrap for multi-line labels
    ///   (<see cref="TextServer.AutowrapMode.WordSmart"/>).</param>
    /// <returns>A configured <see cref="Label"/>.</returns>
    public static Label MakeLabel(
        string text,
        int fontPixelHeight,
        Color color,
        bool multiline = false)
    {
        // Clamp to a minimum readable height; the spec's smallest font is 10px (slot 1/11).
        // spec: Docs/RE/specs/ui_system.md §6.2 — minimum rowHeight = 10.
        int size = Mathf.Max(10, fontPixelHeight);

        var label = new Label
        {
            Text = text,
            // Disable autowrap for fixed-advance grid labels by default.
            AutowrapMode = multiline
                ? TextServer.AutowrapMode.WordSmart
                : TextServer.AutowrapMode.Off,
            // Clip horizontally so the label doesn't overflow its allocated column.
            ClipText = !multiline,
        };

        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color);

        return label;
    }

    // -------------------------------------------------------------------------
    // Atlas TextureRect factory — image widget (no button behaviour)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Creates a <see cref="TextureRect"/> showing a sub-rect of an atlas DDS, or null when offline.
    ///
    /// <para>Useful for pure-image chrome panels (e.g. §11.5a char-info background plates)
    /// that have no click / action behaviour.</para>
    ///
    /// spec: Docs/RE/specs/ui_system.md §1.3 — atlas pixel map 1:1 on 1024×768 canvas.
    /// </summary>
    public static TextureRect? MakeAtlasRect(
        UiAssetLoader loader,
        string atlasPath,
        int destX, int destY, int w, int h,
        int srcX, int srcY)
    {
        AtlasTexture? tex = loader.Slice(atlasPath, srcX, srcY, w, h);
        if (tex is null) return null;

        return new TextureRect
        {
            Texture = tex,
            Position = new Vector2(destX, destY),
            Size = new Vector2(w, h),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
    }

    /// <summary>
    /// Creates a <see cref="StateButton"/> using the NORMAL frame only (2-state atlas button).
    ///
    /// <para>Convenience wrapper for atlas buttons with no distinct hover/pressed art when offline
    /// the returned button is null (faithfully empty offline).</para>
    ///
    /// spec: Docs/RE/specs/ui_system.md §1.5 — "2-state: HOVER = NORMAL; PRESSED = NORMAL".
    /// </summary>
    public static StateButton? MakeAtlasButton(
        UiAssetLoader loader,
        string atlasPath,
        int destX, int destY, int w, int h,
        int normalSrcX, int normalSrcY,
        int actionId,
        string caption = "")
    {
        AtlasTexture? normalFrame = loader.Slice(atlasPath, normalSrcX, normalSrcY, w, h);
        if (normalFrame is null) return null; // faithfully empty when VFS absent

        var btn = new StateButton
        {
            Name = $"StateBtn_{actionId}",
            Position = new Vector2(destX, destY),
            Size = new Vector2(w, h),
            CustomMinimumSize = new Vector2(w, h),
            NormalFrame = normalFrame,
            HoverFrame = normalFrame,
            PressedFrame = normalFrame,
            ActionId = actionId,
            Caption = caption,
            CaptionTint = Colors.White,
        };

        return btn;
    }

    // -------------------------------------------------------------------------
    // Atlas slice helper (thin delegation to UiAssetLoader)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns an <see cref="AtlasTexture"/> for the sub-rect at <c>(srcX, srcY, w, h)</c>
    /// within the named atlas, or <c>null</c> when the VFS is offline.
    ///
    /// <para><c>FilterClip=true</c> is set by <see cref="UiAssetLoader.Slice"/> as a Godot-side
    /// mitigation against atlas bleed at non-integer scale factors; it is not a spec literal
    /// (ui_system.md §13.1 provides Godot reconstruction guidance, not a legacy requirement).</para>
    ///
    /// <para>This wrapper exists so callers in <c>Screens/Widgets/</c> and <c>Screens/</c> can
    /// call a single consistent API rather than holding their own <see cref="UiAssetLoader"/>
    /// reference in every helper function.</para>
    /// </summary>
    /// <param name="loader">Active <see cref="UiAssetLoader"/>.</param>
    /// <param name="vfsPath">VFS-relative DDS path (e.g. <c>"data/ui/login_slice1.dds"</c>).</param>
    /// <param name="srcX">Atlas pixel X of the sub-rect origin.</param>
    /// <param name="srcY">Atlas pixel Y of the sub-rect origin.</param>
    /// <param name="w">Sub-rect width in pixels.</param>
    /// <param name="h">Sub-rect height in pixels.</param>
    /// <returns>An <see cref="AtlasTexture"/> with <c>FilterClip=true</c>, or <c>null</c>.</returns>
    public static AtlasTexture? Slice(
        UiAssetLoader loader,
        string vfsPath,
        int srcX, int srcY, int w, int h)
        => loader.Slice(vfsPath, srcX, srcY, w, h);

    // -------------------------------------------------------------------------
    // Alpha-fade show/hide helpers for arbitrary Control nodes
    // -------------------------------------------------------------------------

    /// <summary>
    /// Starts a fade-in for an arbitrary <see cref="Control"/> by setting its
    /// <see cref="CanvasItem.Modulate"/> alpha target to 1.
    ///
    /// <para>This helper creates a one-shot <see cref="AlphaFadeTween"/> child on the control the
    /// first time it is called, then reuses it.  The tween runs at 64 alpha-units per frame
    /// (normalised to 0..1: step = 64/255 per <c>_Process</c> tick), matching the GUComponent
    /// fast-path fade rate.
    /// spec: Docs/RE/specs/ui_system.md §7.1 — "±64 per tick; GUComponent fast path".</para>
    ///
    /// <para>The control's Visible flag is set to <c>true</c> immediately so it can receive input
    /// as the alpha rises.</para>
    /// </summary>
    /// <param name="control">The control to fade in.</param>
    public static void BeginShow(Control control)
    {
        GetOrAddFadeDriver(control).SetTarget(1f);
        control.Visible = true;
    }

    /// <summary>
    /// Starts a fade-out for an arbitrary <see cref="Control"/> by setting its alpha target to 0.
    ///
    /// <para>The control's Visible flag is set to <c>false</c> once the alpha reaches zero
    /// (handled by <see cref="AlphaFadeDriver"/> internally so the control stops receiving input
    /// when fully hidden).
    /// spec: Docs/RE/specs/ui_system.md §7.1 — "hiding → alpha falls −64 toward 0".</para>
    /// </summary>
    /// <param name="control">The control to fade out.</param>
    public static void BeginHide(Control control)
    {
        GetOrAddFadeDriver(control).SetTarget(0f);
    }

    /// <summary>
    /// Pins a <see cref="Control"/> to full alpha immediately, bypassing the fade
    /// (matching spec +0x0F forced-alpha override).
    /// spec: Docs/RE/specs/ui_system.md §7.1 — "forced-alpha byte ≠ 0xFF pins alpha immediately".
    /// </summary>
    /// <param name="control">The control to pin visible.</param>
    public static void ShowInstant(Control control)
    {
        RemoveFadeDriver(control);
        control.Modulate = Colors.White;
        control.Visible = true;
    }

    /// <summary>
    /// Pins a <see cref="Control"/> to zero alpha immediately, bypassing the fade.
    /// spec: Docs/RE/specs/ui_system.md §7.1 — "forced-alpha byte ≠ 0xFF pins alpha immediately".
    /// </summary>
    /// <param name="control">The control to pin hidden.</param>
    public static void HideInstant(Control control)
    {
        RemoveFadeDriver(control);
        control.Modulate = new Color(1f, 1f, 1f, 0f);
        control.Visible = false;
    }

    // -------------------------------------------------------------------------
    // AlphaFadeDriver management
    // -------------------------------------------------------------------------

    private static readonly StringName FadeDriverName = new("__AlphaFadeDriver");

    private static AlphaFadeDriver GetOrAddFadeDriver(Control control)
    {
        Node? existing = control.FindChild(FadeDriverName, owned: false);
        if (existing is AlphaFadeDriver driver) return driver;

        // First call: add a driver child.
        var newDriver = new AlphaFadeDriver { Name = FadeDriverName };
        control.AddChild(newDriver);
        return newDriver;
    }

    private static void RemoveFadeDriver(Control control)
    {
        Node? existing = control.FindChild(FadeDriverName, owned: false);
        existing?.QueueFree();
    }
}