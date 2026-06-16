using MartialHeroes.Shared.Diagnostics.Logging;
using Microsoft.Extensions.Logging;

namespace MartialHeroes.Shared.Diagnostics.Tests.Logging;

/// <summary>
/// Pins the <see cref="NetworkLog"/> contract: the documented 1000–1099 EventId range, the
/// log levels, and the rendered message templates.
///
/// The opcode is the <c>(major, minor)</c> pair from the frame header — there is no single
/// 16-bit opcode field — and is rendered as <c>major:minor</c> to match
/// <c>PacketOpcode.ToString()</c>.
/// spec: Docs/RE/specs/client_architecture.md §7 — "[u32 size @+0][u16 major @+4][u16 minor @+6]
/// ... The (major, minor) pair IS the opcode".
/// </summary>
public sealed class NetworkLogTests
{
    private static (ListLogger logger, LogRecord record) Capture(Action<ILogger> log)
    {
        var logger = new ListLogger();
        log(logger);
        Assert.Single(logger.Records);
        return (logger, logger.Records[0]);
    }

    [Fact]
    public void PacketReceived_EventId_Level_And_OpcodePairFormat()
    {
        // spec: NetworkLog.cs — EventId 1001, Trace, "opcode={Major}:{Minor}".
        var (_, record) = Capture(l => NetworkLog.PacketReceived(l, major: 3, minor: 14, length: 16));

        Assert.Equal(1001, record.EventId.Id);
        Assert.Equal(LogLevel.Trace, record.Level);
        Assert.Equal("Packet received: opcode=3:14 length=16", record.Message);
    }

    [Fact]
    public void DecryptFailed_EventId_Level_And_OpcodePairFormat()
    {
        // spec: NetworkLog.cs — EventId 1002, Warning, "opcode={Major}:{Minor}".
        var (_, record) = Capture(l => NetworkLog.DecryptFailed(l, major: 1, minor: 4, reason: "short buffer"));

        Assert.Equal(1002, record.EventId.Id);
        Assert.Equal(LogLevel.Warning, record.Level);
        Assert.Equal("Decrypt failed for opcode=1:4: short buffer", record.Message);
    }

    [Fact]
    public void ConnectionStateChanged_EventId_Level_And_Message()
    {
        // spec: NetworkLog.cs — EventId 1003, Information.
        var (_, record) = Capture(l => NetworkLog.ConnectionStateChanged(l, "Connecting", "Connected"));

        Assert.Equal(1003, record.EventId.Id);
        Assert.Equal(LogLevel.Information, record.Level);
        Assert.Equal("Connection state changed: Connecting -> Connected", record.Message);
    }

    [Fact]
    public void UnhandledOpcode_EventId_Level_And_OpcodePairFormat()
    {
        // spec: NetworkLog.cs — EventId 1004, Debug, "opcode={Major}:{Minor}".
        var (_, record) = Capture(l => NetworkLog.UnhandledOpcode(l, major: 5, minor: 153, length: 40));

        Assert.Equal(1004, record.EventId.Id);
        Assert.Equal(LogLevel.Debug, record.Level);
        Assert.Equal("Unhandled opcode=5:153 (length=40) — packet dropped", record.Message);
    }

    [Fact]
    public void SendFailed_EventId_Level_And_OpcodePairFormat()
    {
        // spec: NetworkLog.cs — EventId 1005, Error, "opcode={Major}:{Minor}".
        var (_, record) = Capture(l => NetworkLog.SendFailed(l, major: 2, minor: 112, reason: "socket closed"));

        Assert.Equal(1005, record.EventId.Id);
        Assert.Equal(LogLevel.Error, record.Level);
        Assert.Equal("Send failed for opcode=2:112: socket closed", record.Message);
    }

    [Theory]
    [InlineData(1001)]
    [InlineData(1002)]
    [InlineData(1003)]
    [InlineData(1004)]
    [InlineData(1005)]
    public void EventIds_FallInReservedNetworkRange(int eventId)
    {
        // spec: NetworkLog.cs — "EventId range 1000–1099 reserved for network concerns."
        Assert.InRange(eventId, 1000, 1099);
    }
}