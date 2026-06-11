using MartialHeroes.Client.Application.Events;
using MartialHeroes.Client.Application.StateMachine;
using MartialHeroes.Client.Application.UseCases;
using MartialHeroes.Network.Abstractions.Protocol;
using MartialHeroes.Network.Abstractions.Session;
using MartialHeroes.Shared.Kernel.Numerics;
using Xunit;

namespace MartialHeroes.Client.Application.Tests;

public sealed class ApplicationUseCasesTests
{
    /// <summary>A fake outbound sink that records every send for assertions.</summary>
    private sealed class FakeOutboundSink : IOutboundPacketSink
    {
        public List<(ushort Major, ushort Minor, byte[] Payload)> Sends { get; } = new();

        public ValueTask SendAsync(
            SessionId sessionId, ushort majorOpcode, ushort minorOpcode,
            ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default)
        {
            Sends.Add((majorOpcode, minorOpcode, payload.ToArray()));
            return ValueTask.CompletedTask;
        }
    }

    [Fact]
    public async Task SelectCharacter_drives_fsm_to_loading()
    {
        var bus = new ClientEventBus(ClientEventBus.Unbounded);
        var fsm = new ClientStateMachine(bus, ClientState.CharacterSelection);
        var sink = new FakeOutboundSink();
        var useCases = new ApplicationUseCases(sink, fsm, new SessionId(1));

        await useCases.SelectCharacterAsync(slotIndex: 0);

        Assert.Equal(ClientState.Loading, fsm.Current);
    }

    [Fact]
    public async Task UseSkill_is_a_stub_and_sends_nothing_until_specced()
    {
        var bus = new ClientEventBus(ClientEventBus.Unbounded);
        var fsm = new ClientStateMachine(bus, ClientState.World);
        var sink = new FakeOutboundSink();
        var useCases = new ApplicationUseCases(sink, fsm, new SessionId(1));

        await useCases.UseSkillAsync(skillId: 5, targetId: 0);

        // Capture-gap: the 2/52 send-site layout is unknown; we must not fabricate a frame.
        Assert.Empty(sink.Sends);
    }

    [Fact]
    public async Task RequestMove_is_a_stub_and_sends_nothing_until_specced()
    {
        var bus = new ClientEventBus(ClientEventBus.Unbounded);
        var fsm = new ClientStateMachine(bus, ClientState.World);
        var sink = new FakeOutboundSink();
        var useCases = new ApplicationUseCases(sink, fsm, new SessionId(1));

        await useCases.RequestMoveAsync(Vector3Fixed.FromWholeUnits(3, 0, 4), running: true);

        Assert.Empty(sink.Sends);
    }
}
