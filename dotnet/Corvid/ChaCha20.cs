using System.Buffers.Binary;
using System.Numerics;

namespace Corvid;

public static class ChaCha20
{
    public static void Xor(ReadOnlySpan<byte> key, ReadOnlySpan<byte> nonce, uint counter, Span<byte> data)
    {
        if (key.Length != 32)
        {
            throw new ArgumentException("Key must be 32 bytes.", nameof(key));
        }
        if (nonce.Length != 12)
        {
            throw new ArgumentException("Nonce must be 12 bytes.", nameof(nonce));
        }

        Span<uint> state = stackalloc uint[16];
        state[0] = 0x61707865;
        state[1] = 0x3320646e;
        state[2] = 0x79622d32;
        state[3] = 0x6b206574;
        for (var i = 0; i < 8; i++)
        {
            state[4 + i] = BinaryPrimitives.ReadUInt32LittleEndian(key[(i * 4)..]);
        }
        for (var i = 0; i < 3; i++)
        {
            state[13 + i] = BinaryPrimitives.ReadUInt32LittleEndian(nonce[(i * 4)..]);
        }

        Span<uint> work = stackalloc uint[16];
        Span<byte> block = stackalloc byte[64];
        for (var offset = 0; offset < data.Length; offset += 64, counter++)
        {
            state[12] = counter;
            state.CopyTo(work);
            for (var round = 0; round < 10; round++)
            {
                QuarterRound(work, 0, 4, 8, 12);
                QuarterRound(work, 1, 5, 9, 13);
                QuarterRound(work, 2, 6, 10, 14);
                QuarterRound(work, 3, 7, 11, 15);
                QuarterRound(work, 0, 5, 10, 15);
                QuarterRound(work, 1, 6, 11, 12);
                QuarterRound(work, 2, 7, 8, 13);
                QuarterRound(work, 3, 4, 9, 14);
            }
            for (var i = 0; i < 16; i++)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(block[(i * 4)..], work[i] + state[i]);
            }
            var n = Math.Min(64, data.Length - offset);
            for (var i = 0; i < n; i++)
            {
                data[offset + i] ^= block[i];
            }
        }
    }

    private static void QuarterRound(Span<uint> s, int a, int b, int c, int d)
    {
        s[a] += s[b]; s[d] = BitOperations.RotateLeft(s[d] ^ s[a], 16);
        s[c] += s[d]; s[b] = BitOperations.RotateLeft(s[b] ^ s[c], 12);
        s[a] += s[b]; s[d] = BitOperations.RotateLeft(s[d] ^ s[a], 8);
        s[c] += s[d]; s[b] = BitOperations.RotateLeft(s[b] ^ s[c], 7);
    }
}
