using MartialHeroes.Network.Crypto;

namespace MartialHeroes.Network.Crypto.Tests;

/// <summary>
/// The staged RSA plaintext <c>M</c> is a fixed 17-byte zero-padded buffer: password bytes then zeros,
/// consumed in full as M. spec: Docs/RE/specs/crypto.md §6.1, §6.6, §6b; packets/login.yaml.
/// </summary>
public sealed class CredentialPlaintextTests
{
    [Fact]
    public void StagePassword_Produces_Exactly_17_Bytes()
    {
        byte[] staged = CredentialPlaintext.StagePassword("pw"u8);
        Assert.Equal(17, staged.Length);
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
    public void StagePassword_Rejects_Wrong_Destination_Width()
    {
        byte[] dst = new byte[16];
        Assert.Throws<ArgumentException>(() => CredentialPlaintext.StagePassword("pw"u8, dst));
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