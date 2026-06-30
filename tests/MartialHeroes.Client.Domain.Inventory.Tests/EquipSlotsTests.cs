using MartialHeroes.Client.Domain.Inventory.Inventory;
using Xunit;

namespace MartialHeroes.Client.Domain.Inventory.Tests;

public sealed class EquipSlotsTests
{
    [Fact]
    public void Slot_eight_is_excluded_from_the_stat_sum()
    {
        Assert.True(EquipSlots.IsExcludedFromStatSum(EquipSlots.StatExcludedSlot));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(14)]
    [InlineData(15)]
    [InlineData(19)]
    public void Other_slots_are_not_excluded_from_the_stat_sum(int slot)
    {
        Assert.False(EquipSlots.IsExcludedFromStatSum(slot));
    }

    [Fact]
    public void Appearance_slot_triggers_the_visual_rebuild()
    {
        Assert.True(EquipSlots.TriggersVisualRebuild(EquipSlots.AppearanceSlot));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(8)]
    [InlineData(14)]
    [InlineData(16)]
    public void Non_appearance_slots_do_not_trigger_the_visual_rebuild(int slot)
    {
        Assert.False(EquipSlots.TriggersVisualRebuild(slot));
    }
}
