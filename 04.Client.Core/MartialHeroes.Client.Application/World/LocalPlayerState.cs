using MartialHeroes.Client.Domain.Skills.Skills;
using MartialHeroes.Client.Domain.Stats.Stats;
using MartialHeroes.Shared.Kernel.Ids;

namespace MartialHeroes.Client.Application.World;

public sealed class LocalPlayerState
{
    public const int HotbarSlotCount = 240;
    private readonly SkillId[] _hotbar = new SkillId[HotbarSlotCount];
    private readonly short[] _hotbarPoints = new short[HotbarSlotCount];
    public CooldownTable Cooldowns { get; } = new();
    public BuffTable Buffs { get; } = new();
    public SkillCastState CastState { get; set; } = SkillCastState.Idle;
    public CombatStats Combat { get; set; } = CombatStats.Empty;
    public int ChosenSlotIndex { get; private set; } = -1;
    public ushort Level { get; private set; }
    public byte StatusByte { get; private set; }
    public bool HasEnterSeed { get; private set; }
    public long ClockMs { get; set; }
    public long CurrentHp { get; set; }
    public int CurrentStamina { get; set; }

    public void ConsumeCastCost(short hpCost, ushort staminaCost, int targetCount, int buffFactor)
    {
        if (hpCost > 0)
        {
            CurrentHp -= hpCost;
            if (CurrentHp < 0) CurrentHp = 0;
        }

        if (staminaCost > 0)
        {
            var scaled = (long)staminaCost * targetCount * buffFactor;
            var effective = scaled < staminaCost ? staminaCost : scaled;
            CurrentStamina -= (int)Math.Min(effective, int.MaxValue);
            if (CurrentStamina < 0) CurrentStamina = 0;
        }
    }

    public void SeedEnterChoice(int slotIndex, ushort level, byte statusByte)
    {
        ChosenSlotIndex = slotIndex;
        Level = level;
        StatusByte = statusByte;
        HasEnterSeed = true;
    }

    public void SetHotbarSlot(int slot, SkillId skill, short points, int cooldownDurationMs)
    {
        if ((uint)slot >= (uint)_hotbar.Length) throw new ArgumentOutOfRangeException(nameof(slot));

        _hotbar[slot] = skill;
        _hotbarPoints[slot] = points;
        Cooldowns.SetSlot(slot, skill, cooldownDurationMs);
    }
}