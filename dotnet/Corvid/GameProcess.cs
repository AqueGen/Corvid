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

    public static int[] Ids(IEnumerable<string> names)
    {
        var ids = new List<int>();
        foreach (var name in names)
        {
            foreach (var process in Process.GetProcessesByName(name))
            {
                using (process) ids.Add(process.Id);
            }
        }
        ids.Sort();
        return [.. ids];
    }

    public static List<string> ExePaths(IEnumerable<string> names)
    {
        var found = new List<(DateTime Start, string Path)>();
        foreach (var name in names)
        {
            foreach (var process in Process.GetProcessesByName(name))
            {
                using (process)
                {
                    try
                    {
                        if (process.MainModule?.FileName is { } path) found.Add((process.StartTime, path));
                    }
                    catch (Exception e) when (e is Win32Exception or InvalidOperationException)
                    {
                    }
                }
            }
        }
        return NewestFirst(found);
    }

    internal static List<string> NewestFirst(IEnumerable<(DateTime Start, string Path)> found) =>
        found.OrderByDescending(f => f.Start).Select(f => f.Path).ToList();

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
