namespace Corvid.Tests;

public class QrPictureTests
{
    public static IEnumerable<object[]> Blocks()
    {
        var lines = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Fixtures", "qr-v10.txt"));
        for (var i = 0; i + 57 < lines.Length; i += 58)
            yield return new object[] { lines[i], string.Join("\n", lines.Skip(i + 1).Take(57)) };
    }

    [Theory]
    [MemberData(nameof(Blocks))]
    public void Encode_MatchesFixture(string hex, string rows)
    {
        var matrix = QrPicture.Encode(Convert.FromHexString(hex));
        var lines = rows.Split('\n');
        for (var y = 0; y < 57; y++)
            for (var x = 0; x < 57; x++)
                Assert.True((lines[y][x] == '1') == matrix[x, y], $"x={x} y={y}");
    }

    [Theory]
    [MemberData(nameof(Blocks))]
    public void RenderDecode_ReturnsInput(string hex, string rows)
    {
        Assert.NotEmpty(rows);
        var input = Convert.FromHexString(hex);
        var found = QrPicture.Decode(QrPicture.Render(QrPicture.Encode(input)));
        Assert.Equal(input, Assert.Single(found).Bytes);
    }

    [Fact]
    public void Decode_ReturnsThreeReferencePoints()
    {
        var found = QrPicture.Decode(QrPicture.Render(QrPicture.Encode(new byte[268])));
        var p = Assert.Single(found).Points;
        Assert.InRange(p.TopLeft.X, 14, 16);
        Assert.InRange(p.TopLeft.Y, 14, 16);
        Assert.InRange(p.TopRight.X, 114, 116);
        Assert.InRange(p.TopRight.Y, 14, 16);
        Assert.InRange(p.BottomLeft.X, 14, 16);
        Assert.InRange(p.BottomLeft.Y, 114, 116);
    }

    [Fact]
    public void Encode_RejectsOversizedFrame() =>
        Assert.Throws<ArgumentException>(() => QrPicture.Encode(new byte[269]));
}
