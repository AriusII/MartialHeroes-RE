using Microsoft.Extensions.Logging;

namespace MartialHeroes.Shared.Diagnostics.Logging;

public static partial class NetworkLog
{


    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Trace,
        Message = "Packet received: opcode={Major}:{Minor} length={Length}")]
    public static partial void PacketReceived(ILogger logger, ushort major, ushort minor, int length);

    [LoggerMessage(
        EventId = 1002,
        Level = LogLevel.Warning,
        Message = "Decrypt failed for opcode={Major}:{Minor}: {Reason}")]
    public static partial void DecryptFailed(ILogger logger, ushort major, ushort minor, string reason);

    [LoggerMessage(
        EventId = 1003,
        Level = LogLevel.Information,
        Message = "Connection state changed: {PreviousState} -> {NewState}")]
    public static partial void ConnectionStateChanged(ILogger logger, string previousState, string newState);

    [LoggerMessage(
        EventId = 1004,
        Level = LogLevel.Debug,
        Message = "Unhandled opcode={Major}:{Minor} (length={Length}) — packet dropped")]
    public static partial void UnhandledOpcode(ILogger logger, ushort major, ushort minor, int length);

    [LoggerMessage(
        EventId = 1005,
        Level = LogLevel.Error,
        Message = "Send failed for opcode={Major}:{Minor}: {Reason}")]
    public static partial void SendFailed(ILogger logger, ushort major, ushort minor, string reason);
}