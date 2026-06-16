// spec: Docs/RE/opcodes.md — the inbound dispatch seam (major:minor -> handler).

namespace MartialHeroes.Network.Protocol.Routing;

/// <summary>
/// Sink for typed, zero-copy packet views produced by <see cref="PacketRouter"/>. A higher layer
/// (Client.Application) implements this to react to decoded packets.
/// </summary>
/// <remarks>
/// Handlers receive frames as <c>ref readonly</c> struct views reinterpreted in place over the
/// frame buffer — no copies, no boxing, no allocation. Each method corresponds to one fully-specced
/// fixed-size packet. Unspecced opcodes are surfaced raw via <see cref="OnUnhandled"/>.
/// <para>
/// NOTE: <c>MartialHeroes.Network.Abstractions</c> already defines its own
/// <c>IPacketHandler</c> / <c>IFrameSink</c> / <c>IOutboundPacketSink</c> seam (plus the
/// Session / Transport / Lobby contracts). The open work is the actual wiring decision — whether
/// this Protocol-local interface unifies onto that one — which entails the legal downward reference
/// Protocol -&gt; Abstractions and a layering call on where the typed packet structs live. Deferred
/// to the architecture phase; this seam stays Protocol-local until then.
/// </para>
/// </remarks>
public interface IPacketHandler
{
    /// <summary>5/0 — actor despawn. spec: packets/5-0_char_despawn.yaml.</summary>
    void Handle(in Packets.SmsgCharDespawn packet);

    /// <summary>3/5 — enter-world ack. spec: packets/3-5_enter_game_response.yaml.</summary>
    void Handle(in Packets.SmsgEnterGameAck packet);

    /// <summary>5/13 — actor movement update. spec: packets/5-13_actor_movement_update.yaml.</summary>
    void Handle(in Packets.SmsgActorMovementUpdate packet);

    /// <summary>5/3 — actor spawn. spec: packets/5-3_char_spawn.yaml.</summary>
    void Handle(in Packets.SmsgCharSpawn packet);

    /// <summary>
    /// Any opcode without a specced fixed-size struct. The router passes the raw payload span
    /// (zero-copy) and the packed opcode so the application can route it itself or drop it.
    /// </summary>
    void OnUnhandled(uint packedOpcode, ReadOnlySpan<byte> payload);
}