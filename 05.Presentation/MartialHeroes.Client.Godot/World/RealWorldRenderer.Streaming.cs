using Godot;
using MartialHeroes.Client.Domain.Simulation.Simulation;
using MartialHeroes.Client.Godot.Autoload;

namespace MartialHeroes.Client.Godot.World;

public sealed partial class RealWorldRenderer
{
    private const float B2StreamRadiusHigh = 1800f;

    private const float B2StreamRadiusMedium = 1000f;

    private const float B2StreamRadiusLow = 600f;

    private const float B2StreamRadiusCeiling = 15_000f;

    private const float B2StreamRadiusCeilingReset = 1000f;

    private const float B2RingForwardThreshold = 1000f;

    private static float B2SelectStreamRadius()
    {
        return B2StreamRadiusHigh;
    }

    private static float B2ClampStreamRadius(float radius)
    {
        if (radius < 0f) return 0f;
        return radius >= B2StreamRadiusCeiling ? B2StreamRadiusCeilingReset : radius;
    }

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

        var candidateStreamRadius = B2SelectStreamRadius();
        var effectiveStreamRadius = B2ClampStreamRadius(candidateStreamRadius);
        var ringQuality = effectiveStreamRadius > B2RingForwardThreshold ? StreamQuality.High : StreamQuality.Medium;
        ctx.StreamingService.Quality = ringQuality;
        GD.Print($"[RealWorldRenderer] Stream radius: candidate={candidateStreamRadius:F0} wu, " +
                 $"effective={effectiveStreamRadius:F0} wu (clamp: >=15000 -> 1000), " +
                 $"ring={(ringQuality == StreamQuality.High ? "5x5" : "3x3")}. " +
                 "spec: Docs/RE/specs/terrain-streaming.md §6.6 (quality tier 1800/1000/600 + 15000->1000 clamp), §6.0/§6.2 (radius>1000 -> 5x5 ring).");

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