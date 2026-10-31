// Copyright (c) 2026 AqueGen. All rights reserved. See LICENSE.
using System.Diagnostics;
using System.Security.Cryptography;

namespace Corvid.Tests;

public sealed class AddonSyncTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "corvid-addon-" + Guid.NewGuid().ToString("N"));
    private static readonly ChannelKey Key = ChannelKey.Generate();
    private static readonly string[] Layout = ["AppLayout.lua"];

    private string Source => Path.Combine(_dir, "app", "addon", "Link");
    private string AddOns => Path.Combine(_dir, "_retail_", "Interface", "AddOns");
    private string Target => Path.Combine(AddOns, "Link");

    public AddonSyncTests()
    {
        Write(Source, "1.1", ("a.lua", "new a"), ("b.lua", "b"));
        Directory.CreateDirectory(AddOns);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    private static string KeyText(ChannelKey key) => $"local _, ns = ...\nns.corvidKey = \"{key.ToHex()}\"\n";

    private string KeyFile => Path.Combine(Target, AddonKeyFile.FileName);

    private AddonSyncResult Sync(ChannelKey? key = null) => AddonSync.Sync(Source, AddOns, () => key ?? Key, Layout);

    private static void Write(string dir, string version, params (string Name, string Text)[] files)
    {
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "Link.toc"), $"## Title: Link\n# x-release-please-start-version\n## Version: {version}\n\n{string.Join("\n", files.Select(f => f.Name))}\n");
        foreach (var (name, text) in files) File.WriteAllText(Path.Combine(dir, name), text);
    }

    private void Junction(string dev)
    {
        using var mklink = Process.Start(new ProcessStartInfo("cmd.exe", ["/c", "mklink", "/J", Target, dev]) { CreateNoWindow = true, UseShellExecute = false })!;
        mklink.WaitForExit();
        Assert.NotNull(new DirectoryInfo(Target).LinkTarget);
    }

    [Fact]
    public void Missing_addon_is_installed_with_the_key()
    {
        Assert.Equal(AddonSyncResult.Installed, Sync());
        Assert.Equal("new a", File.ReadAllText(Path.Combine(Target, "a.lua")));
        Assert.Equal(KeyText(Key), File.ReadAllText(KeyFile));
    }

    [Fact]
    public void Missing_addons_folder_is_created_inside_the_game_folder()
    {
        Directory.Delete(Path.Combine(_dir, "_retail_", "Interface"), true);

        Assert.Equal(AddonSyncResult.Installed, Sync());
        Assert.True(File.Exists(Path.Combine(Target, "a.lua")));
    }

    [Fact]
    public void Missing_game_folder_fails()
    {
        Directory.Delete(Path.Combine(_dir, "_retail_"), true);

        Assert.Equal(AddonSyncResult.Failed, Sync());
        Assert.False(Directory.Exists(AddOns));
    }

    [Fact]
    public void Same_content_is_up_to_date_and_untouched()
    {
        Sync();
        var written = File.GetLastWriteTimeUtc(Path.Combine(Target, "a.lua")).AddHours(-1);
        File.SetLastWriteTimeUtc(Path.Combine(Target, "a.lua"), written);
        File.SetLastWriteTimeUtc(KeyFile, written);

        Assert.Equal(AddonSyncResult.UpToDate, Sync());
        Assert.Equal(written, File.GetLastWriteTimeUtc(Path.Combine(Target, "a.lua")));
        Assert.Equal(written, File.GetLastWriteTimeUtc(KeyFile));
    }

    [Fact]
    public void Up_to_date_with_a_missing_key_file_writes_it_and_needs_a_restart()
    {
        Write(Target, "1.1", ("a.lua", "new a"), ("b.lua", "b"));

        Assert.Equal(AddonSyncResult.UpdatedFiles, Sync());
        Assert.Equal(KeyText(Key), File.ReadAllText(KeyFile));
    }

    [Fact]
    public void Up_to_date_with_another_key_rewrites_it_and_needs_a_reload()
    {
        Write(Target, "1.1", ("a.lua", "new a"), ("b.lua", "b"));
        File.WriteAllText(KeyFile, KeyText(ChannelKey.Generate()));

        Assert.Equal(AddonSyncResult.Updated, Sync());
        Assert.Equal(KeyText(Key), File.ReadAllText(KeyFile));
    }

    [Fact]
    public void Key_written_by_corvid_is_recognised_on_the_next_sync()
    {
        Sync();

        Assert.Equal(AddonSyncResult.UpToDate, Sync());
    }

    [Fact]
    public void No_key_installs_without_a_key_file_and_keeps_an_installed_one()
    {
        Assert.Equal(AddonSyncResult.Installed, AddonSync.Sync(Source, AddOns, () => null));
        Assert.False(File.Exists(KeyFile));

        File.WriteAllText(KeyFile, KeyText(Key));
        Write(Source, "1.2", ("a.lua", "newer a"), ("b.lua", "b"));
        Assert.Equal(AddonSyncResult.Updated, AddonSync.Sync(Source, AddOns, () => null));
        Assert.Equal(KeyText(Key), File.ReadAllText(KeyFile));
        Assert.Equal(AddonSyncResult.UpToDate, AddonSync.Sync(Source, AddOns, () => null));
    }

    [Fact]
    public void Key_file_in_source_is_overwritten_by_the_given_key()
    {
        File.WriteAllText(Path.Combine(Source, AddonKeyFile.FileName), "stale");

        AddonSync.Install(Source, AddOns, Key);

        Assert.Equal(KeyText(Key), File.ReadAllText(KeyFile));
    }

    [Fact]
    public void Key_file_is_not_part_of_the_fingerprint()
    {
        Write(Target, "1.0", ("a.lua", "new a"), ("b.lua", "b"));
        File.WriteAllText(KeyFile, KeyText(Key));
        File.WriteAllText(Path.Combine(Source, AddonKeyFile.FileName), "other");

        Assert.Equal(AddonSyncResult.Synced, Sync());
    }

    [Fact]
    public void Failing_key_provider_is_failed_and_a_later_call_succeeds()
    {
        Write(Target, "1.1", ("a.lua", "new a"), ("b.lua", "b"));
        var calls = 0;
        ChannelKey Provider() => calls++ == 0 ? throw new CryptographicException("x") : Key;

        Assert.Equal(AddonSyncResult.Failed, AddonSync.Sync(Source, AddOns, Provider));
        Assert.Equal(AddonSyncResult.UpdatedFiles, AddonSync.Sync(Source, AddOns, Provider));
        Assert.Equal(KeyText(Key), File.ReadAllText(KeyFile));
    }

    [Fact]
    public void Unexpected_exception_is_not_swallowed()
    {
        Assert.Throws<InvalidOperationException>(() => AddonSync.Sync(Source, AddOns, () => throw new InvalidOperationException()));
    }

    [Fact]
    public void Key_write_failure_leaves_the_old_addon_untouched()
    {
        Write(Target, "1.0", ("a.lua", "old a"), ("b.lua", "b"));
        Directory.CreateDirectory(Path.Combine(Source, AddonKeyFile.FileName));

        Assert.ThrowsAny<Exception>(() => AddonSync.Install(Source, AddOns, Key));
        Assert.Equal("old a", File.ReadAllText(Path.Combine(Target, "a.lua")));
        Assert.False(File.Exists(KeyFile));
        Assert.Equal(["Link"], Directory.GetDirectories(AddOns).Select(Path.GetFileName));
    }

    [Fact]
    public void Linked_addon_folder_gets_only_the_key_file()
    {
        var dev = Path.Combine(_dir, "dev", "Link");
        Write(dev, "1.0", ("a.lua", "dev a"), ("b.lua", "b"));
        Junction(dev);

        Assert.Equal(AddonSyncResult.UpdatedFiles, Sync());
        Assert.Equal(AddonSyncResult.UpToDate, Sync());
        Assert.NotNull(new DirectoryInfo(Target).LinkTarget);
        Assert.Equal(KeyText(Key), File.ReadAllText(Path.Combine(dev, AddonKeyFile.FileName)));
        Assert.Contains("## Version: 1.0", File.ReadAllText(Path.Combine(dev, "Link.toc")));
        Assert.Equal("dev a", File.ReadAllText(Path.Combine(dev, "a.lua")));
        Assert.True(AddonSync.IsCurrent(Source, AddOns, Layout));
        Directory.Delete(Target);
    }

    [Fact]
    public void Changed_content_with_the_same_toc_is_updated()
    {
        Write(Target, "1.0", ("a.lua", "old a"), ("b.lua", "b"));
        File.WriteAllText(KeyFile, KeyText(Key));
        File.WriteAllText(Path.Combine(Target, "stale.txt"), "x");

        Assert.Equal(AddonSyncResult.Updated, Sync());
        Assert.Equal("new a", File.ReadAllText(Path.Combine(Target, "a.lua")));
        Assert.False(File.Exists(Path.Combine(Target, "stale.txt")));
        Assert.Equal(AddonSyncResult.UpToDate, Sync());
    }

    [Fact]
    public void Same_version_with_changed_content_is_replaced()
    {
        Write(Target, "1.1", ("a.lua", "dev a"), ("b.lua", "b"));
        File.WriteAllText(KeyFile, KeyText(Key));

        Assert.Equal(AddonSyncResult.Updated, Sync());
        Assert.Equal("new a", File.ReadAllText(Path.Combine(Target, "a.lua")));
    }

    [Theory]
    [InlineData("1.2")]
    [InlineData("2.0.0")]
    [InlineData("dev")]
    [InlineData("")]
    public void Newer_or_unreadable_installed_version_is_never_replaced(string version)
    {
        Write(Target, version, ("a.lua", "dev a"), ("b.lua", "b"));
        File.WriteAllText(KeyFile, KeyText(Key));

        Assert.Equal(AddonSyncResult.UpToDate, Sync());
        Assert.Equal("dev a", File.ReadAllText(Path.Combine(Target, "a.lua")));
        Assert.True(AddonSync.IsCurrent(Source, AddOns, Layout));
    }

    [Fact]
    public void Versions_compare_as_numbers()
    {
        Write(Source, "1.10.0", ("a.lua", "new a"), ("b.lua", "b"));
        Write(Target, "1.9.3", ("a.lua", "old a"), ("b.lua", "b"));
        File.WriteAllText(KeyFile, KeyText(Key));

        Assert.Equal(AddonSyncResult.Updated, Sync());
    }

    [Fact]
    public void Changed_toc_file_list_needs_a_game_restart()
    {
        Write(Target, "1.0", ("a.lua", "old a"));
        File.WriteAllText(KeyFile, KeyText(Key));

        Assert.Equal(AddonSyncResult.UpdatedFiles, Sync());
        Assert.Equal("b", File.ReadAllText(Path.Combine(Target, "b.lua")));
    }

    [Fact]
    public void Toc_file_list_ignores_case()
    {
        Write(Target, "1.0", ("A.lua", "old a"), ("B.lua", "b"));
        File.WriteAllText(KeyFile, KeyText(Key));

        Assert.Equal(AddonSyncResult.Updated, Sync());
    }

    [Fact]
    public void New_file_pulled_in_by_a_library_manifest_needs_only_a_reload()
    {
        Write(Target, "1.0", ("a.lua", "new a"), ("b.lua", "b"));
        File.WriteAllText(KeyFile, KeyText(Key));
        Directory.CreateDirectory(Path.Combine(Source, "Libs"));
        File.WriteAllText(Path.Combine(Source, "Libs", "Extra.lua"), "-- extra");

        Assert.Equal(AddonSyncResult.Updated, Sync());
        Assert.True(File.Exists(Path.Combine(Target, "Libs", "Extra.lua")));
    }

    [Theory]
    [InlineData("## SavedVariables: LinkDB")]
    [InlineData("## Dependencies: Other")]
    [InlineData("## LoadOnDemand: 1")]
    public void Changed_toc_directive_needs_a_game_restart(string directive)
    {
        Write(Target, "1.0", ("a.lua", "new a"), ("b.lua", "b"));
        File.WriteAllText(KeyFile, KeyText(Key));
        File.AppendAllText(Path.Combine(Source, "Link.toc"), directive + "\n");

        Assert.Equal(AddonSyncResult.UpdatedFiles, Sync());
    }

    [Fact]
    public void Changed_toc_comment_needs_only_a_reload()
    {
        Write(Target, "1.0", ("a.lua", "new a"), ("b.lua", "b"));
        File.WriteAllText(KeyFile, KeyText(Key));
        File.AppendAllText(Path.Combine(Source, "Link.toc"), "# note\n");

        Assert.Equal(AddonSyncResult.Updated, Sync());
    }

    [Fact]
    public void Version_only_change_is_synced()
    {
        Write(Target, "1.0", ("a.lua", "new a"), ("b.lua", "b"));
        File.WriteAllText(KeyFile, KeyText(Key));

        Assert.Equal(AddonSyncResult.Synced, Sync());
        Assert.Contains("## Version: 1.1", File.ReadAllText(Path.Combine(Target, "Link.toc")));
    }

    [Fact]
    public void Folder_that_is_not_interface_addons_fails()
    {
        var other = Path.Combine(_dir, "Other");
        Write(Path.Combine(other, "Link"), "1.0", ("a.lua", "old a"), ("b.lua", "b"));

        Assert.Equal(AddonSyncResult.Failed, AddonSync.Sync(Source, other, () => Key));
        Assert.Equal("old a", File.ReadAllText(Path.Combine(other, "Link", "a.lua")));
    }

    [Fact]
    public void Missing_bundled_addon_fails()
    {
        Write(Target, "1.0", ("a.lua", "old a"), ("b.lua", "b"));

        Assert.Equal(AddonSyncResult.Failed, AddonSync.Sync(Path.Combine(_dir, "missing", "Link"), AddOns, () => Key));
        Assert.True(File.Exists(Path.Combine(Target, "a.lua")));
    }

    [Fact]
    public void Folder_without_a_toc_is_reinstalled_and_needs_a_restart()
    {
        Write(Target, "1.0", ("a.lua", "old a"), ("b.lua", "b"));
        File.Delete(Path.Combine(Target, "Link.toc"));

        Assert.Equal(AddonSyncResult.UpdatedFiles, Sync());
        Assert.Equal("new a", File.ReadAllText(Path.Combine(Target, "a.lua")));
    }

    [Fact]
    public void Failed_copy_keeps_the_old_addon_and_leaves_no_staging_folder()
    {
        Write(Target, "1.0", ("a.lua", "old a"), ("b.lua", "b"));

        using (new FileStream(Path.Combine(Source, "b.lua"), FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.Equal(AddonSyncResult.Failed, Sync());

        Assert.Equal("old a", File.ReadAllText(Path.Combine(Target, "a.lua")));
        Assert.Equal(["Link"], Directory.GetDirectories(AddOns).Select(Path.GetFileName));
    }

    [Fact]
    public void Failed_first_install_leaves_nothing_and_the_next_sync_installs()
    {
        using (new FileStream(Path.Combine(Source, "b.lua"), FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.Equal(AddonSyncResult.Failed, Sync());

        Assert.Empty(Directory.GetDirectories(AddOns));
        Assert.Equal(AddonSyncResult.Installed, Sync());
    }

    [Fact]
    public void Failed_swap_keeps_the_old_addon_and_leaves_no_staging_folder()
    {
        Write(Target, "1.0", ("a.lua", "old a"), ("b.lua", "b"));

        using (new FileStream(Path.Combine(Target, "b.lua"), FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.Equal(AddonSyncResult.Failed, Sync());

        Assert.Equal("old a", File.ReadAllText(Path.Combine(Target, "a.lua")));
        Assert.Equal(["Link"], Directory.GetDirectories(AddOns).Select(Path.GetFileName));
    }

    [Fact]
    public void Leftover_staging_folders_are_replaced()
    {
        Write(Target, "1.0", ("a.lua", "old a"), ("b.lua", "b"));
        File.WriteAllText(KeyFile, KeyText(Key));
        Write(Target + ".new", "0.1", ("stale.lua", "x"));
        Write(Target + ".old", "0.1", ("stale.lua", "x"));

        Assert.Equal(AddonSyncResult.Updated, Sync());
        Assert.False(File.Exists(Path.Combine(Target, "stale.lua")));
        Assert.Equal(["Link"], Directory.GetDirectories(AddOns).Select(Path.GetFileName));
    }

    [Fact]
    public void Interrupted_swap_is_restored_then_updated()
    {
        Write(Target + ".old", "1.0", ("a.lua", "old a"), ("b.lua", "b"));
        File.WriteAllText(Path.Combine(Target + ".old", AddonKeyFile.FileName), KeyText(Key));

        Assert.Equal(AddonSyncResult.Updated, Sync());
        Assert.Equal("new a", File.ReadAllText(Path.Combine(Target, "a.lua")));
        Assert.Equal(["Link"], Directory.GetDirectories(AddOns).Select(Path.GetFileName));
    }

    [Fact]
    public void Undeletable_leftovers_do_not_block_the_update()
    {
        Write(Target, "1.0", ("a.lua", "old a"), ("b.lua", "b"));
        Write(Target + ".new", "0.1", ("stale.lua", "x"));
        Write(Target + ".old", "0.1", ("stale.lua", "x"));

        using (new FileStream(Path.Combine(Target + ".new", "stale.lua"), FileMode.Open, FileAccess.Read, FileShare.None))
        using (new FileStream(Path.Combine(Target + ".old", "stale.lua"), FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.NotEqual(AddonSyncResult.Failed, Sync());

        Assert.Equal("new a", File.ReadAllText(Path.Combine(Target, "a.lua")));
        Assert.False(File.Exists(Path.Combine(Target, "stale.lua")));
    }

    [Fact]
    public void App_owned_file_is_carried_over_and_left_out_of_the_fingerprint()
    {
        File.WriteAllText(Path.Combine(Source, "AppLayout.lua"), "stub");
        Sync();
        Assert.Equal("stub", File.ReadAllText(Path.Combine(Target, "AppLayout.lua")));
        File.WriteAllText(Path.Combine(Target, "AppLayout.lua"), "written by the app");

        Assert.Equal(AddonSyncResult.UpToDate, Sync());
        Assert.True(AddonSync.IsCurrent(Source, AddOns, Layout));

        Write(Source, "1.2", ("a.lua", "newer a"), ("b.lua", "b"));
        Assert.Equal(AddonSyncResult.Updated, Sync());
        Assert.Equal("written by the app", File.ReadAllText(Path.Combine(Target, "AppLayout.lua")));
        Assert.Equal("newer a", File.ReadAllText(Path.Combine(Target, "a.lua")));
    }

    [Fact]
    public void Is_current_follows_the_copy_rules()
    {
        Assert.False(AddonSync.IsCurrent(Source, AddOns, Layout));

        Write(Target, "1.0", ("a.lua", "new a"), ("b.lua", "b"));
        Assert.False(AddonSync.IsCurrent(Source, AddOns, Layout));

        Sync();
        Assert.True(AddonSync.IsCurrent(Source, AddOns, Layout));

        File.WriteAllText(Path.Combine(Target, "a.lua"), "edited");
        Assert.False(AddonSync.IsCurrent(Source, AddOns, Layout));
    }
}
