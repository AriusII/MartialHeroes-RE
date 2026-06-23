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

public sealed partial class GamePacketHandler(
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
    WorldEntryState? worldEntry = null,
    Func<byte, CancellationToken, ValueTask>? enterWorldEmitter = null)
    : IPacketHandler
{
    private readonly IClientEventBus _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
    private readonly IUnhandledOpcodeSink _unhandled = unhandled ?? throw new ArgumentNullException(nameof(unhandled));
    private readonly ClientWorld _world = world ?? throw new ArgumentNullException(nameof(world));

    public Func<CombatStats, CombatStats>? CombatStatsRecompute { get; init; }

    public Func<SkillId, int>? CooldownDurationResolver { get; init; }

    public ProgressionState Progression { get; private set; }

    public Func<long>? XpBonusRatePercentResolver { get; init; }

    public IReadOnlyList<long>? RankXpDivisorTable { get; init; }

    public IReadOnlyList<long>? RankXpCapTable { get; init; }

    public Action<ProgressionState>? ProgressionRefresh { get; init; }

    public Action<int>? LevelTableErrorSink { get; init; }

    public Func<SpawnInfo, VitalStats> VitalsResolver { get; init; } = DefaultVitalsResolver;


    public void OnUnhandled(uint packedOpcode, ReadOnlySpan<byte> payload)
    {
        switch (packedOpcode)
        {
            case Opcodes.SmsgKeyExchange:
                HandleKeyExchange(payload);
                return;

            case Opcodes.SmsgGameStateTick:
                HandleGameStateTick(payload);
                return;

            case Opcodes.SmsgAreaEntitySnapshot:
                if (HandleAreaEntitySnapshot(payload)) return;

                break;

            case Opcodes.SmsgActorSkillAction:
                if (HandleActorSkillAction(payload)) return;

                break;

            case Opcodes.SmsgSkillPointUpdate:
                if (payload.Length >= SmsgSkillPointUpdateHeader.HeaderSize)
                {
                    HandleSkillPointUpdate(in MemoryMarshal.AsRef<SmsgSkillPointUpdateHeader>(payload));
                    return;
                }

                break;

            case Opcodes.SmsgBuffSlotUpdate:
                if (HandleBuffSlotUpdate(payload)) return;

                break;

            case Opcodes.SmsgStatsUpdate:
                if (HandleStatsUpdate(payload)) return;

                break;

            case Opcodes.SmsgExpGain:
                if (HandleExpGain(payload)) return;

                break;

            case Opcodes.SmsgRankXpGain:
                if (HandleRankXpGain(payload)) return;

                break;

            case Opcodes.SmsgCombatAttackUpdate:
                if (HandleCombatAttackUpdate(payload)) return;

                break;

            case Opcodes.SmsgChatBroadcast:
                if (HandleChatBroadcast(payload)) return;

                break;

            case Opcodes.SmsgCharacterList:
                if (HandleCharacterList(payload)) return;

                break;

            case Opcodes.SmsgSceneEntityUpdate:
                if (HandleSceneEntityUpdate(payload)) return;

                break;
        }

        _unhandled.Record(packedOpcode, payload.Length);
    }


    private void RecomputeCombatStats()
    {
        if (CombatStatsRecompute is null || localPlayer is null || _world.LocalActorKey is not { } key) return;

        var recomputed = CombatStatsRecompute(localPlayer.Combat);
        localPlayer.Combat = recomputed;
        _eventBus.Publish(new CombatStatsRecomputedEvent(key, recomputed));
    }

    private static string DecodeFixedText(ReadOnlySpan<byte> buffer)
    {
        return Cp949Text.Decode(buffer);
    }

    private static EntitySort ToEntitySort(byte sort)
    {
        return sort switch
        {
            1 => EntitySort.PlayerCharacter,
            2 => EntitySort.Monster,
            3 => EntitySort.NonPlayerCharacter,
            _ => EntitySort.None
        };
    }

    private static VitalStats DefaultVitalsResolver(SpawnInfo info)
    {
        var inputs = VitalFormulaInputs.Empty with
        {
            ClassId = unchecked((byte)info.ServerClass)
        };

        var formula = VitalStats.FromFormula(in inputs, info.CurrentStamina);

        return new VitalStats(
            Math.Max(formula.MaxHp, info.CurrentHp),
            Math.Max(formula.MaxMp, info.CurrentMp),
            Math.Max(formula.MaxStamina, info.CurrentStamina));
    }
}

public readonly record struct SpawnInfo(
    ActorKey Key,
    ushort Level,
    uint CurrentHp,
    uint CurrentMp,
    uint CurrentStamina,
    ushort ServerClass);