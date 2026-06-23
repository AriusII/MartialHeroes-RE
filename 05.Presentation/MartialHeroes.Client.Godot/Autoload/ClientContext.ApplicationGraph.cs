using System.Text;
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
using MartialHeroes.Network.Transport.Pipelines;
using Environment = System.Environment;

namespace MartialHeroes.Client.Godot.Autoload;

public sealed partial class ClientContext
{
    /// <summary>
    ///     Builds the full Application object graph.
    /// </summary>
    private void BuildApplicationGraph()
    {
        // 1. Event bus — bounded 1024 capacity, DropOldest backpressure.
        //    spec: ClientEventBus default policy.
        var bus = new ClientEventBus();

        // 2. Faithful 8-state scene machine — the sole state machine. Boots at state 0 (Init).
        //    spec: Docs/RE/specs/client_runtime.md §7.1.
        var sceneMachine = new SceneStateMachine(bus);

        // 2c. State-2 load orchestrator: engine-free load worker, OPENNING/SKIP reader, and
        //     loading cue sink. The presentation only observes progress/completion.
        //     spec: Docs/RE/specs/resource_pipeline.md §2; client_runtime.md §7.3.
        _loadVfsPipeline = MountLoadResourcePipeline();
        var loadOrchestrator = new LoadOrchestrator(
            sceneMachine,
            new VfsLoadResourceSource(_loadVfsPipeline),
            new OpeningSkipIniReader(ResolveOpeningSkipCfgPath()),
            new GodotLoadingSoundSink());

        // 3. Domain world registry.
        var world = new ClientWorld();

        // 3a. Durable 4/1 world-entry state: survives the transient InGameWorldBootstrappedEvent
        //     channel drain that happens while a front-end scene (LoginScene/LoadScene) is the active
        //     drainer — before the InGame GameLoop exists. The InGame _Ready recovery step reads this
        //     holder to cold-start the area even when the transient event was already consumed.
        //     spec: Docs/RE/specs/world_entry.md §2.3 / §3.1 — durable world-entry seam.
        var worldEntry = new WorldEntryState();

        // 4. Unhandled opcode sink — count-only for now.
        IUnhandledOpcodeSink opcodeSink = new CountingUnhandledOpcodeSink();

        // 5. Relay outbound sink: initially no-op; SetTarget wires the live crypto sink in
        //    OpenGameConnectionAsync once the TCP game connection is established.
        //    Both ApplicationUseCases and LoginHandshakeDriver share the SAME instance so the
        //    secure 1/4 reply goes to the real socket once the connection is open.
        var relaySink = new RelayOutboundPacketSink();
        _relaySink = relaySink;
        IOutboundPacketSink noopSink = relaySink; // alias — ApplicationUseCases receives the relay

        // 6. Session id — starts as None; updated when the game connection is opened.
        var sessionId = SessionId.None;

        // 6b. The single global in-flight latch — the ONLY pending-request primitive in the protocol.
        //     ARMED by the char-management send builders (1/6, 1/7, 1/9, 1/13, 1/14, 2/2) in
        //     ApplicationUseCases; CLEARED by the major-3 result ladder + 4/1 in GamePacketHandler; READ
        //     by KeepaliveDriver (suppress the idle 2/10000 while a request is outstanding) AND by the
        //     3/5 enter-ladder discriminator (an armed latch at 3/5 = a genuine 1/9 enter is pending →
        //     the load terminates at InGame; a disarmed latch = the unsolicited post-login 3/5 → load
        //     lands at Select). ONE shared instance across all three consumers.
        //     CYCLE-10 fix 1: previously this latch was never constructed (null everywhere) → the
        //     enter-ladder discrimination and the keepalive suppression were both inert.
        //     spec: Docs/RE/specs/net_contracts.md §1.3; world_entry.md §3.3; charselect.md §7.4.
        var inFlightLatch = new InFlightLatch();

        // 7. Login credential store.
        //    spec: Docs/RE/specs/crypto.md §6.1.
        var credentialStore = new LoginCredentialStore();

        // 8. Login handshake driver — answers the inbound 0/0 KeyExchange with the secure 1/4 Auth
        //    reply carrying the staged credential (account + optional PIN pre-image + RSA(M)). It shares
        //    the SAME relay sink as ApplicationUseCases; the reply reaches the live socket once
        //    OpenGameConnectionAsync installs the crypto sink. The credential store and session id are
        //    the same instances handed to ApplicationUseCases.
        //    spec: Docs/RE/specs/crypto.md §6.1/§6.6; packets/login.yaml (CmsgLoginCredential).
        ILoginHandshakeDriver loginDriver =
            new LoginHandshakeDriver(relaySink, credentialStore, sessionId);

        // 9. InputBus — UI handler first, world handler wired after InputRouter is created.
        //    The HudInputHandler starts as a pass-through (hitTest: null); the live GameHud.HitTest
        //    is wired later via SetHudHitTest() once GameLoop initialises the HUD node.
        //    F1 fix: store in _hudInputHandler so SetHudHitTest can reach it.
        //    spec: Docs/RE/specs/input_ui.md §3 / §6 (UI before world).
        var hudHandler = new HudInputHandler();
        _hudInputHandler = hudHandler; // F1: stored for late SetHitTest wiring. spec: input_ui.md §3/§6.

        // The world handler is created by InputRouter, so we build InputBus with only the
        // HUD handler for now; the world handler is appended via a late-binding relay.
        // spec: Docs/RE/specs/input_ui.md §3 — UI before world.
        var worldRelay = new RelayInputHandler(); // placeholder; InputRouter sets the target.
        var inputBus = new InputBus(hudHandler, worldRelay);

        // 10. VFS catalogue loader — resolved via ClientPathResolver (config file / env / auto-detect).
        //     Used for item/skill/mob/stat catalogues (displayed by HUD).
        //     spec: PRESERVATION_AND_ARCHITECTURE.md §Non-distribution rules (never hardcode path).
        _catalogueLoader = BuildCatalogueLoader();

        // 11. Real stat catalogue — from userlevel.scr via VfsCatalogueLoader.
        //     spec: Docs/RE/formats/config_tables.md §2.4 userlevel.scr.
        var scrStatCatalogue = ScrStatCatalogue.FromLoader(_catalogueLoader);
        GD.Print(
            $"[ClientContext] ScrStatCatalogue loaded (HP curve entries={scrStatCatalogue.GetHpBaseCurve().Count}, " +
            $"MP curve entries={scrStatCatalogue.GetMpBaseCurve().Count}).");

        // 12. Catalogue items / skills / mobs for UI display names (CP949).
        //     Items come from the runtime master data/script/items.scr (NOT items.csv).
        //     spec: Docs/RE/formats/items_scr.md §4 + config_tables.md §2.8 / §2.9.
        ItemCatalogue = ItemCatalogue.FromLoader(_catalogueLoader);
        SkillCatalogue = SkillCatalogue.FromLoader(_catalogueLoader);
        MobCatalogue = MobCatalogue.FromLoader(_catalogueLoader);
        GD.Print($"[ClientContext] Catalogues loaded: {ItemCatalogue.Count} items, " +
                 $"{SkillCatalogue.Count} skills, {MobCatalogue.Count} mobs.");

        // 13. UI data catalogs: uitex manifest (data/ui/UiTex.txt) and msg.xdb string catalog.
        //     Opens a second RealClientAssets handle; lazy texture loading stays on the main thread.
        //     spec: Docs/RE/formats/ui_manifests.md §1 / misc_data.md §6 / ui_system.md §8.5.
        _uiAssets = RealClientAssets.TryOpen();
        UiCatalogs = new UiCatalogs(_uiAssets);
        GD.Print($"[ClientContext] UiCatalogs: {UiCatalogs.UiTexEntryCount} uitex entries, " +
                 $"{UiCatalogs.MsgRecordCount} msg records.");

        // 13-B. HUD atlas + text libraries for the new Ui/Scenes substrate.
        //     Reuse the same _uiAssets handle; no additional VFS archive opened.
        //     spec: Docs/RE/formats/ui_manifests.md §1 / Docs/RE/formats/msg_xdb.md.
        HudAtlas = new HudAtlasLibrary(_uiAssets);
        HudText = new HudTextLibrary(_uiAssets);
        GD.Print("[ClientContext] HudAtlasLibrary + HudTextLibrary constructed (Ui/Scenes substrate).");

        // 16-A. HUD event hub: the single facade for all per-frame HUD channels.
        //     Constructed once here; widgets subscribe via IHudEventHub.
        //     spec: MartialHeroes.Client.Application.Hud — IHudEventHub / HudEventHub.
        var hudHub = new HudEventHub();
        HudEventHub = hudHub;
        GD.Print("[ClientContext] HudEventHub constructed (6 typed channels).");

        // 16-B. Buff icon catalog (stateicon.dds + buff_icon_position.xdb).
        //     spec: Docs/RE/formats/misc_data.md §1.3 / §1.6.
        BuffIconCatalog = new BuffIconCatalog(_uiAssets);
        GD.Print($"[ClientContext] BuffIconCatalog initialised ({BuffIconCatalog.TableCount} entries).");

        // 16-C. Zone catalog (mapsetting.scr — 52 zone records, CP949 names).
        //     spec: Docs/RE/formats/misc_data.md §7.1 / Docs/RE/specs/minimap.md §6.3.
        ZoneCatalog = new ZoneCatalog(RealClientAssets.TryOpen());
        GD.Print($"[ClientContext] ZoneCatalog initialised ({ZoneCatalog.AllZones.Count} zone records.");

        // 16. VFS archive for terrain (separate from catalogue loader — same paths).
        //     spec: Docs/RE/formats/terrain.md §1.2 / §1.3.
        //     Store in _terrainVfs so _ExitTree can dispose it (VfsTerrainSectorSource does not own/dispose).
        var vfs = OpenVfsForTerrain();
        _terrainVfs = vfs;

        // 16-D. Region service (Ch. 16 — 256-unit PvP/safe/closed zone grid).
        //     spec: Docs/RE/specs/world_systems.md Ch. 16.
        var regionSource = new VfsRegionSource(vfs);
        RegionService = new RegionService(regionSource, hudHub);
        GD.Print("[ClientContext] RegionService constructed. spec: Docs/RE/specs/world_systems.md Ch. 16.");

        // 16-E. AreaComposer + CellAssemblyHandoff — Phase 6a composition seam.
        //   The AreaComposer lives in layer-03 (Assets.Mapping); the CellAssemblyHandoff is the
        //   layer-04 seam that bridges SectorLoadedEvent → CellAssembledEvent. The bake callback
        //   captures the VfsAreaAssemblySource (a layer-05 adapter) and calls AreaComposer.ComposeCell.
        //   The resulting AssembledCell is wrapped in a thin IAssembledCellView adapter so the layer-04
        //   CellAssemblyHandoff can publish it without importing Assets.Mapping.
        //   spec: Docs/RE/specs/assembly_graph.md §1/§4 — AreaComposer + CellAssemblyHandoff contract.
        try
        {
            // Phase 2-B.1 fix: use RebindableAreaAssemblySource (starts at area 0; rebound to
            // the configured area by RealWorldRenderer.TriggerTerrainStreaming via
            // ctx.AreaAssemblySource.SetArea(TargetAreaId) before streaming starts).
            // This ensures AreaComposer.ComposeCell builds data/map<NNN>/dat/... paths for the
            // ACTIVE area rather than always using area 0 (map000).
            // spec: Docs/RE/specs/assembly_graph.md §1/§4 — IAreaAssemblySource drives cell paths.
            // spec: Docs/RE/formats/terrain.md §1.1 — areaId digit decomposition.
            var areaSource =
                new RebindableAreaAssemblySource(vfs, 0);
            AreaAssemblySource = areaSource; // expose for RealWorldRenderer to call SetArea.

            var areaComposer = new AreaComposer();

            CellAssemblyHandoff = new CellAssemblyHandoff(bus,
                (mapX, mapZ, _) =>
                {
                    // AREA-REBIND STREAMING RACE GUARD (CYCLE 6 Lane D fix):
                    //
                    // SectorLoadedEvent can be published into the ClientEventBus by an OLD streaming
                    // task (fired for the previous config-area in TriggerTerrainStreaming) while the
                    // source has ALREADY been rebound to the NEW server area (by a second call to
                    // TriggerTerrainStreaming from OnWorldEntered). When _Process drains those stale
                    // events, areaSource.AreaId == newArea but the cell coordinates belong to the old
                    // area → AreaComposer builds data/map<newArea>/dat/d<newArea>x<oldX>z<oldZ>.*
                    // paths that do not exist → open fails → ComposeCell returns empty → IsResolved=false
                    // → ~36% cell miss.
                    //
                    // Fix: compare areaSource.AreaId against ExpectedBakeAreaId at drain time.
                    // ExpectedBakeAreaId is stamped by SetExpectedBakeArea(TargetAreaId) from
                    // TriggerTerrainStreaming alongside areaSource.SetArea(TargetAreaId).
                    // Both writes and reads are on the Godot main thread (no lock needed).
                    //
                    // A stale event (from the OLD streaming session) is identified by the fact that
                    // its cell coordinates are NOT in the current area's .lst — but we cannot check
                    // that cheaply here. Instead we gate on ExpectedBakeAreaId: if the source was
                    // rebound (areaSource.AreaId == currentArea) but the event was produced BEFORE
                    // the rebind, it is stale. The guard is conservative: it drops ANY bake call
                    // whose source area does not match the expected area. Since SetExpectedBakeArea
                    // and areaSource.SetArea are always called together (TriggerTerrainStreaming),
                    // a valid event always has areaSource.AreaId == ExpectedBakeAreaId.
                    //
                    // spec: Docs/RE/specs/assembly_graph.md §1/§4 — IAreaAssemblySource drives paths.
                    // spec: Docs/RE/formats/area_inventory.md §1A — membership gate before cell stream.
                    var currentBakeArea = ExpectedBakeAreaId;
                    var sourceArea = areaSource.AreaId;
                    if (currentBakeArea >= 0 && sourceArea != currentBakeArea)
                    {
                        // Stale event from a superseded streaming session — drop silently.
                        // This can happen when: (a) OnWorldEntered rebinds the source to serverArea
                        // while events from the configArea streaming task are still in the channel,
                        // OR (b) the source was rebound but ExpectedBakeAreaId was not yet stamped
                        // (impossible since both happen together in TriggerTerrainStreaming).
                        GD.Print($"[ClientContext] CellBake({mapX},{mapZ}): source area {sourceArea} ≠ " +
                                 $"expected {currentBakeArea} — stale streaming event dropped. " +
                                 "spec: assembly_graph.md §1/§4 (area-rebind race guard).");
                        return null;
                    }

                    // Payload bytes from SectorLoadedEvent are the .ted bytes already loaded by
                    // VfsTerrainSectorSource. The AreaComposer re-reads all cell files via the
                    // RebindableAreaAssemblySource (which has full VFS access and is rebound to the
                    // active area before streaming starts). The payload is unused here.
                    // spec: Docs/RE/specs/assembly_graph.md §4 — bake callback is pure/deterministic.
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

            // CYCLE 2 Phase 2-A.5 — AreaAssemblyHandoff.
            // Binds the area bake to AreaComposer.ComposeArea(areaSource) and wraps the result in
            // AssembledAreaViewAdapter so the layer-04 seam never references Assets.Mapping.
            // AreaAssemblyHandoff.OnAreaBound is called from RealWorldRenderer.TriggerTerrainStreaming
            // alongside SetArea() — exactly once per area-enter (idempotent on re-bind).
            // The bake callback re-uses the SAME areaComposer + areaSource instances already captured
            // by the CellBake delegate above, so no second VFS handle is opened.
            // spec: Docs/RE/specs/assembly_graph.md §1 (Phase A — area load → spawns) / §4.
            AreaAssemblyHandoff = new AreaAssemblyHandoff(bus,
                areaId =>
                {
                    try
                    {
                        // Ensure the assembly source is bound to this area before composing.
                        // RealWorldRenderer.TriggerTerrainStreaming also calls areaSource.SetArea,
                        // but we defensively call it here too so the bake is always area-coherent.
                        // spec: Docs/RE/formats/terrain.md §1.1 (per-area path tag). CONFIRMED.
                        areaSource.SetArea(areaId);
                        var area = areaComposer.ComposeArea(areaSource);
                        return new AssembledAreaViewAdapter(area);
                    }
                    catch (Exception ex)
                    {
                        GD.PrintErr($"[ClientContext] AreaComposer.ComposeArea(area={areaId}) failed: {ex.Message}");
                        return null; // unresolved area → nothing published. spec: assembly_graph.md §1.
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

        // 17. Terrain sector source — backed by VFS.
        //     spec: Docs/RE/formats/terrain.md §1.2 / §1.3.
        //     Area 0 is the default starting area. TODO: update on enter-game.
        var terrainSource = new VfsTerrainSectorSource(vfs, 0);

        // 18. Terrain streaming service — high quality (5×5 ring, radius=2).
        //     spec: Docs/RE/formats/terrain.md §12.2 — High quality → 5×5 ring (ring-radius cells = 5).
        //     The 5×5 ring loads up to 25 sectors around the player, covering all spawn-filled cells
        //     in large walled areas (e.g. area 2) that exceed the 3×3 ring footprint.
        var streamingService = new SectorStreamingService(terrainSource, bus, StreamQuality.High);

        // 17. Character-selection stores — shared between GamePacketHandler (fills on 3/1) and
        //     ApplicationUseCases (reads on SelectCharacterAsync / 3/14 spawn seam).
        //     spec: Docs/RE/specs/login_flow.md §3.5 — "caches the chosen slot's record locally … consumed on 3/14".
        //     spec: Docs/RE/specs/login_flow.md §5.2 — AccountCharacterState seeds from 3/5 char-count field.
        var characterSelection = new CharacterSelectionStore();
        var accountCharacters = new AccountCharacterState();
        CharacterSelection = characterSelection; // shared store (filled on 3/1, read on SelectCharacterAsync)
        GD.Print("[ClientContext] CharacterSelectionStore + AccountCharacterState constructed and shared. " +
                 "spec: login_flow.md §3.5 / §5.2.");

        // 17b. Packet handler — orchestrates Domain mutation and event publishing.
        //     Wire the catalogue vitals resolver (real stat curves) at construction.
        //     spec: CatalogueVitalsResolver.Create — builds the seam from the catalogue.
        //     spec: Docs/RE/formats/config_tables.md §2.4.
        //
        //     ENTER-WORLD EMITTER RELAY (CYCLE 12 Phase 2):
        //     ApplicationUseCases is not yet constructed at this point (ordering constraint), so we
        //     cannot pass useCases.EmitEnterWorldRequest directly.  Instead we create a deferred-relay
        //     whose target is set after useCases is constructed — the same pattern as the
        //     RelayInputHandler / _setWorldHandler wiring above.  The relay's Invoke delegate is passed
        //     now; SetTarget is called below once useCases exists.
        //     spec: Docs/RE/specs/frontend_scenes.md §7 (1/9 emitted from the 3/14 handler).
        var enterWorldRelay = new RelayEnterWorldEmitter();
        var handler = new GamePacketHandler(world, bus, opcodeSink, loginDriver,
            characterSelection: characterSelection,
            accountCharacters: accountCharacters,
            sceneStateMachine: sceneMachine,
            inFlightLatch: inFlightLatch, // CYCLE-10 fix 1: clears on 3/x results + 4/1; read by 3/5 discriminator + keepalive.
            worldEntry: worldEntry,
            enterWorldEmitter: enterWorldRelay.Invoke)
        {
            VitalsResolver = CatalogueVitalsResolver.Create(scrStatCatalogue)
        };

        // 18. Inbound frame dispatcher — channel-backed; the live-socket frame sink feeds this.
        var dispatcher = new InboundFrameDispatcher(handler);

        // 19. Lobby stack: host resolver → lobby client + last-server store.
        //     These are constructed here (online mode) so that FetchServerListAsync / SelectServerAsync
        //     work in ApplicationUseCases.  All three degrade gracefully when the client dir is absent.
        //     spec: Docs/RE/specs/login_flow.md §2.0 — three-tier host resolution (ip.txt → list.dat → fallback).
        var clientDirForLobby = ClientPathResolver.ResolveClientDir();
        var lobbyHostResolver = new LobbyHostResolver(clientDirForLobby);
        var lobbyHost = lobbyHostResolver.Resolve();
        GD.Print($"[ClientContext] Lobby host resolved to '{lobbyHost}'. spec: login_flow.md §2.0.");

        var lobbyClient = new LobbyClient(lobbyHost, PayloadCompression.DecompressPayload);
        var lastServerStore = new RegistryLastServerStore();

        // 19b. SessionToken — resolve the 33-byte launcher/session token for payload +0x01 of 1/9.
        //
        //     Priority (first non-empty string wins):
        //       a) env MH_SESSION_TOKEN  — explicit override (env-gated verification harness).
        //          Use System.Environment (NOT Godot.OS — project idiom: global::Godot.* for Godot
        //          statics; System.Environment for OS env).
        //       b) clientdata/session_token.txt (first non-empty trimmed line) — gitignored by the
        //          clientdata/ blanket .gitignore which already ignores everything except .gitignore
        //          and README.md; no extra gitignore rule needed.
        //       c) absent → zero-filled 33-byte buffer. No fabricated demo value. When no real token
        //          is supplied the field stays zero, which is acceptable for the interactive UI login
        //          path where the maintainer has not yet obtained a launcher token.
        //
        //     The resolved string is ASCII-encoded (spec: SessionToken is an asciiz within 33 bytes),
        //     copied up to 32 bytes into a zero-initialised 33-byte array (byte[32] stays NUL).
        //     This 33-byte span is passed as `versionToken:` (back-compat param name) so
        //     ApplicationUseCases stamps it at payload +0x01.
        //     spec: Docs/RE/packets/cmsg_char_enter.yaml (SessionToken @0x01, 33 bytes).
        //     spec: Docs/RE/specs/login_flow.md §3.3.
        //
        //     NOTE: the u32 VersionToken at payload +0x24 is derived independently from the
        //     asset-sourced game.ver field5 (10×field5+9) — see GameVerClientVersionSource.Resolve below.
        //     Passing a non-empty sessionToken span does NOT disturb the +0x24 derivation — see
        //     ApplicationUseCases ctor: _versionToken is derived from versionSource unconditionally.
        //     spec: Docs/RE/packets/cmsg_char_enter.yaml (VersionToken @0x24); login_flow.md §7.

        string? sessionTokenStr = null;
        string sessionTokenSource;

        // a) env MH_SESSION_TOKEN — env-gated verification harness (System.Environment, not Godot.OS).
        // spec: login_flow.md §3.3; cmsg_char_enter.yaml (SessionToken @0x01, 33 bytes).
        var envToken = Environment.GetEnvironmentVariable("MH_SESSION_TOKEN");
        if (!string.IsNullOrWhiteSpace(envToken))
        {
            sessionTokenStr = envToken.Trim();
            sessionTokenSource = "env:MH_SESSION_TOKEN";
        }
        else
        {
            // b) clientdata/session_token.txt — resolved via ClientPathResolver.ResolveClientDir().
            //    The clientdata/ blanket .gitignore already ignores this file; no extra rule needed.
            var clientDirForToken = ClientPathResolver.ResolveClientDir();
            if (clientDirForToken is not null)
            {
                var tokenFilePath = Path.Combine(clientDirForToken, "session_token.txt");
                try
                {
                    if (File.Exists(tokenFilePath))
                        foreach (var rawLine in File.ReadLines(tokenFilePath))
                        {
                            var trimmed = rawLine.Trim();
                            if (trimmed.Length > 0)
                            {
                                sessionTokenStr = trimmed;
                                break;
                            }
                        }
                }
                catch (Exception ex)
                {
                    GD.PrintErr($"[ClientContext] session_token.txt read failed: {ex.Message}");
                }
            }

            sessionTokenSource = sessionTokenStr is not null ? "file:session_token.txt" : "absent";
        }

        // Build the 33-byte buffer: ASCII-encode if a token was found, else stay zero-filled.
        // spec: Docs/RE/packets/cmsg_char_enter.yaml (SessionToken @0x01, 33 bytes, asciiz).
        var sessionTokenBytes = new byte[ApplicationUseCases.SessionTokenLength]; // 33 bytes, zero-init
        if (sessionTokenStr is not null)
        {
            var encodedToken = Encoding.ASCII.GetBytes(sessionTokenStr);
            var copyLen = Math.Min(encodedToken.Length, ApplicationUseCases.SessionTokenLength - 1);
            encodedToken.AsSpan(0, copyLen).CopyTo(sessionTokenBytes);
        }

        // Log source and length only. Never print the raw token value (even from env/file).
        // spec: Docs/RE/specs/login_flow.md §3.3 — token is a launcher identity, not a secret per
        // se, but the env-harness convention is: log "present/absent + length", not the raw value.
        GD.Print(sessionTokenSource == "absent"
            ? "[ClientContext] SessionToken wired (source=absent, zero-filled 33B — supply MH_SESSION_TOKEN or session_token.txt for a real token). " +
              "spec: cmsg_char_enter.yaml; login_flow.md §3.3."
            : $"[ClientContext] SessionToken wired (source={sessionTokenSource}, len={sessionTokenStr!.Length}, 33B). " +
              "spec: cmsg_char_enter.yaml; login_flow.md §3.3.");

        // 19c. Version source — read the REAL data/cursor/game.ver via the VFS (CYCLE-10 fix 2).
        //      Replaces the prior `versionSource: null → DefaultClientVersionSource` (constant 2114),
        //      which was correct only by coincidence. The binary loads game.ver at enter time and derives
        //      the 1/9 token as 10×field5+9; we source field5 from the asset and fall back to the default
        //      (logged) only when the file is absent/unreadable. spec: login_flow.md §3.3 / §7; game_ver.md.
        var gameVerVersionSource = GameVerClientVersionSource.Resolve(_uiAssets);

        // 20. Use-case facade — presentation calls these for input intents.
        //     versionToken: the resolved 33-byte SessionToken span → ApplicationUseCases stamps it at
        //     payload +0x01 of 1/9. spec: Docs/RE/packets/cmsg_char_enter.yaml (SessionToken @0x01).
        //     versionSource: the asset-sourced game.ver field5 → token 10×field5+9 at payload +0x24.
        //     spec: Docs/RE/specs/login_flow.md §3.3 / §7.
        //     inFlightLatch: the shared single latch (armed by the char-mgmt sends here, cleared by the
        //     handler's result ladder, read by the 3/5 discriminator + keepalive). spec: net_contracts.md §1.3.
        //     eventBus, lobbyClient, lastServerStore wired so FetchServerListAsync / SelectServerAsync
        //     publish ServerListReceivedEvent and persist Lastserver.
        //     characterSelection: same instance as the handler's store (shared cache).
        //     spec: Docs/RE/specs/login_flow.md §3.5 / §5.2.
        var useCases = new ApplicationUseCases(noopSink, world, credentialStore, sessionId,
            sessionTokenBytes.AsSpan(), versionSource: gameVerVersionSource, sceneStateMachine: sceneMachine,
            eventBus: bus, lobbyClient: lobbyClient, lastServerStore: lastServerStore,
            characterSelection: characterSelection, inFlightLatch: inFlightLatch);
        GD.Print(
            $"[ClientContext] Version token derived: {ClientVersionToken.Derive(gameVerVersionSource.VersionField)}" +
            $" (= 10 × {gameVerVersionSource.VersionField} + 9; asset-sourced from game.ver). spec: login_flow.md §3.3 / §7.");

        // CYCLE 12 Phase 2 — wire the deferred enter-world emitter relay now that useCases exists.
        // The relay was passed to GamePacketHandler at construction (above); pointing it here at the
        // real emitter completes the 3/14 → 1/9 enter ladder seam without violating ordering.
        // spec: Docs/RE/specs/frontend_scenes.md §7 (1/9 emitted from the 3/14 handler).
        enterWorldRelay.SetTarget(useCases.EmitEnterWorldRequest);
        GD.Print("[ClientContext] enterWorldRelay → useCases.EmitEnterWorldRequest wired. " +
                 "spec: frontend_scenes.md §7 (3/14 → 1/9 enter ladder seam).");

        // 20b. Keepalive driver (CYCLE-10 fix 1) — the in-session heartbeat that keeps the LIVE link alive.
        //      Shares the relay sink (so 2/10000 / 2/112 reach the live socket) and the single in-flight
        //      latch (suppress the idle heartbeat while a char-mgmt request is outstanding). It is ticked
        //      from ClientContext._Process with a wall-clock ms stamp and armed/disarmed on world enter/exit
        //      (polled via WorldEntry.IsActive). Without this the client sent NO idle heartbeat after
        //      enter-world and was at risk of a server-side timeout drop.
        //      spec: Docs/RE/specs/world_entry.md §2.5 / §3.2; net_contracts.md §1.3 / §2.15.
        Keepalive = new KeepaliveDriver(noopSink, sessionId, inFlightLatch);
        GD.Print("[ClientContext] KeepaliveDriver constructed (idle 2/10000 @20s + 2/112 world toggle). " +
                 "spec: world_entry.md §2.5/§3.2; net_contracts.md §2.15.");

        // 21. Fixed-tick GameEngineLoop — 30 Hz.
        //     spec: Docs/RE/specs/game_loop.md §6 ("e.g. 30 Hz via a PeriodicTimer"). CONFIRMED.
        var engineLoop = new GameEngineLoop(world, bus, inputBus);

        // 21b. Timed-event queue — the universal "10001" deferred event queue. Engine-free; drained
        //      per-frame in GameLoop._Process and flushed in InGameScene._ExitTree on world-leave.
        //      spec: Docs/RE/specs/effect-scheduling.md §5A (two-pass full-tree sweep; FlushOnSceneTransition).
        var timedEventQueue = new TimedEventQueue();
        GD.Print("[ClientContext] TimedEventQueue constructed. spec: Docs/RE/specs/effect-scheduling.md §5A.");

        // Publish to fields.
        EventBus = bus;
        UseCases = useCases;
        Dispatcher = dispatcher;
        SceneMachine = sceneMachine;
        LoadOrchestrator = loadOrchestrator;
        InputBus = inputBus;
        EngineLoop = engineLoop;
        StreamingService = streamingService;
        WorldEntry = worldEntry; // spec: Docs/RE/specs/world_entry.md §2.3 / §3.1 — durable seam.
        TimedEventQueue = timedEventQueue; // spec: Docs/RE/specs/effect-scheduling.md §5A.

        // Store the relay setter so InputRouter can set the real handler later.
        _setWorldHandler = worldRelay.SetTarget;

        // Start the fixed-tick loop on a background task.
        // spec: Docs/RE/specs/game_loop.md §6 — "Fixed-rate logic tick … via a PeriodicTimer".
        _loopCts = new CancellationTokenSource();
        var clock = engineLoop.CreateRealtimeClock();

        // Store the RAW loop task so _ExitTree can drain it (wait for the loop to actually exit)
        // before disposing the CancellationTokenSource — avoiding a use-after-free of _loopCts.
        var rawLoop = engineLoop.RunAsync(clock, _loopCts.Token);
        _loopTask = rawLoop;

        // Observe background faults so they do NOT silently kill the process. The continuation runs
        // only if the loop faults, logs, and does NOT rethrow (fire-and-forget; not the drain handle).
        _ = rawLoop.ContinueWith(
            t => GD.PrintErr($"[ClientContext] EngineLoop faulted: {t.Exception}"),
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);

        // Start the inbound frame dispatcher's reader loop on a background task. WITHOUT this, frames
        // enqueued by DispatcherFrameSink (the live socket) are never
        // drained/routed — so the 0/0 KeyExchange never reaches the handler and the 1/4 auth reply is
        // never sent (login could never complete). Shares _loopCts so _ExitTree cancels + drains it
        // alongside the engine loop. spec: Docs/RE/specs/network_dispatch.md §1/§3 — a single
        // recv-consumer reader routes each inbound frame through the dispatch tables.
        var inboundLoop = dispatcher.RunAsync(_loopCts.Token);
        _inboundTask = inboundLoop;
        _ = inboundLoop.ContinueWith(
            t => GD.PrintErr($"[ClientContext] InboundDispatcher faulted: {t.Exception}"),
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);

        GD.Print(
            "[ClientContext] Application graph constructed. EventBus ready. EngineLoop + inbound dispatcher started.");
    }
}

// Internal adapter types used by BuildApplicationGraph and OpenGameConnectionAsync are
// defined in ClientContext.InternalAdapters.cs (same assembly, global namespace).