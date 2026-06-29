using System.Collections.Immutable;
using Godot;
using MartialHeroes.Client.Application.Contracts.Events;
using MartialHeroes.Client.Godot.Autoload;
using MartialHeroes.Client.Godot.Composition;
using MartialHeroes.Client.Godot.Ui.Assets;
using MartialHeroes.Client.Godot.Ui.Widgets;

namespace MartialHeroes.Client.Godot.Ui.Scenes.Select;

public sealed partial class CharSelectWindow : Control
{
    [Signal]
    public delegate void BackRequestedEventHandler();

    [Signal]
    public delegate void CreateCharacterRequestedEventHandler(string name, int internalClass, int faceIndex,
        int[] stats);

    [Signal]
    public delegate void DeleteCharacterRequestedEventHandler(int slotIndex, string characterName);

    [Signal]
    public delegate void EnterGameRequestedEventHandler(string characterName, int slotIndex);

    [Signal]
    public delegate void MoveCharacterSlotRequestedEventHandler(int fromSlot, int toSlot);

    [Signal]
    public delegate void RenameCharacterRequestedEventHandler(int slotIndex, string newName);


    private const int MaxSlots = 5;
    private const string BlankSentinel = "@BLANK@";

    private const float RefW = 1024f;
    private const float RefH = 768f;

    private const string AtlasLoginWindow = "data/ui/loginwindow.dds";
    private const string AtlasInventWindow = "data/ui/inventwindow.dds";
    private const string AtlasMainWindow = "data/ui/mainwindow.dds";
    private const string AtlasCarrierPigeonPerson = "data/ui/CarrierPigeonPerson.dds";
    private const string AtlasCarrierPigeonAll = "data/ui/CarrierPigeonAll.dds";

    private const int ActionServer = 1;
    private const int ActionChannel = 2;
    private const int ActionBack = 3;

    private const int ActionCreate = 4;
    private const int ActionDelete = 5;
    private const int ActionEnter = 6;

    private const int ActionClass0 = 10;

    private const int ActionFaceIncr = 21;
    private const int ActionFaceDecr = 22;

    private const int ActionPointBuyBase = 25;

    private const int ActionConfirm = 35;
    private const int ActionCancel = 36;

    private const int StatGridRows = 5;

    private const int StatFloor = 10;
    private const int StatGridStride = 24;
    private const int StatIconW = 24;
    private const int StatIconH = 16;

    private const int StatCol1NormX = 500;
    private const int StatCol1NormY = 770;

    private const int StatCol1HovX = 548;

    private const int StatCol2NormX = 524;
    private const int StatCol2NormY = 770;
    private const int StatCol2HovX = 572;

    private const float RosterBtnY = 112f;
    private const float RosterBtnW = 59f;
    private const float RosterBtnH = 20f;

    private const int ConfirmDstX = 42;
    private const int CancelDstX = 112;
    private const int ConfirmCancelY = 325;
    private const int ConfirmCancelW = 59;
    private const int ConfirmCancelH = 20;
    private const int ConfirmNormSrcX = 354;
    private const int ConfirmHovSrcX = 413;
    private const int CancelNormSrcX = 472;
    private const int CancelHovSrcX = 531;
    private const int ConfirmCancelSrcY = 1004;

    private const float ModalW = 340f;
    private const float ModalH = 190f;

    private const float NameBoxX = 60f;
    private const float NameBoxY = 80f;
    private const float NameBoxW = 274f;
    private const float NameBoxH = 18f;

    private const uint MsgDeleteConfirmBody = 14001u;
    private const uint MsgDeleteConfirmTitle = 14002u;
    private const float DeleteModalW = 340f;

    private const float DeleteModalH = 190f;

    private const float DeleteModalX = 342f - DeleteModalW / 2f;
    private const float DeleteModalY = 289f - DeleteModalH / 2f;

    private const int FaceMin = 1;
    private const int FaceMax = 7;
    private const int ClassBtnSrcY = 1005;
    private const int ClassBtnW = 45;
    private const int ClassBtnH = 19;

    private const uint MsgCharCount = 2209u;

    private static readonly int[] ClassBtnNormSrcX = [590, 635, 680, 725];
    private static readonly int[] ClassBtnHovSrcX = [770, 815, 860, 905];

    private static readonly int[] UiToInternal = [4, 1, 3, 2];

    private static readonly uint[] ClassMsgIds = [14003u, 14004u, 14005u, 14006u];


    private readonly LiveSlot[] _slots = new LiveSlot[MaxSlots];

    private readonly int[] _statValues = new int[StatGridRows];

    private HudLabel? _charCountCaptionWidget;
    private HudLabel? _createDescLine0Widget;
    private HudLabel? _createDescLine1Widget;
    private HudLabel? _createDescLine2Widget;
    private int _createFaceIndex = FaceMin;
    private HudLabel? _createFaceLabelWidget;
    private bool _createFormVisible;
    private CharCreatePreview3D? _createPreview3D;
    private double _createToastTimer;

    private HudLabel? _createToastWidget;
    private int _createUiClass;

    private HudLabel? _infoLevelWidget;
    private HudLabel? _infoNameWidget;

    private HudLabel? _infoPositionWidget;
    private LineEdit? _nameEntry;

    private HudLabel? _nameModalTitleWidget;
    private HudLabel? _nameToastWidget;

    private NpcScrDescriptions _npcScrDesc = null!;
    private int _pendingDeleteSlot = -1;
    private int _pendingRelocateToSlot = -1;

    private RealClientAssets? _realAssets;

    private bool _rotatePressLeft;
    private bool _rotatePressRight;

    private CharSelectScene3D? _scene3D;
    private SubViewportContainer? _scene3DContainer;
    private SubViewport? _scene3DViewport;
    private int _selectedSlot;
    private double _toastTimer;


    public HudAtlasLibrary? Atlas { get; set; }

    public HudTextLibrary? Text { get; set; }

    public FrontEndAudio? Audio { get; set; }

    private Label? _charCountCaption => (Label?)_charCountCaptionWidget?.GetControl();
    private Label? _infoName => (Label?)_infoNameWidget?.GetControl();
    private Label? _infoLevel => (Label?)_infoLevelWidget?.GetControl();
    private Label? _infoPosition => (Label?)_infoPositionWidget?.GetControl();
    private Label? _createFaceLabel => (Label?)_createFaceLabelWidget?.GetControl();
    private Label? _createDescLine0 => (Label?)_createDescLine0Widget?.GetControl();
    private Label? _createDescLine1 => (Label?)_createDescLine1Widget?.GetControl();
    private Label? _createDescLine2 => (Label?)_createDescLine2Widget?.GetControl();
    private Label? _nameModalTitle => (Label?)_nameModalTitleWidget?.GetControl();
    private Label? _nameToast => (Label?)_nameToastWidget?.GetControl();
    private Label? _createToast => (Label?)_createToastWidget?.GetControl();


    public void ApplyCharacterList(ImmutableArray<CharacterListSlot> slots)
    {
        for (var i = 0; i < MaxSlots; i++)
            _slots[i] = new LiveSlot(true);

        foreach (var s in slots)
        {
            var idx = s.SlotIndex;
            if (idx < 0 || idx >= MaxSlots) continue;

            var empty = s.Name == BlankSentinel || string.IsNullOrEmpty(s.Name);
            _slots[idx] = new LiveSlot(
                empty,
                empty ? string.Empty : s.Name,
                s.Level,
                s.ServerClass,
                s.CurrentHp,
                idx,
                s.PosX,
                s.PosZ,
                s.InternalClass,
                s.AppearanceVariant,
                s.FaceA,
                s.EquipGids);
        }

        _selectedSlot = 0;
        RefreshInfo();
        RefreshCharCountCaption();
        PushSlotDescriptors();
        _scene3D?.RefreshSlotActors(_realAssets);
        _scene3D?.SetSelectedSlot(_selectedSlot);

        GD.Print($"[CharSelectWindow] ApplyCharacterList: {slots.Length} slots; " +
                 $"atlas={Atlas is not null}.");
    }


    public override void _Ready()
    {
        Position = Vector2.Zero;
        Size = new Vector2(RefW, RefH);
        CustomMinimumSize = new Vector2(RefW, RefH);
        MouseFilter = MouseFilterEnum.Ignore;


        try
        {
            _realAssets = RealClientAssets.TryOpen();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[CharSelectWindow] VFS open for 3D previews failed: {ex.Message}");
        }

        _npcScrDesc = NpcScrDescriptions.Load(ClientContext.Instance?.CatalogueStore?.Npc);

        try
        {
            BuildInventory();
            RefreshInfo();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[CharSelectWindow] BuildInventory failed: {ex.Message}");
        }
    }

    public override void _ExitTree()
    {
        _realAssets?.Dispose();
        _realAssets = null;
    }

    public override void _Process(double delta)
    {
        if (_createFormVisible && _createPreview3D is not null && IsInstanceValid(_createPreview3D))
        {
            var dt = (float)delta;
            if (_rotatePressLeft) _createPreview3D.RotateLeft(dt);
            if (_rotatePressRight) _createPreview3D.RotateRight(dt);
        }

        if (_toastTimer > 0.0)
        {
            _toastTimer -= delta;
            if (_toastTimer <= 0.0)
            {
                _toastTimer = 0.0;
                if (_nameToast is not null && IsInstanceValid(_nameToast))
                    _nameToast.Visible = false;
            }
        }

        if (_createToastTimer > 0.0)
        {
            _createToastTimer -= delta;
            if (_createToastTimer <= 0.0)
            {
                _createToastTimer = 0.0;
                if (_createToast is not null && IsInstanceValid(_createToast))
                    _createToast.Visible = false;
            }
        }
    }


    private readonly record struct LiveSlot(
        bool IsEmpty,
        string Name = "",
        ushort Level = 0,
        ushort ServerClass = 0,
        uint CurrentHp = 0,
        int SlotIndex = 0,
        float PosX = 0f,
        float PosZ = 0f,
        ushort InternalClass = 0,
        byte AppearanceVariant = 0,
        ushort FaceA = 0,
        ImmutableArray<uint> EquipGids = default);
}