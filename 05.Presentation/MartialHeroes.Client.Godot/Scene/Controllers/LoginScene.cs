using Godot;
using MartialHeroes.Client.Application.Contracts.Events;
using MartialHeroes.Client.Application.Contracts.Scene;
using MartialHeroes.Client.Godot.Autoload;
using MartialHeroes.Client.Godot.Ui.Scenes;
using MartialHeroes.Client.Godot.Ui.Scenes.Login;
using MartialHeroes.Shared.Kernel.Enums;

namespace MartialHeroes.Client.Godot.Scene.Controllers;

public sealed partial class LoginScene : StubSceneController
{
    private string _account = "";
    private FrontEndAudio? _audio;

    private CancellationTokenSource? _connectCts;
    private ClientContext? _ctx;
    private SceneHost? _host;
    private LoginWindow? _login;
    private string _password = "";
    private ScreenHost? _screenHost;
    private ServerSelectSubView? _serverSelect;
    private bool _syncingScene;

    public override EngineSceneState State => EngineSceneState.Login;

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

        while (_ctx.EventBus.Reader.TryRead(out var evt))
            switch (evt)
            {
                case ServerListReceivedEvent serverList:
                    ApplyServerList(serverList);
                    break;
                case SceneStateChangedEvent stateChange when stateChange.Next.State != State:
                    GD.Print(
                        $"[LoginScene] SceneStateChangedEvent {stateChange.Previous.State}→{stateChange.Next.State}; " +
                        "out-of-band committed transition — calling SyncToCurrentState. spec: client_runtime.md §7.5.2.");
                    _syncingScene = true;
                    _host?.CallDeferred(SceneHost.MethodName.SyncToCurrentState);
                    return;
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
            ServerSelectFactory = CreateInLoginServerSelect
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

        if (_ctx?.UseCases is { } useCases)
            _ = useCases.LoginAsync(account, password, cancellationToken: CancellationToken.None);

        GD.Print($"[LoginScene] Login accepted (account='{account}'); credentials staged; " +
                 "PIN/server sub-states remain inside state 1. spec: login_flow.md §4.2.");
    }

    private PinSubView CreateInLoginPin()
    {
        var pin = new PinSubView(_ctx?.HudAtlas ?? throw new InvalidOperationException("ClientContext lost."))
        {
            Name = "PinSubView"
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
            Name = "ServerSelectSubView"
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

        _serverSelect.SetServers(serverList.Servers);
        GD.Print(
            $"[LoginScene] Applied ServerListReceivedEvent ({serverList.Servers.Length} entries) to state-1 server-list. " +
            "spec: login_flow.md §2.1.");
    }

    private void OnLoginFlowCompleted(int serverId, string pin)
    {
        GD.Print($"[LoginScene] LoginFlowCompleted (server_id={serverId}, pin_len={pin.Length}). " +
                 "Advancing state 1→2 Load. spec: client_runtime.md §7.5.1 / §7.9.5.");

        if (_ctx?.UseCases is { } useCases)
            _ = useCases.LoginAsync(_account, _password, pin, CancellationToken.None);

        if (_ctx?.SceneMachine.Current.State == EngineSceneState.Login)
            _host?.Advance();
    }

    private void OnConnectRequested(int serverId, string pin)
    {
        GD.Print($"[LoginScene] ConnectRequested (server_id={serverId}). Spawning SelectServerAsync. spec: §2.2/§4.");
        _connectCts?.Cancel();
        _connectCts?.Dispose();
        _connectCts = new CancellationTokenSource();
        _ = SelectServerAsync((ushort)serverId, pin, _connectCts.Token);
    }

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
            GD.PrintErr(
                $"[LoginScene] SelectServerAsync({serverId}): no UseCases — cannot connect. spec: §4.");
            CallDeferred(MethodName.ReportConnectFailed);
            return;
        }

        try
        {
            var endpoint =
                await useCases.SelectServerAsync(serverId, ct).ConfigureAwait(false);

            ct.ThrowIfCancellationRequested();

            if (_ctx is not null)
                await _ctx.OpenGameConnectionAsync(endpoint.Host, endpoint.Port).ConfigureAwait(false);

            ct.ThrowIfCancellationRequested();

            GD.Print($"[LoginScene] SelectServerAsync({serverId}): connect SUCCESS. Notifying LoginWindow. spec: §4.");
            CallDeferred(MethodName.ReportConnectSuccess);
        }
        catch (OperationCanceledException)
        {
            GD.Print($"[LoginScene] SelectServerAsync({serverId}): cancelled by user. spec: §4.");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[LoginScene] SelectServerAsync({serverId}) failed: {ex.Message}");
            CallDeferred(MethodName.ReportConnectFailed);
        }
    }

    private void ReportConnectSuccess()
    {
        _login?.NotifyConnectSuccess();
    }

    private void ReportConnectFailed()
    {
        _login?.NotifyConnectFailed();
    }

    private void OnQuitRequested()
    {
        GD.Print("[LoginScene] Quit requested from LoginWindow.");
        _ctx?.SceneMachine.RequestQuit();
        GetTree().Quit();
    }
}