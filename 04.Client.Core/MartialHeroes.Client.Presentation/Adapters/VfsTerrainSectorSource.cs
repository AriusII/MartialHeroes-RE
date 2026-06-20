using MartialHeroes.Assets.Parsers.Terrain;
using MartialHeroes.Assets.Vfs;
using MartialHeroes.Client.Application.World;

namespace MartialHeroes.Client.Presentation.Adapters;

/// <summary>
///     Implements the <see cref="ITerrainSectorSource" /> port by loading per-cell .ted bytes from the
///     VFS archive via <see cref="MappedVfsArchive" /> + <see cref="LstManifestParser" />. Validates each
///     request against the area .lst manifest (absent key → empty, never throw).
///     Fallback: when no archive is mounted (offline / no LegacyClient assets), every load returns empty
///     memory so the offline run is never broken. Errors during manifest loading are swallowed and treated
///     as "no manifest available".
///     spec: Docs/RE/formats/terrain.md §1.2 (.lst manifest — absent key → never loaded).
///     spec: Docs/RE/formats/terrain.md §1.3 (per-cell base path pattern).
///     spec: Docs/RE/formats/terrain.md §Overview (world origin bias 10000).
/// </summary>
public sealed class VfsTerrainSectorSource : ITerrainSectorSource
{
    // The VFS archive; null when no archive is mounted (offline mode).
    private readonly MappedVfsArchive? _vfs;

    // Area identifier for path construction. Mutable: rebound by SetArea when the player enters a
    // different area so streaming resolves the correct per-area path family.
    // spec: Docs/RE/formats/terrain.md §1.1 areaId digit decomposition.
    private int _areaId;

    // Valid cell keys loaded from the .lst manifest; null when no archive / manifest is absent.
    // Reloaded by SetArea so the manifest always matches the current _areaId.
    // spec: Docs/RE/formats/terrain.md §1.2 — cell key formula: key = mapZ + 100000 * mapX. CONFIRMED.
    private HashSet<uint>? _manifestKeys;

    /// <summary>
    ///     Creates a VFS-backed sector source for the given area. Loads and caches the .lst manifest
    ///     on construction so every <see cref="LoadSectorAsync" /> is a pure key-check + VFS read.
    /// </summary>
    /// <param name="vfs">
    ///     The mounted VFS archive, or <see langword="null" /> for offline/test mode (all loads return empty).
    /// </param>
    /// <param name="areaId">
    ///     The area identifier. spec: Docs/RE/formats/terrain.md §1.1.
    /// </param>
    public VfsTerrainSectorSource(MappedVfsArchive? vfs, int areaId)
    {
        _vfs = vfs;
        _areaId = areaId;
        _manifestKeys = vfs is not null ? TryLoadManifest(vfs, areaId) : null;
    }

    /// <inheritdoc />
    /// <remarks>
    ///     Rebinds the active area and reloads its <c>.lst</c> manifest in one shot, so subsequent loads
    ///     resolve <c>data/map{tag}/dat/...</c> for the new area and validate against its manifest. A
    ///     no-op when the area is unchanged. When no archive is mounted (offline), only the id is updated
    ///     (loads still return empty). spec: terrain.md §1.1 (path tag) + §1.2 (manifest).
    /// </remarks>
    public void SetArea(int areaId)
    {
        if (areaId == _areaId &&
            _manifestKeys is not null) return; // already bound to this area with a loaded manifest.

        _areaId = areaId;
        _manifestKeys = _vfs is not null ? TryLoadManifest(_vfs, areaId) : null;
    }

    /// <inheritdoc />
    /// <remarks>
    ///     Returns <see cref="ReadOnlyMemory{byte}.Empty" /> when:
    ///     - no VFS is mounted (offline mode),
    ///     - the cell key is absent from the area manifest,
    ///     - the .ted file is absent from the archive,
    ///     - any I/O or parse error occurs.
    ///     spec: Docs/RE/formats/terrain.md §1.2 (absent key → never loaded).
    /// </remarks>
    public ValueTask<ReadOnlyMemory<byte>> LoadSectorAsync(
        int mapX,
        int mapZ,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Offline / no archive → return empty.
        if (_vfs is null || _manifestKeys is null) return ValueTask.FromResult(ReadOnlyMemory<byte>.Empty);

        // Validate against the area manifest.
        // spec: Docs/RE/formats/terrain.md §1.2 — "absent from manifest → never loaded".
        // Key formula: key = mapZ + 100000 * mapX. CONFIRMED.
        // spec: Docs/RE/formats/terrain.md §1.2 — "key = mapZ + 100000 * mapX": CONFIRMED.
        var key = LstManifestParser.ComputeKey(mapX, mapZ);
        if (!_manifestKeys.Contains(key)) return ValueTask.FromResult(ReadOnlyMemory<byte>.Empty);

        // Construct the per-cell .ted VFS path.
        // spec: Docs/RE/formats/terrain.md §1.3 per-cell base path pattern + .ted suffix.
        var tedPath = BuildTedPath(_areaId, mapX, mapZ);

        var bytes = TryGetContent(_vfs, tedPath);
        return ValueTask.FromResult(bytes);
    }

    // -------------------------------------------------------------------------
    // Path construction helpers
    // spec: Docs/RE/formats/terrain.md §1.1 (digit decomposition).
    // spec: Docs/RE/formats/terrain.md §1.2 (manifest path).
    // spec: Docs/RE/formats/terrain.md §1.3 (per-cell base path).
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Builds the VFS path for the primary .ted blob of a cell.
    ///     spec: Docs/RE/formats/terrain.md §1.3 — "data/map&lt;d0d1d2&gt;/dat/d&lt;d0d1d2&gt;x&lt;mapX&gt;z&lt;mapZ&gt;.ted".
    /// </summary>
    private static string BuildTedPath(int areaId, int mapX, int mapZ)
    {
        // spec: Docs/RE/formats/terrain.md §1.1 — digit decomposition.
        var areaTag = AreaTag(areaId);
        // spec: Docs/RE/formats/terrain.md §1.3 — per-cell base path + .ted for TERRAIN section.
        return $"data/map{areaTag}/dat/d{areaTag}x{mapX}z{mapZ}.ted";
    }

    /// <summary>
    ///     Builds the VFS path for the .lst manifest of an area.
    ///     spec: Docs/RE/formats/terrain.md §1.2 — "data/map&lt;d0d1d2&gt;/dat/d&lt;d0d1d2&gt;.lst".
    /// </summary>
    private static string BuildLstPath(int areaId)
    {
        var areaTag = AreaTag(areaId);
        // spec: Docs/RE/formats/terrain.md §1.2 — manifest path pattern.
        return $"data/map{areaTag}/dat/d{areaTag}.lst";
    }

    /// <summary>
    ///     Returns the zero-padded 3-digit area tag (d0d1d2).
    ///     spec: Docs/RE/formats/terrain.md §1.1 — d0 = areaId/100, d1 = (areaId/10)%10, d2 = areaId%10.
    /// </summary>
    private static string AreaTag(int areaId)
    {
        // spec: Docs/RE/formats/terrain.md §1.1 — digit decomposition: CONFIRMED.
        var d0 = areaId / 100;
        var d1 = areaId / 10 % 10;
        var d2 = areaId % 10;
        return $"{d0}{d1}{d2}";
    }

    // -------------------------------------------------------------------------
    // VFS helpers
    // -------------------------------------------------------------------------

    private static HashSet<uint>? TryLoadManifest(MappedVfsArchive vfs, int areaId)
    {
        var lstPath = BuildLstPath(areaId);

        try
        {
            if (!vfs.Contains(lstPath)) return null;

            var data = vfs.GetFileContent(lstPath);
            if (data.IsEmpty) return null;

            // Parse the .lst manifest and build a fast lookup set.
            // spec: Docs/RE/formats/terrain.md §1.2 — "count u32le | keys u32le[]": CONFIRMED.
            var manifest = LstManifestParser.Parse(data);
            var keys = new HashSet<uint>(manifest.Entries.Length);
            foreach (var entry in manifest.Entries) keys.Add(entry.Key);

            return keys;
        }
        catch (Exception)
        {
            // Manifest absent or corrupt — fall back to "no manifest" mode (all loads return empty).
            return null;
        }
    }

    private static ReadOnlyMemory<byte> TryGetContent(MappedVfsArchive vfs, string path)
    {
        try
        {
            if (!vfs.Contains(path)) return ReadOnlyMemory<byte>.Empty;

            return vfs.GetFileContent(path);
        }
        catch (Exception)
        {
            return ReadOnlyMemory<byte>.Empty;
        }
    }
}