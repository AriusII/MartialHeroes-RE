using Godot;
using MartialHeroes.Client.Application.Events;
using MartialHeroes.Client.Application.Scene;
using MartialHeroes.Client.Godot.Autoload;
using MartialHeroes.Client.Godot.Screens; // ServerEntry, UiAssetLoader, FrontEndAudio, ScreenHost
using MartialHeroes.Client.Godot.Ui.Scenes.Login; // LoginWindow, PinSubView, ServerSelectSubView
using MartialHeroes.Shared.Kernel.Enums;

namespace MartialHeroes.Client.Godot.Scene.Controllers;

/// <summary>
/// State 1 — LoginWindow. Builds the real 1024×768 login form and owns the in-login PIN/server-list
/// sub-views (sub-states 31/32 and 34..41) before the scene advances to Load.
/// spec: Docs/RE/specs/client_runtime.md §7.3 / §7.6; Docs/RE/specs/frontend_scenes.md §1 / §11.
/// </summary>
public sealed partial class LoginScene : StubSceneController
{
    private ClientContext? _ctx;
    private SceneHost? _host;
    private ScreenHost? _screenHost;
    private FrontEndAudio? _audio;
    private LoginWindow? _login;
    private ServerSelectSubView? _serverSelect;
    private string _account = "";
    private string _password = "";
    private bool _syncingScene;

    // CancellationTokenSource for the in-flight SelectServer/OpenGameConnection attempt.
    // Cancelled when the user presses Cancel (action 113) on the connecting popup.
    // spec: Docs/RE/specs/frontend_layout_tables.md §4 "clicking Cancel aborts the join"
    private CancellationTokenSource? _connectCts;

    /// <inheritdoc/>
    public override EngineSceneState State => EngineSceneState.Login;

    /// <inheritdoc/>
    public override void OnEnter(SceneHost host)
    {
        Name = $"Scene{(int)State}_{State}";
        _host = host;
        _ctx = GetNodeOrNull<ClientContext>("/root/ClientContext");

        _screenHost = new ScreenHost { Name = "LoginScreenHost" };
        AddChild(_screenHost);

        _audio = new FrontEndAudio { Name = "FrontEndAudio" };
        AddChild(_audio);

        _login = BuildLoginWindow();
        _screenHost.SetScreen(_login);

        GD.Print("[LoginScene] State 1 Login built real LoginWindow UI with internal PIN/server-list sub-views. " +
                 "spec: client_runtime.md §7.6; frontend_scenes.md §11.");
    }

    public override void _ExitTree()
    {
        _connectCts?.Cancel();
        _connectCts?.Dispose();
        _connectCts = null;

        if (_login is not null)
        {
            _login.LoginAccepted -= OnLoginAccepted;
            _login.QuitRequested -= OnQuitRequested;
            _login.LoginFlowCompleted -= OnLoginFlowCompleted;
            _login.ConnectRequested -= OnConnectRequested;
            _login.ConnectCancelled -= OnConnectCancelled;
        }
    }

    public override void _Process(double delta)
    {
        if (_ctx is null || _syncingScene) return;

        while (_ctx.EventBus.Reader.TryRead(out IClientEvent? evt))
        {
            switch (evt)
            {
                case ServerListReceivedEvent serverList:
                    ApplyServerList(serverList);
                    break;
                case SceneStateChangedEvent { Next.State: EngineSceneState.Load } stateChange:
                    GD.Print(
                        $"[LoginScene] SceneStateChangedEvent {stateChange.Previous.State}→{stateChange.Next.State}; " +
                        "Load transition observed. spec: client_runtime.md §7.5.2.");
                    _syncingScene = true;
                    if (_host?.CurrentState == EngineSceneState.Login)
                        _host.CallDeferred(SceneHost.MethodName.Advance);
                    return;
            }
        }
    }

    private LoginWindow BuildLoginWindow()
    {
        if (_ctx is null) throw new InvalidOperationException("ClientContext not found — VFS required.");
        var atlas = _ctx.HudAtlas;
        var text = _ctx.HudText;

        var login = new LoginWindow(atlas, text)
        {
            Name = "LoginWindow",
            Audio = _audio,
            PinFactory = CreateInLoginPin,
            ServerSelectFactory = CreateInLoginServerSelect,
        };

        login.LoginAccepted += OnLoginAccepted;
        login.QuitRequested += OnQuitRequested;
        login.LoginFlowCompleted += OnLoginFlowCompleted;
        login.ConnectRequested += OnConnectRequested;
        login.ConnectCancelled += OnConnectCancelled;
        return login;
    }

    private void OnLoginAccepted(string account, string password)
    {
        _account = account;
        _password = password;

        // spec: Docs/RE/specs/login_flow.md §4.2; Docs/RE/specs/crypto.md §6.1.
        if (_ctx?.UseCases is { } useCases)
            _ = useCases.LoginAsync(account, password, cancellationToken: CancellationToken.None);

        GD.Print($"[LoginScene] Login accepted (account='{account}'); credentials staged; " +
                 "PIN/server sub-states remain inside state 1. spec: login_flow.md §4.2.");
    }

    private PinSubView CreateInLoginPin()
    {
        var pin = new PinSubView(_ctx?.HudAtlas ?? throw new InvalidOperationException("ClientContext lost."))
        {
            Name = "PinSubView",
        };

        GD.Print("[LoginScene] Created in-login PinSubView (sub-states 31/32) on HudAtlasLibrary. " +
                 "spec: frontend_scenes.md §11.3.");
        return pin;
    }

    private ServerSelectSubView CreateInLoginServerSelect()
    {
        if (_ctx is null) throw new InvalidOperationException("ClientContext lost.");
        _serverSelect = new ServerSelectSubView(_ctx.HudAtlas, _ctx.HudText)
        {
            Name = "ServerSelectSubView",
        };

        _ = FetchServerListAsync();

        GD.Print(
            "[LoginScene] Created in-login ServerSelectSubView (sub-states 34..41) on HudAtlasLibrary. " +
            "spec: frontend_scenes.md §11.4.");
        return _serverSelect;
    }

    private async Task FetchServerListAsync()
    {
        if (_ctx?.UseCases is not { } useCases) return;

        try
        {
            await useCases.FetchServerListAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[LoginScene] FetchServerListAsync skipped/failed: {ex.Message}");
        }
    }

    private void ApplyServerList(ServerListReceivedEvent serverList)
    {
        if (_serverSelect is null || !IsInstanceValid(_serverSelect)) return;

        var entries = new List<ServerEntry>(serverList.Servers.Length);
        foreach (ServerListEntryView e in serverList.Servers)
        {
            entries.Add(new ServerEntry(
                ServerId: e.ServerId,
                DisplayName: string.Empty,
                StatusCode: e.StatusCode,
                Load: e.Load,
                OpenTime: e.OpenTime));
        }

        _serverSelect.SetServers(entries);
        GD.Print($"[LoginScene] Applied ServerListReceivedEvent ({entries.Count} entries) to state-1 server-list. " +
                 "spec: login_flow.md §2.1.");
    }

    // LoginFlowCompleted is now emitted by LoginWindow.NotifyConnectSuccess → state 41.
    // It means the scene should tear down and advance to Load.
    // spec: Docs/RE/specs/frontend_layout_tables.md §2.2 state 41 / §4 "SUCCESS = char-list → state 4"
    private void OnLoginFlowCompleted(int serverId, string pin)
    {
        GD.Print($"[LoginScene] LoginFlowCompleted (server_id={serverId}, pin_len={pin.Length}). " +
                 "Advancing state 1→2 Load. spec: client_runtime.md §7.5.1 / §7.9.5.");

        // Re-login with collected pin for the full credential string. spec: login_flow.md §4.2.
        if (_ctx?.UseCases is { } useCases)
            _ = useCases.LoginAsync(_account, _password, pin, CancellationToken.None);

        if (_ctx?.SceneMachine.Current.State == EngineSceneState.Login)
            _host?.Advance();
    }

    // Called when LoginWindow reaches sub-state 39 (connecting popup shown).
    // Starts the real connect; on success calls NotifyConnectSuccess; on failure calls NotifyConnectFailed.
    // spec: Docs/RE/specs/frontend_layout_tables.md §2.2 state 39 / §4
    private void OnConnectRequested(int serverId, string pin)
    {
        GD.Print($"[LoginScene] ConnectRequested (server_id={serverId}). Spawning SelectServerAsync. spec: §2.2/§4.");
        // Cancel any previous in-flight attempt.
        _connectCts?.Cancel();
        _connectCts?.Dispose();
        _connectCts = new CancellationTokenSource();
        _ = SelectServerAsync((ushort)serverId, pin, _connectCts.Token);
    }

    // Called when the user cancels the connecting popup (action 113).
    // spec: Docs/RE/specs/frontend_layout_tables.md §4 "clicking Cancel aborts the join"
    private void OnConnectCancelled()
    {
        GD.Print("[LoginScene] ConnectCancelled: cancelling in-flight connect. spec: §4.");
        _connectCts?.Cancel();
        _connectCts?.Dispose();
        _connectCts = null;
    }

    private async Task SelectServerAsync(ushort serverId, string pin, CancellationToken ct)
    {
        if (_ctx?.UseCases is not { } useCases)
        {
            // Offline / no UseCases: fail immediately so the popup doesn't hang.
            // spec: §4 "offline-safe: popup shows briefly then failure path shows error box"
            GD.PrintErr($"[LoginScene] SelectServerAsync({serverId}): no UseCases (offline). Reporting failure. spec: §4.");
            CallDeferred(MethodName.ReportConnectFailed);
            return;
        }

        try
        {
            MartialHeroes.Network.Abstractions.Lobby.LobbyChannelEndpoint endpoint =
                await useCases.SelectServerAsync(serverId, ct).ConfigureAwait(false);

            ct.ThrowIfCancellationRequested();

            // spec: Docs/RE/specs/login_flow.md §2.2 — lobby resolves the game-server host:port.
            if (_ctx is not null)
                await _ctx.OpenGameConnectionAsync(endpoint.Host, endpoint.Port).ConfigureAwait(false);

            ct.ThrowIfCancellationRequested();

            // Success: notify LoginWindow to advance to state 41 (LoginFlowCompleted).
            // spec: frontend_layout_tables.md §4 "successful handshake → char-list → advance scene"
            GD.Print($"[LoginScene] SelectServerAsync({serverId}): connect SUCCESS. Notifying LoginWindow. spec: §4.");
            CallDeferred(MethodName.ReportConnectSuccess);
        }
        catch (OperationCanceledException)
        {
            // Cancelled by the user via action 113 — already handled by OnConnectCancelled.
            GD.Print($"[LoginScene] SelectServerAsync({serverId}): cancelled by user. spec: §4.");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[LoginScene] SelectServerAsync({serverId}) failed: {ex.Message}");
            // Connect failure: notify LoginWindow to show the §2.1a countdown error box (msg 4028).
            // spec: frontend_layout_tables.md §2.1a "connect failure → msg 4028 countdown error → return to list"
            CallDeferred(MethodName.ReportConnectFailed);
        }
    }

    // Must run on the main thread (Control mutation via LoginWindow). Called via CallDeferred.
    private void ReportConnectSuccess()
    {
        _login?.NotifyConnectSuccess(); // → state 41 → LoginFlowCompleted. spec: §2.2/§4.
    }

    // Must run on the main thread (Control mutation via LoginWindow). Called via CallDeferred.
    private void ReportConnectFailed()
    {
        _login?.NotifyConnectFailed(); // → hide popup → msg 4028 countdown → state 34/37. spec: §2.1a/§4.
    }

    private void OnQuitRequested()
    {
        GD.Print("[LoginScene] Quit requested from LoginWindow.");
        _ctx?.SceneMachine.RequestQuit();
        GetTree().Quit();
    }
}