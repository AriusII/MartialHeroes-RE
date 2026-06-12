using System.Text;
using MartialHeroes.Assets.Parsers.Models;

namespace MartialHeroes.Assets.Parsers;

/// <summary>
/// Parses <c>data/char/actormotion.txt</c> — the table that maps every actor-class (= mob_id)
/// to its skeleton body-type (skin_class) and motion IDs.
/// </summary>
/// <remarks>
/// <para>
/// Format: tab-separated text, CP949 / EUC-KR. Line 0 is the record count (integer). Each
/// subsequent line is one record with at least 16 tab-separated integer columns.
/// </para>
/// <para>
/// Column layout (0-based):
/// <list type="bullet">
/// <item><description>col[0] = flag (always 0 for mob/NPC entries; 1 or 2 for special rows)</description></item>
/// <item><description>col[1] = actor_class_id  — the key; equals mob_id from .arr spawn records
///   (spec: Docs/RE/formats/npc_spawns.md, mob_id field)</description></item>
/// <item><description>col[2] = skin_class_id  — equals the g-id of the associated .bnd skeleton
///   file at <c>data/char/bind/g{skin_class_id}.bnd</c>.  Also used as the
///   <c>id_b</c> key when locating the body .skn mesh (spec: Docs/RE/formats/mesh.md §id_b).</description></item>
/// <item><description>col[15] = body_anim_base_id — the composite animation ID for the idle/stand
///   motion (first entry in the motion bank).  Corresponds to files in
///   <c>data/char/mot/</c> (motlist.txt index).</description></item>
/// </list>
/// </para>
/// <para>
/// Sample-verified: 902 parsed entries, actor_class range 1..998, skin_class range 1..8892.
/// Columns beyond col[15] carry additional animation IDs for attack, hit, death, etc.;
/// they are captured in <see cref="ActormotionEntry.MotionIds"/> for completeness.
/// </para>
/// <para>
/// ZERO rendering/engine dependencies.
/// </para>
/// </remarks>
public static class ActormotionParser
{
    // Number of leading motion-ID columns stored in MotionIds (cols 15..21, inclusive).
    // spec: actormotion.txt observed column count is 22 (indices 0..21).
    private const int MotionColStart = 15;
    private const int MotionColEnd   = 21; // inclusive
    private const int MotionColCount = MotionColEnd - MotionColStart + 1; // = 7

    /// <summary>
    /// Parses <c>data/char/actormotion.txt</c> bytes (CP949) into an array of
    /// <see cref="ActormotionEntry"/> keyed by <see cref="ActormotionEntry.ActorClassId"/>.
    /// </summary>
    /// <param name="fileBytes">
    /// Raw bytes of the file, typically obtained via
    /// <c>MappedVfsArchive.GetFileContent("data/char/actormotion.txt")</c>.
    /// </param>
    /// <returns>
    /// Array of parsed entries.  Rows with fewer than 16 columns or an unparseable
    /// actor_class_id are silently skipped (they are header/footer artefacts in the source file).
    /// </returns>
    public static ActormotionEntry[] Parse(ReadOnlyMemory<byte> fileBytes)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var cp949 = Encoding.GetEncoding(949); // CP949 / EUC-KR

        // Decode full file as text; split into lines.
        string text  = cp949.GetString(fileBytes.Span);
        string[] raw = text.Split('\n');

        // Preallocate using the declared count from line 0 (best-effort, fallback = line count).
        int capacity = raw.Length;
        if (raw.Length > 0 && int.TryParse(raw[0].Trim('\r'), out int declared))
            capacity = declared;

        var entries = new List<ActormotionEntry>(capacity);

        for (int lineIdx = 1; lineIdx < raw.Length; lineIdx++)
        {
            ReadOnlySpan<char> line = raw[lineIdx].AsSpan().TrimEnd('\r');
            if (line.IsEmpty) continue;

            // Split on tabs; we need at least 16 columns (col[0]..col[15]).
            // Use a stack-allocated scratch array for small column counts.
            string[] cols = raw[lineIdx].TrimEnd('\r').Split('\t');
            if (cols.Length < MotionColStart + 1) continue;

            if (!int.TryParse(cols[1].Trim(), out int actorClassId)) continue;
            if (!int.TryParse(cols[2].Trim(), out int skinClassId))   continue;

            // Capture motion IDs (cols 15..21) if present.
            int available   = Math.Min(MotionColCount, cols.Length - MotionColStart);
            int[] motionIds = new int[MotionColCount];
            for (int m = 0; m < available; m++)
                int.TryParse(cols[MotionColStart + m].Trim(), out motionIds[m]);

            entries.Add(new ActormotionEntry(
                actorClassId,
                skinClassId,
                motionIds));
        }

        return entries.ToArray();
    }

    /// <summary>
    /// Parses <c>data/char/actormotion.txt</c> and returns a dictionary keyed by
    /// <see cref="ActormotionEntry.ActorClassId"/> for O(1) mob-id lookup.
    /// </summary>
    public static Dictionary<int, ActormotionEntry> ParseAsLookup(ReadOnlyMemory<byte> fileBytes)
    {
        var arr = Parse(fileBytes);
        var dict = new Dictionary<int, ActormotionEntry>(arr.Length);
        foreach (var e in arr)
            dict.TryAdd(e.ActorClassId, e); // first occurrence wins for duplicates
        return dict;
    }
}
