// Copyright (c) 2026 AqueGen. All rights reserved. See LICENSE.
using System.ComponentModel;
using System.Diagnostics;

namespace Corvid;

public static class GameProcess
{
    public static IReadOnlyList<string> Default { get; } = ["Wow", "WowT", "WowB"];

    public static bool IsRunning(IEnumerable<string> names) => names.Any(name =>
    {
        var processes = Process.GetProcessesByName(name);
        foreach (var process in processes) process.Dispose();
        return processes.Length > 0;
    });

    public static List<string> ExePaths(IEnumerable<string> names)
    {
        var paths = new List<string>();
        foreach (var name in names)
        {
            foreach (var process in Process.GetProcessesByName(name))
            {
                using (process)
                {
                    try
                    {
                        if (process.MainModule?.FileName is { } path) paths.Add(path);
                    }
                    catch (Exception e) when (e is Win32Exception or InvalidOperationException)
                    {
                    }
                }
            }
        }
        return paths;
    }

    public static string? ProcessName(int pid)
    {
        if (pid == 0) return null;
        try
        {
            using var process = Process.GetProcessById(pid);
            return process.ProcessName;
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }
}
