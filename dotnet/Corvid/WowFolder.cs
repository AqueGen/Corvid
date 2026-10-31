// Copyright (c) 2026 AqueGen. All rights reserved. See LICENSE.
namespace Corvid;

public static class WowFolder
{
    public static bool IsSafeLocal(string? folder) =>
        !string.IsNullOrWhiteSpace(folder) && Path.IsPathFullyQualified(folder) && !folder.StartsWith(@"\\") && !folder.StartsWith("//") && !folder.Split('\\', '/').Contains("..")
        && Path.GetFileName(Path.TrimEndingDirectorySeparator(folder)) != "";

    public static bool IsRetail(string? folder) =>
        IsSafeLocal(folder) && (Directory.Exists(Path.Combine(folder!, "Interface")) || File.Exists(Path.Combine(folder!, "Wow.exe")));

    public static string AddOns(string folder) => Path.Combine(folder, "Interface", "AddOns");

    public static string? FromPicked(string? picked)
    {
        if (!IsSafeLocal(picked)) return null;
        var folder = Path.TrimEndingDirectorySeparator(picked!);
        if (IsNamed(folder, "AddOns") && Path.GetDirectoryName(folder) is { } interfaceDir && IsNamed(interfaceDir, "Interface")) folder = interfaceDir;
        if (IsNamed(folder, "Interface") && Path.GetDirectoryName(folder) is { } flavour) folder = flavour;
        if (IsFlavour(folder)) return folder;
        var retail = Path.Combine(folder, "_retail_");
        if (IsRetail(retail)) return retail;
        try
        {
            return Directory.EnumerateDirectories(folder, "_*_").FirstOrDefault(IsFlavour);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    public static string? FromRunningGame(IEnumerable<string> names) => FromExePaths(GameProcess.ExePaths(names));

    public static string? FromExePaths(IEnumerable<string> exePaths) =>
        exePaths.Select(Path.GetDirectoryName).FirstOrDefault(IsRetail);

    private static bool IsNamed(string folder, string name) => string.Equals(Path.GetFileName(folder), name, StringComparison.OrdinalIgnoreCase);

    private static bool IsFlavour(string folder) =>
        Path.GetFileName(folder) is ['_', _, .., '_'] && IsRetail(folder);
}
