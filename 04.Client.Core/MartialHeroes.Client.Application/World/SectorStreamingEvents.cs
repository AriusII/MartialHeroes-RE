using MartialHeroes.Client.Application.Contracts.Events;

namespace MartialHeroes.Client.Application.World;

public sealed record SectorLoadedEvent(int MapX, int MapZ, ReadOnlyMemory<byte> Payload) : IClientEvent;

public sealed record SectorUnloadedEvent(int MapX, int MapZ) : IClientEvent;