using Godot;
using MartialHeroes.Client.Application.Contracts.Hud;
using MartialHeroes.Client.Domain.Simulation.Simulation;
using MartialHeroes.Client.Godot.Autoload;
using MartialHeroes.Client.Godot.Input;
using MartialHeroes.Client.Godot.Ui.Hud;
using MartialHeroes.Shared.Kernel.Enums;

namespace MartialHeroes.Client.Godot.World;

public sealed partial class GameLoop : Node
{
    [Signal]
    public delegate void WorldExitRequestedEventHandler(bool logout);

    private readonly Action<TimedEventRecord> _onTimedEventDelegate;


    private ActorRegistry _actorRegistry = null!;
    private ClientContext _clientContext = null!;

    private EffectRenderer? _effectRenderer;
    private bool _hasLocalPlayer;

    private IHudEventHub? _hudHub;

    private HudMaster? _hudMaster;
    private InputRouter _inputRouter = null!;

    private uint _localHp, _localMaxHp, _localMp, _localMaxMp, _localStam, _localMaxStam;

    private float _localPlayerLegacyX;
    private float _localPlayerLegacyZ;

    private RealWorldRenderer? _realWorldRenderer;
    private TerrainNode _terrainNode = null!;


    public GameLoop()
    {
        _onTimedEventDelegate = OnTimedEvent;
    }


    public override void _Ready()
    {
        GD.Print("===== [GameLoop] _Ready ENTERED =====");

        try
        {
            ReadyInternal();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[GameLoop] _Ready failed: {ex}");
        }

        GD.Print("===== [GameLoop] _Ready COMPLETED =====");
    }

    public void SetHudMaster(HudMaster hudMaster)
    {
        _hudMaster = hudMaster;
        if (_clientContext is not null)
        {
            _clientContext.SetHudHitTest(hudMaster.HitTest);
            GD.Print("[GameLoop] HudInputHandler.HitTest wired to HudMaster.HitTest. spec: input_ui.md §3/§6.");
        }
    }

    private void ReadyInternal()
    {
        GD.Print("[GameLoop] ReadyInternal: resolving ClientContext");

        _clientContext = GetNode<ClientContext>("/root/ClientContext");

        _actorRegistry = GetNode<ActorRegistry>("ActorRegistry");
        _inputRouter = GetNode<InputRouter>("InputRouter");

        if (HasNode("TerrainNode"))
        {
            _terrainNode = GetNode<TerrainNode>("TerrainNode");
        }
        else
        {
            _terrainNode = new TerrainNode();
            _terrainNode.Name = "TerrainNode";
            AddChild(_terrainNode);
        }

        GD.Print("[GameLoop] ReadyInternal: child nodes resolved — wiring subsystems");

        _hudHub = _clientContext.HudEventHub;

        try
        {
            _actorRegistry.Initialise(_clientContext);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[GameLoop] ActorRegistry.Initialise failed: {ex.Message}");
        }

        try
        {
            _actorRegistry.SetTerrainNode(_terrainNode);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[GameLoop] ActorRegistry.SetTerrainNode failed: {ex.Message}");
        }

        try
        {
            _inputRouter.Initialise(_clientContext);
            _inputRouter.InitialiseBus(_clientContext.InputBus);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[GameLoop] InputRouter.Initialise failed: {ex.Message}");
        }

        GD.Print("[GameLoop] ReadyInternal: subsystems wired — checking real-asset renderer");

        var realRendererStarted = false;
        if (RealWorldRenderer.IsEnabled)
        {
            GD.Print("[GameLoop] Real assets enabled — attempting real-asset renderer.");
            try
            {
                _realWorldRenderer = new RealWorldRenderer();
                _realWorldRenderer.Name = "RealWorldRenderer";
                AddChild(_realWorldRenderer);
                _realWorldRenderer.Initialise(_clientContext, _terrainNode);
                realRendererStarted = true;
                GD.Print("[GameLoop] RealWorldRenderer initialised successfully.");

                if (_clientContext.WorldEntry is { IsActive: true } entry)
                {
                    GD.Print($"[GameLoop] InGameWorldBootstrappedEvent: server AreaId={entry.AreaId} " +
                             "(recovered from durable WorldEntryState — 3-digit dir → <id>.lst). " +
                             "spec: world_entry.md §2.3/§3.1.");
                    _realWorldRenderer.OnWorldEntered(entry.AreaId, entry.SpawnPosition);
                }
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[GameLoop] RealWorldRenderer.Initialise failed: {ex}");
                GD.PrintErr(
                    "[GameLoop] RealWorldRenderer init failed — world will remain empty until VFS is resolved.");
                if (_realWorldRenderer is not null && IsInstanceValid(_realWorldRenderer))
                {
                    _realWorldRenderer.QueueFree();
                    _realWorldRenderer = null;
                }
            }
        }

        if (!realRendererStarted)
            GD.Print("[GameLoop] Real assets unavailable — world will be empty until VFS is resolved.");

        try
        {
            _effectRenderer = new EffectRenderer { Name = "EffectRenderer" };
            AddChild(_effectRenderer);
            _effectRenderer.Bind(_clientContext.HudEventHub);
            GD.Print("[GameLoop] EffectRenderer added + bound to HudEventHub.");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[GameLoop] EffectRenderer init failed: {ex.Message}");
        }

        _ = _clientContext.RegionService.LoadAreaAsync(0).AsTask().ContinueWith(
            t =>
            {
                if (t.IsFaulted)
                    GD.PrintErr(
                        $"[GameLoop] RegionService.LoadAreaAsync(0) failed: {t.Exception?.InnerException?.Message}");
                else
                    GD.Print("[GameLoop] RegionService: area 0 region data loaded. " +
                             "spec: Docs/RE/specs/world_systems.md Ch. 16.");
            },
            TaskScheduler.Default);

        GD.Print("[GameLoop] Ready.");
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true } key) return;

        if (key.PhysicalKeycode == Key.Escape)
        {
            GetViewport().SetInputAsHandled();
            GD.Print("[GameLoop] Leave-world requested (ESC) → return to char-select. " +
                     "spec: Docs/RE/specs/client_runtime.md §7.5.1 (state 5 → 4).");
            EmitSignal(SignalName.WorldExitRequested, false);
        }
    }


    public override void _Input(InputEvent evt)
    {
        if (_clientContext?.SceneMachine.Current.State != EngineSceneState.InGame)
            return;

        if (evt is not InputEventKey key || !key.Pressed || key.Echo)
            return;

        switch (key.Keycode)
        {
            case Key.I:
                _hudMaster?.ToggleInventory();
                GetViewport().SetInputAsHandled();
                break;

            case Key.K:
                _hudMaster?.ToggleSkill();
                GetViewport().SetInputAsHandled();
                break;

            case Key.O:
                GetViewport().SetInputAsHandled();
                break;

            case Key.C:
                _hudMaster?.ToggleStats();
                GetViewport().SetInputAsHandled();
                break;
        }
    }
}