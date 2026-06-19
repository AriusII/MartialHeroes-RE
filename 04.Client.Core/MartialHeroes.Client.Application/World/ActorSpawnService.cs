using MartialHeroes.Client.Application.Events;
using MartialHeroes.Client.Domain.Actors;

namespace MartialHeroes.Client.Application.World;

// =============================================================================
// ActorSpawnService — the ActorComposer → (publish) → ClientWorld seam.
//
// CYCLE 1 Phase 5 Stage C, Deliverable 2. Turns a spawn (a wire 880-byte
// SpawnDescriptor span OR a neutral .arr-derived ActorSpawn) into a baked
// AssembledActor via the ActorComposer, publishes an ActorAssembledEvent on the
// bus (for layer-05 next-frame mesh build), and — where a live Domain Actor
// identity exists — registers it in ClientWorld via the existing registry.
//
// Placement (WorldX / WorldZ / Yaw) flows from the composer's AssembledActor,
// which sources WorldX/WorldZ from SpawnDescriptorReader (+0x4C / +0x50) or the
// .arr. There is NO baked Y here: "the ground Y is re-sampled from the terrain
// each frame" — A1-6 (the fallback-Y race) is Phase 6's symptom-fix, NOT solved
// here. spec: Docs/RE/specs/assembly_graph.md §1; §5 A1-6.
//
// Engine-free, deterministic, single-owner-thread (mirrors ClientWorld /
// SectorStreamingService: mutated only by the single network-reader logical
// owner; deliberately lock-free — do NOT call concurrently).
// =============================================================================

/// <summary>
/// Orchestrates one actor spawn: bakes it through the <see cref="ActorComposer"/>, publishes the
/// neutral <see cref="ActorAssembledEvent"/> for layer-05, and (when a live Domain
/// <see cref="Actor"/> is supplied) registers it in the <see cref="ClientWorld"/> registry. This
/// service does NOT re-implement the registry — it reuses <see cref="ClientWorld.Add(Actor)"/>. spec:
/// Docs/RE/specs/assembly_graph.md §2/§4.
/// </summary>
/// <remarks>
/// <b>Threading.</b> Single-owner: driven by the same network-reader logical owner that mutates
/// Domain and <see cref="ClientWorld"/>. The composer is pure; this service only sequences the bake,
/// the publish, and the (optional) registry add. Do not call concurrently. spec: ClientWorld /
/// SectorStreamingService threading notes.
/// </remarks>
public sealed class ActorSpawnService
{
    private readonly ActorComposer _composer;
    private readonly IClientEventBus _eventBus;
    private readonly ClientWorld _world;

    /// <summary>Creates the spawn service over the composer, the outbound bus, and the live-actor registry.</summary>
    public ActorSpawnService(ActorComposer composer, IClientEventBus eventBus, ClientWorld world)
    {
        ArgumentNullException.ThrowIfNull(composer);
        ArgumentNullException.ThrowIfNull(eventBus);
        ArgumentNullException.ThrowIfNull(world);
        _composer = composer;
        _eventBus = eventBus;
        _world = world;
    }

    /// <summary>
    /// Bakes a neutral spawn (the offline port builds this from an <c>.arr</c> record + the visual
    /// catalogue) into an <see cref="AssembledActor"/> and publishes its
    /// <see cref="ActorAssembledEvent"/>. No Domain registry entry is created — an <c>.arr</c>-derived
    /// NPC has no server-assigned <see cref="ActorKey"/>; the live actor registry is populated only
    /// from the wire path (packet 4/4 → 5/3 → a Domain <see cref="Actor"/>). spec:
    /// Docs/RE/specs/assembly_graph.md §1 (live actors are server-driven; the <c>.arr</c> is
    /// position/facing/static metadata only) / §4 (offline-port synthesis).
    /// </summary>
    /// <returns>The baked descriptor (already published to the bus).</returns>
    public AssembledActor Spawn(in ActorSpawn spawn)
    {
        AssembledActor actor = _composer.Compose(in spawn);
        _eventBus.Publish(new ActorAssembledEvent(actor));
        return actor;
    }

    /// <summary>
    /// Bakes a spawn from the wire 880-byte SpawnDescriptor span plus the caller-resolved identity
    /// inputs (the value-edges <see cref="SpawnDescriptorReader"/> does not byte-decode), publishes its
    /// <see cref="ActorAssembledEvent"/>, and registers the supplied live Domain <paramref name="actor"/>
    /// in <see cref="ClientWorld"/>. The descriptor supplies the placement; the caller supplies both the
    /// resolved <c>id_b</c> / <c>model_class_id</c> / equipment gids (composer identity) and the Domain
    /// <see cref="Actor"/> (the registry identity + vitals built by the 5/3 handler). spec:
    /// Docs/RE/structs/spawn_descriptor.md; Docs/RE/specs/assembly_graph.md §1 (the World and Actor
    /// chains meet where a spawned actor is placed onto a streamed cell).
    /// </summary>
    /// <param name="descriptor">The 880-byte spawn descriptor span (the 5/3 CharSpawn inner block).</param>
    /// <param name="identity">
    /// The caller-resolved composer identity inputs (<c>id_b</c>, <c>model_class_id</c>, motion key,
    /// equipment gids). Its world placement fields are ignored — they come from the descriptor.
    /// </param>
    /// <param name="actor">The live Domain actor to register (its <see cref="Actor.Key"/> is the registry key).</param>
    /// <returns>The baked descriptor (already published to the bus and registered in the world).</returns>
    public AssembledActor Spawn(ReadOnlySpan<byte> descriptor, in ActorSpawn identity, Actor actor)
    {
        ArgumentNullException.ThrowIfNull(actor);

        AssembledActor assembled = _composer.Compose(descriptor, in identity);

        // Register the live actor BEFORE publishing the visual so the presentation, when it drains the
        // event, can already resolve the actor's authoritative Domain state by key. Reuses the existing
        // registry — never re-implements it. spec: ClientWorld (the application-owned actor registry).
        _world.Add(actor);

        _eventBus.Publish(new ActorAssembledEvent(assembled));
        return assembled;
    }
}