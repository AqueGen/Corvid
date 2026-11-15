// Copyright (c) 2026 AqueGen. All rights reserved. See LICENSE.
namespace Corvid.Tests;

public sealed class WowFolderTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("corvid-wow-").FullName;

    public void Dispose() => Directory.Delete(_dir, true);

    private string Flavour(string name)
    {
        var folder = Path.Combine(_dir, "World of Warcraft", name);
        Directory.CreateDirectory(Path.Combine(folder, "Interface", "AddOns"));
        return folder;
    }

    private string Root => Path.Combine(_dir, "World of Warcraft");

    [Theory]
    [InlineData(@"C:\Wow\_retail_", @"C:\Wow")]
    [InlineData(@"C:\Wow\_forever_\Interface\AddOns\", @"C:\Wow")]
    [InlineData(@"C:\Wow\_retail_\Interface", @"C:\Wow")]
    [InlineData(@"C:\Wow", @"C:\Wow")]
    [InlineData(@"C:\Wow\", @"C:\Wow")]
    [InlineData(@"Wow\_retail_", null)]
    [InlineData(@"\\server\share\_retail_", null)]
    [InlineData(null, null)]
    public void RootOf_strips_client_interface_and_addons(string? path, string? expected) => Assert.Equal(expected, WowFolder.RootOf(path));

    [Fact]
    public void Root_needs_a_client()
    {
        Assert.Null(WowFolder.Root(_dir));
        Flavour("_retail_");
        Assert.Equal(Root, WowFolder.Root(Path.Combine(Root, "_retail_", "Interface", "AddOns")));
        Assert.Equal(Root, WowFolder.Root(Root));
    }

    [Fact]
    public void Clients_lists_allowed_clients_only()
    {
        var retail = Flavour("_retail_");
        var forever = Flavour("_forever_");
        Flavour("_classic_");
        Directory.CreateDirectory(Path.Combine(Root, "_cache_"));

        Assert.Equal([forever, retail], WowFolder.Clients(Root));
    }

    [Fact]
    public void Clients_of_custom_install_is_itself()
    {
        var custom = Path.Combine(_dir, "WoW");
        Directory.CreateDirectory(custom);
        File.WriteAllText(Path.Combine(custom, "Wow.exe"), "");

        Assert.Equal([custom], WowFolder.Clients(custom));
        Assert.Equal(custom, WowFolder.Root(custom));
    }

    [Fact]
    public void Clients_ignore_a_stray_interface_in_the_root()
    {
        var retail = Flavour("_retail_");
        var forever = Flavour("_forever_");
        Directory.CreateDirectory(Path.Combine(Root, "Interface", "AddOns"));

        Assert.Equal([forever, retail], WowFolder.Clients(Root));
    }

    [Fact]
    public void Root_with_only_a_stray_interface_has_no_clients()
    {
        Directory.CreateDirectory(Path.Combine(Root, "Interface"));

        Assert.Empty(WowFolder.Clients(Root));
    }

    [Fact]
    public void Running_client_is_added_only_when_allowed_or_elsewhere()
    {
        var retail = Flavour("_retail_");
        var classic = Flavour("_classic_");
        var elsewhere = Path.Combine(_dir, "Other", "WoW");
        Directory.CreateDirectory(Path.Combine(elsewhere, "Interface"));

        Assert.Equal([retail], WowFolder.Clients(Root, classic));
        Assert.Equal([elsewhere, retail], WowFolder.Clients(Root, elsewhere));
        Assert.Equal([retail], WowFolder.Clients(null, retail));
        Assert.Equal([retail], WowFolder.Clients(Root, null));
    }

    [Fact]
    public void Client_folder_on_a_drive_root_stays_itself() => Assert.Equal(@"D:\_retail_", WowFolder.RootOf(@"D:\_retail_\Interface\AddOns"));

    [Theory]
    [InlineData("_retail_", true)]
    [InlineData("wow", true)]
    [InlineData("world of warcraft", true)]
    [InlineData("..", false)]
    [InlineData(".", false)]
    [InlineData(@"..\x", false)]
    [InlineData("a/b", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Client_names_are_one_safe_segment(string? name, bool expected) => Assert.Equal(expected, WowFolder.IsClientNameSafe(name));

    [Fact]
    public void Clients_of_missing_root_is_empty() => Assert.Empty(WowFolder.Clients(Path.Combine(_dir, "missing")));

    [Theory]
    [InlineData(@"G:\Games\World of Warcraft\_retail_", "Retail")]
    [InlineData(@"G:\Games\World of Warcraft\_forever_\", "Forever")]
    [InlineData(@"D:\WoW\_PTR_", "PTR")]
    [InlineData(@"D:\WoW\_beta_", "Beta")]
    [InlineData(@"D:\WoW\_xptr_", "_xptr_")]
    [InlineData(@"D:\Games\WoW", "WoW")]
    public void Client_label_names_known_clients(string folder, string expected) => Assert.Equal(expected, WowFolder.ClientLabel(folder));

    [Fact]
    public void Client_name_is_the_lowercase_folder_name() => Assert.Equal("_forever_", WowFolder.ClientName(@"C:\Wow\_Forever_\"));

    [Fact]
    public void Enabled_drops_disabled_names()
    {
        string[] clients = [@"C:\Wow\_retail_", @"C:\Wow\_forever_", @"C:\Wow\_retail_"];

        Assert.Equal([@"C:\Wow\_retail_"], WowFolder.Enabled(clients, ["_FOREVER_"]));
    }

    [Fact]
    public void Retail_folder_needs_interface_or_wow_exe()
    {
        var retail = Path.Combine(_dir, "retail");
        Directory.CreateDirectory(retail);
        Assert.False(WowFolder.IsRetail(retail));

        File.WriteAllText(Path.Combine(retail, "Wow.exe"), "");
        Assert.True(WowFolder.IsRetail(retail));

        File.Delete(Path.Combine(retail, "Wow.exe"));
        Directory.CreateDirectory(Path.Combine(retail, "Interface"));
        Assert.True(WowFolder.IsRetail(retail));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(@"\\server\share\Wow")]
    [InlineData("//server/share/Wow")]
    [InlineData(@"Wow\_retail_")]
    [InlineData(@"D:\Games\..\Wow")]
    [InlineData(@"D:\")]
    public void Unsafe_folder_is_not_local(string? folder) => Assert.False(WowFolder.IsSafeLocal(folder));

    [Fact]
    public void Safe_folder_is_local() => Assert.True(WowFolder.IsSafeLocal(@"D:\Games\World of Warcraft\_retail_"));

    [Fact]
    public void Unsafe_folder_is_not_retail_even_with_interface()
    {
        var retail = Flavour("_retail_");

        Assert.False(WowFolder.IsRetail(Path.Combine(retail, "..", "_retail_")));
    }

    [Fact]
    public void Picked_root_resolves_to_retail()
    {
        Flavour("_ptr_");
        var retail = Flavour("_retail_");
        var root = Path.GetDirectoryName(retail)!;

        Assert.Equal(retail, WowFolder.FromPicked(root));
        Assert.Equal(retail, WowFolder.FromPicked(root + Path.DirectorySeparatorChar));
    }

    [Fact]
    public void Picked_retail_stays()
    {
        var retail = Flavour("_retail_");

        Assert.Equal(retail, WowFolder.FromPicked(retail));
        Assert.Equal(retail, WowFolder.FromPicked(retail + Path.DirectorySeparatorChar));
        Assert.Equal(retail, WowFolder.FromPicked(retail.ToUpperInvariant()), ignoreCase: true);
    }

    [Fact]
    public void Picked_interface_or_addons_resolves_to_flavour()
    {
        var retail = Flavour("_retail_");

        Assert.Equal(retail, WowFolder.FromPicked(Path.Combine(retail, "Interface")));
        Assert.Equal(retail, WowFolder.FromPicked(Path.Combine(retail, "Interface", "AddOns") + Path.DirectorySeparatorChar));
        Assert.Equal(retail, WowFolder.FromPicked(Path.Combine(retail, "interface", "addons")), ignoreCase: true);
    }

    [Fact]
    public void Picked_other_flavour_is_kept_and_found_from_the_root()
    {
        var ptr = Flavour("_ptr_");

        Assert.Equal(ptr, WowFolder.FromPicked(ptr));
        Assert.Equal(ptr, WowFolder.FromPicked(Path.Combine(ptr, "Interface", "AddOns")));
        Assert.Equal(ptr, WowFolder.FromPicked(Path.GetDirectoryName(ptr)!));
    }

    [Fact]
    public void Picked_random_or_missing_folder_is_rejected()
    {
        Flavour("_retail_");
        var random = Path.Combine(_dir, "Documents");
        Directory.CreateDirectory(Path.Combine(random, "Interface", "AddOns"));

        Assert.Null(WowFolder.FromPicked(random));
        Assert.Null(WowFolder.FromPicked(Path.Combine(random, "Interface")));
        Assert.Null(WowFolder.FromPicked(Path.Combine(_dir, "missing")));
        Assert.Null(WowFolder.FromPicked(Path.Combine(_dir, "missing", "_retail_")));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(@"\\server\share\Wow")]
    [InlineData(@"Wow\_retail_")]
    public void Unusable_pick_resolves_to_nothing(string? picked) => Assert.Null(WowFolder.FromPicked(picked));

    [Fact]
    public void Running_exe_path_gives_the_flavour_folder()
    {
        var ptr = Flavour("_ptr_");

        Assert.Equal(ptr, WowFolder.FromExePaths([Path.Combine(_dir, "missing", "WowT.exe"), Path.Combine(ptr, "WowT.exe")]));
        Assert.Null(WowFolder.FromExePaths([Path.Combine(_dir, "missing", "Wow.exe")]));
        Assert.Null(WowFolder.FromExePaths([]));
    }

    [Fact]
    public void Flavours_lists_every_client_folder_under_the_root()
    {
        var retail = Flavour("_retail_");
        var forever = Flavour("_forever_");
        var root = Path.GetDirectoryName(retail)!;

        foreach (var pick in new[] { root, retail, Path.Combine(forever, "Interface", "AddOns") })
            Assert.Equal([forever, retail], WowFolder.Flavours(pick));
    }

    [Fact]
    public void Flavours_skips_classic_clients()
    {
        var retail = Flavour("_retail_");
        var classic = Flavour("_classic_");
        Flavour("_classic_era_");
        Flavour("_anniversary_");

        Assert.Equal([retail], WowFolder.Flavours(Path.GetDirectoryName(retail)!));
        Assert.Equal([retail], WowFolder.Flavours(classic));
    }

    [Fact]
    public void Flavours_skips_sibling_without_interface()
    {
        var retail = Flavour("_retail_");
        Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(retail)!, "_cache_"));

        Assert.Equal([retail], WowFolder.Flavours(retail));
    }

    [Fact]
    public void Flavours_of_non_standard_folder_is_that_folder()
    {
        var wow = Path.Combine(_dir, "WoW");
        Directory.CreateDirectory(Path.Combine(wow, "Interface", "AddOns"));
        File.WriteAllText(Path.Combine(wow, "Wow.exe"), "");

        Assert.Equal([wow], WowFolder.Flavours(wow));
        Assert.Equal([wow], WowFolder.Flavours(Path.Combine(wow, "Interface", "AddOns")));
    }

    [Fact]
    public void Flavours_of_non_standard_folder_ignores_slash_style()
    {
        var wow = Path.Combine(_dir, "WoW");
        Directory.CreateDirectory(wow);
        File.WriteAllText(Path.Combine(wow, "Wow.exe"), "");

        Assert.Equal([wow], WowFolder.Flavours(wow.Replace('\\', '/'), wow));
    }

    [Fact]
    public void Flavours_of_folder_with_only_interface_is_empty()
    {
        var random = Path.Combine(_dir, "Documents");
        Directory.CreateDirectory(Path.Combine(random, "Interface", "AddOns"));

        Assert.Empty(WowFolder.Flavours(random));
    }

    [Fact]
    public void Flavours_unions_two_roots()
    {
        var retail = Path.Combine(_dir, "A", "_retail_");
        var forever = Path.Combine(_dir, "B", "_forever_");
        Directory.CreateDirectory(Path.Combine(retail, "Interface", "AddOns"));
        Directory.CreateDirectory(Path.Combine(forever, "Interface", "AddOns"));

        Assert.Equal(2, WowFolder.Flavours(retail, forever, retail).Count);
    }

    [Fact]
    public void Flavours_of_nothing_is_empty() => Assert.Empty(WowFolder.Flavours(null, "", @"\\server\share\_retail_"));

    [Fact]
    public void Flavours_of_pick_with_a_nul_char_is_empty() => Assert.Empty(WowFolder.Flavours("C:\\Wo\0W"));

    [Fact]
    public void Addons_folder_sits_under_the_wow_folder() =>
        Assert.Equal(@"D:\Wow\_retail_\Interface\AddOns", WowFolder.AddOns(@"D:\Wow\_retail_"));
}
