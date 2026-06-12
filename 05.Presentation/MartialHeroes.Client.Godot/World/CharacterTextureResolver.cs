// World/CharacterTextureResolver.cs
//
// Static helper: resolves the diffuse ImageTexture for a character skin (.skn) from the VFS,
// and provides a canonical humanoid player skin path for the default avatar.
//
// TEXTURE RESOLUTION — two-step cascade:
//
//   Step 1 — skin.txt authoritative lookup (data/char/skin.txt):
//     skin.txt is a TAB-separated table of 1352 entries (one header count line then N data rows).
//     Each data row has 6 columns:
//       col0  (unused int)
//       col1  (slot index)
//       col2  (class ID)
//       col3  (0)
//       col4  (skinId  — numeric, matches .skn IdA)
//       col5  (texId   — numeric, used to locate the texture file)
//     If col4 matches the skin's IdA and col5 is non-zero, texId = col5.
//     This is the authoritative source: some skins share a single texture (e.g. equipment
//     variants that all use the same base texture 410110000).
//     spec: Probed from real VFS at D:\MartialHeroesClient — see harness
//           mh-tex-probe/Program.cs:  concordant skins confirmed against four tex buckets.
//
//   Step 2 — derivation fallback (prefix substitution):
//     If the skin IdA starts with the digit '2' (9-digit IDs like 2XXXXXXXX), replace the
//     leading '2' with '4' to derive a candidate texId (4XXXXXXXX).
//     This rule is NOT universal (skins whose first digit != 2 cannot use it), but covers the
//     majority of standard character body skins under data/char/skin/g202XXXXXX.skn.
//     Confirmed hits for: g202110001..g202110003 (IdB=1), g202130001..g202130003 (IdB=3), etc.
//     spec: Probed from real VFS — mh-tex-probe/Program.cs cross-class derivation test.
//
//   After obtaining a texId, the VFS is searched across all four texture resolution buckets
//   and both supported extensions (.dds, .png):
//     data/char/tex256256/{texId}.{ext}
//     data/char/tex256512/{texId}.{ext}
//     data/char/tex512512/{texId}.{ext}
//     data/char/tex10241024/{texId}.{ext}
//   The first bucket+extension pair that exists in the VFS wins.
//
// HUMANOID PLAYER DEFAULT:
//   PickHumanoidPlayerSkin() returns "data/char/skin/g202110001.skn":
//   - IdA = 202110001, IdB = 1 (Musa / Swordsman class)
//   - Confirmed in VFS: EXISTS  (checked on real client)
//   - Confirmed skeleton: data/char/bind/g1.bnd  EXISTS
//   - Confirmed texture:  data/char/tex10241024/402110001.png  EXISTS (via skin.txt col5)
//   spec: Probed from real VFS — mh-tex-probe/Program.cs humanoid skin candidates.
//
// CALLING CONVENTION (see wiring notes at file bottom).
//
// spec: PRESERVATION_AND_ARCHITECTURE.md §05.Presentation — strictly passive.
// spec: Docs/RE/formats/texture.md §PNG — character skin textures in data/char/tex*/
// spec: Docs/RE/formats/mesh.md §.skn header (id_a/id_b).

using Godot;
using MartialHeroes.Assets.Parsers.Models;
using MartialHeroes.Client.Godot.Dev;

namespace MartialHeroes.Client.Godot.World;

/// <summary>
/// Pure static helper that resolves the diffuse <see cref="ImageTexture"/> for a character skin
/// and provides a default humanoid player skin VFS path.
///
/// Thread safety: all public methods call into <see cref="RealClientAssets"/> which is
/// main-thread-only — do NOT call from background tasks.
/// </summary>
public static class CharacterTextureResolver
{
    // -------------------------------------------------------------------------
    // Texture resolution
    // -------------------------------------------------------------------------

    // The VFS path to the skin-to-texture mapping table.
    // spec: Probed from real VFS — data/char/skin.txt contains 1352 entries.
    private const string SkinTxtPath = "data/char/skin.txt";

    // Texture bucket directories, ordered from highest to lowest resolution preference.
    // spec: Docs/RE/formats/texture.md §Texture resolution buckets.
    // Searching highest resolution first gives the best visual quality.
    private static readonly string[] TexBuckets =
    [
        "data/char/tex10241024/",
        "data/char/tex512512/",
        "data/char/tex256512/",
        "data/char/tex256256/",
    ];

    // Extensions tried in order for each bucket.
    // spec: Docs/RE/formats/texture.md §PNG §DDS — PNG is the dominant format for character skins;
    //       older DDS variants exist in some buckets for legacy skins.
    private static readonly string[] TexExtensions = [".png", ".dds"];

    // -------------------------------------------------------------------------
    // Per-instance skin.txt cache (keyed by RealClientAssets instance identity)
    // -------------------------------------------------------------------------

    // Caches the parsed skin→tex lookup table so skin.txt is not re-read on every call.
    // Key: skinId (= .skn IdA), Value: texId (= raw numeric from skin.txt col5).
    // Built lazily on first resolve call and reused for all subsequent calls.
    // All state lives here — CharacterTextureResolver itself has no instance state.
    private static Dictionary<uint, uint>? _cachedSkinToTex;
    private static RealClientAssets? _cacheOwner;

    /// <summary>
    /// Resolves the diffuse <see cref="ImageTexture"/> for a character skin given its parsed
    /// <see cref="SkinnedMesh"/>.
    ///
    /// Returns <see langword="null"/> (rather than throwing) when the texture cannot be found —
    /// the caller may then fall back to a neutral material colour.
    ///
    /// <para>Resolution order:</para>
    /// <list type="number">
    ///   <item>Look up IdA in <c>data/char/skin.txt</c> (authoritative texId).</item>
    ///   <item>If not found, derive texId by replacing leading digit '2' with '4' in the IdA
    ///         string (covers the majority of standard 9-digit character skins).</item>
    ///   <item>Search all four tex buckets (1024² first) with both <c>.png</c> and <c>.dds</c>
    ///         extensions; return the first match as a Godot <see cref="ImageTexture"/>.</item>
    /// </list>
    ///
    /// spec: Docs/RE/formats/texture.md §PNG — character skin textures in data/char/tex*/.
    /// spec: Probed skin.txt format on real VFS — see CharacterTextureResolver.cs file header.
    /// </summary>
    /// <param name="assets">Open VFS asset access. Must not be null.</param>
    /// <param name="mesh">Parsed skinned mesh whose <c>IdA</c> is used as the lookup key.</param>
    /// <returns>Resolved texture, or null when unresolvable.</returns>
    public static ImageTexture? Resolve(RealClientAssets assets, SkinnedMesh mesh)
        => Resolve(assets, mesh.IdA);

    /// <summary>
    /// Resolves the diffuse texture directly by skin <c>IdA</c> without needing the full
    /// <see cref="SkinnedMesh"/> object.
    ///
    /// spec: Docs/RE/formats/texture.md §PNG — character skin textures in data/char/tex*/.
    /// </summary>
    /// <param name="assets">Open VFS asset access. Must not be null.</param>
    /// <param name="skinIdA">The IdA field from the .skn header.</param>
    /// <returns>Resolved texture, or null when unresolvable.</returns>
    public static ImageTexture? Resolve(RealClientAssets assets, uint skinIdA)
    {
        if (skinIdA == 0) return null;

        // Ensure the skin.txt table is loaded (lazy, cached per-assets-instance).
        EnsureSkinTxtLoaded(assets);

        // Step 1 — authoritative skin.txt lookup.
        // spec: Probed skin.txt: 6-col format, col4=skinId, col5=texId. CONFIRMED.
        uint texId = 0;
        if (_cachedSkinToTex is not null &&
            _cachedSkinToTex.TryGetValue(skinIdA, out uint fromTable))
        {
            texId = fromTable;
        }

        // Step 2 — derivation fallback: "2XXXXXXXX" → "4XXXXXXXX".
        // spec: Probed on real VFS — prefix-substitution confirmed for g202110001..g202130003.
        if (texId == 0)
        {
            texId = DeriveFallbackTexId(skinIdA);
        }

        if (texId == 0)
        {
            GD.Print($"[CharacterTextureResolver] No texId resolved for skinIdA={skinIdA}.");
            return null;
        }

        return FindTextureInVfs(assets, texId, skinIdA);
    }

    /// <summary>
    /// Searches all four character texture buckets for a texture matching <paramref name="texId"/>,
    /// trying <c>.png</c> then <c>.dds</c> per bucket.
    ///
    /// Returns the loaded <see cref="ImageTexture"/> for the first match, or null.
    /// spec: Docs/RE/formats/texture.md §Texture resolution buckets — 256×256, 256×512,
    ///       512×512, 1024×1024.
    /// </summary>
    private static ImageTexture? FindTextureInVfs(RealClientAssets assets, uint texId, uint skinIdA)
    {
        foreach (string bucket in TexBuckets)
        {
            foreach (string ext in TexExtensions)
            {
                string path = $"{bucket}{texId}{ext}";
                if (!assets.Contains(path)) continue;

                ImageTexture? tex = assets.LoadTexture(path);
                if (tex is not null)
                {
                    GD.Print($"[CharacterTextureResolver] Resolved skinIdA={skinIdA} → texId={texId} → {path}");
                    return tex;
                }
            }
        }

        GD.Print($"[CharacterTextureResolver] texId={texId} not found in any bucket for skinIdA={skinIdA}.");
        return null;
    }

    // -------------------------------------------------------------------------
    // Humanoid player default
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns the VFS path of a confirmed humanoid player-class skin to use as the default
    /// player avatar: <c>data/char/skin/g202110001.skn</c>.
    ///
    /// <para>This skin is:</para>
    /// <list type="bullet">
    ///   <item>IdA = 202110001, IdB = 1 (Musa / Swordsman class)</item>
    ///   <item>Confirmed present in the real VFS at D:\MartialHeroesClient</item>
    ///   <item>Has a confirmed skeleton: <c>data/char/bind/g1.bnd</c></item>
    ///   <item>Has a confirmed texture: <c>data/char/tex10241024/402110001.png</c>
    ///         (via skin.txt authoritative mapping)</item>
    ///   <item>Is a full humanoid mesh (410 faces, 263 vertices — not a spider/NPC proxy)</item>
    /// </list>
    ///
    /// The method first checks that the path actually exists in the open VFS before returning it.
    /// If the skin is absent, it falls back to the first <c>data/char/skin/g202110XXX.skn</c>
    /// entry found by VFS scan. Returns <see langword="null"/> only when no g202110XXX skin
    /// can be found at all.
    ///
    /// spec: Probed on real VFS — g202110001 confirmed present, IdB=1, bnd=EXISTS, tex=EXISTS.
    /// </summary>
    /// <param name="assets">Open VFS asset access. Must not be null.</param>
    /// <returns>VFS path of a humanoid player skin, or null when none is found.</returns>
    public static string? PickHumanoidPlayerSkin(RealClientAssets assets)
    {
        // Primary candidate: confirmed humanoid Musa base skin.
        // spec: Probed real VFS — g202110001 = IdA=202110001, IdB=1, 410 faces, bnd+tex confirmed.
        const string primaryCandidate = "data/char/skin/g202110001.skn";
        if (assets.Contains(primaryCandidate))
        {
            GD.Print(
                $"[CharacterTextureResolver] PickHumanoidPlayerSkin: using confirmed candidate '{primaryCandidate}'.");
            return primaryCandidate;
        }

        // Fallback: probe a small ordered list of g202110XXX.skn candidates directly.
        // RealClientAssets does not expose GetEntries() so a direct span scan is not available
        // here. Probing by Contains() for the first few variants is sufficient for a fallback
        // that only fires when the primary candidate is absent.
        // spec: Probed real VFS — g202110001..g202110003 confirmed present, IdB=1.
        GD.Print("[CharacterTextureResolver] PickHumanoidPlayerSkin: primary candidate absent, probing fallbacks.");
        for (int variant = 2; variant <= 20; variant++)
        {
            string candidate = $"data/char/skin/g202110{variant:D3}.skn";
            if (assets.Contains(candidate))
            {
                GD.Print($"[CharacterTextureResolver] PickHumanoidPlayerSkin: fallback to '{candidate}'.");
                return candidate;
            }
        }

        // Last resort: try other humanoid classes (IdB=2 female Musa, IdB=3 Blader, etc.)
        // spec: Probed real VFS — g202220001 (IdB=2), g202130001 (IdB=3) confirmed present.
        string[] lastResort =
        [
            "data/char/skin/g202220001.skn", // IdB=2 (Tao class)
            "data/char/skin/g202130001.skn", // IdB=3 (Blader class)
            "data/char/skin/g202140001.skn", // IdB=4 (Warrior class)
        ];
        foreach (string fallback in lastResort)
        {
            if (assets.Contains(fallback))
            {
                GD.Print($"[CharacterTextureResolver] PickHumanoidPlayerSkin: last-resort fallback to '{fallback}'.");
                return fallback;
            }
        }

        GD.PrintErr("[CharacterTextureResolver] PickHumanoidPlayerSkin: no humanoid skin found in VFS.");
        return null;
    }

    // -------------------------------------------------------------------------
    // skin.txt lazy loading
    // -------------------------------------------------------------------------

    /// <summary>
    /// Builds the in-memory skinId→texId dictionary from <c>data/char/skin.txt</c> on first
    /// call and caches it. The cache is invalidated if a different <see cref="RealClientAssets"/>
    /// instance is presented (guards against archive re-opens in the same process).
    ///
    /// skin.txt format (TAB-separated):
    ///   Line 1:  integer count of subsequent data rows (discarded by the parser — not trusted)
    ///   Lines 2+: 6-column data rows
    ///     col0  unused int
    ///     col1  slot index
    ///     col2  class ID
    ///     col3  0
    ///     col4  skinId  (= .skn IdA for this slot)
    ///     col5  texId   (= numeric texture ID; 0 = no texture for this slot)
    ///
    /// Rows with col4==0 or col5==0 are skipped (empty slot entries).
    ///
    /// spec: Probed real VFS skin.txt — 1351 6-column rows confirmed.
    ///       Column semantics confirmed by cross-reference with g202110001 (skinId=202110001,
    ///       texId=402110001 resolved to data/char/tex10241024/402110001.png).
    /// </summary>
    private static void EnsureSkinTxtLoaded(RealClientAssets assets)
    {
        // Re-parse if a new assets instance is presented.
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
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
            ReadOnlyMemory<byte> raw = assets.GetRaw(SkinTxtPath);
            // spec: All game text is CP949 (per project brief).
            string text = System.Text.Encoding.GetEncoding(949).GetString(raw.Span);

            var map = new Dictionary<uint, uint>(1400);
            bool firstLine = true;

            foreach (string rawLine in text.Split('\n'))
            {
                string line = rawLine.Trim('\r', '\n', ' ');
                if (line.Length == 0) continue;

                // Skip the first non-empty line (the entry-count header).
                if (firstLine)
                {
                    firstLine = false;
                    continue;
                }

                // All data rows are TAB-separated with exactly 6 columns.
                // spec: Probed real VFS — 1351 6-column rows, zero rows with other column counts.
                string[] cols = line.Split('\t');
                if (cols.Length != 6) continue;

                // col4 = skinId, col5 = texId.
                // Skip zero entries (empty slot placeholders).
                if (!uint.TryParse(cols[4], out uint skinId) || skinId == 0) continue;
                if (!uint.TryParse(cols[5], out uint texId) || texId == 0) continue;

                map[skinId] = texId;
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

    // -------------------------------------------------------------------------
    // Derivation fallback
    // -------------------------------------------------------------------------

    /// <summary>
    /// Derives a candidate texture ID from a skin IdA by replacing the leading digit '2' with '4'.
    /// This rule covers the majority of standard character body skins whose IdA starts with '2'
    /// (9-digit format: 2XX_XXX_XXX → 4XX_XXX_XXX).
    ///
    /// Returns 0 when the rule is not applicable (IdA does not start with '2', or parsing fails).
    ///
    /// spec: Probed real VFS — prefix substitution confirmed for classes IdB=1 (202110001..003),
    ///       IdB=2 (202220001..003), IdB=3 (202130001..003), IdB=4 (202140001..003).
    ///       NOT universal: some skins map to shared textures via skin.txt (e.g. 210110XXX →
    ///       410110000 not 41011XXXX); those are handled exclusively by the skin.txt path.
    /// </summary>
    private static uint DeriveFallbackTexId(uint skinIdA)
    {
        string str = skinIdA.ToString();
        if (str.Length == 0 || str[0] != '2') return 0;
        return uint.TryParse("4" + str[1..], out uint result) ? result : 0u;
    }
}