// Screens/NpcScrDescriptions.cs
//
// Thin adapter: loads data/script/npc.scr via RealClientAssets, parses it with the
// existing NpcScrParser, and exposes the three CP949 class-description lines for UI
// class indices 0..3 (the create-form right panel).
//
// PASSIVE: zero game logic. Read-only loader; returns strings that the UI renders.
//
// Mapping (UI slot → npc.scr key → internal class):
//   UI 0 → key 1 → Monk   (internal 4)
//   UI 1 → key 2 → Musa   (internal 1)
//   UI 2 → key 4 → Dosa   (internal 3)
//   UI 3 → key 3 → Salsu  (internal 2)
// spec: Docs/RE/formats/config_tables.md §2.17.3 — UI-slot vs npc.scr key crossover: CONFIRMED.
// spec: Docs/RE/specs/frontend_scenes.md §4.1.1 — class description source = npc.scr keys 1..4: CONFIRMED.

using Godot;
using MartialHeroes.Assets.Parsers;
using MartialHeroes.Assets.Parsers.Models;
using MartialHeroes.Client.Godot.Dev;

namespace MartialHeroes.Client.Godot.Screens;

/// <summary>
/// Loads the CP949 class-description text from <c>data/script/npc.scr</c> and provides
/// it indexed by UI class index (0..3).
///
/// <para>Reuses <see cref="NpcScrParser"/> from <c>Assets.Parsers</c> — no raw byte
/// parsing in the UI layer.</para>
///
/// <para>VFS path: <c>data/script/npc.scr</c>.
/// spec: Docs/RE/formats/config_tables.md §2.17.3 — stride 404 bytes, 2510 records: CONFIRMED.</para>
///
/// <para>When the VFS is absent, returns the English fallback strings so the offline
/// flow remains functional.</para>
/// </summary>
internal sealed class NpcScrDescriptions
{
    // VFS path for npc.scr.
    // spec: Docs/RE/formats/config_tables.md §2.17.3 — "data/script/npc.scr": CONFIRMED.
    private const string NpcScrVfsPath = "data/script/npc.scr";

    // UI class index (0..3) → npc.scr record key.
    // spec: Docs/RE/formats/config_tables.md §2.17.3 — key↔UI crossover table: CONFIRMED.
    // spec: Docs/RE/specs/frontend_scenes.md §4.1.1 — "UI 0→key 1, UI 1→key 2, UI 2→key 4, UI 3→key 3": CONFIRMED.
    private static readonly uint[] UiIndexToNpcKey = [1u, 2u, 4u, 3u]; // spec: config_tables.md §2.17.3

    // English fallback descriptions — shown when npc.scr is unavailable (VFS absent).
    // Each entry = the three description lines joined with "\n".
    private static readonly string[] Fallbacks =
    [
        // UI 0 → Monk (key 1, internal 4)
        "A powerful warrior who excels in direct combat and defense.",
        // UI 1 → Musa (key 2, internal 1)
        "A martial artist with speed and balanced abilities.",
        // UI 2 → Dosa (key 4, internal 3)
        "A mystical practitioner commanding elemental forces.",
        // UI 3 → Salsu (key 3, internal 2)
        "A swift blader skilled in both attack and evasion.",
    ];

    // Resolved descriptions (three lines joined with \n), indexed by UI class index 0..3.
    // Populated by Load(); null means the VFS was not available.
    private readonly string?[] _resolved = new string?[4];

    // Whether the VFS load succeeded (used for diagnostics).
    private bool _loadedFromVfs;

    private NpcScrDescriptions() { }

    /// <summary>
    /// Attempts to load npc.scr from the VFS.  Returns a ready instance.
    /// On any failure, falls back to English strings — never throws.
    /// </summary>
    public static NpcScrDescriptions Load(RealClientAssets? realAssets)
    {
        var inst = new NpcScrDescriptions();

        if (realAssets is null)
        {
            GD.Print("[NpcScrDescriptions] No real-client VFS — using English fallbacks.");
            return inst;
        }

        try
        {
            ReadOnlyMemory<byte> raw = realAssets.GetRaw(NpcScrVfsPath);
            // spec: Docs/RE/formats/config_tables.md §2.17.3 — "data/script/npc.scr": CONFIRMED.

            if (raw.IsEmpty)
            {
                GD.PrintErr("[NpcScrDescriptions] npc.scr not found in VFS — using English fallbacks. " +
                            "spec: config_tables.md §2.17.3.");
                return inst;
            }

            NpcScrRecord[] records = NpcScrParser.Parse(raw);
            // spec: Docs/RE/formats/config_tables.md §2.17.3 — "stride 404, 2510 records": CONFIRMED.

            // Build a key→first-record map (take first occurrence; npc.scr key IDs are sequential
            // but duplicate key values may occur in the tail of the table — skip duplicates, keep first).
            // spec: config_tables.md §2.17.3 — "sequential 1..2510; class records at keys 1..4": CONFIRMED.
            var byKey = new Dictionary<uint, NpcScrRecord>(capacity: records.Length);
            foreach (NpcScrRecord r in records)
                byKey.TryAdd(r.Id, r); // keep first occurrence; silently skip duplicates

            // Resolve descriptions for each UI slot.
            for (int uiIdx = 0; uiIdx < 4; uiIdx++)
            {
                uint key = UiIndexToNpcKey[uiIdx];
                // spec: config_tables.md §2.17.3 — UI-slot vs npc.scr key crossover: CONFIRMED.

                if (!byKey.TryGetValue(key, out NpcScrRecord? rec))
                {
                    GD.PrintErr($"[NpcScrDescriptions] npc.scr key {key} (UI slot {uiIdx}) not found in " +
                                "parsed records — using English fallback. spec: config_tables.md §2.17.3.");
                    continue;
                }

                // String fields 0/1/2 at offsets +0x14 / +0x54 / +0x94 are the three CP949 lines.
                // spec: Docs/RE/formats/config_tables.md §2.17.3 — "fields 0/1/2 = description lines": CONFIRMED.
                // spec: Docs/RE/specs/frontend_scenes.md §4.1.1 — "three CP949 lines at +0x14/+0x54/+0x94": CONFIRMED.
                // NpcScrParser already decoded CP949 → .NET string; we only need to join non-empty lines.
                string[] lines = [rec.Paragraph0, rec.Paragraph1, rec.Paragraph2];
                string joined = string.Join("\n", lines.Where(l => !string.IsNullOrWhiteSpace(l)));

                if (string.IsNullOrWhiteSpace(joined))
                {
                    GD.PrintErr($"[NpcScrDescriptions] npc.scr key {key} has empty description — using English fallback.");
                    continue;
                }

                inst._resolved[uiIdx] = joined;
                GD.Print($"[NpcScrDescriptions] UI {uiIdx} (npc.scr key {key}): first line = '{lines[0]}'");
            }

            inst._loadedFromVfs = true;
            GD.Print($"[NpcScrDescriptions] npc.scr loaded ({records.Length} records); " +
                     $"class descriptions resolved: {inst._resolved.Count(s => s is not null)}/4. " +
                     "spec: config_tables.md §2.17.3 + frontend_scenes.md §4.1.1.");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[NpcScrDescriptions] Failed to load/parse npc.scr: {ex.Message} — " +
                        "using English fallbacks.");
        }

        return inst;
    }

    /// <summary>
    /// Returns the CP949-decoded description text (three lines joined with newlines) for the
    /// given UI class index (0..3).
    ///
    /// <para>Falls back to the English string when npc.scr was unavailable or the record
    /// was absent.</para>
    ///
    /// spec: Docs/RE/formats/config_tables.md §2.17.3 — string fields 0/1/2 for keys 1..4: CONFIRMED.
    /// spec: Docs/RE/specs/frontend_scenes.md §4.1.1 — class description from npc.scr: CONFIRMED.
    /// </summary>
    public string GetDescription(int uiClassIndex)
    {
        if (uiClassIndex < 0 || uiClassIndex >= 4)
            return Fallbacks[0];

        return _resolved[uiClassIndex] ?? Fallbacks[uiClassIndex];
    }

    /// <summary>True when the descriptions were loaded from the real VFS npc.scr.</summary>
    public bool LoadedFromVfs => _loadedFromVfs;
}
