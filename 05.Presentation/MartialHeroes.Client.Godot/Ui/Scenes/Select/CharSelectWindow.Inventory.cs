using Godot;
using MartialHeroes.Client.Godot.Ui.Widgets;

namespace MartialHeroes.Client.Godot.Ui.Scenes.Select;

public sealed partial class CharSelectWindow
{
    private Control? _bottomRow;
    private Control? _carrierPigeonPanel;
    private Control? _innerSubPanel;
    private Control? _modalCreateClass;
    private Control? _modalCreateName;
    private Control? _modalDeleteCfm;
    private Control? _modalNotice;
    private Control? _modalRename;
    private Control? _noticeSmall;

    private Control? _overlayAction61;
    private Control? _perSlotControls;
    private Control? _rightDetail;


    private Control? _topStrip;
    private int _wActionBindings;
    private int _wButtons;
    private int _wImages;

    private int _wLabels;

    private int _wPanels;
    private int _wTextboxes;


    private void BuildInventory()
    {
        Build3DSceneViewport();

        if (Atlas is null)
            GD.PushWarning("[CharSelectWindow] Atlas offline — inventory chrome will be invisible " +
                           "(widgets still built/functional). spec: frontend_scenes.md §11.5h.");

        BuildGroup1RosterFrame();
        BuildGroup2CreateClassModal();
        BuildGroup3NoticeModal();
        BuildGroup4DeleteConfirmModal();
        BuildGroup5BottomRow();
        BuildGroup6RightDetail();
        BuildGroup7PerSlotControls();
        BuildGroup8InnerSubPanel();
        BuildGroupCarrierPigeon();
        ApplyOpenVisibility();
        BuildGroup9CloseAndChrome();
        BuildGroup10CreateNameModal();
        BuildGroup11RenameModal();

        _createToastWidget = HudWidgetFactory.MakeLabel(
            (int)(RefW / 2f) - 120, 470, 240, 16, string.Empty, new Color(1.0f, 0.55f, 0.20f));
        if (_createToast is not null)
        {
            _createToast.HorizontalAlignment = HorizontalAlignment.Center;
            _createToast.Visible = false;
        }

        if (_createToastWidget?.GetControl() is { } toastCtrl)
            AddChild(toastCtrl);

        RefreshCharCountCaption();

        GD.Print($"[CharSelectWindow] inventory built: panels={_wPanels} images={_wImages} " +
                 $"buttons={_wButtons} labels={_wLabels} textboxes={_wTextboxes} " +
                 $"(total={_wPanels + _wImages + _wButtons + _wLabels + _wTextboxes}); " +
                 $"actionBindings={_wActionBindings}. spec: frontend_scenes.md §11.5h " +
                 "(expect 10/37/46/29/2 = 124; 42 bindings).");

        if (_scene3DContainer is not null)
            _scene3DContainer.GuiInput += OnViewport3DGuiInput;
    }


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

    private HudButton AddButtonAction(Control parent, string atlas, int x, int y, int w, int h,
        int normSx, int normSy, int hovSx, int hovSy, int prsSx, int prsSy, int actionId)
    {
        _wButtons++;
        _wActionBindings++;
        var btn = Atlas is null
            ? new HudButton(x, y, w, h, null) { ActionId = actionId }
            : HudWidgetFactory.BuildButton3State(Atlas, atlas, x, y, w, h,
                normSx, normSy, hovSx, hovSy, prsSx, prsSy, actionId: actionId);
        btn.ActionFired += OnAction;
        if (btn.GetControl() is { } ctrl) parent.AddChild(ctrl);
        return btn;
    }

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