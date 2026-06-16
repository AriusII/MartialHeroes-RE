using MartialHeroes.Network.Crypto;

namespace MartialHeroes.Network.Crypto.Tests;

/// <summary>
/// The staged RSA plaintext <c>M</c> is a zero-padded buffer sized to the password-field cap: password
/// bytes then zeros, consumed in full as M. The width is parameter-driven (default 17 = debugger-observed
/// cap). spec: Docs/RE/specs/crypto.md §6.1, §6.6, §8.1, §9.2 point 2, §6b; packets/login.yaml.
/// </summary>
public sealed class CredentialPlaintextTests
{
    [Fact]
    public void StagePassword_Defaults_To_The_Observed_17_Byte_Cap()
    {
        byte[] staged = CredentialPlaintext.StagePassword("pw"u8);
        Assert.Equal(CredentialPlaintext.DefaultPasswordFieldCap, staged.Length);
        Assert.Equal(17, staged.Length);
    }

    [Fact]
    public void StagePassword_Honors_A_Caller_Supplied_Field_Cap()
    {
        // The width is parameter-driven: a different field cap yields a different-width M. spec §6.6, §8.1.
        byte[] staged = CredentialPlaintext.StagePassword("pw"u8, fieldCap: 24);
        Assert.Equal(24, staged.Length);
        Assert.True(staged.AsSpan(0, 2).SequenceEqual("pw"u8));
        for (int i = 2; i < 24; i++)
        {
            Assert.Equal(0, staged[i]);
        }
    }

    [Fact]
    public void StagePassword_Rejects_A_Field_Cap_Too_Small_For_A_2_Char_Password()
    {
        // Cap must leave room for a 2-char password plus one trailing zero (>= 3). spec §6.1, §6.6.
        Assert.Throws<ArgumentOutOfRangeException>(() => CredentialPlaintext.StagePassword("ab"u8, fieldCap: 2));
    }

    [Fact]
    public void StagePassword_Copies_Password_Then_Zero_Pads_The_Tail()
    {
        // A 5-byte password leaves 12 trailing zero bytes (all part of M). spec §6.1, §6.6.
        ReadOnlySpan<byte> password = "abcde"u8;
        byte[] staged = CredentialPlaintext.StagePassword(password);

        Assert.True(staged.AsSpan(0, password.Length).SequenceEqual(password));
        for (int i = password.Length; i < 17; i++)
        {
            Assert.Equal(0, staged[i]);
        }
    }

    [Fact]
    public void StagePassword_Into_Caller_Buffer_Overwrites_Prior_Content()
    {
        Span<byte> dst = stackalloc byte[17];
        dst.Fill(0xFF); // dirty the buffer to prove the tail is re-zeroed
        CredentialPlaintext.StagePassword("xy"u8, dst);

        Assert.Equal((byte)'x', dst[0]);
        Assert.Equal((byte)'y', dst[1]);
        for (int i = 2; i < 17; i++)
        {
            Assert.Equal(0, dst[i]);
        }
    }

    [Fact]
    public void StagePassword_Rejects_Too_Short_And_Too_Long_Passwords()
    {
        // length >= 2 and < 17. spec: packets/login.yaml.
        Assert.Throws<ArgumentException>(() => CredentialPlaintext.StagePassword("a"u8.ToArray()));
        Assert.Throws<ArgumentException>(() =>
            CredentialPlaintext.StagePassword(new byte[17])); // exactly 17 = no room for pad
        Assert.Throws<ArgumentException>(() => CredentialPlaintext.StagePassword(new byte[18]));
    }

    [Fact]
    public void StagePassword_Into_Buffer_Treats_The_Destination_Width_As_The_Field_Cap()
    {
        // A 16-byte destination is a valid 16-byte field cap (parameter-driven width). spec §6.6, §8.1.
        byte[] dst = new byte[16];
        CredentialPlaintext.StagePassword("pw"u8, dst);
        Assert.Equal((byte)'p', dst[0]);
        Assert.Equal((byte)'w', dst[1]);
        for (int i = 2; i < 16; i++)
        {
            Assert.Equal(0, dst[i]);
        }
    }

    [Fact]
    public void StagePassword_Into_Buffer_Rejects_A_Destination_Too_Small_For_A_2_Char_Password()
    {
        // A 2-byte destination cannot hold a 2-char password plus a trailing zero. spec §6.1, §6.6.
        byte[] dst = new byte[2];
        Assert.Throws<ArgumentException>(() => CredentialPlaintext.StagePassword("ab"u8, dst));
    }

    [Fact]
    public void Boundary_Password_Lengths_2_And_16_Are_Accepted()
    {
        Assert.Equal(17, CredentialPlaintext.StagePassword(new byte[2] { 1, 2 }).Length);
        Assert.Equal(17, CredentialPlaintext.StagePassword(new byte[16]
        {
            1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16
        }).Length);
    }
}