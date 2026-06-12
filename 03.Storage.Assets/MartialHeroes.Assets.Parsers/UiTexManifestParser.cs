using System.Text;
using MartialHeroes.Assets.Parsers.Models;

namespace MartialHeroes.Assets.Parsers;

/// <summary>
/// Parser for <c>data/ui/UiTex.txt</c> — the primary UI texture registry.
/// Braced-block grammar: <c>UI_TEXTURE { DDS { &lt;id&gt; "&lt;path&gt;" … } MSK { … } }</c>.
/// Lines starting with <c>#</c> are comments and are skipped. CP949 encoding.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/ui_manifests.md §1 data/ui/UiTex.txt.
/// Outer keyword: <c>UI_TEXTURE</c> — PARSER-CONFIRMED.
/// Sub-blocks: <c>DDS</c> and <c>MSK</c> — PARSER-CONFIRMED.
/// Entry layout: integer tex_id then quoted vfs_path — PARSER-CONFIRMED.
/// Missing-closing-quote tolerance: per spec §1.3 quoting caveat (entry 0029).
/// ZERO rendering/engine dependencies.
/// </remarks>
public static class UiTexManifestParser
{
    // Block-level keywords.
    // spec: Docs/RE/formats/ui_manifests.md §1.2 — "UI_TEXTURE" outer keyword: PARSER-CONFIRMED.
    private const string KwUiTexture = "UI_TEXTURE";

    // spec: Docs/RE/formats/ui_manifests.md §1.2 — "DDS" sub-block: PARSER-CONFIRMED.
    private const string KwDds = "DDS";

    // spec: Docs/RE/formats/ui_manifests.md §1.6 — "MSK" sub-block: PARSER-CONFIRMED.
    private const string KwMsk = "MSK";

    // Register CP949 once per AppDomain.
    // spec: Docs/RE/formats/ui_manifests.md §1 — "CP949 for all string fields": PARSER-CONFIRMED.
    static UiTexManifestParser() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    /// <inheritdoc cref="Parse(ReadOnlySpan{byte})"/>
    public static UiTexManifest Parse(ReadOnlyMemory<byte> data) => Parse(data.Span);

    /// <summary>
    /// Parses the raw CP949 bytes of a <c>UiTex.txt</c> file.
    /// </summary>
    /// <param name="span">Raw bytes of <c>data/ui/UiTex.txt</c>.</param>
    /// <returns>A <see cref="UiTexManifest"/> with all DDS and MSK entries.</returns>
    /// <remarks>
    /// spec: Docs/RE/formats/ui_manifests.md §1.2 — CP949, CRLF, '#' comment-skip,
    /// whitespace-delimited tokens, '{{' and '}}' block delimiters: PARSER-CONFIRMED.
    /// </remarks>
    public static UiTexManifest Parse(ReadOnlySpan<byte> span)
    {
        // CP949 registered in static constructor.
        // spec: Docs/RE/formats/ui_manifests.md §1 — "CP949 for all string fields": PARSER-CONFIRMED.
        string text = Encoding.GetEncoding(949).GetString(span);
        return ParseText(text);
    }

    /// <summary>Overload accepting pre-decoded text (primarily for unit testing).</summary>
    public static UiTexManifest ParseText(string text)
    {
        var ddsEntries = new List<UiTexEntry>();
        var mskEntries = new List<UiTexEntry>();

        // Tokenize: whitespace-delimited, '#' comment lines skipped.
        // spec: Docs/RE/formats/ui_manifests.md §1.7 shared tokenizer engine: PARSER-CONFIRMED.
        var tokens = Tokenize(text);
        int pos = 0;

        // Scan to the UI_TEXTURE keyword.
        // spec: Docs/RE/formats/ui_manifests.md §1.2 — outer block "UI_TEXTURE": PARSER-CONFIRMED.
        SkipUntilToken(tokens, KwUiTexture, ref pos);
        if (pos >= tokens.Count) return new UiTexManifest(ddsEntries, mskEntries);

        // Expect '{' after UI_TEXTURE.
        pos++; // consume UI_TEXTURE
        if (pos < tokens.Count && tokens[pos] == "{") pos++;

        // Read sub-blocks until the closing '}'.
        while (pos < tokens.Count && tokens[pos] != "}")
        {
            string subBlock = tokens[pos++];

            if (pos >= tokens.Count || tokens[pos] != "{")
                continue; // malformed — skip

            pos++; // consume '{'

            // Choose target list based on sub-block name.
            // spec: §1.2 — "DDS" holds main texture entries; "MSK" is reserved (empty in observed file).
            List<UiTexEntry> target = subBlock.Equals(KwDds, StringComparison.OrdinalIgnoreCase)
                ? ddsEntries
                : subBlock.Equals(KwMsk, StringComparison.OrdinalIgnoreCase)
                    ? mskEntries
                    : []; // unknown sub-block — parse and discard

            // Read entries until closing '}'.
            while (pos < tokens.Count && tokens[pos] != "}")
            {
                string idToken = tokens[pos++];

                // spec: Docs/RE/formats/ui_manifests.md §1.3 — "tex_id: 4-digit zero-padded
                // decimal integer; parsed as signed integer": PARSER-CONFIRMED.
                if (!int.TryParse(idToken, out int texId))
                    continue; // not a numeric token — skip (comment lines already stripped)

                // Read the quoted path. The next token after the id may be a quoted path.
                // spec: §1.3 — "vfs_path: quoted string; opening '"' found first, content
                // extracted to closing '"' without including the quotes": PARSER-CONFIRMED.
                // Quoting caveat (entry 0029): closing quote may be missing; treat rest of
                // line as path value. spec: §1.3 quoting caveat: PARSER-CONFIRMED.
                if (pos >= tokens.Count) break;

                string pathToken = tokens[pos++];
                string path = ExtractPath(pathToken);

                // Assign the block kind.
                // spec: Docs/RE/formats/ui_manifests.md §1.2 — DDS = main; MSK = mask.
                UiTexBlockKind kind = subBlock.Equals(KwDds, StringComparison.OrdinalIgnoreCase)
                    ? UiTexBlockKind.Dds
                    : UiTexBlockKind.Msk;

                target.Add(new UiTexEntry(texId, path, kind));
            }

            if (pos < tokens.Count && tokens[pos] == "}") pos++; // consume closing '}'
        }

        return new UiTexManifest(ddsEntries, mskEntries);
    }

    // ─── helpers ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Extracts the path value from a token that may be: a bare word, a properly quoted string
    /// <c>"path"</c>, or a path with a missing closing quote (spec §1.3 caveat).
    /// </summary>
    private static string ExtractPath(string token)
    {
        // Strip leading '"' if present; strip trailing '"' if present.
        // spec: §1.3 — "opening '\"' found first; content extracted to closing '\"' without the quotes": PARSER-CONFIRMED.
        // Caveat: closing '"' may be absent for entry 0029. spec: §1.3 quoting caveat: PARSER-CONFIRMED.
        if (token.StartsWith('"'))
            token = token[1..];
        if (token.EndsWith('"'))
            token = token[..^1];
        return token;
    }

    /// <summary>
    /// Tokenizes the text: splits by whitespace, skips lines beginning with <c>#</c>.
    /// Quoted strings that span no spaces are returned as single tokens (possibly still with
    /// their quote characters, which <see cref="ExtractPath"/> strips).
    /// </summary>
    /// <remarks>
    /// spec: Docs/RE/formats/ui_manifests.md §1.7 — "#' triggers comment-skip for the remainder
    /// of the line; tokens are whitespace-delimited; '{{' and '}}' are plain tokens": PARSER-CONFIRMED.
    /// </remarks>
    private static List<string> Tokenize(string text)
    {
        var result = new List<string>(256);

        foreach (string rawLine in text.Split('\n'))
        {
            // Strip CR from CRLF.
            string line = rawLine.TrimEnd('\r');

            // Skip blank lines and comment lines.
            // spec: §1.7 — '#' comment-skip: PARSER-CONFIRMED.
            string trimmed = line.TrimStart();
            if (trimmed.Length == 0 || trimmed[0] == '#') continue;

            // Tokenize the line respecting quoted strings so "path with spaces" stays one token.
            // In practice, UiTex.txt paths contain no spaces, but defensive parsing is correct.
            int i = 0;
            while (i < line.Length)
            {
                // Skip whitespace.
                while (i < line.Length && char.IsWhiteSpace(line[i])) i++;
                if (i >= line.Length) break;

                // Comment character mid-line ends the line.
                if (line[i] == '#') break;

                if (line[i] == '"')
                {
                    // Quoted token: consume up to closing '"' or end-of-line.
                    // spec: §1.3 quoting caveat — missing closing quote treated as end-of-line.
                    int start = i; // include the opening '"'
                    i++; // skip opening '"'
                    while (i < line.Length && line[i] != '"') i++;
                    if (i < line.Length) i++; // skip closing '"'
                    result.Add(line[start..i]);
                }
                else
                {
                    // Unquoted token: consume until whitespace or '#'.
                    int start = i;
                    while (i < line.Length && !char.IsWhiteSpace(line[i]) && line[i] != '#') i++;
                    result.Add(line[start..i]);
                }
            }
        }

        return result;
    }

    private static void SkipUntilToken(List<string> tokens, string keyword, ref int pos)
    {
        while (pos < tokens.Count && !tokens[pos].Equals(keyword, StringComparison.OrdinalIgnoreCase))
            pos++;
    }
}