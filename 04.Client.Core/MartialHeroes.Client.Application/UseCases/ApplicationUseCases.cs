using System.Buffers.Binary;
using System.Text;
using MartialHeroes.Client.Application.StateMachine;
using MartialHeroes.Client.Application.World;
using MartialHeroes.Client.Domain.Actors;
using MartialHeroes.Network.Abstractions.Protocol;
using MartialHeroes.Network.Abstractions.Session;
using MartialHeroes.Network.Protocol.Packets;
using MartialHeroes.Shared.Kernel.Numerics;

namespace MartialHeroes.Client.Application.UseCases;

/// <summary>
/// Default <see cref="IApplicationUseCases"/>. Translates presentation intents into FSM transitions
/// and outbound requests via the injected <see cref="IOutboundPacketSink"/>. Each use-case serialises
/// the matching Network.Protocol packet struct to a PLAINTEXT payload (the wire fields, no header) and
/// calls <see cref="IOutboundPacketSink.SendAsync"/> with the opcode; the sink prepends the 8-byte
/// frame header and applies the byte cipher + LZ4 downstream. No transport/framing/crypto here.
/// spec: Docs/RE/opcodes.md (outbound frame header); Docs/RE/specs/crypto.md §6.1 (login staging).
/// </summary>
/// <remarks>
/// <para>
/// <b>Field writes vs. struct construction.</b> The request structs are immutable (no public ctor),
/// so each field is written to the payload at the struct's documented Pack=1 offset using
/// <see cref="BinaryPrimitives"/> (little-endian, matching the struct layout). The byte widths equal
/// the structs' <c>WireSize</c> / <c>HeaderSize</c> constants. spec: each packet yaml.
/// </para>
/// <para>
/// <b>PROVISIONAL defaults.</b> The 1/9 VersionToken is client/build-specific and not recovered
/// (spec: 1-9_enter_game_request.yaml UNKNOWN); an injected token (default zero-filled) is used. The
/// pre-0/0 username send (1/6) is not specced; <see cref="LoginAsync"/> stages the credential but
/// emits no 1/6 frame. The chat/credential text charset is UNKNOWN per the chat specs; UTF-8 is the
/// documented provisional choice.
/// </para>
/// </remarks>
public sealed class ApplicationUseCases : IApplicationUseCases
{
    private readonly IOutboundPacketSink _outbound;
    private readonly ClientStateMachine _stateMachine;
    private readonly ClientWorld _world;
    private readonly LoginCredentialStore _credentials;
    private readonly SessionId _sessionId;
    private readonly byte[] _versionToken;

    /// <summary>The 1/9 version-token length: a fixed 33-byte buffer. spec: 1-9_enter_game_request.yaml.</summary>
    public const int VersionTokenLength = 33;

    /// <summary>
    /// Creates the use-case facade.
    /// </summary>
    /// <param name="versionToken">
    /// PROVISIONAL. The 33-byte 1/9 client/version handshake token. Default (or a shorter buffer) is
    /// zero-padded to 33 bytes; the real derivation is unrecovered. spec: 1-9_enter_game_request.yaml.
    /// </param>
    public ApplicationUseCases(
        IOutboundPacketSink outbound,
        ClientStateMachine stateMachine,
        ClientWorld world,
        LoginCredentialStore credentials,
        SessionId sessionId,
        ReadOnlySpan<byte> versionToken = default)
    {
        _outbound = outbound ?? throw new ArgumentNullException(nameof(outbound));
        _stateMachine = stateMachine ?? throw new ArgumentNullException(nameof(stateMachine));
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));
        _sessionId = sessionId;

        // Zero-pad / truncate the provisional token into the fixed 33-byte buffer.
        _versionToken = new byte[VersionTokenLength];
        if (!versionToken.IsEmpty)
        {
            versionToken[..Math.Min(versionToken.Length, VersionTokenLength)].CopyTo(_versionToken);
        }
    }

    /// <inheritdoc />
    public ValueTask LoginAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(username);
        ArgumentNullException.ThrowIfNull(password);
        cancellationToken.ThrowIfCancellationRequested();

        // Stage the credential before any 0/0 arrives; the 1/4 reply is built later by the login
        // handler when the server's KeyExchange lands. spec: Docs/RE/specs/crypto.md §6.1.
        _credentials.Stage(username, password);

        // Drive the FSM from Login toward handshaking. CharacterSelection is the application-state
        // analogue of "secure session established / awaiting auth result".
        _stateMachine.OnAuthenticated();

        // PROVISIONAL: the pre-0/0 username send (1/6, 52-byte credential/description blob) is not
        // specced (spec: 1-9_enter_game_request.yaml "RELATED ... 1/6 ... not specced here"). We do not
        // fabricate a 1/6 frame; the handshake proceeds from the inbound 0/0. Documented gap.
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask RequestMoveAsync(
        Vector3Fixed target, bool running, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Heading = atan2 of (target - current) in the XZ plane, from the local player's current
        // position. spec: Docs/RE/packets/2-13_move_request.yaml (Heading 0x00). When the local actor
        // is unknown we fall back to a zero origin (heading still well-defined toward the target).
        Vector3Fixed current = _world.LocalActor?.Position ?? Vector3Fixed.Zero;
        var (curX, _, curZ) = current.ToVector3Float();
        var (tgtX, _, tgtZ) = target.ToVector3Float();
        float heading = MathF.Atan2(tgtZ - curZ, tgtX - curX); // angular unit unconfirmed; radians provisional

        // Build the fixed 16-byte 2/13 payload at the struct's Pack=1 offsets. spec: 2-13 fields.
        Span<byte> payload = stackalloc byte[CmsgMoveRequest.WireSize];
        payload.Clear();
        BinaryPrimitives.WriteSingleLittleEndian(payload.Slice(0x00, 4), heading); // 0x00 Heading
        BinaryPrimitives.WriteSingleLittleEndian(payload.Slice(0x04, 4), tgtX); // 0x04 TargetX
        BinaryPrimitives.WriteSingleLittleEndian(payload.Slice(0x08, 4), tgtZ); // 0x08 TargetZ
        // 0x0c ModeFlags: low byte is the click-to-move mode selector (1); a run bit packs into the
        // same region. The sub-byte split is UNKNOWN, so we set mode=1 plus a run bit at 0x100.
        // spec: 2-13_move_request.yaml (ModeFlags, byte split unconfirmed -> PROVISIONAL packing).
        const uint moveModeClickToMove = 1u;
        const uint runBit = 0x0000_0100u;
        uint modeFlags = moveModeClickToMove | (running ? runBit : 0u);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.Slice(0x0c, 4), modeFlags); // 0x0c ModeFlags

        return SendAsync(major: 2, minor: 13, payload, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask SelectCharacterAsync(int slotIndex, CancellationToken cancellationToken = default)
    {
        if (slotIndex is < 0 or > byte.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(slotIndex), slotIndex, "Slot index out of range.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        // Build the fixed 40-byte 1/9 payload. spec: 1-9_enter_game_request.yaml.
        Span<byte> payload = stackalloc byte[CmsgEnterGameRequest.WireSize];
        payload.Clear();
        payload[0x00] = (byte)slotIndex; // 0x00 SlotIndex (HIGH CONFIDENCE)
        _versionToken.CopyTo(payload.Slice(0x01, VersionTokenLength)); // 0x01 VersionToken (PROVISIONAL)
        // 0x22 Tail (6 bytes) stays zero. spec: 1-9 (observed zero).

        // Drive the deterministic lifecycle toward Loading; the 3/5 ack later transitions to World
        // (handled by GamePacketHandler). spec: Docs/RE/opcodes.md (3/5 SmsgEnterGameAck).
        _stateMachine.OnCharacterSelected();

        return SendAsync(major: 1, minor: 9, payload, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask UseSkillAsync(
        byte slot,
        ReadOnlyMemory<uint> targetsA = default,
        ReadOnlyMemory<uint> targetsB = default,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ReadOnlySpan<uint> a = targetsA.Span;
        ReadOnlySpan<uint> b = targetsB.Span;
        if (a.Length > ushort.MaxValue || b.Length > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(targetsA), "Too many skill targets for a u16 count.");
        }

        // Total payload = 24-byte header + (CountA + CountB) * 4. spec: 2-52_use_skill.yaml.
        int tailBytes = (a.Length + b.Length) * sizeof(uint);
        var payload = new byte[CmsgUseSkillHeader.HeaderSize + tailBytes];
        Span<byte> p = payload;
        p.Clear();

        // Fixed 24-byte header at Pack=1 offsets. spec: 2-52 fields.
        p[0x00] = slot; // 0x00 SkillSlot (0xFF = basic attack)
        // 0x01..0x03 Pad0 stays zero.
        // 0x04 AimMode / 0x08 AimScale / 0x0c AimX / 0x10 AimZ default to 0 (caller did not supply aim).
        // spec: 2-52 (aim fields; defaults documented).
        BinaryPrimitives.WriteUInt16LittleEndian(p.Slice(0x14, 2), (ushort)a.Length); // 0x14 CountA
        BinaryPrimitives.WriteUInt16LittleEndian(p.Slice(0x16, 2), (ushort)b.Length); // 0x16 CountB

        // Variable tail: array A (u32 ids) then array B (u32 ids). spec: 2-52 TRAILER.
        int cursor = CmsgUseSkillHeader.HeaderSize;
        foreach (uint id in a)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(p.Slice(cursor, 4), id);
            cursor += sizeof(uint);
        }

        foreach (uint id in b)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(p.Slice(cursor, 4), id);
            cursor += sizeof(uint);
        }

        return SendAsync(major: 2, minor: 52, payload, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask SendChatAsync(
        uint channel,
        string text,
        string? recipientName = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        cancellationToken.ThrowIfCancellationRequested();

        bool isWhisper = !string.IsNullOrEmpty(recipientName);
        return isWhisper
            ? SendWhisperAsync(channel, recipientName!, text, cancellationToken)
            : SendChannelChatAsync(channel, text, cancellationToken);
    }

    // -------------------------------------------------------------------------
    // Chat helpers
    // -------------------------------------------------------------------------

    private ValueTask SendWhisperAsync(uint channel, string recipientName, string text, CancellationToken ct)
    {
        // 2/7: 19-byte header + [u32 textLength][text bytes]; textLength includes the terminating NUL.
        // spec: Docs/RE/packets/2-7_whisper.yaml.
        byte[] body = BuildLengthPrefixedText(text);
        var payload = new byte[CmsgWhisperHeader.HeaderSize + body.Length];
        Span<byte> p = payload;
        p.Clear();

        p[0x00] = (byte)channel; // 0x00 ChannelType (channel selector, low byte)
        // 0x01 Flag stays zero (meaning UNKNOWN; spec).
        // 0x02 TargetName: NUL-padded into a fixed 16-byte buffer. spec: 2-7 (HIGH CONFIDENCE).
        WriteFixedAscii(recipientName, p.Slice(0x02, 16));
        // 0x12 HeaderTail stays zero.

        body.CopyTo(p.Slice(CmsgWhisperHeader.HeaderSize));
        return SendAsync(major: 2, minor: 7, payload, ct);
    }

    private ValueTask SendChannelChatAsync(uint channel, string text, CancellationToken ct)
    {
        // 3/21: 56-byte context header + [u32 textLength][text bytes]; textLength = strlen + 1.
        // spec: Docs/RE/packets/3-21_chat_channel.yaml.
        byte[] body = BuildLengthPrefixedText(text);
        var payload = new byte[CmsgChatChannelHeader.HeaderSize + body.Length];
        Span<byte> p = payload;
        p.Clear();

        // 0x00..0x03 HeaderPrefix stays zero (not decoded; spec).
        BinaryPrimitives.WriteUInt32LittleEndian(p.Slice(0x04, 4), channel); // 0x04 ChannelSelector
        // 0x08..0x37 HeaderRest stays zero (not decoded; spec).

        body.CopyTo(p.Slice(CmsgChatChannelHeader.HeaderSize));
        return SendAsync(major: 3, minor: 21, payload, ct);
    }

    /// <summary>
    /// Builds a chat text body: <c>[u32 LE textLength][UTF-8 text][0x00]</c> where textLength includes
    /// the terminating NUL (spec: 2-7 / 3-21 length-prefix includes NUL). UTF-8 is PROVISIONAL (wire
    /// charset UNKNOWN per the specs).
    /// </summary>
    private static byte[] BuildLengthPrefixedText(string text)
    {
        int textByteCount = Encoding.UTF8.GetByteCount(text);
        uint lengthWithNul = checked((uint)(textByteCount + 1)); // +1 for the terminating NUL
        var body = new byte[sizeof(uint) + textByteCount + 1];
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(0, 4), lengthWithNul);
        Encoding.UTF8.GetBytes(text, body.AsSpan(sizeof(uint), textByteCount));
        // trailing NUL already present (zeroed array).
        return body;
    }

    /// <summary>Writes <paramref name="value"/> as ASCII into a NUL-padded fixed buffer (truncating if too long).</summary>
    private static void WriteFixedAscii(string value, Span<byte> destination)
    {
        destination.Clear();
        int written = Encoding.ASCII.GetBytes(
            value.AsSpan(0, Math.Min(value.Length, destination.Length - 1)), destination);
        _ = written;
    }

    // -------------------------------------------------------------------------
    // Send
    // -------------------------------------------------------------------------

    private ValueTask SendAsync(ushort major, ushort minor, ReadOnlySpan<byte> payload, CancellationToken ct)
    {
        // The sink takes a ReadOnlyMemory that must outlive the send; copy the (often stack-allocated)
        // payload into an owning array. Per-send allocation is acceptable for these low-rate intents.
        var owned = payload.ToArray();
        return _outbound.SendAsync(_sessionId, major, minor, owned, ct);
    }

    private ValueTask SendAsync(ushort major, ushort minor, ReadOnlyMemory<byte> payload, CancellationToken ct) =>
        _outbound.SendAsync(_sessionId, major, minor, payload, ct);
}