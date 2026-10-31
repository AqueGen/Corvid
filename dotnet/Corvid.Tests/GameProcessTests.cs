// Copyright (c) 2026 AqueGen. All rights reserved. See LICENSE.
namespace Corvid.Tests;

public sealed class GameProcessTests
{
    private const int OwnPid = 100;

    [Fact]
    public void Default_list_covers_retail_ptr_and_beta() => Assert.Equal(["Wow", "WowT", "WowB"], GameProcess.Default);

    [Fact]
    public void Wow_process_counts_as_game_in_any_case() => Assert.True(ForegroundTracker.IsGame("wow", false, GameProcess.Default));

    [Fact]
    public void Browser_is_not_the_game()
    {
        var game = ForegroundTracker.IsGame("chrome", false, GameProcess.Default);

        Assert.False(game);
        Assert.False(ForegroundTracker.IsFocused(game, 300, OwnPid));
    }

    [Fact]
    public void Own_process_counts_as_focused_but_not_as_the_game()
    {
        var game = ForegroundTracker.IsGame("PartySignal", false, GameProcess.Default);

        Assert.False(game);
        Assert.True(ForegroundTracker.IsFocused(game, OwnPid, OwnPid));
    }

    [Fact]
    public void Any_foreground_always_counts()
    {
        var game = ForegroundTracker.IsGame(null, true, GameProcess.Default);

        Assert.True(game);
        Assert.True(ForegroundTracker.IsFocused(game, 0, OwnPid));
    }

    [Fact]
    public void No_process_has_no_name() => Assert.Null(GameProcess.ProcessName(0));

    [Fact]
    public void Own_process_has_its_name() =>
        Assert.Equal(System.Diagnostics.Process.GetCurrentProcess().ProcessName, GameProcess.ProcessName(Environment.ProcessId));

    [Fact]
    public void Missing_process_is_not_running() => Assert.False(GameProcess.IsRunning(["corvid-no-such-process-" + Guid.NewGuid().ToString("N")]));
}
