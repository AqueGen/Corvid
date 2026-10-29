using System.Drawing;
using System.Text;
using ZXing;
using ZXing.Common;
using ZXing.QrCode;
using ZXing.QrCode.Internal;

namespace Corvid;

public static class QrPicture
{
    const int Modules = 57;
    const int FrameSize = 268;
    const string Latin1 = "ISO-8859-1";

    static readonly BarcodeReaderGeneric Reader = new()
    {
        Options = new DecodingOptions
        {
            PossibleFormats = [BarcodeFormat.QR_CODE],
            TryHarder = true,
            CharacterSet = Latin1,
        },
    };

    public static bool[,] Encode(byte[] frame)
    {
        if (frame.Length > FrameSize)
            throw new ArgumentException($"frame is longer than {FrameSize} bytes", nameof(frame));
        var hints = new Dictionary<EncodeHintType, object>
        {
            [EncodeHintType.ERROR_CORRECTION] = ErrorCorrectionLevel.L,
            // No CHARACTER_SET hint: with it ZXing.Net prepends an ECI segment.
            [EncodeHintType.MARGIN] = 0,
            [EncodeHintType.QR_VERSION] = 10,
            [EncodeHintType.QR_MASK_PATTERN] = 0,
        };
        var matrix = new QRCodeWriter().encode(Encoding.Latin1.GetString(frame), BarcodeFormat.QR_CODE, Modules, Modules, hints);
        var grid = new bool[matrix.Width, matrix.Height];
        for (var y = 0; y < matrix.Height; y++)
            for (var x = 0; x < matrix.Width; x++)
                grid[x, y] = matrix[x, y];
        return grid;
    }

    public static Bgra Render(bool[,] matrix, int modulePx = 2, int quiet = 4)
    {
        var side = (matrix.GetLength(0) + 2 * quiet) * modulePx;
        var pixels = new byte[side * side * 4];
        Array.Fill(pixels, (byte)255);
        for (var y = 0; y < matrix.GetLength(1); y++)
            for (var x = 0; x < matrix.GetLength(0); x++)
            {
                if (!matrix[x, y]) continue;
                for (var dy = 0; dy < modulePx; dy++)
                    for (var dx = 0; dx < modulePx; dx++)
                    {
                        var i = (((y + quiet) * modulePx + dy) * side + (x + quiet) * modulePx + dx) * 4;
                        pixels[i] = pixels[i + 1] = pixels[i + 2] = 0;
                    }
            }
        return new Bgra(pixels, side, side);
    }

    public static IReadOnlyList<(byte[] Bytes, MarkPoints Points)> Decode(Bgra image)
    {
        var source = new RGBLuminanceSource(image.Pixels, image.Width, image.Height, RGBLuminanceSource.BitmapFormat.BGRA32);
        Result[]? results;
        lock (Reader)
            results = Reader.DecodeMultiple(source);
        if (results is null) return [];
        return results
            .Where(r => r.ResultPoints is { Length: >= 3 })
            .Select(r => (Bytes(r), new MarkPoints(ToPoint(r.ResultPoints[1]), ToPoint(r.ResultPoints[2]), ToPoint(r.ResultPoints[0]))))
            .ToList();
    }

    static PointF ToPoint(ResultPoint p) => new(p.X, p.Y);

    static byte[] Bytes(Result result)
    {
        if (result.ResultMetadata is not null
            && result.ResultMetadata.TryGetValue(ResultMetadataType.BYTE_SEGMENTS, out var segments)
            && segments is IEnumerable<byte[]> list)
            return list.SelectMany(s => s).ToArray();
        return Encoding.Latin1.GetBytes(result.Text);
    }
}
