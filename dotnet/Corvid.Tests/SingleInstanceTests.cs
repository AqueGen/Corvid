// Copyright (c) 2026 AqueGen. All rights reserved. See LICENSE.
namespace Corvid.Tests;

public sealed class SingleInstanceTests
{
    private static readonly string App = "Corvid.Tests." + Guid.NewGuid().ToString("N");

    [Fact]
    public void Second_start_is_refused_and_signals_the_first()
    {
        var dir = Path.Combine(Path.GetTempPath(), "corvid-instance-" + Guid.NewGuid().ToString("N"));
        using var signaled = new ManualResetEventSlim();
        using var first = SingleInstance.Claim(App, dir);
        Assert.NotNull(first);
        first.Listen(signaled.Set);

        Assert.Null(SingleInstance.Claim(App, dir));
        Assert.True(signaled.Wait(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void Data_dirs_differing_only_in_case_and_slashes_share_an_instance()
    {
        var dir = @"C:\Corvid\" + Guid.NewGuid().ToString("N");
        using var first = SingleInstance.Claim(App, dir);

        Assert.NotNull(first);
        Assert.Null(SingleInstance.Claim(App, dir.Replace('\\', '/').ToUpperInvariant()));
    }

    [Fact]
    public void Other_data_dir_runs_its_own_instance()
    {
        using var first = SingleInstance.Claim(App, Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        using var second = SingleInstance.Claim(App, Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));

        Assert.NotNull(first);
        Assert.NotNull(second);
    }

    [Fact]
    public void Released_instance_can_be_claimed_again()
    {
        var dir = Path.Combine(Path.GetTempPath(), "corvid-instance-" + Guid.NewGuid().ToString("N"));
        SingleInstance.Claim(App, dir)!.Dispose();

        using var again = SingleInstance.Claim(App, dir);

        Assert.NotNull(again);
    }
}
