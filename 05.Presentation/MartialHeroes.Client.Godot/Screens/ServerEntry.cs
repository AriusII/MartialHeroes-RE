// Screens/ServerEntry.cs
// Extracted (CAMPAIGN 17 follow-up) from the now-removed ServerSelectScreen.cs so the active
// Login flow (Scene/Controllers/LoginScene.cs + Ui/Scenes/Login/ServerSelectSubView.cs) keeps the
// shared view-model after the legacy front-end was deleted. Pure data record — no engine dependency.

namespace MartialHeroes.Client.Godot.Screens;

/// <summary>
/// View-model for one 8-byte lobby server record.
/// spec: Docs/RE/packets/lobby.yaml §RECORD SHAPE A (the code-confirmed wire spec — supersedes the
/// earlier login_flow.md §2.1 "Load/OpenTime/open-clock" framing). CODE-CONFIRMED.
/// </summary>
public sealed record ServerEntry(
    /// <summary>
    /// Server id / select-key (wire +0). Also indexes the client-local name table (resolved from msg banks
    /// 5001..5040) when <see cref="StatusCode"/> is in 1..39. spec: §RECORD SHAPE A +0.
    /// </summary>
    int ServerId,
    /// <summary>Client-local display name (resolved from msg banks 5001..5040). spec §RECORD SHAPE A +0.</summary>
    string DisplayName,
    /// <summary>
    /// Caption / branch selector (wire +2, <c>status_kind</c>). spec: §RECORD SHAPE A +2:
    ///   0 = derive a population label/color from <see cref="Population"/> / <see cref="Flag"/>;
    ///   3 = special (caption 6004 when Population==24, else 6005 latency digit-split from Population/Flag);
    ///   1..39 = per-value caption-string array; &lt; 1 or &gt; 39 = fallback caption 5901.
    /// </summary>
    int StatusCode,
    /// <summary>
    /// Population / count value (wire +4). In numeric mode (Flag != 0) thresholded 500/800/1200 (strict
    /// greater-than). In discrete mode (Flag == 0) a discrete load level (2/3/4). In the status==3 branch
    /// it is the 6005 latency numerator (and == 24 selects caption 6004). spec: §RECORD SHAPE A +4.
    /// </summary>
    int Population,
    /// <summary>
    /// Mode flag (wire +6): nonzero = treat <see cref="Population"/> as a numeric population (apply
    /// thresholds); zero = treat it as a discrete load level. Also a 6005 latency numerator in the
    /// status==3 branch. NOT an HH:MM open-clock. spec: §RECORD SHAPE A +6.
    /// </summary>
    int Flag)
{
    /// <summary>
    /// Data-driven selectability gate used by the login server-list plate click path.
    /// spec: Docs/RE/specs/frontend_scenes.md §11.4; Docs/RE/packets/lobby.yaml §RECORD SHAPE A.
    /// </summary>
    public bool IsSelectable => StatusCode == 0 && Population < 2400;
}