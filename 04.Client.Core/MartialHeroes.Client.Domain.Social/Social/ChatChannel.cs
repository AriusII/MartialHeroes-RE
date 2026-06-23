namespace MartialHeroes.Client.Domain.Social.Social;

public enum ChatChannel : byte
{
    Whisper = 0,

    Context82 = 1,

    Contextual = 2,

    Variant84 = 3,

    Channel = 4
}

public enum ChatRouteResult
{
    Send = 0,

    EmptyText = 1,

    TooLong = 2,

    SelfTarget = 3
}

public static class ChatRouting
{
    public const int WhisperTextCap = 119;

    public const int ChatTextLimit = 200;

    public const int BroadcastSelectorModulus = 10;

    public const int BroadcastSelectorRemainder = 5;

    public static bool IsBroadcastSelector(int selector)
    {
        return EuclideanMod(selector, BroadcastSelectorModulus) == BroadcastSelectorRemainder;
    }

    public static ChatRouteResult Validate(
        ChatChannel channel,
        int textLength,
        bool isSelfTarget = false,
        int channelSelector = 0)
    {
        if (channel == ChatChannel.Whisper && isSelfTarget) return ChatRouteResult.SelfTarget;

        if (textLength < 0)
            throw new ArgumentOutOfRangeException(nameof(textLength), "Text length must be non-negative.");

        if (channel == ChatChannel.Channel && IsBroadcastSelector(channelSelector)) return ChatRouteResult.Send;

        return channel switch
        {
            ChatChannel.Whisper => textLength > WhisperTextCap ? ChatRouteResult.TooLong : ChatRouteResult.Send,

            ChatChannel.Contextual => GateExclusive(textLength),

            ChatChannel.Channel => textLength >= ChatTextLimit ? ChatRouteResult.TooLong : ChatRouteResult.Send,

            ChatChannel.Context82 or ChatChannel.Variant84 => ChatRouteResult.Send,

            _ => ChatRouteResult.Send
        };
    }

    private static ChatRouteResult GateExclusive(int textLength)
    {
        if (textLength == 0) return ChatRouteResult.EmptyText;

        return textLength >= ChatTextLimit ? ChatRouteResult.TooLong : ChatRouteResult.Send;
    }

    private static int EuclideanMod(int value, int modulus)
    {
        var r = value % modulus;
        return r < 0 ? r + modulus : r;
    }
}