// Copyright (c) 2026 AqueGen. All rights reserved. See LICENSE.
using System.Drawing;

namespace Corvid;

public sealed record Bgra(byte[] Pixels, int Width, int Height);

public readonly record struct PixelRect(int X, int Y, int Width, int Height);

public readonly record struct MarkPoints(PointF TopLeft, PointF TopRight, PointF BottomLeft);
