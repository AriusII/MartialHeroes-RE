using Godot;
using MartialHeroes.Assets.Parsers.Character;
using MartialHeroes.Assets.Parsers.Mesh.Models;
using MartialHeroes.Client.Godot.Composition;
using MartialHeroes.Client.Infrastructure.Catalog;

namespace MartialHeroes.Client.Godot.World;

public static class CharacterTextureResolver
{
    private const string SkinTxtPath = "data/char/skin.txt";


    private static Dictionary<uint, uint>? _cachedSkinToTex;
    private static RealClientAssets? _cacheOwner;

    public static CharacterVisualCatalogue? Catalogue { get; set; }

    public static ImageTexture? Resolve(RealClientAssets assets, SkinnedMesh mesh)
    {
        return Resolve(assets, mesh.IdA);
    }

    public static ImageTexture? Resolve(RealClientAssets assets, uint skinIdA)
    {
        if (skinIdA == 0) return null;

        EnsureSkinTxtLoaded(assets);

        uint texId = 0;
        if (_cachedSkinToTex is not null &&
            _cachedSkinToTex.TryGetValue(skinIdA, out var fromTable))
            texId = fromTable;

        if (texId == 0)
        {
            GD.Print($"[CharacterTextureResolver] skin.txt miss for skinIdA={skinIdA} — no texId; " +
                     "rendering neutral (no fabricated id). spec: skinning.md §8(e).");
            return null;
        }

        return FindTextureInVfs(assets, texId, skinIdA);
    }

    private static ImageTexture? FindTextureInVfs(RealClientAssets assets, uint texId, uint skinIdA)
    {
        var entry = Catalogue?.GetTexById((int)texId);
        if (entry is null)
        {
            GD.Print($"[CharacterTextureResolver] texId={texId} absent from tex{{N}}list manifests for " +
                     $"skinIdA={skinIdA} — null (no probe, no fabrication). spec: skinning.md §8(e).");
            return null;
        }

        if (!assets.Contains(entry.VfsPath))
        {
            GD.Print($"[CharacterTextureResolver] texId={texId} → '{entry.VfsPath}' not present in VFS " +
                     $"for skinIdA={skinIdA} — null.");
            return null;
        }

        var tex = assets.LoadTexture(entry.VfsPath);
        if (tex is not null)
            GD.Print($"[CharacterTextureResolver] Resolved skinIdA={skinIdA} → texId={texId} → {entry.VfsPath} " +
                     "(via tex{N}list catalogue).");
        return tex;
    }


    public static string? PickHumanoidPlayerSkin(RealClientAssets assets)
    {
        const string primaryCandidate = "data/char/skin/g202110001.skn";
        if (assets.Contains(primaryCandidate))
        {
            GD.Print(
                $"[CharacterTextureResolver] PickHumanoidPlayerSkin: using confirmed candidate '{primaryCandidate}'.");
            return primaryCandidate;
        }

        GD.Print("[CharacterTextureResolver] PickHumanoidPlayerSkin: primary candidate absent, probing fallbacks.");
        for (var variant = 2; variant <= 20; variant++)
        {
            var candidate = $"data/char/skin/g202110{variant:D3}.skn";
            if (assets.Contains(candidate))
            {
                GD.Print($"[CharacterTextureResolver] PickHumanoidPlayerSkin: fallback to '{candidate}'.");
                return candidate;
            }
        }

        string[] lastResort =
        [
            "data/char/skin/g202220001.skn",
            "data/char/skin/g202130001.skn",
            "data/char/skin/g202140001.skn"
        ];
        foreach (var fallback in lastResort)
            if (assets.Contains(fallback))
            {
                GD.Print($"[CharacterTextureResolver] PickHumanoidPlayerSkin: last-resort fallback to '{fallback}'.");
                return fallback;
            }

        GD.PrintErr("[CharacterTextureResolver] PickHumanoidPlayerSkin: no humanoid skin found in VFS.");
        return null;
    }


    private static void EnsureSkinTxtLoaded(RealClientAssets assets)
    {
        if (_cacheOwner == assets && _cachedSkinToTex is not null)
            return;

        _cacheOwner = assets;
        _cachedSkinToTex = null;

        if (!assets.Contains(SkinTxtPath))
        {
            GD.Print($"[CharacterTextureResolver] '{SkinTxtPath}' absent — skin.txt lookup disabled.");
            _cachedSkinToTex = new Dictionary<uint, uint>(0);
            return;
        }

        try
        {
            var catalogue = SkinTxtParser.Parse(assets.GetRaw(SkinTxtPath));

            var map = new Dictionary<uint, uint>(catalogue.Count);
            foreach (var entry in catalogue.Entries)
            {
                if (entry.MeshGid <= 0 || entry.TextureId <= 0) continue;
                map[(uint)entry.MeshGid] = (uint)entry.TextureId;
            }

            _cachedSkinToTex = map;
            GD.Print($"[CharacterTextureResolver] skin.txt loaded: {map.Count} skinId→texId mappings.");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[CharacterTextureResolver] Failed to parse skin.txt: {ex.Message}");
            _cachedSkinToTex = new Dictionary<uint, uint>(0);
        }
    }
}