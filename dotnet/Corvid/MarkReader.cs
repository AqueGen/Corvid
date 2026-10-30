namespace Corvid;

public sealed record MarkReaderOptions(int LockedEveryMs, Func<int, int> SearchDelayMs, int LockLostMs = 1500, int RelockProbeMs = 500);

public sealed record MarkRead(Frame Frame, byte[] Bytes, MarkPoints Points);

public sealed class MarkReader(ICaptureSource source, MarkReaderOptions options)
{
    PixelRect? _locked;
    PixelRect? _lastLocked;
    long _lastValidMs;
    long _nextProbeMs;
    long _nextSearchMs;
    PixelRect? _client;
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
            Forget();
            return (null, SearchDelay(nowMs));
        }
        if (_client != client)
        {
            Forget();
            _client = client;
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
            _lastLocked = rect;
            _locked = null;
            _nextProbeMs = nowMs + options.RelockProbeMs;
        }

        if (_lastLocked is { } last && nowMs >= _nextProbeMs)
        {
            _nextProbeMs = nowMs + options.RelockProbeMs;
            if (Read(last, client) is { } probed)
            {
                return (Accept(probed, client, nowMs), options.LockedEveryMs);
            }
        }

        if (nowMs >= _nextSearchMs)
        {
            if (Read(client, client) is { } found)
            {
                return (Accept(found, client, nowMs), options.LockedEveryMs);
            }
            return (null, SearchDelay(nowMs));
        }
        return (null, Remaining(nowMs));
    }

    MarkRead Accept(MarkRead read, PixelRect client, long nowMs)
    {
        _lastValidMs = nowMs;
        _failedSearches = 0;
        _nextSearchMs = 0;
        Lock(client);
        return read;
    }

    int SearchDelay(long nowMs)
    {
        var delay = options.SearchDelayMs(_failedSearches++);
        _nextSearchMs = nowMs + delay;
        return Remaining(nowMs);
    }

    int Remaining(long nowMs)
    {
        var wait = _nextSearchMs - nowMs;
        if (_lastLocked is not null) wait = Math.Min(wait, _nextProbeMs - nowMs);
        return (int)Math.Max(0, wait);
    }

    void Forget()
    {
        _locked = null;
        _lastLocked = null;
        _prevCapture = null;
        _failedSearches = 0;
        _nextSearchMs = 0;
    }

    MarkRead? Read(PixelRect rect, PixelRect client)
    {
        Bgra? capture;
        try
        {
            capture = source.Capture(rect);
        }
        catch (Exception)
        {
            return null;
        }
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
