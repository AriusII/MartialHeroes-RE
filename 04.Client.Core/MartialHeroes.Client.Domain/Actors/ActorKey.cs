namespace MartialHeroes.Client.Domain.Actors;

/// <summary>
/// The composite identity used to look up a live actor.
/// </summary>
/// <remarks>
/// The legacy actor-manager keys actors by the pair <c>(id, sort)</c>, and both parts participate
/// in lookups. spec: Docs/RE/structs/actor.md ("<c>sort</c> doubles as the entity-type
/// discriminator AND part of the actor-manager composite key <c>(id, sort)</c>. Lookups use both.").
/// The raw id is a server-assigned 32-bit value (spec: <c>id</c> at +0x5C, int32, initialised to
/// 0xFFFFFFFF); it is kept as <see cref="uint"/> here and projected to a strongly-typed
/// <c>PlayerId</c>/<c>MonsterId</c> by the caller based on <see cref="Sort"/>.
/// </remarks>
public readonly record struct ActorKey(uint RawId, EntitySort Sort)
{
    /// <summary>The sentinel raw id used by the legacy client before spawn (0xFFFFFFFF).</summary>
    /// <remarks>spec: Docs/RE/structs/actor.md (<c>id</c> at +0x5C "Initialised to 0xFFFFFFFF").</remarks>
    public const uint UnassignedRawId = 0xFFFFFFFFu;
}