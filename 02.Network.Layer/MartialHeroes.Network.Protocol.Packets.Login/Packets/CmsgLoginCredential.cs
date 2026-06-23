namespace MartialHeroes.Network.Protocol.Packets.Login.Packets;

public static class CmsgLoginCredential
{
    public const uint OpcodeId = 0x10004;

    public const byte CredentialSubOpcode = 0x2B;

    public const int PasswordPadWidth = 17;

    public const int AccountMaxLength = 20;

    public const byte WhiteningKey = 0x29;

    public const byte WhiteningSelector = 0x40;
}