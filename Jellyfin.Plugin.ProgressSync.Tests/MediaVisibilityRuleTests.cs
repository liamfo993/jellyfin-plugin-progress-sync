using System;
using Jellyfin.Plugin.ProgressSync.Models;
using Xunit;

namespace Jellyfin.Plugin.ProgressSync.Tests;

public sealed class MediaVisibilityRuleTests
{
    [Fact]
    public void IsHiddenOn_HidesAlwaysWhenNoVisibleWindowIsConfigured()
    {
        var rule = CreateRule();

        Assert.True(rule.IsHiddenOn(new DateOnly(2026, 12, 25)));
        Assert.True(rule.IsHiddenOn(new DateOnly(2026, 6, 25)));
    }

    [Fact]
    public void IsHiddenOn_ShowsInclusiveAnnualVisibleWindowOnly()
    {
        var rule = CreateRule(12, 1, 12, 31);

        Assert.True(rule.IsHiddenOn(new DateOnly(2026, 11, 30)));
        Assert.False(rule.IsHiddenOn(new DateOnly(2026, 12, 1)));
        Assert.False(rule.IsHiddenOn(new DateOnly(2026, 12, 31)));
        Assert.True(rule.IsHiddenOn(new DateOnly(2027, 1, 1)));
        Assert.False(rule.IsHiddenOn(new DateOnly(2027, 12, 25)));
    }

    [Fact]
    public void IsHiddenOn_SupportsVisibleWindowsThatCrossNewYear()
    {
        var rule = CreateRule(12, 20, 1, 5);

        Assert.True(rule.IsHiddenOn(new DateOnly(2026, 12, 19)));
        Assert.False(rule.IsHiddenOn(new DateOnly(2026, 12, 20)));
        Assert.False(rule.IsHiddenOn(new DateOnly(2027, 1, 5)));
        Assert.True(rule.IsHiddenOn(new DateOnly(2027, 1, 6)));
    }

    private static MediaVisibilityRule CreateRule(
        int? fromMonth = null,
        int? fromDay = null,
        int? throughMonth = null,
        int? throughDay = null)
    {
        return new MediaVisibilityRule(
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            fromMonth,
            fromDay,
            throughMonth,
            throughDay,
            DateTime.UtcNow);
    }
}
