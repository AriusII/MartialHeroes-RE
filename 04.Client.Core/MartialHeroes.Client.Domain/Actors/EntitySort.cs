namespace MartialHeroes.Client.Domain.Actors;

/// <summary>
/// Entity category discriminator carried by every actor.
/// </summary>
/// <remarks>
/// Values are fixed by the legacy wire protocol: the actor's <c>sort</c> byte doubles as the
/// entity-type discriminator and as part of the actor-manager composite key <c>(id, sort)</c>.
/// spec: Docs/RE/structs/actor.md (Actor base fields, <c>sort</c> at +0x60: 1=PC, 2=Mob, 3=NPC).
/// Backed by <see cref="byte"/> to mirror the single-byte on-wire field
/// (spec: Docs/RE/packets/5-13_actor_movement_update.yaml, field <c>Sort</c> type u8).
/// </remarks>
public enum EntitySort : byte
{
    /// <summary>Unset / uninitialised category.</summary>
    None = 0,

    /// <summary>Player character. spec: Docs/RE/structs/actor.md (sort == 1).</summary>
    PlayerCharacter = 1,

    /// <summary>Monster / hostile mob. spec: Docs/RE/structs/actor.md (sort == 2).</summary>
    Monster = 2,

    /// <summary>Non-player character. spec: Docs/RE/structs/actor.md (sort == 3).</summary>
    NonPlayerCharacter = 3,
}
