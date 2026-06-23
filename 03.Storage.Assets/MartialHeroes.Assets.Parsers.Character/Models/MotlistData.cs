namespace MartialHeroes.Assets.Parsers.Character.Models;

/// <summary>
///     Decoded result of <c>data/char/motlist.txt</c> — the startup <c>.mot</c> registry the client
///     reads once and registers each named clip relative to <c>data/char/mot/</c>.
/// </summary>
/// <remarks>
///     The faithful client (<c>MotList_LoadAndRegister</c>) reads each bare <c>.mot</c> filename, opens
///     <c>data/char/mot/</c> + filename, parses the clip and registers it in a public motion-id map keyed
///     by the clip header <c>id_b</c> (<c>clip[18]</c>). There is NO <c>g{id}.mot</c> sprintf rule — the
///     filename is whatever is listed here; the runtime resolves a clip by its header <c>id_b</c>.
///     <para>
///         File structure: CP949, newline-delimited bare <c>.mot</c> filenames, NO count prefix
///         (verified: the first line is <c>g1.mot</c>).
///     </para>
///     <para>
///         This model carries only the ordered filename list (engine-free). The <c>id_b → path</c> map
///         requires reading each <c>.mot</c> header (the <c>.mot</c> parser lives in the mesh-parser
///         project) and is built by the layer-05 composition root.
///     </para>
///     <para>ZERO rendering/engine dependencies.</para>
/// </remarks>
public sealed class MotlistData
{
    /// <summary>The literal VFS directory prefix every motlist entry resolves under. spec: MotList_LoadAndRegister.</summary>
    public const string MotDirPrefix = "data/char/mot/";

    private readonly string[] _entries;

    internal MotlistData(string[] entries)
    {
        _entries = entries;
    }

    /// <summary>Total count of registered <c>.mot</c> filenames.</summary>
    public int Count => _entries.Length;

    /// <summary>
    ///     All registered bare <c>.mot</c> filenames in on-disk order. The full VFS path of an entry is
    ///     <see cref="MotDirPrefix" /> + filename.
    /// </summary>
    public IReadOnlyList<string> Entries => _entries;
}
