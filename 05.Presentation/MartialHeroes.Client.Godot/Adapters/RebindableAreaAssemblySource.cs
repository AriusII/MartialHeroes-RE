using MartialHeroes.Assets.Mapping;
using MartialHeroes.Assets.Parsers.Models;
using MartialHeroes.Assets.Vfs;

namespace MartialHeroes.Client.Godot.Adapters;

/// <summary>
/// Layer-05 adapter: a mutable wrapper around <see cref="VfsAreaAssemblySource"/> that lets the
/// composition root <c>ClientContext</c> rebind the active area when <see cref="SetArea"/> is
/// called (e.g. when <see cref="World.RealWorldRenderer"/> resolves the configured area from
/// <c>client_dir.cfg</c>).
///
/// This solves the Phase 2-B.1 area-rebind bug: the original code hard-coded <c>areaId: 0</c> at
/// construction time. When the world loads area 2 the streaming service was correctly rebound via
/// <see cref="MartialHeroes.Client.Application.World.SectorStreamingService.SetArea"/>, but the
/// <see cref="VfsAreaAssemblySource"/> inside <c>CellAssemblyHandoff</c> kept building
/// <c>data/map000/dat/…</c> paths (wrong area) → <c>.map</c> open failed → <c>ComposeCell</c>
/// early-exited with no slots → zero geometry.
///
/// The <see cref="CellBake"/> lambda in <c>ClientContext</c> captures THIS wrapper, not the inner
/// <see cref="VfsAreaAssemblySource"/>. Calling <see cref="SetArea"/> atomically swaps the inner
/// source so the next <c>ComposeCell</c> call uses the correct area.
///
/// Threading: <see cref="SetArea"/> is called on the Godot main thread (from
/// <c>RealWorldRenderer.TriggerTerrainStreaming</c> which is itself synchronous on the main
/// thread). The <c>CellBake</c> callback is also invoked on the main-thread drain loop
/// (<c>_Process</c>). No cross-thread access — no lock needed.
///
/// spec: Docs/RE/specs/assembly_graph.md §1/§4 — IAreaAssemblySource contract.
/// spec: Docs/RE/formats/area_inventory.md §1A — area → cell fan-out.
/// spec: Docs/RE/formats/terrain.md §1.1 — areaId digit decomposition.
/// </summary>
public sealed class RebindableAreaAssemblySource : IAreaAssemblySource
{
    private readonly MappedVfsArchive _vfs;
    private VfsAreaAssemblySource _inner;

    /// <summary>
    /// Creates a rebindable wrapper starting at <paramref name="initialAreaId"/>.
    /// </summary>
    /// <param name="vfs">The mounted VFS archive (owned by the caller — not disposed here).</param>
    /// <param name="initialAreaId">
    /// The initial area to bind. Will be rebbound via <see cref="SetArea"/> when the renderer
    /// resolves the configured area. spec: Docs/RE/formats/terrain.md §1.1.
    /// </param>
    public RebindableAreaAssemblySource(MappedVfsArchive vfs, int initialAreaId)
    {
        _vfs = vfs ?? throw new ArgumentNullException(nameof(vfs));
        _inner = new VfsAreaAssemblySource(vfs, initialAreaId);
    }

    // ── IAreaAssemblySource — delegate to the inner source ─────────────────────

    /// <inheritdoc/>
    public int AreaId => _inner.AreaId;

    /// <inheritdoc/>
    public IReadOnlyCollection<(int MapX, int MapZ)> AreaCellKeys => _inner.AreaCellKeys;

    /// <inheritdoc/>
    public BgtextureLstCatalog TerrainTextureCatalog => _inner.TerrainTextureCatalog;

    /// <inheritdoc/>
    public bool TryGetCellFile(int mapX, int mapZ, string extension, out ReadOnlyMemory<byte> bytes)
        => _inner.TryGetCellFile(mapX, mapZ, extension, out bytes);

    /// <inheritdoc/>
    public bool TryGetCellFileByName(string vfsLogicalPath, out ReadOnlyMemory<byte> bytes)
        => _inner.TryGetCellFileByName(vfsLogicalPath, out bytes);

    // ── Rebind ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Rebinds the active area. Constructs a new <see cref="VfsAreaAssemblySource"/> for
    /// <paramref name="areaId"/> (reloads the .lst cell-key set) and swaps it as the delegate.
    /// A no-op when the area is already bound.
    /// spec: Docs/RE/formats/terrain.md §1.1 (per-area path tag) + §1.2 (per-area manifest).
    /// spec: Docs/RE/specs/assembly_graph.md §1 — area id → d&lt;NNN&gt;.lst cell-key set.
    /// </summary>
    /// <param name="areaId">The area identifier to bind to.</param>
    public void SetArea(int areaId)
    {
        if (areaId == _inner.AreaId) return; // already bound — no-op.
        _inner = new VfsAreaAssemblySource(_vfs, areaId);
    }
}