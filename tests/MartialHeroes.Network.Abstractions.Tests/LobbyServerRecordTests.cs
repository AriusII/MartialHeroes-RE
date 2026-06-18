using MartialHeroes.Network.Abstractions.Lobby;

namespace MartialHeroes.Network.Abstractions.Tests;

public sealed class LobbyServerRecordTests
{
    [Fact]
    public void PositionalConstruction_RoundTrips()
    {
        // spec: Docs/RE/packets/lobby.yaml Record Shape A — 4 × i16 packed entry
        var rec = new LobbyServerRecord(ServerId: 5, StatusCode: 0, Load: 600, OpenTime: 1);

        Assert.Equal((ushort)5, rec.ServerId);
        Assert.Equal((short)0, rec.StatusCode);
        Assert.Equal((short)600, rec.Load);
        Assert.Equal((short)1, rec.OpenTime);
    }

    [Fact]
    public void SelectabilityGate_IsStatusCodeZero_And_LoadUnder2400()
    {
        // spec: Docs/RE/packets/lobby.yaml Record Shape A [STATIC-CONFIRMED]:
        // a row is selectable iff status_code == 0 AND load < 2400 (0x960, signed strict less-than).
        // There is NO ServerId == 100 selectability gate.
        static bool Selectable(LobbyServerRecord r) => r.StatusCode == 0 && r.Load < 2400;

        Assert.True(Selectable(new LobbyServerRecord(ServerId: 1, StatusCode: 0, Load: 2399, OpenTime: 0)));
        Assert.False(
            Selectable(new LobbyServerRecord(ServerId: 1, StatusCode: 0, Load: 2400, OpenTime: 0))); // strict <
        Assert.False(
            Selectable(new LobbyServerRecord(ServerId: 1, StatusCode: 1, Load: 0, OpenTime: 0))); // status != 0
        // ServerId == 100 is NOT a gate: with status_code != 0 it is still NOT selectable.
        Assert.False(Selectable(new LobbyServerRecord(ServerId: 100, StatusCode: 2, Load: 0, OpenTime: 0)));
    }

    [Fact]
    public void StatusCodeThree_CarriesScheduledOpenTimeComponents()
    {
        // spec: Docs/RE/packets/lobby.yaml Record Shape A: status_code == 3 is the scheduled-open
        // branch — Load (+4) is the hour, OpenTime (+6) is the minute (a time value, NOT a flag).
        var rec = new LobbyServerRecord(ServerId: 1, StatusCode: 3, Load: 14, OpenTime: 30);
        Assert.Equal((short)3, rec.StatusCode);
        Assert.Equal((short)14, rec.Load); // hour
        Assert.Equal((short)30, rec.OpenTime); // minute
    }

    [Fact]
    public void Equality_IsValueBased()
    {
        var a = new LobbyServerRecord(1, 0, 500, 1);
        var b = new LobbyServerRecord(1, 0, 500, 1);
        Assert.Equal(a, b);
    }
}