using MartialHeroes.Network.Abstractions.Protocol;
using MartialHeroes.Network.Abstractions.Session;
using MartialHeroes.Network.Protocol.Packets;
using MartialHeroes.Network.Protocol.Packets.Social.Packets;

namespace MartialHeroes.Client.Application.Net;

/// <summary>
///     The single in-session keepalive emitter. The wire protocol has THREE independent link-keepalive
///     mechanisms; this driver owns the two the client actively emits on a timer/toggle:
///     <list type="number">
///         <item>
///             <b>(a) the periodic idle heartbeat <c>2/10000</c></b> — a 4-byte zero body sent after a window
///             of OUTBOUND silence (any outbound send resets the idle clock). It is SUPPRESSED while the
///             <see cref="InFlightLatch" /> is armed (a char-management request is outstanding). spec:
///             Docs/RE/specs/world_entry.md §3.2 (a); Docs/RE/specs/net_contracts.md §1.3 (sole consumer of the latch).
///         </item>
///         <item>
///             <b>(b) the <c>2/112</c> software toggle</b> — body <c>0x01</c> = ENABLE on world-enter, body
///             <c>0x00</c> = DISABLE on world-leave. spec: Docs/RE/specs/world_entry.md §2.5 / §3.2 (b);
///             Docs/RE/specs/world_exit.md §1.2 (DISABLE ordered before 2/0).
///         </item>
///     </list>
///     note: mechanism (c) — the <c>1/2</c> proxy-filler / lobby-ping keepalive — is NOT owned here; it is
///     the optional keepalive-2 mechanism (a lobby-side ping with its own worker). Do not over-engineer it.
/// </summary>
/// <remarks>
///     <para>
///         <b>Engine-free, deterministic.</b> No ambient clock: the host pumps <see cref="Tick(long)" /> with a
///         monotonic millisecond stamp, and the idle cadence is compared against that stamp. The same driver
///         runs headless and on a future server.
///     </para>
///     <para>
///         <b>Cadence value is structure-confirmed, the real-wire number is live-pending.</b> The 20000 ms
///         idle cadence is the documented structure (a ~20 s idle heartbeat); the exact on-wire cadence VALUE
///         is capture/debugger-pending. spec: Docs/RE/specs/world_entry.md §4 (real-wire keepalive cadence
///         live-pending); Docs/RE/specs/client_workflow.md §6.4.1.
///     </para>
/// </remarks>
public sealed class KeepaliveDriver
{
    /// <summary>
    ///     The idle-heartbeat cadence in milliseconds. STRUCTURE-confirmed (~20 s of outbound silence);
    ///     the real-wire VALUE is live-pending (a capture/debugger pass pins it). spec:
    ///     Docs/RE/specs/world_entry.md §3.2 (a) / §4; Docs/RE/specs/client_workflow.md §6.4.1.
    /// </summary>
    public const long IdleHeartbeatIntervalMs = 20_000;

    private const ushort KeepaliveMajor = 2; // 2/10000 and 2/112 share major 2. spec: net_contracts.md §2.15.
    private const ushort IdleHeartbeatMinor = 10000; // 2/10000 timer keepalive. spec: net_contracts.md §2.15.
    private const ushort ToggleMinor = 112; // 2/112 software toggle. spec: net_contracts.md §2.15.

    private const byte ToggleEnable = 0x01; // 2/112 body 0x01 = ENABLE on world-enter. spec: world_entry.md §2.5.
    private const byte ToggleDisable = 0x00; // 2/112 body 0x00 = DISABLE on world-leave. spec: world_exit.md §1.2.
    private readonly InFlightLatch? _latch;

    private readonly IOutboundPacketSink _outbound;
    private readonly SessionId _sessionId;

    private long _lastOutboundMs;

    /// <summary>
    ///     Creates the keepalive driver bound to a session and outbound sink.
    /// </summary>
    /// <param name="outbound">The outbound serialisation seam the use-cases also use.</param>
    /// <param name="sessionId">The persistent opcode connection's session id.</param>
    /// <param name="latch">
    ///     The single in-flight latch. When armed, the idle heartbeat is suppressed. Optional: when
    ///     absent the heartbeat is never suppressed (the latch is a separate, optional concern).
    ///     spec: Docs/RE/specs/net_contracts.md §1.3.
    /// </param>
    public KeepaliveDriver(IOutboundPacketSink outbound, SessionId sessionId, InFlightLatch? latch = null)
    {
        _outbound = outbound ?? throw new ArgumentNullException(nameof(outbound));
        _sessionId = sessionId;
        _latch = latch;
    }

    /// <summary>True while the driver considers the client in-world (between enter and exit).</summary>
    public bool IsInWorld { get; private set; }

    /// <summary>
    ///     World-entered hook: the scene state machine sends <c>2/112</c> ENABLE on case-5 entry just
    ///     before the in-world loop, and the idle heartbeat begins ticking. The composition root calls
    ///     this on <see cref="Events.InGameWorldBootstrappedEvent" />. spec: Docs/RE/specs/world_entry.md §2.5.
    /// </summary>
    public ValueTask OnWorldEnteredAsync(long nowMs, CancellationToken cancellationToken = default)
    {
        IsInWorld = true;
        _lastOutboundMs = nowMs;
        return SendToggleAsync(ToggleEnable, cancellationToken);
    }

    /// <summary>
    ///     World-exited hook: the leave-world path disarms the <c>2/112</c> toggle (body 0x00) FIRST, then
    ///     the caller sends the exit opcode (1/0 or 2/0). After this the idle heartbeat stops. spec:
    ///     Docs/RE/specs/world_exit.md §1.2 (DISABLE ordered before 2/0).
    /// </summary>
    public ValueTask OnWorldExitedAsync(CancellationToken cancellationToken = default)
    {
        IsInWorld = false;
        return SendToggleAsync(ToggleDisable, cancellationToken);
    }

    /// <summary>
    ///     Records that an outbound frame was just sent, resetting the idle clock (any outbound send
    ///     defers the next idle heartbeat). The composition root calls this on every outbound send.
    ///     spec: Docs/RE/specs/world_entry.md §3.2 (a) (heartbeat fires after outbound silence).
    /// </summary>
    public void NoteOutbound(long nowMs)
    {
        _lastOutboundMs = nowMs;
    }

    /// <summary>
    ///     Host tick: fires the idle heartbeat <c>2/10000</c> when in-world, the latch is NOT armed, and
    ///     the idle interval has elapsed since the last outbound send. A no-op otherwise. Returns the
    ///     pending send (or a completed task when nothing fired). spec: Docs/RE/specs/world_entry.md §3.2.
    /// </summary>
    public ValueTask Tick(long nowMs, CancellationToken cancellationToken = default)
    {
        if (!IsInWorld) return ValueTask.CompletedTask; // not in-world: no idle heartbeat.

        // Suppress the idle heartbeat while a char-management request is outstanding (the latch's sole
        // consumer). spec: Docs/RE/specs/net_contracts.md §1.3; world_entry.md §3.2 (a).
        if (_latch is { IsArmed: true }) return ValueTask.CompletedTask;

        if (nowMs - _lastOutboundMs < IdleHeartbeatIntervalMs)
            return ValueTask.CompletedTask; // not idle long enough yet.

        _lastOutboundMs = nowMs; // the heartbeat itself is an outbound send; reset the idle clock.

        // 2/10000 carries a 4-byte zero body. spec: Docs/RE/packets/2-10000_keepalive.yaml (CmsgKeepalive, 4B zero).
        var body = new byte[CmsgKeepalive.Size]; // zero-filled.
        return _outbound.SendAsync(_sessionId, KeepaliveMajor, IdleHeartbeatMinor, body, cancellationToken);
    }

    private ValueTask SendToggleAsync(byte flag, CancellationToken cancellationToken)
    {
        // 2/112 is a fixed 1-byte body: the toggle flag. spec: Docs/RE/specs/opcodes.md (2/112);
        // CmsgKeepaliveToggle (WireSize = 1).
        var body = new byte[CmsgKeepaliveToggle.WireSize];
        body[0] = flag;
        return _outbound.SendAsync(_sessionId, KeepaliveMajor, ToggleMinor, body, cancellationToken);
    }
}