using Godot;
using MartialHeroes.Assets.Parsers.Character;
using MartialHeroes.Assets.Parsers.Character.Models;
using MartialHeroes.Assets.Parsers.Mesh;
using MartialHeroes.Assets.Parsers.Mesh.Models;
using MartialHeroes.Assets.Vfs;
using MartialHeroes.Client.Presentation.Screens;

namespace MartialHeroes.Explorer.Viewer;

public sealed record AssemblyInfo(
    int InternalClass,
    int Variant,
    int ModelClassId,
    string SkeletonPath,
    int BoneCount,
    uint MatchedActorId,
    int PartsResolved,
    int SkinnedParts,
    IReadOnlyList<string> SlotSummaries,
    int IdleMotionId,
    string IdlePath,
    string MotionProvenance);

public static class CharacterAssembler
{
    private const string SkinTxtPath = "data/char/skin.txt";
    private const string SkinDirPrefix = "data/char/skin/g";
    private const int WeaponSlot = 14;
    private const int FaceAnimCatalogueSlot = 40;
    private const int HeadFaceCatalogueSlot = 0;
    private const int HeadFamilyCatalogueSlot = 210;

    private static readonly int[] EquipSlots = [3, 4, 6, 2, 11, 14];

    public static readonly (int Class, string Name)[] Classes =
    [
        (1, "Musa"),
        (2, "Salsu"),
        (3, "Dosa"),
        (4, "Monk")
    ];

    private static int CatalogueSlotFor(int equipSlot)
    {
        return equipSlot switch
        {
            3 => 3,
            4 => 4,
            6 => 11,
            2 => 6,
            11 => 0,
            _ => -1
        };
    }

    public static int DefaultVariant(int internalClass)
    {
        return ClassAppearanceResolver.StarterAppearanceVariant(internalClass);
    }

    public static Node3D Build(
        MappedVfsArchive archive,
        int internalClass,
        int variant,
        out List<ViewerSkinnedNode> parts,
        out ViewerSkinnedNode? body,
        out string[] motionPaths,
        out AssemblyInfo info)
    {
        parts = [];
        body = null;
        motionPaths = [];

        var modelClassId = ClassAppearanceResolver.ModelClassId(internalClass, variant);
        var slotLines = new List<string>();
        info = new AssemblyInfo(internalClass, variant, modelClassId, string.Empty, 0, 0, 0, 0,
            slotLines, 0, string.Empty, "none");

        var root = new Node3D { Name = $"Avatar_c{internalClass}_v{variant}" };
        var pivot = new Node3D { Name = "Pivot" };
        root.AddChild(pivot);

        if (modelClassId <= 0)
        {
            slotLines.Add("invisible sentinel (variant 3) — no mesh");
            GD.Print($"[Assembler] class={internalClass} variant={variant} -> model_class_id=0 (invisible).");
            return root;
        }

        if (!archive.Contains(SkinTxtPath))
        {
            slotLines.Add("skin.txt absent from VFS");
            GD.PrintErr("[Assembler] skin.txt absent — cannot assemble.");
            return root;
        }

        SkinTxtCatalog catalog;
        try
        {
            catalog = SkinTxtParser.Parse(archive.GetFileContent(SkinTxtPath));
        }
        catch (Exception ex)
        {
            slotLines.Add($"skin.txt parse failed: {ex.Message}");
            GD.PrintErr($"[Assembler] skin.txt parse failed: {ex.Message}");
            return root;
        }

        var bodyEntry = FindDefaultPart(catalog, 3, modelClassId);
        if (bodyEntry is null || bodyEntry.MeshGid <= 0)
        {
            slotLines.Add($"no body row for model_class_id={modelClassId}");
            GD.PrintErr($"[Assembler] no skin.txt body row for model_class_id={modelClassId} " +
                        $"(class={internalClass} variant={variant}).");
            return root;
        }

        var meshCache = new Dictionary<int, SkinnedMesh?>();
        var bodyMesh = LoadSkn(archive, bodyEntry.MeshGid, meshCache);
        if (bodyMesh is null)
        {
            slotLines.Add($"body g{bodyEntry.MeshGid}.skn missing/unparseable");
            GD.PrintErr($"[Assembler] body g{bodyEntry.MeshGid}.skn could not load.");
            return root;
        }

        var resolution = CharacterAssetResolver.ResolveSkeleton(archive, bodyMesh);
        var skeleton = resolution.Skeleton;
        info = info with
        {
            SkeletonPath = resolution.Path,
            BoneCount = skeleton?.Bones.Length ?? 0,
            MatchedActorId = resolution.MatchedActorId
        };

        GD.Print($"[Assembler] class={internalClass} variant={variant} model_class_id={modelClassId} " +
                 $"body=g{bodyEntry.MeshGid}.skn id_b={bodyMesh.IdB} -> skeleton {resolution.Path} " +
                 $"bones={skeleton?.Bones.Length ?? 0} mode={resolution.Mode}.");

        var motion = CharacterAssetResolver.ResolveMotionPaths(archive, skeleton, bodyMesh);
        motionPaths = motion.Paths;
        info = info with
        {
            IdleMotionId = motion.IdleMotionId,
            IdlePath = motion.IdlePath ?? string.Empty,
            MotionProvenance = motion.Provenance
        };

        var hasAabb = false;
        var combined = new Aabb();
        var skinnedCount = 0;
        var resolvedCount = 0;

        foreach (var slot in EquipSlots)
        {
            if (slot == WeaponSlot)
                continue;

            var catSlot = CatalogueSlotFor(slot);
            var entry = slot == 3 ? bodyEntry : FindDefaultPart(catalog, catSlot, modelClassId);
            if (entry is null || entry.MeshGid <= 0)
            {
                slotLines.Add($"slot {slot}: empty (no catalogue row, cat_slot={catSlot})");
                continue;
            }

            var partMesh = slot == 3 ? bodyMesh : LoadSkn(archive, entry.MeshGid, meshCache);
            if (partMesh is null)
            {
                slotLines.Add($"slot {slot}: g{entry.MeshGid}.skn missing");
                continue;
            }

            resolvedCount++;
            var albedo = ResolvePartTexture(archive, entry, partMesh);

            if (skeleton is not null)
            {
                var (lbs, aabb) = SknSkinnedBuilder.TryBuildLbsPart(
                    partMesh, skeleton, null, albedo, $"s{slot}_g{entry.MeshGid}");
                if (lbs is not null)
                {
                    pivot.AddChild(lbs);
                    parts.Add(lbs);
                    skinnedCount++;
                    MergeAabb(ref combined, ref hasAabb, aabb);
                    slotLines.Add($"slot {slot}: g{entry.MeshGid}.skn tex={entry.TextureId} skinned " +
                                  $"({(albedo is not null ? "textured" : "untextured")})");
                    continue;
                }
            }

            var (inst, saabb) = SknSkinnedBuilder.BuildStaticPart(partMesh, albedo);
            pivot.AddChild(inst);
            MergeAabb(ref combined, ref hasAabb, saabb);
            slotLines.Add($"slot {slot}: g{entry.MeshGid}.skn tex={entry.TextureId} static-fallback");
        }

        foreach (var faceSlot in (int[])[HeadFamilyCatalogueSlot, HeadFaceCatalogueSlot, FaceAnimCatalogueSlot])
        {
            var faceEntry = FindDefaultPart(catalog, faceSlot, modelClassId);
            if (faceEntry is null || faceEntry.MeshGid <= 0)
            {
                slotLines.Add($"face cat-slot {faceSlot}: empty (no catalogue row)");
                continue;
            }

            var faceMesh = LoadSkn(archive, faceEntry.MeshGid, meshCache);
            if (faceMesh is null)
            {
                slotLines.Add($"face cat-slot {faceSlot}: g{faceEntry.MeshGid}.skn missing");
                continue;
            }

            if (parts.Any(p => p.Name.ToString().Contains($"_g{faceEntry.MeshGid}", StringComparison.Ordinal)))
            {
                slotLines.Add($"face cat-slot {faceSlot}: g{faceEntry.MeshGid}.skn already built");
                continue;
            }

            resolvedCount++;
            var faceAlbedo = ResolvePartTexture(archive, faceEntry, faceMesh);

            if (skeleton is not null)
            {
                var (lbs, aabb) = SknSkinnedBuilder.TryBuildLbsPart(
                    faceMesh, skeleton, null, faceAlbedo, $"face{faceSlot}_g{faceEntry.MeshGid}");
                if (lbs is not null)
                {
                    pivot.AddChild(lbs);
                    parts.Add(lbs);
                    skinnedCount++;
                    MergeAabb(ref combined, ref hasAabb, aabb);
                    slotLines.Add($"face cat-slot {faceSlot}: g{faceEntry.MeshGid}.skn tex={faceEntry.TextureId} " +
                                  $"skinned ({(faceAlbedo is not null ? "textured" : "untextured")})");
                    continue;
                }
            }

            var (finst, fsaabb) = SknSkinnedBuilder.BuildStaticPart(faceMesh, faceAlbedo);
            pivot.AddChild(finst);
            MergeAabb(ref combined, ref hasAabb, fsaabb);
            slotLines.Add(
                $"face cat-slot {faceSlot}: g{faceEntry.MeshGid}.skn tex={faceEntry.TextureId} static-fallback");
        }

        if (hasAabb)
            SknSkinnedBuilder.RecentreRoot(root, SknSkinnedBuilder.TransformAabb(pivot.Transform.Basis, combined));

        body = parts.Count > 0 ? parts[0] : null;

        var weaponLine = TryAttachWeapon(
            archive, catalog, modelClassId, pivot, meshCache, body, skeleton, (int)bodyMesh.IdB);
        slotLines.Add(weaponLine);

        info = info with { PartsResolved = resolvedCount, SkinnedParts = skinnedCount };

        GD.Print($"[Assembler] assembled class={internalClass} variant={variant}: " +
                 $"{resolvedCount} parts ({skinnedCount} skinned), idle={motion.IdleMotionId}->" +
                 $"{Path.GetFileName(motion.IdlePath ?? "none")} prov={motion.Provenance}.");

        return root;
    }

    private static string TryAttachWeapon(
        MappedVfsArchive archive,
        SkinTxtCatalog catalog,
        int modelClassId,
        Node3D pivot,
        Dictionary<int, SkinnedMesh?> meshCache,
        ViewerSkinnedNode? body,
        Skeleton? skeleton,
        int idB)
    {
        var entry = FindDefaultPart(catalog, WeaponSlot, modelClassId);
        if (entry is null || entry.MeshGid <= 0)
            return "slot 14 (weapon): none equipped — skipped (faithful default, no preview weapon)";

        var weaponMesh = LoadSkn(archive, entry.MeshGid, meshCache);
        if (weaponMesh is null)
            return $"slot 14 (weapon): g{entry.MeshGid}.skn missing — skipped";

        var albedo = ResolvePartTexture(archive, entry, weaponMesh);
        var (inst, _) = SknSkinnedBuilder.BuildStaticPart(weaponMesh, albedo);

        if (body is not null && skeleton is not null)
        {
            var subtypeOffset = WeaponSubtypeOffset(0);
            var boneIndex = body.ResolveHandBoneIndex(idB, subtypeOffset);
            if (boneIndex >= 0)
            {
                inst.Name = "WeaponMesh";
                var attach = new RigidBoneAttachment { Name = $"WeaponRigid_g{entry.MeshGid}" };
                attach.AddChild(inst);
                pivot.AddChild(attach);
                attach.Bind(body, boneIndex);
                return $"slot 14 (weapon): g{entry.MeshGid}.skn rigid-attached to hand bone idx {boneIndex} " +
                       "(hypothesis: userjoint hand-node table pending spec)";
            }
        }

        inst.Name = $"WeaponRigid_g{entry.MeshGid}";
        pivot.AddChild(inst);
        return $"slot 14 (weapon): g{entry.MeshGid}.skn rigid-attached (no hand bone resolved — static parent)";
    }

    private static int WeaponSubtypeOffset(int itemSubtype)
    {
        return itemSubtype switch
        {
            1 or 4 or 10 => 2,
            2 or 5 or 8 or 11 => 3,
            3 or 6 or 9 or 12 => 4,
            7 => 2,
            45 => 1,
            _ => 0
        };
    }

    private static ImageTexture? ResolvePartTexture(MappedVfsArchive archive, SkinTxtEntry entry, SkinnedMesh mesh)
    {
        ImageTexture? albedo = null;
        try
        {
            albedo = ViewerTextures.ResolveTexId(archive, entry.TextureId);
        }
        catch (Exception)
        {
        }

        if (albedo is not null) return albedo;

        try
        {
            albedo = ViewerTextures.ResolveSkn(archive, mesh).Texture;
        }
        catch (Exception)
        {
        }

        return albedo;
    }

    private static SkinnedMesh? LoadSkn(MappedVfsArchive archive, int meshGid, Dictionary<int, SkinnedMesh?> cache)
    {
        if (cache.TryGetValue(meshGid, out var cached)) return cached;

        SkinnedMesh? mesh = null;
        var path = $"{SkinDirPrefix}{meshGid}.skn";
        if (archive.Contains(path))
            try
            {
                var raw = archive.GetFileContent(path);
                if (!raw.IsEmpty) mesh = SknParser.Parse(raw);
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[Assembler] SknParser.Parse failed '{path}': {ex.Message}");
            }

        cache[meshGid] = mesh;
        return mesh;
    }

    private static SkinTxtEntry? FindDefaultPart(SkinTxtCatalog catalog, int catalogueSlot, int modelClassId)
    {
        if (catalogueSlot < 0) return null;

        SkinTxtEntry? remainderZero = null;
        SkinTxtEntry? anyMatch = null;

        foreach (var e in catalog.Entries)
        {
            if (e.MillionsGroup != catalogueSlot) continue;
            if (e.HundredsGroup != modelClassId) continue;
            if (e.MeshGid <= 0) continue;

            if (anyMatch is null || e.MeshGid < anyMatch.MeshGid)
                anyMatch = e;

            if (e.LowRemainder == 0 && (remainderZero is null || e.MeshGid < remainderZero.MeshGid))
                remainderZero = e;
        }

        return remainderZero ?? anyMatch;
    }

    private static void MergeAabb(ref Aabb combined, ref bool has, Aabb next)
    {
        if (!has)
        {
            combined = next;
            has = true;
        }
        else
        {
            combined = combined.Merge(next);
        }
    }
}