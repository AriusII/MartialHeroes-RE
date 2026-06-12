using System.Text;
using MartialHeroes.Assets.Parsers.Models;

namespace MartialHeroes.Assets.Parsers;

/// <summary>
/// Parser for <c>data/ui/skillicon/skillicon.txt</c> — the skill icon sheet registry.
/// Grammar: <c>SKILL { &lt;skill_id&gt; &lt;job_id&gt; &lt;kind_id&gt; "&lt;path&gt;" … }</c>.
/// Lines starting with <c>#</c> are comments. CP949 encoding.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/ui_manifests.md §2 data/ui/skillicon/skillicon.txt.
/// Outer keyword: <c>SKILL</c> — PARSER-CONFIRMED.
/// No sub-section nesting (contrast with UiTex.txt which has DDS/MSK sub-blocks).
/// Entry layout: exactly 4 sequential token reads (3 integers + 1 quoted path) — PARSER-CONFIRMED.
/// ZERO rendering/engine dependencies.
/// </remarks>
public static class SkillIconManifestParser
{
    // Outer block keyword.
    // spec: Docs/RE/formats/ui_manifests.md §2.2 — "SKILL" outer keyword: PARSER-CONFIRMED.
    private const string KwSkill = "SKILL";

    // Register CP949 once per AppDomain — safe to call repeatedly but runs only once.
    // spec: Docs/RE/formats/ui_manifests.md §2 — "CP949 for all string fields": PARSER-CONFIRMED.
    static SkillIconManifestParser() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    /// <inheritdoc cref="Parse(ReadOnlySpan{byte})"/>
    public static SkillIconManifest Parse(ReadOnlyMemory<byte> data) => Parse(data.Span);

    /// <summary>
    /// Parses the raw CP949 bytes of a <c>skillicon.txt</c> file.
    /// </summary>
    /// <param name="span">Raw bytes of <c>data/ui/skillicon/skillicon.txt</c>.</param>
    /// <returns>A <see cref="SkillIconManifest"/> with all entries.</returns>
    /// <remarks>
    /// spec: Docs/RE/formats/ui_manifests.md §2.2 — CP949, '#' comment-skip,
    /// whitespace-delimited tokens, single SKILL block, exactly 4 fields per entry: PARSER-CONFIRMED.
    /// </remarks>
    public static SkillIconManifest Parse(ReadOnlySpan<byte> span)
    {
        // spec: Docs/RE/formats/ui_manifests.md §2 — "CP949 for all string fields": PARSER-CONFIRMED.
        string text = Encoding.GetEncoding(949).GetString(span);
        return ParseText(text);
    }

    /// <summary>Overload accepting pre-decoded text (primarily for unit testing).</summary>
    public static SkillIconManifest ParseText(string text)
    {
        var entries = new List<SkillIconEntry>();

        // Tokenize: whitespace-delimited, '#' comment lines skipped.
        // spec: Docs/RE/formats/ui_manifests.md §1.7 shared tokenizer engine: PARSER-CONFIRMED.
        var tokens = Tokenize(text);
        int pos = 0;

        // Scan to the SKILL keyword.
        // spec: Docs/RE/formats/ui_manifests.md §2.2 — outer block "SKILL": PARSER-CONFIRMED.
        while (pos < tokens.Count && !tokens[pos].Equals(KwSkill, StringComparison.OrdinalIgnoreCase))
            pos++;
        if (pos >= tokens.Count) return new SkillIconManifest(entries);

        pos++; // consume SKILL

        // Expect '{' after SKILL.
        if (pos < tokens.Count && tokens[pos] == "{") pos++;

        // Read entries until the closing '}'.
        // spec: §2.3 — "exactly 4 sequential token reads per entry": PARSER-CONFIRMED.
        while (pos < tokens.Count && tokens[pos] != "}")
        {
            // Field 1: skill_id — integer-parse. PARSER-CONFIRMED.
            // spec: §2.3 col 1 — "skill_id: integer-parse helper": PARSER-CONFIRMED.
            string tok0 = tokens[pos++];
            if (!int.TryParse(tok0, out int skillId))
                continue; // not an integer — skip (e.g. a stray token)

            if (pos >= tokens.Count || tokens[pos] == "}") break;

            // Field 2: job_id — integer-parse. PARSER-CONFIRMED.
            // spec: §2.3 col 2 — "job_id: integer-parse helper; 1=Musa,2=Assassin,3=Wizard,4=Monk": PARSER-CONFIRMED.
            // On parse failure advance past the bad token and skip only this entry (not the whole block).
            if (!int.TryParse(tokens[pos++], out int jobId)) continue;

            if (pos >= tokens.Count || tokens[pos] == "}") break;

            // Field 3: kind_id — integer-parse. PARSER-CONFIRMED.
            // spec: §2.3 col 3 — "kind_id: integer-parse helper; 1=jung,2=sa,3=ma": PARSER-CONFIRMED.
            // On parse failure advance past the bad token and skip only this entry (not the whole block).
            if (!int.TryParse(tokens[pos++], out int kindId)) continue;

            if (pos >= tokens.Count || tokens[pos] == "}") break;

            // Field 4: quoted icon_sheet_path. PARSER-CONFIRMED.
            // spec: §2.3 col 4 — "icon_sheet_path: quote-string helpers": PARSER-CONFIRMED.
            string rawPath = tokens[pos++];
            string path = ExtractPath(rawPath);

            entries.Add(new SkillIconEntry(skillId, jobId, kindId, path));
        }

        return new SkillIconManifest(entries);
    }

    // ─── helpers ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Strips surrounding double-quote characters from a path token.
    /// spec: Docs/RE/formats/ui_manifests.md §2.3 — quote delimiter ASCII 34: PARSER-CONFIRMED.
    /// </summary>
    private static string ExtractPath(string token)
    {
        if (token.StartsWith('"')) token = token[1..];
        if (token.EndsWith('"')) token = token[..^1];
        return token;
    }

    /// <summary>
    /// Tokenizes the text: splits by whitespace, skips lines beginning with <c>#</c>.
    /// Quoted strings are captured as single tokens (with quote characters retained for
    /// <see cref="ExtractPath"/> to strip).
    /// </summary>
    /// <remarks>
    /// spec: Docs/RE/formats/ui_manifests.md §1.7 — '#' comment-skip; whitespace-delimited
    /// tokens; same tokenizer as UiTex.txt: PARSER-CONFIRMED.
    /// </remarks>
    private static List<string> Tokenize(string text)
    {
        var result = new List<string>(256);

        foreach (string rawLine in text.Split('\n'))
        {
            string line = rawLine.TrimEnd('\r');
            string trimmed = line.TrimStart();
            if (trimmed.Length == 0 || trimmed[0] == '#') continue;

            int i = 0;
            while (i < line.Length)
            {
                while (i < line.Length && char.IsWhiteSpace(line[i])) i++;
                if (i >= line.Length) break;

                if (line[i] == '#') break; // mid-line comment

                if (line[i] == '"')
                {
                    int start = i;
                    i++; // skip opening '"'
                    while (i < line.Length && line[i] != '"') i++;
                    if (i < line.Length) i++; // skip closing '"'
                    result.Add(line[start..i]);
                }
                else
                {
                    int start = i;
                    while (i < line.Length && !char.IsWhiteSpace(line[i]) && line[i] != '#') i++;
                    result.Add(line[start..i]);
                }
            }
        }

        return result;
    }
}