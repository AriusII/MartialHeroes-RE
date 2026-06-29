namespace MartialHeroes.Assets.Parsers.DataTables.Models;

public sealed class ActorSizeRecord
{
    public required uint ActorClassId { get; init; }

    public required float ScaleXz { get; init; }

    public required float ScaleY { get; init; }
}

public sealed class BuffIconPositionRecord
{
    public required uint BuffId { get; init; }

    public required int AtlasX { get; init; }

    public required int AtlasY { get; init; }
}

public sealed class EffectScaleRecord
{
    public required uint ObjectId { get; init; }

    public required float Scale { get; init; }
}

public sealed class VehicleXdbRecord
{
    public required uint VehicleId { get; init; }

    public required uint ItemId { get; init; }

    public required uint TagA { get; init; }

    public required uint TagB { get; init; }

    public required float Param0 { get; init; }

    public required float Param1 { get; init; }

    public required float Param2 { get; init; }

    public required float Param3 { get; init; }

    public required float Param4 { get; init; }

    public required float SeatYFacing1 { get; init; }

    public required float SeatYFacing2 { get; init; }

    public required float SeatYFacing3 { get; init; }

    public required float SeatYFacing4 { get; init; }
}

public sealed class CreatureItemXdbRecord
{
    public required uint CreatureKey { get; init; }

    public required uint ItemId { get; init; }

    public required float AttachF0 { get; init; }

    public required float AttachF1 { get; init; }

    public required float AttachF2 { get; init; }

    public required float AttachF3 { get; init; }

    public required float AttachF4 { get; init; }

    public required float AttachF5 { get; init; }

    public required float ScaleOrRadius { get; init; }

    public required float VisualScale { get; init; }

    public required byte Flag0 { get; init; }

    public required byte Flag1 { get; init; }

    public required byte Flag2 { get; init; }

    public required byte Flag3 { get; init; }

    public required uint TickInterval { get; init; }
}