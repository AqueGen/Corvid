namespace Corvid.Tests;

public class CryptoTests
{
    public static IEnumerable<object[]> ChaChaRows() =>
        File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Fixtures", "chacha20.tsv"))
            .Where(l => l.Length > 0)
            .Select(l => l.Split('\t'))
            .Select(c => new object[] { c[0], c[1], c[2], uint.Parse(c[3]), c[4], c[5] });

    [Fact]
    public void Crc32_CheckValue() =>
        Assert.Equal(0xCBF43926u, Crc32.Compute("123456789"u8));

    [Theory]
    [MemberData(nameof(ChaChaRows))]
    public void ChaCha20_MatchesFixture(string name, string key, string nonce, uint counter, string plain, string cipher)
    {
        var data = Convert.FromHexString(plain);
        ChaCha20.Xor(Convert.FromHexString(key), Convert.FromHexString(nonce), counter, data);
        Assert.True(cipher == Convert.ToHexStringLower(data), name);
    }

    [Fact]
    public void ChannelKey_ZeroKeyId() =>
        Assert.Equal((byte)0x76, ChannelKey.FromHex(new string('0', 64)).KeyId);

    [Fact]
    public void ChannelKey_RejectsBadHex() =>
        Assert.Throws<FormatException>(() => ChannelKey.FromHex("abc"));

    [Fact]
    public void ChannelKey_GenerateRoundTrips()
    {
        var key = ChannelKey.Generate();
        Assert.Equal(key.ToHex(), ChannelKey.FromHex(key.ToHex()).ToHex());
        Assert.Equal(32, key.Bytes.Length);
    }
}
