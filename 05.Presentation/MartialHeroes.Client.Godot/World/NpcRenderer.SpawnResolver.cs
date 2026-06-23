using Godot;
using MartialHeroes.Assets.Parsers.Mesh;
using MartialHeroes.Assets.Parsers.Mesh.Models;
using MartialHeroes.Client.Application.World;
using MartialHeroes.Client.Godot.Composition;
using MartialHeroes.Client.Presentation.World;

namespace MartialHeroes.Client.Godot.World;

public sealed partial class NpcRenderer
{
    private Node3D? TryBuildFromMobId(RealClientAssets assets, ushort mobId)
    {
        if (_actorMotionLookup is null || _skinClassToSknPath is null)
            return null;

        if (!_actorMotionLookup.TryGetValue(mobId, out var entry))
            return null;

        var skinClass = entry.SkinClassId;

        if (!_skinClassToSknPath.TryGetValue(skinClass, out var sknPath))
            return null;

        var idleMotId = entry.MotionIds.Length > 1 ? entry.MotionIds[1]
            : entry.MotionIds.Length > 0 ? entry.MotionIds[0]
            : 0;

        return TryBuildActorNode(assets, sknPath, skinClass, idleMotId,
            $"mob_id={mobId} skin_class={skinClass}");
    }

    private Node3D? TryBuildDirectSkinProbe(RealClientAssets assets, ushort mobId)
    {
        var sknPath = $"data/char/skin/g{mobId}.skn";
        if (!assets.Contains(sknPath))
            return null;
        return TryBuildActorNode(assets, sknPath, -1, 0,
            $"direct-probe mob_id={mobId}");
    }

    private Node3D? TryBuildActorNode(
        RealClientAssets assets, string sknPath, int skinClass, int idleMotId, string debugLabel)
    {
        return TryBuildActorNode(assets, sknPath, skinClass, idleMotId, [], debugLabel);
    }

    private Node3D? TryBuildActorNode(
        RealClientAssets assets, string sknPath, int skinClass, int idleMotId,
        IReadOnlyList<EquipmentPart> equipParts, string debugLabel)
    {
        SkinnedMesh mesh;
        try
        {
            var sknData = assets.GetRaw(sknPath);
            if (sknData.IsEmpty)
            {
                GD.PrintErr($"[NpcRenderer] .skn empty in VFS: {sknPath} ({debugLabel})");
                return null;
            }

            mesh = SknParser.Parse(sknData);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[NpcRenderer] SknParser.Parse failed '{sknPath}' ({debugLabel}): {ex.Message}");
            return null;
        }

        ImageTexture? albedo = null;
        try
        {
            albedo = CharacterTextureResolver.Resolve(assets, mesh);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[NpcRenderer] Texture resolve failed for '{sknPath}': {ex.Message}");
        }

        var bndId = skinClass > 0 ? skinClass : (int)mesh.IdB;
        var skeleton = TryLoadSkeleton(assets, bndId, debugLabel);

        var clip = skeleton is not null && idleMotId > 0
            ? TryLoadAnimation(assets, idleMotId, debugLabel)
            : null;

        try
        {
            var startPhase = clip is not null ? NextPhase(clip.FrameCount * SkinningMath.MotSecondsPerFrame) : 0f;

            var baseSkinId = skinClass > 0 ? skinClass : (int)mesh.IdB;
            var visualParts =
                skeleton is not null
                && EquipOverlayResolver.RunsOverlayResolution(baseSkinId)
                && equipParts.Count > 0
                    ? EquipmentPartResolver.Resolve(assets, equipParts,
                        EquipOverlayResolver.OtherActorRebuildSlots(), debugLabel)
                    : [];

            Node3D root;
            SkinnedCharacterNode? lbs;
            if (visualParts.Count > 0)
                root = SkinnedCharacterBuilder.BuildWithEquipment(
                    mesh, skeleton, clip, albedo,
                    skeleton is not null,
                    startPhase,
                    visualParts,
                    out lbs,
                    debugLabel);
            else
                root = SkinnedCharacterBuilder.Build(
                    mesh, skeleton, clip, albedo,
                    skeleton is not null,
                    startPhase,
                    out lbs,
                    debugLabel);

            if (lbs is not null)
            {
                var bucket = _skinnedActors.Count % SkinTickGroups;
                _skinnedActors.Add(new SkinnedActor(lbs, bucket));
                _totalSkinned++;
            }
            else
            {
                _totalStaticFallback++;
            }

            return root;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[NpcRenderer] SkinnedCharacterBuilder.Build failed '{sknPath}': {ex.Message}");
            return null;
        }
    }

    private static Skeleton? TryLoadSkeleton(RealClientAssets assets, int bndId, string debugLabel)
    {
        if (bndId <= 0) return null;
        var registry = CharVisualRegistry.GetOrBuild(assets);
        var skeleton = registry?.TryGetSkeletonByIdB(bndId);
        if (skeleton is null)
            GD.Print($"[NpcRenderer] no .bnd registered with parsed actor_id={bndId} ({debugLabel}) — rest pose. " +
                     "spec: skinning.md §8(e) / formats/bindlist.md.");
        return skeleton;
    }

    private static AnimationClip? TryLoadAnimation(RealClientAssets assets, int idleMotId, string debugLabel)
    {
        var registry = CharVisualRegistry.GetOrBuild(assets);
        var motPath = registry?.ResolveMotPath(idleMotId);
        if (motPath is null || !assets.Contains(motPath))
        {
            GD.Print($"[NpcRenderer] idle .mot not registered for id_b={idleMotId} ({debugLabel}) — rest pose. " +
                     "spec: MotList_LoadAndRegister (id_b registry).");
            return null;
        }

        try
        {
            var data = assets.GetRaw(motPath);
            if (data.IsEmpty) return null;
            return AnimationParser.Parse(data);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[NpcRenderer] .mot load failed '{motPath}' ({debugLabel}): {ex.Message}");
            return null;
        }
    }

    private float NextPhase(float durationSeconds)
    {
        if (durationSeconds <= 0f) return 0f;
        var x = _phaseRng;
        x ^= x << 13;
        x ^= x >> 17;
        x ^= x << 5;
        _phaseRng = x;
        var u = (x & 0xFFFFFF) / (float)0x1000000;
        return u * durationSeconds;
    }
}