// Ui/Scenes/Login/LoginWindow.cs
//
// Login window for the Login(1) state — rebuilt on the Ui/Scenes substrate.
//
// Implements the flowSubState machine from:
//   spec: Docs/RE/specs/frontend_layout_tables.md §2.2 / §2.3
//
// Sub-state machine (§2.2):
//   1  intro one-shot: SFX 861010105; reset curtain offset; hide all groups.         → 2
//   2  curtain opening: offset+=5/tick; top Y=−offset; bot Y=offset+326;
//      offset>222                                                                    → 3
//   3  curtain done; host strip already present; credential group still hidden.       → 4
//   4  form idle; Enter → 5                                                       (event)
//   5  commit form.                                                                   → 6
//   6  validate-armed idle: shows credential group; OK/Enter → 29
//  29  validate: ID≥4 / PW≠0 check; hides credential group; shows PIN → 31
//  31  PIN entry: keypad modal shown.                                                (UI)
//  32  PIN poll: wait for PinSubmitted → 33
//  33  start server-list fetch; hide credential group                                → 34
//  34  (re)start fetch                                                               → 35
//  35  fetching                                                                  (→ 36)
//  36  fetch result → 37
//  37  server list shown: pick plate (400/401) or page (115..124).
//      Commit guard: status==0 && load<2400 → 38
//  38  channel-endpoint fetch → 39
//  39..40 connecting
//  41  hand-off: emit LoginFlowCompleted.
//
// Per-sub-state visibility (CORRECTED, §2.2 bands, CYCLE 18 C5b):
//   BG:               hidden 1;  shown 2+ (1→2 edge). spec §2.2 corrected.
//   Curtains (host panels): always-present Y-animated panels; NOT toggled by sub-state.
//                     Binary models them as Y-animated always-present panels that slide off-canvas.
//                     Port hides them after state 2 (equivalent, no partial-occlusion). spec §2.2 bands.
//   FormGroup (host strip): shown from state 2+. ALWAYS PRESENT after curtain starts.
//                     Distinct from credential group. spec §2.2 "Login-form host strip | always present".
//   CredentialGroup:  hidden 1..5; SHOWN on the 5→6 edge; hidden leaving 33.
//                     Band ≈ 5..33 (NOT 3..32). spec §2.2 bands CORRECTED.
//   NoticePanel:      always hidden (never shown per spec §2.1 init=hidden)
//   ServerList:       hidden 1..34; shown 35..37; hidden 38+. spec §2.2 corrected.
//   PinKeypad:        hidden 1..30; shown 31/32; hidden 33+
//   PinYesNo:         always hidden (legacy PIN dialog, unused in active flow)
//
// spec: Docs/RE/specs/frontend_layout_tables.md §2.

using Godot;
using MartialHeroes.Client.Godot.Ui.Assets;
using MartialHeroes.Client.Godot.Ui.Widgets;

// FrontEndAudio

// LoginLayout, WidgetRect (moved to engine-free layer)

namespace MartialHeroes.Client.Godot.Ui.Scenes.Login;

/// <summary>
///     Login window — stateful login scene with spec-faithful flowSubState machine.
///     <para>
///         Strictly passive: reads atlases/text from HUD libraries, builds widgets,
///         turns UI gestures into use-case signals. Never mutates domain state.
///     </para>
///     spec: Docs/RE/specs/frontend_layout_tables.md §2 — login scene specification.
/// </summary>
public sealed partial class LoginWindow : Control
{
    /// <summary>
    ///     Fired when the user presses Cancel (action 113) on the connecting popup.
    ///     LoginScene cancels the in-flight connect attempt.
    ///     spec: Docs/RE/specs/frontend_layout_tables.md §4 "clicking Cancel aborts the join → state 34"
    /// </summary>
    [Signal]
    public delegate void ConnectCancelledEventHandler();

    /// <summary>
    ///     Fired when the user picks a server and the connecting popup is up.
    ///     LoginScene begins the real connect in response.
    ///     spec: Docs/RE/specs/frontend_layout_tables.md §2.2 state 39 / §4
    /// </summary>
    [Signal]
    public delegate void ConnectRequestedEventHandler(int serverId, string pin);

    // -------------------------------------------------------------------------
    // Signals (same contract as old LoginScreen)
    // -------------------------------------------------------------------------

    [Signal]
    public delegate void LoginAcceptedEventHandler(string account, string password);

    [Signal]
    public delegate void LoginFlowCompletedEventHandler(int serverId, string pin);

    [Signal]
    public delegate void QuitRequestedEventHandler();
    // -------------------------------------------------------------------------
    // Spec-cited constants
    // spec: Docs/RE/specs/frontend_layout_tables.md §2.2 / §2.3
    // -------------------------------------------------------------------------

    private const float CurtainSpeed = 5f; // spec: frontend_layout_tables.md §2.2
    private const float CurtainCompleteThresh = 222f; // spec: frontend_layout_tables.md §2.2
    private const int CurtainBotBaseY = 326; // spec: frontend_layout_tables.md §2.3 "bottom Y = offset + 326"

    // Server-list re-fetch throttle (action 105): the IDA handler skips the re-fetch while a fetch is
    // already in flight (sub-state 35) or within 10 s of the last one. spec §2.2.
    private const ulong ServerFetchThrottleMs = 10000;

    // -------------------------------------------------------------------------
    // Runtime state
    // -------------------------------------------------------------------------

    private readonly HudAtlasLibrary _atlas;
    private readonly HudTextLibrary _text;

    // --- Layer containers (visibility gated per sub-state) ---

    // loginwindow.dds backdrop (0,110,1024,490). spec §2.1 "Background | init hidden".
    private Control? _backgroundLayer;

    // Static central banner frame: loginwindow.dds src(0,490) at abs (270,85) 483×490.
    // STATIC — never animated, never hidden after the 1→2 edge. Contains the logo child
    // at panel-local (207,44) = canvas (477,129). Debugger-confirmed IDB 263bd994.
    // spec: Docs/RE/specs/frontend_layout_tables.md §2.1 / §2.3
    private Control? _bannerFrame;
    private string _collectedPin = "";
    private int _collectedServerId;

    // Interactive credential group: ID textbox, PW textbox, Save-ID checkbox + label plate,
    // OK button (103), ID/PW label plates, edit-field frame art.
    // Built hidden; shown on the 5→6 edge; hidden when leaving 33. Band ≈ 5..33.
    // SOLE visibility authority — never toggled elsewhere. spec §2.2 bands CORRECTED.
    private Control? _credentialGroup;
    private Control? _credPanel;

    // Curtain state.
    private float _curtainAcc;
    private TextureRect? _curtainBot; // Y = offset + 326
    private bool _curtainDone;

    // Curtain panels.
    private TextureRect? _curtainTop; // Y = −offset
    private double _errorBudgetMs; // remaining countdown budget in ms (starts 3000)
    private ulong _errorLastDecrementMs; // last wall-clock ms at which N was decremented
    private Label? _errorMsgLabel;
    private int _errorN; // displayed countdown seconds
    private Label? _errorOkBtnLabel;
    private TextureButton? _errorOkBtnNode;

    // Validation-error message box: distinct panel (not Confirm-A/B), shown on validation failure.
    // Carries a 3 s countdown OK button; auto-closes at N=0; early-dismiss via OK (action 671).
    // spec: Docs/RE/specs/frontend_layout_tables.md §2.1a
    private Control? _errorPanel;

    // Quit-confirm ExitPanel (msg 2007), opened by action 102/112; "yes" (101) quits. spec §2.2.
    private Control? _exitConfirm;

    // flowSubState: single field, init=1. spec §2.2.
    private int _flowSubState;

    // Form decorative plate (member +0x27C, A1 src(0,469,494,113)):
    // Hidden until curtain offset>200, then snaps to absolute canvas (265,548).
    // G2 DEBUGGER-CONFIRMED 2026 / IDB 263bd994: real dst=(265,548); prior "(494,469)" confused
    // 494=width and 469=src-top for destination coordinates — CORRECTED.
    // It is a child of formPanel so its position is PANEL-LOCAL; when the snap fires we set
    // the panel-local position so that its canvas position = (265, 548) regardless of formPanel Y.
    // spec: Docs/RE/specs/frontend_layout_tables.md §2.3 "form decorative plate | hidden until offset>200; snaps once"
    private TextureRect? _formDecoPlate;

    // Login-form host strip: always-present bottom bar (server-submit[102], help/quit[105], confirm
    // face-plate, help deco plate). Visible from state 2. spec §2.2 "Login-form host strip | always present".
    private Control? _formGroup;

    // Form backing children ride with the bottom curtain panel. spec §2.3.
    private Control? _formPanel;

    // Credential textboxes — 1:1 atlas-blit MaskedTextField (no Godot LineEdit chrome).
    // spec: Docs/RE/specs/frontend_layout_tables.md §2.7 / §0.11
    private MaskedTextField? _idBox;
    private ulong _lastServerFetchMs;

    // Central notice panel. spec §2.1 "Notice panel | init hidden".
    private Control? _noticePanel;

    // PIN keypad host — structural root gated SOLELY by ApplyVisibility (states 31/32), mirroring
    // _serverListRoot. The PinSubView child lives inside it and is never toggled directly, so the
    // sub-state band is the single visibility authority (no double-source .Visible writes).
    // spec: Docs/RE/specs/frontend_layout_tables.md §2.1/§2.2/§3.
    private Control? _pinKeypadRoot;

    // Sub-view instances.
    private PinSubView? _pinView;

    // PIN yes/no prompt. spec §2.1 "PIN yes/no panel | init hidden".
    private Control? _pinYesNoPanel;
    private MaskedTextField? _pwBox;

    // Re-fetch confirm popups (msg 4023/4024), built init-hidden exactly as the IDA login build
    // creates them. Their OK buttons fire action 113/114 (→ hide + restart server-list fetch). The
    // SHOW trigger lives in the server-list fetch-result path (network layer, not yet ported), so
    // offline they remain hidden — faithful to the real client offline. spec §2.2.
    // Confirm-A (msg 4023) also serves as the "connecting" popup raised at sub-state 40. spec §2.1.
    private Control? _quitModal;
    private Control? _quitModal2;

    // Reserved for a future per-child CanvasItemMaterial (additive overlay sprites/effects only).
    // The root Control uses NORMAL blend; additive is applied only to proven overlay sprite children.
    // spec: Docs/RE/specs/rendering.md §4.2 — global ONE/ONE is D3D9 ground-truth; in Godot the root
    // Control must use normal blend so opaque atlas panels are readable over the bright 3D backdrop.
    private CanvasItemMaterial? _rootMaterial;

    // Save-ID: persisted account id loaded before the textbox is built.
    private string _savedId = "";

    // Save-ID checkbox.
    private HudCheckbox? _saveIdCheck;
    private bool _saveIdChecked;

    // Server-list overlay root (0,0,1024,398). spec §2.1 "Server-list root | init hidden".
    private Control? _serverListRoot;

    // Server-list strip and deco are visible only while the server list is open (state 35 onward).
    // spec: Docs/RE/specs/frontend_layout_tables.md §2.2 "Quit/help strip + help plate | state 35 onward"
    private Control? _serverListStrip;
    private Control? _serverListStripDeco;
    private ServerSelectSubView? _serverSelect;

    // -------------------------------------------------------------------------
    // Construction
    // -------------------------------------------------------------------------

    public LoginWindow(HudAtlasLibrary atlas, HudTextLibrary text)
    {
        _atlas = atlas;
        _text = text;
        MouseFilter = MouseFilterEnum.Pass;
    }

    // -------------------------------------------------------------------------
    // Injectable factories (set by LoginScene before AddChild)
    // -------------------------------------------------------------------------

    public Func<PinSubView>? PinFactory { get; set; }
    public Func<ServerSelectSubView>? ServerSelectFactory { get; set; }

    // Optional audio.
    public FrontEndAudio? Audio { get; set; }

    // -------------------------------------------------------------------------
    // Godot lifecycle
    // -------------------------------------------------------------------------

    public override void _Ready()
    {
        // spec: Docs/RE/specs/rendering.md §4.2 — the D3D9 front-end pipeline sets ONE/ONE additive
        // globally before walking the widget tree. In Godot, applying BlendMode.Add to the root
        // Control cascades to ALL children (including opaque atlas panels), causing additive washout
        // against the bright 3D backdrop (cyan/off-white tint artefact on the bottom bar).
        // The D3D original's ONE/ONE works because UI textures are authored on black backgrounds
        // (adding near-black pixels changes nothing), a constraint that does NOT hold for Godot Label
        // nodes or white-bordered atlas slices rendered over a SubViewport 3D scene.
        // FIX: the root uses NORMAL blend (Godot default = SRCALPHA/INVSRCALPHA). Additive is applied
        // ONLY to individual child nodes that are proven transparent overlay sprites/effects.
        // (_rootMaterial stays null; _ExitTree null-check is a safe no-op.)

        LoadSaveId();

        // Build all layers back-to-front (Z-order = add-order).
        // MEMBER ORDER: Background (+0x270) drawn first (behind), then Top curtain (+0x274),
        // then Bottom curtain (+0x278). Curtains frame/overlay the background art. spec §2.3.
        BuildBackgroundLayer(); // loginwindow.dds backdrop — member +0x270 (drawn behind curtains). spec §2.3.
        BuildBannerFrame(); // static central banner frame + logo (loginwindow.dds) — always at fixed Y=85. spec §2.1/§2.3.
        BuildCurtainPanels(); // curtain halves — member +0x274 (Top) / +0x278 (Bottom), over background.
        BuildNoticePanel(); // notice column (always hidden per spec)
        BuildServerListRoot(); // server-list container (hidden until 35..37)
        BuildFormGroup(); // always-present host strip (visible from state 2)
        BuildCredentialGroup(); // interactive credential widgets (shown on 5→6 edge, band ≈ 5..33)
        BuildPinKeypadRoot(); // PIN keypad host (hidden until 31/32) — above the form, below modals
        BuildPinYesNoPanel(); // PIN yes/no prompt (hidden per spec)
        BuildQuitModals(); // confirm modals (hidden per spec)
        BuildErrorPanel(); // validation-error countdown modal (hidden per spec §2.1a)

        // Enter state 1 — intro one-shot. spec §2.2.
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

    /// <summary>
    ///     Offline/dev harness entry point: fast-tracks the login window to sub-state 35
    ///     (server-list showing) WITHOUT credential validation or a real server fetch.
    ///     Called via CallDeferred from the MH_OFFLINE_SERVERLIST harness in ClientContext.EnvLogin
    ///     so the server-list sub-view is created and visible before the synthetic
    ///     <c>ServerListReceivedEvent</c> is published. MUST be called on the main thread.
    ///     <para>
    ///         Passive: does NOT mutate domain state. Just advances the FSM sub-state so the
    ///         presentation layer renders the correct panel. The synthetic server records arrive
    ///         via <see cref="ServerListReceivedEvent" /> on the event bus (LoginScene._Process
    ///         calls <c>ApplyServerList → _serverSelect.SetServers</c>).
    ///     </para>
    /// </summary>
    public void ActivateOfflineServerList()
    {
        GD.Print("[LoginWindow] ActivateOfflineServerList — skipping credential validation; fast-tracking to state 33. " +
                 "MH_OFFLINE_SERVERLIST dev harness only.");
        DoEnsureServerSelect(); // creates ServerSelectSubView if not already created
        RunState(33); // 33→34→35: CreateServerSelect + show server list root
    }

    public override void _ExitTree()
    {
        // Release the root CanvasItemMaterial by clearing the Material property.
        // CanvasItemMaterial is a RefCounted Resource — never call .Free() on it; instead
        // null-out the property so the engine's ref-count drops to zero and it self-frees.
        // This avoids the 'N RID allocations of type TextureE were leaked' ERROR at exit.
        if (_rootMaterial is not null)
        {
            Material = null;
            _rootMaterial = null;
        }
    }
}