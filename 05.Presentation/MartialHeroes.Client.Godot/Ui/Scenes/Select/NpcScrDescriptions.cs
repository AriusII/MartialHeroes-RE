
using Godot;
using MartialHeroes.Assets.Parsers.DataTables;
using MartialHeroes.Assets.Parsers.DataTables.Models;
using MartialHeroes.Client.Godot.Composition;

namespace MartialHeroes.Client.Godot.Ui.Scenes.Select;

internal sealed class NpcScrDescriptions
{
    private const string NpcScrVfsPath = "data/script/npc.scr";

    private static readonly uint[] UiIndexToNpcKey = [1u, 2u, 4u, 3u];


    private readonly string?[] _resolved = new string?[4];

    private readonly string[][] _resolvedLines = new string[4][];


    private NpcScrDescriptions()
    {
    }

    public bool LoadedFromVfs { get; private set; }

    public static NpcScrDescriptions Load(RealClientAssets? realAssets)
    {
        var inst = new NpcScrDescriptions();

        if (realAssets is null)
        {
            GD.Print(
                "[NpcScrDescriptions] No real-client VFS — class descriptions will be empty (faithfully offline).");
            return inst;
        }

        try
        {
            var raw = realAssets.GetRaw(NpcScrVfsPath);

            if (raw.IsEmpty)
            {
                GD.PrintErr("[NpcScrDescriptions] npc.scr not found in VFS — descriptions will be empty. " +
                            "spec: config_tables.md §2.17.3.");
                return inst;
            }

            var records = NpcScrParser.Parse(raw);

            var byKey = new Dictionary<uint, NpcScrRecord>(records.Length);
            foreach (var r in records)
                byKey.TryAdd(r.Id, r);

            for (var uiIdx = 0; uiIdx < 4; uiIdx++)
            {
                var key = UiIndexToNpcKey[uiIdx];

                if (!byKey.TryGetValue(key, out var rec))
                {
                    GD.PrintErr($"[NpcScrDescriptions] npc.scr key {key} (UI slot {uiIdx}) not found in " +
                                "parsed records — slot will be empty (faithfully offline). spec: config_tables.md §2.17.3.");
                    continue;
                }

                string[] lines =
                    [rec.Paragraph0 ?? string.Empty, rec.Paragraph1 ?? string.Empty, rec.Paragraph2 ?? string.Empty];
                inst._resolvedLines[uiIdx] = lines;
                var joined = string.Join("\n", lines.Where(l => !string.IsNullOrWhiteSpace(l)));

                if (string.IsNullOrWhiteSpace(joined))
                {
                    GD.PrintErr(
                        $"[NpcScrDescriptions] npc.scr key {key} (UI slot {uiIdx}) has empty description — slot will be empty.");
                    continue;
                }

                inst._resolved[uiIdx] = joined;
                GD.Print($"[NpcScrDescriptions] UI {uiIdx} (npc.scr key {key}): first line = '{lines[0]}'");
            }

            inst.LoadedFromVfs = true;
            GD.Print($"[NpcScrDescriptions] npc.scr loaded ({records.Length} records); " +
                     $"class descriptions resolved: {inst._resolved.Count(s => s is not null)}/4. " +
                     "spec: config_tables.md §2.17.3 + frontend_scenes.md §4.1.1.");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[NpcScrDescriptions] Failed to load/parse npc.scr: {ex.Message} — " +
                        "descriptions will be empty (faithfully offline).");
        }

        return inst;
    }

    public string GetDescription(int uiClassIndex)
    {
        if (uiClassIndex < 0 || uiClassIndex >= 4)
            return string.Empty;

        return _resolved[uiClassIndex] ?? string.Empty;
    }

    public string[] GetDescriptionLines(int uiClassIndex)
    {
        if (uiClassIndex < 0 || uiClassIndex >= 4 || _resolvedLines[uiClassIndex] is not { } lines)
            return ["", "", ""];
        return lines;
    }
}