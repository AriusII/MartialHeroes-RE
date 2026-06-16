using MartialHeroes.Network.Abstractions.Lobby;

namespace MartialHeroes.Network.Abstractions.Tests;

public sealed class LobbyServerRecordTests
{
    [Fact]
    public void PositionalConstruction_RoundTrips()
    {
        // spec: Docs/RE/packets/lobby.yaml §RECORD SHAPE A — 4 × i16 packed entry
        var rec = new LobbyServerRecord(ServerId: 5, Status: 0, Population: 600, Flag: 1);

        Assert.Equal((ushort)5, rec.ServerId);
        Assert.Equal((short)0, rec.Status);
        Assert.Equal((short)600, rec.Population);
        Assert.Equal((short)1, rec.Flag);
    }

    [Fact]
    public void AvailabilitySentinel_IsOnServerId_Not_Status()
    {
        // spec: Docs/RE/packets/lobby.yaml §RECORD SHAPE A +0 id_selectkey [CODE-CONFIRMED]:
        // the 100 availability sentinel gates the select buttons on ServerId (+0), NOT Status (+2).
        var rec = new LobbyServerRecord(ServerId: 100, Status: 0, Population: 200, Flag: 1);
        Assert.Equal((ushort)100, rec.ServerId);
    }

    [Fact]
    public void Flag_IsZero_MeansDiscreteLoadLevel()
    {
        // spec: Docs/RE/packets/lobby.yaml §RECORD SHAPE A +6 flag [CODE-CONFIRMED]:
        // Flag==0 -> treat Population as discrete load level (switch on 2/3/4)
        var rec = new LobbyServerRecord(ServerId: 1, Status: 0, Population: 3, Flag: 0);
        Assert.Equal((short)0, rec.Flag);
        Assert.Equal((short)3, rec.Population);
    }

    [Fact]
    public void Flag_IsNonzero_MeansNumericPopulation()
    {
        // spec: Docs/RE/packets/lobby.yaml §RECORD SHAPE A +6 flag [CODE-CONFIRMED]:
        // Flag!=0 -> treat Population as numeric (thresholds 500/800/1200)
        var rec = new LobbyServerRecord(ServerId: 2, Status: 0, Population: 1300, Flag: 1);
        Assert.NotEqual((short)0, rec.Flag);
        Assert.True(rec.Population > 1200); // would trigger message-id 6001
    }

    [Fact]
    public void Equality_IsValueBased()
    {
        var a = new LobbyServerRecord(1, 0, 500, 1);
        var b = new LobbyServerRecord(1, 0, 500, 1);
        Assert.Equal(a, b);
    }
}