namespace MartialHeroes.Client.Domain.Actors.Actors;

/// <summary>
///     The pure motion-intent an actor adopts in reaction to a 5/13 movement update — the deterministic,
///     engine-free classification the presentation drives an animation clip from. This is NOT the wire
///     MotionCode; it is the recovered set of intents the movement consumer resolves to.
/// </summary>
/// <remarks>
///     spec: Docs/RE/packets/5-13_actor_movement_update.yaml (MotionCode @body+0x24, RunFlag @+0x1C);
///     Docs/RE/specs/skinning.md §10.
/// </remarks>
public enum MotionIntent
{
    /// <summary>No movement / settled. The default for an idle or unknown code.</summary>
    Idle = 0,

    /// <summary>Walking toward the destination (RunFlag == 0). spec: 5-13 (RunFlag @+0x1C).</summary>
    Walk = 1,

    /// <summary>Running toward the destination (RunFlag != 0). spec: 5-13 (RunFlag @+0x1C).</summary>
    Run = 2,

    /// <summary>Instant snap / teleport (MotionCode == 5). spec: 5-13 (MotionCode == 5 = instant snap).</summary>
    Snap = 3
}

/// <summary>
///     Pure mapper from the 5/13 wire movement fields to a <see cref="MotionIntent" />. Deterministic, no
///     ambient state.
/// </summary>
public static class MotionIntentMap
{
    /// <summary>
    ///     The known instant-snap MotionCode. spec: Docs/RE/packets/5-13_actor_movement_update.yaml
    ///     (MotionCode @+0x24 tested ONLY as == 5 = instant snap).
    /// </summary>
    public const byte SnapMotionCode = 5;

    /// <summary>
    ///     Resolves the motion intent from the wire MotionCode (@body+0x24) and RunFlag (@+0x1C).
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         STATIC-KNOWN rules (the only ones the binary settles at this layer):
    ///         <list type="bullet">
    ///             <item><c>MotionCode == 5</c> ⇒ <see cref="MotionIntent.Snap" /> (the instant-snap branch).</item>
    ///             <item>
    ///                 otherwise <c>RunFlag != 0</c> ⇒ <see cref="MotionIntent.Run" />, else
    ///                 <see cref="MotionIntent.Walk" />.
    ///             </item>
    ///         </list>
    ///     </para>
    ///     <para>
    ///         The FULL MotionCode value→clip map is live-pending: MotionCode is statically used ONLY as
    ///         <c>== 5</c>; any other per-value animation semantic is unconfirmed and defaults to Walk/Idle
    ///         below. live-pending (5-13 yaml / skinning.md §10).
    ///     </para>
    ///     spec: Docs/RE/packets/5-13_actor_movement_update.yaml; Docs/RE/specs/skinning.md §10.
    /// </remarks>
    public static MotionIntent Resolve(byte motionCode, byte runFlag)
    {
        return motionCode switch
        {
            SnapMotionCode => MotionIntent.Snap, // MotionCode == 5 = instant snap. spec: 5-13 (MotionCode == 5).

            // live-pending (5-13 yaml / skinning.md §10): no other MotionCode value has a confirmed clip;
            // fall through to the run/walk choice keyed on RunFlag (the only other static-known field).
            _ => runFlag != 0 ? MotionIntent.Run : MotionIntent.Walk
        };
    }
}