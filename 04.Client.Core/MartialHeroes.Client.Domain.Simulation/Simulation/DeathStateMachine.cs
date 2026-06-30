namespace MartialHeroes.Client.Domain.Simulation.Simulation;

public enum ActorDeathOp : byte
{
    NoOp,
    KilledByVisual,
    SpawnDeathEffect,
    SetDeathSubState,
    Revive
}

public readonly record struct ActorDeathStateResolution(
    ActorDeathOp Op,
    uint EffectId,
    byte DeathSubState,
    bool ConsumesKiller);

public enum PvpDeathFxOp : byte
{
    NoOp,
    Engage,
    Disengage
}

public readonly record struct PvpDeathFxResolution(
    PvpDeathFxOp Op,
    bool SpawnAura,
    uint AuraEffectId,
    bool DeactivateAura,
    uint DeactivateEffectId,
    bool SpawnBurst,
    uint BurstEffectId);

public static class DeathStateMachine
{
    private const uint DeathEffectBase = 350000038u;
    private const uint PvpEngageAuraEffectId = 371003701u;
    private const uint PvpDisengageBurstEffectId = 371003702u;

    public static ActorDeathStateResolution ResolveActorDeathState(byte mode, uint subSelector)
    {
        switch (mode)
        {
            case 0:
                return new ActorDeathStateResolution(ActorDeathOp.KilledByVisual, 0u, 0, true);

            case 1:
                var effectId = subSelector is >= 1u and <= 7u ? DeathEffectBase + subSelector : 0u;
                return new ActorDeathStateResolution(ActorDeathOp.SpawnDeathEffect, effectId, 0, false);

            case 2:
                var subState = subSelector == 1u ? (byte)6 : (byte)7;
                return new ActorDeathStateResolution(ActorDeathOp.SetDeathSubState, 0u, subState, false);

            case 3:
                return new ActorDeathStateResolution(ActorDeathOp.Revive, 0u, 0, false);

            default:
                return new ActorDeathStateResolution(ActorDeathOp.NoOp, 0u, 0, false);
        }
    }

    public static PvpDeathFxResolution ResolvePvpDeathFx(byte mode, byte gate)
    {
        switch (mode)
        {
            case 1:
                return new PvpDeathFxResolution(
                    PvpDeathFxOp.Engage, gate == 1, PvpEngageAuraEffectId, false, 0u, false, 0u);

            case 6:
                return new PvpDeathFxResolution(
                    PvpDeathFxOp.Disengage, false, 0u, true, PvpEngageAuraEffectId, gate == 1, PvpDisengageBurstEffectId);

            default:
                return new PvpDeathFxResolution(
                    PvpDeathFxOp.NoOp, false, 0u, false, 0u, false, 0u);
        }
    }
}
