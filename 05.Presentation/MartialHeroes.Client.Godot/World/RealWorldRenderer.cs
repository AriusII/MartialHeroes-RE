using Godot;
using MartialHeroes.Assets.Mapping;
using MartialHeroes.Assets.Parsers.Terrain.Models;
using MartialHeroes.Client.Application.World;
using MartialHeroes.Client.Domain.Simulation.Simulation;
using MartialHeroes.Client.Godot.Autoload;
using MartialHeroes.Client.Godot.Composition;

namespace MartialHeroes.Client.Godot.World;

public sealed partial class RealWorldRenderer : Node3D
{
    private const int HysteresisThresholdCells = 1;

    private readonly HashSet<(int MapX, int MapZ)> _composedBuildingsSpawned = new();

    private readonly Dictionary<(int MapX, int MapZ), AssembledCell>
        _composedCells = new();

    private readonly Dictionary<string, ImageTexture?> _composerTexCache = new();

    private readonly CancellationTokenSource _lifetimeCts = new();

    private BgTextureCatalog? _bgTextures;

    private CameraController? _cameraController;

    private CellCollisionManager? _cellCollisionManager;
    private MapDescriptor? _cellMap;

    private bool _composeRender;

    private ClientContext? _ctx;

    private EnvironmentNode? _environmentNode;

    private bool _followAnchorArmed;

    private volatile bool _isRecentering;

    private Node3D? _localPlayerNode;

    private MapXEffectScheduler? _mapXEffectScheduler;

    private (int MapX, int MapZ) _streamAnchor;

    private CancellationTokenSource? _streamingCts;
    private TerrainNode? _terrainNode;

    private volatile bool _worldEntryInProgress;

    public int TargetAreaId { get; set; } = 0;

    public int TargetMapX { get; set; } = 10000;

    public int TargetMapZ { get; set; } = 10000;

    public string? SknVirtualPath { get; set; }

    public string? BndVirtualPath { get; set; }


    public static bool IsEnabled
    {
        get
        {
            var clientDir = ClientPathResolver.ResolveClientDir();
            return ClientPathResolver.RealAssetsEnabled(clientDir);
        }
    }

    public RealClientAssets? Assets { get; private set; }


    public CellCollisionManager? GetCellCollisionManager()
    {
        return _cellCollisionManager;
    }

    public void SetLocalPlayer(Node3D playerNode)
    {
        _localPlayerNode = playerNode;
        GD.Print("[RealWorldRenderer] Local player registered — camera + terrain-streaming now follow the " +
                 "live (3/7) player. spec: login_flow.md §3.5 / resource_pipeline.md §4.3.");
    }

    public void OnSectorUnloaded(SectorUnloadedEvent evt)
    {
        _cellCollisionManager?.UnregisterCell(evt.MapX, evt.MapZ);
    }


    private void FeedMapXEffectScheduler()
    {
        if (_mapXEffectScheduler is null || !IsInstanceValid(_mapXEffectScheduler)) return;

        _mapXEffectScheduler.CurrentAreaId = TargetAreaId;
        _mapXEffectScheduler.HasLocalPlayer = _localPlayerNode is not null && IsInstanceValid(_localPlayerNode);

        if (_mapXEffectScheduler.HasLocalPlayer)
            _mapXEffectScheduler.LocalPlayerGodotPos = _localPlayerNode!.GlobalPosition;

        if (_environmentNode is not null && IsInstanceValid(_environmentNode))
            _mapXEffectScheduler.TimeOfDayMs = (uint)Math.Round(_environmentNode.ClockMs);
    }

    public void UpdateEnvironmentClock(byte serverHour, byte serverMinute)
    {
        if (_environmentNode is null || !IsInstanceValid(_environmentNode)) return;
        var clockMs = (serverHour * 3600.0 + serverMinute * 60.0) * 1000.0;
        _environmentNode.UpdateClockMs(clockMs);
        _environmentNode.CycleEnabled = false;
        GD.Print($"[RealWorldRenderer] Environment clock set from server 4/1: {serverHour:D2}:{serverMinute:D2} " +
                 $"({clockMs / 3600000.0:F2}h) — local free-run disabled, server-authoritative. " +
                 "spec: packets/4-1_game_state_tick.yaml §fields.Hour/Minute.");
    }

    public override void _ExitTree()
    {
        try
        {
            _streamingCts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        try
        {
            _streamingCts?.Dispose();
        }
        catch (ObjectDisposedException)
        {
        }

        _streamingCts = null;

        try
        {
            _lifetimeCts.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        _lifetimeCts.Dispose();

        Assets?.Dispose();
        Assets = null;

        if (_cellCollisionManager is not null)
        {
            _cellCollisionManager.Clear();
            _cellCollisionManager = null;
        }

        _mapXEffectScheduler = null;
        _environmentNode = null;
    }

    public override void _Process(double delta)
    {
        if (_localPlayerNode is not null && IsInstanceValid(_localPlayerNode) && _cameraController is not null)
            _cameraController.PlayerGodotPosition = _localPlayerNode.GlobalPosition;

        FeedMapXEffectScheduler();

        if (!_followAnchorArmed || _ctx is null || _localPlayerNode is null || !IsInstanceValid(_localPlayerNode))
            return;

        var godotPos = _localPlayerNode.GlobalPosition;
        var legacyX = godotPos.X;
        var legacyZ = -godotPos.Z;

        var (playerMapX, playerMapZ) = SectorGrid.WorldToSector(legacyX, legacyZ);

        var distX = Math.Abs(playerMapX - _streamAnchor.MapX);
        var distZ = Math.Abs(playerMapZ - _streamAnchor.MapZ);
        var chebyshev = Math.Max(distX, distZ);

        if (chebyshev < HysteresisThresholdCells || _isRecentering)
            return;

        var newAnchorX = playerMapX;
        var newAnchorZ = playerMapZ;
        (int OldX, int OldZ) oldAnchor = _streamAnchor;

        _streamAnchor = (newAnchorX, newAnchorZ);
        _isRecentering = true;

        GD.Print($"[RealWorldRenderer] StreamFollow: recenter anchor " +
                 $"({oldAnchor.OldX},{oldAnchor.OldZ}) → ({newAnchorX},{newAnchorZ}) " +
                 $"(Chebyshev={chebyshev}, player legacyXZ=({legacyX:F0},{legacyZ:F0})).");

        var lifetime = _lifetimeCts.Token;
        var ctxCapture = _ctx;

        _ = Task.Run(async () =>
        {
            try
            {
                await ctxCapture.StreamingService.UpdateCenterAsync(newAnchorX, newAnchorZ, lifetime)
                    .ConfigureAwait(false);

                if (lifetime.IsCancellationRequested) return;

                var resident = ctxCapture.StreamingService.ResidentCount;
                GD.Print($"[RealWorldRenderer] StreamFollow: recenter to ({newAnchorX},{newAnchorZ}) " +
                         $"complete — resident={resident} sectors.");
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                if (!lifetime.IsCancellationRequested)
                    GD.PrintErr($"[RealWorldRenderer] StreamFollow: UpdateCenterAsync error: {ex.Message}");
            }
            finally
            {
                _isRecentering = false;
            }
        }, lifetime);
    }
}