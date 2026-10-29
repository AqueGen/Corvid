namespace Corvid.Tests;

public class FrameTests
{
    public static IEnumerable<object[]> Cases() =>
        File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Fixtures", "frames.tsv"))
            .Where(l => l.Length > 0)
            .Select(l => l.Split('\t'))
            .Select(c => new object[] { c[0], c[1], c[2], c[3], c[4] });

    private static List<(byte Channel, ChannelKey Key, IReadOnlyList<Message> Messages)> ParseSegments(string text) =>
        text.Length == 0
            ? []
            : text.Split(';').Select(s =>
            {
                var p = s.Split(':', 3);
                var messages = p[2].Split(',').Select(m =>
                {
                    var kv = m.Split('=');
                    return new Message(byte.Parse(kv[0]), Convert.FromHexString(kv[1]));
                }).ToList();
                return (byte.Parse(p[0]), ChannelKey.FromHex(p[1]), (IReadOnlyList<Message>)messages);
            }).ToList();

    [Theory]
    [MemberData(nameof(Cases))]
    public void WriteMatchesFixtureAndOpensBack(string name, string session, string counter, string segments, string frameHex)
    {
        var parsed = ParseSegments(segments);
        var bytes = FrameWriter.Write(Convert.FromHexString(session), uint.Parse(counter), parsed);

        Assert.Equal(frameHex, Convert.ToHexStringLower(bytes));
        Assert.True(Frame.TryParse(bytes, out var frame), name);
        Assert.Equal(uint.Parse(counter), frame.Counter);
        Assert.Equal(Convert.FromHexString(session), frame.Session);
        foreach (var (channel, key, messages) in parsed)
        {
            var result = frame.Open(channel, key);
            Assert.Equal(OpenStatus.Ok, result.Status);
            Assert.Equal(messages.Count, result.Messages.Count);
            for (var i = 0; i < messages.Count; i++)
            {
                Assert.Equal(messages[i].Type, result.Messages[i].Type);
                Assert.Equal(messages[i].Body, result.Messages[i].Body);
            }
        }
    }

    private static byte[] Sample(out ChannelKey key)
    {
        key = ChannelKey.FromHex("0102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f20");
        return FrameWriter.Write(new byte[8], 1, [(1, key, [new Message(0, [1])])]);
    }

    [Fact]
    public void TryParseRejectsBadFrames()
    {
        var good = Sample(out _);
        Assert.True(Frame.TryParse(good, out _));

        var badMagic = (byte[])good.Clone();
        badMagic[0] = 0;
        Assert.False(Frame.TryParse(badMagic, out _));

        var v2 = (byte[])good.Clone();
        v2[2] = 2;
        Assert.False(Frame.TryParse(v2, out _));

        Assert.False(Frame.TryParse(good.AsSpan(0, 267), out _));
        Assert.False(Frame.TryParse(good.Concat(new byte[1]).ToArray(), out _));

        var overrun = (byte[])good.Clone();
        overrun[19] = 0xFF;
        overrun[20] = 0x7F;
        Assert.False(Frame.TryParse(overrun, out _));
    }

    [Fact]
    public void OpenReportsWrongKeyBadCrcAndNoSegment()
    {
        var bytes = Sample(out var key);
        Assert.True(Frame.TryParse(bytes, out var frame));

        ChannelKey other;
        do { other = ChannelKey.Generate(); } while (other.KeyId == key.KeyId);
        Assert.Equal(OpenStatus.WrongKey, frame.Open(1, other).Status);

        ChannelKey sameId;
        do { sameId = ChannelKey.Generate(); } while (sameId.KeyId != key.KeyId || sameId.ToHex() == key.ToHex());
        Assert.Equal(OpenStatus.BadCrc, frame.Open(1, sameId).Status);

        Assert.Equal(OpenStatus.NoSegment, frame.Open(2, key).Status);
    }

    [Fact]
    public void WriteThrowsWhenOverSize()
    {
        var key = ChannelKey.Generate();
        Assert.Throws<ArgumentException>(() =>
            FrameWriter.Write(new byte[8], 0, [(1, key, [new Message(0, new byte[260])])]));
    }
}
