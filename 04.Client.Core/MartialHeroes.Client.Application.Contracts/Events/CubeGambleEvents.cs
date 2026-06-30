namespace MartialHeroes.Client.Application.Contracts.Events;

public sealed record CubeGambleReelEvent(
    byte Phase,
    byte SpinSubKind,
    bool IsReset,
    bool SubmitOnLand,
    byte Phase5DieA,
    byte Phase5DieB,
    byte Phase4DieA,
    byte Phase4DieB,
    uint ThrowValue,
    long SettledMoney,
    bool Settled,
    long Delta,
    uint WinLines,
    sbyte SpecialSlot) : IClientEvent;
