namespace MartialHeroes.Client.Application.Contracts.Hud;

public sealed record StatAllocationView(
    uint BaseStr,
    uint BaseInt,
    uint BaseAgi,
    uint BaseDex,
    uint BaseCon,
    uint DeltaStr,
    uint DeltaInt,
    uint DeltaAgi,
    uint DeltaDex,
    uint DeltaCon) : IHudEvent;