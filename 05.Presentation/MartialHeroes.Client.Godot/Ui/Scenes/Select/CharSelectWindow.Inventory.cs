// Ui/Scenes/Select/CharSelectWindow.Inventory.cs
//
// Character-select 2D builder — the COMPLETE 124-widget census for CharSelectWindow.
// NOTE: "Inventory" in the partial-class filename refers to the WIDGET CENSUS / TREE
// (the 124 GU-ctor sites recovered from frontend_scenes.md §11.5h), NOT to the in-game
// item inventory. Char-select has no item inventory; this file builds the 2D chrome tree.
// HARD TABLE-RASE of the prior ad-hoc layout: every widget below is one of the 124
// CODE-CONFIRMED GU-ctor sites from frontend_scenes.md §11.5h, built in build (= paint/Z)
// order, with EXACTLY the 42 parent-add-with-action bindings — no phantom buttons, no
// orphaned bindings.
//
// CRITICAL ANTIDOTES to the prior garbage (spec §11.5h window-level duplicate verdict):
//   (i)  The 244×187 panel (bi#2) and the SECOND 244×187 panel (bi#82), and the FIVE
//        340×190 modals (bi#21,26,29,110,116), are DISTINCT, MUTUALLY-EXCLUSIVE widgets
//        on DISTINCT parents that SHARE atlas sprites. They are built as separate gated
//        nodes — a bar is NEVER drawn twice into the same parent. The shared-sprite reuse
//        is the documented origin of any earlier "duplicate bar".
//   (ii) Every Button3 maps to exactly one of the 42 actions (via AddChildWithAction) or
//        is a documented plain parent-add (bi#80/81/93/94/106); nothing extra.
//   (iii)The single-BGM guard lives in FrontEndAudio.PlayBgm (one shared slot + per-id
//        dedup), invoked once by SelectScene; never double-plays. spec §3.8.1.
//
// Build groups (frontend_scenes.md §11.5h ordered widget table):
//   G1  bi#1–20   roster frame + top command strip            (loginwindow)
//   G2  bi#21–25  create-class modal A                        (inventwindow 340×190)
//   G3  bi#26–28  caption-only notice modal B                 (inventwindow 340×190)
//   G4  bi#29–32  delete-confirm-style modal C                (inventwindow 340×190)
//   G5  bi#33–36  bottom roster action-button row             (loginwindow)
//   G6  bi#37–65  right detail panel 244×474 + stat column    (loginwindow / mainwindow)
//   G7  bi#66–81  per-slot mini controls                      (loginwindow)
//   G8  bi#82–105 inner sub-panel (2nd 244×187: class-pick)   (loginwindow)
//   G9  bi#106–109 close + notice + textbox + exit chrome     (tradekeep / inventwindow)
//   G10 bi#110–115 create-NAME modal D                        (inventwindow 340×190)
//   G11 bi#116–123 rename modal E                             (inventwindow 340×190)
//   Terminal: slot-count label refresh + 3D scene build + BGM start.
//
// All 'Reg' dst/src fields are [OPEN@DEBUGGER] in the spec (computed at open from layout
// state). We place them at a documented SENSIBLE DEFAULT and tag each // OPEN@DEBUGGER —
// geometry is NOT invented beyond what the spec pins; Reg positions are best-effort layout.
//
// PASSIVE: zero game logic. Every gesture → an action id → OnAction(int) → a signal/use-case
// intent. ZERO domain state, ZERO packet parsing, ZERO optimistic mutation.
//
// spec: Docs/RE/specs/frontend_scenes.md §11.5h (124-widget inventory + 42 action bindings).
// spec: Docs/RE/specs/frontend_scenes.md §3.2/§3.4 (slot info row, action-61 gating).
// spec: Docs/RE/specs/ui_system.md §1.5/§7.3/§7.4 (button/panel/composition primitives).
// spec: Docs/RE/specs/frontend_scenes.md §11.8 (font slots).

using Godot;
using MartialHeroes.Client.Godot.Ui.Widgets;

namespace MartialHeroes.Client.Godot.Ui.Scenes.Select;

public sealed partial class CharSelectWindow
{
    private Control? _bottomRow; // G5 bi#33–36 bottom roster action row
    private Control? _innerSubPanel; // G8 bi#82–105 second 244×187 (class-pick)
    private Control? _modalCreateClass; // G2 bi#21–25 modal A (create-class)
    private Control? _modalCreateName; // G10 bi#110–115 create-NAME modal D
    private Control? _modalDeleteCfm; // G4 bi#29–32 modal C (delete-confirm-style)
    private Control? _modalNotice; // G3 bi#26–28 modal B (caption-only notice)
    private Control? _modalRename; // G11 bi#116–123 rename modal E
    private Control? _noticeSmall; // G9 bi#107 small notice panel

    // The action-61 conditional overlay button (bi#33) — per-slot gated by +0x1548.
    private Control? _overlayAction61;
    private Control? _perSlotControls; // G7 bi#66–81 per-slot mini controls
    private Control? _rightDetail; // G6 bi#37–65 right detail panel 244×474

    // -------------------------------------------------------------------------
    // Modal / sub-form parent containers — DISTINCT, MUTUALLY-EXCLUSIVE nodes.
    // Each shares atlas sprites with siblings but is its OWN gated parent (antidote i).
    // -------------------------------------------------------------------------

    private Control? _topStrip; // G1 bi#1–20 roster frame + command strip
    private int _wActionBindings;
    private int _wButtons;
    private int _wImages;

    private int _wLabels;
    // =========================================================================
    // The 124-widget inventory tally (CODE-CONFIRMED counts, frontend_scenes.md §11.5h).
    //   10 Panel + 37 Image + 46 Button3 + 29 Label + 2 Textbox = 124.
    //   42 parent-add-with-action bindings.
    // Tallied at build to assert the rebuild wires exactly the inventory.
    // =========================================================================

    private int _wPanels;
    private int _wTextboxes;

    // =========================================================================
    // Entry point — replaces the prior ad-hoc BuildUi widget tree.
    // =========================================================================

    private void BuildInventory()
    {
        // LAYER 0 — the 3D backdrop (full canvas, backmost). Reused seam (CharSelectScene3D).
        // The 5-slot preview scene build is the terminal "3D scene build" step of §11.5h.
        Build3DSceneViewport();

        if (Atlas is null)
            GD.PushWarning("[CharSelectWindow] Atlas offline — inventory chrome will be invisible " +
                           "(widgets still built/functional). spec: frontend_scenes.md §11.5h.");

        // LAYER 1 — the 124 2D widgets, in build (paint/Z) order.
        BuildGroup1RosterFrame(); // bi#1–20
        BuildGroup2CreateClassModal(); // bi#21–25
        BuildGroup3NoticeModal(); // bi#26–28
        BuildGroup4DeleteConfirmModal(); // bi#29–32
        BuildGroup5BottomRow(); // bi#33–36
        BuildGroup6RightDetail(); // bi#37–65
        BuildGroup7PerSlotControls(); // bi#66–81
        BuildGroup8InnerSubPanel(); // bi#82–105
        // Visibility broadcast fires here in the original (shows the roster set at open).
        ApplyOpenVisibility(); // §11.5h "single in-builder visibility broadcast"
        BuildGroup9CloseAndChrome(); // bi#106–109
        BuildGroup10CreateNameModal(); // bi#110–115
        BuildGroup11RenameModal(); // bi#116–123

        // Create-form feedback toast — a ROOT overlay label (NOT one of the 124 in-routine widgets;
        // it is a host control like the create-name LineEdit). Hidden until a face/appearance step
        // raises it via ShowCreateToast; auto-hides on the _Process timer. Bottom-centre, orange.
        _createToastWidget = HudWidgetFactory.MakeLabel(
            (int)(RefW / 2f) - 120, 470, 240, 16, string.Empty, new Color(1.0f, 0.55f, 0.20f));
        if (_createToast is not null)
        {
            _createToast.HorizontalAlignment = HorizontalAlignment.Center;
            _createToast.Visible = false;
        }

        if (_createToastWidget?.GetControl() is { } toastCtrl)
            AddChild(toastCtrl);

        // Terminal sequence: slot-count caption refresh + (3D scene already built) + BGM.
        // BGM (920100200) is started by SelectScene via FrontEndAudio.PlayBgm (single-voice
        // guard there). spec §3.8.1. The slot-count caption is the char-count label (msg 2209).
        RefreshCharCountCaption();

        GD.Print($"[CharSelectWindow] inventory built: panels={_wPanels} images={_wImages} " +
                 $"buttons={_wButtons} labels={_wLabels} textboxes={_wTextboxes} " +
                 $"(total={_wPanels + _wImages + _wButtons + _wLabels + _wTextboxes}); " +
                 $"actionBindings={_wActionBindings}. spec: frontend_scenes.md §11.5h " +
                 "(expect 10/37/46/29/2 = 124; 42 bindings).");

        // 3D ray-pick on the viewport container (reused seam).
        if (_scene3DContainer is not null)
            _scene3DContainer.GuiInput += OnViewport3DGuiInput;
    }

    // =========================================================================
    // Small construction helpers (count the inventory as we go).
    // =========================================================================

    private TextureRect? AddImage(Control parent, string atlas, int x, int y, int w, int h, int sx, int sy)
    {
        _wImages++;
        var rect = Atlas is null ? null : HudWidgetFactory.BuildImageComponent(Atlas, atlas, x, y, w, h, sx, sy);
        if (rect is not null) parent.AddChild(rect);
        return rect;
    }

    private HudPanel AddPanel(Control parent, string atlas, int x, int y, int w, int h, int sx, int sy, bool modal)
    {
        _wPanels++;
        var panel = Atlas is null
            ? HudWidgetFactory.BuildTransparentPanel(x, y, w, h, modal)
            : HudWidgetFactory.BuildPanel(Atlas, atlas, x, y, w, h, sx, sy, modal);
        if (panel.GetControl() is { } ctrl) parent.AddChild(ctrl);
        return panel;
    }

    private HudLabel AddLabel(Control parent, int x, int y, int w, int h, string text, Color color, int fontSlot = 0,
        bool multiline = false)
    {
        _wLabels++;
        var lbl = HudWidgetFactory.MakeLabel(x, y, w, h, text, color, fontSlot, multiline);
        if (lbl.GetControl() is { } ctrl) parent.AddChild(ctrl);
        return lbl;
    }

    private HudTextbox AddTextbox(Control parent, int x, int y, int w, int h, int maxLength, int fontSlot)
    {
        _wTextboxes++;
        var tb = HudWidgetFactory.MakeTextbox(x, y, w, h, false, maxLength, fontSlot);
        if (tb.GetControl() is { } ctrl) parent.AddChild(ctrl);
        return tb;
    }

    /// <summary>
    ///     Builds a 3-state Button3 and binds it to its action id (one of the 42).
    ///     Mirrors the legacy parent-add-with-action site: child added AND its click action bound.
    ///     spec: Docs/RE/specs/ui_system.md §7.4 — Panel_AddChildWithAction(parent, child, actionId).
    /// </summary>
    private HudButton AddButtonAction(Control parent, string atlas, int x, int y, int w, int h,
        int normSx, int normSy, int hovSx, int hovSy, int prsSx, int prsSy, int actionId)
    {
        _wButtons++;
        _wActionBindings++;
        var btn = Atlas is null
            ? new HudButton(x, y, w, h, null) { ActionId = actionId }
            : HudWidgetFactory.BuildButton3State(Atlas, atlas, x, y, w, h,
                normSx, normSy, hovSx, hovSy, prsSx, prsSy, actionId: actionId);
        // Single dispatcher — routes every one of the 42 bound actions to OnAction.
        btn.ActionFired += OnAction;
        if (btn.GetControl() is { } ctrl) parent.AddChild(ctrl);
        return btn;
    }

    /// <summary>
    ///     Builds a 3-state Button3 with NO action binding (plain parent-add).
    ///     The four inner-panel OK/Cancel pairs (bi#80/81, bi#93/94) and the corner CLOSE
    ///     (bi#106) are plain parent-adds in the routine — structural / their click route is
    ///     OPEN@DEBUGGER. We still build the widget (it is one of the 46 Button3 sites).
    ///     spec: Docs/RE/specs/frontend_scenes.md §11.5h "Non-clickable buttons (plain parent-add)".
    /// </summary>
    private HudButton AddButtonPlain(Control parent, string atlas, int x, int y, int w, int h,
        int sx, int sy)
    {
        _wButtons++;
        var btn = Atlas is null
            ? new HudButton(x, y, w, h, null)
            : HudWidgetFactory.MakeButton2(Atlas, atlas, x, y, w, h, sx, sy, -1);
        if (btn.GetControl() is { } ctrl) parent.AddChild(ctrl);
        return btn;
    }

    private Control NewContainer(string name, int x, int y, int w, int h, bool stop)
    {
        var c = new Control
        {
            Name = name,
            Position = new Vector2(x, y),
            Size = new Vector2(w, h),
            MouseFilter = stop ? MouseFilterEnum.Stop : MouseFilterEnum.Pass
        };
        AddChild(c);
        return c;
    }
}