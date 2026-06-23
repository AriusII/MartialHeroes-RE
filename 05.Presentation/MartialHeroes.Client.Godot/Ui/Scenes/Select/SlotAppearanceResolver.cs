using Godot;
using MartialHeroes.Assets.Parsers.Character;
using MartialHeroes.Assets.Parsers.Mesh;
using MartialHeroes.Assets.Parsers.Mesh.Models;
using MartialHeroes.Client.Godot.Composition;
using MartialHeroes.Client.Godot.World;
using MartialHeroes.Client.Presentation.Screens;

namespace MartialHeroes.Client.Godot.Ui.Scenes.Select;

public static class SlotAppearanceResolver
{
    private const string SkinTxtTablePath = "data/char/skin.txt";

    public static SlotBuildResult BuildSlotActor(
        RealClientAssets assets,
        SlotAppearance appearance,
        string debugLabel)
    {
        var modelClassId = ClassAppearanceResolver.ModelClassId(
            (int)appearance.InternalClass, (int)appearance.AppearanceVariant);

        if (modelClassId <= 0)
        {
            GD.Print(
                $"[SlotAppearanceResolver] {debugLabel}: model_class_id={modelClassId} (class={appearance.InternalClass}, " +
                $"variant={appearance.AppearanceVariant}) is the invisible/unmapped sentinel — no mesh (faithful). " +
                "spec: skinning.md §3.5.2.");
            return new SlotBuildResult(null, modelClassId, 0u, false, false);
        }

        var bodyModelClassId = ClassAppearanceResolver.StarterBodyModelClassId((int)appearance.InternalClass);
        if (bodyModelClassId <= 0)
        {
            GD.PrintErr(
                $"[SlotAppearanceResolver] {debugLabel}: unknown class={appearance.InternalClass} — no starter body key " +
                "(NO wrong-class fallback). spec: frontend_scenes.md §3.7.5.");
            return new SlotBuildResult(null, modelClassId, 0u, false, false);
        }

        var bodyMeshGid = ResolveBodyMeshGid(assets, bodyModelClassId, debugLabel);
        if (bodyMeshGid is null)
        {
            GD.PrintErr(
                $"[SlotAppearanceResolver] {debugLabel}: DATA GAP — no skin.txt body row for (slot=3, model_class_id={bodyModelClassId}) " +
                $"class={appearance.InternalClass} — LOGGED + skipped (NO wrong-class fallback, NO fabricated geometry). " +
                "spec: frontend_scenes.md §3.7.5 / skinning.md §3.5.3.");
            return new SlotBuildResult(null, modelClassId, 0u, false, false);
        }

        var bodySknPath = ClassAppearanceResolver.BodySknPathForMeshGid(bodyMeshGid.Value);

        if (!assets.Contains(bodySknPath))
        {
            GD.PrintErr(
                $"[SlotAppearanceResolver] {debugLabel}: DATA GAP — body .skn ABSENT '{bodySknPath}' " +
                $"(class={appearance.InternalClass}, model_class_id={bodyModelClassId}, mesh_gid={bodyMeshGid}) — " +
                "LOGGED + skipped (NO wrong-class fallback, NO fabricated geometry). " +
                "spec: frontend_scenes.md §3.7.5 / skinning.md §3.5.3.");
            return new SlotBuildResult(null, modelClassId, 0u, false, false);
        }

        SkinnedMesh bodyMesh;
        try
        {
            var raw = assets.GetRaw(bodySknPath);
            if (raw.IsEmpty)
            {
                GD.PrintErr($"[SlotAppearanceResolver] {debugLabel}: body .skn empty '{bodySknPath}' — skipped.");
                return new SlotBuildResult(null, modelClassId, 0u, false, false);
            }

            bodyMesh = SknParser.Parse(raw);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[SlotAppearanceResolver] {debugLabel}: body .skn parse failed '{bodySknPath}': {ex.Message}");
            return new SlotBuildResult(null, modelClassId, 0u, false, false);
        }

        var skinClassId = (int)appearance.InternalClass;

        var mappedSkinClass = SkinClassForModelClassId(modelClassId);
        if (mappedSkinClass != 0 && mappedSkinClass != skinClassId)
            GD.Print(
                $"[SlotAppearanceResolver] {debugLabel}: NOTE model_class_id={modelClassId} reduces to SkinClassId={mappedSkinClass} " +
                $"but descriptor InternalClass={skinClassId} — keying rig + idle by InternalClass. spec: skinning.md §8(e)/§3.5.2.");

        if ((int)bodyMesh.IdB != skinClassId)
            GD.Print(
                $"[SlotAppearanceResolver] {debugLabel}: NOTE descriptor SkinClassId={skinClassId} but starter body id_b={bodyMesh.IdB} " +
                "(layer-04 §3.7.5 starter table returns a class-1-family mesh) — rig + idle keyed by the descriptor " +
                "SkinClassId (g{n}.bnd + col2==n idle), per §8(e). spec: skinning.md §8(e) / frontend_scenes.md §3.7.5.");

        var registry = CharVisualRegistry.GetOrBuild(assets);

        var poolKey = ClassAppearanceResolver.SkeletonIdBForModelClassId(modelClassId);
        if (poolKey == 0) poolKey = skinClassId;

        var skeleton = TryLoadSkeleton(registry, poolKey, debugLabel);

        var idleClip = TryLoadIdleClip(assets, registry, modelClassId, skinClassId, debugLabel);

        ImageTexture? albedo = null;
        try
        {
            albedo = CharacterTextureResolver.Resolve(assets, bodyMesh.IdA);
        }
        catch (Exception ex)
        {
            GD.PrintErr(
                $"[SlotAppearanceResolver] {debugLabel}: texture resolve failed for IdA={bodyMesh.IdA}: {ex.Message}");
        }

        var parts = ResolveOverlayParts(assets, appearance, skinClassId, debugLabel, out var missingGearSkns);

        Node3D actorRoot;
        try
        {
            if (parts.Count > 0)
                actorRoot = SkinnedCharacterBuilder.BuildWithEquipment(
                    bodyMesh, skeleton, idleClip, albedo,
                    false, 0f, parts, out _, debugLabel);
            else
                actorRoot = SkinnedCharacterBuilder.Build(bodyMesh, skeleton, idleClip, albedo);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[SlotAppearanceResolver] {debugLabel}: SkinnedCharacterBuilder failed: {ex.Message}");
            return new SlotBuildResult(null, modelClassId, bodyMesh.IdB, skeleton is not null, idleClip is not null);
        }

        var missingReport = missingGearSkns.Count > 0
            ? $" MISSING gear .skn (logged+skipped, NOT fabricated): [{string.Join(", ", missingGearSkns)}]"
            : "";

        GD.Print(
            $"[SlotAppearanceResolver] {debugLabel}: built class={appearance.InternalClass} variant={appearance.AppearanceVariant} " +
            $"-> model_class_id={modelClassId} body='{bodySknPath}' (IdA={bodyMesh.IdA}, parsed-id_b={bodyMesh.IdB}) " +
            $"SkinClassId={skinClassId} rig={(skeleton is not null ? "pool[id_b=" + poolKey + "]" : "REST")} idle={(idleClip is not null ? "col16" : "BIND")} " +
            $"equip={parts.Count}.{missingReport} spec: skinning.md §3.5.2/§8(e); frontend_scenes.md §3.3.7/§3.7.5.");

        return new SlotBuildResult(actorRoot, modelClassId, bodyMesh.IdB, skeleton is not null, idleClip is not null);
    }


    private static int? ResolveBodyMeshGid(RealClientAssets assets, int bodyModelClassId, string debugLabel)
    {
        if (!assets.Contains(SkinTxtTablePath))
        {
            GD.PrintErr(
                $"[SlotAppearanceResolver] {debugLabel}: DATA GAP — '{SkinTxtTablePath}' absent; cannot resolve body for " +
                $"model_class_id={bodyModelClassId}. spec: skinning.md §3.5.3.");
            return null;
        }

        try
        {
            var catalogue = SkinTxtParser.Parse(assets.GetRaw(SkinTxtTablePath));
            return catalogue.GetBodyMeshGid(bodyModelClassId);
        }
        catch (Exception ex)
        {
            GD.PrintErr(
                $"[SlotAppearanceResolver] {debugLabel}: skin.txt body resolve failed (model_class_id={bodyModelClassId}): {ex.Message}");
            return null;
        }
    }


    private static Skeleton? TryLoadSkeleton(CharVisualRegistry? registry, int idB, string debugLabel)
    {
        if (idB <= 0)
        {
            GD.PrintErr(
                $"[SlotAppearanceResolver] {debugLabel}: invalid pose-pool id_b={idB} — static rest pose. spec: skinning.md §8(e).");
            return null;
        }

        if (registry is null)
        {
            GD.PrintErr(
                $"[SlotAppearanceResolver] {debugLabel}: no CharVisualRegistry (VFS registries absent) — rest pose. spec: skinning.md §8(e).");
            return null;
        }

        var skeleton = registry.TryGetSkeletonByIdB(idB);
        if (skeleton is null)
            GD.PrintErr(
                $"[SlotAppearanceResolver] {debugLabel}: DATA GAP — no .bnd registered with parsed actor_id={idB} in bindlist.txt — " +
                "rest pose (NOT a g{n}.bnd coincidence). spec: skinning.md §8(e) / formats/bindlist.md.");
        return skeleton;
    }


    private static AnimationClip? TryLoadIdleClip(
        RealClientAssets assets, CharVisualRegistry? registry, int modelClassId, int skinClassId, string debugLabel)
    {
        if (registry is null) return null;

        try
        {
            var entry = registry.GetByMotionKey(modelClassId)
                        ?? registry.ActorMotion.GetBySkinClass(skinClassId);
            if (entry is null)
            {
                GD.Print(
                    $"[SlotAppearanceResolver] {debugLabel}: no actormotion row for motion_key={modelClassId} (or col2==SkinClassId={skinClassId}) — bind pose. " +
                    "spec: skinning.md §8(e)/§10.");
                return null;
            }

            var idle = entry.IdleMotionId;
            if (idle <= 0)
            {
                GD.Print(
                    $"[SlotAppearanceResolver] {debugLabel}: actormotion row motion_key={modelClassId} has empty col16 idle — bind pose. " +
                    "spec: skinning.md §10.");
                return null;
            }

            var motPath = registry.ResolveMotPath(idle);
            if (motPath is null || !assets.Contains(motPath))
            {
                GD.PrintErr(
                    $"[SlotAppearanceResolver] {debugLabel}: idle .mot not registered for id_b={idle} (model_class_id={modelClassId}) — bind pose. " +
                    "spec: MotList_LoadAndRegister (id_b registry).");
                return null;
            }

            var motData = assets.GetRaw(motPath);
            return motData.IsEmpty ? null : AnimationParser.Parse(motData);
        }
        catch (Exception ex)
        {
            GD.PrintErr(
                $"[SlotAppearanceResolver] {debugLabel}: idle resolve failed (model_class_id={modelClassId}): {ex.Message}");
            return null;
        }
    }


    private static int SkinClassForModelClassId(int modelClassId)
    {
        return modelClassId switch
        {
            1 => 1,
            26 => 2,
            11 => 3,
            16 => 4,
            _ => 0
        };
    }


    private static IReadOnlyList<SkinnedCharacterBuilder.EquipmentVisualPart> ResolveOverlayParts(
        RealClientAssets assets, SlotAppearance appearance, int skinClassId, string debugLabel,
        out List<string> missingGids)
    {
        var parts = new List<SkinnedCharacterBuilder.EquipmentVisualPart>();
        missingGids = [];

        var equip = appearance.EquipGids;
        var haveHostEquip = equip is not null && equip.Length > 0;

        var dDigit = (int)appearance.FaceA;
        var aDigit = (int)appearance.AppearanceVariant;
        var bDigit = (int)appearance.InternalClass;

        var starter = StarterOverlayIds(skinClassId);

        for (var i = 0; i < ClassAppearanceResolver.OverlaySlots.Length; i++)
        {
            var slot = ClassAppearanceResolver.OverlaySlots[i];

            if (slot == 3) continue;

            string? sknPath;
            long gid;
            if (haveHostEquip && i < equip!.Length && (int)equip[i] > 0)
            {
                gid = ClassAppearanceResolver.ResolvePartGid(slot, (int)equip[i], dDigit, aDigit, bDigit);
                sknPath = ClassAppearanceResolver.DeformSkinPathForGid(gid);
            }
            else if (starter is not null && StarterIdForSlot(starter.Value, slot) is { } starterGid)
            {
                gid = starterGid;
                sknPath = ClassAppearanceResolver.DeformSkinPathForGid(gid);
            }
            else
            {
                continue;
            }

            if (slot == 14)
            {
                if (!assets.Contains(sknPath))
                {
                    GD.Print(
                        $"[SlotAppearanceResolver] {debugLabel}: weapon .skn absent '{sknPath}' (gid={gid}) — slot 14 empty (logged+skipped, NOT fabricated).");
                    missingGids.Add(sknPath);
                    continue;
                }

                try
                {
                    var raw = assets.GetRaw(sknPath);
                    if (raw.IsEmpty) continue;
                    var weaponMesh = SknParser.Parse(raw);
                    var weaponTex = CharacterTextureResolver.Resolve(assets, weaponMesh.IdA);
                    parts.Add(new SkinnedCharacterBuilder.EquipmentVisualPart(
                        slot, weaponMesh, weaponTex,
                        true, false,
                        SkinnedCharacterNode.DefaultHandBoneId, 1.0f));
                    GD.Print(
                        $"[SlotAppearanceResolver] {debugLabel}: weapon overlay slot 14 -> g{gid}.skn (rigid hand attach). spec: §3.3.7.");
                }
                catch (Exception ex)
                {
                    GD.PrintErr(
                        $"[SlotAppearanceResolver] {debugLabel}: weapon slot 14 parse failed '{sknPath}': {ex.Message}");
                }

                continue;
            }

            if (!assets.Contains(sknPath))
            {
                GD.Print(
                    $"[SlotAppearanceResolver] {debugLabel}: overlay slot {slot} .skn absent '{sknPath}' (gid={gid}) — logged+skipped (NOT fabricated).");
                missingGids.Add(sknPath);
                continue;
            }

            try
            {
                var raw = assets.GetRaw(sknPath);
                if (raw.IsEmpty) continue;
                var partMesh = SknParser.Parse(raw);

                if ((int)partMesh.IdB != skinClassId && partMesh.IdB != 0)
                    GD.Print(
                        $"[SlotAppearanceResolver] {debugLabel}: NOTE overlay slot {slot} '{sknPath}' parsed id_b={partMesh.IdB} " +
                        $"!= descriptor SkinClassId={skinClassId} (class-family part; rig bound by descriptor SkinClassId). spec: skinning.md §8(e).");

                var partTex = CharacterTextureResolver.Resolve(assets, partMesh.IdA);
                parts.Add(new SkinnedCharacterBuilder.EquipmentVisualPart(
                    slot, partMesh, partTex,
                    false, false,
                    SkinnedCharacterNode.DefaultHandBoneId, 1.0f));
                GD.Print(
                    $"[SlotAppearanceResolver] {debugLabel}: overlay slot {slot} -> g{gid}.skn (deform on shared rig, IdA={partMesh.IdA}). spec: §3.5.1/§3.6.2.");
            }
            catch (Exception ex)
            {
                GD.PrintErr(
                    $"[SlotAppearanceResolver] {debugLabel}: overlay slot {slot} parse failed '{sknPath}': {ex.Message}");
            }
        }

        return parts;
    }

    private static StarterOverlay? StarterOverlayIds(int skinClassId)
    {
        return skinClassId switch
        {
            1 => new StarterOverlay(202110003, 203110002, 206110002, 209110001),
            2 => new StarterOverlay(202220003, 203220002, 206220002, 209220001),
            3 => new StarterOverlay(202130003, 203130002, 206130002, 209130001),
            4 => new StarterOverlay(202140003, 203140002, 206140002, 209140001),
            _ => null
        };
    }

    private static long? StarterIdForSlot(StarterOverlay o, int slot)
    {
        return slot switch
        {
            4 => o.Slot4P,
            6 => o.Slot6S,
            2 => o.Slot2A,
            _ => null
        };
    }

    public readonly record struct SlotAppearance(
        uint InternalClass,
        uint AppearanceVariant,
        uint FaceA,
        uint[]? EquipGids);

    public readonly record struct SlotBuildResult(
        Node3D? ActorRoot,
        int ModelClassId,
        uint ParsedIdB,
        bool SkeletonResolved,
        bool IdleResolved);


    private readonly record struct StarterOverlay(int Slot3Body, int Slot4P, int Slot6S, int Slot2A);
}