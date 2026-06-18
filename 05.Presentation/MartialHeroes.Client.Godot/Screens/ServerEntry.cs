// Screens/ServerEntry.cs
// Extracted (CAMPAIGN 17 follow-up) from the now-removed ServerSelectScreen.cs so the active
// Login flow (Scene/Controllers/LoginScene.cs + Ui/Scenes/Login/ServerSelectSubView.cs) keeps the
// shared view-model after the legacy front-end was deleted. Pure data record — no engine dependency.

namespace MartialHeroes.Client.Godot.Screens;

/// <summary>
/// View-model for one 8-byte lobby server record.
/// spec: Docs/RE/packets/lobby.yaml Record Shape A (the static-confirmed wire spec). CODE-CONFIRMED.
/// </summary>
public sealed record ServerEntry(
    /// <summary>
    /// Server id / select-key (wire +0, range 1..40). Also indexes the client-local name table (resolved
    /// from msg banks 5001..5040) when <see cref="StatusCode"/> is in 1..39. Not a selectability gate (the
    /// <c>== 100</c> literal is display-only). spec: Record Shape A +0.
    /// </summary>
    int ServerId,
    /// <summary>Client-local display name (resolved from msg banks 5001..5040). spec: Record Shape A +0.</summary>
    string DisplayName,
    /// <summary>
    /// Availability / caption selector (wire +2). spec: Record Shape A +2:
    ///   0 = active/selectable (derive a load label/color from <see cref="Load"/>);
    ///   3 = scheduled-open (HH:MM caption from <see cref="Load"/> hour + <see cref="OpenTime"/> minute);
    ///   1..39 = per-value caption-string array; &lt; 1 or &gt; 39 = fallback caption 5901.
    /// </summary>
    int StatusCode,
    /// <summary>
    /// Population / load gauge (wire +4). Thresholded 500/800/1200 (strict greater-than) for the plate
    /// color, and gated &lt; 2400 for selectability. In the status==3 branch it is the scheduled-open
    /// HOUR. spec: Record Shape A +4.
    /// </summary>
    int Load,
    /// <summary>
    /// Scheduled-open MINUTE value (wire +6) — a time component, NOT a flag/bitfield. Combined with
    /// <see cref="Load"/> (hour) into an HH:MM caption in the status==3 branch. spec: Record Shape A +6.
    /// </summary>
    int OpenTime)
{
    /// <summary>
    /// Data-driven selectability gate used by the login server-list plate click path:
    /// <c>StatusCode == 0 AND Load &lt; 2400</c> (0x960, signed strict less-than). There is NO
    /// <c>ServerId == 100</c> gate.
    /// spec: Docs/RE/specs/frontend_scenes.md §11.4; Docs/RE/packets/lobby.yaml Record Shape A.
    /// </summary>
    public bool IsSelectable => StatusCode == 0 && Load < 2400;
}