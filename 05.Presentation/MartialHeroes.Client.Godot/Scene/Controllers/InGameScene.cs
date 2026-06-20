using Godot;
using MartialHeroes.Client.Godot.Autoload;
using MartialHeroes.Client.Godot.Input;
using MartialHeroes.Client.Godot.Ui.Assets;
using MartialHeroes.Client.Godot.Ui.Hud;
using MartialHeroes.Client.Godot.World;
using MartialHeroes.Shared.Kernel.Enums;
using Environment = Godot.Environment;

namespace MartialHeroes.Client.Godot.Scene.Controllers;

/// <summary>
///     State 5 — In-game world. Builds the passive Godot world scene graph owned by the recovered
///     BuildGameWorld case body: scene root, camera rig, terrain/building/NPC/player renderers, input,
///     per-frame event drain, and HUD.
///     spec: Docs/RE/specs/client_runtime.md §7.4 / §9; world_systems.md; terrain-streaming.md.
/// </summary>
public sealed partial class InGameScene : StubSceneController
{
    private ClientContext? _ctx;
    private SceneHost? _host;
    private HudMaster? _hudMaster;
    private GameLoop? _worldLoop;

    /// <inheritdoc />
    public override EngineSceneState State => EngineSceneState.InGame;

    /// <inheritdoc />
    public override void OnEnter(SceneHost host)
    {
        Name = $"Scene{(int)State}_{State}";
        _host = host;
        _ctx = GetNodeOrNull<ClientContext>("/root/ClientContext");

        _worldLoop = BuildGameWorld();
        AddChild(_worldLoop);
        _worldLoop.WorldExitRequested += OnWorldExitRequested;

        // CAMPAIGN 17 Wave 2b — HudMaster is now THE sole in-game HUD.
        // GameLoop no longer builds a GameHud; HudMaster drains IHudEventHub channels that
        // GameLoop now publishes into. The hit-test is wired via SetHudMaster so the
        // "UI is the gate" contract is preserved.
        // spec: Docs/RE/specs/ui_hud_layout.md §0 — HUD-build routine asset pipeline.
        // spec: Docs/RE/specs/input_ui.md §3 / §6 — "UI hit-test always before world interaction".
        if (_ctx is not null)
            try
            {
                // Re-use the shared HudAtlasLibrary and HudTextLibrary already initialised
                // by ClientContext (they own the uitex.txt + msg.xdb VFS handles).
                // HudIconLibrary: pass null for assets (degrades to no-icon offline mode)
                // since the RealClientAssets handle is private to HudAtlasLibrary/ClientContext.
                // TODO(composition): expose a HudIconLibrary from ClientContext (like HudAtlas/HudText).
                // spec: Docs/RE/specs/ui_hud_layout.md §0 — HUD-build routine asset pipeline.
                var icons = new HudIconLibrary(null, _ctx.HudAtlas);

                _hudMaster = new HudMaster { Name = "HudMaster" };
                AddChild(_hudMaster);
                _hudMaster.Build(_ctx, _ctx.HudAtlas, icons, _ctx.HudText);
                _hudMaster.BindHub(_ctx);
                _hudMaster.Reconfigure();

                // Wire the HudMaster hit-test into HudInputHandler so HUD clicks are gated
                // before world/camera input sees them.
                // spec: Docs/RE/specs/input_ui.md §3 / §6.
                _worldLoop.SetHudMaster(_hudMaster);

                GD.Print("[InGameScene] HudMaster built, bound, and hit-test wired (CAMPAIGN 17 Wave 2b). " +
                         "GameHud removed — HudMaster is now the sole in-game HUD. " +
                         "spec: Docs/RE/specs/ui_hud_layout.md §0.");
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[InGameScene] HudMaster build failed (degraded mode): {ex.Message}");
            }

        GD.Print("[InGameScene] State 5 BuildGameWorld built: charater scene root, five view-platform slots, " +
                 "terrain stream node, real-asset renderer (area from client_dir.cfg; area 2 town by default), " +
                 "NPC/player/camera/HUD wiring. spec: client_runtime.md §7.4/§9.");
    }

    public override void _ExitTree()
    {
        if (_worldLoop is not null && IsInstanceValid(_worldLoop))
            _worldLoop.WorldExitRequested -= OnWorldExitRequested;

        _worldLoop = null;
        _host = null;
        _ctx = null;
        _hudMaster = null;
    }

    private static GameLoop BuildGameWorld()
    {
        var world = new GameLoop
        {
            // The typo is authentic: the legacy GScene root is labelled "charater scene".
            // spec: Docs/RE/specs/client_runtime.md §9.1 / §9.4.
            Name = "charater scene"
        };

        world.AddChild(BuildViewPlatformSlots());
        world.AddChild(new ActorRegistry { Name = "ActorRegistry" });
        // GameHud { Name="HUD" } removed (CAMPAIGN 17 Wave 2b) — HudMaster is the sole HUD.
        // spec: Docs/RE/specs/ui_hud_layout.md §0.
        world.AddChild(new InputRouter { Name = "InputRouter" });
        world.AddChild(new TerrainNode { Name = "TerrainNode" });
        world.AddChild(BuildDirectionalLight());
        world.AddChild(BuildBootstrapCamera());
        world.AddChild(BuildWorldEnvironment());

        return world;
    }

    private static Node3D BuildViewPlatformSlots()
    {
        var slots = new Node3D { Name = "GViewPlatformSlots" };
        // The legacy builder allocates five in-world view-platform slots: Third, First, Static,
        // Gamble, Event. CameraController owns the active Godot camera and implements the playable
        // manipulators; these passive markers preserve the recovered scene-graph shape.
        // spec: Docs/RE/specs/client_runtime.md §9.1 / §9.5; camera_movement.md §A.
        foreach (var name in new[] { "Third", "First", "Static", "Gamble", "Event" })
            slots.AddChild(new Node3D { Name = $"GViewPlatform_{name}" });
        return slots;
    }

    private static DirectionalLight3D BuildDirectionalLight()
    {
        return new DirectionalLight3D
        {
            Name = "DirectionalLight3D",
            Transform = new Transform3D(
                new Vector3(0.707107f, -0.5f, 0.5f),
                new Vector3(0f, 0.707107f, 0.707107f),
                new Vector3(-0.707107f, -0.5f, 0.5f),
                new Vector3(0f, 50f, 0f)),
            LightEnergy = 1.8f,
            LightColor = new Color(1f, 0.96f, 0.88f),
            ShadowEnabled = true,
            DirectionalShadowMaxDistance = 2000f
        };
    }

    private static Camera3D BuildBootstrapCamera()
    {
        return new Camera3D
        {
            Name = "Camera3D",
            Transform = new Transform3D(
                Vector3.Right,
                new Vector3(0f, 0.707107f, -0.707107f),
                new Vector3(0f, 0.707107f, 0.707107f),
                new Vector3(0f, 25f, 25f)),
            Fov = 65f,
            Near = 5f,
            Far = 15000f,
            Current = true
        };
    }

    private static WorldEnvironment BuildWorldEnvironment()
    {
        var env = new Environment
        {
            BackgroundMode = Environment.BGMode.Color,
            BackgroundColor = new Color(0.45f, 0.45f, 0.45f),
            AmbientLightSource = Environment.AmbientSource.Color,
            AmbientLightColor = Colors.White,
            AmbientLightSkyContribution = 0.5f,
            AmbientLightEnergy = 1.0f,
            TonemapMode = Environment.ToneMapper.Filmic,
            TonemapExposure = 1.1f,
            TonemapWhite = 6.0f,
            GlowEnabled = false,
            SsaoEnabled = false,
            SsilEnabled = false,
            SdfgiEnabled = false
        };

        return new WorldEnvironment
        {
            Name = "WorldEnvironment",
            Environment = env
        };
    }

    private void OnWorldExitRequested(bool logout)
    {
        GD.Print($"[InGameScene] WorldExitRequested(logout={logout}). spec: client_runtime.md §7.5.1/§7.5.3.");

        if (logout)
        {
            _ctx?.SceneMachine.RequestQuit();
            return;
        }

        // Normal state-5 case-body return is 5 → 4; SceneHost owns the re-dispatch.
        // spec: Docs/RE/specs/client_runtime.md §7.5.1.
        _host?.CallDeferred(SceneHost.MethodName.Advance);
    }
}