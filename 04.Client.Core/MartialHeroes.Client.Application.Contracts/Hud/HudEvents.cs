using System.Collections.Immutable;
using MartialHeroes.Client.Domain.Actors.Actors;
using MartialHeroes.Shared.Kernel.Enums;

namespace MartialHeroes.Client.Application.Contracts.Hud;


public sealed record ChatLineEvent(
    int ChannelCode,
    string Text,
    uint ColorArgb,
    string? SenderName = null) : IHudEvent
{
    public const int ChannelSay = 0;

    public const int ChannelWhisper = 9;

    public const uint SayColorArgb = 0xFFFFFFFFu;
}

public readonly record struct BuffSlot(ushort BuffId, uint? RemainingTicks)
{
    public const ushort EmptyBuffId = 0;

    public bool IsEmpty => BuffId == EmptyBuffId;
}

public sealed record BuffStateEvent(ImmutableArray<BuffSlot> Slots) : IHudEvent
{
    public const int SlotCount = 30;

    public static BuffStateEvent FromSlots(ImmutableArray<BuffSlot> slots)
    {
        if (slots.Length != SlotCount)
            throw new ArgumentException(
                $"Buff state must carry exactly {SlotCount} slots (got {slots.Length}).", nameof(slots));

        return new BuffStateEvent(slots);
    }
}

public sealed record HudVitalsEvent(
    uint CurrentHp,
    uint MaxHp,
    uint CurrentMp,
    uint MaxMp,
    uint CurrentStamina,
    uint MaxStamina) : IHudEvent
{
    public static HudVitalsEvent None { get; } = new(0u, 0u, 0u, 0u, 0u, 0u);

    public float HpRatio =>
        MaxHp == 0u ? 0f : Math.Clamp((float)CurrentHp / MaxHp, 0f, 1f);

    public float MpRatio =>
        MaxMp == 0u ? 0f : Math.Clamp((float)CurrentMp / MaxMp, 0f, 1f);

    public float StaminaRatio =>
        MaxStamina == 0u ? 0f : Math.Clamp((float)CurrentStamina / MaxStamina, 0f, 1f);

    public bool IsEmpty =>
        CurrentHp == 0u && MaxHp == 0u && CurrentMp == 0u && MaxMp == 0u
        && CurrentStamina == 0u && MaxStamina == 0u;
}

public sealed record CombatTextEvent(
    ActorKey TargetKey,
    int Value,
    byte Kind,
    bool IsCrit,
    uint SkillId = 0u,
    long RawDamageCandidateA = 0L,
    long RawDamageCandidateB = 0L) : IHudEvent
{
    public const byte MinKind = 0;

    public const byte MaxKind = 7;
}

public sealed record TargetChangedEvent(
    ActorKey TargetKey,
    string Name,
    float HpRatio,
    float MpRatio) : IHudEvent
{
    public static TargetChangedEvent None { get; } =
        new(new ActorKey(ActorKey.UnassignedRawId, default), string.Empty, 0f, 0f);

    public bool IsCleared => TargetKey.RawId == ActorKey.UnassignedRawId;
}

public sealed record ZoneChangedEvent(
    ZoneType Zone) : IHudEvent;

public sealed record ExpLevelEvent(
    long CurrentXp,
    long XpForLevel,
    ushort Level) : IHudEvent
{
    public float Ratio =>
        XpForLevel <= 0 ? 0f : Math.Clamp((float)((double)CurrentXp / XpForLevel), 0f, 1f);
}