using Godot;
using MartialHeroes.Assets.Vfs;
using MartialHeroes.Client.Application.Assets;
using MartialHeroes.Client.Application.Contracts.Events;
using MartialHeroes.Client.Application.Contracts.Hud;
using MartialHeroes.Client.Application.Contracts.Scene;
using MartialHeroes.Client.Application.Engine;
using MartialHeroes.Client.Application.Ingestion;
using MartialHeroes.Client.Application.Input;
using MartialHeroes.Client.Application.Net;
using MartialHeroes.Client.Application.UseCases;
using MartialHeroes.Client.Application.World;
using MartialHeroes.Client.Domain.Simulation.Simulation;
using MartialHeroes.Client.Godot.Adapters;
using MartialHeroes.Client.Godot.Audio;
using MartialHeroes.Client.Godot.Composition;
using MartialHeroes.Client.Godot.Ui.Assets;
using MartialHeroes.Client.Infrastructure.Catalog;
using MartialHeroes.Client.Presentation.Adapters;
using MartialHeroes.Client.Presentation.Input;
using MartialHeroes.Network.Abstractions.Session;
using MartialHeroes.Network.Transport.Pipelines;

namespace MartialHeroes.Client.Godot.Autoload;

public sealed partial class ClientContext : Node
{
    private VfsCatalogueLoader? _catalogueLoader;

    private IConnectionSession? _gameConnection;
    private volatile int _gameConnectionOpened;

    private HudInputHandler? _hudInputHandler;

    private Task? _inboundTask;

    private VfsResourcePipeline? _loadVfsPipeline;

    private CancellationTokenSource? _loopCts;
    private Task? _loopTask;

    private RelayOutboundPacketSink? _relaySink;

    private Action<IInputHandler>? _setWorldHandler;

    private MappedVfsArchive? _terrainVfs;

    private RealClientAssets? _uiAssets;

    public IClientEventBus EventBus { get; private set; } = null!;

    public IApplicationUseCases UseCases { get; private set; } = null!;

    public InboundFrameDispatcher Dispatcher { get; private set; } = null!;

    public SceneStateMachine SceneMachine { get; private set; } = null!;

    public LoadOrchestrator LoadOrchestrator { get; private set; } = null!;

    public InputBus InputBus { get; private set; } = null!;

    public GameEngineLoop EngineLoop { get; private set; } = null!;


    public ItemCatalogue ItemCatalogue { get; private set; } = null!;

    public SkillCatalogue SkillCatalogue { get; private set; } = null!;

    public MobCatalogue MobCatalogue { get; private set; } = null!;

    public SectorStreamingService StreamingService { get; private set; } = null!;

    public UiCatalogs UiCatalogs { get; private set; } = null!;


    public AudioService? Audio { get; private set; }

    public IHudEventHub HudEventHub { get; private set; } = null!;

    public BuffIconCatalog BuffIconCatalog { get; private set; } = null!;

    public ZoneCatalog ZoneCatalog { get; private set; } = null!;

    public HudAtlasLibrary HudAtlas { get; private set; } = null!;

    public HudTextLibrary HudText { get; private set; } = null!;

    public RegionService RegionService { get; private set; } = null!;

    public CharacterSelectionStore? CharacterSelection { get; private set; }

    public CellAssemblyHandoff? CellAssemblyHandoff { get; private set; }

    public AreaAssemblyHandoff? AreaAssemblyHandoff { get; private set; }

    public RebindableAreaAssemblySource? AreaAssemblySource { get; private set; }

    public int ExpectedBakeAreaId { get; private set; } = -1;

    public WorldEntryState WorldEntry { get; private set; } = null!;

    public KeepaliveDriver Keepalive { get; private set; } = null!;

    public TimedEventQueue TimedEventQueue { get; private set; } = null!;

    public void SetExpectedBakeArea(int areaId)
    {
        if (ExpectedBakeAreaId == areaId) return;
        GD.Print($"[ClientContext] SetExpectedBakeArea: {ExpectedBakeAreaId} → {areaId}. " +
                 "spec: assembly_graph.md §1/§4 (area rebind guard).");
        ExpectedBakeAreaId = areaId;
    }


    public override void _Ready()
    {
        BuildApplicationGraph();

        var audio = new AudioService { Name = "AudioService" };
        AddChild(audio);
        Audio = audio;
        GD.Print("[ClientContext] AudioService added as child node.");

        MaybeStartEnvLogin();
    }

    public override void _Process(double delta)
    {
        var keepalive = Keepalive;
        if (keepalive is null) return;

        var nowMs = (long)Time.GetTicksMsec();
        var inWorld = WorldEntry is { IsActive: true };

        if (inWorld && !keepalive.IsInWorld)
            FireAndForget(keepalive.OnWorldEnteredAsync(nowMs));
        else if (!inWorld && keepalive.IsInWorld)
            FireAndForget(keepalive.OnWorldExitedAsync());
        else
            FireAndForget(keepalive.Tick(nowMs));
    }

    private static async void FireAndForget(ValueTask sendTask)
    {
        try
        {
            await sendTask.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[ClientContext] keepalive send faulted: {ex.Message}");
        }
    }

    public override void _ExitTree()
    {
        EventBus?.Complete();

        HudEventHub?.Complete();

        if (_gameConnection is { } conn)
        {
            _gameConnection = null;
            conn.DisconnectAsync().AsTask().Wait(TimeSpan.FromSeconds(2));
        }

        Dispatcher?.Complete();

        _loopCts?.Cancel();
        try
        {
            _loopTask?.Wait(TimeSpan.FromSeconds(2));
            _inboundTask?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
        }

        DrainEnvLogin();

        _loopCts?.Dispose();
        _loopCts = null;
        _loopTask = null;
        _inboundTask = null;

        _catalogueLoader?.Dispose();
        _catalogueLoader = null;

        UiCatalogs?.Dispose();
        HudAtlas?.Dispose();
        HudText?.Dispose();
        _uiAssets?.Dispose();
        _uiAssets = null;

        BuffIconCatalog?.Dispose();

        _terrainVfs?.Dispose();
        _terrainVfs = null;

        _loadVfsPipeline?.Dispose();
        _loadVfsPipeline = null;

        GD.Print(
            "[ClientContext] EventBus completed. EngineLoop drained + stopped. CatalogueLoader + UiCatalogs + TerrainVfs disposed.");
    }

    public void SetWorldInputHandler(IInputHandler worldHandler)
    {
        _setWorldHandler?.Invoke(worldHandler);
    }

    public void SetHudHitTest(Func<int, int, bool> hitTest)
    {
        _hudInputHandler?.SetHitTest(hitTest);
    }
}