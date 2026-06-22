// World/RealWorldRenderer.Streaming.cs
//
// Multi-sector terrain streaming: initial ring load + area rebind + cancel/reset logic.
// Part of the RealWorldRenderer partial class split.
//
// spec: Docs/RE/specs/world_systems.md §13.1 — per-frame streaming copies the camera frustum into the
//   terrain manager and clamps the stream radius to WorldSceneContract.StreamRadiusFarPlaneClamp (1000)
//   at the far plane. All candidate stream radii derived from the camera far-plane are passed through
//   WorldSceneContract.ClampStreamRadius(candidate) before being applied.

using Godot;
using MartialHeroes.Client.Application.World;
using MartialHeroes.Client.Godot.Autoload;

namespace MartialHeroes.Client.Godot.World;

public sealed partial class RealWorldRenderer
{
    /// <summary>
    ///     Calls <see cref="SectorStreamingService.UpdateCenterAsync" /> for the initial spawn-anchor
    ///     cell and arms the player-following streaming loop.
    ///     Boot behaviour: the initial anchor is the spawn-density peak for the server-supplied area
    ///     (resolved by <see cref="TryApplySpawnCell" /> / <see cref="ResolveTargetCellForServerArea" />
    ///     in <see cref="OnWorldEntered" /> before this method is called — the world build is driven by
    ///     the server 4/1, not by an offline config area).
    ///     Follow behaviour: after boot, <see cref="_Process" /> checks the player position each frame
    ///     and recenter the streaming ring whenever the player moves ≥ <see cref="HysteresisThresholdCells" />
    ///     cells (Chebyshev) from <see cref="_streamAnchor" />. Each recenter calls
    ///     <see cref="SectorStreamingService.UpdateCenterAsync" /> which also evicts sectors that drifted
    ///     more than 2 cells from the new anchor (Chebyshev eviction).
    ///     spec: Docs/RE/formats/terrain.md §12.2 — "5×5 ring (High quality) of sectors centred on the player cell".
    ///     CONFIRMED.
    ///     spec: Docs/RE/specs/resource_pipeline.md §4.3 — streamer thread follows the player: CODE-CONFIRMED.
    ///     spec: Docs/RE/formats/terrain.md §9.3 — eviction at Chebyshev distance > 2: CONFIRMED.
    /// </summary>
    private void TriggerTerrainStreaming(ClientContext ctx)
    {
        // ── CYCLE 6 Lane D: area-rebind streaming race fix ──────────────────────
        //
        // Cancel the PREVIOUS streaming task before rebinding the sources.
        //
        // Problem: Initialise fires a Task.Run(UpdateCenterAsync(configArea)) then immediately
        // returns. OnWorldEntered then calls TriggerTerrainStreaming again for the serverArea.
        // The OLD task is still running on the thread pool; it has already published (or will
        // publish) SectorLoadedEvents for configArea cells into the ClientEventBus channel.
        // When _Process drains those events, areaSource.AreaId == serverArea → ComposeCell builds
        // data/map<serverArea>/dat/d<serverArea>x<configArea_mapX>z<configArea_mapZ>... (missing)
        // → empty cell → IsResolved=false → the ~36% cell miss.
        //
        // Fix part (a): cancel the old streaming task so it stops publishing NEW SectorLoadedEvents.
        //   - _streamingCts is a LinkedTokenSource: cancelling it also cancels any UpdateCenterAsync
        //     call using that token; the background thread-pool thread exits cleanly via
        //     OperationCanceledException from _source.LoadSectorAsync.
        //   - Already-published events may still be in the channel (see fix part b below).
        //
        // spec: Docs/RE/formats/terrain.md §12.2 (streaming ring). CONFIRMED.
        // spec: Docs/RE/specs/assembly_graph.md §1/§4 (area rebind before streaming starts). CONFIRMED.
        if (_streamingCts is not null)
        {
            try
            {
                _streamingCts.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }

            try
            {
                _streamingCts.Dispose();
            }
            catch (ObjectDisposedException)
            {
            }

            _streamingCts = null;
            GD.Print($"[RealWorldRenderer] TriggerTerrainStreaming: previous streaming task cancelled " +
                     $"(area rebind to {TargetAreaId}). spec: assembly_graph.md §1 (race fix lane D).");
        }

        // Create a new per-call CTS linked to _lifetimeCts.
        // This token is passed to UpdateCenterAsync AND the player-follow recenter tasks.
        // Cancelling _streamingCts stops THIS area's streaming; cancelling _lifetimeCts (_ExitTree)
        // stops ALL streaming regardless of which CTS the task holds.
        _streamingCts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token);
        var streamingToken = _streamingCts.Token;

        // Point BOTH streaming sources at the resolved area BEFORE streaming starts:
        //   (a) StreamingService.SetArea — rebinds VfsTerrainSectorSource to the correct .ted paths.
        //   (b) AreaAssemblySource.SetArea — rebinds RebindableAreaAssemblySource so the
        //       CellAssemblyHandoff bake lambda calls AreaComposer.ComposeCell with the correct
        //       data/map<NNN>/dat/... paths (Phase 2-B.1 fix: was hard-coded to area 0 → .map open
        //       failed for every non-zero area → ComposeCell early-exit → zero geometry).
        // Both must be rebound before UpdateCenterAsync fires SectorLoadedEvent callbacks.
        // spec: Docs/RE/formats/terrain.md §1.1 (per-area path tag) + §1.2 (per-area manifest).
        // spec: Docs/RE/specs/assembly_graph.md §1/§4 — IAreaAssemblySource drives AreaComposer paths.
        ctx.StreamingService.SetArea(TargetAreaId);
        ctx.AreaAssemblySource?.SetArea(TargetAreaId);

        // ── CYCLE 6 Lane D: area-rebind streaming race fix part (b) ────────────
        //
        // Stamp ExpectedBakeAreaId so the CellBake lambda drops stale events that were already
        // published into the ClientEventBus by the OLD streaming task before we cancelled it.
        //
        // Events already in the channel (produced by the configArea task) will be drained in _Process
        // AFTER this rebind. At drain time areaSource.AreaId == serverArea (rebound just above).
        // Without the guard, ComposeCell would use the server-area source with configArea coords →
        // path miss. The guard reads ExpectedBakeAreaId vs areaSource.AreaId at drain time and
        // drops any event where they differ — making the race observable only as a "stale event
        // dropped" log line rather than an empty-cell silently written into the _composedCells cache.
        //
        // spec: Docs/RE/specs/assembly_graph.md §1/§4 (IAreaAssemblySource drives AreaComposer paths).
        // spec: Docs/RE/formats/area_inventory.md §1A (membership gate before any cell streams).
        ctx.SetExpectedBakeArea(TargetAreaId);

        // CYCLE 2 Phase 2-A.5: compose + publish the area ONCE per area-enter.
        // OnAreaBound is idempotent (no-op if called again with the same area id).
        // This closes the gap where AreaAssembledEvent was never published and
        // RealWorldRenderer.OnAreaAssembled never fired.
        // spec: Docs/RE/specs/assembly_graph.md §1 (Phase A — area load → spawns) / §4.
        ctx.AreaAssemblyHandoff?.OnAreaBound(TargetAreaId);

        // Clamp the per-frame camera-frustum stream radius to the contract far-plane ceiling.
        // The camera frustum far-plane distance is copied into the terrain manager each frame;
        // the effective stream radius is never larger than WorldSceneContract.StreamRadiusFarPlaneClamp.
        // spec: Docs/RE/specs/world_systems.md §13.1 — stream radius clamped to 1000 at the far plane.
        // The camera far-plane is 15 000 wu (camera_movement.md §A.7); the clamped stream radius is 1000.
        const float CameraFarPlaneWu = 15_000f; // spec: Docs/RE/specs/camera_movement.md §A.7
        var candidateStreamRadius = CameraFarPlaneWu;
        var effectiveStreamRadius = WorldSceneContract.ClampStreamRadius(candidateStreamRadius);
        GD.Print($"[RealWorldRenderer] Stream radius: candidate={candidateStreamRadius:F0} wu, " +
                 $"effective (clamped)={effectiveStreamRadius:F0} wu. " +
                 "spec: Docs/RE/specs/world_systems.md §13.1 (WorldSceneContract.ClampStreamRadius).");

        // Initialise the streaming anchor to the resolved spawn-density peak.
        // _Process will update this as the player moves.
        // spec: Docs/RE/formats/terrain.md §Overview — origin bias 10000, cell size 1024. CONFIRMED.
        _streamAnchor = (TargetMapX, TargetMapZ);

        // Launch the initial ring load on the thread pool using the per-call token.
        // spec: Docs/RE/formats/terrain.md §12.2 — 5×5 ring (StreamQuality.High). CONFIRMED.
        _ = Task.Run(async () =>
        {
            try
            {
                await ctx.StreamingService.UpdateCenterAsync(TargetMapX, TargetMapZ, streamingToken)
                    .ConfigureAwait(false);

                // Skip the completion print if this streaming session was cancelled (superseded
                // by a newer TriggerTerrainStreaming call) or the node left the tree.
                if (streamingToken.IsCancellationRequested) return;

                var residentCount = ctx.StreamingService.ResidentCount;
                GD.Print($"[RealWorldRenderer] 5×5 terrain ring streaming complete " +
                         $"(area {TargetAreaId}, resident={residentCount} sectors).");
            }
            catch (OperationCanceledException)
            {
                // Expected: (a) node left the tree (_lifetimeCts); (b) area was superseded
                // (TriggerTerrainStreaming cancelled _streamingCts). Silent.
            }
            catch (Exception ex)
            {
                if (!streamingToken.IsCancellationRequested)
                    GD.PrintErr($"[RealWorldRenderer] Terrain streaming error: {ex.Message}");
            }
        }, streamingToken);

        // Arm the follow-streaming loop (enables the _Process recenter checks).
        // Done AFTER _streamAnchor is set so _Process never sees a zero anchor.
        // spec: Docs/RE/specs/resource_pipeline.md §4.3 — streamer thread follows the player. CODE-CONFIRMED.
        _followAnchorArmed = true;

        GD.Print($"[RealWorldRenderer] Terrain streaming requested for centre ({TargetMapX},{TargetMapZ}). " +
                 $"[StreamFollow] Player-following terrain streaming ARMED — anchor=({TargetMapX},{TargetMapZ}), " +
                 $"hysteresis={HysteresisThresholdCells} cell(s). " +
                 $"As the player moves, the ring will recenter and the 17 south NPCs will ground " +
                 $"via the pending-snap mechanism as their sectors stream in.");
    }
}