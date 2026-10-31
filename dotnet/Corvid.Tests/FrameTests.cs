// Copyright (c) 2026 AqueGen. All rights reserved. See LICENSE.
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

    private static byte[] Raw(ChannelKey key, byte[] cipher, byte count = 1)
    {
        var frame = new byte[FrameWriter.Size];
        frame[0] = 0xC7;
        frame[1] = 0x1D;
        frame[2] = 1;
        frame[16] = count;
        frame[17] = 1;
        frame[18] = key.KeyId;
        frame[19] = (byte)cipher.Length;
        frame[20] = (byte)(cipher.Length >> 8);
        cipher.CopyTo(frame, 21);
        return frame;
    }

    private static byte[] Seal(ChannelKey key, byte[] body)
    {
        var crc = Crc32.Compute(body);
        var plain = body.Concat(BitConverter.GetBytes(crc)).ToArray();
        ChaCha20.Xor(key.Bytes, new byte[12], 0, plain);
        return plain;
    }

    [Fact]
    public void OpenRejectsCipherShorterThanCrc()
    {
        var key = ChannelKey.Generate();
        Assert.True(Frame.TryParse(Raw(key, [1, 2, 3]), out var frame));
        Assert.Equal(OpenStatus.Malformed, frame.Open(1, key).Status);
    }

    [Theory]
    [InlineData("0005000102")]
    [InlineData("00010001FF")]
    [InlineData("0000")]
    public void OpenRejectsMessageLengthsThatMissTheCrc(string bodyHex)
    {
        var key = ChannelKey.Generate();
        Assert.True(Frame.TryParse(Raw(key, Seal(key, Convert.FromHexString(bodyHex))), out var frame));
        Assert.Equal(OpenStatus.Malformed, frame.Open(1, key).Status);
    }

    [Fact]
    public void TryParseWalksIntoPaddingAndOpenStillWorks()
    {
        var key = ChannelKey.Generate();
        Assert.True(Frame.TryParse(Raw(key, Seal(key, [0, 1, 0, 9]), 5), out var frame));
        Assert.Equal(5, frame.Segments.Count);
        var result = frame.Open(1, key);
        Assert.Equal(OpenStatus.Ok, result.Status);
        Assert.Equal(new byte[] { 9 }, Assert.Single(result.Messages).Body);
    }

    [Fact]
    public void WriteThrowsWhenOverSize()
    {
        var key = ChannelKey.Generate();
        Assert.Throws<ArgumentException>(() =>
            FrameWriter.Write(new byte[8], 0, [(1, key, [new Message(0, new byte[260])])]));
    }

    [Fact]
    public void WriteThrowsForBodyOverU16()
    {
        var key = ChannelKey.Generate();
        var ex = Assert.Throws<ArgumentException>(() =>
            FrameWriter.Write(new byte[8], 0, [(1, key, [new Message(0, new byte[65536])])]));
        Assert.Contains("65535", ex.Message);
    }

    [Fact]
    public void WriteThrowsForDuplicateChannel()
    {
        var key = ChannelKey.Generate();
        var ex = Assert.Throws<ArgumentException>(() =>
            FrameWriter.Write(new byte[8], 0, [(1, key, [new Message(0, [1])]), (1, key, [new Message(0, [2])])]));
        Assert.Contains("Channel 1", ex.Message);
    }
}
