namespace MartialHeroes.Client.Application.Contracts.Events;

public sealed record ActionErrorEvent(byte Status, byte Error) : IClientEvent;

public sealed record ShopPageRefreshedEvent(uint Money) : IClientEvent;
