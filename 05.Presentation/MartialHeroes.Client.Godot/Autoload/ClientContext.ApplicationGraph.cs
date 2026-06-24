using Godot;
using MartialHeroes.Assets.Mapping;
using MartialHeroes.Client.Application.Assets;
using MartialHeroes.Client.Application.Contracts.Events;
using MartialHeroes.Client.Application.Contracts.Hud;
using MartialHeroes.Client.Application.Contracts.Scene;
using MartialHeroes.Client.Application.Diagnostics;
using MartialHeroes.Client.Application.Engine;
using MartialHeroes.Client.Application.Handlers;
using MartialHeroes.Client.Application.Ingestion;
using MartialHeroes.Client.Application.Input;
using MartialHeroes.Client.Application.Login;
using MartialHeroes.Client.Application.Net;
using MartialHeroes.Client.Application.UseCases;
using MartialHeroes.Client.Application.World;
using MartialHeroes.Client.Domain.Simulation.Simulation;
using MartialHeroes.Client.Godot.Adapters;
using MartialHeroes.Client.Godot.Composition;
using MartialHeroes.Client.Godot.Ui.Assets;
using MartialHeroes.Client.Infrastructure.Catalog;
using MartialHeroes.Client.Infrastructure.Lobby;
using MartialHeroes.Client.Presentation.Adapters;
using MartialHeroes.Client.Presentation.Input;
using MartialHeroes.Network.Abstractions.Protocol;
using MartialHeroes.Network.Abstractions.Session;
using MartialHeroes.Network.Crypto;
using MartialHeroes.Network.Protocol.Packets.Login.Packets;
using MartialHeroes.Network.Transport.Pipelines;

namespace MartialHeroes.Client.Godot.Autoload;

public sealed partial class ClientContext
{
    private void BuildApplicationGraph()
    {
        var bus = new ClientEventBus();

        var sceneMachine = new SceneStateMachine(bus);

        _loadVfsPipeline = MountLoadResourcePipeline();
        var loadOrchestrator = new LoadOrchestrator(
            sceneMachine,
            new VfsLoadResourceSource(_loadVfsPipeline),
            new OpeningSkipIniReader(ResolveOpeningSkipCfgPath()),
            new GodotLoadingSoundSink());

        var world = new ClientWorld();

        var worldEntry = new WorldEntryState();

        IUnhandledOpcodeSink opcodeSink = new CountingUnhandledOpcodeSink();

        var relaySink = new RelayOutboundPacketSink();
        _relaySink = relaySink;
        IOutboundPacketSink noopSink = relaySink;

        var sessionId = SessionId.None;

        var inFlightLatch = new InFlightLatch();

        var credentialStore = new LoginCredentialStore();

        ILoginHandshakeDriver loginDriver =
            new LoginHandshakeDriver(relaySink, credentialStore, sessionId);

        var hudHandler = new HudInputHandler();
        _hudInputHandler = hudHandler;

        var worldRelay = new RelayInputHandler();
        var inputBus = new InputBus(hudHandler, worldRelay);

        _catalogueLoader = BuildCatalogueLoader();

        var scrStatCatalogue = ScrStatCatalogue.FromLoader(_catalogueLoader);
        GD.Print(
            $"[ClientContext] ScrStatCatalogue loaded (HP curve entries={scrStatCatalogue.GetHpBaseCurve().Count}, " +
            $"MP curve entries={scrStatCatalogue.GetMpBaseCurve().Count}).");

        ItemCatalogue = ItemCatalogue.FromLoader(_catalogueLoader);
        SkillCatalogue = SkillCatalogue.FromLoader(_catalogueLoader);
        MobCatalogue = MobCatalogue.FromLoader(_catalogueLoader);
        GD.Print($"[ClientContext] Catalogues loaded: {ItemCatalogue.Count} items, " +
                 $"{SkillCatalogue.Count} skills, {MobCatalogue.Count} mobs.");

        _uiAssets = RealClientAssets.TryOpen();
        UiCatalogs = new UiCatalogs(_uiAssets);
        GD.Print($"[ClientContext] UiCatalogs: {UiCatalogs.UiTexEntryCount} uitex entries, " +
                 $"{UiCatalogs.MsgRecordCount} msg records.");

        HudAtlas = new HudAtlasLibrary(_uiAssets);
        HudText = new HudTextLibrary(_uiAssets);
        GD.Print("[ClientContext] HudAtlasLibrary + HudTextLibrary constructed (Ui/Scenes substrate).");

        var hudHub = new HudEventHub();
        HudEventHub = hudHub;
        GD.Print("[ClientContext] HudEventHub constructed (6 typed channels).");

        BuffIconCatalog = new BuffIconCatalog(_uiAssets);
        GD.Print($"[ClientContext] BuffIconCatalog initialised ({BuffIconCatalog.TableCount} entries).");

        ZoneCatalog = new ZoneCatalog(RealClientAssets.TryOpen());
        GD.Print($"[ClientContext] ZoneCatalog initialised ({ZoneCatalog.AllZones.Count} zone records.");

        var vfs = OpenVfsForTerrain();
        _terrainVfs = vfs;

        var regionSource = new VfsRegionSource(vfs);
        RegionService = new RegionService(regionSource, hudHub);
        GD.Print("[ClientContext] RegionService constructed. spec: Docs/RE/specs/world_systems.md Ch. 16.");

        try
        {
            var areaSource =
                new RebindableAreaAssemblySource(vfs, 0);
            AreaAssemblySource = areaSource;

            var areaComposer = new AreaComposer();

            CellAssemblyHandoff = new CellAssemblyHandoff(bus,
                (mapX, mapZ, _) =>
                {
                    var currentBakeArea = ExpectedBakeAreaId;
                    var sourceArea = areaSource.AreaId;
                    if (currentBakeArea >= 0 && sourceArea != currentBakeArea)
                    {
                        GD.Print($"[ClientContext] CellBake({mapX},{mapZ}): source area {sourceArea} ≠ " +
                                 $"expected {currentBakeArea} — stale streaming event dropped. " +
                                 "spec: assembly_graph.md §1/§4 (area-rebind race guard).");
                        return null;
                    }

                    try
                    {
                        var cell = areaComposer.ComposeCell(areaSource, mapX, mapZ);
                        return new AssembledCellViewAdapter(cell);
                    }
                    catch (Exception ex)
                    {
                        GD.PrintErr($"[ClientContext] AreaComposer.ComposeCell({mapX},{mapZ}) failed: {ex.Message}");
                        return null;
                    }
                });

            GD.Print(
                "[ClientContext] AreaComposer + CellAssemblyHandoff wired (RebindableAreaAssemblySource). spec: assembly_graph.md §1/§4.");

            AreaAssemblyHandoff = new AreaAssemblyHandoff(bus,
                areaId =>
                {
                    try
                    {
                        areaSource.SetArea(areaId);
                        var area = areaComposer.ComposeArea(areaSource);
                        return new AssembledAreaViewAdapter(area);
                    }
                    catch (Exception ex)
                    {
                        GD.PrintErr($"[ClientContext] AreaComposer.ComposeArea(area={areaId}) failed: {ex.Message}");
                        return null;
                    }
                });

            GD.Print("[ClientContext] AreaAssemblyHandoff wired (CYCLE 2 Phase 2-A.5). spec: assembly_graph.md §1/§4.");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[ClientContext] CellAssemblyHandoff wiring failed (non-fatal): {ex.Message}");
            CellAssemblyHandoff = null;
            AreaAssemblySource = null;
            AreaAssemblyHandoff = null;
        }

        var terrainSource = new VfsTerrainSectorSource(vfs, 0);

        var streamingService = new SectorStreamingService(terrainSource, bus, StreamQuality.High);

        var characterSelection = new CharacterSelectionStore();
        var accountCharacters = new AccountCharacterState();
        CharacterSelection = characterSelection;
        GD.Print("[ClientContext] CharacterSelectionStore + AccountCharacterState constructed and shared. " +
                 "spec: login_flow.md §3.5 / §5.2.");

        var enterWorldRelay = new RelayEnterWorldEmitter();
        var handler = new GamePacketHandler(world, bus, opcodeSink, loginDriver,
            characterSelection: characterSelection,
            accountCharacters: accountCharacters,
            sceneStateMachine: sceneMachine,
            inFlightLatch: inFlightLatch,
            worldEntry: worldEntry,
            enterWorldEmitter: enterWorldRelay.Invoke)
        {
            VitalsResolver = CatalogueVitalsResolver.Create(scrStatCatalogue)
        };

        var dispatcher = new InboundFrameDispatcher(handler);

        var clientDirForLobby = ClientPathResolver.ResolveClientDir();
        var lobbyHostResolver = new LobbyHostResolver(clientDirForLobby);
        var lobbyHost = lobbyHostResolver.Resolve();
        GD.Print($"[ClientContext] Lobby host resolved to '{lobbyHost}'. spec: login_flow.md §2.0.");

        var lobbyClient = new LobbyClient(lobbyHost, PayloadCompression.DecompressPayload);
        var lastServerStore = new RegistryLastServerStore();


        var sessionTokenBytes = new byte[ApplicationUseCases.SessionTokenLength];
        var integrityTokenSource = "absent";
        var clientDirForToken = ClientPathResolver.ResolveClientDir();
        if (clientDirForToken is not null)
        {
            var parentDir = Directory.GetParent(clientDirForToken)?.FullName;
            var candidates = parentDir is null
                ? new[] { Path.Combine(clientDirForToken, "doida.exe") }
                : new[] { Path.Combine(clientDirForToken, "doida.exe"), Path.Combine(parentDir, "doida.exe") };
            foreach (var candidate in candidates)
                if (File.Exists(candidate) &&
                    SessionTokenChecksum.TryWriteChecksumOf(candidate, sessionTokenBytes))
                {
                    integrityTokenSource = candidate;
                    break;
                }
        }

        GD.Print(integrityTokenSource == "absent"
            ? "[ClientContext] 1/9 build-integrity token: genuine doida.exe not found beside data.inf — 33B zero-filled. " +
              "spec: cmsg_char_enter.yaml; login_flow.md §3.3."
            : $"[ClientContext] 1/9 build-integrity token = lowercase-hex MD5 of '{integrityTokenSource}'. " +
              "spec: cmsg_char_enter.yaml; login_flow.md §3.3.");

        var gameVerVersionSource = GameVerClientVersionSource.Resolve(_uiAssets);

        var useCases = new ApplicationUseCases(noopSink, world, credentialStore, sessionId,
            sessionTokenBytes.AsSpan(), versionSource: gameVerVersionSource,
            eventBus: bus, lobbyClient: lobbyClient, lastServerStore: lastServerStore,
            characterSelection: characterSelection, inFlightLatch: inFlightLatch);
        GD.Print(
            $"[ClientContext] Version token derived: {ClientVersionToken.Derive(gameVerVersionSource.VersionField)}" +
            $" (= 10 × {gameVerVersionSource.VersionField} + 9; asset-sourced from game.ver). spec: login_flow.md §3.3 / §7.");

        enterWorldRelay.SetTarget(useCases.EmitEnterWorldRequest);
        GD.Print("[ClientContext] enterWorldRelay → useCases.EmitEnterWorldRequest wired. " +
                 "spec: frontend_scenes.md §7 (3/14 → 1/9 enter ladder seam).");

        Keepalive = new KeepaliveDriver(noopSink, sessionId, inFlightLatch);
        GD.Print("[ClientContext] KeepaliveDriver constructed (idle 2/10000 @20s + 2/112 world toggle). " +
                 "spec: world_entry.md §2.5/§3.2; net_contracts.md §2.15.");

        var engineLoop = new GameEngineLoop(world, bus, inputBus);

        var timedEventQueue = new TimedEventQueue();
        GD.Print("[ClientContext] TimedEventQueue constructed. spec: Docs/RE/specs/effect-scheduling.md §5A.");

        EventBus = bus;
        UseCases = useCases;
        Dispatcher = dispatcher;
        SceneMachine = sceneMachine;
        LoadOrchestrator = loadOrchestrator;
        InputBus = inputBus;
        EngineLoop = engineLoop;
        StreamingService = streamingService;
        WorldEntry = worldEntry;
        TimedEventQueue = timedEventQueue;

        _setWorldHandler = worldRelay.SetTarget;

        _loopCts = new CancellationTokenSource();
        var clock = engineLoop.CreateRealtimeClock();

        var rawLoop = engineLoop.RunAsync(clock, _loopCts.Token);
        _loopTask = rawLoop;

        _ = rawLoop.ContinueWith(
            t => GD.PrintErr($"[ClientContext] EngineLoop faulted: {t.Exception}"),
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);

        var inboundLoop = dispatcher.RunAsync(_loopCts.Token);
        _inboundTask = inboundLoop;
        _ = inboundLoop.ContinueWith(
            t => GD.PrintErr($"[ClientContext] InboundDispatcher faulted: {t.Exception}"),
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);

        GD.Print(
            "[ClientContext] Application graph constructed. EventBus ready. EngineLoop + inbound dispatcher started.");
    }
}