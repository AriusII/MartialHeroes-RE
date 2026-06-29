namespace MartialHeroes.Assets.Parsers.Effects.Models;

public readonly record struct JointEffectEntry
{
    public required uint MapKey { get; init; }

    public required uint EffectId { get; init; }

    public required byte BoneNameMode { get; init; }

    public required int BoneId { get; init; }

    public required float Scale { get; init; }

    public required byte RotSource { get; init; }
}

public readonly record struct MobJointEffectEntry
{
    public required int ClassToken { get; init; }

    public required int OffsetToken { get; init; }

    public required JointEffectEntry Effect { get; init; }
}

public readonly record struct SwordLightEntry
{
    public required uint Key { get; init; }

    public required int Raw1 { get; init; }

    public required float R { get; init; }

    public required float G { get; init; }

    public required float B { get; init; }

    public required int HandSelector { get; init; }

    public required int Raw6 { get; init; }

    public required string TextureName { get; init; }
}
