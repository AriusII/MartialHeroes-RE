using MartialHeroes.Client.Application.Handlers;
using MartialHeroes.Client.Application.UseCases;
using MartialHeroes.Client.Domain.Actors;
using MartialHeroes.Client.Domain.Stats;
using Xunit;

namespace MartialHeroes.Client.Application.Tests;

public sealed class CatalogueVitalsResolverTests
{
    /// <summary>A catalogue that returns a fixed HP curve and an empty MP curve.</summary>
    private sealed class FakeCatalogue(StatBaseCurve hp, StatBaseCurve mp) : IStatCatalogueSource
    {
        public StatBaseCurve GetHpBaseCurve() => hp;
        public StatBaseCurve GetMpBaseCurve() => mp;
    }

    // ServerClass 1 -> ClassId 1 -> HP class multiplier 0.3 (non-zero), so the injected HP base curve
    // actually flows through Stage 3 of VitalFormula. spec: stats.md (CLASS_HP_TABLE index 1 = 0.3).
    private static SpawnInfo Spawn(ushort level, uint currentHp = 0, uint currentMp = 0) =>
        new(new ActorKey(1, EntitySort.PlayerCharacter), level, currentHp, currentMp, 0, ServerClass: 1);

    [Fact]
    public void Empty_catalogue_preserves_server_current_values_as_floor()
    {
        Func<SpawnInfo, VitalStats> resolver =
            CatalogueVitalsResolver.Create(EmptyStatCatalogueSource.Instance);

        VitalStats vitals = resolver(Spawn(level: 5, currentHp: 250, currentMp: 100));

        // With an empty curve and provisional zero bases, the guard never clamps the server-sent
        // current values below what was reported. spec: stats.md (server enforces the cap).
        Assert.True(vitals.MaxHp >= 250u);
        Assert.True(vitals.MaxMp >= 100u);
    }

    [Fact]
    public void Injected_hp_curve_raises_max_hp_above_the_reported_current()
    {
        // A dense 1-based curve: level 1 -> 1000, level 2 -> 2000, level 3 -> 3000 HP base.
        var hpCurve = new StatBaseCurve(new long[] { 1000, 2000, 3000 });
        Func<SpawnInfo, VitalStats> resolver =
            CatalogueVitalsResolver.Create(new FakeCatalogue(hpCurve, StatBaseCurve.Empty));

        VitalStats low = resolver(Spawn(level: 1, currentHp: 10));
        VitalStats high = resolver(Spawn(level: 3, currentHp: 10));

        // The injected curve contributes to the formula base, so a higher level yields a higher max.
        // spec: Docs/RE/formats/config_tables.md §2.4 (userlevel.scr per-level base feeds the formula).
        Assert.True(high.MaxHp > low.MaxHp);
    }
}