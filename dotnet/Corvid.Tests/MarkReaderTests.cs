using ZXing;
using ZXing.QrCode;

namespace Corvid.Tests;

public class MarkReaderTests
{
    const int W = 1280;
    const int H = 720;

    sealed class FakeSource(int originX, int originY) : IScreenSource
    {
        public Bgra Screen { get; set; } = Blank();
        public bool HasClient { get; set; } = true;
        public List<PixelRect> Captures { get; } = [];

        public bool TryGetClient(out PixelRect client)
        {
            client = new PixelRect(originX, originY, W, H);
            return HasClient;
        }

        public Bgra? Capture(PixelRect rect)
        {
            Captures.Add(rect);
            var pixels = new byte[rect.Width * rect.Height * 4];
            for (var y = 0; y < rect.Height; y++)
                Array.Copy(Screen.Pixels, ((rect.Y - originY + y) * W + rect.X - originX) * 4, pixels, y * rect.Width * 4, rect.Width * 4);
            return new Bgra(pixels, rect.Width, rect.Height);
        }
    }

    static Bgra Blank()
    {
        var pixels = new byte[W * H * 4];
        Array.Fill(pixels, (byte)128);
        return new Bgra(pixels, W, H);
    }

    static void Paste(Bgra screen, Bgra picture, int x, int y)
    {
        for (var row = 0; row < picture.Height; row++)
            Array.Copy(picture.Pixels, row * picture.Width * 4, screen.Pixels, ((y + row) * W + x) * 4, picture.Width * 4);
    }

    static Bgra Mark(uint counter)
    {
        var key = ChannelKey.FromHex(new string('a', 64));
        var frame = FrameWriter.Write(new byte[8], counter, [(1, key, [new Message(0, [1])])]);
        return QrPicture.Render(QrPicture.Encode(frame));
    }

    static Bgra Foreign()
    {
        var hints = new Dictionary<EncodeHintType, object> { [EncodeHintType.MARGIN] = 0 };
        var m = new QRCodeWriter().encode("hello", BarcodeFormat.QR_CODE, 25, 25, hints);
        var grid = new bool[m.Width, m.Height];
        for (var y = 0; y < m.Height; y++)
            for (var x = 0; x < m.Width; x++)
                grid[x, y] = m[x, y];
        return QrPicture.Render(grid, 3);
    }

    static MarkReader Reader(FakeSource source) =>
        new(source, new MarkReaderOptions(100, n => 200 * (n + 1)));

    [Fact]
    public void FirstStepSearchesThenLockedStepReadsTheCrop()
    {
        var source = new FakeSource(100, 50);
        Paste(source.Screen, Mark(1), 300, 200);
        var reader = Reader(source);

        var (read, _) = reader.Step(0);

        Assert.NotNull(read);
        Assert.True(reader.Locked);
        Assert.Equal(1u, read.Frame.Counter);
        Assert.InRange(read.Points.TopLeft.X, 314, 316);
        Assert.InRange(read.Points.TopLeft.Y, 214, 216);
        Assert.InRange(read.Points.TopRight.X, 414, 416);
        Assert.InRange(read.Points.BottomLeft.Y, 314, 316);
        Assert.Equal(new PixelRect(100, 50, W, H), source.Captures[0]);

        Paste(source.Screen, Mark(2), 300, 200);
        var (next, delay) = reader.Step(100);

        Assert.Equal(2u, next!.Frame.Counter);
        Assert.Equal(100, delay);
        Assert.InRange(next.Points.TopLeft.X, 314, 316);
        var crop = source.Captures[1];
        Assert.True(crop.Width < 400 && crop.Height < 400);
        Assert.True(crop.X <= 400 && crop.Y <= 250);
    }

    [Fact]
    public void ForeignCodeIsIgnored()
    {
        var source = new FakeSource(0, 0);
        Paste(source.Screen, Foreign(), 100, 100);
        Paste(source.Screen, Mark(1), 300, 200);
        Paste(source.Screen, Foreign(), 700, 400);

        var (read, _) = Reader(source).Step(0);

        Assert.NotNull(read);
        Assert.Equal(1u, read.Frame.Counter);
        Assert.InRange(read.Points.TopLeft.X, 314, 316);
    }

    [Fact]
    public void OnlyForeignCodeLeavesTheReaderSearching()
    {
        var source = new FakeSource(0, 0);
        Paste(source.Screen, Foreign(), 100, 100);
        var reader = Reader(source);

        var (read, delay) = reader.Step(0);

        Assert.Null(read);
        Assert.False(reader.Locked);
        Assert.Equal(200, delay);
        Assert.Equal(400, reader.Step(1000).NextDelayMs);
    }

    [Fact]
    public void LockIsLostAfterLockLostMsAndOldRectIsProbed()
    {
        var source = new FakeSource(0, 0);
        Paste(source.Screen, Mark(1), 300, 200);
        var reader = Reader(source);
        Assert.NotNull(reader.Step(0).Read);
        var locked = source.Captures.Count;

        source.Screen = Blank();
        for (var t = 100; t <= 1400; t += 100) Assert.Null(reader.Step(t).Read);
        Assert.True(reader.Locked);

        Assert.Null(reader.Step(1500).Read);
        Assert.False(reader.Locked);

        var oldRect = source.Captures[locked];
        source.Captures.Clear();
        for (var t = 1600; t <= 3000; t += 100) reader.Step(t);
        var probes = source.Captures.Count(r => r == oldRect);
        Assert.Equal(3, probes);

        Paste(source.Screen, Mark(7), 300, 200);
        var relocked = reader.Step(3500).Read;
        Assert.Equal(7u, relocked!.Frame.Counter);
        Assert.True(reader.Locked);
        Assert.Equal(oldRect, source.Captures[^1]);
    }

    [Fact]
    public void IdenticalCaptureSkipsDecoding()
    {
        var source = new FakeSource(0, 0);
        Paste(source.Screen, Mark(1), 300, 200);
        var reader = Reader(source);
        reader.Step(0);
        var first = reader.Step(100).Read;
        var decodes = reader.DecodeCount;

        var again = reader.Step(200).Read;

        Assert.Same(first, again);
        Assert.Equal(decodes, reader.DecodeCount);

        Paste(source.Screen, Mark(2), 300, 200);
        Assert.Equal(2u, reader.Step(300).Read!.Frame.Counter);
        Assert.Equal(decodes + 1, reader.DecodeCount);
    }

    [Fact]
    public void MissingClientReturnsNothing()
    {
        var source = new FakeSource(0, 0) { HasClient = false };
        var (read, delay) = Reader(source).Step(0);
        Assert.Null(read);
        Assert.Equal(200, delay);
    }
}
