namespace MartialHeroes.Client.Application.Net;

/// <summary>
///     The protocol's single, global, one-deep "a char-management request is outstanding" flag — the
///     ONLY pending-request primitive in the wire protocol. There is no per-request id, sequence number,
///     or transaction key anywhere; a reply is never matched to its request by a correlation token (that
///     would be inventing structure the binary does not have). This is therefore a single boolean, NOT a
///     per-request id→request map.
/// </summary>
/// <remarks>
///     <para>
///         <b>Set / clear roster (control-flow-confirmed).</b> ARMED by the outbound char-management send
///         builders <c>1/6, 1/7, 1/9, 1/13, 1/14, 2/2</c>; CLEARED by the result handlers
///         <c>3/1, 3/4, 3/6, 3/7, 3/13, 3/14</c> AND by <c>4/1</c> (which clears it as its very first action,
///         before its form branch). <c>1/0 CmsgLogout</c> is fire-and-forget and arms NO latch.
///     </para>
///     <para>
///         <b>Sole consumer.</b> The latch's only reader is the keepalive timer
///         (<see cref="KeepaliveDriver" />), which suppresses the idle <c>2/10000</c> heartbeat while a
///         char-management request is outstanding (so the keepalive does not fire on top of an in-flight
///         request). It never selects a handler and never matches a response.
///     </para>
///     <para>
///         In-session play (<c>2/x ↔ 5/x</c> broadcasts) is NOT modelled with this latch — those carry no
///         correlation token and are pure broadcast-and-resolve.
///     </para>
///     spec: Docs/RE/specs/net_contracts.md §1.3 (the single in-flight latch); Docs/RE/specs/world_entry.md §3.3.
/// </remarks>
public sealed class InFlightLatch
{
    /// <summary>True while a char-management request is outstanding (the heartbeat is suppressed).</summary>
    public bool IsArmed { get; private set; }

    /// <summary>Arms the latch on a char-management send (1/6, 1/7, 1/9, 1/13, 1/14, 2/2). spec: net_contracts.md §1.3.</summary>
    public void Arm()
    {
        IsArmed = true;
    }

    /// <summary>Clears the latch on a result handler (3/1, 3/4, 3/6, 3/7, 3/13, 3/14) or 4/1. spec: net_contracts.md §1.3.</summary>
    public void Clear()
    {
        IsArmed = false;
    }
}