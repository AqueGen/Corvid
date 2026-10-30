using System.Security.Cryptography;

namespace Corvid;

public sealed class ChannelKey
{
    private ChannelKey(byte[] bytes)
    {
        Bytes = bytes;
        Span<byte> first = stackalloc byte[1];
        ChaCha20.Xor(bytes, new byte[12], 0, first);
        KeyId = first[0];
    }

    public byte[] Bytes { get; }

    public byte KeyId { get; }

    public static ChannelKey FromHex(string hex)
    {
        if (hex is not { Length: 64 })
        {
            throw new FormatException("Key must be 64 hex characters.");
        }
        return new ChannelKey(Convert.FromHexString(hex));
    }

    public static ChannelKey Generate() => new(RandomNumberGenerator.GetBytes(32));

    public string ToHex() => Convert.ToHexStringLower(Bytes);
}
