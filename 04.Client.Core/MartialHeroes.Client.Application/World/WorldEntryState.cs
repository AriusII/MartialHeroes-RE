using MartialHeroes.Shared.Kernel.Numerics;

namespace MartialHeroes.Client.Application.World;

/// <summary>
/// The application analogue of the binary's world-entry globals (<c>g_EnvCurrentAreaId</c> /
/// <c>g_LocalPlayerAreaId</c>): the durable record of the most recent <c>4/1</c> world entry that
/// materialized a local player. spec: Docs/RE/specs/world_entry.md §2.3 / §3.1; Docs/RE/specs/handlers.md §4/1.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists.</b> The transient <see cref="Events.InGameWorldBootstrappedEvent"/> is published on
/// a SingleReader channel and drained by whichever scene controller is active when <c>4/1</c> arrives
/// (LoginScene / LoadScene) — which can be <em>before</em> the InGame scene and its game loop exist. In that
/// order the area-load signal is lost and the world never renders. This holder is the durable seam that
/// survives the channel handoff: the network reader records the world entry here, and the InGame
/// <c>_Ready</c> can recover the area cold-start from it regardless of who drained the event. The event and
/// this state coexist — the event covers the rarer order where InGame is already up; this state covers the
/// live order. spec: Docs/RE/specs/world_entry.md §2.3 / §3.1.
/// </para>
/// <para>
/// <b>Threading.</b> Like <see cref="ClientWorld"/> and <see cref="LocalPlayerState"/>, this is mutated only
/// by the single network-reader logical owner; it is deliberately lock-free.
/// </para>
/// </remarks>
public sealed class WorldEntryState
{
    /// <summary>
    /// True once a <c>4/1</c> world entry with a materialized local player has been recorded; the InGame
    /// scene reads this on <c>_Ready</c> to drive the area cold-start. spec: Docs/RE/specs/world_entry.md §3.1.
    /// </summary>
    public bool IsActive { get; private set; }

    /// <summary>
    /// The absolute area index from the <c>4/1</c> body (the 3-digit decimal directory selects the on-disk
    /// area). spec: Docs/RE/specs/world_entry.md §3.1; Docs/RE/packets/4-1_game_state_tick.yaml.
    /// </summary>
    public int AreaId { get; private set; }

    /// <summary>
    /// The local player's world-entry spawn position (Q16.16, world Y forced to 0 at the handler boundary).
    /// spec: Docs/RE/specs/world_entry.md §2.3.
    /// </summary>
    public Vector3Fixed SpawnPosition { get; private set; } = Vector3Fixed.Zero;

    /// <summary>
    /// Records the most recent <c>4/1</c> world entry (latest wins / idempotent-overwrite): marks the state
    /// active and stores the area id + spawn position. spec: Docs/RE/specs/world_entry.md §2.3 / §3.1;
    /// Docs/RE/specs/handlers.md §4/1.
    /// </summary>
    public void Record(int areaId, Vector3Fixed spawnPosition)
    {
        IsActive = true;
        AreaId = areaId;
        SpawnPosition = spawnPosition;
    }

    /// <summary>
    /// Resets to inactive on world-leave / disconnect, so a later InGame <c>_Ready</c> does not recover a
    /// stale entry. spec: Docs/RE/specs/world_entry.md (world-leave); Docs/RE/specs/world_exit.md.
    /// </summary>
    public void Clear()
    {
        IsActive = false;
        AreaId = 0;
        SpawnPosition = Vector3Fixed.Zero;
    }
}