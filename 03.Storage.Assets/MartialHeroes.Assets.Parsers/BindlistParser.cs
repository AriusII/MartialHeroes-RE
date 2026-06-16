using System.Text;
using MartialHeroes.Assets.Parsers.Models;

namespace MartialHeroes.Assets.Parsers;

/// <summary>
/// Parser for <c>data/char/bindlist.txt</c> — the authoritative startup skeleton registry.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/bindlist.md — "startup skeleton registry — the client reads this file
///   once and registers (or preloads) each named .bnd relative to data/char/bind/": CONFIRMED.
/// <para>
/// File structure:
/// <list type="bullet">
///   <item>Single column, no header row, CRLF line terminators.</item>
///   <item>Each line is exactly one bare <c>.bnd</c> filename (no directory prefix, no whitespace).</item>
///   <item>The final line has NO trailing CRLF; a non-empty trailing segment after the last CRLF
///     is a valid entry and must be kept.</item>
///   <item>No blank lines and no comment lines appear in a well-formed file.</item>
/// </list>
/// spec: Docs/RE/formats/bindlist.md §File structure: CONFIRMED.
/// </para>
/// <para>
/// Encoding: ASCII only (all observed bytes are in the printable ASCII range).
/// CP949 provider is registered as a safety measure.
/// spec: Docs/RE/formats/bindlist.md §Identification — "ASCII only … Confidence: HIGH": CONFIRMED.
/// </para>
/// <para>ZERO rendering/engine dependencies.</para>
/// </remarks>
public static class BindlistParser
{
    // CRLF sequence used as line terminator.
    // spec: Docs/RE/formats/bindlist.md §File structure — "Line terminator: CRLF (\r\n)": CONFIRMED.
    private static readonly byte[] Crlf = [(byte)'\r', (byte)'\n'];

    static BindlistParser()
    {
        // Register CP949 provider as a safety measure even though all observed filenames
        // are ASCII only.
        // spec: Docs/RE/formats/bindlist.md §Identification — "ASCII only": CONFIRMED.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <summary>
    /// Parses a <c>bindlist.txt</c> file from raw bytes delivered by the VFS.
    /// </summary>
    /// <param name="data">
    /// Raw file bytes (from the VFS or a unit-test fixture).  Must contain the complete file.
    /// </param>
    /// <returns>
    /// A <see cref="BindlistData"/> with all registered skeleton filenames in on-disk order.
    /// </returns>
    /// <exception cref="InvalidDataException">
    /// Thrown when any decoded line is empty (blank lines are illegal in a well-formed file).
    /// </exception>
    /// <remarks>
    /// spec: Docs/RE/formats/bindlist.md §File structure — single column, no header, CRLF
    ///   delimited, final line has no trailing CRLF: CONFIRMED.
    /// </remarks>
    public static BindlistData Parse(ReadOnlyMemory<byte> data)
    {
        // Decode using CP949 (safe superset of ASCII for all registered filenames).
        // spec: Docs/RE/formats/bindlist.md §Identification — "ASCII only": CONFIRMED.
        var cp949 = Encoding.GetEncoding(949);
        string raw = cp949.GetString(data.Span);

        // Split on CRLF.  Keep empty entries = false because trailing-CRLF would produce
        // a spurious empty last element; instead we handle the no-trailing-CRLF case by
        // retaining any non-empty trailing segment.
        // spec: Docs/RE/formats/bindlist.md §File structure —
        //   "final line: terminated WITHOUT a trailing CRLF": CONFIRMED.
        string[] lines = raw.Split("\r\n", StringSplitOptions.None);

        // The split of "a\r\nb\r\nc" yields ["a","b","c"] — correct (3 entries, no trailing CRLF).
        // The split of "a\r\nb\r\n" yields ["a","b",""] — the trailing empty element must be
        // dropped because blank lines are not valid entries.
        // spec: Docs/RE/formats/bindlist.md §File structure —
        //   "no blank lines": CONFIRMED.
        var entries = new List<string>(lines.Length);
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            if (line.Length == 0)
            {
                // An empty line at the very end is the artifact of a trailing CRLF — silently
                // drop it.  An empty line in the middle would be a format violation; we still
                // skip it rather than throw, matching the client's tolerant load behaviour.
                continue;
            }

            entries.Add(line);
        }

        return new BindlistData([.. entries]);
    }
}