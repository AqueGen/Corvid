// Copyright (c) 2026 AqueGen. All rights reserved. See LICENSE.
using System.Runtime.InteropServices;

namespace Corvid;

public static partial class Win32Window
{
    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x20;
    private const int WsExToolWindow = 0x80;
    private const int WsExLayered = 0x80000;
    private const int WsExNoActivate = 0x8000000;
    private const uint WdaExcludeFromCapture = 0x11;
    private const uint WdaNone = 0;
    internal const uint EventSystemForeground = 3;
    internal const uint WinEventOutOfContext = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static partial int GetWindowLong(IntPtr hwnd, int index);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static partial int SetWindowLong(IntPtr hwnd, int index, int value);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);

    [LibraryImport("user32.dll")]
    internal static partial IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr module, IntPtr callback, uint processId, uint threadId, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool UnhookWinEvent(IntPtr hook);

    [LibraryImport("user32.dll")]
    public static partial IntPtr GetForegroundWindow();

    [LibraryImport("user32.dll")]
    public static partial uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPos(out Point point);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRect(IntPtr hwnd, out Rect rect);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ClientToScreen(IntPtr hwnd, ref Point point);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetClientRect(IntPtr hwnd, out Rect rect);

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    public static bool SetCaptureVisible(IntPtr hwnd, bool visible) => SetWindowDisplayAffinity(hwnd, visible ? WdaNone : WdaExcludeFromCapture);

    public static bool ExcludeFromCapture(IntPtr hwnd) => SetWindowDisplayAffinity(hwnd, WdaExcludeFromCapture);

    public static int ProcessOf(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return 0;
        GetWindowThreadProcessId(hwnd, out var pid);
        return (int)pid;
    }

    public static (int X, int Y)? CursorPos() => GetCursorPos(out var p) ? (p.X, p.Y) : null;

    public static PixelRect? WindowArea(IntPtr hwnd) =>
        hwnd != IntPtr.Zero && GetWindowRect(hwnd, out var r) ? new PixelRect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top) : null;

    public static PixelRect? ClientArea(IntPtr hwnd)
    {
        var origin = new Point();
        if (hwnd == IntPtr.Zero || !GetClientRect(hwnd, out var r) || !ClientToScreen(hwnd, ref origin)) return null;
        return new PixelRect(origin.X, origin.Y, r.Right - r.Left, r.Bottom - r.Top);
    }

    public static void SetClickThrough(IntPtr hwnd, bool on, bool activatable = false)
    {
        int style = GetWindowLong(hwnd, GwlExStyle) | WsExLayered | WsExToolWindow;
        style = activatable ? style & ~WsExNoActivate : style | WsExNoActivate;
        SetWindowLong(hwnd, GwlExStyle, on ? style | WsExTransparent : style & ~WsExTransparent);
    }

    public static void UseDarkTitleBar(IntPtr hwnd, byte r, byte g, byte b)
    {
        const int UseImmersiveDarkMode = 20;
        const int CaptionColor = 35;
        var on = 1;
        DwmSetWindowAttribute(hwnd, UseImmersiveDarkMode, ref on, sizeof(int));
        var color = r | g << 8 | b << 16;
        DwmSetWindowAttribute(hwnd, CaptionColor, ref color, sizeof(int));
    }
}
