using Godot;
using MartialHeroes.Client.Infrastructure.Catalog;

namespace MartialHeroes.Client.Godot.Ui.Scenes.Select;

internal sealed class NpcScrDescriptions
{
    private static readonly uint[] UiIndexToNpcKey = [1u, 2u, 4u, 3u];


    private readonly string?[] _resolved = new string?[4];

    private readonly string[][] _resolvedLines = new string[4][];


    private NpcScrDescriptions()
    {
    }

    public bool LoadedFromVfs { get; private set; }

    public static NpcScrDescriptions Load(NpcCatalogue? catalogue)
    {
        var inst = new NpcScrDescriptions();

        if (catalogue is null || catalogue.Count == 0)
        {
            GD.Print(
                "[NpcScrDescriptions] No NpcCatalogue (real-client VFS absent / npc.scr empty) — " +
                "class descriptions will be empty (faithfully offline). spec: config_tables.md §2.17.3.");
            return inst;
        }

        try
        {
            for (var uiIdx = 0; uiIdx < 4; uiIdx++)
            {
                var key = UiIndexToNpcKey[uiIdx];

                var rec = catalogue.GetById(key);
                if (rec is null)
                {
                    GD.PrintErr($"[NpcScrDescriptions] npc.scr key {key} (UI slot {uiIdx}) not in NpcCatalogue — " +
                                "slot will be empty (faithfully offline). spec: config_tables.md §2.17.3.");
                    continue;
                }

                var lines = rec.NameSlots ?? Array.Empty<string>();
                inst._resolvedLines[uiIdx] = lines;
                var joined = string.Join("\n", lines.Where(l => !string.IsNullOrWhiteSpace(l)));

                if (string.IsNullOrWhiteSpace(joined))
                {
                    GD.PrintErr(
                        $"[NpcScrDescriptions] npc.scr key {key} (UI slot {uiIdx}) has empty description — slot will be empty.");
                    continue;
                }

                inst._resolved[uiIdx] = joined;
                GD.Print(
                    $"[NpcScrDescriptions] UI {uiIdx} (npc.scr key {key}): first line = '{(lines.Length > 0 ? lines[0] : string.Empty)}'");
            }

            inst.LoadedFromVfs = true;
            GD.Print($"[NpcScrDescriptions] NpcCatalogue consumed ({catalogue.Count} records); " +
                     $"class descriptions resolved: {inst._resolved.Count(s => s is not null)}/4. " +
                     "spec: config_tables.md §2.17.3 + frontend_scenes.md §4.1.1.");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[NpcScrDescriptions] Failed to resolve NpcCatalogue descriptions: {ex.Message} — " +
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