namespace MartialHeroes.Assets.Parsers.Models;

/// <summary>
/// Identifies which sub-block of <c>UiTex.txt</c> an entry came from.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/ui_manifests.md §1.2 — DDS sub-block (main textures): PARSER-CONFIRMED;
/// MSK sub-block (mask textures, empty in observed version): PARSER-CONFIRMED keyword, PROPOSED semantics.
/// </remarks>
public enum UiTexBlockKind
{
    /// <summary>
    /// Main UI texture entry (DDS sub-block).
    /// spec: Docs/RE/formats/ui_manifests.md §1.2 — "DDS": PARSER-CONFIRMED.
    /// </summary>
    Dds,

    /// <summary>
    /// Mask texture entry (MSK sub-block).
    /// spec: Docs/RE/formats/ui_manifests.md §1.6 — "MSK": PARSER-CONFIRMED keyword; semantics PROPOSED.
    /// The block is empty in the observed version.
    /// </summary>
    Msk,
}

/// <summary>
/// One entry from a <c>UiTex.txt</c> braced-block manifest.
/// </summary>
/// <param name="TexId">
/// 4-digit zero-padded integer handle used by the legacy UI widget system.
/// spec: Docs/RE/formats/ui_manifests.md §1.3 — "tex_id: parsed as signed integer": PARSER-CONFIRMED.
/// </param>
/// <param name="VfsPath">
/// VFS-relative path, e.g. <c>data/ui/mainwindow.dds</c>.
/// spec: Docs/RE/formats/ui_manifests.md §1.3 — "vfs_path: quoted string": PARSER-CONFIRMED.
/// </param>
/// <param name="BlockKind">
/// Identifies whether this entry came from the <c>DDS</c> or <c>MSK</c> sub-block.
/// spec: Docs/RE/formats/ui_manifests.md §1.2: PARSER-CONFIRMED.
/// </param>
/// <remarks>
/// spec: Docs/RE/formats/ui_manifests.md §1.3 Entry record layout: PARSER-CONFIRMED grammar;
/// SAMPLE-VERIFIED content (35 confirmed entries).
/// </remarks>
public sealed record UiTexEntry(int TexId, string VfsPath, UiTexBlockKind BlockKind);

/// <summary>
/// Decoded result of a <c>UiTex.txt</c> file — all DDS and MSK entries.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/ui_manifests.md §1 data/ui/UiTex.txt.
/// The <see cref="DdsEntries"/> list corresponds to the <c>DDS { }</c> sub-block;
/// <see cref="MskEntries"/> to the <c>MSK { }</c> sub-block (empty in observed version).
/// </remarks>
public sealed class UiTexManifest
{
    private readonly Dictionary<int, UiTexEntry> _byId;

    /// <summary>All entries from the <c>DDS</c> sub-block.</summary>
    public IReadOnlyList<UiTexEntry> DdsEntries { get; }

    /// <summary>All entries from the <c>MSK</c> sub-block (empty in the observed version).</summary>
    public IReadOnlyList<UiTexEntry> MskEntries { get; }

    /// <summary>Total entry count across both blocks.</summary>
    public int Count => DdsEntries.Count + MskEntries.Count;

    internal UiTexManifest(IReadOnlyList<UiTexEntry> dds, IReadOnlyList<UiTexEntry> msk)
    {
        DdsEntries = dds;
        MskEntries = msk;

        _byId = new Dictionary<int, UiTexEntry>(dds.Count + msk.Count);
        foreach (var e in dds) _byId[e.TexId] = e;
        foreach (var e in msk) _byId[e.TexId] = e;
    }

    /// <summary>
    /// Returns the entry for the given <paramref name="texId"/>, or <see langword="null"/> if absent.
    /// spec: Docs/RE/formats/ui_manifests.md §1.1 — "tex_id handle passed to widget bind calls": PARSER-CONFIRMED.
    /// </summary>
    public UiTexEntry? GetById(int texId) =>
        _byId.TryGetValue(texId, out UiTexEntry? e) ? e : null;
}