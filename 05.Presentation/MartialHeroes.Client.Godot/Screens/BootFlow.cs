// Screens/BootFlow.cs
//
// Top-level boot flow node. Reads boot_flow= from client_dir.cfg and either:
//   boot_flow=world  → loads Scenes/World.tscn directly (TODAY'S behaviour, byte-for-byte).
//   boot_flow=login  → shows the LoginScreen → CharacterSelectScreen → then loads World.tscn
//                      (the legacy state 1 → 4 → 5 flow; spec §6.2).
// DEFAULT: login (per the task brief).
//
// This node is self-contained: it is the new main scene root and changes nothing in World.tscn or
// the World/* scripts (owned by another agent). The world path simply instances the unchanged
// World.tscn under this node, so the existing GameLoop / ClientContext wiring runs identically.
//
// PASSIVE: zero game logic. The login/select screens emit C# signals (LoginAccepted,
// EnterGameRequested); this node reacts only by swapping the displayed screen or loading the
// world scene. No credentials are validated, no packets are parsed, no domain state is touched.
//
// spec: Docs/RE/specs/ui_system.md §6.2 (state machine 1→4→5), §8.5 (scene flow), §8.1 (canvas).

using Godot;

namespace MartialHeroes.Client.Godot.Screens;

/// <summary>
/// The boot-flow controller. Decides between the menu flow (login → select → world) and the
/// direct world boot, based on the <c>boot_flow=</c> key in <c>client_dir.cfg</c>.
/// </summary>
public sealed partial class BootFlow : Node
{
    // Path to the unchanged world scene (today's direct-boot target).
    private const string WorldScenePath = "res://Scenes/World.tscn";

    // client_dir.cfg key controlling the boot path. spec: task brief — boot_flow=login|world.
    private const string BootFlowKey = "boot_flow";
    private const string ConfigResPath = "res://client_dir.cfg";

    private CanvasLayer? _uiLayer;
    private ScreenHost? _host;
    private UiAssetLoader? _sharedAssets;

    public override void _Ready()
    {
        GD.Print("===== [Screens] BootFlow _Ready ENTERED =====");

        string flow = ReadBootFlow();
        GD.Print($"[Screens] boot_flow='{flow}'.");

        try
        {
            if (flow.Equals("world", StringComparison.OrdinalIgnoreCase))
            {
                EnterWorld();
            }
            else
            {
                // Default and explicit "login": run the menu flow.
                StartMenuFlow();
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[Screens] BootFlow failed: {ex.Message} — falling back to direct world boot.");
            EnterWorld();
        }

        GD.Print("===== [Screens] BootFlow _Ready COMPLETED =====");
    }

    public override void _ExitTree()
    {
        _sharedAssets?.Dispose();
        _sharedAssets = null;
    }

    // ---------------------------------------------------------------------
    // Menu flow: login → character-select → world
    // ---------------------------------------------------------------------

    private void StartMenuFlow()
    {
        // One VFS handle shared by both screens for atlas chrome + msg.xdb strings.
        _sharedAssets = UiAssetLoader.Open();

        // A CanvasLayer keeps the menu UI above any 3D content and independent of camera.
        _uiLayer = new CanvasLayer { Name = "MenuUiLayer", Layer = 10 };
        AddChild(_uiLayer);

        _host = new ScreenHost { Name = "ScreenHost" };
        _uiLayer.AddChild(_host);

        ShowLogin();
    }

    private void ShowLogin()
    {
        var login = new LoginScreen { Name = "LoginScreen", SharedAssets = _sharedAssets };
        login.LoginAccepted += OnLoginAccepted;
        login.QuitRequested += OnQuitRequested;
        _host!.SetScreen(login);
    }

    private void OnLoginAccepted(string account)
    {
        // spec §6.2 — login (state 1) advances toward character-select (state 4).
        GD.Print($"[Screens] BootFlow: login accepted (account='{account}') → character select.");
        ShowCharacterSelect();
    }

    private void OnQuitRequested()
    {
        GD.Print("[Screens] BootFlow: quit requested — closing.");
        GetTree().Quit();
    }

    private void ShowCharacterSelect()
    {
        var select = new CharacterSelectScreen { Name = "CharacterSelectScreen", SharedAssets = _sharedAssets };
        select.EnterGameRequested += OnEnterGameRequested;
        select.BackRequested += OnBackToLogin;
        _host!.SetScreen(select);
    }

    private void OnBackToLogin()
    {
        GD.Print("[Screens] BootFlow: back to login.");
        ShowLogin();
    }

    private void OnEnterGameRequested(string characterName)
    {
        // spec §6.2 — character select (state 4) → in-game (state 5).
        GD.Print($"[Screens] BootFlow: enter game (character='{characterName}') → loading world.");
        TeardownMenu();
        EnterWorld();
    }

    private void TeardownMenu()
    {
        if (_uiLayer is not null && IsInstanceValid(_uiLayer))
        {
            _uiLayer.QueueFree();
            _uiLayer = null;
            _host = null;
        }
    }

    // ---------------------------------------------------------------------
    // World boot: instance the unchanged World.tscn (today's behaviour).
    // ---------------------------------------------------------------------

    private void EnterWorld()
    {
        var packed = GD.Load<PackedScene>(WorldScenePath);
        if (packed is null)
        {
            GD.PrintErr($"[Screens] BootFlow: could not load '{WorldScenePath}'.");
            return;
        }

        Node world = packed.Instantiate();
        // Add under the scene root's parent so the world tree behaves exactly as a top-level
        // World scene would (it self-resolves /root/ClientContext autoload).
        AddChild(world);
        GD.Print("[Screens] BootFlow: World scene instanced.");
    }

    // ---------------------------------------------------------------------
    // Config read — same simple key=value .cfg format the other keys use.
    // ---------------------------------------------------------------------

    /// <summary>
    /// Reads <c>boot_flow=</c> from <c>client_dir.cfg</c>; defaults to <c>"login"</c>.
    /// Uses the same one-key-per-line, '#'-comment .cfg format that
    /// <see cref="MartialHeroes.Client.Godot.Dev.ClientPathResolver"/> uses for its keys.
    /// </summary>
    private static string ReadBootFlow()
    {
        string? path = ResolveCfgPath();
        if (path is null) return "login"; // default per task brief

        try
        {
            foreach (string rawLine in File.ReadLines(path))
            {
                string line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith('#')) continue;

                int eq = line.IndexOf('=');
                if (eq < 0) continue;

                string key = line[..eq].Trim();
                string val = line[(eq + 1)..].Trim();
                if (key.Equals(BootFlowKey, StringComparison.OrdinalIgnoreCase) && val.Length > 0)
                    return val;
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[Screens] BootFlow: could not read '{path}': {ex.Message} — default login.");
        }

        return "login";
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