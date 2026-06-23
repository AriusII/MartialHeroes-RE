using System.Buffers.Binary;

namespace MartialHeroes.Network.Crypto;

public static class HandshakeWhitening
{
    private const byte XorKeyByte = 0x29;

    private const byte Selector = 0x40;

    private const byte ComplementMask = 0x1F;
    private const byte ComplementTriggerValue = 1;

    public static void XorWhitenDwords(Span<byte> authReplyPayload)
    {
        var key = ResolveDwordKey();
        var length = authReplyPayload.Length;

        var full = length & ~3;
        for (var offset = 0; offset < full; offset += sizeof(uint))
        {
            var slot = authReplyPayload.Slice(offset, sizeof(uint));
            var value = BinaryPrimitives.ReadUInt32LittleEndian(slot);
            BinaryPrimitives.WriteUInt32LittleEndian(slot, value ^ key);
        }

        for (var offset = full; offset < length; offset++)
            authReplyPayload[offset] ^= (byte)(key >> (8 * (offset - full)));
    }

    private static uint ResolveDwordKey()
    {
        var keyByte = (Selector & XorKeyByte & ComplementMask) == ComplementTriggerValue
            ? unchecked((byte)~XorKeyByte)
            : XorKeyByte;

        return keyByte;
    }
}