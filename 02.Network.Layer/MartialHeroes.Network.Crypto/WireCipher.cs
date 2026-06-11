using System.Runtime.CompilerServices;
using static MartialHeroes.Network.Crypto.ByteRotate;

namespace MartialHeroes.Network.Crypto;

/// <summary>
/// The Martial Heroes outbound wire byte cipher: a keyless, stateless, position-and-feedback
/// dependent transform of a single packet payload. It is a pure function of the payload bytes and
/// their length — there is no key, seed, or rolling state across packets, so no <c>CipherState</c>
/// is required (see <see cref="CipherState"/> for the deliberately-empty API-symmetry placeholder).
/// <para>
/// The transform applies <c>R = 3</c> identical rounds; each round is a forward sweep (low→high)
/// followed by a backward sweep (high→low). Every sweep restarts a remaining-length countdown and a
/// one-byte feedback accumulator. All numeric constants are cited inline.
/// </para>
/// spec: Docs/RE/specs/crypto.md §3.1, §3.3, §8.1
/// </summary>
public static class WireCipher
{
    /// <summary>Number of full forward+backward rounds. spec: Docs/RE/specs/crypto.md §8.1 (R).</summary>
    private const int Rounds = 3;

    // Forward-sweep rotation amounts. spec: Docs/RE/specs/crypto.md §8.1 (r1_fwd = ROL 3, r2_fwd = ROR 1).
    private const int ForwardRol = 3;
    private const int ForwardRor = 1;

    // Forward whitening additive after one's-complement: 0x48 + NOT8(x) ≡ 71 − x (mod 256).
    // spec: Docs/RE/specs/crypto.md §3.1, §8.1 (W_fwd).
    private const byte ForwardWhitenAdd = 0x48;

    // Backward-sweep rotation amounts. spec: Docs/RE/specs/crypto.md §8.1 (r1_bwd = ROL 4, r2_bwd = ROR 3).
    private const int BackwardRol = 4;
    private const int BackwardRor = 3;

    // Backward whitening XOR mask, applied before the final ROR 3.
    // spec: Docs/RE/specs/crypto.md §3.1, §8.1 (W_bwd).
    private const byte BackwardWhitenXor = 0x13;

    /// <summary>
    /// Apply the forward cipher in place over <paramref name="payload"/> (the post-header payload
    /// region only). A length-0 payload is a no-op pass-through, matching the header-only packet rule.
    /// spec: Docs/RE/specs/crypto.md §2 (header-only pass-through), §3.1.
    /// </summary>
    public static void EncryptInPlace(Span<byte> payload)
    {
        if (payload.IsEmpty)
        {
            return;
        }

        for (int round = 0; round < Rounds; round++)
        {
            EncryptForwardSweep(payload);
            EncryptBackwardSweep(payload);
        }
    }

    /// <summary>
    /// Apply the algebraic inverse in place. Undoes <see cref="EncryptInPlace"/> exactly:
    /// the rounds run unchanged in count, but within each round the backward sweep is inverted
    /// first, then the forward sweep, mirroring §3.3.
    /// spec: Docs/RE/specs/crypto.md §3.3.
    /// </summary>
    public static void DecryptInPlace(Span<byte> payload)
    {
        if (payload.IsEmpty)
        {
            return;
        }

        for (int round = 0; round < Rounds; round++)
        {
            DecryptBackwardSweep(payload);
            DecryptForwardSweep(payload);
        }
    }

    // ---- Forward sweep (encrypt): low → high ----------------------------------------------------

    private static void EncryptForwardSweep(Span<byte> payload)
    {
        byte acc = 0;                       // feedback accumulator, reset per sweep. §3.1
        byte p = (byte)payload.Length;      // remaining-length countdown, low 8 bits. §3.1 load-bearing note

        for (int i = 0; i < payload.Length; i++)
        {
            byte t = Rol8(payload[i], ForwardRol); // ROL 3
            t = (byte)(t + p);                     // ADD8 remaining-length counter
            t = (byte)(t ^ acc);                   // fold in feedback
            acc = t;                               // chain pre-whitening intermediate
            // Whitening: 0x48 + NOT8(ROR8(t,1)) ≡ 71 − ROR8(t,1) (mod 256). §3.1
            payload[i] = (byte)(ForwardWhitenAdd + Not8(Ror8(t, ForwardRor)));
            p = (byte)(p - 1);                     // SUB8 countdown
        }
    }

    // ---- Backward sweep (encrypt): high → low ---------------------------------------------------

    private static void EncryptBackwardSweep(Span<byte> payload)
    {
        byte acc = 0;
        byte p = (byte)payload.Length;

        for (int i = payload.Length - 1; i >= 0; i--)
        {
            byte t = Rol8(payload[i], BackwardRol);  // ROL 4
            t = (byte)(t + p);                       // ADD8 countdown
            t = (byte)(t ^ acc);                     // fold in feedback
            acc = t;                                 // chain pre-whitening intermediate
            payload[i] = Ror8((byte)(t ^ BackwardWhitenXor), BackwardRor); // XOR 0x13 then ROR 3
            p = (byte)(p - 1);
        }
    }

    // ---- Inverse forward sweep: visit in the SAME low → high order encrypt used --------------
    // We recover the per-position intermediate t (== acc_i) from the stored byte, undo the feedback
    // XOR using the previous intermediate, undo the position add, then undo the first rotation.
    // Reconstructing acc / p in the exact order encrypt produced them keeps the chain exact. §3.3

    private static void DecryptForwardSweep(Span<byte> payload)
    {
        byte accPrev = 0;
        byte p = (byte)payload.Length;

        for (int i = 0; i < payload.Length; i++)
        {
            // Undo whitening: out = 0x48 + NOT8(ROR8(t,1))  ⇒  ROR8(t,1) = NOT8(out − 0x48).
            byte rored = Not8((byte)(payload[i] - ForwardWhitenAdd));
            byte t = Rol8(rored, ForwardRor);        // undo ROR8(.,1) → post-feedback intermediate (acc_i)
            byte input = (byte)((t ^ accPrev) - p);  // undo XOR feedback, then undo ADD8(.,p)
            payload[i] = Ror8(input, ForwardRol);    // undo ROL8(.,3)
            accPrev = t;                             // this position's intermediate feeds the next
            p = (byte)(p - 1);
        }
    }

    // ---- Inverse backward sweep: visit in the SAME high → low order encrypt used --------------

    private static void DecryptBackwardSweep(Span<byte> payload)
    {
        byte accPrev = 0;
        byte p = (byte)payload.Length;

        for (int i = payload.Length - 1; i >= 0; i--)
        {
            byte t = (byte)(Rol8(payload[i], BackwardRor) ^ BackwardWhitenXor); // undo ROR8(.,3), then undo XOR 0x13 → acc_i
            byte input = (byte)((t ^ accPrev) - p);  // undo XOR feedback, then undo ADD8(.,p)
            payload[i] = Ror8(input, BackwardRol);   // undo ROL8(.,4)
            accPrev = t;
            p = (byte)(p - 1);
        }
    }
}
