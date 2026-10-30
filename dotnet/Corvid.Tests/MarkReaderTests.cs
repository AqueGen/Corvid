using ZXing;
using ZXing.QrCode;

namespace Corvid.Tests;

public class MarkReaderTests
{
    const int W = 1280;
    const int H = 720;

    sealed class FakeSource(int originX, int originY) : ICaptureSource
    {
        public int OriginX { get; set; } = originX;
        public bool NullCapture { get; set; }
        public bool ThrowOnCapture { get; set; }
        public Bgra Image { get; set; } = Blank();
        public bool HasClient { get; set; } = true;
        public List<PixelRect> Captures { get; } = [];

        public bool TryGetClient(out PixelRect client)
        {
            client = new PixelRect(OriginX, originY, W, H);
            return HasClient;
        }

        public Bgra? Capture(PixelRect rect)
        {
            Captures.Add(rect);
            if (ThrowOnCapture) throw new InvalidOperationException();
            if (NullCapture) return null;
            var length = rect.Width * rect.Height * 4;
            var pixels = ReuseBuffer && _reused is { } held && held.Pixels.Length == length ? held.Pixels : new byte[length];
            for (var y = 0; y < rect.Height; y++)
                Array.Copy(Image.Pixels, ((rect.Y - originY + y) * W + rect.X - OriginX) * 4, pixels, y * rect.Width * 4, rect.Width * 4);
            var capture = ReferenceEquals(_reused?.Pixels, pixels) ? _reused : new Bgra(pixels, rect.Width, rect.Height);
            if (ReuseBuffer) _reused = capture;
            return capture;
        }

        Bgra? _reused;
        public bool ReuseBuffer { get; set; }
        public byte[]? LastPixels => _reused?.Pixels;
    }

    static Bgra Blank()
    {
        var pixels = new byte[W * H * 4];
        Array.Fill(pixels, (byte)128);
        return new Bgra(pixels, W, H);
    }

    static void Paste(Bgra image, Bgra picture, int x, int y)
    {
        for (var row = 0; row < picture.Height; row++)
            Array.Copy(picture.Pixels, row * picture.Width * 4, image.Pixels, ((y + row) * W + x) * 4, picture.Width * 4);
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
        Paste(source.Image, Mark(1), 300, 200);
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

        Paste(source.Image, Mark(2), 300, 200);
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
        Paste(source.Image, Foreign(), 100, 100);
        Paste(source.Image, Mark(1), 300, 200);
        Paste(source.Image, Foreign(), 700, 400);

        var (read, _) = Reader(source).Step(0);

        Assert.NotNull(read);
        Assert.Equal(1u, read.Frame.Counter);
        Assert.InRange(read.Points.TopLeft.X, 314, 316);
    }

    [Fact]
    public void OnlyForeignCodeLeavesTheReaderSearching()
    {
        var source = new FakeSource(0, 0);
        Paste(source.Image, Foreign(), 100, 100);
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
        Paste(source.Image, Mark(1), 300, 200);
        var reader = Reader(source);
        Assert.NotNull(reader.Step(0).Read);
        var locked = source.Captures.Count;

        source.Image = Blank();
        for (var t = 100; t <= 1400; t += 100) Assert.Null(reader.Step(t).Read);
        Assert.True(reader.Locked);

        Assert.Null(reader.Step(1500).Read);
        Assert.False(reader.Locked);

        var oldRect = source.Captures[locked];
        source.Captures.Clear();
        for (var t = 1600; t <= 3000; t += 100) reader.Step(t);
        var probes = source.Captures.Count(r => r == oldRect);
        Assert.Equal(3, probes);

        Paste(source.Image, Mark(7), 300, 200);
        var relocked = reader.Step(3500).Read;
        Assert.Equal(7u, relocked!.Frame.Counter);
        Assert.True(reader.Locked);
        Assert.Equal(oldRect, source.Captures[^1]);
    }

    [Fact]
    public void IdenticalCaptureSkipsDecoding()
    {
        var source = new FakeSource(0, 0);
        Paste(source.Image, Mark(1), 300, 200);
        var reader = Reader(source);
        reader.Step(0);
        var first = reader.Step(100).Read;
        var decodes = reader.DecodeCount;

        var again = reader.Step(200).Read;

        Assert.Same(first, again);
        Assert.Equal(decodes, reader.DecodeCount);

        Paste(source.Image, Mark(2), 300, 200);
        Assert.Equal(2u, reader.Step(300).Read!.Frame.Counter);
        Assert.Equal(decodes + 1, reader.DecodeCount);
    }

    [Fact]
    public void SourceReusingOneBufferStillSeesChangesAndSkipsUnchanged()
    {
        var source = new FakeSource(0, 0) { ReuseBuffer = true };
        Paste(source.Image, Mark(1), 300, 200);
        var reader = Reader(source);
        reader.Step(0);
        var first = reader.Step(100).Read;
        var buffer = source.LastPixels;
        var decodes = reader.DecodeCount;

        Assert.Same(first, reader.Step(200).Read);
        Assert.Equal(decodes, reader.DecodeCount);
        Assert.Same(buffer, source.LastPixels);

        Paste(source.Image, Mark(2), 300, 200);
        Assert.Equal(2u, reader.Step(300).Read!.Frame.Counter);
        Assert.Equal(decodes + 1, reader.DecodeCount);
        Assert.Same(buffer, source.LastPixels);

        Paste(source.Image, Mark(3), 300, 200);
        Assert.Equal(3u, reader.Step(400).Read!.Frame.Counter);
    }

    [Fact]
    public void CaptureSizeChangeIsDecodedAgainWithReusedSource()
    {
        var source = new FakeSource(0, 0) { ReuseBuffer = true };
        Paste(source.Image, Mark(1), 300, 200);
        var reader = Reader(source);
        reader.Step(0);
        var fullFrame = source.LastPixels;
        var decodes = reader.DecodeCount;

        Assert.Equal(1u, reader.Step(100).Read!.Frame.Counter);

        Assert.NotSame(fullFrame, source.LastPixels);
        Assert.Equal(decodes + 1, reader.DecodeCount);
        Paste(source.Image, Mark(2), 300, 200);
        Assert.Equal(2u, reader.Step(200).Read!.Frame.Counter);
    }

    [Fact]
    public void MissingClientReturnsNothing()
    {
        var source = new FakeSource(0, 0) { HasClient = false };
        var (read, delay) = Reader(source).Step(0);
        Assert.Null(read);
        Assert.Equal(200, delay);
    }

    static MarkReader SlowReader(FakeSource source, int searchMs) =>
        new(source, new MarkReaderOptions(100, _ => searchMs));

    [Fact]
    public void MovedMarkIsFoundBySearchEvenWhenSearchDelayExceedsProbeInterval()
    {
        var source = new FakeSource(0, 0);
        Paste(source.Image, Mark(1), 300, 200);
        var reader = SlowReader(source, 2000);
        Assert.NotNull(reader.Step(0).Read);

        source.Image = Blank();
        Assert.Null(reader.Step(2000).Read);
        Assert.False(reader.Locked);
        Paste(source.Image, Mark(2), 800, 400);
        MarkRead? found = null;
        for (var t = 4000; found is null && t <= 12000; t += 2000) found = reader.Step(t).Read;

        Assert.Equal(2u, found!.Frame.Counter);
        Assert.InRange(found.Points.TopLeft.X, 814, 816);
        Assert.True(reader.Locked);
    }

    [Fact]
    public void ClientRectChangeClearsRememberedRect()
    {
        var source = new FakeSource(0, 0);
        Paste(source.Image, Mark(1), 300, 200);
        var reader = Reader(source);
        reader.Step(0);
        reader.Step(100);
        var oldRect = source.Captures[^1];

        source.OriginX = 50;
        source.Captures.Clear();
        var read = reader.Step(200).Read;

        Assert.Equal(new PixelRect(50, 0, W, H), source.Captures[0]);
        Assert.DoesNotContain(oldRect, source.Captures);
        Assert.Equal(1u, read!.Frame.Counter);
        Assert.InRange(read.Points.TopLeft.X, 314, 316);
    }

    [Fact]
    public void ClientThatDisappearsAndReturnsIsSearchedAgain()
    {
        var source = new FakeSource(0, 0);
        Paste(source.Image, Mark(1), 300, 200);
        var reader = Reader(source);
        reader.Step(0);
        reader.Step(100);

        source.HasClient = false;
        source.Captures.Clear();
        var gone = reader.Step(200);
        Assert.Null(gone.Read);
        Assert.False(reader.Locked);
        Assert.Empty(source.Captures);

        source.HasClient = true;
        Assert.NotNull(reader.Step(1000).Read);
        Assert.Equal(new PixelRect(0, 0, W, H), source.Captures[0]);
        Assert.True(reader.Locked);
    }

    [Fact]
    public void NextDelayCoversLockedFailureAndProbeSteps()
    {
        var source = new FakeSource(0, 0);
        Paste(source.Image, Mark(1), 300, 200);
        var reader = SlowReader(source, 5000);
        reader.Step(0);

        source.Image = Blank();
        Assert.Equal(100, reader.Step(100).NextDelayMs);
        for (var t = 200; t <= 1400; t += 100) reader.Step(t);
        Assert.Equal(500, reader.Step(1500).NextDelayMs);
        Assert.Equal(500, reader.Step(2000).NextDelayMs);
        Assert.Equal(500, reader.Step(2500).NextDelayMs);
    }

    [Fact]
    public void FailedAndThrowingCaptureCountAsFailedReads()
    {
        var source = new FakeSource(0, 0);
        Paste(source.Image, Mark(1), 300, 200);
        var reader = Reader(source);

        source.NullCapture = true;
        Assert.Equal((null, 200), reader.Step(0));
        source.NullCapture = false;
        source.ThrowOnCapture = true;
        Assert.Equal((null, 400), reader.Step(1000));
        Assert.False(reader.Locked);

        source.ThrowOnCapture = false;
        Assert.NotNull(reader.Step(3000).Read);
        source.ThrowOnCapture = true;
        Assert.Null(reader.Step(3100).Read);
        Assert.True(reader.Locked);
    }

    [Fact]
    public void FrozenCaptureKeepsTheLockAndReturnsTheSameRead()
    {
        var source = new FakeSource(0, 0);
        Paste(source.Image, Mark(1), 300, 200);
        var reader = Reader(source);
        reader.Step(0);
        var first = reader.Step(100).Read;

        for (var t = 200; t <= 10000; t += 200) Assert.Same(first, reader.Step(t).Read);
        Assert.True(reader.Locked);
    }
}
