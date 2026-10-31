// Copyright (c) 2026 AqueGen. All rights reserved. See LICENSE.
using System.Security.Cryptography;
using System.Text;

namespace Corvid;

public enum AddonSyncResult { Installed, UpToDate, Synced, Updated, UpdatedFiles, Failed }

public static class AddonSync
{
    private static readonly object Gate = new();

    public static AddonSyncResult Sync(string bundledDir, string addOnsDir, Func<ChannelKey?> key, IReadOnlyCollection<string>? appFiles = null)
    {
        try
        {
            lock (Gate)
            {
                var target = Target(bundledDir, addOnsDir);
                var toc = Path.GetFileName(target) + ".toc";
                if (Version(Path.Combine(bundledDir, toc)) is not { } bundled) return AddonSyncResult.Failed;
                if (!Exists(target) && Exists(target + ".old")) Directory.Move(target + ".old", target);
                if (new DirectoryInfo(target).LinkTarget is not null) return KeyOnly(target, key());
                if (!Directory.Exists(target))
                {
                    Install(bundledDir, addOnsDir, key(), appFiles);
                    return AddonSyncResult.Installed;
                }
                if (!File.Exists(Path.Combine(target, toc)))
                {
                    Install(bundledDir, addOnsDir, key(), appFiles);
                    return AddonSyncResult.UpdatedFiles;
                }
                if (Version(Path.Combine(target, toc)) is not { } installed || installed > bundled) return KeyOnly(target, key());
                var sameFiles = Fingerprint(target, toc, appFiles) == Fingerprint(bundledDir, toc, appFiles);
                if (installed == bundled && sameFiles) return KeyOnly(target, key());
                var result = !TocLines(Path.Combine(target, toc)).SequenceEqual(TocLines(Path.Combine(bundledDir, toc)), StringComparer.OrdinalIgnoreCase) ? AddonSyncResult.UpdatedFiles
                    : !sameFiles ? AddonSyncResult.Updated
                    : AddonSyncResult.Synced;
                var newKey = key();
                var keyResult = KeyResult(target, newKey);
                Install(bundledDir, addOnsDir, newKey, appFiles);
                return keyResult > result ? keyResult : result;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or CryptographicException)
        {
            return AddonSyncResult.Failed;
        }
    }

    public static void Install(string bundledDir, string addOnsDir, ChannelKey? key, IReadOnlyCollection<string>? appFiles = null)
    {
        if (!Directory.Exists(bundledDir)) throw new DirectoryNotFoundException(bundledDir);
        lock (Gate)
        {
            var target = Target(bundledDir, addOnsDir);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            var staging = Free(target + ".new");
            var old = Free(target + ".old");
            var exists = Exists(target);
            try
            {
                Copy(bundledDir, staging);
                if (exists && new DirectoryInfo(target).LinkTarget is null)
                {
                    foreach (var name in (appFiles ?? []).Append(AddonKeyFile.FileName))
                    {
                        if (File.Exists(Path.Combine(target, name))) File.Copy(Path.Combine(target, name), Path.Combine(staging, name), true);
                    }
                }
                if (key is not null) AddonKeyFile.Write(staging, key);
                if (exists) Directory.Move(target, old);
                try
                {
                    Directory.Move(staging, target);
                }
                catch
                {
                    if (exists) Directory.Move(old, target);
                    throw;
                }
            }
            catch
            {
                TryRemove(staging);
                throw;
            }
            TryRemove(old);
        }
    }

    public static bool IsCurrent(string bundledDir, string addOnsDir, IReadOnlyCollection<string>? appFiles = null)
    {
        var target = Target(bundledDir, addOnsDir);
        var toc = Path.GetFileName(target) + ".toc";
        if (new DirectoryInfo(target).LinkTarget is not null) return true;
        if (!File.Exists(Path.Combine(target, toc))) return false;
        if (Version(Path.Combine(target, toc)) is not { } installed) return true;
        var bundled = Version(Path.Combine(bundledDir, toc));
        return installed > bundled || (installed == bundled && Fingerprint(target, toc, appFiles) == Fingerprint(bundledDir, toc, appFiles));
    }

    private static AddonSyncResult KeyOnly(string target, ChannelKey? key)
    {
        var result = KeyResult(target, key);
        if (result != AddonSyncResult.UpToDate) AddonKeyFile.Write(target, key!);
        return result;
    }

    private static AddonSyncResult KeyResult(string target, ChannelKey? key)
    {
        var file = Path.Combine(target, AddonKeyFile.FileName);
        return key is null ? AddonSyncResult.UpToDate
            : !File.Exists(file) ? AddonSyncResult.UpdatedFiles
            : File.ReadAllText(file).Contains($"\"{key.ToHex()}\"", StringComparison.Ordinal) ? AddonSyncResult.UpToDate
            : AddonSyncResult.Updated;
    }

    private static string Target(string bundledDir, string addOnsDir)
    {
        if (!IsLocalAddOns(addOnsDir) || !IsLocalAddOns(addOnsDir = Path.GetFullPath(addOnsDir))) throw new ArgumentException("Not an Interface\\AddOns folder", nameof(addOnsDir));
        if (!Directory.Exists(Path.GetDirectoryName(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(addOnsDir))))) throw new DirectoryNotFoundException(addOnsDir);
        return Path.Combine(addOnsDir, Path.GetFileName(Path.TrimEndingDirectorySeparator(bundledDir)));
    }

    private static bool IsLocalAddOns(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Path.IsPathFullyQualified(folder) || folder.StartsWith(@"\\") || folder.StartsWith("//") || folder.Split('\\', '/').Contains("..")) return false;
        var trimmed = Path.TrimEndingDirectorySeparator(folder);
        return string.Equals(Path.GetFileName(trimmed), "AddOns", StringComparison.OrdinalIgnoreCase)
            && string.Equals(Path.GetFileName(Path.GetDirectoryName(trimmed)), "Interface", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> TocLines(string toc) =>
        File.ReadLines(toc).Select(l => l.Trim()).Where(l => l != "" && !IsVersionLine(l) && (!l.StartsWith('#') || l.StartsWith("##"))).Select(l => l.Replace('\\', '/'));

    private static bool IsVersionLine(string line) => line.Trim().StartsWith("## Version:", StringComparison.Ordinal);

    private static string Fingerprint(string dir, string toc, IReadOnlyCollection<string>? appFiles)
    {
        var skip = new HashSet<string>((appFiles ?? []).Append(AddonKeyFile.FileName).Select(f => f.Replace('\\', '/')), StringComparer.OrdinalIgnoreCase);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var path in Directory.GetFiles(dir, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(dir, f).Replace('\\', '/')).Where(p => !skip.Contains(p)).Order(StringComparer.Ordinal))
        {
            var bytes = path == toc
                ? Encoding.UTF8.GetBytes(string.Join('\n', File.ReadLines(Path.Combine(dir, path)).Where(l => !IsVersionLine(l))))
                : File.ReadAllBytes(Path.Combine(dir, path));
            hash.AppendData(Encoding.UTF8.GetBytes(path + "\0"));
            hash.AppendData(BitConverter.GetBytes((long)bytes.Length));
            hash.AppendData(bytes);
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static Version? Version(string toc)
    {
        if (!File.Exists(toc)) return null;
        var line = File.ReadLines(toc).Select(l => l.Trim()).FirstOrDefault(IsVersionLine);
        return line is not null && System.Version.TryParse(line["## Version:".Length..].Trim(), out var version) ? version : null;
    }

    private static bool Exists(string dir) => new DirectoryInfo(dir).LinkTarget is not null || Directory.Exists(dir);

    private static void Remove(string dir)
    {
        if (new DirectoryInfo(dir).LinkTarget is not null) Directory.Delete(dir);
        else if (Directory.Exists(dir)) Directory.Delete(dir, true);
    }

    private static string Free(string dir)
    {
        TryRemove(dir);
        return Exists(dir) ? $"{dir}-{Guid.NewGuid():N}" : dir;
    }

    private static void TryRemove(string dir)
    {
        try
        {
            Remove(dir);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void Copy(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.GetFiles(from)) File.Copy(file, Path.Combine(to, Path.GetFileName(file)), true);
        foreach (var dir in Directory.GetDirectories(from)) Copy(dir, Path.Combine(to, Path.GetFileName(dir)));
    }
}
