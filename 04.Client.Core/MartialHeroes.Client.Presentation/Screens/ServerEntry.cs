// Screens/ServerEntry.cs
// Extracted (CAMPAIGN 17 follow-up) from the now-removed ServerSelectScreen.cs so the active
// Login flow (Scene/Controllers/LoginScene.cs + Ui/Scenes/Login/ServerSelectSubView.cs) keeps the
// shared view-model after the legacy front-end was deleted. Pure data record — no engine dependency.

namespace MartialHeroes.Client.Presentation.Screens;

/// <summary>
///     View-model for one 8-byte lobby server record.
///     spec: Docs/RE/packets/lobby.yaml Record Shape A (the static-confirmed wire spec). CODE-CONFIRMED.
/// </summary>
public sealed record ServerEntry(
    /// <summary>
    /// Server id / select-key (wire +0, 1-based, valid 1..40). The name resolver key: the display name is
    /// message id <c>5000 + ServerId</c> (msg bank 5001..5040 for ids 1..40, no group/channel multiplier;
    /// out-of-range → fallback 5901). The <c>== 100</c> special-row sentinel lives on THIS field (+0), NOT
    /// on <see cref="StatusCode"/> — the painter's <c>v52 = record[+0] == 100</c> test marks a display-only
    /// event row (3 indicator quads), and id 100 is out of the 1..40 name range so it draws msg 5901. This
    /// is NOT a selectability gate (commit gate = <c>StatusCode == 0 &amp;&amp; Load &lt; 2400</c>).
    /// spec: Docs/RE/specs/frontend_layout_tables.md §4.1 (name_id = 5000 + ServerId; server_id == 100 gate,
    /// CORRECTION 2026-06-20); Record Shape A +0.
    /// </summary>
    int ServerId,
    /// <summary>
    /// Client-local display name (resolved from msg bank <c>5000 + ServerId</c> = 5001..5040; the bank is
    /// client-local, not on the wire). spec: Docs/RE/specs/frontend_layout_tables.md §4.1; Record Shape A +0.
    /// </summary>
    string DisplayName,
    /// <summary>
    /// Availability / caption selector (wire +2). The status caption is message id
    /// <c>4029 + StatusCode</c> (StatusCode 0..3 → ids 4029..4032). spec: Record Shape A +2;
    /// frontend_layout_tables.md §4.1 (status caption resolver):
    ///   0 = active/selectable (the only state the commit guard accepts; derive load color from
    ///       <see cref="Load"/> via the +6 load-valid flag — threshold ladder when <see cref="OpenTime"/>
    ///       (+6) ≠ 0, else discrete 4/3/2 ladder);
    ///   3 = scheduled-open (HH:MM caption from <see cref="Load"/> hour + <see cref="OpenTime"/> minute).
    ///   The <c>== 100</c> special-row sentinel is on <see cref="ServerId"/> (+0), NOT this field
    ///   (CORRECTION 2026-06-20).
    /// </summary>
    int StatusCode,
    /// <summary>
    /// Population / load gauge (wire +4). When <see cref="StatusCode"/>==0 with the +6 load-valid flag set
    /// it is a raw count thresholded 500/800/1200 (strict greater-than) for the plate color; with +6 clear
    /// it is a DISCRETE level (4/3/2 → red/orange/yellow). Gated &lt; 2400 for selectability. In the
    /// status==3 branch it is the scheduled-open HOUR. spec: Record Shape A +4; §4.1 (two colour ladders).
    /// </summary>
    int Load,
    /// <summary>
    /// Load-valid flag / scheduled-open MINUTE (wire +6). When <see cref="StatusCode"/>==0 it is the
    /// load-valid FLAG (≠0 ⇒ <see cref="Load"/> is a raw count read with the threshold ladder; ==0 ⇒
    /// <see cref="Load"/> is a discrete 4/3/2 level). When <see cref="StatusCode"/>==3 it is the
    /// scheduled-open MINUTE (combined with <see cref="Load"/> hour into HH:MM). spec: Record Shape A +6;
    /// §4.1 (RESOLVED 2026-06-20).
    /// </summary>
    int OpenTime)
{
    /// <summary>
    ///     Data-driven selectability gate used by the login server-list plate click path:
    ///     <c>StatusCode == 0 AND Load &lt; 2400</c> (0x960, signed strict less-than), confirmed in
    ///     <c>LoginWindow_OnEvent</c> (0x5fa86a). The <c>ServerId == 100</c> sentinel is a display-only special
    ///     row (3 indicator quads), NOT a selectability gate.
    ///     spec: Docs/RE/specs/frontend_layout_tables.md §2.2 sub-state 37 / §4.1 (commit guard
    ///     status_code == 0 &amp;&amp; load &lt; 2400); Docs/RE/packets/lobby.yaml Record Shape A.
    /// </summary>
    public bool IsSelectable => StatusCode == 0 && Load < 2400;
}