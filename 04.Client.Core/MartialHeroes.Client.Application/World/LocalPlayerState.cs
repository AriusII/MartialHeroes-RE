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