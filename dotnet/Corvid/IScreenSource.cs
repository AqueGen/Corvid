namespace Corvid;

public interface IScreenSource
{
    bool TryGetClient(out PixelRect client);

    Bgra? Capture(PixelRect rect);
}
