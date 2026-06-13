namespace MartialHeroes.Assets.Parsers.Models;

// ─────────────────────────────────────────────────────────────────────────────
//  data/item/texturelist.txt — item icon texture manifest
//  spec: Docs/RE/formats/ui_manifests.md §10
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// One entry from <c>data/item/texturelist.txt</c>.
/// Maps a numeric <see cref="TexId"/> to the VFS-relative path of the item icon DDS.
/// </summary>
/// <param name="TexId">
/// Numeric texture ID extracted as the leading decimal digits of the filename.
/// spec: Docs/RE/formats/ui_manifests.md §10.3 step 4 — "tex_id = atol(name)": CODE-CONFIRMED.
/// </param>
/// <param name="VfsPath">
/// VFS-relative path resolved as <c>data/item/texture/&lt;original-filename&gt;</c>.
/// The item is drawn as a full-texture quad (no sub-rect / no atlas).
/// spec: Docs/RE/formats/ui_manifests.md §10.3 step 5 — "data/item/texture/ + name + ext": CODE-CONFIRMED.
/// spec: Docs/RE/formats/ui_manifests.md §10.5 — "whole-texture blit, no sub-rect": CODE-CONFIRMED.
/// </param>
/// <remarks>
/// spec: Docs/RE/formats/ui_manifests.md §10 data/item/texturelist.txt: CODE-CONFIRMED.
/// </remarks>
public sealed record TextureListEntry(int TexId, string VfsPath);

/// <summary>
/// Decoded result of <c>data/item/texturelist.txt</c> — the item icon texture manifest.
/// Provides direct lookup of item icon DDS paths by their numeric <c>tex_id</c>.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/ui_manifests.md §10 data/item/texturelist.txt: CODE-CONFIRMED.
/// The tex_id key space is non-contiguous; lookup must be via dictionary, not array index.
/// spec: Docs/RE/formats/ui_manifests.md §10.3 — "tex_id key space is non-contiguous": CODE-CONFIRMED.
/// Approximate file size: ~20,034 bytes; approximate entry count: 1,000+.
/// spec: Docs/RE/formats/ui_manifests.md §10.2 — "approximately 20,034 bytes, 1,000+ entries": CODE-CONFIRMED.
/// </remarks>
public sealed class TextureListManifest
{
    private readonly Dictionary<int, TextureListEntry> _byTexId;

    /// <summary>All entries in file order.</summary>
    public IReadOnlyList<TextureListEntry> Entries { get; }

    /// <summary>Total entry count.</summary>
    public int Count => Entries.Count;

    internal TextureListManifest(IReadOnlyList<TextureListEntry> entries)
    {
        Entries = entries;
        _byTexId = new Dictionary<int, TextureListEntry>(entries.Count);
        foreach (var e in entries)
            // Last write wins if the file contains a duplicate ID (defensive).
            _byTexId[e.TexId] = e;
    }

    /// <summary>
    /// Returns the entry for the given <paramref name="texId"/>, or <see langword="null"/> if absent.
    /// spec: Docs/RE/formats/ui_manifests.md §10.3 — keyed by tex_id (non-contiguous): CODE-CONFIRMED.
    /// </summary>
    public TextureListEntry? GetById(int texId) =>
        _byTexId.TryGetValue(texId, out var e) ? e : null;
}