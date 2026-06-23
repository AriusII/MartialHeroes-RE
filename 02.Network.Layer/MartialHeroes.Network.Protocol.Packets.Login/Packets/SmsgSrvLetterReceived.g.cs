using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MartialHeroes.Network.Protocol.Packets;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct SmsgSrvLetterReceived
{
    public const uint OpcodeId = 0x10014;

    public uint LetterId;
    public SenderBuffer Sender;
    public Pad0Buffer Pad0;
    public uint LetterType;
    public uint AttachmentGold;
    public uint AttachmentItemId;
    public uint StatusFlags;
    public DateStringBuffer DateString;
    public SubjectStringBuffer SubjectString;
    public ReservedBuffer Reserved;

    [InlineArray(17)]
    public struct SenderBuffer
    {
        private byte _element0;
    }

    [InlineArray(3)]
    public struct Pad0Buffer
    {
        private byte _element0;
    }

    [InlineArray(17)]
    public struct DateStringBuffer
    {
        private byte _element0;
    }

    [InlineArray(17)]
    public struct SubjectStringBuffer
    {
        private byte _element0;
    }

    [InlineArray(2)]
    public struct ReservedBuffer
    {
        private byte _element0;
    }

}
