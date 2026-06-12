using MartialHeroes.Client.Domain.Skills;
using MartialHeroes.Shared.Kernel.Ids;
using MartialHeroes.Shared.Kernel.Numerics;
using Xunit;

namespace MartialHeroes.Client.Domain.Tests;

public sealed class SkillCastStateTests
{
    private static SkillDefinition Skill(
        uint id = 100,
        ushort category = 2,
        short mpFactor = 0,
        ushort cooldownCs = 0,
        SkillTargetMode mode = SkillTargetMode.SingleTarget,
        float baseRange = 5f,
        bool weaponReq = false) => new()
    {
        Id = new SkillId(id),
        Category = category,
        TargetMode = mode,
        BaseRange = baseRange,
        MpCostFactor = mpFactor,
        CooldownCentiseconds = cooldownCs,
        WeaponReqActive = weaponReq,
    };

    /// <summary>A targeting query that always passes range / LoS / target-state. Inject overrides for failures.</summary>
    private sealed class StubTargeting : ISkillTargetingQuery
    {
        public float CasterBodyRadius { get; init; }
        public float BuffRangeBonus { get; init; }
        public float SquaredDistance { get; init; }
        public bool LineOfSight { get; init; } = true;
        public bool TargetValid { get; init; } = true;

        public float SquaredPlanarDistanceToAim(in Vector3Fixed aimPoint) => SquaredDistance;
        public bool HasLineOfSight(in Vector3Fixed aimPoint) => LineOfSight;
        public bool IsTargetStateValid(bool isReviveSkill) => TargetValid;
    }

    private static (SkillCastState State, CooldownTable Cooldowns) Setup()
        => (SkillCastState.Idle, new CooldownTable());

    [Fact]
    public void TryBeginCast_AllClear_Succeeds_AndEntersCasting()
    {
        var (state, cd) = Setup();
        SkillDefinition skill = Skill();

        var (next, result) = state.TryBeginCast(
            skill, CasterState.AllClear, cd, new StubTargeting(), Vector3Fixed.Zero, now: 1000);

        Assert.Equal(SkillCastResult.Ok, result);
        Assert.Equal(SkillCastPhase.Casting, next.Phase);
        Assert.Equal(skill.Id, next.ActiveSkill);
        Assert.Equal(1000 + SkillCastState.DefaultCastTimeMs, next.CastEndMs);
    }

    [Fact]
    public void TryBeginCast_WhileCasting_RejectedAsAlreadyCasting()
    {
        var (state, cd) = Setup();
        SkillDefinition skill = Skill();
        (state, _) = state.TryBeginCast(skill, CasterState.AllClear, cd, new StubTargeting(), Vector3Fixed.Zero, 0);

        var (next, result) =
            state.TryBeginCast(skill, CasterState.AllClear, cd, new StubTargeting(), Vector3Fixed.Zero, 10);

        Assert.Equal(SkillCastResult.AlreadyCasting, result);
        Assert.Equal(SkillCastPhase.Casting, next.Phase);
    }

    [Theory]
    [InlineData(nameof(CasterState.PartyRelationAllied), SkillCastResult.PartyRelation)]
    [InlineData(nameof(CasterState.BillingRankOk), SkillCastResult.BillingOrRank)]
    [InlineData(nameof(CasterState.IsAlive), SkillCastResult.NotAlive)]
    [InlineData(nameof(CasterState.SelfCastEligible), SkillCastResult.SelfCastIneligible)]
    [InlineData(nameof(CasterState.CastWindowOpen), SkillCastResult.CastWindowTiming)]
    [InlineData(nameof(CasterState.HasTargets), SkillCastResult.NoTargets)]
    public void Validate_FailingAllowGate_ReturnsItsCode(string flag, SkillCastResult expected)
    {
        var cd = new CooldownTable();
        CasterState caster = flag switch
        {
            nameof(CasterState.PartyRelationAllied) => CasterState.AllClear with { PartyRelationAllied = false },
            nameof(CasterState.BillingRankOk) => CasterState.AllClear with { BillingRankOk = false },
            nameof(CasterState.IsAlive) => CasterState.AllClear with { IsAlive = false },
            nameof(CasterState.SelfCastEligible) => CasterState.AllClear with { SelfCastEligible = false },
            nameof(CasterState.CastWindowOpen) => CasterState.AllClear with { CastWindowOpen = false },
            nameof(CasterState.HasTargets) => CasterState.AllClear with { HasTargets = false },
            _ => CasterState.AllClear,
        };

        SkillCastResult result = SkillCastValidator.Validate(
            Skill(), caster, cd, new StubTargeting(), Vector3Fixed.Zero, 0);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void Validate_BusyCasting_ReturnsAlreadyCasting()
    {
        SkillCastResult result = SkillCastValidator.Validate(
            Skill(), CasterState.AllClear with { IsBusyCasting = true }, new CooldownTable(),
            new StubTargeting(), Vector3Fixed.Zero, 0);

        Assert.Equal(SkillCastResult.AlreadyCasting, result);
    }

    [Fact]
    public void Validate_NotEnoughMp_Uses100xFactor()
    {
        // mp_cost_factor 3 → gate fails below 300. spec: skills.md §2.1 gate 14.
        SkillDefinition skill = Skill(mpFactor: 3);
        var cd = new CooldownTable();

        SkillCastResult blocked = SkillCastValidator.Validate(
            skill, CasterState.AllClear with { AvailableMp = 299 }, cd, new StubTargeting(), Vector3Fixed.Zero, 0);
        SkillCastResult ok = SkillCastValidator.Validate(
            skill, CasterState.AllClear with { AvailableMp = 300 }, cd, new StubTargeting(), Vector3Fixed.Zero, 0);

        Assert.Equal(SkillCastResult.NotEnoughMp, blocked);
        Assert.Equal(SkillCastResult.Ok, ok);
    }

    [Fact]
    public void Validate_OutOfRange_ReturnsMoveCloser_NoToast()
    {
        // effective range = base 5 + radius 1 + buff 0 = 6; squared 36. Beyond → move-closer.
        var targeting = new StubTargeting { CasterBodyRadius = 1f, SquaredDistance = 36.0001f };

        SkillCastResult result = SkillCastValidator.Validate(
            Skill(baseRange: 5f), CasterState.AllClear, new CooldownTable(), targeting, Vector3Fixed.Zero, 0);

        Assert.Equal(SkillCastResult.MoveCloser, result);
    }

    [Fact]
    public void Validate_LineOfSightBlocked_ReturnsCode9()
    {
        var targeting = new StubTargeting { SquaredDistance = 0f, LineOfSight = false };

        SkillCastResult result = SkillCastValidator.Validate(
            Skill(), CasterState.AllClear, new CooldownTable(), targeting, Vector3Fixed.Zero, 0);

        Assert.Equal(SkillCastResult.LineOfSightBlocked, result);
    }

    [Fact]
    public void Validate_InvalidTarget_ReturnsCode10_ExceptGroundMode()
    {
        var targeting = new StubTargeting { TargetValid = false };

        SkillCastResult single = SkillCastValidator.Validate(
            Skill(mode: SkillTargetMode.SingleTarget), CasterState.AllClear, new CooldownTable(), targeting,
            Vector3Fixed.Zero, 0);
        SkillCastResult ground = SkillCastValidator.Validate(
            Skill(mode: SkillTargetMode.GroundPoint), CasterState.AllClear, new CooldownTable(), targeting,
            Vector3Fixed.Zero, 0);

        Assert.Equal(SkillCastResult.InvalidTarget, single);
        Assert.Equal(SkillCastResult.Ok, ground); // ground mode skips the target-state test. spec: §2.2.
    }

    [Fact]
    public void Validate_WeaponRequirement_OnlyWhenActive()
    {
        SkillDefinition skill = Skill(weaponReq: true);
        CasterState caster = CasterState.AllClear with { WeaponRequirementSatisfied = false };

        SkillCastResult blocked = SkillCastValidator.Validate(skill, caster, new CooldownTable(), new StubTargeting(),
            Vector3Fixed.Zero, 0);
        SkillCastResult ignored = SkillCastValidator.Validate(Skill(weaponReq: false), caster, new CooldownTable(),
            new StubTargeting(), Vector3Fixed.Zero, 0);

        Assert.Equal(SkillCastResult.WeaponRequirement, blocked);
        Assert.Equal(SkillCastResult.Ok, ignored);
    }

    [Fact]
    public void Validate_OnCooldown_BlocksUnlessExemptCategory()
    {
        // Skill on cooldown; non-exempt category blocks, basic-attack category 1 is exempt.
        var cd = new CooldownTable();
        cd.SetSlot(0, new SkillId(100), durationMs: 1000);
        cd.Arm(new SkillId(100), now: 0);

        SkillCastResult blocked = SkillCastValidator.Validate(
            Skill(id: 100, category: 2), CasterState.AllClear, cd, new StubTargeting(), Vector3Fixed.Zero, now: 500);

        // Exempt category 1 (basic attack) is not gated by cooldown. spec: §2.1 gate 13 / §4.
        var cdExempt = new CooldownTable();
        cdExempt.SetSlot(0, new SkillId(101), durationMs: 1000);
        cdExempt.Arm(new SkillId(101), 0);
        SkillCastResult exempt = SkillCastValidator.Validate(
            Skill(id: 101, category: SkillDefinition.BasicAttackCategory), CasterState.AllClear, cdExempt,
            new StubTargeting(), Vector3Fixed.Zero, 500);

        Assert.Equal(SkillCastResult.OnCooldown, blocked);
        Assert.Equal(SkillCastResult.Ok, exempt);
    }

    [Fact]
    public void ConfirmCast_ArmsCooldown_AndEntersCooldownPhase()
    {
        var cd = new CooldownTable();
        SkillDefinition skill = Skill(id: 100, category: 2, cooldownCs: 30); // 3000 ms
        cd.SetSlot(5, skill.Id, skill.CooldownMs);
        var (state, _) = SkillCastState.Idle.TryBeginCast(skill, CasterState.AllClear, cd, new StubTargeting(),
            Vector3Fixed.Zero, 0);

        SkillCastState next = state.ConfirmCast(skill, cd, now: 1000);

        Assert.Equal(SkillCastPhase.Cooldown, next.Phase);
        Assert.Equal(1000 + 3000, next.CooldownEndMs);
        Assert.False(cd[5].IsReady); // armed.
    }

    [Fact]
    public void ConfirmCast_ExemptCategory_GoesIdle_NoCooldown()
    {
        var cd = new CooldownTable();
        SkillDefinition skill = Skill(id: 100, category: SkillDefinition.BasicAttackCategory, cooldownCs: 30);
        var (state, _) = SkillCastState.Idle.TryBeginCast(skill, CasterState.AllClear, cd, new StubTargeting(),
            Vector3Fixed.Zero, 0);

        SkillCastState next = state.ConfirmCast(skill, cd, now: 1000);

        Assert.Equal(SkillCastPhase.Idle, next.Phase);
    }

    [Fact]
    public void Tick_CooldownEnds_ReturnsToIdle()
    {
        var cd = new CooldownTable();
        SkillDefinition skill = Skill(id: 100, category: 2, cooldownCs: 10); // 1000 ms
        cd.SetSlot(0, skill.Id, skill.CooldownMs);
        var (state, _) = SkillCastState.Idle.TryBeginCast(skill, CasterState.AllClear, cd, new StubTargeting(),
            Vector3Fixed.Zero, 0);
        state = state.ConfirmCast(skill, cd, now: 0);

        Assert.Equal(SkillCastPhase.Cooldown, state.Tick(999).Phase);
        Assert.Equal(SkillCastPhase.Idle, state.Tick(1000).Phase);
    }

    [Fact]
    public void Cancel_FromCasting_ReturnsToIdle()
    {
        var cd = new CooldownTable();
        var (state, _) = SkillCastState.Idle.TryBeginCast(Skill(), CasterState.AllClear, cd, new StubTargeting(),
            Vector3Fixed.Zero, 0);

        Assert.Equal(SkillCastPhase.Idle, state.Cancel().Phase);
    }
}