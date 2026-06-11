using MartialHeroes.Assets.Parsers;
using MartialHeroes.Assets.Parsers.Models;
using MartialHeroes.Assets.Vfs;
using MartialHeroes.Client.Application.UseCases;
using MartialHeroes.Client.Domain.Stats;

namespace MartialHeroes.Client.Godot.Adapters;

/// <summary>
/// Implements <see cref="IStatCatalogueSource"/> by parsing <c>data/script/userlevel.scr</c> from
/// the VFS via <see cref="ConfigTableParser.ParseUserLevelScr"/>. The per-level body bytes are
/// UNVERIFIED in layout (spec §2.4); we return <see cref="StatBaseCurve.Empty"/> for the stat curves
/// until the exact byte offsets of HP/MP bases within the 58-byte body are confirmed.
///
/// Fallback: when no archive is mounted, or the file is absent, or parsing fails, falls back to
/// <see cref="EmptyStatCatalogueSource"/> so the offline run is never broken.
///
/// spec: Docs/RE/formats/config_tables.md §2.4 userlevel.scr (stride 60, level u16 @ +0,
///       body 58 bytes @ +2 with stat layout UNVERIFIED).
/// </summary>
internal sealed class ScrStatCatalogueSource : IStatCatalogueSource
{
    // VFS path for the user-level stat catalogue.
    // spec: Docs/RE/formats/config_tables.md §2.4 — "data/script/userlevel.scr".
    private const string UserLevelScrPath = "data/script/userlevel.scr"; // spec: config_tables.md §2.4

    private readonly IStatCatalogueSource _inner;

    /// <summary>
    /// Creates a source backed by <paramref name="vfs"/>. If the VFS is null, absent, or if
    /// <c>userlevel.scr</c> is not present, a silent fallback to
    /// <see cref="EmptyStatCatalogueSource"/> is used.
    /// </summary>
    /// <param name="vfs">The mounted VFS archive, or null for offline mode.</param>
    public ScrStatCatalogueSource(MappedVfsArchive? vfs)
    {
        _inner = TryBuildFromVfs(vfs) ?? EmptyStatCatalogueSource.Instance;
    }

    /// <inheritdoc />
    public StatBaseCurve GetHpBaseCurve() => _inner.GetHpBaseCurve();

    /// <inheritdoc />
    public StatBaseCurve GetMpBaseCurve() => _inner.GetMpBaseCurve();

    // -------------------------------------------------------------------------
    // Construction helpers
    // -------------------------------------------------------------------------

    private static IStatCatalogueSource? TryBuildFromVfs(MappedVfsArchive? vfs)
    {
        if (vfs is null)
        {
            return null;
        }

        try
        {
            if (!vfs.Contains(UserLevelScrPath))
            {
                return null;
            }

            ReadOnlyMemory<byte> data = vfs.GetFileContent(UserLevelScrPath);
            if (data.IsEmpty)
            {
                return null;
            }

            // Parse the per-level records. Stride 60, no header.
            // spec: Docs/RE/formats/config_tables.md §2.4.
            LevelBaseEntry[] entries = ConfigTableParser.ParseUserLevelScr(data);

            // The stat body layout at +2 is UNVERIFIED (spec §2.4 Known unknowns #2).
            // Until the exact HP/MP byte offsets within the 58-byte body are confirmed by a
            // spec author, we cannot reliably extract the base curves.
            // TODO: Once spec §2.4 is updated with confirmed HP/MP offsets within the body,
            //       parse those fields here and build real StatBaseCurve arrays.
            //       For now we return a parsed-but-not-yet-usable source that still falls through
            //       to EmptyStatCatalogueSource (no actual stat curve values are extracted).
            _ = entries; // parsed but body layout unverified — intentional placeholder.

            return null; // falls through to EmptyStatCatalogueSource until layout is confirmed.
        }
        catch (Exception)
        {
            // Parse error → silent fallback.
            return null;
        }
    }
}