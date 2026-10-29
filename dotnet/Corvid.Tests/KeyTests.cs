using System.Text;

namespace Corvid.Tests;

public class KeyTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "corvid-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    [Fact]
    public void LoadOrCreate_RoundTripsAndCreatesDirectories()
    {
        var path = Path.Combine(_dir, "a", "b", "key.dat");
        var first = KeyStore.LoadOrCreate(path);
        var second = KeyStore.LoadOrCreate(path);
        Assert.Equal(first.ToHex(), second.ToHex());
        Assert.DoesNotContain(first.ToHex(), File.ReadAllText(path));
    }

    [Fact]
    public void LoadOrCreate_ReplacesCorruptFile()
    {
        var path = Path.Combine(_dir, "key.dat");
        Directory.CreateDirectory(_dir);
        File.WriteAllText(path, "not base64 !!");
        var key = KeyStore.LoadOrCreate(path);
        Assert.Equal(key.ToHex(), KeyStore.LoadOrCreate(path).ToHex());

        File.WriteAllText(path, Convert.ToBase64String([1, 2, 3]));
        Assert.Equal(64, KeyStore.LoadOrCreate(path).ToHex().Length);
    }

    [Fact]
    public void AddonKeyFile_WritesExactContent()
    {
        var key = ChannelKey.Generate();
        AddonKeyFile.Write(Path.Combine(_dir, "addon"), key);
        var bytes = File.ReadAllBytes(Path.Combine(_dir, "addon", "CorvidKey.lua"));
        Assert.Equal("local _, ns = ...\nns.corvidKey = \"" + key.ToHex() + "\"\n", Encoding.UTF8.GetString(bytes));
        Assert.NotEqual(0xEF, bytes[0]);
    }
}
