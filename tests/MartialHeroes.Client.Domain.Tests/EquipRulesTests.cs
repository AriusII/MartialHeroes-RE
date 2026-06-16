using MartialHeroes.Client.Domain.Inventory;
using MartialHeroes.Client.Domain.Stats;
using MartialHeroes.Shared.Kernel.Enums;
using Xunit;

namespace MartialHeroes.Client.Domain.Tests;

public sealed class EquipRulesTests
{
    [Fact]
    public void CheckEquip_AllClear_Allowed()
    {
        EquipCheckResult result = EquipRules.CheckEquip(
            itemIndex: 3, destinationSlot: 0, EquipStateGates.AllClear, default);

        Assert.Equal(EquipCheckResult.Allowed, result);
    }

    [Fact]
    public void CheckEquip_NegativeIndex_InvalidIndex()
    {
        EquipCheckResult result = EquipRules.CheckEquip(-1, 0, EquipStateGates.AllClear, default);
        Assert.Equal(EquipCheckResult.InvalidIndex, result);
    }

    [Theory]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public void CheckEquip_StateGateFails_StateBlocked(bool inGame, bool notBusy, bool notDead)
    {
        var gates = new EquipStateGates { InGame = inGame, NotBusy = notBusy, NotDead = notDead };

        EquipCheckResult result = EquipRules.CheckEquip(0, 0, gates, default);

        Assert.Equal(EquipCheckResult.StateBlocked, result);
    }

    [Fact]
    public void CheckEquip_Slot8RelationGuard_Blocks_WhenAllConditionsHold()
    {
        var relation = new EquipRelationContext
        {
            BothActorsExist = true,
            ActorsAreDifferent = true,
            SlotActorContextId = 42,
            OtherActorContextId = 42,
        };

        EquipCheckResult result = EquipRules.CheckEquip(
            itemIndex: 0, destinationSlot: EquipSlots.StatExcludedSlot, EquipStateGates.AllClear, relation);

        Assert.Equal(EquipCheckResult.RelationGuardBlocked, result);
    }

    [Fact]
    public void CheckEquip_Slot8Guard_Allowed_WhenContextDiffers()
    {
        var relation = new EquipRelationContext
        {
            BothActorsExist = true,
            ActorsAreDifferent = true,
            SlotActorContextId = 1,
            OtherActorContextId = 2,
        };

        EquipCheckResult result =
            EquipRules.CheckEquip(0, EquipSlots.StatExcludedSlot, EquipStateGates.AllClear, relation);

        Assert.Equal(EquipCheckResult.Allowed, result);
    }

    [Fact]
    public void CheckEquip_Slot8Guard_NotAppliedToOtherSlots()
    {
        var relation = new EquipRelationContext
        {
            BothActorsExist = true,
            ActorsAreDifferent = true,
            SlotActorContextId = 42,
            OtherActorContextId = 42,
        };

        // Destination slot 7 (not 8) → guard does not apply.
        EquipCheckResult result = EquipRules.CheckEquip(0, 7, EquipStateGates.AllClear, relation);

        Assert.Equal(EquipCheckResult.Allowed, result);
    }

    [Fact]
    public void CheckRequirements_LevelAndClass_AndStats()
    {
        var req = new EquipRequirement
        {
            RequiredLevel = 10,
            // Warrior -> Musa (the martial/melee class). spec: Docs/RE/formats/config_tables.md §2.6.
            RequiredClass = CharacterClass.Musa,
            RequiredStr = 20,
        };

        var meets = new EquipCandidateStats(Level: 10, CharacterClass.Musa, Str: 20, 0, 0, 0, 0);
        var tooLow = new EquipCandidateStats(Level: 9, CharacterClass.Musa, Str: 20, 0, 0, 0, 0);
        // "wrong class" needs a class DIFFERENT from the required Musa to exercise the class-mismatch
        // path; the old Mage (caster) maps to Dosa. spec: Docs/RE/formats/config_tables.md §2.6.
        var wrongClass = new EquipCandidateStats(Level: 10, CharacterClass.Dosa, Str: 20, 0, 0, 0, 0);
        var weakStr = new EquipCandidateStats(Level: 10, CharacterClass.Musa, Str: 19, 0, 0, 0, 0);

        Assert.True(EquipRules.CheckRequirements(req, meets));
        Assert.False(EquipRules.CheckRequirements(req, tooLow));
        Assert.False(EquipRules.CheckRequirements(req, wrongClass));
        Assert.False(EquipRules.CheckRequirements(req, weakStr));
    }

    [Fact]
    public void CheckRequirements_NullClass_MatchesAny()
    {
        var req = new EquipRequirement { RequiredLevel = 1, RequiredClass = null };
        // Assassin -> Jagaek (the assassin/swift-strike class); the null RequiredClass matches any
        // class so the candidate's class is incidental here. spec: Docs/RE/formats/config_tables.md §2.6.
        var stats = new EquipCandidateStats(5, CharacterClass.Jagaek, 0, 0, 0, 0, 0);

        Assert.True(EquipRules.CheckRequirements(req, stats));
    }

    [Fact]
    public void RecomputeEquipmentContributions_SkipsSlot8()
    {
        ReadOnlySpan<SlottedEquipmentContribution> worn =
        [
            new SlottedEquipmentContribution(0, new EquipmentContribution(StatKey.Str, 5)),
            new SlottedEquipmentContribution(EquipSlots.StatExcludedSlot, new EquipmentContribution(StatKey.Str, 99)),
            new SlottedEquipmentContribution(2, new EquipmentContribution(StatKey.Dex, 7)),
        ];

        Span<EquipmentContribution> dest = stackalloc EquipmentContribution[3];
        int n = EquipRules.RecomputeEquipmentContributions(worn, dest);

        Assert.Equal(2, n); // slot-8 entry dropped.
        Assert.Equal(new EquipmentContribution(StatKey.Str, 5), dest[0]);
        Assert.Equal(new EquipmentContribution(StatKey.Dex, 7), dest[1]);

        // And the result feeds StatAggregation correctly.
        int str = StatAggregation.Aggregate(StatKey.Str, 0, [], dest[..n], [], []);
        Assert.Equal(5, str); // slot-8 +99 excluded.
    }
}