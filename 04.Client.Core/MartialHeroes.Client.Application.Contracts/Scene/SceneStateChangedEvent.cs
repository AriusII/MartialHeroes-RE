using MartialHeroes.Client.Application.Contracts.Events;
using MartialHeroes.Shared.Kernel.State;

namespace MartialHeroes.Client.Application.Contracts.Scene;

public sealed record SceneStateChangedEvent(GameState Previous, GameState Next) : IClientEvent;