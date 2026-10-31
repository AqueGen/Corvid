// Copyright (c) 2026 AqueGen. All rights reserved. See LICENSE.
using System.Buffers.Binary;

namespace Corvid;

public static class FrameWriter
{
    public const int Size = 268;

    public static byte[] Write(
        byte[] session,
        uint counter,
        IReadOnlyList<(byte Channel, ChannelKey Key, IReadOnlyList<Message> Messages)> segments)
    {
        if (session.Length != 8)
        {
            throw new ArgumentException("Session must be 8 bytes.", nameof(session));
        }

        var nonce = new byte[12];
        session.CopyTo(nonce, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(nonce.AsSpan(8), counter);

        if (segments.GroupBy(s => s.Channel).FirstOrDefault(g => g.Count() > 1) is { } duplicate)
        {
            throw new ArgumentException($"Channel {duplicate.Key} appears more than once.", nameof(segments));
        }
        if (segments.SelectMany(s => s.Messages).FirstOrDefault(m => m.Body.Length > ushort.MaxValue) is { } big)
        {
            throw new ArgumentException($"Message body is {big.Body.Length} bytes, limit is {ushort.MaxValue}.", nameof(segments));
        }

        var frame = new List<byte> { 0xC7, 0x1D, 1, 0 };
        frame.AddRange(session);
        frame.AddRange(nonce.AsSpan(8).ToArray());
        frame.Add((byte)segments.Count);

        foreach (var (channel, key, messages) in segments)
        {
            var plain = new List<byte>();
            foreach (var m in messages)
            {
                plain.Add(m.Type);
                plain.Add((byte)m.Body.Length);
                plain.Add((byte)(m.Body.Length >> 8));
                plain.AddRange(m.Body);
            }
            var crc = Crc32.Compute(plain.ToArray());
            plain.Add((byte)crc);
            plain.Add((byte)(crc >> 8));
            plain.Add((byte)(crc >> 16));
            plain.Add((byte)(crc >> 24));

            var cipher = plain.ToArray();
            ChaCha20.Xor(key.Bytes, nonce, 0, cipher);
            frame.Add(channel);
            frame.Add(key.KeyId);
            frame.Add((byte)cipher.Length);
            frame.Add((byte)(cipher.Length >> 8));
            frame.AddRange(cipher);
        }

        if (frame.Count > Size)
        {
            throw new ArgumentException($"Frame is {frame.Count} bytes, limit is {Size}.", nameof(segments));
        }
        frame.AddRange(new byte[Size - frame.Count]);
        return frame.ToArray();
    }
}
