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
    uint DeltaCon,
    uint RemainingStatPoints) : IHudEvent
{
    public static StatAllocationView Empty { get; } = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    public long PendingTotal => (long)DeltaStr + DeltaInt + DeltaAgi + DeltaDex + DeltaCon;

    public long PointsAvailable => Math.Max(0, RemainingStatPoints - PendingTotal);

    public bool HasPendingAllocation => PendingTotal > 0;

    public uint AbsoluteStr => BaseStr + DeltaStr;

    public uint AbsoluteInt => BaseInt + DeltaInt;

    public uint AbsoluteAgi => BaseAgi + DeltaAgi;

    public uint AbsoluteDex => BaseDex + DeltaDex;

    public uint AbsoluteCon => BaseCon + DeltaCon;
}