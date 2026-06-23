using Godot;
using MartialHeroes.Client.Godot.Ui.Widgets;

namespace MartialHeroes.Client.Godot.Ui.Scenes.Select;

public sealed partial class CharSelectWindow
{
    private const string AtlasTradeKeep = "data/ui/tradekeep.dds";
    private HudLabel? _deleteBodyWidget;
    private HudLabel? _deleteTitleWidget;

    private HudLabel? _noticeBodyWidget;

    private int _pendingRenameSlot = -1;

    private LineEdit? _renameEntry;
    private HudLabel? _renameModalTitleWidget;
    private HudLabel? _renameToastWidget;

    private Label? _noticeBody => (Label?)_noticeBodyWidget?.GetControl();
    private Label? _renameModalTitle => (Label?)_renameModalTitleWidget?.GetControl();
    private Label? _renameToast => (Label?)_renameToastWidget?.GetControl();


    private void Build3DSceneViewport()
    {
        _scene3DViewport = new SubViewport
        {
            Name = "CharSelect3DViewport",
            Size = new Vector2I(1024, 768),
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


    private Control NewModalContainer(string name)
    {
        var x = (RefW - ModalW) / 2f;
        var y = (RefH - ModalH) / 2f;
        var c = new Control
        {
            Name = name,
            Position = new Vector2(x, y),
            Size = new Vector2(ModalW, ModalH),
            MouseFilter = MouseFilterEnum.Stop,
            Visible = false
        };
        AddChild(c);
        return c;
    }


    private void ShowCreateForm()
    {
        _createFormVisible = true;

        for (var i = 0; i < StatGridRows; i++)
            _statValues[i] = StatFloor;

        if (_perSlotControls is not null) _perSlotControls.Visible = true;
        if (_innerSubPanel is not null) _innerSubPanel.Visible = true;

        if (_scene3DContainer is not null && IsInstanceValid(_scene3DContainer))
            _scene3DContainer.Visible = false;

        EnsureCreatePreview3D();
        if (_createPreview3D is not null && IsInstanceValid(_createPreview3D))
        {
            _createPreview3D.Visible = true;
            _createPreview3D.InternalClassId = UiToInternal[_createUiClass];
            _createPreview3D.UpdateFaceIndex(_createFaceIndex);
            _createPreview3D.RebuildForClass();
        }

        _rotatePressLeft = false;
        _rotatePressRight = false;
        SyncClassCaptions();
        GD.Print($"[CharSelectWindow] Create form opened (uiClass={_createUiClass} → " +
                 $"internal={UiToInternal[_createUiClass]}); stats reset to seed {StatFloor}. " +
                 "spec §4 / §11.5h Groups 7–8; character_creation.md §2.1.");
    }

    private void HideCreateForm()
    {
        _createFormVisible = false;
        if (_perSlotControls is not null) _perSlotControls.Visible = false;
        if (_innerSubPanel is not null) _innerSubPanel.Visible = false;
        if (_createPreview3D is not null && IsInstanceValid(_createPreview3D))
            _createPreview3D.Visible = false;
        if (_scene3DContainer is not null && IsInstanceValid(_scene3DContainer))
            _scene3DContainer.Visible = true;
        HideModal(_modalCreateClass);
        HideModal(_modalCreateName);
        _rotatePressLeft = false;
        _rotatePressRight = false;
    }

    private void EnsureCreatePreview3D()
    {
        if (_createPreview3D is not null && IsInstanceValid(_createPreview3D)) return;
        _createPreview3D = new CharCreatePreview3D
        {
            Name = "CreatePreview3D",
            MouseFilter = MouseFilterEnum.Ignore,
            SharedRealAssets = _realAssets,
            InternalClassId = UiToInternal[_createUiClass],
            Visible = false
        };
        _createPreview3D.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(_createPreview3D);
        MoveChild(_createPreview3D, 1);
    }


    private void ShowCreateNameModal()
    {
        HideModal(_modalCreateClass);
        if (_nameModalTitle is not null && IsInstanceValid(_nameModalTitle))
            _nameModalTitle.Text = ClassCaption(_createUiClass);
        if (_nameEntry is not null && IsInstanceValid(_nameEntry))
            _nameEntry.Text = string.Empty;
        if (_nameToast is not null && IsInstanceValid(_nameToast))
            _nameToast.Visible = false;
        _toastTimer = 0.0;
        if (_modalCreateName is not null) _modalCreateName.Visible = true;
        GD.Print($"[CharSelectWindow] create-NAME modal opened for class {_createUiClass}. spec §11.5h G10.");
    }

    private void OnCreateNameConfirm()
    {
        var name = _nameEntry?.Text.Trim() ?? string.Empty;
        if (!ValidateName(name, out var toastMsg))
        {
            ShowToast(toastMsg);
            GD.Print($"[CharSelectWindow] create name rejected: '{name}' → toast.");
            return;
        }

        var internalClass = UiToInternal[_createUiClass];

        var stats = new int[StatGridRows];
        for (var i = 0; i < StatGridRows; i++)
            stats[i] = _statValues[i];

        GD.Print($"[CharSelectWindow] CreateCharacterRequested: name='{name}' " +
                 $"class={internalClass} face={_createFaceIndex} " +
                 $"stats=({stats[0]},{stats[1]},{stats[2]},{stats[3]},{stats[4]}). " +
                 "spec §4/§8; charselect.md §4.3; character_creation.md §2.1.");
        EmitSignal(SignalName.CreateCharacterRequested, name, internalClass, _createFaceIndex, stats);
        HideModal(_modalCreateName);
        HideCreateForm();
        RefreshCharCountCaption();
    }


    private void OnSlotSelectConfirm()
    {
        var ls = _slots[_selectedSlot];
        if (ls.IsEmpty)
        {
            ShowCreateForm();
            return;
        }

        EmitSignal(SignalName.EnterGameRequested, ls.Name, _selectedSlot);
    }


    private void SetCreateClass(int uiIndex)
    {
        _createUiClass = Math.Clamp(uiIndex, 0, 3);
        SyncClassCaptions();

        if (_createPreview3D is not null && IsInstanceValid(_createPreview3D))
        {
            _createPreview3D.InternalClassId = UiToInternal[_createUiClass];
            _createPreview3D.RebuildForClass();
        }

        Audio?.PlayClassPreviewBgm(_createUiClass);

        GD.Print($"[CharSelectWindow] class change → uiClass={_createUiClass} " +
                 $"(internal={UiToInternal[_createUiClass]}); 3D preview rebuilt + per-class BGM. " +
                 "spec §4.1 / sound.md §15.6b.");
    }

    private void SyncClassCaptions()
    {
        if (_nameModalTitle is not null && IsInstanceValid(_nameModalTitle))
            _nameModalTitle.Text = ClassCaption(_createUiClass);

        var lines = GetDescLines(_createUiClass);
        if (_createDescLine0 is not null && IsInstanceValid(_createDescLine0))
            _createDescLine0.Text = lines.Length > 0 ? lines[0] : string.Empty;
        if (_createDescLine1 is not null && IsInstanceValid(_createDescLine1))
            _createDescLine1.Text = lines.Length > 1 ? lines[1] : string.Empty;
        if (_createDescLine2 is not null && IsInstanceValid(_createDescLine2))
            _createDescLine2.Text = lines.Length > 2 ? lines[2] : string.Empty;
    }

    private string ClassCaption(int uiIndex)
    {
        var safe = Math.Clamp(uiIndex, 0, 3);
        return Text?.GetCaption(ClassMsgIds[safe], string.Empty) ?? string.Empty;
    }

    private string[] GetDescLines(int uiIndex)
    {
        return _npcScrDesc.GetDescriptionLines(Math.Clamp(uiIndex, 0, 3));
    }


    private void PushSlotDescriptors()
    {
        if (_scene3D is null) return;

        var descs = new CharSelectScene3D.SlotDescriptor[MaxSlots];
        for (var i = 0; i < MaxSlots; i++)
        {
            var ls = _slots[i];
            if (ls.IsEmpty)
            {
                descs[i] = default;
                continue;
            }

            var internalClass = ls.InternalClass != 0 ? ls.InternalClass : ls.ServerClass;
            descs[i] = new CharSelectScene3D.SlotDescriptor(
                true,
                internalClass,
                ls.AppearanceVariant,
                ls.FaceA,
                ls.EquipGids.IsDefaultOrEmpty ? null : ls.EquipGids.ToArray());
        }

        _scene3D.SlotDescriptors = descs;
    }


    private void RefreshInfo()
    {
        var ls = _slots[_selectedSlot];

        if (_infoName is not null && IsInstanceValid(_infoName))
            _infoName.Text = ls.IsEmpty ? string.Empty : ls.Name;
        if (_infoLevel is not null && IsInstanceValid(_infoLevel))
            _infoLevel.Text = ls.IsEmpty ? string.Empty : ls.Level.ToString();
        if (_infoPosition is not null && IsInstanceValid(_infoPosition))
            _infoPosition.Text = ls.IsEmpty ? string.Empty : $"{(int)ls.PosX} , {(int)ls.PosZ}";

        RefreshAction61Overlay(ls);
    }

    private void RefreshAction61Overlay(LiveSlot ls)
    {
        if (_overlayAction61 is null || !IsInstanceValid(_overlayAction61)) return;
        _overlayAction61.Visible = !ls.IsEmpty;
    }

    private string BuildCharCountCaption()
    {
        var count = 0;
        for (var i = 0; i < MaxSlots; i++)
            if (!_slots[i].IsEmpty)
                count++;

        var template = Text?.GetCaption(MsgCharCount, string.Empty) ?? string.Empty;
        if (string.IsNullOrEmpty(template)) return string.Empty;
        return template.Contains("%d")
            ? template.Replace("%d", count.ToString())
            : string.Format(template, count);
    }

    private void RefreshCharCountCaption()
    {
        if (_charCountCaption is null || !IsInstanceValid(_charCountCaption)) return;
        _charCountCaption.Text = BuildCharCountCaption();
    }
}