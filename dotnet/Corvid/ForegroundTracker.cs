// Copyright (c) 2026 AqueGen. All rights reserved. See LICENSE.
using System.Runtime.InteropServices;

namespace Corvid;

public sealed class ForegroundTracker : IDisposable
{
    private delegate void WinEventProc(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);

    private readonly IReadOnlyCollection<string> _names;
    private readonly bool _anyForeground;
    private readonly int _ownPid;
    private readonly WinEventProc _onForeground;
    private readonly IntPtr _hook;
    private volatile bool _gameInFront;
    private volatile bool _gameFocused;
    private volatile IntPtr _foreground;
    private volatile int _foregroundPid;

    public ForegroundTracker(IReadOnlyCollection<string> names, bool anyForeground, int ownPid)
    {
        _names = names;
        _anyForeground = anyForeground;
        _ownPid = ownPid;
        _onForeground = (_, _, hwnd, _, _, _, _) => Sync(hwnd);
        _hook = Win32Window.SetWinEventHook(Win32Window.EventSystemForeground, Win32Window.EventSystemForeground, IntPtr.Zero, Marshal.GetFunctionPointerForDelegate(_onForeground), 0, 0, Win32Window.WinEventOutOfContext);
        Refresh();
    }

    public bool Hooked => _hook != IntPtr.Zero;

    public bool GameInFront => _gameInFront;

    public bool GameFocused => _gameFocused;

    public IntPtr Foreground => _foreground;

    public int ForegroundPid => _foregroundPid;

    public string? ForegroundName { get; private set; }

    public event Action? Changed;

    public void Refresh() => Sync(Win32Window.GetForegroundWindow());

    internal static bool IsGame(string? processName, bool anyForeground, IReadOnlyCollection<string> names) =>
        anyForeground || processName is not null && names.Contains(processName, StringComparer.OrdinalIgnoreCase);

    internal static bool IsFocused(bool game, int pid, int ownPid) => game || pid == ownPid;

    private void Sync(IntPtr hwnd)
    {
        var pid = Win32Window.ProcessOf(hwnd);
        var name = GameProcess.ProcessName(pid);
        var game = IsGame(name, _anyForeground, _names);
        var focused = IsFocused(game, pid, _ownPid);
        var changed = hwnd != _foreground || game != _gameInFront || focused != _gameFocused;
        _foreground = hwnd;
        _foregroundPid = pid;
        ForegroundName = name;
        _gameInFront = game;
        _gameFocused = focused;
        if (changed) Changed?.Invoke();
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero) Win32Window.UnhookWinEvent(_hook);
    }
}
