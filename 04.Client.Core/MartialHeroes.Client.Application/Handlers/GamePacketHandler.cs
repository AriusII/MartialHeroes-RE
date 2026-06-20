using System.Buffers.Binary;
using System.Runtime.InteropServices;
using MartialHeroes.Client.Application.Diagnostics;
using MartialHeroes.Client.Application.Events;
using MartialHeroes.Client.Application.Hud;
using MartialHeroes.Client.Application.Login;
using MartialHeroes.Client.Application.Net;
using MartialHeroes.Client.Application.Scene;
using MartialHeroes.Client.Application.UseCases;
using MartialHeroes.Client.Application.World;
using MartialHeroes.Client.Domain.Actors;
using MartialHeroes.Client.Domain.Progression;
using MartialHeroes.Client.Domain.Skills;
using MartialHeroes.Client.Domain.Stats;
using MartialHeroes.Network.Protocol.Opcodes;
using MartialHeroes.Network.Protocol.Packets;
using MartialHeroes.Network.Protocol.Routing;
using MartialHeroes.Shared.Kernel.Ids;
using MartialHeroes.Shared.Kernel.Numerics;

namespace MartialHeroes.Client.Application.Handlers;

/// <summary>
/// The single inbound sink the <see cref="PacketRouter"/> dispatches into. Each typed overload
/// validates the wire message, applies it to the Domain via the <see cref="ClientWorld"/> registry
/// and the <see cref="Actor"/> controlled mutators, then publishes an immutable UI event on the
/// outbound <see cref="IClientEventBus"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Float -&gt; fixed boundary.</b> The wire carries IEEE-754 <c>float</c> world coordinates
/// (spec: Docs/RE/structs/actor.md "Coordinate type"; packets carry XZ-plane floats). This handler
/// is the application/network boundary, so it converts to the deterministic
/// <see cref="Vector3Fixed"/> via <see cref="Vector3Fixed.FromFloat"/> here and nowhere deeper. World
/// Y is forced to 0 because the server never sends it (spec: same section).
/// </para>
/// <para>
/// <b>No game-rule math.</b> Handlers compute nothing; they translate wire fields to Domain method
/// calls. Damage/stat/leveling formulas live in Domain.
/// </para>
/// <para>
/// <b>Threading.</b> Invoked by the single network-reader logical owner; it mutates Domain and the
/// registry without locking. Events published are immutable snapshots, so the UI consumer never sees
/// torn Domain state.
/// </para>
/// </remarks>
public sealed class GamePacketHandler : IPacketHandler
{
    private readonly ClientWorld _world;
    private readonly IClientEventBus _eventBus;
    private readonly IUnhandledOpcodeSink _unhandled;
    private readonly ILoginHandshakeDriver? _loginDriver;
    private readonly SceneStateMachine? _sceneStateMachine;
    private readonly LocalPlayerState? _localPlayer;
    private readonly CharacterSelectionStore? _characterSelection;
    private readonly AccountCharacterState? _accountCharacters;
    private readonly IHudEventHub? _hudEventHub;
    private readonly InFlightLatch? _inFlightLatch;
    private readonly WorldEntryState? _worldEntry;

    /// <summary>
    /// The combat-stat recompute seam: invoked whenever an equip / buff / level change should re-accumulate
    /// the local player's derived combat-stat aggregate. The composition root supplies the resolver (it owns
    /// the injected equipment / buff / server-base data the recompose needs); when absent, the recompose is
    /// skipped. The default is a no-op that returns the current aggregate unchanged. spec:
    /// Docs/RE/specs/combat.md §1 / §2 (re-accumulate on input change).
    /// </summary>
    public Func<CombatStats, CombatStats>? CombatStatsRecompute { get; init; }

    /// <summary>
    /// Per-skill cooldown duration resolver (ms) used when the 5/33 hotbar overwrite arms a recast slot.
    /// The Application/Assets layer owns the skills.scr catalogue lookup; when absent the duration is 0
    /// (a ready slot). spec: Docs/RE/specs/skills.md §4 (duration = cooldown_centiseconds × 100).
    /// </summary>
    public Func<SkillId, int>? CooldownDurationResolver { get; init; }

    /// <summary>
    /// The local player's progression aggregate — the experience accumulators and the rank/honor XP
    /// channel the <c>5/9 ExpGain</c> and <c>5/11 RankXpGain</c> handlers advance. The Domain owns the
    /// arithmetic (<see cref="ProgressionState"/>); this handler holds the live state so the routing
    /// has somewhere authoritative to apply it. spec: Docs/RE/specs/progression.md §3 / §4 / §11.
    /// </summary>
    public ProgressionState Progression { get; private set; }

    /// <summary>
    /// The server-set XP percentage-bonus rate used by the <c>5/9</c> §3.1 display split. This is a
    /// server-authored global, NOT a client constant (spec: progression.md §12 Q6); the composition root
    /// supplies it once a capture pins the value. Defaults to 0 (no bonus) so nothing is invented.
    /// spec: Docs/RE/specs/progression.md §3.1.
    /// </summary>
    public Func<long>? XpBonusRatePercentResolver { get; init; }

    /// <summary>
    /// The per-level rank-XP <em>divisor</em> table for the <c>5/11</c> §4 routine, indexed by the
    /// local-player level cache. Server/config DATA, not a client constant (spec: progression.md §12 Q6);
    /// supplied by the composition root, defaulting to empty so the Domain never invents magnitudes.
    /// A 0 divisor for the active level is the documented "leveltable error". spec: Docs/RE/specs/progression.md §4.
    /// </summary>
    public IReadOnlyList<long>? RankXpDivisorTable { get; init; }

    /// <summary>
    /// The per-level rank-XP <em>cap</em> table for the <c>5/11</c> §4 routine (bounds the within-rank
    /// remainder), indexed by the local-player level cache. Server/config DATA, supplied by the
    /// composition root, defaulting to empty. spec: Docs/RE/specs/progression.md §4.
    /// </summary>
    public IReadOnlyList<long>? RankXpCapTable { get; init; }

    /// <summary>
    /// Refresh seam fired after each <c>5/9</c>/<c>5/11</c> progression mutation, carrying the new
    /// aggregate so the presentation can refresh the XP-bar / rank-bar (the streaming HUD gage is a
    /// separate widget — spec: progression.md §12 Q3). Engine-free; the composition root wires the
    /// renderer. When absent the mutation still applies (the state is observable via
    /// <see cref="Progression"/>). spec: Docs/RE/specs/progression.md §3 / §4 / §11.
    /// </summary>
    public Action<ProgressionState>? ProgressionRefresh { get; init; }

    /// <summary>
    /// Diagnostics seam for the <c>5/11</c> "leveltable error" (a 0 divisor for the active level), so the
    /// application can log it without crashing the update — mirroring the client diagnostic.
    /// spec: Docs/RE/specs/progression.md §4.
    /// </summary>
    public Action<int>? LevelTableErrorSink { get; init; }

    /// <summary>
    /// Resolves the vital capacities for a freshly-spawned actor from its wire-reported current HP.
    /// </summary>
    /// <remarks>
    /// The SpawnDescriptor carries only <em>current</em> HP/MP/stamina; max HP/MP are not wire fields
    /// (spec: Docs/RE/structs/actor.md "max_hp / max_mp are NOT stored as fields" — they are computed
    /// from base stats + equipment). The real growth formula is not yet documented (the stat block at
    /// descriptor +0xD4 is unmapped), so this seam is injected: the composition root supplies the
    /// resolution, and Domain owns any actual formula. The default seeds capacity from the reported
    /// current values so spawn HP is not clamped away — a transparent placeholder, not a game formula.
    /// </remarks>
    public Func<SpawnInfo, VitalStats> VitalsResolver { get; init; } = DefaultVitalsResolver;

    public GamePacketHandler(
        ClientWorld world,
        IClientEventBus eventBus,
        IUnhandledOpcodeSink unhandled,
        ILoginHandshakeDriver? loginDriver = null,
        LocalPlayerState? localPlayer = null,
        CharacterSelectionStore? characterSelection = null,
        AccountCharacterState? accountCharacters = null,
        SceneStateMachine? sceneStateMachine = null,
        IHudEventHub? hudEventHub = null,
        InFlightLatch? inFlightLatch = null,
        WorldEntryState? worldEntry = null)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _unhandled = unhandled ?? throw new ArgumentNullException(nameof(unhandled));
        _loginDriver = loginDriver; // optional: only needed for the login handshake flow
        _sceneStateMachine = sceneStateMachine; // optional: faithful 8-state scene spine
        _localPlayer = localPlayer; // optional: only needed for the skill/buff/combat subsystems
        _characterSelection = characterSelection; // optional: only needed for the 3/1 cache + 3/14 spawn
        _accountCharacters = accountCharacters; // optional: tracks the create/delete char-count deltas
        _hudEventHub = hudEventHub; // optional: combat-text / buff HUD stream sink (5/52, 4/102)
        _inFlightLatch = inFlightLatch; // optional: the single in-flight latch (cleared by 3/x results + 4/1)
        _worldEntry = worldEntry; // optional: durable 4/1 world-entry holder the InGame scene recovers from
    }

    // -------------------------------------------------------------------------
    // 5/13 — actor movement update
    // -------------------------------------------------------------------------

    /// <summary>
    /// 5/13 — actor movement update. Converts the wire float XZ coords to <see cref="Vector3Fixed"/>
    /// at this boundary, updates the actor's position/target/yaw, and emits <see cref="ActorMovedEvent"/>.
    /// spec: Docs/RE/packets/5-13_actor_movement_update.yaml; Docs/RE/structs/actor.md (coords float).
    /// </summary>
    public void Handle(in SmsgActorMovementUpdate packet)
    {
        EntitySort sort = ToEntitySort((byte)packet.Sort);
        var key = new ActorKey(packet.ActorId, sort);

        // Float -> fixed conversion happens HERE, at the network/application boundary. World Y = 0.
        // spec: Docs/RE/structs/actor.md ("the server never sends Y and the client forces it to 0").
        Vector3Fixed position = Vector3Fixed.FromFloat(packet.PosX, 0f, packet.PosZ);
        Vector3Fixed target = Vector3Fixed.FromFloat(packet.DestX, 0f, packet.DestZ);
        int yaw = Vector3Fixed.FromFloat(packet.Yaw, 0f, 0f).RawX; // reuse Q16.16 conversion for the yaw scalar

        if (!_world.TryGet(key, out Actor actor))
        {
            // Movement for an actor we have not seen spawn: register a minimal placeholder so the
            // world stays consistent. Vitals are zero; a later spawn/vitals push fills them in.
            actor = new Actor(
                key,
                level: 0,
                vitals: VitalStats.Zero,
                currentHp: 0,
                currentMp: 0,
                currentStamina: 0,
                position: position,
                moveSpeedRawPerSecond: 0,
                yaw: yaw);
            _world.Add(actor);
        }

        // MotionCode == 5 is the legacy "instant snap" branch. spec: 5-13 (MotionCode), actor.md SnapTo.
        const byte instantSnapMotion = 5;
        if (packet.MotionCode == instantSnapMotion)
        {
            actor.SnapTo(position);
        }
        else
        {
            actor.SnapTo(position); // seed the current position from the network sample
            actor.SetMoveTarget(target); // then interpolate toward the destination
        }

        actor.SetYaw(yaw);
        actor.SetLifecycle(packet.RunFlag != 0 ? LifecycleState.Running : LifecycleState.Walking);

        // Derive the pure motion intent from the wire MotionCode (@+0x24) and RunFlag (@+0x1C) and
        // attach it WITHOUT changing the snap/move-target behaviour above (the existing SnapTo /
        // SetMoveTarget calls are unchanged; this only adds the animation-classification intent).
        // spec: Docs/RE/packets/5-13_actor_movement_update.yaml; Docs/RE/specs/skinning.md §10.
        actor.SetMotionIntent(MotionIntentMap.Resolve(packet.MotionCode, packet.RunFlag));

        _eventBus.Publish(new ActorMovedEvent(
            key, actor.Position, actor.MoveTarget, actor.Yaw, packet.RunFlag != 0));
    }

    // -------------------------------------------------------------------------
    // 5/3 — actor spawn
    // -------------------------------------------------------------------------

    /// <summary>
    /// 5/3 — actor spawn. Decodes the embedded 880-byte SpawnDescriptor (name, level, vitals, world
    /// XZ, server class), converts the float coords to <see cref="Vector3Fixed"/> at this boundary,
    /// creates and registers the Domain <see cref="Actor"/>, and emits <see cref="ActorSpawnedEvent"/>.
    /// spec: Docs/RE/packets/5-3_char_spawn.yaml; Docs/RE/structs/actor.md (SpawnDescriptor).
    /// </summary>
    public void Handle(in SmsgCharSpawn packet)
    {
        EntitySort sort = ToEntitySort((byte)packet.Sort);
        var key = new ActorKey(packet.ActorId, sort);

        // The SpawnDescriptor is an [InlineArray(880)] inside the packet; the language projects it to
        // a ReadOnlySpan<byte> with no copy. spec: packets/5-3_char_spawn.yaml (opaque 880-byte blob).
        ReadOnlySpan<byte> descriptorBytes = packet.SpawnDescriptor;
        var reader = new SpawnDescriptorReader(descriptorBytes);

        string name = reader.ReadName();
        ushort level = reader.ReadLevel();
        uint currentHp = reader.ReadCurrentHp();
        uint currentMp = reader.ReadCurrentMp();
        uint currentStamina = reader.ReadCurrentStamina();
        ushort serverClass = reader.ReadServerClass();

        // Float -> fixed at the boundary; world Y forced to 0. spec: actor.md (coords float, Y = 0).
        Vector3Fixed position = Vector3Fixed.FromFloat(reader.ReadWorldX(), 0f, reader.ReadWorldZ());

        var spawnInfo = new SpawnInfo(key, level, currentHp, currentMp, currentStamina, serverClass);
        VitalStats vitals = VitalsResolver(spawnInfo);

        var actor = new Actor(
            key,
            level,
            vitals,
            currentHp,
            currentMp,
            currentStamina,
            position);

        _world.Add(actor);

        _eventBus.Publish(new ActorSpawnedEvent(
            key, name, level, actor.Position, actor.CurrentHp, actor.MaxHp, serverClass));
    }

    // -------------------------------------------------------------------------
    // 5/0 — actor despawn
    // -------------------------------------------------------------------------

    /// <summary>
    /// 5/0 — actor despawn. Removes the actor from the registry and emits
    /// <see cref="ActorDespawnedEvent"/>. spec: Docs/RE/packets/5-0_char_despawn.yaml.
    /// </summary>
    public void Handle(in SmsgCharDespawn packet)
    {
        EntitySort sort = ToEntitySort((byte)packet.Sort);
        var key = new ActorKey(packet.ActorId, sort);

        _world.Remove(key);

        const byte leaveEffectBit = 0x01; // bit0 => play "left" SFX + chat line. spec: 5-0 Flags.
        bool playLeaveEffect = (packet.Flags & leaveEffectBit) != 0;
        _eventBus.Publish(new ActorDespawnedEvent(key, playLeaveEffect));
    }

    // -------------------------------------------------------------------------
    // 3/5 — enter-game ack
    // -------------------------------------------------------------------------

    /// <summary>
    /// 3/5 — enter-world acknowledgement / post-login account-ack. Drives the scene spine to Load
    /// (state 2) and seeds the account character-count authoritatively from char_count@40. 3/5 is
    /// state-agnostic and UNSOLICITED — it is processed regardless of any prior 1/9 request. spec:
    /// Docs/RE/specs/client_runtime.md §7.5.2; Docs/RE/packets/3-5_enter_game_response.yaml.
    /// </summary>
    public void Handle(in SmsgEnterGameAck packet)
    {
        _ = packet.BillingFlag; // available for a future use case; billing behavior is not invented here.
        _sceneStateMachine?.OnEnterGameAck();

        // Seed the account char-count authoritatively from char_count@40. Set() clamps 0..5, so passing the
        // raw u32 is safe; the int cast is guarded against overflow. 3/5 is unsolicited (NOT gated on a prior
        // 1/9). spec: Docs/RE/specs/login_flow.md §3.4 / §5.2 (3/5 char_count@40 seeds the account char-count);
        // §1 step 7 (3/5 is unsolicited, NOT gated on a prior 1/9).
        _accountCharacters?.Set((int)Math.Min(packet.CharacterCount, (uint)int.MaxValue));
    }

    // -------------------------------------------------------------------------
    // 4/1 — game-state tick / world-entry snapshot
    // -------------------------------------------------------------------------

    /// <summary>
    /// 4/1 — world-entry game-state tick. On form 1, reads spawn X/Z from the large 9100-byte body,
    /// creates the local player from the cached select descriptor when needed, and publishes an
    /// engine-free bootstrap event for the world scene. If no local actor can be materialized, drives
    /// the recovered 5→4 scene fallback. spec: Docs/RE/specs/handlers.md §4/1;
    /// Docs/RE/specs/client_runtime.md §7.5.2 and §9.1/§9.4.
    /// </summary>
    private void HandleGameStateTick(ReadOnlySpan<byte> payload)
    {
        // 4/1's very first action, before any form branch, is to CLEAR the single in-flight latch
        // (this closes the 1/9 → 3/5 → 4/1 enter ladder; 3/5 leaves the latch armed). Unconditional.
        // spec: Docs/RE/specs/world_entry.md §2.3 / §3.3; Docs/RE/specs/net_contracts.md §1.3;
        // Docs/RE/specs/handlers.md §4/1 (latch clear is the first statement).
        _inFlightLatch?.Clear();

        if (!SmsgGameStateTick.TryReadWorldEntrySeed(payload, out SmsgGameStateTickSeed seed))
        {
            if (_world.LocalActor is null)
            {
                _sceneStateMachine?.OnGameStateTickNoLocalPlayer();
            }

            return;
        }

        Vector3Fixed position = Vector3Fixed.FromFloat(seed.SpawnX, 0f, seed.SpawnZ);

        if (_world.LocalActor is { } existing)
        {
            existing.SnapTo(position);
            // spec: Docs/RE/specs/world_entry.md §2.3 / §3.1 — persist the world entry so the InGame scene
            // can recover the cold-start after the SingleReader channel handoff.
            _worldEntry?.Record(seed.AreaId, position);
            _eventBus.Publish(new InGameWorldBootstrappedEvent(existing.Key, position, seed.AreaId));
            return;
        }

        if (!TryCreateLocalPlayerFromCachedDescriptor(
                position, out Actor? actor, out int slotIndex, out string name, out ushort serverClass))
        {
            _sceneStateMachine?.OnGameStateTickNoLocalPlayer();
            return;
        }

        Actor localActor = actor!;
        _eventBus.Publish(new LocalPlayerSpawnedEvent(
            localActor.Key, slotIndex, name, localActor.Level, localActor.Position, localActor.CurrentHp,
            localActor.MaxHp,
            serverClass));
        // spec: Docs/RE/specs/world_entry.md §2.3 / §3.1 — persist the world entry so the InGame scene
        // can recover the cold-start after the SingleReader channel handoff.
        _worldEntry?.Record(seed.AreaId, position);
        _eventBus.Publish(new InGameWorldBootstrappedEvent(localActor.Key, position, seed.AreaId));
    }

    // -------------------------------------------------------------------------
    // 4/4 — area entity snapshot (17-byte header + tag loop)
    // -------------------------------------------------------------------------

    /// <summary>
    /// 4/4 — area entity snapshot. Reinterprets the fixed 17-byte area header, then walks the variable
    /// tag loop from payload[<see cref="SmsgAreaEntitySnapshot.HeaderSize"/>..]: each iteration reads one
    /// tag u8 (tag == 0 ends the loop) and the tag-specific record. Tags 1/2/3 carry a 892-byte actor
    /// record (= 8-byte prefix + 880-byte SpawnDescriptor core + 4-byte trailer per §21), with the sort
    /// carried by the tag (1 = PC, 2 = mob, 3 = NPC) and the actor lookup key at record +0; each spawns
    /// and registers an actor and publishes <see cref="ActorSpawnedEvent"/> exactly like 5/3. Tags 4/6/9
    /// raise the real engine-free overlay/ground-item events (tag 4 = ground item, tag 6 = guild
    /// overlay, tag 9 = title/relation overlay). The loop is bounded and stops on any short read; on
    /// drain it publishes <see cref="AreaPopulatedEvent"/>. spec: Docs/RE/specs/handlers.md §4/4 + §21;
    /// Docs/RE/specs/world_entry.md §2.4; Docs/RE/packets/4-4_ground_item_tag4.yaml.
    /// </summary>
    private bool HandleAreaEntitySnapshot(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < SmsgAreaEntitySnapshot.HeaderSize)
        {
            return false;
        }

        // Header is read for the area-centre recenter coords; only the two f32s are consumed. spec: §10.
        ref readonly SmsgAreaEntitySnapshot header = ref MemoryMarshal.AsRef<SmsgAreaEntitySnapshot>(payload);
        float areaCentreX = header.AreaCentreX; // recenter coords; the actor spawns carry absolute XZ. spec: §10.
        float areaCentreZ = header.AreaCentreZ;

        int spawnedActorCount = 0; // tag-1/2/3 actors spawned this snapshot (carried by AreaPopulatedEvent).

        // The 892-byte actor record splits 8 (prefix) + 880 (descriptor core) + 4 (trailer). spec: §21.
        const int actorPrefixSize = 8; // entity id-key u32 at +0 within this prefix. spec: handlers.md §21.

        int cursor = SmsgAreaEntitySnapshot.HeaderSize;
        const int maxIterations = 256; // bound the loop; tag == 0 normally terminates. spec: §10 (loop ends on tag 0).
        for (int i = 0; i < maxIterations; i++)
        {
            if (cursor >= payload.Length)
            {
                break; // short read — stop.
            }

            byte tag = payload[cursor];
            cursor++;

            if (tag == 0)
            {
                break; // tag == 0 terminates the loop. spec: handlers.md §10.
            }

            switch (tag)
            {
                case 1: // player character (sort 1). spec: handlers.md §10 + §21.
                case 2: // mob (sort 2). spec: handlers.md §10 + §21.
                case 3: // NPC (sort 3). spec: handlers.md §10 + §21.
                    if (cursor + SmsgAreaEntitySnapshot.ActorRecordSize > payload.Length)
                    {
                        return true; // short read — consumed what we could.
                    }

                    ReadOnlySpan<byte> actorRecord =
                        payload.Slice(cursor, SmsgAreaEntitySnapshot.ActorRecordSize);
                    cursor += SmsgAreaEntitySnapshot.ActorRecordSize;

                    // Entity id-key u32 is in the 8-byte prefix at record +0; the sort is the tag. spec: §21.
                    uint actorId = BinaryPrimitives.ReadUInt32LittleEndian(actorRecord[..sizeof(uint)]);
                    var key = new ActorKey(actorId, ToEntitySort(tag));

                    // The 880-byte SpawnDescriptor core follows the 8-byte prefix. spec: handlers.md §21.
                    ReadOnlySpan<byte> descriptorBytes =
                        actorRecord.Slice(actorPrefixSize, SpawnDescriptorReader.Size);
                    var reader = new SpawnDescriptorReader(descriptorBytes);

                    string name = reader.ReadName();
                    ushort level = reader.ReadLevel();
                    uint currentHp = reader.ReadCurrentHp();
                    uint currentMp = reader.ReadCurrentMp();
                    uint currentStamina = reader.ReadCurrentStamina();
                    ushort serverClass = reader.ReadServerClass();

                    // Float -> fixed at the boundary; world Y forced to 0. spec: actor.md (coords float, Y = 0).
                    Vector3Fixed position =
                        Vector3Fixed.FromFloat(reader.ReadWorldX(), 0f, reader.ReadWorldZ());

                    var spawnInfo = new SpawnInfo(key, level, currentHp, currentMp, currentStamina, serverClass);
                    VitalStats vitals = VitalsResolver(spawnInfo);

                    var actor = new Actor(key, level, vitals, currentHp, currentMp, currentStamina, position);
                    _world.Add(actor);

                    _eventBus.Publish(new ActorSpawnedEvent(
                        key, name, level, actor.Position, actor.CurrentHp, actor.MaxHp, serverClass));
                    spawnedActorCount++;
                    break;

                case 4: // ground item (24-byte record). spec: handlers.md §4/4 (tag-4); 4-4_ground_item_tag4.yaml.
                    if (cursor + SmsgAreaEntitySnapshot.GroundItemRecordSize > payload.Length)
                    {
                        return PublishAreaPopulated(areaCentreX, areaCentreZ, spawnedActorCount);
                    }

                    PublishGroundItem(payload.Slice(cursor, SmsgAreaEntitySnapshot.GroundItemRecordSize));
                    cursor += SmsgAreaEntitySnapshot.GroundItemRecordSize;
                    break;

                case 6: // guild-name overlay (36-byte record). spec: handlers.md §4/4 (tag-6).
                    if (cursor + SmsgAreaEntitySnapshot.GuildRecordSize > payload.Length)
                    {
                        return PublishAreaPopulated(areaCentreX, areaCentreZ, spawnedActorCount);
                    }

                    PublishGuildOverlay(payload.Slice(cursor, SmsgAreaEntitySnapshot.GuildRecordSize));
                    cursor += SmsgAreaEntitySnapshot.GuildRecordSize;
                    break;

                case 9: // title / relation overlay (24-byte record). spec: handlers.md §4/4 (tag-9).
                    if (cursor + SmsgAreaEntitySnapshot.TitleRecordSize > payload.Length)
                    {
                        return PublishAreaPopulated(areaCentreX, areaCentreZ, spawnedActorCount);
                    }

                    PublishTitleOverlay(payload.Slice(cursor, SmsgAreaEntitySnapshot.TitleRecordSize));
                    cursor += SmsgAreaEntitySnapshot.TitleRecordSize;
                    break;

                default:
                    // An unknown tag has no recoverable record size; stop the loop to avoid mis-stepping.
                    // spec: handlers.md §10 (only tags 1/2/3/4/6/9 are enumerated).
                    return PublishAreaPopulated(areaCentreX, areaCentreZ, spawnedActorCount);
            }
        }

        // The tag loop drained (zero terminator or the iteration bound) — the area is populated. spec: §4/4.
        return PublishAreaPopulated(areaCentreX, areaCentreZ, spawnedActorCount);
    }

    /// <summary>
    /// Publishes the <see cref="AreaPopulatedEvent"/> when the 4/4 tag loop drains, returning true so the
    /// caller treats the snapshot as consumed. spec: Docs/RE/specs/handlers.md §4/4.
    /// </summary>
    private bool PublishAreaPopulated(float areaCentreX, float areaCentreZ, int spawnedActorCount)
    {
        _eventBus.Publish(new AreaPopulatedEvent(areaCentreX, areaCentreZ, spawnedActorCount));
        return true;
    }

    /// <summary>
    /// 4/4 tag-4 ground-item sub-record (24 bytes): Key u32@+0, TemplateId u32@+4, WorldX f32@+0x10,
    /// WorldZ f32@+0x14. Float -&gt; fixed at the boundary (Y forced 0). spec:
    /// Docs/RE/packets/4-4_ground_item_tag4.yaml; Docs/RE/specs/handlers.md §4/4 (tag-4).
    /// </summary>
    private void PublishGroundItem(ReadOnlySpan<byte> record)
    {
        uint key = BinaryPrimitives.ReadUInt32LittleEndian(record.Slice(0x00, sizeof(uint))); // +0x00 Key
        uint templateId = BinaryPrimitives.ReadUInt32LittleEndian(record.Slice(0x04, sizeof(uint))); // +0x04 TemplateId
        float worldX = BinaryPrimitives.ReadSingleLittleEndian(record.Slice(0x10, sizeof(float))); // +0x10 WorldX
        float worldZ = BinaryPrimitives.ReadSingleLittleEndian(record.Slice(0x14, sizeof(float))); // +0x14 WorldZ

        // Float -> fixed at the network/application boundary; world Y forced to 0. spec: 4-4 yaml.
        Vector3Fixed position = Vector3Fixed.FromFloat(worldX, 0f, worldZ);
        _eventBus.Publish(new GroundItemSpawnedEvent(key, templateId, position));
    }

    /// <summary>
    /// 4/4 tag-6 guild-name overlay sub-record (36 bytes): EntityId u32@+0, CP949 NUL-string @+0x05.
    /// spec: Docs/RE/specs/handlers.md §4/4 (tag-6).
    /// </summary>
    private void PublishGuildOverlay(ReadOnlySpan<byte> record)
    {
        uint entityId = BinaryPrimitives.ReadUInt32LittleEndian(record.Slice(0x00, sizeof(uint))); // +0x00 EntityId
        // Guild name occupies +0x05 up to 31 bytes (CP949, NUL-terminated). spec: handlers.md §4/4 (tag-6).
        string guildName = Cp949Text.Decode(record[0x05..]);
        _eventBus.Publish(new GuildOverlayEvent(entityId, guildName));
    }

    /// <summary>
    /// 4/4 tag-9 title / relation overlay sub-record (24 bytes): EntityId u32@+0, RelationState u8@+4,
    /// OverlaySubCode u8@+5, CP949 TitleName 17-byte cell @+6. RelationState / OverlaySubCode value
    /// MEANINGS are live-pending (world_entry.md §4 / handlers.md §4/4) — the raw bytes are forwarded,
    /// no enum meaning is invented. spec: Docs/RE/specs/handlers.md §4/4 (tag-9).
    /// </summary>
    private void PublishTitleOverlay(ReadOnlySpan<byte> record)
    {
        uint entityId = BinaryPrimitives.ReadUInt32LittleEndian(record.Slice(0x00, sizeof(uint))); // +0x00 EntityId
        byte relationState = record[0x04]; // +0x04 — value meaning live-pending (world_entry.md §4 / handlers.md §4/4).
        byte overlaySubCode =
            record[0x05]; // +0x05 — value meaning live-pending (world_entry.md §4 / handlers.md §4/4).
        string titleName = Cp949Text.Decode(record.Slice(0x06, 17)); // +0x06 TitleName (17-byte CP949 cell).
        _eventBus.Publish(new TitleOverlayEvent(entityId, relationState, overlaySubCode, titleName));
    }

    // -------------------------------------------------------------------------
    // 5/52 — actor skill action (24-byte header + 36-byte target records)
    // -------------------------------------------------------------------------

    /// <summary>
    /// 5/52 — actor skill action / combat result. Reinterprets the fixed 24-byte header, then loops
    /// <see cref="SmsgActorSkillAction.TargetCount"/> records of stride
    /// <see cref="SmsgActorSkillAction.TargetRecordStride"/> (36) from
    /// payload[<see cref="SmsgActorSkillAction.HeaderSize"/>..]. Per record it reads TargetSubKey @+0x00
    /// (u8, spec-agreed) and TargetKey @+0x04 (u32, spec-agreed); the 64-bit visible-damage value offset
    /// is AMBIGUOUS, so it reads BOTH candidate i64s raw and forwards them unmodified (no polarity/decode
    /// chosen). For each target it publishes a <see cref="CombatTextEvent"/> on the HUD hub (when wired)
    /// carrying the target key + skill id, with the raw damage candidates passed through. spec:
    /// Docs/RE/packets/5-52_actor_skill_action.yaml; Docs/RE/specs/handlers.md §17.11 / §20.3.
    /// </summary>
    private bool HandleActorSkillAction(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < SmsgActorSkillAction.HeaderSize)
        {
            return false;
        }

        ref readonly SmsgActorSkillAction header = ref MemoryMarshal.AsRef<SmsgActorSkillAction>(payload);
        uint skillId = header.SkillId; // header +0x0C (CONFIRMED). spec: 5-52 (SkillId @0x0C).

        ReadOnlySpan<byte> records = payload[SmsgActorSkillAction.HeaderSize..];

        // TargetCount is bounded (0, 0x28]; iterate as far as the buffer allows. spec: 5-52 (TargetCount @0x14).
        for (int t = 0; t < header.TargetCount; t++)
        {
            int recordStart = t * SmsgActorSkillAction.TargetRecordStride;
            if (recordStart + SmsgActorSkillAction.TargetRecordStride > records.Length)
            {
                break; // short read — stop.
            }

            ReadOnlySpan<byte> record = records.Slice(recordStart, SmsgActorSkillAction.TargetRecordStride);

            // Spec-agreed offsets: sub-key u8 @+0x00, key u32 @+0x04. spec: 5-52 (TargetSubKeyOffset/TargetKeyOffset).
            byte targetSubKey = record[SmsgActorSkillAction.TargetSubKeyOffset];
            uint targetKey = BinaryPrimitives.ReadUInt32LittleEndian(
                record.Slice(SmsgActorSkillAction.TargetKeyOffset, sizeof(uint)));

            // live-pending: damage offset ambiguous (handlers.md §17.11 +0x10/+0x14 vs 5-52.yaml +0x14/+0x18).
            // Read BOTH candidate i64s raw; do NOT pick a polarity or decode damage here.
            // spec: handlers.md §17.11 (polarity live-pending).
            long damageCandidateA =
                record.Length >= 0x10 + sizeof(long)
                    ? BinaryPrimitives.ReadInt64LittleEndian(record.Slice(0x10, sizeof(long)))
                    : 0L; // §17.11 reading: +0x10/+0x14.
            long damageCandidateB =
                record.Length >= 0x14 + sizeof(long)
                    ? BinaryPrimitives.ReadInt64LittleEndian(record.Slice(0x14, sizeof(long)))
                    : 0L; // 5-52.yaml reading: +0x14/+0x18.

            var key = new ActorKey(targetKey, ToEntitySort(targetSubKey));

            // No committed damage semantics: forward target key + skill id, raw candidates passed through.
            // Value left 0 (undecoded), Kind 0, IsCrit false until a capture pins the damage offset/polarity.
            _hudEventHub?.PublishCombatText(new CombatTextEvent(
                key,
                Value: 0,
                Kind: CombatTextEvent.MinKind,
                IsCrit: false,
                SkillId: skillId,
                RawDamageCandidateA: damageCandidateA,
                RawDamageCandidateB: damageCandidateB));
        }

        return true;
    }

    // -------------------------------------------------------------------------
    // 4/102 — full skill/state-window snapshot (30 buff records)
    // -------------------------------------------------------------------------

    /// <summary>
    /// 4/102 — full skill/state-window snapshot. Fixed 476-byte block; the 30 buff records are FIELDS
    /// inside the struct (so this is a typed Handle, not a span loop). Rebuilds the 30-slot HUD buff bar:
    /// each non-empty record (BuffXXId != 0) becomes a populated <see cref="BuffSlot"/>; empty records
    /// become <see cref="BuffSlot.EmptyBuffId"/>. The per-record 12-byte param roles are
    /// CAPTURE-UNVERIFIED (competing {id,X,Y} vs {id,?,duration,stack,flag}), so the duration is passed
    /// as a live-pending candidate (null) rather than inventing duration/stack semantics. Published to
    /// the HUD hub (when wired) via <see cref="IHudEventHub.PublishBuffState"/>. spec:
    /// Docs/RE/packets/4-102_buff_state.yaml.
    /// </summary>
    public void Handle(in SmsgSkillWindowStateUpdate packet)
    {
        if (_hudEventHub is null)
        {
            return; // no HUD sink wired — nothing to publish (the state is server-owned, no Domain mutation).
        }

        // Rebuild all 30 slots in wire order. spec: 4-102 (clear all 30, re-show active ones).
        var slots = System.Collections.Immutable.ImmutableArray.CreateBuilder<BuffSlot>(
            SmsgSkillWindowStateUpdate.BuffRecordCount);

        // live-pending: the per-record 12-byte param roles (X/Y vs duration/stack/flag) are CAPTURE-UNVERIFIED.
        // Pass the id through; carry the duration as a null candidate (do NOT invent ms/s/stack semantics).
        // spec: 4-102_buff_state.yaml (competing {id,X,Y} vs {id,?,duration,stack,flag}).
        AddBuffSlot(slots, packet.Buff00Id);
        AddBuffSlot(slots, packet.Buff01Id);
        AddBuffSlot(slots, packet.Buff02Id);
        AddBuffSlot(slots, packet.Buff03Id);
        AddBuffSlot(slots, packet.Buff04Id);
        AddBuffSlot(slots, packet.Buff05Id);
        AddBuffSlot(slots, packet.Buff06Id);
        AddBuffSlot(slots, packet.Buff07Id);
        AddBuffSlot(slots, packet.Buff08Id);
        AddBuffSlot(slots, packet.Buff09Id);
        AddBuffSlot(slots, packet.Buff10Id);
        AddBuffSlot(slots, packet.Buff11Id);
        AddBuffSlot(slots, packet.Buff12Id);
        AddBuffSlot(slots, packet.Buff13Id);
        AddBuffSlot(slots, packet.Buff14Id);
        AddBuffSlot(slots, packet.Buff15Id);
        AddBuffSlot(slots, packet.Buff16Id);
        AddBuffSlot(slots, packet.Buff17Id);
        AddBuffSlot(slots, packet.Buff18Id);
        AddBuffSlot(slots, packet.Buff19Id);
        AddBuffSlot(slots, packet.Buff20Id);
        AddBuffSlot(slots, packet.Buff21Id);
        AddBuffSlot(slots, packet.Buff22Id);
        AddBuffSlot(slots, packet.Buff23Id);
        AddBuffSlot(slots, packet.Buff24Id);
        AddBuffSlot(slots, packet.Buff25Id);
        AddBuffSlot(slots, packet.Buff26Id);
        AddBuffSlot(slots, packet.Buff27Id);
        AddBuffSlot(slots, packet.Buff28Id);
        AddBuffSlot(slots, packet.Buff29Id);

        _hudEventHub.PublishBuffState(BuffStateEvent.FromSlots(slots.MoveToImmutable()));
    }

    /// <summary>
    /// Appends one 4/102 buff record as a <see cref="BuffSlot"/>: a populated slot when the catalog id is
    /// non-zero, else the empty sentinel. The remaining-time candidate is null (live-pending — the
    /// duration field role is CAPTURE-UNVERIFIED). spec: Docs/RE/packets/4-102_buff_state.yaml.
    /// </summary>
    private static void AddBuffSlot(
        System.Collections.Immutable.ImmutableArray<BuffSlot>.Builder slots, uint buffId)
    {
        if (buffId == 0u)
        {
            slots.Add(new BuffSlot(BuffSlot.EmptyBuffId, RemainingTicks: null)); // empty slot. spec: 4-102.
            return;
        }

        // The catalog id is a u32 on the wire but the HUD slot keys it as u16; take the low word.
        // spec: 4-102_buff_state.yaml (buff id; HUD BuffSlot.BuffId is u16). live-pending: duration role.
        slots.Add(new BuffSlot(unchecked((ushort)buffId), RemainingTicks: null));
    }

    // -------------------------------------------------------------------------
    // Unhandled
    // -------------------------------------------------------------------------

    /// <summary>
    /// Opcodes the typed <see cref="PacketRouter"/> seam does not dispatch — the variable-length S2C
    /// messages whose handlers must read beyond their fixed header (chat text body, the 4/4 tag loop, the
    /// 5/52 target-record loop, per-field decoders) plus the login key exchange (0/0). We decode these
    /// from the raw payload span here and drive the login handshake on 0/0. Anything else is counted via
    /// the injected sink; never throws, never blocks. Fixed-size opcodes whose handler reads entirely
    /// within their struct are routed by the generator to a typed <c>Handle(in T)</c> overload instead.
    /// spec: Docs/RE/opcodes.md.
    /// </summary>
    public void OnUnhandled(uint packedOpcode, ReadOnlySpan<byte> payload)
    {
        switch (packedOpcode)
        {
            case Opcodes.SmsgKeyExchange: // 0/0 — login key exchange
                HandleKeyExchange(payload);
                return;

            case Opcodes.SmsgGameStateTick: // 4/1 — world-entry snapshot / state tick
                HandleGameStateTick(payload);
                return;

            case Opcodes.SmsgAreaEntitySnapshot: // 4/4 — area entity snapshot (17B header + tag loop)
                if (HandleAreaEntitySnapshot(payload))
                {
                    return;
                }

                break;

            case Opcodes.SmsgActorSkillAction: // 5/52 — actor skill action (24B header + 36B target records)
                if (HandleActorSkillAction(payload))
                {
                    return;
                }

                break;

            case Opcodes.SmsgSkillPointUpdate: // 4/150 — skill-point / level update (fixed 16-byte header)
                if (payload.Length >= SmsgSkillPointUpdateHeader.HeaderSize)
                {
                    HandleSkillPointUpdate(in MemoryMarshal.AsRef<SmsgSkillPointUpdateHeader>(payload));
                    return;
                }

                break;

            case Opcodes.SmsgBuffSlotUpdate: // 5/31 — buff/status slot update
                if (HandleBuffSlotUpdate(payload))
                {
                    return;
                }

                break;

            case Opcodes.SmsgStatsUpdate: // 5/67 — world-entry stat sync
                if (HandleStatsUpdate(payload))
                {
                    return;
                }

                break;

            case Opcodes.SmsgExpGain: // 5/9 — experience gain (32-byte payload)
                if (HandleExpGain(payload))
                {
                    return;
                }

                break;

            case Opcodes.SmsgRankXpGain: // 5/11 — rank/honor XP gain (20-byte payload)
                if (HandleRankXpGain(payload))
                {
                    return;
                }

                break;

            case Opcodes.SmsgCombatAttackUpdate: // 4/100 — combat attack / charge update
                if (HandleCombatAttackUpdate(payload))
                {
                    return;
                }

                break;

            case Opcodes.SmsgChatBroadcast: // 5/7 — chat broadcast (36-byte header + text)
                if (HandleChatBroadcast(payload))
                {
                    return;
                }

                break;

            case Opcodes.SmsgCharacterList: // 3/1 — character-select list (3-byte header + per-slot records)
                if (HandleCharacterList(payload))
                {
                    return;
                }

                break;

            case Opcodes.SmsgSceneEntityUpdate: // 3/4 — in-place roster refill (same 3+N×981 decode as 3/1)
                if (HandleSceneEntityUpdate(payload))
                {
                    return;
                }

                break;
        }

        _unhandled.Record(packedOpcode, payload.Length);
    }

    // -------------------------------------------------------------------------
    // 0/0 — login key exchange -> 1/4 Auth reply
    // -------------------------------------------------------------------------

    /// <summary>
    /// 0/0 — server KeyExchange. Delegates to the injected login driver, which parses the payload,
    /// builds the 1/4 reply from the staged credential, and sends it. spec: Docs/RE/specs/crypto.md §6.
    /// </summary>
    private void HandleKeyExchange(ReadOnlySpan<byte> payload)
    {
        if (_loginDriver is null)
        {
            _unhandled.Record(Opcodes.SmsgKeyExchange, payload.Length);
            return;
        }

        int replyBytes = _loginDriver.OnKeyExchange(payload);
        _eventBus.Publish(new LoginHandshakeCompletedEvent(replyBytes));
    }

    /// <summary>
    /// Transport/session disconnect notification — drives the scene machine (load-time → 7/2, else 7/8).
    /// spec: Docs/RE/specs/client_runtime.md §7.5.2.
    /// </summary>
    public void OnDisconnected()
    {
        // Reset the durable 4/1 world-entry record so a later InGame _Ready cannot cold-start a stale
        // area after a disconnect / back-to-select. spec: Docs/RE/specs/world_exit.md (world-leave reset).
        _worldEntry?.Clear();
        _sceneStateMachine?.OnDisconnected();
    }

    // -------------------------------------------------------------------------
    // 5/53 — actor vitals and pair state
    // -------------------------------------------------------------------------

    /// <summary>
    /// 5/53 — current vitals push. Updates the actor's HP/MP/stamina (capped against its computed
    /// maxima by Domain) and emits <see cref="ActorVitalsChangedEvent"/>. The sort value 8 normalises
    /// to 1. spec: Docs/RE/packets/5-53_actor_vitals_and_pair_state.yaml.
    /// </summary>
    public void Handle(in SmsgActorVitalsAndPairState packet)
    {
        byte rawSort = packet.Sort == 8 ? (byte)1 : packet.Sort; // spec: 5-53 (sort 8 -> 1)
        var key = new ActorKey(packet.ActorId, ToEntitySort(rawSort));
        if (!_world.TryGet(key, out Actor actor))
        {
            // No known actor: nothing to update. A later spawn fills state in.
            _unhandled.Record(Opcodes.SmsgActorVitalsAndPairState, SmsgActorVitalsAndPairState.WireSize);
            return;
        }

        // CurrentHp HIGH CONFIDENCE; VitalC is the third vital mirrored to the local-player MP global;
        // Stamina HIGH CONFIDENCE. spec: 5-53 (vital ordering otherwise unconfirmed).
        actor.SetCurrentHp(packet.CurrentHp);
        actor.SetCurrentMp(packet.VitalC);
        actor.SetCurrentStamina(packet.Stamina);

        _eventBus.Publish(new ActorVitalsChangedEvent(
            key, actor.CurrentHp, actor.CurrentMp, actor.CurrentStamina));
    }

    // -------------------------------------------------------------------------
    // 5/1 — extended actor spawn
    // -------------------------------------------------------------------------

    /// <summary>
    /// 5/1 — extended actor spawn. Decodes the embedded 880-byte SpawnDescriptor (name, level, vitals,
    /// world XZ, class) via <see cref="SpawnDescriptorReader"/>, converts coords to fixed-point at this
    /// boundary, registers the Domain actor, and emits <see cref="ActorSpawnedEvent"/>. spec:
    /// Docs/RE/packets/5-1_actor_spawn_extended.yaml; Docs/RE/structs/spawn_descriptor.md.
    /// </summary>
    public void Handle(in SmsgActorSpawnExtended packet)
    {
        EntitySort sort = ToEntitySort(packet.Sort);
        var key = new ActorKey(packet.ActorId, sort);

        // The 880-byte descriptor is an [InlineArray] projected to a span with no copy. spec: 5-1.
        ReadOnlySpan<byte> descriptorBytes = packet.SpawnDescriptor;
        var reader = new SpawnDescriptorReader(descriptorBytes);

        string name = reader.ReadName(); // SD +0x00. spec: spawn_descriptor.md
        ushort level = reader.ReadLevel(); // SD +0x3A. spec: spawn_descriptor.md
        uint currentHp = reader.ReadCurrentHp(); // SD +0x3C. spec: spawn_descriptor.md
        uint currentMp = reader.ReadCurrentMp(); // SD +0x40. spec: spawn_descriptor.md
        uint currentStamina = reader.ReadCurrentStamina(); // SD +0x44. spec: spawn_descriptor.md
        ushort serverClass = reader.ReadServerClass(); // SD +0x74. spec: spawn_descriptor.md

        // Float -> fixed at the boundary; world Y forced 0. spec: spawn_descriptor.md (+0x4C/+0x50).
        Vector3Fixed position = Vector3Fixed.FromFloat(reader.ReadWorldX(), 0f, reader.ReadWorldZ());

        var spawnInfo = new SpawnInfo(key, level, currentHp, currentMp, currentStamina, serverClass);
        VitalStats vitals = VitalsResolver(spawnInfo);

        var actor = new Actor(key, level, vitals, currentHp, currentMp, currentStamina, position);
        _world.Add(actor);

        _eventBus.Publish(new ActorSpawnedEvent(
            key, name, level, actor.Position, actor.CurrentHp, actor.MaxHp, serverClass));
    }

    // -------------------------------------------------------------------------
    // 4/29 — stat update
    // -------------------------------------------------------------------------

    /// <summary>
    /// 4/29 — stat-allocation ack. Applied only when ResultOk == 1; emits
    /// <see cref="ActorStatsChangedEvent"/> with the five echoed absolute stats and remaining points.
    /// The five stat values are wire echoes (Domain owns no stat-allocation mutator yet), so this
    /// handler publishes the snapshot without re-deriving anything. spec:
    /// Docs/RE/packets/4-29_stat_update.yaml.
    /// </summary>
    public void Handle(in SmsgStatUpdate packet)
    {
        const byte applied = 1; // ResultOk == 1 applies the update. spec: 4-29.
        if (packet.ResultOk != applied)
        {
            return;
        }

        // The stat update targets the local player; key it on the known local actor when present.
        ActorKey key = _world.LocalActorKey ?? new ActorKey(packet.Handle, EntitySort.PlayerCharacter);

        _eventBus.Publish(new ActorStatsChangedEvent(
            key, packet.Stat0, packet.Stat1, packet.Stat2, packet.Stat3, packet.Stat4,
            packet.RemainingStatPoints));
    }

    // -------------------------------------------------------------------------
    // 5/32 — level up
    // -------------------------------------------------------------------------

    /// <summary>
    /// 5/32 — level up. Updates the actor's level and refreshed vitals, then emits
    /// <see cref="ActorLeveledUpEvent"/>. HP/MP are packed as two i32 halves in one i64 (HP = low,
    /// MP = high). spec: Docs/RE/packets/5-32_level_up.yaml (HpMpPacked, HIGH CONFIDENCE core).
    /// </summary>
    public void Handle(in SmsgLevelUp packet)
    {
        var key = new ActorKey(packet.ActorId, ToEntitySort(packet.Sort));

        // HP = low i32 half, MP = high i32 half of the packed value. spec: 5-32 (HpMpPacked).
        uint currentHp = unchecked((uint)(packet.HpMpPacked & 0xFFFF_FFFF)); // 0x14 low
        uint currentMp = unchecked((uint)((packet.HpMpPacked >> 32) & 0xFFFF_FFFF)); // 0x14 high
        uint currentStamina = unchecked((uint)packet.Stamina); // 0x1c

        if (_world.TryGet(key, out Actor actor))
        {
            actor.SetLevel(packet.NewLevel);
            actor.SetCurrentHp(currentHp);
            actor.SetCurrentMp(currentMp);
            actor.SetCurrentStamina(currentStamina);
        }

        _eventBus.Publish(new ActorLeveledUpEvent(
            key, packet.NewLevel, currentHp, currentMp, currentStamina, packet.RemainingStatPoints));
    }

    // -------------------------------------------------------------------------
    // 4/12 — equip/unequip result
    // -------------------------------------------------------------------------

    /// <summary>
    /// 4/12 — equip/unequip result. On success applies the equipment-slot/visual update to the local
    /// player and triggers a combat-stat recompute (equipment changed); ToSlot 15 forces a title-slot
    /// visual rebuild. spec: Docs/RE/specs/handlers.md §3 (4/12); Docs/RE/structs/item.md.
    /// </summary>
    public void Handle(in SmsgEquipItemResult packet)
    {
        const byte ok = 1; // result 1 = success. spec: handlers.md §3 (4/12 result byte).
        const byte titleSlot = 15; // ToSlot 15 = title/gear visual rebuild. spec: handlers.md §3 / item.md.
        bool success = packet.Result == ok;

        if (success)
        {
            // Equipment changed -> the derived combat-stat aggregate must be re-accumulated. spec: combat.md §2.
            RecomputeCombatStats();
        }

        _eventBus.Publish(new EquipResultEvent(
            success, packet.FromSlot, packet.ToSlot, packet.ToSlot == titleSlot));
    }

    // -------------------------------------------------------------------------
    // 4/22 — item-slot state ack
    // -------------------------------------------------------------------------

    /// <summary>
    /// 4/22 — item-slot state ack: a slot's state plus stat/enchant fields. On success a recompute is
    /// triggered (the slot's stats may feed the aggregate). spec: Docs/RE/specs/handlers.md §13 Group B
    /// (4/22); Docs/RE/structs/item.md.
    /// </summary>
    public void Handle(in SmsgItemSlotStateAck packet)
    {
        const byte ok = 1; // result 1 = ok. spec: item.md (4/22 result byte).
        bool success = packet.Result == ok;

        if (success)
        {
            RecomputeCombatStats(); // a slot's stat/enchant fields may change the aggregate. spec: combat.md §2.
        }

        _eventBus.Publish(new ItemSlotStateEvent(
            success, packet.FromSlot, packet.ToSlot, packet.BonusField1, packet.BonusField2, packet.BonusField3));
    }

    // -------------------------------------------------------------------------
    // 4/19 — NPC buy / acquire ack
    // -------------------------------------------------------------------------

    /// <summary>
    /// 4/19 — NPC buy / inventory-acquire ack. Publishes the acquire outcome (slot, item actor id, gold).
    /// spec: Docs/RE/specs/handlers.md §13 Group B (4/19); Docs/RE/structs/item.md.
    /// </summary>
    public void Handle(in SmsgNpcBuyOrAcquireAck packet)
    {
        const byte ok = 1; // result 1 = ok. spec: item.md (4/19 result byte).
        bool success = packet.Result == ok;

        _eventBus.Publish(new NpcAcquireResultEvent(
            success, packet.ReasonCode, packet.BagSlotIndex, packet.ItemQuadB, packet.GoldLo));
    }

    // -------------------------------------------------------------------------
    // 5/33 — skill hotbar slot overwrite
    // -------------------------------------------------------------------------

    /// <summary>
    /// 5/33 — authoritative server overwrite of one skill-hotbar slot for the local player. Writes the
    /// {skill, points} entry into the 240-slot hotbar (mirroring the cooldown duration), then emits the
    /// snapshot. Ignored when no <see cref="LocalPlayerState"/> is wired. spec: Docs/RE/specs/handlers.md §4
    /// (5/33); Docs/RE/structs/skill.md.
    /// </summary>
    public void Handle(in SmsgSkillHotbarSlotSet packet)
    {
        // HotbarSlot must be < 240. spec: structs/skill.md (hotbar_slot < 0xF0).
        if (packet.HotbarSlot >= SmsgSkillHotbarSlotSet.HotbarSlotCount)
        {
            _unhandled.Record(Opcodes.SmsgSkillHotbarSlotSet, SmsgSkillHotbarSlotSet.WireSize);
            return;
        }

        var skill = new SkillId(unchecked((uint)packet.SkillId));
        if (_localPlayer is not null)
        {
            int cooldownMs =
                CooldownDurationResolver?.Invoke(skill) ?? 0; // skills.scr lookup; 0 = ready. spec: skills.md §4.
            _localPlayer.SetHotbarSlot(packet.HotbarSlot, skill, packet.SkillPoints, cooldownMs);
        }

        _eventBus.Publish(new SkillHotbarSlotSetEvent(packet.HotbarSlot, skill, packet.SkillPoints));
    }

    // -------------------------------------------------------------------------
    // 4/41 — skill hotbar assign result
    // -------------------------------------------------------------------------

    /// <summary>
    /// 4/41 — result of a client-initiated hotbar assignment. spec: Docs/RE/specs/handlers.md §13 Group C
    /// (4/41); Docs/RE/structs/skill.md.
    /// </summary>
    public void Handle(in SmsgSkillHotbarAssignResult packet)
    {
        const byte ok = 1; // gate 1 = apply/ok. spec: structs/skill.md (4/41 gate).
        bool success = packet.Gate == ok;

        _eventBus.Publish(new SkillHotbarAssignResultEvent(
            success, packet.ResultCode, packet.HotbarSlotEcho,
            new SkillId(unchecked((uint)packet.SkillIdEcho)), packet.SkillPointPool));
    }

    // -------------------------------------------------------------------------
    // 4/150 — skill-point / level update
    // -------------------------------------------------------------------------

    /// <summary>
    /// 4/150 — skill-point update (fixed 16-byte header). Mode 1 sets the total skill-point pool; mode 2
    /// is a level-up notice (Value = new level), which also updates the local actor's level. The 255
    /// display cap is UI-only; the wire value is not clamped. spec: Docs/RE/specs/handlers.md §13 Group F
    /// (4/150); Docs/RE/structs/skill.md.
    /// </summary>
    private void HandleSkillPointUpdate(in SmsgSkillPointUpdateHeader packet)
    {
        const byte valid = 1; // +0 must equal 1. spec: structs/skill.md (valid).
        if (packet.Valid != valid)
        {
            _unhandled.Record(Opcodes.SmsgSkillPointUpdate, SmsgSkillPointUpdateHeader.HeaderSize);
            return;
        }

        const uint levelUpMode = 2; // mode 2 = level-up notice; Value = new level. spec: structs/skill.md (mode).
        if (packet.Mode == levelUpMode
            && _world.LocalActor is { } local
            && packet.Value <= ushort.MaxValue)
        {
            local.SetLevel((ushort)packet.Value);
            RecomputeCombatStats(); // level changed -> recompose. spec: combat.md §2.
        }

        _eventBus.Publish(new SkillPointUpdateEvent(packet.Mode, packet.Value));
    }

    // -------------------------------------------------------------------------
    // 5/31 — buff/status slot update
    // -------------------------------------------------------------------------

    /// <summary>
    /// 5/31 — buff/status slot update. Writes the 12-byte status entry into the per-actor buff table; for
    /// the local player it mirrors into <see cref="LocalPlayerState.Buffs"/> and recomputes combat stats
    /// (a buff is a stat contribution). Only the per-actor table regime (slot 0..30) mutates Domain;
    /// larger slot regimes are surfaced as events only. spec: Docs/RE/specs/handlers.md §4 (5/31);
    /// Docs/RE/specs/skills.md §6.1.
    /// </summary>
    private bool HandleBuffSlotUpdate(ReadOnlySpan<byte> payload)
    {
        const int minSize = 56; // Min fixed payload 56 (0x38). spec: handlers.md §4 (5/31).
        if (payload.Length < minSize)
        {
            return false;
        }

        // (sort@+0, id@+4) actor key; slot@+8; effect code@+12; value/duration@+16; extra/param@+20.
        // spec: handlers.md §4 (5/31 fields).
        byte sort = payload[0x00];
        uint actorId = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(0x04, 4));
        uint slot = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(0x08, 4));
        uint effectCode = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(0x0C, 4));
        uint duration = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(0x10, 4));
        uint extra = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(0x14, 4));

        var key = new ActorKey(actorId, ToEntitySort(sort));

        // Per-actor table regime: small slots (0..30) write the local mirror. The >30 / >=1,000,000 regimes
        // are global / per-actor-only and not modelled in this 31-slot local table. spec: skills.md §6.1.
        bool isLocal = _world.LocalActorKey == key;
        if (isLocal && _localPlayer is not null && slot < (uint)BuffTable.SlotCount)
        {
            // Magnitude (the parallel secondary-table strength) is not in this 56-byte read; default 0.
            // spec: skills.md §6.1 (secondary magnitude table separate).
            _localPlayer.Buffs.Apply(
                (int)slot, unchecked((int)effectCode), unchecked((int)duration), unchecked((int)extra), magnitude: 0);
            RecomputeCombatStats(); // a buff changed -> recompose. spec: combat.md §2.2.
        }

        _eventBus.Publish(new BuffSlotChangedEvent(
            key, unchecked((int)slot), unchecked((int)effectCode), unchecked((int)duration), unchecked((int)extra)));
        return true;
    }

    // -------------------------------------------------------------------------
    // 5/67 — world-entry stat sync
    // -------------------------------------------------------------------------

    /// <summary>
    /// 5/67 — world-entry stat sync. Writes the neutral stat slots and current XP onto the actor's level
    /// (XP only) and publishes the snapshot. The neutral slot numbering (stat0/2/4/5/6) is preserved
    /// pending a named-stat mapping; no game-rule remap happens here. spec: Docs/RE/specs/handlers.md §4
    /// (5/67).
    /// </summary>
    private bool HandleStatsUpdate(ReadOnlySpan<byte> payload)
    {
        const int minSize = 36; // Min fixed payload 36 (0x24). spec: handlers.md §4 (5/67).
        if (payload.Length < minSize)
        {
            return false;
        }

        // sort@+0; id@+4; stat0@+8; stat2@+12; current-XP i64@+16; stat6@+24; stat4@+28; stat5@+32.
        // spec: handlers.md §4 (5/67 fields).
        byte sort = payload[0x00];
        uint actorId = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(0x04, 4));
        uint stat0 = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(0x08, 4));
        uint stat2 = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(0x0C, 4));
        long currentXp = BinaryPrimitives.ReadInt64LittleEndian(payload.Slice(0x10, 8));
        uint stat6 = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(0x18, 4));
        uint stat4 = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(0x1C, 4));
        uint stat5 = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(0x20, 4));

        var key = new ActorKey(actorId, ToEntitySort(sort));

        _eventBus.Publish(new ActorStatSyncEvent(key, stat0, stat2, stat4, stat5, stat6, currentXp));
        return true;
    }

    // -------------------------------------------------------------------------
    // 5/9 — experience gain
    // -------------------------------------------------------------------------

    /// <summary>
    /// 5/9 — experience gain. Adds the 64-bit amount to BOTH the current-XP and lifetime-XP accumulators
    /// (add-with-carry) for the local player, then fires the XP-bar refresh seam. The §3.1 display split
    /// (base/bonus, gated on the source-mode low byte == 2) is a presentation-only transform applied with
    /// the server-set bonus rate — it does not change what accumulates; the rate is injected DATA
    /// (capture-pending per §12 Q6), so it is 0 (no bonus) unless the composition root supplies it.
    /// The two trailing proficiency/mastery slots (+24/+28) are not progression state (a separate
    /// stat-channel writer per §3.3) and are out of scope here.
    /// spec: Docs/RE/specs/progression.md §3 / §3.1 / §3.4 / §11.
    /// </summary>
    private bool HandleExpGain(ReadOnlySpan<byte> payload)
    {
        const int minSize = 32; // 5/9 payload is 32 bytes. spec: progression.md §3.4.
        if (payload.Length < minSize)
        {
            return false;
        }

        // sort@+0; id@+4; source-sort@+8 (low byte == 2 enables the §3.1 split); src-id@+12; amount i64@+16.
        // spec: progression.md §3.4.
        byte sort = payload[0x00];
        uint actorId = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(0x04, 4));
        uint sourceSort = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(0x08, 4));
        long amount = BinaryPrimitives.ReadInt64LittleEndian(payload.Slice(0x10, 8));

        // Progression is local-player state only; ignore XP gain reported for any other actor.
        // spec: progression.md §1 (all five S2C channels gated to the local player).
        var key = new ActorKey(actorId, ToEntitySort(sort));
        if (_world.LocalActorKey is { } localKey && localKey != key)
        {
            return true; // decoded and consumed; just not the local player.
        }

        Progression = Progression.AddExperience(amount); // spec: progression.md §3 (add to both accumulators).

        // §3.1 display split — the floating "<base> + <bonus>" text only fires when source-mode == 2.
        // The split value is informational; the FULL amount already accumulated above. spec: progression.md §3.1.
        if ((byte)sourceSort == 2)
        {
            long ratePercent = XpBonusRatePercentResolver?.Invoke() ?? 0L; // server DATA; 0 = no bonus. spec: §12 Q6.
            _ = ExperienceModel.SplitBonus(amount, ratePercent);
        }

        ProgressionRefresh?.Invoke(Progression); // refresh the XP bar. spec: progression.md §3.
        return true;
    }

    // -------------------------------------------------------------------------
    // 5/11 — rank / honor XP gain
    // -------------------------------------------------------------------------

    /// <summary>
    /// 5/11 — rank/honor XP gain. A separate progression channel (no HP/MP/level math): routes the amount
    /// through the Domain rank-XP model for the local player — mode 2 adds directly to the rank accumulator,
    /// any other mode runs the §4 per-level table routine (capped at 25) keyed by the local-player level
    /// cache. The per-level divisor/cap tables are server/config DATA (capture-pending per §12 Q6), injected
    /// empty so nothing is invented; a 0 divisor surfaces the "leveltable error" diagnostic.
    /// spec: Docs/RE/specs/progression.md §4 / §4.1 / §11.
    /// </summary>
    private bool HandleRankXpGain(ReadOnlySpan<byte> payload)
    {
        const int minSize = 20; // 5/11 payload is 20 bytes. spec: progression.md §4.1.
        if (payload.Length < minSize)
        {
            return false;
        }

        // id@+0; sort@+4; amount u64@+8; mode u8@+16 (2 = direct add). spec: progression.md §4.1.
        uint actorId = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(0x00, 4));
        byte sort = payload[0x04];
        long amount = unchecked((long)BinaryPrimitives.ReadUInt64LittleEndian(payload.Slice(0x08, 8)));
        byte mode = payload[0x10];

        // Local-player only. spec: progression.md §1 / §4 (applied to the local player only).
        var key = new ActorKey(actorId, ToEntitySort(sort));
        if (_world.LocalActorKey is { } localKey && localKey != key)
        {
            return true;
        }

        // The §4 table index / cap special-case is the local-player level cache. spec: progression.md §4.
        int levelCache = _world.LocalActor?.Level ?? 0;

        try
        {
            Progression = Progression.AddRankXp(amount, mode, levelCache, RankXpDivisorTable, RankXpCapTable);
        }
        catch (LevelTableException ex)
        {
            // "leveltable error" — a 0 divisor for the active level. Log and leave state unchanged.
            // spec: progression.md §4.
            LevelTableErrorSink?.Invoke(ex.LevelIndex);
            return true;
        }

        ProgressionRefresh?.Invoke(Progression); // refresh the rank bar. spec: progression.md §4.
        return true;
    }

    // -------------------------------------------------------------------------
    // 4/100 — combat attack / charge update
    // -------------------------------------------------------------------------

    /// <summary>
    /// 4/100 — combat-attack / charge UI state update. Decodes only the documented phase/sub-kind/value
    /// fields (the remaining ~176 bytes are opaque per the spec and not surfaced). Phase 3 starts a timed
    /// charge; phase 5 ends it. spec: Docs/RE/specs/handlers.md §3 (4/100).
    /// </summary>
    private bool HandleCombatAttackUpdate(ReadOnlySpan<byte> payload)
    {
        const int minSize = 188; // Min fixed payload 188 (0xBC). spec: handlers.md §3 (4/100).
        if (payload.Length < minSize)
        {
            return false;
        }

        // phase@+8 (u8); sub-kind@+10 (i8, 0xFF = reset); value@+12 (u32). spec: handlers.md §3 (4/100).
        byte phase = payload[0x08];
        sbyte subKind = unchecked((sbyte)payload[0x0A]);
        uint value = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(0x0C, 4));

        const byte startCharge = 3; // phase 3 starts a timed charge. spec: handlers.md §3 (4/100).
        const byte endCharge = 5; // phase 5 ends it. spec: handlers.md §3 (4/100).

        _eventBus.Publish(new CombatAttackUpdateEvent(
            phase, subKind, value, phase == startCharge, phase == endCharge));
        return true;
    }

    // -------------------------------------------------------------------------
    // 5/7 — chat broadcast
    // -------------------------------------------------------------------------

    /// <summary>
    /// 5/7 — server chat broadcast. Decodes the 36-byte header struct, then the variable text body that
    /// follows it. The body length encoding is unconfirmed; we read a length-prefixed block when one is
    /// present and otherwise treat the remainder as the text (decoding the leading printable run).
    /// CP949 -&gt; managed string at this presentation boundary. spec:
    /// Docs/RE/packets/5-7_chat_broadcast.yaml; Docs/RE/specs/handlers.md §17.12.
    /// </summary>
    private bool HandleChatBroadcast(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < SmsgChatBroadcastHeader.HeaderSize)
        {
            return false;
        }

        ref readonly SmsgChatBroadcastHeader header =
            ref MemoryMarshal.AsRef<SmsgChatBroadcastHeader>(payload);

        string senderName = DecodeFixedText(header.SenderName);
        ReadOnlySpan<byte> body = payload[SmsgChatBroadcastHeader.HeaderSize..];
        string text = DecodeChatBody(body);

        var key = new ActorKey(header.SenderId, ToEntitySort(header.SenderSort));
        _eventBus.Publish(new ChatBroadcastEvent(
            key, senderName, header.Channel, header.ContextId, text));
        return true;
    }

    // -------------------------------------------------------------------------
    // 3/1 — character-select list
    // -------------------------------------------------------------------------

    /// <summary>
    /// 3/1 — character-select list. Decodes the 3-byte header, then one 981-byte per-slot record for each
    /// set bit in the slot mask (LSB-first, exactly 5 slots (indices 0..4)), pulling the name/level/class/HP out of each
    /// record's embedded 880-byte SpawnDescriptor. Forces a Select re-entry and emits the list snapshot.
    /// spec: Docs/RE/packets/3-1_character_list.yaml; Docs/RE/specs/handlers.md §2 / §17.1.
    /// </summary>
    private bool HandleCharacterList(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < SmsgCharacterListHeader.HeaderSize)
        {
            return false;
        }

        // 3/1 is a char-mgmt result handler: clear the single in-flight latch.
        // spec: Docs/RE/specs/net_contracts.md §1.3 (CLEARED by 3/1).
        _inFlightLatch?.Clear();

        ref readonly SmsgCharacterListHeader header =
            ref MemoryMarshal.AsRef<SmsgCharacterListHeader>(payload);

        // A fresh list replaces the prior roster (and any stale chosen-slot cache). spec: login_flow.md §3.2.
        _characterSelection?.Reset();

        // 3/1 and 3/4 both reach this same 3 + N×981 roster decode. spec: login_flow.md §1 step 7 / §5.1.
        System.Collections.Immutable.ImmutableArray<CharacterListSlot> slots =
            DecodeAndRetainRoster(in header, payload);

        // 3/1 CharacterList FORCES a Select (state 4) re-entry, accepted from Load/Select. This is the
        // 3/1-only behaviour; 3/4 does NOT force a scene change. spec: client_runtime.md §7.5.2; login_flow.md §1 step 7.
        _sceneStateMachine?.OnCharacterListReceived();

        _eventBus.Publish(new CharacterListEvent(header.ServerId, header.ChannelId, slots));
        return true;
    }

    /// <summary>
    /// Shared roster decode for the 3+N×981 character list, reached by BOTH <c>3/1 SmsgCharacterList</c>
    /// and <c>3/4 SmsgSceneEntityUpdate</c> (the in-place refill). Walks the slot mask over exactly 5
    /// slots (indices 0..4), decodes each set slot's embedded 880-byte SpawnDescriptor into a
    /// <see cref="CharacterListSlot"/>, and retains each RAW per-slot record into the
    /// <see cref="_characterSelection"/> store. The caller owns the <c>Reset()</c>, the scene transition
    /// (3/1 only), and the <see cref="CharacterListEvent"/> publish. spec: Docs/RE/specs/login_flow.md
    /// §1 step 7 / §5.1 / §3.2; Docs/RE/packets/3-1_character_list.yaml; Docs/RE/structs/spawn_descriptor.md.
    /// </summary>
    private System.Collections.Immutable.ImmutableArray<CharacterListSlot> DecodeAndRetainRoster(
        in SmsgCharacterListHeader header, ReadOnlySpan<byte> payload)
    {
        var builder = System.Collections.Immutable.ImmutableArray.CreateBuilder<CharacterListSlot>();
        int cursor = SmsgCharacterListHeader.HeaderSize;

        // Hard, bounded iteration of exactly 5 slots (indices 0..4); the list never references a slot
        // beyond 4. spec: login_flow.md §3.2 / §7 (Char-list maximum slots = 5).
        for (int slot = 0; slot < CharacterSelectionStore.MaxSlots; slot++)
        {
            if ((header.SlotMask & (1 << slot)) == 0)
            {
                continue; // bit clear -> no record for this slot. spec: 3-1.
            }

            // Each set bit consumes one 981-byte record = 880 descriptor + 96 stats + 1 flag + 4 timestamp.
            // spec: 3-1_character_list.yaml (SlotRecordSize). A short/truncated frame ends the loop.
            if (cursor + SmsgCharacterListHeader.SlotRecordSize > payload.Length)
            {
                break;
            }

            ReadOnlySpan<byte> record = payload.Slice(cursor, SmsgCharacterListHeader.SlotRecordSize);
            cursor += SmsgCharacterListHeader.SlotRecordSize;

            // The descriptor is the first 880 bytes of the record. spec: 3-1 / spawn_descriptor.md.
            var reader = new SpawnDescriptorReader(record[..SpawnDescriptorReader.Size]);
            // CONFLICT (committed-spec disagreement, debugger-pending): frontend_scenes.md §3.2 lists the
            // select-row position at +0xA0/+0xA4; structs/actor.md (this reader's source) pins
            // world_x/world_z at +0x4C/+0x50. Reusing the actor.md offsets for decode consistency; the
            // §3.2 offset is unconfirmed. The X/Z (vs X/Y) axis pairing is itself flagged debugger-pending
            // in §3.2. spec: Docs/RE/structs/actor.md / frontend_scenes.md §3.2
            builder.Add(new CharacterListSlot(
                slot, reader.ReadName(), reader.ReadLevel(), reader.ReadServerClass(), reader.ReadCurrentHp(),
                reader.ReadWorldX(), reader.ReadWorldZ()));

            // Retain the RAW per-slot record (880 descriptor + 96 stats + 1 flag byte) so SelectCharacterAsync
            // can detect "@BLANK@", and the 3/14 handler can materialize the local player. spec: login_flow.md §3.5.
            // The 880 + 96 = 976-byte descriptor+stats span; the flag byte is at record +976. spec: §3.2.
            const int descriptorAndStatsSize = SpawnDescriptorReader.Size + 96; // 976
            byte slotFlag = record.Length > descriptorAndStatsSize ? record[descriptorAndStatsSize] : (byte)0;
            _characterSelection?.Retain(
                new CharacterSlotRecord(slot, record[..descriptorAndStatsSize], slotFlag));
        }

        return builder.ToImmutable();
    }

    // -------------------------------------------------------------------------
    // 3/4 — scene-entity update / in-place roster refill (gated by form_byte0 == 1)
    // -------------------------------------------------------------------------

    /// <summary>
    /// 3/4 — the in-place character-roster refill. The 3-byte header
    /// <c>[form_byte0][channel_byte1][slot_mask_byte2]</c> is byte-identical to the 3/1 header, so it is
    /// read through <see cref="SmsgCharacterListHeader"/>; <c>form_byte0</c> is the header's first byte.
    /// Per spec, 3/4 decodes the roster ONLY when <c>form_byte0 == 1</c> (the in-place refill form);
    /// other forms are consumed as a no-op. Unlike 3/1, 3/4 does NOT force a scene change. On the refill
    /// form it resets the selection store, runs the shared 3+N×981 roster decode, and republishes the
    /// <see cref="CharacterListEvent"/> so the char-select screen repopulates in place. spec:
    /// Docs/RE/specs/login_flow.md §5.1, §1 step 7.
    /// </summary>
    private bool HandleSceneEntityUpdate(ReadOnlySpan<byte> payload)
    {
        // The 3/4 header is byte-identical to the 3/1 header (form, channel, slot mask). spec: §1 step 7 / §5.1.
        if (payload.Length < SmsgCharacterListHeader.HeaderSize)
        {
            return false;
        }

        // 3/4 is a char-mgmt result handler: clear the single in-flight latch.
        // spec: Docs/RE/specs/net_contracts.md §1.3 (CLEARED by 3/4).
        _inFlightLatch?.Clear();

        ref readonly SmsgCharacterListHeader header =
            ref MemoryMarshal.AsRef<SmsgCharacterListHeader>(payload);

        // GATE: form_byte0 is the header's first byte. 3/4 is the in-place refill gated on form_byte0 == 1;
        // any other form is consumed as a no-op refill. spec: Docs/RE/specs/login_flow.md §5.1, §1 step 7.
        const byte refillForm = 1;
        if (header.ServerId != refillForm)
        {
            return true; // consumed; non-form-1 is a no-op refill (no scene change, no decode). spec: §1 step 7.
        }

        // In-place refill: replace the prior roster, run the SAME roster decode 3/1 uses, and republish so
        // the char-select screen repopulates. NO forced scene change (3/4 != 3/1). spec: §5.1, §1 step 7.
        _characterSelection?.Reset();
        System.Collections.Immutable.ImmutableArray<CharacterListSlot> slots =
            DecodeAndRetainRoster(in header, payload);
        _eventBus.Publish(new CharacterListEvent(header.ServerId, header.ChannelId, slots));
        return true;
    }

    // -------------------------------------------------------------------------
    // 3/14 — char-spawn result (the actual local-player spawn)
    // -------------------------------------------------------------------------

    /// <summary>
    /// 3/14 — enter-game spawn result. On Result != 0 the client materializes the local player from the
    /// slot descriptor cached at select time (Section 3.5) and publishes <see cref="LocalPlayerSpawnedEvent"/>;
    /// on Result == 0 it publishes <see cref="LocalPlayerSpawnFailedEvent"/> (a timed failure message).
    /// The local player is registered as the controlled actor (<see cref="ClientWorld.LocalActorKey"/>),
    /// so the move/skill use cases can source its position. spec: Docs/RE/specs/login_flow.md §3.5 / §5.3.
    /// </summary>
    public void Handle(in SmsgCharSpawnResult packet)
    {
        // 3/14 is a char-mgmt result handler: clear the single in-flight latch.
        // spec: Docs/RE/specs/net_contracts.md §1.3 (CLEARED by 3/14).
        _inFlightLatch?.Clear();

        // Result 0 = failure (a timed message is shown). spec: login_flow.md §5.3.
        if (packet.Result == 0)
        {
            _eventBus.Publish(new LocalPlayerSpawnFailedEvent(packet.Slot));
            return;
        }

        // Success: materialize the local player from the CACHED slot descriptor (Section 3.5). Without a
        // cache (no store wired, or no slot confirmed) there is nothing to spawn from; record and bail.
        CharacterSlotRecord? cached = _characterSelection?.Chosen;
        if (cached is null)
        {
            _unhandled.Record(Opcodes.SmsgCharSpawnResult, SmsgCharSpawnResult.WireSize);
            return;
        }

        // The local player's actor id is not carried by the 16-byte 3/14 block (only result + slot + 3
        // opaque spawn-param u32s; their meaning is UNVERIFIED — spec §5.3). Key the local player on the
        // PlayerCharacter sort with the unassigned-id sentinel until a self-spawn (5/3) supplies the real
        // id. spec: Docs/RE/structs/actor.md (id initialised to 0xFFFFFFFF before spawn).
        var key = new ActorKey(ActorKey.UnassignedRawId, EntitySort.PlayerCharacter);

        // Float -> fixed at the boundary; world Y forced to 0. spec: actor.md (coords float, Y = 0).
        Vector3Fixed position = Vector3Fixed.FromFloat(cached.WorldX, 0f, cached.WorldZ);

        var spawnInfo = new SpawnInfo(
            key, cached.Level, cached.CurrentHp, cached.CurrentMp, cached.CurrentStamina, cached.ServerClass);
        VitalStats vitals = VitalsResolver(spawnInfo);

        var actor = new Actor(
            key, cached.Level, vitals, cached.CurrentHp, cached.CurrentMp, cached.CurrentStamina, position);
        _world.Add(actor);
        _world.LocalActorKey = key; // mark the controlled actor for the move/skill use cases.

        _eventBus.Publish(new LocalPlayerSpawnedEvent(
            key, packet.Slot, cached.Name, cached.Level, actor.Position, actor.CurrentHp, actor.MaxHp,
            cached.ServerClass));
    }

    /// <summary>
    /// Creates the local player for the 4/1 world-entry form from the descriptor cached during
    /// character select, overriding descriptor X/Z with the 4/1 spawn seed. spec:
    /// Docs/RE/opcodes.md (4/1 is the local-player world spawn); Docs/RE/specs/client_runtime.md
    /// §9.1 steps 5–7; Docs/RE/structs/actor.md (Y forced to 0 and local-player pointer is side state).
    /// </summary>
    private bool TryCreateLocalPlayerFromCachedDescriptor(
        Vector3Fixed spawnPosition,
        out Actor? actor,
        out int slotIndex,
        out string name,
        out ushort serverClass)
    {
        actor = null;
        slotIndex = -1;
        name = string.Empty;
        serverClass = 0;

        CharacterSlotRecord? cached = _characterSelection?.Chosen;
        if (cached is null || cached.RawDescriptor.Length < SpawnDescriptorReader.Size)
        {
            return false;
        }

        var key = new ActorKey(ActorKey.UnassignedRawId, EntitySort.PlayerCharacter);
        var reader = new SpawnDescriptorReader(cached.RawDescriptor.Span[..SpawnDescriptorReader.Size]);
        ushort level = reader.ReadLevel();
        uint currentHp = reader.ReadCurrentHp();
        uint currentMp = reader.ReadCurrentMp();
        uint currentStamina = reader.ReadCurrentStamina();
        serverClass = reader.ReadServerClass();

        var spawnInfo = new SpawnInfo(key, level, currentHp, currentMp, currentStamina, serverClass);
        VitalStats vitals = VitalsResolver(spawnInfo);

        actor = new Actor(key, level, vitals, currentHp, currentMp, currentStamina, spawnPosition);
        _world.Add(actor);
        _world.LocalActorKey = key;
        slotIndex = cached.SlotIndex;
        name = cached.Name;
        return true;
    }

    // -------------------------------------------------------------------------
    // 3/7 — char manage / delete result (8-byte block)
    // -------------------------------------------------------------------------

    /// <summary>
    /// 3/7 — character manage / delete result. Classifies the subtype (subtype 2 = delete-confirm,
    /// which decrements the account char count) and forwards the ReadyTime so the presentation can
    /// format a "wait HH:MM" delete-cooldown message on the blocked path. spec:
    /// Docs/RE/specs/login_flow.md §5.5; Docs/RE/opcodes.md (3/7 SmsgCharManageResult).
    /// </summary>
    public void Handle(in SmsgCharManageResult packet)
    {
        // 3/7 is a char-mgmt result handler: clear the single in-flight latch.
        // spec: Docs/RE/specs/net_contracts.md §1.3 (CLEARED by 3/7).
        _inFlightLatch?.Clear();

        const byte success = 1; // result 1 = success path. spec: §5.5.
        const byte deleteConfirmSubtype = 2; // subtype 2 = delete-confirm. spec: §5.5.
        bool ok = packet.Result == success;

        CharManageSubtype subtype = packet.Subtype switch
        {
            0 => CharManageSubtype.GenericRefresh, // spec: §5.5 (semantics UNVERIFIED)
            1 => CharManageSubtype.RenameApplied, // spec: §5.5 (semantics UNVERIFIED)
            2 => CharManageSubtype.DeleteConfirm, // spec: §5.5 (delete-confirm)
            _ => CharManageSubtype.Other,
        };

        // A successful delete-confirm decrements the account char count. spec: §5.5.
        int charCount = _accountCharacters?.CharacterCount ?? 0;
        if (ok && packet.Subtype == deleteConfirmSubtype && _accountCharacters is not null)
        {
            charCount = _accountCharacters.Decrement();
        }

        // 3/7 writes NO scene state — the table-driven transition is 3/100 (HandleCharActionResult).
        // spec: Docs/RE/specs/client_runtime.md §7.5.2; Docs/RE/opcodes.md (3/7 vs 3/100).
        _eventBus.Publish(new CharManageResultEvent(
            ok, subtype, packet.Subtype, packet.ReadyTime, charCount));
    }

    // -------------------------------------------------------------------------
    // 3/6 — rename-character result
    // -------------------------------------------------------------------------

    /// <summary>
    /// 3/6 — rename-character result. A 12-byte block: result code, error code, padding, slot index, and an unverified dword.
    /// spec: Docs/RE/packets/3-6_rename_char_result.yaml.
    /// </summary>
    public void Handle(in SmsgRenameCharResult packet)
    {
        // 3/6 is a char-mgmt result handler: clear the single in-flight latch.
        // spec: Docs/RE/specs/net_contracts.md §1.3 (CLEARED by 3/6).
        _inFlightLatch?.Clear();

        bool ok = packet.Result != 0;

        if (ok)
        {
            // The packet doesn't carry the name; the client updates via 3/4 refresh
            _eventBus.Publish(new CharRenameResultEvent(true, string.Empty, ErrorCode: 0));
            return;
        }

        // Failure: ErrorCode maps to a UI error string by the presentation.
        _eventBus.Publish(new CharRenameResultEvent(false, string.Empty, packet.ErrorCode));
    }

    // -------------------------------------------------------------------------
    // 3/23 — character-create result
    // -------------------------------------------------------------------------

    /// <summary>
    /// 3/23 — character-create result. Pairs with the CreateCharacterRequestedEvent the select use-case
    /// emits for a blank slot. On success the Code byte is the assigned slot id and the account char
    /// count is incremented; on failure Code is an error code (0xC8..0xD4). spec:
    /// Docs/RE/specs/login_flow.md §5.4; Docs/RE/packets/SmsgCharCreateResult.
    /// </summary>
    public void Handle(in SmsgCharCreateResult packet)
    {
        const byte success = 1; // result 1 = success. spec: §5.4.
        bool ok = packet.Result == success;

        // On success the account char count is incremented. spec: §5.4.
        int charCount = _accountCharacters?.CharacterCount ?? 0;
        if (ok && _accountCharacters is not null)
        {
            charCount = _accountCharacters.Increment();
        }

        // Code is the assigned slot id on success, or the error code on failure. spec: §5.4.
        byte assignedSlotId = ok ? packet.Code : (byte)0;
        byte errorCode = ok ? (byte)0 : packet.Code;

        _eventBus.Publish(new CharCreateResultEvent(
            ok, assignedSlotId, errorCode, packet.Value1, packet.Value2, charCount));
    }

    /// <summary>
    /// 3/100 — generic character-management action/result code. Feeds the Campaign-15 scene spine with
    /// the exact result-code table (0, 1..4/7, 202/203/232, out-of-range). spec:
    /// Docs/RE/opcodes.md; Docs/RE/specs/client_runtime.md §7.5.2.
    /// </summary>
    public void Handle(in SmsgCharActionResult packet)
    {
        int result = packet.Result > int.MaxValue ? int.MaxValue : (int)packet.Result;
        _sceneStateMachine?.OnCharActionResult(result, _world.LocalActor is not null);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Re-accumulates the local player's derived combat-stat aggregate via the injected
    /// <see cref="CombatStatsRecompute"/> seam, stores it on <see cref="LocalPlayerState"/>, and emits a
    /// <see cref="CombatStatsRecomputedEvent"/>. No-op when the recompute seam or local-player state is
    /// absent. spec: Docs/RE/specs/combat.md §1 / §2.
    /// </summary>
    private void RecomputeCombatStats()
    {
        if (CombatStatsRecompute is null || _localPlayer is null || _world.LocalActorKey is not { } key)
        {
            return;
        }

        CombatStats recomputed = CombatStatsRecompute(_localPlayer.Combat);
        _localPlayer.Combat = recomputed;
        _eventBus.Publish(new CombatStatsRecomputedEvent(key, recomputed));
    }

    /// <summary>
    /// Decodes a NUL-terminated CP949 fixed buffer to a managed string. Routed through
    /// <see cref="Cp949Text.Decode"/>, the single site that registers
    /// <c>CodePagesEncodingProvider.Instance</c> (code page 949 is not built into .NET) and trims at the
    /// first NUL. spec: handlers.md (Korean text fields are CP949-encoded); CLAUDE.md (register the
    /// code-pages provider once).
    /// </summary>
    private static string DecodeFixedText(ReadOnlySpan<byte> buffer) => Cp949Text.Decode(buffer);

    /// <summary>
    /// Decodes the variable chat-body region: a leading length-prefixed <c>[u32 len][text]</c> block when
    /// the prefix is plausible, else the printable run from the start of the body. spec:
    /// Docs/RE/specs/handlers.md §17.12 (body length encoding unconfirmed).
    /// </summary>
    private static string DecodeChatBody(ReadOnlySpan<byte> body)
    {
        if (body.IsEmpty)
        {
            return string.Empty;
        }

        // Try the length-prefixed form (matching the C2S chat senders): [u32 len incl NUL][text].
        // spec: handlers.md §17.12 / 2-7 / 3-21 framing.
        if (body.Length >= sizeof(uint))
        {
            uint len = BinaryPrimitives.ReadUInt32LittleEndian(body[..sizeof(uint)]);
            if (len >= 1 && len <= (uint)(body.Length - sizeof(uint)))
            {
                // Cp949Text.Decode trims at the first NUL and decodes via the registered code page 949.
                return Cp949Text.Decode(body.Slice(sizeof(uint), (int)len));
            }
        }

        // Fall back to the printable run from the body start.
        return DecodeFixedText(body);
    }

    private static EntitySort ToEntitySort(byte sort) => sort switch
    {
        1 => EntitySort.PlayerCharacter, // spec: actor.md sort == 1
        2 => EntitySort.Monster, // spec: actor.md sort == 2
        3 => EntitySort.NonPlayerCharacter, // spec: actor.md sort == 3
        _ => EntitySort.None,
    };

    /// <summary>
    /// Resolves vital capacities for a freshly-spawned actor via the recovered Domain formula
    /// (<see cref="VitalStats.FromFormula"/>). spec: Docs/RE/structs/stats.md.
    /// </summary>
    /// <remarks>
    /// PROVISIONAL: the spawn packet carries current HP/MP/stamina but not the primary stats or the
    /// external level/server bases the formula needs, so we feed what we have (class id from the
    /// spawn's server class; stats/bases left at their provisional 0 defaults). The resulting HP/MP
    /// maxima are structurally-correct but numerically-provisional until catalog/server data exists
    /// (spec: stats.md "External inputs (UNVERIFIED)"). Compose a richer resolver from above when the
    /// spawn's primary stats / equipment are decoded.
    /// <para>
    /// <b>Server-authoritative current-value guard.</b> The server already enforced the HP/MP/stamina
    /// cap before sending current values (spec: stats.md "the server enforces the cap"). Because the
    /// provisional zero-base formula yields unrealistically small maxima, the computed max is raised to
    /// at least the reported current value so the server-authoritative current HP/MP is not clamped
    /// away. Stamina has no growth curve in stats.md, so its max is the reported current. This guard is
    /// PROVISIONAL and removed once real bases/stats feed the formula.
    /// </para>
    /// </remarks>
    private static VitalStats DefaultVitalsResolver(SpawnInfo info)
    {
        var inputs = VitalFormulaInputs.Empty with
        {
            // ClassId indexes the per-class HP table; mapping UNVERIFIED (spec: stats.md). The server
            // class is a u16 here; the table is byte-indexed, so take the low byte. PROVISIONAL.
            ClassId = unchecked((byte)info.ServerClass),
            // EquipmentHpFlat/MpFlat, set bonuses, level/server bases left at 0 (provisional).
        };

        VitalStats formula = VitalStats.FromFormula(in inputs, maxStamina: info.CurrentStamina);

        // Provisional guard: never clamp the server-authoritative current values below what was sent.
        return new VitalStats(
            Math.Max(formula.MaxHp, info.CurrentHp),
            Math.Max(formula.MaxMp, info.CurrentMp),
            Math.Max(formula.MaxStamina, info.CurrentStamina));
    }
}

/// <summary>
/// Immutable inputs available when resolving a freshly-spawned actor's vital capacities.
/// </summary>
public readonly record struct SpawnInfo(
    ActorKey Key,
    ushort Level,
    uint CurrentHp,
    uint CurrentMp,
    uint CurrentStamina,
    ushort ServerClass);