using System.Runtime.InteropServices;
using MartialHeroes.Client.Application.Contracts.Events;
using MartialHeroes.Client.Domain.Simulation.Simulation;
using MartialHeroes.Network.Protocol.Packets.World.Packets;

namespace MartialHeroes.Client.Application.Handlers;

public sealed partial class GamePacketHandler
{
    private const byte CubeResetSentinel = 0xFF;
    private const byte CubePhaseLand = 3;
    private const byte CubePhaseAdvance = 4;
    private const byte CubePhaseSettle = 5;

    private byte _cubePrevPhase;
    private byte _cubeDie5A;
    private byte _cubeDie5B;
    private byte _cubeDie4A;
    private byte _cubeDie4B;
    private long _cubeMoneyBaseline;
    private bool _cubeMoneyHasBaseline;

    public Func<long>? PlayerMoneyResolver { get; init; }

    private void HandleCubeGambleResult(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < SmsgCubeGambleResult.WireSize)
        {
            _unhandled.Record();
            return;
        }

        ref readonly var packet = ref MemoryMarshal.AsRef<SmsgCubeGambleResult>(payload);

        _eventBus.Publish(new CubeGambleResultEvent(
            packet.SubKind, packet.ResultCode, packet.BetType, packet.Wager));
    }

    private void HandleCubeGambleReelUpdate(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < SmsgCubeGambleReelUpdate.WireSize)
        {
            _unhandled.Record();
            return;
        }

        ref readonly var packet = ref MemoryMarshal.AsRef<SmsgCubeGambleReelUpdate>(payload);

        var phase = packet.Phase;
        var pack = packet.ReelDigitPack;

        if (pack == CubeResetSentinel)
        {
            PublishReel(phase, packet.SpinSubKind, true, false, packet.ThrowValue, 0L, false, 0L, 0u,
                CubeGambleEvaluator.NoSpecialSlot);
            return;
        }

        var tens = (byte)((pack >> 4) & 0x0F);
        var ones = (byte)(pack & 0x0F);

        switch (phase)
        {
            case CubePhaseSettle:
                _cubeDie5A = tens;
                _cubeDie5B = ones;
                PublishSettle(in packet);
                break;

            case CubePhaseAdvance:
                _cubeDie4A = tens;
                _cubeDie4B = ones;
                PublishReel(phase, packet.SpinSubKind, false, false, packet.ThrowValue, 0L, false, 0L, 0u,
                    CubeGambleEvaluator.NoSpecialSlot);
                break;

            case CubePhaseLand:
                var submit = _cubePrevPhase is CubePhaseSettle or 0;
                PublishReel(phase, packet.SpinSubKind, false, submit, packet.ThrowValue, 0L, false, 0L, 0u,
                    CubeGambleEvaluator.NoSpecialSlot);
                break;

            default:
                PublishReel(phase, packet.SpinSubKind, false, false, packet.ThrowValue, 0L, false, 0L, 0u,
                    CubeGambleEvaluator.NoSpecialSlot);
                break;
        }

        _cubePrevPhase = phase;
    }

    private void PublishSettle(in SmsgCubeGambleReelUpdate packet)
    {
        var newMoney = unchecked((long)packet.SettledMoney);

        var hasMoney = PlayerMoneyResolver is not null || _cubeMoneyHasBaseline;
        var oldMoney = PlayerMoneyResolver?.Invoke() ?? _cubeMoneyBaseline;

        var settlement = CubeGambleEvaluator.Settle(oldMoney, newMoney);
        var match = CubeGambleEvaluator.EvaluateReels(_cubeDie5A, _cubeDie5B, _cubeDie4A, _cubeDie4B);

        _cubeMoneyBaseline = newMoney;
        _cubeMoneyHasBaseline = true;

        PublishReel(
            CubePhaseSettle, packet.SpinSubKind, false, false, packet.ThrowValue,
            newMoney, hasMoney, hasMoney ? settlement.Delta : 0L,
            (uint)match.Lines, match.SpecialSlot);
    }

    private void PublishReel(
        byte phase, byte spinSubKind, bool isReset, bool submitOnLand, uint throwValue,
        long settledMoney, bool settled, long delta, uint winLines, sbyte specialSlot)
    {
        _eventBus.Publish(new CubeGambleReelEvent(
            phase, spinSubKind, isReset, submitOnLand,
            _cubeDie5A, _cubeDie5B, _cubeDie4A, _cubeDie4B,
            throwValue, settledMoney, settled, delta, winLines, specialSlot));
    }
}
