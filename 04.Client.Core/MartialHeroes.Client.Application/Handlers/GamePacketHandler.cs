using System.Buffers.Binary;
using System.Runtime.InteropServices;
using MartialHeroes.Client.Application.Contracts.Events;
using MartialHeroes.Client.Application.Contracts.Hud;
using MartialHeroes.Client.Application.Contracts.Scene;
using MartialHeroes.Client.Application.Diagnostics;
using MartialHeroes.Client.Application.Login;
using MartialHeroes.Client.Application.Net;
using MartialHeroes.Client.Application.UseCases;
using MartialHeroes.Client.Application.World;
using MartialHeroes.Client.Domain.Actors.Actors;
using MartialHeroes.Client.Domain.Progression.Progression;
using MartialHeroes.Client.Domain.Stats.Stats;
using MartialHeroes.Network.Protocol.Core.Opcodes;
using MartialHeroes.Network.Protocol.Packets.World.Packets;
using MartialHeroes.Network.Protocol.Routing.Routing;
using MartialHeroes.Shared.Kernel.Ids;
using MartialHeroes.Shared.Kernel.Numerics;

namespace MartialHeroes.Client.Application.Handlers;

/// <summary>
///     The single inbound sink the <see cref="Network.Protocol.Routing.Routing.PacketRouter" /> dispatches into. Each
///     typed overload
///     validates the wire message, applies it to the Domain via the <see cref="ClientWorld" /> registry
///     and the <see cref="Actor" /> controlled mutators, then publishes an immutable UI event on the
///     outbound <see cref="IClientEventBus" />.
/// </summary>
/// <remarks>
///     <para>
///         <b>Float -&gt; fixed boundary.</b> The wire carries IEEE-754 <c>float</c> world coordinates
///         (spec: Docs/RE/structs/actor.md "Coordinate type"; packets carry XZ-plane floats). This handler
///         is the application/network boundary, so it converts to the deterministic
///         <see cref="Vector3Fixed" /> via <see cref="Vector3Fixed.FromFloat" /> here and nowhere deeper. World
///         Y is forced to 0 because the server never sends it (spec: same section).
///     </para>
///     <para>
///         <b>No game-rule math.</b> Handlers compute nothing; they translate wire fields to Domain method
///         calls. Damage/stat/leveling formulas live in Domain.
///     </para>
///     <para>
///         <b>Threading.</b> Invoked by the single network-reader logical owner; it mutates Domain and the
///         registry without locking. Events published are immutable snapshots, so the UI consumer never sees
///         torn Domain state.
///     </para>
/// </remarks>
public sealed partial class GamePacketHandler : IPacketHandler
{
    private readonly AccountCharacterState? _accountCharacters;
    private readonly CharacterSelectionStore? _characterSelection;
    private readonly IClientEventBus _eventBus;
    private readonly IHudEventHub? _hudEventHub;
    private readonly InFlightLatch? _inFlightLatch;
    private readonly LocalPlayerState? _localPlayer;
    private readonly ILoginHandshakeDriver? _loginDriver;
    private readonly SceneStateMachine? _sceneStateMachine;
    private readonly IUnhandledOpcodeSink _unhandled;
    private readonly ClientWorld _world;
    private readonly WorldEntryState? _worldEntry;

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

    /// <summary>
    ///     The combat-stat recompute seam: invoked whenever an equip / buff / level change should re-accumulate
    ///     the local player's derived combat-stat aggregate. The composition root supplies the resolver (it owns
    ///     the injected equipment / buff / server-base data the recompose needs); when absent, the recompose is
    ///     skipped. The default is a no-op that returns the current aggregate unchanged. spec:
    ///     Docs/RE/specs/combat.md §1 / §2 (re-accumulate on input change).
    /// </summary>
    public Func<CombatStats, CombatStats>? CombatStatsRecompute { get; init; }

    /// <summary>
    ///     Per-skill cooldown duration resolver (ms) used when the 5/33 hotbar overwrite arms a recast slot.
    ///     The Application/Assets layer owns the skills.scr catalogue lookup; when absent the duration is 0
    ///     (a ready slot). spec: Docs/RE/specs/skills.md §4 (duration = cooldown_centiseconds × 100).
    /// </summary>
    public Func<SkillId, int>? CooldownDurationResolver { get; init; }

    /// <summary>
    ///     The local player's progression aggregate — the experience accumulators and the rank/honor XP
    ///     channel the <c>5/9 ExpGain</c> and <c>5/11 RankXpGain</c> handlers advance. The Domain owns the
    ///     arithmetic (<see cref="ProgressionState" />); this handler holds the live state so the routing
    ///     has somewhere authoritative to apply it. spec: Docs/RE/specs/progression.md §3 / §4 / §11.
    /// </summary>
    public ProgressionState Progression { get; private set; }

    /// <summary>
    ///     The server-set XP percentage-bonus rate used by the <c>5/9</c> §3.1 display split. This is a
    ///     server-authored global, NOT a client constant (spec: progression.md §12 Q6); the composition root
    ///     supplies it once a capture pins the value. Defaults to 0 (no bonus) so nothing is invented.
    ///     spec: Docs/RE/specs/progression.md §3.1.
    /// </summary>
    public Func<long>? XpBonusRatePercentResolver { get; init; }

    /// <summary>
    ///     The per-level rank-XP <em>divisor</em> table for the <c>5/11</c> §4 routine, indexed by the
    ///     local-player level cache. Server/config DATA, not a client constant (spec: progression.md §12 Q6);
    ///     supplied by the composition root, defaulting to empty so the Domain never invents magnitudes.
    ///     A 0 divisor for the active level is the documented "leveltable error". spec: Docs/RE/specs/progression.md §4.
    /// </summary>
    public IReadOnlyList<long>? RankXpDivisorTable { get; init; }

    /// <summary>
    ///     The per-level rank-XP <em>cap</em> table for the <c>5/11</c> §4 routine (bounds the within-rank
    ///     remainder), indexed by the local-player level cache. Server/config DATA, supplied by the
    ///     composition root, defaulting to empty. spec: Docs/RE/specs/progression.md §4.
    /// </summary>
    public IReadOnlyList<long>? RankXpCapTable { get; init; }

    /// <summary>
    ///     Refresh seam fired after each <c>5/9</c>/<c>5/11</c> progression mutation, carrying the new
    ///     aggregate so the presentation can refresh the XP-bar / rank-bar (the streaming HUD gage is a
    ///     separate widget — spec: progression.md §12 Q3). Engine-free; the composition root wires the
    ///     renderer. When absent the mutation still applies (the state is observable via
    ///     <see cref="Progression" />). spec: Docs/RE/specs/progression.md §3 / §4 / §11.
    /// </summary>
    public Action<ProgressionState>? ProgressionRefresh { get; init; }

    /// <summary>
    ///     Diagnostics seam for the <c>5/11</c> "leveltable error" (a 0 divisor for the active level), so the
    ///     application can log it without crashing the update — mirroring the client diagnostic.
    ///     spec: Docs/RE/specs/progression.md §4.
    /// </summary>
    public Action<int>? LevelTableErrorSink { get; init; }

    /// <summary>
    ///     Resolves the vital capacities for a freshly-spawned actor from its wire-reported current HP.
    /// </summary>
    /// <remarks>
    ///     The SpawnDescriptor carries only <em>current</em> HP/MP/stamina; max HP/MP are not wire fields
    ///     (spec: Docs/RE/structs/actor.md "max_hp / max_mp are NOT stored as fields" — they are computed
    ///     from base stats + equipment). The real growth formula is not yet documented (the stat block at
    ///     descriptor +0xD4 is unmapped), so this seam is injected: the composition root supplies the
    ///     resolution, and Domain owns any actual formula. The default seeds capacity from the reported
    ///     current values so spawn HP is not clamped away — a transparent placeholder, not a game formula.
    /// </remarks>
    public Func<SpawnInfo, VitalStats> VitalsResolver { get; init; } = DefaultVitalsResolver;

    // -------------------------------------------------------------------------
    // Unhandled
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Opcodes the typed <see cref="Network.Protocol.Routing.Routing.PacketRouter" /> seam does not dispatch — the
    ///     variable-length S2C
    ///     messages whose handlers must read beyond their fixed header (chat text body, the 4/4 tag loop, the
    ///     5/52 target-record loop, per-field decoders) plus the login key exchange (0/0). We decode these
    ///     from the raw payload span here and drive the login handshake on 0/0. Anything else is counted via
    ///     the injected sink; never throws, never blocks. Fixed-size opcodes whose handler reads entirely
    ///     within their struct are routed by the generator to a typed <c>Handle(in T)</c> overload instead.
    ///     spec: Docs/RE/opcodes.md.
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
                if (HandleAreaEntitySnapshot(payload)) return;

                break;

            case Opcodes.SmsgActorSkillAction: // 5/52 — actor skill action (24B header + 36B target records)
                if (HandleActorSkillAction(payload)) return;

                break;

            case Opcodes.SmsgSkillPointUpdate: // 4/150 — skill-point / level update (fixed 16-byte header)
                if (payload.Length >= SmsgSkillPointUpdateHeader.HeaderSize)
                {
                    HandleSkillPointUpdate(in MemoryMarshal.AsRef<SmsgSkillPointUpdateHeader>(payload));
                    return;
                }

                break;

            case Opcodes.SmsgBuffSlotUpdate: // 5/31 — buff/status slot update
                if (HandleBuffSlotUpdate(payload)) return;

                break;

            case Opcodes.SmsgStatsUpdate: // 5/67 — world-entry stat sync
                if (HandleStatsUpdate(payload)) return;

                break;

            case Opcodes.SmsgExpGain: // 5/9 — experience gain (32-byte payload)
                if (HandleExpGain(payload)) return;

                break;

            case Opcodes.SmsgRankXpGain: // 5/11 — rank/honor XP gain (20-byte payload)
                if (HandleRankXpGain(payload)) return;

                break;

            case Opcodes.SmsgCombatAttackUpdate: // 4/100 — combat attack / charge update
                if (HandleCombatAttackUpdate(payload)) return;

                break;

            case Opcodes.SmsgChatBroadcast: // 5/7 — chat broadcast (36-byte header + text)
                if (HandleChatBroadcast(payload)) return;

                break;

            case Opcodes.SmsgCharacterList: // 3/1 — character-select list (3-byte header + per-slot records)
                if (HandleCharacterList(payload)) return;

                break;

            case Opcodes.SmsgSceneEntityUpdate: // 3/4 — in-place roster refill (same 3+N×981 decode as 3/1)
                if (HandleSceneEntityUpdate(payload)) return;

                break;
        }

        _unhandled.Record(packedOpcode, payload.Length);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Re-accumulates the local player's derived combat-stat aggregate via the injected
    ///     <see cref="CombatStatsRecompute" /> seam, stores it on <see cref="LocalPlayerState" />, and emits a
    ///     <see cref="CombatStatsRecomputedEvent" />. No-op when the recompute seam or local-player state is
    ///     absent. spec: Docs/RE/specs/combat.md §1 / §2.
    /// </summary>
    private void RecomputeCombatStats()
    {
        if (CombatStatsRecompute is null || _localPlayer is null || _world.LocalActorKey is not { } key) return;

        var recomputed = CombatStatsRecompute(_localPlayer.Combat);
        _localPlayer.Combat = recomputed;
        _eventBus.Publish(new CombatStatsRecomputedEvent(key, recomputed));
    }

    /// <summary>
    ///     Decodes a NUL-terminated CP949 fixed buffer to a managed string. Routed through
    ///     <see cref="Cp949Text.Decode" />, the single site that registers
    ///     <c>CodePagesEncodingProvider.Instance</c> (code page 949 is not built into .NET) and trims at the
    ///     first NUL. spec: handlers.md (Korean text fields are CP949-encoded); CLAUDE.md (register the
    ///     code-pages provider once).
    /// </summary>
    private static string DecodeFixedText(ReadOnlySpan<byte> buffer)
    {
        return Cp949Text.Decode(buffer);
    }

    /// <summary>
    ///     Decodes the variable chat-body region: a leading length-prefixed <c>[u32 len][text]</c> block when
    ///     the prefix is plausible, else the printable run from the start of the body. spec:
    ///     Docs/RE/specs/handlers.md §17.12 (body length encoding unconfirmed).
    /// </summary>
    private static string DecodeChatBody(ReadOnlySpan<byte> body)
    {
        if (body.IsEmpty) return string.Empty;

        // Try the length-prefixed form (matching the C2S chat senders): [u32 len incl NUL][text].
        // spec: handlers.md §17.12 / 2-7 / 3-21 framing.
        if (body.Length >= sizeof(uint))
        {
            var len = BinaryPrimitives.ReadUInt32LittleEndian(body[..sizeof(uint)]);
            if (len >= 1 && len <= (uint)(body.Length - sizeof(uint)))
                // Cp949Text.Decode trims at the first NUL and decodes via the registered code page 949.
                return Cp949Text.Decode(body.Slice(sizeof(uint), (int)len));
        }

        // Fall back to the printable run from the body start.
        return DecodeFixedText(body);
    }

    private static EntitySort ToEntitySort(byte sort)
    {
        return sort switch
        {
            1 => EntitySort.PlayerCharacter, // spec: actor.md sort == 1
            2 => EntitySort.Monster, // spec: actor.md sort == 2
            3 => EntitySort.NonPlayerCharacter, // spec: actor.md sort == 3
            _ => EntitySort.None
        };
    }

    /// <summary>
    ///     Resolves vital capacities for a freshly-spawned actor via the recovered Domain formula
    ///     (<see cref="VitalStats.FromFormula" />). spec: Docs/RE/structs/stats.md.
    /// </summary>
    /// <remarks>
    ///     PROVISIONAL: the spawn packet carries current HP/MP/stamina but not the primary stats or the
    ///     external level/server bases the formula needs, so we feed what we have (class id from the
    ///     spawn's server class; stats/bases left at their provisional 0 defaults). The resulting HP/MP
    ///     maxima are structurally-correct but numerically-provisional until catalog/server data exists
    ///     (spec: stats.md "External inputs (UNVERIFIED)"). Compose a richer resolver from above when the
    ///     spawn's primary stats / equipment are decoded.
    ///     <para>
    ///         <b>Server-authoritative current-value guard.</b> The server already enforced the HP/MP/stamina
    ///         cap before sending current values (spec: stats.md "the server enforces the cap"). Because the
    ///         provisional zero-base formula yields unrealistically small maxima, the computed max is raised to
    ///         at least the reported current value so the server-authoritative current HP/MP is not clamped
    ///         away. Stamina has no growth curve in stats.md, so its max is the reported current. This guard is
    ///         PROVISIONAL and removed once real bases/stats feed the formula.
    ///     </para>
    /// </remarks>
    private static VitalStats DefaultVitalsResolver(SpawnInfo info)
    {
        var inputs = VitalFormulaInputs.Empty with
        {
            // ClassId indexes the per-class HP table; mapping UNVERIFIED (spec: stats.md). The server
            // class is a u16 here; the table is byte-indexed, so take the low byte. PROVISIONAL.
            ClassId = unchecked((byte)info.ServerClass)
            // EquipmentHpFlat/MpFlat, set bonuses, level/server bases left at 0 (provisional).
        };

        var formula = VitalStats.FromFormula(in inputs, info.CurrentStamina);

        // Provisional guard: never clamp the server-authoritative current values below what was sent.
        return new VitalStats(
            Math.Max(formula.MaxHp, info.CurrentHp),
            Math.Max(formula.MaxMp, info.CurrentMp),
            Math.Max(formula.MaxStamina, info.CurrentStamina));
    }
}

/// <summary>
///     Immutable inputs available when resolving a freshly-spawned actor's vital capacities.
/// </summary>
public readonly record struct SpawnInfo(
    ActorKey Key,
    ushort Level,
    uint CurrentHp,
    uint CurrentMp,
    uint CurrentStamina,
    ushort ServerClass);