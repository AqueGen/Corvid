// Copyright (c) 2026 AqueGen. All rights reserved. See LICENSE.
using System.Diagnostics.CodeAnalysis;

namespace Corvid;

public static class WowFolder
{
    public static bool IsSafeLocal([NotNullWhen(true)] string? folder) =>
        !string.IsNullOrWhiteSpace(folder) && Path.IsPathFullyQualified(folder) && !folder.StartsWith(@"\\") && !folder.StartsWith("//") && !folder.Split('\\', '/').Contains("..")
        && Path.GetFileName(Path.TrimEndingDirectorySeparator(folder)) != "";

    public static bool IsRetail([NotNullWhen(true)] string? folder) =>
        IsSafeLocal(folder) && (Directory.Exists(Path.Combine(folder, "Interface")) || File.Exists(Path.Combine(folder, "Wow.exe")));

    public static string AddOns(string folder) => Path.Combine(folder, "Interface", "AddOns");

    public static string? FromPicked(string? picked)
    {
        if (!IsSafeLocal(picked)) return null;
        var folder = Path.TrimEndingDirectorySeparator(picked);
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

    public static IReadOnlyList<string> Flavours(params string?[] picks)
    {
        var folders = new List<string>();
        foreach (var pick in picks)
        {
            if ((FromPicked(pick) ?? ExeFolder(pick)) is not { } folder) continue;
            if (!IsFlavourName(folder) || Path.GetDirectoryName(folder) is not { } root)
            {
                folders.Add(folder);
                continue;
            }
            try
            {
                folders.AddRange(Directory.EnumerateDirectories(root, "_*_").Where(f => IsClientName(f) && IsRetail(f)));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
            {
            }
        }
        return folders.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static string? RootOf(string? path)
    {
        if (!IsSafeLocal(path)) return null;
        var folder = Path.TrimEndingDirectorySeparator(path);
        if (IsNamed(folder, "AddOns") && Path.GetDirectoryName(folder) is { } interfaceDir && IsNamed(interfaceDir, "Interface")) folder = interfaceDir;
        if (IsNamed(folder, "Interface") && Path.GetDirectoryName(folder) is { } client) folder = client;
        if (IsFlavourName(folder) && Path.GetDirectoryName(folder) is { } root && IsSafeLocal(root)) folder = root;
        return IsSafeLocal(folder) ? folder : null;
    }

    public static string? Root(string? picked) => RootOf(picked) is { } root && Clients(root).Count > 0 ? root : null;

    public static IReadOnlyList<string> Clients(string? root)
    {
        if (!IsSafeLocal(root)) return [];
        List<string> children;
        try
        {
            children = Directory.EnumerateDirectories(root, "_*_").Where(f => IsClientName(f) && IsRetail(f)).Order(StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            children = [];
        }
        if (children.Count > 0) return children;
        return IsRetail(root) && (IsFlavourName(root) || File.Exists(Path.Combine(root, "Wow.exe"))) ? [root] : [];
    }

    public static IReadOnlyList<string> Clients(string? root, string? running)
    {
        var clients = Clients(root);
        if (!IsRetail(running)) return clients;
        var parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(running));
        var underRoot = root is not null && string.Equals(parent, Path.TrimEndingDirectorySeparator(root), StringComparison.OrdinalIgnoreCase);
        if (underRoot && !IsClientName(running)) return clients;
        return clients.Append(Path.TrimEndingDirectorySeparator(running)).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static bool IsClientNameSafe([NotNullWhen(true)] string? name) =>
        !string.IsNullOrEmpty(name) && name is not ("." or "..") && name.IndexOfAny([.. Path.GetInvalidFileNameChars(), '\\', '/']) < 0;

    public static string ClientName(string folder) => Path.GetFileName(Path.TrimEndingDirectorySeparator(folder)).ToLowerInvariant();

    public static string ClientLabel(string folder) => ClientName(folder) switch
    {
        "_retail_" => "Retail",
        "_forever_" => "Forever",
        "_ptr_" => "PTR",
        "_beta_" => "Beta",
        _ => Path.GetFileName(Path.TrimEndingDirectorySeparator(folder)),
    };

    public static IReadOnlyList<string> Enabled(IEnumerable<string> clients, IReadOnlyCollection<string> disabled) =>
        clients.Where(c => !disabled.Contains(ClientName(c), StringComparer.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList();

    public static string? FromRunningGame(IEnumerable<string> names) => FromExePaths(GameProcess.ExePaths(names));

    public static string? FromExePaths(IEnumerable<string> exePaths) =>
        exePaths.Select(Path.GetDirectoryName).FirstOrDefault(IsRetail);

    private static bool IsNamed(string folder, string name) => string.Equals(Path.GetFileName(folder), name, StringComparison.OrdinalIgnoreCase);

    private static string? ExeFolder(string? pick)
    {
        if (!IsSafeLocal(pick)) return null;
        try
        {
            var folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(pick));
            if (IsNamed(folder, "AddOns") && Path.GetDirectoryName(folder) is { } interfaceDir && IsNamed(interfaceDir, "Interface")) folder = interfaceDir;
            if (IsNamed(folder, "Interface") && Path.GetDirectoryName(folder) is { } parent) folder = parent;
            return File.Exists(Path.Combine(folder, "Wow.exe")) ? folder : null;
        }
        catch (Exception e) when (e is IOException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private static bool IsFlavourName(string folder) => Path.GetFileName(folder) is ['_', _, .., '_'];

    private static bool IsClientName(string folder) =>
        new[] { "_retail_", "_ptr_", "_beta_", "_forever_" }.Contains(Path.GetFileName(folder), StringComparer.OrdinalIgnoreCase);

    private static bool IsFlavour(string folder) => IsFlavourName(folder) && IsRetail(folder);
}
