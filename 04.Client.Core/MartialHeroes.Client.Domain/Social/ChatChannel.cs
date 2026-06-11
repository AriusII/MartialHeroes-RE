namespace MartialHeroes.Client.Domain.Social;

/// <summary>
/// The chat message families the central chat/command parser emits, identified by their C2S opcode
/// role. spec: Docs/RE/specs/social.md §2.1 / §4.
/// </summary>
/// <remarks>
/// These mirror the §2.1 C2S chat/whisper opcode roles. The exact per-field channel/scope of several
/// variants is <c>UNVERIFIED</c> (§9 #3/#4/#10); only the routing family and the text caps are modelled
/// here. spec: social.md §4 / §9.
/// </remarks>
public enum ChatChannel : byte
{
    /// <summary>Private whisper to a named target (2:7). spec: social.md §3.</summary>
    Whisper = 0,

    /// <summary>Chat variant with a 28-byte context header (2:82); purpose UNVERIFIED #3. spec: social.md §4.</summary>
    Context82 = 1,

    /// <summary>Contextual chat (2:83), text gated 0 &lt; len &lt; 200. spec: social.md §4.</summary>
    Contextual = 2,

    /// <summary>Chat variant (2:84); channel/scope UNVERIFIED #4. spec: social.md §4.</summary>
    Variant84 = 3,

    /// <summary>General/channel chat (3:21) with a channel selector in the header. spec: social.md §4.</summary>
    Channel = 4,
}

/// <summary>
/// The result of validating an outbound chat message against the §4 / §8 routing rules.
/// spec: Docs/RE/specs/social.md §4 / §8.
/// </summary>
public enum ChatRouteResult
{
    /// <summary>The message passes the text-length / self-target gates and may be sent. spec: social.md §4/§8.</summary>
    Send = 0,

    /// <summary>The text is empty and the channel does not permit empty text. spec: social.md §4 (0 &lt; len gate).</summary>
    EmptyText = 1,

    /// <summary>The text exceeds the channel's cap. spec: social.md §8 (whisper 119, chat &lt; 200).</summary>
    TooLong = 2,

    /// <summary>The whisper target resolved to the local player (self-whisper). spec: social.md §1/§3 (self-target guard).</summary>
    SelfTarget = 3,
}

/// <summary>
/// Pure chat-routing rules: text-length caps per channel, the 3:21 broadcast special path, and the
/// whisper self-target guard. spec: Docs/RE/specs/social.md §3 / §4 / §8.
/// </summary>
/// <remarks>
/// These are client-side display / send gates; the actual wire framing (length-prefixed CP949 text) is
/// owned by <c>Network.Protocol</c>. Text length here is a character count (the spec's caps are in
/// characters). spec: social.md §1 / §8.
/// </remarks>
public static class ChatRouting
{
    /// <summary>Whisper text cap, in characters. spec: social.md §3 / §8 (119).</summary>
    public const int WhisperTextCap = 119;

    /// <summary>General chat text cap (exclusive upper bound), in characters. spec: social.md §4 / §8 (&lt; 200).</summary>
    public const int ChatTextLimit = 200;

    /// <summary>The 3:21 broadcast-channel selector test: <c>selector mod 10 == 5</c> bypasses the length gate. spec: social.md §4.</summary>
    public const int BroadcastSelectorModulus = 10;

    /// <summary>The selector remainder marking the broadcast / shout channel. spec: social.md §4 (mod 10 == 5).</summary>
    public const int BroadcastSelectorRemainder = 5;

    /// <summary>
    /// True when a 3:21 channel selector takes the broadcast / shout special path (which bypasses the
    /// empty / length gate). spec: Docs/RE/specs/social.md §4 ("selector mod 10 == 5").
    /// </summary>
    public static bool IsBroadcastSelector(int selector) =>
        EuclideanMod(selector, BroadcastSelectorModulus) == BroadcastSelectorRemainder;

    /// <summary>
    /// Validates an outbound chat message: the whisper self-target guard, then the per-channel text
    /// length caps (with the 3:21 broadcast bypass). spec: Docs/RE/specs/social.md §3 / §4 / §8.
    /// </summary>
    /// <param name="channel">The chat family. spec: social.md §2.1.</param>
    /// <param name="textLength">The message text length in characters. spec: social.md §1/§8.</param>
    /// <param name="isSelfTarget">For a whisper, whether the resolved target is the local player. spec: social.md §3.</param>
    /// <param name="channelSelector">For 3:21, the channel selector dword (header +4). spec: social.md §4.</param>
    public static ChatRouteResult Validate(
        ChatChannel channel,
        int textLength,
        bool isSelfTarget = false,
        int channelSelector = 0)
    {
        // Whisper self-target guard: a self-whisper aborts (message id 862010101). spec: social.md §3.
        if (channel == ChatChannel.Whisper && isSelfTarget)
        {
            return ChatRouteResult.SelfTarget;
        }

        if (textLength < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(textLength), "Text length must be non-negative.");
        }

        // 3:21 broadcast channel bypasses the empty / length gate. spec: social.md §4.
        if (channel == ChatChannel.Channel && IsBroadcastSelector(channelSelector))
        {
            return ChatRouteResult.Send;
        }

        return channel switch
        {
            // Whisper: hard cap 119 characters; empty whisper text is allowed (no empty gate observed). spec: §3/§8.
            ChatChannel.Whisper => textLength > WhisperTextCap ? ChatRouteResult.TooLong : ChatRouteResult.Send,

            // Contextual (2:83): gated 0 < len < 200. spec: §4.
            ChatChannel.Contextual => GateExclusive(textLength),

            // General channel (3:21, non-broadcast): text < 200. spec: §4.
            ChatChannel.Channel => textLength >= ChatTextLimit ? ChatRouteResult.TooLong : ChatRouteResult.Send,

            // Variants 2:82 / 2:84: no text gate observed in the builder. spec: §4.
            ChatChannel.Context82 or ChatChannel.Variant84 => ChatRouteResult.Send,

            _ => ChatRouteResult.Send,
        };
    }

    /// <summary>Gate for the contextual channel: <c>0 &lt; len &lt; 200</c>. spec: social.md §4 (2:83).</summary>
    private static ChatRouteResult GateExclusive(int textLength)
    {
        if (textLength == 0)
        {
            return ChatRouteResult.EmptyText;
        }

        return textLength >= ChatTextLimit ? ChatRouteResult.TooLong : ChatRouteResult.Send;
    }

    /// <summary>Non-negative (Euclidean) modulo so a negative selector still maps correctly. spec: social.md §4.</summary>
    private static int EuclideanMod(int value, int modulus)
    {
        int r = value % modulus;
        return r < 0 ? r + modulus : r;
    }
}
