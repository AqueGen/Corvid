using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Corvid.Tests;

public sealed class SandboxShotFactAttribute : FactAttribute
{
    public SandboxShotFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CORVID_SANDBOX_SHOT")))
        {
            Skip = "CORVID_SANDBOX_SHOT is not set.";
        }
    }
}

public class SandboxShotTests
{
    const string KeyHex = "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f";

    static Bgra Load(string path)
    {
        var frame = BitmapFrame.Create(new Uri(path), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        var bgra = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[bgra.PixelWidth * bgra.PixelHeight * 4];
        bgra.CopyPixels(pixels, bgra.PixelWidth * 4, 0);
        return new Bgra(pixels, bgra.PixelWidth, bgra.PixelHeight);
    }

    [SandboxShotFact]
    public void RenderedMarkOpensWithTheTestKey()
    {
        var path = Environment.GetEnvironmentVariable("CORVID_SANDBOX_SHOT")!;

        var found = QrPicture.Decode(Load(path));

        var bytes = Assert.Single(found).Bytes;
        Assert.True(Frame.TryParse(bytes, out var frame));
        var result = frame.Open(9, ChannelKey.FromHex(KeyHex));
        Assert.Equal(OpenStatus.Ok, result.Status);
        Assert.Equal("probe", Encoding.UTF8.GetString(result.Messages.Single(m => m.Type == 0).Body));
        Assert.Equal("hello corvid", Encoding.UTF8.GetString(result.Messages.Single(m => m.Type == 1).Body));
    }
}
