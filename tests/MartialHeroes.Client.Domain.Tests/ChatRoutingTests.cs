using MartialHeroes.Client.Domain.Social;
using Xunit;

namespace MartialHeroes.Client.Domain.Tests;

public sealed class ChatRoutingTests
{
    [Fact]
    public void Constants_MatchSpec()
    {
        Assert.Equal(119, ChatRouting.WhisperTextCap);
        Assert.Equal(200, ChatRouting.ChatTextLimit);
    }

    [Fact]
    public void Whisper_SelfTarget_Rejected()
    {
        Assert.Equal(ChatRouteResult.SelfTarget,
            ChatRouting.Validate(ChatChannel.Whisper, textLength: 5, isSelfTarget: true));
    }

    [Theory]
    [InlineData(0, ChatRouteResult.Send)]     // empty whisper allowed
    [InlineData(119, ChatRouteResult.Send)]   // at cap
    [InlineData(120, ChatRouteResult.TooLong)] // over cap
    public void Whisper_TextCap119(int len, ChatRouteResult expected)
    {
        Assert.Equal(expected, ChatRouting.Validate(ChatChannel.Whisper, len));
    }

    [Theory]
    [InlineData(0, ChatRouteResult.EmptyText)]
    [InlineData(1, ChatRouteResult.Send)]
    [InlineData(199, ChatRouteResult.Send)]
    [InlineData(200, ChatRouteResult.TooLong)]
    public void Contextual_Gated_0_To_200_Exclusive(int len, ChatRouteResult expected)
    {
        Assert.Equal(expected, ChatRouting.Validate(ChatChannel.Contextual, len));
    }

    [Theory]
    [InlineData(0, ChatRouteResult.Send)]      // channel allows empty (no exclusive gate)
    [InlineData(199, ChatRouteResult.Send)]
    [InlineData(200, ChatRouteResult.TooLong)]
    public void Channel_Limit200(int len, ChatRouteResult expected)
    {
        Assert.Equal(expected, ChatRouting.Validate(ChatChannel.Channel, len));
    }

    [Fact]
    public void Channel_BroadcastSelector_BypassesLengthGate()
    {
        // selector mod 10 == 5 → broadcast → empty and long text both pass.
        Assert.True(ChatRouting.IsBroadcastSelector(15));
        Assert.True(ChatRouting.IsBroadcastSelector(5));
        Assert.False(ChatRouting.IsBroadcastSelector(14));

        Assert.Equal(ChatRouteResult.Send,
            ChatRouting.Validate(ChatChannel.Channel, textLength: 0, channelSelector: 5));
        Assert.Equal(ChatRouteResult.Send,
            ChatRouting.Validate(ChatChannel.Channel, textLength: 5000, channelSelector: 25));
    }

    [Fact]
    public void Variants_NoTextGate()
    {
        Assert.Equal(ChatRouteResult.Send, ChatRouting.Validate(ChatChannel.Context82, 9999));
        Assert.Equal(ChatRouteResult.Send, ChatRouting.Validate(ChatChannel.Variant84, 9999));
    }
}
