namespace Corvid;

public interface ICaptureSource
{
    bool TryGetClient(out PixelRect client);

    Bgra? Capture(PixelRect rect);
}
