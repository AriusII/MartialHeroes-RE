// Ui/Hud/HudMaster.WorldEntry.cs
//
// Partial — public pass-through methods so the GameLoop drain can feed world-entry events
// into the correct HUD panels without exposing the private panel fields directly.
//
// Three inbound event paths:
//   A) HotbarInitializedEvent  → _skillHotbar.OnHotbarInitialized(evt)
//   B) RosterSnapshotEvent     → _partyWindow.OnRosterSnapshot(evt)
//   C) ActorDiedEvent          → respawn modal via existing HudMaster surface (ShowConfirm / ShowNotice)
//      Modal shown ONLY when evt.IsLocalPlayer AND evt.DeathCause != 3.
//      DeathCause 3 = special / no-modal (suppressed).
//      Non-local-player deaths: GD.Print only (world/character lane owns the death animation).
//      PK effects (IsPkA / IsPkB) anchor on the victim — that is the world/character lane's job.
//
// spec: Docs/RE/packets/4-1_game_state_tick.yaml (HotbarSlots note; Table A roster).
// spec: Docs/RE/specs/world_systems.md §13.3 — WorldEntryTableA roster model.
// spec: Docs/RE/packets/5-10_combat_death.yaml — DeathCause {0 normal, 1 PK-A, 2 PK-B, 3 special-no-modal}.

using Godot;
using MartialHeroes.Client.Application.Contracts.Events;

namespace MartialHeroes.Client.Godot.Ui.Hud;

public sealed partial class HudMaster
{
    /// <summary>
    ///     Pass-through: forwards the 4/1 HotbarInitializedEvent to the skill hotbar panel.
    ///     The drain calls this on the main thread; the panel populates its 18 visible slots.
    ///     spec: Docs/RE/packets/4-1_game_state_tick.yaml (HotbarSlots note — EntryKey raw, category-pending).
    /// </summary>
    public void OnHotbarInitialized(HotbarInitializedEvent evt)
    {
        // Forward to panel; panel guards against null slot arrays and empty events internally.
        _skillHotbar?.OnHotbarInitialized(evt);
    }

    /// <summary>
    ///     Pass-through: forwards the 4/1 RosterSnapshotEvent to the party window panel.
    ///     The drain calls this on the main thread; the panel fills member rows.
    ///     spec: Docs/RE/packets/4-1_game_state_tick.yaml (Table A 16-byte record).
    ///     spec: Docs/RE/specs/world_systems.md §13.3 — WorldEntryTableA roster model.
    /// </summary>
    public void OnRosterSnapshot(RosterSnapshotEvent evt)
    {
        // Forward to panel; panel clears and repopulates rows from the snapshot.
        _partyWindow?.OnRosterSnapshot(evt);
    }

    /// <summary>
    ///     Handles an ActorDiedEvent from the GameLoop drain.
    ///     <para>
    ///         Rules (spec-faithful):
    ///         <list type="bullet">
    ///             <item>
    ///                 <see cref="ActorDiedEvent.IsLocalPlayer" /> == true AND
    ///                 <see cref="ActorDiedEvent.DeathCause" /> != 3 → show the respawn confirm modal
    ///                 via <see cref="ShowConfirm" /> (the existing mode-1 Yes/No modal surface).
    ///             </item>
    ///             <item>
    ///                 <see cref="ActorDiedEvent.DeathCause" /> == 3 → no modal (special / no-modal death).
    ///                 spec: Docs/RE/packets/5-10_combat_death.yaml — "DeathCause 3 = special / no-modal".
    ///             </item>
    ///             <item>
    ///                 Non-local-player death → GD.Print only; the world/character lane owns the
    ///                 death animation and PK anchor effects.
    ///             </item>
    ///             <item>
    ///                 PK effects (IsPkA / IsPkB) anchoring on the victim is the world/character
    ///                 lane's responsibility — this method does NOT render those effects.
    ///                 spec: Docs/RE/packets/5-10_combat_death.yaml — "PK effects anchor on the victim pair".
    ///             </item>
    ///         </list>
    ///     </para>
    ///     spec: Docs/RE/packets/5-10_combat_death.yaml — DeathCause {0 normal, 1 PK-A, 2 PK-B, 3 special-no-modal}.
    /// </summary>
    public void OnActorDied(ActorDiedEvent evt)
    {
        if (!evt.IsLocalPlayer)
        {
            // Non-local player death — world/character lane handles motion/effect.
            // PK anchor effects (IsPkA / IsPkB) are also the world lane's job.
            // spec: Docs/RE/packets/5-10_combat_death.yaml — "PK effects anchor on the victim pair".
            GD.Print($"[HudMaster] ActorDied: victim={evt.VictimKey} killer={evt.KillerKey} " +
                     $"cause={evt.DeathCause} isPkA={evt.IsPkA} isPkB={evt.IsPkB} — " +
                     "non-local-player; world/character lane owns death motion + PK effects. " +
                     "spec: Docs/RE/packets/5-10_combat_death.yaml.");
            return;
        }

        // Local player died.
        // DeathCause 3 = special / no-modal — suppress the respawn modal entirely.
        // spec: Docs/RE/packets/5-10_combat_death.yaml — "DeathCause 3 = special / no-modal".
        if (evt.DeathCause == 3) // spec: 5-10_combat_death.yaml — cause 3 = special-no-modal
        {
            GD.Print("[HudMaster] ActorDied: local player, DeathCause=3 (special/no-modal) — " +
                     "respawn modal suppressed per spec. " +
                     "spec: Docs/RE/packets/5-10_combat_death.yaml.");
            return;
        }

        // DeathCause 0 = normal, 1 = PK-A, 2 = PK-B — show the respawn confirm modal.
        // spec: Docs/RE/packets/5-10_combat_death.yaml — "causes 0/1/2 show the respawn modal".
        // Reuse the existing ShowConfirm (mode-1 Yes/No) surface — the most faithful existing modal.
        // The respawn-modal message id and penalty magnitudes are value residuals (not modelled).
        // "Yes" = respawn at save point; "No" = stay dead (use-case call is world-campaign TODO).
        var causeLabel = evt.DeathCause switch
        {
            1 => " [PK-A]", // spec: 5-10_combat_death.yaml — cause 1 = PK type A
            2 => " [PK-B]", // spec: 5-10_combat_death.yaml — cause 2 = PK type B
            _ => string.Empty // cause 0 = normal
        };

        // Modal text: faithful placeholder — the exact CP949 message string comes from msg.xdb
        // (message id is a residual not yet resolved — TODO spec).
        // We show what we have: a respawn prompt with the cause annotation.
        // spec: Docs/RE/packets/5-10_combat_death.yaml — "respawn modal msg id = residual (value-pending)".
        var modalText = $"You have died{causeLabel}. Respawn?";

        GD.Print($"[HudMaster] ActorDied: local player cause={evt.DeathCause}{causeLabel} — " +
                 "showing respawn confirm modal. " +
                 "Respawn use-case TODO(world-campaign). " +
                 "Modal msg id = residual (TODO spec). " +
                 "spec: Docs/RE/packets/5-10_combat_death.yaml.");

        ShowConfirm(
            modalText,
            () =>
            {
                // TODO(world-campaign): IApplicationUseCases.Respawn() — respawn at save-point.
                // spec: Docs/RE/packets/5-10_combat_death.yaml — "Yes = respawn at save point".
                GD.Print("[HudMaster] Respawn confirmed — TODO(world-campaign): IApplicationUseCases.Respawn(). " +
                         "spec: Docs/RE/packets/5-10_combat_death.yaml.");
            },
            () =>
            {
                // Player chose to stay dead — no use-case needed (passive).
                // spec: Docs/RE/packets/5-10_combat_death.yaml — "No = stay dead (no C2S needed)".
                GD.Print("[HudMaster] Respawn declined — player stays dead. " +
                         "spec: Docs/RE/packets/5-10_combat_death.yaml.");
            });
    }
}