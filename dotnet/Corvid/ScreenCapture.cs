// Copyright (c) 2026 AqueGen. All rights reserved. See LICENSE.
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Corvid;

public static partial class ScreenCapture
{
    private const uint SrcCopy = 0x00CC0020;

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ClrUsed;
        public uint ClrImportant;
    }

    [LibraryImport("user32.dll")]
    private static partial IntPtr GetDC(IntPtr hwnd);

    [LibraryImport("user32.dll")]
    private static partial int ReleaseDC(IntPtr hwnd, IntPtr hdc);

    [LibraryImport("gdi32.dll")]
    private static partial IntPtr CreateCompatibleDC(IntPtr hdc);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteDC(IntPtr hdc);

    [LibraryImport("gdi32.dll", SetLastError = true)]
    private static partial IntPtr CreateDIBSection(IntPtr hdc, ref BitmapInfoHeader info, uint usage, out IntPtr bits, IntPtr section, uint offset);

    [LibraryImport("gdi32.dll")]
    private static partial IntPtr SelectObject(IntPtr hdc, IntPtr obj);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(IntPtr obj);

    [LibraryImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool BitBlt(IntPtr dest, int x, int y, int width, int height, IntPtr src, int srcX, int srcY, uint rop);

    public static byte[] Capture(PixelRect region)
    {
        var bgra = new byte[Math.Max(region.Width, 0) * Math.Max(region.Height, 0) * 4];
        Capture(region, bgra);
        return bgra;
    }

    public static void Capture(PixelRect region, byte[] bgra)
    {
        if (region.Width <= 0 || region.Height <= 0)
            throw new ArgumentException($"capture region has no area: {region.Width}x{region.Height}", nameof(region));

        var screen = GetDC(IntPtr.Zero);
        if (screen == IntPtr.Zero) throw new Win32Exception("GetDC failed");
        var memory = CreateCompatibleDC(screen);
        var info = new BitmapInfoHeader
        {
            Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
            Width = region.Width,
            Height = -region.Height,
            Planes = 1,
            BitCount = 32,
        };
        var dib = CreateDIBSection(screen, ref info, 0, out var bits, IntPtr.Zero, 0);
        try
        {
            if (memory == IntPtr.Zero || dib == IntPtr.Zero) throw new Win32Exception("CreateDIBSection failed");
            var old = SelectObject(memory, dib);
            var copied = BitBlt(memory, 0, 0, region.Width, region.Height, screen, region.X, region.Y, SrcCopy);
            SelectObject(memory, old);
            if (!copied) throw new Win32Exception();

            var length = region.Width * region.Height * 4;
            Marshal.Copy(bits, bgra, 0, length);
            for (var i = 3; i < length; i += 4) bgra[i] = 0xFF;
        }
        finally
        {
            if (dib != IntPtr.Zero) DeleteObject(dib);
            if (memory != IntPtr.Zero) DeleteDC(memory);
            ReleaseDC(IntPtr.Zero, screen);
        }
    }
}
