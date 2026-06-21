// Ui/Scenes/Select/CharSelectWindow.Composition.cs
//
// Character-select window — 3D SubViewport wiring + widget construction helpers.
// Part of the CharSelectWindow partial class split (Wave 4b).
//
// Covers: Build3DSceneViewport(), InitialiseScene3D(),
//         BuildTabButton(), BuildRosterButton(), BuildGridCellInto(),
//         AddAtlasRect(), AddAtlasRectTo(), AddInfoLabel().
//
// PASSIVE: zero game logic, zero domain state.
// spec: Docs/RE/specs/frontend_scenes.md §3.7.
// spec: Docs/RE/specs/ui_system.md §8.2/§8.4.

using Godot;
using MartialHeroes.Client.Godot.Ui.Widgets;

namespace MartialHeroes.Client.Godot.Ui.Scenes.Select;

public sealed partial class CharSelectWindow
{
    // =========================================================================
    // 3D scene SubViewport. spec: frontend_scenes.md §3.7. CODE-CONFIRMED.
    // =========================================================================

    private void Build3DSceneViewport()
    {
        _scene3DViewport = new SubViewport
        {
            Name = "CharSelect3DViewport",
            Size = new Vector2I(1024, 768), // spec §8.0 "1024×768". CODE-CONFIRMED.
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            TransparentBg = false
        };

        _scene3D = new CharSelectScene3D { Name = "CharSelectScene3D" };
        PushSlotDescriptors();
        _scene3DViewport.AddChild(_scene3D);

        _scene3DContainer = new SubViewportContainer
        {
            Name = "Scene3DContainer",
            Stretch = true,
            MouseFilter = MouseFilterEnum.Pass
        };
        _scene3DContainer.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _scene3DContainer.AddChild(_scene3DViewport);
        AddChild(_scene3DContainer);

        // Force the 3D backdrop to sibling index 0 (backmost) so all 2D chrome siblings
        // added afterward paint on top.  In Godot 4 Control nodes follow back-to-front
        // child-vector insertion order — the last sibling added draws in front.
        // This MoveChild is a defensive guarantee: even if a future wave adds any Control
        // child before this call, the SubViewportContainer stays at the back.
        // spec: Docs/RE/specs/ui_system.md §7.3 ("Paint order = insertion order: later children
        //       paint on top") and §3.1 child-vector draw walk (front → end, back-to-front).
        MoveChild(_scene3DContainer, 0);

        Callable.From(InitialiseScene3D).CallDeferred();
    }

    private void InitialiseScene3D()
    {
        if (_scene3D is null || !IsInstanceValid(_scene3D)) return;
        try
        {
            _scene3D.Initialise(_realAssets);
            _scene3D.SetSelectedSlot(_selectedSlot);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[CharSelectWindow] InitialiseScene3D failed: {ex.Message}");
        }
    }

    // =========================================================================
    // Widget construction helpers
    // =========================================================================

    private void BuildTabButton(
        int x, int y, int w, int h,
        int normSrcX, int normSrcY,
        int hovSrcX, int hovSrcY,
        int prsSrcX, int prsSrcY,
        int actionId)
    {
        if (Atlas is null) return;
        var btn = HudWidgetFactory.MakeButton(
            Atlas, AtlasLoginWindow,
            x, y, w, h,
            normSrcX, normSrcY,
            prsSrcX, prsSrcY, // PRESSED
            hovSrcX, hovSrcY, // HOVER
            actionId);
        btn.ActionFired += OnTabButtonFired;
        AddChild(btn.GetControl()!);
    }

    // D5: Returns the backing Control so the caller can store it for visibility gating.
    // spec: Docs/RE/specs/frontend_scenes.md §3.2/§3.4 (slot buttons gated by occupancy).
    private Control? BuildRosterButton(
        int x, int y,
        int normSrcX, int normSrcY,
        int prsSrcX, int prsSrcY,
        int actionId)
    {
        if (Atlas is null) return null;
        var btn = HudWidgetFactory.MakeButton(
            Atlas, AtlasLoginWindow,
            x, y, (int)RosterBtnW, (int)RosterBtnH,
            normSrcX, normSrcY,
            prsSrcX, prsSrcY,
            normSrcX, normSrcY, // HOVER = NORMAL (spec §1.5 2-state fallback)
            actionId);
        btn.ActionFired += OnRosterButtonFired;
        var ctrl = btn.GetControl();
        if (ctrl is not null) AddChild(ctrl);
        return ctrl;
    }

    private void BuildGridCellInto(
        Control parent,
        int x, int y,
        int normSrcX, int normSrcY,
        int hovSrcX, int hovSrcY,
        int actionId)
    {
        if (Atlas is null) return;
        var cell = HudWidgetFactory.MakeButton3(
            Atlas, AtlasLoginWindow,
            x, y, StatIconW, StatIconH,
            normSrcX, normSrcY,
            hovSrcX, hovSrcY,
            actionId);
        // Point-buy ± intent: emitted as an ApplicationUseCases call by SelectScene.
        // We fire ActionFired here; SelectScene wires it. (No game logic in this class.)
        parent.AddChild(cell.GetControl()!);
    }

    private void AddAtlasRect(string vfsPath, int x, int y, int w, int h, int srcX, int srcY)
    {
        AddAtlasRectTo(this, vfsPath, x, y, w, h, srcX, srcY);
    }

    private void AddAtlasRectTo(
        Control parent, string vfsPath,
        int x, int y, int w, int h, int srcX, int srcY)
    {
        if (Atlas is null) return;
        var rect = HudWidgetFactory.MakeAtlasRect(Atlas, vfsPath, x, y, w, h, srcX, srcY);
        if (rect is not null) parent.AddChild(rect);
    }

    private HudLabel AddInfoLabel(Vector2 pos)
    {
        var widget = HudWidgetFactory.MakeLabel(
            (int)pos.X, (int)pos.Y, 120, 14,
            color: new Color(0.85f, 0.85f, 0.9f));
        var ctrl = widget.GetControl();
        if (ctrl is not null) AddChild(ctrl);
        return widget;
    }
}