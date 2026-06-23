using System.Text;
using MartialHeroes.Assets.Parsers.Character.Models;

namespace MartialHeroes.Assets.Parsers.Character;

/// <summary>
///     Parser for <c>data/char/motlist.txt</c> — the startup <c>.mot</c> registry.
/// </summary>
/// <remarks>
///     Mirrors <c>MotList_LoadAndRegister</c>: CP949, newline-delimited bare <c>.mot</c> filenames, NO
///     count prefix (verified: the first line is <c>g1.mot</c>). The literal directory prefix is
///     <c>data/char/mot/</c> (see <see cref="MotlistData.MotDirPrefix" />); there is no <c>g%d</c>
///     filename formatting — the runtime registers each clip by its header <c>id_b</c>, not by a derived
///     numeric filename.
///     <para>ZERO rendering/engine dependencies.</para>
/// </remarks>
public static class MotlistParser
{
    static MotlistParser()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <summary>Parses the raw CP949 bytes of <c>motlist.txt</c> into a <see cref="MotlistData" />.</summary>
    public static MotlistData Parse(ReadOnlyMemory<byte> data)
    {
        var text = Encoding.GetEncoding(949).GetString(data.Span);
        return ParseText(text);
    }

    /// <summary>Overload accepting pre-decoded text (for testing and diagnostics).</summary>
    public static MotlistData ParseText(string text)
    {
        var lines = text.Split('\n');
        var entries = new List<string>(lines.Length);
        foreach (var rawLine in lines)
        {
            // Trim CR/whitespace; keep only bare .mot filenames (no count prefix line).
            var line = rawLine.Trim('\r', '\n', ' ', '\t');
            if (line.Length == 0) continue;
            if (!line.EndsWith(".mot", StringComparison.OrdinalIgnoreCase)) continue;
            entries.Add(line);
        }

        return new MotlistData([.. entries]);
    }
}
