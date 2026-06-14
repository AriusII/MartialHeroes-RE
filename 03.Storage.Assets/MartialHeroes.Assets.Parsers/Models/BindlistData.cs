namespace MartialHeroes.Assets.Parsers.Models;

/// <summary>
/// Decoded result of <c>data/char/bindlist.txt</c> — the authoritative startup skeleton registry.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/bindlist.md — "startup skeleton registry";
///   "A skeleton that is not listed here is not registered even if a g{N}.bnd file physically
///    exists in the VFS.": CONFIRMED.
/// <para>
/// The file is a single-column, no-header CRLF-delimited list of bare <c>.bnd</c> filenames.
/// The final line has NO trailing CRLF.  Total: 349 entries.
/// spec: Docs/RE/formats/bindlist.md §File structure: CONFIRMED.
/// </para>
/// <para>ZERO rendering/engine dependencies.</para>
/// </remarks>
public sealed class BindlistData
{
    private readonly string[] _entries;
    private readonly HashSet<string> _lookup;

    internal BindlistData(string[] entries)
    {
        _entries = entries;
        // Case-insensitive lookup — filenames are ASCII but be defensive.
        // spec: Docs/RE/formats/bindlist.md §Identification — "ASCII only": CONFIRMED.
        _lookup = new HashSet<string>(entries, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Total count of registered skeleton filenames.
    /// The reference client contains exactly 349 entries.
    /// spec: Docs/RE/formats/bindlist.md §Entry count — "Total entries: 349": CONFIRMED.
    /// </summary>
    public int Count => _entries.Length;

    /// <summary>
    /// All registered <c>.bnd</c> filenames in their on-disk order (0-based).
    /// spec: Docs/RE/formats/bindlist.md §File structure — "ordered list of filenames": CONFIRMED.
    /// </summary>
    public IReadOnlyList<string> Entries => _entries;

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="bndFilename"/> appears in the registry.
    /// </summary>
    /// <param name="bndFilename">
    /// Bare filename including extension, e.g. <c>"g1.bnd"</c>.
    /// Comparison is case-insensitive (filenames are ASCII).
    /// spec: Docs/RE/formats/bindlist.md — "each line is exactly one bare .bnd filename": CONFIRMED.
    /// </param>
    public bool IsRegistered(string bndFilename) => _lookup.Contains(bndFilename);
}
