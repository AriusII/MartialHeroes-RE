// Screens/ScreenHost.cs
//
// Hosts a screen Control built on the 1024×768 legacy reference canvas (spec §2.0) and scales it
// to the actual window. spec §8.1 — "implement the UI root as a Control with reference size
// (1024, 768), then scale to the actual window size". Fill-scale (non-uniform on non-4:3 windows)
// is a Godot-side port choice — the spec does not mandate fill vs. letterbox; the original client
// ran at a fixed 1024×768 window, so this distinction did not arise.
//
// IMPORTANT: this control is typically added as a child of a CanvasLayer (a plain Node, not a
// Control). In that topology, Godot's anchor-based layout does NOT propagate the viewport rect —
// a Control whose parent is not a Control will have Size (0,0) until it is explicitly sized.
// We therefore:
//   1. Explicitly set our own Size from GetViewportRect() in _Ready.
//   2. Connect to GetViewport().SizeChanged to keep Size in sync on window resize.
//   3. Keep the manual fit-scale (uniform, centred) for the reference canvas child.
// This guarantees the full-window coverage at any resolution — spec §8.1 "scale to actual window".

using Godot;
using MartialHeroes.Client.Godot.Screens.Layout;

namespace MartialHeroes.Client.Godot.Screens;

/// <summary>
/// A Control that holds one reference-canvas child and rescales it to fill the window on resize.
/// Fill-scale (non-uniform on non-4:3 windows) is a Godot-side port choice; the spec mandates
/// the reference canvas size (1024×768) but not a specific scaling mode.
/// spec: Docs/RE/specs/ui_system.md §8.1 (reference canvas + scale to window).
/// </summary>
public sealed partial class ScreenHost : Control
{
    private const float RefWidth = LoginLayout.RefWidth; // 1024 — spec §2.0
    private const float RefHeight = LoginLayout.RefHeight; // 768  — spec §2.0

    private Control? _canvas;

    public override void _Ready()
    {
        // Explicitly size ourselves to fill the viewport. Anchors alone do not work when the
        // parent is a CanvasLayer (Node, not Control) — we must set Size directly and track
        // SizeChanged on the viewport so we stay full-window across resizes.
        ApplyViewportSize();
        GetViewport().SizeChanged += OnViewportSizeChanged;

        // Ignore mouse on the host itself so the child screen receives input.
        MouseFilter = MouseFilterEnum.Ignore;

        // The reference canvas: a fixed 1024×768 Control that holds the actual screen widgets.
        _canvas = new Control { Name = "RefCanvas" };
        _canvas.Size = new Vector2(RefWidth, RefHeight);
        _canvas.CustomMinimumSize = new Vector2(RefWidth, RefHeight);
        AddChild(_canvas);

        Rescale();
    }

    public override void _ExitTree()
    {
        // Disconnect the viewport signal to avoid dangling callbacks.
        if (IsInstanceValid(GetViewport()))
            GetViewport().SizeChanged -= OnViewportSizeChanged;
    }

    private void OnViewportSizeChanged()
    {
        ApplyViewportSize();
        Rescale();
    }

    /// <summary>Adds a screen built on the reference canvas; replaces any previous child.</summary>
    public void SetScreen(Control screen)
    {
        if (_canvas is null) return;

        foreach (Node child in _canvas.GetChildren())
        {
            if (child is Control) child.QueueFree();
        }

        screen.Size = new Vector2(RefWidth, RefHeight);
        _canvas.AddChild(screen);
        Rescale();
    }

    /// <summary>
    /// Sizes this control to the full viewport rect so it covers the entire window, even when the
    /// parent is a CanvasLayer (no Control anchor propagation in that topology).
    /// </summary>
    private void ApplyViewportSize()
    {
        Vector2 vpSize = GetViewportRect().Size;
        if (vpSize.X > 0 && vpSize.Y > 0)
        {
            Position = Vector2.Zero;
            Size = vpSize;
        }
    }

    private void Rescale()
    {
        if (_canvas is null) return;

        Vector2 windowSize = Size;
        if (windowSize.X <= 0 || windowSize.Y <= 0)
        {
            // Fallback: read directly from the viewport in case Size hasn't propagated yet.
            windowSize = GetViewportRect().Size;
        }

        if (windowSize.X <= 0 || windowSize.Y <= 0) return;

        // Fill-scale: stretch the legacy 1024×768 canvas to fill the entire window edge-to-edge.
        // spec: Docs/RE/specs/ui_system.md §8.1 "scale to the actual window size".
        // Godot-side port choice: non-uniform fill (slight stretch on non-4:3 windows) rather than
        // letterboxing, because the original ran at a fixed 1024×768 window with no bars.
        // For a 1024×768 window the scale is exactly 1×1 (pixel-perfect).
        float scaleX = windowSize.X / RefWidth;
        float scaleY = windowSize.Y / RefHeight;
        _canvas.Scale = new Vector2(scaleX, scaleY);
        _canvas.Position = Vector2.Zero;
    }
}