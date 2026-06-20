using System.Text;
using MartialHeroes.Assets.Parsers.Texture.Models;

namespace MartialHeroes.Assets.Parsers.Texture;

/// <summary>
///     Parser for <c>data/map{area}/texture/bgtexture.txt</c> — the plain-text companion to the
///     binary <c>bgtexture.lst</c> background-texture pool. It is the preferred, robust source for
///     the pool-index → texture-filename mapping (the binary GHTex record layout is UNVERIFIED).
/// </summary>
/// <remarks>
///     spec: Docs/RE/formats/terrain.md §4.2 — bgtexture.txt text companion. CONFIRMED (observed).
///     <para>
///         Format (CP949, LF or CRLF lines), one entry per line, TAB-separated, no header:
///         <c>&lt;poolIndex:int&gt; \t &lt;typeFlag:int&gt; \t &lt;relPath&gt;</c>
///         where <c>relPath</c> is the texture path relative to <c>data/map{area}/texture/</c> without the
///         <c>.dds</c> extension (e.g. <c>terrain/g3</c>, <c>building/_castle</c>).
///     </para>
///     <para>ZERO rendering/engine dependencies.</para>
/// </remarks>
public static class BgTextureTxtParser
{
    /// <inheritdoc cref="Parse(ReadOnlySpan{byte})" />
    public static BgTextureCatalog Parse(ReadOnlyMemory<byte> data)
    {
        return Parse(data.Span);
    }

    /// <summary>Parses the raw bytes (CP949) of a <c>bgtexture.txt</c> file.</summary>
    public static BgTextureCatalog Parse(ReadOnlySpan<byte> span)
    {
        // spec: Docs/RE/formats/terrain.md §4.2 — encoding CP949 (no BOM). CONFIRMED.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return ParseText(Encoding.GetEncoding(949).GetString(span));
    }

    /// <summary>Overload accepting pre-decoded text (for testing).</summary>
    public static BgTextureCatalog ParseText(string text)
    {
        var map = new Dictionary<int, string>();

        foreach (var rawLine in text.Split('\n'))
        {
            // spec: Docs/RE/formats/terrain.md §4.2 — "<poolIndex>\t<typeFlag>\t<relPath>". CONFIRMED.
            var line = rawLine.Replace("\r", string.Empty);
            if (line.Length == 0) continue;

            var cols = line.Split('\t');
            if (cols.Length < 3) continue;
            if (!int.TryParse(cols[0].Trim(), out var poolIndex)) continue;

            var rel = cols[2].Trim();
            if (rel.Length == 0) continue;

            map[poolIndex] = rel;
        }

        return new BgTextureCatalog(map);
    }
}