using Microsoft.Extensions.Logging;

namespace MartialHeroes.Shared.Diagnostics.Logging;

/// <summary>
/// Source-generated, allocation-free log methods for the network layer.
/// </summary>
/// <remarks>
/// All methods are generated at compile time by the <c>Microsoft.Extensions.Logging.Abstractions</c>
/// <c>[LoggerMessage]</c> source generator. Never add interpolated-string log calls here — use
/// structured placeholders so that hot-path logging incurs zero heap allocations.
/// </remarks>
public static partial class NetworkLog
{
    // EventId range 1000–1099 reserved for network concerns.

    /// <summary>
    /// Logged when a complete packet is received from the server.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="opcode">The raw packet opcode (16-bit value from the wire header).</param>
    /// <param name="length">The total byte length of the received packet including header.</param>
    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Trace,
        Message = "Packet received: opcode=0x{Opcode:X4} length={Length}")]
    public static partial void PacketReceived(ILogger logger, ushort opcode, int length);

    /// <summary>
    /// Logged when in-place decryption of a packet buffer fails.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="opcode">The opcode of the packet that failed decryption, if known.</param>
    /// <param name="reason">A short description of the failure.</param>
    [LoggerMessage(
        EventId = 1002,
        Level = LogLevel.Warning,
        Message = "Decrypt failed for opcode=0x{Opcode:X4}: {Reason}")]
    public static partial void DecryptFailed(ILogger logger, ushort opcode, string reason);

    /// <summary>
    /// Logged when the connection state changes (e.g. Connected, Disconnected, Reconnecting).
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="previousState">The state before the transition.</param>
    /// <param name="newState">The state after the transition.</param>
    [LoggerMessage(
        EventId = 1003,
        Level = LogLevel.Information,
        Message = "Connection state changed: {PreviousState} -> {NewState}")]
    public static partial void ConnectionStateChanged(ILogger logger, string previousState, string newState);

    /// <summary>
    /// Logged when a packet with an unrecognised opcode is received and dropped.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="opcode">The unknown opcode value.</param>
    /// <param name="length">The byte length of the unhandled packet.</param>
    [LoggerMessage(
        EventId = 1004,
        Level = LogLevel.Debug,
        Message = "Unhandled opcode=0x{Opcode:X4} (length={Length}) — packet dropped")]
    public static partial void UnhandledOpcode(ILogger logger, ushort opcode, int length);

    /// <summary>
    /// Logged when a send operation to the server fails.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="opcode">The opcode of the outbound packet.</param>
    /// <param name="reason">A short description of the failure.</param>
    [LoggerMessage(
        EventId = 1005,
        Level = LogLevel.Error,
        Message = "Send failed for opcode=0x{Opcode:X4}: {Reason}")]
    public static partial void SendFailed(ILogger logger, ushort opcode, string reason);
}
