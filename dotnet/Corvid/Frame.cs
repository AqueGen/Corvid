using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;

namespace Corvid;

public sealed record Message(byte Type, byte[] Body);

public sealed record Segment(byte Channel, byte KeyId, byte[] Cipher);

public enum OpenStatus { Ok, NoSegment, WrongKey, BadCrc, Malformed }

public sealed record OpenResult(OpenStatus Status, IReadOnlyList<Message> Messages);

public sealed record Frame(byte[] Session, uint Counter, IReadOnlyList<Segment> Segments)
{
    internal const int HeaderSize = 17;

    public static bool TryParse(ReadOnlySpan<byte> bytes, [NotNullWhen(true)] out Frame? frame)
    {
        frame = null;
        if (bytes.Length != FrameWriter.Size || bytes[0] != 0xC7 || bytes[1] != 0x1D || bytes[2] != 1)
        {
            return false;
        }

        var count = bytes[16];
        var segments = new List<Segment>(count);
        var pos = HeaderSize;
        for (var i = 0; i < count; i++)
        {
            if (pos + 4 > bytes.Length)
            {
                return false;
            }
            var length = BinaryPrimitives.ReadUInt16LittleEndian(bytes[(pos + 2)..]);
            if (pos + 4 + length > bytes.Length)
            {
                return false;
            }
            segments.Add(new Segment(bytes[pos], bytes[pos + 1], bytes.Slice(pos + 4, length).ToArray()));
            pos += 4 + length;
        }

        frame = new Frame(bytes[4..12].ToArray(), BinaryPrimitives.ReadUInt32LittleEndian(bytes[12..]), segments);
        return true;
    }

    public OpenResult Open(byte channel, ChannelKey key)
    {
        var segment = Segments.FirstOrDefault(s => s.Channel == channel);
        if (segment is null)
        {
            return new OpenResult(OpenStatus.NoSegment, []);
        }
        if (segment.KeyId != key.KeyId)
        {
            return new OpenResult(OpenStatus.WrongKey, []);
        }
        if (segment.Cipher.Length < 4)
        {
            return new OpenResult(OpenStatus.Malformed, []);
        }

        var plain = (byte[])segment.Cipher.Clone();
        var nonce = new byte[12];
        Session.CopyTo(nonce, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(nonce.AsSpan(8), Counter);
        ChaCha20.Xor(key.Bytes, nonce, 0, plain);

        var body = plain.AsSpan(0, plain.Length - 4);
        if (Crc32.Compute(body) != BinaryPrimitives.ReadUInt32LittleEndian(plain.AsSpan(plain.Length - 4)))
        {
            return new OpenResult(OpenStatus.BadCrc, []);
        }

        var messages = new List<Message>();
        var pos = 0;
        while (pos < body.Length)
        {
            if (pos + 3 > body.Length)
            {
                return new OpenResult(OpenStatus.Malformed, []);
            }
            var length = BinaryPrimitives.ReadUInt16LittleEndian(body[(pos + 1)..]);
            if (pos + 3 + length > body.Length)
            {
                return new OpenResult(OpenStatus.Malformed, []);
            }
            messages.Add(new Message(body[pos], body.Slice(pos + 3, length).ToArray()));
            pos += 3 + length;
        }
        return new OpenResult(OpenStatus.Ok, messages);
    }
}
