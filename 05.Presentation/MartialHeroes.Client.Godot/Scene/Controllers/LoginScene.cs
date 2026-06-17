using Godot;
using MartialHeroes.Client.Application.Events;
using MartialHeroes.Client.Application.Scene;
using MartialHeroes.Client.Godot.Autoload;
using MartialHeroes.Client.Godot.Screens;       // ServerEntry, UiAssetLoader, FrontEndAudio, ScreenHost
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
    private const string ConfigResPath = "res://client_dir.cfg";
    private const string DevOfflineKey = "dev_offline_flow";

    private ClientContext? _ctx;
    private SceneHost? _host;
    private ScreenHost? _screenHost;
    private FrontEndAudio? _audio;
    private LoginWindow? _login;
    private ServerSelectSubView? _serverSelect;
    private string _account = "";
    private string _password = "";
    private bool _syncingScene;

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
        if (_login is not null)
        {
            _login.LoginAccepted -= OnLoginAccepted;
            _login.QuitRequested -= OnQuitRequested;
            _login.LoginFlowCompleted -= OnLoginFlowCompleted;
        }
    }

    public override void _Process(double delta)
    {
        if (_ctx is null || _syncingScene) return;

        // State 1 is the sole presentation event consumer while Login is live. Route only the login
        // scene's events here, then state-2+ controllers take over after the host re-dispatches.
        // spec: PRESERVATION_AND_ARCHITECTURE.md §05.Presentation — drain Application channels on _Process.
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
        // Resolve the HudAtlasLibrary and HudTextLibrary from the composition root.
        // Both degrade gracefully when the VFS is offline (null-backed).
        var atlas = _ctx?.HudAtlas
                    ?? new MartialHeroes.Client.Godot.Ui.Assets.HudAtlasLibrary(null);
        var text  = _ctx?.HudText
                    ?? new MartialHeroes.Client.Godot.Ui.Assets.HudTextLibrary(null);

        var login = new LoginWindow(atlas, text)
        {
            Name              = "LoginWindow",
            Audio             = _audio,
            PinFactory        = CreateInLoginPin,
            ServerSelectFactory = CreateInLoginServerSelect,
        };

        if (IsDevOfflineMode())
        {
            login.DevPrefillId = DevAccountId();
            login.DevPrefillPw = DevAccountPw();
        }

        login.LoginAccepted     += OnLoginAccepted;
        login.QuitRequested     += OnQuitRequested;
        login.LoginFlowCompleted += OnLoginFlowCompleted;
        return login;
    }

    private void OnLoginAccepted(string account, string password)
    {
        _account = account;
        _password = password;

        // Stage credentials immediately, before the optional PIN re-stage at chain completion.
        // spec: Docs/RE/specs/login_flow.md §4.2; Docs/RE/specs/crypto.md §6.1.
        if (_ctx?.UseCases is { } useCases)
            _ = useCases.LoginAsync(account, password, cancellationToken: CancellationToken.None);

        GD.Print($"[LoginScene] Login accepted (account='{account}'); credentials staged; " +
                 "PIN/server sub-states remain inside state 1. spec: login_flow.md §4.2.");
    }

    private PinSubView CreateInLoginPin()
    {
        var atlas = _ctx?.HudAtlas
                    ?? new MartialHeroes.Client.Godot.Ui.Assets.HudAtlasLibrary(null);

        var pin = new PinSubView(atlas)
        {
            Name = "PinSubView",
            HostInReferenceSpace = true,
        };

        if (IsDevOfflineMode())
            pin.DevPrefillPin = DevAccountPin();

        GD.Print("[LoginScene] Created in-login PinSubView (sub-states 31/32) on HudAtlasLibrary. " +
                 "spec: frontend_scenes.md §11.3.");
        return pin;
    }

    private ServerSelectSubView CreateInLoginServerSelect()
    {
        var atlas = _ctx?.HudAtlas
                    ?? new MartialHeroes.Client.Godot.Ui.Assets.HudAtlasLibrary(null);
        var text  = _ctx?.HudText
                    ?? new MartialHeroes.Client.Godot.Ui.Assets.HudTextLibrary(null);

        _serverSelect = new ServerSelectSubView(atlas, text)
        {
            Name = "ServerSelectSubView",
        };

        if (IsDevOfflineMode())
            _serverSelect.SetServers(DevServerList());
        else
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
                StatusCode: e.Status,
                Population: e.Population,
                Flag: e.Flag));
        }

        _serverSelect.SetServers(entries);
        GD.Print($"[LoginScene] Applied ServerListReceivedEvent ({entries.Count} entries) to state-1 server-list. " +
                 "spec: login_flow.md §2.1.");
    }

    private void OnLoginFlowCompleted(int serverId, string pin)
    {
        GD.Print($"[LoginScene] Login chain complete (server_id={serverId}, pin_len={pin.Length}). " +
                 "Advancing state 1→2 Load. spec: client_runtime.md §7.5.1 / §7.9.5.");

        if (_ctx?.UseCases is { } useCases)
        {
            // Re-stage with the collected PIN and resolve the selected channel through the Application
            // facade. Both are passive intents; failures leave the dev/offline visual flow walkable.
            _ = useCases.LoginAsync(_account, _password, pin, CancellationToken.None);
            _ = SelectServerAsync((ushort)serverId);
        }

        if (_ctx?.SceneMachine.Current.State == EngineSceneState.Login)
            _host?.Advance();
    }

    private async Task SelectServerAsync(ushort serverId)
    {
        if (_ctx?.UseCases is not { } useCases) return;

        try
        {
            await useCases.SelectServerAsync(serverId, CancellationToken.None);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[LoginScene] SelectServerAsync({serverId}) skipped/failed: {ex.Message}");
        }
    }

    private void OnQuitRequested()
    {
        GD.Print("[LoginScene] Quit requested from LoginWindow.");
        _ctx?.SceneMachine.RequestQuit();
        GetTree().Quit();
    }

    private static string DevAccountId() => ReadCfgKey("dev_account_id", "xwdvg26");
    private static string DevAccountPw() => ReadCfgKey("dev_account_pw", "crfgb727*");
    private static string DevAccountPin() => ReadCfgKey("dev_account_pin", "1472");

    private static IReadOnlyList<ServerEntry> DevServerList() =>
    [
        new ServerEntry(ServerId: 1, DisplayName: "무신", StatusCode: 0, Population: 120, Flag: 1),
        new ServerEntry(ServerId: 2, DisplayName: "천마", StatusCode: 0, Population: 640, Flag: 1),
    ];

    private static bool IsDevOfflineMode()
    {
        string? envVal = System.Environment.GetEnvironmentVariable("DEV_OFFLINE_FLOW");
        if (envVal is "1" or "true" or "yes")
        {
            GD.Print("[LoginScene] DEV_OFFLINE_FLOW env var is set → offline replay active.");
            return true;
        }

        string val = ReadCfgKey(DevOfflineKey, "0");
        bool enabled = val is "1" or "true" or "yes";
        if (enabled)
            GD.Print("[LoginScene] dev_offline_flow=1 in client_dir.cfg → offline replay active.");
        return enabled;
    }

    private static string ReadCfgKey(string key, string defaultValue)
    {
        string? path = ResolveCfgPath();
        if (path is null) return defaultValue;

        try
        {
            foreach (string rawLine in File.ReadLines(path))
            {
                string line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith('#')) continue;

                int eq = line.IndexOf('=');
                if (eq < 0) continue;

                string k = line[..eq].Trim();
                string v = line[(eq + 1)..].Trim();
                if (k.Equals(key, StringComparison.OrdinalIgnoreCase) && v.Length > 0)
                    return v;
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[LoginScene] ReadCfgKey('{key}'): {ex.Message}");
        }

        return defaultValue;
    }

    private static string? ResolveCfgPath()
    {
        try
        {
            string abs = ProjectSettings.GlobalizePath(ConfigResPath);
            return File.Exists(abs) ? abs : null;
        }
        catch
        {
            return File.Exists("client_dir.cfg") ? "client_dir.cfg" : null;
        }
    }
}