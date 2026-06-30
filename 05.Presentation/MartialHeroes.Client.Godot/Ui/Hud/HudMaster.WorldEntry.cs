using Godot;
using MartialHeroes.Client.Application.Contracts.Events;

namespace MartialHeroes.Client.Godot.Ui.Hud;

public sealed partial class HudMaster
{
    public void OnHotbarInitialized(HotbarInitializedEvent evt)
    {
        _skillHotbar?.OnHotbarInitialized(evt);
    }

    public void OnRosterSnapshot(RosterSnapshotEvent evt)
    {
        _partyWindow?.OnRosterSnapshot(evt);
    }

    public void OnPartyMemberJoined(PartyMemberJoinedEvent evt)
    {
        _partyWindow?.OnPartyMemberJoined(evt);
    }

    public void OnPartyMemberRemoved(PartyMemberRemovedEvent evt)
    {
        _partyWindow?.OnPartyMemberRemoved(evt);
    }

    public void OnPartyMemberVitals(PartyMemberVitalsEvent evt)
    {
        _partyWindow?.OnPartyMemberVitals(evt);
    }

    public void OnPartyInviteState(PartyInviteStateEvent evt)
    {
        _partyWindow?.OnPartyInviteState(evt);
    }

    public void OnPartyAcceptResult(PartyAcceptResultEvent evt)
    {
        _partyWindow?.OnPartyAcceptResult(evt);
    }

    public void OnGuildRoster(GuildRosterEvent evt)
    {
        _guildWindow?.OnGuildRoster(evt);
    }

    public void OnGuildMemberPatch(GuildMemberPatchEvent evt)
    {
        _guildWindow?.OnGuildMemberPatch(evt);
    }

    public void OnGuildStateChanged(GuildStateChangedEvent evt)
    {
        _guildWindow?.OnGuildStateChanged(evt);
    }

    public void OnActionError(ActionErrorEvent evt)
    {
        _errorPanel?.ShowActionError(evt.Status, evt.Error);
    }

    public void OnPopupCode(PopupCodeEvent evt)
    {
        _announcePanel?.ShowPopupCode(evt.PopupCode);
    }

    public void OnActorDied(ActorDiedEvent evt)
    {
        if (!evt.IsLocalPlayer)
        {
            GD.Print($"[HudMaster] ActorDied: victim={evt.VictimKey} killer={evt.KillerKey} " +
                     $"cause={evt.DeathCause} isPkA={evt.IsPkA} isPkB={evt.IsPkB} — " +
                     "non-local-player; world/character lane owns death motion + PK effects. " +
                     "spec: Docs/RE/packets/5-10_combat_death.yaml.");
            return;
        }

        if (evt.DeathCause == 3)
        {
            GD.Print("[HudMaster] ActorDied: local player, DeathCause=3 (special/no-modal) — " +
                     "respawn modal suppressed per spec. " +
                     "spec: Docs/RE/packets/5-10_combat_death.yaml.");
            return;
        }

        var causeLabel = evt.DeathCause switch
        {
            1 => " [PK-A]",
            2 => " [PK-B]",
            _ => string.Empty
        };

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
                GD.Print("[HudMaster] Respawn confirmed — TODO(world-campaign): IApplicationUseCases.Respawn(). " +
                         "spec: Docs/RE/packets/5-10_combat_death.yaml.");
            },
            () =>
            {
                GD.Print("[HudMaster] Respawn declined — player stays dead. " +
                         "spec: Docs/RE/packets/5-10_combat_death.yaml.");
            });
    }
}