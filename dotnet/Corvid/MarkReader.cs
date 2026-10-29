namespace Corvid;

public sealed record MarkReaderOptions(int LockedEveryMs, Func<int, int> SearchDelayMs, int LockLostMs = 1500, int RelockProbeMs = 500);

public sealed record MarkRead(Frame Frame, byte[] Bytes, MarkPoints Points);

public sealed class MarkReader(IScreenSource source, MarkReaderOptions options)
{
    PixelRect? _locked;
    PixelRect? _lastLocked;
    long _lastValidMs;
    long _nextProbeMs;
    int _failedSearches;
    PixelRect _prevRect;
    Bgra? _prevCapture;
    MarkRead? _prevRead;
    PixelRect _prevMark;

    public bool Locked => _locked is not null;

    public int DecodeCount { get; private set; }

    public (MarkRead? Read, int NextDelayMs) Step(long nowMs)
    {
        if (!source.TryGetClient(out var client))
        {
            Unlock();
            return (null, SearchDelay());
        }

        if (_locked is { } rect)
        {
            if (Read(rect, client) is { } read)
            {
                _lastValidMs = nowMs;
                return (read, options.LockedEveryMs);
            }
            if (nowMs - _lastValidMs < options.LockLostMs)
            {
                return (null, options.LockedEveryMs);
            }
            Unlock();
            _nextProbeMs = nowMs + options.RelockProbeMs;
        }

        var probing = _lastLocked is not null && nowMs >= _nextProbeMs;
        if (probing) _nextProbeMs = nowMs + options.RelockProbeMs;
        var found = Read(probing ? _lastLocked!.Value : client, client);
        if (found is null) return (null, SearchDelay());

        _lastValidMs = nowMs;
        _failedSearches = 0;
        Lock(client);
        return (found, options.LockedEveryMs);
    }

    int SearchDelay() => options.SearchDelayMs(_failedSearches++);

    void Unlock()
    {
        _lastLocked = _locked ?? _lastLocked;
        _locked = null;
    }

    MarkRead? Read(PixelRect rect, PixelRect client)
    {
        var capture = source.Capture(rect);
        if (capture is null) return null;
        if (rect == _prevRect && _prevCapture is { } prev && prev.Pixels.AsSpan().SequenceEqual(capture.Pixels))
        {
            return _prevRead;
        }

        DecodeCount++;
        _prevRect = rect;
        _prevCapture = capture;
        _prevRead = null;
        foreach (var (bytes, p) in QrPicture.Decode(capture))
        {
            if (!Frame.TryParse(bytes, out var frame)) continue;
            var dx = rect.X - client.X;
            var dy = rect.Y - client.Y;
            var shifted = new MarkPoints(
                new(p.TopLeft.X + dx, p.TopLeft.Y + dy),
                new(p.TopRight.X + dx, p.TopRight.Y + dy),
                new(p.BottomLeft.X + dx, p.BottomLeft.Y + dy));
            _prevRead = new MarkRead(frame, bytes, shifted);
            _prevMark = MarkRect(p, rect);
            break;
        }
        return _prevRead;
    }

    static PixelRect MarkRect(MarkPoints p, PixelRect capture)
    {
        var scale = (p.TopRight.X - p.TopLeft.X) / 50f;
        var side = (int)MathF.Round(65 * scale);
        return new PixelRect(
            capture.X + (int)MathF.Round(p.TopLeft.X - 7.5f * scale),
            capture.Y + (int)MathF.Round(p.TopLeft.Y - 7.5f * scale),
            side,
            side);
    }

    void Lock(PixelRect client)
    {
        var mark = _prevMark;
        var pad = Math.Max(mark.Width, mark.Height) / 2;
        var left = Math.Max(client.X, mark.X - pad);
        var top = Math.Max(client.Y, mark.Y - pad);
        var right = Math.Min(client.X + client.Width, mark.X + mark.Width + pad);
        var bottom = Math.Min(client.Y + client.Height, mark.Y + mark.Height + pad);
        if (right <= left || bottom <= top) return;
        _locked = new PixelRect(left, top, right - left, bottom - top);
    }
}
