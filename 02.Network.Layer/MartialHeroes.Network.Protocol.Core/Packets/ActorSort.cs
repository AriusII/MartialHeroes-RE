// spec: Docs/RE/structs/actor.md — `sort` field (entity-category discriminator).

namespace MartialHeroes.Network.Protocol.Core.Packets;

/// <summary>
///     Entity-category discriminator carried by actor packets. On the wire <c>sort</c> is read as a
///     full dword in some packets (5/0, 5/3) and as a single byte in others (5/13); the real value is
///     always the low byte. spec: Docs/RE/structs/actor.md (<c>sort</c>: 1=PC, 2=Mob, 3=NPC, 4+=other).
/// </summary>
/// <remarks>
///     CAPTURE-UNVERIFIED. This enum is a typed view over the low byte; wire structs keep the raw
///     integer field to preserve the exact <c>Pack=1</c> layout, and expose this via a helper accessor.
/// </remarks>
public enum ActorSort : byte
{
    /// <summary>Unknown / uninitialised.</summary>
    Unknown = 0,

    /// <summary>Player character. spec: Docs/RE/structs/actor.md.</summary>
    PlayerCharacter = 1,

    /// <summary>Monster / mob. spec: Docs/RE/structs/actor.md.</summary>
    Mob = 2,

    /// <summary>Non-player character. spec: Docs/RE/structs/actor.md.</summary>
    Npc = 3
}