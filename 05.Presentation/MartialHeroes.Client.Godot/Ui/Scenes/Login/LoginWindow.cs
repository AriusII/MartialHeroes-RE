
using Godot;
using MartialHeroes.Client.Godot.Ui.Assets;
using MartialHeroes.Client.Godot.Ui.Widgets;



namespace MartialHeroes.Client.Godot.Ui.Scenes.Login;

public sealed partial class LoginWindow : Control
{
    [Signal]
    public delegate void ConnectCancelledEventHandler();

    [Signal]
    public delegate void ConnectRequestedEventHandler(int serverId, string pin);


    [Signal]
    public delegate void LoginAcceptedEventHandler(string account, string password);

    [Signal]
    public delegate void LoginFlowCompletedEventHandler(int serverId, string pin);

    [Signal]
    public delegate void QuitRequestedEventHandler();

    private const float CurtainSpeed = 5f;
    private const float CurtainCompleteThresh = 222f;
    private const int CurtainBotBaseY = 326;

    private const ulong ServerFetchThrottleMs = 10000;


    private readonly HudAtlasLibrary _atlas;
    private readonly HudTextLibrary _text;


    private Control? _backgroundLayer;

    private Control? _bannerFrame;
    private string _collectedPin = "";
    private int _collectedServerId;

    private Control? _credentialGroup;
    private Control? _credPanel;

    private float _curtainAcc;
    private TextureRect? _curtainBot;
    private bool _curtainDone;

    private TextureRect? _curtainTop;
    private double _errorBudgetMs;
    private ulong _errorLastDecrementMs;
    private Label? _errorMsgLabel;
    private int _errorN;
    private Label? _errorOkBtnLabel;
    private TextureButton? _errorOkBtnNode;

    private Control? _errorPanel;

    private Control? _exitConfirm;

    private int _flowSubState;

    private TextureRect? _formDecoPlate;

    private Control? _formGroup;

    private Control? _formPanel;

    private MaskedTextField? _idBox;
    private ulong _lastServerFetchMs;

    private Control? _noticePanel;

    private Control? _pinKeypadRoot;

    private PinSubView? _pinView;

    private Control? _pinYesNoPanel;
    private MaskedTextField? _pwBox;

    private Control? _quitModal;
    private Control? _quitModal2;

    private CanvasItemMaterial? _rootMaterial;

    private string _savedId = "";

    private HudCheckbox? _saveIdCheck;
    private bool _saveIdChecked;

    private Control? _serverListRoot;

    private Control? _serverListStrip;
    private Control? _serverListStripDeco;
    private ServerSelectSubView? _serverSelect;


    public LoginWindow(HudAtlasLibrary atlas, HudTextLibrary text)
    {
        _atlas = atlas;
        _text = text;
        MouseFilter = MouseFilterEnum.Pass;
    }


    public Func<PinSubView>? PinFactory { get; set; }
    public Func<ServerSelectSubView>? ServerSelectFactory { get; set; }

    public FrontEndAudio? Audio { get; set; }


    public override void _Ready()
    {

        LoadSaveId();

        BuildBackgroundLayer();
        BuildBannerFrame();
        BuildCurtainPanels();
        BuildNoticePanel();
        BuildServerListRoot();
        BuildFormGroup();
        BuildCredentialGroup();
        BuildPinKeypadRoot();
        BuildPinYesNoPanel();
        BuildQuitModals();
        BuildErrorPanel();

        RunState(1);

        GD.Print($"[LoginWindow] Login(1) built. flowSubState={_flowSubState}. spec: frontend_layout_tables.md §2.");
    }

    public override void _Process(double delta)
    {
        if (_flowSubState == 2) TickCurtain();
        if (_errorPanel?.Visible == true) TickErrorCountdown();
    }

    public override void _Notification(int what)
    {
        if (what == (int)NotificationWMCloseRequest)
        {
            GD.Print("[LoginWindow] OS window-close → QuitRequested.");
            EmitSignal(SignalName.QuitRequested);
        }
    }

    public override void _ExitTree()
    {
        if (_rootMaterial is not null)
        {
            Material = null;
            _rootMaterial = null;
        }
    }
}