using System.Text;
using Godot;
using MartialHeroes.Assets.Parsers.Mesh;
using MartialHeroes.Assets.Parsers.Mesh.Models;
using MartialHeroes.Client.Application.World;
using MartialHeroes.Client.Godot.Composition;
using MartialHeroes.Client.Presentation.Screens;
using MartialHeroes.Client.Presentation.World;

namespace MartialHeroes.Client.Godot.World;

internal static class PlayerAvatarResolver
{
    public static Node3D? TryBuild(RealClientAssets assets, ushort serverClass)
    {
        return TryBuild(assets, serverClass, []);
    }

    public static Node3D? TryBuild(
        RealClientAssets assets, ushort serverClass, IReadOnlyList<EquipmentPart> equipParts)
    {
        int skinClass = serverClass;
        if (skinClass <= 0)
        {
            GD.Print($"[PlayerAvatar] serverClass={serverClass} not a valid SkinClassId — keeping placeholder. " +
                     "spec: skinning.md §8(e).");
            return null;
        }

        var sknPath = ResolveBodySknPath(assets, skinClass);
        if (sknPath is null)
        {
            GD.Print($"[PlayerAvatar] No .skn with IdB=={skinClass} in skinlist.txt — keeping placeholder. " +
                     "spec: skinning.md §8(e).");
            return null;
        }

        SkinnedMesh mesh;
        try
        {
            var sknData = assets.GetRaw(sknPath);
            if (sknData.IsEmpty)
            {
                GD.PrintErr($"[PlayerAvatar] .skn empty in VFS: {sknPath}");
                return null;
            }

            mesh = SknParser.Parse(sknData);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[PlayerAvatar] SknParser.Parse failed '{sknPath}': {ex.Message}");
            return null;
        }

        ImageTexture? albedo = null;
        try
        {
            albedo = CharacterTextureResolver.Resolve(assets, mesh);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[PlayerAvatar] Texture resolve failed for '{sknPath}': {ex.Message} — neutral material.");
        }

        var registry = CharVisualRegistry.GetOrBuild(assets);

        var appearanceKey = ClassAppearanceResolver.StarterBodyModelClassId(skinClass);
        var poolKey = ClassAppearanceResolver.SkeletonIdBForModelClassId(appearanceKey);
        if (poolKey == 0) poolKey = skinClass;

        var skeleton = TryLoadSkeleton(registry, poolKey);

        var idleMotId = skeleton is not null ? ResolveIdleMotionId(registry, appearanceKey, skinClass) : 0;
        var clip = skeleton is not null && idleMotId > 0
            ? TryLoadAnimation(assets, registry, idleMotId)
            : null;

        try
        {
            var baseSkinId = skinClass;
            var runsOverlay = EquipOverlayResolver.RunsOverlayResolution(baseSkinId);
            var rebuildSlots = EquipOverlayResolver.LocalPlayerRebuildSlots(baseSkinId);

            var visualParts =
                runsOverlay && equipParts.Count > 0
                    ? EquipmentPartResolver.Resolve(assets, equipParts, rebuildSlots,
                        $"local-player class={skinClass}")
                    : [];

            Node3D root;
            SkinnedCharacterNode? lbs;
            if (visualParts.Count > 0)
                root = SkinnedCharacterBuilder.BuildWithEquipment(
                    mesh, skeleton, clip, albedo,
                    false,
                    0f,
                    visualParts,
                    out lbs,
                    $"local-player class={skinClass}");
            else
                root = SkinnedCharacterBuilder.Build(
                    mesh, skeleton, clip, albedo,
                    false,
                    0f,
                    out lbs,
                    $"local-player class={skinClass}");

            GD.Print($"[PlayerAvatar] Built class={skinClass} from '{sknPath}' " +
                     $"(skeleton={skeleton is not null}, idleMot={idleMotId}, " +
                     $"skinned={lbs is not null}, idlePlaying={lbs?.IsIdlePlaying ?? false}, " +
                     $"runsOverlay={runsOverlay}, equipParts={equipParts.Count}->{visualParts.Count} rendered). " +
                     "spec: skinning.md §8(e) / equipment_visuals.md §3.4.");
            return root;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[PlayerAvatar] SkinnedCharacterBuilder.Build failed '{sknPath}': {ex.Message}");
            return null;
        }
    }


    private static string? ResolveBodySknPath(RealClientAssets assets, int skinClass)
    {
        const string listPath = "data/char/skinlist.txt";
        if (!assets.Contains(listPath)) return null;

        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var text = Encoding.GetEncoding(949).GetString(assets.GetRaw(listPath).Span);

            const string skinDir = "data/char/skin/";
            foreach (var rawLine in text.Split('\n'))
            {
                var fname = rawLine.Trim('\r', '\n', ' ').ToLowerInvariant();
                if (fname.Length == 0 || !fname.EndsWith(".skn", StringComparison.Ordinal)) continue;

                var sknPath = skinDir + fname;
                if (!assets.Contains(sknPath)) continue;

                try
                {
                    var sknData = assets.GetRaw(sknPath);
                    if (sknData.IsEmpty) continue;
                    var mesh = SknParser.Parse(sknData);
                    if ((int)mesh.IdB == skinClass) return sknPath;
                }
                catch
                {
                }
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[PlayerAvatar] skinlist.txt scan failed: {ex.Message}");
        }

        return null;
    }

    private static Skeleton? TryLoadSkeleton(CharVisualRegistry? registry, int idB)
    {
        if (registry is null)
        {
            GD.Print(
                "[PlayerAvatar] No CharVisualRegistry (VFS registries absent) — static rest pose. spec: skinning.md §8(e).");
            return null;
        }

        var skeleton = registry.TryGetSkeletonByIdB(idB);
        if (skeleton is null)
            GD.Print(
                $"[PlayerAvatar] No .bnd registered with parsed actor_id={idB} in bindlist.txt — static rest pose. " +
                "spec: skinning.md §8(e) / formats/bindlist.md.");
        return skeleton;
    }

    private static int ResolveIdleMotionId(CharVisualRegistry? registry, int appearanceKey, int skinClass)
    {
        if (registry is null) return 0;

        var entry = registry.GetByMotionKey(appearanceKey)
                    ?? registry.ActorMotion.GetBySkinClass(skinClass);
        return entry?.IdleMotionId ?? 0;
    }

    private static AnimationClip? TryLoadAnimation(RealClientAssets assets, CharVisualRegistry? registry, int idleMotId)
    {
        var motPath = registry?.ResolveMotPath(idleMotId);
        if (motPath is null || !assets.Contains(motPath))
        {
            GD.Print($"[PlayerAvatar] idle .mot not registered for id_b={idleMotId} — rest pose. " +
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
            GD.PrintErr($"[PlayerAvatar] .mot load failed '{motPath}': {ex.Message}");
            return null;
        }
    }
}

internal static class EquipmentPartResolver
{
    public static IReadOnlyList<SkinnedCharacterBuilder.EquipmentVisualPart> Resolve(
        RealClientAssets assets,
        IReadOnlyList<EquipmentPart> coreParts,
        ReadOnlySpan<int> rebuildSlots,
        string debugLabel)
    {
        var result = new List<SkinnedCharacterBuilder.EquipmentVisualPart>(coreParts.Count);

        foreach (var part in coreParts)
        {
            if (!SlotInSet(part.Slot, rebuildSlots)) continue;
            if (part.MeshGid <= 0) continue;

            var sknPath = $"data/char/skin/g{part.MeshGid}.skn";
            if (!assets.Contains(sknPath))
            {
                GD.Print($"[EquipPart] slot {part.Slot} mesh '{sknPath}' absent — skipped ({debugLabel}). " +
                         "spec: equipment_visuals.md §3.2 (catalogue miss → null mesh, no crash).");
                continue;
            }

            SkinnedMesh partMesh;
            try
            {
                var raw = assets.GetRaw(sknPath);
                if (raw.IsEmpty) continue;
                partMesh = SknParser.Parse(raw);
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[EquipPart] slot {part.Slot} SknParser.Parse failed '{sknPath}': {ex.Message}");
                continue;
            }

            ImageTexture? albedo = null;
            try
            {
                albedo = part.TextureId > 0
                    ? CharacterTextureResolver.Resolve(assets, (uint)part.TextureId)
                    : CharacterTextureResolver.Resolve(assets, partMesh);
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[EquipPart] slot {part.Slot} texture resolve failed '{sknPath}': {ex.Message}");
            }

            result.Add(new SkinnedCharacterBuilder.EquipmentVisualPart(
                part.Slot,
                partMesh,
                albedo,
                part.IsHandWeapon,
                part.IsOffHand,
                SkinnedCharacterNode.DefaultHandBoneId,
                1.0f));
        }

        return result;
    }

    private static bool SlotInSet(int slot, ReadOnlySpan<int> set)
    {
        foreach (var s in set)
            if (s == slot)
                return true;
        return false;
    }
}