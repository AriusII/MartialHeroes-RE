namespace MartialHeroes.Network.Crypto;

public static class CredentialPlaintext
{
    public const int DefaultPasswordFieldCap = 17;

    public const int MinPasswordLength = 2;

    public static byte[] StagePassword(ReadOnlySpan<byte> password, int fieldCap = DefaultPasswordFieldCap)
    {
        if (fieldCap < MinPasswordLength + 1)
            throw new ArgumentOutOfRangeException(
                nameof(fieldCap),
                fieldCap,
                $"Password-field cap must be at least {MinPasswordLength + 1} (room for a {MinPasswordLength}-char password plus one trailing zero).");

        var staged = new byte[fieldCap];
        StagePassword(password, staged);
        return staged;
    }

    public static void StagePassword(ReadOnlySpan<byte> password, Span<byte> destination)
    {
        var fieldCap = destination.Length;
        if (fieldCap < MinPasswordLength + 1)
            throw new ArgumentException(
                $"Staged M buffer (password-field cap) must be at least {MinPasswordLength + 1} bytes, got {fieldCap}.",
                nameof(destination));

        if (password.Length < MinPasswordLength || password.Length >= fieldCap)
            throw new ArgumentException(
                $"Password length {password.Length} is out of range [{MinPasswordLength}, {fieldCap}).",
                nameof(password));

        destination.Clear();
        password.CopyTo(destination);
    }
}