// Copyright (c) 2026 AqueGen. All rights reserved. See LICENSE.
namespace Corvid;

public interface ICaptureSource
{
    bool TryGetClient(out PixelRect client);

    Bgra? Capture(PixelRect rect);
}
