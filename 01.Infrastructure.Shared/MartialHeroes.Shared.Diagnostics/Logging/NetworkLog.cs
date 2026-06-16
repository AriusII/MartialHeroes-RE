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

    // The wire opcode is the (major, minor) pair from the frame header — there is no single
    // 16-bit opcode field. spec: Docs/RE/specs/client_architecture.md §7 — frame header
    // "[u32 size @+0][u16 major @+4][u16 minor @+6] ... The (major, minor) pair IS the opcode".
    // Formatted as "major:minor" to match PacketOpcode.ToString().

    /// <summary>
    /// Logged when a complete packet is received from the server.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="major">The opcode major word from the wire frame header.</param>
    /// <param name="minor">The opcode minor word from the wire frame header.</param>
    /// <param name="length">The total byte length of the received packet including header.</param>
    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Trace,
        Message = "Packet received: opcode={Major}:{Minor} length={Length}")]
    public static partial void PacketReceived(ILogger logger, ushort major, ushort minor, int length);

    /// <summary>
    /// Logged when in-place decryption of a packet buffer fails.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="major">The opcode major word of the packet that failed decryption, if known.</param>
    /// <param name="minor">The opcode minor word of the packet that failed decryption, if known.</param>
    /// <param name="reason">A short description of the failure.</param>
    [LoggerMessage(
        EventId = 1002,
        Level = LogLevel.Warning,
        Message = "Decrypt failed for opcode={Major}:{Minor}: {Reason}")]
    public static partial void DecryptFailed(ILogger logger, ushort major, ushort minor, string reason);

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
    /// <param name="major">The unknown opcode major word.</param>
    /// <param name="minor">The unknown opcode minor word.</param>
    /// <param name="length">The byte length of the unhandled packet.</param>
    [LoggerMessage(
        EventId = 1004,
        Level = LogLevel.Debug,
        Message = "Unhandled opcode={Major}:{Minor} (length={Length}) — packet dropped")]
    public static partial void UnhandledOpcode(ILogger logger, ushort major, ushort minor, int length);

    /// <summary>
    /// Logged when a send operation to the server fails.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="major">The opcode major word of the outbound packet.</param>
    /// <param name="minor">The opcode minor word of the outbound packet.</param>
    /// <param name="reason">A short description of the failure.</param>
    [LoggerMessage(
        EventId = 1005,
        Level = LogLevel.Error,
        Message = "Send failed for opcode={Major}:{Minor}: {Reason}")]
    public static partial void SendFailed(ILogger logger, ushort major, ushort minor, string reason);
}