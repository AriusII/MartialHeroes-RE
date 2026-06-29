using System.Collections.Concurrent;
using Godot;
using MartialHeroes.Client.Application.Contracts.Hud;
using MartialHeroes.Client.Domain.Actors.Actors;
using MartialHeroes.Client.Godot.World;
using MartialHeroes.Client.Presentation.Helpers;
using MartialHeroes.Shared.Kernel.Enums;
using MartialHeroes.Shared.Kernel.Numerics;
using AppInputEvent = MartialHeroes.Client.Application.Input.InputEvent;

namespace MartialHeroes.Client.Godot.Input;

public sealed partial class InputRouter
{
    private const float EntityPickRadius = 3.0f;
    private const float EntityPickCentreLift = 2.0f;

    private const float PickRayLength = 1000f;

    private const float MoveStepClampUnits = 12f;
    private const float MoveStepClampUnitsSq = 144f;
    private const float MoveDeadZoneUnitsSq = 4f;

    private const ulong ClickReissueThrottleMs = 100;
    private const ulong MoveHeartbeatMs = 200;

    private readonly ConcurrentQueue<PendingWorldClick> _pendingClicks = new();

    private MeshInstance3D? _clickMarker;
    private ulong _lastHeartbeatMs;
    private ulong _lastMoveIssueMs;
    private bool _moveActive;
    private float _moveTargetLegacyX;
    private float _moveTargetLegacyZ;

    internal bool EnqueueWorldClick(in AppInputEvent e)
    {
        if (!e.IsLeftButtonClick) return false;

        _pendingClicks.Enqueue(new PendingWorldClick(e.X, e.Y, e.Modifiers));
        return true;
    }

    public override void _Process(double delta)
    {
        if (_clientContext?.SceneMachine.Current.State != EngineSceneState.InGame)
        {
            _pendingClicks.Clear();
            return;
        }

        while (_pendingClicks.TryDequeue(out var click))
            ProcessWorldClick(click);

        EmitMoveHeartbeat();
    }

    private void ProcessWorldClick(PendingWorldClick click)
    {
        if (_clientContext is null) return;

        var ctrl = (click.Modifiers & ModCtrl) != 0;
        var alt = (click.Modifiers & ModAlt) != 0;

        if (ctrl)
        {
            HandleGroundSpecial(click.X, click.Y);
            return;
        }

        if (TryPickEntity(click.X, click.Y, out var target) && target is not null)
        {
            PublishTarget(target);
            return;
        }

        if (alt)
        {
            GD.Print("[InputRouter] Alt entity-pick miss → movement suppressed. " +
                     "spec: Docs/RE/specs/input_ui.md §3b Case A.");
            return;
        }

        TryClickToMove(click.X, click.Y);
    }

    private bool TryPickEntity(int screenX, int screenY, out VisualActor? best)
    {
        best = null;

        var cam = ResolveCamera();
        if (cam is null) return false;

        var registry = ResolveActorRegistry();
        if (registry is null) return false;

        var screenPos = new Vector2(screenX, screenY);
        var origin = cam.ProjectRayOrigin(screenPos);
        var dir = cam.ProjectRayNormal(screenPos);

        var bestT = float.MaxValue;
        foreach (var child in registry.GetChildren())
        {
            if (child is not VisualActor actor || !IsInstanceValid(actor)) continue;
            if (actor.IsLocalPlayer) continue;
            if (actor.ActorKey.Sort == EntitySort.None) continue;

            var centre = actor.GlobalPosition + new Vector3(0f, EntityPickCentreLift, 0f);
            if (!RaySphere(origin, dir, centre, EntityPickRadius, out var t)) continue;
            if (t < 0f || t > PickRayLength) continue;
            if (t < bestT)
            {
                bestT = t;
                best = actor;
            }
        }

        return best is not null;
    }

    private void PublishTarget(VisualActor actor)
    {
        var ev = new TargetChangedEvent(actor.ActorKey, actor.ActorName, 1f, 1f);
        _clientContext!.HudEventHub.PublishTargetChanged(ev);

        GD.Print($"[InputRouter] entity-pick → {actor.ActorKey.Sort} '{actor.ActorName}' id={actor.ActorKey.RawId}; " +
                 "PublishTargetChanged (HP/MP placeholder=full pending application actor combat-state read). " +
                 "spec: Docs/RE/specs/input_ui.md §3b/§3c.");
    }

    private void HandleGroundSpecial(int screenX, int screenY)
    {
        if (!TryGroundPlanePick(screenX, screenY, out var lx, out var lz, out _)) return;

        GD.Print($"[InputRouter] Ctrl ground-special pick @ legacy({lx:F1},{lz:F1}) — player not moved " +
                 "(ground-skill / AoE-template use-case not exposed to layer 05). " +
                 "spec: Docs/RE/specs/input_ui.md §3b Case C.");
    }

    private void TryClickToMove(int screenX, int screenY)
    {
        if (_clientContext is null) return;

        var cam = ResolveCameraController();
        if (cam is not null && !cam.IsGameplayView)
        {
            GD.Print("[InputRouter] click-to-move suppressed (camera not in a gameplay view). " +
                     "spec: Docs/RE/specs/camera_movement.md §B.2 / input_ui.md §3b guard.");
            return;
        }

        var now = global::Godot.Time.GetTicksMsec();
        if (now - _lastMoveIssueMs < ClickReissueThrottleMs) return;

        if (!TryGroundPlanePick(screenX, screenY, out var tgtX, out var tgtZ, out var godotHit)) return;

        var player = cam?.PlayerGodotPosition ?? Vector3.Zero;
        var curX = player.X;
        var curZ = -player.Z;

        var dx = tgtX - curX;
        var dz = tgtZ - curZ;
        var distSq = dx * dx + dz * dz;

        if (distSq <= MoveDeadZoneUnitsSq) return;

        if (distSq > MoveStepClampUnitsSq)
        {
            var scale = MoveStepClampUnits / MathF.Sqrt(distSq);
            tgtX = curX + dx * scale;
            tgtZ = curZ + dz * scale;
            godotHit = new Vector3(tgtX, godotHit.Y, -tgtZ);
        }

        _lastMoveIssueMs = now;
        _lastHeartbeatMs = now;
        _moveActive = true;
        _moveTargetLegacyX = tgtX;
        _moveTargetLegacyZ = tgtZ;

        DropClickMarker(godotHit);

        var target = Vector3Fixed.FromFloat(tgtX, 0f, tgtZ);
        _ = _clientContext.UseCases.RequestMoveAsync(target, false);

        GD.Print($"[InputRouter] click-to-move 2/13 → legacy({tgtX:F1},{tgtZ:F1}) " +
                 "(ground-plane pick, step-clamp ≤12u, dead-zone >2u, re-issue throttle 100ms). " +
                 "spec: Docs/RE/specs/camera_movement.md §B.2.");
    }

    private void EmitMoveHeartbeat()
    {
        if (!_moveActive || _clientContext is null) return;

        var cam = ResolveCameraController();
        if (cam is null)
        {
            _moveActive = false;
            return;
        }

        var godot = cam.PlayerGodotPosition;
        var legacyX = godot.X;
        var legacyZ = -godot.Z;

        var dx = legacyX - _moveTargetLegacyX;
        var dz = legacyZ - _moveTargetLegacyZ;
        if (dx * dx + dz * dz <= MoveDeadZoneUnitsSq)
        {
            _moveActive = false;
            return;
        }

        var now = global::Godot.Time.GetTicksMsec();
        if (now - _lastHeartbeatMs < MoveHeartbeatMs) return;
        _lastHeartbeatMs = now;

        var here = Vector3Fixed.FromFloat(legacyX, 0f, legacyZ);
        _ = _clientContext.UseCases.RequestMoveAsync(here, false);

        GD.Print($"[InputRouter] move-heartbeat 2/13 @ legacy({legacyX:F1},{legacyZ:F1}) " +
                 "(reports current position for server reconciliation; ±20 dither intentionally not ported). " +
                 "spec: Docs/RE/specs/camera_movement.md §B.1.2.");
    }

    private bool TryGroundPlanePick(int screenX, int screenY, out float legacyX, out float legacyZ, out Vector3 godotHit)
    {
        legacyX = 0f;
        legacyZ = 0f;
        godotHit = Vector3.Zero;

        var cam = ResolveCamera();
        if (cam is null) return false;

        var planeY = ResolveCameraController()?.PlayerGodotPosition.Y ?? 0f;

        var screenPos = new Vector2(screenX, screenY);
        var origin = cam.ProjectRayOrigin(screenPos);
        var dir = cam.ProjectRayNormal(screenPos);

        var plane = new Plane(Vector3.Up, planeY);
        var hit = plane.IntersectsRay(origin, dir);
        if (hit is null) return false;

        godotHit = hit.Value;

        var (lx, _, lz) = WorldCoordinates.ToLegacy(godotHit.X, godotHit.Y, godotHit.Z);
        legacyX = lx;
        legacyZ = lz;
        return true;
    }

    private void DropClickMarker(Vector3 godotPos)
    {
        if (_clickMarker is not null && IsInstanceValid(_clickMarker))
        {
            _clickMarker.QueueFree();
            _clickMarker = null;
        }

        var mesh = new TorusMesh { InnerRadius = 0.6f, OuterRadius = 0.95f };
        var marker = new MeshInstance3D { Name = "ClickMarker", Mesh = mesh };
        var mat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.95f, 0.92f, 0.25f),
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded
        };
        marker.MaterialOverride = mat;
        marker.Position = godotPos + new Vector3(0f, 0.05f, 0f);

        GetParent()?.AddChild(marker);
        _clickMarker = marker;
    }

    private Camera3D? ResolveCamera()
    {
        if (_camera is null || !IsInstanceValid(_camera) || !_camera.Current)
            _camera = GetViewport()?.GetCamera3D();
        return _camera;
    }

    private CameraController? ResolveCameraController()
    {
        return ResolveCamera() as CameraController;
    }

    private ActorRegistry? ResolveActorRegistry()
    {
        return GetParent()?.GetNodeOrNull<ActorRegistry>("ActorRegistry");
    }

    private static bool RaySphere(Vector3 origin, Vector3 dir, Vector3 centre, float radius, out float t)
    {
        t = 0f;
        var oc = origin - centre;
        var b = oc.Dot(dir);
        var c = oc.Dot(oc) - radius * radius;
        if (c > 0f && b > 0f) return false;

        var disc = b * b - c;
        if (disc < 0f) return false;

        var sqrt = MathF.Sqrt(disc);
        var t0 = -b - sqrt;
        t = t0 >= 0f ? t0 : -b + sqrt;
        return t >= 0f;
    }

    private readonly record struct PendingWorldClick(int X, int Y, int Modifiers);
}
