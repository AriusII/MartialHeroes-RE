using Godot;
using MartialHeroes.Client.Application.World;
using MartialHeroes.Client.Godot.Autoload;

namespace MartialHeroes.Client.Godot.World;

public sealed partial class RealWorldRenderer
{
    private void TriggerTerrainStreaming(ClientContext ctx)
    {
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

        _streamingCts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token);
        var streamingToken = _streamingCts.Token;

        ctx.StreamingService.SetArea(TargetAreaId);
        ctx.AreaAssemblySource?.SetArea(TargetAreaId);

        ctx.SetExpectedBakeArea(TargetAreaId);

        ctx.AreaAssemblyHandoff?.OnAreaBound(TargetAreaId);

        if (_cellCollisionManager is null)
            _cellCollisionManager = new CellCollisionManager();
        else
            _cellCollisionManager.Clear();

        if (_localPlayerNode is VisualActor localPlayerActor && IsInstanceValid(localPlayerActor))
            localPlayerActor.SetCollisionManager(_cellCollisionManager);

        GD.Print($"[RealWorldRenderer] CellCollisionManager ready for area {TargetAreaId} " +
                 "(single instance; geometry cleared on rebind). spec: Docs/RE/formats/sod.md.");

        const float CameraFarPlaneWu = 15_000f;
        var candidateStreamRadius = CameraFarPlaneWu;
        var effectiveStreamRadius = WorldSceneContract.ClampStreamRadius(candidateStreamRadius);
        GD.Print($"[RealWorldRenderer] Stream radius: candidate={candidateStreamRadius:F0} wu, " +
                 $"effective (clamped)={effectiveStreamRadius:F0} wu. " +
                 "spec: Docs/RE/specs/world_systems.md §13.1 (WorldSceneContract.ClampStreamRadius).");

        _streamAnchor = (TargetMapX, TargetMapZ);

        _ = Task.Run(async () =>
        {
            try
            {
                await ctx.StreamingService.UpdateCenterAsync(TargetMapX, TargetMapZ, streamingToken)
                    .ConfigureAwait(false);

                if (streamingToken.IsCancellationRequested) return;

                var residentCount = ctx.StreamingService.ResidentCount;
                GD.Print($"[RealWorldRenderer] 5×5 terrain ring streaming complete " +
                         $"(area {TargetAreaId}, resident={residentCount} sectors).");
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                if (!streamingToken.IsCancellationRequested)
                    GD.PrintErr($"[RealWorldRenderer] Terrain streaming error: {ex.Message}");
            }
        }, streamingToken);

        _followAnchorArmed = true;

        GD.Print($"[RealWorldRenderer] Terrain streaming requested for centre ({TargetMapX},{TargetMapZ}). " +
                 $"[StreamFollow] Player-following terrain streaming ARMED — anchor=({TargetMapX},{TargetMapZ}), " +
                 $"hysteresis={HysteresisThresholdCells} cell(s). " +
                 $"As the player moves, the ring will recenter and the 17 south NPCs will ground " +
                 $"via the pending-snap mechanism as their sectors stream in.");
    }
}