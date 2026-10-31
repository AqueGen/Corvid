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
    public void Addons_folder_sits_under_the_wow_folder() =>
        Assert.Equal(@"D:\Wow\_retail_\Interface\AddOns", WowFolder.AddOns(@"D:\Wow\_retail_"));
}
