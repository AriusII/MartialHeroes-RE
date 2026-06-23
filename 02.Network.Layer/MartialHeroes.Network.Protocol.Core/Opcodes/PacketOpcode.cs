namespace MartialHeroes.Network.Protocol.Core.Opcodes;

public readonly record struct PacketOpcode(ushort Major, ushort Minor)
{
    public uint Packed => ((uint)Major << 16) | Minor;

    public static PacketOpcode FromPacked(uint packed)
    {
        return new PacketOpcode((ushort)(packed >> 16), (ushort)(packed & 0xFFFF));
    }

    public override string ToString()
    {
        return $"{Major}:{Minor} (0x{Packed:x})";
    }
}