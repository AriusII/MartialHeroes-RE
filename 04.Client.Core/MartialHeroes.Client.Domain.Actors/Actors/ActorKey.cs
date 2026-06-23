namespace MartialHeroes.Client.Domain.Actors.Actors;

public readonly record struct ActorKey(uint RawId, EntitySort Sort)
{
    public const uint UnassignedRawId = 0xFFFFFFFFu;
}