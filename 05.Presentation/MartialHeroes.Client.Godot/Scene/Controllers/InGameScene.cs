using Godot;
using MartialHeroes.Client.Application.World;
using MartialHeroes.Client.Godot.Autoload;
using MartialHeroes.Client.Godot.Input;
using MartialHeroes.Client.Godot.Ui.Assets;
using MartialHeroes.Client.Godot.Ui.Hud;
using MartialHeroes.Client.Godot.World;
using MartialHeroes.Shared.Kernel.Enums;
using Environment = Godot.Environment;

namespace MartialHeroes.Client.Godot.Scene.Controllers;

public sealed partial class InGameScene : StubSceneController
{
    private ClientContext? _ctx;
    private SceneHost? _host;
    private CanvasLayer? _hudLayer;
    private HudMaster? _hudMaster;
    private GameLoop? _worldLoop;

    public override EngineSceneState State => EngineSceneState.InGame;

    public override void OnEnter(SceneHost host)
    {
        Name = $"Scene{(int)State}_{State}";
        _host = host;
        _ctx = GetNodeOrNull<ClientContext>("/root/ClientContext");

        _worldLoop = BuildGameWorld();
        AddChild(_worldLoop);
        _worldLoop.WorldExitRequested += OnWorldExitRequested;

        if (_ctx is not null)
            try
            {
                var icons = new HudIconLibrary(null, _ctx.HudAtlas);

                _hudLayer = new CanvasLayer
                {
                    Name = "HudCanvasLayer",
                    Layer = 128
                };
                AddChild(_hudLayer);

                _hudMaster = new HudMaster { Name = "HudMaster" };
                _hudLayer.AddChild(_hudMaster);
                _hudMaster.Build(_ctx, _ctx.HudAtlas, icons, _ctx.HudText);
                _hudMaster.BindHub(_ctx);
                _hudMaster.Reconfigure();

                _worldLoop.SetHudMaster(_hudMaster);

                GD.Print("[InGameScene] HudMaster built in CanvasLayer(128), bound, and hit-test wired. " +
                         "CanvasLayer makes the last-draw-over-3D invariant explicit and order-independent. " +
                         "spec: Docs/RE/scenes/ingame_composition.md §4.2 (UI/HUD +212 = LAST callback). " +
                         "spec: Docs/RE/specs/ui_hud_layout.md §0.");
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[InGameScene] HudMaster build failed (degraded mode): {ex.Message}");
            }

        GD.Print($"[InGameScene] State 5 BuildGameWorld built: charater scene root, " +
                 $"{WorldSceneContract.ViewPlatformCount} view-platform slots (WorldSceneContract.ViewPlatformCount), " +
                 $"{WorldSceneContract.LayerNodeCount} layer nodes (WorldSceneContract.LayerNodeCount; ids 2006/2004/2005/2148/2148), " +
                 "terrain stream node, real-asset renderer (world build deferred to the server 4/1 world-entry), " +
                 "camera/HUD wiring. spec: Docs/RE/specs/world_systems.md §13.1 / client_runtime.md §7.4/§9.");
    }

    public override void _ExitTree()
    {
        if (_worldLoop is not null && IsInstanceValid(_worldLoop))
            _worldLoop.WorldExitRequested -= OnWorldExitRequested;

        if (_ctx?.TimedEventQueue is { } tq)
        {
            var discarded = tq.FlushOnSceneTransition();
            GD.Print($"[InGameScene] _ExitTree: TimedEventQueue flushed ({discarded} pending events discarded). " +
                     "spec: Docs/RE/specs/effect-scheduling.md §5A.3 (scene-transition flush).");
        }

        _worldLoop = null;
        _host = null;
        _ctx = null;
        _hudMaster = null;
        _hudLayer = null;
    }

    private static GameLoop BuildGameWorld()
    {
        var world = new GameLoop
        {
            Name = "charater scene"
        };

        world.AddChild(BuildViewPlatformSlots());
        world.AddChild(BuildLayerNodes());
        world.AddChild(new ActorRegistry { Name = "ActorRegistry" });
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
        ReadOnlySpan<string> names = ["Third", "First", "Static", "Gamble", "Event"];
        if (names.Length != WorldSceneContract.ViewPlatformCount)
            GD.PushError($"[InGameScene] ViewPlatformSlots name count {names.Length} != " +
                         $"WorldSceneContract.ViewPlatformCount {WorldSceneContract.ViewPlatformCount}. " +
                         "spec: Docs/RE/specs/world_systems.md §13.1.");
        for (var i = 0; i < names.Length; i++)
            slots.AddChild(new Node3D { Name = $"GViewPlatform_{names[i]}" });
        return slots;
    }

    private static Node3D BuildLayerNodes()
    {
        var root = new Node3D { Name = "GLayerNodes" };
        var msgIds = WorldSceneContract.LayerNodeMessageIds;
        for (var i = 0; i < WorldSceneContract.LayerNodeCount; i++)
            root.AddChild(new Node3D { Name = $"GLayer_{msgIds[i]}_{i}" });
        return root;
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
            BackgroundColor =
                new Color(0.314f, 0.314f, 0.314f),
            AmbientLightSource = Environment.AmbientSource.Color,
            AmbientLightColor = Colors.White,
            AmbientLightSkyContribution = 0f,
            AmbientLightEnergy = 1.0f,
            TonemapMode = Environment.ToneMapper.Linear,
            TonemapExposure = 1.0f,
            GlowEnabled = false,
            SsaoEnabled = false,
            SsilEnabled = false,
            SdfgiEnabled = false,
            FogEnabled = false
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

        _host?.CallDeferred(SceneHost.MethodName.Advance);
    }
}