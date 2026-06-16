using MartialHeroes.Network.Abstractions.Session;

namespace MartialHeroes.Network.Abstractions.Tests;

public sealed class SessionIdTests
{
    [Fact]
    public void None_HasValueZero()
    {
        Assert.Equal(0UL, SessionId.None.Value);
    }

    [Fact]
    public void None_EqualsDefaultConstructed()
    {
        // Sentinel must equal the zero-value struct.
        Assert.Equal(new SessionId(0UL), SessionId.None);
    }

    [Fact]
    public void ToString_IncludesValue()
    {
        var id = new SessionId(42UL);
        Assert.Equal("Session(42)", id.ToString());
    }

    [Fact]
    public void None_ToString_IsSession0()
    {
        Assert.Equal("Session(0)", SessionId.None.ToString());
    }

    [Fact]
    public void Equality_ByValue()
    {
        var a = new SessionId(7UL);
        var b = new SessionId(7UL);
        Assert.Equal(a, b);
        Assert.NotEqual(a, SessionId.None);
    }
}
